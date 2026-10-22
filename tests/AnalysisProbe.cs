using System;
using System.Linq;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Headless analysis contract tests and native-protocol scenario driver.</summary>
internal static class AnalysisProbe
{
    /// <summary>Returns a rectangular contour for a small soil/wall test domain.</summary>
    private static XY[] Box(double x,double y,double w,double h)
    { return new[]{new XY(x,y),new XY(x+w,y),new XY(x+w,y+h),new XY(x,y+h)}; }
    /// <summary>Creates drained soil in canonical SI units.</summary>
    private static MaterialData Soil()
    { return new MaterialData("Soil",true,20e6,.3,18000,20000,5000,Math.PI/6,0,0); }
    /// <summary>Creates a two-region model with gravity, installation and surcharge phases.</summary>
    internal static ModelData Demo()
    {
        var soil=Soil();
        var wall=new MaterialData("Concrete",false,30e9,.2,24000,24000,0,0,0,0);
        return new ModelData("AnalysisDemo",new[]{new RegionData("Base",Box(0,-2,4,2),soil),
            new RegionData("Wall",Box(0,0,4,2),wall)},0,
            new[]{new InterfaceData("Contact",new SegmentData(new XY(0,0),new XY(4,0)),"Base","Wall",soil,true,.67)},
            new[]{new LoadData("Surcharge",new SegmentData(new XY(0,2),new XY(4,2)),"Wall",0,-10000)},
            new[]{new PhaseData("Initial","",new[]{"Base"},null,null,false),
                new PhaseData("Install","Initial",new[]{"Base","Wall"},new[]{"Contact"},null,true),
                new PhaseData("Load","Install",new[]{"Base","Wall"},new[]{"Contact"},new[]{"Surcharge"},false)},
            new MeshData(.5,new[]{"Wall"},1));
    }
    /// <summary>Asserts an invariant and produces a readable build log.</summary>
    private static void Check(string name,bool valid)
    { if(!valid) throw new Exception(name); Console.WriteLine("PASS: "+name); }
    /// <summary>Requires invalid analysis input to fail before transfer.</summary>
    private static void Reject(string name,Action action)
    { try { action(); } catch(ArgumentException) { Console.WriteLine("PASS: "+name); return; } throw new Exception("Expected rejection: "+name); }
    /// <summary>Tests topology, units, full activation states and parent ordering.</summary>
    private static void Core()
    {
        var model=Demo(); var a=model.Analysis;
        Check("Analysis phases preserve parent order",String.Join(",",a.Phases.Select(p=>p.Id))=="Initial,Install,Load");
        Check("Loads retain signed SI line forces",a.Loads[0].Command().Contains("\"qy_start\" -10000"));
        Check("Contact copy has manual strength and gap closure",a.Interfaces[0].MaterialCommand().Contains("\"InterfaceStrengthDetermination\" \"Manual\"") &&
            a.Interfaces[0].MaterialCommand().Contains("\"TensileStrength\" 0 \"GapClosure\" True"));
        Check("Contact does not mutate original soil",!Soil().Command().Contains("Rinter"));
        Check("Initial gravity displacement reset is explicit",!a.Phases[0].ResetDisplacements && a.Phases[1].ResetDisplacements && !a.Phases[2].ResetDisplacements);
        Check("Shared boundary recognized",a.Interfaces[0].Segment.OnBoundary(model.Regions[0]) && a.Interfaces[0].Segment.OnBoundary(model.Regions[1]));
        Reject("Zero-length line rejected",()=>new SegmentData(new XY(0,0),new XY(0,0)));
        Reject("Invalid contact factor rejected",()=>new InterfaceData("Bad",a.Interfaces[0].Segment,"Base","Wall",Soil(),true,0));
        Reject("Initial phase cannot reset",()=>new PhaseData("Initial","",new[]{"Base"},null,null,true));
        Reject("Initial phase cannot activate load",()=>new PhaseData("Initial","",new[]{"Base"},null,new[]{"Surcharge"},false));
        Reject("Duplicate active IDs rejected",()=>new PhaseData("X","Initial",new[]{"Base","base"},null,null,false));
        Reject("Analysis requires mesh",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases,null));
        Reject("Unknown refinement region rejected",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases,new MeshData(.5,new[]{"Unknown"},1)));
        Reject("Unknown load host rejected",()=>new AnalysisData(model.Regions,a.Interfaces,
            new[]{new LoadData("Surcharge",a.Loads[0].Segment,"Unknown",0,-1)},a.Phases,a.Mesh));
        Reject("Interior line cannot be a boundary load",()=>new AnalysisData(model.Regions,a.Interfaces,
            new[]{new LoadData("Surcharge",new SegmentData(new XY(1,1),new XY(3,1)),"Wall",0,-1)},a.Phases,a.Mesh));
        Reject("Contact requires a shared boundary",()=>new AnalysisData(model.Regions,
            new[]{new InterfaceData("Contact",a.Loads[0].Segment,"Base","Wall",Soil(),true,.67)},a.Loads,a.Phases,a.Mesh));
        Reject("Contact cannot activate without both regions",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("Install","Initial",new[]{"Base"},new[]{"Contact"},null,false)},a.Mesh));
        Reject("Load cannot activate on inactive host",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("Install","Initial",new[]{"Base"},null,new[]{"Surcharge"},false)},a.Mesh));
        Reject("Cyclic phases rejected",()=>new AnalysisData(model.Regions,a.Interfaces,a.Loads,
            new[]{a.Phases[0],new PhaseData("A","B",new[]{"Base"},null,null,false),new PhaseData("B","A",new[]{"Base"},null,null,false)},a.Mesh));
        var reversed=new AnalysisData(model.Regions,a.Interfaces,a.Loads,a.Phases.Reverse(),a.Mesh);
        Check("Unordered phase inputs sorted",reversed.Phases[0].Id=="Initial" && reversed.Phases[2].Id=="Load");
        Reject("Analysis with contour margin rejected",()=>new ModelData(model.Id,model.Regions,1,a.Interfaces,a.Loads,a.Phases,a.Mesh));
        var changed=new ModelData(model.Id,model.Regions,0,a.Interfaces,a.Loads,a.Phases,new MeshData(.25,null,0));
        Check("Mesh changes invalidate model fingerprint",model.Fingerprint!=changed.Fingerprint);
        Reject("Overlapping contacts rejected",()=>new AnalysisData(model.Regions,new[]{a.Interfaces[0],
            new InterfaceData("Other",a.Interfaces[0].Segment,"Base","Wall",Soil(),false,.67)},a.Loads,a.Phases,a.Mesh));
    }
    /// <summary>Runs core assertions or a build/solve sequence against an isolated server.</summary>
    public static int Main(string[] args)
    {
        if(args.Length==0) { Core(); return 0; }
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var connection=new ConnectionData("127.0.0.1",Int32.Parse(args[0]),Console.ReadLine());
        var build=ModelBuilder.BuildAsync(Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
        CalculationResult calc=null;
        if(build.Success && build.Receipt!=null)
        {
            if(args[2]=="stale")
                ModelBuilder.BuildAsync(Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
            calc=ModelBuilder.CalculateAsync(build.Receipt,connection,1,args[2]=="output",s=>{}).GetAwaiter().GetResult();
        }
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{
            BuildSuccess=build.Success,BuildError=build.Error,HasReceipt=build.Receipt!=null,
            Success=calc!=null&&calc.Success,Error=calc==null?"":calc.Error,
            Status=calc==null?new string[0]:calc.PhaseStatus.ToArray()
        }));
        return 0;
    }
}

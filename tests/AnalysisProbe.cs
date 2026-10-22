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

    /// <summary>Synthetic plate fixture with installation and subsequent deactivation; not an engineering demo.</summary>
    internal static ModelData PlateDemo()
    {
        var m=new PlateMaterialData("PlateConcrete",12e9,160e6,.2,9600,true,0);
        var plates=new[]{new PlateData("Left",new SegmentData(new XY(1,-1),new XY(1,1)),m),
            new PlateData("Right",new SegmentData(new XY(3,-1),new XY(3,1)),m)};
        return new ModelData("AnalysisDemo",Demo().Regions,0,null,null,
            new[]{new PhaseData("Initial","",new[]{"Base","Wall"},null,null,false),
                new PhaseData("Install","Initial",new[]{"Base","Wall"},null,null,true,new[]{"Left","Right"}),
                new PhaseData("Remove","Install",new[]{"Base","Wall"},null,null,false,new[]{"Right"})},
            new MeshData(.5,null,0),plates);
    }
    /// <summary>Checks plate invariants, compatibility and stale-result invalidation before any HTTP calls.</summary>
    private static void PlateCore()
    {
        var model=PlateDemo(); var plate=model.Plates[0]; var material=plate.Material;
        Check("Plate creation uses native plate command",plate.Command()=="plate (1 -1) (1 1)");
        Check("Plate material uses documented plate properties",material.Command().Contains("\"Isotropic\" True") && material.Command().Contains("\"StructNu\"") && !material.Command().Contains("Eref"));
        Check("Plate installation and removal are full snapshots",model.Analysis.Phases[0].Plates.Count==0 && model.Analysis.Phases[1].Plates.Count==2 && model.Analysis.Phases[2].Plates.Count==1);
        Check("Legacy models have no plates",Demo().Plates.Count==0 && Demo().Analysis.Phases.All(p=>p.Plates.Count==0));
        Reject("Plate rejects continuum material absence",()=>new PlateData("X",plate.Segment,null));
        Reject("Plate rejects zero EA",()=>new PlateMaterialData("X",0,1,.2,1,true,0));
        Reject("Plate rejects non-finite EI",()=>new PlateMaterialData("X",1,Double.NaN,.2,1,true,0));
        Reject("Plate rejects negative weight",()=>new PlateMaterialData("X",1,1,.2,-1,true,0));
        Reject("Plate rejects invalid Poisson ratio",()=>new PlateMaterialData("X",1,1,.5,1,true,0));
        Reject("Initial phase cannot activate plates",()=>new PhaseData("Initial","",new[]{"Base"},null,null,false,new[]{"Left"}));
        Reject("Unknown active plate rejected",()=>new ModelData("X",model.Regions,0,null,null,model.Analysis.Phases,model.Analysis.Mesh));
        Reject("Duplicate plate IDs rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,plate}));
        Reject("Overlapping plate geometry rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,new PlateData("Other",plate.Segment,material)}));
        Reject("Out-of-domain plate rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{new PlateData("Other",new SegmentData(new XY(1,0),new XY(5,0)),material)}));
        Reject("Conflicting shared plate materials rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{plate,new PlateData("Other",model.Plates[1].Segment,new PlateMaterialData(material.Id,1,1,.2,1,true,0))}));
        Reject("Continuum and plate material ID collision rejected",()=>new ModelData("X",model.Regions,0,plates:new[]{new PlateData("Other",plate.Segment,new PlateMaterialData("Soil",1,1,.2,1,true,0))}));
        var geometry=new ModelData("X",model.Regions,0,plates:model.Plates);
        Check("Geometry-only plates do not force analysis",geometry.Analysis==null && geometry.Plates.Count==2);
        var changed=new ModelData("X",model.Regions,0,plates:new[]{new PlateData(plate.Id,plate.Segment,new PlateMaterialData("Changed",material.EA*2,material.EI,.2,9600,true,0)),model.Plates[1]});
        Check("Plate stiffness changes invalidate fingerprint",geometry.Fingerprint!=changed.Fingerprint);
        var phaseChanged=new PhaseData("Remove","Install",new[]{"Base","Wall"},null,null,false,new[]{"Left"});
        Check("Plate activation changes invalidate phase signature",model.Analysis.Phases[2].Signature()!=phaseChanged.Signature());
    }

    /// <summary>Runs core assertions or a build/solve sequence against an isolated server.</summary>
    public static int Main(string[] args)
    {
        if(args.Length==0) { Core(); PlateCore(); return 0; }
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var connection=new ConnectionData("127.0.0.1",Int32.Parse(args[0]),Console.ReadLine());
        var build=ModelBuilder.BuildAsync(args[2]=="plates"?PlateDemo():Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
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

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Headless checks and encrypted-build probe using the production core without Rhino.</summary>
internal static class ModelProbe
{
    /// <summary>Creates a representative SI concrete definition.</summary>
    private static MaterialData Concrete()
    { return new MaterialData("Concrete", false, 30e9, .2, 24000, 24000, 0, 0, 0, -1); }

    /// <summary>Creates a counter-clockwise rectangle for geometric test cases.</summary>
    private static XY[] Box(double x, double y, double w, double h)
    { return new[]{new XY(x,y),new XY(x+w,y),new XY(x+w,y+h),new XY(x,y+h)}; }

    /// <summary>Fails the executable when a named invariant is broken.</summary>
    private static void Check(string name, bool value)
    { if(!value) throw new Exception(name); Console.WriteLine("PASS: "+name); }

    /// <summary>Requires invalid input to fail before a server is contacted.</summary>
    private static void Reject(string name, Action action)
    {
        try { action(); } catch(ArgumentException) { Console.WriteLine("PASS: "+name); return; }
        throw new Exception("Expected rejection: "+name);
    }

    /// <summary>Exercises geometry, material validation, culture and model identity.</summary>
    private static void CoreTests()
    {
        var concrete=Concrete();
        Check("Non-porous concrete omits read-only saturated weight",!concrete.Command().Contains("\"gammaSat\"") &&
            concrete.Command().Contains("\"gammaUnsat\" 24000"));
        var a=new RegionData("A",Box(0,0,2,2),concrete);
        var b=new RegionData("B",Box(2,0,2,2),concrete);
        Check("Rectangle area", Math.Abs(a.Area-4)<1e-12);
        var reversed=new RegionData("R",a.Points.Reverse(),concrete);
        Check("Clockwise input normalized",reversed.Area==4 && Geometry2D.SignedArea(reversed.Points)>0);
        Reject("Bow-tie rejected",()=>new RegionData("X",new[]{new XY(0,0),new XY(2,2),new XY(0,2),new XY(2,0)},concrete));
        Reject("Repeated vertex rejected",()=>new RegionData("X",new[]{new XY(0,0),new XY(2,0),new XY(2,2),new XY(0,0)},concrete));
        Reject("Non-finite coordinate rejected",()=>new RegionData("X",new[]{new XY(0,0),new XY(Double.NaN,2),new XY(2,0)},concrete));
        Reject("Collinear polygon rejected",()=>new RegionData("X",new[]{new XY(0,0),new XY(1,0),new XY(2,0)},concrete));
        Check("Shared boundary accepted",new ModelData("Demo",new[]{a,b},.5).Materials.Count==1);
        Check("Point contact accepted",!Geometry2D.Overlaps(a.Points,Box(2,2,1,1)));
        Check("Identical polygons overlap",Geometry2D.Overlaps(a.Points,a.Points));
        Check("Contained polygon overlaps",Geometry2D.Overlaps(a.Points,Box(.5,.5,.5,.5)));
        Reject("Overlapping model rejected",()=>new ModelData("Demo",new[]{a,new RegionData("B",Box(1,1,2,2),concrete)},0));
        Reject("Duplicate region IDs rejected",()=>new ModelData("Demo",new[]{a,new RegionData("a",b.Points,concrete)},0));
        var concave=new RegionData("L",new[]{new XY(0,0),new XY(3,0),new XY(3,1),new XY(1,1),new XY(1,3),new XY(0,3)},concrete);
        Check("Concave triangulation conserves area",Math.Abs(Geometry2D.Triangles(concave.Points).Sum(t=>Geometry2D.SignedArea(t))-5)<1e-12);
        Check("Concave notch is empty",!Geometry2D.Overlaps(concave.Points,Box(1.5,1.5,1,1)));
        Check("Concave arm overlaps",Geometry2D.Overlaps(concave.Points,Box(.5,1.5,1,1)));
        var extra=new RegionData("Extra",new[]{new XY(0,0),new XY(1,0),new XY(2,0),new XY(2,2),new XY(0,2)},concrete);
        Check("Redundant straight vertex accepted",Math.Abs(Geometry2D.Triangles(extra.Points).Sum(t=>Geometry2D.SignedArea(t))-4)<1e-12);
        Reject("Unsafe ID rejected",()=>new ModelData("Bad ID",new[]{a},0));
        Reject("Trailing newline in ID rejected",()=>new ModelData("Demo\n",new[]{a},0));
        Reject("Empty model rejected",()=>new ModelData("Demo",new RegionData[0],0));
        Reject("Negative stiffness rejected",()=>new MaterialData("M",false,-1,.2,24000,24000,0,0,0,0));
        Reject("Incompressible limit rejected",()=>new MaterialData("M",false,1,.5,24000,24000,0,0,0,0));
        Reject("Degrees supplied as radians rejected",()=>new MaterialData("M",true,1,.2,18000,20000,0,30,0,0));
        var different=new MaterialData("Concrete",false,25e9,.2,24000,24000,0,0,0,-1);
        Reject("Conflicting material ID rejected",()=>new ModelData("Demo",new[]{a,new RegionData("B",b.Points,different)},0));
        var model=new ModelData("Demo",new[]{a,b},.5);
        Check("Bounds include explicit margin",model.MinX==-.5 && model.MaxX==4.5 && model.MinY==-.5 && model.MaxY==2.5);
        Check("Changed geometry invalidates fingerprint",model.Fingerprint!=new ModelData("Demo",new[]{a},.5).Fingerprint);
        var previous=Thread.CurrentThread.CurrentCulture;
        try {
            Thread.CurrentThread.CurrentCulture=CultureInfo.GetCultureInfo("de-AT");
            Check("Invariant decimal formatting",Rules.N(1.25)=="1.25" && concrete.Command().Contains("30000000000"));
        } finally { Thread.CurrentThread.CurrentCulture=previous; }
        var soil=new MaterialData("Soil",true,20e6,.3,18000,20000,5000,Math.PI/6,0,0);
        Check("Drained soil retains distinct saturated weight",soil.Command().Contains("\"gammaSat\" 20000"));
        Check("Pa and unit weight retained",soil.Command().Contains("\"Eref\" 20000000") && soil.Command().Contains("\"gammaUnsat\" 18000"));
        Check("Radians converted only at adapter boundary",Math.Abs(soil.Phi*180/Math.PI-30)<1e-12);
        var wall=new RegionData("Wall",new[]{new XY(0,-1),new XY(3,-1),new XY(2,4),new XY(1,4)},concrete);
        var baseSoil=new RegionData("BaseSoil",Box(-6,-4,16,3),soil);
        var front=new RegionData("FrontSoil",new[]{new XY(-6,-1),new XY(0,-1),new XY(.2,0),new XY(-6,0)},soil);
        var back=new RegionData("Backfill",new[]{new XY(3,-1),new XY(10,-1),new XY(10,4),new XY(2,4)},soil);
        Check("Documented wall section partitions without overlap",new ModelData("WallDemo",new[]{wall,baseSoil,front,back},0).Regions.Count==4);
        Check("Documented example areas",Math.Abs(wall.Area-10)<1e-12 && Math.Abs(baseSoil.Area-48)<1e-12 &&
            Math.Abs(front.Area-6.1)<1e-12 && Math.Abs(back.Area-37.5)<1e-12);
        Reject("Nonlocal server rejected",()=>new ConnectionData("example.com",10000,"abcd"));
    }

    /// <summary>Runs local assertions with no arguments or transfers a fixed model to a test server.</summary>
    public static int Main(string[] args)
    {
        if(args.Length==0) { CoreTests(); return 0; }
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        string password=Console.ReadLine();
        var model=new ModelData("Demo",new[]{
            new RegionData("Wall",Box(0,0,2,4),Concrete()),
            new RegionData("Ground",Box(2,0,4,4),new MaterialData("Soil",true,20e6,.3,18000,20000,5000,Math.PI/6,0,0))
        },0);
        var result=ModelBuilder.BuildAsync(model,new ConnectionData("127.0.0.1",Int32.Parse(args[0]),password),args[1],message=>{}).GetAwaiter().GetResult();
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{
            result.Success,result.Error,result.ProjectFile,result.BackupFile,result.Summary
        }));
        return result.Success?0:1;
    }
}

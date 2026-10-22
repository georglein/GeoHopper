using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Headless numerical, provenance and Output orchestration regression driver.</summary>
internal static class ResultsProbe
{
    /// <summary>Asserts one independently meaningful result contract.</summary>
    private static void Check(string name,bool value)
    { if(!value) throw new Exception(name); Console.WriteLine("PASS: "+name); }
    /// <summary>Requires invalid data to fail without silently producing an objective.</summary>
    private static void Reject(string name,Action action)
    { try { action(); } catch(ArgumentException) { Console.WriteLine("PASS: "+name); return; } catch(InvalidOperationException) { Console.WriteLine("PASS: "+name); return; } throw new Exception(name); }
    /// <summary>Constructs a finite wall sample with known displacement.</summary>
    private static PointResultData Sample(string id,double x,double y,double ux,double uy,string snapshot="one")
    { return new PointResultData(new ResultPointData(id,"Wall",new XY(x,y)),snapshot,"Load",ux,uy); }
    /// <summary>Checks signs, exact rotation, polygon membership, scalar decoding and material quantities.</summary>
    private static void Core()
    {
        var a=Sample("A",1,.2,.003,-.004); var b=Sample("B",1,1.8,.003,-.004);
        Check("Rigid translation has zero chord rotation",Math.Abs(ResultMetrics.Rotation(a,b))<1e-14);
        Check("Displacement magnitude uses both signed components",Math.Abs(a.Magnitude-.005)<1e-14);
        var origin=Sample("A",0,0,0,0);
        var turned=Sample("B",0,1,-Math.Sin(.1),Math.Cos(.1)-1);
        Check("Known counterclockwise rotation returns positive radians",Math.Abs(ResultMetrics.Rotation(origin,turned)-.1)<1e-14);
        Check("Chord reversal preserves signed rotation",Math.Abs(ResultMetrics.Rotation(turned,origin)-.1)<1e-14);
        Reject("Mixed snapshots rejected",()=>ResultMetrics.Rotation(a,Sample("B",1,1,0,0,"two")));
        Reject("Coincident points rejected",()=>ResultMetrics.Rotation(a,a));
        Reject("Collapsed deformed chord rejected",()=>ResultMetrics.Rotation(origin,Sample("B",0,1,0,-1)));
        Reject("Nonfinite displacement rejected",()=>Sample("A",0,0,Double.NaN,0));
        var model=AnalysisProbe.Demo();
        Check("Material volume per metre equals section area",ResultMetrics.MaterialArea(model,"Concrete")==8);
        Reject("Missing material rejected",()=>ResultMetrics.MaterialArea(model,"Missing"));
        var receipt=new CalculationReceipt(new BuildReceipt(model,new ConnectionData("localhost",10000,"testpassword"),@"C:\test.p2dx",new[]{"a","b","c"}));
        var point=new ResultPointData("Inside","Wall",new XY(1,1));
        var request=new ResultsRequest(receipt,"",new[]{point});
        Check("Empty phase selects final prepared phase",request.Phase.Id=="Load");
        Reject("Unknown phase rejected",()=>new ResultsRequest(receipt,"Missing",new[]{point}));
        Reject("Inactive region rejected",()=>new ResultsRequest(receipt,"Initial",new[]{point}));
        Reject("Boundary sampling rejected",()=>new ResultsRequest(receipt,"",new[]{new ResultPointData("Edge","Wall",new XY(0,1))}));
        Reject("Outside sampling rejected",()=>new ResultsRequest(receipt,"",new[]{new ResultPointData("Outside","Wall",new XY(5,1))}));
        Reject("Duplicate point IDs rejected",()=>new ResultsRequest(receipt,"",new[]{point,point}));
        Reject("Empty sampling list rejected",()=>new ResultsRequest(receipt,"",new ResultPointData[0]));
        var changed=new ResultsRequest(receipt,"Install",new[]{point});
        Check("Phase changes invalidate read fingerprint",request.Fingerprint!=changed.Fingerprint);
        var json=PlaxisClient.Map("returnedobjects",new[]{PlaxisClient.Map("type","JSON","json",-.125)});
        Check("Native scalar JSON decoded without display-text parsing",ModelBuilder.NumericResult(json)==-.125);
        Check("Legacy numeric returnedvalues decoded",ModelBuilder.NumericResult(PlaxisClient.Map("returnedvalues",new[]{.25}))==.25);
        Reject("Missing result rejected rather than zero",()=>ModelBuilder.NumericResult(PlaxisClient.Map("extrainfo","0")));
        Reject("Text result rejected",()=>ModelBuilder.NumericResult(PlaxisClient.Map("returnedobjects",new[]{PlaxisClient.Map("type","JSON","json","not found")})));
        Reject("Multiple scalar values rejected",()=>ModelBuilder.NumericResult(PlaxisClient.Map("returnedvalues",new[]{1,2})));
        Reject("Nonfinite scalar rejected",()=>ModelBuilder.NumericResult(PlaxisClient.Map("returnedvalues",new[]{Double.PositiveInfinity})));
    }
    /// <summary>Runs core tests or a complete mocked build, solve and result-read workflow.</summary>
    public static int Main(string[] args)
    {
        if(args.Length==0) { Core(); return 0; }
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var connection=new ConnectionData("127.0.0.1",Int32.Parse(args[0]),Console.ReadLine());
        var build=ModelBuilder.BuildAsync(AnalysisProbe.Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
        CalculationResult calc=null; ResultsReadResult read=null;
        if(build.Success)
        {
            calc=ModelBuilder.CalculateAsync(build.Receipt,connection,1,false,s=>{}).GetAwaiter().GetResult();
            if(calc.Success)
            {
                var request=new ResultsRequest(calc.Receipt,"",new[]{new ResultPointData("Base","Wall",new XY(1,.2)),new ResultPointData("Top","Wall",new XY(1,1.8))});
                if(args[2]=="stale") ModelBuilder.BuildAsync(AnalysisProbe.Demo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
                if(args[2]=="recalculate") ModelBuilder.CalculateAsync(build.Receipt,connection,1,false,s=>{}).GetAwaiter().GetResult();
                read=ModelBuilder.ReadResultsAsync(request,connection,s=>{}).GetAwaiter().GetResult();
            }
        }
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{
            Success=read!=null&&read.Success,Error=read==null?(calc==null?build.Error:calc.Error):read.Error,
            Samples=read==null?new object[0]:read.Samples.Select(s=>(object)new{Id=s.Point.Id,s.Ux,s.Uy,s.Magnitude}).ToArray(),
            Rotation=read!=null&&read.Success?ResultMetrics.Rotation(read.Samples[0],read.Samples[1]):(double?)null
        }));
        return 0;
    }
}

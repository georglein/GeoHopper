using System;
using System.Linq;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Checks structural result validation and guarded encrypted Output reads.</summary>
internal static class StructuralProbe
{
    /// <summary>Fails immediately when a result contract is violated.</summary>
    private static void Check(string name,bool ok) { if(!ok) throw new Exception(name); Console.WriteLine("PASS: "+name); }
    /// <summary>Requires a controlled validation failure.</summary>
    private static void Reject(string name,Action action)
    { try { action(); } catch(ArgumentException) { Check(name,true); return; } catch(InvalidOperationException) { Check(name,true); return; } throw new Exception(name); }
    /// <summary>Tests phase selection, array integrity, coordinate matching and display scaling.</summary>
    internal static void Core()
    {
        var model=AnalysisProbe.StrutDemo();
        var receipt=new CalculationReceipt(new BuildReceipt(model,new ConnectionData("localhost",10000,"testpassword"),@"C:\test.p2dx",new[]{"a","b","c","d","e","f"}));
        var request=new StructuralRequest(receipt,"Deepen","left",false);
        Check("Structural IDs resolve canonically",request.Id=="Left"&&request.Phase.Id=="Deepen");
        Reject("Unknown structure rejected",()=>new StructuralRequest(receipt,"Deepen","Unknown",false));
        Reject("Inactive plate rejected",()=>new StructuralRequest(receipt,"Initial","Left",false));
        Reject("Removed anchor rejected",()=>new StructuralRequest(receipt,"Remove","Strut1",true));
        Reject("Unknown structural phase rejected",()=>new StructuralRequest(receipt,"Unknown","Left",false));
        Check("Structural phase invalidates fingerprint",request.Fingerprint!=new StructuralRequest(receipt,"Install","Left",false).Fingerprint);
        var a=new StructuralRow("part",request.Segment,-1,-4,.01,-.02,10,20,30);
        var b=new StructuralRow("part",request.Segment,-1,0,.02,-.03,11,21,31);
        var snapshot=new StructuralSnapshot(request,new[]{b,a,a});
        Check("Native rows sorted and duplicates retained",snapshot.Rows.Count==3&&snapshot.Rows[0].Y==-4);
        var preview=snapshot.Deformed(10);
        Check("Preview deduplicates coincident displacement nodes",preview.Count==1&&preview[0].Count==2);
        Check("Preview scales displacements only",Math.Abs(preview[0][0].X-(-.9))<1e-12&&Math.Abs(preview[0][0].Y-(-4.2))<1e-12);
        Check("Zero preview factor preserves geometry",snapshot.Deformed(0)[0][0].X==-1);
        Reject("Negative preview factor rejected",()=>snapshot.Deformed(-1));
        Reject("Foreign structural coordinates rejected",()=>new StructuralRow("x",request.Segment,1,0,0,0,0,0,0));
        Reject("Nonfinite structural values rejected",()=>new StructuralRow("x",request.Segment,-1,0,0,0,Double.NaN,0,0));
        var clash=new StructuralRow("part",request.Segment,-1,-4,.5,0,0,0,0);
        Reject("Conflicting coincident displacement nodes rejected",()=>new StructuralSnapshot(request,new[]{a,clash,b}).Deformed(1));
        var json=PlaxisClient.Map("returnedobjects",new[]{PlaxisClient.Map("type","JSON","json",new[]{-2.0,3.0})});
        Check("Native numeric array retains signs",ModelBuilder.NumericArray(json).SequenceEqual(new[]{-2.0,3.0}));
        Reject("Empty structural arrays rejected",()=>ModelBuilder.NumericArray(PlaxisClient.Map("returnedvalues",new double[0])));
        Reject("Text structural arrays rejected",()=>ModelBuilder.NumericArray(PlaxisClient.Map("returnedvalues",new[]{"3"})));
        Reject("Nonfinite structural arrays rejected",()=>ModelBuilder.NumericArray(PlaxisClient.Map("returnedvalues",new[]{Double.NaN})));
    }
    /// <summary>Runs production build/solve/read with independent Input and Output namespaces.</summary>
    internal static int Run(string[] args)
    {
        Console.InputEncoding=new System.Text.UTF8Encoding(false);
        var connection=new ConnectionData("127.0.0.1",Int32.Parse(args[0]),Console.ReadLine());
        var build=ModelBuilder.BuildAsync(AnalysisProbe.StrutDemo(),connection,args[1],s=>{}).GetAwaiter().GetResult();
        StructuralReadResult read=null; CalculationResult calc=null;
        if(build.Success)
        {
            calc=ModelBuilder.CalculateAsync(build.Receipt,connection,1,false,s=>{}).GetAwaiter().GetResult();
            if(calc.Success)
            {
                bool anchor=args[2]=="structural-anchor";
                var request=new StructuralRequest(calc.Receipt,"Deepen",anchor?"Strut1":"Left",anchor);
                if(args[2]=="structural-stale") ModelBuilder.CalculateAsync(build.Receipt,connection,1,false,s=>{}).GetAwaiter().GetResult();
                read=ModelBuilder.ReadStructuralAsync(request,connection,s=>{}).GetAwaiter().GetResult();
            }
        }
        Console.WriteLine(new JavaScriptSerializer().Serialize(new{
            Success=read!=null&&read.Success,Error=read==null?(calc==null?build.Error:calc.Error):read.Error,
            Rows=read==null||read.Snapshot==null?new object[0]:read.Snapshot.Rows.Select(x=>(object)new{x.Part,x.X,x.Y,x.Ux,x.Uy,x.N,x.Q,x.M}).ToArray()
        }));
        return 0;
    }
}

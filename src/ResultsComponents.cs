using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Defines an interior continuum measurement point using native Rhino geometry.</summary>
    public sealed class ResultPointComponent : DefinitionComponent
    {
        /// <summary>Creates a data-only editor; no PLAXIS calls occur here.</summary>
        public ResultPointComponent():base("Result Point","ResultPt","Defines an interior point for continuum displacement sampling.","Results") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("ID","ID","Unique measurement ID",GH_ParamAccess.item,"Top");
            p.AddTextParameter("Region","Region","Host region ID; must be active in the selected phase",GH_ParamAccess.item,"Wall");
            p.AddPointParameter("Point","Point","Strictly interior World-XY point in Rhino document units",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Result point","Point","Measurement definition in metres",GH_ParamAccess.item); }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                string id="",region=""; var point=Point3d.Unset;
                if(!d.GetData(0,ref id)||!d.GetData(1,ref region)||!d.GetData(2,ref point)) return;
                var doc=RhinoDoc.ActiveDoc;
                if(doc==null||doc.ModelUnitSystem==UnitSystem.None||doc.ModelUnitSystem==UnitSystem.CustomUnits)
                    throw new ArgumentException("Set explicit Rhino document units before creating result points.");
                double scale=RhinoMath.UnitScale(doc.ModelUnitSystem,UnitSystem.Meters);
                if(!point.IsValid||Math.Abs(point.Z*scale)>Rules.Tolerance) throw new ArgumentException("Result point must be finite and on World XY (Z=0).");
                d.SetData(0,new DefinitionGoo<ResultPointData>(new ResultPointData(id,region,new XY(point.X*scale,point.Y*scale))));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed11"); } }
    }

    /// <summary>Explicit asynchronous Output read bound to a successful calculation and immutable measurement request.</summary>
    public sealed class ReadResultsComponent : DefinitionComponent
    {
        private bool previousTrigger,busy;
        private string status="Ready.",resultKey="";
        private ResultsReadResult result;
        /// <summary>Creates the reader without opening Output or querying results.</summary>
        public ReadResultsComponent():base("Read Results","Read","Opens the selected calculated phase in Output and reads interior continuum displacements.","Results") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Calculated model","Calculated","Successful Calculate receipt",GH_ParamAccess.item);
            p.AddGenericParameter("Connection","Connection","Same Input connection used for Build and Calculate",GH_ParamAccess.item);
            p.AddTextParameter("Phase","Phase","Phase ID; empty selects the last prepared phase",GH_ParamAccess.item,"");
            p.AddGenericParameter("Points","Points","1 to 100 Result Point definitions; flatten this input",GH_ParamAccess.list);
            p.AddBooleanParameter("Read","Read","Button: read once; never calculates",GH_ParamAccess.item,false);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Success","Success","All requested points read successfully",GH_ParamAccess.item);
            p.AddTextParameter("Info","Info","Progress, selected phase and units",GH_ParamAccess.item);
            p.AddGenericParameter("Samples","Samples","Point results with phase and snapshot identity for Chord Rotation",GH_ParamAccess.list);
            p.AddTextParameter("IDs","IDs","Point IDs in the same order as all numerical lists",GH_ParamAccess.list);
            p.AddNumberParameter("Ux [m]","Ux","Global horizontal displacement; right is positive",GH_ParamAccess.list);
            p.AddNumberParameter("Uy [m]","Uy","Global vertical displacement; up is positive",GH_ParamAccess.list);
            p.AddNumberParameter("Magnitude [m]","U","sqrt(Ux^2+Uy^2) at each point; not a whole-region maximum",GH_ParamAccess.list);
            p.AddNumberParameter("Settlement [m]","Settlement","-Uy; downward is positive",GH_ParamAccess.list);
            p.AddTextParameter("Error","Error","Diagnostic; no numeric values returned after failure",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                bool trigger=false; d.GetData(4,ref trigger);
                bool rising=trigger&&!previousTrigger; previousTrigger=trigger;
                var receipt=Read<CalculationReceipt>(d,0); var connection=Read<ConnectionData>(d,1);
                string phase=""; var raw=new List<object>();
                if(!d.GetData(2,ref phase)||!d.GetDataList(3,raw)) return;
                var request=new ResultsRequest(receipt,phase,raw.Select(Unwrap<ResultPointData>));
                string key=Rules.Hash(request.Fingerprint+"|"+connection.Fingerprint);
                if(rising&&!busy)
                {
                    var doc=OnPingDocument(); if(doc==null) return;
                    busy=true; result=null; status="Starting result read...";
                    Task.Run(async delegate
                    {
                        Action<string> progress=message=>RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            if(OnPingDocument()!=doc) return;
                            status=message; doc.ScheduleSolution(20,delegate(GH_Document g){ ExpireSolution(false); });
                        });
                        var completed=await ModelBuilder.ReadResultsAsync(request,connection,progress).ConfigureAwait(false);
                        RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            busy=false; if(OnPingDocument()!=doc) return;
                            result=completed; resultKey=key;
                            doc.ScheduleSolution(1,delegate(GH_Document g){ ExpireSolution(false); });
                        });
                    });
                }
                bool valid=!busy&&result!=null&&key==resultKey;
                d.SetData(0,valid&&result.Success);
                d.SetData(1,busy?status:valid?result.Summary:"Ready. Calculate the model, then press Read.");
                if(valid&&result.Success)
                {
                    d.SetDataList(2,result.Samples.Select(s=>new DefinitionGoo<PointResultData>(s)));
                    d.SetDataList(3,result.Samples.Select(s=>s.Point.Id));
                    d.SetDataList(4,result.Samples.Select(s=>s.Ux)); d.SetDataList(5,result.Samples.Select(s=>s.Uy));
                    d.SetDataList(6,result.Samples.Select(s=>s.Magnitude)); d.SetDataList(7,result.Samples.Select(s=>-s.Uy));
                }
                d.SetData(8,valid?result.Error:"");
                Message=busy?"Reading...":valid&&result.Success?"Read":valid?"Failed":"Ready";
                if(valid&&!String.IsNullOrEmpty(result.Error)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error,result.Error);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed12"); } }
    }

    /// <summary>Measures chord orientation change using two points from one result snapshot and one region.</summary>
    public sealed class ChordRotationComponent : DefinitionComponent
    {
        /// <summary>Creates a pure result postprocessor without contacting PLAXIS.</summary>
        public ChordRotationComponent():base("Chord Rotation","Rotation","Angle change between two sampled points, not a rigid-body fit or maximum rotation.","Results") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Samples","Samples","Samples from one Read Results operation",GH_ParamAccess.list);
            p.AddTextParameter("Point A","A","First measurement ID",GH_ParamAccess.item,"Base");
            p.AddTextParameter("Point B","B","Second measurement ID",GH_ParamAccess.item,"Top");
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Rotation [rad]","Rotation","Signed angle change, counterclockwise positive",GH_ParamAccess.item);
            p.AddNumberParameter("Absolute rotation [rad]","Abs","Absolute chord angle change",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                var raw=new List<object>(); string a="",b="";
                if(!d.GetDataList(0,raw)||!d.GetData(1,ref a)||!d.GetData(2,ref b)) return;
                var samples=raw.Select(Unwrap<PointResultData>).ToList();
                var first=samples.Where(s=>String.Equals(s.Point.Id,a,StringComparison.OrdinalIgnoreCase)).ToList();
                var second=samples.Where(s=>String.Equals(s.Point.Id,b,StringComparison.OrdinalIgnoreCase)).ToList();
                if(first.Count!=1||second.Count!=1) throw new ArgumentException("Each selected point ID must occur exactly once in Samples.");
                double rotation=ResultMetrics.Rotation(first[0],second[0]);
                d.SetData(0,rotation); d.SetData(1,Math.Abs(rotation));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed13"); } }
    }

    /// <summary>Generic material quantity from validated non-overlapping regions of a 2D model.</summary>
    public sealed class MaterialQuantityComponent : DefinitionComponent
    {
        /// <summary>Creates a geometry quantity calculator; no FE calculation is needed.</summary>
        public MaterialQuantityComponent():base("Material Quantity","Quantity","Sums section area by material ID and extrudes it over a specified length.","Results") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model","Model","Validated Model definition used for the analysis",GH_ParamAccess.item);
            p.AddTextParameter("Material","Material","Material ID to sum across all regions",GH_ParamAccess.item,"Concrete");
            p.AddNumberParameter("Length [m]","Length","Out-of-plane extrusion length in metres",GH_ParamAccess.item,1);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Section area [m2]","Area","Sum of all regions using this material",GH_ParamAccess.item);
            p.AddNumberParameter("Volume per length [m3/m]","Volume/m","Volume per metre of out-of-plane length",GH_ParamAccess.item);
            p.AddNumberParameter("Volume [m3]","Volume","Section area times Length; not a cost estimate",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                var model=Read<ModelData>(d,0); string material=""; double length=1;
                if(!d.GetData(1,ref material)||!d.GetData(2,ref length)) return;
                Rules.Range(length,1e-9,1e6,"Length [m]");
                double area=ResultMetrics.MaterialArea(model,material);
                d.SetData(0,area); d.SetData(1,area); d.SetData(2,area*length);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed14"); } }
    }
}

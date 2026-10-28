using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace GeoHopper
{
    /// <summary>Shared button-driven structural reader with input-bound cache invalidation.</summary>
    public abstract class StructuralResultsComponent : DefinitionComponent
    {
        private readonly bool anchor;
        private bool previousTrigger,busy;
        private string status="Ready.",resultKey="";
        private StructuralReadResult result;
        /// <summary>Creates a plate or anchor reader; construction does not contact PLAXIS.</summary>
        protected StructuralResultsComponent(bool isAnchor):base(isAnchor?"Anchor Results":"Plate Results",isAnchor?"AnchorResults":"PlateResults",
            "Reads native nodal structural results from a successfully calculated phase. Explicit Read button required.","Results") { anchor=isAnchor; }
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Calculated model","Calculated","Successful Calculate receipt",GH_ParamAccess.item);
            p.AddGenericParameter("Connection","Connection","Same Input connection used for the calculation",GH_ParamAccess.item);
            p.AddTextParameter("Phase","Phase","Phase ID; empty selects the final phase",GH_ParamAccess.item,"");
            p.AddTextParameter("Structure ID","ID","Exact Plate or Anchor definition ID",GH_ParamAccess.item);
            p.AddBooleanParameter("Read","Read","Button: read once without calculating",GH_ParamAccess.item,false);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Success","Success","Complete read succeeded",GH_ParamAccess.item);
            p.AddTextParameter("Info","Info","Progress, selected structure, phase and units",GH_ParamAccess.item);
            p.AddGenericParameter("Results","Results","Complete immutable structural snapshot",GH_ParamAccess.item);
            p.AddTextParameter("Parts","Parts","Native part name per row; duplicate nodes retained",GH_ParamAccess.list);
            p.AddNumberParameter("X [m]","X","Original global X coordinate",GH_ParamAccess.list);
            p.AddNumberParameter("Y [m]","Y","Original global Y coordinate",GH_ParamAccess.list);
            // Runtime type is available even while GH registers ports in the base constructor.
            if(this is AnchorResultsComponent)
            {
                p.AddNumberParameter("Anchor force [N]","Force","Native force per individual anchor; PLAXIS sign convention",GH_ParamAccess.list);
                p.AddNumberParameter("Force per width [N/m]","Force/m","Native anchor force divided by out-of-plane spacing",GH_ParamAccess.list);
            }
            else
            {
                p.AddNumberParameter("Ux [m]","Ux","Global displacement to the right",GH_ParamAccess.list);
                p.AddNumberParameter("Uy [m]","Uy","Global displacement upwards",GH_ParamAccess.list);
                p.AddNumberParameter("N [N/m]","N","Native axial force per model width",GH_ParamAccess.list);
                p.AddNumberParameter("Q [N/m]","Q","Native shear force per model width",GH_ParamAccess.list);
                p.AddNumberParameter("M [N m/m]","M","Native bending moment per model width",GH_ParamAccess.list);
            }
            p.AddTextParameter("Error","Error","Failure diagnostic; no partial values on failure",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                bool trigger=false; d.GetData(4,ref trigger); bool rising=trigger&&!previousTrigger; previousTrigger=trigger;
                var receipt=Read<CalculationReceipt>(d,0); var connection=Read<ConnectionData>(d,1);
                string phase="",id=""; if(!d.GetData(2,ref phase)||!d.GetData(3,ref id)) return;
                var request=new StructuralRequest(receipt,phase,id,anchor);
                string key=Rules.Hash(request.Fingerprint+"|"+connection.Fingerprint);
                if(rising&&!busy)
                {
                    var doc=OnPingDocument(); if(doc==null) return;
                    busy=true; result=null; status="Starting structural result read...";
                    Task.Run(async delegate
                    {
                        Action<string> progress=message=>RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            if(OnPingDocument()!=doc) return;
                            status=message; doc.ScheduleSolution(20,delegate(GH_Document g){ ExpireSolution(false); });
                        });
                        var completed=await ModelBuilder.ReadStructuralAsync(request,connection,progress).ConfigureAwait(false);
                        RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            busy=false; if(OnPingDocument()!=doc) return;
                            result=completed; resultKey=key;
                            doc.ScheduleSolution(1,delegate(GH_Document g){ ExpireSolution(false); });
                        });
                    });
                }
                bool valid=!busy&&result!=null&&key==resultKey;
                d.SetData(0,valid&&result.Success); d.SetData(1,busy?status:valid?result.Summary:"Ready. Calculate, then press Read.");
                if(valid&&result.Success)
                {
                    var rows=result.Snapshot.Rows;
                    d.SetData(2,new DefinitionGoo<StructuralSnapshot>(result.Snapshot));
                    d.SetDataList(3,rows.Select(x=>x.Part)); d.SetDataList(4,rows.Select(x=>x.X)); d.SetDataList(5,rows.Select(x=>x.Y));
                    if(anchor)
                    {
                        d.SetDataList(6,rows.Select(x=>x.N));
                        d.SetDataList(7,rows.Select(x=>x.N/request.Spacing));
                    }
                    else
                    {
                        d.SetDataList(6,rows.Select(x=>x.Ux)); d.SetDataList(7,rows.Select(x=>x.Uy));
                        d.SetDataList(8,rows.Select(x=>x.N)); d.SetDataList(9,rows.Select(x=>x.Q)); d.SetDataList(10,rows.Select(x=>x.M));
                    }
                }
                d.SetData(anchor?8:11,valid?result.Error:"");
                Message=busy?"Reading...":valid&&result.Success?"Read":valid?"Failed":"Ready";
                if(valid&&!String.IsNullOrEmpty(result.Error)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error,result.Error);
            } catch(Exception ex) { Report(ex); }
        }
    }

    /// <summary>Reads wall displacements and local section forces at native plate nodes.</summary>
    public sealed class PlateResultsComponent : StructuralResultsComponent
    {
        /// <summary>Creates a plate reader.</summary>
        public PlateResultsComponent():base(false) {}
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed20"); } }
    }

    /// <summary>Reads native force per discrete anchor and derives force per width.</summary>
    public sealed class AnchorResultsComponent : StructuralResultsComponent
    {
        /// <summary>Creates an anchor reader.</summary>
        public AnchorResultsComponent():base(true) {}
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed21"); } }
    }

    /// <summary>Displays plate nodal displacements as scaled polylines, preserving separate native parts.</summary>
    public sealed class PlateDeformationPreviewComponent : DefinitionComponent
    {
        /// <summary>Creates a pure display component, without a PLAXIS connection.</summary>
        public PlateDeformationPreviewComponent():base("Plate Deformation Preview","PlatePreview","Draws original and scaled deformed plate polylines in Rhino document units.","Results") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Results","Results","Snapshot from Plate Results",GH_ParamAccess.item);
            p.AddNumberParameter("Display factor","Factor","Non-negative visual exaggeration; does not change result values",GH_ParamAccess.item,100);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Original","Original","Original geometry, one curve per native part",GH_ParamAccess.list);
            p.AddCurveParameter("Deformed","Deformed","Scaled nodal displacement polylines, not interpolated FE shape functions",GH_ParamAccess.list);
            p.AddTextParameter("Info","Info","Structure, phase and display factor",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                var snapshot=Read<StructuralSnapshot>(d,0); double factor=100; if(!d.GetData(1,ref factor)) return;
                var doc=RhinoDoc.ActiveDoc;
                if(doc==null||doc.ModelUnitSystem==UnitSystem.None||doc.ModelUnitSystem==UnitSystem.CustomUnits) throw new ArgumentException("Set explicit Rhino document units.");
                double scale=RhinoMath.UnitScale(UnitSystem.Meters,doc.ModelUnitSystem);
                d.SetDataList(0,snapshot.Deformed(0).Select(points=>new PolylineCurve(points.Select(p=>new Point3d(p.X*scale,p.Y*scale,0)))));
                d.SetDataList(1,snapshot.Deformed(factor).Select(points=>new PolylineCurve(points.Select(p=>new Point3d(p.X*scale,p.Y*scale,0)))));
                d.SetData(2,snapshot.Request.Id+" | "+snapshot.Request.Phase.Id+" | displacement display factor "+Rules.N(factor));
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed22"); } }
    }
}

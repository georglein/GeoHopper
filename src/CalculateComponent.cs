using System;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;

namespace GeoHopper
{
    /// <summary>Explicit asynchronous solver action for the current prepared build receipt.</summary>
    public sealed class CalculateComponent : DefinitionComponent
    {
        private bool previousTrigger,busy;
        private string status="Ready.",resultKey="";
        private CalculationResult result;
        /// <summary>Creates the calculation component without initiating any work.</summary>
        public CalculateComponent():base("Calculate","Calculate","Calculates prepared dry phases one at a time and verifies native result codes.","Analysis") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Built model","Built","Current analysis receipt from Build Model",GH_ParamAccess.item);
            p.AddGenericParameter("Connection","Connection","Same connection used to build the model",GH_ParamAccess.item);
            p.AddBooleanParameter("Calculate","Calculate","Button: calculate the current prepared phases",GH_ParamAccess.item,false);
            p.AddBooleanParameter("Open Output","Output","Open the final phase in PLAXIS Output after success",GH_ParamAccess.item,true);
            p.AddIntegerParameter("Timeout [min]","Timeout","Maximum time per server request; no automatic retry",GH_ParamAccess.item,30);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Success","Success","Every phase completed with native result code 1",GH_ParamAccess.item);
            p.AddTextParameter("Info","Info","Progress and completion summary",GH_ParamAccess.item);
            p.AddTextParameter("Phase status","Phases","Per-phase OK or failure log",GH_ParamAccess.list);
            p.AddTextParameter("Project file","Project","Prepared/calculated project path; inspect phase status for success",GH_ParamAccess.item);
            p.AddTextParameter("Error","Error","English diagnostic; empty on success",GH_ParamAccess.item);
            p.AddGenericParameter("Calculated model","Calculated","Transient successful solve receipt for Read Results",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess d)
        {
            try
            {
                bool trigger=false,output=true; int timeout=30; d.GetData(2,ref trigger);
                bool rising=trigger&&!previousTrigger; previousTrigger=trigger;
                var receipt=Read<BuildReceipt>(d,0); var connection=Read<ConnectionData>(d,1);
                if(!d.GetData(3,ref output)||!d.GetData(4,ref timeout)) return;
                if(timeout<1||timeout>180) throw new ArgumentException("Timeout must be 1 to 180 minutes.");
                string key=receipt.RunId+"|"+connection.Fingerprint;
                if(rising&&!busy)
                {
                    var doc=OnPingDocument(); if(doc==null) return;
                    busy=true; result=null; status="Starting calculation...";
                    Task.Run(async delegate
                    {
                        Action<string> progress=message=>RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            if(OnPingDocument()!=doc) return;
                            status=message; doc.ScheduleSolution(20,delegate(GH_Document g){ ExpireSolution(false); });
                        });
                        var completed=await ModelBuilder.CalculateAsync(receipt,connection,timeout,output,progress).ConfigureAwait(false);
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
                d.SetData(1,busy?status:valid?result.Summary:"Ready. Build first, then press Calculate.");
                if(valid) d.SetDataList(2,result.PhaseStatus);
                d.SetData(3,valid?result.ProjectFile:""); d.SetData(4,valid?result.Error:"");
                if(valid&&result.Success&&result.Receipt!=null) d.SetData(5,new DefinitionGoo<CalculationReceipt>(result.Receipt));
                Message=busy?"Calculating...":valid&&result.Success?"Calculated":valid?"Failed":"Ready";
                if(valid&&!String.IsNullOrEmpty(result.Error)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error,result.Error);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed10"); } }
    }
}

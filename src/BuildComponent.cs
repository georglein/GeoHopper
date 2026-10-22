using System;
using System.IO;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;

namespace GeoHopper
{
    /// <summary>Explicit asynchronous transfer action, with optional mesh and phase preparation.</summary>
    public sealed class BuildComponent : DefinitionComponent
    {
        private bool previousTrigger, busy;
        private string currentStatus="Ready.", resultKey="";
        private BuildResult result;
        /// <summary>Creates the model transfer component with explicit run-folder and button inputs.</summary>
        public BuildComponent():base("Build Model","Build","Rebuilds a marked project. With analysis inputs, prepares mesh and phases; Calculate runs the solver separately.","Model") {}
        /// <inheritdoc/>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model","Model","Validated Model output",GH_ParamAccess.item);
            p.AddGenericParameter("Connection","Connection","Connection settings from GeoHopper Connection",GH_ParamAccess.item);
            p.AddTextParameter("Folder","Folder","Absolute folder for saved models and pre-build backups",GH_ParamAccess.item,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"GeoHopper","Runs"));
            p.AddBooleanParameter("Build","Build","Button: save the marked project and rebuild once",GH_ParamAccess.item,false);
        }
        /// <inheritdoc/>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Success","Success","Last build succeeded for these exact inputs",GH_ParamAccess.item);
            p.AddTextParameter("Info","Info","Progress, semantic object map or stale-result notice",GH_ParamAccess.item);
            p.AddTextParameter("Project file","Project","Completed PLAXIS model path; empty after failure",GH_ParamAccess.item);
            p.AddTextParameter("Backup file","Backup","Pre-build backup path",GH_ParamAccess.item);
            p.AddTextParameter("Error","Error","English failure diagnostic",GH_ParamAccess.item);
            p.AddGenericParameter("Built model","Built","Transient analysis receipt for Calculate; empty for geometry-only models",GH_ParamAccess.item);
        }
        /// <inheritdoc/>
        protected override void SolveInstance(IGH_DataAccess data)
        {
            try
            {
                bool trigger=false; data.GetData(3,ref trigger);
                bool rising=trigger&&!previousTrigger; previousTrigger=trigger;
                var model=Read<ModelData>(data,0); var connection=Read<ConnectionData>(data,1);
                string folder=""; if(!data.GetData(2,ref folder)) return;
                string key=Rules.Hash(model.Fingerprint+"|"+connection.Fingerprint+"|"+folder);
                if(rising&&!busy)
                {
                    var doc=OnPingDocument(); if(doc==null) return;
                    busy=true; result=null; currentStatus="Starting build...";
                    Task.Run(async delegate
                    {
                        Action<string> progress=message=>RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            if(OnPingDocument()!=doc) return;
                            currentStatus=message;
                            doc.ScheduleSolution(20,delegate(GH_Document d) { ExpireSolution(false); });
                        });
                        var completed=await ModelBuilder.BuildAsync(model,connection,folder,progress).ConfigureAwait(false);
                        RhinoApp.InvokeOnUiThread((Action)delegate
                        {
                            busy=false;
                            if(OnPingDocument()!=doc) return;
                            result=completed; resultKey=key;
                            doc.ScheduleSolution(1,delegate(GH_Document d) { ExpireSolution(false); });
                        });
                    });
                }
                bool valid=!busy&&result!=null&&resultKey==key;
                data.SetData(0,valid&&result.Success);
                data.SetData(1,busy?currentStatus:valid?result.Summary:"Ready. Inputs changed or no build yet. Press Build.");
                data.SetData(2,valid?result.ProjectFile:""); data.SetData(3,valid?result.BackupFile:""); data.SetData(4,valid?result.Error:"");
                if(valid&&result.Success&&result.Receipt!=null) data.SetData(5,new DefinitionGoo<BuildReceipt>(result.Receipt));
                Message=busy?"Building...":valid&&result.Success?"Built":valid?"Failed":"Ready";
                if(valid&&!String.IsNullOrEmpty(result.Error)) AddRuntimeMessage(GH_RuntimeMessageLevel.Error,result.Error);
            } catch(Exception ex) { Report(ex); }
        }
        /// <inheritdoc/>
        public override Guid ComponentGuid { get { return new Guid("84a71ed2-4b66-4ce5-972f-c35bab71ed05"); } }
    }
}


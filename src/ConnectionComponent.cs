using System;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino;

namespace GeoHopper
{
    /// <summary>Button-triggered read-only connection probe with non-blocking HTTP and UI-thread updates.</summary>
    public sealed class ConnectionComponent : GH_Component
    {
        private bool previousTrigger;
        private bool busy;
        private string resultFingerprint = "";
        private ReadResult result;

        /// <summary>Creates the connection component. Its GUID and port order remain compatible with the prototype.</summary>
        public ConnectionComponent() : base("GeoHopper Connection", "GH Connect",
            "Reads Project.Title directly from PLAXIS Input using encrypted HTTP. Does not change the model.",
            "GeoHopper", "Connection") { }

        /// <summary>Defines single-item connection settings and an explicit rising-edge trigger.</summary>
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Host", "Host", "Local PLAXIS server address", GH_ParamAccess.item, "localhost");
            p.AddIntegerParameter("Port", "Port", "PLAXIS Input remote scripting port", GH_ParamAccess.item, 10000);
            p.AddTextParameter("Password", "Password", "Remote scripting server password", GH_ParamAccess.item, "");
            p.AddBooleanParameter("Connect", "Connect", "Button: read the project title once", GH_ParamAccess.item, false);
        }

        /// <summary>Returns a result snapshot, human-readable information and a separate error channel.</summary>
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("Connected", "Connected", "Last read succeeded; this is not a continuous connection monitor", GH_ParamAccess.item);
            p.AddTextParameter("Info", "Info", "Project title or current status", GH_ParamAccess.item);
            p.AddTextParameter("Error", "Error", "Diagnostic message with the password removed", GH_ParamAccess.item);
            p.AddGenericParameter("Connection", "Connection", "Transient settings for Build Model; the server is checked again when building", GH_ParamAccess.item);
        }

        /// <summary>
        /// Starts at most one read on a false-to-true transition. Cached results belong to the
        /// exact connection settings that produced them and are hidden when those settings change.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess data)
        {
            string host = "localhost", password = "";
            int port = 10000;
            bool trigger = false;
            if (!data.GetData(0, ref host) || !data.GetData(1, ref port) ||
                !data.GetData(2, ref password) || !data.GetData(3, ref trigger)) return;

            string fingerprint = Fingerprint(host, port, password);
            try { data.SetData(3,new DefinitionGoo<ConnectionData>(new ConnectionData(host,port,password))); }
            catch (ArgumentException) { /* Invalid settings are reported by the existing connection probe. */ }
            bool risingEdge = trigger && !previousTrigger;
            previousTrigger = trigger;
            if (risingEdge && !busy)
            {
                var document = OnPingDocument();
                if (document == null) return;
                busy = true;
                result = null;
                Message = "Reading PLAXIS...";
                Task.Run(async delegate
                {
                    ReadResult received = await PlaxisClient.ReadProjectAsync(host, port, password).ConfigureAwait(false);
                    // Grasshopper state is updated on Rhino's UI thread, never on the HTTP worker.
                    RhinoApp.InvokeOnUiThread((Action)delegate
                    {
                        busy = false;
                        if (OnPingDocument() != document) return;
                        result = received;
                        resultFingerprint = fingerprint;
                        document.ScheduleSolution(1, delegate(GH_Document d) { ExpireSolution(false); });
                    });
                });
            }

            bool current = result != null && resultFingerprint == fingerprint && !busy;
            bool connected = current && result.Connected;
            string info = busy ? "Reading PLAXIS Input..." :
                !current ? "Ready. Press the Connect button." :
                connected ? "Project.Title: " + (result.Title.Length == 0 ? "(empty)" : result.Title) : "Connection test failed.";
            string error = current ? result.Error : "";
            Message = busy ? "Reading..." : connected ? "Read OK" : current ? "Error" : "Ready";
            data.SetData(0, connected);
            data.SetData(1, info);
            data.SetData(2, error);
            if (error.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);
        }

        /// <summary>Identifies the input snapshot without storing a second plaintext password in component state.</summary>
        private static string Fingerprint(string host, int port, string password)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(host + "\n" + port + "\n" + password)));
        }

        /// <summary>Displays the embedded connection icon without changing connection behavior.</summary>
        protected override Bitmap Icon { get { return ComponentIcons.Get(GetType()); } }
        /// <summary>Stable identity used by saved GH definitions; must not change during renaming.</summary>
        public override Guid ComponentGuid { get { return new Guid("5d6e1399-4f93-4f67-a7f7-23e4b7004dd2"); } }
    }

    /// <summary>Plugin metadata displayed by Grasshopper; preserves the original library identity.</summary>
    public sealed class AssemblyInfo : GH_AssemblyInfo
    {
        /// <summary>Public product name.</summary>
        public override string Name { get { return "GeoHopper"; } }
        /// <summary>Current scope and release number.</summary>
        public override string Description { get { return "GeoHopper " + VersionInfo.Number + " - direct PLAXIS connection for Rhino 7."; } }
        /// <summary>Stable Grasshopper library identity.</summary>
        public override Guid Id { get { return new Guid("e3f53ab2-26c7-47ed-ae5c-315c4ea5830c"); } }
        /// <summary>Project attribution.</summary>
        public override string AuthorName { get { return "GeoHopper"; } }
    }
}

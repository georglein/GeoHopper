using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GeoHopper
{
    /// <summary>Transient local connection settings. Never persisted by GeoHopper or shown with a password.</summary>
    internal sealed class ConnectionData
    {
        internal readonly string Host, Password, Fingerprint;
        internal readonly int Port;
        /// <summary>Validates endpoint and credentials before producing a GH connection object.</summary>
        internal ConnectionData(string host, int port, string password)
        {
            Host=(host??"").Trim(); Port=port; Password=password??"";
            IPAddress ip;
            if (!String.Equals(Host,"localhost",StringComparison.OrdinalIgnoreCase) &&
                (!IPAddress.TryParse(Host,out ip) || !IPAddress.IsLoopback(ip)))
                throw new ArgumentException("Only localhost or loopback addresses are supported.");
            if(port<1 || port>65535) throw new ArgumentException("Port must be between 1 and 65535.");
            if(Encoding.UTF8.GetByteCount(Password)<4 || Encoding.UTF8.GetByteCount(Password)>56)
                throw new ArgumentException("Password must contain 4 to 56 UTF-8 bytes.");
            Fingerprint=Rules.Hash(Host+"|"+Port+"|"+Password);
        }
        /// <summary>Does not expose credentials through a GH panel.</summary>
        public override string ToString() { return "PLAXIS Input | "+Host+":"+Port; }
    }

    /// <summary>Completion snapshot with saved paths and semantic object mapping.</summary>
    internal sealed class BuildResult
    {
        internal bool Success;
        internal BuildReceipt Receipt;
        internal string Error="", ProjectFile="", BackupFile="", Summary="";
    }

    /// <summary>Serializes model-writing jobs per local PLAXIS port and preserves managed projects before rebuilding.</summary>
    internal static partial class ModelBuilder
    {
        private static readonly ConcurrentDictionary<int, byte> Active = new ConcurrentDictionary<int, byte>();
        /// <summary>Returns the explicit project title required before GeoHopper is allowed to rebuild a project.</summary>
        internal static string Marker(ModelData model) { return "GeoHopper:"+model.Id; }

        /// <summary>
        /// Builds only a deliberately marked project. Saves its existing state to a new backup,
        /// creates a fresh project, transfers all objects, then saves to a unique run path.
        /// Never retries writes after timeouts because the server may already have executed them.
        /// </summary>
        internal static async Task<BuildResult> BuildAsync(ModelData model, ConnectionData connection,
            string folder, Action<string> progress)
        {
            var result=new BuildResult();
            string stage="Validating build inputs";
            Action<string> report=message=> { stage=message; progress(message); };
            if(!Active.TryAdd(connection.Port,0)) { result.Error="Another GeoHopper build is already using this PLAXIS port."; return result; }
            try
            {
                if(String.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder) || Path.GetPathRoot(folder).Length<3)
                    throw new ArgumentException("Provide an absolute output folder for run files.");
                folder=Path.GetFullPath(folder); Rules.Quote(folder);
                using(var handler=new HttpClientHandler { UseProxy=false, AllowAutoRedirect=false })
                using(var http=new HttpClient(handler))
                {
                    http.BaseAddress=new UriBuilder("http",connection.Host,connection.Port).Uri;
                    http.Timeout=TimeSpan.FromSeconds(60);
                    report("Checking target project...");
                    var project=await Named(http,connection.Password,"Project").ConfigureAwait(false);
                    string title=Convert.ToString(await Property(http,connection.Password,project,"Title").ConfigureAwait(false));
                    if(title!=Marker(model))
                        throw new InvalidOperationException("Target project is not owned by this model. Open a dedicated new PLAXIS Input project and set its Title to "+Marker(model)+". Current project was not modified.");
                    string previousRun; Latest.TryRemove(connection.Port,out previousRun);
                    Directory.CreateDirectory(folder);
                    string run=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
                    string backupFile=Path.Combine(folder,model.Id+"_"+run+"_before.p2dx");
                    string targetFile=Path.Combine(folder,model.Id+"_"+run+".p2dx");
                    report("Saving current managed project...");
                    await Command(http,connection.Password,"save "+Rules.Quote(backupFile)).ConfigureAwait(false);
                    result.BackupFile=backupFile;
                    report("Creating new project...");
                    await PlaxisClient.PostAsync(http,"environment",PlaxisClient.Map("name","new","filename",""),connection.Password).ConfigureAwait(false);
                    report("Configuring project units and model settings...");
                    await Command(http,connection.Password,"setproperties Project \"Title\" "+Rules.Quote(Marker(model))+
                        " \"UnitLength\" \"m\" \"UnitForce\" \"N\" \"UnitTime\" \"s\" \"ModelType\" \"Plane strain\" \"ElementType\" \"15-Noded\"").ConfigureAwait(false);
                    // Explicit SI project units: no hidden Pa-to-kPa or N-to-kN conversion.
                    report("Initializing model contour...");
                    await Command(http,connection.Password,"initializerectangular SoilContour "+Rules.N(model.MinX)+" "+Rules.N(model.MinY)+" "+
                        Rules.N(model.MaxX)+" "+Rules.N(model.MaxY)).ConfigureAwait(false);
                    await Command(http,connection.Password,"gotostructures").ConfigureAwait(false);
                    var materials=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var material in model.Materials)
                    {
                        report("Creating material "+material.Id+"...");
                        var reply=await Command(http,connection.Password,material.Command()).ConfigureAwait(false);
                        materials.Add(material.Id,Created(reply,"SoilMat"));
                        if(model.Analysis!=null && model.Analysis.Phases[0].InitialMethod=="K0 procedure")
                            await Command(http,connection.Password,"setproperties "+materials[material.Id]+" \"K0Determination\" \"Automatic\"").ConfigureAwait(false);
                    }
                    var mapping=new List<string>();
                    var soils=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    var polygons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var region in model.Regions)
                    {
                        report("Creating region "+region.Id+"...");
                        var reply=await Command(http,connection.Password,region.Command()).ConfigureAwait(false);
                        string soil=Created(reply,"Soil"), polygon=Created(reply,"Polygon");
                        soils.Add(region.Id,soil); polygons.Add(region.Id,polygon);
                        await Command(http,connection.Password,"setmaterial "+soil+" "+materials[region.Material.Id]).ConfigureAwait(false);
                        await Command(http,connection.Password,"rename "+soil+" "+Rules.Quote("GH_R_"+region.Id)).ConfigureAwait(false);
                        await Command(http,connection.Password,"rename "+polygon+" "+Rules.Quote("GH_P_"+region.Id)).ConfigureAwait(false);
                        mapping.Add(region.Id+" -> GH_R_"+region.Id+" | "+region.Material.Id);
                    }
                    var plateLines=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    var plateGuids=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    var plateMaterials=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var plate in model.Plates)
                    {
                        if(!plateMaterials.ContainsKey(plate.Material.Id))
                        {
                            report("Creating plate material "+plate.Material.Id+"...");
                            plateMaterials.Add(plate.Material.Id,Created(await Command(http,connection.Password,plate.Material.Command()).ConfigureAwait(false),"PlateMat"));
                        }
                        report("Creating plate "+plate.Id+"...");
                        var plateReply=await Command(http,connection.Password,plate.Command()).ConfigureAwait(false);
                        string feature=Created(plateReply,"Plate");
                        if(model.Analysis!=null && model.Analysis.Mesh.RefinePlates.Contains(plate.Id,StringComparer.OrdinalIgnoreCase))
                            plateLines.Add(plate.Id,Created(plateReply,"Line"));
                        await Command(http,connection.Password,"setmaterial "+feature+" "+plateMaterials[plate.Material.Id]).ConfigureAwait(false);
                        await Command(http,connection.Password,"rename "+feature+" "+Rules.Quote("GH_Plate_"+plate.Id)).ConfigureAwait(false);
                        plateGuids.Add(plate.Id,feature);
                        mapping.Add(plate.Id+" -> GH_Plate_"+plate.Id+" | "+plate.Material.Id);
                    }
                    List<string> phaseGuids=null;
                    if(model.Analysis!=null)
                        phaseGuids=await PrepareAnalysis(http,connection.Password,model,soils,polygons,plateGuids,plateLines,report).ConfigureAwait(false);
                    report("Saving completed model...");
                    await Command(http,connection.Password,"save "+Rules.Quote(targetFile)).ConfigureAwait(false);
                    result.ProjectFile=targetFile;
                    result.Success=true;
                    if(phaseGuids!=null)
                    {
                        result.Receipt=new BuildReceipt(model,connection,targetFile,phaseGuids);
                        Latest[connection.Port]=result.Receipt.RunId;
                    }
                    result.Summary=model+" | SI: m, N, s | Plane strain\n"+String.Join("\n",mapping);
                    if(model.Analysis!=null) result.Summary+="\nMesh and "+phaseGuids.Count+" dry phases prepared. Inspect in PLAXIS, then press Calculate.";
                }
            }
            catch(Exception ex)
            {
                result.Error=(stage+"\n"+(ex is TaskCanceledException ?
                    "PLAXIS request timed out. Inspect the current project before retrying; a command may have completed." :
                    ex is HttpRequestException ? "PLAXIS server is unreachable. Check the Input server and port." : ex.Message))
                    .Replace(connection.Password,"[password]");
                result.ProjectFile="";
                result.Summary="Build failed. No completed model is reported.";
            }
            finally { byte ignored; Active.TryRemove(connection.Port,out ignored); }
            return result;
        }

        /// <summary>Resolves a named server object to its current GUID.</summary>
        private static async Task<string> Named(HttpClient http,string password,string name)
        {
            var response=await PlaxisClient.PostAsync(http,"namedobjects",PlaxisClient.Map("namedobjects",new[]{name}),password).ConfigureAwait(false);
            var obj=PlaxisClient.ObjectAt(PlaxisClient.ObjectAt(response,"namedobjects"),name);
            if(!obj.ContainsKey("success") || !Object.Equals(obj["success"],true))
                throw new InvalidOperationException("PLAXIS object unavailable: "+name+". Open a project in Input.");
            return PlaxisClient.StringAt(PlaxisClient.ObjectAt(obj,"returnedobject"),"guid");
        }
        /// <summary>Reads a required primitive property using the single-owner API signature.</summary>
        private static async Task<object> Property(HttpClient http,string password,string guid,string property)
        {
            var response=await PlaxisClient.PostAsync(http,"propertyvalues",
                PlaxisClient.Map("propertyvalues",PlaxisClient.Map("owner",guid,"propertyname",property,"phaseguid","")),password).ConfigureAwait(false);
            var values=PlaxisClient.ObjectAt(PlaxisClient.ObjectAt(PlaxisClient.ObjectAt(response,"queries"),guid),"properties");
            object value;
            if(!values.TryGetValue(property,out value)) throw new InvalidOperationException("PLAXIS did not return "+property+".");
            var typed=value as Dictionary<string,object>;
            if(typed!=null) { if(!typed.TryGetValue("value",out value)) throw new InvalidOperationException("Unsupported property response."); }
            return value;
        }
        /// <summary>Sends exactly one generated command and checks the PLAXIS-level success flag as well as HTTP.</summary>
        private static async Task<Dictionary<string,object>> Command(HttpClient http,string password,string text)
        {
            var response=await PlaxisClient.PostAsync(http,"commands",PlaxisClient.Map("commands",new[]{text}),password).ConfigureAwait(false);
            object raw;
            if(!response.TryGetValue("commands",out raw)) throw new InvalidOperationException("PLAXIS command response is missing.");
            var list=raw as IList;
            if(list==null || list.Count!=1) throw new InvalidOperationException("Unexpected command response count.");
            // Native command replies wrap success, diagnostics and created objects
            // inside feedback (unlike named-object replies). Reject malformed envelopes.
            var envelope=list[0] as Dictionary<string,object>;
            object feedbackObject;
            if(envelope==null || !envelope.TryGetValue("feedback",out feedbackObject) ||
                !(feedbackObject is Dictionary<string,object>))
                throw new InvalidOperationException("PLAXIS command response is missing valid feedback for "+text.Split(' ')[0]+".");
            var item=(Dictionary<string,object>)feedbackObject;
            object success;
            if(!item.TryGetValue("success",out success) || !(success is bool))
                throw new InvalidOperationException("PLAXIS command feedback is missing a valid success flag for "+text.Split(' ')[0]+".");
            if(!(bool)success)
            {
                string feedback=item.ContainsKey("extrainfo")?Convert.ToString(item["extrainfo"]):"";
                if(String.IsNullOrWhiteSpace(feedback)) feedback="No diagnostic returned.";
                throw new InvalidOperationException("PLAXIS rejected "+text.Split(' ')[0]+": "+feedback);
            }
            return item;
        }
        /// <summary>Selects a created object by type, never by an assumed global collection index.</summary>
        private static string Created(Dictionary<string,object> response,string expectedType)
        {
            object raw;
            if(!response.TryGetValue("returnedobjects",out raw) || !(raw is IList))
                throw new InvalidOperationException("No created objects returned for "+expectedType+".");
            var matches=((IList)raw).Cast<object>().OfType<Dictionary<string,object>>()
                .Where(o=>o.ContainsKey("type") && String.Equals(Convert.ToString(o["type"]),expectedType,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count!=1) throw new InvalidOperationException("Expected one created "+expectedType+"; received "+matches.Count+".");
            string guid=PlaxisClient.StringAt(matches[0],"guid"); Guid checkedGuid;
            if(!Guid.TryParse(guid,out checkedGuid)) throw new InvalidOperationException("Invalid object GUID returned by PLAXIS.");
            return guid;
        }
    }
}


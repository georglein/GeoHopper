using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace GeoHopper
{
    /// <summary>Transient proof of one completed analysis build; invalidated by the next build on its port.</summary>
    internal sealed class BuildReceipt
    {
        internal readonly string RunId,File,ConnectionFingerprint;
        internal readonly ModelData Model;
        internal readonly ReadOnlyCollection<string> PhaseGuids;
        /// <summary>Captures the exact model, saved file, endpoint identity and temporary phase handles.</summary>
        internal BuildReceipt(ModelData model,ConnectionData connection,string file,IEnumerable<string> phases)
        {
            RunId=Guid.NewGuid().ToString("N"); File=file; Model=model;
            ConnectionFingerprint=connection.Fingerprint; PhaseGuids=phases.ToList().AsReadOnly();
        }
        /// <inheritdoc/>
        public override string ToString() { return Model.Id+" | Built analysis | "+Path.GetFileName(File); }
    }

    /// <summary>Calculation status for an exact build receipt, including one status line per requested phase.</summary>
    internal sealed class CalculationResult
    {
        internal bool Success;
        internal string Error="",Summary="",ProjectFile="";
        internal CalculationReceipt Receipt;
        internal readonly List<string> PhaseStatus=new List<string>();
    }

    /// <summary>Analysis extension sharing the builder's per-port lock and command decoder.</summary>
    internal static partial class ModelBuilder
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int,string> Latest=
            new System.Collections.Concurrent.ConcurrentDictionary<int,string>();

        /// <summary>Creates custom contacts and uniform loads, generates the mesh and prepares dry staged phases.</summary>
        private static async Task<List<string>> PrepareAnalysis(HttpClient http,string password,ModelData model,
            Dictionary<string,string> soilGuids,Dictionary<string,string> polygonGuids,Dictionary<string,string> plateGuids,Dictionary<string,string> plateLines,Dictionary<string,string> anchorGuids,Action<string> report)
        {
            var analysis=model.Analysis;
            var interfaces=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var loads=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var contact in analysis.Interfaces)
            {
                report("Creating interface "+contact.Id+"...");
                string material=Created(await Command(http,password,contact.MaterialCommand()).ConfigureAwait(false),"SoilMat");
                var created=await Command(http,password,(contact.Positive?"posinterface ":"neginterface ")+contact.Segment.Command()).ConfigureAwait(false);
                string feature=Created(created,contact.Positive?"PositiveInterface":"NegativeInterface");
                await Command(http,password,"setproperties "+feature+" \"MaterialMode\" \"Custom\"").ConfigureAwait(false);
                await Command(http,password,"setmaterial "+feature+" "+material).ConfigureAwait(false);
                await Command(http,password,"rename "+feature+" "+Rules.Quote("GH_I_"+contact.Id)).ConfigureAwait(false);
                interfaces.Add(contact.Id,feature);
            }
            foreach(var load in analysis.Loads)
            {
                report("Creating line load "+load.Id+"...");
                string feature=Created(await Command(http,password,load.Command()).ConfigureAwait(false),"LineLoad");
                await Command(http,password,"rename "+feature+" "+Rules.Quote("GH_L_"+load.Id)).ConfigureAwait(false);
                loads.Add(load.Id,feature);
            }
            report("Generating finite-element mesh...");
            await Command(http,password,"gotomesh").ConfigureAwait(false);
            for(int pass=0;pass<analysis.Mesh.RefinementPasses;pass++)
            {
                foreach(var id in analysis.Mesh.RefineRegions)
                    await Command(http,password,"refine "+polygonGuids[id]).ConfigureAwait(false);
                foreach(var id in analysis.Mesh.RefinePlates)
                    await Command(http,password,"refine "+plateLines[id]).ConfigureAwait(false);
            }
            await Command(http,password,"meshd "+Rules.N(analysis.Mesh.ElementSize)+" True").ConfigureAwait(false);
            await Command(http,password,"gotostages").ConfigureAwait(false);
            // Staged construction exposes SourceFeature objects with new GUIDs. Resolve
            // semantic names again; geometry-mode handles must not drive phase activation.
            report("Resolving staged construction objects...");
            foreach(var id in soilGuids.Keys.ToList())
                soilGuids[id]=await Named(http,password,"GH_R_"+id).ConfigureAwait(false);
            foreach(var id in interfaces.Keys.ToList())
                interfaces[id]=await Named(http,password,"GH_I_"+id).ConfigureAwait(false);
            foreach(var id in loads.Keys.ToList())
                loads[id]=await Named(http,password,"GH_L_"+id).ConfigureAwait(false);
            foreach(var id in plateGuids.Keys.ToList())
                plateGuids[id]=await Named(http,password,"GH_Plate_"+id).ConfigureAwait(false);
            foreach(var id in anchorGuids.Keys.ToList())
                anchorGuids[id]=await Named(http,password,"GH_Anchor_"+id).ConfigureAwait(false);
            var phases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var result=new List<string>();
            foreach(var phase in analysis.Phases)
            {
                report("Preparing phase "+phase.Id+"...");
                bool initial=phase.Parent=="";
                string guid=initial?await Named(http,password,"InitialPhase").ConfigureAwait(false):
                    Created(await Command(http,password,"phase "+phases[phase.Parent]).ConfigureAwait(false),"Phase");
                string name="GH_Phase_"+phase.Id;
                await Command(http,password,"rename "+guid+" "+Rules.Quote(name)).ConfigureAwait(false);
                await Command(http,password,"setproperties "+guid+" \"Identification\" "+Rules.Quote(phase.Id)+
                    " \"DeformCalcType\" "+Rules.Quote(initial?phase.InitialMethod:"Plastic")+
                    " \"PorePresCalcType\" \"Phreatic\" \"ShouldCalculate\" True").ConfigureAwait(false);
                if(!initial)
                    await Command(http,password,"set "+name+".Deform.ResetDisplacementsToZero "+(phase.ResetDisplacements?"True":"False")).ConfigureAwait(false);
                // Full snapshots avoid activation inherited accidentally from a parent phase.
                report("Preparing phase "+phase.Id+": deactivating soils...");
                await Command(http,password,"deactivate Soils "+guid).ConfigureAwait(false);
                foreach(var item in interfaces)
                {
                    report("Preparing phase "+phase.Id+": deactivating interface "+item.Key+"...");
                    await Command(http,password,"deactivate "+item.Value+" "+guid).ConfigureAwait(false);
                }
                foreach(var item in loads)
                {
                    report("Preparing phase "+phase.Id+": deactivating load "+item.Key+"...");
                    await Command(http,password,"deactivate "+item.Value+" "+guid).ConfigureAwait(false);
                }
                foreach(var item in plateGuids)
                    await Command(http,password,"deactivate "+item.Value+" "+guid).ConfigureAwait(false);
                foreach(var item in anchorGuids)
                {
                    report("Preparing phase "+phase.Id+": deactivating anchor "+item.Key+"...");
                    await Command(http,password,"deactivate "+item.Value+" "+guid).ConfigureAwait(false);
                }
                if(anchorGuids.Count>0)
                {
                    // SourceFeature handles support activation, but staged properties belong
                    // to generated cut elements. Every anchor in this freshly built model is
                    // unprestressed, so apply once to the native cut-element collection.
                    report("Preparing phase "+phase.Id+": disabling prestress adjustment for all anchor elements...");
                    await Command(http,password,"apply NodeToNodeAnchors \"setproperties\" \"AdjustPrestress\" "+guid+" False").ConfigureAwait(false);
                }
                report("Preparing phase "+phase.Id+": applying activation snapshot...");
                foreach(var id in phase.Regions)
                    await Command(http,password,"activate "+soilGuids[id]+" "+guid).ConfigureAwait(false);
                foreach(var id in phase.Plates)
                    await Command(http,password,"activate "+plateGuids[id]+" "+guid).ConfigureAwait(false);
                foreach(var id in phase.Anchors)
                    await Command(http,password,"activate "+anchorGuids[id]+" "+guid).ConfigureAwait(false);
                foreach(var id in phase.Interfaces)
                    await Command(http,password,"activate "+interfaces[id]+" "+guid).ConfigureAwait(false);
                foreach(var id in phase.Loads)
                    await Command(http,password,"activate "+loads[id]+" "+guid).ConfigureAwait(false);
                await Command(http,password,"setwaterdry Soils "+guid).ConfigureAwait(false);
                // Explicit standard plane-strain supports. Margin=0 is required by ModelData.
                await Command(http,password,"setproperties Deformations \"BoundaryXMin\" "+guid+" \"Horizontally fixed\""+
                    " \"BoundaryXMax\" "+guid+" \"Horizontally fixed\" \"BoundaryYMin\" "+guid+" \"Fully fixed\""+
                    " \"BoundaryYMax\" "+guid+" \"Free\"").ConfigureAwait(false);
                phases.Add(phase.Id,guid); result.Add(guid);
            }
            await Command(http,password,"setcurrentphase "+result.Last()).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Calculates one prepared phase at a time and reads native result codes. A receipt from an old
        /// build, another endpoint or another open project is refused before calculation starts.
        /// </summary>
        internal static async Task<CalculationResult> CalculateAsync(BuildReceipt receipt,ConnectionData connection,
            int timeoutMinutes,bool openOutput,Action<string> progress)
        {
            var result=new CalculationResult();
            if(!Active.TryAdd(connection.Port,0)) { result.Error="Another GeoHopper job is using this PLAXIS port."; return result; }
            string stage="Validating calculation receipt";
            try
            {
                string latest;
                if(receipt==null || receipt.Model.Analysis==null || receipt.ConnectionFingerprint!=connection.Fingerprint ||
                    !Latest.TryGetValue(connection.Port,out latest) || latest!=receipt.RunId)
                    throw new InvalidOperationException("Build receipt is stale or belongs to another connection. Build the current model first.");
                if(timeoutMinutes<1 || timeoutMinutes>180) throw new ArgumentException("Timeout must be 1 to 180 minutes per request.");
                string oldSolve;
                Solved.TryRemove(connection.Port,out oldSolve);
                using(var handler=new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false })
                using(var http=new HttpClient(handler))
                {
                    http.BaseAddress=new UriBuilder("http",connection.Host,connection.Port).Uri;
                    http.Timeout=TimeSpan.FromMinutes(timeoutMinutes);
                    string project=await Named(http,connection.Password,"Project").ConfigureAwait(false);
                    string title=Convert.ToString(await Property(http,connection.Password,project,"Title").ConfigureAwait(false));
                    string file=Convert.ToString(await Property(http,connection.Password,project,"Filename").ConfigureAwait(false));
                    if(title!=Marker(receipt.Model) || !String.Equals(Path.GetFullPath(file),Path.GetFullPath(receipt.File),StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The open PLAXIS project is not the saved model from this build receipt. Build again.");
                    result.ProjectFile=receipt.File;
                    for(int i=0;i<receipt.PhaseGuids.Count;i++)
                    {
                        string id=receipt.Model.Analysis.Phases[i].Id, guid=receipt.PhaseGuids[i];
                        stage="Calculating phase "+id; progress(stage+"...");
                        await Command(http,connection.Password,"calculate "+guid).ConfigureAwait(false);
                        object code=await Property(http,connection.Password,guid,"CalculationResult").ConfigureAwait(false);
                        bool ok=Object.Equals(code,1);
                        if(!ok)
                        {
                            string log=Convert.ToString(await Property(http,connection.Password,guid,"LogInfo").ConfigureAwait(false));
                            result.PhaseStatus.Add(id+": FAILED (code "+Convert.ToString(code)+") "+log);
                            await Command(http,connection.Password,"save").ConfigureAwait(false);
                            throw new InvalidOperationException("Phase "+id+" did not finish successfully. "+log);
                        }
                        result.PhaseStatus.Add(id+": OK");
                    }
                    stage="Saving calculation results"; progress(stage+"...");
                    await Command(http,connection.Password,"save").ConfigureAwait(false);
                    result.Success=true; result.Summary="All "+receipt.PhaseGuids.Count+" phases calculated successfully.";
                    result.Receipt=new CalculationReceipt(receipt);
                    Solved[connection.Port]=result.Receipt.SolveId;
                    if(openOutput)
                    {
                        try { await Command(http,connection.Password,"view "+receipt.PhaseGuids.Last()).ConfigureAwait(false); }
                        catch(Exception) { result.Summary+=" Open PLAXIS Output manually; the view command did not complete."; }
                    }
                }
            }
            catch(Exception ex)
            {
                // Do not silently retry: a timed-out solve may still be running in PLAXIS.
                if(ex is TaskCanceledException) { string ignored; Latest.TryRemove(connection.Port,out ignored); }
                result.Error=(stage+"\n"+(ex is TaskCanceledException?
                    "Request timed out. Inspect PLAXIS before proceeding; calculation may still be running. This receipt is invalidated.":
                    ex.Message)).Replace(connection.Password,"[password]");
                result.Summary="Calculation did not complete successfully.";
            }
            finally { byte ignored; Active.TryRemove(connection.Port,out ignored); }
            return result;
        }
    }
}

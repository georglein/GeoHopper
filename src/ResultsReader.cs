using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace GeoHopper
{
    /// <summary>All-or-nothing result read; failure never exposes a partial set of numerical objectives.</summary>
    internal sealed class ResultsReadResult
    {
        internal bool Success;
        internal string Error="",Summary="";
        internal readonly List<PointResultData> Samples=new List<PointResultData>();
    }

    /// <summary>Output access uses the same Input-port job lock as Build and Calculate.</summary>
    internal static partial class ModelBuilder
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int,string> Solved=
            new System.Collections.Concurrent.ConcurrentDictionary<int,string>();

        /// <summary>Decodes exactly one scalar from native JSON objects or legacy returnedvalues; never parses display text.</summary>
        internal static double NumericResult(Dictionary<string,object> feedback)
        {
            object raw,value=null;
            var values=new List<object>();
            if(feedback.TryGetValue("returnedobjects",out raw) && raw is IList)
                foreach(var entry in (IList)raw)
                {
                    var obj=entry as Dictionary<string,object>;
                    if(obj==null || !obj.ContainsKey("type") || !String.Equals(Convert.ToString(obj["type"]),"JSON",StringComparison.OrdinalIgnoreCase) ||
                        !obj.TryGetValue("json",out value)) throw new InvalidOperationException("Unexpected result object in scalar response.");
                    values.Add(value);
                }
            if(values.Count==0 && feedback.TryGetValue("returnedvalues",out raw) && raw is IList)
                values.AddRange(((IList)raw).Cast<object>());
            if(values.Count!=1) throw new InvalidOperationException("Expected exactly one numeric result from PLAXIS Output.");
            value=values[0];
            if(!(value is double || value is float || value is int || value is long || value is decimal))
                throw new InvalidOperationException("PLAXIS returned no numeric value at the requested point. Check active soil and point location.");
            double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);
            if(Double.IsNaN(number)||Double.IsInfinity(number)) throw new InvalidOperationException("PLAXIS returned a non-finite result.");
            return number;
        }

        /// <summary>Verifies project provenance and reads unsmoothed total continuum displacements from the selected final phase step.</summary>
        internal static async Task<ResultsReadResult> ReadResultsAsync(ResultsRequest request,ConnectionData connection,Action<string> report)
        {
            var result=new ResultsReadResult();
            if(!Active.TryAdd(connection.Port,0)) { result.Error="Another GeoHopper job is using this PLAXIS port."; return result; }
            string stage="Validating calculated model";
            try
            {
                var receipt=request.Receipt; var build=receipt.Build;
                string latest,solved;
                if(connection.Fingerprint!=build.ConnectionFingerprint || !Latest.TryGetValue(connection.Port,out latest) || latest!=build.RunId ||
                    !Solved.TryGetValue(connection.Port,out solved) || solved!=receipt.SolveId)
                    throw new InvalidOperationException("Calculated model is stale or belongs to another connection. Build and Calculate the current model first.");
                using(var handler=new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false })
                using(var input=new HttpClient(handler))
                {
                    input.BaseAddress=new UriBuilder("http",connection.Host,connection.Port).Uri;
                    input.Timeout=TimeSpan.FromSeconds(60);
                    string project=await Named(input,connection.Password,"Project").ConfigureAwait(false);
                    string title=Convert.ToString(await Property(input,connection.Password,project,"Title").ConfigureAwait(false));
                    string file=Convert.ToString(await Property(input,connection.Password,project,"Filename").ConfigureAwait(false));
                    if(title!=Marker(build.Model)||!SameFile(file,build.File)) throw new InvalidOperationException("Input project does not match the calculated model.");
                    string phase=build.PhaseGuids[request.PhaseIndex];
                    if(!Object.Equals(await Property(input,connection.Password,phase,"CalculationResult").ConfigureAwait(false),1))
                        throw new InvalidOperationException("Selected Input phase is no longer successfully calculated.");
                    stage="Opening calculated phase in PLAXIS Output"; report(stage+"...");
                    double number=NumericResult(await Command(input,connection.Password,"view "+phase).ConfigureAwait(false));
                    if(number<1 || number>65535 || number!=Math.Truncate(number) || number==connection.Port)
                        throw new InvalidOperationException("PLAXIS view returned an invalid Output server port.");
                    using(var outputHandler=new HttpClientHandler { UseProxy=false,AllowAutoRedirect=false })
                    using(var output=new HttpClient(outputHandler))
                    {
                        output.BaseAddress=new UriBuilder("http",connection.Host,(int)number).Uri;
                        output.Timeout=TimeSpan.FromSeconds(30);
                        stage="Verifying Output project and phase"; report(stage+"...");
                        string info=await Named(output,connection.Password,"GeneralInfo").ConfigureAwait(false);
                        string outputFile=Convert.ToString(await Property(output,connection.Password,info,"Filename").ConfigureAwait(false));
                        string outputTitle=Convert.ToString(await Property(output,connection.Password,info,"Title").ConfigureAwait(false));
                        string units=Convert.ToString(await Property(output,connection.Password,info,"UnitLength").ConfigureAwait(false));
                        if(!SameFile(outputFile,build.File)||outputTitle!=Marker(build.Model)) throw new InvalidOperationException("Output project does not match the calculated model.");
                        if(units!="m") throw new InvalidOperationException("Output length unit must be m; received "+units+".");
                        string phaseName="GH_Phase_"+request.Phase.Id;
                        string outputPhase=await Named(output,connection.Password,phaseName).ConfigureAwait(false);
                        if(Convert.ToString(await Property(output,connection.Password,outputPhase,"Name").ConfigureAwait(false))!=phaseName)
                            throw new InvalidOperationException("Output phase identity does not match the requested phase.");
                        var samples=new List<PointResultData>();
                        string snapshot=Guid.NewGuid().ToString("N");
                        foreach(var point in request.Points)
                        {
                            stage="Reading point "+point.Id; report(stage+"...");
                            string position=" ("+Rules.N(point.Position.X)+" "+Rules.N(point.Position.Y)+") False";
                            double ux=NumericResult(await Command(output,connection.Password,"getsingleresult "+outputPhase+" ResultTypes.Soil.Ux"+position).ConfigureAwait(false));
                            double uy=NumericResult(await Command(output,connection.Password,"getsingleresult "+outputPhase+" ResultTypes.Soil.Uy"+position).ConfigureAwait(false));
                            samples.Add(new PointResultData(point,snapshot,request.Phase.Id,ux,uy));
                        }
                        // Detect an externally switched project before publishing any values.
                        if(!SameFile(Convert.ToString(await Property(output,connection.Password,info,"Filename").ConfigureAwait(false)),build.File))
                            throw new InvalidOperationException("Output project changed while reading results.");
                        result.Samples.AddRange(samples); result.Success=true;
                        result.Summary="Read "+samples.Count+" point(s) in "+request.Phase.Id+". Displacements in m; settlement is -Uy. Values include phase reset history.";
                    }
                }
            }
            catch(Exception ex)
            {
                result.Samples.Clear();
                result.Error=(stage+"\n"+(ex is TaskCanceledException?"Result request timed out. Inspect PLAXIS Output and retry explicitly.":ex.Message)).Replace(connection.Password,"[password]");
                result.Summary="No result values returned.";
            }
            finally { byte ignored; Active.TryRemove(connection.Port,out ignored); }
            return result;
        }

        /// <summary>Compares normalized Windows project paths without accepting empty metadata.</summary>
        private static bool SameFile(string actual,string expected)
        { return !String.IsNullOrWhiteSpace(actual)&&String.Equals(Path.GetFullPath(actual),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase); }
    }
}

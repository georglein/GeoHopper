using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace GeoHopper
{
    /// <summary>Structural Output adapter; all reads share the existing build/solve lock.</summary>
    internal static partial class ModelBuilder
    {
        /// <summary>Decodes finite numerical arrays without parsing display text or substituting missing values.</summary>
        internal static double[] NumericArray(Dictionary<string,object> feedback)
        {
            object raw; IList values=null;
            if(feedback.TryGetValue("returnedobjects",out raw)&&raw is IList&&((IList)raw).Count>0)
            {
                var objects=(IList)raw;
                if(objects.Count!=1) throw new InvalidOperationException("Expected one JSON result array.");
                var entry=objects[0] as Dictionary<string,object>;
                if(entry==null||!entry.ContainsKey("type")||Convert.ToString(entry["type"])!="JSON"||!entry.TryGetValue("json",out raw))
                    throw new InvalidOperationException("Expected a JSON structural result array.");
                values=raw as IList;
            }
            else if(feedback.TryGetValue("returnedvalues",out raw)) values=raw as IList;
            if(values==null||values.Count==0||values.Count>200000) throw new InvalidOperationException("Missing or oversized structural result array.");
            var numbers=new double[values.Count];
            for(int i=0;i<values.Count;i++)
            {
                object v=values[i];
                if(!(v is double||v is float||v is int||v is long||v is decimal)) throw new InvalidOperationException("Non-numeric structural result.");
                numbers[i]=Convert.ToDouble(v,CultureInfo.InvariantCulture);
                if(Double.IsNaN(numbers[i])||Double.IsInfinity(numbers[i])) throw new InvalidOperationException("Non-finite structural result.");
            }
            return numbers;
        }

        /// <summary>Uses the installed wrapper's list/sublist protocol to enumerate generated Output parts.</summary>
        private static async Task<List<string>> OutputChildren(HttpClient http,string password,string owner)
        {
            var countResponse=await PlaxisClient.PostAsync(http,"list",PlaxisClient.Map("listqueries",new[]{PlaxisClient.Map("guid",owner,"method","count")}),password).ConfigureAwait(false);
            object countRaw;
            if(!countResponse.TryGetValue("listqueries",out countRaw)||!(countRaw is IList)||((IList)countRaw).Count!=1) throw new InvalidOperationException("Invalid Output part count.");
            var countQuery=((IList)countRaw)[0] as Dictionary<string,object>;
            if(countQuery==null||!countQuery.ContainsKey("success")||!Object.Equals(countQuery["success"],true)||!countQuery.TryGetValue("outputdata",out countRaw)||!(countRaw is int)) throw new InvalidOperationException("Invalid Output part count.");
            int count=(int)countRaw;
            if(count<1||count>1000) throw new InvalidOperationException("Expected 1 to 1000 Output structural parts.");
            var response=await PlaxisClient.PostAsync(http,"list",PlaxisClient.Map("listqueries",new[]{PlaxisClient.Map("guid",owner,"method","sublist","startindex",0,"stopindex",count)}),password).ConfigureAwait(false);
            object raw;
            if(!response.TryGetValue("listqueries",out raw)||!(raw is IList)||((IList)raw).Count!=1) throw new InvalidOperationException("Invalid Output list response.");
            var query=((IList)raw)[0] as Dictionary<string,object>;
            if(query==null||!query.ContainsKey("success")||!Object.Equals(query["success"],true)||!query.TryGetValue("outputdata",out raw)||!(raw is IList))
                throw new InvalidOperationException("Cannot enumerate Output structural parts.");
            var children=new List<string>();
            foreach(var item in (IList)raw)
            {
                var entry=item as Dictionary<string,object>;
                if(entry==null) throw new InvalidOperationException("Invalid Output part.");
                string guid=PlaxisClient.StringAt(entry,"guid"); Guid parsed;
                if(!Guid.TryParse(guid,out parsed)||children.Contains(guid)) throw new InvalidOperationException("Invalid or duplicate Output part identity.");
                children.Add(guid);
                if(children.Count>1000) throw new InvalidOperationException("Too many Output structural parts.");
            }
            if(children.Count!=count) throw new InvalidOperationException("Output part list changed during enumeration.");
            return children;
        }

        /// <summary>Verifies project provenance and reads complete structural nodal arrays from the selected final phase step.</summary>
        internal static async Task<StructuralReadResult> ReadStructuralAsync(StructuralRequest request,ConnectionData connection,Action<string> report)
        {
            var result=new StructuralReadResult();
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
                        string forceUnit=Convert.ToString(await Property(output,connection.Password,info,"UnitForce").ConfigureAwait(false));
                        if(forceUnit!="N") throw new InvalidOperationException("Output force unit must be N; received "+forceUnit+".");
                        stage="Resolving Output structure "+request.Id; report(stage+"...");
                        string sourceName=(request.IsAnchor?"GH_Anchor_":"GH_Plate_")+request.Id;
                        string source=await Named(output,connection.Password,sourceName).ConfigureAwait(false);
                        if(Convert.ToString(await Property(output,connection.Password,source,"Name").ConfigureAwait(false))!=sourceName)
                            throw new InvalidOperationException("Output structure identity mismatch.");
                        var parts=await OutputChildren(output,connection.Password,source).ConfigureAwait(false);
                        if(parts.Count==0) throw new InvalidOperationException("No Output cut elements found for "+sourceName+".");
                        var rows=new List<StructuralRow>();
                        foreach(var part in parts)
                        {
                            string partName=Convert.ToString(await Property(output,connection.Password,part,"Name").ConfigureAwait(false));
                            stage="Reading "+request.Id+" / "+partName; report(stage+"...");
                            string family=request.IsAnchor?"NodeToNodeAnchor":"Plate";
                            string[] fields=request.IsAnchor?new[]{"X","Y","N"}:new[]{"X","Y","Ux","Uy","Nx2D","Q2D","M2D"};
                            var arrays=new List<double[]>();
                            foreach(string field in fields)
                            {
                                stage="Reading "+request.Id+" / "+partName+" / "+field; report(stage+"...");
                                arrays.Add(NumericArray(await Command(output,connection.Password,"getresults "+part+" "+outputPhase+" ResultTypes."+family+"."+field+" \"node\"").ConfigureAwait(false)));
                            }
                            int count=arrays[0].Length;
                            if(arrays.Any(values=>values.Length!=count)) throw new InvalidOperationException("Structural result arrays have different lengths.");
                            if(rows.Count+count>200000) throw new InvalidOperationException("Structural result exceeds 200000 rows.");
                            for(int i=0;i<count;i++)
                                rows.Add(new StructuralRow(partName,request.Segment,arrays[0][i],arrays[1][i],
                                    request.IsAnchor?0:arrays[2][i],request.IsAnchor?0:arrays[3][i],
                                    request.IsAnchor?arrays[2][i]:arrays[4][i],request.IsAnchor?0:arrays[5][i],request.IsAnchor?0:arrays[6][i]));
                        }
                        var snapshot=new StructuralSnapshot(request,rows);
                        // Detect an externally switched project before publishing any values.
                        if(!SameFile(Convert.ToString(await Property(output,connection.Password,info,"Filename").ConfigureAwait(false)),build.File))
                            throw new InvalidOperationException("Output project changed while reading results.");
                        result.Snapshot=snapshot; result.Success=true;
                        result.Summary="Read "+snapshot.Rows.Count+" rows for "+request.Id+" in "+request.Phase.Id+
                            (request.IsAnchor?". Native force per anchor: N; equivalent force per width = N / spacing [m].":". Coordinates/displacements: m; N and Q: N/m; M: N m/m. Native local signs.");
                    }
                }
            }
            catch(Exception ex)
            {
                result.Snapshot=null;
                result.Error=(stage+"\n"+(ex is TaskCanceledException?"Result request timed out. Inspect PLAXIS Output and retry explicitly.":ex.Message)).Replace(connection.Password,"[password]");
                result.Summary="No result values returned.";
            }
            finally { byte ignored; Active.TryRemove(connection.Port,out ignored); }
            return result;
        }

    }
}

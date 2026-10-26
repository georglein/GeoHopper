using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GeoHopper
{
    /// <summary>A finite straight segment in canonical metres, shared by loads and contacts.</summary>
    internal sealed class SegmentData
    {
        internal readonly XY A, B;
        /// <summary>Rejects zero-length and non-finite geometry before any server call.</summary>
        internal SegmentData(XY a, XY b)
        {
            foreach(var p in new[]{a,b}) { Rules.Range(p.X,-1e6,1e6,"X [m]"); Rules.Range(p.Y,-1e6,1e6,"Y [m]"); }
            if(Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y))<=Rules.Tolerance)
                throw new ArgumentException("A line feature needs a nonzero straight segment.");
            A=a; B=b;
        }
        /// <summary>Returns command-safe coordinate pairs.</summary>
        internal string Command() { return "("+Rules.N(A.X)+" "+Rules.N(A.Y)+") ("+Rules.N(B.X)+" "+Rules.N(B.Y)+")"; }
        /// <summary>Tests whether both endpoints lie on a single polygon edge, including partial edges.</summary>
        internal bool OnBoundary(RegionData region)
        {
            for(int i=0;i<region.Points.Count;i++)
                if(OnEdge(region.Points[i],region.Points[(i+1)%region.Points.Count],A) &&
                   OnEdge(region.Points[i],region.Points[(i+1)%region.Points.Count],B)) return true;
            return false;
        }
        /// <summary>Tests finite segment containment within the metre tolerance.</summary>
        internal static bool OnEdge(XY a,XY b,XY p)
        {
            double dx=b.X-a.X,dy=b.Y-a.Y;
            return Math.Abs(dx*(p.Y-a.Y)-dy*(p.X-a.X))<=Rules.Tolerance*Math.Sqrt(dx*dx+dy*dy) &&
                p.X>=Math.Min(a.X,b.X)-Rules.Tolerance && p.X<=Math.Max(a.X,b.X)+Rules.Tolerance &&
                p.Y>=Math.Min(a.Y,b.Y)-Rules.Tolerance && p.Y<=Math.Max(a.Y,b.Y)+Rules.Tolerance;
        }
        /// <summary>Detects overlapping collinear features that PLAXIS might merge into one feature.</summary>
        internal bool Overlaps(SegmentData other)
        {
            double dx=B.X-A.X,dy=B.Y-A.Y,len=Math.Sqrt(dx*dx+dy*dy);
            if(Math.Abs(dx*(other.A.Y-A.Y)-dy*(other.A.X-A.X))>Rules.Tolerance*len ||
               Math.Abs(dx*(other.B.Y-A.Y)-dy*(other.B.X-A.X))>Rules.Tolerance*len) return false;
            double s=((other.A.X-A.X)*dx+(other.A.Y-A.Y)*dy)/len;
            double t=((other.B.X-A.X)*dx+(other.B.Y-A.Y)*dy)/len;
            return Math.Min(len,Math.Max(s,t))-Math.Max(0,Math.Min(s,t))>Rules.Tolerance;
        }
    }

    /// <summary>One contact feature between two named regions, with explicit side and contact material.</summary>
    internal sealed class InterfaceData
    {
        internal readonly string Id, RegionA, RegionB;
        internal readonly string PlateId;
        internal readonly SegmentData Segment;
        internal readonly MaterialData Material;
        internal readonly bool Positive;
        internal readonly double Rinter;
        /// <summary>Requires drained soil properties for this initial contact formulation.</summary>
        internal InterfaceData(string id,SegmentData segment,string regionA,string regionB,MaterialData material,bool positive,double rinter)
        {
            Id=Rules.Id(id); RegionA=Rules.Id(regionA); RegionB=Rules.Id(regionB);
            if(String.Equals(RegionA,RegionB,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Contact regions must be different.");
            if(segment==null || material==null || !material.MohrCoulomb) throw new ArgumentException("An interface needs a straight segment and a drained Mohr-Coulomb material.");
            Rules.Range(rinter,.01,1,"Rinter");
            Segment=segment; Material=material; Positive=positive; Rinter=rinter;
        }
        /// <summary>Defines one plate/soil contact segment, directed consistently with the source plate.</summary>
        internal InterfaceData(string id,SegmentData segment,PlateData plate,string soilRegion,MaterialData material,bool positive,double rinter)
        {
            Id=Rules.Id(id); RegionA=Rules.Id(soilRegion); RegionB="";
            if(plate==null || segment==null || material==null || !material.MohrCoulomb)
                throw new ArgumentException("Plate Interface requires a plate, straight segment and drained MC material.");
            if(!SegmentData.OnEdge(plate.Segment.A,plate.Segment.B,segment.A) || !SegmentData.OnEdge(plate.Segment.A,plate.Segment.B,segment.B))
                throw new ArgumentException("Contact segment must lie entirely on its plate.");
            double dot=(segment.B.X-segment.A.X)*(plate.Segment.B.X-plate.Segment.A.X)+(segment.B.Y-segment.A.Y)*(plate.Segment.B.Y-plate.Segment.A.Y);
            Segment=dot>=0?segment:new SegmentData(segment.B,segment.A);
            PlateId=plate.Id; PlateSignature=plate.Signature(); Material=material; Positive=positive;
            Rules.Range(rinter,.01,1,"Rinter"); Rinter=rinter;
        }
        internal readonly string PlateSignature;
        /// <summary>Allows coincident opposite faces only on the same plate and identical segment extents.</summary>
        internal bool OppositeFace(InterfaceData other)
        {
            return PlateId!=null && other.PlateId!=null && String.Equals(PlateId,other.PlateId,StringComparison.OrdinalIgnoreCase) &&
                Positive!=other.Positive && SegmentData.OnEdge(Segment.A,Segment.B,other.Segment.A) &&
                SegmentData.OnEdge(Segment.A,Segment.B,other.Segment.B) && SegmentData.OnEdge(other.Segment.A,other.Segment.B,Segment.A) &&
                SegmentData.OnEdge(other.Segment.A,other.Segment.B,Segment.B);
        }
        /// <summary>Copies soil properties into a separately identified contact material; never mutates the source soil.</summary>
        internal string MaterialCommand()
        {
            return Material.Command().Replace("\"Identification\" "+Rules.Quote(Material.Id),
                "\"Identification\" "+Rules.Quote("GH_Contact_"+Id))+
                " \"InterfaceStrengthDetermination\" \"Manual\" \"Rinter\" "+Rules.N(Rinter)+
                " \"TensionCutOff\" True \"TensileStrength\" 0 \"GapClosure\" True";
        }
        /// <summary>Includes geometry, side, region references and mechanical properties in the build identity.</summary>
        internal string Signature() { return Id+"|"+Segment.Command()+"|"+RegionA+"|"+RegionB+"|"+Positive+"|"+MaterialCommand()+"|"+PlateId+"|"+PlateSignature; }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | Contact: "+RegionA+" / "+(PlateId==null?RegionB:"Plate "+PlateId); }
    }

    /// <summary>Uniform global XY line force in N/m on a one-metre out-of-plane strip.</summary>
    internal sealed class LoadData
    {
        internal readonly string Id, Region;
        internal readonly SegmentData Segment;
        internal readonly double Qx,Qy;
        /// <summary>Defines a load attached to one boundary of a named host region.</summary>
        internal LoadData(string id,SegmentData segment,string region,double qx,double qy)
        {
            Id=Rules.Id(id); Region=Rules.Id(region);
            if(segment==null) throw new ArgumentException("A load segment is required.");
            Rules.Range(qx,-1e12,1e12,"qx [N/m]"); Rules.Range(qy,-1e12,1e12,"qy [N/m]");
            Segment=segment; Qx=qx; Qy=qy;
        }
        /// <summary>Sets uniform global components explicitly; negative qy acts downwards.</summary>
        internal string Command() { return "lineload "+Segment.Command()+" \"Distribution\" \"Uniform\" \"qx_start\" "+Rules.N(Qx)+" \"qy_start\" "+Rules.N(Qy); }
        /// <summary>Returns a stable definition signature.</summary>
        internal string Signature() { return Id+"|"+Region+"|"+Command(); }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | qx="+Rules.N(Qx)+", qy="+Rules.N(Qy)+" N/m"; }
    }

    /// <summary>Full activation snapshot, with an explicit parent; empty parent denotes the initial gravity phase.</summary>
    internal sealed class PhaseData
    {
        internal readonly string Id,Parent,InitialMethod;
        internal readonly ReadOnlyCollection<string> Regions,Interfaces,Loads,Plates;
        internal readonly bool ResetDisplacements;
        /// <summary>Validates identifiers and copies lists so later GH solutions cannot mutate running jobs.</summary>
        internal PhaseData(string id,string parent,IEnumerable<string> regions,IEnumerable<string> interfaces,IEnumerable<string> loads,bool reset,IEnumerable<string> plates=null,string initialMethod="Gravity loading")
        {
            Id=Rules.Id(id); Parent=String.IsNullOrWhiteSpace(parent)?"":Rules.Id(parent.Trim());
            if(initialMethod!="Gravity loading" && initialMethod!="K0 procedure")
                throw new ArgumentException("Initial method must be Gravity loading or K0 procedure.");
            if(Parent!="" && initialMethod!="Gravity loading")
                throw new ArgumentException("Set Initial method only on the initial phase; leave its default on child phases.");
            InitialMethod=initialMethod;
            Regions=Ids(regions); Interfaces=Ids(interfaces); Loads=Ids(loads); Plates=Ids(plates); ResetDisplacements=reset;
            if(Regions.Count==0) throw new ArgumentException("Each phase needs at least one active region.");
            if(Parent=="" && (reset || Interfaces.Count>0 || Loads.Count>0 || Plates.Count>0))
                throw new ArgumentException("The initial gravity phase must not reset displacements or activate contacts/loads/plates.");
        }
        /// <summary>Normalizes an optional list, rejecting duplicates and unsafe identifiers.</summary>
        internal static ReadOnlyCollection<string> Ids(IEnumerable<string> values)
        {
            var list=(values??Enumerable.Empty<string>()).Where(x=>!String.IsNullOrWhiteSpace(x)).Select(x=>Rules.Id(x.Trim())).ToList();
            if(list.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Count) throw new ArgumentException("An ID list contains duplicate entries.");
            return list.AsReadOnly();
        }
        /// <summary>Captures the complete activation state, rather than only changes from a parent.</summary>
        internal string Signature() { return Id+"|"+Parent+"|"+String.Join(",",Regions)+"|"+String.Join(",",Interfaces)+"|"+String.Join(",",Loads)+"|"+ResetDisplacements+"|"+String.Join(",",Plates)+"|"+InitialMethod; }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | "+(Parent==""?"Initial "+InitialMethod:"Plastic, parent "+Parent); }
    }

    /// <summary>Absolute global element dimension and repeated local refinement on selected region polygons.</summary>
    internal sealed class MeshData
    {
        internal readonly double ElementSize;
        internal readonly ReadOnlyCollection<string> RefineRegions,RefinePlates;
        internal readonly int RefinementPasses;
        /// <summary>Validates bounded mesh controls; size is metres, not a relative coarseness factor.</summary>
        internal MeshData(double size,IEnumerable<string> regions,int passes,IEnumerable<string> plates=null)
        {
            Rules.Range(size,.001,1e4,"Element size [m]");
            if(passes<0 || passes>3) throw new ArgumentException("Refinement passes must be between 0 and 3.");
            ElementSize=size; RefineRegions=PhaseData.Ids(regions); RefinePlates=PhaseData.Ids(plates); RefinementPasses=passes;
        }
        /// <summary>Captures meshing controls in the immutable model identity.</summary>
        internal string Signature() { return Rules.N(ElementSize)+"|"+String.Join(",",RefineRegions)+"|"+RefinementPasses+"|"+String.Join(",",RefinePlates); }
        /// <inheritdoc/>
        public override string ToString() { return "Element size "+Rules.N(ElementSize)+" m | "+RefinementPasses+" refinement passes"; }
    }

    /// <summary>Validated dry staged analysis. Phases are topologically sorted before transfer.</summary>
    internal sealed class AnalysisData
    {
        internal readonly ReadOnlyCollection<InterfaceData> Interfaces;
        internal readonly ReadOnlyCollection<LoadData> Loads;
        internal readonly ReadOnlyCollection<PhaseData> Phases;
        internal readonly MeshData Mesh;
        /// <summary>Rejects invalid references, duplicate features, phase cycles and contacts without both active regions.</summary>
        internal AnalysisData(IList<RegionData> regions,IEnumerable<InterfaceData> interfaces,IEnumerable<LoadData> loads,
            IEnumerable<PhaseData> phases,MeshData mesh,IEnumerable<PlateData> plates=null)
        {
            if(mesh==null) throw new ArgumentException("Analysis requires Mesh Settings.");
            Mesh=mesh;
            var ints=(interfaces??Enumerable.Empty<InterfaceData>()).ToList();
            var lds=(loads??Enumerable.Empty<LoadData>()).ToList();
            var phs=(phases??Enumerable.Empty<PhaseData>()).ToList();
            if(ints.Any(x=>x==null)||lds.Any(x=>x==null)||phs.Any(x=>x==null)) throw new ArgumentException("Analysis contains missing definitions.");
            Unique(ints.Select(x=>x.Id)); Unique(lds.Select(x=>x.Id)); Unique(phs.Select(x=>x.Id));
            if(phs.Count<2 || phs.Count>30 || phs.Count(x=>x.Parent=="")!=1)
                throw new ArgumentException("Provide 2 to 30 phases with exactly one initial phase (empty Parent).");
            if(ints.Count>100 || lds.Count>100) throw new ArgumentException("At most 100 interfaces and 100 loads are supported.");
            var reg=regions.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
            var plateMap=(plates??Enumerable.Empty<PlateData>()).ToDictionary(p=>p.Id,StringComparer.OrdinalIgnoreCase);
            foreach(var id in mesh.RefinePlates) Require(plateMap,id,"mesh plate");
            foreach(var id in mesh.RefineRegions) Require(reg,id,"mesh region");
            foreach(var it in ints)
            {
                if(!it.Segment.OnBoundary(Require(reg,it.RegionA,"contact region")))
                    throw new ArgumentException("Interface "+it.Id+" must lie on its soil region boundary. Split contacts at soil boundaries.");
                if(it.PlateId==null)
                {
                    if(!it.Segment.OnBoundary(Require(reg,it.RegionB,"contact region")))
                        throw new ArgumentException("Interface "+it.Id+" must lie on a shared straight boundary of both regions.");
                }
                else if(Require(plateMap,it.PlateId,"contact plate").Signature()!=it.PlateSignature)
                    throw new ArgumentException("Plate Interface "+it.Id+" refers to a different plate definition than Model.Plates.");
            }
            foreach(var l in lds)
                if(!l.Segment.OnBoundary(Require(reg,l.Region,"load region")))
                    throw new ArgumentException("Load "+l.Id+" must lie on its host region boundary.");
            for(int i=0;i<ints.Count;i++) for(int j=i+1;j<ints.Count;j++)
                if(ints[i].Segment.Overlaps(ints[j].Segment) && !ints[i].OppositeFace(ints[j]))
                    throw new ArgumentException("Overlapping contacts require identical segments on opposite sides of the same plate. Split both sides at the same levels.");
            for(int i=0;i<lds.Count;i++) for(int j=i+1;j<lds.Count;j++)
                if(lds[i].Segment.Overlaps(lds[j].Segment)) throw new ArgumentException("Load segments overlap; combine their components.");
            var im=ints.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
            var lm=lds.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
            var plateIds=new HashSet<string>((plates??Enumerable.Empty<PlateData>()).Select(p=>p.Id),StringComparer.OrdinalIgnoreCase);
            var pm=phs.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
            foreach(var p in phs)
            {
                foreach(var id in p.Regions) Require(reg,id,"active region");
                foreach(var id in p.Plates)
                    if(!plateIds.Contains(id)) throw new ArgumentException("Unknown active plate: "+id);
                if(p.Parent!="") Require(pm,p.Parent,"parent phase");
                foreach(var id in p.Interfaces)
                {
                    var it=Require(im,id,"active interface");
                    if(!p.Regions.Contains(it.RegionA,StringComparer.OrdinalIgnoreCase) ||
                        (it.PlateId==null?!p.Regions.Contains(it.RegionB,StringComparer.OrdinalIgnoreCase):!p.Plates.Contains(it.PlateId,StringComparer.OrdinalIgnoreCase)))
                        throw new ArgumentException("Phase "+p.Id+" activates contact "+id+" without its active soil and adjacent region/plate. Omit excavated-side contacts.");
                }
                foreach(var id in p.Loads)
                    if(!p.Regions.Contains(Require(lm,id,"active load").Region,StringComparer.OrdinalIgnoreCase))
                        throw new ArgumentException("Phase "+p.Id+" activates a load on an inactive region.");
            }
            var ordered=new List<PhaseData>();
            while(ordered.Count<phs.Count)
            {
                var next=phs.FirstOrDefault(p=>!ordered.Contains(p) && (p.Parent=="" || ordered.Any(q=>String.Equals(q.Id,p.Parent,StringComparison.OrdinalIgnoreCase))));
                if(next==null) throw new ArgumentException("Phase parents contain a cycle.");
                ordered.Add(next);
            }
            if(mesh.ElementSize>Math.Max(regions.Max(r=>r.Points.Max(p=>p.X))-regions.Min(r=>r.Points.Min(p=>p.X)),
                regions.Max(r=>r.Points.Max(p=>p.Y))-regions.Min(r=>r.Points.Min(p=>p.Y))))
                throw new ArgumentException("Element size exceeds the model extent.");
            if(ordered[0].InitialMethod=="K0 procedure") InitialStateRules.ValidateK0(regions,ordered[0]);
            Interfaces=ints.AsReadOnly(); Loads=lds.AsReadOnly(); Phases=ordered.AsReadOnly();
        }
        /// <summary>Ensures IDs in each namespace are unique, ignoring case.</summary>
        private static void Unique(IEnumerable<string> ids)
        { var values=ids.ToList(); if(values.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=values.Count) throw new ArgumentException("Analysis IDs must be unique within each object type."); }
        /// <summary>Resolves a semantic reference with a useful diagnostic.</summary>
        private static T Require<T>(Dictionary<string,T> map,string id,string kind)
        { T value; if(!map.TryGetValue(id,out value)) throw new ArgumentException("Unknown "+kind+": "+id); return value; }
        /// <summary>Includes every analysis definition and phase order in stale-result detection.</summary>
        internal string Signature()
        { return Mesh.Signature()+"|"+String.Join("|",Interfaces.Select(x=>x.Signature()))+"|"+String.Join("|",Loads.Select(x=>x.Signature()))+"|"+String.Join("|",Phases.Select(x=>x.Signature())); }
    }
}

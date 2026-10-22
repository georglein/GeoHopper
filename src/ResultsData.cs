using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Proof of one fully successful solve, invalidated by a later build or solve attempt.</summary>
    internal sealed class CalculationReceipt
    {
        internal readonly BuildReceipt Build;
        internal readonly string SolveId=Guid.NewGuid().ToString("N");
        /// <summary>Captures the exact immutable build whose phases completed successfully.</summary>
        internal CalculationReceipt(BuildReceipt build) { Build=build; }
        /// <inheritdoc/>
        public override string ToString() { return Build.Model.Id+" | Calculated model"; }
    }

    /// <summary>Named continuum sampling location in metres; never silently snaps to a boundary.</summary>
    internal sealed class ResultPointData
    {
        internal readonly string Id,Region;
        internal readonly XY Position;
        /// <summary>Validates identifiers and finite SI coordinates; containment is checked against the solved model.</summary>
        internal ResultPointData(string id,string region,XY position)
        {
            Id=Rules.Id(id); Region=Rules.Id(region);
            Rules.Range(position.X,-1e6,1e6,"X [m]"); Rules.Range(position.Y,-1e6,1e6,"Y [m]");
            Position=position;
        }
        /// <summary>Includes every measurement input in the UI result fingerprint.</summary>
        internal string Signature() { return Id+"|"+Region+"|"+Rules.N(Position.X)+"|"+Rules.N(Position.Y); }
        /// <summary>Requires strict polygon interior, avoiding ambiguous soil/interface-side interpolation.</summary>
        internal void Validate(RegionData region)
        {
            bool inside=false;
            for(int i=0,j=region.Points.Count-1;i<region.Points.Count;j=i++)
            {
                var a=region.Points[j]; var b=region.Points[i];
                double dx=b.X-a.X,dy=b.Y-a.Y;
                double t=Math.Max(0,Math.Min(1,((Position.X-a.X)*dx+(Position.Y-a.Y)*dy)/(dx*dx+dy*dy)));
                double ex=Position.X-a.X-t*dx,ey=Position.Y-a.Y-t*dy;
                if(ex*ex+ey*ey<=Rules.Tolerance*Rules.Tolerance)
                    throw new ArgumentException("Result point "+Id+" is on a region boundary. Move it inside "+region.Id+".");
                if((a.Y>Position.Y)!=(b.Y>Position.Y) && Position.X<(b.X-a.X)*(Position.Y-a.Y)/(b.Y-a.Y)+a.X)
                    inside=!inside;
            }
            if(!inside) throw new ArgumentException("Result point "+Id+" is outside region "+region.Id+".");
        }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | "+Region+" | ("+Rules.N(Position.X)+", "+Rules.N(Position.Y)+") m"; }
    }

    /// <summary>One immutable sampled displacement with provenance for safe downstream comparisons.</summary>
    internal sealed class PointResultData
    {
        internal readonly ResultPointData Point;
        internal readonly string Snapshot,Phase;
        internal readonly double Ux,Uy,Magnitude;
        /// <summary>Rejects missing, non-finite and implausible protocol sentinels rather than substituting zero.</summary>
        internal PointResultData(ResultPointData point,string snapshot,string phase,double ux,double uy)
        {
            Rules.Range(ux,-1e6,1e6,"Ux [m]"); Rules.Range(uy,-1e6,1e6,"Uy [m]");
            Point=point; Snapshot=snapshot; Phase=phase; Ux=ux; Uy=uy; Magnitude=Math.Sqrt(ux*ux+uy*uy);
        }
        /// <inheritdoc/>
        public override string ToString() { return Point.Id+" | "+Phase+" | Ux="+Rules.N(Ux)+", Uy="+Rules.N(Uy)+" m"; }
    }

    /// <summary>Validated measurement request bound to a selected phase of an immutable calculated model.</summary>
    internal sealed class ResultsRequest
    {
        internal readonly CalculationReceipt Receipt;
        internal readonly PhaseData Phase;
        internal readonly int PhaseIndex;
        internal readonly ReadOnlyCollection<ResultPointData> Points;
        internal readonly string Fingerprint;
        /// <summary>Checks phase membership, unique IDs, active host regions and strict point containment before HTTP.</summary>
        internal ResultsRequest(CalculationReceipt receipt,string phase,IEnumerable<ResultPointData> points)
        {
            if(receipt==null || receipt.Build.Model.Analysis==null) throw new ArgumentException("Connect a calculated model receipt.");
            Receipt=receipt;
            var phases=receipt.Build.Model.Analysis.Phases;
            phase=(phase??"").Trim();
            PhaseIndex=phase==""?phases.Count-1:phases.ToList().FindIndex(p=>String.Equals(p.Id,phase,StringComparison.OrdinalIgnoreCase));
            if(PhaseIndex<0) throw new ArgumentException("Unknown result phase: "+phase);
            Phase=phases[PhaseIndex];
            var list=(points??Enumerable.Empty<ResultPointData>()).ToList();
            if(list.Count<1 || list.Count>100) throw new ArgumentException("Supply 1 to 100 result points per read.");
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var point in list)
            {
                if(point==null || !ids.Add(point.Id)) throw new ArgumentException("Result point IDs must be unique.");
                var region=receipt.Build.Model.Regions.FirstOrDefault(r=>String.Equals(r.Id,point.Region,StringComparison.OrdinalIgnoreCase));
                if(region==null) throw new ArgumentException("Unknown result region: "+point.Region);
                if(!Phase.Regions.Contains(region.Id,StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("Result region "+region.Id+" is inactive in phase "+Phase.Id+".");
                point.Validate(region);
            }
            Points=list.AsReadOnly();
            Fingerprint=Rules.Hash(receipt.SolveId+"|"+Phase.Id+"|"+String.Join(";",Points.Select(p=>p.Signature())));
        }
    }

    /// <summary>Pure derived quantities with explicit sign conventions and consistent result provenance.</summary>
    internal static class ResultMetrics
    {
        /// <summary>Signed change of the A-to-B chord orientation in radians, positive counterclockwise.</summary>
        internal static double Rotation(PointResultData a,PointResultData b)
        {
            if(a.Snapshot!=b.Snapshot || a.Phase!=b.Phase ||
                !String.Equals(a.Point.Region,b.Point.Region,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Rotation needs two points from the same result snapshot, phase and region.");
            double x=b.Point.Position.X-a.Point.Position.X,y=b.Point.Position.Y-a.Point.Position.Y;
            double u=x+b.Ux-a.Ux,v=y+b.Uy-a.Uy;
            if(x*x+y*y<=Rules.Tolerance*Rules.Tolerance || u*u+v*v<=Rules.Tolerance*Rules.Tolerance)
                throw new ArgumentException("Rotation requires distinct points and a non-collapsed deformed chord.");
            return Math.Atan2(x*v-y*u,x*u+y*v);
        }
        /// <summary>Sums section area for a named material; equal numerically to volume per metre of extrusion.</summary>
        internal static double MaterialArea(ModelData model,string material)
        {
            material=Rules.Id(material);
            var regions=model.Regions.Where(r=>String.Equals(r.Material.Id,material,StringComparison.OrdinalIgnoreCase)).ToList();
            if(regions.Count==0) throw new ArgumentException("No model regions use material "+material+".");
            return regions.Sum(r=>r.Area);
        }
    }
}

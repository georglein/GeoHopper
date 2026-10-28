using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Validated structural request tied to one successful solve and active element.</summary>
    internal sealed class StructuralRequest
    {
        internal readonly CalculationReceipt Receipt;
        internal readonly PhaseData Phase;
        internal readonly int PhaseIndex;
        internal readonly string Id,Fingerprint;
        internal readonly bool IsAnchor;
        internal readonly SegmentData Segment;
        internal readonly double Spacing;
        /// <summary>Resolves canonical IDs and rejects inactive structures before any network access.</summary>
        internal StructuralRequest(CalculationReceipt receipt,string phase,string id,bool anchor)
        {
            if(receipt==null||receipt.Build.Model.Analysis==null) throw new ArgumentException("Connect a calculated model.");
            Receipt=receipt; IsAnchor=anchor;
            var model=receipt.Build.Model; var phases=model.Analysis.Phases;
            PhaseIndex=String.IsNullOrWhiteSpace(phase)?phases.Count-1:phases.ToList().FindIndex(p=>String.Equals(p.Id,phase.Trim(),StringComparison.OrdinalIgnoreCase));
            if(PhaseIndex<0) throw new ArgumentException("Unknown result phase: "+phase);
            Phase=phases[PhaseIndex]; id=Rules.Id(id);
            if(anchor)
            {
                var item=model.Anchors.FirstOrDefault(x=>String.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));
                if(item==null) throw new ArgumentException("Unknown anchor: "+id);
                Id=item.Id; Segment=item.Segment; Spacing=item.Material.Spacing;
                if(!Phase.Anchors.Contains(Id,StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Anchor is inactive in "+Phase.Id+".");
            }
            else
            {
                var item=model.Plates.FirstOrDefault(x=>String.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));
                if(item==null) throw new ArgumentException("Unknown plate: "+id);
                Id=item.Id; Segment=item.Segment; Spacing=1;
                if(!Phase.Plates.Contains(Id,StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Plate is inactive in "+Phase.Id+".");
            }
            Fingerprint=Rules.Hash(receipt.SolveId+"|"+Phase.Id+"|"+anchor+"|"+Id);
        }
    }

    /// <summary>One unaveraged native nodal row. Force signs retain the PLAXIS local convention.</summary>
    internal sealed class StructuralRow
    {
        internal readonly string Part;
        internal readonly double X,Y,Ux,Uy,N,Q,M,Station;
        /// <summary>Validates coordinates against the requested source line and keeps all duplicate rows.</summary>
        internal StructuralRow(string part,SegmentData segment,double x,double y,double ux,double uy,double n,double q,double m)
        {
            Part=part; X=x; Y=y; Ux=ux; Uy=uy; N=n; Q=q; M=m;
            foreach(double value in new[]{x,y,ux,uy,n,q,m})
                if(Double.IsNaN(value)||Double.IsInfinity(value)) throw new ArgumentException("Non-finite structural result.");
            double dx=segment.B.X-segment.A.X,dy=segment.B.Y-segment.A.Y;
            double length=Math.Sqrt(dx*dx+dy*dy);
            Station=((x-segment.A.X)*dx+(y-segment.A.Y)*dy)/length;
            double cross=Math.Abs((x-segment.A.X)*dy-(y-segment.A.Y)*dx)/length;
            double tolerance=Math.Max(1e-5,length*1e-7);
            if(cross>tolerance||Station < -tolerance||Station>length+tolerance)
                throw new ArgumentException("Output coordinates do not lie on the requested structure.");
        }
    }

    /// <summary>Immutable complete structural read used by preview and numerical outputs.</summary>
    internal sealed class StructuralSnapshot
    {
        internal readonly StructuralRequest Request;
        internal readonly ReadOnlyCollection<StructuralRow> Rows;
        /// <summary>Sorts stably along source geometry, retaining native duplicate forces at element boundaries.</summary>
        internal StructuralSnapshot(StructuralRequest request,IEnumerable<StructuralRow> rows)
        {
            Request=request; var list=rows.OrderBy(x=>x.Station).ThenBy(x=>x.Part,StringComparer.Ordinal).ToList();
            if(list.Count<2) throw new ArgumentException("Expected at least two structural result rows.");
            Rows=list.AsReadOnly();
        }
        /// <summary>Groups preview vertices by native part and rejects conflicting nodal displacements instead of averaging.</summary>
        internal List<List<XY>> Deformed(double factor)
        {
            if(Request.IsAnchor) throw new ArgumentException("Plate preview requires Plate Results.");
            Rules.Range(factor,0,1e6,"Display factor");
            var curves=new List<List<XY>>();
            foreach(var group in Rows.GroupBy(x=>x.Part))
            {
                var vertices=new List<XY>(); StructuralRow previous=null;
                foreach(var row in group.OrderBy(x=>x.Station))
                {
                    if(previous!=null&&Math.Abs(row.Station-previous.Station)<1e-8)
                    {
                        if(Math.Abs(row.Ux-previous.Ux)>1e-8||Math.Abs(row.Uy-previous.Uy)>1e-8)
                            throw new ArgumentException("Coincident plate nodes have different displacements; inspect native results.");
                        continue;
                    }
                    vertices.Add(new XY(row.X+factor*row.Ux,row.Y+factor*row.Uy)); previous=row;
                }
                if(vertices.Count<2) throw new ArgumentException("Insufficient distinct nodes for plate preview.");
                curves.Add(vertices);
            }
            return curves;
        }
        /// <inheritdoc/>
        public override string ToString() { return Request.Id+" | "+Request.Phase.Id+" | "+Rows.Count+" structural rows"; }
    }

    /// <summary>Failure contains no partial snapshot.</summary>
    internal sealed class StructuralReadResult
    {
        internal bool Success;
        internal string Error="",Summary="";
        internal StructuralSnapshot Snapshot;
    }
}

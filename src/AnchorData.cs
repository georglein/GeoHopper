using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Elastic axial properties per discrete anchor, with explicit out-of-plane spacing.</summary>
    internal sealed class AnchorMaterialData
    {
        internal readonly string Id;
        internal readonly double EA,Spacing;
        internal readonly int Argb;
        /// <summary>Validates EA [N] per anchor and spacing [m]; neither value is pre-divided by spacing.</summary>
        internal AnchorMaterialData(string id,double ea,double spacing,int argb)
        {
            Id=Rules.Id(id); Rules.Range(ea,1e-9,1e18,"Anchor EA [N]"); Rules.Range(spacing,1e-6,1e6,"Anchor spacing [m]");
            EA=ea; Spacing=spacing; Argb=argb;
        }
        /// <summary>Creates the native elastic anchor material using PLAXIS 2D 2025.1 property names.</summary>
        internal string Command()
        { return "anchormat \"Identification\" "+Rules.Quote(Id)+" \"MaterialType\" \"Elastic\" \"EA\" "+Rules.N(EA)+" \"LSpacing\" "+Rules.N(Spacing); }
        /// <summary>Includes mechanical and display values in definition identity.</summary>
        internal string Signature() { return Command()+"|"+Argb; }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | Elastic anchor | EA="+Rules.N(EA)+" N | spacing="+Rules.N(Spacing)+" m"; }
    }

    /// <summary>Straight node-to-node axial element with optional declared plate attachments at its endpoints.</summary>
    internal sealed class AnchorData
    {
        internal readonly string Id,StartPlate,EndPlate;
        internal readonly SegmentData Segment;
        internal readonly AnchorMaterialData Material;
        /// <summary>Copies immutable line/material data; blank plate references leave attachment validation to the user.</summary>
        internal AnchorData(string id,SegmentData segment,AnchorMaterialData material,string startPlate="",string endPlate="")
        {
            Id=Rules.Id(id);
            if(segment==null || material==null) throw new ArgumentException("Node-to-Node Anchor requires a straight line and Anchor Material.");
            Segment=segment; Material=material;
            StartPlate=String.IsNullOrWhiteSpace(startPlate)?"":Rules.Id(startPlate.Trim());
            EndPlate=String.IsNullOrWhiteSpace(endPlate)?"":Rules.Id(endPlate.Trim());
        }
        /// <summary>Creates a line feature; attachment IDs do not invent rigid links or move endpoints.</summary>
        internal string Command() { return "n2nanchor "+Segment.Command(); }
        /// <summary>Includes spacing, endpoint references and geometry for stale-result detection.</summary>
        internal string Signature() { return Id+"|"+Command()+"|"+Material.Signature()+"|"+StartPlate+"|"+EndPlate; }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | Node-to-node anchor | "+Material.Id; }
        /// <summary>Rejects ambiguous elements and checks explicitly declared plate attachments before transfer.</summary>
        internal static void Validate(IList<AnchorData> anchors,IList<PlateData> plates,double minX,double minY,double maxX,double maxY)
        {
            if(anchors.Count>100 || anchors.Any(a=>a==null)) throw new ArgumentException("Provide at most 100 non-null anchors.");
            if(anchors.Select(a=>a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=anchors.Count)
                throw new ArgumentException("Anchor IDs must be unique (case-insensitive).");
            foreach(var group in anchors.GroupBy(a=>a.Material.Id,StringComparer.OrdinalIgnoreCase))
                if(group.Any(a=>a.Material.Signature()!=group.First().Material.Signature()))
                    throw new ArgumentException("Conflicting anchor material definitions: "+group.Key);
            var map=plates.ToDictionary(p=>p.Id,StringComparer.OrdinalIgnoreCase);
            foreach(var a in anchors)
            {
                foreach(var point in new[]{a.Segment.A,a.Segment.B})
                    if(point.X<minX || point.X>maxX || point.Y<minY || point.Y>maxY)
                        throw new ArgumentException("Anchor "+a.Id+" lies outside the model contour.");
                Attachment(a.StartPlate,a.Segment.A,map); Attachment(a.EndPlate,a.Segment.B,map);
            }
            for(int i=0;i<anchors.Count;i++) for(int j=i+1;j<anchors.Count;j++)
                if(anchors[i].Segment.Overlaps(anchors[j].Segment)) throw new ArgumentException("Overlapping anchors: "+anchors[i].Id+" and "+anchors[j].Id);
        }
        /// <summary>Requires an endpoint to lie on its named plate; does not snap or alter user geometry.</summary>
        private static void Attachment(string id,XY point,Dictionary<string,PlateData> plates)
        {
            if(id=="") return;
            PlateData p;
            if(!plates.TryGetValue(id,out p)) throw new ArgumentException("Unknown anchor attachment plate: "+id);
            if(!SegmentData.OnEdge(p.Segment.A,p.Segment.B,point)) throw new ArgumentException("Anchor endpoint does not lie on attachment plate "+id+".");
        }
        /// <summary>Rejects activation while a declared supporting plate is inactive.</summary>
        internal void ValidatePhase(PhaseData phase)
        {
            foreach(var id in new[]{StartPlate,EndPlate})
                if(id!="" && !phase.Plates.Contains(id,StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("Phase "+phase.Id+" activates anchor "+Id+" without attachment plate "+id+".");
        }
    }
}

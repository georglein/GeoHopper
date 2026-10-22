using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Isotropic elastic plate properties per metre out of plane, independent of continuum materials.</summary>
    internal sealed class PlateMaterialData
    {
        internal readonly string Id;
        internal readonly double EA, EI, Nu, Weight;
        internal readonly int Argb;
        internal readonly bool PreventPunching;
        /// <summary>Validates axial stiffness [N/m], bending stiffness [N m2/m], and weight [N/m/m].</summary>
        internal PlateMaterialData(string id,double ea,double ei,double nu,double weight,bool preventPunching,int argb)
        {
            Id=Rules.Id(id);
            Rules.Range(ea,1e-9,1e18,"EA [N/m]"); Rules.Range(ei,1e-9,1e18,"EI [N m2/m]");
            Rules.Range(nu,0,.499,"Poisson ratio"); Rules.Range(weight,0,1e12,"Weight [N/m/m]");
            EA=ea; EI=ei; Nu=nu; Weight=weight; PreventPunching=preventPunching; Argb=argb;
        }
        /// <summary>Uses PLAXIS 2D plate-specific property names, not soil material properties.</summary>
        internal string Command()
        {
            return "platemat \"Identification\" "+Rules.Quote(Id)+" \"MaterialType\" \"Elastic\" \"Isotropic\" True"+
                " \"EA1\" "+Rules.N(EA)+" \"EI\" "+Rules.N(EI)+" \"StructNu\" "+Rules.N(Nu)+
                " \"w\" "+Rules.N(Weight)+" \"PreventPunching\" "+(PreventPunching?"True":"False");
        }
        /// <summary>Includes preview metadata in immutable model identity.</summary>
        internal string Signature() { return Command()+"|"+Argb; }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | Elastic isotropic plate | EA="+Rules.N(EA)+" N/m | EI="+Rules.N(EI)+" N m2/m"; }
    }

    /// <summary>One directed straight structural plate; it does not remove or replace soil volume.</summary>
    internal sealed class PlateData
    {
        internal readonly string Id;
        internal readonly SegmentData Segment;
        internal readonly PlateMaterialData Material;
        /// <summary>Associates a stable ID with geometry in metres and a plate-only material.</summary>
        internal PlateData(string id,SegmentData segment,PlateMaterialData material)
        {
            Id=Rules.Id(id);
            if(segment==null || material==null) throw new ArgumentException("A plate requires a straight line and Plate Material.");
            Segment=segment; Material=material;
        }
        /// <summary>Creates a single native plate feature along its directed line.</summary>
        internal string Command() { return "plate "+Segment.Command(); }
        /// <summary>Changes to geometry or stiffness invalidate prior build receipts.</summary>
        internal string Signature() { return Id+"|"+Command()+"|"+Material.Signature(); }
        /// <inheritdoc/>
        public override string ToString() { return Id+" | Plate | "+Material.Id; }
        /// <summary>Rejects duplicate IDs, conflicting materials, overlapping plates and out-of-domain endpoints.</summary>
        internal static void Validate(IList<PlateData> plates,double minX,double minY,double maxX,double maxY)
        {
            if(plates.Count>100 || plates.Any(p=>p==null)) throw new ArgumentException("Provide at most 100 non-null plates.");
            if(plates.Select(p=>p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=plates.Count)
                throw new ArgumentException("Plate IDs must be unique (case-insensitive).");
            foreach(var group in plates.GroupBy(p=>p.Material.Id,StringComparer.OrdinalIgnoreCase))
                if(group.Any(p=>p.Material.Signature()!=group.First().Material.Signature()))
                    throw new ArgumentException("Conflicting plate material definitions: "+group.Key);
            foreach(var plate in plates)
                foreach(var point in new[]{plate.Segment.A,plate.Segment.B})
                    if(point.X<minX || point.X>maxX || point.Y<minY || point.Y>maxY)
                        throw new ArgumentException("Plate "+plate.Id+" lies outside the model contour.");
            for(int i=0;i<plates.Count;i++) for(int j=i+1;j<plates.Count;j++)
                if(plates[i].Segment.Overlaps(plates[j].Segment))
                    throw new ArgumentException("Overlapping plates: "+plates[i].Id+" and "+plates[j].Id);
        }
    }
}

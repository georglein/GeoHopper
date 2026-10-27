using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoHopper
{
    /// <summary>Finite coordinate in the canonical PLAXIS XY plane, in metres.</summary>
    internal struct XY
    {
        internal readonly double X, Y;
        /// <summary>Creates a coordinate without hidden unit conversion.</summary>
        internal XY(double x, double y) { X = x; Y = y; }
    }

    /// <summary>Shared input validation and culture-independent protocol formatting.</summary>
    internal static class Rules
    {
        internal const double Tolerance = 1e-7;
        /// <summary>Requires a finite scalar in a closed interval.</summary>
        internal static void Range(double value, double min, double max, string name)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException(name + " must be finite and between " + N(min) + " and " + N(max) + ".");
        }
        /// <summary>Validates a stable, command-safe model or material identifier.</summary>
        internal static string Id(string value)
        {
            if (value == null || !Regex.IsMatch(value, @"\A[A-Za-z][A-Za-z0-9_]{0,39}\z"))
                throw new ArgumentException("IDs must start with a letter and contain at most 40 letters, digits or underscores.");
            return value;
        }
        /// <summary>Formats a finite value with an invariant decimal separator.</summary>
        internal static string N(double value) { return value.ToString("G17", CultureInfo.InvariantCulture); }
        /// <summary>Quotes a string for PLAXIS; rejects ambiguous quotes and line breaks.</summary>
        internal static string Quote(string value)
        {
            if (value == null || value.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("Text and paths must not contain double quotes or line breaks.");
            return "\"" + value + "\"";
        }
        /// <summary>Hashes immutable input snapshots to reject stale build results.</summary>
        internal static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Immutable material definition in Pa, N/m3 and radians; colour is display metadata.</summary>
    internal sealed class MaterialData
    {
        internal readonly string Id;
        internal readonly bool MohrCoulomb;
        internal readonly double E, Nu, GammaUnsat, GammaSat, Cohesion, Phi, Psi;
        internal readonly int Argb;
        /// <summary>Validates drained MC or non-porous linear-elastic input without contacting PLAXIS.</summary>
        internal MaterialData(string id, bool mc, double e, double nu, double gu, double gs,
            double c, double phi, double psi, int argb)
        {
            Id = Rules.Id(id);
            Rules.Range(e, 1e-9, 1e15, "E [Pa]");
            Rules.Range(nu, 0, 0.499, "Poisson ratio");
            Rules.Range(gu, 0, 1e9, "Unsaturated unit weight [N/m3]");
            Rules.Range(gs, gu, 1e9, "Saturated unit weight [N/m3]");
            Rules.Range(c, 0, 1e12, "Cohesion [Pa]");
            Rules.Range(phi, 0, Math.PI / 2 - 1e-5, "Friction angle [rad]");
            Rules.Range(psi, 0, phi, "Dilatancy angle [rad]");
            MohrCoulomb = mc; E = e; Nu = nu; GammaUnsat = gu; GammaSat = gs;
            Cohesion = c; Phi = phi; Psi = psi; Argb = argb;
        }
        /// <summary>Creates a command for a project explicitly configured in metres and newtons.</summary>
        internal string Command()
        {
            string text = "soilmat \"Identification\" " + Rules.Quote(Id) +
                " \"SoilModel\" " + Rules.Quote(MohrCoulomb ? "Mohr-Coulomb" : "Linear elastic") +
                " \"DrainageType\" " + Rules.Quote(MohrCoulomb ? "Drained" : "Non-porous") +
                " \"gammaUnsat\" " + Rules.N(GammaUnsat) +
                // Non-porous materials expose gammaSat as read-only in PLAXIS.
                // Only drained soil receives a separate saturated unit weight.
                (MohrCoulomb ? " \"gammaSat\" " + Rules.N(GammaSat) : "") +
                " \"Eref\" " + Rules.N(E) + " \"nu\" " + Rules.N(Nu);
            if (MohrCoulomb) text += " \"cRef\" " + Rules.N(Cohesion) + " \"phi\" " +
                Rules.N(Phi * 180 / Math.PI) + " \"psi\" " + Rules.N(Psi * 180 / Math.PI);
            return text;
        }
        /// <summary>Includes display colour in the definition identity, without sending it to PLAXIS.</summary>
        internal string Signature() { return Command() + "|" + Argb.ToString(CultureInfo.InvariantCulture); }
        /// <summary>Displays readable material information in GH panels.</summary>
        public override string ToString() { return Id + (MohrCoulomb ? " | Mohr-Coulomb" : " | Linear elastic"); }
    }

    /// <summary>Immutable, simple polygon paired with one material. Holes are not supported in WP1.</summary>
    internal sealed class RegionData
    {
        internal readonly string Id;
        internal readonly MaterialData Material;
        internal readonly ReadOnlyCollection<XY> Points;
        internal readonly double Area;
        /// <summary>Validates and normalizes a polygon to counter-clockwise orientation.</summary>
        internal RegionData(string id, IEnumerable<XY> points, MaterialData material)
        {
            Id = Rules.Id(id);
            if (material == null) throw new ArgumentException("A material is required.");
            var p = points.ToList();
            Geometry2D.Validate(p);
            if (Geometry2D.SignedArea(p) < 0) p.Reverse();
            Points = p.AsReadOnly(); Material = material; Area = Geometry2D.SignedArea(p);
        }
        /// <summary>Serializes coordinates to the native polygon command, already in metres.</summary>
        internal string Command()
        {
            return "polygon " + String.Join(" ", Points.Select(p => "(" + Rules.N(p.X) + " " + Rules.N(p.Y) + ")"));
        }
        /// <summary>Returns the full definition used by model fingerprints.</summary>
        internal string Signature() { return Id + "|" + Command() + "|" + Material.Signature(); }
        /// <summary>Displays region identity, material and area in GH panels.</summary>
        public override string ToString() { return Id + " | " + Material.Id + " | " + Rules.N(Area) + " m2"; }
    }

    /// <summary>Validated model snapshot; adjoining regions are allowed, overlapping interiors are rejected.</summary>
    internal sealed class ModelData
    {
        internal readonly string Id, Fingerprint;
        internal readonly ReadOnlyCollection<RegionData> Regions;
        internal readonly ReadOnlyCollection<MaterialData> Materials;
        internal readonly AnalysisData Analysis;
        internal readonly ReadOnlyCollection<PlateData> Plates;
        internal readonly ReadOnlyCollection<AnchorData> Anchors;
        internal readonly double MinX, MinY, MaxX, MaxY;
        /// <summary>Checks IDs, material consistency, polygon overlap and automatic model extents.</summary>
        internal ModelData(string id, IEnumerable<RegionData> regions, double margin,
            IEnumerable<InterfaceData> interfaces=null, IEnumerable<LoadData> loads=null,
            IEnumerable<PhaseData> phases=null, MeshData mesh=null, IEnumerable<PlateData> plates=null, IEnumerable<AnchorData> anchors=null)
        {
            Id = Rules.Id(id); Rules.Range(margin, 0, 1e5, "Model margin [m]");
            var list = regions.ToList();
            if (list.Count == 0 || list.Count > 100) throw new ArgumentException("Provide between 1 and 100 regions.");
            if (list.Any(r => r == null)) throw new ArgumentException("A region is missing.");
            if (list.Select(r => r.Id.ToLowerInvariant()).Distinct().Count() != list.Count)
                throw new ArgumentException("Region IDs must be unique (case-insensitive).");
            var materials = new List<MaterialData>();
            foreach (var group in list.GroupBy(r => r.Material.Id, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First().Material;
                if (group.Any(r => r.Material.Signature() != first.Signature()))
                    throw new ArgumentException("Conflicting definitions for material " + first.Id + ".");
                materials.Add(first);
            }
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (Geometry2D.Overlaps(list[i].Points, list[j].Points))
                        throw new ArgumentException("Overlapping interiors: " + list[i].Id + " and " + list[j].Id +
                            ". Partition the model into non-overlapping regions.");
            Regions = list.AsReadOnly(); Materials = materials.AsReadOnly();
            MinX = list.Min(r => r.Points.Min(p => p.X)) - margin;
            MinY = list.Min(r => r.Points.Min(p => p.Y)) - margin;
            MaxX = list.Max(r => r.Points.Max(p => p.X)) + margin;
            MaxY = list.Max(r => r.Points.Max(p => p.Y)) + margin;
            var plateList=(plates??Enumerable.Empty<PlateData>()).ToList();
            PlateData.Validate(plateList,MinX,MinY,MaxX,MaxY);
            Plates=plateList.AsReadOnly();
            if(plateList.Any(p=>materials.Any(m=>String.Equals(m.Id,p.Material.Id,StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("Use distinct IDs for continuum materials and plate materials.");
            var contacts=(interfaces??Enumerable.Empty<InterfaceData>()).ToList();
            if(contacts.Any(c=>plateList.Any(p=>String.Equals(p.Material.Id,"GH_Contact_"+c.Id,StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("Plate material ID conflicts with an automatically generated contact material name.");
            var anchorList=(anchors??Enumerable.Empty<AnchorData>()).ToList();
            AnchorData.Validate(anchorList,Plates,MinX,MinY,MaxX,MaxY);
            Anchors=anchorList.AsReadOnly();
            var reserved=materials.Select(m=>m.Id).Concat(Plates.Select(p=>p.Material.Id)).Concat(contacts.Select(c=>"GH_Contact_"+c.Id));
            if(anchorList.Any(a=>reserved.Contains(a.Material.Id,StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Anchor material IDs must differ from continuum, plate and generated contact material IDs.");
            var forces=(loads??Enumerable.Empty<LoadData>()).ToList();
            var stages=(phases??Enumerable.Empty<PhaseData>()).ToList();
            if(mesh!=null || contacts.Count>0 || forces.Count>0 || stages.Count>0)
            {
                if(margin!=0) throw new ArgumentException("Analysis currently requires Margin=0 so the model boundaries coincide with the region bounds.");
                Analysis=new AnalysisData(Regions,contacts,forces,stages,mesh,Plates,Anchors);
                if(contacts.Any(c=>materials.Any(m=>String.Equals(m.Id,"GH_Contact_"+c.Id,StringComparison.OrdinalIgnoreCase))))
                    throw new ArgumentException("Material ID conflicts with an automatically generated GH_Contact_ name.");
            }
            Fingerprint = Rules.Hash(Id + "|" + Rules.N(margin) + "|" + String.Join("|", list.Select(r => r.Signature()))+
                (Analysis==null?"":"|"+Analysis.Signature())+"|"+String.Join("|",Plates.Select(p=>p.Signature()))+"|"+String.Join("|",Anchors.Select(a=>a.Signature())));
        }
        /// <summary>Describes this immutable model without serializing a PLAXIS session.</summary>
        public override string ToString() { return Id + " | " + Regions.Count + " regions | " + Materials.Count + " continuum materials | " + Plates.Count + " plates | " + Anchors.Count + " anchors"; }
    }
}


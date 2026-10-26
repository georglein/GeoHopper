using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Conservative eligibility checks for the first dry automatic-K0 implementation.</summary>
    internal static class InitialStateRules
    {
        /// <summary>Requires a filled rectangular initial domain, rectangular partitions and horizontally uniform soil layers.</summary>
        internal static void ValidateK0(IList<RegionData> regions,PhaseData initial)
        {
            if(initial.Regions.Count!=regions.Count || regions.Any(r=>!initial.Regions.Contains(r.Id,StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("K0 procedure requires all soil regions active initially; excavate in a later phase.");
            double minX=regions.Min(r=>r.Points.Min(p=>p.X)),maxX=regions.Max(r=>r.Points.Max(p=>p.X));
            double minY=regions.Min(r=>r.Points.Min(p=>p.Y)),maxY=regions.Max(r=>r.Points.Max(p=>p.Y));
            foreach(var r in regions)
            {
                if(!r.Material.MohrCoulomb) throw new ArgumentException("K0 procedure currently requires drained Mohr-Coulomb soil regions only.");
                double x0=r.Points.Min(p=>p.X),x1=r.Points.Max(p=>p.X),y0=r.Points.Min(p=>p.Y),y1=r.Points.Max(p=>p.Y);
                if(Math.Abs(r.Area-(x1-x0)*(y1-y0))>Rules.Tolerance*Math.Max(1,(x1-x0)+(y1-y0)))
                    throw new ArgumentException("K0 procedure currently requires axis-aligned rectangular soil partitions.");
            }
            if(Math.Abs(regions.Sum(r=>r.Area)-(maxX-minX)*(maxY-minY))>Rules.Tolerance*Math.Max(1,(maxX-minX)+(maxY-minY)))
                throw new ArgumentException("K0 initial soil must fill the rectangular domain without holes or steps.");
            for(int i=0;i<regions.Count;i++) for(int j=i+1;j<regions.Count;j++)
            {
                var a=regions[i]; var b=regions[j];
                double overlap=Math.Min(a.Points.Max(p=>p.Y),b.Points.Max(p=>p.Y))-Math.Max(a.Points.Min(p=>p.Y),b.Points.Min(p=>p.Y));
                // Compare mechanical commands without IDs; display colour is irrelevant to initial stress.
                string ma=a.Material.Command().Replace(Rules.Quote(a.Material.Id),"\"Soil\"");
                string mb=b.Material.Command().Replace(Rules.Quote(b.Material.Id),"\"Soil\"");
                if(overlap>Rules.Tolerance && ma!=mb)
                    throw new ArgumentException("K0 procedure requires horizontally uniform materials at every elevation.");
            }
        }
    }
}

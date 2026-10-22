using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoHopper
{
    /// <summary>Rhino-independent polygon validation and overlap checks in canonical metres.</summary>
    internal static class Geometry2D
    {
        /// <summary>Returns the signed oriented area using coordinates relative to the first vertex.</summary>
        internal static double SignedArea(IList<XY> p)
        {
            double sum = 0;
            for (int i = 1; i < p.Count - 1; i++) sum += Cross(p[0], p[i], p[i + 1]);
            return sum / 2;
        }
        /// <summary>Returns twice the oriented area of a triangle.</summary>
        private static double Cross(XY a, XY b, XY c) { return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X); }
        /// <summary>Returns the Euclidean distance between two coordinates.</summary>
        private static double Distance(XY a, XY b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
        /// <summary>Checks whether a point is on a finite edge within the geometric tolerance.</summary>
        private static bool OnEdge(XY a, XY b, XY p)
        {
            return Math.Abs(Cross(a,b,p)) <= Rules.Tolerance * Distance(a,b) &&
                p.X >= Math.Min(a.X,b.X)-Rules.Tolerance && p.X <= Math.Max(a.X,b.X)+Rules.Tolerance &&
                p.Y >= Math.Min(a.Y,b.Y)-Rules.Tolerance && p.Y <= Math.Max(a.Y,b.Y)+Rules.Tolerance;
        }
        /// <summary>Detects crossings and non-adjacent touches, including collinear intersections.</summary>
        private static bool Intersects(XY a, XY b, XY c, XY d)
        {
            if (OnEdge(a,b,c) || OnEdge(a,b,d) || OnEdge(c,d,a) || OnEdge(c,d,b)) return true;
            return (Cross(a,b,c)>0)!=(Cross(a,b,d)>0) && (Cross(c,d,a)>0)!=(Cross(c,d,b)>0);
        }
        /// <summary>Rejects degeneracy, self-intersection, repeated vertices and unsupported polygon sizes.</summary>
        internal static void Validate(IList<XY> p)
        {
            if (p.Count < 3 || p.Count > 200) throw new ArgumentException("A region needs 3 to 200 distinct polygon vertices.");
            for (int i = 0; i < p.Count; i++)
            {
                Rules.Range(p[i].X, -1e6, 1e6, "X [m]"); Rules.Range(p[i].Y, -1e6, 1e6, "Y [m]");
                for (int j = i + 1; j < p.Count; j++)
                    if (Distance(p[i],p[j]) <= Rules.Tolerance)
                        throw new ArgumentException("Polygon contains duplicate or indistinguishable vertices.");
                XY a = p[(i+p.Count-1)%p.Count], b = p[i], c = p[(i+1)%p.Count];
                if (Math.Abs(Cross(a,b,c)) <= Rules.Tolerance * Distance(a,c) &&
                    (a.X-b.X)*(c.X-b.X)+(a.Y-b.Y)*(c.Y-b.Y) > 0)
                    throw new ArgumentException("Polygon contains a backtracking edge.");
                for (int j = i + 1; j < p.Count; j++)
                    if (j != i+1 && !(i == 0 && j == p.Count-1) &&
                        Intersects(p[i], p[(i+1)%p.Count], p[j], p[(j+1)%p.Count]))
                        throw new ArgumentException("Polygon self-intersects or touches itself.");
            }
            if (Math.Abs(SignedArea(p)) < 1e-10) throw new ArgumentException("Polygon area is too small.");
        }
        /// <summary>Tests inclusive membership of a counter-clockwise triangle.</summary>
        private static bool InTriangle(XY p, XY a, XY b, XY c)
        {
            return Cross(a,b,p)>=-1e-12 && Cross(b,c,p)>=-1e-12 && Cross(c,a,p)>=-1e-12;
        }
        /// <summary>Ear-clips a validated polygon; redundant straight vertices are discarded first.</summary>
        internal static List<XY[]> Triangles(IList<XY> input)
        {
            var p = input.ToList();
            if (SignedArea(p)<0) p.Reverse();
            bool removed = true;
            while (removed && p.Count>3)
            {
                removed = false;
                for (int i=0;i<p.Count;i++)
                    if (Math.Abs(Cross(p[(i+p.Count-1)%p.Count],p[i],p[(i+1)%p.Count]))<1e-12)
                    { p.RemoveAt(i); removed=true; break; }
            }
            var result = new List<XY[]>();
            while(p.Count>3)
            {
                bool clipped = false;
                for(int i=0;i<p.Count;i++)
                {
                    int before=(i+p.Count-1)%p.Count, after=(i+1)%p.Count;
                    XY a=p[before], b=p[i], c=p[after];
                    if(Cross(a,b,c)<=1e-12) continue;
                    if(p.Where((x,j)=>j!=before && j!=i && j!=after).Any(x=>InTriangle(x,a,b,c))) continue;
                    result.Add(new[]{a,b,c}); p.RemoveAt(i); clipped=true; break;
                }
                if(!clipped) throw new ArgumentException("Polygon cannot be triangulated reliably; simplify its contour.");
            }
            result.Add(p.ToArray()); return result;
        }
        /// <summary>Clips one convex polygon against a directed half-plane.</summary>
        private static List<XY> Clip(List<XY> input, XY a, XY b)
        {
            var output=new List<XY>();
            if(input.Count==0) return output;
            XY previous=input[input.Count-1]; double dp=Cross(a,b,previous);
            foreach(XY current in input)
            {
                double dc=Cross(a,b,current);
                if((dc>=0)!=(dp>=0))
                {
                    double t=dp/(dp-dc);
                    output.Add(new XY(previous.X+t*(current.X-previous.X), previous.Y+t*(current.Y-previous.Y)));
                }
                if(dc>=0) output.Add(current);
                previous=current; dp=dc;
            }
            return output;
        }
        /// <summary>Detects positive-area overlap, including containment and identical polygons; shared edges are valid.</summary>
        internal static bool Overlaps(IList<XY> a, IList<XY> b)
        {
            if(a.Max(p=>p.X)<=b.Min(p=>p.X) || b.Max(p=>p.X)<=a.Min(p=>p.X) ||
               a.Max(p=>p.Y)<=b.Min(p=>p.Y) || b.Max(p=>p.Y)<=a.Min(p=>p.Y)) return false;
            var ta=Triangles(a); var tb=Triangles(b);
            foreach(var x in ta) foreach(var y in tb)
            {
                var clipped=x.ToList();
                for(int i=0;i<3;i++) clipped=Clip(clipped,y[i],y[(i+1)%3]);
                if(clipped.Count>=3 && Math.Abs(SignedArea(clipped))>1e-10) return true;
            }
            return false;
        }
    }
}


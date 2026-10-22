using System;
using System.Collections.Generic;
using System.Drawing;

namespace GeoHopper
{
    /// <summary>Loads embedded presentation-only icons independently of model and connection logic.</summary>
    internal static class ComponentIcons
    {
        private static readonly Dictionary<string,Bitmap> Cache=new Dictionary<string,Bitmap>();
        /// <summary>Returns the cached 24-pixel icon mapped to the component class, or the default icon if unavailable.</summary>
        internal static Bitmap Get(Type componentType)
        {
            string name="GeoHopper.Icons."+componentType.Name+".png";
            lock(Cache)
            {
                Bitmap icon;
                if(Cache.TryGetValue(name,out icon)) return icon;
                using(var stream=typeof(ComponentIcons).Assembly.GetManifestResourceStream(name))
                {
                    if(stream==null) return null;
                    // Detach the cached bitmap from the resource stream before disposing it.
                    using(var source=new Bitmap(stream)) icon=new Bitmap(source);
                }
                Cache.Add(name,icon);
                return icon;
            }
        }
    }
}

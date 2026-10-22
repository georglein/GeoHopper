using System.Reflection;

[assembly: AssemblyTitle("GeoHopper")]
[assembly: AssemblyDescription("Direct Grasshopper to PLAXIS integration")]
[assembly: AssemblyVersion(GeoHopper.VersionInfo.Number)]
[assembly: AssemblyFileVersion(GeoHopper.VersionInfo.Number)]

namespace GeoHopper
{
    /// <summary>Single version source for compiled metadata and the plugin description.</summary>
    public static class VersionInfo
    {
        /// <summary>Four-part .NET version. Increase for every published user-facing release.</summary>
        public const string Number = "0.5.0.0";
    }
}

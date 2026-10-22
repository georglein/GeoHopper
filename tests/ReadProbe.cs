using System;
using System.Web.Script.Serialization;
using GeoHopper;

/// <summary>Console host for protocol tests; never installed as part of the plugin.</summary>
internal static class ReadProbe
{
    /// <summary>Reads credentials from stdin and writes one JSON result; exit code indicates success.</summary>
    public static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        // Password enters through stdin, never through a process command line.
        string password = Console.ReadLine();
        var result = PlaxisClient.ReadProjectAsync(args[0], Int32.Parse(args[1]), password).GetAwaiter().GetResult();
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Console.WriteLine(new JavaScriptSerializer().Serialize(result));
        return result.Connected ? 0 : 1;
    }
}

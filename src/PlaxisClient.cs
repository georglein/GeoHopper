using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace GeoHopper
{
    /// <summary>Snapshot of one read attempt; never represents a persistent server connection.</summary>
    public sealed class ReadResult
    {
        /// <summary>True only after the project title was successfully read and authenticated.</summary>
        public bool Connected;
        /// <summary>Unmodified project title, including an empty title when valid.</summary>
        public string Title = "";
        /// <summary>English diagnostic text, with the supplied password removed.</summary>
        public string Error = "";
    }

    /// <summary>
    /// Minimal read-only PLAXIS Input client, independent of Rhino and Grasshopper.
    /// Uses encrypted HTTP on loopback; does not launch Python or modify a project.
    /// </summary>
    public static class PlaxisClient
    {
        /// <summary>Reads Project.Title using two authenticated, independently timed HTTP requests.</summary>
        /// <param name="host">Localhost or a loopback IP address.</param>
        /// <param name="port">PLAXIS Input remote scripting port, from 1 to 65535.</param>
        /// <param name="password">Server password, 4 to 56 UTF-8 bytes; never persisted by this client.</param>
        /// <returns>A success snapshot or a failure with an empty title. Errors are returned as data.</returns>
        public static async Task<ReadResult> ReadProjectAsync(string host, int port, string password)
        {
            try
            {
                host = (host ?? "").Trim();
                IPAddress address;
                if (!String.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                    (!IPAddress.TryParse(host, out address) || !IPAddress.IsLoopback(address)))
                    throw new InvalidOperationException("This connection test accepts only localhost or a loopback IP address.");
                if (port < 1 || port > 65535)
                    throw new InvalidOperationException("Port must be between 1 and 65535.");
                if (String.IsNullOrEmpty(password))
                    throw new InvalidOperationException("Enter the PLAXIS scripting server password. This test requires encryption.");
                int keyLength = Encoding.UTF8.GetByteCount(password);
                if (keyLength < 4 || keyLength > 56)
                    throw new InvalidOperationException("The password must contain between 4 and 56 UTF-8 bytes for this test.");

                using (var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
                using (var http = new HttpClient(handler))
                {
                    http.BaseAddress = new UriBuilder("http", host, port).Uri;
                    http.Timeout = TimeSpan.FromSeconds(3);
                    var named = await PostAsync(http, "namedobjects", Map("namedobjects", new[] { "Project" }), password).ConfigureAwait(false);
                    var project = ObjectAt(ObjectAt(named, "namedobjects"), "Project");
                    object success;
                    if (!project.TryGetValue("success", out success) || !(success is bool) || !(bool)success)
                        throw new InvalidOperationException("PLAXIS did not return a Project object. Open a project in PLAXIS Input and use its Input port.");
                    string guid = StringAt(ObjectAt(project, "returnedobject"), "guid");
                    Guid parsedGuid;
                    if (!Guid.TryParse(guid, out parsedGuid))
                        throw new InvalidOperationException("Project has no valid object identifier.");

                    var query = Map("owner", guid, "propertyname", "Title", "phaseguid", "");
                    var values = await PostAsync(http, "propertyvalues", Map("propertyvalues", query), password).ConfigureAwait(false);
                    var properties = ObjectAt(ObjectAt(ObjectAt(values, "queries"), guid), "properties");
                    object title;
                    if (!properties.TryGetValue("Title", out title))
                        throw new InvalidOperationException("PLAXIS did not return the Project.Title property.");
                    // Some protocol versions wrap primitive values in a typed object.
                    var typed = title as Dictionary<string, object>;
                    if (typed != null && !typed.TryGetValue("value", out title))
                        throw new InvalidOperationException("Unknown response format for Project.Title.");
                    if (!(title is string))
                        throw new InvalidOperationException("Project.Title was not returned as text.");
                    return new ReadResult { Connected = true, Title = (string)title };
                }
            }
            catch (TaskCanceledException)
            {
                return new ReadResult { Error = "Request timed out (3 seconds per request). Check that the server is running and PLAXIS is responsive." };
            }
            catch (HttpRequestException)
            {
                return new ReadResult { Error = "PLAXIS server is unreachable. Check remote scripting in PLAXIS Input and the port." };
            }
            catch (Exception ex)
            {
                return new ReadResult { Error = SafeError(ex.Message, password) };
            }
        }

        /// <summary>Encrypts one action, posts it, decrypts the response and verifies its unique reply code.</summary>
        internal static async Task<Dictionary<string, object>> PostAsync(HttpClient http, string endpoint,
            Dictionary<string, object> action, string password)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            string replyCode = Guid.NewGuid().ToString("N");
            string clear = serializer.Serialize(Map("action", action, "ReplyCode", replyCode));
            byte[] iv = new byte[8];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(iv);
            string body = serializer.Serialize(Map("Code", Convert.ToBase64String(iv),
                "RequestData", Encrypt(clear, password, iv)));

            using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync(endpoint, content).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("PLAXIS returned HTTP " + (int)response.StatusCode +
                        ". Check the password, Input port and server state.");
                string raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                string mediaType=response.Content.Headers.ContentType==null ? "unspecified" : response.Content.Headers.ContentType.MediaType;
                // The vendor wrapper does not decrypt non-JSON environment acknowledgements.
                // Only environment actions allow this; command/data replies still require
                // encrypted JSON and a matching reply code.
                if (endpoint == "environment" && (String.IsNullOrWhiteSpace(raw) ||
                    mediaType.IndexOf("json",StringComparison.OrdinalIgnoreCase)<0)) return Map();
                Dictionary<string, object> decoded;
                string decodingStage="JSON envelope parsing";
                try
                {
                    var envelope = serializer.DeserializeObject(raw) as Dictionary<string, object>;
                    decodingStage="encrypted payload decoding";
                    string plaintext = Decrypt(StringAt(envelope, "Response"), password,
                        Convert.FromBase64String(StringAt(envelope, "Code")));
                    decodingStage="decrypted JSON parsing";
                    decoded = serializer.DeserializeObject(plaintext) as Dictionary<string, object>;
                }
                catch (Exception)
                {
                    throw new InvalidOperationException("PLAXIS /"+endpoint+" response failed at "+decodingStage+
                        " (HTTP "+(int)response.StatusCode+", Content-Type: "+mediaType+"). Check server protocol compatibility and credentials.");
                }
                if (!String.Equals(StringAt(decoded, "ReplyCode"), replyCode, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reply code does not match the request. Response rejected.");
                return decoded;
            }
        }

        /// <summary>Encodes UTF-8 JSON using PLAXIS space padding and Blowfish-CBC, then Base64.</summary>
        private static string Encrypt(string text, string password, byte[] iv)
        {
            byte[] source = Encoding.UTF8.GetBytes(text);
            // PLAXIS uses space padding, including a full block for aligned messages.
            byte[] padded = new byte[source.Length + 8 - source.Length % 8];
            Buffer.BlockCopy(source, 0, padded, 0, source.Length);
            for (int i = source.Length; i < padded.Length; i++) padded[i] = 0x20;
            return Convert.ToBase64String(Transform(padded, password, iv, true));
        }

        /// <summary>Validates block sizes and reverses PLAXIS encryption with strict UTF-8 decoding.</summary>
        private static string Decrypt(string text, string password, byte[] iv)
        {
            byte[] bytes = Convert.FromBase64String(text);
            if (iv.Length != 8 || bytes.Length == 0 || bytes.Length % 8 != 0)
                throw new InvalidOperationException("Invalid encrypted response.");
            return new UTF8Encoding(false, true).GetString(Transform(bytes, password, iv, false)).TrimEnd();
        }

        /// <summary>Processes complete eight-byte Blowfish blocks with a fresh cipher for each message.</summary>
        private static byte[] Transform(byte[] input, string password, byte[] iv, bool encrypt)
        {
            byte[] key = Encoding.UTF8.GetBytes(password);
            var cipher = new CbcBlockCipher(new BlowfishEngine());
            cipher.Init(encrypt, new ParametersWithIV(new KeyParameter(key), iv));
            byte[] output = new byte[input.Length];
            for (int i = 0; i < input.Length; i += 8) cipher.ProcessBlock(input, i, output, i);
            return output;
        }

        /// <summary>Builds a small protocol object from alternating string keys and values.</summary>
        internal static Dictionary<string, object> Map(params object[] entries)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < entries.Length; i += 2) result.Add((string)entries[i], entries[i + 1]);
            return result;
        }

        /// <summary>Reads a required JSON object field, failing explicitly on protocol changes.</summary>
        internal static Dictionary<string, object> ObjectAt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || !(value is Dictionary<string, object>))
                throw new InvalidOperationException("Unexpected PLAXIS response: missing object field " + key + ".");
            return (Dictionary<string, object>)value;
        }

        /// <summary>Reads a required JSON string field without silently converting other value types.</summary>
        internal static string StringAt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || !(value is string))
                throw new InvalidOperationException("Unexpected PLAXIS response: missing text field " + key + ".");
            return (string)value;
        }

        /// <summary>Removes accidental password occurrences from exception messages before display.</summary>
        private static string SafeError(string message, string password)
        {
            return String.IsNullOrEmpty(password) ? message : message.Replace(password, "[password]");
        }
    }
}

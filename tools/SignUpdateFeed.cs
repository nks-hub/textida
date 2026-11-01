// Usage: dotnet run tools/SignUpdateFeed.cs -- PACKAGE_DIR VERSION RELEASE_URL
// FEED_SIGNING_KEY contains a PEM ECDSA P-256 private key; it is never written to disk.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

if (args.Length != 3) { Console.Error.WriteLine("Usage: SignUpdateFeed PACKAGE_DIR VERSION RELEASE_URL"); return 2; }
var secret = Environment.GetEnvironmentVariable("FEED_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(secret)) { Console.Error.WriteLine("FEED_SIGNING_KEY is required to sign the update feed."); return 2; }
using var key = ECDsa.Create();
key.ImportFromPem(secret);
if (key.KeySize != 256) throw new CryptographicException("The update feed requires an ECDSA P-256 key.");
var folder = Path.GetFullPath(args[0]);
var version = args[1];
var releases = new JsonArray();
foreach (var rid in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64" })
{
    var name = $"Textida-{version}-{rid}" + (rid.StartsWith("linux-") ? ".tar.gz" : ".zip");
    var file = Path.Combine(folder, name);
    if (!File.Exists(file))
    {
        if (rid.StartsWith("linux-")) continue;
        throw new FileNotFoundException("An update package is missing.", name);
    }
    using var stream = File.OpenRead(file);
    releases.Add((JsonNode)new JsonObject { ["version"] = version, ["rid"] = rid, ["url"] = args[2].TrimEnd('/') + "/" + name,
        ["sha256"] = Convert.ToHexString(SHA256.HashData(stream)), ["size"] = stream.Length });
}
var payload = new JsonObject { ["releases"] = releases }.ToJsonString();
var signature = key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256);
var feed = new JsonObject { ["format"] = 1, ["payload"] = payload, ["signature"] = Convert.ToBase64String(signature) };
File.WriteAllText(Path.Combine(folder, "feed.json"), feed.ToJsonString());
Console.WriteLine($"Signed update feed with {releases.Count} packages.");
return 0;

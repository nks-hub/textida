// Usage: dotnet run tools/SignUpdateFeed.cs -- PACKAGE_DIR VERSION RELEASE_URL [PUBLIC_KEY_BASE64]
//        dotnet run tools/SignUpdateFeed.cs -- --resign FEED_JSON [PUBLIC_KEY_BASE64]
// FEED_SIGNING_KEY contains a PEM ECDSA P-256 private key; it is never written to disk.
// --resign signs the releases of a feed the trusted key signed before with a new issue and expiry date, so the feed stays valid between releases.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

bool resign = args.Length > 0 && args[0] == "--resign";
if (resign ? args.Length is not (2 or 3) : args.Length is not (3 or 4))
{
    Console.Error.WriteLine("Usage: SignUpdateFeed PACKAGE_DIR VERSION RELEASE_URL [PUBLIC_KEY_BASE64]\n       SignUpdateFeed --resign FEED_JSON [PUBLIC_KEY_BASE64]");
    return 2;
}
var secret = Environment.GetEnvironmentVariable("FEED_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(secret)) { Console.Error.WriteLine("FEED_SIGNING_KEY is required to sign the update feed."); return 2; }
using var key = ECDsa.Create();
key.ImportFromPem(secret);
if (key.KeySize != 256) throw new CryptographicException("The update feed requires an ECDSA P-256 key.");
var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var source = Path.Combine(tools, "..", "src", "NksHub.Textida.App", "Services", "AppUpdater.cs");
int keyArg = resign ? 2 : 3;
var expected = args.Length > keyArg ? args[keyArg] : Regex.Match(File.ReadAllText(source), "DefaultPublicKey\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
using var verifier = ECDsa.Create();
verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(expected), out _);
if (!key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(verifier.ExportSubjectPublicKeyInfo()))
    throw new CryptographicException("FEED_SIGNING_KEY does not match the public key trusted by this build.");
JsonArray releases;
string output;
if (resign)
{
    // only what the trusted key signed is signed again: a feed edited by hand, or signed by another key, is refused
    output = Path.GetFullPath(args[1]);
    var old = JsonNode.Parse(File.ReadAllText(output))!.AsObject();
    var oldPayload = (string)old["payload"]!;
    if (!verifier.VerifyData(Encoding.UTF8.GetBytes(oldPayload), Convert.FromBase64String((string)old["signature"]!), HashAlgorithmName.SHA256))
        throw new CryptographicException("The feed to sign again is not signed by the trusted public key.");
    releases = (JsonArray)JsonNode.Parse(oldPayload)!["releases"]!.AsArray().DeepClone();
    if (releases.Count == 0) throw new InvalidDataException("The feed to sign again lists no release.");
}
else
{
    var folder = Path.GetFullPath(args[0]);
    output = Path.Combine(folder, "feed.json");
    var version = args[1];
    releases = new JsonArray();
    foreach (var rid in new[] { "win-x64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64" })
    {
        var name = $"Textida-{version}-{rid}" + (rid.StartsWith("linux-") ? ".tar.gz" : ".zip");
        var file = Path.Combine(folder, name);
        if (!File.Exists(file)) throw new FileNotFoundException("An update package is missing.", name);
        using var stream = File.OpenRead(file);
        releases.Add((JsonNode)new JsonObject { ["version"] = version, ["rid"] = rid, ["url"] = args[2].TrimEnd('/') + "/" + name,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(stream)), ["size"] = stream.Length });
    }
}
// a replayed old feed holds an installation back for at most this long; a new release (or a new signing of the feed) must come within it
const int FeedLifetimeDays = 90;
var issued = DateTimeOffset.UtcNow;
// Textida 0.1.4 and older read only "releases" from the signed text, so the dates do not break them
var payload = new JsonObject { ["issued"] = issued.ToUnixTimeSeconds(), ["expires"] = issued.AddDays(FeedLifetimeDays).ToUnixTimeSeconds(), ["releases"] = releases }.ToJsonString();
var signature = key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256);
if (!verifier.VerifyData(Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256))
    throw new CryptographicException("The signed feed cannot be verified by the trusted public key.");
var feed = new JsonObject { ["format"] = 1, ["payload"] = payload, ["signature"] = Convert.ToBase64String(signature) };
File.WriteAllText(output, feed.ToJsonString());
Console.WriteLine($"Signed update feed with {releases.Count} packages, valid until {issued.AddDays(FeedLifetimeDays):yyyy-MM-dd}.");
return 0;

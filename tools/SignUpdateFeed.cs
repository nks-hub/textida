// Usage: dotnet run tools/SignUpdateFeed.cs -- PACKAGE_DIR VERSION RELEASE_URL [PUBLIC_KEY_BASE64]
// FEED_SIGNING_KEY contains a PEM ECDSA P-256 private key; it is never written to disk.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length is not (3 or 4)) { Console.Error.WriteLine("Usage: SignUpdateFeed PACKAGE_DIR VERSION RELEASE_URL [PUBLIC_KEY_BASE64]"); return 2; }
var secret = Environment.GetEnvironmentVariable("FEED_SIGNING_KEY");
if (string.IsNullOrWhiteSpace(secret)) { Console.Error.WriteLine("FEED_SIGNING_KEY is required to sign the update feed."); return 2; }
using var key = ECDsa.Create();
key.ImportFromPem(secret);
if (key.KeySize != 256) throw new CryptographicException("The update feed requires an ECDSA P-256 key.");
var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var source = Path.Combine(tools, "..", "src", "NksHub.Textida.App", "Services", "AppUpdater.cs");
var expected = args.Length == 4 ? args[3] : Regex.Match(File.ReadAllText(source), "DefaultPublicKey\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
using var verifier = ECDsa.Create();
verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(expected), out _);
if (!key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(verifier.ExportSubjectPublicKeyInfo()))
    throw new CryptographicException("FEED_SIGNING_KEY does not match the public key trusted by this build.");
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
if (!verifier.VerifyData(Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256))
    throw new CryptographicException("The signed feed cannot be verified by the trusted public key.");
var feed = new JsonObject { ["format"] = 1, ["payload"] = payload, ["signature"] = Convert.ToBase64String(signature) };
File.WriteAllText(Path.Combine(folder, "feed.json"), feed.ToJsonString());
Console.WriteLine($"Signed update feed with {releases.Count} packages.");
return 0;

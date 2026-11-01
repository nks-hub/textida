using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.PluginHost.Manager;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>PZ.8.1: the index of the plugin catalog is read, and a hash or signature that does not match is found.</summary>
public class PluginCatalogTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static string PublicKey => Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo());

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Index(string entryJson, string publishers = "") =>
        "{\"schemaVersion\":1,\"publishers\":[" + (publishers.Length > 0 ? publishers : "{\"id\":\"acme\",\"name\":\"Acme\",\"publicKey\":\"" + PublicKey + "\"}") + "],\"plugins\":[" + entryJson + "]}";

    private static string Entry(string id = "acme.clips", string version = "1.0.0", string package = "packages/clips.zip", string sha = "", string[]? permissions = null, string? signature = null)
    {
        sha = sha.Length > 0 ? sha : Hash("zip");
        permissions ??= ["document.read"];
        signature ??= PluginCatalog.Sign(Key, id, version, sha, permissions);
        return "{\"id\":\"" + id + "\",\"name\":\"Clips\",\"version\":\"" + version + "\",\"publisher\":\"acme\",\"package\":\"" + package + "\",\"sha256\":\"" + sha + "\",\"permissions\":[" + string.Join(",", permissions.Select(p => "\"" + p + "\"")) + "],\"signature\":\"" + signature + "\"}";
    }

    [Fact]
    public void An_index_gives_publishers_and_entries_and_names_each_entry_it_skips()
    {
        var index = PluginCatalog.Parse(Index(Entry() + "," + Entry("Bad Id") + "," + Entry(package: "../x.zip") + "," + Entry(package: "C:/x.zip") + "," + Entry(sha: "123")));
        var entry = Assert.Single(index.Entries);
        Assert.Equal(("acme.clips", "1.0.0", "packages/clips.zip"), (entry.Id, entry.Version, entry.Package));
        Assert.Equal(["document.read"], entry.Permissions);
        Assert.Equal("Acme", Assert.Single(index.Publishers).Name);
        Assert.Equal(4, index.Problems.Count);
        Assert.Contains(index.Problems, p => p.Contains("Bad Id"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":2,\"plugins\":[]}")]
    [InlineData("[1]")]
    [InlineData("{\"schemaVersion\":\"x\"}")]
    public void Text_that_is_no_index_gives_no_entries_and_a_problem(string text)
    {
        var index = PluginCatalog.Parse(text);
        Assert.Empty(index.Entries);
        Assert.NotEmpty(index.Problems);
    }

    [Theory]
    [InlineData("packages/a.zip", true)]
    [InlineData("a.zip", true)]
    [InlineData("https://example.test/a.zip", true)]
    [InlineData("http://example.test/a.zip", false)]
    [InlineData("https://user:pw@example.test/a.zip", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("../a.zip", false)]
    [InlineData("a/../../b.zip", false)]
    [InlineData("/a.zip", false)]
    [InlineData("C:/a.zip", false)]
    [InlineData("a\\b.zip", false)]
    [InlineData("a/b.txt", false)]
    [InlineData("a//b.zip", false)]
    [InlineData("", false)]
    public void A_package_is_a_zip_inside_the_repository_or_an_https_address(string package, bool safe) => Assert.Equal(safe, PluginCatalog.IsSafePackage(package));

    [Fact]
    public void The_signature_holds_for_the_publisher_and_breaks_when_anything_it_covers_changes()
    {
        var index = PluginCatalog.Parse(Index(Entry(permissions: ["document.read", "network"])));
        var entry = index.Entries.Single();
        var publisher = index.Publishers.Single();
        Assert.Equal(CatalogCheck.Ok, PluginCatalog.CheckSignature(entry, publisher));
        Assert.Equal(CatalogCheck.UnknownPublisher, PluginCatalog.CheckSignature(entry, null));

        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry with { Version = "1.0.1" }, publisher));
        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry with { Id = "acme.other" }, publisher));
        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry with { Sha256 = Hash("other") }, publisher));
        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry with { Permissions = ["document.read", "network", "workspace.files"] }, publisher));
        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry with { Permissions = ["document.read"] }, publisher));
        Assert.Equal(CatalogCheck.Ok, PluginCatalog.CheckSignature(entry with { Permissions = ["network", "document.read"] }, publisher)); // the order is not part of the signature

        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(CatalogCheck.BadSignature, PluginCatalog.CheckSignature(entry, publisher with { PublicKey = Convert.ToBase64String(stranger.ExportSubjectPublicKeyInfo()) }));
        Assert.Equal(CatalogCheck.BadKey, PluginCatalog.CheckSignature(entry, publisher with { PublicKey = "!!!" }));
        Assert.Equal(CatalogCheck.BadKey, PluginCatalog.CheckSignature(entry with { Signature = "!!!" }, publisher));
        using var big = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Equal(CatalogCheck.BadKey, PluginCatalog.CheckSignature(entry, publisher with { PublicKey = Convert.ToBase64String(big.ExportSubjectPublicKeyInfo()) }));
    }

    [Fact]
    public void The_hash_of_the_file_has_to_be_the_one_in_the_index()
    {
        string file = Path.Combine(T.TempDir(), "p.zip");
        File.WriteAllText(file, "zip");
        var entry = PluginCatalog.Parse(Index(Entry())).Entries.Single();
        Assert.Equal(CatalogCheck.Ok, PluginCatalog.CheckHash(entry, file));
        File.WriteAllText(file, "zip!");
        Assert.Equal(CatalogCheck.BadHash, PluginCatalog.CheckHash(entry, file));
    }

    [Fact]
    public void A_fingerprint_is_the_key_hash_in_groups_of_four_and_versions_compare_as_numbers()
    {
        var publisher = PluginCatalog.Parse(Index(Entry())).Publishers.Single();
        Assert.Matches("^([0-9A-F]{4} ){15}[0-9A-F]{4}$", publisher.Fingerprint);
        Assert.Equal("", (publisher with { PublicKey = "!!!" }).Fingerprint);
        Assert.True(PluginCatalog.IsNewer("1.10.0", "1.9.0"));
        Assert.False(PluginCatalog.IsNewer("1.0.0", "1.0.0"));
        Assert.False(PluginCatalog.IsNewer("0.9", "1.0"));
        Assert.True(PluginCatalog.IsNewer("2.0-beta", "1.0-beta"));
    }
}

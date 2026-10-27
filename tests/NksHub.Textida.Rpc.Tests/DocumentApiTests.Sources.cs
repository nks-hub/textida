using System.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P6.2.1 and P6.2.3: a plugin serves a fake scheme from memory; the editor reads with revisions, caches, and writes back against the revision it read.</summary>
public partial class DocumentApiTests
{
    const string Page = "fake://wiki/page";

    static DocumentApiOptions WithSources(DocumentApiOptions o, int cacheEntries = 8, long cacheBytes = 1 << 20) =>
        o with { Sources = new FileSourceOptions(TimeSpan.FromSeconds(5), 1 << 20, 100, 2, cacheEntries, cacheBytes, 16, 20) };

    static async Task<Rig> SourceRigAsync(bool atomic = true, int cacheEntries = 8, long cacheBytes = 1 << 20, string[]? plugins = null, string extra = "")
    {
        var rig = await Rig.CreateAsync(Utf8("x"), o => WithSources(o, cacheEntries, cacheBytes), plugins: plugins);
        rig.Permissions.Grant("p", DocumentPermissions.Source, GrantScope.All);
        var ok = await rig.CallAsync(DocumentMethods.SourceRegister, $"{{\"scheme\":\"fake\",\"name\":\"Fake wiki\",\"list\":true,\"write\":true,\"atomicWrite\":{(atomic ? "true" : "false")}{extra}}}");
        Assert.True(ok.GetProperty("enabled").GetBoolean(), ok.GetRawText());
        await rig.Host.CallAsync("p", "sourceMode", T.Json($"{{\"atomic\":{(atomic ? "true" : "false")}}}"));
        return rig;
    }

    static Task Put(Rig rig, string uri, string text) => rig.Host.CallAsync("p", "sourcePut", T.Json($"{{\"uri\":\"{uri}\",\"text\":\"{text}\"}}"));

    static Task Mode(Rig rig, bool offline, bool atomic = true) => rig.Host.CallAsync("p", "sourceMode", T.Json($"{{\"offline\":{(offline ? "true" : "false")},\"atomic\":{(atomic ? "true" : "false")}}}"));

    static async Task<string> Remote(Rig rig, string uri) =>
        (await rig.Host.CallAsync("p", "sourceGet", T.Json($"{{\"uri\":\"{uri}\"}}"))).GetProperty("text").GetString()!;

    [Fact]
    public async Task APluginRegistersASchemeWithItsCapabilitiesAndOnlyWithTheGrant()
    {
        await using var rig = await SourceRigAsync(atomic: false, plugins: ["p", "q"]);
        var source = Assert.Single(rig.Api.Sources);
        Assert.Equal(("p", "fake", "Fake wiki", new SourceCapabilities(true, true, false)), (source.PluginId, source.Scheme, source.Name, source.Capabilities));
        Assert.Same(source, rig.Api.SourceFor("fake://anything"));
        Assert.Null(rig.Api.SourceFor(@"C:\work\a.txt"));

        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"fake\"}", DocumentErrorCodes.PermissionDenied, "q");
        rig.Permissions.Grant("q", DocumentPermissions.Source, GrantScope.All);
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"fake\"}", DocumentErrorCodes.SourceSchemeTaken, "q");
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"file\"}", RpcErrorCodes.InvalidParams, "q");
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"x\",\"atomicWrite\":true}", RpcErrorCodes.InvalidParams, "q");

        rig.Permissions.Revoke("p", DocumentPermissions.Source, GrantScope.All);
        Assert.Empty(rig.Api.Sources);
    }

    [Fact]
    public async Task ASourceThatSearchesStatesItsVersionsAndFindsFilesByTheirWords()
    {
        await using var rig = await SourceRigAsync(extra: ",\"search\":true,\"adapterVersion\":\"1.2.3\",\"serviceVersion\":\"API v1\"");
        var source = rig.Api.SourceFor(Page)!;
        Assert.Equal(("1.2.3", "API v1", true), (source.AdapterVersion, source.ServiceVersion, source.Capabilities.Search));
        await Put(rig, Page, "the quick brown fox");
        await Put(rig, "fake://wiki/other", "a lazy dog");

        Assert.Equal([Page], (await rig.Api.SourceSearchAsync("fake", "  QUICK ")).Select(e => e.Uri));
        Assert.Empty(await rig.Api.SourceSearchAsync("fake", "cat"));
        var empty = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceSearchAsync("fake", "   "));
        Assert.Equal(SourceFailure.Failed, empty.Kind);
        var tooLong = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceSearchAsync("fake", new string('a', 21)));
        Assert.Contains("1 to 20 characters", tooLong.Message);
    }

    [Fact]
    public async Task ASourceThatDoesNotSearchIsNotAskedToAndNamesNoVersions()
    {
        await using var rig = await SourceRigAsync();
        var source = rig.Api.SourceFor(Page)!;
        Assert.Equal((null, null, false), (source.AdapterVersion, source.ServiceVersion, source.Capabilities.Search));

        var e = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceSearchAsync("fake", "x"));

        Assert.Contains("does not search", e.Message);
    }

    [Fact]
    public async Task AVersionTextThatIsTooLongOrHoldsControlCharactersIsRefusedAtRegistration()
    {
        await using var rig = await SourceRigAsync();
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"other\",\"adapterVersion\":\"" + new string('v', 17) + "\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"other\",\"serviceVersion\":\"1\\n2\"}", RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SourceRegister, "{\"scheme\":\"other\",\"serviceVersion\":7}", RpcErrorCodes.InvalidParams);
        Assert.Single(rig.Api.Sources);
    }

    [Fact]
    public async Task ASourceServesStatListAndReadWithRevisions()
    {
        await using var rig = await SourceRigAsync();
        Assert.Null(await rig.Api.SourceStatAsync(Page));
        await Put(rig, Page, "one");
        await Put(rig, "fake://wiki/other", "two");

        var stat = await rig.Api.SourceStatAsync(Page);
        Assert.Equal(("r1", 3), (stat!.Revision, stat.Size));
        var read = await rig.Api.SourceReadAsync(Page);
        Assert.Equal(("one", "r1", false), (Encoding.UTF8.GetString(read.Content), read.Revision, read.FromCache));
        Assert.Equal([Page, "fake://wiki/other"], (await rig.Api.SourceListAsync("fake://wiki/")).Select(e => e.Uri));
        var missing = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceReadAsync("fake://wiki/none"));
        Assert.Equal(SourceFailure.NotFound, missing.Kind);
        var none = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceReadAsync("nobody://x"));
        Assert.Equal(SourceFailure.NoSource, none.Kind);
    }

    [Fact]
    public async Task ASaveWithTheRevisionThatWasReadWritesAndGivesTheNewRevision()
    {
        await using var rig = await SourceRigAsync();
        await Put(rig, Page, "one");
        var read = await rig.Api.SourceReadAsync(Page);

        var revision = await rig.Api.SourceWriteAsync(Page, Utf8("one edited"), read.Revision);
        Assert.Equal("r2", revision);
        Assert.Equal("one edited", await Remote(rig, Page));
        // the next save goes against the revision the last one returned
        Assert.Equal("r3", await rig.Api.SourceWriteAsync(Page, Utf8("again"), revision));
        // a file that does not exist yet is created with a null revision, and only once
        Assert.Equal("r1", await rig.Api.SourceWriteAsync("fake://wiki/new", Utf8("n"), null));
        var exists = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceWriteAsync("fake://wiki/new", Utf8("m"), null));
        Assert.Equal(SourceFailure.Conflict, exists.Kind);
    }

    [Fact]
    public async Task ASaveAfterARemoteChangeIsAConflictThatNamesTheCurrentRevisionAndWritesNothing()
    {
        await using var rig = await SourceRigAsync();
        await Put(rig, Page, "one");
        var read = await rig.Api.SourceReadAsync(Page);
        await Put(rig, Page, "changed by someone else");

        var conflict = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceWriteAsync(Page, Utf8("my edit"), read.Revision));
        Assert.Equal((SourceFailure.Conflict, "r2"), (conflict.Kind, conflict.CurrentRevision));
        Assert.Equal("changed by someone else", await Remote(rig, Page));
        // the cache still holds what was read, not what the refused write carried
        Assert.Equal("one", Encoding.UTF8.GetString(rig.Api.CachedSource(Page)!.Content));
    }

    [Fact]
    public async Task ASourceWithoutAtomicWriteIsCheckedByTheEditorFirstAndSaysThatItIsNotAtomic()
    {
        await using var rig = await SourceRigAsync(atomic: false);
        Assert.False(rig.Api.SourceFor(Page)!.Capabilities.AtomicWrite);
        await Put(rig, Page, "one");
        var read = await rig.Api.SourceReadAsync(Page);
        await Put(rig, Page, "changed");

        // the source itself would overwrite blindly; the editor's own stat check still refuses
        var conflict = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceWriteAsync(Page, Utf8("my edit"), read.Revision));
        Assert.Equal(SourceFailure.Conflict, conflict.Kind);
        Assert.Equal("changed", await Remote(rig, Page));
        Assert.Equal("r3", await rig.Api.SourceWriteAsync(Page, Utf8("my edit"), "r2"));
    }

    [Fact]
    public async Task WithoutAConnectionTheCachedCopyIsServedAndTheCacheKeepsToItsBounds()
    {
        await using var rig = await SourceRigAsync(cacheEntries: 2);
        foreach (var n in new[] { "a", "b", "c" }) await Put(rig, "fake://wiki/" + n, "text " + n);
        await rig.Api.SourceReadAsync("fake://wiki/a");
        await rig.Api.SourceReadAsync("fake://wiki/b");
        await rig.Api.SourceReadAsync("fake://wiki/a"); // a is now the most recent
        await rig.Api.SourceReadAsync("fake://wiki/c"); // evicts b
        await Mode(rig, offline: true);

        var a = await rig.Api.SourceReadAsync("fake://wiki/a");
        Assert.Equal(("text a", true), (Encoding.UTF8.GetString(a.Content), a.FromCache));
        Assert.NotNull(a.CachedAt);
        Assert.True((await rig.Api.SourceReadAsync("fake://wiki/c")).FromCache);
        var evicted = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceReadAsync("fake://wiki/b"));
        Assert.Equal(SourceFailure.Unavailable, evicted.Kind);
        var offlineWrite = await Assert.ThrowsAsync<SourceException>(() => rig.Api.SourceWriteAsync("fake://wiki/a", Utf8("x"), a.Revision));
        Assert.Equal(SourceFailure.Unavailable, offlineWrite.Kind);
    }

    [Fact]
    public void TheCacheEvictsByBytesAndDoesNotKeepAFileLargerThanItsBound()
    {
        var cache = new SourceCache(maxEntries: 10, maxBytes: 10);
        var now = DateTimeOffset.UnixEpoch;
        Assert.True(cache.Put("u1", new byte[6], "r", now));
        Assert.True(cache.Put("u2", new byte[6], "r", now));
        Assert.False(cache.Contains("u1"));
        Assert.Equal((1, 6), (cache.Count, cache.Bytes));
        Assert.False(cache.Put("u2", new byte[11], "r", now));
        Assert.Equal((0, 0), (cache.Count, cache.Bytes));
    }
}

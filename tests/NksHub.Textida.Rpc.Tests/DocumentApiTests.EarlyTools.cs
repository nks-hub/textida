using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P3b.3.5: symbols of a revision, reveal, snippets with carets and the Tab key, through a real plugin process.</summary>
public partial class DocumentApiTests
{
    internal sealed class RecordingTools : IEditorTools
    {
        public readonly List<(SymbolSet Set, bool Current)> Symbols = [];
        public readonly List<(string Plugin, Guid Document)> Cleared = [];
        public readonly List<ByteRange> Revealed = [];
        public readonly List<SelectionSet> Placed = [];
        public bool HasView = true;
        public string Eol = "\n";

        public void SymbolsChanged(SymbolSet set, bool current) { lock (Symbols) Symbols.Add((set, current)); }
        public void SymbolsCleared(string pluginId, Guid document) { lock (Cleared) Cleared.Add((pluginId, document)); }
        public bool Reveal(Guid document, ByteRange range) { if (HasView) Revealed.Add(range); return HasView; }
        public void PlaceSelections(Guid document, SelectionSet selections) => Placed.Add(selections);
        public string NewLine(Guid document) => Eol;
    }

    static string Symbol(string id, string name, long start, long end, string? parent = null) =>
        $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"kind\":\"function\",\"start\":{start},\"end\":{end}{(parent is null ? "" : $",\"parent\":\"{parent}\"")}}}";

    static string Publish(long revision, params string[] symbols) => $"\"revision\":{revision},\"symbols\":[{string.Join(",", symbols)}]";

    [Fact]
    public async Task SymbolsOfOneRevisionAreShownAndAnOlderAnswerIsNeverShownAsCurrent()
    {
        // "é" takes two bytes: the ranges below are bytes, not characters
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), codec: TextCodec.Utf8);
        var older = rig.Revision;

        var ok = await rig.CallAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(older, Symbol("a", "é1", 0, 3), Symbol("b", "f2", 4, 6, parent: "a"))));
        Assert.Equal(2, ok.GetProperty("symbols").GetInt32());
        var (set, current) = Assert.Single(rig.Tools.Symbols);
        Assert.True(current);
        Assert.Equal(older, set.Revision);
        Assert.Equal([new ByteRange(0, 3), new ByteRange(4, 6)], set.Symbols.Select(s => s.Range));
        Assert.Equal("a", set.Symbols[1].ParentId);

        // the document moves on: the set stays but is reported as outdated, once
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));
        Assert.Equal([(older, true), (older, false)], rig.Tools.Symbols.Select(x => (x.Set.Revision, x.Current)));
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("y")));
        Assert.Equal(2, rig.Tools.Symbols.Count);

        // a slow plugin answers for the revision it read: rejected, nothing shown
        var stale = await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(older, Symbol("a", "é1", 0, 3))), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(2, rig.Tools.Symbols.Count);

        var now = rig.Revision;
        await rig.CallAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(now, Symbol("a", "yxé1", 0, 5))));
        Assert.Equal((now, true), (rig.Tools.Symbols[^1].Set.Revision, rig.Tools.Symbols[^1].Current));
    }

    [Fact]
    public async Task AnOutdatedSymbolIsFoundWhereItIsNowAndLostWhenItsTextWasChanged()
    {
        await using var rig = await Rig.CreateAsync(Utf8("aa\nbb\ncc\n"), codec: TextCodec.Utf8);
        await rig.CallAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rig.Revision, Symbol("b", "bb", 3, 5), Symbol("c", "cc", 6, 8))));

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("12")));
        Assert.Equal(new ByteRange(5, 7), rig.Owner.Run(() => rig.Api.ResolveSymbol("p", rig.Doc.Id, "b")));

        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(5, 6), Utf8("XY")));
        Assert.Null(rig.Owner.Run(() => rig.Api.ResolveSymbol("p", rig.Doc.Id, "b")));
        Assert.NotNull(rig.Owner.Run(() => rig.Api.ResolveSymbol("p", rig.Doc.Id, "c")));
        Assert.Null(rig.Owner.Run(() => rig.Api.ResolveSymbol("p", rig.Doc.Id, "nope")));

        rig.Owner.Run(() => { rig.Api.Withdraw(rig.Doc); return 0; });
        Assert.Equal([("p", rig.Doc.Id)], rig.Tools.Cleared);
    }

    [Fact]
    public async Task SymbolsAreCheckedAndLimited()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), o => o with { MaxSymbolsPerDocument = 2 }, codec: TextCodec.Utf8);
        var rev = rig.Revision;
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 1))), DocumentErrorCodes.InvalidRange); // inside "é"
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 99))), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 2), Symbol("a", "y", 3, 4))), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 2, parent: "later"), Symbol("later", "y", 3, 4))), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", new string('n', 51), 0, 2))), RpcErrorCodes.InvalidParams);
        var quota = await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 2), Symbol("b", "y", 3, 4), Symbol("c", "z", 4, 5))), DocumentErrorCodes.SymbolsQuotaExceeded);
        Assert.Equal(2, quota.GetProperty("data").GetProperty("limit").GetInt32());
        Assert.Empty(rig.Tools.Symbols);

        rig.Permissions.RevokeAll("p");
        await rig.ErrorAsync(DocumentMethods.SymbolsPublish, rig.D(Publish(rev, Symbol("a", "x", 0, 2))), DocumentErrorCodes.PermissionDenied);
    }

    [Fact]
    public async Task RevealNeedsTheCurrentRevisionAndARangeOnCharacterBoundaries()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), codec: TextCodec.Utf8);
        var rev = rig.Revision;

        var ok = await rig.CallAsync(DocumentMethods.Reveal, rig.D($"\"revision\":{rev},\"start\":4,\"end\":6"));
        Assert.True(ok.GetProperty("revealed").GetBoolean());
        Assert.Equal([new ByteRange(4, 6)], rig.Tools.Revealed);

        await rig.ErrorAsync(DocumentMethods.Reveal, rig.D($"\"revision\":{rev},\"start\":1,\"end\":3"), DocumentErrorCodes.InvalidRange);
        rig.Tools.HasView = false;
        await rig.ErrorAsync(DocumentMethods.Reveal, rig.D($"\"revision\":{rev},\"start\":4,\"end\":6"), DocumentErrorCodes.NoTextView);

        rig.Tools.HasView = true;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));
        await rig.ErrorAsync(DocumentMethods.Reveal, rig.D($"\"revision\":{rev},\"start\":4,\"end\":6"), DocumentErrorCodes.RevisionConflict);
        Assert.Single(rig.Tools.Revealed);
    }

    private static Func<SelectionSet?> TwoSelections() =>
        () => SelectionSet.Create([new TextSelection(2, 3), new TextSelection(6, 7)], 1);

    private string Text(Rig rig) => System.Text.Encoding.UTF8.GetString(rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, rig.Doc.ByteLength))));

    [Fact]
    public async Task ASnippetGoesIntoEverySelectionWithCaretsAsOneUndoStep()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;
        var depth = rig.Owner.Run(() => rig.Doc.UndoDepth);

        var r = await rig.CallAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"[]\",\"carets\":[1]"));

        Assert.Equal("é[]\né[]\n", Text(rig));
        Assert.Equal(rev + 1, r.GetProperty("revision").GetInt64());
        Assert.Equal(rev + 1, rig.Revision);
        Assert.Equal(depth + 1, rig.Owner.Run(() => rig.Doc.UndoDepth));
        var placed = Assert.Single(rig.Tools.Placed);
        Assert.Equal([TextSelection.At(3), TextSelection.At(8)], placed.Items);
        Assert.Equal(1, placed.Primary);

        var undo = rig.Owner.Run(() => rig.Doc.Undo());
        Assert.Equal("é1\né2\n", Text(rig));
        Assert.Equal([new ByteRange(2, 3), new ByteRange(6, 7)], undo!.Selections);
    }

    [Fact]
    public async Task ASnippetWithSeveralCaretsPlacesAllOfThemInEverySelectionAndUsesTheLineBreakOfTheDocument()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        rig.Tools.Eol = "\r\n";

        await rig.CallAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rig.Revision},\"text\":\"<a>\\n</a>\",\"carets\":[3,4]"));

        Assert.Equal("é<a>\r\n</a>\né<a>\r\n</a>\n", Text(rig));
        // "<a>" is 3 bytes, the line break 2: in each insertion the carets stand before and after the line break; the second insertion is 8 bytes further
        Assert.Equal([5, 7, 17, 19], Assert.Single(rig.Tools.Placed).Items.Select(s => s.Caret));
    }

    [Fact]
    public async Task ASnippetIsRefusedWhenItsRevisionIsOldOrItsCaretsOrPermissionAreWrong()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), o => o with { MaxSnippetCarets = 2 }, codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;
        const string before = "é1\né2\n";

        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"a😀b\",\"carets\":[2]"), RpcErrorCodes.InvalidParams); // inside a surrogate pair
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"a\\r\\nb\",\"carets\":[2]"), RpcErrorCodes.InvalidParams); // inside a line break
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"ab\",\"carets\":[3]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"ab\",\"carets\":[0,1,2]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"{new string('x', 201)}\"") , RpcErrorCodes.InvalidParams);
        Assert.Equal(before, Text(rig));

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("z")));
        var stale = await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"text\":\"ab\""), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal("z" + before, Text(rig));
        Assert.Empty(rig.Tools.Placed);

        await using var noEdit = await Rig.CreateAsync(Utf8("ab"), canEdit: false, codec: TextCodec.Utf8);
        await noEdit.ErrorAsync(DocumentMethods.SnippetInsert, noEdit.D($"\"revision\":{noEdit.Revision},\"text\":\"x\""), DocumentErrorCodes.PermissionDenied);
        Assert.Equal("ab", Text(noEdit));
    }

    [Fact]
    public async Task ASnippetNeedsSelectionsToInsertInto()
    {
        await using var rig = await Rig.CreateAsync(Utf8("ab"), codec: TextCodec.Utf8);
        rig.Api.Withdraw(rig.Doc);
        rig.Api.Expose(rig.Doc, null);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rig.Revision},\"text\":\"x\""), DocumentErrorCodes.NoTextView);
        Assert.Equal("ab", Text(rig));
    }

    private async Task<JsonElement> RequestsAsync(Rig rig) => (await rig.Host.CallAsync("p", "tabRequests")).Clone();

    private static Task<JsonElement> SetTabAsync(Rig rig, string json) => rig.Host.CallAsync("p", "setTab", T.Json(json));

    [Fact]
    public async Task TabGoesToThePluginsThatRegisteredAndEveryFailureLeavesItToTheEditor()
    {
        await using var rig = await Rig.CreateAsync(Utf8("ab\n"), o => o with { TabTimeout = TimeSpan.FromMilliseconds(400) }, codec: TextCodec.Utf8);
        Assert.False(rig.Api.HasTabProviders);
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));
        Assert.Equal(0, (await RequestsAsync(rig)).GetArrayLength());

        await rig.CallAsync(DocumentMethods.TabRegister, "{\"enabled\":true}");
        Assert.True(rig.Api.HasTabProviders);

        // not handled, with the revision and the selections it was asked about
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));
        var asked = (await RequestsAsync(rig))[0];
        Assert.Equal(rig.Revision, asked.GetProperty("revision").GetInt64());
        Assert.Equal(rig.Doc.Id.ToString(), asked.GetProperty("document").GetString());
        Assert.Equal((1, 3), (asked.GetProperty("selections")[0].GetProperty("anchor").GetInt32(), asked.GetProperty("selections")[0].GetProperty("caret").GetInt32()));

        await SetTabAsync(rig, "{\"handled\":true}");
        Assert.True(await rig.Api.OfferTabAsync(rig.Doc.Id));

        await SetTabAsync(rig, "{\"handled\":true,\"error\":true}");
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));

        await SetTabAsync(rig, "{\"handled\":true,\"delayMs\":5000}");
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));
        Assert.True(started.ElapsedMilliseconds < 4000, "Tab waited for a plugin that is too slow.");

        // no read grant for this document: the plugin is not asked
        await SetTabAsync(rig, "{\"handled\":true}");
        var asks = (await RequestsAsync(rig)).GetArrayLength();
        rig.Permissions.RevokeAll("p");
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));
        Assert.Equal(asks, (await RequestsAsync(rig)).GetArrayLength());

        rig.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.All);
        await rig.CallAsync(DocumentMethods.TabRegister, "{\"enabled\":false}");
        Assert.False(rig.Api.HasTabProviders);
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id));
    }
}

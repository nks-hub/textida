using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Snippets;
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
        public readonly List<IReadOnlyList<(long Start, ParsedSnippet Snippet)>> Sessions = [];
        public void BeginSnippets(Guid document, IReadOnlyList<(long Start, ParsedSnippet Snippet)> snippets) => Sessions.Add(snippets);
        public string NewLine(Guid document) => Eol;

        public Func<TableSelectionData?>? Table;
        public readonly List<(string Text, string? Html)> Clipboard = [];
        public TableSelectionData? TableSelection(Guid document) => Table?.Invoke();
        public void SetClipboard(Guid document, string text, string? html) { lock (Clipboard) Clipboard.Add((text, html)); }

        public readonly List<(string Plugin, string Name, string? Title, Document Document)> Virtuals = [];
        public readonly List<DiffRequest> Diffs = [];
        public bool DiffShown = true;

        public readonly Dictionary<Guid, VirtualInfo> VirtualInfos = [];
        public readonly List<Guid> VirtualClosed = [];

        public Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec, VirtualInfo info)
        {
            var doc = Document.CreateVirtual(name, content, PagedBackendFactory.Instance, codec, info.Editable);
            lock (Virtuals) { Virtuals.Add((pluginId, name, title, doc)); VirtualInfos[doc.Id] = info; }
            return doc;
        }

        public void CloseVirtual(Guid document) { lock (Virtuals) VirtualClosed.Add(document); }

        public bool OpenDiff(DiffRequest request) { lock (Diffs) Diffs.Add(request); return DiffShown; }

        public readonly List<Guid> DiagnosticsNotified = [];

        public void DiagnosticsChanged(Guid document) { lock (DiagnosticsNotified) DiagnosticsNotified.Add(document); }
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
    public async Task SnippetItemsReplaceTheirOwnRangesWithTheirOwnTextAndCaretsAsOneUndoStep()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;
        var depth = rig.Owner.Run(() => rig.Doc.UndoDepth);

        // the second item is listed first: the order does not matter
        var r = await rig.CallAsync(DocumentMethods.SnippetInsert, rig.D(
            $"\"revision\":{rev},\"items\":[{{\"start\":4,\"end\":7,\"text\":\"Y\\nZ\",\"carets\":[1,3]}},{{\"start\":0,\"end\":3,\"text\":\"X\",\"carets\":[1]}}]"));

        Assert.Equal("X\nY\nZ\n", Text(rig));
        Assert.Equal((rev + 1, 3), (r.GetProperty("revision").GetInt64(), r.GetProperty("carets").GetInt32()));
        Assert.Equal(depth + 1, rig.Owner.Run(() => rig.Doc.UndoDepth));
        var placed = Assert.Single(rig.Tools.Placed);
        Assert.Equal([1, 3, 5], placed.Items.Select(s => s.Caret));
        Assert.Equal(1, placed.Primary); // the primary selection lay in the second item

        var undo = rig.Owner.Run(() => rig.Doc.Undo());
        Assert.Equal("é1\né2\n", Text(rig));
        Assert.Equal([new ByteRange(2, 3), new ByteRange(6, 7)], undo!.Selections);
    }

    [Fact]
    public async Task SnippetItemsThatOverlapSplitACharacterOrAreStaleChangeNothing()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;

        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[{{\"start\":0,\"end\":3,\"text\":\"a\"}},{{\"start\":2,\"end\":4,\"text\":\"b\"}}]"), DocumentErrorCodes.EditsOverlap);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[{{\"start\":1,\"end\":3,\"text\":\"a\"}}]"), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[{{\"start\":3,\"end\":2,\"text\":\"a\"}}]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[{{\"start\":0,\"end\":3,\"text\":\"ab\",\"carets\":[3]}}]"), RpcErrorCodes.InvalidParams);
        Assert.Equal("é1\né2\n", Text(rig));

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("z")));
        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"items\":[{{\"start\":0,\"end\":3,\"text\":\"a\"}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("zé1\né2\n", Text(rig));
        Assert.Empty(rig.Tools.Placed);
    }

    private static string Quoted(string text) => JsonSerializer.Serialize(text);

    [Fact]
    public async Task TemplateItemsBeginOneSessionWithTheirOwnStopsAndKeepTheirTextAsItIs()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;
        var depth = rig.Owner.Run(() => rig.Doc.UndoDepth);

        // \$ is a dollar sign; the second item is not re-indented, and its carets (not used for a template) are not checked
        var items = $"[{{\"start\":0,\"end\":3,\"text\":{Quoted("<a b=\"${2}\">${1}</a>")},\"carets\":[99]}},{{\"start\":4,\"end\":7,\"text\":{Quoted("\\$x\n\t${1}${2}")}}}]";
        var r = await rig.CallAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"format\":\"template\",\"items\":{items}"));

        Assert.Equal("<a b=\"\"></a>\n$x\n\t\n", Text(rig));
        Assert.Equal((rev + 1, 2), (r.GetProperty("revision").GetInt64(), r.GetProperty("carets").GetInt32()));
        Assert.Equal(depth + 1, rig.Owner.Run(() => rig.Doc.UndoDepth));
        var placed = Assert.Single(rig.Tools.Placed);
        Assert.Equal([12, 17], placed.Items.Select(s => s.Caret)); // $0 is the end of each snippet
        Assert.Equal(1, placed.Primary);

        var session = Assert.Single(rig.Tools.Sessions);
        Assert.Equal([0L, 13L], session.Select(s => s.Start));
        Assert.Equal([8, 6], session[0].Snippet.Stops.Select(stop => stop.Spans[0].Start)); // stop 1 stands after the tag, stop 2 in the attribute
        Assert.Equal([4, 4], session[1].Snippet.Stops.Select(stop => stop.Spans[0].Start));
        Assert.All(session, s => Assert.Equal([1, 2], s.Snippet.Stops.Select(stop => stop.Number)));
    }

    [Fact]
    public async Task ATemplateItemWithoutStopsBeginsNoSessionAndAnUnknownVariableIsRefused()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\né2\n"), codec: TextCodec.Utf8, selections: TwoSelections());
        var rev = rig.Revision;

        await rig.ErrorAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"format\":\"template\",\"items\":[{{\"start\":0,\"end\":3,\"text\":\"${{name}}\"}}]"), RpcErrorCodes.InvalidParams);
        Assert.Equal("é1\né2\n", Text(rig));
        Assert.Empty(rig.Tools.Placed);

        await rig.CallAsync(DocumentMethods.SnippetInsert, rig.D($"\"revision\":{rev},\"format\":\"template\",\"items\":[{{\"start\":0,\"end\":3,\"text\":{Quoted("<br>")}}}]"));
        Assert.Equal("<br>\né2\n", Text(rig));
        Assert.Equal([4], Assert.Single(rig.Tools.Placed).Items.Select(s => s.Caret));
        Assert.Empty(rig.Tools.Sessions);
    }

    [Fact]
    public async Task SnippetItemsThatTouchAHiddenValueOfASecretsFileAreRefusedUnlessThePluginMaySeeIt()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var bytes = Utf8("KEY=hunter2\nB=2\n");
        var path = WriteEnv(".env", bytes);
        var doc = Document.Open(path, PagedBackendFactory.Instance);
        rig.Owner.Run(() => { rig.Api.Expose(doc, () => SelectionSet.Single(TextSelection.At(0))); return 0; });
        try
        {
            string Items(string items, long revision) => $"{{\"document\":\"{doc.Id}\",\"revision\":{revision},\"items\":[{items}]}}";
            byte[] Bytes() => rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength)));

            await rig.ErrorAsync(DocumentMethods.SnippetInsert, Items("{\"start\":4,\"end\":11,\"text\":\"x\"}", doc.Revision), DocumentErrorCodes.PermissionDenied);
            await rig.ErrorAsync(DocumentMethods.SnippetInsert, Items("{\"start\":0,\"end\":3,\"text\":\"APP\"},{\"start\":6,\"end\":6,\"text\":\"y\"}", doc.Revision), DocumentErrorCodes.PermissionDenied);
            Assert.Equal(bytes, Bytes());

            await rig.CallAsync(DocumentMethods.SnippetInsert, Items("{\"start\":0,\"end\":3,\"text\":\"APP\"},{\"start\":12,\"end\":13,\"text\":\"C\"}", doc.Revision));
            Assert.Equal(Utf8("APP=hunter2\nC=2\n"), Bytes());

            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(doc.Id));
            await rig.CallAsync(DocumentMethods.SnippetInsert, Items("{\"start\":4,\"end\":11,\"text\":\"new\"}", doc.Revision));
            Assert.Equal(Utf8("APP=new\nC=2\n"), Bytes());
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
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

        // a plugin the user turned off for the document is not asked
        var beforeSkip = (await RequestsAsync(rig)).GetArrayLength();
        Assert.False(await rig.Api.OfferTabAsync(rig.Doc.Id, skip: "p"));
        Assert.Equal(beforeSkip, (await RequestsAsync(rig)).GetArrayLength());

        // the language mode and the indentation unit travel with the request when the editor gives them
        Assert.True(await rig.Api.OfferTabAsync(rig.Doc.Id, default, "html", "  "));
        var all = await RequestsAsync(rig);
        var withContext = all[all.GetArrayLength() - 1];
        Assert.Equal(("html", "  "), (withContext.GetProperty("language").GetString(), withContext.GetProperty("indent").GetString()));
        Assert.False(asked.TryGetProperty("language", out _));

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

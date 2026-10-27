using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>Document API v1 through real plugin processes: the test plugin calls the editor and reports what it got.</summary>
public partial class DocumentApiTests
{
    sealed class OwnerLock : IDocumentOwnerThread
    {
        readonly object _gate = new();

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            try { lock (_gate) return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }

        public T Run<T>(Func<T> work) { lock (_gate) return work(); }
    }

    sealed class ManualTime : TimeProvider
    {
        DateTimeOffset _now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    sealed class RecordingProposals : IEditProposals
    {
        public readonly List<EditProposal> Items = [];

        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
        {
            lock (Items) { Items.Add(proposal); return ValueTask.FromResult(new ProposalDecision("proposal-" + Items.Count, ProposalOutcome.Applied, proposal.Edits.Count, proposal.Edits.Count)); }
        }
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Api { get; init; }
        public required Document Doc { get; init; }
        public required OwnerLock Owner { get; init; }
        public required RecordingProposals Proposals { get; init; }
        public required PluginPermissions Permissions { get; init; }
        public required PluginCallLog CallLog { get; init; }
        public required TaskCompletionSource Gate { get; init; }
        public required RecordingTools Tools { get; init; }

        public static async Task<Rig> CreateAsync(byte[] content, Func<DocumentApiOptions, DocumentApiOptions>? tune = null, bool canEdit = true,
            TimeProvider? time = null, bool gated = false, IBackendFactory? factory = null, string[]? plugins = null, NksHub.Textida.Core.Encodings.TextCodec? codec = null,
            Func<Func<PluginHost.PluginHost>, DocumentApi, PluginPermissions, PluginCallLog, PluginApiCallback>? route = null,
            Func<SelectionSet?>? selections = null)
        {
            var proposals = new RecordingProposals();
            var tools = new RecordingTools();
            var permissions = new PluginPermissions();
            var callLog = new PluginCallLog(1000);
            foreach (var id in plugins ?? ["p"])
            {
                permissions.Grant(id, DocumentPermissions.Read, GrantScope.All);
                permissions.Grant(id, DocumentPermissions.EditPropose, GrantScope.All);
                if (canEdit) permissions.Grant(id, DocumentPermissions.EditDirect, GrantScope.All);
            }
            var options = new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16,
                MaxFindMatches = 100,
                MaxSnapshotsPerPlugin = 2,
                SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64,
                MaxRememberedBatches = 16,
                Permissions = permissions,
                CallLog = callLog,
                Proposals = proposals,
                Tools = tools,
                MaxSymbolsPerDocument = 100,
                MaxSymbolTextLength = 50,
                MaxSnippetLength = 200,
                MaxSnippetCarets = 4,
                TabTimeout = TimeSpan.FromMilliseconds(500),
                MaxSecretScanBytes = 1 << 20,
                MaxClipboardChars = 1000,
                MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20,
            };
            if (tune is not null) options = tune(options);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!gated) gate.SetResult();
            PluginHost.PluginHost? host = null;
            var owner = new OwnerLock();
            var api = new DocumentApi(options, owner, async (plugin, name, payload) =>
            {
                await gate.Task;
                return await host!.SendEventAsync(plugin, name, payload);
            }, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct), time);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), route?.Invoke(() => host!, api, permissions, callLog) ?? api.HandleAsync, api);
            var doc = Document.FromBytes(content, factory ?? PagedBackendFactory.Instance, codec is null ? null : new DocumentOptions { Format = codec });
            api.Expose(doc, selections ?? (() => SelectionSet.Single(new TextSelection(1, 3))));
            foreach (var id in plugins ?? ["p"])
            {
                host.Register(new PluginSpec(id, "Plugin " + id, T.DotnetHost, [T.PluginDll], ["onCommand:" + id]));
                await host.ActivateAsync("onCommand:" + id);
            }
            return new Rig { Host = host, Api = api, Doc = doc, Owner = owner, Proposals = proposals, Permissions = permissions, CallLog = callLog, Gate = gate, Tools = tools };
        }

        public string D(string rest = "") => $"{{\"document\":\"{Doc.Id}\"{(rest.Length > 0 ? "," + rest : "")}}}";

        public long Revision => Owner.Run(() => Doc.Revision);

        /// <summary>The plugin calls the editor; the answer is the result, or <c>{"error":code,"data":...}</c>.</summary>
        public async Task<JsonElement> CallAsync(string method, string paramsJson, string plugin = "p")
        {
            var json = $"{{\"method\":\"{method}\",\"params\":{paramsJson}}}";
            return (await Host.CallAsync(plugin, "callEditor", T.Json(json))).Clone();
        }

        public async Task<JsonElement> ErrorAsync(string method, string paramsJson, int code, string plugin = "p")
        {
            var r = await CallAsync(method, paramsJson, plugin);
            Assert.True(r.ValueKind == JsonValueKind.Object && r.TryGetProperty("error", out var e) && e.GetInt32() == code, "Expected error " + code + ", got " + r.GetRawText());
            return r;
        }

        public async Task<List<JsonElement>> EventsAsync(string plugin = "p") =>
            [.. (await Host.CallAsync(plugin, "events")).EnumerateArray().Select(e => e.Clone())];

        public async Task WaitEventsAsync(Func<List<JsonElement>, bool> done, string plugin = "p")
        {
            for (var i = 0; i < 750; i++)
            {
                if (done(await EventsAsync(plugin))) return;
                await Task.Delay(20);
            }
            throw new TimeoutException("Events not reached: " + string.Join(" ", (await EventsAsync(plugin)).Select(e => e.GetRawText())));
        }

        public async ValueTask DisposeAsync()
        {
            Gate.TrySetResult();
            await Host.DisposeAsync();
            await Api.DisposeAsync();
            Doc.Dispose();
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    static byte[] FromB64(JsonElement r) => Convert.FromBase64String(r.GetProperty("bytes").GetString()!);

    [Fact]
    public async Task ReadsAreBoundToTheRevisionAndTextStopsAtCharacterBoundaries()
    {
        await using var rig = await Rig.CreateAsync(Utf8("héllo\nworld\n"));
        var rev = rig.Revision;

        var list = await rig.CallAsync(DocumentMethods.List, "{}");
        Assert.Equal(rig.Doc.Id.ToString(), list[0].GetProperty("id").GetString());
        Assert.Equal(13, list[0].GetProperty("byteLength").GetInt64());

        var bytes = await rig.CallAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":4"));
        Assert.Equal(Utf8("hél"), FromB64(bytes));

        var text = await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev},\"start\":0,\"end\":4"));
        Assert.Equal("hél", text.GetProperty("text").GetString());
        Assert.Equal(0, text.GetProperty("invalid").GetArrayLength());

        await rig.ErrorAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev},\"start\":0,\"end\":2"), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":14"), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":{(1 << 16) + 1}"), RpcErrorCodes.MessageTooLarge);

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));
        var stale = await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":1"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(rev + 1, stale.GetProperty("data").GetProperty("currentRevision").GetInt64());
        var future = await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev + 5},\"start\":0,\"end\":1"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("FutureRevision", future.GetProperty("data").GetProperty("reason").GetString());
        await rig.ErrorAsync(DocumentMethods.ReadBytes, "{\"document\":\"" + Guid.NewGuid() + "\",\"revision\":0,\"start\":0,\"end\":1}", DocumentErrorCodes.UnknownDocument);
    }

    [Fact]
    public async Task InvalidUtf8IsReportedNotHiddenAndSurvivesAnEditAroundIt()
    {
        byte[] original = [.. Utf8("ab"), 0xFF, 0xC3, .. Utf8("cd")];
        await using var rig = await Rig.CreateAsync(original, codec: NksHub.Textida.Core.Encodings.TextCodec.Utf8);
        var rev = rig.Revision;

        var text = await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev},\"start\":0,\"end\":6"));
        Assert.Contains("�", text.GetProperty("text").GetString());
        Assert.Equal([2, 3], text.GetProperty("invalid").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(original, FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":6"))));

        // Write the unreadable bytes back unchanged and change a character next to them.
        var apply = await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"b1\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"X\"}},{{\"start\":2,\"end\":4,\"bytes\":\"{B64(0xFF, 0xC3)}\"}}]"));
        Assert.True(apply.GetProperty("applied").GetBoolean());
        Assert.Equal([.. Utf8("Xb"), 0xFF, 0xC3, .. Utf8("cd")], rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, rig.Doc.ByteLength))));
    }

    [Fact]
    public async Task APluginReadsARangeOfAMappedFileWithoutLoadingIt()
    {
        var path = Path.Combine(T.TempDir(), "big.txt");
        try
        {
            using (var fs = File.Create(path))
            {
                var line = Utf8(new string('a', 99) + "\n");
                for (var i = 0; i < 30_000; i++) fs.Write(line);
            }
            Document doc;
            await using var rig = await Rig.CreateAsync(Utf8("x"));
            doc = Document.Open(path, PagedBackendFactory.Instance);
            rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
            var r = await rig.CallAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"start\":2000000,\"end\":2000100}}");
            Assert.Equal(Utf8(new string('a', 99) + "\n"), FromB64(r));
            Assert.Equal(3_000_000, doc.ByteLength);
            rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task PositionsAreExactAndAnIncompleteIndexIsNotReportedAsLineZero()
    {
        await using var rig = await Rig.CreateAsync(Utf8("one\ntwo\nthree\n"));
        var rev = rig.Revision;
        var pos = await rig.CallAsync(DocumentMethods.OffsetToPosition, rig.D($"\"revision\":{rev},\"offset\":9"));
        Assert.Equal(2, pos.GetProperty("line").GetInt64());
        Assert.Equal(1, pos.GetProperty("column").GetInt64());
        var back = await rig.CallAsync(DocumentMethods.PositionToOffset, rig.D($"\"revision\":{rev},\"line\":2,\"column\":1"));
        Assert.Equal(9, back.GetProperty("offset").GetInt64());
        await rig.ErrorAsync(DocumentMethods.PositionToOffset, rig.D($"\"revision\":{rev},\"line\":99,\"column\":0"), DocumentErrorCodes.PositionUnknown);
        await rig.ErrorAsync(DocumentMethods.OffsetToPosition, rig.D($"\"revision\":{rev - 1},\"offset\":9"), DocumentErrorCodes.RevisionConflict);

        await using var unindexed = await Rig.CreateAsync(Utf8(string.Concat(Enumerable.Repeat("line\n", 1000))), factory: new PagedBackendFactory(startIndexing: false, indexBlockSize: 1024));
        var far = await unindexed.ErrorAsync(DocumentMethods.OffsetToPosition, unindexed.D($"\"revision\":{unindexed.Revision},\"offset\":4000"), DocumentErrorCodes.PositionUnknown);
        Assert.False(far.GetProperty("data").GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task FindReturnsByteRangesOfTheRevisionAndSaysWhenItStoppedEarly()
    {
        await using var rig = await Rig.CreateAsync(Utf8("ab ab ab ab"), o => o with { MaxFindMatches = 3 });
        var rev = rig.Revision;
        var r = await rig.CallAsync(DocumentMethods.Find, rig.D($"\"revision\":{rev},\"text\":\"ab\""));
        Assert.Equal([0L, 3L, 6L], r.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("start").GetInt64()));
        Assert.True(r.GetProperty("truncated").GetBoolean());
        var few = await rig.CallAsync(DocumentMethods.Find, rig.D($"\"revision\":{rev},\"text\":\"ab\",\"start\":4,\"end\":8"));
        Assert.Equal([6L], few.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("start").GetInt64()));
        Assert.False(few.GetProperty("truncated").GetBoolean());
        var sel = await rig.CallAsync(DocumentMethods.Selections, rig.D());
        Assert.Equal(3, sel.GetProperty("selections")[0].GetProperty("caret").GetInt64());
    }

    [Fact]
    public async Task ASnapshotKeepsItsRevisionExpiresAndIsLimitedPerPlugin()
    {
        var time = new ManualTime();
        await using var rig = await Rig.CreateAsync(Utf8("hello"), time: time);
        var snap = await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
        var id = snap.GetProperty("snapshot").GetString()!;
        var oldRevision = snap.GetProperty("revision").GetInt64();

        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(0, 5), Utf8("changed")));
        var read = await rig.CallAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{id}\",\"start\":0,\"end\":5}}");
        Assert.Equal(Utf8("hello"), FromB64(read));
        Assert.Equal(oldRevision, read.GetProperty("revision").GetInt64());

        await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
        var quota = await rig.ErrorAsync(DocumentMethods.SnapshotCreate, rig.D(), DocumentErrorCodes.SnapshotQuotaExceeded);
        Assert.Equal(2, quota.GetProperty("data").GetProperty("limit").GetInt32());

        Assert.True((await rig.CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{id}\"}}")).GetProperty("released").GetBoolean());
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{id}\",\"start\":0,\"end\":5}}", DocumentErrorCodes.SnapshotExpired);

        var second = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        time.Advance(TimeSpan.FromSeconds(11));
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{second}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.SnapshotExpired);
        await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
    }

    [Fact]
    public async Task EachReadOfASnapshotRestartsItsLifetime()
    {
        var time = new ManualTime();
        await using var rig = await Rig.CreateAsync(Utf8("hello"), time: time);
        var id = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        for (int i = 0; i < 3; i++)
        {
            time.Advance(TimeSpan.FromSeconds(6)); // 18 s in all, the lifetime is 10 s
            Assert.Equal(Utf8("hello"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{id}\",\"start\":0,\"end\":5}}")));
        }
        time.Advance(TimeSpan.FromSeconds(11));
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{id}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.SnapshotExpired);
    }

    [Fact]
    public async Task PositionsMapForwardThroughTheChangeLogAndAmbiguousOnesAreAConflict()
    {
        await using var rig = await Rig.CreateAsync(Utf8("0123456789"));
        var from = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("AB")));
        rig.Owner.Run(() => rig.Doc.Delete(new ByteRange(6, 8)));

        var ok = await rig.CallAsync(DocumentMethods.MapRanges, rig.D($"\"fromRevision\":{from},\"ranges\":[{{\"start\":1,\"end\":3}},{{\"start\":8,\"end\":10}}]"));
        Assert.Equal(from + 2, ok.GetProperty("revision").GetInt64());
        Assert.Equal([(3L, 5L), (8L, 10L)], ok.GetProperty("ranges").EnumerateArray().Select(r => (r.GetProperty("start").GetInt64(), r.GetProperty("end").GetInt64())));

        var touched = await rig.ErrorAsync(DocumentMethods.MapRanges, rig.D($"\"fromRevision\":{from},\"ranges\":[{{\"start\":1,\"end\":3}},{{\"start\":5,\"end\":7}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Overlap", touched.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(1, touched.GetProperty("data").GetProperty("index").GetInt32());
        var future = await rig.ErrorAsync(DocumentMethods.MapRanges, rig.D($"\"fromRevision\":{from + 9},\"ranges\":[]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("FutureRevision", future.GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task EventsCarryRangesAndRevisionsInOrderAndNoText()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello world"));
        await rig.CallAsync(DocumentMethods.Subscribe, rig.D());
        var r0 = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(0, 5), Utf8("secret")));
        rig.Owner.Run(() => rig.Doc.Delete(new ByteRange(0, 1)));
        await rig.WaitEventsAsync(e => e.Count == 2);

        var events = await rig.EventsAsync();
        Assert.All(events, e => Assert.Equal(DocumentEvents.Changed, e.GetProperty("name").GetString()));
        Assert.Equal([r0 + 1, r0 + 2], events.Select(e => e.GetProperty("payload").GetProperty("revision").GetInt64()));
        var first = events[0].GetProperty("payload");
        Assert.Equal("Edit", first.GetProperty("kind").GetString());
        Assert.Equal((0L, 5L, 6L), (first.GetProperty("edits")[0].GetProperty("start").GetInt64(), first.GetProperty("edits")[0].GetProperty("deleted").GetInt64(), first.GetProperty("edits")[0].GetProperty("inserted").GetInt64()));
        Assert.DoesNotContain("secret", events[0].GetRawText());

        await rig.CallAsync(DocumentMethods.Unsubscribe, rig.D());
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("z")));
        await Task.Delay(300);
        Assert.Equal(2, (await rig.EventsAsync()).Count);
    }

    [Fact]
    public async Task ASlowPluginGetsOneEventsLostWithTheCurrentRevisionInsteadOfAGap()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), o => o with { EventQueueCapacity = 2 }, gated: true);
        await rig.CallAsync(DocumentMethods.Subscribe, rig.D());
        for (var i = 0; i < 8; i++) rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("a")));
        var current = rig.Revision;
        rig.Gate.SetResult();

        await rig.WaitEventsAsync(e => e.Any(x => x.GetProperty("name").GetString() == DocumentEvents.EventsLost));
        var events = await rig.EventsAsync();
        var lost = events.Where(e => e.GetProperty("name").GetString() == DocumentEvents.EventsLost).ToList();
        Assert.Single(lost);
        Assert.Equal(current, lost[0].GetProperty("payload").GetProperty("revision").GetInt64());
        Assert.Equal(DocumentEvents.EventsLost, events[^1].GetProperty("name").GetString());
        var delivered = events.Where(e => e.GetProperty("name").GetString() == DocumentEvents.Changed).Select(e => e.GetProperty("payload").GetProperty("revision").GetInt64()).ToList();
        Assert.InRange(delivered.Count, 2, 3);
        Assert.Equal(Enumerable.Range(0, delivered.Count).Select(i => current - 8 + 1 + i), delivered);

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("b")));
        await rig.WaitEventsAsync(e => e.Count > events.Count);
    }

    [Fact]
    public async Task AfterAPluginRestartItLearnsTheCurrentRevisionsAndKeepsReceivingEvents()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"));
        await rig.CallAsync(DocumentMethods.Subscribe, rig.D());
        var snap = await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
        var snapId = snap.GetProperty("snapshot").GetString()!;
        await Assert.ThrowsAsync<PluginCrashedException>(() => rig.Host.CallAsync("p", "crash", T.Json("{\"code\":3}")));
        await T.Until(() => rig.Host.GetState("p") == PluginState.WaitingRestart);
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("a")));
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("b")));
        var current = rig.Revision;

        await T.Until(() => rig.Host.GetState("p") == PluginState.Running);
        var resync = await rig.Host.CallAsync("p", "getResync");
        Assert.True(resync.GetProperty("restarted").GetBoolean());
        var docs = resync.GetProperty("state").GetProperty("documents");
        Assert.Equal(1, docs.GetArrayLength());
        Assert.Equal(rig.Doc.Id.ToString(), docs[0].GetProperty("id").GetString());
        Assert.Equal(current, docs[0].GetProperty("revision").GetInt64());
        Assert.True(resync.GetProperty("lostEvents").GetInt32() >= 1);

        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{snapId}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.SnapshotExpired);
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("c")));
        await rig.WaitEventsAsync(e => e.Count == 1 && e[0].GetProperty("payload").GetProperty("revision").GetInt64() == current + 1);
    }

    [Fact]
    public async Task DirectEditsNeedThePermissionAndTheStatedRevision()
    {
        await using var denied = await Rig.CreateAsync(Utf8("hello"), canEdit: false);
        var rev = denied.Revision;
        await denied.ErrorAsync(DocumentMethods.EditApply, denied.D($"\"revision\":{rev},\"batchId\":\"b\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}}]"), DocumentErrorCodes.PermissionDenied);
        Assert.Equal(rev, denied.Revision);
        Assert.Equal(Utf8("hello"), denied.Owner.Run(() => denied.Doc.ReadBytes(new ByteRange(0, 5))));

        await using var rig = await Rig.CreateAsync(Utf8("hello"));
        rev = rig.Revision;
        var undoDepth = rig.Owner.Run(() => rig.Doc.UndoDepth);
        var ok = await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"b1\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}},{{\"start\":5,\"end\":5,\"text\":\"!\"}}]"));
        Assert.Equal(rev + 1, ok.GetProperty("revision").GetInt64());
        Assert.False(ok.GetProperty("duplicate").GetBoolean());
        Assert.Equal(Utf8("Jello!"), rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, 6))));
        Assert.Equal(undoDepth + 1, rig.Owner.Run(() => rig.Doc.UndoDepth));
        rig.Owner.Run(() => rig.Doc.Undo());
        Assert.Equal(Utf8("hello"), rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, 5))));

        var stale = await rig.ErrorAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"b2\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"X\"}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(Utf8("hello"), rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, 5))));
    }

    [Fact]
    public async Task ABatchWithOverlappingEditsIsRejectedAsAWhole()
    {
        await using var rig = await Rig.CreateAsync(Utf8("0123456789"));
        var rev = rig.Revision;
        await rig.ErrorAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"o\",\"edits\":[{{\"start\":0,\"end\":2,\"text\":\"A\"}},{{\"start\":5,\"end\":8,\"text\":\"B\"}},{{\"start\":7,\"end\":9,\"text\":\"C\"}}]"), DocumentErrorCodes.EditsOverlap);
        Assert.Equal(rev, rig.Revision);
        Assert.Equal(Utf8("0123456789"), rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, 10))));
        // The rejected batch is not remembered: the corrected one with the same id goes through.
        var ok = await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"o\",\"edits\":[{{\"start\":0,\"end\":2,\"text\":\"A\"}}]"));
        Assert.Equal(rev + 1, ok.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task ABatchWhoseAnswerWasLostToACrashIsNotAppliedTwiceWhenSentAgain()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"));
        var rev = rig.Revision;
        var args = rig.D($"\"revision\":{rev},\"batchId\":\"once\",\"edits\":[{{\"start\":5,\"end\":5,\"text\":\"!\"}}]");
        var e = await Assert.ThrowsAsync<PluginCrashedException>(() => rig.Host.CallAsync("p", "callEditorThenCrash", T.Json($"{{\"method\":\"{DocumentMethods.EditApply}\",\"params\":{args}}}")));
        Assert.Equal(43, e.Info.ExitCode);
        await T.Until(() => rig.Host.GetState("p") == PluginState.Running);
        Assert.Equal(rev + 1, rig.Revision);

        var again = await rig.CallAsync(DocumentMethods.EditApply, args);
        Assert.True(again.GetProperty("duplicate").GetBoolean());
        Assert.Equal(rev + 1, again.GetProperty("revision").GetInt64());
        Assert.Equal(rev + 1, rig.Revision);
        Assert.Equal(Utf8("hello!"), rig.Owner.Run(() => rig.Doc.ReadBytes(new ByteRange(0, rig.Doc.ByteLength))));

        var reused = await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"once\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}}]"));
        Assert.Equal(RpcErrorCodes.InvalidParams, reused.GetProperty("error").GetInt32());
    }

    [Fact]
    public async Task AProposalIsCheckedAgainstTheCurrentRevisionAndLeavesTheDocumentAlone()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"), canEdit: false);
        var rev = rig.Revision;
        var ok = await rig.CallAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev},\"title\":\"Shout\",\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"HELLO\"}}]"));
        Assert.Equal("proposal-1", ok.GetProperty("proposal").GetString());
        Assert.Equal(("applied", 1, 1), (ok.GetProperty("decision").GetString(), ok.GetProperty("accepted").GetInt32(), ok.GetProperty("total").GetInt32()));
        lock (rig.Proposals.Items)
        {
            var p = Assert.Single(rig.Proposals.Items);
            Assert.Equal((rev, "Shout", "p"), (p.Revision, p.Title, p.PluginId));
            Assert.Equal(new ByteRange(0, 5), p.Edits[0].Range);
        }
        Assert.Equal(rev, rig.Revision);

        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8(">")));
        var stale = await rig.ErrorAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev},\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"HELLO\"}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        await rig.ErrorAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev + 1},\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"a\"}},{{\"start\":2,\"end\":4,\"text\":\"b\"}}]"), DocumentErrorCodes.EditsOverlap);
        lock (rig.Proposals.Items) Assert.Single(rig.Proposals.Items);
    }
}

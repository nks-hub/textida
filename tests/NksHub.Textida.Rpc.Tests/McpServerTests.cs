using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>The MCP server over a raw line-delimited wire: protocol eras, the tools and the permission model they sit on.</summary>
public class McpServerTests
{
    const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientInfo\":{\"name\":\"test\",\"version\":\"1\"},\"io.modelcontextprotocol/clientCapabilities\":{}}";

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

    sealed class NoTools : IEditorTools
    {
        public void SymbolsChanged(SymbolSet set, bool current) { }
        public void SymbolsCleared(string pluginId, Guid document) { }
        public bool Reveal(Guid document, ByteRange range) => false;
        public void PlaceSelections(Guid document, SelectionSet selections) { }
        public string NewLine(Guid document) => "\n";
        public TableSelectionData? TableSelection(Guid document) => null;
        public void SetClipboard(Guid document, string text, string? html) { }
        public Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, NksHub.Textida.Core.Encodings.TextCodec codec) => throw new NotSupportedException();
        public bool OpenDiff(DiffRequest request) => false;
        public void DiagnosticsChanged(Guid document) { }
    }

    /// <summary>The user: sees each proposal and answers when the test says so.</summary>
    sealed class FakeReviewer : IProposalReviewer
    {
        public readonly List<EditProposal> Seen = [];
        public TaskCompletionSource<ReviewOutcome> Decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ReviewOutcome> ReviewAsync(EditProposal proposal, CancellationToken cancellation)
        {
            lock (Seen) Seen.Add(proposal);
            return new ValueTask<ReviewOutcome>(Decision.Task);
        }
    }

    sealed record Reply(JsonElement Message)
    {
        public bool IsError => Message.TryGetProperty("error", out _);
        public int Code => Message.GetProperty("error").GetProperty("code").GetInt32();
        public JsonElement Result => Message.GetProperty("result");
    }

    sealed record ToolReply(bool IsError, string Text)
    {
        public JsonElement Json => JsonDocument.Parse(Text).RootElement.Clone();
    }

    sealed class Rig : IAsyncDisposable
    {
        readonly MemoryPipe _toServer = new(), _fromServer = new();
        readonly Dictionary<int, TaskCompletionSource<Reply>> _pending = [];
        Task _serving = Task.CompletedTask, _reading = Task.CompletedTask;
        int _nextId;

        public required DocumentApi Api { get; init; }
        public required McpServer Server { get; init; }
        public required PluginPermissions Permissions { get; init; }
        public required ProposalLedger Ledger { get; init; }
        public required FakeReviewer Reviewer { get; init; }
        public required OwnerLock Owner { get; init; }
        public required PluginCallLog CallLog { get; init; }
        public List<JsonElement> Notifications { get; } = [];
        public int OutlineRequests;

        public static Rig Create(Func<McpServerOptions, McpServerOptions>? tune = null, long maxScanBytes = 1 << 22)
        {
            var permissions = new PluginPermissions();
            var ledger = new ProposalLedger(100);
            var reviewer = new FakeReviewer();
            var callLog = new PluginCallLog(1000);
            var owner = new OwnerLock();
            Rig? rig = null;
            var api = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 20,
                MaxFindMatches = 100,
                MaxSnapshotsPerPlugin = 2,
                SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64,
                MaxRememberedBatches = 16,
                Permissions = permissions,
                CallLog = callLog,
                Proposals = new McpProposals(ledger, reviewer),
                Tools = new NoTools(),
                MaxSymbolsPerDocument = 100,
                MaxSymbolTextLength = 50,
                MaxSnippetLength = 200,
                MaxSnippetCarets = 4,
                TabTimeout = TimeSpan.FromMilliseconds(500),
                MaxSecretScanBytes = 1 << 20,
                MaxClipboardChars = 1000,
                MaxScanBytes = maxScanBytes, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20, MaxDiagnosticsPerDocument = 10, MaxCompletionItems = 10, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(2),
            }, owner, (_, _, _) => Task.FromResult(false), (_, _, _, _, _) => throw new NotSupportedException());
            var options = new McpServerOptions
            {
                Documents = api,
                Proposals = ledger,
                Limits = T.Roomy,
                ServerName = "textida-test",
                ServerVersion = "0.0.1",
                MaxResultChars = 200_000,
                MaxRangeBytes = 4096,
                MaxItems = 50,
                SnippetBytes = 12,
                MaxLineChars = 200,
                MaxPendingProposals = 10,
                TitleOf = null,
                EnsureOutline = (_, _) => { Interlocked.Increment(ref rig!.OutlineRequests); return Task.CompletedTask; },
            };
            if (tune is not null) options = tune(options);
            var server = new McpServer(options);
            rig = new Rig { Api = api, Server = server, Permissions = permissions, Ledger = ledger, Reviewer = reviewer, Owner = owner, CallLog = callLog };
            return rig.Start();
        }

        Rig Start()
        {
            _serving = Server.ServeAsync(_toServer, _fromServer);
            _reading = ReadAsync();
            return this;
        }

        async Task ReadAsync()
        {
            var reader = new FrameReader(_fromServer);
            while (true)
            {
                var frame = await RpcProtocols.Mcp.Framing.ReadAsync(reader, 16 << 20, default);
                if (frame.Status != FrameStatus.Ok) break;
                var message = JsonDocument.Parse(frame.Payload!).RootElement.Clone();
                TaskCompletionSource<Reply>? waiting = null;
                lock (_pending)
                {
                    if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) _pending.Remove(id.GetInt32(), out waiting);
                }
                if (waiting is not null) waiting.SetResult(new Reply(message));
                else lock (Notifications) Notifications.Add(message);
            }
            lock (_pending) foreach (var waiting in _pending.Values) waiting.TrySetException(new InvalidOperationException("The server closed the connection."));
        }

        public async Task SendAsync(string json) => await _toServer.WriteAsync(RpcProtocols.Mcp.Framing.Encode(Encoding.UTF8.GetBytes(json)));

        public async Task<Reply> RequestAsync(string method, string paramsJson)
        {
            var id = Interlocked.Increment(ref _nextId);
            var waiting = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) _pending[id] = waiting;
            await SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}");
            return await waiting.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        public Task<Reply> ModernAsync(string method, string extra = "") => RequestAsync(method, $"{{{Meta}{(extra.Length > 0 ? "," + extra : "")}}}");

        public async Task<ToolReply> ToolAsync(string name, string argumentsJson = "{}")
        {
            var reply = await ModernAsync("tools/call", $"\"name\":\"{name}\",\"arguments\":{argumentsJson}");
            Assert.False(reply.IsError, reply.Message.GetRawText());
            Assert.Equal("complete", reply.Result.GetProperty("resultType").GetString());
            var text = reply.Result.GetProperty("content")[0].GetProperty("text").GetString()!;
            return new ToolReply(reply.Result.GetProperty("isError").GetBoolean(), text);
        }

        public Document NewDocument(byte[] content, TextCodec? codec = null) =>
            Document.FromBytes(content, PagedBackendFactory.Instance, codec is null ? null : new DocumentOptions { Format = codec });

        /// <summary>Shows the document to the assistant the way the editor does when the user shares a tab.</summary>
        public Document Share(Document doc, bool propose = true, Func<SelectionSet?>? selections = null)
        {
            Owner.Run(() => { Api.Expose(doc, selections); return 0; });
            Permissions.Grant(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
            if (propose) Permissions.Grant(McpServer.ClientId, DocumentPermissions.EditPropose, GrantScope.OfDocument(doc.Id));
            return doc;
        }

        public string Args(Document doc, string more = "") => $"{{\"document\":\"{doc.Id}\"{(more.Length > 0 ? "," + more : "")}}}";

        public async ValueTask DisposeAsync()
        {
            Server.CloseAll();
            await _toServer.DisposeAsync();
            await Api.DisposeAsync();
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    // ---- protocol ----

    [Fact]
    public async Task A_modern_client_discovers_lists_and_calls_without_a_handshake()
    {
        await using var rig = Rig.Create();
        var discover = await rig.ModernAsync("server/discover");
        Assert.False(discover.IsError, discover.Message.GetRawText());
        var d = discover.Result;
        Assert.Equal("complete", d.GetProperty("resultType").GetString());
        Assert.Contains("2026-07-28", d.GetProperty("supportedVersions").EnumerateArray().Select(v => v.GetString()));
        Assert.True(d.TryGetProperty("tools", out _) || d.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.True(d.GetProperty("ttlMs").GetInt64() >= 0);
        Assert.Contains(d.GetProperty("cacheScope").GetString(), new[] { "public", "private" });
        Assert.Equal("textida-test", d.GetProperty("_meta").GetProperty("io.modelcontextprotocol/serverInfo").GetProperty("name").GetString());

        var list = await rig.ModernAsync("tools/list");
        var tools = list.Result.GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(McpServer.AllTools.Select(t => t.Name), tools.Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(14, tools.Count);
        foreach (var tool in tools)
        {
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
        }
        Assert.True(list.Result.GetProperty("ttlMs").GetInt64() >= 0);

        var call = await rig.ToolAsync("editor_list_documents");
        Assert.False(call.IsError);
        Assert.Equal(0, call.Json.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task A_request_that_breaks_the_per_request_rules_gets_the_error_of_the_specification()
    {
        await using var rig = Rig.Create();
        Assert.Equal(-32602, (await rig.RequestAsync("tools/list", "{}")).Code);
        var unsupported = await rig.RequestAsync("tools/list", "{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"1900-01-01\",\"io.modelcontextprotocol/clientCapabilities\":{}}}");
        Assert.Equal(-32022, unsupported.Code);
        var data = unsupported.Message.GetProperty("error").GetProperty("data");
        Assert.Equal("1900-01-01", data.GetProperty("requested").GetString());
        Assert.Contains("2026-07-28", data.GetProperty("supported").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(-32602, (await rig.RequestAsync("tools/list", "{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"}}")).Code);
        Assert.Equal(-32601, (await rig.ModernAsync("resources/list")).Code);
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", "\"name\":\"nope\"")).Code);
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", "\"name\":\"editor_read_range\",\"arguments\":{}")).Code);
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", "\"name\":\"editor_read_range\",\"arguments\":{\"document\":\"x\"}")).Code);
    }

    [Fact]
    public async Task A_legacy_client_that_opens_with_initialize_is_served_by_the_2025_11_25_rules()
    {
        await using var rig = Rig.Create();
        Assert.Equal(-32601, (await rig.RequestAsync("ping", "{}")).Code);
        Assert.Equal(-32602, (await rig.RequestAsync("tools/list", "{}")).Code); // no handshake, no _meta
        var init = await rig.RequestAsync("initialize", "{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"old\",\"version\":\"1\"}}");
        Assert.Equal("2025-11-25", init.Result.GetProperty("protocolVersion").GetString());
        Assert.True(init.Result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("textida-test", init.Result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.False(init.Result.TryGetProperty("resultType", out _));
        await rig.SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        var list = await rig.RequestAsync("tools/list", "{}");
        Assert.Equal(14, list.Result.GetProperty("tools").GetArrayLength());
        Assert.False(list.Result.TryGetProperty("resultType", out _));
        var call = await rig.RequestAsync("tools/call", "{\"name\":\"editor_list_documents\"}");
        Assert.False(call.Result.GetProperty("isError").GetBoolean());
        Assert.Equal(0, (await rig.RequestAsync("ping", "{}")).Result.EnumerateObject().Count());
    }

    [Fact]
    public async Task A_tool_the_user_switched_off_is_neither_listed_nor_callable_and_a_listener_hears_of_it()
    {
        await using var rig = Rig.Create();
        const int listen = 9000;
        await rig.SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{listen},\"method\":\"subscriptions/listen\",\"params\":{{{Meta},\"notifications\":{{\"toolsListChanged\":true}}}}}}");
        await T.Until(() => { lock (rig.Notifications) return rig.Notifications.Count > 0; });
        rig.Server.SetEnabled("editor_search_bytes", false);
        Assert.DoesNotContain("editor_search_bytes", (await rig.ModernAsync("tools/list")).Result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", "\"name\":\"editor_search_bytes\",\"arguments\":{}")).Code);
        await T.Until(() => { lock (rig.Notifications) return rig.Notifications.Any(n => n.GetProperty("method").GetString() == "notifications/tools/list_changed"); });
        JsonElement[] seen;
        lock (rig.Notifications) seen = [.. rig.Notifications];
        Assert.Equal("notifications/subscriptions/acknowledged", seen[0].GetProperty("method").GetString());
        Assert.All(seen, n => Assert.Equal(listen, n.GetProperty("params").GetProperty("_meta").GetProperty("io.modelcontextprotocol/subscriptionId").GetInt32()));
        rig.Server.SetEnabled("editor_search_bytes", true);
        Assert.Contains("editor_search_bytes", (await rig.ModernAsync("tools/list")).Result.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        // the client cancels the stream: no answer is sent for it
        await rig.SendAsync($"{{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{{\"requestId\":{listen}}}}}");
        await T.Until(() => rig.Server.ConnectionCount == 1);
    }

    // ---- P6.3.1 ----

    [Fact]
    public async Task A_tab_that_is_not_shared_is_not_listed_and_cannot_be_read_and_withdrawing_stops_the_next_read()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(rig.NewDocument(Utf8("shared text\n")));
        var hidden = rig.NewDocument(Utf8("secret plans\n"));
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; }); // visible to plugins that were granted it, not to the assistant

        var list = (await rig.ToolAsync("editor_list_documents")).Json.GetProperty("documents");
        Assert.Equal([shared.Id.ToString()], list.EnumerateArray().Select(d => d.GetProperty("id").GetString()));

        var refused = await rig.ToolAsync("editor_read_range", rig.Args(hidden));
        Assert.True(refused.IsError);
        Assert.DoesNotContain("secret", refused.Text);
        var unknown = await rig.ToolAsync("editor_read_range", $"{{\"document\":\"{Guid.NewGuid()}\"}}");
        Assert.True(unknown.IsError);
        Assert.True((await rig.ToolAsync("editor_search", rig.Args(hidden, "\"text\":\"secret\""))).IsError);

        var read = await rig.ToolAsync("editor_read_range", rig.Args(shared));
        Assert.False(read.IsError);
        Assert.Equal("shared text\n", read.Json.GetProperty("text").GetString());

        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(shared.Id));
        var after = await rig.ToolAsync("editor_read_range", rig.Args(shared));
        Assert.True(after.IsError);
        Assert.Contains("not shared", after.Text);
        Assert.Empty((await rig.ToolAsync("editor_list_documents")).Json.GetProperty("documents").EnumerateArray());
        Assert.Contains(rig.CallLog.Entries(), e => e.PluginId == McpServer.ClientId && e.Outcome == CallOutcome.Denied);
    }

    [Fact]
    public async Task The_assistant_reads_unsaved_text_and_proposes_a_change_the_user_applies_while_a_stale_revision_is_refused()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.NewDocument(Utf8("alpha beta gamma\n")));
        Assert.True(rig.Owner.Run(() => doc.Insert(0, Utf8("unsaved ")).Applied)); // not on disk, only in the editor
        long revision = rig.Owner.Run(() => doc.Revision);

        var read = (await rig.ToolAsync("editor_read_range", rig.Args(doc))).Json;
        Assert.Equal("unsaved alpha beta gamma\n", read.GetProperty("text").GetString());
        Assert.Equal(revision, read.GetProperty("revision").GetInt64());

        // an offset computed on the revision before the edit is refused, nothing reaches the user
        var stale = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, $"\"revision\":{revision - 1},\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"x\"}}]"));
        Assert.True(stale.IsError);
        Assert.Contains("-32020", stale.Text);
        Assert.Empty(rig.Reviewer.Seen);

        var ambiguous = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, $"\"revision\":{revision},\"edits\":[{{\"find\":\"a\",\"text\":\"x\"}}]"));
        Assert.True(ambiguous.IsError);
        Assert.Contains("exactly once", ambiguous.Text);

        var proposed = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, $"\"revision\":{revision},\"title\":\"Rename beta\",\"edits\":[{{\"find\":\"beta\",\"text\":\"delta\"}}]"));
        Assert.False(proposed.IsError, proposed.Text);
        var id = proposed.Json.GetProperty("proposal").GetString()!;
        Assert.Equal("pending", proposed.Json.GetProperty("status").GetString());
        Assert.Equal("unsaved alpha beta gamma\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength)))); // the proposal wrote nothing
        Assert.Equal("pending", (await rig.ToolAsync("editor_get_change_status", $"{{\"proposal\":\"{id}\"}}")).Json.GetProperty("status").GetString());
        var proposal = Assert.Single(rig.Reviewer.Seen);
        Assert.Equal("mcp", proposal.PluginId);
        Assert.Equal("Rename beta", proposal.Title);
        Assert.False(proposal.RawBytes);

        // the user looks at the diff and applies it
        var applied = rig.Owner.Run(() => doc.Apply(proposal.Edits, new EditOptions { BaseRevision = proposal.Revision, Source = "plugin:mcp" }));
        Assert.True(applied.Applied);
        rig.Reviewer.Decision.SetResult(new ReviewOutcome(ProposalStatus.Applied, applied.Revision));
        await T.Until(() => rig.Ledger.Get(id)!.Status == ProposalStatus.Applied);
        var status = (await rig.ToolAsync("editor_get_change_status", $"{{\"proposal\":\"{id}\"}}")).Json;
        Assert.Equal("applied", status.GetProperty("status").GetString());
        Assert.Equal(applied.Revision, status.GetProperty("appliedRevision").GetInt64());
        Assert.Equal("unknown", (await rig.ToolAsync("editor_get_change_status", "{\"proposal\":\"nope\"}")).Json.GetProperty("status").GetString());
        Assert.Equal("unsaved alpha delta gamma\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength))));
    }

    [Fact]
    public async Task A_proposal_for_a_tab_with_only_read_access_is_refused()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.NewDocument(Utf8("read only\n")), propose: false);
        var reply = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, "\"revision\":0,\"edits\":[{\"start\":0,\"end\":1,\"text\":\"R\"}]"));
        Assert.True(reply.IsError);
        Assert.Empty(rig.Reviewer.Seen);
    }

    [Fact]
    public async Task Values_of_an_env_file_reach_the_assistant_as_stars_until_the_user_allows_them()
    {
        await using var rig = Rig.Create();
        var dir = T.TempDir();
        try
        {
            var path = Path.Combine(dir, ".env");
            File.WriteAllBytes(path, Utf8("# keys\nAPI_KEY=hunter2\nOTHER=plain value\n"));
            var doc = Document.Open(path, PagedBackendFactory.Instance);
            rig.Share(doc);

            var read = (await rig.ToolAsync("editor_read_range", rig.Args(doc))).Json.GetProperty("text").GetString()!;
            Assert.DoesNotContain("hunter2", read);
            Assert.Contains("API_KEY=*******", read);
            var search = await rig.ToolAsync("editor_search", rig.Args(doc, "\"text\":\"hunter2\""));
            Assert.Equal(0, search.Json.GetProperty("matches").GetArrayLength());
            var snippets = await rig.ToolAsync("editor_search", rig.Args(doc, "\"text\":\"API_KEY\""));
            Assert.DoesNotContain("hunter2", snippets.Text);
            var raw = await rig.ToolAsync("editor_read_bytes", rig.Args(doc, "\"start\":0,\"length\":100,\"encoding\":\"base64\""));
            Assert.DoesNotContain("hunter2", Encoding.UTF8.GetString(Convert.FromBase64String(raw.Json.GetProperty("data").GetString()!)));
            Assert.Empty((await rig.ToolAsync("editor_search_bytes", rig.Args(doc, "\"pattern\":\"68 75 6E 74 65 72 32\""))).Json.GetProperty("matches").EnumerateArray());
            var edit = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, "\"revision\":0,\"edits\":[{\"start\":17,\"end\":24,\"text\":\"leaked\"}]"));
            Assert.True(edit.IsError);
            Assert.Empty(rig.Reviewer.Seen);
            Assert.DoesNotContain("hunter2", string.Join("\n", rig.CallLog.Entries().Select(e => e.ToString())));

            // after the user's confirmation for this tab the values come as they are
            rig.Permissions.Grant(McpServer.ClientId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(doc.Id));
            Assert.Contains("hunter2", (await rig.ToolAsync("editor_read_range", rig.Args(doc))).Json.GetProperty("text").GetString());
            rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task A_virtual_document_the_user_shared_is_read_through_the_same_gate_and_takes_no_proposal()
    {
        await using var rig = Rig.Create();
        var doc = Document.CreateVirtual(".env", Utf8("API_KEY=hunter2\nname=shown\n"), PagedBackendFactory.Instance, TextCodec.Utf8);
        rig.Share(doc);

        var listed = (await rig.ToolAsync("editor_list_documents")).Json.GetProperty("documents");
        Assert.Equal(doc.Id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetGuid());
        var read = (await rig.ToolAsync("editor_read_range", rig.Args(doc))).Json.GetProperty("text").GetString()!;
        Assert.DoesNotContain("hunter2", read);
        Assert.Contains("API_KEY=*******", read);
        Assert.Empty((await rig.ToolAsync("editor_search", rig.Args(doc, "\"text\":\"hunter2\""))).Json.GetProperty("matches").EnumerateArray());

        var edit = await rig.ToolAsync("editor_prepare_changes", rig.Args(doc, $"\"revision\":{doc.Revision},\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"x\"}}]"));
        Assert.True(edit.IsError);
        Assert.Contains("read only", edit.Text);
        Assert.Empty(rig.Reviewer.Seen);
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
    }

    [Fact]
    public async Task The_selection_tool_reports_each_range_with_its_line_and_text_and_cuts_the_text_at_the_size_limit()
    {
        await using var rig = Rig.Create(o => o with { MaxRangeBytes = 8 });
        var doc = rig.NewDocument(Utf8("alpha beta\ngamma delta\n"));
        rig.Share(doc, selections: () => SelectionSet.Create([TextSelection.At(0), new TextSelection(6, 10), new TextSelection(11, 22)], 1));
        long revision = rig.Owner.Run(() => doc.Revision);

        var reply = (await rig.ToolAsync("editor_get_selection", rig.Args(doc))).Json;
        Assert.Equal(revision, reply.GetProperty("revision").GetInt64());
        Assert.Equal(1, reply.GetProperty("primary").GetInt32());
        var rows = reply.GetProperty("selections").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("", rows[0].GetProperty("text").GetString());
        Assert.Equal("beta", rows[1].GetProperty("text").GetString());
        Assert.Equal((6, 10, 1), (rows[1].GetProperty("start").GetInt32(), rows[1].GetProperty("end").GetInt32(), rows[1].GetProperty("line").GetInt32()));
        Assert.Equal("gamm", rows[2].GetProperty("text").GetString()); // 8 bytes for all: 4 were used by "beta"
        Assert.True(rows[2].GetProperty("truncated").GetBoolean());
        Assert.Equal(22, rows[2].GetProperty("end").GetInt64());
        Assert.Equal(2, rows[2].GetProperty("line").GetInt32());
        Assert.True(reply.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task The_selection_tool_names_a_tab_without_a_text_view_and_refuses_one_that_is_not_shared()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(rig.NewDocument(Utf8("no view\n")));
        var none = (await rig.ToolAsync("editor_get_selection", rig.Args(shared))).Json;
        Assert.Equal(0, none.GetProperty("selections").GetArrayLength());
        Assert.Contains("no cursor", none.GetProperty("note").GetString());

        var hidden = rig.NewDocument(Utf8("secret plans\n"));
        rig.Owner.Run(() => { rig.Api.Expose(hidden, () => SelectionSet.Single(new TextSelection(0, 6))); return 0; });
        var refused = await rig.ToolAsync("editor_get_selection", rig.Args(hidden));
        Assert.True(refused.IsError);
        Assert.DoesNotContain("secret", refused.Text);
    }

    [Fact]
    public async Task The_selection_tool_hides_env_values_until_the_user_allows_them()
    {
        await using var rig = Rig.Create();
        var doc = Document.CreateVirtual(".env", Utf8("API_KEY=hunter2\nname=shown\n"), PagedBackendFactory.Instance, TextCodec.Utf8);
        rig.Share(doc, selections: () => SelectionSet.Single(new TextSelection(0, 15)));
        var hidden = await rig.ToolAsync("editor_get_selection", rig.Args(doc));
        Assert.DoesNotContain("hunter2", hidden.Text);
        Assert.Equal("API_KEY=*******", hidden.Json.GetProperty("selections")[0].GetProperty("text").GetString());

        rig.Permissions.Grant(McpServer.ClientId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(doc.Id));
        Assert.Equal("API_KEY=hunter2", (await rig.ToolAsync("editor_get_selection", rig.Args(doc))).Json.GetProperty("selections")[0].GetProperty("text").GetString());
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
    }

    [Fact]
    public async Task Proposals_waiting_for_the_user_are_limited_and_a_decision_makes_room()
    {
        await using var rig = Rig.Create(o => o with { MaxPendingProposals = 2 });
        var doc = rig.Share(rig.NewDocument(Utf8("alpha beta gamma\n")));
        long revision = rig.Owner.Run(() => doc.Revision);
        string Text(string text) => rig.Args(doc, $"\"revision\":{revision},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"{text}\"}}]");

        Assert.False((await rig.ToolAsync("editor_prepare_changes", Text("x"))).IsError);
        Assert.False((await rig.ToolAsync("editor_prepare_byte_changes", rig.Args(doc, $"\"revision\":{revision},\"edits\":[{{\"offset\":0,\"bytes\":\"41\"}}]"))).IsError);
        Assert.Equal(2, rig.Ledger.PendingCount);

        var refused = await rig.ToolAsync("editor_prepare_changes", Text("y"));
        Assert.True(refused.IsError);
        Assert.Contains("limit", refused.Text);
        Assert.Equal(2, rig.Reviewer.Seen.Count); // the refused proposal never reached the user
        Assert.True((await rig.ToolAsync("editor_prepare_byte_changes", rig.Args(doc, $"\"revision\":{revision},\"edits\":[{{\"offset\":0,\"bytes\":\"41\"}}]"))).IsError);

        rig.Reviewer.Decision.SetResult(new ReviewOutcome(ProposalStatus.Rejected));
        await T.Until(() => rig.Ledger.PendingCount == 0);
        Assert.False((await rig.ToolAsync("editor_prepare_changes", Text("z"))).IsError);
    }

    [Fact]
    public async Task A_result_larger_than_the_size_limit_is_refused_with_the_advice_to_ask_for_less()
    {
        await using var rig = Rig.Create(o => o with { MaxResultChars = 300 });
        var doc = rig.Share(rig.NewDocument(Utf8(new string('x', 1000))));
        var large = await rig.ToolAsync("editor_read_range", rig.Args(doc));
        Assert.True(large.IsError);
        Assert.Contains("limit is 300", large.Text);
        Assert.DoesNotContain("xxxxxxxxxx", large.Text);

        var small = await rig.ToolAsync("editor_read_range", rig.Args(doc, "\"start\":0,\"end\":50"));
        Assert.False(small.IsError, small.Text);
        Assert.Equal(new string('x', 50), small.Json.GetProperty("text").GetString());
    }

    // ---- P6.3.2 ----

    [Fact]
    public async Task File_facts_of_a_cp1250_file_with_mixed_line_endings_name_both()
    {
        await using var rig = Rig.Create();
        // "Příliš žluťoučký kůň" in Windows-1250, then one CRLF, one LF and one CR
        var czech = Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var bytes = czech.GetBytes("Příliš žluťoučký\r\nkůň\npěl\rdědy\n");
        var doc = rig.Share(rig.NewDocument(bytes, TextCodec.Cp1250));
        var facts = (await rig.ToolAsync("editor_file_facts", rig.Args(doc))).Json;
        Assert.Equal("Windows-1250", facts.GetProperty("codec").GetString());
        Assert.Equal(0, facts.GetProperty("bomLength").GetInt32());
        var eol = facts.GetProperty("lineEndings");
        Assert.Equal(2, eol.GetProperty("lf").GetInt32());
        Assert.Equal(1, eol.GetProperty("crlf").GetInt32());
        Assert.Equal(1, eol.GetProperty("cr").GetInt32());
        Assert.Equal("mixed", eol.GetProperty("style").GetString());
        Assert.Equal(0, facts.GetProperty("invalidSequences").GetInt32());
        Assert.True(facts.GetProperty("complete").GetBoolean());
        Assert.Equal(bytes.Length, facts.GetProperty("byteLength").GetInt64());
        Assert.False(string.IsNullOrEmpty(facts.GetProperty("storage").GetString()));
    }

    [Fact]
    public async Task File_facts_count_invalid_utf8_and_a_bom()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.NewDocument([0xEF, 0xBB, 0xBF, (byte)'a', 0xFF, (byte)'b', 0xC3, (byte)'\n']));
        var facts = (await rig.ToolAsync("editor_file_facts", rig.Args(doc))).Json;
        Assert.Equal(3, facts.GetProperty("bomLength").GetInt32());
        Assert.Equal("EFBBBF", facts.GetProperty("bom").GetString());
        Assert.Equal(2, facts.GetProperty("invalidSequences").GetInt32());
        Assert.Equal("lf", facts.GetProperty("lineEndings").GetProperty("dominant").GetString());
    }

    [Fact]
    public async Task The_outline_comes_from_the_symbols_a_provider_published_for_the_live_text()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.NewDocument(Utf8("# Title\n## Part\n")));
        Assert.Empty((await rig.ToolAsync("editor_outline", rig.Args(doc))).Json.GetProperty("sets").EnumerateArray());
        Assert.Equal(1, rig.OutlineRequests);

        rig.Permissions.Grant("provider", DocumentPermissions.Read, GrantScope.All);
        using var args = JsonDocument.Parse($"{{\"document\":\"{doc.Id}\",\"revision\":{rig.Owner.Run(() => doc.Revision)},\"symbols\":[{{\"id\":\"1\",\"name\":\"Title\",\"kind\":\"heading\",\"start\":0,\"end\":7}},{{\"id\":\"2\",\"name\":\"Part\",\"kind\":\"heading\",\"parent\":\"1\",\"start\":8,\"end\":15}}]}}");
        await rig.Api.HandleAsync("provider", DocumentMethods.SymbolsPublish, args.RootElement, default);

        var outline = (await rig.ToolAsync("editor_outline", rig.Args(doc))).Json;
        var set = Assert.Single(outline.GetProperty("sets").EnumerateArray());
        Assert.Equal("provider", set.GetProperty("provider").GetString());
        Assert.True(set.GetProperty("current").GetBoolean());
        var symbols = set.GetProperty("symbols").EnumerateArray().ToList();
        Assert.Equal(["Title", "Part"], symbols.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal("Title", symbols[1].GetProperty("parent").GetString());

        Assert.True(rig.Owner.Run(() => doc.Insert(0, Utf8("x")).Applied));
        Assert.False((await rig.ToolAsync("editor_outline", rig.Args(doc))).Json.GetProperty("sets")[0].GetProperty("current").GetBoolean());
    }

    // ---- P8.4.1 ----

    static void Publish(Rig rig, Document doc, params (long Start, long End, string Message)[] items) =>
        rig.Owner.Run(() => rig.Api.PublishOwn(doc, "lint", doc.Revision, [.. items.Select(x => new Diagnostic(DiagnosticSeverity.Warning, x.Message, "W1", new ByteRange(x.Start, x.End)))]));

    [Fact]
    public async Task Diagnostics_of_a_shared_tab_come_with_line_and_follow_the_text()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.NewDocument(Utf8("one\ntwo\n")));
        Assert.Empty((await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json.GetProperty("diagnostics").EnumerateArray());
        Publish(rig, doc, (4, 7, "suspicious"));
        var reply = (await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json;
        var d = Assert.Single(reply.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("suspicious", d.GetProperty("message").GetString());
        Assert.Equal("warning", d.GetProperty("severity").GetString());
        Assert.Equal("lint", d.GetProperty("source").GetString());
        Assert.Equal(2, d.GetProperty("line").GetInt32());
        Assert.Equal(0, reply.GetProperty("withheld").GetInt32());

        Assert.True(rig.Owner.Run(() => doc.Insert(0, Utf8("x")).Applied));
        Assert.Equal(5, (await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json.GetProperty("diagnostics")[0].GetProperty("start").GetInt64());
    }

    [Fact]
    public async Task Diagnostics_beyond_the_item_limit_are_cut_and_the_result_says_so()
    {
        await using var rig = Rig.Create(o => o with { MaxItems = 2 });
        var doc = rig.Share(rig.NewDocument(Utf8("one\ntwo\nthree\n")));
        Publish(rig, doc, (0, 3, "a"), (4, 7, "b"));
        Assert.False((await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json.GetProperty("truncated").GetBoolean());
        Publish(rig, doc, (0, 3, "a"), (4, 7, "b"), (8, 13, "c"));
        var cut = (await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json;
        Assert.Equal(2, cut.GetProperty("diagnostics").GetArrayLength());
        Assert.True(cut.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Diagnostics_of_a_tab_that_is_not_shared_are_refused()
    {
        await using var rig = Rig.Create();
        var doc = rig.NewDocument(Utf8("one\n"));
        rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
        Publish(rig, doc, (0, 3, "not for the assistant"));
        var reply = await rig.ToolAsync("editor_diagnostics", rig.Args(doc));
        Assert.True(reply.IsError);
        Assert.DoesNotContain("not for the assistant", reply.Text);
    }

    [Fact]
    public async Task A_diagnostic_on_an_env_value_is_withheld_until_the_user_allows_the_values()
    {
        await using var rig = Rig.Create();
        var doc = Document.CreateVirtual(".env", Utf8("API_KEY=hunter2\nname=shown\n"), PagedBackendFactory.Instance, TextCodec.Utf8);
        rig.Share(doc);
        Publish(rig, doc, (8, 15, "value hunter2 looks weak"), (16, 20, "name is fine"));
        var reply = await rig.ToolAsync("editor_diagnostics", rig.Args(doc));
        Assert.DoesNotContain("hunter2", reply.Text);
        Assert.Equal(1, reply.Json.GetProperty("withheld").GetInt32());
        Assert.Equal("name is fine", Assert.Single(reply.Json.GetProperty("diagnostics").EnumerateArray()).GetProperty("message").GetString());

        rig.Permissions.Grant(McpServer.ClientId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(doc.Id));
        Assert.Equal(2, (await rig.ToolAsync("editor_diagnostics", rig.Args(doc))).Json.GetProperty("diagnostics").GetArrayLength());
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
    }

    [Fact]
    public async Task Find_in_files_searches_the_live_text_of_the_shared_documents_only()
    {
        await using var rig = Rig.Create();
        var one = rig.Share(rig.NewDocument(Utf8("needle in one\n")));
        var two = rig.Share(rig.NewDocument(Utf8("nothing\n")));
        var three = rig.NewDocument(Utf8("needle in three\n"));
        rig.Owner.Run(() => { rig.Api.Expose(three); return 0; });
        Assert.True(rig.Owner.Run(() => two.Insert(0, Utf8("a needle appears only in the editor ")).Applied));

        var found = (await rig.ToolAsync("editor_find_in_files", "{\"text\":\"needle\"}")).Json;
        var documents = found.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(new[] { one.Id.ToString(), two.Id.ToString() }.Order(), documents.Select(d => d.GetProperty("document").GetString()).Order());
        Assert.DoesNotContain(three.Id.ToString(), found.GetRawText());
        Assert.All(documents, d => Assert.Contains("needle", d.GetProperty("matches")[0].GetProperty("snippet").GetString()));
        Assert.False(found.GetProperty("truncated").GetBoolean());
        Assert.True((await rig.ToolAsync("editor_find_in_files", "{\"text\":\"needle\",\"maxResults\":1}")).Json.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Log_query_returns_the_lines_of_a_level_and_says_where_to_go_on()
    {
        await using var rig = Rig.Create();
        var log = new StringBuilder();
        for (int i = 0; i < 40; i++) log.Append(i % 10 == 3 ? $"2026-01-01 ERROR failure number {i}\n" : $"2026-01-01 INFO line {i}\n");
        var doc = rig.Share(rig.NewDocument(Utf8(log.ToString())));
        var first = (await rig.ToolAsync("editor_log_query", rig.Args(doc, "\"level\":\"error\",\"maxLines\":2"))).Json;
        Assert.Equal(["2026-01-01 ERROR failure number 3", "2026-01-01 ERROR failure number 13"], first.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("text").GetString()));
        var next = first.GetProperty("next").GetInt64();
        var rest = (await rig.ToolAsync("editor_log_query", rig.Args(doc, $"\"level\":\"error\",\"start\":{next}"))).Json;
        Assert.Equal(["2026-01-01 ERROR failure number 23", "2026-01-01 ERROR failure number 33"], rest.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("text").GetString()));
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("next").ValueKind);
        Assert.Equal(0, (await rig.ToolAsync("editor_log_query", rig.Args(doc, "\"level\":\"warning\""))).Json.GetProperty("lines").GetArrayLength());
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", $"\"name\":\"editor_log_query\",\"arguments\":{rig.Args(doc, "\"level\":\"loud\"")}")).Code);
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task Log_query_answers_from_a_ten_gibibyte_log_with_lines_and_without_reading_all_of_it()
    {
        const long length = 10L << 30;
        using var file = new TempSparse(length);
        long[] errorAt = [0, 7L << 30, length - 64];
        file.Write(errorAt[0], "2026-01-01 ERROR first failure\n");
        file.Write(errorAt[1], "\n2026-01-01 ERROR failure at seven gigabytes\n");
        file.Write(errorAt[2], "\n2026-01-01 ERROR last failure\n");
        await using var rig = Rig.Create(maxScanBytes: 1L << 30);
        var doc = Document.Open(file.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        rig.Share(doc);
        long before = GC.GetTotalMemory(true);

        var first = (await rig.ToolAsync("editor_log_query", rig.Args(doc, "\"level\":\"error\",\"maxLines\":1"))).Json;
        Assert.Equal("2026-01-01 ERROR first failure", first.GetProperty("lines")[0].GetProperty("text").GetString());
        // the next call is told to stop after a bounded scan and where it went on, so the whole file is never read in one call
        var second = (await rig.ToolAsync("editor_log_query", rig.Args(doc, $"\"level\":\"error\",\"start\":{first.GetProperty("next").GetInt64()},\"maxLines\":1"))).Json;
        Assert.Equal(0, second.GetProperty("lines").GetArrayLength());
        long scannedTo = second.GetProperty("next").GetInt64();
        Assert.InRange(scannedTo, 1L << 29, 3L << 30);
        var third = (await rig.ToolAsync("editor_log_query", rig.Args(doc, $"\"level\":\"error\",\"start\":{(7L << 30) - 1024},\"maxLines\":1"))).Json;
        Assert.Equal("2026-01-01 ERROR failure at seven gigabytes", third.GetProperty("lines")[0].GetProperty("text").GetString());
        Assert.InRange(GC.GetTotalMemory(true) - before, -1, 128L << 20);
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task Withdrawing_access_stops_a_scan_of_a_ten_gibibyte_log_that_is_running()
    {
        const long length = 10L << 30;
        using var file = new TempSparse(length);
        file.Write(length - 64, "\n2026-01-01 ERROR last failure\n");
        await using var rig = Rig.Create(maxScanBytes: 100L << 30);
        var doc = Document.Open(file.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        rig.Share(doc);

        var query = rig.ToolAsync("editor_log_query", rig.Args(doc, "\"level\":\"error\",\"maxLines\":1"));
        await Task.Delay(1500);
        Assert.False(query.IsCompleted, "the scan was expected to take longer than this");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
        var reply = await query.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True(reply.IsError);
        Assert.Contains("withdrew", reply.Text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
    }

    sealed class TempSparse : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(T.TempDir(), "big.log");

        public TempSparse(long length)
        {
            File.WriteAllBytes(Path, []);
            if (OperatingSystem.IsWindows())
            {
                var command = new System.Diagnostics.ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
                foreach (var arg in new[] { "sparse", "setflag", Path }) command.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(command)!;
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write);
            stream.SetLength(length);
        }

        public void Write(long offset, string text)
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write);
            stream.Position = offset;
            stream.Write(Utf8(text));
        }

        public void Dispose() => Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true);
    }

    // ---- P6.3.3 ----

    [Fact]
    public async Task The_assistant_reads_256_bytes_finds_a_pattern_and_proposes_four_bytes_that_the_user_sees_as_bytes()
    {
        await using var rig = Rig.Create();
        var content = new byte[1024];
        for (int i = 0; i < content.Length; i++) content[i] = (byte)(i * 7 + 3);
        content[600] = 0xCA; content[601] = 0xFE; content[602] = 0xBA; content[603] = 0xBE;
        var doc = rig.Share(rig.NewDocument(content, TextCodec.Cp1252));
        long revision = rig.Owner.Run(() => doc.Revision);

        var read = (await rig.ToolAsync("editor_read_bytes", rig.Args(doc, "\"start\":300,\"length\":256"))).Json;
        Assert.Equal(Convert.ToHexString(content.AsSpan(300, 256)), read.GetProperty("data").GetString());
        Assert.Equal(300, read.GetProperty("start").GetInt64());
        Assert.Equal(556, read.GetProperty("end").GetInt64());
        var base64 = (await rig.ToolAsync("editor_read_bytes", rig.Args(doc, "\"start\":300,\"length\":16,\"encoding\":\"base64\""))).Json;
        Assert.Equal(content.AsSpan(300, 16).ToArray(), Convert.FromBase64String(base64.GetProperty("data").GetString()!));
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", $"\"name\":\"editor_read_bytes\",\"arguments\":{rig.Args(doc, "\"start\":0,\"length\":65537")}")).Code);

        var found = (await rig.ToolAsync("editor_search_bytes", rig.Args(doc, "\"pattern\":\"CA FE ?? BE\""))).Json;
        var match = Assert.Single(found.GetProperty("matches").EnumerateArray());
        Assert.Equal(600, match.GetProperty("start").GetInt64());
        Assert.Equal(604, match.GetProperty("end").GetInt64());
        Assert.True((await rig.ToolAsync("editor_search_bytes", rig.Args(doc, "\"pattern\":\"zz\""))).IsError);

        var proposed = await rig.ToolAsync("editor_prepare_byte_changes", rig.Args(doc, $"\"revision\":{revision},\"title\":\"Patch magic\",\"edits\":[{{\"offset\":600,\"bytes\":\"DE AD BE EF\"}}]"));
        Assert.False(proposed.IsError, proposed.Text);
        var proposal = Assert.Single(rig.Reviewer.Seen);
        Assert.True(proposal.RawBytes);
        var edit = Assert.Single(proposal.Edits);
        Assert.Equal(new ByteRange(600, 604), edit.Range);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, edit.Insert.ToArray());
        Assert.Equal(content, rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));

        // an edit based on an older revision is refused
        Assert.True(rig.Owner.Run(() => doc.Apply([new ByteEdit(new ByteRange(0, 1), new byte[] { 1 })], new EditOptions { RawBytes = true }).Applied));
        var stale = await rig.ToolAsync("editor_prepare_byte_changes", rig.Args(doc, $"\"revision\":{revision},\"edits\":[{{\"offset\":600,\"bytes\":\"00\"}}]"));
        Assert.True(stale.IsError);
        Assert.Contains("-32020", stale.Text);
        Assert.Single(rig.Reviewer.Seen);
        Assert.Equal(-32602, (await rig.ModernAsync("tools/call", $"\"name\":\"editor_prepare_byte_changes\",\"arguments\":{rig.Args(doc, $"\"revision\":{revision + 1},\"edits\":[{{\"offset\":600,\"bytes\":\"0\"}}]")}")).Code);
    }
}

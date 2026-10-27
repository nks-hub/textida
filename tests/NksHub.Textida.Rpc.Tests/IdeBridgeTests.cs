using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>The /ide bridge: discovery through the lock file, the token, the selection notification and the path of a proposal through the same review as every assistant.</summary>
public class IdeBridgeTests
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

    sealed class NoTools : IEditorTools
    {
        public void SymbolsChanged(SymbolSet set, bool current) { }
        public void SymbolsCleared(string pluginId, Guid document) { }
        public bool Reveal(Guid document, ByteRange range) => false;
        public void PlaceSelections(Guid document, SelectionSet selections) { }
        public string NewLine(Guid document) => "\n";
        public TableSelectionData? TableSelection(Guid document) => null;
        public void SetClipboard(Guid document, string text, string? html) { }
        public Document OpenVirtual(string pluginId, string name, string? title, ReadOnlyMemory<byte> content, TextCodec codec, VirtualInfo info) => throw new NotSupportedException();
        public bool OpenDiff(DiffRequest request) => false;
        public void DiagnosticsChanged(Guid document) { }
    }

    /// <summary>The user in front of the change review: sees each proposal and answers when the test says so.</summary>
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

    sealed class Rig : IAsyncDisposable
    {
        public required DocumentApi Api { get; init; }
        public required McpServer Server { get; init; }
        public required PluginPermissions Permissions { get; init; }
        public required ProposalLedger Ledger { get; init; }
        public required FakeReviewer Reviewer { get; init; }
        public required OwnerLock Owner { get; init; }
        public required string LockDirectory { get; init; }
        public IdeBridge Bridge { get; private set; } = null!;

        public static Rig Create()
        {
            var permissions = new PluginPermissions();
            var ledger = new ProposalLedger(100);
            var reviewer = new FakeReviewer();
            var owner = new OwnerLock();
            var api = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 20, MaxFindMatches = 100, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10), EventQueueCapacity = 64, MaxRememberedBatches = 16,
                Permissions = permissions, CallLog = new PluginCallLog(1000), Proposals = new McpProposals(ledger, reviewer), Tools = new NoTools(),
                MaxSymbolsPerDocument = 100, MaxSymbolTextLength = 50, MaxSnippetLength = 200, MaxSnippetCarets = 4, TabTimeout = TimeSpan.FromMilliseconds(500), MaxSecretScanBytes = 1 << 20,
                MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20, MaxDiagnosticsPerDocument = 10, MaxCompletionItems = 10, MaxCompletionEdits = 4,
                CompletionTimeout = TimeSpan.FromSeconds(2),
            }, owner, (_, _, _) => Task.FromResult(false), (_, _, _, _, _) => throw new NotSupportedException());
            var server = new McpServer(new McpServerOptions
            {
                Documents = api, Proposals = ledger, Limits = T.Roomy, ServerName = "textida-test", ServerVersion = "0.0.1", MaxResultChars = 200_000, MaxRangeBytes = 4096,
                MaxItems = 50, SnippetBytes = 12, MaxLineChars = 200, MaxPendingProposals = 10,
            });
            var rig = new Rig { Api = api, Server = server, Permissions = permissions, Ledger = ledger, Reviewer = reviewer, Owner = owner, LockDirectory = Path.Combine(T.TempDir(), "ide") };
            rig.Bridge = new IdeBridge(new IdeBridgeOptions { Server = server, LockDirectory = rig.LockDirectory, HandshakeTimeout = TimeSpan.FromSeconds(5), MaxHeaderBytes = 8192 });
            return rig;
        }

        public Document Share(Document doc)
        {
            Owner.Run(() => { Api.Expose(doc); return 0; });
            Permissions.Grant(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
            Permissions.Grant(McpServer.ClientId, DocumentPermissions.EditPropose, GrantScope.OfDocument(doc.Id));
            return doc;
        }

        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            await Api.DisposeAsync();
            Directory.Delete(Path.GetDirectoryName(LockDirectory)!, true);
        }
    }

    /// <summary>What an agent does: finds the lock file, reads the token and connects.</summary>
    sealed class FakeAgent : IAsyncDisposable
    {
        readonly ClientWebSocket _socket = new();
        int _nextId;

        public List<JsonElement> Notifications { get; } = [];

        public static (int Port, string Token) Discover(string lockDirectory)
        {
            var file = Assert.Single(Directory.GetFiles(lockDirectory, "*.lock"));
            using var lockContent = JsonDocument.Parse(File.ReadAllBytes(file));
            return (int.Parse(Path.GetFileNameWithoutExtension(file)), lockContent.RootElement.GetProperty("authToken").GetString()!);
        }

        public static async Task<FakeAgent> ConnectAsync(string lockDirectory, string? token = null, bool useToken = true, string? origin = null)
        {
            var (port, found) = Discover(lockDirectory);
            var agent = new FakeAgent();
            if (useToken) agent._socket.Options.SetRequestHeader(IdeBridge.AuthorizationHeader, token ?? found);
            if (origin is not null) agent._socket.Options.SetRequestHeader("Origin", origin);
            agent._socket.Options.AddSubProtocol("mcp");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await agent._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token);
            return agent;
        }

        async Task SendAsync(string json) => await _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        async Task<JsonElement> ReceiveAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                Assert.Equal(WebSocketMessageType.Text, result.MessageType);
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);
            return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
        }

        public async Task<JsonElement> RequestAsync(string method, string paramsJson)
        {
            var id = ++_nextId;
            await SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}");
            while (true)
            {
                var message = await ReceiveAsync();
                if (message.TryGetProperty("id", out var got) && got.GetInt32() == id) return message;
                Notifications.Add(message);
            }
        }

        public async Task InitializeAsync()
        {
            var reply = await RequestAsync("initialize", "{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"fake-agent\",\"version\":\"1\"}}");
            Assert.False(reply.TryGetProperty("error", out _), reply.GetRawText());
            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        }

        public async Task<(bool IsError, string Text)> ToolTextAsync(string name, string argumentsJson = "{}")
        {
            var reply = await RequestAsync("tools/call", $"{{\"name\":\"{name}\",\"arguments\":{argumentsJson}}}");
            Assert.False(reply.TryGetProperty("error", out _), reply.GetRawText());
            var result = reply.GetProperty("result");
            return (result.GetProperty("isError").GetBoolean(), result.GetProperty("content")[0].GetProperty("text").GetString()!);
        }

        public async Task<JsonElement> ToolAsync(string name, string argumentsJson = "{}")
        {
            var (isError, text) = await ToolTextAsync(name, argumentsJson);
            Assert.False(isError, text);
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public async Task<JsonElement> NextNotificationAsync()
        {
            if (Notifications.Count > 0)
            {
                var first = Notifications[0];
                Notifications.RemoveAt(0);
                return first;
            }
            return await ReceiveAsync();
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    Document NewDocument(string text) => Document.FromBytes(Utf8(text), PagedBackendFactory.Instance, null);

    // ---- discovery and authentication ----

    [Fact]
    public async Task An_agent_finds_the_lock_file_connects_with_its_token_and_lists_tools()
    {
        await using var rig = Rig.Create();
        var (port, token) = FakeAgent.Discover(rig.LockDirectory);
        Assert.Equal(rig.Bridge.Port, port);
        Assert.InRange(port, IdeBridge.MinPort, IdeBridge.MaxPort);
        Assert.Equal(rig.Bridge.AuthToken, token);
        Assert.True(token.Length >= 32);

        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var tools = (await agent.RequestAsync("tools/list", "{}")).GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("getDiagnostics", tools);
        Assert.Contains("editor_prepare_changes", tools);
        Assert.Equal(0, (await agent.ToolAsync("getDiagnostics")).GetProperty("files").GetArrayLength());
        Assert.Equal(1, rig.Bridge.ConnectionCount);
    }

    [Fact]
    public async Task Diagnostics_reach_the_agent_for_shared_tabs_only()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(NewDocument("alpha\nbeta\n"));
        var hidden = NewDocument("secret\n");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; });
        rig.Owner.Run(() =>
        {
            rig.Api.PublishOwn(shared, "lint", shared.Revision, [new Diagnostic(DiagnosticSeverity.Error, "bad beta", "E1", new ByteRange(6, 10))]);
            rig.Api.PublishOwn(hidden, "lint", hidden.Revision, [new Diagnostic(DiagnosticSeverity.Error, "bad secret", "E2", new ByteRange(0, 6))]);
            return 0;
        });
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var result = await agent.ToolAsync("getDiagnostics");
        var file = Assert.Single(result.GetProperty("files").EnumerateArray());
        Assert.StartsWith("untitled:", file.GetProperty("uri").GetString());
        var d = Assert.Single(file.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("bad beta", d.GetProperty("message").GetString());
        Assert.Equal("Error", d.GetProperty("severity").GetString());
        Assert.Equal(1, d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.DoesNotContain("bad secret", result.GetRawText());
        Assert.Equal(0, (await agent.ToolAsync("getDiagnostics", "{\"uri\":\"untitled:other\"}")).GetProperty("files").GetArrayLength());
    }

    [Fact]
    public async Task A_wrong_missing_or_cross_site_connection_is_refused()
    {
        await using var rig = Rig.Create();
        await Assert.ThrowsAsync<WebSocketException>(() => FakeAgent.ConnectAsync(rig.LockDirectory, token: "not-the-token"));
        await Assert.ThrowsAsync<WebSocketException>(() => FakeAgent.ConnectAsync(rig.LockDirectory, useToken: false));
        await Assert.ThrowsAsync<WebSocketException>(() => FakeAgent.ConnectAsync(rig.LockDirectory, origin: "https://example.com"));
        Assert.Equal(0, rig.Bridge.ConnectionCount);
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory); // the right token still works after the refusals
        await agent.InitializeAsync();
    }

    [Fact]
    public async Task The_lock_file_is_private_to_the_user_and_goes_when_the_bridge_closes()
    {
        var rig = Rig.Create();
        var path = rig.Bridge.LockPath;
        Assert.Equal(Path.Combine(rig.LockDirectory, rig.Bridge.Port + ".lock"), path);
        if (OperatingSystem.IsWindows())
        {
            var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier));
            var rule = Assert.Single(rules.Cast<System.Security.AccessControl.FileSystemAccessRule>());
            Assert.Equal(System.Security.Principal.WindowsIdentity.GetCurrent().User, rule.IdentityReference);
            Assert.True(new FileInfo(path).GetAccessControl().AreAccessRulesProtected);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(rig.LockDirectory));
        }
        await rig.DisposeAsync();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_new_bridge_after_a_restart_has_a_new_token()
    {
        await using var first = Rig.Create();
        await using var second = Rig.Create();
        Assert.NotEqual(first.Bridge.AuthToken, second.Bridge.AuthToken);
    }

    // ---- selection ----

    [Fact]
    public async Task The_agent_hears_about_the_selection_of_a_shared_tab_and_of_nothing_else()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(NewDocument("alpha beta gamma\n"));
        var hidden = NewDocument("secret text\n");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; }); // open in the editor, but never shared with the assistant
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        Assert.False(await rig.Bridge.PublishSelectionAsync(hidden.Id, 0, 6));
        Assert.True(await rig.Bridge.PublishSelectionAsync(shared.Id, 6, 10));
        var update = await agent.NextNotificationAsync();
        Assert.Equal(IdeBridge.SelectionMethod, update.GetProperty("method").GetString());
        var p = update.GetProperty("params");
        Assert.Equal("beta", p.GetProperty("text").GetString());
        Assert.Equal(6, p.GetProperty("start").GetInt64());
        Assert.Equal(10, p.GetProperty("end").GetInt64());
        Assert.Equal(shared.Id.ToString(), p.GetProperty("document").GetString());

        // the user withdraws the tab: no more updates
        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(shared.Id));
        Assert.False(await rig.Bridge.PublishSelectionAsync(shared.Id, 0, 5));
    }

    [Fact]
    public async Task Hidden_values_of_an_env_file_are_not_in_the_selection()
    {
        await using var rig = Rig.Create();
        var dir = T.TempDir();
        try
        {
            var path = Path.Combine(dir, ".env");
            File.WriteAllBytes(path, Utf8("API_KEY=hunter2\n"));
            var doc = rig.Share(Document.Open(path, PagedBackendFactory.Instance));
            await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
            await agent.InitializeAsync();
            Assert.True(await rig.Bridge.PublishSelectionAsync(doc.Id, 0, 15));
            var text = (await agent.NextNotificationAsync()).GetProperty("params").GetProperty("text").GetString()!;
            Assert.DoesNotContain("hunter2", text);
            Assert.Contains("API_KEY=", text);
            rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; });
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- edits ----

    [Fact]
    public async Task A_proposed_edit_goes_to_review_and_is_applied_only_by_the_user()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(NewDocument("alpha beta gamma\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var revision = (await agent.ToolAsync("editor_list_documents")).GetProperty("documents")[0].GetProperty("revision").GetInt64();

        var proposed = await agent.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{doc.Id}\",\"revision\":{revision},\"title\":\"Rename beta\",\"edits\":[{{\"find\":\"beta\",\"text\":\"delta\"}}]}}");
        var id = proposed.GetProperty("proposal").GetString()!;
        Assert.Equal("pending", proposed.GetProperty("status").GetString());
        Assert.Equal("alpha beta gamma\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength)))); // nothing written before the decision
        var seen = Assert.Single(rig.Reviewer.Seen);
        Assert.Equal("Rename beta", seen.Title);

        var applied = rig.Owner.Run(() => doc.Apply(seen.Edits, new EditOptions { BaseRevision = seen.Revision, Source = "plugin:mcp" }));
        rig.Reviewer.Decision.SetResult(new ReviewOutcome(ProposalStatus.Applied, applied.Revision));
        await T.Until(() => rig.Ledger.Get(id)!.Status == ProposalStatus.Applied);
        Assert.Equal("applied", (await agent.ToolAsync("editor_get_change_status", $"{{\"proposal\":\"{id}\"}}")).GetProperty("status").GetString());
        Assert.Equal("alpha delta gamma\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength))));
    }

    [Fact]
    public async Task A_rejected_edit_leaves_the_text_alone_and_an_unshared_tab_takes_no_proposal()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(NewDocument("alpha beta gamma\n"));
        var other = NewDocument("not shared\n");
        rig.Owner.Run(() => { rig.Api.Expose(other); return 0; });
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        var refused = await agent.ToolTextAsync("editor_prepare_changes", $"{{\"document\":\"{other.Id}\",\"revision\":0,\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"x\"}}]}}");
        Assert.True(refused.IsError);
        Assert.Empty(rig.Reviewer.Seen);

        var proposed = await agent.ToolAsync("editor_prepare_changes", $"{{\"document\":\"{doc.Id}\",\"revision\":0,\"edits\":[{{\"find\":\"beta\",\"text\":\"delta\"}}]}}");
        var id = proposed.GetProperty("proposal").GetString()!;
        rig.Reviewer.Decision.SetResult(new ReviewOutcome(ProposalStatus.Rejected));
        await T.Until(() => rig.Ledger.Get(id)!.Status == ProposalStatus.Rejected);
        Assert.Equal("rejected", (await agent.ToolAsync("editor_get_change_status", $"{{\"proposal\":\"{id}\"}}")).GetProperty("status").GetString());
        Assert.Equal("alpha beta gamma\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength))));
    }
}

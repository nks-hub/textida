using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// The /ide bridge: discovery through the lock file, the token, the selection and diagnostics the agent gets, the diffs it opens in the change review, one bridge per window and the path of a proposal
/// through the same review as every assistant. The agent is a fake client over loopback; no run against a real Claude Code is part of these tests.
/// </summary>
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

    /// <summary>The editor's side of an open diff: records what it was asked to show and answers when the test says so. Like the editor, it answers "rejected" as soon as it is cancelled.</summary>
    sealed class FakeDiffs
    {
        public readonly List<IdeDiffRequest> Seen = [];
        public readonly List<CancellationToken> Tokens = [];
        readonly List<TaskCompletionSource<IdeDiffOutcome>> _decisions = [];

        public async Task<IdeDiffOutcome> Show(IdeDiffRequest request, CancellationToken cancellation)
        {
            var decision = new TaskCompletionSource<IdeDiffOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Seen)
            {
                Seen.Add(request);
                Tokens.Add(cancellation);
                _decisions.Add(decision);
            }
            using var _ = cancellation.Register(() => decision.TrySetResult(IdeDiffOutcome.Rejected));
            return await decision.Task;
        }

        /// <summary>The user decides the diff that was shown <paramref name="index"/>th.</summary>
        public void Decide(IdeDiffOutcome outcome, int index = 0)
        {
            lock (Seen) _decisions[index].TrySetResult(outcome);
        }

        public int Count
        {
            get { lock (Seen) return Seen.Count; }
        }
    }

    sealed class Rig : IAsyncDisposable
    {
        readonly List<IdeBridge> _extra = [];
        readonly List<Document> _files = [];

        public required DocumentApi Api { get; init; }
        public required McpServer Server { get; init; }
        public required PluginPermissions Permissions { get; init; }
        public required ProposalLedger Ledger { get; init; }
        public required FakeReviewer Reviewer { get; init; }
        public required OwnerLock Owner { get; init; }
        public required string LockDirectory { get; init; }
        public IdeBridge Bridge { get; private set; } = null!;

        /// <summary>The temporary folder that holds the lock directory and the files of the test.</summary>
        public string Root => Path.GetDirectoryName(LockDirectory)!;

        /// <param name="seed">Runs on the lock directory before the bridge starts (lock files an earlier editor left behind).</param>
        public static Rig Create(Func<IdeBridgeOptions, IdeBridgeOptions>? configure = null, Action<string>? seed = null, int maxPending = 10)
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
                MaxItems = 50, SnippetBytes = 12, MaxLineChars = 200, MaxPendingProposals = maxPending,
            });
            var rig = new Rig { Api = api, Server = server, Permissions = permissions, Ledger = ledger, Reviewer = reviewer, Owner = owner, LockDirectory = Path.Combine(T.TempDir(), "ide") };
            if (seed is not null)
            {
                Directory.CreateDirectory(rig.LockDirectory);
                seed(rig.LockDirectory);
            }
            rig.Bridge = rig.Build(configure);
            return rig;
        }

        IdeBridge Build(Func<IdeBridgeOptions, IdeBridgeOptions>? configure)
        {
            var options = new IdeBridgeOptions { Server = Server, LockDirectory = LockDirectory, HandshakeTimeout = TimeSpan.FromSeconds(5), MaxHeaderBytes = 8192, MaxAgentNameChars = 40 };
            return new IdeBridge(configure is null ? options : configure(options));
        }

        /// <summary>Another bridge on the same server and lock directory: another window of the same editor.</summary>
        public IdeBridge AddBridge(Func<IdeBridgeOptions, IdeBridgeOptions>? configure = null)
        {
            var bridge = Build(configure);
            _extra.Add(bridge);
            return bridge;
        }

        public Document Share(Document doc, Func<SelectionSet?>? selections = null)
        {
            Owner.Run(() => { Api.Expose(doc, selections); return 0; });
            Permissions.Grant(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
            Permissions.Grant(McpServer.ClientId, DocumentPermissions.EditPropose, GrantScope.OfDocument(doc.Id));
            return doc;
        }

        /// <summary>A document opened from a file of the test folder, so it has a path to give the agent.</summary>
        public Document FileDocument(string text, string name = "a.txt")
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, Utf8(text));
            var doc = Document.Open(path, PagedBackendFactory.Instance);
            _files.Add(doc);
            return doc;
        }

        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            foreach (var bridge in _extra) await bridge.DisposeAsync();
            foreach (var doc in _files) Owner.Run(() => { if (Api.IsExposed(doc.Id)) Api.Withdraw(doc); doc.Dispose(); return 0; });
            await Api.DisposeAsync();
            Directory.Delete(Root, true);
        }
    }

    /// <summary>What an agent does: finds the lock file, reads the token and connects.</summary>
    sealed class FakeAgent : IAsyncDisposable
    {
        readonly ClientWebSocket _socket = new();
        int _nextId;

        public List<JsonElement> Notifications { get; } = [];

        /// <summary>Finds the lock file the way an agent does: the one file of the directory, or the one of the given port when several editors (or windows) are running.</summary>
        public static (int Port, string Token) Discover(string lockDirectory, int? port = null)
        {
            var file = port is { } chosen ? Path.Combine(lockDirectory, chosen + ".lock") : Assert.Single(Directory.GetFiles(lockDirectory, "*.lock"));
            using var lockContent = JsonDocument.Parse(File.ReadAllBytes(file));
            return (int.Parse(Path.GetFileNameWithoutExtension(file)), lockContent.RootElement.GetProperty("authToken").GetString()!);
        }

        public static async Task<FakeAgent> ConnectAsync(string lockDirectory, string? token = null, bool useToken = true, string? origin = null, int? port = null)
        {
            var (listening, found) = Discover(lockDirectory, port);
            var agent = new FakeAgent();
            if (useToken) agent._socket.Options.SetRequestHeader(IdeBridge.AuthorizationHeader, token ?? found);
            if (origin is not null) agent._socket.Options.SetRequestHeader("Origin", origin);
            agent._socket.Options.AddSubProtocol("mcp");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await agent._socket.ConnectAsync(new Uri($"ws://127.0.0.1:{listening}"), timeout.Token);
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

        public async Task InitializeAsync(string name = "fake-agent", string version = "1")
        {
            var reply = await RequestAsync("initialize", $"{{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"{name}\",\"version\":\"{version}\"}}}}");
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
        foreach (var name in new[] { "getDiagnostics", "openDiff", "close_tab", "closeAllDiffTabs", "getCurrentSelection", "getLatestSelection", "openFile", "getOpenEditors", "getWorkspaceFolders", "checkDocumentDirty", "editor_prepare_changes" }) Assert.Contains(name, tools);
        Assert.Equal(0, (await agent.ToolAsync("getDiagnostics")).GetArrayLength());
        Assert.Equal(1, rig.Bridge.ConnectionCount);
    }

    [Fact]
    public async Task The_bridge_listens_on_the_loopback_interface_only()
    {
        await using var rig = Rig.Create();
        Assert.Equal(IPAddress.Loopback, rig.Bridge.EndPoint.Address);
        Assert.Equal(rig.Bridge.Port, rig.Bridge.EndPoint.Port);
    }

    [Fact]
    public async Task Diagnostics_reach_the_agent_for_shared_tabs_only()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(rig.FileDocument("alpha\nbeta\n"));
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
        var file = Assert.Single(result.EnumerateArray());
        var uri = new Uri(shared.FilePath!).AbsoluteUri;
        Assert.Equal(uri, file.GetProperty("uri").GetString());
        var d = Assert.Single(file.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("bad beta", d.GetProperty("message").GetString());
        Assert.Equal("Error", d.GetProperty("severity").GetString());
        Assert.Equal(1, d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(0, d.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal("lint", d.GetProperty("source").GetString());
        Assert.DoesNotContain("bad secret", result.GetRawText());
        Assert.Single((await agent.ToolAsync("getDiagnostics", $"{{\"uri\":\"{uri}\"}}")).EnumerateArray());
        Assert.Equal(0, (await agent.ToolAsync("getDiagnostics", "{\"uri\":\"untitled:other\"}")).GetArrayLength());
    }

    [Fact]
    public async Task The_lock_file_names_the_editor_and_the_folders_of_the_window_and_can_list_new_ones()
    {
        var first = Path.Combine(Path.GetTempPath(), "project-a");
        var second = Path.Combine(Path.GetTempPath(), "project-b");
        await using var rig = Rig.Create(o => o with { WorkspaceFolders = [first] });
        string[] Folders(out JsonElement root, out JsonDocument content)
        {
            content = JsonDocument.Parse(File.ReadAllBytes(rig.Bridge.LockPath));
            root = content.RootElement;
            return [.. root.GetProperty("workspaceFolders").EnumerateArray().Select(e => e.GetString()!)];
        }
        Assert.Equal(new[] { first }, Folders(out var root, out var content));
        Assert.Equal(Environment.ProcessId, root.GetProperty("pid").GetInt32());
        Assert.Equal(IdeBridge.IdeName, root.GetProperty("ideName").GetString());
        Assert.Equal("ws", root.GetProperty("transport").GetString());
        Assert.Equal(rig.Bridge.AuthToken, root.GetProperty("authToken").GetString());
        content.Dispose();

        Assert.True(rig.Bridge.UpdateWorkspaceFolders([first, second]));
        Assert.Equal(new[] { first, second }, Folders(out root, out content));
        Assert.Equal(rig.Bridge.AuthToken, root.GetProperty("authToken").GetString()); // the token stays; only the folders change
        content.Dispose();
    }

    [Fact]
    public async Task Lock_files_of_a_crashed_editor_are_removed_when_the_next_bridge_starts()
    {
        string dead = "", alive = "", otherIde = "", broken = "";
        await using var rig = Rig.Create(o => o with { IsProcessAlive = pid => pid == 4242 }, seed: dir =>
        {
            dead = Path.Combine(dir, "10001.lock");
            File.WriteAllText(dead, "{\"pid\":9999,\"ideName\":\"Textida\",\"authToken\":\"left-behind\"}");
            alive = Path.Combine(dir, "10002.lock");
            File.WriteAllText(alive, "{\"pid\":4242,\"ideName\":\"Textida\"}");
            otherIde = Path.Combine(dir, "10003.lock");
            File.WriteAllText(otherIde, "{\"pid\":9999,\"ideName\":\"Another IDE\"}");
            broken = Path.Combine(dir, "10004.lock");
            File.WriteAllText(broken, "not json");
        });
        Assert.False(File.Exists(dead)); // the editor that wrote it is gone
        Assert.True(File.Exists(alive)); // a running instance keeps its lock file
        Assert.True(File.Exists(otherIde)); // not ours to delete
        Assert.True(File.Exists(broken));
        Assert.True(File.Exists(rig.Bridge.LockPath));
    }

    [Fact]
    public async Task Every_bridge_has_its_own_port_token_and_lock_file_and_keeps_the_lock_file_of_the_others()
    {
        await using var rig = Rig.Create();
        var second = rig.AddBridge();
        Assert.NotEqual(rig.Bridge.Port, second.Port);
        Assert.NotEqual(rig.Bridge.AuthToken, second.AuthToken);
        Assert.Equal(2, Directory.GetFiles(rig.LockDirectory, "*.lock").Length); // starting the second did not delete the first, which belongs to the same process

        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory, port: second.Port);
        await agent.InitializeAsync();
        Assert.Equal(1, second.ConnectionCount);
        Assert.Equal(0, rig.Bridge.ConnectionCount);
        await Assert.ThrowsAsync<WebSocketException>(() => FakeAgent.ConnectAsync(rig.LockDirectory, token: rig.Bridge.AuthToken, port: second.Port)); // the token of one is no key to the other

        await second.DisposeAsync();
        Assert.False(File.Exists(second.LockPath));
        Assert.True(File.Exists(rig.Bridge.LockPath));
    }

    [Fact]
    public async Task A_bridge_serves_only_the_documents_of_its_own_window()
    {
        var windowA = new HashSet<Guid>();
        var windowB = new HashSet<Guid>();
        await using var rig = Rig.Create(o => o with { Scope = windowA.Contains });
        var otherWindow = rig.AddBridge(o => o with { Scope = windowB.Contains });
        var a = rig.Share(rig.FileDocument("alpha\n", "a.txt"));
        var b = rig.Share(rig.FileDocument("bravo\n", "b.txt"));
        windowA.Add(a.Id);
        windowB.Add(b.Id);
        rig.Owner.Run(() =>
        {
            rig.Api.PublishOwn(a, "lint", a.Revision, [new Diagnostic(DiagnosticSeverity.Error, "in a", "E1", new ByteRange(0, 5))]);
            rig.Api.PublishOwn(b, "lint", b.Revision, [new Diagnostic(DiagnosticSeverity.Error, "in b", "E2", new ByteRange(0, 5))]);
            return 0;
        });

        await using var agentA = await FakeAgent.ConnectAsync(rig.LockDirectory, port: rig.Bridge.Port);
        await agentA.InitializeAsync();
        Assert.Equal(a.Id.ToString(), Assert.Single((await agentA.ToolAsync("editor_list_documents")).GetProperty("documents").EnumerateArray()).GetProperty("id").GetString());
        var read = await agentA.ToolTextAsync("editor_read_range", $"{{\"document\":\"{b.Id}\",\"start\":0,\"end\":3}}");
        Assert.True(read.IsError); // shared in the other window: as good as not shared here
        var diagnostics = await agentA.ToolAsync("getDiagnostics");
        Assert.Contains("in a", diagnostics.GetRawText());
        Assert.DoesNotContain("in b", diagnostics.GetRawText());
        Assert.False(await rig.Bridge.PublishSelectionAsync(b.Id, 0, 3));
        Assert.True(await rig.Bridge.PublishSelectionAsync(a.Id, 0, 3));

        await using var agentB = await FakeAgent.ConnectAsync(rig.LockDirectory, port: otherWindow.Port);
        await agentB.InitializeAsync();
        Assert.Equal(b.Id.ToString(), Assert.Single((await agentB.ToolAsync("editor_list_documents")).GetProperty("documents").EnumerateArray()).GetProperty("id").GetString());
        Assert.DoesNotContain("in a", (await agentB.ToolAsync("getDiagnostics")).GetRawText());
    }

    [Fact]
    public async Task The_status_names_the_connected_agent_and_disconnecting_cuts_it_off_but_leaves_the_bridge_open()
    {
        await using var rig = Rig.Create();
        int changes = 0;
        rig.Bridge.AgentsChanged += () => Interlocked.Increment(ref changes);
        Assert.Empty(rig.Bridge.Agents);

        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync("fake-agent", "2.5");
        await T.Until(() => rig.Bridge.Agents.Count == 1);
        Assert.Equal(new IdeAgent("fake-agent", "2.5"), rig.Bridge.Agents[0]);
        Assert.True(Volatile.Read(ref changes) >= 2); // it connected and it said who it is

        Assert.Equal(1, rig.Bridge.DisconnectAgents());
        await T.Until(() => rig.Bridge.Agents.Count == 0);
        await Assert.ThrowsAnyAsync<Exception>(() => agent.RequestAsync("ping", "{}"));

        await using var again = await FakeAgent.ConnectAsync(rig.LockDirectory); // the bridge is still open for the next /ide
        await again.InitializeAsync();
        Assert.Single(rig.Bridge.Agents);
    }

    [Fact]
    public async Task The_name_of_an_agent_is_cut_to_the_limit()
    {
        await using var rig = Rig.Create(o => o with { MaxAgentNameChars = 4 });
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync("fake-agent", "1234567");
        Assert.Equal(new IdeAgent("fake", "1234"), Assert.Single(rig.Bridge.Agents));
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
        await rig.Bridge.DisposeAsync(); // the bridge alone: disposing the rig would delete the whole folder and hide a lock file left behind
        Assert.False(File.Exists(path));
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task A_new_bridge_after_a_restart_has_a_new_token()
    {
        await using var first = Rig.Create();
        await using var second = Rig.Create();
        Assert.NotEqual(first.Bridge.AuthToken, second.Bridge.AuthToken);
    }

    // ---- the handshake on the wire ----

    /// <summary>Sends one request head to the bridge as it is and returns the status line of the answer; null when the bridge closes the connection without one.</summary>
    static async Task<string?> RawUpgradeAsync(int port, IEnumerable<string> head)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(string.Join("\r\n", head) + "\r\n\r\n"), timeout.Token);
        var buffer = new byte[256];
        int read;
        try { read = await stream.ReadAsync(buffer, timeout.Token); }
        catch (IOException) { return null; }
        return read == 0 ? null : Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
    }

    static List<string> UpgradeHead(int port, string? token, string host = "", string method = "GET", string? origin = null, bool upgrade = true)
    {
        var head = new List<string> { $"{method} / HTTP/1.1" };
        if (host != "-") head.Add("Host: " + (host.Length > 0 ? host : $"127.0.0.1:{port}"));
        if (upgrade) head.AddRange(["Upgrade: websocket", "Connection: Upgrade"]);
        head.AddRange(["Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==", "Sec-WebSocket-Version: 13"]);
        if (token is not null) head.Add($"{IdeBridge.AuthorizationHeader}: {token}");
        if (origin is not null) head.Add("Origin: " + origin);
        return head;
    }

    [Fact]
    public async Task Only_a_request_with_the_exact_token_for_a_loopback_host_and_without_an_origin_is_upgraded()
    {
        await using var rig = Rig.Create();
        int port = rig.Bridge.Port;
        string token = rig.Bridge.AuthToken;
        var cases = new (string Label, List<string> Head, string Status)[]
        {
            ("the right token", UpgradeHead(port, token), "HTTP/1.1 101"),
            ("localhost as the host", UpgradeHead(port, token, host: $"localhost:{port}"), "HTTP/1.1 101"),
            ("no token", UpgradeHead(port, null), "HTTP/1.1 401"),
            ("an empty token", UpgradeHead(port, ""), "HTTP/1.1 401"),
            ("another token", UpgradeHead(port, new string('0', token.Length)), "HTTP/1.1 401"),
            ("the token with a character more", UpgradeHead(port, token + "0"), "HTTP/1.1 401"),
            ("the token without its last character", UpgradeHead(port, token[..^1]), "HTTP/1.1 401"),
            ("the token with a space before it", UpgradeHead(port, "x" + token), "HTTP/1.1 401"),
            ("a host that is not loopback", UpgradeHead(port, token, host: $"evil.example:{port}"), "HTTP/1.1 403"),
            ("a LAN address as the host", UpgradeHead(port, token, host: $"192.0.2.10:{port}"), "HTTP/1.1 403"),
            ("the loopback host with another port", UpgradeHead(port, token, host: $"127.0.0.1:{port + 1}"), "HTTP/1.1 403"),
            ("no host", UpgradeHead(port, token, host: "-"), "HTTP/1.1 403"),
            ("an origin, as a web page sends", UpgradeHead(port, token, origin: "http://127.0.0.1"), "HTTP/1.1 403"),
            ("a method other than GET", UpgradeHead(port, token, method: "POST"), "HTTP/1.1 400"),
            ("no upgrade", UpgradeHead(port, token, upgrade: false), "HTTP/1.1 400"),
        };
        foreach (var (label, head, expected) in cases)
        {
            var status = await RawUpgradeAsync(port, head);
            Assert.True(status is not null && status.StartsWith(expected, StringComparison.Ordinal), $"{label}: expected {expected}, got {status ?? "no answer"}");
        }
    }

    [Fact]
    public async Task A_request_head_over_the_limit_or_a_handshake_that_never_comes_is_dropped_and_the_bridge_goes_on()
    {
        await using var rig = Rig.Create(o => o with { HandshakeTimeout = TimeSpan.FromMilliseconds(300), MaxHeaderBytes = 2048 });
        var big = UpgradeHead(rig.Bridge.Port, rig.Bridge.AuthToken);
        big.Add("X-Padding: " + new string('a', 4096));
        Assert.Null(await RawUpgradeAsync(rig.Bridge.Port, big));

        using (var silent = new TcpClient())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await silent.ConnectAsync(IPAddress.Loopback, rig.Bridge.Port, timeout.Token);
            var read = 0;
            try { read = await silent.GetStream().ReadAsync(new byte[16], timeout.Token); }
            catch (IOException) { }
            Assert.Equal(0, read); // the bridge hung up on the connection that sent nothing
        }
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
    }

    [Fact]
    public async Task The_bridge_cannot_be_reached_by_the_addresses_of_the_machine_other_than_loopback()
    {
        await using var rig = Rig.Create();
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(a => a.Address))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .Take(3).ToList();
        foreach (var address in addresses) // a machine with no other address has nothing to try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<Exception>(async () => await client.ConnectAsync(address, rig.Bridge.Port, timeout.Token));
        }
    }

    // ---- lock files and the process table ----

    /// <summary>A process that lives until its input is closed.</summary>
    static Process StartWaiter()
    {
        var start = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", "/c \"set /p x=\"") : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "read x" } };
        start.RedirectStandardInput = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        return Process.Start(start)!;
    }

    static string WriteLock(string directory, string name, int pid, string ideName)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, $"{{\"pid\":{pid},\"ideName\":\"{ideName}\",\"authToken\":\"left-behind\"}}");
        return path;
    }

    [Fact]
    public async Task A_lock_file_is_stale_only_when_the_process_that_wrote_it_is_gone_from_the_machine()
    {
        using var running = StartWaiter();
        using var finished = StartWaiter();
        finished.StandardInput.Close();
        Assert.True(finished.WaitForExit(20_000));
        string dead = "", alive = "", otherIde = "", noPid = "";
        await using var rig = Rig.Create(seed: dir => // no fake of the process table: the bridge asks the operating system
        {
            dead = WriteLock(dir, "10001.lock", finished.Id, IdeBridge.IdeName);
            alive = WriteLock(dir, "10002.lock", running.Id, IdeBridge.IdeName);
            otherIde = WriteLock(dir, "10003.lock", finished.Id, "Another IDE");
            noPid = Path.Combine(dir, "10004.lock");
            File.WriteAllText(noPid, "{\"ideName\":\"Textida\"}");
        });
        try
        {
            Assert.False(File.Exists(dead));
            Assert.True(File.Exists(alive));
            Assert.True(File.Exists(otherIde));
            Assert.True(File.Exists(noPid));
        }
        finally
        {
            running.StandardInput.Close();
            running.WaitForExit(20_000);
        }
    }

    [Fact]
    public async Task The_lock_file_goes_when_the_process_exits_without_closing_the_bridge()
    {
        var rig = Rig.Create();
        var path = rig.Bridge.LockPath;
        Assert.True(File.Exists(path));
        // the process-exit handler cannot be raised from a test, so the method it is registered with is called
        typeof(IdeBridge).GetMethod("OnProcessExit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(rig.Bridge, [null, EventArgs.Empty]);
        Assert.False(File.Exists(path));
        await rig.DisposeAsync();
    }

    // ---- selection ----

    [Fact]
    public async Task The_agent_hears_about_the_selection_of_a_shared_tab_and_of_nothing_else()
    {
        await using var rig = Rig.Create();
        var shared = rig.Share(rig.FileDocument("alpha beta gamma\n"));
        var hidden = rig.FileDocument("secret text\n", "hidden.txt");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; }); // open in the editor, but never shared with the assistant
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        Assert.False(await rig.Bridge.PublishSelectionAsync(hidden.Id, 0, 6));
        Assert.True(await rig.Bridge.PublishSelectionAsync(shared.Id, 6, 10));
        var update = await agent.NextNotificationAsync();
        Assert.Equal("selection_changed", IdeBridge.SelectionMethod);
        Assert.Equal(IdeBridge.SelectionMethod, update.GetProperty("method").GetString());
        var p = update.GetProperty("params");
        Assert.Equal("beta", p.GetProperty("text").GetString());
        Assert.Equal(shared.FilePath, p.GetProperty("filePath").GetString());
        Assert.Equal(new Uri(shared.FilePath!).AbsoluteUri, p.GetProperty("fileUrl").GetString());
        var selection = p.GetProperty("selection");
        Assert.Equal(0, selection.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(6, selection.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(0, selection.GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(10, selection.GetProperty("end").GetProperty("character").GetInt32());
        Assert.False(selection.GetProperty("isEmpty").GetBoolean());

        // a cursor without a selection is the open file as far as the agent is concerned
        Assert.True(await rig.Bridge.PublishSelectionAsync(shared.Id, 3, 3));
        var cursor = (await agent.NextNotificationAsync()).GetProperty("params");
        Assert.Equal("", cursor.GetProperty("text").GetString());
        Assert.True(cursor.GetProperty("selection").GetProperty("isEmpty").GetBoolean());

        // the user withdraws the tab: no more updates
        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(shared.Id));
        Assert.False(await rig.Bridge.PublishSelectionAsync(shared.Id, 0, 5));
    }

    [Fact]
    public async Task A_tab_without_a_file_is_not_announced()
    {
        await using var rig = Rig.Create();
        var untitled = rig.Share(NewDocument("alpha\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        Assert.False(await rig.Bridge.PublishSelectionAsync(untitled.Id, 0, 3)); // the notification has no way to say where the text lives
        Assert.False((await agent.ToolAsync("getLatestSelection")).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Hidden_values_of_an_env_file_are_not_in_the_selection()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.FileDocument("API_KEY=hunter2\n", ".env"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        Assert.True(await rig.Bridge.PublishSelectionAsync(doc.Id, 0, 15));
        var text = (await agent.NextNotificationAsync()).GetProperty("params").GetProperty("text").GetString()!;
        Assert.DoesNotContain("hunter2", text);
        Assert.Contains("API_KEY=", text);
    }

    [Fact]
    public async Task Hidden_values_of_an_env_file_stay_hidden_in_every_answer_the_agent_can_ask_for()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.FileDocument("API_KEY=hunter2\n", ".env"), () => SelectionSet.Single(new TextSelection(0, 15)));
        rig.Owner.Run(() => { rig.Api.PublishOwn(doc, "lint", doc.Revision, [new Diagnostic(DiagnosticSeverity.Warning, "key is set", "W1", new ByteRange(8, 15))]); return 0; });
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        Assert.True(await rig.Bridge.PublishSelectionAsync(doc.Id, 0, 15));
        var answers = new[]
        {
            (await agent.NextNotificationAsync()).GetRawText(),
            (await agent.ToolAsync("getCurrentSelection")).GetRawText(),
            (await agent.ToolAsync("getLatestSelection")).GetRawText(),
            (await agent.ToolAsync("getDiagnostics")).GetRawText(),
            (await agent.ToolTextAsync("editor_read_range", $"{{\"document\":\"{doc.Id}\"}}")).Text,
        };
        foreach (var answer in answers) Assert.DoesNotContain("hunter2", answer);
        Assert.Contains("API_KEY=", answers[1]);
    }

    [Fact]
    public async Task The_agent_asks_for_the_current_and_the_latest_selection_and_loses_them_with_the_focus_and_the_share()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.FileDocument("alpha beta gamma\n"), () => SelectionSet.Single(new TextSelection(6, 10)));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        var none = await agent.ToolAsync("getCurrentSelection"); // no shared tab has been selected yet
        Assert.False(none.GetProperty("success").GetBoolean());
        Assert.Equal("No active editor found", none.GetProperty("message").GetString());
        Assert.Equal("No selection available", (await agent.ToolAsync("getLatestSelection")).GetProperty("message").GetString());

        Assert.True(await rig.Bridge.PublishSelectionAsync(doc.Id, 6, 10));
        var current = await agent.ToolAsync("getCurrentSelection");
        Assert.True(current.GetProperty("success").GetBoolean());
        Assert.Equal("beta", current.GetProperty("text").GetString());
        Assert.Equal(doc.FilePath, current.GetProperty("filePath").GetString());
        Assert.Equal(6, current.GetProperty("selection").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(10, current.GetProperty("selection").GetProperty("end").GetProperty("character").GetInt32());

        rig.Bridge.ClearActive(); // the user moved to a tab that is not shared
        Assert.False((await agent.ToolAsync("getCurrentSelection")).GetProperty("success").GetBoolean());
        Assert.Equal("beta", (await agent.ToolAsync("getLatestSelection")).GetProperty("text").GetString()); // the last shared one is still the latest

        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
        Assert.False((await agent.ToolAsync("getLatestSelection")).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task An_agent_that_connects_later_is_told_where_the_selection_is()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.FileDocument("alpha beta gamma\n"), () => SelectionSet.Single(new TextSelection(6, 10)));
        Assert.False(await rig.Bridge.PublishSelectionAsync(doc.Id, 6, 10)); // nobody to tell yet; the bridge remembers the tab
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var pushed = await agent.NextNotificationAsync();
        Assert.Equal(IdeBridge.SelectionMethod, pushed.GetProperty("method").GetString());
        Assert.Equal("beta", pushed.GetProperty("params").GetProperty("text").GetString());
    }

    // ---- editors, folders, openFile, at_mentioned ----

    static IdeEditor Editor(Document doc, string label, bool dirty = false, string language = "plaintext") => new(doc.Id, doc.FilePath, label, language, dirty);

    [Fact]
    public async Task The_agent_asks_for_the_workspace_folders_of_the_window()
    {
        var first = Path.Combine(Path.GetTempPath(), "project-a");
        var second = Path.Combine(Path.GetTempPath(), "project-b");
        await using var rig = Rig.Create(o => o with { WorkspaceFolders = [first, second] });
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        var answer = await agent.ToolAsync("getWorkspaceFolders");
        Assert.True(answer.GetProperty("success").GetBoolean());
        var folders = answer.GetProperty("folders").EnumerateArray().ToList();
        Assert.Equal(2, folders.Count);
        Assert.Equal("project-a", folders[0].GetProperty("name").GetString());
        Assert.Equal(first, folders[0].GetProperty("path").GetString());
        Assert.Equal(new Uri(first).AbsoluteUri, folders[0].GetProperty("uri").GetString());
        Assert.Equal(first, answer.GetProperty("rootPath").GetString());

        Assert.True(rig.Bridge.UpdateWorkspaceFolders([]));
        var none = await agent.ToolAsync("getWorkspaceFolders");
        Assert.Equal(0, none.GetProperty("folders").GetArrayLength());
        Assert.False(none.TryGetProperty("rootPath", out _));
    }

    [Fact]
    public async Task The_open_editors_are_the_shared_tabs_in_the_order_of_the_editor_and_the_active_one_is_marked()
    {
        IReadOnlyList<IdeEditor> editors = [];
        await using var rig = Rig.Create(o => o with { Editors = _ => Task.FromResult(editors) });
        var a = rig.Share(rig.FileDocument("one\n", "a.txt"));
        var b = rig.Share(rig.FileDocument("two\n", "b.md"));
        var untitled = rig.Share(NewDocument("scratch"));
        var hidden = rig.FileDocument("secret\n", "hidden.txt");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; }); // open in the editor, but never shared with the assistant
        editors = [Editor(a, "a.txt"), Editor(hidden, "hidden.txt"), Editor(b, "b.md", dirty: true, language: "markdown"), new IdeEditor(untitled.Id, null, "new 1", "plaintext", true)];
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        Assert.True(await rig.Bridge.PublishSelectionAsync(b.Id, 0, 1));

        var tabs = (await agent.ToolAsync("getOpenEditors")).GetProperty("tabs").EnumerateArray().ToList();
        Assert.Equal(3, tabs.Count);
        Assert.Equal(new Uri(a.FilePath!).AbsoluteUri, tabs[0].GetProperty("uri").GetString());
        Assert.Equal("a.txt", tabs[0].GetProperty("label").GetString());
        Assert.Equal("plaintext", tabs[0].GetProperty("languageId").GetString());
        Assert.False(tabs[0].GetProperty("isActive").GetBoolean());
        Assert.False(tabs[0].GetProperty("isDirty").GetBoolean());
        Assert.Equal(new Uri(b.FilePath!).AbsoluteUri, tabs[1].GetProperty("uri").GetString());
        Assert.True(tabs[1].GetProperty("isActive").GetBoolean());
        Assert.True(tabs[1].GetProperty("isDirty").GetBoolean());
        Assert.Equal("markdown", tabs[1].GetProperty("languageId").GetString());
        Assert.StartsWith("untitled:", tabs[2].GetProperty("uri").GetString());
        Assert.DoesNotContain("hidden.txt", (await agent.ToolAsync("getOpenEditors")).GetRawText());

        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(a.Id)); // withdrawn: gone from the list at once
        Assert.Equal(2, (await agent.ToolAsync("getOpenEditors")).GetProperty("tabs").GetArrayLength());
    }

    [Fact]
    public async Task The_tools_that_need_the_editor_say_so_when_it_gives_them_nothing()
    {
        await using var rig = Rig.Create();
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var arguments = JsonSerializer.Serialize(new { filePath = Path.Combine(rig.Root, "a.txt") });
        foreach (var tool in new[] { "getOpenEditors", "checkDocumentDirty", "openFile" })
        {
            var (isError, text) = await agent.ToolTextAsync(tool, arguments);
            Assert.True(isError, tool);
            Assert.NotEmpty(text);
        }
    }

    [Fact]
    public async Task Unsaved_changes_are_told_for_a_shared_tab_and_every_other_file_is_not_open()
    {
        IReadOnlyList<IdeEditor> editors = [];
        await using var rig = Rig.Create(o => o with { Editors = _ => Task.FromResult(editors) });
        var dirty = rig.Share(rig.FileDocument("one\n", "dirty.txt"));
        var clean = rig.Share(rig.FileDocument("two\n", "clean.txt"));
        var hidden = rig.FileDocument("secret\n", "hidden.txt");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; });
        editors = [Editor(dirty, "dirty.txt", dirty: true), Editor(clean, "clean.txt"), Editor(hidden, "hidden.txt", dirty: true)];
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        var yes = await agent.ToolAsync("checkDocumentDirty", JsonSerializer.Serialize(new { filePath = dirty.FilePath }));
        Assert.True(yes.GetProperty("success").GetBoolean());
        Assert.Equal(dirty.FilePath, yes.GetProperty("filePath").GetString());
        Assert.True(yes.GetProperty("isDirty").GetBoolean());
        Assert.False(yes.GetProperty("isUntitled").GetBoolean());
        var no = await agent.ToolAsync("checkDocumentDirty", JsonSerializer.Serialize(new { filePath = clean.FilePath }));
        Assert.False(no.GetProperty("isDirty").GetBoolean());

        foreach (var path in new[] { hidden.FilePath!, Path.Combine(rig.Root, "missing.txt"), "clean.txt" }) // not shared, not there, not an absolute path
        {
            var gone = await agent.ToolAsync("checkDocumentDirty", JsonSerializer.Serialize(new { filePath = path }));
            Assert.False(gone.GetProperty("success").GetBoolean());
            Assert.Equal("Document not open: " + path, gone.GetProperty("message").GetString());
        }
    }

    sealed class OpenRig : IAsyncDisposable
    {
        public readonly List<IdeOpenFileRequest> Opened = [];
        public IReadOnlyList<IdeEditor> Editors = [];
        public Exception? Refuse;
        public required Rig Rig { get; init; }

        public static OpenRig Create(string? workspace = null)
        {
            OpenRig? self = null;
            var rig = Rig.Create(o => o with
            {
                Editors = _ => Task.FromResult(self!.Editors),
                OpenFile = (request, _) =>
                {
                    if (self!.Refuse is { } refuse) throw refuse;
                    lock (self.Opened) self.Opened.Add(request);
                    return Task.FromResult(new IdeOpenedFile(request.Path, "plaintext", 3));
                },
            });
            self = new OpenRig { Rig = rig };
            rig.Bridge.UpdateWorkspaceFolders([workspace ?? rig.Root]);
            return self;
        }

        public int Count
        {
            get { lock (Opened) return Opened.Count; }
        }

        public ValueTask DisposeAsync() => Rig.DisposeAsync();
    }

    [Fact]
    public async Task An_agent_opens_a_file_of_the_workspace_with_the_options_it_gave()
    {
        await using var open = OpenRig.Create();
        await using var agent = await FakeAgent.ConnectAsync(open.Rig.LockDirectory);
        await agent.InitializeAsync();
        var inside = Path.Combine(open.Rig.Root, "src", "inside.txt"); // the editor decides whether the file is there

        var (isError, text) = await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = inside }));
        Assert.False(isError, text);
        Assert.Equal("Opened file: " + inside, text);
        var plain = Assert.Single(open.Opened);
        Assert.Equal(inside, plain.Path);
        Assert.True(plain.MakeFrontmost);
        Assert.False(plain.Preview);
        Assert.False(plain.SelectToEndOfLine);
        Assert.Null(plain.StartText);
        Assert.Null(plain.EndText);

        var dotted = Path.Combine(open.Rig.Root, "src", "..", "src", "inside.txt"); // the editor is given the path in its plain form
        await agent.ToolAsync("openFile", JsonSerializer.Serialize(new { filePath = dotted, preview = true, startText = "function hello", endText = "}", selectToEndOfLine = true, makeFrontmost = true }));
        var full = open.Opened[1];
        Assert.Equal(inside, full.Path);
        Assert.True(full.Preview);
        Assert.True(full.SelectToEndOfLine);
        Assert.Equal("function hello", full.StartText);
        Assert.Equal("}", full.EndText);
    }

    [Fact]
    public async Task A_file_outside_the_workspace_a_relative_path_and_a_network_path_are_not_opened()
    {
        await using var open = OpenRig.Create();
        await using var agent = await FakeAgent.ConnectAsync(open.Rig.LockDirectory);
        await agent.InitializeAsync();
        var network = OperatingSystem.IsWindows() ? @"\\server\share\a.txt" : "//server/share/a.txt";
        var paths = new[]
        {
            Path.Combine(open.Rig.Root, "..", "other.txt"), // leaves the folder by way of ..
            open.Rig.Root + "-other" + Path.DirectorySeparatorChar + "a.txt", // a sibling whose name starts like the folder
            Path.Combine(Path.GetTempPath(), "elsewhere.txt"),
            "a.txt",
            "../a.txt",
            network,
        };
        foreach (var path in paths)
        {
            var (isError, text) = await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = path }));
            Assert.True(isError, path);
            Assert.NotEmpty(text);
        }
        Assert.Equal(0, open.Count);
    }

    [Fact]
    public async Task A_shared_tab_can_be_opened_from_outside_the_workspace_and_only_a_shared_one_is_described()
    {
        await using var open = OpenRig.Create(workspace: Path.Combine(Path.GetTempPath(), "some-other-project"));
        var shared = open.Rig.Share(open.Rig.FileDocument("one\ntwo\nthree\n", "shared.txt"));
        var unshared = open.Rig.FileDocument("secret\n", "unshared.txt");
        open.Editors = [Editor(shared, "shared.txt")];
        await using var agent = await FakeAgent.ConnectAsync(open.Rig.LockDirectory);
        await agent.InitializeAsync();

        var brought = await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = shared.FilePath }));
        Assert.False(brought.IsError);
        Assert.Equal("Opened file: " + shared.FilePath, brought.Text);

        var described = await agent.ToolAsync("openFile", JsonSerializer.Serialize(new { filePath = shared.FilePath, makeFrontmost = false }));
        Assert.True(described.GetProperty("success").GetBoolean());
        Assert.Equal(shared.FilePath, described.GetProperty("filePath").GetString());
        Assert.Equal("plaintext", described.GetProperty("languageId").GetString());
        Assert.Equal(3, described.GetProperty("lineCount").GetInt32());
        Assert.False(open.Opened[1].MakeFrontmost);

        // a file the user has not shared is not opened (outside the workspace), and not described in the workspace either
        Assert.True((await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = unshared.FilePath }))).IsError);
        open.Rig.Bridge.UpdateWorkspaceFolders([open.Rig.Root]);
        var inWorkspace = await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = unshared.FilePath, makeFrontmost = false }));
        Assert.True(inWorkspace.IsError);
        Assert.DoesNotContain("lineCount", inWorkspace.Text);
        Assert.Equal(2, open.Count);
        Assert.False((await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = unshared.FilePath }))).IsError); // as the front tab it is the user's to see
    }

    [Fact]
    public async Task A_file_the_editor_cannot_open_is_an_error_with_the_reason()
    {
        await using var open = OpenRig.Create();
        await using var agent = await FakeAgent.ConnectAsync(open.Rig.LockDirectory);
        await agent.InitializeAsync();
        open.Refuse = new IdeOpenRefusedException("The file is gone.");
        var (isError, text) = await agent.ToolTextAsync("openFile", JsonSerializer.Serialize(new { filePath = Path.Combine(open.Rig.Root, "gone.txt") }));
        Assert.True(isError);
        Assert.Equal("The file is gone.", text);
    }

    [Fact]
    public async Task OpenFile_called_with_arguments_it_cannot_read_is_an_incompatibility()
    {
        await using var open = OpenRig.Create();
        string? reason = null;
        open.Rig.Bridge.Incompatible += r => reason = r;
        await using var agent = await FakeAgent.ConnectAsync(open.Rig.LockDirectory);
        await agent.InitializeAsync();
        var reply = await agent.RequestAsync("tools/call", "{\"name\":\"openFile\",\"arguments\":{\"filePath\":\"/a.txt\",\"makeFrontmost\":\"yes\"}}");
        Assert.True(reply.TryGetProperty("error", out _));
        await T.Until(() => reason is not null);
        Assert.Contains("openFile", reason);
        Assert.Equal(0, open.Count);
    }

    [Fact]
    public async Task An_at_mention_gives_the_agent_a_path_and_the_lines_and_only_for_a_shared_tab()
    {
        await using var rig = Rig.Create();
        var doc = rig.Share(rig.FileDocument("alpha\nbeta\ngamma\ndelta\n"));
        var hidden = rig.FileDocument("secret\n", "hidden.txt");
        rig.Owner.Run(() => { rig.Api.Expose(hidden); return 0; });
        Assert.False(await rig.Bridge.PublishAtMentionAsync(doc.Id, 6, 9)); // nobody is connected
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        Assert.True(await rig.Bridge.PublishAtMentionAsync(doc.Id, 6, 9));
        var inside = await agent.NextNotificationAsync();
        Assert.Equal("at_mentioned", IdeBridge.AtMentionMethod);
        Assert.Equal(IdeBridge.AtMentionMethod, inside.GetProperty("method").GetString());
        var p = inside.GetProperty("params");
        Assert.Equal(doc.FilePath, p.GetProperty("filePath").GetString());
        Assert.Equal(1, p.GetProperty("lineStart").GetInt32()); // 0-based, like the lines of selection_changed
        Assert.Equal(1, p.GetProperty("lineEnd").GetInt32());
        Assert.DoesNotContain("beta", inside.GetRawText()); // the text is not part of it

        Assert.True(await rig.Bridge.PublishAtMentionAsync(doc.Id, 6, 17)); // "beta\ngamma\n": the range ends where the next line begins, which is not part of it
        var lines = (await agent.NextNotificationAsync()).GetProperty("params");
        Assert.Equal(1, lines.GetProperty("lineStart").GetInt32());
        Assert.Equal(2, lines.GetProperty("lineEnd").GetInt32());

        Assert.True(await rig.Bridge.PublishAtMentionAsync(doc.Id, 7, 7)); // no selection: the file
        var file = (await agent.NextNotificationAsync()).GetProperty("params");
        Assert.Equal(doc.FilePath, file.GetProperty("filePath").GetString());
        Assert.False(file.TryGetProperty("lineStart", out _));
        Assert.False(file.TryGetProperty("lineEnd", out _));

        Assert.False(await rig.Bridge.PublishAtMentionAsync(hidden.Id, 0, 3)); // not shared
        Assert.False(await rig.Bridge.PublishAtMentionAsync(NewDocument("x").Id, 0, 1)); // not even known
        rig.Permissions.Revoke(McpServer.ClientId, DocumentPermissions.Read, GrantScope.OfDocument(doc.Id));
        Assert.False(await rig.Bridge.PublishAtMentionAsync(doc.Id, 6, 9)); // withdrawn
    }

    // ---- diffs ----

    static string DiffArgs(string oldPath, string contents, string tab)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("old_file_path", oldPath);
            w.WriteString("new_file_path", oldPath);
            w.WriteString("new_file_contents", contents);
            w.WriteString("tab_name", tab);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact]
    public async Task A_diff_opens_in_the_review_waits_for_the_user_and_answers_FILE_SAVED_when_all_of_it_was_applied()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        var call = agent.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "alpha delta\n", "Rename beta"));
        await T.Until(() => diffs.Count == 1);
        Assert.False(call.IsCompleted); // the agent waits for the user
        var seen = diffs.Seen[0];
        Assert.Equal(doc.FilePath, seen.OldPath);
        Assert.Equal(doc.FilePath, seen.NewPath);
        Assert.Equal("alpha delta\n", seen.NewContents);
        Assert.Equal("Rename beta", seen.TabName);

        diffs.Decide(IdeDiffOutcome.Saved);
        Assert.Equal((false, "FILE_SAVED"), await call);
        Assert.Equal("alpha beta\n", rig.Owner.Run(() => doc.ReadText(new ByteRange(0, doc.ByteLength)))); // the bridge itself writes nothing; the editor applies what the user accepts
    }

    [Fact]
    public async Task A_rejected_diff_answers_DIFF_REJECTED()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var call = agent.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "alpha delta\n", "Rename beta"));
        await T.Until(() => diffs.Count == 1);
        diffs.Decide(IdeDiffOutcome.Rejected);
        Assert.Equal((false, "DIFF_REJECTED"), await call);
    }

    [Theory]
    [InlineData(IdeDiffOutcome.Partial, "only some of the changes")]
    [InlineData(IdeDiffOutcome.Stale, "changed in the editor")]
    [InlineData(IdeDiffOutcome.Failed, "could not apply")]
    public async Task A_diff_that_was_not_applied_as_a_whole_is_an_error_the_agent_can_read(IdeDiffOutcome outcome, string fragment)
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var call = agent.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "alpha delta\n", "Rename beta"));
        await T.Until(() => diffs.Count == 1);
        diffs.Decide(outcome);
        var (isError, text) = await call;
        Assert.True(isError);
        Assert.Contains(fragment, text);
    }

    [Fact]
    public async Task A_diff_the_editor_refuses_or_cannot_show_is_an_error()
    {
        await using var refusing = Rig.Create(o => o with { ShowDiff = (_, _) => throw new IdeDiffRefusedException("The file is not open in this window of the editor.") });
        await using var agent = await FakeAgent.ConnectAsync(refusing.LockDirectory);
        await agent.InitializeAsync();
        var (isError, text) = await agent.ToolTextAsync("openDiff", DiffArgs("/nowhere/a.txt", "x", "tab"));
        Assert.True(isError);
        Assert.Contains("not open in this window", text);
        var malformed = await agent.RequestAsync("tools/call", "{\"name\":\"openDiff\",\"arguments\":{\"old_file_path\":\"/nowhere/a.txt\"}}"); // no tab_name
        Assert.True(malformed.TryGetProperty("error", out _));

        await using var none = Rig.Create(); // an editor that has no way to show a diff
        await using var other = await FakeAgent.ConnectAsync(none.LockDirectory);
        await other.InitializeAsync();
        Assert.True((await other.ToolTextAsync("openDiff", DiffArgs("/nowhere/a.txt", "x", "tab"))).IsError);
    }

    [Fact]
    public async Task Closing_the_tab_ends_the_open_diff_and_the_agent_hears_it_was_not_applied()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var waiting = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await waiting.InitializeAsync();
        await using var closer = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await closer.InitializeAsync();

        var call = waiting.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "alpha delta\n", "Tab A"));
        await T.Until(() => diffs.Count == 1);
        Assert.Equal((false, "TAB_CLOSED"), await closer.ToolTextAsync("close_tab", "{\"tab_name\":\"Tab A\"}"));
        Assert.Equal((false, "DIFF_REJECTED"), await call);
        Assert.True(diffs.Tokens[0].IsCancellationRequested); // the review is told to close
        Assert.Equal((false, "TAB_CLOSED"), await closer.ToolTextAsync("close_tab", "{\"tab_name\":\"No such tab\"}"));
    }

    [Fact]
    public async Task Closing_all_diff_tabs_ends_every_open_diff()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var first = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await first.InitializeAsync();
        await using var second = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await second.InitializeAsync();
        await using var closer = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await closer.InitializeAsync();

        var callA = first.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "one\n", "Tab A"));
        var callB = second.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "two\n", "Tab B"));
        await T.Until(() => diffs.Count == 2);
        Assert.Equal((false, "CLOSED_2_DIFF_TABS"), await closer.ToolTextAsync("closeAllDiffTabs"));
        Assert.Equal((false, "DIFF_REJECTED"), await callA);
        Assert.Equal((false, "DIFF_REJECTED"), await callB);
        Assert.Equal((false, "CLOSED_0_DIFF_TABS"), await closer.ToolTextAsync("closeAllDiffTabs"));
    }

    [Fact]
    public async Task An_agent_that_goes_away_or_is_disconnected_takes_its_open_diff_with_it()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));

        var leaving = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await leaving.InitializeAsync();
        _ = leaving.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "one\n", "Tab A"));
        await T.Until(() => diffs.Count == 1);
        await leaving.DisposeAsync(); // the terminal was closed
        await T.Until(() => diffs.Tokens[0].IsCancellationRequested);

        await using var cut = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await cut.InitializeAsync();
        _ = cut.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "two\n", "Tab B"));
        await T.Until(() => diffs.Count == 2);
        rig.Bridge.DisconnectAgents(); // the user cut it off
        await T.Until(() => diffs.Tokens[1].IsCancellationRequested);
    }

    [Fact]
    public async Task Switching_the_bridge_off_ends_the_open_diff()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show });
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        _ = agent.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "one\n", "Tab A"));
        await T.Until(() => diffs.Count == 1);
        await rig.Bridge.DisposeAsync();
        Assert.True(diffs.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task Diffs_waiting_for_the_user_are_limited_and_a_diff_of_the_same_name_replaces_the_old_one()
    {
        var diffs = new FakeDiffs();
        await using var rig = Rig.Create(o => o with { ShowDiff = diffs.Show }, maxPending: 1);
        var doc = rig.Share(rig.FileDocument("alpha beta\n"));
        await using var first = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await first.InitializeAsync();
        await using var second = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await second.InitializeAsync();

        var callA = first.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "one\n", "Tab A"));
        await T.Until(() => diffs.Count == 1);
        var (isError, text) = await second.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "two\n", "Tab B"));
        Assert.True(isError);
        Assert.Contains("limit", text);
        Assert.Equal(1, diffs.Count); // the refused diff never reached the review

        var callA2 = second.ToolTextAsync("openDiff", DiffArgs(doc.FilePath!, "three\n", "Tab A")); // same name: the first one is closed, this one takes its place
        Assert.Equal((false, "DIFF_REJECTED"), await callA);
        await T.Until(() => diffs.Count == 2);
        Assert.False(callA2.IsCompleted);
        rig.Bridge.DisconnectAgents();
    }

    // ---- compatibility ----

    [Fact]
    public async Task A_tool_the_protocol_documents_but_the_bridge_leaves_out_is_an_error_and_a_tool_nobody_documents_is_an_incompatibility()
    {
        await using var rig = Rig.Create();
        var reasons = new List<string>();
        rig.Bridge.Incompatible += reason => { lock (reasons) reasons.Add(reason); };
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();

        foreach (var tool in new[] { "saveDocument", "executeCode" })
        {
            var (isError, text) = await agent.ToolTextAsync(tool);
            Assert.True(isError);
            Assert.Contains(tool, text);
        }
        Assert.True((await agent.RequestAsync("tools/call", "{\"name\":\"editor_read_range\",\"arguments\":{}}")).TryGetProperty("error", out _)); // bad arguments for a tool that is not the bridge's own
        lock (reasons) Assert.Empty(reasons);

        Assert.True((await agent.RequestAsync("tools/call", "{\"name\":\"someNewTool\",\"arguments\":{}}")).TryGetProperty("error", out _));
        await T.Until(() => { lock (reasons) return reasons.Count == 1; });
        Assert.Contains("someNewTool", reasons[0]);

        await agent.RequestAsync("tools/call", "{\"name\":\"anotherNewTool\",\"arguments\":{}}");
        await Task.Delay(100);
        lock (reasons) Assert.Single(reasons); // said once; the owner switches the bridge off at the first
    }

    [Fact]
    public async Task A_tool_of_the_bridge_called_with_arguments_it_cannot_read_is_an_incompatibility()
    {
        await using var rig = Rig.Create(o => o with { ShowDiff = new FakeDiffs().Show });
        string? reason = null;
        rig.Bridge.Incompatible += r => reason = r;
        await using var agent = await FakeAgent.ConnectAsync(rig.LockDirectory);
        await agent.InitializeAsync();
        var reply = await agent.RequestAsync("tools/call", "{\"name\":\"openDiff\",\"arguments\":{\"old_file_path\":\"/nowhere/a.txt\"}}"); // no tab_name, no contents
        Assert.True(reply.TryGetProperty("error", out _));
        await T.Until(() => reason is not null);
        Assert.Contains("openDiff", reason);
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

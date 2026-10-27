using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Wiki;
using NksHub.Textida.Rpc;
using Xunit;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P6.4.1: the wiki adapter over the plugin protocol, against a local HTTP listener. The listener implements the ASSUMED shape of <see cref="AssumedWikiHttp"/> (the real wiki API is not
/// documented anywhere in the repository): <c>GET pages?prefix=</c>, <c>GET pages/{slug}</c>, <c>PUT pages/{slug}</c> with <c>expectedRevision</c> checked in one step (409 otherwise), Bearer token.
/// The test side plays the editor: it answers <c>source/register</c>, <c>secret/*</c> (through the real <see cref="PluginSecrets"/> and its call log) and <c>ui/input</c>.
/// </summary>
public sealed class WikiPluginTests : IAsyncDisposable
{
    private const string Token = "TOP-SECRET-TOKEN-123";
    private const string PluginId = "textida.wiki";
    private static readonly RpcLimits Limits = new(4 * 1024 * 1024, 64, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024);

    private sealed class MemorySecrets : ISecretBackend
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Get(string pluginId, string name) => Values.GetValueOrDefault(name);
        public void Set(string pluginId, string name, string value) => Values[name] = value;
        public bool Delete(string pluginId, string name) => Values.Remove(name);
    }

    /// <summary>The editor side of the connection.</summary>
    private sealed class FakeEditor(PluginSecrets secrets, Func<string, string?> answer) : IRpcHandler
    {
        public readonly List<string> Registered = [];
        public readonly List<string> Asked = [];

        public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken ct)
        {
            if (method == DocumentMethods.SourceRegister)
            {
                Registered.Add(p.GetRawText());
                return "{\"enabled\":true}"u8.ToArray();
            }
            if (method.StartsWith("secret/", StringComparison.Ordinal)) return await secrets.HandleAsync(PluginId, method, p, ct);
            if (method == "ui/input")
            {
                var title = p.GetProperty("title").GetString()!;
                Asked.Add(title + (p.TryGetProperty("secret", out var s) && s.GetBoolean() ? " [hidden]" : ""));
                return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string?> { ["value"] = answer(title) });
            }
            throw new RpcException(RpcErrorCodes.MethodNotFound, method);
        }

        public ValueTask HandleNotificationAsync(string method, JsonElement p) => ValueTask.CompletedTask;
    }

    /// <summary>The wiki side: an HTTP listener with the assumed shape.</summary>
    private sealed class FakeWiki : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly object _gate = new();
        public readonly Dictionary<string, (string Text, int Revision)> Pages = [];
        public readonly List<string> Requests = [];
        public bool RejectToken;
        public string Url { get; }

        public FakeWiki()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/wiki-api";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext c;
                try { c = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                try { Handle(c); }
                finally { c.Response.Close(); }
            }
        }

        private static void Reply(HttpListenerContext c, int status, string json)
        {
            c.Response.StatusCode = status;
            var bytes = Encoding.UTF8.GetBytes(json);
            c.Response.ContentType = "application/json";
            c.Response.OutputStream.Write(bytes);
        }

        private void Handle(HttpListenerContext c)
        {
            lock (_gate)
            {
                var request = c.Request;
                Requests.Add(request.HttpMethod + " " + request.Url!.PathAndQuery);
                if (RejectToken || request.Headers["Authorization"] != "Bearer " + Token) { Reply(c, 401, "{}"); return; }
                var path = request.Url.AbsolutePath;
                const string root = "/wiki-api/pages";
                if (path == root && request.HttpMethod == "GET")
                {
                    var prefix = request.QueryString["prefix"] ?? "";
                    var list = Pages.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Select(k => $"{{\"slug\":\"{k}\"}}");
                    Reply(c, 200, "{\"pages\":[" + string.Join(",", list) + "]}");
                    return;
                }
                var slug = Uri.UnescapeDataString(path[(root.Length + 1)..]);
                bool exists = Pages.TryGetValue(slug, out var page);
                if (request.HttpMethod == "GET")
                {
                    if (!exists) Reply(c, 404, "{}");
                    else Reply(c, 200, JsonSerializer.Serialize(new Dictionary<string, object> { ["content"] = page.Text, ["revision"] = page.Revision.ToString() }));
                    return;
                }
                using var body = JsonDocument.Parse(new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd());
                var expected = body.RootElement.GetProperty("expectedRevision") is { ValueKind: JsonValueKind.String } e ? e.GetString() : null;
                var current = exists ? page.Revision.ToString() : null;
                if (expected != current)
                {
                    Reply(c, 409, JsonSerializer.Serialize(new Dictionary<string, string?> { ["currentRevision"] = current }));
                    return;
                }
                Pages[slug] = (body.RootElement.GetProperty("content").GetString()!, page.Revision + 1);
                Reply(c, 200, $"{{\"revision\":\"{Pages[slug].Revision}\"}}");
            }
        }

        public void Dispose() => _listener.Close();
    }

    private readonly FakeWiki _wiki = new();
    private readonly MemorySecrets _secrets = new();
    private readonly PluginCallLog _log = new(1000);
    private readonly FakeEditor _editor;
    private readonly JsonRpcDispatcher _rpc;
    private readonly Task _pluginRun;
    private readonly AnonymousPipeServerStream _toPlugin = new(PipeDirection.Out), _fromPlugin = new(PipeDirection.In);
    private string? _tokenAnswer = Token;

    public WikiPluginTests()
    {
        _secrets.Values[WikiPlugin.BaseUrlSecret] = _wiki.Url;
        _editor = new FakeEditor(new PluginSecrets(_secrets, _log), title => title.Contains("token", StringComparison.OrdinalIgnoreCase) ? _tokenAnswer : null);
        var pluginIn = new AnonymousPipeClientStream(PipeDirection.In, _toPlugin.ClientSafePipeHandle);
        var pluginOut = new AnonymousPipeClientStream(PipeDirection.Out, _fromPlugin.ClientSafePipeHandle);
        var plugin = new WikiPlugin(new WikiSettings(TimeSpan.FromSeconds(10), 1 << 20, 100, "en"));
        _pluginRun = PluginRunner.RunAsync(plugin, Limits, pluginIn, pluginOut);
        _rpc = new JsonRpcDispatcher(_fromPlugin, _toPlugin, RpcProtocols.Plugin, Limits, _editor);
    }

    public async ValueTask DisposeAsync()
    {
        await _rpc.DisposeAsync();
        _wiki.Dispose();
        _toPlugin.Dispose();
        _fromPlugin.Dispose();
    }

    private async Task<JsonElement> CallAsync(string method, object parameters) =>
        (await _rpc.RequestAsync(method, JsonSerializer.SerializeToUtf8Bytes(parameters), TimeSpan.FromSeconds(20))).Clone();

    private Task<JsonElement> ReadAsync(string uri) => CallAsync(DocumentRequests.SourceRead, new { uri });

    private Task<JsonElement> WriteAsync(string uri, string text, string? expected) =>
        CallAsync(DocumentRequests.SourceWrite, new { uri, expectedRevision = expected, bytes = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) });

    private const string Page = "nksis://wiki/docs/intro";

    [Fact]
    public async Task The_plugin_registers_its_scheme_as_listing_and_writing_but_not_atomic()
    {
        await CallAsync(PluginMethods.Resync, new { });

        var registered = JsonDocument.Parse(Assert.Single(_editor.Registered)).RootElement;
        Assert.Equal("nksis", registered.GetProperty("scheme").GetString());
        Assert.True(registered.GetProperty("list").GetBoolean());
        Assert.True(registered.GetProperty("write").GetBoolean());
        Assert.False(registered.GetProperty("atomicWrite").GetBoolean()); // the atomic check of the wiki is not verified (P6.4.4)
        Assert.False(WikiPlugin.AtomicWriteVerified);
    }

    [Fact]
    public async Task A_page_is_listed_opened_and_saved_with_the_revision_it_was_read_at()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/intro"] = ("one\n", 1);
        _wiki.Pages["docs/other page"] = ("x", 5);
        _wiki.Pages["misc/a"] = ("y", 1);

        var list = await CallAsync(DocumentRequests.SourceList, new { uri = "nksis://wiki/docs" });
        var uris = list.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("uri").GetString()).Order().ToArray();
        Assert.Equal(["nksis://wiki/docs/intro", "nksis://wiki/docs/other%20page"], uris);

        var read = await ReadAsync(Page);
        Assert.Equal("one\n", read.GetProperty("text").GetString());
        var revision = read.GetProperty("revision").GetString();
        Assert.Equal("1", revision);
        var stat = await CallAsync(DocumentRequests.SourceStat, new { uri = Page });
        Assert.Equal(revision, stat.GetProperty("revision").GetString());

        var saved = await WriteAsync(Page, "edited one\n", revision);

        Assert.Equal("2", saved.GetProperty("revision").GetString());
        Assert.Equal(("edited one\n", 2), _wiki.Pages["docs/intro"]);
        Assert.Equal("edited one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_save_against_a_changed_revision_is_a_conflict_with_the_current_revision_and_writes_nothing()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/intro"] = ("one\n", 1);
        var revision = (await ReadAsync(Page)).GetProperty("revision").GetString();
        _wiki.Pages["docs/intro"] = ("changed by a colleague\n", 2);

        var e = await Assert.ThrowsAsync<RpcException>(() => WriteAsync(Page, "mine\n", revision));

        Assert.Equal(DocumentErrorCodes.SourceConflict, e.Code);
        Assert.Equal("2", JsonDocument.Parse(e.DataJson!).RootElement.GetProperty("currentRevision").GetString());
        Assert.Equal(("changed by a colleague\n", 2), _wiki.Pages["docs/intro"]);

        // a page that is gone is a conflict with no current revision
        _wiki.Pages.Remove("docs/intro");
        var gone = await Assert.ThrowsAsync<RpcException>(() => WriteAsync(Page, "mine\n", "2"));
        Assert.Equal(DocumentErrorCodes.SourceConflict, gone.Code);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(gone.DataJson!).RootElement.GetProperty("currentRevision").ValueKind);
    }

    [Fact]
    public async Task Two_writes_against_the_same_revision_let_exactly_one_win_on_the_assumed_server()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/intro"] = ("one\n", 1);

        var results = await Task.WhenAll(new[] { "a\n", "b\n" }.Select(async text =>
        {
            try { await WriteAsync(Page, text, "1"); return 0; }
            catch (RpcException e) { return e.Code; }
        }));

        Assert.Equal(1, results.Count(r => r == 0));
        Assert.Equal(1, results.Count(r => r == DocumentErrorCodes.SourceConflict));
    }

    [Fact]
    public async Task Without_a_token_the_user_is_asked_and_when_nothing_is_entered_nothing_is_sent()
    {
        _wiki.Pages["docs/intro"] = ("one\n", 1);
        _tokenAnswer = null;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Contains("No access token", e.Message);
        Assert.Equal(["NKS IS wiki: access token [hidden]"], _editor.Asked); // the typing is hidden
        Assert.Empty(_wiki.Requests);
        Assert.False(_secrets.Values.ContainsKey(WikiPlugin.TokenSecret));

        // entered at the next attempt: stored in the secret storage once, not asked again
        _tokenAnswer = Token;
        Assert.Equal("one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
        Assert.Equal(Token, _secrets.Values[WikiPlugin.TokenSecret]);
        await ReadAsync(Page);
        Assert.Equal(2, _editor.Asked.Count);
    }

    [Fact]
    public async Task Without_a_base_url_the_user_is_asked_and_no_host_is_built_in()
    {
        _secrets.Values.Clear();
        _tokenAnswer = Token;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Contains("No wiki address", e.Message);
        Assert.Equal(["NKS IS wiki"], _editor.Asked);
        Assert.Empty(_wiki.Requests);
    }

    [Fact]
    public async Task A_token_the_wiki_refuses_is_dropped_so_the_next_attempt_asks_again()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/intro"] = ("one\n", 1);
        _wiki.RejectToken = true;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Contains("did not accept the token", e.Message);
        Assert.False(_secrets.Values.ContainsKey(WikiPlugin.TokenSecret));
        _wiki.RejectToken = false;
        Assert.Equal("one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
        Assert.Single(_editor.Asked);
    }

    [Fact]
    public async Task An_unreachable_wiki_is_reported_as_unavailable_so_the_editor_may_use_its_cache()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _secrets.Values[WikiPlugin.BaseUrlSecret] = "http://127.0.0.1:1/wiki-api";

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Equal(DocumentErrorCodes.SourceUnavailable, e.Code);
    }

    [Fact]
    public async Task The_token_is_in_no_log_no_address_and_no_message()
    {
        _tokenAnswer = Token;
        _wiki.Pages["docs/intro"] = ("one\n", 1);
        var texts = new List<string>();

        var list = await CallAsync(DocumentRequests.SourceList, new { uri = "nksis://wiki/" });
        texts.Add(list.GetRawText());
        texts.Add((await ReadAsync(Page)).GetRawText());
        texts.Add((await WriteAsync(Page, "two\n", "1")).GetRawText());
        _wiki.RejectToken = true;
        texts.Add((await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page))).Message);
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        texts.Add((await Assert.ThrowsAsync<RpcException>(() => ReadAsync("nksis://wiki/docs/x?token=" + Token))).Message);

        // the editor's call log holds no parameters by construction; the addresses the plugin returns and the messages it raised carry no token
        texts.Add(JsonSerializer.Serialize(_log.Entries().Select(l => l.Method + l.Outcome)));
        texts.Add(string.Join("\n", _wiki.Requests));
        foreach (var text in texts) Assert.DoesNotContain(Token, text);
        Assert.Contains(_log.Entries(), l => l.Method == "secret/set");
        Assert.Equal(JsonValueKind.String, list.GetProperty("entries")[0].GetProperty("uri").ValueKind);
    }
}

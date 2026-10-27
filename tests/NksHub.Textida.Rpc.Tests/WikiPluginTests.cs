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
/// P6.4.1: the wiki adapter over the plugin protocol, against a local HTTP listener that follows the OpenAPI of the service as <see cref="NksIsWikiHttp"/> maps it:
/// <c>{base}/api/v1/wiki</c> (paged list, read with <c>content</c>, <c>PUT</c> of <c>{content}</c> without any revision check), key in <c>X-Api-Key</c>, 401 for a bad key, 429 over the limit.
/// The test side plays the editor: it answers <c>source/register</c>, <c>secret/*</c> (through the real <see cref="PluginSecrets"/> and its call log), <c>net/request</c>
/// (through the real <see cref="PluginNetwork"/>, here without a proxy) and <c>ui/input</c>.
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
    private sealed class FakeEditor(PluginSecrets secrets, PluginNetwork network, Func<string, string?> answer) : IRpcHandler
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
            if (method.StartsWith("net/", StringComparison.Ordinal)) return await network.HandleAsync(PluginId, method, p, ct);
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
        public bool RejectToken, RateLimited;
        public int PageSize = 100;
        public string Url { get; }

        /// <summary>The revision token the adapter derives from <c>version</c>, <c>revisionCount</c> and <c>updatedAt</c> of revision number <paramref name="n"/> (below 10).</summary>
        public static string TokenOf(int n) => $"1.{n}|{n}|2026-01-0{n}T00:00:00Z";

        private string PageJson(string slug, bool content)
        {
            var (text, n) = Pages[slug];
            var fields = new Dictionary<string, object>
            {
                ["id"] = 1, ["title"] = "Page " + slug, ["slug"] = slug, ["syntaxType"] = "markdown", ["isFolder"] = slug.EndsWith("/folder", StringComparison.Ordinal),
                ["parentId"] = null!, ["version"] = $"1.{n}", ["revisionCount"] = n, ["updatedAt"] = $"2026-01-0{n}T00:00:00Z",
            };
            if (content) fields["content"] = text;
            return JsonSerializer.Serialize(fields);
        }

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
                if (RateLimited) { Reply(c, 429, "{}"); return; }
                if (RejectToken || request.Headers["X-Api-Key"] != Token || request.Headers["Authorization"] is not null) { Reply(c, 401, "{}"); return; }
                var path = request.Url.AbsolutePath;
                const string root = "/wiki-api/api/v1/wiki";
                if (path == root && request.HttpMethod == "GET")
                {
                    // the search is a text search; the adapter filters the slugs by prefix itself
                    int number = int.Parse(request.QueryString["page"]!), limit = int.Parse(request.QueryString["limit"]!);
                    var all = Pages.Keys.Order().ToArray();
                    var items = all.Skip((number - 1) * PageSize).Take(Math.Min(limit, PageSize)).Select(k => PageJson(k, false));
                    Reply(c, 200, $"{{\"items\":[{string.Join(",", items)}],\"total\":{all.Length},\"page\":{number},\"limit\":{limit},\"pages\":{Math.Max(1, (all.Length + PageSize - 1) / PageSize)}}}");
                    return;
                }
                var slug = Uri.UnescapeDataString(path[(root.Length + 1)..]);
                bool exists = Pages.TryGetValue(slug, out var page);
                if (!exists) { Reply(c, 404, "{}"); return; }
                if (request.HttpMethod == "GET") { Reply(c, 200, PageJson(slug, true)); return; }
                // the update takes the fields to change and checks no revision
                using var body = JsonDocument.Parse(new StreamReader(request.InputStream, Encoding.UTF8).ReadToEnd());
                Pages[slug] = (body.RootElement.GetProperty("content").GetString()!, page.Revision + 1);
                Reply(c, 200, PageJson(slug, true));
            }
        }

        public void Dispose() => _listener.Close();
    }

    private readonly FakeWiki _wiki = new();
    private readonly MemorySecrets _secrets = new();
    private readonly PluginCallLog _log = new(1000);
    private readonly PluginPermissions _permissions = new();
    private readonly FakeEditor _editor;
    private readonly JsonRpcDispatcher _rpc;
    private readonly Task _pluginRun;
    private readonly AnonymousPipeServerStream _toPlugin = new(PipeDirection.Out), _fromPlugin = new(PipeDirection.In);
    private string? _tokenAnswer = Token;

    public WikiPluginTests()
    {
        _secrets.Values[WikiPlugin.BaseUrlSecret] = _wiki.Url;
        _permissions.Grant(PluginId, PluginPermissionNames.Network, GrantScope.All);
        var network = new PluginNetwork(_permissions, () => new HttpClientHandler { UseProxy = false }, new PluginNetworkOptions(TimeSpan.FromSeconds(10), 1 << 20), _log);
        _editor = new FakeEditor(new PluginSecrets(_secrets, _log), network, title => title.Contains("API key", StringComparison.Ordinal) ? _tokenAnswer : null);
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

    private const string Page = "nksis://wiki/docs/page-a";

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
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        _wiki.Pages["docs/page-b"] = ("x", 5);
        _wiki.Pages["docs/folder"] = ("", 1);
        _wiki.Pages["misc/page-c"] = ("y", 1);
        _wiki.PageSize = 2; // the list is read page by page

        var list = await CallAsync(DocumentRequests.SourceList, new { uri = "nksis://wiki/docs" });
        var entries = list.GetProperty("entries").EnumerateArray().OrderBy(e => e.GetProperty("uri").GetString(), StringComparer.Ordinal).ToArray();
        Assert.Equal(["nksis://wiki/docs/folder", "nksis://wiki/docs/page-a", "nksis://wiki/docs/page-b"], entries.Select(e => e.GetProperty("uri").GetString()));
        Assert.Equal([true, false, false], entries.Select(e => e.GetProperty("isDirectory").GetBoolean()));
        Assert.Contains(_wiki.Requests, r => r.Contains("page=2"));

        var read = await ReadAsync(Page);
        Assert.Equal("one\n", read.GetProperty("text").GetString());
        var revision = read.GetProperty("revision").GetString();
        Assert.Equal(FakeWiki.TokenOf(1), revision);
        var stat = await CallAsync(DocumentRequests.SourceStat, new { uri = Page });
        Assert.Equal(revision, stat.GetProperty("revision").GetString());

        var saved = await WriteAsync(Page, "edited one\n", revision);

        Assert.Equal(FakeWiki.TokenOf(2), saved.GetProperty("revision").GetString());
        Assert.Equal(("edited one\n", 2), _wiki.Pages["docs/page-a"]);
        Assert.Equal("edited one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_save_by_someone_else_changes_the_revision_token_which_is_what_the_editor_compares_before_it_writes()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        var revision = (await ReadAsync(Page)).GetProperty("revision").GetString();

        _wiki.Pages["docs/page-a"] = ("changed by a colleague\n", 2);

        var stat = await CallAsync(DocumentRequests.SourceStat, new { uri = Page });
        Assert.NotEqual(revision, stat.GetProperty("revision").GetString());
        // a page that is gone has no revision at all
        _wiki.Pages.Remove("docs/page-a");
        Assert.False((await CallAsync(DocumentRequests.SourceStat, new { uri = Page })).GetProperty("exists").GetBoolean());
    }

    [Fact]
    public async Task The_wiki_checks_no_revision_so_the_write_is_declared_non_atomic_and_the_plugin_does_not_pretend_otherwise()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        _wiki.Pages["docs/page-a"] = ("changed by a colleague\n", 2);

        await WriteAsync(Page, "mine\n", FakeWiki.TokenOf(1)); // the stale token is not looked at by the wiki; the editor compared it before

        Assert.Equal(("mine\n", 3), _wiki.Pages["docs/page-a"]);
        Assert.False(WikiPlugin.AtomicWriteVerified);
    }

    [Fact]
    public async Task Over_the_rate_limit_the_wiki_is_reported_unavailable_with_a_clear_message_and_asked_once()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        _wiki.RateLimited = true;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Equal(DocumentErrorCodes.SourceUnavailable, e.Code);
        Assert.Contains("60 requests per minute", e.Message);
        Assert.Single(_wiki.Requests); // no retry loop
        Assert.True(_secrets.Values.ContainsKey(WikiPlugin.TokenSecret)); // the key is fine, only the rate is not
    }

    [Fact]
    public async Task Without_a_token_the_user_is_asked_and_when_nothing_is_entered_nothing_is_sent()
    {
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        _tokenAnswer = null;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Contains("No API key", e.Message);
        Assert.Equal(["NKS IS wiki: API key [hidden]"], _editor.Asked); // the typing is hidden
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
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
        _wiki.RejectToken = true;

        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Contains("missing or invalid", e.Message);
        Assert.False(_secrets.Values.ContainsKey(WikiPlugin.TokenSecret));
        _wiki.RejectToken = false;
        Assert.Equal("one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
        Assert.Single(_editor.Asked);
    }

    [Fact]
    public async Task The_wiki_is_reached_through_the_editor_so_without_the_network_permission_nothing_is_sent()
    {
        _secrets.Values[WikiPlugin.TokenSecret] = Token;
        _wiki.Pages["docs/page-a"] = ("one\n", 1);

        Assert.Equal("one\n", (await ReadAsync(Page)).GetProperty("text").GetString());
        Assert.Contains(_log.Entries(), l => l.Method == PluginNetwork.Request && l.Outcome == CallOutcome.Ok);

        _permissions.Revoke(PluginId, PluginPermissionNames.Network, GrantScope.All);
        _wiki.Requests.Clear();
        var e = await Assert.ThrowsAsync<RpcException>(() => ReadAsync(Page));

        Assert.Equal(DocumentErrorCodes.SourceUnavailable, e.Code);
        Assert.Empty(_wiki.Requests);
        Assert.Contains(_log.Entries(), l => l.Method == PluginNetwork.Request && l.Outcome == CallOutcome.Denied);
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
        _wiki.Pages["docs/page-a"] = ("one\n", 1);
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

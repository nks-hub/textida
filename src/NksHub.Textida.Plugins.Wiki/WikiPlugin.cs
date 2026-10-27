using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Wiki;

/// <summary>Limits and language of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
public sealed record WikiSettings(TimeSpan Timeout, long MaxBytes, int MaxEntries, string Language);

/// <summary>What the plugin asks of the editor: its secret storage (the OS credential store) and a prompt. The token only ever travels through these.</summary>
public interface IWikiEditor
{
    Task<string?> GetSecretAsync(string name, CancellationToken cancellation);
    Task SetSecretAsync(string name, string value, CancellationToken cancellation);
    Task DeleteSecretAsync(string name, CancellationToken cancellation);

    /// <summary>The text the user entered, or null when the prompt was dismissed. <paramref name="secret"/> hides the typing.</summary>
    Task<string?> AskAsync(string title, string label, bool secret, CancellationToken cancellation);
}

/// <summary>
/// The NKS IS wiki as a file source for the scheme <c>nksis</c> (P6.4.1): <c>nksis://wiki/&lt;page&gt;</c> is a page; the authority is only a label. The base URL of the wiki and the
/// access token are asked from the user once and kept in the secret storage of the editor under the names <see cref="BaseUrlSecret"/> and <see cref="TokenSecret"/>;
/// no host is built in, and the token is never part of an address, a log line or a message. Without a base URL or a token nothing is sent.
/// The write is declared non-atomic (<see cref="AtomicWriteVerified"/>) until the wiki checks the expected revision in one step (P6.4.4), so the editor warns before saving.
/// </summary>
public sealed class WikiPlugin : PluginBase
{
    public const string Scheme = "nksis";
    public const string BaseUrlSecret = "baseUrl";
    public const string TokenSecret = "token";

    /// <summary>False until the contract test of P6.4.4 (two concurrent writes against one revision, exactly one wins) has passed against the real service.</summary>
    public const bool AtomicWriteVerified = false;

    private readonly WikiSettings _settings;
    private readonly Func<IWikiEditor> _editor;
    private readonly Func<Uri, string, IWikiApi> _connect;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IWikiApi? _api;

    public WikiPlugin(WikiSettings settings, Func<IWikiEditor>? editor = null, Func<Uri, string, IWikiApi>? connect = null) : base("nks-wiki", apiVersion: 1)
    {
        _settings = settings;
        _editor = editor ?? (() => new RpcEditor(Rpc));
        _connect = connect ?? ((baseUrl, token) => new AssumedWikiHttp(Shared, baseUrl, token, settings.Timeout));
        On(DocumentRequests.SourceList, (p, ct) => Guard(() => ListAsync(p, ct), ct));
        On(DocumentRequests.SourceRead, (p, ct) => Guard(() => ReadAsync(p, ct), ct));
        On(DocumentRequests.SourceStat, (p, ct) => Guard(() => StatAsync(p, ct), ct));
        On(DocumentRequests.SourceWrite, (p, ct) => Guard(() => WriteAsync(p, ct), ct));
    }

    private static readonly HttpClient Shared = new();

    protected override async ValueTask OnResyncAsync(JsonElement state)
    {
        var request = Obj(w =>
        {
            w.WriteString("scheme", Scheme);
            w.WriteString("name", "NKS IS wiki");
            w.WriteBoolean("list", true);
            w.WriteBoolean("write", true);
            w.WriteBoolean("atomicWrite", AtomicWriteVerified);
        });
        await Rpc.RequestAsync(DocumentMethods.SourceRegister, request, null, CancellationToken.None).ConfigureAwait(false);
    }

    private string T(string key, params (string Name, string Value)[] arguments) => Texts.Get(_settings.Language, key, arguments);

    private static byte[] Obj(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static RpcException Fail(string message) => new(RpcErrorCodes.InternalError, message);

    /// <summary>Turns what the wiki layer throws into the errors of the source protocol, so the editor can tell a conflict from an outage.</summary>
    private async ValueTask<byte[]?> Guard(Func<Task<byte[]>> work, CancellationToken ct)
    {
        try { return await work().ConfigureAwait(false); }
        catch (WikiConflictException e)
        {
            var data = Obj(w => { if (e.CurrentRevision is null) w.WriteNull("currentRevision"); else w.WriteString("currentRevision", e.CurrentRevision); });
            throw new RpcException(DocumentErrorCodes.SourceConflict, T("conflict")) { DataJson = Encoding.UTF8.GetString(data) };
        }
        catch (WikiAuthException)
        {
            _api = null;
            await _editor().DeleteSecretAsync(TokenSecret, ct).ConfigureAwait(false);
            throw Fail(T("rejected"));
        }
        catch (WikiUnavailableException e) { throw new RpcException(DocumentErrorCodes.SourceUnavailable, T("unreachable", ("reason", e.Message))); }
        catch (WikiRequestException e) { throw Fail(T("failed", ("status", e.Status.ToString()))); }
    }

    // ---- connection: base URL and token from the secret storage, asked once ----

    private async Task<IWikiApi> ConnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_api is not null) return _api;
            var editor = _editor();
            var text = await editor.GetSecretAsync(BaseUrlSecret, ct).ConfigureAwait(false);
            bool asked = string.IsNullOrWhiteSpace(text);
            if (asked)
            {
                text = (await editor.AskAsync(T("askBaseTitle"), T("askBaseLabel"), false, ct).ConfigureAwait(false))?.Trim();
                if (string.IsNullOrEmpty(text)) throw Fail(T("noBaseUrl"));
            }
            if (!Uri.TryCreate(text, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https") || baseUrl.UserInfo.Length > 0)
                throw Fail(T("badBaseUrl"));
            var token = await editor.GetSecretAsync(TokenSecret, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                token = (await editor.AskAsync(T("askTokenTitle"), T("askTokenLabel"), true, ct).ConfigureAwait(false))?.Trim();
                if (string.IsNullOrEmpty(token)) throw Fail(T("noToken"));
                await editor.SetSecretAsync(TokenSecret, token, ct).ConfigureAwait(false);
            }
            if (asked) await editor.SetSecretAsync(BaseUrlSecret, baseUrl.AbsoluteUri, ct).ConfigureAwait(false);
            return _api = _connect(baseUrl, token);
        }
        finally { _gate.Release(); }
    }

    // ---- addresses ----

    /// <summary>The page of <c>nksis://wiki/a/b</c> is <c>a/b</c>; query and fragment are not part of it. Empty for the root.</summary>
    private string SlugOf(JsonElement p, bool required)
    {
        var uri = p.GetProperty("uri").GetString() ?? "";
        const string head = Scheme + "://";
        if (!uri.StartsWith(head, StringComparison.OrdinalIgnoreCase)) throw new RpcException(RpcErrorCodes.InvalidParams, T("badAddress", ("uri", NksHub.Textida.Core.Secrets.UriMask.Mask(uri))));
        var rest = uri[head.Length..];
        int cut = rest.IndexOfAny(['?', '#']);
        if (cut >= 0) rest = rest[..cut];
        int slash = rest.IndexOf('/');
        var slug = slash < 0 ? "" : Uri.UnescapeDataString(rest[(slash + 1)..]).Trim('/');
        if (required && slug.Length == 0) throw new RpcException(RpcErrorCodes.InvalidParams, T("badAddress", ("uri", NksHub.Textida.Core.Secrets.UriMask.Mask(uri))));
        return slug;
    }

    private static string UriOf(string slug) => Scheme + "://wiki/" + string.Join('/', slug.Split('/').Select(Uri.EscapeDataString));

    // ---- the source protocol ----

    private async Task<byte[]> ListAsync(JsonElement p, CancellationToken ct)
    {
        var prefix = SlugOf(p, required: false);
        var api = await ConnectAsync(ct).ConfigureAwait(false);
        var pages = await api.ListAsync(prefix, ct).ConfigureAwait(false);
        return Obj(w =>
        {
            w.WriteStartArray("entries");
            foreach (var page in pages.Take(_settings.MaxEntries))
            {
                w.WriteStartObject();
                w.WriteString("uri", UriOf(page.Slug));
                w.WriteString("name", page.Title ?? page.Slug[(page.Slug.LastIndexOf('/') + 1)..]);
                w.WriteBoolean("isDirectory", false);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    private async Task<byte[]> ReadAsync(JsonElement p, CancellationToken ct)
    {
        var slug = SlugOf(p, required: true);
        var page = await (await ConnectAsync(ct).ConfigureAwait(false)).ReadAsync(slug, ct).ConfigureAwait(false)
            ?? throw new RpcException(DocumentErrorCodes.SourceNotFound, T("notFound", ("slug", slug)));
        if (Encoding.UTF8.GetByteCount(page.Text) > _settings.MaxBytes) throw new RpcException(RpcErrorCodes.MessageTooLarge, T("tooLarge", ("max", _settings.MaxBytes.ToString())));
        return Obj(w => { w.WriteString("revision", page.Revision); w.WriteString("text", page.Text); });
    }

    private async Task<byte[]> StatAsync(JsonElement p, CancellationToken ct)
    {
        var slug = SlugOf(p, required: true);
        var page = await (await ConnectAsync(ct).ConfigureAwait(false)).ReadAsync(slug, ct).ConfigureAwait(false);
        return page is null
            ? Obj(w => w.WriteBoolean("exists", false))
            : Obj(w => { w.WriteString("revision", page.Revision); w.WriteNumber("size", Encoding.UTF8.GetByteCount(page.Text)); });
    }

    private async Task<byte[]> WriteAsync(JsonElement p, CancellationToken ct)
    {
        var slug = SlugOf(p, required: true);
        var expected = p.TryGetProperty("expectedRevision", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        var bytes = Convert.FromBase64String(p.GetProperty("bytes").GetString()!);
        if (bytes.LongLength > _settings.MaxBytes) throw new RpcException(RpcErrorCodes.MessageTooLarge, T("tooLarge", ("max", _settings.MaxBytes.ToString())));
        string text;
        ReadOnlySpan<byte> content = bytes.AsSpan().StartsWith<byte>([0xEF, 0xBB, 0xBF]) ? bytes.AsSpan(3) : bytes;
        try { text = new UTF8Encoding(false, true).GetString(content); }
        catch (DecoderFallbackException) { throw Fail(T("notText")); }
        var revision = await (await ConnectAsync(ct).ConfigureAwait(false)).WriteAsync(slug, text, expected, ct).ConfigureAwait(false);
        return Obj(w => w.WriteString("revision", revision));
    }

    /// <summary>The editor side over the plugin connection: <c>secret/*</c> and <c>ui/input</c>.</summary>
    private sealed class RpcEditor(JsonRpcDispatcher rpc) : IWikiEditor
    {
        private Task<JsonElement> CallAsync(string method, byte[] request, CancellationToken ct) => rpc.RequestAsync(method, request, null, ct);

        public async Task<string?> GetSecretAsync(string name, CancellationToken ct)
        {
            var answer = await CallAsync("secret/get", Obj(w => w.WriteString("name", name)), ct).ConfigureAwait(false);
            return answer.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        public async Task SetSecretAsync(string name, string value, CancellationToken ct) =>
            await CallAsync("secret/set", Obj(w => { w.WriteString("name", name); w.WriteString("value", value); }), ct).ConfigureAwait(false);

        public async Task DeleteSecretAsync(string name, CancellationToken ct) =>
            await CallAsync("secret/delete", Obj(w => w.WriteString("name", name)), ct).ConfigureAwait(false);

        public async Task<string?> AskAsync(string title, string label, bool secret, CancellationToken ct)
        {
            var answer = await CallAsync("ui/input", Obj(w => { w.WriteString("title", title); w.WriteString("label", label); w.WriteBoolean("secret", secret); }), ct).ConfigureAwait(false);
            return answer.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }
    }
}

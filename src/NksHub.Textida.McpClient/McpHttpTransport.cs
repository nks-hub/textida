using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.McpClient;

/// <summary>
/// Streamable HTTP for <see cref="McpClient"/> (protocol 2025-11-25). The JSON-RPC layer writes and reads lines; this class makes one POST of every line it is given and turns what
/// comes back (a JSON body, or an event stream that ends with the answer) into lines for the reader. A failed exchange becomes an error answer to the request, so the caller
/// that waits for it learns why: <see cref="AuthErrorCode"/> for a refused key, <see cref="FailedErrorCode"/> for anything else. Cancelling a request (<c>notifications/cancelled</c>)
/// also ends the read of its answer. The session the server opens with <c>Mcp-Session-Id</c> is echoed and ended with a DELETE when the transport is disposed.
/// Nothing here opens the optional GET stream for messages the server starts on its own.
/// </summary>
internal sealed class McpHttpTransport : IAsyncDisposable
{
    public const int AuthErrorCode = -32061;
    public const int FailedErrorCode = -32062;

    private readonly record struct Peeked(RpcId? Id, string? Method, RpcId? CancelTarget)
    {
        public bool IsRequest => Id is not null && Method is not null;
    }

    private sealed class Exchange(Peeked sent, CancellationTokenSource cts)
    {
        public Peeked Sent { get; } = sent;
        public CancellationTokenSource Cts { get; } = cts;
        public bool Answered { get; set; }
    }

    private readonly McpRemote _remote;
    private readonly HttpClient _http;
    private readonly int _maxBytes;
    private readonly TimeSpan _grace;
    private readonly Channel<byte[]> _incoming = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { SingleReader = true });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<RpcId, CancellationTokenSource> _inFlight = [];
    private readonly HashSet<Task> _posts = [];
    private readonly object _gate = new();
    private volatile string? _sessionId;
    private int _disposed;

    /// <summary>Sent as <c>MCP-Protocol-Version</c> once the handshake has chosen the version.</summary>
    public volatile string? ProtocolVersion;

    public McpHttpTransport(McpRemote remote, HttpClient http, int maxBytes, TimeSpan grace)
    {
        _remote = remote;
        _http = http;
        _maxBytes = maxBytes;
        _grace = grace;
        Input = new LineSource(_incoming.Reader);
        Output = new LineSink(this);
    }

    /// <summary>What the JSON-RPC layer reads: the answers, one per line.</summary>
    public Stream Input { get; }

    /// <summary>What the JSON-RPC layer writes: every complete line is sent as one POST.</summary>
    public Stream Output { get; }

    // ---- sending ----

    /// <summary>Sends the message. The task of a notification is returned: the writer waits for it so that a request after <c>notifications/initialized</c> cannot overtake it.</summary>
    private Task? Send(byte[] message)
    {
        if (Volatile.Read(ref _disposed) == 1) return null;
        var sent = Peek(message);
        var exchange = new Exchange(sent, CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
        Task post;
        lock (_gate)
        {
            if (sent.IsRequest) _inFlight[sent.Id!.Value] = exchange.Cts;
            if (sent.CancelTarget is { } target && _inFlight.Remove(target, out var stale)) Cancel(stale);
            post = Task.Run(() => PostAsync(message, exchange));
            _posts.Add(post);
        }
        _ = post.ContinueWith(t => { lock (_gate) _posts.Remove(t); }, TaskScheduler.Default);
        return sent.Method is not null && sent.Id is null && sent.CancelTarget is null ? post : null;
    }

    private static void Cancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* the exchange ended by itself */ }
    }

    private void AddHeaders(HttpRequestMessage request)
    {
        foreach (var (name, value) in _remote.Headers) request.Headers.TryAddWithoutValidation(name, value);
        if (_sessionId is { } session) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
        if (ProtocolVersion is { } version) request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
    }

    private async Task PostAsync(byte[] message, Exchange exchange)
    {
        var sent = exchange.Sent;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _remote.Url) { Content = new ByteArrayContent(message) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json", "utf-8");
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            AddHeaders(request);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, exchange.Cts.Token).ConfigureAwait(false);
            if (_sessionId is null && response.Headers.TryGetValues("Mcp-Session-Id", out var ids)) _sessionId = ids.FirstOrDefault();
            if (!response.IsSuccessStatusCode)
            {
                await FailAsync(sent, response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? AuthErrorCode : FailedErrorCode, Describe(response.StatusCode)).ConfigureAwait(false);
                return;
            }
            if (!sent.IsRequest) return; // a notification or an answer of ours is acknowledged with 202 and nothing else
            await using var body = await response.Content.ReadAsStreamAsync(exchange.Cts.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream") await ReadEventsAsync(body, exchange).ConfigureAwait(false);
            else await EmitAsync(await ReadAllAsync(body, exchange.Cts.Token).ConfigureAwait(false), exchange).ConfigureAwait(false);
            if (!exchange.Answered) await FailAsync(sent, FailedErrorCode, "The server ended its answer without answering the request.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (exchange.Cts.IsCancellationRequested) { /* cancelled by the caller or by the end of the session */ }
        catch (Exception e)
        {
            await FailAsync(sent, FailedErrorCode, _remote.Mask(e is HttpRequestException ? "The server cannot be reached: " + e.Message : e.Message)).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (sent.Id is { } id && _inFlight.TryGetValue(id, out var own) && ReferenceEquals(own, exchange.Cts)) _inFlight.Remove(id);
            }
            exchange.Cts.Dispose();
        }
    }

    private string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"The server refused the key (HTTP {(int)status}).",
        HttpStatusCode.NotFound when _sessionId is not null => "The server ended the session (HTTP 404).",
        _ => $"The server answered HTTP {(int)status}.",
    };

    // ---- reading ----

    private async Task<byte[]> ReadAllAsync(Stream body, CancellationToken ct)
    {
        using var all = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int n;
        while ((n = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            all.Write(chunk, 0, n);
            if (all.Length > _maxBytes) throw TooLarge();
        }
        return all.ToArray();
    }

    private McpClientException TooLarge() => new($"The server sent a message of more than {_maxBytes} bytes.");

    /// <summary>Reads server-sent events: the <c>data</c> lines up to a blank line are one message. Comments, ids and event names are ignored; an event without data (a priming event) is skipped.</summary>
    private async Task ReadEventsAsync(Stream body, Exchange exchange)
    {
        using var reader = new StreamReader(body, new UTF8Encoding(false), false, 8192);
        var chars = new char[8192];
        var line = new StringBuilder();
        var data = new StringBuilder();
        bool hasData = false;
        int n;
        while ((n = await reader.ReadAsync(chars.AsMemory(), exchange.Cts.Token).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                char c = chars[i];
                if (c == '\r') continue;
                if (c != '\n')
                {
                    line.Append(c);
                    if (line.Length > _maxBytes) throw TooLarge();
                    continue;
                }
                if (line.Length == 0)
                {
                    if (hasData && data.Length > 0) await EmitAsync(Encoding.UTF8.GetBytes(data.ToString()), exchange).ConfigureAwait(false);
                    if (exchange.Answered) return; // the answer ended the exchange; whatever the server still sends on this stream is not read
                    data.Clear();
                    hasData = false;
                    continue;
                }
                var text = line.ToString();
                line.Clear();
                if (text[0] == ':') continue;
                int colon = text.IndexOf(':');
                if ((colon < 0 ? text : text[..colon]) != "data") continue;
                var value = colon < 0 ? "" : text[(colon + 1)..];
                if (value.StartsWith(' ')) value = value[1..];
                if (hasData) data.Append('\n');
                data.Append(value);
                hasData = true;
                if (data.Length > _maxBytes) throw TooLarge();
            }
        }
        if (hasData && data.Length > 0) await EmitAsync(Encoding.UTF8.GetBytes(data.ToString()), exchange).ConfigureAwait(false);
    }

    /// <summary>Hands one JSON message (or the messages of a batch) to the reader as one compact line each.</summary>
    private async Task EmitAsync(byte[] json, Exchange exchange)
    {
        if (json.Length == 0) return; // an empty body answers nothing; the caller reports that
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToArray() : [doc.RootElement];
        foreach (var item in items)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var w = new Utf8JsonWriter(buffer)) item.WriteTo(w);
            var seen = Peek(buffer.WrittenMemory);
            if (seen.Method is null && seen.Id is not null && seen.Id == exchange.Sent.Id) exchange.Answered = true;
            await DeliverAsync(buffer.WrittenMemory).ConfigureAwait(false);
        }
    }

    private async Task FailAsync(Peeked sent, int code, string message)
    {
        if (!sent.IsRequest) return;
        await DeliverAsync(RpcWire.Error(sent.Id, code, message)).ConfigureAwait(false);
    }

    private async Task DeliverAsync(ReadOnlyMemory<byte> json)
    {
        var line = new byte[json.Length + 1];
        json.Span.CopyTo(line);
        line[^1] = (byte)'\n';
        try { await _incoming.Writer.WriteAsync(line, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception e) when (e is ChannelClosedException or OperationCanceledException) { /* disposed meanwhile */ }
    }

    /// <summary>Reads id, method and cancel target with the same parser and the same MCP rules the JSON-RPC layer uses; anything it would refuse is unknown here too.</summary>
    private static Peeked Peek(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var message = RpcMessage.Parse(json.ToArray());
            RpcId? target = message.Method is { } method && RpcProtocols.Mcp.TryParseCancel(method, message.Params, out var cancelled) ? cancelled : null;
            return new Peeked(message.Id, message.Method, target);
        }
        catch (RpcException) { return default; }
    }

    // ---- the end ----

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        if (_sessionId is not null)
        {
            try
            {
                using var stop = new CancellationTokenSource(_grace);
                using var request = new HttpRequestMessage(HttpMethod.Delete, _remote.Url);
                AddHeaders(request);
                using var response = await _http.SendAsync(request, stop.Token).ConfigureAwait(false); // a server may answer 405: it does not end sessions on request
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { /* best effort */ }
        }
        await _lifetime.CancelAsync().ConfigureAwait(false);
        _incoming.Writer.TryComplete();
        Task[] posts;
        lock (_gate) posts = [.. _posts];
        try { await Task.WhenAll(posts).WaitAsync(_grace).ConfigureAwait(false); }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException) { }
        _http.Dispose();
    }

    // ---- the two streams the JSON-RPC layer sees ----

    private abstract class OneWayStream(bool read) : Stream
    {
        public override bool CanRead => read;
        public override bool CanWrite => !read;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class LineSource(ChannelReader<byte[]> reader) : OneWayStream(true)
    {
        private byte[] _current = [];
        private int _offset;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_offset >= _current.Length)
            {
                if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
                if (reader.TryRead(out var next)) (_current, _offset) = (next, 0);
            }
            int count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }
    }

    private sealed class LineSink(McpHttpTransport owner) : OneWayStream(false)
    {
        private readonly MemoryStream _pending = new();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            foreach (var line in TakeLines(buffer))
            {
                if (owner.Send(line) is not { } notification) continue;
                try { await notification.WaitAsync(owner._grace, cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) { /* a server that does not acknowledge a notification must not stop the messages behind it */ }
            }
        }

        private List<byte[]> TakeLines(ReadOnlyMemory<byte> buffer)
        {
            _pending.Write(buffer.Span);
            var all = _pending.GetBuffer().AsSpan(0, (int)_pending.Length);
            var lines = new List<byte[]>();
            int start = 0, end;
            while ((end = all[start..].IndexOf((byte)'\n')) >= 0)
            {
                if (end > 0) lines.Add(all.Slice(start, end).ToArray());
                start += end + 1;
            }
            var rest = all[start..].ToArray();
            _pending.SetLength(0);
            _pending.Write(rest);
            return lines;
        }
    }
}

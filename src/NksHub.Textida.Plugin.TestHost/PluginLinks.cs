using System.Buffers;
using System.Text.Json;
using System.Threading.Channels;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;
using Host = NksHub.Textida.PluginHost.PluginHost;

namespace NksHub.Textida.Plugin.Testing;

/// <summary>How the editor side reaches a plugin: it sends events and requests, and the plugin's requests come back through the API callback given when the link was made.</summary>
internal interface IPluginLink : IAsyncDisposable
{
    Task<bool> SendEventAsync(string pluginId, string name, ReadOnlyMemory<byte> payloadJson);

    Task<JsonElement> RequestAsync(string pluginId, string method, ReadOnlyMemory<byte> paramsJson, TimeSpan? timeout, CancellationToken ct);
}

/// <summary>One direction of an in-memory byte pipe.</summary>
internal sealed class MemoryPipe : Stream
{
    private readonly Channel<byte[]> _channel = Channel.CreateUnbounded<byte[]>();
    private byte[] _current = [];
    private int _pos;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (_pos >= _current.Length)
        {
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return 0;
            }
            catch (ChannelClosedException) { return 0; }
            if (_channel.Reader.TryRead(out var next)) { _current = next; _pos = 0; }
        }
        var n = Math.Min(buffer.Length, _current.Length - _pos);
        _current.AsMemory(_pos, n).CopyTo(buffer);
        _pos += n;
        return n;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
        _channel.Writer.TryWrite(buffer.ToArray()) ? ValueTask.CompletedTask : throw new IOException("The pipe is closed.");

    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        _channel.Writer.TryComplete();
        base.Dispose(disposing);
    }
}

/// <summary>The connection of a plugin that is driven by test code: it answers the handshake itself and hands the test every event of the editor.</summary>
public sealed class RawPlugin
{
    private readonly List<EditorEvent> _events = [];

    /// <summary>The connection of the plugin side, for the clients of the SDK that take one (for example <see cref="NksHub.Textida.PluginSdk.DocumentClient"/>).</summary>
    public JsonRpcDispatcher Connection { get; internal set; } = null!;

    /// <summary>Answers the requests the editor sends to the plugin (such as <c>virtual/save</c> or <c>completion/request</c>): the method and parameters in, the result as JSON out. Null leaves them unanswered with a method-not-found error.</summary>
    public Func<string, JsonElement, byte[]?>? Answer { get; set; }

    /// <summary>The events the editor sent, in order.</summary>
    public IReadOnlyList<EditorEvent> Events { get { lock (_events) return [.. _events]; } }

    /// <summary>Sends a request to the editor as the plugin would. An error answer is thrown as <see cref="RpcException"/>.</summary>
    public async Task<JsonElement> RequestAsync(string method, string paramsJson = "{}", CancellationToken ct = default) =>
        (await Connection.RequestAsync(method, System.Text.Encoding.UTF8.GetBytes(paramsJson), null, ct).ConfigureAwait(false)).Clone();

    /// <summary>Waits until the editor has sent an event with this name, and returns it.</summary>
    public async Task<EditorEvent> WaitForEventAsync(string name, TimeSpan? timeout = null)
    {
        var stop = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < stop)
        {
            if (Events.FirstOrDefault(e => e.Name == name) is { } found) return found;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException($"The editor sent no '{name}' event.");
    }

    internal void Add(EditorEvent e) { lock (_events) _events.Add(e); }
}

/// <summary>The plugin runs in this process: its <see cref="PluginBase"/> object, or test code behind a <see cref="RawPlugin"/>, on a pair of in-memory pipes.</summary>
internal sealed class InProcessLink : IPluginLink
{
    private readonly JsonRpcDispatcher _editor;
    private readonly JsonRpcDispatcher? _rawSide;
    private readonly Task? _run;
    private readonly MemoryPipe _toPlugin;
    private readonly MemoryPipe _toEditor;

    private InProcessLink(JsonRpcDispatcher editor, JsonRpcDispatcher? rawSide, Task? run, MemoryPipe toPlugin, MemoryPipe toEditor)
    {
        (_editor, _rawSide, _run, _toPlugin, _toEditor) = (editor, rawSide, run, toPlugin, toEditor);
    }

    public static async Task<InProcessLink> StartAsync(PluginApiCallback api, string pluginId, PluginBase? plugin, RawPlugin? raw, int apiVersion, Func<byte[]> state, RpcLimits limits, TimeSpan timeout)
    {
        var toPlugin = new MemoryPipe();
        var toEditor = new MemoryPipe();
        var editor = new JsonRpcDispatcher(toEditor, toPlugin, RpcProtocols.Plugin, limits, new EditorSide(api, pluginId));
        JsonRpcDispatcher? rawSide = null;
        Task? run = null;
        if (plugin is not null) run = PluginRunner.RunAsync(plugin, limits, toPlugin, toEditor);
        else
        {
            rawSide = new JsonRpcDispatcher(toPlugin, toEditor, RpcProtocols.Plugin, limits, new RawSide(raw!, apiVersion));
            raw!.Connection = rawSide;
        }
        var link = new InProcessLink(editor, rawSide, run, toPlugin, toEditor);
        try { await link.HandshakeAsync(pluginId, apiVersion, state, timeout).ConfigureAwait(false); }
        catch
        {
            await link.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return link;
    }

    private async Task HandshakeAsync(string pluginId, int apiVersion, Func<byte[]> state, TimeSpan timeout)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new InitializeParams(apiVersion, false, "Textida test host"), PluginJson.Default.InitializeParams);
        InitializeResult? result;
        try { result = (await _editor.RequestAsync(PluginMethods.Initialize, request, timeout).ConfigureAwait(false)).Deserialize(PluginJson.Default.InitializeResult); }
        catch (RpcException e) { throw new PluginHandshakeException(pluginId, $"The plugin refused the handshake: {e.Message}"); }
        if (result is null || result.ApiVersion != apiVersion)
            throw new PluginHandshakeException(pluginId, $"The plugin implements plugin API version {result?.ApiVersion.ToString() ?? "(none)"}, but the test host provides version {apiVersion}.");
        await _editor.NotifyAsync(PluginMethods.Initialized).ConfigureAwait(false);

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("restarted", false);
            w.WriteNumber("lostEvents", 0);
            w.WritePropertyName("state");
            using (var d = JsonDocument.Parse(state())) d.RootElement.WriteTo(w);
            w.WriteEndObject();
        }
        try { await _editor.RequestAsync(PluginMethods.Resync, buffer.WrittenMemory, timeout).ConfigureAwait(false); }
        catch (RpcException e) { throw new PluginHandshakeException(pluginId, $"The plugin failed the state resync: {e.Message}"); }
    }

    public async Task<bool> SendEventAsync(string pluginId, string name, ReadOnlyMemory<byte> payloadJson)
    {
        try
        {
            await _editor.NotifyAsync(PluginMethods.Event, EventPayload(name, payloadJson)).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException) { return false; }
    }

    public Task<JsonElement> RequestAsync(string pluginId, string method, ReadOnlyMemory<byte> paramsJson, TimeSpan? timeout, CancellationToken ct) =>
        _editor.RequestAsync(method, paramsJson, timeout, ct);

    public async ValueTask DisposeAsync()
    {
        try { await _editor.NotifyAsync(PluginMethods.Exit).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or OperationCanceledException) { }
        if (_run is not null)
        {
            try { await _run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
            catch (TimeoutException) { }
        }
        await _editor.DisposeAsync().ConfigureAwait(false);
        if (_rawSide is not null) await _rawSide.DisposeAsync().ConfigureAwait(false);
        _toPlugin.Dispose();
        _toEditor.Dispose();
    }

    /// <summary>The event as the editor sends it: <c>{"name", "payload"}</c>.</summary>
    internal static byte[] EventPayload(string name, ReadOnlyMemory<byte> payloadJson)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WritePropertyName("payload");
            if (payloadJson.IsEmpty) w.WriteNullValue();
            else { using var d = JsonDocument.Parse(payloadJson); d.RootElement.WriteTo(w); }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private sealed class EditorSide(PluginApiCallback api, string pluginId) : IRpcHandler
    {
        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation) => api(pluginId, method, parameters, cancellation);

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters) => ValueTask.CompletedTask;
    }

    private sealed class RawSide(RawPlugin raw, int apiVersion) : IRpcHandler
    {
        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation)
        {
            switch (method)
            {
                case PluginMethods.Initialize:
                    return ValueTask.FromResult<byte[]?>(JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(apiVersion, "raw plugin"), PluginJson.Default.InitializeResult));
                case PluginMethods.Resync:
                    return ValueTask.FromResult<byte[]?>(null);
                case PluginMethods.Ping:
                case PluginMethods.Shutdown:
                    return ValueTask.FromResult<byte[]?>("true"u8.ToArray());
            }
            return raw.Answer?.Invoke(method, parameters) is { } answer
                ? ValueTask.FromResult<byte[]?>(answer)
                : throw new RpcException(RpcErrorCodes.MethodNotFound, $"The plugin has no method '{method}'.");
        }

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters)
        {
            if (method == PluginMethods.Event && parameters.TryGetProperty("name", out var name))
                raw.Add(new EditorEvent(name.GetString() ?? "", parameters.TryGetProperty("payload", out var payload) ? payload.GetRawText() : "null"));
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>The plugin is a program: the real plugin host of the editor starts it, shakes hands, pings it and ends it.</summary>
internal sealed class ProcessLink : IPluginLink
{
    private readonly Host _host;
    private readonly string _pluginId;

    private ProcessLink(Host host, string pluginId) => (_host, _pluginId) = (host, pluginId);

    public static async Task<ProcessLink> StartAsync(PluginApiCallback api, IPluginStateSource state, PluginSpec spec, PluginHostOptions options)
    {
        var host = new Host(options, api, state);
        host.Register(spec);
        try { await host.StartAsync(spec.Id).ConfigureAwait(false); }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new ProcessLink(host, spec.Id);
    }

    /// <summary>The last lines the plugin wrote to its standard error.</summary>
    public IReadOnlyList<string> StderrTail => _host.GetStderrTail(_pluginId);

    public Task<bool> SendEventAsync(string pluginId, string name, ReadOnlyMemory<byte> payloadJson) => _host.SendEventAsync(pluginId, name, payloadJson, paced: true);

    public Task<JsonElement> RequestAsync(string pluginId, string method, ReadOnlyMemory<byte> paramsJson, TimeSpan? timeout, CancellationToken ct) =>
        _host.CallAsync(pluginId, method, paramsJson, timeout, ct);

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}

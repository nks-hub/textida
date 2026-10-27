using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;

namespace NksHub.Textida.App.Startup;

/// <summary>One file in an <see cref="IpcRequest"/>.</summary>
public sealed class IpcFile
{
    public string Path { get; set; } = "";
    public int? Line { get; set; }
    public bool Hex { get; set; }
    public long? Offset { get; set; }
}

/// <summary>What a second launch asks the running instance to do. One JSON line on the wire.</summary>
public sealed class IpcRequest
{
    public List<IpcFile> Files { get; set; } = [];
    /// <summary>The two paths of a comparison (left, right); null when none is asked for.</summary>
    public List<string>? Diff { get; set; }
    public bool NewWindow { get; set; }
    /// <summary>The reply is sent only after every requested tab has been closed.</summary>
    public bool Wait { get; set; }
}

public sealed class IpcResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(IpcResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class IpcJson : JsonSerializerContext;

/// <summary>
/// Local, per-user transport: a named pipe on Windows, a unix socket in a 0700 directory elsewhere.
/// Ownership of the endpoint is decided by an exclusive lock file, so two simultaneous launches cannot both become the server.
/// </summary>
public static class IpcEndpoint
{
    private const int MaxMessageBytes = 1 << 20;

    /// <summary>Directory for the lock file and the unix socket; created private to the current user.</summary>
    internal static string RuntimeDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "textida-" + Sanitize(Environment.UserName));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }

    internal static string PipeName(string channel) => $"textida-{Sanitize(Environment.UserName)}-{Sanitize(channel)}";

    internal static string SocketPath(string channel)
    {
        var path = Path.Combine(RuntimeDirectory(), Sanitize(channel) + ".sock");
        if (Encoding.UTF8.GetByteCount(path) > 100) throw new IOException($"unix socket path too long: {path}");
        return path;
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.ToString();
    }

    /// <summary>Takes the right to be the running instance of a channel, or returns null when another process holds it.</summary>
    public static IDisposable? TryAcquire(string channel)
    {
        try
        {
            return new FileStream(Path.Combine(RuntimeDirectory(), Sanitize(channel) + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await stream.ReadAsync(one, ct) == 1)
        {
            if (one[0] == (byte)'\n') return Encoding.UTF8.GetString(bytes.ToArray());
            bytes.Add(one[0]);
            if (bytes.Count > MaxMessageBytes) throw new InvalidDataException("IPC message too large");
        }
        return bytes.Count == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
    }

    internal static async Task WriteLineAsync(Stream stream, string json, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.UTF8.GetBytes(json + "\n"), ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task<Stream> ConnectAsync(string channel, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", PipeName(channel), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(cts.Token); }
            catch { await pipe.DisposeAsync(); throw; }
            return pipe;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath(channel)), cts.Token); }
        catch { socket.Dispose(); throw; }
        return new NetworkStream(socket, ownsSocket: true);
    }
}

/// <summary>Sends a request to the running instance and waits for its reply.</summary>
public static class IpcClient
{
    /// <summary>Retries the connection for <paramref name="startupGrace"/> because the owner of the lock may still be opening its listener.</summary>
    public static async Task<IpcResponse> SendAsync(string channel, IpcRequest request, TimeSpan startupGrace, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + startupGrace;
        Stream stream;
        while (true)
        {
            try { stream = await IpcEndpoint.ConnectAsync(channel, TimeSpan.FromMilliseconds(500), ct); break; }
            catch (Exception e) when (e is IOException or SocketException or TimeoutException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                if (DateTime.UtcNow >= deadline) throw new IOException("the running instance does not answer", e);
                await Task.Delay(100, ct);
            }
        }

        await using (stream)
        {
            await IpcEndpoint.WriteLineAsync(stream, System.Text.Json.JsonSerializer.Serialize(request, IpcJson.Default.IpcRequest), ct);
            var line = await IpcEndpoint.ReadLineAsync(stream, ct) ?? throw new IOException("the running instance closed the connection without a reply");
            return System.Text.Json.JsonSerializer.Deserialize(line, IpcJson.Default.IpcResponse) ?? throw new InvalidDataException("empty IPC reply");
        }
    }
}

/// <summary>Listens on a channel and passes each request to <paramref name="handler"/>; the reply is sent when the handler's task completes.</summary>
public sealed class IpcServer : IAsyncDisposable
{
    private readonly string _channel;
    private readonly Func<IpcRequest, Task> _handler;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private Socket? _listener;

    public IpcServer(string channel, Func<IpcRequest, Task> handler)
    {
        _channel = channel;
        _handler = handler;
        if (!OperatingSystem.IsWindows())
        {
            var path = IpcEndpoint.SocketPath(channel);
            if (File.Exists(path)) File.Delete(path); // stale: the lock proves no live owner
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(path));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _listener.Listen(16);
        }
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Stream stream;
                if (_listener is { } listener) stream = new NetworkStream(await listener.AcceptAsync(ct), ownsSocket: true);
                else
                {
                    var pipe = new NamedPipeServerStream(IpcEndpoint.PipeName(_channel), PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    try { await pipe.WaitForConnectionAsync(ct); }
                    catch { await pipe.DisposeAsync(); throw; }
                    stream = pipe;
                }
                Track(ServeAsync(stream, ct));
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (IOException) { await Task.Delay(50, CancellationToken.None); }
        }
    }

    private readonly HashSet<Task> _inflight = [];

    private void Track(Task task)
    {
        lock (_inflight) _inflight.Add(task);
        _ = task.ContinueWith(t => { lock (_inflight) _inflight.Remove(t); }, TaskScheduler.Default);
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        await using (stream)
        {
            var reply = new IpcResponse { Ok = true };
            try
            {
                var line = await IpcEndpoint.ReadLineAsync(stream, ct);
                if (line is null) return;
                var request = System.Text.Json.JsonSerializer.Deserialize(line, IpcJson.Default.IpcRequest) ?? throw new InvalidDataException("empty request");
                await _handler(request);
            }
            catch (OperationCanceledException) { reply = new IpcResponse { Ok = false, Error = "the instance is shutting down" }; }
            catch (Exception e) when (e is not OutOfMemoryException) { reply = new IpcResponse { Ok = false, Error = e.Message }; }

            try { await IpcEndpoint.WriteLineAsync(stream, System.Text.Json.JsonSerializer.Serialize(reply, IpcJson.Default.IpcResponse), CancellationToken.None); }
            catch (IOException) { /* the client went away */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener?.Dispose();
        try { await _loop; } catch (OperationCanceledException) { }
        Task[] pending;
        lock (_inflight) pending = [.. _inflight];
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(TimeSpan.FromSeconds(2))); // let waiting clients get their reply before the process ends
        if (_listener is not null) try { File.Delete(IpcEndpoint.SocketPath(_channel)); } catch (IOException) { }
        _cts.Dispose();
    }
}

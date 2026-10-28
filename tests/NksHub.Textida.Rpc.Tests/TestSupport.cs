using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>One direction of an in-memory byte pipe. Writes are cut into <c>chunk</c>-byte pieces so readers see partial and glued frames.</summary>
internal sealed class MemoryPipe(int capacityChunks = 1024, int chunk = int.MaxValue) : Stream
{
    readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(capacityChunks);
    byte[] _current = [];
    int _pos;

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
                if (!await _channel.Reader.WaitToReadAsync(ct)) return 0;
            }
            catch (ChannelClosedException) { return 0; }
            if (_channel.Reader.TryRead(out var next)) { _current = next; _pos = 0; }
        }
        var n = Math.Min(buffer.Length, _current.Length - _pos);
        _current.AsMemory(_pos, n).CopyTo(buffer);
        _pos += n;
        return n;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        for (var i = 0; i < buffer.Length; i += chunk)
            await _channel.Writer.WriteAsync(buffer.Slice(i, Math.Min(chunk, buffer.Length - i)).ToArray(), ct);
    }

    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    protected override void Dispose(bool disposing)
    {
        _channel.Writer.TryComplete();
        base.Dispose(disposing);
    }
}

internal sealed class DelegateHandler(
    Func<string, JsonElement, CancellationToken, ValueTask<byte[]?>>? onRequest = null,
    Func<string, JsonElement, ValueTask>? onNotification = null) : IRpcHandler
{
    public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken ct) =>
        onRequest is null ? throw new RpcException(RpcErrorCodes.MethodNotFound, method) : onRequest(method, p, ct);

    public ValueTask HandleNotificationAsync(string method, JsonElement p) => onNotification?.Invoke(method, p) ?? ValueTask.CompletedTask;
}

/// <summary>Two dispatchers wired to each other through memory pipes.</summary>
internal sealed class Pair : IAsyncDisposable
{
    public required JsonRpcDispatcher A { get; init; }
    public required JsonRpcDispatcher B { get; init; }
    public required MemoryPipe AToB { get; init; }
    public required MemoryPipe BToA { get; init; }

    public static Pair Create(RpcProtocol protocol, RpcLimits limitsA, IRpcHandler handlerA, RpcLimits limitsB, IRpcHandler handlerB, int chunk = int.MaxValue, int capacity = 1024)
    {
        var ab = new MemoryPipe(capacity, chunk);
        var ba = new MemoryPipe(capacity, chunk);
        return new Pair
        {
            AToB = ab,
            BToA = ba,
            A = new JsonRpcDispatcher(ba, ab, protocol, limitsA, handlerA),
            B = new JsonRpcDispatcher(ab, ba, protocol, limitsB, handlerB),
        };
    }

    public async ValueTask DisposeAsync()
    {
        await A.DisposeAsync();
        await B.DisposeAsync();
    }
}

internal static class T
{
    public static readonly RpcLimits Roomy = new(1 << 20, 32, 32, 10_000, 8 << 20, 8 << 20);

    public static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    public static async Task Until(Func<bool> condition, int timeoutMs = 15_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Condition not reached.");
            await Task.Delay(20);
        }
    }

    public static string DotnetHost => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    public static string PluginDll
    {
        get
        {
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
            Assert.True(File.Exists(dll), dll);
            return dll;
        }
    }

    /// <summary>Full path of the dotnet host; plugin manifests do not accept a bare program name.</summary>
    public static string DotnetFullPath
    {
        get
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (host is not null && File.Exists(host)) return host;
            return FindOnPath("dotnet.exe") ?? FindOnPath("dotnet") ?? throw new FileNotFoundException("dotnet host not found");
        }
    }

    public static string? FindOnPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, file)).FirstOrDefault(p => File.Exists(p) && new FileInfo(p).Length > 0);

    public static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "textida-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a folder that held a program a test started or killed: the system keeps the image file of an ended program locked for a moment, so the delete is repeated for a few seconds
    /// (on a loaded machine that moment is long). A folder that is still locked after that is left to the temp cleanup.
    /// </summary>
    public static void DeleteFolder(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 50) return;
                Thread.Sleep(100);
            }
        }
    }

    public static string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "textida-rpc-" + Guid.NewGuid().ToString("N") + ".txt");
        return path;
    }
}

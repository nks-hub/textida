using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>The pipes of child processes are read on threads of their own, so a plugin, a language server or an agent that stays silent holds no thread of the pool.</summary>
public class ChildPipeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A source whose reads block like those of a pipe: it gives the chunks it is handed and waits for more.</summary>
    private sealed class BlockingSource : Stream
    {
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]?> _chunks = [];
        private byte[] _current = [];
        private int _at;
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);
        public void Give(byte[] bytes) => _chunks.Add(bytes);
        public void End() => _chunks.Add(null);

        public override int Read(byte[] buffer, int offset, int count)
        {
            Interlocked.Increment(ref _reads);
            if (_at >= _current.Length)
            {
                var next = _chunks.Take();
                if (next is null) return 0;
                (_current, _at) = (next, 0);
            }
            int n = Math.Min(count, _current.Length - _at);
            Array.Copy(_current, _at, buffer, offset, n);
            _at += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<byte[]> ReadToEnd(Stream stream, int bufferSize)
    {
        using var all = new MemoryStream();
        var buffer = new byte[bufferSize];
        for (int n; (n = await stream.ReadAsync(buffer)) > 0;) all.Write(buffer, 0, n);
        return all.ToArray();
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(70_000)]
    public async Task TheBytesComeOutInOrderAndTheEndOfTheSourceIsTheEnd(int bufferSize)
    {
        var bytes = new byte[3_000_000];
        new Random(5).NextBytes(bytes);
        using var pipe = new ChildPipeStream(new MemoryStream(bytes));

        var read = await ReadToEnd(pipe, bufferSize);
        Assert.True(bytes.AsSpan().SequenceEqual(read));
        Assert.Equal(0, await pipe.ReadAsync(new byte[10]));
    }

    [Fact]
    public async Task AFailingSourceFailsTheReadAfterTheBytesItGave()
    {
        var buffer = new byte[10];
        using var broken = new ChildPipeStream(new ThrowAfterStream("def"u8.ToArray()));
        Assert.Equal(3, await broken.ReadAsync(buffer));
        await Assert.ThrowsAsync<IOException>(async () => await broken.ReadAsync(buffer));
    }

    private sealed class ThrowAfterStream(byte[] first) : Stream
    {
        private bool _given;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_given) throw new IOException("the pipe is broken");
            _given = true;
            first.CopyTo(buffer, offset);
            return first.Length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ASilentSourceLeavesTheReadWaitingAndACancelEndsIt()
    {
        using var source = new BlockingSource();
        using var pipe = new ChildPipeStream(source);
        using var cancel = new CancellationTokenSource();
        var waiting = pipe.ReadAsync(new byte[10], cancel.Token).AsTask();
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        source.Give("late"u8.ToArray()); // the pipe goes on after a cancelled read
        Assert.Equal(4, await pipe.ReadAsync(new byte[10]));
        source.End();
        Assert.Equal(0, await pipe.ReadAsync(new byte[10]));
    }

    [Fact]
    public async Task AReaderThatFallsBehindStopsTheThreadAfterAFewChunks()
    {
        using var source = new BlockingSource();
        using var pipe = new ChildPipeStream(source);
        for (int i = 0; i < 200; i++) source.Give(new byte[60_000]);
        await Task.Delay(300);

        Assert.InRange(source.Reads, 1, 40); // the queue holds a few chunks; the rest waits in the source, as it would in the pipe
        var buffer = new byte[60_000];
        for (int i = 0; i < 200; i++) Assert.Equal(60_000, await ReadFully(pipe, buffer));
    }

    private static async Task<int> ReadFully(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    [Fact]
    public async Task ADisposedPipeEndsTheReadsThatWait()
    {
        using var source = new BlockingSource();
        var pipe = new ChildPipeStream(source);
        var waiting = pipe.ReadAsync(new byte[10]).AsTask();
        await Task.Delay(50);
        pipe.Dispose();

        Assert.Equal(0, await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        source.End();
    }

    private static Process StartSleeper()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");
        Assert.True(File.Exists(dll), dll);
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--sleep");
        return Process.Start(info)!;
    }

    /// <summary>
    /// Many silent children, each with a read pending on its output and its error pipe, as a host with many plugins has: a task started meanwhile still runs at once. The floor of the pool that the
    /// tests set is lowered for the length of the test to the number of cores, which is what the editor runs with, so the reads of the old kind (they held a pool thread each) would show here as seconds.
    /// </summary>
    [Fact]
    public async Task ManyChildProcessesWithSilentPipesDoNotHoldUpTheThreadPool()
    {
        const int count = 16;
        var slowest = await SlowestTaskWithSilentChildren(count, viaChildPipe: true);
        Assert.True(slowest < TimeSpan.FromSeconds(1), $"a task waited {slowest.TotalMilliseconds:0} ms for a thread with {count} silent children");
    }

    /// <summary>The same with the pipes read the way they were before (the output and error streams of the process): prints how long a task waits then, to show what the test above guards against. Asserts nothing.</summary>
    [Fact]
    public async Task ForComparisonThePipesOfTheProcessClassHoldAThreadEach()
    {
        var raw = await SlowestTaskWithSilentChildren(16, viaChildPipe: false);
        var own = await SlowestTaskWithSilentChildren(16, viaChildPipe: true);
        output.WriteLine($"16 silent children on {Environment.ProcessorCount} cores: slowest Task.Run {raw.TotalMilliseconds:0} ms with the pipes of the process class, {own.TotalMilliseconds:0} ms with ChildPipe");
    }

    private static async Task<TimeSpan> SlowestTaskWithSilentChildren(int count, bool viaChildPipe)
    {
        ThreadPool.GetMinThreads(out int workerFloor, out int ioFloor);
        ThreadPool.SetMinThreads(Environment.ProcessorCount, ioFloor);
        var children = new List<Process>();
        var pending = new List<Task>();
        var streams = new List<IDisposable>();
        try
        {
            for (int i = 0; i < count; i++) children.Add(StartSleeper());
            foreach (var child in children)
            {
                var output = viaChildPipe ? ChildPipe.Output(child) : child.StandardOutput.BaseStream;
                var error = viaChildPipe ? ChildPipe.Error(child, new UTF8Encoding(false)) : child.StandardError;
                streams.Add(output);
                var said = new StringBuilder();
                var first = new byte[64];
                while (!said.ToString().Contains('\n'))
                {
                    int n = await output.ReadAsync(first).AsTask().WaitAsync(TimeSpan.FromSeconds(60));
                    Assert.True(n > 0, "the child ended before it said that it had started");
                    said.Append(Encoding.UTF8.GetString(first, 0, n));
                }
                pending.Add(output.ReadAsync(new byte[64]).AsTask()); // the child says nothing more
                pending.Add(error.ReadLineAsync());
            }
            await Task.Delay(200);
            Assert.All(pending, t => Assert.False(t.IsCompleted));

            var slowest = TimeSpan.Zero;
            for (int i = 0; i < (viaChildPipe ? 20 : 3); i++) // a starved pool answers in seconds
            {
                var clock = Stopwatch.StartNew();
                await Task.Run(() => i);
                if (clock.Elapsed > slowest) slowest = clock.Elapsed;
                await Task.Delay(20);
            }
            return slowest;
        }
        finally
        {
            foreach (var child in children)
            {
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            foreach (var child in children) child.WaitForExit(10_000);
            foreach (var stream in streams) stream.Dispose();
            foreach (var child in children) child.Dispose();
            ThreadPool.SetMinThreads(workerFloor, ioFloor);
        }
    }
}

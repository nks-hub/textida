using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The output of a child process read on a thread of its own. The pipes of a redirected child (and anonymous pipes) have no asynchronous reads in .NET: a
/// <c>ReadAsync</c> on them holds a thread-pool thread until data arrives. A plugin host with a dozen plugins, a language server and an agent keeps two such reads each
/// (standard output and standard error), and on a machine with few cores the pool then adds one thread every half second, so any <c>Task.Run</c> of the editor waits seconds.
/// This stream reads the pipe synchronously on a background thread that belongs to it and hands the bytes on through a channel, so a waiting <c>ReadAsync</c> holds nothing,
/// and a read can be cancelled. The thread ends when the pipe ends (the child exited or was killed) or fails. Read errors of the pipe come out of the next read, as they would from the pipe.
/// </summary>
public sealed class ChildPipeStream : Stream
{
    private const int ChunkBytes = 1 << 16;
    private const int QueuedChunks = 16;
    private const int ThreadStackBytes = 256 * 1024;

    private readonly record struct Chunk(byte[] Buffer, int Length);

    private readonly Stream _source;
    private readonly Channel<Chunk> _chunks = Channel.CreateBounded<Chunk>(new BoundedChannelOptions(QueuedChunks) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private Chunk _current;
    private int _offset;

    /// <summary>Starts reading <paramref name="source"/> at once; the stream does not dispose <paramref name="source"/>, whose owner (the process) does.</summary>
    public ChildPipeStream(Stream source)
    {
        _source = source;
        new Thread(Pump, ThreadStackBytes) { IsBackground = true, Name = "child-pipe" }.Start();
    }

    private void Pump()
    {
        Exception? failure = null;
        try
        {
            for (;;)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
                int n;
                try { n = _source.Read(buffer, 0, ChunkBytes); }
                catch
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw;
                }
                if (n <= 0 || !Hand(new Chunk(buffer, n))) break;
            }
        }
        catch (Exception e) when (!_stop.IsCancellationRequested)
        {
            failure = e;
        }
        catch (Exception)
        {
            // the stream was disposed: nobody reads any more
        }
        _chunks.Writer.TryComplete(failure);
    }

    /// <summary>Blocks this thread, not a pool thread, while the reader is behind; false when the stream was disposed meanwhile.</summary>
    private bool Hand(Chunk chunk)
    {
        try
        {
            _chunks.Writer.WriteAsync(chunk, _stop.Token).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception e) when (e is OperationCanceledException or ChannelClosedException)
        {
            ArrayPool<byte>.Shared.Return(chunk.Buffer);
            return false;
        }
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty) return 0;
        if (_current.Buffer is null)
        {
            if (!await _chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) return 0;
            if (!_chunks.Reader.TryRead(out _current)) return 0;
            _offset = 0;
        }
        int n = Math.Min(destination.Length, _current.Length - _offset);
        _current.Buffer.AsMemory(_offset, n).CopyTo(destination);
        _offset += n;
        if (_offset >= _current.Length)
        {
            ArrayPool<byte>.Shared.Return(_current.Buffer);
            _current = default;
        }
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Cancel();
            _chunks.Writer.TryComplete();
            while (_chunks.Reader.TryRead(out var chunk)) ArrayPool<byte>.Shared.Return(chunk.Buffer);
            if (_current.Buffer is not null) ArrayPool<byte>.Shared.Return(_current.Buffer);
            _current = default;
        }
        base.Dispose(disposing);
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

/// <summary>The two output pipes of a child process that was started with its output redirected, as <see cref="ChildPipeStream"/>s.</summary>
public static class ChildPipe
{
    /// <summary>Standard output of <paramref name="process"/> as bytes, read on a thread of its own. Use this instead of <c>StandardOutput.BaseStream</c> for a pipe that stays open for the life of the child.</summary>
    public static Stream Output(Process process) => new ChildPipeStream(process.StandardOutput.BaseStream);

    /// <summary>Standard error of <paramref name="process"/> as text in <paramref name="encoding"/>, read on a thread of its own.</summary>
    public static TextReader Error(Process process, Encoding encoding) => new StreamReader(new ChildPipeStream(process.StandardError.BaseStream), encoding);
}

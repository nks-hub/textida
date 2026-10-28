namespace NksHub.Textida.Remote;

/// <summary>How far one transfer is, counted in bytes the connection has taken (upload) or the local file has received (download).</summary>
public readonly record struct TransferProgress(bool Upload, string Path, long Done, long Total)
{
    /// <summary>Whole percent, 0 to 100; an empty file counts as done.</summary>
    public int Percent => Total <= 0 ? 100 : (int)Math.Clamp(Done * 100 / Total, 0, 100);
}

/// <summary>Reports a transfer to <see cref="TransferOptions.Progress"/> when its whole percent changes, so a large file does not flood the user interface.</summary>
internal sealed class ProgressReporter(Action<TransferProgress> sink, bool upload, string path, long total)
{
    private int _last = -1;

    public void At(long done)
    {
        var progress = new TransferProgress(upload, path, Math.Clamp(done, 0, Math.Max(total, 0)), total);
        if (Interlocked.Exchange(ref _last, progress.Percent) != progress.Percent) sink(progress);
    }

    /// <summary>The stream with every read and write counted against this report.</summary>
    public Stream Wrap(Stream inner) => new ProgressStream(inner, At);
}

/// <summary>Passes a stream through and tells where it stands after every read or write. It leaves <paramref name="inner"/> open: the caller owns it.</summary>
internal sealed class ProgressStream(Stream inner, Action<long> positionChanged) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellation) => inner.FlushAsync(cancellation);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);

    public override int Read(byte[] buffer, int offset, int count) => Moved(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Moved(inner.Read(buffer));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation) =>
        Moved(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellation).ConfigureAwait(false));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default) =>
        Moved(await inner.ReadAsync(buffer, cancellation).ConfigureAwait(false));

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        Moved(count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        Moved(buffer.Length);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellation)
    {
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellation).ConfigureAwait(false);
        Moved(count);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellation = default)
    {
        await inner.WriteAsync(buffer, cancellation).ConfigureAwait(false);
        Moved(buffer.Length);
    }

    private int Moved(int n)
    {
        positionChanged(inner.Position);
        return n;
    }
}

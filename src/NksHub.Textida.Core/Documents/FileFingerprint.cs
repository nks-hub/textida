using System.Security.Cryptography;

namespace NksHub.Textida.Core.Documents;

/// <summary>Size, write time and SHA-256 of a file; the hash is what makes a change with equal size and time visible.</summary>
public sealed record FileFingerprint(long Size, DateTime LastWriteUtc, string Sha256)
{
    /// <summary>Reads the file; null when it is missing or cannot be opened for reading.</summary>
    public static FileFingerprint? TryRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var time = File.GetLastWriteTimeUtc(path);
            return new(fs.Length, time, Convert.ToHexString(SHA256.HashData(fs)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Like <see cref="TryRead(string)"/>, but the pass over the file stops with <see cref="OperationCanceledException"/> when <paramref name="ct"/> is cancelled.</summary>
    internal static FileFingerprint? TryReadCancellable(string path, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var time = File.GetLastWriteTimeUtc(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 20];
            for (int n; (n = fs.Read(buf)) > 0;)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buf, 0, n);
            }
            return new(fs.Length, time, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Size and write time only; the hash is empty. Cheap at any file size.</summary>
    public static FileFingerprint? TryReadQuick(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new(info.Length, info.LastWriteTimeUtc, "") : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Like <see cref="TryRead(string)"/>, but cancellable; null also when the write time moved while the file was read, because then the hash
    /// may mix two states of the file.
    /// </summary>
    public static FileFingerprint? TryRead(string path, CancellationToken ct) => TryRead(path, ct, null);

    /// <summary>The same pass; <paramref name="blocks"/>, when given, receives the bytes in order and is sealed when the whole file was read and nothing moved meanwhile.</summary>
    internal static FileFingerprint? TryRead(string path, CancellationToken ct, OriginalBlockHashes? blocks)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            var before = File.GetLastWriteTimeUtc(path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 20];
            long total = 0;
            for (int n; (n = fs.Read(buf)) > 0;)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buf, 0, n);
                blocks?.Append(buf.AsSpan(0, n));
                total += n;
            }
            if (File.GetLastWriteTimeUtc(path) != before || (blocks is not null && total != fs.Length)) return null;
            blocks?.Seal(total);
            return new(fs.Length, before, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>False for a fingerprint taken after an in-place save, before the background hash finished.</summary>
    public bool HasHash => Sha256.Length > 0;

    /// <summary>Equal size and, when both sides know their hash, equal hash.</summary>
    public bool SameContent(FileFingerprint other) => Size == other.Size && (!HasHash || !other.HasHash || Sha256 == other.Sha256);
}

public enum ExternalChangeKind
{
    /// <summary>The file exists but its content differs from the one the document was loaded from or last saved to.</summary>
    Modified,
    /// <summary>The file is gone: deleted, renamed or unreadable.</summary>
    Missing,
}

/// <summary>A conflict between the document and its file on disk.</summary>
public sealed record ExternalChange(Guid DocumentId, long Revision, string Path, FileFingerprint Expected, FileFingerprint? Actual, ExternalChangeKind Kind);

/// <summary>Size and SHA-256 of the file a save is about to move over <see cref="Target"/>; <see cref="Revision"/> is the revision that file holds (the document may be further on).</summary>
public sealed class SavingEventArgs(string target, long size, string sha256, long revision = -1) : EventArgs
{
    public long Revision { get; } = revision;
    public string Target { get; } = target;
    public long Size { get; } = size;
    public string Sha256 { get; } = sha256;
}

/// <param name="Loss">Set when a save in another encoding was refused because characters would be lost and no replacement was confirmed.</param>
public sealed record SaveResult(bool Saved, ExternalChange? Conflict, Encodings.LossReport? Loss = null);

/// <summary>Stream wrapper that hashes what is written through it, and optionally captures its block digests.</summary>
internal sealed class HashingStream(Stream inner, OriginalBlockHashes? blocks = null) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long Written { get; private set; }
    public string Finish()
    {
        blocks?.Seal(Written);
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
        blocks?.Append(buffer);
        inner.Write(buffer);
        Written += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Flush() => inner.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

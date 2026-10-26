using System.Security.Cryptography;

namespace NksPad.Core.Documents;

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

    public bool SameContent(FileFingerprint other) => Size == other.Size && Sha256 == other.Sha256;
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

public sealed record SaveResult(bool Saved, ExternalChange? Conflict);

/// <summary>Stream wrapper that hashes what is written through it.</summary>
internal sealed class HashingStream(Stream inner) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long Written { get; private set; }
    public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
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

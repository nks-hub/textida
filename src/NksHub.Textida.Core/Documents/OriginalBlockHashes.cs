using System.Security.Cryptography;

namespace NksHub.Textida.Core.Documents;

/// <summary>Result of comparing bytes with one block of an <see cref="OriginalBlockHashes"/> baseline.</summary>
internal enum BlockCheck
{
    Match,
    Differs,
    /// <summary>The baseline is not sealed or has no digest for this block; nothing can be said about the bytes.</summary>
    BaselineIncomplete,
}

/// <summary>
/// SHA-256 digests of a file in blocks of <see cref="BlockSize"/>, taken from the bytes the document read or wrote itself, so a later
/// check can tell that a block of the file is not what the document believes it is, even when size and write time are unchanged.
/// Blocks are captured only while the baseline is built (the initial read, or the bytes of a streamed save) and <see cref="Seal"/> closes it;
/// a missing baseline is never built from bytes found on disk at save time, because a change made behind the document would be accepted.
/// Not thread safe. The check is not atomic: a writer that changes a block between the comparison and the document's own write is not excluded
/// without locking the file, which the editor does not do.
/// </summary>
internal sealed class OriginalBlockHashes
{
    public const int BlockSize = 1 << 20;

    private readonly List<(byte[]? Digest, int Length)> _blocks = [];
    private byte[]? _pending;
    private int _pendingFill;
    private long _length;

    public bool IsSealed { get; private set; }
    public long Length => _length;

    /// <summary>Records the digest of the block that starts at <paramref name="offset"/> (a multiple of <see cref="BlockSize"/>); only the last block may be shorter.</summary>
    public void CaptureBlock(long offset, ReadOnlySpan<byte> bytes)
    {
        if (IsSealed) throw new InvalidOperationException("The baseline is sealed.");
        if (offset != (long)_blocks.Count * BlockSize || bytes.Length is 0 or > BlockSize) throw new ArgumentException("Blocks are captured in order, each non-empty and at most one block long.");
        if (_blocks.Count > 0 && _blocks[^1].Length != BlockSize) throw new InvalidOperationException("A short block must be the last one.");
        _blocks.Add((SHA256.HashData(bytes), bytes.Length));
        _length += bytes.Length;
    }

    /// <summary>Appends the next bytes of the file in order; full blocks are captured as they complete.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        _pending ??= new byte[BlockSize];
        while (!bytes.IsEmpty)
        {
            int n = Math.Min(bytes.Length, BlockSize - _pendingFill);
            bytes[..n].CopyTo(_pending.AsSpan(_pendingFill));
            _pendingFill += n;
            bytes = bytes[n..];
            if (_pendingFill == BlockSize) FlushPending();
        }
    }

    private void FlushPending()
    {
        CaptureBlock((long)_blocks.Count * BlockSize, _pending.AsSpan(0, _pendingFill));
        _pendingFill = 0;
    }

    /// <summary>Closes the baseline once the whole file of <paramref name="length"/> bytes has been captured; throws when it has not.</summary>
    public void Seal(long length)
    {
        if (_pendingFill > 0) FlushPending();
        if (_length != length) throw new InvalidOperationException("The captured blocks do not cover the file.");
        _pending = null;
        IsSealed = true;
    }

    /// <summary>Compares the bytes of the block that starts at <paramref name="offset"/> with the baseline.</summary>
    public BlockCheck CompareBlock(long offset, ReadOnlySpan<byte> bytes)
    {
        long index = offset / BlockSize;
        if (!IsSealed || offset % BlockSize != 0 || index >= _blocks.Count || _blocks[(int)index].Digest is not { } digest) return BlockCheck.BaselineIncomplete;
        return _blocks[(int)index].Length == bytes.Length && SHA256.HashData(bytes).AsSpan().SequenceEqual(digest) ? BlockCheck.Match : BlockCheck.Differs;
    }

    /// <summary>Accepts the digests of blocks our own verified write changed (key: block offset); the other blocks keep their baseline.</summary>
    public void Replace(IReadOnlyDictionary<long, byte[]> digests)
    {
        if (!IsSealed) throw new InvalidOperationException("Only a sealed baseline is updated.");
        foreach (var (offset, digest) in digests)
        {
            int i = (int)(offset / BlockSize);
            _blocks[i] = (digest, _blocks[i].Length);
        }
    }
}

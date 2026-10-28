using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core.History;

/// <summary>
/// A patch that turns one file into another: ranges copied from the old file and bytes that are new. The history keeps a revision of a large file as such
/// a patch against a whole copy of an earlier one, so a few edits cost a few kilobytes instead of the size of the file. Format: <c>TXD1</c>, the length of the
/// new file, then operations until it is complete: <c>1</c> offset length (copy), <c>2</c> length bytes (add); numbers are variable-length (seven bits per byte).
/// Both files are read at positions, never whole: memory stays at a few buffers and an index with one entry per block of the old file.
/// </summary>
public static class BinaryDelta
{
    private static readonly byte[] Magic = "TXD1"u8.ToArray();
    private const byte OpCopy = 1, OpAdd = 2;
    private const int Chunk = 1 << 20;
    private const ulong Prime = 0x100000001B3UL;

    /// <summary>
    /// Bytes per block of the old file that the search for matches starts from: 4 KiB, more for a larger file so that the index stays at about 260 000 entries.
    /// A match is extended past its block as far as the files agree, so the block size only bounds the smallest piece found again after an edit.
    /// </summary>
    public static int BlockFor(long sourceLength)
    {
        int block = 4096;
        while (block < Chunk && sourceLength / block > 1 << 18) block <<= 1;
        return block;
    }

    /// <summary>
    /// Writes to <paramref name="patch"/> what turns the first <paramref name="sourceLength"/> bytes of <paramref name="source"/> into the first <paramref name="targetLength"/>
    /// bytes of <paramref name="target"/>. Returns the length of the patch, or -1 when it would be longer than <paramref name="maxPatchBytes"/> (the files are too different to be worth a patch);
    /// the stream then holds an unfinished patch.
    /// </summary>
    public static long Create(SafeFileHandle source, long sourceLength, SafeFileHandle target, long targetLength, Stream patch, long maxPatchBytes, CancellationToken ct = default)
    {
        var writer = new PatchWriter(patch, maxPatchBytes);
        writer.Header(targetLength);
        new Matcher(source, sourceLength, target, targetLength, writer, ct).Run();
        return writer.Length > maxPatchBytes ? -1 : writer.Length;
    }

    /// <summary>
    /// Writes the file a patch describes to <paramref name="output"/>, or its first <paramref name="limit"/> bytes. Throws <see cref="InvalidDataException"/> for a patch that is
    /// damaged or does not fit <paramref name="source"/>.
    /// </summary>
    public static void Apply(SafeFileHandle source, long sourceLength, Stream patch, Stream output, long limit = long.MaxValue)
    {
        Span<byte> magic = stackalloc byte[4];
        if (patch.Read(magic) != 4 || !magic.SequenceEqual(Magic)) throw new InvalidDataException("This is not a patch of the history.");
        long targetLength = ReadVarint(patch);
        long end = Math.Min(targetLength, limit);
        long written = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(Chunk);
        try
        {
            while (written < end)
            {
                int op = patch.ReadByte();
                if (op == OpCopy)
                {
                    long from = ReadVarint(patch), length = ReadVarint(patch);
                    if (from < 0 || length < 0 || from + length > sourceLength) throw new InvalidDataException("The patch copies from beyond the end of the file it was made against.");
                    for (long done = 0, take = Math.Min(length, end - written); done < take;)
                    {
                        int n = (int)Math.Min(buffer.Length, take - done);
                        if (ReadAt(source, buffer.AsSpan(0, n), from + done) != n) throw new InvalidDataException("The file the patch was made against is shorter than it was.");
                        output.Write(buffer, 0, n);
                        done += n;
                        written += n;
                    }
                }
                else if (op == OpAdd)
                {
                    long length = ReadVarint(patch);
                    for (long done = 0, take = Math.Min(length, end - written); done < take;)
                    {
                        int n = (int)Math.Min(buffer.Length, take - done);
                        patch.ReadExactly(buffer, 0, n);
                        output.Write(buffer, 0, n);
                        done += n;
                        written += n;
                    }
                }
                else throw new InvalidDataException(op < 0 ? "The patch ends before the file it makes." : "The patch is damaged.");
            }
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The patch ends before the file it makes.", e);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static long ReadVarint(Stream stream)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            int b = stream.ReadByte();
            if (b < 0) throw new InvalidDataException("The patch ends before the file it makes.");
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return (long)value;
        }
        throw new InvalidDataException("The patch is damaged.");
    }

    /// <summary>Reads at a position until the buffer is full or the file ends; returns the bytes read.</summary>
    private static int ReadAt(SafeFileHandle file, Span<byte> buffer, long position)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = RandomAccess.Read(file, buffer[done..], position + done);
            if (n <= 0) break;
            done += n;
        }
        return done;
    }

    private sealed class PatchWriter(Stream stream, long max)
    {
        public long Length { get; private set; }
        public bool Exceeded => Length > max;

        private void Raw(ReadOnlySpan<byte> bytes)
        {
            stream.Write(bytes);
            Length += bytes.Length;
        }

        private void Varint(long value)
        {
            Span<byte> b = stackalloc byte[10];
            int n = 0;
            ulong v = (ulong)value;
            while (v >= 0x80)
            {
                b[n++] = (byte)(v | 0x80);
                v >>= 7;
            }
            b[n++] = (byte)v;
            Raw(b[..n]);
        }

        public void Header(long targetLength)
        {
            Raw(Magic);
            Varint(targetLength);
        }

        public void Copy(long from, long length)
        {
            Raw([OpCopy]);
            Varint(from);
            Varint(length);
        }

        public void Add(ReadOnlySpan<byte> bytes)
        {
            Raw([OpAdd]);
            Varint(bytes.Length);
            Raw(bytes);
        }
    }

    /// <summary>Walks the new file with a rolling hash over a window of one block and looks each position up in the index of the old file's blocks.</summary>
    private sealed class Matcher(SafeFileHandle source, long sourceLength, SafeFileHandle target, long targetLength, PatchWriter writer, CancellationToken ct)
    {
        private readonly int _block = BlockFor(sourceLength);
        private readonly byte[] _window = new byte[2 * Chunk];
        private long _windowStart, _windowLength;
        private readonly byte[] _blockBuffer = new byte[Chunk];
        private readonly byte[] _left = new byte[Chunk], _right = new byte[Chunk];
        private readonly Dictionary<ulong, int> _index = [];
        private ulong[] _filter = [];
        private int _filterShift;

        private static ulong Hash(ReadOnlySpan<byte> bytes)
        {
            ulong h = 0;
            foreach (byte b in bytes) h = h * Prime + b;
            return h;
        }

        private bool MaybeKnown(ulong hash)
        {
            ulong slot = (hash * 0x9E3779B97F4A7C15UL) >> _filterShift;
            return (_filter[slot >> 6] & (1UL << (int)(slot & 63))) != 0;
        }

        private void BuildIndex()
        {
            long blocks = sourceLength / _block;
            int bits = 64;
            while (bits < blocks * 16) bits <<= 1;
            _filter = new ulong[bits / 64];
            _filterShift = 64 - System.Numerics.BitOperations.Log2((uint)bits);
            int perChunk = Chunk / _block;
            var buffer = _blockBuffer;
            for (long number = 0; number < blocks;)
            {
                ct.ThrowIfCancellationRequested();
                int count = (int)Math.Min(perChunk, blocks - number);
                if (ReadAt(source, buffer.AsSpan(0, count * _block), number * _block) != count * _block) throw new InvalidDataException("The file the patch is made against is shorter than it was.");
                for (int k = 0; k < count; k++, number++)
                {
                    ulong h = Hash(buffer.AsSpan(k * _block, _block));
                    _index.TryAdd(h, (int)number);
                    ulong slot = (h * 0x9E3779B97F4A7C15UL) >> _filterShift;
                    _filter[slot >> 6] |= 1UL << (int)(slot & 63);
                }
            }
        }

        /// <summary>Makes sure the window holds the bytes from <paramref name="at"/> on, as far as the file goes, up to <paramref name="need"/> of them.</summary>
        private void Ensure(long at, int need)
        {
            long wanted = Math.Min(at + need, targetLength);
            if (at >= _windowStart && wanted <= _windowStart + _windowLength) return;
            _windowStart = at;
            _windowLength = ReadAt(target, _window.AsSpan(0, (int)Math.Min(_window.Length, targetLength - at)), at);
        }

        private void Flush(long from, long to)
        {
            for (long at = from; at < to;)
            {
                int n = (int)Math.Min(Chunk, to - at);
                if (ReadAt(target, _left.AsSpan(0, n), at) != n) throw new InvalidDataException("The file shrank while the revision was taken.");
                writer.Add(_left.AsSpan(0, n));
                at += n;
            }
        }

        /// <summary>Bytes after the matched blocks in which the two files agree.</summary>
        private long Extend(long sourceAt, long targetAt)
        {
            long total = 0;
            while (sourceAt < sourceLength && targetAt < targetLength)
            {
                ct.ThrowIfCancellationRequested();
                int n = (int)Math.Min(Math.Min(Chunk, sourceLength - sourceAt), targetLength - targetAt);
                int a = ReadAt(source, _left.AsSpan(0, n), sourceAt), b = ReadAt(target, _right.AsSpan(0, n), targetAt);
                int m = Math.Min(a, b);
                if (m <= 0) break;
                int same = _left.AsSpan(0, m).CommonPrefixLength(_right.AsSpan(0, m));
                total += same;
                sourceAt += same;
                targetAt += same;
                if (same < m) break;
            }
            return total;
        }

        public void Run()
        {
            int block = _block;
            if (sourceLength >= block) BuildIndex();
            ulong top = 1;
            for (int i = 1; i < block; i++) top *= Prime;
            long at = 0, pending = 0;
            ulong h = 0;
            bool hashed = false;
            while (at + block <= targetLength)
            {
                ct.ThrowIfCancellationRequested();
                if (writer.Exceeded) return;
                Ensure(at, block + 1);
                var here = _window.AsSpan((int)(at - _windowStart), block);
                if (!hashed)
                {
                    h = Hash(here);
                    hashed = true;
                }
                if (_index.Count > 0 && MaybeKnown(h) && _index.TryGetValue(h, out int number) && SameBlock(number, here))
                {
                    Flush(pending, at);
                    long from = (long)number * block;
                    long length = block + Extend(from + block, at + block);
                    writer.Copy(from, length);
                    at += length;
                    pending = at;
                    hashed = false;
                    continue;
                }
                if (at + block >= targetLength) break;
                h = (h - _window[(int)(at - _windowStart)] * top) * Prime + _window[(int)(at - _windowStart) + block];
                at++;
                if (at - pending >= Chunk)
                {
                    Flush(pending, at);
                    pending = at;
                }
            }
            Flush(pending, targetLength);
        }

        private bool SameBlock(int number, ReadOnlySpan<byte> here)
        {
            var buffer = _blockBuffer.AsSpan(0, _block);
            return ReadAt(source, buffer, (long)number * _block) == _block && buffer.SequenceEqual(here);
        }
    }
}

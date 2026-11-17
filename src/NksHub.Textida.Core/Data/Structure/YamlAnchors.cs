using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Data.Structure;

internal sealed class YamlAnchors : IDisposable
{
    private const int BucketCount = 65536;
    private const int RecordSize = 40;
    private readonly long[] heads = new long[BucketCount];
    private readonly byte[] left = new byte[64 * 1024], right = new byte[64 * 1024];
    private readonly IByteSnapshot source;
    private readonly CancellationToken ct;
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly string directory;
    private readonly SafeFileHandle file;
    private long records;

    internal YamlAnchors(IByteSnapshot source, CancellationToken ct)
    {
        this.source = source;
        this.ct = ct;
        Array.Fill(heads, -1L);
        directory = Directory.CreateTempSubdirectory("textida-yaml-anchors-").FullName;
        try
        {
            file = File.OpenHandle(Path.Combine(directory, "anchors.bin"), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, FileOptions.RandomAccess | FileOptions.DeleteOnClose);
        }
        catch
        {
            hash.Dispose();
            Directory.Delete(directory);
            throw;
        }
    }

    internal long RetainedBytes => heads.LongLength * sizeof(long) + left.LongLength + right.LongLength;

    internal void Define(long start, long end)
    {
        var (a, b) = Hash(start, end);
        int bucket = (int)(a & (BucketCount - 1));
        Span<byte> record = stackalloc byte[RecordSize];
        BinaryPrimitives.WriteInt64LittleEndian(record, heads[bucket]);
        BinaryPrimitives.WriteUInt64LittleEndian(record[8..], a);
        BinaryPrimitives.WriteUInt64LittleEndian(record[16..], b);
        BinaryPrimitives.WriteInt64LittleEndian(record[24..], start);
        BinaryPrimitives.WriteInt64LittleEndian(record[32..], end);
        ct.ThrowIfCancellationRequested();
        long offset = checked(records * RecordSize);
        RandomAccess.Write(file, record, offset);
        heads[bucket] = offset;
        records++;
    }

    internal bool Contains(long start, long end)
    {
        var (a, b) = Hash(start, end);
        long at = heads[(int)(a & (BucketCount - 1))];
        Span<byte> record = stackalloc byte[RecordSize];
        while (at >= 0)
        {
            ct.ThrowIfCancellationRequested();
            int used = 0;
            while (used < record.Length)
            {
                int count = RandomAccess.Read(file, record[used..], at + used);
                if (count == 0) throw new EndOfStreamException("The YAML anchor index is incomplete.");
                used += count;
            }
            if (BinaryPrimitives.ReadUInt64LittleEndian(record[8..]) == a
                && BinaryPrimitives.ReadUInt64LittleEndian(record[16..]) == b
                && Equal(start, end, BinaryPrimitives.ReadInt64LittleEndian(record[24..]), BinaryPrimitives.ReadInt64LittleEndian(record[32..])))
                return true;
            at = BinaryPrimitives.ReadInt64LittleEndian(record);
        }
        return false;
    }

    private (ulong, ulong) Hash(long start, long end)
    {
        while (start < end)
        {
            ct.ThrowIfCancellationRequested();
            int count = (int)Math.Min(left.Length, end - start);
            Read(start, left.AsSpan(0, count));
            hash.AppendData(left, 0, count);
            start += count;
        }
        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int size) || size != digest.Length)
            throw new CryptographicException("The YAML anchor name could not be hashed.");
        return (BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]));
    }

    private bool Equal(long firstStart, long firstEnd, long secondStart, long secondEnd)
    {
        if (firstEnd - firstStart != secondEnd - secondStart) return false;
        while (firstStart < firstEnd)
        {
            ct.ThrowIfCancellationRequested();
            int count = (int)Math.Min(left.Length, firstEnd - firstStart);
            Read(firstStart, left.AsSpan(0, count));
            Read(secondStart, right.AsSpan(0, count));
            if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count))) return false;
            firstStart += count;
            secondStart += count;
        }
        return true;
    }

    private void Read(long at, Span<byte> target)
    {
        while (!target.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();
            int count = source.Read(at, target);
            if (count == 0) throw new EndOfStreamException("The YAML anchor source span is unavailable.");
            at += count;
            target = target[count..];
        }
    }

    public void Dispose()
    {
        hash.Dispose();
        file.Dispose();
        Directory.Delete(directory);
    }
}

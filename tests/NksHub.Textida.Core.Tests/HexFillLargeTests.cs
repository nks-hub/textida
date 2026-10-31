using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Tests;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.10: a fill of 5 GiB through the repeated pattern piece, and hashes of a 1 GiB selection against computations that share no code with them.</summary>
public sealed class HexFillLargeTests : IDisposable
{
    private const long FiveGiB = 5L << 30;
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-fill-").FullName;
    private readonly List<IDisposable> _open = [];

    public void Dispose()
    {
        foreach (var o in _open) o.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private Doc Open(string path)
    {
        var d = Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        _open.Add(d);
        return d;
    }

    /// <summary>A sparse file of <paramref name="length"/> bytes (zeros) with a few marker bytes written into it.</summary>
    private string SparseFile(string name, long length, params (long At, byte Value)[] markers)
    {
        string path = Path.Combine(_dir, name);
        File.Create(path).Dispose();
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("fsutil", $"sparse setflag \"{path}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.SetLength(length);
        foreach (var (at, value) in markers)
        {
            fs.Position = at;
            fs.WriteByte(value);
        }
        return path;
    }

    /// <summary>Checks a file against <c>pattern[i % pattern.Length]</c> with plain stream reads, and returns the SHA-256 of what it read.</summary>
    private static string VerifyPattern(string path, long length, byte[] pattern)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        Assert.Equal(length, fs.Length);
        var buffer = new byte[1 << 20];
        var expected = new byte[1 << 20];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long at = 0; at < length; at += buffer.Length)
        {
            int n = (int)Math.Min(buffer.Length, length - at);
            fs.ReadExactly(buffer, 0, n);
            for (int i = 0; i < n; i++) expected[i] = pattern[(at + i) % pattern.Length];
            if (!buffer.AsSpan(0, n).SequenceEqual(expected.AsSpan(0, n))) Assert.Fail($"The file differs from the pattern in the block at {at}.");
            sha.AppendData(buffer, 0, n);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    /// <summary>The save writes the whole fill, so this is the one test of the class that writes real bytes: 2 GiB and 4 MiB (past the offsets of a 32 bit signed number), 5 GiB with <c>TEXTIDA_HUGE=1</c>.</summary>
    [Fact]
    [Trait("Category", "Large")]
    public void Filling_a_file_past_2_GiB_takes_no_memory_and_the_saved_file_is_byte_exact_by_an_independent_computation()
    {
        long size = HugeText.SizePast(2L << 30, FiveGiB);
        byte[] pattern = [0xDE, 0xAD, 0xBE, 0xEF, 0x01];
        string path = SparseFile("fill.bin", size, (0, 0x11), (size / 2, 0x22), (size - 1, 0x33));
        var d = Open(path);
        d.Replace(new ByteRange(0, 1), new byte[] { 0x44 }, new() { RawBytes = true }); // an ordinary edit before the fill: it goes below the fill in the undo stack
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var result = d.Fill(new ByteRange(0, size), pattern, size, new() { RawBytes = true });
        long grown = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(result.Applied);
        Assert.True(grown <= 16 << 20, $"The fill allocated {grown} bytes.");
        Assert.Equal(size, d.ByteLength);

        var probe = new byte[7];
        d.Read(size / 2 - 2, probe);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => pattern[(size / 2 - 2 + i) % 5]).ToArray(), probe);

        var saved = d.SaveAsync().GetAwaiter().GetResult();
        Assert.True(saved.Saved);
        string independent = VerifyPattern(path, size, pattern);
        Assert.Equal(independent, d.Fingerprint!.Sha256);
        Assert.False(d.IsModified);
        // the save wrote over the mapped file: the undo of the fill, and of what came before it, is released
        Assert.False(d.CanUndo);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Undo_and_redo_of_a_5_GiB_fill_restore_the_bytes_it_replaced_and_an_insert_grows_the_document()
    {
        string path = SparseFile("undo.bin", FiveGiB, (0, 0x11), (FiveGiB / 2, 0x22), (FiveGiB - 1, 0x33));
        var d = Open(path);
        Assert.True(d.Fill(new ByteRange(0, FiveGiB), new byte[] { 0 }, FiveGiB, new() { RawBytes = true }).Applied);
        Assert.Equal(1, d.UndoDepth);
        Assert.Equal(FiveGiB, d.ByteLength);
        var one = new byte[1];
        foreach (long at in new[] { 0L, FiveGiB / 2, FiveGiB - 1 }) { d.Read(at, one); Assert.Equal(0, one[0]); }
        Assert.NotNull(d.Undo());
        foreach (var (at, value) in new[] { (0L, (byte)0x11), (FiveGiB / 2, (byte)0x22), (FiveGiB - 1, (byte)0x33) }) { d.Read(at, one); Assert.Equal(value, one[0]); }
        Assert.False(d.IsModified);
        Assert.NotNull(d.Redo());
        d.Read(FiveGiB / 2, one);
        Assert.Equal(0, one[0]);
        d.Undo();

        Assert.True(d.Fill(new ByteRange(1000, 1000), new byte[] { 1, 2, 3 }, FiveGiB, new() { RawBytes = true }).Applied);
        Assert.Equal(2 * FiveGiB, d.ByteLength);
        var window = new byte[5];
        d.Read(FiveGiB + 998, window);
        // the run [1000, 1000 + 5 GiB) ends two bytes into the window; the original zeros follow
        Assert.Equal(new byte[] { (byte)((FiveGiB - 2) % 3 + 1), (byte)((FiveGiB - 1) % 3 + 1), 0, 0, 0 }, window);
        d.Undo();
        Assert.Equal(FiveGiB, d.ByteLength);
        d.Read(FiveGiB / 2, one);
        Assert.Equal(0x22, one[0]);
    }

    [Fact]
    [Trait("Category", "Large")]
    public void The_journal_records_a_large_fill_as_its_pattern_and_recovery_replays_it_with_undo_and_redo()
    {
        using var dir = new JournalTempDir();
        using var session = JournalSession.Start(dir.Combine("journal"), JournalFormatTests.ReferenceResolver, new JournalOptions { Fsync = FsyncPolicy.Manual });
        const long fill = 300L << 20; // over the size of a patch, so the fill is kept as a pattern
        byte[] pattern = [1, 2, 3, 4, 5, 6, 7];
        using var doc = Doc.FromBytes(Enumerable.Range(0, 5000).Select(i => (byte)(i * 7)).ToArray(), new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        session.Track(doc);
        Assert.True(doc.Fill(new ByteRange(1000, 2000), pattern, fill, new() { RawBytes = true }).Applied);
        doc.Replace(new ByteRange(0, 1), new byte[] { 0xEE }, new() { RawBytes = true });
        doc.Undo();
        doc.Undo();
        doc.Redo();
        doc.Redo();
        session.Flush();
        Assert.True(new FileInfo(session.JournalPath!).Length < 1 << 20, "The journal holds the pattern, not the bytes of the fill.");

        var recovery = JournalRecovery.Recover(session.JournalPath!, JournalFormatTests.ReferenceResolver);
        var back = Assert.Single(recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.Equal(doc.ByteLength, restored.ByteLength);
        var sha = Checksums.Find("sha256")!;
        var all = new ByteRange(0, doc.ByteLength);
        Assert.Equal(Checksums.Compute(sha, doc.GetSnapshot(), all), Checksums.Compute(sha, restored.GetSnapshot(), all));
        // the recovered document undoes the fill like the original
        Assert.NotNull(restored.Undo());
        Assert.NotNull(restored.Undo());
        Assert.Equal(5000, restored.ByteLength);
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Filling_5_GiB_with_zeros_takes_under_a_second_and_16_MiB()
    {
        string path = SparseFile("perf.bin", FiveGiB, (FiveGiB - 1, 0x33));
        var d = Open(path);
        GC.Collect();
        long before = Process.GetCurrentProcess().PrivateMemorySize64;
        var time = Stopwatch.StartNew();
        var result = d.Fill(new ByteRange(0, FiveGiB), new byte[] { 0 }, FiveGiB, new() { RawBytes = true });
        time.Stop();
        long grown = Process.GetCurrentProcess().PrivateMemorySize64 - before;
        Assert.True(result.Applied);
        Assert.True(time.ElapsedMilliseconds <= 1000, $"The fill took {time.ElapsedMilliseconds} ms.");
        Assert.True(grown <= 16 << 20, $"The process grew by {grown} bytes.");
    }

    [Fact]
    [Trait("Category", "Large")]
    public void SHA_256_and_CRC_32_of_a_1_GiB_selection_equal_computations_that_read_the_file_themselves()
    {
        const long size = (1L << 30) + (3 << 20) + 17, start = 1_234_567, length = 1L << 30;
        string path = Path.Combine(_dir, "random.bin");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var random = new Random(42);
            var block = new byte[1 << 20];
            for (long written = 0; written < size; written += block.Length)
            {
                random.NextBytes(block);
                fs.Write(block, 0, (int)Math.Min(block.Length, size - written));
            }
        }
        var d = Open(path);
        var snapshot = d.GetSnapshot();
        var range = new ByteRange(start, start + length);

        string sha = Checksums.Compute(Checksums.Find("sha256")!, snapshot, range);
        string crc = Checksums.Compute(Checksums.Find("crc-32")!, snapshot, range);

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var gzipTail = new TailStream();
        using (var gzip = new GZipStream(gzipTail, CompressionLevel.NoCompression, leaveOpen: true))
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
        {
            file.Position = start;
            var buffer = new byte[1 << 20];
            for (long left = length; left > 0;)
            {
                int n = file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                Assert.True(n > 0);
                sha256.AppendData(buffer, 0, n);
                gzip.Write(buffer, 0, n);
                left -= n;
            }
        }
        Assert.Equal(Convert.ToHexStringLower(sha256.GetHashAndReset()), sha);
        // the gzip trailer holds the CRC-32 of the data, little-endian, then its length modulo 2^32
        var tail = gzipTail.Last8;
        Assert.Equal(Convert.ToHexStringLower([tail[3], tail[2], tail[1], tail[0]]), crc);
    }

    /// <summary>Counts what is written to it and keeps the last 8 bytes.</summary>
    private sealed class TailStream : Stream
    {
        public byte[] Last8 { get; } = new byte[8];
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length >= 8) buffer[^8..].CopyTo(Last8);
            else
            {
                Last8.AsSpan(buffer.Length).CopyTo(Last8);
                buffer.CopyTo(Last8.AsSpan(8 - buffer.Length));
            }
        }
    }
}

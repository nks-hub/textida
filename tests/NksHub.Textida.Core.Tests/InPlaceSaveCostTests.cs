using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

internal sealed class CountingStream(Stream inner, Action<int> counted) : Stream
{
    public override int Read(Span<byte> buffer) { int n = inner.Read(buffer); counted(n); return n; }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}

/// <summary>An in-place save costs what the changed blocks cost, not what the file costs; the checks that remain still catch a foreign change and a bad write.</summary>
public class InPlaceSaveCostTests
{
    private const int Block = OriginalBlockHashes.BlockSize;
    private static Doc Open(string path) => Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
    private static EditOptions Raw => new() { RawBytes = true };

    /// <summary>Waits for the background hash that follows an in-place save.</summary>
    internal static void AwaitHash(Doc d)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!d.Fingerprint!.HasHash && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.True(d.Fingerprint!.HasHash, "The background hash did not finish.");
    }

    private static long CountReads(Doc d, Func<SaveResult> save)
    {
        long read = 0;
        d.FileReader = p => new CountingStream(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), n => Interlocked.Add(ref read, n));
        Assert.True(save().Saved);
        return read;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_in_place_save_reads_the_changed_blocks_and_not_the_file(bool async)
    {
        byte[] original = new byte[32 << 20];
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        long at = (17L << 20) + 5;
        Assert.True(d.Replace(new(at, at + 4), new byte[] { 1, 2, 3, 4 }, Raw).Applied);
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);

        long read = CountReads(d, () => async ? d.SaveAsync().GetAwaiter().GetResult() : d.Save());
        // one block to check before the write and one to read back after it
        Assert.True(read <= 2 * Block, $"The save read {read} bytes of a {original.Length} byte file.");
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(file.Path).AsSpan((int)at, 4).ToArray());
        AwaitHash(d);
        Assert.Equal(FileFingerprint.TryRead(file.Path)!.Sha256, d.Fingerprint!.Sha256);
        Assert.Null(d.CheckExternalChange());
    }

    [Fact]
    public void A_patch_spanning_two_blocks_reads_those_two_blocks()
    {
        using var file = new TempFile(new byte[8 << 20]);
        using var d = Open(file.Path);
        Assert.True(d.Replace(new(3L * Block - 2, 3L * Block + 2), new byte[] { 1, 2, 3, 4 }, Raw).Applied);
        long read = CountReads(d, () => d.Save());
        Assert.True(read <= 4 * Block, $"The save read {read} bytes.");
    }

    [Theory]
    [InlineData("size")]
    [InlineData("time")]
    public void A_foreign_change_of_size_or_write_time_is_a_conflict_and_nothing_is_written(string what)
    {
        byte[] original = Gen.RandomBytes(new Random(11), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        if (what == "size")
            using (var fs = new FileStream(file.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) fs.WriteByte(1);
        else
        {
            using (var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { fs.Position = 100; fs.WriteByte((byte)(original[100] ^ 0xFF)); }
            File.SetLastWriteTimeUtc(file.Path, File.GetLastWriteTimeUtc(file.Path).AddMinutes(5));
        }
        long before = new FileInfo(file.Path).Length;
        var result = d.Save();
        Assert.False(result.Saved);
        Assert.Equal(ExternalChangeKind.Modified, result.Conflict!.Kind);
        Assert.Equal(original[10], File.ReadAllBytes(file.Path)[10]);
        Assert.Equal(before, new FileInfo(file.Path).Length);
        Assert.True(d.IsModified);
    }

    [Fact]
    public void A_touch_that_only_moves_the_write_time_is_checked_by_content_and_is_no_conflict()
    {
        byte[] original = Gen.RandomBytes(new Random(12), 2 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        File.SetLastWriteTimeUtc(file.Path, File.GetLastWriteTimeUtc(file.Path).AddMinutes(5));
        Assert.True(d.Save().Saved);
        Assert.Equal(0xEE, File.ReadAllBytes(file.Path)[10]);
    }

    [Fact]
    public void The_file_identity_is_the_same_for_a_file_and_differs_between_files_where_the_os_gives_one()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var a = new TempFile([1]);
        using var b = new TempFile([1]);
        Assert.NotNull(FileIdentity.TryGet(a.Path));
        Assert.Equal(FileIdentity.TryGet(a.Path), FileIdentity.TryGet(a.Path));
        Assert.NotEqual(FileIdentity.TryGet(a.Path), FileIdentity.TryGet(b.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_write_that_is_not_what_the_revision_says_is_found_by_the_read_back_and_not_recorded(bool overwriteChangedFile)
    {
        byte[] original = Gen.RandomBytes(new Random(13), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        if (overwriteChangedFile) OriginalBlockHashesTests.Foreign(file.Path, 50, (byte)(original[50] ^ 0xFF)); // no digests exist then: the written range itself is compared
        Assert.True(d.Replace(new(10, 14), new byte[] { 1, 2, 3, 4 }, Raw).Applied);
        d.InPlaceHooks = new PatchHooks
        {
            AfterStep = step =>
            {
                if (step == "commit") OriginalBlockHashesTests.Foreign(file.Path, 12, 0x77); // inside the written range, write time put back
            },
        };
        var result = d.Save(overwriteExternalChanges: overwriteChangedFile);
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        Assert.True(d.IsModified);
    }

    [Fact]
    public void Our_own_save_is_not_flagged_before_or_after_the_background_hash_arrives()
    {
        using var file = new TempFile(Gen.RandomBytes(new Random(14), 2 << 20));
        using var d = Open(file.Path);
        int events = 0;
        d.ExternalChangeDetected += (_, _) => events++;
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        Assert.True(d.Save().Saved);
        Assert.Null(d.CheckExternalChange());
        AwaitHash(d);
        Assert.Null(d.CheckExternalChange());
        Assert.Equal(0, events);
        // and a second in-place save right away cancels the first hash and ends with the right one
        Assert.True(d.Replace(new(11, 12), new byte[] { 0xDD }, Raw).Applied);
        Assert.True(d.Save().Saved);
        AwaitHash(d);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.Path))), d.Fingerprint!.Sha256);
    }
}

[Trait("Category", "Large")]
public class InPlaceSaveCostLargeTests
{
    [Fact]
    public void A_4_byte_save_in_a_4_5_GiB_file_reads_a_few_blocks()
    {
        string path = BigFileBackendFactory.NewPath();
        const long at = 4_400_000_000;
        using (var fs = BigFileBackendFactory.CreateSparse(path)) { fs.Position = at; fs.Write(new byte[] { 1, 2, 3, 4 }); }
        using var d = Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        Assert.True(d.Replace(new(at, at + 4), new byte[] { 5, 6, 7, 8 }, new() { RawBytes = true }).Applied);
        long read = 0;
        d.FileReader = p => new CountingStream(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), n => Interlocked.Add(ref read, n));
        Assert.True(d.Save().Saved);
        Assert.True(read <= 2 * OriginalBlockHashes.BlockSize, $"The save read {read} bytes.");
        InPlaceSaveCostTests.AwaitHash(d);
    }
}

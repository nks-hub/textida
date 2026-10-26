using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class OriginalBlockHashesTests
{
    private const int Block = OriginalBlockHashes.BlockSize;
    private static Doc Open(string path) => Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
    private static EditOptions Raw => new() { RawBytes = true };

    /// <summary>Changes one byte behind the document and puts the write time back, so only the content shows it.</summary>
    internal static void Foreign(string path, long at, byte value)
    {
        var time = File.GetLastWriteTimeUtc(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Position = at;
            fs.WriteByte(value);
        }
        File.SetLastWriteTimeUtc(path, time);
    }

    [Fact]
    public void A_sealed_baseline_tells_a_match_from_a_difference_and_an_open_one_knows_nothing()
    {
        var bytes = Gen.RandomBytes(new Random(1), Block + 100);
        var open = new OriginalBlockHashes();
        open.CaptureBlock(0, bytes.AsSpan(0, Block));
        Assert.Equal(BlockCheck.BaselineIncomplete, open.CompareBlock(0, bytes.AsSpan(0, Block)));

        var sealedHashes = new OriginalBlockHashes();
        sealedHashes.Append(bytes.AsSpan(0, 12345));
        sealedHashes.Append(bytes.AsSpan(12345));
        sealedHashes.Seal(bytes.Length);
        Assert.Equal(BlockCheck.Match, sealedHashes.CompareBlock(0, bytes.AsSpan(0, Block)));
        Assert.Equal(BlockCheck.Match, sealedHashes.CompareBlock(Block, bytes.AsSpan(Block)));
        var changed = bytes.AsSpan(Block).ToArray();
        changed[5] ^= 1;
        Assert.Equal(BlockCheck.Differs, sealedHashes.CompareBlock(Block, changed));
        Assert.Equal(BlockCheck.Differs, sealedHashes.CompareBlock(Block, changed.AsSpan(0, 50)));
        Assert.Equal(BlockCheck.BaselineIncomplete, sealedHashes.CompareBlock(2L * Block, changed));
        Assert.Throws<InvalidOperationException>(() => new OriginalBlockHashes().Seal(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_foreign_change_in_the_same_block_with_the_write_time_restored_is_a_conflict_and_nothing_is_written(bool async)
    {
        byte[] original = Gen.RandomBytes(new Random(2), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        // after the whole-file check, before our write: the metadata and the whole-file check were already past
        d.BeforeSaveCommit = () => Foreign(file.Path, 20, (byte)(original[20] ^ 0xFF));
        var result = async ? await d.SaveAsync() : d.Save();
        Assert.False(result.Saved);
        Assert.Equal(ExternalChangeKind.Modified, result.Conflict!.Kind);
        var disk = File.ReadAllBytes(file.Path);
        Assert.Equal(original[10], disk[10]);
        Assert.Equal((byte)(original[20] ^ 0xFF), disk[20]);
        Assert.True(d.IsModified);
    }

    [Fact]
    public void A_foreign_writer_that_still_has_the_file_open_is_found_by_content()
    {
        byte[] original = Gen.RandomBytes(new Random(3), 2 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(10, 11), new byte[] { 0xEE }, Raw);
        using var writer = new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        d.BeforeSaveCommit = () =>
        {
            writer.Position = 30;
            writer.WriteByte((byte)(original[30] ^ 0xFF));
            writer.Flush(true); // the write time of an open handle may not move yet
        };
        var result = d.Save();
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        writer.Dispose();
        Assert.Equal(original[10], File.ReadAllBytes(file.Path)[10]);
    }

    [Fact]
    public void Our_own_patch_moves_the_baseline_so_the_next_patch_in_the_block_passes_and_an_old_snapshot_keeps_its_bytes()
    {
        byte[] original = Gen.RandomBytes(new Random(4), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        var old = d.GetSnapshot();
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        Assert.True(d.Save().Saved);
        Assert.True(d.Replace(new(Block + 7, Block + 9), new byte[] { 1, 2 }, Raw).Applied);
        Assert.True(d.Save().Saved);
        Assert.True(d.Replace(new(11, 12), new byte[] { 0xDD }, Raw).Applied); // block 0 again: needs the digest derived from our first patch
        Assert.True(d.Save().Saved);
        var expected = (byte[])original.Clone();
        expected[10] = 0xEE;
        expected[11] = 0xDD;
        expected[Block + 7] = 1;
        expected[Block + 8] = 2;
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
        Assert.Equal(original, old.ReadBytes(new(0, old.ByteLength)));

        // the accepted digests describe our bytes and no others: a foreign byte in the block patched earlier is still seen
        d.Replace(new(12, 13), new byte[] { 0xCC }, Raw);
        d.BeforeSaveCommit = () => Foreign(file.Path, 40, (byte)(expected[40] ^ 0xFF));
        var result = d.Save();
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        Assert.Equal(expected[12], File.ReadAllBytes(file.Path)[12]);
    }

    [Fact]
    public void A_baseline_given_up_by_an_overwrite_makes_the_next_save_stream_and_that_one_builds_it_from_the_bytes_it_writes()
    {
        byte[] original = Gen.RandomBytes(new Random(5), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        Foreign(file.Path, 50, (byte)(original[50] ^ 0xFF));
        d.Replace(new(10, 11), new byte[] { 0xEE }, Raw);
        Assert.True(d.Save(overwriteExternalChanges: true).Saved);
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode); // the block was not what the baseline says, so there is nothing to vouch for the others

        d.Replace(new(11, 12), new byte[] { 0xDD }, Raw);
        Assert.True(d.Save().Saved);
        d.Replace(new(12, 13), new byte[] { 0xCC }, Raw);
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
        d.BeforeSaveCommit = () => Foreign(file.Path, 60, (byte)(original[60] ^ 0xFF));
        Assert.False(d.Save().Saved);
    }

    [Fact]
    public void A_streamed_save_takes_the_baseline_from_the_bytes_it_wrote()
    {
        byte[] original = Gen.RandomBytes(new Random(6), 3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Insert(100, new byte[] { 9 });
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);
        Assert.True(d.Save().Saved);
        var written = File.ReadAllBytes(file.Path);

        Assert.True(d.Replace(new(Block + 1, Block + 2), new byte[] { 7 }, Raw).Applied);
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
        d.BeforeSaveCommit = () => Foreign(file.Path, Block + 500, (byte)(written[Block + 500] ^ 0xFF));
        Assert.False(d.Save().Saved);
        d.BeforeSaveCommit = null;
        Assert.NotNull(d.CheckExternalChange());
    }
}

[Trait("Category", "Large")]
public class OriginalBlockHashesLargeTests
{
    [Fact]
    public void A_patch_beyond_4_GiB_is_verified_against_its_block_and_a_foreign_change_next_to_it_is_a_conflict()
    {
        string path = BigFileBackendFactory.NewPath();
        const long at = 4_400_000_000;
        using (var fs = BigFileBackendFactory.CreateSparse(path))
        {
            fs.Position = at;
            fs.Write(new byte[] { 1, 2, 3, 4 });
        }
        using var d = Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        Assert.True(d.Replace(new(at, at + 1), new byte[] { 9 }, new() { RawBytes = true }).Applied);
        Assert.True(d.Save().Saved);
        Assert.True(d.Replace(new(at + 1, at + 2), new byte[] { 8 }, new() { RawBytes = true }).Applied);
        d.BeforeSaveCommit = () => OriginalBlockHashesTests.Foreign(path, at + 100, 0x55);
        var result = d.Save();
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
    }
}

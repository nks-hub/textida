using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>Checksums (P4.10.3), copy and paste as (P4.10.1) and fill and operations (P4.10.2) of the hex tools.</summary>
public class HexToolsTests
{
    private static readonly byte[] Check = "123456789"u8.ToArray();

    private static string Run(ChecksumSpec spec, byte[] data, int split = 0)
    {
        using var state = spec.Create();
        if (split <= 0 || split >= data.Length) state.Append(data);
        else
        {
            state.Append(data.AsSpan(0, split));
            state.Append(data.AsSpan(split));
        }
        return state.Finish();
    }

    /// <summary>The definition of the Rocksoft model, bit by bit; shares no code with the table-driven implementation.</summary>
    private static ulong BitwiseCrc(CrcModel m, byte[] data)
    {
        ulong mask = m.Width == 64 ? ulong.MaxValue : (1UL << m.Width) - 1, top = 1UL << (m.Width - 1), reg = m.Init;
        static ulong Reverse(ulong v, int w)
        {
            ulong r = 0;
            for (int i = 0; i < w; i++) r |= (v >> i & 1) << (w - 1 - i);
            return r;
        }
        foreach (byte raw in data)
        {
            ulong b = m.ReflectIn ? Reverse(raw, 8) : raw;
            for (int i = 7; i >= 0; i--)
            {
                bool xor = ((reg & top) != 0) != ((b >> i & 1) != 0);
                reg = reg << 1 & mask;
                if (xor) reg ^= m.Polynomial;
            }
        }
        if (m.ReflectOut) reg = Reverse(reg, m.Width);
        return (reg ^ m.XorOut) & mask;
    }

    private static string Hex(ulong v, int width) => v.ToString("x" + (width + 3) / 4);

    private sealed class SyncProgress(List<long> into) : IProgress<long>
    {
        public void Report(long value) => into.Add(value);
    }

    // ---- checksums ----

    [Fact]
    public void Every_crc_of_the_catalog_gives_its_published_check_value_and_the_bitwise_definition_agrees()
    {
        var data = Gen.RandomBytes(new Random(5), 10_000);
        foreach (var model in CrcModel.Catalog)
        {
            var spec = Checksums.Crc(model);
            Assert.Equal(Hex(model.Check!.Value, model.Width), Run(spec, Check));
            Assert.Equal(Hex(BitwiseCrc(model, Check), model.Width), Run(spec, Check));
            Assert.Equal(Hex(BitwiseCrc(model, data), model.Width), Run(spec, data));
            Assert.Equal(Run(spec, data), Run(spec, data, split: 3333));
        }
    }

    [Fact]
    public void A_crc_with_a_custom_polynomial_and_a_width_below_a_byte_follows_the_model()
    {
        var crc8 = new CrcModel("CRC-8/SMBUS", 8, 0x07, 0, false, false, 0);
        var crc5 = new CrcModel("CRC-5/USB", 5, 0x05, 0x1F, true, true, 0x1F);
        var mixed = new CrcModel("mixed", 12, 0x80F, 0x123, true, false, 0x0F0);
        Assert.Equal("f4", Run(Checksums.Crc(crc8), Check));
        Assert.Equal("19", Run(Checksums.Crc(crc5), Check));
        var data = Gen.RandomBytes(new Random(6), 777);
        foreach (var model in new[] { crc8, crc5, mixed, mixed with { ReflectIn = false, ReflectOut = true } })
            Assert.Equal(Hex(BitwiseCrc(model, data), model.Width), Run(Checksums.Crc(model), data));
    }

    [Fact]
    public void A_model_that_does_not_fit_its_width_is_refused()
    {
        Assert.NotNull(new CrcModel("x", 16, 0x18005, 0, true, true, 0).Problem);
        Assert.NotNull(new CrcModel("x", 0, 1, 0, true, true, 0).Problem);
        Assert.NotNull(new CrcModel("x", 16, 0x8004, 0, true, true, 0).Problem);
        Assert.Throws<ArgumentException>(() => Checksums.Crc(new CrcModel("x", 65, 1, 0, true, true, 0)));
    }

    [Fact]
    public void Crc32_and_adler32_equal_the_trailers_of_gzip_and_zlib_which_are_another_implementation()
    {
        var data = Gen.RandomBytes(new Random(8), 300_000);
        var gzip = new MemoryStream();
        using (var z = new GZipStream(gzip, CompressionLevel.NoCompression, leaveOpen: true)) z.Write(data);
        var g = gzip.ToArray();
        Assert.Equal(Convert.ToHexStringLower([g[^5], g[^6], g[^7], g[^8]]), Run(Checksums.Find("crc-32")!, data));

        var zlib = new MemoryStream();
        using (var z = new ZLibStream(zlib, CompressionLevel.NoCompression, leaveOpen: true)) z.Write(data);
        var zb = zlib.ToArray();
        Assert.Equal(Convert.ToHexStringLower(zb[^4..]), Run(Checksums.Find("adler32")!, data));
    }

    [Fact]
    public void Hashes_sums_and_xxhash_give_the_known_values()
    {
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Run(Checksums.Find("md5")!, "abc"u8.ToArray()));
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Run(Checksums.Find("sha1")!, "abc"u8.ToArray()));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Run(Checksums.Find("sha256")!, "abc"u8.ToArray()));
        Assert.Equal("02cc5d05", Run(Checksums.Find("xxh32")!, []));
        Assert.Equal("ef46db3751d8e999", Run(Checksums.Find("xxh64")!, []));
        Assert.Equal("32d153ff", Run(Checksums.Find("xxh32")!, "abc"u8.ToArray()));
        Assert.Equal("44bc2cf5ad770999", Run(Checksums.Find("xxh64")!, "abc"u8.ToArray()));
        Assert.Equal("e2293b2f", Run(Checksums.Find("xxh32")!, "Nobody inspects the spammish repetition"u8.ToArray()));
        Assert.Equal("fbcea83c8a378bf1", Run(Checksums.Find("xxh64")!, "Nobody inspects the spammish repetition"u8.ToArray()));
        Assert.Equal("fd", Run(Checksums.Find("sum8")!, [0xFF, 0xFE]));
        Assert.Equal("01fd", Run(Checksums.Find("sum16")!, [0xFF, 0xFE]));
        Assert.Equal("000001fd", Run(Checksums.Find("sum32")!, [0xFF, 0xFE]));
        Assert.Equal("11e60398", Run(Checksums.Find("adler32")!, "Wikipedia"u8.ToArray()));
    }

    [Fact]
    public void Streaming_in_any_split_gives_the_same_value_as_one_run()
    {
        var data = Gen.RandomBytes(new Random(9), 5000);
        foreach (var spec in Checksums.All)
            foreach (int split in new[] { 1, 15, 16, 17, 31, 32, 33, 4999 })
                Assert.Equal(Run(spec, data), Run(spec, data, split));
    }

    [Fact]
    public void Compute_reads_a_range_of_a_document_reports_progress_and_can_be_cancelled()
    {
        var bytes = Gen.RandomBytes(new Random(10), 3 << 20);
        using var d = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var snapshot = d.GetSnapshot();
        var range = new ByteRange(1234, (2 << 20) + 99);
        var progress = new List<long>();
        string got = Checksums.Compute(Checksums.Find("sha256")!, snapshot, range, new SyncProgress(progress));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(1234, (int)range.Length))), got);
        Assert.Equal(range.Length, progress[^1]);
        Assert.True(progress.Count >= 2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Checksums.Compute(Checksums.Find("crc-32")!, snapshot, range, null, cts.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => Checksums.Compute(Checksums.Find("crc-32")!, snapshot, new ByteRange(0, bytes.Length + 1)));
    }

    [Fact]
    public void A_pasted_value_is_compared_as_a_number()
    {
        Assert.True(Checksums.SameValue("cbf43926", "CBF43926"));
        Assert.True(Checksums.SameValue("cbf43926", "0xCB:F4 39-26"));
        Assert.True(Checksums.SameValue("0000beef", "BEEF"));
        Assert.False(Checksums.SameValue("cbf43926", "cbf43927"));
        Assert.False(Checksums.SameValue("cbf43926", "  "));
    }

    // ---- copy and paste as ----

    private static readonly byte[] Sample = [0x4E, 0x4B, 0x53];

    [Fact]
    public void Text_forms_of_the_bytes_are_written_as_documented()
    {
        Assert.Equal("4E 4B 53", ByteFormats.Format(Sample, ByteFormat.HexSpaced));
        Assert.Equal("4E4B53", ByteFormats.Format(Sample, ByteFormat.HexPlain));
        Assert.Equal("TktT", ByteFormats.Format(Sample, ByteFormat.Base64));
        Assert.Equal("unsigned char data[] = {\n    0x4E, 0x4B, 0x53\n};", ByteFormats.Format(Sample, ByteFormat.CArray));
        Assert.Equal("byte[] data = new byte[] {\n    0x4E, 0x4B, 0x53\n};", ByteFormats.Format(Sample, ByteFormat.CSharp));
        Assert.Equal("byte[] data = {\n    (byte) 0x4E, (byte) 0x4B, (byte) 0x53\n};", ByteFormats.Format(Sample, ByteFormat.Java));
        Assert.Equal("data = bytes([\n    0x4E, 0x4B, 0x53\n])", ByteFormats.Format(Sample, ByteFormat.Python));
        Assert.Equal("const DATA: [u8; 3] = [\n    0x4E, 0x4B, 0x53\n];", ByteFormats.Format(Sample, ByteFormat.Rust));
        Assert.Equal("var data = []byte{\n    0x4E, 0x4B, 0x53\n}", ByteFormats.Format(Sample, ByteFormat.Go));
        Assert.Equal("const data = new Uint8Array([\n    0x4E, 0x4B, 0x53\n]);", ByteFormats.Format(Sample, ByteFormat.JavaScript));
        Assert.Equal("const\n  Data: array[0..2] of Byte = (\n    $4E, $4B, $53\n  );", ByteFormats.Format(Sample, ByteFormat.Pascal));
    }

    [Fact]
    public void Long_arrays_break_after_sixteen_bytes_with_the_comma_at_the_end_of_the_line()
    {
        string text = ByteFormats.Format(new byte[17], ByteFormat.CSharp);
        var lines = text.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.EndsWith("0x00,", lines[1]);
        Assert.Equal("    0x00", lines[2]);
        Assert.Equal(16, lines[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Intel_hex_and_s_record_carry_the_offsets_and_valid_checksums()
    {
        Assert.Equal(":030000004E4B5311\n:00000001FF\n", ByteFormats.Format(Sample, ByteFormat.IntelHex));
        Assert.Equal("S0070000646174615E\nS10600004E4B530D\nS9030000FC\n", ByteFormats.Format(Sample, ByteFormat.SRecord));
        // 0xFFFE is two bytes before a 64 KiB boundary: the record is cut there and an extended address record follows
        string split = ByteFormats.Format([1, 2, 3, 4], ByteFormat.IntelHex, address: 0xFFFE);
        Assert.Equal(":02FFFE000102FE\n:020000040001F9\n:020000000304F7\n:00000001FF\n", split);
        Assert.StartsWith("S2", ByteFormats.Format(Sample, ByteFormat.SRecord, address: 0x1_0000).Split('\n')[1]);
        Assert.StartsWith("S3", ByteFormats.Format(Sample, ByteFormat.SRecord, address: 0x100_0000).Split('\n')[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteFormats.Format(Sample, ByteFormat.IntelHex, address: 0xFFFF_FFFFL));
        // every record sums to zero modulo 256
        foreach (string record in ByteFormats.Format(Gen.RandomBytes(new Random(3), 100), ByteFormat.IntelHex, address: 0x12FF0).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.Equal(0, Convert.FromHexString(record[1..]).Sum(b => b) & 0xFF);
    }

    [Fact]
    public void A_hexdump_has_the_layout_of_hexdump_dash_c()
    {
        byte[] bytes = [.. Enumerable.Range(0, 16).Select(i => (byte)i), (byte)'H', (byte)'i'];
        Assert.Equal("00000000  00 01 02 03 04 05 06 07  08 09 0a 0b 0c 0d 0e 0f  |................|\n"
            + "00000010  48 69" + new string(' ', 45) + "|Hi|\n00000012\n", ByteFormats.Format(bytes, ByteFormat.HexDump));
    }

    [Fact]
    public void A_selection_is_written_from_its_own_offset_and_cancelling_stops_the_export()
    {
        byte[] bytes = [.. Enumerable.Range(0, 64).Select(i => (byte)i)];
        var text = new StringWriter();
        ByteFormats.Write(text, ByteFormat.HexDump, new ArraySnapshot(bytes), new ByteRange(16, 32), 16);
        Assert.StartsWith("00000010  10 11", text.ToString());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ByteFormats.Write(new StringWriter(), ByteFormat.HexPlain, new ArraySnapshot(bytes), new ByteRange(0, 64), 0, cts.Token));
    }

    [Fact]
    public void The_formats_that_can_be_pasted_give_back_the_same_bytes()
    {
        var bytes = Gen.RandomBytes(new Random(4), 1000);
        foreach (var f in new[] { ByteFormat.CArray, ByteFormat.CSharp, ByteFormat.Java, ByteFormat.Python, ByteFormat.Rust, ByteFormat.Go, ByteFormat.JavaScript, ByteFormat.Pascal })
        {
            Assert.True(ByteFormats.TryParse(ByteTextKind.CArray, ByteFormats.Format(bytes, f), out var back, out _), f.ToString());
            Assert.Equal(bytes, back);
        }
        Assert.True(ByteFormats.TryParse(ByteTextKind.Base64, ByteFormats.Format(bytes, ByteFormat.Base64), out var b64, out _));
        Assert.Equal(bytes, b64);
        Assert.True(ByteFormats.TryParse(ByteTextKind.Hex, ByteFormats.Format(bytes, ByteFormat.HexSpaced), out var hex, out _));
        Assert.Equal(bytes, hex);
        Assert.True(ByteFormats.TryParse(ByteTextKind.Hex, ByteFormats.Format(bytes, ByteFormat.HexPlain), out hex, out _));
        Assert.Equal(bytes, hex);
    }

    [Fact]
    public void Base64_of_a_large_run_survives_the_blocks_it_is_written_in()
    {
        var bytes = Gen.RandomBytes(new Random(12), 48 * 1024 * 3 + 7);
        Assert.Equal(Convert.ToBase64String(bytes), ByteFormats.Format(bytes, ByteFormat.Base64));
    }

    [Theory]
    [InlineData(ByteTextKind.CArray, "{ 0x4E, 0x100 }")]
    [InlineData(ByteTextKind.CArray, "no numbers here")]
    [InlineData(ByteTextKind.Base64, "not*base64")]
    [InlineData(ByteTextKind.Hex, "4E4")]
    public void Text_that_is_not_in_the_form_is_refused_with_a_reason(ByteTextKind kind, string text)
    {
        Assert.False(ByteFormats.TryParse(kind, text, out var bytes, out string problem));
        Assert.Empty(bytes);
        Assert.NotEmpty(problem);
    }

    [Fact]
    public void An_array_reads_signed_values_comments_and_binary_and_ignores_numbers_in_names()
    {
        Assert.True(ByteFormats.TryParse(ByteTextKind.CArray, "uint8_t buf8[16] = { -1, 0b101, 12, // 99\n 0x7f };", out var bytes, out _));
        Assert.Equal([0xFF, 5, 12, 0x7F], bytes);
    }

    // ---- fill and operations ----

    private static (Doc Doc, HexEditor Editor) Make(byte[] bytes)
    {
        var d = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        return (d, new HexEditor(d, HexCharset.Windows1252));
    }

    private static byte[] All(Doc d) => d.ReadBytes(new(0, d.ByteLength));

    [Fact]
    public void Fill_repeats_the_pattern_over_the_selection_as_one_undo_step_and_keeps_the_selection()
    {
        var (d, e) = Make(new byte[10]);
        e.Select(2, 9);
        Assert.True(e.Fill([0xDE, 0xAD, 0xBE]).Applied);
        Assert.Equal([0, 0, 0xDE, 0xAD, 0xBE, 0xDE, 0xAD, 0xBE, 0xDE, 0], All(d));
        Assert.Equal(new ByteRange(2, 9), e.Selection);
        Assert.Equal(1, d.UndoDepth);
        Assert.True(e.Undo());
        Assert.Equal(new byte[10], All(d));
        Assert.True(e.Redo());
        Assert.Equal(0xDE, All(d)[2]);
    }


    [Fact]
    public void Fill_without_a_selection_overwrites_a_count_in_overwrite_mode_and_inserts_in_insert_mode()
    {
        var (d, e) = Make([1, 2, 3, 4, 5]);
        e.MoveTo(1);
        Assert.True(e.Fill([0xAA], 2).Applied);
        Assert.Equal([1, 0xAA, 0xAA, 4, 5], All(d));
        e.MoveTo(1);
        Assert.Equal(HexIssue.EndOfFile, e.Fill([0xBB], 9).Issue);
        e.Insert = true;
        e.MoveTo(5);
        Assert.True(e.Fill([7, 8], 5).Applied);
        Assert.Equal([1, 0xAA, 0xAA, 4, 5, 7, 8, 7, 8, 7], All(d));
        Assert.True(e.Undo());
        Assert.Equal(5, d.ByteLength);
    }

    [Fact]
    public void Repeat_selection_puts_copies_behind_it()
    {
        var (d, e) = Make([9, 1, 2, 9]);
        e.Select(1, 3);
        Assert.True(e.RepeatSelection(2).Applied);
        Assert.Equal([9, 1, 2, 1, 2, 1, 2, 9], All(d));
        Assert.Equal(new ByteRange(1, 7), e.Selection);
        e.Undo();
        Assert.Equal([9, 1, 2, 9], All(d));
    }

    [Fact]
    public void Random_fill_changes_the_selection_and_a_sequence_counts_in_words()
    {
        var (d, e) = Make(new byte[64]);
        e.Select(0, 64);
        Assert.True(e.FillRandom().Applied);
        Assert.NotEqual(new byte[64], All(d));
        e.Select(0, 10);
        Assert.True(e.FillSequence(0x0102, 0x10, 2, bigEndian: true).Applied);
        Assert.Equal([0x01, 0x02, 0x01, 0x12, 0x01, 0x22, 0x01, 0x32, 0x01, 0x42], All(d)[..10]);
        e.Select(0, 7);
        Assert.True(e.FillSequence(1, 1, 4, bigEndian: false).Applied);
        Assert.Equal([1, 0, 0, 0, 2, 0, 0], All(d)[..7]);
        e.Select(0, 3);
        Assert.True(e.FillSequence(250, 3, 1, bigEndian: false).Applied);
        Assert.Equal([250, 253, 0], All(d)[..3]);
    }

    [Theory]
    [InlineData(HexOperation.Xor, new byte[] { 0xFF, 0x0F }, 0, new byte[] { 0xFF, 0x0E, 0xFD, 0x0C }, new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    [InlineData(HexOperation.And, new byte[] { 0x0F }, 0, new byte[] { 0x00, 0x01, 0x0F, 0x03 }, new byte[] { 0xF0, 0x01, 0xFF, 0x03 })]
    [InlineData(HexOperation.Or, new byte[] { 0x80, 0x01 }, 0, new byte[] { 0x80, 0x01, 0x81, 0x01 }, new byte[] { 0x00, 0x01, 0x01, 0x01 })]
    [InlineData(HexOperation.Add, new byte[] { 0x10 }, 0, new byte[] { 0x10, 0x00 }, new byte[] { 0x00, 0xF0 })]
    [InlineData(HexOperation.Not, null, 0, new byte[] { 0xFF, 0x0F }, new byte[] { 0x00, 0xF0 })]
    [InlineData(HexOperation.ShiftLeft, null, 3, new byte[] { 0x08, 0xF8 }, new byte[] { 0x01, 0xFF })]
    [InlineData(HexOperation.ShiftRight, null, 3, new byte[] { 0x01, 0x1F }, new byte[] { 0x08, 0xFF })]
    [InlineData(HexOperation.RotateLeft, null, 3, new byte[] { 0x08, 0xFF, 0x05 }, new byte[] { 0x01, 0xFF, 0xA0 })]
    [InlineData(HexOperation.RotateRight, null, 3, new byte[] { 0x20, 0xFF, 0x14 }, new byte[] { 0x01, 0xFF, 0xA0 })]    [InlineData(HexOperation.SwapEndian, null, 2, new byte[] { 2, 1, 4, 3, 5 }, new byte[] { 1, 2, 3, 4, 5 })]
    [InlineData(HexOperation.SwapEndian, null, 4, new byte[] { 4, 3, 2, 1, 5, 6 }, new byte[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(HexOperation.SwapEndian, null, 8, new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })]
    public void An_operation_changes_the_selected_bytes_and_is_one_undo_step(HexOperation op, byte[]? operand, int amount, byte[] expected, byte[] input)
    {
        var (d, e) = Make(input);
        e.SelectAll();
        Assert.True(e.Operate(op, operand, amount).Applied);
        Assert.Equal(expected, All(d));
        Assert.Equal(new ByteRange(0, input.Length), e.Selection);
        Assert.True(e.Undo());
        Assert.Equal(input, All(d));
    }

    [Fact]
    public void An_operation_with_a_wrong_argument_is_refused_before_it_changes_anything()
    {
        var (d, e) = Make([1, 2, 3]);
        e.SelectAll();
        Assert.Throws<ArgumentException>(() => e.Operate(HexOperation.Xor, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Operate(HexOperation.ShiftLeft, null, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Operate(HexOperation.SwapEndian, null, 3));
        Assert.Equal([1, 2, 3], All(d));
        Assert.Equal(0, d.UndoDepth);
        Assert.True(new HexEditor(d, HexCharset.Windows1252).Operate(HexOperation.Not).Applied); // nothing selected: nothing to do
    }

    // ---- the repeated pattern piece ----

    private static PagedDocument Paged(byte[] bytes) => PagedDocument.OpenMemory(bytes, new() { StartIndexing = false });

    private static byte[] ReadAll(PagedDocument p)
    {
        var all = new byte[p.ByteLength];
        Assert.Equal(all.Length, p.Read(0, all));
        return all;
    }

    [Fact]
    public void A_repeated_piece_reads_like_the_expanded_bytes_after_any_cut()
    {
        var model = Enumerable.Range(0, 20).Select(i => (byte)(100 + i)).ToList();
        using var p = Paged([.. model]);
        byte[] pattern = [1, 2, 3];
        var token = p.ReplaceRepeated(5, 7, pattern, 25);
        model.RemoveRange(5, 7);
        model.InsertRange(5, Enumerable.Range(0, 25).Select(i => pattern[i % 3]));
        Assert.Equal(model.ToArray(), ReadAll(p));
        // cuts inside the piece keep the phase of the pattern
        p.Replace(9, 2, [200]);
        model.RemoveRange(9, 2);
        model.Insert(9, 200);
        p.Replace(20, 0, [201, 202]);
        model.InsertRange(20, new byte[] { 201, 202 });
        p.Replace(11, 5, default);
        model.RemoveRange(11, 5);
        Assert.Equal(model.ToArray(), ReadAll(p));
        var window = new byte[13];
        Assert.Equal(13, p.Read(7, window));
        Assert.Equal(model.GetRange(7, 13).ToArray(), window);
        Assert.Equal((long)model.Count, p.ByteLength);
        Assert.NotNull(token);
    }

    [Fact]
    public void Restoring_a_removed_run_brings_back_the_original_bytes()
    {
        byte[] original = Gen.RandomBytes(new Random(2), 1000);
        using var p = Paged(original);
        var token = p.ReplaceRepeated(100, 300, [0xAA], 5000);
        Assert.Equal(5700, p.ByteLength);
        p.RestoreRemoved(100, 5000, token);
        Assert.Equal(original, ReadAll(p));
    }

    [Fact]
    public void A_snapshot_and_a_save_write_the_repeated_run_and_the_pieces_around_it()
    {
        byte[] original = Gen.RandomBytes(new Random(3), 20_000);
        using var file = new TempFile(original);
        using var p = PagedDocument.Open(file.Path, new() { StartIndexing = false });
        byte[] pattern = [0xDE, 0xAD, 0xBE, 0xEF, 0x01];
        p.ReplaceRepeated(1000, 4000, pattern, 3_000_000);
        var snapshot = p.CreateSnapshot();
        var expected = new byte[original.Length - 4000 + 3_000_000];
        original.AsSpan(0, 1000).CopyTo(expected);
        for (int i = 0; i < 3_000_000; i++) expected[1000 + i] = pattern[i % 5];
        original.AsSpan(5000).CopyTo(expected.AsSpan(1000 + 3_000_000));
        var viaSnapshot = new byte[expected.Length];
        Assert.Equal(expected.Length, snapshot.Read(0, viaSnapshot));
        Assert.Equal(expected, viaSnapshot);
        var stream = new MemoryStream();
        p.WriteTo(stream);
        Assert.Equal(expected, stream.ToArray());
        p.SaveAsync().GetAwaiter().GetResult();
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
    }

    [Fact]
    public void Lines_are_counted_through_a_repeated_piece_whose_pattern_holds_line_breaks()
    {
        using var p = PagedDocument.OpenMemory("ab\ncd\n"u8.ToArray());
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!p.LineIndexState.Complete && DateTime.UtcNow < until) Thread.Sleep(5);
        // the pattern puts a CR first, so the 1 MiB blocks of the line scanner end between a CR and its LF
        p.ReplaceRepeated(3, 0, "\r\nx"u8, 3_000_000);
        Assert.Equal((1_000_003L, true), p.LineIndexState);
        // line 1 starts after "ab\n", line m >= 2 after the (m - 1)-th CR LF of the run
        Assert.True(p.TryGetLineStart(1, out long first));
        Assert.Equal(3, first);
        foreach (long line in new[] { 2L, 1000, 500_000, 1_000_001 })
        {
            Assert.True(p.TryGetLineStart(line, out long start));
            Assert.Equal(3 * line - 1, start);
        }
        Assert.True(p.TryGetLineStart(1_000_002, out long last));
        Assert.Equal(3 + 3_000_000 + 3, last);
    }

    // ---- the fill of the document ----

    [Fact]
    public void A_small_fill_is_an_ordinary_edit_with_undo_and_a_change_that_is_a_byte_edit()
    {
        var (d, _) = Make([1, 2, 3, 4, 5, 6]);
        Assert.True(d.Fill(new ByteRange(1, 4), new byte[] { 0xA, 0xB }, 5).Applied);
        Assert.Equal([1, 0xA, 0xB, 0xA, 0xB, 0xA, 5, 6], All(d));
        Assert.Equal(1, d.UndoDepth);
        d.Undo();
        Assert.Equal([1, 2, 3, 4, 5, 6], All(d));
        Assert.Throws<ArgumentException>(() => d.Fill(new ByteRange(0, 1), ReadOnlyMemory<byte>.Empty, 1));
        Assert.Equal(ConflictReason.InvalidRange, d.Fill(new ByteRange(0, 99), new byte[] { 1 }, 1).Conflict);
        Assert.Equal(0, d.Fill(new ByteRange(2, 2), new byte[] { 1 }, 0).Revision - d.Revision);
    }
}

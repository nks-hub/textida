using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.5.1: every action that changes bytes outside the edited ranges lists the changed ranges and cannot run before it is confirmed.</summary>
public class ByteInspectionTests
{
    private static Doc Make(byte[] bytes, TextCodec codec) => Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = codec });

    private static byte[] All(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));

    [Fact]
    public void Convert_lists_the_changed_range_and_waits_for_confirmation()
    {
        byte[] original = Encoding.UTF8.GetBytes("abc řeč");
        using var d = Make(original, TextCodec.Utf8);
        var report = d.InspectConvert(TextCodec.Cp1250);

        var range = Assert.Single(report.Ranges);
        Assert.Equal(4, range.Start);
        Assert.Equal(TextCodec.Utf8, report.CodecBefore);
        Assert.Equal(TextCodec.Cp1250, report.CodecAfter);
        Assert.Equal(original.Length, report.SizeBefore);
        Assert.Equal(Encoding.GetEncoding(1250).GetBytes("abc řeč").Length, report.SizeAfter);
        Assert.False(report.HasLoss);

        var refused = d.Apply(report);
        Assert.False(refused.Applied);
        Assert.Equal(ConflictReason.NotConfirmed, refused.Conflict);
        Assert.Equal(original, All(d));
        Assert.Equal(TextCodec.Utf8, d.Codec);

        report.Confirm();
        Assert.True(d.Apply(report).Applied);
        Assert.Equal(Encoding.GetEncoding(1250).GetBytes("abc řeč"), All(d));
        Assert.NotNull(d.Undo());
        Assert.Equal(original, All(d));
    }

    [Fact]
    public void A_conversion_that_loses_characters_needs_the_replacement_in_the_confirmation()
    {
        byte[] original = Encoding.UTF8.GetBytes("ř😀");
        using var d = Make(original, TextCodec.Utf8);
        var report = d.InspectConvert(TextCodec.Cp1250);
        Assert.Equal(1, report.Loss.Total);
        Assert.True(report.IsBlocked);
        Assert.Throws<InvalidOperationException>(() => report.Confirm());
        Assert.False(report.IsConfirmed);
        Assert.Equal(ConflictReason.NotConfirmed, d.Apply(report).Conflict);

        report.Confirm("?");
        Assert.False(report.IsBlocked);
        Assert.True(d.Apply(report).Applied);
        Assert.Equal([0xF8, (byte)'?'], All(d));
    }

    [Fact]
    public void Converting_ascii_to_another_single_byte_encoding_changes_no_byte()
    {
        using var d = Make("plain"u8.ToArray(), TextCodec.Utf8);
        var report = d.InspectConvert(TextCodec.Cp1250);
        Assert.Empty(report.Ranges);
        Assert.Equal(0, report.RangeCount);
        report.Confirm();
        Assert.True(d.Apply(report).Applied);
        Assert.Equal(TextCodec.Cp1250, d.Codec);
    }

    [Fact]
    public void Unify_eol_lists_each_line_ending_with_old_and_new_bytes()
    {
        using var d = Make("a\r\nb\nc\r\n"u8.ToArray(), TextCodec.Utf8);
        var report = d.InspectUnifyEol(EolStyle.Lf);
        Assert.Equal(2, report.RangeCount);
        Assert.Equal([1L, 6L], report.Ranges.Select(r => r.Start));
        Assert.All(report.Ranges, r => { Assert.Equal(2, r.OldLength); Assert.Equal(1, r.NewLength); Assert.Equal("\r\n"u8.ToArray(), r.OldHead); Assert.Equal("\n"u8.ToArray(), r.NewHead); });
        Assert.Equal(EolStyle.Mixed, report.EolBefore.Style);
        Assert.Equal(EolStyle.Lf, report.EolAfter.Style);
        Assert.Equal(report.SizeBefore - 2, report.SizeAfter);

        Assert.Equal(ConflictReason.NotConfirmed, d.Apply(report).Conflict);
        Assert.Equal("a\r\nb\nc\r\n"u8.ToArray(), All(d));
        report.Confirm();
        Assert.True(d.Apply(report).Applied);
        Assert.Equal("a\nb\nc\n"u8.ToArray(), All(d));
        Assert.Equal(1, d.UndoDepth);
    }

    [Fact]
    public void Normalize_lists_the_lines_it_rewrites_and_applies_after_confirmation()
    {
        using var d = Make(Encoding.UTF8.GetBytes("é x\nplain\n"), TextCodec.Utf8);
        var report = d.InspectNormalize(NormalizationForm.FormC);
        var range = Assert.Single(report.Ranges);
        Assert.Equal(0, range.Start);
        Assert.True(d.Apply(report).Conflict == ConflictReason.NotConfirmed);
        report.Confirm();
        Assert.True(d.Apply(report).Applied);
        Assert.Equal(Encoding.UTF8.GetBytes("é x\nplain\n"), All(d));
    }

    [Fact]
    public void An_inspection_of_an_older_revision_is_refused()
    {
        using var d = Make("a\r\nb"u8.ToArray(), TextCodec.Utf8);
        var report = d.InspectUnifyEol(EolStyle.Lf);
        report.Confirm();
        Assert.True(d.Insert(0, "x"u8.ToArray()).Applied);
        var result = d.Apply(report);
        Assert.False(result.Applied);
        Assert.Equal("xa\r\nb"u8.ToArray(), All(d));
    }

    [Fact]
    public void The_listed_ranges_are_capped_but_counted()
    {
        var text = string.Concat(Enumerable.Repeat("a\r\n", 1500));
        using var d = Make(Encoding.ASCII.GetBytes(text), TextCodec.Utf8);
        var report = d.InspectUnifyEol(EolStyle.Lf);
        Assert.Equal(1500, report.RangeCount);
        Assert.Equal(ByteInspection.MaxListed, report.Ranges.Count);
        Assert.Equal(1500, report.ChangedBytes);
    }

    [Fact]
    public void The_save_report_lists_the_changed_ranges_with_the_old_bytes_from_the_file()
    {
        using var file = new TempFile(Enumerable.Range(0, 200).Select(i => (byte)i).ToArray());
        using var d = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        Assert.True(d.Replace(new(10, 12), new byte[] { 0xAA, 0xBB }, new() { RawBytes = true }).Applied);
        var report = d.InspectUnsaved();
        var range = Assert.Single(report.Ranges);
        Assert.Equal((10L, 2L, 2L), (range.Start, range.OldLength, range.NewLength));
        Assert.Equal(new byte[] { 10, 11 }, range.OldHead);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, range.NewHead);
        Assert.False(d.Apply(report).Applied);
    }
}

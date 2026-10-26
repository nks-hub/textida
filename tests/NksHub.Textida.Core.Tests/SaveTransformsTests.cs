using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class SaveTransformsTests
{
    private static Doc OpenFile(JournalTempDir dir, string text, Encoding? enc = null, byte[]? bom = null, DocumentOptions? options = null)
    {
        string path = dir.Combine("f.txt");
        File.WriteAllBytes(path, [.. bom ?? [], .. (enc ?? new UTF8Encoding(false)).GetBytes(text)]);
        return Doc.Open(path, ReferenceBackendFactory.Instance, options);
    }

    private static string Text(Doc d) => d.Codec.GetString(d.ReadBytes(new ByteRange(d.BomLength, d.ByteLength)).AsSpan(0, (int)(d.ByteLength - d.BomLength)));

    private static long Offset(Doc d, string needle) => Encoding.UTF8.GetByteCount(Text(d)[..Text(d).IndexOf(needle, StringComparison.Ordinal)]);

    [Fact]
    public void Only_the_lines_edited_since_the_last_save_are_trimmed()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "keep1  \nedit me  \nkeep2 \t\nalso edited   \nkeep3  ");
        doc.Insert(Offset(doc, "edit me") + 7, "!"u8.ToArray());
        doc.Replace(new ByteRange(Offset(doc, "also"), Offset(doc, "also") + 4), "ALSO"u8.ToArray());

        var (plan, result) = SaveTransforms.Apply(doc, new SaveTransformOptions(true));

        Assert.True(result!.Value.Applied);
        Assert.Null(plan.Note);
        Assert.Equal("keep1  \nedit me!\nkeep2 \t\nALSO edited\nkeep3  ", Text(doc));
        Assert.Equal(2, plan.Edits.Count);
    }

    [Fact]
    public void A_save_resets_what_counts_as_edited()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "a  \nb  \n");
        doc.Insert(0, "x"u8.ToArray());
        SaveTransforms.Apply(doc, new SaveTransformOptions(true));
        Assert.True(doc.Save().Saved);
        Assert.Empty(SaveTransforms.Plan(doc, new SaveTransformOptions(true)).Edits);

        doc.Insert(Offset(doc, "b") + 1, "y"u8.ToArray());
        SaveTransforms.Apply(doc, new SaveTransformOptions(true));
        Assert.Equal("xa\nby\n", Text(doc));
    }

    [Fact]
    public void Deleting_text_marks_the_line_as_edited()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "foo  bar\nuntouched  \n");
        doc.Delete(new ByteRange(5, 8));
        SaveTransforms.Apply(doc, new SaveTransformOptions(true));
        Assert.Equal("foo\nuntouched  \n", Text(doc));
    }

    [Fact]
    public void A_document_that_was_never_saved_is_trimmed_everywhere()
    {
        using var doc = Doc.FromBytes("a  \r\nb\t\r\n  \r\nc"u8.ToArray(), ReferenceBackendFactory.Instance);
        SaveTransforms.Apply(doc, new SaveTransformOptions(true));
        Assert.Equal("a\r\nb\r\n\r\nc", Text(doc));
    }

    [Fact]
    public void Line_endings_are_never_converted_and_the_final_newline_follows_the_document()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "one  \r\ntwo  \r\nthree  ");
        doc.Insert(doc.ByteLength, "!"u8.ToArray());
        var plan = SaveTransforms.Plan(doc, new SaveTransformOptions(true, true, EolStyle.Lf));
        doc.Apply(plan.Edits);
        Assert.Equal("one  \r\ntwo  \r\nthree  !\r\n", Text(doc));   // only the third line was edited; every CRLF stays

        var none = OpenFile(new JournalTempDir(), "single line");
        none.Insert(0, "x"u8.ToArray());
        SaveTransforms.Apply(none, new SaveTransformOptions(false, true, EolStyle.CrLf));
        Assert.Equal("xsingle line\r\n", Text(none));
        none.Dispose();
    }

    [Fact]
    public void The_final_newline_can_be_removed_and_an_empty_document_stays_empty()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "a\r\nb\r\n");
        doc.Insert(0, "x"u8.ToArray());
        SaveTransforms.Apply(doc, new SaveTransformOptions(false, false));
        Assert.Equal("xa\r\nb", Text(doc));

        using var empty = Doc.FromBytes(ReadOnlyMemory<byte>.Empty, ReferenceBackendFactory.Instance);
        Assert.Empty(SaveTransforms.Plan(empty, new SaveTransformOptions(true, true)).Edits);
    }

    [Fact]
    public void Nothing_is_planned_for_an_unedited_document_and_a_short_history_is_reported()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "a  \nb  \n", options: new DocumentOptions { ChangeLogCapacity = 2 });
        Assert.Empty(SaveTransforms.Plan(doc, new SaveTransformOptions(true, true)).Edits);
        for (int i = 0; i < 5; i++) doc.Insert(0, "z"u8.ToArray());
        var plan = SaveTransforms.Plan(doc, new SaveTransformOptions(true));
        Assert.Empty(plan.Edits);
        Assert.Contains("history", plan.Note);
    }

    [Theory]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("cp1250")]
    public void Trimming_works_in_other_encodings_and_changes_no_other_byte(string kind)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var (enc, bom) = kind switch
        {
            "utf16le" => (new UnicodeEncoding(false, false), TextCodec.Utf16LEBom.Bom),
            "utf16be" => (new UnicodeEncoding(true, false), TextCodec.Utf16BEBom.Bom),
            _ => ((Encoding)Encoding.GetEncoding(1250), Array.Empty<byte>()),
        };
        using var dir = new JournalTempDir();
        string original = "ěš  \nabc  \nčř \t\n";
        using var doc = OpenFile(dir, original, enc, bom);
        var bytes = enc.GetBytes("abc");
        long at = bom.Length + enc.GetByteCount("ěš  \n");
        doc.Insert(at, enc.GetBytes("X"));
        SaveTransforms.Apply(doc, new SaveTransformOptions(true));
        Assert.Equal([.. bom, .. enc.GetBytes("ěš  \nXabc\nčř \t\n")], doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void The_transforms_are_one_undo_step()
    {
        using var dir = new JournalTempDir();
        using var doc = OpenFile(dir, "a  \nb  \n");
        doc.Insert(0, "x"u8.ToArray());
        int depth = doc.UndoDepth;
        SaveTransforms.Apply(doc, new SaveTransformOptions(true, true));
        Assert.Equal(depth + 1, doc.UndoDepth);
        doc.Undo();
        Assert.Equal("xa  \nb  \n", Text(doc));
    }

    [Fact]
    public void ChangedRangesSince_follows_edits_through_later_changes()
    {
        using var doc = Doc.FromBytes("0123456789"u8.ToArray(), ReferenceBackendFactory.Instance);
        long start = doc.Revision;
        doc.Replace(new ByteRange(4, 6), "ABCD"u8.ToArray());     // 0123ABCD6789
        doc.Insert(0, "__"u8.ToArray());                          // __0123ABCD6789
        doc.Delete(new ByteRange(12, 14));                        // __0123ABCD67 (89 gone)
        Assert.Equal([new ByteRange(0, 2), new ByteRange(6, 10), new ByteRange(12, 12)], doc.ChangedRangesSince(start));
        Assert.Empty(doc.ChangedRangesSince(doc.Revision)!);
        Assert.Null(doc.ChangedRangesSince(doc.Revision + 1));
    }
}

using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.Contract;

/// <summary>
/// P1.3.3 to P1.3.5 through <see cref="Doc"/>: re-read as, convert to with the loss warning, save as, unify line endings and
/// normalise Unicode, all undoable. Expected bytes come from framework encodings and string operations.
/// </summary>
public abstract class DocumentEncodingSuite
{
    protected abstract IBackendFactory Factory { get; }

    private bool Wide => Factory.Capabilities.SupportsUtf16;
    private static readonly Encoding Cp1250 = CodePage(1250);

    private static Encoding CodePage(int page)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(page);
    }

    private Doc New(byte[] bytes, TextCodec codec) => Doc.FromBytes(bytes, Factory, new DocumentOptions { Format = codec });
    private static byte[] Bytes(Doc d) => d.ReadBytes(new(0, d.ByteLength));
    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);
    private static string Text(Doc d) => d.ReadText(new(d.BomLength, d.ByteLength));

    // ---- re-read as ----

    [Fact]
    public void ReReadAsChangesTheViewButNeitherTheBytesNorTheFile()
    {
        using var dir = new TempDir();
        string path = dir.File("a.txt");
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Utf8("příliš\r\nžluťoučký")];
        File.WriteAllBytes(path, bytes);
        using var d = Doc.Open(path, Factory);
        Assert.Same(TextCodec.Utf8Bom, d.Codec);
        var fingerprint = d.Fingerprint;
        long revision = d.Revision;
        var kinds = new List<ChangeKind>();
        d.Changed += (_, e) => kinds.Add(e.Change.Kind);
        SpinWait.SpinUntil(() => d.LineIndexState.Complete, TimeSpan.FromSeconds(10));
        Assert.True(d.TryGetOffset(new(1, 0), out long lineTwo));

        d.ReinterpretAs(TextCodec.Cp1250);

        Assert.Same(TextCodec.Cp1250, d.Codec);
        Assert.Equal(0, d.BomLength);
        Assert.Equal(bytes, Bytes(d));
        Assert.Equal(Cp1250.GetString(bytes), d.ReadText(new(0, d.ByteLength)));
        Assert.Equal(revision + 1, d.Revision);
        Assert.Equal([ChangeKind.View], kinds);
        Assert.Equal(fingerprint, d.Fingerprint);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Null(d.CheckExternalChange());
        Assert.False(d.IsModified);
        Assert.False(d.CanUndo);
        Assert.True(d.TryGetOffset(new(1, 0), out long again));
        Assert.Equal(lineTwo, again);
        var mapped = d.MapRange(new(5, 9), revision);
        Assert.True(mapped.Ok);
        Assert.Equal(new ByteRange(5, 9), mapped.Range);

        d.ReinterpretAs(TextCodec.Utf8Bom);
        Assert.Equal("příliš\r\nžluťoučký", Text(d));
        d.ReinterpretAs(TextCodec.Utf8Bom);
        Assert.Equal(revision + 2, d.Revision);
    }

    [Fact]
    public void ReReadAsAnUnsupportedEncodingRefusesAndChangesNothing()
    {
        if (Wide) return;
        using var d = New(Utf8("abc"), TextCodec.Utf8);
        Assert.Throws<NotSupportedException>(() => d.ReinterpretAs(TextCodec.Utf16LE));
        Assert.Throws<NotSupportedException>(() => d.ReinterpretAs(TextCodec.Utf32BEBom));
        Assert.Equal(0, d.Revision);
        Assert.Same(TextCodec.Utf8, d.Codec);
        Assert.Equal("abc", Text(d));
    }

    [Fact]
    public void ReReadAsUtf16KeepsAnOddTailAsOneInvalidUnit()
    {
        if (!Wide) return;
        byte[] bytes = "abcde"u8.ToArray();
        using var d = New(bytes, TextCodec.Utf8);
        d.ReinterpretAs(TextCodec.Utf16LE);
        Assert.Equal(new UnicodeEncoding(false, false).GetString(bytes), Text(d));
        Assert.True(d.IsCharBoundary(4));
        Assert.False(d.IsCharBoundary(3));
        Assert.Equal(bytes, Bytes(d));
    }

    // ---- convert to ----

    [Fact]
    public void ConvertToACodePageIsOneUndoableStepThatRestoresBytesAndEncoding()
    {
        string text = "příliš\r\nžluťoučký kůň";
        byte[] original = Utf8(text);
        using var d = New(original, TextCodec.Utf8);
        var preview = d.PreviewConvert(TextCodec.Cp1250);
        Assert.False(preview.Loss.HasLoss);
        Assert.Equal(Cp1250.GetByteCount(text), preview.ByteLength);
        Assert.Equal(0, d.Revision);
        Assert.Equal(original, Bytes(d));

        Assert.True(d.ConvertTo(TextCodec.Cp1250).Applied);
        Assert.Equal(1, d.Revision);
        Assert.Same(TextCodec.Cp1250, d.Codec);
        Assert.Equal(Cp1250.GetBytes(text), Bytes(d));
        Assert.Equal(text, Text(d));
        Assert.True(d.IsModified);

        d.Undo();
        Assert.Equal(original, Bytes(d));
        Assert.Same(TextCodec.Utf8, d.Codec);
        Assert.Equal(text, Text(d));
        Assert.False(d.CanUndo);
        d.Redo();
        Assert.Equal(Cp1250.GetBytes(text), Bytes(d));
        Assert.Same(TextCodec.Cp1250, d.Codec);
    }

    [Fact]
    public void ConvertToIsBlockedUntilAReplacementIsConfirmed()
    {
        string text = "a😀b€ř";
        byte[] original = Utf8(text);
        using var d = New(original, TextCodec.Utf8);
        var preview = d.PreviewConvert(TextCodec.Cp1250);
        Assert.Equal(1, preview.Loss.Total);
        Assert.Equal(0x1F600, preview.Loss.Entries[0].CodePoint);

        var blocked = d.ConvertTo(TextCodec.Cp1250);
        Assert.False(blocked.Applied);
        Assert.Equal(ConflictReason.LossNotConfirmed, blocked.Conflict);
        Assert.Equal(0, d.Revision);
        Assert.Equal(original, Bytes(d));
        Assert.Same(TextCodec.Utf8, d.Codec);

        Assert.True(d.ConvertTo(TextCodec.Cp1250, "?").Applied);
        Assert.Equal(Cp1250.GetBytes("a?b€ř"), Bytes(d));
        d.Undo();
        Assert.Equal(original, Bytes(d));
        Assert.Same(TextCodec.Utf8, d.Codec);
    }

    [Fact]
    public void ConvertToCountsInvalidSourceBytesAsLossAndNeverCarriesThemAcross()
    {
        byte[] original = [.. "a"u8, 0xFF, .. "b"u8, 0xE2, 0x82];
        using var d = New(original, TextCodec.Utf8);
        var preview = d.PreviewConvert(TextCodec.Cp1250);
        Assert.Equal(2, preview.Loss.Total);
        Assert.All(preview.Loss.Entries, e => Assert.True(e.FromInvalidBytes));
        Assert.False(d.ConvertTo(TextCodec.Cp1250).Applied);
        Assert.True(d.ConvertTo(TextCodec.Cp1250, "?").Applied);
        Assert.Equal("a?b?"u8.ToArray(), Bytes(d));
        d.Undo();
        Assert.Equal(original, Bytes(d));
    }

    [Fact]
    public void ConvertToChangesOnlyTheByteOrderMarkWhenTheBytesAreTheSame()
    {
        byte[] original = Utf8("ahoj");
        using var d = New(original, TextCodec.Utf8);
        Assert.True(d.ConvertTo(TextCodec.Utf8Bom).Applied);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. original], Bytes(d));
        Assert.Equal(3, d.BomLength);
        Assert.Equal("ahoj", Text(d));
        Assert.True(d.ConvertTo(TextCodec.Utf8).Applied);
        Assert.Equal(original, Bytes(d));
        d.Undo();
        d.Undo();
        Assert.Equal(original, Bytes(d));
        Assert.Same(TextCodec.Utf8, d.Codec);
        Assert.Equal(0, d.BomLength);
        Assert.Equal(4, d.Revision); // two converts and two undos
    }

    [Fact]
    public void ConvertToUtf16WorksOrIsRefusedByTheBackend()
    {
        string text = "příliš\r\nžlutý 😀";
        byte[] original = Utf8(text);
        using var d = New(original, TextCodec.Utf8);
        if (!Wide)
        {
            Assert.Throws<NotSupportedException>(() => d.PreviewConvert(TextCodec.Utf16LEBom));
            Assert.Throws<NotSupportedException>(() => d.ConvertTo(TextCodec.Utf16LEBom));
            Assert.Equal(0, d.Revision);
            Assert.Equal(original, Bytes(d));
            return;
        }
        Assert.False(d.PreviewConvert(TextCodec.Utf16LEBom).Loss.HasLoss);
        Assert.True(d.ConvertTo(TextCodec.Utf16LEBom).Applied);
        Assert.Equal([0xFF, 0xFE, .. new UnicodeEncoding(false, false).GetBytes(text)], Bytes(d));
        Assert.Equal(text, Text(d));
        Assert.True(d.ConvertTo(TextCodec.Utf32BE).Applied);
        Assert.Equal(new UTF32Encoding(true, false).GetBytes(text), Bytes(d));
        Assert.True(d.ConvertTo(TextCodec.Utf8).Applied);
        Assert.Equal(original, Bytes(d));
        d.Undo();
        d.Undo();
        d.Undo();
        Assert.Equal(original, Bytes(d));
        Assert.Same(TextCodec.Utf8, d.Codec);
    }

    // ---- save as ----

    [Fact]
    public void SaveAsRunsTheLossCheckBeforeAnythingIsWritten()
    {
        using var dir = new TempDir();
        string source = dir.File("a.txt"), target = dir.File("b.txt");
        byte[] original = Utf8("ř😀");
        File.WriteAllBytes(source, original);
        using var d = Doc.Open(source, Factory);

        var blocked = d.SaveAs(target, TextCodec.Cp1250);
        Assert.False(blocked.Saved);
        Assert.Equal(1, blocked.Loss!.Total);
        Assert.False(File.Exists(target));
        Assert.Equal(0, d.Revision);
        Assert.Same(TextCodec.Utf8, d.Codec);
        Assert.False(d.IsModified);

        var saved = d.SaveAs(target, TextCodec.Cp1250, "?");
        Assert.True(saved.Saved);
        Assert.Equal(Cp1250.GetBytes("ř?"), File.ReadAllBytes(target));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Same(TextCodec.Cp1250, d.Codec);
        Assert.Equal(Path.GetFullPath(target), d.FilePath);
        Assert.False(d.IsModified);
    }

    [Fact]
    public void SaveAsWithoutLossNeedsNoReplacement()
    {
        using var dir = new TempDir();
        string target = dir.File("b.txt");
        using var d = New(Utf8("příliš"), TextCodec.Utf8);
        var result = d.SaveAs(target, TextCodec.Utf8Bom);
        Assert.True(result.Saved);
        Assert.Null(result.Loss);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. Utf8("příliš")], File.ReadAllBytes(target));
    }

    [Fact]
    public void ASaveAsThatFindsAnExternalChangeTakesTheConversionBack()
    {
        using var dir = new TempDir();
        string path = dir.File("a.txt");
        byte[] original = Utf8("příliš");
        File.WriteAllBytes(path, original);
        using var d = Doc.Open(path, Factory);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            fs.Write(Utf8("someone else")); // longer than the original, so a mapped file can take it

        var result = d.SaveAs(path, TextCodec.Cp1250);
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        if (!Factory.MapsFiles) Assert.Equal(original, Bytes(d)); // a mapped file shows the other program's write through the mapping
        Assert.Same(TextCodec.Utf8, d.Codec);
        Assert.False(d.CanRedo);
        Assert.Equal(Utf8("someone else"), File.ReadAllBytes(path));
    }

    // ---- line endings ----

    [Fact]
    public void UnifyingLineEndingsPreviewsTheChangedRangesAndUndoReturnsTheOriginalBytes()
    {
        string text = "a\r\nb\nc\rd\r\n";
        foreach (var f in TestFormat.All.Where(f => Wide || !f.IsUtf16))
        {
            using var d = New(f.Bytes(text), f.Format);
            byte[] original = Bytes(d);

            var preview = d.PreviewUnifyEol(EolStyle.Lf);
            Assert.Equal(3, preview.Count);
            Assert.Equal(f.Bom.Length + f.Raw("a").Length, preview.Edits[0].Range.Start);
            Assert.Equal(f.Raw("\r\n").Length, preview.Edits[0].Range.Length);
            Assert.Equal(f.Raw("\n"), preview.Edits[0].Insert.ToArray());
            Assert.Equal(0, d.Revision);
            Assert.Equal(original, Bytes(d));

            Assert.True(d.ApplyPreview(preview).Applied);
            Assert.Equal(f.Bytes("a\nb\nc\nd\n"), Bytes(d));
            Assert.Equal(1, d.Revision);
            d.Undo();
            Assert.Equal(original, Bytes(d));
            Assert.False(d.CanUndo);

            Assert.True(d.UnifyEol(EolStyle.CrLf).Applied);
            Assert.Equal(f.Bytes("a\r\nb\r\nc\r\nd\r\n"), Bytes(d));
            d.Undo();
            Assert.Equal(original, Bytes(d));
            Assert.True(d.UnifyEol(EolStyle.Cr).Applied);
            Assert.Equal(f.Bytes("a\rb\rc\rd\r"), Bytes(d));
        }
    }

    [Fact]
    public void UnifyingLineEndingsLeavesInvalidBytesAndUntouchedEndingsAlone()
    {
        byte[] original = [.. "a"u8, 0xFF, .. "\r\n\n"u8, 0xE2, 0x82, .. "\r"u8];
        using var d = New(original, TextCodec.Utf8);
        Assert.Equal(2, d.PreviewUnifyEol(EolStyle.Lf).Count);
        Assert.True(d.UnifyEol(EolStyle.Lf).Applied);
        Assert.Equal([.. "a"u8, 0xFF, .. "\n\n"u8, 0xE2, 0x82, .. "\n"u8], Bytes(d));
        d.Undo();
        Assert.Equal(original, Bytes(d));
        Assert.Equal(2, d.PreviewUnifyEol(EolStyle.CrLf).Count);
    }

    [Fact]
    public void APreviewStaysValidWhileTheUserTypesElsewhereAndIsRejectedWhenTheyEditTheSameRange()
    {
        byte[] original = Utf8("a\r\nb\r\nc");
        using var d = New(original, TextCodec.Utf8);
        var preview = d.PreviewUnifyEol(EolStyle.Lf);
        Assert.True(d.Insert(0, "XX"u8.ToArray()).Applied);
        Assert.True(d.ApplyPreview(preview).Applied);
        Assert.Equal("XXa\nb\nc"u8.ToArray(), Bytes(d));

        using var e = New(original, TextCodec.Utf8);
        var stale = e.PreviewUnifyEol(EolStyle.Lf);
        Assert.True(e.Insert(2, "Z"u8.ToArray()).Applied); // between CR and LF
        var rejected = e.ApplyPreview(stale);
        Assert.False(rejected.Applied);
        Assert.Equal(ConflictReason.Overlap, rejected.Conflict);
        Assert.Equal("a\rZ\nb\r\nc"u8.ToArray(), Bytes(e));
    }

    // ---- Unicode normalisation ----

    [Fact]
    public void NormalisationPreviewsLinesAndUndoReturnsTheOriginalBytes()
    {
        string text = "café ǎb\nplain\nné";
        foreach (var f in TestFormat.All.Where(f => (f.Format.Kind != CodecKind.SingleByte) && (Wide || !f.IsUtf16)))
        {
            using var d = New(f.Bytes(text), f.Format);
            byte[] original = Bytes(d);
            var preview = d.PreviewNormalize(NormalizationForm.FormC);
            Assert.Equal(2, preview.Count);
            Assert.Equal(original, Bytes(d));

            Assert.True(d.ApplyPreview(preview).Applied);
            Assert.Equal(f.Bytes(text.Normalize(NormalizationForm.FormC)), Bytes(d));
            d.Undo();
            Assert.Equal(original, Bytes(d));

            Assert.True(d.Normalize(NormalizationForm.FormC).Applied);
            var nfc = Bytes(d);
            Assert.True(d.Normalize(NormalizationForm.FormD).Applied);
            Assert.Equal(f.Bytes(text.Normalize(NormalizationForm.FormD)), Bytes(d));
            d.Undo();
            Assert.Equal(nfc, Bytes(d));
            d.Undo();
            Assert.Equal(original, Bytes(d));
            Assert.Equal(2, d.PreviewNormalize(NormalizationForm.FormC).Count);
        }
    }

    [Fact]
    public void NormalisationNeverTouchesInvalidBytes()
    {
        byte[] original = [.. Utf8("é"), 0xFF, .. Utf8("é")];
        using var d = New(original, TextCodec.Utf8);
        Assert.True(d.Normalize(NormalizationForm.FormC).Applied);
        Assert.Equal([0xC3, 0xA9, 0xFF, 0xC3, 0xA9], Bytes(d));
        d.Undo();
        Assert.Equal(original, Bytes(d));
    }

    [Fact]
    public void NormalisingToADecomposedFormInACodePageIsBlockedAsLoss()
    {
        byte[] original = Cp1250.GetBytes("café");
        using var d = New(original, TextCodec.Cp1250);
        var preview = d.PreviewNormalize(NormalizationForm.FormD);
        Assert.True(preview.IsBlocked);
        Assert.Equal(1, preview.Loss.Total);
        var blocked = d.ApplyPreview(preview);
        Assert.False(blocked.Applied);
        Assert.Equal(ConflictReason.LossNotConfirmed, blocked.Conflict);
        Assert.Equal(original, Bytes(d));
        Assert.Equal(0, d.Revision);

        Assert.True(d.Normalize(NormalizationForm.FormD, "?").Applied);
        Assert.Equal(Cp1250.GetBytes("cafe?"), Bytes(d));
        d.Undo();
        Assert.Equal(original, Bytes(d));
    }
}

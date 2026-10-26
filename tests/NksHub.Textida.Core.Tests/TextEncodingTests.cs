using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>Our own small corpus: every case is a name, a codec and the exact bytes.</summary>
public static class EncodingCorpus
{
    public const string Czech = "Příliš žluťoučký kůň\r\núpěl ďábelské ódy\r\n";
    public const string French = "Où est le garçon? Il a mangé une crêpe à la fraise, très déçu de l'été passé.\n";

    private static byte[] Raw(TextCodec c, string text, bool bom = false)
    {
        var enc = c.Encode(text);
        return bom ? [.. c.Bom, .. enc] : enc;
    }

    public static IEnumerable<(string Name, TextCodec Codec, byte[] Bytes)> All()
    {
        string wide = Czech + "emoji \U0001F600 end\n";
        yield return ("ascii", TextCodec.Utf8, "plain text\nsecond line\n"u8.ToArray());
        yield return ("utf8-cz", TextCodec.Utf8, Raw(TextCodec.Utf8, Czech));
        yield return ("utf8-bom", TextCodec.Utf8Bom, Raw(TextCodec.Utf8Bom, wide, true));
        yield return ("utf16le-bom", TextCodec.Utf16LEBom, Raw(TextCodec.Utf16LEBom, wide, true));
        yield return ("utf16be-bom", TextCodec.Utf16BEBom, Raw(TextCodec.Utf16BEBom, wide, true));
        yield return ("utf16le", TextCodec.Utf16LE, Raw(TextCodec.Utf16LE, wide));
        yield return ("utf16be", TextCodec.Utf16BE, Raw(TextCodec.Utf16BE, wide));
        yield return ("utf32le-bom", TextCodec.Utf32LEBom, Raw(TextCodec.Utf32LEBom, wide, true));
        yield return ("utf32be-bom", TextCodec.Utf32BEBom, Raw(TextCodec.Utf32BEBom, wide, true));
        yield return ("utf32le", TextCodec.Utf32LE, Raw(TextCodec.Utf32LE, wide));
        yield return ("utf32be", TextCodec.Utf32BE, Raw(TextCodec.Utf32BE, wide));
        yield return ("cp1250", TextCodec.Cp1250, Raw(TextCodec.Cp1250, Czech));
        yield return ("cp1252", TextCodec.Cp1252, Raw(TextCodec.Cp1252, French));
        yield return ("latin2", TextCodec.Latin2, Raw(TextCodec.Latin2, Czech));
        yield return ("kamenicky", TextCodec.Kamenicky, Raw(TextCodec.Kamenicky, Czech));
        yield return ("mixed-eol", TextCodec.Utf8, "a\r\nb\nc\rd\r\ne"u8.ToArray());
        yield return ("broken-utf8", TextCodec.Utf8, [.. "ab"u8, 0xC3, 0x28, .. "cd"u8, 0x80, .. "ef\n"u8, 0xE2, 0x82]);
        yield return ("utf16-odd-tail", TextCodec.Utf16LE, [.. Raw(TextCodec.Utf16LE, "ab\ncd"), 0x41]);
        yield return ("utf16-lone-surrogate", TextCodec.Utf16LE, [.. Raw(TextCodec.Utf16LE, "ab"), 0x00, 0xD8, .. Raw(TextCodec.Utf16LE, "cd")]);
        yield return ("utf32-short-tail", TextCodec.Utf32BE, [.. Raw(TextCodec.Utf32BE, "ab\ncd"), 0x00, 0x00]);
        yield return ("utf32-out-of-range", TextCodec.Utf32LE, [.. Raw(TextCodec.Utf32LE, "ab"), 0x00, 0x00, 0x11, 0x00, .. Raw(TextCodec.Utf32LE, "cd")]);
    }

    public static IEnumerable<object[]> Names() => All().Select(c => new object[] { c.Name });
    public static (string Name, TextCodec Codec, byte[] Bytes) Get(string name) => All().First(c => c.Name == name);
}

public class ByteFidelityTests
{
    [Theory, MemberData(nameof(EncodingCorpus.Names), MemberType = typeof(EncodingCorpus))]
    public void OpenWithoutChangeSavesIdenticalBytes(string name)
    {
        var (_, codec, bytes) = EncodingCorpus.Get(name);
        var doc = codec.Decode(bytes);
        Assert.Equal(bytes, ByteFidelity.Save(doc, []));
        Assert.Equal(bytes, ByteFidelity.Save(doc, [new TextEdit(0, 0, "")]));
    }

    [Theory, MemberData(nameof(EncodingCorpus.Names), MemberType = typeof(EncodingCorpus))]
    public void ChangingOneCharacterDiffersOnlyInThatRange(string name)
    {
        var (_, codec, bytes) = EncodingCorpus.Get(name);
        var doc = codec.Decode(bytes);
        int k = doc.Text.IndexOf('b') is var i and >= 0 ? i : doc.Text.IndexOf('a');
        var edit = new TextEdit(k, 1, "X");
        var plan = ByteFidelity.PlanSave(doc, [edit]);
        var result = plan.ToBytes();
        var diff = ByteDiff.Compute(bytes, result);
        long from = doc.ByteOffsetOf(k), to = doc.ByteOffsetOf(k + 1);
        Assert.False(diff.IsEmpty);
        Assert.InRange(diff.FirstDifference, from, to);
        Assert.True(diff.FirstDifference + diff.OldChanged <= to);
        Assert.Equal(bytes.Length - (to - from) + codec.Encode("X").Length, result.Length);
        Assert.Equal(bytes.AsSpan(0, (int)from).ToArray(), result.AsSpan(0, (int)from).ToArray());
        Assert.Equal(bytes.AsSpan((int)to).ToArray(), result.AsSpan((int)from + codec.Encode("X").Length).ToArray());
        Assert.Equal(plan.DiffFromSource(), diff);
    }

    [Theory, MemberData(nameof(EncodingCorpus.Names), MemberType = typeof(EncodingCorpus))]
    public void BlockwiseDecodeEqualsOneShotForEveryBlockSize(string name)
    {
        var (_, codec, bytes) = EncodingCorpus.Get(name);
        var whole = codec.Decode(bytes);
        for (int block = 4; block <= 41; block++)
        {
            var parts = codec.DecodeInBlocks(bytes, block);
            Assert.Equal(whole.Text, parts.Text);
            for (int i = 0; i <= whole.Text.Length; i++) Assert.Equal(whole.ByteOffsetOf(i), parts.ByteOffsetOf(i));
            Assert.Equal(whole.InvalidCount, parts.InvalidCount);
        }
    }

    [Fact]
    public void MultibyteCharacterAcrossBlockBoundaryIsDecodedOnce()
    {
        foreach (var codec in new[] { TextCodec.Utf8, TextCodec.Utf16LE, TextCodec.Utf16BE, TextCodec.Utf32LE })
            for (int pad = 0; pad < 8; pad++)
            {
                var bytes = codec.Encode(new string('a', pad) + "č😀ř");
                var doc = codec.DecodeInBlocks(bytes, 5);
                Assert.Equal(new string('a', pad) + "č😀ř", doc.Text);
                Assert.Equal(0, doc.InvalidCount);
            }
    }

    [Fact]
    public void InvalidSequencesShowAsReplacementCharAndKeepTheirBytes()
    {
        var (_, codec, bytes) = EncodingCorpus.Get("broken-utf8");
        var doc = codec.Decode(bytes);
        Assert.Equal("ab�(cd�ef\n�", doc.Text);
        Assert.Equal(3, doc.InvalidCount);
        Assert.True(doc.IsInvalid(2) && doc.IsInvalid(6) && doc.IsInvalid(10) && !doc.IsInvalid(3));
        Assert.Equal(2, doc.ByteOffsetOf(2));
        Assert.Equal(bytes.Length, doc.ByteOffsetOf(doc.Text.Length));

        var saved = ByteFidelity.Save(doc, [new TextEdit(0, 2, "XY"), new TextEdit(7, 2, "z")]);
        Assert.Equal([.. "XY"u8, 0xC3, 0x28, .. "cd"u8, 0x80, .. "z\n"u8, 0xE2, 0x82], saved);
        Assert.Equal(-1, saved.AsSpan().IndexOf(new byte[] { 0xEF, 0xBF, 0xBD }));
    }

    [Theory]
    [InlineData("utf16-odd-tail", 1)]
    [InlineData("utf16-lone-surrogate", 1)]
    [InlineData("utf32-short-tail", 1)]
    [InlineData("utf32-out-of-range", 1)]
    public void MalformedWideTextKeepsItsBytes(string name, int invalidCount)
    {
        var (_, codec, bytes) = EncodingCorpus.Get(name);
        var doc = codec.Decode(bytes);
        Assert.Equal(invalidCount, doc.InvalidCount);
        var saved = ByteFidelity.Save(doc, [new TextEdit(0, 1, "Q")]);
        Assert.Equal(bytes.Length, saved.Length);
        Assert.Equal(bytes.AsSpan(codec.Encode("a").Length).ToArray(), saved.AsSpan(codec.Encode("Q").Length).ToArray());
    }

    [Fact]
    public void ReinterpretKeepsTheBytesUntouched()
    {
        var (_, _, bytes) = EncodingCorpus.Get("utf8-bom");
        var utf8 = TextCodec.Utf8Bom.Decode(bytes);
        var cp = utf8.Reinterpret(TextCodec.Cp1250);
        Assert.NotEqual(utf8.Text, cp.Text);
        Assert.StartsWith("ď»ż", cp.Text); // BOM bytes are ordinary characters under another codec
        Assert.True(utf8.Bytes.Span.SequenceEqual(bytes) && cp.Bytes.Span.SequenceEqual(bytes));
        Assert.Equal(bytes, ByteFidelity.Save(cp, []));
    }

    [Fact]
    public void KamenickyTableIsOurOwnBijectionWithAsciiBottomHalf()
    {
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var doc = TextCodec.Kamenicky.Decode(all);
        Assert.Equal(256, doc.Text.Distinct().Count());
        for (int i = 0; i < 128; i++) Assert.Equal((char)i, doc.Text[i]);
        Assert.Equal([0xA9, 0x87, 0x80, 0x91, 0x88, 0xA8], TextCodec.Kamenicky.Encode("řčČžěš"));
        Assert.Equal(Czech2, TextCodec.Kamenicky.Decode(TextCodec.Kamenicky.Encode(Czech2)).Text);
        Assert.Equal(all, TextCodec.Kamenicky.Encode(doc.Text));
    }

    private const string Czech2 = "ÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž";
}

public class EncodingGuessTests
{
    [Fact]
    public void EmptyFileGetsTheDefault()
    {
        Assert.Same(TextCodec.Utf8, EncodingGuesser.Guess([], true).Codec);
        Assert.Same(TextCodec.Cp1250, EncodingGuesser.Guess([], true, TextCodec.Cp1250).Codec);
        Assert.False(EncodingGuesser.Guess([], true).IsUncertain);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf32le-bom")]
    [InlineData("utf32be-bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    [InlineData("utf8-cz")]
    [InlineData("cp1250")]
    [InlineData("cp1252")]
    [InlineData("latin2")]
    [InlineData("kamenicky")]
    public void FixturesAreDetectedExactlyAndConfidently(string name)
    {
        var (_, codec, bytes) = EncodingCorpus.Get(name);
        var g = EncodingGuesser.Guess(bytes, true);
        Assert.Same(codec, g.Codec);
        Assert.False(g.IsUncertain);
    }

    [Fact]
    public void AsciiIsCertainAndMapsToUtf8()
    {
        var g = EncodingGuesser.Guess("hello"u8, true);
        Assert.Same(TextCodec.Utf8, g.Codec);
        Assert.Equal(1.0, g.Confidence);
    }

    [Fact]
    public void AmbiguousSingleByteTextAdmitsUncertaintyAndOffersAlternatives()
    {
        var g = EncodingGuesser.Guess([(byte)'c', (byte)'a', (byte)'f', 0xE9], true);
        Assert.True(g.IsUncertain);
        Assert.Contains(TextCodec.Cp1252, g.Alternatives.Append(g.Codec));
        Assert.NotEmpty(g.Alternatives);
    }

    [Fact]
    public void SingleMultibyteUtf8SequenceIsLessSureThanPlentyOfThem()
    {
        var one = EncodingGuesser.Guess("až b"u8.ToArray(), true);
        var many = EncodingGuesser.Guess("příliš žluťoučký"u8.ToArray(), true);
        Assert.Same(TextCodec.Utf8, one.Codec);
        Assert.True(one.Confidence < many.Confidence);
    }

    [Fact]
    public void TruncatedUtf8SampleStillDetectsUtf8()
    {
        var bytes = TextCodec.Utf8.Encode(string.Concat(Enumerable.Repeat("příliš žluťoučký kůň ", 20)));
        for (int cut = 100; cut < 140; cut++)
            Assert.Same(TextCodec.Utf8, EncodingGuesser.Guess(bytes.AsSpan(0, cut), wholeFile: false).Codec);
    }

    [Fact]
    public void CzechTextInOtherEightBitPagesIsNotMistakenForCp1250()
    {
        foreach (var codec in new[] { TextCodec.Latin2, TextCodec.Kamenicky })
            Assert.Same(codec, EncodingGuesser.Guess(codec.Encode("Příliš žluťoučký kůň úpěl ďábelské ódy"), true).Codec);
    }
}

public class LossWarningTests
{
    [Fact]
    public void ConvertingToCp1250ReportsOnlyTheEmoji()
    {
        var doc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("řč\U0001F600\nx"));
        var plan = ByteFidelity.PlanConvert(doc, TextCodec.Cp1250);
        Assert.True(plan.IsBlocked);
        var e = Assert.Single(plan.Loss.Entries);
        Assert.Equal(0x1F600, e.CodePoint);
        Assert.Equal((2, 1, 3, 2L), (e.CharIndex, e.Line, e.Column, e.ByteOffset));
        Assert.Equal(1, plan.Loss.Total);
        Assert.Throws<UnrepresentableCharactersException>(() => plan.ToBytes());
        Assert.Throws<UnrepresentableCharactersException>(() => ByteFidelity.Convert(doc, TextCodec.Cp1250));
        Assert.Equal([0xF8, 0xE8, (byte)'?', (byte)'\n', (byte)'x'], ByteFidelity.Convert(doc, TextCodec.Cp1250, "?"));

        var ok = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("řč"));
        Assert.Equal([0xF8, 0xE8], ByteFidelity.Convert(ok, TextCodec.Cp1250));
    }

    [Fact]
    public void TargetListMarksLossyCodecsBeforeSelection()
    {
        var doc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("řč\U0001F600"));
        var lost = ByteFidelity.ListTargets(doc).ToDictionary(t => t.Codec.Name, t => t.Lost);
        Assert.Equal(1, lost["Windows-1250"]);
        Assert.Equal(3, lost["Windows-1252"]);
        Assert.Equal(0, lost["UTF-16 LE"]);
        Assert.Equal(0, lost["UTF-8 with BOM"]);
    }

    [Fact]
    public void SavingCzechLetterIntoCp1252ListsPositionsAndNeverWritesAnythingSilently()
    {
        var bytes = TextCodec.Cp1252.Encode("abc\ndef\n");
        var doc = TextCodec.Cp1252.Decode(bytes);
        var plan = ByteFidelity.PlanSave(doc, [new TextEdit(5, 0, "č"), new TextEdit(7, 0, "čč")]);
        Assert.True(plan.IsBlocked);
        Assert.Equal(3, plan.Loss.Total);
        Assert.Equal([(2, 2), (2, 5), (2, 6)], plan.Loss.Entries.Select(x => (x.Line, x.Column)));
        Assert.Equal([5L, 8L, 9L], plan.Loss.Entries.Select(x => x.ByteOffset));
        Assert.Equal(new LossGroup('č', 3), Assert.Single(plan.Loss.Groups));
        Assert.Throws<UnrepresentableCharactersException>(() => plan.WriteTo(new MemoryStream()));
        Assert.Throws<UnrepresentableCharactersException>(() => ByteFidelity.Save(doc, [new TextEdit(5, 0, "č")]));

        var chosen = ByteFidelity.Save(doc, [new TextEdit(5, 0, "č")], "?");
        Assert.Equal("abc\nd?ef\n"u8.ToArray(), chosen);
        Assert.Throws<ArgumentException>(() => ByteFidelity.Save(doc, [new TextEdit(5, 0, "č")], "ř"));
    }

    [Fact]
    public void ConvertingTextWithInvalidBytesFlagsThemAsLoss()
    {
        var doc = TextCodec.Utf8.Decode(new byte[] { (byte)'a', 0xC3, 0x28 });
        var plan = ByteFidelity.PlanConvert(doc, TextCodec.Utf16LE);
        Assert.True(plan.IsBlocked);
        Assert.True(plan.Loss.Entries[0].FromInvalidBytes);
    }

    [Fact]
    public void ConvertWithOnlyABomChangeKeepsEveryOtherByte()
    {
        var (_, _, bytes) = EncodingCorpus.Get("broken-utf8");
        var doc = TextCodec.Utf8.Decode(bytes);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. bytes], ByteFidelity.Convert(doc, TextCodec.Utf8Bom));
    }

    [Fact]
    public void ConvertBetweenUnicodeFormsRoundTrips()
    {
        var doc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode(EncodingCorpus.Czech + "\U0001F600"));
        foreach (var target in new[] { TextCodec.Utf16LEBom, TextCodec.Utf16BE, TextCodec.Utf32BEBom, TextCodec.Utf32LE })
        {
            var converted = ByteFidelity.Convert(doc, target);
            var back = target.Decode(converted);
            Assert.Equal(doc.Text, back.Text);
            Assert.Equal(doc.Bytes.ToArray(), ByteFidelity.Convert(back, TextCodec.Utf8));
        }
    }

    [Fact]
    public void ConvertShowsChangedBytesInThePreview()
    {
        var doc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("abc ř"));
        var diff = ByteFidelity.PlanConvert(doc, TextCodec.Cp1250).DiffFromSource();
        Assert.Equal((4L, 2L, 1L), (diff.FirstDifference, diff.OldChanged, diff.NewChanged));
    }
}

public class EolTests
{
    [Fact]
    public void MixedEolIsReportedAndPreservedByteForByte()
    {
        var (_, codec, bytes) = EncodingCorpus.Get("mixed-eol");
        var doc = codec.Decode(bytes);
        var stats = Eol.Analyze(doc.Text);
        Assert.Equal((2, 1, 1), (stats.CrLf, stats.Lf, stats.Cr));
        Assert.True(stats.IsMixed);
        Assert.Equal("mixed", stats.Label);
        int k = doc.Text.IndexOf('c');
        var saved = ByteFidelity.Save(doc, [new TextEdit(k, 1, "CC")]);
        Assert.Equal("a\r\nb\nCC\rd\r\ne"u8.ToArray(), saved);
    }

    [Fact]
    public void DetectionNamesEachStyle()
    {
        Assert.Equal(EolStyle.Lf, Eol.Analyze("a\nb\n").Style);
        Assert.Equal(EolStyle.CrLf, Eol.Analyze("a\r\nb").Style);
        Assert.Equal(EolStyle.Cr, Eol.Analyze("a\rb\r").Style);
        Assert.Equal(EolStyle.None, Eol.Analyze("abc").Style);
        Assert.Equal(EolStyle.CrLf, Eol.Analyze("a\r\nb\r\nc\n").Dominant);
    }

    [Fact]
    public void CrLfSplitAcrossBlocksIsCountedOnceAtEverySplit()
    {
        const string text = "a\r\nb\r\n\r\nc\rd\ne\r";
        var whole = Eol.Analyze(text);
        for (int cut = 0; cut <= text.Length; cut++)
        {
            var s = new EolScanner();
            s.Add(text.AsSpan(0, cut));
            s.Add(text.AsSpan(cut));
            Assert.Equal(whole, s.Finish());
        }
        Assert.Equal((1, 3, 2), (whole.Lf, whole.CrLf, whole.Cr));
    }

    [Fact]
    public void CrLfSplitAcrossDecodeBlocksIsStillOneLineEnding()
    {
        var bytes = TextCodec.Utf8.Encode("ab\r\ncd\r\n");
        for (int block = 4; block < 9; block++)
            Assert.Equal(new EolStats(0, 2, 0), Eol.Analyze(TextCodec.Utf8.DecodeInBlocks(bytes, block).Text));
    }

    [Theory]
    [InlineData(EolStyle.Lf, "a\nb\nc\nd\ne", 3, 1)]
    [InlineData(EolStyle.CrLf, "a\r\nb\r\nc\r\nd\r\ne", 2, 2)]
    [InlineData(EolStyle.Cr, "a\rb\rc\rd\re", 3, 1)]
    public void ExplicitUnificationPreviewsExactCountsAndChangesOnlyLineEndings(EolStyle target, string expected, int changed, int unchanged)
    {
        var (_, codec, bytes) = EncodingCorpus.Get("mixed-eol");
        var doc = codec.Decode(bytes);
        var preview = Eol.PreviewNormalize(doc, target);
        Assert.Equal((changed, unchanged), (preview.Changed, preview.Unchanged));
        var plan = ByteFidelity.PlanSave(doc, preview.Edits);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), plan.ToBytes());
        Assert.True(plan.DiffFromSource().FirstDifference >= doc.ByteOffsetOf(preview.Edits[0].Start));

        // undo = the inverse edit restores the original bytes
        var after = codec.Decode(plan.ToBytes());
        Assert.Equal(bytes, ByteFidelity.Save(after, [TextEdit.FromDiff(after.Text, doc.Text)]));
    }

    [Fact]
    public void UnificationWorksInUtf16AndTouchesNothingElse()
    {
        var doc = TextCodec.Utf16BEBom.Decode((byte[])[.. TextCodec.Utf16BEBom.Bom, .. TextCodec.Utf16BE.Encode("a\nb\r\n")]);
        var saved = ByteFidelity.Save(doc, Eol.PreviewNormalize(doc, EolStyle.CrLf).Edits);
        Assert.Equal([.. TextCodec.Utf16BEBom.Bom, .. TextCodec.Utf16BE.Encode("a\r\nb\r\n")], saved);
    }

    [Fact]
    public void NewLinesUseConfiguredThenDetectedThenFallback()
    {
        var crlf = Eol.Analyze("a\r\nb\r\n");
        Assert.Equal("\r\n", Eol.ForNewLine(crlf));
        Assert.Equal("\n", Eol.ForNewLine(crlf, EolStyle.Lf));
        Assert.Equal("\r", Eol.ForNewLine(Eol.Analyze("x"), null, EolStyle.Cr));
        Assert.Equal("\n", Eol.ForNewLine(Eol.Analyze("x"), EolStyle.Mixed));
    }

    [Fact]
    public void UnicodeNormalizationIsExplicitAndRangeExact()
    {
        string nfd = "café\nok\né";
        var doc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode(nfd));
        var toNfc = UnicodeNormalizer.Preview(doc, NormalizationForm.FormC);
        Assert.Equal([new TextEdit(0, 5, "café"), new TextEdit(9, 2, "é")], toNfc);
        Assert.Equal(TextCodec.Utf8.Encode("café\nok\né"), ByteFidelity.Save(doc, toNfc));

        var nfc = TextCodec.Utf8.Decode(TextCodec.Utf8.Encode("café\nok"));
        var toNfd = UnicodeNormalizer.Preview(nfc, NormalizationForm.FormD);
        Assert.Equal(new TextEdit(0, 4, "café"), Assert.Single(toNfd));
        Assert.Empty(UnicodeNormalizer.Preview(TextCodec.Utf8.Decode("plain"u8.ToArray()), NormalizationForm.FormC));
    }

    [Fact]
    public void NormalizationLeavesInvalidBytesAndBlocksLossyTargets()
    {
        var broken = TextCodec.Utf8.Decode((byte[])[.. "e"u8, 0xCC, 0x81, 0xFF, .. "e"u8, 0xCC, 0x81]);
        var edits = UnicodeNormalizer.Preview(broken, NormalizationForm.FormC);
        Assert.Equal([.. "é"u8.ToArray(), 0xFF, .. "é"u8.ToArray()], ByteFidelity.Save(broken, edits));

        var cp = TextCodec.Cp1250.Decode(TextCodec.Cp1250.Encode("café"));
        Assert.True(ByteFidelity.PlanSave(cp, UnicodeNormalizer.Preview(cp, NormalizationForm.FormD)).IsBlocked);
    }
}

using System.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>P1.2.7 and P1.2.10 over the AvaloniaEdit adapter: the same contract suite, plus the byte checks that need its text control.</summary>
public class AvaloniaEditContractTests : DocumentContractSuite
{
    protected override IBackendFactory Factory => AvaloniaEditBackendFactory.Instance;
}

public class AvaloniaEditByteFidelityGate : ByteFidelityGateSuite
{
    protected override IBackendFactory Factory => AvaloniaEditBackendFactory.Instance;
}

public class AvaloniaEditEncodingOperations : DocumentEncodingSuite
{
    protected override IBackendFactory Factory => AvaloniaEditBackendFactory.Instance;
}

public class CrossBackendTests
{
    private static readonly IBackendFactory[] All = [ReferenceBackendFactory.Instance, AvaloniaEditBackendFactory.Instance, PagedBackendFactory.Instance];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void ReferencePagedAndAvaloniaEditAgreeWithTheNaiveModelOverThousandsOfEdits(int seed) => RandomizedParity.Run(seed, 3000, All);

    [Fact]
    public void AnUnsavedUnnamedDocumentMovesBetweenAdaptersInBothDirections()
    {
        BackendSwitch.RoundTrip(AvaloniaEditBackendFactory.Instance, PagedBackendFactory.Instance);
        BackendSwitch.RoundTrip(PagedBackendFactory.Instance, AvaloniaEditBackendFactory.Instance);
        BackendSwitch.RoundTrip(AvaloniaEditBackendFactory.Instance, ReferenceBackendFactory.Instance, PagedBackendFactory.Instance);
    }

    [Fact]
    public void TheSameDataAndOperationsGiveTheSameBytesOnEveryAdapter()
    {
        byte[] data = [0xEF, 0xBB, 0xBF, .. "a\r\nb\nc\rd"u8, 0xE2, 0x82, (byte)'x', 0xFF, 0xC3, 0xA9, 0x80, .. "\r\n\r"u8];
        var docs = All.Select(f => Doc.FromBytes(data, f, new DocumentOptions { Format = TestFormat.Utf8Bom.Format })).ToArray();
        try
        {
            foreach (var d in docs)
            {
                d.Insert(3, "Ž"u8.ToArray());
                d.Delete(new(6, 8));
                d.Replace(new(10, 11), "€"u8.ToArray());
                d.Undo();
                d.Redo();
                d.Insert(d.ByteLength, new byte[] { 0xFF, 0xFE });
            }
            var expected = docs[0].ReadBytes(new(0, docs[0].ByteLength));
            foreach (var d in docs) Assert.Equal(expected, d.ReadBytes(new(0, d.ByteLength)));
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }
}

public class AdapterTests
{
    private static AvaloniaEditBackend Backend(Doc d) => (AvaloniaEditBackend)d.Backend;

    [Fact]
    public void TypingIntoTheTextControlEditsBytesOnlyWhereTheUserTyped()
    {
        foreach (var f in TestFormat.All)
        {
            byte[] body = f.IsUtf16 ? [0x61, 0, 0x0D, 0, 0x0A, 0, 0x00, 0xD8, 0x62, 0, 0x41] : f == TestFormat.Cp1250 ? [.. "a\r\nb"u8, 0x81, 0xE9, .. "\n"u8] : [.. "a\r\nb"u8, 0xE2, 0x82, 0xFF, 0xC3, 0xA9, .. "\n"u8];
            if (f.IsUtf16 && f == TestFormat.Utf16BE) body = [0, 0x61, 0, 0x0D, 0, 0x0A, 0xD8, 0x00, 0, 0x62, 0x41];
            byte[] original = [.. f.Bom, .. body];
            using var d = Doc.FromBytes(original, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = f.Format });
            var text = Backend(d).TextDocument;
            var changes = new List<DocumentChangedEventArgs>();
            d.Changed += (_, e) => changes.Add(e);

            text.Replace(0, 1, "Z");
            Assert.Single(changes);
            Assert.Equal(ChangeKind.Backend, changes[0].Change.Kind);
            Assert.Equal(1, d.Revision);
            Assert.True(d.CanUndo);
            byte[] expected = [.. f.Bom, .. f.Raw("Z"), .. body.AsSpan(f.IsUtf16 ? 2 : 1)];
            Assert.Equal(expected, d.ReadBytes(new(0, d.ByteLength)));

            text.Insert(text.TextLength, f == TestFormat.Cp1250 ? "č" : "€");
            Assert.Equal(2, d.Revision);
            Assert.Equal([.. expected, .. f.Raw(f == TestFormat.Cp1250 ? "č" : "€")], d.ReadBytes(new(0, d.ByteLength)));

            d.Undo();
            d.Undo();
            Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
            Assert.Equal(f.Enc.GetString(body), text.Text);
            Assert.False(d.CanUndo);
        }
    }

    [Fact]
    public void ReReadConvertAndTheirUndoKeepTheTextControlInStepWithTheBytes()
    {
        const string crlf = "příliš\r\nžlutý";
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(crlf)];
        using var d = Doc.FromBytes(original, AvaloniaEditBackendFactory.Instance);
        var text = Backend(d).TextDocument;
        Assert.Equal(crlf, text.Text);

        d.ReinterpretAs(TextCodec.Cp1250);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal(Encoding.GetEncoding(1250).GetString(original), text.Text);
        d.ReinterpretAs(TextCodec.Utf8Bom);
        Assert.Equal(crlf, text.Text);

        Assert.True(d.ConvertTo(TextCodec.Utf16BE).Applied);
        Assert.Equal(crlf, text.Text);
        Assert.Equal(new UnicodeEncoding(true, false).GetBytes(crlf), d.ReadBytes(new(0, d.ByteLength)));
        text.Replace(0, 1, "P");
        d.Undo();
        d.Undo();
        Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
        Assert.Equal(crlf, text.Text);
        Assert.Same(TextCodec.Utf8Bom, d.Codec);

        Assert.True(d.UnifyEol(Core.Encodings.EolStyle.Lf).Applied);
        Assert.Equal("příliš\nžlutý", text.Text);
        d.Undo();
        Assert.Equal(crlf, text.Text);
    }

    [Fact]
    public void OpenTypeOneCharacterSaveWritesOnlyThatRange()
    {
        using var dir = new TempDir();
        string path = dir.File("a.txt");
        byte[] original = [0xEF, 0xBB, 0xBF, .. "a\r\nb\nc\rd"u8, 0xE2, 0x82, (byte)'x', 0xFF, 0xC3, 0xA9, 0x80, .. "\r\n\r"u8];
        File.WriteAllBytes(path, original);
        using var d = Doc.Open(path, AvaloniaEditBackendFactory.Instance);
        Assert.Same(TextCodec.Utf8Bom, d.Codec);
        Assert.True(d.Save().Saved);
        Assert.Equal(original, File.ReadAllBytes(path));

        var text = Backend(d).TextDocument;
        int at = text.Text.IndexOf('x');
        text.Replace(at, 1, "y");
        Assert.True(d.Save().Saved);
        var saved = File.ReadAllBytes(path);
        Assert.Equal(original.Length, saved.Length);
        for (int i = 0; i < saved.Length; i++)
            Assert.Equal(i == 3 + 8 + 2 ? (byte)'y' : original[i], saved[i]);
    }

    [Fact]
    public void EveryTextOffsetConvertsToTheSameByteAsTheReferenceCalculationAfterTypedEdits()
    {
        var r = new Random(5);
        string model = "ahoj\r\nsvět €\r\n😀 x\n";
        using var d = Doc.FromBytes(Encoding.UTF8.GetBytes(model), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var text = Backend(d).TextDocument;
        string[] atoms = ["a", "é", "€", "😀", "\r\n", "\n", "x"];
        for (int step = 0; step < 300; step++)
        {
            int at = r.Next(model.Length + 1);
            while (at > 0 && at < model.Length && char.IsLowSurrogate(model[at])) at--;
            int len = Math.Min(model.Length - at, r.Next(0, 4));
            if (len > 0 && char.IsHighSurrogate(model[at + len - 1])) len = Math.Min(model.Length - at, len + 1);
            string ins = r.Next(3) == 0 ? "" : atoms[r.Next(atoms.Length)];
            text.Replace(at, len, ins);
            model = model[..at] + ins + model[(at + len)..];
        }
        Assert.Equal(model, text.Text);
        Assert.Equal(Encoding.UTF8.GetBytes(model), d.ReadBytes(new(0, d.ByteLength)));
        for (int c = 0; c <= model.Length; c++)
        {
            if (c > 0 && c < model.Length && (char.IsLowSurrogate(model[c]) || (model[c - 1] == '\r' && model[c] == '\n'))) continue;
            long expected = Encoding.UTF8.GetByteCount(model[..c]);
            var line = text.GetLineByOffset(c);
            Assert.True(d.TryGetOffset(new(line.LineNumber - 1, c - line.Offset), out long got));
            Assert.Equal(expected, got);
            Assert.True(d.TryGetPosition(expected, out var pos));
            Assert.Equal(new LinePosition(line.LineNumber - 1, c - line.Offset), pos);
        }
    }

    [Fact]
    public void ADeletedHalfOfASurrogatePairBecomesReplacementBytesAndNothingElseChanges()
    {
        using var d = Doc.FromBytes("a😀b"u8.ToArray(), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var text = Backend(d).TextDocument;
        text.Remove(2, 1);
        Assert.Equal(1, d.Revision);
        Assert.Equal([(byte)'a', 0xEF, 0xBF, 0xBD, (byte)'b'], d.ReadBytes(new(0, d.ByteLength)));
        d.Undo();
        Assert.Equal("a😀b"u8.ToArray(), d.ReadBytes(new(0, d.ByteLength)));
        Assert.Equal("a😀b", text.Text);
    }

    [Fact]
    public void CharactersTheCodePageCannotHoldAreReplacedOnlyInTheTypedText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var f = TestFormat.Cp1250;
        byte[] original = [.. "ab"u8, 0x81, 0xE9];
        using var d = Doc.FromBytes(original, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = f.Format });
        Backend(d).TextDocument.Insert(1, "😀");
        Assert.Equal([(byte)'a', (byte)'?', (byte)'?', (byte)'b', 0x81, 0xE9], d.ReadBytes(new(0, d.ByteLength)));
        Backend(d).TextDocument.Insert(Backend(d).TextDocument.TextLength, "x");
        Assert.Equal([(byte)'a', (byte)'?', (byte)'?', (byte)'b', 0x81, 0xE9, (byte)'x'], d.ReadBytes(new(0, d.ByteLength)));
    }

    [Fact]
    public void UnicodeLineSeparatorsAreNotLineBreaksJustLikeInTheByteContract()
    {
        using var d = Doc.FromBytes("a\u2028b\u0085c\nd"u8.ToArray(), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.Equal(2, d.LineIndexState.Lines);
        Assert.True(d.TryGetOffset(new(1, 0), out long start));
        Assert.Equal((long)"a\u2028b\u0085c\n"u8.Length, start);
    }

    [Fact]
    public void ALargeFileKeepsEditingCheapBecauseOnlyTouchedRunsAreDecoded()
    {
        byte[] line = "řádek s českým textem a emoji 😀\r\n"u8.ToArray();
        var data = new byte[line.Length * 60000];
        for (int i = 0; i < 60000; i++) line.CopyTo(data, i * line.Length);
        using var d = Doc.FromBytes(data, AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        var text = Backend(d).TextDocument;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) text.Insert(text.GetLineByNumber(30000 + i).Offset, "x");
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds} ms for 200 typed characters");
        Assert.Equal(data.Length + 200, d.ByteLength);
        Assert.Equal(200, d.Revision);
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(3072)]
    public void ARangeStartingExactlyOnAMapCheckpointCanBeReadAndReplaced(int at)
    {
        using var d = Doc.FromBytes(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("foo ", 1500))), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.Equal("foo ", d.ReadText(new ByteRange(at, at + 4)));
        Assert.True(d.Replace(new ByteRange(at, at + 3), "bar"u8.ToArray()).Applied);
        Assert.Equal("bar ", d.ReadText(new ByteRange(at, at + 4)));
        Assert.Equal("bar", Backend(d).TextDocument.GetText(at, 3));
    }
}

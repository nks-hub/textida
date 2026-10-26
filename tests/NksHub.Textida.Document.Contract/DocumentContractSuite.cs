using System.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Document.Contract;

/// <summary>
/// Backend independent tests of the byte contract (P1.2.1). A backend passes by deriving from this class and naming its factory;
/// every expectation is spelled out as bytes or computed from strings, never from the code under test.
/// </summary>
public abstract partial class DocumentContractSuite
{
    protected abstract IBackendFactory Factory { get; }

    protected bool Supports(TestFormat f) => !f.IsUtf16 || Factory.Capabilities.SupportsUtf16;
    protected IEnumerable<TestFormat> Formats => TestFormat.All.Where(Supports);

    protected NksHub.Textida.Core.Documents.Document New(byte[] bytes, TestFormat? format = null, int logCapacity = 4096) =>
        NksHub.Textida.Core.Documents.Document.FromBytes(bytes, Factory, new DocumentOptions { Format = (format ?? TestFormat.Utf8).Format, ChangeLogCapacity = logCapacity });

    protected static byte[] Bytes(NksHub.Textida.Core.Documents.Document d) => d.ReadBytes(new(0, d.ByteLength));

    protected static void WaitIndex(NksHub.Textida.Core.Documents.Document d)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!d.LineIndexState.Complete)
        {
            Assert.True(DateTime.UtcNow < until, "line index did not complete");
            Thread.Sleep(1);
        }
    }

    protected static ByteRange Chars(TestFormat f, string text, int from, int to) =>
        new(f.Bom.Length + f.Enc.GetByteCount(text[..from]), f.Bom.Length + f.Enc.GetByteCount(text[..to]));

    // ---- bytes ----

    [Fact]
    public void InsertDeleteReplaceUndoRedoGiveExpectedBytes()
    {
        foreach (var f in Formats)
        {
            string text = "příliš\r\nkůň";
            using var d = New(f.Bytes(text), f);
            Assert.Equal(f.Bytes(text), Bytes(d));

            string t1 = "příliš!\r\nkůň";
            Assert.True(d.Insert(Chars(f, text, 6, 6).Start, f.Raw("!")).Applied);
            Assert.Equal(f.Bytes(t1), Bytes(d));

            string t2 = "příliš!\r\nkň";
            Assert.True(d.Delete(Chars(f, t1, 10, 11)).Applied);
            Assert.Equal(f.Bytes(t2), Bytes(d));

            string t3 = "příliš!\r\nkňň";
            Assert.True(d.Replace(Chars(f, t2, 11, 11), f.Raw("ň")).Applied);
            Assert.Equal(f.Bytes(t3), Bytes(d));
            Assert.Equal(t3, d.ReadText(new(f.Bom.Length, d.ByteLength)));

            d.Undo();
            Assert.Equal(f.Bytes(t2), Bytes(d));
            d.Undo();
            Assert.Equal(f.Bytes(t1), Bytes(d));
            d.Undo();
            Assert.Equal(f.Bytes(text), Bytes(d));
            Assert.False(d.CanUndo);
            d.Redo();
            d.Redo();
            d.Redo();
            Assert.Equal(f.Bytes(t3), Bytes(d));
            Assert.False(d.CanRedo);
        }
    }

    [Fact]
    public void ChangingOneCharKeepsEveryOtherByteIncludingInvalidSequencesAndMixedEol()
    {
        byte[] utf8 = [.. "a\r\nb\nc\rd"u8, 0xE2, 0x82, (byte)'x', 0xFF, 0xC3, 0xA9, 0x80, .. "\r\n\r"u8];
        byte[] cp1250 = [.. "a\r\nb\nc\rd"u8, 0x81, 0x8D, 0x90, 0xE9, 0xFF, .. "\r\n\r"u8];
        foreach (var (f, body) in new[] { (TestFormat.Utf8, utf8), (TestFormat.Utf8Bom, utf8), (TestFormat.Cp1250, cp1250) })
        {
            byte[] original = [.. f.Bom, .. body];
            using var d = New(original, f);
            Assert.Equal(original, Bytes(d));
            var range = new ByteRange(f.Bom.Length, f.Bom.Length + 1);
            Assert.True(d.Replace(range, "Z"u8.ToArray()).Applied);
            byte[] expected = (byte[])original.Clone();
            expected[f.Bom.Length] = (byte)'Z';
            Assert.Equal(expected, Bytes(d));
            Assert.Equal(expected.Length, d.ByteLength);
        }
    }

    [Fact]
    public void Utf16InvalidUnitsSurviveAnEdit()
    {
        if (!Supports(TestFormat.Utf16LE)) return;
        byte[] body = [(byte)'a', 0, 0x00, 0xD8, (byte)'b', 0, 0x00, 0xDC, 0x3D, 0xD8, 0x00, 0xDE, 0x41];
        byte[] original = [.. TestFormat.Utf16LE.Bom, .. body];
        using var d = New(original, TestFormat.Utf16LE);
        Assert.True(d.Replace(new(2, 4), new byte[] { (byte)'Q', 0 }).Applied);
        byte[] expected = [.. TestFormat.Utf16LE.Bom, (byte)'Q', 0, .. body.AsSpan(2)];
        Assert.Equal(expected, Bytes(d));
    }

    [Fact]
    public void EditsOutsideCharacterBoundariesAreRejectedAndChangeNothing()
    {
        using var d = New("é"u8.ToArray());
        Assert.False(d.IsCharBoundary(1));
        var r = d.Insert(1, "x"u8.ToArray());
        Assert.False(r.Applied);
        Assert.Equal(ConflictReason.InvalidRange, r.Conflict);
        Assert.Equal(ConflictReason.InvalidRange, d.Delete(new(0, 1)).Conflict);
        Assert.Equal(ConflictReason.InvalidRange, d.Delete(new(1, 2)).Conflict);
        Assert.Equal(0, d.Revision);
        Assert.Equal("é"u8.ToArray(), Bytes(d));
        Assert.Throws<ArgumentException>(() => d.ReadText(new(0, 1)));
        Assert.Equal(ConflictReason.InvalidRange, d.Insert(3, "x"u8.ToArray()).Conflict);

        if (!Supports(TestFormat.Utf16LE)) return;
        using var u = New(TestFormat.Utf16LE.Bytes("a😀b"), TestFormat.Utf16LE);
        Assert.False(u.IsCharBoundary(3));
        Assert.False(u.IsCharBoundary(6));
        Assert.True(u.IsCharBoundary(4));
        Assert.True(u.IsCharBoundary(8));
        Assert.Equal(ConflictReason.InvalidRange, u.Insert(6, new byte[] { 0x41, 0 }).Conflict);
        Assert.Equal(ConflictReason.InvalidRange, u.Insert(4, new byte[] { 0x41 }).Conflict);
    }

    [Fact]
    public void AnEditThatWouldMergeOrSplitUnitsIsRejected()
    {
        using var d = New([0xE2, 0x82, (byte)'x', 0xAC]);
        Assert.True(d.IsCharBoundary(2));
        Assert.True(d.IsCharBoundary(3));
        Assert.Equal(ConflictReason.InvalidRange, d.Delete(new(2, 3)).Conflict);
        Assert.Equal(ConflictReason.InvalidRange, d.Insert(2, new byte[] { 0xAC }).Conflict);
        Assert.Equal(new byte[] { 0xE2, 0x82, (byte)'x', 0xAC }, Bytes(d));
        Assert.True(d.Insert(2, "y"u8.ToArray()).Applied);

        using var m = New([0xE2, 0x82, 0xAC, (byte)'a']);
        Assert.Equal(ConflictReason.InvalidRange, m.Insert(1, "a"u8.ToArray()).Conflict);
        Assert.True(m.Insert(3, new byte[] { 0xFF }).Applied);
    }

    [Fact]
    public void ABatchWhoseLaterEditIsRejectedLeavesTheDocumentUntouched()
    {
        using var d = New([.. "abc"u8, 0xE2, 0x82, (byte)'x', 0xAC]);
        var r = d.Apply([ByteEdit.InsertAt(0, "Q"u8.ToArray()), ByteEdit.Delete(new(5, 6))]);
        Assert.False(r.Applied);
        Assert.Equal([.. "abc"u8, 0xE2, 0x82, (byte)'x', 0xAC], Bytes(d));
        Assert.Equal(0, d.Revision);
        Assert.False(d.CanUndo);
    }

    [Fact]
    public void BomBytesCannotBeEdited()
    {
        using var d = New(TestFormat.Utf8Bom.Bytes("ab"), TestFormat.Utf8Bom);
        Assert.Equal(ConflictReason.InvalidRange, d.Delete(new(0, 1)).Conflict);
        Assert.Equal(ConflictReason.InvalidRange, d.Insert(1, "x"u8.ToArray()).Conflict);
        Assert.True(d.Insert(3, "x"u8.ToArray()).Applied);
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'x', (byte)'a', (byte)'b'], Bytes(d));
    }

    // ---- positions ----

    private static (long Line, long Col)[] Expected(string text)
    {
        var result = new (long, long)[text.Length + 1];
        long line = 0, col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            result[i] = (line, col);
            if (text[i] == '\n' || (text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n'))) { line++; col = 0; }
            else col++;
        }
        result[text.Length] = (line, col);
        return result;
    }

    private void CheckPositions(NksHub.Textida.Core.Documents.Document d, TestFormat f, string text)
    {
        WaitIndex(d);
        var expected = Expected(text);
        Assert.Equal(expected[^1].Line + 1, d.LineIndexState.Lines);
        for (int i = 0; i <= text.Length; i++)
        {
            long offset = f.Bom.Length + f.Enc.GetByteCount(text[..i]);
            bool splitsPair = i > 0 && i < text.Length && char.IsHighSurrogate(text[i - 1]);
            bool insideCrLf = i > 0 && i < text.Length && text[i - 1] == '\r' && text[i] == '\n';
            if (splitsPair) continue;
            bool ok = d.TryGetPosition(offset, out var p);
            if (insideCrLf)
            {
                Assert.False(ok, $"{f} inside CRLF at char {i}");
                continue;
            }
            Assert.True(ok, $"{f} char {i}");
            Assert.Equal(new LinePosition(expected[i].Line, expected[i].Col), p);
            Assert.True(d.TryGetOffset(p, out long back), $"{f} offset of {p}");
            Assert.Equal(offset, back);
        }
        Assert.False(d.TryGetOffset(new(expected[^1].Line + 1, 0), out _));
        Assert.False(d.TryGetOffset(new(0, 100000), out _));
        Assert.False(d.TryGetPosition(d.ByteLength + 1, out _));
    }

    [Fact]
    public void PositionsMatchTheTextViewForEveryFormat()
    {
        foreach (var f in Formats)
        {
            string text = "ab\r\nčd\n\r\nx\r\ry";
            using var d = New(f.Bytes(text), f);
            CheckPositions(d, f, text);
        }
        foreach (var f in Formats.Where(f => f != TestFormat.Cp1250))
        {
            string text = "a😀b\n😀😀\r\nž";
            using var d = New(f.Bytes(text), f);
            CheckPositions(d, f, text);
        }
    }

    [Fact]
    public void PositionsFollowEditsAndUndo()
    {
        foreach (var f in Formats)
        {
            string text = "ab\ncd\r\nef";
            using var d = New(f.Bytes(text), f);
            WaitIndex(d);
            d.Insert(Chars(f, text, 2, 2).Start, f.Raw("\r\nž\r"));
            string t1 = "ab\r\nž\r\ncd\r\nef";
            CheckPositions(d, f, t1);
            d.Delete(Chars(f, t1, 3, 8));
            CheckPositions(d, f, "ab\rd\r\nef");
            d.Undo();
            CheckPositions(d, f, t1);
            d.Undo();
            CheckPositions(d, f, text);
        }
    }

    [Fact]
    public void InvalidSequencesCountAsOneColumnEach()
    {
        using var d = New([(byte)'a', 0xE2, 0x82, (byte)'b', 0xFF, (byte)'c', (byte)'\n', (byte)'d']);
        WaitIndex(d);
        Assert.True(d.TryGetPosition(3, out var p));
        Assert.Equal(new LinePosition(0, 2), p);
        Assert.True(d.TryGetPosition(5, out p));
        Assert.Equal(new LinePosition(0, 4), p);
        Assert.True(d.TryGetOffset(new(1, 1), out long end));
        Assert.Equal(8, end);
        Assert.Equal("a�b�c\nd", d.ReadText(new(0, d.ByteLength)));
    }

    [Fact]
    public void BomShiftsTheFirstLineStartNotTheColumns()
    {
        foreach (var f in new[] { TestFormat.Utf8Bom }.Concat(Formats.Where(x => x.IsUtf16)))
        {
            using var d = New(f.Bytes("ab\ncd"), f);
            WaitIndex(d);
            Assert.False(d.TryGetPosition(0, out _));
            Assert.True(d.TryGetPosition(f.Bom.Length, out var p));
            Assert.Equal(new LinePosition(0, 0), p);
            Assert.True(d.TryGetOffset(new(0, 0), out long o));
            Assert.Equal(f.Bom.Length, o);
        }
    }

    [Fact]
    public void ReadingAnUntouchedRangeReturnsTheOriginalBytes()
    {
        foreach (var f in Formats)
        {
            string text = "ěščř\r\nžýáí\n";
            var original = f.Bytes(text);
            using var d = New(original, f);
            d.Insert(f.Bom.Length, f.Raw("X"));
            var slice = d.ReadBytes(new(f.Bom.Length + f.Raw("X").Length, d.ByteLength));
            Assert.Equal(original[f.Bom.Length..], slice);
            var buf = new byte[3];
            Assert.Equal(3, d.Read(f.Bom.Length, buf));
        }
    }

    // ---- snapshots and revisions ----

    [Fact]
    public void SnapshotKeepsItsBytesWhileTheDocumentChanges()
    {
        using var d = New("hello world"u8.ToArray());
        var s0 = d.GetSnapshot();
        d.Insert(5, ","u8.ToArray());
        var s1 = d.GetSnapshot();
        d.Delete(new(0, 6));
        d.Undo();
        d.Undo();
        d.Insert(0, new byte[300]);
        Assert.Equal(0, s0.Revision);
        Assert.Equal("hello world"u8.ToArray(), s0.ReadBytes(new(0, s0.ByteLength)));
        Assert.Equal("hello, world"u8.ToArray(), s1.ReadBytes(new(0, s1.ByteLength)));
        Assert.Equal("hello", s1.ReadText(new(0, 5)));
        Assert.Same(d.GetSnapshot(), d.GetSnapshot());
        var viaThread = Task.Run(() => s1.ReadBytes(new(0, 5))).Result;
        Assert.Equal("hello"u8.ToArray(), viaThread);
    }

    [Fact]
    public void EveryChangeIncludingUndoAndRedoRaisesTheRevisionByOne()
    {
        using var d = New("abc"u8.ToArray());
        var id = d.Id;
        var seen = new List<(long, ChangeKind)>();
        d.Changed += (_, e) => seen.Add((e.Change.Revision, e.Change.Kind));
        Assert.Equal(0, d.Revision);
        d.Insert(0, "x"u8.ToArray());
        d.Delete(new(0, 2));
        d.Undo();
        d.Redo();
        d.Insert(0, ""u8.ToArray());
        Assert.Equal(4, d.Revision);
        Assert.Equal([(1, ChangeKind.Edit), (2, ChangeKind.Edit), (3, ChangeKind.Undo), (4, ChangeKind.Redo)], seen);
        Assert.Equal(id, d.Id);
    }

    // ---- randomized, string model ----

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void RandomTextEditsMatchAStringModelInEveryFormat(int seed)
    {
        foreach (var f in Formats)
        {
            var r = new Random(seed * 31 + f.Name.Length);
            string[] atoms = f == TestFormat.Cp1250
                ? ["a", "b", "č", "ž", "\r\n", "\n", "\r", "x y", "ř"]
                : ["a", "b", "č", "😀", "\r\n", "\n", "\r", "漢", "x y"];
            string model = string.Concat(Enumerable.Range(0, 30).Select(_ => atoms[r.Next(atoms.Length)]));
            var history = new List<string> { model };
            int at = 0;
            using var d = New(f.Bytes(model), f);
            for (int step = 0; step < 400; step++)
            {
                int op = r.Next(10);
                if (op < 7)
                {
                    int a = r.Next(model.Length + 1), b = Math.Min(model.Length, a + r.Next(0, 6));
                    while (a > 0 && a < model.Length && char.IsLowSurrogate(model[a])) a--;
                    while (b < model.Length && char.IsLowSurrogate(model[b])) b++;
                    if (b < a) b = a;
                    string ins = r.Next(3) == 0 ? "" : string.Concat(Enumerable.Range(0, r.Next(1, 4)).Select(_ => atoms[r.Next(atoms.Length)]));
                    if (a == b && ins == "") continue;
                    var result = d.Replace(Chars(f, model, a, b), f.Raw(ins));
                    Assert.True(result.Applied, $"{f} step {step}");
                    model = model[..a] + ins + model[b..];
                    history.RemoveRange(at + 1, history.Count - at - 1);
                    history.Add(model);
                    at++;
                }
                else if (op < 9 && at > 0) { Assert.NotNull(d.Undo()); model = history[--at]; }
                else if (at + 1 < history.Count) { Assert.NotNull(d.Redo()); model = history[++at]; }

                Assert.Equal(f.Bytes(model), Bytes(d));
                if (step % 40 == 0) CheckPositions(d, f, model);
            }
        }
    }
}

using NksHub.Textida.Accessibility;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility.Tests;

public class DocumentTextSurfaceTests
{
    [Fact]
    public void Typing_raises_text_change_and_moves_the_caret()
    {
        using var s = new TestSurface("ab\ncd");
        var changes = new List<TextChange>();
        int selections = 0;
        s.TextChanged += changes.Add;
        s.SelectionChanged += () => selections++;
        s.Select(2, 2);
        s.ReplaceText(2, 0, "XY");
        Assert.Equal("abXY\ncd", s.GetText(0, s.Length));
        Assert.Equal(new TextChange(2, 0, 2, s.Revision), Assert.Single(changes));
        Assert.Equal(4, s.CaretOffset);
        Assert.True(selections >= 2);
    }

    [Fact]
    public void Undo_maps_selection_through_the_change_and_reports_it()
    {
        using var s = new TestSurface("hello");
        s.Select(5, 5);
        s.ReplaceText(5, 0, " world");
        var changes = new List<TextChange>();
        s.TextChanged += changes.Add;
        s.Document.Undo();
        Assert.Equal("hello", s.GetText(0, s.Length));
        var c = Assert.Single(changes);
        Assert.Equal((5, 6, 0), (c.Start, c.Removed, c.Inserted));
        Assert.Equal(5, s.CaretOffset);
    }

    [Fact]
    public void Edit_after_surrogate_pair_uses_utf16_offsets()
    {
        using var s = new TestSurface("\U0001F600\nx");
        s.ReplaceText(3, 1, "yz");
        Assert.Equal("\U0001F600\nyz", s.GetText(0, s.Length));
        Assert.Equal(2, s.LineCount);
    }

    [Fact]
    public void Selection_keeps_direction()
    {
        using var s = new TestSurface("abcdef");
        s.Select(4, 1);
        Assert.Equal(new TextSpan(1, 3), s.Selection);
        Assert.Equal(1, s.CaretOffset);
    }

    [Fact]
    public void Empty_document_has_one_empty_line()
    {
        using var s = new TestSurface("");
        Assert.Equal(1, s.LineCount);
        Assert.Equal(0, s.Length);
        var r = TextRange.Document(s);
        Assert.Equal("", r.GetText());
        Assert.Equal(0, r.Move(TextUnit.Line, 1));
    }

    private static int[] StartsOf(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] is not ('\r' or '\n')) continue;
            starts.Add(i + 1);
        }
        return [.. starts];
    }

    private static void AssertMirrors(TestSurface s)
    {
        string expected = s.Document.ReadText(new(s.Document.BomLength, s.Document.ByteLength));
        Assert.Equal(expected, s.GetText(0, s.Length));
        var starts = StartsOf(expected);
        Assert.Equal(starts.Length, s.LineCount);
        for (int line = 0; line < starts.Length; line++) Assert.Equal(starts[line], s.LineStart(line));
        Assert.Equal(expected.Length, s.LineStart(s.LineCount));
    }

    [Fact]
    public void Edits_applied_in_place_leave_the_same_text_lines_and_change_reports_as_a_full_read()
    {
        string[] pieces = ["", "a", "\n", "\r\n", "\r", "\u00e9", "\U0001F600", "xy\nz", "\r\nx\r", "line one\r\nline two\n", "\n\n"];
        var random = new Random(7);
        using var s = new TestSurface("start\r\nsecond \U0001F600 line\nthird\r\n\r\nend");
        string before = s.GetText(0, s.Length);
        var failures = new List<string>();
        s.TextChanged += c =>
        {
            string now = s.GetText(0, s.Length);
            if (before[..c.Start] + now.Substring(c.Start, c.Inserted) + before[(c.Start + c.Removed)..] != now) failures.Add($"{c} on '{before}'");
            before = now;
        };
        for (int step = 0; step < 400; step++)
        {
            string text = s.GetText(0, s.Length);
            if (step % 9 == 8 && s.Document.CanUndo) s.Document.Undo();
            else if (step % 13 == 12 && s.Document.CanRedo) s.Document.Redo();
            else
            {
                int start = Boundary(text, random.Next(text.Length + 1));
                int end = Boundary(text, Math.Min(text.Length, start + (random.Next(4) == 0 ? random.Next(12) : random.Next(2))));
                if (end < start) end = start;
                s.ReplaceText(start, end - start, pieces[random.Next(pieces.Length)]);
            }
            AssertMirrors(s);
        }
        Assert.Empty(failures);
    }

    private static EditResult ApplyAt(TestSurface s, string text, IEnumerable<(int Start, int End, string Text)> edits) =>
        s.Document.Apply([.. edits.Select(e => new ByteEdit(
            new ByteRange(System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, e.Start)), System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, e.End))),
            System.Text.Encoding.UTF8.GetBytes(e.Text)))]);

    [Fact]
    public void Typing_at_a_thousand_carets_decodes_only_what_is_typed()
    {
        string line = new string('x', 63) + "\r\n";
        using var s = new TestSurface(string.Concat(Enumerable.Repeat(line, 16_384))); // 1 MiB
        long afterOpen = s.CharsDecoded;
        var changes = new List<TextChange>();
        s.TextChanged += changes.Add;
        string text = s.GetText(0, s.Length);
        for (int round = 0; round < 3; round++)
        {
            Assert.True(ApplyAt(s, text, Enumerable.Range(0, 1000).Select(i => (s.LineStart(i * 16) + 5, s.LineStart(i * 16) + 5, "k"))).Applied);
            text = s.Document.ReadText(new(s.Document.BomLength, s.Document.ByteLength));
            AssertMirrors(s);
        }
        Assert.Equal(3000, s.CharsDecoded - afterOpen); // a full read per keystroke would decode 1 MiB each time
        Assert.Equal(3, changes.Count);
        s.Document.Undo();
        Assert.Equal(3000, s.CharsDecoded - afterOpen);
        AssertMirrors(s);
    }

    [Fact]
    public void Several_edits_in_one_change_leave_the_same_text_lines_and_change_report_as_a_full_read()
    {
        string[] pieces = ["", "a", "\n", "\r\n", "\r", "é", "\U0001F600", "xy\nz", "\r\nx\r", "\n\n"];
        var random = new Random(11);
        using var s = new TestSurface("start\r\nsecond \U0001F600 line\nthird\r\n\r\nend\r\nmore text here\n");        string before = s.GetText(0, s.Length);
        var failures = new List<string>();
        s.TextChanged += c =>
        {
            string now = s.GetText(0, s.Length);
            if (before[..c.Start] + now.Substring(c.Start, c.Inserted) + before[(c.Start + c.Removed)..] != now) failures.Add($"{c} on '{before}'");
            before = now;
        };
        for (int step = 0; step < 400; step++)
        {
            string text = s.GetText(0, s.Length);
            if (step % 11 == 10 && s.Document.CanUndo) s.Document.Undo();
            else
            {
                var edits = new List<(int, int, string)>();
                int at = 0;
                for (int n = random.Next(1, 6); n > 0; n--)
                {
                    int start = Boundary(text, Math.Min(text.Length, at + random.Next(10)));
                    if (start < at) start = at;
                    int end = Boundary(text, Math.Min(text.Length, start + (random.Next(3) == 0 ? random.Next(6) : 0)));
                    if (end < start) end = start;
                    edits.Add((start, end, pieces[random.Next(pieces.Length)]));
                    at = end + 1;
                    if (at >= text.Length) break;
                }
                if (!ApplyAt(s, text, edits).Applied) continue;
            }
            AssertMirrors(s);
        }
        Assert.Empty(failures);
    }

    /// <summary>The nearest offset that is not inside a surrogate pair or between CR and LF.</summary>
    private static int Boundary(string text, int offset)
    {
        while (offset > 0 && offset < text.Length && (char.IsLowSurrogate(text[offset]) || (text[offset - 1] == '\r' && text[offset] == '\n'))) offset--;
        return offset;
    }

    [Fact]
    public void Typing_in_a_large_document_decodes_only_what_is_typed()
    {
        string line = new string('x', 63) + "\r\n";
        using var s = new TestSurface(string.Concat(Enumerable.Repeat(line, 16_384))); // 1 MiB
        Assert.True(s.Length >= 1 << 20);
        long afterOpen = s.CharsDecoded;
        int at = s.LineStart(8_000) + 10;
        for (int i = 0; i < 200; i++) s.ReplaceText(at + i, 0, i % 50 == 49 ? "\n" : "k");
        Assert.Equal(200, s.GetText(at, 200).Length);
        Assert.Equal(16_384 + 4 + 1, s.LineCount);
        Assert.Equal(200, s.CharsDecoded - afterOpen); // the old code read the whole 1 MiB text again at each of the 200 keystrokes
        s.Document.Undo();
        Assert.Equal(200, s.CharsDecoded - afterOpen); // an undo removes text and decodes none
        AssertMirrors(s);
    }
}

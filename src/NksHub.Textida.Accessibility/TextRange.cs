using System.Globalization;

namespace NksHub.Textida.Accessibility;

/// <summary>Text units with the numeric values of UIA <c>TextUnit</c>; Format, Page and Document all mean the whole text.</summary>
public enum TextUnit { Character = 0, Format = 1, Word = 2, Line = 3, Paragraph = 4, Page = 5, Document = 6 }

public enum RangeEndpoint { Start = 0, End = 1 }

/// <summary>
/// Unit boundaries over a surface. Character and word scans are local to one line, so a line of many megabytes
/// costs a scan of that line; a paged surface should keep its lines short or answer these from its own index.
/// </summary>
public static class TextUnits
{
    /// <summary>Largest boundary at or before <paramref name="offset"/>.</summary>
    public static int UnitStart(ITextAutomationSurface s, TextUnit unit, int offset)
    {
        offset = Math.Clamp(offset, 0, s.Length);
        switch (unit)
        {
            case TextUnit.Line or TextUnit.Paragraph:
                return s.LineStart(s.LineOfOffset(offset));
            case TextUnit.Character:
            case TextUnit.Word:
                {
                    int line = s.LineOfOffset(offset);
                    int ls = s.LineStart(line);
                    if (offset == ls) return offset;
                    var bounds = LineBoundaries(s, unit, line, out ls);
                    int i = bounds.BinarySearch(offset - ls);
                    if (i < 0) i = ~i - 1;
                    return ls + bounds[Math.Max(i, 0)];
                }
            default:
                return 0;
        }
    }

    /// <summary>Smallest boundary after <paramref name="offset"/>; <c>Length</c> when there is none.</summary>
    public static int UnitEnd(ITextAutomationSurface s, TextUnit unit, int offset)
    {
        offset = Math.Clamp(offset, 0, s.Length);
        if (offset >= s.Length) return s.Length;
        switch (unit)
        {
            case TextUnit.Line or TextUnit.Paragraph:
                {
                    int line = s.LineOfOffset(offset);
                    return line + 1 < s.LineCount ? s.LineStart(line + 1) : s.Length;
                }
            case TextUnit.Character:
            case TextUnit.Word:
                {
                    int line = s.LineOfOffset(offset);
                    var bounds = LineBoundaries(s, unit, line, out int ls);
                    int i = bounds.BinarySearch(offset - ls);
                    i = i < 0 ? ~i : i + 1;
                    int lineEnd = line + 1 < s.LineCount ? s.LineStart(line + 1) : s.Length;
                    return i < bounds.Count ? ls + bounds[i] : lineEnd;
                }
            default:
                return s.Length;
        }
    }

    /// <summary>Boundaries relative to the line start; the first is always 0.</summary>
    private static List<int> LineBoundaries(ITextAutomationSurface s, TextUnit unit, int line, out int lineStart)
    {
        lineStart = s.LineStart(line);
        int end = line + 1 < s.LineCount ? s.LineStart(line + 1) : s.Length;
        string text = s.GetText(lineStart, end - lineStart);
        var result = new List<int> { 0 };
        int i = 0;
        while (i < text.Length)
        {
            if (unit == TextUnit.Character)
            {
                i += StringInfo.GetNextTextElementLength(text, i);
            }
            else
            {
                i = NextWordEnd(text, i);
            }
            if (i < text.Length) result.Add(i);
        }
        return result;
    }

    private enum Cls { Word, Space, Punct, Break }

    private static Cls Classify(char c) =>
        c is '\r' or '\n' ? Cls.Break
        : char.IsWhiteSpace(c) ? Cls.Space
        : char.IsLetterOrDigit(c) || c == '_' || char.IsSurrogate(c) ? Cls.Word
        : Cls.Punct;

    /// <summary>A word is a run of letters/digits or of punctuation plus the blanks after it; a line terminator is a word of its own.</summary>
    private static int NextWordEnd(string text, int i)
    {
        var first = Classify(text[i]);
        if (first == Cls.Break) return i + StringInfo.GetNextTextElementLength(text, i);
        if (first != Cls.Space)
            while (i < text.Length && Classify(text[i]) == first) i++;
        while (i < text.Length && Classify(text[i]) == Cls.Space) i++;
        return i;
    }
}

/// <summary>
/// A live range over a surface with the semantics of UIA <c>ITextRangeProvider</c>. Both bridges use it, so the unit
/// logic exists once. Offsets are clamped to the current text, so a range outlives edits but does not track them.
/// </summary>
public sealed class TextRange
{
    private int _start;
    private int _end;

    public TextRange(ITextAutomationSurface surface, int start, int end)
    {
        Surface = surface;
        _start = start;
        _end = end;
        Clamp();
    }

    public ITextAutomationSurface Surface { get; }
    public int Start { get { Clamp(); return _start; } }
    public int End { get { Clamp(); return _end; } }
    public int Length => End - Start;
    public bool IsEmpty => Start == End;

    public static TextRange Document(ITextAutomationSurface s) => new(s, 0, s.Length);
    public static TextRange Caret(ITextAutomationSurface s) => new(s, s.CaretOffset, s.CaretOffset);
    public static TextRange SelectionOf(ITextAutomationSurface s) => new(s, s.Selection.Start, s.Selection.End);

    public TextRange Clone() => new(Surface, _start, _end);

    public bool Equals(TextRange other) => Start == other.Start && End == other.End;

    /// <summary>Sign of (this endpoint minus the other endpoint).</summary>
    public int CompareEndpoints(RangeEndpoint endpoint, TextRange other, RangeEndpoint otherEndpoint) =>
        Get(endpoint).CompareTo(other.Get(otherEndpoint));

    public string GetText(int maxLength = -1)
    {
        int len = maxLength < 0 ? Length : Math.Min(Length, maxLength);
        return len == 0 ? "" : Surface.GetText(Start, len);
    }

    public void ExpandToEnclosingUnit(TextUnit unit)
    {
        int s = TextUnits.UnitStart(Surface, unit, Start);
        int e;
        if (IsEmpty || End == s) e = TextUnits.UnitEnd(Surface, unit, s);
        else e = TextUnits.UnitStart(Surface, unit, End) == End ? End : TextUnits.UnitEnd(Surface, unit, End);
        _start = s;
        _end = e;
        Clamp();
    }

    /// <summary>
    /// Normalizes the start to a unit boundary, moves it by whole units and gives the range one unit (or none when it was empty).
    /// Returns the number of units moved, which is smaller than asked at the ends of the text.
    /// </summary>
    public int Move(TextUnit unit, int count)
    {
        bool empty = IsEmpty;
        int pos = TextUnits.UnitStart(Surface, unit, Start);
        int moved = 0;
        for (; moved < count; moved++)
        {
            if (pos >= Surface.Length) break;
            int next = TextUnits.UnitEnd(Surface, unit, pos);
            if (!empty && next >= Surface.Length) break;
            pos = next;
        }
        for (; moved > count; moved--)
        {
            if (pos <= 0) break;
            pos = TextUnits.UnitStart(Surface, unit, pos - 1);
        }
        _start = pos;
        _end = empty ? pos : TextUnits.UnitEnd(Surface, unit, pos);
        Clamp();
        return moved;
    }

    public int MoveEndpointByUnit(RangeEndpoint endpoint, TextUnit unit, int count)
    {
        int pos = Get(endpoint);
        int moved = 0;
        for (; moved < count && pos < Surface.Length; moved++) pos = TextUnits.UnitEnd(Surface, unit, pos);
        for (; moved > count && pos > 0; moved--) pos = TextUnits.UnitStart(Surface, unit, pos - 1);
        Set(endpoint, pos);
        return moved;
    }

    public void MoveEndpointByRange(RangeEndpoint endpoint, TextRange target, RangeEndpoint targetEndpoint) =>
        Set(endpoint, target.Get(targetEndpoint));

    public TextRange? FindText(string text, bool backward, bool ignoreCase)
    {
        if (text.Length == 0) return null;
        var cmp = ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.Ordinal;
        string haystack = GetText();
        int at = backward ? haystack.LastIndexOf(text, cmp) : haystack.IndexOf(text, cmp);
        return at < 0 ? null : new TextRange(Surface, Start + at, Start + at + text.Length);
    }

    public void Select() => Surface.SetSelection(Start, Length);

    public IReadOnlyList<ScreenRect> GetBounds() => Surface.GetRangeBounds(Start, Length);

    public void ScrollIntoView(bool alignToTop) => Surface.ScrollIntoView(Start, Length, alignToTop);

    private int Get(RangeEndpoint e) => e == RangeEndpoint.Start ? Start : End;

    private void Set(RangeEndpoint e, int value)
    {
        value = Math.Clamp(value, 0, Surface.Length);
        if (e == RangeEndpoint.Start)
        {
            _start = value;
            if (_end < value) _end = value;
        }
        else
        {
            _end = value;
            if (_start > value) _start = value;
        }
    }

    private void Clamp()
    {
        int len = Surface.Length;
        _start = Math.Clamp(_start, 0, len);
        _end = Math.Clamp(_end, _start, len);
    }
}

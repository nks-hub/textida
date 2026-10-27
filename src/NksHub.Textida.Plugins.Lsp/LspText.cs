using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>
/// Lines of a text as the language server protocol counts them: <c>\n</c>, <c>\r\n</c> and a lone <c>\r</c> end a line, and a column is a number of UTF-16 code units.
/// </summary>
public sealed class LineMap
{
    private readonly string _text;
    private readonly List<int> _starts = [0];

    public LineMap(string text)
    {
        _text = text;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] != '\r' && text[i] != '\n') continue;
            _starts.Add(i + 1);
        }
    }

    public int LineCount => _starts.Count;

    /// <summary>The 0-based line and UTF-16 column of a character offset.</summary>
    public (int Line, int Character) PositionOf(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        int at = _starts.BinarySearch(offset);
        int line = at >= 0 ? at : ~at - 1;
        return (line, offset - _starts[line]);
    }

    /// <summary>The character offset of a position; a column past the end of the line stops at the end of its text, a line past the end of the text at the end of the text.</summary>
    public int OffsetOf(int line, int character)
    {
        if (line < 0) return 0;
        if (line >= _starts.Count) return _text.Length;
        int end = line + 1 < _starts.Count ? _starts[line + 1] : _text.Length;
        while (end > _starts[line] && _text[end - 1] is '\n' or '\r') end--;
        return (int)Math.Min((long)_starts[line] + Math.Max(character, 0), end);
    }

    /// <summary>The text of a line without its line break.</summary>
    public string LineText(int line)
    {
        if (line < 0 || line >= _starts.Count) return "";
        int start = _starts[line];
        return _text[start..OffsetOf(line, int.MaxValue)];
    }
}

/// <summary>The one change that turns an old text into a new one: the old characters <c>[Start, OldEnd)</c> become <see cref="Inserted"/>.</summary>
public readonly record struct TextChange(int Start, int OldEnd, string Inserted)
{
    /// <summary>
    /// The smallest single change between two texts (common prefix and suffix), or null when they are equal. A surrogate pair is never cut in two.
    /// shortcut: one change per synchronisation, whatever the editor did in between; fine while the whole text is read for every one.
    /// </summary>
    public static TextChange? Between(string old, string next)
    {
        int max = Math.Min(old.Length, next.Length);
        int prefix = 0;
        while (prefix < max && old[prefix] == next[prefix]) prefix++;
        if (prefix == old.Length && prefix == next.Length) return null;
        if (prefix > 0 && char.IsHighSurrogate(old[prefix - 1])) prefix--;
        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == next[next.Length - 1 - suffix]) suffix++;
        if (suffix > 0 && char.IsLowSurrogate(old[old.Length - suffix])) suffix--;
        return new TextChange(prefix, old.Length - suffix, next[prefix..(next.Length - suffix)]);
    }

    /// <summary>The change applied to <paramref name="old"/>.</summary>
    public string ApplyTo(string old) => string.Concat(old.AsSpan(0, Start), Inserted, old.AsSpan(OldEnd));
}

/// <summary>Character offsets of a text turned into byte offsets of the file it was read from.</summary>
public static class ByteMap
{
    /// <summary>Byte offsets (from the start of the file, byte order mark included) by character offset; an offset inside a surrogate pair moves behind it.</summary>
    public static Dictionary<int, long> ToBytes(TextCodec codec, int bom, string text, IEnumerable<int> offsets)
    {
        var plain = codec.WithBom(false);
        var result = new Dictionary<int, long>();
        int at = 0;
        long bytes = bom;
        foreach (var c in offsets.Distinct().Order())
        {
            int target = Math.Clamp(c, at, text.Length);
            if (target > 0 && target < text.Length && char.IsHighSurrogate(text[target - 1]) && char.IsLowSurrogate(text[target])) target++;
            bytes += plain.Encode(text[at..target]).Length;
            at = target;
            result[c] = bytes;
        }
        return result;
    }
}

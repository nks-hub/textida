using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Secrets;

/// <summary>A run of characters in a text that holds a secret value (the value of a <c>.env</c> variable, quotes included).</summary>
public readonly record struct SecretSpan(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// The one filter that decides which part of a file is a secret and hides it. The renderer, the minimap, the clipboard, the search previews and the
/// plugin API all take their answer from here, so a value is hidden everywhere or nowhere. Hiding changes only what is shown or handed out; the
/// file and the document keep their bytes.
/// </summary>
public static class SecretValues
{
    /// <summary>What replaces a hidden value, whatever its length.</summary>
    public const string Placeholder = "••••";

    /// <summary>A file whose variables are secrets: <c>.env</c>, <c>.env.*</c> (<c>.env.local</c>) and <c>*.env</c> (<c>prod.env</c>).</summary>
    public static bool IsSecretFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var name = path.AsSpan(path.AsSpan().LastIndexOfAny('/', '\\') + 1);
        return name.Equals(".env", StringComparison.OrdinalIgnoreCase) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".env", StringComparison.OrdinalIgnoreCase);
    }

    private static int LineEnd(ReadOnlySpan<char> text, int from)
    {
        int i = text[from..].IndexOfAny('\r', '\n');
        return i < 0 ? text.Length : from + i;
    }

    private static int NextLine(ReadOnlySpan<char> text, int lineEnd)
    {
        if (lineEnd >= text.Length) return text.Length;
        return text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
    }

    private static bool Blank(char c) => c is ' ' or '\t';

    /// <summary>
    /// The values of the variables of a <c>.env</c> text: <c>KEY=value</c>, <c>export KEY=value</c>, values in double, single or back quotes (which may span lines;
    /// the span includes the quotes), unquoted values up to a trailing <c> # comment</c>. Comments, blank lines and empty values hold nothing. A quote that is
    /// never closed hides the rest of the text.
    /// </summary>
    public static List<SecretSpan> Find(ReadOnlySpan<char> text)
    {
        var spans = new List<SecretSpan>();
        int i = 0;
        while (i < text.Length)
        {
            int lineEnd = LineEnd(text, i);
            int p = i;
            while (p < lineEnd && Blank(text[p])) p++;
            if (p >= lineEnd || text[p] == '#') { i = NextLine(text, lineEnd); continue; }
            if (text[p..lineEnd].StartsWith("export", StringComparison.Ordinal) && p + 6 < lineEnd && Blank(text[p + 6]))
            {
                p += 6;
                while (p < lineEnd && Blank(text[p])) p++;
            }
            int eq = text[p..lineEnd].IndexOf('=');
            if (eq < 0) { i = NextLine(text, lineEnd); continue; }
            int start = p + eq + 1;
            while (start < lineEnd && Blank(text[start])) start++;
            int end;
            if (start < lineEnd && text[start] is '"' or '\'' or '`')
            {
                char quote = text[start];
                int k = start + 1;
                while (k < text.Length && text[k] != quote) k += quote == '"' && text[k] == '\\' && k + 1 < text.Length ? 2 : 1;
                end = k < text.Length ? k + 1 : text.Length;
            }
            else
            {
                end = start;
                for (int k = start; k < lineEnd; k++)
                {
                    if (text[k] == '#' && k > start && Blank(text[k - 1])) break;
                    if (!Blank(text[k])) end = k + 1;
                }
            }
            if (end > start) spans.Add(new SecretSpan(start, end - start));
            i = NextLine(text, LineEnd(text, end));
        }
        return spans;
    }

    /// <summary>
    /// <paramref name="text"/> is the part of the whole text that starts at <paramref name="offset"/>; every part of a secret that lies in it becomes one
    /// <see cref="Placeholder"/>. <paramref name="spans"/> are sorted, as <see cref="Find"/> returns them.
    /// </summary>
    public static string Mask(string text, IReadOnlyList<SecretSpan> spans, int offset = 0)
    {
        if (text.Length == 0 || spans.Count == 0) return text;
        int end = offset + text.Length;
        System.Text.StringBuilder? sb = null;
        int copied = 0;
        foreach (var span in spans)
        {
            if (span.End <= offset) continue;
            if (span.Start >= end) break;
            sb ??= new System.Text.StringBuilder(text.Length);
            int from = Math.Max(span.Start, offset) - offset, to = Math.Min(span.End, end) - offset;
            sb.Append(text, copied, from - copied).Append(Placeholder);
            copied = to;
        }
        return sb is null ? text : sb.Append(text, copied, text.Length - copied).ToString();
    }

    /// <summary>
    /// A single line of a secrets file seen without the text around it (a list of recent edits): the value of a variable is hidden, a comment and a blank line stay,
    /// and a line without <c>=</c> may be the rest of a value that spans lines, so it is hidden whole.
    /// </summary>
    public static string MaskLine(string line)
    {
        var trimmed = line.AsSpan().TrimStart();
        if (trimmed.IsEmpty || trimmed[0] == '#') return line;
        var spans = Find(line);
        return spans.Count > 0 ? Mask(line, spans) : line.Contains('=') ? line : Placeholder;
    }

    /// <summary>The first span that contains <paramref name="offset"/>, or null.</summary>
    public static SecretSpan? SpanAt(IReadOnlyList<SecretSpan> spans, int offset)
    {
        foreach (var span in spans)
        {
            if (span.Start > offset) break;
            if (offset < span.End) return span;
        }
        return null;
    }

    /// <summary>Whether [<paramref name="start"/>, <paramref name="end"/>) touches a span; an empty range counts when it lies strictly inside one.</summary>
    public static bool Overlaps(IReadOnlyList<SecretSpan> spans, int start, int end)
    {
        foreach (var span in spans)
        {
            if (span.Start >= Math.Max(end, start + 1)) break;
            if (start == end ? start > span.Start && start < span.End : start < span.End && end > span.Start) return true;
        }
        return false;
    }

    /// <summary>The secrets of a revision as byte ranges of the document (after the byte order mark).</summary>
    public static List<ByteRange> ByteRanges(DocumentSnapshot snapshot, int bomLength)
    {
        var decoded = snapshot.Codec.WithBom(false).Decode(snapshot.ReadBytes(new ByteRange(bomLength, snapshot.ByteLength)));
        return [.. Find(decoded.Text).Select(s => new ByteRange(bomLength + decoded.ByteOffsetOf(s.Start), bomLength + decoded.ByteOffsetOf(s.End)))];
    }

    /// <summary>Whether [<paramref name="start"/>, <paramref name="end"/>) of the document touches a secret (an empty range: lies strictly inside one).</summary>
    public static bool Overlaps(IReadOnlyList<ByteRange> secrets, long start, long end)
    {
        foreach (var r in secrets)
        {
            if (r.Start >= Math.Max(end, start + 1)) break;
            if (start == end ? start > r.Start && start < r.End : start < r.End && end > r.Start) return true;
        }
        return false;
    }

    /// <summary>
    /// Overwrites the secret bytes of <paramref name="data"/> (bytes of the document from <paramref name="dataStart"/>) with <c>*</c> written in the encoding
    /// of the document, one per code unit, so the length, the byte offsets and the validity of the text stay as they were.
    /// </summary>
    public static void MaskBytes(Span<byte> data, long dataStart, IReadOnlyList<ByteRange> secrets, TextCodec codec)
    {
        var star = codec.WithBom(false).Encode("*");
        long dataEnd = dataStart + data.Length;
        foreach (var r in secrets)
        {
            if (r.End <= dataStart) continue;
            if (r.Start >= dataEnd) break;
            for (long p = Math.Max(r.Start, dataStart); p < Math.Min(r.End, dataEnd); p++)
                data[(int)(p - dataStart)] = star[(int)((p - r.Start) % star.Length)];
        }
    }
}

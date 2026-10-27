using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Hex;

/// <summary>
/// A regular expression over bytes. A byte is a character 0 to 255: <c>.</c> is any one byte (also 0A and 0D), <c>\xHH</c> is a byte, and a class such as
/// <c>[\x01-\x08]</c> or <c>[^\x00]</c> is a set of bytes. Supported: groups <c>( )</c> and <c>(?: )</c>, alternation <c>|</c>, the repeats <c>? * + {n} {n,m} {n,}</c>
/// with a lazy <c>?</c> after them, and the escapes <c>\n \r \t \0 \d \D \w \W \s \S</c> (ASCII). The anchors <c>^</c> and <c>$</c> are the start and the end of the searched range (the file, or the selection when the search is limited to it). Not supported, because bytes have no lines, words or look-around:
/// <c>\b</c>, groups with a look-around, back references, flags and a letter or symbol above 7E written bare (write it as <c>\xHH</c>).
/// <para>
/// The wildcard <c>*</c> stands for 0 to N bytes: <c>.*</c> reaches at most N bytes, and a <c>*</c> with nothing before it (at the start, after <c>(</c> or <c>|</c>) is <c>.{0,N}</c>.
/// Every other open repeat (<c>*</c> after a byte or a class, <c>+</c>, <c>{n,}</c>) is bounded by the same N, so the longest match is always known and a search
/// can read exactly that much past a block. <c>N</c> is passed in; the pattern is refused when it needs one and none is given.
/// </para>
/// A match must be at least one byte. Matches are found at every start offset, so they may overlap, as with <see cref="BytePattern"/>.
/// </summary>
public sealed class ByteRegex : ByteQuery
{
    private readonly Regex _regex;

    private ByteRegex(string source, Node root, int? maxRepeat)
    {
        Source = source;
        MaxRepeat = maxRepeat;
        var (min, max) = root.Length();
        if (min < 1) throw new FormatException("The pattern can match nothing; a match needs at least one byte.");
        if (max > int.MaxValue / 2) throw new FormatException("The pattern can match too many bytes.");
        MinLength = (int)min;
        MaxLength = (int)max;
        var text = new StringBuilder();
        root.Emit(text);
        _regex = new Regex(text.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTools.MatchTimeoutMs));
    }

    public string Source { get; }
    /// <summary>The N of the wildcard, null when none was given.</summary>
    public int? MaxRepeat { get; }
    public override int MinLength { get; }
    public override int MaxLength { get; }

    /// <summary>Parses the regular expression; <paramref name="maxRepeat"/> is N, the most bytes an open repeat may reach.</summary>
    /// <exception cref="FormatException">The pattern is not valid or can match nothing.</exception>
    public static ByteRegex Parse(string pattern, int? maxRepeat = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (maxRepeat is < 1) throw new ArgumentOutOfRangeException(nameof(maxRepeat), "N must be at least 1.");
        var parser = new Parser(pattern, maxRepeat);
        return new ByteRegex(pattern, parser.ParseAll(), maxRepeat);
    }

    /// <summary>
    /// Hex bytes the way <see cref="BytePattern.ParseHex"/> reads them (<c>??</c> and <c>4?</c> included) with <c>*</c> for 0 to N bytes and <c>*{m,n}</c> for a range of your own,
    /// for example <c>50 4B * 04</c>.
    /// </summary>
    public static ByteRegex ParseHexWildcard(string text, int? maxRepeat = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxRepeat is < 1) throw new ArgumentOutOfRangeException(nameof(maxRepeat), "N must be at least 1.");
        var seq = new List<Node>();
        var pending = new StringBuilder();
        void Flush()
        {
            if (pending.Length == 0) return;
            var pattern = BytePattern.ParseHex(pending.ToString());
            pending.Clear();
            foreach (var n in pattern.ToNodes()) seq.Add(n);
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '*') { pending.Append(c); continue; }
            Flush();
            int min = 0;
            long max;
            if (i + 1 < text.Length && text[i + 1] == '{')
            {
                int close = text.IndexOf('}', i + 2);
                if (close < 0) throw new FormatException("A '{' needs a closing '}'.");
                (min, max) = Parser.Counts(text[(i + 2)..close], maxRepeat);
                i = close;
            }
            else max = maxRepeat ?? throw new FormatException("The wildcard * needs N, the most bytes it may stand for.");
            seq.Add(new Repeat(Any.Instance, min, max, lazy: false));
        }
        Flush();
        if (seq.Count == 0) throw new FormatException("The search pattern is empty.");
        return new ByteRegex(text, new Seq(seq), maxRepeat);
    }

    internal override bool Scan(ReadOnlySpan<byte> window, int starts, long block, List<ByteRange> matches, int maxResults, CancellationToken ct, bool startsRange = false, bool endsRange = false)
    {
        // a marker char (not a byte) stands before the range start and after its end so ^ and $ can be lookarounds that never consume a byte
        string text = (startsRange ? Marker : "") + Encoding.Latin1.GetString(window) + (endsRange ? Marker : "");
        int shift = startsRange ? 1 : 0;
        int at = shift;
        while (at < starts + shift)
        {
            ct.ThrowIfCancellationRequested();
            var m = _regex.Match(text, at);
            if (!m.Success || m.Index >= starts + shift) break;
            matches.Add(new ByteRange(block + m.Index - shift, block + m.Index - shift + m.Length));
            if (matches.Count == maxResults) return true;
            at = m.Index + 1;
        }
        return false;
    }

    private static readonly string Marker = ((char)0x100).ToString();

    /// <summary>True when the regular expression matches exactly these bytes from the first to the last; for tests and previews.</summary>
    public bool IsMatch(ReadOnlySpan<byte> bytes)
    {
        var m = _regex.Match(Encoding.Latin1.GetString(bytes));
        return m.Success && m.Index == 0 && m.Length == bytes.Length;
    }

    // ---- syntax tree ----

    internal abstract class Node
    {
        public abstract (long Min, long Max) Length();
        public abstract void Emit(StringBuilder sb);
    }

    private sealed class Set(bool[] bytes) : Node
    {
        public override (long Min, long Max) Length() => (1, 1);

        public override void Emit(StringBuilder sb)
        {
            int count = bytes.Count(b => b);
            if (count == 256) { sb.Append(@"[\x00-\xFF]"); return; }
            if (count == 1) { Hex(sb, Array.IndexOf(bytes, true)); return; }
            sb.Append('[');
            for (int i = 0; i < 256;)
            {
                if (!bytes[i]) { i++; continue; }
                int j = i;
                while (j + 1 < 256 && bytes[j + 1]) j++;
                Hex(sb, i);
                if (j > i) { sb.Append('-'); Hex(sb, j); }
                i = j + 1;
            }
            sb.Append(']');
        }

        private static void Hex(StringBuilder sb, int b) => sb.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));

        public bool Matches(int b) => bytes[b];

        public static Set Of(int b)
        {
            var set = new bool[256];
            set[b] = true;
            return new Set(set);
        }

        public static Set Mask(byte value, byte mask)
        {
            var set = new bool[256];
            for (int b = 0; b < 256; b++) set[b] = (b & mask) == value;
            return new Set(set);
        }
    }

    private sealed class Any : Node
    {
        public static readonly Any Instance = new();
        public override (long Min, long Max) Length() => (1, 1);
        public override void Emit(StringBuilder sb) => sb.Append(@"[\x00-\xFF]");
    }

    private sealed class Anchor(bool start) : Node
    {
        public override (long Min, long Max) Length() => (0, 0);
        public override void Emit(StringBuilder sb) => sb.Append(start ? $"(?<={Marker})" : $"(?={Marker})");
    }

    private sealed class Seq(List<Node> items) : Node
    {
        public override (long Min, long Max) Length() => items.Aggregate((0L, 0L), (a, n) => { var (lo, hi) = n.Length(); return (a.Item1 + lo, a.Item2 + hi); });
        public override void Emit(StringBuilder sb) { foreach (var n in items) n.Emit(sb); }
    }

    private sealed class Alt(List<Node> options) : Node
    {
        public override (long Min, long Max) Length() => (options.Min(o => o.Length().Min), options.Max(o => o.Length().Max));

        public override void Emit(StringBuilder sb)
        {
            sb.Append("(?:");
            for (int i = 0; i < options.Count; i++)
            {
                if (i > 0) sb.Append('|');
                options[i].Emit(sb);
            }
            sb.Append(')');
        }
    }

    private sealed class Repeat(Node inner, long min, long max, bool lazy) : Node
    {
        public override (long Min, long Max) Length()
        {
            var (lo, hi) = inner.Length();
            return (lo * min, hi * max);
        }

        public override void Emit(StringBuilder sb)
        {
            sb.Append("(?:");
            inner.Emit(sb);
            sb.Append(")");
            if (min == 0 && max == 1) sb.Append('?');
            else sb.Append('{').Append(min).Append(',').Append(max).Append('}');
            if (lazy) sb.Append('?');
        }
    }

    internal static Node SetFor(byte value, byte mask) => Set.Mask(value, mask);

    private sealed class Parser(string text, int? maxRepeat)
    {
        private int _pos;

        public Node ParseAll()
        {
            if (text.Length == 0) throw new FormatException("The search pattern is empty.");
            var node = ParseAlt();
            if (_pos < text.Length) throw new FormatException(text[_pos] == ')' ? "Unmatched ')'." : $"Unexpected '{text[_pos]}'.");
            return node;
        }

        private Node ParseAlt()
        {
            var options = new List<Node> { ParseSeq() };
            while (_pos < text.Length && text[_pos] == '|')
            {
                _pos++;
                options.Add(ParseSeq());
            }
            return options.Count == 1 ? options[0] : new Alt(options);
        }

        private Node ParseSeq()
        {
            var items = new List<Node>();
            while (_pos < text.Length && text[_pos] is not ('|' or ')'))
            {
                Node? atom;
                char c = text[_pos];
                if (c is '*' or '+' or '?' or '{')
                {
                    if (c == '{' && !LooksLikeCount()) throw new FormatException("Write \\{ for the byte, or give a count such as {2,4}.");
                    if (c != '*') throw new FormatException($"Nothing to repeat before '{c}'.");
                    // a bare * is the wildcard: 0 to N bytes
                    _pos++;
                    atom = new Repeat(Any.Instance, 0, Cap(null), lazy: LazySuffix());
                    items.Add(atom);
                    continue;
                }
                atom = ParseAtom();
                items.Add(ParseRepeats(atom));
            }
            if (items.Count == 0) throw new FormatException("An empty alternative; write at least one byte.");
            return items.Count == 1 ? items[0] : new Seq(items);
        }

        private bool LazySuffix()
        {
            if (_pos < text.Length && text[_pos] == '?') { _pos++; return true; }
            return false;
        }

        private bool LooksLikeCount()
        {
            int close = text.IndexOf('}', _pos);
            if (close < 0) return false;
            var body = text[(_pos + 1)..close];
            return body.Length > 0 && body.All(ch => char.IsAsciiDigit(ch) || ch == ',') && char.IsAsciiDigit(body[0]);
        }

        private long Cap(long? requested)
        {
            if (requested is { } r) return r;
            return maxRepeat ?? throw new FormatException("This repeat is open-ended and needs N, the most bytes it may reach.");
        }

        private Node ParseRepeats(Node atom)
        {
            while (_pos < text.Length)
            {
                char c = text[_pos];
                long min, max;
                if (c == '?') { _pos++; (min, max) = (0, 1); }
                else if (c == '*') { _pos++; (min, max) = (0, Cap(null)); }
                else if (c == '+') { _pos++; (min, max) = (1, Cap(null)); }
                else if (c == '{' && LooksLikeCount())
                {
                    int close = text.IndexOf('}', _pos);
                    (min, max) = Counts(text[(_pos + 1)..close], maxRepeat);
                    _pos = close + 1;
                }
                else break;
                atom = new Repeat(atom, min, max, LazySuffix());
            }
            return atom;
        }

        private Node ParseAtom()
        {
            char c = text[_pos++];
            switch (c)
            {
                case '.': return Any.Instance;
                case '(':
                    if (_pos < text.Length && text[_pos] == '?')
                    {
                        if (_pos + 1 < text.Length && text[_pos + 1] == ':') _pos += 2;
                        else throw new FormatException("Only (?: ) groups are supported; there is no look-around, naming or flag.");
                    }
                    var inner = ParseAlt();
                    if (_pos >= text.Length || text[_pos] != ')') throw new FormatException("A '(' needs a closing ')'.");
                    _pos++;
                    return inner;
                case '[': return ParseClass();
                case '\\': return Escape(inClass: false);
                case '^': return new Anchor(start: true);
                case '$': return new Anchor(start: false);
                case ']' or '}': throw new FormatException($"Unexpected '{c}'; write \\{c} for the byte.");
                default:
                    if (c is < ' ' or > '~') throw new FormatException($"Write the byte 0x{(int)c:X2} as \\x{(int)c:X2}.");
                    return Set.Of(c);
            }
        }

        private Node Escape(bool inClass)
        {
            if (_pos >= text.Length) throw new FormatException("A pattern cannot end with a backslash.");
            char c = text[_pos++];
            switch (c)
            {
                case 'x':
                    if (_pos + 2 > text.Length || !Uri.IsHexDigit(text[_pos]) || !Uri.IsHexDigit(text[_pos + 1])) throw new FormatException("\\x needs two hex digits.");
                    int value = int.Parse(text.AsSpan(_pos, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                    _pos += 2;
                    return Set.Of(value);
                case 'n': return Set.Of(0x0A);
                case 'r': return Set.Of(0x0D);
                case 't': return Set.Of(0x09);
                case '0': return Set.Of(0);
                case 'd': return Class("0-9", negate: false);
                case 'D': return Class("0-9", negate: true);
                case 'w': return Class("0-9A-Za-z_", negate: false);
                case 'W': return Class("0-9A-Za-z_", negate: true);
                case 's': return Class("\t\n\v\f\r ", negate: false);
                case 'S': return Class("\t\n\v\f\r ", negate: true);
                case 'b' or 'B' or 'A' or 'z' or 'Z' or 'G' when !inClass: throw new FormatException($"\\{c} is not supported: bytes have no words or lines.");
                case >= '1' and <= '9': throw new FormatException("Back references are not supported.");
                default:
                    if (char.IsAsciiLetterOrDigit(c)) throw new FormatException($"Unknown escape \\{c}.");
                    if (c is < ' ' or > '~') throw new FormatException($"Write the byte 0x{(int)c:X2} as \\x{(int)c:X2}.");
                    return Set.Of(c);
            }
        }

        private static Set Class(string members, bool negate)
        {
            var set = new bool[256];
            for (int i = 0; i < members.Length; i++)
            {
                if (i + 2 < members.Length && members[i + 1] == '-')
                {
                    for (int b = members[i]; b <= members[i + 2]; b++) set[b] = true;
                    i += 2;
                }
                else set[members[i]] = true;
            }
            if (negate) for (int b = 0; b < 256; b++) set[b] = !set[b];
            return new Set(set);
        }

        private Node ParseClass()
        {
            bool negate = _pos < text.Length && text[_pos] == '^';
            if (negate) _pos++;
            var set = new bool[256];
            bool any = false;
            while (true)
            {
                if (_pos >= text.Length) throw new FormatException("A '[' needs a closing ']'.");
                if (text[_pos] == ']' && any) { _pos++; break; }
                int low = ClassByte(set, out bool wasSet);
                if (wasSet) { any = true; continue; }
                if (_pos + 1 < text.Length && text[_pos] == '-' && text[_pos + 1] != ']')
                {
                    _pos++;
                    int high = ClassByte(set, out bool highSet);
                    if (highSet) throw new FormatException("A range ends in a byte, not in a class.");
                    if (high < low) throw new FormatException("A range runs backwards.");
                    for (int b = low; b <= high; b++) set[b] = true;
                }
                else set[low] = true;
                any = true;
            }
            if (negate) for (int b = 0; b < 256; b++) set[b] = !set[b];
            return new Set(set);
        }

        /// <summary>One member of a class; for <c>\d</c> and the like the set itself is merged in and <paramref name="wasSet"/> is true.</summary>
        private int ClassByte(bool[] into, out bool wasSet)
        {
            wasSet = false;
            char c = text[_pos++];
            if (c != '\\')
            {
                if (c is < ' ' or > '~') throw new FormatException($"Write the byte 0x{(int)c:X2} as \\x{(int)c:X2}.");
                return c;
            }
            if (_pos < text.Length && text[_pos] is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                var shorthand = (Set)Escape(inClass: true);
                for (int b = 0; b < 256; b++) if (shorthand.Matches(b)) into[b] = true;
                wasSet = true;
                return 0;
            }
            var node = (Set)Escape(inClass: true);
            for (int b = 0; b < 256; b++) if (node.Matches(b)) return b;
            throw new FormatException("Empty class member.");
        }

        public static (int Min, long Max) Counts(string body, int? maxRepeat)
        {
            var parts = body.Split(',');
            if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int min)) throw new FormatException("A count is {n}, {n,m} or {n,}.");
            long max;
            if (parts.Length == 1) max = min;
            else if (parts[1].Length == 0) max = maxRepeat ?? throw new FormatException("{n,} is open-ended and needs N, the most bytes it may reach.");
            else if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)) throw new FormatException("A count is {n}, {n,m} or {n,}.");
            else max = m;
            if (max < min) throw new FormatException("A count runs backwards.");
            if (max > 1 << 20) throw new FormatException("A repeat of more than 1 MiB is not supported.");
            return (min, max);
        }
    }
}

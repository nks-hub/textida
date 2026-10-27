using System.Globalization;
using System.Text;

namespace NksHub.Textida.Core.Data;

/// <summary>
/// Reads the part of YAML 1.2 that configuration files use into <see cref="DataNode"/>s with offsets: block mappings and sequences (also a sequence at the indentation of its key), flow
/// <c>[]</c> and <c>{}</c>, plain, single-quoted and double-quoted scalars (multi-line ones are folded), block scalars <c>|</c> and <c>&gt;</c> with chomping and indentation indicators,
/// comments, several documents, and the core schema for null, booleans and numbers (so <c>on</c>, <c>yes</c> and <c>1.0.0</c> are strings).
/// Several documents give an array of them. Anchors, aliases, tags, complex keys (<c>?</c>), directives, single-pair mappings in flow sequences and lone CR line breaks are not supported and
/// are reported as errors, never read wrongly. A duplicate key and a tab used as indentation are errors too.
/// </summary>
public static class YamlDataReader
{
    public static bool TryParse(string text, int maxDepth, out DataNode? node, out DataSyntaxException? error) => TryParse(text, 0, text.Length, maxDepth, out node, out error);

    /// <summary>Reads <c>text[begin..limit]</c>; offsets in the result are offsets in <paramref name="text"/>. <paramref name="begin"/> must be the start of a line.</summary>
    public static bool TryParse(string text, int begin, int limit, int maxDepth, out DataNode? node, out DataSyntaxException? error)
    {
        try
        {
            node = new Reader(text, begin, limit, maxDepth).ParseStream();
            error = null;
            return true;
        }
        catch (DataSyntaxException e)
        {
            node = null;
            error = e;
            return false;
        }
    }

    /// <summary>
    /// Reads the YAML frontmatter of a Markdown file: the lines between a first line <c>---</c> and the next line <c>---</c>. A file without frontmatter gives an empty object over its first line,
    /// so that a schema can ask for required fields; frontmatter that is empty or null does the same.
    /// </summary>
    public static bool TryParseFrontmatter(string text, int maxDepth, out DataNode? node, out DataSyntaxException? error)
    {
        int open = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        var firstLine = new DataNode(DataKind.Object, 0, LineEnd(text, 0));
        if (!IsFence(text, open, out int body))
        {
            node = firstLine;
            error = null;
            return true;
        }
        for (int p = body; p < text.Length; p = NextLine(text, p))
        {
            if (!IsFence(text, p, out _)) continue;
            if (!TryParse(text, body, p, maxDepth, out node, out error)) return false;
            if (node!.Kind == DataKind.Null) node = firstLine;
            return true;
        }
        node = null;
        error = new DataSyntaxException("The frontmatter is not closed.", open, open + 3);
        return false;
    }

    /// <summary>Where the body of the frontmatter starts and ends, or false when the file has none.</summary>
    public static bool TryFrontmatterBody(string text, out int start, out int end)
    {
        start = end = 0;
        if (!IsFence(text, text.Length > 0 && text[0] == '﻿' ? 1 : 0, out start)) return false;
        for (int p = start; p < text.Length; p = NextLine(text, p))
            if (IsFence(text, p, out _)) { end = p; return true; }
        return false;
    }

    private static int LineEnd(string t, int at)
    {
        int e = t.IndexOf('\n', at);
        if (e < 0) e = t.Length;
        return e > at && t[e - 1] == '\r' ? e - 1 : e;
    }

    private static int NextLine(string t, int at)
    {
        int e = t.IndexOf('\n', at);
        return e < 0 ? t.Length : e + 1;
    }

    private static bool IsFence(string t, int at, out int next)
    {
        next = 0;
        if (at > t.Length - 3 || string.CompareOrdinal(t, at, "---", 0, 3) != 0) return false;
        int j = at + 3;
        while (j < t.Length && t[j] is ' ' or '\t') j++;
        if (j < t.Length && t[j] is not ('\n' or '\r')) return false;
        next = NextLine(t, at);
        return true;
    }

    private sealed class Reader(string s, int begin, int limit, int maxDepth)
    {
        private int _begin = begin;
        private readonly int _n = limit;
        private int _i = begin;

        private bool End => _i >= _n;
        private char Cur => s[_i];

        private DataSyntaxException Fail(string what, int length = 1) => new(what, Math.Min(_i, _n), Math.Min(_i + length, _n));

        private bool BlankOrEol(int j) => j >= _n || s[j] is ' ' or '\t' or '\n' or '\r';

        private int Column(int j)
        {
            int k = j;
            while (k > _begin && s[k - 1] != '\n') k--;
            return j - k;
        }

        private bool AtDocMarker(int j) =>
            j + 3 <= _n && Column(j) == 0 && (string.CompareOrdinal(s, j, "---", 0, 3) == 0 || string.CompareOrdinal(s, j, "...", 0, 3) == 0) && BlankOrEol(j + 3);

        private bool OnlyBlanksBefore(int j)
        {
            int k = j;
            while (k > _begin && s[k - 1] is ' ' or '\t') k--;
            return k == _begin || s[k - 1] == '\n';
        }

        /// <summary>Moves to the next character that belongs to a token: over blanks, line breaks and comments. In block context a tab that indents a line with content is an error.</summary>
        private void SkipTrivia(bool flow = false)
        {
            while (!End)
            {
                char c = Cur;
                if (c is ' ' or '\r' or '\n') _i++;
                else if (c == '\t')
                {
                    if (!flow && OnlyBlanksBefore(_i))
                    {
                        int j = _i;
                        while (j < _n && s[j] is ' ' or '\t') j++;
                        if (j < _n && s[j] is not ('\r' or '\n' or '#')) throw Fail("A tab cannot be used as indentation.");
                    }
                    _i++;
                }
                else if (c == '#') { while (!End && Cur != '\n') _i++; }
                else break;
            }
        }

        private void EndOfValueLine()
        {
            while (!End && Cur is ' ' or '\t') _i++;
            if (End || Cur is '\n' or '\r' or '#') return;
            throw Fail("Unexpected text after the value.");
        }

        private void ConsumeBreak()
        {
            if (!End && Cur == '\r') _i++;
            if (!End && Cur == '\n') _i++;
        }

        public DataNode ParseStream()
        {
            for (int k = _begin; k < _n; k++)
                if (s[k] == '\r' && (k + 1 >= _n || s[k + 1] != '\n')) throw new DataSyntaxException("A line break that is a lone CR is not supported.", k, k + 1);
            if (!End && Cur == '﻿') { _i++; _begin++; } // the byte order mark is not a column
            var docs = new List<DataNode>();
            while (true)
            {
                SkipTrivia();
                if (End) break;
                if (Cur == '%' && Column(_i) == 0) throw Fail("YAML directives are not supported.", LineLength());
                DataNode doc;
                if (AtDocMarker(_i))
                {
                    bool start = Cur == '-';
                    _i += 3;
                    if (!start) continue;
                    int marker = _i;
                    SkipTrivia();
                    doc = End || AtDocMarker(_i) ? new DataNode(DataKind.Null, marker, marker) : ParseBlock(-1, 1);
                }
                else doc = ParseBlock(-1, 1);
                SkipTrivia();
                if (!End && !AtDocMarker(_i)) throw Fail("Unexpected text; check the indentation.");
                if (!End && Cur == '.') _i += 3;
                docs.Add(doc);
            }
            return docs.Count switch
            {
                0 => new DataNode(DataKind.Null, _begin, _begin),
                1 => docs[0],
                _ => Documents(docs),
            };
        }

        private static DataNode Documents(List<DataNode> docs)
        {
            var all = new DataNode(DataKind.Array, docs[0].Start, docs[^1].End);
            all.Items.AddRange(docs);
            return all;
        }

        private int LineLength()
        {
            int j = _i;
            while (j < _n && s[j] is not ('\n' or '\r')) j++;
            return j - _i;
        }

        private DataNode ParseBlock(int parentIndent, int depth)
        {
            if (depth > maxDepth) throw Fail("The nesting is too deep.");
            int col = Column(_i);
            char c = Cur;
            if (c == '-' && BlankOrEol(_i + 1)) return ParseSeq(col, depth);
            if (c == '?' && BlankOrEol(_i + 1)) throw Fail("Complex mapping keys ('? ') are not supported.");
            RejectIndicator(c);
            if (c is '|' or '>') return ParseBlockScalar(parentIndent);
            if (LooksLikeKey()) return ParseMap(col, depth);
            return ParseScalarOrFlow(parentIndent, depth);
        }

        private void RejectIndicator(char c)
        {
            if (c is '&' or '*') throw Fail("Anchors and aliases are not supported.", TokenLength());
            if (c == '!') throw Fail("Tags are not supported.", TokenLength());
            if (c is '%' or '@' or '`') throw Fail($"The character '{c}' cannot start a value.");
            if (c is ',' or ']' or '}') throw Fail($"Unexpected '{c}'.");
        }

        private int TokenLength()
        {
            int j = _i;
            while (j < _n && !BlankOrEol(j)) j++;
            return j - _i;
        }

        private int SkipQuotedOnLine(int j)
        {
            char q = s[j++];
            while (j < _n && s[j] is not ('\n' or '\r'))
            {
                if (q == '"' && s[j] == '\\') j++;
                else if (s[j] == q)
                {
                    if (q == '\'' && j + 1 < _n && s[j + 1] == '\'') j++;
                    else return j + 1;
                }
                j++;
            }
            return -1;
        }

        private bool LooksLikeKey()
        {
            int j = _i;
            char c = s[j];
            if (c is '[' or '{') return false;
            if (c is '"' or '\'')
            {
                j = SkipQuotedOnLine(j);
                if (j < 0) return false;
                while (j < _n && s[j] is ' ' or '\t') j++;
                return j < _n && s[j] == ':' && BlankOrEol(j + 1);
            }
            for (; j < _n && s[j] is not ('\n' or '\r'); j++)
            {
                if (s[j] == ':' && BlankOrEol(j + 1)) return true;
                if (s[j] == '#' && j > _i && s[j - 1] is ' ' or '\t') return false;
            }
            return false;
        }

        private DataNode ParseMap(int indent, int depth)
        {
            var node = new DataNode(DataKind.Object, _i, _i);
            while (true)
            {
                var (name, ks, ke) = ParseKey();
                if (node.Get(name) is not null) throw new DataSyntaxException($"The key '{name}' is defined twice.", ks, ke);
                var value = ParseMapValue(indent, depth + 1);
                node.Members.Add(new DataMember(name, ks, ke, value));
                node.End = Math.Max(value.End, ke + 1);
                SkipTrivia();
                if (End || AtDocMarker(_i)) return node;
                int col = Column(_i);
                if (col < indent) return node;
                if (col > indent) throw Fail("The indentation does not match the previous lines.");
                if (Cur == '-' && BlankOrEol(_i + 1)) return node;
                if (!LooksLikeKey()) throw Fail("A key followed by ':' is expected.");
            }
        }

        /// <summary>Reads a key and stops on its ':'.</summary>
        private (string Name, int Start, int End) ParseKey()
        {
            int start = _i;
            string name;
            if (Cur is '"' or '\'')
            {
                name = Cur == '"' ? ReadDouble() : ReadSingle();
                int end = _i;
                while (Cur is ' ' or '\t') _i++;
                return (name, start, end);
            }
            RejectIndicator(Cur);
            int j = _i;
            while (!(s[j] == ':' && BlankOrEol(j + 1))) j++;
            int e = j;
            while (s[e - 1] is ' ' or '\t') e--;
            _i = j;
            return (s[start..e], start, e);
        }

        /// <summary>Reads what follows the ':' of a key whose block collection has the indentation <paramref name="keyIndent"/>.</summary>
        private DataNode ParseMapValue(int keyIndent, int depth)
        {
            _i++;
            int afterColon = _i;
            while (!End && Cur is ' ' or '\t') _i++;
            if (End || Cur is '\n' or '\r' or '#')
            {
                SkipTrivia();
                if (!End && !AtDocMarker(_i))
                {
                    int col = Column(_i);
                    if (col > keyIndent) return ParseBlock(keyIndent, depth);
                    if (col == keyIndent && Cur == '-' && BlankOrEol(_i + 1)) return ParseSeq(col, depth);
                }
                return new DataNode(DataKind.Null, afterColon, afterColon);
            }
            char c = Cur;
            if (c == '-' && BlankOrEol(_i + 1)) throw Fail("A block sequence cannot start on the line of its key.");
            RejectIndicator(c);
            if (c is '|' or '>') return ParseBlockScalar(keyIndent);
            return ParseScalarOrFlow(keyIndent, depth);
        }

        private DataNode ParseSeq(int indent, int depth)
        {
            var node = new DataNode(DataKind.Array, _i, _i);
            while (true)
            {
                int dash = _i;
                _i++;
                int afterDash = _i;
                while (!End && Cur is ' ' or '\t') _i++;
                DataNode item;
                if (End || Cur is '\n' or '\r' or '#')
                {
                    SkipTrivia();
                    item = !End && !AtDocMarker(_i) && Column(_i) > indent ? ParseBlock(indent, depth + 1) : new DataNode(DataKind.Null, afterDash, afterDash);
                }
                else item = ParseBlock(indent, depth + 1);
                node.Items.Add(item);
                node.End = Math.Max(item.End, dash + 1);
                SkipTrivia();
                if (End || AtDocMarker(_i)) return node;
                int col = Column(_i);
                if (col < indent) return node;
                if (col > indent) throw Fail("The indentation does not match the previous lines.");
                if (!(Cur == '-' && BlankOrEol(_i + 1))) return node;
            }
        }

        private DataNode ParseScalarOrFlow(int parentIndent, int depth)
        {
            int start = _i;
            switch (Cur)
            {
                case '[' or '{':
                    {
                        var flow = ParseFlow(depth);
                        EndOfValueLine();
                        return flow;
                    }
                case '"':
                    {
                        var t = ReadDouble();
                        int end = _i;
                        EndOfValueLine();
                        return new DataNode(DataKind.String, start, end) { Text = t };
                    }
                case '\'':
                    {
                        var t = ReadSingle();
                        int end = _i;
                        EndOfValueLine();
                        return new DataNode(DataKind.String, start, end) { Text = t };
                    }
            }
            return ParsePlain(parentIndent);
        }

        private DataNode ParsePlain(int parentIndent)
        {
            int start = _i;
            var sb = new StringBuilder();
            int end = ScanPlainLine(sb);
            while (NextContentLine(end, out int pos, out int blanks))
            {
                if (Column(pos) <= parentIndent || s[pos] == '#' || AtDocMarker(pos)) break;
                sb.Append(blanks > 0 ? new string('\n', blanks) : " ");
                _i = pos;
                end = ScanPlainLine(sb);
            }
            _i = end;
            return Resolve(sb.ToString(), start, end);
        }

        private int ScanPlainLine(StringBuilder sb)
        {
            int st = _i, j = _i, last = _i;
            while (j < _n && s[j] is not ('\n' or '\r'))
            {
                char c = s[j];
                if (c == ':' && BlankOrEol(j + 1)) throw new DataSyntaxException("A mapping value is not allowed here.", j, j + 1);
                if (c == '#' && j > st && s[j - 1] is ' ' or '\t') break;
                if (c is not (' ' or '\t')) last = j + 1;
                j++;
            }
            sb.Append(s, st, last - st);
            return last;
        }

        /// <summary>The first character of the next line that has content: false at the end; <paramref name="blanks"/> counts the empty lines in between.</summary>
        private bool NextContentLine(int fromPos, out int pos, out int blanks)
        {
            blanks = 0;
            pos = -1;
            int p = fromPos;
            while (true)
            {
                while (p < _n && s[p] != '\n') p++;
                if (p >= _n) return false;
                p++;
                int q = p;
                while (q < _n && s[q] == ' ') q++;
                int t = q;
                while (t < _n && s[t] is ' ' or '\t') t++;
                if (t >= _n) return false;
                if (s[t] is '\r' or '\n') { blanks++; p = t; continue; }
                if (t > q) { _i = q; throw Fail("A tab cannot be used as indentation."); }
                pos = t;
                return true;
            }
        }

        private static DataNode Resolve(string t, int start, int end)
        {
            switch (t)
            {
                case "" or "~" or "null" or "Null" or "NULL": return new DataNode(DataKind.Null, start, end);
                case "true" or "True" or "TRUE": return new DataNode(DataKind.Boolean, start, end) { Bool = true };
                case "false" or "False" or "FALSE": return new DataNode(DataKind.Boolean, start, end);
                case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF": return new DataNode(DataKind.Number, start, end) { Text = "Infinity" };
                case "-.inf" or "-.Inf" or "-.INF": return new DataNode(DataKind.Number, start, end) { Text = "-Infinity" };
                case ".nan" or ".NaN" or ".NAN": return new DataNode(DataKind.Number, start, end) { Text = "NaN" };
            }
            if (TryNumber(t, out var number)) return new DataNode(DataKind.Number, start, end) { Text = number };
            return new DataNode(DataKind.String, start, end) { Text = t };
        }

        private static bool TryNumber(string t, out string text)
        {
            text = t;
            if (t.Length > 2 && t[0] == '0' && t[1] is 'x' or 'o')
            {
                if (t[1] == 'x' && ulong.TryParse(t.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var h)) { text = h.ToString(CultureInfo.InvariantCulture); return true; }
                if (t[1] == 'o' && TryOctal(t.AsSpan(2), out var o)) { text = o.ToString(CultureInfo.InvariantCulture); return true; }
                return false;
            }
            int i = t.Length > 0 && (t[0] is '+' or '-') ? 1 : 0;
            int ds = i;
            while (i < t.Length && char.IsAsciiDigit(t[i])) i++;
            int intDigits = i - ds;
            if (i == t.Length) return intDigits > 0;
            int fracDigits = 0;
            if (t[i] == '.')
            {
                int fs = ++i;
                while (i < t.Length && char.IsAsciiDigit(t[i])) i++;
                fracDigits = i - fs;
            }
            if (intDigits == 0 && fracDigits == 0) return false;
            if (i < t.Length && t[i] is 'e' or 'E')
            {
                i++;
                if (i < t.Length && t[i] is '+' or '-') i++;
                int es = i;
                while (i < t.Length && char.IsAsciiDigit(t[i])) i++;
                if (i == es) return false;
            }
            return i == t.Length;
        }

        private static bool TryOctal(ReadOnlySpan<char> digits, out ulong value)
        {
            value = 0;
            if (digits.IsEmpty || digits.Length > 21) return false;
            foreach (var c in digits)
            {
                if (c is < '0' or > '7') return false;
                value = value * 8 + (ulong)(c - '0');
            }
            return true;
        }

        private string ReadSingle()
        {
            int open = _i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (End) throw new DataSyntaxException("The string is not closed.", open, _n);
                char c = Cur;
                if (c == '\'')
                {
                    if (_i + 1 < _n && s[_i + 1] == '\'') { sb.Append('\''); _i += 2; continue; }
                    _i++;
                    return sb.ToString();
                }
                if (c is '\n' or '\r') { FoldBreak(sb, 0); continue; }
                sb.Append(c);
                _i++;
            }
        }

        private string ReadDouble()
        {
            int open = _i++;
            var sb = new StringBuilder();
            int protect = 0;
            while (true)
            {
                if (End) throw new DataSyntaxException("The string is not closed.", open, _n);
                char c = Cur;
                if (c == '"') { _i++; return sb.ToString(); }
                if (c is '\n' or '\r') { FoldBreak(sb, protect); continue; }
                if (c != '\\') { sb.Append(c); _i++; continue; }
                _i++;
                if (End) throw new DataSyntaxException("The string is not closed.", open, _n);
                char e = Cur;
                _i++;
                switch (e)
                {
                    case '0': sb.Append('\0'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 't' or '\t': sb.Append('\t'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'e': sb.Append('\u001b'); break;
                    case ' ' or '"' or '/' or '\\': sb.Append(e); break;
                    case 'N': sb.Append('\u0085'); break;
                    case '_': sb.Append('\u00a0'); break;
                    case 'L': sb.Append('\u2028'); break;
                    case 'P': sb.Append('\u2029'); break;
                    case 'x': sb.Append(Hex(2)); break;
                    case 'u': sb.Append(Hex(4)); break;
                    case 'U': sb.Append(Hex(8)); break;
                    case '\r' or '\n': // an escaped line break joins the lines without a space
                        if (e == '\r' && !End && Cur == '\n') _i++;
                        while (!End && Cur is ' ' or '\t') _i++;
                        break;
                    default: _i -= 2; throw Fail($"Unknown escape '\\{e}'.", 2);
                }
                protect = sb.Length;
            }
        }

        private string Hex(int digits)
        {
            if (_i + digits > _n || !int.TryParse(s.AsSpan(_i, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code) || code is < 0 or > 0x10FFFF)
                throw Fail("A bad hexadecimal escape.", digits);
            _i += digits;
            return code is >= 0xD800 and <= 0xDFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code);
        }

        /// <summary>A line break inside a quoted scalar: one break becomes a space, each empty line after it a line feed; blanks around the break go.</summary>
        private void FoldBreak(StringBuilder sb, int protect)
        {
            while (sb.Length > protect && sb[^1] is ' ' or '\t') sb.Length--;
            int empties = 0;
            ConsumeBreak();
            while (true)
            {
                while (!End && Cur is ' ' or '\t') _i++;
                if (!End && Cur is '\n' or '\r') { ConsumeBreak(); empties++; }
                else break;
            }
            if (empties == 0) sb.Append(' ');
            else sb.Append('\n', empties);
        }

        private DataNode ParseBlockScalar(int parentIndent)
        {
            int start = _i;
            bool folded = Cur == '>';
            _i++;
            char chomp = 'c';
            int explicitIndent = 0;
            for (int k = 0; k < 2 && !End; k++)
            {
                if (Cur is '+' or '-' && chomp == 'c') { chomp = Cur; _i++; }
                else if (Cur is >= '1' and <= '9' && explicitIndent == 0) { explicitIndent = Cur - '0'; _i++; }
                else break;
            }
            int headerEnd = _i;
            while (!End && Cur is ' ' or '\t') _i++;
            if (!End && Cur is not ('\n' or '\r' or '#')) throw Fail("Unexpected text after the block scalar indicator.");
            while (!End && Cur != '\n') _i++;

            var raw = new List<string>();
            int blockIndent = explicitIndent > 0 ? Math.Max(parentIndent, 0) + explicitIndent : -1;
            int textEnd = headerEnd;
            bool finalBreak = false;
            int p = _i + 1;
            while (p < _n)
            {
                int le = p;
                while (le < _n && s[le] != '\n') le++;
                int ce = le > p && s[le - 1] == '\r' ? le - 1 : le;
                int sp = 0;
                while (p + sp < ce && s[p + sp] == ' ') sp++;
                bool blank = true;
                for (int k = p + sp; k < ce; k++) if (s[k] is not (' ' or '\t')) { blank = false; break; }
                if (blank)
                {
                    raw.Add(blockIndent >= 0 && ce - p > blockIndent ? s[(p + blockIndent)..ce] : "");
                    _i = le;
                    p = le + 1;
                    continue;
                }
                if (blockIndent < 0)
                {
                    if (sp <= parentIndent) break;
                    blockIndent = sp;
                }
                else if (sp < blockIndent) break;
                if (AtDocMarker(p)) break;
                raw.Add(s[(p + blockIndent)..ce]);
                textEnd = ce;
                finalBreak = le < _n;
                _i = le;
                p = le + 1;
            }

            var sb = new StringBuilder();
            string? prev = null;
            int blanks = 0;
            foreach (var line in raw)
            {
                if (line.Length == 0) { blanks++; continue; }
                if (prev is null) sb.Append('\n', blanks);
                else if (folded && !MoreIndented(prev) && !MoreIndented(line)) sb.Append(blanks == 0 ? " " : new string('\n', blanks));
                else sb.Append('\n', blanks + 1);
                sb.Append(line);
                prev = line;
                blanks = 0;
            }
            if (chomp == '+')
            {
                if (prev is not null && finalBreak) sb.Append('\n');
                sb.Append('\n', blanks);
            }
            else if (chomp == 'c' && prev is not null && finalBreak) sb.Append('\n');
            return new DataNode(DataKind.String, start, textEnd) { Text = sb.ToString() };
        }

        private static bool MoreIndented(string line) => line[0] is ' ' or '\t';

        private DataNode ParseFlow(int depth)
        {
            if (depth > maxDepth) throw Fail("The nesting is too deep.");
            return Cur == '[' ? ParseFlowSeq(depth) : ParseFlowMap(depth);
        }

        private DataNode ParseFlowSeq(int depth)
        {
            var node = new DataNode(DataKind.Array, _i, _i);
            _i++;
            while (true)
            {
                SkipTrivia(true);
                if (End) throw Fail("']' is expected.");
                if (Cur == ']') { _i++; node.End = _i; return node; }
                var item = ParseFlowValue(depth + 1);
                SkipTrivia(true);
                if (!End && Cur == ':' && BlankOrFlow(_i + 1)) throw Fail("A single-pair mapping in a flow sequence is not supported.");
                node.Items.Add(item);
                if (!End && Cur == ',') _i++;
                else if (End || Cur != ']') throw Fail("',' or ']' is expected.");
            }
        }

        private DataNode ParseFlowMap(int depth)
        {
            var node = new DataNode(DataKind.Object, _i, _i);
            _i++;
            while (true)
            {
                SkipTrivia(true);
                if (End) throw Fail("'}' is expected.");
                if (Cur == '}') { _i++; node.End = _i; return node; }
                int ks = _i;
                string name;
                bool quoted = Cur is '"' or '\'';
                if (Cur == '"') name = ReadDouble();
                else if (Cur == '\'') name = ReadSingle();
                else if (Cur is '[' or '{') throw Fail("Complex mapping keys are not supported.");
                else if (Cur == '?' && BlankOrEol(_i + 1)) throw Fail("Complex mapping keys ('? ') are not supported.");
                else
                {
                    RejectIndicator(Cur);
                    (name, _) = ParseFlowPlain(ks);
                    if (name.Length == 0) throw Fail("A key is expected.");
                }
                int ke = _i;
                if (node.Get(name) is not null) throw new DataSyntaxException($"The key '{name}' is defined twice.", ks, ke);
                SkipTrivia(true);
                DataNode value;
                if (!End && Cur == ':' && (quoted || BlankOrFlow(_i + 1)))
                {
                    _i++;
                    SkipTrivia(true);
                    value = !End && Cur is ',' or '}' ? new DataNode(DataKind.Null, _i, _i) : ParseFlowValue(depth + 1);
                }
                else value = new DataNode(DataKind.Null, ke, ke);
                node.Members.Add(new DataMember(name, ks, ke, value));
                SkipTrivia(true);
                if (!End && Cur == ',') _i++;
                else if (End || Cur != '}') throw Fail("',' or '}' is expected.");
            }
        }

        private bool BlankOrFlow(int j) => BlankOrEol(j) || s[j] is ',' or '[' or ']' or '{' or '}';

        private DataNode ParseFlowValue(int depth)
        {
            int start = _i;
            switch (Cur)
            {
                case '[' or '{': return ParseFlow(depth);
                case '"': { var t = ReadDouble(); return new DataNode(DataKind.String, start, _i) { Text = t }; }
                case '\'': { var t = ReadSingle(); return new DataNode(DataKind.String, start, _i) { Text = t }; }
            }
            RejectIndicator(Cur);
            if (Cur is '|' or '>') throw Fail("A block scalar cannot be written inside a flow collection.");
            var (text, last) = ParseFlowPlain(start);
            if (text.Length == 0) throw Fail("A value is expected.");
            return Resolve(text, start, last);
        }

        /// <summary>A plain scalar inside a flow collection; it may continue on the next lines and ends at a flow indicator, a ': ' or a comment.</summary>
        private (string Text, int End) ParseFlowPlain(int start)
        {
            var sb = new StringBuilder();
            int last = _i;
            while (!End)
            {
                char c = Cur;
                if (c is ',' or '[' or ']' or '{' or '}') break;
                if (c == ':' && BlankOrFlow(_i + 1)) break;
                if (c == '#' && _i > start && s[_i - 1] is ' ' or '\t' or '\n') break;
                if (c is '\n' or '\r')
                {
                    SkipTrivia(true);
                    if (!End && Cur is not (',' or ']' or '}' or ':')) sb.Append(' ');
                    continue;
                }
                sb.Append(c);
                if (c is not (' ' or '\t')) last = _i + 1;
                _i++;
            }
            return (sb.ToString().TrimEnd(' ', '\t'), last);
        }
    }
}

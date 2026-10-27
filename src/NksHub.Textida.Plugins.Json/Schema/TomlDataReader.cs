using System.Globalization;
using System.Text;

using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>
/// Reads TOML 1.0 into <see cref="DataNode"/>s with offsets: tables, arrays of tables, dotted keys, all string forms, integers (decimal, hex, octal, binary),
/// floats, booleans, arrays and inline tables. Dates and times are kept as strings. A file that is not valid TOML gives a <see cref="DataSyntaxException"/>.
/// </summary>
public static class TomlDataReader
{
    public static bool TryParse(string text, int maxDepth, out DataNode? node, out DataSyntaxException? error)
    {
        try
        {
            node = new Reader(text, maxDepth).ParseDocument();
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

    private sealed class Reader(string s, int maxDepth)
    {
        private int _i;
        private readonly HashSet<DataNode> _explicit = [];
        private readonly HashSet<DataNode> _closed = []; // inline tables and static arrays cannot be extended

        private DataSyntaxException Fail(string what, int length = 1) => new(what, Math.Min(_i, s.Length), Math.Min(_i + length, s.Length));
        private bool End => _i >= s.Length;
        private char Cur => s[_i];

        private void SkipBlanks() { while (!End && Cur is ' ' or '\t') _i++; }

        private void SkipComment() { while (!End && Cur != '\n') _i++; }

        private void SkipTrivia()
        {
            while (!End)
            {
                if (Cur is ' ' or '\t' or '\r' or '\n') _i++;
                else if (Cur == '#') SkipComment();
                else break;
            }
        }

        private void EndOfLine()
        {
            SkipBlanks();
            if (End) return;
            if (Cur == '#') { SkipComment(); return; }
            if (Cur == '\r') _i++;
            if (!End && Cur == '\n') return;
            throw Fail("Unexpected text after a value.");
        }

        public DataNode ParseDocument()
        {
            var root = new DataNode(DataKind.Object, 0, s.Length);
            var current = root;
            while (true)
            {
                SkipTrivia();
                if (End) { Extend(root); return root; }
                if (Cur == '[')
                {
                    int headerStart = _i;
                    bool array = _i + 1 < s.Length && s[_i + 1] == '[';
                    _i += array ? 2 : 1;
                    var path = ParseKeyPath();
                    SkipBlanks();
                    if (End || Cur != ']') throw Fail("']' is expected.");
                    _i++;
                    if (array)
                    {
                        if (End || Cur != ']') throw Fail("']]' is expected.");
                        _i++;
                    }
                    current = array ? NewArrayItem(root, path, headerStart) : OpenTable(root, path, headerStart);
                    EndOfLine();
                    continue;
                }
                AssignKeyValue(current, 1);
                EndOfLine();
            }
        }

        /// <summary>A table written with a header ends where its last value ends.</summary>
        private static int Extend(DataNode n)
        {
            foreach (var m in n.Members) n.End = Math.Max(n.End, Extend(m.Value));
            foreach (var i in n.Items) n.End = Math.Max(n.End, Extend(i));
            return n.End;
        }

        private void AssignKeyValue(DataNode table, int depth)
        {
            var key = ParseKeyPath();
            SkipBlanks();
            if (End || Cur != '=') throw Fail("'=' is expected.");
            _i++;
            SkipBlanks();
            var value = ParseValue(depth);
            var target = table;
            for (int k = 0; k < key.Count - 1; k++) target = Child(target, key[k], true);
            var last = key[^1];
            if (target.Get(last.Name) is not null || _closed.Contains(target)) throw new DataSyntaxException($"The key '{last.Name}' is defined twice.", last.Start, last.End);
            target.Members.Add(new DataMember(last.Name, last.Start, last.End, value));
        }

        /// <summary>The table named <paramref name="key"/> inside <paramref name="parent"/>, created when missing; the last table of an array of tables is entered.</summary>
        private DataNode Child(DataNode parent, Key key, bool dotted)
        {
            if (_closed.Contains(parent)) throw new DataSyntaxException("An inline table cannot be extended.", key.Start, key.End);
            var next = parent.Get(key.Name);
            if (next is null)
            {
                next = new DataNode(DataKind.Object, key.Start, key.End);
                parent.Members.Add(new DataMember(key.Name, key.Start, key.End, next));
            }
            if (next.Kind == DataKind.Array && !_closed.Contains(next) && next.Items.Count > 0 && next.Items[^1].Kind == DataKind.Object) next = next.Items[^1];
            if (next.Kind != DataKind.Object || _closed.Contains(next)) throw new DataSyntaxException($"'{key.Name}' is not a table.", key.Start, key.End);
            return next;
        }

        private DataNode OpenTable(DataNode root, List<Key> path, int headerStart)
        {
            var t = root;
            foreach (var k in path) t = Child(t, k, false);
            if (!_explicit.Add(t)) throw new DataSyntaxException($"The table '{path[^1].Name}' is defined twice.", path[^1].Start, path[^1].End);
            t.Start = headerStart;
            return t;
        }

        private DataNode NewArrayItem(DataNode root, List<Key> path, int headerStart)
        {
            var parent = root;
            for (int k = 0; k < path.Count - 1; k++) parent = Child(parent, path[k], false);
            var last = path[^1];
            var existing = parent.Get(last.Name);
            if (existing is null)
            {
                existing = new DataNode(DataKind.Array, headerStart, headerStart);
                parent.Members.Add(new DataMember(last.Name, last.Start, last.End, existing));
            }
            else if (existing.Kind != DataKind.Array || _closed.Contains(existing))
                throw new DataSyntaxException($"'{last.Name}' is not an array of tables.", last.Start, last.End);
            var item = new DataNode(DataKind.Object, headerStart, headerStart);
            existing.Items.Add(item);
            _explicit.Add(item);
            return item;
        }

        private readonly record struct Key(string Name, int Start, int End);

        private List<Key> ParseKeyPath()
        {
            var path = new List<Key>();
            while (true)
            {
                SkipBlanks();
                if (End) throw Fail("A key is expected.");
                int start = _i;
                string name;
                if (Cur == '"') name = ParseBasicString();
                else if (Cur == '\'') name = ParseLiteralString();
                else
                {
                    while (!End && (char.IsAsciiLetterOrDigit(Cur) || Cur is '_' or '-')) _i++;
                    if (_i == start) throw Fail("A key is expected.");
                    name = s[start.._i];
                }
                path.Add(new Key(name, start, _i));
                SkipBlanks();
                if (!End && Cur == '.') { _i++; continue; }
                return path;
            }
        }

        private DataNode ParseValue(int depth)
        {
            if (depth > maxDepth) throw Fail("The nesting is too deep.");
            if (End) throw Fail("A value is expected.");
            int start = _i;
            switch (Cur)
            {
                case '"': { var t = ParseBasicString(); return new DataNode(DataKind.String, start, _i) { Text = t }; }
                case '\'': { var t = ParseLiteralString(); return new DataNode(DataKind.String, start, _i) { Text = t }; }
                case '[': return ParseArray(depth);
                case '{': return ParseInlineTable(depth);
            }
            while (!End && Cur is not (',' or ']' or '}' or '#' or '\r' or '\n')) _i++;
            var raw = s[start.._i].TrimEnd();
            int end = start + raw.Length;
            _i = end;
            if (raw == "true") return new DataNode(DataKind.Boolean, start, end) { Bool = true };
            if (raw == "false") return new DataNode(DataKind.Boolean, start, end);
            if (TryNumber(raw, out var number)) return new DataNode(DataKind.Number, start, end) { Text = number };
            // a date or time: digits with '-' or ':'; the value stays text
            if (raw.Length > 0 && char.IsAsciiDigit(raw[0]) && raw.Any(c => c is '-' or ':')) return new DataNode(DataKind.String, start, end) { Text = raw };
            _i = start;
            throw Fail($"Cannot read the value '{raw}'.", Math.Max(raw.Length, 1));
        }

        private static bool TryNumber(string raw, out string text)
        {
            text = "";
            var n = raw.Replace("_", "", StringComparison.Ordinal);
            switch (n)
            {
                case "inf" or "+inf": text = "Infinity"; return true;
                case "-inf": text = "-Infinity"; return true;
                case "nan" or "+nan" or "-nan": text = "NaN"; return true;
            }
            if (n.Length > 2 && n[0] == '0' && n[1] is 'x' or 'o' or 'b')
            {
                var style = n[1] == 'x' ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
                if (n[1] == 'x' && long.TryParse(n.AsSpan(2), style, CultureInfo.InvariantCulture, out var h)) { text = h.ToString(CultureInfo.InvariantCulture); return true; }
                if (n[1] != 'x' && TryRadix(n.AsSpan(2), n[1] == 'o' ? 8 : 2, out var r)) { text = r.ToString(CultureInfo.InvariantCulture); return true; }
                return false;
            }
            if (n.Length == 0 || !(char.IsAsciiDigit(n[^1]))) return false;
            if (long.TryParse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) { text = l.ToString(CultureInfo.InvariantCulture); return true; }
            if (n.Contains('-', StringComparison.Ordinal) && n.IndexOf('-') > 0 && !n.Contains('e', StringComparison.OrdinalIgnoreCase)) return false; // a date
            if (n.Contains(':', StringComparison.Ordinal)) return false;
            if (double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { text = d.ToString("R", CultureInfo.InvariantCulture); return true; }
            return false;
        }

        private static bool TryRadix(ReadOnlySpan<char> digits, int radix, out long value)
        {
            value = 0;
            if (digits.IsEmpty) return false;
            foreach (var c in digits)
            {
                int d = c - '0';
                if (d < 0 || d >= radix) return false;
                value = checked(value * radix + d);
            }
            return true;
        }

        private DataNode ParseArray(int depth)
        {
            var node = new DataNode(DataKind.Array, _i, _i);
            _closed.Add(node);
            _i++;
            while (true)
            {
                SkipTrivia();
                if (End) throw Fail("']' is expected.");
                if (Cur == ']') { _i++; node.End = _i; return node; }
                node.Items.Add(ParseValue(depth + 1));
                SkipTrivia();
                if (!End && Cur == ',') _i++;
                else if (End || Cur != ']') throw Fail("',' or ']' is expected.");
            }
        }

        private DataNode ParseInlineTable(int depth)
        {
            var node = new DataNode(DataKind.Object, _i, _i);
            _i++;
            while (true)
            {
                SkipTrivia();
                if (End) throw Fail("'}' is expected.");
                if (Cur == '}') { _i++; node.End = _i; _closed.Add(node); return node; }
                AssignKeyValue(node, depth + 1);
                SkipTrivia();
                if (!End && Cur == ',') _i++;
                else if (End || Cur != '}') throw Fail("',' or '}' is expected.");
            }
        }

        private string ParseLiteralString()
        {
            int open = _i;
            if (string.CompareOrdinal(s, _i, "'''", 0, 3) == 0)
            {
                _i += 3;
                if (!End && Cur == '\r') _i++;
                if (!End && Cur == '\n') _i++;
                int close = s.IndexOf("'''", _i, StringComparison.Ordinal);
                if (close < 0) throw new DataSyntaxException("The multi-line string is not closed.", open, s.Length);
                var text = s[_i..close].Replace("\r\n", "\n", StringComparison.Ordinal);
                _i = close + 3;
                return text;
            }
            int end = s.IndexOf('\'', _i + 1);
            int eol = s.IndexOf('\n', _i);
            if (end < 0 || (eol >= 0 && eol < end)) throw new DataSyntaxException("The string is not closed.", open, eol >= 0 ? eol : s.Length);
            var value = s[(_i + 1)..end];
            _i = end + 1;
            return value;
        }

        private string ParseBasicString()
        {
            int open = _i;
            bool multi = string.CompareOrdinal(s, _i, "\"\"\"", 0, 3) == 0;
            _i += multi ? 3 : 1;
            if (multi)
            {
                if (!End && Cur == '\r') _i++;
                if (!End && Cur == '\n') _i++;
            }
            var sb = new StringBuilder();
            while (true)
            {
                if (End) throw new DataSyntaxException("The string is not closed.", open, s.Length);
                char c = Cur;
                if (multi ? string.CompareOrdinal(s, _i, "\"\"\"", 0, 3) == 0 : c == '"')
                {
                    _i += multi ? 3 : 1;
                    return sb.ToString();
                }
                if (c == '\n' && !multi) throw new DataSyntaxException("The string is not closed.", open, _i);
                if (multi && c == '\r' && _i + 1 < s.Length && s[_i + 1] == '\n') { sb.Append('\n'); _i += 2; continue; }
                if (c != '\\') { sb.Append(c); _i++; continue; }
                _i++;
                if (End) throw new DataSyntaxException("The string is not closed.", open, s.Length);
                char e = Cur;
                _i++;
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'e': sb.Append('\u001b'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u': sb.Append(Hex(4)); break;
                    case 'U': sb.Append(Hex(8)); break;
                    case '\r' or '\n' or ' ' or '\t' when multi:
                        _i--;
                        while (!End && Cur is ' ' or '\t' or '\r' or '\n') _i++;
                        break;
                    default: _i -= 2; throw Fail($"Unknown escape '\\{e}'.", 2);
                }
            }
        }

        private string Hex(int digits)
        {
            if (_i + digits > s.Length || !int.TryParse(s.AsSpan(_i, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code) || code is < 0 or > 0x10FFFF)
                throw Fail("A bad \\u escape.", digits);
            _i += digits;
            return char.ConvertFromUtf32(code);
        }
    }
}

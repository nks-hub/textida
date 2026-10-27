using System.Globalization;
using System.Text;

namespace NksHub.Textida.McpClient;

/// <summary>
/// The part of TOML that a configuration file of an agent uses, enough to read its tables: tables and arrays of tables, dotted keys, strings (basic, literal, multi-line),
/// integers, floats, booleans, dates (kept as text), arrays and inline tables. Tables are <see cref="Dictionary{TKey, TValue}"/>, arrays <see cref="List{T}"/>,
/// numbers <see cref="long"/> or <see cref="double"/>. A file that is not valid TOML throws <see cref="FormatException"/> with the line.
/// </summary>
public static class MiniToml
{
    public static Dictionary<string, object> Parse(string text)
    {
        var p = new Parser(text);
        return p.ParseDocument();
    }

    private sealed class Parser(string s)
    {
        private int _i;

        private FormatException Fail(string what)
        {
            int line = 1;
            for (int k = 0; k < _i && k < s.Length; k++) if (s[k] == '\n') line++;
            return new FormatException($"TOML line {line}: {what}");
        }

        private bool End => _i >= s.Length;
        private char Cur => s[_i];

        private void SkipBlanks()
        {
            while (!End && (Cur == ' ' || Cur == '\t')) _i++;
        }

        private void SkipTrivia()
        {
            while (!End)
            {
                if (Cur is ' ' or '\t' or '\r' or '\n') _i++;
                else if (Cur == '#') SkipComment();
                else break;
            }
        }

        private void SkipComment()
        {
            while (!End && Cur != '\n') _i++;
        }

        private void EndOfLine()
        {
            SkipBlanks();
            if (End) return;
            if (Cur == '#') { SkipComment(); return; }
            if (Cur == '\r') _i++;
            if (!End && Cur == '\n') return;
            throw Fail("unexpected text after a value");
        }

        public Dictionary<string, object> ParseDocument()
        {
            var root = new Dictionary<string, object>();
            var current = root;
            while (true)
            {
                SkipTrivia();
                if (End) return root;
                if (Cur == '[')
                {
                    bool array = _i + 1 < s.Length && s[_i + 1] == '[';
                    _i += array ? 2 : 1;
                    var path = ParseKeyPath();
                    SkipBlanks();
                    if (End || Cur != ']') throw Fail("']' expected");
                    _i++;
                    if (array)
                    {
                        if (End || Cur != ']') throw Fail("']]' expected");
                        _i++;
                    }
                    current = array ? NewArrayItem(root, path) : Descend(root, path);
                    EndOfLine();
                    continue;
                }
                var key = ParseKeyPath();
                SkipBlanks();
                if (End || Cur != '=') throw Fail("'=' expected");
                _i++;
                SkipBlanks();
                var value = ParseValue();
                var target = key.Count > 1 ? Descend(current, key.Take(key.Count - 1).ToList()) : current;
                if (!target.TryAdd(key[^1], value)) throw Fail($"the key '{key[^1]}' is defined twice");
                EndOfLine();
            }
        }

        private Dictionary<string, object> Descend(Dictionary<string, object> from, IReadOnlyList<string> path)
        {
            var table = from;
            foreach (var part in path)
            {
                if (!table.TryGetValue(part, out var next)) table[part] = next = new Dictionary<string, object>();
                if (next is List<object> list && list.Count > 0 && list[^1] is Dictionary<string, object> last) next = last;
                table = next as Dictionary<string, object> ?? throw Fail($"'{part}' is not a table");
            }
            return table;
        }

        private Dictionary<string, object> NewArrayItem(Dictionary<string, object> root, List<string> path)
        {
            var parent = path.Count > 1 ? Descend(root, path.Take(path.Count - 1).ToList()) : root;
            if (!parent.TryGetValue(path[^1], out var existing)) parent[path[^1]] = existing = new List<object>();
            var item = new Dictionary<string, object>();
            (existing as List<object> ?? throw Fail($"'{path[^1]}' is not an array of tables")).Add(item);
            return item;
        }

        private List<string> ParseKeyPath()
        {
            var path = new List<string>();
            while (true)
            {
                SkipBlanks();
                if (End) throw Fail("a key is expected");
                if (Cur == '"') path.Add(ParseBasicString());
                else if (Cur == '\'') path.Add(ParseLiteralString());
                else
                {
                    int start = _i;
                    while (!End && (char.IsAsciiLetterOrDigit(Cur) || Cur is '_' or '-')) _i++;
                    if (_i == start) throw Fail("a key is expected");
                    path.Add(s[start.._i]);
                }
                SkipBlanks();
                if (!End && Cur == '.') { _i++; continue; }
                return path;
            }
        }

        private object ParseValue()
        {
            if (End) throw Fail("a value is expected");
            switch (Cur)
            {
                case '"': return ParseBasicString();
                case '\'': return ParseLiteralString();
                case '[': return ParseArray();
                case '{': return ParseInlineTable();
            }
            int start = _i;
            while (!End && Cur is not (',' or ']' or '}' or '#' or '\r' or '\n')) _i++;
            var raw = s[start.._i].Trim();
            if (raw == "true") return true;
            if (raw == "false") return false;
            var number = raw.Replace("_", "", StringComparison.Ordinal);
            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            if (raw.Length > 0 && (char.IsAsciiDigit(raw[0]) || raw is "inf" or "nan" or "+inf" or "-inf")) return raw; // a date, time or special float
            throw Fail($"cannot read the value '{raw}'");
        }

        private List<object> ParseArray()
        {
            _i++;
            var list = new List<object>();
            while (true)
            {
                SkipTrivia();
                if (End) throw Fail("']' expected");
                if (Cur == ']') { _i++; return list; }
                list.Add(ParseValue());
                SkipTrivia();
                if (!End && Cur == ',') _i++;
                else if (End || Cur != ']') throw Fail("',' or ']' expected");
            }
        }

        private Dictionary<string, object> ParseInlineTable()
        {
            _i++;
            var table = new Dictionary<string, object>();
            while (true)
            {
                SkipTrivia();
                if (End) throw Fail("'}' expected");
                if (Cur == '}') { _i++; return table; }
                var key = ParseKeyPath();
                SkipBlanks();
                if (End || Cur != '=') throw Fail("'=' expected");
                _i++;
                SkipBlanks();
                var value = ParseValue();
                var target = key.Count > 1 ? Descend(table, key.Take(key.Count - 1).ToList()) : table;
                if (!target.TryAdd(key[^1], value)) throw Fail($"the key '{key[^1]}' is defined twice");
                SkipTrivia();
                if (!End && Cur == ',') _i++;
                else if (End || Cur != '}') throw Fail("',' or '}' expected");
            }
        }

        private string ParseLiteralString()
        {
            if (string.CompareOrdinal(s, _i, "'''", 0, 3) == 0)
            {
                _i += 3;
                if (!End && Cur == '\r') _i++;
                if (!End && Cur == '\n') _i++;
                int close = s.IndexOf("'''", _i, StringComparison.Ordinal);
                if (close < 0) throw Fail("the multi-line string is not closed");
                // a file saved on Windows has CRLF inside the string; TOML lets a reader normalise the newline
                var text = s[_i..close].Replace("\r\n", "\n", StringComparison.Ordinal);
                _i = close + 3;
                return text;
            }
            int end = s.IndexOf('\'', _i + 1);
            int eol = s.IndexOf('\n', _i);
            if (end < 0 || (eol >= 0 && eol < end)) throw Fail("the string is not closed");
            var value = s[(_i + 1)..end];
            _i = end + 1;
            return value;
        }

        private string ParseBasicString()
        {
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
                if (End) throw Fail("the string is not closed");
                char c = Cur;
                if (multi ? string.CompareOrdinal(s, _i, "\"\"\"", 0, 3) == 0 : c == '"')
                {
                    _i += multi ? 3 : 1;
                    return sb.ToString();
                }
                if (c == '\n' && !multi) throw Fail("the string is not closed");
                if (multi && c == '\r' && _i + 1 < s.Length && s[_i + 1] == '\n') { sb.Append('\n'); _i += 2; continue; }
                if (c != '\\') { sb.Append(c); _i++; continue; }
                _i++;
                if (End) throw Fail("the string is not closed");
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
                        while (!End && Cur is ' ' or '\t' or '\r' or '\n') _i++; // a backslash at the end of a line removes the white space after it
                        break;
                    default: throw Fail($"unknown escape '\\{e}'");
                }
            }
        }

        private string Hex(int digits)
        {
            if (_i + digits > s.Length || !int.TryParse(s.AsSpan(_i, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) || code is < 0 or > 0x10FFFF)
                throw Fail("a bad \\u escape");
            _i += digits;
            return char.ConvertFromUtf32(code);
        }
    }
}

using System.Globalization;
using System.Text;

using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>Reads JSON (strict) or JSONC (comments and trailing commas) into <see cref="DataNode"/>s with offsets.</summary>
public static class JsonDataReader
{
    /// <param name="jsonc">Accept <c>//</c> and <c>/* */</c> comments and trailing commas.</param>
    /// <param name="maxDepth">Deepest nesting accepted; there is no default, the caller decides.</param>
    public static bool TryParse(string text, bool jsonc, int maxDepth, out DataNode? node, out DataSyntaxException? error)
    {
        try
        {
            node = new Reader(text, jsonc, maxDepth).ParseDocument();
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

    private sealed class Reader(string s, bool jsonc, int maxDepth)
    {
        private int _i;

        private DataSyntaxException Fail(string what, int length = 1) => new(what, Math.Min(_i, s.Length), Math.Min(_i + length, s.Length));

        public DataNode ParseDocument()
        {
            Skip();
            if (_i >= s.Length) throw Fail("A value is expected.", 0);
            var v = ParseValue(1);
            Skip();
            if (_i < s.Length) throw Fail("Unexpected text after the value.");
            return v;
        }

        private void Skip()
        {
            while (_i < s.Length)
            {
                char c = s[_i];
                if (c is ' ' or '\t' or '\r' or '\n') _i++;
                else if (jsonc && c == '/' && _i + 1 < s.Length && s[_i + 1] == '/')
                {
                    while (_i < s.Length && s[_i] != '\n') _i++;
                }
                else if (jsonc && c == '/' && _i + 1 < s.Length && s[_i + 1] == '*')
                {
                    int close = s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    if (close < 0) throw Fail("The comment is not closed.", s.Length - _i);
                    _i = close + 2;
                }
                else break;
            }
        }

        private DataNode ParseValue(int depth)
        {
            if (depth > maxDepth) throw Fail("The nesting is too deep.");
            if (_i >= s.Length) throw Fail("A value is expected.");
            char c = s[_i];
            switch (c)
            {
                case '{': return ParseObject(depth);
                case '[': return ParseArray(depth);
                case '"':
                {
                    int start = _i;
                    var text = ParseString();
                    return new DataNode(DataKind.String, start, _i) { Text = text };
                }
                case 't': return Literal("true", DataKind.Boolean, true);
                case 'f': return Literal("false", DataKind.Boolean, false);
                case 'n': return Literal("null", DataKind.Null, false);
                default:
                    if (c == '-' || char.IsAsciiDigit(c)) return ParseNumber();
                    throw Fail("A value is expected.");
            }
        }

        private DataNode Literal(string word, DataKind kind, bool value)
        {
            if (string.CompareOrdinal(s, _i, word, 0, word.Length) != 0) throw Fail("A value is expected.");
            var n = new DataNode(kind, _i, _i + word.Length) { Bool = value };
            _i += word.Length;
            return n;
        }

        private DataNode ParseNumber()
        {
            int start = _i;
            if (s[_i] == '-') _i++;
            if (_i >= s.Length || !char.IsAsciiDigit(s[_i])) throw Fail("A digit is expected.");
            if (s[_i] == '0') _i++;
            else while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            if (_i < s.Length && s[_i] == '.')
            {
                _i++;
                if (_i >= s.Length || !char.IsAsciiDigit(s[_i])) throw Fail("A digit is expected after the decimal point.");
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            }
            if (_i < s.Length && s[_i] is 'e' or 'E')
            {
                _i++;
                if (_i < s.Length && s[_i] is '+' or '-') _i++;
                if (_i >= s.Length || !char.IsAsciiDigit(s[_i])) throw Fail("A digit is expected in the exponent.");
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            }
            return new DataNode(DataKind.Number, start, _i) { Text = s[start.._i] };
        }

        private string ParseString()
        {
            int open = _i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_i >= s.Length) throw new DataSyntaxException("The string is not closed.", open, s.Length);
                char c = s[_i];
                if (c == '"') { _i++; return sb.ToString(); }
                if (c < 0x20) throw Fail("A control character in a string must be escaped.");
                if (c != '\\') { sb.Append(c); _i++; continue; }
                _i++;
                if (_i >= s.Length) throw new DataSyntaxException("The string is not closed.", open, s.Length);
                char e = s[_i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_i + 4 > s.Length || !int.TryParse(s.AsSpan(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw Fail("A \\u escape needs four hexadecimal digits.", 4);
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: _i--; throw Fail($"Unknown escape '\\{e}'.", 2);
                }
            }
        }

        private DataNode ParseArray(int depth)
        {
            var node = new DataNode(DataKind.Array, _i, _i);
            _i++;
            while (true)
            {
                Skip();
                if (_i >= s.Length) throw Fail("']' is expected.");
                if (s[_i] == ']') { _i++; node.End = _i; return node; }
                node.Items.Add(ParseValue(depth + 1));
                Skip();
                if (_i < s.Length && s[_i] == ',')
                {
                    _i++;
                    Skip();
                    if (!jsonc && _i < s.Length && s[_i] == ']') throw Fail("A trailing comma is not allowed in JSON.");
                }
                else if (_i >= s.Length || s[_i] != ']') throw Fail("',' or ']' is expected.");
            }
        }

        private DataNode ParseObject(int depth)
        {
            var node = new DataNode(DataKind.Object, _i, _i);
            _i++;
            while (true)
            {
                Skip();
                if (_i >= s.Length) throw Fail("'}' is expected.");
                if (s[_i] == '}') { _i++; node.End = _i; return node; }
                if (s[_i] != '"') throw Fail("A property name in double quotes is expected.");
                int keyStart = _i;
                var name = ParseString();
                int keyEnd = _i;
                Skip();
                if (_i >= s.Length || s[_i] != ':') throw Fail("':' is expected.");
                _i++;
                Skip();
                node.Members.Add(new DataMember(name, keyStart, keyEnd, ParseValue(depth + 1)));
                Skip();
                if (_i < s.Length && s[_i] == ',')
                {
                    _i++;
                    Skip();
                    if (!jsonc && _i < s.Length && s[_i] == '}') throw Fail("A trailing comma is not allowed in JSON.");
                }
                else if (_i >= s.Length || s[_i] != '}') throw Fail("',' or '}' is expected.");
            }
        }
    }
}

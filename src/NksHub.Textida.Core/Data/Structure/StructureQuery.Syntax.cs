using System.Globalization;
using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

public sealed partial class StructureQuery
{
    private sealed class QueryParser(string text, StructureQueryLanguage language)
    {
        private int at;
        private readonly List<Step> parsed = [];

        public StructureQuery Parse()
        {
            Space();
            if (language == StructureQueryLanguage.JsonPath) Json();
            else if (language == StructureQueryLanguage.XPath) Xml();
            else throw new ArgumentOutOfRangeException(nameof(language));
            Space();
            if (at != text.Length) throw Error("Unsupported query syntax.");
            return new(text, language, parsed.ToArray());
        }

        private void Json()
        {
            Require('$');
            while (true)
            {
                Space();
                if (at == text.Length) return;
                int position = at;
                Axis axis = Axis.Child;
                if (Take('.'))
                {
                    if (Take('.')) axis = Axis.Descendant;
                    if (Take('*')) Add(new(axis, Test.JsonWildcard, null, -1, [], position));
                    else if (Peek() == '[') JsonBracket(axis, position);
                    else Add(new(axis, Test.JsonName, Identifier(false), -1, [], position));
                }
                else if (Peek() == '[') JsonBracket(axis, position);
                else throw Error("Expected a member, index, wildcard or filter.");
            }
        }

        private void JsonBracket(Axis axis, int position)
        {
            Require('[');
            Space();
            if (Peek() is '\'' or '"') Add(new(axis, Test.JsonName, Quoted(true), -1, [], position));
            else if (Take('*')) Add(new(axis, Test.JsonWildcard, null, -1, [], position));
            else if (Take('?'))
            {
                Space();
                bool parentheses = Take('(');
                Space();
                Require('@');
                var path = new List<JsonPart>();
                while (true)
                {
                    Space();
                    if (Take('.')) path.Add(new(Identifier(false), null));
                    else if (Take('['))
                    {
                        Space();
                        if (Peek() is '\'' or '"') path.Add(new(Quoted(true), null));
                        else path.Add(new(null, Integer(false)));
                        Space();
                        Require(']');
                    }
                    else break;
                    if (path.Count > MaxSteps) throw Error("The filter path is too long.");
                }
                Space();
                Require('=');
                Require('=');
                Space();
                var literal = JsonLiteral();
                Space();
                if (parentheses) Require(')');
                Add(new(axis, Test.JsonWildcard, null, -1, [new(Value: literal, JsonPath: path.ToArray())], position));
            }
            else Add(new(axis, Test.JsonIndex, null, Integer(false), [], position));
            Space();
            Require(']');
        }

        private Literal JsonLiteral()
        {
            if (Peek() is '\'' or '"') return new(LiteralKind.String, Quoted(true));
            if (Word("true")) return new(LiteralKind.Boolean, "true");
            if (Word("false")) return new(LiteralKind.Boolean, "false");
            if (Word("null")) return new(LiteralKind.Null, "null");
            int start = at;
            Take('-');
            if (Take('0'))
            {
                if (char.IsAsciiDigit(Peek())) throw Error("A number cannot have leading zeros.");
            }
            else
            {
                if (Peek() is < '1' or > '9') throw Error("Expected a scalar JSON literal.");
                while (char.IsAsciiDigit(Peek())) at++;
            }
            if (Take('.')) Digits();
            if (Take('e') || Take('E'))
            {
                if (!Take('+')) Take('-');
                Digits();
            }
            return new(LiteralKind.Number, text[start..at]);
        }

        private void Xml()
        {
            Axis axis = Axis.Child;
            if (Take('/'))
            {
                if (Take('/')) axis = Axis.Descendant;
                else { Space(); if (at == text.Length) return; }
            }
            else if (Take('.'))
            {
                if (at == text.Length) return;
                Require('/');
                if (Take('/')) axis = Axis.Descendant;
            }
            while (true)
            {
                Space();
                int position = at;
                bool attribute = Take('@');
                var (test, name) = XmlTest(attribute);
                var predicates = new List<Predicate>();
                Space();
                while (Take('['))
                {
                    Space();
                    predicates.Add(XmlPredicate());
                    Space();
                    Require(']');
                    Space();
                    if (predicates.Count > MaxSteps) throw Error("There are too many predicates.");
                }
                Add(new(axis, test, name, -1, predicates.ToArray(), position));
                if (at == text.Length) return;
                Require('/');
                axis = Take('/') ? Axis.Descendant : Axis.Child;
            }
        }

        private (Test Test, string? Name) XmlTest(bool attribute)
        {
            string name = QName();
            if (!attribute && Take('('))
            {
                Space();
                Require(')');
                return name switch
                {
                    "text" => (Test.XmlText, null),
                    "node" => (Test.XmlNode, null),
                    "comment" => (Test.XmlComment, null),
                    _ => throw Error("Unsupported XPath node test."),
                };
            }
            return (attribute ? Test.XmlAttribute : Test.XmlElement, name);
        }

        private Predicate XmlPredicate()
        {
            if (char.IsAsciiDigit(Peek())) return new(Position: Integer(true));
            Test test;
            string? name;
            bool self = Take('.');
            if (self) { test = Test.XmlNode; name = null; }
            else (test, name) = XmlTest(Take('@'));
            Space();
            if (Peek() == ']' && !self) return new(Test: test, Name: name);
            Require('=');
            Space();
            if (Peek() is not ('\'' or '"')) throw Error("XPath equality requires a quoted string.");
            var literal = new Literal(LiteralKind.String, Quoted(false));
            return new(Test: test, Name: name, Value: literal, SelfValue: self);
        }

        private string QName()
        {
            if (Take('*')) return "*";
            string first = Identifier(true);
            if (!Take(':')) return first;
            return first + ":" + (Take('*') ? "*" : Identifier(true));
        }

        private string Identifier(bool xml)
        {
            int start = at;
            if (!NameStart(Peek(), xml)) throw Error("Expected a name.");
            Scalar();
            while (at < text.Length && (NameStart(Peek(), xml) || char.IsAsciiDigit(Peek())
                || xml && Peek() is '-' or '.')) Scalar();
            return text[start..at];
        }

        private static bool NameStart(char c, bool xml) => c == '_' || !xml && c == '$'
            || char.IsLetter(c) || c >= 0x80 && !char.IsLowSurrogate(c) && !char.IsWhiteSpace(c);

        private void Scalar()
        {
            char c = text[at++];
            if (char.IsHighSurrogate(c))
            {
                if (at == text.Length || !char.IsLowSurrogate(text[at])) throw Error("An invalid Unicode scalar was found.");
                at++;
            }
            else if (char.IsLowSurrogate(c)) throw Error("An invalid Unicode scalar was found.");
        }

        private string Quoted(bool escapes)
        {
            char quote = text[at++];
            var result = new StringBuilder();
            while (at < text.Length && Peek() != quote)
            {
                if (escapes && Take('\\'))
                {
                    if (at == text.Length) throw Error("An escape is incomplete.");
                    char escape = text[at++];
                    if (escape == 'u')
                    {
                        char high = (char)Hex4();
                        result.Append(high);
                        if (char.IsHighSurrogate(high))
                        {
                            Require('\\');
                            Require('u');
                            char low = (char)Hex4();
                            if (!char.IsLowSurrogate(low)) throw Error("Expected a low surrogate escape.");
                            result.Append(low);
                        }
                        else if (char.IsLowSurrogate(high)) throw Error("An unpaired low surrogate escape was found.");
                    }
                    else result.Append(escape switch
                    {
                        '"' => '"', '\'' => '\'', '\\' => '\\', '/' => '/', 'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                        _ => throw Error("Unsupported string escape."),
                    });
                }
                else
                {
                    if (escapes && Peek() < ' ') throw Error("A JSON string contains an unescaped control character.");
                    int start = at;
                    Scalar();
                    result.Append(text, start, at - start);
                }
            }
            Require(quote);
            return result.ToString();
        }

        private int Hex4()
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                int digit = at == text.Length ? -1 : text[at] is >= '0' and <= '9' ? text[at] - '0'
                    : text[at] is >= 'a' and <= 'f' ? text[at] - 'a' + 10 : text[at] is >= 'A' and <= 'F' ? text[at] - 'A' + 10 : -1;
                if (digit < 0) throw Error("Expected four hexadecimal digits.");
                at++;
                value = (value << 4) | digit;
            }
            return value;
        }

        private long Integer(bool positive)
        {
            int start = at;
            Digits();
            if (!long.TryParse(text.AsSpan(start, at - start), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
                || positive && value == 0) throw Error("The index is outside the supported range.");
            return value;
        }

        private void Digits()
        {
            if (!char.IsAsciiDigit(Peek())) throw Error("Expected a digit.");
            while (char.IsAsciiDigit(Peek())) at++;
        }

        private bool Word(string value)
        {
            if (!text.AsSpan(at).StartsWith(value, StringComparison.Ordinal)) return false;
            at += value.Length;
            return true;
        }

        private void Add(Step step)
        {
            if (parsed.Count >= MaxSteps) throw Error("The query exceeds 256 steps.");
            parsed.Add(step);
        }

        private char Peek() => at < text.Length ? text[at] : '\0';
        private bool Take(char c) { if (Peek() != c) return false; at++; return true; }
        private void Require(char c) { if (!Take(c)) throw Error("Expected '" + c + "'."); }
        private void Space() { while (at < text.Length && char.IsWhiteSpace(text[at])) at++; }
        private StructureQueryException Error(string message) => new(message, at);
    }
}

using System.Text;

namespace NksHub.Textida.Plugins.Emmet;

/// <summary>An abbreviation that does not follow the grammar; the text is left alone.</summary>
public sealed class AbbreviationException(string message) : Exception(message);

/// <summary>The expansion would be longer than the limit the editor gave.</summary>
public sealed class ExpansionTooLargeException() : Exception("The expansion is too large.");

/// <summary>
/// One node of an HTML abbreviation: an element, or a group in parentheses (<see cref="Name"/> null) that only repeats and orders its children.
/// <see cref="Repeat"/> is the number after <c>*</c>; <see cref="ImplicitRepeat"/> is a bare <c>*</c>, which makes sense only when wrapping lines.
/// </summary>
public sealed class Node
{
    public string? Name { get; set; }
    public List<(string Name, string Value)> Attributes { get; } = [];
    public string Text { get; set; } = "";
    public int Repeat { get; set; } = 1;
    public bool ImplicitRepeat { get; set; }
    public List<Node> Children { get; } = [];
    public bool IsGroup => Name is null;

    internal void AddClass(string value)
    {
        int at = Attributes.FindIndex(a => a.Name == "class");
        if (at < 0) Attributes.Add(("class", value));
        else Attributes[at] = ("class", Attributes[at].Value.Length == 0 ? value : Attributes[at].Value + " " + value);
    }
}

/// <summary>
/// Parser of the HTML abbreviation syntax described in the public Emmet documentation (docs.emmet.io): <c>name#id.class[attr=value]{text}*N</c>,
/// the operators <c>&gt;</c> <c>+</c> <c>^</c> and groups in parentheses. Written for this editor from that description; no code or data of the Emmet projects is used.
/// </summary>
public static class AbbreviationParser
{
    public static List<Node> Parse(string text)
    {
        var parser = new Parser(text);
        var root = new Node();
        parser.Sequence(root, inGroup: false);
        if (parser.Position != text.Length) throw new AbbreviationException("Unexpected character.");
        return root.Children;
    }

    private sealed class Parser(string s)
    {
        private int _i;
        public int Position => _i;

        private bool More => _i < s.Length;

        private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '$' or '@';

        public void Sequence(Node container, bool inGroup)
        {
            var cursor = container;
            var ancestors = new Stack<Node>();
            while (true)
            {
                var unit = Unit();
                cursor.Children.Add(unit);
                if (!More) break;
                char c = s[_i];
                if (c == '+') { _i++; continue; }
                if (c == '>')
                {
                    if (unit.IsGroup) throw new AbbreviationException("A group cannot have children.");
                    _i++;
                    ancestors.Push(cursor);
                    cursor = unit;
                    continue;
                }
                if (c == '^')
                {
                    while (More && s[_i] == '^')
                    {
                        _i++;
                        if (ancestors.Count == 0) throw new AbbreviationException("Nothing to climb out of.");
                        cursor = ancestors.Pop();
                    }
                    if (More && s[_i] == '+') _i++;
                    if (!More) throw new AbbreviationException("Missing element.");
                    continue;
                }
                if (c == ')' && inGroup) break;
                throw new AbbreviationException("Unexpected character.");
            }
        }

        private Node Unit()
        {
            if (!More) throw new AbbreviationException("Missing element.");
            Node node;
            if (s[_i] == '(')
            {
                _i++;
                node = new Node();
                Sequence(node, inGroup: true);
                if (!More || s[_i] != ')') throw new AbbreviationException("Missing closing parenthesis.");
                _i++;
            }
            else node = Element();
            Multiplier(node);
            return node;
        }

        private Node Element()
        {
            var node = new Node();
            int start = _i;
            while (More && IsNameChar(s[_i])) _i++;
            node.Name = s[start.._i];
            bool any = node.Name.Length > 0;
            while (More)
            {
                char c = s[_i];
                if (c == '#') { _i++; node.Attributes.Add(("id", Word())); }
                else if (c == '.') { _i++; node.AddClass(Word()); }
                else if (c == '[') Attributes(node);
                else if (c == '{') node.Text += Text();
                else break;
                any = true;
            }
            if (!any) throw new AbbreviationException("Missing element.");
            return node;
        }

        private string Word()
        {
            int start = _i;
            while (More && IsNameChar(s[_i])) _i++;
            if (_i == start) throw new AbbreviationException("Missing name.");
            return s[start.._i];
        }

        private string Text()
        {
            _i++;
            var sb = new StringBuilder();
            int depth = 1;
            while (More)
            {
                char c = s[_i++];
                if (c == '\\' && More) { sb.Append(s[_i++]); continue; }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return sb.ToString();
                sb.Append(c);
            }
            throw new AbbreviationException("Missing closing brace.");
        }

        private void Attributes(Node node)
        {
            _i++;
            while (true)
            {
                while (More && (char.IsWhiteSpace(s[_i]) || s[_i] == ',')) _i++;
                if (!More) throw new AbbreviationException("Missing closing bracket.");
                if (s[_i] == ']') { _i++; return; }
                int start = _i;
                while (More && (IsNameChar(s[_i]) || s[_i] == '.')) _i++;
                if (_i == start) throw new AbbreviationException("Missing attribute name.");
                string name = s[start.._i];
                string value = "";
                if (More && s[_i] == '=')
                {
                    _i++;
                    if (More && s[_i] is '"' or '\'')
                    {
                        char quote = s[_i++];
                        int from = _i;
                        while (More && s[_i] != quote) _i++;
                        if (!More) throw new AbbreviationException("Missing closing quote.");
                        value = s[from.._i++];
                    }
                    else
                    {
                        int from = _i;
                        while (More && !char.IsWhiteSpace(s[_i]) && s[_i] != ']' && s[_i] != ',') _i++;
                        value = s[from.._i];
                    }
                }
                if (name == "class") node.AddClass(value);
                else node.Attributes.Add((name, value));
            }
        }

        private void Multiplier(Node node)
        {
            if (!More || s[_i] != '*') return;
            _i++;
            int start = _i;
            while (More && char.IsAsciiDigit(s[_i])) _i++;
            if (_i == start) { node.ImplicitRepeat = true; return; }
            if (!int.TryParse(s.AsSpan(start.._i), out var n) || n < 1) throw new AbbreviationException("The repeat count must be a positive number.");
            node.Repeat = n;
        }
    }
}

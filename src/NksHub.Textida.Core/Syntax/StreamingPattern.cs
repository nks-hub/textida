using System.Globalization;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

internal abstract record StreamNode;
internal sealed record StreamChar(StreamCharSet Characters) : StreamNode;
internal sealed record StreamSequence(IReadOnlyList<StreamNode> Items) : StreamNode;
internal sealed record StreamChoice(IReadOnlyList<StreamNode> Items) : StreamNode;
internal sealed record StreamRepeat(StreamNode Child, int Minimum, int? Maximum, bool Lazy) : StreamNode;
internal sealed record StreamLook(StreamNode Child, bool Positive, bool Behind) : StreamNode;
internal sealed record StreamAnchor(StreamAnchorKind Kind) : StreamNode;
internal enum StreamAnchorKind { Start, End, WordBoundary, NotWordBoundary }

internal static class StreamPattern
{
    internal static StreamNode Compile(string pattern, bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        _ = new Regex(pattern, options, TimeSpan.FromSeconds(1));
        return new Parser(pattern).Parse(caseSensitive);
    }

    private sealed class Parser(string pattern)
    {
        private int position;
        private int depth;

        internal StreamNode Parse(bool caseSensitive)
        {
            var result = Choice(caseSensitive);
            if (position != pattern.Length) throw Unsupported("Unexpected closing group");
            return result;
        }

        private StreamNode Choice(bool caseSensitive)
        {
            var choices = new List<StreamNode> { Sequence(caseSensitive) };
            while (Take('|')) choices.Add(Sequence(caseSensitive));
            return choices.Count == 1 ? choices[0] : new StreamChoice(choices.AsReadOnly());
        }

        private StreamNode Sequence(bool caseSensitive)
        {
            var items = new List<StreamNode>();
            while (position < pattern.Length && pattern[position] is not ('|' or ')'))
            {
                StreamNode child = Atom(caseSensitive);
                if (RepeatBounds(out int minimum, out int? maximum))
                    child = new StreamRepeat(child, minimum, maximum, Take('?'));
                items.Add(child);
            }
            return items.Count == 1 ? items[0] : new StreamSequence(items.AsReadOnly());
        }

        private StreamNode Atom(bool caseSensitive)
        {
            int start = position;
            char value = pattern[position++];
            return value switch
            {
                '(' => Group(caseSensitive),
                '[' => CharacterClass(start, caseSensitive),
                '\\' => Escape(start, caseSensitive),
                '^' => new StreamAnchor(StreamAnchorKind.Start),
                '$' => new StreamAnchor(StreamAnchorKind.End),
                '.' => Character(".", caseSensitive),
                '*' or '+' or '?' => throw Unsupported("A quantifier has no preceding atom"),
                _ => Character(Regex.Escape(value.ToString()), caseSensitive),
            };
        }

        private StreamNode Group(bool caseSensitive)
        {
            if (++depth > 128) throw Unsupported("Groups are nested too deeply");
            if (!Take('?')) throw Unsupported("Capturing groups are not supported");
            bool look = false, positive = true, behind = false;
            if (Take(':')) { }
            else if (Take('=')) look = true;
            else if (Take('!')) (look, positive) = (true, false);
            else if (Take('<'))
            {
                behind = true;
                look = true;
                if (Take('=')) positive = true;
                else if (Take('!')) positive = false;
                else throw Unsupported("Named and balancing groups are not supported");
            }
            else if (Take('i'))
            {
                if (!Take(':')) throw Unsupported("Only scoped ignore-case options are supported");
                caseSensitive = false;
            }
            else if (Take('-'))
            {
                if (!Take('i') || !Take(':')) throw Unsupported("Only scoped ignore-case options are supported");
                caseSensitive = true;
            }
            else throw Unsupported("This group construct is not supported");

            var child = Choice(caseSensitive);
            if (!Take(')')) throw Unsupported("The group is not closed");
            depth--;
            return look ? new StreamLook(child, positive, behind) : child;
        }

        private StreamNode CharacterClass(int start, bool caseSensitive)
        {
            ScanClass();
            return Character(pattern[start..position], caseSensitive);
        }

        private void ScanClass()
        {
            if (++depth > 128) throw Unsupported("Character classes are nested too deeply");
            Take('^');
            bool first = true;
            while (position < pattern.Length)
            {
                char value = pattern[position++];
                if (value == ']' && !first) { depth--; return; }
                if (value == '\\')
                {
                    if (position == pattern.Length) break;
                    position++;
                }
                else if (value == '-' && position < pattern.Length && pattern[position] == '[')
                {
                    position++;
                    ScanClass();
                }
                first = false;
            }
            throw Unsupported("The character class is not closed");
        }

        private StreamNode Escape(int start, bool caseSensitive)
        {
            if (position == pattern.Length) throw Unsupported("The escape is incomplete");
            char escaped = pattern[position++];
            if (escaped == 'b') return new StreamAnchor(StreamAnchorKind.WordBoundary);
            if (escaped == 'B') return new StreamAnchor(StreamAnchorKind.NotWordBoundary);
            if (escaped is 'A' or 'Z' or 'z' or 'G') throw Unsupported("This anchor is not supported");
            if (char.IsAsciiDigit(escaped) || escaped == 'k') throw Unsupported("Numeric escapes and backreferences are not supported");
            if (escaped is 'p' or 'P')
            {
                if (!Take('{')) throw Unsupported("A Unicode category needs braces");
                while (position < pattern.Length && pattern[position] != '}') position++;
                if (!Take('}')) throw Unsupported("The Unicode category is not closed");
            }
            else if (escaped is 'x' or 'u')
            {
                // .NET regex uses exactly two digits for \x and four for \u.
                position += escaped == 'x' ? 2 : 4;
                if (position > pattern.Length) throw Unsupported("The character escape is incomplete");
            }
            else if (escaped == 'c')
            {
                if (position == pattern.Length) throw Unsupported("The control escape is incomplete");
                position++;
            }
            return Character(pattern[start..position], caseSensitive);
        }

        private bool RepeatBounds(out int minimum, out int? maximum)
        {
            minimum = 0;
            maximum = null;
            if (Take('*')) return true;
            if (Take('+')) { minimum = 1; return true; }
            if (Take('?')) { maximum = 1; return true; }
            if (position == pattern.Length || pattern[position] != '{') return false;

            int end = position + 1;
            while (end < pattern.Length && char.IsAsciiDigit(pattern[end])) end++;
            if (end == position + 1) return false;
            int minimumEnd = end;
            int maximumStart = end;
            if (end < pattern.Length && pattern[end] == ',')
            {
                maximumStart = ++end;
                while (end < pattern.Length && char.IsAsciiDigit(pattern[end])) end++;
            }
            if (end == pattern.Length || pattern[end] != '}') return false;
            minimum = int.Parse(pattern.AsSpan(position + 1, minimumEnd - position - 1), CultureInfo.InvariantCulture);
            maximum = minimumEnd == end ? minimum
                : maximumStart == end ? null
                : int.Parse(pattern.AsSpan(maximumStart, end - maximumStart), CultureInfo.InvariantCulture);
            position = end + 1;
            return true;
        }

        private bool Take(char value)
        {
            if (position == pattern.Length || pattern[position] != value) return false;
            position++;
            return true;
        }

        private static StreamChar Character(string expression, bool caseSensitive) => new(new StreamCharSet(expression, caseSensitive));
        private NotSupportedException Unsupported(string reason) => new($"{reason} at character {position + 1} of '{pattern}'.");
    }
}

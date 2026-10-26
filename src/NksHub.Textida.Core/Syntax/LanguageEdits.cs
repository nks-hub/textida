namespace NksHub.Textida.Core.Syntax;

/// <summary>Replace <see cref="Length"/> characters at <see cref="Column"/> of <see cref="Line"/> (0-based, columns of the original line) with <see cref="Text"/>.</summary>
public readonly record struct LineEdit(int Line, int Column, int Length, string Text);

/// <summary>
/// Toggle comment over whole lines with the markers of the language at each line's first non-blank character, so a line inside
/// a script block of HTML gets <c>//</c> and a line of the HTML around it <c>&lt;!-- --&gt;</c>.
/// shortcut: tokenizes from the first line up to the last one touched, like <see cref="BracketMatcher"/>; a paged document needs checkpoints (P3).
/// </summary>
public static class CommentToggle
{
    /// <summary>
    /// The edits for the given line ranges (0-based, inclusive). Within a range the lines of one language are commented when
    /// any non-blank one is not commented yet, otherwise uncommented. Blank lines and languages without markers are left alone.
    /// Only markers are inserted and removed, so positions in the rest of a line keep their meaning. Sorted by line and column; apply from the end.
    /// </summary>
    public static IReadOnlyList<LineEdit> Toggle(IReadOnlyList<string> lines, Tokenizer? tokenizer, IEnumerable<(int First, int Last)> ranges)
    {
        var result = new List<LineEdit>();
        if (tokenizer is null || lines.Count == 0) return result;
        var clamped = ranges.Select(r => (First: Math.Clamp(Math.Min(r.First, r.Last), 0, lines.Count - 1), Last: Math.Clamp(Math.Max(r.First, r.Last), 0, lines.Count - 1)))
            .OrderBy(r => r.First).ToList();
        if (clamped.Count == 0) return result;
        var languages = Languages(lines, tokenizer, clamped.Max(r => r.Last));

        var done = new HashSet<int>();
        foreach (var (first, last) in clamped)
        {
            foreach (var group in Enumerable.Range(first, last - first + 1).Where(n => !done.Contains(n) && lines[n].Trim().Length > 0).GroupBy(n => languages[n]))
            {
                var g = group.Key;
                if (g.LineComment is null && g.BlockComment is null) continue;
                var members = group.ToList();
                bool uncomment = members.All(n => IsCommented(g, lines[n]));
                int indent = members.Min(n => Indent(lines[n]));
                foreach (int n in members)
                {
                    if (uncomment) Uncomment(g, lines[n], n, result);
                    else Comment(g, lines[n], n, indent, result);
                }
            }
            for (int n = first; n <= last; n++) done.Add(n);
        }
        result.Sort((a, b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column));
        return result;
    }

    /// <summary>The grammar in charge at the first non-blank character of each line up to <paramref name="last"/>.</summary>
    private static Grammar[] Languages(IReadOnlyList<string> lines, Tokenizer tokenizer, int last)
    {
        var languages = new Grammar[last + 1];
        var state = tokenizer.InitialState;
        var scratch = new List<Token>();
        for (int n = 0; n <= last; n++)
        {
            string line = lines[n];
            var at = state;
            tokenizer.Scan(line[..Indent(line)], 0, true, ref at, scratch, 0);
            languages[n] = LanguageOf(at, tokenizer.Grammar);
            scratch.Clear();
            state = tokenizer.TokenizeLine(line, state, scratch);
            scratch.Clear();
        }
        return languages;
    }

    private static Grammar LanguageOf(LineState state, Grammar root)
    {
        var context = state.Context;
        if (context.Embed is { } guest) return root.Registry.Find(guest) ?? context.Owner;
        return context.Owner;
    }

    private static int Indent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t') i++;
        return i;
    }

    private static bool IsCommented(Grammar g, string line)
    {
        var body = line.AsSpan().Trim();
        if (g.LineComment is { } marker) return body.StartsWith(marker, StringComparison.Ordinal);
        var (open, close) = g.BlockComment!.Value;
        return body.Length >= open.Length + close.Length && body.StartsWith(open, StringComparison.Ordinal) && body.EndsWith(close, StringComparison.Ordinal);
    }

    private static void Comment(Grammar g, string line, int n, int indent, List<LineEdit> edits)
    {
        if (g.LineComment is { } marker)
        {
            edits.Add(new LineEdit(n, indent, 0, marker + " "));
            return;
        }
        var (open, close) = g.BlockComment!.Value;
        int end = line.TrimEnd().Length;
        edits.Add(new LineEdit(n, indent, 0, open + " "));
        edits.Add(new LineEdit(n, end, line.Length - end, " " + close));
    }

    private static void Uncomment(Grammar g, string line, int n, List<LineEdit> edits)
    {
        int at = Indent(line);
        if (g.LineComment is { } marker)
        {
            int after = at + marker.Length;
            edits.Add(new LineEdit(n, at, after - at + (after < line.Length && line[after] == ' ' ? 1 : 0), ""));
            return;
        }
        var (open, close) = g.BlockComment!.Value;
        int bodyStart = at + open.Length;
        if (bodyStart < line.Length && line[bodyStart] == ' ') bodyStart++;
        int closeAt = line.TrimEnd().Length - close.Length;
        int bodyEnd = closeAt > bodyStart && line[closeAt - 1] == ' ' ? closeAt - 1 : closeAt;
        edits.Add(new LineEdit(n, at, bodyStart - at, ""));
        edits.Add(new LineEdit(n, Math.Max(bodyEnd, bodyStart), closeAt + close.Length - Math.Max(bodyEnd, bodyStart), ""));
    }
}

/// <summary>
/// Expand selection: a caret takes its word, then each step takes the content of the enclosing bracket pair, then the pair itself,
/// then the next pair out, then the whole lines and finally the whole text. Brackets in strings and comments do not count.
/// shortcut: tokenizes from the start of the document like <see cref="BracketMatcher"/>; a paged document needs checkpoints (P3).
/// </summary>
public static class SelectionGrowth
{
    public static (TextPosition Start, TextPosition End) Expand(IReadOnlyList<string> lines, Tokenizer? tokenizer, TextPosition start, TextPosition end)
    {
        if (Compare(start, end) > 0) (start, end) = (end, start);
        if (lines.Count == 0) return (start, end);
        if (Compare(start, end) == 0 && Word(lines[start.Line], start.Column) is { } word)
            return (new TextPosition(start.Line, word.Start), new TextPosition(start.Line, word.End));

        var tokens = new BracketMatcher.LineTokens(lines, tokenizer);
        var from = start;
        while (Previous(lines, from) is { } p)
        {
            var open = UnmatchedOpenBefore(lines, tokens, p);
            if (open is not { } o) break;
            // a pair that closes before the selection ends does not enclose it
            if (BracketMatcher.FindPair(lines, tokenizer, o.Line, o.Column) is { } c && Compare(c, end) >= 0)
            {
                var contentStart = new TextPosition(o.Line, o.Column + 1);
                if (Compare(contentStart, start) != 0 || Compare(c, end) != 0) return (contentStart, c);
                return (o, new TextPosition(c.Line, c.Column + 1));
            }
            from = o;
        }

        var lineStart = new TextPosition(start.Line, 0);
        var lineEnd = new TextPosition(end.Line, lines[end.Line].Length);
        if (Compare(lineStart, start) != 0 || Compare(lineEnd, end) != 0) return (lineStart, lineEnd);
        return (new TextPosition(0, 0), new TextPosition(lines.Count - 1, lines[^1].Length));
    }

    private static int Compare(TextPosition a, TextPosition b) => a.Line != b.Line ? a.Line.CompareTo(b.Line) : a.Column.CompareTo(b.Column);

    private static (int Start, int End)? Word(string line, int column)
    {
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        int s = Math.Clamp(column, 0, line.Length), e = s;
        while (s > 0 && IsWord(line[s - 1])) s--;
        while (e < line.Length && IsWord(line[e])) e++;
        return e > s ? (s, e) : null;
    }

    private static TextPosition? Previous(IReadOnlyList<string> lines, TextPosition p)
    {
        if (p.Column > 0) return p with { Column = Math.Min(p.Column, lines[p.Line].Length + 1) - 1 };
        for (int l = p.Line - 1; l >= 0; l--)
            if (lines[l].Length > 0) return new TextPosition(l, lines[l].Length - 1);
        return null;
    }

    /// <summary>The nearest opening bracket at or before <paramref name="from"/> whose partner is not before it.</summary>
    private static TextPosition? UnmatchedOpenBefore(IReadOnlyList<string> lines, BracketMatcher.LineTokens tokens, TextPosition from)
    {
        Span<int> depth = stackalloc int[3];
        for (int l = from.Line; l >= 0; l--)
        {
            string text = lines[l];
            for (int c = l == from.Line ? Math.Min(from.Column, text.Length - 1) : text.Length - 1; c >= 0; c--)
            {
                int kind = "([{)]}".IndexOf(text[c]);
                if (kind < 0 || tokens.IsText(l, c)) continue;
                if (kind >= 3) depth[kind - 3]++;
                else if (depth[kind] > 0) depth[kind]--;
                else return new TextPosition(l, c);
            }
        }
        return null;
    }
}

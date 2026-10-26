using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Text changes a save can ask for, and the indentation of a file.</summary>
public static class SaveAssists
{
    /// <summary>Removes spaces and tabs at the end of every line. Returns true when something changed.</summary>
    public static bool TrimTrailingWhitespace(TextDocument d)
    {
        bool changed = false;
        using (d.RunUpdate())
        {
            for (int n = d.LineCount; n >= 1; n--)
            {
                var line = d.GetLineByNumber(n);
                int end = line.EndOffset, start = end;
                while (start > line.Offset && d.GetCharAt(start - 1) is ' ' or '\t') start--;
                if (start == end) continue;
                d.Remove(start, end - start);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Adds a line break at the end of a non-empty text that has none. Returns true when it did.</summary>
    public static bool EnsureFinalNewline(TextDocument d, string newLine)
    {
        if (d.TextLength == 0 || d.GetCharAt(d.TextLength - 1) is '\n' or '\r') return false;
        d.Insert(d.TextLength, newLine);
        return true;
    }
}

/// <summary>Guesses how a file is indented from its first lines.</summary>
public static class IndentDetector
{
    private const int MaxLines = 600;

    /// <summary>
    /// Tabs when more lines start with a tab than with spaces; otherwise the most common step between the indentation of neighbouring
    /// lines (2, 3, 4 or 8). Null when no line is indented, so the configured indentation stays.
    /// </summary>
    public static IndentSettings? Detect(string text, IndentSettings current)
    {
        int tabs = 0, spaces = 0, previous = 0, seen = 0;
        var steps = new Dictionary<int, int>();
        foreach (var raw in text.Split('\n'))
        {
            if (++seen > MaxLines) break;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;
            if (line[0] == '\t') { tabs++; continue; }
            int lead = 0;
            while (lead < line.Length && line[lead] == ' ') lead++;
            if (lead >= 2)
            {
                spaces++;
                int step = Math.Abs(lead - previous);
                if (step is 2 or 3 or 4 or 8) steps[step] = steps.GetValueOrDefault(step) + 1;
            }
            previous = lead;
        }
        if (tabs == 0 && spaces == 0) return null;
        if (tabs > spaces) return new IndentSettings(false, current.IndentSize, current.TabSize);
        if (steps.Count == 0) return null;
        int size = steps.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;
        return new IndentSettings(true, size, size);
    }
}

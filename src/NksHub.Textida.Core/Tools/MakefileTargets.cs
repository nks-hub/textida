namespace NksHub.Textida.Core.Tools;

/// <summary>A target of a Makefile and the 1-based line of the rule that names it.</summary>
public readonly record struct MakeTarget(string Name, int Line);

/// <summary>
/// Reads the explicit targets of a Makefile without running make: the names left of the colon of a rule. Recipe lines, variable assignments,
/// directives, <c>define</c> blocks, special targets (a leading dot), pattern rules and names that hold a variable or a wildcard are not targets.
/// A line continued with a backslash belongs to the line before it.
/// </summary>
public static class MakefileTargets
{
    private static readonly HashSet<string> Directives = new(StringComparer.Ordinal)
    {
        "include", "-include", "sinclude", "export", "unexport", "override", "vpath", "ifeq", "ifneq", "ifdef", "ifndef", "else", "endif", "define", "endef", "undefine", "private",
    };

    /// <summary>The lines of make output that point to a file: <c>file:line:column:</c> of a compiler and <c>Makefile:12:</c> of make itself. Use it as a regular expression with the groups file, line and column.</summary>
    public const string ErrorPattern = @"(?<file>(?:[A-Za-z]:[\\/])?[^\s:""'<>|*?\[\]()]+):(?<line>\d+)(?::(?<column>\d+))?:";

    /// <summary>The make program a target runs with; the file name of the Makefile is given to it as <c>-f</c>.</summary>
    public const string DefaultProgram = "make";

    public static bool IsMakefileName(string fileName) =>
        fileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase) || fileName.Equals("GNUmakefile", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".mk", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".mak", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<MakeTarget> Parse(string text)
    {
        var result = new List<MakeTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int defineDepth = 0;
        bool continued = false;
        int number = 0;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            number++;
            bool wasContinued = continued;
            continued = raw.EndsWith('\\');
            if (wasContinued) continue;
            string line = raw.TrimEnd();
            string head = line.TrimStart();
            string word = head.Split([' ', '\t'], 2)[0];
            if (defineDepth > 0)
            {
                if (word == "endef") defineDepth--;
                else if (word == "define") defineDepth++;
                continue;
            }
            if (word == "define") { defineDepth++; continue; }
            if (line.Length == 0 || line[0] == '\t' || head[0] == '#' || Directives.Contains(word)) continue;

            int colon = FindColon(head);
            if (colon <= 0) continue;
            if (colon + 1 < head.Length && head[colon + 1] == '=' || head.AsSpan(colon).StartsWith("::=") || head.AsSpan(colon).StartsWith(":::=")) continue;
            string left = head[..colon];
            if (left.Contains('=')) continue;
            foreach (var name in left.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (name[0] == '.' || name.IndexOfAny(['%', '$', '(', ')', '*', '?', '[']) >= 0) continue;
                if (seen.Add(name)) result.Add(new MakeTarget(name, number));
            }
        }
        return result;
    }

    /// <summary>The first colon that is not inside <c>$(...)</c> or <c>${...}</c>, or -1.</summary>
    private static int FindColon(string line)
    {
        int depth = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '$' && i + 1 < line.Length && line[i + 1] is '(' or '{') { depth++; i++; }
            else if (depth > 0 && c is ')' or '}') depth--;
            else if (c == ':' && depth == 0) return i;
        }
        return -1;
    }
}

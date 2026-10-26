namespace NksHub.Textida.Core.Files;

/// <summary>Two file name endings that belong together, like <c>.c</c> and <c>.h</c>; the rule works in both directions.</summary>
public readonly record struct RelatedRule(string A, string B);

/// <summary>Finds the file that belongs to another one: header and source, a file and its test, a view and its code-behind.</summary>
public static class RelatedFiles
{
    public static IReadOnlyList<RelatedRule> Builtin { get; } =
    [
        new(".c", ".h"), new(".c", ".hpp"), new(".cpp", ".hpp"), new(".cpp", ".h"), new(".cc", ".hh"), new(".cc", ".h"), new(".cxx", ".hxx"), new(".m", ".h"), new(".mm", ".h"),
        new(".go", "_test.go"), new(".py", "_test.py"), new(".php", "Test.php"), new(".cs", "Tests.cs"),
        new(".ts", ".spec.ts"), new(".ts", ".test.ts"), new(".tsx", ".test.tsx"), new(".js", ".spec.js"), new(".js", ".test.js"),
        new(".xaml", ".xaml.cs"), new(".axaml", ".axaml.cs"), new(".razor", ".razor.cs"),
    ];

    /// <summary>
    /// Reads user rules written as <c>endingA|endingB</c> pairs separated by semicolons or line breaks, for example <c>.component.ts|.component.html;.vert|.frag</c>.
    /// A pair without both sides is ignored.
    /// </summary>
    public static IReadOnlyList<RelatedRule> ParseRules(string? text)
    {
        var rules = new List<RelatedRule>();
        foreach (var part in (text ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var sides = part.Split('|', StringSplitOptions.TrimEntries);
            if (sides.Length == 2 && sides[0].Length > 0 && sides[1].Length > 0 && sides[0] != sides[1]) rules.Add(new RelatedRule(sides[0], sides[1]));
        }
        return rules;
    }

    /// <summary>Existing files in the same folder that the rules pair with <paramref name="path"/>; user rules come first. The file itself is never listed.</summary>
    public static IReadOnlyList<string> Find(string path, IReadOnlyList<RelatedRule> userRules, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        string directory = Path.GetDirectoryName(path) ?? "";
        string name = Path.GetFileName(path);
        var found = new List<string>();
        foreach (var rule in userRules.Concat(Builtin))
        {
            foreach (var (from, to) in new[] { (rule.A, rule.B), (rule.B, rule.A) })
            {
                if (name.Length <= from.Length || !name.EndsWith(from, StringComparison.OrdinalIgnoreCase)) continue;
                string candidate = Path.Combine(directory, name[..^from.Length] + to);
                if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase) && exists(candidate) && !found.Contains(candidate, StringComparer.OrdinalIgnoreCase)) found.Add(candidate);
            }
        }
        return found;
    }
}

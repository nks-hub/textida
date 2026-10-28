using System.Text.RegularExpressions;

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

    /// <summary>The name under which the digest of a Makefile is tied to the approval of a run (see <see cref="ToolRunner.WithProjectValues"/>).</summary>
    public const string ApprovalKey = "makefile.sha256";

    /// <summary>
    /// SHA-256 (hex) of the bytes of the file on disk, which is what make reads. A yes to a run is tied to it, so a Makefile that changed since asks again.
    /// Files it includes are not covered. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the file cannot be read.
    /// </summary>
    public static string ContentDigest(string makefile)
    {
        using var stream = new FileStream(makefile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    /// <summary>The names make itself looks for, in the order it prefers them.</summary>
    private static readonly string[] DefaultNames = ["GNUmakefile", "makefile", "Makefile"];

    /// <summary>
    /// The Makefile nearest to <paramref name="fromDirectory"/>: the folder itself, then each folder above it, up to <paramref name="maxLevels"/> folders, and never past the root
    /// of the project (see <see cref="ToolVariables.IsProjectRoot"/>), so a Makefile outside the project is not taken. Returns its full path, or null.
    /// </summary>
    public static string? FindNearest(string? fromDirectory, int maxLevels)
    {
        try
        {
            var dir = fromDirectory is null ? null : new DirectoryInfo(Path.GetFullPath(fromDirectory));
            for (int level = 0; dir is not null && level < maxLevels; level++, dir = dir.Parent)
            {
                var names = dir.EnumerateFiles().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
                if (DefaultNames.FirstOrDefault(names.Contains) is { } found) return Path.Combine(dir.FullName, found);
                if (ToolVariables.IsProjectRoot(dir.FullName)) break;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { /* nothing readable */ }
        return null;
    }

    private static readonly Regex DirectoryNotice = new(@"^\S*make\S*?(?:\[\d+\])?: (?<kind>Entering|Leaving) directory \S(?<dir>.+?)\S\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// The folder a line of make output was written in, given the lines before it. A recursive make says <c>make[1]: Entering directory '...'</c> and <c>Leaving directory</c>, and a file name in an error is
    /// relative to the folder entered last; without such a notice it is <paramref name="fallback"/>. A path of the form <c>/c/dir</c> (MSYS) is read as <c>C:\dir</c> on Windows.
    /// </summary>
    public static string DirectoryAt(IEnumerable<string> linesBefore, string fallback)
    {
        var entered = new Stack<string>();
        foreach (var line in linesBefore)
        {
            if (!line.Contains("directory", StringComparison.Ordinal)) continue;
            Match m;
            try { m = DirectoryNotice.Match(line); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success) continue;
            if (m.Groups["kind"].Value == "Entering") entered.Push(m.Groups["dir"].Value);
            else if (entered.Count > 0) entered.Pop();
        }
        if (entered.Count == 0) return fallback;
        string dir = entered.Peek();
        return OperatingSystem.IsWindows() && Regex.Match(dir, "^/([A-Za-z])(?:/|$)") is { Success: true } drive ? $"{char.ToUpperInvariant(drive.Groups[1].Value[0])}:{dir[2..].Replace('/', '\\')}\\" : dir;
    }

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

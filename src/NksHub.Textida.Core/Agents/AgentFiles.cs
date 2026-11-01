using System.Text;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Agents;

/// <summary>The kind of a file an agent reads: the instruction files, the rules and the skills.</summary>
public enum AgentFileKind { Instructions, Rule, Skill }

/// <summary>
/// A file an agent loads. <paramref name="Applies"/> is false for a rule that is limited to paths (<c>paths:</c> or <c>globs:</c> in its header) that the active file is not in; such a file is listed, dimmed, so the user sees why it is not loaded.
/// <paramref name="Tokens"/> is an estimate by <see cref="TokenEstimator"/>, never an exact count.
/// </summary>
public sealed record AgentFile(string Path, string Display, AgentFileKind Kind, long Bytes, long Tokens, bool Applies, string? Limit);

/// <summary>
/// What an agent loads for a file in a folder. Instruction files (<c>AGENTS.md</c>, <c>CLAUDE.md</c>, <c>CLAUDE.local.md</c>, <c>GEMINI.md</c>) are taken from every folder between the root and the folder of the file, the farthest first,
/// because agents read them from the working folder up. The rules are the <c>.claude/rules</c> and <c>.cursor/rules</c> files and <c>.github/copilot-instructions.md</c> of the root, the skills are the <c>.claude/skills/*/SKILL.md</c> files of the root
/// and of the folders in between. Which agent reads which file is a convention of each product, not a standard: the list is what a file of these names would pull in, not a promise about one product.
/// Nothing outside the root is read except <paramref name="home"/>'s <c>.claude/CLAUDE.md</c>, which every Claude session loads.
/// </summary>
public static class AgentFiles
{
    public const int MaxFiles = 200;
    public const long MaxHeaderBytes = 4096;

    private static readonly string[] InstructionNames = ["AGENTS.md", "CLAUDE.md", "CLAUDE.local.md", "GEMINI.md"];

    public static IReadOnlyList<AgentFile> Find(string root, string? activeFile, string? home, TokenPreset preset)
    {
        var result = new List<AgentFile>();
        string rootFull = System.IO.Path.GetFullPath(root);
        string relative = activeFile is null ? "" : System.IO.Path.GetRelativePath(rootFull, System.IO.Path.GetFullPath(activeFile)).Replace('\\', '/');
        bool inside = activeFile is not null && !relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative);

        if (home is not null) Add(result, System.IO.Path.Combine(home, ".claude", "CLAUDE.md"), "~/.claude/CLAUDE.md", AgentFileKind.Instructions, true, null, preset);

        var folders = new List<string> { rootFull };
        if (inside && System.IO.Path.GetDirectoryName(relative.Replace('/', System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } below)
        {
            string current = rootFull;
            foreach (var part in below.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = System.IO.Path.Combine(current, part);
                folders.Add(current);
            }
        }
        foreach (var folder in folders)
            foreach (var name in InstructionNames)
                Add(result, System.IO.Path.Combine(folder, name), Display(rootFull, System.IO.Path.Combine(folder, name)), AgentFileKind.Instructions, true, null, preset);

        Add(result, System.IO.Path.Combine(rootFull, ".github", "copilot-instructions.md"), ".github/copilot-instructions.md", AgentFileKind.Rule, true, null, preset);
        foreach (var rules in new[] { ".claude/rules", ".cursor/rules" })
            foreach (var file in Files(System.IO.Path.Combine(rootFull, rules), "*.md", "*.mdc"))
            {
                var limit = Limits(file);
                bool applies = limit.Count == 0 || (inside && limit.Any(g => Glob.IsMatch(g, relative)));
                Add(result, file, Display(rootFull, file), AgentFileKind.Rule, applies, limit.Count == 0 ? null : string.Join(", ", limit), preset);
            }
        foreach (var folder in folders)
            foreach (var skills in Directories(System.IO.Path.Combine(folder, ".claude", "skills")))
                Add(result, System.IO.Path.Combine(skills, "SKILL.md"), Display(rootFull, System.IO.Path.Combine(skills, "SKILL.md")), AgentFileKind.Skill, true, null, preset);
        return result.Count > MaxFiles ? result[..MaxFiles] : result;
    }

    /// <summary>The plans agents leave in a folder, newest first: <c>.claude/plans</c>, <c>.cursor/plans</c>, <c>.agents/plans</c>, <c>docs/plans</c>, <c>PLAN.md</c> and <c>PLANS.md</c> of the root, and <c>.claude/plans</c> of <paramref name="home"/>.</summary>
    public static IReadOnlyList<string> FindPlans(string? root, string? home)
    {
        var found = new List<string>();
        if (root is not null)
        {
            foreach (var folder in new[] { ".claude/plans", ".cursor/plans", ".agents/plans", "docs/plans" })
                found.AddRange(Files(System.IO.Path.Combine(root, folder.Replace('/', System.IO.Path.DirectorySeparatorChar)), "*.md", "*.mdc"));
            foreach (var name in new[] { "PLAN.md", "PLANS.md" })
                if (File.Exists(System.IO.Path.Combine(root, name))) found.Add(System.IO.Path.Combine(root, name));
        }
        if (home is not null) found.AddRange(Files(System.IO.Path.Combine(home, ".claude", "plans"), "*.md"));
        return [.. found.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(SafeTime).Take(500)];
    }

    private static DateTime SafeTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (IOException) { return DateTime.MinValue; }
    }

    private static string Display(string root, string path) => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> Files(string folder, params string[] patterns)
    {
        if (!Directory.Exists(folder)) return [];
        try { return patterns.SelectMany(p => Directory.EnumerateFiles(folder, p, SearchOption.AllDirectories)).Order(StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> Directories(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        try { return Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private static void Add(List<AgentFile> list, string path, string display, AgentFileKind kind, bool applies, string? limit, TokenPreset preset)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            // the estimate counts characters, which are not the bytes; the file is read for its length only up to a bound that agents themselves do not exceed
            long characters = info.Length <= 4 << 20 ? File.ReadAllText(path).Length : info.Length;
            list.Add(new AgentFile(path, display, kind, info.Length, TokenEstimator.Estimate(characters, preset), applies, limit));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a file that cannot be read is not loaded either */ }
    }

    /// <summary>The path patterns in the header of a rule: <c>paths:</c> (Claude) or <c>globs:</c> (Cursor), as a list or one line. A rule with <c>alwaysApply: true</c> has none.</summary>
    internal static IReadOnlyList<string> Limits(string file)
    {
        string head;
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[MaxHeaderBytes];
            head = Encoding.UTF8.GetString(buffer, 0, stream.Read(buffer));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
        var lines = head.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---") return [];
        var patterns = new List<string>();
        bool always = false, inList = false;
        for (int i = 1; i < lines.Length && lines[i].Trim() != "---"; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (inList && trimmed.StartsWith("- ", StringComparison.Ordinal)) { patterns.Add(Unquote(trimmed[2..])); continue; }
            inList = false;
            int colon = trimmed.IndexOf(':');
            if (colon < 0) continue;
            string key = trimmed[..colon].Trim(), value = trimmed[(colon + 1)..].Trim();
            if (key == "alwaysApply") always = value == "true";
            else if (key is "paths" or "globs")
            {
                if (value.Length == 0) inList = true;
                else patterns.AddRange(value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote));
            }
        }
        return always ? [] : [.. patterns.Where(p => p.Length > 0)];
    }

    private static string Unquote(string text) => text.Trim().Trim('"', '\'');
}

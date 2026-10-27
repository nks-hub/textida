using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

public enum StackTraceKind { DotNet, Java, Python, Php, Node }

/// <summary>A file and line named by a stack trace frame; <paramref name="Start"/> and <paramref name="Length"/> cover the <c>path:line</c> part of the text. Column is 0 when the frame has none.</summary>
public readonly record struct StackTraceLink(int Start, int Length, string Path, int Line, int Column, StackTraceKind Kind);

/// <summary>Finds the file and line of the frames of .NET, Java, Python, PHP and Node.js stack traces in one line of text.</summary>
public static partial class StackTraceLinks
{
    // at Ns.Type.Method(args) in C:\src\File.cs:line 42
    [GeneratedRegex(@"\bin (?<path>(?:[A-Za-z]:[\\/]|[\\/]|\.{1,2}[\\/])?[^\r\n:]+?):line (?<line>\d+)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex DotNet();

    // at com.example.Type.method(Type.java:42)
    [GeneratedRegex(@"\((?<path>[\w$.\-]+\.(?:java|kt|kts|scala|groovy)):(?<line>\d+)\)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Java();

    //   File "/app/main.py", line 12, in run
    [GeneratedRegex(@"\bFile ""(?<path>[^""\r\n]+)"", line (?<line>\d+)", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Python();

    // #0 /var/www/a.php(12): run()   and   in /var/www/a.php on line 12   and   in /var/www/a.php:12
    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:[\\/]|[\\/]|\.{1,2}[\\/])?[^\s()""'<>|*?:]+\.(?:php\d?|phtml|inc))(?:\((?<line>\d+)\)| on line (?<line>\d+)|:(?<line>\d+))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, 200)]
    private static partial Regex Php();

    // at fn (/app/a.js:12:34)   at /app/a.js:12:34   at file:///C:/app/a.mjs:12:34
    [GeneratedRegex(@"(?<path>(?:file:///)?(?:[A-Za-z]:[\\/]|[\\/]|\.{1,2}[\\/])?[^\s():""'<>|*?]+\.(?:[cm]?[jt]sx?)):(?<line>\d+)(?::(?<col>\d+))?", RegexOptions.CultureInvariant, 200)]
    private static partial Regex Node();

    /// <summary>All frames named in the line, left to right; a place that two patterns both match is reported once, for the first.</summary>
    public static IReadOnlyList<StackTraceLink> Find(string line)
    {
        if (line.Length < 4 || line.Length > 8192) return [];
        var found = new List<StackTraceLink>();
        try
        {
            Add(found, DotNet(), line, StackTraceKind.DotNet);
            Add(found, Java(), line, StackTraceKind.Java);
            Add(found, Python(), line, StackTraceKind.Python);
            Add(found, Php(), line, StackTraceKind.Php);
            Add(found, Node(), line, StackTraceKind.Node);
        }
        catch (RegexMatchTimeoutException) { /* a line the patterns cannot handle names no frame */ }
        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    /// <summary>The frame whose text holds the character at <paramref name="index"/>, or null.</summary>
    public static StackTraceLink? At(string line, int index)
    {
        foreach (var link in Find(line))
            if (index >= link.Start && index <= link.Start + link.Length) return link;
        return null;
    }

    private static void Add(List<StackTraceLink> found, Regex regex, string line, StackTraceKind kind)
    {
        for (var match = regex.Match(line); match.Success; match = match.NextMatch())
        {
            var path = match.Groups["path"];
            if (!int.TryParse(match.Groups["line"].ValueSpan, out int number) || number < 1) continue;
            int column = match.Groups["col"].Success && int.TryParse(match.Groups["col"].ValueSpan, out int c) ? c : 0;
            int start = path.Index, end = match.Index + match.Length;
            if (found.Any(f => start < f.Start + f.Length && f.Start < end)) continue;
            found.Add(new StackTraceLink(start, end - start, Clean(path.Value), number, column, kind));
        }
    }

    /// <summary>A <c>file:///</c> URL as a path: the drive form keeps its drive, the other one is rooted again.</summary>
    private static string Clean(string path)
    {
        if (!path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) return path;
        string rest = Uri.UnescapeDataString(path[8..]);
        return rest.Length > 1 && rest[1] == ':' ? rest : "/" + rest;
    }

    /// <summary>
    /// The existing file a link means. An absolute path is taken as it is; a relative one is tried under each folder of <paramref name="folders"/> in order
    /// (the folder of the log first, then the workspace roots). A bare file name (Java names only the file) is looked for below the folders, visiting at most
    /// <paramref name="maxVisited"/> entries so a huge tree cannot stall the click.
    /// </summary>
    public static string? Resolve(StackTraceLink link, IEnumerable<string> folders, int maxVisited = 20000)
    {
        string path = link.Path;
        if (Path.IsPathRooted(path)) return File.Exists(path) ? path : null;
        var bases = folders.Where(Directory.Exists).ToList();
        foreach (var folder in bases)
        {
            string candidate = Path.GetFullPath(Path.Combine(folder, path));
            if (File.Exists(candidate)) return candidate;
        }
        if (path.AsSpan().IndexOfAny('/', '\\') >= 0) return null;
        int visited = 0;
        var queue = new Queue<string>(bases);
        while (queue.Count > 0)
        {
            string folder = queue.Dequeue(), candidate = Path.Combine(folder, path);
            if (File.Exists(candidate)) return candidate;
            try
            {
                foreach (string sub in Directory.EnumerateDirectories(folder))
                {
                    if (++visited > maxVisited) return null;
                    if (!Path.GetFileName(sub).StartsWith('.')) queue.Enqueue(sub);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an unreadable folder has nothing to offer */ }
        }
        return null;
    }
}

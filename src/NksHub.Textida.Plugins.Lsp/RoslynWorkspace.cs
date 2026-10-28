using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>
/// The Roslyn language server does not look for a solution by itself: until it is told to open one (or projects) every file it is shown is a project of its own, and a rename or a search for references reaches that file only.
/// The client sends <c>solution/open</c> or <c>project/open</c> after <c>initialized</c> and the server answers with <c>workspace/projectInitializationComplete</c> when everything is loaded (checked with 5.13).
/// </summary>
public static class RoslynWorkspace
{
    public const string LoadedNotification = "workspace/projectInitializationComplete";

    private static readonly EnumerationOptions Simple = new() { MatchType = MatchType.Simple, IgnoreInaccessible = true };

    /// <summary>The program is the Roslyn server: the executable of the C# extension or the .NET tool.</summary>
    public static bool IsRoslyn(string program)
    {
        string name = Path.GetFileName(program); // the dots of the executable's own name are not an extension
        foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name[..^extension.Length];
        return name.Equals("Microsoft.CodeAnalysis.LanguageServer", StringComparison.OrdinalIgnoreCase) || name.Equals("roslyn-language-server", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The notification that opens the workspace of <paramref name="root"/>: its solution (<c>.sln</c> or <c>.slnx</c>, the first by name) or, without one, its projects. Null when the folder has neither.
    /// </summary>
    public static (string Method, string Params)? Open(string root)
    {
        try
        {
            var solution = Directory.EnumerateFiles(root, "*.sln", Simple).Concat(Directory.EnumerateFiles(root, "*.slnx", Simple)).Order(StringComparer.Ordinal).FirstOrDefault();
            if (solution is not null) return ("solution/open", $"{{\"solution\":\"{LspServer.Encode(PathUri.FromPath(solution))}\"}}");
            var projects = Directory.EnumerateFiles(root, "*.csproj", Simple).Order(StringComparer.Ordinal).ToList();
            return projects.Count == 0 ? null : ("project/open", $"{{\"projects\":[{string.Join(',', projects.Select(p => "\"" + LspServer.Encode(PathUri.FromPath(p)) + "\""))}]}}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}

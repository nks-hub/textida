using System.Text.Json;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>How a language server is started: the program, its arguments one by one (no shell) and extra environment variables.</summary>
public sealed record ServerLaunch(string Language, string Program, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// The language servers the plugin can start, by language id of the editor. Several entries for one language are fallbacks: the first whose program is found is used.
/// </summary>
public sealed class ServerCatalog(IReadOnlyList<ServerLaunch> entries)
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    /// <summary>
    /// The servers that come with the editor. OPEN: the version of the Roslyn server to pin is not decided, so the entry names the program and leaves the version to the
    /// installation; the launch line (<c>--stdio</c>, <c>--telemetryLevel off</c>) is data here and has not been run against a real Roslyn or csharp-ls server.
    /// </summary>
    public static ServerCatalog BuiltIn { get; } = new(
    [
        new("csharp", "Microsoft.CodeAnalysis.LanguageServer", ["--stdio", "--telemetryLevel", "off"], NoEnvironment),
        new("csharp", "csharp-ls", [], NoEnvironment),
    ]);

    /// <summary>Reads <c>[{"language":"x","program":"p","arguments":["a"],"environment":{"K":"V"}}]</c>.</summary>
    public static ServerCatalog FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ServerLaunch>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var arguments = item.TryGetProperty("arguments", out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
            var environment = new Dictionary<string, string>();
            if (item.TryGetProperty("environment", out var e))
                foreach (var pair in e.EnumerateObject()) environment[pair.Name] = pair.Value.GetString()!;
            list.Add(new ServerLaunch(item.GetProperty("language").GetString()!, item.GetProperty("program").GetString()!, arguments, environment));
        }
        return new ServerCatalog(list);
    }

    /// <summary>The first entry of the language whose program exists; when none does, the first entry (so the notice can name the program that was looked for), or null for an unknown language.</summary>
    public ServerLaunch? Resolve(string language, out bool found)
    {
        var candidates = entries.Where(x => x.Language == language).ToList();
        foreach (var candidate in candidates)
            if (Find(candidate.Program) is { } path)
            {
                found = true;
                return candidate with { Program = path };
            }
        found = false;
        return candidates.FirstOrDefault();
    }

    /// <summary>The full path of a program: the name itself when it is a path to a file, otherwise the first match in <c>PATH</c> (with the extensions of <c>PATHEXT</c> on Windows).</summary>
    public static string? Find(string program)
    {
        if (program.Contains(Path.DirectorySeparatorChar) || program.Contains(Path.AltDirectorySeparatorChar)) return File.Exists(program) ? Path.GetFullPath(program) : null;
        var extensions = OperatingSystem.IsWindows()
            ? (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("").ToArray()
            : [""];
        foreach (var folder in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, program + extension);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

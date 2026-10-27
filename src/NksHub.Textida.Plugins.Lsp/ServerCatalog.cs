using System.Text.Json;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>How a language server is started: the program, its arguments one by one (no shell) extra environment variables and the raw JSON object sent as <c>initializationOptions</c>.</summary>
public sealed record ServerLaunch(string Language, string Program, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment, string? InitializationOptions = null);

/// <summary>
/// The language servers the plugin can start, by language id of the editor. Several entries for one language are fallbacks: the first whose program is found is used.
/// </summary>
public sealed class ServerCatalog(IReadOnlyList<ServerLaunch> entries)
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    /// <summary>
    /// The servers that come with the editor, from <c>LanguageCatalog</c> (the entries marked as shipped). OPEN: the version of the Roslyn server to pin is not decided, so the entry names the program and leaves the version to the
    /// installation; the launch line (<c>--stdio</c>, <c>--telemetryLevel off</c>) is data here and has not been run against a real Roslyn or csharp-ls server.
    /// </summary>
    public static ServerCatalog BuiltIn { get; } = new([.. LanguageCatalog.Lsp.Where(x => x.Shipped).Select(x => new ServerLaunch(x.Language, x.Program, x.Arguments, NoEnvironment))]);

    /// <summary>Reads <c>[{"language":"x","program":"p","arguments":["a"],"environment":{"K":"V"},"initializationOptions":{}}]</c>.</summary>
    public static ServerCatalog FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new ServerCatalog([.. doc.RootElement.EnumerateArray().Select(ParseLaunch)]);
    }

    /// <summary>Reads one entry of the shape <see cref="FromJson"/> takes; the editor sends it with a command for a server a plugin manifest declares.</summary>
    public static ServerLaunch ParseLaunch(JsonElement item)
    {
        var arguments = item.TryGetProperty("arguments", out var a) ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var environment = new Dictionary<string, string>();
        if (item.TryGetProperty("environment", out var e))
            foreach (var pair in e.EnumerateObject()) environment[pair.Name] = pair.Value.GetString()!;
        var options = item.TryGetProperty("initializationOptions", out var o) && o.ValueKind == JsonValueKind.Object ? o.GetRawText() : null;
        return new ServerLaunch(item.GetProperty("language").GetString()!, item.GetProperty("program").GetString()!, arguments, environment, options);
    }

    /// <summary>The launch with its program located (see <see cref="Find"/>); <paramref name="found"/> is false when the program does not exist, and the launch still names it.</summary>
    public static ServerLaunch Locate(ServerLaunch launch, out bool found)
    {
        var path = Find(launch.Program);
        found = path is not null;
        return path is null ? launch : launch with { Program = path };
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
    public static string? Find(string program) => ToolLocator.Find(program);
}

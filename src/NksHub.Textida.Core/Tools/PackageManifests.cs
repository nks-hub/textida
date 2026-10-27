using System.Text.Json;

namespace NksHub.Textida.Core.Tools;

/// <summary>A package registry the editor may ask about the dependencies of a manifest. <paramref name="Hosts"/> names every host a lookup contacts, for the consent dialog.</summary>
public sealed record PackageRegistry(string Id, string Name, string Hosts);

/// <summary>A script of a manifest. <paramref name="Body"/> is the script as written; the runner and its arguments are what starts it.</summary>
public sealed record PackageScript(string Name, string Body, string Executable, IReadOnlyList<string> Args);

/// <summary>Which manifest files have a registry and a script runner. The registry lookups themselves live in the JSON plugin; the app uses this table for consent and scripts.</summary>
public static class PackageManifests
{
    public static readonly IReadOnlyList<PackageRegistry> Registries =
    [
        new("npm", "npm", "registry.npmjs.org"),
        new("packagist", "Packagist", "packagist.org, repo.packagist.org"),
        new("pypi", "PyPI", "pypi.org"),
        new("nuget", "NuGet", "azuresearch-usnc.nuget.org, api.nuget.org"),
        new("go", "Go proxy", "proxy.golang.org"),
        new("crates", "crates.io", "crates.io"),
        new("rubygems", "RubyGems", "rubygems.org"),
        new("pub", "pub.dev", "pub.dev"),
    ];

    /// <summary>The registry whose manifest <paramref name="path"/> is, by file name; null for any other file.</summary>
    public static PackageRegistry? For(string path) => Registries.FirstOrDefault(r => r.Id == IdOf(Path.GetFileName(path)));

    private static string? IdOf(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.StartsWith("requirements", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal)) return "pypi";
        if (name.EndsWith(".csproj", StringComparison.Ordinal)) return "nuget";
        return name switch { "package.json" => "npm", "composer.json" => "packagist", "go.mod" => "go", "cargo.toml" => "crates", "gemfile" => "rubygems", "pubspec.yaml" => "pub", _ => null };
    }

    /// <summary>
    /// On Windows <c>npm</c> and <c>composer</c> are batch files. The runner can start those through cmd.exe, but this is kept: it avoids cmd.exe, and a script name with a line break or <c>!</c> (refused for a batch file) still runs. This finds the program the batch file starts: Node with the <c>npm-cli.js</c> that sits next to
    /// <c>npm.cmd</c>, or PHP with the <c>composer.phar</c> next to <c>composer.bat</c>. Null when the layout is not the standard one; the runner then starts the batch file itself.
    /// </summary>
    /// <returns>The program and the arguments that go before the script arguments.</returns>
    public static (string Executable, IReadOnlyList<string> Prefix)? WindowsTarget(string runner, string pathVariable)
    {
        var dirs = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim('"')).ToList();
        string? OnPath(string file) => dirs.Select(d => { try { return Path.Combine(d, file); } catch (ArgumentException) { return ""; } }).FirstOrDefault(f => f.Length > 0 && File.Exists(f));
        if (runner == "npm" && OnPath("npm.cmd") is { } cmd && Path.GetDirectoryName(cmd) is { } npmDir)
        {
            var cli = Path.Combine(npmDir, "node_modules", "npm", "bin", "npm-cli.js");
            var node = Path.Combine(npmDir, "node.exe");
            return File.Exists(cli) && File.Exists(node) ? (node, [cli]) : null;
        }
        if (runner == "composer" && OnPath("composer.bat") is { } bat && Path.GetDirectoryName(bat) is { } composerDir)
        {
            var phar = Path.Combine(composerDir, "composer.phar");
            return File.Exists(phar) && OnPath("php.exe") is { } php ? (php, [phar]) : null;
        }
        return null;
    }

    /// <summary>The scripts of a <c>package.json</c> (run by <c>npm run</c>) or <c>composer.json</c> (<c>composer run-script</c>); empty for any other file or text that is not valid JSON.</summary>
    public static IReadOnlyList<PackageScript> Scripts(string path, string text)
    {
        var (executable, verb) = Path.GetFileName(path).ToLowerInvariant() switch { "package.json" => ("npm", "run"), "composer.json" => ("composer", "run-script"), _ => ("", "") };
        if (executable.Length == 0) return [];
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object) return [];
            return [.. scripts.EnumerateObject().Where(s => s.Name.Length > 0 && s.Name[0] != '-').Select(s => new PackageScript(s.Name, s.Value.GetRawText(), executable, [verb, s.Name]))];
        }
        catch (JsonException) { return []; }
    }
}

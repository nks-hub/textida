using System.Text.Json;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manager;

namespace NksHub.Textida.App.Services;

public enum PublisherTrust { Trusted, New, Changed }

/// <summary>
/// The publisher keys the user confirmed (PZ.8.1), one file in the profile: publisher id and the key. A publisher is trusted only while its key is the one that was confirmed; a different key under the same id is
/// <see cref="PublisherTrust.Changed"/> and the catalog refuses its plugins until the user removes the old entry by hand, so a swapped key in the repository cannot sign a new version silently.
/// </summary>
internal sealed class CatalogTrust(string path)
{
    private Dictionary<string, string> Read()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 1 << 20) return map;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* an unreadable file trusts nobody */ }
        return map;
    }

    public PublisherTrust Of(CatalogPublisher publisher) =>
        !Read().TryGetValue(publisher.Id, out string? key) ? PublisherTrust.New : key == publisher.PublicKey ? PublisherTrust.Trusted : PublisherTrust.Changed;

    public void Trust(CatalogPublisher publisher)
    {
        var map = Read();
        map[publisher.Id] = publisher.PublicKey;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        foreach (var (id, key) in map.OrderBy(p => p.Key, StringComparer.Ordinal)) writer.WriteString(id, key);
        writer.WriteEndObject();
    }
}

/// <summary>
/// The Git repository that holds the catalog (PZ.8.1) and its copy in the profile. The copy is made by the <c>git</c> program found through the tool catalog, a shallow clone behind the proxy of the editor, into a new
/// folder that replaces the old copy only when the clone worked. Only an https address or an existing folder is a source, and the transports of git that run programs are switched off.
/// </summary>
internal static class PluginCatalogSource
{
    public static readonly CatalogTool Git = new("git");

    internal sealed record CatalogTool(string Executable);

    public static bool IsSource(string source) =>
        Directory.Exists(source) || Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>The folder of the copy of this source: one per source, named by its hash.</summary>
    public static string CacheFolder(string profile, string source) =>
        Path.Combine(profile, "plugin-catalog", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..16]);

    public static IReadOnlyList<string> CloneArguments(string source, string target) =>
        ["-c", "protocol.ext.allow=never", "-c", "protocol.allow=user", "-c", "core.symlinks=false", "clone", "--depth", "1", "--no-tags", "--quiet", "--", source, target];

    public static ToolInvocation Invocation(string program, IReadOnlyList<string> args, string workingDirectory, string source) =>
        new("git", program, args, workingDirectory, TimeSpan.FromMinutes(3), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("plugin-catalog\0" + program))),
            Environment: new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_CONFIG_NOSYSTEM"] = "1" });

    /// <summary>Clones the source into a new folder and, when that worked and holds an index, puts it in place of the old copy. Null when it worked, else why not.</summary>
    public static async Task<string?> RefreshAsync(string program, string profile, string source, Func<ToolInvocation, Task<bool>> approve)
    {
        string folder = CacheFolder(profile, source), fresh = folder + ".new";
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        DeleteFolder(fresh);
        var tool = Invocation(program, CloneArguments(source, fresh), Path.GetDirectoryName(folder)!, source);
        if (!await approve(tool)) return "cancelled";
        try
        {
            var run = await CodeGraphCallers.RunAsync(tool);
            if (run.TimedOut) return "git did not finish in time";
            if (run.ExitCode != 0) return FirstLine(run.Errors) ?? $"git exited with {run.ExitCode}";
        }
        catch (ToolException e) { return e.Message; }
        if (!File.Exists(Path.Combine(fresh, PluginCatalog.IndexFile))) { DeleteFolder(fresh); return $"the repository has no {PluginCatalog.IndexFile}"; }
        DeleteFolder(folder);
        Directory.Move(fresh, folder);
        return null;
    }

    private static string? FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    /// <summary>Removes a folder with read-only files (git writes some).</summary>
    public static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(folder, true);
    }

    /// <summary>The index of the copy, or null when there is none or it is too large to be an index.</summary>
    public static PluginCatalogIndex? ReadIndex(string folder)
    {
        string file = Path.Combine(folder, PluginCatalog.IndexFile);
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > PluginCatalog.MaxIndexBytes) return null;
            return PluginCatalog.Parse(File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The file of a package kept in the repository; null when the path leaves the folder (a link inside the copy included).</summary>
    public static string? LocalPackage(string folder, string package)
    {
        string root = Path.GetFullPath(folder), path = Path.GetFullPath(Path.Combine(root, package.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return new FileInfo(path).LinkTarget is null ? path : null;
    }
}

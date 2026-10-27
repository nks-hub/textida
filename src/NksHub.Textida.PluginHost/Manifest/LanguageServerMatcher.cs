using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NksHub.Textida.PluginHost.Manifest;

/// <summary>A language server a plugin declares, with the folder of the plugin (the base of a <c>plugin</c> location).</summary>
public sealed record DeclaredServer(string PluginId, string Directory, LanguageServerContribution Server)
{
    /// <summary>The program to start: the bare name for a lookup in <c>PATH</c>, or the full path inside the plugin folder.</summary>
    public string Program => Server.Locate == "plugin" ? Path.GetFullPath(Path.Combine(Directory, Server.Command)) : Server.Command;

    /// <summary>The language the server runs for: the language of the document when the server lists it, otherwise (a match by glob) the first language of the server.</summary>
    public string LanguageFor(string languageId) => Server.Languages.Contains(languageId, StringComparer.Ordinal) ? languageId : Server.Languages[0];

    /// <summary>The launch description the language server client takes: <c>{"language","program","arguments","initializationOptions"}</c>.</summary>
    public string ToLaunchJson(string languageId)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("language", LanguageFor(languageId));
            w.WriteString("program", Program);
            w.WriteStartArray("arguments");
            foreach (var a in Server.Args) w.WriteStringValue(a);
            w.WriteEndArray();
            if (Server.InitializationOptions is { } options)
            {
                w.WritePropertyName("initializationOptions");
                options.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>Chooses the declared server for a document by language mode id or file glob.</summary>
public static class LanguageServerMatcher
{
    public const int MaxGlobLength = 256;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsValidGlob(string glob) => glob is { Length: > 0 and <= MaxGlobLength } && !glob.Any(char.IsControl);

    /// <summary>
    /// Whether <paramref name="path"/> matches the glob: <c>*</c> is any run of characters except <c>/</c>, <c>?</c> one character except <c>/</c>, <c>**</c> any run including <c>/</c>. Case does not matter.
    /// A glob without <c>/</c> is matched against the file name only; a glob with <c>/</c> matches the end of the path at a folder boundary, and <c>**/</c> also matches no folder.
    /// </summary>
    public static bool MatchesGlob(string glob, string path)
    {
        var normalized = path.Replace('\\', '/');
        var subject = glob.Contains('/') ? normalized : normalized[(normalized.LastIndexOf('/') + 1)..];
        var pattern = new StringBuilder(glob.Contains('/') ? "(?:^|/)" : "^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*' && i + 2 < glob.Length && glob[i + 1] == '*' && glob[i + 2] == '/')
            {
                pattern.Append("(?:.*/)?");
                i += 2;
            }
            else if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                pattern.Append(".*");
                i++;
            }
            else if (c == '*') pattern.Append("[^/]*");
            else if (c == '?') pattern.Append("[^/]");
            else pattern.Append(Regex.Escape(c.ToString()));
        }
        try { return Regex.IsMatch(subject, pattern.Append('$').ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>The first server (in the order given) that lists <paramref name="languageId"/> or has a glob that matches <paramref name="path"/>; null when none does.</summary>
    public static DeclaredServer? Find(IEnumerable<DeclaredServer> servers, string languageId, string? path) =>
        servers.FirstOrDefault(s => s.Server.Languages.Contains(languageId, StringComparer.Ordinal) || (path is not null && s.Server.Globs.Any(g => MatchesGlob(g, path))));
}

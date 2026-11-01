using System.Text;

namespace NksHub.Textida.Core.Http;

/// <summary>
/// Two small text rules that build web addresses and touch no network: the address to search a text on the web from a template the user wrote (<c>{query}</c> is the text, percent-encoded), and the address of a local file
/// from the rules the user wrote (<c>folder=&gt;address</c>, several separated by semicolons or lines). Only http and https addresses come out.
/// </summary>
public static class WebLinks
{
    public const int MaxQueryChars = 1000;

    /// <summary>The address of a search for <paramref name="text"/>, or null when the template is empty, is not an http or https address, lacks <c>{query}</c>, or the text is empty or longer than <see cref="MaxQueryChars"/>.</summary>
    public static Uri? SearchUri(string? template, string text)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{query}", StringComparison.Ordinal) || text.Length == 0 || text.Length > MaxQueryChars) return null;
        return Uri.TryCreate(template.Trim().Replace("{query}", Uri.EscapeDataString(text), StringComparison.Ordinal), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    /// <summary>One rule: files below <see cref="Folder"/> are served from <see cref="Address"/> (which ends with a slash).</summary>
    public sealed record PathRule(string Folder, string Address);

    /// <summary>Reads <c>folder=&gt;address</c> rules separated by semicolons or lines; a rule without both parts, or with an address that is not http or https, is left out.</summary>
    public static IReadOnlyList<PathRule> ParseRules(string? text)
    {
        var rules = new List<PathRule>();
        foreach (var part in (text ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int arrow = part.IndexOf("=>", StringComparison.Ordinal);
            if (arrow <= 0) continue;
            string folder = part[..arrow].Trim().TrimEnd('\\', '/'), address = part[(arrow + 2)..].Trim();
            if (folder.Length == 0 || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            rules.Add(new PathRule(folder, address.EndsWith('/') ? address : address + "/"));
        }
        return rules;
    }

    /// <summary>
    /// The address of <paramref name="path"/>: the rule with the longest folder that holds it, the rest of the path with its segments percent-encoded and joined with slashes. Null when no rule holds the file.
    /// Folders are compared without regard to case on Windows and macOS, where paths are.
    /// </summary>
    public static Uri? MapPath(IReadOnlyList<PathRule> rules, string path)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string normalized = path.Replace('\\', '/');
        PathRule? best = null;
        foreach (var rule in rules)
        {
            string folder = rule.Folder.Replace('\\', '/');
            if (normalized.Length > folder.Length && normalized.StartsWith(folder, comparison) && normalized[folder.Length] == '/' && (best is null || folder.Length > best.Folder.Length))
                best = rule;
        }
        if (best is null) return null;
        var rest = normalized[(best.Folder.Length + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (rest.Any(s => s == "..")) return null;
        var sb = new StringBuilder(best.Address);
        sb.Append(string.Join('/', rest.Select(Uri.EscapeDataString)));
        return Uri.TryCreate(sb.ToString(), UriKind.Absolute, out var uri) ? uri : null;
    }
}

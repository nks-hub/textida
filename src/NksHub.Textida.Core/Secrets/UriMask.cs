using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Secrets;

/// <summary>
/// Hides the secrets of a URI wherever it is shown or written down (tab title, recent files, logs): the password or the whole user part of the authority, the value of
/// every query or fragment parameter whose name suggests a secret, nothing else. Limit: a secret inside the path (<c>/t/abc/page</c>) cannot be told from a name and stays.
/// </summary>
public static partial class UriMask
{
    public const string Placeholder = "***";

    private static readonly string[] SecretNames = ["token", "secret", "pass", "pwd", "key", "sig", "auth", "credential", "session", "bearer", "jwt"];

    [GeneratedRegex("""[A-Za-z][A-Za-z0-9+.\-]*://[^\s"'<>]+""")]
    private static partial Regex UriPattern();

    /// <summary>True for <c>scheme://...</c>; a Windows path (<c>C:\x</c>) is not.</summary>
    public static bool IsUri(string? text) => text is not null && UriPattern().Match(text) is { Success: true, Index: 0 };

    /// <summary>The scheme in lower case, or null when <paramref name="uri"/> is not a URI.</summary>
    public static string? SchemeOf(string uri) => IsUri(uri) ? uri[..uri.IndexOf("://", StringComparison.Ordinal)].ToLowerInvariant() : null;

    /// <summary>The URI with its secrets replaced by <see cref="Placeholder"/>; text that is not a URI is returned as it is.</summary>
    public static string Mask(string uri)
    {
        int at = uri.IndexOf("://", StringComparison.Ordinal);
        if (at < 0) return uri;
        string head = uri[..(at + 3)], rest = uri[(at + 3)..], fragment = "", query = "";
        int hash = rest.IndexOf('#');
        if (hash >= 0) { fragment = rest[hash..]; rest = rest[..hash]; }
        int question = rest.IndexOf('?');
        if (question >= 0) { query = rest[question..]; rest = rest[..question]; }
        int slash = rest.IndexOf('/');
        string authority = slash < 0 ? rest : rest[..slash], path = slash < 0 ? "" : rest[slash..];
        int sign = authority.LastIndexOf('@');
        if (sign >= 0)
        {
            string user = authority[..sign];
            int colon = user.IndexOf(':');
            authority = (colon < 0 ? Placeholder : user[..colon] + ":" + Placeholder) + authority[sign..];
        }
        return head + authority + path + MaskPairs(query, '?') + (fragment.Contains('=') ? MaskPairs(fragment, '#') : fragment);
    }

    private static string MaskPairs(string part, char lead)
    {
        if (part.Length == 0) return part;
        var pairs = part[1..].Split('&').Select(pair =>
        {
            int eq = pair.IndexOf('=');
            return eq > 0 && IsSecretName(pair[..eq]) ? pair[..(eq + 1)] + Placeholder : pair;
        });
        return lead + string.Join('&', pairs);
    }

    private static bool IsSecretName(string name) => SecretNames.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>Masks every URI found in a line of text (a log line, a message from a plugin).</summary>
    public static string MaskText(string text) => text.Contains("://", StringComparison.Ordinal) ? UriPattern().Replace(text, m => Mask(m.Value)) : text;

    /// <summary>A short name for a tab: the last segment of the path of the masked URI, else its host.</summary>
    public static string DisplayName(string uri)
    {
        string masked = Mask(uri);
        int at = masked.IndexOf("://", StringComparison.Ordinal);
        string rest = at < 0 ? masked : masked[(at + 3)..];
        int cut = rest.IndexOfAny(['?', '#']);
        if (cut >= 0) rest = rest[..cut];
        string last = rest.TrimEnd('/').Split('/')[^1];
        int sign = last.LastIndexOf('@');
        if (sign >= 0 && !rest.Contains('/', StringComparison.Ordinal)) last = last[(sign + 1)..];
        try { return last.Length == 0 ? masked : Uri.UnescapeDataString(last); }
        catch (UriFormatException) { return last; }
    }
}

namespace NksHub.Textida.Core.Tools;

/// <summary>What a help source points at: a web address that opens in the browser, or a file of the data plugin that opens in the editor.</summary>
public enum HelpKind { Url, File }

/// <summary>
/// One place that explains a language, as a data plugin states it. <paramref name="Language"/> is the language mode id (<c>php</c>, <c>css</c>, ...).
/// <paramref name="Target"/> of a <see cref="HelpKind.Url"/> source is an address in which <c>${word}</c> stands for the word under the caret and
/// <c>${language}</c> for the language id; of a <see cref="HelpKind.File"/> source the full path of the file.
/// </summary>
public sealed record HelpSource(string Id, string Title, string Language, HelpKind Kind, string Target);

/// <summary>
/// Resolves "help for the word under the caret" from the sources a data plugin gave. The mapping from language to document or address is data; this class only
/// finds the word, picks the sources of the language and builds the address. It never opens anything: the caller shows the address and asks first.
/// </summary>
public static class LanguageHelp
{
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The word that contains <paramref name="column"/> or ends right before it, in a line of text; null when there is none.</summary>
    public static string? WordAt(string line, int column)
    {
        column = Math.Clamp(column, 0, line.Length);
        int start = column, end = column;
        if (start == line.Length || !IsWordChar(line[start]))
        {
            if (start == 0 || !IsWordChar(line[start - 1])) return null;
            start--;
            end = start;
        }
        while (start > 0 && IsWordChar(line[start - 1])) start--;
        while (end < line.Length && IsWordChar(line[end])) end++;
        return line[start..end];
    }

    /// <summary>The sources set for a language (ids compared without case), in the order they were given.</summary>
    public static IReadOnlyList<HelpSource> For(IEnumerable<HelpSource> sources, string language) =>
        [.. sources.Where(s => string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The address of a URL source for a word, or null when the template does not make an <c>http</c> or <c>https</c> address (a source can never open another kind).</summary>
    public static Uri? UrlFor(HelpSource source, string word)
    {
        if (source.Kind != HelpKind.Url) return null;
        var text = source.Target.Replace("${word}", Uri.EscapeDataString(word), StringComparison.Ordinal).Replace("${language}", Uri.EscapeDataString(source.Language), StringComparison.Ordinal);
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }
}

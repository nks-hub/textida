namespace NksHub.Textida.Core.Csv;

/// <summary>
/// How a delimited text is split. The delimiter and the quote are ASCII characters, so in every supported encoding they are
/// found as whole units of the bytes (a byte in UTF-8 and single-byte text, 2 or 4 bytes in UTF-16 and UTF-32).
/// </summary>
/// <param name="Delimiter">Separates fields; not CR, LF or the quote.</param>
/// <param name="Quote">Encloses a field that holds a delimiter, a quote or a line break (a doubled quote inside is one quote); <c>'\0'</c> means no quoting.</param>
/// <param name="HasHeader">The first row names the columns.</param>
public readonly record struct CsvDialect(char Delimiter, char Quote = '"', bool HasHeader = true)
{
    public static CsvDialect Csv { get; } = new(',');
    public static CsvDialect Tsv { get; } = new('\t');

    public bool HasQuote => Quote != '\0';

    /// <summary>Tab for <c>.tsv</c> and <c>.tab</c>, comma for anything else.</summary>
    public static CsvDialect ForPath(string? path) =>
        Path.GetExtension(path ?? "").ToLowerInvariant() is ".tsv" or ".tab" ? Tsv : Csv;

    /// <summary>
    /// Picks the delimiter from the first lines of a text: the candidate (comma, semicolon, tab, pipe) that splits most of them into the same
    /// number of fields (more than one). Quotes are tracked per line, so a quoted line break may mislead it. Without a clear winner the fallback stays.
    /// </summary>
    public static CsvDialect Detect(string sample, CsvDialect fallback, int maxLines = 20)
    {
        var lines = sample.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(maxLines).ToList();
        if (lines.Count < 2) return fallback;
        char best = fallback.Delimiter;
        (int Lines, int Fields) bestScore = (0, 0);
        foreach (char candidate in new[] { ',', ';', '\t', '|' })
        {
            if (candidate == fallback.Quote) continue;
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate, fallback.Quote)).ToList();
            var (fields, hits) = counts.Where(c => c > 0).GroupBy(c => c).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).ThenByDescending(g => g.Key).FirstOrDefault();
            if (hits * 5 < lines.Count * 4) continue;
            if ((hits, fields) is var score && (score.hits > bestScore.Lines || (score.hits == bestScore.Lines && score.fields > bestScore.Fields)))
                (best, bestScore) = (candidate, score);
        }
        return bestScore.Lines == 0 ? fallback : fallback with { Delimiter = best };
    }

    private static int CountOutsideQuotes(string line, char delimiter, char quote)
    {
        int count = 0;
        bool inQuote = false;
        foreach (char c in line)
        {
            if (c == quote && quote != '\0') inQuote = !inQuote;
            else if (c == delimiter && !inQuote) count++;
        }
        return count;
    }

    public void Validate()
    {
        if (Delimiter is '\r' or '\n' or '\0' || Delimiter > 0x7F || Quote > 0x7F || Delimiter == Quote || Quote is '\r' or '\n')
            throw new ArgumentException("The delimiter and the quote must be different ASCII characters other than a line break.");
    }
}

/// <summary>A value cannot be written in the dialect (a line break or delimiter in a file without quoting).</summary>
public sealed class CsvFormatException(string message) : InvalidOperationException(message);

/// <summary>One field as it lies in the document: the whole raw range and, for a quoted field, the range between the quotes.</summary>
/// <param name="Start">First byte of the field, the opening quote included.</param>
/// <param name="End">Byte after the field, the closing quote and anything glued to it included.</param>
/// <param name="Quoted">The field starts with a quote.</param>
/// <param name="ValueStart">First byte of the content (after the opening quote).</param>
/// <param name="ValueEnd">Byte after the content (before the closing quote, or <paramref name="End"/> when the quote never closes).</param>
/// <param name="Closed">The quote was closed, so <c>[ValueEnd + unit, End)</c> holds text glued behind it.</param>
public readonly record struct CsvField(long Start, long End, bool Quoted, long ValueStart, long ValueEnd, bool Closed);

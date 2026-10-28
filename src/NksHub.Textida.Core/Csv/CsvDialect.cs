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
    /// Picks the delimiter from the first rows of a text: the candidate (comma, semicolon, tab, pipe) that splits most of them into the same
    /// number of fields (more than one); a tie goes to the fallback. Rows are cut as <see cref="CsvTable"/> cuts them: a line break inside a quoted field (one that opens at the start of a field) does not end the row,
    /// so a cell of several lines is one row. <paramref name="truncated"/>: the sample is only the start of a longer text, so a row the sample ends in is not counted. Without a clear winner the fallback stays.
    /// </summary>
    public static CsvDialect Detect(string sample, CsvDialect fallback, int maxLines = 20, bool truncated = false)
    {
        char best = fallback.Delimiter;
        (int Lines, int Fields) bestScore = (0, 0);
        foreach (char candidate in new[] { ',', ';', '\t', '|' })
        {
            if (candidate == fallback.Quote) continue;
            var counts = DelimitersPerRow(sample, candidate, fallback.Quote, maxLines, truncated);
            if (counts.Count < 2) continue;
            var (fields, hits) = counts.Where(c => c > 0).GroupBy(c => c).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).ThenByDescending(g => g.Key).FirstOrDefault();
            if (hits * 5 < counts.Count * 4) continue;
            bool tie = hits == bestScore.Lines && fields == bestScore.Fields;
            if ((hits, fields) is var score && (score.hits > bestScore.Lines || (score.hits == bestScore.Lines && score.fields > bestScore.Fields) || (tie && candidate == fallback.Delimiter)))
                (best, bestScore) = (candidate, score);
        }
        return bestScore.Lines == 0 ? fallback : fallback with { Delimiter = best };
    }

    /// <summary>The number of delimiters in each non-empty row of the text, the first <paramref name="maxRows"/> rows. A row that ends inside a quoted field, or (with <paramref name="truncated"/>) at the end of the text, is not complete and is left out.</summary>
    private static List<int> DelimitersPerRow(string text, char delimiter, char quote, int maxRows, bool truncated)
    {
        var rows = new List<int>();
        int count = 0;
        bool inQuote = false, fieldStart = true, any = false;
        for (int i = 0; i < text.Length && rows.Count < maxRows; i++)
        {
            char c = text[i];
            if (inQuote)
            {
                if (c != quote) continue;
                if (i + 1 < text.Length && text[i + 1] == quote) i++; // a doubled quote is a quote of the text
                else inQuote = false;
                continue;
            }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (any) rows.Add(count);
                (count, any, fieldStart) = (0, false, true);
                continue;
            }
            any = true;
            if (c == delimiter) { count++; fieldStart = true; continue; }
            if (fieldStart && quote != '\0' && c == quote) inQuote = true;
            fieldStart = false;
        }
        if (any && !inQuote && !truncated && rows.Count < maxRows) rows.Add(count);
        return rows;
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

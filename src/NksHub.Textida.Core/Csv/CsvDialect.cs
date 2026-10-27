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

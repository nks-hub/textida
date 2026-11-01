using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Converters;

/// <summary>The converter cannot read the file, or is asked to write what it cannot write.</summary>
public sealed class ConverterException(string message) : Exception(message);

/// <summary>The text a file is shown as, and what the converter wants the user to know about it.</summary>
public sealed record ConverterRead(string Text, IReadOnlyList<string> Notes);

/// <summary>The bytes a text is saved as, and everything the conversion could not keep (empty: nothing is lost).</summary>
public sealed record ConverterWrite(byte[] Bytes, IReadOnlyList<string> Losses);

/// <summary>
/// The contract of a file type converter (PZ.5.2). A converter shows a file of one type as text and, if it can write, turns the text back into that type.
/// <list type="bullet">
/// <item><see cref="Read"/> takes the whole file as bytes and gives text; it throws <see cref="ConverterException"/> for a file it cannot read. It changes nothing on disk.</item>
/// <item><see cref="Write"/> takes the text and the bytes of the file as they are on disk (so what the text does not show, such as the encoding and the byte order mark, can be kept) and gives the new bytes
/// and the list of things it could not keep. A converter that cannot write has <see cref="CanWrite"/> false and the file opens read only.</item>
/// <item>A save is allowed only after <see cref="ConverterRoundTrip.Check"/>: the written bytes are read back, and the text has to be the text that was saved. A difference, or any loss the converter names, is refused unless the user consents to that save.</item>
/// <item>Input and output are bytes and text. A converter has no access to the disk, the network or the editor.</item>
/// </list>
/// </summary>
public interface IFileConverter
{
    string Id { get; }
    string Name { get; }
    /// <summary>File extensions the converter is offered for, with the dot, lower case.</summary>
    IReadOnlyList<string> Extensions { get; }
    bool CanWrite { get; }
    ConverterRead Read(byte[] input);
    ConverterWrite Write(string text, byte[] original);
}

/// <summary>What reading a saved file back shows: <see cref="Stable"/> when it gives the text that was saved, and the losses the converter named.</summary>
public sealed record RoundTripReport(bool Stable, IReadOnlyList<string> Losses, byte[] Bytes)
{
    public bool NeedsConsent => !Stable || Losses.Count > 0;
}

public static class ConverterRoundTrip
{
    /// <summary>Writes <paramref name="text"/> and reads the result back; never touches the disk.</summary>
    public static RoundTripReport Check(IFileConverter converter, string text, byte[] original)
    {
        var written = converter.Write(text, original);
        bool stable;
        try { stable = converter.Read(written.Bytes).Text == text; }
        catch (ConverterException) { stable = false; }
        return new RoundTripReport(stable, written.Losses, written.Bytes);
    }
}

/// <summary>CSV and TSV: a <c>.csv</c> file is shown as tab separated text and saved back as CSV with the separator it had (comma or semicolon), the encoding it had and the same line ends.</summary>
public sealed class CsvTsvConverter : IFileConverter
{
    public string Id => "csv-tsv";
    public string Name => "CSV as TSV";
    public IReadOnlyList<string> Extensions { get; } = [".csv"];
    public bool CanWrite => true;

    private static readonly char[] Separators = [',', ';'];

    private static char SeparatorOf(string text)
    {
        // the one of the two that appears more often in the first line outside quotes
        int comma = 0, semicolon = 0;
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && c is '\r' or '\n') break;
            else if (!quoted && c == ',') comma++;
            else if (!quoted && c == ';') semicolon++;
        }
        return semicolon > comma ? ';' : ',';
    }

    private static string EolOf(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : "\r\n";

    /// <summary>The records of a CSV text: fields split at the separator outside quotes, <c>""</c> inside quotes is one quote.</summary>
    internal static List<List<string>> Parse(string text, char separator)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) { quoted = true; any = true; }
            else if (c == separator) { fields.Add(field.ToString()); field.Clear(); any = true; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                field.Clear();
                records.Add(fields);
                fields = [];
                any = false;
            }
            else { field.Append(c); any = true; }
        }
        if (any || field.Length > 0 || fields.Count > 0) { fields.Add(field.ToString()); records.Add(fields); }
        return records;
    }

    // a tab, a line end or a backslash inside a field is written as \t, \n, \r and \\ in the TSV text, so the text has one record to a line and reads back exactly
    internal static string Escape(string field) => field.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    internal static string Unescape(string field)
    {
        if (!field.Contains('\\')) return field;
        var sb = new StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 1 < field.Length)
            {
                char next = field[++i];
                sb.Append(next switch { 't' => '\t', 'n' => '\n', 'r' => '\r', '\\' => '\\', _ => next });
            }
            else sb.Append(field[i]);
        }
        return sb.ToString();
    }

    public ConverterRead Read(byte[] input)
    {
        var decoded = EncodingGuesser.Guess(input.AsSpan(0, Math.Min(input.Length, 1 << 20)), input.Length <= 1 << 20).Codec.Decode(input);
        string text = decoded.Text;
        char separator = SeparatorOf(text);
        var lines = Parse(text, separator).Select(r => string.Join('\t', r.Select(Escape)));
        return new ConverterRead(string.Join('\n', lines) + (text.Length > 0 ? "\n" : ""), [$"separator {(separator == ';' ? "semicolon" : "comma")}, {decoded.Codec.Name}"]);
    }

    public ConverterWrite Write(string text, byte[] original)
    {
        var codec = EncodingGuesser.Guess(original.AsSpan(0, Math.Min(original.Length, 1 << 20)), original.Length <= 1 << 20).Codec;
        string originalText = codec.Decode(original).Text;
        char separator = SeparatorOf(originalText);
        string eol = EolOf(originalText);
        var sb = new StringBuilder();
        var rows = text.Split('\n');
        int count = text.EndsWith('\n') ? rows.Length - 1 : rows.Length;
        for (int i = 0; i < count; i++)
        {
            var fields = rows[i].TrimEnd('\r').Split('\t').Select(Unescape);
            sb.Append(string.Join(separator, fields.Select(f => f.IndexOfAny([separator, '"', '\r', '\n']) >= 0 || f.Length != f.Trim().Length ? "\"" + f.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : f)));
            sb.Append(eol);
        }
        var losses = new List<string>();
        byte[] bom = codec.Bom.Length > 0 || codec.Kind == CodecKind.Utf8 && original.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? codec.Bom : [];
        byte[] body;
        try { body = codec.Encode(sb.ToString()); }
        catch (UnrepresentableCharactersException e)
        {
            body = codec.EncodeLossy(sb.ToString());
            losses.Add($"{e.Report.Total} character(s) cannot be written in {codec.Name} and become a question mark");
        }
        return new ConverterWrite(bom.Length > 0 && !body.AsSpan().StartsWith(bom) ? [.. bom, .. body] : body, losses);
    }
}

/// <summary>
/// A converter that is a program on the machine (PZ.5.2): <c>pandoc -t plain {input}</c> for a <c>.docx</c> is the model. The command line has <c>{input}</c>, which is replaced by the path of a copy of the file in a
/// private temporary folder, and, for a converter that writes, a second command with <c>{input}</c> (the text, as UTF-8) and <c>{output}</c>. The program's standard output is the text (UTF-8). A program that exits with
/// a code other than 0, or runs longer than the limit, is a <see cref="ConverterException"/> with the start of what it printed on standard error.
/// </summary>
public sealed class ExternalConverter(string id, string name, IReadOnlyList<string> extensions, string program, IReadOnlyList<string> readArguments, IReadOnlyList<string>? writeArguments = null, string? writeExtension = null, TimeSpan? timeout = null) : IFileConverter
{
    public string Id => id;
    public string Name => name;
    public IReadOnlyList<string> Extensions => extensions;
    public bool CanWrite => writeArguments is not null;

    private TimeSpan Limit => timeout ?? TimeSpan.FromSeconds(30);

    private (int Exit, string Output, string Errors) Run(IReadOnlyList<string> arguments, string folder, string input, string? output)
    {
        var info = ProcessLaunch.CreateStartInfo(program, [.. arguments.Select(a => a.Replace("{input}", input, StringComparison.Ordinal).Replace("{output}", output ?? "", StringComparison.Ordinal))]);
        info.WorkingDirectory = folder;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardOutputEncoding = info.StandardErrorEncoding = new UTF8Encoding(false);
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { throw new ConverterException($"{name}: {e.Message}"); }
        ProcessLaunch.Track(process);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Limit))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new ConverterException($"{name}: no answer within {Limit.TotalSeconds:0} s");
        }
        return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static string Folder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "textida-convert-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public ConverterRead Read(byte[] input)
    {
        string folder = Folder();
        try
        {
            string path = Path.Combine(folder, "input" + (extensions.Count > 0 ? extensions[0] : ""));
            File.WriteAllBytes(path, input);
            var (exit, output, errors) = Run(readArguments, folder, path, null);
            if (exit != 0) throw new ConverterException($"{name}: exit code {exit}. {errors.Trim()[..Math.Min(errors.Trim().Length, 300)]}");
            return new ConverterRead(output.ReplaceLineEndings("\n"), []);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    public ConverterWrite Write(string text, byte[] original)
    {
        if (writeArguments is null) throw new ConverterException($"{name} cannot write.");
        string folder = Folder();
        try
        {
            string input = Path.Combine(folder, "text.txt"), output = Path.Combine(folder, "output" + (writeExtension ?? (extensions.Count > 0 ? extensions[0] : "")));
            File.WriteAllText(input, text, new UTF8Encoding(false));
            var (exit, _, errors) = Run(writeArguments, folder, input, output);
            if (exit != 0 || !File.Exists(output)) throw new ConverterException($"{name}: exit code {exit}. {errors.Trim()[..Math.Min(errors.Trim().Length, 300)]}");
            return new ConverterWrite(File.ReadAllBytes(output), []);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

/// <summary>The converters the editor knows: the built-in CSV and TSV and, when a program of the machine is found, the ones that need it.</summary>
public static class FileConverters
{
    public static readonly IFileConverter CsvTsv = new CsvTsvConverter();

    /// <summary>A converter that reads <c>.docx</c> as text through <c>pandoc</c> when the program is found; read only. Null when it is not there (nothing is downloaded).</summary>
    public static IFileConverter? DocxText() =>
        ToolLocator.Find("pandoc") is { } pandoc ? new ExternalConverter("docx-text", "Word document as text (pandoc)", [".docx"], pandoc, ["-f", "docx", "-t", "plain", "--wrap=none", "{input}"]) : null;

    public static IReadOnlyList<IFileConverter> All() => [CsvTsv, .. new[] { DocxText() }.OfType<IFileConverter>()];

    public static IReadOnlyList<IFileConverter> For(string path) => [.. All().Where(c => c.Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))];
}

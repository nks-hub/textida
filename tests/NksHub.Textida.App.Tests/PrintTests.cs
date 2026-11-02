using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.2.2: the pages of a print job, the PDF file made of them, and (on Windows) a real print through GDI to the printer that writes PDF files.</summary>
public class PrintLayoutTests
{
    private static List<List<PrintRun>> Lines(string text, params (int Start, int Length, int? Rgb)[] segments) =>
        PrintLayout.SourceLines(text, segments.Length == 0 ? [(0, text.Length, null)] : segments, 0);

    [Fact]
    public void Lines_split_at_every_kind_of_break_and_keep_the_colour_of_each_run()
    {
        var lines = Lines("ab\r\ncd\nef", (0, 2, 0xFF0000), (2, 2, null), (4, 5, 0x00FF00));
        Assert.Equal(3, lines.Count);
        Assert.Equal([new PrintRun("ab", 0xFF0000)], lines[0]);
        Assert.Equal([new PrintRun("cd", 0x00FF00)], lines[1]);
        Assert.Equal([new PrintRun("ef", 0x00FF00)], lines[2]);
        Assert.Single(Lines("a\n"));
    }

    [Fact]
    public void A_long_line_wraps_a_tab_goes_to_its_stop_and_only_the_first_row_has_a_number()
    {
        var pages = PrintLayout.Paginate(Lines("\tabcdefghijklmnop"), new PrintOptions(14, 10, true, 4));
        var rows = pages.Single().Lines;
        Assert.True(rows.Count >= 2);
        Assert.Equal(1, rows[0].Number);
        Assert.All(rows.Skip(1), r => Assert.Null(r.Number));
        Assert.All(rows, r => Assert.True(r.Runs.Sum(x => x.Text.Length) <= 14));
        Assert.StartsWith("1 ", rows[0].Runs[0].Text);
        Assert.StartsWith("    abcd", string.Concat(rows[0].Runs.Skip(1).Select(r => r.Text)));
    }

    [Fact]
    public void Pages_fill_to_the_row_count_and_the_numbers_are_aligned_to_the_widest()
    {
        var text = string.Join('\n', Enumerable.Range(1, 25).Select(i => "x" + i));
        var pages = PrintLayout.Paginate(Lines(text), new PrintOptions(40, 10, true));
        Assert.Equal([10, 10, 5], pages.Select(p => p.Lines.Count));
        Assert.Equal([1, 2, 3], pages.Select(p => p.Number));
        Assert.Equal(" 1 ", pages[0].Lines[0].Runs[0].Text);
        Assert.Equal("25 ", pages[2].Lines[4].Runs[0].Text);
        Assert.Single(PrintLayout.Paginate(Lines(""), new PrintOptions(40, 10, false)));
    }

    [Fact]
    public void The_pdf_has_a_page_for_each_page_the_header_the_colours_and_an_embedded_font()
    {
        var text = string.Join('\n', Enumerable.Range(1, 200).Select(i => i == 3 ? "řešení (ě) \\ ů č ť €" : "line " + i));
        var pages = PrintLayout.Paginate(Lines(text, (0, 4, 0xFF0000), (4, text.Length - 4, null)), PdfExport.Options(true));
        Assert.True(pages.Count >= 3);
        var pdf = PdfExport.Write(pages, "demo.cs");
        string s = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-", s);
        Assert.Contains("%%EOF", s[^8..]);
        Assert.Equal(pages.Count, Pages(s));
        Assert.Contains($"/Count {pages.Count}", s);
        Assert.Contains("/FontFile2", s);
        Assert.Contains("/ToUnicode", s);
        Assert.True(pdf.Length < 150_000, $"{pdf.Length} bytes: the font was not subset");
        string content = string.Join('\n', Streams(pdf));
        Assert.Contains("1 0 0 rg", content); // the red run
        Assert.Contains(".502 .502 .502 rg", content); // the grey line numbers
        var mapped = MappedText(pdf);
        foreach (char c in "demo.cs/123řešení()\\ůčť€") Assert.Contains(c.ToString(), mapped);
    }

    [AvaloniaFact] // the bidi levels of the Arabic line come from Avalonia's text formatter
    public void Every_script_prints_in_an_embedded_font_and_maps_back_to_its_text()
    {
        var pages = PrintLayout.Paginate(Lines(UnicodeSample), PdfExport.Options(true));
        var pdf = PdfExport.Write(pages, "unicode.txt");
        string s = Encoding.Latin1.GetString(pdf);

        Assert.Single(pages);
        Assert.Equal(1, Pages(s));
        Assert.Contains("/FontFile2", s);
        Assert.Contains("/ToUnicode", s);
        Assert.True(pdf.Length < 1_000_000, $"{pdf.Length} bytes: a fallback font was not subset");
        var mapped = MappedText(pdf);
        // the joined Arabic letters have no entry of their own in the cmap; their text is in the ActualText of the run
        var missing = UnicodeSample.EnumerateRunes().Where(r => r.Value is < 0x0600 or > 0x06FF).Select(r => r.ToString()).Where(r => r is not ("\n" or " ") && !mapped.Contains(r)).Distinct().ToList();
        Assert.True(missing.Count == 0, "not mapped: " + string.Join(' ', missing));
        Assert.Contains("مرحبا", ActualTexts(pdf).Select(a => a.Text));
        Assert.DoesNotContain("?", mapped); // nothing was replaced by a question mark
    }

    [AvaloniaFact]
    public void Arabic_and_hebrew_print_joined_right_to_left_and_read_back_in_logical_order()
    {
        const string sample = "مرحبا بالعالم\nשלום עולם\nlet s = \"مرحبا\";\nب ب";
        var pdf = PdfExport.Write(PrintLayout.Paginate(Lines(sample), PdfExport.Options(true)), "rtl.txt");
        var runs = ActualTexts(pdf);
        Assert.Equal(["مرحبا بالعالم", "שלום עולם", "مرحبا", "ب ب"], runs.Select(r => r.Text));
        // the beh inside the word is joined (medial form), not the isolated glyph of the last line (two letters, so that line gets an ActualText too); the word shapes the same in both lines it is in
        var word = runs[2].Glyphs;
        Assert.Equal(5, word.Count);
        Assert.DoesNotContain(runs[3].Glyphs[0], word);
        Assert.Equal(runs[0].Glyphs[^5..], word); // drawn right to left, so the first word of the line is the last glyphs
    }

    /// <summary>The ActualText spans of the content, each with the glyphs drawn inside it in the order they are drawn.</summary>
    private static List<(string Text, List<int> Glyphs)> ActualTexts(byte[] pdf) =>
        Regex.Matches(string.Join('\n', Streams(pdf)), @"/ActualText\s*<FEFF([0-9A-Fa-f]*)>(.*?)EMC", RegexOptions.Singleline)
            .Select(m => (Encoding.BigEndianUnicode.GetString(Convert.FromHexString(m.Groups[1].Value)),
                Regex.Matches(m.Groups[2].Value, @"<([0-9A-Fa-f]+)>\s*Tj").SelectMany(t => t.Groups[1].Value.Chunk(4).Select(c => Convert.ToInt32(new string(c), 16))).ToList()))
            .ToList();

    [Fact]
    public void A_surrogate_pair_never_splits_across_rows()
    {
        var pages = PrintLayout.Paginate(Lines("abc😀d"), new PrintOptions(4, 10, false));
        Assert.All(pages.Single().Lines.SelectMany(l => l.Runs), r => Assert.False(char.IsHighSurrogate(r.Text[^1])));
    }

    internal const string UnicodeSample = "Příliš žluťoučký kůň\nGrüße, Straße\nПривет, мир\nΓειά σου κόσμε\n漢字かなカナ 中文\nمرحبا\n😀 🚀";

    private static int Pages(string pdf) => Regex.Matches(pdf, @"/Type\s*/Page\b(?!s)").Count;

    /// <summary>The streams of the file, inflated where they are compressed.</summary>
    internal static List<string> Streams(byte[] pdf)
    {
        string s = Encoding.Latin1.GetString(pdf);
        var streams = new List<string>();
        foreach (Match m in Regex.Matches(s, @"/Length (\d+)[^>]*>>\s*stream\r?\n"))
        {
            var raw = pdf.AsSpan(m.Index + m.Length, int.Parse(m.Groups[1].Value)).ToArray();
            try
            {
                using var z = new System.IO.Compression.ZLibStream(new MemoryStream(raw), System.IO.Compression.CompressionMode.Decompress);
                using var o = new MemoryStream();
                z.CopyTo(o);
                streams.Add(Encoding.Latin1.GetString(o.ToArray()));
            }
            catch (InvalidDataException) { streams.Add(Encoding.Latin1.GetString(raw)); }
        }
        return streams;
    }

    /// <summary>Every text the ToUnicode maps of the file give back for its glyphs.</summary>
    internal static HashSet<string> MappedText(byte[] pdf)
    {
        static string Utf16(string hex) => Encoding.BigEndianUnicode.GetString(Convert.FromHexString(hex));
        var mapped = new HashSet<string>();
        foreach (string cmap in Streams(pdf).Where(x => x.Contains("begincmap")))
        {
            foreach (Match block in Regex.Matches(cmap, "beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"<[0-9A-Fa-f]+>\s*<([0-9A-Fa-f]+)>"))
                    mapped.Add(Utf16(m.Groups[1].Value));
            foreach (Match block in Regex.Matches(cmap, "beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int count = Convert.ToInt32(m.Groups[2].Value, 16) - Convert.ToInt32(m.Groups[1].Value, 16);
                    string first = Utf16(m.Groups[3].Value);
                    for (int i = 0; i <= count; i++) mapped.Add(first[..^1] + (char)(first[^1] + i));
                }
        }
        return mapped;
    }
}

[Collection("LocState")]
public class PrintAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public PrintAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task Export_as_pdf_writes_the_highlighted_document_with_its_name_in_the_header()
    {
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("demo.js", "// note\nfunction f() { return \"x\"; }\n"), null);
        string pdf = Path.Combine(_temp.Path, "out.pdf");
        _dialogs.SaveFiles.Enqueue(pdf);

        await w.ExportPdfAsync();

        var bytes = await File.ReadAllBytesAsync(pdf);
        Assert.Equal(["demo.pdf"], _dialogs.SaveFileNames);
        var mapped = PrintLayoutTests.MappedText(bytes);
        foreach (char c in "demo.jsfunction\"") Assert.Contains(c.ToString(), mapped);
        Assert.Matches(@"\d\.?\d* \d\.?\d* \d\.?\d* rg", string.Join('\n', PrintLayoutTests.Streams(bytes)));
        Assert.Contains(Loc.Get(K.PrintPdfDone, ("name", "out.pdf")), w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task Print_gives_the_pages_to_the_printer_and_says_so()
    {
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("demo.js", "let a = 1;\nlet b = 2;\n"), null);
        IReadOnlyList<PrintPage>? printed = null;
        w.PrintOverride = (title, layout) =>
        {
            printed = layout(new PrintOptions(80, 50));
            return (WindowsPrinter.Outcome.Printed, null);
        };

        await w.PrintDocumentAsync();

        Assert.Equal(2, printed!.Single().Lines.Count);
        Assert.Equal(Loc.Get(K.PrintDone, ("name", "demo.js")), w.StatusMessageText);
    }
}

public class WindowsPrinterTests
{
    [Fact]
    public void A_real_print_to_the_pdf_printer_of_windows_makes_a_pdf_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(Path.GetTempPath(), "textida-print-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
        try
        {
            var text = string.Join('\n', Enumerable.Range(1, 150).Select(i => $"let value{i} = {i}; // řádek"));
            var lines = PrintLayout.SourceLines(text, [(0, text.Length, 0x0000C0)], 0);
            IReadOnlyList<PrintPage>? pages = null;
            var (outcome, error) = WindowsPrinter.PrintTo("Microsoft Print to PDF", file, "demo.js", options => pages = PrintLayout.Paginate(lines, options), true);
            if (outcome == WindowsPrinter.Outcome.Failed && Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\Microsoft Print to PDF") is null) return; // the printer is not installed on this machine
            Assert.True(outcome == WindowsPrinter.Outcome.Printed, error);
            Assert.True(pages!.Count >= 2);
            var bytes = File.ReadAllBytes(file);
            Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.True(bytes.Length > 2000);
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    }
}

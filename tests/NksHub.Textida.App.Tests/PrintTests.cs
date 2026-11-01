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
    public void The_pdf_has_a_page_for_each_page_the_header_the_colours_and_the_central_european_letters()
    {
        var text = string.Join('\n', Enumerable.Range(1, 200).Select(i => i == 3 ? "řešení (ě) \\ ů č ť €" : "line " + i));
        var pages = PrintLayout.Paginate(Lines(text, (0, 4, 0xFF0000), (4, text.Length - 4, null)), PdfExport.Options(true));
        Assert.True(pages.Count >= 3);
        var pdf = PdfExport.Write(pages, "demo.cs");
        string s = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", s);
        Assert.EndsWith("%%EOF\n", s);
        Assert.Equal(pages.Count, Regex.Matches(s, "/Type /Page /Parent").Count);
        Assert.Contains($"/Count {pages.Count}", s);
        Assert.Contains("(demo.cs) Tj", s);
        Assert.Contains($"(1 / {pages.Count}) Tj", s);
        Assert.Contains("1 0 0 rg", s); // the red run
        Assert.Contains("/Differences", s);
        Assert.Contains("/rcaron", s);
        Assert.Contains("/ecaron", s);
        Assert.Contains("/uring", s);
        Assert.Contains("/tcaron", s);
        Assert.Contains("\\\\", s); // the backslash is escaped
        // the xref offsets point at the objects
        var xref = Regex.Match(s, @"startxref\n(\d+)\n");
        Assert.StartsWith("xref", s[int.Parse(xref.Groups[1].Value)..]);
        foreach (Match m in Regex.Matches(s, @"(\d{10}) 00000 n"))
            Assert.Matches(@"^\d+ 0 obj", s[int.Parse(m.Groups[1].Value)..]);
    }

    [Fact]
    public void Letters_the_font_cannot_show_print_as_a_question_mark()
    {
        var pdf = Encoding.Latin1.GetString(PdfExport.Write(PrintLayout.Paginate(Lines("a中b"), new PrintOptions(40, 10, false)), "t"));
        Assert.Contains("(a?b) Tj", pdf);
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

        string s = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(pdf));
        Assert.Equal(["demo.pdf"], _dialogs.SaveFileNames);
        Assert.Contains("(demo.js) Tj", s);
        Assert.Contains("function", s);
        Assert.Matches(@"\d\.?\d* \d\.?\d* \d\.?\d* rg", s);
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

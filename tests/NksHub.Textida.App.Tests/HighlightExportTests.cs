using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using Doc = NksHub.Textida.Core.Documents.Document;
using static NksHub.Textida.App.Tests.HighlightExportTests;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.3.2: HTML and RTF export and copy with the colors of the editor.</summary>
public class HighlightExportTests
{
    internal const string CSharp = "// comment with é and <tag> & \"quotes\"\nusing System;\nclass A\n{\n    string s = \"x<y&z\\n\"; int n = 42; /* multi\n    line */ void M() { }\n}\n";
    internal const string Markdown = "# Heading\n\nSome **bold** and `code` and [link](http://example.org)\n\n```csharp\nint x = 1;\n```\n\n- item\n";

    internal static ExportTheme Theme(bool dark = false) =>
        new(dark ? Colors.Black : Colors.White, dark ? Colors.White : Colors.Black, dark ? SyntaxPalette.Dark : SyntaxPalette.Light);

    /// <summary>What the editor does: the tokens of the grammar line by line with the state carried over, the color of each kind from the palette. One entry per character; null is plain.</summary>
    internal static Color?[] EditorColors(string text, string grammar, SyntaxPalette palette)
    {
        var colors = new Color?[text.Length];
        var tokenizer = LanguageModes.Tokenizer(grammar);
        var state = tokenizer.InitialState;
        int pos = 0;
        foreach (string line in Regex.Split(text, "(?<=\n)"))
        {
            if (line.Length == 0) continue;
            string body = line.TrimEnd('\n');
            var tokens = new List<Token>();
            state = tokenizer.TokenizeLine(body, state, tokens);
            foreach (var token in tokens)
                if (palette.GetBrush(token.Kind) is ISolidColorBrush brush)
                    for (int i = token.Start; i < Math.Min(token.End, body.Length); i++) colors[pos + i] = brush.Color;
            pos += line.Length;
        }
        return colors;
    }

    /// <summary>The color of every character of the text in the HTML, read back from the spans.</summary>
    internal static Color?[] HtmlColors(string html)
    {
        int open = html.IndexOf("<pre", StringComparison.Ordinal);
        string body = html[(html.IndexOf('>', open) + 1)..html.LastIndexOf("</pre>", StringComparison.Ordinal)];
        var result = new List<Color?>();
        foreach (Match m in Regex.Matches(body, "<span style=\"color:(#[0-9A-F]{6})\">(.*?)</span>|([^<]+)", RegexOptions.Singleline))
        {
            Color? color = m.Groups[1].Success ? Color.Parse(m.Groups[1].Value) : null;
            string text = System.Net.WebUtility.HtmlDecode(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
            result.AddRange(Enumerable.Repeat(color, text.Length));
        }
        return [.. result];
    }

    [Theory]
    [InlineData(CSharp, "csharp", false)]
    [InlineData(CSharp, "csharp", true)]
    [InlineData(Markdown, "markdown", false)]
    [InlineData(Markdown, "markdown", true)]
    public void The_html_has_the_color_the_editor_gives_each_character(string text, string grammar, bool dark)
    {
        var theme = Theme(dark);
        var expected = EditorColors(text, grammar, theme.Palette);
        var actual = HtmlColors(HighlightExport.Html(text, LanguageModes.Tokenizer(grammar), null, theme));
        Assert.Equal(expected, actual);
        Assert.True(expected.Distinct().Count() >= 3, "the sample must use several colors");
    }

    [Fact]
    public void A_selection_is_colored_by_the_state_of_the_whole_document()
    {
        // the selection starts inside a block comment: it is a comment in the editor, so it is one here
        int from = CSharp.IndexOf("line */", StringComparison.Ordinal);
        var theme = Theme();
        string html = HighlightExport.Html(CSharp, LanguageModes.Tokenizer("csharp"), null, theme, range: (from, 7));
        var colors = HtmlColors(html);
        var comment = ((ISolidColorBrush)theme.Palette.GetBrush("comment")!).Color;
        Assert.Equal(7, colors.Length);
        Assert.All(colors, c => Assert.Equal(comment, c));
    }

    [Fact]
    public void A_text_taken_from_the_middle_of_a_document_is_colored_from_the_state_given_for_its_start()
    {
        var tokenizer = LanguageModes.Tokenizer("csharp");
        var theme = Theme();
        const string before = "int a; /* open\n", tail = "  inside */ int b;\n";
        var state = tokenizer.TokenizeLine("int a; /* open", tokenizer.InitialState, []);
        var expected = EditorColors(before + tail, "csharp", theme.Palette)[before.Length..];
        Assert.Equal(expected, HtmlColors(HighlightExport.Html(tail, tokenizer, null, theme, startState: state)));
        Assert.Equal(expected, HtmlColors(HighlightExport.Html(tail, tokenizer, null, theme, startState: state, range: (0, tail.Length))));
        Assert.NotEqual(expected, HtmlColors(HighlightExport.Html(tail, tokenizer, null, theme)));
        Assert.Contains($"\\red{((ISolidColorBrush)theme.Palette.GetBrush("comment")!).Color.R}", HighlightExport.Rtf(tail, tokenizer, null, theme, startState: state));
    }

    [Fact]
    public void Html_escapes_markup_and_a_page_has_a_title()
    {
        string page = HighlightExport.Html("a < b && \"c\"", null, null, Theme(), title: "x<y");
        Assert.StartsWith("<!DOCTYPE html>", page);
        Assert.Contains("<title>x&lt;y</title>", page);
        Assert.Contains("a &lt; b &amp;&amp; &quot;c&quot;", page);
        Assert.DoesNotContain("<pre", HighlightExport.Html("x", null, null, Theme()).Replace("<pre style", "")); // the fragment is one pre element
    }

    [Fact]
    public void A_log_gets_the_color_of_the_level_of_each_line()
    {
        var theme = Theme();
        var colors = HtmlColors(HighlightExport.Html("ERROR bad\nINFO fine\nnothing\n", null, LogLevelRules.Default, theme));
        Color Of(LogLevel l) => ((ISolidColorBrush)theme.Palette.GetBrush(LogLevelKinds.Of(l))!).Color;
        Assert.Equal(Of(LogLevel.Error), colors[0]);
        Assert.Equal(Of(LogLevel.Info), colors[10]);
        Assert.Null(colors[^2]);
    }

    /// <summary>Reads the text back from RTF: escapes undone, groups and control words dropped.</summary>
    internal static string DecodeRtf(string rtf)
    {
        string body = rtf[(rtf.IndexOf("\\chcbpat1\\cf2 ", StringComparison.Ordinal) + "\\chcbpat1\\cf2 ".Length)..^1];
        var sb = new StringBuilder();
        for (int i = 0; i < body.Length;)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                char n = body[i + 1];
                if (n is '\\' or '{' or '}') { sb.Append(n); i += 2; continue; }
                int j = i + 1;
                while (j < body.Length && char.IsAsciiLetter(body[j])) j++;
                string word = body[(i + 1)..j];
                int k = j;
                if (k < body.Length && (body[k] == '-' || char.IsAsciiDigit(body[k]))) k++;
                while (k < body.Length && char.IsAsciiDigit(body[k])) k++;
                string number = body[j..k];
                if (k < body.Length && body[k] == ' ') k++;
                if (word == "u") { sb.Append((char)short.Parse(number, CultureInfo.InvariantCulture)); k++; } // the fallback character after \u
                else if (word == "par") sb.Append('\n');
                else if (word == "tab") sb.Append('\t');
                i = k;
            }
            else
            {
                if (c is not ('{' or '}' or '\r' or '\n')) sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    [Fact]
    public void The_rtf_has_a_color_table_and_escapes_backslash_braces_and_non_ascii()
    {
        string text = "path C:\\dir {x} \"Příliš žluťoučký kůň\" \U0001F600\ttab\r\nnext\n";
        var theme = Theme();
        string rtf = HighlightExport.Rtf(text, LanguageModes.Tokenizer("csharp"), null, theme);
        Assert.StartsWith("{\\rtf1\\ansi", rtf);
        Assert.True(rtf.All(c => c < 128), "RTF must be ASCII");
        Assert.Contains("\\u345?", rtf); // ř
        Assert.Contains("\\u-10179?\\u-8704?", rtf); // the emoji as a surrogate pair
        Assert.Equal(text.Replace("\r\n", "\n"), DecodeRtf(rtf));

        // groups are balanced once the escaped braces are removed
        string bare = Regex.Replace(rtf, @"\\[\\{}]", "");
        Assert.Equal(bare.Count(c => c == '{'), bare.Count(c => c == '}'));

        string table = Regex.Match(rtf, @"\{\\colortbl;(.*?)\}").Groups[1].Value;
        int colors = Regex.Matches(table, @"\\red\d+\\green\d+\\blue\d+;").Count;
        Assert.True(colors >= 3, "page, text and at least one token color");
        foreach (Match m in Regex.Matches(rtf, @"\\cf(\d+) ")) Assert.InRange(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), 1, colors);
    }

    [Fact]
    public void The_rtf_colors_follow_the_palette_of_the_theme()
    {
        string rtf = HighlightExport.Rtf("// c\n", LanguageModes.Tokenizer("csharp"), null, Theme(dark: true));
        var comment = ((ISolidColorBrush)SyntaxPalette.Dark.GetBrush("comment")!).Color;
        Assert.Contains($"\\red{comment.R}\\green{comment.G}\\blue{comment.B};", rtf);
        Assert.Contains("\\red0\\green0\\blue0;\\red255\\green255\\blue255;", rtf); // page, then text
    }

}

[Collection("LocState")]
public class HighlightExportWindowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HighlightExportWindowTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private async Task<string> ExportToFile(DocTab tab, MainWindow.ExportForm form)
    {
        string path = Path.Combine(_temp.Path, "out." + (form == MainWindow.ExportForm.Html ? "html" : "rtf"));
        _dialogs.SaveFiles.Enqueue(path);
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _w.ExportHighlighted(form));
        ToolTestKit.WaitFor(() => File.Exists(path) && _w.StatusMessageText == Loc.Get(K.ExportDone, ("name", Path.GetFileName(path))), "the export did not finish", 6000);
        return await File.ReadAllTextAsync(path);
    }

    [AvaloniaFact]
    public async Task Exporting_an_open_file_writes_the_colors_of_the_editor()
    {
        var tab = _w.OpenFile(_temp.File("a.cs", CSharp), null);
        _w.UpdateLayout();
        string html = await ExportToFile(tab, MainWindow.ExportForm.Html);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Equal(EditorColors(CSharp, "csharp", SyntaxPalette.Light), HtmlColors(html));
        Assert.Equal("a.html", _dialogs.SaveFileNames[0]);

        string rtf = await ExportToFile(tab, MainWindow.ExportForm.Rtf);
        Assert.Equal(CSharp, DecodeRtf(rtf));
    }

    [AvaloniaFact]
    public async Task Exporting_a_selection_writes_only_the_selected_text()
    {
        var tab = _w.OpenFile(_temp.File("b.cs", CSharp), null);
        _w.UpdateLayout();
        int from = CSharp.IndexOf("using", StringComparison.Ordinal);
        tab.Editor!.Select(from, "using System;".Length);
        string html = await ExportToFile(tab, MainWindow.ExportForm.Html);
        Assert.Equal(EditorColors(CSharp, "csharp", SyntaxPalette.Light)[from..(from + 13)], HtmlColors(html));
    }

    [AvaloniaFact]
    public void A_paged_file_is_exported_by_its_selection_only_and_without_one_a_message_says_so()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.File("big.txt", string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n"))), null); // over the 1 MiB threshold
        Assert.True(tab.Doc.IsLarge);
        _dialogs.SaveFiles.Enqueue(Path.Combine(_temp.Path, "no.html"));
        _w.ExportHighlighted(MainWindow.ExportForm.Html);
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.ExportNothing), "no message");
        Assert.False(File.Exists(Path.Combine(_temp.Path, "no.html")));
    }

    private DocTab OpenPagedCs(string text, string name = "p.cs")
    {
        // the setting files.largeFileThreshold cannot go below 1 MiB, so a small text is opened in the paged backend directly
        var tab = _w.AddRecovered(OpenDocument.Open(_temp.Bytes(name, Encoding.UTF8.GetBytes(text)), 0), _w.ActiveGroup);
        Assert.True(tab.Doc.IsLarge);
        _w.SelectTab(tab);
        _w.UpdateLayout();
        ToolTestKit.WaitFor(() => tab.Paged!.Index.Complete, "the line index did not finish");
        return tab;
    }

    private static (long Start, long End) Bytes(string text, int from, int length) =>
        (Encoding.UTF8.GetByteCount(text.AsSpan(0, from)), Encoding.UTF8.GetByteCount(text.AsSpan(0, from + length)));

    [AvaloniaTheory]
    [InlineData("line */", 7)] // inside a block comment that opens on an earlier line
    [InlineData("line */ void M() { }", 20)] // the comment ends inside the selection
    [InlineData("y&z\\n\"; int n", 13)] // starts in the middle of a string: the quote that follows closes it
    public async Task A_selection_in_a_large_file_is_colored_as_the_whole_document(string selected, int length)
    {
        var tab = OpenPagedCs(CSharp);
        int from = CSharp.IndexOf(selected, StringComparison.Ordinal);
        Assert.True(from >= 0);
        var (start, end) = Bytes(CSharp, from, length);
        tab.Paged!.SelectBytes(start, end);
        string html = await ExportToFile(tab, MainWindow.ExportForm.Html);
        Assert.Equal(EditorColors(CSharp, "csharp", SyntaxPalette.Light)[from..(from + length)], HtmlColors(html)); // as many characters as selected: the head is not exported
    }

    [AvaloniaFact]
    public async Task The_start_state_of_a_line_is_the_state_the_whole_document_leaves_before_it()
    {
        var tokenizer = LanguageModes.Tokenizer("csharp");
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(CSharp), PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        string[] lines = CSharp.TrimEnd('\n').Split('\n'); // the empty line after the last line break is no line a session reports
        var expected = tokenizer.InitialState;
        for (int line = 0; line < lines.Length; line++)
        {
            Assert.Same(expected, await SyntaxFeeder.StartStateOfLineAsync(doc, tokenizer, line));
            expected = tokenizer.TokenizeLine(lines[line], expected, []);
        }
        Assert.Null(await SyntaxFeeder.StartStateOfLineAsync(doc, tokenizer, 1000)); // past the end: no state to give
    }

    /// <summary>Calls the function with each value it is given, at once and on the thread that reports (what <see cref="Progress{T}"/> does later).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    /// <summary>C# of about the given size, one short statement per line; <paramref name="lines"/> is how many lines it has.</summary>
    private static string FarCSharp(int megabytes, out int lines)
    {
        var sb = new StringBuilder();
        int i = 0;
        for (; sb.Length < megabytes * 1_000_000; i++) sb.Append("int value").Append(i).Append(" = ").Append(i).Append("; // line ").Append(i).Append('\n');
        lines = i;
        return sb.ToString();
    }

    [AvaloniaFact]
    public async Task Reading_the_state_of_a_far_line_reports_how_far_it_is_and_ends_between_two_slices_when_it_is_cancelled()
    {
        var tokenizer = LanguageModes.Tokenizer("csharp");
        using var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(FarCSharp(12, out int lines)), PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        using var cts = new CancellationTokenSource();
        var first = new List<long>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SyntaxFeeder.StartStateOfLineAsync(doc, tokenizer, lines - 5, cts.Token, new SyncProgress<long>(scanned => { first.Add(scanned); cts.Cancel(); })));

        Assert.Single(first); // it ended at the next check, not at the end of the file
        Assert.True(first[0] > 0);

        // a read that nobody cancels reports growing numbers and gives the state
        var all = new List<long>();
        var state = await SyntaxFeeder.StartStateOfLineAsync(doc, tokenizer, lines - 5, default, new SyncProgress<long>(all.Add));
        Assert.NotNull(state);
        Assert.True(all.Count > 1);
        Assert.Equal(all.Order(), all);
    }

    [AvaloniaFact]
    public void Esc_ends_the_reading_of_the_colors_of_a_large_file_for_an_export_and_nothing_is_asked_or_written()
    {
        string text = FarCSharp(12, out _);
        var tab = OpenPagedCs(text, "far.cs");
        int start = text.LastIndexOf("int value", StringComparison.Ordinal); // the text is ASCII: characters are bytes
        tab.Paged!.SelectBytes(start, start + 9);
        string path = Path.Combine(_temp.Path, "far.html");
        _dialogs.SaveFiles.Enqueue(path);

        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _w.ExportHighlighted(MainWindow.ExportForm.Html));
        ToolTestKit.WaitFor(() => _w.ExportReading, "the colors were not being read");
        Assert.Contains("%", _w.StatusMessageText);
        TestApp.Press(_w, Avalonia.Input.Key.Escape);
        ToolTestKit.WaitFor(() => !_w.ExportReading && _w.StatusMessageText == Loc.Get(K.ExportCancelled), "the reading did not end", 6000);

        Assert.False(File.Exists(path));
        Assert.Empty(_dialogs.SaveFileNames);
    }

    [AvaloniaFact]
    public void A_large_document_is_colored_off_the_UI_thread_and_the_command_returns_at_once()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(100_000_000));
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 2_000_000; i++) sb.Append("int value").Append(i).Append(" = ").Append(i).Append("; // line ").Append(i).Append('\n');
        var tab = _w.OpenFile(_temp.File("large.cs", sb.ToString()), null);
        Assert.False(tab.Doc.IsLarge);
        _w.UpdateLayout();
        var watch = Stopwatch.StartNew();
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _w.CopyHighlighted(htmlSource: true));
        long returned = watch.ElapsedMilliseconds;
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.ExportCopied), "the copy did not finish", 12000);
        Assert.True(returned < 500, $"the command blocked the UI thread for {returned} ms");
    }

    [AvaloniaFact]
    public void Copy_with_formatting_offers_html_and_rtf_next_to_the_text()
    {
        var tab = _w.OpenFile(_temp.File("c.cs", CSharp), null);
        _w.UpdateLayout();
        _w.CopyHighlighted(htmlSource: false);
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.ExportCopied), "the copy did not finish");
        Assert.Equal(CSharp, _app.ClipboardHistory.Items[0]);
        Assert.Contains("HTML", ClipboardHtml.Format.Identifier, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(ClipboardHtml.Format.Identifier, ClipboardRtf.Format.Identifier);
        Assert.Equal("{\\rtf1", Encoding.ASCII.GetString(ClipboardRtf.Encode("{\\rtf1 x}"), 0, 6));
    }
}

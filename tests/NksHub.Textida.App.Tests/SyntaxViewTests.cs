using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>Syntax colors on both surfaces: the editor, the paged view, the language choice and the catch-up after a jump.</summary>
[Collection("LocState")]
public class SyntaxViewTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _temp = new();
    private App? _app;
    private MainWindow? _w;

    static SyntaxViewTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private MainWindow Window(bool paged = false)
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        if (paged) _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        return _w;
    }

    public void Dispose()
    {
        if (_app is not null)
        {
            _app.RequestedThemeVariant = ThemeVariant.Default;
            TestApp.Cleanup(_app);
        }
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private static HashSet<Color> Colors(DocTab tab, int line)
    {
        tab.Editor!.TextArea.TextView.EnsureVisualLines();
        return tab.Editor.TextArea.TextView.GetVisualLine(line)!.Elements
            .Select(e => e.TextRunProperties.ForegroundBrush).OfType<ISolidColorBrush>().Select(b => b.Color).ToHashSet();
    }

    [AvaloniaFact]
    public void The_editor_colors_a_file_by_its_extension_and_a_manual_choice_survives_reload()
    {
        var w = Window();
        var path = _temp.File("a.txt", "{\"a\": \"b\", \"n\": 12}\n");
        var tab = w.OpenFile(path, null);
        w.UpdateLayout();
        Settle();
        Assert.DoesNotContain(SyntaxPalette.Light.GetBrush("string") is ISolidColorBrush s ? s.Color : default, Colors(tab, 1));

        w.SetLanguage(tab, LanguageModes.ById("json"));
        w.UpdateLayout();
        Settle();
        var light = (ISolidColorBrush)SyntaxPalette.Light.GetBrush("string")!;
        var dark = (ISolidColorBrush)SyntaxPalette.Dark.GetBrush("string")!;
        Assert.Contains(Colors(tab, 1), c => c == light.Color || c == dark.Color);

        Assert.True(w.ReloadAsync(tab).GetAwaiter().GetResult());
        w.UpdateLayout();
        Settle();
        Assert.Equal("json", tab.Doc.Language.Id);
        Assert.Contains(Colors(tab, 1), c => c == light.Color || c == dark.Color);
    }

    [AvaloniaTheory]
    [InlineData("services.neon", "# comment\n", "neon")]
    [InlineData("layout.latte", "{* comment *}\n", "latte")]
    [InlineData("Index.cshtml", "@* comment *@\n", "razor")]
    [InlineData("App.sln", "# comment\n", "sln")]
    public void Neon_latte_razor_and_solution_files_are_colored_by_their_extension(string name, string text, string language)
    {
        var w = Window();
        var tab = w.OpenFile(_temp.File(name, text), null);
        w.UpdateLayout();
        Settle();
        Assert.Equal(language, tab.Doc.Language.Id);
        var light = (ISolidColorBrush)SyntaxPalette.Light.GetBrush("comment")!;
        var dark = (ISolidColorBrush)SyntaxPalette.Dark.GetBrush("comment")!;
        Assert.Contains(Colors(tab, 1), c => c == light.Color || c == dark.Color);
    }

    private const string PhpConsole = "<?php\n\nrequire __DIR__ . '/app/bootstrap.php';\n\nApp\\Bootstrap::boot()\n    ->createContainer()\n    ->getByType(Contributte\\Console\\Application::class)\n    ->run();\n";

    /// <summary>
    /// Each character on screen has the color of the token the grammar gives it: read from the visual lines the editor paints after the window
    /// opened the file, not from the tokenizer (P3.2.4: a PHP file was colored only in its first two columns).
    /// </summary>
    [AvaloniaTheory]
    [InlineData("console.php", PhpConsole, "Dark")]
    [InlineData("console.php", PhpConsole, "Light")]
    [InlineData("a.cs", "using System;\n\nnamespace App;\n\npublic sealed class Program\n{\n    public static string Name { get; } = \"some text\"; // a note\n}\n", "Dark")]
    [InlineData("a.js", "import { readFile } from 'node:fs';\n\nexport function load(path) {\n    return readFile(path, \"utf8\"); // a note\n}\n", "Light")]
    [InlineData("a.json", "{\n  \"name\": \"some text\",\n  \"count\": 12,\n  \"enabled\": true\n}\n", "Dark")]
    [InlineData("a.md", "# A title\n\nSome **bold** words and `code` after them.\n", "Light")]
    public async Task Every_character_on_screen_has_the_color_of_its_token(string name, string text, string theme)
    {
        var w = Window();
        _app!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var palette = theme == "Dark" ? SyntaxPalette.Dark : SyntaxPalette.Light;
        var tab = w.OpenFile(_temp.File(name, text), null);
        var tokenizer = LanguageModes.Tokenizer(LanguageModes.GrammarId(tab.Doc)!);
        var lines = text.TrimEnd('\n').Split('\n');
        var expected = new List<(int Line, int Column, Color Color)>();
        var state = tokenizer.InitialState;
        for (int i = 0; i < lines.Length; i++)
        {
            var tokens = new List<Token>();
            state = tokenizer.TokenizeLine(lines[i], state, tokens);
            foreach (var token in tokens)
                if (palette.GetBrush(token.Kind) is ISolidColorBrush brush)
                    for (int c = token.Start; c < token.End; c++) expected.Add((i, c, brush.Color));
        }
        Assert.True(expected.Count > 10, "the grammar colors almost nothing");

        List<string> wrong = [];
        for (int round = 0; round < 100; round++)
        {
            await Task.Delay(20); // the dispatcher runs meanwhile: layout, the feeder's slices and its timers
            var view = tab.Editor!.TextArea.TextView;
            if (!view.VisualLinesValid) continue;
            wrong = [.. expected.Where(e => PaintedAt(view, e.Line, e.Column) != e.Color).Select(e => $"{e.Line + 1}:{e.Column + 1} '{lines[e.Line][e.Column]}' {PaintedAt(view, e.Line, e.Column)} != {e.Color}")];
            if (wrong.Count == 0) break;
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {expected.Count} characters wrong (window {tab.Syntax!.Requested}, width {tab.Editor!.TextArea.TextView.Bounds.Width}), first: {string.Join("; ", wrong.Take(12))}");
    }

    private static Color? PaintedAt(AvaloniaEdit.Rendering.TextView view, int line, int column) =>
        view.GetVisualLine(line + 1)?.Elements.FirstOrDefault(e => e.RelativeTextOffset <= column && column < e.RelativeTextOffset + e.DocumentLength)
            ?.TextRunProperties.ForegroundBrush is ISolidColorBrush b ? b.Color : null;

    [AvaloniaFact]
    public void The_editor_colors_the_columns_it_is_scrolled_to()
    {
        var w = Window();
        var tab = w.OpenFile(_temp.File("h.cs", new string(' ', 100000) + "var x=1;\n"), null);
        w.UpdateLayout();
        Settle();
        var view = tab.Editor!.TextArea.TextView;
        ((Avalonia.Controls.Primitives.ILogicalScrollable)view).Offset = new Avalonia.Vector(100000 * view.WideSpaceWidth, 0);
        w.UpdateLayout();
        Settle();
        view.EnsureVisualLines();
        var element = view.GetVisualLine(1)!.Elements.First(e => e.RelativeTextOffset <= 100000 && 100000 < e.RelativeTextOffset + e.DocumentLength);
        var color = ((ISolidColorBrush)element.TextRunProperties.ForegroundBrush!).Color;
        Assert.True(new[] { ((ISolidColorBrush)SyntaxPalette.Light.GetBrush("keyword")!).Color, ((ISolidColorBrush)SyntaxPalette.Dark.GetBrush("keyword")!).Color }.Contains(color), $"color {color} offset {view.ScrollOffset.X} cell {view.WideSpaceWidth} width {view.Bounds.Width}");
    }

    [AvaloniaFact]
    public void The_paged_view_colors_the_columns_it_is_scrolled_to()
    {
        var w = Window(paged: true);
        var text = new string(' ', 12000) + "var x=1;\n" + string.Concat(Enumerable.Repeat("var y = 2; // filler line\n", 45_000));
        var tab = w.OpenFile(_temp.File("h.cs", text), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged!.Focus();
        TestApp.Press(w, Avalonia.Input.Key.End);
        for (int i = 0; i < 100 && !(tab.Paged.TokensOf(0)?.Any(t => t.Start >= 12000 && t.Kind == "keyword") ?? false); i++) { Thread.Sleep(20); w.UpdateLayout(); Settle(); }
        Assert.Contains(tab.Paged.TokensOf(0)!, t => t.Start >= 12000 && t.Kind == "keyword");
    }

    [AvaloniaFact]
    public void A_file_changed_behind_the_paged_view_drops_the_colors_and_leaves_the_check_to_the_document()
    {
        var w = Window(paged: true);
        var path = _temp.File("x.js", string.Concat(Enumerable.Repeat("var y = 2; // filler line\n", 45_000)));
        var tab = w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        for (int i = 0; i < 100 && tab.Paged!.TokensOf(0) is null; i++) { Thread.Sleep(20); w.UpdateLayout(); Settle(); }
        Assert.NotNull(tab.Paged!.TokensOf(0));
        bool reported = false;
        tab.Doc.ExternalChangeDetected += _ => reported = true;

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Position = 4;
            fs.WriteByte((byte)'Z');
        }
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        tab.Paged.GoToLine(30_000);
        for (int i = 0; i < 100 && !(reported && tab.Paged.TokensOf(0) is null); i++) { Thread.Sleep(20); w.UpdateLayout(); Settle(); }
        Assert.Null(tab.Paged.TokensOf(0));
        Assert.True(reported);
    }

    [AvaloniaFact]
    public void A_line_beyond_the_full_scan_limit_is_colored_by_the_streaming_scan()
    {
        var w = Window(paged: true);
        var tab = w.OpenFile(_temp.File("long.js", string.Concat(Enumerable.Repeat("var a = 1; ", 120_000)) + "\nvar b = 2;\n"), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !(tab.Paged!.TokensOf(1)?.Any(t => t.Kind == "keyword") ?? false); i++) { Thread.Sleep(25); w.UpdateLayout(); Settle(); }
        Assert.Contains(tab.Paged!.TokensOf(0) ?? [], t => t.Kind == "keyword");
        Assert.Contains(tab.Paged.TokensOf(1) ?? [], t => t.Kind == "keyword");
        Assert.False(tab.Syntax!.LineTooLong);
        Assert.False(tab.Syntax.Pending);
    }

    [AvaloniaFact]
    public void The_paged_view_colors_the_visible_lines()
    {
        var w = Window(paged: true);
        var tab = w.OpenFile(_temp.File("p.json", string.Concat(Enumerable.Repeat("{\"a\": \"b\"}\n", 110_000))), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 100 && tab.Paged!.TokensOf(0) is null; i++) { Thread.Sleep(20); w.UpdateLayout(); Settle(); }
        Assert.Contains(tab.Paged!.TokensOf(0)!, t => t.Kind.StartsWith("string", StringComparison.Ordinal));
        Assert.False(tab.Syntax.Pending);
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void A_jump_past_an_eight_mebibyte_comment_is_colored_within_250_ms()
    {
        var w = Window(paged: true);
        int rows = (8 << 20) / 1025;
        var text = "/*" + string.Concat(Enumerable.Repeat(new string('x', 1024) + "\n", rows)) + "*/\nvar after = 1;\n";
        var tab = w.OpenFile(_temp.Bytes("c.js", Encoding.UTF8.GetBytes(text)), null);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        Assert.True(tab.Paged!.Index.Complete);
        Thread.Sleep(400); // the scroll range follows the index on a timer
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 50&& tab.Paged.TokensOf(0) is null; i++) { Thread.Sleep(20); Dispatcher.UIThread.RunJobs(); }
        var timer = Stopwatch.StartNew();
        tab.Paged.GoToLine(rows + 2);
        while (timer.ElapsedMilliseconds < 5000 && tab.Paged.TokensOf(rows + 1) is null) Dispatcher.UIThread.RunJobs();
        double ms = timer.Elapsed.TotalMilliseconds;
        output.WriteLine($"jump-catch-up-ms={ms:F1} pending={tab.Syntax.Pending} line0={tab.Paged.TokensOf(0)?.Count} top={tab.Paged.TopLine} lines={tab.Paged.Index.Lines} rows={rows} topTokens={tab.Paged.TokensOf(tab.Paged.TopLine)?.Count} lastTokens={tab.Paged.TokensOf(tab.Paged.Index.Lines-1)?.Count}");
        Assert.Contains(tab.Paged.TokensOf(rows + 1) ?? [], t => t.Kind == "keyword");
        Assert.True(ms <= 250, $"catch-up took {ms:F1} ms");
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Typing_in_a_highlighted_one_mebibyte_file_stays_within_33_ms_at_p95()
    {
        var w = Window();
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("    var value").Append(i).Append(" = \"text\"; // note ").Append(i).Append('\n');
        var tab = w.OpenFile(_temp.File("one-mib.cs", sb.ToString()), null);
        Settle();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = tab.Editor.Document.GetOffset(2000, 1);
        w.UpdateLayout();
        Settle();
        for (int i = 0; i < 30; i++) { TestApp.Type(w, "w"); Dispatcher.UIThread.RunJobs(); }
        Assert.Contains(Colors(tab, 2000), c => c == ((ISolidColorBrush)SyntaxPalette.Light.GetBrush("string")!).Color || c == ((ISolidColorBrush)SyntaxPalette.Dark.GetBrush("string")!).Color);

        var rounds = new List<string>();
        double p95 = double.MaxValue;
        for (int round = 0; round < 3 && p95 > 33; round++)
        {
            var times = new List<double>();
            for (int i = 0; i < 300; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                TestApp.Type(w, "x");
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                times.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
            }
            times.Sort();
            p95 = times[(int)(times.Count * 0.95)];
            rounds.Add($"p95 {p95:0.0} ms, median {times[times.Count / 2]:0.0} ms");
        }
        output.WriteLine(string.Join("; ", rounds));
        Assert.True(p95 <= 33, "typing " + string.Join("; ", rounds));
    }
}

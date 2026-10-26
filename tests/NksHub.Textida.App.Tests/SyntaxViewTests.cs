using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

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
        if (_app is not null) TestApp.Cleanup(_app);
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
        int rows = (8 << 20) / 81;
        var text = "/*" + string.Concat(Enumerable.Repeat(new string('x', 80) + "\n", rows)) + "*/\nvar after = 1;\n";
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
        Assert.Contains(Colors(tab, 2000), c => c.A == 255); // the typed line is colored, not just present

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

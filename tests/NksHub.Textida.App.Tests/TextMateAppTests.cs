using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.5.3: an imported TextMate grammar becomes a language that colours a file by its extension, and the warnings are listed.</summary>
[Collection("LocState")]
public class TextMateAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly ITestOutputHelper _output;

    public TextMateAppTests(ITestOutputHelper output)
    {
        _output = output;
        _app = TestApp.Prepare(_temp, _dialogs);
    }

    public void Dispose()
    {
        LanguageModes.UseUserGrammars(new GrammarRegistry(BuiltinGrammars.Registry));
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task An_imported_grammar_colours_files_of_its_extension_and_its_warnings_are_in_the_tasks_tab()
    {
        string grammar = _temp.File("toy.tmLanguage.json", """
            { "name": "Toy", "scopeName": "source.toy", "fileTypes": ["toy"],
              "patterns": [ { "match": "\\b(if|else)\\b", "name": "keyword.control.toy" }, { "begin": "^```", "while": "^>", "name": "markup.raw.toy" } ] }
            """);
        var w = _app.CreateWindow(null);

        await w.ImportTextMateAsync(grammar);

        Assert.Equal("toy", LanguageModes.ForPath("a.toy").Id);
        var tab = w.OpenFile(_temp.File("a.toy", "if x else y\n"), null);
        Assert.Equal("toy", tab.Doc.Language.Id);
        var lines = _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Contains(lines, l => l.Contains("toy"));
        Assert.Contains(lines, l => l.Contains("while"));

        await w.ImportTextMateAsync(_temp.File("broken.json", "{ nope"));
        Assert.Contains("broken.json", w.StatusMessageText);
    }

    /// <summary>The typing gate of <c>SyntaxViewTests</c> with a converted grammar: an include, captures split into one rule per group, regions with inner rules.</summary>
    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public async Task Typing_in_a_one_mebibyte_file_coloured_by_an_imported_grammar_stays_within_33_ms_at_p95()
    {
        string grammar = _temp.File("toy.tmLanguage.json", """
            { "name": "Toy", "scopeName": "source.toy", "fileTypes": ["toy"],
              "patterns": [
                { "include": "#comments" },
                { "match": "\\b(if|else|return)\\b", "name": "keyword.control.toy" },
                { "match": "\\b(def)\\s+([[:alpha:]_]\\w*)", "captures": { "1": { "name": "keyword.control.toy" }, "2": { "name": "entity.name.function.toy" } } },
                { "match": "\\b\\d++\\b", "name": "constant.numeric.toy" },
                { "begin": "\"", "end": "\"", "name": "string.quoted.double.toy", "patterns": [ { "match": "\\\\.", "name": "constant.character.escape.toy" } ] },
                { "begin": "/\\*", "end": "\\*/", "name": "comment.block.toy" }
              ],
              "repository": { "comments": { "patterns": [ { "match": "#.*$", "name": "comment.line.number-sign.toy" } ] } } }
            """);
        var w = _app.CreateWindow(null);
        await w.ImportTextMateAsync(grammar);
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("def run").Append(i).Append(" if x return 12 \"text\\n\" /* c */ # note ").Append(i).Append('\n');
        var tab = w.OpenFile(_temp.File("one-mib.toy", sb.ToString()), null);
        Assert.Equal("toy", tab.Doc.Language.Id);
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = tab.Editor.Document.GetOffset(2000, 1);
        w.UpdateLayout();
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 30; i++) { TestApp.Type(w, "w"); Dispatcher.UIThread.RunJobs(); } // warm up
        tab.Editor.TextArea.TextView.EnsureVisualLines();
        var colours = tab.Editor.TextArea.TextView.GetVisualLine(2001)!.Elements.Select(e => e.TextRunProperties.ForegroundBrush).OfType<ISolidColorBrush>().Select(b => b.Color).ToHashSet();
        Assert.Contains(colours, c => c == ((ISolidColorBrush)SyntaxPalette.Light.GetBrush("keyword")!).Color || c == ((ISolidColorBrush)SyntaxPalette.Dark.GetBrush("keyword")!).Color);

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
        _output.WriteLine(string.Join("; ", rounds));
        Assert.True(p95 <= 33, "typing " + string.Join("; ", rounds));
    }
}

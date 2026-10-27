using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.5.2 through the running window and the Emmet plugin process: Tab expands, Tab elsewhere indents as before, the preview and the wrapping change the text in one undo step.</summary>
[Collection("LocState")]
public class EmmetAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public EmmetAppTests()
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

    private DocTab Open(string name, string content, int? caret = null)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = caret ?? tab.Editor.Document.TextLength;
        return tab;
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private static string Unit(DocTab t) => t.Editor!.Settings.Indent.Unit;

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private async Task TabAsync()
    {
        var run = _app.Workbench.Commands.ExecuteAsync("nks.edit.tab", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, "the Tab command");
        Assert.Equal(CommandOutcome.Handled, await run);
    }

    private async Task CommandAsync(string id)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, id);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");
    }

    [AvaloniaFact]
    public async Task Tab_after_ul_li_times_3_makes_a_list_with_a_caret_in_every_item_and_one_undo_step()
    {
        var tab = Open("a.html", "<body>\n  ul>li*3");
        var depth = tab.Doc.Doc.UndoDepth;
        string u = Unit(tab);

        await TabAsync();

        Assert.Equal($"<body>\n  <ul>\n  {u}<li></li>\n  {u}<li></li>\n  {u}<li></li>\n  </ul>", Text(tab));
        Assert.Equal(3, tab.Editor!.CaretCount);
        Assert.All(tab.Editor.GetSelections(), s => Assert.True(Text(tab)[..s.Caret].EndsWith("<li>") && Text(tab)[s.Caret..].StartsWith("</li>")));
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        Assert.True(_app.Plugins.Documents.HasTabProviders);
        Assert.False(_app.Plugins.Documents.IsExposed(tab.Doc.Doc.Id));

        _w.Undo();
        Assert.Equal("<body>\n  ul>li*3", Text(tab));
    }

    [AvaloniaFact]
    public async Task Tab_expands_the_abbreviation_before_every_caret_at_once()
    {
        var tab = Open("m.html", "p\nul>li\n", 0);
        tab.Editor!.SetSelections([Sel.At(1), Sel.At(7)]);
        var depth = tab.Doc.Doc.UndoDepth;

        await TabAsync();

        Assert.Equal("<p></p>\n<ul>\n" + Unit(tab) + "<li></li>\n</ul>\n", Text(tab));
        Assert.Equal(2, tab.Editor.CaretCount);
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public async Task Tab_that_is_not_an_abbreviation_indents_as_it_always_did()
    {
        var baseline = Open("plain.txt", "hello world");
        await TabAsync();
        string indented = Text(baseline);
        Assert.NotEqual("hello world", indented);
        Assert.False(_app.Plugins.Documents.HasTabProviders); // a text file never starts the plugin

        foreach (var (content, caret) in new[] { ("hello world", 11), ("<div cla", 8), ("ul>", 3), ("  ", 2) })
        {
            var reference = Open("r" + content.Length + ".txt", content, caret);
            await TabAsync();
            var tab = Open("t" + content.Length + ".html", content, caret);
            await TabAsync();
            Assert.Equal(Text(reference), Text(tab));
        }
        Assert.True(_app.Plugins.Documents.HasTabProviders);
    }

    [AvaloniaFact]
    public async Task A_selection_is_indented_by_Tab_and_not_expanded()
    {
        var html = Open("s.html", "ul>li*3\nx", 0);
        html.Editor!.Select(0, 7);
        await TabAsync();
        var indentedHtml = Text(html);
        var txt = Open("s.txt", "ul>li*3\nx", 0);
        txt.Editor!.Select(0, 7);
        await TabAsync();
        Assert.Equal(Text(txt), indentedHtml);
        Assert.DoesNotContain("<ul>", indentedHtml);
    }

    [AvaloniaFact]
    public async Task Css_abbreviations_expand_inside_a_block_only()
    {
        var inside = Open("a.css", "a {\n  m10-20", 12);
        await TabAsync();
        Assert.Equal("a {\n  margin: 10px 20px;", Text(inside));
        Assert.Equal(Text(inside).Length, inside.Editor!.CaretOffset);

        var reference = Open("b.txt", "a { }\nm10");
        await TabAsync();
        var outside = Open("b.css", "a { }\nm10");
        await TabAsync();
        Assert.Equal(Text(reference), Text(outside));
        Assert.DoesNotContain("margin", Text(outside));
    }

    [AvaloniaFact]
    public async Task The_preview_shows_the_result_and_expands_only_when_confirmed()
    {
        var tab = Open("p.html", "ul>li*2");
        _dialogs.Answers.Enqueue(1);
        await CommandAsync("nks.tools.emmetPreview");
        Assert.Equal("ul>li*2", Text(tab));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains("ul>li*2", asked.Message);
        Assert.Contains("<li>|</li>", asked.Message);

        var depth = tab.Doc.Doc.UndoDepth;
        _dialogs.Answers.Enqueue(0);
        await CommandAsync("nks.tools.emmetPreview");
        // a file with no line break gets the line break of the platform
        Assert.Equal($"<ul>\n{Unit(tab)}<li></li>\n{Unit(tab)}<li></li>\n</ul>", Text(tab).Replace("\r\n", "\n"));
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        _w.Undo();
        Assert.Equal("ul>li*2", Text(tab));
    }

    [AvaloniaFact]
    public async Task Wrapping_puts_every_selection_into_the_abbreviation_as_one_undo_step()
    {
        var tab = Open("w.html", "one\ntwo");
        tab.Editor!.SelectAll();
        _dialogs.Prompts.Enqueue("ul>li*");
        var depth = tab.Doc.Doc.UndoDepth;

        await CommandAsync("nks.tools.emmetWrap");

        Assert.Equal($"<ul>\n{Unit(tab)}<li>one</li>\n{Unit(tab)}<li>two</li>\n</ul>", Text(tab));
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        _w.Undo();
        Assert.Equal("one\ntwo", Text(tab));

        var many = Open("w2.html", "ab cd");
        many.Editor!.SetSelections([new Sel(0, 2), new Sel(3, 5)]);
        _dialogs.Prompts.Enqueue("b");
        depth = many.Doc.Doc.UndoDepth;
        await CommandAsync("nks.tools.emmetWrap");
        Assert.Equal("<b>ab</b> <b>cd</b>", Text(many));
        Assert.Equal(depth + 1, many.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public async Task Wrapping_with_a_wrong_abbreviation_or_nothing_selected_changes_nothing()
    {
        var tab = Open("n.html", "text", 0);
        await CommandAsync("nks.tools.emmetWrap"); // nothing selected
        Assert.Equal("text", Text(tab));

        tab.Editor!.SelectAll();
        _dialogs.Prompts.Enqueue("br");
        await CommandAsync("nks.tools.emmetWrap"); // a void element would lose the text
        Assert.Equal("text", Text(tab));

        _dialogs.Prompts.Enqueue(null);
        await CommandAsync("nks.tools.emmetWrap");
        Assert.Equal("text", Text(tab));
    }
}

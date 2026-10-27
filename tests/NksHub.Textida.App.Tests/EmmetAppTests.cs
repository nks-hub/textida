using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
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

    private readonly TimeSpan _tabTimeout = PluginLimitsPlaceholders.TabTimeout;

    public EmmetAppTests()
    {
        // Tab waits for the plugin process; a busy machine can miss 500 ms (the timeout path has its own test in DocumentApiTests.EarlyTools)
        PluginLimitsPlaceholders.TabTimeout = TimeSpan.FromSeconds(10);
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        PluginLimitsPlaceholders.TabTimeout = _tabTimeout;
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

    /// <summary>Where the carets of the walk through the items stand: inside every <c>&lt;li&gt;</c>.</summary>
    private static List<int> ItemCarets(DocTab t)
    {
        var text = Text(t);
        var carets = new List<int>();
        for (int at = text.IndexOf("<li>", StringComparison.Ordinal); at >= 0; at = text.IndexOf("<li>", at + 1, StringComparison.Ordinal)) carets.Add(at + "<li>".Length);
        return carets;
    }

    [AvaloniaFact]
    public async Task Tab_after_ul_li_times_3_makes_a_list_with_a_stop_in_every_item_and_one_undo_step()
    {
        var tab = Open("a.html", "<body>\n  ul>li*3");
        var depth = tab.Doc.Doc.UndoDepth;
        string u = Unit(tab);

        await TabAsync();

        Assert.Equal($"<body>\n  <ul>\n  {u}<li></li>\n  {u}<li></li>\n  {u}<li></li>\n  </ul>", Text(tab));
        Assert.True(tab.Editor!.InSnippet);
        Assert.Equal(1, tab.Editor.CaretCount);
        Assert.Equal(ItemCarets(tab)[0], tab.Editor.CaretOffset);
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        Assert.True(_app.Plugins.Documents.HasTabProviders);
        Assert.False(_app.Plugins.Documents.IsExposed(tab.Doc.Doc.Id));

        _w.Undo();
        Assert.Equal("<body>\n  ul>li*3", Text(tab));
        Assert.False(tab.Editor.InSnippet);
    }

    [AvaloniaFact]
    public async Task After_Undo_of_an_expansion_Tab_expands_the_abbreviation_again()
    {
        var tab = Open("u.html", "ul>li*2");
        await TabAsync();
        var expanded = Text(tab);

        _w.Undo();
        Assert.Equal("ul>li*2", Text(tab));
        await TabAsync();

        Assert.Equal(expanded, Text(tab));
        Assert.Equal(ItemCarets(tab)[0], tab.Editor!.CaretOffset);
    }

    [AvaloniaFact]
    public async Task Tab_walks_from_item_to_item_and_after_the_last_one_to_the_end_of_the_expansion()
    {
        var tab = Open("walk.html", "ul>li*3");
        await TabAsync();
        var items = ItemCarets(tab);
        var depth = tab.Doc.Doc.UndoDepth;

        for (int i = 1; i < items.Count; i++)
        {
            await TabAsync();
            Assert.Equal(items[i], tab.Editor!.CaretOffset);
            Assert.True(tab.Editor.InSnippet);
        }
        await TabAsync();

        Assert.False(tab.Editor!.InSnippet);
        Assert.Equal(Text(tab).Length, tab.Editor.CaretOffset);
        Assert.Equal(depth, tab.Doc.Doc.UndoDepth); // walking changes no text
    }

    [AvaloniaFact]
    public async Task Inside_the_session_the_Tab_key_jumps_and_a_tag_name_typed_at_a_stop_is_not_expanded()
    {
        var tab = Open("key.html", "ul>li*2");
        await TabAsync();

        TestApp.Type(_w, "p"); // a known tag name: outside a session Tab would turn it into <p></p>
        TestApp.Press(_w, Key.Tab); // the key itself, resolved by the `when` contexts of the key bindings

        Assert.Contains("<li>p</li>", Text(tab));
        Assert.DoesNotContain("<p></p>", Text(tab));
        Assert.Equal(ItemCarets(tab)[1], tab.Editor!.CaretOffset);
        Assert.True(tab.Editor.InSnippet);
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
    public async Task Expansions_with_a_different_number_of_places_are_walked_together()
    {
        var tab = Open("d.html", "p\nul>li*2\n", 0);
        tab.Editor!.SetSelections([Sel.At(1), Sel.At(9)]);

        await TabAsync();

        var text = Text(tab);
        Assert.Equal("<p></p>\n<ul>\n" + Unit(tab) + "<li></li>\n" + Unit(tab) + "<li></li>\n</ul>\n", text);
        Assert.Equal([text.IndexOf("<p>", StringComparison.Ordinal) + 3, ItemCarets(tab)[0]], tab.Editor.GetSelections().Select(s => s.Caret));

        await TabAsync(); // the paragraph has one place only: its caret waits behind it
        Assert.Equal([text.IndexOf("</p>", StringComparison.Ordinal) + 4, ItemCarets(tab)[1]], tab.Editor.GetSelections().Select(s => s.Caret));
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
    public async Task The_next_Tab_jumps_to_the_empty_attribute_and_the_one_after_the_last_place_ends_the_session()
    {
        var tab = Open("j.html", "a");
        await TabAsync();
        Assert.Equal("<a href=\"\"></a>", Text(tab));
        Assert.Equal("<a href=\"\">".Length, tab.Editor!.CaretOffset);
        Assert.True(tab.Editor.InSnippet);

        TestApp.Type(_w, "Home");
        var depth = tab.Doc.Doc.UndoDepth;
        await TabAsync();
        Assert.Equal("<a href=\"\">Home</a>", Text(tab));
        Assert.Equal("<a href=\"".Length, tab.Editor.CaretOffset);
        Assert.Equal(depth, tab.Doc.Doc.UndoDepth); // the jump changes no text

        TestApp.Type(_w, "x");
        await TabAsync();
        Assert.False(tab.Editor.InSnippet);
        Assert.Equal("<a href=\"x\">Home</a>", Text(tab));
        Assert.Equal(Text(tab).Length, tab.Editor.CaretOffset); // $0, the end of the expansion

        // outside a session Tab is the usual Tab again
        await TabAsync();
        var reference = Open("j.txt", "<a href=\"x\">Home</a>");
        await TabAsync();
        Assert.Equal(Text(reference), Text(tab));
    }

    [AvaloniaFact]
    public async Task A_caret_that_left_the_expansion_gets_the_usual_Tab()
    {
        var tab = Open("k.html", "a\n\nx", 1);
        await TabAsync();
        Assert.True(tab.Editor!.InSnippet);

        tab.Editor.CaretOffset = tab.Editor.Document.TextLength;
        Assert.False(tab.Editor.InSnippet);
        await TabAsync();
        var reference = Open("k.txt", "<a href=\"\"></a>\n\nx");
        await TabAsync();
        Assert.Equal(Text(reference), Text(tab));
    }

    [AvaloniaFact]
    public async Task With_Emmet_on_Tab_off_Tab_indents_in_html_and_the_plugin_is_not_started()
    {
        _app.Workbench.Set("editor.emmet", JsonValue.Create(false));
        var reference = Open("off.txt", "ul>li*3");
        await TabAsync();
        var tab = Open("off.html", "ul>li*3");
        await TabAsync();
        Assert.Equal(Text(reference), Text(tab));
        Assert.False(_app.Plugins.Documents.HasTabProviders);
        Assert.False(tab.Editor!.InSnippet);
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
    public async Task Wrapping_in_a_link_leaves_the_caret_in_the_empty_href_and_Tab_leaves_the_element()
    {
        var tab = Open("w3.html", "home");
        tab.Editor!.SelectAll();
        _dialogs.Prompts.Enqueue("a");

        await CommandAsync("nks.tools.emmetWrap");

        Assert.Equal("<a href=\"\">home</a>", Text(tab));
        Assert.Equal("<a href=\"".Length, tab.Editor.CaretOffset);
        Assert.True(tab.Editor.InSnippet);
        await TabAsync();
        Assert.False(tab.Editor.InSnippet);
        Assert.Equal(Text(tab).Length, tab.Editor.CaretOffset);
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

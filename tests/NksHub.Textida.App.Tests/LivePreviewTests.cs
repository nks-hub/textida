using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.3.1 and P9.3.3 in the app: the live preview of the current Markdown or HTML file, the outline of its headings, tables as grids, and the commands that align a table and write the table of contents.</summary>
[Collection("LocState")]
public class LivePreviewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public LivePreviewTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab Open(string text, string language)
    {
        var tab = _w.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById(language));
        tab.Editor!.Document.Text = text;
        return tab;
    }

    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static string TextOf(Control root) => string.Join("\n", Tree(root).OfType<TextBlock>().Select(t =>
        t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? ""));

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 10_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task The_preview_follows_an_edit_after_the_debounce_and_the_command_is_wired()
    {
        var tab = Open("# First\n", "markdown");
        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.markdownPreview", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, "the command");
        Assert.True(_w.PreviewPanelOpen);
        Assert.Contains("First", TextOf(_w.PreviewBody!));

        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "\nSome **new** words\n");
        await Until(() => TextOf(_w.PreviewBody!).Contains("Some new words"), "the preview to follow the edit");

        _w.ClosePreview();
        Assert.False(_w.PreviewPanelOpen);
        Assert.Null(_w.LivePreviewTab);
        tab.Editor.Document.Insert(0, "x");
        await Task.Delay(PluginLimitsPlaceholders.PreviewLiveDebounceMs + 200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(_w.PreviewPanelOpen);
    }

    [AvaloniaFact]
    public void A_table_renders_as_a_grid_with_its_cells_and_the_text_after_it_stays_a_paragraph()
    {
        Open("Intro\n\n| name | qty |\n|:--|--:|\n| apple | 3 |\n| fig |\n\nafter\n", "markdown");
        _w.ShowLivePreview(outline: false);
        var grid = Tree(_w.PreviewBody!).OfType<Grid>().Single();
        Assert.Equal(2, grid.ColumnDefinitions.Count);
        Assert.Equal(3, grid.RowDefinitions.Count);
        var cells = grid.Children.OfType<Border>().ToDictionary(b => (Grid.GetRow(b), Grid.GetColumn(b)), b => TextOf(b));
        Assert.Equal("name", cells[(0, 0)]);
        Assert.Equal("qty", cells[(0, 1)]);
        Assert.Equal("apple", cells[(1, 0)]);
        Assert.Equal("3", cells[(1, 1)]);
        Assert.Equal("fig", cells[(2, 0)]);
        Assert.Equal("", cells[(2, 1)]);
        Assert.Contains("after", TextOf(_w.PreviewBody!));
    }

    [AvaloniaFact]
    public void The_outline_lists_the_headings_and_a_click_puts_the_caret_on_the_line()
    {
        var tab = Open("# Top\ntext\n## Part A\nmore\n```\n# in code\n```\n### Deep\n", "markdown");
        _w.ShowLivePreview(outline: true);
        var buttons = Tree(_w.PreviewBody!).OfType<Button>().ToList();
        Assert.Equal(["Top", "Part A", "Deep"], buttons.Select(b => TextOf(b)));
        buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(3, tab.Editor!.Document.GetLineByOffset(tab.Editor.CaretOffset).LineNumber);
    }

    [AvaloniaFact]
    public void A_script_in_an_html_file_stays_text_and_a_remote_image_is_not_loaded()
    {
        Open("<html><script>alert(1)</script><img src=\"https://remote.example/a.png\"></html>", "html");
        _w.ShowLivePreview(outline: false);
        Assert.Contains("<script>alert(1)</script>", TextOf(_w.PreviewBody!));
        Assert.DoesNotContain(Tree(_w.PreviewBody!), c => c is Image or Button);

        Open("![chart](https://remote.example/a.png) and ![local](file:///C:/private/a.png)\n", "markdown");
        _w.ShowLivePreview(outline: false);
        var text = TextOf(_w.PreviewBody!);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "chart")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "local")), text);
        Assert.DoesNotContain(Tree(_w.PreviewBody!), c => c is Image);
    }

    [AvaloniaFact]
    public void A_file_of_another_kind_gets_a_message_and_no_panel()
    {
        Open("plain", "csharp");
        _w.ShowLivePreview(outline: false);
        Assert.False(_w.PreviewPanelOpen);
    }

    [AvaloniaFact]
    public void The_table_command_aligns_the_table_under_the_caret_as_one_undo_step()
    {
        var tab = Open("before\n\nname|qty\n:--|--:\napple|3\n\nafter\n", "markdown");
        tab.Editor!.CaretOffset = tab.Editor.Document.GetLineByNumber(5).Offset + 2;
        _w.FormatMarkdownTable();
        Assert.Equal("before\n\n| name  | qty |\n| :---- | --: |\n| apple |   3 |\n\nafter\n", tab.Editor.Document.Text);
        _w.Undo();
        Assert.Equal("before\n\nname|qty\n:--|--:\napple|3\n\nafter\n", tab.Editor.Document.Text);

        tab.Editor.CaretOffset = 0;
        _w.FormatMarkdownTable();
        Assert.Equal("before\n\nname|qty\n:--|--:\napple|3\n\nafter\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void The_toc_command_rewrites_only_the_block_between_the_markers()
    {
        var tab = Open("# Title\n\n<!-- TOC -->\nold\n<!-- /TOC -->\n\n## One\ntext\n## Two\n", "markdown");
        _w.UpdateMarkdownToc();
        Assert.Equal("# Title\n\n<!-- TOC -->\n- [Title](#title)\n  - [One](#one)\n  - [Two](#two)\n<!-- /TOC -->\n\n## One\ntext\n## Two\n", tab.Editor!.Document.Text);
        var again = tab.Editor.Document.Text;
        _w.UpdateMarkdownToc();
        Assert.Equal(again, tab.Editor.Document.Text);
    }

    private static string Selected(DocTab tab) => tab.Editor!.SelectedText;

    private void Press(Avalonia.Input.Key key, Avalonia.Input.RawInputModifiers mods = Avalonia.Input.RawInputModifiers.None)
    {
        Dispatcher.UIThread.RunJobs();
        TestApp.Press(_w, key, mods);
    }

    [AvaloniaFact]
    public void Tab_in_a_table_selects_the_next_cell_aligns_and_adds_a_row_after_the_last_cell()
    {
        var tab = Open("name|qty\n:--|--:\napple|3\n", "markdown");
        tab.Editor!.CaretOffset = 0;
        Press(Avalonia.Input.Key.Tab);
        Assert.Equal("| name  | qty |\n| :---- | --: |\n| apple |   3 |\n", tab.Editor.Document.Text);
        Assert.Equal("qty", Selected(tab));
        Press(Avalonia.Input.Key.Tab);
        Assert.Equal("apple", Selected(tab));
        Press(Avalonia.Input.Key.Tab);
        Assert.Equal("3", Selected(tab));
        Press(Avalonia.Input.Key.Tab);
        Assert.Equal("| name  | qty |\n| :---- | --: |\n| apple |   3 |\n|       |     |\n", tab.Editor.Document.Text);
        Assert.Equal(4, tab.Editor.Document.GetLineByOffset(tab.Editor.CaretOffset).LineNumber);
        Press(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.Shift);
        Assert.Equal("3", Selected(tab));
        Press(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.Shift);
        Assert.Equal("apple", Selected(tab));
    }

    [AvaloniaFact]
    public void Each_table_key_is_one_undo_step()
    {
        const string original = "name|qty\n:--|--:\napple|3\n";
        var tab = Open(original, "markdown");
        tab.Editor!.CaretOffset = 0;
        Press(Avalonia.Input.Key.Tab);
        _w.Undo();
        Assert.Equal(original, tab.Editor.Document.Text);

        tab.Editor.CaretOffset = tab.Editor.Document.GetLineByNumber(3).Offset;
        Press(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Shift);
        Assert.Equal("| name  | qty |\n| :---- | --: |\n| apple |   3 |\n|       |     |\n", tab.Editor.Document.Text);
        _w.Undo();
        Assert.Equal(original, tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public void Shift_enter_in_a_table_adds_an_empty_row_with_the_right_number_of_cells_below_the_caret_row()
    {
        var tab = Open("| a | b | c |\n|---|---|---|\n| 1 | 2 | 3 |\n| 4 | 5 | 6 |\n", "markdown");
        tab.Editor!.CaretOffset = tab.Editor.Document.GetLineByNumber(3).Offset + 2;
        Press(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Shift);
        Assert.Equal("| a   | b   | c   |\n| --- | --- | --- |\n| 1   | 2   | 3   |\n|     |     |     |\n| 4   | 5   | 6   |\n", tab.Editor.Document.Text);
        Assert.Equal(4, tab.Editor.Document.GetLineByOffset(tab.Editor.CaretOffset).LineNumber);
    }

    [AvaloniaFact]
    public void Tab_outside_a_table_indents_as_before()
    {
        const string text = "one | two\nthree\n";
        var plain = Open(text, "markdown");
        plain.Editor!.CaretOffset = 0;
        Assert.Equal("false", new ShellContext(_w, forMenu: false).Get("inMarkdownTable"));
        Press(Avalonia.Input.Key.Tab);
        int indent = plain.Editor.Document.Text.Length - text.Length;
        Assert.True(indent > 0);
        Assert.Equal(text, plain.Editor.Document.Text[indent..]);

        var table = Open("a|b\n-|-\n1|2\n", "markdown");
        table.Editor!.CaretOffset = 0;
        Assert.Equal("true", new ShellContext(_w, forMenu: false).Get("inMarkdownTable"));
    }

    [AvaloniaFact]
    public void The_repair_command_fixes_a_table_without_dashes_as_one_undo_step()
    {
        var tab = Open("x\n\na|b\n1|\n", "markdown");
        tab.Editor!.CaretOffset = tab.Editor.Document.GetLineByNumber(3).Offset;
        _w.RepairMarkdownTable();
        Assert.Equal("x\n\n| a   | b   |\n| --- | --- |\n| 1   |     |\n", tab.Editor.Document.Text);
        _w.Undo();
        Assert.Equal("x\n\na|b\n1|\n", tab.Editor.Document.Text);
    }
}

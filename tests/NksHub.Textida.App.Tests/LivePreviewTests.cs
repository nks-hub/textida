using System.Text;
using System.Text.RegularExpressions;
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

    private static string TextOf(Control root) => string.Join("\n", Tree(root).Select(c => c switch
    {
        MarkdownReader reader => reader.Document.PlainText,
        TextBlock t => t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? "",
        _ => null,
    }).OfType<string>());

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
        await _w.PreviewRenderTask;
        Assert.True(_w.PreviewPanelOpen);
        Assert.Contains("First", TextOf(_w.PreviewBody!));

        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "\nSome **new** words\n");
        await Until(() => TextOf(_w.PreviewBody!).Contains("Some new words"), "the preview to follow the edit");

        _w.ClosePreview();
        Assert.False(_w.PreviewPanelOpen);
        Assert.Null(_w.LivePreviewTab);
        tab.Editor.Document.Insert(0, "x");
        await Task.Delay(PluginLimits.PreviewLiveDebounceMs + 200);
        Dispatcher.UIThread.RunJobs();
        Assert.False(_w.PreviewPanelOpen);
    }

    [AvaloniaFact]
    public async Task A_table_renders_as_a_grid_with_its_cells_and_the_text_after_it_stays_a_paragraph()
    {
        Open("Intro\n\n| name | qty |\n|:--|--:|\n| apple | 3 |\n| fig |\n\nafter\n", "markdown");
        _w.ShowLivePreview();
        await _w.PreviewRenderTask;
        var reader = Assert.IsType<MarkdownReader>(_w.PreviewBody);
        var rows = reader.Document.Blocks.Where(b => b.Kind == ReaderBlockKind.TableRow).ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, r.Cells.Length));
        Assert.Equal(["name\tqty", "apple\t3", "fig\t"], rows.Select(r => r.Text));
        Assert.True(rows[0].Header);
        Assert.Contains("after", TextOf(_w.PreviewBody!));
    }

    [AvaloniaFact]
    public async Task The_outline_command_opens_the_headings_panel_and_a_heading_puts_the_caret_on_its_line()
    {
        var tab = Open("# Top\ntext\n## Part A\nmore\n```\n# in code\n```\n### Deep\n", "markdown");
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _w.ShowHeadingsPanel());
        Assert.True(_w.IsPanelOpen(SidePanel.Headings));
        var view = _w.HeadingsPanelView;
        await Until(() => view.Headings.Count == 3, "the headings");
        Assert.Equal(["Top", "Part A", "Deep"], view.Headings.Select(h => h.Title));
        view.Activate(view.Headings[1]);
        Assert.Equal(3, tab.Editor!.Document.GetLineByOffset(tab.Editor.CaretOffset).LineNumber);
    }

    [AvaloniaFact]
    public async Task A_script_in_an_html_file_stays_text_and_a_remote_image_waits_for_the_user()
    {
        Open("<html><script>alert(1)</script><img src=\"https://remote.example/a.png\"></html>", "html");
        _w.ShowLivePreview();
        await _w.PreviewRenderTask;
        _w.UpdateLayout();
        Assert.Contains("<script>alert(1)</script>", TextOf(_w.PreviewBody!));
        Assert.DoesNotContain(Tree(_w.PreviewBody!), c => c is Image);
        var ask = Assert.Single(Tree(_w.PreviewBody!).OfType<Button>());
        Assert.Equal(Loc.Get(K.PreviewImageRemote, ("host", "remote.example"), ("alt", "")), Avalonia.Automation.AutomationProperties.GetName(ask));

        Open("![chart](https://remote.example/a.png) and ![local](file:///C:/private/a.png)\n", "markdown");
        _w.ShowLivePreview();
        await _w.PreviewRenderTask;
        _w.UpdateLayout();
        var text = TextOf(_w.PreviewBody!);
        Assert.Contains(Loc.Get(K.PreviewImageRemote, ("host", "remote.example"), ("alt", "chart")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "local")), text);
        Assert.DoesNotContain(Tree(_w.PreviewBody!), c => c is Image);
    }

    [AvaloniaFact]
    public void A_file_of_another_kind_gets_a_message_and_no_panel()
    {
        Open("plain", "csharp");
        _w.ShowLivePreview();
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

    [AvaloniaFact]
    public void Formatting_and_adding_a_row_keep_quote_list_prefixes_mixed_endings_and_unicode_selections()
    {
        const string source = "before\r\n\r\n> - name|value\r\n>   :---|---:\n>   ž|😀\r\n\r\nafter\n";
        var tab = Open(source, "markdown");
        int depth = tab.Doc.Doc.UndoDepth;
        byte[] Bytes() => tab.Doc.Doc.ReadBytes(new NksHub.Textida.Core.Documents.ByteRange(0, tab.Doc.Doc.ByteLength));
        static string[] Endings(string text) => Regex.Matches(text, "\\r\\n|\\r|\\n").Select(match => match.Value).ToArray();
        byte[] originalBytes = Encoding.UTF8.GetBytes(source);
        Assert.Equal(originalBytes, Bytes());
        tab.Editor!.Select(source.IndexOf("😀", StringComparison.Ordinal), 2);

        _w.FormatMarkdownTable();
        string formatted = tab.Editor.Document.Text;
        Assert.NotEqual(source, formatted);
        Assert.StartsWith("before\r\n\r\n> - |", formatted);
        Assert.EndsWith("\r\n\r\nafter\n", formatted);
        Assert.Equal(Endings(source), Endings(formatted));
        Assert.Equal("😀", tab.Editor.SelectedText);
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);

        Press(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Shift);
        string withRow = tab.Editor.Document.Text;
        Assert.Equal(["\r\n", "\r\n", "\r\n", "\n", "\r\n", "\r\n", "\r\n", "\n"], Endings(withRow));
        var caretLine = tab.Editor.Document.GetLineByOffset(tab.Editor.CaretOffset);
        Assert.Equal(6, caretLine.LineNumber);
        string newRow = tab.Editor.Document.GetText(caretLine.Offset, caretLine.Length);
        Assert.StartsWith(">   |", newRow);
        Assert.Equal(newRow.IndexOf('|') + 2, tab.Editor.CaretOffset - caretLine.Offset);
        Assert.Equal(depth + 2, tab.Doc.Doc.UndoDepth);

        _w.Undo();
        Assert.Equal(formatted, tab.Editor.Document.Text);
        _w.Undo();
        Assert.Equal(source, tab.Editor.Document.Text);
        Assert.Equal(originalBytes, Bytes());
        Assert.Equal(depth, tab.Doc.Doc.UndoDepth);
        _w.Redo();
        _w.Redo();
        Assert.Equal(withRow, tab.Editor.Document.Text);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_refused_table_format_or_missing_separator_repair_keeps_bytes_selection_and_undo(bool repair)
    {
        string header = "|" + string.Concat(Enumerable.Repeat("header|", 500)) + "\n";
        string source = header + (repair ? "" : "|" + string.Concat(Enumerable.Repeat("---|", 500)) + "\n")
            + string.Concat(Enumerable.Repeat("|one|\n", 100));
        var tab = Open(source, "markdown");
        tab.Editor!.Select(source.IndexOf("one", StringComparison.Ordinal), 3);
        byte[] original = Encoding.UTF8.GetBytes(source);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
        long revision = tab.Doc.Doc.Revision;
        int undo = tab.Doc.Doc.UndoDepth;
        var selection = tab.Surface!.GetSelections()[0];
        bool modified = tab.Doc.Doc.IsModified;

        if (repair) _w.RepairMarkdownTable();
        else _w.FormatMarkdownTable();

        byte[] bytes = tab.Doc.Doc.ReadBytes(new NksHub.Textida.Core.Documents.ByteRange(0, tab.Doc.Doc.ByteLength));
        Assert.Equal(source, tab.Editor.Document.Text);
        Assert.Equal(original, bytes);
        Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.Equal(undo, tab.Doc.Doc.UndoDepth);
        Assert.Equal(modified, tab.Doc.Doc.IsModified);
        Assert.Equal(selection, tab.Surface.GetSelections()[0]);
        Assert.NotEmpty(_w.StatusMessageText);
    }
}

using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>Text compare (P5.2.1) and synchronized scrolling (P5.2.2) in the window: the tab, the copy of a block and the context menu.</summary>
[Collection("LocState")]
public class CompareTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public CompareTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab NewWith(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        return tab;
    }

    private async Task<DiffView> OpenWithClipboard(DocTab tab, string clipboard)
    {
        await _w.Clipboard!.SetTextAsync(clipboard);
        var opened = await _w.CompareWithClipboardAsync();
        var view = (DiffView)opened!.Custom!;
        await view.Session.RefreshAsync();
        return view;
    }

    [AvaloniaFact]
    public async Task Compare_with_the_clipboard_opens_a_tab_whose_document_side_takes_a_block_in_one_undo_step()
    {
        var tab = NewWith("a\nb\nc\n");
        var view = await OpenWithClipboard(tab, "a\nB\nc\n");

        Assert.Same(view, _w.ActiveCompare);
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);
        Assert.Equal($"Porovnání: {tab.Title} / Schránka", _w.Active!.Title);
        Assert.Equal("1 rozdíl", view.SummaryText);

        var doc = tab.Doc.Doc;
        int undoBefore = doc.UndoDepth;
        view.SelectHunk(0);
        Assert.True(view.CopyCurrent(DiffSide.A));
        Assert.Equal("a\nB\nc\n", tab.Editor!.Document.Text);
        Assert.Equal(undoBefore + 1, doc.UndoDepth);

        await view.Session.RefreshAsync();
        Assert.True(view.Session.Diff!.IsIdentical);
        Assert.Equal("Žádné rozdíly", view.SummaryText);

        doc.Undo();
        Assert.Equal("a\nb\nc\n", tab.Editor.Document.Text);
        Assert.Equal(undoBefore, doc.UndoDepth);
    }

    [AvaloniaFact]
    public async Task A_side_that_is_not_a_document_does_not_take_a_block()
    {
        var tab = NewWith("a\nb\n");
        var view = await OpenWithClipboard(tab, "a\nx\n");
        string? said = null;
        view.Refused += s => said = s;
        view.SelectHunk(0);

        Assert.False(view.CopyCurrent(DiffSide.B));

        Assert.Equal("Tuto stranu nelze upravovat.", said);
        Assert.Equal("a\nb\n", tab.Editor!.Document.Text);
        Assert.False(view.ToRightButton.IsVisible);
        Assert.True(view.ToLeftButton.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_copy_after_the_document_changed_is_refused_and_the_comparison_is_made_again()
    {
        var tab = NewWith("a\nb\nc\n");
        var view = await OpenWithClipboard(tab, "a\nB\nc\n");
        string? said = null;
        view.Refused += s => said = s;
        view.SelectHunk(0);
        tab.Editor!.Document.Insert(0, "typed ");

        Assert.False(view.CopyCurrent(DiffSide.A));

        Assert.StartsWith("Text se po porovnání změnil", said);
        Assert.Equal("typed a\nb\nc\n", tab.Editor.Document.Text);
        await view.Session.RefreshAsync();
        Assert.Equal([new DiffHunk(0, 2, 0, 2)], view.Session.Diff!.Hunks);
    }

    [AvaloniaFact]
    public async Task A_selection_is_compared_with_the_clipboard_instead_of_the_whole_tab()
    {
        var tab = NewWith("first\nsecond\nthird\n");
        tab.Editor!.Select(6, 6);
        await _w.Clipboard!.SetTextAsync("second");

        var view = (DiffView)(await _w.CompareWithClipboardAsync())!.Custom!;
        await view.Session.RefreshAsync();

        Assert.Equal($"Výběr z {tab.Title}", view.Session.Left.Title);
        Assert.True(view.Session.Diff!.IsIdentical);
        Assert.False(view.ToLeftButton.IsVisible);
    }

    [AvaloniaFact]
    public async Task An_empty_clipboard_opens_nothing()
    {
        NewWith("x");
        await _w.Clipboard!.ClearAsync();
        int tabs = _w.Tabs.Count();

        Assert.Null(await _w.CompareWithClipboardAsync());

        Assert.Equal(tabs, _w.Tabs.Count());
        Assert.Contains("Schránka neobsahuje text", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void Compare_with_disk_opens_the_built_in_comparison_while_no_tool_is_set()
    {
        var tab = _w.OpenFile(_temp.File("disk.txt", "one\ntwo\n"), null);
        tab.Editor!.Document.Insert(0, "zero\n");

        Assert.Null(_w.CompareWithDisk(tab));

        var view = Assert.IsType<DiffView>(_w.Active!.Custom);
        Assert.Equal("disk.txt", view.Session.Left.Title);
        Assert.Equal("disk.txt na disku", view.Session.Right.Title);
        Assert.False(view.Session.Right.CanReceive);
    }

    [AvaloniaFact]
    public async Task Compare_with_a_file_reads_the_file_in_its_own_encoding()
    {
        var tab = NewWith("příliš\nžluťoučký\n");
        var path = _temp.Bytes("cp1250.txt", System.Text.Encoding.GetEncoding(1250).GetBytes("příliš\nkůň\n"));
        _dialogs.Picked.Add(path);

        var view = (DiffView)(await _w.CompareWithFileAsync())!.Custom!;
        await view.Session.RefreshAsync();

        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);
        Assert.Equal(["kůň"], view.Session.Diff.B.ReadLines(1, 1));
    }

    [AvaloniaFact]
    public async Task The_command_line_opens_two_files_as_a_comparison()
    {
        var left = _temp.File("l.txt", "1\n2\n");
        var right = _temp.File("r.txt", "1\nX\n");

        await _app.OpenRequestAsync(new NksHub.Textida.App.Startup.IpcRequest { Diff = [left, right] });

        var view = Assert.IsType<DiffView>(_w.Active!.Custom);
        await view.Session.RefreshAsync();
        Assert.Equal(left, view.Session.Left.Document!.Path);
        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _app.OpenRequestAsync(new NksHub.Textida.App.Startup.IpcRequest { Diff = [left, Path.Combine(_temp.Path, "none.txt")] }));
    }

    [AvaloniaFact]
    public async Task Two_pasted_texts_are_compared_with_nothing_in_the_way()
    {
        var tab = _w.OpenTextCompare();
        var view = (DiffView)tab.Custom!;
        await view.Session.RefreshAsync();
        Assert.Equal("Žádné rozdíly", view.SummaryText);
        Assert.True(view.PasteLeftButton.IsVisible && view.PasteRightButton.IsVisible);

        await _w.Clipboard!.SetTextAsync("x\ny\nz");
        Assert.True(await view.PasteAsync(DiffSide.A));
        await _w.Clipboard.SetTextAsync("x\nq\nz");
        Assert.True(await view.PasteAsync(DiffSide.B));

        Assert.Equal([new DiffHunk(1, 1, 1, 1)], view.Session.Diff!.Hunks);
        Assert.Equal("1 rozdíl", view.SummaryText);
    }

    [AvaloniaFact]
    public async Task Next_and_previous_difference_walk_the_blocks_and_wrap()
    {
        var tab = NewWith("1\n2\n3\n4\n5\n6\n7\n");
        var view = await OpenWithClipboard(tab, "1\nx\n3\n4\ny\n6\n7\n");

        Assert.True(view.NextDifference());
        Assert.Equal(0, view.CurrentHunk);
        Assert.True(view.NextDifference());
        Assert.Equal(1, view.CurrentHunk);
        Assert.True(view.NextDifference());
        Assert.Equal(0, view.CurrentHunk);
        Assert.True(view.PreviousDifference());
        Assert.Equal(1, view.CurrentHunk);
    }

    [AvaloniaFact]
    public async Task A_side_follows_the_other_across_an_inserted_block()
    {
        // long enough to scroll whatever height the laid-out window gives the view; the view cannot scroll past its last page
        var lines = Enumerable.Range(0, 300).Select(i => "l" + i).ToList();
        var left = string.Join("\n", lines);
        var right = string.Join("\n", lines.Take(150).Concat(["n1", "n2", "n3"]).Concat(lines.Skip(150)));
        var tab = NewWith(left);
        var view = await OpenWithClipboard(tab, right);

        view.ScrollToRow(148);
        Assert.Equal((148, 148), (view.FirstLine(DiffSide.A), view.FirstLine(DiffSide.B)));
        view.ScrollToRow(151); // inside the inserted block: the left side stays where the block sits
        Assert.Equal((150, 151), (view.FirstLine(DiffSide.A), view.FirstLine(DiffSide.B)));
        view.ScrollToRow(153); // l150 stands level with l150
        Assert.Equal((150, 153), (view.FirstLine(DiffSide.A), view.FirstLine(DiffSide.B)));
        view.ScrollToRow(200);
        Assert.Equal((197, 200), (view.FirstLine(DiffSide.A), view.FirstLine(DiffSide.B)));
    }

    [AvaloniaFact]
    public async Task The_view_draws_and_a_click_on_the_gutter_arrow_copies_the_block()
    {
        var tab = NewWith("x\nb\nc\n");
        var view = await OpenWithClipboard(tab, "a\nb\nc\n");
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.CaptureRenderedFrame());
        Assert.True(view.VisibleRows > 2);

        var surface = view.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "DiffSurface");
        var arrow = surface.TranslatePoint(new Point((surface.Bounds.Width - 48) / 2 + 10, 4), _w)!.Value;
        _w.MouseDown(arrow, MouseButton.Left);
        _w.MouseUp(arrow, MouseButton.Left);

        Assert.Equal("a\nb\nc\n", tab.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task The_comparison_follows_edits_of_the_document_after_a_pause()
    {
        var tab = NewWith("a\nb\n");
        var view = await OpenWithClipboard(tab, "a\nb\n");
        Assert.True(view.Session.Diff!.IsIdentical);

        tab.Editor!.Document.Insert(0, "new\n");
        await view.Session.RefreshAsync();

        Assert.Equal([new DiffHunk(0, 1, 0, 0)], view.Session.Diff!.Hunks);
    }

    [AvaloniaFact]
    public async Task A_paged_file_is_compared_and_takes_a_block_without_loading_its_text()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        const string Line = " some text of a line of the file";
        var lines = Enumerable.Range(0, 80_000).Select(i => $"{i:D6}{Line}");
        var path = _temp.File("big.txt", string.Join("\n", lines) + "\n");
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Doc.IsLarge);
        var doc = tab.Doc.Doc;
        long lineBytes = 6 + Line.Length + 1;
        doc.Replace(new ByteRange(40_000 * lineBytes, 40_000 * lineBytes), "inserted\n"u8.ToArray());
        int undoBefore = doc.UndoDepth;

        Assert.Null(_w.CompareWithDisk(tab));
        var view = (DiffView)_w.Active!.Custom!;
        await view.Session.RefreshAsync();

        Assert.Equal([new DiffHunk(40_000, 1, 40_000, 0)], view.Session.Diff!.Hunks);
        view.SelectHunk(0);
        Assert.True(view.CopyCurrent(DiffSide.A));
        await view.Session.RefreshAsync();
        Assert.True(view.Session.Diff!.IsIdentical);
        Assert.Equal(undoBefore + 1, doc.UndoDepth);
    }

    [AvaloniaFact]
    public void The_context_menu_of_the_editor_offers_the_compares()
    {
        var tab = NewWith("x");
        var menu = Assert.IsType<ContextMenu>(tab.Editor!.ContextMenu);
        var nodes = MenuBuilder.Nodes(_app.Workbench, new ShellContext(_w, forMenu: true), _ => null, _ => { }, "editor.context");
        var ids = nodes.OfType<MenuCommand>().Select(c => c.CommandId).ToList();
        Assert.Contains("nks.compare.clipboard", ids);
        Assert.Contains("nks.file.compareWithDisk", ids);
        Assert.Contains("nks.edit.paste", ids);
        Assert.Equal("Porovnat se schránkou", nodes.OfType<MenuCommand>().Single(c => c.CommandId == "nks.compare.clipboard").Label);

        Assert.NotNull(menu);
    }

    [AvaloniaFact]
    public async Task The_compare_commands_run_through_the_registry()
    {
        var tab = NewWith("a\nb\n");
        await _w.Clipboard!.SetTextAsync("a\nc\n");
        var ctx = new ShellContext(_w, forMenu: true);
        await _app.Workbench.Commands.ExecuteAsync("nks.compare.clipboard", ctx);
        for (int i = 0; i < 200 && _w.ActiveCompare is null; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        var view = Assert.IsType<DiffView>(_w.Active!.Custom);
        await view.Session.RefreshAsync();

        Assert.Equal("true", new ShellContext(_w, forMenu: false).Get("compareMode"));
        await _app.Workbench.Commands.ExecuteAsync("nks.compare.next", new ShellContext(_w, forMenu: false));
        Assert.Equal(0, view.CurrentHunk);
        await _app.Workbench.Commands.ExecuteAsync("nks.compare.copyToLeft", new ShellContext(_w, forMenu: false));
        Assert.Equal("a\nc\n", tab.Editor!.Document.Text);
    }

    private static IEnumerable<MenuCommand> FlattenCommands(IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c],
        MenuGroup g => FlattenCommands(g.Items),
        _ => [],
    });

    [AvaloniaFact]
    public async Task Ignoring_whitespace_and_case_compares_the_open_tab_again_and_the_bar_says_so()
    {
        var tab = NewWith("Alpha\n  beta\ngamma\n");
        var view = await OpenWithClipboard(tab, "alpha\nbeta\nGAMMA\n");
        Assert.Equal([new DiffHunk(0, 3, 0, 3)], view.Session.Diff!.Hunks);
        MenuCommand Item(string id) => FlattenCommands(_w.MenuModel).Single(c => c.CommandId == id);
        bool Checked(string id) => Item(id).CheckedNow?.Invoke() ?? Item(id).Checked ?? false;
        Assert.False(Checked("nks.compare.ignoreWhitespace"));

        Item("nks.compare.ignoreWhitespace").Run();
        Assert.True(Checked("nks.compare.ignoreWhitespace"));
        Assert.True(_app.Workbench.Settings.GetBool("compare.ignoreWhitespace"));
        Assert.Equal(DiffIgnore.Whitespace, view.Session.Ignore);
        await view.Session.RefreshAsync();
        Assert.Equal([new DiffHunk(0, 1, 0, 1), new DiffHunk(2, 1, 2, 1)], view.Session.Diff!.Hunks);
        Assert.Equal("2 rozdíly · mezery se nerozlišují", view.SummaryText);

        Item("nks.compare.ignoreCase").Run();
        await view.Session.RefreshAsync();
        Assert.True(view.Session.Diff!.IsIdentical);
        Assert.Equal("Žádné rozdíly · mezery se nerozlišují · velikost písmen se nerozlišuje", view.SummaryText);

        var next = (DiffView)_w.OpenTextCompare().Custom!;
        Assert.Equal(DiffIgnore.Whitespace | DiffIgnore.Case, next.Session.Ignore);
    }

    [AvaloniaFact]
    public async Task An_open_comparison_follows_a_change_of_the_editor_font()
    {
        var tab = NewWith("a\n");
        var view = await OpenWithClipboard(tab, "b\n");
        double size = view.Options.FontSize + 7;

        Assert.Null(_app.Workbench.Set("editor.fontSize", JsonValue.Create(size)));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(size, view.Options.FontSize);
        Assert.Equal(size, tab.Editor!.FontSize);
    }

    [AvaloniaFact]
    public async Task The_screen_reader_hears_the_current_difference_with_its_lines_and_the_first_line_of_each_side()
    {
        var tab = NewWith("a\n  old one\nold two\nc\n");
        var view = await OpenWithClipboard(tab, "a\nnew\nc\n");
        var peer = Assert.IsType<DiffAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(view));
        Assert.Equal("1 rozdíl", peer.Value);
        Assert.True(peer.IsReadOnly);
        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
        Assert.Equal("Porovnání", peer.GetName());

        view.SelectHunk(0);
        Assert.Equal("Rozdíl 1 z 1, vlevo řádky 2 až 3, vpravo řádek 2. Vlevo: old one. Vpravo: new", peer.Value);

        var inserted = await OpenWithClipboard(NewWith("a\nc\n"), "a\nb\nc\n");
        var other = (DiffAutomationPeer)ControlAutomationPeer.CreatePeerForElement(inserted);
        inserted.NextDifference();
        Loc.SetLanguage("en");
        Assert.Equal("Difference 1 of 1, left no lines, right line 2. Right: b", other.Value);
    }

    // ---- synchronized scrolling of two tabs ----

    private sealed class FakeHost(long lines) : IMinimapHost
    {
        public long First { get; set; }
        public int Moves { get; private set; }
        public long LineCount => lines;
        public string? LineText(long line) => null;
        public (long First, long Count) Visible => (First, 5);
        public void ScrollToLine(long line) { First = line; Moves++; }
        public event Action? MapChanged;
        public event Action? ContentChanged { add { } remove { } }
        public void Scroll(long line) { First = line; MapChanged?.Invoke(); }
        public void Echo() => MapChanged?.Invoke(); // the layout reporting the scroll the sync itself made
    }

    [AvaloniaFact]
    public async Task Two_views_scroll_together_through_the_line_diff_and_do_not_bounce()
    {
        var a = string.Join("\n", Enumerable.Range(0, 10).Select(i => "l" + i));
        var b = string.Join("\n", ["l0", "l1", "l2", "n1", "n2", "n3", "l3", "l4", "l5", "l6", "l7", "l8", "l9"]);
        var hostA = new FakeHost(10);
        var hostB = new FakeHost(13);
        using var sync = new ScrollSync(hostA, hostB, () => DiffSource.OfText(a), () => DiffSource.OfText(b));
        await sync.RefreshAsync();
        Assert.Equal(1, sync.Diff!.Hunks.Count);

        hostA.Scroll(3);
        Assert.Equal(6, hostB.First);
        hostB.Echo();
        Assert.Equal(3, hostA.First);
        Assert.Equal(0, hostA.Moves);

        hostB.Scroll(4); // inside the inserted block
        Assert.Equal(3, hostA.First);
        Assert.Equal(4, hostB.First);

        hostB.Scroll(9);
        Assert.Equal(6, hostA.First);
    }

    [AvaloniaFact]
    public void Synchronized_scrolling_needs_two_groups_with_text_views()
    {
        NewWith("a");
        _w.ToggleSyncScroll();
        Assert.Contains("dva textové soubory vedle sebe", _w.StatusLeftText);
        Assert.False(_w.SyncScrollOn);

        var second = NewWith("b");
        _w.MoveToOtherGroup();
        _w.ToggleSyncScroll();
        Assert.True(_w.SyncScrollOn);
        _w.ToggleSyncScroll();
        Assert.False(_w.SyncScrollOn);
        Assert.EndsWith("Synchronizované posouvání je vypnuté.", _w.StatusLeftText);
        Assert.NotNull(second);
    }
}

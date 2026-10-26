using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>Tabs, groups and split views.</summary>
[Collection("LocState")]
public class TabsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public TabsTests()
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

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private DocTab Open(string name, string content = "x") => _w.OpenFile(_temp.File(name, content), null);

    private IEnumerable<Border> Dots() => _w.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("modifieddot"));

    [AvaloniaFact]
    public void Unsaved_tab_shows_a_dot_and_a_clean_tab_a_close_button()
    {
        var tab = Open("a.txt");
        _w.UpdateLayout();
        var dot = Assert.Single(Dots());
        var close = _w.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabclose") && b.GetVisualAncestors().OfType<TabStripItem>().Any());
        Assert.False(dot.IsVisible);
        Assert.True(close.IsVisible);

        tab.Editor!.Document.Insert(1, "y");
        _w.UpdateLayout();
        dot = Assert.Single(Dots());
        close = _w.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("tabclose") && b.GetVisualAncestors().OfType<TabStripItem>().Any());
        Assert.True(dot.IsVisible);
        Assert.False(close.IsVisible);
        Assert.Equal("a.txt (neuloženo)", AutomationProperties.GetName(tab.Group!.Strip.Items.OfType<TabStripItem>().Single()));

        Assert.True(_w.SaveAsync(tab).Result);
        _w.UpdateLayout();
        Assert.False(Assert.Single(Dots()).IsVisible);
    }

    [AvaloniaFact]
    public void New_unsaved_document_has_a_dot_only_once_it_has_text()
    {
        var tab = _w.NewDocument();
        _w.UpdateLayout();
        Assert.False(Assert.Single(Dots()).IsVisible);
        tab.Editor!.Document.Text = "a";
        _w.UpdateLayout();
        Assert.True(Assert.Single(Dots()).IsVisible);
    }

    [AvaloniaFact]
    public void Large_file_tab_carries_a_size_badge()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var path = _temp.Bytes("big.log", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"row {i}\n"))));
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        var badge = Assert.Single(_w.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("sizebadge"));
        Assert.Equal(tab.Doc.SizeBadge, ((TextBlock)badge.Child!).Text);
        Assert.Matches(@"^\d+(\.\d)? MiB$", tab.Doc.SizeBadge);
        // a normal tab has none
        Open("small.txt");
        _w.UpdateLayout();
        Assert.Single(_w.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("sizebadge"));
    }

    [AvaloniaFact]
    public void Two_views_of_one_document_show_the_same_revision_and_share_undo()
    {
        var a = Open("a.txt", "one");
        var b = _w.SplitView()!;
        Assert.Equal(2, _w.Groups.Count);
        Assert.Same(a.Doc, b.Doc);
        Assert.NotSame(a.Group, b.Group);
        Assert.Same(a.Editor!.Document, b.Editor!.Document);

        a.Editor.Document.Insert(3, "!");
        Assert.Equal("one!", Text(b));
        Assert.Equal(a.Doc.Doc.Revision, b.Doc.Doc.Revision);

        _w.SelectTab(b);
        _w.Undo(); // through the view in the second group
        Assert.Equal("one", Text(a));
        Assert.Equal("one", Text(b));
        _w.SelectTab(a);
        _w.Redo();
        Assert.Equal("one!", Text(b));
    }

    [AvaloniaFact]
    public async Task Closing_one_view_keeps_the_document_and_closing_the_last_one_closes_it()
    {
        var a = Open("a.txt");
        var b = _w.SplitView()!;
        Assert.True(await _w.CloseTabAsync(b));
        Assert.Single(_w.Tabs);
        Assert.Single(_w.Groups); // the emptied group disappears
        Assert.False(a.Closed.Task.IsCompleted);
        Assert.True(await _w.CloseTabAsync(a));
        Assert.True(a.Closed.Task.IsCompleted);
        Assert.Empty(_w.Documents);
    }

    [AvaloniaFact]
    public void A_tab_moves_to_another_group_and_an_emptied_group_goes_away()
    {
        var a = Open("a.txt");
        var b = Open("b.txt");
        _w.SelectTab(b);
        _w.MoveToOtherGroup();
        Assert.Equal(2, _w.Groups.Count);
        Assert.Equal([a], _w.Groups[0].Tabs);
        Assert.Equal([b], _w.Groups[1].Tabs);

        _w.MoveTab(b, _w.Groups[0], 0);
        Assert.Single(_w.Groups);
        Assert.Equal([b, a], _w.Groups[0].Tabs);
        Assert.Same(b, _w.Active);
    }

    [AvaloniaFact]
    public void Dragging_a_tab_onto_another_group_moves_it()
    {
        var a = Open("a.txt");
        var b = Open("b.txt");
        _w.SelectTab(a);
        var c = _w.SplitView()!;
        _w.UpdateLayout();
        var target = _w.Groups[1];
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, 200), _w)!.Value;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(TabGroup.TabFormat, b));

        _w.DragDrop(point, Avalonia.Input.Raw.RawDragEventType.DragEnter, transfer, DragDropEffects.Move);
        _w.DragDrop(point, Avalonia.Input.Raw.RawDragEventType.DragOver, transfer, DragDropEffects.Move);
        _w.DragDrop(point, Avalonia.Input.Raw.RawDragEventType.Drop, transfer, DragDropEffects.Move);

        Assert.Same(_w.Groups[1], b.Group);
        Assert.Contains(b, _w.Groups[1].Tabs);
        Assert.DoesNotContain(b, _w.Groups[0].Tabs);
    }

    [AvaloniaFact]
    public void Tabs_have_a_role_a_name_and_keyboard_focus()
    {
        var a = Open("a.txt");
        var b = Open("b.txt");
        var items = _w.Groups[0].Strip.Items.OfType<TabStripItem>().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(["a.txt", "b.txt"], items.Select(AutomationProperties.GetName));
        Assert.All(items, i => Assert.True(i.Focusable));
        Assert.NotNull(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(items[0]));

        Dispatcher.UIThread.RunJobs(); // let the pending focus of the new tab settle, then move the focus to the strip
        items[1].Focus();
        TestApp.Press(_w, Key.Left);
        Assert.Same(a, _w.Active);
        Assert.Same(a, _w.Groups[0].Active);
    }

    [AvaloniaFact]
    public void Next_and_previous_tab_commands_cycle_within_the_group()
    {
        var a = Open("a.txt");
        var b = Open("b.txt");
        var c = Open("c.txt");
        TestApp.Press(_w, Key.Tab, RawInputModifiers.Control);
        Assert.Same(a, _w.Active);
        TestApp.Press(_w, Key.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Same(c, _w.Active);
    }

    [AvaloniaFact]
    public void Tab_context_menu_lists_only_the_actions_that_apply()
    {
        var a = Open("a.txt");
        var labels = ContextLabels(a);
        Assert.Equal(["Zavřít", "Rozdělit pohled", "Kopírovat cestu"], labels);

        var b = Open("b.txt");
        labels = ContextLabels(b);
        Assert.Contains("Zavřít ostatní", labels);
        Assert.Contains("Přesunout do jiné skupiny", labels);

        var untitled = _w.NewDocument();
        Assert.DoesNotContain("Kopírovat cestu", ContextLabels(untitled));
    }

    private IEnumerable<string> ContextLabels(DocTab tab)
    {
        var item = tab.Group!.Strip.Items.OfType<TabStripItem>().ElementAt(tab.Group.Tabs.ToList().IndexOf(tab));
        return item.ContextMenu!.Items.OfType<MenuItem>().Select(m => (string)m.Header!);
    }

    [AvaloniaFact]
    public void Middle_click_closes_a_tab()
    {
        var a = Open("a.txt");
        var item = a.Group!.Strip.Items.OfType<TabStripItem>().Single();
        _w.UpdateLayout();
        var center = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), _w)!.Value;
        _w.MouseDown(center, MouseButton.Middle);
        _w.MouseUp(center, MouseButton.Middle);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(_w.Tabs);
    }

    [AvaloniaFact]
    public void Document_list_panel_lists_every_tab_across_groups()
    {
        Open("a.txt");
        Open("b.txt");
        _w.SplitView();
        _w.ToggleDocuments(true);
        var list = _w.GetVisualDescendants().OfType<ListBox>().First(l => AutomationProperties.GetName(l) == Loc.Get(K.A11yDocumentList));
        Assert.Equal(3, list.Items.Count);
    }
}

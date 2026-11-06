using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.3.4: the headings panel stands beside a Markdown file in every mode, marks the current heading, jumps, follows typing, folds by level and filters.</summary>
[Collection("LocState")]
public class HeadingsPanelTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private const string Doc = "# Guide\n\nIntro text.\n\n## Install\n\nSteps.\n\n### Windows part\n\nText.\n\n### macOS part\n\nText.\n\n## Use\n\nMore.\n\n#### Deep four\n\n##### Five\n\n###### Six\n\n# Appendix\n\nEnd.\n";

    public HeadingsPanelTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
        _w.Show();
        _w.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
    }

    private async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            Settle();
        }
        Assert.True(condition(), "timed out waiting for " + what);
    }

    private async Task<(DocTab Tab, HeadingsView View)> Open(string text, ViewMode mode = ViewMode.Read, string name = "doc.md")
    {
        _app.Workbench.Set("markdown.defaultMode", mode switch { ViewMode.Edit => "edit", ViewMode.Text => "source", _ => "read" });
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.SelectTab(tab);
        Settle();
        if (tab.Custom is MarkdownPreview page) await page.RenderTask;
        if (tab.SidePreview is { } side) await side.RenderTask;
        _w.TogglePanel(SidePanel.Headings, true);
        var view = _w.HeadingsPanelView;
        await Until(() => view.Headings.Count > 0 || view.Message.Length > 0, "the headings");
        Settle();
        return (tab, view);
    }

    private static string[] Titles(IEnumerable<MarkdownHeading> headings) => [.. headings.Select(h => h.Title)];

    [AvaloniaFact]
    public async Task The_panel_stands_right_of_the_document_with_the_tree_of_headings_and_stays_in_every_mode()
    {
        var (tab, view) = await Open(Doc);
        Assert.Equal(PanelDock.Right, _w.SideLayout.DockOf(SidePanel.Headings));
        Assert.True(_w.HeadingsTool.IsVisible);
        Assert.True(_w.StripButton(SidePanel.Headings).IsVisible);
        Assert.Equal(["Guide", "Install", "Windows part", "macOS part", "Use", "Deep four", "Five", "Six", "Appendix"], Titles(view.Headings));
        var roots = view.Tree.ItemsSource!.Cast<TreeViewItem>().ToList();
        Assert.Equal(["Guide", "Appendix"], roots.Select(r => ((MarkdownHeading)r.Tag!).Title));
        var install = roots[0].Items.Cast<TreeViewItem>().First();
        Assert.Equal(["Windows part", "macOS part"], install.Items.Cast<TreeViewItem>().Select(i => ((MarkdownHeading)i.Tag!).Title));

        foreach (var mode in new[] { ViewMode.Edit, ViewMode.Text, ViewMode.Read })
        {
            Assert.True(_w.SetViewMode(tab.Doc, mode));
            Settle();
            Assert.True(_w.IsPanelOpen(SidePanel.Headings), $"open in {mode}");
            Assert.Equal(9, view.Headings.Count);
        }
    }

    [AvaloniaFact]
    public async Task Alt_8_shows_the_panel_with_the_keyboard_in_it_and_hides_it_again()
    {
        var binding = Assert.Single(_app.Workbench.Keymap.Bindings, b => b.CommandId == "nks.tools.markdownOutline");
        Assert.Equal("Alt+8", binding.Sequence.Display(NksHub.Textida.Workbench.Keys.Platform.Windows));
        Assert.DoesNotContain(_app.Workbench.Keymap.Bindings, b => b.CommandId != binding.CommandId && b.Sequence.ToString() == binding.Sequence.ToString());

        var (_, view) = await Open(Doc);
        _w.TogglePanel(SidePanel.Headings, false);
        _w.FocusActiveView();
        Settle();
        Assert.False(_w.IsPanelOpen(SidePanel.Headings));

        _w.KeyPress(Avalonia.Input.Key.D8, Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.Digit8, null);
        Settle();
        Assert.True(_w.IsPanelOpen(SidePanel.Headings));
        Assert.True(view.IsKeyboardFocusWithin);

        _w.KeyPress(Avalonia.Input.Key.D8, Avalonia.Input.RawInputModifiers.Alt, Avalonia.Input.PhysicalKey.Digit8, null);
        Settle();
        Assert.False(_w.IsPanelOpen(SidePanel.Headings));
    }

    [AvaloniaFact]
    public async Task The_current_heading_follows_the_caret_and_a_heading_moves_the_caret_in_source()
    {
        var (tab, view) = await Open(Doc, ViewMode.Text);
        var editor = _w.Active!.Editor!;
        editor.CaretOffset = editor.Document.Text.IndexOf("More.", StringComparison.Ordinal);
        Settle();
        Assert.Equal("Use", view.Current?.Title);
        var item = view.Tree.ItemsSource!.Cast<TreeViewItem>().First().Items.Cast<TreeViewItem>().Last();
        Assert.Contains("current", item.Classes);
        Assert.Equal(Loc.Get(K.HeadingsCurrent), Avalonia.Automation.AutomationProperties.GetItemStatus(item));

        view.Activate(view.Headings.Single(h => h.Title == "macOS part"));
        Settle();
        Assert.Equal(13, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
        Assert.Equal("macOS part", view.Current?.Title);
        Assert.Equal(tab.Doc, _w.Active!.Doc);
    }

    [AvaloniaFact]
    public async Task Typing_updates_the_tree_after_a_pause()
    {
        var (_, view) = await Open(Doc, ViewMode.Edit);
        var editor = _w.Active!.Editor!;
        editor.Document.Insert(editor.Document.TextLength, "\n## Freshly typed\n");
        Settle();
        Assert.DoesNotContain("Freshly typed", Titles(view.Headings)); // not on every key
        await Until(() => view.Headings.Any(h => h.Title == "Freshly typed"), "the new heading");
        Assert.Equal(10, view.Headings.Count);
    }

    [AvaloniaFact]
    public async Task The_levels_fold_the_deeper_headings_and_the_filter_keeps_matches_with_their_parents()
    {
        var (_, view) = await Open(Doc);
        view.ShowDepth(1);
        Assert.Equal(["Guide", "Appendix"], Titles(view.Visible));
        view.ShowDepth(2);
        Assert.Equal(["Guide", "Install", "Use", "Appendix"], Titles(view.Visible));
        view.ShowDepth(6);
        Assert.Equal(9, view.Visible.Count);

        view.ShowDepth(1);
        view.Filter.Text = "PART";
        Settle();
        Assert.Equal(["Guide", "Install", "Windows part", "macOS part"], Titles(view.Visible));
        view.Filter.Text = "zzz";
        Settle();
        Assert.Empty(view.Visible);
        Assert.Equal(Loc.Get(K.HeadingsNoMatch), view.Message);
        view.Filter.Text = "";
        Settle();
        Assert.Equal("", view.Message);
    }

    [AvaloniaFact]
    public async Task In_reading_the_todo_lists_every_heading_and_a_click_far_down_scrolls_the_page_there()
    {
        string todo = File.ReadAllText(Path.Combine(Repo.Root, "TODO.md"));
        var (tab, view) = await Open(todo, ViewMode.Read, "TODO.md");
        await Until(() => view.Headings.Count == MarkdownSyntax.Headings(MarkdownSyntax.Parse(todo)).Count, "all headings");
        var page = Assert.IsType<MarkdownPreview>(tab.Custom);
        var target = view.Headings[^2];
        view.Activate(target);
        await Until(() => Math.Abs(page.TopLine - target.Line) < 1.5, $"the page at line {target.Line}, now {page.TopLine}");
        Settle();
        Assert.Equal(target.Title, view.Current?.Title);
        Assert.InRange(page.Reader!.LaidCount, 1, 400);
    }

    [AvaloniaFact]
    public async Task Another_kind_of_file_gets_a_message_and_no_button_and_the_side_is_kept_in_the_profile()
    {
        var (_, view) = await Open(Doc);
        var text = _w.OpenFile(_temp.File("plain.txt", "text"), null);
        _w.SelectTab(text);
        Settle();
        Assert.Equal(Loc.Get(K.PreviewOutlineUnsupported), view.Message);
        Assert.Empty(view.Headings);

        _w.MovePanel(SidePanel.Headings, PanelDock.Left);
        Assert.Equal("left", _w.SideLayout.ToSaved().Headings);
        Assert.Equal(PanelDock.Left, PanelLayout.FromSaved(_w.SideLayout.ToSaved()).DockOf(SidePanel.Headings));
        Assert.Equal(PanelDock.Right, PanelLayout.FromSaved(new SavedPanels()).DockOf(SidePanel.Headings));
        _w.TogglePanel(SidePanel.Headings, false);
        Settle();
        Assert.False(_w.StripButton(SidePanel.Headings).IsVisible);
    }

    [AvaloniaFact]
    public async Task The_panel_command_shows_it_puts_the_keyboard_in_it_and_hides_it()
    {
        await Open(Doc);
        _w.TogglePanel(SidePanel.Headings, false);
        Settle();
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, await _app.Workbench.Commands.ExecuteAsync("nks.view.toggleHeadingsPanel", new ShellContext(_w, forMenu: true)));
        Settle();
        Assert.True(_w.IsPanelOpen(SidePanel.Headings));
        Assert.True(_w.HeadingsTool.IsKeyboardFocusWithin);
        await _app.Workbench.Commands.ExecuteAsync("nks.view.toggleHeadingsPanel", new ShellContext(_w, forMenu: true));
        Settle();
        Assert.False(_w.IsPanelOpen(SidePanel.Headings));
    }
}

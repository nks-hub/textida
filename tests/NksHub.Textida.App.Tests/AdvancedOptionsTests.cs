using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>The "Advanced" settings (P9.2.1): every one is listed here with the test that shows its effect.</summary>
[Collection("LocState")]
public class AdvancedOptionsTests : IDisposable
{
    // OPEN: list to confirm. The owner has not approved the set of advanced options yet; a new option is added here together with its effect test.
    private static readonly Dictionary<string, string> Covered = new()
    {
        ["advanced.scrollPastEnd"] = nameof(Scroll_past_end_reaches_the_editor),
        ["advanced.cutCopyWholeLine"] = nameof(Cut_copy_whole_line_reaches_the_editor),
        ["advanced.hideMouseWhileTyping"] = nameof(Hide_mouse_while_typing_reaches_the_editor),
        ["advanced.linkNeedsCtrl"] = nameof(Link_needs_ctrl_reaches_the_editor),
        ["advanced.wrapIndent"] = nameof(Wrap_indent_reaches_the_editor),
        ["advanced.recentFilesCount"] = nameof(Recent_files_count_cuts_the_list),
        ["advanced.autosaveIntervalSeconds"] = nameof(Autosave_interval_comes_from_the_setting),
    };

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AdvancedOptionsTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void Every_advanced_option_has_an_effect_test()
    {
        var advanced = WorkbenchHost.CreateBuiltin().Settings.Definitions.Where(d => d.Category == "cat.advanced").Select(d => d.Key).Order(StringComparer.Ordinal).ToList();
        var listed = Covered.Keys.Order(StringComparer.Ordinal).ToList();
        var missing = advanced.Except(listed).ToList();
        var stale = listed.Except(advanced).ToList();
        Assert.True(missing.Count == 0 && stale.Count == 0,
            $"Advanced options without a test entry: [{string.Join(", ", missing)}]; test entries without an option: [{string.Join(", ", stale)}].");
        Assert.All(Covered.Values, name => Assert.NotNull(typeof(AdvancedOptionsTests).GetMethod(name)));
    }

    private EditorSurface Surface() => _w.OpenFile(_temp.File("a.txt", "one two"), null).Editor!;

    [AvaloniaFact]
    public void Scroll_past_end_reaches_the_editor()
    {
        var surface = Surface();
        Assert.False(surface.Options.AllowScrollBelowDocument);
        Assert.Null(_app.Workbench.Set("advanced.scrollPastEnd", JsonValue.Create(true)));
        Assert.True(surface.Options.AllowScrollBelowDocument);
    }

    [AvaloniaFact]
    public void Cut_copy_whole_line_reaches_the_editor()
    {
        var surface = Surface();
        Assert.False(surface.Options.CutCopyWholeLine);
        Assert.Null(_app.Workbench.Set("advanced.cutCopyWholeLine", JsonValue.Create(true)));
        Assert.True(surface.Options.CutCopyWholeLine);
    }

    [AvaloniaFact]
    public void Hide_mouse_while_typing_reaches_the_editor()
    {
        var surface = Surface();
        Assert.True(surface.Options.HideCursorWhileTyping);
        Assert.Null(_app.Workbench.Set("advanced.hideMouseWhileTyping", JsonValue.Create(false)));
        Assert.False(surface.Options.HideCursorWhileTyping);
    }

    [AvaloniaFact]
    public void Link_needs_ctrl_reaches_the_editor()
    {
        var surface = Surface();
        Assert.True(surface.Options.RequireControlModifierForHyperlinkClick);
        Assert.Null(_app.Workbench.Set("advanced.linkNeedsCtrl", JsonValue.Create(false)));
        Assert.False(surface.Options.RequireControlModifierForHyperlinkClick);
    }

    [AvaloniaFact]
    public void Wrap_indent_reaches_the_editor()
    {
        var surface = Surface();
        Assert.Equal(0, surface.Options.WordWrapIndentation);
        Assert.Null(_app.Workbench.Set("advanced.wrapIndent", JsonValue.Create(8)));
        Assert.Equal(8, surface.Settings.WrapIndent);
        Assert.True(surface.Options.WordWrapIndentation > 0);
    }

    [AvaloniaFact]
    public void Recent_files_count_cuts_the_list()
    {
        Assert.Null(_app.Workbench.Set("advanced.recentFilesCount", JsonValue.Create(2)));
        foreach (var name in new[] { "r1.txt", "r2.txt", "r3.txt" }) _app.Recent.Add(_temp.File(name, "x"));
        Assert.Equal(2, _app.Recent.Files.Count);
        Assert.EndsWith("r3.txt", _app.Recent.Files[0]);
    }

    [AvaloniaFact]
    public async Task Autosave_interval_comes_from_the_setting()
    {
        Assert.Null(_app.Workbench.Set("advanced.autosaveIntervalSeconds", JsonValue.Create(1)));
        _app.Workbench.Set("files.autoSave", "afterDelay");
        var path = _temp.File("auto.txt", "a");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "b");

        for (int i = 0; i < 80 && File.ReadAllText(path) == "a"; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("ab", File.ReadAllText(path));
    }
}

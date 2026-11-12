using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

public partial class ViewOptionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slow_frames_during_a_minimap_drag_keep_colors_deferred_until_release(bool paged)
    {
        var tab = Open("slow-drag.cs", KeywordFile(paged ? 100_000 : 5000), paged);
        var map = tab.Minimap!;
        var syntax = tab.Editor?.Syntax ?? tab.Paged!.Syntax;
        Assert.True(syntax.Active);
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 10);
        var last = start;
        _w.MouseDown(start, MouseButton.Left);
        int editorRuns = syntax.ViewportRuns, mapRuns = map.ColorRuns;
        try
        {
            for (int i = 1; i <= 2; i++)
            {
                Thread.Sleep(PluginLimits.SyntaxJumpSettleMilliseconds + 40);
                last = new Point(start.X, origin.Y + 100 + i * 120);
                _w.MouseMove(last);
                _w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(editorRuns, syntax.ViewportRuns);
                Assert.Equal(mapRuns, map.ColorRuns);
            }
        }
        finally { _w.MouseUp(last, MouseButton.Left); }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3)
            && (syntax.ViewportRuns == editorRuns || (!map.IsSampled && map.ColorRuns == mapRuns) || !EditorHasVisibleSyntaxColors(tab)))
        {
            Thread.Sleep(15);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(syntax.ViewportRuns > editorRuns, "Editor colors did not resume after the drag.");
        Assert.True(map.IsSampled || map.ColorRuns > mapRuns, "Minimap colors did not resume after the drag.");
        Assert.True(EditorHasVisibleSyntaxColors(tab), "The visible editor text stayed uncolored after the drag.");
        Assert.True(WaitForColors(tab, ["keyword", "comment"]), "The minimap stayed uncolored after the drag.");
        IMinimapHost host = tab.Editor is { } editor ? editor : tab.Paged!;
        Assert.True(host.Visible.First > 0, "The held pointer did not move the editor.");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_the_minimap_during_a_drag_resumes_editor_colors(bool paged)
    {
        var tab = Open("removed-map.cs", KeywordFile(paged ? 100_000 : 5000), paged);
        var map = tab.Minimap!;
        var syntax = tab.Editor?.Syntax ?? tab.Paged!.Syntax;
        var origin = map.TranslatePoint(default, _w)!.Value;
        var start = new Point(origin.X + 40, origin.Y + 10);
        var target = new Point(start.X, origin.Y + 340);
        _w.MouseDown(start, MouseButton.Left);
        int runs = syntax.ViewportRuns;
        try
        {
            _w.MouseMove(target);
            Assert.True(syntax.IsDragging);
            Assert.Equal(runs, syntax.ViewportRuns);
            var parent = Assert.IsAssignableFrom<Panel>(map.Parent);
            Assert.True(parent.Children.Remove(map));
            _w.UpdateLayout();
            Assert.False(syntax.IsDragging);
            var clock = Stopwatch.StartNew();
            while ((syntax.ViewportRuns == runs || !EditorHasVisibleSyntaxColors(tab)) && clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                Thread.Sleep(15);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(syntax.ViewportRuns > runs, "Removing the minimap left the editor colors deferred.");
            Assert.True(EditorHasVisibleSyntaxColors(tab), "Removing the minimap left the visible editor text uncolored.");
        }
        finally { _w.MouseUp(target, MouseButton.Left); }
    }

    private bool EditorHasVisibleSyntaxColors(DocTab tab)
    {
        Control surface = tab.Editor is { } editor ? editor : tab.Paged!;
        var area = InWindow(surface, new Rect(0, 0, surface.Bounds.Width, Math.Min(100, surface.Bounds.Height)));
        return Matching(Capture(), area, SyntaxColors(["keyword", "comment"]).ToList()) > 20;
    }
}

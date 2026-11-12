using System.Diagnostics;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Tests;

public partial class ViewOptionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slow_frames_during_a_minimap_drag_keep_colors_deferred_until_release(bool paged)
    {
        var tab = Open("slow-drag.cs", KeywordFile(5000), paged);
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
            && (syntax.ViewportRuns == editorRuns || (!map.IsSampled && map.ColorRuns == mapRuns)))
        {
            Thread.Sleep(15);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(syntax.ViewportRuns > editorRuns, "Editor colors did not resume after the drag.");
        Assert.True(map.IsSampled || map.ColorRuns > mapRuns, "Minimap colors did not resume after the drag.");
        IMinimapHost host = tab.Editor is { } editor ? editor : tab.Paged!;
        Assert.True(host.Visible.First > 0, "The held pointer did not move the editor.");
    }
}

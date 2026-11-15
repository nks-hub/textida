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
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

public partial class ViewOptionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Slow_frames_during_a_minimap_drag_keep_colors_deferred_until_release(bool paged)
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
            await Task.Delay(15);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(syntax.ViewportRuns > editorRuns, "Editor colors did not resume after the drag.");
        Assert.True(map.IsSampled || map.ColorRuns > mapRuns, "Minimap colors did not resume after the drag.");
        bool editorColored = EditorHasVisibleSyntaxColors(tab);
        Assert.True(editorColored, editorColored ? null : "The visible editor text stayed uncolored after the drag. " + ColorRecoveryState(tab));
        bool mapColored = await WaitForMinimapColorsAsync(tab);
        Assert.True(mapColored, mapColored ? null : "The minimap stayed uncolored after the drag. " + ColorRecoveryState(tab));
        IMinimapHost host = tab.Editor is { } editor ? editor : tab.Paged!;
        Assert.True(host.Visible.First > 0, "The held pointer did not move the editor.");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removing_the_minimap_during_a_drag_resumes_editor_colors(bool paged)
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
                await Task.Delay(15);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(syntax.ViewportRuns > runs, "Removing the minimap left the editor colors deferred.");
            bool colored = EditorHasVisibleSyntaxColors(tab);
            Assert.True(colored, colored ? null : "Removing the minimap left the visible editor text uncolored. " + ColorRecoveryState(tab));
        }
        finally { _w.MouseUp(target, MouseButton.Left); }
    }

    private bool EditorHasVisibleSyntaxColors(DocTab tab)
    {
        Control surface = tab.Editor is { } editor ? editor : tab.Paged!;
        var area = InWindow(surface, new Rect(0, 0, surface.Bounds.Width, Math.Min(100, surface.Bounds.Height)));
        return Matching(Capture(settle: false), area, SyntaxColors(["keyword", "comment"]).ToList()) > 20;
    }

    private async Task<bool> WaitForMinimapColorsAsync(DocTab tab)
    {
        var colors = SyntaxColors(["keyword", "comment"]).ToList();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 10)
        {
            var area = MinimapLowerPart(tab);
            if (Matching(Capture(settle: false), area, colors) > 20) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private string ColorRecoveryState(DocTab tab)
    {
        Control surface = tab.Editor is { } editor ? editor : tab.Paged!;
        IMinimapHost host = tab.Editor is { } editorHost ? editorHost : tab.Paged!;
        var syntax = tab.Editor?.Syntax ?? tab.Paged!.Syntax;
        var visible = host.Visible;
        var shot = Capture(settle: false);
        var area = InWindow(surface, new Rect(0, 0, surface.Bounds.Width, Math.Min(100, surface.Bounds.Height)));
        int pixels = Matching(shot, area, SyntaxColors(["keyword", "comment"]).ToList());
        var state = syntax.CaptureReadiness(tab.Doc.Doc);
        string pagedState = "";
        if (tab.Paged is { } paged && tab.Doc.Doc.Backend is PagedBackend backend)
        {
            var cache = paged.CaptureReadiness(tab.Doc.Doc, backend);
            pagedState = $"\npaged current={cache.CurrentCache} rows={cache.CachedRows}/{cache.VisibleRows} long={cache.LongRows} "
                + $"columns={cache.CompleteColumnIndexes}/{cache.RequiredColumnIndexes} windows={cache.CachedWindows}/{cache.WindowRows} detour={cache.Detour}"
                + $"\npaged viewport={paged.CaptureReadinessViewport()} windowsPending={cache.WindowsPending} columnWork={cache.ColumnWork} "
                + $"columnFailed={cache.ColumnFailed} indexTimer={cache.IndexTimerPending}";
            var rows = Enumerable.Range(0, (int)Math.Min(6, visible.Count)).Select(offset =>
            {
                long line = visible.First + offset;
                string text = paged.DisplayOf(line) ?? "<unavailable>";
                var tokens = paged.TokensOf(line);
                return $"{line}:text={text[..Math.Min(80, text.Length)]},tokens={string.Join(',', tokens?.Select(token => $"{token.Start}+{token.Length}:{token.Kind}") ?? [])}";
            });
            pagedState += $"\nrows=[{string.Join("\n", rows)}]";
        }
        return $"\npixels={pixels} area={area} surface={surface.Bounds} visible={surface.IsEffectivelyVisible} host={visible}"
            + $"\nframeRows={shot.Data.Length / shot.Stride} stride={shot.Stride} window={_w.ClientSize} mapRuns={tab.Minimap?.ColorRuns} mapLayout={tab.Minimap?.Layout}"
            + $"\nsyntax runs={syntax.ViewportRuns} requested={syntax.Requested} sessionRev={state.SessionRevision} doneRev={state.DoneRevision} "
            + $"matches={state.RequestedMatches} pending={state.Pending} scheduled={state.Scheduled} dragging={syntax.IsDragging}{pagedState}";
    }
}

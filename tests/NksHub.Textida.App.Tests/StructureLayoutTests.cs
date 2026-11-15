using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureLayoutTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public StructureLayoutTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
        window = app.CreateWindow(null);
        window.Width = 1200; window.Height = 800;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    private void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private async Task Drain(StructureView view)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            Layout();
            Task current = view.Working;
            if (current.IsCompleted)
            {
                await current;
                Layout();
                if (ReferenceEquals(current, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The structure layout fixture did not finish reading its document.");
    }

    private static Rect BoundsIn(Control target, Visual reference)
    {
        var position = target.TranslatePoint(default, reference);
        Assert.NotNull(position);
        return new(position.Value, target.Bounds.Size);
    }

    private static void AssertContained(Rect available, Rect actual)
    {
        Assert.True(actual.Width > 0 && actual.Height > 0, $"Empty target bounds: {actual}");
        Assert.True(available.Inflate(0.5).Contains(actual), $"Target {actual} exceeds viewport {available}");
    }

    private async Task Reveal(Control target, ScrollContentPresenter viewport)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            target.BringIntoView();
            Layout();
            var bounds = BoundsIn(target, viewport);
            if (bounds.Width > 0 && bounds.Height > 0 && new Rect(viewport.Bounds.Size).Inflate(0.5).Contains(bounds)) return;
            await Task.Delay(10);
        }
        AssertContained(new Rect(viewport.Bounds.Size), BoundsIn(target, viewport));
    }

    [AvaloniaTheory]
    [InlineData("en", false)]
    [InlineData("de", false)]
    [InlineData("cs", false)]
    [InlineData("en", true)]
    [InlineData("de", true)]
    [InlineData("cs", true)]
    public async Task Default_right_width_and_bottom_height_keep_controls_scrollable_and_the_tree_inside_its_half(string language, bool bottom)
    {
        Loc.SetLanguage(language);
        var tab = window.OpenFile(temporary.File("layout.xml", "<r attr='old'><![CDATA[value]]></r>"), null);
        window.SelectTab(tab); window.ShowStructurePanel();
        if (bottom) window.MovePanel(SidePanel.Structure, PanelDock.Bottom);
        var view = window.StructurePanelView;
        await Drain(view);
        var host = window.PanelHost(bottom ? PanelDock.Bottom : PanelDock.Right);
        Assert.Equal(language, Loc.Language);
        if (bottom) Assert.Equal(PanelLayout.DefaultHeight, host.Bounds.Height);
        else Assert.Equal(PanelLayout.DefaultWidth, host.Bounds.Width);
        Assert.Single(view.Tree.Items.OfType<StructureTreeNode>().Where(item => item.Tag is StructureNode));
        Assert.True(view.CopyPathButton.IsEnabled);
        Assert.True(view.EditButton.IsEnabled);

        var clear = view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Loc.Get(K.StructureClear)));
        Assert.False(BoundsIn(view.SearchScope, view).Intersects(BoundsIn(view.Filter, view)));
        Assert.False(BoundsIn(view.Filter, view).Intersects(BoundsIn(view.FindButton, view)));
        Assert.False(BoundsIn(view.FindButton, view).Intersects(BoundsIn(clear, view)));
        Rect panelBounds = new(view.Bounds.Size);
        Rect treeBounds = BoundsIn(view.Tree, view);
        AssertContained(panelBounds, treeBounds);
        Assert.True(treeBounds.Height >= view.Bounds.Height / 2 - 1);

        var scroll = view.Filter.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewport = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>()
            .Single(item => ReferenceEquals(item.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(), scroll));
        view.TypeCounts.IsExpanded = true;
        Layout();
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        var counts = Assert.IsType<StackPanel>(view.TypeCounts.Content).Children.OfType<TextBlock>().ToArray();
        Assert.Equal(Enum.GetValues<StructureKind>().Length, counts.Length);
        Control[] targets =
        [
            view.SearchScope, view.Filter, view.FindButton, clear,
            view.CopyPathButton, view.CopySourceButton, view.EditButton,
        ];

        AssertContained(panelBounds, treeBounds);
        Assert.True(treeBounds.Height >= view.Bounds.Height / 2 - 1);

        foreach (var target in targets)
        {
            await Reveal(target, viewport);
            AssertContained(new Rect(viewport.Bounds.Size), BoundsIn(target, viewport));
            Assert.Equal(0, scroll.Offset.X);
            Assert.Equal(treeBounds, BoundsIn(view.Tree, view));
            var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window);
            Assert.NotNull(centre);
            var hit = window.GetVisualAt(centre.Value, visual =>
                visual is not IInputElement input || input.IsHitTestVisible && input.IsEffectivelyVisible);
            Assert.NotNull(hit);
            Assert.True(ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target),
                $"The centre of {target.GetType().Name} hits {hit.GetType().Name} instead.");
        }

        foreach (var count in counts)
        {
            await Reveal(count, viewport);
            AssertContained(new Rect(viewport.Bounds.Size), BoundsIn(count, viewport));
            Assert.Equal(treeBounds, BoundsIn(view.Tree, view));
        }
    }
}

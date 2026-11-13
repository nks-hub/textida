using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class TitleBarHitTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir temp = new();
    private App? app;

    public void Dispose()
    {
        if (app is not null) TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temp.Dispose();
    }

    [AvaloniaFact]
    public void The_empty_mac_tab_row_hits_the_title_bar_but_tabs_and_close_buttons_do_not()
    {
        app = TestApp.Prepare(temp);
        var window = app.CreateWindow(null, look: new ShellLook(false, true, true));
        window.OpenFile(temp.File("sample.txt", "sample\n"), null);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);

        var group = Assert.Single(window.Groups);
        var item = Assert.Single(group.Strip.Items.OfType<TabStripItem>());
        var close = item.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("tabclose"));
        var title = window.GetVisualDescendants().OfType<Border>().Single(border =>
            border.Classes.Contains("tabrow") && WindowDecorationProperties.GetElementRole(border) == WindowDecorationsElementRole.TitleBar);
        Assert.True(title.Bounds.Height > 0);

        Visual Hit(Control control, Point inside, string name)
        {
            var point = control.TranslatePoint(inside, window);
            Assert.NotNull(point);
            var hit = window.GetVisualAt(point.Value, visual =>
                visual is not IInputElement input || input.IsHitTestVisible && input.IsEffectivelyVisible);
            Assert.NotNull(hit);
            output.WriteLine($"{name}: {hit.GetType().Name}, role={WindowDecorationProperties.GetElementRole(hit)}, point={point.Value}");
            return hit;
        }

        var tabHit = Hit(item, new Point(item.Bounds.Width / 3, item.Bounds.Height / 2), "tab");
        Assert.True(ReferenceEquals(tabHit, item) || tabHit.GetVisualAncestors().Contains(item));
        Assert.NotEqual(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(tabHit));

        var closeHit = Hit(close, new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), "close");
        Assert.True(ReferenceEquals(closeHit, close) || closeHit.GetVisualAncestors().Contains(close));
        Assert.NotEqual(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(closeHit));

        var itemRight = item.TranslatePoint(new Point(item.Bounds.Width, 0), group.Strip);
        Assert.NotNull(itemRight);
        Assert.True(group.Strip.Bounds.Width > itemRight.Value.X + 20);
        var empty = new Point((itemRight.Value.X + group.Strip.Bounds.Width) / 2, group.Strip.Bounds.Height / 2);
        var emptyHit = Hit(group.Strip, empty, "empty tab row");
        Assert.Equal(WindowDecorationsElementRole.TitleBar, WindowDecorationProperties.GetElementRole(emptyHit));
        Assert.Same(title, emptyHit);
    }
}

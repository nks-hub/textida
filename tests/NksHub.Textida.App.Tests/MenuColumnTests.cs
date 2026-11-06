using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.17: the check mark and the icon of a menu row share one narrow column, and a menu without either draws no column.</summary>
[Collection("LocState")]
public class MenuColumnTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public MenuColumnTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static T Part<T>(MenuItem item, string name) where T : Control => item.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    /// <summary>Where the text of a row starts inside the row, after the column of the check mark and the icon.</summary>
    private static double TextStart(MenuItem item) => Part<ContentPresenter>(item, "PART_HeaderPresenter").Bounds.X;

    private void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_menu_without_icons_or_check_marks_draws_no_column()
    {
        _w.NewDocument();
        var flyout = _w.ShowEolMenu()!;
        Layout();
        var rows = flyout.Items.OfType<MenuItem>().ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(0, TextStart(r)));
        flyout.Hide();
    }

    [AvaloniaFact]
    public void A_check_mark_stands_in_the_column_of_the_icons_and_the_column_is_16_px_and_a_small_gap()
    {
        _w.NewDocument();
        var flyout = _w.ShowZoomMenu(); // Reset Zoom without icon, the levels with a radio mark on the current one
        Layout();
        var rows = flyout.Items.OfType<MenuItem>().ToList();
        var current = rows.Single(r => r.IsChecked);
        var toggle = Part<ContentControl>(current, "PART_ToggleIconPresenter");
        Assert.True(toggle.IsVisible);
        Assert.Equal(Grid.GetColumn(Part<ContentControl>(current, "PART_IconPresenter")), Grid.GetColumn(toggle)); // the column of the icons
        Assert.Equal(4, ((Grid)toggle.GetVisualParent()!).ColumnDefinitions.Count); // mark and icon, text, keys, arrow
        Assert.All(rows, r => Assert.Equal(22, TextStart(r))); // 16 px mark, 6 px gap, the same for the rows without a mark
        flyout.Hide();
    }

    [AvaloniaFact]
    public void In_a_menu_with_icons_and_check_marks_both_share_one_column()
    {
        var withIcon = new MenuItem { Header = "Icon", Icon = CommandIcons.Create(NksHub.Textida.Workbench.Icons.BuiltinIcons.All["search"]) };
        var checkedRow = new MenuItem { Header = "Checked", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true, Icon = CommandIcons.Create(NksHub.Textida.Workbench.Icons.BuiltinIcons.All["save"]) };
        var plain = new MenuItem { Header = "Plain" };
        var flyout = new MenuFlyout { Items = { withIcon, checkedRow, plain } };
        _w.NewDocument(); // the status bar item the flyout opens at shows with a document
        flyout.ShowAt(_w.ZoomItem);
        Layout();

        var icon = Part<ContentControl>(withIcon, "PART_IconPresenter");
        var mark = Part<ContentControl>(checkedRow, "PART_ToggleIconPresenter");
        Assert.True(icon.IsVisible && mark.IsVisible);
        Assert.Equal(icon.Bounds.X, mark.Bounds.X); // one column
        Assert.Equal(16, icon.Bounds.Width);
        Assert.False(Part<ContentControl>(checkedRow, "PART_IconPresenter").IsVisible); // the mark takes the place of the icon
        Assert.All(new[] { withIcon, checkedRow, plain }, r => Assert.Equal(22, TextStart(r)));
        flyout.Hide();
    }

    [AvaloniaFact]
    public void The_rows_of_the_View_menu_start_their_text_after_one_narrow_column()
    {
        _w.NewDocument();
        var view = _w.GetVisualDescendants().OfType<Menu>().Single().Items.OfType<MenuItem>().Single(m => Avalonia.Automation.AutomationProperties.GetName(m) == "Zobrazit");
        view.IsSubMenuOpen = true;
        Layout();
        var rows = view.Items.OfType<MenuItem>().Where(r => r.IsVisible).ToList();
        Assert.Contains(rows, r => r.Icon is not null);
        Assert.All(rows, r => Assert.Equal(22, TextStart(r)));
        view.IsSubMenuOpen = false;
    }

    [AvaloniaFact]
    public void The_command_palette_has_its_own_picture_and_not_the_prompt_of_a_terminal()
    {
        var icon = _app.Workbench.Commands.IconOf("nks.view.commandPalette")!;
        Assert.Equal(NksHub.Textida.Workbench.Icons.BuiltinIcons.All["command-palette"].Paths, icon.Paths);
        Assert.NotEqual(NksHub.Textida.Workbench.Icons.BuiltinIcons.All["palette"].Paths, icon.Paths);
    }
}

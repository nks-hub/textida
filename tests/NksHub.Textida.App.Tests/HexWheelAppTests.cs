using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The width of the hex row: Ctrl+wheel, fit to the window, and the font size beside it (Ctrl+Shift+wheel, zoom without the old cap).</summary>
[Collection("LocState")]
public class HexWheelAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly HexView _hex;

    public HexWheelAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 3600;
        _w.Height = 700;
        var bytes = new byte[8192];
        new Random(7).NextBytes(bytes);
        bytes[0] = 0;
        _w.OpenFile(_temp.Bytes("wheel.bin", bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
        _hex = _w.Active!.Hex!;
        _hex.SetBytesPerRow(16);
        _hex.SetGroup(1);
        _w.UpdateLayout();
    }

    public void Dispose()
    {
        _hex.SetFitWidth(false);
        _hex.SetBytesPerRow(16);
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private Point Inside => _hex.TranslatePoint(new Point(200, 100), _w)!.Value;

    private void Wheel(double y, RawInputModifiers mods)
    {
        _w.MouseWheel(Inside, new Vector(0, y), mods);
        _w.UpdateLayout();
    }

    private void Resize(double width)
    {
        _w.Width = width;
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
    }

    private bool CaretRowShown => _hex.Caret / _hex.BytesPerRow >= _hex.FirstRow && _hex.Caret / _hex.BytesPerRow < _hex.FirstRow + _hex.VisibleRows;

    [AvaloniaFact]
    public void Ctrl_wheel_changes_the_row_by_one_block_and_keeps_the_caret_row_on_screen()
    {
        _hex.MoveTo(1500);
        _w.UpdateLayout();
        Assert.True(CaretRowShown);
        Wheel(1, TestApp.Mod);
        Assert.Equal(24, _hex.BytesPerRow);
        Assert.True(CaretRowShown);
        Wheel(-1, TestApp.Mod);
        Assert.Equal(16, _hex.BytesPerRow);
        Assert.True(CaretRowShown);
        Wheel(-1, TestApp.Mod);
        Assert.Equal(8, _hex.BytesPerRow);
        Wheel(-1, TestApp.Mod);
        Assert.Equal(8, _hex.BytesPerRow);
    }

    [AvaloniaFact]
    public void The_row_goes_past_64_when_the_view_is_wide_enough_and_stops_at_its_width()
    {
        for (int i = 0; i < 12; i++) Wheel(1, TestApp.Mod);
        Assert.True(_hex.BytesPerRow > 64, _hex.BytesPerRow.ToString());
        for (int i = 0; i < 200; i++) Wheel(1, TestApp.Mod);
        int widest = _hex.BytesPerRow;
        Assert.Equal(0, widest % 8);
        Assert.True(widest <= _hex.MaxBytesPerRow());
        Assert.True(widest + 8 > _hex.MaxBytesPerRow());
        Wheel(1, TestApp.Mod);
        Assert.Equal(widest, _hex.BytesPerRow);

        Resize(900);
        int before = _hex.BytesPerRow;
        Wheel(1, TestApp.Mod);
        Assert.Equal(before, _hex.BytesPerRow);
    }

    [AvaloniaFact]
    public void Fit_width_follows_the_window_in_multiples_of_the_group_and_a_choice_by_hand_ends_it()
    {
        _hex.SetGroup(4);
        _hex.SetFitWidth(true);
        _w.UpdateLayout();
        int wide = _hex.BytesPerRow;
        Assert.True(_hex.FitWidth);
        Assert.Equal(0, wide % 4);
        Assert.Equal(_hex.MaxBytesPerRow(), wide);

        Resize(2000);
        Assert.True(_hex.BytesPerRow < wide, $"{_hex.BytesPerRow} < {wide}");
        Assert.Equal(0, _hex.BytesPerRow % 4);

        Resize(3600);
        Assert.Equal(wide, _hex.BytesPerRow);

        Wheel(-1, TestApp.Mod);
        Assert.False(_hex.FitWidth);
        Assert.Equal(wide - 8, _hex.BytesPerRow);
        Resize(2000);
        Assert.Equal(wide - 8, _hex.BytesPerRow);

        _hex.SetFitWidth(true);
        _hex.SetBytesPerRow(32);
        Assert.False(_hex.FitWidth);
    }

    [AvaloniaFact]
    public void Ctrl_shift_wheel_changes_the_font_size_and_the_view_measures_again()
    {
        double size = _app.Workbench.Settings.GetNumber("editor.fontSize");
        Wheel(1, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal(size + 1, _app.Workbench.Settings.GetNumber("editor.fontSize"));
        Assert.Equal(size + 1, _hex.Options.FontSize);
        Assert.Equal(16, _hex.BytesPerRow);
        Wheel(-1, TestApp.Mod | RawInputModifiers.Shift);
        Assert.Equal(size, _hex.Options.FontSize);
        _w.Zoom(2);
        Assert.Equal(size + 2, _hex.Options.FontSize);
        _w.ZoomReset();
        Assert.Equal(size, _hex.Options.FontSize);
    }

    [AvaloniaFact]
    public void Fit_width_is_reapplied_when_the_font_size_changes()
    {
        _hex.SetFitWidth(true);
        _w.UpdateLayout();
        int before = _hex.BytesPerRow;
        _w.Zoom(6);
        _w.UpdateLayout();
        Assert.True(_hex.BytesPerRow < before, $"{_hex.BytesPerRow} < {before}");
        _w.ZoomReset();
    }

    [AvaloniaFact]
    public void Zoom_goes_above_72_points()
    {
        _app.Workbench.Set("editor.fontSize", System.Text.Json.Nodes.JsonValue.Create(72));
        _w.Zoom(1);
        Assert.Equal(73, _hex.Options.FontSize);
        _w.Zoom(30);
        Assert.Equal(103, _hex.Options.FontSize);
        _w.ZoomReset();
    }

    [AvaloniaFact]
    public void The_plain_wheel_still_scrolls()
    {
        long first = _hex.FirstRow;
        Wheel(-1, RawInputModifiers.None);
        Assert.Equal(first + 3, _hex.FirstRow);
        Assert.Equal(16, _hex.BytesPerRow);
        Wheel(1, RawInputModifiers.None);
        Assert.Equal(first, _hex.FirstRow);
    }
}

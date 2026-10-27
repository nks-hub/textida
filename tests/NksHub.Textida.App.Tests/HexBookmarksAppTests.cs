using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.6.3 in the window: bookmarks of the hex view with the keys of the text bookmarks, the panel, the dialog and the export and import.</summary>
[Collection("LocState")]
public class HexBookmarksAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexBookmarksAppTests()
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

    private DocTab OpenHex(int length = 256)
    {
        _w.OpenFile(_temp.Bytes("marks.bin", Enumerable.Range(0, length).Select(i => (byte)i).ToArray()), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        _w.Active.Hex!.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void Ctrl_f2_marks_the_selection_removes_it_again_and_f2_steps_between_the_bookmarks()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Select(16, 20);
        TestApp.Press(_w, Key.F2, TestApp.Mod);
        var mark = Assert.Single(hex.Marks.Items);
        Assert.Equal(new ByteRange(16, 20), mark.Range);
        Assert.Contains("4", _w.StatusLeftText);

        hex.Select(100, 108);
        TestApp.Press(_w, Key.F2, TestApp.Mod);
        Assert.Equal(2, hex.Marks.Count);

        hex.MoveTo(0);
        TestApp.Press(_w, Key.F2);
        Assert.Equal(new ByteRange(16, 20), hex.Editor.Selection);
        TestApp.Press(_w, Key.F2);
        Assert.Equal(new ByteRange(100, 108), hex.Editor.Selection);
        TestApp.Press(_w, Key.F2);
        Assert.Equal(16, hex.Editor.Selection.Start); // wraps around
        TestApp.Press(_w, Key.F2, RawInputModifiers.Shift);
        Assert.Equal(100, hex.Editor.Selection.Start);

        hex.Select(100, 108);
        TestApp.Press(_w, Key.F2, TestApp.Mod); // exactly the bookmark: removes it
        Assert.Equal([new ByteRange(16, 20)], hex.Marks.Items.Select(b => b.Range));
        hex.MoveTo(17);
        TestApp.Press(_w, Key.F2, TestApp.Mod); // inside the remaining one, no selection: removes it
        Assert.Equal(0, hex.Marks.Count);
    }

    [AvaloniaFact]
    public void A_bookmark_made_from_the_data_inspector_takes_its_type_and_length()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.MoveTo(32);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.I, TestApp.Mod);
        Pump();
        _w.UpdateLayout();
        Pump();
        hex.Inspector.Rows.SelectedIndex = 5; // uint32
        hex.Focus();
        TestApp.Press(_w, Key.F2, TestApp.Mod);
        var mark = Assert.Single(hex.Marks.Items);
        Assert.Equal((new ByteRange(32, 36), "uint32 LE"), (mark.Range, mark.Type));
    }

    [AvaloniaFact]
    public async Task Bytes_inserted_before_a_bookmark_move_it_and_the_status_names_it()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Select(40, 44);
        TestApp.Press(_w, Key.F2, TestApp.Mod);
        _dialogs.BookmarkForms.Enqueue(new HexBookmarkForm("length", "#0F7B6C", "four bytes", null, ""));
        await _w.EditHexBookmarkAsync(hex);
        hex.Editor.Insert = true;
        hex.MoveTo(0);
        Assert.True(hex.Editor.Write(new ByteRange(0, 0), [1, 2, 3]).Applied);
        var mark = Assert.Single(hex.Marks.Items);
        Assert.Equal(new ByteRange(43, 47), mark.Range);
        Assert.Equal(("length", "#0F7B6C", "four bytes"), (mark.Name, mark.Color, mark.Comment));
        hex.MoveTo(45);
        Assert.Contains(Loc.Get(K.HexBmStatus, ("name", "length")), _w.StatusLeftText);

        _w.Undo();
        Assert.Equal(new ByteRange(40, 44), mark.Range);
    }

    [AvaloniaFact]
    public async Task The_dialog_edits_a_bookmark_and_cancelling_a_new_one_leaves_nothing()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Select(8, 12);
        _dialogs.BookmarkForms.Enqueue(null);
        await _w.EditHexBookmarkAsync(hex);
        Assert.Equal(0, hex.Marks.Count);
        Assert.Single(_dialogs.BookmarkAsked);
        Assert.Contains("0x00000008", _dialogs.BookmarkAsked[0].Region);

        _dialogs.BookmarkForms.Enqueue(new HexBookmarkForm("header", "#B3261E", "magic number", null, ""));
        await _w.EditHexBookmarkAsync(hex);
        var mark = Assert.Single(hex.Marks.Items);
        Assert.Equal(("header", "#B3261E", "magic number"), (mark.Name, mark.Color, mark.Comment));

        _dialogs.BookmarkForms.Enqueue(new HexBookmarkForm("renamed", null, null, null, ""));
        await _w.EditHexBookmarkAsync(hex); // the selection is the bookmark: it is edited, not duplicated
        Assert.Equal(("renamed", null), (hex.Marks.Items.Single().Name, hex.Marks.Items.Single().Color));
    }

    [AvaloniaFact]
    public void The_panel_lists_the_bookmarks_and_the_filter_narrows_them_and_enter_selects_the_region()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Marks.Add(new ByteRange(10, 14), "alpha", "#0F7B6C", "first field", "uint32 LE");
        hex.Marks.Add(new ByteRange(60, 62), "beta", null, null, null);

        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexBookmarkPanel());
        Pump();
        var panel = hex.BookmarkList;
        Assert.True(panel.IsVisible);
        Assert.Equal(2, panel.RowTexts.Count);
        Assert.Contains("alpha", panel.RowTexts[0]);
        Assert.Contains("uint32 LE", panel.RowTexts[0]);
        Assert.Contains("first field", panel.RowTexts[0]);

        panel.FilterBox.Text = "FIELD";
        Pump();
        Assert.Single(panel.RowTexts);
        panel.FilterBox.Text = "";
        Pump();
        panel.Rows.SelectedIndex = 1;
        panel.Rows.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Pump();
        Assert.Equal(new ByteRange(60, 62), hex.Editor.Selection);
    }

    [AvaloniaFact]
    public async Task Export_and_import_through_files_give_back_the_same_bookmarks()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Marks.Add(new ByteRange(10, 14), "a, \"b\"", "#0F7B6C", "line one\nline two", "uint32 LE");
        hex.Marks.Add(new ByteRange(60, 62), "beta");
        var expected = hex.Marks.Capture();
        string csv = Path.Combine(_temp.Path, "marks.csv"), json = Path.Combine(_temp.Path, "marks.json");

        foreach (string path in new[] { csv, json })
        {
            _dialogs.SaveFiles.Enqueue(path);
            await _w.ExportHexBookmarksAsync(hex);
            Assert.True(File.Exists(path));
            hex.Marks.Clear();
            _dialogs.Picked.Clear();
            _dialogs.Picked.Add(path);
            await _w.ImportHexBookmarksAsync(hex);
            Assert.Equal(expected, hex.Marks.Capture());
        }
        Assert.StartsWith("{", File.ReadAllText(json).TrimStart());
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(Path.Combine(_temp.Path, "marks.bin"));
        await _w.ImportHexBookmarksAsync(hex);
        Assert.Contains(Loc.Get(K.HexBmImportFailed, ("reason", "")).Split(':')[0], _w.StatusLeftText);
        Assert.Equal(expected, hex.Marks.Capture());
    }

    [AvaloniaFact]
    public void The_session_carries_the_bookmarks_of_a_tab()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Marks.Add(new ByteRange(10, 14), "kept", "#2D6CDF", "in the session", "uint32 BE");

        var captured = _w.CaptureSession().AllTabs.Single();

        var stored = Assert.Single(captured.HexBookmarks!);
        Assert.Equal(("kept", "#2D6CDF", "in the session", "uint32 BE", 10L, 14L), (stored.Name, stored.Color, stored.Comment, stored.Type, stored.Start, stored.End));
    }

    [AvaloniaFact]
    public void The_clear_command_removes_the_bookmarks_of_the_hex_view_and_a_region_is_drawn()
    {
        var tab = OpenHex();
        var hex = tab.Hex!;
        hex.Marks.Add(new ByteRange(2, 6), "x", "#C77700");
        _w.Width = 900;
        _w.Height = 600;
        _w.UpdateLayout();
        Assert.NotNull(_w.CaptureRenderedFrame());
        Assert.True(_w.ClearBookmarks());
        Assert.Equal(0, hex.Marks.Count);
        Assert.Contains(Loc.Get(K.HexBmCleared), _w.StatusLeftText);
    }
}

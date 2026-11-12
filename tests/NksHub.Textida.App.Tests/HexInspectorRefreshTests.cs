using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class HexInspectorRefreshTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _window;

    public HexInspectorRefreshTests()
    {
        _app = TestApp.Prepare(_temp);
        Loc.SetLanguage("en");
        _window = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private HexView OpenInspector(byte[] bytes)
    {
        _window.OpenFile(_temp.Bytes("values.bin", bytes), null);
        TestApp.Press(_window, Key.K, TestApp.Mod);
        TestApp.Press(_window, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = Assert.IsType<HexView>(_window.Active!.Hex);
        Assert.True(hex.Inspector.FocusList());
        _window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(hex.Inspector.Rows.IsKeyboardFocusWithin);
        return hex;
    }

    [AvaloniaTheory]
    [InlineData(false, "67305985", "134678021")]
    [InlineData(true, "16909060", "84281096")]
    public void An_open_inspector_tracks_the_caret_selection_and_end_of_file(
        bool bigEndian, string atStart, string atFour)
    {
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var hex = OpenInspector(bytes);
        var panel = hex.Inspector;
        var document = _window.Active!.Doc.Doc;
        TestApp.Press(_window, bigEndian ? Key.B : Key.L);
        Assert.Equal(bigEndian, panel.BigEndian);
        Assert.Contains("uint32: " + atStart, panel.RowTexts);
        long revision = document.Revision;

        hex.MoveTo(4);
        Assert.Contains("uint8: 5", panel.RowTexts);
        Assert.Contains("uint32: " + atFour, panel.RowTexts);

        hex.Select(0, 4);
        Assert.Contains("uint8: 1", panel.RowTexts);
        Assert.Contains("uint32: " + atStart, panel.RowTexts);

        hex.MoveTo(15);
        Assert.Contains("uint8: 16", panel.RowTexts);
        Assert.Contains("uint32: " + Loc.Get(K.HexInspectorIncomplete, ("need", 4)), panel.RowTexts);

        hex.MoveTo(0);
        Assert.Contains("uint32: " + atStart, panel.RowTexts);
        Assert.Equal(revision, document.Revision);
        Assert.Equal(0, document.UndoDepth);
        Assert.False(document.IsModified);
        Assert.Equal(bytes, document.ReadBytes(new ByteRange(0, bytes.Length)));
    }

    [AvaloniaFact]
    public void Moving_the_caret_does_not_change_the_length_of_the_value_being_edited()
    {
        byte[] bytes = [0xC3, 0xA9, 0x41, 0x43];
        var hex = OpenInspector(bytes);
        var panel = hex.Inspector;
        var document = _window.Active!.Doc.Doc;
        long revision = document.Revision;
        Assert.False(hex.Insert);
        Assert.Contains("UTF-8: é U+00E9", panel.RowTexts);
        panel.Rows.SelectedIndex = Array.IndexOf(DataInspector.All.ToArray(), InspectorKind.Utf8);
        TestApp.Press(_window, Key.Enter);
        Assert.True(panel.IsEditing);
        var editBox = panel.EditBox;
        Assert.Equal("U+00E9", editBox.Text);

        Assert.True(hex.Focus());
        hex.MoveTo(2);
        Assert.Contains("UTF-8: A U+0041", panel.RowTexts);
        Assert.True(panel.IsEditing);
        Assert.True(editBox.IsVisible);
        Assert.Equal("U+00E9", editBox.Text);
        Assert.True(editBox.Focus());
        editBox.Text = "U+0042";
        Dispatcher.UIThread.RunJobs();
        TestApp.Press(_window, Key.Enter);

        Assert.True(panel.IsEditing);
        Assert.Equal(Loc.Get(K.HexInspectorLength, ("old", 2), ("new", 1)), panel.ErrorShown);
        Assert.Equal(bytes, document.ReadBytes(new ByteRange(0, bytes.Length)));
        Assert.Equal(revision, document.Revision);
        Assert.Equal(0, document.UndoDepth);
        Assert.False(document.IsModified);
        TestApp.Press(_window, Key.Escape);
        Assert.False(panel.IsEditing);
        Assert.False(editBox.IsVisible);
    }
}

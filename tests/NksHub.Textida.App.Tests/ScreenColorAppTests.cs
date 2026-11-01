using Avalonia.Headless.XUnit;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.4.1: the colour picker of the screen inserts or copies what it picked and says why it cannot start; the clipboard monitor is off by default and collects only the copies made after it was switched on.</summary>
[Collection("LocState")]
public class ScreenColorAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public ScreenColorAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task The_picked_colour_goes_to_the_caret_or_to_the_clipboard_and_a_cancel_changes_nothing()
    {
        _app.ScreenColorCheck = () => (ScreenColorSupport.Available, "");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("a.css", "color: ;"), null);
        tab.Editor!.CaretOffset = 7;

        _dialogs.ScreenColors.Enqueue(new Rgba(255, 128, 0));
        await w.PickScreenColorAsync();
        Assert.Equal("color: #ff8000;", tab.Editor.Document.Text);
        Assert.Contains("#ff8000", w.StatusMessageText);

        _dialogs.ScreenColors.Enqueue(null);
        await w.PickScreenColorAsync();
        Assert.Equal("color: #ff8000;", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Without_the_permission_or_a_display_the_picker_says_so_and_does_not_open()
    {
        bool asked = false;
        _app.ScreenColorCheck = () => (ScreenColorSupport.NeedsPermission, "");
        _app.ScreenColorPermission = () => asked = true;
        var w = _app.CreateWindow(null);

        _dialogs.Answers.Enqueue(0);
        await w.PickScreenColorAsync();
        Assert.True(asked);
        Assert.Contains("Screen Recording", _dialogs.Asked[^1].Message.Replace("Nahrávání obrazovky", "Screen Recording"));

        _app.ScreenColorCheck = () => (ScreenColorSupport.Unavailable, "Wayland without X11");
        _dialogs.Answers.Enqueue(0);
        await w.PickScreenColorAsync();
        Assert.Contains("Wayland without X11", _dialogs.Asked[^1].Message);
        Assert.Empty(_dialogs.ScreenColors);
    }

    [AvaloniaFact]
    public async Task The_clipboard_monitor_is_off_by_default_and_collects_only_the_copies_made_after_it_was_switched_on()
    {
        Assert.False(_app.Workbench.Settings.GetBool("editor.clipboardMonitor"));
        string? clipboard = "already there";
        _app.ReadClipboard = () => Task.FromResult(clipboard);
        var w = _app.CreateWindow(null);

        clipboard = "copied while off";
        await _app.ClipboardWatch.PollAsync();
        Assert.Empty(_app.ClipboardHistory.Items);
        Assert.False(_app.ClipboardWatch.Armed);

        w.ToggleClipboardMonitor();
        Assert.True(_app.Workbench.Settings.GetBool("editor.clipboardMonitor"));
        await _app.ClipboardWatch.PollAsync(); // the start: what is there now is not collected
        Assert.Empty(_app.ClipboardHistory.Items);
        clipboard = "copied after";
        await _app.ClipboardWatch.PollAsync();
        await _app.ClipboardWatch.PollAsync(); // the same text twice is one item
        clipboard = "and another";
        await _app.ClipboardWatch.PollAsync();
        Assert.Equal(["and another", "copied after"], _app.ClipboardHistory.Items);

        w.ToggleClipboardMonitor();
        clipboard = "copied while off again";
        await _app.ClipboardWatch.PollAsync();
        w.ToggleClipboardMonitor();
        await _app.ClipboardWatch.PollAsync(); // switched on again: a new start, the text that lay there is not taken
        Assert.Equal(["and another", "copied after"], _app.ClipboardHistory.Items);
    }
}

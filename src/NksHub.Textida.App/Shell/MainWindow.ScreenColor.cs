using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Colors;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The colour picker of the screen and the switch of the clipboard monitor (PZ.4.1). The picker is offered only where the platform lets the program read a pixel (<see cref="ScreenColor"/>): on macOS it needs the Screen Recording permission and says so,
/// on Linux it needs X11 or XWayland. The monitor is the setting <c>editor.clipboardMonitor</c>, off by default.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The picker in progress, for tests.</summary>
    internal Task ScreenColorWork { get; private set; } = Task.CompletedTask;

    internal CommandOutcome PickScreenColor()
    {
        ScreenColorWork = PickScreenColorAsync();
        return CommandOutcome.Handled;
    }

    internal async Task PickScreenColorAsync()
    {
        var (support, reason) = _app.ScreenColorCheck();
        if (support != ScreenColorSupport.Available)
        {
            string message;
            if (support == ScreenColorSupport.NeedsPermission)
            {
                _app.ScreenColorPermission(); // the system asks the user once; a refusal or a later grant needs a new start of the program
                message = Loc.Get(K.ScreenColorPermission);
            }
            else message = Loc.Get(K.ScreenColorUnavailable, ("reason", reason));
            SetStatusMessage(message);
            await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.ScreenColorTitle), message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)]));
            return;
        }
        if (await _app.Dialogs.PickScreenColorAsync(this, _app.ScreenColorReader) is not { } color) return;
        string text = $"#{color.R:x2}{color.G:x2}{color.B:x2}";
        if (ActiveEditor is { IsReadOnly: false })
        {
            RunEdit((s, selections) => AvaloniaEditAdapter.LineOperations.ReplaceSelections(s.Document, selections, _ => text));
            SetStatusMessage(Loc.Get(K.ScreenColorInserted, ("color", text)));
        }
        else
        {
            _ = Clipboard?.SetTextAsync(text);
            SetStatusMessage(Loc.Get(K.ScreenColorCopied, ("color", text)));
        }
    }

    internal CommandOutcome ToggleClipboardMonitor()
    {
        bool on = !_app.Workbench.Settings.GetBool("editor.clipboardMonitor");
        _app.Workbench.Set("editor.clipboardMonitor", JsonValue.Create(on));
        SetStatusMessage(Loc.Get(on ? K.ClipboardMonitorOn : K.ClipboardMonitorOff));
        _menuModel = BuildMenuModel();
        return CommandOutcome.Handled;
    }
}

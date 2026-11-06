using Avalonia.Automation;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The formatted JSON view of the window (P3.1.14): it lies over the text view of the tab like the table view and reads the same document.
/// Switching keeps the place: the caret goes to the line that shows the byte the other view stood on.
/// </summary>
internal sealed partial class MainWindow
{
    private string _lastJsonKey = "";

    internal JsonPrettyView? ActiveJson => Active is { JsonShown: true } tab ? tab.Json : null;

    /// <summary>The command <c>nks.view.jsonPretty</c>: the active document is shown formatted, or as text again.</summary>
    internal CommandOutcome ToggleJsonPretty()
    {
        if (Active is not { Custom: null, Hex: null } tab || tab.Doc.IsResults || tab.Doc.IsHex) return CommandOutcome.NotHandled;
        SetJsonPretty(tab, !tab.JsonShown);
        return CommandOutcome.Handled;
    }

    internal void SetJsonPretty(DocTab tab, bool on)
    {
        if (on == tab.JsonShown) return;
        long offset = on ? TextCaretOffset(tab) : tab.Json!.SourceOffset;
        var before = tab.Json;
        tab.ShowJson(on, OptionsFor(tab));
        if (tab.Json != before) WireJson(tab, tab.Json!);
        if (on) tab.Json!.RevealOffset(offset, offset);
        else GoToByte(tab, offset);
        FocusView(tab);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Active == tab) FocusView(tab); }, Avalonia.Threading.DispatcherPriority.Background);
        UpdateStatus();
        RefreshMenuIfContextChanged();
    }

    private static long TextCaretOffset(DocTab tab)
    {
        if (tab.Paged is { } paged) return paged.CaretOffset;
        if (tab.Surface is { } surface && tab.Doc.TextBackend is { } backend) return backend.ToByteOffset(surface.CaretOffset);
        return tab.Doc.Doc.BomLength;
    }

    private void WireJson(DocTab tab, JsonPrettyView json)
    {
        json.ViewChanged += () => { if (tab == Active) UpdateStatus(); };
        json.Refused += SetStatusMessage;
        AutomationProperties.SetName(json, Loc.Get(K.A11yJsonPretty));
    }

    /// <summary>The command <c>nks.json.goToKey</c>: asks for a key name and moves to its next occurrence after the caret, in the formatted view.</summary>
    internal async Task GoToJsonKeyAsync()
    {
        if (Active is not { Custom: null, Hex: null } tab) return;
        if (!tab.JsonShown) SetJsonPretty(tab, true);
        if (tab.Json is not { } json) return;
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.JsonGoToKeyTitle), Loc.Get(K.JsonGoToKeyLabel), _lastJsonKey) is not { Length: > 0 } key) return;
        _lastJsonKey = key;
        await json.GoToKeyAsync(key);
        if (Active == tab) FocusView(tab);
    }
}

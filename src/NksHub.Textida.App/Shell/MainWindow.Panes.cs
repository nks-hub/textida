using Avalonia.Automation;
using Avalonia.Controls;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What a keyboard user and a screen reader need from the window as a whole: F6 moves the keyboard between its parts (the editor takes Tab for itself),
/// and a message that matters is announced without moving the keyboard.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Not drawn. The status text changes with every caret move and would be read each time, so a message is announced through this live region instead.</summary>
    private readonly TextBlock _announcer = new() { Width = 1, Height = 1, Opacity = 0, IsHitTestVisible = false, ClipToBounds = true };

    /// <summary>The last announced message, for tests.</summary>
    internal string AnnouncedText => _announcer.Text ?? "";

    internal AutomationLiveSetting AnnouncedUrgency => AutomationProperties.GetLiveSetting(_announcer);

    /// <summary>Tells a screen reader a message without moving the keyboard. <paramref name="urgent"/> lets it interrupt what is being read.</summary>
    internal void Announce(string text, bool urgent = false)
    {
        if (text.Length == 0) return;
        AutomationProperties.SetLiveSetting(_announcer, urgent ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        _announcer.Text = ""; // the same message twice is still a change
        _announcer.Text = text;
    }

    /// <summary>The parts of the window F6 goes through, in reading order; one that is closed is left out. Each knows how to take the keyboard.</summary>
    private List<(Control Part, Func<bool> Focus)> Panes()
    {
        var panes = new List<(Control, Func<bool>)>();
        if (_leftHost.IsVisible) panes.Add((_leftHost, FocusLeftPanel));
        panes.Add((_groupsGrid, () => { FocusActiveView(); return Active is not null; }));
        if (_previewPanel.IsVisible) panes.Add((_previewPanel, () => _previewScroll.Focus()));
        if (_agentPanel.IsVisible) panes.Add((_agentPanel, () => _agentInput.Focus()));
        if (_outputPanel.IsVisible) panes.Add((_outputPanel, () => _outputText.Focus()));
        if (_find.IsVisible) panes.Add((_find, () => _find.FindBox.Focus()));
        panes.Add((_statusBar, () => _statusRight.Children.OfType<Button>().FirstOrDefault(b => b.IsVisible && b.IsEffectivelyEnabled)?.Focus() ?? false));
        return panes;
    }

    private bool FocusLeftPanel()
    {
        if (_docPanel.IsVisible) return _docFilter.Focus();
        if (_filePanel.IsVisible) return (_fileTree is null ? (Control)_filesOpenButton : _fileList).Focus();
        if (!_pluginPanel.IsVisible) return false;
        FocusPluginTree();
        return true;
    }

    /// <summary>F6 and Shift+F6: the keyboard goes to the next (or previous) part of the window and wraps around. From a part that is not in the list, such as the menu, it goes to the editor.</summary>
    internal CommandOutcome FocusNextPane(bool forward)
    {
        var panes = Panes();
        int at = panes.FindIndex(p => p.Part.IsKeyboardFocusWithin);
        if (at < 0) at = panes.FindIndex(p => ReferenceEquals(p.Part, _groupsGrid)) + (forward ? -1 : 1);
        for (int step = 1; step <= panes.Count; step++)
        {
            var next = panes[((at + (forward ? step : -step)) % panes.Count + panes.Count) % panes.Count];
            if (next.Focus()) break;
        }
        return CommandOutcome.Handled;
    }
}

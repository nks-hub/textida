using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Terminal;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The terminal panel (PZ.6.1): a shell on a pseudo terminal below the editor. Opening the panel starts the shell of the setting <c>terminal.shell</c> (PowerShell on Windows, <c>$SHELL</c> elsewhere) in the folder of the file panel or of the open
/// file; closing the panel, or the window, ends the shell and everything it started. A shell that ended by itself leaves its last screen and a Restart button.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Border _terminalPanel = new() { IsVisible = false, Height = 240 };
    private readonly TerminalView _terminalView = new();
    private readonly TextBlock _terminalTitle = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(10, 0), FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly Button _terminalRestart = new() { IsVisible = false };
    private readonly Button _terminalClose = new();
    private TerminalSession? _terminal;
    private string _terminalShellName = "";

    public bool TerminalPanelOpen => _terminalPanel.IsVisible;
    internal TerminalView TerminalViewControl => _terminalView;
    internal TerminalSession? TerminalSessionNow => _terminal;

    private Control BuildTerminalPanel()
    {
        _terminalClose.Classes.Add("outputaction");
        _terminalRestart.Classes.Add("outputaction");
        _terminalClose.Click += (_, _) => ToggleTerminal(false);
        _terminalRestart.Click += (_, _) => StartTerminal();
        _terminalView.SessionEnded += code =>
        {
            _terminalTitle.Text = Loc.Get(K.TerminalEnded, ("shell", _terminalShellName), ("code", code));
            _terminalRestart.IsVisible = true;
            SetStatusMessage(_terminalTitle.Text);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(0, 0, 8, 0), Children = { _terminalRestart, _terminalClose } };
        var header = new ReadingDockPanel { Height = 30, LastChildFill = false };
        header.Children.Add(Dock(actions, Avalonia.Controls.Dock.Right));
        header.Children.Add(Dock(_terminalTitle, Avalonia.Controls.Dock.Left));
        _terminalPanel.Classes.Add("outputpanel");
        HelpContext.Set(_terminalPanel, "view.terminal");
        _terminalPanel.Child = new DockPanel { Children = { Dock(header, Avalonia.Controls.Dock.Top), _terminalView } };
        Closed += (_, _) => EndTerminal();
        ApplyTerminalTexts();
        return _terminalPanel;
    }

    private void ApplyTerminalTexts()
    {
        _terminalRestart.Content = Loc.Get(K.TerminalRestart);
        _terminalClose.Content = Loc.Get(K.PanelOutputClose);
        AutomationProperties.SetName(_terminalPanel, Loc.Get(K.TerminalPanel));
        AutomationProperties.SetName(_terminalView, Loc.Get(K.TerminalPanel));
        AutomationProperties.SetName(_terminalRestart, Loc.Get(K.TerminalRestart));
        AutomationProperties.SetName(_terminalClose, Loc.Get(K.PanelOutputClose));
        if (_terminal is { Running: true }) _terminalTitle.Text = _terminalShellName;
    }

    internal CommandOutcome ToggleTerminalCommand()
    {
        ToggleTerminal();
        return CommandOutcome.Handled;
    }

    public void ToggleTerminal(bool? open = null)
    {
        bool show = open ?? !TerminalPanelOpen;
        if (show == TerminalPanelOpen) return;
        if (show)
        {
            _terminalPanel.IsVisible = true;
            _menuModel = BuildMenuModel();
            StartTerminal();
            Dispatcher.UIThread.Post(() => _terminalView.Focus(), DispatcherPriority.Input);
        }
        else
        {
            EndTerminal();
            _terminalPanel.IsVisible = false;
            _menuModel = BuildMenuModel();
            if (Active is { } tab) FocusView(tab);
        }
    }

    private void EndTerminal()
    {
        _terminalView.Detach();
        var session = _terminal;
        _terminal = null;
        session?.Dispose();
    }

    private string TerminalFolder() =>
        FileRootFolder is { } root && Directory.Exists(root) ? root : Active?.Path is { } path && Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Starts the shell of the setting in the terminal panel, ending the one that runs; says in the status bar why when it cannot.</summary>
    internal void StartTerminal()
    {
        EndTerminal();
        _terminalRestart.IsVisible = false;
        string folder = TerminalFolder();
        string setting = _app.Workbench.Settings.GetString("terminal.shell");
        if (PseudoTerminal.ResolveShell(setting, folder) is not { } shell)
        {
            _terminalTitle.Text = Loc.Get(K.TerminalNoShell, ("shell", setting.Length == 0 ? "?" : setting));
            SetStatusMessage(_terminalTitle.Text);
            return;
        }
        try
        {
            _terminal = TerminalSession.Start(shell.Program, shell.Arguments, folder, null, _terminalView.Columns, _terminalView.Rows);
        }
        catch (IOException e)
        {
            _terminalTitle.Text = Loc.Get(K.TerminalFailed, ("reason", e.Message));
            SetStatusMessage(_terminalTitle.Text);
            return;
        }
        _terminalShellName = Path.GetFileNameWithoutExtension(shell.Program);
        _terminalTitle.Text = _terminalShellName;
        _terminalView.Attach(_terminal);
        _terminalView.Focus();
    }

    /// <summary>True when the keyboard focus is in the terminal and the key is for the shell.</summary>
    private bool KeyBelongsToTerminal(Avalonia.Input.KeyEventArgs e) =>
        FocusManager?.GetFocusedElement() is TerminalView && !TerminalView.IsWindowKey(e.Key, e.KeyModifiers);
}

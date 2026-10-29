using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The user toolbar: a row of buttons under the menu bar, off until View, Toolbar (the setting <c>workbench.toolbar</c>) switches it on. The buttons are the ones of <c>toolbar.json</c>;
/// each runs a command of the registry like the menu does, shows the icon of the command (its title when it has none), and is greyed while the command is disabled for the open document.
/// A button whose command is not registered is not drawn.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Border _toolbar = new() { IsVisible = false };
    private readonly WrapPanel _toolbarRow = new() { Orientation = Orientation.Horizontal };
    private IReadOnlyList<string>? _toolbarLayout;
    private IReadOnlyList<string> _toolbarDrawn = [];
    private string? _toolbarLanguage;
    private bool _toolbarByPointer;
    private ToolbarEditorWindow? _toolbarEditor;

    /// <summary>True while the toolbar is switched on.</summary>
    internal bool ToolbarShown => _app.Workbench.Settings.GetBool("workbench.toolbar");

    /// <summary>The toolbar, for tests.</summary>
    internal Control ToolbarHost => _toolbar;

    /// <summary>The buttons drawn now, in order.</summary>
    internal IReadOnlyList<Button> ToolbarButtons => [.. _toolbarRow.Children.OfType<Button>()];

    /// <summary>The ids drawn now, in order, with <see cref="ToolbarLayout.Separator"/> for a separator.</summary>
    internal IReadOnlyList<string> ToolbarDrawn => _toolbarDrawn;

    internal void ToggleToolbar()
    {
        ToggleSetting("workbench.toolbar");
        if (ToolbarShown && _app.Toolbar.Problem is { } problem) SetStatusMessage(Loc.Get(K.ToolbarUnreadable, ("reason", problem)));
    }

    private Control BuildToolbar()
    {
        _toolbar.Classes.Add("toolbar");
        _toolbar.Child = _toolbarRow;
        HelpContext.Set(_toolbar, "view.toolbar");
        _app.Toolbar.Changed += OnToolbarChanged;
        return _toolbar;
    }

    private void OnToolbarChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnToolbarChanged);
            return;
        }
        _toolbarLayout = null;
        _toolbarDrawn = [];
        RefreshToolbar();
    }

    private void ReleaseToolbar()
    {
        _app.Toolbar.Changed -= OnToolbarChanged;
        _toolbarEditor?.Close();
    }

    /// <summary>Shows or hides the toolbar and draws it again when its buttons, the commands or the language are not the ones drawn.</summary>
    internal void RefreshToolbar()
    {
        bool on = ToolbarShown;
        _toolbar.IsVisible = on;
        if (!on) return;
        var wb = _app.Workbench;
        AutomationProperties.SetName(_toolbar, Loc.Get(K.ToolbarA11y));
        _toolbarLayout ??= _app.Toolbar.Load();
        var shown = ToolbarLayout.Shown(_toolbarLayout, id => wb.Commands.TryGet(id, out _) && wb.Commands.HasHandler(id));
        if (_toolbarLanguage != Loc.Language || !shown.SequenceEqual(_toolbarDrawn))
        {
            _toolbarRow.Children.Clear();
            foreach (var id in shown) _toolbarRow.Children.Add(id == ToolbarLayout.Separator ? ToolbarSeparator() : ToolbarButton(id));
            _toolbarDrawn = shown;
            _toolbarLanguage = Loc.Language;
        }
        RefreshToolbarState();
    }

    /// <summary>Greys the buttons whose command is disabled for the open document. Called as the status line is, so a selection that appears enables Copy at once; nothing happens while the toolbar is off.</summary>
    private void RefreshToolbarState()
    {
        if (!_toolbar.IsVisible) return;
        var context = new ShellContext(this, forMenu: true);
        foreach (var button in _toolbarRow.Children.OfType<Button>())
            if (button.Tag is string id && _app.Workbench.Commands.TryGet(id, out var command)) button.IsEnabled = command.IsEnabled(context);
    }

    private static Control ToolbarSeparator()
    {
        var line = new Border { Width = 1, Height = 18, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        line.Classes.Add("toolsep");
        AutomationProperties.SetAccessibilityView(line, AccessibilityView.Raw);
        return line;
    }

    private Button ToolbarButton(string id)
    {
        var wb = _app.Workbench;
        wb.Commands.TryGet(id, out var command);
        string title = wb.Strings.Get(command.TitleKey);
        string? shortcut = wb.Keymap.ShortcutsFor(id).FirstOrDefault();
        var icon = wb.Commands.IconOf(id);
        var button = new Button
        {
            Tag = id,
            Content = icon is null ? new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } : CommandIcons.Create(icon),
            IsEnabled = false,
        };
        button.Classes.Add("toolbutton");
        AutomationProperties.SetName(button, title);
        ToolTip.SetTip(button, shortcut is null ? title : $"{title} ({shortcut})");
        // a click with the pointer hands the keyboard back to the document, so typing goes on there; a key press on the button keeps it where it is
        button.AddHandler(PointerReleasedEvent, (_, _) => _toolbarByPointer = true, RoutingStrategies.Tunnel);
        button.Click += (_, _) =>
        {
            bool byPointer = _toolbarByPointer;
            _toolbarByPointer = false;
            RunToolbarCommand(id, byPointer);
        };
        return button;
    }

    private void RunToolbarCommand(string id, bool refocus)
    {
        var task = _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(this, forMenu: true)).AsTask();
        if (refocus) Dispatcher.UIThread.Post(() => { if (Active is { } tab && !IsPaletteOpen && !InSidePanels) FocusView(tab); }, DispatcherPriority.Input);
        _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(RefreshToolbarState), TaskScheduler.Default);
    }

    /// <summary>Opens the editor of the toolbar buttons; a second call brings the open one forward.</summary>
    internal ToolbarEditorWindow ShowToolbarEditor()
    {
        if (_toolbarEditor is { } open)
        {
            open.Activate();
            return open;
        }
        var window = _toolbarEditor = new ToolbarEditorWindow(_app.Workbench, _app.Toolbar);
        window.Closed += (_, _) => { if (_toolbarEditor == window) _toolbarEditor = null; };
        window.Show(this);
        return window;
    }
}

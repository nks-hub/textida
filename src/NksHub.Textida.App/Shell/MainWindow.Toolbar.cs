using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The user toolbar: a row of buttons under the menu bar, on by default and switched off and on with View, Toolbar (the setting <c>workbench.toolbar</c>). The buttons are the ones of <c>toolbar.json</c>;
/// each runs a command of the registry like the menu does, shows the icon of the command (its title when it has none), and is greyed while the command is disabled for the open document.
/// A button whose command is not registered is not drawn.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Border _toolbar = new() { IsVisible = false };
    private readonly WrapPanel _toolbarRow = new() { Orientation = Orientation.Horizontal };
    private IReadOnlyList<string>? _toolbarLayout;
    private IReadOnlyList<string> _toolbarDrawn = [];
    private string? _toolbarLook;
    private ContextMenu? _toolbarMenu;
    private bool _toolbarByPointer;
    private string? _toolbarCurrent;
    private ToolbarEditorWindow? _toolbarEditor;

    /// <summary>True while the toolbar is switched on.</summary>
    internal bool ToolbarShown => _app.Workbench.Settings.GetBool("workbench.toolbar");

    /// <summary>True while the toolbar shows large icons (the setting <c>workbench.toolbarLargeIcons</c>).</summary>
    internal bool ToolbarLargeIcons => _app.Workbench.Settings.GetBool("workbench.toolbarLargeIcons");

    /// <summary>True while the toolbar icons are coloured by the kind of command (the setting <c>workbench.toolbarColorIcons</c>); high contrast draws them in one colour anyway.</summary>
    internal bool ToolbarColorIcons => _app.Workbench.Settings.GetBool("workbench.toolbarColorIcons");

    /// <summary>Side of a toolbar icon now.</summary>
    internal double ToolbarIconSize => ToolbarLargeIcons ? LargeToolbarIcon : CommandIcons.Size;

    /// <summary>Side of a large toolbar icon in device-independent pixels.</summary>
    internal const double LargeToolbarIcon = 24;

    /// <summary>The context menu of the toolbar while it is open or was opened last, for tests.</summary>
    internal ContextMenu? ToolbarMenu => _toolbarMenu;

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
        _toolbarRow.AddHandler(KeyDownEvent, OnToolbarKeyDown);
        _toolbarRow.AddHandler(GotFocusEvent, OnToolbarGotFocus);
        _toolbar.AddHandler(ContextRequestedEvent, OnToolbarContextRequested);
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
        _toolbarMenu?.Close();
    }

    /// <summary>Shows or hides the toolbar and draws it again when its buttons, the commands or the language are not the ones drawn.</summary>
    internal void RefreshToolbar()
    {
        bool on = ToolbarShown;
        _toolbar.IsVisible = on;
        if (!on) return;
        var wb = _app.Workbench;
        AutomationProperties.SetName(_toolbar, Loc.Get(K.ToolbarA11y));
        if (_toolbarLayout is null)
        {
            _toolbarLayout = _app.Toolbar.Load();
            if (_app.Toolbar.Problem is { } problem) SetStatusMessage(Loc.Get(K.ToolbarUnreadable, ("reason", problem)));
        }
        var shown = ToolbarLayout.Shown(_toolbarLayout, id => wb.Commands.TryGet(id, out _) && wb.Commands.HasHandler(id));
        string look = $"{Loc.Language}|{ToolbarIconSize}|{ToolbarColorIcons}";
        if (_toolbarLook != look || !shown.SequenceEqual(_toolbarDrawn))
        {
            _toolbarRow.Children.Clear();
            foreach (var id in shown) _toolbarRow.Children.Add(id == ToolbarLayout.Separator ? ToolbarSeparator() : ToolbarButton(id));
            _toolbarDrawn = shown;
            _toolbarLook = look;
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
        UpdateToolbarStops();
    }

    /// <summary>
    /// The toolbar is one Tab stop: the button the keyboard was on last (the first enabled one before any), and the others are reached with the arrow keys.
    /// A remembered button that is greyed for now gives the stop to the first enabled one, so the toolbar never becomes unreachable.
    /// </summary>
    private void UpdateToolbarStops()
    {
        var buttons = _toolbarRow.Children.OfType<Button>().ToList();
        var current = buttons.FirstOrDefault(b => b.IsEnabled && (string?)b.Tag == _toolbarCurrent) ?? buttons.FirstOrDefault(b => b.IsEnabled);
        foreach (var button in buttons) button.IsTabStop = button == current;
    }

    private void OnToolbarGotFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { Tag: string id }) return;
        _toolbarCurrent = id;
        UpdateToolbarStops();
    }

    /// <summary>Left and Right move to the neighbouring enabled button (the other way round in a right-to-left layout), Home and End to the first and last; the ends do not wrap.</summary>
    private void OnToolbarKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not Button from) return;
        // the platform does not raise the context request for the keyboard everywhere, so the toolbar opens its menu itself
        if ((e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift))
        {
            e.Handled = true;
            OpenToolbarMenu(from);
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        var stops = _toolbarRow.Children.OfType<Button>().Where(b => b.IsEnabled).ToList();
        int at = stops.IndexOf(from);
        bool rtl = _toolbarRow.FlowDirection == FlowDirection.RightToLeft;
        int? target = e.Key switch
        {
            Key.Right => at + (rtl ? -1 : 1),
            Key.Left => at + (rtl ? 1 : -1),
            Key.Home => 0,
            Key.End => stops.Count - 1,
            _ => null,
        };
        if (at < 0 || target is not { } index) return;
        e.Handled = true;
        stops[Math.Clamp(index, 0, stops.Count - 1)].Focus(NavigationMethod.Directional);
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
            Content = icon is null ? new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center } : new IconView(icon, ToolbarIconSize) { Colored = ToolbarColorIcons },
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
        // only a keyboard still on the toolbar goes back: a command that moved it itself (Find opens its bar) keeps it where it put it
        if (refocus) Dispatcher.UIThread.Post(() => { if (Active is { } tab && !IsPaletteOpen && !InSidePanels && KeyboardOnToolbar()) FocusView(tab); }, DispatcherPriority.Input);
        _ = task.ContinueWith(_ => Dispatcher.UIThread.Post(RefreshToolbarState), TaskScheduler.Default);
    }

    private bool KeyboardOnToolbar() => FocusManager?.GetFocusedElement() is not Visual focused || focused == _toolbar || _toolbar.IsVisualAncestorOf(focused);

    // ---- the context menu ----

    private void OnToolbarContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        var over = (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true);
        OpenToolbarMenu(over is { Tag: string } && over.GetVisualParent() == _toolbarRow ? over : null);
    }

    /// <summary>
    /// Opens the context menu of the toolbar: Customize, Hide, Remove This Button (when it was asked for over a button), Restore Default Buttons, the icon size and Colored Icons.
    /// The right button of the pointer and the Menu key or Shift+F10 on a button open it.
    /// </summary>
    internal ContextMenu OpenToolbarMenu(Button? over)
    {
        _toolbarMenu?.Close();
        var menu = _toolbarMenu = new ContextMenu();
        AutomationProperties.SetName(menu, Loc.Get(K.ToolbarMenuA11y));
        MenuItem Add(string key, Action run)
        {
            var item = new MenuItem { Header = Loc.Get(key) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
            return item;
        }
        Add(K.ToolbarMenuEdit, () => ShowToolbarEditor());
        Add(K.ToolbarMenuHide, () => _app.Workbench.Set("workbench.toolbar", JsonValue.Create(false)));
        menu.Items.Add(new Separator());
        if (over?.Tag is string id) Add(K.ToolbarMenuRemove, () => SaveToolbar([.. (_toolbarLayout ?? _app.Toolbar.Load()).Where(b => b != id)]));
        Add(K.ToolbarMenuReset, () => SaveToolbar(ToolbarLayout.Default));
        menu.Items.Add(new Separator());
        bool large = ToolbarLargeIcons;
        foreach (var (key, value) in new[] { (K.ToolbarMenuSmall, false), (K.ToolbarMenuLarge, true) })
        {
            var item = Add(key, () => _app.Workbench.Set("workbench.toolbarLargeIcons", JsonValue.Create(value)));
            item.ToggleType = MenuItemToggleType.Radio;
            item.GroupName = "toolbarSize";
            item.IsChecked = large == value;
        }
        bool colored = ToolbarColorIcons;
        var colors = Add(K.ToolbarMenuColors, () => _app.Workbench.Set("workbench.toolbarColorIcons", JsonValue.Create(!colored)));
        colors.ToggleType = MenuItemToggleType.CheckBox;
        colors.IsChecked = colored;
        menu.Open(over ?? (Control)_toolbar);
        return menu;
    }

    private void SaveToolbar(IReadOnlyList<string> buttons)
    {
        if (!_app.Toolbar.Save(buttons, out var detail)) SetStatusMessage(Loc.Get(K.ToolbarEditWriteFailed, ("reason", detail)));
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

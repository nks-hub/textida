using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Where the side panels stand. Each panel is docked to the left, the right or the bottom of the window, a side shows one panel at a time, and the size of a side is changed by dragging the handle
/// between it and the text. A panel moves to another side by dragging its header or by a command, and the layout is written into the profile with the geometry of the window, so it survives a restart;
/// a command takes it back to the default. Everything that is done with the mouse is also a command, and every change is said to a screen reader.
/// </summary>
internal sealed partial class MainWindow
{
    private PanelLayout _layout = new();
    private readonly Dictionary<PanelDock, SidePanel?> _shown = new() { [PanelDock.Left] = null, [PanelDock.Right] = null, [PanelDock.Bottom] = null };
    private readonly Dictionary<PanelDock, Border> _hosts = [];
    private readonly Dictionary<PanelDock, Panel> _hostStacks = [];
    private readonly Dictionary<PanelDock, PanelSplitter> _splitters = [];
    private readonly Dictionary<SidePanel, Control> _panelHeaders = [];
    private readonly Border _dropHint = new() { IsVisible = false, IsHitTestVisible = false };
    private SidePanel? _lastPanel;

    private Border _leftHost => _hosts[PanelDock.Left];

    internal PanelLayout SideLayout => _layout;

    /// <summary>The panel shown on a side, or null.</summary>
    internal SidePanel? PanelShown(PanelDock dock) => _shown[dock];

    internal Control PanelHost(PanelDock dock) => _hosts[dock];

    internal PanelSplitter SplitterOf(PanelDock dock) => _splitters[dock];

    /// <summary>The area the panels and the text share.</summary>
    internal Grid Body => _body;

    /// <summary>The mark that shows where a dragged panel would land.</summary>
    internal Border DropHint => _dropHint;

    internal Control PanelHeader(SidePanel panel) => _panelHeaders[panel];

    internal bool IsPanelOpen(SidePanel panel) => IsOpen(panel);

    private bool IsOpen(SidePanel panel) => _shown[_layout.DockOf(panel)] == panel;

    /// <summary>True when a side panel is open: the commands that move, resize and hide the panel work on it.</summary>
    internal bool AnySidePanelOpen => PanelLayout.Panels.Any(IsOpen);

    /// <summary>True when the keyboard is in a side panel.</summary>
    private bool InSidePanels => _hosts.Values.Any(h => h.IsKeyboardFocusWithin);

    private Control ControlOf(SidePanel panel) => panel switch { SidePanel.Documents => _docPanel, SidePanel.Files => _filePanel, _ => _pluginPanel };

    private string PanelName(SidePanel panel) => panel switch
    {
        SidePanel.Documents => Loc.Get(K.PanelDocumentsTitle),
        SidePanel.Files => Loc.Get(K.PanelFilesTitle),
        _ => _pluginPanelTitle.Text is { Length: > 0 } title ? title : Loc.Get(K.PanelPluginName),
    };

    // ---- building ----

    /// <summary>Puts the sides, the handles between them and the text, and the mark that shows where a dragged panel would land into the grid of the body.</summary>
    private void BuildPanelHosts(Control editorArea)
    {
        foreach (var dock in PanelLayout.Docks)
        {
            var stack = new Panel();
            var host = new Border { Child = stack, IsVisible = false, ClipToBounds = true };
            host.Classes.Add("sidepanel");
            host.Classes.Add(dock switch { PanelDock.Right => "dockright", PanelDock.Bottom => "dockbottom", _ => "dockleft" });
            var side = dock;
            var splitter = new PanelSplitter(side, () => _layout.SizeOf(side), () => SizeLimits(side), (size, final) => SetPanelSize(side, size, final)) { IsVisible = false };
            _hosts[dock] = host;
            _hostStacks[dock] = stack;
            _splitters[dock] = splitter;
        }
        Grid.SetColumn(editorArea, 2);
        Place(_hosts[PanelDock.Left], 0, 0);
        Place(_splitters[PanelDock.Left], 0, 1);
        Place(_splitters[PanelDock.Right], 0, 3);
        Place(_hosts[PanelDock.Right], 0, 4);
        Place(_splitters[PanelDock.Bottom], 1, 0, columnSpan: 5);
        Place(_hosts[PanelDock.Bottom], 2, 0, columnSpan: 5);
        _body.Children.Add(editorArea);
        _dropHint.Classes.Add("paneldrop");
        Grid.SetRowSpan(_dropHint, 3);
        Grid.SetColumnSpan(_dropHint, 5);
        _body.Children.Add(_dropHint);

        void Place(Control control, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            Grid.SetColumnSpan(control, columnSpan);
            _body.Children.Add(control);
        }
    }

    /// <summary>Makes a header the place a panel is dragged by, and gives it the menu of the panel.</summary>
    private Control MakeDraggable(Control header, SidePanel panel)
    {
        _panelHeaders[panel] = header;
        header.Cursor = new Cursor(StandardCursorType.SizeAll);
        AttachDrag(header, panel);
        return header;
    }

    /// <summary>Wraps a title in a header that can be dragged (a title alone is hit only where its letters are).</summary>
    private Control DraggableTitle(TextBlock title, SidePanel panel) => MakeDraggable(new Border { Background = Avalonia.Media.Brushes.Transparent, Child = title }, panel);

    // ---- what is shown ----

    private (double Min, double Max) SizeLimits(PanelDock dock) => PanelLayout.Limits(dock, dock == PanelDock.Bottom ? _body.Bounds.Height : _body.Bounds.Width);

    /// <summary>Puts every panel into the side it is docked to, and shows the sides that have a panel open.</summary>
    private void ApplyPanelState(bool rebuildMenu = true)
    {
        foreach (var panel in PanelLayout.Panels)
        {
            var stack = _hostStacks[_layout.DockOf(panel)];
            var control = ControlOf(panel);
            if (!ReferenceEquals(control.Parent, stack))
            {
                (control.Parent as Panel)?.Children.Remove(control);
                stack.Children.Add(control);
            }
            control.IsVisible = IsOpen(panel);
        }
        foreach (var dock in PanelLayout.Docks) _hosts[dock].IsVisible = _splitters[dock].IsVisible = _shown[dock] is not null;
        ApplySizes();
        if (rebuildMenu) _menuModel = BuildMenuModel();
    }

    private void ApplySizes()
    {
        double Size(PanelDock dock) => _shown[dock] is null ? 0 : _layout.SizeOf(dock);
        double Line(PanelDock dock) => _shown[dock] is null ? 0 : PanelSplitter.Thickness;
        _body.ColumnDefinitions[0].Width = new GridLength(Size(PanelDock.Left));
        _body.ColumnDefinitions[1].Width = new GridLength(Line(PanelDock.Left));
        _body.ColumnDefinitions[3].Width = new GridLength(Line(PanelDock.Right));
        _body.ColumnDefinitions[4].Width = new GridLength(Size(PanelDock.Right));
        _body.RowDefinitions[1].Height = new GridLength(Line(PanelDock.Bottom));
        _body.RowDefinitions[2].Height = new GridLength(Size(PanelDock.Bottom));
    }

    private void PersistLayout() => _app.SaveWindowState(this);

    // ---- opening and closing ----

    /// <summary>Shows or hides a panel on its side. A panel that was shown there is replaced, and stays docked there for the next time.</summary>
    private void TogglePanel(SidePanel which, bool? open)
    {
        var dock = _layout.DockOf(which);
        bool show = open ?? !IsOpen(which);
        if (show)
        {
            _shown[dock] = which;
            _lastPanel = which;
            if (which == SidePanel.Files)
            {
                RestoreFolder();
                if (_fileTree is not null && _gitFilesStale) RefreshGitFiles(now: true);
            }
        }
        else if (_shown[dock] == which) _shown[dock] = null;
        ApplyPanelState();
        if (show) Dispatcher.UIThread.Post(() => FocusPanel(which), DispatcherPriority.Input);
        else if (Active is { } tab) FocusView(tab);
    }

    /// <summary>Gives the keyboard to the part of a panel that takes it first.</summary>
    private void FocusPanel(SidePanel which)
    {
        switch (which)
        {
            case SidePanel.Plugin: FocusPluginTree(); break;
            case SidePanel.Documents: _docFilter.Focus(); break;
            default: (_fileTree is null ? (Control)_filesOpenButton : _fileList).Focus(); break;
        }
    }

    /// <summary>Esc closes the panel it is pressed in (a plug-in panel stays beside the text while you type, so it closes only from inside), or else the open list of documents or files; false when none was closed.</summary>
    private bool CloseSidePanel()
    {
        if (IsOpen(SidePanel.Plugin) && _pluginPanel.IsKeyboardFocusWithin)
        {
            TogglePanel(SidePanel.Plugin, false);
            return true;
        }
        if (IsOpen(SidePanel.Documents)) TogglePanel(SidePanel.Documents, false);
        else if (IsOpen(SidePanel.Files)) TogglePanel(SidePanel.Files, false);
        else return false;
        return true;
    }

    /// <summary>The panel the layout commands work on: the one that has the keyboard, else the one opened last, else the first open one. Null when none is open.</summary>
    internal SidePanel? CurrentSidePanel()
    {
        foreach (var panel in PanelLayout.Panels)
            if (IsOpen(panel) && ControlOf(panel).IsKeyboardFocusWithin) return panel;
        if (_lastPanel is { } last && IsOpen(last)) return last;
        foreach (var panel in PanelLayout.Panels)
            if (IsOpen(panel)) return panel;
        return null;
    }

    // ---- moving ----

    /// <summary>Docks a panel to a side. An open panel moves there at once and replaces the one shown there; a closed one opens there the next time.</summary>
    internal void MovePanel(SidePanel panel, PanelDock dock)
    {
        var from = _layout.DockOf(panel);
        if (from != dock)
        {
            bool open = IsOpen(panel), hadFocus = ControlOf(panel).IsKeyboardFocusWithin;
            if (_shown[from] == panel) _shown[from] = null;
            _layout.Place(panel, dock);
            if (open)
            {
                _shown[dock] = panel;
                _lastPanel = panel;
            }
            ApplyPanelState();
            PersistLayout();
            if (hadFocus) Dispatcher.UIThread.Post(() => FocusPanel(panel), DispatcherPriority.Input);
        }
        Announce(Loc.Get(dock switch { PanelDock.Right => K.A11yPanelDockedRight, PanelDock.Bottom => K.A11yPanelDockedBottom, _ => K.A11yPanelDockedLeft }, ("panel", PanelName(panel))));
    }

    /// <summary>The command: docks the current panel to a side.</summary>
    internal void DockCurrentPanel(PanelDock dock)
    {
        if (CurrentSidePanel() is { } panel) MovePanel(panel, dock);
    }

    /// <summary>The command: hides the current panel.</summary>
    internal void HideCurrentPanel()
    {
        if (CurrentSidePanel() is not { } panel) return;
        string name = PanelName(panel);
        TogglePanel(panel, false);
        Announce(Loc.Get(K.A11yPanelHidden, ("panel", name)));
    }

    /// <summary>The command: puts every panel on the left at the default size.</summary>
    internal void ResetPanelLayout()
    {
        var open = PanelLayout.Panels.Where(IsOpen).OrderBy(p => p == _lastPanel).ToList(); // the one opened last is the one that stays shown
        _layout = new PanelLayout();
        foreach (var dock in PanelLayout.Docks) _shown[dock] = null;
        foreach (var panel in open) _shown[_layout.DockOf(panel)] = panel;
        ApplyPanelState();
        PersistLayout();
        foreach (var dock in PanelLayout.Docks) _splitters[dock].Changed();
        Announce(Loc.Get(K.A11yPanelReset));
    }

    // ---- resizing ----

    /// <summary>The command: makes the current panel larger by a step (or smaller, with a negative sign).</summary>
    internal void ResizeCurrentPanel(int steps)
    {
        if (CurrentSidePanel() is not { } panel) return;
        var dock = _layout.DockOf(panel);
        SetPanelSize(dock, _layout.SizeOf(dock) + steps * PanelSplitter.Step, final: true);
    }

    /// <summary>Sets the size of a side inside its limits. <paramref name="final"/>: the drag is over or a key was pressed, so the layout is kept and the new size is said.</summary>
    internal double SetPanelSize(PanelDock dock, double size, bool final)
    {
        var (min, max) = SizeLimits(dock);
        double now = _layout.SetSize(dock, size, dock == PanelDock.Bottom ? _body.Bounds.Height : _body.Bounds.Width);
        ApplySizes();
        _splitters[dock].Changed();
        if (!final) return now;
        PersistLayout();
        var panel = _shown[dock] ?? PanelLayout.Panels.FirstOrDefault(p => _layout.DockOf(p) == dock);
        string text = Loc.Get(dock == PanelDock.Bottom ? K.A11yPanelHeight : K.A11yPanelWidth, ("panel", PanelName(panel)), ("size", (int)Math.Round(now)));
        if (size < min || size > max) text += " " + Loc.Get(K.A11yPanelLimit);
        Announce(text);
        return now;
    }

    // ---- dragging a panel by its header ----

    private void AttachDrag(Control handle, SidePanel panel)
    {
        Point? start = null;
        bool dragging = false;
        void End(PointerEventArgs? e, bool drop)
        {
            if (start is null) return;
            var side = drop && dragging && e is not null ? PanelLayout.DropSide(e.GetPosition(_body), _body.Bounds.Size) : null;
            start = null;
            dragging = false;
            ShowDropHint(null);
            e?.Pointer.Capture(null);
            if (side is { } to) MovePanel(panel, to);
        }
        handle.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(_body);
            e.Pointer.Capture(handle);
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not { } from) return;
            var point = e.GetPosition(_body);
            if (!dragging && Math.Abs(point.X - from.X) + Math.Abs(point.Y - from.Y) < 8) return;
            dragging = true;
            ShowDropHint(PanelLayout.DropSide(point, _body.Bounds.Size));
        };
        handle.PointerReleased += (_, e) => End(e, drop: true);
        handle.PointerCaptureLost += (_, _) => End(null, drop: false);
    }

    /// <summary>Shows the part of the window a dragged panel would take, or nothing.</summary>
    private void ShowDropHint(PanelDock? side)
    {
        _dropHint.IsVisible = side is not null;
        if (side is not { } dock) return;
        var area = _body.Bounds.Size;
        _dropHint.HorizontalAlignment = dock switch { PanelDock.Left => HorizontalAlignment.Left, PanelDock.Right => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
        _dropHint.VerticalAlignment = dock == PanelDock.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        _dropHint.Width = dock == PanelDock.Bottom ? double.NaN : area.Width * 0.25;
        _dropHint.Height = dock == PanelDock.Bottom ? area.Height * 0.25 : double.NaN;
    }

    // ---- texts ----

    private ContextMenu BuildPanelMenu(SidePanel panel)
    {
        var wb = _app.Workbench;
        var menu = new ContextMenu();
        void Add(string commandId, Action run)
        {
            if (!wb.Commands.TryGet(commandId, out var command)) return;
            var item = new MenuItem { Header = wb.Strings.Get(command.TitleKey) };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }
        Add("nks.panel.dockLeft", () => MovePanel(panel, PanelDock.Left));
        Add("nks.panel.dockRight", () => MovePanel(panel, PanelDock.Right));
        Add("nks.panel.dockBottom", () => MovePanel(panel, PanelDock.Bottom));
        Add("nks.panel.hide", () => TogglePanel(panel, false));
        Add("nks.panel.resetLayout", ResetPanelLayout);
        return menu;
    }

    private void ApplyPanelLayoutTexts()
    {
        foreach (var (panel, header) in _panelHeaders) header.ContextMenu = BuildPanelMenu(panel);
        AutomationProperties.SetName(_splitters[PanelDock.Left], Loc.Get(K.A11yPanelSplitterLeft));
        AutomationProperties.SetName(_splitters[PanelDock.Right], Loc.Get(K.A11yPanelSplitterRight));
        AutomationProperties.SetName(_splitters[PanelDock.Bottom], Loc.Get(K.A11yPanelSplitterBottom));
    }
}

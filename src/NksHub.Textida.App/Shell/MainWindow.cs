using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// D5 main window: only a text menu and tabs on top, the document below, a status bar at the bottom, a hidden list of open documents on the left.
/// Windows: menu in the title strip and tabs in a row under it. macOS: tabs in the title bar next to the traffic lights, menu in the system menu bar.
/// Tabs live in groups side by side; two tabs of one document are the views of a split.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private const double PanelWidth = 220;

    private readonly App _app;
    private readonly ShellLook _look;
    private readonly List<TabGroup> _groups = [];
    private readonly List<OpenDocument> _docs = [];
    private TabGroup _activeGroup = null!;
    private bool _syncing;
    private bool _closeApproved;

    private readonly Grid _groupsGrid = new();
    private readonly Border _titleTabRow = new() { Height = 38 };
    private readonly StackPanel _notices = new();
    private readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("0,*") };
    private readonly ListBox _docList = new() { Background = Brushes.Transparent };
    private readonly TextBlock _docListTitle = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 6) };
    private readonly TextBlock _docListEmpty = new() { Margin = new Thickness(12, 4), Opacity = 0.7 };
    private readonly Border _statusBar = new() { Height = 26 };
    private readonly ContentControl _menuHost = new() { VerticalAlignment = VerticalAlignment.Center };
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public IReadOnlyList<TabGroup> Groups => _groups;
    public TabGroup ActiveGroup => _activeGroup;
    public IReadOnlyList<OpenDocument> Documents => _docs;
    public IEnumerable<DocTab> Tabs => _groups.SelectMany(g => g.Tabs);
    public DocTab? Active => _activeGroup.Active;
    public bool DocumentsPanelOpen => _body.ColumnDefinitions[0].Width.Value > 0;
    public EditorOptions Options => EditorOptions.From(_app.Workbench.Settings, Active?.Doc.Language.Id);

    /// <summary>Text of the menu groups currently shown, for tests and accessibility checks.</summary>
    public IReadOnlyList<string> MenuHeaders => _menuModel.Select(g => g.Label).ToList();
    internal IReadOnlyList<MenuGroup> MenuModel => _menuModel;
    private IReadOnlyList<MenuGroup> _menuModel = [];
    private NativeMenu? _nativeMenu;

    public MainWindow(App app, ShellLook look, SavedWindow? saved)
    {
        _app = app;
        _look = look;
        var placement = WindowPlacement.Restore(saved, Screens.All.Select(s => new ScreenBox(new PixelBox(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height), s.Scaling, s.IsPrimary)).ToList());

        Classes.Add("shell");
        MinWidth = 520;
        MinHeight = 320;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = look.TabsInTitleBar ? 38 : 32;
        if (look.Mica)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }
        else TransparencyLevelHint = [WindowTransparencyLevel.None];

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized) WindowState = WindowState.Maximized;
        _normalPosition = Position;
        _normalSize = new Size(placement.Width, placement.Height);

        BuildLayout();
        AddGroup();
        ApplyTexts();
        Loc.Changed += OnLanguageChanged;
        PositionChanged += (_, e) => { if (WindowState == WindowState.Normal) _normalPosition = e.Point; };
        Resized += (_, e) => { if (WindowState == WindowState.Normal) _normalSize = e.ClientSize; };
        _docList.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _docList.SelectedIndex is var i and >= 0 && i < Tabs.Count()) SelectTab(Tabs.ElementAt(i));
        };
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnWindowDrop, Avalonia.Interactivity.RoutingStrategies.Bubble | Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver, Avalonia.Interactivity.RoutingStrategies.Bubble | Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(this, true);
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        _app.Workbench.SettingChanged += OnSettingChanged;
    }

    public SavedWindow CaptureGeometry() => new()
    {
        X = _normalPosition.X, Y = _normalPosition.Y, Width = _normalSize.Width, Height = _normalSize.Height,
        Maximized = WindowState == WindowState.Maximized,
    };

    // ---- layout ----

    private void BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        if (_look.TabsInTitleBar)
        {
            _titleTabRow.Padding = new Thickness(78, 0, 8, 0);
            _titleTabRow.VerticalAlignment = VerticalAlignment.Bottom;
            _titleTabRow.Classes.Add("tabrow");
            WindowDecorationProperties.SetElementRole(_titleTabRow, WindowDecorationsElementRole.TitleBar);
            Grid.SetRow(_titleTabRow, 0);
            root.Children.Add(_titleTabRow);
        }
        else
        {
            var mark = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = Loc.Get(K.AppName)[..1], FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            mark.Classes.Add("mark");
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Height = 32 };
            title.Classes.Add("titlestrip");
            Grid.SetColumn(_menuHost, 1);
            title.Children.Add(mark);
            title.Children.Add(_menuHost);
            WindowDecorationProperties.SetElementRole(title, WindowDecorationsElementRole.TitleBar);
            WindowDecorationProperties.SetElementRole(_menuHost, WindowDecorationsElementRole.User);
            Grid.SetRow(title, 0);
            root.Children.Add(title);
        }

        var panel = new Border { Child = new DockPanel { Children = { Dock(_docListTitle, Avalonia.Controls.Dock.Top), Dock(_docListEmpty, Avalonia.Controls.Dock.Bottom), _docList } } };
        panel.Classes.Add("sidepanel");

        var editorArea = new DockPanel { Children = { Dock(_notices, Avalonia.Controls.Dock.Top), _groupsGrid } };
        Grid.SetColumn(editorArea, 1);
        _body.Children.Add(panel);
        _body.Children.Add(editorArea);

        var content = new Border { Child = new Panel { Children = { _body, BuildPalette() } } };
        content.Classes.Add("content");
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        _statusBar.Child = BuildStatusBar();
        _statusBar.Classes.Add("statusbar");
        Grid.SetRow(_statusBar, 2);
        root.Children.Add(_statusBar);

        Content = root;
    }

    private static Control Dock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    // ---- groups ----

    private TabGroup AddGroup(int? index = null)
    {
        var hint = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Text = EmptyHintText() };
        hint.Classes.Add("hint");
        var group = new TabGroup { Empty = hint, ContextActions = TabContextActions };
        group.Activated += (g, tab) => { _activeGroup = g; OnActiveTabChanged(); };
        group.Focused += g => { if (_activeGroup != g) { _activeGroup = g; OnActiveTabChanged(); } };
        group.CloseRequested += tab => _ = CloseTabAsync(tab);
        group.TabDropped += (target, tab, at) => MoveTab(tab, target, at);
        group.FilesDropped += (target, paths) => OpenDropped(target, paths);
        _groups.Insert(Math.Clamp(index ?? _groups.Count, 0, _groups.Count), group);
        _activeGroup ??= group;
        Relayout();
        return group;
    }

    private void RemoveGroupIfEmpty(TabGroup group)
    {
        if (group.Tabs.Count > 0 || _groups.Count <= 1) return;
        _groups.Remove(group);
        if (_activeGroup == group) _activeGroup = _groups[0];
        Relayout();
    }

    private void Relayout()
    {
        _groupsGrid.Children.Clear();
        _groupsGrid.ColumnDefinitions.Clear();
        foreach (var g in _groups) g.StripHost.Child = null;
        bool inTitle = _look.TabsInTitleBar && _groups.Count == 1;
        if (_look.TabsInTitleBar) _titleTabRow.Child = inTitle ? _groups[0].Strip : null;
        for (int i = 0; i < _groups.Count; i++)
        {
            var g = _groups[i];
            g.StripHost.IsVisible = !inTitle;
            if (!inTitle) g.StripHost.Child = g.Strip;
            if (i > 0)
            {
                _groupsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(4)));
                var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent, Width = 4 };
                splitter.Classes.Add("groupsplitter");
                Grid.SetColumn(splitter, _groupsGrid.ColumnDefinitions.Count - 1);
                _groupsGrid.Children.Add(splitter);
            }
            _groupsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 120 });
            Grid.SetColumn(g, _groupsGrid.ColumnDefinitions.Count - 1);
            _groupsGrid.Children.Add(g);
        }
    }

    // ---- tabs ----

    public DocTab SelectTab(DocTab tab)
    {
        var group = tab.Group!;
        _activeGroup = group;
        group.Select(tab);
        OnActiveTabChanged();
        return tab;
    }

    private void OnActiveTabChanged()
    {
        RefreshDocList();
        ApplyLanguageOptions();
        UpdateStatus();
        RefreshTitle();
        _menuModel = BuildMenuModel();
        var tab = Active;
        Dispatcher.UIThread.Post(() => { if (Active == tab && tab is not null && !_docList.IsKeyboardFocusWithin && !IsPaletteOpen && !IsFocusInTextInput()) FocusView(tab); }, DispatcherPriority.Input);
    }

    /// <summary>Gives the keyboard to the text area of the tab, so typing works at once without a click.</summary>
    private static void FocusView(DocTab tab)
    {
        if (tab.Surface is { } surface) surface.TextArea.Focus();
        else tab.View.Focus();
    }

    private static string EmptyHintText() => Loc.Get(K.WorkspaceEmpty, ("shortcut", Shortcuts.Display(Avalonia.Input.Key.N)));

    private bool IsFocusInTextInput() => FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>True when the keyboard focus is inside the active tab's text surface.</summary>
    public bool EditorHasFocus => Active is { } tab && FocusManager?.GetFocusedElement() is Visual v && (ReferenceEquals(v, tab.View) || tab.View.IsVisualAncestorOf(v));

    private void RefreshTitle() => Title = Active is null ? Loc.Get(K.AppName) : $"{Active.Title} - {Loc.Get(K.AppName)}";

    private void RefreshDocList()
    {
        _syncing = true;
        _docList.Items.Clear();
        foreach (var tab in Tabs) _docList.Items.Add(new ListBoxItem { Content = tab.Title });
        _docList.SelectedIndex = Active is null ? -1 : Tabs.ToList().IndexOf(Active);
        _docListEmpty.IsVisible = !Tabs.Any();
        _syncing = false;
    }

    /// <summary>Applies the settings of the active tab's language to every editor; called when the active tab or a setting changes.</summary>
    private void ApplyLanguageOptions()
    {
        foreach (var tab in Tabs)
        {
            var options = EditorOptions.From(_app.Workbench.Settings, tab.Doc.Language.Id);
            tab.Surface?.ApplyOptions(options);
            tab.Paged?.ApplyOptions(options);
        }
    }

    private void RefreshHeaders()
    {
        foreach (var g in _groups)
            if (g.Tabs.Any(t => TabGroup.HeaderStateOf(t) != t.HeaderState)) g.Refresh();
    }

    // ---- panels ----

    public void ToggleDocuments(bool? open = null)
    {
        bool show = open ?? !DocumentsPanelOpen;
        _body.ColumnDefinitions[0].Width = new GridLength(show ? PanelWidth : 0);
        _menuModel = BuildMenuModel();
        if (show) _docList.Focus();
        else if (Active is { } tab) FocusView(tab);
    }

    // ---- text ----

    private void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyTexts();
        else Dispatcher.UIThread.Post(ApplyTexts);
    }

    /// <summary>Sets every user-visible text from the catalogue; called at construction and whenever the language or the tab set changes.</summary>
    public void ApplyTexts()
    {
        foreach (var g in _groups) if (g.Empty is TextBlock hint) hint.Text = EmptyHintText();
        _docListTitle.Text = Loc.Get(K.PanelDocumentsTitle);
        _docListEmpty.Text = Loc.Get(K.PanelDocumentsEmpty);
        AutomationProperties.SetName(_docList, Loc.Get(K.A11yDocumentList));
        foreach (var g in _groups) AutomationProperties.SetName(g.Strip, Loc.Get(K.A11yTabs));
        AutomationProperties.SetName(_statusBar, Loc.Get(K.A11yStatusBar));
        AutomationProperties.SetName(_groupsGrid, Loc.Get(K.A11yWorkspace));
        AutomationProperties.SetName(_menuHost, Loc.Get(K.A11yMainMenu));
        ApplyPaletteTexts();
        _menuModel = BuildMenuModel();
        foreach (var g in _groups) g.Refresh();
        RefreshDocList();
        UpdateStatus();
        RefreshTitle();
    }

    public void SetStatusMessage(string text)
    {
        _message = text;
        UpdateStatus();
    }

    private void OnSettingChanged(string key)
    {
        if (key is "workbench.theme" or "workbench.uiLanguage") return;
        ApplyLanguageOptions();
        UpdateStatus();
        _menuModel = BuildMenuModel();
    }

    // ---- shutdown ----

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        _app.Workbench.SettingChanged -= OnSettingChanged;
        bool others = _app.Windows.Count > 1;
        foreach (var doc in _docs.ToList())
        {
            // work in a closed window stays in the journal (hot exit); a document without changes only when the whole app ends
            if (others && !doc.HasUnsavedWork) _app.Journal.Untrack(doc);
            doc.Closed.TrySetResult();
            doc.Dispose();
        }
        _docs.Clear();
        _app.WindowClosed(this);
    }
}

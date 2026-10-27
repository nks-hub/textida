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
using NksHub.Textida.Workbench.Settings;

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
    public EditorOptions Options => Active is { } tab ? OptionsFor(tab) : EditorOptions.From(_app.Workbench.Settings);

    /// <summary>Settings for a tab: its language layer, with the indentation detected from the file when the setting asks for it.</summary>
    internal EditorOptions OptionsFor(DocTab tab) => OptionsFor(tab.Doc);

    /// <summary>The same for a document: its <c>.editorconfig</c> layer goes into the settings stack first, so every value carries its source; an indentation set there beats the one detected from the content.</summary>
    internal EditorOptions OptionsFor(OpenDocument od)
    {
        EditorConfigSettings.Apply(_app.Workbench.Settings, od.EditorConfig);
        var options = EditorOptions.From(_app.Workbench.Settings, od.Language.Id);
        bool configIndent = od.EditorConfig.Properties.Keys.Any(k => k is "indent_style" or "indent_size" or "tab_width");
        return od.IndentOverride is { } indent && !configIndent ? options.WithIndent(indent) : options;
    }

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
            if (!_syncing && _docList.SelectedIndex is var i and >= 0 && i < _docListTabs.Count) SelectTab(_docListTabs[i]);
        };
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnWheelZoom, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnWindowDrop, Avalonia.Interactivity.RoutingStrategies.Bubble | Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver, Avalonia.Interactivity.RoutingStrategies.Bubble | Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(this, true);
        Activated += (_, _) => Dispatcher.UIThread.Post(FocusViewIfLost, DispatcherPriority.Input);
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
        _app.Workbench.SettingChanged += OnSettingChanged;
    }

    public SavedWindow CaptureGeometry() => new()
    {
        X = _normalPosition.X, Y = _normalPosition.Y, Width = _normalSize.Width, Height = _normalSize.Height,
        Maximized = WindowState == WindowState.Maximized || (IsFullScreen && _beforeFullScreen == WindowState.Maximized),
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

        var panel = BuildLeftColumn();

        var editorArea = new DockPanel { Children = { Dock(_notices, Avalonia.Controls.Dock.Top), _groupsGrid } };
        Grid.SetColumn(editorArea, 1);
        _body.Children.Add(panel);
        _body.Children.Add(editorArea);

        var content = new Border { Child = new DockPanel { Children = { Dock(BuildFindBar(), Avalonia.Controls.Dock.Bottom), Dock(BuildOutputPanel(), Avalonia.Controls.Dock.Bottom), new Panel { Children = { _body, BuildPalette() } } } } };
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
        FindActiveTabChanged();
        RefreshDocList();
        ApplyLanguageOptions();
        UpdateStatus();
        RefreshTitle();
        _menuModel = BuildMenuModel();
        var tab = Active;
        Dispatcher.UIThread.Post(() => { if (Active == tab && tab is not null && !_body.Children[0].IsKeyboardFocusWithin && !IsPaletteOpen && !IsFocusInTextInput()) FocusView(tab); }, DispatcherPriority.Input);
    }

    /// <summary>Gives the keyboard to the text area of the tab, so typing works at once without a click.</summary>
    private static void FocusView(DocTab tab)
    {
        if (tab.TableShown) tab.Table!.Focus();
        else if (tab.Surface is { } surface) surface.TextArea.Focus();
        else tab.View.Focus();
    }

    private static string EmptyHintText() => Loc.Get(K.WorkspaceEmpty, ("shortcut", Shortcuts.Display(Avalonia.Input.Key.N)));

    private bool IsFocusInTextInput() => FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>True when the keyboard focus is inside the active tab's text surface.</summary>
    public bool EditorHasFocus => Active is { } tab && FocusManager?.GetFocusedElement() is Visual v && (ReferenceEquals(v, tab.View) || tab.View.IsVisualAncestorOf(v) || (tab.Table is { } table && (ReferenceEquals(v, table) || table.IsVisualAncestorOf(v))));

    private void RefreshTitle() => Title = Active is null ? Loc.Get(K.AppName) : $"{Active.Title} - {Loc.Get(K.AppName)}";

    private void RefreshDocList()
    {
        _syncing = true;
        string filter = (_docFilter.Text ?? "").Trim();
        _docListTabs.Clear();
        _docList.Items.Clear();
        foreach (var tab in Tabs)
        {
            if (filter.Length > 0 && !Matches(filter, tab.Title + " " + tab.Path)) continue;
            bool modified = tab.Doc.HasUnsavedWork;
            var item = new ListBoxItem { Content = modified ? tab.Title + " ●" : tab.Title };
            AutomationProperties.SetName(item, modified ? Loc.Get(K.TabModifiedName, ("title", tab.Title)) : tab.Title);
            _docListTabs.Add(tab);
            _docList.Items.Add(item);
        }
        _docList.SelectedIndex = filter.Length > 0 ? (_docListTabs.Count > 0 ? 0 : -1) : Active is null ? -1 : _docListTabs.IndexOf(Active);
        _docListEmpty.IsVisible = _docListTabs.Count == 0;
        _docListEmpty.Text = Loc.Get(Tabs.Any() ? K.PanelDocumentsNoMatch : K.PanelDocumentsEmpty);
        _syncing = false;
    }

    /// <summary>Applies the settings of the active tab's language to every editor; called when the active tab or a setting changes.</summary>
    private void ApplyLanguageOptions()
    {
        Active?.Doc.RefreshEditorConfig();
        foreach (var tab in Tabs)
        {
            var options = OptionsFor(tab);
            tab.Surface?.ApplyOptions(options);
            tab.Paged?.ApplyOptions(options);
            tab.Hex?.ApplyOptions(options);
            tab.Table?.ApplyOptions(options);
            if (tab.Minimap is { } minimap) minimap.IsVisible = options.Minimap && !tab.TableShown;
        }
        if (Active is { } current) EditorConfigSettings.Apply(_app.Workbench.Settings, current.Doc.EditorConfig);
    }

    private void RefreshHeaders()
    {
        foreach (var g in _groups)
            if (g.Tabs.Any(t => TabGroup.HeaderStateOf(t) != t.HeaderState)) g.Refresh();
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
        AutomationProperties.SetName(_docList, Loc.Get(K.A11yDocumentList));
        ApplyFileTexts();
        ApplyHexTexts();
        ApplyTableTexts();
        ApplyOutputTexts();
        RefreshPluginUi();
        foreach (var g in _groups) AutomationProperties.SetName(g.Strip, Loc.Get(K.A11yTabs));
        AutomationProperties.SetName(_statusBar, Loc.Get(K.A11yStatusBar));
        AutomationProperties.SetName(_groupsGrid, Loc.Get(K.A11yWorkspace));
        foreach (var tab in Tabs) tab.Minimap?.UpdateName();
        ApplyPaletteTexts();
        ApplyFindTexts();
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
        if (key == "files.autoSave") foreach (var od in _docs) ConfigureAutosave(od);
        ApplyLanguageOptions();
        UpdateStatus();
        _menuModel = BuildMenuModel();
    }

    // ---- shutdown ----

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        _app.Workbench.SettingChanged -= OnSettingChanged;
        ReleaseNavigation();
        ReleaseOutput();
        ReleasePlugins();
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

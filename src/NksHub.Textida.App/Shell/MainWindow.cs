using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// D5 main window: only a text menu and tabs on top, the document below, a status bar at the bottom, a hidden list of open documents on the left.
/// Windows: menu in the title strip and tabs in a row under it. macOS: tabs in the title bar next to the traffic lights, menu in the system menu bar.
/// </summary>
internal sealed class MainWindow : Window
{
    private const double PanelWidth = 220;

    private readonly App _app;
    private readonly ShellLook _look;
    private readonly List<DocTab> _tabs = [];
    private DocTab? _active;
    private bool _syncing;

    private readonly TabStrip _tabStrip = new() { Background = Brushes.Transparent };
    private readonly ContentControl _editorHost = new();
    private readonly TextBlock _emptyHint = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _body = new() { ColumnDefinitions = new ColumnDefinitions("0,*") };
    private readonly ListBox _docList = new() { Background = Brushes.Transparent };
    private readonly TextBlock _docListTitle = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 6) };
    private readonly TextBlock _docListEmpty = new() { Margin = new Thickness(12, 4), Opacity = 0.7 };
    private readonly TextBlock _statusPosition = new();
    private readonly TextBlock _statusMessage = new();
    private readonly TextBlock _statusTabs = new();
    private readonly Border _statusBar = new() { Height = 26 };
    private readonly ContentControl _menuHost = new() { VerticalAlignment = VerticalAlignment.Center };

    private static readonly Geometry CloseGlyph = Geometry.Parse("M0,0 L8,8 M8,0 L0,8");
    private static int _untitledCounter;
    private PixelPoint _normalPosition;
    private Size _normalSize;

    public IReadOnlyList<DocTab> Tabs => _tabs;
    public DocTab? Active => _active;
    public bool DocumentsPanelOpen => _body.ColumnDefinitions[0].Width.Value > 0;

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
        ApplyTexts();
        Loc.Changed += OnLanguageChanged;
        PositionChanged += (_, e) => { if (WindowState == WindowState.Normal) _normalPosition = e.Point; };
        Resized += (_, e) => { if (WindowState == WindowState.Normal) _normalSize = e.ClientSize; };
        _tabStrip.SelectionChanged += (_, _) => { if (!_syncing && _tabStrip.SelectedIndex is var i and >= 0 && i < _tabs.Count) Select(_tabs[i]); };
        _docList.SelectionChanged += (_, _) => { if (!_syncing && _docList.SelectedIndex is var i and >= 0 && i < _tabs.Count) Select(_tabs[i]); };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !DocumentsPanelOpen) return;
            ToggleDocuments(false);
            e.Handled = true;
        };
        Closed += OnWindowClosed;
    }

    public SavedWindow CaptureGeometry() => new()
    {
        X = _normalPosition.X, Y = _normalPosition.Y, Width = _normalSize.Width, Height = _normalSize.Height,
        Maximized = WindowState == WindowState.Maximized,
    };

    private void BuildLayout()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };

        var tabRow = new Border { Child = _tabStrip, Padding = _look.TabsInTitleBar ? new Thickness(78, 0, 8, 0) : new Thickness(8, 2, 8, 0), VerticalAlignment = VerticalAlignment.Bottom };
        tabRow.Classes.Add("tabrow");
        WindowDecorationProperties.SetElementRole(_tabStrip, WindowDecorationsElementRole.User);

        if (_look.TabsInTitleBar)
        {
            tabRow.Height = 38;
            WindowDecorationProperties.SetElementRole(tabRow, WindowDecorationsElementRole.TitleBar);
            Grid.SetRow(tabRow, 0);
            root.Children.Add(tabRow);
        }
        else
        {
            var mark = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = Loc.Get(K.AppName)[..1], FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            mark.Classes.Add("mark");
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Height = 32 };
            Grid.SetColumn(_menuHost, 1);
            title.Children.Add(mark);
            title.Children.Add(_menuHost);
            WindowDecorationProperties.SetElementRole(title, WindowDecorationsElementRole.TitleBar);
            WindowDecorationProperties.SetElementRole(_menuHost, WindowDecorationsElementRole.User);
            Grid.SetRow(title, 0);
            Grid.SetRow(tabRow, 1);
            root.Children.Add(title);
            root.Children.Add(tabRow);
        }

        var panel = new Border { Child = new DockPanel { Children = { Dock(_docListTitle, Avalonia.Controls.Dock.Top), Dock(_docListEmpty, Avalonia.Controls.Dock.Bottom), _docList } } };
        panel.Classes.Add("sidepanel");
        Grid.SetColumn(_editorHost, 1);
        _body.Children.Add(panel);
        _body.Children.Add(_editorHost);
        var content = new Border { Child = _body };
        content.Classes.Add("content");
        Grid.SetRow(content, 2);
        root.Children.Add(content);

        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center };
        _statusPosition.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(_statusMessage, 1);
        Grid.SetColumn(_statusTabs, 2);
        status.Children.Add(_statusPosition);
        status.Children.Add(_statusMessage);
        status.Children.Add(_statusTabs);
        _statusBar.Child = status;
        _statusBar.Classes.Add("statusbar");
        Grid.SetRow(_statusBar, 3);
        root.Children.Add(_statusBar);

        _emptyHint.Classes.Add("hint");
        Content = root;
    }

    private static Control Dock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    // ---- documents ----

    public DocTab NewDocument()
    {
        var tab = new DocTab(null, ++_untitledCounter, "");
        Add(tab);
        return tab;
    }

    /// <summary>Opens a file in this window or activates its tab. Throws <see cref="IOException"/> with a localised message when it cannot be read.</summary>
    public DocTab OpenFile(string path, int? line)
    {
        path = Path.GetFullPath(path);
        var tab = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, PathComparison)) ?? Add(Read(path));
        Select(tab);
        if (line is { } l) tab.GoToLine(l);
        tab.Editor.Focus();
        return tab;
    }

    public static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private DocTab Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException(null, path);
            if (info.Length > DocTab.MaxFileBytes) throw new IOException(Loc.Get(K.ErrorOpenTooLarge, ("path", path)));
            var bytes = File.ReadAllBytes(path);
            const int sampleLength = 64 * 1024;
            var guess = EncodingGuesser.Guess(bytes.AsSpan(0, Math.Min(bytes.Length, sampleLength)), wholeFile: bytes.Length <= sampleLength);
            var text = guess.Codec.Decode(bytes).Text;
            return new DocTab(path, 0, text) { EncodingName = guess.Codec.Name };
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            throw new IOException(e is IOException io && io.Message.Contains(path, StringComparison.Ordinal) ? io.Message : Loc.Get(K.ErrorOpenFailed, ("path", path), ("reason", e.Message)), e);
        }
    }

    private DocTab Add(DocTab tab)
    {
        _tabs.Add(tab);
        tab.Editor.GetObservable(TextBox.CaretIndexProperty).Subscribe(new ActionObserver<int>(_ => { if (tab == _active) UpdateStatus(); }));
        AutomationProperties.SetName(tab.Editor, Loc.Get(K.A11yEditor));
        tab.Editor.PlaceholderText = tab.ReadOnly ? null : Loc.Get(K.EditorPlaceholder);
        Select(tab);
        return tab;
    }

    public void Select(DocTab tab)
    {
        _active = tab;
        _editorHost.Content = tab.Editor;
        RefreshTabs();
        UpdateStatus();
        Dispatcher.UIThread.Post(() => { if (_active == tab && !_docList.IsKeyboardFocusWithin) tab.Editor.Focus(); }, DispatcherPriority.Input);
    }

    public void CloseTab(DocTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        tab.Closed.TrySetResult();
        if (_active == tab)
        {
            _active = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            _editorHost.Content = (Control?)_active?.Editor ?? _emptyHint;
        }
        RefreshTabs();
        UpdateStatus();
    }

    private void RefreshTabs()
    {
        _syncing = true;
        _tabStrip.Items.Clear();
        _docList.Items.Clear();
        foreach (var tab in _tabs)
        {
            var title = new TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center };
            var close = new Button { Content = new Avalonia.Controls.Shapes.Path { Data = CloseGlyph, StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None }, Margin = new Thickness(8, 0, 0, 0) };
            close.Classes.Add("tabclose");
            AutomationProperties.SetName(close, Loc.Get(K.TabCloseA11y, ("title", tab.Title)));
            var captured = tab;
            close.Click += (_, e) => { CloseTab(captured); e.Handled = true; };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 6, 0), Children = { title, close } };
            var item = new TabStripItem { Content = header };
            AutomationProperties.SetName(item, tab.Title);
            _tabStrip.Items.Add(item);
            _docList.Items.Add(new ListBoxItem { Content = tab.Title });
        }
        int selected = _active is null ? -1 : _tabs.IndexOf(_active);
        _tabStrip.SelectedIndex = selected;
        _docList.SelectedIndex = selected;
        _docListEmpty.IsVisible = _tabs.Count == 0;
        if (_tabs.Count == 0) _editorHost.Content = _emptyHint;
        _syncing = false;
        Title = _active is null ? Loc.Get(K.AppName) : $"{_active.Title} - {Loc.Get(K.AppName)}";
    }

    // ---- panels ----

    public void ToggleDocuments(bool? open = null)
    {
        bool show = open ?? !DocumentsPanelOpen;
        _body.ColumnDefinitions[0].Width = new GridLength(show ? PanelWidth : 0);
        _menuModel = BuildMenuModel();
        if (show) _docList.Focus();
        else _active?.Editor.Focus();
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
        _emptyHint.Text = Loc.Get(K.WorkspaceEmpty, ("shortcut", Shortcuts.Display(Key.N)));
        _docListTitle.Text = Loc.Get(K.PanelDocumentsTitle);
        _docListEmpty.Text = Loc.Get(K.PanelDocumentsEmpty);
        AutomationProperties.SetName(_docList, Loc.Get(K.A11yDocumentList));
        AutomationProperties.SetName(_tabStrip, Loc.Get(K.A11yTabs));
        AutomationProperties.SetName(_statusBar, Loc.Get(K.A11yStatusBar));
        AutomationProperties.SetName(_editorHost, Loc.Get(K.A11yWorkspace));
        AutomationProperties.SetName(_menuHost, Loc.Get(K.A11yMainMenu));
        foreach (var tab in _tabs) tab.Editor.PlaceholderText = tab.ReadOnly ? null : Loc.Get(K.EditorPlaceholder);

        _menuModel = BuildMenuModel();
        RefreshTabs();
        UpdateStatus();
    }

    public void SetStatusMessage(string text) => _statusMessage.Text = text;

    private void UpdateStatus()
    {
        if (_active is { } tab)
        {
            var (line, column) = tab.CaretPosition();
            _statusPosition.Text = Loc.Get(K.StatusPosition, ("line", line), ("column", column));
            _statusMessage.Text = tab.ReadOnly ? $"{tab.EncodingName} · {Loc.Get(K.StatusReadOnly)}" : "";
        }
        else
        {
            _statusPosition.Text = "";
            _statusMessage.Text = Loc.Get(K.StatusReady);
        }
        _statusTabs.Text = Loc.Plural(K.StatusTabs, _tabs.Count);
    }

    // ---- menu ----

    private IReadOnlyList<MenuGroup> BuildMenuModel()
    {
        var theme = _app.Settings.Theme;
        MenuCommand Theme(string label, string tag) => new(label, () => _app.SetTheme(tag), Checked: theme == tag, Radio: true);
        MenuCommand Lang(string label, string tag) => new(label, () => _app.SetLanguage(tag), Checked: Loc.Language == tag, Radio: true);
        MenuCommand Edit(string label, Key key, Action run, bool shift = false) => new(label, run, Shortcuts.Gesture(key, shift), ShowOnly: true);

        var file = new List<MenuNode>
        {
            new MenuCommand(Loc.Get(K.MenuFileNew), () => NewDocument(), Shortcuts.Gesture(Key.N)),
            new MenuCommand(Loc.Get(K.MenuFileNewWindow), () => _app.NewWindow(this), Shortcuts.Gesture(Key.N, shift: true)),
            new MenuCommand(Loc.Get(K.MenuFileOpen), () => _ = OpenWithDialogAsync(), Shortcuts.Gesture(Key.O)),
            new MenuSeparator(),
            new MenuCommand(Loc.Get(K.MenuFileClose), () => { if (_active is { } t) CloseTab(t); }, Shortcuts.Gesture(Key.W)),
        };
        if (!_look.NativeMenu) file.AddRange([new MenuSeparator(), new MenuCommand(Loc.Get(K.MenuFileExit), _app.Exit)]);

        var groups = new List<MenuGroup>
        {
            new(Loc.Get(K.MenuFile), file),
            new(Loc.Get(K.MenuEdit),
            [
                Edit(Loc.Get(K.MenuEditUndo), Key.Z, () => _active?.Editor.Undo()),
                Edit(Loc.Get(K.MenuEditRedo), Key.Y, () => _active?.Editor.Redo()),
                new MenuSeparator(),
                Edit(Loc.Get(K.MenuEditCut), Key.X, () => _active?.Editor.Cut()),
                Edit(Loc.Get(K.MenuEditCopy), Key.C, () => _active?.Editor.Copy()),
                Edit(Loc.Get(K.MenuEditPaste), Key.V, () => _active?.Editor.Paste()),
                Edit(Loc.Get(K.MenuEditSelectAll), Key.A, () => _active?.Editor.SelectAll()),
            ]),
            new(Loc.Get(K.MenuView),
            [
                new MenuCommand(Loc.Get(K.MenuViewDocuments), () => ToggleDocuments(), Shortcuts.Gesture(Key.B), Checked: DocumentsPanelOpen),
                new MenuSeparator(),
                new MenuGroup(Loc.Get(K.MenuViewTheme), [Theme(Loc.Get(K.MenuViewThemeSystem), "system"), Theme(Loc.Get(K.MenuViewThemeLight), "light"), Theme(Loc.Get(K.MenuViewThemeDark), "dark")]),
                new MenuGroup(Loc.Get(K.MenuViewLanguage), [Lang(Loc.Get(K.LanguageNameCs), "cs"), Lang(Loc.Get(K.LanguageNameEn), "en")]),
            ]),
            new(Loc.Get(K.MenuHelp), [new MenuCommand(Loc.Get(K.MenuHelpAbout, ("name", Loc.Get(K.AppName))), () => _ = ShowAboutAsync())]),
        };

        if (_look.NativeMenu)
        {
            if (_nativeMenu is null) NativeMenu.SetMenu(this, _nativeMenu = new NativeMenu());
            MenuRenderer.FillNative(_nativeMenu, groups);
        }
        else _menuHost.Content = MenuRenderer.ToMenu(groups);
        return groups;
    }

    private async Task OpenWithDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.Get(K.DialogOpenTitle), AllowMultiple = true });
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try { OpenFile(path, null); }
            catch (IOException e) { SetStatusMessage(e.Message); }
        }
    }

    private async Task ShowAboutAsync()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        var close = new Button { Content = Loc.Get(K.DialogAboutClose), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = Loc.Get(K.DialogAboutTitle, ("name", Loc.Get(K.AppName))),
            Width = 340, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 10,
                Children = { new TextBlock { Text = Loc.Get(K.AppName), FontSize = 20, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = Loc.Get(K.DialogAboutVersion, ("version", version)) }, close },
            },
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Loc.Changed -= OnLanguageChanged;
        foreach (var tab in _tabs) tab.Closed.TrySetResult();
        _app.WindowClosed(this);
    }
}

internal sealed class ActionObserver<T>(Action<T> onNext) : IObserver<T>
{
    public void OnNext(T value) => onNext(value);
    public void OnError(Exception error) { }
    public void OnCompleted() { }
}

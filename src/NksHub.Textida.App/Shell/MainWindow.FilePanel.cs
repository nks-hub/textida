using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

internal enum LeftPanel { Documents, Files, Plugin }

/// <summary>
/// The panels on the left, one at a time, hidden until asked for: the open documents (Ctrl+B) and the files of a folder on disk (Ctrl+Shift+E).
/// </summary>
internal sealed partial class MainWindow
{
    private static readonly Geometry FolderGlyph = Geometry.Parse("M2,4.5 L6,4.5 L7.5,6 L14,6 L14,12.5 L2,12.5 Z");
    private static readonly Geometry FileGlyph = Geometry.Parse("M4,2.5 L9.5,2.5 L12,5 L12,13.5 L4,13.5 Z M9.5,2.5 L9.5,5 L12,5");
    private static readonly Geometry ChevronGlyph = Geometry.Parse("M5.5,3.5 L10,8 L5.5,12.5");

    private readonly DockPanel _docPanel = new();
    private readonly DockPanel _filePanel = new() { IsVisible = false };
    private readonly TextBox _docFilter = new() { Margin = new Thickness(8, 0, 8, 6) };
    private readonly List<DocTab> _docListTabs = [];
    private readonly TextBlock _filesTitle = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ListBox _fileList = new() { Background = Brushes.Transparent };
    private readonly StackPanel _filesEmpty = new() { Margin = new Thickness(12, 4), Spacing = 8 };
    private readonly TextBlock _filesEmptyText = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };
    private readonly Button _filesOpenButton = new();
    private FileTree? _fileTree;

    public bool DocumentsPanelOpen => _body.ColumnDefinitions[0].Width.Value > 0 && _docPanel.IsVisible;
    public bool FilePanelOpen => _body.ColumnDefinitions[0].Width.Value > 0 && _filePanel.IsVisible;

    internal FileTree? Files => _fileTree;
    internal Control FilePanelControl => _filePanel;
    internal ListBox FileList => _fileList;
    internal TextBox DocumentFilter => _docFilter;
    internal IReadOnlyList<DocTab> DocumentListTabs => _docListTabs;

    /// <summary>The row selected in the file panel, or null.</summary>
    internal FileTreeRow? SelectedFileRow => _fileList.SelectedItem is FileTreeRow row ? row : null;

    private Border BuildLeftColumn()
    {
        _docList.Classes.Add("doclist");
        _docFilter.Classes.Add("panelfilter");
        _docFilter.TextChanged += (_, _) => RefreshDocList();
        _docFilter.AddHandler(KeyDownEvent, OnDocFilterKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _docPanel.Children.Add(Dock(_docListTitle, Avalonia.Controls.Dock.Top));
        _docPanel.Children.Add(Dock(_docFilter, Avalonia.Controls.Dock.Top));
        _docPanel.Children.Add(Dock(_docListEmpty, Avalonia.Controls.Dock.Bottom));
        _docPanel.Children.Add(_docList);

        _fileList.Classes.Add("filelist");
        _fileList.IsVisible = false;
        _fileList.Focusable = true; // a list is not a tab stop by default; the panel hands it the keyboard when it opens
        _docList.Focusable = true;
        _fileList.ItemTemplate = new FuncDataTemplate<FileTreeRow>((row, _) => BuildFileRow(row));
        _fileList.DoubleTapped += (_, _) => _ = ActivateFileRowAsync();
        _fileList.AddHandler(KeyDownEvent, OnFileListKey, Avalonia.Interactivity.RoutingStrategies.Tunnel); // before the list takes the arrow keys for itself
        _fileList.PointerPressed += OnFileListPointer;
        _filesOpenButton.Classes.Add("panelaction");
        _filesOpenButton.Click += (_, _) => _ = OpenFolderWithDialogAsync();
        _filesEmpty.Children.Add(_filesEmptyText);
        _filesEmpty.Children.Add(_filesOpenButton);
        _filePanel.Children.Add(Dock(_filesTitle, Avalonia.Controls.Dock.Top));
        _filePanel.Children.Add(Dock(_filesEmpty, Avalonia.Controls.Dock.Top));
        _filePanel.Children.Add(_fileList);

        var host = new Border { Child = new Panel { Children = { _docPanel, _filePanel, BuildPluginPanel() } } };
        host.Classes.Add("sidepanel");
        return host;
    }

    private void ApplyFileTexts()
    {
        _docFilter.PlaceholderText = Loc.Get(K.PanelDocumentsFilter);
        AutomationProperties.SetName(_docFilter, Loc.Get(K.A11yDocumentsFilter));
        _filesTitle.Text = _fileTree is { } tree ? Path.GetFileName(tree.Root) is { Length: > 0 } n ? n : tree.Root : Loc.Get(K.PanelFilesTitle);
        _filesOpenButton.Content = Loc.Get(K.PanelFilesOpen);
        UpdateFilesEmpty();
        AutomationProperties.SetName(_filePanel, Loc.Get(K.A11yFilesPanel));
        AutomationProperties.SetName(_fileList, Loc.Get(K.A11yFilesTree));
        AutomationProperties.SetName(_filesOpenButton, Loc.Get(K.PanelFilesOpen));
        _fileList.ContextMenu = BuildFileMenu();
        if (_fileTree is not null) SyncFileList();
    }

    private ContextMenu BuildFileMenu()
    {
        var wb = _app.Workbench;
        var menu = new ContextMenu();
        foreach (var id in new[] { "nks.files.newFile", "nks.files.newFolder", "nks.files.duplicate", "nks.files.rename", "nks.files.move", "nks.files.delete", "nks.files.refresh" })
        {
            if (!wb.Commands.TryGet(id, out var command)) continue;
            var item = new MenuItem { Header = wb.Strings.Get(command.TitleKey) };
            string commandId = id;
            item.Click += (_, _) => _ = wb.Commands.ExecuteAsync(commandId, new ShellContext(this, forMenu: true));
            menu.Items.Add(item);
        }
        return menu;
    }

    // ---- showing the panels ----

    public void ToggleDocuments(bool? open = null) => ToggleLeft(LeftPanel.Documents, open);

    public void ToggleFiles(bool? open = null) => ToggleLeft(LeftPanel.Files, open);

    private void ToggleLeft(LeftPanel which, bool? open)
    {
        bool isOpen = which switch { LeftPanel.Documents => DocumentsPanelOpen, LeftPanel.Files => FilePanelOpen, _ => PluginPanelOpen };
        bool show = open ?? !isOpen;
        if (show)
        {
            _docPanel.IsVisible = which == LeftPanel.Documents;
            _filePanel.IsVisible = which == LeftPanel.Files;
            _pluginPanel.IsVisible = which == LeftPanel.Plugin;
            if (which == LeftPanel.Files) RestoreFolder();
        }
        _body.ColumnDefinitions[0].Width = new GridLength(show ? PanelWidth : 0);
        _menuModel = BuildMenuModel();
        if (show) Dispatcher.UIThread.Post(() => (which == LeftPanel.Documents ? (Control)_docFilter : which == LeftPanel.Plugin ? _pluginTree : _fileTree is null ? _filesOpenButton : _fileList).Focus(), DispatcherPriority.Input);
        else if (Active is { } tab) FocusView(tab);
    }

    /// <summary>Closes whichever left panel is open; false when none was.</summary>
    private bool CloseLeftPanel()
    {
        if (!DocumentsPanelOpen && !FilePanelOpen) return false;
        ToggleLeft(DocumentsPanelOpen ? LeftPanel.Documents : LeftPanel.Files, false);
        return true;
    }

    // ---- the list of open documents with its filter ----

    private void OnDocFilterKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up when _docListTabs.Count > 0:
                _syncing = true;
                _docList.SelectedIndex = Math.Clamp(_docList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _docListTabs.Count - 1);
                _syncing = false;
                e.Handled = true;
                break;
            case Key.Enter:
                if (_docList.SelectedIndex is >= 0 and var i && i < _docListTabs.Count)
                {
                    var tab = _docListTabs[i];
                    _docFilter.Text = "";
                    SelectTab(tab);
                    FocusView(tab);
                }
                e.Handled = true;
                break;
        }
    }

    // ---- the files of a folder ----

    /// <summary>Opens a folder in the panel (reading only its first level) and shows the panel. False, with a message, when the folder cannot be read.</summary>
    internal async Task<bool> ShowFolderAsync(string path)
    {
        var tree = new FileTree(path);
        try
        {
            await tree.LoadRootAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            tree.Dispose();
            SetStatusMessage(Loc.Get(K.FilesErrorOpenFolder, ("reason", e.Message)));
            return false;
        }
        SetFileTree(tree);
        ToggleLeft(LeftPanel.Files, true);
        return true;
    }

    private void SetFileTree(FileTree tree)
    {
        _fileTree?.Dispose();
        _fileTree = tree;
        InvalidateWorkspaceIndex();
        tree.Changed += SyncFileList;
        tree.DiskChanged += OnFolderChangedOnDisk;
        tree.Watch();
        _app.Recent.SetFolder(tree.Root);
        ApplyFileTexts();
        _menuModel = BuildMenuModel();
        if (IsPaletteOpen && _paletteMode == PaletteMode.Files) { EnsureWorkspaceIndex(); RefreshPalette(); }
        // the panel was opened before the folder was read: the keyboard waited on the button that is gone now
        if (FilePanelOpen && !IsPaletteOpen) Dispatcher.UIThread.Post(() => _fileList.Focus(), DispatcherPriority.Input);
    }

    /// <summary>The folder of the last run is read when the panel or Go to Anything is first used, not at startup.</summary>
    private void RestoreFolder()
    {
        if (_fileTree is not null || _app.Recent.Folder is not { } folder || !Directory.Exists(folder)) return;
        _ = ShowFolderAsync(folder);
    }

    internal async Task OpenFolderWithDialogAsync()
    {
        if (await _app.Dialogs.PickFolderAsync(this) is { } folder) await ShowFolderAsync(folder);
    }

    private void OnFolderChangedOnDisk(IReadOnlyCollection<string> directories) => Dispatcher.UIThread.Post(async () =>
    {
        if (_fileTree is not { } tree) return;
        InvalidateWorkspaceIndex();
        try
        {
            foreach (var directory in directories.Where(tree.IsLoaded).ToList()) await tree.RefreshAsync(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the next change tries again */ }
    });

    /// <summary>Shows the rows of the tree in the list, keeping the selection and the scroll position.</summary>
    private void SyncFileList()
    {
        if (_fileTree is not { } tree) return;
        string? keep = SelectedFileRow?.Entry.Path;
        var scroll = _fileList.Scroll as ScrollViewer;
        var offset = scroll?.Offset;
        bool hadFocus = _fileList.IsKeyboardFocusWithin;
        _fileList.ItemsSource = tree.Rows;
        _fileList.SelectedIndex = keep is null ? -1 : tree.IndexOf(keep);
        // the row that held the keyboard was replaced with the others; without this the keys would go nowhere
        if (hadFocus) Dispatcher.UIThread.Post(() => _fileList.Focus(), DispatcherPriority.Input);
        UpdateFilesEmpty();
        _filesTitle.Text = Path.GetFileName(tree.Root) is { Length: > 0 } n ? n : tree.Root;
        if (offset is { } o) Dispatcher.UIThread.Post(() => { if (_fileList.Scroll is ScrollViewer sv) sv.Offset = o; }, DispatcherPriority.Loaded);
        _menuModel = BuildMenuModel();
    }

    private void UpdateFilesEmpty()
    {
        bool noFolder = _fileTree is null;
        bool emptyFolder = _fileTree is { Rows.Count: 0 };
        _filesEmpty.IsVisible = noFolder || emptyFolder;
        _filesOpenButton.IsVisible = noFolder;
        _filesEmptyText.Text = Loc.Get(noFolder ? K.PanelFilesEmpty : K.PanelFilesEmptyFolder);
        _fileList.IsVisible = !noFolder;
    }

    private Control BuildFileRow(FileTreeRow row)
    {
        var entry = row.Entry;
        var chevron = new Avalonia.Controls.Shapes.Path { Data = ChevronGlyph, Width = 16, Height = 16, Stretch = Stretch.None, IsVisible = entry.IsDirectory, RenderTransform = new RotateTransform(row.Expanded ? 90 : 0) };
        chevron.Classes.Add("fileglyph");
        chevron.PointerPressed += (_, e) => { _ = ToggleFolderAsync(entry.Path); e.Handled = true; };
        var icon = new Avalonia.Controls.Shapes.Path { Data = entry.IsDirectory ? FolderGlyph : FileGlyph, Width = 16, Height = 16, Stretch = Stretch.None, Margin = new Thickness(2, 0, 6, 0) };
        icon.Classes.Add("fileglyph");
        var name = new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(row.Depth * 14, 0, 4, 0), Height = 24 };
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(name, 2);
        grid.Children.Add(chevron);
        grid.Children.Add(icon);
        grid.Children.Add(name);
        AutomationProperties.SetName(grid, Loc.Get(!entry.IsDirectory ? K.A11yFilesFile : row.Expanded ? K.A11yFilesFolderOpen : K.A11yFilesFolderClosed, ("name", entry.Name)));
        return grid;
    }

    private async Task ToggleFolderAsync(string directory)
    {
        if (_fileTree is not { } tree) return;
        if (tree.IsExpanded(directory)) tree.Collapse(directory);
        else await tree.ExpandAsync(directory);
    }

    /// <summary>Enter or a double click: a folder opens or closes, a file opens in a tab.</summary>
    internal async Task ActivateFileRowAsync()
    {
        if (SelectedFileRow is not { } row) return;
        if (row.Entry.IsDirectory) await ToggleFolderAsync(row.Entry.Path);
        else JumpToFile(row.Entry.Path, null);
    }

    private void OnFileListKey(object? sender, KeyEventArgs e)
    {
        if (_fileTree is not { } tree) return;
        var row = SelectedFileRow;
        switch (e.Key)
        {
            case Key.Enter:
                _ = ActivateFileRowAsync();
                break;
            case Key.Right when row is { Entry.IsDirectory: true, Expanded: false }:
                _ = tree.ExpandAsync(row.Value.Entry.Path);
                break;
            case Key.Left when row is { Entry.IsDirectory: true, Expanded: true }:
                tree.Collapse(row.Value.Entry.Path);
                break;
            case Key.Left when row is { } r && r.Depth > 0:
                SelectFilePath(Path.GetDirectoryName(r.Entry.Path)!);
                break;
            case Key.F2: _ = RunFileCommand("nks.files.rename"); break;
            case Key.Delete: _ = RunFileCommand("nks.files.delete"); break;
            case Key.F5: _ = RunFileCommand("nks.files.refresh"); break;
            case Key.Escape: CloseLeftPanel(); break;
            default: return;
        }
        e.Handled = true;
    }

    private ValueTask<NksHub.Textida.Workbench.Commands.CommandOutcome> RunFileCommand(string id) =>
        _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(this, forMenu: true));

    private void OnFileListPointer(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_fileList).Properties.IsRightButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is { DataContext: FileTreeRow } item) _fileList.SelectedItem = item.DataContext;
    }

    internal void SelectFilePath(string path)
    {
        if (_fileTree is not { } tree) return;
        int index = tree.IndexOf(path);
        if (index < 0) return;
        _fileList.SelectedIndex = index;
        _fileList.ScrollIntoView(index);
    }

    /// <summary>The folder a new file goes into: the selected folder, the folder of the selected file, or the root.</summary>
    private string TargetDirectory() => SelectedFileRow is { } row ? row.Entry.IsDirectory ? row.Entry.Path : Path.GetDirectoryName(row.Entry.Path)! : _fileTree!.Root;
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// How a row of the file tree looks: an icon and a colour by the kind of the file, a dimmed row for a hidden or ignored entry, and the state Git gives the file as a colour and a letter
/// after the name (a folder shows the heaviest state below it). Git is asked through its plugin, and only for a folder that lies in a repository; the state is read again when a file of the
/// tree changes and when the index or <c>HEAD</c> of the repository change, also by another program.
/// </summary>
internal sealed partial class MainWindow
{
    private IReadOnlyList<GitFileStatus> _gitFiles = [];
    private bool _gitFilesRunning, _gitFilesAgain, _gitFilesStale;
    private DispatcherTimer? _gitFilesTimer;
    private readonly List<FileSystemWatcher> _gitDirectoryWatchers = [];

    internal bool ShowHiddenFiles => _app.Workbench.Settings.GetBool("files.showHidden");

    private bool ShowGitStatus => _app.Workbench.Settings.GetBool("files.gitStatus");

    internal IReadOnlyList<GitFileStatus> GitFiles => _gitFiles;

    internal void ToggleHiddenFiles() => ToggleSetting("files.showHidden");

    /// <summary>The row of the tree as it is drawn. Internal for tests.</summary>
    internal Control BuildFileRow(FileTreeRow row)
    {
        var entry = row.Entry;
        var kind = FileKinds.Of(entry.Name, entry.IsDirectory);
        var mark = ShowGitStatus ? GitFileStatus.StateOf(_gitFiles, entry.Path, entry.IsDirectory) : default;
        string display = FileNames.Display(entry.Name);

        var chevron = new Avalonia.Controls.Shapes.Path { Data = ChevronGlyph, Width = 16, Height = 16, Stretch = Stretch.None, IsVisible = entry.IsDirectory, RenderTransform = new RotateTransform(row.Expanded ? 90 : 0) };
        chevron.Classes.Add("fileglyph");
        chevron.PointerPressed += (_, e) => { _ = ToggleFolderAsync(entry.Path); e.Handled = true; };
        var icon = new Avalonia.Controls.Shapes.Path { Data = FileGlyphs.Of(kind, row.Expanded), Width = 16, Height = 16, Stretch = Stretch.None, Margin = new Thickness(2, 0, 6, 0) };
        icon.Classes.Add("fileglyph");
        icon.Classes.Add(FileGlyphs.ClassOf(kind));
        var name = new TextBlock { Text = display, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(row.Depth * 14, 0, 4, 0), Height = 24 };
        grid.Classes.Add("filerow");
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(name, 2);
        grid.Children.Add(chevron);
        grid.Children.Add(icon);
        grid.Children.Add(name);

        string label = entry.IsDirectory
            ? Loc.Get(row.Expanded ? K.A11yFilesFolderOpen : K.A11yFilesFolderClosed, ("name", display))
            : Loc.Get(K.A11yFilesFile, ("name", display), ("kind", Loc.Get(KindKey(kind))));
        if (entry.Hidden)
        {
            grid.Classes.Add("hiddenfile");
            label += ", " + Loc.Get(K.A11yFilesHidden);
        }
        if (mark.State != GitFileState.None)
        {
            string state = Loc.Get(GitKey(mark));
            grid.Classes.Add("git" + mark.State.ToString().ToLowerInvariant());
            var letter = new TextBlock { Text = GitLetter(mark.State) };
            letter.Classes.Add("gitmark");
            ToolTip.SetTip(letter, state);
            Grid.SetColumn(letter, 3);
            grid.Children.Add(letter);
            label += ", " + state;
        }
        AutomationProperties.SetName(grid, label);
        return grid;
    }

    private static string KindKey(FileKind kind) => kind switch
    {
        FileKind.Text => K.A11yFilesKindText, FileKind.Code => K.A11yFilesKindCode, FileKind.Markup => K.A11yFilesKindMarkup, FileKind.Data => K.A11yFilesKindData,
        FileKind.Style => K.A11yFilesKindStyle, FileKind.Image => K.A11yFilesKindImage, FileKind.Archive => K.A11yFilesKindArchive, FileKind.Binary => K.A11yFilesKindBinary,
        _ => K.A11yFilesKindFile,
    };

    private static string GitKey(GitFileMark mark) => (mark.State, mark.Below) switch
    {
        (GitFileState.Ignored, _) => K.A11yFilesGitIgnored,
        (GitFileState.New, false) => K.A11yFilesGitNew,
        (GitFileState.New, true) => K.A11yFilesGitBelowNew,
        (GitFileState.Deleted, false) => K.A11yFilesGitDeleted,
        (GitFileState.Deleted, true) => K.A11yFilesGitBelowDeleted,
        (GitFileState.Modified, false) => K.A11yFilesGitModified,
        (GitFileState.Modified, true) => K.A11yFilesGitBelowModified,
        (_, false) => K.A11yFilesGitConflict,
        _ => K.A11yFilesGitBelowConflict,
    };

    private static string GitLetter(GitFileState state) => state switch
    {
        GitFileState.Modified => "M", GitFileState.New => "N", GitFileState.Deleted => "D", GitFileState.Ignored => "I", _ => "!",
    };

    // ---- the state Git gives the files ----

    /// <summary>A setting of the tree changed, or a tree was opened: hidden entries and the Git state are applied to it.</summary>
    private void ApplyFileLookSettings(string? key = null)
    {
        if (_fileTree is not { } tree) return;
        if (key is null or "files.showHidden") tree.ShowHidden = ShowHiddenFiles;
        if (key is null or "files.gitStatus") RefreshGitFiles(now: true);
    }

    /// <summary>A file of the tree changed. While the panel is closed nothing is read; the panel reads when it opens again.</summary>
    private void OnTreeContentChanged(FileTree tree)
    {
        if (!ReferenceEquals(_fileTree, tree) || !ShowGitStatus) return;
        if (FilePanelOpen) RefreshGitFiles(now: true);
        else _gitFilesStale = true;
    }

    /// <summary>Reads the state of the files again: at once, or once the changes of the repository are over (they come in bursts).</summary>
    private void RefreshGitFiles(bool now = false)
    {
        _gitFilesTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.GitWatchDebounceMilliseconds) };
        _gitFilesTimer.Tick -= OnGitFilesTimer;
        _gitFilesTimer.Stop();
        if (now) _ = ReadGitFilesAsync();
        else
        {
            _gitFilesTimer.Tick += OnGitFilesTimer;
            _gitFilesTimer.Start();
        }
    }

    private void OnGitFilesTimer(object? sender, EventArgs e)
    {
        _gitFilesTimer?.Stop();
        _ = ReadGitFilesAsync();
    }

    private async Task ReadGitFilesAsync()
    {
        if (_gitFilesRunning)
        {
            _gitFilesAgain = true;
            return;
        }
        _gitFilesRunning = true;
        try
        {
            do
            {
                _gitFilesAgain = false;
                _gitFilesStale = false;
                var tree = _fileTree;
                var found = new List<GitFileStatus>();
                if (tree is not null && ShowGitStatus)
                    foreach (var root in tree.Roots)
                        if (Directory.Exists(root) && await _app.Plugins.GitTreeStatusAsync(root) is { } status) found.Add(status);
                if (ReferenceEquals(tree, _fileTree)) ApplyGitFiles(found);
            }
            while (_gitFilesAgain);
        }
        finally { _gitFilesRunning = false; }
    }

    private void ApplyGitFiles(List<GitFileStatus> found)
    {
        WatchGitDirectories(found);
        bool same = found.Count == _gitFiles.Count && found.Zip(_gitFiles).All(p => p.First.SameAs(p.Second));
        if (same) return;
        _gitFiles = found;
        if (_fileTree is not null) SyncFileList(redraw: true);
    }

    /// <summary>The index and <c>HEAD</c> of each repository are watched, so a commit, a stage or a checkout made by another program shows. Nothing is watched when no folder is in a repository.</summary>
    private void WatchGitDirectories(IReadOnlyList<GitFileStatus> statuses)
    {
        var wanted = statuses.Select(s => GitFileStatus.FindGitDirectory(s.Root)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == _gitDirectoryWatchers.Count && wanted.All(w => _gitDirectoryWatchers.Any(x => string.Equals(x.Path, w, StringComparison.OrdinalIgnoreCase)))) return;
        ReleaseGitDirectoryWatchers();
        foreach (var directory in wanted)
        {
            try
            {
                var watcher = new FileSystemWatcher(directory) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size, IncludeSubdirectories = false };
                void OnGitFile(object? s, FileSystemEventArgs e)
                {
                    if (e.Name is "index" or "HEAD") Dispatcher.UIThread.Post(() => { if (FilePanelOpen) RefreshGitFiles(); else _gitFilesStale = true; });
                }
                watcher.Created += OnGitFile;
                watcher.Changed += OnGitFile;
                watcher.Deleted += OnGitFile;
                watcher.Renamed += (s, e) => OnGitFile(s, e);
                watcher.EnableRaisingEvents = true;
                _gitDirectoryWatchers.Add(watcher);
            }
            catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // the folder cannot be watched: the state still follows the files of the tree
            }
        }
    }

    private void ReleaseGitDirectoryWatchers()
    {
        foreach (var watcher in _gitDirectoryWatchers) watcher.Dispose();
        _gitDirectoryWatchers.Clear();
    }

    private void ReleaseGitFiles()
    {
        _gitFilesTimer?.Stop();
        ReleaseGitDirectoryWatchers();
    }
}

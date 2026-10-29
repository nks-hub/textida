using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>A file the commit panel lists: the two status letters of <c>git status</c> (space = unchanged, <c>?</c> = untracked) and, for a rename, the path it came from.</summary>
internal sealed record CommitEntry(string Path, char Index, char Tree, string? From, bool Conflict)
{
    public bool Untracked => Index == '?';

    /// <summary>The index holds a change of the file against <c>HEAD</c>.</summary>
    public bool HasStaged => !Conflict && !Untracked && Index != ' ';

    /// <summary>The file differs from the index (an untracked file differs from nothing).</summary>
    public bool HasUnstaged => !Conflict && (Untracked || Tree != ' ');

    /// <summary>True when everything of the file is in the index, false when nothing is, null when only a part is.</summary>
    public bool? Included => Conflict ? false : HasStaged ? (HasUnstaged ? null : true) : false;

    public static CommitEntry Of(JsonElement e) =>
        new(e.GetProperty("path").GetString() ?? "", FirstChar(e.GetProperty("index").GetString()), FirstChar(e.GetProperty("tree").GetString()),
            e.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String ? from.GetString() : null, e.GetProperty("conflict").GetBoolean());

    private static char FirstChar(string? text) => text is { Length: > 0 } ? text[0] : ' ';
}

/// <summary>What the commit panel shows of a repository: its changed files, its branch and how far the branch is from its upstream, and the messages of the last commits.</summary>
internal sealed record CommitChanges(string Root, string Branch, string? Upstream, int Ahead, int Behind, bool HasCommits, int Total, IReadOnlyList<CommitEntry> Entries, IReadOnlyList<string> Messages)
{
    /// <summary>Reads the answer <c>changes</c> of the Git plugin; null for any other answer.</summary>
    public static CommitChanges? Parse(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("status", out var status) || status.GetString() != "changes") return null;
        return new CommitChanges(a.GetProperty("root").GetString() ?? "", a.GetProperty("branch").GetString() ?? "",
            a.TryGetProperty("upstream", out var up) && up.ValueKind == JsonValueKind.String ? up.GetString() : null, a.GetProperty("ahead").GetInt32(), a.GetProperty("behind").GetInt32(),
            a.GetProperty("hasCommits").GetBoolean(), a.GetProperty("total").GetInt32(), [.. a.GetProperty("entries").EnumerateArray().Select(CommitEntry.Of)],
            [.. a.GetProperty("messages").EnumerateArray().Select(m => m.GetString() ?? "")]);
    }
}

/// <summary>A file as <c>HEAD</c> or the index has it: it is not there, it is too large to send, or its bytes.</summary>
internal readonly record struct CommitBlob(bool Exists, bool TooLarge, byte[] Bytes);

/// <summary>What the commit panel asks of the window; each call is a command of the Git plugin.</summary>
/// <param name="Read">The changed files and the state of the branch; null when the plugin cannot answer.</param>
/// <param name="Stage">Puts paths (relative to the top of the repository) in the index, or takes them out of it.</param>
/// <param name="StageHunk">Stages (or takes back) the change that starts at a line of the new side; false when nothing was written.</param>
/// <param name="Blob">A file as <c>HEAD</c> (true) or the index (false) has it.</param>
/// <param name="Commit">Commits the index with the message, or amends the last commit; true when a commit was made.</param>
/// <param name="Push">Pushes the current branch (the plugin asks first).</param>
internal sealed record CommitViewActions(Func<Task<CommitChanges?>> Read, Func<IReadOnlyList<string>, bool, Task> Stage, Func<string, bool, long, Task<bool>> StageHunk,
    Func<string, bool, Task<CommitBlob?>> Blob, Func<string, bool, Task<bool>> Commit, Func<Task> Push);

/// <summary>
/// The commit panel of a repository as a tab: on the left the changed files with a box for each (a ticked file is in the index, so it goes into the commit), the box for the message, Amend and the
/// buttons Commit and Commit and Push; on the right the changes of the chosen file as two comparisons, what is staged (<c>HEAD</c> against the index) and what is not (the index against the file),
/// each with a button that stages or takes back the change the comparison is on. The panel reads the repository again shortly after a change of it; a ticked box, a button or a commit does so at once.
/// The plugin does the work and shows its notices; this view shows only what it was told.
/// </summary>
internal sealed class CommitView : Grid, IDisposable
{
    private sealed class Row
    {
        public required CommitEntry Entry { get; init; }
        public required ListBoxItem Item { get; init; }
        public required CheckBox Check { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock State { get; init; }
    }

    private readonly CommitViewActions _actions;
    private readonly Func<EditorOptions> _options;
    private readonly string _root;
    private readonly TextBlock _branch = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _distance = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4) };
    private readonly CheckBox _all = new() { IsThreeState = true };
    private readonly Button _refresh = new();
    private readonly ListBox _files = new();
    private readonly ComboBox _recent = new();
    private readonly TextBox _message = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxHeight = 200 };
    private readonly CheckBox _amend = new();
    private readonly Button _commit = new(), _commitPush = new();
    private readonly TextBlock _fileHeader = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 8, 12, 2) };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock _stagedTitle = new() { Margin = new Thickness(12, 4, 12, 2), FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _unstagedTitle = new() { Margin = new Thickness(12, 4, 12, 2), FontWeight = FontWeight.SemiBold };
    private readonly ContentControl _stagedHost = new(), _unstagedHost = new();
    private readonly DockPanel _stagedPane, _unstagedPane;
    private readonly Grid _diffs;
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.GitCommitReloadMilliseconds) };
    private readonly List<Row> _rows = [];
    private CommitChanges? _changes;
    private CancellationTokenSource? _showing;
    private string? _shownKey;
    private int _generation, _restoreStaged, _restoreUnstaged;
    private bool _filling, _disposed, _busy;

    public CommitView(string root, CommitViewActions actions, Func<EditorOptions> options)
    {
        HelpContext.Set(this, "tools.gitCommit");
        _root = root;
        _actions = actions;
        _options = options;
        ColumnDefinitions = new ColumnDefinitions("360,Auto,*");
        Focusable = true; // the window gives the focus to the view of a tab; it passes it on
        GotFocus += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this)) FocusMessage();
        };

        _refresh.Classes.Add("barbutton");
        _refresh.Padding = new Thickness(10, 2);
        _refresh.MinHeight = 24;
        foreach (var button in new[] { _commit, _commitPush })
        {
            button.Classes.Add("barbutton");
            button.Padding = new Thickness(12, 4);
            button.MinHeight = 28;
            button.Margin = new Thickness(0, 0, 8, 0);
        }
        _recent.HorizontalAlignment = HorizontalAlignment.Stretch;

        var selectRow = new DockPanel { Margin = new Thickness(8, 2, 8, 0), LastChildFill = false };
        DockPanel.SetDock(_refresh, Dock.Right);
        selectRow.Children.Add(_refresh);
        selectRow.Children.Add(_all);
        var top = new StackPanel { Margin = new Thickness(8, 8, 8, 0), Spacing = 2, Children = { _branch, _distance } };
        var header = new StackPanel { Children = { top, selectRow } };
        DockPanel.SetDock(header, Dock.Top);

        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8), Children = { _commit, _commitPush } };
        var composer = new StackPanel { Spacing = 4, Margin = new Thickness(8, 4, 8, 0), Children = { _recent, _message, _amend } };
        var bottom = new StackPanel { Children = { _summary, composer, buttons } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var left = new DockPanel { Children = { header, bottom, _files } };

        var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns };
        SetColumn(splitter, 1);

        _stagedPane = new DockPanel { Children = { _stagedTitle, _stagedHost } };
        _unstagedPane = new DockPanel { Children = { _unstagedTitle, _unstagedHost } };
        DockPanel.SetDock(_stagedTitle, Dock.Top);
        DockPanel.SetDock(_unstagedTitle, Dock.Top);
        SetRow(_unstagedPane, 1);
        _diffs = new Grid { RowDefinitions = new RowDefinitions("*,0"), Children = { _stagedPane, _unstagedPane } };
        var headerBox = new StackPanel { Children = { _fileHeader, _note } };
        DockPanel.SetDock(headerBox, Dock.Top);
        var right = new DockPanel { Children = { headerBox, _diffs } };
        SetColumn(right, 2);
        Children.Add(left);
        Children.Add(splitter);
        Children.Add(right);

        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            _ = ReloadAsync();
        };
        _files.SelectionChanged += (_, _) =>
        {
            if (!_filling) _ = ShowSelectionAsync();
        };
        _refresh.Click += (_, _) => _ = ReloadAsync();
        _all.Click += (_, _) => _ = ToggleAllAsync();
        _message.TextChanged += (_, _) => UpdateButtons();
        _message.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && _commit.IsEnabled)
            {
                e.Handled = true;
                _ = CommitAsync(push: false);
            }
        };
        _amend.IsCheckedChanged += (_, _) => OnAmendChanged();
        _recent.SelectionChanged += (_, _) => UseRecentMessage();
        _commit.Click += (_, _) => _ = CommitAsync(push: false);
        _commitPush.Click += (_, _) => _ = CommitAsync(push: true);
        ApplyTexts();
        UpdateButtons();
    }

    /// <summary>The top of the working tree of the repository this panel is for.</summary>
    public string Root => _root;

    public CommitChanges? Changes => _changes;
    public ListBox Files => _files;
    public TextBox MessageBox => _message;
    public CheckBox AmendBox => _amend;
    public CheckBox AllBox => _all;
    public ComboBox RecentBox => _recent;
    public Button CommitButton => _commit;
    public Button CommitPushButton => _commitPush;
    public Button RefreshButton => _refresh;
    public string BranchText => _branch.Text ?? "";
    public string DistanceText => _distance.Text ?? "";
    public string SummaryText => _summary.Text ?? "";
    public string NoteText => _note.Text ?? "";
    public string FileHeaderText => _fileHeader.Text ?? "";

    /// <summary>The comparison of what is staged in the chosen file (<c>HEAD</c> against the index), or null.</summary>
    public DiffView? StagedDiff => _stagedHost.Content as DiffView;

    /// <summary>The comparison of what is not staged in the chosen file (the index against the file), or null.</summary>
    public DiffView? UnstagedDiff => _unstagedHost.Content as DiffView;

    /// <summary>The box of a file by its path, or null when the file is not listed.</summary>
    public CheckBox? CheckOf(string path) => _rows.FirstOrDefault(r => r.Entry.Path == path)?.Check;

    public string? RowName(string path) => _rows.FirstOrDefault(r => r.Entry.Path == path) is { } row ? AutomationProperties.GetName(row.Item) : null;

    /// <summary>The path of the file chosen in the list, or null.</summary>
    public string? SelectedPath => (_files.SelectedItem as ListBoxItem) is { } item ? _rows.FirstOrDefault(r => ReferenceEquals(r.Item, item))?.Entry.Path : null;

    /// <summary>Selects the file with the path; false when it is not listed.</summary>
    public bool Select(string path)
    {
        if (_rows.FirstOrDefault(r => r.Entry.Path == path) is not { } row) return false;
        _files.SelectedItem = row.Item;
        return true;
    }

    // ---- texts ----

    private static string StateWord(char letter) => Loc.Get(letter switch
    {
        'M' => K.CommitStateModified,
        'A' => K.CommitStateAdded,
        'D' => K.CommitStateDeleted,
        'R' => K.CommitStateRenamed,
        'C' => K.CommitStateCopied,
        'T' => K.CommitStateTypeChanged,
        '?' => K.CommitStateUntracked,
        _ => K.CommitStateConflict,
    });

    /// <summary>What a row says of its file: that it is in conflict or untracked, or what is staged and what is changed in the tree.</summary>
    public static string DescribeOf(CommitEntry e)
    {
        if (e.Conflict) return StateWord('U');
        if (e.Untracked) return StateWord('?');
        var parts = new List<string>();
        if (e.HasStaged) parts.Add(Loc.Get(K.CommitRowIndex, ("state", StateWord(e.Index))));
        if (e.Tree != ' ') parts.Add(Loc.Get(K.CommitRowTree, ("state", StateWord(e.Tree))));
        return string.Join("; ", parts);
    }

    /// <summary>Sets every text and name from the catalogue; again when the language changes.</summary>
    public void ApplyTexts()
    {
        _all.Content = Loc.Get(K.CommitAll);
        _refresh.Content = Loc.Get(K.CommitRefresh);
        _message.PlaceholderText = Loc.Get(K.CommitMessagePlaceholder);
        _amend.Content = Loc.Get(K.CommitAmend);
        _commit.Content = Loc.Get(K.CommitCommit);
        _commitPush.Content = Loc.Get(K.CommitCommitPush);
        _recent.PlaceholderText = Loc.Get(K.CommitRecent);
        AutomationProperties.SetName(this, Loc.Get(K.A11yCommitPanel));
        AutomationProperties.SetName(_files, Loc.Get(K.A11yCommitFiles));
        AutomationProperties.SetName(_all, Loc.Get(K.CommitAll));
        AutomationProperties.SetName(_refresh, Loc.Get(K.CommitRefresh));
        AutomationProperties.SetName(_message, Loc.Get(K.A11yCommitMessage));
        AutomationProperties.SetName(_amend, Loc.Get(K.CommitAmend));
        AutomationProperties.SetName(_commit, Loc.Get(K.CommitCommit));
        AutomationProperties.SetName(_commitPush, Loc.Get(K.CommitCommitPush));
        AutomationProperties.SetName(_recent, Loc.Get(K.A11yCommitRecent));
        AutomationProperties.SetName(_fileHeader, Loc.Get(K.A11yCommitFile));
        AutomationProperties.SetName(_stagedHost, Loc.Get(K.CommitDiffStaged));
        AutomationProperties.SetName(_unstagedHost, Loc.Get(K.CommitDiffUnstaged));
        _stagedTitle.Text = Loc.Get(K.CommitDiffStaged);
        _unstagedTitle.Text = Loc.Get(K.CommitDiffUnstaged);
        if (_changes is not null) Apply(_changes, keepDiff: false);
    }

    // ---- the list ----

    /// <summary>Reads the repository again soon; several calls within the quiet time are one read.</summary>
    public void RequestReload()
    {
        if (_disposed) return;
        _reload.Stop();
        _reload.Start();
    }

    /// <summary>Reads the changed files and fills the list; the file that was chosen stays chosen, and the message that was typed stays.</summary>
    public async Task ReloadAsync()
    {
        if (_disposed) return;
        _reload.Stop();
        int generation = ++_generation;
        CommitChanges? changes;
        try { changes = await _actions.Read(); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            changes = null;
        }
        if (_disposed || generation != _generation) return;
        if (changes is null)
        {
            _summary.Text = Loc.Get(K.CommitUnavailable);
            return;
        }
        Apply(changes, keepDiff: true);
    }

    private static int Weight(CommitEntry e) => e.Conflict ? 0 : e.Untracked ? 2 : 1;

    private Row NewRow(CommitEntry e)
    {
        var check = new CheckBox { IsThreeState = e.Included is null, IsChecked = e.Included, IsEnabled = !e.Conflict, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        string file = System.IO.Path.GetFileName(e.Path);
        string folder = System.IO.Path.GetDirectoryName(e.Path)?.Replace('\\', '/') ?? "";
        var name = new TextBlock { Text = folder.Length > 0 ? $"{file}  {folder}" : file, TextTrimming = TextTrimming.CharacterEllipsis };
        var state = new TextBlock { Text = DescribeOf(e), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.8 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(2, 1) };
        var texts = new StackPanel { Children = { name, state } };
        SetColumn(texts, 1);
        grid.Children.Add(check);
        grid.Children.Add(texts);
        var item = new ListBoxItem { Content = grid };
        var row = new Row { Entry = e, Item = item, Check = check, Name = name, State = state };
        check.Click += (_, ev) =>
        {
            ev.Handled = true;
            _ = StageAsync([e.Path], unstage: e.Included == true);
        };
        AutomationProperties.SetName(check, Loc.Get(K.A11yCommitInclude, ("file", e.Path)));
        AutomationProperties.SetName(item, $"{e.Path}, {DescribeOf(e)}, " + Loc.Get(e.Included switch { true => K.CommitIncluded, null => K.CommitIncludedPart, _ => K.CommitNotIncluded }));
        return row;
    }

    private void Apply(CommitChanges changes, bool keepDiff)
    {
        _changes = changes;
        string? chosen = SelectedPath;
        _filling = true;
        _files.Items.Clear();
        _rows.Clear();
        foreach (var e in changes.Entries.OrderBy(Weight).ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)) _rows.Add(NewRow(e));
        foreach (var row in _rows) _files.Items.Add(row.Item);
        var again = chosen is null ? null : _rows.FirstOrDefault(r => r.Entry.Path == chosen);
        _files.SelectedItem = (again ?? (_rows.Count > 0 ? _rows[0] : null))?.Item;
        _filling = false;

        _branch.Text = changes.Branch.Length > 0 ? Loc.Get(K.CommitBranch, ("branch", changes.Branch)) : Loc.Get(K.CommitNoBranch);
        _distance.Text = DistanceOf(changes);
        int included = changes.Entries.Count(e => e.HasStaged);
        _summary.Text = changes.Entries.Count == 0
            ? Loc.Get(K.CommitEmpty)
            : Loc.Plural(K.CommitSummary, changes.Entries.Count, ("included", included)) + (changes.Total > changes.Entries.Count ? " " + Loc.Get(K.CommitTruncated, ("shown", changes.Entries.Count), ("total", changes.Total)) : "");
        var selectable = changes.Entries.Where(e => !e.Conflict).ToList();
        _all.IsEnabled = selectable.Count > 0;
        bool every = selectable.Count > 0 && selectable.All(e => e.Included == true), none = selectable.All(e => e.Included == false);
        _all.IsChecked = every ? true : none ? false : null;

        _filling = true;
        _recent.Items.Clear();
        foreach (var m in changes.Messages) _recent.Items.Add(new ComboBoxItem { Content = FirstLine(m) });
        _recent.SelectedIndex = -1;
        _recent.IsVisible = changes.Messages.Count > 0;
        _filling = false;
        UpdateButtons();
        if (!keepDiff) _shownKey = null;
        _ = ShowSelectionAsync();
    }

    private static string FirstLine(string message)
    {
        int end = message.IndexOfAny(['\r', '\n']);
        return end < 0 ? message : message[..end];
    }

    private static string DistanceOf(CommitChanges c)
    {
        var parts = new List<string> { c.Upstream is { } up ? Loc.Get(K.CommitTracks, ("upstream", up)) : Loc.Get(K.CommitNoUpstream) };
        if (c.Ahead > 0) parts.Add(Loc.Plural(K.CommitAhead, c.Ahead));
        if (c.Behind > 0) parts.Add(Loc.Plural(K.CommitBehind, c.Behind));
        return string.Join(" · ", parts);
    }

    // ---- staging ----

    private async Task StageAsync(IReadOnlyList<string> paths, bool unstage)
    {
        if (_busy || paths.Count == 0) return;
        _busy = true;
        try { await _actions.Stage(paths, unstage); }
        finally { _busy = false; }
        await ReloadAsync();
    }

    private Task ToggleAllAsync()
    {
        if (_changes is null) return Task.CompletedTask;
        var paths = _changes.Entries.Where(e => !e.Conflict).ToList();
        bool unstage = paths.Count > 0 && paths.All(e => e.Included == true);
        return StageAsync([.. paths.Select(e => e.Path)], unstage);
    }

    private async Task StageHunkAsync(CommitEntry entry, bool unstage, DiffView view, int hunk)
    {
        if (_busy || view.Session.Diff is not { } diff || hunk < 0 || hunk >= diff.Hunks.Count) return;
        _busy = true;
        try { await _actions.StageHunk(entry.Path, unstage, diff.Hunks[hunk].BStart); }
        finally { _busy = false; }
        if (unstage) _restoreStaged = hunk; else _restoreUnstaged = hunk;
        _shownKey = null;
        await ReloadAsync();
    }

    // ---- the message and the commit ----

    private bool CanCommit => !_busy && _changes is { } c && (_amend.IsChecked == true ? c.HasCommits : c.Entries.Any(e => e.HasStaged) && !string.IsNullOrWhiteSpace(_message.Text));

    private void UpdateButtons() => _commit.IsEnabled = _commitPush.IsEnabled = CanCommit;

    private void OnAmendChanged()
    {
        if (_amend.IsChecked == true && string.IsNullOrWhiteSpace(_message.Text) && _changes is { Messages.Count: > 0 } c) _message.Text = c.Messages[0];
        UpdateButtons();
    }

    private void UseRecentMessage()
    {
        if (_filling || _recent.SelectedIndex < 0 || _changes is not { } c || _recent.SelectedIndex >= c.Messages.Count) return;
        _message.Text = c.Messages[_recent.SelectedIndex];
        _message.CaretIndex = _message.Text.Length;
        _message.Focus();
    }

    /// <summary>Commits what is in the index with the message of the box (or amends the last commit); with <paramref name="push"/> the push follows. The box is emptied only when the commit was made.</summary>
    public async Task<bool> CommitAsync(bool push)
    {
        if (!CanCommit) return false;
        _busy = true;
        UpdateButtons();
        bool done;
        try { done = await _actions.Commit(_message.Text ?? "", _amend.IsChecked == true); }
        finally { _busy = false; }
        if (done)
        {
            _message.Text = "";
            _amend.IsChecked = false;
        }
        await ReloadAsync();
        if (done && push) await _actions.Push();
        return done;
    }

    /// <summary>Moves the keyboard focus into the box of the message.</summary>
    public void FocusMessage() => _message.Focus();

    // ---- the comparisons of the chosen file ----

    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false; // UTF-16 holds zero bytes as text
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF) return false;
        return bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0);
    }

    private static string Decode(byte[] bytes)
    {
        var codec = EncodingGuesser.Guess(bytes.AsSpan(0, Math.Min(bytes.Length, 1 << 20)), bytes.Length <= 1 << 20).Codec;
        return codec.GetString(bytes.AsSpan(0, codec.CompleteLength(bytes))).TrimStart('﻿');
    }

    private void SetDiffs(DiffView? staged, DiffView? unstaged, string? note)
    {
        if (_stagedHost.Content is DiffView oldStaged)
        {
            _stagedHost.Content = null;
            oldStaged.Session.Dispose();
        }
        if (_unstagedHost.Content is DiffView oldUnstaged)
        {
            _unstagedHost.Content = null;
            oldUnstaged.Session.Dispose();
        }
        _stagedHost.Content = staged;
        _unstagedHost.Content = unstaged;
        _stagedPane.IsVisible = staged is not null;
        _unstagedPane.IsVisible = unstaged is not null;
        _diffs.RowDefinitions = new RowDefinitions(staged is not null && unstaged is not null ? "*,*" : staged is not null ? "*,0" : "0,*");
        _note.Text = note ?? "";
        _note.IsVisible = !string.IsNullOrEmpty(note);
    }

    private string ShownKey(CommitEntry e)
    {
        long stamp = 0;
        try { stamp = File.GetLastWriteTimeUtc(System.IO.Path.Combine(_root, e.Path)).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return $"{e.Path}|{e.Index}{e.Tree}|{e.From}|{stamp}";
    }

    private async Task ShowSelectionAsync()
    {
        _showing?.Cancel();
        var cts = _showing = new CancellationTokenSource();
        var entry = SelectedPath is { } path ? _rows.FirstOrDefault(r => r.Entry.Path == path)?.Entry : null;
        if (entry is null)
        {
            _shownKey = null;
            _fileHeader.Text = "";
            SetDiffs(null, null, null);
            return;
        }
        string key = ShownKey(entry);
        _fileHeader.Text = $"{entry.Path}  ·  {DescribeOf(entry)}";
        if (key == _shownKey && (StagedDiff is not null || UnstagedDiff is not null || !string.IsNullOrEmpty(_note.Text))) return;
        try
        {
            if (entry.Conflict)
            {
                SetDiffs(null, null, Loc.Get(K.CommitConflictNote));
                _shownKey = key;
                return;
            }
            DiffView? staged = null, unstaged = null;
            string? note = null;
            if (entry.HasStaged)
            {
                var head = await _actions.Blob(entry.From ?? entry.Path, true);
                var index = await _actions.Blob(entry.Path, false);
                if (cts.IsCancellationRequested || _disposed) return;
                if (SideOf(Loc.Get(K.CommitSideHead), head, entry.Path, out var left, out var problem) && SideOf(Loc.Get(K.CommitSideIndex), index, entry.Path, out var right, out problem))
                    staged = NewDiff(left!, right!, entry, unstage: true);
                else note = problem;
            }
            if (entry.HasUnstaged)
            {
                var index = await _actions.Blob(entry.Path, false);
                if (cts.IsCancellationRequested || _disposed) return;
                string full = System.IO.Path.Combine(_root, entry.Path);
                if (SideOf(Loc.Get(K.CommitSideIndex), index, entry.Path, out var left, out var problem) && FileSide(full, entry.Path, out var right, out problem))
                    unstaged = NewDiff(left!, right!, entry, unstage: false);
                else note ??= problem;
            }
            if (cts.IsCancellationRequested || _disposed)
            {
                staged?.Session.Dispose();
                unstaged?.Session.Dispose();
                return;
            }
            SetDiffs(staged, unstaged, note);
            _shownKey = key;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (!cts.IsCancellationRequested) SetDiffs(null, null, Loc.Get(K.CommitFileFailed, ("file", entry.Path), ("reason", e.Message)));
        }
    }

    /// <summary>A side from a blob of <c>HEAD</c> or the index; false, with the reason, for a file too large or a binary one. A file that is not there is an empty side.</summary>
    private static bool SideOf(string title, CommitBlob? blob, string path, out CompareSide? side, out string? problem)
    {
        side = null;
        problem = null;
        if (blob is not { } b)
        {
            problem = Loc.Get(K.CommitUnavailable);
            return false;
        }
        if (b.TooLarge)
        {
            problem = Loc.Get(K.CommitTooLarge, ("file", path));
            return false;
        }
        if (b.Exists && IsBinary(b.Bytes))
        {
            problem = Loc.Get(K.CommitBinary, ("file", path));
            return false;
        }
        side = new CompareSide { Title = title, Text = b.Exists ? Decode(b.Bytes) : "" };
        return true;
    }

    private static bool FileSide(string full, string path, out CompareSide? side, out string? problem)
    {
        side = null;
        problem = null;
        string title = Loc.Get(K.CommitSideFile);
        if (!File.Exists(full))
        {
            side = new CompareSide { Title = title, Text = "" };
            return true;
        }
        using (var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var sample = new byte[(int)Math.Min(stream.Length, 8000)];
            int read = stream.Read(sample, 0, sample.Length);
            if (IsBinary(sample[..read]))
            {
                problem = Loc.Get(K.CommitBinary, ("file", path));
                return false;
            }
        }
        side = new CompareSide { Title = title, FilePath = full };
        return true;
    }

    private DiffView NewDiff(CompareSide left, CompareSide right, CommitEntry entry, bool unstage)
    {
        // the plugin finds the change by the line it starts at, so both compare the same way: no difference is ignored
        var view = new DiffView(new CompareSession(left, right) { Ignore = DiffIgnore.None }, _options());
        // the buttons act on the current difference: the first one is current from the start, and after a change was taken the next one stands where it was
        view.RestoreHunk(unstage ? _restoreStaged : _restoreUnstaged);
        if (unstage) _restoreStaged = 0; else _restoreUnstaged = 0;
        string title = Loc.Get(unstage ? K.CommitActionUnstage : K.CommitActionStage);
        view.SetActions([new DiffAction(unstage ? "unstage" : "stage", title, null)]);
        view.ActionRequested += (action, hunk) => _ = StageHunkAsync(entry, unstage, view, hunk);
        return view;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reload.Stop();
        _showing?.Cancel();
        SetDiffs(null, null, null);
    }
}

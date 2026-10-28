using System.Collections.ObjectModel;
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
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Core.Agents.Transcripts;

namespace NksHub.Textida.App.Shell;

/// <summary>What the transcript window needs from the window that opened it.</summary>
/// <param name="OpenFile">Opens a file the agent touched in the editor; false when it does not exist.</param>
/// <param name="OpenRaw">Shows the text of one raw line (values of secrets files already hidden) in a read-only document: the number of the line, 1-based, and its text.</param>
internal sealed record TranscriptHost(TranscriptIndex Index, string Path, Func<TokenPreset> Rule, Func<string, bool> OpenFile, Action<long, string> OpenRaw);

/// <summary>
/// The viewer of the transcript of an agent session (P8b.3.3), read only: the turns of the session in a list that fills while the index is built, the prompt, answers, tool calls with their diffs and
/// results of the chosen turn, a search over the whole transcript, the files the turn touched and the raw line behind every row. The file is never written; the values of secrets files are hidden everywhere
/// (see <see cref="TranscriptMasker"/>).
/// </summary>
internal sealed class TranscriptWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TranscriptHost _host;
    private readonly TranscriptIndex _index;
    private readonly ObservableCollection<string> _titles = [], _hitTexts = [], _fileTexts = [];
    private readonly List<TranscriptHit> _hitData = [];
    private readonly List<TouchedFile> _fileData = [];
    private readonly ListBox _turns = new() { Background = Brushes.Transparent }, _hits = new() { Background = Brushes.Transparent, MinHeight = 90 }, _rows = new() { Background = Brushes.Transparent },
        _files = new() { Background = Brushes.Transparent, MinHeight = 70, MaxHeight = 120 };
    private readonly TextBox _search = new();
    private readonly TextBlock _readOnly = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 }, _status = new() { TextWrapping = TextWrapping.Wrap },
        _turnHeader = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold }, _notice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _turnsLabel = new(), _hitsLabel = new(), _filesLabel = new();
    private readonly Button _searchButton = new() { MinWidth = 90, IsDefault = true }, _stopButton = new() { MinWidth = 70 }, _openRaw = new() { MinWidth = 140 }, _openFile = new() { MinWidth = 110 }, _close = new() { MinWidth = 84, IsCancel = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.TranscriptRefreshMilliseconds) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _searchCts;
    private Task _build = Task.CompletedTask, _searchTask = Task.CompletedTask;
    private int _titlesShown, _shownTurn = -1;
    private long? _pendingLine;
    private string _searchState = "";
    private bool _loading;

    public TranscriptWindow(TranscriptHost host)
    {
        _host = host;
        _index = host.Index;
        HelpContext.OnF1(this, "tools.transcript");
        Classes.Add("shell");
        Width = 1180;
        Height = 780;
        MinWidth = 800;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var label in new[] { _turnsLabel, _hitsLabel, _filesLabel }) label.FontWeight = FontWeight.SemiBold;
        _turns.ItemsSource = _titles;
        _hits.ItemsSource = _hitTexts;
        _files.ItemsSource = _fileTexts;

        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, Margin = new Thickness(18, 0, 18, 8), Children = { _search, _searchButton, _stopButton } };
        Grid.SetColumn(_searchButton, 1);
        Grid.SetColumn(_stopButton, 2);

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 4 };
        void Place(Grid grid, Control c, int row) { Grid.SetRow(c, row); grid.Children.Add(c); }
        Place(left, _turnsLabel, 0);
        Place(left, new Border { Child = _turns, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 1);
        Place(left, _hitsLabel, 2);
        Place(left, new Border { Child = _hits, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 3);

        var fileButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _openFile, _openRaw } };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 4 };
        Place(right, _turnHeader, 0);
        Place(right, _notice, 1);
        Place(right, new Border { Child = _rows, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 2);
        Place(right, _filesLabel, 3);
        Place(right, new Border { Child = _files, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 4);
        Place(right, fileButtons, 5);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*"), ColumnSpacing = 16, Margin = new Thickness(18, 0, 18, 8) };
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _close } };
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 6, Children = { _status, buttons } };
        var header = new StackPanel { Margin = new Thickness(18, 12, 18, 6), Children = { _readOnly } };
        var top = new StackPanel { Children = { header, searchRow } };
        var root = new ReadingDockPanel { Children = { top, footer, body } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        _turns.SelectionChanged += (_, _) => { if (!_loading && _turns.SelectedIndex >= 0) _ = ShowTurnAsync(_turns.SelectedIndex); };
        _hits.SelectionChanged += (_, _) => { if (!_loading && _hits.SelectedIndex >= 0) GoToHit(_hits.SelectedIndex); };
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = SearchNow(_search.Text ?? ""); e.Handled = true; } };
        _rows.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = OpenRawNow(); e.Handled = true; } };
        _rows.DoubleTapped += (_, _) => _ = OpenRawNow();
        _files.DoubleTapped += (_, _) => OpenFileNow();
        _searchButton.Click += (_, _) => _ = SearchNow(_search.Text ?? "");
        _stopButton.Click += (_, _) => StopSearch();
        _openRaw.Click += (_, _) => _ = OpenRawNow();
        _openFile.Click += (_, _) => OpenFileNow();
        _close.Click += (_, _) => Close();
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) => { Loc.Changed += OnLanguageChanged; ApplyTexts(); _timer.Start(); Tick(); _search.Focus(); };
        Closed += (_, _) => Shutdown();
        ApplyTexts();
    }

    // ---- state for tests ----

    internal IReadOnlyList<string> TurnTitles => [.. _titles];
    internal IReadOnlyList<string> HitTexts => [.. _hitTexts];
    internal IReadOnlyList<TranscriptHit> HitData => _hitData;
    internal IReadOnlyList<string> FileTexts => [.. _fileTexts];
    internal IReadOnlyList<string> RowTexts => [.. _rows.Items.OfType<Border>().Select(b => b.Tag is TranscriptRow r ? r.Text : "")];
    internal IReadOnlyList<TranscriptRow> RowData => [.. _rows.Items.OfType<Border>().Select(b => b.Tag).OfType<TranscriptRow>()];
    internal string StatusText => _status.Text ?? "";
    internal string HeaderText => _turnHeader.Text ?? "";
    internal string NoticeText => _notice.Text ?? "";
    internal string SearchState => _searchState;
    internal int ShownTurn => _shownTurn;
    internal ListBox TurnList => _turns;
    internal ListBox RowList => _rows;
    internal ListBox FileList => _files;
    internal ListBox HitList => _hits;
    internal TextBox SearchBox => _search;

    /// <summary>The index finished (or stopped) and the list shows all of it.</summary>
    internal bool IsListComplete => _index.IsComplete && _titlesShown >= _index.TurnCount;

    /// <summary>The task that shows the turn chosen last.</summary>
    internal Task TurnTask { get; private set; } = Task.CompletedTask;

    /// <summary>Starts the index on the pool; the list fills while it runs.</summary>
    internal void StartIndexing() => _build = _index.BuildAsync(_lifetime.Token);

    // ---- the list of turns ----

    private string TurnTitle(TranscriptTurn turn) =>
        turn.Number == 0 ? Loc.Get(K.TranscriptPreamble)
        : turn.Continued ? Loc.Get(K.TranscriptTurnContinued, ("number", turn.Number))
        : turn.Preview.Length > 0 ? Loc.Get(K.TranscriptTurnTitle, ("number", turn.Number), ("preview", turn.Preview))
        : Loc.Get(K.TranscriptTurnEmpty, ("number", turn.Number));

    /// <summary>One look at the index: new turns are added to the list, the status is written, and when the index is complete the titles are written again (values learned late are hidden then).</summary>
    internal void Tick()
    {
        int count = _index.TurnCount;
        for (; _titlesShown < count; _titlesShown++) _titles.Add(TurnTitle(_index.Turn(_titlesShown)));
        if (_turns.SelectedIndex < 0 && _titles.Count > 0)
        {
            _loading = true;
            _turns.SelectedIndex = 0;
            _loading = false;
            _ = ShowTurnAsync(0);
        }
        if (_index.IsComplete && _titlesShown >= _index.TurnCount)
        {
            _timer.Stop();
            RewriteTitles();
        }
        WriteStatus();
    }

    /// <summary>Writes the titles again, keeping the chosen turn (a replaced item would drop the selection).</summary>
    private void RewriteTitles()
    {
        int selected = _turns.SelectedIndex;
        _loading = true;
        for (int i = 0; i < _titles.Count; i++) _titles[i] = TurnTitle(_index.Turn(i));
        _turns.SelectedIndex = selected;
        _loading = false;
    }

    private void WriteStatus()
    {
        string lines = FormatCount(_index.LineCount), turns = FormatCount(_index.TurnCount);
        if (_index.Error is { } error) _status.Text = Loc.Get(K.TranscriptError, ("reason", error.Message));
        else if (!_index.IsComplete) _status.Text = Loc.Get(K.TranscriptIndexing, ("lines", lines), ("turns", turns), ("percent", _index.FileLength == 0 ? 100 : _index.BytesIndexed * 100 / _index.FileLength));
        else if (_index.IsTruncated) _status.Text = Loc.Get(K.TranscriptTruncated, ("lines", lines), ("turns", turns), ("size", OpenDocument.FormatSize(_index.Limits.IndexMaxBytes)));
        else _status.Text = Loc.Get(K.TranscriptDone, ("lines", lines), ("turns", turns));
        if (_searchState.Length > 0) _status.Text += " " + _searchState;
    }

    private static string FormatCount(long n) => n.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    // ---- one turn ----

    internal Task ShowTurnAsync(int turn)
    {
        _shownTurn = turn;
        return TurnTask = LoadTurnAsync(turn);
    }

    private async Task LoadTurnAsync(int turn)
    {
        TurnContent content;
        TranscriptTurn info;
        try
        {
            info = _index.Turn(turn);
            content = await Task.Run(() => _index.ReadTurn(turn));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            _status.Text = Loc.Get(K.TranscriptError, ("reason", e.Message));
            return;
        }
        if (_shownTurn != turn) return; // another turn was chosen meanwhile
        Fill(info, content);
    }

    private IBrush? ThemeBrush(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : null;

    private string RowLabel(TranscriptRow row) => Loc.Get(K.TranscriptRowLabel, ("kind", row.Kind switch
    {
        TranscriptRowKind.User => Loc.Get(K.TranscriptRowUser),
        TranscriptRowKind.Assistant => Loc.Get(K.TranscriptRowAssistant),
        TranscriptRowKind.Thinking => Loc.Get(K.TranscriptRowThinking),
        TranscriptRowKind.ToolCall => Loc.Get(K.TranscriptRowTool, ("tool", row.Tool ?? "")),
        TranscriptRowKind.ToolResult => Loc.Get(K.TranscriptRowResult),
        TranscriptRowKind.ToolError => Loc.Get(K.TranscriptRowError),
        TranscriptRowKind.DiffRemoved => Loc.Get(K.TranscriptRowRemoved),
        TranscriptRowKind.DiffAdded => Loc.Get(K.TranscriptRowAdded),
        _ => Loc.Get(K.TranscriptRowRaw),
    }), ("line", row.Line + 1));

    private Control RowControl(TranscriptRow row)
    {
        bool code = row.Kind is not (TranscriptRowKind.User or TranscriptRowKind.Assistant or TranscriptRowKind.Thinking);
        var label = new TextBlock { Text = RowLabel(row), FontWeight = FontWeight.SemiBold, FontSize = 12, Opacity = 0.8 };
        var text = new SelectableTextBlock { Text = row.Text, TextWrapping = TextWrapping.Wrap, FontFamily = code ? Mono : FontFamily, FontStyle = row.Kind == TranscriptRowKind.Thinking ? FontStyle.Italic : FontStyle.Normal };
        var border = new Border
        {
            Tag = row,
            Padding = new Thickness(8, 4),
            Child = new StackPanel { Children = { label, text } },
            Background = row.Kind switch { TranscriptRowKind.DiffRemoved => ThemeBrush("DiffRemoved"), TranscriptRowKind.DiffAdded => ThemeBrush("DiffAdded"), _ => null },
        };
        if (row.Kind == TranscriptRowKind.ToolError) text.Foreground = ThemeColors.Danger;
        AutomationProperties.SetName(border, label.Text + ": " + (row.Text.Length > 120 ? row.Text[..120] : row.Text));
        return border;
    }

    private void Fill(TranscriptTurn turn, TurnContent content)
    {
        var controls = content.Rows.Select(RowControl).ToList();
        _loading = true;
        _rows.ItemsSource = controls;
        _fileData.Clear();
        _fileTexts.Clear();
        foreach (var file in content.Files.DistinctBy(f => (f.Path, f.Tool)))
        {
            _fileData.Add(file);
            _fileTexts.Add(Loc.Get(K.TranscriptFileItem, ("tool", file.Tool), ("path", file.Path)));
        }
        _loading = false;
        long characters = content.Rows.Sum(r => (long)r.Text.Length);
        string tokens = MainWindow.FormatTokens(TokenEstimator.Estimate(characters, _host.Rule()));
        _turnHeader.Text = Loc.Get(K.TranscriptTurnHeader, ("title", TurnTitle(turn)), ("first", turn.FirstLine + 1), ("last", turn.FirstLine + turn.LineCount), ("tokens", tokens));
        _notice.Text = content.OversizedLines > 0 ? Loc.Get(K.TranscriptOversized, ("count", content.OversizedLines)) : "";
        _notice.IsVisible = content.OversizedLines > 0;
        if (_pendingLine is { } line)
        {
            _pendingLine = null;
            int at = controls.FindIndex(c => c.Tag is TranscriptRow r && r.Line == line);
            if (at >= 0)
            {
                _rows.SelectedIndex = at;
                _rows.ScrollIntoView(controls[at]);
            }
        }
        else if (controls.Count > 0) _rows.ScrollIntoView(controls[0]);
    }

    // ---- search ----

    internal Task SearchNow(string text)
    {
        StopSearch();
        _hitData.Clear();
        _hitTexts.Clear();
        _searchState = "";
        if (text.Trim().Length == 0)
        {
            WriteStatus();
            return Task.CompletedTask;
        }
        var cts = _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchState = Loc.Get(K.TranscriptSearching);
        WriteStatus();
        return _searchTask = Task.Run(() =>
        {
            try
            {
                var summary = _index.Search(text, hit => Dispatcher.UIThread.Post(() => AddHit(hit, cts)), cts.Token);
                Dispatcher.UIThread.Post(() => SearchDone(summary, cts));
            }
            catch (OperationCanceledException) { /* stopped: the hits found stay */ }
        });
    }

    private void AddHit(TranscriptHit hit, CancellationTokenSource owner)
    {
        if (owner.IsCancellationRequested) return;
        _hitData.Add(hit);
        _hitTexts.Add(Loc.Get(K.TranscriptHit, ("line", hit.Line + 1), ("snippet", hit.Snippet)));
    }

    private void SearchDone(TranscriptSearchSummary summary, CancellationTokenSource owner)
    {
        if (owner.IsCancellationRequested || !ReferenceEquals(owner, _searchCts)) return;
        _searchState = summary.Limited ? Loc.Get(K.TranscriptSearchLimited, ("hits", summary.Hits))
            : summary.Skipped > 0 ? Loc.Get(K.TranscriptSearchSkipped, ("hits", summary.Hits), ("lines", FormatCount(summary.Lines)), ("skipped", FormatCount(summary.Skipped)))
            : Loc.Get(K.TranscriptSearchDone, ("hits", summary.Hits), ("lines", FormatCount(summary.Lines)));
        WriteStatus();
    }

    internal void StopSearch() => _searchCts?.Cancel();

    private void GoToHit(int index)
    {
        if (index >= _hitData.Count) return;
        var hit = _hitData[index];
        if (hit.Turn < 0) return;
        if (_shownTurn == hit.Turn && _turns.SelectedIndex == hit.Turn)
        {
            int at = _rows.Items.OfType<Border>().ToList().FindIndex(b => b.Tag is TranscriptRow r && r.Line == hit.Line);
            if (at >= 0)
            {
                _rows.SelectedIndex = at;
                _rows.ScrollIntoView(_rows.Items[at]!);
            }
            return;
        }
        _pendingLine = hit.Line;
        _turns.SelectedIndex = hit.Turn;
    }

    /// <summary>Selects the hit as a click does: the turn of the hit opens and the row of the line is chosen.</summary>
    internal void SelectHit(int index)
    {
        _hits.SelectedIndex = -1;
        _hits.SelectedIndex = index;
    }

    // ---- the line behind a row, and the files ----

    /// <summary>Opens the raw line behind the chosen row (or hit, or the start of the turn) in a read-only document.</summary>
    internal async Task OpenRawNow()
    {
        long line = _rows.SelectedItem is Border { Tag: TranscriptRow row } ? row.Line
            : _hits.SelectedIndex >= 0 && _hits.SelectedIndex < _hitData.Count ? _hitData[_hits.SelectedIndex].Line
            : _shownTurn >= 0 ? _index.Turn(_shownTurn).FirstLine : 0;
        string text;
        try { text = await Task.Run(() => _index.ReadRaw(line)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException or ArgumentOutOfRangeException)
        {
            _status.Text = Loc.Get(K.TranscriptError, ("reason", e.Message));
            return;
        }
        _host.OpenRaw(line + 1, text);
    }

    internal bool OpenFileNow()
    {
        if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _fileData.Count) return false;
        var file = _fileData[_files.SelectedIndex];
        bool opened = _host.OpenFile(file.Path);
        if (!opened) _status.Text = Loc.Get(K.TranscriptNoFile, ("path", file.Path));
        return opened;
    }

    // ---- language and end ----

    private void OnLanguageChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
        RewriteTitles();
        if (_shownTurn >= 0) _ = ShowTurnAsync(_shownTurn);
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.TranscriptTitle, ("name", System.IO.Path.GetFileName(_host.Path)));
        _readOnly.Text = Loc.Get(K.TranscriptReadOnly, ("path", _host.Path));
        _turnsLabel.Text = Loc.Get(K.TranscriptTurns);
        _hitsLabel.Text = Loc.Get(K.TranscriptHits);
        _filesLabel.Text = Loc.Get(K.TranscriptFiles);
        _searchButton.Content = Loc.Get(K.TranscriptSearchButton);
        _stopButton.Content = Loc.Get(K.TranscriptStop);
        _openRaw.Content = Loc.Get(K.TranscriptOpenRaw);
        _openFile.Content = Loc.Get(K.TranscriptOpenFile);
        _close.Content = Loc.Get(K.PanelOutputClose);
        _search.PlaceholderText = Loc.Get(K.TranscriptSearchLabel);
        AutomationProperties.SetName(_search, Loc.Get(K.TranscriptSearchLabel));
        AutomationProperties.SetName(_turns, _turnsLabel.Text);
        AutomationProperties.SetName(_hits, _hitsLabel.Text);
        AutomationProperties.SetName(_files, _filesLabel.Text);
        AutomationProperties.SetName(_rows, Loc.Get(K.TranscriptRows));
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        WriteStatus();
    }

    private void Shutdown()
    {
        Loc.Changed -= OnLanguageChanged;
        _timer.Stop();
        _lifetime.Cancel();
        // the index is closed when nothing reads it any more
        var running = Task.WhenAll(_build.ContinueWith(_ => { }, TaskScheduler.Default), _searchTask.ContinueWith(_ => { }, TaskScheduler.Default), TurnTask.ContinueWith(_ => { }, TaskScheduler.Default));
        _ = running.ContinueWith(_ => _index.Dispose(), TaskScheduler.Default);
    }
}

using System.Collections.Concurrent;
using System.Globalization;
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
using NksHub.Textida.Core.History;

namespace NksHub.Textida.App.Shell;

/// <summary>What the buttons of the history panel ask the window to do with a revision.</summary>
/// <param name="Restore">Puts the whole revision into the tab of the file (one undo step); null when the tab cannot take it (a large file, a read-only tab).</param>
internal sealed record HistoryPanelActions(Action<HistoryRevision>? Restore, Func<HistoryRevision, Task> Label, Action<HistoryRevision> TogglePin, Func<HistoryRevision, Task> SaveCopy);

/// <summary>
/// The local history of a file as a tab: on the left the revisions, newest first, each with when, why, how big and how many lines it added to the revision before it and removed from it; on
/// the right the built-in comparison of the chosen revision with the current text or the revision before it, or of two revisions chosen together. A block of the comparison goes into the
/// tab of the file with the arrow of the comparison, the whole revision with a button; both are one undo step. The counts of lines are made on the thread pool, one revision at a time. The
/// list follows the store: a save, a change from outside, a label or a pin shows at once.
/// </summary>
internal sealed class HistoryView : Grid, IDisposable
{
    private sealed class Row
    {
        public required HistoryRevision Revision { get; init; }
        public required ListBoxItem Item { get; init; }
        public required TextBlock When { get; init; }
        public required TextBlock Detail { get; init; }
        public required TextBlock Change { get; init; }
        public bool IsFirst { get; set; }
    }

    /// <summary>The counted change of a revision and the revision it was counted against: it counts again only when that one is gone or another.</summary>
    private readonly record struct Counted(string? Before, RevisionChange? Change);

    private readonly FileHistory _history;
    private readonly string _path;
    private readonly Func<CompareSide> _current;
    private readonly Func<EditorOptions> _options;
    private readonly Func<TextCodec> _codec;
    private readonly Func<DiffIgnore> _ignore;
    private readonly HistoryPanelActions _actions;
    private readonly ComboBox _source = new(), _mode = new();
    private readonly TextBox _labelFilter = new();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 6, 8, 0) };
    private readonly TextBlock _retention = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 8, 6) };
    private readonly TextBlock _header = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _restore = new(), _label = new(), _pin = new(), _copy = new();
    private readonly ContentControl _diffHost = new();
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly Dictionary<string, Counted> _counted = [];
    private readonly ConcurrentDictionary<string, string?> _asked = [];
    private readonly List<Row> _rows = [];
    private IReadOnlyList<HistoryRevision> _all = [];
    private CancellationTokenSource? _counting, _showing;
    private bool _disposed, _filling;

    public HistoryView(FileHistory history, string path, Func<CompareSide> current, Func<EditorOptions> options, Func<TextCodec> codec, Func<DiffIgnore> ignore, HistoryPanelActions actions)
    {
        HelpContext.Set(this, "file.historyPanel");
        _history = history;
        _path = Path.GetFullPath(path);
        _current = current;
        _options = options;
        _codec = codec;
        _ignore = ignore;
        _actions = actions;
        ColumnDefinitions = new ColumnDefinitions("340,Auto,*");
        Focusable = true; // the window gives the focus to the view of a tab; it passes it on to the list
        GotFocus += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this)) FocusList();
        };

        _source.SelectedIndex = 0;
        _mode.Items.Add(new ComboBoxItem());
        _mode.Items.Add(new ComboBoxItem());
        _mode.SelectedIndex = 0;
        foreach (var combo in new[] { _source, _mode }) combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        for (int i = 0; i < SourceChoices.Length + 1; i++) _source.Items.Add(new ComboBoxItem());

        var filters = new StackPanel { Spacing = 6, Margin = new Thickness(8, 8, 8, 6), Children = { _mode, _source, _labelFilter } };
        DockPanel.SetDock(filters, Dock.Top);
        var footer = new StackPanel { Children = { _summary, _retention } };
        DockPanel.SetDock(footer, Dock.Bottom);
        var left = new DockPanel { Children = { filters, footer, _list } };

        var splitter = new GridSplitter { Width = 4, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Columns };
        SetColumn(splitter, 1);

        foreach (var button in new[] { _restore, _label, _pin, _copy })
        {
            button.Classes.Add("barbutton");
            button.Padding = new Thickness(10, 2);
            button.MinHeight = 24;
        }
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _restore, _label, _pin, _copy } };
        foreach (var button in buttons.Children) button.Margin = new Thickness(0, 0, 8, 4);
        var top = new StackPanel { Spacing = 4, Margin = new Thickness(12, 8, 12, 4), Children = { _header, _details, buttons } };
        DockPanel.SetDock(top, Dock.Top);
        var right = new DockPanel { Children = { top, _diffHost } };
        SetColumn(right, 2);
        Children.Add(left);
        Children.Add(splitter);
        Children.Add(right);

        AutomationProperties.SetLiveSetting(_header, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_details, AutomationLiveSetting.Polite);
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            Reload();
        };
        _list.SelectionChanged += (_, _) => { if (!_filling) _ = ShowSelectionAsync(); };
        _source.SelectionChanged += (_, _) => { if (!_filling) Reload(); };
        _mode.SelectionChanged += (_, _) => { if (!_filling) _ = ShowSelectionAsync(); };
        _labelFilter.TextChanged += (_, _) => { if (!_filling) Reload(); };
        _restore.Click += (_, _) => { if (Single() is { } r) _actions.Restore?.Invoke(r); };
        _label.Click += (_, _) => { if (Single() is { } r) _ = _actions.Label(r); };
        _pin.Click += (_, _) => { if (Single() is { } r) _actions.TogglePin(r); };
        _copy.Click += (_, _) => { if (Single() is { } r) _ = _actions.SaveCopy(r); };
        _history.FileChanged += OnFileChanged;
        ApplyTexts();
    }

    /// <summary>Raised with a localised sentence when something needs saying (a store that cannot be read, a revision too large).</summary>
    public event Action<string>? Message;

    public ListBox List => _list;
    public string HeaderText => _header.Text ?? "";
    public string DetailsText => _details.Text ?? "";
    public string SummaryText => _summary.Text ?? "";
    public string RetentionText => _retention.Text ?? "";
    public Button RestoreButton => _restore;
    public Button LabelButton => _label;
    public Button PinButton => _pin;
    public Button CopyButton => _copy;
    public ComboBox SourceBox => _source;
    public ComboBox ModeBox => _mode;
    public TextBox LabelFilterBox => _labelFilter;

    /// <summary>The comparison shown at the right, or null.</summary>
    public DiffView? Diff => _diffHost.Content as DiffView;

    /// <summary>The revisions in the list now, newest first.</summary>
    public IReadOnlyList<HistoryRevision> Shown => [.. _rows.Select(r => r.Revision)];

    /// <summary>The text of the row of a revision as a screen reader reads it, or null when the revision is not in the list.</summary>
    public string? RowName(string id) => _rows.FirstOrDefault(r => r.Revision.Id == id) is { } row ? AutomationProperties.GetName(row.Item) : null;

    /// <summary>The counted change of a revision, null while it is not counted or cannot be.</summary>
    public RevisionChange? ChangeOf(string id) => _counted.TryGetValue(id, out var c) ? c.Change : null;

    /// <summary>True while the counts of lines are still being made.</summary>
    public bool Counting => _counting is { IsCancellationRequested: false } && _rows.Any(r => !_counted.ContainsKey(r.Revision.Id) && !r.IsFirst);

    private static readonly HistorySource[] SourceChoices = [HistorySource.Save, HistorySource.External, HistorySource.Initial, HistorySource.Manual, HistorySource.BeforeRestore];

    // ---- texts ----

    public static string When(HistoryRevision r) => r.TimeUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public static string SourceText(HistorySource s) => Loc.Get(s switch
    {
        HistorySource.Save => K.HistorySourceSave,
        HistorySource.External => K.HistorySourceExternal,
        HistorySource.Manual => K.HistorySourceManual,
        HistorySource.Initial => K.HistorySourceInitial,
        _ => K.HistorySourceBeforeRestore,
    });

    /// <summary>The second line of a row: why the revision was taken, how big it is, whether it is a patch, its label and whether it is pinned.</summary>
    public static string DetailOf(HistoryRevision r) => string.Join(" · ", new[]
    {
        SourceText(r.Source), OpenDocument.FormatSize(r.Size), r.IsPatch ? Loc.Get(K.HistoryPatchTag, ("size", OpenDocument.FormatSize(r.PatchBytes))) : null,
        r.Label, r.Pinned ? Loc.Get(K.HistoryPinnedTag) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>The size a revision gained or lost against the one before it, with its sign.</summary>
    public static string SizeDelta(long delta) => (delta < 0 ? "−" : "+") + OpenDocument.FormatSize(Math.Abs(delta));

    /// <summary>The limits that apply to the revisions of a file, in words; "no limit" when none is set.</summary>
    public static string RetentionOf(HistoryPolicy policy)
    {
        var limits = new List<string>();
        if (policy.MaxAge is { } age) limits.Add(Loc.Plural(K.HistoryPanelLimitAge, (long)Math.Ceiling(age.TotalDays)));
        if (policy.MaxRevisionsPerFile is { } count) limits.Add(Loc.Plural(K.HistoryPanelLimitRevisions, count));
        if (policy.MaxBytesPerFile is { } bytes) limits.Add(Loc.Get(K.HistoryPanelLimitBytes, ("size", OpenDocument.FormatSize(bytes))));
        return limits.Count == 0 ? Loc.Get(K.HistoryPanelRetentionNone) : Loc.Get(K.HistoryPanelRetention, ("limits", string.Join(", ", limits)));
    }

    /// <summary>The text of the summary of the whole history of the file.</summary>
    public static string SummaryOf(HistorySummary summary) => Loc.Plural(K.HistoryPanelSummary, summary.Revisions, ("size", OpenDocument.FormatSize(summary.StoredBytes)),
        ("oldest", summary.OldestUtc is { } oldest ? oldest.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "–"));

    /// <summary>Sets every text and name from the catalogue; again when the language changes.</summary>
    public void ApplyTexts()
    {
        _filling = true;
        int source = _source.SelectedIndex, mode = _mode.SelectedIndex;
        ((ComboBoxItem)_source.Items[0]!).Content = Loc.Get(K.HistoryPanelFilterAll);
        for (int i = 0; i < SourceChoices.Length; i++) ((ComboBoxItem)_source.Items[i + 1]!).Content = SourceText(SourceChoices[i]);
        ((ComboBoxItem)_mode.Items[0]!).Content = Loc.Get(K.HistoryPanelAgainstCurrent);
        ((ComboBoxItem)_mode.Items[1]!).Content = Loc.Get(K.HistoryPanelAgainstPrevious);
        _source.SelectedIndex = Math.Max(0, source);
        _mode.SelectedIndex = Math.Max(0, mode);
        _filling = false;
        _labelFilter.PlaceholderText = Loc.Get(K.HistoryPanelFilterLabel);
        AutomationProperties.SetName(this, Loc.Get(K.A11yHistoryPanel));
        AutomationProperties.SetName(_list, Loc.Get(K.A11yHistoryList));
        AutomationProperties.SetName(_source, Loc.Get(K.A11yHistorySource));
        AutomationProperties.SetName(_mode, Loc.Get(K.A11yHistoryMode));
        AutomationProperties.SetName(_labelFilter, Loc.Get(K.A11yHistoryLabelFilter));
        AutomationProperties.SetName(_header, Loc.Get(K.A11yHistoryDetails));
        _restore.Content = Loc.Get(K.HistoryRestoreFile);
        _label.Content = Loc.Get(K.HistorySetLabel);
        _copy.Content = Loc.Get(K.HistorySaveCopy);
        foreach (var button in new[] { _restore, _label, _copy }) AutomationProperties.SetName(button, (string)button.Content!);
        if (_disposed) return;
        Reload();
    }

    // ---- the list ----

    private HistorySource? SourceFilter => _source.SelectedIndex is > 0 and var i && i <= SourceChoices.Length ? SourceChoices[i - 1] : null;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void OnFileChanged(string path)
    {
        if (_disposed || !SamePath(path, _path)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _reload.Stop();
            _reload.Start();
        });
    }

    private IEnumerable<HistoryRevision> SelectedRevisions() => _list.SelectedItems?.OfType<ListBoxItem>().Select(i => _rows.FirstOrDefault(r => ReferenceEquals(r.Item, i))?.Revision).OfType<HistoryRevision>() ?? [];

    private HistoryRevision? Single() => SelectedRevisions().ToList() is [var one] ? one : null;

    /// <summary>Reads the revisions again and fills the list; the revisions that were chosen stay chosen, the counts already made are kept.</summary>
    public void Reload()
    {
        if (_disposed) return;
        var chosen = SelectedRevisions().Select(r => r.Id).ToHashSet();
        string? filter = string.IsNullOrWhiteSpace(_labelFilter.Text) ? null : _labelFilter.Text.Trim();
        try { _all = _history.List(_path); }
        catch (HistoryFormatException e)
        {
            Message?.Invoke(Loc.Get(K.HistoryStoreDamaged, ("reason", e.Message)));
            _all = [];
        }
        var source = SourceFilter;
        var shown = _all.Where(r => (source is null || r.Source == source) && (filter is null || r.Label?.Contains(filter, StringComparison.CurrentCultureIgnoreCase) == true)).ToList();

        _filling = true;
        _list.Items.Clear();
        _rows.Clear();
        foreach (var r in shown) _rows.Add(NewRow(r, isFirst: _all.Count > 0 && _all[^1].Id == r.Id));
        foreach (var row in _rows) _list.Items.Add(row.Item);
        foreach (var row in _rows.Where(r => chosen.Contains(r.Revision.Id))) _list.SelectedItems?.Add(row.Item);
        if (_list.SelectedItems?.Count == 0 && _rows.Count > 0) _list.SelectedIndex = 0;
        _filling = false;

        var summary = HistorySummary.Of(_all);
        _summary.Text = _all.Count == 0 ? Loc.Get(K.HistoryEmpty) : shown.Count == 0 ? Loc.Get(K.HistoryPanelNoMatch) : SummaryOf(summary);
        _retention.Text = RetentionOf(_history.Policy);
        StartCounting();
        // the comparison on the right stays as it is when the same revisions are still chosen, so a save does not redraw it
        if (_diffHost.Content is null || !chosen.SetEquals(SelectedRevisions().Select(r => r.Id))) _ = ShowSelectionAsync();
        else UpdateButtons([.. SelectedRevisions()]);
    }

    private Row NewRow(HistoryRevision r, bool isFirst)
    {
        var when = new TextBlock { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.8 };
        var change = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 2) };
        SetColumn(change, 1);
        SetRowSpan(change, 2);
        grid.RowDefinitions = new RowDefinitions("Auto,Auto");
        SetRow(detail, 1);
        grid.Children.Add(when);
        grid.Children.Add(detail);
        grid.Children.Add(change);
        var row = new Row { Revision = r, Item = new ListBoxItem { Content = grid }, When = when, Detail = detail, Change = change, IsFirst = isFirst };
        Fill(row);
        return row;
    }

    private void Fill(Row row)
    {
        var r = row.Revision;
        row.When.Text = r.Label is { Length: > 0 } label ? $"{When(r)}  {label}" : When(r);
        row.Detail.Text = DetailOf(r);
        row.Change.Text = _counted.TryGetValue(r.Id, out var c) && c.Change is { } change
            ? Loc.Get(K.HistoryPanelRowChange, ("added", change.Added), ("removed", change.Removed))
            : _counted.ContainsKey(r.Id) || row.IsFirst ? "" : "…";
        AutomationProperties.SetName(row.Item, string.Join(", ", new[] { When(r), DetailOf(r), ChangeSentence(row) }.Where(s => !string.IsNullOrEmpty(s))));
    }

    /// <summary>What a screen reader hears of the change of a revision: the counts, or why there are none.</summary>
    private string ChangeSentence(Row row)
    {
        if (row.IsFirst) return Loc.Get(K.HistoryPanelFirst);
        if (!_counted.TryGetValue(row.Revision.Id, out var c)) return Loc.Get(K.HistoryPanelCounting);
        return c.Change is { } change ? Loc.Get(K.HistoryPanelChange, ("added", change.Added), ("removed", change.Removed)) : Loc.Get(K.HistoryPanelNotCounted);
    }

    // ---- the counts of lines ----

    private void StartCounting()
    {
        _counting?.Cancel();
        var cts = _counting = new CancellationTokenSource();
        var codec = _codec();
        var ids = _all.Select(r => r.Id).ToHashSet();
        foreach (var gone in _counted.Keys.Where(k => !ids.Contains(k)).ToList()) _counted.Remove(gone);
        var known = new Dictionary<string, Counted>(_counted);
        _asked.Clear();
        Task.Run(() =>
        {
            try
            {
                _history.CountChanges(_path, codec, PluginLimitsPlaceholders.HistoryTextRevisionMaxBytes,
                    (r, before) =>
                    {
                        if (known.TryGetValue(r.Id, out var c) && c.Before == before?.Id) return false; // counted against the same revision already
                        _asked[r.Id] = before?.Id;
                        return true;
                    },
                    (r, change) =>
                    {
                        string? before = _asked.TryGetValue(r.Id, out var b) ? b : null;
                        Dispatcher.UIThread.Post(() => ApplyCount(cts, r, before, change));
                    }, cts.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or UnauthorizedAccessException or HistoryFormatException) { /* counted again by the next change of the list */ }
        }, cts.Token);
    }

    private void ApplyCount(CancellationTokenSource source, HistoryRevision r, string? before, RevisionChange? change)
    {
        if (_disposed || source.IsCancellationRequested) return;
        _counted[r.Id] = new Counted(before, change);
        if (_rows.FirstOrDefault(x => x.Revision.Id == r.Id) is { } row)
        {
            Fill(row);
            if (SelectedRevisions().ToList() is [var one] && one.Id == r.Id) ShowDetails(one);
        }
    }

    // ---- the comparison ----

    private void UpdateButtons(IReadOnlyList<HistoryRevision> chosen)
    {
        var one = chosen.Count == 1 ? chosen[0] : null;
        _restore.IsEnabled = one is not null;
        _restore.IsVisible = _actions.Restore is not null;
        _label.IsEnabled = _pin.IsEnabled = _copy.IsEnabled = one is not null;
        _pin.Content = Loc.Get(one is not null && one.Pins.Contains("user") ? K.HistoryUnpin : K.HistoryPin);
        AutomationProperties.SetName(_pin, (string)_pin.Content!);
    }

    private void ShowDetails(HistoryRevision r)
    {
        var row = _rows.FirstOrDefault(x => x.Revision.Id == r.Id);
        _header.Text = Loc.Get(K.HistoryPanelHeader, ("when", When(r)), ("source", SourceText(r.Source)), ("size", OpenDocument.FormatSize(r.Size)))
            + (r.Label is { Length: > 0 } label ? " · " + label : "");
        var before = _all.SkipWhile(x => x.Id != r.Id).Skip(1).FirstOrDefault();
        string size = before is null ? "" : Loc.Get(K.HistoryPanelSizeChange, ("delta", SizeDelta(r.Size - before.Size)));
        _details.Text = row is null ? "" : string.Join(" · ", new[] { ChangeSentence(row), size }.Where(s => !string.IsNullOrEmpty(s)));
    }

    private CompareSide EmptySide() => new() { Title = "", Text = "" };

    private async Task<CompareSide> SideOfAsync(HistoryRevision r, CancellationToken ct)
    {
        if (r.Size > PluginLimitsPlaceholders.HistoryTextRevisionMaxBytes) throw new InvalidOperationException(Loc.Get(K.HistoryRevisionTooBig, ("size", OpenDocument.FormatSize(r.Size))));
        var codec = _codec();
        var bytes = await Task.Run(() => _history.Read(_path, r.Id), ct);
        string title = Loc.Get(K.HistoryActionsTitle, ("time", When(r))) + (r.Label is { Length: > 0 } label ? " · " + label : "");
        return new CompareSide { Title = title, Text = codec.GetString(bytes.AsSpan(0, codec.CompleteLength(bytes))).TrimStart('﻿') };
    }

    private void SetDiff(DiffView? view)
    {
        if (_diffHost.Content is DiffView old)
        {
            _diffHost.Content = null;
            old.Session.Dispose();
        }
        _diffHost.Content = view;
    }

    /// <summary>Shows the comparison the chosen revisions ask for: one revision with the current text or with the revision before it, two revisions with each other (the older on the left).</summary>
    private async Task ShowSelectionAsync()
    {
        _showing?.Cancel();
        var cts = _showing = new CancellationTokenSource();
        var chosen = SelectedRevisions().OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        UpdateButtons(chosen);
        if (chosen.Count == 0)
        {
            _header.Text = _all.Count == 0 ? Loc.Get(K.HistoryEmpty) : "";
            _details.Text = "";
            SetDiff(null);
            return;
        }
        if (chosen.Count > 2)
        {
            _header.Text = Loc.Get(K.HistoryPanelTwoSelected);
            _details.Text = "";
            SetDiff(null);
            return;
        }
        try
        {
            CompareSide left, right;
            if (chosen.Count == 2)
            {
                _header.Text = Loc.Get(K.HistoryPanelTwoSelected);
                _details.Text = "";
                left = await SideOfAsync(chosen[0], cts.Token);
                right = await SideOfAsync(chosen[1], cts.Token);
            }
            else if (_mode.SelectedIndex == 0)
            {
                ShowDetails(chosen[0]);
                left = await SideOfAsync(chosen[0], cts.Token);
                right = _current();
                right.Title = Loc.Get(K.HistoryPanelCurrent);
            }
            else
            {
                ShowDetails(chosen[0]);
                var before = _all.SkipWhile(x => x.Id != chosen[0].Id).Skip(1).FirstOrDefault();
                left = before is null ? EmptySide() : await SideOfAsync(before, cts.Token);
                right = await SideOfAsync(chosen[0], cts.Token);
            }
            if (cts.IsCancellationRequested || _disposed) return;
            var view = new DiffView(new CompareSession(left, right) { Ignore = _ignore() }, _options());
            view.Refused += text => Message?.Invoke(text);
            SetDiff(view);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (InvalidOperationException e)
        {
            if (!cts.IsCancellationRequested) Message?.Invoke(e.Message);
            SetDiff(null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FileNotFoundException or HistoryFormatException)
        {
            if (!cts.IsCancellationRequested) Message?.Invoke(Loc.Get(K.HistoryFailed, ("reason", e.Message)));
            SetDiff(null);
        }
    }

    /// <summary>Moves the keyboard focus into the list of revisions.</summary>
    public void FocusList()
    {
        if (_list.SelectedItem is ListBoxItem item) item.Focus();
        else _list.Focus();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _history.FileChanged -= OnFileChanged;
        _reload.Stop();
        _counting?.Cancel();
        _showing?.Cancel();
        SetDiff(null);
    }
}

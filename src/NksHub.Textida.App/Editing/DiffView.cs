using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The content of a compare tab: the two sides next to each other with equal lines level, changed blocks tinted and marked in the gutter, and an arrow
/// per block in the gutter that copies the block to the other side. One scroll position serves both sides, so a side follows the other across inserted
/// and removed blocks (<see cref="DiffLayout"/>). It draws only the rows on screen and reads the lines of a side through its sparse line index, so the
/// size of a side does not matter. The session holds the diff; this control turns clicks, keys and the clipboard into calls on it.
/// </summary>
internal sealed partial class DiffView : DockPanel
{
    private const double GutterWidth = 48;

    /// <summary>The digit keys 1 to 9 press the first nine buttons of the plugin.</summary>
    private const int MaxActionKeys = 9;

    private readonly CompareSession _session;
    private readonly DiffSurface _surface;
    private readonly ScrollBar _bar = new() { Orientation = Orientation.Vertical, Minimum = 0, SmallChange = 1, AllowAutoHide = false };
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _leftTitle = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 4, 4, 4) };
    private readonly TextBlock _rightTitle = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 4, 4, 4) };
    private readonly Button _previous = new(), _next = new(), _toLeft = new(), _toRight = new(), _pasteLeft = new(), _pasteRight = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly StackPanel _tools;
    private readonly List<(DiffAction Action, Button Button)> _actions = [];
    private EditorOptions _options;
    private long _top;
    private int _current = -1, _restoreHunk = -1;
    private bool _syncingBar, _shown;

    public DiffView(CompareSession session, EditorOptions options)
    {
        _session = session;
        _options = options;
        Focusable = true;
        Classes.Add("compare");
        _surface = new DiffSurface(this);

        foreach (var b in new[] { _previous, _next, _toLeft, _toRight, _pasteLeft, _pasteRight })
        {
            b.Classes.Add("barbutton");
            b.Padding = new Thickness(10, 2);
            b.MinHeight = 24;
            b.Focusable = false;
        }
        _pasteLeft.IsVisible = session.Left.Pasteable;
        _pasteRight.IsVisible = session.Right.Pasteable;
        _tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 6, 8, 6), Children = { _previous, _next, _toLeft, _toRight, _pasteLeft, _pasteRight, _summary } };
        SetDock(_tools, Dock.Top);
        Children.Add(_tools);

        var titles = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{GutterWidth},*"), Margin = new Thickness(0, 0, 16, 0) };
        Grid.SetColumn(_rightTitle, 2);
        titles.Children.Add(_leftTitle);
        titles.Children.Add(_rightTitle);
        SetDock(titles, Dock.Top);
        Children.Add(titles);

        SetDock(_bar, Dock.Right);
        Children.Add(_bar);
        Children.Add(_surface);

        _bar.ValueChanged += (_, _) =>
        {
            if (_syncingBar) return;
            _top = (long)_bar.Value;
            _surface.InvalidateVisual();
            ViewChanged?.Invoke();
        };
        _previous.Click += (_, _) => PreviousDifference();
        _next.Click += (_, _) => NextDifference();
        _toLeft.Click += (_, _) => CopyCurrent(DiffSide.A);
        _toRight.Click += (_, _) => CopyCurrent(DiffSide.B);
        _pasteLeft.Click += (_, _) => _ = PasteAsync(DiffSide.A);
        _pasteRight.Click += (_, _) => _ = PasteAsync(DiffSide.B);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = _session.RefreshAsync();
        };
        ApplyTexts();
        UpdateState();
    }

    public CompareSession Session => _session;
    public long TopRow => _top;
    public int CurrentHunk => _current;
    public int VisibleRows => Math.Max(1, _surface.VisibleRows);
    public string SummaryText => _summary.Text ?? "";
    public string LeftTitleText => _leftTitle.Text ?? "";
    public string RightTitleText => _rightTitle.Text ?? "";
    public Button PreviousButton => _previous;
    public Button NextButton => _next;
    public Button ToLeftButton => _toLeft;
    public Button ToRightButton => _toRight;
    public Button PasteLeftButton => _pasteLeft;
    public Button PasteRightButton => _pasteRight;
    internal EditorOptions Options => _options;

    /// <summary>Raised when the first row changed (scrolled by the user, a jump to a difference, or a new diff).</summary>
    public event Action? ViewChanged;

    /// <summary>Raised with a localised sentence when a copy was refused or something else needs saying.</summary>
    public event Action<string>? Refused;

    /// <summary>The user asked for the other side of a pasted text; the window reads the clipboard.</summary>
    public Func<Task<string?>>? ReadClipboard { get; set; }

    /// <summary>The user pressed a button the plugin that opened the comparison added: the action and the number of the current difference.</summary>
    public event Action<DiffAction, int>? ActionRequested;

    public IReadOnlyList<Button> ActionButtons => [.. _actions.Select(a => a.Button)];

    /// <summary>
    /// Adds a button for each action of the plugin that opened the comparison, after the buttons of the view; each acts on the current difference. The digit keys 1 to 9 press the buttons in order
    /// while the view has the focus, and the buttons say which key is theirs.
    /// </summary>
    public void SetActions(IReadOnlyList<DiffAction>? actions)
    {
        foreach (var (_, button) in _actions) _tools.Children.Remove(button);
        _actions.Clear();
        int at = _tools.Children.IndexOf(_summary);
        foreach (var action in actions ?? [])
        {
            int number = _actions.Count;
            var button = new Button { Content = action.Title, Padding = new Thickness(10, 2), MinHeight = 24, Focusable = false };
            button.Classes.Add("barbutton");
            button.Click += (_, _) => RunAction(number);
            AutomationProperties.SetName(button, action.Title);
            if (number < MaxActionKeys)
            {
                string key = (number + 1).ToString();
                AutomationProperties.SetAcceleratorKey(button, key);
                ToolTip.SetTip(button, Loc.Get(K.CompareActionTip, ("title", action.Title), ("key", key)));
            }
            _tools.Children.Insert(at++, button);
            _actions.Add((action, button));
        }
        UpdateState();
    }

    /// <summary>Runs action <paramref name="number"/> on the current difference; false when there is no such action or no current difference.</summary>
    public bool RunAction(int number)
    {
        if (number < 0 || number >= _actions.Count || _current < 0 || ActionRequested is null) return false;
        ActionRequested(_actions[number].Action, _current);
        return true;
    }

    public void ApplyOptions(EditorOptions o)
    {
        _options = o;
        _surface.InvalidateMeasure();
        _surface.InvalidateVisual();
    }

    /// <summary>Compares again when what the diff ignores changed; a view that is not shown compares when it is shown again (see <see cref="CompareSession.IsCurrent"/>).</summary>
    public void SetIgnore(DiffIgnore ignore)
    {
        if (_session.Ignore == ignore) return;
        _session.Ignore = ignore;
        if (_shown) _ = _session.RefreshAsync();
    }

    public void ApplyTexts()
    {
        _previous.Content = Loc.Get(K.ComparePrevious);
        _next.Content = Loc.Get(K.CompareNext);
        _toLeft.Content = Loc.Get(K.CompareToLeft);
        _toRight.Content = Loc.Get(K.CompareToRight);
        _pasteLeft.Content = Loc.Get(K.ComparePasteLeft);
        _pasteRight.Content = Loc.Get(K.ComparePasteRight);
        _leftTitle.Text = _session.Left.Title;
        _rightTitle.Text = _session.Right.Title;
        AutomationProperties.SetName(this, Loc.Get(K.A11yCompare));
        AutomationProperties.SetName(_surface, Loc.Get(K.A11yCompare));
        UpdateState();
    }

    // ---- attach ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _shown = true;
        _session.Updated += OnUpdated;
        Watch(_session.Left, subscribe: true);
        Watch(_session.Right, subscribe: true);
        if (!_session.IsCurrent && !_session.Busy) _ = _session.RefreshAsync();
        else UpdateState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _shown = false;
        _session.Updated -= OnUpdated;
        Watch(_session.Left, subscribe: false);
        Watch(_session.Right, subscribe: false);
        _debounce.Stop();
    }

    private void Watch(CompareSide side, bool subscribe)
    {
        if (side.Document is not { } od) return;
        if (subscribe) { od.Changed += ScheduleRefresh; od.Replaced += ScheduleRefresh; }
        else { od.Changed -= ScheduleRefresh; od.Replaced -= ScheduleRefresh; }
    }

    private void ScheduleRefresh()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Makes the difference with this number the current one once the first comparison ends (or the last one when there are fewer): a comparison that takes the place of another one
    /// opened with the same key carries on where the user was, and a hunk that was taken away leaves the next one current.
    /// </summary>
    public void RestoreHunk(int hunk) => _restoreHunk = hunk;

    private void OnUpdated()
    {
        if (_session.Diff is { } diff)
        {
            _current = diff.Hunks.Count == 0 ? -1 : Math.Min(_current, diff.Hunks.Count - 1);
            UpdateScrollRange();
            if (_restoreHunk >= 0 && diff.Hunks.Count > 0)
            {
                int hunk = Math.Min(_restoreHunk, diff.Hunks.Count - 1);
                _restoreHunk = -1;
                SelectHunk(hunk);
            }
        }
        UpdateState();
        _surface.InvalidateVisual();
        ViewChanged?.Invoke();
    }

    private void UpdateScrollRange()
    {
        long rows = _session.Diff?.Layout.RowCount ?? 0;
        _syncingBar = true;
        _bar.Maximum = Math.Max(0, rows - VisibleRows);
        _bar.ViewportSize = VisibleRows;
        _bar.LargeChange = Math.Max(1, VisibleRows - 1);
        _top = Math.Clamp(_top, 0, (long)_bar.Maximum);
        _bar.Value = _top;
        _syncingBar = false;
    }

    internal void SizeChanged() => UpdateScrollRange();

    private void UpdateState()
    {
        var diff = _session.Diff;
        int count = diff?.Hunks.Count ?? 0;
        string text = _session.Problem is { } p ? Loc.Get(K.CompareProblem, ("reason", p))
            : diff is null ? Loc.Get(K.CompareBusy)
            : count == 0 ? Loc.Get(K.CompareIdentical)
            : Loc.Plural(K.CompareDifferences, count);
        if (diff is { Exact: false }) text += " · " + Loc.Get(K.CompareApproximate);
        if (diff is not null && diff.Ignore.HasFlag(DiffIgnore.Whitespace)) text += " · " + Loc.Get(K.CompareIgnoresWhitespace);
        if (diff is not null && diff.Ignore.HasFlag(DiffIgnore.Case)) text += " · " + Loc.Get(K.CompareIgnoresCase);
        _summary.Text = text;
        _previous.IsEnabled = _next.IsEnabled = count > 0;
        _toRight.IsEnabled = _current >= 0 && _session.Right.CanReceive;
        _toLeft.IsEnabled = _current >= 0 && _session.Left.CanReceive;
        _toRight.IsVisible = _session.Right.CanReceive;
        _toLeft.IsVisible = _session.Left.CanReceive;
        foreach (var (_, button) in _actions) button.IsEnabled = _current >= 0;
        (ControlAutomationPeer.FromElement(this) as DiffAutomationPeer)?.Changed();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DiffAutomationPeer(this);

    /// <summary>
    /// The sentence a screen reader says for the view: the current difference with the lines it covers on each side and the first line of each side,
    /// or the summary when no difference is current.
    /// </summary>
    internal string Describe()
    {
        if (_session.Diff is not { } diff || _current < 0 || _current >= diff.Hunks.Count) return SummaryText;
        var h = diff.Hunks[_current];
        string text = Loc.Get(K.CompareA11yHunk, ("index", _current + 1), ("total", diff.Hunks.Count), ("left", Lines(h.AStart, h.ACount)), ("right", Lines(h.BStart, h.BCount)));
        if (h.ACount > 0) text += ". " + Loc.Get(K.CompareA11yLeft, ("text", Head(diff.A, h.AStart)));
        if (h.BCount > 0) text += ". " + Loc.Get(K.CompareA11yRight, ("text", Head(diff.B, h.BStart)));
        return text;

        static string Lines(long start, long count) =>
            count == 0 ? Loc.Get(K.CompareA11yNoLines)
            : count == 1 ? Loc.Get(K.CompareA11yLine, ("line", start + 1))
            : Loc.Get(K.CompareA11yLines, ("first", start + 1), ("last", start + count));

        static string Head(LineIndex index, long line) => index.ReadLines(line, 1) is [var first] ? first.Trim() : "";
    }

    // ---- moving ----

    /// <summary>Scrolls so that <paramref name="row"/> is the first row shown.</summary>
    public void ScrollToRow(long row)
    {
        UpdateScrollRange();
        _top = Math.Clamp(row, 0, (long)_bar.Maximum);
        _syncingBar = true;
        _bar.Value = _top;
        _syncingBar = false;
        _surface.InvalidateVisual();
        ViewChanged?.Invoke();
    }

    public void SelectHunk(int hunk)
    {
        if (_session.Diff is not { } diff || hunk < 0 || hunk >= diff.Hunks.Count) return;
        _current = hunk;
        long row = diff.Layout.RowOfHunk(hunk);
        if (row < _top || row >= _top + VisibleRows) ScrollToRow(Math.Max(0, row - VisibleRows / 3));
        UpdateState();
        _surface.InvalidateVisual();
    }

    public bool NextDifference() => Move(+1);
    public bool PreviousDifference() => Move(-1);

    private bool Move(int step)
    {
        if (_session.Diff is not { Hunks.Count: > 0 } diff) return false;
        int count = diff.Hunks.Count, target;
        if (_current >= 0) target = (_current + step + count) % count;
        else
        {
            // nothing selected yet: the first block at or below the top row, or the last one above it
            int i = 0;
            while (i < count && diff.Layout.RowOfHunk(i) < _top) i++;
            target = step > 0 ? i % count : (i - 1 + count) % count;
        }
        SelectHunk(target);
        return true;
    }

    /// <summary>First line of a side that is shown (the line level with the top row).</summary>
    public long FirstLine(DiffSide side)
    {
        if (_session.Diff is not { } diff) return 0;
        var row = diff.Layout.Row(Math.Min(_top, Math.Max(0, diff.Layout.RowCount - 1)));
        long line = side == DiffSide.A ? row.A : row.B;
        return line >= 0 ? line : diff.Layout.MapLine(side == DiffSide.A ? DiffSide.B : DiffSide.A, side == DiffSide.A ? row.B : row.A);
    }

    // ---- copying ----

    /// <summary>Copies the current block to the other side's document: <paramref name="target"/> is the side that changes.</summary>
    public bool CopyCurrent(DiffSide target) => Copy(_current, target);

    public bool Copy(int hunk, DiffSide target)
    {
        var status = _session.Transfer(hunk, target, out string? problem);
        switch (status)
        {
            case TransferStatus.Done:
                _ = _session.RefreshAsync();
                return true;
            case TransferStatus.Stale:
                Refused?.Invoke(Loc.Get(K.CompareStale));
                _ = _session.RefreshAsync();
                return false;
            case TransferStatus.Lossy:
                Refused?.Invoke(Loc.Get(K.CompareLossy));
                return false;
            case TransferStatus.Failed:
                Refused?.Invoke(Loc.Get(K.CompareCopyFailed, ("reason", problem ?? "")));
                return false;
            default:
                Refused?.Invoke(Loc.Get(K.CompareReadOnly));
                return false;
        }
    }

    /// <summary>Puts the text of the clipboard into a side that was made for pasting, then compares again.</summary>
    public async Task<bool> PasteAsync(DiffSide side)
    {
        var target = side == DiffSide.A ? _session.Left : _session.Right;
        if (!target.Pasteable) return false;
        string? text = ReadClipboard is { } read ? await read() : TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text))
        {
            Refused?.Invoke(Loc.Get(K.CompareClipboardEmpty));
            return false;
        }
        target.Text = text;
        await _session.RefreshAsync();
        return true;
    }

    // ---- keys ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers != KeyModifiers.None) return;
        int digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1 : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1 : -1;
        if (digit >= 0)
        {
            e.Handled = RunAction(digit);
            return;
        }
        long page = Math.Max(1, VisibleRows - 1);
        switch (e.Key)
        {
            case Key.Up: ScrollToRow(_top - 1); break;
            case Key.Down: ScrollToRow(_top + 1); break;
            case Key.PageUp: ScrollToRow(_top - page); break;
            case Key.PageDown: ScrollToRow(_top + page); break;
            case Key.Home: ScrollToRow(0); break;
            case Key.End: ScrollToRow(long.MaxValue / 2); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0) _surface.ScrollHorizontally(e.Delta.X != 0 ? e.Delta.X : e.Delta.Y);
        else ScrollToRow(_top - (long)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }
}

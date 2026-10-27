using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The binary comparison of two files (P5.6.5): two read-only hex grids side by side that scroll together, the differences tinted in both and listed below, and the
/// choice between the aligned comparison (inserted and deleted bytes are found) and the byte by byte one. The comparison runs in the background.
/// </summary>
internal sealed class HexCompareWindow : Window
{
    private readonly OpenDocument _left;
    private readonly OpenDocument _right;
    private readonly string _leftName, _rightName;
    private readonly bool _rightIsFile;
    private readonly HexView _leftView, _rightView;
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly TextBlock _leftTitle = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _rightTitle = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly RadioButton _aligned = new() { GroupName = "mode" };
    private readonly RadioButton _positional = new() { GroupName = "mode" };
    private readonly Button _previous = new(), _next = new(), _close = new();
    private CancellationTokenSource? _run;
    private BinaryCompareResult? _result;
    private long _runId;
    private bool _alignedMode = true;
    private bool _syncing;
    private bool _subscribed;

    /// <param name="right">The document on the right; the window disposes it when <paramref name="ownsRight"/>.</param>
    public HexCompareWindow(OpenDocument left, string leftName, OpenDocument right, string rightName, bool rightIsFile, bool ownsRight, EditorOptions options)
    {
        _left = left;
        _right = right;
        _leftName = leftName;
        _rightName = rightName;
        _rightIsFile = rightIsFile;
        Classes.Add("shell");
        Width = 1280;
        Height = 820;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _leftView = new HexView(left, options) { ReadOnly = true };
        _rightView = new HexView(right, options) { ReadOnly = true };
        _leftView.Minimap.IsVisible = _rightView.Minimap.IsVisible = true;

        _aligned.IsChecked = true;
        // the radio group unchecks the other button only after the event of the checked one, so the mode is taken from the button that was checked
        _aligned.IsCheckedChanged += (_, _) => { if (_aligned.IsChecked == true) { _alignedMode = true; _ = RunAsync(); } };
        _positional.IsCheckedChanged += (_, _) => { if (_positional.IsChecked == true) { _alignedMode = false; _ = RunAsync(); } };
        _previous.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
        _close.Click += (_, _) => Close();
        _list.SelectionChanged += (_, _) => { if (!_syncing && _list.SelectedIndex >= 0) ShowDifference(_list.SelectedIndex); };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedIndex >= 0) { ShowDifference(_list.SelectedIndex); e.Handled = true; }
        };
        _leftView.ViewChanged += () => Follow(_leftView, _rightView, left: true);
        _rightView.ViewChanged += () => Follow(_rightView, _leftView, left: false);

        var leftPane = new DockPanel { Children = { _leftTitle, _leftView } };
        DockPanel.SetDock(_leftTitle, Dock.Top);
        var rightPane = new DockPanel { Children = { _rightTitle, _rightView } };
        DockPanel.SetDock(_rightTitle, Dock.Top);
        var grids = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*"), Margin = new Thickness(12, 8, 12, 4) };
        Grid.SetColumn(rightPane, 2);
        grids.Children.Add(leftPane);
        grids.Children.Add(rightPane);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _aligned, _positional, _previous, _next } };
        var top = new DockPanel { Margin = new Thickness(12, 10, 12, 0), LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        top.Children.Add(buttons);
        top.Children.Add(new StackPanel { Children = { _summary, _note } });

        var bottom = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(12, 0, 12, 10), Height = 170 };
        var frame = new Border { Child = _list, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) };
        Grid.SetRow(_close, 1);
        _close.HorizontalAlignment = HorizontalAlignment.Right;
        _close.Margin = new Thickness(0, 6, 0, 0);
        bottom.Children.Add(frame);
        bottom.Children.Add(_close);

        var root = new DockPanel { Children = { top, bottom, grids } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F8) { Step(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1); e.Handled = true; }
        };
        Opened += (_, _) =>
        {
            Subscribe();
            ApplyTexts();
            _ = RunAsync();
        };
        Closed += (_, _) =>
        {
            Unsubscribe();
            _run?.Cancel();
            if (ownsRight) _right.Dispose();
        };
        ApplyTexts();
    }

    internal HexView LeftView => _leftView;
    internal HexView RightView => _rightView;
    internal BinaryCompareResult? Result => _result;
    internal IReadOnlyList<string> RowTexts => [.. _list.Items.OfType<ListBoxItem>().Select(AutomationProperties.GetName)];
    internal string SummaryText => _summary.Text ?? "";
    internal string NoteText => _note.Text ?? "";
    internal bool IsAligned => _alignedMode;
    internal int SelectedDifference => _list.SelectedIndex;


    /// <summary>The comparison that is running or ran last; for tests.</summary>
    internal Task? Running { get; private set; }

    private void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        Loc.Changed += ApplyTexts;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        Loc.Changed -= ApplyTexts;
    }

    internal void SetAligned(bool aligned)
    {
        if (aligned) _aligned.IsChecked = true;
        else _positional.IsChecked = true;
    }

    private void ApplyTexts()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyTexts);
            return;
        }
        Title = Loc.Get(K.HexCompareTitle, ("left", _leftName), ("right", _rightName));
        _aligned.Content = Loc.Get(K.HexCompareAligned);
        _positional.Content = Loc.Get(K.HexComparePositional);
        _previous.Content = Loc.Get(K.HexComparePrevious);
        _next.Content = Loc.Get(K.HexCompareNext);
        _close.Content = Loc.Get(K.HexCompareClose);
        _leftTitle.Text = Loc.Get(K.HexCompareLeft, ("name", Loc.Get(K.HexCompareMemory, ("name", _leftName))), ("size", _left.Doc.ByteLength));
        _rightTitle.Text = Loc.Get(K.HexCompareLeft, ("name", Loc.Get(_rightIsFile ? K.HexCompareFile : K.HexCompareMemory, ("name", _rightName))), ("size", _right.Doc.ByteLength));
        AutomationProperties.SetName(_list, Loc.Get(K.HexCompareListA11y));
        AutomationProperties.SetName(_leftView, Loc.Get(K.HexCompareLeftA11y));
        AutomationProperties.SetName(_rightView, Loc.Get(K.HexCompareRightA11y));
        if (_result is not null) Show(_result);
    }

    // ---- running ----

    private async Task RunAsync()
    {
        _run?.Cancel();
        var cts = _run = new CancellationTokenSource();
        long id = ++_runId;
        _summary.Text = Loc.Get(K.HexCompareRunning);
        _note.Text = "";
        DocumentSnapshot left = _left.Doc.GetSnapshot(), right = _right.Doc.GetSnapshot();
        bool aligned = _alignedMode;
        var options = new BinaryCompareOptions();
        bool tooLarge = aligned && Math.Max(left.ByteLength, right.ByteLength) > options.AlignLimit;
        var work = Task.Run(() =>
        {
            try
            {
                var result = aligned && !tooLarge ? BinaryCompare.Aligned(left, right, options, cts.Token) : BinaryCompare.Positional(left, right, options, cts.Token);
                return (Result: result, Error: (string?)null);
            }
            catch (OperationCanceledException)
            {
                return (null, null);
            }
            catch (Exception e)
            {
                return (null, e.Message); // whatever stopped it is told in the window; the comparison has no result to keep
            }
        });
        Running = work;
        var (result, error) = await work.ConfigureAwait(false);
        // the answer is shown on the UI thread whatever thread the change of the mode came from
        Dispatcher.UIThread.Post(() => Finish(id, result, error, tooLarge ? options.AlignLimit >> 20 : null));
    }

    private void Finish(long id, BinaryCompareResult? result, string? error, long? limitMiB)
    {
        if (id != _runId) return;
        if (error is not null)
        {
            _summary.Text = Loc.Get(K.HexCompareFailed, ("reason", error));
            return;
        }
        if (result is null) return;
        Show(result);
        if (limitMiB is { } limit) _note.Text = Loc.Get(K.HexCompareTooLarge, ("limit", limit));
    }

    private static string RangeText(HexView view, ByteRange range) =>
        range.IsEmpty ? view.OffsetText(range.Start) : $"{view.OffsetText(range.Start)}–{view.OffsetText(range.End)}";

    private void Show(BinaryCompareResult result)
    {
        _result = result;
        _syncing = true;
        _list.Items.Clear();
        for (int i = 0; i < result.Differences.Count; i++)
        {
            var d = result.Differences[i];
            string kind = Loc.Get(d.Kind switch { DiffKind.Insert => K.HexCompareKindInsert, DiffKind.Delete => K.HexCompareKindDelete, _ => K.HexCompareKindReplace });
            string text = Loc.Get(K.HexCompareRow, ("n", i + 1), ("kind", kind), ("left", RangeText(_leftView, d.Left)), ("right", RangeText(_rightView, d.Right)));
            var item = new ListBoxItem { Content = new TextBlock { Text = text, FontFamily = new FontFamily(_leftView.Options.FontFamily) }, Padding = new Thickness(6, 2) };
            AutomationProperties.SetName(item, text);
            _list.Items.Add(item);
        }
        _syncing = false;
        _summary.Text = result.AreEqual
            ? Loc.Get(K.HexCompareIdentical)
            : Loc.Plural(K.HexCompareSummary, result.Differences.Count, ("left", result.LeftChanged), ("right", result.RightChanged));
        _note.Text = result.Truncated ? Loc.Get(K.HexCompareTruncated, ("count", result.Differences.Count)) : "";
        _leftView.ShowDifferences([.. result.Differences.Where(d => !d.Left.IsEmpty).Select(d => d.Left)], "DiffRemoved");
        _rightView.ShowDifferences([.. result.Differences.Where(d => !d.Right.IsEmpty).Select(d => d.Right)], "DiffAdded");
        _leftView.Minimap.Matches = [.. result.Differences.Select(d => d.Left.Start)];
        _rightView.Minimap.Matches = [.. result.Differences.Select(d => d.Right.Start)];
        _previous.IsEnabled = _next.IsEnabled = result.Differences.Count > 0;
    }

    // ---- moving ----

    /// <summary>Scrolling one grid scrolls the other to the corresponding place; the mapping follows the inserted and deleted bytes.</summary>
    private void Follow(HexView from, HexView to, bool left)
    {
        if (_syncing || _result is null) return;
        _syncing = true;
        try
        {
            long offset = from.FirstByte;
            to.ScrollToOffset(left ? _result.MapLeftToRight(offset) : _result.MapRightToLeft(offset));
        }
        finally { _syncing = false; }
    }

    private void ShowDifference(int index)
    {
        if (_result is not { } result || index < 0 || index >= result.Differences.Count) return;
        var d = result.Differences[index];
        _syncing = true;
        try
        {
            Place(_leftView, d.Left);
            Place(_rightView, d.Right);
            _list.SelectedIndex = index;
            _list.ScrollIntoView(_list.Items[index]!);
        }
        finally { _syncing = false; }
    }

    private static void Place(HexView view, ByteRange range)
    {
        long length = view.Document.ByteLength;
        if (range.IsEmpty) view.MoveTo(Math.Min(range.Start, Math.Max(0, length - 1)));
        else view.Select(range.Start, range.End);
        view.Center(range.Start);
    }

    /// <summary>The next (or previous) difference after the one selected, or after the first byte on screen; wraps around.</summary>
    internal void Step(int direction)
    {
        if (_result is not { Differences.Count: > 0 } result) return;
        int current = _list.SelectedIndex;
        if (current < 0)
        {
            long at = _leftView.FirstByte;
            current = direction > 0 ? result.Differences.ToList().FindLastIndex(d => d.Left.End <= at) : result.Differences.ToList().FindIndex(d => d.Left.Start >= at);
        }
        int next = ((current + direction) % result.Differences.Count + result.Differences.Count) % result.Differences.Count;
        ShowDifference(next);
    }

    internal void Select(int index) => ShowDifference(index);
}

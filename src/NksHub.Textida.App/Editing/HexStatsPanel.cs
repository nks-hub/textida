using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>The 256 bars of a byte histogram, one per value, in a height that does not let the most frequent value hide the rest.</summary>
internal sealed class HistogramBars : Control
{
    private IReadOnlyList<long> _counts = [];

    public HistogramBars()
    {
        Height = 84;
        Focusable = false;
    }

    public void Show(IReadOnlyList<long> counts)
    {
        _counts = counts;
        InvalidateVisual();
    }

    private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext context)
    {
        var frame = Brush("Border", Brushes.Gray);
        context.DrawRectangle(new Pen(frame, 1), new Rect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1)));
        if (_counts.Count != 256) return;
        long max = _counts.Max();
        if (max == 0) return;
        var accent = Brush("Accent", Brushes.Teal);
        var muted = Brush("FgMuted", Brushes.Gray);
        var danger = Brush("Danger", Brushes.Red);
        double w = (Bounds.Width - 2) / 256.0, h = Bounds.Height - 2;
        for (int v = 0; v < 256; v++)
        {
            if (_counts[v] == 0) continue;
            double bar = Math.Max(1, Math.Sqrt((double)_counts[v] / max) * h);
            var brush = ByteClasses.Of((byte)v) switch { ByteClass.Text => accent, ByteClass.Ones => danger, _ => muted };
            context.FillRectangle(brush, new Rect(1 + v * w, 1 + h - bar, Math.Max(1, w), bar));
        }
    }
}

/// <summary>
/// The histogram of the selection beside the hex grid (the whole file when nothing is selected and it is small enough): how often each byte value occurs,
/// how many distinct values there are, the entropy, the most frequent values and the shares of the byte classes. The counting runs in the background and
/// starts again when the selection or the content changes.
/// </summary>
internal sealed class HexStatsPanel : Border
{
    private const int TopCount = 5;
    private readonly HexView _owner;
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _range = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly HistogramBars _bars = new();
    private readonly TextBlock _figures = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private CancellationTokenSource? _job;
    private int _generation;
    private (long Revision, long Start, long End) _shownFor = (-1, -1, -1);

    public HexStatsPanel(HexView owner)
    {
        _owner = owner;
        Classes.Add("sidepanel");
        Width = 280;
        Padding = new Thickness(10, 8);
        var stack = new StackPanel { Children = { _title, _range, _bars, _figures } };
        Child = stack;
        _settle.Tick += (_, _) => { _settle.Stop(); Refresh(); };
        AutomationProperties.SetLiveSetting(_figures, AutomationLiveSetting.Polite);
        ApplyTexts();
    }

    /// <summary>The histogram on show, or null while it is being counted; for tests.</summary>
    internal ByteHistogram? Shown { get; private set; }

    /// <summary>The count that is running now, for tests.</summary>
    internal Task? Counting { get; private set; }

    internal string FiguresText => _figures.Text ?? "";
    internal string RangeText => _range.Text ?? "";

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
        _owner.ViewChanged += Schedule;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
        _owner.ViewChanged -= Schedule;
        _settle.Stop();
        Cancel();
    }

    private void ApplyTexts()
    {
        _title.Text = Loc.Get(K.HexStatsTitle);
        AutomationProperties.SetName(_bars, Loc.Get(K.HexStatsBarsA11y));
        AutomationProperties.SetName(_figures, Loc.Get(K.HexStatsTitle));
        _shownFor = (-1, -1, -1);
        Refresh();
    }

    private void Schedule()
    {
        if (!IsVisible) return;
        _settle.Stop();
        _settle.Start();
    }

    private void Cancel()
    {
        _job?.Cancel();
        _job = null;
        _generation++;
    }

    private static string Percent(long part, long total) => total == 0 ? "0%" : (100.0 * part / total).ToString("0.#", CultureInfo.CurrentCulture) + "%";

    /// <summary>Counts the selection (or the file) again unless it was counted already.</summary>
    public void Refresh()
    {
        if (!IsVisible) return;
        var doc = _owner.Document;
        var editor = _owner.Editor;
        bool selection = editor.HasSelection;
        var range = selection ? editor.Selection : new ByteRange(0, doc.ByteLength);
        var key = (doc.Revision, range.Start, range.End);
        if (key == _shownFor) return;
        _shownFor = key;
        Cancel();
        _range.Text = selection
            ? Loc.Get(K.HexStatsSelection, ("bytes", range.Length), ("start", _owner.OffsetText(range.Start)), ("end", _owner.OffsetText(range.End)))
            : Loc.Get(K.HexStatsFile, ("bytes", range.Length));
        if (!selection && range.Length > ByteMapBuilder.FullComputeLimit)
        {
            Shown = null;
            _bars.Show([]);
            _figures.Text = Loc.Get(K.HexStatsTooLarge);
            return;
        }
        _figures.Text = Loc.Get(K.HexStatsCounting);
        DocumentSnapshot snapshot;
        try { snapshot = doc.GetSnapshot(); }
        catch (ObjectDisposedException) { return; }
        int generation = _generation;
        var cts = _job = new CancellationTokenSource();
        Counting = Task.Run(() =>
        {
            try
            {
                var h = ByteHistogram.Compute(snapshot, range, cts.Token);
                Dispatcher.UIThread.Post(() => { if (generation == _generation) Show(h); });
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                // the content moved on; the next change counts again
            }
        });
    }

    private void Show(ByteHistogram h)
    {
        Shown = h;
        _bars.Show(h.Counts);
        long total = h.Total;
        if (total == 0)
        {
            _figures.Text = Loc.Get(K.HexStatsEmpty);
            return;
        }
        var top = string.Join(", ", h.Top(TopCount).Select(t => $"{t.Value:X2} ×{t.Count} ({Percent(t.Count, total)})"));
        _figures.Text = string.Join("\n", new[]
        {
            Loc.Get(K.HexStatsDistinct, ("n", h.Distinct)),
            Loc.Get(K.HexStatsEntropy, ("bits", h.Entropy.ToString("0.00", CultureInfo.CurrentCulture))),
            Loc.Get(K.HexStatsTop, ("values", top)),
            Loc.Get(K.HexStatsClasses, ("zero", Percent(h.CountOf(ByteClass.Zero), total)), ("text", Percent(h.CountOf(ByteClass.Text), total)),
                ("control", Percent(h.CountOf(ByteClass.Control), total)), ("high", Percent(h.CountOf(ByteClass.High), total)), ("ones", Percent(h.CountOf(ByteClass.Ones), total))),
        });
    }

    public bool FocusPanel()
    {
        IsVisible = true;
        _shownFor = (-1, -1, -1);
        Refresh();
        return Focus();
    }
}

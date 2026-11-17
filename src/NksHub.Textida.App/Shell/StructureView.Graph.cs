using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

internal sealed class StructureGraph : Control, IDisposable
{
    private const double CardWidth = 220, CardHeight = 58, ColumnGap = 72, RowGap = 18;
    private readonly StructureTree tree;
    private readonly Dictionary<StructureTreeNode, (TextLayout Key, TextLayout? Value)> labels = [];
    private CancellationTokenSource? layoutRequest;
    private GraphCard[] cards = [];
    private Vector pan = new(24, 24);
    private double scale = 1;
    private Point? pressed, lastPoint;
    private bool dragged, cameraSet, disposed;
    private long version;

    private sealed record Entry(StructureTreeNode Node, long Version, int Parent, string Key, string Value);
    private sealed record GraphCard(Entry Entry, Rect Bounds);

    public StructureGraph(StructureTree tree)
    {
        this.tree = tree;
        Focusable = true; ClipToBounds = true;
        tree.ProjectionChanged += OnProjectionChanged;
        tree.SelectionChanged += OnSelectionChanged;
        ActualThemeVariantChanged += (_, _) => { ClearLabels(); InvalidateVisual(); };
    }

    public event EventHandler? NodeActivated;
    public event EventHandler? EditRequested;
    internal Task LayoutWork { get; private set; } = Task.CompletedTask;
    internal int LoadedCards => cards.Length;
    internal int DrawnCards { get; private set; }
    internal double ZoomLevel => scale;
    internal bool SelectedCardVisible => cards.Any(card => ReferenceEquals(card.Entry.Node, tree.SelectedNode) && Viewport.Contains(card.Bounds));

    private void OnProjectionChanged(object? sender, EventArgs e) => Refresh();
    private void OnSelectionChanged(object? sender, EventArgs e) { RevealSelection(); InvalidateVisual(); }

    internal void Refresh()
    {
        if (disposed || !IsVisible) return;
        layoutRequest?.Cancel(); layoutRequest?.Dispose();
        var request = layoutRequest = new CancellationTokenSource();
        var token = request.Token;
        long currentVersion = ++version;
        var visible = tree.VisibleNodes;
        var positions = new Dictionary<StructureTreeNode, int>();
        var entries = new Entry[visible.Count];
        for (int i = 0; i < visible.Count; i++)
        {
            var node = visible[i];
            positions[node] = i;
            var parent = node.Parent;
            while (parent is { HeaderHidden: true }) parent = parent.Parent;
            entries[i] = new(node, node.BindingVersion, parent is not null && positions.TryGetValue(parent, out int parentIndex) ? parentIndex : -1,
                node.DisplayKey, node.DisplayValue);
        }
        LayoutWork = Task.Run(() =>
        {
            try
            {
                var next = ArrangeCards(entries, token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (disposed || token.IsCancellationRequested || currentVersion != version) return;
                    var selected = tree.SelectedNode;
                    var oldAnchor = cards.FirstOrDefault(card => ReferenceEquals(card.Entry.Node, selected));
                    var newAnchor = next.FirstOrDefault(card => ReferenceEquals(card.Entry.Node, selected));
                    if (cameraSet && oldAnchor is not null && newAnchor is not null)
                        pan += (oldAnchor.Bounds.Center - newAnchor.Bounds.Center) * scale;
                    cards = next; ClearLabels();
                    if (!cameraSet) ResetCamera();
                    RevealSelection();
                    InvalidateVisual();
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private static GraphCard[] ArrangeCards(Entry[] entries, CancellationToken token)
    {
        var result = new GraphCard[entries.Length];
        var children = new List<int>[entries.Length];
        for (int i = 0; i < children.Length; i++) children[i] = [];
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Parent >= 0) children[entries[i].Parent].Add(i);
        double row = 0;
        void Place(int index, int depth)
        {
            token.ThrowIfCancellationRequested();
            double y;
            if (children[index].Count == 0) { y = row * (CardHeight + RowGap); row++; }
            else
            {
                foreach (int child in children[index]) Place(child, depth + 1);
                y = (result[children[index][0]].Bounds.Y + result[children[index][^1]].Bounds.Y) / 2;
            }
            result[index] = new(entries[index], new Rect(depth * (CardWidth + ColumnGap), y, CardWidth, CardHeight));
        }
        for (int i = 0; i < entries.Length; i++)
            if (entries[i].Parent < 0) Place(i, 0);
        return result;
    }

    internal void Zoom(double factor)
    {
        var viewport = Viewport;
        var anchor = cards.FirstOrDefault(card => ReferenceEquals(card.Entry.Node, tree.SelectedNode) && card.Bounds.Intersects(viewport))
            ?? cards.FirstOrDefault(card => card.Bounds.Intersects(viewport));
        if (anchor is null && cards.Length > 0)
        {
            ResetCamera(); viewport = Viewport;
            anchor = cards.FirstOrDefault(card => card.Bounds.Intersects(viewport));
        }
        var point = anchor is null ? new Point(Bounds.Width / 2, Bounds.Height / 2)
            : new Point(anchor.Bounds.Center.X * scale + pan.X, anchor.Bounds.Center.Y * scale + pan.Y);
        ZoomAt(factor, point);
    }

    private void ZoomAt(double factor, Point anchor)
    {
        double next = Math.Clamp(scale * factor, 0.15, 2.5);
        var world = World(anchor);
        pan = new Vector(anchor.X - world.X * next, anchor.Y - world.Y * next);
        scale = next; cameraSet = true; InvalidateVisual();
    }

    internal void Fit()
    {
        if (cards.Length == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double right = cards.Max(card => card.Bounds.Right), bottom = cards.Max(card => card.Bounds.Bottom);
        scale = Math.Clamp(Math.Min((Bounds.Width - 48) / Math.Max(1, right), (Bounds.Height - 48) / Math.Max(1, bottom)), 0.15, 1);
        pan = new Vector(24, Math.Max(24, (Bounds.Height - bottom * scale) / 2));
        cameraSet = true; InvalidateVisual();
    }

    private void ResetCamera()
    {
        if (cards.Length == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        scale = 1; pan = new Vector(24, 24); cameraSet = true;
        var anchor = cards.FirstOrDefault(card => ReferenceEquals(card.Entry.Node, tree.SelectedNode)) ?? cards[0];
        RevealCard(anchor);
        InvalidateVisual();
    }

    private Point World(Point point) => new((point.X - pan.X) / scale, (point.Y - pan.Y) / scale);
    private Rect Viewport => new(World(default), new Size(Bounds.Width / scale, Bounds.Height / scale));
    private GraphCard? Hit(Point point) => cards.LastOrDefault(card => card.Bounds.Contains(World(point)));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var background = BackgroundBrush;
        var foreground = TextElement.GetForeground(this) ?? Brushes.Black;
        var outline = new Pen(new SolidColorBrush(ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#686868") : Color.Parse("#B7BDC5")), 1);
        context.FillRectangle(background, new Rect(Bounds.Size));
        var viewport = Viewport;
        DrawnCards = 0;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(new Matrix(scale, 0, 0, scale, pan.X, pan.Y)))
        {
            foreach (var card in cards)
            {
                if (card.Entry.Parent < 0) continue;
                var parent = cards[card.Entry.Parent];
                var start = new Point(parent.Bounds.Right, parent.Bounds.Center.Y);
                var end = new Point(card.Bounds.Left, card.Bounds.Center.Y);
                if (!new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)).Inflate(2).Intersects(viewport)) continue;
                double bend = (start.X + end.X) / 2;
                context.DrawLine(outline, start, new Point(bend, start.Y));
                context.DrawLine(outline, new Point(bend, start.Y), new Point(bend, end.Y));
                context.DrawLine(outline, new Point(bend, end.Y), end);
            }
            foreach (var card in cards)
            {
                if (!card.Bounds.Intersects(viewport)) continue;
                DrawnCards++;
                bool selected = ReferenceEquals(tree.SelectedNode, card.Entry.Node);
                context.DrawRectangle(selected ? new SolidColorBrush(Color.Parse("#3080A8FF")) : background,
                    new Pen(selected ? foreground : outline.Brush, selected ? 1.5 : 1), card.Bounds, 6, 6);
                if (card.Entry.Node.Kind == StructureTreeNodeKind.Pager) { DrawPager(context, card, foreground); continue; }
                var text = Label(card, foreground);
                text.Key.Draw(context, card.Bounds.TopLeft + new Vector(12, text.Value is null ? 18 : 9));
                text.Value?.Draw(context, card.Bounds.TopLeft + new Vector(12, 31));
                if (card.Entry.Node.HasChildren)
                {
                    var centre = new Point(card.Bounds.Right - 15, card.Bounds.Top + 16);
                    context.DrawLine(new Pen(foreground, 1.5), centre - new Vector(4, 0), centre + new Vector(4, 0));
                    if (!card.Entry.Node.IsExpanded) context.DrawLine(new Pen(foreground, 1.5), centre - new Vector(0, 4), centre + new Vector(0, 4));
                }
            }
        }
    }

    private IBrush BackgroundBrush => this.TryFindResource("SystemControlBackgroundAltHighBrush", out var brush) && brush is IBrush value
        ? value : ActualThemeVariant == ThemeVariant.Dark ? new SolidColorBrush(Color.Parse("#202020")) : Brushes.White;

    private (TextLayout Key, TextLayout? Value) Label(GraphCard card, IBrush foreground)
    {
        if (labels.TryGetValue(card.Entry.Node, out var cached)) return cached;
        string key = card.Entry.Key.Length > 0 ? card.Entry.Key : card.Entry.Value;
        var value = card.Entry.Key.Length > 0 && card.Entry.Value.Length > 0 ? card.Entry.Value : "";
        var typeface = new Typeface(TextElement.GetFontFamily(this));
        var result = (new TextLayout(key, typeface, 13, foreground, maxWidth: CardWidth - 40, textTrimming: TextTrimming.CharacterEllipsis),
            value.Length == 0 ? null : new TextLayout(value, typeface, 12, foreground, maxWidth: CardWidth - 24, textTrimming: TextTrimming.CharacterEllipsis));
        labels.Add(card.Entry.Node, result);
        return result;
    }

    private void DrawPager(DrawingContext context, GraphCard card, IBrush foreground)
    {
        using var text = new TextLayout(Loc.Get(K.StructurePrevious) + "    /    " + Loc.Get(K.StructureNext),
            new Typeface(TextElement.GetFontFamily(this)), 12, foreground, maxWidth: CardWidth - 16, textTrimming: TextTrimming.CharacterEllipsis);
        text.Draw(context, card.Bounds.TopLeft + new Vector(8, 20));
    }

    private void ClearLabels()
    {
        foreach (var label in labels.Values) { label.Key.Dispose(); label.Value?.Dispose(); }
        labels.Clear();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && Hit(e.GetPosition(this)) is { } target
            && target.Entry.Node.Kind == StructureTreeNodeKind.Normal && tree.IsCurrent(target.Entry.Node, target.Entry.Version))
            tree.SelectedNode = target.Entry.Node;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); pressed = lastPoint = e.GetPosition(this); dragged = false;
        e.Pointer.Capture(this); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressed is not { } origin || lastPoint is not { } last) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - origin.X) + Math.Abs(current.Y - origin.Y) > 3) dragged = true;
        if (dragged) { pan += current - last; cameraSet = true; InvalidateVisual(); }
        lastPoint = current; e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressed is null) return;
        var point = e.GetPosition(this);
        pressed = lastPoint = null; e.Pointer.Capture(null);
        if (!dragged && Hit(point) is { } card && tree.IsCurrent(card.Entry.Node, card.Entry.Version))
        {
            var node = card.Entry.Node;
            if (node.Kind == StructureTreeNodeKind.Pager && node.PagerContent is Panel pager)
            {
                var buttons = pager.Children.OfType<Button>().ToArray();
                int index = World(point).X < card.Bounds.Center.X ? 0 : 1;
                if (index < buttons.Length && buttons[index].IsEnabled) buttons[index].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            }
            else if (node.Kind == StructureTreeNodeKind.Normal)
            {
                tree.SelectedNode = node;
                if (node.HasChildren && World(point).X >= card.Bounds.Right - 30) node.IsExpanded = !node.IsExpanded;
            }
        }
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        pressed = lastPoint = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) ZoomAt(Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
        else { pan += new Vector(e.Delta.X * 40, e.Delta.Y * 40); cameraSet = true; InvalidateVisual(); }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.PageUp or Key.PageDown)
        {
            var pager = tree.VisibleNodes.LastOrDefault(node => node.Kind == StructureTreeNodeKind.Pager)?.PagerContent as Panel;
            var button = pager?.Children.OfType<Button>().ElementAtOrDefault(e.Key == Key.PageUp ? 0 : 1);
            if (button is { IsEnabled: true }) button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            e.Handled = true; return;
        }
        if (e.Key == Key.Home) { ResetCamera(); e.Handled = true; return; }
        if (e.Key is Key.Add or Key.OemPlus) { Zoom(1.2); e.Handled = true; return; }
        if (e.Key is Key.Subtract or Key.OemMinus) { Zoom(1 / 1.2); e.Handled = true; return; }
        var selected = tree.SelectedNode;
        if (e.Key == Key.Enter) { NodeActivated?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        if (e.Key == Key.F2) { EditRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        if (e.Key == Key.Right && selected is { HasChildren: true, IsExpanded: false }) selected.IsExpanded = true;
        else if (e.Key == Key.Left && selected is { IsExpanded: true }) selected.IsExpanded = false;
        else if (e.Key == Key.Left && selected?.Parent is { HeaderHidden: false } parent) tree.SelectedNode = parent;
        else if (e.Key == Key.Right && selected is { IsExpanded: true } branch && branch.Items.FirstOrDefault(node => node.Kind == StructureTreeNodeKind.Normal) is { } child) tree.SelectedNode = child;
        else if (e.Key is Key.Up or Key.Down)
        {
            var nodes = tree.VisibleNodes.Where(node => node.Kind == StructureTreeNodeKind.Normal).ToArray();
            if (nodes.Length == 0) return;
            int position = Array.IndexOf(nodes, selected);
            int next = Math.Clamp(position + (e.Key == Key.Up ? -1 : 1), 0, nodes.Length - 1);
            if (nodes.Length > 0) tree.SelectedNode = nodes[next];
        }
        else return;
        RevealSelection(); e.Handled = true;
    }

    private void RevealSelection()
    {
        if (cards.FirstOrDefault(card => ReferenceEquals(card.Entry.Node, tree.SelectedNode)) is not { } card) return;
        RevealCard(card);
    }

    private void RevealCard(GraphCard card)
    {
        var viewport = Viewport;
        if (viewport.Contains(card.Bounds)) return;
        double x = card.Bounds.Left < viewport.Left || card.Bounds.Right > viewport.Right
            ? Bounds.Width / 2 - card.Bounds.Center.X * scale : pan.X;
        double y = card.Bounds.Top < viewport.Top || card.Bounds.Bottom > viewport.Bottom
            ? Bounds.Height / 2 - card.Bounds.Center.Y * scale : pan.Y;
        pan = new Vector(x, y);
        cameraSet = true; InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && !cameraSet) ResetCamera();
    }

    public void Dispose()
    {
        disposed = true; version++;
        layoutRequest?.Cancel(); layoutRequest?.Dispose();
        tree.ProjectionChanged -= OnProjectionChanged; tree.SelectionChanged -= OnSelectionChanged;
        cards = []; ClearLabels();
    }
}

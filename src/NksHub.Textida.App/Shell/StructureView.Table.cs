using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed record StructureTableColumn(string Name, int Occurrence = 1)
{
    internal string Label => Occurrence > 1 ? Name + " [" + Occurrence + "]" : Name;
}
internal sealed record StructureTableCell(StructureNode Node, string Name, string Value, ByteRange? ResultRange = null);
internal sealed record StructureTableRow(string Name, StructureNode Node, IReadOnlyDictionary<StructureTableColumn, StructureTableCell> Cells);
internal sealed record StructureTablePage(IReadOnlyList<StructureTableColumn> Columns, IReadOnlyList<StructureTableRow> Rows,
    int Page, int FieldPage, bool MoreRows, bool MoreFields);

internal sealed class StructureTable : Control
{
    private const double RowHeight = 30, NameWidth = 160, CellWidth = 190, FooterHeight = 34;
    private StructureTablePage page = new([], [], 0, 0, false, false);
    private readonly Dictionary<string, TextLayout> labels = new(StringComparer.Ordinal);
    private Vector offset;
    private Point? lastPoint;
    private bool dragging;
    private int selectedRow = -1, selectedColumn;

    public StructureTable()
    {
        Focusable = true; ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => { ClearLabels(); InvalidateVisual(); };
    }

    public event Action<StructureTableCell>? CellSelected;
    public event Action<StructureTableCell>? EditRequested;
    public event Action<int, int>? PageRequested;
    internal int LoadedRows => page.Rows.Count;
    internal int LoadedCells => page.Rows.Sum(row => row.Cells.Count);
    internal int DrawnCells { get; private set; }
    internal StructureTablePage CurrentPage => page;

    internal void SetPage(StructureTablePage value)
    {
        page = value; offset = default; selectedRow = -1; selectedColumn = 0;
        ClearLabels(); InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        IBrush background = dark ? new SolidColorBrush(Color.Parse("#202020")) : Brushes.White;
        var foreground = TextElement.GetForeground(this) ?? (dark ? Brushes.White : Brushes.Black);
        var line = new Pen(new SolidColorBrush(dark ? Color.Parse("#505050") : Color.Parse("#D8DCE2")), 1);
        context.FillRectangle(background, new Rect(Bounds.Size));
        double bottom = Math.Max(RowHeight, Bounds.Height - FooterHeight);
        int firstRow = Math.Max(0, (int)(offset.Y / RowHeight));
        int lastRow = Math.Min(page.Rows.Count, firstRow + (int)Math.Ceiling(bottom / RowHeight));
        int firstColumn = Math.Max(0, (int)(offset.X / CellWidth));
        int lastColumn = Math.Min(page.Columns.Count, firstColumn + (int)Math.Ceiling(Math.Max(0, Bounds.Width - NameWidth) / CellWidth) + 1);
        DrawnCells = 0;
        using (context.PushClip(new Rect(0, RowHeight, Bounds.Width, Math.Max(0, bottom - RowHeight))))
        {
            for (int row = firstRow; row < lastRow; row++)
            {
                double y = RowHeight + row * RowHeight - offset.Y;
                if (row == selectedRow) context.FillRectangle(new SolidColorBrush(Color.Parse("#3080A8FF")), new Rect(0, y, Bounds.Width, RowHeight));
                DrawText(context, page.Rows[row].Name, new Rect(8, y + 6, NameWidth - 16, RowHeight - 8), foreground);
                using (context.PushClip(new Rect(NameWidth, y, Math.Max(0, Bounds.Width - NameWidth), RowHeight)))
                for (int column = firstColumn; column < lastColumn; column++)
                {
                    double x = NameWidth + column * CellWidth - offset.X;
                    if (page.Rows[row].Cells.TryGetValue(page.Columns[column], out var cell))
                    {
                        DrawText(context, cell.Value, new Rect(x + 8, y + 6, CellWidth - 16, RowHeight - 8), foreground);
                        DrawnCells++;
                    }
                    context.DrawLine(line, new Point(x, y), new Point(x, y + RowHeight));
                }
                context.DrawLine(line, new Point(0, y + RowHeight), new Point(Bounds.Width, y + RowHeight));
            }
        }
        context.FillRectangle(background, new Rect(0, 0, Bounds.Width, RowHeight));
        DrawText(context, Loc.Get(K.StructureTableKey), new Rect(8, 6, NameWidth - 16, 22), foreground);
        using (context.PushClip(new Rect(NameWidth, 0, Math.Max(0, Bounds.Width - NameWidth), RowHeight)))
            for (int column = firstColumn; column < lastColumn; column++)
                DrawText(context, page.Columns[column].Label, new Rect(NameWidth + column * CellWidth - offset.X + 8, 6, CellWidth - 16, 22), foreground);
        context.DrawLine(line, new Point(0, RowHeight), new Point(Bounds.Width, RowHeight));
        string[] footer = [Loc.Get(K.StructurePrevious), Loc.Get(K.StructureNext), Loc.Get(K.StructurePreviousFields), Loc.Get(K.StructureNextFields)];
        for (int i = 0; i < footer.Length; i++)
        {
            var bounds = FooterButton(i);
            context.DrawRectangle(background, line, bounds);
            DrawText(context, footer[i], bounds.Deflate(6), foreground);
        }
    }

    private Rect FooterButton(int index)
    {
        double width = Bounds.Width / 4;
        return new Rect(index * width, Math.Max(0, Bounds.Height - FooterHeight), width, FooterHeight);
    }

    private void DrawText(DrawingContext context, string text, Rect bounds, IBrush brush)
    {
        string key = bounds.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + text;
        if (!labels.TryGetValue(key, out var layout))
        {
            layout = new TextLayout(text, new Typeface(TextElement.GetFontFamily(this)), 13, brush,
                maxWidth: Math.Max(1, bounds.Width), textTrimming: TextTrimming.CharacterEllipsis);
            if (labels.Count >= StructureView.MaximumItems) ClearLabels();
            labels.Add(key, layout);
        }
        using (context.PushClip(bounds)) layout.Draw(context, bounds.TopLeft);
    }

    private StructureTableCell? SelectedCell() => selectedRow >= 0 && selectedRow < page.Rows.Count
        && selectedColumn >= 0 && selectedColumn < page.Columns.Count
        ? page.Rows[selectedRow].Cells.GetValueOrDefault(page.Columns[selectedColumn]) : null;

    private void Select(int row, int column, bool edit = false)
    {
        if (page.Rows.Count == 0 || page.Columns.Count == 0) return;
        selectedRow = Math.Clamp(row, 0, page.Rows.Count - 1); selectedColumn = Math.Clamp(column, 0, page.Columns.Count - 1);
        if (SelectedCell() is { } cell) { if (edit) EditRequested?.Invoke(cell); else CellSelected?.Invoke(cell); }
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        var point = e.GetPosition(this);
        if (properties.IsRightButtonPressed)
        {
            if (point.Y >= RowHeight && point.Y < Bounds.Height - FooterHeight)
                Select((int)((point.Y - RowHeight + offset.Y) / RowHeight), Math.Max(0, (int)((point.X - NameWidth + offset.X) / CellWidth)));
            return;
        }
        if (!properties.IsLeftButtonPressed) return;
        Focus();
        if (point.Y >= Bounds.Height - FooterHeight)
        {
            int button = Math.Clamp((int)(point.X / Math.Max(1, Bounds.Width / 4)), 0, 3);
            if (button == 0 && page.Page > 0) PageRequested?.Invoke(page.Page - 1, page.FieldPage);
            if (button == 1 && page.MoreRows) PageRequested?.Invoke(page.Page + 1, page.FieldPage);
            if (button == 2 && page.FieldPage > 0) PageRequested?.Invoke(page.Page, page.FieldPage - 1);
            if (button == 3 && page.MoreFields) PageRequested?.Invoke(page.Page, page.FieldPage + 1);
        }
        else if (point.Y >= RowHeight)
        {
            Select((int)((point.Y - RowHeight + offset.Y) / RowHeight), Math.Max(0, (int)((point.X - NameWidth + offset.X) / CellWidth)), e.ClickCount == 2);
            lastPoint = point; dragging = e.Pointer.Type == PointerType.Touch;
            if (dragging) e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || lastPoint is not { } last) return;
        var point = e.GetPosition(this); offset -= point - last; lastPoint = point; ClampOffset();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); dragging = false; lastPoint = null; e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        dragging = false; lastPoint = null;
        base.OnPointerCaptureLost(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        offset -= e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y * 50, 0) : new Vector(e.Delta.X * 50, e.Delta.Y * RowHeight * 3);
        ClampOffset(); e.Handled = true;
    }

    private void ClampOffset()
    {
        offset = new Vector(Math.Clamp(offset.X, 0, Math.Max(0, page.Columns.Count * CellWidth + NameWidth - Bounds.Width)),
            Math.Clamp(offset.Y, 0, Math.Max(0, page.Rows.Count * RowHeight + RowHeight + FooterHeight - Bounds.Height)));
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Right && page.MoreFields) PageRequested?.Invoke(page.Page, page.FieldPage + 1);
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Left && page.FieldPage > 0) PageRequested?.Invoke(page.Page, page.FieldPage - 1);
        else if (e.Key == Key.Home) Select(0, 0);
        else if (e.Key == Key.End) Select(page.Rows.Count - 1, page.Columns.Count - 1);
        else if (e.Key == Key.PageDown && page.MoreRows) PageRequested?.Invoke(page.Page + 1, page.FieldPage);
        else if (e.Key == Key.PageUp && page.Page > 0) PageRequested?.Invoke(page.Page - 1, page.FieldPage);
        else if (e.Key == Key.Down) Select(selectedRow + 1, selectedColumn);
        else if (e.Key == Key.Up) Select(selectedRow - 1, selectedColumn);
        else if (e.Key == Key.Right) Select(selectedRow, selectedColumn + 1);
        else if (e.Key == Key.Left) Select(selectedRow, selectedColumn - 1);
        else if (e.Key is Key.Enter or Key.F2) { if (SelectedCell() is { } cell) EditRequested?.Invoke(cell); }
        else return;
        if (selectedRow >= 0)
        {
            offset = new Vector(Math.Max(0, selectedColumn * CellWidth - Math.Max(0, Bounds.Width - NameWidth - CellWidth)),
                Math.Max(0, selectedRow * RowHeight - Math.Max(0, Bounds.Height - FooterHeight - 2 * RowHeight)));
            ClampOffset();
        }
        e.Handled = true;
    }

    private void ClearLabels() { foreach (var label in labels.Values) label.Dispose(); labels.Clear(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearLabels(); base.OnDetachedFromVisualTree(e);
    }
}

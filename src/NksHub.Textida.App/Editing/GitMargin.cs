using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Editing;

internal enum GitMarkKind { Added, Modified, Removed }

/// <summary>
/// A change of the open file for the gutter: <paramref name="Lines"/> lines from <paramref name="Line"/> (0 based) that differ from the Git index, or, when <paramref name="Staged"/>, that differ from
/// <c>HEAD</c> but not from the index. A removed change has no lines and stands at the top of line <paramref name="Line"/>.
/// </summary>
internal sealed record GitMark(int Line, int Lines, GitMarkKind Kind, bool Staged);

/// <summary>The marks Git gave for a document as it was at <paramref name="Revision"/>, in file order.</summary>
internal sealed record GitMarks(long Revision, IReadOnlyList<GitMark> Marks);

/// <summary>
/// Left margin of the text editor that marks the lines Git sees as changed: a filled bar for a change that is not staged, an outlined one for a change that is. A click on a mark is reported;
/// the window stages the change, or takes it back from the index when it is staged.
/// </summary>
internal sealed class GitMargin : AbstractMargin
{
    public const double MarginWidth = 7;
    private static readonly Cursor Hand = new(StandardCursorType.Hand);
    private IReadOnlyList<GitMark> _marks = [];

    public event Action<GitMark>? Clicked;

    public void SetMarks(IReadOnlyList<GitMark>? marks)
    {
        _marks = marks ?? [];
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>The view is closed: stop listening so a closed split view does not stay alive with the document.</summary>
    public void Release()
    {
        if (TextView is { } view) view.VisualLinesChanged -= OnVisualLinesChanged;
    }

    /// <summary>The mark that covers a line (0 based); a change that is not staged wins over one that is.</summary>
    internal static GitMark? MarkAt(IReadOnlyList<GitMark> marks, int line)
    {
        GitMark? staged = null;
        foreach (var m in marks)
        {
            if (m.Line > line) break;
            if (m.Lines > 0 ? line >= m.Line + m.Lines : m.Line != line) continue;
            if (!m.Staged) return m;
            staged ??= m;
        }
        return staged;
    }

    internal static string TipOf(GitMark mark)
    {
        var kind = Loc.Get(mark.Kind switch { GitMarkKind.Added => K.GitGutterAdded, GitMarkKind.Removed => K.GitGutterRemoved, _ => K.GitGutterModified });
        return Loc.Get(mark.Staged ? K.GitGutterTipUnstage : K.GitGutterTipStage, ("kind", kind));
    }

    private GitMark? MarkAtY(double y)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid || _marks.Count == 0) return null;
        foreach (var line in view.VisualLines)
        {
            double top = line.VisualTop - view.VerticalOffset;
            if (y >= top && y < top + line.Height) return MarkAt(_marks, line.FirstDocumentLine.LineNumber - 1);
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || MarkAtY(e.GetPosition(this).Y) is not { } mark) return;
        e.Handled = true;
        Clicked?.Invoke(mark);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var mark = MarkAtY(e.GetPosition(this).Y);
        Cursor = mark is null ? null : Hand;
        ToolTip.SetTip(this, mark is null ? null : TipOf(mark));
    }

    private IBrush BrushOf(GitMarkKind kind) => kind switch
    {
        GitMarkKind.Added => Brushes.SeaGreen,
        GitMarkKind.Removed => this.TryFindResource("Danger", ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Firebrick,
        _ => Brushes.SteelBlue,
    };

    public override void Render(DrawingContext context)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid || _marks.Count == 0) return;
        foreach (var line in view.VisualLines)
        {
            if (MarkAt(_marks, line.FirstDocumentLine.LineNumber - 1) is not { } mark) continue;
            double top = line.VisualTop - view.VerticalOffset;
            var brush = BrushOf(mark.Kind);
            // a removal has no line of its own: a short bar on the edge between the two lines
            var bar = mark.Lines == 0 ? new Rect(1, top - 1, MarginWidth - 2, 3) : new Rect(1, top, MarginWidth - 3, line.Height);
            if (mark.Staged) context.DrawRectangle(null, new Pen(brush, 1), bar.Deflate(0.5));
            else context.FillRectangle(brush, bar);
        }
    }
}

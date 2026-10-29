using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Editing;

internal enum GitMarkKind { Added, Modified, Removed }

/// <summary>What the changes of a file are shown against: the Git index, or the last commit.</summary>
internal enum GitBase { Index, Head }

/// <summary>
/// A change of the open file for the gutter: <paramref name="Lines"/> lines from <paramref name="Line"/> (0 based) that differ from the Git index, or, when <paramref name="Staged"/>, that differ from
/// <c>HEAD</c> but not from the index. A removed change has no lines and stands at the top of line <paramref name="Line"/>. <paramref name="OldLines"/> lines from <paramref name="OldStart"/> of
/// <paramref name="Base"/> are what the change replaced.
/// </summary>
internal sealed record GitMark(int Line, int Lines, GitMarkKind Kind, bool Staged, int OldStart = 0, int OldLines = 0, GitBase Base = GitBase.Index);

/// <summary>The marks Git gave for a document as it was at <paramref name="Revision"/>, in file order.</summary>
internal sealed record GitMarks(long Revision, IReadOnlyList<GitMark> Marks);

/// <summary>The lines <paramref name="Mark"/> replaced, as of <paramref name="Revision"/> of the document: the old text of the change, for the peek and for taking the change back.</summary>
/// <param name="EndsWithNewline">The last old line ends with a line break in the file it comes from.</param>
/// <param name="Truncated">Only the first lines of the change are in <paramref name="Old"/>; it cannot be reverted from them.</param>
internal sealed record GitPeek(long Revision, GitMark Mark, IReadOnlyList<string> Old, bool EndsWithNewline, bool Truncated);

/// <summary>The commit that last changed a line, as Git blame names it.</summary>
internal sealed record GitBlame(long Revision, int Line, string Sha, string Author, DateTimeOffset Time, string Summary, bool Uncommitted);

/// <summary>
/// Left margin of the text editor that marks the lines Git sees as changed: a filled bar for a change that is not staged, an outlined one for a change that is, and a triangle where lines were
/// removed; a changed (not added) bar has a gap down its middle, so the kinds differ by shape in the high contrast theme, where every colour is the same. A click on a mark is reported;
/// the window opens the change under the mark.
/// </summary>
internal sealed class GitMargin : AbstractMargin
{
    public const double MarginWidth = 8;
    private static readonly Cursor Hand = new(StandardCursorType.Hand);
    private IReadOnlyList<GitMark> _marks = [];

    public GitMargin() => UpdateName();

    /// <summary>Sets the screen reader name from the current UI language.</summary>
    public void UpdateName() => AutomationProperties.SetName(this, Loc.Get(K.A11yGitGutter));

    public event Action<GitMark>? Clicked;

    /// <summary>The marks shown now, in file order.</summary>
    public IReadOnlyList<GitMark> Marks => _marks;

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

    /// <summary>
    /// The change to go to from the line <paramref name="caretLine"/>: the first change that starts after the change the caret is in (after the caret when it is in none), or, backwards, the last that
    /// starts before it. After the last change comes the first and before the first the last. Null without marks. Where a staged and a change that is not staged start at one line, the latter.
    /// </summary>
    internal static GitMark? StepFrom(IReadOnlyList<GitMark> marks, int caretLine, bool forward) =>
        MarkAt(marks, caretLine) is { } current ? StepFrom(marks, current, forward) : Step(marks, caretLine, caretLine, forward);

    /// <summary>The change after (or before) <paramref name="from"/>, see <see cref="StepFrom(IReadOnlyList{GitMark}, int, bool)"/>.</summary>
    internal static GitMark? StepFrom(IReadOnlyList<GitMark> marks, GitMark from, bool forward) =>
        Step(marks, from.Line, from.Line + Math.Max(0, from.Lines - 1), forward);

    private static GitMark? Step(IReadOnlyList<GitMark> marks, int firstLine, int lastLine, bool forward)
    {
        if (marks.Count == 0) return null;
        GitMark? pick = forward ? marks.FirstOrDefault(m => m.Line > lastLine) : marks.LastOrDefault(m => m.Line < firstLine);
        pick ??= forward ? marks[0] : marks[^1];
        return marks.Where(m => m.Line == pick.Line).OrderBy(m => m.Staged).First();
    }

    /// <summary>What a change is, in words: its kind and size, whether it is staged and what it is shown against. A screen reader reads it, and the tooltip of a mark starts with it.</summary>
    internal static string DescribeOf(GitMark mark)
    {
        string size = mark.Kind switch
        {
            GitMarkKind.Added => Loc.Plural(K.GitGutterSizeAdded, mark.Lines),
            GitMarkKind.Removed => Loc.Plural(K.GitGutterSizeRemoved, mark.OldLines),
            _ => Loc.Plural(K.GitGutterSizeModified, mark.Lines, ("old", mark.OldLines)),
        };
        return Loc.Get(K.GitGutterDescribe, ("size", size), ("state", Loc.Get(mark.Staged ? K.GitGutterStaged : K.GitGutterUnstaged)),
            ("base", Loc.Get(mark.Base == GitBase.Head ? K.GitGutterBaseHead : K.GitGutterBaseIndex)));
    }

    internal static string TipOf(GitMark mark) => DescribeOf(mark) + "\n" + Loc.Get(K.GitGutterTipPeek);

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

    /// <summary>The colour of a kind of change: the gutter tokens of the theme (the one colour of the system in high contrast).</summary>
    private IBrush BrushOf(GitMarkKind kind)
    {
        (string key, IBrush fallback) = kind switch
        {
            GitMarkKind.Added => ("GitGutterAdded", Brushes.SeaGreen),
            GitMarkKind.Removed => ("GitGutterRemoved", Brushes.Firebrick),
            _ => ("GitGutterModified", Brushes.SteelBlue),
        };
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
    }

    public override void Render(DrawingContext context)
    {
        var view = TextView;
        if (view is null || !view.VisualLinesValid || _marks.Count == 0) return;
        var gap = this.TryFindResource("BgEditor", ActualThemeVariant, out var editor) && editor is IBrush back ? back : Brushes.White;
        foreach (var line in view.VisualLines)
        {
            if (MarkAt(_marks, line.FirstDocumentLine.LineNumber - 1) is not { } mark) continue;
            double top = line.VisualTop - view.VerticalOffset;
            var brush = BrushOf(mark.Kind);
            if (mark.Lines == 0)
            {
                // a removal has no line of its own: a triangle on the edge between the two lines
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    g.BeginFigure(new Point(1, top - 4), true);
                    g.LineTo(new Point(MarginWidth - 1, top));
                    g.LineTo(new Point(1, top + 4));
                    g.EndFigure(true);
                }
                if (mark.Staged) context.DrawGeometry(null, new Pen(brush, 1), geometry);
                else context.DrawGeometry(brush, null, geometry);
                continue;
            }
            var bar = new Rect(1, top, MarginWidth - 3, line.Height);
            if (mark.Staged) context.DrawRectangle(null, new Pen(brush, 1), bar.Deflate(0.5));
            else context.FillRectangle(brush, bar);
            if (mark.Kind == GitMarkKind.Modified) context.FillRectangle(gap, new Rect(bar.X + Math.Floor((bar.Width - 1) / 2), top, 1, line.Height));
        }
    }
}

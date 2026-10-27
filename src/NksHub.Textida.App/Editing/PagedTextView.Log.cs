using Avalonia;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Full log mode on the paged surface (P9.4.2). It shows ANSI colors with the escapes hidden, and opens the file of a stack trace frame. Folding runs of lines
/// behind a placeholder row needs the line index to map rows to lines, which a file of this size cannot hold, so here the lines the fold would hide are dimmed instead.
/// </summary>
internal sealed partial class PagedTextView
{
    private bool _ansiRaw;

    /// <summary>Raised when the user Ctrl+clicks a stack trace frame of a log.</summary>
    public event Action<StackTraceLink>? StackTraceClicked;

    /// <summary>Shows the escape sequences as they are in the file instead of their colors.</summary>
    public bool AnsiRaw
    {
        get => _ansiRaw;
        set
        {
            _ansiRaw = value;
            RepaintLog();
        }
    }

    private bool HideAnsi => _doc.IsLog && !_ansiRaw;

    /// <summary>Lines that this criteria would fold are drawn dim; null draws every line as usual.</summary>
    public LogFoldCriteria? FoldCriteria { get; private set; }

    public void SetFold(LogFoldCriteria? criteria)
    {
        FoldCriteria = criteria is { IsEmpty: false } ? criteria : null;
        _canvas.InvalidateVisual();
    }

    private StackTraceLink? StackTraceIn(LineView view, int index) =>
        _doc.IsLog && !view.Truncated ? StackTraceLinks.At(view.Map.Text, index) : null;

    /// <summary>The stack trace frame under a point of the text area, or null.</summary>
    private StackTraceLink? StackTraceAt(Point point)
    {
        long line = TopLine + (long)Math.Floor(point.Y / _canvas.LineHeight);
        if (line < 0 || line >= LineCount || GetLine(line) is not { } view) return null;
        return StackTraceIn(view, _canvas.IndexAtX(view, point.X - _canvas.TextLeft + _hScroll));
    }

    /// <summary>The stack trace frame under the caret, or null.</summary>
    public StackTraceLink? StackTraceAtCaret() =>
        CaretLine() is { } c ? StackTraceIn(c.View, c.View.Map.CharIndexOf(_caret - c.View.Start)) : null;
}

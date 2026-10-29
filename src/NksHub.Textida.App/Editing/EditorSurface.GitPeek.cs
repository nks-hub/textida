using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The peek at a change of the Git gutter. The marks are kept here (the margin draws them, the minimap and the strip beside the scroll bar repeat them); the peek lies over the editor, in the overlay
/// layer of the window, right under the lines of the change, follows the scrolling and goes away when the text is edited, the tab is left or Esc is pressed. The caret is never moved by it.
/// </summary>
internal sealed partial class EditorSurface
{
    private GitPeekView? _peek;
    private OverlayLayer? _peekLayer;

    /// <summary>The changes Git reports for this file, in file order; empty when there are none.</summary>
    public IReadOnlyList<GitMark> GitMarks => _gitMargin.Marks;

    /// <summary>Raised when the marks of the gutter were replaced.</summary>
    public event Action? GitMarksChanged;

    /// <summary>The peek that is open now, or null.</summary>
    internal GitPeekView? PeekView => _peek;

    /// <summary>The gutter was told again what Git says: the peek of an older answer is not true any more.</summary>
    private void OnGitMarksSet()
    {
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
        GitMarksChanged?.Invoke();
        if (_peek is { } peek && !GitMarks.Contains(peek.Data.Mark)) CloseGitPeek();
    }

    private void InitGitPeek()
    {
        TextArea.TextView.BackgroundRenderers.Add(new GitChangeStripLayer(this));
        Doc.Doc.Changed += (_, _) =>
        {
            if (_peek is null) return;
            if (Dispatcher.UIThread.CheckAccess()) CloseGitPeek();
            else Dispatcher.UIThread.Post(CloseGitPeek);
        };
    }

    /// <summary>Updates the names and texts of the margin and of an open peek after the interface language changed.</summary>
    public void UpdateGitTexts()
    {
        _gitMargin.UpdateName();
        _peek?.UpdateTexts();
    }

    /// <summary>The text of the lines a change stands for, as the editor holds them now; a removal has none.</summary>
    internal IReadOnlyList<string> GitNewLines(GitMark mark)
    {
        var lines = new List<string>(mark.Lines);
        for (int i = 0; i < mark.Lines && mark.Line + i < Document.LineCount; i++)
        {
            var line = Document.GetLineByNumber(mark.Line + i + 1);
            lines.Add(Document.GetText(line.Offset, Math.Min(line.Length, 4000)));
        }
        return lines;
    }

    /// <summary>
    /// Shows the old text of a change under it: the old lines in red above the new ones in green, the words that changed inside a line marked. <paramref name="index"/> of <paramref name="total"/> says
    /// which change of the file this is. With <paramref name="focus"/> the keyboard focus moves to the peek, for the command that opens it from the keyboard.
    /// </summary>
    public void ShowGitPeek(GitPeek data, int index, int total, GitPeekActions actions, bool focus)
    {
        CloseGitPeek();
        if (OverlayLayer.GetOverlayLayer(this) is not { } layer) return;
        _peekLayer = layer;
        _peek = new GitPeekView(data, GitNewLines(data.Mark), FontFamily, FontSize, index, total, actions with { Close = () => { actions.Close(); CloseGitPeek(); } }, canRevert: !IsReadOnly);
        layer.Children.Add(_peek);
        var view = TextArea.TextView;
        view.ScrollOffsetChanged += OnPeekAnchorMoved;
        view.VisualLinesChanged += OnPeekAnchorMoved;
        view.LayoutUpdated += OnPeekAnchorMoved;
        PlaceGitPeek();
        if (focus) Dispatcher.UIThread.Post(() => _peek?.FocusFirst(), DispatcherPriority.Input);
    }

    /// <summary>Takes the peek away and gives the keyboard focus back to the text when it was in the peek.</summary>
    public void CloseGitPeek()
    {
        if (_peek is not { } peek) return;
        _peek = null;
        var view = TextArea.TextView;
        view.ScrollOffsetChanged -= OnPeekAnchorMoved;
        view.VisualLinesChanged -= OnPeekAnchorMoved;
        view.LayoutUpdated -= OnPeekAnchorMoved;
        bool hadFocus = peek.IsKeyboardFocusWithin;
        _peekLayer?.Children.Remove(peek);
        _peekLayer = null;
        if (hadFocus) TextArea.Focus();
    }

    private void OnPeekAnchorMoved(object? sender, EventArgs e) => PlaceGitPeek();

    /// <summary>
    /// Lays the peek under the last line of its change, as wide as the text area. When the line is out of the view the peek is hidden; when there is no room under the line the peek is moved up
    /// as far as it must to fit in the view.
    /// </summary>
    private void PlaceGitPeek()
    {
        if (_peek is not { } peek || _peekLayer is not { } layer) return;
        var view = TextArea.TextView;
        if (!view.VisualLinesValid || view.Bounds.Height <= 0)
        {
            peek.IsVisible = false;
            return;
        }
        var mark = peek.Data.Mark;
        int below = mark.Line + mark.Lines + 1; // the 1-based line under the change
        double y = below <= Document.LineCount
            ? view.GetVisualTopByDocumentLine(below)
            : view.GetVisualTopByDocumentLine(Document.LineCount) + view.DefaultLineHeight;
        double top = y - view.VerticalOffset;
        double width = Math.Max(240, view.Bounds.Width - 24);
        peek.Width = width;
        peek.Measure(new Size(width, double.PositiveInfinity));
        double height = peek.DesiredSize.Height;
        bool anchorVisible = top > -view.DefaultLineHeight && top < view.Bounds.Height;
        peek.IsVisible = anchorVisible;
        if (!anchorVisible) return;
        if (top + height > view.Bounds.Height) top = Math.Max(0, view.Bounds.Height - height);
        if (view.TranslatePoint(new Point(8, top), layer) is not { } at) return;
        if (Canvas.GetLeft(peek) != at.X) Canvas.SetLeft(peek, at.X);
        if (Canvas.GetTop(peek) != at.Y) Canvas.SetTop(peek, at.Y);
    }

    /// <summary>The line break the lines around a change use, for putting back lines of the old text.</summary>
    private string LineBreakNear(int line)
    {
        for (int n = Math.Clamp(line + 1, 1, Document.LineCount); n >= 1; n--)
        {
            var l = Document.GetLineByNumber(n);
            if (l.DelimiterLength > 0) return Document.GetText(l.EndOffset, l.DelimiterLength);
        }
        for (int n = Math.Clamp(line + 1, 1, Document.LineCount); n <= Document.LineCount; n++)
        {
            var l = Document.GetLineByNumber(n);
            if (l.DelimiterLength > 0) return Document.GetText(l.EndOffset, l.DelimiterLength);
        }
        return Doc.NewLine(null);
    }

    /// <summary>
    /// Puts the old lines of a change back in place of its new ones, as one undo step. False, and the text untouched, when the document is read-only, the old text is cut (the change has
    /// more lines than the answer held), the answer is of another revision than the text now or the lines are not where the mark says.
    /// </summary>
    public bool RevertGitChange(GitPeek peek)
    {
        var mark = peek.Mark;
        // a change that is in the index is not taken back in the text: the text would only differ from the index then
        if (IsReadOnly || peek.Truncated || mark.Staged || peek.Revision != Doc.Doc.Revision || mark.Line + mark.Lines > Document.LineCount) return false;
        string lineBreak = LineBreakNear(mark.Line);
        string restored = string.Concat(peek.Old.Select((l, i) => l + (i < peek.Old.Count - 1 || peek.EndsWithNewline ? lineBreak : "")));
        int start = mark.Line < Document.LineCount ? Document.GetLineByNumber(mark.Line + 1).Offset : Document.TextLength;
        int end = start;
        if (mark.Lines > 0)
        {
            var last = Document.GetLineByNumber(mark.Line + mark.Lines);
            end = last.EndOffset + last.DelimiterLength;
            // lines that reach the end of a text without a final line break: the break before them goes with them, so no empty line is left behind
            if (last.DelimiterLength == 0 && peek.Old.Count == 0 && mark.Line > 0)
            {
                var before = Document.GetLineByNumber(mark.Line);
                start = before.EndOffset;
            }
        }
        else if (mark.Line >= Document.LineCount && Document.LineCount > 0 && Document.GetLineByNumber(Document.LineCount).DelimiterLength == 0 && start > 0 && restored.Length > 0)
        {
            restored = lineBreak + restored; // the text ends in a line without a break: the restored lines start on a line of their own
        }
        using (Doc.Doc.BeginUndoGroup()) Document.Replace(start, end - start, restored);
        return true;
    }

    /// <summary>Marks the changes at the right edge of the text, beside the scroll bar, one tick per change and a little left of the ticks of the other occurrences.</summary>
    private sealed class GitChangeStripLayer(EditorSurface owner) : IBackgroundRenderer
    {
        private const double Width = 3, Gap = 2;

        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var marks = owner.GitMarks;
            long lines = owner.Document.LineCount;
            double height = textView.Bounds.Height;
            if (marks.Count == 0 || lines <= 0 || height <= 0) return;
            double x = textView.Bounds.Width - OccurrenceStrip.Width - Gap - Width;
            foreach (var mark in marks)
            {
                if (!owner.TryFindResource(GitChangeColors.KeyOf(mark.Kind), owner.ActualThemeVariant, out var found) || found is not IBrush brush) continue;
                double top = Math.Min(height - 2, mark.Line * height / lines);
                double bottom = mark.Lines > 0 ? (mark.Line + mark.Lines) * height / lines : top + 2;
                drawingContext.FillRectangle(brush, new Rect(x, top, Width, Math.Max(2, bottom - top)));
            }
        }
    }
}

/// <summary>The theme tokens the colours of the changes are looked up by, in the gutter, the minimap and beside the scroll bar.</summary>
internal static class GitChangeColors
{
    public static string KeyOf(GitMarkKind kind) => kind switch
    {
        GitMarkKind.Added => "GitGutterAdded",
        GitMarkKind.Removed => "GitGutterRemoved",
        _ => "GitGutterModified",
    };
}

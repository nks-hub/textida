using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The other occurrences of the selected text on the paged surface (P3.6.12). Only the lines on screen are searched and drawn, so the size of the file does not matter;
/// the count and the marks beside the scrollbar are those of the lines on screen too, and the status bar says so. A scroll searches the new lines again after a short delay.
/// </summary>
internal sealed partial class PagedTextView
{
    private OccurrenceTracker? _occ;
    private (long Revision, long Caret, long Anchor, long Top) _occSeen = (-1, -1, -1, -1);

    /// <summary>What is highlighted, the count and the marked lines.</summary>
    public OccurrenceTracker Occurrences => _occ!;

    /// <summary>Raised when the count or the marked lines changed.</summary>
    public event Action? OccurrencesChanged;

    private void InitOccurrences()
    {
        _occ = new OccurrenceTracker(() => _options.Occurrence, ReadOccurrenceTarget, ScanVisibleLines, () => HasSelection, () => _canvas.InvalidateVisual());
        _occ.Changed += () =>
        {
            _canvas.InvalidateVisual();
            OccurrencesChanged?.Invoke();
        };
    }

    /// <summary>The view moved, the caret or the selection changed or the text did: what changed decides whether the highlight is dropped or only counted again.</summary>
    private void NoteOccurrenceState()
    {
        var was = _occSeen;
        _occSeen = (Doc.Revision, _caret, HasSelection ? _anchor : -1, TopLine);
        if (was.Revision != _occSeen.Revision || was.Caret != _occSeen.Caret || was.Anchor != _occSeen.Anchor) _occ?.Touch();
        else if (was.Top != _occSeen.Top) _occ?.Refresh();
    }

    private OccurrenceTarget? ReadOccurrenceTarget(OccurrenceOptions options)
    {
        if (IsMulti || IsComposing || SecretsHidden) return null;
        if (HasSelection)
        {
            var range = SelectionRange;
            if (range.Length > PluginLimitsPlaceholders.OccurrenceMaxChars * 4L) return null; // a character is at most 4 bytes
            return OccurrenceMatcher.TargetOf(Doc.Codec.GetString(Doc.ReadBytes(range)), range.Start, wholeWord: false, options);
        }
        if (!options.AtCaret || CaretLine() is not { } caret || caret.View.Truncated) return null;
        var text = caret.View.Map.Text;
        if (!OccurrenceMatcher.WordAt(text, caret.View.Map.CharIndexOf(_caret - caret.View.Start), out int start, out int end)) return null;
        return OccurrenceMatcher.TargetOf(text[start..end], caret.View.Start + caret.View.Map.ByteOffsetOf(start), wholeWord: true, options);
    }

    /// <summary>Counts the other occurrences in the lines on screen and lists them. A line cut to its first part (longer than <see cref="MaxLineBytes"/>) is left plain.</summary>
    private Task<OccurrenceScan> ScanVisibleLines(OccurrenceTarget target, CancellationToken cancel)
    {
        if (_occ?.Matcher is not { } regex) return Task.FromResult(OccurrenceScan.None);
        var lines = new List<long>();
        var spans = new List<(int Start, int Length)>();
        long count = 0, top = TopLine;
        for (int i = 0; i <= _canvas.VisibleLines && !cancel.IsCancellationRequested; i++)
        {
            if (GetLine(top + i) is not { } view) break;
            if (view.Truncated || view.Map.Text.Length > PluginLimitsPlaceholders.OccurrenceLineMaxChars) continue;
            spans.Clear();
            OccurrenceMatcher.Spans(regex, view.Map.Text, spans, PluginLimitsPlaceholders.OccurrenceMaxMarks);
            int here = 0;
            foreach (var (start, _) in spans)
                if (view.Start + view.Map.ByteOffsetOf(start) != target.Own) here++;
            if (here == 0) continue;
            lines.Add(top + i);
            count += here;
        }
        return Task.FromResult(new OccurrenceScan(lines, count, InView: true));
    }

    // ---- the minimap shows the same lines ----

    IReadOnlyList<long> IMinimapHost.OccurrenceLines => _occ?.Result.Lines ?? [];

    event Action? IMinimapHost.OccurrencesChanged
    {
        add => OccurrencesChanged += value;
        remove => OccurrencesChanged -= value;
    }

    private sealed partial class LinesSurface
    {
        private readonly List<(int Start, int Length)> _occSpans = [];
        private IBrush _occFill = Brushes.Transparent;
        private IBrush _occLine = Brushes.Gray;
        private IPen? _occPen;
        private int _occRoom;

        /// <summary>Takes the colors of the theme and the room for the occurrences of one draw; null when nothing is highlighted.</summary>
        private Regex? BeginOccurrences(Func<string, IBrush, IBrush> brush)
        {
            if (owner._occ is not { Target: not null, Matcher: { } regex }) return null;
            _occFill = brush("OccurrenceBg", Brushes.Transparent);
            _occLine = brush("OccurrenceBorder", Brushes.Gray);
            _occPen = new Pen(_occLine, 1);
            _occRoom = PluginLimitsPlaceholders.OccurrenceMaxDrawn;
            return regex;
        }

        /// <summary>Fills the other occurrences in a line, behind its text.</summary>
        private void DrawOccurrences(DrawingContext context, LineView view, double y, Regex regex, long own)
        {
            var text = view.Map.Text;
            if (view.Truncated || _occRoom <= 0 || text.Length > PluginLimitsPlaceholders.OccurrenceLineMaxChars) return;
            _occSpans.Clear();
            OccurrenceMatcher.Spans(regex, text, _occSpans, _occRoom);
            foreach (var (start, length) in _occSpans)
            {
                if (view.Start + view.Map.ByteOffsetOf(start) == own) continue;
                _occRoom--;
                double left = XOf(view, start), right = XOf(view, start + length);
                context.DrawRectangle(_occFill, _occPen, new Rect(TextLeft - owner._hScroll + left, y, right - left, _lineHeight), 2, 2);
            }
        }

        /// <summary>Marks the lines of the occurrences at the right edge of the text, beside the scrollbar.</summary>
        private void DrawOccurrenceStrip(DrawingContext context)
        {
            if (owner._occ is { Result.Lines.Count: > 0 } occ) OccurrenceStrip.Draw(context, occ.Result.Lines, owner.LineCount, new Rect(Bounds.Size), _occLine);
        }
    }
}

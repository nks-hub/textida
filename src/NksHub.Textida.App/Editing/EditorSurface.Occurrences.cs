using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Services;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The other occurrences of the selected text (P3.6.12). The lines on screen are searched and drawn here, in a background renderer of their own; the count and the
/// marked lines of the whole document come from the search worker over a snapshot, off the UI thread. Several carets, a block selection, a selection over
/// several lines and hidden values show nothing.
/// </summary>
internal sealed partial class EditorSurface
{
    private OccurrenceTracker? _occ;
    private readonly List<TextSegment> _occSegments = [];
    private readonly List<(int Start, int Length)> _occSpans = [];
    private (ITextSourceVersion Version, int First, int Last, OccurrenceTarget Target)? _occKey;
    private Pen? _occPen;

    /// <summary>Background of the other occurrences and the line around them; set with the theme.</summary>
    internal IBrush? OccurrenceBrush { get; private set; }

    internal IBrush? OccurrenceBorder { get; private set; }

    /// <summary>What is highlighted, the count and the marked lines.</summary>
    public OccurrenceTracker Occurrences => _occ!;

    /// <summary>Raised when the count or the marked lines changed.</summary>
    public event Action? OccurrencesChanged;

    private void InitOccurrences()
    {
        _occ = new OccurrenceTracker(() => Settings.Occurrence, ReadOccurrenceTarget, ScanOccurrencesAsync, () => !TextArea.Selection.IsEmpty,
            () => TextArea.TextView.InvalidateLayer(KnownLayer.Background));
        _occ.Changed += () =>
        {
            TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
            OccurrencesChanged?.Invoke();
        };
        TextArea.TextView.BackgroundRenderers.Add(new OccurrenceRenderer(this));
        TextArea.TextView.BackgroundRenderers.Add(new OccurrenceStripLayer(this));
        TextArea.Caret.PositionChanged += (_, _) => _occ.Touch();
        TextArea.SelectionChanged += (_, _) => _occ.Touch();
        CaretsChanged += _occ.Touch;
    }

    private void ApplyOccurrenceBrushes(Func<string, IBrush?> find)
    {
        OccurrenceBrush = find("OccurrenceBg");
        OccurrenceBorder = find("OccurrenceBorder");
        _occPen = OccurrenceBorder is null ? null : new Pen(OccurrenceBorder, 1);
        _occKey = null;
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
    }

    private OccurrenceTarget? ReadOccurrenceTarget(OccurrenceOptions options)
    {
        if (IsMulti || IsComposing || IsBlockSelection || BlockMode || SecretsHidden) return null;
        var selection = TextArea.Selection;
        if (!selection.IsEmpty)
        {
            var segment = selection.SurroundingSegment;
            if (segment.Length > PluginLimitsPlaceholders.OccurrenceMaxChars || segment.EndOffset > Document.GetLineByOffset(segment.Offset).EndOffset) return null;
            return OccurrenceMatcher.TargetOf(Document.GetText(segment.Offset, segment.Length), segment.Offset, wholeWord: false, options);
        }
        if (!options.AtCaret) return null;
        var line = Document.GetLineByOffset(CaretOffset);
        if (line.Length > PluginLimitsPlaceholders.OccurrenceLineMaxChars) return null;
        string text = Document.GetText(line.Offset, line.Length);
        return OccurrenceMatcher.WordAt(text, CaretOffset - line.Offset, out int start, out int end)
            ? OccurrenceMatcher.TargetOf(text[start..end], line.Offset + start, wholeWord: true, options)
            : null;
    }

    /// <summary>
    /// Counts the other occurrences in the whole document and lists their lines, on the search worker over a snapshot of the text, so the same places are found as by the find bar.
    /// The occurrence that is the selection itself (the one starting at the selection) is not counted. The scan stops at <see cref="PluginLimitsPlaceholders.OccurrenceMaxMarks"/>.
    /// </summary>
    private async Task<OccurrenceScan> ScanOccurrencesAsync(OccurrenceTarget target, CancellationToken cancel)
    {
        if (Doc.TextBackend is not { } backend) return OccurrenceScan.None;
        long own = backend.ToByteOffsets([(int)target.Own])[0];
        int max = PluginLimitsPlaceholders.OccurrenceMaxMarks;
        var settings = new SearchSettings { MaxResults = max + 1, StopAtLimit = true, Parallelism = 1, PreviewChars = 0 };
        var run = SearchWorker.Start(target.Query.Pattern, new SearchScope([], [LiveBuffer.Of(Doc.Doc)]), settings, cancel);
        var lines = new List<long>();
        long count = 0, last = -1;
        await foreach (var batch in run.Batches.ReadAllAsync(cancel))
            foreach (var hit in batch.Hits)
            {
                if (hit.Range.Start == own) continue;
                count++;
                if (hit.Line - 1 != last) lines.Add(last = hit.Line - 1);
            }
        var summary = await run.Completion;
        return new OccurrenceScan(lines, Math.Min(count, max), summary.LimitReached);
    }

    /// <summary>
    /// The other occurrences in the lines of the screen, as segments of the text. They are kept while the text, the lines on screen and what is looked for stay as they were, so a
    /// repaint without a change searches nothing.
    /// </summary>
    private IReadOnlyList<TextSegment> OccurrenceSegments(TextView view)
    {
        if (_occ is not { Target: { } target, Matcher: { } regex } || !view.VisualLinesValid || view.VisualLines is not { Count: > 0 } visual) return [];
        int first = visual[0].FirstDocumentLine.LineNumber, last = visual[^1].LastDocumentLine.LineNumber;
        var version = Document.Version;
        if (_occKey is { } key && key.First == first && key.Last == last && key.Target == target && version.BelongsToSameDocumentAs(key.Version) && version.CompareAge(key.Version) == 0)
            return _occSegments;
        _occKey = (version, first, last, target);
        _occSegments.Clear();
        int room = PluginLimitsPlaceholders.OccurrenceMaxDrawn;
        foreach (var screenLine in visual)
        {
            for (var line = screenLine.FirstDocumentLine; line is not null; line = line.NextLine)
            {
                if (line.Length > 0 && line.Length <= PluginLimitsPlaceholders.OccurrenceLineMaxChars) // shortcut: a longer line is left plain; upgrade by searching only the columns on screen
                {
                    _occSpans.Clear();
                    OccurrenceMatcher.Spans(regex, Document.GetText(line.Offset, line.Length), _occSpans, room);
                    foreach (var (start, length) in _occSpans)
                    {
                        if (line.Offset + start == target.Own) continue;
                        _occSegments.Add(new TextSegment { StartOffset = line.Offset + start, Length = length });
                        room--;
                    }
                }
                if (line == screenLine.LastDocumentLine || room <= 0) break;
            }
            if (room <= 0) break;
        }
        return _occSegments;
    }

    // ---- the minimap shows the same lines ----

    IReadOnlyList<long> IMinimapHost.OccurrenceLines => _occ?.Result.Lines ?? [];

    event Action? IMinimapHost.OccurrencesChanged
    {
        add => OccurrencesChanged += value;
        remove => OccurrencesChanged -= value;
    }

    /// <summary>Gives the other occurrences of the selection a background and an outline of their own, between the current line and the selection.</summary>
    private sealed class OccurrenceRenderer(EditorSurface owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            long trace = Core.TypingTrace.Begin();
            try
            {
                if (owner.OccurrenceBrush is not { } brush) return;
                var segments = owner.OccurrenceSegments(textView);
                if (segments.Count == 0) return;
                var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
                foreach (var segment in segments) builder.AddSegment(textView, segment);
                if (builder.CreateGeometry() is { } geometry) drawingContext.DrawGeometry(brush, owner._occPen, geometry);
            }
            finally { Core.TypingTrace.End("editor.drawOccurrences", trace); }
        }
    }

    /// <summary>Marks the lines with another occurrence at the right edge of the text, beside the scrollbar.</summary>
    private sealed class OccurrenceStripLayer(EditorSurface owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Caret;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (owner.OccurrenceBorder is not { } brush || owner._occ is not { Result.Lines.Count: > 0 } occ) return;
            OccurrenceStrip.Draw(drawingContext, occ.Result.Lines, owner.Document.LineCount, new Avalonia.Rect(textView.Bounds.Size), brush);
        }
    }
}

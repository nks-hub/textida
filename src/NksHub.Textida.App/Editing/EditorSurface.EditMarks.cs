using Avalonia.Threading;
using AvaloniaEdit.Document;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The lines edited since the file was opened or saved, for the strip of the minimap (PZ.10.1). A line is remembered as the line object of the document, which follows the line through later
/// edits above it; an insertion of many lines is remembered as one range between two anchors of the document instead, so that a paste of a whole file costs two anchors and is marked in full.
/// A save, a reload or an undo back to the saved text forgets them all.
/// </summary>
internal sealed partial class EditorSurface
{
    /// <summary>The most lines remembered one by one; an edit that would not fit marks its first lines only. shortcut: scattered edits beyond this many lines are not marked, upgrade by merging neighbours into ranges.</summary>
    internal const int MaxEditedLines = 50_000;

    /// <summary>An insertion of more lines than this is remembered as one range of lines, not line by line.</summary>
    internal const int MaxLinesMarkedOneByOne = 256;

    /// <summary>The most ranges remembered; a further big insertion is not marked.</summary>
    internal const int MaxEditedRanges = 10_000;

    private readonly HashSet<DocumentLine> _edited = [];
    private readonly List<(TextAnchor Start, TextAnchor End)> _editedRanges = [];
    private DispatcherTimer? _editedTimer;
    private bool _editedDirty;

    /// <summary>The lines edited since the last save changed (settled a moment after the typing).</summary>
    public event Action? EditedLinesChanged;

    private void InitEditMarks()
    {
        Document.Changed += OnEditedText;
        Doc.Doc.Saved += (_, _) => ForgetEdits();
    }

    private void OnEditedText(object? sender, DocumentChangeEventArgs e)
    {
        var firstLine = Document.GetLineByOffset(Math.Min(e.Offset, Document.TextLength));
        int end = Math.Min(e.Offset + e.InsertionLength, Document.TextLength);
        int first = firstLine.LineNumber, last = Document.GetLineByOffset(end).LineNumber;
        if (e.InsertionLength > 0 && last > first && firstLine.Offset == e.Offset && Document.GetLineByOffset(end).Offset == end) last--; // whole lines put in front of a line leave that line alone
        if (last - first >= MaxLinesMarkedOneByOne)
        {
            if (_editedRanges.Count < MaxEditedRanges)
            {
                // text put in at the very start of the range is before it, text put in at its end is after it, and deleting text inside it shrinks it
                var (start, stop) = (Document.CreateAnchor(e.Offset), Document.CreateAnchor(end));
                (start.MovementType, stop.MovementType) = (AnchorMovementType.AfterInsertion, AnchorMovementType.BeforeInsertion);
                (start.SurviveDeletion, stop.SurviveDeletion) = (true, true);
                _editedRanges.Add((start, stop));
            }
        }
        else if (_edited.Count < MaxEditedLines)
            for (int number = first; number <= last && _edited.Count < MaxEditedLines; number++) _edited.Add(Document.GetLineByNumber(number));
        _editedDirty = true;
        if (_editedTimer is null)
        {
            _editedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _editedTimer.Tick += (_, _) =>
            {
                _editedTimer.Stop();
                if (!Doc.HasUnsavedWork) ClearEdits(); // loaded, reloaded or undone back to the saved text
                _editedDirty = false;
                EditedLinesChanged?.Invoke();
            };
        }
        if (!_editedTimer.IsEnabled) _editedTimer.Start();
    }

    private void ForgetEdits()
    {
        if (_edited.Count == 0 && _editedRanges.Count == 0 && !_editedDirty) return;
        ClearEdits();
        EditedLinesChanged?.Invoke();
    }

    private void ClearEdits()
    {
        _edited.Clear();
        _editedRanges.Clear();
    }

    /// <summary>The runs of zero-based lines edited since the file was opened or saved, ascending, apart from each other; a line edited once and a range pasted are alike.</summary>
    public IReadOnlyList<(long First, long Last)> EditedRanges
    {
        get
        {
            var runs = _edited.Where(l => !l.IsDeleted).Select(l => (long)l.LineNumber - 1).Select(n => (First: n, Last: n)).ToList();
            foreach (var (start, stop) in _editedRanges)
            {
                if (start.IsDeleted || stop.IsDeleted) continue;
                int first = Document.GetLineByOffset(start.Offset).LineNumber, last = Document.GetLineByOffset(stop.Offset).LineNumber;
                if (last > first && Document.GetLineByOffset(stop.Offset).Offset == stop.Offset) last--; // the end is where the next line begins
                runs.Add((first - 1, last - 1));
            }
            runs.Sort();
            var merged = new List<(long First, long Last)>();
            foreach (var (first, last) in runs)
            {
                if (merged.Count > 0 && first <= merged[^1].Last + 1) merged[^1] = (merged[^1].First, Math.Max(merged[^1].Last, last));
                else merged.Add((first, last));
            }
            return merged;
        }
    }

    /// <summary>Zero-based lines edited since the file was opened or saved, ascending; a pasted range is listed line by line, so ask <see cref="EditedRanges"/> where the size matters.</summary>
    public IReadOnlyList<long> EditedLines => [.. EditedRanges.SelectMany(r => LongRange(r.First, r.Last))];

    private static IEnumerable<long> LongRange(long first, long last)
    {
        for (long n = first; n <= last; n++) yield return n;
    }

    IReadOnlyList<(long First, long Last)> IMinimapHost.EditedRanges => EditedRanges;

    event Action? IMinimapHost.EditedLinesChanged
    {
        add => EditedLinesChanged += value;
        remove => EditedLinesChanged -= value;
    }
}

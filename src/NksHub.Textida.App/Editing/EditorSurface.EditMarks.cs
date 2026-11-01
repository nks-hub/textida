using Avalonia.Threading;
using AvaloniaEdit.Document;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The lines edited since the file was opened or saved, for the strip of the minimap (PZ.10.1). A line is remembered as the line object of the document, which follows the line through later
/// edits above it; a save, a reload or an undo back to the saved text forgets them all.
/// </summary>
internal sealed partial class EditorSurface
{
    /// <summary>The most lines remembered; a bigger edit marks its first lines only. shortcut: a paste of more lines than this is not marked in full, upgrade by keeping ranges.</summary>
    internal const int MaxEditedLines = 50_000;

    private readonly HashSet<DocumentLine> _edited = [];
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
        if (_edited.Count < MaxEditedLines)
        {
            var firstLine = Document.GetLineByOffset(Math.Min(e.Offset, Document.TextLength));
            int end = Math.Min(e.Offset + e.InsertionLength, Document.TextLength);
            int first = firstLine.LineNumber, last = Document.GetLineByOffset(end).LineNumber;
            if (e.InsertionLength > 0 && last > first && firstLine.Offset == e.Offset && Document.GetLineByOffset(end).Offset == end) last--; // whole lines put in front of a line leave that line alone
            for (int number = first; number <= last && _edited.Count < MaxEditedLines; number++) _edited.Add(Document.GetLineByNumber(number));
        }
        _editedDirty = true;
        if (_editedTimer is null)
        {
            _editedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _editedTimer.Tick += (_, _) =>
            {
                _editedTimer.Stop();
                if (!Doc.HasUnsavedWork) _edited.Clear(); // loaded, reloaded or undone back to the saved text
                _editedDirty = false;
                EditedLinesChanged?.Invoke();
            };
        }
        if (!_editedTimer.IsEnabled) _editedTimer.Start();
    }

    private void ForgetEdits()
    {
        if (_edited.Count == 0 && !_editedDirty) return;
        _edited.Clear();
        EditedLinesChanged?.Invoke();
    }

    /// <summary>Zero-based lines edited since the file was opened or saved, ascending.</summary>
    public IReadOnlyList<long> EditedLines => [.. _edited.Where(l => !l.IsDeleted).Select(l => (long)l.LineNumber - 1).Order()];

    IReadOnlyList<long> IMinimapHost.EditedLines => EditedLines;

    event Action? IMinimapHost.EditedLinesChanged
    {
        add => EditedLinesChanged += value;
        remove => EditedLinesChanged -= value;
    }
}

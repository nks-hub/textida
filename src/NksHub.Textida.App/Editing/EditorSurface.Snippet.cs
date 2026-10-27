using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Snippets;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// A snippet session: after a snippet with tab stops was inserted, Tab and Shift+Tab walk through its stops, the other places of a stop follow what is typed
/// into the first one, and the session ends at <c>$0</c>, on Escape, or when the caret leaves the snippet. Every inserted snippet (one per caret) is part of
/// the session; their stops are visited together, one caret each. The places are anchors of the text view document, so they follow any edit.
/// </summary>
internal sealed partial class EditorSurface
{
    private sealed record Place(TextAnchor Start, TextAnchor End)
    {
        public int Offset => Start.Offset;
        public int Length => Math.Max(0, End.Offset - Start.Offset);
    }

    private sealed record LiveStop(IReadOnlyList<Place> Places, IReadOnlyList<string> Choices);

    private sealed record LiveSnippet(IReadOnlyList<LiveStop> Stops, TextAnchor Final, TextAnchor RegionStart, TextAnchor RegionEnd);

    private List<LiveSnippet>? _snippets;
    private int _snippetStop = -1;
    private bool _snippetBusy;
    private TextDocument? _snippetDocument;

    /// <summary>Raised when the stop that became current offers a list of values; <see cref="ChooseSnippetValue"/> takes the pick.</summary>
    public event Action<IReadOnlyList<string>>? SnippetChoicesRequested;

    /// <summary>True while a snippet session is running; the <c>inSnippet</c> context.</summary>
    public bool InSnippet => _snippets is not null;

    private TextAnchor Anchor(int offset, AnchorMovementType movement)
    {
        var anchor = Document.CreateAnchor(Math.Clamp(offset, 0, Document.TextLength));
        anchor.MovementType = movement;
        anchor.SurviveDeletion = true;
        return anchor;
    }

    /// <summary>
    /// Starts a session for snippets that were just written: each at the document offset <c>Start</c>, with its text exactly as inserted. The first stop
    /// is selected in every one. Snippets without a numbered stop need no session and are skipped.
    /// </summary>
    public void BeginSnippet(IReadOnlyList<(int Start, ParsedSnippet Snippet)> placed)
    {
        EndSnippet();
        var live = new List<LiveSnippet>();
        foreach (var (start, snippet) in placed)
        {
            if (snippet.Stops.Count == 0) continue;
            var stops = snippet.Stops.Select(stop => new LiveStop(
                [.. stop.Spans.Select(x => new Place(Anchor(start + x.Start, AnchorMovementType.BeforeInsertion), Anchor(start + x.Start + x.Length, AnchorMovementType.AfterInsertion)))],
                stop.Choices)).ToList();
            live.Add(new LiveSnippet(stops, Anchor(start + snippet.FinalOffset, AnchorMovementType.AfterInsertion),
                Anchor(start, AnchorMovementType.BeforeInsertion), Anchor(start + snippet.Text.Length, AnchorMovementType.AfterInsertion)));
        }
        if (live.Count == 0 || live.Any(l => l.Stops.Count != live[0].Stops.Count)) return;
        _snippets = live;
        _snippetDocument = Document;
        Document.UpdateFinished += OnSnippetDocumentUpdated;
        TextArea.Caret.PositionChanged += OnSnippetCaretMoved;
        GoToSnippetStop(0);
    }

    /// <summary>Tab: the next stop; after the last one the caret goes to <c>$0</c> and the session ends. False when no session runs.</summary>
    public bool NextSnippetStop()
    {
        if (_snippets is null) return false;
        GoToSnippetStop(_snippetStop + 1);
        return true;
    }

    /// <summary>Shift+Tab: the previous stop (the first one stays).</summary>
    public bool PreviousSnippetStop()
    {
        if (_snippets is null) return false;
        GoToSnippetStop(Math.Max(0, _snippetStop - 1));
        return true;
    }

    public void EndSnippet()
    {
        if (_snippets is null) return;
        _snippets = null;
        _snippetStop = -1;
        if (_snippetDocument is { } document) document.UpdateFinished -= OnSnippetDocumentUpdated;
        _snippetDocument = null;
        TextArea.Caret.PositionChanged -= OnSnippetCaretMoved;
    }

    /// <summary>Writes a value from the list of the current stop into its first place in every snippet; the mirrors follow.</summary>
    public void ChooseSnippetValue(string value)
    {
        if (_snippets is null || _snippetStop < 0 || IsReadOnly) return;
        using (Doc.Doc.BeginUndoGroup())
        {
            _snippetBusy = true;
            try
            {
                using (Document.RunUpdate())
                    foreach (var live in _snippets)
                    {
                        var place = live.Stops[_snippetStop].Places[0];
                        Document.Replace(place.Offset, place.Length, value);
                    }
            }
            finally { _snippetBusy = false; }
            SyncSnippetMirrors();
        }
        GoToSnippetStop(_snippetStop, offerChoices: false);
    }

    private void GoToSnippetStop(int index, bool offerChoices = true)
    {
        if (_snippets is null) return;
        bool finished = index >= _snippets[0].Stops.Count;
        var selections = new List<Sel>(_snippets.Count);
        _snippetBusy = true;
        try
        {
            _snippetStop = index;
            foreach (var live in _snippets)
            {
                if (finished) selections.Add(Sel.At(live.Final.Offset));
                else
                {
                    var place = live.Stops[index].Places[0];
                    selections.Add(new Sel(place.Offset, place.Offset + place.Length));
                }
            }
            SetSelections(selections);
        }
        finally { _snippetBusy = false; }
        if (finished) EndSnippet();
        else if (offerChoices && _snippets![0].Stops[index].Choices is { Count: > 0 } choices) SnippetChoicesRequested?.Invoke(choices);
    }

    private void OnSnippetDocumentUpdated(object? sender, EventArgs e)
    {
        if (!_snippetBusy) SyncSnippetMirrors();
    }

    /// <summary>Makes every other place of the current stop show the text of its first place.</summary>
    private void SyncSnippetMirrors()
    {
        if (_snippets is null || _snippetStop < 0) return;
        _snippetBusy = true;
        try
        {
            foreach (var live in _snippets)
            {
                if (_snippetStop >= live.Stops.Count) continue;
                var places = live.Stops[_snippetStop].Places;
                string text = Document.GetText(places[0].Offset, places[0].Length);
                for (int i = 1; i < places.Count; i++)
                    if (Document.GetText(places[i].Offset, places[i].Length) != text) Document.Replace(places[i].Offset, places[i].Length, text);
            }
        }
        finally { _snippetBusy = false; }
    }

    private void OnSnippetCaretMoved(object? sender, EventArgs e)
    {
        if (_snippets is null || _snippetBusy) return;
        int caret = CaretOffset;
        if (!_snippets.Any(l => caret >= l.RegionStart.Offset && caret <= l.RegionEnd.Offset)) EndSnippet();
    }
}

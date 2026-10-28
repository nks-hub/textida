using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Snippets;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// A snippet session: after a snippet with tab stops was inserted, Tab and Shift+Tab walk through its stops, the other places of a stop follow what is typed
/// into the first one, and the session ends at <c>$0</c>, on Escape, or when the caret leaves the snippet. Every inserted snippet (one per caret) is part of
/// the session; their stops are visited together, one caret each. The places are anchors of the text view document, so they follow any edit.
/// A stop inside the default of another stop is visited after the one it stands in; a place with a transform shows the transformed text of its stop.
/// </summary>
internal sealed partial class EditorSurface
{
    private sealed record Place(TextAnchor Start, TextAnchor End, SnippetTransform? Transform = null)
    {
        public int Offset => Start.Offset;
        public int Length => Math.Max(0, End.Offset - Start.Offset);
        public bool Contains(Place other) => Offset <= other.Offset && Offset + Length >= other.Offset + other.Length;
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
                [.. stop.Spans.Select(x => new Place(Anchor(start + x.Start, AnchorMovementType.BeforeInsertion), Anchor(start + x.Start + x.Length, AnchorMovementType.AfterInsertion), x.Transform))],
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

    /// <summary>
    /// Makes every other place of the current stop show the text of its first place (changed by its transform, if it has one). A place that is inside the first place
    /// of another stop changes that stop too, so the stops that hold a changed place are synced after it; a few rounds at most, in case stops hold each other.
    /// </summary>
    private void SyncSnippetMirrors()
    {
        if (_snippets is null || _snippetStop < 0) return;
        _snippetBusy = true;
        try
        {
            foreach (var live in _snippets)
            {
                if (_snippetStop >= live.Stops.Count) continue;
                var pending = new List<int> { _snippetStop };
                int round = 0;
                void QueueHolders(Place changed)
                {
                    for (int s = 0; s < live.Stops.Count; s++)
                    {
                        var holder = live.Stops[s].Places[0];
                        if (!ReferenceEquals(holder, changed) && holder.Contains(changed) && pending.IndexOf(s, round + 1) < 0) pending.Add(s);
                    }
                }
                QueueHolders(live.Stops[_snippetStop].Places[0]);
                for (; round < pending.Count && round < live.Stops.Count * 4; round++)
                {
                    var places = live.Stops[pending[round]].Places;
                    string text = Document.GetText(places[0].Offset, places[0].Length);
                    for (int i = 1; i < places.Count; i++)
                    {
                        var place = places[i];
                        string shown = Document.GetText(place.Offset, place.Length);
                        string wanted = MirrorText(place, text, shown);
                        if (wanted == shown) continue;
                        Document.Replace(place.Offset, place.Length, wanted);
                        QueueHolders(place);
                    }
                }
            }
        }
        finally { _snippetBusy = false; }
    }

    /// <summary>The text a mirror should show; a transform that runs too long leaves the mirror as it is.</summary>
    private static string MirrorText(Place place, string text, string shown)
    {
        if (place.Transform is not { } transform) return text;
        try { return transform.Apply(text); }
        catch (RegexMatchTimeoutException) { return shown; }
    }

    private void OnSnippetCaretMoved(object? sender, EventArgs e)
    {
        if (_snippets is null || _snippetBusy) return;
        int caret = CaretOffset;
        if (!_snippets.Any(l => caret >= l.RegionStart.Offset && caret <= l.RegionEnd.Offset)) EndSnippet();
    }
}

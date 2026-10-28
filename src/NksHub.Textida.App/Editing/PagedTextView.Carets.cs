using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// More than one caret on the paged surface. The caret and selection of the view stay the primary one; the others are anchors of the document
/// (<see cref="AnchoredSelections"/>), so they follow edits from any source, and are drawn only where they are on screen. While there are several, keys, typed
/// text and the clipboard act on all of them in one transaction (<see cref="ByteCarets"/>): one revision, one undo step, byte-exact. The selections are
/// byte offsets of the current revision, so they stay valid while the line index grows; what needs a line number (up, down, Home, End) skips a caret whose line is not known yet.
/// </summary>
internal sealed partial class PagedTextView
{
    /// <summary>A document up to this size is searched for occurrences on the UI thread; a larger one on the pool, over a snapshot.</summary>
    private const long InlineSearchBytes = 16 << 20;

    private AnchoredSelections? _others;
    private AnchoredSelections? _primaryKept;
    private (long Revision, int Stamp, SelectionSet Set)? _allCache;
    private int _caretStamp;
    private int[]? _wantedColumns;
    private int? _addColumn;
    private bool _wholeWord;
    private string _runKind = "";
    private long _runRevision = -1;
    private SelectionSet? _runCarets;
    private int _runId;
    private CancellationTokenSource? _searchCts;
    private bool _searching;

    /// <summary>Most carets the view takes; a test lowers it. Placeholder, OPEN: <see cref="PluginLimitsPlaceholders.PagedMaxCarets"/>.</summary>
    internal int CaretLimit { get; set; } = PluginLimitsPlaceholders.PagedMaxCarets;

    /// <summary>Longest selection, in bytes, whose other occurrences are looked for; a test lowers it.</summary>
    internal int OccurrenceLimitBytes { get; set; } = PluginLimitsPlaceholders.PagedOccurrenceMaxBytes;

    /// <summary>Most bytes a paste inserts over all carets together; a test lowers it.</summary>
    internal long MultiPasteLimitBytes { get; set; } = PluginLimitsPlaceholders.PagedMultiPasteMaxBytes;

    /// <summary>True while there is more than one caret.</summary>
    public bool IsMulti => _others is not null;

    public int CaretCount => _others is null ? 1 : AllCarets().Count;

    /// <summary>True while the input method has text composed at the caret that is not in the document yet; its keys (Esc, Enter, the arrows) belong to it.</summary>
    public bool IsComposing => _preedit.Length > 0;

    /// <summary>Carets the last draw put on screen while there were several; a test checks that only those on screen are drawn.</summary>
    internal int DrawnCarets => _canvas.DrawnCarets;

    private enum CaretStep { Left, Right, LineStart, LineEnd }

    // ---- the selections as a set ----

    /// <summary>All carets in document order with the primary one marked; one selection when there is one caret.</summary>
    internal SelectionSet AllCarets()
    {
        var own = new TextSelection(HasSelection ? _anchor : _caret, _caret);
        if (_others is null) return SelectionSet.Single(own);
        if (_allCache is { } cache && cache.Revision == Doc.Revision && cache.Stamp == _caretStamp) return cache.Set;
        var items = new List<TextSelection>();
        if (_others.Current() is { } rest) items.AddRange(rest.Items);
        items.Add(own);
        var set = SelectionSet.Create(items, items.Count - 1);
        _allCache = (Doc.Revision, _caretStamp, set);
        return set;
    }

    /// <summary>Makes <paramref name="set"/> the carets of the view; the primary one becomes the caret and selection of the view, the others anchors.</summary>
    internal void SetCarets(SelectionSet set)
    {
        var primary = set.PrimarySelection;
        _caret = primary.Caret;
        _anchor = primary.IsEmpty ? -1 : primary.Anchor;
        _preedit = "";
        _typingKey = null;
        _wanted = -1;
        _wantedColumns = null;
        _addColumn = null;
        _caretStamp++;
        if (set.Count == 1)
        {
            ReleaseAnchors();
            _others = null;
            _primaryKept = null;
            return;
        }
        var rest = SelectionSet.Create([.. set.Items.Where((_, i) => i != set.Primary)], 0);
        var own = SelectionSet.Single(primary);
        // after typing at every caret the anchors have followed the text to where the new carets are
        if (_others is null || _primaryKept is null || !_others.Matches(rest) || !_primaryKept.Matches(own))
        {
            ReleaseAnchors();
            _others = new AnchoredSelections(Doc, rest);
            _primaryKept = new AnchoredSelections(Doc, own);
        }
    }

    private void ReleaseAnchors()
    {
        _others?.Release();
        _primaryKept?.Release();
    }

    /// <summary>Back to the primary caret alone; any single-caret move or edit of the view does this first.</summary>
    private void DropOthers()
    {
        if (_others is null) return;
        ReleaseAnchors();
        _others = null;
        _primaryKept = null;
        _wantedColumns = null;
        _addColumn = null;
        _caretStamp++;
    }

    /// <summary>Back to one caret, the primary one.</summary>
    public void ClearCarets()
    {
        if (_others is null) return;
        DropOthers();
        _canvas.InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>A change that this view did not make: the anchors have moved with the text, so the primary caret and the others are read from them again.</summary>
    private void FollowExternalChange(ChangeSet change)
    {
        if (change.Kind != ChangeKind.View)
        {
            ResyncCarets();
            return;
        }
        // the bytes are read as another encoding: the offsets may no longer be characters, so only the primary caret stays
        DropOthers();
        _caret = Math.Clamp(_caret, Doc.BomLength, Doc.ByteLength);
        _anchor = -1;
    }

    /// <summary>The carets are read from their anchors again: after a change of another source, or when the view returns to the visual tree (another tab was shown meanwhile and it missed the changes).</summary>
    private void ResyncCarets()
    {
        if (_primaryKept?.Current() is not { } kept)
        {
            DropOthers();
            _caret = Math.Clamp(_caret, Doc.BomLength, Doc.ByteLength);
            _anchor = -1;
            return;
        }
        var primary = kept.PrimarySelection;
        (_caret, _anchor) = (primary.Caret, primary.IsEmpty ? -1 : primary.Anchor);
        SetCarets(AllCarets());
    }

    private static SelectionSet CaretsOf(IReadOnlyList<ByteRange> ranges, Document doc)
    {
        long Clamp(long offset) => Math.Clamp(offset, doc.BomLength, doc.ByteLength);
        var items = ranges.Select(r => new TextSelection(Clamp(r.Start), Clamp(r.End))).ToList();
        return SelectionSet.Create(items, items.Count - 1); // the document hands them back with the primary one last
    }

    // ---- places ----

    /// <summary>True when the line of an offset can be found: the line index has reached it, or the view continues from a place before it (a detour counts lines from there on).</summary>
    private bool CanFindLine(long offset) => CanLocate(offset) && (_detour is null || offset >= _detour.BaseStart);

    private (long Line, LineView View)? LineAt(long offset)
    {
        if (offset == _caret) return CaretLine();
        if (!CanFindLine(offset)) return null;
        long line = LineOf(offset);
        return GetLine(line) is { } view ? (line, view) : null;
    }

    private Site? SiteAt(long offset)
    {
        if (LineAt(offset) is not { } c) return null;
        if (!c.View.Truncated) return new Site(c.Line, c.View, c.View.Map.CharIndexOf(offset - c.View.Start), false);
        return new Site(c.Line, LocalView(c.View, offset), ColumnOfOffset(c.View, offset), true);
    }

    /// <summary>Column of a place in the line as it is drawn (tabs expanded), the unit that up and down keep.</summary>
    private static int DisplayColumn(Site site) =>
        site.Long ? (int)Math.Min(site.Column, int.MaxValue) : site.View.DisplayAt[(int)site.Column];

    private long OffsetAtDisplay(LineView view, int column) =>
        view.Truncated && column >= view.Display.Length ? OffsetOfColumn(view, column) : view.Start + view.Map.ByteOffsetOf(view.IndexAtDisplay(column));

    /// <summary>Start or end (before the line break) of the line that holds an offset; null while the line index does not know it.</summary>
    private long? LineEdgeAt(long offset, bool end)
    {
        if (!CanFindLine(offset)) return null;
        return TryBounds(LineOf(offset), out long start, out long textEnd, out _) ? (end ? textEnd : start) : null;
    }

    /// <summary>
    /// Scrolls to the primary caret. A caret beyond the part of the file the line index has reached cannot be found by line number, so the view continues from
    /// there as after a jump (the line numbers are estimates until the index catches up) without touching the selections.
    /// </summary>
    private void RevealPrimary()
    {
        if (!Indexed && _caret >= Paged.IndexedBytes && (_detour is null || _caret < _detour.BaseStart))
        {
            EnterDetour(_caret, keepSelection: true);
            return;
        }
        if (_detour is not null && (Indexed || _caret < Paged.IndexedBytes))
        {
            _detour = null;
            _lines.Clear();
            _lineMemo = (-1, -1, 0);
            UpdateScrollRange();
        }
        Reveal();
    }

    // ---- moving ----

    /// <summary>Moves every caret (Shift extends the selections). Carets that meet become one.</summary>
    private void MoveCarets(CaretStep step, bool extend)
    {
        var before = AllCarets();
        var text = ByteText.Of(Doc);
        var moved = new List<TextSelection>(before.Count);
        foreach (var s in before.Items)
        {
            long target;
            if (!extend && !s.IsEmpty && step is CaretStep.Left or CaretStep.Right) target = step == CaretStep.Left ? s.Start : s.End;
            else target = step switch
            {
                CaretStep.Left => ByteCarets.Previous(text, s.Caret),
                CaretStep.Right => ByteCarets.Next(text, s.Caret),
                _ => LineEdgeAt(s.Caret, step == CaretStep.LineEnd) ?? s.Caret,
            };
            moved.Add(extend ? new TextSelection(s.Anchor, target) : TextSelection.At(target));
        }
        ApplyMoved(SelectionSet.Create(moved, before.Primary));
    }

    /// <summary>Moves every caret one line up or down to its remembered display column, which repeated moves keep across short lines.</summary>
    private void MoveCaretsVertical(int delta, bool extend)
    {
        var before = AllCarets();
        var sticky = _wantedColumns is { } known && known.Length == before.Count ? known : null;
        var columns = new int[before.Count];
        var moved = new List<TextSelection>(before.Count);
        for (int i = 0; i < before.Count; i++)
        {
            var s = before.Items[i];
            if (SiteAt(s.Caret) is not { } site)
            {
                columns[i] = sticky?[i] ?? 0;
                moved.Add(s);
                continue;
            }
            columns[i] = sticky?[i] ?? DisplayColumn(site);
            long target = site.Line + delta;
            if (target < 0 || target >= LineCount || GetLine(target) is not { } view)
            {
                moved.Add(s);
                continue;
            }
            long to = OffsetAtDisplay(view, columns[i]);
            moved.Add(extend ? new TextSelection(s.Anchor, to) : TextSelection.At(to));
        }
        var set = SelectionSet.Create(moved, before.Primary);
        ApplyMoved(set);
        _wantedColumns = set.Count == before.Count ? columns : null;
    }

    private void ApplyMoved(SelectionSet set)
    {
        SetCarets(set);
        RevealPrimary();
        RaiseViewChanged();
    }

    /// <summary>The keys that act on all carets while there are several; false lets the view handle the key as for one caret.</summary>
    private bool MultiKey(KeyEventArgs e, bool command)
    {
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Left when !command: MoveCarets(CaretStep.Left, shift); return true;
            case Key.Right when !command: MoveCarets(CaretStep.Right, shift); return true;
            case Key.Up when !command: MoveCaretsVertical(-1, shift); return true;
            case Key.Down when !command: MoveCaretsVertical(1, shift); return true;
            case Key.Home when !command: MoveCarets(CaretStep.LineStart, shift); return true;
            case Key.End when !command: MoveCarets(CaretStep.LineEnd, shift); return true;
            case Key.Escape when !Doc.IsSaving && _preedit.Length == 0: ClearCarets(); return true; // while the input method composes, Esc is its own
            case Key.PageUp or Key.PageDown: DropOthers(); return false; // the page keys move one caret only
            default: return false;
        }
    }

    // ---- adding ----

    /// <summary>Adds a caret on the line above (-1) or below (+1) the topmost or bottommost caret at the same display column; the new caret is primary.</summary>
    public void AddCaretVertical(int direction)
    {
        var all = AllCarets();
        var edge = direction < 0 ? all.Items[0] : all.Items[^1];
        if (SiteAt(edge.Caret) is not { } site) return;
        int column = _addColumn ?? DisplayColumn(site);
        long target = site.Line + direction;
        if (target < 0 || target >= LineCount || GetLine(target) is not { } view) return;
        if (AddCaret(all, TextSelection.At(OffsetAtDisplay(view, column)))) _addColumn = column; // the column outlives the move, so a short line does not shorten the next
    }

    /// <summary>Alt+click: a caret at <paramref name="offset"/>, or, when a caret or selection is there and others remain, removes that one.</summary>
    internal void AddOrRemoveCaretAt(long offset)
    {
        var all = AllCarets();
        int hit = -1;
        for (int i = 0; i < all.Count; i++)
            if (all.Items[i].Start <= offset && offset <= all.Items[i].End) hit = i;
        if (hit < 0) AddCaret(all, TextSelection.At(offset));
        else if (all.Count > 1)
        {
            var rest = all.Items.Where((_, i) => i != hit).ToList();
            ApplyMoved(SelectionSet.Create(rest, Math.Min(all.Primary > hit ? all.Primary - 1 : all.Primary, rest.Count - 1)));
        }
    }

    private bool AddCaret(SelectionSet all, TextSelection added)
    {
        var list = new List<TextSelection>(all.Items) { added };
        return Adopt(SelectionSet.Create(list, list.Count - 1), all.Count);
    }

    /// <summary>Takes a set that may have more carets than before; past the limit it is refused with a message and the carets stay as they were.</summary>
    private bool Adopt(SelectionSet next, int countBefore)
    {
        if (next.Count > countBefore && next.Count > CaretLimit)
        {
            Refused?.Invoke(Loc.Get(K.PagedCaretsLimit, ("limit", CaretLimit)));
            return false;
        }
        if (next.Count == countBefore && next.Items.SequenceEqual(AllCarets().Items)) return false;
        ApplyMoved(next);
        return true;
    }

    // ---- occurrences ----

    /// <summary>Ctrl+D: an empty primary caret first selects the word under it; then each call adds the next occurrence of the primary selection, wrapping around the end.</summary>
    public Task AddNextOccurrenceAsync() => StepOccurrenceAsync(keepCurrent: true);

    /// <summary>Ctrl+K Ctrl+D: like Add Next Occurrence, but the primary selection is dropped for the next occurrence.</summary>
    public Task SkipOccurrenceAsync() => StepOccurrenceAsync(keepCurrent: false);

    private async Task StepOccurrenceAsync(bool keepCurrent)
    {
        var all = AllCarets();
        var primary = all.PrimarySelection;
        if (primary.IsEmpty)
        {
            if (!ByteCarets.TryWordAt(ByteText.Of(Doc), primary.Caret, out long wordStart, out long wordEnd)) return;
            _wholeWord = true;
            var list = new List<TextSelection>(all.Items) { [all.Primary] = new TextSelection(wordStart, wordEnd) };
            ApplyMoved(SelectionSet.Create(list, all.Primary));
            return;
        }
        if (TooLongToSearch(primary.Length)) return;
        byte[] needle = Doc.ReadBytes(primary.ToRange());
        bool whole = _wholeWord;
        (long revision, int stamp) = (Doc.Revision, _caretStamp);
        var found = await SearchAsync((text, cancel) =>
        {
            var hits = new List<long>(1);
            bool Take(long hit)
            {
                if (all.Items.Any(a => a.Start < hit + needle.Length && hit < a.End)) return true;
                hits.Add(hit);
                return false;
            }
            ByteCarets.Find(text, needle, primary.End, text.Length, whole, Take, cancel);
            if (hits.Count == 0) ByteCarets.Find(text, needle, text.Bom, primary.Start, whole, Take, cancel);
            return hits;
        });
        if (found is not { Count: > 0 } || Doc.Revision != revision || _caretStamp != stamp) return; // the document or the carets changed while it searched
        var added = new TextSelection(found[0], found[0] + needle.Length);
        var next = new List<TextSelection>(all.Items);
        if (keepCurrent) next.Add(added);
        else next[all.Primary] = added;
        Adopt(SelectionSet.Create(next, keepCurrent ? next.Count - 1 : all.Primary), all.Count);
    }

    /// <summary>Ctrl+Shift+L: a selection at every occurrence of the primary selection (of the word under an empty caret), up to the caret limit.</summary>
    public async Task SelectAllOccurrencesAsync()
    {
        var all = AllCarets();
        var primary = all.PrimarySelection;
        long start = primary.Start, end = primary.End;
        bool whole = _wholeWord;
        if (primary.IsEmpty)
        {
            if (!ByteCarets.TryWordAt(ByteText.Of(Doc), primary.Caret, out start, out end)) return;
            whole = _wholeWord = true;
        }
        if (TooLongToSearch(end - start)) return;
        byte[] needle = Doc.ReadBytes(new ByteRange(start, end));
        int limit = CaretLimit;
        (long revision, int stamp) = (Doc.Revision, _caretStamp);
        var hits = await SearchAsync((text, cancel) =>
        {
            var list = new List<long>();
            ByteCarets.Find(text, needle, text.Bom, text.Length, whole, hit => { list.Add(hit); return list.Count <= limit; }, cancel);
            return list;
        });
        if (hits is not { Count: > 0 } || Doc.Revision != revision || _caretStamp != stamp) return;
        bool more = hits.Count > limit;
        if (more) hits.RemoveAt(hits.Count - 1);
        int at = hits.IndexOf(start);
        ApplyMoved(SelectionSet.Create([.. hits.Select(h => new TextSelection(h, h + needle.Length))], at < 0 ? hits.Count - 1 : at));
        if (more) Refused?.Invoke(Loc.Get(K.PagedCaretsLimit, ("limit", limit)));
    }

    private bool TooLongToSearch(long length)
    {
        if (length <= OccurrenceLimitBytes) return false;
        Refused?.Invoke(Loc.Get(K.PagedOccurrenceTooLong, ("limit", OpenDocument.FormatSize(OccurrenceLimitBytes))));
        return true;
    }

    /// <summary>
    /// Runs a search over the text of the current revision: on the UI thread for a small document, else on the pool over a snapshot, so the window keeps
    /// answering. Null when it was cancelled, failed (the message says why) or another search is still running.
    /// </summary>
    private async Task<List<long>?> SearchAsync(Func<ByteText, CancellationToken, List<long>> work)
    {
        if (_searching) return null;
        _searching = true;
        try
        {
            if (Doc.ByteLength <= InlineSearchBytes) return work(ByteText.Of(Doc), CancellationToken.None);
            var text = ByteText.Of(Doc.GetSnapshot(), Doc.BomLength);
            var cts = _searchCts = new CancellationTokenSource();
            return await Task.Run(() => work(text, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Refused?.Invoke(e.Message); // the file changed under the snapshot
            return null;
        }
        finally { _searching = false; }
    }

    // ---- editing ----

    /// <summary>Edits that can run: the file is not locked and its line index is complete.</summary>
    private bool CanEditDocument()
    {
        if (ReadOnlyReason is { } locked) { Refused?.Invoke(locked); return false; }
        if (Indexed) return true;
        Refused?.Invoke(Loc.Get(K.PagedIndexing));
        return false;
    }

    private bool SameCarets(SelectionSet a, SelectionSet b) => a.Primary == b.Primary && a.Items.SequenceEqual(b.Items);

    /// <summary>The key of the undo step of a run of typing or deleting: it goes on while the carets stand where the run left them.</summary>
    private string RunKey(string kind, SelectionSet before)
    {
        bool continues = _runKind == kind && _runRevision == Doc.Revision && _runCarets is { } last && SameCarets(last, before);
        if (!continues) _runId++;
        return $"paged-carets-{kind}#{_runId}";
    }

    /// <summary>Applies one edit per caret as one revision and one undo step, and puts the carets where the edits leave them.</summary>
    private bool EditCarets(List<CaretEdit> edits, SelectionSet before, string? kind)
    {
        var plan = ByteCarets.Assemble(before, edits);
        string? key = kind is null ? null : RunKey(kind, before);
        var (caret, anchor) = (_caret, _anchor);
        // the handlers of Changed read the caret while Apply runs: it already stands where the edit puts it
        (_caret, _anchor) = (plan.After.PrimarySelection.Caret, -1);
        _own = true;
        EditResult result = default;
        try { result = ByteCarets.Apply(Doc, before, plan, key); }
        catch (Exception e) when (e is NotSupportedException or OutOfMemoryException)
        {
            // the bytes the edits remove must be kept for Undo; Apply fails before it changes anything
            Refused?.Invoke(Loc.Get(K.PagedEditTooLarge));
        }
        finally
        {
            _own = false;
            if (!result.Applied) (_caret, _anchor) = (caret, anchor);
        }
        if (result.Conflict == ConflictReason.Saving) Refused?.Invoke(Loc.Get(K.StatusSaveBusy));
        if (!result.Applied) return false;
        SetCarets(plan.After);
        (_runKind, _runRevision, _runCarets) = (kind ?? "", Doc.Revision, plan.After);
        _lineMemo = (-1, -1, 0);
        UpdateScrollRange();
        RevealPrimary();
        RaiseViewChanged();
        return true;
    }

    /// <summary>Typed or pasted text replaces every selection and goes in at every caret. A character the encoding cannot hold is refused.</summary>
    private bool InsertAtCarets(string text, bool typing)
    {
        if (!CanEditDocument()) return false;
        byte[] bytes;
        try { bytes = Doc.Codec.Encode(text); }
        catch (UnrepresentableCharactersException)
        {
            Refused?.Invoke(Loc.Get(K.PagedNotEncodable, ("encoding", Doc.Codec.Name)));
            return false;
        }
        var before = AllCarets();
        return EditCarets(ByteCarets.PlanReplace(before, _ => bytes), before, typing && text.Length <= 16 ? "typing" : null);
    }

    /// <summary>Paste with several carets: one clipboard line per caret when the counts agree, otherwise the whole text at every caret.</summary>
    private bool PasteAtCarets(string text)
    {
        if (!CanEditDocument()) return false;
        var before = AllCarets();
        var pieces = MultiCaret.SplitForCarets(text, before.Count);
        byte[][] bytes;
        try { bytes = pieces is null ? [Doc.Codec.Encode(text)] : [.. pieces.Select(Doc.Codec.Encode)]; }
        catch (UnrepresentableCharactersException)
        {
            Refused?.Invoke(Loc.Get(K.PagedNotEncodable, ("encoding", Doc.Codec.Name)));
            return false;
        }
        long total = pieces is null ? (long)bytes[0].Length * before.Count : bytes.Sum(b => (long)b.Length);
        if (total > MultiPasteLimitBytes)
        {
            Refused?.Invoke(Loc.Get(K.PagedMultiPasteTooLarge, ("size", OpenDocument.FormatSize(total)), ("limit", OpenDocument.FormatSize(MultiPasteLimitBytes))));
            return false;
        }
        return EditCarets(ByteCarets.PlanReplace(before, i => pieces is null ? bytes[0] : bytes[i]), before, null);
    }

    private void DeleteAtCarets(bool backwards)
    {
        if (!CanEditDocument()) return;
        var before = AllCarets();
        EditCarets(ByteCarets.PlanDelete(ByteText.Of(Doc), before, backwards), before, "deleting");
    }

    /// <summary>Tab at every caret: spaces up to the next tab stop of its own column, or a tab character.</summary>
    private void TabAtCarets()
    {
        if (!CanEditDocument()) return;
        var before = AllCarets();
        int size = Math.Max(1, _options.InsertSpaces ? _options.IndentSize : _options.TabSize);
        var pieces = new byte[before.Count][];
        for (int i = 0; i < pieces.Length; i++)
        {
            int column = SiteAt(before.Items[i].Start) is { } site ? DisplayColumn(site) : 0;
            pieces[i] = Doc.Codec.Encode(_options.InsertSpaces ? new string(' ', size - column % size) : "\t");
        }
        EditCarets(ByteCarets.PlanReplace(before, i => pieces[i]), before, null);
    }

    // ---- clipboard ----

    /// <summary>Copies the selected texts of all carets, one per line, and with <paramref name="cut"/> removes them as one undo step; a caret without a selection stays.</summary>
    private async Task CopyAtCaretsAsync(bool cut)
    {
        var before = AllCarets();
        var ranges = before.Items.Where(s => !s.IsEmpty).Select(s => s.ToRange()).ToList();
        if (ranges.Count == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || cut && !CanEditDocument()) return;
        long total = ranges.Sum(r => r.Length);
        if (total > CopyLimitBytes)
        {
            Refused?.Invoke(Loc.Get(K.PagedCopyTooLarge, ("size", OpenDocument.FormatSize(total)), ("limit", OpenDocument.FormatSize(CopyLimitBytes))));
            return;
        }
        long revision = Doc.Revision;
        string newLine = _doc.NewLine(_options.ConfiguredEol);
        var masked = ranges.Select(r => _secrets.Touches(r.Start, r.End)).ToArray(); // a value never leaves the view by the clipboard, and a cut that touches one deletes nothing
        string text;
        if (total <= InlineCopyBytes) text = string.Join(newLine, ranges.Select(TextOf));
        else
        {
            var snapshot = Doc.GetSnapshot();
            var hidden = masked.Any(m => m) ? _secrets.Ranges() : null;
            try
            {
                text = await Task.Run(() => string.Join(newLine, ranges.Select((r, i) =>
                    masked[i] ? MaskedText(snapshot.Codec, snapshot.ReadBytes(r), r.Start, hidden!) : snapshot.ReadText(r))));
            }
            catch (IOException e)
            {
                Refused?.Invoke(e.Message); // the file changed under the snapshot
                return;
            }
        }
        await clipboard.SetTextAsync(text);
        Copied?.Invoke(text);
        if (cut && !masked.Any(m => m) && Doc.Revision == revision && SameCarets(AllCarets(), before)) EditCarets(ByteCarets.PlanDeleteSelected(before), before, null);
    }
}

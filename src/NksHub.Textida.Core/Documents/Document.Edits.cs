using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

public sealed partial class Document
{
    private readonly record struct Pending(long Start, long Deleted, ReadOnlyMemory<byte> Insert);
    private readonly record struct AppliedEdit(long Offset, byte[] Removed, byte[] Inserted);
    private sealed record Step(AppliedEdit[] Edits, IReadOnlyList<ByteRange>? Before, IReadOnlyList<ByteRange>? After, TextCodec? CodecBefore = null, TextCodec? CodecAfter = null, FillEdit? Fill = null);

    /// <summary>A fill kept as pattern and lengths; <see cref="Removed"/> is what the backend gave back for the bytes it replaced, null while the step is undone or after a save made it unusable.</summary>
    private sealed class FillEdit(long offset, long deleted, long length, byte[] pattern, object removed)
    {
        public long Offset { get; } = offset;
        public long Deleted { get; } = deleted;
        public long Length { get; } = length;
        public byte[] Pattern { get; } = pattern;
        public object? Removed { get; set; } = removed;
    }

    private sealed class UndoEntry(string? key)
    {
        public string? Key { get; } = key;
        public List<Step> Steps { get; } = [];
    }

    private readonly List<UndoEntry> _undo = [];
    private readonly List<UndoEntry> _redo = [];
    private UndoEntry? _saved;
    private bool _savedUnreachable;
    private int _groupDepth;
    private UndoEntry? _groupEntry;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    /// <summary>Number of undo steps; a journal uses it to tell a new undo step from an edit merged into the previous one.</summary>
    public int UndoDepth => _undo.Count;
    public int RedoDepth => _redo.Count;
    /// <summary>Coalesce key of the newest undo step; a journal replay joins edits to that step through it.</summary>
    internal string? TopUndoKey => _undo.Count > 0 ? _undo[^1].Key : null;

    /// <summary>True when the content differs from the last save (or the document was never saved).</summary>
    public bool IsModified => _savedUnreachable || (_undo.Count > 0 ? _undo[^1] : null) != _saved;

    // ---- edits ----

    public EditResult Insert(long offset, ReadOnlyMemory<byte> bytes, EditOptions? options = null) => Apply([ByteEdit.InsertAt(offset, bytes)], options);
    public EditResult Delete(ByteRange range, EditOptions? options = null) => Apply([ByteEdit.Delete(range)], options);
    public EditResult Replace(ByteRange range, ReadOnlyMemory<byte> bytes, EditOptions? options = null) => Apply([new ByteEdit(range, bytes)], options);

    /// <summary>
    /// Applies edits as one transaction (one revision, one undo step). Ranges are in <see cref="EditOptions.BaseRevision"/>
    /// (default: current); older ones are mapped through the change log. Nothing is applied when any edit conflicts.
    /// </summary>
    public EditResult Apply(IReadOnlyList<ByteEdit> edits, EditOptions? options = null)
    {
        ThrowIfDisposed();
        if (_saving) return EditResult.Fail(ConflictReason.Saving, _revision);
        options ??= new();
        long baseRevision = options.BaseRevision ?? _revision;
        var work = new List<Pending>(edits.Count);
        foreach (var edit in edits)
        {
            var mapped = MapRange(edit.Range, baseRevision);
            if (!mapped.Ok) return EditResult.Fail(mapped.Conflict, _revision);
            if (mapped.Range.IsEmpty && edit.Insert.IsEmpty) continue;
            work.Add(new(mapped.Range.Start, mapped.Range.Length, edit.Insert));
        }
        if (work.Count == 0) return new(true, _revision, ConflictReason.None);

        if (!IsInEditOrder(work)) work = [.. work.OrderBy(w => w.Start).ThenBy(w => w.Deleted == 0 ? 0 : 1)];
        for (int k = 0; k < work.Count; k++)
        {
            var w = work[k];
            if ((!options.RawBytes && w.Start < _bomLength) || w.Start + w.Deleted > ByteLength) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
            if (k > 0)
            {
                var p = work[k - 1];
                if (p.Start + p.Deleted > w.Start || (p.Deleted == 0 && w.Deleted == 0 && p.Start == w.Start))
                    return EditResult.Fail(ConflictReason.EditsOverlap, _revision);
            }
        }

        long tExec = TypingTrace.Begin();
        var removed = Execute(work, validate: !options.RawBytes, captureRemoved: true, out var failure);
        TypingTrace.End("doc.execute", tExec);
        if (failure != ConflictReason.None) return EditResult.Fail(failure, _revision);

        var step = new Step([.. work.Select((w, k) => new AppliedEdit(w.Start, removed![k], w.Insert.IsEmpty ? [] : w.Insert.ToArray()))], options.SelectionsBefore, options.SelectionsAfter);
        Commit(ChangeKind.Edit, options.Source, work, step, options.CoalesceKey);
        return new(true, _revision, ConflictReason.None);
    }

    /// <summary>
    /// Replaces <paramref name="range"/> by <paramref name="length"/> bytes of <paramref name="pattern"/> repeated from its first byte, as one undo step.
    /// Like the edits of the hex view it is byte-level: no BOM or character checks. A backend that keeps patterns takes a fill larger than a patch
    /// (<see cref="InPlacePatch.MaxPatchBytes"/>) without copying a byte, so the memory does not grow with the length; the undo of such a fill, and
    /// of the steps before it, ends when a save writes over the file the backend maps. Smaller fills are ordinary edits and keep their undo.
    /// </summary>
    public EditResult Fill(ByteRange range, ReadOnlyMemory<byte> pattern, long length, EditOptions? options = null)
    {
        ThrowIfDisposed();
        if (pattern.IsEmpty || length < 0) throw new ArgumentException("A fill needs a pattern of at least one byte and a length of zero or more.");
        if (_saving) return EditResult.Fail(ConflictReason.Saving, _revision);
        options ??= new();
        var mapped = MapRange(range, options.BaseRevision ?? _revision);
        if (!mapped.Ok) return EditResult.Fail(mapped.Conflict, _revision);
        var (start, end) = (mapped.Range.Start, mapped.Range.End);
        if (end > ByteLength) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
        if (start == end && length == 0) return new(true, _revision, ConflictReason.None);

        if (!_backend.RepeatsPatterns || (end - start <= InPlacePatch.MaxPatchBytes && length <= InPlacePatch.MaxPatchBytes))
        {
            if (length > Array.MaxLength) throw new NotSupportedException("This backend cannot hold a fill of more than 2 GiB.");
            var bytes = new byte[length];
            for (int at = 0; at < bytes.Length; at += pattern.Length) pattern.Span[..Math.Min(pattern.Length, bytes.Length - at)].CopyTo(bytes.AsSpan(at));
            return Apply([new ByteEdit(new ByteRange(start, end), bytes)], new EditOptions
            {
                Source = options.Source, CoalesceKey = options.CoalesceKey, RawBytes = true,
                SelectionsBefore = options.SelectionsBefore, SelectionsAfter = options.SelectionsAfter,
            });
        }

        byte[] copy = pattern.ToArray();
        var fill = new FillEdit(start, end - start, length, copy, _backend.ReplaceRepeated(start, end - start, copy, length));
        Commit(ChangeKind.Edit, options.Source, [new ChangeSet.Span(start, end - start, length) { Pattern = copy }],
            new Step([], options.SelectionsBefore, options.SelectionsAfter, Fill: fill), null);
        return new(true, _revision, ConflictReason.None);
    }

    /// <summary>After a save wrote over the mapped file, what fills removed by reference is gone: those steps and all before them can no longer be undone.</summary>
    private void ReleaseFillUndo()
    {
        int last = _undo.FindLastIndex(e => e.Steps.Any(s => s.Fill?.Removed is not null));
        if (last >= 0) _undo.RemoveRange(0, last + 1);
    }

    /// <summary>Applies disjoint ascending edits to the backend, highest offset first so lower offsets stay valid.</summary>
    private byte[][]? Execute(List<Pending> work, bool validate, bool captureRemoved, out ConflictReason failure)
    {
        failure = ConflictReason.None;
        byte[][]? removed = null;
        if (captureRemoved)
        {
            removed = new byte[work.Count][];
            for (int k = 0; k < work.Count; k++) removed[k] = ReadExact(work[k].Start, work[k].Deleted);
        }
        for (int k = work.Count - 1; k >= 0; k--)
        {
            var w = work[k];
            if (validate && !EditKeepsBoundaries(w))
            {
                for (int j = k + 1; j < work.Count; j++) _backend.Replace(work[j].Start, work[j].Insert.Length, removed![j]);
                failure = ConflictReason.InvalidRange;
                return null;
            }
            _backend.Replace(w.Start, w.Deleted, w.Insert.Span);
        }
        return removed;
    }

    private byte[] ReadExact(long offset, long length)
    {
        if (length > Array.MaxLength) throw new NotSupportedException("An edit that removes more than 2 GiB cannot be undone in memory.");
        if (length == 0) return [];
        var bytes = new byte[length];
        _backend.Read(offset, bytes);
        return bytes;
    }

    /// <summary>Both ends of the removed range must be boundaries now, and the join with the new bytes must not merge or split units.</summary>
    private bool EditKeepsBoundaries(Pending w)
    {
        long end = w.Start + w.Deleted;
        // one read around the edit serves both boundary checks and both sides of the join; a long removal reads its two ends apart
        if (w.Deleted > 64) return EditKeepsBoundariesApart(w, end);
        var (window, pos) = Window(w.Start, 4, (int)w.Deleted + 4);
        int endPos = pos + (int)w.Deleted;
        if (!BoundaryIn(w.Start, window, pos) || !BoundaryIn(end, window, endPos)) return false;
        int insertLength = w.Insert.Length, rightLength = window.Length - endPos;
        int total = pos + insertLength + rightLength;
        Span<byte> joined = total <= 256 ? stackalloc byte[total] : new byte[total];
        window.AsSpan(0, pos).CopyTo(joined);
        w.Insert.Span.CopyTo(joined[pos..]);
        window.AsSpan(endPos).CopyTo(joined[(pos + insertLength)..]);
        return _codec.IsBoundary(joined, pos) && _codec.IsBoundary(joined, pos + insertLength);
    }

    private bool BoundaryIn(long offset, byte[] window, int pos) =>
        offset >= _bomLength && offset <= ByteLength && (offset == _bomLength || offset == ByteLength || _codec.IsBoundary(window, pos));

    private bool EditKeepsBoundariesApart(Pending w, long end)
    {
        if (!IsCharBoundary(w.Start) || !IsCharBoundary(end)) return false;
        var (left, leftPos) = Window(w.Start, 4, 0);
        var (right, _) = Window(end, 0, 4);
        var joined = new byte[leftPos + w.Insert.Length + right.Length];
        left.CopyTo(joined, 0);
        w.Insert.Span.CopyTo(joined.AsSpan(leftPos));
        right.CopyTo(joined, leftPos + w.Insert.Length);
        return _codec.IsBoundary(joined, leftPos) && _codec.IsBoundary(joined, leftPos + w.Insert.Length);
    }

    /// <summary>True when the edits already stand in the order <see cref="Apply"/> wants: by start, an insertion before a removal at the same place.</summary>
    private static bool IsInEditOrder(List<Pending> work)
    {
        for (int i = 1; i < work.Count; i++)
        {
            var (a, b) = (work[i - 1], work[i]);
            if (a.Start > b.Start || (a.Start == b.Start && a.Deleted != 0 && b.Deleted == 0)) return false;
        }
        return true;
    }

    private void Commit(ChangeKind kind, string? source, List<Pending> work, Step? step, string? coalesceKey)
    {
        var spans = work.Select(w => new ChangeSet.Span(w.Start, w.Deleted, w.Insert.Length)).ToArray();
        Commit(kind, source, spans, step, coalesceKey);
    }

    private void Commit(ChangeKind kind, string? source, ChangeSet.Span[] spans, Step? step, string? coalesceKey)
    {
        if (kind != ChangeKind.Backend) _typingKind = TypingKind.None;
        var change = new ChangeSet(++_revision, kind, source, spans) { TouchesLineBreaks = step is null || step.Fill is not null || step.Edits.Any(e => HasLineBreakByte(e.Removed) || HasLineBreakByte(e.Inserted)) };
        if (spans.Any(s => s.Start < 4)) _bomLength = BomOf(_backend, _codec);
        _log.Add(change);
        if (_log.Count > _logCapacity) _log.RemoveRange(0, _log.Count - _logCapacity);
        long tAnchors = TypingTrace.Begin();
        UpdateAnchors(change);
        TypingTrace.End("doc.anchors", tAnchors);
        if (step is not null) PushUndo(step, coalesceKey);
        long tChanged = TypingTrace.Begin();
        var args = new DocumentChangedEventArgs(change);
        if (TypingTrace.Enabled && Changed is { } changed)
            foreach (var handler in changed.GetInvocationList())
            {
                long tHandler = TypingTrace.Begin();
                ((EventHandler<DocumentChangedEventArgs>)handler)(this, args);
                TypingTrace.End("handler " + handler.Method.DeclaringType?.Name + "." + handler.Method.Name, tHandler);
            }
        else Changed?.Invoke(this, args);
        TypingTrace.End("doc.changedEvent", tChanged);
    }

    private static bool HasLineBreakByte(byte[] bytes) => bytes.AsSpan().IndexOfAny((byte)10, (byte)13) >= 0;

    private const int TypingRunBytes = 16;
    private enum TypingKind { None, Insert, Remove }
    private TypingKind _typingKind;
    private long _typingEnd;
    private int _typingRun;

    /// <summary>
    /// Keystrokes of one run share an undo step: consecutive short insertions, or short removals (Backspace, Delete), at the
    /// position where the previous one ended. A paste, a replaced selection, another place or any other change starts a new step.
    /// </summary>
    private string? TypingKey(BackendEdit e)
    {
        var kind = e.Removed.Length == 0 && e.Inserted.Length is > 0 and <= TypingRunBytes ? TypingKind.Insert
            : e.Inserted.Length == 0 && e.Removed.Length is > 0 and <= TypingRunBytes ? TypingKind.Remove
            : TypingKind.None;
        bool continues = kind != TypingKind.None && kind == _typingKind && (kind == TypingKind.Insert
            ? e.Offset == _typingEnd
            : e.Offset == _typingEnd || e.Offset + e.Removed.Length == _typingEnd);
        if (kind != TypingKind.None && !continues) _typingRun++;
        _typingKind = kind;
        _typingEnd = kind == TypingKind.Insert ? e.Offset + e.Inserted.Length : e.Offset;
        return kind == TypingKind.None ? null : "typing#" + _typingRun;
    }

    private void OnBackendEdit(BackendEdit e)
    {
        var key = TypingKey(e);
        var step = new Step([new AppliedEdit(e.Offset, e.Removed, e.Inserted)], null, null);
        Commit(ChangeKind.Backend, "backend", [new ChangeSet.Span(e.Offset, e.Removed.Length, e.Inserted.Length)], step, key);
    }

    // ---- change log ----

    private long FirstReachableRevision => _log.Count == 0 ? _revision : _log[0].Revision - 1;

    /// <summary>Maps a range of an older revision to the current one; a conflict when a change touched it or the log is too short.</summary>
    public RangeMapResult MapRange(ByteRange range, long fromRevision)
    {
        if (fromRevision > _revision || fromRevision < 0) return new(false, range, ConflictReason.FutureRevision);
        if (fromRevision == _revision) return new(true, range, ConflictReason.None);
        if (fromRevision < FirstReachableRevision) return new(false, range, ConflictReason.LogTruncated);
        long start = range.Start, end = range.End;
        for (int i = (int)(fromRevision + 1 - _log[0].Revision); i < _log.Count; i++)
            if (!ChangeMap.TryMapRange(ref start, ref end, _log[i])) return new(false, range, ConflictReason.Overlap);
        return new(true, new ByteRange(start, end), ConflictReason.None);
    }

    /// <summary>
    /// Maps a region of an older revision to the current one the way a marker needs it (see <see cref="ChangeMap.TryMapRegion"/>): edits that touch the region
    /// change it instead of making it a conflict. Null when nothing of it is left or the change log no longer reaches back to <paramref name="fromRevision"/>.
    /// </summary>
    internal ByteRange? MapRegion(ByteRange range, long fromRevision)
    {
        if (fromRevision > _revision || fromRevision < 0 || fromRevision < FirstReachableRevision) return null;
        long start = range.Start, end = range.End;
        if (fromRevision < _revision)
            for (int i = (int)(fromRevision + 1 - _log[0].Revision); i < _log.Count; i++)
                if (!ChangeMap.TryMapRegion(ref start, ref end, _log[i])) return null;
        return new ByteRange(start, end);
    }

    /// <summary>Oldest revision whose ranges can still be mapped to the current one.</summary>
    public long OldestMappableRevision => FirstReachableRevision;

    // ---- anchors ----

    /// <summary>
    /// Creates an anchor at an offset of <paramref name="atRevision"/> (default: current). The result is invalid when the
    /// revision cannot be mapped to the current one or a change in between deleted the position.
    /// </summary>
    public Anchor CreateAnchor(long offset, AnchorGravity gravity = AnchorGravity.Left, long? atRevision = null)
    {
        long revision = atRevision ?? _revision;
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (revision == _revision && offset > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        bool valid = revision <= _revision && revision >= FirstReachableRevision;
        long pos = offset;
        if (valid && revision < _revision)
            for (int i = (int)(revision + 1 - _log[0].Revision); valid && i < _log.Count; i++)
                valid = ChangeMap.TryMapPoint(ref pos, gravity, _log[i]);
        var anchor = new Anchor(pos, gravity, valid);
        if (valid) _anchors.Add(new(anchor));
        return anchor;
    }

    private void UpdateAnchors(ChangeSet change)
    {
        int live = 0;
        var prefix = ChangeMap.ShiftPrefix(change);
        for (int i = 0; i < _anchors.Count; i++)
        {
            if (!_anchors[i].TryGetTarget(out var a) || !a.IsValid) continue;
            long pos = a.Offset;
            if (ChangeMap.TryMapPoint(ref pos, a.Gravity, change, prefix)) a.Offset = pos;
            else a.IsValid = false;
            if (a.IsValid) _anchors[live++] = _anchors[i];
        }
        _anchors.RemoveRange(live, _anchors.Count - live);
    }

    // ---- undo ----

    /// <summary>
    /// Groups every edit made until the returned scope is disposed into one undo step. Scopes nest; the outermost one decides.
    /// <paramref name="selectionsBefore"/> and <paramref name="selectionsAfter"/> (asked when the scope ends) are what undo and redo give back,
    /// for edits made through a text control that cannot pass them per edit.
    /// </summary>
    public IDisposable BeginUndoGroup(IReadOnlyList<ByteRange>? selectionsBefore = null, Func<IReadOnlyList<ByteRange>>? selectionsAfter = null)
    {
        _groupDepth++;
        return new GroupScope(this, selectionsBefore, selectionsAfter);
    }

    private sealed class GroupScope(Document owner, IReadOnlyList<ByteRange>? before, Func<IReadOnlyList<ByteRange>>? after) : IDisposable
    {
        private Document? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } d) return;
            _owner = null;
            if (--d._groupDepth != 0) return;
            if (d._groupEntry is { Steps.Count: > 0 } entry && (before is not null || after is not null))
            {
                if (before is not null) entry.Steps[0] = entry.Steps[0] with { Before = before };
                if (after is not null) entry.Steps[^1] = entry.Steps[^1] with { After = after() };
            }
            d._groupEntry = null;
        }
    }

    private void PushUndo(Step step, string? key)
    {
        if (_redo.Count > 0)
        {
            if (_saved is not null && _redo.Contains(_saved)) _savedUnreachable = true;
            _redo.Clear();
        }
        UndoEntry? top = _undo.Count > 0 ? _undo[^1] : null;
        if (_groupDepth > 0 && _groupEntry is { } group) Join(group, step);
        else if (_groupDepth == 0 && key is not null && top is not null && top.Key == key && top != _saved) Join(top, step);
        else
        {
            var entry = new UndoEntry(key);
            entry.Steps.Add(step);
            _undo.Add(entry);
            if (_groupDepth > 0) _groupEntry = entry;
        }
    }

    /// <summary>Adds a step to an entry. Undo gives back the selections before its first step and redo those after its last, so the others are not kept.</summary>
    private static void Join(UndoEntry entry, Step step)
    {
        if (entry.Steps.Count > 0 && entry.Steps[^1].After is not null) entry.Steps[^1] = entry.Steps[^1] with { After = null };
        entry.Steps.Add(step.Before is null ? step : step with { Before = null });
    }

    /// <summary>Reverts the last undo step and returns the selections it restored; null when there is nothing to undo.</summary>
    public UndoResult? Undo()
    {
        ThrowIfDisposed();
        if (_saving || _undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        for (int s = entry.Steps.Count - 1; s >= 0; s--)
        {
            if (entry.Steps[s].Fill is { } fill)
            {
                _backend.RestoreRemoved(fill.Offset, fill.Length, fill.Removed!);
                fill.Removed = null;
                Commit(ChangeKind.Undo, null, [new ChangeSet.Span(fill.Offset, fill.Length, fill.Deleted)], null, null);
                continue;
            }
            long shift = 0;
            var work = new List<Pending>();
            foreach (var e in entry.Steps[s].Edits)
            {
                work.Add(new(e.Offset + shift, e.Inserted.Length, e.Removed));
                shift += e.Inserted.Length - e.Removed.Length;
            }
            Execute(work, validate: false, captureRemoved: false, out _);
            if (entry.Steps[s].CodecBefore is { } before) SetCodec(before);
            Commit(ChangeKind.Undo, null, work, null, null);
        }
        return new(_revision, entry.Steps[0].Before ?? []);
    }

    /// <summary>Re-applies the last undone step.</summary>
    public UndoResult? Redo()
    {
        ThrowIfDisposed();
        if (_saving || _redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        foreach (var step in entry.Steps)
        {
            if (step.Fill is { } fill)
            {
                fill.Removed = _backend.ReplaceRepeated(fill.Offset, fill.Deleted, fill.Pattern, fill.Length);
                Commit(ChangeKind.Redo, null, [new ChangeSet.Span(fill.Offset, fill.Deleted, fill.Length) { Pattern = fill.Pattern }], null, null);
                continue;
            }
            var work = step.Edits.Select(e => new Pending(e.Offset, e.Removed.Length, e.Inserted)).ToList();
            Execute(work, validate: false, captureRemoved: false, out _);
            if (step.CodecAfter is { } after) SetCodec(after);
            Commit(ChangeKind.Redo, null, work, null, null);
        }
        return new(_revision, entry.Steps[^1].After ?? []);
    }

    private void MarkSaved()
    {
        if (FilePath is { } saved && _backend.BackingPath is { } mapped && SamePath(mapped, saved)) ReleaseFillUndo();
        _saved = _undo.Count > 0 ? _undo[^1] : null;
        _savedUnreachable = false;
        SavedRevision = _revision;
    }

    /// <summary>Revision of the last save; the revision the document was created or opened at (0) until then.</summary>
    public long SavedRevision { get; private set; }

    /// <summary>
    /// The ranges, in the current revision, that changes after <paramref name="revision"/> inserted or replaced; a deletion leaves an empty range at its place.
    /// Null when the change log no longer reaches back to that revision. Ranges are ascending and merged where they touch.
    /// </summary>
    public IReadOnlyList<ByteRange>? ChangedRangesSince(long revision)
    {
        if (revision > _revision || revision < 0 || revision < FirstReachableRevision) return null;
        var ranges = new List<(long Start, long End)>();
        if (revision == _revision) return [];
        for (int i = (int)(revision + 1 - _log[0].Revision); i < _log.Count; i++)
        {
            var spans = _log[i].Edits;
            for (int r = 0; r < ranges.Count; r++) ranges[r] = (ShiftStart(ranges[r].Start, spans), ShiftEnd(ranges[r].End, spans));
            long shift = 0;
            foreach (var span in spans)
            {
                long start = span.Start + shift;
                ranges.Add((start, start + span.Inserted));
                shift += span.Inserted - span.Deleted;
            }
            ranges = Merge(ranges);
        }
        return [.. ranges.Select(r => new ByteRange(r.Start, r.End))];
    }

    private static long ShiftStart(long p, IReadOnlyList<ChangeSet.Span> spans)
    {
        long shift = 0;
        foreach (var s in spans)
        {
            if (p >= s.Start + s.Deleted) shift += s.Inserted - s.Deleted;
            else if (p > s.Start) return s.Start + shift;
            else break;
        }
        return p + shift;
    }

    private static long ShiftEnd(long p, IReadOnlyList<ChangeSet.Span> spans)
    {
        long shift = 0;
        foreach (var s in spans)
        {
            if (p >= s.Start + s.Deleted) shift += s.Inserted - s.Deleted;
            else if (p > s.Start) return s.Start + shift + s.Inserted;
            else break;
        }
        return p + shift;
    }

    private static List<(long Start, long End)> Merge(List<(long Start, long End)> ranges)
    {
        ranges.Sort();
        var merged = new List<(long Start, long End)>(ranges.Count);
        foreach (var r in ranges)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
            else merged.Add(r);
        }
        return merged;
    }
}

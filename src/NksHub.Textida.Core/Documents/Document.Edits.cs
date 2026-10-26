using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

public sealed partial class Document
{
    private readonly record struct Pending(long Start, long Deleted, ReadOnlyMemory<byte> Insert);
    private readonly record struct AppliedEdit(long Offset, byte[] Removed, byte[] Inserted);
    private sealed record Step(AppliedEdit[] Edits, IReadOnlyList<ByteRange>? Before, IReadOnlyList<ByteRange>? After, TextCodec? CodecBefore = null, TextCodec? CodecAfter = null);

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

        work = [.. work.OrderBy(w => w.Start).ThenBy(w => w.Deleted == 0 ? 0 : 1)];
        for (int k = 0; k < work.Count; k++)
        {
            var w = work[k];
            if (w.Start < _bomLength || w.Start + w.Deleted > ByteLength) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
            if (k > 0)
            {
                var p = work[k - 1];
                if (p.Start + p.Deleted > w.Start || (p.Deleted == 0 && w.Deleted == 0 && p.Start == w.Start))
                    return EditResult.Fail(ConflictReason.EditsOverlap, _revision);
            }
        }

        var removed = Execute(work, validate: true, captureRemoved: true, out var failure);
        if (failure != ConflictReason.None) return EditResult.Fail(failure, _revision);

        var step = new Step([.. work.Select((w, k) => new AppliedEdit(w.Start, removed![k], w.Insert.ToArray()))], options.SelectionsBefore, options.SelectionsAfter);
        Commit(ChangeKind.Edit, options.Source, work, step, options.CoalesceKey);
        return new(true, _revision, ConflictReason.None);
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
        var bytes = new byte[length];
        if (length > 0) _backend.Read(offset, bytes);
        return bytes;
    }

    /// <summary>Both ends of the removed range must be boundaries now, and the join with the new bytes must not merge or split units.</summary>
    private bool EditKeepsBoundaries(Pending w)
    {
        long end = w.Start + w.Deleted;
        if (!IsCharBoundary(w.Start) || !IsCharBoundary(end)) return false;
        var (left, leftPos) = Window(w.Start, 4, 0);
        var (right, _) = Window(end, 0, 4);
        var joined = new byte[leftPos + w.Insert.Length + right.Length];
        left.CopyTo(joined, 0);
        w.Insert.Span.CopyTo(joined.AsSpan(leftPos));
        right.CopyTo(joined, leftPos + w.Insert.Length);
        return _codec.IsBoundary(joined, leftPos) && _codec.IsBoundary(joined, leftPos + w.Insert.Length);
    }

    private void Commit(ChangeKind kind, string? source, List<Pending> work, Step? step, string? coalesceKey)
    {
        var spans = work.Select(w => new ChangeSet.Span(w.Start, w.Deleted, w.Insert.Length)).ToArray();
        Commit(kind, source, spans, step, coalesceKey);
    }

    private void Commit(ChangeKind kind, string? source, ChangeSet.Span[] spans, Step? step, string? coalesceKey)
    {
        if (kind != ChangeKind.Backend) _typingKind = TypingKind.None;
        var change = new ChangeSet(++_revision, kind, source, spans);
        _log.Add(change);
        if (_log.Count > _logCapacity) _log.RemoveRange(0, _log.Count - _logCapacity);
        UpdateAnchors(change);
        if (step is not null) PushUndo(step, coalesceKey);
        Changed?.Invoke(this, new DocumentChangedEventArgs(change));
    }

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
        for (int i = 0; i < _anchors.Count; i++)
        {
            if (!_anchors[i].TryGetTarget(out var a) || !a.IsValid) continue;
            long pos = a.Offset;
            if (ChangeMap.TryMapPoint(ref pos, a.Gravity, change)) a.Offset = pos;
            else a.IsValid = false;
            if (a.IsValid) _anchors[live++] = _anchors[i];
        }
        _anchors.RemoveRange(live, _anchors.Count - live);
    }

    // ---- undo ----

    /// <summary>Groups every edit made until the returned scope is disposed into one undo step. Scopes nest.</summary>
    public IDisposable BeginUndoGroup()
    {
        _groupDepth++;
        return new GroupScope(this);
    }

    private sealed class GroupScope(Document owner) : IDisposable
    {
        private Document? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } d) return;
            _owner = null;
            if (--d._groupDepth == 0) d._groupEntry = null;
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
        if (_groupDepth > 0 && _groupEntry is { } group) group.Steps.Add(step);
        else if (_groupDepth == 0 && key is not null && top is not null && top.Key == key && top != _saved) top.Steps.Add(step);
        else
        {
            var entry = new UndoEntry(key);
            entry.Steps.Add(step);
            _undo.Add(entry);
            if (_groupDepth > 0) _groupEntry = entry;
        }
    }

    /// <summary>Reverts the last undo step and returns the selections it restored; null when there is nothing to undo.</summary>
    public UndoResult? Undo()
    {
        ThrowIfDisposed();
        if (_undo.Count == 0) return null;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        for (int s = entry.Steps.Count - 1; s >= 0; s--)
        {
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
        if (_redo.Count == 0) return null;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        foreach (var step in entry.Steps)
        {
            var work = step.Edits.Select(e => new Pending(e.Offset, e.Removed.Length, e.Inserted)).ToList();
            Execute(work, validate: false, captureRemoved: false, out _);
            if (step.CodecAfter is { } after) SetCodec(after);
            Commit(ChangeKind.Redo, null, work, null, null);
        }
        return new(_revision, entry.Steps[^1].After ?? []);
    }

    private void MarkSaved()
    {
        _saved = _undo.Count > 0 ? _undo[^1] : null;
        _savedUnreachable = false;
    }
}

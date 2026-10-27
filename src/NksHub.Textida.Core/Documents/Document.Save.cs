using System.Security.Cryptography;

namespace NksHub.Textida.Core.Documents;

/// <summary>How a save writes the bytes.</summary>
public enum SaveMode
{
    /// <summary>The changed bytes go into the existing file; the length does not change.</summary>
    InPlace,
    /// <summary>A whole new file is written next to the target and swapped in.</summary>
    Stream,
}

/// <summary>What a save would do right now: the way it writes and, for an in-place save, the ranges it touches.</summary>
public sealed record SavePlan(SaveMode Mode, IReadOnlyList<ByteRange> Ranges, long ChangedBytes);

public sealed partial class Document
{
    private bool _saving;
    private bool _editsDuringSave;
    private CancellationTokenSource? _saveCancel;

    /// <summary>Test seam of the in-place write.</summary>
    internal PatchHooks? InPlaceHooks;

    /// <summary>Test seam of the streamed save: called with the step (<c>temp-writing</c> on the worker before the first byte, <c>temp-written</c>, <c>before-swap</c>, <c>after-swap</c>, <c>saved</c>) and the path of the temporary file; a kill is simulated by ending the process there.</summary>
    internal Action<string, string>? StreamSaveStep;

    /// <summary>The state a save writes: what the document was when its snapshot was taken, which may be older than the document is when the save ends.</summary>
    private readonly record struct SaveMark(UndoEntry? Top, long Revision, long Length);

    /// <summary>
    /// True while a save works on a snapshot. The document takes no change meanwhile (<see cref="ConflictReason.Saving"/>), except edits
    /// while a whole new file is streamed by a backend that can keep them (<see cref="EditsDuringSave"/>).
    /// </summary>
    public bool IsSaving => _saving;

    /// <summary>True while a save runs and <see cref="Apply"/> still takes edits; they stay unsaved when the save ends.</summary>
    public bool EditsDuringSave => _saving && _editsDuringSave;

    /// <summary>
    /// How <see cref="Save"/> would write the document to its own file now: in place when only same-length changes happened since
    /// the last save, the file is mapped and the change is small enough for the journal; otherwise as a new file.
    /// </summary>
    public SavePlan PlanSave()
    {
        var ranges = FilePath is not null ? InPlaceRanges() : null;
        if (ranges is null) return new(SaveMode.Stream, [], 0);
        return new(SaveMode.InPlace, ranges, ranges.Sum(r => r.Length));
    }

    /// <summary>Ranges whose bytes differ from the saved file when no change since the last save moved a byte; null when that does not hold.</summary>
    private List<ByteRange>? InPlaceRanges()
    {
        if (Fingerprint is not { } print || FilePath is not { } path || _backend.BackingPath is not { } mapped || !SamePath(mapped, path)) return null;
        if (_baseline is not { IsSealed: true }) return null; // without a baseline nothing can vouch for the unchanged blocks; a streamed save builds a new one
        if (ByteLength != print.Size) return null;
        var spans = new List<(long Start, long End)>();
        if (SavedRevision != _revision)
        {
            if (SavedRevision < FirstReachableRevision) return null;
            for (int i = (int)(SavedRevision + 1 - _log[0].Revision); i < _log.Count; i++)
                foreach (var s in _log[i].Edits)
                {
                    if (s.Deleted != s.Inserted) return null;
                    if (s.Inserted > 0) spans.Add((s.Start, s.Start + s.Inserted));
                }
        }
        var merged = Merge(spans);
        if (merged.Sum(r => r.End - r.Start) > InPlacePatch.MaxPatchBytes) return null;
        return [.. merged.Select(r => new ByteRange(r.Start, r.End))];
    }

    /// <summary>Test seam: opens the file for the reads of an in-place save (block checks, read-back).</summary>
    internal Func<string, Stream>? FileReader;

    private Stream OpenForRead(string path) =>
        FileReader?.Invoke(path) ?? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);

    /// <summary>
    /// The external-change check of an in-place save: size, write time and file identity against the fingerprint, no content read. The content
    /// of the blocks the patch overwrites is checked right after against the baseline (<see cref="CheckOriginalBlocks"/>); a change elsewhere in the
    /// file that keeps size, time and identity is not seen on this path.
    /// </summary>
    private ExternalChange? CheckFileQuick(string target)
    {
        var actual = FileFingerprint.TryReadQuick(target);
        var expected = Fingerprint!;
        if (actual is not null && actual.Size == expected.Size && (_fileId is null || FileIdentity.TryGet(target) is not { } id || id == _fileId))
        {
            if (actual.LastWriteUtc == expected.LastWriteUtc) return null;
            // only the time moved (a touch, or the rollback of an aborted save): the content decides, as for any idle check; costs a pass over the file in this rare case
            if (expected.HasHash) return CheckExternalChange();
        }
        return ReportChange(target, actual, expected.Sha256);
    }

    /// <summary>The first <c>Save</c> branch: an in-place write on the calling thread.</summary>
    private SaveResult SaveInPlace(string target, SavePlan plan, bool guard)
    {
        var snapshot = _backend.CreateSnapshot();
        if (guard && CheckFileQuick(target) is { } late) return new(false, late);
        BeforeSaveCommit?.Invoke();
        var (digests, blockConflict) = CheckOriginalBlocks(target, plan, snapshot, guard, CancellationToken.None);
        if (blockConflict is not null) return new(false, blockConflict);
        Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, InPlaceIntent.Encode(plan.Ranges, snapshot), _revision));
        InPlacePatch.Apply(target, plan.Ranges, snapshot, CancellationToken.None, InPlaceHooks, _backend.BeginInPlaceWrite());
        if (VerifyWritten(target, plan, snapshot, digests) is { } differs) return new(false, differs);
        CompleteSave(target, "", AcceptDigests(digests));
        return new(true, null);
    }

    /// <summary>
    /// Before our own patch: every original block the patch touches, also the ones it hits only partly, is read from the file and compared with the
    /// baseline. A block that differs was changed behind the document (a conflict when <paramref name="guard"/>, otherwise the user chose to overwrite
    /// and the baseline is given up). The digests the blocks will have afterwards are derived from the verified old bytes plus our exact changes;
    /// they are accepted only after the write is verified. A writer that changes a block between this check and our write is not excluded without locking the file.
    /// </summary>
    private (Dictionary<long, byte[]>? Digests, ExternalChange? Conflict) CheckOriginalBlocks(string target, SavePlan plan, IByteSnapshot snapshot, bool guard, CancellationToken ct)
    {
        var baseline = _baseline!;
        var digests = new Dictionary<long, byte[]>();
        var block = new byte[OriginalBlockHashes.BlockSize];
        using var file = OpenForRead(target);
        bool differs = file.Length != ByteLength;
        foreach (var range in plan.Ranges)
            for (long start = range.Start / block.Length * block.Length; !differs && start < range.End; start += block.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (digests.ContainsKey(start)) continue;
                int n = (int)Math.Min(block.Length, ByteLength - start);
                file.Position = start;
                file.ReadExactly(block.AsSpan(0, n));
                if (baseline.CompareBlock(start, block.AsSpan(0, n)) != BlockCheck.Match) { differs = true; break; }
                foreach (var r in plan.Ranges)
                {
                    long from = Math.Max(r.Start, start), to = Math.Min(r.End, start + n);
                    for (long at = from; at < to;)
                    {
                        int got = snapshot.Read(at, block.AsSpan((int)(at - start), (int)(to - at)));
                        if (got == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                        at += got;
                    }
                }
                digests[start] = SHA256.HashData(block.AsSpan(0, n));
            }
        if (!differs) return (digests, null);
        return (null, guard ? ReportChange(target, FileFingerprint.TryReadQuick(target), Fingerprint!.Sha256) : null);
    }

    /// <summary>The baseline after a verified in-place write: the old one with the patched blocks replaced, or none when the old one could not be trusted.</summary>
    private OriginalBlockHashes? AcceptDigests(Dictionary<long, byte[]>? digests)
    {
        if (digests is null) return null;
        _baseline!.Replace(digests);
        return _baseline;
    }

    /// <summary>
    /// After an in-place write (flushed) only what was written is read back: each block the patch touched must hash to the digest derived before the
    /// write, or, when no digests exist (the user chose to overwrite a changed file), the written ranges must equal the snapshot. Something else changing
    /// those blocks behind the document, or a write that did not land, is reported and nothing is recorded as saved. The rest of the file is not read.
    /// </summary>
    private ExternalChange? VerifyWritten(string target, SavePlan plan, IByteSnapshot snapshot, Dictionary<long, byte[]>? digests)
    {
        bool ok;
        try
        {
            using var file = OpenForRead(target);
            ok = file.Length == ByteLength;
            var block = new byte[OriginalBlockHashes.BlockSize];
            var want = new byte[OriginalBlockHashes.BlockSize];
            if (ok && digests is not null)
            {
                foreach (var (start, digest) in digests)
                {
                    int n = (int)Math.Min(block.Length, ByteLength - start);
                    file.Position = start;
                    file.ReadExactly(block.AsSpan(0, n));
                    if (!SHA256.HashData(block.AsSpan(0, n)).AsSpan().SequenceEqual(digest)) { ok = false; break; }
                }
            }
            else if (ok)
            {
                foreach (var range in plan.Ranges)
                    for (long at = range.Start; ok && at < range.End;)
                    {
                        int n = (int)Math.Min(block.Length, range.End - at);
                        file.Position = at;
                        file.ReadExactly(block.AsSpan(0, n));
                        for (int done = 0; done < n;)
                        {
                            int got = snapshot.Read(at + done, want.AsSpan(done, n - done));
                            if (got == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                            done += got;
                        }
                        ok = block.AsSpan(0, n).SequenceEqual(want.AsSpan(0, n));
                        at += n;
                    }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ok = false;
        }
        return ok ? null : ReportChange(target, FileFingerprint.TryReadQuick(target), Fingerprint?.Sha256 ?? "");
    }

    private ExternalChange ReportChange(string target, FileFingerprint? actual, string hash)
    {
        var expected = Fingerprint ?? new FileFingerprint(ByteLength, default, hash);
        var change = new ExternalChange(Id, _revision, target, expected, actual, actual is null ? ExternalChangeKind.Missing : ExternalChangeKind.Modified);
        _lastReported = (change.Kind, actual?.Sha256);
        ExternalChangeDetected?.Invoke(this, change);
        return change;
    }

    private void CompleteSave(string target, string hash, OriginalBlockHashes? baseline, SaveMark? mark = null)
    {
        _baseline = baseline;
        _snapshot = null; // the cached snapshot predates the write; dropping it lets the preserved old bytes go
        FilePath = target;
        _hashCancel?.Cancel();
        _fileId = FileIdentity.TryGet(target);
        var print = Fingerprint = new FileFingerprint(mark?.Length ?? ByteLength, File.GetLastWriteTimeUtc(target), hash);
        _lastReported = null;
        MarkSaved(mark);
        Saved?.Invoke(this, EventArgs.Empty);
        if (!print.HasHash) HashInBackground(target, print);
    }

    /// <summary>
    /// Fills in the hash of a fingerprint taken after an in-place save. Runs on the thread pool, stops when another save, a dispose or a change of the
    /// fingerprint comes first, and discards its result when the file's write time moved while it was read.
    /// </summary>
    private void HashInBackground(string target, FileFingerprint pending)
    {
        var cts = _hashCancel = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            try
            {
                if (FileFingerprint.TryRead(target, cts.Token) is { } full && full.Size == pending.Size && full.LastWriteUtc == pending.LastWriteUtc)
                    Interlocked.CompareExchange(ref _fingerprint, pending with { Sha256 = full.Sha256 }, pending);
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>
    /// Saves like <see cref="Save"/>, but the bytes are written on a worker thread from a snapshot of the current revision, so the
    /// caller's thread stays free. The document takes no change until the task ends. Cancelling before the data is committed leaves the
    /// file as it was; the task then ends as cancelled. Call it from the thread that owns the document.
    /// </summary>
    public async Task<SaveResult> SaveAsync(string? path = null, bool overwriteExternalChanges = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_saving) throw new InvalidOperationException("A save is already running.");
        string target = Path.GetFullPath(path ?? FilePath ?? throw new InvalidOperationException("An unnamed document needs a path."));
        bool inPlace = FilePath is not null && SamePath(target, FilePath);
        bool guard = inPlace && !overwriteExternalChanges;
        _saving = true;
        _saveCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _saveCancel.Token;
        string? temp = null;
        bool streaming = false;
        try
        {
            var plan = inPlace ? PlanSave() : new SavePlan(SaveMode.Stream, [], 0);
            // the snapshot is taken before the first await, so the document takes edits (when it can) from the moment the save is called
            streaming = plan.Mode == SaveMode.Stream && _backend.EditsDuringStreamingSave;
            IByteSnapshot? snapshot = streaming ? _backend.BeginStreamingSave() : _backend.CreateSnapshot();
            var mark = new SaveMark(_undo.Count > 0 ? _undo[^1] : null, _revision, snapshot.ByteLength);
            if (streaming)
            {
                _joinBarrier = mark.Top;
                _editsDuringSave = true;
            }
            // an in-place save checks the file once, cheaply, before the blocks it overwrites; a new file checks before and after it is written
            if (guard && plan.Mode == SaveMode.Stream && await Task.Run(CheckExternalChange, CancellationToken.None) is { } early) return new(false, early);
            ct.ThrowIfCancellationRequested();

            string hash;
            OriginalBlockHashes? baseline;
            if (plan.Mode == SaveMode.InPlace)
            {
                try
                {
                    hash = "";
                    if (guard && await Task.Run(() => CheckFileQuick(target), CancellationToken.None) is { } late) return new(false, late);
                    ct.ThrowIfCancellationRequested();
                    BeforeSaveCommit?.Invoke();
                    var (digests, blockConflict) = await Task.Run(() => CheckOriginalBlocks(target, plan, snapshot!, guard, ct), CancellationToken.None);
                    if (blockConflict is not null) return new(false, blockConflict);
                    Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, InPlaceIntent.Encode(plan.Ranges, snapshot!), _revision));
                    await Task.Run(() => InPlacePatch.Apply(target, plan.Ranges, snapshot!, ct, InPlaceHooks, _backend.BeginInPlaceWrite()), CancellationToken.None);
                    if (await Task.Run(() => VerifyWritten(target, plan, snapshot!, digests), CancellationToken.None) is { } differs) return new(false, differs);
                    baseline = AcceptDigests(digests);
                }
                catch (SnapshotInvalidatedException) when (guard)
                {
                    // the file moved under the snapshot: what it holds now decides between a conflict and an error
                    if (await Task.Run(CheckExternalChange, CancellationToken.None) is { } changed) return new(false, changed);
                    throw;
                }
            }
            else
            {
                temp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                string tempPath = temp;
                try
                {
                    (hash, baseline) = await Task.Run(() => WriteTemp(snapshot!, tempPath, ct, StreamSaveStep), CancellationToken.None);
                    StreamSaveStep?.Invoke("temp-written", tempPath);
                }
                catch (SnapshotInvalidatedException) when (guard)
                {
                    if (await Task.Run(CheckExternalChange, CancellationToken.None) is { } changed) return new(false, changed);
                    throw;
                }
                if (guard && await Task.Run(CheckExternalChange, CancellationToken.None) is { } late) return new(false, late);
                ct.ThrowIfCancellationRequested();
                _snapshot = null;
                snapshot = null;
                Saving?.Invoke(this, new SavingEventArgs(target, mark.Length, hash, mark.Revision));
                StreamSaveStep?.Invoke("before-swap", tempPath);
                if (_backend.BackingPath is { } mapped && SamePath(mapped, target)) _backend.ReplaceBackingFile(tempPath, target);
                else FileSwap.Replace(tempPath, target);
                StreamSaveStep?.Invoke("after-swap", tempPath);
            }
            CompleteSave(target, hash, baseline, streaming ? mark : null);
            StreamSaveStep?.Invoke("saved", target);
            return new(true, null);
        }
        finally
        {
            if (streaming) _backend.EndStreamingSave();
            _editsDuringSave = false;
            _joinBarrier = null;
            _barrierLost = false;
            _saving = false;
            _saveCancel?.Dispose();
            _saveCancel = null;
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>Writes the snapshot to <paramref name="temp"/>; the new baseline is taken from the bytes written, not from the target afterwards.</summary>
    private static (string Hash, OriginalBlockHashes Blocks) WriteTemp(IByteSnapshot snapshot, string temp, CancellationToken ct, Action<string, string>? step)
    {
        using var fs = new FileStream(temp, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            PreallocationSize = snapshot.ByteLength,
            BufferSize = 0,
        });
        step?.Invoke("temp-writing", temp);
        var blocks = new OriginalBlockHashes();
        var hashing = new HashingStream(fs, blocks);
        var buf = new byte[1 << 20];
        for (long at = 0; at < snapshot.ByteLength;)
        {
            ct.ThrowIfCancellationRequested();
            int n = snapshot.Read(at, buf);
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            hashing.Write(buf.AsSpan(0, n));
            at += n;
        }
        fs.Flush(true);
        return (hashing.Finish(), blocks);
    }

    /// <summary>Asks a running <see cref="SaveAsync"/> to stop; it does so before the data is committed.</summary>
    public void CancelSave() => _saveCancel?.Cancel();
}

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
    private CancellationTokenSource? _saveCancel;

    /// <summary>Test seam of the in-place write.</summary>
    internal PatchHooks? InPlaceHooks;

    /// <summary>True while a save works on a snapshot; the document takes no change meanwhile (<see cref="ConflictReason.Saving"/>).</summary>
    public bool IsSaving => _saving;

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

    private static string HashSnapshot(IByteSnapshot snapshot, CancellationToken ct)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        for (long at = 0; at < snapshot.ByteLength;)
        {
            ct.ThrowIfCancellationRequested();
            int n = snapshot.Read(at, buf);
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            hash.AppendData(buf, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>The first <c>Save</c> branch: an in-place write on the calling thread.</summary>
    private SaveResult SaveInPlace(string target, SavePlan plan, bool guard)
    {
        var snapshot = _backend.CreateSnapshot();
        string hash = HashSnapshot(snapshot, CancellationToken.None);
        if (guard && CheckExternalChange() is { } late) return new(false, late);
        Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, hash));
        InPlacePatch.Apply(target, plan.Ranges, snapshot, CancellationToken.None, InPlaceHooks, _backend.BeginInPlaceWrite());
        if (VerifyWritten(target, hash) is { } differs) return new(false, differs);
        CompleteSave(target, hash);
        return new(true, null);
    }

    /// <summary>
    /// After an in-place write the file on disk is read once more: it must hash to what the revision is. A part of the file nobody wrote to can differ
    /// from what the document believes only when something else changed it behind the document; then nothing is recorded as saved, and the difference is reported.
    /// </summary>
    private ExternalChange? VerifyWritten(string target, string hash)
    {
        var actual = FileFingerprint.TryRead(target);
        if (actual is not null && actual.Size == ByteLength && actual.Sha256 == hash) return null;
        var expected = Fingerprint ?? new FileFingerprint(ByteLength, default, hash);
        var change = new ExternalChange(Id, _revision, target, expected, actual, actual is null ? ExternalChangeKind.Missing : ExternalChangeKind.Modified);
        _lastReported = (change.Kind, actual?.Sha256);
        ExternalChangeDetected?.Invoke(this, change);
        return change;
    }

    private void CompleteSave(string target, string hash)
    {
        _snapshot = null; // the cached snapshot predates the write; dropping it lets the preserved old bytes go
        FilePath = target;
        Fingerprint = new FileFingerprint(ByteLength, File.GetLastWriteTimeUtc(target), hash);
        _lastReported = null;
        MarkSaved();
        Saved?.Invoke(this, EventArgs.Empty);
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
        try
        {
            var plan = inPlace ? PlanSave() : new SavePlan(SaveMode.Stream, [], 0);
            // an in-place save checks the file once, after the hash; a new file checks before and after it is written
            if (guard && plan.Mode == SaveMode.Stream && await Task.Run(CheckExternalChange, CancellationToken.None) is { } early) return new(false, early);
            ct.ThrowIfCancellationRequested();

            IByteSnapshot? snapshot = _backend.CreateSnapshot();
            string hash;
            if (plan.Mode == SaveMode.InPlace)
            {
                try
                {
                    hash = await Task.Run(() => HashSnapshot(snapshot!, ct), CancellationToken.None);
                    if (guard && await Task.Run(CheckExternalChange, CancellationToken.None) is { } late) return new(false, late);
                    ct.ThrowIfCancellationRequested();
                    Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, hash));
                    await Task.Run(() => InPlacePatch.Apply(target, plan.Ranges, snapshot!, ct, InPlaceHooks, _backend.BeginInPlaceWrite()), CancellationToken.None);
                    if (await Task.Run(() => VerifyWritten(target, hash), CancellationToken.None) is { } differs) return new(false, differs);
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
                try { hash = await Task.Run(() => WriteTemp(snapshot!, tempPath, ct), CancellationToken.None); }
                catch (SnapshotInvalidatedException) when (guard)
                {
                    if (await Task.Run(CheckExternalChange, CancellationToken.None) is { } changed) return new(false, changed);
                    throw;
                }
                if (guard && await Task.Run(CheckExternalChange, CancellationToken.None) is { } late) return new(false, late);
                ct.ThrowIfCancellationRequested();
                _snapshot = null;
                snapshot = null;
                Saving?.Invoke(this, new SavingEventArgs(target, ByteLength, hash));
                if (_backend.BackingPath is { } mapped && SamePath(mapped, target)) _backend.ReplaceBackingFile(tempPath, target);
                else FileSwap.Replace(tempPath, target);
            }
            CompleteSave(target, hash);
            return new(true, null);
        }
        finally
        {
            _saving = false;
            _saveCancel?.Dispose();
            _saveCancel = null;
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string WriteTemp(IByteSnapshot snapshot, string temp, CancellationToken ct)
    {
        using var fs = new FileStream(temp, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            PreallocationSize = snapshot.ByteLength,
            BufferSize = 0,
        });
        var hashing = new HashingStream(fs);
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
        return hashing.Finish();
    }

    /// <summary>Asks a running <see cref="SaveAsync"/> to stop; it does so before the data is committed.</summary>
    public void CancelSave() => _saveCancel?.Cancel();
}

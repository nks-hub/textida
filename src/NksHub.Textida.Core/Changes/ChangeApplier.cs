using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// Applies a reviewed <see cref="ChangeProposal"/>. Every target is all or nothing: an open document gets all its accepted hunks in one
/// transaction (one revision, one undo step) and only when it is still at the proposal's revision; a file on disk is rewritten only
/// when its content is still the base of the proposal, from a temporary file moved into place, with every byte outside the hunks
/// copied verbatim and the replacement texts encoded in the file's own codec. A stale target is reported as a conflict and left alone,
/// the others go on. Disk files are journaled (see FORMAT.md) so that an interrupted batch can be finished or rolled back
/// (<see cref="BatchRecovery"/>). The call is synchronous and, because it edits documents, runs on the thread that owns them.
/// </summary>
/// <param name="findDocument">Resolves the document a proposal names; null when it is not open.</param>
/// <param name="journalDirectory">Directory of the batch journals; null applies disk files without crash recovery.</param>
public sealed class ChangeApplier(Func<Guid, Document?> findDocument, string? journalDirectory = null)
{
    /// <summary>Test seam called at named points of a batch ("plan", "before-swap:N", "after-swap:N", "after-file:N", "before-end").</summary>
    public Action<string>? FaultPoint { get; init; }

    /// <summary>One file of a batch: what the report names it, how many hunks were chosen of how many, and the plan that is journaled and applied.</summary>
    private sealed record Job(string Key, string DisplayName, int Selected, int Total, BatchFilePlan Plan);

    /// <summary>A document change checked against the open document: the document and the edits to write.</summary>
    private sealed record PreparedDocument(FileChange File, Document Doc, List<ByteEdit> Edits, int Chosen);

    public ApplyReport Apply(ChangeProposal proposal, ChangeSelection? selection = null)
    {
        selection ??= new();
        var outcomes = new Dictionary<string, FileOutcome>();
        var documents = new List<PreparedDocument>();
        var disk = new List<(FileChange File, IReadOnlyList<Hunk> Hunks)>();
        var operations = new List<FileChange>();
        foreach (var file in proposal.Files)
        {
            if (file.Operation is not null)
            {
                if (!selection.IsAccepted(file)) outcomes[file.Key] = new(file.Key, file.DisplayName, ChangeStatus.Rejected, ChangeConflict.None, 0, 1);
                else operations.Add(file);
                continue;
            }
            var chosen = selection.Selected(file);
            if (chosen.Count == 0) outcomes[file.Key] = new(file.Key, file.DisplayName, ChangeStatus.Rejected, ChangeConflict.None, 0, file.Hunks.Count);
            else if (!file.IsDocument) disk.Add((file, chosen));
            else if (PrepareDocument(file, chosen, out var prepared, out var failure)) documents.Add(prepared!);
            else outcomes[file.Key] = failure!;
        }
        var jobs = PrepareDisk(disk, outcomes);
        foreach (var operation in operations)
        {
            if (FileOperationRun.Check(operation) is { } blocked) outcomes[operation.Key] = Conflict(operation, blocked.Reason, 1, blocked.Message);
        }

        if (proposal.Atomic && outcomes.Values.Any(o => o.Status is ChangeStatus.Conflict or ChangeStatus.Failed))
        {
            NotApplied(documents, jobs, operations, outcomes);
            return Report(proposal, outcomes, null);
        }

        var run = new FileOperationRun();
        foreach (var operation in operations.Where(o => !outcomes.ContainsKey(o.Key)))
        {
            try
            {
                run.Do(operation);
                outcomes[operation.Key] = new(operation.Key, operation.DisplayName, ChangeStatus.Applied, ChangeConflict.None, 1, 0);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                string? undone = run.RollBack();
                foreach (var done in operations) outcomes.Remove(done.Key);
                NotApplied(documents, jobs, operations, outcomes);
                outcomes[operation.Key] = Failed(operation, 1, e.Message + (undone is null ? "" : " Undoing the earlier file operations failed too: " + undone));
                return Report(proposal, outcomes, null);
            }
        }

        foreach (var document in documents) outcomes[document.File.Key] = ApplyDocument(proposal, document);
        Guid? batch = jobs.Count > 0 ? RunJobs(proposal.Title, proposal.Origin, jobs, outcomes) : null;
        run.Commit();
        return Report(proposal, outcomes, batch);
    }

    private static ApplyReport Report(ChangeProposal proposal, Dictionary<string, FileOutcome> outcomes, Guid? batch) =>
        new(proposal.Id, [.. proposal.Files.Select(f => outcomes[f.Key])], batch);

    /// <summary>An atomic proposal that cannot go through: everything that has no outcome yet is reported as left alone.</summary>
    private static void NotApplied(List<PreparedDocument> documents, List<Job> jobs, List<FileChange> operations, Dictionary<string, FileOutcome> outcomes)
    {
        const string Message = "Not applied: another change of the batch is stale or cannot be done.";
        foreach (var d in documents) outcomes.TryAdd(d.File.Key, new(d.File.Key, d.File.DisplayName, ChangeStatus.Rejected, ChangeConflict.None, 0, d.Chosen, Message));
        foreach (var j in jobs) outcomes.TryAdd(j.Key, new(j.Key, j.DisplayName, ChangeStatus.Rejected, ChangeConflict.None, 0, j.Selected, Message));
        foreach (var o in operations) outcomes.TryAdd(o.Key, new(o.Key, o.DisplayName, ChangeStatus.Rejected, ChangeConflict.None, 0, 1, Message));
    }

    /// <summary>Why a file operation cannot be done now (the file is gone, changed, or the name is taken), or null when it can.</summary>
    public static (ChangeConflict Reason, string Message)? CheckOperation(FileChange change) => FileOperationRun.Check(change);

    private static FileOutcome Conflict(FileChange f, ChangeConflict reason, int hunks, string message) => Conflict(f.Key, f.DisplayName, reason, hunks, message);

    private static FileOutcome Conflict(string key, string name, ChangeConflict reason, int hunks, string message) =>
        new(key, name, ChangeStatus.Conflict, reason, 0, hunks, message);

    private bool PrepareDocument(FileChange file, IReadOnlyList<Hunk> chosen, out PreparedDocument? prepared, out FileOutcome? failure)
    {
        prepared = null;
        failure = null;
        if (file.DocumentId is not { } id || findDocument(id) is not { } doc) { failure = Conflict(file, ChangeConflict.DocumentUnavailable, chosen.Count, "The document is not open any more."); return false; }
        if (doc.Revision != file.BaseRevision) { failure = Conflict(file, ChangeConflict.StaleRevision, chosen.Count, $"The document changed (revision {file.BaseRevision} -> {doc.Revision}); make a new preview."); return false; }

        var edits = new List<ByteEdit>(chosen.Count);
        foreach (var h in chosen)
        {
            try
            {
                if (doc.ReadText(h.Range) != h.OldText) { failure = Conflict(file, ChangeConflict.StaleRevision, chosen.Count, "The text at a range is not the one in the preview."); return false; }
                edits.Add(new ByteEdit(h.Range, doc.Codec.Encode(h.NewText)));
            }
            catch (ArgumentException)
            {
                failure = Conflict(file, ChangeConflict.InvalidRange, chosen.Count, $"Range {h.Range} is not valid in the document.");
                return false;
            }
            catch (UnrepresentableCharactersException)
            {
                failure = Conflict(file, ChangeConflict.EncodingLoss, chosen.Count, $"The new text cannot be written in {doc.Codec.Name}.");
                return false;
            }
        }
        prepared = new PreparedDocument(file, doc, edits, chosen.Count);
        return true;
    }

    private static FileOutcome ApplyDocument(ChangeProposal proposal, PreparedDocument p)
    {
        var (file, doc, edits, chosen) = p;
        var result = doc.Apply(edits, new EditOptions { Source = proposal.Origin });
        if (!result.Applied)
        {
            var reason = result.Conflict is ConflictReason.FutureRevision or ConflictReason.LogTruncated or ConflictReason.Overlap ? ChangeConflict.StaleRevision : ChangeConflict.InvalidRange;
            return Conflict(file, reason, chosen, $"The document refused the edits ({result.Conflict}).");
        }
        return new(file.Key, file.DisplayName, ChangeStatus.Applied, ChangeConflict.None, chosen, file.Hunks.Count - chosen);
    }

    private static List<Job> PrepareDisk(List<(FileChange File, IReadOnlyList<Hunk> Hunks)> selected, Dictionary<string, FileOutcome> outcomes)
    {
        // 1. check every file against its base and encode the new texts; only intact files enter the batch
        var items = new List<Job>();
        foreach (var (file, hunks) in selected)
        {
            try
            {
                if (file.Path is null || file.BaseFingerprint is null) throw new ArgumentException($"File change '{file.Key}' has no path and fingerprint.");
                var bytes = BatchFiles.TryRead(BatchFiles.ResolveTarget(file.Path));
                if (bytes is null)
                {
                    outcomes[file.Key] = File.Exists(file.Path) ? Failed(file, hunks.Count, "The file cannot be read.") : Conflict(file, ChangeConflict.Missing, hunks.Count, "The file is gone.");
                    continue;
                }
                if (bytes.Length != file.BaseFingerprint.Size || BatchFiles.Sha(bytes) != file.BaseFingerprint.Sha256)
                {
                    outcomes[file.Key] = Conflict(file, ChangeConflict.ExternalChange, hunks.Count, "The file was changed by another program after the preview; make a new preview.");
                    continue;
                }
                var plan = Plan(items.Count, file, hunks, bytes);
                items.Add(new Job(file.Key, file.DisplayName, hunks.Count, file.Hunks.Count, plan));
            }
            catch (UnrepresentableCharactersException)
            {
                outcomes[file.Key] = Conflict(file, ChangeConflict.EncodingLoss, hunks.Count, $"The new text cannot be written in {file.Codec.Name}.");
            }
            catch (InvalidDataException e)
            {
                outcomes[file.Key] = Conflict(file, ChangeConflict.InvalidRange, hunks.Count, e.Message);
            }
        }
        return items;
    }

    /// <summary>
    /// Applies plans that are already made (an encoding batch): the same journal, the same checks and the same swap as a proposal.
    /// Each plan names the content it is based on; a file that no longer has it is a conflict and stays as it is.
    /// </summary>
    internal ApplyReport ApplyPlans(string title, string origin, IReadOnlyList<(string Key, string DisplayName, BatchFilePlan Plan)> plans)
    {
        var outcomes = new Dictionary<string, FileOutcome>();
        var jobs = plans.Select((p, i) => new Job(p.Key, p.DisplayName, p.Plan.Hunks.Count, p.Plan.Hunks.Count, p.Plan with { Index = i })).ToList();
        Guid? batch = RunJobs(title, origin, jobs, outcomes);
        return new ApplyReport(Guid.NewGuid(), [.. plans.Select(p => outcomes[p.Key])], batch);
    }

    private Guid? RunJobs(string title, string origin, List<Job> items, Dictionary<string, FileOutcome> outcomes)
    {
        if (items.Count == 0) return null;

        // 2. write the whole plan and sync it before the first file is touched
        BatchJournalWriter? journal = null;
        Guid batchId = Guid.NewGuid();
        if (journalDirectory is not null)
        {
            try
            {
                journal = BatchJournalWriter.Create(journalDirectory, batchId);
                journal.Begin(title, origin, items.Count);
                foreach (var item in items) journal.Plan(item.Plan);
                journal.Flush();
                if (journal.Fault is not null) throw new IOException("The journal could not be written.", journal.Fault);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                journal?.Dispose();
                foreach (var item in items) outcomes[item.Key] = Failed(item.Key, item.DisplayName, item.Selected, "The recovery journal could not be written, so nothing was changed: " + e.Message);
                return null;
            }
        }

        // 3. one file at a time: intent (synced), temporary file, move into place
        try
        {
            FaultPoint?.Invoke("plan");
            foreach (var item in items) outcomes[item.Key] = ApplyFile(item, batchId, journal);
            FaultPoint?.Invoke("before-end");
        }
        catch
        {
            journal?.Dispose(); // an interrupted batch keeps its journal for recovery
            throw;
        }
        if (journal is not null)
        {
            if (journal.Fault is null) journal.Complete();
            else journal.Dispose();
        }
        return journalDirectory is null ? null : batchId;
    }

    private static FileOutcome Failed(FileChange f, int hunks, string message) => Failed(f.Key, f.DisplayName, hunks, message);

    private static FileOutcome Failed(string key, string name, int hunks, string message) => new(key, name, ChangeStatus.Failed, ChangeConflict.None, 0, hunks, message);

    private static BatchFilePlan Plan(int index, FileChange file, IReadOnlyList<Hunk> hunks, byte[] bytes)
    {
        var list = new List<BatchHunk>(hunks.Count);
        long end = 0;
        foreach (var h in hunks.OrderBy(h => h.Range.Start))
        {
            if (h.Range.Start < end || h.Range.End > bytes.Length) throw new InvalidDataException($"Range {h.Range} overlaps another or lies outside the file.");
            list.Add(new BatchHunk(h.Range.Start, bytes.AsSpan((int)h.Range.Start, (int)h.Range.Length).ToArray(), file.Codec.Encode(h.NewText)));
            end = h.Range.End;
        }
        return new BatchFilePlan(index, file.Path!, file.Codec.Name, bytes.Length, BatchFiles.Sha(bytes), list);
    }

    private FileOutcome ApplyFile(Job item, Guid batchId, BatchJournalWriter? journal)
    {
        var plan = item.Plan;
        string target = BatchFiles.ResolveTarget(plan.Path);
        string temp = $"{target}.textida-{batchId.ToString("N")[..8]}-{plan.Index}.tmp";
        try
        {
            var stamp = File.GetLastWriteTimeUtc(target);
            var current = BatchFiles.TryRead(target);
            if (current is null || current.Length != plan.BaseSize || BatchFiles.Sha(current) != plan.BaseSha)
            {
                journal?.Skipped(plan.Index, "changed after the plan was made");
                return Conflict(item.Key, item.DisplayName, current is null ? ChangeConflict.Missing : ChangeConflict.ExternalChange, item.Selected, "The file changed while the batch ran.");
            }
            var content = BatchFiles.Apply(current, plan.Hunks);
            journal?.Intent(new BatchIntent(plan.Index, content.Length, BatchFiles.Sha(content), temp));
            journal?.Flush();
            if (journal?.Fault is { } fault) throw new IOException("The journal could not be written.", fault);

            bool changedMeanwhile = false;
            BatchFiles.WriteReplacing(target, temp, content, () =>
            {
                FaultPoint?.Invoke($"before-swap:{plan.Index}");
                var now = new FileInfo(target);
                if (now.Length != plan.BaseSize || now.LastWriteTimeUtc != stamp) changedMeanwhile = true;
                if (changedMeanwhile) throw new InvalidDataException("changed during the write");
            });
            FaultPoint?.Invoke($"after-swap:{plan.Index}");
            journal?.Done(plan.Index);
            FaultPoint?.Invoke($"after-file:{plan.Index}");
            return new(item.Key, item.DisplayName, ChangeStatus.Applied, ChangeConflict.None, item.Selected, item.Total - item.Selected);
        }
        catch (InvalidDataException)
        {
            journal?.Skipped(plan.Index, "changed during the write");
            return Conflict(item.Key, item.DisplayName, ChangeConflict.ExternalChange, item.Selected, "The file changed while the batch ran.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            journal?.Skipped(plan.Index, e.Message);
            return Failed(item.Key, item.DisplayName, item.Selected, e.Message);
        }
    }
}

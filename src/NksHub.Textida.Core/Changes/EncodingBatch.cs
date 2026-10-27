using System.IO.Enumeration;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Changes;

/// <summary>Why a file is not converted by a batch.</summary>
public enum BatchSkip
{
    None,
    /// <summary>A NUL byte in a text that is not UTF-16 or UTF-32.</summary>
    Binary,
    /// <summary>The encoding is only a guess (<see cref="EncodingGuess.IsUncertain"/>); converting from a wrong guess would corrupt the text.</summary>
    UncertainEncoding,
    /// <summary>The file already has exactly the bytes the conversion would write.</summary>
    Unchanged,
    Unreadable,
    /// <summary>Characters would be lost and no replacement was chosen.</summary>
    Lossy,
}

/// <summary>One file in the preview of a batch conversion.</summary>
/// <param name="Source">The encoding the file is read in (detected, or forced by the options); null for a file that cannot be read.</param>
/// <param name="Loss">Characters (and undecodable bytes) that the target cannot hold.</param>
/// <param name="NewSize">Size after the conversion.</param>
/// <param name="Sha">SHA-256 of the content the preview is based on.</param>
public sealed record EncodingBatchItem(string Path, long Size, TextCodec? Source, double Confidence, BatchSkip Skip, LossReport Loss, long NewSize, string Sha, string? Message = null)
{
    public bool HasLoss => Loss.HasLoss;
    /// <summary>The file can be converted: without loss, or with loss once a replacement is chosen.</summary>
    public bool CanConvert => Skip == BatchSkip.None;
}

public sealed record EncodingBatchOptions
{
    public required TextCodec Target { get; init; }
    /// <summary>Read every file in this encoding instead of detecting it.</summary>
    public TextCodec? Source { get; init; }
    /// <summary>File name masks (<c>*</c> and <c>?</c>); empty takes every file the walk finds.</summary>
    public IReadOnlyList<string> Masks { get; init; } = [];
    /// <summary>The walk; files above the preview limit of <see cref="ChangeBuilder.MaxPlanBytes"/> are left out and counted in the walk statistics.</summary>
    public FileWalkOptions Walk { get; init; } = new() { MaxFileSize = ChangeBuilder.MaxPlanBytes };
    public int Parallelism { get; init; } = Environment.ProcessorCount;
}

/// <summary>The preview of a batch conversion: every file with what it would lose, before anything is written.</summary>
public sealed class EncodingBatchPlan
{
    internal EncodingBatchPlan(TextCodec target, TextCodec? source, IReadOnlyList<EncodingBatchItem> items, FileWalkStats walk)
    {
        Target = target;
        Source = source;
        Items = items;
        Walk = walk;
    }

    public TextCodec Target { get; }
    public TextCodec? Source { get; }
    public IReadOnlyList<EncodingBatchItem> Items { get; }
    public FileWalkStats Walk { get; }
    /// <summary>Files that convert without losing anything.</summary>
    public IEnumerable<EncodingBatchItem> Clean => Items.Where(i => i.CanConvert && !i.HasLoss);
    /// <summary>Files that lose characters; they convert only with a replacement.</summary>
    public IEnumerable<EncodingBatchItem> Lossy => Items.Where(i => i.CanConvert && i.HasLoss);
    public IEnumerable<EncodingBatchItem> Skipped => Items.Where(i => !i.CanConvert);
}

/// <param name="Files">The outcome of every file that was tried; a file the preview excluded or that could not run is in <see cref="NotRun"/>.</param>
/// <param name="BatchId">Id of the recovery journal; null when no journal directory was given or nothing was written.</param>
/// <param name="Ran">The files that were written or tried, with the encoding they were read in.</param>
public sealed record EncodingBatchResult(IReadOnlyList<FileOutcome> Files, IReadOnlyList<EncodingBatchItem> NotRun, IReadOnlyList<EncodingBatchItem> Ran, Guid? BatchId)
{
    public int Converted => Files.Count(f => f.Status == ChangeStatus.Applied);
    public int Conflicts => Files.Count(f => f.Status == ChangeStatus.Conflict);
    public int Failed => Files.Count(f => f.Status == ChangeStatus.Failed);
}

/// <summary>
/// Converts the encoding of many files (F016). <see cref="Plan(string, EncodingBatchOptions, CancellationToken)"/> reads every file and lists what each one
/// would lose; <see cref="Run"/> then writes the chosen ones through the journal of the other batch operations (<see cref="ChangeApplier"/>), so an
/// interrupted run is finished or rolled back with <see cref="BatchRecovery"/>. Each file is replaced as a whole or not at all, and only when it still
/// has the content the preview was made from. A guessed encoding is never converted from unless the options name the source encoding.
/// </summary>
public static class EncodingBatch
{
    private const int SampleSize = 1 << 20;
    private const int NulProbe = 8000;

    public static EncodingBatchPlan Plan(string root, EncodingBatchOptions options, CancellationToken ct = default)
    {
        var stats = new FileWalkStats();
        var paths = FileWalker.Walk(root, options.Walk, stats, ct).Select(e => e.Path).Where(p => Matches(p, options.Masks)).ToList();
        return Plan(paths, options, stats, ct);
    }

    /// <summary>The preview for explicit files (for example the open documents that have a path).</summary>
    public static EncodingBatchPlan Plan(IReadOnlyList<string> paths, EncodingBatchOptions options, FileWalkStats? walk = null, CancellationToken ct = default)
    {
        var items = new EncodingBatchItem[paths.Count];
        Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism), CancellationToken = ct }, i =>
        {
            items[i] = Analyze(paths[i], options, "?").Item;
        });
        return new EncodingBatchPlan(options.Target, options.Source, items, walk ?? new FileWalkStats());
    }

    private static bool Matches(string path, IReadOnlyList<string> masks)
    {
        if (masks.Count == 0) return true;
        string name = Path.GetFileName(path);
        return masks.Any(m => FileSystemName.MatchesSimpleExpression(m, name, ignoreCase: OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()));
    }

    private static (EncodingBatchItem Item, BatchHunk? Hunk, byte[]? Bytes) Analyze(string path, EncodingBatchOptions options, string replacement)
    {
        var bytes = BatchFiles.TryRead(BatchFiles.ResolveTarget(path));
        if (bytes is null) return (Blank(path, BatchSkip.Unreadable, "The file cannot be read."), null, null);
        return Analyze(path, bytes, options, replacement);
    }

    private static EncodingBatchItem Blank(string path, BatchSkip skip, string? message, long size = 0, TextCodec? source = null, double confidence = 0, string sha = "") =>
        new(path, size, source, confidence, skip, LossReport.None, size, sha, message);

    private static (EncodingBatchItem Item, BatchHunk? Hunk, byte[]? Bytes) Analyze(string path, byte[] bytes, EncodingBatchOptions options, string replacement)
    {
        string sha = BatchFiles.Sha(bytes);
        TextCodec source;
        double confidence = 1;
        if (options.Source is { } forced) source = forced;
        else
        {
            var sample = bytes.AsSpan(0, Math.Min(bytes.Length, SampleSize));
            var guess = EncodingGuesser.Guess(sample, bytes.Length <= SampleSize);
            bool nul = sample[..Math.Min(sample.Length, NulProbe)].Contains((byte)0);
            if (nul && guess.Codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32)) return (Blank(path, BatchSkip.Binary, null, bytes.Length, null, 0, sha), null, bytes);
            source = guess.Codec;
            confidence = guess.Confidence;
            if (guess.IsUncertain)
                return (Blank(path, BatchSkip.UncertainEncoding, guess.Reason, bytes.Length, source, confidence, sha), null, bytes);
        }
        var plan = ByteFidelity.PlanConvert(source.Decode(bytes), options.Target, replacement);
        var converted = plan.Assemble();
        var diff = ByteDiff.Compute(bytes, converted);
        var item = new EncodingBatchItem(path, bytes.Length, source, confidence, diff.IsEmpty && !plan.Loss.HasLoss ? BatchSkip.Unchanged : BatchSkip.None, plan.Loss, converted.Length, sha);
        if (diff.IsEmpty) return (item, null, bytes);
        var hunk = new BatchHunk(diff.FirstDifference, bytes.AsSpan((int)diff.FirstDifference, (int)diff.OldChanged).ToArray(), converted.AsSpan((int)diff.FirstDifference, (int)diff.NewChanged).ToArray());
        return (item, hunk, bytes);
    }

    /// <summary>
    /// Writes the conversion. Files that lose characters are converted only when <paramref name="replacement"/> is given (the text written for each lost character);
    /// without it they are left alone and reported in <see cref="EncodingBatchResult.NotRun"/>. <paramref name="exclude"/> names paths the user took out of the preview.
    /// A file that changed since the preview is a conflict and stays as it is.
    /// </summary>
    /// <param name="journalDirectory">Where the recovery journal goes; null runs without crash recovery.</param>
    /// <param name="faultPoint">Test seam, see <see cref="ChangeApplier.FaultPoint"/>.</param>
    public static EncodingBatchResult Run(EncodingBatchPlan plan, string? replacement, string? journalDirectory, IReadOnlySet<string>? exclude = null, Action<string>? faultPoint = null)
    {
        var options = new EncodingBatchOptions { Target = plan.Target };
        var notRun = new List<EncodingBatchItem>();
        var ran = new List<EncodingBatchItem>();
        var outcomes = new List<FileOutcome>();
        var jobs = new List<(string Key, string DisplayName, BatchFilePlan Plan)>();
        foreach (var item in plan.Items)
        {
            if (!item.CanConvert || exclude?.Contains(item.Path) == true) { notRun.Add(item); continue; }
            if (item.HasLoss && replacement is null) { notRun.Add(item with { Skip = BatchSkip.Lossy }); continue; }
            var now = BatchFiles.TryRead(BatchFiles.ResolveTarget(item.Path));
            if (now is null || BatchFiles.Sha(now) != item.Sha)
            {
                outcomes.Add(new FileOutcome(item.Path, item.Path, ChangeStatus.Conflict, now is null ? ChangeConflict.Missing : ChangeConflict.ExternalChange, 0, 1, "The file changed after the preview."));
                continue;
            }
            var (fresh, hunk, _) = Analyze(item.Path, now, options with { Source = item.Source }, replacement ?? "?");
            if (hunk is null || fresh.Skip != BatchSkip.None) { notRun.Add(fresh); continue; }
            jobs.Add((item.Path, item.Path, new BatchFilePlan(0, item.Path, item.Source!.Name, now.Length, item.Sha, [hunk])));
            ran.Add(fresh);
        }
        Guid? batchId = null;
        if (jobs.Count > 0)
        {
            var report = new ChangeApplier(_ => null, journalDirectory) { FaultPoint = faultPoint }.ApplyPlans($"Convert to {plan.Target.Name}", "encoding", jobs);
            outcomes.AddRange(report.Files);
            batchId = report.BatchId;
        }
        return new EncodingBatchResult(outcomes, notRun, ran, batchId);
    }
}

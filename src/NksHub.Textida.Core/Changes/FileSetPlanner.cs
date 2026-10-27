using System.Collections.Concurrent;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// The text of one source of a file set as the producer sees it. <paramref name="Path"/> is null for a document that has no file yet; <paramref name="HasInvalidBytes"/> says the file holds bytes
/// the codec cannot read, which a producer that must write the text back as bytes (a formatter) cannot keep.
/// </summary>
public sealed record FileSetText(string Name, string? Path, string Text, TextCodec Codec, int BomLength, bool HasInvalidBytes, string NewLine);

/// <param name="Edits">Parts of <see cref="FileSetText.Text"/> to replace, in characters, ascending and disjoint; none leaves the source out of the plan.</param>
/// <param name="Problem">Why the source cannot be planned; it is reported and left out, the other sources go on.</param>
public sealed record FileSetEdits(IReadOnlyList<(int Start, int End, string NewText)> Edits, string? Problem = null);

public delegate Task<FileSetEdits> FileSetProducer(FileSetText file, CancellationToken ct);

/// <summary>
/// Plans one change over a set of files and open documents with a producer of edits (a recipe). Each source is read fresh, decoded with its own encoding, given to the producer and turned into the same
/// <see cref="FileChange"/> that Replace in Files makes, so the preview, the revision checks, the journal and the per-file conflicts are those of <see cref="ChangeApplier"/>. A source that cannot be read or planned
/// is named in <see cref="ReplacePlan.Skipped"/> and does not stop the others. The proposal is not atomic: a conflict in one file leaves only that file out.
/// </summary>
public static class FileSetPlanner
{
    public static async Task<ReplacePlan> PlanAsync(string title, string origin, IEnumerable<string> paths, IEnumerable<LiveBuffer> buffers, FileSetProducer producer,
        int parallelism = 4, CancellationToken ct = default)
    {
        var live = buffers.ToList();
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var covered = live.Where(b => b.Path is not null).Select(b => Path.GetFullPath(b.Path!)).ToHashSet(comparer);
        var disk = paths.Select(Path.GetFullPath).Where(p => !covered.Contains(p)).Distinct(comparer).ToList();

        var files = new ConcurrentBag<FileChange>();
        var skipped = new ConcurrentBag<string>();
        var sources = live.Select(b => (Buffer: (LiveBuffer?)b, Path: (string?)null)).Concat(disk.Select(p => (Buffer: (LiveBuffer?)null, Path: (string?)p)));
        await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct }, async (source, token) =>
        {
            string name = source.Buffer?.Path ?? source.Buffer?.Title ?? source.Path!;
            try
            {
                var (change, problem) = await PlanSource(source.Buffer, source.Path, producer, token);
                if (problem is not null) skipped.Add($"{name}: {problem}");
                else if (change is not null) files.Add(change);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                skipped.Add($"{name}: {e.Message}");
            }
        });
        var proposal = new ChangeProposal(Guid.NewGuid(), title, origin, [.. files.OrderBy(f => f.Key, StringComparer.Ordinal)]);
        return new ReplacePlan(proposal, [.. skipped.Order(StringComparer.Ordinal)]);
    }

    private static async Task<(FileChange? Change, string? Problem)> PlanSource(LiveBuffer? buffer, string? path, FileSetProducer producer, CancellationToken ct)
    {
        byte[] bytes;
        TextCodec codec;
        FileFingerprint? print = null;
        if (buffer is not null)
        {
            bytes = ChangeBuilder.ReadAll(buffer.Snapshot);
            codec = buffer.Snapshot.Codec;
        }
        else
        {
            (bytes, print) = ChangeBuilder.ReadFile(path!);
            codec = ContentScanner.Detect(bytes, bytes.Length) ?? throw new NotSupportedException("The file looks binary.");
        }

        var dt = codec.Decode(bytes);
        string newLine = Eol.ForNewLine(Eol.Analyze(dt.Text));
        string? filePath = buffer?.Path ?? path;
        var result = await producer(new FileSetText(filePath ?? buffer!.Title, filePath, dt.Text, codec, dt.BomLength, dt.InvalidCount > 0, newLine), ct);
        if (result.Problem is not null) return (null, result.Problem);
        if (result.Edits.Count == 0) return (null, null);

        if (buffer is { } b)
            return (ChangeBuilder.FromChars(b.Snapshot.DocumentId.ToString(), b.Path, b.Snapshot.DocumentId, b.Snapshot.Revision, null, codec, dt, result.Edits), null);
        return (ChangeBuilder.FromChars(path!, path, null, null, print, codec, dt, result.Edits), null);
    }
}

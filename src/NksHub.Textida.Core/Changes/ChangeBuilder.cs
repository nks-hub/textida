using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Changes;

/// <summary>A replacement of a byte range of the base content with new text.</summary>
public readonly record struct TextReplacement(ByteRange Range, string NewText);

/// <summary>Builds <see cref="FileChange"/> and <see cref="ChangeProposal"/> values for producers other than Replace in Files (recipes, plugins, agents).</summary>
public static class ChangeBuilder
{
    /// <summary>Content larger than this is not previewed: the preview decodes the whole content with a character map.</summary>
    public const long MaxPlanBytes = 64L << 20;

    private const int ContextChars = 120;

    public static ChangeProposal Proposal(string title, string origin, params FileChange[] files) => new(Guid.NewGuid(), title, origin, files);

    /// <summary>A change for an open document, based on its current revision (read from a snapshot). Ranges must be character boundaries of that revision.</summary>
    public static FileChange ForDocument(Document doc, IEnumerable<TextReplacement> edits)
    {
        var snap = doc.GetSnapshot();
        return FromBytes(doc.Id.ToString(), doc.FilePath, doc.Id, snap.Revision, null, snap.Codec, ReadAll(snap), edits);
    }

    /// <summary>A change for a file, based on the content read now. The codec defaults to the detected one.</summary>
    public static FileChange ForFile(string path, IEnumerable<TextReplacement> edits, TextCodec? codec = null)
    {
        path = Path.GetFullPath(path);
        var (bytes, print) = ReadFile(path);
        codec ??= ContentScanner.Detect(bytes, bytes.Length) ?? throw new InvalidOperationException("The file looks binary.");
        return FromBytes(path, path, null, null, print, codec, bytes, edits);
    }

    internal static byte[] ReadAll(DocumentSnapshot snap)
    {
        if (snap.ByteLength > MaxPlanBytes) throw new NotSupportedException("The document is too large for a change preview.");
        return snap.ReadBytes(new ByteRange(0, snap.ByteLength));
    }

    internal static (byte[] Bytes, FileFingerprint Print) ReadFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        if (fs.Length > MaxPlanBytes) throw new NotSupportedException("The file is too large for a change preview.");
        var time = File.GetLastWriteTimeUtc(path);
        var bytes = new byte[fs.Length];
        fs.ReadExactly(bytes);
        return (bytes, new FileFingerprint(bytes.Length, time, Convert.ToHexString(SHA256.HashData(bytes))));
    }

    internal static FileChange FromBytes(string key, string? path, Guid? docId, long? revision, FileFingerprint? print, TextCodec codec, byte[] bytes, IEnumerable<TextReplacement> edits)
    {
        var dt = codec.Decode(bytes);
        var chars = new List<(int, int, string)>();
        foreach (var e in edits.OrderBy(e => e.Range.Start).ThenBy(e => e.Range.End))
        {
            if (e.Range.End > bytes.Length || e.Range.Start < dt.BomLength) throw new ArgumentException($"Range {e.Range} is outside the text.", nameof(edits));
            int cs = dt.CharIndexOf(e.Range.Start), ce = dt.CharIndexOf(e.Range.End);
            if (dt.ByteOffsetOf(cs) != e.Range.Start || dt.ByteOffsetOf(ce) != e.Range.End || !dt.IsCharBoundary(cs) || !dt.IsCharBoundary(ce))
                throw new ArgumentException($"Range {e.Range} does not fall on character boundaries.", nameof(edits));
            if (chars.Count > 0 && chars[^1].Item2 > cs) throw new ArgumentException("Ranges overlap.", nameof(edits));
            chars.Add((cs, ce, e.NewText));
        }
        return FromChars(key, path, docId, revision, print, codec, dt, chars);
    }

    /// <summary>Hunks from edits in characters of the decoded text, ascending and disjoint.</summary>
    internal static FileChange FromChars(string key, string? path, Guid? docId, long? revision, FileFingerprint? print, TextCodec codec, DecodedText dt, IReadOnlyList<(int Start, int End, string NewText)> edits)
    {
        var text = dt.Text.AsSpan();
        var hunks = new List<Hunk>(edits.Count);
        int cursor = 0, line = 1;
        foreach (var (cs, ce, newText) in edits)
        {
            line += text[cursor..cs].Count('\n');
            cursor = cs;
            int lineStart = text[..cs].LastIndexOf('\n') + 1;
            int lf = text[ce..].IndexOf('\n');
            int lineEnd = lf < 0 ? text.Length : ce + lf;
            if (lineEnd > ce && text[lineEnd - 1] == '\r') lineEnd--;
            string before = text[lineStart..cs].ToString(), after = text[ce..Math.Max(ce, lineEnd)].ToString();
            if (before.Length > ContextChars) before = before[^ContextChars..];
            if (after.Length > ContextChars) after = after[..ContextChars];

            string? problem = null;
            if (dt.InvalidCount > 0 && Enumerable.Range(cs, ce - cs).Any(dt.IsInvalid)) problem = "The text to replace contains bytes that are not valid " + codec.Name + ".";
            else if (!Encodable(codec, newText)) problem = "The new text has characters that cannot be written in " + codec.Name + ".";
            hunks.Add(new Hunk(hunks.Count, new ByteRange(dt.ByteOffsetOf(cs), dt.ByteOffsetOf(ce)), dt.Text.Substring(cs, ce - cs), newText, line, before, after, problem));
        }
        return new FileChange(key, path, docId, revision, print, codec, hunks);
    }

    private static bool Encodable(TextCodec codec, string text)
    {
        for (int i = 0; i < text.Length;)
        {
            bool ok = Rune.DecodeFromUtf16(text.AsSpan(i), out Rune r, out int used) == System.Buffers.OperationStatus.Done;
            if (!codec.CanEncode(ok ? r.Value : text[i])) return false;
            i += Math.Max(1, used);
        }
        return true;
    }
}

/// <summary>The preview of a replace and the sources that could not be previewed (binary, too large, pattern too slow) with the reason.</summary>
public sealed record ReplacePlan(ChangeProposal Proposal, IReadOnlyList<string> Skipped);

/// <summary>
/// Plans Replace in Files: finds the sources with a match (search worker, so ignore rules, encodings and open documents behave as in
/// the search), reads each fresh and produces one hunk per match. A regex replacement may use <c>$1</c>, <c>${name}</c> and <c>$&amp;</c>;
/// a literal replacement is taken as it is. Matches without text are replaced too (<c>^</c> inserts a prefix, <c>$</c> appends).
/// </summary>
public static class ReplacePlanner
{
    public static async Task<ReplacePlan> PlanAsync(SearchPattern pattern, string replacement, SearchScope scope, SearchSettings? settings = null, int parallelism = 4, CancellationToken ct = default)
    {
        settings ??= new();
        var compiled = CompiledPattern.Create(pattern, settings.Engine);
        var sources = new ConcurrentBag<SearchSource>();
        var run = SearchWorker.Start(pattern, scope, settings with { MaxResults = 0, StopAtLimit = false, IncludeEmpty = true, FileMatched = (src, _) => sources.Add(src) }, ct);
        await foreach (var _ in run.Batches.ReadAllAsync(CancellationToken.None)) { }
        if ((await run.Completion).End == SearchEnd.Cancelled) throw new OperationCanceledException(ct);

        var buffers = (scope.Buffers ?? []).ToDictionary(b => b.Snapshot.DocumentId);
        var files = new ConcurrentBag<FileChange>();
        var skipped = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct }, (src, _) =>
        {
            try
            {
                if (PlanSource(src, buffers, compiled, pattern.IsRegex, replacement) is { } change) files.Add(change);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or RegexMatchTimeoutException)
            {
                skipped.Add($"{src.Path ?? src.Title}: {e.Message}");
            }
            return ValueTask.CompletedTask;
        });
        var proposal = new ChangeProposal(Guid.NewGuid(), $"Replace '{pattern.Text}' with '{replacement}'", "replace", [.. files.OrderBy(f => f.Key, StringComparer.Ordinal)]);
        return new ReplacePlan(proposal, [.. skipped.Order(StringComparer.Ordinal)]);
    }

    private static FileChange? PlanSource(SearchSource src, Dictionary<Guid, LiveBuffer> buffers, CompiledPattern q, bool isRegex, string replacement)
    {
        byte[] bytes;
        TextCodec codec;
        FileFingerprint? print = null;
        if (src.DocumentId is { } id)
        {
            var snap = buffers[id].Snapshot;
            bytes = ChangeBuilder.ReadAll(snap);
            codec = snap.Codec;
        }
        else
        {
            (bytes, print) = ChangeBuilder.ReadFile(src.Path!);
            codec = ContentScanner.Detect(bytes, bytes.Length) ?? throw new NotSupportedException("The file looks binary.");
        }

        var dt = codec.Decode(bytes);
        var edits = RegexTools.ReplacementEdits(q.Regex, dt.Text, replacement, isRegex);
        if (edits.Count == 0) return null;
        string key = src.DocumentId is { } d ? d.ToString() : src.Path!;
        return ChangeBuilder.FromChars(key, src.Path, src.DocumentId, src.Revision, print, codec, dt, edits);
    }
}

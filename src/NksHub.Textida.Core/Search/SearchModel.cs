using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Search;

/// <summary>
/// What to look for. Regular expressions use the .NET dialect (System.Text.RegularExpressions, culture invariant) with
/// <c>^</c> and <c>$</c> at line starts and ends and <c>.</c> not matching LF. A CRLF file therefore needs <c>\r?$</c> to match
/// before the line break. Whole word wraps the pattern in <c>\b</c> (a literal only on the sides that start or end with a word character).
/// The search is case sensitive unless <see cref="CaseSensitive"/> is false. Empty matches are not listed (Replace uses them, see <see cref="RegexTools.ReplacementEdits"/>). An empty or invalid pattern
/// throws <see cref="ArgumentException"/> when the search starts.
/// A match never crosses the cut between two read chunks of a file larger than <see cref="SearchSettings.ChunkBytes"/>;
/// chunks are cut after a line feed, so only a pattern that itself contains a line break can be affected.
/// </summary>
public sealed record SearchPattern(string Text, bool IsRegex = false, bool CaseSensitive = true, bool WholeWord = false);

public enum RegexEngine
{
    /// <summary>The linear-time engine when the pattern allows it (no lookarounds, backreferences, atomic groups), else backtracking with <see cref="RegexTools.MatchTimeoutMs"/>.</summary>
    Auto,
    Backtracking,
}

public sealed record SearchSettings
{
    /// <summary>Hits kept and delivered; more matches are still counted unless <see cref="StopAtLimit"/>.</summary>
    public int MaxResults { get; init; } = 10_000;
    /// <summary>Stop the search once <see cref="MaxResults"/> hits are delivered (the summary says so).</summary>
    public bool StopAtLimit { get; init; } = true;
    public int Parallelism { get; init; } = Environment.ProcessorCount;
    /// <summary>A batch is delivered when it holds this many hits.</summary>
    public int BatchSize { get; init; } = 200;
    /// <summary>... or when this long has passed since the last delivery.</summary>
    public TimeSpan BatchInterval { get; init; } = TimeSpan.FromMilliseconds(50);
    /// <summary>Bytes read and decoded at a time; bounds the memory per worker thread.</summary>
    public int ChunkBytes { get; init; } = 4 << 20;
    /// <summary>Longest preview text of a hit in UTF-16 units.</summary>
    public int PreviewChars { get; init; } = 240;
    public RegexEngine Engine { get; init; } = RegexEngine.Auto;
    /// <summary>Count matches without text (<c>^</c>, <c>$</c>) too; Replace in Files needs them, a search list does not.</summary>
    public bool IncludeEmpty { get; init; }
    /// <summary>Called on a worker thread for every source with at least one match and its match count; callers must be thread safe.</summary>
    public Action<SearchSource, long>? FileMatched { get; init; }
    public FileWalkOptions Walk { get; init; } = new();
}

/// <summary>An open document searched in its current revision, including changes that are not saved.</summary>
public sealed record LiveBuffer(DocumentSnapshot Snapshot, string? Path, string Title)
{
    /// <summary>Takes the snapshot; call it on the thread that owns <paramref name="doc"/>.</summary>
    public static LiveBuffer Of(Document doc, string? title = null) =>
        new(doc.GetSnapshot(), doc.FilePath, title ?? (doc.FilePath is { } p ? System.IO.Path.GetFileName(p) : "Untitled"));
}

/// <summary>Folders to walk and open buffers; a buffer replaces the file of the same path on disk.</summary>
public sealed record SearchScope(IReadOnlyList<string> Roots, IReadOnlyList<LiveBuffer>? Buffers = null);

/// <summary>What was searched: a file on disk (<see cref="Stamp"/>) or one revision of an open document.</summary>
public sealed record SearchSource(string? Path, Guid? DocumentId, long? Revision, string Title, TextCodec Codec, long Length, DateTime? LastWriteUtc)
{
    public bool IsLive => DocumentId is not null;
}

/// <summary>
/// One match. <see cref="Range"/> is in bytes of the searched revision (live buffer) or file content. Line is 1-based, Column 0-based in UTF-16 units
/// (relative to the chunk start when a line is longer than a chunk).
/// </summary>
public sealed record SearchHit(SearchSource Source, ByteRange Range, int Line, int Column, string Preview);

public sealed record SearchBatch(IReadOnlyList<SearchHit> Hits, long FilesScanned, long TotalMatches);

public enum SearchEnd { Completed, Cancelled, LimitReached }

public sealed record SearchSummary(
    SearchEnd End, long FilesScanned, long FilesMatched, long TotalMatches, long HitsDelivered, bool HitsDropped,
    long SkippedBinary, long SkippedUnreadable, FileWalkStats Walk, IReadOnlyList<string> Errors, TimeSpan Elapsed)
{
    public bool LimitReached => End == SearchEnd.LimitReached || HitsDropped;
}

/// <summary>Compiled form of a <see cref="SearchPattern"/>.</summary>
internal sealed class CompiledPattern
{
    private CompiledPattern(SearchPattern pattern, Regex regex, byte[]? utf8Literal)
    {
        Pattern = pattern;
        Regex = regex;
        Utf8Literal = utf8Literal;
    }

    public SearchPattern Pattern { get; }
    public Regex Regex { get; }
    /// <summary>The pattern as UTF-8 for the byte search; only for a case sensitive literal without whole word.</summary>
    public byte[]? Utf8Literal { get; }

    public static CompiledPattern Create(SearchPattern p, RegexEngine engine = RegexEngine.Auto)
    {
        if (string.IsNullOrEmpty(p.Text)) throw new ArgumentException("The search text is empty.", nameof(p));
        string body = p.IsRegex ? p.Text : Regex.Escape(p.Text);
        if (p.WholeWord)
        {
            string left = p.IsRegex || IsWord(p.Text[0]) ? @"\b" : "";
            string right = p.IsRegex || IsWord(p.Text[^1]) ? @"\b" : "";
            body = $"{left}(?:{body}){right}";
        }
        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (p.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        Regex? regex = null;
        if (engine == RegexEngine.Auto)
        {
            try { regex = new Regex(body, options | RegexOptions.NonBacktracking); }
            catch (NotSupportedException) { }
        }
        regex ??= new Regex(body, options, TimeSpan.FromMilliseconds(RegexTools.MatchTimeoutMs));
        bool plain = !p.IsRegex && p.CaseSensitive && !p.WholeWord;
        return new CompiledPattern(p, regex, plain ? Encoding.UTF8.GetBytes(p.Text) : null);
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}

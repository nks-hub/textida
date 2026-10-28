namespace NksHub.Textida.Core.Agents.Transcripts;

/// <summary>Limits the caller decides; there are no defaults.</summary>
/// <param name="WholeLineBytes">A line up to this size is read and parsed whole while the index is built (that is where the values of a secrets file are learned); a longer one is classified from its first <paramref name="PeekBytes"/> and parsed only when it is shown.</param>
/// <param name="PeekBytes">Bytes of the start of a long line that decide what kind of record it is.</param>
/// <param name="IndexMaxBytes">Memory the index may use; past it the index stops growing and says so.</param>
/// <param name="MaxLinesPerTurn">A turn with more lines is continued in the next one, so a turn never needs more than this many lines to be shown.</param>
/// <param name="MaxRowChars">Characters of one row of a turn that are shown; the rest is cut with a note.</param>
/// <param name="MaxRawBytes">Bytes of a raw line that are shown or opened.</param>
/// <param name="MaxSearchHits">Hits the search collects before it stops.</param>
/// <param name="PreviewChars">Characters of a prompt shown as the title of its turn.</param>
/// <param name="MaxDepth">Deepest JSON nesting that is read.</param>
/// <param name="MinSecretLength">Shortest value of a secrets file that is hidden wherever it appears in the transcript.</param>
/// <param name="MaxPendingTools">Tool calls waiting for their result that are remembered while the transcript is read.</param>
public sealed record TranscriptLimits(
    int WholeLineBytes, int PeekBytes, long IndexMaxBytes, int MaxLinesPerTurn, int MaxRowChars, int MaxRawBytes, int MaxSearchHits, int PreviewChars, int MaxDepth, int MinSecretLength, int MaxPendingTools);

public enum TranscriptKind : byte
{
    /// <summary>A record this reader does not interpret: it is shown as raw JSON.</summary>
    Other,
    /// <summary>The line is not a JSON object.</summary>
    Invalid,
    UserPrompt,
    ToolResult,
    Assistant,
}

public enum TranscriptRowKind { User, Assistant, Thinking, ToolCall, ToolResult, ToolError, DiffRemoved, DiffAdded, Raw, Note }

/// <summary>One piece of a turn: text, the line of the file it came from, and for a tool call the file it touches. The text is masked.</summary>
public sealed record TranscriptRow(long Line, TranscriptRowKind Kind, string Text, string? Path = null, string? Tool = null);

/// <summary>A turn: a prompt and everything up to the next prompt. Number 0 holds the lines before the first prompt. A turn that reached the line limit goes on in the next, <see cref="Continued"/>.</summary>
public sealed record TranscriptTurn(int Number, long FirstLine, long LineCount, string Preview, bool Continued);

public sealed record TouchedFile(string Path, string Tool, long Line);

public sealed class TurnContent
{
    public List<TranscriptRow> Rows { get; } = [];
    public List<TouchedFile> Files { get; } = [];

    /// <summary>The tokens the file itself reports for the answers of the turn (<c>message.usage</c> of the Messages API); not an estimate. Valid when <see cref="HasUsage"/>.</summary>
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public bool HasUsage { get; set; }

    /// <summary>Lines longer than the parse limit; they are listed as raw text cut short.</summary>
    public int OversizedLines { get; set; }
}

/// <summary>A hit of the search: the line, the turn it belongs to and the text around it (masked).</summary>
public sealed record TranscriptHit(long Line, int Turn, string Snippet);

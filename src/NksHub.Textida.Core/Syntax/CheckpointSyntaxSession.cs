using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Syntax;

public enum SyntaxViewportStatus { Pending, Complete, FullLineRequired }

/// <summary>Only complete lines from the requested viewport. FullLineRequired cannot resume without a different input or grammar.</summary>
public sealed record SyntaxViewport(Guid DocumentId, long Revision, IReadOnlyList<LineTokens> Lines, SyntaxViewportStatus Status, long NextLine)
{
    public bool IsComplete => Status == SyntaxViewportStatus.Complete;
}

/// <summary>
/// Reads a document revision in 32 KiB windows and retains sparse line checkpoints. Calls and document edits must be serialized.
/// Each query can be resumed with the same arguments; a changed document discards pending tokens before any further result is returned.
/// </summary>
public sealed class CheckpointSyntaxSession : IDisposable
{
    private const int CheckpointLimit = 256;
    public const int FullLineLimitChars = 1 << 20;
    private const int MaximumRows = 1024;
    private const int MaximumTokens = 65536;
    private const long MinimumCheckpointBytes = 64 << 10;
    private readonly record struct Checkpoint(long Offset, long Line, LineState State);
    private readonly record struct Request(long First, int Count, int Column, int Columns);

    private readonly Document document;
    private readonly Tokenizer tokenizer;
    private readonly List<Checkpoint> checkpoints = new(CheckpointLimit);
    private readonly List<Checkpoint> stale = new(CheckpointLimit);
    private readonly List<Token> scratch = [];
    private readonly List<Token> lineTokens = [];
    private readonly List<LineTokens> lines = [];
    private readonly StringBuilder fullLine = new();
    private DocumentSnapshot? snapshot;
    private SyntaxChunkReader? reader;
    private long readerOffset;
    private Request? request;
    private LineState state;
    private LineState lineStart;
    private string tail = "";
    private char prefix;
    private long currentLine;
    private long column;
    private long checkpointSpacing = MinimumCheckpointBytes;
    private int retainedTokens;
    private bool complete;
    private bool documentEnd;
    private bool disposed;
    private bool bufferedLine = true;
    private ContextDef? streamingRegion;
    private bool unsupported;
    private StreamingSyntaxSession? streaming;
    private long bytesScanned;

    public CheckpointSyntaxSession(Document document, Tokenizer tokenizer)
    {
        this.document = document;
        this.tokenizer = tokenizer;
        state = lineStart = tokenizer.InitialState;
        readerOffset = document.BomLength;
        checkpoints.Add(new(document.BomLength, 0, state));
        document.Changed += OnChanged;
    }

    // The snapshot of a byte backend that keeps its bytes in one piece is a full copy, so it is taken when a scan needs it and not at every edit.
    private DocumentSnapshot Snapshot => snapshot ??= document.GetSnapshot();

    public long Revision => snapshot?.Revision ?? document.Revision;
    public long BytesScanned => bytesScanned + (streaming?.BytesRead ?? 0);
    public int CheckpointCount => checkpoints.Count + stale.Count;

    /// <summary>
    /// Conservative checkpoint-storage bound: two 256-entry arrays and two active states, with 65 frames of 128 B per entry
    /// including weak interning. Document storage, shared grammar definitions, text windows and returned/cached viewport tokens are excluded.
    /// </summary>
    public long CheckpointMemoryUpperBoundBytes => (2L * CheckpointLimit + 2) * (24 + 65 * 128) + (streaming?.CheckpointMemoryUpperBoundBytes ?? 0);

    /// <summary>
    /// Returns only requested UTF-16 columns (4096 by default, at most 65536). At most 1024 lines and 65536 visible token runs can be retained.
    /// The slice is checked between byte windows, not inside a regex match or the final full-line scan; its duration is not the total catch-up time.
    /// A zero slice only reads cached results. Cancellation preserves completed windows for the next call.
    /// Lines up to 1 Mi UTF-16 characters use a full reference scan. Other builtin lines use snapshot-backed streaming plans.
    /// A custom grammar without a streaming plan can return FullLineRequired; repeating that request does not scan again.
    /// </summary>
    public SyntaxViewport TokenizeViewport(long firstLine, int count, TimeSpan slice,
        CancellationToken ct = default, int firstColumn = 0, int columnCount = 4096)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (firstLine < 0) throw new ArgumentOutOfRangeException(nameof(firstLine));
        if (count < 0 || count > MaximumRows) throw new ArgumentOutOfRangeException(nameof(count));
        if (firstColumn < 0 || columnCount < 0 || columnCount > MaximumTokens || (long)firstColumn + columnCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(columnCount));
        if (slice < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(slice));
        ct.ThrowIfCancellationRequested();
        if (streaming is not null) return StreamViewport(firstLine, count, slice, ct, firstColumn, columnCount);
        var next = new Request(firstLine, count, firstColumn, columnCount);
        if (request != next) Begin(next);
        long started = Stopwatch.GetTimestamp();
        while (!complete && !unsupported && Stopwatch.GetElapsedTime(started) < slice)
        {
            ct.ThrowIfCancellationRequested();
            Step();
        }
        var status = unsupported ? SyntaxViewportStatus.FullLineRequired : complete ? SyntaxViewportStatus.Complete : SyntaxViewportStatus.Pending;
        if (unsupported && ReferenceEquals(tokenizer.Grammar.Registry, BuiltinGrammars.Registry))
        {
            streaming = new StreamingSyntaxSession(document, tokenizer);
            return StreamViewport(firstLine, count, TimeSpan.Zero, ct, firstColumn, columnCount);
        }
        return new(document.Id, Revision, lines.ToArray(), status, currentLine);
    }

    private SyntaxViewport StreamViewport(long firstLine, int count, TimeSpan slice, CancellationToken ct, int firstColumn, int columnCount)
    {
        var view = streaming!.TokenizeWindow(firstLine, count, firstColumn, columnCount, slice, ct);
        var converted = view.Lines.Select(line => new LineTokens(line.Line,
            line.Tokens.Select(token => token with { Start = checked(token.Start + firstColumn) }).ToArray(), line.StartState, line.EndState)).ToArray();
        return new(view.DocumentId, view.Revision, converted, view.Status, view.NextLine);
    }

    private void Begin(Request next)
    {
        request = next;
        lines.Clear();
        retainedTokens = 0;
        complete = next.Count == 0;
        unsupported = false;
        var checkpoint = checkpoints[0];
        foreach (var candidate in checkpoints)
        {
            if (candidate.Line > next.First) break;
            checkpoint = candidate;
        }
        Resume(checkpoint);
    }

    private void Resume(Checkpoint checkpoint)
    {
        reader = null;
        readerOffset = checkpoint.Offset;
        currentLine = checkpoint.Line;
        state = lineStart = checkpoint.State;
        prefix = '\0';
        tail = "";
        column = 0;
        lineTokens.Clear();
        documentEnd = false;
        fullLine.Clear();
        bufferedLine = true;
        streamingRegion = IsStreamingRegion(state.Context) ? state.Context : null;
    }

    private void Step()
    {
        var active = request!.Value;
        reader ??= new SyntaxChunkReader(Snapshot, readerOffset);
        long before = reader.Offset;
        var chunk = reader.Read();
        bytesScanned += reader.Offset - before;
        if (bufferedLine)
        {
            if (fullLine.Length + chunk.Text.Length <= FullLineLimitChars) fullLine.Append(chunk.Text);
            else bufferedLine = false;
        }
        if (!bufferedLine && streamingRegion is null)
        {
            unsupported = true;
            lineTokens.Clear();
            return;
        }
        string text = prefix == '\0' ? tail + chunk.Text : "\0" + prefix + tail + chunk.Text;
        int begin = prefix == '\0' ? 0 : 2;
        scratch.Clear();
        int consumed;
        if (chunk.LineEnd && bufferedLine)
        {
            state = lineStart;
            lineTokens.Clear();
            consumed = tokenizer.Scan(fullLine.ToString(), 0, true, ref state, scratch, 0,
                active.Column, currentLine >= active.First ? active.Column + active.Columns : active.Column);
            if (currentLine >= active.First) Retain(scratch, 0, active);
        }
        else
        {
            bool regionEnded = false;
            consumed = tokenizer.Scan(text, begin, chunk.LineEnd, ref state, scratch, 0, regionEnded: () => regionEnded = true);
            if (column == 0 && streamingRegion is null && tokenizer.Grammar.Id != "json" &&
                IsStreamingRegion(state.Context) && StartsWithRegion(text, state.Context))
                streamingRegion = state.Context;
            if (!chunk.LineEnd && (regionEnded || !ReferenceEquals(state.Context, streamingRegion))) streamingRegion = null;
            if (currentLine >= active.First) Retain(scratch, column - begin, active);
        }
        column += consumed - begin;
        if (!chunk.LineEnd)
        {
            prefix = consumed > 0 ? text[consumed - 1] : prefix;
            tail = text[consumed..];
            return;
        }

        if (currentLine >= active.First)
        {
            var tokens = lineTokens.ToArray();
            retainedTokens += tokens.Length;
            lines.Add(new(currentLine, tokens, lineStart, state));
        }
        currentLine++;
        column = 0;
        prefix = '\0';
        tail = "";
        lineTokens.Clear();
        lineStart = state;
        fullLine.Clear();
        bufferedLine = true;
        streamingRegion = IsStreamingRegion(state.Context) ? state.Context : null;
        documentEnd = chunk.DocumentEnd;
        if (!documentEnd) SaveCheckpoint(new(reader.Offset, currentLine, state));
        complete = documentEnd || currentLine - active.First >= active.Count;
        if (!complete && currentLine < active.First) SkipKnown(active.First);
    }

    private static bool StartsWithRegion(string text, ContextDef context) =>
        context.Begin is { } begin && TryLiteral(begin, out string literal) && text.StartsWith(literal, StringComparison.Ordinal);

    private static bool IsStreamingRegion(ContextDef context)
    {
        if (context.Token is not (TokenKinds.Comment or TokenKinds.DocComment or TokenKinds.String)) return false;
        if (context.Begin is null || context.End is null || context.Embed is not null) return false;
        if (!TryLiteral(context.Begin, out _) || !TryLiteral(context.End, out _)) return false;
        return context.Rules.All(rule => rule is MatchRule { Pattern: @"\\." or @"\\['\\]" });
    }

    private static bool TryLiteral(string pattern, out string literal)
    {
        var value = new StringBuilder(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\')
            {
                if (++i == pattern.Length || char.IsLetterOrDigit(pattern[i])) { literal = ""; return false; }
                value.Append(pattern[i]);
            }
            else if (".[](){}*+?^$|".Contains(c)) { literal = ""; return false; }
            else value.Append(c);
        }
        literal = value.ToString();
        return literal.Length > 0;
    }

    private void Retain(List<Token> tokens, long shift, Request active)
    {
        long endColumn = (long)active.Column + active.Columns;
        foreach (var token in tokens)
        {
            long from = Math.Max(token.Start + shift, active.Column);
            long to = Math.Min(token.End + shift, endColumn);
            if (from >= to) continue;
            var adjusted = new Token((int)from, (int)(to - from), token.Kind);
            if (lineTokens.Count > 0 && lineTokens[^1].End == adjusted.Start && lineTokens[^1].Kind == adjusted.Kind)
                lineTokens[^1] = lineTokens[^1] with { Length = lineTokens[^1].Length + adjusted.Length };
            else
            {
                if (retainedTokens + lineTokens.Count >= MaximumTokens)
                    throw new InvalidOperationException("The syntax viewport has too many token runs; request fewer rows or columns.");
                lineTokens.Add(adjusted);
            }
        }
    }

    private void SaveCheckpoint(Checkpoint checkpoint)
    {
        int firstStale = 0;
        while (firstStale < stale.Count && stale[firstStale].Offset < checkpoint.Offset) firstStale++;
        if (firstStale > 0) stale.RemoveRange(0, firstStale);
        if (stale.Count > 0 && stale[0].Offset == checkpoint.Offset)
        {
            var prior = stale[0];
            if (ReferenceEquals(prior.State, checkpoint.State))
            {
                long delta = checkpoint.Line - prior.Line;
                foreach (var item in stale) AddCheckpoint(item with { Line = item.Line + delta });
                stale.Clear();
                return;
            }
            stale.RemoveAt(0);
        }
        if (checkpoint.Offset - checkpoints[^1].Offset >= checkpointSpacing) AddCheckpoint(checkpoint);
    }

    private void AddCheckpoint(Checkpoint checkpoint)
    {
        if (checkpoint.Offset <= checkpoints[^1].Offset) return;
        if (checkpoints.Count == CheckpointLimit)
        {
            int write = 1;
            for (int read = 2; read < checkpoints.Count; read += 2) checkpoints[write++] = checkpoints[read];
            checkpoints.RemoveRange(write, checkpoints.Count - write);
            checkpointSpacing *= 2;
        }
        checkpoints.Add(checkpoint);
    }

    private void SkipKnown(long target)
    {
        Checkpoint? found = null;
        foreach (var checkpoint in checkpoints)
        {
            if (checkpoint.Line > target) break;
            if (checkpoint.Line > currentLine) found = checkpoint;
        }
        if (found is { } next) Resume(next);
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (streaming is not null) bytesScanned += streaming.BytesRead;
        streaming?.Dispose();
        streaming = null;
        var change = args.Change;
        bool unresolved = stale.Count > 0;
        stale.Clear();
        long first = change.Edits.Count == 0 ? 0 : change.Edits.Min(edit => edit.Start);
        int keep = 1;
        while (keep < checkpoints.Count && checkpoints[keep].Offset < first) keep++;
        if (!unresolved && change.Kind != ChangeKind.View && change.Edits.Count == 1)
        {
            var edit = change.Edits[0];
            long end = edit.Start + edit.Deleted;
            long delta = edit.Inserted - edit.Deleted;
            for (int i = keep; i < checkpoints.Count; i++)
                if (checkpoints[i].Offset > end) stale.Add(checkpoints[i] with { Offset = checkpoints[i].Offset + delta });
        }
        checkpoints.RemoveRange(keep, checkpoints.Count - keep);
        if (change.Kind == ChangeKind.View || document.BomLength != checkpoints[0].Offset)
        {
            checkpoints.Clear();
            stale.Clear();
            checkpoints.Add(new(document.BomLength, 0, tokenizer.InitialState));
        }
        snapshot = null;
        request = null;
        lines.Clear();
        lineTokens.Clear();
        tail = "";
        complete = false;
        Resume(checkpoints[^1]);
    }

    public void Dispose()
    {
        if (disposed) return;
        document.Changed -= OnChanged;
        disposed = true;
        streaming?.Dispose();
        streaming = null;
        checkpoints.Clear();
        stale.Clear();
        lines.Clear();
        lineTokens.Clear();
    }
}

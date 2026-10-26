using System.Diagnostics;
using System.Runtime.CompilerServices;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Syntax;

/// <summary>Token columns are local to FirstColumn; the line and its viewport origin are 64 bit.</summary>
public sealed record WindowLineTokens(long Line, long FirstColumn, IReadOnlyList<Token> Tokens, LineState StartState, LineState EndState);
public sealed record SyntaxWindow(Guid DocumentId, long Revision, IReadOnlyList<WindowLineTokens> Lines, SyntaxViewportStatus Status, long NextLine)
{
    public bool IsComplete => Status == SyntaxViewportStatus.Complete;
}

/// <summary>Resumable builtin grammar matching against an immutable snapshot, without retaining whole lines.</summary>
public sealed class StreamingSyntaxSession : IDisposable
{
    private readonly record struct Request(long First, int Count, long Column, int Columns);
    private readonly record struct Checkpoint(long Line, long Offset, LineState State);
    private sealed record Plan(StreamNode Pattern, Alt Alt);
    private sealed class ContextPlans
    {
        public Plan[] Plans { get; }
        public int MaximumRepeats { get; }
        private readonly bool[] ascii = new bool[128];

        public ContextPlans(ContextDef context)
        {
            Plans = Compile(context);
            MaximumRepeats = Plans.Length == 0 ? 0 : Plans.Max(plan => CountRepeats(plan.Pattern));
            for (int i = 0; i < ascii.Length; i++) ascii[i] = Test((char)i);
        }

        private static Plan[] Compile(ContextDef context)
        {
            var matcher = context.GetMatcher();
            return matcher.Alts.Select((alt, i) => new Plan(StreamPattern.Compile(matcher.Patterns[i], alt.Owner.CaseSensitive), alt)).ToArray();
        }

        public bool CanStart(char c) => c < ascii.Length ? ascii[c] : Test(c);

        private bool Test(char c) => Plans.Any(plan => StreamingMatcher.CanStart(plan.Pattern, c) ||
            StreamingMatcher.Nullable(plan.Pattern) && plan.Pattern is not StreamAnchor { Kind: StreamAnchorKind.End });

        private static int CountRepeats(StreamNode node) => node switch
        {
            StreamRepeat repeat => 1 + CountRepeats(repeat.Child),
            StreamSequence sequence => sequence.Items.Sum(CountRepeats),
            StreamChoice choice => choice.Items.Sum(CountRepeats),
            StreamLook look => CountRepeats(look.Child),
            _ => 0,
        };
    }

    private static readonly ConditionalWeakTable<ContextDef, ContextPlans> Compiled = new();
    private readonly Document document;
    private readonly Tokenizer tokenizer;
    private readonly List<Checkpoint> checkpoints = [];
    private readonly List<WindowLineTokens> results = [];
    private readonly List<Token> tokens = [];
    private DocumentSnapshot snapshot;
    private Request? request;
    private IEnumerator<bool>? pending;
    private LineState state;
    private long nextLine;
    private long nextOffset;
    private long checkpointSpacing = 64 << 10;
    private SyntaxViewportStatus status;
    private bool disposed;
    private int maximumRepeats;

    public StreamingSyntaxSession(Document document, Tokenizer tokenizer)
    {
        this.document = document;
        this.tokenizer = tokenizer;
        snapshot = document.GetSnapshot();
        state = tokenizer.InitialState;
        checkpoints.Add(new(0, document.BomLength, state));
        document.Changed += OnChanged;
    }

    public long Revision => snapshot.Revision;
    public long BytesRead { get; private set; }

    /// <summary>Bound for line states, the sparse byte map, repeat replay offsets and scanned-run caches; excludes source/output text.</summary>
    public long CheckpointMemoryUpperBoundBytes => 258L * (24 + 65 * 128) + 512 * 16 + maximumRepeats * 256L * 16 + 64 * 24;

    public SyntaxWindow TokenizeWindow(long firstLine, int count, long firstColumn, int columnCount, TimeSpan slice, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (firstLine < 0 || firstLine > long.MaxValue - count) throw new ArgumentOutOfRangeException(nameof(firstLine));
        if (count < 0 || count > 1024) throw new ArgumentOutOfRangeException(nameof(count));
        if (firstColumn < 0 || columnCount < 0 || columnCount > 65536 || firstColumn > long.MaxValue - columnCount)
            throw new ArgumentOutOfRangeException(nameof(firstColumn));
        if (slice < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(slice));
        ct.ThrowIfCancellationRequested();
        var wanted = new Request(firstLine, count, firstColumn, columnCount);
        if (request != wanted) Begin(wanted);
        long start = Stopwatch.GetTimestamp();
        while (status == SyntaxViewportStatus.Pending && Stopwatch.GetElapsedTime(start) < slice)
        {
            ct.ThrowIfCancellationRequested();
            pending ??= ScanLine().GetEnumerator();
            if (!pending.MoveNext())
            {
                pending.Dispose();
                pending = null;
            }
        }
        return new(document.Id, snapshot.Revision, results.ToArray(), status, nextLine);
    }

    private void Begin(Request wanted)
    {
        pending?.Dispose();
        pending = null;
        request = wanted;
        results.Clear();
        tokens.Clear();
        var checkpoint = checkpoints[0];
        foreach (var entry in checkpoints)
        {
            if (entry.Line > wanted.First) break;
            checkpoint = entry;
        }
        (nextLine, nextOffset, state) = (checkpoint.Line, checkpoint.Offset, checkpoint.State);
        status = wanted.Count == 0 ? SyntaxViewportStatus.Complete : SyntaxViewportStatus.Pending;
        if (!ReferenceEquals(tokenizer.Grammar.Registry, BuiltinGrammars.Registry)) status = SyntaxViewportStatus.FullLineRequired;
    }

    private IEnumerable<bool> ScanLine()
    {
        var wanted = request!.Value;
        var source = new StreamingLineSource(snapshot, nextOffset, count => BytesRead += count);
        var matcher = new StreamingMatcher(source);
        var startState = state;
        tokens.Clear();
        long position = 0, search = 0;
        while (true)
        {
            var plans = Compiled.GetValue(state.Context, static context => new ContextPlans(context));
            maximumRepeats = Math.Max(maximumRepeats, plans.MaximumRepeats);
            foreach (var _ in source.Ensure(search)) yield return true;
            int c = source.At(search);
            if (c >= 0 && !plans.CanStart((char)c))
            {
                long next = source.SkipWindow(search, plans.CanStart);
                search = next > search ? next : search + 1;
                yield return true;
                continue;
            }

            Plan? selected = null;
            long end = search;
            foreach (var plan in plans.Plans)
            {
                if (c >= 0 && !StreamingMatcher.Nullable(plan.Pattern) && !StreamingMatcher.CanStart(plan.Pattern, (char)c)) continue;
                foreach (var step in matcher.Match(plan.Pattern, search))
                {
                    if (!step.IsMatch) { yield return true; continue; }
                    selected = plan;
                    end = step.End;
                    break;
                }
                if (selected is not null) break;
            }

            if (selected is null)
            {
                if (c < 0) break;
                search++;
                if ((search & 1023) == 0) yield return true;
                continue;
            }
            var alternative = selected.Alt;
            if (end == search && alternative.Kind != AltKind.End)
            {
                if (c < 0) break;
                search++;
                yield return true;
                continue;
            }
            Emit(position, search, state.Context.Token, wanted);
            switch (alternative.Kind)
            {
                case AltKind.End:
                    Emit(search, end, state.Context.EndToken, wanted);
                    state = state.Parent!;
                    break;
                case AltKind.Begin:
                    var child = alternative.Child!;
                    if (state.Depth < LineState.MaxDepth) state = child.Push(state);
                    Emit(search, end, child.BeginToken, wanted);
                    break;
                case AltKind.Match:
                    Emit(search, end, alternative.Token, wanted);
                    break;
                case AltKind.Word:
                    long length = end - search;
                    if (length <= alternative.Owner.MaximumKeywordLength)
                    {
                        foreach (var _ in source.Ensure(search)) yield return true;
                        foreach (var _ in source.Ensure(end - 1)) yield return true;
                    }
                    string? kind = length <= alternative.Owner.MaximumKeywordLength
                        ? alternative.Owner.LookupWord(source.SmallSlice(search, (int)length)) : null;
                    Emit(search, end, kind, wanted);
                    break;
            }
            position = search = end;
            yield return true;
        }
        Emit(position, search, state.Context.Token, wanted);
        while (state.Parent is not null && !state.Context.Multiline) state = state.Parent;
        if (nextLine >= wanted.First) results.Add(new(nextLine, wanted.Column, tokens.ToArray(), startState, state));
        nextLine++;
        nextOffset = source.NextByteOffset;
        if (!source.IsDocumentEnd) SaveCheckpoint();
        if (source.IsDocumentEnd || nextLine - wanted.First >= wanted.Count) status = SyntaxViewportStatus.Complete;
    }

    private void Emit(long from, long to, string? kind, Request wanted)
    {
        if (kind is null || nextLine < wanted.First) return;
        from = Math.Max(from, wanted.Column);
        to = Math.Min(to, wanted.Column + wanted.Columns);
        if (from >= to) return;
        var token = new Token((int)(from - wanted.Column), (int)(to - from), kind);
        if (tokens.Count > 0 && tokens[^1].End == token.Start && tokens[^1].Kind == kind)
            tokens[^1] = tokens[^1] with { Length = tokens[^1].Length + token.Length };
        else tokens.Add(token);
    }

    private void SaveCheckpoint()
    {
        if (nextOffset - checkpoints[^1].Offset < checkpointSpacing) return;
        if (checkpoints.Count == 256)
        {
            int target = 1;
            for (int i = 2; i < checkpoints.Count; i += 2) checkpoints[target++] = checkpoints[i];
            checkpoints.RemoveRange(target, checkpoints.Count - target);
            checkpointSpacing *= 2;
        }
        checkpoints.Add(new(nextLine, nextOffset, state));
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (disposed) return;
        pending?.Dispose();
        pending = null;
        request = null;
        long first = args.Change.Edits.Count == 0 ? 0 : args.Change.Edits.Min(edit => edit.Start);
        checkpoints.RemoveAll(checkpoint => checkpoint.Line > 0 && checkpoint.Offset >= first);
        if (args.Change.Kind == ChangeKind.View || document.BomLength != checkpoints[0].Offset)
        {
            checkpoints.Clear();
            checkpoints.Add(new(0, document.BomLength, tokenizer.InitialState));
        }
        snapshot = document.GetSnapshot();
        results.Clear();
        tokens.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        document.Changed -= OnChanged;
        pending?.Dispose();
        pending = null;
        results.Clear();
        tokens.Clear();
        checkpoints.Clear();
    }
}

namespace NksHub.Textida.Core.Syntax;

internal readonly record struct StreamMatchStep(long End, bool IsMatch)
{
    public static StreamMatchStep Pending => new(0, false);
    public static StreamMatchStep Match(long end) => new(end, true);
}

/// <summary>Prioritized endpoints over a snapshot line. Progress steps preserve the iterators during lookaround and replay.</summary>
internal sealed class StreamingMatcher(StreamingLineSource source)
{
    private readonly record struct Run(StreamCharSet Characters, long Start, long End);
    private readonly List<Run> runs = [];
    private long operations;

    public IEnumerable<StreamMatchStep> Match(StreamNode node, long at, int direction = 1)
    {
        if ((++operations & 1023) == 0) yield return StreamMatchStep.Pending;
        switch (node)
        {
            case StreamChar atom:
                foreach (var _ in source.Ensure(direction > 0 ? at : at - 1)) yield return StreamMatchStep.Pending;
                int c = source.At(direction > 0 ? at : at - 1);
                if (c >= 0 && atom.Characters.Matches((char)c)) yield return StreamMatchStep.Match(at + direction);
                break;
            case StreamAnchor anchor:
                foreach (var _ in source.Ensure(at - 1)) yield return StreamMatchStep.Pending;
                foreach (var _ in source.Ensure(at)) yield return StreamMatchStep.Pending;
                if (Assert(anchor.Kind, at)) yield return StreamMatchStep.Match(at);
                break;
            case StreamSequence sequence:
                foreach (var item in Sequence(sequence.Items, direction > 0 ? 0 : sequence.Items.Count - 1, at, direction)) yield return item;
                break;
            case StreamChoice choice:
                foreach (var branch in choice.Items)
                    foreach (var item in Match(branch, at, direction)) yield return item;
                break;
            case StreamLook look:
                bool matched = false;
                foreach (var item in Match(look.Child, at, look.Behind ? -1 : 1))
                {
                    if (!item.IsMatch) { yield return item; continue; }
                    matched = true;
                    break;
                }
                if (matched == look.Positive) yield return StreamMatchStep.Match(at);
                break;
            case StreamRepeat repeat:
                foreach (var item in Repeat(repeat, at, direction, true)) yield return item;
                break;
            default:
                throw new NotSupportedException($"No streaming plan for {node.GetType().Name}.");
        }
    }

    private IEnumerable<StreamMatchStep> Sequence(IReadOnlyList<StreamNode> items, int index, long at, int direction)
    {
        if (index < 0 || index >= items.Count) { yield return StreamMatchStep.Match(at); yield break; }
        int next = index + direction;
        var node = items[index];
        bool backtrack = true;
        if (node is StreamRepeat repeated && next >= 0 && next < items.Count && items[next] is StreamChar delimiter &&
            Literal(delimiter.Characters) is { } literal && !CanStart(repeated.Child, literal, direction))
            backtrack = false;
        var candidates = node is StreamRepeat repeat ? Repeat(repeat, at, direction, backtrack) : Match(node, at, direction);
        foreach (var candidate in candidates)
        {
            if (!candidate.IsMatch) { yield return candidate; continue; }
            foreach (var item in Sequence(items, next, candidate.End, direction)) yield return item;
        }
    }

    private IEnumerable<StreamMatchStep> Repeat(StreamRepeat repeat, long start, int direction, bool backtrack)
    {
        if (repeat.Maximum == 0) { yield return StreamMatchStep.Match(start); yield break; }
        if (repeat.Child is StreamChar atom)
        {
            foreach (var item in RepeatChar(atom.Characters, repeat, start, direction, backtrack)) yield return item;
            yield break;
        }
        if (repeat.Maximum is <= 1)
        {
            if (repeat.Minimum == 0 && repeat.Lazy) yield return StreamMatchStep.Match(start);
            foreach (var item in Match(repeat.Child, start, direction)) yield return item;
            if (repeat.Minimum == 0 && !repeat.Lazy) yield return StreamMatchStep.Match(start);
            yield break;
        }

        // Repeated builtin units have disjoint leading characters (for example a normal character or an escape).
        // Unit boundaries are sparse offsets; replay replaces an input-sized backtracking stack.
        var checkpoints = new List<(long Count, long Position)> { (0, start) };
        long spacing = 1024;
        long count = 0, position = start;
        long maximum = repeat.Maximum ?? long.MaxValue;
        if (repeat.Lazy && repeat.Minimum == 0) yield return StreamMatchStep.Match(position);
        while (count < maximum)
        {
            bool found = false;
            long next = position;
            foreach (var item in Match(repeat.Child, position, direction))
            {
                if (!item.IsMatch) { yield return item; continue; }
                next = item.End;
                found = true;
                break;
            }
            if (!found || next == position) break;
            position = next;
            count++;
            if (count - checkpoints[^1].Count >= spacing)
            {
                if (checkpoints.Count == 256)
                {
                    int write = 1;
                    for (int read = 2; read < checkpoints.Count; read += 2) checkpoints[write++] = checkpoints[read];
                    checkpoints.RemoveRange(write, checkpoints.Count - write);
                    spacing *= 2;
                }
                checkpoints.Add((count, position));
            }
            if (repeat.Lazy && count >= repeat.Minimum) yield return StreamMatchStep.Match(position);
        }
        if (repeat.Lazy || count < repeat.Minimum) yield break;
        yield return StreamMatchStep.Match(position);
        if (!backtrack) yield break;
        while (--count >= repeat.Minimum)
        {
            var checkpoint = checkpoints[0];
            foreach (var item in checkpoints)
            {
                if (item.Count > count) break;
                checkpoint = item;
            }
            long done = checkpoint.Count;
            position = checkpoint.Position;
            while (done++ < count)
            {
                foreach (var item in Match(repeat.Child, position, direction))
                {
                    if (!item.IsMatch) { yield return item; continue; }
                    position = item.End;
                    break;
                }
            }
            yield return StreamMatchStep.Match(position);
        }
    }

    private IEnumerable<StreamMatchStep> RepeatChar(StreamCharSet characters, StreamRepeat repeat, long start, int direction, bool backtrack)
    {
        long position = start, count = 0;
        long maximum = repeat.Maximum ?? long.MaxValue;
        if (repeat.Lazy && repeat.Minimum == 0) yield return StreamMatchStep.Match(position);
        if (!repeat.Lazy)
        {
            foreach (var run in runs)
            {
                if (!ReferenceEquals(run.Characters, characters) || start < run.Start || start > run.End) continue;
                long available = direction > 0 ? run.End - start : start - run.Start;
                count = Math.Min(available, maximum);
                position = start + count * direction;
                break;
            }
        }
        while (count < maximum)
        {
            foreach (var _ in source.Ensure(direction > 0 ? position : position - 1)) yield return StreamMatchStep.Pending;
            int c = source.At(direction > 0 ? position : position - 1);
            if (c < 0 || !characters.Matches((char)c)) break;
            position += direction;
            count++;
            if ((++operations & 4095) == 0) yield return StreamMatchStep.Pending;
            if (repeat.Lazy && count >= repeat.Minimum) yield return StreamMatchStep.Match(position);
        }
        if (runs.Count == 64) runs.RemoveAt(0);
        runs.Add(new(characters, Math.Min(start, position), Math.Max(start, position)));
        if (repeat.Lazy || count < repeat.Minimum) yield break;
        yield return StreamMatchStep.Match(position);
        if (!backtrack) yield break;
        while (--count >= repeat.Minimum)
        {
            position -= direction;
            if ((++operations & 4095) == 0) yield return StreamMatchStep.Pending;
            yield return StreamMatchStep.Match(position);
        }
    }

    private bool Assert(StreamAnchorKind kind, long at) => kind switch
    {
        StreamAnchorKind.Start => at == 0,
        StreamAnchorKind.End => source.At(at) < 0,
        StreamAnchorKind.WordBoundary => IsWord(source.At(at - 1)) != IsWord(source.At(at)),
        StreamAnchorKind.NotWordBoundary => IsWord(source.At(at - 1)) == IsWord(source.At(at)),
        _ => throw new NotSupportedException($"Unknown streaming assertion {kind}."),
    };

    private static bool IsWord(int c) => c >= 0 && (c is 0x200C or 0x200D || Word.Matches((char)c));
    private static readonly StreamCharSet Word = new(@"\w", true);

    internal static bool CanStart(StreamNode node, char c, int direction = 1) => node switch
    {
        StreamChar atom => atom.Characters.Matches(c),
        StreamChoice choice => choice.Items.Any(item => CanStart(item, c, direction)),
        StreamSequence sequence => SequenceStart(sequence.Items, c, direction),
        StreamRepeat repeat => CanStart(repeat.Child, c, direction),
        _ => false,
    };

    private static bool SequenceStart(IReadOnlyList<StreamNode> items, char c, int direction)
    {
        for (int i = direction > 0 ? 0 : items.Count - 1; i >= 0 && i < items.Count; i += direction)
        {
            if (CanStart(items[i], c, direction)) return true;
            if (!Nullable(items[i])) return false;
        }
        return false;
    }

    internal static bool Nullable(StreamNode node) => node switch
    {
        StreamChar => false,
        StreamSequence sequence => sequence.Items.All(Nullable),
        StreamChoice choice => choice.Items.Any(Nullable),
        StreamRepeat repeat => repeat.Minimum == 0 || Nullable(repeat.Child),
        _ => true,
    };

    private static char? Literal(StreamCharSet set)
    {
        string pattern = set.Pattern;
        if (pattern.Length == 1 && !char.IsLetterOrDigit(pattern[0]) && pattern[0] != '.') return pattern[0];
        if (pattern.Length == 2 && pattern[0] == '\\' && !char.IsLetterOrDigit(pattern[1])) return pattern[1];
        return null;
    }
}

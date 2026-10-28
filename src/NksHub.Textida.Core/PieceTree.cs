namespace NksHub.Textida.Core;

/// <summary>
/// A byte range of the original (mapped) buffer, of the append-only add buffer, or, when <see cref="Pattern"/> is set, a pattern repeated
/// to <see cref="Length"/> bytes. A repeated piece costs the same few bytes at any length; its <see cref="Start"/> is the position in the
/// pattern of its first byte.
/// </summary>
internal readonly record struct Piece(bool Original, long Start, long Length, byte[]? Pattern = null)
{
    public bool Repeated => Pattern is not null;

    /// <summary>The part of this piece that begins <paramref name="skip"/> bytes in and runs <paramref name="take"/> bytes.</summary>
    public Piece Slice(long skip, long take) =>
        Pattern is null ? this with { Start = Start + skip, Length = take } : this with { Start = (Start + skip) % Pattern.Length, Length = take };

    /// <summary>Copies the bytes from <paramref name="skip"/> into the piece to <paramref name="destination"/>, which must not run past the end.</summary>
    public void CopyRepeated(long skip, Span<byte> destination)
    {
        var pattern = Pattern!;
        if (pattern.Length == 1)
        {
            destination.Fill(pattern[0]);
            return;
        }
        int at = (int)((Start + skip) % pattern.Length);
        int done = 0;
        while (done < destination.Length)
        {
            int n = Math.Min(pattern.Length - at, destination.Length - done);
            pattern.AsSpan(at, n).CopyTo(destination[done..]);
            done += n;
            at = 0;
        }
    }
}

/// <summary>
/// Line terminators of a byte run read on its own (a trailing CR counts), plus the edge bytes needed
/// to join a CR and an LF that end up in neighbouring runs.
/// </summary>
internal readonly record struct LineStats(long Terminators, bool FirstLf, bool LastCr)
{
    public static LineStats Join(LineStats a, LineStats b) =>
        new(a.Terminators + b.Terminators - (a.LastCr && b.FirstLf ? 1 : 0), a.FirstLf, b.LastCr);
}

/// <summary>
/// Sequence of pieces kept in a treap keyed by cumulative byte length, so offset lookup,
/// split and merge are O(log n) regardless of file size. Once <see cref="TrackLines"/> is called every
/// node also carries line terminator counts, so line lookup stays O(log n) after edits.
/// </summary>
internal sealed class PieceTree
{
    private sealed class Node(Piece piece, uint priority)
    {
        public Piece Piece = piece;
        public Node? Left, Right;
        public readonly uint Priority = priority;
        public long Sum = piece.Length;
        public LineStats Own, Agg;
    }

    private Node? _root;
    private uint _rng = 2463534242;
    private Func<Piece, LineStats>? _measure;

    public long Length => Sum(_root);
    public bool TracksLines => _measure is not null;
    /// <summary>Line terminators in the whole sequence; valid only while <see cref="TracksLines"/>.</summary>
    public long Terminators => _root?.Agg.Terminators ?? 0;

    private static long Sum(Node? n) => n?.Sum ?? 0;

    private static Node Update(Node n)
    {
        n.Sum = Sum(n.Left) + n.Piece.Length + Sum(n.Right);
        var agg = n.Own;
        if (n.Left is { } l) agg = LineStats.Join(l.Agg, agg);
        if (n.Right is { } r) agg = LineStats.Join(agg, r.Agg);
        n.Agg = agg;
        return n;
    }

    private Node NewNode(Piece p)
    {
        var n = new Node(p, NextPriority());
        if (_measure is not null) n.Own = n.Agg = _measure(p);
        return n;
    }

    /// <summary>Starts keeping line counts; <paramref name="measure"/> must count a piece on its own. Costs one measure per piece now and two per split later.</summary>
    public void TrackLines(Func<Piece, LineStats> measure)
    {
        _measure = measure;
        Remeasure(_root);
    }

    /// <summary>Drops the line counts; <see cref="TrackLines"/> measures every piece again.</summary>
    public void StopTrackingLines() => _measure = null;

    private void Remeasure(Node? n)
    {
        if (n is null) return;
        Remeasure(n.Left);
        Remeasure(n.Right);
        n.Own = _measure!(n.Piece);
        Update(n);
    }

    /// <summary>
    /// Offset right after the <paramref name="k"/>-th (1-based) terminator. <paramref name="afterNthInPiece"/> returns
    /// the same for a piece read on its own; a trailing CR followed by LF in the next piece is skipped here.
    /// </summary>
    public long OffsetAfterTerminator(long k, Func<Piece, long, long> afterNthInPiece)
    {
        if (k < 1 || k > Terminators) throw new ArgumentOutOfRangeException(nameof(k));
        var n = _root;
        long at = 0;
        bool nextLf = false;
        while (n is not null)
        {
            long left = n.Left is { } l ? l.Agg.Terminators - (l.Agg.LastCr && n.Own.FirstLf ? 1 : 0) : 0;
            if (k <= left) { nextLf = n.Own.FirstLf; n = n.Left; continue; }
            k -= left;
            at += Sum(n.Left);
            bool afterLf = n.Right?.Agg.FirstLf ?? nextLf;
            long own = n.Own.Terminators - (n.Own.LastCr && afterLf ? 1 : 0);
            if (k <= own) return at + afterNthInPiece(n.Piece, k);
            k -= own;
            at += n.Piece.Length;
            n = n.Right;
        }
        throw new InvalidOperationException("line counts out of sync");
    }

    /// <summary>
    /// Line statistics of the first <paramref name="offset"/> bytes, joined from the aggregates met on the way down, so the cost is the height of the tree plus one
    /// <c>measure</c> of the piece that holds the offset, cut at it; null when there is nothing before the offset. Valid only while <see cref="TracksLines"/>.
    /// </summary>
    public LineStats? StatsBefore(long offset)
    {
        LineStats? acc = null;
        static LineStats Add(LineStats? a, LineStats b) => a is { } known ? LineStats.Join(known, b) : b;
        var n = _root;
        long at = 0;
        while (n is not null)
        {
            long leftEnd = at + Sum(n.Left);
            if (offset < leftEnd) { n = n.Left; continue; }
            if (n.Left is { } left) acc = Add(acc, left.Agg);
            at = leftEnd;
            if (offset == at) return acc;
            if (offset >= at + n.Piece.Length)
            {
                acc = Add(acc, n.Own);
                at += n.Piece.Length;
                n = n.Right;
                continue;
            }
            return Add(acc, _measure!(n.Piece.Slice(0, offset - at)));
        }
        return acc;
    }

    private uint NextPriority()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng;
    }

    /// <summary>Replaces the content and stops line tracking (the measure may depend on the old content).</summary>
    public void Reset(Piece? single)
    {
        _measure = null;
        _root = single is { Length: > 0 } p ? new Node(p, NextPriority()) : null;
    }

    private static Node? Merge(Node? a, Node? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        if (a.Priority > b.Priority)
        {
            a.Right = Merge(a.Right, b);
            return Update(a);
        }
        b.Left = Merge(a, b.Left);
        return Update(b);
    }

    private void Split(Node? t, long pos, out Node? l, out Node? r)
    {
        if (t is null) { l = r = null; return; }
        long ls = Sum(t.Left);
        if (pos <= ls)
        {
            Split(t.Left, pos, out l, out var rl);
            t.Left = rl;
            r = Update(t);
        }
        else if (pos >= ls + t.Piece.Length)
        {
            Split(t.Right, pos - ls - t.Piece.Length, out var lr, out r);
            t.Right = lr;
            l = Update(t);
        }
        else
        {
            long cut = pos - ls;
            var rightPiece = t.Piece.Slice(cut, t.Piece.Length - cut);
            var rightNode = NewNode(rightPiece);
            var oldRight = t.Right;
            t.Right = null;
            t.Piece = t.Piece with { Length = cut };
            if (_measure is not null) t.Own = _measure(t.Piece);
            l = Update(t);
            r = Merge(rightNode, oldRight);
        }
    }

    /// <summary>Removes <paramref name="length"/> bytes at <paramref name="offset"/> and returns the removed pieces.</summary>
    public Piece[] Remove(long offset, long length)
    {
        if (length == 0) return [];
        Split(_root, offset, out var a, out var rest);
        Split(rest, length, out var mid, out var b);
        var removed = new List<Piece>();
        Collect(mid, removed);
        _root = Merge(a, b);
        return [.. removed];
    }

    public void Insert(long offset, ReadOnlySpan<Piece> pieces)
    {
        if (pieces.IsEmpty) return;
        Node? mid = null;
        foreach (var p in pieces)
            if (p.Length > 0) mid = Merge(mid, NewNode(p));
        Split(_root, offset, out var a, out var b);
        _root = Merge(Merge(a, mid), b);
    }

    private static void Collect(Node? n, List<Piece> into)
    {
        if (n is null) return;
        Collect(n.Left, into);
        into.Add(n.Piece);
        Collect(n.Right, into);
    }

    public int PieceCount()
    {
        var all = new List<Piece>();
        Collect(_root, all);
        return all.Count;
    }

    /// <summary>Pieces covering [offset, offset + length), trimmed at both ends, in document order.</summary>
    public IEnumerable<Piece> EnumerateRange(long offset, long length)
    {
        var stack = new Stack<Node>();
        var cur = _root;
        long skip = offset, inner = 0;
        while (cur is not null)
        {
            long ls = Sum(cur.Left);
            if (skip < ls) { stack.Push(cur); cur = cur.Left; }
            else if (skip < ls + cur.Piece.Length) { inner = skip - ls; stack.Push(cur); break; }
            else { skip -= ls + cur.Piece.Length; cur = cur.Right; }
        }

        long remaining = length;
        while (stack.Count > 0 && remaining > 0)
        {
            var n = stack.Pop();
            long take = Math.Min(n.Piece.Length - inner, remaining);
            yield return n.Piece.Slice(inner, take);
            remaining -= take;
            inner = 0;
            for (var m = n.Right; m is not null; m = m.Left) stack.Push(m);
        }
    }
}

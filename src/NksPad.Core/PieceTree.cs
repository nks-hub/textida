namespace NksPad.Core;

/// <summary>A byte range of either the original (mapped) buffer or the append-only add buffer.</summary>
internal readonly record struct Piece(bool Original, long Start, long Length);

/// <summary>
/// Sequence of pieces kept in a treap keyed by cumulative byte length, so offset lookup,
/// split and merge are O(log n) regardless of file size.
/// </summary>
internal sealed class PieceTree
{
    private sealed class Node(Piece piece, uint priority)
    {
        public Piece Piece = piece;
        public Node? Left, Right;
        public readonly uint Priority = priority;
        public long Sum = piece.Length;
    }

    private Node? _root;
    private uint _rng = 2463534242;

    public long Length => Sum(_root);

    private static long Sum(Node? n) => n?.Sum ?? 0;

    private static Node Update(Node n)
    {
        n.Sum = Sum(n.Left) + n.Piece.Length + Sum(n.Right);
        return n;
    }

    private uint NextPriority()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng;
    }

    public void Reset(Piece? single)
    {
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
            var rightPiece = new Piece(t.Piece.Original, t.Piece.Start + cut, t.Piece.Length - cut);
            var rightNode = new Node(rightPiece, NextPriority());
            var oldRight = t.Right;
            t.Right = null;
            t.Piece = t.Piece with { Length = cut };
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
            if (p.Length > 0) mid = Merge(mid, new Node(p, NextPriority()));
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
            yield return new Piece(n.Piece.Original, n.Piece.Start + inner, take);
            remaining -= take;
            inner = 0;
            for (var m = n.Right; m is not null; m = m.Left) stack.Push(m);
        }
    }
}

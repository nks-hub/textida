namespace NksHub.Textida.Core.Documents;

/// <summary>A selection: <see cref="Anchor"/> is the fixed end, <see cref="Caret"/> the moving one. Offsets are in the space of the caller (bytes of one revision).</summary>
public readonly record struct TextSelection(long Anchor, long Caret)
{
    public long Start => Math.Min(Anchor, Caret);
    public long End => Math.Max(Anchor, Caret);
    public long Length => End - Start;
    public bool IsEmpty => Anchor == Caret;
    public bool IsForward => Anchor <= Caret;
    public static TextSelection At(long offset) => new(offset, offset);
    public ByteRange ToRange() => new(Start, End);
}

/// <summary>
/// Selections of one text view: ascending, never overlapping or touching, one of them primary. <see cref="Create"/> normalises:
/// selections that overlap or touch (also an empty caret on the edge of another selection, and equal carets) become one that spans both.
/// The merged selection keeps the direction of the primary one when it contains it, otherwise of its first member.
/// </summary>
public sealed class SelectionSet
{
    private SelectionSet(IReadOnlyList<TextSelection> items, int primary)
    {
        Items = items;
        Primary = primary;
    }

    public IReadOnlyList<TextSelection> Items { get; }

    /// <summary>Index of the primary selection in <see cref="Items"/>.</summary>
    public int Primary { get; }

    public TextSelection PrimarySelection => Items[Primary];

    public int Count => Items.Count;

    public static SelectionSet Single(TextSelection selection) => new([selection], 0);

    /// <summary>Normalises <paramref name="selections"/>; <paramref name="primary"/> indexes the unsorted input and is clamped.</summary>
    public static SelectionSet Create(IReadOnlyList<TextSelection> selections, int primary)
    {
        if (selections.Count == 0) throw new ArgumentException("A selection set needs at least one selection.", nameof(selections));
        primary = Math.Clamp(primary, 0, selections.Count - 1);
        if (IsDisjointAscending(selections)) return new([.. selections], primary); // nothing to sort or merge, as with the carets after typing at all of them
        var order = new int[selections.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = selections[a].Start.CompareTo(selections[b].Start);
            return c != 0 ? c : selections[a].End != selections[b].End ? selections[a].End.CompareTo(selections[b].End) : a.CompareTo(b);
        });

        var merged = new List<TextSelection>(selections.Count);
        int mergedPrimary = 0;
        long start = 0, end = 0;
        bool forward = true, hasPrimary = false, open = false;
        void Flush()
        {
            if (!open) return;
            if (hasPrimary) mergedPrimary = merged.Count;
            merged.Add(forward ? new(start, end) : new(end, start));
            open = false;
        }

        foreach (int i in order)
        {
            var s = selections[i];
            if (open && s.Start <= end)
            {
                end = Math.Max(end, s.End);
                if (i == primary) { if (!s.IsEmpty) forward = s.IsForward; hasPrimary = true; }
                continue;
            }
            Flush();
            (start, end, forward, hasPrimary, open) = (s.Start, s.End, s.IsForward, i == primary, true);
        }
        Flush();
        return new(merged, mergedPrimary);
    }

    private static bool IsDisjointAscending(IReadOnlyList<TextSelection> selections)
    {
        for (int i = 1; i < selections.Count; i++)
            if (selections[i].Start <= selections[i - 1].End) return false;
        return true;
    }

    public SelectionSet WithPrimary(int index) => new(Items, Math.Clamp(index, 0, Items.Count - 1));
}

/// <summary>
/// A <see cref="SelectionSet"/> that follows the document: both ends of every selection are <see cref="Anchor"/>s, so text inserted
/// before or between selections leaves them on the same text, whoever made the change (edit, undo, replace, reload of a range).
/// Text inserted exactly at an edge goes outside the selection; an empty caret moves behind it. A deletion that cuts one end off
/// leaves a caret at the other end; a selection that lost both ends is dropped. Not thread safe, like <see cref="Document"/>.
/// </summary>
public sealed class AnchoredSelections
{
    private readonly record struct Pair(Anchor Anchor, Anchor Caret);

    private readonly Pair[] _pairs;
    private readonly int _primary;

    public AnchoredSelections(Document document, SelectionSet set)
    {
        _pairs = new Pair[set.Count];
        for (int i = 0; i < _pairs.Length; i++)
        {
            var s = set.Items[i];
            var (anchorGravity, caretGravity) = GravitiesOf(s);
            _pairs[i] = new(document.CreateAnchor(s.Anchor, anchorGravity), document.CreateAnchor(s.Caret, caretGravity));
        }
        _primary = set.Primary;
    }

    private static (AnchorGravity Anchor, AnchorGravity Caret) GravitiesOf(TextSelection s) =>
        s.IsEmpty ? (AnchorGravity.Right, AnchorGravity.Right)
        : s.IsForward ? (AnchorGravity.Right, AnchorGravity.Left) : (AnchorGravity.Left, AnchorGravity.Right);

    /// <summary>True when these anchors already stand where <paramref name="set"/> puts its selections and follow the text the way new ones would, so new ones are not needed.</summary>
    public bool Matches(SelectionSet set)
    {
        if (set.Count != _pairs.Length || set.Primary != _primary) return false;
        for (int i = 0; i < _pairs.Length; i++)
        {
            var (a, c) = _pairs[i];
            var s = set.Items[i];
            var (anchorGravity, caretGravity) = GravitiesOf(s);
            if (!a.IsValid || !c.IsValid || a.Offset != s.Anchor || c.Offset != s.Caret || a.Gravity != anchorGravity || c.Gravity != caretGravity) return false;
        }
        return true;
    }

    /// <summary>Stops following the document. Anchors that nobody uses any more cost every later change, so a holder that replaces its selections releases the old ones.</summary>
    public void Release()
    {
        foreach (var (a, c) in _pairs)
        {
            a.IsValid = false;
            c.IsValid = false;
        }
    }

    /// <summary>The selections as they are now; null when every selection was lost.</summary>
    public SelectionSet? Current()
    {
        var list = new List<TextSelection>(_pairs.Length);
        int primary = -1;
        for (int i = 0; i < _pairs.Length; i++)
        {
            var (a, c) = _pairs[i];
            if (!a.IsValid && !c.IsValid) continue;
            if (i <= _primary) primary = list.Count;
            list.Add(a.IsValid && c.IsValid ? new(a.Offset, c.Offset) : TextSelection.At(a.IsValid ? a.Offset : c.Offset));
        }
        return list.Count == 0 ? null : SelectionSet.Create(list, Math.Max(primary, 0));
    }
}

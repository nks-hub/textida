using AvaloniaEdit.Document;
using NksHub.Textida.Core.Documents;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>The selections of a text view in document order, never overlapping or touching, with the index of the primary one.</summary>
public sealed record Carets(IReadOnlyList<Sel> Items, int Primary)
{
    public Sel PrimarySel => Items[Primary];
    public int Count => Items.Count;

    public static Carets One(Sel sel) => new([sel], 0);

    /// <summary>Sorts and merges overlapping and touching selections (<see cref="SelectionSet"/>); <paramref name="primary"/> indexes <paramref name="sels"/>.</summary>
    public static Carets Create(IReadOnlyList<Sel> sels, int primary)
    {
        var set = SelectionSet.Create([.. sels.Select(s => new TextSelection(s.Anchor, s.Caret))], primary);
        return new([.. set.Items.Select(t => new Sel((int)t.Anchor, (int)t.Caret))], set.Primary);
    }

    /// <summary>The selections with the primary one last, the order in which a list of selections is stored in an undo step and given back.</summary>
    public IReadOnlyList<Sel> PrimaryLast() => [.. Items.Where((_, i) => i != Primary), Items[Primary]];
}

/// <summary>Where the carets go: by one character, one word, to the edge of the line or of the document.</summary>
public enum CaretMove { Left, Right, WordLeft, WordRight, LineStart, LineEnd, DocumentStart, DocumentEnd }

/// <summary>
/// Caret movement, selecting and adding carets over a list of selections. All functions are pure over the text and return the
/// new <see cref="Carets"/>; the view applies them. Columns are visual (tabs count to the next tab stop) so carets keep their column across lines.
/// </summary>
public static class MultiCaret
{
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ---- columns ----

    public static int VisualColumn(TextDocument d, int offset, int tabSize)
    {
        var line = d.GetLineByOffset(offset);
        int col = 0;
        for (int i = line.Offset; i < offset; i++) col = d.GetCharAt(i) == '\t' ? col + tabSize - col % tabSize : col + 1;
        return col;
    }

    /// <summary>The offset on <paramref name="line"/> (1-based) closest to the visual column without passing it; the end of the line when it is shorter.</summary>
    public static int OffsetAtColumn(TextDocument d, int line, int column, int tabSize)
    {
        var l = d.GetLineByNumber(Math.Clamp(line, 1, d.LineCount));
        int col = 0, i = l.Offset;
        while (i < l.EndOffset)
        {
            int next = d.GetCharAt(i) == '\t' ? col + tabSize - col % tabSize : col + 1;
            if (next > column) break;
            col = next;
            i++;
        }
        return i;
    }

    // ---- movement ----

    /// <summary>Moves every caret. Without <paramref name="extend"/> a selection collapses: to its start for left movements, to its end for right ones.</summary>
    public static Carets Move(TextDocument d, Carets c, CaretMove move, bool extend, bool smartHome = true)
    {
        var moved = new List<Sel>(c.Count);
        foreach (var s in c.Items)
        {
            bool backwards = move is CaretMove.Left or CaretMove.WordLeft or CaretMove.LineStart or CaretMove.DocumentStart;
            int target;
            if (!extend && !s.IsEmpty && move is CaretMove.Left or CaretMove.Right) target = backwards ? s.Start : s.End;
            else target = Target(d, s.Caret, move, smartHome);
            moved.Add(extend ? new Sel(s.Anchor, target) : Sel.At(target));
        }
        return Carets.Create(moved, c.Primary);
    }

    private static int Target(TextDocument d, int at, CaretMove move, bool smartHome)
    {
        switch (move)
        {
            case CaretMove.Left: return Next(d, at, LogicalDirection.Backward, CaretPositioningMode.Normal);
            case CaretMove.Right: return Next(d, at, LogicalDirection.Forward, CaretPositioningMode.Normal);
            case CaretMove.WordLeft: return Next(d, at, LogicalDirection.Backward, CaretPositioningMode.WordStartOrSymbol);
            case CaretMove.WordRight: return Next(d, at, LogicalDirection.Forward, CaretPositioningMode.WordStartOrSymbol);
            case CaretMove.LineEnd: return d.GetLineByOffset(at).EndOffset;
            case CaretMove.DocumentStart: return 0;
            case CaretMove.DocumentEnd: return d.TextLength;
            default:
                var line = d.GetLineByOffset(at);
                if (!smartHome) return line.Offset;
                int first = line.Offset;
                while (first < line.EndOffset && d.GetCharAt(first) is ' ' or '\t') first++;
                return at == first ? line.Offset : first;
        }
    }

    private static int Next(TextDocument d, int at, LogicalDirection direction, CaretPositioningMode mode)
    {
        int next = TextUtilities.GetNextCaretPosition(d, at, direction, mode);
        return next < 0 ? at : next;
    }

    /// <summary>
    /// Moves every caret one line up or down to its remembered visual column (<paramref name="columns"/>, one per selection, or null to take the
    /// current ones) and returns the columns to remember. A caret on the first or last line stays.
    /// </summary>
    public static Carets MoveVertical(TextDocument d, Carets c, int direction, bool extend, int tabSize, IReadOnlyList<int>? columns, out int[] remembered)
    {
        remembered = [.. c.Items.Select((s, i) => columns is not null && columns.Count == c.Count ? columns[i] : VisualColumn(d, s.Caret, tabSize))];
        var moved = new List<Sel>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var s = c.Items[i];
            int line = d.GetLineByOffset(s.Caret).LineNumber + direction;
            int target = line < 1 || line > d.LineCount
                ? (direction < 0 ? 0 : d.TextLength) // like the stock caret: the first line goes to its start, the last to its end
                : OffsetAtColumn(d, line, remembered[i], tabSize);
            moved.Add(extend ? new Sel(s.Anchor, target) : Sel.At(target));
        }
        var result = Carets.Create(moved, c.Primary);
        if (result.Count != c.Count) remembered = [.. result.Items.Select(s => VisualColumn(d, s.Caret, tabSize))];
        return result;
    }

    // ---- adding carets ----

    /// <summary>Adds a caret on the line above or below the topmost or bottommost caret at the same visual column; the new caret is primary.</summary>
    public static Carets? AddVertical(TextDocument d, Carets c, int direction, int tabSize, int? column = null)
    {
        var edge = direction < 0 ? c.Items[0] : c.Items[^1];
        int line = d.GetLineByOffset(edge.Caret).LineNumber + direction;
        if (line < 1 || line > d.LineCount) return null;
        int at = OffsetAtColumn(d, line, column ?? VisualColumn(d, edge.Caret, tabSize), tabSize);
        return Add(c, Sel.At(at));
    }

    /// <summary>Alt+click: a caret at <paramref name="offset"/>, or when a caret or selection is there and others remain, removes that one.</summary>
    public static Carets AddOrRemoveAt(Carets c, int offset)
    {
        int hit = -1;
        for (int i = 0; i < c.Count; i++)
            if (c.Items[i].Start <= offset && offset <= c.Items[i].End) hit = i;
        if (hit < 0) return Add(c, Sel.At(offset)) ?? c;
        if (c.Count == 1) return c;
        var rest = c.Items.Where((_, i) => i != hit).ToList();
        return Carets.Create(rest, Math.Min(c.Primary > hit ? c.Primary - 1 : c.Primary, rest.Count - 1));
    }

    private static Carets? Add(Carets c, Sel added)
    {
        var all = new List<Sel>(c.Items) { added };
        var result = Carets.Create(all, all.Count - 1);
        return result.Count == c.Count ? null : result;
    }

    /// <summary>One caret per row of a column selection, the last row primary; selections keep the columns they cover.</summary>
    public static Carets FromRows(IReadOnlyList<Sel> rows) => Carets.Create(rows, rows.Count - 1);

    // ---- occurrences ----

    public static (int Start, int End) WordAt(TextDocument d, int offset)
    {
        int s = offset, e = offset;
        while (s > 0 && IsWordChar(d.GetCharAt(s - 1))) s--;
        while (e < d.TextLength && IsWordChar(d.GetCharAt(e))) e++;
        return (s, e);
    }

    private static bool IsWholeWord(TextDocument d, int start, int length) =>
        (start == 0 || !IsWordChar(d.GetCharAt(start - 1))) && (start + length >= d.TextLength || !IsWordChar(d.GetCharAt(start + length)));

    /// <summary>
    /// Ctrl+D. An empty primary caret first selects the word under it (and later hits must be whole words, <paramref name="wholeWord"/>);
    /// then each call adds the next occurrence of the primary selection, wrapping around the end, as the new primary. Null when there is nothing to add.
    /// </summary>
    public static Carets? AddNextOccurrence(TextDocument d, Carets c, ref bool wholeWord) => Step(d, c, ref wholeWord, keepCurrent: true);

    /// <summary>Ctrl+K Ctrl+D: like <see cref="AddNextOccurrence"/> but the current primary selection is dropped.</summary>
    public static Carets? SkipOccurrence(TextDocument d, Carets c, ref bool wholeWord) => Step(d, c, ref wholeWord, keepCurrent: false);

    private static Carets? Step(TextDocument d, Carets c, ref bool wholeWord, bool keepCurrent)
    {
        var p = c.PrimarySel;
        if (p.IsEmpty)
        {
            var (s, e) = WordAt(d, p.Caret);
            if (e == s) return null;
            wholeWord = true;
            return Carets.Create([.. c.Items.Select((x, i) => i == c.Primary ? new Sel(s, e) : x)], c.Primary);
        }
        string needle = d.GetText(p.Start, p.Length);
        int from = p.End;
        for (int pass = 0; pass < 2; pass++)
        {
            while (from <= d.TextLength - needle.Length)
            {
                int hit = d.IndexOf(needle, from, d.TextLength - from, StringComparison.Ordinal);
                if (hit < 0) break;
                from = hit + 1;
                if (wholeWord && !IsWholeWord(d, hit, needle.Length)) continue;
                if (c.Items.Any(a => a.Start < hit + needle.Length && hit < a.End)) continue;
                var list = new List<Sel>(c.Items);
                var added = new Sel(hit, hit + needle.Length);
                if (keepCurrent) list.Add(added); else list[c.Primary] = added;
                return Carets.Create(list, keepCurrent ? list.Count - 1 : c.Primary);
            }
            from = 0;
        }
        return null;
    }

    /// <summary>Ctrl+Shift+L: every occurrence of the primary selection (of the word under an empty caret). The occurrence at the primary stays primary.</summary>
    public static Carets? AllOccurrences(TextDocument d, Carets c, ref bool wholeWord)
    {
        var p = c.PrimarySel;
        if (p.IsEmpty)
        {
            var (s, e) = WordAt(d, p.Caret);
            if (e == s) return null;
            wholeWord = true;
            p = new Sel(s, e);
        }
        string needle = d.GetText(p.Start, p.Length);
        var hits = new List<Sel>();
        for (int from = 0; from <= d.TextLength - needle.Length;)
        {
            int hit = d.IndexOf(needle, from, d.TextLength - from, StringComparison.Ordinal);
            if (hit < 0) break;
            from = hit + needle.Length;
            if (!wholeWord || IsWholeWord(d, hit, needle.Length)) hits.Add(new Sel(hit, hit + needle.Length));
        }
        int primary = hits.FindIndex(h => h.Start == p.Start);
        return hits.Count == 0 ? null : Carets.Create(hits, primary < 0 ? hits.Count - 1 : primary);
    }

    // ---- clipboard ----

    /// <summary>The texts of the non-empty selections, one per line, in document order; null when nothing is selected.</summary>
    public static string? CopyText(TextDocument d, Carets c, string newLine)
    {
        var parts = c.Items.Where(s => !s.IsEmpty).Select(s => d.GetText(s.Start, s.Length)).ToList();
        return parts.Count == 0 ? null : string.Join(newLine, parts);
    }

    /// <summary>The lines of <paramref name="text"/> when there are exactly <paramref name="count"/> of them (one final line break is ignored); null otherwise, so the whole text goes to every caret.</summary>
    public static string[]? SplitForCarets(string text, int count)
    {
        if (count < 2) return null;
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        if (lines.Length > 1 && lines[^1].Length == 0) lines = lines[..^1];
        return lines.Length == count ? lines : null;
    }
}

/// <summary>One caret's part of a multi-caret edit: the replacement and the selection it leaves, relative to the text after that edit alone.</summary>
public readonly record struct Planned(TextEdit Edit, Sel Local, int Index);

/// <summary>
/// Edits over all carets as one transaction. A <c>Plan…</c> function describes what each caret does; <see cref="Apply"/> turns the plan
/// into one <see cref="Doc.Apply"/> (one revision, one undo step that gives back every caret) and returns the carets that follow.
/// </summary>
public static class MultiEdit
{
    // ---- plans ----

    /// <summary>Replaces every selection with <paramref name="text"/>, or with the matching one of <paramref name="pieces"/> when given.</summary>
    public static List<Planned> PlanText(Carets c, string text, string[]? pieces = null)
    {
        var plan = new List<Planned>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var s = c.Items[i];
            string t = pieces?[i] ?? text;
            plan.Add(new(new(s.Start, s.Length, t), Sel.At(s.Start + t.Length), i));
        }
        return plan;
    }

    /// <summary>Typing of one character through <paramref name="rules"/> (auto-closing pairs, smart keys); a caret no rule handles inserts the character.</summary>
    public static List<Planned> PlanTyped(TextDocument d, Carets c, string typed, IReadOnlyList<ITypingRule> rules)
    {
        var plan = new List<Planned>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var s = c.Items[i];
            TextEdit edit = default;
            Sel local = default;
            bool done = false;
            if (typed.Length == 1)
                foreach (var rule in rules)
                    if (done = rule.TryPlan(d, s, typed[0], out edit, out local)) break;
            if (!done)
            {
                edit = new(s.Start, s.Length, typed);
                local = Sel.At(s.Start + typed.Length);
            }
            plan.Add(new(edit, local, i));
        }
        return plan;
    }

    /// <summary>Enter at every caret: comment continuation when <paramref name="style"/> is given, else the indentation of the line.</summary>
    public static List<Planned> PlanLineBreaks(TextDocument d, Carets c, string newLine, CommentStyle? style)
    {
        var plan = new List<Planned>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var s = c.Items[i];
            if (style is null || !CommentContinuation.TryPlan(d, s, style, newLine, out var edit, out var local)) LineBreak.Plan(d, s, newLine, out edit, out local);
            plan.Add(new(edit, local, i));
        }
        return plan;
    }

    /// <summary>
    /// Backspace or Delete at every caret: a selection is removed, an empty caret removes one character (a pair of brackets or quotes
    /// between the caret when <paramref name="pairs"/>) or, with <paramref name="word"/>, up to the word border.
    /// </summary>
    public static List<Planned> PlanDelete(TextDocument d, Carets c, bool backwards, bool word, bool pairs)
    {
        var plan = new List<Planned>(c.Count);
        for (int i = 0; i < c.Count; i++)
        {
            var s = c.Items[i];
            TextEdit edit;
            Sel local;
            if (!s.IsEmpty) { edit = new(s.Start, s.Length, ""); local = Sel.At(s.Start); }
            else if (backwards && !word && pairs && PairedCharacters.TryPlanBackspace(d, s, out edit, out local)) { }
            else
            {
                int other = TextUtilities.GetNextCaretPosition(d, s.Caret, backwards ? LogicalDirection.Backward : LogicalDirection.Forward,
                    word ? CaretPositioningMode.WordStartOrSymbol : CaretPositioningMode.Normal);
                int start = other < 0 ? s.Caret : Math.Min(s.Caret, other), end = other < 0 ? s.Caret : Math.Max(s.Caret, other);
                edit = new(start, end - start, "");
                local = Sel.At(start);
            }
            plan.Add(new(edit, local, i));
        }
        return plan;
    }

    // ---- applying ----

    /// <summary>Orders the plan and drops edits that overlap an earlier one. <paramref name="carets"/> are the carets after all the kept edits, in the order of <paramref name="kept"/> (not yet merged);
    /// <paramref name="primary"/> is the kept edit of the primary caret, or the nearest before it.</summary>
    public static List<Planned> Assemble(IReadOnlyList<Planned> plan, Carets before, out List<Sel> carets, out int primary)
    {
        var kept = new List<Planned>(plan.Count);
        carets = new List<Sel>(plan.Count);
        primary = 0;
        int delta = 0, end = -1;
        foreach (var p in plan.OrderBy(p => p.Edit.Start).ThenBy(p => p.Index))
        {
            if (p.Edit.Start < end) continue;
            end = p.Edit.Start + p.Edit.Length;
            if (p.Index <= before.Primary) primary = kept.Count;
            kept.Add(p);
            carets.Add(new(p.Local.Anchor + delta, p.Local.Caret + delta));
            delta += p.Edit.Text.Length - p.Edit.Length;
        }
        return kept;
    }

    /// <summary>The carets after an edit, as the text view knows them and as byte positions of the new revision, one per selection in the same order.</summary>
    public sealed record Result(EditResult Edit, Carets Carets, IReadOnlyList<TextSelection> Bytes);

    /// <summary>
    /// Applies the plan as one transaction: one revision and one undo step that gives back every caret. Text goes through the document in
    /// bytes (byte-exact); a lossy single-byte encoding goes through the text view instead, so the loss report of typed characters keeps
    /// working. The byte positions of the new carets are worked out from the edits, not looked up again, so the cost stays linear in the carets.
    /// </summary>
    public static Result Apply(Doc doc, AvaloniaEditBackend backend, Carets before, IReadOnlyList<Planned> plan)
    {
        var kept = Assemble(plan, before, out var keptCarets, out int keptPrimary);
        var text = backend.TextDocument;
        var codec = doc.Codec;

        var wanted = new SortedSet<int>();
        foreach (var s in before.Items) { wanted.Add(s.Start); wanted.Add(s.End); }
        foreach (var p in kept) { wanted.Add(p.Edit.Start); wanted.Add(p.Edit.Start + p.Edit.Length); }
        var offsets = wanted.ToArray();
        var bytesAt = new Dictionary<int, long>(offsets.Length);
        var converted = backend.ToByteOffsets(offsets);
        for (int i = 0; i < offsets.Length; i++) bytesAt[offsets[i]] = converted[i];

        // new carets in bytes: where the edit starts (shifted by the edits before it) plus the bytes of the text up to the caret
        var inserted = new byte[kept.Count][];
        var byteEdits = new List<ByteEdit>(kept.Count);
        var keptBytes = new List<TextSelection>(kept.Count);
        long shift = 0;
        for (int i = 0; i < kept.Count; i++)
        {
            var e = kept[i].Edit;
            long b0 = bytesAt[e.Start], b1 = bytesAt[e.Start + e.Length];
            var bytes = inserted[i] = codec.EncodeLossy(e.Text);
            if (e.Length > 0 || e.Text.Length > 0) byteEdits.Add(new(new(b0, b1), bytes));
            long Position(int localOffset)
            {
                int k = localOffset - e.Start;
                if (k == e.Text.Length) return b0 + shift + bytes.Length;
                if (k < e.Text.Length) return b0 + shift + codec.EncodeLossy(e.Text.AsSpan(0, k)).Length;
                return b0 + shift + bytes.Length + codec.EncodeLossy(text.GetText(e.Start + e.Length, k - e.Text.Length)).Length;
            }
            keptBytes.Add(new(Position(kept[i].Local.Anchor), Position(kept[i].Local.Caret)));
            shift += bytes.Length - (b1 - b0);
        }
        // the same merge on characters and on bytes gives the same selections in the same order
        var after = Carets.Create(keptCarets, keptPrimary);
        var afterSet = SelectionSet.Create(keptBytes, keptPrimary);
        var afterBytes = afterSet.Items;

        IReadOnlyList<ByteRange> Ranges(IEnumerable<TextSelection> sels) => [.. sels.Select(s => s.ToRange())];
        var beforeRanges = Ranges(before.PrimaryLast().Select(s => new TextSelection(bytesAt[s.Start], bytesAt[s.End])));
        var afterRanges = Ranges(afterBytes.Where((_, i) => i != afterSet.Primary).Append(afterBytes[afterSet.Primary]));

        EditResult result;
        using (doc.BeginUndoGroup(beforeRanges, () => afterRanges))
        {
            if (codec.IsLossy)
            {
                using (text.RunUpdate())
                    for (int k = kept.Count - 1; k >= 0; k--)
                    {
                        var e = kept[k].Edit;
                        if (e.Length > 0 || e.Text.Length > 0) text.Replace(e.Start, e.Length, e.Text);
                    }
                result = new(true, doc.Revision, ConflictReason.None);
            }
            else result = doc.Apply(byteEdits);
        }
        return new(result, after, afterBytes);
    }
}

/// <summary>A rule that turns one typed character at one selection into an edit (auto-closing pairs, smart keys for web).</summary>
public interface ITypingRule
{
    /// <summary>False when the rule does not apply and the character is inserted as typed; otherwise the edit and the selection after it alone.</summary>
    bool TryPlan(TextDocument d, Sel sel, char typed, out TextEdit edit, out Sel result);
}

/// <summary><see cref="PairedCharacters"/> as a typing rule.</summary>
public sealed class PairedCharactersRule : ITypingRule
{
    public static readonly PairedCharactersRule Instance = new();
    public bool TryPlan(TextDocument d, Sel sel, char typed, out TextEdit edit, out Sel result) => PairedCharacters.TryPlanType(d, sel, typed, out edit, out result);
}

/// <summary>Enter: a line break that keeps the indentation of the line it breaks.</summary>
public static class LineBreak
{
    /// <summary>The edit for one selection; <paramref name="result"/> is the caret after <paramref name="edit"/> alone.</summary>
    public static void Plan(TextDocument d, Sel sel, string newLine, out TextEdit edit, out Sel result)
    {
        var line = d.GetLineByOffset(sel.Start);
        int indent = 0;
        while (indent < sel.Start - line.Offset && d.GetCharAt(line.Offset + indent) is ' ' or '\t') indent++;
        string text = newLine + d.GetText(line.Offset, indent);
        edit = new(sel.Start, sel.Length, text);
        result = Sel.At(sel.Start + text.Length);
    }
}

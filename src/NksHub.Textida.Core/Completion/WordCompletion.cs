namespace NksHub.Textida.Core.Completion;

/// <summary>
/// The text of one open document for word completion. <paramref name="Caret"/> is the offset of the caret in <paramref name="Text"/> when this is the document being
/// edited, otherwise -1; the word the caret is in is the one being typed, so it is not offered. <paramref name="Index"/> is the index of <paramref name="Text"/> when the caller
/// keeps one between offers; without it the text is scanned (and <paramref name="Text"/> is not needed when there is no caret). <paramref name="Near"/> is the text around the
/// caret of the document being edited; with it the words that stand nearest to the caret come first.
/// </summary>
public readonly record struct CompletionSource(string Text, int Caret = -1, WordIndex? Index = null, CompletionNear? Near = null);

/// <summary>
/// A stretch of the document being edited: <paramref name="Caret"/> is the offset of the caret in <paramref name="Text"/>. <paramref name="CutStart"/> and <paramref name="CutEnd"/>
/// tell that the stretch begins or ends inside the document, so a word touching that edge may be cut short and is left out.
/// </summary>
public readonly record struct CompletionNear(string Text, int Caret, bool CutStart = false, bool CutEnd = false);

/// <summary>
/// Completion of words from the text of open documents. A word is letters, digits and underscores, as everywhere else in the editor. The text a caller gives is
/// what may be offered: a document whose values are hidden (a secrets file) is given with the values already masked, so nothing hidden can be offered.
/// </summary>
public static class WordCompletion
{
    public static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || (c >= 128 && char.IsLetterOrDigit(c));

    /// <summary>The offset where the word in front of <paramref name="caret"/> starts (the caret itself when no word character is in front of it).</summary>
    public static int PrefixStart(ReadOnlySpan<char> text, int caret)
    {
        int start = Math.Min(caret, text.Length);
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        return start;
    }

    /// <summary>The word the caret is in (its characters in front of the caret make the prefix), or null.</summary>
    private static string? TypedWord(string text, int caret)
    {
        if (caret <= 0 || caret > text.Length || !IsWordChar(text[caret - 1])) return null;
        int start = caret - 1, end = caret;
        while (start > 0 && IsWordChar(text[start - 1])) start--;
        while (end < text.Length && IsWordChar(text[end])) end++;
        return text[start..end];
    }

    private struct Count
    {
        public int Total;
        public bool InFirstSource;
        /// <summary>Characters from the caret to the nearest occurrence in the stretch around it, plus one; 0 when the word does not occur there.</summary>
        public int Nearness;
    }

    /// <summary>
    /// The words that start with <paramref name="prefix"/> (ignoring case) and are longer than it, from all the sources, at most <paramref name="max"/>. The first
    /// source is the document being edited: its words come first, those nearest to the caret before the others (when the source tells the text around the caret);
    /// then the more often a word occurs, the sooner it comes; then alphabetical.
    /// </summary>
    public static List<string> Words(IReadOnlyList<CompletionSource> sources, string prefix, int max)
    {
        var counts = new Dictionary<string, Count>(StringComparer.Ordinal);
        for (int k = 0; k < sources.Count; k++)
        {
            var index = sources[k].Index ?? WordIndex.Build(sources[k].Text);
            string? typed = TypedWord(sources[k].Text, sources[k].Caret);
            var nearest = k == 0 && sources[k].Near is { } near ? Nearest(near, prefix) : null;
            index.ForEachStartingWith(prefix, (word, total) =>
            {
                if (word.Length <= prefix.Length) return;
                if (typed is not null && word == typed) total--;
                if (total <= 0) return;
                ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(counts, word, out _);
                count.Total += total;
                if (k == 0) count.InFirstSource = true;
                if (nearest is not null && nearest.TryGetValue(word, out int nearness)) count.Nearness = nearness;
            });
        }
        // the best `max` of possibly very many words: a heap that keeps the worst on top is cheaper than sorting them all
        var best = new PriorityQueue<KeyValuePair<string, Count>, KeyValuePair<string, Count>>(Comparer<KeyValuePair<string, Count>>.Create((a, b) => Rank(b, a)));
        foreach (var entry in counts)
        {
            best.Enqueue(entry, entry);
            if (best.Count > max) best.Dequeue();
        }
        var result = new List<KeyValuePair<string, Count>>(best.Count);
        while (best.Count > 0) result.Add(best.Dequeue());
        result.Sort(Rank);
        return [.. result.Select(p => p.Key)];
    }

    /// <summary>
    /// Gives an all-lower-case <paramref name="word"/> the case the user typed: an all-capitals prefix (two letters or more) makes it upper case, a capitalised one capitalises it.
    /// A word with capitals of its own, or a prefix typed in lower case, leaves it as it is.
    /// </summary>
    public static string MatchCase(string word, string prefix)
    {
        if (prefix.Length == 0 || word.Any(char.IsUpper) || !char.IsUpper(prefix[0])) return word;
        if (prefix.Length > 1 && prefix.All(c => !char.IsLower(c))) return word.ToUpperInvariant();
        return prefix.Skip(1).Any(char.IsUpper) ? word : char.ToUpperInvariant(word[0]) + word[1..];
    }

    /// <summary>
    /// The words of the stretch around the caret that start with <paramref name="prefix"/> and are longer than it, each with the number of characters between the caret and
    /// its nearest occurrence, plus one. The word being typed is not an occurrence of itself, and a word touching a cut edge of the stretch is left out.
    /// </summary>
    private static Dictionary<string, int> Nearest(CompletionNear near, string prefix)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var lookup = result.GetAlternateLookup<ReadOnlySpan<char>>();
        var text = near.Text.AsSpan();
        for (int i = 0; i < text.Length;)
        {
            if (!IsWordChar(text[i])) { i++; continue; }
            int start = i;
            while (i < text.Length && IsWordChar(text[i])) i++;
            if (i - start <= prefix.Length || (near.CutStart && start == 0) || (near.CutEnd && i == text.Length)) continue;
            if (near.Caret > start && near.Caret <= i) continue;
            if (!text.Slice(start, prefix.Length).Equals(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            int nearness = (i <= near.Caret ? near.Caret - i : start - near.Caret) + 1;
            ref int best = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(lookup, text[start..i], out _);
            if (best == 0 || nearness < best) best = nearness;
        }
        return result;
    }

    private static int Far(int nearness) => nearness == 0 ? int.MaxValue : nearness;

    private static int Rank(KeyValuePair<string, Count> a, KeyValuePair<string, Count> b)
    {
        int c = b.Value.InFirstSource.CompareTo(a.Value.InFirstSource);
        if (c != 0) return c;
        c = Far(a.Value.Nearness).CompareTo(Far(b.Value.Nearness));
        if (c != 0) return c;
        c = b.Value.Total.CompareTo(a.Value.Total);
        if (c != 0) return c;
        c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
    }
}

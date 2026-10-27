namespace NksHub.Textida.Core.Completion;

/// <summary>
/// The text of one open document for word completion. <paramref name="Caret"/> is the offset of the caret in <paramref name="Text"/> when this is the document being
/// edited, otherwise -1; the word the caret is in is the one being typed, so it is not offered.
/// </summary>
public readonly record struct CompletionSource(string Text, int Caret = -1);

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

    private struct Count
    {
        public int Total;
        public bool InFirstSource;
    }

    /// <summary>
    /// The words that start with <paramref name="prefix"/> (ignoring case) and are longer than it, from all the sources, at most <paramref name="max"/>. The first
    /// source is the document being edited: its words come first; then the more often a word occurs, the sooner it comes; then alphabetical.
    /// </summary>
    public static List<string> Words(IReadOnlyList<CompletionSource> sources, string prefix, int max)
    {
        var counts = new Dictionary<string, Count>(StringComparer.Ordinal);
        var lookup = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int k = 0; k < sources.Count; k++)
        {
            var text = sources[k].Text.AsSpan();
            int caret = sources[k].Caret;
            for (int i = 0; i < text.Length;)
            {
                if (!IsWordChar(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && IsWordChar(text[i])) i++;
                int length = i - start;
                bool beingTyped = caret > start && caret <= i;
                if (length <= prefix.Length || beingTyped) continue;
                var word = text.Slice(start, length);
                if (prefix.Length > 0 && !word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                ref var count = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(lookup, word, out bool existed);
                if (!existed) count = default;
                count.Total++;
                if (k == 0) count.InFirstSource = true;
            }
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

    private static int Rank(KeyValuePair<string, Count> a, KeyValuePair<string, Count> b)
    {
        int c = b.Value.InFirstSource.CompareTo(a.Value.InFirstSource);
        if (c != 0) return c;
        c = b.Value.Total.CompareTo(a.Value.Total);
        if (c != 0) return c;
        c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
    }
}

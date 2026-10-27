using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Completion;

/// <summary>
/// The distinct words of one text with how often each occurs, sorted ignoring case, so the words that start with a prefix are one range found by binary search.
/// Built once per revision of a document; immutable.
/// </summary>
public sealed class WordIndex
{
    private readonly string[] _words;
    private readonly int[] _totals;

    private WordIndex(string[] words, int[] totals) => (_words, _totals) = (words, totals);

    public int Count => _words.Length;

    public static WordIndex Build(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lookup = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        var span = text.AsSpan();
        for (int i = 0; i < span.Length;)
        {
            if (!WordCompletion.IsWordChar(span[i])) { i++; continue; }
            int start = i;
            while (i < span.Length && WordCompletion.IsWordChar(span[i])) i++;
            CollectionsMarshal.GetValueRefOrAddDefault(lookup, span[start..i], out _)++;
        }
        var entries = new KeyValuePair<string, int>[counts.Count];
        ((ICollection<KeyValuePair<string, int>>)counts).CopyTo(entries, 0);
        Array.Sort(entries, static (a, b) =>
        {
            int c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });
        return new WordIndex([.. entries.Select(e => e.Key)], [.. entries.Select(e => e.Value)]);
    }

    /// <summary>Calls <paramref name="visit"/> with every word that starts with <paramref name="prefix"/> (ignoring case) and its number of occurrences.</summary>
    public void ForEachStartingWith(string prefix, Action<string, int> visit)
    {
        for (int i = LowerBound(prefix); i < _words.Length && Compare(_words[i], prefix) == 0; i++) visit(_words[i], _totals[i]);
    }

    /// <summary>The words compared by their first characters only, as long as the prefix.</summary>
    private static int Compare(string word, string prefix) =>
        MemoryExtensions.CompareTo(word.AsSpan(0, Math.Min(word.Length, prefix.Length)), prefix, StringComparison.OrdinalIgnoreCase);

    private int LowerBound(string prefix)
    {
        int lo = 0, hi = _words.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = Compare(_words[mid], prefix);
            if (c < 0) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

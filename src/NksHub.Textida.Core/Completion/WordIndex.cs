using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Completion;

/// <summary>
/// The distinct words of one text with how often each occurs, sorted ignoring case, so the words that start with a prefix are one range found by binary search.
/// Built once when a document opens; an edit then changes it by the lines it touched (<see cref="Change"/>), so it never needs a rebuild.
/// </summary>
public sealed class WordIndex
{
    private readonly List<string> _words;
    private readonly List<int> _totals;

    private WordIndex(List<string> words, List<int> totals) => (_words, _totals) = (words, totals);

    public int Count => _words.Count;

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
        Array.Sort(entries, static (a, b) => CompareFull(a.Key, b.Key));
        return new WordIndex([.. entries.Select(e => e.Key)], [.. entries.Select(e => e.Value)]);
    }

    /// <summary>
    /// Takes the words of <paramref name="removed"/> out and puts those of <paramref name="added"/> in. Both are whole lines (a word never spans lines): the lines
    /// an edit touched, as they were before it and as they are after it.
    /// </summary>
    public void Change(ReadOnlySpan<char> removed, ReadOnlySpan<char> added)
    {
        Apply(removed, -1);
        Apply(added, 1);
    }

    private void Apply(ReadOnlySpan<char> text, int delta)
    {
        for (int i = 0; i < text.Length;)
        {
            if (!WordCompletion.IsWordChar(text[i])) { i++; continue; }
            int start = i;
            while (i < text.Length && WordCompletion.IsWordChar(text[i])) i++;
            var word = text[start..i];
            int at = Find(word, out bool found);
            if (!found)
            {
                _words.Insert(at, word.ToString());
                _totals.Insert(at, 0);
            }
            if ((_totals[at] += delta) <= 0)
            {
                _words.RemoveAt(at);
                _totals.RemoveAt(at);
            }
        }
    }

    private int Find(ReadOnlySpan<char> word, out bool found)
    {
        int lo = 0, hi = _words.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            int c = CompareFull(word, _words[mid]);
            if (c == 0) { found = true; return mid; }
            if (c > 0) lo = mid + 1; else hi = mid;
        }
        found = false;
        return lo;
    }

    private static int CompareFull(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        int c = a.CompareTo(b, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : a.CompareTo(b, StringComparison.Ordinal);
    }

    /// <summary>Calls <paramref name="visit"/> with every word that starts with <paramref name="prefix"/> (ignoring case) and its number of occurrences.</summary>
    public void ForEachStartingWith(string prefix, Action<string, int> visit)
    {
        for (int i = LowerBound(prefix); i < _words.Count && Compare(_words[i], prefix) == 0; i++) visit(_words[i], _totals[i]);
    }

    /// <summary>The words compared by their first characters only, as long as the prefix.</summary>
    private static int Compare(string word, string prefix) =>
        MemoryExtensions.CompareTo(word.AsSpan(0, Math.Min(word.Length, prefix.Length)), prefix, StringComparison.OrdinalIgnoreCase);

    private int LowerBound(string prefix)
    {
        int lo = 0, hi = _words.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (Compare(_words[mid], prefix) < 0) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

namespace NksHub.Textida.Workbench.Matching;

/// <summary>One match of <see cref="PathIndex.Query"/>.</summary>
public readonly record struct PathMatch(int Index, int Score);

/// <summary>
/// A list of paths prepared for matching while the user types ("Go to Anything"). Each word of the query must match the path; a word is
/// better when it is the start of the file name, then inside the name, then at the start of a folder, then anywhere in the path, and only then
/// as letters in order. Words may come in any order, so "mvc ctrl" finds <c>Mvc/Controllers/HomeController.cs</c>. Case and accents do not matter.
/// </summary>
public sealed class PathIndex
{
    private readonly string[] _folded;
    private readonly int[] _nameStart;

    public PathIndex(IReadOnlyList<string> paths)
    {
        Paths = paths;
        _folded = new string[paths.Count];
        _nameStart = new int[paths.Count];
        for (int i = 0; i < paths.Count; i++)
        {
            var folded = TextMatching.Fold(paths[i]).Replace('\\', '/');
            _folded[i] = folded;
            _nameStart[i] = folded.LastIndexOf('/') + 1;
        }
    }

    public IReadOnlyList<string> Paths { get; }

    /// <summary>Matches best first, at most <paramref name="limit"/>; an empty query matches nothing (the caller shows its own default list).</summary>
    public IReadOnlyList<PathMatch> Query(string query, int limit, CancellationToken ct = default)
    {
        var words = TextMatching.Fold(query).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        var found = new List<PathMatch>();
        for (int i = 0; i < _folded.Length; i++)
        {
            if ((i & 0x3FF) == 0) ct.ThrowIfCancellationRequested();
            if (Score(words, i) is { } score) found.Add(new PathMatch(i, score));
        }
        found.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : string.CompareOrdinal(Paths[a.Index], Paths[b.Index]));
        return found.Count > limit ? found.GetRange(0, limit) : found;
    }

    private int? Score(string[] words, int i)
    {
        string path = _folded[i];
        int nameAt = _nameStart[i];
        int total = 0;
        foreach (var word in words)
        {
            int best;
            int inName = path.IndexOf(word, nameAt, StringComparison.Ordinal);
            if (inName == nameAt) best = 100;
            else if (inName > nameAt) best = 80;
            else
            {
                int inPath = path.IndexOf(word, StringComparison.Ordinal);
                if (inPath >= 0) best = inPath == 0 || path[inPath - 1] == '/' ? 60 : 50;
                else if (InOrder(word, path, nameAt)) best = 40;
                else if (InOrder(word, path, 0)) best = 20;
                else return null;
            }
            total += best;
        }
        return total - Math.Min(path.Length / 8, 15);
    }

    private static bool InOrder(string word, string text, int from)
    {
        int w = 0;
        for (int t = from; t < text.Length && w < word.Length; t++)
            if (text[t] == word[w]) w++;
        return w == word.Length;
    }
}

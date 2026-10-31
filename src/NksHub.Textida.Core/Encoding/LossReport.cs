using System.Text;

namespace NksHub.Textida.Core.Encodings;

/// <param name="CodePoint">The character that cannot be written (a lone surrogate keeps its surrogate value).</param>
/// <param name="CharIndex">UTF-16 index in the text the report refers to.</param>
/// <param name="Line">1-based line.</param>
/// <param name="Column">1-based column in UTF-16 units.</param>
/// <param name="ByteOffset">Offset in the output where the replacement would start.</param>
/// <param name="FromInvalidBytes">The character stands for undecodable source bytes, not for real text.</param>
public readonly record struct LossEntry(int CodePoint, int CharIndex, int Line, int Column, long ByteOffset, bool FromInvalidBytes)
{
    public string Display => Rune.IsValid(CodePoint) ? new Rune(CodePoint).ToString() : $"U+{CodePoint:X4}";
}

public readonly record struct LossGroup(int CodePoint, int Count);

/// <summary>Characters that the target encoding cannot represent, with positions and counts.</summary>
public sealed class LossReport
{
    public static readonly LossReport None = new([], [], 0, false);

    private LossReport(IReadOnlyList<LossEntry> entries, IReadOnlyList<LossGroup> groups, int total, bool truncated)
    {
        Entries = entries; Groups = groups; Total = total; Truncated = truncated;
    }

    /// <summary>Individual occurrences, at most the requested limit; see <see cref="Truncated"/>.</summary>
    public IReadOnlyList<LossEntry> Entries { get; }
    /// <summary>Counts per distinct character, most frequent first.</summary>
    public IReadOnlyList<LossGroup> Groups { get; }
    public int Total { get; }
    public bool Truncated { get; }
    public bool HasLoss => Total > 0;

    /// <summary>Loss of writing the whole <paramref name="text"/> in <paramref name="codec"/>; offsets assume a one-byte replacement.</summary>
    public static LossReport ForText(string text, TextCodec codec)
    {
        var c = new Collector();
        for (int i = 0; i < text.Length;)
        {
            bool ok = Rune.DecodeFromUtf16(text.AsSpan(i), out Rune r, out int used) == System.Buffers.OperationStatus.Done;
            int cp = ok ? r.Value : text[i];
            if (!codec.CanEncode(cp)) c.Add(cp, i, i, false);
            i += used;
        }
        return c.Build(text);
    }

    /// <summary>The loss of a conversion that was counted block by block: the groups and the total, without the single places (they are positions in blocks that are gone).</summary>
    internal static LossReport FromCounts(IReadOnlyDictionary<int, int> counts, int total) =>
        total == 0 ? None : new([], [.. counts.Select(kv => new LossGroup(kv.Key, kv.Value)).OrderByDescending(g => g.Count).ThenBy(g => g.CodePoint)], total, true);

    internal sealed class Collector
    {
        private readonly List<(int Cp, int Index, long Offset, bool Invalid)> _items = [];
        private readonly Dictionary<int, int> _counts = [];
        private readonly int _limit;
        private int _total;

        public Collector(int limit = 1000) => _limit = limit;

        public void Add(int cp, int charIndex, long byteOffset, bool invalid)
        {
            _total++;
            _counts[cp] = _counts.GetValueOrDefault(cp) + 1;
            if (_items.Count < _limit) _items.Add((cp, charIndex, byteOffset, invalid));
        }

        /// <param name="text">The text whose indices were reported; used for line and column.</param>
        public LossReport Build(string text)
        {
            if (_total == 0) return None;
            var starts = LineStarts(text);
            var entries = new List<LossEntry>(_items.Count);
            foreach (var (cp, index, offset, invalid) in _items)
            {
                int line = Array.BinarySearch(starts, index);
                if (line < 0) line = ~line - 1;
                entries.Add(new LossEntry(cp, index, line + 1, index - starts[line] + 1, offset, invalid));
            }
            var groups = _counts.Select(kv => new LossGroup(kv.Key, kv.Value)).OrderByDescending(g => g.Count).ThenBy(g => g.CodePoint).ToList();
            return new LossReport(entries, groups, _total, _total > entries.Count);
        }
    }

    /// <summary>Start index of every line; CR, LF and CRLF each end a line.</summary>
    internal static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (text[i] != '\r' && text[i] != '\n') continue;
            starts.Add(i + 1);
        }
        return starts.ToArray();
    }
}

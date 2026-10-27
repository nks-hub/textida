using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>Fills <paramref name="buffer"/> with the bytes at <paramref name="offset"/> and returns how many it got; 0 means the source ended early.</summary>
public delegate ValueTask<int> ChunkReader(long offset, Memory<byte> buffer, CancellationToken cancellation);

/// <param name="Done">Bytes consumed from the start of the range.</param>
/// <param name="Completed">False when the scan was cancelled before the end of the range.</param>
public readonly record struct ScanOutcome(long Done, bool Completed);

/// <summary>
/// Reads a byte range in chunks of a fixed size into one reused buffer, so the memory of a scan is the chunk, not the range.
/// A cancelled scan returns how far it got instead of throwing; every other error propagates.
/// </summary>
public static class ByteScan
{
    public static async Task<ScanOutcome> RunAsync(long start, long end, int chunkBytes, ChunkReader read, Action<ReadOnlySpan<byte>> consume,
        Func<long, ValueTask>? progress, CancellationToken cancellation)
    {
        if (chunkBytes < 1) throw new ArgumentOutOfRangeException(nameof(chunkBytes));
        var buffer = new byte[chunkBytes];
        long position = start;
        try
        {
            while (position < end)
            {
                cancellation.ThrowIfCancellationRequested();
                int want = (int)Math.Min(chunkBytes, end - position);
                int got = await read(position, buffer.AsMemory(0, want), cancellation).ConfigureAwait(false);
                if (got <= 0) throw new EndOfStreamException($"The source ended at byte {position}, before {end}.");
                consume(buffer.AsSpan(0, got));
                position += got;
                if (progress is not null) await progress(position - start).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return new ScanOutcome(position - start, false);
        }
        return new ScanOutcome(position - start, true);
    }
}

public readonly record struct TextStatisticsResult(long Bytes, long Characters, long Words, long Lines, long LongestLine);

/// <summary>
/// Counts characters, words and lines over decoded chunks. A character split between two chunks is held back until its rest arrives.
/// A line ends at LF, or at CR that is not directly followed by LF (the rule of <c>LineScanner</c>), so a file with N terminators has N + 1 lines.
/// A word is a run of letters and digits; the definition is not decided yet (placeholder).
/// With <paramref name="maxDistinctWords"/> above zero it also tallies characters and lower-cased words; the word table stops growing at that many
/// distinct words (<see cref="WordTableFull"/>) and a word is cut at <paramref name="maxWordLength"/> characters, so the memory of the tables is bounded.
/// </summary>
public sealed class TextStatistics(TextCodec codec, int maxDistinctWords = 0, int maxWordLength = 0)
{
    private const int MaxHeldBytes = 8;

    private byte[] _work = [];
    private int _held;
    private long _characters, _words, _terminators, _lineLength, _longest;
    private bool _inWord, _afterCr;
    private readonly Dictionary<char, long> _characterTally = [];
    private readonly Dictionary<string, long> _wordTally = new(StringComparer.Ordinal);
    private readonly StringBuilder _word = new();

    /// <summary>True when a new distinct word was left out of the table because it already held the maximum.</summary>
    public bool WordTableFull { get; private set; }

    public void Add(ReadOnlySpan<byte> bytes)
    {
        int total = _held + bytes.Length;
        if (_work.Length < total) Array.Resize(ref _work, total);
        bytes.CopyTo(_work.AsSpan(_held));
        int cut = codec.CompleteLength(_work.AsSpan(0, total));
        if (total - cut > MaxHeldBytes) cut = total;
        Count(codec.GetString(_work.AsSpan(0, cut)));
        _held = total - cut;
        if (_held > 0) Buffer.BlockCopy(_work, cut, _work, 0, _held);
    }

    private void Count(string text)
    {
        foreach (char c in text)
        {
            bool second = char.IsLowSurrogate(c);
            if (!second) _characters++;
            if (c == '\r' || c == '\n')
            {
                bool secondOfCrLf = c == '\n' && _afterCr;
                _afterCr = c == '\r';
                EndWord();
                _inWord = false;
                if (secondOfCrLf) continue;
                _terminators++;
                _longest = Math.Max(_longest, _lineLength);
                _lineLength = 0;
                continue;
            }
            _afterCr = false;
            if (!second) _lineLength++;
            bool word = char.IsLetterOrDigit(c);
            if (word && !_inWord) _words++;
            if (maxDistinctWords > 0)
            {
                if (!char.IsWhiteSpace(c) && !char.IsControl(c) && !char.IsSurrogate(c)) _characterTally[c] = _characterTally.GetValueOrDefault(c) + 1;
                if (word && _word.Length < maxWordLength) _word.Append(char.ToLowerInvariant(c));
            }
            if (!word) EndWord();
            _inWord = word;
        }
    }

    private void EndWord()
    {
        if (_word.Length == 0) return;
        var key = _word.ToString();
        _word.Clear();
        if (_wordTally.TryGetValue(key, out var n)) _wordTally[key] = n + 1;
        else if (_wordTally.Count < maxDistinctWords) _wordTally[key] = 1;
        else WordTableFull = true;
    }

    /// <summary>Ends the text of one range so that the next range starts a new character and a new word; its lines go on counting as one text.</summary>
    public void Break()
    {
        Flush();
        EndWord();
        _inWord = false;
        _afterCr = false;
    }

    private void Flush()
    {
        if (_held == 0) return;
        Count(codec.GetString(_work.AsSpan(0, _held)));
        _held = 0;
    }

    public IReadOnlyList<(string Text, long Count)> TopCharacters(int n) =>
        [.. _characterTally.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(n).Select(x => (x.Key.ToString(), x.Value))];

    public IReadOnlyList<(string Text, long Count)> TopWords(int n) =>
        [.. _wordTally.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(n).Select(x => (x.Key, x.Value))];

    /// <summary>Flushes a character cut short by the end of the range and returns the totals; <paramref name="bytes"/> is the size of the range that was fed.</summary>
    public TextStatisticsResult Finish(long bytes)
    {
        Break();
        return new TextStatisticsResult(bytes, _characters, _words, _terminators + 1, Math.Max(_longest, _lineLength));
    }
}

/// <summary>Hash algorithms of the hash command, over raw bytes. The full list is not decided yet (placeholder: what <see cref="IncrementalHash"/> offers, and CRC-32).</summary>
public static class BlockHash
{
    /// <summary>CRC-32 (IEEE 802.3, the one of zip and PNG) is not in <see cref="IncrementalHash"/>; this name selects the code below.</summary>
    public static readonly HashAlgorithmName Crc32 = new("CRC32");

    private static readonly uint[] Crc32Table = Enumerable.Range(0, 256).Select(i =>
    {
        uint c = (uint)i;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    /// <summary>Length of the lower-case hex digest of each algorithm, to tell which one a pasted hash belongs to.</summary>
    public static string? IdForHexLength(int length) => length switch { 8 => "crc32", 32 => "md5", 40 => "sha1", 64 => "sha256", 96 => "sha384", 128 => "sha512", _ => null };

    public static IReadOnlyList<(string Id, string Label, HashAlgorithmName Algorithm)> Algorithms { get; } =
    [
        ("md5", "MD5", HashAlgorithmName.MD5),
        ("sha1", "SHA-1", HashAlgorithmName.SHA1),
        ("sha256", "SHA-256", HashAlgorithmName.SHA256),
        ("sha384", "SHA-384", HashAlgorithmName.SHA384),
        ("sha512", "SHA-512", HashAlgorithmName.SHA512),
        ("crc32", "CRC-32", Crc32),
    ];

    public static bool TryGet(string id, out (string Id, string Label, HashAlgorithmName Algorithm) algorithm)
    {
        foreach (var a in Algorithms)
            if (string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) { algorithm = a; return true; }
        algorithm = default;
        return false;
    }

    /// <summary>Lower-case hex digest of the bytes of a range read in chunks.</summary>
    public static async Task<(string? Hex, ScanOutcome Outcome)> ComputeAsync(HashAlgorithmName algorithm, long start, long end, int chunkBytes, ChunkReader read,
        Func<long, ValueTask>? progress, CancellationToken cancellation)
    {
        if (algorithm == Crc32)
        {
            uint crc = 0xFFFFFFFFu;
            var scanned = await ByteScan.RunAsync(start, end, chunkBytes, read, bytes =>
            {
                foreach (var b in bytes) crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }, progress, cancellation).ConfigureAwait(false);
            return (scanned.Completed ? (~crc).ToString("x8") : null, scanned);
        }
        using var hash = IncrementalHash.CreateHash(algorithm);
        var outcome = await ByteScan.RunAsync(start, end, chunkBytes, read, bytes => hash.AppendData(bytes), progress, cancellation).ConfigureAwait(false);
        return (outcome.Completed ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null, outcome);
    }
}

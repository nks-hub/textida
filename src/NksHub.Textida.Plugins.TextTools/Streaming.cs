using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;

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

/// <summary>
/// The hash command over raw bytes. The algorithms are those of the checksum dialog of the hex view (<see cref="Checksums"/>): every CRC variant, Adler-32, the sums,
/// xxHash and XXH3, MD5, SHA-1 and SHA-2.
/// </summary>
public static class BlockHash
{
    /// <summary>CRC-32 (IEEE 802.3, the one of zip and PNG); the name selects the checksum of that id in the overload that takes a <see cref="HashAlgorithmName"/>.</summary>
    public static readonly HashAlgorithmName Crc32 = new("CRC32");

    /// <summary>The algorithm a pasted hash of this many hex digits belongs to, for the lengths where one of the common algorithms is meant; null for any other length.</summary>
    public static string? IdForHexLength(int length) => length switch { 8 => "crc-32", 32 => "md5", 40 => "sha1", 64 => "sha256", 96 => "sha384", 128 => "sha512", _ => null };

    public static IReadOnlyList<ChecksumSpec> Algorithms => Checksums.All;

    /// <summary>The algorithm with this id (<c>crc32</c>, the id this command had before the list was shared, finds <c>crc-32</c>).</summary>
    public static bool TryGet(string id, [NotNullWhen(true)] out ChecksumSpec? algorithm)
    {
        algorithm = Checksums.Find(string.Equals(id, "crc32", StringComparison.OrdinalIgnoreCase) ? "crc-32" : id);
        return algorithm is not null;
    }

    /// <summary>Lower-case hex digest of the bytes of a range read in chunks; null when the run was cancelled.</summary>
    public static async Task<(string? Hex, ScanOutcome Outcome)> ComputeAsync(ChecksumSpec algorithm, long start, long end, int chunkBytes, ChunkReader read,
        Func<long, ValueTask>? progress, CancellationToken cancellation)
    {
        using var state = algorithm.Create();
        var outcome = await ByteScan.RunAsync(start, end, chunkBytes, read, bytes => state.Append(bytes), progress, cancellation).ConfigureAwait(false);
        return (outcome.Completed ? state.Finish() : null, outcome);
    }

    public static Task<(string? Hex, ScanOutcome Outcome)> ComputeAsync(HashAlgorithmName algorithm, long start, long end, int chunkBytes, ChunkReader read,
        Func<long, ValueTask>? progress, CancellationToken cancellation) =>
        TryGet(algorithm == Crc32 ? "crc32" : algorithm.Name!.Replace("-", "", StringComparison.Ordinal), out var spec)
            ? ComputeAsync(spec, start, end, chunkBytes, read, progress, cancellation)
            : throw new ArgumentException($"No algorithm named {algorithm.Name}.", nameof(algorithm));
}

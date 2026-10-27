using System.Security.Cryptography;
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
/// </summary>
public sealed class TextStatistics(TextCodec codec)
{
    private const int MaxHeldBytes = 8;

    private byte[] _work = [];
    private int _held;
    private long _characters, _words, _terminators, _lineLength, _longest;
    private bool _inWord, _afterCr;

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
            _inWord = word;
        }
    }

    /// <summary>Flushes a character cut short by the end of the range and returns the totals; <paramref name="bytes"/> is the size of the range that was fed.</summary>
    public TextStatisticsResult Finish(long bytes)
    {
        if (_held > 0)
        {
            Count(codec.GetString(_work.AsSpan(0, _held)));
            _held = 0;
        }
        return new TextStatisticsResult(bytes, _characters, _words, _terminators + 1, Math.Max(_longest, _lineLength));
    }
}

/// <summary>Hash algorithms of the hash command, over raw bytes. The full list is not decided yet (placeholder: what <see cref="IncrementalHash"/> offers).</summary>
public static class BlockHash
{
    public static IReadOnlyList<(string Id, string Label, HashAlgorithmName Algorithm)> Algorithms { get; } =
    [
        ("md5", "MD5", HashAlgorithmName.MD5),
        ("sha1", "SHA-1", HashAlgorithmName.SHA1),
        ("sha256", "SHA-256", HashAlgorithmName.SHA256),
        ("sha384", "SHA-384", HashAlgorithmName.SHA384),
        ("sha512", "SHA-512", HashAlgorithmName.SHA512),
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
        using var hash = IncrementalHash.CreateHash(algorithm);
        var outcome = await ByteScan.RunAsync(start, end, chunkBytes, read, bytes => hash.AppendData(bytes), progress, cancellation).ConfigureAwait(false);
        return (outcome.Completed ? Convert.ToHexStringLower(hash.GetHashAndReset()) : null, outcome);
    }
}

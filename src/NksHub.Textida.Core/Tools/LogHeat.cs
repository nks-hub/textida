using System.Buffers;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tools;

/// <summary>How much of a log <see cref="LogHeat"/> reads: <paramref name="ChunkBytes"/> per read, the whole file up to <paramref name="FullScanBytes"/>, and beyond that only <paramref name="SampleBytesPerBucket"/> at the start of each bucket.</summary>
public readonly record struct LogHeatLimits(int ChunkBytes, long FullScanBytes, int SampleBytesPerBucket);

/// <summary>Counts of error and warning lines per equal part of the file. In a sampled map the counts are those of the lines read, so only their ratio to <see cref="Lines"/> means something.</summary>
public sealed class LogHeatmap(int[] errors, int[] warnings, int[] lines, bool sampled)
{
    public int Buckets => Lines.Count;
    public IReadOnlyList<int> Errors { get; } = errors;
    public IReadOnlyList<int> Warnings { get; } = warnings;
    public IReadOnlyList<int> Lines { get; } = lines;
    /// <summary>Only a part of each bucket was read.</summary>
    public bool Sampled { get; } = sampled;

    private double? _maxErrors, _maxWarnings;

    /// <summary>The share of error lines in the bucket relative to the densest bucket (0 to 1); 0 when the bucket has none.</summary>
    public double ErrorDensity(int bucket) => Relative(Errors, bucket, ref _maxErrors);

    /// <summary>The same for warning lines.</summary>
    public double WarningDensity(int bucket) => Relative(Warnings, bucket, ref _maxWarnings);

    private double Relative(IReadOnlyList<int> counts, int bucket, ref double? max)
    {
        max ??= Enumerable.Range(0, Buckets).Max(i => Ratio(counts, i));
        return max <= 0 ? 0 : Ratio(counts, bucket) / max.Value;
    }

    private double Ratio(IReadOnlyList<int> counts, int bucket) => Lines[bucket] == 0 ? 0 : counts[bucket] / (double)Lines[bucket];
}

/// <summary>
/// Builds the error heatmap of a log of any size. The file is cut into equal buckets by byte position and the lines that begin in each bucket are counted
/// by level. Up to <see cref="LogHeatLimits.FullScanBytes"/> every line is read, in chunks; a larger file is sampled, a window at the start of each bucket,
/// so the time does not grow with the size of the file. Runs on any thread; the snapshot keeps the bytes steady.
/// </summary>
public static class LogHeat
{
    public static LogHeatmap Compute(IByteSnapshot bytes, TextCodec codec, long firstLineStart, LogLevelRules rules, int buckets, LogHeatLimits limits, CancellationToken ct = default)
    {
        buckets = Math.Max(1, buckets);
        long length = bytes.ByteLength;
        var errors = new int[buckets];
        var warnings = new int[buckets];
        var lines = new int[buckets];
        long span = length - firstLineStart;
        bool sampled = span > limits.FullScanBytes;
        if (span <= 0) return new LogHeatmap(errors, warnings, lines, sampled);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(limits.ChunkBytes, 4096));
        try
        {
            long previousEnd = firstLineStart;
            for (int b = 0; b < buckets; b++)
            {
                ct.ThrowIfCancellationRequested();
                long from = firstLineStart + span * b / buckets, to = firstLineStart + span * (b + 1) / buckets;
                long lineFrom = b == 0 ? firstLineStart : Math.Max(previousEnd, Boundary(bytes, codec, from));
                long limit = sampled ? Math.Min(to, from + limits.SampleBytesPerBucket) : to;
                long lineTo = b == buckets - 1 && !sampled ? length : Math.Max(lineFrom, Boundary(bytes, codec, limit));
                if (!sampled) previousEnd = lineTo;
                CountLines(bytes, codec, rules, lineFrom, lineTo, buffer, limits.ChunkBytes, ref errors[b], ref warnings[b], ref lines[b], ct);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return new LogHeatmap(errors, warnings, lines, sampled);
    }

    /// <summary>The start of the first line that begins at or after <paramref name="offset"/>.</summary>
    private static long Boundary(IByteSnapshot bytes, TextCodec codec, long offset)
    {
        int w = codec.Alignment;
        long before = offset - w;
        return before < 0 ? 0 : LogScan.NextLineStart(bytes, codec, before - before % w);
    }

    private static void CountLines(IByteSnapshot bytes, TextCodec codec, LogLevelRules rules, long from, long to, byte[] buffer, int chunk, ref int errors, ref int warnings, ref int lines, CancellationToken ct)
    {
        int w = codec.Alignment;
        chunk -= chunk % w;
        for (long pos = from; pos < to;)
        {
            ct.ThrowIfCancellationRequested();
            int got = bytes.Read(pos, buffer.AsSpan(0, (int)Math.Min(chunk, to - pos)));
            got -= got % w;
            if (got <= 0) break;
            var block = buffer.AsSpan(0, got);
            int cut = pos + got >= to ? got : LogScan.WholeLines(block, codec);
            if (cut <= 0) cut = got;
            string text = codec.GetString(block[..cut]);
            for (int i = 0; i < text.Length;)
            {
                int end = text.AsSpan(i).IndexOfAny('\n', '\r');
                int stop = end < 0 ? text.Length : i + end;
                lines++;
                switch (rules.Classify(text.AsSpan(i, stop - i)))
                {
                    case LogLevel.Error: errors++; break;
                    case LogLevel.Warning: warnings++; break;
                }
                i = stop < text.Length && text[stop] == '\r' && stop + 1 < text.Length && text[stop + 1] == '\n' ? stop + 2 : stop + 1;
            }
            pos += cut;
        }
    }
}

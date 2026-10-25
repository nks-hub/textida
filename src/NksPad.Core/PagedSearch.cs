using System.Buffers;
using System.Text;
using System.Text.RegularExpressions;

namespace NksPad.Core;

/// <summary>One match: byte offset in the file and its length in bytes.</summary>
public readonly record struct SearchMatch(long ByteOffset, int ByteLength);

public sealed record SearchOptions
{
    /// <summary>Matches kept in the result; counting continues past the limit.</summary>
    public int MaxResults { get; init; } = 1000;
    /// <summary>Approximate bytes per parallel work item; blocks are cut at line starts.</summary>
    public int BlockSize { get; init; } = 16 << 20;
    public int Parallelism { get; init; } = Environment.ProcessorCount;
}

public sealed record SearchResult(long MatchCount, IReadOnlyList<SearchMatch> Matches);

/// <summary>
/// Parallel search over a mapped file. The file is cut into line-aligned blocks (LF), so a match never
/// spans blocks and non-overlapping counting matches ripgrep. Only the unmodified file on disk is searched.
/// </summary>
public static class PagedSearch
{
    /// <summary>Case-sensitive literal search. The pattern is bytes in the file encoding and must not contain CR or LF.</summary>
    public static Task<SearchResult> SearchLiteralAsync(MappedFile file, ReadOnlyMemory<byte> pattern, SearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new();
        if (pattern.IsEmpty || pattern.Span.IndexOfAny((byte)'\n', (byte)'\r') >= 0)
            throw new ArgumentException("Pattern must be non-empty and single-line.", nameof(pattern));
        return RunBlocks(file, options, ct, options.BlockSize, () => (object?)null, (start, end, _, state) =>
        {
            var span = file.GetSpan(start, checked((int)(end - start)));
            var p = pattern.Span;
            long count = 0;
            List<SearchMatch>? kept = null;
            int pos = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int i = span[pos..].IndexOf(p);
                if (i < 0) break;
                count++;
                if (count <= options.MaxResults) (kept ??= []).Add(new(start + pos + i, p.Length));
                pos += i + p.Length;
            }
            return (count, kept);
        });
    }

    /// <summary>Regex search over text decoded block by block. Use patterns that do not match across line breaks.</summary>
    public static Task<SearchResult> SearchRegexAsync(MappedFile file, Encoding encoding, Regex regex, SearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new();
        int block = Math.Min(options.BlockSize, 4 << 20);
        return RunBlocks(file, options, ct, block, () => new char[block + 1024], (start, end, _, buffer) =>
        {
            var span = file.GetSpan(start, checked((int)(end - start)));
            if (encoding.GetMaxCharCount(span.Length) > buffer.Length) buffer = new char[encoding.GetMaxCharCount(span.Length)];
            int n = encoding.GetChars(span, buffer);
            var chars = buffer.AsSpan(0, n);
            long count = 0;
            List<SearchMatch>? kept = null;
            int lastChar = 0;
            long lastByte = 0;
            foreach (var m in regex.EnumerateMatches(chars))
            {
                if ((++count & 255) == 0) ct.ThrowIfCancellationRequested();
                if (count > options.MaxResults) continue;
                lastByte += encoding.GetByteCount(chars[lastChar..m.Index]);
                lastChar = m.Index;
                (kept ??= []).Add(new(start + lastByte, encoding.GetByteCount(chars.Slice(m.Index, m.Length))));
            }
            return (count, kept);
        });
    }

    private static Task<SearchResult> RunBlocks<TState>(
        MappedFile file, SearchOptions options, CancellationToken ct, int blockSize,
        Func<TState> init, Func<long, long, int, TState, (long Count, List<SearchMatch>? Kept)> work)
    {
        return Task.Run(() =>
        {
            int blocks = (int)Math.Max(1, (file.Length + blockSize - 1) / blockSize);
            var counts = new long[blocks];
            var kept = new List<SearchMatch>?[blocks];
            Parallel.For(0, blocks, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = options.Parallelism }, init, (b, _, state) =>
            {
                long start = AlignedStart(file, (long)b * blockSize);
                long end = b + 1 == blocks ? file.Length : AlignedStart(file, (long)(b + 1) * blockSize);
                if (end > start)
                {
                    var (c, k) = work(start, end, b, state);
                    counts[b] = c;
                    kept[b] = k;
                }
                return state;
            }, _ => { });

            var all = new List<SearchMatch>();
            foreach (var k in kept)
            {
                if (k is null) continue;
                foreach (var m in k)
                    if (all.Count < options.MaxResults) all.Add(m);
            }
            return new SearchResult(counts.Sum(), all);
        }, ct);
    }

    /// <summary>First line start at or after <paramref name="raw"/> (the file length when none follows).</summary>
    private static long AlignedStart(MappedFile file, long raw)
    {
        if (raw <= 0) return 0;
        if (raw >= file.Length) return file.Length;
        long pos = raw - 1;
        while (pos < file.Length)
        {
            var window = file.GetSpanClamped(pos, 1 << 20);
            int i = window.IndexOf((byte)'\n');
            if (i >= 0) return pos + i + 1;
            pos += window.Length;
        }
        return file.Length;
    }
}

public sealed record TreeSearchResult(long FilesScanned, long FilesMatched, long Matches, long BytesRead, long FilesSkipped);

/// <summary>Parallel search over many small files; binary files (NUL in the first 8000 bytes) and files over 256 MiB are skipped.</summary>
public static class TreeSearch
{
    private const long MaxFileSize = 256L << 20;

    public static Task<TreeSearchResult> SearchLiteralAsync(string root, ReadOnlyMemory<byte> pattern, int parallelism = 0, IProgress<long>? filesDone = null, CancellationToken ct = default) =>
        Run(root, parallelism, filesDone, ct, (buf, len) =>
        {
            var span = buf.AsSpan(0, len);
            long n = 0;
            int pos = 0;
            while (true)
            {
                int i = span[pos..].IndexOf(pattern.Span);
                if (i < 0) return n;
                n++;
                pos += i + pattern.Length;
            }
        });

    public static Task<TreeSearchResult> SearchRegexAsync(string root, Regex regex, int parallelism = 0, IProgress<long>? filesDone = null, CancellationToken ct = default) =>
        Run(root, parallelism, filesDone, ct, (buf, len) =>
        {
            var chars = ArrayPool<char>.Shared.Rent(len + 1);
            try
            {
                int n = Encoding.UTF8.GetChars(buf.AsSpan(0, len), chars);
                long count = 0;
                foreach (var _ in regex.EnumerateMatches(chars.AsSpan(0, n))) count++;
                return count;
            }
            finally { ArrayPool<char>.Shared.Return(chars); }
        });

    private static async Task<TreeSearchResult> Run(string root, int parallelism, IProgress<long>? filesDone, CancellationToken ct, Func<byte[], int, long> count)
    {
        long scanned = 0, matched = 0, matches = 0, bytes = 0, skipped = 0;
        var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = parallelism > 0 ? parallelism : Environment.ProcessorCount };
        await Parallel.ForEachAsync(files, po, (path, token) =>
        {
            byte[]? buf = null;
            try
            {
                using var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
                long length = RandomAccess.GetLength(h);
                if (length > MaxFileSize) { Interlocked.Increment(ref skipped); return ValueTask.CompletedTask; }
                buf = ArrayPool<byte>.Shared.Rent((int)Math.Max(length, 1));
                int got = 0;
                while (got < length)
                {
                    int r = RandomAccess.Read(h, buf.AsSpan(got, (int)length - got), got);
                    if (r <= 0) break;
                    got += r;
                }
                Interlocked.Add(ref bytes, got);
                if (buf.AsSpan(0, Math.Min(got, 8000)).Contains((byte)0)) { Interlocked.Increment(ref skipped); return ValueTask.CompletedTask; }
                long n = count(buf, got);
                if (n > 0) { Interlocked.Increment(ref matched); Interlocked.Add(ref matches, n); }
                long done = Interlocked.Increment(ref scanned);
                if ((done & 1023) == 0) filesDone?.Report(done);
            }
            catch (IOException) { Interlocked.Increment(ref skipped); }
            catch (UnauthorizedAccessException) { Interlocked.Increment(ref skipped); }
            finally
            {
                if (buf is not null) ArrayPool<byte>.Shared.Return(buf);
            }
            return ValueTask.CompletedTask;
        });
        return new TreeSearchResult(scanned, matched, matches, bytes, skipped);
    }
}

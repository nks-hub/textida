using System.Collections.Concurrent;
using System.Globalization;

namespace NksHub.Textida.Tests;

/// <summary>
/// Text files of many GiB for the performance tests, written fast enough that the disk is the limit. Every line has 64 bytes: a 12 digit line number, a space, a level word
/// (<c>INFO </c>, <c>WARN </c> or <c>ERROR</c>), a space, 44 letters that depend on the seed of the text, and a line feed. Line <c>n</c> of a text starts at byte <c>64 n</c>, so
/// the expected offset and content of any line is a formula, not a pass over the file.
/// The tests that need a machine with 100 GiB of free disk run only where <c>TEXTIDA_HUGE=1</c> is set; <c>TEXTIDA_HUGE_SCALE</c> (for example <c>0.01</c>) shrinks every size for a dry run.
/// </summary>
internal static class HugeText
{
    public const int LineBytes = 64;
    public const int BlockBytes = 1 << 20;
    public const int LinesPerBlock = BlockBytes / LineBytes;

    public static readonly string[] Levels = ["INFO ", "WARN ", "ERROR"];

    /// <summary>True on a machine that was set up for the 50 GiB tests.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("TEXTIDA_HUGE") == "1";

    /// <summary>The factor of <c>TEXTIDA_HUGE_SCALE</c>, 1 when it is not set.</summary>
    public static double Scale => double.TryParse(Environment.GetEnvironmentVariable("TEXTIDA_HUGE_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s > 0 ? s : 1;

    /// <summary>A size scaled for a dry run, whole MiB, at least 2 MiB.</summary>
    public static long Size(long bytes) => Math.Max(2L * BlockBytes, (long)(bytes * Scale) / BlockBytes * BlockBytes);

    /// <summary>Fails with the numbers when the drive of <paramref name="path"/> has less than <paramref name="bytes"/> free; a performance run must never fill the disk.</summary>
    public static void RequireRoom(string path, long bytes)
    {
        long room = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;
        if (room < bytes) throw new IOException($"{room >> 30} GiB are free, the test needs {bytes >> 30} GiB");
    }

    private static readonly ConcurrentDictionary<long, byte[]> Letters = new();

    /// <summary>The 44 letters of the lines of the text number <paramref name="seed"/>.</summary>
    private static byte[] LettersOf(long seed) => Letters.GetOrAdd(seed, s =>
    {
        var letters = new byte[44];
        ulong h = (ulong)s * 0x9E3779B97F4A7C15UL + 0x94D049BB133111EBUL;
        for (int i = 0; i < letters.Length; i++)
        {
            h = h * 6364136223846793005UL + 1442695040888963407UL;
            letters[i] = (byte)('a' + (int)((h >> 33) % 26));
        }
        return letters;
    });

    private static readonly ConcurrentDictionary<(long, int), byte[]> Tails = new();

    /// <summary>The 52 bytes of a line after its number: the space, the level, the space, the letters and the line feed.</summary>
    private static byte[] TailOf(long seed, int level) => Tails.GetOrAdd((seed, level), key =>
    {
        var tail = new byte[LineBytes - 12];
        tail[0] = (byte)' ';
        for (int i = 0; i < 5; i++) tail[1 + i] = (byte)Levels[key.Item2][i];
        tail[6] = (byte)' ';
        LettersOf(key.Item1).CopyTo(tail, 7);
        tail[^1] = (byte)'\n';
        return tail;
    });

    private static void Digits(long number, Span<byte> digits)
    {
        for (int i = 11; i >= 0; i--, number /= 10) digits[i] = (byte)('0' + number % 10);
    }

    private static void Increment(Span<byte> digits)
    {
        for (int i = 11; i >= 0; i--)
        {
            if (digits[i] != (byte)'9') { digits[i]++; return; }
            digits[i] = (byte)'0';
        }
    }

    /// <summary>The bytes of text number <paramref name="seed"/> from <paramref name="offset"/>, as many as <paramref name="destination"/> holds; every line has the level <paramref name="level"/>.</summary>
    public static void Fill(long seed, long offset, Span<byte> destination, int level)
    {
        var tail = TailOf(seed, level);
        Span<byte> digits = stackalloc byte[12];
        Span<byte> line = stackalloc byte[LineBytes];
        Digits(offset / LineBytes, digits);
        int column = (int)(offset % LineBytes), at = 0;
        while (at < destination.Length)
        {
            if (column == 0 && destination.Length - at >= LineBytes)
            {
                digits.CopyTo(destination[at..]);
                tail.CopyTo(destination[(at + 12)..]);
                at += LineBytes;
            }
            else
            {
                digits.CopyTo(line);
                tail.CopyTo(line[12..]);
                int n = Math.Min(LineBytes - column, destination.Length - at);
                line.Slice(column, n).CopyTo(destination[at..]);
                at += n;
                column = 0;
            }
            Increment(digits);
        }
    }

    /// <summary>Same signature as the text of <c>LargeText</c>, so a model of a document can be generated either way.</summary>
    public static void Fill(long seed, long offset, Span<byte> destination) => Fill(seed, offset, destination, 0);

    /// <summary>The text of line <paramref name="line"/> (zero based) of text number <paramref name="seed"/> without the line feed.</summary>
    public static string LineText(long line, int level = 0, long seed = 0)
    {
        var bytes = new byte[LineBytes];
        Fill(seed, line * LineBytes, bytes, level);
        return System.Text.Encoding.ASCII.GetString(bytes, 0, LineBytes - 1);
    }

    /// <summary>
    /// Pushes what the system holds in its file cache out of it by writing and reading back a file of 1.3 times the memory of the machine in <paramref name="directory"/>; returns its size.
    /// What was written before and not touched since is the oldest in the cache, so the next read of it comes from the disk.
    /// </summary>
    public static long EvictFileCache(string directory)
    {
        long memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long bytes = Math.Max(2L * BlockBytes, memory * 13 / 10 / BlockBytes * BlockBytes);
        string path = Path.Combine(directory, "evict.bin");
        RequireRoom(path, bytes + (2L << 30));
        try
        {
            WriteFile(path, bytes);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, BlockBytes, FileOptions.SequentialScan);
            var buffer = new byte[BlockBytes];
            while (stream.Read(buffer, 0, buffer.Length) > 0) { }
        }
        finally { File.Delete(path); }
        return bytes;
    }

    /// <summary>Writes <paramref name="bytes"/> (whole MiB) of text number 0; <paramref name="levelOfBlock"/> gives the level of every line of a block of 1 MiB, INFO when it is null.</summary>
    public static void WriteFile(string path, long bytes, Func<long, int>? levelOfBlock = null)
    {
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, PreallocationSize = bytes,
        });
        var buffer = new byte[BlockBytes];
        for (long at = 0; at < bytes; at += BlockBytes)
        {
            int n = (int)Math.Min(BlockBytes, bytes - at);
            Fill(0, at, buffer.AsSpan(0, n), levelOfBlock?.Invoke(at / BlockBytes) ?? 0);
            stream.Write(buffer, 0, n);
        }
    }
}

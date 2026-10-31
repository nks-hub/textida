using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using Xunit.Abstractions;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.4.3 on a real file: a sparse log that doubles in size, followed through <see cref="FileLogSource"/>; 1 GiB as a large test, 10 GiB as a performance test.
/// Runs alone: the private memory size counts every thread of the process, so tests running beside it would show up as growth.</summary>
[Collection("Process memory")]
public sealed class LogFollowPerfTests(ITestOutputHelper output)
{
    private const long GiB = 1L << 30;
    private const int ScanBlock = 4 << 20;

    /// <summary>The private memory growth that PLAN chapter 3 allows the whole application for a large file; the follower is held to the same figure.</summary>
    private const long MemoryGrowth = 350L << 20;

    private static byte[] ErrorLine(long at) => Encoding.ASCII.GetBytes($"ERROR failure written at {at}\n");

    /// <summary>The offsets where a line starts that begins with <c>ERROR</c>, found by one pass over the file that shares nothing with the follower.</summary>
    private static List<long> NaiveErrorLines(string path)
    {
        var found = new List<long>();
        var buffer = new byte[ScanBlock];
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        byte[] word = "ERROR"u8.ToArray();
        long offset = 0;
        bool lineStart = true;
        int read;
        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            var block = buffer.AsSpan(0, read);
            if (!block.ContainsAnyExcept((byte)0))
            {
                lineStart = false;
                offset += read;
                continue;
            }
            for (int i = 0; i < read; i++)
            {
                if (lineStart && i + word.Length <= read && block.Slice(i, word.Length).SequenceEqual(word)) found.Add(offset + i);
                lineStart = block[i] == (byte)'\n';
            }
            offset += read;
        }
        return found;
    }

    /// <summary>The place moved back so that an error line does not straddle two blocks of the naive pass, which would miss it.</summary>
    private static long Inside(long position) => position % ScanBlock > ScanBlock - 128 ? position - 256 : position;

    private void Follow(long half)
    {
        using var temp = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", temp.Path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        // the file is NUL bytes, an error line behind a line break at each place, and a line break at the very end so that no line is left open
        void Grow(long length, params long[] lines)
        {
            using var stream = new FileStream(temp.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.SetLength(length);
            foreach (long position in lines)
            {
                if (position > 0)
                {
                    stream.Position = position - 1;
                    stream.WriteByte((byte)'\n');
                }
                stream.Position = position;
                stream.Write(ErrorLine(position));
            }
            stream.Position = length - 1;
            stream.WriteByte((byte)'\n');
        }

        using var self = Process.GetCurrentProcess();
        long Private()
        {
            self.Refresh();
            return self.PrivateMemorySize64;
        }

        long[] before = [0, Inside(half * 2 / 5), half - 64], added = [Inside(half + half / 5), Inside(2 * half - half / 10 + 12_345), 2 * half - 64];
        Grow(half, before);
        var follower = new LogFollower(new FileLogSource(temp.Path), TextCodec.Utf8, LogLevelRules.Default, new LogFilter([LogLevel.Error]));
        long baseline = Private(), peak = baseline;
        var clock = Stopwatch.StartNew();
        var first = follower.Poll(default, _ => peak = Math.Max(peak, Private()));
        var firstTime = clock.Elapsed;
        Assert.Equal(LogFollowChange.None, first.Change);
        Assert.Equal(before, follower.Matches());
        Assert.Equal(half, follower.Consumed);

        // the log doubles: the added half has error lines too, one of them in its last bytes
        Grow(2 * half, added);
        clock.Restart();
        var second = follower.Poll(default, _ => peak = Math.Max(peak, Private()));
        var secondTime = clock.Elapsed;

        Assert.Equal(LogFollowChange.None, second.Change);
        // only what was added is examined: the first half is not read again, apart from the head that tells the file is the same. A block that holds an error line ends at the
        // line break behind it, so the rest of that block, here NUL bytes, is read once more with the next one: one block more for each of them
        Assert.InRange(second.BytesRead, half - 1, half + LogFollower.HeadBytes + (added.Length + 1) * (long)LogScan.BlockBytes);
        Assert.Equal(2 * half, follower.Consumed);
        var expected = NaiveErrorLines(temp.Path);
        Assert.Equal([.. before, .. added], expected);
        Assert.Equal(expected, follower.Matches());

        long growth = peak - baseline;
        output.WriteLine($"Following a log from {half / (double)GiB:0.#} to {2 * half / (double)GiB:0.#} GiB: first poll {firstTime.TotalSeconds:0.0} s, added half {secondTime.TotalSeconds:0.0} s, {second.BytesRead >> 20} MiB examined the second time; " +
            $"private memory {baseline >> 20} MiB before, {peak >> 20} MiB at the peak, growth {growth >> 20} MiB");
        Assert.True(growth <= MemoryGrowth, $"private memory grew by {growth >> 20} MiB");
    }

    [Fact]
    [Trait("Category", "Large")]
    public void A_log_that_grows_from_half_to_one_gibibyte_adds_the_matches_of_the_added_half_alone_and_they_agree_with_a_pass_over_the_file() => Follow(GiB / 2);

    [Fact]
    [Trait("Category", "Perf")]
    public void A_log_that_grows_from_five_to_ten_gibibytes_adds_the_matches_of_the_added_half_alone_and_they_agree_with_a_pass_over_the_file() => Follow(5 * GiB);
}

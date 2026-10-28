using System.Diagnostics;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Plugins.TextTools;
using Xunit.Abstractions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.8.1 on a real file: the statistics of a sparse file run in the plugin process through the document API, and the memory of that process is measured.</summary>
public partial class TextToolsTests(ITestOutputHelper output)
{
    const long GiB = 1L << 30;
    const string Island = "alpha beta\r\n";

    /// <summary>The RPC limits the app gives its plugin host (<c>PluginLimitsPlaceholders.Host</c>): a chunk of 1 MiB is 1.4 MiB of base64 in a message.</summary>
    static readonly NksHub.Textida.Rpc.RpcLimits AppLimits = new(4 << 20, 8, 64, 100_000, 64L << 20, 64L << 20);

    /// <summary>A file of <paramref name="length"/> bytes that is NUL except for <paramref name="lines"/> lines of <see cref="Island"/> at its start, in the middle and at its end.</summary>
    static string SparseFileWithIslands(long length, int lines, out long gapBefore, out long gapAfter)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("textida-stats-").FullName, "big.txt");
        File.WriteAllBytes(path, []);
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        byte[] text = Utf8(string.Concat(Enumerable.Repeat(Island, lines)));
        long middle = length / 2, last = length - text.Length;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
            foreach (long at in new[] { 0, middle, last })
            {
                stream.Position = at;
                stream.Write(text);
            }
        }
        // the NUL line that ends in the first line of the next island is as long as the gap and the ten characters of that line
        gapBefore = middle - text.Length;
        gapAfter = last - (middle + text.Length);
        return path;
    }

    static (long Bytes, long Characters, long Words, long Lines, long Longest) ParseEnglishStatistics(string notice)
    {
        var m = Regex.Match(notice, @"^Bytes: ([\d,]+), characters: ([\d,]+), words: ([\d,]+), lines: ([\d,]+), longest line: ([\d,]+) characters\.");
        Assert.True(m.Success, notice);
        long N(int group) => long.Parse(m.Groups[group].Value.Replace(",", ""));
        return (N(1), N(2), N(3), N(4), N(5));
    }

    /// <summary>Samples the private memory of a process every 100 ms while a command runs.</summary>
    sealed class MemoryWatch : IDisposable
    {
        readonly Process _process;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        readonly CancellationTokenSource _stop = new();
        readonly Task _loop;
        readonly List<(double Seconds, long Bytes)> _samples = [];

        public long Baseline { get; }

        public MemoryWatch(int processId)
        {
            _process = Process.GetProcessById(processId);
            Baseline = Sample();
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    Sample();
                    try { await Task.Delay(100, _stop.Token); } catch (OperationCanceledException) { }
                }
            });
        }

        long Sample()
        {
            _process.Refresh();
            long bytes = _process.PrivateMemorySize64;
            lock (_samples) _samples.Add((_clock.Elapsed.TotalSeconds, bytes));
            return bytes;
        }

        /// <summary>The highest value seen between two fractions of the time the watch ran; the highest of all by default.</summary>
        public long Peak(double fromFraction = 0, double toFraction = 1)
        {
            lock (_samples)
            {
                double total = _samples[^1].Seconds;
                return _samples.Where(s => s.Seconds >= total * fromFraction && s.Seconds <= total * toFraction).Select(s => s.Bytes).DefaultIfEmpty(0).Max();
            }
        }

        /// <summary>The highest value of each second, in MiB, for the log of the run.</summary>
        public string PerSecond()
        {
            lock (_samples) return string.Join(' ', _samples.GroupBy(s => (int)s.Seconds).Select(g => (g.Max(s => s.Bytes) >> 20).ToString()));
        }

        bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            _loop.Wait();
            Sample();
            _process.Dispose();
            _stop.Dispose();
        }
    }

    async Task StatisticsOfARealFileAsync(long length)
    {
        const int lines = 1000;
        string path = SparseFileWithIslands(length, lines, out long gapBefore, out long gapAfter);
        try
        {
            using var doc = Document.Open(path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
            await using var rig = await Rig.CreateAsync(doc, chunk: 1 << 20, maxRead: 1 << 20, limits: AppLimits);
            int plugin = rig.Host.ProcessIds().Single();

            using var watch = new MemoryWatch(plugin);
            var clock = Stopwatch.StartNew();
            string status = await rig.RunAsync(TextToolsPlugin.Statistics, "en");
            clock.Stop();
            watch.Dispose();

            Assert.Equal("completed", status);
            var result = ParseEnglishStatistics(rig.LastNotice);
            // a NUL is a character like any other and no part of a word; the longest line is a NUL gap with the ten characters of the first line of the island behind it
            Assert.Equal((length, length, 3L * lines * 2, 3L * lines + 1, Math.Max(gapBefore, gapAfter) + 10), result);

            // The process starts small and has loaded the runtime and the code of the scan within the first second, so its private memory is compared between the first and the last
            // third of the run. A scan that keeps what it read grows with the file; one that does not stays where it was, and the 64 MiB are what it may go up by.
            long early = watch.Peak(0, 1.0 / 3), late = watch.Peak(2.0 / 3, 1);
            output.WriteLine($"Statistics of {length / GiB} GiB through the plugin process: {clock.Elapsed.TotalSeconds:0.0} s, {length / (double)(1 << 20) / clock.Elapsed.TotalSeconds:0} MiB/s; " +
                $"private memory of the plugin process {watch.Baseline >> 20} MiB before, {early >> 20} MiB at most in the first third, {late >> 20} MiB in the last, {watch.Peak() >> 20} MiB at the peak; MiB at each second: {watch.PerSecond()}");
            Assert.True(late - early <= WorkingMemory, $"the private memory of the plugin process went from {early >> 20} MiB in the first third of the run to {late >> 20} MiB in the last");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public Task Statistics_of_a_real_one_gibibyte_file_run_in_the_plugin_process_with_a_bounded_memory() => StatisticsOfARealFileAsync(GiB);

    [Fact]
    [Trait("Category", "Perf")]
    public Task Statistics_of_a_real_ten_gibibyte_file_run_in_the_plugin_process_with_a_bounded_memory() => StatisticsOfARealFileAsync(10 * GiB);

    async Task CancellingAsync(long length)
    {
        string path = SparseFileWithIslands(length, 1000, out _, out _);
        try
        {
            using var doc = Document.Open(path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
            await using var rig = await Rig.CreateAsync(doc, chunk: 1 << 20, maxRead: 1 << 20, limits: AppLimits);

            var run = rig.RunAsync(TextToolsPlugin.Statistics, "en");
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.False(run.IsCompleted, $"a statistic of {length / GiB} GiB was expected to run longer than this");
            var clock = Stopwatch.StartNew();
            await CancelTaskAsync(rig);

            Assert.Equal("incomplete", await run.WaitAsync(TimeSpan.FromSeconds(60)));
            clock.Stop();
            output.WriteLine($"Cancelling the statistics of {length / GiB} GiB took {clock.Elapsed.TotalMilliseconds:0} ms; {rig.LastNotice[..Math.Min(rig.LastNotice.Length, 120)]}");
            Assert.StartsWith("Incomplete statistics, ", rig.LastNotice);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
            Assert.Single(rig.Recorded.Ended);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public Task Cancelling_the_statistics_of_a_real_one_gibibyte_file_ends_the_run_in_seconds_and_says_how_far_it_got() => CancellingAsync(GiB);

    [Fact]
    [Trait("Category", "Perf")]
    public Task Cancelling_the_statistics_of_a_real_ten_gibibyte_file_ends_the_run_in_seconds_and_says_how_far_it_got() => CancellingAsync(10 * GiB);
}

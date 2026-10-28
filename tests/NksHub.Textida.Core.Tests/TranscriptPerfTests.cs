using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;
using NksHub.Textida.Core.Agents.Transcripts;
using NksHub.Textida.Tests;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8b.3.3 and P8b.3.5, the size of the target: a transcript of 1.2 GiB with one line of 54 MiB. The first turns must be on screen within 2 seconds of opening (the cold limit), the giant line must be
/// classified without being read, and the index must stay within its memory limit. The limits are the placeholders of the app. The scroll time of the panel (p95 of 33 ms) is not measured here.
/// Writes 1.2 GiB to the temporary folder, so it runs only with the performance tests.
/// </summary>
[Trait("Category", "Perf")]
[Collection("SecretPatterns")]
public class TranscriptPerfTests(ITestOutputHelper output)
{
    private static readonly TranscriptLimits Limits = new(1 << 20, 64 * 1024, 64L << 20, 200, 20_000, 8 << 20, 1000, 120, 64, 6, 1000);

    private static long Write(string path, out long giantLine) => TranscriptFile.Write(path, out giantLine);

    [Fact]
    public Task The_first_turns_appear_within_a_second_the_giant_line_is_not_read_and_the_index_keeps_to_its_limit() => Measure(cold: false);

    /// <summary>
    /// The same with the file out of the file cache of the system: after it is written, a file larger than the memory of the machine is written and read, which pushes the transcript out.
    /// It needs that much disk, so it runs only where <c>TEXTIDA_HUGE=1</c> is set. A cache of the host below a virtual machine stays, as nothing in the machine can empty it.
    /// </summary>
    [Fact]
    public Task The_first_turns_appear_within_two_seconds_with_the_file_out_of_the_cache() => HugeText.Enabled ? Measure(cold: true) : Task.CompletedTask;

    private async Task Measure(bool cold)
    {
        string dir = Directory.CreateTempSubdirectory("textida-perf-").FullName;
        try
        {
            string path = Path.Combine(dir, "big.jsonl");
            var writing = Stopwatch.StartNew();
            long lines = Write(path, out long giantLine);
            output.WriteLine($"{new FileInfo(path).Length >> 20} MiB, {lines} lines written in {writing.Elapsed.TotalSeconds:F1} s");
            if (cold)
            {
                writing.Restart();
                long pushed = HugeText.EvictFileCache(dir);
                output.WriteLine($"{pushed >> 30} GiB written and read to push the transcript out of the file cache, {writing.Elapsed.TotalSeconds:F0} s");
            }

            var clock = Stopwatch.StartNew();
            using var index = TranscriptIndex.Open(path, Limits, new TranscriptMasker(Limits.MinSecretLength));
            var building = index.BuildAsync(CancellationToken.None);
            while (index.TurnCount < 2 && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(1);
            var first = index.ReadTurn(0);
            var firstImage = clock.Elapsed;
            output.WriteLine($"{(cold ? "cold" : "warm")}: first image after {firstImage.TotalMilliseconds:F0} ms with {index.LineCount} lines indexed");
            Assert.NotEmpty(first.Rows);
            var limit = TimeSpan.FromSeconds(cold ? 2 : 1);
            Assert.True(firstImage < limit, $"the first image took {firstImage.TotalSeconds:F2} s");

            await building;
            output.WriteLine($"index complete after {clock.Elapsed.TotalSeconds:F1} s: {index.LineCount} lines, {index.TurnCount} turns, {index.IndexBytes >> 10} KiB");
            Assert.Null(index.Error);
            Assert.True(index.IsComplete);
            Assert.False(index.IsTruncated);
            Assert.Equal(lines, index.LineCount);
            Assert.True(index.IndexBytes <= Limits.IndexMaxBytes, $"the index uses {index.IndexBytes} bytes");

            // the giant line is classified from its start and read only up to the limit
            Assert.Equal(TranscriptKind.ToolResult, index.KindOf(giantLine));
            var shown = index.ReadLineBytes(giantLine, Limits.WholeLineBytes, out bool cut);
            Assert.True(cut);
            Assert.Equal(Limits.WholeLineBytes, shown.Length);
            var turn = index.ReadTurn(index.TurnOf(giantLine));
            Assert.Equal(1, turn.OversizedLines);
            Assert.True(GC.GetTotalMemory(forceFullCollection: true) < 512L << 20, "the managed memory after the index is large");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}

using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;
using NksHub.Textida.Core.Agents.Transcripts;

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

    private const int GiantBytes = 54 << 20;

    private static byte[] Block(out int lines)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"Question about the build\"}}\n");
        lines = 1;
        string text = new('a', 800);
        for (int i = 0; i < 60; i++)
        {
            sb.Append("{\"type\":\"assistant\",\"message\":{\"id\":\"m").Append(i).Append("\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"").Append(text).Append("\"}]}}\n");
            lines++;
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static long Write(string path, out long giantLine)
    {
        long target = (long)(1.2 * (1L << 30)), written = 0, line = 0;
        giantLine = -1;
        var block = Block(out int blockLines);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        while (written < target)
        {
            if (giantLine < 0 && written >= target / 2)
            {
                giantLine = line++;
                var head = Encoding.UTF8.GetBytes("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"big\",\"type\":\"tool_result\",\"content\":\"");
                var tail = Encoding.UTF8.GetBytes("\"}]}}\n");
                var chunk = new byte[1 << 20];
                Array.Fill(chunk, (byte)'x');
                stream.Write(head);
                for (int sent = 0; sent < GiantBytes; sent += chunk.Length) stream.Write(chunk, 0, Math.Min(chunk.Length, GiantBytes - sent));
                stream.Write(tail);
                written += head.Length + GiantBytes + tail.Length;
            }
            stream.Write(block);
            written += block.Length;
            line += blockLines;
        }
        return line;
    }

    [Fact]
    public async Task The_first_turns_appear_within_two_seconds_the_giant_line_is_not_read_and_the_index_keeps_to_its_limit()
    {
        string dir = Directory.CreateTempSubdirectory("textida-perf-").FullName;
        try
        {
            string path = Path.Combine(dir, "big.jsonl");
            var writing = Stopwatch.StartNew();
            long lines = Write(path, out long giantLine);
            output.WriteLine($"{new FileInfo(path).Length >> 20} MiB, {lines} lines written in {writing.Elapsed.TotalSeconds:F1} s");

            var clock = Stopwatch.StartNew();
            using var index = TranscriptIndex.Open(path, Limits, new TranscriptMasker(Limits.MinSecretLength));
            var building = index.BuildAsync(CancellationToken.None);
            while (index.TurnCount < 2 && clock.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(1);
            var first = index.ReadTurn(0);
            var firstImage = clock.Elapsed;
            output.WriteLine($"first image after {firstImage.TotalMilliseconds:F0} ms with {index.LineCount} lines indexed");
            Assert.NotEmpty(first.Rows);
            Assert.True(firstImage < TimeSpan.FromSeconds(2), $"the first image took {firstImage.TotalSeconds:F2} s");

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

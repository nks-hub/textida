using System.Diagnostics;
using System.Globalization;
using NksHub.Textida.App.Startup;

namespace NksHub.Textida.App.Tests;

public class CompositionFrameTraceTests
{
    [Fact]
    public async Task Batch_notifications_keep_their_clock_and_marker_without_claiming_presentation()
    {
        using var temp = new TempDir();
        var trace = new CompositionFrameTrace(Path.Combine(temp.Path, "measure.jsonl"), "test-run");
        var processed = new TaskCompletionSource();
        var rendered = new TaskCompletionSource();
        long draw = Stopwatch.GetTimestamp();
        trace.Record(0x1753A9, draw, processed.Task, rendered.Task);
        processed.SetResult();
        long between = Stopwatch.GetTimestamp();
        rendered.SetResult();
        await trace.WriteAsync(TimeSpan.FromSeconds(2));
        var rows = File.ReadAllLines(trace.Path);
        Assert.Equal(2, rows.Length);
        Assert.DoesNotContain("present", rows[0]);
        var row = rows[1].Split(',');
        Assert.Equal(new[] { "test-run", "1753A9", draw.ToString(CultureInfo.InvariantCulture) }, row[..3]);
        Assert.InRange(long.Parse(row[3], CultureInfo.InvariantCulture), draw, between);
        Assert.InRange(long.Parse(row[4], CultureInfo.InvariantCulture), between, Stopwatch.GetTimestamp());
        Assert.Equal(Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture), row[5]);
        Assert.Equal(new[] { "RanToCompletion", "RanToCompletion" }, row[6..]);
    }

    [Fact]
    public async Task Repeated_draws_are_deduplicated_and_missing_or_failed_notifications_stay_empty()
    {
        using var temp = new TempDir();
        var trace = new CompositionFrameTrace(Path.Combine(temp.Path, "measure.jsonl"), "test-run");
        var pending = new TaskCompletionSource();
        trace.Record(1, 100, Task.CompletedTask, pending.Task);
        trace.Record(1, 200, Task.CompletedTask, Task.CompletedTask);
        trace.Record(2, 300, Task.CompletedTask, Task.FromException(new IOException("Render notification failed")));
        await trace.WriteAsync(TimeSpan.Zero);
        var rows = File.ReadAllLines(trace.Path).Skip(1).Select(line => line.Split(',')).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("100", rows[0][2]);
        Assert.Equal("", rows[0][4]);
        Assert.Equal("pending", rows[0][7]);
        Assert.Equal("", rows[1][4]);
        Assert.Equal("Faulted", rows[1][7]);
        Assert.Throws<IOException>(() => new CompositionFrameTrace(Path.Combine(temp.Path, "measure.jsonl"), "another-run"));
    }
}

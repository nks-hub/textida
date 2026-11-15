using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NksHub.Textida.App.Startup;

// Batch notifications locate render-thread delays. They do not prove successful presentation.
internal sealed class CompositionFrameTrace
{
    private sealed class Frame(uint marker, long draw)
    {
        internal readonly uint Marker = marker;
        internal readonly long Draw = draw;
        internal long Processed, Rendered;
        internal string ProcessedStatus = "pending", RenderedStatus = "pending";
    }

    private readonly string runId;
    private readonly Dictionary<uint, Frame> frames = [];
    private readonly List<Task> notifications = [];
    private bool written;
    internal string Path { get; }

    internal CompositionFrameTrace(string logPath, string runId)
    {
        this.runId = runId;
        Path = System.IO.Path.ChangeExtension(logPath, ".composition.csv");
        using var file = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        file.WriteLine("run_id,marker,draw_qpc,processed_notification_qpc,rendered_notification_qpc,qpc_frequency,processed_status,rendered_status");
    }

    internal bool HasMarker(uint marker) => frames.ContainsKey(marker);

    internal void Record(uint marker, long draw, Task processed, Task rendered)
    {
        if (written) throw new InvalidOperationException("The composition trace has already been written");
        var frame = new Frame(marker, draw);
        if (!frames.TryAdd(marker, frame)) return;
        notifications.Add(processed.ContinueWith(task =>
        {
            Interlocked.Exchange(ref frame.Processed, Stopwatch.GetTimestamp());
            Volatile.Write(ref frame.ProcessedStatus, task.Status.ToString());
            _ = task.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
        notifications.Add(rendered.ContinueWith(task =>
        {
            Interlocked.Exchange(ref frame.Rendered, Stopwatch.GetTimestamp());
            Volatile.Write(ref frame.RenderedStatus, task.Status.ToString());
            _ = task.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
    }

    internal async Task WriteAsync(TimeSpan timeout)
    {
        if (written) throw new InvalidOperationException("The composition trace has already been written");
        written = true;
        try { await Task.WhenAll(notifications).WaitAsync(timeout); }
        catch (TimeoutException) { }
        var csv = new StringBuilder();
        foreach (var frame in frames.Values)
        {
            string processedStatus = Volatile.Read(ref frame.ProcessedStatus), renderedStatus = Volatile.Read(ref frame.RenderedStatus);
            string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
            csv.AppendLine(string.Join(',', runId, frame.Marker.ToString("X6", CultureInfo.InvariantCulture), Number(frame.Draw),
                processedStatus == "RanToCompletion" ? Number(Interlocked.Read(ref frame.Processed)) : "",
                renderedStatus == "RanToCompletion" ? Number(Interlocked.Read(ref frame.Rendered)) : "",
                Number(Stopwatch.Frequency), processedStatus, renderedStatus));
        }
        await File.AppendAllTextAsync(Path, csv.ToString());
    }
}

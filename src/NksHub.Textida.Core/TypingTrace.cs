using System.Diagnostics;
using System.Text;

namespace NksHub.Textida.Core;

/// <summary>
/// Stopwatch spans around the typing path. Off unless <c>TEXTIDA_TYPING_TRACE</c> is set to 1; the summary lists count, mean and p95 per span,
/// and the bytes the thread allocated inside the span (mean), so a span that allocates in proportion to the document shows up.
/// </summary>
public static class TypingTrace
{
    private static readonly Dictionary<string, List<(double Ms, long Bytes)>> Spans = [];
    [ThreadStatic] private static Dictionary<long, long>? _allocatedAtBegin;

    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("TEXTIDA_TYPING_TRACE") == "1";

    public static long Begin()
    {
        if (!Enabled) return 0;
        long start = Stopwatch.GetTimestamp();
        (_allocatedAtBegin ??= [])[start] = GC.GetAllocatedBytesForCurrentThread();
        return start;
    }

    public static void End(string span, long start)
    {
        if (start == 0) return;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = _allocatedAtBegin is not null && _allocatedAtBegin.Remove(start, out long at) ? GC.GetAllocatedBytesForCurrentThread() - at : -1;
        lock (Spans)
        {
            if (!Spans.TryGetValue(span, out var list)) Spans[span] = list = [];
            list.Add((ms, bytes));
        }
    }

    public static void Reset()
    {
        lock (Spans) Spans.Clear();
    }

    /// <summary>Appends the summary under <paramref name="title"/> to <c>logs\typing-trace.txt</c> beside the temp folder and clears the spans.</summary>
    public static void Dump(string title)
    {
        if (!Enabled) return;
        string path = Path.Combine(Path.GetTempPath(), "..", "logs", "typing-trace.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, "== " + title + "\n" + Summary() + "\n");
        Reset();
    }

    public static string Summary()
    {
        lock (Spans)
        {
            var sb = new StringBuilder();
            foreach (var (name, list) in Spans.OrderByDescending(p => p.Value.Sum(s => s.Ms)))
            {
                var sorted = list.Select(s => s.Ms).Order().ToArray();
                var measured = list.Where(s => s.Bytes >= 0).ToArray();
                double alloc = measured.Length > 0 ? measured.Average(s => s.Bytes) : 0;
                sb.Append($"{name,-28} n={sorted.Length,6} sum={sorted.Sum(),9:0.0} mean={sorted.Average():0.00} p95={sorted[(int)(sorted.Length * 0.95)]:0.00} max={sorted[^1]:0.0} alloc={alloc:0}\n");
            }
            return sb.ToString();
        }
    }
}

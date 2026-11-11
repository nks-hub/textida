using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NksHub.Textida.App.Startup;

internal readonly record struct MeasureSample(long StartedAt, double? UiMs = null, double? FrameMs = null,
    double? HandlerMs = null, double? DrawMs = null, double? FirstTickMs = null, double? LastDrawMs = null,
    string InputPath = "routed", bool? Sent = null, bool? Delivered = null, DesktopFrame? Desktop = null);

internal sealed class MeasureSamples(string logPath)
{
    internal string Path { get; } = System.IO.Path.ChangeExtension(logPath, ".samples.csv");
    internal string RunId { get; } = Guid.NewGuid().ToString("N");
    private const string Header = "run_id,scenario,backend,sample,start_qpc,qpc_frequency,input_path,sent,delivered,ui_ms,frame_ms,handler_ms,draw_ms,first_tick_ms,last_draw_ms,desktop_marker,desktop_qpc,desktop_ms,desktop_read_ms,desktop_poll_ms,desktop_reads,desktop_status";

    internal void Append(string scenario, string backend, IReadOnlyList<MeasureSample> samples)
    {
        if (samples.Count == 0) return;
        var csv = new StringBuilder();
        if (!File.Exists(Path) || new FileInfo(Path).Length == 0)
            csv.AppendLine(Header);
        else
        {
            using var reader = File.OpenText(Path);
            if (reader.ReadLine() != Header) throw new InvalidDataException("The samples CSV has a different schema; choose a new measurement log");
        }
        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            string[] fields = [RunId, scenario, backend, i.ToString(CultureInfo.InvariantCulture),
                s.StartedAt.ToString(CultureInfo.InvariantCulture), Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture),
                s.InputPath, Flag(s.Sent), Flag(s.Delivered), Number(s.UiMs), Number(s.FrameMs), Number(s.HandlerMs),
                Number(s.DrawMs), Number(s.FirstTickMs), Number(s.LastDrawMs), s.Desktop?.Marker.ToString("X6", CultureInfo.InvariantCulture) ?? "",
                s.Desktop is { ObservedAt: > 0 } desktop ? desktop.ObservedAt.ToString(CultureInfo.InvariantCulture) : "",
                Number(s.Desktop?.Since(s.StartedAt)), Number(s.Desktop?.ReadMs), Number(s.Desktop?.PollMs),
                s.Desktop?.Reads.ToString(CultureInfo.InvariantCulture) ?? "", s.Desktop?.Status ?? ""];
            csv.AppendLine(string.Join(',', fields.Select(Escape)));
        }
        // Flush between series, outside the timed input and rendering path.
        File.AppendAllText(Path, csv.ToString());
    }

    private static string Number(double? value) => value is >= 0 && double.IsFinite(value.Value)
        ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "";

    private static string Flag(bool? value) => value is { } flag ? flag ? "true" : "false" : "";

    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}

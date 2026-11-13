using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureNativeDiagnostic
{
    private long nativeWindow;

    private async Task PollAsync()
    {
        string? previousArm = null;
        try
        {
            Directory.CreateDirectory(directory);
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The process executable is unavailable.");
            using (var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var process = Process.GetCurrentProcess())
                identity = new(Services.BuildStamp.Commit, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), Environment.ProcessId, process.SessionId);
            if (identity.Session == 0) throw new InvalidOperationException("Desktop measurements require an interactive session.");
            VerifyPins();
            while (!stopped.IsCancellationRequested)
            {
                var publications = await Dispatcher.UIThread.InvokeAsync(Publications, DispatcherPriority.Background);
                foreach (var (name, record) in publications) await WriteAtomicAsync(name, record, stopped.Token).ConfigureAwait(false);
                string? request = ReadArm();
                if (request is not null && request != previousArm)
                {
                    previousArm = request;
                    Arm? arm = null;
                    try
                    {
                        arm = ParseArm(request);
                        var acknowledged = await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var sample = ArmSample(arm);
                            // Set before publishing armed.json: an external SendInput may arrive as soon as its rename completes.
                            sample.Acknowledged = true;
                            return SampleJson(sample, "armed");
                        }, DispatcherPriority.Background);
                        await WriteAtomicAsync("armed.json", acknowledged, stopped.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Record("error.jsonl", ErrorJson("arm", error, arm));
                    }
                }
                await Task.Delay(10, stopped.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Record("error.jsonl", ErrorJson("adapter", error)); }
    }

    private void VerifyPins()
    {
        string? head = Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE_HEAD");
        if (!string.IsNullOrEmpty(head) && !(head.Equals(identity!.Source, StringComparison.OrdinalIgnoreCase)
            || identity.Source.Length >= 7 && identity.Source.All(char.IsAsciiHexDigit) && head.StartsWith(identity.Source, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The requested source revision differs from the running build stamp.");
        string? sha = Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE_EXE_SHA");
        if (!string.IsNullOrEmpty(sha) && !sha.Equals(identity!.ExeSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The requested executable SHA-256 differs from the running executable.");
    }

    private List<(string Name, JsonObject Record)> Publications()
    {
        var records = new List<(string, JsonObject)>();
        if (stopped.IsCancellationRequested) return records;
        if (window.TryGetPlatformHandle()?.Handle is { } hwnd && hwnd != 0) nativeWindow = (long)OwnedWindow();
        long now = Stopwatch.GetTimestamp();
        if (lastStatus == 0 || Stopwatch.GetElapsedTime(lastStatus, now).TotalMilliseconds >= 100)
        {
            records.Add(("status.json", StatusJson()));
            lastStatus = now;
        }
        if (!readyWritten && identity is not null && EnsureMarker() && view.Working.IsCompleted && view.Parsing.IsCompleted && Items().Any())
        {
            var ready = StatusJson();
            ready["status"] = "ready";
            ready["points"] = PointsJson(Points());
            ready["viewport"] = ViewportJson();
            var targets = new JsonArray();
            foreach (var item in Items())
            {
                var node = (StructureNode)item.Tag!;
                targets.Add((JsonNode)new JsonObject { ["target"] = AutomationProperties.GetName(item), ["nodeId"] = node.Id,
                    ["expanded"] = item.IsExpanded, ["childCount"] = node.ChildCount });
            }
            ready["targets"] = targets;
            records.Add(("ready.json", ready));
            readyWritten = true;
        }
        if (pending is { } sample && Stopwatch.GetElapsedTime(sample.InputAt > 0 ? sample.InputAt : sample.ArmedAt, now).TotalSeconds > 10)
        {
            pending = null;
            Record("error.jsonl", ErrorJson("sample-timeout", new TimeoutException("The requested input and completed target frame were not observed."), sample.Arm));
        }
        return records;
    }

    private string? ReadArm()
    {
        string path = Path.Combine(directory, "arm.json");
        if (!File.Exists(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length is < 2 or > 16384) throw new InvalidDataException("The arm request must contain 2 to 16384 bytes.");
        using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static Arm ParseArm(string request)
    {
        var json = JsonNode.Parse(request)?.AsObject() ?? throw new InvalidDataException("The arm request is not an object.");
        string run = json["run"]?.GetValue<string>() ?? "";
        string action = json["action"]?.GetValue<string>() ?? "";
        string target = json["target"]?.GetValue<string>() ?? "";
        int sample = json["sample"]?.GetValue<int>() ?? -1;
        uint marker = json["marker"]?.GetValue<uint>() ?? 0;
        string? direction = json["direction"]?.GetValue<string>();
        if (run.Length is < 1 or > 128 || target.Length is < 1 or > 4096 || sample < 0 || marker is 0 or >= 0xFFFFFF
            || action is not ("select" or "expand" or "collapse" or "scroll") || action == "scroll" && direction is not ("up" or "down"))
            throw new InvalidDataException("The arm request has an unsupported action, target, direction or marker.");
        return new(run, sample, action, marker, target, direction);
    }

    private JsonObject IdentityJson() => new()
    {
        ["source"] = identity?.Source ?? Services.BuildStamp.Commit,
        ["exeSha"] = identity?.ExeSha,
        ["pid"] = identity?.Pid ?? Environment.ProcessId,
        ["session"] = identity?.Session,
        ["hwnd"] = nativeWindow,
        ["qpcFrequency"] = Stopwatch.Frequency,
        ["scope"] = "VM desktop upper bound; draw QPC records display-list commands, not physical display or RDP transport",
    };

    private JsonObject StatusJson()
    {
        var result = IdentityJson();
        var current = source?.Invoke();
        result["status"] = "status";
        result["observedQpc"] = Stopwatch.GetTimestamp();
        result["workingComplete"] = view.Working.IsCompleted;
        result["parsingComplete"] = view.Parsing.IsCompleted;
        result["message"] = view.Message;
        result["sourceDocumentId"] = current?.DocumentId.ToString();
        result["sourceRevision"] = current?.Revision;
        result["readOnly"] = current?.ReadOnly;
        result["cachedItems"] = view.CachedItems;
        return result;
    }

    private JsonObject SampleJson(Pending sample, string status)
    {
        var result = StatusJson();
        result["status"] = status;
        result["run"] = sample.Arm.Run;
        result["sample"] = sample.Arm.Sample;
        result["action"] = sample.Arm.Action;
        result["target"] = sample.Arm.Target;
        result["direction"] = sample.Arm.Direction;
        result["marker"] = sample.Arm.Marker;
        result["rgb1"] = sample.Arm.Marker;
        result["rgb2"] = sample.Arm.Marker ^ 0xFFFFFF;
        result["armedQpc"] = sample.ArmedAt;
        result["points"] = PointsJson(sample.Points);
        result["viewport"] = ViewportJson();
        result["initialState"] = StateJson(sample.Initial);
        return result;
    }

    private JsonObject ErrorJson(string stage, Exception error, Arm? arm = null)
    {
        var result = IdentityJson();
        result["status"] = "error";
        result["stage"] = stage;
        result["error"] = error.Message;
        result["observedQpc"] = Stopwatch.GetTimestamp();
        if (arm is not null)
        {
            result["run"] = arm.Run; result["sample"] = arm.Sample;
            result["action"] = arm.Action; result["marker"] = arm.Marker;
        }
        return result;
    }

    private static JsonObject StateJson(State state) => new()
    {
        ["selected"] = state.Selected, ["selectedId"] = state.SelectedId, ["expanded"] = state.Expanded,
        ["children"] = state.Children, ["workingComplete"] = state.WorkingComplete, ["parsingComplete"] = state.ParsingComplete,
        ["scrollX"] = state.ScrollX, ["scrollY"] = state.ScrollY, ["items"] = state.Items, ["targetExists"] = state.TargetExists,
    };

    private static JsonObject PointsJson(MarkerPoints points) => new()
    {
        ["x1"] = points.First.X, ["y1"] = points.First.Y, ["x2"] = points.Second.X, ["y2"] = points.Second.Y,
    };

    private JsonObject ViewportJson()
    {
        PixelPoint first = view.Tree.PointToScreen(default), last = view.Tree.PointToScreen(new Point(view.Tree.Bounds.Width, view.Tree.Bounds.Height));
        return new() { ["x"] = first.X, ["y"] = first.Y, ["width"] = last.X - first.X, ["height"] = last.Y - first.Y };
    }

    private void Record(string name, JsonObject row)
    {
        string text = row.ToJsonString() + "\n";
        _ = Task.Run(() =>
        {
            try { lock (outputGate) File.AppendAllText(Path.Combine(directory, name), text); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        });
    }

    private async Task WriteAtomicAsync(string name, JsonObject row, CancellationToken ct)
    {
        string final = Path.Combine(directory, name), temp = final + "." + Environment.ProcessId + ".tmp";
        await File.WriteAllTextAsync(temp, row.ToJsonString(), ct).ConfigureAwait(false);
        for (int retry = 0; ; retry++)
        {
            ct.ThrowIfCancellationRequested();
            try { File.Move(temp, final, overwrite: true); return; }
            catch (IOException) when (retry < 4) { await Task.Delay(5, ct).ConfigureAwait(false); }
        }
    }
}

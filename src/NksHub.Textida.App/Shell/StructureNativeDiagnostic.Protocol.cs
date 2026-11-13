using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureNativeDiagnostic
{
    private const string OperationKey = "Textida.StructureNativeDiagnostic.Operation";
    private const string FileIoKey = "Textida.StructureNativeDiagnostic.FileIo";
    private long nativeWindow;

    private async Task PollAsync()
    {
        string? previousArm = null;
        string phase = "CreateDirectory";
        try
        {
            Directory.CreateDirectory(directory);
            phase = "ResolveExecutable";
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The process executable is unavailable.");
            phase = "ReadExecutableIdentity";
            using (var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var process = Process.GetCurrentProcess())
                identity = new(Services.BuildStamp.Commit, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), Environment.ProcessId, process.SessionId);
            phase = "VerifySession";
            if (identity.Session == 0) throw new InvalidOperationException("Desktop measurements require an interactive session.");
            phase = "VerifyPins";
            VerifyPins();
            while (!stopped.IsCancellationRequested)
            {
                phase = "CapturePublications";
                var publications = await Dispatcher.UIThread.InvokeAsync(Publications, DispatcherPriority.Background);
                foreach (var (name, record) in publications)
                {
                    phase = "Publish:" + name;
                    await WriteAtomicAsync(name, record, stopped.Token).ConfigureAwait(false);
                }
                phase = "ReadArm";
                string? request = ReadArm();
                if (request is not null && request != previousArm)
                {
                    previousArm = request;
                    Arm? arm = null;
                    try
                    {
                        phase = "ParseArm";
                        arm = ParseArm(request);
                        phase = "ArmSample";
                        var acknowledged = await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var sample = ArmSample(arm);
                            // Set before publishing armed.json: an external SendInput may arrive as soon as its rename completes.
                            sample.Acknowledged = true;
                            return SampleJson(sample, "armed");
                        }, DispatcherPriority.Background);
                        phase = "Publish:armed.json";
                        await WriteAtomicAsync("armed.json", acknowledged, stopped.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        Record("error.jsonl", ErrorJson("arm", error, arm, phase));
                    }
                }
                phase = "PollDelay";
                await Task.Delay(10, stopped.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Record("error.jsonl", ErrorJson("adapter", error, phase: phase)); }
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
        string operation = "OpenArm";
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            operation = "ReadArmLength";
            if (file.Length is < 2 or > 16384) throw new InvalidDataException("The arm request must contain 2 to 16384 bytes.");
            operation = "ReadArmText";
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            return reader.ReadToEnd();
        }
        catch (Exception error)
        {
            error.Data[OperationKey] = operation;
            error.Data[FileIoKey] = new JsonObject { ["input"] = PathState(path) };
            throw;
        }
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
        result["focus"] = FocusJson();
        result["lastTreeKey"] = lastTreeKey is { } key ? new JsonObject
        {
            ["key"] = key.Key.ToString(), ["observedQpc"] = key.ObservedQpc, ["focusedNodeId"] = key.FocusedNodeId,
        } : null;
        return result;
    }

    private JsonObject FocusJson()
    {
        var focused = window.FocusManager?.GetFocusedElement();
        var control = focused as Control;
        var focusedItem = FocusedTreeItem(focused);
        var selectedItem = view.Tree.SelectedItem as TreeViewItem;
        return new()
        {
            ["windowActive"] = window.IsActive,
            ["treeIsFocused"] = view.Tree.IsFocused,
            ["treeKeyboardWithin"] = view.Tree.IsKeyboardFocusWithin,
            ["focusedType"] = focused?.GetType().FullName,
            ["focusedName"] = BoundedFocusName(control?.Name),
            ["focusedAutomationName"] = BoundedFocusName(control is null ? null : AutomationProperties.GetName(control)),
            ["focusedIsFocused"] = focused?.IsFocused,
            ["focusedKeyboardWithin"] = focused?.IsKeyboardFocusWithin,
            ["focusedInTree"] = focused is Visual visual && InMeasuredTree(visual),
            ["focusedItem"] = FocusItemJson(focusedItem),
            ["selectedItem"] = FocusItemJson(selectedItem),
            ["selectedIsFocusedItem"] = selectedItem is not null && ReferenceEquals(selectedItem, focusedItem),
            ["managedIdentityScope"] = "process-local CLR object identity; not a UIA runtime ID",
        };
    }

    private bool InMeasuredTree(Visual visual) => ReferenceEquals(visual, view.Tree) || view.Tree.IsVisualAncestorOf(visual);

    private TreeViewItem? FocusedTreeItem(IInputElement? focused) => focused is Visual visual && InMeasuredTree(visual)
        ? visual.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() : null;

    private static JsonObject? FocusItemJson(TreeViewItem? item) => item is null ? null : new()
    {
        ["nodeId"] = item.Tag is StructureNode node ? node.Id : null,
        ["automationName"] = BoundedFocusName(AutomationProperties.GetName(item)),
        ["managedIdentity"] = RuntimeHelpers.GetHashCode(item),
        ["isFocused"] = item.IsFocused,
        ["keyboardWithin"] = item.IsKeyboardFocusWithin,
        ["isSelected"] = item.IsSelected,
    };

    private static string? BoundedFocusName(string? name) => name is { Length: > 512 } ? name[..512] : name;

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

    private JsonObject ErrorJson(string stage, Exception error, Arm? arm = null, string? phase = null)
    {
        var result = IdentityJson();
        result["status"] = "error";
        result["stage"] = stage;
        result["error"] = BoundedDiagnostic(error.Message, 2048);
        result["exceptionType"] = error.GetType().FullName;
        result["hResultHex"] = HResultHex(error);
        result["stackTrace"] = BoundedDiagnostic(error.StackTrace, 8192);
        result["phase"] = phase;
        result["operation"] = error.Data[OperationKey] as string;
        if (error.Data[FileIoKey] is JsonObject fileIo) result["fileIo"] = fileIo.DeepClone();
        var innerChain = new JsonArray();
        var inner = error.InnerException;
        for (int depth = 0; inner is not null && depth < 4; depth++, inner = inner.InnerException)
            innerChain.Add((JsonNode)new JsonObject
            {
                ["exceptionType"] = inner.GetType().FullName, ["hResultHex"] = HResultHex(inner),
                ["error"] = BoundedDiagnostic(inner.Message, 1024), ["stackTrace"] = BoundedDiagnostic(inner.StackTrace, 2048),
            });
        result["innerChain"] = innerChain;
        result["innerChainTruncated"] = inner is not null;
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
        string operation = "SerializeRecord";
        int retry = 0;
        try
        {
            string json = row.ToJsonString();
            operation = "WriteTemp";
            await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
            operation = "MoveReplace";
            for (; ; retry++)
            {
                ct.ThrowIfCancellationRequested();
                try { File.Move(temp, final, overwrite: true); return; }
                catch (IOException) when (retry < 4) { await Task.Delay(5, ct).ConfigureAwait(false); }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            error.Data[OperationKey] = operation;
            error.Data[FileIoKey] = new JsonObject
            {
                ["name"] = name, ["retry"] = retry,
                ["temp"] = PathState(temp), ["final"] = PathState(final), ["directory"] = PathState(directory),
            };
            throw;
        }
    }

    private static JsonObject PathState(string path)
    {
        var result = new JsonObject { ["path"] = path, ["observedQpc"] = Stopwatch.GetTimestamp() };
        try
        {
            var attributes = File.GetAttributes(path);
            result["exists"] = true;
            result["attributes"] = attributes.ToString();
            result["attributesHex"] = "0x" + ((uint)attributes).ToString("X8");
        }
        catch (Exception error)
        {
            result["exists"] = error is FileNotFoundException or DirectoryNotFoundException ? false : null;
            result["probeExceptionType"] = error.GetType().FullName;
            result["probeHResultHex"] = HResultHex(error);
            result["probeError"] = BoundedDiagnostic(error.Message, 1024);
        }
        return result;
    }

    private static string HResultHex(Exception error) => "0x" + unchecked((uint)error.HResult).ToString("X8");
    private static string? BoundedDiagnostic(string? value, int limit) => value is { } text && text.Length > limit ? text[..limit] : value;
}

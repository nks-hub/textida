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
        string? previousInspection = null;
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
                phase = "ReadInspection";
                string? inspection = await ReadRequest("inspect.json", 32768, "Inspection").ConfigureAwait(false);
                if (inspection is not null && inspection != previousInspection)
                {
                    previousInspection = inspection;
                    Inspection? parsed = null;
                    try
                    {
                        phase = "ParseInspection";
                        parsed = ParseInspection(inspection);
                        phase = "CaptureInspection";
                        var response = await Dispatcher.UIThread.InvokeAsync(() => Inspect(parsed), DispatcherPriority.Background);
                        phase = "Publish:inspection.json";
                        await WriteAtomicAsync("inspection.json", response, stopped.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        var record = ErrorJson("inspection", error, phase: phase);
                        record["inspectionNonce"] = parsed?.Nonce;
                        record["requestedTarget"] = parsed?.Target;
                        record["result"] = "error";
                        Record("error.jsonl", record);
                        await WriteAtomicAsync("inspection.json", record, stopped.Token).ConfigureAwait(false);
                    }
                }
                phase = "ReadArm";
                string? request = await ReadArm().ConfigureAwait(false);
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
                targets.Add((JsonNode)new JsonObject { ["target"] = NodeName(item), ["nodeId"] = node.Id,
                    ["expanded"] = item.IsExpanded, ["childCount"] = node.ChildCount });
            }
            ready["targets"] = targets;
            AddTypingTraceSnapshot(ready, "typingTraceAtReady");
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

    private Task<string?> ReadArm() => ReadRequest("arm.json", 16384, "Arm");

    private async Task<string?> ReadRequest(string name, int maximumBytes, string kind)
    {
        string path = Path.Combine(directory, name);
        if (!File.Exists(path)) return null;
        string operation = "Open" + kind;
        try
        {
            FileStream file;
            for (int attempt = 1; ; attempt++)
            {
                stopped.Token.ThrowIfCancellationRequested();
                try
                {
                    file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    break;
                }
                catch (IOException error) when (attempt < 5 && unchecked((uint)error.HResult) is 0x80070002u or 0x80070020u)
                {
                    var record = ErrorJson("request-open-retry", error, phase: "Read" + kind);
                    record["status"] = "retry";
                    record["operation"] = operation;
                    record["attempt"] = attempt;
                    record["fileIo"] = new JsonObject { ["input"] = PathState(path) };
                    Record("request-open-retry.jsonl", record);
                    await Task.Delay(5, stopped.Token).ConfigureAwait(false);
                }
            }
            using (file)
            {
                operation = "Read" + kind + "Length";
                if (file.Length < 2 || file.Length > maximumBytes) throw new InvalidDataException($"The {kind} request must contain 2 to {maximumBytes} bytes.");
                operation = "Read" + kind + "Text";
                var bytes = new byte[(int)file.Length];
                file.ReadExactly(bytes);
                if (file.ReadByte() != -1) throw new InvalidDataException("The request grew while it was being read.");
                int preamble = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes.AsSpan(preamble));
            }
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
        result["sourceGeneration"] = view.SourceGeneration;
        result["cancellation"] = view.ObservedCancellation is { } cancellation ? new JsonObject
        {
            ["nonce"] = cancellation.Nonce.ToString(), ["sourceDocumentId"] = cancellation.DocumentId.ToString(),
            ["sourceRevision"] = cancellation.Revision, ["sourceGeneration"] = cancellation.Generation,
            ["requestedQpc"] = cancellation.RequestedQpc, ["workerStoppedQpc"] = cancellation.WorkerStoppedQpc,
            ["workerWasActive"] = cancellation.WorkerWasActive, ["workerCompleted"] = cancellation.WorkerCompleted,
            ["workerSucceeded"] = cancellation.WorkerSucceeded, ["workerOnDispatcher"] = cancellation.WorkerOnDispatcher,
            ["requestedThreadId"] = cancellation.RequestedThreadId, ["workerThreadId"] = cancellation.WorkerThreadId,
        } : null;
        result["readOnly"] = current?.ReadOnly;
        result["cachedItems"] = view.CachedItems;
        result["logicalCachedChildren"] = Items().Sum(item => item.Items.Count(child => child.Tag is StructureNode));
        result["realizedRows"] = view.Tree.GetRealizedRows().Count;
        result["boundRows"] = view.Tree.BoundRowCount;
        result["cumulativeCreatedRows"] = view.Tree.CreatedRowCount;
        result["cumulativeRecyclingRows"] = view.Tree.RecyclingRowCreationCount;
        result["automationAnchors"] = view.Tree.AutomationAnchorCount;
        result["automationRequested"] = view.Tree.AutomationRequested;
        result["visualControls"] = view.Tree.GetVisualDescendants().Count(visual => visual is Control);
        result["focus"] = FocusJson();
        result["lastTreeKey"] = lastTreeKey is { } key ? new JsonObject
        {
            ["key"] = key.Key.ToString(), ["observedQpc"] = key.ObservedQpc, ["focusedNodeId"] = key.FocusedNodeId,
        } : null;
        result["lastPointer"] = lastPointer?.DeepClone();
        result["lastWheel"] = lastWheel?.DeepClone();
        result["inspectionNonce"] = lastInspectionNonce;
        result["gc"] = GcJson();
        return result;
    }

    private static JsonObject GcJson()
    {
        long observed = Stopwatch.GetTimestamp();
        var last = GC.GetGCMemoryInfo();
        var result = new JsonObject
        {
            ["observedQpc"] = observed,
            ["estimatedManagedBytes"] = GC.GetTotalMemory(false),
            ["estimatedTotalAllocatedBytes"] = GC.GetTotalAllocatedBytes(false),
            ["collectionCount0"] = GC.CollectionCount(0),
            ["collectionCount1"] = GC.CollectionCount(1),
            ["collectionCount2"] = GC.CollectionCount(2),
            ["lastGCIndex"] = last.Index,
            ["lastGCHeapSizeBytes"] = last.Index > 0 ? last.HeapSizeBytes : null,
            ["lastGCTotalCommittedBytes"] = last.Index > 0 ? last.TotalCommittedBytes : null,
            ["lastGCFragmentedBytes"] = last.Index > 0 ? last.FragmentedBytes : null,
        };
        if (Core.TypingTrace.Enabled && last.Index > 0)
        {
            var generations = last.GenerationInfo;
            var sizes = new JsonArray();
            for (int index = 0; index < generations.Length; index++)
            {
                var generation = generations[index];
                sizes.Add((JsonNode)new JsonObject
                {
                    ["index"] = index,
                    ["sizeAfterBytes"] = generation.SizeAfterBytes,
                    ["fragmentationAfterBytes"] = generation.FragmentationAfterBytes,
                });
            }
            result["lastGCGenerationInfo"] = sizes;
        }
        return result;
    }

    private JsonObject FocusJson()
    {
        var focused = window.FocusManager?.GetFocusedElement();
        var control = focused as Control;
        var focusedItem = FocusedTreeItem(focused);
        var selectedItem = view.Tree.SelectedNode;
        return new()
        {
            ["windowActive"] = window.IsActive,
            ["treeIsFocused"] = view.Tree.IsFocused,
            ["treeListIsFocused"] = view.Tree.List.IsFocused,
            ["focusedIsTreeList"] = ReferenceEquals(focused, view.Tree.List),
            ["treeListType"] = view.Tree.List.GetType().FullName,
            ["treeAutomationId"] = view.Tree.Name,
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

    private StructureTreeNode? FocusedTreeItem(IInputElement? focused) => focused is Visual visual && InMeasuredTree(visual)
        ? SourceItem(view.Tree, visual)?.BoundNode : null;

    private JsonObject? FocusItemJson(StructureTreeNode? item) => item is null ? null : new()
    {
        ["nodeId"] = item.Tag is StructureNode node ? node.Id : null,
        ["automationName"] = BoundedFocusName(NodeName(item)),
        ["managedIdentity"] = RuntimeHelpers.GetHashCode(item),
        ["isFocused"] = view.Tree.TryGetRealizedRow(item)?.IsFocused == true,
        ["keyboardWithin"] = view.Tree.TryGetRealizedRow(item)?.IsKeyboardFocusWithin == true,
        ["isSelected"] = ReferenceEquals(view.Tree.SelectedNode, item),
        ["bindingVersion"] = item.BindingVersion,
    };

    private static string? BoundedFocusName(string? name) => name is { Length: > 512 } ? name[..512] : name;

    private static string NodeName(StructureTreeNode item) => item.DisplayName;

    private JsonObject SampleJson(Pending sample, string status)
    {
        var result = StatusJson();
        result["status"] = status;
        result["targetBindingVersion"] = sample.TargetVersion;
        result["targetCurrentBindingVersion"] = sample.Target.BindingVersion;
        result["targetIsCurrent"] = view.Tree.IsCurrent(sample.Target, sample.TargetVersion);
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
        result["targetNodeId"] = sample.Target.Tag is StructureNode node ? node.Id : null;
        result["targetParentNodeId"] = sample.Target.Tag is StructureNode data ? data.ParentId : null;
        result["inspectionNonce"] = sample.InspectionNonce;
        result["inputEvent"] = sample.InputEvent?.DeepClone();
        result["firstSelectionCompletedQpc"] = sample.FirstSelectionCompletedQpc > 0 ? sample.FirstSelectionCompletedQpc : null;
        result["lastSelectionCompletedQpc"] = sample.LastSelectionCompletedQpc > 0 ? sample.LastSelectionCompletedQpc : null;
        result["selectionCompletedCount"] = sample.SelectionCompletedCount;
        var working = sample.WorkingObservation;
        long workingObserved = working is null ? 0 : Interlocked.Read(ref working.ObservedCompleteQpc);
        result["workingObservedCompleteQpc"] = workingObserved > 0 ? workingObserved : null;
        result["workingObservedCompleteKind"] = workingObserved > 0 ? working!.Kind : null;
        result["workingObservedForSelectionCount"] = working?.SelectionCount;
        result["firstAnimationFrameCallbackQpc"] = sample.FirstAnimationFrameCallbackQpc > 0 ? sample.FirstAnimationFrameCallbackQpc : null;
        result["animationFrameCallbackCount"] = sample.AnimationFrameCallbackCount;
        result["firstPaintQpc"] = sample.FirstPaintQpc > 0 ? sample.FirstPaintQpc : null;
        result["paintAttempts"] = sample.PaintAttempts;
        result["firstPaintState"] = sample.FirstPaintState is { } first ? new JsonObject
        {
            ["workingComplete"] = first.WorkingComplete, ["parsingComplete"] = first.ParsingComplete,
            ["viewArrangeValid"] = first.ViewArrangeValid, ["viewMeasureValid"] = first.ViewMeasureValid,
            ["treeArrangeValid"] = first.TreeArrangeValid, ["treeMeasureValid"] = first.TreeMeasureValid,
            ["targetArrangeValid"] = first.TargetArrangeValid, ["targetMeasureValid"] = first.TargetMeasureValid,
        } : null;
        if (sample.TypingTraceWindowBeginQpc > 0)
        {
            result["typingTraceBeforeArm"] = sample.TypingTraceBeforeArm;
            result["typingTraceSinceArm"] = Core.TypingTrace.Summary();
            result["typingTraceWindowBeginQpc"] = sample.TypingTraceWindowBeginQpc;
            result["typingTraceSnapshotQpc"] = Stopwatch.GetTimestamp();
            result["typingTraceScope"] = "Non-atomic process-wide completed-span snapshot since reset; spans may start before reset, and nested spans overlap. QPC values bracket the observation, not each span.";
        }
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
        if (stage == "adapter") AddTypingTraceSnapshot(result, "typingTraceAtFailure");
        if (arm is not null)
        {
            result["run"] = arm.Run; result["sample"] = arm.Sample;
            result["action"] = arm.Action; result["marker"] = arm.Marker;
        }
        return result;
    }

    private static void AddTypingTraceSnapshot(JsonObject record, string name)
    {
        if (!Core.TypingTrace.Enabled) return;
        record[name] = Core.TypingTrace.Summary();
        record["typingTraceScope"] = "Non-atomic process-wide completed-span snapshot since the latest trace reset; spans may start before reset and nested spans overlap. Elapsed spans are not CPU time or input latency.";
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
            operation = "PublishAtomic";
            Exception? retryReason = null;
            for (; ; retry++)
            {
                ct.ThrowIfCancellationRequested();
                if (retryReason is not null)
                {
                    var record = ErrorJson("publication-retry", retryReason, phase: "Publish:" + name);
                    record["status"] = "retry";
                    record["operation"] = operation;
                    record["retry"] = retry;
                    record["fileIo"] = new JsonObject
                    {
                        ["name"] = name, ["retry"] = retry,
                        ["temp"] = PathState(temp), ["final"] = PathState(final), ["directory"] = PathState(directory),
                    };
                    Record("publication-retry.jsonl", record);
                    retryReason = null;
                }
                try { PublishFile(temp, final); return; }
                catch (Exception error) when (retry < 4 && (error is IOException
                    || OperatingSystem.IsWindows() && error is UnauthorizedAccessException && unchecked((uint)error.HResult) == 0x80070005u))
                {
                    retryReason = error;
                    await Task.Delay(5, ct).ConfigureAwait(false);
                }
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

    internal static void PublishFile(string temp, string final)
    {
        // MoveFileEx cannot replace an open reader even when it shares deletion.
        if (File.Exists(final)) File.Replace(temp, final, null);
        else File.Move(temp, final);
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

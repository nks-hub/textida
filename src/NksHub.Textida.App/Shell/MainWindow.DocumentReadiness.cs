using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private DocumentReadiness? documentReadiness;

    internal readonly record struct DocumentReadinessRequest(string Nonce, long RequestedQpc, int Pid, int Session,
        long? Hwnd = null, Guid? DocumentId = null, long? Revision = null);

    internal Task DocumentReadinessCompletion => documentReadiness?.Completion ?? Task.CompletedTask;

    private void AttachDocumentReadiness()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("TEXTIDA_STRUCTURE_NATIVE") is not { Length: > 0 } root) return;
        documentReadiness = new(this, Path.GetFullPath(root));
        Closed += (_, _) => _ = documentReadiness.StopAsync();
    }

    internal JsonObject CaptureDocumentReadiness(DocumentReadinessRequest request)
    {
        Dispatcher.UIThread.VerifyAccess();
        long begin = Stopwatch.GetTimestamp();
        using var process = Process.GetCurrentProcess();
        long hwnd = (long)(TryGetPlatformHandle()?.Handle ?? 0);
        var result = new JsonObject
        {
            ["schema"] = 1, ["kind"] = "shared-document-readiness", ["nonce"] = request.Nonce,
            ["source"] = Services.BuildStamp.Commit, ["pid"] = Environment.ProcessId, ["session"] = process.SessionId,
            ["hwnd"] = hwnd, ["qpcFrequency"] = Stopwatch.Frequency, ["requestedQpc"] = request.RequestedQpc,
            ["captureBeginQpc"] = begin, ["ready"] = false,
        };
        string state = "not-ready";
        if (documentReadiness is null) state = "disabled";
        else if (request.Pid != Environment.ProcessId || request.Session != process.SessionId
            || request.Hwnd is { } expectedHwnd && expectedHwnd != hwnd
            || request.RequestedQpc < documentReadiness.StartedQpc || request.RequestedQpc > begin)
            state = "stale-request";
        else if (Active is { Paged: { } view } tab && tab.Doc.Doc.Backend is PagedBackend backend)
        {
            var document = tab.Doc.Doc;
            long revision = document.Revision;
            var paged = backend.Paged;
            var source = paged.Source;
            var indexTask = source.IndexTask;
            var viewport = view.CaptureReadinessViewport();
            var syntaxState = view.Syntax.CaptureReadiness(document);
            var viewState = view.CaptureReadiness(document, backend);
            var indexState = document.LineIndexState;
            long byteLength = document.ByteLength, indexedBytes = paged.IndexedBytes;
            bool indexComplete = indexTask.IsCompletedSuccessfully && indexState.Complete && indexedBytes == byteLength;
            bool stable = ReferenceEquals(Active, tab) && ReferenceEquals(tab.Doc.Doc, document)
                && document.Revision == revision && document.ByteLength == byteLength
                && ReferenceEquals(document.Backend, backend) && ReferenceEquals(backend.Paged, paged)
                && ReferenceEquals(paged.Source, source) && ReferenceEquals(source.IndexTask, indexTask)
                && viewport == view.CaptureReadinessViewport();
            bool expected = (request.DocumentId is null || request.DocumentId == document.Id)
                && (request.Revision is null || request.Revision == revision);
            result["documentId"] = document.Id.ToString(); result["revision"] = revision;
            result["backendType"] = backend.GetType().FullName; result["backendIdentity"] = RuntimeHelpers.GetHashCode(backend);
            result["sourceType"] = source.GetType().FullName; result["sourceIdentity"] = RuntimeHelpers.GetHashCode(source);
            result["identityScope"] = "Process-local object hashes; reference equality is checked within this capture.";
            result["byteLength"] = byteLength; result["indexedBytes"] = indexedBytes;
            result["indexTaskComplete"] = indexTask.IsCompletedSuccessfully;
            result["lineIndexComplete"] = indexState.Complete; result["lineCount"] = indexState.Lines;
            result["stable"] = stable;
            result["viewport"] = new JsonObject { ["firstLine"] = viewport.FirstLine, ["visibleLines"] = viewport.VisibleLines,
                ["horizontal"] = viewport.Horizontal, ["textWidth"] = viewport.TextWidth, ["height"] = viewport.Height };
            result["syntax"] = SyntaxReadinessJson(syntaxState, revision);
            result["view"] = PagedReadinessJson(viewState);
            bool ready = expected && stable && IsVisible && view.IsEffectivelyVisible && indexComplete
                && syntaxState.Ready(revision) && viewState.Ready;
            result["ready"] = ready;
            state = !expected ? "stale-document" : !stable ? "changed-during-capture" : ready ? "ready" : "not-ready";
        }
        else state = "no-active-paged-document";
        result["status"] = state;
        result["captureEndQpc"] = Stopwatch.GetTimestamp();
        return result;
    }

    private static JsonObject SyntaxReadinessJson(SyntaxFeeder.Readiness state, long revision) => new()
    {
        ["ready"] = state.Ready(revision), ["disposed"] = state.Disposed, ["boundToDocument"] = state.BoundToDocument,
        ["first"] = state.First, ["count"] = state.Count, ["column"] = state.Column, ["columns"] = state.Columns,
        ["requestedMatches"] = state.RequestedMatches, ["sessionRevision"] = state.SessionRevision,
        ["doneRevision"] = state.DoneRevision, ["pending"] = state.Pending, ["lineTooLong"] = state.LineTooLong,
        ["scheduled"] = state.Scheduled,
    };

    private static JsonObject PagedReadinessJson(PagedTextView.Readiness state) => new()
    {
        ["ready"] = state.Ready, ["sameDocument"] = state.SameDocument, ["currentCache"] = state.CurrentCache,
        ["modified"] = state.Modified, ["detour"] = state.Detour, ["visibleRows"] = state.VisibleRows,
        ["cachedRows"] = state.CachedRows, ["longRows"] = state.LongRows,
        ["requiredColumnIndexes"] = state.RequiredColumnIndexes, ["completeColumnIndexes"] = state.CompleteColumnIndexes,
        ["windowRows"] = state.WindowRows, ["cachedWindows"] = state.CachedWindows,
        ["columnTracking"] = state.ColumnTracking, ["columnWork"] = state.ColumnWork, ["columnFailed"] = state.ColumnFailed,
        ["windowsPending"] = state.WindowsPending,
        ["caretMovePending"] = state.CaretMovePending, ["indexTimerPending"] = state.IndexTimerPending,
    };

    private sealed class DocumentReadiness
    {
        private const string RequestName = "shared-document-request.json";
        private const string ResponseName = "shared-document.json";
        private readonly MainWindow owner;
        private readonly string directory;
        private readonly FileSystemWatcher watcher;
        private readonly SemaphoreSlim signal = new(0, 1);
        private readonly CancellationTokenSource stopped = new();
        private int queued, stopping;
        private bool watcherFailed;
        private Task? stopTask;

        public long StartedQpc { get; } = Stopwatch.GetTimestamp();
        public Task Completion { get; }

        public DocumentReadiness(MainWindow owner, string directory)
        {
            this.owner = owner; this.directory = directory;
            Directory.CreateDirectory(directory);
            watcher = new(directory, RequestName) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Created += (_, _) => Signal();
            watcher.Changed += (_, _) => Signal();
            watcher.Renamed += (_, _) => Signal();
            watcher.Error += (_, _) => { Volatile.Write(ref watcherFailed, true); Signal(); };
            watcher.EnableRaisingEvents = true;
            Completion = Task.Run(ReadRequests);
            Signal();
        }

        private void Signal()
        {
            if (Volatile.Read(ref stopping) != 0 || Interlocked.Exchange(ref queued, 1) != 0) return;
            try { signal.Release(); }
            catch (ObjectDisposedException) when (Volatile.Read(ref stopping) != 0) { }
        }

        private async Task ReadRequests()
        {
            string? previousRequest = null, previousNonce = null;
            try
            {
                while (true)
                {
                    await signal.WaitAsync(stopped.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref queued, 0);
                    if (Volatile.Read(ref watcherFailed)) throw new IOException("The readiness request watcher failed.");
                    string path = Path.Combine(directory, RequestName);
                    if (!File.Exists(path)) continue;
                    string? nonce = null;
                    JsonObject response;
                    try
                    {
                        string text = ReadRequest(path);
                        if (text == previousRequest) continue;
                        previousRequest = text;
                        var request = ParseRequest(text);
                        nonce = request.Nonce;
                        if (nonce == previousNonce) continue;
                        previousNonce = nonce;
                        long received = Stopwatch.GetTimestamp();
                        if (request.RequestedQpc > received) throw new InvalidDataException("The readiness request timestamp is in the future.");
                        response = await Dispatcher.UIThread.InvokeAsync(() => owner.CaptureDocumentReadiness(request),
                            DispatcherPriority.Background, stopped.Token);
                        response["receivedQpc"] = received;
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        // This file is an external transport boundary; errors must never produce a ready response.
                        response = Error(error, nonce);
                    }
                    await WriteResponse(response).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!stopped.IsCancellationRequested)
                {
                    try { await WriteResponse(Error(error, previousNonce)).ConfigureAwait(false); }
                    catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException or OperationCanceledException) { }
                }
            }
        }

        private static JsonObject Error(Exception error, string? nonce) => new()
        {
            ["schema"] = 1, ["kind"] = "shared-document-readiness", ["nonce"] = nonce,
            ["pid"] = Environment.ProcessId, ["status"] = "error", ["ready"] = false,
            ["errorType"] = error.GetType().FullName, ["hResult"] = error.HResult,
            ["captureEndQpc"] = Stopwatch.GetTimestamp(),
        };

        private static string ReadRequest(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length is < 2 or > 4096) throw new InvalidDataException("A readiness request must contain 2 to 4096 bytes.");
            var bytes = new byte[(int)file.Length];
            file.ReadExactly(bytes);
            if (file.ReadByte() != -1) throw new InvalidDataException("The readiness request grew while being read.");
            int preamble = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes.AsSpan(preamble));
        }

        private static DocumentReadinessRequest ParseRequest(string text)
        {
            var json = JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("A readiness request must be an object.");
            string nonce = json["nonce"]?.GetValue<string>() ?? "";
            long qpc = json["requestedQpc"]?.GetValue<long>() ?? 0;
            int pid = json["pid"]?.GetValue<int>() ?? 0, session = json["session"]?.GetValue<int>() ?? -1;
            long? hwnd = json["hwnd"]?.GetValue<long>();
            Guid? documentId = json["documentId"] is { } id ? Guid.Parse(id.GetValue<string>()) : null;
            long? revision = json["revision"]?.GetValue<long>();
            if (nonce.Length is < 1 or > 128 || nonce.Any(char.IsControl) || qpc <= 0 || pid <= 0 || session < 0
                || hwnd is <= 0 || revision is < 0 || documentId.HasValue != revision.HasValue)
                throw new InvalidDataException("The readiness request has an invalid identity, nonce or timestamp.");
            return new(nonce, qpc, pid, session, hwnd, documentId, revision);
        }

        private async Task WriteResponse(JsonObject response)
        {
            string path = Path.Combine(directory, ResponseName), temp = path + "." + Environment.ProcessId + ".tmp";
            await File.WriteAllTextAsync(temp, response.ToJsonString(), stopped.Token).ConfigureAwait(false);
            for (int attempt = 0; ; attempt++)
            {
                stopped.Token.ThrowIfCancellationRequested();
                try { StructureNativeDiagnostic.PublishFile(temp, path); return; }
                catch (IOException) when (attempt < 4) { await Task.Delay(5, stopped.Token).ConfigureAwait(false); }
            }
        }

        public Task StopAsync() => stopTask ??= StopCoreAsync();

        private async Task StopCoreAsync()
        {
            Interlocked.Exchange(ref stopping, 1);
            watcher.Dispose();
            stopped.Cancel();
            await Completion.ConfigureAwait(false);
            signal.Dispose(); stopped.Dispose();
        }
    }
}

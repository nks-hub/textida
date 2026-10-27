using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

// Test double for a real plugin: a separate process speaking the plugin protocol on stdin/stdout.
// Options: --api N, --crash-on-start CODE, --stderr-flood LINES, --sleep-before-read MS, --exit-after-handshake,
// --on-command JSON ({"method","params"} the plugin sends to the editor on command/execute; $document and $revision are filled in from the command).
var api = 1;
var crashOnStart = -1;
var stderrFlood = 0;
var sleepBeforeRead = 0;
string? onCommand = null;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--api": api = int.Parse(args[i + 1]); break;
        case "--crash-on-start": crashOnStart = int.Parse(args[i + 1]); break;
        case "--stderr-flood": stderrFlood = int.Parse(args[i + 1]); break;
        case "--sleep-before-read": sleepBeforeRead = int.Parse(args[i + 1]); break;
        case "--on-command": onCommand = args[i + 1]; break;
    }
}

if (stderrFlood > 0)
{
    var line = new string('e', 200);
    for (var i = 0; i < stderrFlood; i++) Console.Error.WriteLine($"flood {i} {line}");
}
if (crashOnStart >= 0)
{
    Console.Error.WriteLine("test plugin: crashing on start");
    Environment.Exit(crashOnStart);
}
if (sleepBeforeRead > 0) Thread.Sleep(sleepBeforeRead);

var handler = new Handler(api, onCommand);
var limits = new RpcLimits(4 * 1024 * 1024, 64, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024);
await using var rpc = new JsonRpcDispatcher(Console.OpenStandardInput(), Console.OpenStandardOutput(), RpcProtocols.Plugin, limits, handler);
handler.Rpc = rpc;
await rpc.Completion;
return 0;

sealed class Handler(int api, string? onCommand) : IRpcHandler
{
    public JsonRpcDispatcher Rpc = null!;
    volatile bool _frozen;
    long _sinkCount, _sinkBytes;
    string _resync = "null";
    readonly List<string> _events = [];
    readonly List<string> _commands = [];
    readonly List<string> _tabs = [];
    volatile bool _tabHandled, _tabError;
    volatile int _tabDelayMs;
    readonly List<string> _completions = [];
    volatile string _completionAnswer = "{\"revision\":0,\"items\":[]}";
    volatile int _completionDelayMs;
    readonly Dictionary<string, (byte[] Content, int Revision)> _files = [];
    volatile bool _sourceOffline;
    volatile bool _sourceAtomic = true;

    void SourceReachable()
    {
        if (_sourceOffline) throw new RpcException(DocumentErrorCodes.SourceUnavailable, "test plugin: offline");
    }

    static string Q(string s) => "\"" + System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(s) + "\"";

    static byte[] Raw(string json) => Encoding.UTF8.GetBytes(json);
    static string Str(JsonElement p, string name) => p.GetProperty(name).GetString()!;
    static int Int(JsonElement p, string name) => p.GetProperty(name).GetInt32();

    public async ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case PluginMethods.Initialize:
                return JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(api, "test-plugin"), PluginJson.Default.InitializeResult);
            case PluginMethods.Resync:
                _resync = p.GetRawText();
                return null;
            case PluginMethods.Ping:
                while (_frozen) await Task.Delay(20, CancellationToken.None);
                return Raw("true");
            case PluginMethods.Shutdown:
                return Raw("true");
            case "command/execute":
                {
                    string answer = "null";
                    if (onCommand is not null && p.TryGetProperty("document", out var document))
                    {
                        using var call = JsonDocument.Parse(onCommand.Replace("$document", document.GetString()).Replace("$revision", p.GetProperty("revision").GetInt64().ToString()));
                        try { answer = (await Rpc.RequestAsync(Str(call.RootElement, "method"), Raw(call.RootElement.GetProperty("params").GetRawText()))).GetRawText(); }
                        catch (RpcException e) { answer = $"{{\"error\":{e.Code}}}"; }
                    }
                    lock (_commands) _commands.Add($"{{\"params\":{p.GetRawText()},\"answer\":{answer}}}");
                    return Raw("true");
                }
            case "commands":
                lock (_commands) return Raw("[" + string.Join(",", _commands) + "]");
            case "getResync":
                return Raw(_resync);
            case "echo":
                return Raw(p.GetRawText());
            case "pid":
                return Raw(Environment.ProcessId.ToString());
            case "write":
                File.AppendAllText(Str(p, "file"), Str(p, "text") + "\n");
                return Raw("\"ok\"");
            case "writeThenCrash":
                File.AppendAllText(Str(p, "file"), Str(p, "text") + "\n");
                Console.Error.WriteLine("test plugin: crashing after the write");
                Environment.Exit(42);
                return null;
            case "crash":
                Console.Error.WriteLine("test plugin: asked to crash");
                Environment.Exit(Int(p, "code"));
                return null;
            case "freeze":
                _frozen = true;
                return Raw("true");
            case "awaitUser":
                await Rpc.NotifyAsync(PluginMethods.AwaitingUser, Raw("{\"awaiting\":true}"));
                await Task.Delay(Int(p, "ms"), ct);
                await Rpc.NotifyAsync(PluginMethods.AwaitingUser, Raw("{\"awaiting\":false}"));
                return Raw("\"decided\"");
            case "awaitFrozen":
                await Rpc.NotifyAsync(PluginMethods.AwaitingUser, Raw("{\"awaiting\":true}"));
                _frozen = true;
                await Task.Delay(Int(p, "ms"), ct);
                _frozen = false;
                await Rpc.NotifyAsync(PluginMethods.AwaitingUser, Raw("{\"awaiting\":false}"));
                return Raw("\"decided\"");
            case "slow":
                try
                {
                    await Task.Delay(Int(p, "ms"), ct);
                    return Raw("\"done\"");
                }
                catch (OperationCanceledException)
                {
                    File.AppendAllText(Str(p, "file"), "cancelled\n");
                    throw;
                }
            case "bigText":
                return Raw("\"" + new string('x', Int(p, "bytes")) + "\"");
            case "items":
                return Raw("[" + string.Join(",", Enumerable.Range(0, Int(p, "count"))) + "]");
            case "stats":
                return Raw($"{{\"inFlight\":{Rpc.InFlightIncoming},\"sinkCount\":{Interlocked.Read(ref _sinkCount)},\"sinkBytes\":{Interlocked.Read(ref _sinkBytes)},\"late\":{Rpc.LateResponses}}}");
            case "notifyBig":
                await Rpc.NotifyAsync("big", Raw("\"" + new string('y', Int(p, "bytes")) + "\""));
                return Raw("true");
            case "hammer":
                {
                    var codes = await Task.WhenAll(Enumerable.Range(0, Int(p, "n")).Select(async _ =>
                    {
                        try { await Rpc.RequestAsync("editor.wait"); return 0; }
                        catch (RpcException e) { return e.Code; }
                    }));
                    return Raw($"{{\"ok\":{codes.Count(c => c == 0)},\"tooMany\":{codes.Count(c => c == RpcErrorCodes.TooManyRequests)},\"other\":{codes.Count(c => c != 0 && c != RpcErrorCodes.TooManyRequests)}}}");
                }
            case "callEditor":
                try
                {
                    var r = await Rpc.RequestAsync(Str(p, "method"), p.TryGetProperty("params", out var args) ? Raw(args.GetRawText()) : default);
                    return Raw(r.GetRawText());
                }
                catch (RpcException e)
                {
                    return Raw($"{{\"error\":{e.Code},\"data\":{e.DataJson ?? "null"}}}");
                }
            case "callEditorThenCrash":
                await Rpc.RequestAsync(Str(p, "method"), Raw(p.GetProperty("params").GetRawText()));
                Environment.Exit(43);
                return null;
            case "events":
                lock (_events) return Raw("[" + string.Join(",", _events) + "]");
            case DocumentRequests.Tab:
                lock (_tabs) _tabs.Add(p.GetRawText());
                if (_tabDelayMs > 0) await Task.Delay(_tabDelayMs, ct);
                if (_tabError) throw new RpcException(RpcErrorCodes.InternalError, "test plugin: Tab failed");
                return Raw($"{{\"handled\":{(_tabHandled ? "true" : "false")}}}");
            case "setTab":
                _tabHandled = p.TryGetProperty("handled", out var h) && h.GetBoolean();
                _tabDelayMs = p.TryGetProperty("delayMs", out var delay) ? delay.GetInt32() : 0;
                _tabError = p.TryGetProperty("error", out var err) && err.GetBoolean();
                return Raw("true");
            case DocumentRequests.Completion:
                lock (_completions) _completions.Add(p.GetRawText());
                if (_completionDelayMs > 0) await Task.Delay(_completionDelayMs, ct);
                return Raw(_completionAnswer);
            case "setCompletion":
                _completionAnswer = p.GetProperty("answer").GetRawText();
                _completionDelayMs = p.TryGetProperty("delayMs", out var cd) ? cd.GetInt32() : 0;
                return Raw("true");
            case "completionRequests":
                lock (_completions) return Raw("[" + string.Join(",", _completions) + "]");
            case DocumentRequests.SourceRead:
                lock (_files)
                {
                    SourceReachable();
                    if (!_files.TryGetValue(Str(p, "uri"), out var file)) throw new RpcException(DocumentErrorCodes.SourceNotFound, "test plugin: no such file");
                    return Raw($"{{\"revision\":\"r{file.Revision}\",\"text\":{Q(Encoding.UTF8.GetString(file.Content))}}}");
                }
            case DocumentRequests.SourceStat:
                lock (_files)
                {
                    SourceReachable();
                    return Raw(_files.TryGetValue(Str(p, "uri"), out var stat) ? $"{{\"revision\":\"r{stat.Revision}\",\"size\":{stat.Content.Length}}}" : "{\"exists\":false}");
                }
            case DocumentRequests.SourceList:
                lock (_files)
                {
                    SourceReachable();
                    var folder = Str(p, "uri");
                    return Raw("{\"entries\":[" + string.Join(",", _files.Where(f => f.Key.StartsWith(folder, StringComparison.Ordinal)).Select(f => $"{{\"uri\":{Q(f.Key)},\"size\":{f.Value.Content.Length}}}")) + "]}");
                }
            case DocumentRequests.SourceWrite:
                lock (_files)
                {
                    SourceReachable();
                    var uri = Str(p, "uri");
                    var expected = p.GetProperty("expectedRevision").GetString();
                    var current = _files.TryGetValue(uri, out var existing) ? "r" + existing.Revision : null;
                    // a source that declared atomicWrite decides in one step; the other one writes whatever it is told
                    if (_sourceAtomic && expected != current)
                        throw new RpcException(DocumentErrorCodes.SourceConflict, "test plugin: not at the expected revision") { DataJson = $"{{\"currentRevision\":{(current is null ? "null" : "\"" + current + "\"")}}}" };
                    var revision = existing.Revision + 1;
                    _files[uri] = (Convert.FromBase64String(Str(p, "bytes")), revision);
                    return Raw($"{{\"revision\":\"r{revision}\"}}");
                }
            case "sourcePut":
                lock (_files) _files[Str(p, "uri")] = (Encoding.UTF8.GetBytes(Str(p, "text")), (_files.TryGetValue(Str(p, "uri"), out var put) ? put.Revision : 0) + 1);
                return Raw("true");
            case "sourceGet":
                lock (_files) return Raw(_files.TryGetValue(Str(p, "uri"), out var got) ? $"{{\"revision\":\"r{got.Revision}\",\"text\":{Q(Encoding.UTF8.GetString(got.Content))}}}" : "null");
            case "sourceMode":
                _sourceOffline = p.TryGetProperty("offline", out var off) && off.GetBoolean();
                _sourceAtomic = !p.TryGetProperty("atomic", out var atomic) || atomic.GetBoolean();
                return Raw("true");
            case "tabRequests":
                lock (_tabs) return Raw("[" + string.Join(",", _tabs) + "]");
            default:
                throw new RpcException(RpcErrorCodes.MethodNotFound, $"Unknown method '{method}'.");
        }
    }

    public ValueTask HandleNotificationAsync(string method, JsonElement p)
    {
        switch (method)
        {
            case PluginMethods.Initialized:
                break;
            case PluginMethods.Exit:
                Environment.Exit(0);
                break;
            case PluginMethods.Event:
                lock (_events) _events.Add(p.GetRawText());
                break;
            case "sink":
                Interlocked.Increment(ref _sinkCount);
                Interlocked.Add(ref _sinkBytes, p.GetRawText().Length);
                break;
        }
        return ValueTask.CompletedTask;
    }
}

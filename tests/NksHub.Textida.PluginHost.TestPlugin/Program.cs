using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Rpc;

// Test double for a real plugin: a separate process speaking the plugin protocol on stdin/stdout.
// Options: --api N, --crash-on-start CODE, --stderr-flood LINES, --sleep-before-read MS, --exit-after-handshake.
var api = 1;
var crashOnStart = -1;
var stderrFlood = 0;
var sleepBeforeRead = 0;
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--api": api = int.Parse(args[i + 1]); break;
        case "--crash-on-start": crashOnStart = int.Parse(args[i + 1]); break;
        case "--stderr-flood": stderrFlood = int.Parse(args[i + 1]); break;
        case "--sleep-before-read": sleepBeforeRead = int.Parse(args[i + 1]); break;
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

var handler = new Handler(api);
var limits = new RpcLimits(4 * 1024 * 1024, 64, 64, 100_000, 64 * 1024 * 1024, 64 * 1024 * 1024);
await using var rpc = new JsonRpcDispatcher(Console.OpenStandardInput(), Console.OpenStandardOutput(), RpcProtocols.Plugin, limits, handler);
handler.Rpc = rpc;
await rpc.Completion;
return 0;

sealed class Handler(int api) : IRpcHandler
{
    public JsonRpcDispatcher Rpc = null!;
    volatile bool _frozen;
    long _sinkCount, _sinkBytes;
    string _resync = "null";
    readonly List<string> _events = [];

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
                return Raw("true");
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

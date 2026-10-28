using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NksHub.Textida.Rpc;

/// <summary>
/// The JSON-RPC layer under NativeAOT, for the four adapters (LSP, MCP, ACP, plugins): two dispatchers over in-memory pipes cut into 3-byte pieces. A subset of the scenarios of
/// <c>NksHub.Textida.Rpc.Tests/DispatcherTests</c> that decide whether framing, ids, cancellation and the end of a connection survive trimming and ahead-of-time compilation.
/// </summary>
internal static class RpcChecks
{
    private static readonly RpcLimits Roomy = new(1 << 20, 32, 32, 10_000, 8 << 20, 8 << 20);

    private static readonly (string Name, RpcProtocol Protocol)[] Protocols =
        [("lsp", RpcProtocols.Lsp), ("mcp", RpcProtocols.Mcp), ("acp", RpcProtocols.Acp), ("plugin", RpcProtocols.Plugin)];

    private sealed class Pipe(int chunk = int.MaxValue) : Stream
    {
        private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(1024);
        private byte[] _current = [];
        private int _position;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (_position >= _current.Length)
            {
                try { if (!await _channel.Reader.WaitToReadAsync(ct)) return 0; }
                catch (ChannelClosedException) { return 0; }
                if (_channel.Reader.TryRead(out var next)) { _current = next; _position = 0; }
            }
            int n = Math.Min(buffer.Length, _current.Length - _position);
            _current.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            for (int i = 0; i < buffer.Length; i += chunk)
                await _channel.Writer.WriteAsync(buffer.Slice(i, Math.Min(chunk, buffer.Length - i)).ToArray(), ct);
        }

        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            _channel.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }

    private sealed class Handler(Func<string, JsonElement, CancellationToken, ValueTask<byte[]?>>? onRequest = null) : IRpcHandler
    {
        public ValueTask<byte[]?> HandleRequestAsync(string method, JsonElement parameters, CancellationToken cancellation) =>
            onRequest is null ? throw new RpcException(RpcErrorCodes.MethodNotFound, method) : onRequest(method, parameters, cancellation);

        public ValueTask HandleNotificationAsync(string method, JsonElement parameters) => ValueTask.CompletedTask;
    }

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static Handler Echo(string side) => new((_, p, _) => new ValueTask<byte[]?>(Json($"{{\"side\":\"{side}\",\"echo\":{p.GetRawText()}}}")));

    private static (JsonRpcDispatcher A, JsonRpcDispatcher B) Connect(RpcProtocol protocol, IRpcHandler a, IRpcHandler b, int chunk = int.MaxValue)
    {
        var ab = new Pipe(chunk);
        var ba = new Pipe(chunk);
        return (new JsonRpcDispatcher(ba, ab, protocol, Roomy, a), new JsonRpcDispatcher(ab, ba, protocol, Roomy, b));
    }

    private static async Task<bool> UntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(20);
        return condition();
    }

    public static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var (name, protocol) in Protocols)
        {
            try { await ProtocolAsync(name, protocol, check); }
            catch (Exception e) { check(false, $"rpc {name}: {e.GetType().Name} {e.Message}"); }
        }
        try { await GarbageAsync(check); }
        catch (Exception e) { check(false, $"rpc garbage: {e.GetType().Name} {e.Message}"); }
        Console.WriteLine("rpc: four adapters done");
    }

    private static async Task ProtocolAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        // a request over frames cut in 3-byte pieces; the body holds a character of two UTF-8 bytes
        {
            var (a, b) = Connect(protocol, Echo("A"), Echo("B"), chunk: 3);
            await using (a) await using (b)
            {
                var r = await a.RequestAsync("m", Json("{\"k\":[1,2,\"ž\"]}"));
                check(r.GetProperty("side").GetString() == "B" && r.GetProperty("echo").GetProperty("k")[2].GetString() == "ž", $"rpc {name}: a request round-trips over fragmented frames");
            }
        }

        // both sides call at once with the same ids and each gets its own answer
        {
            Handler Delayed(string side) => new(async (_, p, ct) =>
            {
                await Task.Delay(Random.Shared.Next(1, 20), ct);
                return Json($"{{\"side\":\"{side}\",\"n\":{p.GetProperty("n").GetInt32()}}}");
            });
            var (a, b) = Connect(protocol, Delayed("A"), Delayed("B"));
            await using (a) await using (b)
            {
                var fromA = Enumerable.Range(0, 20).Select(i => a.RequestAsync("m", Json($"{{\"n\":{i}}}"))).ToArray();
                var fromB = Enumerable.Range(0, 20).Select(i => b.RequestAsync("m", Json($"{{\"n\":{i}}}"))).ToArray();
                var results = await Task.WhenAll(fromA.Concat(fromB));
                bool own = true;
                for (int i = 0; i < 20; i++)
                    own &= results[i].GetProperty("side").GetString() == "B" && results[i].GetProperty("n").GetInt32() == i
                        && results[20 + i].GetProperty("side").GetString() == "A" && results[20 + i].GetProperty("n").GetInt32() == i;
                check(own, $"rpc {name}: no answer goes to the request of the other side");
            }
        }

        // cancelling stops the handler and frees the slot; a late answer finds no owner and the next call is answered correctly
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelledHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler(async (method, _, ct) =>
            {
                if (method == "cooperative")
                {
                    started.TrySetResult();
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { cancelledHandler.TrySetResult(); throw; }
                    return null;
                }
                if (method == "ignoresCancel")
                {
                    started.TrySetResult();
                    await finish.Task;
                    return Json("\"stale\"");
                }
                return Json("\"fresh\"");
            });
            var (a, b) = Connect(protocol, Echo("A"), handler);
            await using (a) await using (b)
            {
                using var first = new CancellationTokenSource();
                var call = a.RequestAsync("cooperative", ct: first.Token);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
                first.Cancel();
                bool thrown = await call.ContinueWith(t => t.IsCanceled || t.Exception?.InnerException is OperationCanceledException);
                check(thrown && await cancelledHandler.Task.WaitAsync(TimeSpan.FromSeconds(15)).ContinueWith(t => t.IsCompletedSuccessfully), $"rpc {name}: cancelling reaches the handler of the peer");
                check(await UntilAsync(() => b.InFlightIncoming == 0 && b.InboundBytesInUse == 0) && a.PendingOutgoing == 0, $"rpc {name}: the slot of a cancelled request is free");

                started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var second = new CancellationTokenSource();
                var stale = a.RequestAsync("ignoresCancel", ct: second.Token);
                await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
                second.Cancel();
                await stale.ContinueWith(_ => { });
                var next = await a.RequestAsync("other");
                check(next.GetString() == "fresh", $"rpc {name}: the call after a cancelled one is answered by its own answer");
                finish.SetResult();
                if (protocol.RespondsToCancelled) check(await UntilAsync(() => a.LateResponses == 1), $"rpc {name}: the late answer of the cancelled request is dropped");
                else check(await UntilAsync(() => b.InFlightIncoming == 0) && a.LateResponses == 0, $"rpc {name}: the peer stays silent for a cancelled request");
            }
        }

        // the cancel on the wire is the one of the adapter, with the id of the request
        {
            var input = new Pipe();
            var output = new Pipe();
            await using var rpc = new JsonRpcDispatcher(input, output, protocol, Roomy, Echo("A"));
            using var cts = new CancellationTokenSource();
            var call = rpc.RequestAsync("m", ct: cts.Token);
            var reader = new FrameReader(output);
            var firstFrame = await protocol.Framing.ReadAsync(reader, 4096, default);
            using var request = RpcMessage.Parse(firstFrame.Payload!);
            cts.Cancel();
            await call.ContinueWith(_ => { });
            var secondFrame = await protocol.Framing.ReadAsync(reader, 4096, default);
            using var cancel = RpcMessage.Parse(secondFrame.Payload!);
            check(cancel.Method == protocol.CancelMethod && protocol.TryParseCancel(cancel.Method!, cancel.Params, out var id) && id == request.Id, $"rpc {name}: the wire carries {protocol.CancelMethod} with the id of the request");
        }

        // a peer that goes away fails the pending request and the connection takes no more; an unknown method is an error with a code
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hang = new Handler(async (_, _, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return null; });
            var (a, b) = Connect(protocol, Echo("A"), hang);
            await using (a)
            {
                var call = a.RequestAsync("m");
                await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await b.DisposeAsync();
                var failure = await call.ContinueWith(t => t.Exception?.InnerException);
                check(failure is RpcConnectionClosedException, $"rpc {name}: a peer that goes away fails the pending request ({failure?.GetType().Name})");
                await a.Completion.WaitAsync(TimeSpan.FromSeconds(15));
                var again = await a.RequestAsync("again").ContinueWith(t => t.Exception?.InnerException);
                check(again is RpcConnectionClosedException, $"rpc {name}: a closed connection refuses new requests");
            }
            var (c, d) = Connect(protocol, Echo("A"), new Handler());
            await using (c) await using (d)
            {
                var error = await c.RequestAsync("nope").ContinueWith(t => t.Exception?.InnerException as RpcException);
                check(error?.Code == RpcErrorCodes.MethodNotFound, $"rpc {name}: an unknown method is answered with method-not-found");
            }
        }
    }

    /// <summary>Text that is not JSON gets a parse error and the next request on the same line connection is still answered.</summary>
    private static async Task GarbageAsync(Action<bool, string> check)
    {
        var input = new Pipe();
        var output = new Pipe();
        await using var rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Plugin, Roomy, Echo("B"));
        await input.WriteAsync("this is not json\n{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"m\",\"params\":1}\n"u8.ToArray());
        var reader = new FrameReader(output);
        using var parseError = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
        using var answer = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
        check(parseError.ErrorCode == RpcErrorCodes.ParseError && answer.Id == RpcId.FromNumber(5) && answer.Result.GetProperty("side").GetString() == "B", "rpc plugin: garbage gets a parse error and the connection keeps working");
    }
}

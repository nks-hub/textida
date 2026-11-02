using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NksHub.Textida.Core.Tools;
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
        (string Step, Func<string, RpcProtocol, Action<bool, string>, Task> Run)[] steps =
        [
            ("core", ProtocolAsync), ("framing", FramingAsync), ("same id", SameIdAsync), ("timeout", TimeoutAsync), ("stderr flood", StderrFloodAsync), ("restart", RestartAsync),
        ];
        foreach (var (name, protocol) in Protocols)
            foreach (var (step, run) in steps)
            {
                try { await run(name, protocol, check); }
                catch (Exception e) { check(false, $"rpc {name} {step}: {e.GetType().Name} {e.Message}"); }
            }
        Console.WriteLine("rpc: four adapters done");
    }

    private static async Task ProtocolAsync(string name, RpcProtocol protocol, Action<bool, string> check)    {
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

    private static async Task<RpcMessage> ReadMessageAsync(RpcProtocol protocol, FrameReader reader) =>
        RpcMessage.Parse((await protocol.Framing.ReadAsync(reader, 65536, default)).Payload!);

    private static byte[] Frame(RpcProtocol protocol, string json) => protocol.Framing.Encode(Json(json));

    /// <summary>Malformed JSON, frames stuck together, a frame cut by the end of the stream and a broken header, on the framing of each adapter. The bytes arrive in 3-byte pieces.</summary>
    private static async Task FramingAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        // text that is not JSON gets a parse error and the next frame on the same connection is still answered
        {
            var input = new Pipe(3);
            var output = new Pipe();
            await using var rpc = new JsonRpcDispatcher(input, output, protocol, Roomy, Echo("B"));
            await input.WriteAsync(Frame(protocol, "this is not json").Concat(Frame(protocol, "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"m\",\"params\":1}")).ToArray());
            var reader = new FrameReader(output);
            using var parseError = await ReadMessageAsync(protocol, reader);
            using var answer = await ReadMessageAsync(protocol, reader);
            check(parseError.ErrorCode == RpcErrorCodes.ParseError && answer.Id == RpcId.FromNumber(5) && answer.Result.GetProperty("side").GetString() == "B", $"rpc {name}: garbage gets a parse error and the connection keeps working");
        }

        // frames stuck together in one write: each request gets the answer with its own id and its own params
        {
            var input = new Pipe(3);
            var output = new Pipe();
            await using var rpc = new JsonRpcDispatcher(input, output, protocol, Roomy, Echo("B"));
            await input.WriteAsync(Enumerable.Range(1, 3).SelectMany(i => Frame(protocol, $"{{\"jsonrpc\":\"2.0\",\"id\":{i},\"method\":\"m\",\"params\":{{\"n\":{i}}}}}")).ToArray());
            var reader = new FrameReader(output);
            bool matched = true;
            var seen = new HashSet<long>();
            for (int i = 0; i < 3; i++)
            {
                using var answer = await ReadMessageAsync(protocol, reader);
                long n = answer.Result.GetProperty("echo").GetProperty("n").GetInt64();
                matched &= answer.Id == RpcId.FromNumber(n) && seen.Add(n);
            }
            check(matched && seen.Count == 3, $"rpc {name}: frames stuck together are told apart and answered by their own ids");
        }

        // the stream ends inside a frame: the half frame reaches no handler, the pending call fails and the connection ends with a framing fault
        {
            int handled = 0;
            var input = new Pipe(3);
            await using var rpc = new JsonRpcDispatcher(input, new Pipe(), protocol, Roomy, new Handler((_, _, _) => { Interlocked.Increment(ref handled); return new ValueTask<byte[]?>(Json("1")); }));
            var call = rpc.RequestAsync("m");
            var whole = Frame(protocol, "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"m\"}");
            await input.WriteAsync(whole.AsMemory(0, whole.Length - 4).ToArray());
            input.Dispose();
            await rpc.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            var failure = await call.ContinueWith(t => t.Exception?.InnerException);
            check(rpc.Fault is RpcFramingException && failure is RpcConnectionClosedException && handled == 0, $"rpc {name}: a partial frame at the end of the stream is a framing fault, reaches no handler and fails the pending call ({rpc.Fault?.GetType().Name})");
        }

        // a clean end of the stream between frames is no fault
        {
            var input = new Pipe();
            await using var rpc = new JsonRpcDispatcher(input, new Pipe(), protocol, Roomy, Echo("B"));
            await input.WriteAsync(Frame(protocol, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\",\"params\":1}"));
            input.Dispose();
            await rpc.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            check(rpc.Fault is null && rpc.IsClosed, $"rpc {name}: the end of the stream between frames ends the connection without a fault");
        }

        // a header that is not a length (Content-Length framing only; a line has no header to break)
        if (protocol.Framing is ContentLengthFraming)
        {
            var input = new Pipe(3);
            await using var rpc = new JsonRpcDispatcher(input, new Pipe(), protocol, Roomy, Echo("B"));
            await input.WriteAsync("Content-Length: abc\r\n\r\n{}"u8.ToArray());
            await rpc.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            check(rpc.Fault is RpcFramingException, $"rpc {name}: a broken Content-Length header ends the connection with a framing fault ({rpc.Fault?.GetType().Name})");
        }
    }

    /// <summary>
    /// Both sides use the same number. On the wire: a request of the peer with the id of our pending request is answered as a request,
    /// and only a response carrying that id completes our call, with its own result.
    /// </summary>
    private static async Task SameIdAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        var input = new Pipe();
        var output = new Pipe();
        await using var rpc = new JsonRpcDispatcher(input, output, protocol, Roomy, Echo("A"));
        var call = rpc.RequestAsync("ours");
        var reader = new FrameReader(output);
        using var sent = await ReadMessageAsync(protocol, reader);
        long id = sent.Id!.Value.Number;

        await input.WriteAsync(Frame(protocol, $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"theirs\",\"params\":{{\"v\":7}}}}"));
        using var reply = await ReadMessageAsync(protocol, reader);
        bool answeredTheirs = reply.Id == sent.Id && reply.Result.GetProperty("side").GetString() == "A" && reply.Result.GetProperty("echo").GetProperty("v").GetInt32() == 7;
        check(answeredTheirs && !call.IsCompleted && rpc.PendingOutgoing == 1, $"rpc {name}: a request of the peer with the id of our pending request does not complete our call");

        await input.WriteAsync(Frame(protocol, $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":\"mine\"}}"));
        check((await call.WaitAsync(TimeSpan.FromSeconds(15))).GetString() == "mine" && rpc.LateResponses == 0, $"rpc {name}: only the response with that id completes our call");
    }

    /// <summary>A timeout throws, cancels the handler of the peer, frees the slot, and the next call gets its own answer.</summary>
    private static async Task TimeoutAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (method, _, ct) =>
        {
            if (method != "slow") return Json("\"fresh\"");
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return null;
        });
        var (a, b) = Connect(protocol, Echo("A"), handler);
        await using (a) await using (b)
        {
            // a request still queued when its time runs out never reaches the peer (a busy machine can hold the writer up): ask again
            bool timedOut = true;
            for (int attempt = 0; !started.Task.IsCompleted; attempt++)
            {
                if (attempt >= 20) { timedOut = false; break; }
                var failure = await a.RequestAsync("slow", timeout: TimeSpan.FromMilliseconds(150)).ContinueWith(t => t.Exception?.InnerException);
                timedOut &= failure is RpcTimeoutException;
                await Task.WhenAny(started.Task, Task.Delay(1000));
            }
            check(timedOut, $"rpc {name}: a request that outlasts its timeout throws a timeout");
            check(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15)).ContinueWith(t => t.IsCompletedSuccessfully) && await UntilAsync(() => a.PendingOutgoing == 0 && b.InFlightIncoming == 0), $"rpc {name}: a timeout cancels the handler of the peer and frees the slot");
            var next = await a.RequestAsync("other", timeout: TimeSpan.FromSeconds(15));
            check(next.GetString() == "fresh", $"rpc {name}: the call after a timed-out one gets its own answer");
        }
    }

    private sealed class FloodReader(int lines, string line) : TextReader
    {
        private int _sent;

        public override async Task<string?> ReadLineAsync()
        {
            if (_sent >= lines) return null;
            if (_sent % 500 == 0) await Task.Yield();
            return $"{_sent++}:{line}";
        }
    }

    /// <summary>
    /// A child that floods its standard error: the pump reads to the end, keeps only the last lines (each cut), survives a failing sink, and the calls over the protocol
    /// are answered meanwhile. The flood is a reader here; this tool does not start a real child process.
    /// </summary>
    private static async Task StderrFloodAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        const int lines = 50_000, capacity = 20, maxChars = 100;
        var tail = new Queue<string>();
        int sunk = 0;
        var (a, b) = Connect(protocol, Echo("A"), Echo("B"));
        await using (a) await using (b)
        {
            var pump = Task.Run(() => StderrPump.RunAsync(new FloodReader(lines, new string('e', 2000)), tail, capacity, _ => { if (Interlocked.Increment(ref sunk) % 1000 == 0) throw new InvalidOperationException("sink"); }, maxLineChars: maxChars));
            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => a.RequestAsync("m", Json($"{{\"n\":{i}}}"), timeout: TimeSpan.FromSeconds(30))));
            await pump.WaitAsync(TimeSpan.FromSeconds(60));
            bool answered = results.Select((r, i) => r.GetProperty("echo").GetProperty("n").GetInt32() == i).All(x => x);
            string[] kept;
            lock (tail) kept = [.. tail];
            check(answered, $"rpc {name}: calls are answered with their own params while stderr floods");
            check(sunk == lines && kept.Length == capacity && kept[^1].StartsWith($"{lines - 1}:") && kept[0].StartsWith($"{lines - capacity}:") && kept.All(l => l.Length <= maxChars),
                $"rpc {name}: a flooded stderr is read to its end, keeps the last {capacity} lines cut to {maxChars} chars, and a failing sink does not stop it");
        }
    }

    /// <summary>
    /// A write is cancelled and its late answer arrives; a second write is in flight when the peer dies; the connection starts again. The dispatcher never sends a request twice,
    /// the new connection hears only what the caller asks of it, and nothing of the old connection answers the new call. The restart of a real process is not done by this tool.
    /// </summary>
    private static async Task RestartAsync(string name, RpcProtocol protocol, Action<bool, string> check)
    {
        int writes = 0, newWrites = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (a, b) = Connect(protocol, Echo("A"), new Handler(async (method, _, _) =>
        {
            if (method != "write") return Json("\"fresh\"");
            Interlocked.Increment(ref writes);
            started.TrySetResult();
            await finish.Task;
            return Json("\"stale\"");
        }));
        await using (a) await using (b)
        {
            using var cts = new CancellationTokenSource();
            var first = a.RequestAsync("write", Json("{\"seq\":1}"), ct: cts.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cts.Cancel();
            await first.ContinueWith(_ => { });
            // the cancel went through the pipe before this request, so the peer has handled it when this one is answered
            check((await a.RequestAsync("other")).GetString() == "fresh", $"rpc {name}: the call after a cancelled write gets its own answer");
            finish.SetResult();
            bool late = protocol.RespondsToCancelled ? await UntilAsync(() => a.LateResponses == 1) : await UntilAsync(() => b.InFlightIncoming == 0) && a.LateResponses == 0;
            check(late && a.PendingOutgoing == 0 && Volatile.Read(ref writes) == 1, $"rpc {name}: the late answer of a cancelled write completes nothing and the write was made once");
        }

        started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (a1, b1) = Connect(protocol, Echo("A"), new Handler(async (_, _, ct) =>
        {
            Interlocked.Increment(ref writes);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }));
        await using (a1)
        {
            var inFlight = a1.RequestAsync("write", Json("{\"seq\":2}"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await b1.DisposeAsync();
            var failure = await inFlight.ContinueWith(t => t.Exception?.InnerException);
            check(failure is RpcConnectionClosedException, $"rpc {name}: a write in flight when the peer dies fails");
        }

        var (a2, b2) = Connect(protocol, Echo("A"), new Handler((_, _, _) => { Interlocked.Increment(ref newWrites); return new ValueTask<byte[]?>(Json("\"fresh\"")); }));
        await using (a2) await using (b2)
        {
            await Task.Delay(200);
            check(Volatile.Read(ref newWrites) == 0, $"rpc {name}: the restarted connection receives no write the caller did not ask for");
            var again = await a2.RequestAsync("write", Json("{\"seq\":2}"), timeout: TimeSpan.FromSeconds(15));
            await Task.Delay(100);
            check(again.GetString() == "fresh" && Volatile.Read(ref newWrites) == 1 && Volatile.Read(ref writes) == 2 && a2.LateResponses == 0,
                $"rpc {name}: after the restart the write is made once and answered by the new peer (old {writes}, new {newWrites})");
        }
    }
}

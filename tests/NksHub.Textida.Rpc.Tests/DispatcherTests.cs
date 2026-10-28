using System.Text.Json;

namespace NksHub.Textida.Rpc.Tests;

public class DispatcherTests
{
    public static TheoryData<string> Protocols => ["lsp", "mcp", "acp", "plugin"];

    static RpcProtocol P(string name) => name switch
    {
        "lsp" => RpcProtocols.Lsp,
        "mcp" => RpcProtocols.Mcp,
        "acp" => RpcProtocols.Acp,
        _ => RpcProtocols.Plugin,
    };

    static DelegateHandler Echo(string side) => new((method, p, ct) =>
        new ValueTask<byte[]?>(T.Json($"{{\"side\":\"{side}\",\"echo\":{p.GetRawText()}}}")));

    static RpcLimits With(Func<RpcLimits, RpcLimits> change) => change(T.Roomy);

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task RequestsRoundTripOverFragmentedFrames(string name)
    {
        await using var pair = Pair.Create(P(name), T.Roomy, Echo("A"), T.Roomy, Echo("B"), chunk: 3);
        var r = await pair.A.RequestAsync("m", T.Json("{\"k\":[1,2,\"ž\"]}"));
        Assert.Equal("B", r.GetProperty("side").GetString());
        Assert.Equal(3, r.GetProperty("echo").GetProperty("k").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task BothSidesCallAtOnceWithTheSameIdsAndEachGetsItsOwnAnswer(string name)
    {
        var handler = (string side) => new DelegateHandler(async (method, p, ct) =>
        {
            await Task.Delay(Random.Shared.Next(1, 20), ct);
            return T.Json($"{{\"side\":\"{side}\",\"n\":{p.GetProperty("n").GetInt32()}}}");
        });
        await using var pair = Pair.Create(P(name), T.Roomy, handler("A"), T.Roomy, handler("B"));
        var fromA = Enumerable.Range(0, 20).Select(i => pair.A.RequestAsync("m", T.Json($"{{\"n\":{i}}}"))).ToArray();
        var fromB = Enumerable.Range(0, 20).Select(i => pair.B.RequestAsync("m", T.Json($"{{\"n\":{i}}}"))).ToArray();
        var results = await Task.WhenAll(fromA.Concat(fromB));
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal("B", results[i].GetProperty("side").GetString());
            Assert.Equal(i, results[i].GetProperty("n").GetInt32());
            Assert.Equal("A", results[20 + i].GetProperty("side").GetString());
            Assert.Equal(i, results[20 + i].GetProperty("n").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task CancellingARequestStopsTheHandlerAndFreesItsSlot(string name)
    {
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            return null;
        });
        await using var pair = Pair.Create(P(name), T.Roomy, Echo("A"), T.Roomy, handler);
        using var cts = new CancellationTokenSource();
        var call = pair.A.RequestAsync("slow", ct: cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await T.Until(() => pair.B.InFlightIncoming == 0 && pair.B.InboundBytesInUse == 0);
        Assert.Equal(0, pair.A.PendingOutgoing);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task ALateAnswerAfterCancellationFindsNoOwnerAndTheNextCallIsAnsweredCorrectly(string name)
    {
        var started = new TaskCompletionSource();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            if (method == "ignoresCancel")
            {
                started.TrySetResult();
                // not a timer: on a busy machine the cancel could come later than any fixed delay, and the call would then finish with "stale" instead of being cancelled
                await finish.Task;
                return T.Json("\"stale\"");
            }
            return T.Json("\"fresh\"");
        });
        await using var pair = Pair.Create(P(name), T.Roomy, Echo("A"), T.Roomy, handler);
        using var cts = new CancellationTokenSource();
        var call = pair.A.RequestAsync("ignoresCancel", ct: cts.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        // the cancel went through the pipe before this request, so the peer has handled it when this one is answered
        var next = await pair.A.RequestAsync("other");
        Assert.Equal("fresh", next.GetString());
        finish.SetResult();
        if (P(name).RespondsToCancelled)
        {
            await T.Until(() => pair.A.LateResponses == 1);
            var error = await Record.ExceptionAsync(async () => await pair.A.RequestAsync("other"));
            Assert.Null(error);
        }
        else
        {
            await T.Until(() => pair.B.InFlightIncoming == 0);
            await Task.Delay(300);
            Assert.Equal(0, pair.A.LateResponses);
        }
    }

    [Fact]
    public async Task TheCancelNotificationOnTheWireIsTheOneOfTheAdapter()
    {
        foreach (var protocol in new[] { RpcProtocols.Lsp, RpcProtocols.Mcp, RpcProtocols.Acp, RpcProtocols.Plugin })
        {
            var input = new MemoryPipe();
            var output = new MemoryPipe();
            await using var rpc = new JsonRpcDispatcher(input, output, protocol, T.Roomy, Echo("A"));
            using var cts = new CancellationTokenSource();
            var call = rpc.RequestAsync("m", ct: cts.Token);
            var reader = new FrameReader(output);
            var first = await protocol.Framing.ReadAsync(reader, 4096, default);
            using var request = RpcMessage.Parse(first.Payload!);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
            var second = await protocol.Framing.ReadAsync(reader, 4096, default);
            using var cancel = RpcMessage.Parse(second.Payload!);
            Assert.Equal(protocol.CancelMethod, cancel.Method);
            Assert.True(protocol.TryParseCancel(cancel.Method!, cancel.Params, out var id));
            Assert.Equal(request.Id, id);
        }
    }

    [Fact]
    public async Task AnotherProtocolsCancelIsAnOrdinaryNotificationAndCancelsNothing()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<string>();
        var observedCancel = false;
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            started.SetResult();
            await release.Task;
            observedCancel = ct.IsCancellationRequested;
            return T.Json("true");
        }, (method, p) => { lock (seen) seen.Add(method); return ValueTask.CompletedTask; });
        await using var pair = Pair.Create(RpcProtocols.Mcp, T.Roomy, Echo("A"), T.Roomy, handler);
        var call = pair.A.RequestAsync("work");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await pair.A.NotifyAsync(RpcProtocols.Lsp.CancelMethod, RpcProtocols.Lsp.CancelParams(RpcId.FromNumber(1)));
        await T.Until(() => { lock (seen) return seen.Count == 1; });
        release.SetResult();
        Assert.True((await call).GetBoolean());
        Assert.False(observedCancel);
        lock (seen) Assert.Equal(["$/cancelRequest"], seen);
    }

    [Fact]
    public async Task ATimeoutThrowsAndCancelsTheHandler()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return null;
        });
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), T.Roomy, handler);
        // a request still in the queue when its time runs out is taken back and never reaches the handler (a busy machine can hold the writer up that long): ask again
        for (var attempt = 0; !started.Task.IsCompleted; attempt++)
        {
            Assert.True(attempt < 20, "no request reached the handler");
            await Assert.ThrowsAsync<RpcTimeoutException>(() => pair.A.RequestAsync("m", timeout: TimeSpan.FromMilliseconds(150)));
            await Task.WhenAny(started.Task, Task.Delay(1000));
        }
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, pair.A.PendingOutgoing);
    }

    [Fact]
    public async Task APeerThatGoesAwayFailsPendingRequests()
    {
        var started = new TaskCompletionSource();
        var handler = new DelegateHandler(async (method, p, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return null; });
        await using var pair = Pair.Create(RpcProtocols.Lsp, T.Roomy, Echo("A"), T.Roomy, handler);
        var call = pair.A.RequestAsync("m");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await pair.B.DisposeAsync();
        await Assert.ThrowsAsync<RpcConnectionClosedException>(() => call);
        await pair.A.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<RpcConnectionClosedException>(() => pair.A.RequestAsync("again"));
    }

    [Fact]
    public async Task GarbageGetsAParseErrorAndTheConnectionKeepsWorking()
    {
        var input = new MemoryPipe();
        var output = new MemoryPipe();
        await using var rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Plugin, T.Roomy, Echo("B"));
        await input.WriteAsync("this is not json\n{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"m\",\"params\":1}\n"u8.ToArray());
        var reader = new FrameReader(output);
        using var parseError = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
        Assert.Equal(RpcErrorCodes.ParseError, parseError.ErrorCode);
        using var answer = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
        Assert.Equal(RpcId.FromNumber(5), answer.Id);
        Assert.Equal("B", answer.Result.GetProperty("side").GetString());
    }

    [Fact]
    public async Task AnUnknownMethodIsAnErrorWithTheRequestId()
    {
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), T.Roomy, new DelegateHandler());
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("nope"));
        Assert.Equal(RpcErrorCodes.MethodNotFound, e.Code);
    }

    [Theory]
    [InlineData("lsp")]
    [InlineData("plugin")]
    public async Task AnOversizeRequestIsRefusedWithItsIdAndTheConnectionGoesOn(string name)
    {
        await using var pair = Pair.Create(P(name), T.Roomy, Echo("A"), With(l => l with { MaxMessageBytes = 1000 }), Echo("B"));
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m", T.Json("\"" + new string('x', 5000) + "\"")));
        Assert.Equal(RpcErrorCodes.MessageTooLarge, e.Code);
        Assert.Equal("B", (await pair.A.RequestAsync("m", T.Json("1"))).GetProperty("side").GetString());
    }

    [Theory]
    [InlineData("lsp")]
    [InlineData("plugin")]
    public async Task AnOversizeResponseFailsTheCallerInsteadOfLeavingItWaiting(string name)
    {
        var big = new DelegateHandler((m, p, ct) => new ValueTask<byte[]?>(T.Json("\"" + new string('x', 5000) + "\"")));
        await using var pair = Pair.Create(P(name), With(l => l with { MaxMessageBytes = 1000 }), Echo("A"), T.Roomy, big);
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m", timeout: TimeSpan.FromSeconds(10)));
        Assert.Equal(RpcErrorCodes.MessageTooLarge, e.Code);
    }

    [Fact]
    public async Task AnOutgoingMessageOverTheLimitThrowsBeforeAnythingIsSent()
    {
        var reached = 0;
        var peer = new DelegateHandler((m, p, ct) =>
        {
            Interlocked.Increment(ref reached);
            return new ValueTask<byte[]?>(T.Json("true"));
        });
        await using var pair = Pair.Create(RpcProtocols.Plugin, With(l => l with { MaxMessageBytes = 1000 }), Echo("A"), T.Roomy, peer);
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m", T.Json("\"" + new string('x', 5000) + "\"")));
        Assert.Equal(RpcErrorCodes.MessageTooLarge, e.Code);
        Assert.Equal(0, pair.A.PendingOutgoing);
        Assert.Equal(0, pair.A.QueuedOutgoingBytes);
        await Task.Delay(200);
        Assert.Equal(0, Volatile.Read(ref reached)); // the peer never saw it
    }

    [Fact]
    public async Task RequestsBeyondTheConcurrencyLimitAreRefusedAndTheOthersFinish()
    {
        var gate = new TaskCompletionSource();
        var handler = new DelegateHandler(async (m, p, ct) => { await gate.Task; return T.Json("\"done\""); });
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), With(l => l with { MaxConcurrentIncoming = 2 }), handler);
        var first = pair.A.RequestAsync("m");
        var second = pair.A.RequestAsync("m");
        await T.Until(() => pair.B.InFlightIncoming == 2);
        var refused = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Record.ExceptionAsync(() => pair.A.RequestAsync("m"))));
        Assert.All(refused, e => Assert.Equal(RpcErrorCodes.TooManyRequests, Assert.IsType<RpcException>(e).Code));
        gate.SetResult();
        Assert.Equal("done", (await first).GetString());
        Assert.Equal("done", (await second).GetString());
        await T.Until(() => pair.B.InFlightIncoming == 0);
    }

    [Fact]
    public async Task AResultWithTooManyItemsIsRefusedOnBothSides()
    {
        var fifty = new DelegateHandler((m, p, ct) => new ValueTask<byte[]?>(T.Json("[" + string.Join(",", Enumerable.Range(0, 50)) + "]")));
        await using (var sender = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), With(l => l with { MaxResultItems = 10 }), fifty))
        {
            var e = await Assert.ThrowsAsync<RpcException>(() => sender.A.RequestAsync("m", timeout: TimeSpan.FromSeconds(10)));
            Assert.Equal(RpcErrorCodes.TooManyResults, e.Code);
            Assert.Equal(0, sender.B.InFlightIncoming);
        }
        await using (var receiver = Pair.Create(RpcProtocols.Plugin, With(l => l with { MaxResultItems = 10 }), Echo("A"), T.Roomy, fifty))
        {
            var e = await Assert.ThrowsAsync<RpcException>(() => receiver.A.RequestAsync("m", timeout: TimeSpan.FromSeconds(10)));
            Assert.Equal(RpcErrorCodes.TooManyResults, e.Code);
            Assert.Equal(0, receiver.A.PendingOutgoing);
        }
    }

    [Fact]
    public async Task RequestsOverTheSessionMemoryBudgetAreRefusedAndTheBudgetIsReturned()
    {
        var gate = new TaskCompletionSource();
        var handler = new DelegateHandler(async (m, p, ct) => { await gate.Task; return T.Json("\"done\""); });
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), With(l => l with { MaxSessionBytes = 2500 }), handler);
        var held = pair.A.RequestAsync("m", T.Json("\"" + new string('x', 1500) + "\""));
        await T.Until(() => pair.B.InboundBytesInUse > 1500);
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m", T.Json("\"" + new string('x', 1500) + "\"")));
        Assert.Equal(RpcErrorCodes.SessionMemoryExceeded, e.Code);
        gate.SetResult();
        await held;
        await T.Until(() => pair.B.InboundBytesInUse == 0);
    }

    [Fact]
    public async Task ANotificationOverTheSessionMemoryBudgetEndsTheConnectionOnBothSides()
    {
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), With(l => l with { MaxSessionBytes = 2000 }), Echo("B"));
        await pair.A.NotifyAsync("big", T.Json("\"" + new string('x', 3000) + "\""));
        await pair.B.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RpcErrorCodes.SessionMemoryExceeded, Assert.IsType<RpcException>(pair.B.Fault).Code);
        await pair.A.Completion.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CallersWaitWhenTooManyRequestsAreAlreadyPending()
    {
        var gate = new TaskCompletionSource();
        var handler = new DelegateHandler(async (m, p, ct) => { await gate.Task; return T.Json("\"done\""); });
        await using var pair = Pair.Create(RpcProtocols.Plugin, With(l => l with { MaxPendingOutgoing = 2 }), Echo("A"), T.Roomy, handler);
        var calls = Enumerable.Range(0, 3).Select(_ => pair.A.RequestAsync("m")).ToArray();
        await T.Until(() => pair.B.InFlightIncoming == 2);
        await Task.Delay(200);
        Assert.False(calls[2].IsCompleted);
        Assert.Equal(2, pair.B.InFlightIncoming);
        gate.SetResult();
        Assert.All(await Task.WhenAll(calls), r => Assert.Equal("done", r.GetString()));
    }

    [Fact]
    public async Task ASenderWaitsWhileTheOutgoingQueueIsFullAndContinuesWhenThePeerReads()
    {
        var size = RpcWire.Notification("n", T.Json("\"" + new string('x', 400) + "\"")).Length;
        var input = new MemoryPipe();
        var output = new MemoryPipe(capacityChunks: 1);
        await using var rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Plugin, With(l => l with { MaxOutgoingQueueBytes = size * 5 / 2 }), Echo("A"));
        var sends = Enumerable.Range(0, 6).Select(_ => rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""))).ToArray();
        await Task.Delay(300);
        Assert.InRange(sends.Count(s => s.IsCompletedSuccessfully), 1, 5);
        Assert.True(rpc.QueuedOutgoingBytes <= size * 5 / 2);

        var reader = new FrameReader(output);
        for (var i = 0; i < 6; i++) Assert.Equal(FrameStatus.Ok, (await new LineFraming().ReadAsync(reader, 4096, default)).Status);
        await Task.WhenAll(sends).WaitAsync(TimeSpan.FromSeconds(10));
        await T.Until(() => rpc.QueuedOutgoingBytes == 0);
    }

    [Fact]
    public async Task ASenderStuckInTheQueueCanGiveUp()
    {
        var size = RpcWire.Notification("n", T.Json("\"" + new string('x', 400) + "\"")).Length;
        await using var rpc = new JsonRpcDispatcher(new MemoryPipe(), new MemoryPipe(capacityChunks: 1), RpcProtocols.Plugin, With(l => l with { MaxOutgoingQueueBytes = size }), Echo("A"));
        _ = rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""));
        _ = rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""));
        await Task.Delay(200);
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""), cts.Token));
    }

    [Fact]
    public async Task ATimeoutAlsoEndsTheWaitForAFreeSlotAndTheSlotIsNotLost()
    {
        var gate = new TaskCompletionSource();
        var handler = new DelegateHandler(async (m, p, ct) => { await gate.Task; return T.Json("\"done\""); });
        await using var pair = Pair.Create(RpcProtocols.Plugin, With(l => l with { MaxPendingOutgoing = 1 }), Echo("A"), T.Roomy, handler);
        var first = pair.A.RequestAsync("m");
        await T.Until(() => pair.B.InFlightIncoming == 1);
        await Assert.ThrowsAsync<RpcTimeoutException>(() => pair.A.RequestAsync("m", timeout: TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, pair.B.InFlightIncoming);
        gate.SetResult();
        Assert.Equal("done", (await first).GetString());
        Assert.Equal("done", (await pair.A.RequestAsync("m", timeout: TimeSpan.FromSeconds(10))).GetString());
    }

    [Fact]
    public async Task ATimeoutAlsoEndsTheWaitForRoomInTheQueue()
    {
        var size = RpcWire.Notification("n", T.Json("\"" + new string('x', 400) + "\"")).Length;
        await using var rpc = new JsonRpcDispatcher(new MemoryPipe(), new MemoryPipe(capacityChunks: 1), RpcProtocols.Plugin, With(l => l with { MaxOutgoingQueueBytes = size }), Echo("A"));
        _ = rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""));
        _ = rpc.NotifyAsync("n", T.Json("\"" + new string('x', 400) + "\""));
        await T.Until(() => rpc.QueuedOutgoingBytes == size);
        await Assert.ThrowsAsync<RpcTimeoutException>(() => rpc.RequestAsync("m", timeout: TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, rpc.PendingOutgoing);
        Assert.Equal(size, rpc.QueuedOutgoingBytes);
    }

    [Fact]
    public async Task ARequestCancelledWhileStillQueuedIsNeverSentAndNeedsNoCancel()
    {
        var output = new MemoryPipe(capacityChunks: 1);
        await using var rpc = new JsonRpcDispatcher(new MemoryPipe(), output, RpcProtocols.Plugin, T.Roomy, Echo("A"));
        var second = RpcWire.Notification("second").Length;
        await rpc.NotifyAsync("first");
        await rpc.NotifyAsync("second");
        await T.Until(() => rpc.QueuedOutgoingBytes == second);
        using var cts = new CancellationTokenSource();
        var call = rpc.RequestAsync("never", ct: cts.Token);
        await T.Until(() => rpc.PendingOutgoing == 1 && rpc.QueuedOutgoingBytes > second);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(second, rpc.QueuedOutgoingBytes);
        await rpc.NotifyAsync("last");

        var reader = new FrameReader(output);
        var methods = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var m = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
            methods.Add(m.Method!);
        }
        Assert.Equal(["first", "second", "last"], methods);
        Assert.Equal(0, rpc.QueuedReplyBytes);
    }

    [Fact]
    public async Task ARequestThatTimesOutWhileStillQueuedIsNeverSentAndNeedsNoCancel()
    {
        var output = new MemoryPipe(capacityChunks: 1);
        await using var rpc = new JsonRpcDispatcher(new MemoryPipe(), output, RpcProtocols.Plugin, T.Roomy, Echo("A"));
        var second = RpcWire.Notification("second").Length;
        await rpc.NotifyAsync("first");
        await rpc.NotifyAsync("second");
        await T.Until(() => rpc.QueuedOutgoingBytes == second);
        await Assert.ThrowsAsync<RpcTimeoutException>(() => rpc.RequestAsync("never", timeout: TimeSpan.FromMilliseconds(200)).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, rpc.PendingOutgoing);
        Assert.Equal(second, rpc.QueuedOutgoingBytes);
        await rpc.NotifyAsync("last");

        var reader = new FrameReader(output);
        var methods = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var m = RpcMessage.Parse((await RpcProtocols.Plugin.Framing.ReadAsync(reader, 4096, default)).Payload!);
            methods.Add(m.Method!);
        }
        Assert.Equal(["first", "second", "last"], methods);
        Assert.Equal(0, rpc.QueuedReplyBytes);
    }

    /// <summary>
    /// Requests 1 to 4 to a dispatcher whose peer reads nothing: answer 1 sits in the pipe, answer 2 is being written,
    /// answers 3 and 4 wait for room in the queue and hold both slots.
    /// </summary>
    static async Task<(JsonRpcDispatcher Rpc, MemoryPipe Input, MemoryPipe Output)> StuckAnswersAsync(RpcProtocol protocol)
    {
        var input = new MemoryPipe();
        var output = new MemoryPipe(capacityChunks: 1);
        var answer = T.Json("\"" + new string('a', 1000) + "\"");
        var handled = 0;
        var rpc = new JsonRpcDispatcher(input, output, protocol, With(l => l with { MaxConcurrentIncoming = 2, MaxOutgoingQueueBytes = 1500 }),
            new DelegateHandler((m, p, ct) => { Interlocked.Increment(ref handled); return new ValueTask<byte[]?>(answer); }));
        Task Send(long id) => input.WriteAsync(protocol.Framing.Encode(RpcWire.Request(RpcId.FromNumber(id), "m"))).AsTask();
        await Send(1);
        await T.Until(() => Volatile.Read(ref handled) == 1 && rpc.InFlightIncoming == 0 && rpc.QueuedOutgoingBytes == 0);
        await Send(2);
        await T.Until(() => Volatile.Read(ref handled) == 2 && rpc.InFlightIncoming == 0 && rpc.QueuedOutgoingBytes > 0);
        await Send(3);
        await T.Until(() => rpc.InFlightIncoming == 1);
        await Send(4);
        await T.Until(() => rpc.InFlightIncoming == 2);
        return (rpc, input, output);
    }

    /// <summary>Reads <paramref name="count"/> replies: the id of each with 0 for a result or the code of its error.</summary>
    static async Task<Dictionary<long, int>> ReadRepliesAsync(RpcProtocol protocol, MemoryPipe output, int count)
    {
        var reader = new FrameReader(output);
        var replies = new Dictionary<long, int>();
        for (var i = 0; i < count; i++)
        {
            var frame = await protocol.Framing.ReadAsync(reader, 4096, default).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            using var m = RpcMessage.Parse(frame.Payload!);
            replies[m.Id!.Value.Number] = m.Kind == RpcKind.ErrorResponse ? m.ErrorCode : 0;
        }
        return replies;
    }

    [Fact]
    public async Task AnswersToAPeerThatDoesNotReadWaitForRoomAndHoldTheirSlots()
    {
        var (rpc, input, output) = await StuckAnswersAsync(RpcProtocols.Plugin);
        await using (rpc)
        {
            Assert.True(rpc.QueuedOutgoingBytes <= 1500);
            await input.WriteAsync(RpcProtocols.Plugin.Framing.Encode(RpcWire.Request(RpcId.FromNumber(5), "m")));
            await T.Until(() => rpc.QueuedReplyBytes > 0);
            var replies = await ReadRepliesAsync(RpcProtocols.Plugin, output, 5);
            Assert.Equal(RpcErrorCodes.TooManyRequests, replies[5]);
            Assert.All(new long[] { 1, 2, 3, 4 }, id => Assert.Equal(0, replies[id]));
            await T.Until(() => rpc.InFlightIncoming == 0 && rpc.QueuedOutgoingBytes == 0 && rpc.QueuedReplyBytes == 0);
        }
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task ACancelFreesTheSlotOfARequestWhoseAnswerWaitsForRoom(string name)
    {
        var protocol = P(name);
        var (rpc, input, output) = await StuckAnswersAsync(protocol);
        await using (rpc)
        {
            await input.WriteAsync(protocol.Framing.Encode(RpcWire.Notification(protocol.CancelMethod, protocol.CancelParams(RpcId.FromNumber(3)))));
            await T.Until(() => rpc.InFlightIncoming == 1);
            await input.WriteAsync(protocol.Framing.Encode(RpcWire.Request(RpcId.FromNumber(6), "m")));
            await T.Until(() => rpc.InFlightIncoming == 2);
            var replies = await ReadRepliesAsync(protocol, output, protocol.RespondsToCancelled ? 5 : 4);
            Assert.Equal(protocol.RespondsToCancelled ? new long[] { 1, 2, 3, 4, 6 } : new long[] { 1, 2, 4, 6 }, replies.Keys.Order());
            if (protocol.RespondsToCancelled) Assert.Equal(RpcErrorCodes.RequestCancelled, replies[3]);
            Assert.Equal(0, replies[6]);
        }
    }

    [Fact]
    public async Task APeerThatKeepsSendingWithoutReadingTheRepliesIsCutOff()
    {
        var input = new MemoryPipe();
        var output = new MemoryPipe(capacityChunks: 1);
        var stuck = new DelegateHandler(async (m, p, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; });
        await using var rpc = new JsonRpcDispatcher(input, output, RpcProtocols.Plugin, With(l => l with { MaxConcurrentIncoming = 1, MaxOutgoingQueueBytes = 2000 }), stuck);
        var flood = Enumerable.Range(1, 200).SelectMany(i => RpcProtocols.Plugin.Framing.Encode(RpcWire.Request(RpcId.FromNumber(i), "m"))).ToArray();
        await input.WriteAsync(flood);
        await rpc.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RpcErrorCodes.SessionMemoryExceeded, Assert.IsType<RpcException>(rpc.Fault).Code);
        Assert.True(rpc.QueuedReplyBytes <= 2000);
    }
}

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
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            if (method == "ignoresCancel")
            {
                started.TrySetResult();
                await Task.Delay(300, CancellationToken.None);
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
        var next = await pair.A.RequestAsync("other");
        Assert.Equal("fresh", next.GetString());
        if (P(name).RespondsToCancelled)
        {
            await T.Until(() => pair.A.LateResponses == 1);
            var error = await Record.ExceptionAsync(async () => await pair.A.RequestAsync("other"));
            Assert.Null(error);
        }
        else
        {
            await Task.Delay(500);
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
        var seen = new List<string>();
        var observedCancel = false;
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            started.SetResult();
            await Task.Delay(400, CancellationToken.None);
            observedCancel = ct.IsCancellationRequested;
            return T.Json("true");
        }, (method, p) => { lock (seen) seen.Add(method); return ValueTask.CompletedTask; });
        await using var pair = Pair.Create(RpcProtocols.Mcp, T.Roomy, Echo("A"), T.Roomy, handler);
        var call = pair.A.RequestAsync("work");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await pair.A.NotifyAsync(RpcProtocols.Lsp.CancelMethod, RpcProtocols.Lsp.CancelParams(RpcId.FromNumber(1)));
        Assert.True((await call).GetBoolean());
        Assert.False(observedCancel);
        lock (seen) Assert.Equal(["$/cancelRequest"], seen);
    }

    [Fact]
    public async Task ATimeoutThrowsAndCancelsTheHandler()
    {
        var cancelled = new TaskCompletionSource();
        var handler = new DelegateHandler(async (method, p, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            return null;
        });
        await using var pair = Pair.Create(RpcProtocols.Plugin, T.Roomy, Echo("A"), T.Roomy, handler);
        await Assert.ThrowsAsync<RpcTimeoutException>(() => pair.A.RequestAsync("m", timeout: TimeSpan.FromMilliseconds(150)));
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
        await using var pair = Pair.Create(RpcProtocols.Plugin, With(l => l with { MaxMessageBytes = 1000 }), Echo("A"), T.Roomy, Echo("B"));
        var e = await Assert.ThrowsAsync<RpcException>(() => pair.A.RequestAsync("m", T.Json("\"" + new string('x', 5000) + "\"")));
        Assert.Equal(RpcErrorCodes.MessageTooLarge, e.Code);
        Assert.Equal(0, pair.A.PendingOutgoing);
        Assert.Equal(0, pair.A.QueuedOutgoingBytes);
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
}

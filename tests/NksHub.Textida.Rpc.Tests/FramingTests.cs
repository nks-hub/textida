using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Rpc.Tests;

public class FramingTests
{
    static FrameReader Reader(byte[] bytes, int chunk = int.MaxValue)
    {
        var pipe = new MemoryPipe(100_000, chunk);
        pipe.WriteAsync(bytes).AsTask().Wait();
        pipe.Dispose();
        return new FrameReader(pipe);
    }

    static string Text(Frame f) => Encoding.UTF8.GetString(f.Payload!);

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LineFramingSplitsGluedAndFragmentedInput(int chunk)
    {
        var r = Reader(Encoding.UTF8.GetBytes("{\"a\":1}\n\n{\"b\":2}\r\n{\"c\":3}\n"), chunk);
        var f = new LineFraming();
        Assert.Equal("{\"a\":1}", Text(await f.ReadAsync(r, 100, default)));
        Assert.Equal("{\"b\":2}", Text(await f.ReadAsync(r, 100, default)));
        Assert.Equal("{\"c\":3}", Text(await f.ReadAsync(r, 100, default)));
        Assert.Equal(FrameStatus.Eof, (await f.ReadAsync(r, 100, default)).Status);
    }

    [Fact]
    public async Task LineFramingDropsAnOversizeLineAndKeepsTheNext()
    {
        var r = Reader(Encoding.UTF8.GetBytes(new string('x', 5000) + "\n{\"ok\":true}\n"), 7);
        var f = new LineFraming();
        var big = await f.ReadAsync(r, 100, default);
        Assert.Equal(FrameStatus.Oversize, big.Status);
        Assert.Equal(new string('x', FrameReader.PrefixBytes), Encoding.UTF8.GetString(big.Prefix!));
        Assert.Equal("{\"ok\":true}", Text(await f.ReadAsync(r, 100, default)));
    }

    [Fact]
    public async Task LineFramingReportsAStreamThatEndsInsideAFrame()
    {
        var r = Reader(Encoding.UTF8.GetBytes("{\"a\":1}\n{\"b\":"));
        var f = new LineFraming();
        await f.ReadAsync(r, 100, default);
        await Assert.ThrowsAsync<RpcFramingException>(async () => await f.ReadAsync(r, 100, default));
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ContentLengthCountsBytesNotCharacters(int chunk)
    {
        var f = new ContentLengthFraming();
        var a = f.Encode(Encoding.UTF8.GetBytes("{\"t\":\"žluťoučký\"}"));
        var b = f.Encode(Encoding.UTF8.GetBytes("{\"t\":\"🙂\"}"));
        var r = Reader([.. a, .. b], chunk);
        Assert.Equal("{\"t\":\"žluťoučký\"}", Text(await f.ReadAsync(r, 1000, default)));
        Assert.Equal("{\"t\":\"🙂\"}", Text(await f.ReadAsync(r, 1000, default)));
        Assert.Equal(FrameStatus.Eof, (await f.ReadAsync(r, 1000, default)).Status);
        Assert.StartsWith($"Content-Length: {Encoding.UTF8.GetByteCount("{\"t\":\"žluťoučký\"}")}\r\n\r\n", Encoding.UTF8.GetString(a));
    }

    [Fact]
    public async Task ContentLengthAcceptsOtherHeadersInAnyCase()
    {
        var r = Reader(Encoding.ASCII.GetBytes("content-length: 2\r\nContent-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n{}"));
        Assert.Equal("{}", Text(await new ContentLengthFraming().ReadAsync(r, 100, default)));
    }

    [Fact]
    public async Task ContentLengthDrainsAnOversizeBodyAndKeepsTheNextFrame()
    {
        var f = new ContentLengthFraming();
        var r = Reader([.. f.Encode(Encoding.UTF8.GetBytes("{\"id\":4,\"pad\":\"" + new string('x', 3000) + "\"}")), .. f.Encode(Encoding.UTF8.GetBytes("{}"))], 11);
        var big = await f.ReadAsync(r, 100, default);
        Assert.Equal(FrameStatus.Oversize, big.Status);
        Assert.Equal("{}", Text(await f.ReadAsync(r, 100, default)));
    }

    [Theory]
    [InlineData("Content-Type: x\r\n\r\n{}")]
    [InlineData("Content-Length: abc\r\n\r\n{}")]
    [InlineData("Content-Length: 50\r\n\r\n{}")]
    [InlineData("Content-Length: 2\r\n")]
    public async Task ContentLengthRejectsBrokenHeadersAndTruncatedBodies(string raw)
    {
        var r = Reader(Encoding.ASCII.GetBytes(raw));
        await Assert.ThrowsAsync<RpcFramingException>(async () => await new ContentLengthFraming().ReadAsync(r, 100, default));
    }
}

public class MessageTests
{
    [Fact]
    public void InvalidJsonIsAParseErrorAndABatchIsInvalid()
    {
        Assert.Equal(RpcErrorCodes.ParseError, Assert.Throws<RpcException>(() => RpcMessage.Parse(T.Json("{oops"))).Code);
        Assert.Equal(RpcErrorCodes.InvalidRequest, Assert.Throws<RpcException>(() => RpcMessage.Parse(T.Json("[{}]"))).Code);
    }

    [Fact]
    public void AnInvalidRequestKeepsItsIdForTheErrorReply()
    {
        var e = Assert.Throws<RpcException>(() => RpcMessage.Parse(T.Json("{\"jsonrpc\":\"2.0\",\"id\":7}")));
        Assert.Equal(RpcId.FromNumber(7), e.Id);
    }

    [Fact]
    public void NumericAndTextIdsAreDifferent()
    {
        using var n = RpcMessage.Parse(T.Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"m\"}"));
        using var s = RpcMessage.Parse(T.Json("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"m\"}"));
        Assert.NotEqual(n.Id, s.Id);
        Assert.Equal(RpcKind.Request, n.Kind);
    }

    [Fact]
    public void TheIdOfAMessageCutOffInTheMiddleCanStillBeRead()
    {
        var full = RpcWire.Request(RpcId.FromNumber(41), "x", T.Json("\"" + new string('a', 2000) + "\""));
        Assert.True(RpcMessage.TryPeekId(full.AsSpan(0, 100), out var id, out var hasMethod));
        Assert.Equal(RpcId.FromNumber(41), id);
        Assert.True(hasMethod);
        var response = RpcWire.Result(RpcId.FromNumber(9), T.Json("\"" + new string('a', 2000) + "\""), 10);
        Assert.True(RpcMessage.TryPeekId(response.AsSpan(0, 60), out id, out hasMethod));
        Assert.Equal(RpcId.FromNumber(9), id);
        Assert.False(hasMethod);
    }

    [Fact]
    public void RawParamsWithNewlinesStayOnOneLine()
    {
        var payload = RpcWire.Notification("m", T.Json("{\n \"a\": 1\n}"));
        Assert.DoesNotContain((byte)'\n', payload);
        new LineFraming().Encode(payload);
    }

    [Fact]
    public void EachAdapterRecognisesOnlyItsOwnCancelAndProgress()
    {
        var protocols = new[] { RpcProtocols.Lsp, RpcProtocols.Mcp, RpcProtocols.Acp, RpcProtocols.Plugin };
        foreach (var own in protocols)
        {
            using var doc = JsonDocument.Parse(own.CancelParams(RpcId.FromNumber(12)));
            foreach (var other in protocols)
            {
                var recognised = other.TryParseCancel(own.CancelMethod, doc.RootElement, out var id);
                // LSP and the plugin protocol differ in method name, MCP and ACP in method name too: no cross-recognition.
                Assert.Equal(other == own, recognised);
                if (recognised) Assert.Equal(RpcId.FromNumber(12), id);
            }
        }
        using var progress = JsonDocument.Parse("{\"token\":\"t1\",\"value\":{\"pct\":5}}");
        Assert.True(RpcProtocols.Lsp.TryParseProgress("$/progress", progress.RootElement, out var token, out var value));
        Assert.Equal("t1", token);
        Assert.Equal(5, value.GetProperty("pct").GetInt32());
        Assert.False(RpcProtocols.Mcp.TryParseProgress("$/progress", progress.RootElement, out _, out _));
        Assert.Equal(["$/cancelRequest", "notifications/cancelled", "$/cancel_request", "$/cancel"], protocols.Select(p => p.CancelMethod));
    }

    [Fact]
    public void OnlyMcpStaysSilentForACancelledRequestAndOnlyLspUsesContentLength()
    {
        Assert.False(RpcProtocols.Mcp.RespondsToCancelled);
        Assert.True(RpcProtocols.Lsp.RespondsToCancelled && RpcProtocols.Acp.RespondsToCancelled && RpcProtocols.Plugin.RespondsToCancelled);
        Assert.IsType<ContentLengthFraming>(RpcProtocols.Lsp.Framing);
        Assert.All(new[] { RpcProtocols.Mcp, RpcProtocols.Acp, RpcProtocols.Plugin }, p => Assert.IsType<LineFraming>(p.Framing));
    }
}

#pragma warning disable CS1591 // wire details of the protocols of the editor; not part of the API of the plugin SDK package
using System.Buffers;
using System.Globalization;
using System.Text;

namespace NksHub.Textida.Rpc;

public enum FrameStatus { Ok, Oversize, Eof }

/// <summary>
/// One framed message. <see cref="FrameStatus.Oversize"/> means the frame was read and thrown away; <paramref name="Prefix"/>
/// then holds its first bytes so the sender can still be told which request it belonged to.
/// </summary>
public readonly record struct Frame(byte[]? Payload, FrameStatus Status, byte[]? Prefix = null);

/// <summary>Buffered reader that hands out lines and exact byte counts without ever holding more than the caller allows.</summary>
public sealed class FrameReader(Stream stream)
{
    public const int PrefixBytes = 512;
    readonly byte[] _buf = new byte[16 * 1024];
    int _start, _end;

    async ValueTask<bool> FillAsync(CancellationToken ct)
    {
        _start = _end = 0;
        _end = await stream.ReadAsync(_buf, ct).ConfigureAwait(false);
        return _end > 0;
    }

    /// <summary>Reads up to '\n' (a trailing '\r' is removed). A line longer than <paramref name="max"/> is consumed and reported as oversize.</summary>
    public async ValueTask<Frame> ReadLineAsync(int max, CancellationToken ct)
    {
        var acc = new ArrayBufferWriter<byte>();
        var over = false;
        var any = false;
        var prefix = new byte[PrefixBytes];
        var prefixLen = 0;
        while (true)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
            {
                if (any) throw new RpcFramingException("The stream ended inside a frame.");
                return new Frame(null, FrameStatus.Eof);
            }
            any = true;
            var span = _buf.AsSpan(_start, _end - _start);
            var i = span.IndexOf((byte)'\n');
            var chunk = i < 0 ? span : span[..i];
            var take = Math.Min(PrefixBytes - prefixLen, chunk.Length);
            chunk[..take].CopyTo(prefix.AsSpan(prefixLen));
            prefixLen += take;
            if (!over)
            {
                if (acc.WrittenCount + (long)chunk.Length > (long)max + 1) { over = true; acc.Clear(); }
                else acc.Write(chunk);
            }
            _start += i < 0 ? span.Length : i + 1;
            if (i >= 0) break;
        }
        if (over) return new Frame(null, FrameStatus.Oversize, prefix[..prefixLen]);
        var line = acc.WrittenSpan;
        if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
        return line.Length > max ? new Frame(null, FrameStatus.Oversize, prefix[..prefixLen]) : new Frame(line.ToArray(), FrameStatus.Ok);
    }

    public async ValueTask<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var result = new byte[count];
        var got = 0;
        while (got < count)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                throw new RpcFramingException("The stream ended inside a frame body.");
            var n = Math.Min(count - got, _end - _start);
            _buf.AsSpan(_start, n).CopyTo(result.AsSpan(got));
            _start += n;
            got += n;
        }
        return result;
    }

    public async ValueTask DiscardAsync(long count, CancellationToken ct)
    {
        while (count > 0)
        {
            if (_start == _end && !await FillAsync(ct).ConfigureAwait(false))
                throw new RpcFramingException("The stream ended inside a frame body.");
            var n = (int)Math.Min(count, _end - _start);
            _start += n;
            count -= n;
        }
    }
}

/// <summary>Splits a byte stream into messages and back. Stateless; the reader carries the state.</summary>
public abstract class RpcFraming
{
    public abstract ValueTask<Frame> ReadAsync(FrameReader reader, int maxBytes, CancellationToken ct);
    public abstract byte[] Encode(ReadOnlyMemory<byte> payload);
}

/// <summary>One message per line (MCP stdio, ACP stdio, plugin protocol). Blank lines are ignored.</summary>
public sealed class LineFraming : RpcFraming
{
    public override async ValueTask<Frame> ReadAsync(FrameReader reader, int maxBytes, CancellationToken ct)
    {
        while (true)
        {
            var f = await reader.ReadLineAsync(maxBytes, ct).ConfigureAwait(false);
            if (f.Status != FrameStatus.Ok || f.Payload!.Length > 0) return f;
        }
    }

    public override byte[] Encode(ReadOnlyMemory<byte> payload)
    {
        if (payload.Span.Contains((byte)'\n')) throw new ArgumentException("A line-framed payload must not contain a newline.");
        var framed = new byte[payload.Length + 1];
        payload.CopyTo(framed);
        framed[^1] = (byte)'\n';
        return framed;
    }
}

/// <summary>LSP framing: <c>Content-Length: N</c> counted in bytes, a blank line, then N bytes.</summary>
public sealed class ContentLengthFraming : RpcFraming
{
    const int MaxHeaderLine = 4096;

    public override async ValueTask<Frame> ReadAsync(FrameReader reader, int maxBytes, CancellationToken ct)
    {
        long length = -1;
        var sawHeader = false;
        while (true)
        {
            var h = await reader.ReadLineAsync(MaxHeaderLine, ct).ConfigureAwait(false);
            if (h.Status == FrameStatus.Eof)
            {
                if (sawHeader) throw new RpcFramingException("The stream ended inside a frame header.");
                return h;
            }
            if (h.Status == FrameStatus.Oversize) throw new RpcFramingException("A frame header line is too long.");
            if (h.Payload!.Length == 0 && sawHeader) break;
            if (h.Payload.Length == 0) continue;
            sawHeader = true;
            var text = Encoding.ASCII.GetString(h.Payload);
            var colon = text.IndexOf(':');
            if (colon > 0 && text[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(text[(colon + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length))
                    throw new RpcFramingException("Invalid Content-Length value.");
            }
        }
        if (length < 0) throw new RpcFramingException("The frame has no Content-Length header.");
        if (length > maxBytes)
        {
            var head = (int)Math.Min(length, FrameReader.PrefixBytes);
            var prefix = await reader.ReadExactAsync(head, ct).ConfigureAwait(false);
            await reader.DiscardAsync(length - head, ct).ConfigureAwait(false);
            return new Frame(null, FrameStatus.Oversize, prefix);
        }
        return new Frame(await reader.ReadExactAsync((int)length, ct).ConfigureAwait(false), FrameStatus.Ok);
    }

    public override byte[] Encode(ReadOnlyMemory<byte> payload)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");
        var framed = new byte[header.Length + payload.Length];
        header.CopyTo(framed, 0);
        payload.CopyTo(framed.AsMemory(header.Length));
        return framed;
    }
}

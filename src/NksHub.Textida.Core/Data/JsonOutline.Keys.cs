using System.Buffers;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Data;

public sealed partial class JsonOutline
{
    /// <summary>The path to the value on the line, as <c>$.key[3]["odd key"]</c>; null for source text after an error.</summary>
    public string? PathOf(JsonLine line)
    {
        if (line.Kind == JsonLineKind.Raw) return null;
        var parts = new List<string>();
        JsonFrame? chain;
        if (line.Kind is JsonLineKind.Open or JsonLineKind.Close or JsonLineKind.Comment) chain = line.Frame; // the frame of an opening line is the container it opens
        else
        {
            if (line.HasKey) parts.Add(KeyPart(ReadKey(_source, line.KeyStart, line.KeyEnd)));
            else if (line.Index >= 0) parts.Add($"[{line.Index}]");
            chain = line.Frame;
        }
        for (var f = chain; f is not null; f = f.Parent)
        {
            if (f.Key is not null) parts.Add(KeyPart(f.Key));
            else if (f.Index >= 0) parts.Add($"[{f.Index}]");
        }
        parts.Add("$");
        parts.Reverse();
        return string.Concat(parts);
    }

    private static string KeyPart(string key)
    {
        bool plain = key.Length > 0 && !char.IsDigit(key[0]);
        foreach (char ch in key)
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or '$')) { plain = false; break; }
        return plain ? "." + key : "[\"" + JsonEncodedText.Encode(key, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"]";
    }

    /// <summary>The decoded text of the key token at the range, at most <see cref="KeyChars"/> characters.</summary>
    public string KeyText(JsonLine line) => line.HasKey ? ReadKey(_source, line.KeyStart, line.KeyEnd) : "";

    private string ReadKey(IByteSnapshot source, long start, long end)
    {
        int length = (int)Math.Min(end - start, KeyChars * 4L);
        var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
        try
        {
            int n = source.Read(start, bytes.AsSpan(0, length));
            Interlocked.Add(ref _bytesRead, n);
            return Unquote(_codec.GetString(bytes.AsSpan(0, n)), whole: n == end - start);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    private static string Unquote(string token, bool whole)
    {
        if (whole && token.Contains('\\'))
        {
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(token));
                if (reader.Read() && reader.TokenType == JsonTokenType.String) return Cap(reader.GetString() ?? "");
            }
            catch (JsonException) { }
        }
        string inner = token.Length > 0 && token[0] == '"' ? token[1..] : token;
        if (whole && inner.EndsWith('"')) inner = inner[..^1];
        return Cap(inner);
    }

    private static string Cap(string text)
    {
        if (text.Length <= KeyChars) return text;
        int length = KeyChars;
        if (char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length])) length--;
        return text[..length];
    }

    /// <summary>
    /// The first line after <paramref name="afterLine"/> whose key is <paramref name="key"/> (decoded, exact), wrapping around to the start; -1 when no
    /// key matches. It scans on its own from the noted state before the line to the end, so it does not wait for the background scan. Safe on any thread.
    /// </summary>
    public long FindKey(string key, long afterLine, CancellationToken ct = default)
    {
        ScanState start, origin;
        IByteSnapshot source;
        long firstNumber;
        long from = Math.Max(0, afterLine + 1);
        lock (_gate)
        {
            int segment = (int)Math.Min(_points.Count - 1, from / SegmentLines);
            start = _points[segment];
            origin = _points[0];
            source = _source;
            firstNumber = (long)segment * SegmentLines;
        }
        var plain = _codec.EncodeLossy(key);
        long found = ScanKeys(source, start, firstNumber, from, long.MaxValue, key, plain, ct);
        if (found >= 0) return found;
        return ScanKeys(source, origin, 0, 0, Math.Max(0, afterLine + 1), key, plain, ct);
    }

    private long ScanKeys(IByteSnapshot source, ScanState state, long number, long from, long until, string key, byte[] plain, CancellationToken ct)
    {
        var cursor = new Cursor(source, _codec, null, _comments);
        var buffer = new byte[plain.Length + 2];
        for (; number < until; number++)
        {
            if ((number & 1023) == 0) ct.ThrowIfCancellationRequested();
            if (!Next(ref state, cursor, number, out var line)) return -1;
            if (number < from || !line.HasKey || line.KeyAbove) continue;
            long length = line.KeyEnd - line.KeyStart;
            if (length == plain.Length + 2)
            {
                source.Read(line.KeyStart, buffer);
                if (buffer.AsSpan(1, plain.Length).SequenceEqual(plain)) return number;
            }
            else if (length > plain.Length + 2 && length <= KeyChars * 6L && ReadKey(source, line.KeyStart, line.KeyEnd) == key) return number;
        }
        return -1;
    }
}

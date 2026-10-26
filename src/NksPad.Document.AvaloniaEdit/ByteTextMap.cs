using System.Text;
using NksPad.Core.Documents;

namespace NksPad.AvaloniaEditAdapter;

/// <summary>
/// Maps UTF-16 offsets of a text view to byte offsets of the document bytes. The text is a sequence of runs: an original run
/// points into the untouched file bytes (so they are never re-encoded), an edited run owns the bytes of one edit. Inside an
/// original run the map walks from a checkpoint kept every <see cref="CheckpointChars"/> chars.
/// </summary>
internal sealed class ByteTextMap
{
    public const int CheckpointChars = 1024;

    internal sealed class Run
    {
        public bool Original;
        public int OrigByte, OrigChar;
        public int Bytes, Chars;
        /// <summary>Bytes of an edited run.</summary>
        public byte[]? Data;
        /// <summary>For an edited run made from typed text: the chars the view shows (lone surrogates are encoded as replacement chars).</summary>
        public string? Text;
    }

    private readonly byte[] _orig;
    private readonly ByteCodec _codec;
    private List<(int Char, int Byte)>? _checkpoints;
    private List<Run> _runs = [];

    public ByteTextMap(byte[] original, ByteCodec codec, int chars)
    {
        _orig = original;
        _codec = codec;
        if (original.Length > 0) _runs.Add(new Run { Original = true, Bytes = original.Length, Chars = chars });
    }

    public long TotalBytes => _runs.Sum(r => (long)r.Bytes);

    private List<(int Char, int Byte)> Checkpoints()
    {
        if (_checkpoints is not null) return _checkpoints;
        var list = new List<(int, int)> { (0, 0) };
        int at = 0, chars = 0, next = CheckpointChars;
        while (at < _orig.Length)
        {
            at += _codec.ReadUnit(_orig.AsSpan(at), out int c);
            chars += c;
            if (chars >= next)
            {
                list.Add((chars, at));
                next = chars + CheckpointChars;
            }
        }
        return _checkpoints = list;
    }

    public ReadOnlySpan<byte> BytesOf(Run r) => r.Original ? _orig.AsSpan(r.OrigByte, r.Bytes) : r.Data;

    /// <summary>Units (chars, bytes) of a run from its local char/byte start, optionally jumping over whole checkpoints.</summary>
    private IEnumerable<(int Chars, int Bytes)> Units(Run r, int skipChars, int skipBytes)
    {
        if (r.Text is { } text)
        {
            for (int i = 0; i < text.Length;)
            {
                int n = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                yield return (n, _codec.Encoding.GetByteCount(text.AsSpan(i, n)));
                i += n;
            }
            yield break;
        }
        var data = r.Original ? _orig.AsMemory(r.OrigByte, r.Bytes) : r.Data.AsMemory();
        for (int at = skipBytes; at < data.Length;)
        {
            int len = _codec.ReadUnit(data.Span[at..], out int c);
            yield return (c, len);
            at += len;
        }
    }

    /// <summary>Local (chars, bytes) position to start walking from for a local char target.</summary>
    private (int Chars, int Bytes) StartAtChar(Run r, int localChars)
    {
        if (!r.Original) return (0, 0);
        var cps = Checkpoints();
        int global = r.OrigChar + localChars;
        int lo = 0, hi = cps.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (cps[mid].Char <= global) lo = mid; else hi = mid - 1;
        }
        var cp = cps[lo];
        return cp.Char <= r.OrigChar ? (0, 0) : (cp.Char - r.OrigChar, cp.Byte - r.OrigByte);
    }

    private (int Chars, int Bytes) StartAtByte(Run r, int localBytes)
    {
        if (!r.Original) return (0, 0);
        var cps = Checkpoints();
        int global = r.OrigByte + localBytes;
        int lo = 0, hi = cps.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (cps[mid].Byte <= global) lo = mid; else hi = mid - 1;
        }
        var cp = cps[lo];
        return cp.Byte <= r.OrigByte ? (0, 0) : (cp.Char - r.OrigChar, cp.Byte - r.OrigByte);
    }

    private (int Bytes, bool SplitsPair) LocalCharsToBytes(Run r, int k)
    {
        if (k == 0) return (0, false);
        if (k == r.Chars) return (r.Bytes, false);
        var (chars, bytes) = StartAtChar(r, k);
        foreach (var (c, b) in Units(r, chars, bytes))
        {
            if (chars + c > k) return (bytes, true);
            chars += c;
            bytes += b;
            if (chars == k) return (bytes, false);
        }
        throw new ArgumentOutOfRangeException(nameof(k));
    }

    private int LocalBytesToChars(Run r, int b)
    {
        if (b == 0) return 0;
        if (b == r.Bytes) return r.Chars;
        var (chars, bytes) = StartAtByte(r, b);
        foreach (var (c, n) in Units(r, chars, bytes))
        {
            chars += c;
            bytes += n;
            if (bytes == b) return chars;
            if (bytes > b) break;
        }
        throw new ArgumentException("The byte offset is not on a character boundary.");
    }

    /// <summary>Byte offset (after the BOM) of a char offset; when it falls inside a surrogate pair, the start of that pair.</summary>
    public long CharToByte(int c, out bool splitsPair)
    {
        splitsPair = false;
        long bytes = 0;
        int chars = 0;
        foreach (var r in _runs)
        {
            if (c <= chars + r.Chars)
            {
                var (b, split) = LocalCharsToBytes(r, c - chars);
                splitsPair = split;
                return bytes + b;
            }
            chars += r.Chars;
            bytes += r.Bytes;
        }
        if (c == chars) return bytes;
        throw new ArgumentOutOfRangeException(nameof(c));
    }

    public int ByteToChar(long b)
    {
        long bytes = 0;
        int chars = 0;
        foreach (var r in _runs)
        {
            if (b <= bytes + r.Bytes) return chars + LocalBytesToChars(r, (int)(b - bytes));
            chars += r.Chars;
            bytes += r.Bytes;
        }
        if (b == bytes) return chars;
        throw new ArgumentOutOfRangeException(nameof(b));
    }

    /// <summary>Index of the run that starts exactly at char <paramref name="c"/>, splitting a run when needed.</summary>
    private int SplitAt(int c)
    {
        int chars = 0;
        for (int i = 0; i < _runs.Count; i++)
        {
            var r = _runs[i];
            if (c == chars) return i;
            if (c < chars + r.Chars)
            {
                int k = c - chars;
                var (b, _) = LocalCharsToBytes(r, k);
                Run left, right;
                if (r.Original)
                {
                    left = new Run { Original = true, OrigByte = r.OrigByte, OrigChar = r.OrigChar, Bytes = b, Chars = k };
                    right = new Run { Original = true, OrigByte = r.OrigByte + b, OrigChar = r.OrigChar + k, Bytes = r.Bytes - b, Chars = r.Chars - k };
                }
                else
                {
                    left = new Run { Data = r.Data!.AsSpan(0, b).ToArray(), Bytes = b, Chars = k, Text = r.Text?[..k] };
                    right = new Run { Data = r.Data.AsSpan(b).ToArray(), Bytes = r.Bytes - b, Chars = r.Chars - k, Text = r.Text?[k..] };
                }
                _runs[i] = left;
                _runs.Insert(i + 1, right);
                return i + 1;
            }
            chars += r.Chars;
        }
        if (c == chars) return _runs.Count;
        throw new ArgumentOutOfRangeException(nameof(c));
    }

    /// <summary>Replaces chars <c>[c0, c1)</c> (both unit boundaries) and returns the bytes that were removed.</summary>
    public byte[] Replace(int c0, int c1, byte[] bytes, string? text, int chars)
    {
        int i0 = SplitAt(c0);
        int i1 = SplitAt(c1);
        var removed = new List<byte>();
        for (int i = i0; i < i1; i++) removed.AddRange(BytesOf(_runs[i]).ToArray());
        _runs.RemoveRange(i0, i1 - i0);
        if (bytes.Length > 0 || chars > 0) _runs.Insert(i0, new Run { Data = bytes, Bytes = bytes.Length, Chars = chars, Text = text });
        return [.. removed];
    }

    public int Read(long offset, Span<byte> destination)
    {
        long at = 0;
        int done = 0;
        foreach (var r in _runs)
        {
            if (done == destination.Length) break;
            if (offset + done >= at + r.Bytes) { at += r.Bytes; continue; }
            int skip = (int)(offset + done - at);
            var src = BytesOf(r)[skip..];
            int n = Math.Min(src.Length, destination.Length - done);
            src[..n].CopyTo(destination[done..]);
            done += n;
            at += r.Bytes;
        }
        return done;
    }

    public void CopyTo(Stream stream)
    {
        foreach (var r in _runs) stream.Write(BytesOf(r));
    }
}

using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.AvaloniaEditAdapter;

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
    private readonly TextCodec _codec;
    private List<(int Char, int Byte)>? _checkpoints;
    private List<Run> _runs = [];

    public ByteTextMap(byte[] original, TextCodec codec, int chars)
    {
        _orig = original;
        _codec = codec;
        if (original.Length > 0) _runs.Add(new Run { Original = true, Bytes = original.Length, Chars = chars });
        _totalBytes = original.Length;
    }

    // Start of every run in bytes and chars. Entries below _valid are known; a change at run i keeps the entries up to i, and lookups
    // extend the known part forward only as far as they need, so a batch of edits from the end of the text to its start stays cheap.
    private long[] _byteStart = new long[8];
    private int[] _charStart = new int[8];
    private int _valid = 1;

    private int ExtendedChar(int entry)
    {
        ExtendTo(entry);
        return _charStart[entry];
    }

    private void Invalidate(int fromRun) => _valid = Math.Min(_valid, fromRun + 1);

    private void ExtendTo(int entry)
    {
        int n = _runs.Count;
        entry = Math.Min(entry, n);
        if (_byteStart.Length < n + 1) { Array.Resize(ref _byteStart, n + 1 + n / 2); Array.Resize(ref _charStart, n + 1 + n / 2); }
        for (; _valid <= entry; _valid++)
        {
            _byteStart[_valid] = _byteStart[_valid - 1] + _runs[_valid - 1].Bytes;
            _charStart[_valid] = _charStart[_valid - 1] + _runs[_valid - 1].Chars;
        }
    }

    /// <summary>First run whose end is at or after <paramref name="target"/> (chars or bytes); the run count when there is none. The entries up to the answer and the one after it are known afterwards.</summary>
    private int Locate(bool chars, long target)
    {
        int n = _runs.Count;
        if (n == 0 || target <= 0) return 0;
        long Start(int i) => chars ? _charStart[i] : _byteStart[i];
        while (_valid <= n && Start(_valid - 1) < target) ExtendTo(_valid);
        int lo = 0, hi = Math.Min(_valid - 1, n); // either Start(hi) >= target, or hi == n and the target is past the end
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (Start(mid + 1) >= target) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    private long _totalBytes;

    public long TotalBytes => _totalBytes;

    private int RunEndingAtOrAfterChar(int c) => Locate(true, c);

    private int RunEndingAtOrAfterByte(long b) => Locate(false, b);

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
                yield return (n, _codec.LossyByteCount(text.AsSpan(i, n)));
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
        if (chars == k) return (bytes, false);
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
        if (bytes == b) return chars;
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
        int i = RunEndingAtOrAfterChar(c);
        if (i == _runs.Count)
        {
            if (c == _charStart[i]) return _byteStart[i];
            throw new ArgumentOutOfRangeException(nameof(c));
        }
        var (b, split) = LocalCharsToBytes(_runs[i], c - _charStart[i]);
        splitsPair = split;
        return _byteStart[i] + b;
    }

    /// <summary>Byte offsets (after the BOM) of char offsets.</summary>
    public long[] CharsToBytes(ReadOnlySpan<int> chars)
    {
        var result = new long[chars.Length];
        for (int q = 0; q < chars.Length; q++) result[q] = CharToByte(chars[q], out _);
        return result;
    }

    public int ByteToChar(long b)
    {
        int i = RunEndingAtOrAfterByte(b);
        if (i == _runs.Count)
        {
            if (b == _byteStart[i]) return _charStart[i];
            throw new ArgumentOutOfRangeException(nameof(b));
        }
        return _charStart[i] + LocalBytesToChars(_runs[i], (int)(b - _byteStart[i]));
    }

    /// <summary>Index of the run that starts exactly at char <paramref name="c"/>, splitting a run when needed.</summary>
    private int SplitAt(int c)
    {
        int i = RunEndingAtOrAfterChar(c);
        // a run that ends exactly at c is followed by the one that starts there
        while (i < _runs.Count && ExtendedChar(i + 1) == c) i++;
        if (i == _runs.Count)
        {
            if (c == _charStart[i]) return i;
            throw new ArgumentOutOfRangeException(nameof(c));
        }
        if (_charStart[i] == c) return i;
        var r = _runs[i];
        int k = c - _charStart[i];
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
        Invalidate(i);
        return i + 1;
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
        _totalBytes += bytes.Length - removed.Count;
        Invalidate(i0);
        return [.. removed];
    }

    /// <summary>Per char of the view: true when it belongs to typed text (an edited run made from text), false when it comes from the original bytes.</summary>
    public IEnumerable<(int Start, string Text)> TypedRuns()
    {
        int chars = 0;
        foreach (var r in _runs)
        {
            if (r.Text is { } text) yield return (chars, text);
            chars += r.Chars;
        }
    }

    public int Read(long offset, Span<byte> destination)
    {
        int done = 0;
        for (int i = RunEndingAtOrAfterByte(offset + 1); i < _runs.Count && done < destination.Length; i++)
        {
            var r = _runs[i];
            ExtendTo(i);
            long at = _byteStart[i];
            if (offset + done >= at + r.Bytes) continue;
            var src = BytesOf(r)[(int)(offset + done - at)..];
            int n = Math.Min(src.Length, destination.Length - done);
            src[..n].CopyTo(destination[done..]);
            done += n;
        }
        return done;
    }

    public void CopyTo(Stream stream)
    {
        foreach (var r in _runs) stream.Write(BytesOf(r));
    }
}

using System.Text;
using NksPad.Core.Documents;
using NksPad.Core.Encodings;

namespace NksPad.Document.Contract;

/// <summary>Document formats used by the contract data; the BOM is part of the expected bytes.</summary>
public sealed record TestFormat(string Name, TextCodec Format, Encoding Enc)
{
    private static readonly bool Registered = Register();

    private static bool Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    // The framework encodings are the independent oracle for the codec under test.
    public static TestFormat Utf8 { get; } = new("utf8", TextCodec.Utf8, new UTF8Encoding(false));
    public static TestFormat Utf8Bom { get; } = new("utf8-bom", TextCodec.Utf8Bom, new UTF8Encoding(false));
    public static TestFormat Cp1250 { get; } = new("cp1250", TextCodec.Cp1250, Encoding.GetEncoding(1250));
    public static TestFormat Utf16LE { get; } = new("utf16le", TextCodec.Utf16LEBom, new UnicodeEncoding(false, false));
    public static TestFormat Utf16BE { get; } = new("utf16be", TextCodec.Utf16BEBom, new UnicodeEncoding(true, false));
    public static TestFormat Utf32LE { get; } = new("utf32le", TextCodec.Utf32LEBom, new UTF32Encoding(false, false));
    public static TestFormat Utf32BE { get; } = new("utf32be", TextCodec.Utf32BEBom, new UTF32Encoding(true, false));
    /// <summary>The formats every backend must pass the contract with.</summary>
    public static IReadOnlyList<TestFormat> All { get; } = [Utf8, Utf8Bom, Cp1250, Utf16LE, Utf16BE];

    public byte[] Bom => Format.Bom;
    public bool IsUtf16 => Format.Kind == CodecKind.Utf16;
    public bool IsWide => Format.Kind is CodecKind.Utf16 or CodecKind.Utf32;

    /// <summary>BOM plus the text encoded without replacement surprises (text must be representable).</summary>
    public byte[] Bytes(string text) => [.. Bom, .. Enc.GetBytes(text)];
    public byte[] Raw(string text) => Enc.GetBytes(text);
    public string Text(ReadOnlySpan<byte> bytes) => Enc.GetString(bytes[Bom.Length..]);
    public override string ToString() => Name;
}

/// <summary>Plain <c>byte[]</c> backend; the reference every other backend is compared with. Positions are computed naively from the unit walk.</summary>
public sealed class ReferenceBackend : IByteBackend
{
    private TextCodec _codec;
    private int _bom;
    private byte[] _bytes;

    public ReferenceBackend(byte[] bytes, TextCodec codec)
    {
        _bytes = bytes;
        _codec = codec;
        _bom = codec.BomLengthIn(bytes);
    }

    public bool Supports(TextCodec codec) => true;

    public void Reinterpret(TextCodec codec)
    {
        _codec = codec;
        _bom = codec.BomLengthIn(_bytes);
    }

    public BackendCapabilities Capabilities => ReferenceBackendFactory.Caps;
    public TextCodec Codec => _codec;
    public long ByteLength => _bytes.Length;
    public string? BackingPath => null;
#pragma warning disable CS0067
    public event Action<BackendEdit>? EditedByBackend;
#pragma warning restore CS0067

    public int Read(long offset, Span<byte> destination)
    {
        int n = (int)Math.Min(destination.Length, _bytes.Length - offset);
        _bytes.AsSpan((int)offset, n).CopyTo(destination);
        return n;
    }

    public void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert)
    {
        var next = new byte[_bytes.Length - deleteLength + insert.Length];
        _bytes.AsSpan(0, (int)offset).CopyTo(next);
        insert.CopyTo(next.AsSpan((int)offset));
        _bytes.AsSpan((int)(offset + deleteLength)).CopyTo(next.AsSpan((int)offset + insert.Length));
        _bytes = next;
    }

    public string ReadText(ByteRange range) => _codec.GetString(_bytes.AsSpan((int)range.Start, (int)range.Length));
    public IByteSnapshot CreateSnapshot() => new ArraySnapshot((byte[])_bytes.Clone());
    public void CopyTo(Stream destination) => destination.Write(_bytes);
    public void ReplaceBackingFile(string tempPath, string targetPath) => FileSwap.Replace(tempPath, targetPath);
    public void Dispose() { }

    /// <summary>Starts of all units with the line and column they have; the end of the text closes the list.</summary>
    private List<(long Offset, long Line, long Column, bool InsideCrLf)> Walk()
    {
        var result = new List<(long, long, long, bool)>();
        long off = _bom, line = 0, col = 0;
        while (off < _bytes.Length)
        {
            int len = _codec.ReadUnit(_bytes.AsSpan((int)off), out int chars);
            string unit = _codec.GetString(_bytes.AsSpan((int)off, len));
            bool crlf = unit == "\r" && off + len < _bytes.Length && _codec.GetString(_bytes.AsSpan((int)(off + len), _codec.ReadUnit(_bytes.AsSpan((int)(off + len)), out _))) == "\n";
            result.Add((off, line, col, false));
            if (crlf)
            {
                int lf = _codec.ReadUnit(_bytes.AsSpan((int)(off + len)), out _);
                result.Add((off + len, line, col + 1, true));
                off += len + lf;
                line++;
                col = 0;
                continue;
            }
            off += len;
            if (unit is "\n" or "\r") { line++; col = 0; }
            else col += chars;
        }
        result.Add((off, line, col, false));
        return result;
    }

    public (long Lines, bool Complete) LineIndexState => (Walk()[^1].Line + 1, true);

    public bool TryGetPosition(long offset, out LinePosition position)
    {
        position = default;
        foreach (var (o, line, col, inside) in Walk())
            if (o == offset && !inside)
            {
                position = new(line, col);
                return true;
            }
        return false;
    }

    public bool TryGetOffset(LinePosition position, out long offset)
    {
        offset = 0;
        var walk = Walk();
        for (int i = 0; i < walk.Count; i++)
        {
            var (o, line, col, inside) = walk[i];
            if (inside || line != position.Line || col != position.Column) continue;
            offset = o;
            return true;
        }
        return false;
    }
}

public sealed class ReferenceBackendFactory : IBackendFactory
{
    internal static readonly BackendCapabilities Caps = new("reference", int.MaxValue, SupportsUtf16: true, PositionsMayBeUnknown: false, MaterializesText: true);
    public static ReferenceBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => Caps;
    public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec) => new ReferenceBackend(bytes.ToArray(), codec);
}

/// <summary>Naive UTF-8 model of a document: a byte list and positions computed straight from <see cref="Encoding.UTF8"/>.</summary>
public sealed class NaiveModel(byte[] initial)
{
    private readonly List<byte[]> _undo = [];
    private readonly List<byte[]> _redo = [];
    public byte[] Bytes { get; private set; } = initial;
    public int Bom => Bytes.AsSpan().StartsWith("﻿"u8) ? 3 : 0;

    /// <summary>The bytes after applying disjoint ascending edits, without changing the model.</summary>
    public byte[] Preview(IReadOnlyList<(int Start, int Deleted, byte[] Insert)> edits)
    {
        var result = new List<byte>();
        int at = 0;
        foreach (var (start, deleted, insert) in edits)
        {
            result.AddRange(Bytes.AsSpan(at, start - at));
            result.AddRange(insert);
            at = start + deleted;
        }
        result.AddRange(Bytes.AsSpan(at));
        return [.. result];
    }

    public void Apply(IReadOnlyList<(int Start, int Deleted, byte[] Insert)> edits)
    {
        var next = Preview(edits);
        _undo.Add(Bytes);
        _redo.Clear();
        Bytes = next;
    }

    public static HashSet<int> BoundariesOf(byte[] bytes) => new NaiveModel(bytes).Boundaries();

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(Bytes);
        Bytes = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(Bytes);
        Bytes = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        return true;
    }

    /// <summary>Boundaries found by decoding the whole document from the start with the framework's rune decoder.</summary>
    public HashSet<int> Boundaries()
    {
        var set = new HashSet<int>();
        int i = Bom;
        while (i < Bytes.Length)
        {
            set.Add(i);
            Rune.DecodeFromUtf8(Bytes.AsSpan(i), out _, out int used);
            i += Math.Max(1, used);
        }
        set.Add(Bytes.Length);
        return set;
    }

    public List<int> LineStarts()
    {
        var starts = new List<int> { Bom };
        for (int i = Bom; i < Bytes.Length; i++)
            if (Bytes[i] == '\n' || (Bytes[i] == '\r' && (i + 1 == Bytes.Length || Bytes[i + 1] != '\n'))) starts.Add(i + 1);
        return starts;
    }

    public bool TryPosition(int offset, out LinePosition position)
    {
        position = default;
        if (!Boundaries().Contains(offset) || offset < Bom) return false;
        if (offset > Bom && offset < Bytes.Length && Bytes[offset - 1] == '\r' && Bytes[offset] == '\n') return false;
        var starts = LineStarts();
        int line = starts.FindLastIndex(s => s <= offset);
        position = new(line, Encoding.UTF8.GetString(Bytes, starts[line], offset - starts[line]).Length);
        return true;
    }
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nkspad-doc-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

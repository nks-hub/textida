using AvaloniaEdit.Document;
using NksPad.Core;
using NksPad.Core.Documents;

namespace NksPad.AvaloniaEditAdapter;

/// <summary>Creates <see cref="AvaloniaEditBackend"/> instances.</summary>
public sealed class AvaloniaEditBackendFactory : IBackendFactory
{
    internal static readonly BackendCapabilities Caps = new("avaloniaedit", int.MaxValue, SupportsUtf16: true, PositionsMayBeUnknown: false, MaterializesText: true);
    public static AvaloniaEditBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => Caps;
    public IByteBackend Create(ReadOnlyMemory<byte> bytes, DetectedEncoding format) => new AvaloniaEditBackend(bytes.ToArray(), format);
}

/// <summary>
/// <see cref="IByteBackend"/> over an AvaloniaEdit <see cref="TextDocument"/>. The original bytes stay authoritative: the
/// text control shows a decoded view, and every offset converts to bytes through <see cref="ByteTextMap"/>. Untouched ranges
/// are never re-encoded, so invalid sequences, mixed line endings and the BOM survive; typing re-encodes only the typed text.
/// Edits made on <see cref="TextDocument"/> (typing) raise <see cref="EditedByBackend"/>, edits made through
/// <see cref="Replace"/> do not. The undo stack of the text document is disabled; undo belongs to <see cref="Document"/>.
/// </summary>
/// <remarks>
/// Known limit: text and bytes agree unit by unit. A typed deletion that joins two invalid fragments into one valid character
/// keeps showing two replacement characters until the file is reopened; the bytes are right in every case.
/// </remarks>
public sealed class AvaloniaEditBackend : IByteBackend
{
    private readonly ByteCodec _codec;
    private readonly byte[] _bom;
    private readonly ByteTextMap _map;
    private bool _applying;

    public AvaloniaEditBackend(byte[] bytes, DetectedEncoding format)
    {
        _codec = new ByteCodec(format);
        Format = format;
        _bom = bytes.AsSpan(0, Math.Min(format.BomLength, bytes.Length)).ToArray();
        var body = bytes.AsSpan(_bom.Length).ToArray();
        string text = _codec.Decode(body);
        _map = new ByteTextMap(body, _codec, text.Length);
        TextDocument = new TextDocument(text);
        TextDocument.UndoStack.SizeLimit = 0;
        TextDocument.Changed += OnTextChanged;
    }

    /// <summary>The document the text control binds to.</summary>
    public TextDocument TextDocument { get; }

    public BackendCapabilities Capabilities => AvaloniaEditBackendFactory.Caps;
    public DetectedEncoding Format { get; }
    public long ByteLength => _bom.Length + _map.TotalBytes;
    public string? BackingPath => null;
    public event Action<BackendEdit>? EditedByBackend;

    public int Read(long offset, Span<byte> destination)
    {
        int done = 0;
        if (offset < _bom.Length)
        {
            done = (int)Math.Min(destination.Length, _bom.Length - offset);
            _bom.AsSpan((int)offset, done).CopyTo(destination);
        }
        if (done < destination.Length) done += _map.Read(Math.Max(0, offset - _bom.Length), destination[done..]);
        return done;
    }

    public void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert)
    {
        int c0 = _map.ByteToChar(offset - _bom.Length);
        int c1 = _map.ByteToChar(offset + deleteLength - _bom.Length);
        var bytes = insert.ToArray();
        string text = _codec.Decode(bytes);
        _applying = true;
        try { TextDocument.Replace(c0, c1 - c0, text); }
        finally { _applying = false; }
        _map.Replace(c0, c1, bytes, null, _codec.CountChars(bytes));
    }

    private void OnTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_applying) return;
        int c0 = e.Offset, c1 = e.Offset + e.RemovalLength;
        long b0 = _map.CharToByte(c0, out bool splitStart);
        _map.CharToByte(c1, out bool splitEnd);
        string prefix = splitStart ? TextDocument.GetText(c0 - 1, 1) : "";
        string suffix = splitEnd ? TextDocument.GetText(c0 + e.InsertionLength, 1) : "";
        if (splitStart) c0--;
        if (splitEnd) c1++;
        string text = prefix + e.InsertedText.Text + suffix;
        var bytes = _codec.Encode(text);
        var removed = _map.Replace(c0, c1, bytes, text, text.Length);
        EditedByBackend?.Invoke(new BackendEdit(_bom.Length + b0, removed, bytes));
    }

    public string ReadText(ByteRange range)
    {
        int c0 = _map.ByteToChar(range.Start - _bom.Length);
        int c1 = _map.ByteToChar(range.End - _bom.Length);
        return TextDocument.GetText(c0, c1 - c0);
    }

    public (long Lines, bool Complete) LineIndexState => (TextDocument.LineCount, true);

    public bool TryGetPosition(long offset, out LinePosition position)
    {
        position = default;
        if (offset < _bom.Length || offset > ByteLength) return false;
        int c;
        try { c = _map.ByteToChar(offset - _bom.Length); }
        catch (ArgumentException) { return false; }
        var line = TextDocument.GetLineByOffset(c);
        position = new(line.LineNumber - 1, c - line.Offset);
        return true;
    }

    public bool TryGetOffset(LinePosition position, out long offset)
    {
        offset = 0;
        if (position.Line < 0 || position.Column < 0 || position.Line >= TextDocument.LineCount) return false;
        var line = TextDocument.GetLineByNumber((int)position.Line + 1);
        if (position.Column > line.Length) return false;
        int c = line.Offset + (int)position.Column;
        if (position.Column > 0 && position.Column < line.Length && char.IsHighSurrogate(TextDocument.GetCharAt(c - 1)) && char.IsLowSurrogate(TextDocument.GetCharAt(c))) return false;
        offset = _bom.Length + _map.CharToByte(c, out _);
        return true;
    }

    public IByteSnapshot CreateSnapshot()
    {
        using var ms = new MemoryStream(checked((int)ByteLength));
        CopyTo(ms);
        return new ArraySnapshot(ms.ToArray());
    }

    public void CopyTo(Stream destination)
    {
        destination.Write(_bom);
        _map.CopyTo(destination);
    }

    public void ReplaceBackingFile(string tempPath, string targetPath) => FileSwap.Replace(tempPath, targetPath);

    public void Dispose() => TextDocument.Changed -= OnTextChanged;
}

using AvaloniaEdit.Document;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Creates <see cref="AvaloniaEditBackend"/> instances.</summary>
public sealed class AvaloniaEditBackendFactory : IBackendFactory
{
    internal static readonly BackendCapabilities Caps = new("avaloniaedit", int.MaxValue, SupportsUtf16: true, PositionsMayBeUnknown: false, MaterializesText: true);
    public static AvaloniaEditBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => Caps;
    public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec) => new AvaloniaEditBackend(bytes.ToArray(), codec);
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
    private TextCodec _codec;
    private byte[] _bom = [];
    private ByteTextMap _map = null!;
    private bool _applying;

    public AvaloniaEditBackend(byte[] bytes, TextCodec codec)
    {
        _codec = codec;
        string text = Load(bytes);
        TextDocument = new TextDocument(text);
        TextDocument.UndoStack.SizeLimit = 0;
        TextDocument.Changed += OnTextChanged;
    }

    /// <summary>Splits off the BOM, decodes the rest and builds the byte map; returns the text to show.</summary>
    private string Load(byte[] bytes)
    {
        _bom = bytes.AsSpan(0, _codec.BomLengthIn(bytes)).ToArray();
        var body = bytes.AsSpan(_bom.Length).ToArray();
        string text = _codec.GetString(body);
        _map = new ByteTextMap(body, _codec, text.Length);
        return text;
    }

    private byte[] AllBytes()
    {
        using var ms = new MemoryStream(checked((int)ByteLength));
        CopyTo(ms);
        return ms.ToArray();
    }

    private void Show(string text)
    {
        _applying = true;
        try { TextDocument.Replace(0, TextDocument.TextLength, text); }
        finally { _applying = false; }
    }

    public bool Supports(TextCodec codec) => true;

    public void Reinterpret(TextCodec codec)
    {
        var bytes = AllBytes();
        _codec = codec;
        Show(Load(bytes));
    }

    /// <summary>The document the text control binds to.</summary>
    public TextDocument TextDocument { get; }

    public BackendCapabilities Capabilities => AvaloniaEditBackendFactory.Caps;
    public TextCodec Codec => _codec;
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
        if (offset < _bom.Length)
        {
            // an edit inside the BOM can change where the text starts: rebuild from the spliced bytes
            var all = AllBytes();
            var spliced = new byte[all.Length - deleteLength + insert.Length];
            all.AsSpan(0, (int)offset).CopyTo(spliced);
            insert.CopyTo(spliced.AsSpan((int)offset));
            all.AsSpan((int)(offset + deleteLength)).CopyTo(spliced.AsSpan((int)offset + insert.Length));
            Show(Load(spliced));
            return;
        }
        int c0 = _map.ByteToChar(offset - _bom.Length);
        int c1 = _map.ByteToChar(offset + deleteLength - _bom.Length);
        var bytes = insert.ToArray();
        string text = _codec.GetString(bytes);
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
        var bytes = _codec.EncodeLossy(text);
        var removed = _map.Replace(c0, c1, bytes, text, text.Length);
        EditedByBackend?.Invoke(new BackendEdit(_bom.Length + b0, removed, bytes));
    }

    /// <summary>
    /// Characters typed or pasted since the file was read that the encoding cannot hold. The bytes already carry a replacement
    /// ('?' or U+FFFD) for each of them, so saving writes the replacement; the caller must show this report first.
    /// Positions are in the text of the view; untouched text never reports a loss.
    /// </summary>
    public LossReport TypedLoss()
    {
        var typed = _map.TypedRuns().Where(r => r.Text.Any(c => !_codec.CanEncode(c) || char.IsSurrogate(c))).ToList();
        if (typed.Count == 0) return LossReport.None;
        var probe = new System.Text.StringBuilder(TextDocument.TextLength);
        int at = 0;
        foreach (var (start, text) in typed)
        {
            AppendMasked(probe, at, start);
            probe.Append(text);
            at = start + text.Length;
        }
        AppendMasked(probe, at, TextDocument.TextLength);
        return LossReport.ForText(probe.ToString(), _codec);
    }

    private void AppendMasked(System.Text.StringBuilder sb, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            char c = TextDocument.GetCharAt(i);
            sb.Append(c is '\r' or '\n' ? c : 'x');
        }
    }

    public string ReadText(ByteRange range)
    {
        int c0 = _map.ByteToChar(range.Start - _bom.Length);
        int c1 = _map.ByteToChar(range.End - _bom.Length);
        return TextDocument.GetText(c0, c1 - c0);
    }

    /// <summary>Byte offset of a text view offset (inside a surrogate pair: the start of the pair).</summary>
    public long ToByteOffset(int charOffset) => _bom.Length + _map.CharToByte(charOffset, out _);

    /// <summary>Text view offset of a byte offset; false when it is not a character boundary or outside the text.</summary>
    public bool TryToCharOffset(long byteOffset, out int charOffset)
    {
        charOffset = 0;
        if (byteOffset < _bom.Length || byteOffset > ByteLength) return false;
        try { charOffset = _map.ByteToChar(byteOffset - _bom.Length); }
        catch (ArgumentException) { return false; }
        return true;
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

using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Accessibility;

/// <summary>
/// The text, line and selection half of <see cref="ITextAutomationSurface"/> over a <see cref="Document"/>; a view derives from it
/// and adds geometry and focus. The text of the revision is decoded once per revision, so this fits surfaces that materialize
/// text (the AvaloniaEdit surface); a paged surface implements the interface on its own line index.
/// While <see cref="MaskSecrets"/> is true the reported text shows <see cref="HiddenMarker"/> in place of every secret value; all offsets of the
/// interface are then offsets of that shown text, and <see cref="ToDocument"/> / <see cref="ToShown"/> convert from and to the document's own.
/// Not thread safe: call it on the UI thread, which is what <see cref="InvokeOnUiThread"/> guarantees for the bridges.
/// </summary>
public abstract class DocumentTextSurface : ITextAutomationSurface, IDisposable
{
    private readonly Document _document;
    private string _real = "";
    private List<SecretSpan> _masked = [];
    private string _marker = "";
    private string _text = "";
    private int[] _lineStarts = [0];
    private int[] _realLineStarts = [0];
    private TextSpan _selection;
    private int _caret;

    protected DocumentTextSurface(Document document)
    {
        _document = document;
        Reload();
        _document.Changed += OnDocumentChanged;
    }

    public Document Document => _document;
    public long Revision => _document.Revision;
    public int Length => _text.Length;
    public int LineCount => _lineStarts.Length;
    public TextSpan Selection => _selection;
    public int CaretOffset => _caret;
    public virtual bool IsReadOnly => false;

    public event Action<TextChange>? TextChanged;
    public event Action? SelectionChanged;
    public event Action? FocusChanged;

    public abstract bool HasFocus { get; }
    public abstract void Focus();
    public abstract ScreenRect ScreenBounds { get; }
    public abstract TextSpan VisibleRange { get; }
    public abstract IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length);
    public abstract int OffsetFromPoint(double x, double y);
    public abstract void ScrollIntoView(int start, int length, bool alignToTop);

    public virtual void InvokeOnUiThread(Action action) => action();

    /// <summary>The values of a secrets file are hidden on screen, so they are hidden from the screen reader too.</summary>
    protected virtual bool MaskSecrets => false;
    /// <summary>The one value that is shown in plain now (the user rests on it), in document offsets.</summary>
    protected virtual SecretSpan? RevealedSecret => null;
    /// <summary>What the screen reader gets in place of a hidden value; it names the value.</summary>
    protected virtual string HiddenMarker => SecretValues.Placeholder;

    /// <summary>The mask changed without an edit (values shown or hidden): the text is built again and <see cref="TextChanged"/> tells what differs.</summary>
    protected void RefreshMask()
    {
        string old = _text;
        Reload();
        if (old != _text) RaiseTextChanged(old);
    }

    /// <summary>Offset in the document's own text to the offset in the shown text; inside a hidden value it is the start of the marker, or its end when <paramref name="end"/>.</summary>
    protected int ToShown(int documentOffset, bool end = false)
    {
        int shift = 0;
        foreach (var span in _masked)
        {
            if (documentOffset >= span.End) shift += _marker.Length - span.Length;
            else if (documentOffset > span.Start) return span.Start + shift + (end ? _marker.Length : 0);
            else break;
        }
        return documentOffset + shift;
    }

    /// <summary>Offset in the shown text to the offset in the document's own text; inside a marker it is the nearer end of the value.</summary>
    protected int ToDocument(int shownOffset)
    {
        int shift = 0;
        foreach (var span in _masked)
        {
            int from = span.Start + shift, to = from + _marker.Length;
            if (shownOffset >= to) shift += _marker.Length - span.Length;
            else if (shownOffset > from) return (shownOffset - from) * 2 >= _marker.Length ? span.End : span.Start;
            else break;
        }
        return shownOffset - shift;
    }

    protected void RaiseFocusChanged() => FocusChanged?.Invoke();

    public string GetText(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > _text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        return _text.Substring(start, length);
    }

    public int LineStart(int line)
    {
        if (line < 0 || line > _lineStarts.Length) throw new ArgumentOutOfRangeException(nameof(line));
        return line == _lineStarts.Length ? _text.Length : _lineStarts[line];
    }

    public int LineOfOffset(int offset)
    {
        offset = Math.Clamp(offset, 0, _text.Length);
        return LineIndexOf(_lineStarts, offset);
    }

    public virtual void SetSelection(int start, int length) => Select(start, start + length);

    /// <summary>Selects from an anchor to the caret; the caret may lie before the anchor.</summary>
    public void Select(int anchor, int caret)
    {
        anchor = Math.Clamp(anchor, 0, _text.Length);
        caret = Math.Clamp(caret, 0, _text.Length);
        var span = new TextSpan(Math.Min(anchor, caret), Math.Abs(caret - anchor));
        if (span == _selection && caret == _caret) return;
        _selection = span;
        _caret = caret;
        SelectionChanged?.Invoke();
    }

    /// <summary>Replaces a range through the document and puts the caret after the new text.</summary>
    public void ReplaceText(int start, int length, string text, string? coalesceKey = null)
    {
        if (IsReadOnly) throw new InvalidOperationException("The surface is read only.");
        int from = ToDocument(start), to = ToDocument(start + length);
        var range = new ByteRange(ToByteOffset(from), ToByteOffset(to));
        var result = _document.Replace(range, _document.Codec.Encode(text), new EditOptions { CoalesceKey = coalesceKey });
        if (!result.Applied) throw new InvalidOperationException($"The document refused the edit: {result.Conflict}.");
        int caret = ToShown(from + text.Length);
        Select(caret, caret);
    }

    private long ToByteOffset(int offset)
    {
        int line = LineIndexOf(_realLineStarts, offset);
        if (_document.TryGetOffset(new LinePosition(line, offset - _realLineStarts[line]), out long bytes)) return bytes;
        // shortcut: the line index is still being built, so encode the prefix (O(n)); only the first edits after opening a huge file pay for it
        var prefix = _real.AsSpan(0, offset);
        var codec = _document.Codec;
        return _document.BomLength + (codec.IsLossy ? codec.LossyByteCount(prefix) : codec.Encode(prefix.ToString()).Length);
    }

    private static int[] LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            else if (c is not ('\r' or '\n')) continue;
            starts.Add(i + 1);
        }
        return [.. starts];
    }

    private static int LineIndexOf(int[] starts, int offset)
    {
        int i = Array.BinarySearch(starts, offset);
        return i >= 0 ? i : ~i - 1;
    }

    private void Reload()
    {
        long from = _document.BomLength;
        _real = _document.ByteLength > from ? _document.ReadText(new ByteRange(from, _document.ByteLength)) : "";
        _realLineStarts = LineStarts(_real);
        _masked = [];
        _marker = HiddenMarker;
        if (MaskSecrets)
        {
            var revealed = RevealedSecret;
            _masked = SecretValues.Find(_real).Where(s => s != revealed).ToList();
        }
        if (_masked.Count == 0) _text = _real;
        else
        {
            var sb = new System.Text.StringBuilder(_real.Length);
            int copied = 0;
            foreach (var span in _masked)
            {
                sb.Append(_real, copied, span.Start - copied).Append(_marker);
                copied = span.End;
            }
            _text = sb.Append(_real, copied, _real.Length - copied).ToString();
        }
        _lineStarts = _masked.Count == 0 ? _realLineStarts : LineStarts(_text);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        long trace = NksHub.Textida.Core.TypingTrace.Begin();
        try { OnDocumentChangedCore(); }
        finally { NksHub.Textida.Core.TypingTrace.End("a11y.onDocumentChanged", trace); }
    }

    private void OnDocumentChangedCore()
    {
        string old = _text;
        Reload();
        int anchor0 = _caret == _selection.Start ? _selection.End : _selection.Start;
        var change = DiffChange(old);

        int Map(int p) => p <= change.Start ? p
            : p >= change.Start + change.Removed ? p + change.Inserted - change.Removed
            : change.Start + change.Inserted;
        int anchor2 = Math.Clamp(Map(anchor0), 0, _text.Length), caret2 = Math.Clamp(Map(_caret), 0, _text.Length);

        TextChanged?.Invoke(change);
        Select(anchor2, caret2);
    }

    private TextChange DiffChange(string old)
    {
        int prefix = 0, max = Math.Min(old.Length, _text.Length);
        while (prefix < max && old[prefix] == _text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == _text[_text.Length - 1 - suffix]) suffix++;
        return new TextChange(prefix, old.Length - prefix - suffix, _text.Length - prefix - suffix, _document.Revision);
    }

    private void RaiseTextChanged(string old) => TextChanged?.Invoke(DiffChange(old));

    public virtual void Dispose() => _document.Changed -= OnDocumentChanged;
}

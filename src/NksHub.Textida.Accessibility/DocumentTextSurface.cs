using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility;

/// <summary>
/// The text, line and selection half of <see cref="ITextAutomationSurface"/> over a <see cref="Document"/>; a view derives from it
/// and adds geometry and focus. The text of the revision is decoded once per revision, so this fits surfaces that materialize
/// text (the AvaloniaEdit surface); a paged surface implements the interface on its own line index.
/// Not thread safe: call it on the UI thread, which is what <see cref="InvokeOnUiThread"/> guarantees for the bridges.
/// </summary>
public abstract class DocumentTextSurface : ITextAutomationSurface, IDisposable
{
    private readonly Document _document;
    private string _text = "";
    private int[] _lineStarts = [0];
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
        int i = Array.BinarySearch(_lineStarts, offset);
        return i >= 0 ? i : ~i - 1;
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
        var range = new ByteRange(ToByteOffset(start), ToByteOffset(start + length));
        var result = _document.Replace(range, _document.Codec.Encode(text), new EditOptions { CoalesceKey = coalesceKey });
        if (!result.Applied) throw new InvalidOperationException($"The document refused the edit: {result.Conflict}.");
        Select(start + text.Length, start + text.Length);
    }

    private long ToByteOffset(int offset)
    {
        int line = LineOfOffset(offset);
        if (_document.TryGetOffset(new LinePosition(line, offset - LineStart(line)), out long bytes)) return bytes;
        // shortcut: the line index is still being built, so encode the prefix (O(n)); only the first edits after opening a huge file pay for it
        var prefix = _text.AsSpan(0, offset);
        var codec = _document.Codec;
        return _document.BomLength + (codec.IsLossy ? codec.LossyByteCount(prefix) : codec.Encode(prefix.ToString()).Length);
    }

    private void Reload()
    {
        long from = _document.BomLength;
        _text = _document.ByteLength > from ? _document.ReadText(new ByteRange(from, _document.ByteLength)) : "";
        var starts = new List<int> { 0 };
        for (int i = 0; i < _text.Length; i++)
        {
            char c = _text[i];
            if (c == '\r' && i + 1 < _text.Length && _text[i + 1] == '\n') i++;
            else if (c is not ('\r' or '\n')) continue;
            starts.Add(i + 1);
        }
        _lineStarts = [.. starts];
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
        int prefix = 0, max = Math.Min(old.Length, _text.Length);
        while (prefix < max && old[prefix] == _text[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == _text[_text.Length - 1 - suffix]) suffix++;
        var change = new TextChange(prefix, old.Length - prefix - suffix, _text.Length - prefix - suffix, _document.Revision);

        int Map(int p) => p <= change.Start ? p
            : p >= change.Start + change.Removed ? p + change.Inserted - change.Removed
            : change.Start + change.Inserted;
        int anchor = _caret == _selection.Start ? _selection.End : _selection.Start;
        int anchor2 = Math.Clamp(Map(anchor), 0, _text.Length), caret2 = Math.Clamp(Map(_caret), 0, _text.Length);

        TextChanged?.Invoke(change);
        Select(anchor2, caret2);
    }

    public virtual void Dispose() => _document.Changed -= OnDocumentChanged;
}

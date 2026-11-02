using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Accessibility;

/// <summary>
/// The text, line and selection half of <see cref="ITextAutomationSurface"/> over a <see cref="Document"/>; a view derives from it
/// and adds geometry and focus. The text is decoded once, when the surface is made; an edit of one stretch (typing, a deletion, undo) is applied to it
/// in place, so the cost of a keystroke follows the size of the edit and not the size of the document. Anything else (a new encoding,
/// hidden secret values, a line index that is not ready) decodes the document again.
/// While <see cref="MaskSecrets"/> is true the reported text shows <see cref="HiddenMarker"/> in place of every secret value; all offsets of the
/// interface are then offsets of that shown text, and <see cref="ToDocument"/> / <see cref="ToShown"/> convert from and to the document's own.
/// Not thread safe: call it on the UI thread, which is what <see cref="InvokeOnUiThread"/> guarantees for the bridges.
/// </summary>
public abstract class DocumentTextSurface : ITextAutomationSurface, IDisposable
{
    private readonly Document _document;
    private GapText _real = new("");
    private List<SecretSpan> _masked = [];
    private string _marker = "";
    private string? _shown; // the text with the markers in place of the values; null while nothing is hidden and _real is the shown text
    private int[] _lineStarts = [0];
    private int[] _realLineStarts = [0];
    private long _syncedRevision;
    private TextSpan _selection;
    private int _caret;

    protected DocumentTextSurface(Document document)
    {
        _document = document;
        Reload();
        _document.Changed += OnDocumentChanged;
    }

    /// <summary>Characters decoded from the document so far: all of them at every full read, only the inserted ones for an edit applied in place.</summary>
    internal long CharsDecoded { get; private set; }

    public Document Document => _document;
    public long Revision => _document.Revision;
    public int Length => _shown?.Length ?? _real.Length;
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
        string old = ShownText();
        Reload();
        string now = ShownText();
        if (old != now) RaiseTextChanged(old, now);
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
        if (start < 0 || length < 0 || start + length > Length) throw new ArgumentOutOfRangeException(nameof(start));
        return _shown is null ? _real.Substring(start, length) : _shown.Substring(start, length);
    }

    public int LineStart(int line)
    {
        if (line < 0 || line > _lineStarts.Length) throw new ArgumentOutOfRangeException(nameof(line));
        return line == _lineStarts.Length ? Length : _lineStarts[line];
    }

    public int LineOfOffset(int offset)
    {
        offset = Math.Clamp(offset, 0, Length);
        return LineIndexOf(_lineStarts, offset);
    }

    public virtual void SetSelection(int start, int length) => Select(start, start + length);

    /// <summary>Selects from an anchor to the caret; the caret may lie before the anchor.</summary>
    public void Select(int anchor, int caret)
    {
        anchor = Math.Clamp(anchor, 0, Length);
        caret = Math.Clamp(caret, 0, Length);
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
        return _document.BomLength + ByteCount(_real.Substring(0, offset));
    }

    private long ByteCount(string text) => _document.Codec.IsLossy ? _document.Codec.LossyByteCount(text) : _document.Codec.Encode(text).Length;

    private string ShownText() => _shown ?? _real.ToString();

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
        string real = _document.ByteLength > from ? _document.ReadText(new ByteRange(from, _document.ByteLength)) : "";
        CharsDecoded += real.Length;
        _syncedRevision = _document.Revision;
        _real = new GapText(real);
        _realLineStarts = LineStarts(real);
        _masked = [];
        _marker = HiddenMarker;
        if (MaskSecrets)
        {
            var revealed = RevealedSecret;
            _masked = SecretValues.Find(real).Where(s => s != revealed).ToList();
        }
        if (_masked.Count == 0)
        {
            _shown = null;
            _lineStarts = _realLineStarts;
            return;
        }
        var sb = new System.Text.StringBuilder(real.Length);
        int copied = 0;
        foreach (var span in _masked)
        {
            sb.Append(real, copied, span.Start - copied).Append(_marker);
            copied = span.End;
        }
        _shown = sb.Append(real, copied, real.Length - copied).ToString();
        _lineStarts = LineStarts(_shown);
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        long trace = NksHub.Textida.Core.TypingTrace.Begin();
        try { OnDocumentChangedCore(e.Change); }
        finally { NksHub.Textida.Core.TypingTrace.End("a11y.onDocumentChanged", trace); }
    }

    private void OnDocumentChangedCore(ChangeSet changeSet)
    {
        int anchor0 = _caret == _selection.Start ? _selection.End : _selection.Start;
        if (!TryApply(changeSet, out var change))
        {
            string old = ShownText();
            Reload();
            change = DiffChange(old, ShownText(), _document.Revision);
        }

        int Map(int p) => p <= change.Start ? p
            : p >= change.Start + change.Removed ? p + change.Inserted - change.Removed
            : change.Start + change.Inserted;
        int anchor2 = Math.Clamp(Map(anchor0), 0, Length), caret2 = Math.Clamp(Map(_caret), 0, Length);

        TextChanged?.Invoke(change);
        Select(anchor2, caret2);
    }

    /// <summary>
    /// Applies a change to the text and the line starts in place, edit by edit from the front: when an edit is reached, everything in front of it already
    /// reads as in the new document. False when the change is not that simple (the caller then reads the document again): a re-read in another encoding,
    /// hidden values, a position the line index does not know yet. The text is then put back as it was, so the caller can still diff against it.
    /// </summary>
    private bool TryApply(ChangeSet changeSet, out TextChange change)
    {
        change = default;
        if (changeSet.Kind is not (ChangeKind.Edit or ChangeKind.Undo or ChangeKind.Redo) || changeSet.Edits.Count == 0 || changeSet.Revision != _syncedRevision + 1 || MaskSecrets || _shown is not null) return false;
        List<(int Start, string Removed, int Inserted)> done = [];
        try
        {
            long shiftBytes = 0;
            int shiftChars = 0, first = -1, oldEnd = 0;
            foreach (var edit in changeSet.Edits)
            {
                long newStart = edit.Start + shiftBytes;
                if (edit.Start < _document.BomLength || edit.Inserted > int.MaxValue / 4 || edit.Deleted > int.MaxValue / 4) return Undo(done);
                if (!TryCharIndexOf(newStart, out int start)) return Undo(done);
                if (!TryCharsOf(start, edit.Deleted, out int removed)) return Undo(done);
                string inserted = edit.Inserted == 0 ? "" : _document.ReadText(new ByteRange(newStart, newStart + edit.Inserted));
                string was = _real.Substring(start, removed);
                if (!ApplyLineStarts(start, removed, inserted)) return Undo(done);
                CharsDecoded += inserted.Length;
                _real.Replace(start, removed, inserted);
                done.Add((start, was, inserted.Length));
                if (first < 0) first = start;
                oldEnd = start + removed - shiftChars;
                shiftChars += inserted.Length - removed;
                shiftBytes += edit.Inserted - edit.Deleted;
            }
            _lineStarts = _realLineStarts;
            _syncedRevision = changeSet.Revision;
            change = new TextChange(first, oldEnd - first, oldEnd - first + shiftChars, changeSet.Revision);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return Undo(done);
        }

        bool Undo(List<(int Start, string Removed, int Inserted)> applied)
        {
            for (int i = applied.Count - 1; i >= 0; i--) _real.Replace(applied[i].Start, applied[i].Inserted, applied[i].Removed);
            return false;
        }
    }

    /// <summary>
    /// The index in the text so far of the character at a byte offset of the new document. The text in front of the edit is the same in both revisions, so the line
    /// and column of that offset are its line and column here; except at the start of a line, where a CR in front may have been part of a CR LF pair in the old text.
    /// The character in front is then located instead, because the line it ends starts in front of the edit and means the same in both texts.
    /// </summary>
    private bool TryCharIndexOf(long newOffset, out int index)
    {
        index = 0;
        if (!_document.TryGetPosition(newOffset, out var position) || position.Line >= _realLineStarts.Length) return false;
        long at;
        if (position.Column == 0 && position.Line > 0)
        {
            int width = _document.Codec.Encode("\n").Length;
            if (newOffset < _document.BomLength + width || !_document.TryGetPosition(newOffset - width, out var before) || before.Line >= position.Line) return false;
            at = _realLineStarts[before.Line] + before.Column + 1;
        }
        else at = _realLineStarts[position.Line] + position.Column;
        if (at > _real.Length) return false;
        index = (int)at;
        return true;
    }

    /// <summary>How many characters of the old text from <paramref name="from"/> take exactly <paramref name="bytes"/> bytes in the encoding; false when no number of characters does.</summary>
    private bool TryCharsOf(int from, long bytes, out int chars)
    {
        chars = 0;
        if (bytes == 0) return true;
        int high = (int)Math.Min(bytes, _real.Length - from); // a character takes at least one byte
        if (high <= 0) return false;
        // a character takes more bytes than one only in rare texts; try the answer for plain ASCII first, else bisect (the byte count grows with every character)
        if (ByteCount(_real.Substring(from, high)) == bytes) { chars = high; return true; }
        int low = 0;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (ByteCount(_real.Substring(from, middle)) < bytes) low = middle + 1;
            else high = middle;
        }
        if (low == 0 || ByteCount(_real.Substring(from, low)) != bytes) return false;
        chars = low;
        return true;
    }

    /// <summary>Moves the line starts for an edit; false (nothing changed) when the edit would join or split a CR LF pair, which only a full scan gets right.</summary>
    private bool ApplyLineStarts(int start, int removed, string inserted)
    {
        int end = start + removed, length = _real.Length;
        char before = start > 0 ? _real[start - 1] : '\0', after = end < length ? _real[end] : '\0';
        char first = inserted.Length > 0 ? inserted[0] : after, last = inserted.Length > 0 ? inserted[^1] : before;
        if ((before == '\r' && first == '\n') || (last == '\r' && after == '\n') || (before == '\r' && start < length && _real[start] == '\n')) return false;

        var starts = _realLineStarts;
        int kept = LineIndexOf(starts, start) + 1; // the starts up to and including the one at or before the edit stay
        int tail = LineIndexOf(starts, end) + 1; // the starts after the removed text
        int delta = inserted.Length - removed;
        List<int>? made = null;
        for (int i = 0; i < inserted.Length; i++)
        {
            char c = inserted[i];
            if (c == '\r' && i + 1 < inserted.Length && inserted[i + 1] == '\n') i++;
            else if (c is not ('\r' or '\n')) continue;
            (made ??= []).Add(start + i + 1);
        }

        if (made is null && tail == kept) // typing: no line break comes or goes, the starts after the edit move
        {
            if (delta != 0) for (int i = tail; i < starts.Length; i++) starts[i] += delta;
            return true;
        }
        var result = new int[kept + (made?.Count ?? 0) + (starts.Length - tail)];
        Array.Copy(starts, result, kept);
        made?.CopyTo(result, kept);
        int next = kept + (made?.Count ?? 0);
        for (int i = tail; i < starts.Length; i++) result[next++] = starts[i] + delta;
        _realLineStarts = result;
        return true;
    }

    private static TextChange DiffChange(string old, string now, long revision)
    {
        int prefix = 0, max = Math.Min(old.Length, now.Length);
        while (prefix < max && old[prefix] == now[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && old[old.Length - 1 - suffix] == now[now.Length - 1 - suffix]) suffix++;
        return new TextChange(prefix, old.Length - prefix - suffix, now.Length - prefix - suffix, revision);
    }

    private void RaiseTextChanged(string old, string now) => TextChanged?.Invoke(DiffChange(old, now, _document.Revision));

    public virtual void Dispose() => _document.Changed -= OnDocumentChanged;

    /// <summary>Text with a gap at the place of the last edit, so typing there moves no characters.</summary>
    private sealed class GapText
    {
        private char[] _buffer;
        private int _gapStart, _gapEnd;

        public GapText(string text)
        {
            _buffer = new char[text.Length + Math.Max(256, text.Length / 16)];
            text.CopyTo(0, _buffer, 0, text.Length);
            _gapStart = text.Length;
            _gapEnd = _buffer.Length;
        }

        public int Length => _buffer.Length - (_gapEnd - _gapStart);

        public char this[int index] => _buffer[index < _gapStart ? index : index + (_gapEnd - _gapStart)];

        public string Substring(int start, int length)
        {
            int gap = _gapEnd - _gapStart;
            if (start + length <= _gapStart) return new string(_buffer, start, length);
            if (start >= _gapStart) return new string(_buffer, start + gap, length);
            return string.Create(length, (this, start), static (span, state) =>
            {
                var (text, from) = state;
                int head = text._gapStart - from;
                text._buffer.AsSpan(from, head).CopyTo(span);
                text._buffer.AsSpan(text._gapEnd, span.Length - head).CopyTo(span[head..]);
            });
        }

        public override string ToString() => Substring(0, Length);

        public void Replace(int start, int removed, string inserted)
        {
            MoveGap(start);
            _gapEnd += removed;
            if (inserted.Length > _gapEnd - _gapStart)
            {
                int tail = _buffer.Length - _gapEnd, size = Length + inserted.Length + Math.Max(256, Length / 16);
                var grown = new char[size];
                Array.Copy(_buffer, grown, _gapStart);
                Array.Copy(_buffer, _gapEnd, grown, size - tail, tail);
                _buffer = grown;
                _gapEnd = size - tail;
            }
            inserted.CopyTo(0, _buffer, _gapStart, inserted.Length);
            _gapStart += inserted.Length;
        }

        private void MoveGap(int position)
        {
            if (position < _gapStart)
            {
                int count = _gapStart - position;
                Array.Copy(_buffer, position, _buffer, _gapEnd - count, count);
                _gapStart = position;
                _gapEnd -= count;
            }
            else if (position > _gapStart)
            {
                int count = position - _gapStart;
                Array.Copy(_buffer, _gapEnd, _buffer, _gapStart, count);
                _gapStart += count;
                _gapEnd += count;
            }
        }
    }
}

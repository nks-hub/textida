namespace NksHub.Textida.Core.Documents;

/// <summary>
/// Bookmarks of one document: each is an <see cref="Anchor"/> at the start of a line, so it moves with the text through edits, undo and redo.
/// A bookmark whose line was deleted from inside is dropped, never moved to a guessed line.
/// </summary>
public sealed class Bookmarks
{
    private readonly List<Anchor> _anchors = [];
    private Document _document;

    public Bookmarks(Document document) => _document = document;

    /// <summary>Raised after a bookmark was added, removed or the set was replaced.</summary>
    public event Action? Changed;

    /// <summary>Zero-based lines that carry a bookmark, ascending and without duplicates; lines the index does not reach yet are left out.</summary>
    public IReadOnlyList<long> Lines
    {
        get
        {
            var lines = new SortedSet<long>();
            foreach (var anchor in _anchors.ToList())
            {
                if (!anchor.IsValid) { _anchors.Remove(anchor); continue; }
                if (_document.TryGetPosition(anchor.Offset, out var p)) lines.Add(p.Line);
            }
            return [.. lines];
        }
    }

    /// <summary>Byte offsets of the bookmarks at the current revision, for the journal.</summary>
    public IReadOnlyList<long> Offsets => [.. _anchors.Where(a => a.IsValid).Select(a => a.Offset).Order().Distinct()];

    public bool HasAny => _anchors.Count > 0;

    internal int ListenerCount => Changed?.GetInvocationList().Length ?? 0;

    /// <summary>Adds a bookmark on the line, or removes the ones on it. False when the line has no offset yet (large file, index incomplete).</summary>
    public bool Toggle(long line, out bool added)
    {
        added = false;
        if (!_document.TryGetOffset(new LinePosition(line, 0), out long offset)) return false;
        var onLine = _anchors.Where(a => a.IsValid && _document.TryGetPosition(a.Offset, out var p) && p.Line == line).ToList();
        if (onLine.Count > 0) _anchors.RemoveAll(onLine.Contains);
        else
        {
            _anchors.Add(_document.CreateAnchor(offset, AnchorGravity.Right));
            added = true;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The next bookmark line after (or before) <paramref name="line"/>, wrapping around the document; null without bookmarks.</summary>
    public long? Step(long line, bool forward)
    {
        var lines = Lines;
        if (lines.Count == 0) return null;
        if (forward) return lines.Cast<long?>().FirstOrDefault(l => l > line) ?? lines[0];
        return lines.Cast<long?>().LastOrDefault(l => l < line) ?? lines[^1];
    }

    public void Clear()
    {
        if (_anchors.Count == 0) return;
        _anchors.Clear();
        Changed?.Invoke();
    }

    /// <summary>Sets the bookmarks from offsets that were valid at <paramref name="revision"/> (the session of the journal); offsets that cannot be mapped to now are dropped.</summary>
    public void Restore(IEnumerable<long> offsets, long revision)
    {
        _anchors.Clear();
        foreach (long offset in offsets)
        {
            if (offset < 0 || offset > _document.ByteLength && revision == _document.Revision) continue;
            var anchor = _document.CreateAnchor(offset, AnchorGravity.Right, revision);
            if (anchor.IsValid) _anchors.Add(anchor);
        }
        Changed?.Invoke();
    }

    /// <summary>The document was replaced by a reload: bookmarks stay on their lines where the new text still has them.</summary>
    public void Rebind(Document next)
    {
        var lines = Lines;
        _document = next;
        _anchors.Clear();
        foreach (long line in lines)
            if (next.TryGetOffset(new LinePosition(line, 0), out long offset)) _anchors.Add(next.CreateAnchor(offset, AnchorGravity.Right));
        Changed?.Invoke();
    }
}

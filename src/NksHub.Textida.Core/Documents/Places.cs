namespace NksHub.Textida.Core.Documents;

/// <summary>
/// A spot in a document that follows the text through edits, undo and redo (it holds an <see cref="Anchor"/>). When the document is closed
/// <see cref="Freeze"/> turns it into a plain line and column of the file, which is all a later reopening needs.
/// </summary>
public sealed class Place
{
    private Anchor? _anchor;
    private Document? _document;

    private Place(Document document, Anchor anchor, string? path, long line, long column)
    {
        _document = document;
        _anchor = anchor;
        Path = path;
        _line = line;
        _column = column;
    }

    /// <summary>The open document the place lives in; null after <see cref="Freeze"/>.</summary>
    public Document? Document => _document;

    /// <summary>File of the document, kept up to date when the file is moved; null for an unnamed document.</summary>
    public string? Path { get; internal set; }

    /// <summary>Zero-based line; for a live place the position of the anchor as of the last <see cref="Refresh"/>.</summary>
    public long Line
    {
        get { if (!_looked) Refresh(); return _line; }
        private set => _line = value;
    }

    public long Column
    {
        get { if (!_looked) Refresh(); return _column; }
        private set => _column = value;
    }

    private long _line, _column;
    private bool _looked;

    /// <summary>A line of text around the place, for lists. Set by whoever has the text.</summary>
    public string? Snippet { get; set; }

    public bool IsLive => _document is not null;

    /// <param name="refresh">False looks the line up when <see cref="Line"/> or <see cref="Column"/> is first read; a lookup can cost a scan of the line index.</param>
    public static Place At(Document document, long byteOffset, bool refresh = true)
    {
        var anchor = document.CreateAnchor(Math.Clamp(byteOffset, 0, document.ByteLength));
        var place = new Place(document, anchor, document.FilePath, 0, 0);
        if (refresh) place.Refresh();
        return place;
    }

    /// <summary>Reads the current position from the anchor. An anchor whose text was deleted keeps the last known line, never a guess.</summary>
    public void Refresh()
    {
        long trace = TypingTrace.Begin();
        try { RefreshCore(); }
        finally { TypingTrace.End("place.refresh", trace); }
    }

    private void RefreshCore()
    {
        _looked = true;
        if (_document is not { } doc || _anchor is not { IsValid: true } a) return;
        try
        {
            if (!doc.TryGetPosition(a.Offset, out var p)) return;
            Line = p.Line;
            Column = p.Column;
        }
        catch (ObjectDisposedException)
        {
            // the document was replaced or closed without Detach; what we know now is all there will be
            _document = null;
            _anchor = null;
        }
    }

    /// <summary>Byte offset in the live document, or null for a frozen place or one whose text was deleted.</summary>
    public long? Offset => _anchor is { IsValid: true } a ? a.Offset : null;

    /// <summary>Detaches the place from its document, which is about to close.</summary>
    public void Freeze()
    {
        Refresh();
        Path = _document?.FilePath ?? Path;
        _document = null;
        _anchor = null;
    }

    /// <summary>True when both places are on the same line of the same document or file.</summary>
    public bool SameLineAs(Place other)
    {
        bool same = _document is not null ? ReferenceEquals(_document, other._document) : other._document is null && Path is not null && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);
        if (!same) return false;
        if (_document is { } doc && _anchor is { IsValid: true } a && other._anchor is { IsValid: true } b && doc.NoLineBreakBetween(a.Offset, b.Offset)) return true;
        Refresh();
        other.Refresh();
        return Line == other.Line;
    }
}

/// <summary>
/// Places of the last edits, newest first, one per line and document. Documents are attached to be watched; every edit moves its line to the top.
/// </summary>
public sealed class PlaceList(int capacity = 100)
{
    private readonly List<Place> _places = [];
    private readonly Dictionary<Document, EventHandler<DocumentChangedEventArgs>> _watched = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Place> Places => _places;

    /// <summary>Raised after the list changed.</summary>
    public event Action? Changed;

    public void Attach(Document document)
    {
        if (_watched.ContainsKey(document)) return;
        void OnChanged(object? sender, DocumentChangedEventArgs e) => RecordEdit(document, e.Change);
        _watched[document] = OnChanged;
        document.Changed += OnChanged;
    }

    /// <summary>Stops watching a document and turns its places into file positions (unnamed documents leave nothing behind).</summary>
    public void Detach(Document document, Action<Place>? beforeFreeze = null)
    {
        if (_watched.Remove(document, out var handler)) document.Changed -= handler;
        for (int i = _places.Count - 1; i >= 0; i--)
        {
            var place = _places[i];
            if (!ReferenceEquals(place.Document, document)) continue;
            beforeFreeze?.Invoke(place);
            place.Freeze();
            if (place.Path is null) _places.RemoveAt(i);
        }
        Changed?.Invoke();
    }

    /// <summary>The file of a document or of a folder was moved.</summary>
    public void Rename(string oldPath, string newPath)
    {
        foreach (var place in _places)
            if (PathMove.Rebase(place.Path, oldPath, newPath) is { } moved) place.Path = moved;
    }

    private void RecordEdit(Document document, ChangeSet change)
    {
        if (change.Kind == ChangeKind.View || change.Edits.Count == 0) return;
        long shift = 0, at = 0;
        foreach (var span in change.Edits)
        {
            at = span.Start + shift + span.Inserted;
            shift += span.Inserted - span.Deleted;
        }
        Record(Place.At(document, at, refresh: false));
    }

    public void Record(Place place)
    {
        _places.RemoveAll(p => p.SameLineAs(place));
        _places.Insert(0, place);
        if (_places.Count > capacity) _places.RemoveRange(capacity, _places.Count - capacity);
        Changed?.Invoke();
    }
}

/// <summary>Back and forward through the places left by jumps (go to line, open from a list, a search hit), like the history of a browser.</summary>
public sealed class JumpHistory(int capacity = 100)
{
    private readonly List<Place> _back = [];
    private readonly List<Place> _forward = [];

    public int BackCount => _back.Count;
    public int ForwardCount => _forward.Count;

    /// <summary>The user is about to jump away from <paramref name="from"/>; going forward is no longer possible.</summary>
    public void Jumped(Place from)
    {
        _forward.Clear();
        Push(_back, from);
    }

    /// <summary>The place to go back to, or null. <paramref name="current"/> is where the user is now and can be reached again with <see cref="Forward"/>.</summary>
    public Place? Back(Place current)
    {
        if (_back.Count == 0) return null;
        var target = Pop(_back);
        Push(_forward, current);
        return target;
    }

    public Place? Forward(Place current)
    {
        if (_forward.Count == 0) return null;
        var target = Pop(_forward);
        Push(_back, current);
        return target;
    }

    /// <summary>Freezes the places of a document that closes; pathless ones are dropped.</summary>
    public void Detach(Document document, Action<Place>? beforeFreeze = null)
    {
        foreach (var list in new[] { _back, _forward })
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(list[i].Document, document)) continue;
                beforeFreeze?.Invoke(list[i]);
                list[i].Freeze();
                if (list[i].Path is null) list.RemoveAt(i);
            }
    }

    public void Rename(string oldPath, string newPath)
    {
        foreach (var place in _back.Concat(_forward))
            if (PathMove.Rebase(place.Path, oldPath, newPath) is { } moved) place.Path = moved;
    }

    private void Push(List<Place> list, Place place)
    {
        if (list.Count > 0 && list[^1].SameLineAs(place)) list.RemoveAt(list.Count - 1);
        list.Add(place);
        if (list.Count > capacity) list.RemoveAt(0);
    }

    private static Place Pop(List<Place> list)
    {
        var place = list[^1];
        list.RemoveAt(list.Count - 1);
        return place;
    }
}

/// <summary>Path arithmetic for a rename or move of a file or a folder.</summary>
public static class PathMove
{
    private static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>True when <paramref name="path"/> lies below <paramref name="folder"/> (or is it).</summary>
    public static bool IsInside(string path, string folder) => Rebase(path, folder, "") is not null;

    /// <summary>
    /// The path after <paramref name="oldPath"/> (a file or a folder) was moved to <paramref name="newPath"/>:
    /// the new path itself, the path below the new folder, or null when <paramref name="path"/> is not affected.
    /// </summary>
    public static string? Rebase(string? path, string oldPath, string newPath)
    {
        if (path is null) return null;
        if (string.Equals(path, oldPath, Comparison)) return newPath;
        var prefix = oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, Comparison) ? Path.Combine(newPath, path[prefix.Length..]) : null;
    }
}

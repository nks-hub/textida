using System.Globalization;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>A named, coloured region of a document with a comment and the type it was read as in the data inspector.</summary>
public sealed class HexBookmark
{
    internal HexBookmark(ByteRange range, string name, string? color, string? comment, string? type)
    {
        Id = Guid.NewGuid();
        Range = range;
        Name = name;
        Color = color;
        Comment = comment;
        Type = type;
    }

    public Guid Id { get; }
    public string Name { get; internal set; }
    /// <summary><c>#RRGGBB</c>, or null for the default colour.</summary>
    public string? Color { get; internal set; }
    public string? Comment { get; internal set; }
    /// <summary>The inspector type the region was marked as, for example <c>uint32 LE</c>; null for a plain region.</summary>
    public string? Type { get; internal set; }
    /// <summary>Where the region is in the current revision of the document.</summary>
    public ByteRange Range { get; internal set; }
}

/// <summary>A bookmark without its identity: what a session, an export and an import carry. The range belongs to one revision of the document.</summary>
public sealed record StoredHexBookmark(string Name, string? Color, string? Comment, string? Type, long Start, long End);

public sealed record HexBookmarkImport(int Added, int Skipped);

/// <summary>
/// The regions marked in the hex view of one document. They are kept in the metadata of the session and in exports, never in the file, and follow the
/// bytes through every change of the document (typing, hex edits, undo, redo): bytes inserted before a region move it, bytes inserted inside make it longer,
/// and an edit that overlaps an edge of a region is taken into the region. A region whose bytes were all deleted is dropped, never moved to a guessed place.
/// </summary>
public sealed class HexBookmarks : IDisposable
{
    private readonly List<HexBookmark> _items = [];
    private Document _doc;

    public HexBookmarks(Document document)
    {
        _doc = document;
        document.Changed += OnChanged;
    }

    /// <summary>Raised after a region was added, removed, changed or moved by an edit.</summary>
    public event Action? Changed;

    /// <summary>The regions in the order of their first byte.</summary>
    public IReadOnlyList<HexBookmark> Items => _items;

    public int Count => _items.Count;

    internal int ListenerCount => Changed?.GetInvocationList().Length ?? 0;

    public void Dispose() => _doc.Changed -= OnChanged;

    /// <summary>A reload gave the document a new content: the regions stay where they were as long as the new content reaches them.</summary>
    public void Rebind(Document next)
    {
        _doc.Changed -= OnChanged;
        _doc = next;
        next.Changed += OnChanged;
        _items.RemoveAll(b => b.Range.End > next.ByteLength);
        Changed?.Invoke();
    }

    // ---- editing the set ----

    /// <exception cref="ArgumentException">The range is empty or lies outside the document, or the colour is not <c>#RRGGBB</c>.</exception>
    public HexBookmark Add(ByteRange range, string? name = null, string? color = null, string? comment = null, string? type = null)
    {
        if (range.IsEmpty || range.End > _doc.ByteLength) throw new ArgumentException("A region needs at least one byte inside the document.", nameof(range));
        var item = new HexBookmark(range, (name ?? "").Trim(), NormalizeColor(color), Clean(comment), Clean(type));
        int at = _items.FindLastIndex(b => b.Range.Start <= range.Start) + 1;
        _items.Insert(at, item);
        Changed?.Invoke();
        return item;
    }

    public bool Remove(Guid id)
    {
        if (_items.RemoveAll(b => b.Id == id) == 0) return false;
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }

    /// <summary>Sets the label fields of a region; the range stays.</summary>
    public bool Update(Guid id, string? name, string? color, string? comment, string? type)
    {
        var item = _items.Find(b => b.Id == id);
        if (item is null) return false;
        (item.Name, item.Color, item.Comment, item.Type) = ((name ?? "").Trim(), NormalizeColor(color), Clean(comment), Clean(type));
        Changed?.Invoke();
        return true;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The colour as <c>#RRGGBB</c> in capitals; null for none.</summary>
    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        string c = color.Trim();
        if (c.Length != 7 || c[0] != '#' || c.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF")) throw new ArgumentException("A colour is written #RRGGBB.", nameof(color));
        return c.ToUpperInvariant();
    }

    // ---- looking up ----

    /// <summary>The regions that contain the byte, the shortest first.</summary>
    public IReadOnlyList<HexBookmark> Containing(long offset) =>
        [.. _items.Where(b => b.Range.Start <= offset && offset < b.Range.End).OrderBy(b => b.Range.Length)];

    /// <summary>The next (or previous) region start after <paramref name="offset"/>, wrapping around; null without regions.</summary>
    public HexBookmark? Step(long offset, bool forward)
    {
        if (_items.Count == 0) return null;
        if (forward) return _items.FirstOrDefault(b => b.Range.Start > offset) ?? _items[0];
        return _items.LastOrDefault(b => b.Range.Start < offset) ?? _items[^1];
    }

    /// <summary>The regions whose name, comment or type contains <paramref name="text"/> (case and accents as they are, case ignored) and, when given, that have the colour.</summary>
    public IReadOnlyList<HexBookmark> Filter(string? text, string? color = null)
    {
        string? wanted = NormalizeColor(color);
        return [.. _items.Where(b => (wanted is null || b.Color == wanted)
            && (string.IsNullOrEmpty(text) || Contains(b.Name, text) || Contains(b.Comment, text) || Contains(b.Type, text)))];
    }

    private static bool Contains(string? haystack, string needle) => haystack?.Contains(needle, StringComparison.CurrentCultureIgnoreCase) == true;

    // ---- following the document ----

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_items.Count == 0 || e.Change.Edits.Count == 0) return;
        bool moved = false;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            long start = item.Range.Start, end = item.Range.End;
            if (!ChangeMap.TryMapRegion(ref start, ref end, e.Change)) _items.RemoveAt(i);
            else if (start != item.Range.Start || end != item.Range.End) item.Range = new ByteRange(start, end);
            else continue;
            moved = true;
        }
        if (!moved) return;
        _items.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start)); // List.Sort is not stable, but the order of equal starts carries no meaning
        Changed?.Invoke();
    }

    // ---- session ----

    /// <summary>The regions as they are now (valid for <see cref="Document.Revision"/>).</summary>
    public IReadOnlyList<StoredHexBookmark> Capture() =>
        [.. _items.Select(b => new StoredHexBookmark(b.Name, b.Color, b.Comment, b.Type, b.Range.Start, b.Range.End))];

    /// <summary>Replaces the set by regions that were valid at <paramref name="revision"/>, mapped through the changes since; regions the changes removed, or that the log cannot reach, are left out.</summary>
    public void Restore(IEnumerable<StoredHexBookmark> stored, long revision)
    {
        _items.Clear();
        foreach (var s in stored)
        {
            if (s.Start < 0 || s.End <= s.Start) continue;
            if (_doc.MapRegion(new ByteRange(s.Start, s.End), revision) is not { } now || now.End > _doc.ByteLength) continue;
            _items.Add(new HexBookmark(now, s.Name, SafeColor(s.Color), s.Comment, s.Type));
        }
        _items.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));
        Changed?.Invoke();
    }

    private static string? SafeColor(string? color)
    {
        try { return NormalizeColor(color); }
        catch (ArgumentException) { return null; }
    }

    // ---- export and import ----

    private const string CsvHeader = "name,color,comment,type,start,end";
    private const string JsonFormat = "textida-hex-bookmarks";

    /// <summary>One line per region: <c>name,color,comment,type,start,end</c>; start and end are byte offsets (end excluded) of the current revision.</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder(CsvHeader).Append("\r\n");
        foreach (var b in _items)
            sb.Append(Quote(b.Name)).Append(',').Append(Quote(b.Color)).Append(',').Append(Quote(b.Comment)).Append(',').Append(Quote(b.Type)).Append(',')
              .Append(b.Range.Start.ToString(CultureInfo.InvariantCulture)).Append(',').Append(b.Range.End.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        return sb.ToString();
    }

    private static string Quote(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        return field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", JsonFormat);
            w.WriteNumber("version", 1);
            w.WriteStartArray("bookmarks");
            foreach (var b in _items)
            {
                w.WriteStartObject();
                w.WriteString("name", b.Name);
                if (b.Color is not null) w.WriteString("color", b.Color);
                if (b.Comment is not null) w.WriteString("comment", b.Comment);
                if (b.Type is not null) w.WriteString("type", b.Type);
                w.WriteNumber("start", b.Range.Start);
                w.WriteNumber("end", b.Range.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads the regions of an export (CSV with its header line, or the JSON form) and adds them to the current revision; one that is empty, outside the document or already there is skipped.</summary>
    /// <exception cref="FormatException">The text is neither form.</exception>
    public HexBookmarkImport Import(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var read = text.TrimStart().StartsWith('{') ? ParseJson(text) : ParseCsv(text);
        int added = 0, skipped = 0;
        foreach (var s in read)
        {
            if (s.Start < 0 || s.End <= s.Start || s.End > _doc.ByteLength || _items.Any(b => b.Range.Start == s.Start && b.Range.End == s.End && b.Name == s.Name.Trim()))
            {
                skipped++;
                continue;
            }
            int at = _items.FindLastIndex(b => b.Range.Start <= s.Start) + 1;
            _items.Insert(at, new HexBookmark(new ByteRange(s.Start, s.End), s.Name.Trim(), SafeColor(s.Color), Clean(s.Comment), Clean(s.Type)));
            added++;
        }
        if (added > 0) Changed?.Invoke();
        return new HexBookmarkImport(added, skipped);
    }

    private static List<StoredHexBookmark> ParseJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("bookmarks", out var list) || list.ValueKind != JsonValueKind.Array) throw new FormatException("The JSON has no list of bookmarks.");
            var result = new List<StoredHexBookmark>();
            foreach (var e in list.EnumerateArray())
            {
                string? Str(string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!e.TryGetProperty("start", out var s) || !e.TryGetProperty("end", out var z) || !s.TryGetInt64(out long start) || !z.TryGetInt64(out long end)) throw new FormatException("A bookmark needs a start and an end.");
                result.Add(new StoredHexBookmark(Str("name") ?? "", Str("color"), Str("comment"), Str("type"), start, end));
            }
            return result;
        }
        catch (JsonException e)
        {
            throw new FormatException("The text is not valid JSON: " + e.Message, e);
        }
    }

    private static List<StoredHexBookmark> ParseCsv(string text)
    {
        var rows = ParseCsvRows(text);
        if (rows.Count == 0 || !string.Equals(string.Join(',', rows[0]), CsvHeader, StringComparison.Ordinal)) throw new FormatException($"The first line has to be {CsvHeader}.");
        var result = new List<StoredHexBookmark>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0) continue;
            if (row.Count != 6 || !long.TryParse(row[4], NumberStyles.None, CultureInfo.InvariantCulture, out long start) || !long.TryParse(row[5], NumberStyles.None, CultureInfo.InvariantCulture, out long end))
                throw new FormatException("A line needs six fields and numbers for the start and the end.");
            result.Add(new StoredHexBookmark(row[0], row[1], row[2], row[3], start, end));
        }
        return result;
    }

    private static List<List<string>> ParseCsvRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) { quoted = true; any = true; }
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); any = true; }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
                any = false;
            }
            else { field.Append(c); any = true; }
        }
        if (quoted) throw new FormatException("A quoted field is not closed.");
        if (any || field.Length > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }
}

using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Syntax;

/// <summary>Random UTF-16 access to one snapshot line, with four decoded windows and a bounded sparse byte map.</summary>
internal sealed class StreamingLineSource(DocumentSnapshot snapshot, long byteStart, Action<long>? onRead = null)
{
    private const int MaximumAnchors = 512;
    private readonly record struct Anchor(long Column, long Offset);
    private sealed record Window(long Column, string Text, long NextOffset);
    private readonly List<Anchor> anchors = [new(0, byteStart)];
    private readonly List<Window> windows = [];
    private long spacing = 64 << 10;
    private long? length;

    public long ByteStart => byteStart;
    public long? Length => length;
    public long NextByteOffset { get; private set; }
    public bool IsDocumentEnd { get; private set; }
    public long BytesRead { get; private set; }

    public int At(long column)
    {
        if (column < 0 || length is { } end && column >= end) return -1;
        var window = Cached(column) ?? throw new InvalidOperationException("Prepare the syntax window before reading it.");
        return column - window.Column < window.Text.Length ? window.Text[(int)(column - window.Column)] : -1;
    }

    public string SmallSlice(long from, int count)
    {
        var chars = new char[count];
        for (int i = 0; i < count; i++) chars[i] = (char)At(from + i);
        return new string(chars);
    }

    public long SkipWindow(long from, Func<char, bool> canStart)
    {
        if (length is { } end && from >= end) return from;
        var window = Cached(from) ?? throw new InvalidOperationException("Prepare the syntax window before scanning it.");
        int at = (int)(from - window.Column);
        while (at < window.Text.Length && !canStart(window.Text[at])) at++;
        return window.Column + at;
    }

    public long WindowEnd(long from)
    {
        if (length is { } end && from >= end) return end;
        var window = Cached(from) ?? throw new InvalidOperationException("Prepare the syntax window before reading its end.");
        return window.Column + window.Text.Length;
    }

    private Window? Cached(long column)
    {
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            var item = windows[i];
            if (column >= item.Column && column < item.Column + item.Text.Length) return item;
        }
        return null;
    }

    /// <summary>Each yielded step reads at most one byte window, including sparse-map replay after a backward jump.</summary>
    public IEnumerable<bool> Ensure(long column)
    {
        if (column < 0 || length is { } end && column >= end || Cached(column) is not null) yield break;

        var anchor = anchors[0];
        foreach (var candidate in anchors)
        {
            if (candidate.Column > column) break;
            anchor = candidate;
        }
        foreach (var item in windows)
        {
            long next = item.Column + item.Text.Length;
            if (next <= column && next > anchor.Column) anchor = new(next, item.NextOffset);
        }
        var reader = new SyntaxChunkReader(snapshot, anchor.Offset);
        long at = anchor.Column;
        while (true)
        {
            long before = reader.Offset;
            var chunk = reader.Read();
            BytesRead += reader.Offset - before;
            onRead?.Invoke(reader.Offset - before);
            var window = new Window(at, chunk.Text, reader.Offset);
            if (windows.Count == 4) windows.RemoveAt(0);
            windows.Add(window);
            long next = at + chunk.Text.Length;
            if (chunk.LineEnd)
            {
                length = next;
                NextByteOffset = reader.Offset;
                IsDocumentEnd = chunk.DocumentEnd;
                yield return true;
                yield break;
            }
            AddAnchor(new(next, reader.Offset));
            yield return true;
            if (column < next) yield break;
            at = next;
        }
    }

    private void AddAnchor(Anchor anchor)
    {
        if (anchor.Column - anchors[^1].Column < spacing) return;
        if (anchors.Count == MaximumAnchors)
        {
            int target = 1;
            for (int i = 2; i < anchors.Count; i += 2) anchors[target++] = anchors[i];
            anchors.RemoveRange(target, anchors.Count - target);
            spacing *= 2;
        }
        anchors.Add(anchor);
    }
}

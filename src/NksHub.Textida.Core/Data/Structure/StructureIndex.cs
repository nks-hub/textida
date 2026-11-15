using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Data.Structure;

public sealed class StructureIndex : IStructureIndex, IStructureSink, IDisposable
{
    private const int RecordSize = 104;
    private const int PageSize = 64 * 1024;
    private const int CachePages = 64;
    private const int MaxIssues = 1000;
    private readonly string directory;
    private readonly SafeFileHandle file;
    private readonly Dictionary<long, Page> cache = [];
    private readonly LinkedList<long> recency = [];
    private readonly List<StructureIssue> issues = [];
    private readonly long[] kinds = new long[Enum.GetValues<StructureKind>().Length];
    private readonly object gate = new();
    private bool disposed;

    private sealed class Page(byte[] bytes, LinkedListNode<long> position)
    {
        public byte[] Bytes { get; } = bytes;
        public LinkedListNode<long> Position { get; } = position;
        public bool Dirty { get; set; }
    }

    public StructureIndex(DocumentSnapshot source, int bomLength, StructureFormat format)
    {
        Source = source;
        Codec = source.Codec;
        BomLength = bomLength;
        Format = format;
        DocumentId = source.DocumentId;
        Revision = source.Revision;
        directory = Directory.CreateTempSubdirectory("textida-structure-").FullName;
        file = File.OpenHandle(Path.Combine(directory, "nodes.bin"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read, FileOptions.RandomAccess | FileOptions.DeleteOnClose);
    }

    public IByteSnapshot Source { get; }
    public TextCodec Codec { get; }
    public int BomLength { get; }
    public StructureFormat Format { get; }
    public Guid DocumentId { get; }
    public long Revision { get; }
    public long Count { get; private set; }
    public int MaximumDepth { get; private set; }
    public long IssueCount { get; private set; }
    public IReadOnlyList<StructureIssue> Issues => issues;
    public long CachedBytes => (long)cache.Count * PageSize;
    public long SourceRetainedBytes => Source.RetainedBytes;
    public long CountOf(StructureKind kind) => kinds[(int)kind];

    public long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            long id = Count++;
            WriteNode(new(id, parentId, kind, start, start, nameStart, nameEnd, valueStart, valueStart, ordinal, 0, id, depth));
            kinds[(int)kind]++;
            MaximumDepth = Math.Max(MaximumDepth, depth);
            return id;
        }
    }

    public void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount)
    {
        lock (gate)
        {
            var node = ReadNode(id);
            WriteNode(node with { ValueStart = valueStart, ValueEnd = valueEnd, End = end, ChildCount = childCount, LastDescendantId = Count - 1 });
        }
    }

    public void SetNamespace(long id, long namespaceId)
    {
        lock (gate) WriteNode(ReadNode(id) with { NamespaceId = namespaceId });
    }

    public void Report(StructureIssue issue)
    {
        lock (gate)
        {
            IssueCount++;
            if (issues.Count < MaxIssues) issues.Add(issue);
        }
    }

    public StructureNode ReadNode(long id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (id < 0 || id >= Count) throw new ArgumentOutOfRangeException(nameof(id));
            Span<byte> bytes = stackalloc byte[RecordSize];
            Transfer(checked(id * RecordSize), bytes, write: false);
            Span<long> values = stackalloc long[13];
            for (int i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes[(i * 8)..]);
            return new(id, values[0], (StructureKind)values[12], values[1], values[2], values[3], values[4],
                values[5], values[6], values[7], values[8], values[9], checked((int)values[11]), values[10]);
        }
    }

    public IReadOnlyList<StructureNode> ReadChildren(long parentId, long afterId, int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var parent = ReadNode(parentId);
        var result = new List<StructureNode>(Math.Min(count, 256));
        long id = afterId < 0 ? parentId + 1 : ReadNode(afterId).LastDescendantId + 1;
        while (id <= parent.LastDescendantId && result.Count < count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = ReadNode(id);
            if (node.ParentId != parentId) throw new InvalidDataException("The structural index contains an invalid child link.");
            result.Add(node);
            id = node.LastDescendantId + 1;
        }
        return result;
    }

    public StructureNode? FindContaining(long offset, CancellationToken cancellationToken)
    {
        long left = 0, right = Count - 1, candidate = -1;
        while (left <= right)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long middle = left + (right - left) / 2;
            if (ReadNode(middle).Start <= offset) { candidate = middle; left = middle + 1; }
            else right = middle - 1;
        }
        while (candidate >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = ReadNode(candidate);
            if (node.Contains(offset)) return node;
            candidate = node.ParentId;
        }
        return null;
    }

    private void WriteNode(StructureNode node)
    {
        Span<long> values = stackalloc long[] { node.ParentId, node.Start, node.End, node.NameStart, node.NameEnd,
            node.ValueStart, node.ValueEnd, node.Ordinal, node.ChildCount, node.LastDescendantId, node.NamespaceId, node.Depth, (long)node.Kind };
        Span<byte> bytes = stackalloc byte[RecordSize];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt64LittleEndian(bytes[(i * 8)..], values[i]);
        Transfer(checked(node.Id * RecordSize), bytes, write: true);
    }

    private void Transfer(long offset, Span<byte> data, bool write)
    {
        while (!data.IsEmpty)
        {
            long pageId = offset / PageSize;
            var page = GetPage(pageId);
            int at = (int)(offset % PageSize), length = Math.Min(data.Length, PageSize - at);
            if (write) { data[..length].CopyTo(page.Bytes.AsSpan(at)); page.Dirty = true; }
            else page.Bytes.AsSpan(at, length).CopyTo(data);
            data = data[length..];
            offset += length;
        }
    }

    private Page GetPage(long id)
    {
        if (cache.TryGetValue(id, out var existing))
        {
            recency.Remove(existing.Position);
            recency.AddLast(existing.Position);
            return existing;
        }
        byte[] bytes;
        if (cache.Count == CachePages)
        {
            long oldest = recency.First!.Value;
            var evicted = cache[oldest];
            if (evicted.Dirty) RandomAccess.Write(file, evicted.Bytes, oldest * PageSize);
            cache.Remove(oldest);
            recency.RemoveFirst();
            bytes = evicted.Bytes;
            Array.Clear(bytes);
        }
        else bytes = new byte[PageSize];
        int used = 0;
        while (used < bytes.Length)
        {
            int read = RandomAccess.Read(file, bytes.AsSpan(used), checked(id * PageSize + used));
            if (read == 0) break;
            used += read;
        }
        var page = new Page(bytes, recency.AddLast(id));
        cache.Add(id, page);
        return page;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            cache.Clear();
            recency.Clear();
            file.Dispose();
            Directory.Delete(directory);
        }
    }
}

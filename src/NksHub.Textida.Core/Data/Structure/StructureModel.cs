using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Data.Structure;

public enum StructureFormat { Json, Jsonc, Xml }

public enum StructureKind : byte
{
    Document, JsonObject, JsonArray, JsonString, JsonNumber, JsonBoolean, JsonNull,
    XmlElement, XmlAttribute, XmlNamespace, XmlText, XmlCData, XmlComment, XmlProcessingInstruction,
}

public enum StructureIssueKind : byte { Syntax, DuplicateKey, ForbiddenDtd, UnknownEntity, ResourceLimit, Encoding, UnsupportedSyntax }

public readonly record struct StructureIssue(StructureIssueKind Kind, long Start, long End, string Message);

// Offsets are absolute source bytes; -1 denotes an absent name or namespace.
public readonly record struct StructureNode(
    long Id, long ParentId, StructureKind Kind, long Start, long End,
    long NameStart, long NameEnd, long ValueStart, long ValueEnd,
    long Ordinal, long ChildCount, long LastDescendantId, int Depth, long NamespaceId = -1)
{
    public bool HasName => NameStart >= 0;
    public bool Contains(long offset) => Start <= offset && offset < End;
    public ByteRange Range => new(Start, End);
    public ByteRange ValueRange => new(ValueStart, ValueEnd);
}

public interface IStructureSink
{
    long AddNode(StructureKind kind, long parentId, long start, long nameStart, long nameEnd, long valueStart, long ordinal, int depth);
    void CompleteNode(long id, long valueStart, long valueEnd, long end, long childCount);
    // NamespaceId points to an XmlNamespace node; -2 is the predefined xml namespace.
    void SetNamespace(long id, long namespaceId);
    void Report(StructureIssue issue);
}

public interface IStructureIndex
{
    long Count { get; }
    IByteSnapshot Source { get; }
    TextCodec Codec { get; }
    int BomLength { get; }
    StructureFormat Format { get; }
    Guid DocumentId { get; }
    long Revision { get; }
    StructureNode ReadNode(long id);
}

public sealed class StructureParseContext(IByteSnapshot source, TextCodec codec, int bomLength, IStructureSink sink, CancellationToken cancellationToken, Action<long>? progress = null)
{
    public IByteSnapshot Source { get; } = source;
    public TextCodec Codec { get; } = codec;
    public int BomLength { get; } = bomLength;
    public IStructureSink Sink { get; } = sink;
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public StructureCharReader OpenReader() => new(Source, Codec, BomLength, CancellationToken, progress);
}

public sealed class StructureSyntaxException(StructureIssueKind kind, long start, long end, string message) : Exception(message)
{
    public StructureIssue Issue { get; } = new(kind, start, end, message);
}

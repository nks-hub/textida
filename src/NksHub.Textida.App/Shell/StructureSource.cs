using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Shell;

internal sealed record StructureSource(object Key, Guid DocumentId, long Revision, TextCodec Codec,
    int BomLength, StructureFormat Format, long Offset, bool ReadOnly, Func<DocumentSnapshot> Snapshot,
    Action<ByteRange> Jump, Func<ByteEdit, long, CancellationToken, Task<bool>> Apply,
    Action<ByteRange>? OpenSource = null);

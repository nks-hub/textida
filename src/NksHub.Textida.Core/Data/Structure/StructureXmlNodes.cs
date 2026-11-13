namespace NksHub.Textida.Core.Data.Structure;

internal static class StructureXmlNodes
{
    internal static bool IsText(StructureNode node) => node.Kind is StructureKind.XmlText or StructureKind.XmlCData;

    internal static bool IsXPathNode(IStructureIndex index, StructureNode node, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsText(node))
        {
            // XPath has no text children outside the document element or empty text nodes.
            if (node.ParentId < 0 || index.ReadNode(node.ParentId).Kind != StructureKind.XmlElement) return false;
            if (node.Id > 0 && IsAdjacentText(index.ReadNode(node.Id - 1), node.ParentId)) return false;
            for (var part = node; ;)
            {
                ct.ThrowIfCancellationRequested();
                if (part.ValueEnd > part.ValueStart) return true;
                if (part.Id + 1 >= index.Count) return false;
                var next = index.ReadNode(part.Id + 1);
                if (!IsAdjacentText(next, node.ParentId)) return false;
                part = next;
            }
        }
        return node.Kind switch
        {
            StructureKind.Document or StructureKind.XmlElement or StructureKind.XmlAttribute or StructureKind.XmlComment => true,
            StructureKind.XmlProcessingInstruction => !IsDeclaration(index, node, ct),
            _ => false,
        };
    }

    internal static StructureNode LogicalTextStart(IStructureIndex index, StructureNode node, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsText(node)) return node;
        while (node.Id > 0)
        {
            ct.ThrowIfCancellationRequested();
            var previous = index.ReadNode(node.Id - 1);
            if (!IsAdjacentText(previous, node.ParentId)) break;
            node = previous;
        }
        return node;
    }

    internal static StructureNode LogicalTextEnd(IStructureIndex index, StructureNode node, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!IsText(node)) return node;
        while (node.Id + 1 < index.Count)
        {
            ct.ThrowIfCancellationRequested();
            var next = index.ReadNode(node.Id + 1);
            if (!IsAdjacentText(next, node.ParentId)) break;
            node = next;
        }
        return node;
    }

    private static bool IsAdjacentText(StructureNode node, long parentId) => node.ParentId == parentId && IsText(node);

    private static bool IsDeclaration(IStructureIndex index, StructureNode node, CancellationToken ct)
    {
        long length = node.NameEnd - node.NameStart;
        if (length is < 3 or > 12 || node.NameStart < 0) return false;
        Span<byte> bytes = stackalloc byte[12];
        int done = 0;
        while (done < length)
        {
            ct.ThrowIfCancellationRequested();
            int read = index.Source.Read(node.NameStart + done, bytes.Slice(done, (int)length - done));
            if (read <= 0 || read > length - done) throw new EndOfStreamException("The XML target name is incomplete.");
            done += read;
        }
        return index.Codec.GetString(bytes[..done]) == "xml";
    }
}

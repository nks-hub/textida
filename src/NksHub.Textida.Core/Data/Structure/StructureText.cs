using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

public static class StructureText
{
    public const int MaxChars = 1_048_576;

    public static StructurePreview ReadName(IStructureIndex index, StructureNode node, int maxChars = 256,
        CancellationToken ct = default) => Read(index, node, true, maxChars, ct);

    public static StructurePreview ReadValue(IStructureIndex index, StructureNode node, int maxChars = 256,
        CancellationToken ct = default) => Read(index, node, false, maxChars, ct);

    public static StructurePreview ReadSource(IStructureIndex index, StructureNode node, int maxChars = MaxChars,
        CancellationToken ct = default)
    {
        CheckLimit(maxChars);
        var reader = new StructureCharReader(index.Source, index.Codec, index.BomLength, ct);
        reader.Seek(node.Start);
        var text = new StringBuilder(Math.Min(maxChars, 256));
        while (reader.Position < node.End)
        {
            int value = reader.Read();
            if (value < 0) throw new EndOfStreamException("The source ended inside a node.");
            int width = value > char.MaxValue ? 2 : 1;
            if (text.Length + width > maxChars) return new(text.ToString(), true);
            if (width == 1) text.Append((char)value);
            else text.Append(char.ConvertFromUtf32(value));
        }
        return new(text.ToString(), false);
    }

    private static StructurePreview Read(IStructureIndex index, StructureNode node, bool name, int maxChars, CancellationToken ct)
    {
        CheckLimit(maxChars);
        ct.ThrowIfCancellationRequested();
        return new StructureQuery.ValueReader(index, ct).Preview(node, name, maxChars);
    }

    private static void CheckLimit(int maxChars)
    {
        if (maxChars is < 1 or > MaxChars) throw new ArgumentOutOfRangeException(nameof(maxChars));
    }
}

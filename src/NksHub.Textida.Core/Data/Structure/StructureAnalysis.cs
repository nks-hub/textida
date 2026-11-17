using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Data.Structure;

public sealed class StructureAnalysis : IDisposable
{
    private StructureAnalysis(StructureIndex index, bool valid, long duplicateKeys)
    {
        Index = index;
        IsValid = valid;
        DuplicateKeys = duplicateKeys;
    }

    public StructureIndex Index { get; }
    public bool IsValid { get; }
    public long DuplicateKeys { get; }

    // The caller runs this on a worker and owns the returned temporary index.
    public static StructureAnalysis Parse(DocumentSnapshot snapshot, int bomLength, StructureFormat format,
        CancellationToken cancellationToken, Action<long>? progress = null)
    {
        var index = new StructureIndex(snapshot, bomLength, format);
        try
        {
            var context = new StructureParseContext(snapshot, snapshot.Codec, bomLength, index, cancellationToken, progress);
            try
            {
                switch (format)
                {
                    case StructureFormat.Xml: XmlStructureReader.Parse(context); break;
                    case StructureFormat.Yaml: YamlStructureReader.Parse(context); break;
                    case StructureFormat.Neon: NeonStructureReader.Parse(context); break;
                    default: JsonStructureReader.Parse(context, format == StructureFormat.Jsonc); break;
                }
            }
            catch (StructureSyntaxException error)
            {
                index.Report(error.Issue);
                return new(index, false, 0);
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke(snapshot.ByteLength);
            long duplicates = format == StructureFormat.Xml ? 0 : StructureDuplicates.Analyze(index, cancellationToken);
            bool valid = duplicates == 0 || format is not (StructureFormat.Yaml or StructureFormat.Neon);
            return new(index, valid, duplicates);
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    public void Dispose() => Index.Dispose();
}

using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

public sealed partial class Document
{
    /// <summary>
    /// The name of a virtual document: a view of data a plugin supplies, with no file. Null for every other document. The name decides the language mode
    /// and whether the content is a secrets file (<see cref="SecretsPath"/>).
    /// </summary>
    public string? VirtualName { get; private set; }

    public bool IsVirtual => VirtualName is not null;

    /// <summary>The path that decides whether the values of this document are hidden: its file, or the name of a virtual document.</summary>
    public string? SecretsPath => FilePath ?? VirtualName;

    /// <summary>Creates a virtual document: content in <paramref name="codec"/>, never saved, never counted as modified.</summary>
    public static Document CreateVirtual(string name, ReadOnlyMemory<byte> bytes, IBackendFactory factory, TextCodec codec)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A virtual document needs a name.", nameof(name));
        var doc = FromBytes(bytes, factory, new DocumentOptions { Format = codec });
        doc.VirtualName = name;
        doc.ForgetHistory();
        return doc;
    }

    /// <summary>
    /// Applies the edits the owner of a virtual document makes (a new version of its data). The document keeps no undo history and stays unmodified,
    /// so the user cannot undo the owner's data away or be asked to save it. Ranges refer to <paramref name="baseRevision"/>, or to the current one.
    /// </summary>
    public EditResult ApplyVirtual(IReadOnlyList<ByteEdit> edits, long? baseRevision, string? source)
    {
        if (!IsVirtual) throw new InvalidOperationException("Only a virtual document takes owner edits.");
        var result = Apply(edits, new EditOptions { BaseRevision = baseRevision, Source = source, RawBytes = true });
        if (result.Applied) ForgetHistory();
        return result;
    }

    private void ForgetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        MarkSaved();
    }
}

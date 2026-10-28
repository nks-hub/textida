using NksHub.Textida.App.Services;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// What a view that shows the bytes of a document (the paged and the hex view) knows about hidden values: whether the file is a secrets file by its name,
/// whether the user showed the values in this tab, and where the values lie in the current revision. The editor keeps the same state in characters.
/// </summary>
internal sealed class ByteSecrets(OpenDocument document)
{
    private (long Revision, IReadOnlyList<ByteRange> Ranges) _known = (-1, []);

    /// <summary>The document is a secrets file by its name.</summary>
    public bool IsSecretFile => SecretValues.IsSecretFile(document.Doc.SecretsPath);

    /// <summary>The user chose to show the values in this tab.</summary>
    public bool Revealed { get; private set; }

    /// <summary>The values are hidden now.</summary>
    public bool Hidden => IsSecretFile && !Revealed;

    public void Toggle() => Revealed = !Revealed;

    /// <summary>The hidden ranges of the current revision, found again only after an edit; empty when nothing is hidden.</summary>
    public IReadOnlyList<ByteRange> Ranges()
    {
        if (!Hidden) return [];
        var doc = document.Doc;
        if (_known.Revision != doc.Revision) _known = (doc.Revision, SecretValues.HiddenRanges(doc.GetSnapshot(), doc.BomLength, PluginLimitsPlaceholders.SecretScanMaxBytes));
        return _known.Ranges;
    }

    /// <summary>Whether [<paramref name="start"/>, <paramref name="end"/>) touches a hidden value.</summary>
    public bool Touches(long start, long end) => Hidden && SecretValues.Overlaps(Ranges(), start, end);
}

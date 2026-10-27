using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What a plugin shows through the document API: a virtual document (P6.2.6) as a read only tab, and the diff of two revisions (P6.2.2) in the same comparison tab
/// the editor opens itself, so the user gets the one diff view, with the same keys and scrolling.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Opens a tab for a virtual document. The tab is named <paramref name="title"/>, or after the name of the document.</summary>
    internal DocTab OpenVirtualDocument(Document document, string? title)
    {
        var od = OpenDocument.NewVirtual(document);
        od.TitleOverride = () => title ?? document.VirtualName!;
        Adopt(od);
        return AddView(od, _activeGroup);
    }

    /// <summary>Opens the comparison a plugin asked for. Both sides are the revisions it named, not the documents as they are later.</summary>
    internal DocTab OpenDiffRequest(DiffRequest request)
    {
        CompareSide Side(DiffInput input) => new() { Title = input.Title ?? RevisionTitle(input.Snapshot), Snapshot = input.Snapshot, SnapshotBom = input.Bom };
        return OpenCompare(Side(request.Left), Side(request.Right), request.Title);
    }

    private string RevisionTitle(DocumentSnapshot snapshot) =>
        _app.Windows.SelectMany(w => w.Documents).FirstOrDefault(d => d.Doc.Id == snapshot.DocumentId) is { } open
            ? Loc.Get(K.CompareSideRevision, ("title", open.Title), ("revision", snapshot.Revision))
            : Loc.Get(K.CompareSideRevisionBare, ("revision", snapshot.Revision));
}

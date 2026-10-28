using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What a plugin shows through the document API: a virtual document (P6.2.6) as a tab that is read only, or that the user edits and saves to the plugin, and the diff of two revisions (P6.2.2)
/// in the same comparison tab the editor opens itself, so the user gets the one diff view, with the same keys and scrolling.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>
    /// Opens a tab for a virtual document. The tab is named <paramref name="title"/>, or after the name of the document. With a <paramref name="binding"/> (a document of a plugin) the tab
    /// carries a badge and a bar that name the plugin and the address, and say what a save does.
    /// </summary>
    internal DocTab OpenVirtualDocument(Document document, string? title, VirtualBinding? binding = null)
    {
        var od = OpenDocument.NewVirtual(document);
        od.Virtual = binding;
        od.TitleOverride = () => title ?? document.VirtualName!;
        Adopt(od);
        var tab = AddView(od, _activeGroup);
        if (binding is not null) ShowVirtualBar(tab);
        return tab;
    }

    private void ShowVirtualBar(DocTab tab)
    {
        if (tab.Doc.Virtual is not { } b) return;
        var message = Loc.Get(tab.Doc.Doc.VirtualEditable ? K.VirtualBarEditable : K.VirtualBarReadOnly, ("name", b.PluginName), ("address", b.Address));
        tab.ShowBar(new InfoBar(message, [], onClose: () => tab.ShowBar(null), helpTopic: "plugins.virtual"));
    }

    /// <summary>Closes every view of a virtual document without asking; the plugin closed it or ended, so there is nobody to save it to.</summary>
    internal void CloseVirtualDocument(OpenDocument od)
    {
        bool unsaved = od.HasUnsavedWork;
        string title = od.Title;
        foreach (var tab in od.Views.ToList()) RemoveTab(tab, untrack: true);
        SetStatusMessage(Loc.Get(unsaved ? K.VirtualClosedUnsaved : K.VirtualClosed, ("title", title)));
    }

    /// <summary>
    /// Sends the text of an editable virtual tab to the plugin that supplies it; nothing goes to disk. The tab stays modified when the plugin refuses or cannot be reached,
    /// and text typed while the plugin answered stays unsaved.
    /// </summary>
    private async Task<bool> SaveVirtualAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (!od.Doc.VirtualEditable || od.Virtual is not { } b) return RefuseVirtualSave();
        if (!await ConfirmTypedLossAsync(tab)) return false;
        PrepareForSave(od);
        long revision = od.Doc.Revision;
        var bytes = od.Doc.ReadBytes(new ByteRange(0, od.Doc.ByteLength));
        try
        {
            await _app.Plugins.Documents.VirtualSaveAsync(od.Doc.Id, bytes, revision);
            if (od.Views.Count == 0) return true; // the plugin took it, then ended or closed the tab
            if (od.Doc.Revision == revision) od.Doc.MarkClean();
            od.NotifyChanged();
            SetStatusMessage(Loc.Get(K.VirtualSaved, ("title", od.Title), ("name", b.PluginName)));
            return true;
        }
        catch (SourceException e)
        {
            SetStatusMessage(Loc.Get(K.VirtualSaveFailed, ("title", od.Title), ("name", b.PluginName), ("reason", SourceReason(e))));
            return false;
        }
    }

    /// <summary>The comparison tabs plugins opened with a key, by plugin and key; opening one again takes the place of the tab.</summary>
    private readonly Dictionary<(string Plugin, string Key), DocTab> _keyedDiffs = [];

    /// <summary>
    /// Opens the comparison a plugin asked for. Both sides are the revisions it named, not the documents as they are later. The buttons the plugin added act on the difference the tab is on;
    /// a tab with the same plugin and key as an open one takes its place.
    /// </summary>
    internal DocTab OpenDiffRequest(DiffRequest request)
    {
        CompareSide Side(DiffInput input) => new() { Title = input.Title ?? RevisionTitle(input.Snapshot), Snapshot = input.Snapshot, SnapshotBom = input.Bom };
        (string, string)? keyed = request is { PluginId: { } plugin, Key: { } key } ? (plugin, key) : null;
        TabGroup? group = null;
        int? index = null;
        if (keyed is { } known && _keyedDiffs.Remove(known, out var old) && old.Group is { } oldGroup && oldGroup.Tabs.Contains(old))
        {
            (group, index) = (oldGroup, oldGroup.Tabs.ToList().IndexOf(old));
            RemoveTab(old, untrack: true);
        }
        var tab = OpenCompare(Side(request.Left), Side(request.Right), request.Title, group, index, view =>
        {
            if (request.Actions is not { Count: > 0 } actions) return;
            view.SetActions(actions);
            view.ActionRequested += (action, hunk) => _ = RunDiffActionAsync(view, request, action, hunk);
        });
        if (keyed is { } opened)
        {
            _keyedDiffs[opened] = tab;
            tab.ViewClosed += () => { if (_keyedDiffs.TryGetValue(opened, out var current) && current == tab) _keyedDiffs.Remove(opened); };
        }
        return tab;
    }

    private string RevisionTitle(DocumentSnapshot snapshot) =>
        _app.Windows.SelectMany(w => w.Documents).FirstOrDefault(d => d.Doc.Id == snapshot.DocumentId) is { } open
            ? Loc.Get(K.CompareSideRevision, ("title", open.Title), ("revision", snapshot.Revision))
            : Loc.Get(K.CompareSideRevisionBare, ("revision", snapshot.Revision));
}

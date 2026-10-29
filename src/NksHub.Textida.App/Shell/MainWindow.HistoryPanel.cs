using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.History;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The history panel of a file (P5.3.4): a tab with the revisions of the file, their counts of lines, the comparison of a revision and the buttons to restore it, label it, pin it and
/// save it as a copy. One panel per file; it opens from the File menu, the menu of a tab and the menu of the file panel.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<string, DocTab> _historyPanels = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The history panels of this window.</summary>
    internal IEnumerable<HistoryView> HistoryViews => Tabs.Select(t => t.Custom).OfType<HistoryView>();

    internal HistoryView? ActiveHistory => Active?.Custom as HistoryView;

    private void ApplyHistoryTexts()
    {
        foreach (var view in HistoryViews) view.ApplyTexts();
    }

    /// <summary>"Local History Panel" of the File menu: the panel of the file in the active tab.</summary>
    internal CommandOutcome ShowHistoryPanel()
    {
        if (!HistoryTarget(out var tab, out var path)) return CommandOutcome.Handled;
        OpenHistoryPanel(tab, path);
        return CommandOutcome.Handled;
    }

    /// <summary>The same for the file chosen in the file panel; the file is opened in a tab first, as the panel restores into the tab.</summary>
    internal CommandOutcome ShowHistoryPanelOfSelectedFile()
    {
        if (SelectedFileRow is not { } row) return CommandOutcome.NotHandled;
        if (row.Entry.IsDirectory)
        {
            SetStatusMessage(Loc.Get(K.HistoryPanelNeedsFile));
            return CommandOutcome.Handled;
        }
        OpenHistoryPanel(OpenFile(row.Entry.Path, null), row.Entry.Path);
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the history panel of the file of <paramref name="source"/> as a tab, or goes to the one that is open.</summary>
    internal DocTab OpenHistoryPanel(DocTab source, string path)
    {
        string key = Path.GetFullPath(path);
        if (_historyPanels.TryGetValue(key, out var open) && open.Group is not null)
        {
            SelectTab(open);
            (open.Custom as HistoryView)?.FocusList();
            return open;
        }
        var codec = source.Doc.Doc.Codec;
        DocTab? panel = null;
        void Restore(HistoryRevision r)
        {
            if (source.Group is null)
            {
                SetStatusMessage(Loc.Get(K.HistoryNoEditor));
                return;
            }
            // the restore is an edit of the tab of the file: it is shown for the moment, and the panel comes back with the message of the restore
            SelectTab(source);
            RestoreRevision(source, path, r, section: false);
            string message = _message;
            if (panel?.Group is not null) SelectTab(panel);
            if (message.Length > 0) SetStatusMessage(message);
        }
        var actions = new HistoryPanelActions(source.Paged is null ? Restore : null, r => LabelRevisionAsync(path, r), r => TogglePin(path, r), r => SaveRevisionCopyAsync(path, r));
        var view = new HistoryView(_app.History, path,
            () => source.Group is not null ? DocumentSide(source) : new CompareSide { Title = source.Title, FilePath = path, FileCodec = codec },
            () => Options, () => source.Group is not null ? source.Doc.Doc.Codec : codec, () => CompareIgnore, actions);
        view.Message += SetStatusMessage;
        var od = OpenDocument.NewResults();
        od.TitleOverride = () => Loc.Get(K.HistoryPanelTitle, ("file", Path.GetFileName(path)));
        Adopt(od);
        panel = new DocTab(od, Options, view);
        panel.ViewClosed += () =>
        {
            view.Dispose();
            _historyPanels.Remove(key);
        };
        _historyPanels[key] = panel;
        _activeGroup.Add(panel, null);
        OnActiveTabChanged();
        _app.SessionChanged();
        return panel;
    }
}

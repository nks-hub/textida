using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.History;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The history panel of a file (P5.3.4): a tool window with the revisions of the file, their counts of lines, the comparison of a revision and the buttons to restore it, label it, pin it and
/// save it as a copy. It shows one file at a time (opening it for another file replaces the view); it opens from the File menu, the menu of a tab and the menu of the file panel.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The history panel of this window (none, or the one of the file that was opened last).</summary>
    internal IEnumerable<HistoryView> HistoryViews => _historyView is { } view ? [view] : [];

    /// <summary>The history panel while it is shown, else null.</summary>
    internal HistoryView? ActiveHistory => IsOpen(SidePanel.History) ? _historyView : null;

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

    /// <summary>Shows the history panel for the file of <paramref name="source"/>: the view that is there when it is the same file, else a new one in its place.</summary>
    internal HistoryView OpenHistoryPanel(DocTab source, string path)
    {
        string key = Path.GetFullPath(path);
        if (_historyView is { } open && string.Equals(_historyKey, key, PathComparison))
        {
            TogglePanel(SidePanel.History, true);
            return open;
        }
        var codec = source.Doc.Doc.Codec;
        void Restore(HistoryRevision r)
        {
            if (source.Group is null)
            {
                SetStatusMessage(Loc.Get(K.HistoryNoEditor));
                return;
            }
            // the restore is an edit of the tab of the file; the keyboard comes back to the panel with the message of the restore
            SelectTab(source);
            RestoreRevision(source, path, r, section: false);
            string message = _message;
            FocusPanel(SidePanel.History);
            if (message.Length > 0) SetStatusMessage(message);
        }
        var actions = new HistoryPanelActions(source.Paged is null ? Restore : null, r => LabelRevisionAsync(path, r), r => TogglePin(path, r), r => SaveRevisionCopyAsync(path, r));
        var view = new HistoryView(_app.History, path,
            () => source.Group is not null ? DocumentSide(source) : new CompareSide { Title = source.Title, FilePath = path, FileCodec = codec },
            () => Options, () => source.Group is not null ? source.Doc.Doc.Codec : codec, () => CompareIgnore, actions);
        view.Message += SetStatusMessage;
        var old = _historyView;
        _historyView = view;
        _historyKey = key;
        _historyTool.View = view;
        old?.Dispose();
        ApplyToolTitles();
        TogglePanel(SidePanel.History, true);
        return view;
    }
}

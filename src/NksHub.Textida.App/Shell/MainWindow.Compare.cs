using Avalonia.Controls;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Text compare (P5.2.1): a document against the clipboard, its selection, a file or the file on disk, and two pasted texts. The result is a tab with a
/// <see cref="DiffView"/>; a block can be copied to the side that is a document, as one edit. The same line diff drives the synchronized scrolling
/// of two ordinary text tabs (P5.2.2).
/// </summary>
internal sealed partial class MainWindow
{
    private ScrollSync? _sync;

    internal DiffView? ActiveCompare => Active?.Custom as DiffView;

    /// <summary>The compare tabs of this window.</summary>
    internal IEnumerable<DiffView> CompareViews => Tabs.Select(t => t.Custom).OfType<DiffView>();

    private void ApplyCompareTexts()
    {
        foreach (var view in CompareViews) view.ApplyTexts();
    }

    /// <summary>
    /// The context menu of a text view: clipboard commands and the compares, taken from the <c>editor.context</c> menu of the manifest. Its items are
    /// built when it opens, so they follow the language and the state of the commands.
    /// </summary>
    private void AttachEditorMenu(DocTab tab)
    {
        Control? view = (Control?)tab.Surface ?? tab.Paged;
        if (view is null) return;
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            var wb = _app.Workbench;
            var context = new ShellContext(this, forMenu: true);
            menu.Items.Clear();
            foreach (var item in MenuRenderer.ToContextItems(MenuBuilder.Nodes(wb, context, CheckState, id => _ = wb.Commands.ExecuteAsync(id, context), "editor.context")))
                menu.Items.Add(item);
        };
        view.ContextMenu = menu;
    }

    private DocTab? CompareSource() => Active is { Custom: null, Hex: null } tab && !tab.Doc.IsResults ? tab : null;

    /// <summary>Opens a compare tab for two sides and starts the comparison.</summary>
    internal DocTab OpenCompare(CompareSide left, CompareSide right, string? title = null)
    {
        var session = new CompareSession(left, right) { Ignore = CompareIgnore };
        var view = new DiffView(session, Options) { ReadClipboard = async () => Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null };
        view.Refused += SetStatusMessage;
        view.ViewChanged += () => { if (ActiveCompare == view) UpdateStatus(); };
        var od = OpenDocument.NewResults();
        od.TitleOverride = () => title ?? Loc.Get(K.CompareTab, ("left", left.Title), ("right", right.Title));
        Adopt(od);
        var tab = new DocTab(od, Options, view);
        tab.ViewClosed += session.Dispose;
        _activeGroup.Add(tab);
        OnActiveTabChanged();
        _app.SessionChanged();
        return tab;
    }

    private CompareSide DocumentSide(DocTab tab) => new() { Title = tab.Title, Document = tab.Doc };

    private CompareSide FileSide(string path, Core.Encodings.TextCodec? codec = null) =>
        new() { Title = System.IO.Path.GetFileName(path), FilePath = path, FileCodec = codec };

    /// <summary>The text of the selection when the tab has one in the text surface; null otherwise (a paged view compares the whole file).</summary>
    private static string? SelectionOf(DocTab tab) => tab.Surface is { HasSelection: true } s && s.SelectedText is { Length: > 0 } text ? text : null;

    /// <summary>Compares the active tab, or its selection, with the text on the clipboard.</summary>
    internal async Task<DocTab?> CompareWithClipboardAsync()
    {
        if (CompareSource() is not { } tab)
        {
            SetStatusMessage(Loc.Get(K.CompareNoDocument));
            return null;
        }
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text))
        {
            SetStatusMessage(Loc.Get(K.CompareClipboardEmpty));
            return null;
        }
        var left = SelectionOf(tab) is { } selected
            ? new CompareSide { Title = Loc.Get(K.CompareSideSelection, ("title", tab.Title)), Text = selected }
            : DocumentSide(tab);
        return OpenCompare(left, new CompareSide { Title = Loc.Get(K.CompareSideClipboard), Text = text });
    }

    /// <summary>Compares the active tab with a file the user picks.</summary>
    internal async Task<DocTab?> CompareWithFileAsync()
    {
        if (CompareSource() is not { } tab)
        {
            SetStatusMessage(Loc.Get(K.CompareNoDocument));
            return null;
        }
        var picked = await _app.Dialogs.PickFilesAsync(this);
        return picked.Count == 0 ? null : CompareWithFile(tab, picked[0]);
    }

    internal DocTab? CompareWithFile(DocTab tab, string path)
    {
        if (!File.Exists(path))
        {
            SetStatusMessage(Loc.Get(K.ErrorOpenFailed, ("path", path), ("reason", new FileNotFoundException().Message)));
            return null;
        }
        return OpenCompare(DocumentSide(tab), FileSide(System.IO.Path.GetFullPath(path)));
    }

    /// <summary>Compares two files from the command line: the left one is opened in a tab (and can take blocks of the right one), the right one is read-only.</summary>
    internal DocTab CompareFiles(string left, string right)
    {
        right = System.IO.Path.GetFullPath(right);
        if (!File.Exists(right)) throw new FileNotFoundException(Loc.Get(K.ErrorOpenFailed, ("path", right), ("reason", new FileNotFoundException().Message)), right);
        return OpenCompare(DocumentSide(OpenFile(left, null)), FileSide(right));
    }

    /// <summary>The built-in comparison of a tab with its file on disk, used while no external tool is set.</summary>
    private DocTab CompareWithDiskBuiltIn(DocTab tab, string path) =>
        OpenCompare(DocumentSide(tab), new CompareSide { Title = Loc.Get(K.CompareSideDisk, ("title", tab.Title)), FilePath = path, FileCodec = tab.Doc.Doc.Codec });

    /// <summary>Two empty sides to paste two texts into.</summary>
    internal DocTab OpenTextCompare() =>
        OpenCompare(new CompareSide { Title = Loc.Get(K.CompareSideLeft), Pasteable = true }, new CompareSide { Title = Loc.Get(K.CompareSideRight), Pasteable = true });

    internal CommandOutcome CompareStep(bool next) =>
        ActiveCompare is not { } view ? CommandOutcome.NotHandled : (next ? view.NextDifference() : view.PreviousDifference()) ? CommandOutcome.Handled : CommandOutcome.NotHandled;

    internal CommandOutcome CompareCopy(Core.Compare.DiffSide target) =>
        ActiveCompare is not { CurrentHunk: >= 0 } view ? CommandOutcome.NotHandled : view.CopyCurrent(target) ? CommandOutcome.Handled : CommandOutcome.NotHandled;

    /// <summary>What compares do not tell apart, from the settings; a change of them reaches the open compare tabs through <see cref="ApplyLanguageOptions"/>.</summary>
    internal DiffIgnore CompareIgnore =>
        (_app.Workbench.Settings.GetBool("compare.ignoreWhitespace") ? DiffIgnore.Whitespace : DiffIgnore.None)
        | (_app.Workbench.Settings.GetBool("compare.ignoreCase") ? DiffIgnore.Case : DiffIgnore.None);

    internal void ToggleCompareIgnoreWhitespace() => ToggleSetting("compare.ignoreWhitespace");

    internal void ToggleCompareIgnoreCase() => ToggleSetting("compare.ignoreCase");

    // ---- synchronized scrolling ----

    internal bool SyncScrollOn => _sync is not null;

    private static IMinimapHost? HostOf(DocTab? tab) => tab is { Custom: null, Hex: null, TableShown: false } ? (IMinimapHost?)tab.Surface ?? tab.Paged : null;

    /// <summary>
    /// Switches synchronized scrolling between the active tab and the active tab of the next group on or off. It needs two groups, each showing a text view.
    /// </summary>
    internal CommandOutcome ToggleSyncScroll()
    {
        if (_sync is not null)
        {
            StopSyncScroll();
            SetStatusMessage(Loc.Get(K.CompareSyncOff));
            return CommandOutcome.Handled;
        }
        var other = _groups.Count > 1 ? _groups[(_groups.IndexOf(_activeGroup) + 1) % _groups.Count].Active : null;
        if (Active is not { } mine || HostOf(mine) is not { } hostMine || HostOf(other) is not { } hostOther)
        {
            SetStatusMessage(Loc.Get(K.CompareSyncNeed));
            return CommandOutcome.Handled;
        }
        var sync = _sync = new ScrollSync(hostMine, hostOther, () => DiffSource.Of(mine.Doc.Doc), () => DiffSource.Of(other!.Doc.Doc));
        void Changed() => sync.Invalidate();
        foreach (var tab in new[] { mine, other! })
        {
            tab.Doc.Changed += Changed;
            tab.Doc.Replaced += Changed;
            _syncCleanup += () => { tab.Doc.Changed -= Changed; tab.Doc.Replaced -= Changed; };
            tab.ViewClosed += () => { if (ReferenceEquals(_sync, sync)) StopSyncScroll(); };
        }
        _ = sync.RefreshAsync();
        SetStatusMessage(Loc.Get(K.CompareSyncOn, ("title", other!.Title)));
        return CommandOutcome.Handled;
    }

    private Action? _syncCleanup;

    private void StopSyncScroll()
    {
        _syncCleanup?.Invoke();
        _syncCleanup = null;
        _sync?.Dispose();
        _sync = null;
    }
}

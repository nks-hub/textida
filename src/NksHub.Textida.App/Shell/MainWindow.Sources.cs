using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Files served by a plugin for a URI scheme (P6.2.1, P6.2.3). The text opens in an ordinary tab without a file of its own; the bar above it says where it comes from, at which
/// revision, what the source can do, and whether it is the cached copy of an unreachable source. A save is conditional on that revision: when the source moved on, nothing is
/// written and the user gets the same choice as for a file changed on disk (differences, overwrite, leave). Everything shown or logged carries the URI masked.
/// A source that searches takes words instead of an address, and the log of every access states the adapter and the service the source named.
/// </summary>
internal sealed partial class MainWindow
{
    private DocumentApi SourceApi => _app.Plugins.Documents;

    private void SourceLog(FileSource source, string line) => _app.Output.Append(OutputChannel.Remote, source.Name, line);

    private static string SourceReason(SourceException e) => UriMask.MaskText(e.Message);

    private static string DescribeSource(FileSource source)
    {
        var caps = source.Capabilities;
        string write = Loc.Get(!caps.Write ? K.SourceWriteNone : caps.AtomicWrite ? K.SourceWriteAtomic : K.SourceWritePlain);
        return Loc.Get(K.SourceCapabilities, ("scheme", source.Scheme), ("write", write), ("list", Loc.Get(caps.List ? K.SourceListYes : K.SourceListNo)))
            + (caps.Search ? ", " + Loc.Get(K.SourceSearchYes) : "") + SourceVersions(source);
    }

    /// <summary>The adapter and service versions the source named, as a parenthesis to append to a line; empty when it named none.</summary>
    private static string SourceVersions(FileSource source) =>
        source.AdapterVersion is null && source.ServiceVersion is null ? "" : " (" + Loc.Get(K.SourceVersions, ("adapter", source.AdapterVersion ?? "-"), ("service", source.ServiceVersion ?? "-")) + ")";

    // ---- opening ----

    internal CommandOutcome OpenFromSource()
    {
        _ = OpenFromSourceAsync();
        return CommandOutcome.Handled;
    }

    private async Task OpenFromSourceAsync()
    {
        await _app.Plugins.ActivateSourcesAsync();
        var sources = SourceApi.Sources;
        if (sources.Count == 0) { SetStatusMessage(Loc.Get(K.SourceNone)); return; }
        var pick = await PickAsync(Loc.Get(K.SourceWatermark), Loc.Get(K.SourceA11y), [.. sources.Select(s => (s.Name, (string?)DescribeSource(s), s))]);
        if (!pick.Chosen) return;
        var source = pick.Value!;
        bool searches = source.Capabilities.Search;
        string? text = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.SourceOpenTitle), Loc.Get(searches ? K.SourceOpenPromptSearch : K.SourceOpenPrompt, ("scheme", source.Scheme)), searches ? "" : source.Scheme + "://"))?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (searches && !text.Contains("://", StringComparison.Ordinal)) await SearchSourceAsync(source, text);
        else await OpenSourceAsync(text);
    }

    /// <summary>Asks the source for the pages that match the words and opens the one the user picks.</summary>
    private async Task SearchSourceAsync(FileSource source, string query)
    {
        IReadOnlyList<SourceEntry> found;
        try { found = [.. (await SourceApi.SourceSearchAsync(source.Scheme, query)).Where(e => !e.IsDirectory)]; }
        catch (SourceException e)
        {
            SourceLog(source, Loc.Get(K.SourceLogFailed, ("uri", source.Scheme + "://"), ("reason", SourceReason(e))));
            SetStatusMessage(Loc.Get(K.SourceSearchFailed, ("name", source.Name), ("reason", SourceReason(e))));
            return;
        }
        if (found.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.SourceSearchNone, ("query", query), ("name", source.Name)));
            return;
        }
        var chosen = await PickAsync(Loc.Get(K.SourceSearchWatermark, ("query", query)), Loc.Get(K.SourceSearchA11y), [.. found.Select(e => (e.Name, (string?)UriMask.Mask(e.Uri), e.Uri))]);
        if (chosen.Chosen) await OpenSourceAsync(chosen.Value!);
    }

    /// <summary>Opens the file at <paramref name="uri"/> in a tab, or selects the tab that already shows it. Null (and a message in the status bar) when no plugin serves it or it cannot be read.</summary>
    internal async Task<DocTab?> OpenSourceAsync(string uri)
    {
        string masked = UriMask.Mask(uri);
        if (Tabs.FirstOrDefault(t => t.Doc.Source?.Uri == uri) is { } open)
        {
            SelectTab(open);
            FocusView(open);
            return open;
        }
        if (SourceApi.SourceFor(uri) is null) await _app.Plugins.ActivateSourcesAsync(); // a recent file may name a scheme whose plugin has not started yet
        if (SourceApi.SourceFor(uri) is not { } source)
        {
            SetStatusMessage(Loc.Get(K.SourceNoPlugin, ("uri", masked)));
            return null;
        }
        SourceContent read;
        try { read = await SourceApi.SourceReadAsync(uri); }
        catch (SourceException e)
        {
            SourceLog(source, Loc.Get(K.SourceLogFailed, ("uri", masked), ("reason", SourceReason(e))));
            SetStatusMessage(Loc.Get(K.SourceOpenFailed, ("uri", masked), ("reason", SourceReason(e))));
            return null;
        }
        _app.Recent.Add(uri);
        string name = UriMask.DisplayName(uri);
        var od = OpenDocument.FromSource(read.Content, name);
        od.Source = new SourceBinding(uri, source, read.Revision, read.FromCache, read.CachedAt);
        od.TitleOverride = () => name;
        Adopt(od);
        var tab = AddView(od, _activeGroup);
        od.Doc.MarkClean(); // the view loading its text must not count as an edit
        ShowSourceBar(tab);
        FocusView(tab);
        SourceLog(source, Loc.Get(read.FromCache ? K.SourceLogCached : K.SourceLogOpened, ("uri", masked), ("revision", read.Revision)) + SourceVersions(source));
        SetStatusMessage(Loc.Get(K.SourceOpened, ("uri", masked), ("name", source.Name)));
        return tab;
    }

    private void ShowSourceBar(DocTab tab)
    {
        if (tab.Doc.Source is not { } b) return;
        var caps = b.Source.Capabilities;
        string write = Loc.Get(!caps.Write ? K.SourceWriteNone : caps.AtomicWrite ? K.SourceWriteAtomic : K.SourceWritePlain);
        string message = b.FromCache
            ? Loc.Get(K.SourceBarOffline, ("name", b.Source.Name), ("uri", b.MaskedUri), ("revision", b.Revision ?? "-"), ("time", b.CachedAt?.ToLocalTime().ToString("g") ?? "-"))
            : Loc.Get(K.SourceBarOnline, ("name", b.Source.Name), ("uri", b.MaskedUri), ("revision", b.Revision ?? "-"), ("write", write));
        tab.ShowBar(new InfoBar(message, [(Loc.Get(K.SourceBarReload), () => _ = ReloadFromSourceAsync(tab))], warning: b.FromCache || (caps.Write && !caps.AtomicWrite), onClose: () => tab.ShowBar(null), helpTopic: "source.open"));
    }

    /// <summary>Reads the file again and replaces the text. Never discards unsaved changes.</summary>
    private async Task ReloadFromSourceAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (od.Source is not { } b) return;
        if (od.Doc.IsModified) { SetStatusMessage(Loc.Get(K.SourceReloadUnsaved)); return; }
        try
        {
            var read = await SourceApi.SourceReadAsync(b.Uri);
            if (od.Doc.IsModified) return; // typed while the source answered
            od.Doc.Replace(new ByteRange(0, od.Doc.ByteLength), read.Content);
            od.Doc.MarkClean();
            (b.Revision, b.FromCache, b.CachedAt) = (read.Revision, read.FromCache, read.CachedAt);
            od.NotifyChanged();
            ShowSourceBar(tab);
        }
        catch (SourceException e)
        {
            SourceLog(b.Source, Loc.Get(K.SourceLogFailed, ("uri", b.MaskedUri), ("reason", SourceReason(e))));
            SetStatusMessage(Loc.Get(K.SourceOpenFailed, ("uri", b.MaskedUri), ("reason", SourceReason(e))));
        }
    }

    // ---- saving ----

    /// <summary>Writes the text to the source against the revision it was read at. A source that cannot write atomically is warned about first.</summary>
    private async Task<bool> SaveSourceAsync(DocTab tab)
    {
        var od = tab.Doc;
        var b = od.Source!;
        var caps = b.Source.Capabilities;
        if (!caps.Write)
        {
            SetStatusMessage(Loc.Get(K.SourceReadOnly, ("name", b.Source.Name)));
            return false;
        }
        if (!await ConfirmTypedLossAsync(tab)) return false;
        if (!caps.AtomicWrite)
        {
            var warn = new ChoiceRequest(Loc.Get(K.SourcePlainTitle), Loc.Get(K.SourcePlainWarning, ("name", b.Source.Name), ("uri", b.MaskedUri)),
                [new DialogButton(Loc.Get(K.SourcePlainWrite)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, warn) != 0) return false;
        }
        PrepareForSave(od);
        long revision = od.Doc.Revision;
        var bytes = od.Doc.ReadBytes(new ByteRange(0, od.Doc.ByteLength));
        try
        {
            b.Revision = await SourceApi.SourceWriteAsync(b.Uri, bytes, b.Revision);
            b.FromCache = false;
            b.CachedAt = null;
            if (od.Doc.Revision == revision) od.Doc.MarkClean(); // text typed while the source answered stays unsaved
            od.NotifyChanged();
            ShowSourceBar(tab);
            SourceLog(b.Source, Loc.Get(K.SourceLogSaved, ("uri", b.MaskedUri), ("revision", b.Revision)) + SourceVersions(b.Source));
            SetStatusMessage(Loc.Get(K.SourceSaved, ("title", od.Title), ("name", b.Source.Name)));
            _app.SessionChanged();
            return true;
        }
        catch (SourceException e) when (e.Kind == SourceFailure.Conflict)
        {
            return await ResolveSourceConflictAsync(tab, e);
        }
        catch (SourceException e)
        {
            SourceLog(b.Source, Loc.Get(K.SourceLogFailed, ("uri", b.MaskedUri), ("reason", SourceReason(e))));
            SetStatusMessage(Loc.Get(K.SourceSaveFailed, ("title", od.Title), ("reason", SourceReason(e))));
            return false;
        }
    }

    /// <summary>Nothing was written and the local text is untouched. The user looks at the differences, saves over exactly the version found, or leaves it.</summary>
    private async Task<bool> ResolveSourceConflictAsync(DocTab tab, SourceException conflict)
    {
        var od = tab.Doc;
        var b = od.Source!;
        bool gone = conflict.CurrentRevision is null;
        SourceLog(b.Source, Loc.Get(K.SourceLogConflict, ("uri", b.MaskedUri)) + SourceVersions(b.Source));
        var kinds = new List<int>();
        var buttons = new List<DialogButton>();
        if (!gone) { buttons.Add(new DialogButton(Loc.Get(K.SourceConflictDifferences), IsDefault: true)); kinds.Add(0); }
        buttons.Add(new DialogButton(Loc.Get(K.SourceConflictOverwrite))); kinds.Add(1);
        buttons.Add(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true, IsDefault: gone)); kinds.Add(2);
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.SourceConflictTitle),
            Loc.Get(gone ? K.SourceConflictGone : K.SourceConflictMessage, ("title", od.Title), ("name", b.Source.Name)), buttons));
        switch (answer < 0 || answer >= kinds.Count ? 2 : kinds[answer])
        {
            case 0:
                try
                {
                    var read = await SourceApi.SourceReadAsync(b.Uri);
                    if (read.FromCache) { SetStatusMessage(Loc.Get(K.SourceConflictUnreachable)); return false; }
                    b.Revision = read.Revision; // the next save replaces exactly the version the user is shown
                    string text = od.Doc.Codec.GetString(read.Content).TrimStart('﻿');
                    OpenCompare(new CompareSide { Title = Loc.Get(K.SourceCompareRemote, ("title", od.Title), ("name", b.Source.Name)), Text = text }, DocumentSide(tab));
                    SetStatusMessage(Loc.Get(K.SourceConflictShown));
                }
                catch (SourceException e) { SetStatusMessage(Loc.Get(K.SourceOpenFailed, ("uri", b.MaskedUri), ("reason", SourceReason(e)))); }
                return false;
            case 1:
                b.Revision = conflict.CurrentRevision;
                return await SaveSourceAsync(tab);
            default:
                return false;
        }
    }
}

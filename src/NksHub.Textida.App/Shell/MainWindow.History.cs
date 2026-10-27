using System.Globalization;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.History;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Local history of the file in the active tab: the list of revisions, compare with a revision in the external diff tool, restore of the whole file or of the
/// selected lines (an undoable edit; the text it replaces is kept as a revision first), labels, and search in older revisions.
/// </summary>
internal sealed partial class MainWindow
{
    private const int HistoryPreviewBytes = 4000;

    /// <summary>The pin owner of a revision that the user pinned by hand; other owners (a pending review) are not touched by it.</summary>
    private const string UserPinOwner = "user";

    private bool HistoryTarget(out DocTab tab, out string path)
    {
        tab = null!;
        path = "";
        if (Active is not { } active || active.Doc.Path is not { } p)
        {
            SetStatusMessage(Loc.Get(K.HistoryNoFile));
            return false;
        }
        tab = active;
        path = p;
        return true;
    }

    private static string HistoryWhen(HistoryRevision r) => r.TimeUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static string HistorySourceText(HistorySource s) => Loc.Get(s switch
    {
        HistorySource.Save => K.HistorySourceSave,
        HistorySource.External => K.HistorySourceExternal,
        HistorySource.Manual => K.HistorySourceManual,
        HistorySource.Initial => K.HistorySourceInitial,
        _ => K.HistorySourceBeforeRestore,
    });

    private QuickRow HistoryRow(DocTab tab, string path, HistoryRevision r, string? detail, Action run)
    {
        var when = HistoryWhen(r);
        detail ??= string.Join(" · ", new[] { HistorySourceText(r.Source), OpenDocument.FormatSize(r.Size), r.Label, r.Pinned ? Loc.Get(K.HistoryPinnedTag) : null }.Where(s => !string.IsNullOrEmpty(s)));
        return new QuickRow(r.Label is { Length: > 0 } ? $"{when}  {r.Label}" : when, null, detail, null, true, null, $"{when}, {detail}", run, () => HistoryPreview(tab, path, r));
    }

    private string HistoryPreview(DocTab tab, string path, HistoryRevision r)
    {
        try
        {
            var bytes = _app.History.ReadPrefix(path, r.Id, HistoryPreviewBytes);
            var codec = tab.Doc.Doc.Codec;
            return codec.GetString(bytes.AsSpan(0, codec.CompleteLength(bytes))).TrimStart('﻿');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return e.Message; }
    }

    // ---- list ----

    internal CommandOutcome ShowHistory()
    {
        if (!HistoryTarget(out var tab, out var path)) return CommandOutcome.Handled;
        List<QuickRow> rows;
        try { rows = _app.History.List(path).Select(r => HistoryRow(tab, path, r, null, () => _ = HistoryActionsAsync(tab, path, r))).ToList(); }
        catch (HistoryFormatException e) { ReportDamagedStore(e); return CommandOutcome.Handled; }
        ShowChoice(Loc.Get(K.HistoryWatermark), Loc.Get(K.HistoryA11y), rows, emptyText: Loc.Get(K.HistoryEmpty));
        return CommandOutcome.Handled;
    }

    private async Task HistoryActionsAsync(DocTab tab, string path, HistoryRevision r)
    {
        var buttons = new List<DialogButton>
        {
            new(Loc.Get(K.HistoryCompare), IsDefault: true), new(Loc.Get(K.HistoryRestoreFile)), new(Loc.Get(K.HistoryRestoreSection)),
            new(Loc.Get(K.HistorySetLabel)), new(Loc.Get(r.Pins.Contains(UserPinOwner) ? K.HistoryUnpin : K.HistoryPin)), new(Loc.Get(K.DialogCancel), IsCancel: true),
        };
        var details = r.Label is { Length: > 0 } label ? new[] { Loc.Get(K.HistoryActionsLabel, ("label", label)) } : null;
        var request = new ChoiceRequest(Loc.Get(K.HistoryActionsTitle, ("time", HistoryWhen(r))),
            Loc.Get(K.HistoryActionsMessage, ("source", HistorySourceText(r.Source)), ("size", OpenDocument.FormatSize(r.Size))), buttons, details);
        switch (await _app.Dialogs.ChooseAsync(this, request))
        {
            case 0: CompareWithRevision(tab, path, r); break;
            case 1: RestoreRevision(tab, path, r, section: false); break;
            case 2: RestoreRevision(tab, path, r, section: true); break;
            case 3: await LabelRevisionAsync(path, r); break;
            case 4: TogglePin(path, r); break;
        }
    }

    private void TogglePin(string path, HistoryRevision r)
    {
        bool pin = !r.Pins.Contains(UserPinOwner);
        try
        {
            if (pin) _app.History.Pin(path, r.Id, UserPinOwner); else _app.History.Unpin(path, r.Id, UserPinOwner);
            SetStatusMessage(Loc.Get(pin ? K.HistoryPinned : K.HistoryUnpinned));
        }
        catch (HistoryFormatException e) { ReportDamagedStore(e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message))); }
    }

    private void ReportDamagedStore(HistoryFormatException e) => SetStatusMessage(Loc.Get(K.HistoryStoreDamaged, ("reason", e.Message)));

    private async Task LabelRevisionAsync(string path, HistoryRevision r)
    {
        var label = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HistoryLabelTitle), Loc.Get(K.HistoryLabelPrompt), r.Label ?? "");
        if (label is null) return;
        try { _app.History.SetLabel(path, r.Id, label); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message))); }
    }

    // ---- compare ----

    /// <summary>
    /// The revision on the left, the content of the tab on the right: in the built-in compare tab, or in the external diff tool when one is set
    /// (the revision then goes to a temporary file).
    /// </summary>
    internal void CompareWithRevision(DocTab tab, string path, HistoryRevision r)
    {
        var settings = _app.Workbench.Settings;
        var tool = settings.GetString("tools.diffTool");
        if (string.IsNullOrWhiteSpace(tool))
        {
            try
            {
                string text = tab.Doc.Doc.Codec.GetString(_app.History.Read(path, r.Id)).TrimStart('﻿');
                string title = Path.GetFileName(path) + " · " + (r.Label is { Length: > 0 } label ? label : r.TimeUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture));
                OpenCompare(new CompareSide { Title = title, Text = text }, DocumentSide(tab));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message)));
            }
            return;
        }
        var dir = Path.Combine(Path.GetTempPath(), "textida-revision-" + Guid.NewGuid().ToString("N")[..12]);
        void Cleanup()
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the OS cleans the temp folder later */ }
        }
        try
        {
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, r.Id + "-" + Path.GetFileName(path));
            _app.History.Export(path, r.Id, file);
            var started = ExternalDiff.Compare(tab.Doc.Doc, file, tool, settings.GetString("tools.diffArguments"));
            if (!started.Ok)
            {
                Cleanup();
                SetStatusMessage(Loc.Get(K.CompareFailed, ("reason", started.Problem ?? "")));
            }
            else _ = started.Completion.ContinueWith(_ => Cleanup(), TaskScheduler.Default);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Cleanup();
            SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message)));
        }
    }

    // ---- restore ----

    private void RestoreRevision(DocTab tab, string path, HistoryRevision r, bool section)
    {
        if (!ReferenceEquals(Active, tab) || ActiveEditor is not { IsReadOnly: false } surface) { SetStatusMessage(Loc.Get(K.HistoryNoEditor)); return; }
        string revisionText;
        try
        {
            var codec = tab.Doc.Doc.Codec;
            revisionText = codec.GetString(_app.History.Read(path, r.Id)).TrimStart('﻿');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message)));
            return;
        }

        var doc = surface.Document;
        int from = 0, to = doc.TextLength;
        string replacement = revisionText;
        if (section)
        {
            var sels = surface.GetSelections();
            int start = sels.Min(s => s.Start), end = sels.Max(s => s.End);
            if (start == end) { SetStatusMessage(Loc.Get(K.HistoryNoSelection)); return; }
            int firstLine = doc.GetLineByOffset(start).LineNumber - 1;
            var last = doc.GetLineByOffset(end);
            int lastLine = end == last.Offset ? last.LineNumber - 1 : last.LineNumber; // exclusive
            var restored = HistoryRestore.RestoreSection(doc.Text, revisionText, firstLine, lastLine);
            if (restored is null) { SetStatusMessage(Loc.Get(K.HistoryTooBig)); return; }
            from = firstLine < doc.LineCount ? doc.GetLineByNumber(firstLine + 1).Offset : doc.TextLength;
            to = lastLine < doc.LineCount ? doc.GetLineByNumber(lastLine + 1).Offset : doc.TextLength;
            replacement = restored.Substring(from, restored.Length - from - (doc.TextLength - to));
        }

        RecordBeforeRestore(tab, path);
        RunEdit((s, sels) =>
        {
            s.Document.Replace(from, to - from, replacement);
            return [Sel.At(Math.Min(from, s.Document.TextLength))];
        });
        SetStatusMessage(Loc.Get(section ? K.HistoryRestoredSection : K.HistoryRestoredFile, ("time", HistoryWhen(r))));
    }

    /// <summary>Keeps the text that a restore replaces: it may never have been saved.</summary>
    private void RecordBeforeRestore(DocTab tab, string path)
    {
        using var ms = new MemoryStream();
        tab.Doc.Doc.Backend.CopyTo(ms);
        ReportHistory(RecordSafely(path, ms.ToArray(), HistorySource.BeforeRestore, null));
    }

    /// <summary>A store that cannot be opened (damaged marker, newer format) is reported as a failed record instead of an exception.</summary>
    private HistoryRecordResult RecordSafely(string path, byte[] content, HistorySource source, string? label)
    {
        try { return _app.History.Record(path, content, source, label); }
        catch (HistoryFormatException e) { return new HistoryRecordResult(HistoryStatus.Failed, null, e.Message); }
    }

    private void ReportHistory(HistoryRecordResult result)
    {
        if (result.Status == HistoryStatus.TooLarge)
        {
            SetStatusMessage(Loc.Get(K.HistoryTooLarge, ("size", OpenDocument.FormatSize(_app.History.Policy.MaxRevisionBytes ?? 0))));
            return;
        }
        if (result.Status == HistoryStatus.Failed) SetStatusMessage(result.NoSpace ? Loc.Get(K.HistoryNoSpace) : Loc.Get(K.HistoryFailed, ("reason", result.Problem ?? "")));
        else if (result.Status == HistoryStatus.Skipped) SetStatusMessage(Loc.Get(K.HistorySkipped));
    }

    // ---- mark ----

    internal CommandOutcome MarkRevision()
    {
        if (!HistoryTarget(out var tab, out var path)) return CommandOutcome.Handled;
        _ = MarkRevisionAsync(tab, path);
        return CommandOutcome.Handled;
    }

    private async Task MarkRevisionAsync(DocTab tab, string path)
    {
        var label = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.HistoryMarkTitle), Loc.Get(K.HistoryMarkPrompt), ""))?.Trim();
        if (string.IsNullOrEmpty(label)) return;
        using var ms = new MemoryStream();
        tab.Doc.Doc.Backend.CopyTo(ms);
        var result = RecordSafely(path, ms.ToArray(), HistorySource.Manual, label);
        try
        {
            // the newest revision already holds this text: the label goes to it
            if (result.Status == HistoryStatus.Unchanged && result.Revision is { } same) _app.History.SetLabel(path, same.Id, label);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.HistoryFailed, ("reason", e.Message))); return; }
        if (result.Status is HistoryStatus.Recorded or HistoryStatus.Unchanged) SetStatusMessage(Loc.Get(K.HistoryMarked, ("label", label)));
        else ReportHistory(result);
    }

    // ---- search ----

    internal CommandOutcome SearchHistory()
    {
        if (!HistoryTarget(out var tab, out var path)) return CommandOutcome.Handled;
        _ = SearchHistoryAsync(tab, path);
        return CommandOutcome.Handled;
    }

    private async Task SearchHistoryAsync(DocTab tab, string path)
    {
        var text = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HistorySearchTitle), Loc.Get(K.HistorySearchPrompt), "");
        if (string.IsNullOrEmpty(text)) return;
        IReadOnlyList<HistorySearchHit> hits;
        try { hits = await _app.History.SearchAsync(path, new SearchPattern(text, CaseSensitive: false)); }
        catch (HistoryFormatException e) { ReportDamagedStore(e); return; }
        if (hits.Count == 0) { SetStatusMessage(Loc.Get(K.HistorySearchNone)); return; }
        var rows = hits.Select(h => HistoryRow(tab, path, h.Revision,
            Loc.Get(K.HistorySearchDetail, ("matches", h.Matches), ("line", h.FirstLine), ("preview", h.Preview.Trim())), () => CompareWithRevision(tab, path, h.Revision))).ToList();
        ShowChoice(Loc.Get(K.HistorySearchWatermark), Loc.Get(K.HistoryA11y), rows, emptyText: Loc.Get(K.HistorySearchNone));
    }
}

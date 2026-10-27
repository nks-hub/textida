using System.Text;
using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Shell;

internal enum UnsavedAnswer { Cancel, Save, Discard, KeepInJournal }

/// <summary>Answer of the loss inspector: write nothing, write in another encoding, or write the replacement bytes.</summary>
internal enum LossAnswer { Cancel, ChangeEncoding, Replace }

internal sealed partial class MainWindow
{
    private string _message = "";
    private readonly Dictionary<string, InfoBar> _noticeByKey = [];

    // ---- notices ----

    /// <summary>Shows a window-level bar; a bar with the same key is replaced.</summary>
    internal InfoBar ShowNotice(string key, string message, IReadOnlyList<(string Label, Action Click)>? actions = null, bool warning = false)
    {
        DismissNotice(key);
        var bar = new InfoBar(message, actions ?? [], warning, () => DismissNotice(key));
        _noticeByKey[key] = bar;
        _notices.Children.Add(bar);
        return bar;
    }

    internal void DismissNotice(string key)
    {
        if (_noticeByKey.Remove(key, out var bar)) _notices.Children.Remove(bar);
    }

    internal IReadOnlyCollection<InfoBar> Notices => _noticeByKey.Values;

    // ---- questions ----

    private async Task<UnsavedAnswer> AskUnsavedAsync(OpenDocument od)
    {
        var buttons = new List<DialogButton> { new(Loc.Get(K.DialogSave), IsDefault: true), new(Loc.Get(K.DialogDontSave)) };
        var map = new List<UnsavedAnswer> { UnsavedAnswer.Save, UnsavedAnswer.Discard };
        if (_app.Journal.IsActive)
        {
            buttons.Add(new DialogButton(Loc.Get(K.DialogKeepInJournal)));
            map.Add(UnsavedAnswer.KeepInJournal);
        }
        buttons.Add(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true));
        map.Add(UnsavedAnswer.Cancel);
        var request = new ChoiceRequest(Loc.Get(K.DialogUnsavedTitle), Loc.Get(K.DialogUnsavedMessage, ("title", od.Title)), buttons);
        int index = await _app.Dialogs.ChooseAsync(this, request);
        return index < 0 || index >= map.Count ? UnsavedAnswer.Cancel : map[index];
    }

    private string DescribeLoss(LossReport loss, TextCodec codec)
    {
        var groups = string.Join(", ", loss.Groups.Take(8).Select(g => $"{DisplayOf(g.CodePoint)} ×{g.Count}"));
        return Loc.Plural(K.LossMessage, loss.Total, ("encoding", codec.Name), ("groups", groups));
    }

    private static string DisplayOf(int codePoint) => System.Text.Rune.IsValid(codePoint) ? new System.Text.Rune(codePoint).ToString() : $"U+{codePoint:X4}";

    /// <summary>The size, ranges, encoding and line endings of an inspected action, as the lines under its headline.</summary>
    internal static string DescribeInspection(ByteInspection report)
    {
        string Eol(EolStats e) => e.Style switch { EolStyle.Mixed => Loc.Get(K.StatusEolMixed), EolStyle.None => Loc.Get(K.InspectEolNone), _ => e.Label };
        var text = Loc.Get(K.InspectSummary, ("ranges", report.RangeCount), ("bytes", report.ChangedBytes), ("before", report.SizeBefore), ("after", report.SizeAfter))
            + "\n" + Loc.Get(K.InspectFormat, ("codecBefore", report.CodecBefore.Name), ("codecAfter", report.CodecAfter.Name), ("eolBefore", Eol(report.EolBefore)), ("eolAfter", Eol(report.EolAfter)));
        return report.RangeCount == 0 ? text + "\n" + Loc.Get(K.InspectNoBytes) : text;
    }

    /// <summary>One line per listed range of the report: where it is, old and new length, the first old and new bytes.</summary>
    internal static List<string> InspectionDetails(Document doc, ByteInspection report)
    {
        var lines = new List<string>();
        foreach (var r in report.Ranges.Take(500))
        {
            string where = report.Action != InspectedAction.Save && doc.Revision == report.Revision && doc.TryGetPosition(r.Start, out var p)
                ? Loc.Get(K.InspectAtLine, ("line", p.Line + 1), ("column", p.Column + 1))
                : Loc.Get(K.InspectAtByte, ("start", r.Start));
            string newHex = HexEditor.ToHexText(r.NewHead) + (r.NewLength > r.NewHead.Length ? " …" : "");
            lines.Add(r.OldLength is { } old && r.OldHead is { } oldHead
                ? Loc.Get(K.InspectRange, ("where", where), ("old", old), ("new", r.NewLength), ("oldHex", HexEditor.ToHexText(oldHead) + (old > oldHead.Length ? " …" : "")), ("newHex", newHex))
                : Loc.Get(K.InspectRangeNew, ("where", where), ("new", r.NewLength), ("newHex", newHex)));
        }
        if (report.RangeCount > lines.Count) lines.Add(Loc.Get(K.PreviewMore, ("count", report.RangeCount - lines.Count)));
        return lines;
    }

    /// <summary>
    /// The byte inspector (F169) for an action that rewrites bytes the user did not edit: the headline, the size, encoding and line endings before and after,
    /// every changed range, and the characters the target cannot hold. Without loss there are two answers, apply and cancel; with loss three: cancel (nothing is
    /// written), change the encoding (the one picked next) or replace with "?". <see cref="LossAnswer.Replace"/> also means "applied" when nothing is lost.
    /// </summary>
    internal async Task<(LossAnswer Answer, TextCodec? Codec)> ConfirmInspectionAsync(OpenDocument od, ByteInspection report, string headline, string acceptLabel)
    {
        if (report.HasLoss) return await ConfirmLossAsync(od, report.Loss, report.CodecAfter, acceptLabel, report, headline);
        var request = new ChoiceRequest(Loc.Get(K.InspectTitle), headline + "\n" + DescribeInspection(report),
            [new DialogButton(Loc.Get(K.PreviewApply), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], InspectionDetails(od.Doc, report));
        return await _app.Dialogs.ChooseAsync(this, request) == 0 ? (LossAnswer.Replace, null) : (LossAnswer.Cancel, null);
    }

    /// <summary>
    /// The text part of the byte inspector (F169): every character the encoding cannot hold with its line, column, byte position and the bytes written instead,
    /// the size before and after, and three answers: cancel (nothing is written), change the encoding (the one picked next) or replace with "?".
    /// </summary>
    internal async Task<(LossAnswer Answer, TextCodec? Codec)> ConfirmLossAsync(OpenDocument od, LossReport loss, TextCodec codec, string acceptLabel, ByteInspection? report = null, string? headline = null)
    {
        var doc = od.Doc;
        string replacement = HexEditor.ToHexText(codec.EncodeLossy("?"));
        var details = loss.Entries.Take(500).Select(e => Loc.Get(K.LossEntry, ("char", DisplayOf(e.CodePoint)), ("code", $"U+{e.CodePoint:X4}"), ("line", e.Line), ("column", e.Column), ("byte", e.ByteOffset), ("replacement", replacement))).ToList();
        if (loss.Truncated || loss.Entries.Count > 500) details.Add(Loc.Get(K.LossMore));
        string message = DescribeLoss(loss, codec);
        if (report is not null) message = headline + "\n" + message + "\n" + DescribeInspection(report);
        else if (!od.IsLarge)
            message += "\n" + Loc.Get(K.LossSize, ("encoding", codec.Name), ("before", doc.ByteLength), ("after", codec == doc.Codec ? doc.ByteLength : doc.PreviewConvert(codec).ByteLength));
        var request = new ChoiceRequest(Loc.Get(K.LossTitle), message + "\n" + Loc.Get(K.LossReplacement),
            [new DialogButton(acceptLabel), new DialogButton(Loc.Get(K.LossChangeEncoding)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)], details);
        switch (await _app.Dialogs.ChooseAsync(this, request))
        {
            case 0: return (LossAnswer.Replace, null);
            case 1:
                var targets = LossTargets(od, codec);
                if (targets.Count == 0) return (LossAnswer.Cancel, null);
                var picked = await _app.Dialogs.PickEncodingAsync(this, Loc.Get(K.LossPickTitle), Loc.Get(K.LossPickLabel), targets, (targets.FirstOrDefault(t => t.Lost == 0) is { Codec: not null } safe ? safe : targets[0]).Codec);
                return picked is null ? (LossAnswer.Cancel, null) : (LossAnswer.ChangeEncoding, picked);
            default: return (LossAnswer.Cancel, null);
        }
    }

    /// <summary>The encodings the document could be written in instead of <paramref name="lossy"/>, each with the number of characters it would lose from the text as the user sees it.</summary>
    private static IReadOnlyList<(TextCodec Codec, int Lost)> LossTargets(OpenDocument od, TextCodec lossy)
    {
        var doc = od.Doc;
        IReadOnlyList<(TextCodec Codec, int Lost)> all = od.TextBackend is { } backend && backend.TypedLoss().HasLoss
            ? [.. TextCodec.All.Select(c => (c, LossReport.ForText(backend.TextDocument.Text, c).Total))]
            : ByteFidelity.ListTargets(doc.Codec.Decode(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        return [.. all.Where(t => t.Codec != lossy && doc.Backend.Supports(t.Codec))];
    }

    /// <summary>
    /// Characters typed or pasted that the encoding of the document cannot hold already carry a replacement in the bytes; saving writes it, so the user must agree first.
    /// Changing the encoding converts the document and puts the typed text back, so it is written as typed. False when nothing may be written.
    /// </summary>
    private async Task<bool> ConfirmTypedLossAsync(DocTab tab, string? acceptLabel = null)
    {
        var od = tab.Doc;
        while (od.TextBackend?.TypedLoss() is { HasLoss: true } loss)
        {
            var (answer, codec) = await ConfirmLossAsync(od, loss, od.Doc.Codec, acceptLabel ?? Loc.Get(K.LossSaveReplaced));
            if (answer == LossAnswer.Replace) return true;
            if (answer == LossAnswer.Cancel || !await ConvertKeepingTypedAsync(od, codec!)) return false;
        }
        return true;
    }

    private async Task<bool> ConvertKeepingTypedAsync(OpenDocument od, TextCodec codec)
    {
        var text = od.TextBackend!.TextDocument;
        string typed = text.Text;
        var report = od.Doc.InspectConvert(codec);
        if ((await ConfirmInspectionAsync(od, report, Loc.Get(K.InspectConvert, ("encoding", codec.Name)), Loc.Get(K.LossConvertReplaced))).Answer != LossAnswer.Replace) return false;
        report.Confirm(report.HasLoss ? "?" : null);
        if (!od.Doc.Apply(report).Applied)
        {
            SetStatusMessage(Loc.Get(K.StatusConvertFailed, ("encoding", codec.Name)));
            return false;
        }
        string now = text.Text; // the conversion read the replacement bytes back; the typed characters go in again, encoded in the new codec
        int head = 0, tail = 0;
        while (head < now.Length && head < typed.Length && now[head] == typed[head]) head++;
        while (tail < now.Length - head && tail < typed.Length - head && now[^(tail + 1)] == typed[^(tail + 1)]) tail++;
        if (head + tail < Math.Max(now.Length, typed.Length)) text.Replace(head, now.Length - head - tail, typed.Substring(head, typed.Length - head - tail));
        od.SetEncodingChosen();
        od.NotifyChanged();
        SetStatusMessage(Loc.Get(K.StatusConverted, ("encoding", codec.Name)));
        return true;
    }

    // ---- save ----

    /// <param name="origin">Who saves: a save by a plugin runs none of the save actions (<see cref="SaveActionRunner"/>).</param>
    public async Task<bool> SaveAsync(DocTab tab, SaveOrigin origin = SaveOrigin.Manual)
    {
        var od = tab.Doc;
        if (od.IsVirtual) return RefuseVirtualSave();
        if (od.Source is not null) return await SaveSourceAsync(tab);
        if (od.Path is null) return await SaveAsAsync(tab);
        if (!await ConfirmTypedLossAsync(tab)) return false;
        return await SaveCoreAsync(tab, overwrite: false, origin);
    }

    /// <summary>The save of a named file with the save actions around it; a save that comes back through here from inside the actions (a conflict answered with "overwrite") runs none again.</summary>
    private Task<bool> SaveCoreAsync(DocTab tab, bool overwrite, SaveOrigin origin = SaveOrigin.Manual) =>
        tab.Doc.LockedReadOnly || tab.Hex is not null ? SaveFileAsync(tab, overwrite) : SaveWithActionsAsync(tab, origin, () => SaveFileAsync(tab, overwrite));

    private async Task<bool> SaveFileAsync(DocTab tab, bool overwrite)
    {
        var od = tab.Doc;
        if (od.LockedReadOnly)
        {
            SetStatusMessage(Loc.Get(K.LockSaveRefused));
            return false;
        }
        if (tab.Hex is not null) return await SaveHexAsync(tab, overwrite);
        PrepareForSave(od);
        try
        {
            var outcome = _app.Backups.Save(od.Doc, null, overwrite);
            if (outcome.Save is null)
            {
                SetStatusMessage(Loc.Get(K.StatusBackupFailed, ("reason", outcome.Backup.Problem ?? "")));
                return false;
            }
            if (outcome.Save.Saved)
            {
                od.NotifyChanged();
                if (od.Path is not null && System.IO.Path.GetFileName(od.Path) == ".editorconfig") ApplyLanguageOptions(); // the SQL dialect of its folder may have changed
                tab.ShowBar(null);
                SetStatusMessage(outcome.Backup.Status == BackupStatus.Failed ? Loc.Get(K.StatusSavedNoBackup, ("reason", outcome.Backup.Problem ?? "")) : Loc.Get(K.StatusSaved));
                _app.SessionChanged();
                return true;
            }
            if (outcome.Save.Conflict is { } change) return await ResolveSaveConflictAsync(tab, change);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.StatusSaveFailed, ("reason", e.Message)));
            return false;
        }
    }

    private async Task<bool> ResolveSaveConflictAsync(DocTab tab, ExternalChange change)
    {
        var message = change.Kind == ExternalChangeKind.Missing ? Loc.Get(K.ConflictMissing, ("path", change.Path)) : Loc.Get(K.ConflictModified, ("path", change.Path));
        var request = new ChoiceRequest(Loc.Get(K.ConflictTitle), message,
            [new DialogButton(Loc.Get(K.ConflictOverwrite)), new DialogButton(Loc.Get(K.ConflictCompare)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
        switch (await _app.Dialogs.ChooseAsync(this, request))
        {
            case 0: return await SaveCoreAsync(tab, overwrite: true);
            case 1: CompareWithDisk(tab); return false;
            default: return false;
        }
    }

    private bool RefuseVirtualSave()
    {
        SetStatusMessage(Loc.Get(K.VirtualReadOnly));
        return false;
    }

    public async Task<bool> SaveAsAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (od.IsVirtual) return RefuseVirtualSave();
        var doc = od.Doc;
        if (!await ConfirmTypedLossAsync(tab)) return false;

        IReadOnlyList<(TextCodec Codec, int Lost)> targets = od.IsLarge || od.IsHex
            ? [(doc.Codec, 0)]
            : ByteFidelity.ListTargets(doc.Codec.Decode(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
        var suggested = od.Path ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), od.Title.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? od.Title : od.Title + ".txt");
        var choice = await _app.Dialogs.SaveAsAsync(this, new SaveAsRequest(suggested, doc.Codec, targets));
        if (choice is null) return false;

        bool sameFile = od.Path is not null && string.Equals(System.IO.Path.GetFullPath(od.Path), choice.Path, PathComparison);
        if (!sameFile && File.Exists(choice.Path))
        {
            var ask = new ChoiceRequest(Loc.Get(K.DialogOverwriteTitle), Loc.Get(K.DialogOverwriteMessage, ("path", choice.Path)),
                [new DialogButton(Loc.Get(K.DialogOverwrite)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        }

        if (tab.Hex is not null) return await SaveHexAsync(tab, overwrite: false, choice.Path);
        PrepareForSave(od);
        try
        {
            var codec = choice.Codec;
            string? replacement = null;
            while (codec != doc.Codec && !od.IsLarge)
            {
                var report = doc.InspectConvert(codec);
                var (answer, picked) = await ConfirmInspectionAsync(od, report, Loc.Get(K.InspectConvert, ("encoding", codec.Name)), Loc.Get(K.LossSaveReplaced));
                if (answer == LossAnswer.Cancel) return false;
                if (answer == LossAnswer.ChangeEncoding) { codec = picked!; continue; }
                replacement = report.HasLoss ? "?" : null;
                break;
            }
            var result = doc.SaveAs(choice.Path, codec, replacement);
            while (result.Loss is { HasLoss: true } loss)
            {
                var (answer, picked) = await ConfirmLossAsync(od, loss, codec, Loc.Get(K.LossSaveReplaced));
                if (answer == LossAnswer.Cancel) return false;
                if (answer == LossAnswer.Replace) result = doc.SaveAs(choice.Path, codec, "?");
                else result = doc.SaveAs(choice.Path, codec = picked!);
            }
            if (result.Saved)
            {
                if (od.Source is not null)
                {
                    // a copy on disk is a file of its own; the tab no longer belongs to the source
                    od.Source = null;
                    od.TitleOverride = null;
                    tab.ShowBar(null);
                }
                if (codec != od.Guess?.Codec) od.SetEncodingChosen();
                od.NotifyChanged();
                AcquireLock(od);
                ApplyLock(od);
                RefreshAll();
                SetStatusMessage(Loc.Get(K.StatusSaved));
                _app.SessionChanged();
                return true;
            }
            if (result.Conflict is { } change) return await ResolveSaveConflictAsync(tab, change);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.StatusSaveFailed, ("reason", e.Message)));
            return false;
        }
    }

    public async Task<bool> SaveAllAsync()
    {
        foreach (var od in _docs.Where(d => d.Doc.IsModified).ToList())
            if (od.Views.FirstOrDefault() is { } tab && !await SaveAsync(tab)) return false;
        return true;
    }

    private void RefreshAll()
    {
        foreach (var g in _groups) g.Refresh();
        RefreshDocList();
        RefreshTitle();
        UpdateStatus();
    }

    // ---- reload, external changes, compare ----

    public async Task<bool> ReloadAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (od.Path is not { } path) return false;
        if (od.Doc.IsModified)
        {
            var ask = new ChoiceRequest(Loc.Get(K.DialogReloadTitle), Loc.Get(K.DialogReloadMessage, ("title", od.Title)),
                [new DialogButton(Loc.Get(K.DialogReloadDiscard)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        }
        try
        {
            var (fresh, guess) = OpenDocument.OpenDocumentOnly(path, LargeFileBytes);
            _app.Journal.Untrack(od);
            od.Replace(fresh, guess);
            _app.Journal.Track(od);
            SetStatusMessage(Loc.Get(K.StatusReloaded));
            return true;
        }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return false;
        }
    }

    /// <summary>A file changed on disk behind an open document: a non-modal bar offers reload, keeping the tab, or compare. Nothing is overwritten silently.</summary>
    private void OnExternalChange(OpenDocument od, ExternalChange change)
    {
        if (!_docs.Contains(od) || !string.Equals(change.Path, od.Path, PathComparison)) return;
        bool reviewable = change.Kind == ExternalChangeKind.Modified && od.Views.FirstOrDefault() is { } first && ReviewTarget(first);
        if (reviewable && _app.Workbench.Settings.GetBool("review.autoKeep"))
        {
            _ = KeepExternalAsync(od.Views[0]);
            return;
        }
        foreach (var tab in od.Views.ToList())
        {
            var t = tab;
            IReadOnlyList<(string, Action)> actions = change.Kind == ExternalChangeKind.Missing
                ? [(Loc.Get(K.BarKeep), () => DismissBars(od)), (Loc.Get(K.BarCloseTab), () => { DismissBars(od); _ = CloseTabAsync(t); })]
                : [(Loc.Get(K.BarReload), () => _ = ReloadAsync(t)), (Loc.Get(K.BarKeepMine), () => { DismissBars(od); SetStatusMessage(Loc.Get(K.StatusKeptMine)); }), (Loc.Get(K.BarCompare), () => CompareWithDisk(t))];
            if (reviewable) actions = [(Loc.Get(K.ReviewBar), () => { DismissBars(od); _ = StartReviewAsync(t); }), .. actions];
            var message = change.Kind == ExternalChangeKind.Missing
                ? Loc.Get(K.BarMissing, ("title", od.Title))
                : Loc.Get(K.BarChanged, ("title", od.Title));
            t.ShowBar(new InfoBar(message, actions, warning: true, onClose: () => DismissBars(od)));
        }
    }

    /// <summary>With <c>review.autoKeep</c> on, a change from outside to a tab without unsaved edits is taken over at once.</summary>
    private async Task KeepExternalAsync(DocTab tab)
    {
        if (await ReloadAsync(tab)) SetStatusMessage(Loc.Get(K.ReviewAutoKept, ("title", tab.Title)));
    }

    private static void DismissBars(OpenDocument od)
    {
        foreach (var tab in od.Views) tab.ShowBar(null);
    }

    /// <summary>
    /// Compares the content of the tab with the file on disk: in the configured external tool when one is set, else in the built-in comparison
    /// (then null is returned and a compare tab opens).
    /// </summary>
    public ExternalDiff.Started? CompareWithDisk(DocTab tab)
    {
        var od = tab.Doc;
        if (od.Path is not { } path || !File.Exists(path))
        {
            SetStatusMessage(Loc.Get(K.CompareNoFile));
            return null;
        }
        var settings = _app.Workbench.Settings;
        var tool = settings.GetString("tools.diffTool");
        if (string.IsNullOrWhiteSpace(tool))
        {
            CompareWithDiskBuiltIn(tab, path);
            return null;
        }
        var started = ExternalDiff.Compare(od.Doc, path, tool, settings.GetString("tools.diffArguments"));
        if (!started.Ok) SetStatusMessage(Loc.Get(K.CompareFailed, ("reason", started.Problem ?? "")));
        return started;
    }
}

using System.Text;
using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.App.Shell;

internal enum UnsavedAnswer { Cancel, Save, Discard, KeepInJournal }

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

    /// <summary>The loss dialog: every character the encoding cannot hold with its line, column and byte position. True when the user explicitly accepts the replacement.</summary>
    internal async Task<bool> ConfirmLossAsync(LossReport loss, TextCodec codec, string acceptLabel)
    {
        var details = loss.Entries.Take(500).Select(e => Loc.Get(K.LossEntry, ("char", DisplayOf(e.CodePoint)), ("code", $"U+{e.CodePoint:X4}"), ("line", e.Line), ("column", e.Column), ("byte", e.ByteOffset))).ToList();
        if (loss.Truncated || loss.Entries.Count > 500) details.Add(Loc.Get(K.LossMore));
        var request = new ChoiceRequest(Loc.Get(K.LossTitle), DescribeLoss(loss, codec) + "\n" + Loc.Get(K.LossReplacement),
            [new DialogButton(acceptLabel), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)], details);
        return await _app.Dialogs.ChooseAsync(this, request) == 0;
    }

    /// <summary>Characters typed or pasted that the encoding of the document cannot hold already carry a replacement in the bytes; saving writes it, so the user must agree first.</summary>
    private async Task<bool> ConfirmTypedLossAsync(OpenDocument od)
    {
        if (od.TextBackend?.TypedLoss() is not { HasLoss: true } loss) return true;
        return await ConfirmLossAsync(loss, od.Doc.Codec, Loc.Get(K.LossSaveReplaced));
    }

    // ---- save ----

    public async Task<bool> SaveAsync(DocTab tab)
    {
        var od = tab.Doc;
        if (od.Path is null) return await SaveAsAsync(tab);
        if (od.IsLarge) return true; // a paged document cannot have unsaved changes
        if (!await ConfirmTypedLossAsync(od)) return false;
        return await SaveCoreAsync(tab, overwrite: false);
    }

    private async Task<bool> SaveCoreAsync(DocTab tab, bool overwrite)
    {
        var od = tab.Doc;
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

    public async Task<bool> SaveAsAsync(DocTab tab)
    {
        var od = tab.Doc;
        var doc = od.Doc;
        if (!await ConfirmTypedLossAsync(od)) return false;

        IReadOnlyList<(TextCodec Codec, int Lost)> targets = od.IsLarge
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

        PrepareForSave(od);
        try
        {
            var result = doc.SaveAs(choice.Path, choice.Codec);
            if (result.Loss is { HasLoss: true } loss)
            {
                if (!await ConfirmLossAsync(loss, choice.Codec, Loc.Get(K.LossSaveReplaced))) return false;
                result = doc.SaveAs(choice.Path, choice.Codec, "?");
            }
            if (result.Saved)
            {
                if (choice.Codec != od.Guess?.Codec) od.SetEncodingChosen();
                od.NotifyChanged();
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
        foreach (var od in _docs.Where(d => d.Doc.IsModified && !d.IsLarge).ToList())
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
        if (!_docs.Contains(od)) return;
        foreach (var tab in od.Views.ToList())
        {
            var t = tab;
            IReadOnlyList<(string, Action)> actions = change.Kind == ExternalChangeKind.Missing
                ? [(Loc.Get(K.BarKeep), () => DismissBars(od)), (Loc.Get(K.BarCloseTab), () => { DismissBars(od); _ = CloseTabAsync(t); })]
                : [(Loc.Get(K.BarReload), () => _ = ReloadAsync(t)), (Loc.Get(K.BarKeepMine), () => { DismissBars(od); SetStatusMessage(Loc.Get(K.StatusKeptMine)); }), (Loc.Get(K.BarCompare), () => CompareWithDisk(t))];
            var message = change.Kind == ExternalChangeKind.Missing
                ? Loc.Get(K.BarMissing, ("title", od.Title))
                : Loc.Get(K.BarChanged, ("title", od.Title));
            t.ShowBar(new InfoBar(message, actions, warning: true, onClose: () => DismissBars(od)));
        }
    }

    private static void DismissBars(OpenDocument od)
    {
        foreach (var tab in od.Views) tab.ShowBar(null);
    }

    /// <summary>Opens the configured external tool on the file on disk and the content of the tab.</summary>
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
            ShowNotice("diff", Loc.Get(K.CompareNoTool), [(Loc.Get(K.CompareOpenSettings), () => { DismissNotice("diff"); _app.OpenSettings("diff"); })], warning: true);
            return null;
        }
        var started = ExternalDiff.Compare(od.Doc, path, tool, settings.GetString("tools.diffArguments"));
        if (!started.Ok) SetStatusMessage(Loc.Get(K.CompareFailed, ("reason", started.Problem ?? "")));
        return started;
    }
}

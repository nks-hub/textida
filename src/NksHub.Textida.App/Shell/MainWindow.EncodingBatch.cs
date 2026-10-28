using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Shell;

/// <summary>Convert the encoding of the files of a folder (P5.5.2): the preview names the files that would lose characters before anything is written.</summary>
internal sealed partial class MainWindow
{
    private const int BatchListed = 500;

    /// <summary>
    /// Folder, file masks and target encoding are asked first; then every file is read and the preview lists what would be lost. The run is journaled,
    /// so a stop in the middle is offered for finishing or rolling back at the next start (<see cref="CheckPendingBatches"/>).
    /// </summary>
    internal async Task ConvertFilesInFolderAsync(string? folder = null)
    {
        folder ??= await _app.Dialogs.PickFolderAsync(this);
        if (folder is null) return;
        var maskText = await _app.Dialogs.PromptAsync(this, Loc.Get(K.BatchMasksTitle), Loc.Get(K.BatchMasksLabel), "");
        if (maskText is null) return;
        var targets = TextCodec.All.Select(c => (c, 0)).ToList();
        var target = await _app.Dialogs.PickEncodingAsync(this, Loc.Get(K.BatchTargetTitle), Loc.Get(K.BatchTargetLabel), targets, TextCodec.Utf8);
        if (target is null) return;

        var options = new EncodingBatchOptions { Target = target, Masks = [.. maskText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] };
        var modified = _docs.Where(d => d.Doc.IsModified && d.Path is not null).Select(d => System.IO.Path.GetFullPath(d.Path!)).ToHashSet(PathComparer);
        SetStatusMessage(Loc.Get(K.BatchScanning));
        EncodingBatchPlan plan;
        try { plan = await Task.Run(() => EncodingBatch.Plan(folder, options)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(e.Message);
            return;
        }
        SetStatusMessage("");
        var clean = plan.Clean.Where(i => !modified.Contains(i.Path)).ToList();
        var lossy = plan.Lossy.Where(i => !modified.Contains(i.Path)).ToList();
        if (clean.Count + lossy.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.BatchNothing));
            return;
        }

        var details = new List<string>();
        foreach (var item in lossy.Take(BatchListed))
            details.Add(Loc.Get(K.BatchLineLossy, ("path", Relative(folder, item.Path)), ("source", item.Source!.Name), ("groups", LostGroups(item))));
        foreach (var item in plan.Skipped.Where(i => i.Skip != BatchSkip.Unchanged).Take(BatchListed))
            details.Add(Loc.Get(K.BatchLineSkipped, ("path", Relative(folder, item.Path)), ("reason", SkipReason(item))));
        if (lossy.Count + plan.Skipped.Count() > details.Count) details.Add(Loc.Get(K.PreviewMore, ("count", lossy.Count + plan.Skipped.Count() - details.Count)));

        string message = Loc.Get(K.BatchPreview, ("encoding", target.Name), ("clean", clean.Count), ("lossy", lossy.Count), ("skipped", plan.Skipped.Count()));
        if (modified.Count > 0) message += "\n" + Loc.Get(K.BatchExcluded, ("count", modified.Count));
        var buttons = new List<DialogButton> { new(Loc.Plural(K.BatchConvert, clean.Count), IsDefault: true) };
        if (lossy.Count > 0) buttons.Add(new DialogButton(Loc.Get(K.BatchConvertAll)));
        buttons.Add(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true));
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.BatchTitle), message, buttons, details));
        bool replace = lossy.Count > 0 && answer == 1;
        if (answer < 0 || answer >= buttons.Count - 1) return;
        if (!replace && clean.Count == 0) return;

        string? journal = BatchJournalDirectory;
        EncodingBatchResult result;
        using (OwnWrite()) result = await Task.Run(() => EncodingBatch.Run(plan, replace ? "?" : null, journal, modified));
        await ShowBatchResultAsync(folder, target, result);
    }

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Relative(string folder, string path) => DisplayPath(folder, path);

    private static string LostGroups(EncodingBatchItem item) =>
        string.Join(", ", item.Loss.Groups.Take(8).Select(g => $"{DisplayOf(g.CodePoint)} ×{g.Count}"));

    private static string SkipReason(EncodingBatchItem item) => item.Skip switch
    {
        BatchSkip.Binary => Loc.Get(K.BatchSkipBinary),
        BatchSkip.UncertainEncoding => Loc.Get(K.BatchSkipUncertain, ("reason", $"{item.Source?.Name} {item.Confidence:P0}")),
        BatchSkip.Unchanged => Loc.Get(K.BatchSkipUnchanged),
        BatchSkip.Lossy => Loc.Get(K.BatchSkipLossy),
        _ => Loc.Get(K.BatchSkipUnreadable),
    };

    private async Task ShowBatchResultAsync(string folder, TextCodec target, EncodingBatchResult result)
    {
        var lines = new List<string>();
        foreach (var f in result.Files)
        {
            string path = Relative(folder, f.DisplayName);
            lines.Add(f.Status switch
            {
                ChangeStatus.Applied => Loc.Get(K.BatchLineDone, ("path", path), ("source", result.Ran.FirstOrDefault(i => i.Path == f.Key)?.Source?.Name ?? "")),
                ChangeStatus.Conflict => Loc.Get(K.BatchLineConflict, ("path", path)),
                _ => Loc.Get(K.BatchLineFailed, ("path", path), ("reason", f.Message ?? "")),
            });
        }
        foreach (var item in result.NotRun.Where(i => i.Skip != BatchSkip.Unchanged))
            lines.Add(Loc.Get(K.BatchLineSkipped, ("path", Relative(folder, item.Path)), ("reason", SkipReason(item))));
        string summary = Loc.Get(K.BatchResult, ("converted", result.Converted), ("left", result.NotRun.Count), ("conflicts", result.Conflicts), ("failed", result.Failed));
        SetStatusMessage(Loc.Plural(K.BatchConverted, result.Converted, ("encoding", target.Name)));
        _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.BatchTitle), summary);
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.BatchResultTitle), summary, [new DialogButton(Loc.Get(K.BatchOk), IsDefault: true, IsCancel: true)], lines.Take(BatchListed).ToList()));
    }
}

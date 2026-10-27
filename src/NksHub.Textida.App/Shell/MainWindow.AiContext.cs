using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.AiContext;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Edit, Copy as AI Context: the selections, the file, the open tabs or the opened folder as one block of Markdown or XML on the clipboard. The preview says how
/// large the block is and what was left out and why before anything is copied. The rules (secrets files never, paged files only as a selection) are in
/// <see cref="AiContextBuilder"/>; nothing is sent anywhere.
/// </summary>
internal sealed partial class MainWindow
{
    private const int AiPreviewLines = 200;

    /// <summary>The copy the last chosen scope started; tests wait for it.</summary>
    internal Task AiContextTask { get; private set; } = Task.CompletedTask;

    /// <summary>What a scope gathered: the sources and what was left out, or the reason it could not run.</summary>
    internal sealed record AiGathered(IReadOnlyList<AiSource> Sources, IReadOnlyList<AiOmission> Omissions);

    internal CommandOutcome ShowAiContext()
    {
        var tab = Active is { Hex: null, Custom: null } t && !t.Doc.IsResults ? t : null; // the tab to take a selection or the file from
        string? folder = FileRootFolder;
        if (tab is null && folder is null && !Tabs.Any()) return CommandOutcome.NotHandled;
        var rows = new List<QuickRow>();
        if (tab is not null)
        {
            rows.Add(AiRow(K.AiScopeSelection, SelectionPieces(tab).Count > 0, K.AiNoSelection, () => Task.FromResult(new AiGathered(SelectionSources(tab), []))));
            rows.Add(AiRow(K.AiScopeFile, true, null, () => Task.FromResult(Gather([tab]))));
        }
        rows.Add(AiRow(K.AiScopeTabs, true, null, () => Task.FromResult(Gather(Tabs.DistinctBy(t => t.Doc)))));
        rows.Add(AiRow(K.AiScopeFolder, folder is not null, K.AiNoFolder, () => Task.Run(() =>
        {
            var read = AiContextFolder.Collect(folder!, p => LanguageModes.ForPath(p).Id);
            return new AiGathered(read.Sources, read.Omissions);
        })));
        ShowChoice(Loc.Get(K.AiWatermark), Loc.Get(K.A11yAiContext), rows);
        return CommandOutcome.Handled;
    }

    private QuickRow AiRow(string titleKey, bool enabled, string? reasonKey, Func<Task<AiGathered>> gather)
    {
        var title = Loc.Get(titleKey);
        return new QuickRow(title, null, null, null, enabled, enabled ? null : Loc.Get(reasonKey!), title, () => AiContextTask = CopyAiContextAsync(gather));
    }

    private async Task CopyAiContextAsync(Func<Task<AiGathered>> gather)
    {
        AiGathered gathered;
        try { gathered = await gather(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.AiFailed, ("reason", e.Message))); return; }
        var markdown = AiContextBuilder.Build(gathered.Sources, gathered.Omissions, AiContextFormat.Markdown);
        var xml = AiContextBuilder.Build(gathered.Sources, gathered.Omissions, AiContextFormat.Xml);

        var details = markdown.Omissions.Select(o => o.Name is { } name
            ? Loc.Get(K.AiOmitNamed, ("name", name), ("reason", ReasonText(o.Reason)))
            : Loc.Get(K.AiOmitCount, ("number", o.Count), ("reason", ReasonText(o.Reason)))).Take(AiPreviewLines).ToList();
        if (markdown.Omissions.Count > details.Count) details.Add(Loc.Get(K.PreviewMore, ("count", markdown.Omissions.Count - details.Count)));
        var message = Loc.Get(K.AiPreview, ("files", markdown.Files), ("markdown", OpenDocument.FormatSize(markdown.Bytes)), ("xml", OpenDocument.FormatSize(xml.Bytes)))
            + (details.Count > 0 ? " " + Loc.Get(K.AiLeftOut) : "");
        var buttons = markdown.Files == 0
            ? new List<DialogButton> { new(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true) }
            : [new(Loc.Get(K.AiCopyMarkdown), IsDefault: true), new(Loc.Get(K.AiCopyXml)), new(Loc.Get(K.DialogCancel), IsCancel: true)];
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.AiPreviewTitle), message, buttons, details.Count > 0 ? details : null));
        if (markdown.Files == 0 || answer is not (0 or 1)) return;

        var chosen = answer == 0 ? markdown : xml;
        if (Clipboard is { } clipboard) await clipboard.SetTextAsync(chosen.Text);
        SetStatusMessage(Loc.Get(K.AiCopied, ("files", chosen.Files), ("size", OpenDocument.FormatSize(chosen.Bytes))));
    }

    private static string ReasonText(AiOmitReason reason) => Loc.Get(reason switch
    {
        AiOmitReason.SecretFile => K.AiReasonSecrets,
        AiOmitReason.IgnoreRules => K.AiReasonIgnore,
        AiOmitReason.Hidden => K.AiReasonHidden,
        AiOmitReason.Binary => K.AiReasonBinary,
        AiOmitReason.TooLarge => K.AiReasonTooLarge,
        AiOmitReason.Unreadable => K.AiReasonUnreadable,
        _ => K.AiReasonPaged,
    });

    /// <summary>The selected parts of a tab: text and 1-based line range (null when a paged file's line index does not reach them).</summary>
    private static List<AiRange> SelectionPieces(DocTab tab)
    {
        var pieces = new List<AiRange>();
        if (tab.Surface is { } surface)
        {
            var document = surface.Document;
            foreach (var s in surface.GetSelections().Where(s => !s.IsEmpty).OrderBy(s => s.Start))
            {
                var from = document.GetLocation(s.Start);
                var to = document.GetLocation(s.End);
                long last = to.Column == 1 && to.Line > from.Line ? to.Line - 1 : to.Line;
                pieces.Add(new AiRange(document.GetText(s.Start, s.Length), from.Line, last));
            }
        }
        else if (tab.Paged is { HasSelection: true } paged)
        {
            var range = paged.SelectionRange;
            var core = tab.Doc.Doc;
            bool known = core.TryGetPosition(range.Start, out var first) & core.TryGetPosition(range.End, out var end);
            var text = paged.SelectedText();
            pieces.Add(known ? new AiRange(text, first.Line + 1, end.Column == 0 && end.Line > first.Line ? end.Line : end.Line + 1) : new AiRange(text));
        }
        return pieces;
    }

    private AiSource SourceOf(DocTab tab, IReadOnlyList<AiRange> ranges) =>
        new(tab.Path is { } path ? Display(path) : tab.Title, tab.Path, tab.Doc.Language.Id, tab.Doc.Doc.Codec.Name, tab.Doc.Doc.Revision, tab.Doc.Doc.IsModified, ranges);

    private List<AiSource> SelectionSources(DocTab tab) => SelectionPieces(tab) is { Count: > 0 } pieces ? [SourceOf(tab, pieces)] : [];

    /// <summary>
    /// Whole tabs. A text tab gives all of its text, as it is in the editor now (unsaved changes included and marked); a paged tab gives only its selection and
    /// is named as left out when nothing is selected. Tabs of hex and of the search results have no text to give.
    /// </summary>
    private AiGathered Gather(IEnumerable<DocTab> tabs)
    {
        var sources = new List<AiSource>();
        var omissions = new List<AiOmission>();
        foreach (var tab in tabs)
        {
            if (tab.Doc.IsResults || tab.Hex is not null || tab.Custom is not null) continue;
            if (tab.Surface is { } surface) sources.Add(SourceOf(tab, [AiRange.Whole(surface.Document.Text)]));
            else if (SelectionSources(tab) is { Count: > 0 } selected) sources.AddRange(selected);
            else omissions.Add(new AiOmission(AiOmitReason.Paged, tab.Path is { } path ? Display(path) : tab.Title));
        }
        return new AiGathered(sources, omissions);
    }
}

using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Format the document with a formatter of the tool catalog and look at the result before it is written (PZ.5.1): the formatter (PHP CS Fixer, gofmt, Prettier, ...) runs on a copy, and its result is not applied but proposed in the change review, where
/// the user ticks what to keep. This is the manual counterpart of the format action of a save, which applies the result at once as one undo step.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The proposal the last format put in the review, for tests.</summary>
    internal Task<ProposalDecision>? FormatDecision { get; private set; }

    internal CommandOutcome FormatWithReview()
    {
        if (Active is not { Surface: not null } tab || tab.Doc.ReadOnly) return CommandOutcome.NotHandled;
        _ = FormatWithReviewAsync(tab);
        return CommandOutcome.Handled;
    }

    internal async Task FormatWithReviewAsync(DocTab tab)
    {
        if (tab.Path is not { } path) { SetStatusMessage(Loc.Get(K.FormatReviewNoFile)); return; }
        var offered = ConfigFormatters.Offered(path);
        if (offered.Count == 0) { SetStatusMessage(Loc.Get(K.FormatReviewNone, ("file", Path.GetFileName(path)))); return; }
        FormatterDef def = offered[0];
        if (offered.Count > 1)
        {
            var pick = await PickAsync(Loc.Get(K.FormatReviewWatermark), Loc.Get(K.FormatReviewA11y), [.. offered.Select(f => (f.Title, (string?)Path.GetFileName(ConfigFormatters.ConfigFor(f, path)!), f))]);
            if (!pick.Chosen) return;
            def = pick.Value!;
        }
        var set = LoadSaveActions(path);
        var timeout = set.FormatTimeout ?? TimeSpan.FromSeconds(60);
        SetStatusMessage(Loc.Get(K.FormatReviewRunning, ("name", def.Title)));
        var plan = await ConfigFormatters.PlanAsync(tab.Doc.Doc, def, timeout, toolPath: set.ToolPaths?.GetValueOrDefault(def.Id));
        switch (plan.Outcome.Result)
        {
            case FormatResult.Unchanged:
                SetStatusMessage(Loc.Get(K.FormatReviewUnchanged, ("name", def.Title)));
                return;
            case FormatResult.Formatted:
                break;
            default:
                SetStatusMessage(Loc.Get(K.FormatReviewFailed, ("name", def.Title), ("reason", plan.Outcome.Reason)));
                return;
        }
        FileChange change;
        try { change = ChangeBuilder.ForDocument(tab.Doc.Doc, [new TextReplacement(plan.Range, plan.NewText)]); }
        catch (ArgumentException e)
        {
            SetStatusMessage(Loc.Get(K.FormatReviewFailed, ("name", def.Title), ("reason", e.Message)));
            return;
        }
        FormatDecision = ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.FormatReviewProposal, ("name", def.Title), ("file", tab.Title)), "format:" + def.Id, tab, change, CancellationToken.None);
    }
}

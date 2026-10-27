using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Search;
using NksHub.Textida.PluginHost.Conversions;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Edit, Run Recipe on Files: a recipe of a data plugin or of the <c>recipes</c> folder of the profile runs over a folder with a file mask, the open tabs or the files of the search results.
/// The text work is <see cref="RecipeBatch"/>; what it plans is the proposal of the Search Results tab (the preview of Replace in Files), so every file is checked against its revision when the ticked changes
/// are applied, a file that changed meanwhile is reported and left out, and the others are written through the journal of batches.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The planning the last chosen set started; tests wait for it.</summary>
    internal Task RecipeFilesTask { get; private set; } = Task.CompletedTask;

    internal CommandOutcome ShowRecipeFiles()
    {
        ReportLibraryProblems();
        var problems = new List<(string File, string Reason)>();
        var entries = _app.Clips.Recipes.Concat(_app.RecipeFiles.Load(problems)).ToList();
        if (problems.Count > 0) SetStatusMessage(Loc.Get(K.RecipeFilesDamaged, ("file", problems[0].File), ("reason", problems[0].Reason)));
        var rows = entries.Select(e => new QuickRow(e.Recipe.Title, e.PluginName, null, null, true, null, $"{e.Recipe.Title}, {e.PluginName}",
            () => Dispatcher.UIThread.Post(() => ShowRecipeFileSets(e)))).ToList();
        ShowChoice(Loc.Get(K.RecipeFilesWatermark), Loc.Get(K.A11yRecipeFiles), rows, emptyText: Loc.Get(K.RecipeFilesEmpty));
        return CommandOutcome.Handled;
    }

    private void ShowRecipeFileSets(RecipeEntry entry)
    {
        bool hasResults = ResultsView?.Rows.Any(r => r is SearchResultsView.HitRow) == true;
        var rows = new List<QuickRow>
        {
            SetRow(K.RecipeFilesSetFolder, true, null, () => _ = ChooseFolderAsync(entry)),
            SetRow(K.RecipeFilesSetTabs, true, null, () => RecipeFilesTask = RunRecipeFilesAsync(entry, [], OpenBuffers(null, editableOnly: true))),
            SetRow(K.RecipeFilesSetResults, hasResults, K.RecipeFilesNoResults, () =>
            {
                var (paths, buffers) = ResultFiles();
                RecipeFilesTask = RunRecipeFilesAsync(entry, paths, buffers);
            }),
        };
        ShowChoice(Loc.Get(K.RecipeFilesSetWatermark), Loc.Get(K.A11yRecipeFileSet), rows);
    }

    private static QuickRow SetRow(string titleKey, bool enabled, string? reasonKey, Action run)
    {
        var title = Loc.Get(titleKey);
        return new QuickRow(title, null, null, null, enabled, enabled ? null : Loc.Get(reasonKey!), title, () => Dispatcher.UIThread.Post(run));
    }

    private async Task ChooseFolderAsync(RecipeEntry entry)
    {
        var start = FileRootFolder ?? (Active?.Path is { } p ? System.IO.Path.GetDirectoryName(p) : null) ?? Environment.CurrentDirectory;
        var folder = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.RecipeFilesFolderTitle), Loc.Get(K.RecipeFilesFolderLabel), start))?.Trim();
        if (string.IsNullOrEmpty(folder)) return;
        if (!Directory.Exists(folder)) { SetStatusMessage(Loc.Get(K.FindFolderMissing, ("path", folder))); return; }
        var mask = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.RecipeFilesFolderTitle), Loc.Get(K.RecipeFilesMaskLabel), "*"))?.Trim();
        if (mask is null) return;
        folder = System.IO.Path.GetFullPath(folder);
        var paths = await Task.Run(() => RecipeBatch.FolderFiles(folder, mask));
        RecipeFilesTask = RunRecipeFilesAsync(entry, paths, OpenBuffers(folder, editableOnly: true));
    }

    /// <summary>The files of the hits in the Search Results tab: files on disk by path, open documents by their current content.</summary>
    private (IReadOnlyList<string> Paths, IReadOnlyList<LiveBuffer> Buffers) ResultFiles()
    {
        var sources = ResultsView!.Rows.OfType<SearchResultsView.HitRow>().Select(h => h.Hit.Source).ToList();
        var paths = sources.Where(s => s.DocumentId is null && s.Path is not null).Select(s => s.Path!).Distinct().ToList();
        var buffers = sources.Where(s => s.DocumentId is not null).Select(s => s.DocumentId!.Value).Distinct()
            .Select(FindOpenDocument).OfType<NksHub.Textida.Core.Documents.Document>().Select(d => LiveBuffer.Of(d)).ToList();
        return (paths, buffers);
    }

    /// <summary>Plans the recipe over the files and shows the result in the review of the Search Results tab. Nothing is written until the ticked changes are applied there.</summary>
    internal async Task RunRecipeFilesAsync(RecipeEntry entry, IReadOnlyList<string> paths, IReadOnlyList<LiveBuffer> buffers)
    {
        var title = entry.Recipe.Title;
        var context = _app.VariableContextFor(null, "", out var broken);
        if (broken is not null) { SetStatusMessage(Loc.Get(K.RecipeFailed, ("title", title), ("reason", Loc.Get(K.ClipVariablesBroken, ("reason", broken))))); return; }
        TimeSpan? timeout = entry.Recipe.Steps.Any(s => s.Kind == NksHub.Textida.PluginHost.Manifest.RecipeStep.Format)
            ? LoadSaveActions(paths.FirstOrDefault() ?? FileRootFolder).FormatTimeout
            : null;
        SetStatusMessage(Loc.Plural(K.RecipeFilesPlanning, paths.Count + buffers.Count, ("title", title)));

        ReplacePlan plan;
        try { plan = await RecipeBatch.PlanAsync(entry.Recipe, entry.Converters, paths, buffers, context, timeout); }
        catch (OperationCanceledException) { return; }
        if (plan.Proposal.ChangeCount == 0)
        {
            SetStatusMessage(plan.Skipped.Count > 0
                ? Loc.Plural(K.RecipeFilesSkipped, plan.Skipped.Count, ("title", title), ("reason", plan.Skipped[0]))
                : Loc.Plural(K.RecipeFilesNothing, paths.Count + buffers.Count, ("title", title)));
            return;
        }
        var decision = ShowPlanAsync(Guid.NewGuid().ToString(), plan.Proposal, null, CancellationToken.None);
        if (ReferenceEquals(_plan, plan.Proposal))
            foreach (var skipped in plan.Skipped) _resultsView?.AddNote(Loc.Get(K.ResultsNotPreviewed, ("text", skipped)), warning: true);
        await decision;
    }
}

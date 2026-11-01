using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// "Who calls this" (PZ.6.4): the callers of the symbol under the caret from the CodeGraph graph of the project, listed with the place of each (Enter opens it, the lines around it are shown beside the list). It is offered only where a <c>.codegraph</c> folder
/// exists above the file and only reads: the editor never indexes. A tab with unsaved text gets a bar that says the graph is the state of the files on disk, not of the text in the tab.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The last call, for tests.</summary>
    internal Task CallersWork { get; private set; } = Task.CompletedTask;

    internal CommandOutcome ShowCallers()
    {
        if (Active is not { Surface: not null } tab || tab.Path is null) return CommandOutcome.NotHandled;
        CallersWork = ShowCallersAsync(tab);
        return CommandOutcome.Handled;
    }

    internal async Task ShowCallersAsync(DocTab tab)
    {
        string path = tab.Path!;
        string? root = CodeGraphCallers.ProjectRoot(Path.GetDirectoryName(path)!);
        if (root is null) { SetStatusMessage(Loc.Get(K.CallersNoGraph)); return; }
        string symbol = (SelectionOrWord() ?? "").Trim();
        if (symbol.Length == 0 || symbol.Contains('\n') || symbol.Length > 200) { SetStatusMessage(Loc.Get(K.CallersNoSymbol)); return; }
        var catalog = LanguageCatalog.CodeGraph;
        string? program = ToolLocator.Find(catalog.Executable, root, LoadSaveActions(path).ToolPaths?.GetValueOrDefault(catalog.Id));
        if (program is null) { SetStatusMessage(Loc.Get(K.CallersMissing, ("program", catalog.Executable))); return; }

        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var args = CodeGraphCallers.Arguments(root, symbol, relative);
        var tool = CodeGraphCallers.Invocation(program, root, args);
        if (!await ApproveToolAsync(tool, catalog.Title)) return;

        SetStatusMessage(Loc.Get(K.CallersRunning, ("symbol", symbol)));
        (string Output, string Errors, int ExitCode, bool TimedOut) run;
        try { run = await CodeGraphCallers.RunAsync(tool); }
        catch (ToolException e) { SetStatusMessage(Loc.Get(K.CallersFailed, ("reason", e.Message))); return; }
        if (run.TimedOut) { SetStatusMessage(Loc.Get(K.CallersTimedOut, ("seconds", (int)(CodeGraphCallers.Timeout.TotalSeconds)))); return; }
        if (run.ExitCode != 0) { SetStatusMessage(Loc.Get(K.CallersFailed, ("reason", FirstLine(run.Errors.Length > 0 ? run.Errors : run.Output)))); return; }

        var answer = CodeGraphCallers.Parse(symbol, run.Output);
        if (answer.NotFound) { SetStatusMessage(Loc.Get(K.CallersNotFound, ("symbol", symbol))); return; }
        bool unsaved = tab.Doc.Doc.IsModified;
        if (unsaved) tab.ShowBar(new InfoBar(Loc.Get(K.CallersUnsaved), [], onClose: () => tab.ShowBar(null)));

        var rows = new List<QuickRow>();
        int total = answer.Definitions.Sum(d => d.Callers.Count);
        string summary = Loc.Plural(K.CallersSummary, total, ("symbol", answer.Symbol), ("definitions", answer.Definitions.Count));
        string? note = unsaved ? string.Join(" ", new[] { answer.Note, Loc.Get(K.CallersUnsaved) }.Where(t => !string.IsNullOrEmpty(t))) : answer.Note; // the panel says it too, for whoever reads the list and not the tab
        rows.Add(new QuickRow(summary, null, note, null, false, null, summary, () => { }));
        foreach (var definition in answer.Definitions)
            foreach (var caller in definition.Callers)
            {
                string full = Path.GetFullPath(Path.Combine(root, caller.File.Replace('/', Path.DirectorySeparatorChar)));
                string title = $"{caller.Name}", detail = $"{caller.File}:{caller.Line}";
                string category = Loc.Get(K.CallersOf, ("name", definition.Name));
                int line = caller.Line;
                rows.Add(new QuickRow(title, category, detail, null, true, null, $"{title}, {detail}", () => JumpToFile(full, line), () => PreviewAround(full, line)));
            }
        ShowChoice(Loc.Get(K.CallersWatermark, ("symbol", answer.Symbol)), Loc.Get(K.CallersA11y), rows, null, Loc.Get(K.CallersNone, ("symbol", answer.Symbol)));
    }

    /// <summary>The lines around a place of a file, for the preview beside the list; a message when the file cannot be read.</summary>
    private static string PreviewAround(string path, int line)
    {
        try
        {
            var lines = File.ReadLines(path).Skip(Math.Max(0, line - 3)).Take(10).ToList();
            return string.Join("\n", lines.Select((l, i) => $"{Math.Max(1, line - 2) + i,5}  {l}"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Loc.Get(K.PalettePreviewError, ("reason", e.Message)); }
    }
}

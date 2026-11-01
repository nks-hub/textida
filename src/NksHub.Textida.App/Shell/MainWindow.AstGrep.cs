using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Structural search and rewrite with ast-grep (PZ.6.5). The pattern and the rewrite are asked for, the program is found through the tool catalog, approved once like every tool, and run behind the proxy of the editor over the folder.
/// The open tabs of the folder are not read from disk: their bytes go to the standard input of a run each, so unsaved text is searched and a match refers to the revision the tab was at. With a rewrite the matches become one proposal in
/// the change preview (ticks per change, a file or tab that changed since is a conflict and is left alone); without one the matches are listed in the Tasks tab.
/// </summary>
internal sealed partial class MainWindow
{
    private const int StructuralMaxTabs = 50;

    /// <summary>The proposal the last rewrite put in the preview, for tests.</summary>
    internal Task<ProposalDecision>? StructuralDecision { get; private set; }

    /// <summary>Asks once per program whether it may run, showing the command and the folder (the same question as for a linter); true when it was approved before or now.</summary>
    internal async Task<bool> ApproveToolAsync(ToolInvocation tool, string title)
    {
        if (_app.ToolApprovals.IsApproved(tool)) return true;
        var request = new ChoiceRequest(Loc.Get(K.ToolConfirmTitle), Loc.Get(K.ToolConfirmMessage, ("title", title)),
            [new DialogButton(Loc.Get(K.ToolConfirmRun), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
            [Loc.Get(K.ToolConfirmCommand, ("command", tool.CommandLine)), Loc.Get(K.ToolConfirmDirectory, ("directory", tool.WorkingDirectory))]);
        if (await _app.Dialogs.ChooseAsync(this, request) != 0) return false;
        _app.ToolApprovals.Approve(tool);
        return true;
    }

    internal CommandOutcome StructuralSearch()
    {
        if (Active is null) return CommandOutcome.NotHandled;
        _ = StructuralSearchAsync();
        return CommandOutcome.Handled;
    }

    internal async Task StructuralSearchAsync()
    {
        if (Active is not { } tab) return;
        string? lang = AstGrep.LanguageFor(tab.Doc.Language.Id, tab.Path is { } p ? Path.GetExtension(p) : null);
        if (lang is null) { SetStatusMessage(Loc.Get(K.AstGrepNoLanguage, ("language", tab.Doc.Language.Name))); return; }
        string? folder = FileRootFolder ?? (tab.Path is { } own ? Path.GetDirectoryName(own) : null);
        if (folder is null) { SetStatusMessage(Loc.Get(K.AstGrepNoFolder)); return; }
        folder = Path.GetFullPath(folder);

        string title = Loc.Get(K.AstGrepTitle);
        string initial = ActiveEditor is { } surface && surface.GetSelections().Where(s => !s.IsEmpty).Select(s => surface.Document.GetText(s.Start, s.Length)).FirstOrDefault() is { } selected && !selected.Contains('\n') ? selected : "";
        if (await _app.Dialogs.PromptAsync(this, title, Loc.Get(K.AstGrepPatternLabel), initial) is not { } pattern || pattern.Trim().Length == 0) return;
        if (await _app.Dialogs.PromptAsync(this, title, Loc.Get(K.AstGrepRewriteLabel), "") is not { } rewrite) return;
        rewrite = rewrite.Trim().Length == 0 ? "" : rewrite;

        var catalog = LanguageCatalog.StructuralSearch;
        string? program = ToolLocator.Find(catalog.Executable, folder, LoadSaveActions(tab.Path).ToolPaths?.GetValueOrDefault(catalog.Id));
        if (program is null) { SetStatusMessage(Loc.Get(K.AstGrepMissing, ("program", catalog.Executable))); return; }

        var buffers = _app.Windows.SelectMany(w => w.Documents)
            .Where(d => !d.IsResults && !d.ReadOnly && d.Path is { } path && IsUnder(folder, path) && AstGrep.LanguageFor(d.Language.Id, Path.GetExtension(path)) == lang && StructuralPlanner.IsUtf8(d.Doc.Codec))
            .DistinctBy(d => d.Doc.Id).Take(StructuralMaxTabs).ToList();
        string Relative(string full) => Path.GetRelativePath(folder, full).Replace('\\', '/');
        var folderArgs = AstGrep.Arguments(pattern, rewrite, lang, false, buffers.Select(d => Relative(d.Path!)));
        var tool = AstGrep.Invocation(program, folder, folderArgs);
        if (!await ApproveToolAsync(tool, catalog.Title)) return;

        SetStatusMessage(Loc.Get(K.AstGrepRunning));
        var notes = new List<string>();
        var changes = new List<FileChange>();
        var found = new List<(string File, AstGrepMatch Match)>();
        bool incomplete = false;
        try
        {
            var disk = await AstGrep.RunAsync(tool with { Args = folderArgs }, null);
            if (disk.Matches.Count == 0 && disk.Errors.Length > 0) { SetStatusMessage(Loc.Get(K.AstGrepFailed, ("reason", FirstLine(disk.Errors)))); return; }
            incomplete |= disk.TimedOut || disk.LimitReached;
            if (disk.TimedOut) notes.Add(Loc.Get(K.AstGrepTimedOut, ("seconds", (int)(tool.Timeout?.TotalSeconds ?? 0))));
            if (disk.LimitReached) notes.Add(Loc.Get(K.AstGrepLimit, ("limit", AstGrep.MaxMatches)));
            foreach (var group in disk.Matches.GroupBy(m => Path.GetFullPath(m.File, folder), StringComparer.OrdinalIgnoreCase))
            {
                foreach (var m in group) found.Add((group.Key, m));
                if (rewrite.Length == 0) continue;
                try { if (StructuralPlanner.ForFile(group.Key, group) is { } change) changes.Add(change); }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    notes.Add(Loc.Get(K.AstGrepSkipped, ("file", Relative(group.Key)), ("reason", e is InvalidOperationException ? Loc.Get(K.AstGrepNotUtf8) : e.Message)));
                }
            }
            foreach (var open in buffers)
            {
                var doc = open.Doc;
                long revision = doc.Revision;
                var bytes = doc.ReadBytes(new ByteRange(0, doc.ByteLength));
                var run = await AstGrep.RunAsync(tool with { Args = AstGrep.Arguments(pattern, rewrite, lang, true, []) }, bytes);
                incomplete |= run.TimedOut || run.LimitReached;
                foreach (var m in run.Matches) found.Add((open.Path!, m));
                if (rewrite.Length == 0 || run.Matches.Count == 0) continue;
                if (doc.Revision != revision) { notes.Add(Loc.Get(K.AstGrepSkipped, ("file", Relative(open.Path!)), ("reason", Loc.Get(K.AstGrepChangedMeanwhile)))); continue; }
                try { if (StructuralPlanner.ForDocument(doc, run.Matches) is { } change) changes.Add(change); }
                catch (ArgumentException e) { notes.Add(Loc.Get(K.AstGrepSkipped, ("file", Relative(open.Path!)), ("reason", e.Message))); }
            }
        }
        catch (ToolException e)
        {
            SetStatusMessage(Loc.Get(K.AstGrepFailed, ("reason", e.Message)));
            return;
        }

        const string source = "ast-grep";
        foreach (var note in notes) _app.Output.Append(OutputChannel.Tasks, source, note);
        if (found.Count == 0) { SetStatusMessage(Loc.Get(K.AstGrepNone) + (incomplete ? " " + notes.FirstOrDefault() : "")); return; }
        if (rewrite.Length == 0)
        {
            _app.Output.Append(OutputChannel.Tasks, source, "> " + tool.CommandLine);
            foreach (var (file, m) in found) _app.Output.Append(OutputChannel.Tasks, source, $"{file}:{m.Line}:{m.Column + 1}: {FirstLine(m.Text)}");
            _outputLogs[source] = new OutputLog(MakeErrors.Value, folder, tab.Path);
            SetStatusMessage(Loc.Get(K.AstGrepFound, ("matches", found.Count), ("files", found.Select(f => f.File).Distinct(StringComparer.OrdinalIgnoreCase).Count())));
            ShowOutput(OutputChannel.Tasks);
            return;
        }
        if (changes.Count == 0) { SetStatusMessage(Loc.Get(K.AstGrepNothingToChange)); return; }
        var plan = ChangeBuilder.Proposal(Loc.Get(K.AstGrepProposal, ("pattern", pattern)), "ast-grep", [.. changes.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)]);
        SetStatusMessage(Loc.Get(K.AstGrepProposed, ("changes", plan.ChangeCount), ("files", plan.Files.Count)));
        StructuralDecision = ShowPlanAsync(Guid.NewGuid().ToString("N"), plan, null, CancellationToken.None);
        if (notes.Count > 0) _resultsView?.AddNote(string.Join(" ", notes), warning: true);
    }
}

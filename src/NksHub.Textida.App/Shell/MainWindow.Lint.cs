using System.Globalization;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Linters (P8.8.1): PHPStan, Psalm, golangci-lint, Clippy, clang-tidy, RuboCop, ESLint, Ruff, ShellCheck, SQLFluff and dart analyze run after a save (the save action kind "lint") or by the command Check File with Linter, never on a change.
/// The run is <see cref="Linters"/>; this part asks for the time limit and for the first-run confirmation, publishes what was found as underlines through the store that plugin diagnostics use,
/// and writes one line per finding to the Tasks tab, where a click opens the place. A run replaces the findings of the same linter, so a fixed problem is gone after the next run.
/// A run started by a save waits a moment for a further save of the file (the newer save replaces it); findings of a text that was edited meanwhile are moved with the text or dropped where their text changed.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<(Guid Document, string Linter), CancellationTokenSource> _lintJobs = [];
    private readonly Dictionary<(Guid Document, string Linter), CancellationTokenSource> _lintWaits = [];

    /// <summary>Tools, Check File with Linter: runs the linter that suits the file (see <see cref="Linters.Choose"/>).</summary>
    internal CommandOutcome RunLinter()
    {
        if (Active is not { Custom: null, Hex: null } tab || tab.Path is not { } path) return CommandOutcome.NotHandled;
        if (Linters.Choose(path, LoadSaveActions(path).ToolPaths) is not { } linter)
        {
            SetStatusMessage(Loc.Get(K.LintNone));
            return CommandOutcome.Handled;
        }
        _ = RunLinterAsync(tab, linter, interactive: true, manual: true);
        return CommandOutcome.Handled;
    }

    /// <summary>Runs one linter for a tab and shows the result. Everything it has to say goes to the status bar and the Tasks tab; it does not throw for a tool that is missing, slow or confusing.</summary>
    internal async Task RunLinterAsync(DocTab tab, CatalogLinter linter, bool interactive, bool manual)
    {
        if (tab.Path is not { } path) return;
        var set = LoadSaveActions(path);
        var timeout = set.LintTimeout ?? TimeSpan.FromSeconds(PluginLimitsPlaceholders.LintTimeoutSeconds);
        ToolInvocation tool;
        try { tool = Linters.Prepare(linter, path, timeout, set.ToolPaths?.GetValueOrDefault(linter.Id)); }
        catch (ToolException e)
        {
            ReportLint(linter, e.Missing ? Loc.Get(K.LintMissing, ("name", linter.Title), ("program", linter.Executable)) : Loc.Get(K.LintFailed, ("name", linter.Title), ("reason", e.Message)), manual);
            return;
        }
        if (!_app.ToolApprovals.IsApproved(tool))
        {
            if (!interactive)
            {
                ReportLint(linter, Loc.Get(K.LintFailed, ("name", linter.Title), ("reason", Loc.Get(K.SaveActionsToolNotApproved))), manual);
                return;
            }
            var request = new ChoiceRequest(Loc.Get(K.ToolConfirmTitle), Loc.Get(K.ToolConfirmMessage, ("title", linter.Title)),
                [new DialogButton(Loc.Get(K.ToolConfirmRun), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
                [Loc.Get(K.ToolConfirmCommand, ("command", tool.CommandLine)), Loc.Get(K.ToolConfirmDirectory, ("directory", tool.WorkingDirectory))]);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
            _app.ToolApprovals.Approve(tool);
        }

        // asked after the approval: the version request starts the program the user said yes to; the answer is kept per program file
        var version = await LinterVersions.CheckAsync(linter, tool, TimeSpan.FromSeconds(PluginLimitsPlaceholders.LintVersionTimeoutSeconds));
        if (!Tabs.Contains(tab)) return;

        var document = tab.Doc.Doc;
        var key = (document.Id, linter.Id);
        if (!manual)
        {
            if (!await LintDebounceAsync(key) || !Tabs.Contains(tab)) return;
            if (document.IsModified) return; // typed again since the save: the next save checks it
        }
        var cts = new CancellationTokenSource();
        if (_lintJobs.Remove(key, out var older)) older.Cancel(); // the newer run replaces the one still going
        _lintJobs[key] = cts;
        LintOutcome outcome;
        try { outcome = await Linters.RunAsync(document, linter, tool, cts.Token); }
        finally
        {
            if (_lintJobs.TryGetValue(key, out var current) && current == cts) _lintJobs.Remove(key);
        }
        bool replaced = cts.IsCancellationRequested;
        cts.Dispose();
        if (replaced || !Tabs.Contains(tab)) return;

        string name = linter.Title;
        switch (outcome.State)
        {
            case LintState.Done:
                // findings of the text as it was when the run started: the store moves them to the text as it is now and drops those whose text was changed since
                int shown = _app.Plugins.PublishOwnDiagnostics(tab, linter.Id, outcome.Revision, [.. outcome.Findings.Select(f => new Diagnostic(Editor(f.Severity), f.Message, f.Code, f.Range))]);
                string source = "lint:" + linter.Id;
                _outputLogs[source] = new OutputLog(MakeErrors.Value, tool.WorkingDirectory, path);
                _app.Output.Append(OutputChannel.Tasks, source, "> " + tool.CommandLine);
                foreach (var f in outcome.Findings)
                    _app.Output.Append(OutputChannel.Tasks, source, $"{Path.GetFullPath(path)}:{f.Line}:{f.Column}: {f.Severity.ToString().ToLowerInvariant()}: {FirstLine(f.Message)}{(f.Code is null ? "" : $" [{f.Code}]")}");
                if (outcome.Dropped > 0) _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.LintDropped, ("number", outcome.Dropped)));
                int stale = outcome.Findings.Count - shown;
                if (stale > 0) _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.LintStale, ("number", stale)));
                string result = outcome.Findings.Count == 0 ? Loc.Get(K.LintClean, ("name", name)) : shown == 0 ? Loc.Get(K.LintStale, ("number", stale)) : Loc.Get(K.LintFound, ("name", name), ("number", shown));
                ReportLint(linter, VersionNote(linter, version, failed: false) is { } note ? result + " " + note : result, manual);
                break;
            case LintState.TimedOut:
                ReportLint(linter, Loc.Get(K.LintTimedOut, ("name", name), ("seconds", timeout.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture))), manual);
                break;
            default:
                ReportLint(linter, Loc.Get(K.LintFailed, ("name", name), ("reason", VersionNote(linter, version, failed: true) is { } why ? outcome.Reason + " " + why : outcome.Reason)), manual);
                break;
        }
    }

    /// <summary>
    /// Says that the program is a version the check was not written for. A run that failed also says when the version could not be read, as that may be why; a run that worked does not, as the version request of some programs is simply silent.
    /// </summary>
    private static string? VersionNote(CatalogLinter linter, LinterVersionCheck check, bool failed) => check.State switch
    {
        LinterVersionState.TooOld => Loc.Get(K.LintVersionOld, ("name", linter.Title), ("found", check.Found!), ("min", check.Range!.Min)),
        LinterVersionState.TooNew => Loc.Get(K.LintVersionNew, ("name", linter.Title), ("found", check.Found!), ("min", check.Range!.Min), ("below", check.Range.Below ?? "")),
        LinterVersionState.Unreadable when failed => Loc.Get(K.LintVersionUnreadable, ("name", linter.Title), ("reason", check.Reason), ("min", check.Range!.Min), ("below", check.Range.Below ?? "")),
        _ => null,
    };

    /// <summary>Waits for a further save of the same file before a run that a save started; the newer wait replaces this one. False: this run was replaced.</summary>
    private async Task<bool> LintDebounceAsync((Guid Document, string Linter) key)
    {
        var wait = new CancellationTokenSource();
        if (_lintWaits.Remove(key, out var older)) older.Cancel();
        _lintWaits[key] = wait;
        try
        {
            await Task.Delay(PluginLimitsPlaceholders.LintOnSaveDebounceMilliseconds, wait.Token);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        finally
        {
            if (_lintWaits.TryGetValue(key, out var current) && current == wait) _lintWaits.Remove(key);
            wait.Dispose();
        }
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }

    private static DiagnosticSeverity Editor(ToolSeverity severity) => severity switch
    {
        ToolSeverity.Error => DiagnosticSeverity.Error,
        ToolSeverity.Warning => DiagnosticSeverity.Warning,
        ToolSeverity.Info => DiagnosticSeverity.Information,
        _ => DiagnosticSeverity.Hint,
    };

    /// <summary>Says how a run ended. The Tasks tab keeps every message; it is brought forward only for a run the user started.</summary>
    private void ReportLint(CatalogLinter linter, string text, bool manual)
    {
        _app.Output.Append(OutputChannel.Tasks, "lint:" + linter.Id, text);
        SetStatusMessage(text);
        if (manual) ShowOutput(OutputChannel.Tasks);
    }
}

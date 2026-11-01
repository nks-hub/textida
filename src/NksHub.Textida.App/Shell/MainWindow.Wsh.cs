using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>Tools, Run with Windows Script Host (PZ.4.4): the script file of the active tab is run by cscript.exe and its output goes to the Tasks channel of the output window. Windows only.</summary>
internal sealed partial class MainWindow
{
    /// <summary>The time limit of a run; tests shorten it.</summary>
    internal TimeSpan WshTimeout { get; set; } = WshBridge.DefaultTimeout;

    internal CommandOutcome RunWsh()
    {
        if (Active is not { Custom: null, Hex: null }) return CommandOutcome.NotHandled;
        _ = RunWshAsync();
        return CommandOutcome.Handled;
    }

    internal async Task RunWshAsync()
    {
        if (Active is not { } tab) return;
        if (!WshBridge.IsSupported)
        {
            SetStatusMessage(Loc.Get(K.WshWindowsOnly));
            return;
        }
        if (tab.Path is not { } path || !WshBridge.IsScript(path))
        {
            SetStatusMessage(Loc.Get(K.WshNotScript, ("types", string.Join(", ", WshBridge.Extensions))));
            return;
        }
        if (WshBridge.CscriptPath() is not { } cscript)
        {
            SetStatusMessage(Loc.Get(K.WshNoHost));
            return;
        }
        var tool = WshBridge.Invocation(cscript, path, WshTimeout);
        if (!_app.ToolApprovals.IsApproved(tool))
        {
            var ask = new ChoiceRequest(Loc.Get(K.WshConfirmTitle), Loc.Get(K.WshConfirmMessage, ("script", Path.GetFileName(path))),
                [new DialogButton(Loc.Get(K.WshRun), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
                [Loc.Get(K.ToolConfirmCommand, ("command", tool.CommandLine)), Loc.Get(K.ToolConfirmDirectory, ("directory", tool.WorkingDirectory)), Loc.Get(K.WshConfirmNote)], "tools.wsh");
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
            _app.ToolApprovals.Approve(tool);
        }
        string unsaved = tab.Doc.Doc.IsModified ? " " + Loc.Get(K.WshUnsaved) : "";
        SetStatusMessage(Loc.Get(K.WshRunning, ("script", Path.GetFileName(path))) + unsaved);
        _app.Output.Append(OutputChannel.Tasks, "cscript", "> " + tool.CommandLine);
        WshResult result;
        try { result = await WshBridge.RunAsync(tool); }
        catch (ToolException e)
        {
            SetStatusMessage(Loc.Get(K.WshFailedToStart, ("reason", e.Message)));
            return;
        }
        foreach (string line in result.Output.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Where(l => l.Length > 0)) _app.Output.Append(OutputChannel.Tasks, "cscript", line);
        foreach (string line in result.Errors.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n').Where(l => l.Length > 0)) _app.Output.Append(OutputChannel.Tasks, "cscript", line);
        _app.Output.Append(OutputChannel.Tasks, "cscript", result.TimedOut ? Loc.Get(K.WshTimedOut, ("seconds", (int)WshTimeout.TotalSeconds)) : Loc.Get(K.WshExit, ("code", result.ExitCode)));
        ShowOutput(OutputChannel.Tasks);
        SetStatusMessage(result.TimedOut ? Loc.Get(K.WshTimedOut, ("seconds", (int)WshTimeout.TotalSeconds)) : Loc.Get(K.WshExit, ("code", result.ExitCode)));
    }
}

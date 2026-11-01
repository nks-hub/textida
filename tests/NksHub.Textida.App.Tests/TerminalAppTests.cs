using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.1: the terminal panel starts the shell of the setting, takes input, follows its size and ends the shell with the panel.</summary>
[Collection("LocState")]
public class TerminalAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public TerminalAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 20_000)
    {
        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < timeoutMs; await Task.Delay(50))
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
        }
        return condition();
    }

    [AvaloniaFact]
    public async Task The_panel_runs_the_shell_of_the_setting_takes_pasted_input_and_closing_it_ends_the_process()
    {
        if (!OperatingSystem.IsWindows()) return; // the shells of macOS and Linux are checked on those systems
        _app.Workbench.Set("terminal.shell", Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /q");
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("a.txt", "x"), null);

        w.ToggleTerminal(true);
        Assert.True(w.TerminalPanelOpen);
        var session = w.TerminalSessionNow!;
        Assert.True(await Until(() => session.Screen.ScreenText().Contains('>') && session.Screen.ScreenText().Contains(_temp.Path[..3])), "the prompt");
        Assert.Contains(Path.GetFileName(_temp.Path), session.Screen.ScreenText()); // it started in the folder of the open file

        w.TerminalViewControl.Paste("echo from the panel\n");
        Assert.True(await Until(() => session.Screen.ScreenText().Split('\n').Count(l => l.Contains("from the panel")) >= 2), "the echo: " + session.Screen.ScreenText());

        int id = session.ProcessId;
        w.ToggleTerminal(false);
        Assert.Null(w.TerminalSessionNow);
        Assert.False(w.TerminalPanelOpen);
        Assert.True(await Until(() => !Process.GetProcesses().Any(p => p.Id == id)), "the shell is gone after the panel closed");
    }

    [AvaloniaFact]
    public async Task A_shell_that_ends_by_itself_offers_a_restart_and_a_missing_shell_says_so()
    {
        if (!OperatingSystem.IsWindows()) return;
        _app.Workbench.Set("terminal.shell", Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /q");
        var w = _app.CreateWindow(null);
        w.ToggleTerminal(true);
        var first = w.TerminalSessionNow!;
        Assert.True(await Until(() => first.Screen.ScreenText().Contains('>')), "the prompt");

        w.TerminalViewControl.Paste("exit\n");
        Assert.True(await Until(() => !first.Running), "the shell ends");
        Assert.True(await Until(() => w.StatusMessageText.Contains("0")), "the end is said");

        w.StartTerminal();
        Assert.NotSame(first, w.TerminalSessionNow);
        Assert.True(w.TerminalSessionNow!.Running);
        w.ToggleTerminal(false);

        _app.Workbench.Set("terminal.shell", "no-such-shell-program-xyz");
        w.ToggleTerminal(true);
        Assert.Null(w.TerminalSessionNow);
        Assert.Contains("no-such-shell-program-xyz", w.StatusMessageText);
        w.ToggleTerminal(false);
    }
}

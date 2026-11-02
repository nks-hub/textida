using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Terminal;

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

    /// <summary>The shell of the account ($SHELL, else /bin/sh) without its startup files, so the prompt and the output do not depend on the account.</summary>
    private static string UnixShell()
    {
        string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s && File.Exists(s) ? s : "/bin/sh";
        return Path.GetFileName(shell) switch
        {
            "zsh" => $"\"{shell}\" -f",
            "bash" => $"\"{shell}\" --norc --noprofile",
            _ => $"\"{shell}\"",
        };
    }

    /// <summary>Pastes a command and presses Enter as a key: zsh and bash 5.1 turn bracketed paste on, and there a pasted newline is text, not Enter.</summary>
    private static void Run(MainWindow w, TerminalSession session, string command)
    {
        w.TerminalViewControl.Paste(command);
        session.Send(TerminalKeys.Sequence(TerminalKey.Enter, TerminalModifiers.None, false));
    }

    [AvaloniaFact]
    public async Task On_unix_the_panel_runs_the_shell_follows_a_resize_and_closing_it_ends_the_process()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the pty of macOS and Linux; Windows runs cmd.exe above");
        _app.Workbench.Set("terminal.shell", UnixShell());
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.File("a.txt", "x"), null);

        w.ToggleTerminal(true);
        Assert.True(w.TerminalPanelOpen);
        var session = w.TerminalSessionNow!;
        Run(w, session, "echo from-$((6*7)); pwd");
        Assert.True(await Until(() => session.Screen.ScreenText().Contains("from-42")), "the echo: " + session.Screen.ScreenText());
        Assert.True(await Until(() => session.Screen.ScreenText().Contains(Path.GetFileName(_temp.Path))), "it started in the folder of the open file: " + session.Screen.ScreenText());

        session.Resize(100, 30);
        Run(w, session, "stty size");
        Assert.True(await Until(() => session.Screen.ScreenText().Contains("30 100")), "the pty follows the resize: " + session.Screen.ScreenText());

        int id = session.ProcessId;
        w.ToggleTerminal(false);
        Assert.Null(w.TerminalSessionNow);
        Assert.False(w.TerminalPanelOpen);
        Assert.True(await Until(() => !Process.GetProcesses().Any(p => p.Id == id)), "the shell is gone after the panel closed");
    }

    [AvaloniaFact]
    public async Task On_unix_a_shell_that_exits_says_its_code_offers_a_restart_and_a_missing_shell_says_so()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the pty of macOS and Linux; Windows runs cmd.exe above");
        _app.Workbench.Set("terminal.shell", UnixShell());
        var w = _app.CreateWindow(null);
        w.ToggleTerminal(true);
        var first = w.TerminalSessionNow!;

        Run(w, first, "exit 5");
        Assert.True(await Until(() => !first.Running), "the shell ends");
        Assert.True(await Until(() => first.ExitCode is not null), "the exit code is reported");
        Assert.Equal(5, first.ExitCode);
        Assert.True(await Until(() => w.StatusMessageText.Contains('5')), "the end is said: " + w.StatusMessageText);

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

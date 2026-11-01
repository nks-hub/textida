using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.4.4: the real cscript.exe of Windows runs JScript and VBScript files of the user and their output reaches the output window. Windows only.</summary>
public class WshBridgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wsh-" + Guid.NewGuid().ToString("N")[..8]);

    public WshBridgeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Script(string name, string text)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public async Task Jscript_and_vbscript_run_in_the_real_host_with_their_output_and_exit_codes()
    {
        if (WshBridge.CscriptPath() is not { } cscript) return; // not Windows

        var js = await WshBridge.RunAsync(WshBridge.Invocation(cscript, Script("a.js", "WScript.Echo('hello ' + (1 + 2));\nWScript.Quit(3);")));
        Assert.Equal(("hello 3", 3, false), (js.Output.Trim(), js.ExitCode, js.TimedOut));

        var vbs = await WshBridge.RunAsync(WshBridge.Invocation(cscript, Script("b.vbs", "WScript.Echo \"vb \" & UCase(\"ok\")")));
        Assert.Equal(("vb OK", 0), (vbs.Output.Trim(), vbs.ExitCode));

        var broken = await WshBridge.RunAsync(WshBridge.Invocation(cscript, Script("c.js", "var = ;")));
        Assert.NotEqual(0, broken.ExitCode);
        Assert.Contains("c.js", broken.Output + broken.Errors);
    }

    [Fact]
    public async Task A_script_that_does_not_end_is_ended_at_the_limit()
    {
        if (WshBridge.CscriptPath() is not { } cscript) return;
        var result = await WshBridge.RunAsync(WshBridge.Invocation(cscript, Script("loop.js", "while (true) {}"), TimeSpan.FromMilliseconds(800)));
        Assert.True(result.TimedOut);
    }

    [Fact]
    public void The_approval_follows_the_content_of_the_script_and_only_scripts_of_the_host_are_accepted()
    {
        if (WshBridge.CscriptPath() is not { } cscript) return;
        string path = Script("d.js", "WScript.Echo(1);");
        string first = WshBridge.Invocation(cscript, path).Fingerprint;
        Assert.Equal(first, WshBridge.Invocation(cscript, path).Fingerprint);
        File.WriteAllText(path, "WScript.Echo(2);");
        Assert.NotEqual(first, WshBridge.Invocation(cscript, path).Fingerprint);
        Assert.StartsWith(Environment.SystemDirectory, cscript, StringComparison.OrdinalIgnoreCase); // never found through the PATH
        Assert.True(WshBridge.IsScript("x.VBS") && WshBridge.IsScript("x.wsf") && !WshBridge.IsScript("x.ps1"));
    }
}

[Collection("LocState")]
public class WshAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly Shell.MainWindow _w;

    public WshAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task The_command_asks_once_per_content_runs_the_script_and_writes_its_output_to_the_window()
    {
        if (!WshBridge.IsSupported) return;
        var tab = _w.OpenFile(_temp.File("run.js", "WScript.Echo('from wsh');"), null);
        _dialogs.Answers.Enqueue(1);
        await _w.RunWshAsync();
        Assert.DoesNotContain(_app.Output.Read(OutputChannel.Tasks), l => l.Text == "from wsh");

        _dialogs.Answers.Enqueue(0);
        await _w.RunWshAsync();
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Source == "cscript" && l.Text == "from wsh");
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Text == Loc.Get(K.WshExit, ("code", 0)));

        int asked = _dialogs.Asked.Count;
        await _w.RunWshAsync(); // the same content: no question
        Assert.Equal(asked, _dialogs.Asked.Count);

        tab.Editor!.Document.Text = "WScript.Echo('changed');";
        _w.SaveAsync(tab).Wait();
        _dialogs.Answers.Enqueue(0);
        await _w.RunWshAsync();
        Assert.Equal(asked + 1, _dialogs.Asked.Count);
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Text == "changed");
    }

    [AvaloniaFact]
    public async Task A_tab_that_is_not_a_script_is_refused_without_a_question()
    {
        if (!WshBridge.IsSupported) return;
        _w.OpenFile(_temp.File("note.txt", "x"), null);
        await _w.RunWshAsync();
        Assert.Contains(".vbs", _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }
}

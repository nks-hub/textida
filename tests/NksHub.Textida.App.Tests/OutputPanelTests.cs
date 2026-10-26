using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.3: the output channels, the output window and full screen.</summary>
[Collection("LocState")]
public class OutputPanelTests : IDisposable
{
    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string OutputDir => Path.Combine(_temp.Path, "output");

    [Fact]
    public void Channels_keep_their_purposes_apart_and_a_new_instance_reads_what_an_old_one_wrote()
    {
        var first = new OutputChannels(OutputDir);
        first.Append(OutputChannel.Tasks, "build", "compiling\nlinking");
        first.Append(OutputChannel.Search, "find", "3 matches");

        var again = new OutputChannels(OutputDir);
        Assert.Equal(["compiling", "linking"], again.Read(OutputChannel.Tasks).Select(l => l.Text));
        Assert.Equal(["build", "build"], again.Read(OutputChannel.Tasks).Select(l => l.Source));
        Assert.Equal(["3 matches"], again.Read(OutputChannel.Search).Select(l => l.Text));

        again.Clear(OutputChannel.Tasks);
        Assert.Empty(again.Read(OutputChannel.Tasks));
        Assert.Single(again.Read(OutputChannel.Search));
    }

    [Fact]
    public void A_line_cut_off_in_the_middle_is_read_as_plain_text_and_the_log_keeps_working()
    {
        var channels = new OutputChannels(OutputDir);
        channels.Append(OutputChannel.Tasks, "t", "whole");
        File.AppendAllText(channels.PathOf(OutputChannel.Tasks), "half-writ", new UTF8Encoding(false));
        channels.Append(OutputChannel.Tasks, "t", "next");
        var lines = channels.Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Equal("whole", lines[0]);
        Assert.Contains("next", lines[^1]);
    }

    [Fact]
    public void Output_of_a_tool_that_was_killed_stays_in_the_channel()
    {
        var channels = new OutputChannels(OutputDir);
        using var reader = new CountdownEvent(3);
        var psi = new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
            "[Console]::Out.WriteLine('step one'); [Console]::Out.WriteLine('step two'); [Console]::Out.WriteLine('ERROR: access violation'); Start-Sleep -Seconds 120"])
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var tool = Process.Start(psi)!;
        tool.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            channels.Append(OutputChannel.Tasks, "tool", e.Data);
            if (!reader.IsSet) reader.Signal();
        };
        tool.BeginOutputReadLine();
        Assert.True(reader.Wait(TimeSpan.FromSeconds(60)), "the tool wrote nothing");

        tool.Kill(entireProcessTree: true);
        Assert.True(tool.WaitForExit(30_000));
        Assert.False(tool.HasExited && tool.ExitCode == 0);

        var after = new OutputChannels(OutputDir).Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Equal(["step one", "step two", "ERROR: access violation"], after);
    }

    [AvaloniaFact]
    public void The_output_window_is_hidden_at_start_and_opens_and_closes_from_the_menu_and_the_key()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        Assert.False(w.OutputPanelOpen);
        Assert.False(w.OutputPanelControl.IsVisible);
        var commands = w.MenuModel.SelectMany(g => g.Items).OfType<MenuCommand>().Select(c => c.CommandId).ToList();
        Assert.Contains("nks.view.toggleOutput", commands);
        Assert.Contains("nks.view.fullScreen", commands);

        TestApp.Press(w, Key.Y, TestApp.Mod | RawInputModifiers.Shift);
        Assert.True(w.OutputPanelOpen);
        TestApp.Press(w, Key.Y, TestApp.Mod | RawInputModifiers.Shift);
        Assert.False(w.OutputPanelOpen);

        w.ToggleOutput(true);
        TestApp.Press(w, Key.Escape);
        Assert.False(w.OutputPanelOpen);
    }

    [AvaloniaFact]
    public async Task The_window_shows_the_channels_separately_and_follows_new_lines()
    {
        _app = TestApp.Prepare(_temp);
        _app.Output.Append(OutputChannel.Tasks, "tool", "left by a tool that crashed");
        var w = _app.CreateWindow(null);

        w.ShowOutput(OutputChannel.Tasks);
        Assert.Contains("left by a tool that crashed", w.OutputPanelText);
        Assert.DoesNotContain("matches", w.OutputPanelText);

        _app.Output.Append(OutputChannel.Search, "find", "7 matches");
        w.ShowOutput(OutputChannel.Search);
        Assert.Contains("7 matches", w.OutputPanelText);
        Assert.DoesNotContain("crashed", w.OutputPanelText);

        _app.Output.Append(OutputChannel.Search, "find", "more");
        var sw = Stopwatch.StartNew();
        while (!w.OutputPanelText.Contains("more"))
        {
            Assert.True(sw.ElapsedMilliseconds < 10_000, "the window did not follow the channel");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public void Full_screen_toggles_with_f11_and_returns_to_the_state_before()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        Assert.False(w.IsFullScreen);
        TestApp.Press(w, Key.F11);
        Assert.True(w.IsFullScreen);
        TestApp.Press(w, Key.F11);
        Assert.False(w.IsFullScreen);
    }

    [AvaloniaFact]
    public void Texts_of_the_output_window_exist_in_every_language()
    {
        _app = TestApp.Prepare(_temp);
        foreach (var tag in Loc.Languages)
        {
            Loc.SetLanguage(tag);
            foreach (var key in new[] { K.PanelOutputTasks, K.PanelOutputSearch, K.PanelOutputClear, K.PanelOutputClose, K.PanelOutputEmpty, K.A11yOutputPanel, K.A11yOutputText, K.A11yOutputTasks, K.A11yOutputSearch })
                Assert.NotEqual(key, Loc.Get(key));
        }
    }
}

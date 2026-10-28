using System.Diagnostics;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>Child processes that must not outlive a killed editor: the job object on Windows, the leftover files elsewhere, the parent watch of a plugin.</summary>
public class ChildProcessTests : IDisposable
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);
    private static readonly ProcessKey NoSuchProcess = new(2_000_000_000, 1);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-children-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<Process> _started = [];

    public ChildProcessTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        foreach (var process in _started)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private static ProcessStartInfo Dotnet(string dll, params string[] args)
    {
        var path = Path.Combine(AppContext.BaseDirectory, dll);
        Assert.True(File.Exists(path), path);
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(path);
        foreach (var a in args) info.ArgumentList.Add(a);
        return info;
    }

    private Process StartSleeper()
    {
        var info = Dotnet("NksHub.Textida.ToolEcho.dll", "--sleep");
        info.RedirectStandardOutput = true;
        var process = Process.Start(info)!;
        _started.Add(process);
        Assert.Equal("started", process.StandardOutput.ReadLine());
        return process;
    }

    private static ProcessKey Me => ChildProcessGuard.Self ?? throw new InvalidOperationException("The start time of this process is not readable.");

    private static ProcessKey KeyOf(Process process) => new(process.Id, process.StartTime.ToUniversalTime().Ticks);

    private string Entry(string name, ProcessKey child, ProcessKey owner)
    {
        var path = Path.Combine(_folder, name + ".child");
        ChildProcessGuard.Write(path, child, owner);
        return path;
    }

    // ---- the editor is killed ----

    /// <summary>Starts the harness as a stand-in for the editor, waits for the id of the child it started and kills the harness. Returns the child.</summary>
    private Process KillParentOf(string mode)
    {
        var pidFile = Path.Combine(_folder, mode + ".pid");
        var parent = Process.Start(Dotnet("NksHub.Textida.Journal.CrashHarness.dll", mode, pidFile))!;
        _started.Add(parent);
        WaitForFile(parent, pidFile);
        var child = Adopt(int.Parse(File.ReadAllText(pidFile)));
        parent.Kill();
        parent.WaitForExit();
        return child;
    }

    private static void WaitForFile(Process parent, string path)
    {
        var waited = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            Assert.False(parent.HasExited, "the stand-in for the editor ended on its own");
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "the stand-in for the editor did not start its children");
            Thread.Sleep(50);
        }
    }

    /// <summary>Opens a process the harness started so that the test ends it in the end, whatever the outcome.</summary>
    private Process Adopt(int pid)
    {
        var process = Process.GetProcessById(pid);
        _started.Add(process);
        Assert.False(process.HasExited);
        return process;
    }

    [Fact]
    public void A_tracked_child_ends_when_the_editor_is_killed_on_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var child = KillParentOf("guard-parent");
        Assert.True(child.WaitForExit(10_000), "the child outlived the killed editor");
    }

    [Fact]
    public void A_grandchild_of_a_tracked_child_ends_with_it_and_an_untracked_process_of_the_same_program_lives_on_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var file = Path.Combine(_folder, "tree.pids");
        var parent = Process.Start(Dotnet("NksHub.Textida.Journal.CrashHarness.dll", "guard-tree-parent", file))!;
        _started.Add(parent);
        WaitForFile(parent, file);
        WaitForFile(parent, file + ".grandchild");
        var ids = File.ReadAllText(file).Split(' ');
        var tracked = Adopt(int.Parse(ids[0]));
        var untracked = Adopt(int.Parse(ids[1]));
        var grandchild = Adopt(int.Parse(File.ReadAllText(file + ".grandchild")));
        Assert.Equal(untracked.ProcessName, tracked.ProcessName);

        parent.Kill();
        parent.WaitForExit();

        Assert.True(tracked.WaitForExit(10_000), "the tracked child outlived the killed editor");
        Assert.True(grandchild.WaitForExit(10_000), "the grandchild of the tracked child outlived the killed editor");
        Assert.False(untracked.WaitForExit(1_000), "the untracked process was ended with the editor");
    }

    [Fact]
    public void A_plugin_that_watches_its_parent_ends_by_itself_when_the_editor_is_killed()
    {
        var child = KillParentOf("guard-watched-parent");
        Assert.True(child.WaitForExit(10_000), "the plugin did not notice that the editor is gone");
        Assert.True(File.Exists(Path.Combine(_folder, "guard-watched-parent.pid.gone")), "the plugin ended without its watch seeing the editor go"); // the exit code of a process that is not ours cannot be read
    }

    // ---- the files of the children ----

    [Fact]
    public void A_leftover_of_a_gone_editor_that_still_runs_as_the_same_process_is_ended()
    {
        var sleeper = StartSleeper();
        var path = Entry("one", KeyOf(sleeper), NoSuchProcess);

        Assert.Equal(1, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.True(sleeper.WaitForExit(10_000), "the leftover was not ended");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_recycled_process_id_is_spared()
    {
        var sleeper = StartSleeper();
        var key = KeyOf(sleeper);
        var path = Entry("one", key with { StartTicks = key.StartTicks - TimeSpan.FromHours(1).Ticks }, NoSuchProcess); // the id was another process when the file was written

        Assert.Equal(0, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.False(sleeper.HasExited);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_start_time_inside_the_tolerance_is_the_same_process()
    {
        var sleeper = StartSleeper();
        var key = KeyOf(sleeper);
        Entry("one", key with { StartTicks = key.StartTicks + TimeSpan.FromMilliseconds(500).Ticks }, NoSuchProcess);

        Assert.Equal(1, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.True(sleeper.WaitForExit(10_000));
    }

    [Fact]
    public void A_leftover_of_an_editor_that_still_runs_is_kept()
    {
        var sleeper = StartSleeper();
        var path = Entry("one", KeyOf(sleeper), Me);

        Assert.Equal(0, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.False(sleeper.HasExited);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void An_editor_whose_process_id_was_taken_by_a_later_process_is_gone()
    {
        var sleeper = StartSleeper();
        var self = Me;
        Entry("one", KeyOf(sleeper), self with { StartTicks = self.StartTicks - TimeSpan.FromHours(1).Ticks });

        Assert.Equal(1, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.True(sleeper.WaitForExit(10_000));
    }

    [Fact]
    public void A_file_that_is_not_an_entry_is_removed_and_the_sweep_goes_on()
    {
        var junk = Path.Combine(_folder, "junk.child");
        File.WriteAllText(junk, "not numbers");
        var sleeper = StartSleeper();
        Entry("one", KeyOf(sleeper), NoSuchProcess);

        Assert.Equal(1, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.False(File.Exists(junk));
        Assert.True(sleeper.WaitForExit(10_000));
    }

    [Fact]
    public void The_entry_of_a_child_that_is_gone_is_removed_and_nothing_else_is_touched()
    {
        var path = Entry("one", NoSuchProcess, NoSuchProcess);

        Assert.Equal(0, ChildProcessGuard.Sweep(_folder, Tolerance));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_registered_child_is_listed_until_it_ends()
    {
        var sleeper = StartSleeper();
        ChildProcessGuard.Register(sleeper, _folder, Me);
        var path = Path.Combine(_folder, sleeper.Id + ".child");
        Assert.True(File.Exists(path));
        Assert.Equal(0, ChildProcessGuard.Sweep(_folder, Tolerance)); // its editor runs: the file stays and the child lives
        Assert.True(File.Exists(path));
        Assert.False(sleeper.HasExited);

        sleeper.Kill();
        var waited = Stopwatch.StartNew();
        while (File.Exists(path) && waited.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(50);
        Assert.False(File.Exists(path));
    }

    // ---- the plugin looks at its parent ----

    [Fact]
    public void The_watch_sees_a_parent_that_is_gone_and_one_that_is_not()
    {
        var self = Me;
        Assert.False(new ParentWatch(self, TimeSpan.FromMilliseconds(50), Tolerance).IsParentGone());
        Assert.True(new ParentWatch(self with { StartTicks = self.StartTicks - TimeSpan.FromHours(1).Ticks }, TimeSpan.FromMilliseconds(50), Tolerance).IsParentGone());
        Assert.True(new ParentWatch(NoSuchProcess, TimeSpan.FromMilliseconds(50), Tolerance).IsParentGone());
    }

    [Fact]
    public async Task The_watch_completes_for_a_gone_parent_and_stops_when_cancelled()
    {
        await new ParentWatch(NoSuchProcess, TimeSpan.FromMilliseconds(20), Tolerance).WaitUntilGoneAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        using var stop = new CancellationTokenSource();
        var alive = new ParentWatch(Me, TimeSpan.FromMilliseconds(20), Tolerance).WaitUntilGoneAsync(stop.Token);
        await Task.Delay(100);
        Assert.False(alive.IsCompleted);
        stop.Cancel();
        await alive.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_plugin_gets_the_editor_and_the_asked_for_watch_in_its_environment()
    {
        var info = new ProcessStartInfo("unused");
        ChildProcessGuard.PassTo(info);
        ParentWatch.Apply(info, TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(2000));

        var self = Me;
        Assert.Equal(self.Pid.ToString(), info.Environment[ChildProcessGuard.ParentPidVariable]);
        Assert.Equal(self.StartTicks.ToString(), info.Environment[ChildProcessGuard.ParentStartVariable]);
        Assert.Equal("1500", info.Environment[ParentWatch.PollVariable]);
        Assert.Equal("2000", info.Environment[ParentWatch.ToleranceVariable]);
    }
}

public class StderrPumpTests
{
    private static async Task<List<string>> PumpAsync(string text, int capacity, Action<string>? sink = null, Func<string, string>? filter = null, int maxLineChars = int.MaxValue)
    {
        var tail = new Queue<string>();
        await StderrPump.RunAsync(new StringReader(text), tail, capacity, sink, filter, maxLineChars);
        return [.. tail];
    }

    [Fact]
    public async Task Keeps_the_last_lines_only()
    {
        Assert.Equal(["c", "d"], await PumpAsync("a\nb\nc\nd\n", capacity: 2));
        Assert.Equal(["a", "b"], await PumpAsync("a\nb", capacity: 5));
        Assert.Empty(await PumpAsync("a\nb\n", capacity: 0));
    }

    [Fact]
    public async Task Filters_then_cuts_a_line_and_hands_the_result_to_the_sink()
    {
        var seen = new List<string>();
        var tail = await PumpAsync("token=abc\nshort\n", capacity: 10, seen.Add, line => line.Replace("abc", "***"), maxLineChars: 7);

        Assert.Equal(["token=*", "short"], tail);
        Assert.Equal(tail, seen);
    }

    [Fact]
    public async Task A_sink_that_throws_does_not_stop_the_reading()
    {
        var calls = 0;
        var tail = await PumpAsync("a\nb\nc\n", capacity: 10, _ => { calls++; throw new InvalidOperationException("log is broken"); });

        Assert.Equal(["a", "b", "c"], tail);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task A_stream_that_breaks_or_is_closed_ends_the_pump_quietly_with_what_was_read()
    {
        var tail = new Queue<string>();
        await StderrPump.RunAsync(new BrokenReader(new ObjectDisposedException("pipe")), tail, 10);
        await StderrPump.RunAsync(new BrokenReader(new IOException("pipe closed")), tail, 10);
        await StderrPump.RunAsync(new BrokenReader(new InvalidOperationException("not redirected")), tail, 10);

        Assert.Equal(["first", "first", "first"], tail);
    }

    [Fact]
    public async Task Reads_the_standard_error_of_a_real_process()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--both");
        using var process = Process.Start(info)!;
        var tail = new Queue<string>();
        var pump = StderrPump.RunAsync(process.StandardError, tail, 5);
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        await pump.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["to-err"], tail);
    }

    private sealed class BrokenReader(Exception failure) : TextReader
    {
        private bool _first = true;

        public override Task<string?> ReadLineAsync()
        {
            if (!_first) return Task.FromException<string?>(failure);
            _first = false;
            return Task.FromResult<string?>("first");
        }
    }
}

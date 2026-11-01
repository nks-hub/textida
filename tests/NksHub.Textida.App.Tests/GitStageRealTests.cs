using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P6.2.2 with the real thing: the Git plugin process of the app stages the hunk at the caret of a file in a real temporary repository, and the hunk is in the real index (<c>git diff --cached</c>).</summary>
[Collection("LocState")]
[Trait("Category", "RealTools")]
public class GitStageRealTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _repo;

    public GitStageRealTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _repo = Path.Combine(_temp.Path, "repo");
        Directory.CreateDirectory(_repo);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string Git(params string[] args)
    {
        var info = new ProcessStartInfo(ToolLocator.Find("git")!) { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "core.autocrlf=false" }.Concat(args)) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        string output = p.StandardOutput.ReadToEnd();
        string error = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(30_000));
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {error}");
        return output.Replace("\r\n", "\n");
    }

    private static string Lines(Func<int, string?> change) => string.Concat(Enumerable.Range(1, 12).Select(i => (change(i) ?? $"line {i}") + "\n"));

    [AvaloniaFact]
    public async Task Stage_Hunk_at_the_caret_puts_only_that_change_in_the_real_index()
    {
        Assert.SkipWhen(ToolLocator.Find("git") is null, "git is not installed here (not found in PATH).");
        Git("init", "-q");
        string file = Path.Combine(_repo, "f.txt");
        File.WriteAllText(file, Lines(_ => null));
        Git("add", "f.txt");
        Git("commit", "-q", "-m", "first");
        string edited = Lines(i => i switch { 2 => "line 2 changed", 10 => "line 10 changed", _ => null });
        File.WriteAllText(file, edited);

        var tab = _w.OpenFile(file, null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.CaretOffset = edited.IndexOf("line 10 changed", StringComparison.Ordinal) + 3;

        var run = _app.Workbench.Commands.ExecuteAsync("nks.git.stageHunk", new ShellContext(_w, forMenu: false)).AsTask();
        var sw = Stopwatch.StartNew();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (run.IsCompleted && Git("diff", "--cached", "--name-only").Contains("f.txt", StringComparison.Ordinal)) break;
            Assert.True(sw.Elapsed.TotalSeconds < 60, "The hunk did not reach the index.");
            await Task.Delay(50);
        }
        Assert.Equal(CommandOutcome.Handled, await run);

        string cached = Git("diff", "--cached", "-U0", "--no-color");
        Assert.Equal(1, cached.Split('\n').Count(l => l.StartsWith("@@ ", StringComparison.Ordinal)));
        Assert.Contains("-line 10\n+line 10 changed\n", cached);
        Assert.DoesNotContain("line 2 changed", cached);
        Assert.Equal(edited, File.ReadAllText(file).Replace("\r\n", "\n")); // the working file is not touched
        string unstaged = Git("diff", "-U0", "--no-color");
        Assert.Contains("+line 2 changed", unstaged); // the other change is still waiting
        Assert.DoesNotContain("line 10 changed", unstaged);
    }
}

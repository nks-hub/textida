using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.10.1: the operating system limit on a plugin process. The Windows job is tried on a real command shell; the macOS profile is checked as text (it can only run on a Mac).</summary>
public class ProcessSandboxTests
{
    /// <summary>Starts a command shell that waits for its input, limits it, then tells it to run a second shell; returns what the second one printed and what it complained about.</summary>
    private static (string Output, string Error) RunInnerShell(ProcessSandboxLevel level)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/q")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default,
        };
        using var shell = Process.Start(info)!;
        try
        {
            Assert.Equal(level != ProcessSandboxLevel.Off, ProcessSandbox.Apply(shell, level));
            shell.StandardInput.WriteLine("cmd.exe /c echo inner-started");
            shell.StandardInput.WriteLine("exit");
            var error = shell.StandardError.ReadToEndAsync();
            string output = shell.StandardOutput.ReadToEnd();
            Assert.True(shell.WaitForExit(15_000));
            return (output, error.GetAwaiter().GetResult());
        }
        finally
        {
            if (!shell.HasExited) shell.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void A_strict_process_cannot_start_a_program_and_a_contained_or_free_one_can()
    {
        if (!OperatingSystem.IsWindows()) return; // the job object limit is Windows only

        Assert.Contains("inner-started", RunInnerShell(ProcessSandboxLevel.Off).Output);
        Assert.Contains("inner-started", RunInnerShell(ProcessSandboxLevel.Contained).Output);

        var (output, error) = RunInnerShell(ProcessSandboxLevel.Strict);
        Assert.DoesNotContain("inner-started", output);
        Assert.NotEqual("", error.Trim()); // the shell reports that it could not start the program
    }

    [Fact]
    public void The_macos_profiles_leave_the_files_alone_and_the_start_info_is_untouched_where_there_is_no_sandbox_exec()
    {
        Assert.Null(ProcessSandbox.MacProfile(ProcessSandboxLevel.Off));
        Assert.Contains("(allow default)", ProcessSandbox.MacProfile(ProcessSandboxLevel.Contained));
        Assert.DoesNotContain("process-fork", ProcessSandbox.MacProfile(ProcessSandboxLevel.Contained));
        Assert.Contains("(deny process-fork)", ProcessSandbox.MacProfile(ProcessSandboxLevel.Strict));
        Assert.DoesNotContain("file-", ProcessSandbox.MacProfile(ProcessSandboxLevel.Strict));

        var info = new ProcessStartInfo("/opt/plugin/run", ["--a", "b c"]);
        bool wrapped = ProcessSandbox.Wrap(info, ProcessSandboxLevel.Strict);
        if (OperatingSystem.IsMacOS() && File.Exists(ProcessSandbox.MacProgram))
        {
            Assert.True(wrapped);
            Assert.Equal(ProcessSandbox.MacProgram, info.FileName);
            Assert.Equal(["-p", ProcessSandbox.MacProfile(ProcessSandboxLevel.Strict)!, "/opt/plugin/run", "--a", "b c"], info.ArgumentList);
        }
        else
        {
            Assert.False(wrapped);
            Assert.Equal("/opt/plugin/run", info.FileName);
            Assert.Equal(["--a", "b c"], info.ArgumentList);
        }
    }
}

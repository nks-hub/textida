using System.Diagnostics;
using NksHub.Textida.Tests;
using Xunit.Abstractions;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P7.1.4 at the size of its condition: the process that edits a 50 GiB document is killed from outside during small edits, in the middle of a paste of a GiB, and when the streamed save
/// has written half of the file. Recovery has to give back the work except for the last 2 seconds and leave the file of the user the original. The harness writes the file, kills the
/// child, compares the recovered document and the file with a model byte by byte and prints the numbers. It needs about 90 GiB of free disk and a few minutes per kill, so it runs only
/// where <c>TEXTIDA_HUGE=1</c> is set.
/// </summary>
[Trait("Category", "Perf")]
[Collection("Crash harness")]
public sealed class HugeRecoveryTests(ITestOutputHelper output)
{
    private void KillAt(string point)
    {
        if (!HugeText.Enabled)
        {
            output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        string dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Journal.CrashHarness.dll");
        Assert.True(File.Exists(dll), dll);
        string work = Path.Combine(Path.GetTempPath(), "textida-huge-" + Guid.NewGuid().ToString("N")[..8]);
        long size = HugeText.Size(50L << 30);
        int paste = (int)Math.Max(2, 256 * HugeText.Scale);
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { dll, "huge", "--work", work, "--size", size.ToString(), "--paste", paste.ToString(), "--points", point }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        try
        {
            bool done = proc.WaitForExit(27 * 60_000);
            if (!done) proc.Kill(true);
            output.WriteLine(stdout.Result);
            Assert.True(done, "the harness did not finish in 27 minutes");
            Assert.True(proc.ExitCode == 0, stderr.Result);
            Assert.Contains("failures 0", stdout.Result);
        }
        finally
        {
            if (!proc.HasExited) proc.Kill(true);
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Killing_the_process_among_small_edits_of_a_50_GiB_document_recovers_all_but_the_last_two_seconds_and_leaves_the_file_whole() => KillAt("edit");

    [Fact]
    public void Killing_the_process_in_the_middle_of_a_GiB_paste_into_a_50_GiB_document_recovers_all_but_the_last_two_seconds_and_leaves_the_file_whole() => KillAt("paste");

    [Fact]
    public void Killing_the_process_when_the_save_of_a_50_GiB_document_is_half_written_recovers_all_but_the_last_two_seconds_and_leaves_the_file_whole() => KillAt("save");
}

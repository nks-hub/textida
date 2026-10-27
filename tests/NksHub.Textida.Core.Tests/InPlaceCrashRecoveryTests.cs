using System.Diagnostics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.Core.Tests;

/// <summary>A process killed around an in-place save recovers to exactly the saved bytes: nothing applied twice, and clean when the document equals the disk.</summary>
[Collection("Crash harness")]
public class InPlaceCrashRecoveryTests
{
    private static IBackendFactory? Resolve(string name) => name == "paged" ? PagedBackendFactory.Instance : null;

    [Theory]
    [InlineData("saving")]
    [InlineData("saved-unflushed")]
    [InlineData("saved-flushed")]
    public void A_killed_process_recovers_to_the_saved_bytes(string point)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Journal.CrashHarness.dll");
        Assert.True(File.Exists(dll), dll);
        using var dir = new JournalTempDir();
        byte[] original = Gen.RandomBytes(new Random(21), 4 << 20);
        string data = dir.Combine("data.bin");
        File.WriteAllBytes(data, original);
        var expected = (byte[])original.Clone();
        new byte[] { 1, 2, 3, 4, 9, 9, 9, 9, 9, 9, 9, 9 }.CopyTo(expected, 100);
        new byte[] { 0xA, 0xB, 0xC, 0xD }.CopyTo(expected, 3_000_000);

        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { dll, "inplace-child", dir.Path, point }) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var error = proc.StandardError.ReadToEndAsync();
        string? line = proc.StandardOutput.ReadLine();
        bool reached = line == point;
        proc.Kill(true);
        proc.WaitForExit();
        Assert.True(reached, $"The child said '{line}'. {error.Result}");

        var journal = Directory.GetFiles(dir.Combine("journal"), "session-*.njl").Single();
        var recovery = JournalRecovery.Recover(journal, Resolve);
        var doc = Assert.Single(recovery.Documents);
        try
        {
            Assert.NotNull(doc.Document);
            var recovered = doc.Document!;
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(recovered.ReadBytes(new(0, recovered.ByteLength)))));
            if (point == "saving")
            {
                // nothing was written: the edits come back as unsaved work on the untouched file
                Assert.Equal(RecoveryStatus.Restored, doc.Status);
                Assert.True(recovered.IsModified);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(data))));
            }
            else
            {
                Assert.Equal(RecoveryStatus.Clean, doc.Status);
                Assert.False(recovered.IsModified);
                Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(data))));
            }
        }
        finally
        {
            doc.Document?.Dispose();
        }
    }
}

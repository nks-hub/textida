using System.Diagnostics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

[Collection("Crash harness")]
public sealed class StreamSaveResilienceTests
{
    [Theory]
    [InlineData("before-flush", false)]
    [InlineData("after-flush", false)]
    [InlineData("before-swap", false)]
    [InlineData("after-swap", true)]
    public async Task Killing_the_save_around_flush_and_swap_keeps_the_file_whole_and_recovers_the_confirmed_edit(string point, bool swapped)
    {
        using var directory = new JournalTempDir();
        string path = directory.Combine("data.bin");
        var original = Gen.RandomBytes(new Random(421), 1 << 20);
        byte[] expected = [.. original[..100], .. "inserted revision"u8.ToArray(), .. original[100..]];
        File.WriteAllBytes(path, original);
        string originalHash = Hash(original), expectedHash = Hash(expected);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Journal.CrashHarness.dll"), "stream-flush-child", directory.Path, point })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var error = child.StandardError.ReadToEndAsync();
        string? reached;
        try { reached = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        Assert.True(reached == point, $"The child reached '{reached}', expected '{point}': {await error}");

        string journal = Assert.Single(Directory.GetFiles(directory.Combine("journal"), "session-*.njl"));
        var recovery = JournalRecovery.Recover(journal, name => name == "paged" ? PagedBackendFactory.Instance : null);
        var entry = Assert.Single(recovery.Documents);
        using var restored = entry.Document;
        Assert.NotNull(restored);
        Assert.Equal(expectedHash, Hash(restored.ReadBytes(new(0, restored.ByteLength))));
        Assert.Equal(swapped ? expectedHash : originalHash, Hash(File.ReadAllBytes(path)));
        Assert.Equal(!swapped, restored.IsModified);
        Assert.Equal(swapped ? RecoveryStatus.Clean : RecoveryStatus.Restored, entry.Status);
    }

    [Fact]
    public async Task A_full_disk_before_the_stream_flush_keeps_the_original_and_unsaved_revision_for_a_retry()
    {
        using var directory = new JournalTempDir();
        string path = directory.Combine("data.bin");
        var original = Gen.RandomBytes(new Random(57), 64 << 10);
        byte[] expected = [.. original, .. "unsaved work"u8.ToArray()];
        File.WriteAllBytes(path, original);
        using var document = Doc.Open(path, PagedBackendFactory.Instance);
        Assert.True(document.Insert(original.Length, "unsaved work"u8.ToArray(), new() { RawBytes = true }).Applied);
        int confirmed = 0;
        document.Saved += (_, _) => confirmed++;
        document.BeforeStreamFlush = _ => throw new IOException("The disk is full.", unchecked((int)0x80070070));

        var error = await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());

        Assert.Equal(unchecked((int)0x80070070), error.HResult);
        Assert.Equal(Hash(original), Hash(File.ReadAllBytes(path)));
        Assert.Equal(Hash(expected), Hash(document.ReadBytes(new(0, document.ByteLength))));
        Assert.True(document.IsModified);
        Assert.False(document.IsSaving);
        Assert.Equal(0, confirmed);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));

        document.BeforeStreamFlush = null;
        Assert.True((await document.SaveAsync()).Saved);
        Assert.Equal(Hash(expected), Hash(File.ReadAllBytes(path)));
        Assert.Equal(1, confirmed);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

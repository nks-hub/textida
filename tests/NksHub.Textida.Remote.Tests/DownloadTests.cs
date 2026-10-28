using System.Security.Cryptography;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// Download with cancel, resume at the byte where it stopped, repeats after a lost connection and the percent of both directions, against the
/// FTPS server in the test process and the paramiko SFTP server.
/// </summary>
public class DownloadTests
{
    public static IEnumerable<object[]> ServersData => Servers.Both;

    /// <summary>Bytes that never repeat over a short distance, so a resume at a wrong offset shows in the content.</summary>
    private static byte[] Pattern(int length) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * 31 + i / 251))];

    private static string[] Parts(SyncTests.Rig r) => Directory.GetFiles(r.Local.Path, "*.part", SearchOption.AllDirectories);

    private static string[] ServerLeftovers(SyncTests.Rig r) => Directory.GetFiles(r.Dir.Path).Select(f => Path.GetFileName(f)!).Where(f => f.Contains(".textida-")).ToArray();

    /// <summary>The size of a file the server may still be writing for a moment after the client gave up: read until it stops changing.</summary>
    private static async Task<long> SettledLengthAsync(string path)
    {
        long last = -1;
        for (int i = 0; i < 50; i++)
        {
            long now = new FileInfo(path).Length;
            if (now == last) return now;
            last = now;
            await Task.Delay(100);
        }
        return last;
    }

    private static void AssertWholeRun(IReadOnlyList<TransferProgress> progress, bool upload, string path, long total, int firstPercent = 0)
    {
        Assert.True(progress.Count >= 2, "progress reports: " + progress.Count);
        Assert.All(progress, p => Assert.Equal((upload, path, total), (p.Upload, p.Path, p.Total)));
        Assert.Equal(firstPercent, progress[0].Percent);
        Assert.Equal(100, progress[^1].Percent);
        Assert.Equal(total, progress[^1].Done);
        for (int i = 1; i < progress.Count; i++)
        {
            Assert.True(progress[i].Percent > progress[i - 1].Percent, $"percent {progress[i].Percent} after {progress[i - 1].Percent}"); // each whole percent is reported once
            Assert.True(progress[i].Done >= progress[i - 1].Done);
        }
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task DownloadReportsEachPercentOnceFromZeroToOneHundred(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        byte[] data = Pattern(300_000);
        File.WriteAllBytes(r.ServerFile("big.bin"), data);
        var progress = new List<TransferProgress>();

        var copy = await r.Store.OpenAsync(r.Client, "/big.bin", default, new TransferOptions { Progress = progress.Add });

        Assert.Equal(data, File.ReadAllBytes(copy.LocalPath));
        AssertWholeRun(progress, upload: false, "/big.bin", data.Length);
        Assert.Empty(Parts(r));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task UploadReportsEachPercentOnceFromZeroToOneHundred(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol, ("a.txt", "one"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        byte[] data = Pattern(300_000);
        File.WriteAllBytes(copy.LocalPath, data);
        var progress = new List<TransferProgress>();

        var result = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, new TransferOptions { Progress = progress.Add }, default);

        Assert.Equal(UploadStatus.Uploaded, result.Status);
        Assert.Equal(data, File.ReadAllBytes(r.ServerFile("a.txt")));
        AssertWholeRun(progress, upload: true, "/a.txt", data.Length);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task CancelledDownloadKeepsThePartAndTheNextOneContinuesAtThatByte(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        byte[] data = Pattern(2_000_000);
        File.WriteAllBytes(r.ServerFile("big.bin"), data);
        var store = r.Store;
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.OpenAsync(r.Client, "/big.bin", cancel.Token, new TransferOptions { Progress = p => { if (p.Percent >= 20) cancel.Cancel(); } }));

        long kept = new FileInfo(Assert.Single(Parts(r))).Length;
        Assert.InRange(kept, 1, data.Length - 1);
        Assert.Null(store.Find(r.Client.Connection.Id, "/big.bin")); // no copy exists until the file is whole

        var log = new List<TransferEvent>();
        var progress = new List<TransferProgress>();
        var copy = await store.OpenAsync(r.Client, "/big.bin", default, new TransferOptions { Log = log.Add, Progress = progress.Add });

        Assert.Contains(log, l => l is { Step: TransferStep.ResumingDownload } && l.Bytes == kept && l.Total == data.Length);
        Assert.DoesNotContain(log, l => l.Step == TransferStep.Downloading);
        Assert.Equal(data, File.ReadAllBytes(copy.LocalPath));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(data)), copy.Base.Sha256);
        Assert.Equal(LocalState.InSync, store.LocalStateOf(copy));
        Assert.Empty(Parts(r));
        AssertWholeRun(progress, upload: false, "/big.bin", data.Length, firstPercent: (int)(kept * 100 / data.Length));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task LostConnectionKeepsThePartAndAChangedServerFileDropsIt(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        byte[] first = Pattern(5000);
        File.WriteAllBytes(r.ServerFile("a.bin"), first);
        var store = r.Store;
        var cut = new FaultyClient(r.Client) { TruncateDownloadAt = 1000 };

        var lost = await Assert.ThrowsAsync<RemoteException>(() => store.DownloadAsync(cut, "/a.bin", default));
        Assert.Equal(RemoteError.ConnectionLost, lost.Error);
        Assert.Equal(1000, new FileInfo(Assert.Single(Parts(r))).Length);

        // The server file grows: the part belongs to another version and must not be continued.
        byte[] second = Pattern(7000);
        File.WriteAllBytes(r.ServerFile("a.bin"), second);
        var log = new List<TransferEvent>();
        var copy = await store.DownloadAsync(r.Client, "/a.bin", default, new TransferOptions { Log = log.Add });
        Assert.Contains(log, l => l.Step == TransferStep.RemovingPartialDownload);
        Assert.Contains(log, l => l is { Step: TransferStep.Downloading, Bytes: 7000 });
        Assert.DoesNotContain(log, l => l.Step == TransferStep.ResumingDownload);
        Assert.Equal(second, File.ReadAllBytes(copy.LocalPath));
        Assert.Empty(Parts(r));

        // The same size with another modification time is another version as well.
        byte[] third = [.. second.Reverse()];
        File.WriteAllBytes(r.ServerFile("a.bin"), third);
        File.SetLastWriteTimeUtc(r.ServerFile("a.bin"), copy.Base.ModifiedUtc!.Value.AddMinutes(10));
        cut.TruncateDownloadAt = 2000;
        await Assert.ThrowsAsync<RemoteException>(() => store.DownloadAsync(cut, "/a.bin", default));
        Assert.Equal(2000, new FileInfo(Assert.Single(Parts(r))).Length);
        byte[] fourth = [.. second.Select(b => (byte)(b ^ 0x55))];
        File.WriteAllBytes(r.ServerFile("a.bin"), fourth);
        File.SetLastWriteTimeUtc(r.ServerFile("a.bin"), copy.Base.ModifiedUtc!.Value.AddMinutes(20));
        log.Clear();
        var again = await store.DownloadAsync(r.Client, "/a.bin", default, new TransferOptions { Log = log.Add });
        Assert.Contains(log, l => l.Step == TransferStep.RemovingPartialDownload);
        Assert.Equal(fourth, File.ReadAllBytes(again.LocalPath));
        Assert.Empty(Parts(r));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ARetryAfterALostConnectionContinuesTheDownloadWhereItBroke(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        byte[] data = Pattern(5000);
        File.WriteAllBytes(r.ServerFile("a.bin"), data);
        var store = r.Store;
        var cut = new FaultyClient(r.Client) { TruncateDownloadAt = 1000 };
        var log = new List<TransferEvent>();
        var repeats = new List<RetryInfo>();

        var copy = await RemoteRetry.RunAsync(() => store.DownloadAsync(cut, "/a.bin", default, new TransferOptions { Log = log.Add }), 3, 1, info =>
        {
            repeats.Add(info);
            cut.TruncateDownloadAt = null; // the connection is made again and holds
            return Task.FromResult(true);
        });

        Assert.NotNull(copy);
        Assert.Equal(1, Assert.Single(repeats).Attempt);
        Assert.Contains(log, l => l is { Step: TransferStep.ResumingDownload, Bytes: 1000 });
        Assert.Equal(data, File.ReadAllBytes(copy.LocalPath));
        Assert.Empty(Parts(r));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task AnEmptyFileDownloadsAndAFolderIsRefused(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol, ("empty.txt", ""));
        Directory.CreateDirectory(r.ServerFile("sub"));
        var store = r.Store;
        var progress = new List<TransferProgress>();

        var copy = await store.OpenAsync(r.Client, "/empty.txt", default, new TransferOptions { Progress = progress.Add });

        Assert.Equal(0, new FileInfo(copy.LocalPath).Length);
        Assert.Equal(0, copy.Base.Size);
        Assert.Equal(100, Assert.Single(progress).Percent);
        Assert.Empty(Parts(r));
        Assert.Equal(RemoteError.Failed, (await Assert.ThrowsAsync<RemoteException>(() => store.DownloadAsync(r.Client, "/sub", default))).Error);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task APartIsNotContinuedWhenTheServerGivesNoTime(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        byte[] data = Pattern(100_000);
        File.WriteAllBytes(r.ServerFile("big.bin"), data);
        string local = Path.Combine(r.Local.Path, "big.bin");
        var server = new RemoteStat(data.Length, null);
        File.WriteAllBytes(RemoteTransfer.PartNameOf(local, server), new byte[40_000]); // zeros of another version of the same size: a resume would keep them
        var log = new List<TransferEvent>();

        string part = await RemoteTransfer.DownloadPartAsync(r.Client, "/big.bin", local, server, new TransferOptions { Log = log.Add }, default);

        Assert.Equal(data, File.ReadAllBytes(part));
        Assert.DoesNotContain(log, l => l.Step == TransferStep.ResumingDownload);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task CancelledUploadLeavesTheOldFileAndTheNextUploadContinues(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol, ("a.txt", "old version"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        byte[] data = Pattern(2_000_000);
        File.WriteAllBytes(copy.LocalPath, data);
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null,
            new TransferOptions { Progress = p => { if (p.Percent >= 30) cancel.Cancel(); } }, cancel.Token));

        Assert.Equal("old version", File.ReadAllText(r.ServerFile("a.txt")));
        long kept = await SettledLengthAsync(r.ServerFile(Assert.Single(ServerLeftovers(r))));
        Assert.InRange(kept, 1, data.Length - 1);

        var log = new List<TransferEvent>();
        var result = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, new TransferOptions { Log = log.Add }, default);
        Assert.Equal(UploadStatus.Uploaded, result.Status);
        Assert.Contains(log, l => l.Step == TransferStep.Resuming && l.Bytes == kept);
        Assert.Equal(data, File.ReadAllBytes(r.ServerFile("a.txt")));
        Assert.Empty(ServerLeftovers(r));
    }

    [SftpFact]
    public async Task ServerCuttingTheConnectionMidDownloadLeavesAPartThatAHealthyServerContinues()
    {
        using var dir = new TempDir();
        using var local = new TempDir();
        byte[] data = Pattern(800_000);
        File.WriteAllBytes(dir.Combine("big.bin"), data);
        var store = new WorkingCopyStore(local.Path);
        string hostKey = local.Combine("host_key");

        await using (var cutting = SftpServer.Start(dir.Path, hostKey, dropReadAfterBytes: 200_000))
        {
            await using var client = ConnectionTests.Create(cutting.Connection(trusted: cutting.Identity) with { Id = "same" }, DenyUnknownServers.Instance);
            await client.ConnectAsync(default);
            var lost = await Assert.ThrowsAsync<RemoteException>(() => store.OpenAsync(client, "/big.bin", default));
            Assert.Equal(RemoteError.ConnectionLost, lost.Error);
        }
        var parts = Directory.GetFiles(local.Path, "*.part", SearchOption.AllDirectories);
        long kept = parts.Length == 1 ? new FileInfo(parts[0]).Length : 0; // how much arrived before the cut depends on the pipelining of the library

        await using var healthy = SftpServer.Start(dir.Path, hostKey);
        await using var again = ConnectionTests.Create(healthy.Connection(trusted: healthy.Identity) with { Id = "same" }, DenyUnknownServers.Instance);
        await again.ConnectAsync(default);
        var log = new List<TransferEvent>();
        var copy = await store.OpenAsync(again, "/big.bin", default, new TransferOptions { Log = log.Add });

        Assert.Equal(data, File.ReadAllBytes(copy.LocalPath));
        if (kept > 0) Assert.Contains(log, l => l.Step == TransferStep.ResumingDownload && l.Bytes == kept);
        else Assert.Contains(log, l => l.Step == TransferStep.Downloading);
        Assert.Empty(Directory.GetFiles(local.Path, "*.part", SearchOption.AllDirectories));
    }

    // ---- RemoteRetry: the loop on its own ----

    [Fact]
    public void ThePauseBeforeEachRepeatIsDoubled()
    {
        Assert.Equal([2.0, 4.0, 8.0], new[] { 1, 2, 3 }.Select(n => RemoteRetry.DelayBefore(n, 2000).TotalSeconds));
        Assert.Equal(TimeSpan.FromMilliseconds(1), RemoteRetry.DelayBefore(1, 1));
    }

    [Fact]
    public async Task ALostConnectionIsRepeatedWithGrowingPausesUntilItWorks()
    {
        int tries = 0;
        var repeats = new List<RetryInfo>();

        string? result = await RemoteRetry.RunAsync(() =>
        {
            if (++tries < 3) throw new RemoteException(RemoteError.ConnectionLost, "cut " + tries);
            return Task.FromResult("done");
        }, retries: 3, baseDelayMilliseconds: 2000, info =>
        {
            repeats.Add(info);
            return Task.FromResult(true);
        });

        Assert.Equal("done", result);
        Assert.Equal(3, tries);
        Assert.Equal([1, 2], repeats.Select(i => i.Attempt));
        Assert.Equal([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)], repeats.Select(i => i.Delay));
        Assert.All(repeats, i => Assert.Equal(3, i.Max));
        Assert.Equal(["cut 1", "cut 2"], repeats.Select(i => i.Error.Message));
    }

    [Fact]
    public async Task AfterTheCountTheLastErrorGoesToTheCaller()
    {
        int tries = 0;
        int repeats = 0;

        var error = await Assert.ThrowsAsync<RemoteException>(() => RemoteRetry.RunAsync<string>(() => throw new RemoteException(RemoteError.ConnectionLost, "cut " + ++tries), 2, 1, _ =>
        {
            repeats++;
            return Task.FromResult(true);
        }));

        Assert.Equal(("cut 3", 3, 2), (error.Message, tries, repeats)); // the first try and two repeats
    }

    [Theory]
    [InlineData(RemoteError.Failed)]
    [InlineData(RemoteError.ChangedOnServer)]
    [InlineData(RemoteError.AuthenticationFailed)]
    [InlineData(RemoteError.NotFound)]
    public async Task OnlyALostConnectionIsRepeated(RemoteError kind)
    {
        int tries = 0;

        var error = await Assert.ThrowsAsync<RemoteException>(() => RemoteRetry.RunAsync<string>(() => throw new RemoteException(kind, "no " + ++tries), 3, 1, _ => Task.FromResult(true)));

        Assert.Equal((kind, 1), (error.Error, tries));
    }

    [Fact]
    public async Task RepeatingStopsWhenTheConnectionCannotBeMadeAgain()
    {
        int tries = 0;

        string? result = await RemoteRetry.RunAsync(() => tries++ < 5 ? throw new RemoteException(RemoteError.ConnectionLost, "cut") : Task.FromResult("done"), 3, 1, _ => Task.FromResult(false));

        Assert.Null(result);
        Assert.Equal(1, tries);
    }

    [Fact]
    public async Task ACancelDuringTheRepeatIsNotSwallowed()
    {
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoteRetry.RunAsync<string>(() => throw new RemoteException(RemoteError.ConnectionLost, "cut"), 3, 1, async _ =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, cancel.Token); // the pause of the repeat, ended by the cancel
            return true;
        }));
    }
}

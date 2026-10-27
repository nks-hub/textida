using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>A client that passes everything to a real one and breaks chosen operations, to cut a transfer at a precise point.</summary>
internal sealed class FaultyClient(IRemoteClient inner) : IRemoteClient
{
    public long? TruncateUploadAt { get; set; }
    public bool FailAllDeletes { get; set; }
    public bool NoAtomicReplace { get; set; }
    public Func<string, string, bool>? FailRename { get; set; }

    public RemoteConnection Connection => inner.Connection;
    public string? ConfirmedIdentity => inner.ConfirmedIdentity;
    public Task ConnectAsync(CancellationToken c) => inner.ConnectAsync(c);
    public Task<RemoteStat?> StatAsync(string path, CancellationToken c) => inner.StatAsync(path, c);
    public Task DownloadAsync(string path, Stream d, CancellationToken c) => inner.DownloadAsync(path, d, c);
    public Task<IReadOnlyList<RemoteEntry>> ListAsync(string dir, CancellationToken c) => inner.ListAsync(dir, c);

    public async Task UploadAsync(string path, Stream source, CancellationToken c)
    {
        if (TruncateUploadAt is not { } cut) { await inner.UploadAsync(path, source, c); return; }
        var head = new byte[cut];
        int read = await source.ReadAtLeastAsync(head, head.Length, false, c);
        await inner.UploadAsync(path, new MemoryStream(head, 0, read), c);
        throw new RemoteException(RemoteError.ConnectionLost, "cut");
    }

    public Task ResumeUploadAsync(string path, Stream source, long offset, CancellationToken c) => inner.ResumeUploadAsync(path, source, offset, c);

    public Task RenameAsync(string from, string to, CancellationToken c) =>
        FailRename?.Invoke(from, to) == true ? throw new RemoteException(RemoteError.ConnectionLost, "cut") : inner.RenameAsync(from, to, c);

    public Task<bool> TryReplaceAsync(string from, string to, CancellationToken c) =>
        NoAtomicReplace ? Task.FromResult(false) : FailRename?.Invoke(from, to) == true ? throw new RemoteException(RemoteError.ConnectionLost, "cut") : inner.TryReplaceAsync(from, to, c);

    public Task DeleteAsync(string path, CancellationToken c) =>
        FailAllDeletes ? throw new RemoteException(RemoteError.ConnectionLost, "cut") : inner.DeleteAsync(path, c);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

public class SyncTests
{
    public static IEnumerable<object[]> ServersData => Servers.Both;

    private sealed class Rig : IAsyncDisposable
    {
        public required TempDir Dir;
        public required TempDir Local;
        public required IServerFixture Server;
        public required IRemoteClient Client;
        public WorkingCopyStore Store => new(Local.Path);

        public static async Task<Rig> StartAsync(RemoteProtocol protocol, params (string Name, string Text)[] files)
        {
            var dir = new TempDir();
            var local = new TempDir();
            foreach (var (name, text) in files) File.WriteAllText(dir.Combine(name), text);
            var server = await Servers.StartAsync(protocol, dir.Path, local.Path);
            var client = ConnectionTests.Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance);
            await client.ConnectAsync(default);
            return new Rig { Dir = dir, Local = local, Server = server, Client = client };
        }

        public string ServerFile(string name) => Dir.Combine(name);

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            Dir.Dispose();
            Local.Dispose();
        }
    }

    private static string[] ServerFiles(Rig r) => Directory.GetFiles(r.Dir.Path).Select(Path.GetFileName).OrderBy(n => n).ToArray()!;

    [Theory, MemberData(nameof(ServersData))]
    public async Task OpenDownloadsACopyWithItsRemoteRevisionAndReopeningKeepsLocalWork(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "server one"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);

        Assert.Equal("server one", File.ReadAllText(copy.LocalPath));
        Assert.Equal(10, copy.Base.Size);
        Assert.Equal(await RemoteTransfer.HashOfAsync(copy.LocalPath, default), copy.Base.Sha256);
        Assert.NotNull(copy.Base.ModifiedUtc);
        Assert.Equal(LocalState.InSync, store.LocalStateOf(copy));
        Assert.Equal(copy, new WorkingCopyStore(r.Local.Path).Find(r.Client.Connection.Id, "/a.txt"));

        File.WriteAllText(copy.LocalPath, "local edit");
        File.WriteAllText(r.ServerFile("a.txt"), "someone else");
        var again = await store.OpenAsync(r.Client, "/a.txt", default);
        Assert.Equal("local edit", File.ReadAllText(again.LocalPath)); // reopening never replaces unsent work
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task WorkContinuesOfflineAndUnsentChangesSurviveARestart(RemoteProtocol protocol)
    {
        var rig = await Rig.StartAsync(protocol, ("a.txt", "v1"), ("b.txt", "other"));
        var store = rig.Store;
        var a = await store.OpenAsync(rig.Client, "/a.txt", default);
        var b = await store.OpenAsync(rig.Client, "/b.txt", default);
        string root = rig.Local.Path;

        // The connection goes away; the file can still be edited and saved locally.
        await rig.Client.DisposeAsync();
        await rig.Server.DisposeAsync();
        File.WriteAllText(a.LocalPath, "v1 edited offline");

        // A "restart": a new store over the same folder, no server, no client.
        var afterRestart = new WorkingCopyStore(root);
        var unsent = afterRestart.Unsent();
        Assert.Equal("/a.txt", Assert.Single(unsent).RemotePath);
        Assert.Equal(LocalState.Unsent, afterRestart.LocalStateOf(unsent[0]));
        Assert.Equal(LocalState.InSync, afterRestart.LocalStateOf(afterRestart.Find(b.ConnectionId, "/b.txt")!));
        Assert.Equal(2, afterRestart.All().Count);
        rig.Dir.Dispose();
        rig.Local.Dispose();
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task UploadSendsTheCopyWhenTheServerIsUnchangedAndLeavesNoTemporaryFile(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "one"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);

        Assert.Equal(UploadStatus.NothingToSend, (await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default)).Status);

        File.WriteAllText(copy.LocalPath, "one and two");
        var log = new List<TransferEvent>();
        var result = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, new TransferOptions { Log = log.Add }, default);

        Assert.Equal(UploadStatus.Uploaded, result.Status);
        Assert.Equal("one and two", File.ReadAllText(r.ServerFile("a.txt")));
        Assert.Equal(["a.txt"], ServerFiles(r));
        Assert.Equal(LocalState.InSync, store.LocalStateOf(result.Copy));
        Assert.Equal(11, result.Copy.Base.Size);
        Assert.Contains(log, l => l.Step == TransferStep.Uploading && l.Bytes == 11);
        Assert.Contains(log, l => l.Step == TransferStep.Replacing && l.Path == "/a.txt");
        // A second upload of the same content has nothing to do.
        Assert.Equal(UploadStatus.NothingToSend, (await RemoteSync.UploadAsync(r.Client, store, result.Copy, VersionCheck.Stat, null, null, default)).Status);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task AStagedProjectFileGoesToANewServerFileKeepsTheLastUploadAsBaseAndAFileItNeverSentIsAConflict(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("old.txt", "on the server"));
        using var project = new TempDir();
        var store = r.Store;
        string source = project.Combine("a.txt");
        File.WriteAllText(source, "first");

        var copy = store.Stage(r.Client.Connection.Id, "/a.txt", source);
        Assert.True(copy.Base.IsNone);
        var sent = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Uploaded, sent.Status);
        Assert.Equal("first", File.ReadAllText(r.ServerFile("a.txt")));

        File.WriteAllText(source, "second");
        var again = store.Stage(r.Client.Connection.Id, "/a.txt", source);
        Assert.Equal(sent.Copy.Base, again.Base);
        Assert.Equal(UploadStatus.Uploaded, (await RemoteSync.UploadAsync(r.Client, store, again, VersionCheck.Stat, null, null, default)).Status);
        Assert.Equal("second", File.ReadAllText(r.ServerFile("a.txt")));

        File.WriteAllText(project.Combine("old.txt"), "from the project");
        var never = store.Stage(r.Client.Connection.Id, "/old.txt", project.Combine("old.txt"));
        var conflict = await RemoteSync.UploadAsync(r.Client, store, never, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Conflict, conflict.Status);
        Assert.Equal("on the server", Encoding.UTF8.GetString(conflict.ServerContent!));
        Assert.Equal("on the server", File.ReadAllText(r.ServerFile("old.txt")));
        Assert.Equal("from the project", File.ReadAllText(project.Combine("old.txt"))); // the file of the project is only read
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ServerChangedAfterOpenGivesAConflictWithTheServerContentAndWritesNothing(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "original"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "my edit");

        File.WriteAllText(r.ServerFile("a.txt"), "colleague wrote this");
        File.SetLastWriteTimeUtc(r.ServerFile("a.txt"), copy.Base.ModifiedUtc!.Value.AddMinutes(5));

        foreach (var mode in new[] { VersionCheck.Stat, VersionCheck.Content })
        {
            var result = await RemoteSync.UploadAsync(r.Client, store, copy, mode, null, null, default);
            Assert.Equal(UploadStatus.Conflict, result.Status);
            Assert.Equal("colleague wrote this", Encoding.UTF8.GetString(result.ServerContent!));
            Assert.Equal("colleague wrote this", File.ReadAllText(r.ServerFile("a.txt")));
            Assert.Equal("my edit", File.ReadAllText(copy.LocalPath));
            Assert.Equal(LocalState.Unsent, store.LocalStateOf(copy));
        }
        Assert.Equal(1, ServerFiles(r).Count(f => f == "a.txt"));
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));

        // The user looked at the diff and accepts that server version: the upload is allowed against exactly that revision.
        var conflict = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default);
        var accepted = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, conflict.Server, null, default);
        Assert.Equal(UploadStatus.Uploaded, accepted.Status);
        Assert.Equal("my edit", File.ReadAllText(r.ServerFile("a.txt")));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ControlledChangeJustBeforeTheReplaceIsDetectedAndNothingIsReplaced(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "original"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "my edit");

        // The new file is complete under its temporary name; now the colleague writes the target.
        var options = new TransferOptions
        {
            BeforeCommit = _ =>
            {
                File.WriteAllText(r.ServerFile("a.txt"), "colleague, last second!");
                File.SetLastWriteTimeUtc(r.ServerFile("a.txt"), copy.Base.ModifiedUtc!.Value.AddMinutes(3));
                return Task.CompletedTask;
            },
        };
        var result = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, options, default);

        Assert.Equal(UploadStatus.Conflict, result.Status);
        Assert.Equal("colleague, last second!", Encoding.UTF8.GetString(result.ServerContent!));
        Assert.Equal("colleague, last second!", File.ReadAllText(r.ServerFile("a.txt")));
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ChangeWithinTheSameSecondAndSizeIsMissedByStatCheckButCaughtByContentCheck(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "AAAA"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "mine");

        File.WriteAllText(r.ServerFile("a.txt"), "BBBB");
        File.SetLastWriteTimeUtc(r.ServerFile("a.txt"), copy.Base.ModifiedUtc!.Value);

        var viaContent = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Content, null, null, default);
        Assert.Equal(UploadStatus.Conflict, viaContent.Status);

        // This is the documented limit of the cheap check: size and second are equal, so it cannot see the difference.
        var viaStat = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Uploaded, viaStat.Status);
        Assert.Contains("not detected", RemoteSync.Limit);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task TouchWithoutContentChangeIsNotAConflictAndDeletedServerFileIs(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "same"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "edited");

        File.SetLastWriteTimeUtc(r.ServerFile("a.txt"), copy.Base.ModifiedUtc!.Value.AddHours(1));
        var touched = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Uploaded, touched.Status);

        File.WriteAllText(touched.Copy.LocalPath, "edited again");
        File.Delete(r.ServerFile("a.txt"));
        var gone = await RemoteSync.UploadAsync(r.Client, store, touched.Copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Conflict, gone.Status);
        Assert.Null(gone.ServerContent);
        Assert.False(File.Exists(r.ServerFile("a.txt")));

        var recreate = await RemoteSync.UploadAsync(r.Client, store, touched.Copy, VersionCheck.Stat, RemoteRevision.None, null, default);
        Assert.Equal(UploadStatus.Uploaded, recreate.Status);
        Assert.Equal("edited again", File.ReadAllText(r.ServerFile("a.txt")));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task InterruptedUploadLeavesTheOldVersionAndARetryChecksTheVersionAgain(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "old version"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, new string('n', 5000));

        var cut = new FaultyClient(r.Client) { TruncateUploadAt = 1000, FailAllDeletes = true };
        await Assert.ThrowsAsync<RemoteException>(() => RemoteSync.UploadAsync(cut, store, copy, VersionCheck.Stat, null, null, default));
        Assert.Equal("old version", File.ReadAllText(r.ServerFile("a.txt")));
        var leftover = Assert.Single(ServerFiles(r), f => f.Contains(".textida-"));
        Assert.Equal(1000, new FileInfo(r.ServerFile(leftover)).Length); // the dead connection could not clean up

        // Someone changes the file before the retry: the retry is a new call and must not overwrite it.
        File.WriteAllText(r.ServerFile("a.txt"), "changed meanwhile!");
        File.SetLastWriteTimeUtc(r.ServerFile("a.txt"), copy.Base.ModifiedUtc!.Value.AddMinutes(9));
        var retry = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Conflict, retry.Status);
        Assert.Equal("changed meanwhile!", File.ReadAllText(r.ServerFile("a.txt")));

        // After the user accepts that version, the retry succeeds and removes the leftover of the failed attempt.
        var ok = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, retry.Server, null, default);
        Assert.Equal(UploadStatus.Uploaded, ok.Status);
        Assert.Equal(new string('n', 5000), File.ReadAllText(r.ServerFile("a.txt")));
        Assert.True(await RemoteTransfer.RecoverAsync(r.Client, "/a.txt", default) is false);
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task UploadContinuesAVerifiedPartialFileAndDropsOneThatDiffers(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "old version"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, new string('n', 5000));

        var cut = new FaultyClient(r.Client) { TruncateUploadAt = 1000 };
        await Assert.ThrowsAsync<RemoteException>(() => RemoteSync.UploadAsync(cut, store, copy, VersionCheck.Stat, null, null, default));
        Assert.Equal(1000, new FileInfo(r.ServerFile(Assert.Single(ServerFiles(r), f => f.Contains(".textida-")))).Length); // kept for the next attempt

        var log = new List<TransferEvent>();
        var result = await RemoteSync.UploadAsync(r.Client, store, copy, VersionCheck.Stat, null, new TransferOptions { Log = log.Add }, default);
        Assert.Equal(UploadStatus.Uploaded, result.Status);
        Assert.Contains(log, l => l.Step == TransferStep.Resuming && l.Bytes == 1000);
        Assert.Equal(new string('n', 5000), File.ReadAllText(r.ServerFile("a.txt")));

        // A partial file whose bytes are not the start of the local file is deleted and the upload starts over.
        File.WriteAllText(copy.LocalPath, new string('m', 3000));
        string temp = RemoteTransfer.TempNameOf("/a.txt", await RemoteTransfer.HashOfAsync(copy.LocalPath, default));
        File.WriteAllText(r.ServerFile(temp[1..]), new string('x', 500));
        var again = await RemoteSync.UploadAsync(r.Client, store, result.Copy, VersionCheck.Stat, null, new TransferOptions { Log = log.Add }, default);
        Assert.Equal(UploadStatus.Uploaded, again.Status);
        Assert.Contains(log, l => l.Step == TransferStep.RemovingLeftover);
        Assert.Equal(new string('m', 3000), File.ReadAllText(r.ServerFile("a.txt")));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task ReplaceWithoutAtomicRenameKeepsTheOldFileWhenTheLastStepFails(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "old version"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "new version");

        // The move of the new file into place fails (connection lost) after the old file was parked.
        var cut = new FaultyClient(r.Client) { NoAtomicReplace = true, FailRename = (from, to) => to == "/a.txt" && from.EndsWith(".part") };
        await Assert.ThrowsAsync<RemoteException>(() => RemoteSync.UploadAsync(cut, store, copy, VersionCheck.Stat, null, null, default));

        Assert.Equal("old version", File.ReadAllText(r.ServerFile("a.txt")));
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));

        var okResult = await RemoteSync.UploadAsync(new FaultyClient(r.Client) { NoAtomicReplace = true }, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Uploaded, okResult.Status);
        Assert.Equal("new version", File.ReadAllText(r.ServerFile("a.txt")));
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task RecoverRestoresAParkedFileAfterTheConnectionDiedBetweenTheRenames(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "precious old"));
        var store = r.Store;
        var copy = await store.OpenAsync(r.Client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, "new");

        // Everything after the first rename fails, including the restore: the target is gone and the old content is parked.
        int renames = 0;
        var dead = new FaultyClient(r.Client) { NoAtomicReplace = true, FailAllDeletes = true, FailRename = (_, _) => ++renames > 1 };
        await Assert.ThrowsAsync<RemoteException>(() => RemoteSync.UploadAsync(dead, store, copy, VersionCheck.Stat, null, null, default));
        Assert.False(File.Exists(r.ServerFile("a.txt")));

        Assert.True(await RemoteTransfer.RecoverAsync(r.Client, "/a.txt", default));
        Assert.Equal("precious old", File.ReadAllText(r.ServerFile("a.txt")));
        Assert.DoesNotContain(ServerFiles(r), f => f.Contains(".textida-"));
    }

    [SftpFact]
    public async Task ServerCuttingTheConnectionMidUploadLeavesTheOldFile()
    {
        using var dir = new TempDir();
        using var local = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "old version");
        await using var server = SftpServer.Start(dir.Path, local.Combine("host_key"), dropAfterBytes: 20000);
        await using var client = ConnectionTests.Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance);
        await client.ConnectAsync(default);
        var store = new WorkingCopyStore(local.Path);
        var copy = await store.OpenAsync(client, "/a.txt", default);
        File.WriteAllText(copy.LocalPath, new string('x', 400_000));

        await Assert.ThrowsAsync<RemoteException>(() => RemoteSync.UploadAsync(client, store, copy, VersionCheck.Stat, null, null, default));
        Assert.Equal("old version", File.ReadAllText(dir.Combine("a.txt")));

        // A new connection to a healthy server cleans the leftover and the retry goes through.
        await server.DisposeAsync();
        await using var healthy = SftpServer.Start(dir.Path, local.Combine("host_key"));
        await using var again = ConnectionTests.Create(healthy.Connection(trusted: healthy.Identity), DenyUnknownServers.Instance);
        await again.ConnectAsync(default);
        await RemoteTransfer.RecoverAsync(again, "/a.txt", default);
        Assert.DoesNotContain(Directory.GetFiles(dir.Path), f => f.Contains(".textida-"));
        var done = await RemoteSync.UploadAsync(again, store, copy, VersionCheck.Stat, null, null, default);
        Assert.Equal(UploadStatus.Uploaded, done.Status);
        Assert.Equal(400_000, new FileInfo(dir.Combine("a.txt")).Length);
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task AfterARestartTheCacheIsPairedWithTheServerByTheStoredRevision(RemoteProtocol protocol)
    {
        await using var r = await Rig.StartAsync(protocol, ("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"), ("d.txt", "d"));
        var store = r.Store;
        foreach (var n in new[] { "a", "b", "c", "d" }) await store.OpenAsync(r.Client, $"/{n}.txt", default);
        File.WriteAllText(store.Find(r.Client.Connection.Id, "/b.txt")!.LocalPath, "b local");
        File.WriteAllText(r.ServerFile("c.txt"), "c on server");
        File.SetLastWriteTimeUtc(r.ServerFile("c.txt"), DateTime.UtcNow.AddHours(2));
        File.Delete(r.ServerFile("d.txt"));

        var paired = (await new WorkingCopyStore(r.Local.Path).PairAsync(r.Client, default)).ToDictionary(s => s.Copy.RemotePath);
        Assert.Equal((LocalState.InSync, ServerState.InSync), (paired["/a.txt"].Local, paired["/a.txt"].Server!.Value));
        Assert.Equal((LocalState.Unsent, ServerState.InSync), (paired["/b.txt"].Local, paired["/b.txt"].Server!.Value));
        Assert.Equal((LocalState.InSync, ServerState.Changed), (paired["/c.txt"].Local, paired["/c.txt"].Server!.Value));
        Assert.Equal(ServerState.Missing, paired["/d.txt"].Server);
        Assert.False(paired["/b.txt"].IsConflict);

        File.WriteAllText(store.Find(r.Client.Connection.Id, "/c.txt")!.LocalPath, "c local");
        Assert.True((await store.CheckAsync(r.Client, store.Find(r.Client.Connection.Id, "/c.txt")!, default)).IsConflict);
    }

    [Fact]
    public async Task PruneRemovesOldCleanCopiesAndNeverUnsentOnes()
    {
        await using var r = await Rig.StartAsync(RemoteProtocol.Ftps, ("a.txt", "aaaa"), ("b.txt", "bbbb"), ("c.txt", "cccc"));
        var store = r.Store;
        foreach (var n in new[] { "a", "b", "c" }) await store.OpenAsync(r.Client, $"/{n}.txt", default);
        File.WriteAllText(store.Find(r.Client.Connection.Id, "/a.txt")!.LocalPath, "unsent!");

        Assert.Empty(store.Prune(new CachePolicy(), DateTime.UtcNow.AddYears(1))); // no limits set, nothing is removed
        var removed = store.Prune(new CachePolicy(MaxAge: TimeSpan.FromDays(1)), DateTime.UtcNow.AddDays(2));
        Assert.Equal(["/b.txt", "/c.txt"], removed.Select(c => c.RemotePath).Order().ToArray());
        Assert.Equal("/a.txt", Assert.Single(store.All()).RemotePath);
        Assert.Empty(store.Prune(new CachePolicy(MaxBytes: 0), DateTime.UtcNow));
    }
}

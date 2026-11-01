using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.Remote.Tests;

/// <summary>PZ.7.3: a folder on a server as a side of a comparison, and the folders the client makes for a copy.</summary>
public class RemoteFolderTests
{
    public static IEnumerable<object[]> ServersData => Servers.Both;

    [Theory, MemberData(nameof(ServersData))]
    public async Task A_copy_makes_the_missing_folders_on_the_server_and_a_second_copy_replaces_the_file(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol);
        using var local = new TempDir();
        File.WriteAllText(local.Combine("a.txt"), "first");
        var right = new RemoteFolderSource(r.Client, "/", "server");

        await right.WriteFromFileAsync("deep/er/a.txt", local.Combine("a.txt"), null, default);

        Assert.Equal("first", File.ReadAllText(Path.Combine(r.Dir.Path, "deep", "er", "a.txt")));
        File.WriteAllText(local.Combine("a.txt"), "second");
        await right.WriteFromFileAsync("deep/er/a.txt", local.Combine("a.txt"), null, default);
        Assert.Equal("second", File.ReadAllText(Path.Combine(r.Dir.Path, "deep", "er", "a.txt")));

        var files = await right.ListAsync(default);
        Assert.Equal(["deep/er/a.txt"], files.Select(f => f.Path));
        await using var read = await right.OpenReadAsync("deep/er/a.txt", default);
        Assert.Equal("second", await new StreamReader(read).ReadToEndAsync());
    }

    [Theory, MemberData(nameof(ServersData))]
    public async Task A_local_folder_is_compared_with_a_folder_on_the_server_and_copied_into_it(RemoteProtocol protocol)
    {
        await using var r = await SyncTests.Rig.StartAsync(protocol, ("same.txt", "x"));
        using var local = new TempDir();
        File.WriteAllText(local.Combine("same.txt"), "x");
        Directory.CreateDirectory(local.Combine("sub"));
        File.WriteAllText(local.Combine("sub/new.txt"), "new");
        var left = new LocalFolderSource(local.Path);
        var right = new RemoteFolderSource(r.Client, "/", "server");

        var differences = await FolderCompare.CompareAsync(left, right, FolderCompareBy.Content, default);
        Assert.Equal([("same.txt", FolderState.Same), ("sub/new.txt", FolderState.OnlyLeft)], differences.Select(d => (d.Path, d.State)));

        var result = await FolderCompare.CopyAsync(left, right, FolderCompare.PlanCopy(differences, CopyDirection.LeftToRight), _ => null, null, default);

        Assert.Equal(1, result.Copied);
        Assert.Empty(result.Failed);
        Assert.Equal("new", File.ReadAllText(Path.Combine(r.Dir.Path, "sub", "new.txt")));
    }
}

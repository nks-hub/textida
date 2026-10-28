using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// An "AStagedProjectFile..." run on a loaded machine failed once with "503 Parallel commands aren't allowed." on the RNTO that follows RNFR. The client did nothing wrong:
/// it sends RNTO after it has read the 350. The server in the tests answers 503 to a command it declares abortable (RNFR and RNTO are) while it still holds the
/// background task of the previous one, and that task ends a moment after the reply was queued. Here the server is slow to end it on purpose.
/// </summary>
public class FtpServerRaceTests
{
    [Fact]
    public async Task A_rename_succeeds_against_a_server_that_ends_RNFR_long_after_its_reply()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Combine("a.txt"), "one");
        await using var server = await FtpsServer.StartAsync(dir.Path, commandLag: TimeSpan.FromMilliseconds(300));
        await using var client = ConnectionTests.Create(server.Connection(trusted: server.Identity), DenyUnknownServers.Instance);
        await client.ConnectAsync(default);

        await client.RenameAsync("/a.txt", "/b.txt", default);

        Assert.False(File.Exists(dir.Combine("a.txt")));
        Assert.Equal("one", File.ReadAllText(dir.Combine("b.txt")));
    }
}

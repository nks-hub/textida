namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// A run of these tests once stopped in the middle (54 of 82 tests, "Test host process crashed", after an <see cref="ObjectDisposedException"/> thrown on a pool thread inside the FTP test server) and an
/// SFTP test server left behind by a host that died kept `dotnet test` waiting for ever. These tests are the guards: the one exception that is known to come from the server library does not end the host, and the
/// SFTP server ends when its host does.
/// </summary>
public class HostSurvivalTests
{
    [Fact]
    public async Task The_known_race_of_the_FTP_server_library_on_a_pool_thread_does_not_end_the_test_host()
    {
        _ = FtpsServer.FreePort(); // the first use of FtpsServer installs the handler
        var before = FtpsServer.HeldServerRaces;
        var real = FtpsServer.IsKnownServerRace;
        // the shape of the failure without the library: a semaphore released after its owner returned; only the stack of the real one names the library
        FtpsServer.IsKnownServerRace = e => e is ObjectDisposedException { ObjectName: "System.Threading.SemaphoreSlim" };
        try
        {
            var semaphore = new SemaphoreSlim(0, 1);
            semaphore.Dispose();
            ThreadPool.UnsafeQueueUserWorkItem(_ => semaphore.Release(), null);
            for (int i = 0; i < 100 && FtpsServer.HeldServerRaces == before; i++) await Task.Delay(50);
        }
        finally { FtpsServer.IsKnownServerRace = real; }
        Assert.Equal(before + 1, FtpsServer.HeldServerRaces);
    }

    [Fact]
    public void The_filter_takes_the_semaphore_of_the_server_library_and_nothing_else()
    {
        Exception Thrown(Action action)
        {
            try { action(); }
            catch (Exception e) { return e; }
            throw new InvalidOperationException("nothing was thrown");
        }
        var disposed = new SemaphoreSlim(0, 1);
        disposed.Dispose();
        var fromElsewhere = Thrown(() => disposed.Release());
        Assert.False(FtpsServer.IsKnownServerRace(fromElsewhere)); // right type, but not from the library
        Assert.False(FtpsServer.IsKnownServerRace(new InvalidOperationException()));
        Assert.False(FtpsServer.IsKnownServerRace(new ObjectDisposedException("System.Net.Sockets.Socket")));
    }

    [SftpFact]
    public void The_SFTP_server_ends_when_its_host_closes_its_stdin_as_a_dying_host_does()
    {
        using var dir = new TempDir();
        var server = SftpServer.Start(dir.Path, dir.Combine("host_key"));
        var process = server.ServerProcess;
        try
        {
            Assert.False(process.HasExited);
            process.StandardInput.Close();
            Assert.True(process.WaitForExit(20_000), "the SFTP test server went on after its host was gone");
        }
        finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}

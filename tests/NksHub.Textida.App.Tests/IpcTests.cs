using NksHub.Textida.App.Startup;

namespace NksHub.Textida.App.Tests;

public class IpcTests
{
    private static string Channel() => "test-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Request_round_trips_through_the_real_transport()
    {
        var channel = Channel();
        IpcRequest? seen = null;
        await using var server = new IpcServer(channel, r => { seen = r; return Task.CompletedTask; });

        var sent = new IpcRequest { NewWindow = true, Files = [new IpcFile { Path = "C:/x/ěščř.txt", Line = 12 }, new IpcFile { Path = "other" }] };
        var reply = await IpcClient.SendAsync(channel, sent, TimeSpan.FromSeconds(5));

        Assert.True(reply.Ok);
        Assert.NotNull(seen);
        Assert.True(seen.NewWindow);
        Assert.False(seen.Wait);
        Assert.Equal(2, seen.Files.Count);
        Assert.Equal("C:/x/ěščř.txt", seen.Files[0].Path);
        Assert.Equal(12, seen.Files[0].Line);
        Assert.Null(seen.Files[1].Line);
    }

    /// <summary>A context whose work never runs: the thread that owns it is blocked in a wait, as the UI thread is while the editor ends its server.</summary>
    private sealed class BlockedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    [Fact]
    public void Ending_the_server_does_not_need_the_thread_that_waits_for_it()
    {
        var server = new IpcServer(Channel(), _ => Task.CompletedTask);
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new BlockedContext());
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            // the handler of the exit of the editor waits for three seconds at most: a normal end must not use them up
            Assert.True(server.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1.5)), $"the end did not come in {clock.Elapsed.TotalSeconds:F1} s");
        }
        finally { SynchronizationContext.SetSynchronizationContext(before); }
    }

    [Fact]
    public async Task Reply_waits_for_the_handler()
    {
        var channel = Channel();
        var release = new TaskCompletionSource();
        await using var server = new IpcServer(channel, _ => release.Task);

        var pending = IpcClient.SendAsync(channel, new IpcRequest { Wait = true, Files = [new IpcFile { Path = "a" }] }, TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Assert.False(pending.IsCompleted);

        release.SetResult();
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(5))).Ok);
    }

    [Fact]
    public async Task Handler_failure_reaches_the_client()
    {
        var channel = Channel();
        await using var server = new IpcServer(channel, _ => Task.FromException(new IOException("cannot open it")));

        var reply = await IpcClient.SendAsync(channel, new IpcRequest(), TimeSpan.FromSeconds(5));

        Assert.False(reply.Ok);
        Assert.Equal("cannot open it", reply.Error);
    }

    [Fact]
    public async Task Several_clients_are_served_one_after_another()
    {
        var channel = Channel();
        int count = 0;
        await using var server = new IpcServer(channel, _ => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        for (int i = 0; i < 5; i++) Assert.True((await IpcClient.SendAsync(channel, new IpcRequest(), TimeSpan.FromSeconds(5))).Ok);
        Assert.Equal(5, count);
    }

    [Fact]
    public async Task Concurrent_clients_do_not_block_each_other()
    {
        var channel = Channel();
        var gate = new TaskCompletionSource();
        await using var server = new IpcServer(channel, r => r.Wait ? gate.Task : Task.CompletedTask);

        var waiting = IpcClient.SendAsync(channel, new IpcRequest { Wait = true }, TimeSpan.FromSeconds(5));
        await Task.Delay(200);
        Assert.True((await IpcClient.SendAsync(channel, new IpcRequest(), TimeSpan.FromSeconds(5))).Ok);
        Assert.False(waiting.IsCompleted);
        gate.SetResult();
        Assert.True((await waiting).Ok);
    }

    [Fact]
    public async Task Client_gives_up_when_nobody_listens()
    {
        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<IOException>(() => IpcClient.SendAsync(Channel(), new IpcRequest(), TimeSpan.FromMilliseconds(600)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Only_one_process_can_own_a_channel()
    {
        var channel = Channel();
        using var first = IpcEndpoint.TryAcquire(channel);
        Assert.NotNull(first);
        Assert.Null(IpcEndpoint.TryAcquire(channel));
        first.Dispose();
        using var again = IpcEndpoint.TryAcquire(channel);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Disposed_server_stops_accepting()
    {
        var channel = Channel();
        var server = new IpcServer(channel, _ => Task.CompletedTask);
        Assert.True((await IpcClient.SendAsync(channel, new IpcRequest(), TimeSpan.FromSeconds(5))).Ok);
        await server.DisposeAsync();
        await Assert.ThrowsAsync<IOException>(() => IpcClient.SendAsync(channel, new IpcRequest(), TimeSpan.FromMilliseconds(600)));
    }
}

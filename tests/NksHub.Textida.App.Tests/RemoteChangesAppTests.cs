using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.7.1: the unsent changes of several files are ticked, shown against the server and sent in one go, against a real FTPS server in the test process and a real SSH server (paramiko) for SFTP.</summary>
[Collection("LocState")]
public class RemoteChangesAppTests : IDisposable
{
    public static IEnumerable<object[]> Protocols => Servers.Both;

    private readonly TempDir _temp = new();
    private readonly TempDir _server = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly MemoryCredentials _secrets = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private IServerFixture? _fixture;

    public RemoteChangesAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        if (_fixture is not null) Task.Run(() => _fixture.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(10));
        TestApp.Cleanup(_app);
        _temp.Dispose();
        _server.Dispose();
    }

    private sealed class TrustAll : IServerTrustPrompt
    {
        public ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation) => ValueTask.FromResult(true);
    }

    private async Task Until(Func<bool> condition, string what, int timeoutMs = 30_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition(); await Task.Delay(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Waited for " + what + ". Rows: " + string.Join(" | ", _w.QuickResults.Select(r => r.Title + " / " + r.Detail)) + " Status: " + _w.StatusMessageText + " Remote: " + string.Join(" | ", _app.Output.Read(OutputChannel.Remote).Select(l => l.Text)));
        }
    }

    private async Task Row(string startsWith)
    {
        await Until(() => _w.QuickResults.Any(r => r.Title.StartsWith(startsWith, StringComparison.Ordinal)), "the row " + startsWith);
        _w.QuickResults.First(r => r.Title.StartsWith(startsWith, StringComparison.Ordinal)).Run();
    }

    /// <summary>Clicks a row and waits until the list that follows (the loop shows a new one) has the row in its new state.</summary>
    private async Task Toggle(string name, bool tick)
    {
        await Row((tick ? "☐ " : "☑ ") + name);
        await Until(() => _w.QuickResults.Any(r => r.Title == (tick ? "☑ " : "☐ ") + name), "the list after " + name);
    }

    [AvaloniaTheory, MemberData(nameof(Protocols))]
    public async Task Ticked_files_are_sent_together_and_a_file_changed_on_the_server_needs_its_differences_first(RemoteProtocol protocol)
    {
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Combine(_server.Path, name), name + " original\n");
        _fixture = await Servers.StartAsync(protocol, _server.Path, _temp.Path);
        var connection = _fixture.Connection(_fixture.Identity) with { Name = "Test server" };
        _app.Remote.Connections.Add(connection, _fixture.Password, _secrets);
        await using (var client = _app.Remote.CreateClient(connection, new TrustAll()))
        {
            await client.ConnectAsync(default);
            foreach (var name in new[] { "/a.txt", "/b.txt", "/c.txt" })
            {
                var copy = await _app.Remote.Copies.OpenAsync(client, name, default);
                File.WriteAllText(copy.LocalPath, name + " edited here\n");
            }
        }
        File.WriteAllText(Path.Combine(_server.Path, "b.txt"), "b.txt changed by someone else\n");

        _w.SendRemoteChanges();
        await Toggle("a.txt", tick: true);
        await Row("☐ b.txt"); // refused: changed on the server
        await Until(() => _w.QuickResults.Count > 0 && _w.QuickResults[0].Title == Loc.Get(K.RemoteChangesNeedDiff, ("name", "b.txt")), "the refusal");
        Assert.Contains("☐ b.txt", _w.QuickResults.Select(r => r.Title));
        Assert.Contains("☑ a.txt", _w.QuickResults.Select(r => r.Title));

        int tabs = _w.Tabs.Count();
        await Row(Loc.Get(K.RemoteChangesDiff));
        await Row("b.txt"); // show its differences
        await Until(() => _w.Tabs.Count() > tabs, "the comparison");
        await Toggle("b.txt", tick: true); // now it can be ticked
        await Toggle("c.txt", tick: true);
        await Toggle("c.txt", tick: false); // and unticked again: c stays unsent
        await Row(Loc.Get(K.RemoteChangesSend, ("selected", 2)));
        await _w.SendRemoteChangesTask;

        Assert.Equal("/a.txt edited here\n", File.ReadAllText(Path.Combine(_server.Path, "a.txt")));
        Assert.Equal("/b.txt edited here\n", File.ReadAllText(Path.Combine(_server.Path, "b.txt")));
        Assert.Equal("c.txt original\n", File.ReadAllText(Path.Combine(_server.Path, "c.txt")));
        Assert.Single(_app.Remote.Copies.Unsent());
        Assert.Contains("2", _w.StatusMessageText);
    }

    [AvaloniaTheory, MemberData(nameof(Protocols))]
    public async Task A_newer_server_version_after_the_differences_were_shown_is_not_overwritten(RemoteProtocol protocol)
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "original\n");
        _fixture = await Servers.StartAsync(protocol, _server.Path, _temp.Path);
        var connection = _fixture.Connection(_fixture.Identity) with { Name = "Test server" };
        _app.Remote.Connections.Add(connection, _fixture.Password, _secrets);
        await using (var client = _app.Remote.CreateClient(connection, new TrustAll()))
        {
            await client.ConnectAsync(default);
            var copy = await _app.Remote.Copies.OpenAsync(client, "/a.txt", default);
            File.WriteAllText(copy.LocalPath, "edited here\n");
        }
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "first change by someone else\n");

        int opened = _w.Tabs.Count();
        _w.SendRemoteChanges();
        await Row(Loc.Get(K.RemoteChangesDiff));
        await Row("a.txt");
        await Until(() => _w.Tabs.Count() > opened, "the comparison");
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "second change, after the differences were shown\n");
        await Toggle("a.txt", tick: true);
        await Row(Loc.Get(K.RemoteChangesSend, ("selected", 1)));
        await _w.SendRemoteChangesTask;

        Assert.Equal("second change, after the differences were shown\n", File.ReadAllText(Path.Combine(_server.Path, "a.txt")));
        Assert.Single(_app.Remote.Copies.Unsent());
    }
}

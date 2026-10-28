using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Files;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// Remote files in the window (P5.4) against a real FTPS server in the test process: the dialogs are scripted, the protocol, the files and the
/// credential store of the profile are real.
/// </summary>
[Collection("LocState")]
public class RemoteFileTests : IDisposable
{
    private const string Password = "S3cret-Pass-9981";

    private readonly TempDir _temp = new();
    private readonly TempDir _server = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly MemoryCredentials _secrets = new();
    private App _app;
    private MainWindow _w;
    private FtpsServer? _ftps;

    public RemoteFileTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
        Lines = () => _app.Output.Read(OutputChannel.Remote).Select(l => l.Text).Append("status: " + _w.StatusMessageText);
    }

    public void Dispose()
    {
        if (_ftps is not null) Task.Run(() => _ftps.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(10)); // a server that still has a client does not stop at once
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
        _server.Dispose();
    }

    private static Func<IEnumerable<string>> Lines = () => [];

    /// <summary>The text of a file the server is changing right now; null while FTP has the old file parked and the new one not yet in place.</summary>
    private static string? Read(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
    }

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 20_000)
    {
        for (var sw = Stopwatch.StartNew(); !condition(); await Task.Delay(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Waited for " + what + ". " + string.Join(" | ", Lines()));
        }
    }

    private async Task<RemoteConnection> StartServerAndSaveConnectionAsync(bool trusted)
    {
        _ftps = await FtpsServer.StartAsync(_server.Path, password: Password);
        var connection = _ftps.Connection(trusted ? _ftps.Identity : null) with { Name = "Test server" };
        _app.Remote.Connections.Add(connection, Password, _secrets);
        return connection;
    }

    private async Task<DocTab> OpenRemoteAsync(string remotePath)
    {
        _dialogs.Prompts.Enqueue(remotePath);
        _w.OpenRemote();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults[0].Run();
        await Until(() => _w.Tabs.Any(t => t.Doc.RemoteCopy is not null), "the remote file to open");
        var tab = _w.Tabs.First(t => t.Doc.RemoteCopy is not null);
        // the bar of the local state is there at once; the one that compared the copy with the server replaces it
        await Until(() => tab.Bar is InfoBar bar && bar.Message != Loc.Get(K.RemoteBarOffline), "the bar about the server");
        return tab;
    }

    private static string? BarOf(DocTab tab) => (tab.Bar as InfoBar)?.Message;

    [AvaloniaFact]
    public async Task A_new_connection_keeps_its_password_in_the_credential_store_and_in_no_file()
    {
        _ftps = await FtpsServer.StartAsync(_server.Path, password: Password);
        _dialogs.Prompts.Enqueue("Office server");
        _dialogs.Answers.Enqueue(1); // FTPS, explicit
        _dialogs.Prompts.Enqueue("127.0.0.1");
        _dialogs.Prompts.Enqueue(_ftps.Port.ToString());
        _dialogs.Prompts.Enqueue("u");
        _dialogs.Secrets.Enqueue(Password);

        _w.ShowConnections();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults.Single(r => r.Title == Loc.Get(K.RemoteNewConnection)).Run();
        await Until(() => _app.Remote.Connections.Load().Count == 1, "the connection to be saved");

        var saved = _app.Remote.Connections.Load().Single();
        Assert.Equal(("Office server", RemoteProtocol.Ftps, "u"), (saved.Name, saved.Protocol, saved.UserName));
        Assert.Equal(Password, _secrets.Get(saved.SecretKey));
        Assert.Contains(Loc.Get(K.RemoteNewPassword), _dialogs.SecretsAsked);
        foreach (var file in Directory.EnumerateFiles(_app.ProfileDirectory, "*", SearchOption.AllDirectories))
        {
            // the journal and logs may hold other things; none of them may hold the password
            byte[] bytes;
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            catch (IOException) { continue; } // the journal holds its lock file exclusively; it keeps no text
            Assert.False(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(Password)) >= 0, file);
            Assert.False(bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(Password)) >= 0, file);
        }
    }

    [AvaloniaFact]
    public async Task Plain_ftp_needs_an_explicit_answer_to_the_warning()
    {
        _dialogs.Prompts.Enqueue("Old server");
        _dialogs.Answers.Enqueue(3); // FTP, unencrypted
        _dialogs.Answers.Enqueue(1); // ... but the warning is declined

        _w.ShowConnections();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults.Single(r => r.Title == Loc.Get(K.RemoteNewConnection)).Run();
        await Until(() => _dialogs.Asked.Count == 2, "the warning");

        Assert.Equal(Loc.Get(K.RemotePlainTitle), _dialogs.Asked[1].Title);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(_app.Remote.Connections.Load());
    }

    [AvaloniaFact]
    public async Task Opening_a_file_makes_a_local_copy_and_the_bar_says_it_is_the_same_as_the_server()
    {
        Directory.CreateDirectory(Path.Combine(_server.Path, "docs"));
        File.WriteAllText(Path.Combine(_server.Path, "docs", "a.txt"), "server one");
        await StartServerAndSaveConnectionAsync(trusted: true);

        var tab = await OpenRemoteAsync("/docs/a.txt");

        Assert.Equal("server one", tab.Editor!.Document.Text);
        Assert.StartsWith(_app.Remote.Copies.Root, tab.Path!);
        Assert.Equal(Loc.Get(K.RemoteBarInSync), BarOf(tab));
        Assert.Contains(_app.Output.Read(OutputChannel.Remote), l => l.Text.Contains("127.0.0.1"));
    }

    [AvaloniaFact]
    public async Task A_server_identity_nobody_trusted_yet_is_shown_and_remembered_after_the_answer()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "x");
        var connection = await StartServerAndSaveConnectionAsync(trusted: false);
        _dialogs.Answers.Enqueue(0); // Trust and Connect

        var tab = await OpenRemoteAsync("/a.txt");

        var asked = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.RemoteTrustTitle), asked.Title);
        Assert.Contains(asked.Details!, d => d.Contains(_ftps!.Identity, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(_ftps!.Identity, _app.Remote.Connections.Load().Single(c => c.Id == connection.Id).TrustedIdentity);

        // the next connection does not ask again
        await _w.CloseTabAsync(tab);
        await OpenRemoteAsync("/a.txt");
        Assert.Single(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Declining_the_server_identity_opens_nothing()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "x");
        await StartServerAndSaveConnectionAsync(trusted: false);
        _dialogs.Answers.Enqueue(1); // Cancel
        _dialogs.Prompts.Enqueue("/a.txt");

        _w.OpenRemote();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults[0].Run();
        await Until(() => _w.StatusMessageText.Contains("Test server"), "the failure message");

        Assert.DoesNotContain(_w.Tabs, t => t.Doc.RemoteCopy is not null);
        Assert.Null(_app.Remote.Connections.Load().Single().TrustedIdentity);
    }

    [AvaloniaFact]
    public async Task Upload_sends_the_saved_copy_and_the_bar_is_in_step_again()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "server one");
        await StartServerAndSaveConnectionAsync(trusted: true);
        var tab = await OpenRemoteAsync("/a.txt");

        tab.Editor!.Document.Insert(0, "edited ");
        _w.UploadRemote();
        await Until(() => Read(Path.Combine(_server.Path, "a.txt")) == "edited server one", "the upload");
        await Until(() => BarOf(tab) == Loc.Get(K.RemoteBarInSync) && _w.StatusMessageText.Contains("Test server"), "the bar after the upload");

        Assert.Equal(["a.txt"], Directory.GetFiles(_server.Path).Select(Path.GetFileName));
        Assert.Contains(_app.Output.Read(OutputChannel.Remote), l => l.Text.Contains("replacing", StringComparison.OrdinalIgnoreCase) || l.Text.Contains("nahrazuji"));
        Assert.Equal(LocalState.InSync, _app.Remote.Copies.LocalStateOf(tab.Doc.RemoteCopy!));
    }

    [AvaloniaFact]
    public async Task A_change_on_the_server_gives_the_differences_not_an_overwrite_and_the_user_can_then_upload_against_that_version()
    {
        string serverFile = Path.Combine(_server.Path, "a.txt");
        File.WriteAllText(serverFile, "line one\nline two\n");
        await StartServerAndSaveConnectionAsync(trusted: true);
        var tab = await OpenRemoteAsync("/a.txt");
        tab.Editor!.Document.Insert(0, "mine\n");

        File.WriteAllText(serverFile, "line one\nline two changed by a colleague\n");
        File.SetLastWriteTimeUtc(serverFile, DateTime.UtcNow.AddMinutes(10));
        _dialogs.Answers.Enqueue(0); // Show Differences
        _w.UploadRemote();
        await Until(() => _w.ActiveCompare is not null, "the compare tab");

        Assert.Equal(Loc.Get(K.RemoteConflictTitle), _dialogs.Asked.Single().Title);
        Assert.Equal("line one\nline two changed by a colleague\n", File.ReadAllText(serverFile)); // nothing was overwritten
        Assert.Equal(Loc.Get(K.RemoteConflictShown), _w.StatusMessageText);

        // after the user has seen that version, the upload is allowed against exactly it
        await _w.SaveAsync(tab);
        _w.SelectTab(tab);
        _w.UploadRemote();
        await Until(() => Read(serverFile)?.StartsWith("mine") == true, "the upload against the version that was shown");
        Assert.Single(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Upload_anyway_replaces_the_version_that_was_found_and_a_deleted_file_offers_no_differences()
    {
        string serverFile = Path.Combine(_server.Path, "a.txt");
        File.WriteAllText(serverFile, "original");
        await StartServerAndSaveConnectionAsync(trusted: true);
        var tab = await OpenRemoteAsync("/a.txt");
        tab.Editor!.Document.Insert(0, "mine ");

        File.Delete(serverFile);
        _dialogs.Answers.Enqueue(0); // with no server version to show the first button is Upload Anyway
        _w.UploadRemote();
        await Until(() => Read(serverFile) == "mine original", "the file to be created again");

        Assert.Equal("mine original", File.ReadAllText(serverFile));
        var request = _dialogs.Asked.Single();
        Assert.Equal(Loc.Get(K.RemoteConflictDeleted, ("title", tab.Title)), request.Message);
        Assert.DoesNotContain(request.Buttons, b => b.Label == Loc.Get(K.RemoteConflictDifferences));
    }

    [AvaloniaFact]
    public async Task Unsent_changes_survive_closing_the_tab_and_are_listed_without_a_connection()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "server one");
        await StartServerAndSaveConnectionAsync(trusted: true);
        var tab = await OpenRemoteAsync("/a.txt");
        tab.Editor!.Document.Insert(0, "offline edit ");
        await _w.SaveAsync(tab);
        await _w.CloseTabAsync(tab);
        await _ftps!.DisposeAsync(); // the server is gone
        _ftps = null;

        _w.ShowUnsent();
        await Until(() => _w.QuickResults.Count == 1, "the list of unsent changes");
        Assert.Equal("a.txt", _w.QuickResults[0].Title);
        _w.QuickResults[0].Run();
        await Until(() => _w.Tabs.Any(t => t.Doc.RemoteCopy is not null), "the copy to open");

        var reopened = _w.Tabs.First(t => t.Doc.RemoteCopy is not null);
        Assert.Equal("offline edit server one", reopened.Editor!.Document.Text);
        Assert.Equal(Loc.Get(K.RemoteBarUnsent), BarOf(reopened));
    }

    [AvaloniaFact]
    public async Task After_a_restart_the_cache_is_paired_with_the_server_by_the_stored_revision_and_nothing_is_downloaded()
    {
        foreach (var name in new[] { "a", "b", "c" }) File.WriteAllText(Path.Combine(_server.Path, name + ".txt"), "server " + name);
        var connection = await StartServerAndSaveConnectionAsync(trusted: true);
        foreach (var name in new[] { "a", "b", "c" }) await _w.CloseTabAsync(await OpenRemoteAsync($"/{name}.txt"));
        File.WriteAllText(_app.Remote.Copies.Find(connection.Id, "/a.txt")!.LocalPath, "local a");
        string serverB = Path.Combine(_server.Path, "b.txt");
        File.WriteAllText(serverB, "server b changed by a colleague");
        File.SetLastWriteTimeUtc(serverB, DateTime.UtcNow.AddMinutes(10));
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets); // the next start knows the copies only from the disk

        _w.PairCache();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults[0].Run();
        await Until(() => _w.QuickResults.Count == 3 && _w.QuickResults[0].Title == "a.txt", "the copies paired with the server");

        var rows = _w.QuickResults.ToDictionary(r => r.Title, r => r.Detail!);
        Assert.StartsWith(Loc.Get(K.RemoteBarUnsent), rows["a.txt"]);
        Assert.StartsWith(Loc.Get(K.RemoteBarServerChanged), rows["b.txt"]);
        Assert.StartsWith(Loc.Get(K.RemoteBarInSync), rows["c.txt"]);
        Assert.Equal("server b", File.ReadAllText(_app.Remote.Copies.Find(connection.Id, "/b.txt")!.LocalPath));
        Assert.Equal("local a", File.ReadAllText(_app.Remote.Copies.Find(connection.Id, "/a.txt")!.LocalPath));

        _w.QuickResults.Single(r => r.Title == "b.txt").Run();
        await Until(() => _w.Tabs.Any(t => t.Doc.RemoteCopy?.RemotePath == "/b.txt" && BarOf(t) == Loc.Get(K.RemoteBarServerChanged)), "the bar of the opened copy");
    }

    [AvaloniaFact]
    public async Task A_configuration_restored_at_the_next_start_keeps_the_connection_working_without_a_password_in_the_backup_and_the_cache_pairs_with_the_server()
    {
        File.WriteAllText(Path.Combine(_server.Path, "a.txt"), "server a");
        var connection = await StartServerAndSaveConnectionAsync(trusted: true);
        await _w.CloseTabAsync(await OpenRemoteAsync("/a.txt"));
        string localCopy = _app.Remote.Copies.Find(connection.Id, "/a.txt")!.LocalPath;

        string zip = Path.Combine(_temp.Path, "config.zip");
        _dialogs.SaveFiles.Enqueue(zip);
        _w.BackupConfiguration();
        await Until(() => File.Exists(zip) && _w.StatusMessageText.Contains(zip), "the backup");
        var entries = ConfigBackupTests.Entries(zip);
        Assert.All(entries.Values, text => Assert.DoesNotContain(Password, text));
        Assert.Equal(connection.Id, Assert.Single(ConnectionStore.Import(entries["remote/connections.json"])).Id);
        Assert.DoesNotContain(entries.Keys, k => k.StartsWith("remote/cache"));

        _app.Remote.Connections.Save([]); // the list is lost, the password stays in the credential store
        Assert.Empty(_app.Remote.Connections.Load());
        _dialogs.Picked.Add(zip);
        _dialogs.Answers.Enqueue(1);
        _w.RestoreConfiguration();
        await Until(() => _w.StatusMessageText == Loc.Get(K.ConfigRestoreStaged), "the restore to be staged");

        _app = TestApp.Restart(_temp, _app, _dialogs);
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
        Assert.Null(_app.ConfigRestore!.Problem);
        Assert.Equal(connection.Id, Assert.Single(_app.Remote.Connections.Load()).Id);

        File.WriteAllText(localCopy, "local edit");
        _w.PairCache();
        await Until(() => _w.QuickResults.Count > 0, "the list of connections");
        _w.QuickResults[0].Run();
        await Until(() => _w.QuickResults.Count == 1 && _w.QuickResults[0].Title == "a.txt", "the copy paired with the server");
        Assert.StartsWith(Loc.Get(K.RemoteBarUnsent), _w.QuickResults[0].Detail);
        Assert.Equal("server a", File.ReadAllText(Path.Combine(_server.Path, "a.txt")));
    }

    [AvaloniaFact]
    public void Comparing_the_cache_says_so_when_no_connection_has_copies()
    {
        _w.PairCache();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get(K.RemoteCacheEmpty), _w.StatusMessageText);
        Assert.Empty(_w.QuickResults);
    }

    [AvaloniaFact]
    public async Task A_proxy_is_saved_without_its_password_in_the_policy_file()
    {
        _dialogs.Answers.Enqueue(3); // SOCKS5
        _dialogs.Prompts.Enqueue("198.51.100.7");
        _dialogs.Prompts.Enqueue("1080");
        _dialogs.Prompts.Enqueue("proxyuser");
        _dialogs.Secrets.Enqueue("proxy-pass-7731");

        _w.SetNetworkProxy();
        await Until(() => File.Exists(_app.Remote.NetworkPolicyPath), "the policy file");

        var policy = _app.Remote.Policy();
        Assert.Equal(new ProxySettings(ProxyType.Socks5, "198.51.100.7", 1080, "proxyuser"), policy.Proxy);
        Assert.Equal("proxy-pass-7731", policy.ProxyPassword);
        Assert.DoesNotContain("proxy-pass-7731", File.ReadAllText(_app.Remote.NetworkPolicyPath));
    }

    [AvaloniaFact]
    public async Task Remote_commands_say_so_when_the_tab_is_not_a_remote_file()
    {
        _w.NewDocument();
        _w.UploadRemote();
        Assert.Equal(Loc.Get(K.RemoteNotRemote), _w.StatusMessageText);
        _w.CheckRemote();
        Assert.Equal(Loc.Get(K.RemoteNotRemote), _w.StatusMessageText);
        await Task.CompletedTask;
    }
}

/// <summary>File locks in the window (P5.4.5): a locked file opens read only, says why, refuses to save, and can be switched to concurrent writing on purpose.</summary>
[Collection("LocState")]
public class LockedFileTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public LockedFileTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task A_file_another_program_forbids_writing_to_opens_read_only_with_the_reason_and_cannot_be_saved()
    {
        string path = _temp.File("locked.txt", "content");
        // Windows enforces FileShare.Read against writers; elsewhere only FileShare.None (an exclusive flock) refuses a second open
        using var other = new FileStream(path, FileMode.Open, FileAccess.Read, OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None);

        var tab = _w.OpenFile(path, null);

        Assert.True(tab.Editor!.IsReadOnly);
        Assert.True(tab.Doc.LockedReadOnly);
        Assert.Equal(Loc.Get(K.LockSystem, ("title", tab.Title)), ((InfoBar)tab.Bar!).Message);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal(Loc.Get(K.LockSaveRefused), _w.StatusMessageText);
        other.Dispose(); // an exclusive flock would refuse the read below
        Assert.Equal("content", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void A_file_another_instance_edits_opens_read_only_and_try_again_works_after_it_lets_go()
    {
        string path = _temp.File("shared.txt", "content");
        var otherInstance = new FileLocks(Path.Combine(_app.ProfileDirectory, "locks"), "other");
        var held = otherInstance.Acquire(path);

        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Editor!.IsReadOnly);
        var bar = (InfoBar)tab.Bar!;
        Assert.Equal(Loc.Get(K.LockInstanceOwner, ("title", tab.Title), ("pid", Environment.ProcessId)), bar.Message);

        held.Dispose();
        bar.Buttons.First(b => (string?)b.Content == Loc.Get(K.LockTryAgain)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.False(tab.Editor.IsReadOnly);
        Assert.Equal(Loc.Get(K.LockFree, ("title", tab.Title)), _w.StatusMessageText);
        Assert.Null(tab.Bar);
    }

    [AvaloniaFact]
    public async Task Edit_anyway_turns_on_concurrent_writing_and_a_save_still_checks_the_disk()
    {
        string path = _temp.File("both.txt", "content");
        var otherInstance = new FileLocks(Path.Combine(_app.ProfileDirectory, "locks"), "other");
        using var held = otherInstance.Acquire(path);
        var tab = _w.OpenFile(path, null);
        Assert.True(tab.Editor!.IsReadOnly);

        _w.ChooseConcurrentWrite(tab.Doc);
        Assert.False(tab.Editor.IsReadOnly);
        Assert.Equal(LockState.ConcurrentWrite, tab.Doc.Lock!.State);

        tab.Editor.Document.Insert(0, "mine ");
        File.WriteAllText(path, "written by the other instance");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        _dialogs.Answers.Enqueue(2); // Cancel the conflict question
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal("written by the other instance", File.ReadAllText(path));
        Assert.Equal(Loc.Get(K.ConflictTitle), _dialogs.Asked.Single().Title);
    }

    [AvaloniaFact]
    public void A_free_file_is_writable_and_its_lock_is_given_back_when_the_tab_closes()
    {
        string path = _temp.File("free.txt", "content");
        var tab = _w.OpenFile(path, null);
        Assert.False(tab.Editor!.IsReadOnly);
        Assert.Null(tab.Bar);

        var other = new FileLocks(Path.Combine(_app.ProfileDirectory, "locks"), "other");
        using (var seen = other.Acquire(path)) Assert.Equal(LockState.ReadOnlyOtherInstance, seen.State);
        _w.CloseTab(tab);
        using var afterClose = other.Acquire(path);
        Assert.Equal(LockState.Writable, afterClose.State);
    }
}

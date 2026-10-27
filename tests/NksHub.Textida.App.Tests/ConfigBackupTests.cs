using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>The backup of the configuration (P5.4.3, F111): what goes in, that no password or token does, and a restore that comes back at the next start.</summary>
public class ConfigBackupTests : IDisposable
{
    private const string Password = "Conn-Pass-5521";

    private readonly TempDir _profile = new();
    private readonly TempDir _out = new();

    public void Dispose()
    {
        _profile.Dispose();
        _out.Dispose();
    }

    private string P(string relative) => Path.Combine(_profile.Path, relative);

    private void Write(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
        File.WriteAllText(P(relative), text);
    }

    internal static Dictionary<string, string> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }

    private string Zip(params (string Name, string Text)[] entries)
    {
        string path = Path.Combine(_out.Path, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(text);
        }
        return path;
    }

    [Theory]
    [InlineData("githubToken", true)]
    [InlineData("API_TOKEN", true)]
    [InlineData("x-api-key", true)]
    [InlineData("password", true)]
    [InlineData("proxy.passphrase", true)]
    [InlineData("clientSecret", true)]
    [InlineData("history.storeSecretFiles", false)]
    [InlineData("privateKeyPath", false)]
    [InlineData("key", false)]
    [InlineData("auth", false)]
    [InlineData("editor.smartKeys.jsonComma", false)]
    public void A_secret_name_is_told_by_its_last_word(string name, bool secret) => Assert.Equal(secret, ConfigBackup.IsSecretName(name));

    [Fact]
    public void The_backup_holds_the_configuration_and_no_password_or_token()
    {
        var secrets = new MemoryCredentials();
        new ConnectionStore(P("remote/connections.json")).Add(new RemoteConnection { Name = "Office", Host = "server.example", UserName = "u" }, Password, secrets);
        Write("user-settings.json", """{ "schemaVersion": 1, "settings": { "history.storeSecretFiles": true, "myPlugin.apiToken": "tok-1111" } }""");
        Write("variables.json", """{ "deployToken": "tok-2222", "author": "Jane", "copied": "pw is Conn-Pass-5521" }""");
        Write("tools.json", """{ "tools": [ { "title": "Deploy", "arguments": ["--password", "Conn-Pass-5521"], "env": { "API_KEY": "key-3333" } } ] }""");
        Write("plugins/user-clips/note.txt", "the password: Conn-Pass-5521");
        Write("plugins/user-clips/hello.txt", "hello");
        Write("journal/session.json", "Conn-Pass-5521");
        Write("history/a/1.rev", "x");
        Write("remote/cache/c/a.txt", "cached");

        string zip = Path.Combine(_out.Path, ConfigBackup.SuggestedName(new DateTime(2026, 10, 27)));
        var result = ConfigBackup.Export(_profile.Path, zip, [Password]);

        Assert.EndsWith("textida-config-2026-10-27.zip", zip);
        var entries = Entries(zip);
        foreach (var (name, text) in entries)
            foreach (var secret in new[] { Password, "tok-1111", "tok-2222", "key-3333" })
                Assert.False(text.Contains(secret, StringComparison.Ordinal), $"{name} holds {secret}");
        Assert.Contains("history.storeSecretFiles", entries["user-settings.json"]);
        Assert.Contains("Jane", entries["variables.json"]);
        Assert.Contains("--password", entries["tools.json"]);
        Assert.Equal("hello", entries["plugins/user-clips/hello.txt"]);
        Assert.DoesNotContain("plugins/user-clips/note.txt", entries.Keys);
        Assert.DoesNotContain(entries.Keys, k => k.StartsWith("journal/") || k.StartsWith("history/") || k.StartsWith("remote/cache/"));
        Assert.Equal("Office", Assert.Single(ConnectionStore.Import(entries["remote/connections.json"])).Name);
        Assert.Equal(6, result.LeftOut); // apiToken, deployToken, copied, the password in the arguments, API_KEY, note.txt
        Assert.Equal(entries.Count - 1, result.Files); // the description of the backup is no file of the profile
    }

    [Fact]
    public void A_staged_restore_changes_nothing_until_the_next_start_then_puts_the_files_back_and_keeps_the_configuration_from_before()
    {
        using var other = new TempDir();
        File.WriteAllText(Path.Combine(other.Path, "user-settings.json"), """{ "schemaVersion": 1, "settings": { "editor.tabSize": 8 } }""");
        Directory.CreateDirectory(Path.Combine(other.Path, "macros"));
        File.WriteAllText(Path.Combine(other.Path, "macros", "wrap.json"), "{}");
        string zip = Path.Combine(_out.Path, "backup.zip");
        ConfigBackup.Export(other.Path, zip, []);

        Write("user-settings.json", """{ "schemaVersion": 1, "settings": { "editor.tabSize": 2 } }""");
        Write("keymap.json", "{ }");
        Assert.Equal(2, ConfigBackup.Inspect(zip));
        ConfigBackup.Stage(_profile.Path, zip);
        Assert.Contains("\"editor.tabSize\": 2", File.ReadAllText(P("user-settings.json")));

        var result = ConfigBackup.ApplyPending(_profile.Path, new DateTime(2026, 10, 27, 9, 30, 0))!;

        Assert.Equal((2, (string?)null), (result.Files, result.Problem));
        Assert.Contains("\"editor.tabSize\": 8", File.ReadAllText(P("user-settings.json")));
        Assert.Equal("{}", File.ReadAllText(P("macros/wrap.json")));
        Assert.Equal("{ }", File.ReadAllText(P("keymap.json"))); // not in the backup: stays
        Assert.False(File.Exists(P(ConfigBackup.PendingName)));
        Assert.StartsWith(P(ConfigBackup.BeforeRestoreFolder), result.SavedBefore);
        Assert.Contains("\"editor.tabSize\": 2", Entries(result.SavedBefore!)["user-settings.json"]);
        Assert.Null(ConfigBackup.ApplyPending(_profile.Path, DateTime.Now)); // applied once
    }

    [Fact]
    public void A_newer_backup_a_foreign_zip_and_a_file_that_is_no_zip_are_refused()
    {
        Assert.Contains("newer", Assert.Throws<ConfigBackupException>(() => ConfigBackup.Inspect(Zip(("textida-backup.json", """{ "format": 99 }"""), ("user-settings.json", "{}")))).Message);
        Assert.Throws<ConfigBackupException>(() => ConfigBackup.Inspect(Zip(("readme.txt", "hello"))));
        Assert.Throws<ConfigBackupException>(() => ConfigBackup.Inspect(Zip(("textida-backup.json", """{ "format": "one" }"""))));
        string notZip = Path.Combine(_out.Path, "not.zip");
        File.WriteAllText(notZip, "plain text");
        Assert.Throws<ConfigBackupException>(() => ConfigBackup.Inspect(notZip));
    }

    [Fact]
    public void A_restore_writes_nothing_outside_the_configuration_of_the_profile()
    {
        string zip = Zip(("textida-backup.json", """{ "format": 1 }"""), ("../outside.txt", "x"), ("macros/../../outside2.txt", "x"), ("journal/session.json", "x"),
            ("C:/temp/abs.txt", "x"), ("variables.json", "{}"));
        Assert.Equal(1, ConfigBackup.Inspect(zip));
        ConfigBackup.Stage(_profile.Path, zip);

        Assert.Equal(1, ConfigBackup.ApplyPending(_profile.Path, DateTime.Now)!.Files);
        Assert.True(File.Exists(P("variables.json")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_profile.Path)!, "outside.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_profile.Path)!, "outside2.txt")));
        Assert.False(Directory.Exists(P("journal")));
    }
}

/// <summary>Back Up Configuration and Restore Configuration in the window: the file, the status bar, and the restore that waits for the next start.</summary>
[Collection("LocState")]
public class ConfigBackupWindowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly MemoryCredentials _secrets = new();
    private App _app;
    private MainWindow _w;

    public ConfigBackupWindowTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Remote = new RemoteServices(_app.ProfileDirectory, _secrets);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private async Task Until(Func<bool> condition, string what)
    {
        for (var sw = Stopwatch.StartNew(); !condition(); await Task.Delay(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (sw.ElapsedMilliseconds > 10_000) throw new TimeoutException("Waited for " + what + ". status: " + _w.StatusMessageText);
        }
    }

    [AvaloniaFact]
    public async Task A_backup_without_the_password_is_restored_at_the_next_start()
    {
        const string password = "Pass-for-backup-77";
        _app.Remote.Connections.Add(new RemoteConnection { Name = "Office", Protocol = RemoteProtocol.Ftps, Host = "server.example", UserName = "u" }, password, _secrets);
        Assert.Null(_app.Workbench.Set("editor.tabSize", JsonValue.Create(7)));
        string zip = Path.Combine(_temp.Path, "config.zip");
        _dialogs.SaveFiles.Enqueue(zip);

        _w.BackupConfiguration();
        await Until(() => File.Exists(zip) && _w.StatusMessageText.Contains(zip), "the backup");

        Assert.Equal(ConfigBackup.SuggestedName(_app.Clock()), _dialogs.SaveFileNames.Single());
        var entries = ConfigBackupTests.Entries(zip);
        Assert.Contains("user-settings.json", entries.Keys);
        Assert.Contains("remote/connections.json", entries.Keys);
        Assert.Equal(Loc.Get(K.ConfigBackupDone, ("files", entries.Count - 1), ("leftOut", 0), ("path", zip)), _w.StatusMessageText);
        Assert.All(entries.Values, text => Assert.DoesNotContain(password, text));

        Assert.Null(_app.Workbench.Set("editor.tabSize", JsonValue.Create(3)));
        _dialogs.Picked.Add(zip);
        _dialogs.Answers.Enqueue(1); // At Next Start
        _w.RestoreConfiguration();
        await Until(() => _w.StatusMessageText == Loc.Get(K.ConfigRestoreStaged), "the restore to be staged");
        Assert.Equal(Loc.Get(K.ConfigRestoreTitle), _dialogs.Asked.Single().Title);
        Assert.Equal(3, _app.Workbench.Settings.GetInt("editor.tabSize")); // nothing changes in this run

        _app = TestApp.Restart(_temp, _app, _dialogs);
        _w = _app.CreateWindow(null);
        _w.ShowConfigRestore();

        Assert.Equal(7, _app.Workbench.Settings.GetInt("editor.tabSize"));
        Assert.Null(_app.ConfigRestore!.Problem);
        Assert.True(File.Exists(_app.ConfigRestore.SavedBefore));
        Assert.Equal(entries.Count - 1, _app.ConfigRestore.Files);
        Assert.Contains(_w.Notices, n => n.Message == Loc.Get(K.ConfigRestoreApplied, ("files", _app.ConfigRestore.Files), ("path", _app.ConfigRestore.SavedBefore!)));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_restore_stages_nothing()
    {
        string zip = Path.Combine(_temp.Path, "config.zip");
        ConfigBackup.Export(_app.ProfileDirectory, zip, []);
        _dialogs.Picked.Add(zip);
        _dialogs.Answers.Enqueue(2); // Cancel

        _w.RestoreConfiguration();
        await Until(() => _dialogs.Asked.Count == 1, "the question");
        Dispatcher.UIThread.RunJobs();

        Assert.False(File.Exists(Path.Combine(_app.ProfileDirectory, ConfigBackup.PendingName)));
    }
}

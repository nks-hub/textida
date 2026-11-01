using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.4.2: the job of the elevated helper, run as the user (the helper is the same code with the rights of the process it is started in), and the flows of the window with an elevation the test controls.</summary>
public class ElevatedHelperTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "elevate-" + Guid.NewGuid().ToString("N")[..8]);

    public ElevatedHelperTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private string Staged(string text)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".staged");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void An_install_writes_the_staged_bytes_over_an_existing_file_and_creates_a_missing_one()
    {
        string target = Path.Combine(_dir, "hosts");
        File.WriteAllText(target, "old old old old");
        string staged = Staged("new\n");

        ElevatedHelper.Install(staged, target, Hash(File.ReadAllBytes(staged)));
        Assert.Equal("new\n", File.ReadAllText(target));

        string created = Path.Combine(_dir, "fresh.txt");
        ElevatedHelper.Install(staged, created, Hash(File.ReadAllBytes(staged)));
        Assert.Equal("new\n", File.ReadAllText(created));
    }

    [Fact]
    public void An_install_overwrites_in_place_so_the_target_keeps_its_identity_and_a_hard_link_sees_the_change()
    {
        string target = Path.Combine(_dir, "linked.txt");
        File.WriteAllText(target, "before");
        var before = new FileInfo(target);
        DateTime created = before.CreationTimeUtc;
        string staged = Staged("after");

        ElevatedHelper.Install(staged, target, Hash(File.ReadAllBytes(staged)));

        Assert.Equal("after", File.ReadAllText(target));
        Assert.Equal(created, new FileInfo(target).CreationTimeUtc); // the same file, not a new one in its place
    }

    [Fact]
    public void A_different_hash_a_folder_or_a_relative_path_changes_nothing()
    {
        string target = Path.Combine(_dir, "keep.txt");
        File.WriteAllText(target, "keep");
        string staged = Staged("tampered");

        Assert.Contains("hash", Assert.Throws<ElevationException>(() => ElevatedHelper.Install(staged, target, Hash(Encoding.UTF8.GetBytes("something else")))).Message);
        Assert.Equal("keep", File.ReadAllText(target));
        Assert.Throws<ElevationException>(() => ElevatedHelper.Install(staged, _dir, Hash(File.ReadAllBytes(staged)))); // a folder
        Assert.Throws<ElevationException>(() => ElevatedHelper.Install(staged, "relative.txt", Hash(File.ReadAllBytes(staged))));
        Assert.Throws<ElevationException>(() => ElevatedHelper.Install("relative.staged", target, Hash(File.ReadAllBytes(staged))));
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    public void A_read_copies_a_file_into_a_new_one_and_refuses_one_over_the_limit_or_a_destination_that_exists()
    {
        string source = Staged("secret text");
        string copy = Path.Combine(_dir, "copy.tmp");
        ElevatedHelper.Read(source, copy, 1000);
        Assert.Equal("secret text", File.ReadAllText(copy));

        Assert.Throws<ElevationException>(() => ElevatedHelper.Read(source, Path.Combine(_dir, "small.tmp"), 3));
        Assert.Throws<IOException>(() => ElevatedHelper.Read(source, copy, 1000)); // never over an existing file
    }

    [Fact]
    public void The_entry_of_the_helper_says_ok_or_the_reason_in_its_result_file_and_by_its_exit_code()
    {
        string target = Path.Combine(_dir, "t.txt"), result = Path.Combine(_dir, "r1.result");
        string staged = Staged("via run");
        Assert.Equal(0, ElevatedHelper.Run(["--elevated-install", staged, target, Hash(File.ReadAllBytes(staged)), result]));
        Assert.Equal("ok", File.ReadAllText(result));
        Assert.Equal("via run", File.ReadAllText(target));

        string bad = Path.Combine(_dir, "r2.result");
        Assert.Equal(1, ElevatedHelper.Run(["--elevated-install", staged, target, "00", bad]));
        Assert.Contains("hash", File.ReadAllText(bad));
        string unknown = Path.Combine(_dir, "r3.result");
        Assert.Equal(1, ElevatedHelper.Run(["--elevated-install", "x", unknown]));
        Assert.Contains("arguments", File.ReadAllText(unknown));
    }

    [Fact]
    public async Task The_program_itself_runs_the_job_when_started_with_the_helper_arguments()
    {
        // the real entry point (Program.Main) of the app, started as a process: the same code the system starts with the rights of the user's allowance
        string app = Path.Combine(AppContext.BaseDirectory, "Textida.dll");
        string target = Path.Combine(_dir, "from-process.txt"), result = Path.Combine(_dir, "p.result");
        string staged = Staged("written by the program");
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { app, "--elevated-install", staged, target, Hash(File.ReadAllBytes(staged)), result }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("ok", File.ReadAllText(result));
        Assert.Equal("written by the program", File.ReadAllText(target));
    }

    [Fact]
    public void The_macos_scripts_pass_every_path_through_the_quoting_of_applescript_and_the_shell()
    {
        string script = SystemElevation.MacInstallScript("/tmp/a \"b\" c\\d", "/etc/hosts", "ABCDEF");
        Assert.Contains("set src to \"/tmp/a \\\"b\\\" c\\\\d\"", script);
        Assert.Contains("set want to \"abcdef\"", script);
        Assert.Contains("quoted form of src", script);
        Assert.Contains("with administrator privileges", script);
        Assert.DoesNotContain("/etc/hosts && ", script.Replace("set dst to \"/etc/hosts\"", "")); // the path is never part of the shell text
        string read = SystemElevation.MacReadScript("/private/etc/x", "/tmp/y", 100);
        Assert.Contains("-le 100", read);
        Assert.Contains("/usr/sbin/chown", read);
    }

    [Fact]
    public void The_elevation_of_this_system_is_offered_on_windows_and_macos_only()
    {
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), new SystemElevation().IsAvailable);
    }
}

[Collection("LocState")]
public class ElevationAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly FakeElevation _elevation = new();

    /// <summary>An elevation that does the job in-process, the way the helper does it, and records what it was asked.</summary>
    private sealed class FakeElevation : IElevation
    {
        public bool IsAvailable { get; set; } = true;
        public bool Refuse { get; set; }
        public List<(string Target, string Hash, string Text)> Installs { get; } = [];
        public List<string> Reads { get; } = [];
        public Dictionary<string, string> Protected { get; } = [];

        public void Install(string tempPath, string target, string sha256)
        {
            if (Refuse) throw new ElevationCancelledException();
            Installs.Add((target, sha256, File.ReadAllText(tempPath)));
            File.SetAttributes(target, FileAttributes.Normal);
            ElevatedHelper.Install(tempPath, target, sha256);
        }

        public void Read(string source, string destination, long maxBytes)
        {
            if (Refuse) throw new ElevationCancelledException();
            Reads.Add(source);
            File.WriteAllText(destination, Protected[source], new UTF8Encoding(false));
        }
    }

    public ElevationAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Elevation = _elevation;
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (DocTab Tab, string Path) OpenReadOnly(string text)
    {
        string path = _temp.File("hosts.txt", text);
        var tab = _w.OpenFile(path, null);
        File.SetAttributes(path, FileAttributes.ReadOnly); // the system refuses the write: as a protected file does
        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "added\n");
        return (tab, path);
    }

    [AvaloniaFact]
    public async Task A_save_the_system_refuses_is_offered_to_the_helper_and_written_after_the_user_agrees()
    {
        var (tab, path) = OpenReadOnly("127.0.0.1 localhost\n");
        _dialogs.Answers.Enqueue(0);

        bool saved = await _w.SaveAsync(tab);

        Assert.True(saved);
        var ask = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.ElevateSaveTitle), ask.Title);
        Assert.Contains(path, ask.Message);
        var install = Assert.Single(_elevation.Installs);
        Assert.Equal((Path.GetFullPath(path), "127.0.0.1 localhost\nadded\n"), (install.Target, install.Text));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("127.0.0.1 localhost\nadded\n"))), install.Hash);
        Assert.Equal("127.0.0.1 localhost\nadded\n", File.ReadAllText(path));
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Null(tab.Doc.Doc.ProtectedWriter); // the writer was for this one save
        Assert.False(Directory.EnumerateFiles(Path.GetTempPath(), "textida-*.tmp").Any(f => File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddMinutes(-1) && new FileInfo(f).Length < 100 && File.ReadAllText(f).Contains("added")));
    }

    [AvaloniaFact]
    public async Task Declining_the_helper_or_the_refusal_of_the_system_leaves_the_file_and_the_unsaved_text_alone()
    {
        var (tab, path) = OpenReadOnly("keep\n");
        _dialogs.Answers.Enqueue(1);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Empty(_elevation.Installs);
        Assert.Equal("keep\n", File.ReadAllText(path));
        Assert.True(tab.Doc.Doc.IsModified);

        _elevation.Refuse = true;
        _dialogs.Answers.Enqueue(0);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal("keep\n", File.ReadAllText(path));
        Assert.True(tab.Doc.Doc.IsModified);
        Assert.Null(tab.Doc.Doc.ProtectedWriter);
    }

    [AvaloniaFact]
    public async Task Where_the_system_has_no_elevation_the_failure_is_the_plain_one_and_nothing_is_asked()
    {
        _elevation.IsAvailable = false;
        var (tab, _) = OpenReadOnly("keep\n");
        Assert.False(await _w.SaveAsync(tab));
        Assert.Empty(_dialogs.Asked);
        Assert.StartsWith(Loc.Get(K.StatusSaveFailed, ("reason", "ZZZ")).Split("ZZZ")[0], _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task A_protected_file_is_read_through_the_helper_opens_with_its_own_path_and_saves_through_the_helper_again()
    {
        string path = Path.Combine(_temp.Path, "locked", "secret.conf");
        _elevation.Protected[Path.GetFullPath(path)] = "key=1\n";
        _dialogs.Answers.Enqueue(0);

        var tab = await _w.OpenProtectedAsync(path);

        Assert.NotNull(tab);
        Assert.Equal(Path.GetFullPath(path), tab!.Path);
        Assert.Equal("key=1\n", tab.Editor!.Document.Text);
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Equal([Path.GetFullPath(path)], _elevation.Reads);
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), "textida-protected-*.tmp").Where(f => File.GetLastWriteTimeUtc(f) > DateTime.UtcNow.AddMinutes(-1))); // the copy is gone

        // the folder does not exist, so a plain save fails as a protected folder does and the helper is asked again
        tab.Editor.Document.Insert(tab.Editor.Document.TextLength, "key=2\n");
        _dialogs.Answers.Enqueue(0);
        _elevation.Installs.Clear();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "key=1\n");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("key=1\nkey=2\n", File.ReadAllText(path));
        Assert.Equal(Loc.Get(K.ElevateSaveTitle), _dialogs.Asked[^1].Title);
    }

    [AvaloniaFact]
    public async Task Declining_the_read_or_a_refused_permission_opens_nothing()
    {
        string path = Path.Combine(_temp.Path, "secret.conf");
        _elevation.Protected[Path.GetFullPath(path)] = "x";
        _dialogs.Answers.Enqueue(1);
        Assert.Null(await _w.OpenProtectedAsync(path));
        Assert.Empty(_elevation.Reads);

        _elevation.Refuse = true;
        _dialogs.Answers.Enqueue(0);
        Assert.Null(await _w.OpenProtectedAsync(path));
        Assert.Equal(Loc.Get(K.ElevateCancelled), _w.StatusMessageText);
        Assert.DoesNotContain(_w.Tabs, t => t.Path == Path.GetFullPath(path));
    }

    [AvaloniaFact]
    public async Task A_file_the_account_may_not_read_offers_the_helper_after_the_open_failed()
    {
        if (!OperatingSystem.IsWindows()) return; // the denied right is set with a Windows ACL
        string path = _temp.File("noread.txt", "classified");
        var user = WindowsIdentity.GetCurrent().User!;
        var security = new FileInfo(path).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.ReadData | FileSystemRights.ReadAttributes, AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(security);
        _elevation.Protected[Path.GetFullPath(path)] = "classified";
        _dialogs.Answers.Enqueue(0);

        try
        {
            _w.OpenDropped(_w.ActiveGroup, [path]);
            ToolTestKit.WaitFor(() => _elevation.Reads.Count == 1, "the helper to be asked");
            await Task.Delay(100);
            Assert.Equal(Loc.Get(K.ElevateOpenTitle), _dialogs.Asked[0].Title);
        }
        finally
        {
            security.RemoveAccessRule(new FileSystemAccessRule(user, FileSystemRights.ReadData | FileSystemRights.ReadAttributes, AccessControlType.Deny));
            new FileInfo(path).SetAccessControl(security);
        }
    }
}

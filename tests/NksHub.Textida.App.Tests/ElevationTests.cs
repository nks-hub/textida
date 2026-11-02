using System.Diagnostics;
using System.Runtime.Versioning;
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
    public void On_Linux_pkexec_starts_the_same_helper_with_the_same_arguments_and_a_missing_pkexec_is_said_in_words()
    {
        Assert.Equal(["/opt/textida/Textida", "--elevated-read", "/etc/shadow", "/tmp/x", "100", "/tmp/r"],
            SystemElevation.LinuxArguments("/opt/textida/Textida", "/opt/textida/Textida", "--elevated-read", ["/etc/shadow", "/tmp/x", "100"], "/tmp/r"));
        Assert.Equal(["/usr/bin/dotnet", "/app/Textida.dll", "--elevated-install", "a", "b", "c", "/tmp/r"],
            SystemElevation.LinuxArguments("/usr/bin/dotnet", "/app/Textida.dll", "--elevated-install", ["a", "b", "c"], "/tmp/r"));
        if (!OperatingSystem.IsLinux() || SystemElevation.FindPkexec() is not null) return;
        Assert.Contains("pkexec", Assert.Throws<ElevationException>(() => new SystemElevation().Read("/etc/shadow", Path.Combine(_dir, "d"), 10)).Message);
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
    public void The_entry_of_the_helper_says_ok_or_the_reason_by_its_output_and_its_exit_code()
    {
        string target = Path.Combine(_dir, "t.txt");
        string staged = Staged("via run");
        Assert.Equal((0, "ok"), Answer("--elevated-install", staged, target, Hash(File.ReadAllBytes(staged))));
        Assert.Equal("via run", File.ReadAllText(target));

        var (code, reason) = Answer("--elevated-install", staged, target, "00");
        Assert.Equal(1, code);
        Assert.Contains("hash", reason);
        Assert.Contains("arguments", Answer("--elevated-install", "x").Text);

        // a path as the last argument is no way back any more: the helper writes no file the user named
        string named = Path.Combine(_dir, "r.result");
        Assert.Equal(0, ElevatedHelper.Run(["--elevated-install", staged, target, Hash(File.ReadAllBytes(staged)), named], TextWriter.Null, TextWriter.Null));
        Assert.False(File.Exists(named));
    }

    /// <summary>Runs the helper with the answer on its standard output and error ("-"), and gives the exit code and what it said.</summary>
    private static (int Code, string Text) Answer(params string[] args)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        int code = ElevatedHelper.Run([.. args, "-"], output, errors);
        return (code, output.ToString() + errors.ToString());
    }

    [Fact]
    public async Task The_program_itself_runs_the_job_when_started_with_the_helper_arguments()
    {
        // the real entry point (Program.Main) of the app, started as a process: the same code the system starts with the rights of the user's allowance
        string app = Path.Combine(AppContext.BaseDirectory, "Textida.dll");
        string target = Path.Combine(_dir, "from-process.txt");
        string staged = Staged("written by the program");
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in new[] { app, "--elevated-install", staged, target, Hash(File.ReadAllBytes(staged)), "-" }) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("ok", output.Trim());
        Assert.Equal("written by the program", File.ReadAllText(target));
    }

    [Fact]
    public void The_macos_scripts_pass_every_path_through_the_quoting_of_applescript_and_the_shell()
    {
        // an install runs the helper of the program as root, which hashes the bytes it then writes: no shasum and cp that read the staged file twice
        var line = SystemElevation.LinuxArguments("/Applications/Textida.app/Contents/MacOS/Textida", null, "--elevated-install", ["/tmp/a \"b\" c\\d", "/etc/hosts", "ABCDEF"], "/tmp/r");
        string script = SystemElevation.MacHelperScript(line);
        Assert.Contains("set a0 to \"/Applications/Textida.app/Contents/MacOS/Textida\"", script);
        Assert.Contains("set a2 to \"/tmp/a \\\"b\\\" c\\\\d\"", script);
        Assert.EndsWith("do shell script quoted form of a0 & \" \" & quoted form of a1 & \" \" & quoted form of a2 & \" \" & quoted form of a3 & \" \" & quoted form of a4 & \" \" & quoted form of a5 with administrator privileges", script);
        Assert.Contains("set a1 to \"--elevated-install\"", script);
        Assert.DoesNotContain("shasum", script);
        Assert.DoesNotContain("/bin/cp", script);

        // a read runs the same helper as root too: no stat, cp or chown of the shell on paths the user could swap, and the answer comes on the standard output ("-"), not in a file
        string read = SystemElevation.MacHelperScript(SystemElevation.LinuxArguments("/Applications/Textida.app/Contents/MacOS/Textida", null, "--elevated-read-as", ["/private/etc/x 'y'", "textida-read-1", "100", "501"], "-"));
        Assert.Contains("set a1 to \"--elevated-read-as\"", read);
        Assert.Contains("set a2 to \"/private/etc/x 'y'\"", read);
        Assert.Contains("set a6 to \"-\"", read);
        Assert.EndsWith("quoted form of a6 with administrator privileges", read);
        foreach (string shell in new[] { "stat", "/bin/cp", "chown", "test " }) Assert.DoesNotContain(shell, read);
    }

    [Fact]
    public void The_read_as_root_takes_only_a_plain_folder_name_in_the_shared_temporary_folder()
    {
        string source = Staged("secret");
        foreach (string name in new[] { "", ".", "..", "../etc", "a/b", "/etc" })
        {
            var (code, reason) = Answer("--elevated-read-as", source, name, "100", "501");
            Assert.Equal(1, code);
            Assert.Contains("plain name", reason);
        }
        Assert.Contains("arguments", Answer("--elevated-read-as", source, "x", "100", "-1").Text); // not a user id
    }

    // ---- Windows: UAC hands back only the exit code, so the answer and the copy go into a folder the elevated helper makes itself in the Temp folder of Windows ----

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier Me => WindowsIdentity.GetCurrent().User!;

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier Sid(WellKnownSidType type) => new(type, null);

    private static void Junction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target]) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(Directory.Exists(link) && File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "the junction was made");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_windows_the_box_of_the_helper_lets_the_user_read_and_delete_but_not_write()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the box of UAC is Windows only");
        string box = Path.Combine(_dir, "box");
        ElevatedHelper.MakeBox(box, Me, Sid(WellKnownSidType.BuiltinAdministratorsSid));

        Assert.False(File.GetAttributes(box).HasFlag(FileAttributes.ReparsePoint));
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        {
            // the user cannot place a file, a link or a folder in it before or while the elevated helper writes
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(box, ElevatedHelper.ResultName), "planted"));
            Assert.Throws<UnauthorizedAccessException>(() => Directory.CreateDirectory(Path.Combine(box, "sub")));
            // the user owns this test box, yet cannot give itself the right to write: OWNER RIGHTS takes the implied right to change the rights away
            var rights = new DirectoryInfo(box).GetAccessControl();
            rights.AddAccessRule(new FileSystemAccessRule(Me, FileSystemRights.FullControl, AccessControlType.Allow));
            Assert.Throws<UnauthorizedAccessException>(() => new DirectoryInfo(box).SetAccessControl(rights));
        }
        Directory.Delete(box, true); // and can remove it when done
        Assert.False(Directory.Exists(box));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_windows_the_box_writes_its_answer_and_copy_as_new_files_the_user_can_read()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the box of UAC is Windows only");
        // the test is its own "administrator" here: the writer is this account, the reader another one
        string box = Path.Combine(_dir, "own-box"), source = Staged("protected text");
        ElevatedHelper.MakeBox(box, Sid(WellKnownSidType.BuiltinGuestsSid), Me);
        ElevatedHelper.Read(source, Path.Combine(box, ElevatedHelper.CopyName), 1000);
        ElevatedHelper.WriteNew(Path.Combine(box, ElevatedHelper.ResultName), Encoding.UTF8.GetBytes("ok"));
        Assert.Equal("protected text", File.ReadAllText(Path.Combine(box, ElevatedHelper.CopyName)));
        Assert.Equal("ok", File.ReadAllText(Path.Combine(box, ElevatedHelper.ResultName)));
        Assert.Throws<IOException>(() => ElevatedHelper.WriteNew(Path.Combine(box, ElevatedHelper.ResultName), [1])); // never over a file that is there
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_windows_a_junction_in_place_of_the_box_is_refused_and_nothing_is_written_where_it_points()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the box of UAC is Windows only");
        string target = Path.Combine(_dir, "system-like"), box = Path.Combine(_dir, "box-link");
        Directory.CreateDirectory(target);
        Junction(box, target);

        Assert.Throws<ElevationException>(() => ElevatedHelper.MakeBox(box, Me, Sid(WellKnownSidType.BuiltinAdministratorsSid)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));

        // a bad box in the arguments is refused before any job is done
        string staged = Staged("x"), installed = Path.Combine(_dir, "never.txt");
        foreach (string box2 in new[] { "box:not-a-sid:n", $"box:{Me.Value}:..", $"box:{Me.Value}:a\\b", "box:" })
            Assert.Equal(1, ElevatedHelper.Run(["--elevated-install", staged, installed, Hash(File.ReadAllBytes(staged)), box2], TextWriter.Null, TextWriter.Null));
        Assert.False(File.Exists(installed));
        Directory.Delete(box); // the junction itself, before the recursive clean-up of the test folder meets it
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_windows_a_link_in_place_of_the_answer_is_refused_and_its_target_is_left_alone()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the box of UAC is Windows only");
        string target = Path.Combine(_dir, "important.txt"), link = Path.Combine(_dir, ElevatedHelper.ResultName);
        File.WriteAllText(target, "keep");
        try { File.CreateSymbolicLink(link, target); }
        catch (IOException e) { Assert.Skip("a file link needs Developer Mode or the right to make links: " + e.Message); }
        catch (UnauthorizedAccessException e) { Assert.Skip("a file link needs Developer Mode or the right to make links: " + e.Message); }

        Assert.Throws<IOException>(() => ElevatedHelper.WriteNew(link, Encoding.UTF8.GetBytes("ok")));
        Assert.Equal("keep", File.ReadAllText(target));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_read_as_root_writes_a_0600_copy_in_a_new_0700_folder_and_hands_both_to_the_user()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the read as root is the job of macOS and Linux (open, mkdir, fchown of libc)");
        string source = Staged("secret text"), folder = Path.Combine(_dir, "read");
        uint me = ElevatedHelper.getuid();

        string copy = ElevatedHelper.ReadAs(source, folder, 1000, me);

        Assert.Equal(Path.Combine(folder, ElevatedHelper.CopyName), copy);
        Assert.Equal("secret text", File.ReadAllText(copy));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(copy));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
        Assert.Equal(me.ToString(), Owner(copy));
        Assert.Equal(me.ToString(), Owner(folder));

        if (me != 0) Assert.Throws<ElevationException>(() => ElevatedHelper.ReadAs(source, Path.Combine(_dir, "other"), 1000, 0)); // the copy really is chowned: a user may not give it to root
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void The_read_as_root_refuses_a_folder_that_exists_a_linked_folder_a_linked_source_and_a_file_over_the_limit()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the read as root is the job of macOS and Linux (open, mkdir, fchown of libc)");
        string source = Staged("secret");
        uint me = ElevatedHelper.getuid();

        string existing = Path.Combine(_dir, "existing");
        Directory.CreateDirectory(existing);
        Assert.Throws<ElevationException>(() => ElevatedHelper.ReadAs(source, existing, 1000, me));
        Assert.Empty(Directory.EnumerateFileSystemEntries(existing));

        string elsewhere = Path.Combine(_dir, "elsewhere"), linked = Path.Combine(_dir, "linked");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateSymbolicLink(linked, elsewhere);
        Assert.Throws<ElevationException>(() => ElevatedHelper.ReadAs(source, linked, 1000, me));
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere)); // nothing written where the link points

        string link = Path.Combine(_dir, "source-link");
        File.CreateSymbolicLink(link, source);
        Assert.Throws<ElevationException>(() => ElevatedHelper.ReadAs(link, Path.Combine(_dir, "via-link"), 1000, me));
        Assert.False(Directory.Exists(Path.Combine(_dir, "via-link")));

        Assert.Throws<ElevationException>(() => ElevatedHelper.ReadAs(source, Path.Combine(_dir, "big"), 3, me));
        Assert.False(Directory.Exists(Path.Combine(_dir, "big"))); // checked before anything is made
    }

    private static string Owner(string path)
    {
        var info = new ProcessStartInfo("stat", OperatingSystem.IsMacOS() ? ["-f", "%u", path] : ["-c", "%u", path]) { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(info)!;
        string owner = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return owner;
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

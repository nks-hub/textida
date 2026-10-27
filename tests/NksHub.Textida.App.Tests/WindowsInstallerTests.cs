using System.Diagnostics;
using Microsoft.Win32;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.7.1: tools/windows-installer/Setup.ps1 on a fake package (whoami.exe stands in (it exits at once on any argument) for Textida.exe, so the real program never starts). Install folder, shortcut folder and
/// the Apps &amp; features key are redirected to a temp folder and <c>HKCU\Software\Textida.Tests\{guid}</c>; the real profile and the real uninstall list stay untouched.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class WindowsInstallerTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _keyPath = @"Software\Textida.Tests\" + Guid.NewGuid().ToString("N");
    private readonly string _package, _dir, _shortcuts;

    public WindowsInstallerTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Setup.ps1 is the Windows installer");
        _package = Path.Combine(_temp.Path, "package");
        _dir = Path.Combine(_temp.Path, "installed");
        _shortcuts = Path.Combine(_temp.Path, "start");
        Directory.CreateDirectory(Path.Combine(_package, "plugins", "json"));
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), Path.Combine(_package, "Textida.exe"));
        File.WriteAllText(Path.Combine(_package, "plugins", "json", "plugin.txt"), "old");
        File.Copy(Path.Combine(Repo.Root, "tools", "windows-installer", "Setup.ps1"), Path.Combine(_package, "Setup.ps1"));
        File.WriteAllText(Path.Combine(_package, "version.txt"), "1.0.0");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    private (int Code, string Output) Setup(string script, string language, params string[] extra)
    {
        var args = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Dir", _dir, "-UninstallKey", @"HKCU:\" + _keyPath + @"\Uninstall", "-ShortcutDir", _shortcuts, "-Language", language };
        args.AddRange(extra);
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), "Setup.ps1 did not finish");
        return (process.ExitCode, output.Result + error.Result);
    }

    private (int Code, string Output) Install(string language) => Setup(Path.Combine(_package, "Setup.ps1"), language);

    private RegistryKey? Entry() => Registry.CurrentUser.OpenSubKey(_keyPath + @"\Uninstall");

    [Fact]
    public void Install_copies_the_package_adds_shortcut_and_entry_and_speaks_the_chosen_language()
    {
        var (code, output) = Install("de");

        Assert.Equal(0, code);
        Assert.Contains("installiert", output);
        Assert.True(File.Exists(Path.Combine(_dir, "Textida.exe")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(_dir, "plugins", "json", "plugin.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Setup.ps1")));
        Assert.False(File.Exists(Path.Combine(_dir, "Install.cmd")));
        Assert.True(File.Exists(Path.Combine(_shortcuts, "Textida.lnk")));
        using var entry = Entry()!;
        Assert.Equal("1.0.0", entry.GetValue("DisplayVersion"));
        Assert.Equal(_dir, entry.GetValue("InstallLocation"));
        Assert.Contains(Path.Combine(_dir, "Setup.ps1"), (string)entry.GetValue("UninstallString")!);
        Assert.Contains("-Uninstall", (string)entry.GetValue("UninstallString")!);
    }

    [Fact]
    public void Update_replaces_the_files_and_keeps_the_portable_profile()
    {
        Install("en");
        Directory.CreateDirectory(Path.Combine(_dir, "profile"));
        File.WriteAllText(Path.Combine(_dir, "profile", "settings.json"), "mine");
        File.Delete(Path.Combine(_package, "plugins", "json", "plugin.txt"));
        File.WriteAllText(Path.Combine(_package, "plugins", "json", "new.txt"), "new");
        File.WriteAllText(Path.Combine(_package, "version.txt"), "1.1.0");

        var (code, output) = Install("cs");

        Assert.Equal(0, code);
        Assert.Contains("byla aktualizov", output);
        Assert.False(File.Exists(Path.Combine(_dir, "plugins", "json", "plugin.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_dir, "plugins", "json", "new.txt")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_dir, "profile", "settings.json")));
        using var entry = Entry()!;
        Assert.Equal("1.1.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public void A_folder_that_is_not_ours_and_an_incomplete_package_are_refused_without_changes()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "other.txt"), "keep");
        var (code, output) = Install("en");
        Assert.Equal(1, code);
        Assert.Contains("is not a Textida folder", output);
        Assert.Equal(["other.txt"], Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));
        Assert.Null(Entry());

        File.Delete(Path.Combine(_dir, "other.txt"));
        File.Delete(Path.Combine(_package, "Textida.exe"));
        (code, output) = Install("en");
        Assert.Equal(1, code);
        Assert.Contains("Textida.exe is missing", output);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void Uninstall_removes_program_shortcut_and_entry_but_keeps_a_portable_profile()
    {
        Install("en");
        Directory.CreateDirectory(Path.Combine(_dir, "profile"));
        File.WriteAllText(Path.Combine(_dir, "profile", "settings.json"), "mine");

        var (code, output) = Setup(Path.Combine(_dir, "Setup.ps1"), "de", "-Uninstall");

        Assert.Equal(0, code);
        Assert.Contains("wurde entfernt", output);
        Assert.False(File.Exists(Path.Combine(_shortcuts, "Textida.lnk")));
        Assert.Null(Entry());
        Assert.Equal(["profile"], Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_dir, "profile", "settings.json")));
    }

    [Fact]
    public void Uninstall_without_profile_removes_the_folder_and_a_second_uninstall_changes_nothing()
    {
        Install("en");
        var script = Path.Combine(_dir, "Setup.ps1");
        Assert.Equal(0, Setup(script, "en", "-Uninstall").Code);
        Assert.False(Directory.Exists(_dir));

        var (code, output) = Setup(Path.Combine(_package, "Setup.ps1"), "en", "-Uninstall");
        Assert.Equal(1, code);
        Assert.Contains("is not installed", output);
    }
}

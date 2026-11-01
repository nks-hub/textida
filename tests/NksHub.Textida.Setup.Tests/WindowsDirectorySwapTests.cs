using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup.Tests;

public sealed class WindowsDirectorySwapTests : IDisposable
{
    private readonly Sandbox _box = new();
    public void Dispose() => _box.Dispose();

    private void Versions()
    {
        var drive = new DriveInfo(Path.GetPathRoot(_box.Root)!);
        Assert.SkipUnless(drive.DriveFormat == "NTFS", "the native transaction requires an NTFS test volume");
        foreach (var (suffix, version) in new[] { ("", "1.1.0"), (".new", "1.2.0"), (".previous", "1.0.0") })
        {
            var dir = _box.Dir + suffix;
            Directory.CreateDirectory(Path.Combine(dir, "plugins", "example"));
            File.WriteAllText(Path.Combine(dir, "Textida.exe"), "program " + version);
            File.WriteAllText(Path.Combine(dir, "version.txt"), version);
            File.WriteAllText(Path.Combine(dir, "plugins", "example", "data.txt"), version);
        }
    }

    private string[] Snapshot() => Directory.EnumerateFiles(_box.Root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(_box.Root, path) + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .ToArray();

    [Fact]
    public void A_real_ntfs_transaction_updates_and_rolls_back_nonempty_directories()
    {
        Versions();
        var swap = new WindowsDirectorySwap(_box.Dir);

        swap.Apply(_box.Dir + ".new", rollback: false);

        Assert.Equal("1.2.0", Installer.InstalledVersion(_box.Dir));
        Assert.Equal("1.1.0", Installer.InstalledVersion(_box.Dir + ".previous"));
        Assert.Equal("1.2.0", _box.Read("plugins/example/data.txt"));
        Assert.False(Directory.Exists(_box.Dir + ".new"));
        Assert.False(Directory.Exists(_box.Dir + ".aside"));
        Assert.False(Directory.Exists(_box.Dir + ".retired"));

        swap.Apply(_box.Dir + ".previous", rollback: true);
        Assert.Equal("1.1.0", Installer.InstalledVersion(_box.Dir));
        Assert.Equal("1.2.0", Installer.InstalledVersion(_box.Dir + ".previous"));
        swap.Apply(_box.Dir + ".previous", rollback: true);
        Assert.Equal("1.2.0", Installer.InstalledVersion(_box.Dir));
    }

    [Theory]
    [InlineData("after-current-move", false)]
    [InlineData("after-incoming-move", false)]
    [InlineData("before-commit", false)]
    [InlineData("after-current-move", true)]
    [InlineData("after-incoming-move", true)]
    [InlineData("before-commit", true)]
    public void Aborting_before_commit_keeps_every_original_directory_and_its_children(string checkpoint, bool rollback)
    {
        Versions();
        var expected = Snapshot();
        var swap = new WindowsDirectorySwap(_box.Dir)
        {
            Checkpoint = point => { if (point == checkpoint) throw new IOException("injected abort"); }
        };

        var error = Assert.Throws<IOException>(() => swap.Apply(_box.Dir + (rollback ? ".previous" : ".new"), rollback));

        Assert.Equal("injected abort", error.Message);
        Assert.Equal(expected, Snapshot());
        Assert.True(File.Exists(_box.Exe));
        Assert.False(Directory.Exists(_box.Dir + ".aside"));
        Assert.False(Directory.Exists(_box.Dir + ".retired"));
    }

    [Fact]
    public async Task The_powershell_installer_uses_the_native_transaction_and_keeps_the_portable_profile()
    {
        Versions();
        _box.Add("profile/settings.json", "latest settings");
        _box.Add("profile/document.journal", "unconfirmed text");
        _box.Add("textida.portable", "");
        var journalHash = SHA256.HashData(File.ReadAllBytes(Path.Combine(_box.Dir, "profile", "document.journal")));

        var result = await PowerShell(Path.Combine(Repo.Root, "tools", "windows-installer", "Setup.ps1"),
            "-Dir", _box.Dir, "-ApplyUpdate", _box.Dir + ".new", "-UninstallKey", @"HKCU:\" + _box.Locations.UninstallKeyPath);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal("1.2.0", Installer.InstalledVersion(_box.Dir));
        Assert.Equal("1.1.0", Installer.InstalledVersion(_box.Dir + ".previous"));
        Assert.Equal("latest settings", _box.Read("profile/settings.json"));
        Assert.Equal(journalHash, SHA256.HashData(File.ReadAllBytes(Path.Combine(_box.Dir, "profile", "document.journal"))));
        Assert.False(Directory.Exists(_box.Dir + ".aside"));
        Assert.False(Directory.Exists(_box.Dir + ".retired"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_aborted_or_terminated_powershell_transaction_preserves_the_installed_executable(bool terminate)
    {
        Versions();
        var script = File.ReadAllText(Path.Combine(Repo.Root, "tools", "windows-installer", "Setup.ps1"));
        var source = Regex.Match(script, @"Add-Type -TypeDefinition @'\r?\n(?<code>.*?)\r?\n'@", RegexOptions.Singleline);
        Assert.True(source.Success, "the standalone installer must embed its transaction implementation");
        var runner = Path.Combine(_box.Root, "interrupt.ps1");
        var action = terminate ? "[Environment]::Exit(17)" : "throw 'injected abort'";
        File.WriteAllText(runner, """
            $ErrorActionPreference = 'Stop'
            Add-Type -TypeDefinition @'
            """ + "\n" + source.Groups["code"].Value + "\n'@\n" + """
            try {
                [TextidaDirectorySwap]::Apply($env:TEXTIDA_TEST_INSTALL, $env:TEXTIDA_TEST_INCOMING, $false,
                    [Action[string]] { param($point) if ($point -eq 'before-commit') {
            """ + "\n" + action + "\n" + """
                    } })
                exit 3
            } catch {
                if ($_.Exception.ToString() -notmatch 'injected abort') { throw }
                exit 0
            }
            """);
        var expected = Snapshot();

        var result = await PowerShell(runner);

        Assert.True(result.ExitCode == (terminate ? 17 : 0), result.Output);
        Assert.Equal(expected, Snapshot());
        Assert.True(File.Exists(_box.Exe));
    }

    private async Task<(int ExitCode, string Output)> PowerShell(string script, params string[] args)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _box.Root,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(args)) start.ArgumentList.Add(arg);
        start.Environment["TEXTIDA_TEST_INSTALL"] = _box.Dir;
        start.Environment["TEXTIDA_TEST_INCOMING"] = _box.Dir + ".new";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output + await errors);
    }
}

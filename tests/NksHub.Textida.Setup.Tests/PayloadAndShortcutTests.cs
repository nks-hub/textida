using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup.Tests;

/// <summary>The package inside the setup program, the shortcut files, and the removal of the uninstaller itself.</summary>
[SupportedOSPlatform("windows")]
public class PayloadAndShortcutTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private string Program(string content = "PROGRAM-BYTES")
    {
        var path = Path.Combine(_box.Root, "program-" + Guid.NewGuid().ToString("N")[..6] + ".exe");
        File.WriteAllText(path, content);
        return path;
    }

    private string PackageFile(string version = "7.0.0")
    {
        var path = Path.Combine(_box.Root, "package-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        File.WriteAllBytes(path, Sandbox.Package(version).ToArray());
        return path;
    }

    // ---- the package in the program ----

    [Fact]
    public void An_appended_package_is_read_back_as_a_zip_and_the_program_is_unchanged()
    {
        var program = Program();
        var output = Path.Combine(_box.Root, "setup.exe");

        PayloadFile.Append(program, PackageFile("7.0.0"), output);

        Assert.True(PayloadFile.HasPayload(output));
        Assert.False(PayloadFile.HasPayload(program));
        using var stream = PayloadFile.Open(output)!;
        Assert.Equal("7.0.0", Installer.PackageVersion(stream));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "Textida.exe");
        Assert.StartsWith("PROGRAM-BYTES", File.ReadAllText(output));
    }

    [Fact]
    public void The_copy_without_the_package_is_the_original_program_byte_for_byte()
    {
        var program = Program(new string('x', 70_000));
        var setup = Path.Combine(_box.Root, "setup.exe");
        PayloadFile.Append(program, PackageFile(), setup);
        var copy = Path.Combine(_box.Root, "copy.exe");

        PayloadFile.CopyWithoutPayload(setup, copy);

        Assert.Equal(File.ReadAllBytes(program), File.ReadAllBytes(copy));
        PayloadFile.CopyWithoutPayload(program, copy); // a program without a package is copied whole
        Assert.Equal(File.ReadAllBytes(program), File.ReadAllBytes(copy));
    }

    [Fact]
    public void Appending_to_a_setup_that_has_a_package_replaces_it()
    {
        var program = Program();
        var first = Path.Combine(_box.Root, "first.exe");
        PayloadFile.Append(program, PackageFile("1.0.0"), first);
        var second = Path.Combine(_box.Root, "second.exe");
        var package = PackageFile("2.0.0");

        PayloadFile.Append(first, package, second);

        using var stream = PayloadFile.Open(second)!;
        Assert.Equal("2.0.0", Installer.PackageVersion(stream));
        Assert.Equal(new FileInfo(program).Length + new FileInfo(package).Length + PayloadFile.TrailerLength, new FileInfo(second).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("TXSETUP1")]
    public void A_file_without_a_trailer_has_no_package(string content)
    {
        var path = Program(content);
        Assert.False(PayloadFile.HasPayload(path));
        Assert.Null(PayloadFile.Open(path));
    }

    [Fact]
    public void A_trailer_whose_length_does_not_fit_the_file_is_not_a_package()
    {
        var path = Program(new string('a', 100));
        using (var file = new FileStream(path, FileMode.Append))
        {
            file.Write("TXSETUP1"u8);
            file.Write(BitConverter.GetBytes(1_000_000L)); // longer than the file
        }
        Assert.False(PayloadFile.HasPayload(path));
    }

    [Fact]
    public void The_package_stream_can_be_read_while_the_program_file_is_in_use()
    {
        var setup = Path.Combine(_box.Root, "setup.exe");
        PayloadFile.Append(Program(), PackageFile(), setup);

        using var running = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read); // what the loader holds
        using var stream = PayloadFile.Open(setup)!;

        Assert.Equal("7.0.0", Installer.PackageVersion(stream));
    }

    // ---- shortcut files ----

    [Fact]
    public void A_shortcut_points_at_the_target_with_its_icon_and_replaces_an_older_one()
    {
        var target = Program();
        var other = Program();
        var link = Path.Combine(_box.Root, "links", "Textida.lnk");

        ShortcutFile.Create(link, target, Path.GetDirectoryName(target)!, "Textida");
        Assert.Equal(target, ShortcutFile.ReadTarget(link), ignoreCase: true);
        Assert.Equal((target, 0), ShortcutFile.ReadIcon(link)!.Value, new IconComparer());

        ShortcutFile.Create(link, other, Path.GetDirectoryName(other)!, "Textida");
        Assert.Equal(other, ShortcutFile.ReadTarget(link), ignoreCase: true);
    }

    [Fact]
    public void A_missing_file_and_a_file_that_is_not_a_shortcut_have_no_target()
    {
        var text = Path.Combine(_box.Root, "plain.lnk");
        File.WriteAllText(text, "not a shortcut");

        Assert.Null(ShortcutFile.ReadTarget(Path.Combine(_box.Root, "missing.lnk")));
        Assert.Null(ShortcutFile.ReadTarget(text));
        Assert.Null(ShortcutFile.ReadIcon(text));
    }

    [Fact]
    public async Task Shortcuts_work_from_a_thread_of_any_apartment_and_in_parallel()
    {
        var target = Program();
        var tasks = Enumerable.Range(0, 6).Select(i => Task.Run(() =>
        {
            var link = Path.Combine(_box.Root, "parallel", $"{i}.lnk");
            ShortcutFile.Create(link, target, Path.GetDirectoryName(target)!, "Textida");
            return ShortcutFile.ReadTarget(link);
        })).ToArray();
        Assert.All(await Task.WhenAll(tasks), result => Assert.Equal(target, result, ignoreCase: true));
    }

    private sealed class IconComparer : IEqualityComparer<(string Path, int Index)>
    {
        public bool Equals((string Path, int Index) a, (string Path, int Index) b) => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) && a.Index == b.Index;

        public int GetHashCode((string Path, int Index) value) => value.Index;
    }

    // ---- switches of the system integration ----

    [Fact]
    public void Each_switch_changes_only_what_it_names_and_reads_back_from_the_registry_and_the_disk()
    {
        var exe = Program();
        var integration = new SystemIntegration(_box.Locations, exe);

        integration.SetMenu(true, "Open with Textida");
        integration.SetAssociated(".log", true);
        integration.SetStartMenuShortcut(true);
        Assert.True(integration.HasMenu());
        Assert.Equal([".log"], integration.AssociatedExtensions());
        Assert.True(integration.HasStartMenuShortcut());
        Assert.False(integration.HasDesktopShortcut());

        integration.SetAssociations([".md", ".json"]);
        Assert.Equal([".json", ".md"], integration.AssociatedExtensions().Order());
        integration.SetMenu(false, "x");
        integration.SetStartMenuShortcut(false);
        Assert.False(integration.HasMenu());
        Assert.False(integration.HasStartMenuShortcut());
        Assert.Equal(2, integration.AssociatedExtensions().Count);

        integration.RemoveAll();
        Assert.Empty(_box.ClassesSnapshot());
    }

    // ---- the uninstaller removes itself ----

    [Fact]
    public void The_removal_command_waits_deletes_the_file_and_removes_only_an_empty_folder()
    {
        var info = SelfRemoval.Command(@"C:\Some Folder\TextidaSetup.exe", @"C:\Some Folder");

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/c ping -n 4 127.0.0.1 >nul & del /f /q \"C:\\Some Folder\\TextidaSetup.exe\" & rmdir \"C:\\Some Folder\"", info.Arguments);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void The_scheduled_removal_really_deletes_the_file_and_the_empty_folder_after_a_moment()
    {
        var dir = Path.Combine(_box.Root, "leftover folder");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "TextidaSetup.exe");
        File.WriteAllText(file, "x");

        SelfRemoval.Schedule(file, dir);

        var clock = Stopwatch.StartNew();
        while (Directory.Exists(dir) && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(100);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void The_scheduled_removal_leaves_a_folder_that_still_holds_something()
    {
        var dir = Path.Combine(_box.Root, "with profile");
        Directory.CreateDirectory(Path.Combine(dir, "profile"));
        var file = Path.Combine(dir, "TextidaSetup.exe");
        File.WriteAllText(file, "x");

        SelfRemoval.Schedule(file, dir);

        var clock = Stopwatch.StartNew();
        while (File.Exists(file) && clock.Elapsed < TimeSpan.FromSeconds(30)) Thread.Sleep(100);
        Thread.Sleep(500);
        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(Path.Combine(dir, "profile")));
    }
}

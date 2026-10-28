using System.Runtime.Versioning;
using Microsoft.Win32;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup.Tests;

/// <summary>Install, update, rollback and uninstall against a temp folder and a registry key of the test; see <see cref="Sandbox"/>.</summary>
[SupportedOSPlatform("windows")]
public class InstallerTests : IDisposable
{
    private readonly Sandbox _box = new();

    public void Dispose() => _box.Dispose();

    private InstallResult Install(string version = "1.0.0", string content = "old", InstallOptions? options = null, params (string Name, string Content)[] extra)
    {
        using var package = Sandbox.Package(version, content, extra);
        return _box.Installer().Install(package, options ?? _box.Options(setupExe: _box.FakeSetup()));
    }

    private static SetupError Refused(Action action) => Assert.Throws<SetupException>(action).Error;

    // ---- first install ----

    [Fact]
    public void A_first_install_extracts_the_package_but_not_the_zip_script()
    {
        var result = Install();

        Assert.False(result.Updated);
        Assert.Null(result.PreviousDir);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(_box.Exe, result.ExePath);
        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Equal("old", _box.Read(Path.Combine("plugins", "json", "plugin.txt")));
        Assert.Equal("script", _box.Read("Setup.ps1")); // the updater of an install made by the script needs it
        Assert.False(File.Exists(Path.Combine(_box.Dir, "Install.cmd")));
        Assert.False(Directory.Exists(_box.Dir + ".new"));
    }

    [Fact]
    public void The_apps_list_entry_names_the_version_publisher_icon_and_the_uninstaller()
    {
        Install(version: "2.3.4");

        using var entry = _box.UninstallEntry()!;
        var setup = Path.Combine(_box.Dir, "TextidaSetup.exe");
        Assert.Equal("Textida", entry.GetValue("DisplayName"));
        Assert.Equal("2.3.4", entry.GetValue("DisplayVersion"));
        Assert.Equal("NKS Hub", entry.GetValue("Publisher"));
        Assert.Equal(_box.Dir, entry.GetValue("InstallLocation"));
        Assert.Equal($"\"{_box.Exe}\",0", entry.GetValue("DisplayIcon"));
        Assert.Equal($"\"{setup}\" --uninstall", entry.GetValue("UninstallString"));
        Assert.Equal($"\"{setup}\" --uninstall --silent", entry.GetValue("QuietUninstallString"));
        Assert.Equal("https://github.com/nks-hub/textida", entry.GetValue("URLInfoAbout"));
        Assert.Equal(1, entry.GetValue("NoModify"));
        Assert.Equal(1, entry.GetValue("NoRepair"));
        Assert.True((int)entry.GetValue("EstimatedSize")! >= 0);
        Assert.Equal(DateTime.Now.ToString("yyyyMMdd"), entry.GetValue("InstallDate"));
    }

    [Fact]
    public void The_uninstaller_in_the_folder_is_the_setup_without_its_package()
    {
        var setup = _box.FakeSetup();
        Install(options: _box.Options(setupExe: setup));

        var installed = Path.Combine(_box.Dir, "TextidaSetup.exe");
        Assert.True(PayloadFile.HasPayload(setup));
        Assert.False(PayloadFile.HasPayload(installed));
        Assert.Equal(new string('P', 5000), File.ReadAllText(installed));
    }

    [Fact]
    public void The_options_become_a_menu_entry_associations_and_shortcuts()
    {
        Install(options: _box.Options(startMenu: true, desktop: true, menu: true, extensions: [".log", ".md"], setupExe: _box.FakeSetup()));

        var integration = new SystemIntegration(_box.Locations, _box.Exe);
        Assert.True(integration.HasMenu());
        Assert.Equal([".log", ".md"], integration.AssociatedExtensions());
        Assert.Equal(_box.Exe, ShortcutFile.ReadTarget(integration.StartMenuShortcut), ignoreCase: true);
        Assert.Equal(_box.Exe, ShortcutFile.ReadTarget(integration.DesktopShortcut), ignoreCase: true);
        foreach (var shortcut in new[] { integration.StartMenuShortcut, integration.DesktopShortcut })
        {
            var icon = ShortcutFile.ReadIcon(shortcut)!.Value; // the icon of the program, which carries the application icon
            Assert.Equal(_box.Exe, icon.Path, ignoreCase: true);
            Assert.Equal(0, icon.Index);
        }
        using var verb = _box.Key.OpenSubKey(@"Classes\*\shell\Textida")!;
        Assert.Equal("Open with Textida", verb.GetValue(""));
        using var command = verb.OpenSubKey("command")!;
        Assert.Equal($"\"{_box.Exe}\" \"%1\"", command.GetValue(""));
    }

    [Fact]
    public void Switched_off_options_leave_nothing_behind()
    {
        var before = _box.ClassesSnapshot();
        Install(options: _box.Options(startMenu: false, desktop: false, menu: false, extensions: [], setupExe: _box.FakeSetup()));

        Assert.Equal(before, _box.ClassesSnapshot());
        Assert.False(Directory.Exists(Path.Combine(_box.Root, "start")));
        Assert.False(Directory.Exists(Path.Combine(_box.Root, "desktop")));
    }

    [Fact]
    public void The_progress_runs_through_the_stages_to_the_end()
    {
        var seen = new List<(SetupStage Stage, double Fraction)>();
        using var package = Sandbox.Package();
        _box.Installer((stage, fraction) => seen.Add((stage, fraction))).Install(package, _box.Options());

        Assert.Equal(SetupStage.Preparing, seen[0].Stage);
        Assert.Contains(seen, s => s.Stage == SetupStage.Files && s.Fraction >= 1);
        Assert.Equal((SetupStage.Finishing, 1.0), seen[^1]);
        Assert.Equal(seen.Select(s => s.Stage).Distinct().Order(), seen.Select(s => s.Stage).Distinct()); // never goes back to an earlier stage
        Assert.All(seen, s => Assert.InRange(s.Fraction, 0, 1));
    }

    [Fact]
    public void Install_and_uninstall_leave_the_keys_of_other_programs_as_they_were()
    {
        using (var other = _box.Key.CreateSubKey(@"Classes\*\shell\PSPad\command")) other.SetValue("", @"C:\pspad\PSPad.exe ""%1""");
        using (var other = _box.Key.CreateSubKey(@"Classes\.txt\OpenWithProgids")) other.SetValue("OtherApp.txt", Array.Empty<byte>(), RegistryValueKind.None);
        var before = _box.ClassesSnapshot();

        Install(options: _box.Options(menu: true, extensions: [".txt", ".log"], setupExe: _box.FakeSetup()));
        Assert.NotEqual(before, _box.ClassesSnapshot());
        _box.Installer().Uninstall(_box.Dir, deleteUserData: false);

        Assert.Equal(before, _box.ClassesSnapshot());
    }

    // ---- refusals and failures ----

    [Fact]
    public void A_folder_with_other_files_is_refused_and_not_touched()
    {
        _box.Add("other.txt", "keep");

        Assert.Equal(SetupError.UnknownDir, Refused(() => Install()));

        Assert.Equal(["other.txt"], Directory.GetFileSystemEntries(_box.Dir).Select(Path.GetFileName));
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void A_running_program_stops_the_install_before_anything_changes()
    {
        Install();
        _box.Running = true;
        _box.Add("marker.txt", "x");

        Assert.Equal(SetupError.Running, Refused(() => Install(version: "2.0.0", content: "new")));

        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.False(Directory.Exists(_box.Dir + ".new"));
        Assert.False(Directory.Exists(_box.Dir + ".previous"));
    }

    [Fact]
    public void A_package_without_the_program_or_a_damaged_one_is_refused_and_creates_nothing()
    {
        using var empty = Sandbox.Zip(("version.txt", "1"));
        Assert.Equal(SetupError.BadPackage, Refused(() => _box.Installer().Install(empty, _box.Options())));
        using var damaged = new MemoryStream("this is not a zip"u8.ToArray());
        Assert.Equal(SetupError.BadPackage, Refused(() => _box.Installer().Install(damaged, _box.Options())));

        Assert.False(Directory.Exists(_box.Dir));
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void An_entry_that_leaves_the_install_folder_is_refused_and_the_half_made_folder_is_removed()
    {
        using var package = Sandbox.Zip(("Textida.exe", "x"), ("..\\..\\escaped.txt", "evil"));

        Assert.Equal(SetupError.BadPackage, Refused(() => _box.Installer().Install(package, _box.Options())));

        Assert.False(Directory.Exists(_box.Dir));
        Assert.False(File.Exists(Path.Combine(_box.Root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_box.Root)!, "escaped.txt")));
    }

    [Fact]
    public void An_existing_empty_folder_is_used_and_stays_when_the_install_fails()
    {
        Directory.CreateDirectory(_box.Dir);
        using var package = Sandbox.Zip(("Textida.exe", "x"), ("..\\escaped.txt", "evil"));

        Assert.Equal(SetupError.BadPackage, Refused(() => _box.Installer().Install(package, _box.Options())));

        Assert.True(Directory.Exists(_box.Dir));
        Assert.Empty(Directory.GetFileSystemEntries(_box.Dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative\\folder")]
    [InlineData("C:\\")]
    [InlineData("C:")]
    [InlineData("\\\\server\\share")]
    public void An_install_folder_must_be_a_full_path_and_not_a_drive_root(string dir)
    {
        Assert.Equal(SetupError.InvalidDir, Refused(() => Installer.NormalizeDir(dir)));
    }

    [Fact]
    public void An_install_folder_is_taken_as_typed_with_variables_quotes_and_trailing_slash_resolved()
    {
        Assert.Equal(Path.Combine(Path.GetTempPath().TrimEnd('\\'), "My Textida"), Installer.NormalizeDir("\"%TEMP%\\My Textida\\\""));
    }

    [Fact]
    public void A_failure_in_the_registry_after_a_first_install_takes_everything_back()
    {
        var failing = _box.Locations with { OpenClasses = () => throw new IOException("denied") };
        using var package = Sandbox.Package();

        var error = Assert.Throws<SetupException>(() => new Installer(failing) { IsRunning = _ => false }.Install(package, _box.Options(setupExe: _box.FakeSetup())));

        Assert.Equal(SetupError.Failed, error.Error);
        Assert.Contains("denied", error.Detail);
        Assert.False(Directory.Exists(_box.Dir));
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void A_shortcut_that_cannot_be_written_is_a_warning_not_a_failure()
    {
        var blocker = Path.Combine(_box.Root, "blocker");
        File.WriteAllText(blocker, "a file where a folder should be");
        var locations = _box.Locations with { StartMenuDir = Path.Combine(blocker, "start") };
        using var package = Sandbox.Package();

        var result = new Installer(locations) { IsRunning = _ => false }.Install(package, _box.Options(startMenu: true));

        Assert.Single(result.Warnings);
        Assert.True(File.Exists(_box.Exe));
        Assert.NotNull(_box.UninstallEntry());
    }

    // ---- update and rollback ----

    [Fact]
    public void An_install_over_an_existing_one_is_an_update_that_keeps_the_old_version_the_profile_and_the_uninstaller()
    {
        Install(version: "1.0.0", content: "old");
        _box.Add(Path.Combine("profile", "settings.json"), "mine");
        _box.Add("textida.portable", "");

        var result = Install(version: "1.1.0", content: "new");

        Assert.True(result.Updated);
        Assert.Equal(_box.Dir + ".previous", result.PreviousDir);
        Assert.Equal("program new", _box.Read("Textida.exe"));
        Assert.Equal("mine", _box.Read(Path.Combine("profile", "settings.json")));
        Assert.True(File.Exists(Path.Combine(_box.Dir, "textida.portable")));
        Assert.True(File.Exists(Path.Combine(_box.Dir, "TextidaSetup.exe")));
        Assert.Equal("program old", File.ReadAllText(Path.Combine(_box.Dir + ".previous", "Textida.exe")));
        Assert.False(Directory.Exists(_box.Dir + ".new"));
        Assert.False(Directory.Exists(_box.Dir + ".aside"));
        using var entry = _box.UninstallEntry()!;
        Assert.Equal("1.1.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public void An_update_drops_files_the_new_version_no_longer_has()
    {
        Install(extra: ("old-only.txt", "x"));
        Install(version: "1.1.0", content: "new");
        Assert.False(File.Exists(Path.Combine(_box.Dir, "old-only.txt")));
        Assert.True(File.Exists(Path.Combine(_box.Dir + ".previous", "old-only.txt")));
    }

    [Fact]
    public void An_update_applies_the_changed_options_exactly()
    {
        Install(options: _box.Options(menu: true, extensions: [".log"], startMenu: true, setupExe: _box.FakeSetup()));

        Install(version: "1.1.0", options: _box.Options(menu: false, extensions: [".md"], startMenu: false, desktop: true, setupExe: _box.FakeSetup()));

        var integration = new SystemIntegration(_box.Locations, _box.Exe);
        Assert.False(integration.HasMenu());
        Assert.Equal([".md"], integration.AssociatedExtensions());
        Assert.False(integration.HasStartMenuShortcut());
        Assert.True(integration.HasDesktopShortcut());
    }

    [Fact]
    public void A_failure_after_the_switch_puts_the_old_version_back()
    {
        Install(version: "1.0.0", content: "old");
        var failing = _box.Locations with { OpenClasses = () => throw new IOException("denied") };
        using var package = Sandbox.Package("1.1.0", "new");

        Assert.Equal(SetupError.Failed, Refused(() => new Installer(failing) { IsRunning = _ => false }.Install(package, _box.Options())));

        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Equal("1.0.0", _box.Read("version.txt"));
        using var entry = _box.UninstallEntry()!;
        Assert.Equal("1.0.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public void Rollback_swaps_with_the_previous_version_and_back_again_and_keeps_the_profile()
    {
        Install(version: "1.0.0", content: "old");
        Install(version: "1.1.0", content: "new");
        _box.Add(Path.Combine("profile", "settings.json"), "changed after the update");
        var installer = _box.Installer();

        Assert.Equal("1.0.0", installer.Rollback(_box.Dir));
        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Equal("changed after the update", _box.Read(Path.Combine("profile", "settings.json")));
        Assert.Equal("program new", File.ReadAllText(Path.Combine(_box.Dir + ".previous", "Textida.exe")));
        using (var entry = _box.UninstallEntry()!) Assert.Equal("1.0.0", entry.GetValue("DisplayVersion"));

        Assert.Equal("1.1.0", installer.Rollback(_box.Dir));
        Assert.Equal("program new", _box.Read("Textida.exe"));
    }

    [Fact]
    public void Rollback_without_a_previous_version_and_an_update_from_a_broken_folder_change_nothing()
    {
        Install();
        var installer = _box.Installer();
        Assert.Equal(SetupError.NoPrevious, Refused(() => installer.Rollback(_box.Dir)));

        var broken = Path.Combine(_box.Root, "staged");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "readme.txt"), "no program in here");
        Assert.Equal(SetupError.BadStaging, Refused(() => installer.ApplyUpdate(_box.Dir, broken)));

        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.True(File.Exists(Path.Combine(broken, "readme.txt")));
    }

    [Fact]
    public void A_staged_folder_is_switched_in_the_way_the_updater_does_it()
    {
        Install(version: "1.0.0", content: "old");
        var staged = _box.Dir + ".new";
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "Textida.exe"), "program staged");
        File.WriteAllText(Path.Combine(staged, "version.txt"), "3.0.0");

        Assert.Equal("3.0.0", _box.Installer().ApplyUpdate(_box.Dir, staged));

        Assert.Equal("program staged", _box.Read("Textida.exe"));
        Assert.True(File.Exists(Path.Combine(_box.Dir, "TextidaSetup.exe"))); // carried over because the staged package has none
        Assert.Equal("program old", File.ReadAllText(Path.Combine(_box.Dir + ".previous", "Textida.exe")));
    }

    [Fact]
    public void A_crash_between_the_two_moves_is_repaired_by_the_next_switch()
    {
        Install(version: "1.0.0", content: "old");
        Directory.Move(_box.Dir, _box.Dir + ".aside"); // what a crash after the first move leaves
        var staged = _box.Dir + ".new";
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "Textida.exe"), "program staged");

        _box.Installer().ApplyUpdate(_box.Dir, staged);

        Assert.Equal("program staged", _box.Read("Textida.exe"));
        Assert.Equal("program old", File.ReadAllText(Path.Combine(_box.Dir + ".previous", "Textida.exe")));
    }

    // ---- uninstall ----

    [Fact]
    public void Uninstall_removes_files_entries_shortcuts_and_the_apps_list_key_but_keeps_the_settings()
    {
        Install(options: _box.Options(startMenu: true, desktop: true, menu: true, extensions: [".log"], setupExe: _box.FakeSetup()));
        Install(version: "1.1.0", content: "new", options: _box.Options(startMenu: true, desktop: true, menu: true, extensions: [".log"], setupExe: _box.FakeSetup()));
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");
        var before = _box.ClassesSnapshot();

        _box.Installer().Uninstall(_box.Dir, deleteUserData: false);

        Assert.False(Directory.Exists(_box.Dir));
        Assert.False(Directory.Exists(_box.Dir + ".previous"));
        Assert.Null(_box.UninstallEntry());
        Assert.False(File.Exists(Path.Combine(_box.Root, "start", "Textida.lnk")));
        Assert.False(File.Exists(Path.Combine(_box.Root, "desktop", "Textida.lnk")));
        Assert.NotEqual(before, _box.ClassesSnapshot());
        Assert.Empty(_box.ClassesSnapshot());
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json")));
    }

    [Fact]
    public void Uninstall_keeps_a_portable_profile_unless_the_user_asks_to_delete_it()
    {
        Install();
        _box.Add(Path.Combine("profile", "settings.json"), "mine");
        _box.Add("textida.portable", "");
        _box.Installer().Uninstall(_box.Dir, deleteUserData: false);

        Assert.Equal(["profile", "textida.portable"], Directory.GetFileSystemEntries(_box.Dir).Select(Path.GetFileName).Order());
        Assert.Equal("mine", _box.Read(Path.Combine("profile", "settings.json")));
        Assert.Equal(Path.Combine(_box.Dir, "profile"), _box.Installer().UserDataLocation(_box.Dir));
    }

    [Fact]
    public void Uninstall_can_delete_the_settings_too()
    {
        Install();
        _box.Add(Path.Combine("profile", "settings.json"), "mine");
        _box.Add("textida.portable", "");
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");

        _box.Installer().Uninstall(_box.Dir, deleteUserData: true);

        Assert.False(Directory.Exists(_box.Dir));
        Assert.False(Directory.Exists(_box.Locations.UserDataDir));
    }

    [Fact]
    public void Uninstall_leaves_a_shortcut_that_points_at_another_copy_of_textida()
    {
        Install(options: _box.Options(startMenu: false));
        var other = Path.Combine(_box.Root, "other", "Textida.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "another copy");
        var shortcut = Path.Combine(_box.Root, "desktop", "Textida.lnk");
        ShortcutFile.Create(shortcut, other, Path.GetDirectoryName(other)!, "Textida");

        _box.Installer().Uninstall(_box.Dir, deleteUserData: false);

        Assert.True(File.Exists(shortcut));
    }

    [Fact]
    public void The_running_setup_is_left_in_the_folder_for_the_caller_to_remove()
    {
        var setup = _box.FakeSetup();
        Install(options: _box.Options(setupExe: setup));
        var uninstaller = Path.Combine(_box.Dir, "TextidaSetup.exe");

        _box.Installer().Uninstall(_box.Dir, deleteUserData: false, runningProgram: uninstaller);

        Assert.Equal(["TextidaSetup.exe"], Directory.GetFileSystemEntries(_box.Dir).Select(Path.GetFileName));
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void Uninstall_of_a_missing_install_and_of_a_running_one_change_nothing()
    {
        Assert.Equal(SetupError.NotInstalled, Refused(() => _box.Installer().Uninstall(_box.Dir, false)));

        Install();
        _box.Running = true;
        Assert.Equal(SetupError.Running, Refused(() => _box.Installer().Uninstall(_box.Dir, false)));
        Assert.True(File.Exists(_box.Exe));
        Assert.NotNull(_box.UninstallEntry());
    }

    // ---- finding the install ----

    [Fact]
    public void The_installed_product_is_found_by_the_apps_list_then_by_the_default_folder()
    {
        var installer = _box.Installer();
        Assert.Null(installer.FindInstalled());

        Directory.CreateDirectory(_box.Locations.DefaultInstallDir);
        File.WriteAllText(Path.Combine(_box.Locations.DefaultInstallDir, "Textida.exe"), "x");
        File.WriteAllText(Path.Combine(_box.Locations.DefaultInstallDir, "version.txt"), "0.9.0");
        Assert.Equal(new InstalledProduct(_box.Locations.DefaultInstallDir, "0.9.0"), installer.FindInstalled());

        Install(version: "2.0.0");
        Assert.Equal(new InstalledProduct(_box.Dir, "2.0.0"), installer.FindInstalled());
    }
}

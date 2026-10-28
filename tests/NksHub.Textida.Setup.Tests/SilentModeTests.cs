using Microsoft.Win32;
using NksHub.Textida.Install;
using NksHub.Textida.Setup;

namespace NksHub.Textida.Setup.Tests;

/// <summary>
/// The silent mode and the switch of an update, run through the same entry point as the program (<see cref="SetupRunner"/>) against a sandbox:
/// a temp folder for the program and a registry key of the test. The exit code and the sentences are what a script sees.
/// </summary>
public class SilentModeTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly Recorder _recorder = new();
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public void Dispose() => _box.Dispose();

    private int Run(string[] args, Func<Stream?>? package = null, string language = "en", string? setupExe = null) =>
        SetupRunner.Run(SetupCommand.Parse(args), _box.Env(_recorder, language, package, setupExe), _out, _err);

    private SystemIntegration Integration => new(_box.Locations, _box.Exe);

    [Fact]
    public void A_silent_install_does_what_the_line_says_and_reports_it()
    {
        var code = Run(["--silent", "--dir", _box.Dir, "--no-menu", "--desktop", "--assoc", ".log,.md"]);

        Assert.Equal(0, code);
        Assert.Contains("is installed in " + _box.Dir, _out.ToString());
        Assert.Equal("", _err.ToString());
        Assert.True(File.Exists(_box.Exe));
        Assert.False(File.Exists(Path.Combine(_box.Dir, "Install.cmd")));
        Assert.True(File.Exists(Path.Combine(_box.Dir, "TextidaSetup.exe")));
        Assert.False(Integration.HasMenu());
        Assert.True(Integration.HasStartMenuShortcut());
        Assert.True(Integration.HasDesktopShortcut());
        Assert.Equal([".log", ".md"], Integration.AssociatedExtensions());
        Assert.NotNull(_box.UninstallEntry());
        Assert.Empty(_recorder.Launched);
    }

    [Fact]
    public void Without_choices_a_first_install_takes_the_defaults_and_does_not_start_the_program()
    {
        Assert.Equal(0, Run(["--silent", "--dir", _box.Dir]));

        Assert.True(Integration.HasMenu());
        Assert.True(Integration.HasStartMenuShortcut());
        Assert.False(Integration.HasDesktopShortcut());
        Assert.Empty(Integration.AssociatedExtensions());
        Assert.Empty(_recorder.Launched);
    }

    [Fact]
    public void The_folder_defaults_to_the_one_of_the_system_and_launch_starts_the_program()
    {
        Assert.Equal(0, Run(["--silent", "--launch"]));

        var exe = Path.Combine(_box.Locations.DefaultInstallDir, "Textida.exe");
        Assert.True(File.Exists(exe));
        Assert.Equal([(exe, _box.Locations.DefaultInstallDir)], _recorder.Launched);
    }

    [Fact]
    public void An_update_keeps_every_choice_the_line_does_not_name()
    {
        Run(["--silent", "--dir", _box.Dir, "--menu", "--desktop", "--assoc", ".log"]);

        var code = Run(["--silent", "--no-menu"], package: () => Sandbox.Package("2.0.0", "new"));

        Assert.Equal(0, code);
        Assert.Contains("updated to 2.0.0", _out.ToString());
        Assert.Equal("program new", _box.Read("Textida.exe"));
        Assert.False(Integration.HasMenu());
        Assert.True(Integration.HasDesktopShortcut());
        Assert.True(Integration.HasStartMenuShortcut());
        Assert.Equal([".log"], Integration.AssociatedExtensions());
        using var entry = _box.UninstallEntry()!;
        Assert.Equal("2.0.0", entry.GetValue("DisplayVersion"));
    }

    [Theory]
    [InlineData("cs", "je nainstalovaná")]
    [InlineData("sk", "je nainštalovaná")]
    [InlineData("de", "ist in")]
    [InlineData("en", "is installed")]
    public void The_sentences_come_in_the_language_asked_for_or_the_one_of_the_system(string language, string expected)
    {
        Assert.Equal(0, Run(["--silent", "--dir", _box.Dir, "--lang", language], language: "en"));
        Assert.Contains(expected, _out.ToString());

        _out.GetStringBuilder().Clear();
        Run(["--silent", "--dir", Path.Combine(_box.Root, "second")], language: language);
        Assert.Contains(expected, _out.ToString());
    }

    [Fact]
    public void The_menu_entry_is_written_in_the_language_of_the_setup()
    {
        Run(["--silent", "--dir", _box.Dir, "--menu"], language: "de");
        using var verb = _box.Key.OpenSubKey(@"Classes\*\shell\Textida")!;
        Assert.Equal("Mit Textida öffnen", verb.GetValue(""));
    }

    [Fact]
    public void A_folder_with_other_files_fails_with_exit_code_1_and_changes_nothing()
    {
        _box.Add("other.txt", "keep");

        var code = Run(["--silent", "--dir", _box.Dir]);

        Assert.Equal(1, code);
        Assert.Contains("is not a Textida folder", _err.ToString());
        Assert.Equal("", _out.ToString());
        Assert.Equal(["other.txt"], Directory.GetFileSystemEntries(_box.Dir).Select(Path.GetFileName));
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void A_running_program_and_a_bad_folder_and_a_missing_package_each_fail_with_their_own_sentence()
    {
        Run(["--silent", "--dir", _box.Dir]);
        _box.Running = true;
        _err.GetStringBuilder().Clear();
        Assert.Equal(1, Run(["--silent"], package: () => Sandbox.Package("2.0.0")));
        Assert.Contains("Textida is running", _err.ToString());

        _err.GetStringBuilder().Clear();
        Assert.Equal(1, Run(["--silent", "--dir", @"C:\"]));
        Assert.Contains("cannot be used as an install folder", _err.ToString());

        _err.GetStringBuilder().Clear();
        Assert.Equal(1, Run(["--silent", "--dir", Path.Combine(_box.Root, "x")], package: () => null));
        Assert.Contains("carries no program files", _err.ToString());
        Assert.False(Directory.Exists(Path.Combine(_box.Root, "x")));
    }

    [Fact]
    public void A_wrong_package_is_a_failure_with_the_sentence_for_it()
    {
        var code = Run(["--silent", "--dir", _box.Dir], package: () => Sandbox.Zip(("version.txt", "1")));
        Assert.Equal(1, code);
        Assert.Contains("no complete Textida package", _err.ToString());
        Assert.False(Directory.Exists(_box.Dir));
    }

    // ---- uninstall ----

    [Fact]
    public void A_silent_uninstall_removes_everything_it_added_and_keeps_the_settings()
    {
        Run(["--silent", "--dir", _box.Dir, "--desktop", "--assoc", ".log"]);
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");
        _out.GetStringBuilder().Clear();

        var code = Run(["--uninstall", "--silent", "--dir", _box.Dir]);

        Assert.Equal(0, code);
        Assert.Contains("settings in " + _box.Locations.UserDataDir + " were kept", _out.ToString());
        Assert.False(Directory.Exists(_box.Dir));
        Assert.Null(_box.UninstallEntry());
        Assert.Empty(_box.ClassesSnapshot());
        Assert.False(Integration.HasStartMenuShortcut());
        Assert.False(Integration.HasDesktopShortcut());
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json")));
    }

    [Fact]
    public void A_silent_uninstall_can_delete_the_settings_too()
    {
        Run(["--silent", "--dir", _box.Dir]);
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");

        Assert.Equal(0, Run(["--uninstall", "--silent", "--delete-settings", "--dir", _box.Dir]));

        Assert.False(Directory.Exists(_box.Locations.UserDataDir));
        Assert.Contains("settings were removed", _out.ToString());
    }

    [Fact]
    public void The_uninstaller_in_the_install_folder_finds_the_folder_itself_and_schedules_its_own_removal()
    {
        Run(["--silent", "--dir", _box.Dir]);
        var uninstaller = Path.Combine(_box.Dir, "TextidaSetup.exe");

        var code = Run(["--uninstall", "--silent"], setupExe: uninstaller, package: () => null);

        Assert.Equal(0, code);
        Assert.Equal([(uninstaller, _box.Dir)], _recorder.Removed);
        Assert.Equal(["TextidaSetup.exe"], Directory.GetFileSystemEntries(_box.Dir).Select(Path.GetFileName)); // only the running program is left, for the scheduled removal
        Assert.Null(_box.UninstallEntry());
    }

    [Fact]
    public void A_copy_of_the_setup_in_a_temporary_folder_removes_itself_after_its_work()
    {
        Assert.Equal(0, Run(["--check", "--temp-copy"]));

        var self = Path.Combine(_box.Root, "TextidaSetup.exe");
        Assert.Equal([(self, _box.Root)], _recorder.Removed);
    }

    [Fact]
    public void Uninstalling_what_is_not_installed_fails_without_changes()
    {
        Assert.Equal(1, Run(["--uninstall", "--silent", "--dir", _box.Dir]));
        Assert.Contains("is not installed", _err.ToString());
        Assert.Empty(_recorder.Removed);
    }

    // ---- update and rollback as the editor starts them ----

    [Fact]
    public void An_update_is_switched_after_the_editor_ended_and_the_editor_is_started_again()
    {
        Run(["--silent", "--dir", _box.Dir]);
        var staged = _box.Dir + ".new";
        Directory.CreateDirectory(staged);
        File.WriteAllText(Path.Combine(staged, "Textida.exe"), "program staged");
        File.WriteAllText(Path.Combine(staged, "version.txt"), "3.0.0");
        _out.GetStringBuilder().Clear();

        var code = Run(["--apply-update", staged, "--dir", _box.Dir, "--wait-pid", "4242", "--restart"], package: () => null);

        Assert.Equal(0, code);
        Assert.Equal([4242], _recorder.Waited);
        Assert.Equal("program staged", _box.Read("Textida.exe"));
        Assert.Contains("Textida 3.0.0 is now in " + _box.Dir, _out.ToString());
        Assert.Equal([(_box.Exe, _box.Dir)], _recorder.Launched);
        using var entry = _box.UninstallEntry()!;
        Assert.Equal("3.0.0", entry.GetValue("DisplayVersion"));
    }

    [Fact]
    public void A_rollback_swaps_back_and_a_failed_switch_still_starts_the_old_editor()
    {
        Run(["--silent", "--dir", _box.Dir]);
        Run(["--silent"], package: () => Sandbox.Package("2.0.0", "new"));
        Assert.Equal("program new", _box.Read("Textida.exe"));

        Assert.Equal(0, Run(["--rollback", "--dir", _box.Dir, "--restart"], package: () => null));
        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Single(_recorder.Launched);

        _err.GetStringBuilder().Clear();
        Directory.Delete(_box.Dir + ".previous", recursive: true);
        Assert.Equal(1, Run(["--rollback", "--dir", _box.Dir, "--restart"], package: () => null));
        Assert.Contains("no previous version", _err.ToString());
        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Equal(2, _recorder.Launched.Count);
    }

    [Fact]
    public void An_update_from_an_unfinished_folder_fails_and_leaves_the_install_alone()
    {
        Run(["--silent", "--dir", _box.Dir]);
        var staged = Path.Combine(_box.Root, "half");
        Directory.CreateDirectory(staged);

        Assert.Equal(1, Run(["--apply-update", staged, "--dir", _box.Dir], package: () => null));

        Assert.Contains("is not a complete Textida package", _err.ToString());
        Assert.Equal("program old", _box.Read("Textida.exe"));
        Assert.Empty(_recorder.Launched);
    }

    // ---- check and help ----

    [Fact]
    public void Check_reports_the_package_and_fails_when_there_is_none()
    {
        Assert.Equal(0, Run(["--check"], package: () => Sandbox.Package("4.5.6")));
        Assert.Contains("package 4.5.6", _out.ToString());

        Assert.Equal(1, Run(["--check"], package: () => null));
        Assert.Contains("carries no program files", _err.ToString());
    }

    [Fact]
    public void Help_prints_the_usage()
    {
        Assert.Equal(0, Run(["--help"]));
        Assert.Contains("--silent", _out.ToString());
        Assert.Contains("--no-menu", _out.ToString());
    }
}

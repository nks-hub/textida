using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.Install;
using NksHub.Textida.Setup;

namespace NksHub.Textida.Setup.Tests;

/// <summary>The pages of the setup window and its options, over a sandbox: nothing real is read or written, no program is started.</summary>
public class SetupWindowTests : IDisposable
{
    private readonly Sandbox _box = new();
    private readonly Recorder _recorder = new();
    private SetupWindow? _window;

    public void Dispose()
    {
        _window?.Close();
        _box.Dispose();
    }

    private SetupWindow Open(string language = "en", Func<Stream?>? package = null, bool uninstall = false, string? setupExe = null, Action<SetupModel>? prepare = null)
    {
        var model = new SetupModel(_box.Env(_recorder, language, package, setupExe));
        model.Dir = _box.Dir;
        prepare?.Invoke(model);
        _window = new SetupWindow(model, uninstall);
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        return _window;
    }

    private static T Get<T>(SetupWindow window, string id) where T : Control
    {
        window.UpdateLayout(); // the template of a scroll viewer, and so its content, exists after the first layout pass
        return window.Find<T>(id) ?? throw new InvalidOperationException("no control " + id + " on page " + window.Page);
    }

    private static IEnumerable<string> Texts(SetupWindow window)
    {
        window.UpdateLayout();
        return window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task Finish(SetupWindow window)
    {
        await Until(() => window.Work.IsCompleted, "the work to end");
        Dispatcher.UIThread.RunJobs();
    }

    private SetupWindow ToOptions(string language = "en", Func<Stream?>? package = null, Action<SetupModel>? prepare = null)
    {
        var window = Open(language, package, prepare: prepare);
        window.Accepted = true;
        window.Primary();
        Assert.Equal(SetupPage.Options, window.Page);
        return window;
    }

    // ---- welcome and licence ----

    [AvaloniaFact]
    public void The_first_page_greets_with_the_version_and_shows_the_licence_of_the_repository()
    {
        var window = Open();

        Assert.Equal(SetupPage.Welcome, window.Page);
        Assert.Equal("Textida Setup", window.Title);
        Assert.Contains(Texts(window), t => t.Contains("installs Textida 1.0.0 for your user account") && t.Contains("no administrator rights"));
        var licence = Get<TextBox>(window, "licence");
        Assert.True(licence.IsReadOnly);
        Assert.Equal(File.ReadAllText(Path.Combine(Repo.Root, "LICENSE")).Trim().ReplaceLineEndings("\n"), licence.Text!.ReplaceLineEndings("\n"));
        Assert.StartsWith("MIT License", licence.Text);
    }

    [AvaloniaFact]
    public void Next_waits_for_the_licence_to_be_accepted()
    {
        var window = Open();
        Assert.False(Get<Button>(window, "primary").IsEnabled);
        window.Primary();
        Assert.Equal(SetupPage.Welcome, window.Page);

        Get<CheckBox>(window, "accept").IsChecked = true;

        Assert.True(Get<Button>(window, "primary").IsEnabled);
        window.Primary();
        Assert.Equal(SetupPage.Options, window.Page);
    }

    [AvaloniaFact]
    public void The_window_opens_in_the_language_of_the_system_and_the_user_can_switch()
    {
        var window = Open("sk");
        Assert.Equal("Inštalácia Textidy", window.Title);
        Assert.Contains(Texts(window), t => t == "Licencia");
        Assert.Equal(["Čeština", "English", "Slovenčina", "Deutsch"], Get<ComboBox>(window, "language").Items.Cast<string>());

        window.SelectLanguage("de");
        Assert.Equal("Textida-Setup", window.Title);
        Assert.Contains(Texts(window), t => t == "Lizenz");
        Assert.Equal("Weiter", Get<Button>(window, "primary").Content);

        window.SelectLanguage("cs");
        Assert.Equal("Další", Get<Button>(window, "primary").Content);
    }

    [AvaloniaFact]
    public void The_chosen_language_survives_the_next_pages_and_is_used_for_the_menu_entry()
    {
        var window = Open("en");
        window.SelectLanguage("cs");
        window.Accepted = true;
        window.Primary();
        Assert.Contains(Texts(window), t => t == "Volby instalace");
        Assert.Contains(Texts(window), t => t.StartsWith("Nabízet Textidu"));
        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => (string?)c.Content == "Položka „Otevřít v Textidě“ v místní nabídce Průzkumníka");
    }

    [AvaloniaFact]
    public void A_setup_without_a_package_says_so_and_offers_only_to_close()
    {
        var window = Open(package: () => null);

        Assert.Contains(Texts(window), t => t.Contains("carries no program files"));
        Assert.Null(window.Find<CheckBox>("accept"));
        Assert.Null(window.Find<Button>("primary"));
        Assert.NotNull(window.Find<Button>("cancel"));
    }

    [AvaloniaFact]
    public void The_window_carries_the_application_icon()
    {
        Assert.NotNull(Open().Icon);
    }

    // ---- options ----

    [AvaloniaFact]
    public void The_options_start_with_the_defaults_of_a_first_install()
    {
        var window = ToOptions();

        Assert.Equal(_box.Dir, Get<TextBox>(window, "dir").Text);
        Assert.True(Get<CheckBox>(window, "startMenu").IsChecked);
        Assert.False(Get<CheckBox>(window, "desktop").IsChecked);
        Assert.True(Get<CheckBox>(window, "menu").IsChecked);
        Assert.True(Get<CheckBox>(window, "launch").IsChecked);
        Assert.All(ShellIntegration.Extensions, e => Assert.False(Get<CheckBox>(window, "ext" + e).IsChecked));
        Assert.Equal("Install", Get<Button>(window, "primary").Content);
        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), c => (string?)c.Content == "Entry “Open with Textida” in the Explorer context menu");
    }

    [AvaloniaFact]
    public void The_default_folder_is_the_one_of_the_system_and_back_returns_to_the_licence()
    {
        var window = Open(prepare: m => m.Dir = _box.Locations.DefaultInstallDir);
        window.Accepted = true;
        window.Primary();
        Assert.Equal(_box.Locations.DefaultInstallDir, Get<TextBox>(window, "dir").Text);

        window.Back();
        Assert.Equal(SetupPage.Welcome, window.Page);
        Assert.True(Get<CheckBox>(window, "accept").IsChecked);
    }

    [AvaloniaFact]
    public void A_folder_that_cannot_be_used_is_explained_and_blocks_the_install()
    {
        var window = ToOptions();
        var dir = Get<TextBox>(window, "dir");

        dir.Text = @"C:\";
        Assert.Contains("cannot be used as an install folder", Get<TextBlock>(window, "problem").Text);
        Assert.False(Get<Button>(window, "primary").IsEnabled);
        window.Primary();
        Assert.Equal(SetupPage.Options, window.Page);

        _box.Add("other.txt", "x");
        dir.Text = _box.Dir;
        Assert.Contains("holds files that do not belong to Textida", Get<TextBlock>(window, "problem").Text);
        Assert.False(Get<Button>(window, "primary").IsEnabled);

        dir.Text = Path.Combine(_box.Root, "free");
        Assert.Equal("", Get<TextBlock>(window, "problem").Text);
        Assert.True(Get<Button>(window, "primary").IsEnabled);
    }

    [AvaloniaFact]
    public void The_file_types_are_a_checklist_with_all_and_none()
    {
        var window = ToOptions();

        Get<CheckBox>(window, "ext.log").IsChecked = true;
        Assert.Equal([".log"], window.Model.Extensions.ToArray());
        Get<Button>(window, "assocAll").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(ShellIntegration.Extensions.Order(), window.Model.Extensions.Order());
        Get<Button>(window, "assocNone").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(window.Model.Extensions);
    }

    [AvaloniaFact]
    public async Task Browse_takes_the_folder_and_makes_a_subfolder_when_the_one_chosen_holds_other_files()
    {
        var window = ToOptions();
        var parent = Path.Combine(_box.Root, "parent");
        Directory.CreateDirectory(parent);
        File.WriteAllText(Path.Combine(parent, "something.txt"), "x");
        var empty = Path.Combine(_box.Root, "empty");
        Directory.CreateDirectory(empty);

        window.FolderPicker = () => Task.FromResult<string?>(parent);
        Get<Button>(window, "browse").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => Get<TextBox>(window, "dir").Text != _box.Dir, "the folder to be taken");
        Assert.Equal(Path.Combine(parent, "Textida"), Get<TextBox>(window, "dir").Text);

        window.FolderPicker = () => Task.FromResult<string?>(empty);
        Get<Button>(window, "browse").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => Get<TextBox>(window, "dir").Text == empty, "the empty folder to be taken as it is");

        window.FolderPicker = () => Task.FromResult<string?>(null); // the dialog was cancelled
        Get<Button>(window, "browse").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(empty, Get<TextBox>(window, "dir").Text);
    }

    // ---- install, progress, done ----

    [AvaloniaFact]
    public async Task The_chosen_options_are_installed_and_the_done_page_starts_textida_on_request()
    {
        var window = ToOptions();
        Get<CheckBox>(window, "desktop").IsChecked = true;
        Get<CheckBox>(window, "menu").IsChecked = false;
        Get<CheckBox>(window, "ext.md").IsChecked = true;
        Get<CheckBox>(window, "ext.log").IsChecked = true;

        window.Primary();
        Assert.Equal(SetupPage.Progress, window.Page);
        Assert.False(Get<Button>(window, "cancel").IsEnabled);
        await Finish(window);

        Assert.Equal(SetupPage.Done, window.Page);
        Assert.Contains(Texts(window), t => t == $"Textida 1.0.0 is installed in {_box.Dir}.");
        var integration = new SystemIntegration(_box.Locations, _box.Exe);
        Assert.False(integration.HasMenu());
        Assert.True(integration.HasStartMenuShortcut());
        Assert.True(integration.HasDesktopShortcut());
        Assert.Equal([".log", ".md"], integration.AssociatedExtensions());
        Assert.True(File.Exists(Path.Combine(_box.Dir, "TextidaSetup.exe")));
        Assert.NotNull(_box.UninstallEntry());
        Assert.Empty(_recorder.Launched);

        Assert.True(Get<CheckBox>(window, "launchDone").IsChecked);
        window.Primary();
        Assert.Equal([(_box.Exe, _box.Dir)], _recorder.Launched);
    }

    [AvaloniaFact]
    public async Task Textida_is_not_started_when_the_option_is_off()
    {
        var window = ToOptions();
        Get<CheckBox>(window, "launch").IsChecked = false;
        window.Primary();
        await Finish(window);

        Assert.False(Get<CheckBox>(window, "launchDone").IsChecked);
        window.Primary();
        Assert.Empty(_recorder.Launched);
    }

    [AvaloniaFact]
    public async Task Over_an_installed_textida_the_setup_updates_and_starts_from_what_is_installed()
    {
        var first = ToOptions();
        Get<CheckBox>(first, "menu").IsChecked = false;
        Get<CheckBox>(first, "ext.json").IsChecked = true;
        first.Primary();
        await Finish(first);
        first.Close();

        var window = Open(package: () => Sandbox.Package("1.1.0", "new"));
        Assert.Contains(Texts(window), t => t.Contains("Textida 1.0.0 is installed in") && t.Contains("updates it to 1.1.0 and keeps your settings"));
        window.Accepted = true;
        window.Primary();

        Assert.Equal(_box.Dir, Get<TextBox>(window, "dir").Text);
        Assert.False(Get<CheckBox>(window, "menu").IsChecked);
        Assert.True(Get<CheckBox>(window, "ext.json").IsChecked);
        Assert.Equal("Update", Get<Button>(window, "primary").Content);

        window.Primary();
        await Finish(window);

        Assert.Equal(SetupPage.Done, window.Page);
        Assert.Contains(Texts(window), t => t.StartsWith("Textida was updated to 1.1.0") && t.Contains(_box.Dir + ".previous"));
        Assert.Equal("program new", _box.Read("Textida.exe"));
        Assert.Equal("program old", File.ReadAllText(Path.Combine(_box.Dir + ".previous", "Textida.exe")));
    }

    [AvaloniaFact]
    public async Task A_failure_shows_the_reason_and_try_again_works_once_the_cause_is_gone()
    {
        var window = ToOptions();
        _box.Running = true;
        // a running program is only checked over an existing install, so make one first
        Directory.CreateDirectory(_box.Dir);
        File.WriteAllText(_box.Exe, "program running");
        window.Primary();
        await Finish(window);

        Assert.Equal(SetupPage.Error, window.Page);
        Assert.Equal("Textida is running. Close it and try again.", Get<TextBlock>(window, "message").Text);
        Assert.Equal("Try again", Get<Button>(window, "primary").Content);
        Assert.Equal("program running", _box.Read("Textida.exe"));

        _box.Running = false;
        window.Primary();
        await Finish(window);

        Assert.Equal(SetupPage.Done, window.Page);
        Assert.Equal("program old", _box.Read("Textida.exe"));
    }

    [AvaloniaFact]
    public async Task Back_from_a_failure_returns_to_the_options_with_the_choices_kept()
    {
        var window = ToOptions();
        Get<CheckBox>(window, "desktop").IsChecked = true;
        _box.Running = true;
        Directory.CreateDirectory(_box.Dir);
        File.WriteAllText(_box.Exe, "program running");
        window.Primary();
        await Finish(window);
        Assert.Equal(SetupPage.Error, window.Page);

        window.Back();

        Assert.Equal(SetupPage.Options, window.Page);
        Assert.True(Get<CheckBox>(window, "desktop").IsChecked);
    }

    [AvaloniaFact]
    public async Task A_shortcut_that_could_not_be_made_is_listed_on_the_done_page()
    {
        File.WriteAllText(Path.Combine(_box.Root, "blocker"), "a file where a folder should be");
        var broken = _box.Locations with { StartMenuDir = Path.Combine(_box.Root, "blocker", "start") };
        var model = new SetupModel(new SetupEnvironment
        {
            Locations = broken, OpenPackage = () => Sandbox.Package(), SetupExe = _box.FakeSetup(), UiLanguage = "en", IsRunning = _ => false,
            Launch = (_, _) => { }, RemoveSelf = (_, _) => { },
        }) { Dir = _box.Dir };
        _window = new SetupWindow(model, uninstall: false);
        _window.Show();
        _window.Accepted = true;
        _window.Primary();
        _window.Primary();
        await Finish(_window);

        Assert.Equal(SetupPage.Done, _window.Page);
        Assert.Contains(Texts(_window), t => t.StartsWith("Not done:"));
        Assert.True(File.Exists(_box.Exe));
    }

    // ---- uninstall ----

    [AvaloniaFact]
    public async Task The_uninstall_asks_removes_everything_and_keeps_the_settings_unless_ticked()
    {
        var install = ToOptions();
        Get<CheckBox>(install, "desktop").IsChecked = true;
        Get<CheckBox>(install, "ext.txt").IsChecked = true;
        install.Primary();
        await Finish(install);
        install.Close();
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");
        var uninstaller = Path.Combine(_box.Dir, "TextidaSetup.exe");

        var window = Open(uninstall: true, setupExe: uninstaller, package: () => null, prepare: m => m.Dir = _box.Dir);

        Assert.Equal(SetupPage.Uninstall, window.Page);
        Assert.Contains(Texts(window), t => t == $"Textida will be removed from {_box.Dir}, together with its shortcuts and the entries it added to Windows.");
        Assert.Contains(Texts(window), t => t == $"Your settings in {_box.Locations.UserDataDir} stay.");
        Assert.False(Get<CheckBox>(window, "deleteSettings").IsChecked);
        Assert.Equal("Uninstall", Get<Button>(window, "primary").Content);

        window.Primary();
        await Finish(window);

        Assert.Equal(SetupPage.Removed, window.Page);
        Assert.Contains(Texts(window), t => t == $"Textida was removed. Your settings in {_box.Locations.UserDataDir} were kept.");
        Assert.Null(_box.UninstallEntry());
        Assert.Empty(_box.ClassesSnapshot());
        Assert.False(new SystemIntegration(_box.Locations, _box.Exe).HasDesktopShortcut());
        Assert.False(File.Exists(_box.Exe));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json")));
        Assert.Empty(_recorder.Removed); // the running setup is removed when the program ends, not while its window is still open
        Assert.Equal([(uninstaller, _box.Dir)], window.Model.Env.PendingRemoval);
        window.Model.Env.RunPendingRemoval();
        Assert.Equal([(uninstaller, _box.Dir)], _recorder.Removed);
        Assert.Empty(window.Model.Env.PendingRemoval);
    }

    [AvaloniaFact]
    public async Task Ticking_delete_settings_removes_them_too()
    {
        var install = ToOptions();
        install.Primary();
        await Finish(install);
        install.Close();
        Directory.CreateDirectory(_box.Locations.UserDataDir);
        File.WriteAllText(Path.Combine(_box.Locations.UserDataDir, "settings.json"), "mine");

        var window = Open(uninstall: true, package: () => null);
        Get<CheckBox>(window, "deleteSettings").IsChecked = true;
        window.Primary();
        await Finish(window);

        Assert.Equal(SetupPage.Removed, window.Page);
        Assert.Contains(Texts(window), t => t == "Textida and its settings were removed.");
        Assert.False(Directory.Exists(_box.Locations.UserDataDir));
        Assert.False(Directory.Exists(_box.Dir));
    }

    [AvaloniaFact]
    public async Task A_failed_uninstall_says_why_and_try_again_runs_it_again()
    {
        var install = ToOptions();
        install.Primary();
        await Finish(install);
        install.Close();
        _box.Running = true;

        var window = Open(uninstall: true, package: () => null);
        window.Primary();
        await Finish(window);
        Assert.Equal(SetupPage.Error, window.Page);
        Assert.Contains("Textida is running", Get<TextBlock>(window, "message").Text);
        Assert.Null(window.Find<Button>("back")); // there are no options to go back to

        _box.Running = false;
        window.Primary();
        await Finish(window);
        Assert.Equal(SetupPage.Removed, window.Page);
    }

    [AvaloniaFact]
    public void The_pages_follow_the_language_in_the_uninstall_too()
    {
        var window = Open("de", uninstall: true, package: () => null);
        Assert.Contains(Texts(window), t => t == "Textida entfernen");
        Assert.Equal("Deinstallieren", Get<Button>(window, "primary").Content);
        Assert.Equal("Einstellungen ebenfalls löschen", Get<CheckBox>(window, "deleteSettings").Content);
    }
}

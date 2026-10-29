using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Win32;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// The System integration section of the settings. Every write goes to <c>HKCU\Software\Textida.Tests\{guid}</c> standing in for the classes key and to folders of a temp directory
/// standing in for the Start menu and the desktop; the real ones are never opened.
/// </summary>
[Collection("LocState")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SystemIntegrationSettingsTests : IDisposable
{
    private readonly string _path = @"Software\Textida.Tests\" + Guid.NewGuid().ToString("N");
    private readonly TempDir _temp = new();
    private readonly RegistryKey? _root;
    private readonly string _exe;
    private readonly SystemLocations _locations;
    private readonly App _app;
    private SettingsWindow? _view;

    public SystemIntegrationSettingsTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry and the shortcuts exist on Windows only");
        _root = Registry.CurrentUser.CreateSubKey(_path);
        _exe = _temp.File("Textida.exe", "stand-in");
        _locations = new SystemLocations
        {
            OpenClasses = () => _root.CreateSubKey("Classes"),
            StartMenuDir = Path.Combine(_temp.Path, "start"),
            DesktopDir = Path.Combine(_temp.Path, "desktop"),
            NotifyExplorer = false,
        };
        ShellHost.LocationsOverride = _locations;
        ShellHost.ExeOverride = _exe;
        _app = TestApp.Prepare(_temp);
    }

    public void Dispose()
    {
        _view?.Close();
        TestApp.Cleanup(_app);
        ShellHost.LocationsOverride = null;
        ShellHost.ExeOverride = null;
        _app.PathOpener = null;
        Loc.SetLanguage(Loc.DefaultLanguage);
        _root?.Dispose();
        if (OperatingSystem.IsWindows()) Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    private SettingsWindow Open(string? query = "")
    {
        _app.OpenSettings(query);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _view = _app.SettingsView!;
        // without a search the page of a category is shown: the section is on the page of the System category
        if (string.IsNullOrEmpty(query)) _view.SelectCategory(NksHub.Textida.Workbench.Settings.SettingCategories.System);
        return _view;
    }

    private SystemIntegration Integration => new(_locations, _exe);

    private static List<CheckBox> Boxes(SettingsWindow view) => [.. view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<CheckBox>()];

    private static CheckBox Box(SettingsWindow view, string label) => Boxes(view).Single(b => (string?)b.Content == label);

    [AvaloniaTheory]
    [InlineData("integrace")]
    [InlineData("zástupce")]
    [InlineData("Pruzkumnik")]
    [InlineData("kontextova nabidka")]
    [InlineData(".yml")]
    [InlineData("otevřít pomocí")]
    public void The_section_is_found_by_its_title_its_keywords_and_the_extensions(string query)
    {
        Assert.Contains(SettingsWindow.SystemSectionId, Open(query).ShownIds);
    }

    [AvaloniaFact]
    public void The_section_is_not_found_by_unrelated_words_and_is_listed_without_a_query()
    {
        Assert.DoesNotContain(SettingsWindow.SystemSectionId, Open("tabSize").ShownIds);
        Assert.DoesNotContain(SettingsWindow.SystemSectionId, Open("xyzzy").ShownIds);
        Assert.Contains(SettingsWindow.SystemSectionId, Open("").ShownIds);
    }

    [AvaloniaFact]
    public void The_menu_switch_shows_the_registry_and_writes_it_at_once()
    {
        var view = Open();
        var label = Loc.Get(K.SettingsSystemMenu, ("label", Loc.Get(K.ShellMenuLabel)));
        Assert.False(Box(view, label).IsChecked);
        Assert.False(Integration.HasMenu());

        Box(view, label).IsChecked = true;
        Assert.True(Integration.HasMenu());
        using (var verb = _root!.OpenSubKey(@"Classes\*\shell\Textida")!)
        {
            Assert.Equal(Loc.Get(K.ShellMenuLabel), verb.GetValue(""));
            using var command = verb.OpenSubKey("command")!;
            Assert.Equal($"\"{_exe}\" \"%1\"", command.GetValue(""));
        }

        Box(view, label).IsChecked = false;
        Assert.False(Integration.HasMenu());
        Assert.Null(_root.OpenSubKey(@"Classes\*"));
    }

    [AvaloniaFact]
    public void The_state_is_read_from_the_registry_when_the_view_is_built_not_remembered()
    {
        var view = Open();
        var label = Loc.Get(K.SettingsSystemMenu, ("label", Loc.Get(K.ShellMenuLabel)));
        Assert.False(Box(view, label).IsChecked);

        Integration.SetMenu(true, "from the Tools menu"); // another part of the program, or the installer, changed it
        Integration.SetAssociated(".log", true);
        view.Refresh();

        Assert.True(Box(view, label).IsChecked);
        Assert.True(Box(view, ".log").IsChecked);
        Assert.False(Box(view, ".txt").IsChecked);
    }

    [AvaloniaFact]
    public void The_associations_are_a_checklist_of_the_known_extensions()
    {
        var view = Open();
        var extensions = Boxes(view).Where(b => ShellIntegration.Extensions.Contains((string?)b.Content ?? "")).ToList();
        Assert.Equal(ShellIntegration.Extensions, extensions.Select(b => (string)b.Content!));
        Assert.All(extensions, b => Assert.False(b.IsChecked));

        Box(view, ".log").IsChecked = true;
        Box(view, ".md").IsChecked = true;
        Assert.Equal([".log", ".md"], Integration.AssociatedExtensions());
        using (var progIds = _root!.OpenSubKey(@"Classes\.log\OpenWithProgids")!) Assert.NotNull(progIds.GetValue(ShellIntegration.ProgId));

        Box(view, ".log").IsChecked = false;
        Assert.Equal([".md"], Integration.AssociatedExtensions());
        Box(view, ".md").IsChecked = false;
        Assert.Null(_root.OpenSubKey(@"Classes\" + ShellIntegration.ProgId));
    }

    [AvaloniaFact]
    public void The_shortcuts_are_created_for_this_program_and_removed_again()
    {
        var view = Open();
        var start = Loc.Get(K.SettingsSystemStartMenu);
        var desktop = Loc.Get(K.SettingsSystemDesktop);
        Assert.False(Box(view, start).IsChecked);

        Box(view, start).IsChecked = true;
        Box(view, desktop).IsChecked = true;
        Assert.Equal(_exe, ShortcutFile.ReadTarget(Path.Combine(_temp.Path, "start", "Textida.lnk")), ignoreCase: true);
        Assert.Equal(_exe, ShortcutFile.ReadTarget(Path.Combine(_temp.Path, "desktop", "Textida.lnk")), ignoreCase: true);

        Box(view, start).IsChecked = false;
        Assert.False(File.Exists(Path.Combine(_temp.Path, "start", "Textida.lnk")));
        Assert.True(File.Exists(Path.Combine(_temp.Path, "desktop", "Textida.lnk")));
    }

    [AvaloniaFact]
    public void A_refused_write_puts_the_switch_back_and_says_why()
    {
        var blocker = _temp.File("not-a-folder");
        var broken = _locations with { StartMenuDir = Path.Combine(blocker, "start") }; // a folder cannot be made below a file
        ShellHost.LocationsOverride = broken;
        var view = Open();
        var start = Loc.Get(K.SettingsSystemStartMenu);

        Box(view, start).IsChecked = true;

        Assert.False(Box(view, start).IsChecked);
        var texts = view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.StartsWith("Integrace do Průzkumníka se nezdařila", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_button_opens_the_default_apps_page_of_windows()
    {
        var opened = new List<string>();
        _app.PathOpener = path => { opened.Add(path); return Task.FromResult(true); };
        var view = Open();

        var button = view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<Button>().Single(b => b.GetType() == typeof(Button)); // a check box is a Button too
        Assert.Equal(Loc.Get(K.SettingsSystemDefaultButton), button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(["ms-settings:defaultapps"], opened);
        Assert.Contains(view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.SettingsSystemDefaultNote));
    }

    [AvaloniaFact]
    public void The_texts_follow_the_language()
    {
        var view = Open();
        _app.SetLanguage("en");
        Assert.Contains(view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "System integration");
        Assert.Contains(SettingsWindow.SystemSectionId, Open("shortcut").ShownIds);
        view.SetQuery("");
        _app.SetLanguage("de");
        Assert.Contains(view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<CheckBox>(), b => (string?)b.Content == "Verknüpfung im Startmenü");
    }

    [AvaloniaFact]
    public void Every_control_of_the_section_has_a_name_and_a_role_in_every_language()
    {
        var keys = A11yAudit.Keys();
        foreach (var language in A11yAudit.Languages)
        {
            _app.SetLanguage(language);
            var items = A11yAudit.Collect(Open("").RowOf(SettingsWindow.SystemSectionId)!);
            Assert.True(items.Count >= ShellIntegration.Extensions.Count + 4, language);
            Assert.All(items, item => Assert.Empty(A11yAudit.Problems(item, keys, [])));
        }

        _app.SetLanguage("en");
        Assert.Contains(A11yAudit.Collect(Open("").RowOf(SettingsWindow.SystemSectionId)!), i => i.Name == "Open with: .log");
    }

    [AvaloniaFact]
    public void Outside_textida_exe_the_section_explains_instead_of_offering_switches()
    {
        ShellHost.ExeOverride = null; // the test host is not Textida.exe
        var view = Open();

        var section = view.RowOf(SettingsWindow.SystemSectionId)!;
        Assert.Empty(section.GetVisualDescendants().OfType<CheckBox>());
        Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.ShellUnavailable));
        Assert.Single(section.GetVisualDescendants().OfType<Button>()); // the page of the default apps is still one click away
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.6.6: the folder action "Open in Textida as project" on Linux, turned on and off in the settings. The user's data folder and the system's data folder are folders of a temp directory
/// standing in for <c>XDG_DATA_HOME</c> and <c>XDG_DATA_DIRS</c>; the real ones are never touched.
/// </summary>
[Collection("LocState")]
public class LinuxIntegrationSettingsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly LinuxLocations _locations;
    private readonly string _exe = "/home/user/.local/opt/textida/Textida";
    private readonly App _app;
    private SettingsWindow? _view;

    public LinuxIntegrationSettingsTests()
    {
        _locations = new LinuxLocations { DataHome = Path.Combine(_temp.Path, "home-share"), DataDirs = [Path.Combine(_temp.Path, "usr-share")], RefreshDatabase = false };
        LinuxHost.LocationsOverride = _locations;
        LinuxHost.ExeOverride = _exe;
        _app = TestApp.Prepare(_temp);
    }

    public void Dispose()
    {
        _view?.Close();
        TestApp.Cleanup(_app);
        LinuxHost.LocationsOverride = null;
        LinuxHost.ExeOverride = null;
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string UserEntry => Path.Combine(_locations.DataHome, "applications", LinuxDesktop.ProjectEntryFile);

    private string SystemEntry => Path.Combine(_locations.DataDirs[0], "applications", LinuxDesktop.ProjectEntryFile);

    private SettingsWindow Open(string query = "")
    {
        _app.OpenSettings(query);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        _view = _app.SettingsView!;
        if (query.Length == 0) _view.SelectCategory(SettingCategories.System);
        return _view;
    }

    private CheckBox FolderBox(SettingsWindow view) => view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<CheckBox>()
        .Single(b => (string?)b.Content == Loc.Get(K.SettingsSystemFolderMenu, ("label", Loc.Get(K.ShellFolderMenuLabel))));

    [AvaloniaFact]
    public void The_users_entry_is_written_for_the_program_and_deleted_again()
    {
        Assert.False(LinuxDesktop.HasProjectEntry(_locations));
        LinuxDesktop.SetProjectEntry(_locations, true, _exe);
        Assert.Equal(LinuxDesktop.ProjectEntry(_exe), File.ReadAllText(UserEntry));
        Assert.True(LinuxDesktop.HasProjectEntry(_locations));

        LinuxDesktop.SetProjectEntry(_locations, false, _exe);
        Assert.False(File.Exists(UserEntry));
        Assert.False(LinuxDesktop.HasProjectEntry(_locations));
        LinuxDesktop.SetProjectEntry(_locations, false, _exe); // off again is no error
    }

    [AvaloniaFact]
    public void An_entry_of_the_system_is_hidden_for_the_user_and_shown_again_and_is_never_touched()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SystemEntry)!);
        File.WriteAllText(SystemEntry, LinuxDesktop.ProjectEntry("/opt/textida/Textida"));
        Assert.True(LinuxDesktop.HasProjectEntry(_locations));

        LinuxDesktop.SetProjectEntry(_locations, false, _exe);
        Assert.Contains("Hidden=true", File.ReadAllText(UserEntry));
        Assert.False(LinuxDesktop.HasProjectEntry(_locations));

        LinuxDesktop.SetProjectEntry(_locations, true, _exe);
        Assert.Equal(LinuxDesktop.ProjectEntry(_exe), File.ReadAllText(UserEntry));
        Assert.True(LinuxDesktop.HasProjectEntry(_locations));
        Assert.Equal(LinuxDesktop.ProjectEntry("/opt/textida/Textida"), File.ReadAllText(SystemEntry));
    }

    [AvaloniaTheory]
    [InlineData("integrace")]
    [InlineData("Nautilus")]
    [InlineData("linux slozka")]
    public void The_section_is_found_by_its_title_and_its_keywords_and_not_by_others(string query)
    {
        Assert.Contains(SettingsWindow.SystemSectionId, Open(query).ShownIds);
        Assert.DoesNotContain(SettingsWindow.SystemSectionId, Open("tabSize").ShownIds);
    }

    [AvaloniaFact]
    public void The_switch_shows_the_disk_and_writes_the_entry_at_once()
    {
        LinuxDesktop.SetProjectEntry(_locations, true, _exe); // as install.sh left it
        var view = Open();
        var box = FolderBox(view);
        Assert.True(box.IsChecked);
        Assert.Contains(view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(K.SettingsLinuxDescription));

        box.IsChecked = false;
        Assert.False(File.Exists(UserEntry));
        box.IsChecked = true;
        Assert.Equal(LinuxDesktop.ProjectEntry(_exe), File.ReadAllText(UserEntry));
    }

    [AvaloniaFact]
    public void A_refused_write_flips_the_switch_back_and_says_why()
    {
        Directory.CreateDirectory(_locations.DataHome);
        File.WriteAllText(Path.Combine(_locations.DataHome, "applications"), "a file where the folder belongs");
        var view = Open();
        var box = FolderBox(view);
        Assert.False(box.IsChecked);

        box.IsChecked = true;
        Assert.False(box.IsChecked);
        Assert.Contains(view.RowOf(SettingsWindow.SystemSectionId)!.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsVisible && (t.Text ?? "").StartsWith(Loc.Get(K.SettingsLinuxFailed, ("reason", "")), StringComparison.Ordinal));
    }
}

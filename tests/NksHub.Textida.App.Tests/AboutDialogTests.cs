using System.Globalization;
using System.IO.Compression;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The About window: every fact it shows, the block "Copy information" copies, the links that open only on a click, the notices viewer, the names in four languages.</summary>
[Collection("LocState")]
public class AboutDialogTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AboutDialogTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>Opens the real About window (the questions it asks go to the scripted dialogs of the app), runs <paramref name="inspect"/> on it and closes it.</summary>
    private async Task InAbout(Func<Window, Task> inspect, string? programDirectory = null)
    {
        var dialogs = new AvaloniaDialogs();
        if (programDirectory is not null) dialogs.ProgramDirectory = programDirectory;
        Exception? failure = null;
        dialogs.Shown = window => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                window.UpdateLayout();
                await inspect(window);
            }
            catch (Exception e) { failure = e; }
            finally { window.Close(); }
        }, DispatcherPriority.Background);
        await dialogs.ShowAboutAsync(_w).WaitAsync(TimeSpan.FromSeconds(30));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private Task InAbout(Action<Window> inspect, string? programDirectory = null) => InAbout(w => { inspect(w); return Task.CompletedTask; }, programDirectory);

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    /// <summary>The value of a fact by its label: the value is named "label: value".</summary>
    private static string Fact(Window w, string label) =>
        w.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => (AutomationProperties.GetName(t) ?? "").StartsWith(label + ": ", StringComparison.Ordinal)).Text!;

    private static Button ButtonShowing(Window w, string content) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s == content);

    /// <summary>The line next to the buttons that says what the last press did.</summary>
    private static string StatusText(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Single(t => AutomationProperties.GetLiveSetting(t) == AutomationLiveSetting.Polite).Text ?? "";

    private static string Lines(string text) => text.ReplaceLineEndings("\n");

    private const int SystemTab = 0, PluginsTab = 1, LicenseTab = 2;

    /// <summary>Shows a tab; only the page that is shown has controls to look at.</summary>
    private static void Select(Window w, int tab)
    {
        w.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = tab;
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private string Zip(string version, string id = "acme.clips")
    {
        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N") + ".zip");
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("nkspad-plugin.json").Open()))
            writer.Write("{\"schemaVersion\":1,\"id\":\"" + id + "\",\"name\":\"Acme clips\",\"version\":\"" + version + "\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[\"document.read\"]," +
                "\"contributes\":{\"commands\":[{\"id\":\"" + id + ".hi\",\"title\":\"Hi\"}],\"clips\":[{\"id\":\"hi\",\"title\":\"Hi\",\"text\":\"hi\",\"command\":\"" + id + ".hi\"}]}}");
        return path;
    }

    // ---- the facts ----

    [AvaloniaFact]
    public async Task The_window_shows_each_fact()
    {
        using var program = new TempDir();
        program.File("version.txt", "9.8.7\n");
        program.File("LICENSE", File.ReadAllText(Path.Combine(Repo.Root, "LICENSE")));
        program.File("THIRD-PARTY-NOTICES.md", "# Notices\n");
        _dialogs.Answers.Enqueue(0);
        var install = _w.InstallPluginAsync(Zip("1.2.3"));
        ToolTestKit.WaitFor(() => install.IsCompleted, "the plugin was not installed");
        Assert.True(install.Result);

        await InAbout(w =>
        {
            Assert.Equal(Loc.Get(K.DialogAboutTitle, ("name", Loc.Get(K.AppName))), w.Title);
            var texts = Texts(w).ToList();
            Assert.Contains(Loc.Get(K.AppName), texts);
            Assert.Contains(Loc.Get(K.DialogAboutVersion, ("version", "9.8.7")), texts);
            Assert.Matches("^[0-9a-f]{8}$", BuildStamp.Commit);
            Assert.Matches(@"^\d{4}-\d\d-\d\d$", BuildStamp.Date);
            Assert.Contains(Loc.Get(K.DialogAboutBuild, ("commit", BuildStamp.Commit), ("date", BuildStamp.Date)), texts);
            Assert.NotNull(Assert.Single(w.GetVisualDescendants().OfType<Image>()).Source);

            Assert.Equal(RuntimeInformation.FrameworkDescription, Fact(w, Loc.Get(K.DialogAboutFactRuntime)));
            Assert.Equal("JIT", Fact(w, Loc.Get(K.DialogAboutFactCompiled)));
            Assert.Equal(HostFacts.OsName(), Fact(w, Loc.Get(K.DialogAboutFactOs)));
            Assert.Equal(HostFacts.ArchitectureName(), Fact(w, Loc.Get(K.DialogAboutFactArchitecture)));
            Assert.Equal($"{CultureInfo.GetCultureInfo("cs").NativeName} (cs)", Fact(w, Loc.Get(K.DialogAboutFactLanguage)));
            Assert.Equal(Loc.Get(K.DialogAboutProfileCustom), Fact(w, Loc.Get(K.DialogAboutFactProfile))); // the profile of a test is a folder of its own
            Assert.Equal(_app.ProfileDirectory, Fact(w, Loc.Get(K.DialogAboutFactProfileFolder)));
            Assert.Equal(_app.Output.Folder, Fact(w, Loc.Get(K.DialogAboutFactLogFolder)));
            Assert.NotNull(ButtonShowing(w, AboutInfo.ProjectUrl));
            Assert.NotNull(ButtonShowing(w, AboutInfo.IssuesUrl));

            Select(w, PluginsTab);
            foreach (var (_, name) in PluginServices.BuiltIns)
                Assert.Equal(Loc.Get(K.DialogAboutPluginRow, ("version", "9.8.7"), ("state", Loc.Get(K.PluginsOriginBuiltin))), Fact(w, name));
            Assert.Equal(Loc.Get(K.DialogAboutPluginRow, ("version", "1.2.3"), ("state", Loc.Get(K.PluginsOriginInstalled))), Fact(w, "Acme clips"));

            Select(w, LicenseTab);
            Assert.Contains(Loc.Get(K.DialogAboutMit) + "\nCopyright (c) 2026 NKS Hub", Texts(w));
        }, program.Path);
    }

    [AvaloniaFact]
    public async Task A_disabled_plugin_is_listed_as_installed_and_disabled()
    {
        _dialogs.Answers.Enqueue(0);
        var install = _w.InstallPluginAsync(Zip("1.2.3"));
        ToolTestKit.WaitFor(() => install.IsCompleted, "the plugin was not installed");
        var disable = _app.Plugins.Manager.SetEnabledAsync("acme.clips", false);
        ToolTestKit.WaitFor(() => disable.IsCompleted, "the plugin was not disabled");

        await InAbout(w =>
        {
            Select(w, PluginsTab);
            Assert.Equal(Loc.Get(K.DialogAboutPluginRow, ("version", "1.2.3"), ("state", Loc.Get(K.PluginsOriginInstalled) + ", " + Loc.Get(K.PluginsOriginDisabled).ToLowerInvariant())), Fact(w, "Acme clips"));
        });
    }

    [AvaloniaFact]
    public async Task Without_version_txt_the_version_of_the_build_is_shown_and_without_LICENSE_there_is_no_copyright()
    {
        using var empty = new TempDir();
        await InAbout(w =>
        {
            Assert.Contains(Loc.Get(K.DialogAboutVersion, ("version", BuildStamp.Version)), Texts(w));
            Select(w, LicenseTab);
            var texts = Texts(w).ToList();
            Assert.Contains(Loc.Get(K.DialogAboutMit), texts);
            Assert.DoesNotContain(texts, t => t.Contains("Copyright", StringComparison.Ordinal));
        }, empty.Path);
    }

    [AvaloniaFact]
    public async Task The_open_folder_buttons_hand_the_profile_and_the_log_folder_to_the_system()
    {
        var opened = new List<string>();
        bool works = true;
        _app.PathOpener = folder => { opened.Add(folder); return Task.FromResult(works); };
        await InAbout(w =>
        {
            Assert.Empty(opened);
            Click(w.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == Loc.Get(K.DialogAboutOpenProfile)));
            Click(w.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == Loc.Get(K.DialogAboutOpenLog)));
            ToolTestKit.WaitFor(() => opened.Count == 2, "the folders were not opened");
            Assert.Equal([_app.ProfileDirectory, _app.Output.Folder], opened);
            Assert.True(Directory.Exists(_app.Output.Folder));
            Assert.Equal("", StatusText(w));

            works = false;
            Click(w.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == Loc.Get(K.DialogAboutOpenProfile)));
            ToolTestKit.WaitFor(() => StatusText(w) != "", "no message after a folder that did not open");
            Assert.Equal(Loc.Get(K.SpecialLaunchFailed, ("path", _app.ProfileDirectory)), StatusText(w));
        });
    }

    // ---- Copy information ----

    private string ExpectedReport(string version, bool paths)
    {
        var lines = new List<string>
        {
            "Textida " + version, "Commit: " + BuildStamp.Commit, "Build date: " + BuildStamp.Date, $"Runtime: {RuntimeInformation.FrameworkDescription} (JIT)",
            "OS: " + HostFacts.OsName(), "Architecture: " + HostFacts.ArchitectureName(), "UI language: cs", "Profile: custom",
            "Plugins: " + string.Join(", ", PluginServices.BuiltIns.Select(b => $"{b.Name} {version} (bundled)")),
        };
        if (paths) lines.AddRange(["Profile folder: " + _app.ProfileDirectory, "Log folder: " + _app.Output.Folder]);
        return string.Join("\n", lines);
    }

    [AvaloniaFact]
    public async Task Copy_information_copies_the_block_without_personal_paths_unless_the_box_is_ticked()
    {
        using var program = new TempDir();
        program.File("version.txt", "9.8.7");
        await InAbout(async w =>
        {
            var copy = ButtonShowing(w, Loc.Get(K.DialogAboutCopy));
            var box = w.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.False(box.IsChecked);
            Assert.Equal(Loc.Get(K.DialogAboutIncludePaths), AutomationProperties.GetName(box));
            Assert.Equal("", StatusText(w));

            await w.Clipboard!.SetTextAsync("before");
            Click(copy);
            ToolTestKit.WaitFor(() => StatusText(w) == Loc.Get(K.DialogAboutCopied), "nothing was copied");
            var plain = Lines((await w.Clipboard.TryGetTextAsync())!);
            Assert.Equal(ExpectedReport("9.8.7", paths: false), plain);
            foreach (var personal in new[] { _app.ProfileDirectory, _app.Output.Folder, _temp.Path, Environment.UserName })
                Assert.DoesNotContain(personal, plain, StringComparison.OrdinalIgnoreCase);

            box.IsChecked = true;
            await w.Clipboard.SetTextAsync("before");
            Click(copy);
            for (int i = 0; i < 300 && (await w.Clipboard.TryGetTextAsync()) == "before"; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.Equal(ExpectedReport("9.8.7", paths: true), Lines((await w.Clipboard.TryGetTextAsync())!));
        }, program.Path);
    }

    // ---- links ----

    [AvaloniaFact]
    public async Task A_link_does_nothing_until_it_is_pressed_and_then_asks_the_same_question_as_every_link()
    {
        var opened = new List<Uri>();
        bool works = true;
        _app.UriOpener = uri => { opened.Add(uri); return Task.FromResult(works); };
        await InAbout(w =>
        {
            Assert.Empty(opened);
            Assert.Empty(_dialogs.Asked);

            _dialogs.Answers.Enqueue(1); // the user says no
            Click(ButtonShowing(w, AboutInfo.ProjectUrl));
            var asked = Assert.Single(_dialogs.Asked);
            Assert.Equal(Loc.Get(K.PreviewLinkTitle), asked.Title);
            Assert.Equal(Loc.Get(K.PreviewLinkMessage, ("url", AboutInfo.ProjectUrl)), asked.Message);
            Assert.Empty(opened);

            _dialogs.Answers.Enqueue(0);
            Click(ButtonShowing(w, AboutInfo.IssuesUrl));
            ToolTestKit.WaitFor(() => opened.Count == 1, "the link was not opened");
            Assert.Equal(new Uri(AboutInfo.IssuesUrl), opened[0]);
            Assert.Equal("", StatusText(w));

            works = false;
            _dialogs.Answers.Enqueue(0);
            Click(ButtonShowing(w, AboutInfo.ProjectUrl));
            ToolTestKit.WaitFor(() => StatusText(w) != "", "no message after a link that did not open");
            Assert.Equal(Loc.Get(K.PreviewLinkFailed, ("url", AboutInfo.ProjectUrl)), StatusText(w));
            Assert.Equal(3, _dialogs.Asked.Count);
        });
    }

    // ---- licence and notices ----

    [AvaloniaFact]
    public async Task The_notices_viewer_holds_the_file_that_ships_with_the_program_and_can_be_scrolled_and_selected()
    {
        foreach (var file in new[] { "LICENSE", "THIRD-PARTY-NOTICES.md" })
            Assert.Equal(Lines(File.ReadAllText(Path.Combine(Repo.Root, file))), Lines(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)))); // the build puts both next to the program

        await InAbout(w =>
        {
            Select(w, LicenseTab);
            var viewer = w.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal(Lines(File.ReadAllText(Path.Combine(Repo.Root, "THIRD-PARTY-NOTICES.md"))), Lines(viewer.Text!));
            Assert.Contains("SSH.NET", viewer.Text);
            Assert.True(viewer.IsReadOnly);
            Assert.True(viewer.Focusable && viewer.IsTabStop);
            Assert.Equal(Loc.Get(K.DialogAboutNotices), AutomationProperties.GetName(viewer));
            viewer.SelectAll();
            Assert.Equal(viewer.Text!.Length, viewer.SelectedText.Length);
        });
    }

    [AvaloniaFact]
    public async Task A_missing_notices_file_is_said_so_and_the_rest_of_the_window_works()
    {
        using var empty = new TempDir();
        await InAbout(w =>
        {
            Assert.Equal(_app.ProfileDirectory, Fact(w, Loc.Get(K.DialogAboutFactProfileFolder)));
            Select(w, LicenseTab);
            Assert.Equal(Loc.Get(K.DialogAboutNoticesMissing, ("file", "THIRD-PARTY-NOTICES.md")), w.GetVisualDescendants().OfType<TextBox>().Single().Text);
        }, empty.Path);
    }

    // ---- keyboard and screen reader ----

    [AvaloniaFact]
    public async Task Esc_closes_the_window()
    {
        await InAbout(w =>
        {
            Assert.True(w.IsVisible);
            TestApp.Press(w, Key.Escape);
            Assert.False(w.IsVisible);
        });
    }

    [AvaloniaFact]
    public async Task Every_control_has_a_name_in_every_language_in_reading_order()
    {
        var run = new GateRun();
        foreach (var lang in A11yAudit.Languages)
        {
            _app.SetLanguage(lang);
            await InAbout(w =>
            {
                // every page of the tab control is walked: the keyboard reaches its controls and a screen reader reads them
                foreach (var tab in new[] { SystemTab, PluginsTab, LicenseTab })
                {
                    Select(w, tab);
                    run.Audit("about:" + tab, lang, w);
                    run.AuditOrder("about:" + tab, w);
                }
                Select(w, LicenseTab);
                var viewer = AutomationProperties.GetName(w.GetVisualDescendants().OfType<TextBox>().Single());
                var box = AutomationProperties.GetName(w.GetVisualDescendants().OfType<CheckBox>().Single());
                Assert.Equal(Loc.Get(K.DialogAboutNotices), viewer);
                Assert.Equal(Loc.Get(K.DialogAboutIncludePaths), box);
                if (lang != "en")
                {
                    Assert.NotEqual(Loc.Catalog("en")[K.DialogAboutNotices], viewer);
                    Assert.NotEqual(Loc.Catalog("en")[K.DialogAboutIncludePaths], box);
                }
                Select(w, SystemTab);
                var buttonNames = w.GetVisualDescendants().OfType<Button>().Select(b => AutomationProperties.GetName(b) ?? b.Content as string ?? "").ToList();
                Assert.Contains(Loc.Get(K.DialogAboutLinkProject) + ": " + AboutInfo.ProjectUrl, buttonNames);
                Assert.Contains(Loc.Get(K.DialogAboutLinkIssues) + ": " + AboutInfo.IssuesUrl, buttonNames);
                Assert.Contains(Loc.Get(K.DialogAboutOpenProfile), buttonNames);
                Assert.Contains(Loc.Get(K.DialogAboutOpenLog), buttonNames);
                Assert.Contains(Loc.Get(K.DialogAboutCopy), buttonNames);
                Assert.Contains(Loc.Get(K.DialogAboutClose), buttonNames);
                Assert.Equal(Loc.Get(K.DialogAboutFactOs) + ": " + HostFacts.OsName(), w.GetVisualDescendants().OfType<SelectableTextBlock>().Select(t => AutomationProperties.GetName(t) ?? "").Single(n => n.StartsWith(Loc.Get(K.DialogAboutFactOs), StringComparison.Ordinal)));
                Assert.Contains(w.GetVisualDescendants().OfType<Button>(), b => b.IsCancel && b.IsDefault);
                Assert.Equal([Loc.Get(K.DialogAboutSectionSystem), Loc.Get(K.A11yPluginsManager), Loc.Get(K.DialogAboutSectionLicense)], w.GetVisualDescendants().OfType<TabItem>().Select(t => t.Header as string));
            });
        }
        _app.SetLanguage(Loc.DefaultLanguage);
        run.AssertClean();
    }
}

/// <summary>The parts of the About window that need no window: the report, the profile kind, what the host says about itself, the files next to the program.</summary>
public class AboutFactsTests
{
    private static string Lines(string text) => text.ReplaceLineEndings("\n");

    [Fact]
    public void The_report_names_the_facts_in_English_and_adds_the_folders_only_on_request()
    {
        var info = new AboutInfo("1.4.0", "abcd1234", "2026-10-28", ".NET 10.0.0", "NativeAOT", "macOS 14.5", "x64 (Rosetta on Apple silicon)", "de", ProfileKind.Portable,
            "/Users/someone/Textida/profile", "/Users/someone/Textida/profile/output",
            [new AboutPlugin("JSON", "1.4.0", PluginOrigin.Bundled), new AboutPlugin("Acme", "0.2.0", PluginOrigin.Disabled), new AboutPlugin("Broken", "0.1.0", PluginOrigin.Unreadable, "bad manifest")],
            "Copyright (c) 2026 NKS Hub");
        const string plain =
            "Textida 1.4.0\nCommit: abcd1234\nBuild date: 2026-10-28\nRuntime: .NET 10.0.0 (NativeAOT)\nOS: macOS 14.5\nArchitecture: x64 (Rosetta on Apple silicon)\nUI language: de\nProfile: portable\n" +
            "Plugins: JSON 1.4.0 (bundled), Acme 0.2.0 (installed, disabled), Broken 0.1.0 (unreadable)";
        Assert.Equal(plain, Lines(info.ToReport(includePaths: false)));
        Assert.Equal(plain + "\nProfile folder: /Users/someone/Textida/profile\nLog folder: /Users/someone/Textida/profile/output", Lines(info.ToReport(includePaths: true)));
        Assert.Contains("Commit: unknown", (info with { Commit = "" }).ToReport(false));
    }

    [Fact]
    public void The_profile_kind_is_the_place_the_profile_is_in()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "textida-kind"));
        string exe = Path.Combine(root, "program"), home = Path.Combine(root, "home"), data = Path.Combine(root, "appdata");
        string portable = Path.Combine(exe, "profile"), mac = Path.Combine(home, "Library", "Application Support", "Textida"), other = Path.Combine(data, "Textida");

        Assert.Equal(ProfileKind.Portable, ProfileLocation.KindOf(portable, exe, markerExists: true, isMac: false, home, data));
        Assert.Equal(ProfileKind.Portable, ProfileLocation.KindOf(portable + Path.DirectorySeparatorChar, exe, markerExists: true, isMac: false, home, data));
        Assert.Equal(ProfileKind.Custom, ProfileLocation.KindOf(portable, exe, markerExists: false, isMac: false, home, data)); // without the marker that folder is nobody's profile
        Assert.Equal(ProfileKind.User, ProfileLocation.KindOf(other, exe, markerExists: false, isMac: false, home, data));
        Assert.Equal(ProfileKind.User, ProfileLocation.KindOf(mac, exe, markerExists: false, isMac: true, home, data));
        Assert.Equal(ProfileKind.Custom, ProfileLocation.KindOf(mac, exe, markerExists: false, isMac: false, home, data));
        Assert.Equal(ProfileKind.Custom, ProfileLocation.KindOf(Path.Combine(root, "elsewhere"), exe, markerExists: true, isMac: false, home, data));
    }

    [Fact]
    public void The_host_facts_read_the_macOS_version_and_name_Rosetta()
    {
        const string plist = "<plist><dict><key>ProductName</key><string>macOS</string>\n<key>ProductVersion</key>\n  <string>14.5</string></dict></plist>";
        Assert.Equal("14.5", HostFacts.ProductVersionOf(plist));
        Assert.Null(HostFacts.ProductVersionOf("<plist/>"));
        Assert.Equal("x64 (Rosetta on Apple silicon)", HostFacts.ArchitectureName(Architecture.X64, rosetta: true));
        Assert.Equal("arm64", HostFacts.ArchitectureName(Architecture.Arm64, rosetta: false));
        if (!OperatingSystem.IsMacOS()) Assert.False(HostFacts.IsRunningUnderRosetta());
    }

    [Fact]
    public void The_copyright_line_and_the_version_come_from_the_files_next_to_the_program()
    {
        using var program = new TempDir();
        Assert.Equal("", AboutInfo.CopyrightIn(program.Path));
        Assert.Null(AboutInfo.NoticesIn(program.Path));
        Assert.Equal(BuildStamp.Version, AboutInfo.VersionIn(program.Path));
        program.File("LICENSE", "MIT License\n\nCopyright (c) 2099 Somebody\n\nPermission is hereby granted\n");
        program.File("version.txt", " 3.2.1\r\n");
        program.File("THIRD-PARTY-NOTICES.md", "notice");
        Assert.Equal("Copyright (c) 2099 Somebody", AboutInfo.CopyrightIn(program.Path));
        Assert.Equal("3.2.1", AboutInfo.VersionIn(program.Path));
        Assert.Equal("notice", AboutInfo.NoticesIn(program.Path));
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), BuildStamp.Version);
    }
}

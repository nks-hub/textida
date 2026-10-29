using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.7.8: what the macOS package and the macOS integration write. The files are checked on any system; the symbolic link of the terminal command needs a system that lets a user make one.</summary>
public class MacBundleTests
{
    /// <summary>A property list is well formed: every dictionary has a key before each value, and the document announces itself as a property list.</summary>
    internal static void AssertPropertyList(string xml)
    {
        var document = XDocument.Parse(xml);
        Assert.Equal("plist", document.Root!.Name.LocalName);
        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
        Assert.Contains("<!DOCTYPE plist PUBLIC", xml, StringComparison.Ordinal);
        foreach (var dict in document.Descendants("dict"))
        {
            var children = dict.Elements().ToList();
            Assert.True(children.Count % 2 == 0, "a dictionary with a key or a value too many: " + dict.Elements().FirstOrDefault()?.Value);
            for (int i = 0; i < children.Count; i += 2) Assert.Equal("key", children[i].Name.LocalName);
            for (int i = 1; i < children.Count; i += 2) Assert.NotEqual("key", children[i].Name.LocalName);
        }
    }

    private static Dictionary<string, XElement> Entries(XElement dict)
    {
        var children = dict.Elements().ToList();
        return Enumerable.Range(0, children.Count / 2).ToDictionary(i => children[2 * i].Value, i => children[2 * i + 1]);
    }

    [Theory]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("1.4.0-beta.2+abc123", "1.4.0")]
    [InlineData("0.0.0-3f2a9c1", "0.0.0")]
    [InlineData("2.1", "2.1")]
    [InlineData("10.20.30.40", "10.20.30")]
    [InlineData("01.02.03", "1.2.3")]
    [InlineData("v1.2", "0.0.0")]
    [InlineData("", "0.0.0")]
    public void The_version_of_the_bundle_keeps_its_numbers_and_drops_the_rest(string version, string expected) => Assert.Equal(expected, MacBundle.BundleVersion(version));

    [Theory]
    [InlineData("/Applications/Textida.app/Contents/MacOS/Textida", "/Applications/Textida.app")]
    [InlineData("/Users/me/Apps/Textida.app/Contents/MacOS/Textida", "/Users/me/Apps/Textida.app")]
    [InlineData("/tmp/build/Textida", null)]
    [InlineData("/Applications/Textida.app/Contents/Resources/Textida", null)]
    [InlineData("/Applications/Textida/Contents/MacOS/Textida", null)]
    public void The_bundle_of_a_program_is_found_from_its_path(string executable, string? expected) =>
        Assert.Equal(expected, MacBundle.BundleOf(executable)?.Replace('\\', '/'));

    [Theory]
    [InlineData("/Applications/Textida.app/Contents/MacOS/", "/Applications/Textida.app/Contents/Resources")]
    [InlineData("/Applications/Textida.app/Contents/MacOS", "/Applications/Textida.app/Contents/Resources")]
    [InlineData("/opt/textida/", "/opt/textida/")]
    [InlineData("/Applications/Textida/Contents/MacOS/", "/Applications/Textida/Contents/MacOS/")]
    public void The_plugins_and_notices_are_in_the_resources_of_a_bundle_and_next_to_the_program_elsewhere(string programDirectory, string expected) =>
        Assert.Equal(expected, ProgramFolders.FilesOf(programDirectory).Replace('\\', '/'));

    [Fact]
    public void The_info_plist_names_the_program_the_icon_the_oldest_system_and_the_document_types()
    {
        var xml = MacBundle.InfoPlist("1.4.0-beta.2");
        AssertPropertyList(xml);
        var entries = Entries(XDocument.Parse(xml).Root!.Element("dict")!);

        Assert.Equal(MacBundle.BundleId, entries["CFBundleIdentifier"].Value);
        Assert.Equal("Textida", entries["CFBundleExecutable"].Value);
        Assert.Equal("Textida", entries["CFBundleIconFile"].Value);
        Assert.Equal("APPL", entries["CFBundlePackageType"].Value);
        Assert.Equal("1.4.0", entries["CFBundleShortVersionString"].Value);
        Assert.Equal("1.4.0", entries["CFBundleVersion"].Value);
        Assert.Equal("14.0", entries["LSMinimumSystemVersion"].Value);
        Assert.Equal("true", entries["NSHighResolutionCapable"].Name.LocalName);
        Assert.Equal(["en", "cs", "sk", "de"], entries["CFBundleLocalizations"].Elements().Select(e => e.Value));

        var types = entries["CFBundleDocumentTypes"].Elements("dict").Select(Entries).ToList();
        Assert.Equal(2, types.Count);
        Assert.All(types, t =>
        {
            Assert.Equal("Editor", t["CFBundleTypeRole"].Value);
            Assert.Equal("Alternate", t["LSHandlerRank"].Value); // installing Textida takes no file type away from another application
        });
        Assert.Equal(IntegrationExtensions.All.Select(e => e.TrimStart('.')), types[0]["CFBundleTypeExtensions"].Elements().Select(e => e.Value));
        Assert.Contains("public.plain-text", types[1]["LSItemContentTypes"].Elements().Select(e => e.Value));
    }

    [Fact]
    public void The_quick_action_is_a_service_for_finder_that_opens_the_chosen_items_with_the_bundle()
    {
        var files = MacBundle.FinderActionFiles("Open in Textida", "/Applications/Textida.app");

        Assert.Equal(["Contents/Info.plist", "Contents/document.wflow"], files.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var xml in files.Values) AssertPropertyList(xml);

        var service = Entries(XDocument.Parse(files["Contents/Info.plist"]).Root!.Element("dict")!)["NSServices"].Element("dict")!;
        var entries = Entries(service);
        Assert.Equal("Open in Textida", Entries(entries["NSMenuItem"])["default"].Value);
        Assert.Equal("runWorkflowAsService", entries["NSMessage"].Value);
        Assert.Equal("com.apple.finder", Entries(entries["NSRequiredContext"])["NSApplicationIdentifier"].Value);
        Assert.Equal(["public.item"], entries["NSSendFileTypes"].Elements().Select(e => e.Value));

        var workflow = Entries(XDocument.Parse(files["Contents/document.wflow"]).Root!.Element("dict")!);
        var action = Entries(Entries(workflow["actions"].Element("dict")!)["action"]);
        var parameters = Entries(action["ActionParameters"]);
        Assert.Equal("com.apple.RunShellScript", action["BundleIdentifier"].Value);
        Assert.Equal("1", parameters["inputMethod"].Value); // the files arrive as arguments
        Assert.Contains(MacBundle.ActionMarker, parameters["COMMAND_STRING"].Value, StringComparison.Ordinal);
        Assert.Contains("open -a '/Applications/Textida.app' \"$f\"", parameters["COMMAND_STRING"].Value, StringComparison.Ordinal);
        var meta = Entries(workflow["workflowMetaData"]);
        Assert.Equal("com.apple.Automator.servicesMenu", meta["workflowTypeIdentifier"].Value);
        Assert.Equal("com.apple.Automator.fileSystemObject", meta["serviceInputTypeIdentifier"].Value);
    }

    [Fact]
    public void A_label_or_a_path_with_markup_characters_stays_text_and_a_quote_in_a_path_stays_one_word_of_the_shell()
    {
        var files = MacBundle.FinderActionFiles("Open <in> \"T&T\"", "/Users/o'brien/Apps/T&T.app");
        foreach (var xml in files.Values) AssertPropertyList(xml);

        Assert.Equal("Open <in> \"T&T\"", Entries(Entries(XDocument.Parse(files["Contents/Info.plist"]).Root!.Element("dict")!)["NSServices"].Element("dict")!)["NSMenuItem"].Element("string")!.Value);
        Assert.Equal("'/Users/o'\\''brien/Apps/T&T.app'", MacBundle.ShellQuote("/Users/o'brien/Apps/T&T.app"));
        Assert.Contains("open -a '/Users/o'\\''brien/Apps/T&T.app'", files["Contents/document.wflow"].Replace("&amp;", "&").Replace("&apos;", "'"), StringComparison.Ordinal);
    }
}

public class MacIntegrationTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly string _exe;
    private readonly MacLocations _locations;

    public MacIntegrationTests()
    {
        _exe = Path.Combine(_temp.Path, "Applications", "Textida.app", "Contents", "MacOS", "Textida");
        Directory.CreateDirectory(Path.GetDirectoryName(_exe)!);
        File.WriteAllText(_exe, "program");
        _locations = new MacLocations { Home = Path.Combine(_temp.Path, "home"), SystemBinDir = Path.Combine(_temp.Path, "usr-local-bin"), RefreshServices = false };
    }

    public void Dispose() => _temp.Dispose();

    /// <summary>Launch Services as a table: the application each extension opens.</summary>
    internal sealed class FakeLaunchServices : ILaunchServices
    {
        public readonly Dictionary<string, string> Handlers = new(StringComparer.Ordinal);
        public bool Refuse;

        public string? DefaultHandlerOf(string extension) => Handlers.GetValueOrDefault(extension);

        public void SetDefaultHandler(string extension, string bundleId)
        {
            if (Refuse) throw new IOException("Launch Services refused " + extension + " (error -54).");
            Handlers[extension] = bundleId;
        }
    }

    private MacIntegration Integration(FakeLaunchServices? services = null, string? exe = null) => new(_locations, exe ?? _exe, services ?? new FakeLaunchServices());

    /// <summary>Making a symbolic link needs a right that a Windows user may not have; the test then has nothing to check on this machine.</summary>
    private bool CanLink()
    {
        var probe = Path.Combine(_temp.Path, "probe-link");
        try
        {
            File.CreateSymbolicLink(probe, _exe);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public void The_command_is_a_link_to_the_program_in_the_folder_of_the_user_when_the_folder_of_the_system_is_missing_and_goes_again_with_one_call()
    {
        if (!CanLink()) return;
        var integration = Integration();
        Assert.False(integration.HasCommand());

        var link = integration.AddCommand();

        Assert.Equal(Path.Combine(_locations.Home, "bin", "textida"), link);
        Assert.Equal(_exe, new FileInfo(link).LinkTarget);
        Assert.True(integration.HasCommand());
        Assert.Equal(link, integration.CommandLink());
        Assert.Equal(link, integration.AddCommand()); // already there: the same link, no error

        integration.RemoveCommand();

        Assert.False(File.Exists(link) || new FileInfo(link).LinkTarget is not null);
        Assert.True(File.Exists(_exe));
        Assert.False(integration.HasCommand());
    }

    [Fact]
    public void The_command_goes_to_the_folder_of_the_system_when_it_is_there_and_may_be_written()
    {
        if (!CanLink()) return;
        Directory.CreateDirectory(_locations.SystemBinDir);
        var integration = Integration();

        var link = integration.AddCommand();

        Assert.Equal(Path.Combine(_locations.SystemBinDir, "textida"), link);
        Assert.False(Directory.Exists(Path.Combine(_locations.Home, "bin"))); // nothing was made in the folder of the user
    }

    [Fact]
    public void A_command_of_that_name_that_leads_elsewhere_is_never_replaced_or_removed()
    {
        if (!CanLink()) return;
        Directory.CreateDirectory(_locations.SystemBinDir);
        var foreign = Path.Combine(_locations.SystemBinDir, "textida");
        File.WriteAllText(foreign, "another program");
        var integration = Integration();

        var link = integration.AddCommand(); // the folder of the system is taken: the folder of the user is next

        Assert.Equal(Path.Combine(_locations.Home, "bin", "textida"), link);
        Assert.Equal("another program", File.ReadAllText(foreign));
        integration.RemoveCommand();
        Assert.Equal("another program", File.ReadAllText(foreign));
        Assert.False(integration.HasCommand());

        Directory.CreateDirectory(Path.Combine(_locations.Home, "bin"));
        File.WriteAllText(Path.Combine(_locations.Home, "bin", "textida"), "mine, not yours");
        var again = Assert.Throws<IOException>(() => integration.AddCommand());
        Assert.Contains("does not lead to this program", again.Message);
        Assert.Equal("mine, not yours", File.ReadAllText(Path.Combine(_locations.Home, "bin", "textida")));
    }

    [Fact]
    public void The_quick_action_is_written_into_the_services_folder_pointing_at_the_bundle_and_taken_away_again()
    {
        var integration = Integration();
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_exe))), integration.BundlePath);
        Assert.False(integration.HasFinderAction());

        integration.AddFinderAction("Open in Textida");

        var folder = Path.Combine(_locations.Home, "Library", "Services", "Open in Textida.workflow");
        Assert.True(integration.HasFinderAction());
        MacBundleTests.AssertPropertyList(File.ReadAllText(Path.Combine(folder, "Contents", "Info.plist")));
        var workflow = File.ReadAllText(Path.Combine(folder, "Contents", "document.wflow"));
        MacBundleTests.AssertPropertyList(workflow);
        Assert.Contains(integration.BundlePath!.Replace("&", "&amp;"), workflow, StringComparison.Ordinal);

        integration.AddFinderAction("Otevřít v Textidě"); // again, in another language: the same folder, the new label
        Assert.Contains("Otevřít v Textidě", File.ReadAllText(Path.Combine(folder, "Contents", "Info.plist")), StringComparison.Ordinal);

        integration.RemoveFinderAction();
        Assert.False(Directory.Exists(folder));
        Assert.False(integration.HasFinderAction());
    }

    [Fact]
    public void An_action_of_that_name_that_Textida_did_not_write_is_left_alone_and_a_program_outside_a_bundle_has_no_action()
    {
        var foreign = Path.Combine(_locations.Home, "Library", "Services", "Open in Textida.workflow", "Contents");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "document.wflow"), "someone else's workflow");
        var integration = Integration();

        Assert.False(integration.HasFinderAction());
        Assert.Throws<IOException>(() => integration.AddFinderAction("Open in Textida"));
        integration.RemoveFinderAction();
        Assert.Equal("someone else's workflow", File.ReadAllText(Path.Combine(foreign, "document.wflow")));

        var loose = Integration(exe: Path.Combine(_temp.Path, "build", "Textida"));
        Assert.Null(loose.BundlePath);
        Assert.Throws<InvalidOperationException>(() => loose.AddFinderAction("Open in Textida"));
    }

    [Fact]
    public void An_association_makes_Textida_the_application_of_the_extension_and_switching_it_off_gives_back_the_one_it_had()
    {
        var services = new FakeLaunchServices();
        services.Handlers[".md"] = "com.apple.TextEdit";
        var integration = Integration(services);
        Assert.False(integration.IsAssociated(".md"));

        integration.SetAssociated(".md", true);

        Assert.Equal(MacBundle.BundleId, services.Handlers[".md"]);
        Assert.True(integration.IsAssociated(".md"));
        Assert.True(integration.IsAssociated("MD")); // a case and a dot do not matter
        Assert.Equal([".md"], integration.AssociatedExtensions());
        Assert.True(File.Exists(_locations.StateFile)); // what it had is remembered

        integration.SetAssociated(".md", false);

        Assert.Equal("com.apple.TextEdit", services.Handlers[".md"]);
        Assert.False(integration.IsAssociated(".md"));
        Assert.False(File.Exists(_locations.StateFile)); // nothing is left to remember
    }

    [Fact]
    public void An_extension_that_no_other_application_opens_stays_with_Textida_after_it_is_switched_off()
    {
        var services = new FakeLaunchServices(); // no handler for .cs at all
        var integration = Integration(services);

        integration.SetAssociated(".cs", true);
        integration.SetAssociated(".cs", false);

        Assert.Equal(MacBundle.BundleId, services.Handlers[".cs"]); // there is nobody to give it back to
    }

    [Fact]
    public void A_refusal_of_Launch_Services_is_an_error_and_remembers_nothing()
    {
        var services = new FakeLaunchServices { Refuse = true };
        services.Handlers[".log"] = "com.apple.Console";
        var integration = Integration(services);

        var refused = Assert.Throws<IOException>(() => integration.SetAssociated(".log", true));

        Assert.Contains("refused .log", refused.Message);
        Assert.Equal("com.apple.Console", services.Handlers[".log"]);
        Assert.False(File.Exists(_locations.StateFile));
    }

    [Fact]
    public void Remove_all_takes_back_the_command_the_action_and_every_association_and_leaves_nothing_but_the_settings()
    {
        var services = new FakeLaunchServices();
        services.Handlers[".md"] = "com.apple.TextEdit";
        services.Handlers[".json"] = "com.microsoft.VSCode";
        var integration = Integration(services);
        bool linked = CanLink();
        if (linked) integration.AddCommand();
        integration.AddFinderAction("Open in Textida");
        integration.SetAssociated(".md", true);
        integration.SetAssociated(".json", true);
        integration.SetAssociated(".txt", true);

        integration.RemoveAll();

        Assert.False(integration.HasCommand());
        Assert.False(integration.HasFinderAction());
        Assert.Equal("com.apple.TextEdit", services.Handlers[".md"]);
        Assert.Equal("com.microsoft.VSCode", services.Handlers[".json"]);
        Assert.Empty(integration.AssociatedExtensions().Except([".txt"])); // .txt had no other application
        Assert.False(File.Exists(_locations.StateFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_locations.Home, "Library", "Services")));
        if (linked) Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_locations.Home, "bin")));
        Assert.True(File.Exists(_exe));
    }
}

/// <summary>P3.7.8: the window of the integration and its commands.</summary>
[Collection("LocState")]
public class MacIntegrationWindowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly MacIntegrationTests.FakeLaunchServices _services = new();
    private readonly string _exe;

    public MacIntegrationWindowTests()
    {
        _exe = Path.Combine(_temp.Path, "Applications", "Textida.app", "Contents", "MacOS", "Textida");
        Directory.CreateDirectory(Path.GetDirectoryName(_exe)!);
        File.WriteAllText(_exe, "program");
        MacHost.LocationsOverride = new MacLocations { Home = Path.Combine(_temp.Path, "home"), SystemBinDir = Path.Combine(_temp.Path, "usr-local-bin"), RefreshServices = false };
        MacHost.ExeOverride = _exe;
        MacHost.LaunchServicesOverride = _services;
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        MacHost.LocationsOverride = null;
        MacHost.ExeOverride = null;
        MacHost.LaunchServicesOverride = null;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private MacIntegrationWindow Open()
    {
        Assert.Equal(CommandOutcome.Handled, _w.ShowMacIntegration());
        return _w.MacWindow!;
    }

    [AvaloniaFact]
    public void The_window_shows_the_three_groups_with_names_for_a_screen_reader_and_what_the_system_says()
    {
        Loc.SetLanguage("en");
        _services.Handlers[".md"] = MacBundle.BundleId; // associated before the window opened
        var window = Open();

        Assert.Equal("System integration (macOS)", window.Title);
        Assert.Equal(Loc.Get(K.A11yMacWindow), AutomationProperties.GetName(window));
        Assert.False(window.Section.CommandBox.IsChecked);
        Assert.False(window.Section.FinderBox.IsChecked);
        Assert.Equal(IntegrationExtensions.All, window.Section.Extensions.Select(e => e.Extension));
        Assert.Equal(Loc.Get(K.MacAssocItem, ("extension", ".md")), AutomationProperties.GetName(window.Section.Extensions.Single(e => e.Extension == ".md").Box));
        Assert.True(window.Section.Extensions.Single(e => e.Extension == ".md").Box.IsChecked);
        Assert.All(window.Section.Extensions.Where(e => e.Extension != ".md"), e => Assert.False(e.Box.IsChecked));
        Assert.Equal("", window.Section.NotInBundleText);
        Assert.Same(window, Open()); // a second command brings the same window
    }

    [AvaloniaFact]
    public void A_switch_writes_at_once_and_a_refusal_puts_it_back_with_the_reason()
    {
        Loc.SetLanguage("en");
        var window = Open();
        var folder = Path.Combine(_temp.Path, "home", "Library", "Services", "Open in Textida.workflow");

        window.Section.FinderBox.IsChecked = true;
        Assert.True(Directory.Exists(folder));
        Assert.Contains("Open in Textida", File.ReadAllText(Path.Combine(folder, "Contents", "Info.plist")));
        window.Section.FinderBox.IsChecked = false;
        Assert.False(Directory.Exists(folder));

        var markdown = window.Section.Extensions.Single(e => e.Extension == ".md").Box;
        _services.Handlers[".md"] = "com.apple.TextEdit";
        markdown.IsChecked = true;
        Assert.Equal(MacBundle.BundleId, _services.Handlers[".md"]);
        markdown.IsChecked = false;
        Assert.Equal("com.apple.TextEdit", _services.Handlers[".md"]);

        _services.Refuse = true;
        markdown.IsChecked = true;
        Assert.False(markdown.IsChecked); // back where it was
        Assert.Equal("macOS refused the change: Launch Services refused .md (error -54).", window.Section.ProblemText);
        _services.Refuse = false;
        markdown.IsChecked = true;
        Assert.Equal("", window.Section.ProblemText);
    }

    [AvaloniaFact]
    public void Remove_all_gives_everything_back_and_says_so_and_the_command_does_the_same_without_the_window()
    {
        Loc.SetLanguage("en");
        _services.Handlers[".json"] = "com.apple.TextEdit";
        var window = Open();
        window.Section.FinderBox.IsChecked = true;
        window.Section.Extensions.Single(e => e.Extension == ".json").Box.IsChecked = true;

        window.Section.RemoveAllButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

        Assert.False(window.Section.FinderBox.IsChecked);
        Assert.False(window.Section.Extensions.Single(e => e.Extension == ".json").Box.IsChecked);
        Assert.Equal("com.apple.TextEdit", _services.Handlers[".json"]);
        Assert.Equal(Loc.Get(K.MacRemoved), window.Section.ResultText);

        window.Section.FinderBox.IsChecked = true;
        Assert.Equal(CommandOutcome.Handled, _w.RemoveMacIntegration());
        Assert.False(window.Section.FinderBox.IsChecked);
        Assert.Equal(Loc.Get(K.MacRemoved), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void A_program_outside_a_bundle_offers_only_the_command_and_says_so()
    {
        Loc.SetLanguage("en");
        MacHost.ExeOverride = Path.Combine(_temp.Path, "build", "Textida");
        var window = Open();

        Assert.Equal(Loc.Get(K.MacNotInBundle), window.Section.NotInBundleText);
        Assert.False(window.Section.FinderBox.IsEnabled);
        Assert.All(window.Section.Extensions, e => Assert.False(e.Box.IsEnabled));
        Assert.True(window.Section.CommandBox.IsEnabled);
    }

    [AvaloniaFact]
    public void The_window_follows_the_language_and_the_commands_are_registered_for_macOS_only()
    {
        Loc.SetLanguage("en");
        var window = Open();
        Loc.SetLanguage("cs");
        window.ApplyTexts();

        Assert.Equal("Integrace do systému (macOS)", window.Title);
        Assert.Equal("Příkaz textida v terminálu", window.Section.CommandBox.Content);
        Assert.Equal("Odebrat vše", window.Section.RemoveAllButton.Content);

        var registry = _app.Workbench.Commands;
        foreach (var id in new[] { "nks.tools.macIntegration", "nks.tools.macRemove" })
        {
            Assert.True(registry.HasHandler(id), id);
            var enablement = registry.Commands.Single(c => c.Id == id).Enablement!;
            Assert.True(enablement.Evaluate(new WhenContext().Set(ContextKeys.Platform, "macos")));
            Assert.False(enablement.Evaluate(new WhenContext().Set(ContextKeys.Platform, "windows")));
        }
    }

    [AvaloniaFact]
    public void Without_a_program_to_point_at_the_commands_say_that_they_are_for_macOS()
    {
        Loc.SetLanguage("en");
        MacHost.ExeOverride = null; // this is not a Mac
        if (OperatingSystem.IsMacOS()) return;

        Assert.Equal(CommandOutcome.Handled, _w.ShowMacIntegration());
        Assert.Equal("This command is for macOS only.", _w.StatusMessageText);
        Assert.Null(_w.MacWindow);
    }
}

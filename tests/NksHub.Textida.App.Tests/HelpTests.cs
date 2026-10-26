using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>The offline help covers every registered command in every UI language, so a new command fails here until it is documented.</summary>
public class HelpCatalogTests
{
    private static IReadOnlyList<string> CommandIds() => [.. WorkbenchHost.CreateBuiltin().Commands.Commands.Select(c => c.Id)];

    public static IEnumerable<object[]> Languages => StringTable.UiLanguages.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_registered_command_has_a_help_topic(string language)
    {
        var missing = CommandIds().Where(id => HelpCatalog.ForCommand(language, id) is null).ToList();
        Assert.True(missing.Count == 0, $"no help topic in '{language}' for: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Help_documents_only_commands_that_exist(string language)
    {
        var known = CommandIds().ToHashSet();
        var unknown = HelpCatalog.Topics(language).SelectMany(t => t.Commands).Where(c => !known.Contains(c)).ToList();
        Assert.True(unknown.Count == 0, $"help '{language}' names commands that are not registered: {string.Join(", ", unknown)}");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Topics_are_complete_and_ids_are_unique(string language)
    {
        var topics = HelpCatalog.Topics(language);
        Assert.Equal(topics.Count, topics.Select(t => t.Id).Distinct().Count());
        Assert.All(topics, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Title), t.Id);
            Assert.True(t.Lines.Count > 0, t.Id);
        });
        Assert.NotNull(HelpCatalog.Find(language, HelpCatalog.StartTopic));
    }

    [Fact]
    public void All_languages_have_the_same_topics_with_the_same_commands()
    {
        var reference = HelpCatalog.Topics(Loc.DefaultLanguage);
        foreach (var language in StringTable.UiLanguages)
        {
            var topics = HelpCatalog.Topics(language);
            Assert.Equal(reference.Select(t => t.Id), topics.Select(t => t.Id));
            Assert.Equal(reference.Select(t => string.Join(',', t.Commands)), topics.Select(t => string.Join(',', t.Commands)));
        }
    }

    [Fact]
    public void Topics_the_code_jumps_to_exist()
    {
        foreach (var id in new[] { "start", "view.palette", "search.gotoAnything", "search.jump", "search.find", "search.replace", "search.inFiles", "search.regex", "view.files", "view.documents", "app.settings", "paged" })
            Assert.All(StringTable.UiLanguages, l => Assert.NotNull(HelpCatalog.Find(l, id)));
    }

    [Fact]
    public void Search_ignores_accents_and_case_and_needs_every_word()
    {
        Assert.Contains(HelpCatalog.Search("cs", "ULOZIT"), t => t.Id == "file.save");
        Assert.Contains(HelpCatalog.Search("de", "zeilenumbruch"), t => t.Id == "view.wordwrap");
        Assert.Empty(HelpCatalog.Search("en", "zzzzqqq"));
        Assert.Equal(HelpCatalog.Topics("en").Count, HelpCatalog.Search("en", "  ").Count);
        Assert.All(HelpCatalog.Search("en", "save backup"), t => Assert.Contains("backup", t.Haystack));
    }

    [Fact]
    public void Parser_reads_headings_commands_and_lines()
    {
        var topics = HelpCatalog.Parse("ignored\n## [a.b] Title one\ncommands: x.y, z.w\n\nline 1\n- bullet\n## [c] Two\nbody\n");
        Assert.Equal(2, topics.Count);
        Assert.Equal(("a.b", "Title one"), (topics[0].Id, topics[0].Title));
        Assert.Equal(["x.y", "z.w"], topics[0].Commands);
        Assert.Equal(["line 1", "- bullet"], topics[0].Lines);
        Assert.Empty(topics[1].Commands);
    }
}

/// <summary>F1 and the Help menu open the help window at the topic of where the user is.</summary>
[Collection("LocState")]
public class HelpWindowTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HelpWindowTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        _app.HelpView?.Close();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void F1_opens_the_start_page_and_the_help_menu_has_the_command()
    {
        Assert.Contains("nks.help.contents", _app.Workbench.Commands.Commands.Select(c => c.Id));
        TestApp.Press(_w, Key.F1);
        Assert.NotNull(_app.HelpView);
        Assert.Equal(HelpCatalog.StartTopic, _app.HelpView!.CurrentTopic);
        var help = _w.MenuModel.Single(g => g.Label == "Nápověda");
        Assert.Contains(help.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.help.contents");
    }

    [AvaloniaFact]
    public void F1_in_the_palette_opens_the_topic_of_the_selected_command()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("uložit jako");
        Assert.Equal("nks.file.saveAs", _w.PaletteResults[0].CommandId);
        TestApp.Press(_w, Key.F1);
        Assert.False(_w.IsPaletteOpen);
        Assert.Equal("file.saveAs", _app.HelpView!.CurrentTopic);
        Assert.Contains("Ctrl+Shift+S", _app.HelpView.BodyText);
    }

    [AvaloniaFact]
    public void Quick_open_has_its_own_topic_and_a_second_F1_reuses_the_window()
    {
        _w.ShowQuickOpen();
        Assert.Equal("search.gotoAnything", _w.HelpContextTopic());
        TestApp.Press(_w, Key.F1);
        var window = _app.HelpView;
        Assert.Equal("search.gotoAnything", window!.CurrentTopic);
        _app.OpenHelp("file.new");
        Assert.Same(window, _app.HelpView);
        Assert.Equal("file.new", window.CurrentTopic);
        _app.OpenHelp("no.such.topic");
        Assert.Equal(HelpCatalog.StartTopic, window.CurrentTopic);
    }

    [AvaloniaFact]
    public void Search_filters_the_topic_list_and_the_language_switch_translates_the_text()
    {
        _app.OpenHelp();
        var window = _app.HelpView!;
        window.Search("zalamování");
        Assert.Contains("Zalamování řádků", window.ShownTitles);
        Assert.True(window.ShownTitles.Count < HelpCatalog.Topics("cs").Count);
        window.Search("");
        _app.OpenHelp("view.wordwrap");
        Loc.SetLanguage("de");
        Assert.Equal("view.wordwrap", window.CurrentTopic);
        Assert.Contains("Zeilenumbruch", window.BodyText);
    }
}

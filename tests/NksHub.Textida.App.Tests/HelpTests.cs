using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Tools;
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

    private static readonly Regex NumberedStep = new(@"^\d+\. ");

    /// <summary>One letter per line of a topic: H subheading, B bullet, N numbered step, P paragraph. Topics of two languages must have the same shape.</summary>
    private static string Shape(HelpTopic topic) =>
        string.Concat(topic.Lines.Select(l => l.StartsWith("### ", StringComparison.Ordinal) ? 'H' : l.StartsWith("- ", StringComparison.Ordinal) ? 'B' : NumberedStep.IsMatch(l) ? 'N' : 'P'));

    [Fact]
    public void All_languages_have_the_same_structure_in_every_topic()
    {
        var reference = HelpCatalog.Topics(Loc.DefaultLanguage).ToDictionary(t => t.Id, Shape);
        var problems = new List<string>();
        foreach (var language in StringTable.UiLanguages)
        {
            foreach (var topic in HelpCatalog.Topics(language))
            {
                if (!reference.TryGetValue(topic.Id, out var shape)) problems.Add($"{language}:{topic.Id} is not in the default language");
                else if (Shape(topic) != shape) problems.Add($"{language}:{topic.Id} is {Shape(topic)}, the default language is {shape}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("de")]
    public void Help_is_translated_not_copied_from_english(string language)
    {
        var english = HelpCatalog.Topics("en").ToDictionary(t => t.Id);
        var copies = new List<string>();
        foreach (var topic in HelpCatalog.Topics(language))
        {
            for (int i = 0; i < topic.Lines.Count; i++)
            {
                var line = topic.Lines[i];
                if (english[topic.Id].Lines[i] == line && Regex.IsMatch(Regex.Replace(line, "`[^`]*`", ""), @"\p{L}{3,}")) copies.Add($"{language}:{topic.Id}:{i + 1} {line}");
            }
        }
        Assert.True(copies.Count == 0, "lines left in English:\n" + string.Join("\n", copies));
    }

    [Theory]
    [InlineData("cs", "áčďéěíňóřšťúůýž")]
    [InlineData("sk", "áäčďéíĺľňóôŕšťúýž")]
    public void Czech_and_Slovak_help_is_written_with_diacritics(string language, string letters)
    {
        // a line of seven or more plain ASCII words with none of the language's letters was written without diacritics
        var plain = new List<string>();
        foreach (var topic in HelpCatalog.Topics(language))
            foreach (var line in topic.Lines)
            {
                var words = Regex.Replace(line, "`[^`]*`", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => Regex.IsMatch(w, "^[A-Za-z]{2,}$"));
                if (words >= 7 && !line.Any(c => letters.Contains(char.ToLowerInvariant(c)))) plain.Add($"{topic.Id}: {line}");
            }
        Assert.True(plain.Count == 0, string.Join("\n", plain));
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Repo.AppSources, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static readonly Regex[] TopicReferences =
    [
        new(@"HelpContext\.(?:Set|OnF1)\([^;]*?,\s*""([A-Za-z0-9.]+)""\)"),
        new(@"[hH]elpTopic: ""([A-Za-z0-9.]+)"""),
        new(@"\bFrame\([^;]*?,\s*""([A-Za-z0-9.]+)""\)"),
        new(@"OpenHelp\(""([A-Za-z0-9.]+)"""),
    ];

    private static readonly Regex TopicArm = new(@"(?:=>|return) ""([a-z][A-Za-z0-9.]*)""");

    [Fact]
    public void Every_topic_the_code_names_for_F1_exists_in_every_language()
    {
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in TopicReferences) foreach (Match m in pattern.Matches(text)) named.Add(m.Groups[1].Value);
            // the chain of F1 in the main window and the kinds of window-level bars name their topics in switch arms
            if (Path.GetFileName(file) == "MainWindow.Help.cs") foreach (Match m in TopicArm.Matches(text)) named.Add(m.Groups[1].Value);
            if (Path.GetFileName(file) == "MainWindow.Files.cs" && text.IndexOf("NoticeTopic(string key)", StringComparison.Ordinal) is var at and >= 0)
                foreach (Match m in TopicArm.Matches(text[at..text.IndexOf("};", at, StringComparison.Ordinal)])) named.Add(m.Groups[1].Value);
        }
        Assert.True(named.Count >= 40, $"only {named.Count} topics found in the code: {string.Join(", ", named)}");
        var missing = named.SelectMany(id => StringTable.UiLanguages.Where(l => HelpCatalog.Find(l, id) is null).Select(l => $"{l}:{id}")).ToList();
        Assert.True(missing.Count == 0, "F1 names topics that do not exist: " + string.Join(", ", missing));
    }

    [Fact]
    public void Help_describes_errors_recovery_and_limits_of_the_features_of_1_0()
    {
        foreach (var id in new[] { "errors.file", "errors.files", "errors.search", "errors.remote", "errors.plugins", "errors.agents", "errors.tools", "recovery", "limits", "paged", "help.updates" })
            Assert.All(StringTable.UiLanguages, l => Assert.NotNull(HelpCatalog.Find(l, id)));
    }

    private static string FirstSentence(string template)
    {
        var m = Regex.Match(template, @"^(.+?[.!?])(\s+[A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽÄÖÜ„""(]|$)", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : template;
    }

    private static Regex MessagePattern(string template) => new("^" + Regex.Replace(Regex.Escape(template.Trim()), @"\\\{[A-Za-z]+\}", ".+?"), RegexOptions.Singleline);

    /// <summary>The lines of the error topics quote the messages of the program; a message that is reworded without the help fails here.</summary>
    [Theory]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("sk")]
    [InlineData("de")]
    public void Error_topics_quote_the_messages_the_program_shows(string language)
    {
        int column = Array.IndexOf(new[] { "cs", "en", "sk", "de" }, language);
        var templates = Loc.Catalog(language).Values.Concat(TranslationGateTests.PluginRows().Where(r => r.Columns.Length == 4).Select(r => r.Columns[column])).ToList();
        var patterns = templates.SelectMany(t => new[] { t, FirstSentence(t) }).Distinct().Select(MessagePattern).ToList();

        var updates = HelpCatalog.Find(language, "help.updates")!.Lines;
        var bullets = new[] { "errors.remote", "errors.plugins", "errors.agents", "errors.tools" }.SelectMany(id => HelpCatalog.Find(language, id)!.Lines.Select(l => (id, l)))
            .Concat(updates.Skip(updates.ToList().FindLastIndex(l => l.StartsWith("### ", StringComparison.Ordinal)) + 1).Select(l => ("help.updates", l)))
            .Where(x => x.Item2.StartsWith("- ", StringComparison.Ordinal)).ToList();
        Assert.True(bullets.Count >= 70, $"{language}: only {bullets.Count} quoted messages");
        var unknown = bullets.Where(b => !patterns.Any(p => p.IsMatch(b.Item2[2..]))).Select(b => $"{language}:{b.Item1}: {b.Item2}").ToList();
        Assert.True(unknown.Count == 0, "help quotes messages the program does not have:\n" + string.Join("\n", unknown));
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

/// <summary>P9.2.3: F1 opens the topic that fits in every window, dialog, panel and view of the program.</summary>
[Collection("LocState")]
public class HelpEverywhereTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly AvaloniaDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HelpEverywhereTests()
    {
        _app = TestApp.Prepare(_temp);
        _app.Dialogs = _dialogs;
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static T Show<T>(T window) where T : Window
    {
        window.Show();
        return window;
    }

    /// <summary>Presses F1 in <paramref name="window"/> and returns the topic the help window opened at; the help window is closed again.</summary>
    private string F1In(Window window)
    {
        _app.HelpView?.Close();
        TestApp.Press(window, Key.F1);
        var topic = _app.HelpView?.CurrentTopic ?? "(the help did not open)";
        _app.HelpView?.Close();
        return topic;
    }

    private DocTab OpenHex()
    {
        var bytes = new byte[4000];
        new Random(7).NextBytes(bytes);
        bytes[0] = 0;
        _w.OpenFile(_temp.Bytes("a.bin", bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        return _w.Active;
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Every_command_the_palette_offers_has_a_topic_in_the_running_program()
    {
        var registry = _app.Workbench.Commands;
        var missing = registry.Commands.Where(c => !c.Hidden && registry.HasHandler(c.Id))
            .SelectMany(c => StringTable.UiLanguages.Where(l => HelpCatalog.ForCommand(l, c.Id) is null).Select(l => $"{l}:{c.Id}")).ToList();
        Assert.True(missing.Count == 0, "palette commands without a help topic: " + string.Join(", ", missing));
    }

    [AvaloniaFact]
    public void F1_in_each_window_opens_the_topic_about_that_window()
    {
        // one window at a time: the keyboard belongs to the window shown last, so a key pressed "in" an older window would reach the newest one
        var windows = new List<(string Topic, Func<Window> Open)>
        {
            ("app.settings", () => { _app.OpenSettings(); return _app.SettingsView!; }),
            ("search.regex", () => Show(new RegexTesterWindow(new SearchPattern(@"(\w+) (\w+)", true)))),
            ("tools.logPattern", () => Show(new LogPatternWindow("%F(%L,%C)", LogPatternSyntax.Mask, "a.cs(1,2): error"))),
            ("tools.edit", () => Show(new ToolEditorWindow(_app.Tools, _w.PreviewTool))),
        };
        foreach (var (topic, open) in windows)
        {
            var window = open();
            try { Assert.Equal(topic, F1In(window)); }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void F1_in_the_help_window_opens_the_topic_about_the_help()
    {
        _app.OpenHelp("file.new");
        TestApp.Press(_app.HelpView!, Key.F1);
        Assert.Equal("help.contents", _app.HelpView!.CurrentTopic);
    }

    [AvaloniaFact]
    public void F1_in_the_binary_comparison_opens_its_topic()
    {
        var window = _w.CompareHexWithSaved(OpenHex())!;
        Assert.NotNull(window);
        try { Assert.Equal("hex.compare", F1In(window)); }
        finally { window.Close(); }
    }

    /// <summary>Starts a dialog (it stays open until closed), presses F1 in it and closes it. <paramref name="open"/> returns the task of the dialog, if it has one.</summary>
    private async Task<string> F1InDialog(Func<object?> open)
    {
        Window? dialog = null;
        void Capture(Window w) => dialog = w;
        AvaloniaDialogs.DialogCreated += Capture;
        object? pending;
        try { pending = open(); }
        finally { AvaloniaDialogs.DialogCreated -= Capture; }
        Assert.NotNull(dialog);
        var topic = F1In(dialog!);
        dialog!.Close();
        if (pending is Task task) await task;
        else Dispatcher.UIThread.RunJobs();
        return topic;
    }

    [AvaloniaFact]
    public async Task F1_in_a_dialog_opens_the_topic_the_dialog_names()
    {
        (TextCodec, int)[] targets = [(TextCodec.Utf8, 0)];
        Assert.Equal("file.saveAs", await F1InDialog(() => _dialogs.SaveAsAsync(_w, new SaveAsRequest(null, TextCodec.Utf8, targets))));
        Assert.Equal("format.encoding", await F1InDialog(() => _dialogs.PickEncodingAsync(_w, "t", "l", targets, TextCodec.Utf8)));
        Assert.Equal("help.about", await F1InDialog(() => _dialogs.ShowAboutAsync(_w)));
        Assert.Equal("hex.bookmarks", await F1InDialog(() => _dialogs.HexBookmarkAsync(_w, new HexBookmarkForm("n", null, null, null, ""))));
        Assert.Equal("edit.insertLines", await F1InDialog(() => _dialogs.InsertIntoLinesAsync(_w, new LineInsertOptions(), _ => "")));
        Assert.Equal("edit.sort", await F1InDialog(() => _dialogs.SortLinesAsync(_w, new SortOptions(), _ => "")));
        Assert.Equal("table.region", await F1InDialog(() => _dialogs.TableRegionAsync(_w, CsvDialect.Csv, _ => "")));
        var question = new ChoiceRequest("t", "m", [new DialogButton("OK", IsDefault: true, IsCancel: true)], HelpTopic: "conflict");
        Assert.Equal("conflict", await F1InDialog(() => _dialogs.ChooseAsync(_w, question)));
    }

    [AvaloniaFact]
    public async Task F1_in_a_dialog_that_a_command_opened_opens_the_topic_of_that_command()
    {
        _w.NewDocument();
        var topic = await F1InDialog(() =>
        {
            _ = _app.Workbench.Commands.ExecuteAsync("nks.search.gotoLine", new ShellContext(_w, forMenu: true));
            return null;
        });
        Assert.Equal("search.gotoLine", topic);
    }

    [AvaloniaFact]
    public async Task F1_in_a_dialog_without_a_topic_opens_the_topic_of_where_the_user_is()
    {
        _w.ShowQuickOpen();
        var question = new ChoiceRequest("t", "m", [new DialogButton("OK", IsDefault: true, IsCancel: true)]);
        Assert.Equal("search.gotoAnything", await F1InDialog(() => _dialogs.ChooseAsync(_w, question)));
    }

    [AvaloniaFact]
    public void F1_in_a_panel_opens_the_topic_of_that_panel()
    {
        _w.ToggleOutput(true);
        Dispatcher.UIThread.RunJobs();
        _w.OutputPanelControl.GetVisualDescendants().OfType<TextBox>().First().Focus();
        Assert.Equal("view.output", _w.HelpContextTopic());
        _w.ToggleOutput(false);

        _w.ToggleAgentPanel(true);
        Dispatcher.UIThread.RunJobs();
        _w.AgentInputBox.Focus();
        Assert.Equal("tools.agent", _w.HelpContextTopic());
        TestApp.Press(_w, Key.F1);
        Assert.Equal("tools.agent", _app.HelpView!.CurrentTopic);
        _w.ToggleAgentPanel(false);

        Assert.Equal("tools.markdown", HelpContext.Of(_w.PreviewPanelControl));
        Assert.Equal("view.tabs", HelpContext.Of(_w.ActiveGroup.Strip));
        Assert.Equal("statusbar", HelpContext.Of(_w.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("statusbar"))));
    }

    [AvaloniaFact]
    public void F1_in_a_bar_above_the_document_opens_the_topic_of_that_bar()
    {
        Assert.Equal("recovery", HelpContext.Of(_w.ShowNotice("restore", "m")));
        Assert.Equal("help.updates", HelpContext.Of(_w.ShowNotice("update", "m")));
        Assert.Equal("errors.plugins", HelpContext.Of(_w.ShowNotice("plugin.crash:x", "m")));
        Assert.Null(HelpContext.Of(_w.ShowNotice("something.else", "m")));
    }

    [AvaloniaFact]
    public void F1_in_a_view_of_a_tab_opens_the_topic_of_that_view()
    {
        var hex = OpenHex().Hex!;
        hex.Focus();
        Assert.Equal("hex.open", HelpContext.Of(hex));
        Assert.Equal("hex.open", _w.HelpContextTopic());

        var compare = _w.CompareFiles(_temp.File("left.txt", "one\ntwo\n"), _temp.File("right.txt", "one\n2\n"));
        Assert.Equal("compare.text", HelpContext.Of((Control)compare.Custom!));

        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1)); // MiB: the file below is larger
        var paged = _w.OpenFile(_temp.Bytes("big.log", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 150_000).Select(i => $"line {i}\n")))), null);
        Assert.NotNull(paged.Paged);
        Assert.Equal("paged", HelpContext.Of(paged.Paged!));

        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(6));
        var csv = _w.OpenFile(_temp.File("data.csv", "id,name\n1,Ann\n"), null);
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.table"));
        Assert.Equal("table.view", HelpContext.Of(_w.ActiveTable!));
        Assert.Same(csv, _w.Active);
    }
}

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

/// <summary>The offline help covers every registered command, the commands of the bundled plugins included, in every UI language, so a new command fails here until it is documented.</summary>
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

    private static bool AnyLine(string language, string commandId, Func<string, bool> test) =>
        HelpCatalog.Topics(language).Where(t => t.Commands.Contains(commandId)).SelectMany(t => t.Lines).Any(test);

    /// <summary>P9.2.3: every command has a procedure (numbered steps) and a bullet (a limit, a restriction, or a message and what to do) in a topic of every language.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_command_has_a_procedure_and_a_bullet_of_limits_or_failures(string language)
    {
        var ids = CommandIds();
        var withoutSteps = ids.Where(id => !AnyLine(language, id, l => NumberedStep.IsMatch(l))).ToList();
        var withoutBullets = ids.Where(id => !AnyLine(language, id, l => l.StartsWith("- ", StringComparison.Ordinal))).ToList();
        Assert.True(withoutSteps.Count == 0, $"no numbered steps in '{language}' for: {string.Join(", ", withoutSteps)}");
        Assert.True(withoutBullets.Count == 0, $"no bullet in '{language}' for: {string.Join(", ", withoutBullets)}");
    }

    /// <summary>The commands a bundled plugin runs are read from the source of the commands, so a command that is added there cannot go without a topic.</summary>
    private static IReadOnlyList<string> BundledPluginCommands() =>
        [.. Regex.Matches(File.ReadAllText(Path.Combine(Repo.AppSources, "Shell", "AppCommands.cs")), @"Fire\(""(nks\.[A-Za-z0-9.]+)"",[^\r\n;]*?\bapp\.Plugins\.")
            .Select(m => m.Groups[1].Value).Distinct()];

    [Fact]
    public void Commands_of_the_bundled_plugins_are_registered_and_documented_in_every_language()
    {
        var found = BundledPluginCommands();
        Assert.True(found.Count >= 40, $"only {found.Count} commands of the bundled plugins were found in AppCommands.cs");
        var registered = CommandIds().ToHashSet();
        Assert.True(found.All(registered.Contains), "commands of plugins that are not registered: " + string.Join(", ", found.Where(id => !registered.Contains(id))));

        // one command of every bundled plugin: a plugin that is added to the editor fails here until it has a command in the registry and in the help
        var examples = new Dictionary<string, string>
        {
            [PluginServices.JsonPluginId] = "nks.edit.formatJson", [PluginServices.TextToolsPluginId] = "nks.tools.statistics", [PluginServices.WebDataPluginId] = "nks.tools.colorToHex",
            [PluginServices.CodeExplorerPluginId] = "nks.tools.showOutline", [PluginServices.EmmetPluginId] = "nks.tools.emmetWrap", [PluginServices.FormattersPluginId] = "nks.edit.formatSql",
            [PluginServices.GitPluginId] = "nks.git.status", [PluginServices.LspPluginId] = "nks.lsp.start", [PluginServices.WikiPluginId] = "nks.source.open",
        };
        Assert.Equal(PluginServices.BuiltIns.Select(b => b.Id).Order(StringComparer.Ordinal), examples.Keys.Order(StringComparer.Ordinal));

        var problems = new List<string>();
        foreach (var language in StringTable.UiLanguages)
            foreach (var id in found.Concat(examples.Values).Distinct())
            {
                if (!registered.Contains(id)) problems.Add($"{id} is not registered");
                else if (HelpCatalog.ForCommand(language, id) is null) problems.Add($"{language}: {id} has no topic");
                else if (!AnyLine(language, id, l => NumberedStep.IsMatch(l))) problems.Add($"{language}: {id} has no numbered steps");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Plugins, remote files, MCP, agents and large files: each topic has steps and a bullet, the large files have what works, what does not and the messages.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Plugins_remote_files_MCP_agents_and_large_files_have_procedures_limits_and_messages(string language)
    {
        string[] procedures =
        [
            "plugins.manager", "plugins.lifecycle", "plugins.virtual", "remote.connections", "remote.open", "remote.upload", "remote.cancel", "remote.check", "remote.unsent", "remote.cache",
            "source.open", "tools.mcp", "tools.ide", "tools.agent", "tools.mcpAgents", "recovery",
        ];
        var problems = new List<string>();
        foreach (var id in procedures)
        {
            var topic = HelpCatalog.Find(language, id);
            if (topic is null) problems.Add($"{id} is missing");
            else if (!topic.Lines.Any(l => NumberedStep.IsMatch(l)) || !topic.Lines.Any(l => l.StartsWith("- ", StringComparison.Ordinal))) problems.Add($"{id} has no steps or no bullet");
        }
        var paged = HelpCatalog.Find(language, "paged");
        if (paged is null || paged.Lines.Count(l => l.StartsWith("### ", StringComparison.Ordinal)) < 3) problems.Add("paged has less than the three subheadings: what works, what does not, messages");
        Assert.True(problems.Count == 0, $"{language}: " + string.Join("; ", problems));
    }

    /// <summary>The failures of the commands of 1.0 (files, remote files and sources, plugins, MCP, agents, large files, packages, schemas), by the key of the text of the program.</summary>
    private static readonly string[] QuotedMessages =
    [
        "agent.version.unknown", "agent.version.unverified", "convert.failed", "favorite.exists", "favorite.nothing", "ide.notOn", "inspect.editorConfigDone",
        "inspect.editorConfigNone", "inspect.noBytes", "lines.dateFormat.invalid", "lines.insertFile.tooLarge", "lines.pasteHtml.none",
        "lines.reformat.noMargin", "lock.instance", "lock.instanceOwner", "lock.still", "lock.system", "log.fold.dimmed", "log.fold.nothing",
        "log.stack.none", "log.stack.notFound", "loss.message.other", "mcp.agents.none", "mcp.agents.problems", "mcp.client.notSaved", "mcp.client.nothing",
        "mcp.client.resultError", "packages.busy", "packages.error", "packages.notManifest", "packages.script.none", "packages.script.notManifest",
        "packages.tooLarge", "paged.copyTooLarge", "paged.editTooLarge", "paged.lineTooLong", "paged.pasteEncoding", "paged.pasteTooLarge",
        "plugins.error.duplicateEntry", "plugins.error.symbolicLink", "plugins.error.tooManyEntries", "plugins.keys.overridden", "plugins.keys.prefix",
        "plugins.row.problem", "plugins.status.failed", "project.needsFolder", "project.noFolder", "project.none", "project.openedMissing",
        "project.settingsBroken", "remote.bar.offline", "remote.checkFailed", "remote.conflict.deleted", "remote.conflict.message", "remote.connectionGone",
        "remote.copyMissing", "remote.noConnections", "remote.noTransfer", "remote.notRemote", "remote.trust.invalid", "remote.unsent.empty", "review.busy",
        "review.noBase", "review.none", "review.notSaved", "schema.notFile", "schema.nothingToAllow", "schema.unavailable", "schema.unsupported",
        "session.nothing", "session.openedMissing", "source.conflict.gone", "source.conflict.message", "source.conflict.unreachable", "source.none",
        "source.search.failed", "source.search.none", "special.launchFailed", "special.noClosed", "special.notFile", "status.convertFailed",
        "status.nothingToChange", "table.copyAs.failed", "table.copyAs.tooLarge", "virtual.readOnly", "virtual.saveAsRefused"
    ];

    /// <summary>P9.2.3: a message of the program starts a bullet of the help word for word (a value in it as a word), so the help never describes a message that was reworded or invented.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Failure_messages_of_the_features_of_1_0_are_quoted_in_the_help(string language)
    {
        var catalog = Loc.Catalog(language);
        var bullets = HelpCatalog.Topics(language).SelectMany(t => t.Lines).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();
        var unknown = QuotedMessages.Where(k => !catalog.ContainsKey(k)).ToList();
        Assert.True(unknown.Count == 0, $"{language}: no such text in the program: {string.Join(", ", unknown)}");
        var notQuoted = QuotedMessages.Where(key =>
        {
            var patterns = new[] { catalog[key], FirstSentence(catalog[key]) }.Select(MessagePattern).ToList();
            return !bullets.Any(b => patterns.Any(p => p.IsMatch(b)));
        }).ToList();
        Assert.True(notQuoted.Count == 0, $"{language}: the help does not quote: {string.Join(", ", notQuoted)}");
    }

    /// <summary>P9.2.3: the help is read without a network. It is part of the program, its text holds no link, image or markup that loads something, and the code that shows it fetches and starts nothing.</summary>
    [Fact]
    public void Help_needs_no_network_to_be_read()
    {
        var resources = typeof(HelpCatalog).Assembly.GetManifestResourceNames();
        Assert.All(StringTable.UiLanguages, l => Assert.Contains($"NksHub.Textida.App.Help.help.{l}.md", resources));

        var markup = new Regex(@"!\[|\]\(|<\s*/?\s*(?:a|img|iframe|script|link|video|audio|source|object|embed|style)\b[^>]*>|\bsrc\s*=|\bhref\s*=|\burl\(|(?:https?|ftp|file)://[A-Za-z0-9]|\bwww\.", RegexOptions.IgnoreCase);
        var remote = StringTable.UiLanguages.SelectMany(l => HelpCatalog.Topics(l).SelectMany(t => t.Lines.Select(line => (Place: $"{l}:{t.Id}", Line: line))))
            .Where(x => markup.IsMatch(Regex.Replace(x.Line, "`[^`]*`", ""))).Select(x => $"{x.Place}: {x.Line}").ToList();
        Assert.True(remote.Count == 0, "the help holds links, images or markup that loads something:\n" + string.Join("\n", remote));

        var fetching = new Regex(@"\b(?:HttpClient|WebClient|WebView\w*|Process|Launcher|Bitmap|Image|HyperlinkButton|Socket|Uri)\b|https?://");
        var code = new[] { Path.Combine("Help", "HelpCatalog.cs"), Path.Combine("Help", "HelpContext.cs"), Path.Combine("Shell", "HelpWindow.cs"), Path.Combine("Shell", "MainWindow.Help.cs") };
        var network = code.SelectMany(f => File.ReadAllLines(Path.Combine(Repo.AppSources, f)).Select((line, i) => (File: f, Line: i + 1, Text: line)))
            .Where(x => fetching.IsMatch(x.Text)).Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}").ToList();
        Assert.True(network.Count == 0, "the code of the help touches the network, a process or an image:\n" + string.Join("\n", network));
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
            ("view.toolbar", () => Show(new ToolbarEditorWindow(_app.Workbench, _app.Toolbar))),
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

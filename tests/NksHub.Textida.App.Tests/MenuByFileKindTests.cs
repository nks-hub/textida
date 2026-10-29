using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.6.16: the items of the menu follow the kind of the open file. For each kind (plain text, code of a language, JSON, YAML, CSV as text and as a table, Markdown in the editor and as a page, HTML, log, picture,
/// hex, a binary file, a file in a Git repository) the table says which items are shown and available (S), shown and grey (G) or not shown at all (-); the test reads the real menu of the window.
/// </summary>
[Collection("LocState")]
public class MenuByFileKindTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public MenuByFileKindTests()
    {
        RepositoryProbe.Forget();
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>The columns of the table: a short name and the command it stands for.</summary>
    private static readonly Dictionary<string, string> Columns = new()
    {
        ["json"] = "nks.edit.formatJson", ["sql"] = "nks.edit.formatSql", ["tidy"] = "nks.edit.htmlTidy", ["closeTag"] = "nks.edit.htmlCloseTag", ["toc"] = "nks.tools.markdownUpdateToc",
        ["cycle"] = "nks.view.cycleMode", ["keyPath"] = "nks.tools.copyKeyPath", ["outline"] = "nks.tools.showOutline", ["hex"] = "nks.hex.inspector", ["row"] = "nks.table.insertRow",
        ["next"] = "nks.compare.next", ["git"] = "nks.git.diffHead", ["lsp"] = "nks.lsp.goToDefinition", ["lint"] = "nks.tools.lint", ["unfold"] = "nks.log.unfold",
        ["upload"] = "nks.remote.upload", ["reload"] = "nks.file.reload", ["comment"] = "nks.edit.toggleComment", ["read"] = "nks.view.modeRead", ["pkg"] = "nks.packages.lookup",
    };

    /// <summary>One row per kind of file; a column that is not named is not shown.</summary>
    public static IEnumerable<object[]> Table()
    {
        yield return ["untitled", "json:G sql:G reload:G comment:S"];
        yield return ["text", "json:G sql:G reload:S comment:S"];
        yield return ["code", "json:G sql:G outline:S lsp:S lint:S reload:S comment:S"];
        yield return ["php", "json:G sql:G closeTag:S outline:S lsp:S lint:S reload:S comment:S"];
        yield return ["json", "json:S sql:G keyPath:S lsp:S lint:S reload:S comment:S"];
        yield return ["package", "json:S sql:G keyPath:S lsp:S lint:S reload:S comment:S pkg:S"];
        yield return ["yaml", "json:G sql:G keyPath:S lsp:S reload:S comment:S"];
        yield return ["csv", "json:G sql:G lsp:S reload:S comment:S"];
        yield return ["csvTable", "row:S reload:S comment:G"];
        yield return ["markdown", "json:G sql:G toc:S cycle:S outline:S reload:S comment:S read:S"];
        yield return ["markdownPage", "toc:G cycle:S outline:S reload:S comment:G read:S"];
        yield return ["html", "json:G sql:G tidy:S closeTag:S outline:S lsp:S lint:S reload:S comment:S"];
        yield return ["log", "json:G sql:G unfold:S reload:S comment:S"];
        yield return ["sql", "json:G sql:S lsp:S lint:S reload:S comment:S"];
        yield return ["picture", "cycle:S reload:S comment:G"];
        yield return ["hex", "hex:S reload:S comment:G"];
        yield return ["binary", "hex:S reload:S comment:G"];
        yield return ["git", "json:G sql:G outline:S git:S lsp:S lint:S reload:S comment:S"];
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 20_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private async Task<DocTab> OpenAsync(string kind)
    {
        switch (kind)
        {
            case "untitled": return _w.NewDocument();
            case "text": return _w.OpenFile(_temp.File("a.txt", "hello\n"), null);
            case "code": return _w.OpenFile(_temp.File("a.cs", "class A { }\n"), null);
            case "php": return _w.OpenFile(_temp.File("a.php", "<?php echo 1;\n"), null);
            case "json": return _w.OpenFile(_temp.File("a.json", "{\"a\":1}\n"), null);
            case "package": return _w.OpenFile(_temp.File("package.json", "{\"name\":\"x\"}\n"), null);
            case "yaml": return _w.OpenFile(_temp.File("a.yaml", "a: 1\n"), null);
            case "csv": return _w.OpenFile(_temp.File("a.csv", "a,b\n1,2\n"), null);
            case "csvTable":
                var table = _w.OpenFile(_temp.File("t.csv", "a,b\n1,2\n"), null);
                _w.SetTable(table, true);
                return table;
            case "markdown": return _w.OpenFile(_temp.File("a.md", "# Title\n"), null);
            case "markdownPage":
                var page = _w.OpenFile(_temp.File("b.md", "# Title\n"), null);
                Assert.True(_w.SetViewMode(page.Doc, ViewMode.Read));
                return _w.Active!;
            case "html": return _w.OpenFile(_temp.File("a.html", "<p>x</p>\n"), null);
            case "log": return _w.OpenFile(_temp.File("a.log", "INFO start\nERROR boom\n"), null);
            case "sql": return _w.OpenFile(_temp.File("a.sql", "select 1;\n"), null);
            case "picture":
                _w.OpenFile(_temp.Bytes("a.png", Pictures.Png()), null);
                await Until(() => _w.Active?.Custom is ImageView { Loading: false }, "the picture to be decoded");
                return _w.Active!;
            case "hex": return _w.OpenHex(_temp.File("h.txt", "hello\n"), null);
            case "binary": return _w.OpenHex(_temp.Bytes("b.bin", [0, 1, 2, 3, 255, 254]), null);
            case "git":
                Directory.CreateDirectory(Path.Combine(_temp.Path, "repo", ".git"));
                return _w.OpenFile(_temp.File(Path.Combine("repo", "g.cs"), "class G { }\n"), null);
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static IEnumerable<MenuItem> Walk(ItemsControl root) => root.Items.OfType<MenuItem>().SelectMany(i => Walk(i).Prepend(i));

    private static IEnumerable<MenuCommand> Shown(IEnumerable<MenuGroup> menu) => menu.SelectMany(g => g.Items).AllCommands();

    /// <summary>S: in the menu and available now; G: in the menu, grey; -: not in the menu.</summary>
    private char State(string commandId)
    {
        var command = Shown(_w.MenuModel).FirstOrDefault(c => c.CommandId == commandId);
        return command is null ? '-' : command.EnabledNow!() ? 'S' : 'G';
    }

    [AvaloniaTheory]
    [MemberData(nameof(Table))]
    public async Task The_menu_shows_greys_and_hides_the_items_of_each_kind_of_file_as_the_table_says(string kind, string expected)
    {
        var tab = await OpenAsync(kind);
        Dispatcher.UIThread.RunJobs();
        _w.SelectTab(tab);
        var wanted = expected.Split(' ').Select(p => p.Split(':')).ToDictionary(p => p[0], p => p[1][0]);
        Assert.All(wanted.Keys, name => Assert.True(Columns.ContainsKey(name), name));
        var wrong = Columns.Where(c => State(c.Value) != wanted.GetValueOrDefault(c.Key, '-')).Select(c => $"{c.Key} ({c.Value}) is {State(c.Value)}, the table says {wanted.GetValueOrDefault(c.Key, '-')}").ToList();
        Assert.True(wrong.Count == 0, $"{kind}: " + string.Join("; ", wrong));
        // the HTML menu is drawn for the documents it is for and for no other
        Assert.Equal(kind is "php" or "html", _w.MenuHeaders.Contains("HTML"));
        // an item the table calls available can be run: its command has a handler and the registry does not refuse it
        var context = new ShellContext(_w, forMenu: true);
        foreach (var (name, state) in wanted.Where(w => w.Value == 'S'))
        {
            Assert.True(_app.Workbench.Commands.TryGet(Columns[name], out var command), name);
            Assert.True(command.IsEnabled(context), $"{kind}: {name} is shown as available and the registry refuses it");
        }
    }

    [AvaloniaFact]
    public async Task Every_item_that_is_only_for_one_mode_is_hidden_in_the_others_and_never_merely_grey()
    {
        var wb = _app.Workbench;
        var gated = wb.Commands.Commands.Where(c => c.Id.StartsWith("nks.hex.", StringComparison.Ordinal) || c.Id.StartsWith("nks.table.", StringComparison.Ordinal) && c.Id != "nks.table.editRegion"
            || c.Id.StartsWith("nks.compare.", StringComparison.Ordinal) && (c.When?.Keys.Contains(ContextKeys.CompareMode) ?? false)).ToList();
        Assert.True(gated.Count > 30);
        Assert.All(gated, c => Assert.True(c.When is not null && c.Enablement is null, $"{c.Id} is for one mode: it belongs in `when`, not in `enablement`"));
        var tab = await OpenAsync("text");
        _w.SelectTab(tab);
        var present = Shown(_w.MenuModel).Select(c => c.CommandId).ToHashSet();
        Assert.Empty(gated.Select(c => c.Id).Where(present.Contains));
        // none of them is offered by the palette for a text document either
        _w.ShowPalette();
        _w.SetPaletteQuery("Hex");
        Assert.DoesNotContain(_w.PaletteResults, r => r.CommandId.StartsWith("nks.hex.", StringComparison.Ordinal));
        _w.ClosePalette();
    }

    [AvaloniaFact]
    public async Task Changing_the_mode_the_language_or_the_tab_changes_the_menu_at_once_without_any_refresh_by_the_test()
    {
        var text = await OpenAsync("text");
        Assert.Equal('-', State("nks.hex.inspector"));
        Assert.Equal('G', State("nks.edit.formatJson"));

        Assert.True(_w.SetHex(text.Doc, true));
        Assert.Equal('S', State("nks.hex.inspector"));
        Assert.Equal('-', State("nks.edit.formatJson"));
        Assert.True(_w.SetHex(text.Doc, false));
        Assert.Equal('-', State("nks.hex.inspector"));
        Assert.Equal('G', State("nks.edit.formatJson"));

        _w.Active!.Doc.ChooseLanguage(LanguageModes.ById("json"));
        Assert.Equal('S', State("nks.edit.formatJson"));
        Assert.Equal('S', State("nks.tools.copyKeyPath"));
        _w.Active.Doc.ChooseLanguage(LanguageModes.ById("sql"));
        Assert.Equal('G', State("nks.edit.formatJson"));
        Assert.Equal('S', State("nks.edit.formatSql"));
        Assert.Equal('-', State("nks.tools.copyKeyPath"));

        var picture = await OpenAsync("picture");
        Assert.Equal('-', State("nks.edit.formatSql"));
        Assert.Equal('S', State("nks.view.cycleMode"));
        Assert.True(_w.SetViewMode(picture.Doc, ViewMode.Text));
        Assert.Equal('G', State("nks.edit.formatSql"));
        Assert.Equal('S', State("nks.edit.toggleComment"));
        Assert.True(_w.SetViewMode(picture.Doc, ViewMode.Image));
        await Until(() => _w.Active?.Custom is ImageView { Loading: false }, "the picture to be decoded again");
        Assert.Equal('-', State("nks.edit.formatSql"));

        _w.SelectTab(text.Doc.Views[0]);
        Assert.Equal('S', State("nks.edit.formatSql"));

        var csv = _w.OpenFile(_temp.File("m.csv", "a,b\n"), null);
        Assert.Equal('-', State("nks.table.insertRow"));
        _w.SetTable(csv, true);
        Assert.Equal('S', State("nks.table.insertRow"));
        _w.SetTable(csv, false);
        Assert.Equal('-', State("nks.table.insertRow"));
    }

    [AvaloniaFact]
    public async Task Saving_a_new_document_under_a_name_that_makes_it_a_package_manifest_shows_the_package_items()
    {
        var tab = _w.NewDocument();
        Assert.Equal('-', State("nks.packages.lookup"));
        _dialogs.SaveAs.Enqueue(new SaveAsChoice(Path.Combine(_temp.Path, "composer.json"), TextCodec.Utf8));
        tab.Editor!.Document.Text = "{}";
        await _w.SaveAsAsync(tab);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal('S', State("nks.packages.lookup"));
    }

    [AvaloniaFact]
    public async Task A_grey_item_is_reported_as_unavailable_to_the_screen_reader_and_a_hidden_one_is_not_there()
    {
        var tab = await OpenAsync("text");
        _w.SelectTab(tab);
        var menu = MenuRenderer.ToMenu(_w.MenuModel);
        var items = Walk(menu).ToList();
        var grey = items.Single(i => i.Tag is MenuCommand { CommandId: "nks.edit.formatJson" });
        Assert.False(grey.IsEnabled);
        Assert.False(ControlAutomationPeer.CreatePeerForElement(grey).IsEnabled());
        Assert.Equal(_app.Workbench.Strings.Get("cmd.edit.formatJson"), Avalonia.Automation.AutomationProperties.GetName(grey));
        Assert.DoesNotContain(items, i => i.Tag is MenuCommand { CommandId: "nks.hex.inspector" });
        var available = items.Single(i => i.Tag is MenuCommand { CommandId: "nks.edit.toggleComment" });
        Assert.True(ControlAutomationPeer.CreatePeerForElement(available).IsEnabled());
    }

    [AvaloniaFact]
    public async Task The_palette_lists_a_grey_command_with_its_reason_and_leaves_out_the_commands_of_another_kind()
    {
        var tab = await OpenAsync("text");
        _w.SelectTab(tab);
        var wb = _app.Workbench;
        _w.ShowPalette();
        _w.SetPaletteQuery(wb.Strings.Get("cmd.edit.formatJson"));
        var json = Assert.Single(_w.PaletteResults, r => r.CommandId == "nks.edit.formatJson");
        Assert.False(json.Enabled);
        Assert.Equal(wb.Strings.Get("reason.hasSelection.false"), json.DisabledReason);
        _w.SetPaletteQuery(wb.Strings.Get("cmd.edit.htmlTidy"));
        Assert.DoesNotContain(_w.PaletteResults, r => r.CommandId == "nks.edit.htmlTidy");
        _w.ClosePalette();

        tab.Editor!.Document.Text = "{\"a\":1}";
        tab.Editor.Select(0, 7);
        _w.ShowPalette();
        _w.SetPaletteQuery(wb.Strings.Get("cmd.edit.formatJson"));
        Assert.True(Assert.Single(_w.PaletteResults, r => r.CommandId == "nks.edit.formatJson").Enabled);
        _w.ClosePalette();
    }

    [AvaloniaFact]
    public async Task The_context_menu_of_the_editor_adds_the_commonest_actions_of_the_kind_of_file()
    {
        var wb = _app.Workbench;
        List<string> Offered()
        {
            var context = new ShellContext(_w, forMenu: true);
            return [.. MenuBuilder.Nodes(wb, context, _ => null, _ => { }, "editor.context").AllCommands().Select(c => c.CommandId!)];
        }

        _w.SelectTab(await OpenAsync("text"));
        var text = Offered();
        Assert.Equal(["nks.edit.cut", "nks.edit.copy", "nks.edit.paste", "nks.edit.selectAll", "nks.edit.toggleComment", "nks.compare.clipboard", "nks.file.compareWithDisk", "nks.compare.file"], text);

        _w.SelectTab(await OpenAsync("json"));
        var json = Offered();
        Assert.Contains("nks.edit.formatJson", json);
        Assert.Contains("nks.tools.copyKeyPath", json);
        Assert.Contains("nks.lsp.goToDefinition", json);
        Assert.DoesNotContain("nks.edit.formatSql", json);
        Assert.DoesNotContain("nks.git.diffHead", json);

        _w.SelectTab(await OpenAsync("markdown"));
        var markdown = Offered();
        Assert.Contains("nks.tools.markdownPreview", markdown);
        Assert.Contains("nks.tools.markdownFormatTable", markdown);
        Assert.DoesNotContain("nks.edit.formatJson", markdown);

        _w.SelectTab(await OpenAsync("log"));
        Assert.Contains("nks.log.nextError", Offered());

        _w.SelectTab(await OpenAsync("git"));
        var git = Offered();
        Assert.Contains("nks.git.diffHead", git);
        Assert.Contains("nks.git.stageHunk", git);
    }

    [AvaloniaFact]
    public async Task The_context_keys_describe_the_active_tab()
    {
        string Get(string key) => new ShellContext(_w, forMenu: true).Get(key)!;

        Assert.Equal("", Get(ContextKeys.FileKind));
        Assert.Equal("", Get(ContextKeys.LanguageGroup));
        Assert.Equal("false", Get(ContextKeys.TextView));

        _w.SelectTab(await OpenAsync("code"));
        Assert.Equal(("code", "csharp", "true", "true", "false", "false", "false"),
            (Get(ContextKeys.FileKind), Get(ContextKeys.LanguageGroup), Get(ContextKeys.TextView), Get(ContextKeys.HasPath), Get(ContextKeys.LogMode), Get(ContextKeys.RemoteFile), Get(ContextKeys.InGitRepo)));

        _w.SelectTab(await OpenAsync("git"));
        Assert.Equal("true", Get(ContextKeys.InGitRepo));
        _w.SelectTab(await OpenAsync("package"));
        Assert.Equal(("data", "js-ts", "true"), (Get(ContextKeys.FileKind), Get(ContextKeys.LanguageGroup), Get(ContextKeys.PackageManifest)));
        _w.SelectTab(await OpenAsync("log"));
        Assert.Equal(("text", "true"), (Get(ContextKeys.FileKind), Get(ContextKeys.LogMode)));
        _w.SelectTab(await OpenAsync("html"));
        Assert.Equal(("markup", "web"), (Get(ContextKeys.FileKind), Get(ContextKeys.LanguageGroup)));
        _w.SelectTab(await OpenAsync("picture"));
        Assert.Equal(("image", "false"), (Get(ContextKeys.FileKind), Get(ContextKeys.TextView)));
        _w.SelectTab(await OpenAsync("binary"));
        Assert.Equal(("binary", "false"), (Get(ContextKeys.FileKind), Get(ContextKeys.TextView)));
        _w.SelectTab(_w.NewDocument());
        Assert.Equal(("text", "false", "plaintext"), (Get(ContextKeys.FileKind), Get(ContextKeys.HasPath), Get(ContextKeys.Language)));

        // the Markdown page: reading and editing have one, the source has none
        var markdown = _w.OpenFile(_temp.File("k.md", "# K\n"), null);
        Assert.Equal("false", Get(ContextKeys.MarkdownPreview));
        Assert.True(_w.SetViewMode(markdown.Doc, ViewMode.Edit));
        Assert.Equal("true", Get(ContextKeys.MarkdownPreview));
        Assert.True(_w.SetViewMode(markdown.Doc, ViewMode.Read));
        Assert.Equal(("true", "false"), (Get(ContextKeys.MarkdownPreview), Get(ContextKeys.TextView)));
        Assert.True(_w.SetViewMode(markdown.Doc, ViewMode.Text));
        Assert.Equal(("false", "true"), (Get(ContextKeys.MarkdownPreview), Get(ContextKeys.TextView)));
    }

    [AvaloniaFact]
    public void Every_group_of_the_language_catalog_is_a_value_of_the_language_group_key()
    {
        foreach (var group in LanguageCatalog.Groups)
        {
            var tab = _w.NewDocument();
            tab.Doc.ChooseLanguage(LanguageModes.ById(group.Modes[0].Id));
            Assert.Equal(group.Id, new ShellContext(_w, forMenu: true).Get(ContextKeys.LanguageGroup));
        }
        Assert.Equal(LanguageCatalog.Groups.Count, LanguageCatalog.Groups.Select(g => g.Id).Distinct().Count());
    }

    [AvaloniaFact]
    public void The_kind_of_a_document_comes_from_its_name_and_falls_back_to_its_language()
    {
        Assert.Equal("code", DocumentKinds.KindOfLanguage("csharp"));
        Assert.Equal("markup", DocumentKinds.KindOfLanguage("html"));
        Assert.Equal("style", DocumentKinds.KindOfLanguage("css"));
        Assert.Equal("data", DocumentKinds.KindOfLanguage("yaml"));
        Assert.Equal("text", DocumentKinds.KindOfLanguage("markdown"));
        Assert.Equal("text", DocumentKinds.KindOfLanguage("plaintext"));
        Assert.Equal("text", DocumentKinds.KindOfLanguage("a-grammar-of-a-plugin"));
        Assert.True(DocumentKinds.IsPackageManifest(@"C:\work\Demo\Demo.csproj"));
        Assert.True(DocumentKinds.IsPackageManifest("/w/package.json"));
        Assert.False(DocumentKinds.IsPackageManifest("/w/readme.md"));
        Assert.Equal("", DocumentKinds.GroupOf("markdown"));
        Assert.Equal("php", DocumentKinds.GroupOf("latte"));
    }

    [AvaloniaFact]
    public void A_file_lies_in_a_repository_when_a_git_entry_is_at_or_above_its_folder()
    {
        var inside = Directory.CreateDirectory(Path.Combine(_temp.Path, "r", "sub", "deep")).FullName;
        Directory.CreateDirectory(Path.Combine(_temp.Path, "r", ".git"));
        var linked = Directory.CreateDirectory(Path.Combine(_temp.Path, "w")).FullName;
        File.WriteAllText(Path.Combine(linked, ".git"), "gitdir: ../elsewhere");
        var outside = Directory.CreateDirectory(Path.Combine(_temp.Path, "o")).FullName;
        RepositoryProbe.Forget();
        Assert.True(RepositoryProbe.IsInside(Path.Combine(inside, "a.cs")));
        Assert.True(RepositoryProbe.IsInside(Path.Combine(linked, "a.cs")));
        Assert.False(RepositoryProbe.IsInside(Path.Combine(outside, "a.cs")));
        Assert.False(RepositoryProbe.IsInside("not a path \0"));
    }
}

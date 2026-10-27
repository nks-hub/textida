using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.Workbench.Tests;

public class ManifestTests
{
    private const string PluginManifest = """
        {
          "schemaVersion": 1,
          "id": "acme.formatter",
          "commands": [
            { "id": "acme.formatter.format", "title": "t.format", "when": "editorFocus", "aliases": ["FormatDoc"] }
          ],
          "menus": [ { "menu": "edit", "command": "acme.formatter.format", "group": "7_format", "order": 1 } ],
          "keybindings": [ { "key": "mod+alt+f", "command": "acme.formatter.format" } ],
          "settings": [ { "key": "acme.formatter.width", "type": "integer", "default": 80, "title": "t.width", "minimum": 20 } ]
        }
        """;

    [Fact]
    public void Builtin_manifest_loads_without_errors_and_registers_everything()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.NotEmpty(host.Commands.Commands);
        Assert.NotEmpty(host.Settings.Definitions);
        Assert.All(host.Commands.Commands, c => Assert.StartsWith("nks.", c.Id));
        Assert.Single(host.ContributedLayers);
        Assert.Equal(KeymapLayerKind.Default, host.ContributedLayers[0].Kind);
    }

    [Fact]
    public void Builtin_manifest_only_uses_known_context_keys_and_has_texts_in_every_language()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var known = ContextKeys.All.ToHashSet();
        var expressions = host.Commands.Commands.SelectMany(c => new[] { c.When, c.Enablement }).OfType<WhenExpression>()
            .Concat(host.ContributedLayers.SelectMany(l => l.Bindings).Where(b => b.When != null).Select(b => WhenExpression.Parse(b.When!)));
        Assert.All(expressions.SelectMany(e => e.Keys), k => Assert.Contains(k, known));

        foreach (var language in StringTable.UiLanguages)
        {
            var table = BuiltinTable(language);
            foreach (var c in host.Commands.Commands)
            {
                Assert.True(table.HasOwn(c.TitleKey), $"{language}: {c.TitleKey}");
                Assert.True(c.CategoryKey is null || table.HasOwn(c.CategoryKey), $"{language}: {c.CategoryKey}");
            }

            foreach (var s in host.Settings.Definitions)
            {
                Assert.True(table.HasOwn(s.Title) && table.HasOwn(s.Description!) && table.HasOwn(s.Category!), $"{language}: {s.Key}");
            }

            foreach (var menu in host.Commands.MenuContributions.Select(m => "menu." + m.Menu).Distinct())
            {
                Assert.True(table.HasOwn(menu), $"{language}: {menu}");
            }
        }
    }

    private static StringTable BuiltinTable(string language) => StringTable.LoadBuiltin(language);

    private static Dictionary<string, string> RawTable(string language)
    {
        using var stream = typeof(StringTable).Assembly.GetManifestResourceStream($"strings.{language}.json")!;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static HashSet<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{([A-Za-z]+)\}").Select(m => m.Groups[1].Value).ToHashSet();

    [Fact]
    public void Every_ui_language_has_exactly_the_english_keys_with_the_same_placeholders()
    {
        var embedded = typeof(StringTable).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("strings.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Select(n => n["strings.".Length..^".json".Length]);
        Assert.Equal(StringTable.UiLanguages.Order(), embedded.Order());
        var english = RawTable("en");
        foreach (var language in StringTable.UiLanguages)
        {
            var table = RawTable(language);
            Assert.True(english.Keys.ToHashSet().SetEquals(table.Keys), $"{language}: missing {string.Join(", ", english.Keys.Except(table.Keys))}; extra {string.Join(", ", table.Keys.Except(english.Keys))}");
            Assert.All(table, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{language}:{kv.Key}"));
            Assert.All(english, kv => Assert.True(Placeholders(kv.Value).SetEquals(Placeholders(table[kv.Key])), $"{language}:{kv.Key} placeholders differ"));
        }
    }

    [Fact]
    public void Language_setting_and_language_commands_follow_the_ui_language_list()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.True(host.Settings.TryGet("workbench.uiLanguage", out var setting));
        Assert.Equal(["auto", .. StringTable.UiLanguages], setting.Values!);
        var languageCommands = host.Commands.Commands.Select(c => c.Id).Where(id => id.StartsWith("nks.settings.language.", StringComparison.Ordinal));
        Assert.Equal(StringTable.UiLanguages.Select(l => "nks.settings.language." + l), languageCommands);
        var inMenu = host.Commands.MenuContributions.Where(m => m.Menu == "settings").Select(m => m.Command).ToHashSet();
        Assert.All(languageCommands, id => Assert.Contains(id, inMenu));
    }

    [Fact]
    public void Builtin_ids_are_unique_and_follow_the_scheme()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var ids = host.Commands.Commands.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(ManifestLoader.IsValidId(id), id));
        Assert.Empty(host.Commands.Validate());
    }

    [Theory]
    [InlineData("nks.file.save", true)]
    [InlineData("acme.formatter.format", true)]
    [InlineData("nks.edit.moveLineUp", true)]
    [InlineData("save", false)]
    [InlineData("Nks.file.save", false)]
    [InlineData("nks..save", false)]
    [InlineData("nks.file.", false)]
    [InlineData("nks.1file.save", false)]
    [InlineData("nks.file save", false)]
    [InlineData("", false)]
    public void Id_scheme(string id, bool valid) => Assert.Equal(valid, ManifestLoader.IsValidId(id));

    [Fact]
    public void Plugin_manifest_is_accepted_with_its_own_prefix()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.Empty(host.AddManifest(PluginManifest, ManifestTrust.Plugin));
        Assert.True(host.Commands.TryGet("FormatDoc", out var command));
        Assert.Equal("acme.formatter.format", command.Id);
        Assert.True(host.Settings.TryGet("acme.formatter.width", out _));
        Assert.Equal(KeymapLayerKind.Plugin, host.ContributedLayers[^1].Kind);
    }

    [Fact]
    public void Plugin_menu_entry_may_name_a_submenu_and_an_empty_name_is_refused()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var nested = PluginManifest.Replace("\"order\": 1 }", "\"order\": 1, \"submenu\": \"formatting\" }");
        Assert.Empty(host.AddManifest(nested, ManifestTrust.Plugin));
        Assert.Contains(host.Commands.MenuContributions, m => m.Command == "acme.formatter.format" && m.Submenu == "formatting");

        var empty = PluginManifest.Replace("acme.formatter", "acme.other").Replace("FormatDoc", "OtherDoc").Replace("\"order\": 1 }", "\"order\": 1, \"submenu\": \" \" }");
        Assert.Contains(WorkbenchHost.CreateBuiltin().AddManifest(empty, ManifestTrust.Plugin), e => e.Contains("submenu", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_submenu_of_the_builtin_manifest_has_a_title_in_every_language()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var ids = host.Commands.MenuContributions.Select(m => m.Submenu).OfType<string>().Distinct().ToList();
        Assert.True(ids.Count >= 20);
        foreach (var language in StringTable.UiLanguages)
        {
            var table = BuiltinTable(language);
            Assert.All(ids, id => Assert.True(table.HasOwn("submenu." + id), $"{language}: submenu.{id}"));
        }
    }

    [Fact]
    public void Plugin_cannot_claim_core_ids_or_foreign_prefixes()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var claimsCore = PluginManifest.Replace("acme.formatter.format", "nks.file.save");
        Assert.NotEmpty(host.AddManifest(claimsCore, ManifestTrust.Plugin));
        var impersonates = PluginManifest.Replace("\"id\": \"acme.formatter\"", "\"id\": \"nks.evil\"");
        Assert.NotEmpty(host.AddManifest(impersonates, ManifestTrust.Plugin));
        var foreignSetting = PluginManifest.Replace("acme.formatter.width", "editor.tabSize");
        Assert.NotEmpty(host.AddManifest(foreignSetting, ManifestTrust.Plugin));
        Assert.False(host.Commands.TryGet("acme.formatter.format", out _));
    }

    [Fact]
    public void Duplicate_ids_and_alias_clashes_are_reported_and_nothing_is_added()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.Empty(host.AddManifest(PluginManifest, ManifestTrust.Plugin));
        var before = host.Commands.Commands.Count;

        var errors = host.AddManifest(PluginManifest, ManifestTrust.Plugin);
        Assert.Contains(errors, e => e.Contains("already", StringComparison.Ordinal));

        var other = PluginManifest.Replace("acme.formatter", "acme.other");
        errors = host.AddManifest(other, ManifestTrust.Plugin);
        Assert.Contains(errors, e => e.Contains("FormatDoc", StringComparison.Ordinal));
        Assert.Equal(before, host.Commands.Commands.Count);
        Assert.False(host.Settings.TryGet("acme.other.width", out _));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"schemaVersion\": 2, \"id\": \"acme.x\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"unknownMember\": 1 }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"commands\": [ { \"id\": \"acme.x.a\", \"title\": \"t\", \"when\": \"a &&\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"commands\": [ { \"id\": \"acme.x.a\", \"title\": \"t\", \"undo\": \"always\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"keybindings\": [ { \"key\": \"ctrl+nonsense\", \"command\": \"acme.x.a\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"settings\": [ { \"key\": \"acme.x.n\", \"type\": \"integer\", \"default\": \"text\", \"title\": \"t\" } ] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"acme.x\", \"settings\": [ { \"key\": \"acme.x.n\", \"type\": \"integer\", \"default\": 5, \"minimum\": 10, \"title\": \"t\" } ] }")]
    public void Invalid_manifests_are_rejected(string json)
    {
        var result = ManifestLoader.Load(json, ManifestTrust.Plugin);
        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Limits_are_enforced()
    {
        var commands = string.Join(",", Enumerable.Range(0, ManifestLoader.MaxCommands + 1).Select(i => $"{{\"id\":\"acme.x.c{i}\",\"title\":\"t\"}}"));
        var result = ManifestLoader.Load($"{{\"schemaVersion\":1,\"id\":\"acme.x\",\"commands\":[{commands}]}}", ManifestTrust.Plugin);
        Assert.Contains(result.Errors, e => e.Contains("commands", StringComparison.Ordinal));

        var huge = "{\"schemaVersion\":1,\"id\":\"acme.x\",\"commands\":[]}" + new string(' ', ManifestLoader.MaxBytes);
        Assert.False(ManifestLoader.Load(huge, ManifestTrust.Plugin).Succeeded);
    }

    [Fact]
    public void Dangling_references_are_found_by_validation()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var json = """
            { "schemaVersion": 1, "id": "acme.x",
              "commands": [ { "id": "acme.x.a", "title": "t", "fallback": "acme.x.missing" } ],
              "menus": [ { "menu": "edit", "command": "acme.x.ghost", "group": "g" } ],
              "keybindings": [ { "key": "mod+9", "command": "acme.x.ghost2" } ] }
            """;
        Assert.Empty(host.AddManifest(json, ManifestTrust.Plugin));
        var problems = host.Validate(host.BuildKeymap(Platform.Windows));
        Assert.Contains(problems, p => p.Contains("acme.x.missing", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("acme.x.ghost'", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("acme.x.ghost2", StringComparison.Ordinal));
    }

    [Fact]
    public void Shortcut_commands_are_in_a_menu_or_the_palette_on_both_platforms()
    {
        var host = WorkbenchHost.CreateBuiltin();
        foreach (var platform in Enum.GetValues<Platform>())
        {
            Assert.Empty(host.Validate(host.BuildKeymap(platform)));
        }

        var hiddenWithKey = """
            { "schemaVersion": 1, "id": "acme.x",
              "commands": [ { "id": "acme.x.a", "title": "t", "hidden": true } ],
              "keybindings": [ { "key": "mod+9", "command": "acme.x.a" } ] }
            """;
        Assert.Empty(host.AddManifest(hiddenWithKey, ManifestTrust.Plugin));
        Assert.Contains(host.Validate(host.BuildKeymap(Platform.Windows)), p => p.Contains("acme.x.a", StringComparison.Ordinal));
    }
}

public class RegistryTests
{
    private static WorkbenchHost Host => WorkbenchHost.CreateBuiltin();

    [Fact]
    public async Task Handler_runs_only_when_command_is_enabled()
    {
        var registry = Host.Commands;
        var calls = 0;
        registry.SetHandler("nks.edit.deleteLine", (_, _) => { calls++; return ValueTask.FromResult(CommandOutcome.Handled); });

        var writable = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, false);
        var readOnly = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, true);
        var noFocus = new WhenContext();

        Assert.Equal(CommandOutcome.Handled, await registry.ExecuteAsync("nks.edit.deleteLine", writable));
        Assert.Equal(CommandOutcome.Disabled, await registry.ExecuteAsync("nks.edit.deleteLine", readOnly));
        Assert.Equal(CommandOutcome.Disabled, await registry.ExecuteAsync("nks.edit.deleteLine", noFocus));
        Assert.Equal(CommandOutcome.Unknown, await registry.ExecuteAsync("nks.nothing.here", writable));
        Assert.Equal(CommandOutcome.NoHandler, await registry.ExecuteAsync("nks.edit.moveLineUp", writable));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Handler_conflicts_are_detected()
    {
        var registry = Host.Commands;
        CommandHandler handler = (_, _) => ValueTask.FromResult(CommandOutcome.Handled);
        registry.SetHandler("nks.file.save", handler);
        Assert.Throws<InvalidOperationException>(() => registry.SetHandler("nks.file.save", handler));
        Assert.Throws<ArgumentException>(() => registry.SetHandler("nks.file.unknown", handler));
    }

    [Fact]
    public async Task Repeat_runs_the_last_handled_repeatable_command_again_and_tells_it_so()
    {
        var registry = Host.Commands;
        var log = new List<string>();
        CommandHandler Log(string name) => (_, _) => { log.Add(name + (registry.IsRepeating ? "*" : "")); return ValueTask.FromResult(CommandOutcome.Handled); };
        registry.SetHandler("nks.edit.deleteLine", Log("delete"));
        registry.SetHandler("nks.edit.joinLines", Log("join"));
        registry.SetHandler("nks.edit.copy", Log("copy"));
        registry.SetHandler("nks.edit.moveLineUp", (_, _) => ValueTask.FromResult(CommandOutcome.NotHandled));
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, false);

        Assert.Equal(CommandOutcome.NotHandled, await registry.RepeatLastAsync(ctx));
        Assert.True(registry.TryGet("nks.edit.deleteLine", out var delete) && delete.Repeatable);
        Assert.True(registry.TryGet("nks.edit.copy", out var copy) && !copy.Repeatable);

        await registry.ExecuteAsync("nks.edit.deleteLine", ctx);
        await registry.ExecuteAsync("nks.edit.copy", ctx);
        await registry.ExecuteAsync("nks.edit.moveLineUp", ctx);
        Assert.Equal("nks.edit.deleteLine", registry.LastRepeatable);

        Assert.Equal(CommandOutcome.Handled, await registry.RepeatLastAsync(ctx));
        await registry.ExecuteAsync("nks.edit.joinLines", ctx);
        await registry.RepeatLastAsync(ctx);
        Assert.Equal(["delete", "copy", "delete*", "join", "join*"], log);
        Assert.False(registry.IsRepeating);
        Assert.Equal("nks.edit.joinLines", registry.LastRepeatable);
    }

    [Fact]
    public async Task Declined_command_runs_its_fallback()
    {
        var registry = Host.Commands;
        var log = new List<string>();
        registry.SetHandler("nks.edit.tab", (_, _) => { log.Add("tab"); return ValueTask.FromResult(CommandOutcome.NotHandled); });
        registry.SetHandler("nks.edit.insertIndent", (_, _) => { log.Add("indent"); return ValueTask.FromResult(CommandOutcome.Handled); });
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, false);

        Assert.Equal(CommandOutcome.Handled, await registry.ExecuteAsync("nks.edit.tab", ctx));
        Assert.Equal(["tab", "indent"], log);
    }

    [Fact]
    public async Task Fallback_chains_are_bounded()
    {
        var host = Host;
        var json = """
            { "schemaVersion": 1, "id": "acme.x",
              "commands": [ { "id": "acme.x.a", "title": "t", "fallback": "acme.x.b" }, { "id": "acme.x.b", "title": "t", "fallback": "acme.x.a" } ] }
            """;
        Assert.Empty(host.AddManifest(json, ManifestTrust.Plugin));
        host.Commands.SetHandler("acme.x.a", (_, _) => ValueTask.FromResult(CommandOutcome.NotHandled));
        host.Commands.SetHandler("acme.x.b", (_, _) => ValueTask.FromResult(CommandOutcome.NotHandled));
        Assert.Equal(CommandOutcome.NotHandled, await host.Commands.ExecuteAsync("acme.x.a", new WhenContext()));
    }

    [Fact]
    public void Commands_declare_undo_transaction_and_cancellation_as_data()
    {
        Assert.True(Host.Commands.TryGet("nks.edit.deleteLine", out var c));
        Assert.Equal(UndoMode.Transaction, c.Undo);
        Assert.True(Host.Commands.TryGet("nks.file.save", out var save));
        Assert.Equal(UndoMode.None, save.Undo);
    }

    [Fact]
    public void Menu_reads_the_registry_and_orders_items()
    {
        var host = Host;
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, true).Set(ContextKeys.HasSelection, false);
        var edit = host.Commands.GetMenu("edit", ctx);
        Assert.Equal("nks.edit.undo", edit[0].CommandId);
        Assert.False(edit.Single(i => i.CommandId == "nks.edit.cut").Enabled);
        Assert.True(edit.Single(i => i.CommandId == "nks.edit.copy").Enabled);
        Assert.Equal(edit.OrderBy(i => i.Group, StringComparer.Ordinal).ThenBy(i => i.Order).Select(i => i.CommandId), edit.Select(i => i.CommandId));

        Assert.DoesNotContain(host.Commands.GetMenu("edit", new WhenContext()), i => i.CommandId == "nks.edit.undo");

        Assert.Empty(host.AddManifest("""
            { "schemaVersion": 1, "id": "acme.x", "commands": [ { "id": "acme.x.a", "title": "t" } ],
              "menus": [ { "menu": "edit", "command": "acme.x.a", "group": "0_first", "order": 1 } ] }
            """, ManifestTrust.Plugin));
        Assert.Equal("acme.x.a", host.Commands.GetMenu("edit", ctx)[0].CommandId);
    }
}

public class PaletteTests
{
    private static (WorkbenchHost Host, StringTable Strings) Setup(string language = "en") =>
        (WorkbenchHost.CreateBuiltin(), StringTable.LoadBuiltin(language));

    [Fact]
    public void Query_finds_commands_fuzzily_and_shows_the_shortcut_of_the_platform()
    {
        var (host, strings) = Setup();
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true);

        var win = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, ctx, "save as");
        Assert.Equal("nks.file.saveAs", win[0].CommandId);
        Assert.Equal("File", win[0].Category);
        Assert.Equal("Ctrl+Shift+S", win[0].Shortcut);

        var mac = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.MacOS), strings, ctx, "save as");
        Assert.Equal("⇧⌘S", mac[0].Shortcut);

        var fuzzy = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, ctx, "dupl ln");
        Assert.Equal("nks.edit.duplicateLine", fuzzy[0].CommandId);
        Assert.Empty(CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, ctx, "qqqqzz"));
    }

    [Fact]
    public void Query_respects_when_hidden_and_enablement()
    {
        var (host, strings) = Setup();
        var keymap = host.BuildKeymap(Platform.Windows);

        var none = CommandPalette.Query(host.Commands, keymap, strings, new WhenContext(), "", 1000);
        Assert.DoesNotContain(none, i => i.CommandId == "nks.edit.undo");
        Assert.Contains(none, i => i.CommandId == "nks.file.new");
        Assert.False(none.Single(i => i.CommandId == "nks.file.save").Enabled);

        Assert.Empty(host.AddManifest("""
            { "schemaVersion": 1, "id": "acme.x", "commands": [ { "id": "acme.x.secret", "title": "Secret", "hidden": true } ] }
            """, ManifestTrust.Plugin));
        Assert.DoesNotContain(CommandPalette.Query(host.Commands, keymap, strings, new WhenContext(), "secret", 1000), i => i.CommandId == "acme.x.secret");
    }

    [Fact]
    public void Query_ignores_czech_diacritics()
    {
        var (host, strings) = Setup("cs");
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true);
        var hits = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, ctx, "prejit na radek");
        Assert.Equal("nks.search.gotoLine", hits[0].CommandId);
        Assert.Equal("Přejít na řádek...", hits[0].Title);
    }
}

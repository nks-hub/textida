using System.Text.Json.Nodes;
using NksPad.Workbench.Commands;
using NksPad.Workbench.Keys;
using NksPad.Workbench.Settings;

namespace NksPad.Workbench.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nkspad-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}

public class SettingsStackTests
{
    private static readonly SettingSource[] Order =
    [
        SettingSource.Default, SettingSource.Plugin, SettingSource.User, SettingSource.UserLanguage,
        SettingSource.Workspace, SettingSource.WorkspaceLanguage, SettingSource.EditorConfig, SettingSource.Document,
    ];

    private static SettingsStack NewStack() => new(WorkbenchHost.CreateBuiltin().Settings);

    private static void Put(SettingsStack stack, SettingSource source, long value, string key = "editor.tabSize") =>
        stack.SetLayer(source, new JsonObject { [key] = value }, SettingRules.IsLanguageSource(source) ? "csharp" : null, source.ToString());

    [Fact]
    public void Default_value_comes_from_the_definition_and_reports_its_source()
    {
        var r = NewStack().Resolve("editor.tabSize")!;
        Assert.Equal(4, r.Value.GetValue<long>());
        Assert.Equal(SettingSource.Default, r.Source);
        Assert.Null(NewStack().Resolve("not.defined"));
    }

    [Fact]
    public void Every_pair_of_sources_has_the_expected_winner()
    {
        for (var i = 1; i < Order.Length; i++)
        {
            for (var j = i + 1; j < Order.Length; j++)
            {
                var stack = NewStack();
                Put(stack, Order[i], 10);
                Put(stack, Order[j], 20);
                var r = stack.Resolve("editor.tabSize", "csharp")!;
                Assert.True(Order[j] == r.Source, $"{Order[i]} vs {Order[j]}: winner {r.Source}");
                Assert.Equal(20, r.Value.GetValue<long>());
                Assert.Equal(Order[j].ToString(), r.LayerName);
            }
        }

        for (var i = 1; i < Order.Length; i++)
        {
            var stack = NewStack();
            Put(stack, Order[i], 9);
            Assert.Equal(Order[i], stack.Resolve("editor.tabSize", "csharp")!.Source);
        }
    }

    [Fact]
    public void Precedence_order_is_the_declared_enum_order()
    {
        Assert.Equal(Order, Enum.GetValues<SettingSource>());
    }

    [Fact]
    public void Language_layers_apply_only_to_their_language()
    {
        var stack = NewStack();
        Put(stack, SettingSource.User, 2);
        Put(stack, SettingSource.UserLanguage, 8);
        Assert.Equal(8, stack.GetInt("editor.tabSize", "CSharp"));
        Assert.Equal(2, stack.GetInt("editor.tabSize", "json"));
        Assert.Equal(2, stack.GetInt("editor.tabSize"));
    }

    [Fact]
    public void Invalid_values_are_skipped_and_the_next_layer_applies()
    {
        var stack = NewStack();
        Put(stack, SettingSource.User, 3);
        stack.SetLayer(SettingSource.Workspace, new JsonObject { ["editor.tabSize"] = 99 }, null, "ws");
        stack.SetLayer(SettingSource.Document, new JsonObject { ["editor.tabSize"] = "wide" }, null, "doc");
        var r = stack.Resolve("editor.tabSize")!;
        Assert.Equal(SettingSource.User, r.Source);
        Assert.Equal(3, r.Value.GetValue<long>());
        Assert.Equal([SettingSource.Document, SettingSource.Workspace], r.Skipped.Select(s => s.Source));
    }

    [Fact]
    public void Scope_limits_which_layers_may_set_a_value()
    {
        var stack = NewStack();
        stack.SetLayer(SettingSource.User, new JsonObject { ["editor.fontSize"] = 20, ["editor.wordWrap"] = "on" }, null, "user");
        stack.SetLayer(SettingSource.Workspace, new JsonObject { ["editor.fontSize"] = 30 }, null, "ws");
        stack.SetLayer(SettingSource.Document, new JsonObject { ["editor.wordWrap"] = "off" }, null, "doc");
        var size = stack.Resolve("editor.fontSize")!;
        Assert.Equal(SettingSource.User, size.Source);
        Assert.Single(size.Skipped);
        Assert.Equal("on", stack.GetString("editor.wordWrap"));
        Assert.Equal(SettingSource.User, stack.Resolve("editor.wordWrap")!.Source);
    }

    [Fact]
    public void Enum_and_bool_types_are_validated()
    {
        var stack = NewStack();
        stack.SetLayer(SettingSource.User, new JsonObject { ["files.eol"] = "cr", ["editor.insertSpaces"] = "yes" }, null, "user");
        Assert.Equal("auto", stack.GetString("files.eol"));
        Assert.True(stack.GetBool("editor.insertSpaces"));
        Assert.NotEmpty(stack.Resolve("files.eol")!.Skipped);
    }

    [Fact]
    public void Plugin_layer_sits_above_defaults_but_below_the_user()
    {
        var stack = NewStack();
        Put(stack, SettingSource.Plugin, 6);
        Assert.Equal(6, stack.GetInt("editor.tabSize"));
        Put(stack, SettingSource.User, 2);
        Assert.Equal(2, stack.GetInt("editor.tabSize"));
    }

    [Fact]
    public void Layer_arguments_are_checked()
    {
        var stack = NewStack();
        Assert.Throws<ArgumentException>(() => stack.SetLayer(SettingSource.Default, new JsonObject()));
        Assert.Throws<ArgumentException>(() => stack.SetLayer(SettingSource.UserLanguage, new JsonObject()));
        Assert.Throws<ArgumentException>(() => stack.SetLayer(SettingSource.User, new JsonObject(), "csharp"));
    }

    [Fact]
    public void Builtin_defaults_are_all_valid_and_scopes_known()
    {
        foreach (var d in WorkbenchHost.CreateBuiltin().Settings.Definitions)
        {
            Assert.Null(SettingRules.Validate(d, d.Default));
            Assert.True(SettingRules.TryParseScope(d.Scope, out _), d.Key);
        }
    }
}

public class SettingsFileTests
{
    [Fact]
    public void Unknown_keys_and_properties_survive_a_round_trip()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "settings": { "editor.tabSize": 2, "future.option": { "deep": [1, 2, 3] }, "editor.fontSize": 12 },
              "languages": { "csharp": { "editor.tabSize": 4, "future.langOption": "x" } },
              "futureSection": { "a": true }
            }
            """;
        var file = SettingsFile.Parse(json);
        file.Set("editor.tabSize", 8);
        file.Set("files.eol", "lf", "csharp");

        var copy = SettingsFile.Parse(file.ToJson());
        Assert.Equal(8, copy.Get("editor.tabSize")!.GetValue<int>());
        Assert.Equal("[1,2,3]", copy.Get("future.option")!["deep"]!.ToJsonString());
        Assert.Equal("x", copy.Get("future.langOption", "csharp")!.GetValue<string>());
        Assert.Equal("lf", copy.Get("files.eol", "csharp")!.GetValue<string>());
        Assert.True(copy.Root["futureSection"]!["a"]!.GetValue<bool>());
        Assert.Equal(12, copy.Get("editor.fontSize")!.GetValue<int>());

        var registry = WorkbenchHost.CreateBuiltin().Settings;
        Assert.Equal(["future.option", "future.langOption"], copy.UnknownKeys(registry));
    }

    [Fact]
    public void Removing_a_key_removes_only_that_key()
    {
        var file = SettingsFile.Parse("""{ "settings": { "a.b": 1, "editor.tabSize": 3 } }""");
        file.Set("editor.tabSize", null);
        Assert.Null(file.Get("editor.tabSize"));
        Assert.Equal(1, file.Get("a.b")!.GetValue<int>());
    }

    [Fact]
    public void Missing_version_is_treated_as_current_and_is_stamped()
    {
        var file = SettingsFile.Parse("""{ "settings": { "editor.tabSize": 3 } }""");
        Assert.Equal(1, file.Version);
        Assert.Null(file.LoadError);
    }

    private static SettingsMigrator Migrator() => new(3,
    [
        SettingsMigration.RenameKey(1, "editor.tabWidth", "editor.tabSize"),
        new SettingsMigration(2, root =>
        {
            foreach (var section in SettingsFile.Sections(root))
            {
                if (section.TryGetPropertyValue("editor.useTabs", out var useTabs))
                {
                    section.Remove("editor.useTabs");
                    section["editor.insertSpaces"] = !useTabs!.GetValue<bool>();
                }
            }
        }),
    ]);

    [Fact]
    public void Old_documents_are_migrated_step_by_step_and_keep_unknown_keys()
    {
        const string v1 = """
            { "schemaVersion": 1,
              "settings": { "editor.tabWidth": 2, "editor.useTabs": false, "plugin.thing": [1] },
              "languages": { "json": { "editor.tabWidth": 6, "editor.useTabs": true } } }
            """;
        var file = SettingsFile.Parse(v1, Migrator());
        Assert.Equal(3, file.Version);
        Assert.Equal(1, file.MigratedFrom);
        Assert.Equal(3, file.Root["schemaVersion"]!.GetValue<int>());
        Assert.Equal(2, file.Get("editor.tabSize")!.GetValue<int>());
        Assert.True(file.Get("editor.insertSpaces")!.GetValue<bool>());
        Assert.Equal(6, file.Get("editor.tabSize", "json")!.GetValue<int>());
        Assert.False(file.Get("editor.insertSpaces", "json")!.GetValue<bool>());
        Assert.Null(file.Get("editor.tabWidth"));
        Assert.Equal("[1]", file.Get("plugin.thing")!.ToJsonString());
    }

    [Fact]
    public void Migration_from_the_middle_and_no_migration_when_current()
    {
        var file = SettingsFile.Parse("""{ "schemaVersion": 2, "settings": { "editor.useTabs": true, "editor.tabWidth": 9 } }""", Migrator());
        Assert.Equal(2, file.MigratedFrom);
        Assert.False(file.Get("editor.insertSpaces")!.GetValue<bool>());
        Assert.NotNull(file.Get("editor.tabWidth"));

        var current = SettingsFile.Parse("""{ "schemaVersion": 3, "settings": { "editor.tabWidth": 9 } }""", Migrator());
        Assert.Null(current.MigratedFrom);
        Assert.NotNull(current.Get("editor.tabWidth"));
    }

    [Fact]
    public void Migration_does_not_overwrite_an_existing_target_key()
    {
        var file = SettingsFile.Parse("""{ "schemaVersion": 1, "settings": { "editor.tabWidth": 2, "editor.tabSize": 5 } }""", Migrator());
        Assert.Equal(5, file.Get("editor.tabSize")!.GetValue<int>());
        Assert.Equal(2, file.Get("editor.tabWidth")!.GetValue<int>());
    }

    [Fact]
    public void Migrator_rejects_gaps()
    {
        Assert.Throws<ArgumentException>(() => new SettingsMigrator(3, [SettingsMigration.RenameKey(2, "a.b", "a.c")]));
    }

    [Fact]
    public void Saving_a_migrated_file_keeps_a_backup_of_the_original()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        const string v1 = """{ "schemaVersion": 1, "settings": { "editor.tabWidth": 2 } }""";
        File.WriteAllText(path, v1);

        var file = SettingsFile.Load(path, Migrator());
        Assert.Equal(SettingsSaveResult.Saved, file.Save(path));
        Assert.Equal(v1, File.ReadAllText(dir.File("settings.json.v1.bak")));
        Assert.Equal(3, SettingsFile.Load(path, Migrator()).Version);

        File.WriteAllText(path, v1.Replace("2", "7", StringComparison.Ordinal));
        SettingsFile.Load(path, Migrator()).Save(path);
        Assert.Equal(v1, File.ReadAllText(dir.File("settings.json.v1.bak")));
    }

    [Fact]
    public void A_file_from_a_newer_editor_is_read_but_never_overwritten()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        const string newer = """{ "schemaVersion": 7, "settings": { "editor.tabSize": 3, "x.y": 1 }, "newThing": {} }""";
        File.WriteAllText(path, newer);

        var file = SettingsFile.Load(path);
        Assert.True(file.IsNewerThanSupported);
        Assert.Null(file.LoadError);
        Assert.Equal(3, file.Get("editor.tabSize")!.GetValue<int>());
        file.Set("editor.tabSize", 9);
        Assert.Equal(SettingsSaveResult.RefusedNewerVersion, file.Save(path));
        Assert.Equal(newer, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "schemaVersion": "one" }""")]
    [InlineData("""{ "schemaVersion": 0 }""")]
    [InlineData("""{ "settings": [1] }""")]
    [InlineData("""{ "languages": { "csharp": 1 } }""")]
    [InlineData("{ \"settings\": {}, // comment\n}")]
    public void Unreadable_files_are_never_overwritten(string content)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, content);

        var file = SettingsFile.Load(path);
        Assert.NotNull(file.LoadError);
        file.Set("editor.tabSize", 2);
        Assert.Equal(SettingsSaveResult.RefusedLoadError, file.Save(path));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Failed_migration_is_a_load_error()
    {
        var bad = new SettingsMigrator(2, [new SettingsMigration(1, _ => throw new InvalidOperationException("boom"))]);
        var file = SettingsFile.Parse("""{ "schemaVersion": 1 }""", bad);
        Assert.Contains("boom", file.LoadError, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_is_atomic_and_leaves_no_temp_files()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "nested", "settings.json");

        var file = SettingsFile.Create();
        file.Set("editor.tabSize", 3);
        file.Set("editor.fontFamily", "Cascadia Mono");
        Assert.Equal(SettingsSaveResult.Saved, file.Save(path));
        Assert.Equal(3, SettingsFile.Load(path).Get("editor.tabSize")!.GetValue<int>());

        file.Set("editor.tabSize", 5);
        Assert.Equal(SettingsSaveResult.Saved, file.Save(path));
        Assert.Equal(5, SettingsFile.Load(path).Get("editor.tabSize")!.GetValue<int>());
        Assert.Equal([path], Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!));
        Assert.DoesNotContain("\r", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_write_keeps_the_old_file_and_removes_the_temp_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // replacing an open file only fails on Windows
        }

        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, "old");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(path, "new"));
        }

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void Non_ascii_text_is_written_readably()
    {
        using var dir = new TempDir();
        var path = dir.File("s.json");
        var file = SettingsFile.Create();
        file.Set("editor.fontFamily", "Písmo");
        file.Save(path);
        Assert.Contains("Písmo", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith("﻿"u8.ToArray()));
    }

    [Fact]
    public void File_layers_feed_the_stack_with_sources()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var stack = new SettingsStack(host.Settings);
        var file = SettingsFile.Parse("""
            { "settings": { "editor.tabSize": 3 }, "languages": { "csharp": { "editor.tabSize": 5 } } }
            """);
        file.ApplyTo(stack, SettingSource.User, SettingSource.UserLanguage, "user settings");
        Assert.Equal(3, stack.GetInt("editor.tabSize"));
        var r = stack.Resolve("editor.tabSize", "csharp")!;
        Assert.Equal((SettingSource.UserLanguage, "csharp", "user settings"), (r.Source, r.Language, r.LayerName));
    }
}

public class SchemaTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NksPad.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void AssertInSync(string relative, string generated)
    {
        var path = RepoFile(relative);
        if (Environment.GetEnvironmentVariable("NKSPAD_UPDATE_SCHEMAS") == "1")
        {
            File.WriteAllText(path, generated);
        }

        var committed = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.True(committed == generated, $"{relative} is out of date. Regenerate it with NKSPAD_UPDATE_SCHEMAS=1 dotnet test.");
    }

    [Fact]
    public void Settings_schema_is_in_sync_with_the_definitions()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var schema = SchemaGenerator.ForSettings(host.Settings, StringTable.LoadBuiltin("en"), SettingsMigrator.Default.CurrentVersion);
        AssertInSync("src/NksPad.Workbench/schemas/settings.schema.json", SchemaGenerator.ToText(schema));
    }

    [Fact]
    public void Contribution_and_keymap_schemas_are_in_sync_with_the_model_types()
    {
        AssertInSync("src/NksPad.Workbench/schemas/contributions.schema.json", SchemaGenerator.ToText(SchemaGenerator.ForContributions()));
        AssertInSync("src/NksPad.Workbench/schemas/keymap.schema.json", SchemaGenerator.ToText(SchemaGenerator.ForKeymapFile()));
    }

    [Fact]
    public void Settings_schema_describes_every_setting_with_its_default_and_limits()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var schema = SchemaGenerator.ForSettings(host.Settings, StringTable.LoadBuiltin("en"), 1);
        var props = schema["properties"]!["settings"]!["properties"]!.AsObject();
        Assert.Equal(host.Settings.Definitions.Count, props.Count);
        Assert.Equal(4, props["editor.tabSize"]!["default"]!.GetValue<int>());
        Assert.Equal(32, props["editor.tabSize"]!["maximum"]!.GetValue<double>());
        Assert.Equal(["auto", "lf", "crlf"], props["files.eol"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.True(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void Builtin_manifest_matches_its_schema_shape()
    {
        var json = JsonNode.Parse(File.ReadAllText(RepoFile("src/NksPad.Workbench/Resources/builtin.contributions.json")))!.AsObject();
        var schemaProps = SchemaGenerator.ForContributions()["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
        Assert.All(json.Select(p => p.Key), k => Assert.Contains(k, schemaProps));
    }
}

public class SettingsSearchTests
{
    private static (WorkbenchHost Host, SettingsStack Stack, SettingsSearchIndex Index, Keymap Keymap) Setup(string language = "en", Platform platform = Platform.Windows)
    {
        var host = WorkbenchHost.CreateBuiltin();
        var strings = StringTable.LoadBuiltin(language);
        return (host, new SettingsStack(host.Settings), new SettingsSearchIndex(host.Settings, strings), host.BuildKeymap(platform));
    }

    [Fact]
    public void Tab_finds_all_indentation_and_tab_options_with_their_value_source()
    {
        var (host, stack, index, keymap) = Setup();
        stack.SetLayer(SettingSource.User, new JsonObject { ["editor.tabSize"] = 2 }, null, "user");

        var hits = index.Search("tab", stack, host.Commands, keymap);
        var settings = hits.Where(h => h.Kind == SearchHitKind.Setting).ToDictionary(h => h.Id);
        foreach (var key in new[] { "editor.tabSize", "editor.indentSize", "editor.insertSpaces", "editor.detectIndentation" })
        {
            Assert.True(settings.ContainsKey(key), key);
        }

        Assert.Equal(("2", SettingSource.User), (settings["editor.tabSize"].ValueText, settings["editor.tabSize"].ValueSource));
        Assert.Equal(SettingSource.Default, settings["editor.insertSpaces"].ValueSource);
        Assert.DoesNotContain("files.encoding", settings.Keys);
        Assert.Equal("editor.tabSize", hits[0].Id);
    }

    [Fact]
    public void Language_changes_the_shown_value_and_source()
    {
        var (host, stack, index, keymap) = Setup();
        stack.SetLayer(SettingSource.User, new JsonObject { ["editor.tabSize"] = 2 }, null, "user");
        stack.SetLayer(SettingSource.UserLanguage, new JsonObject { ["editor.tabSize"] = 8 }, "makefile", "user");
        var hit = index.Search("tab size", stack, host.Commands, keymap, "makefile").First(h => h.Id == "editor.tabSize");
        Assert.Equal(("8", SettingSource.UserLanguage), (hit.ValueText, hit.ValueSource));
    }

    [Fact]
    public void Czech_query_without_diacritics_finds_the_diacritic_title()
    {
        var (host, stack, index, keymap) = Setup("cs");
        var hits = index.Search("kodovani", stack, host.Commands, keymap);
        Assert.Equal("files.encoding", hits[0].Id);
        Assert.Equal("Výchozí kódování", hits[0].Title);
        Assert.Equal(hits[0].Id, index.Search("KÓDOVÁNÍ", stack, host.Commands, keymap)[0].Id);
        Assert.Contains(index.Search("odsazeni", stack, host.Commands, keymap), h => h.Id == "editor.indentSize");
    }

    [Fact]
    public void Values_are_searchable()
    {
        var (host, stack, index, keymap) = Setup();
        stack.SetLayer(SettingSource.User, new JsonObject { ["files.encoding"] = "windows-1250" }, null, "user");
        var hits = index.Search("1250", stack, host.Commands, keymap);
        Assert.Equal(["files.encoding"], hits.Select(h => h.Id));
    }

    [Fact]
    public void All_words_must_match_and_empty_query_lists_every_setting()
    {
        var (host, stack, index, keymap) = Setup();
        Assert.Empty(index.Search("tab zzzzqq", stack, host.Commands, keymap));
        var all = index.Search("", stack, host.Commands, keymap);
        Assert.Equal(host.Settings.Definitions.Count, all.Count);
        Assert.All(all, h => Assert.Equal(SearchHitKind.Setting, h.Kind));
    }

    [Fact]
    public void Keybinding_rows_show_shortcut_and_source()
    {
        var (host, stack, index, keymap) = Setup();
        var hit = index.Search("save as", stack, host.Commands, keymap).Single(h => h.Kind == SearchHitKind.Keybinding);
        Assert.Equal(("nks.file.saveAs", "Ctrl+Shift+S", "Default: nks.core"), (hit.Id, hit.Shortcut, hit.ShortcutSource));
        Assert.False(hit.HasConflict);

        var mac = Setup(platform: Platform.MacOS);
        var macHit = mac.Index.Search("ctrl+shift+s", mac.Stack, mac.Host.Commands, mac.Keymap);
        Assert.Empty(macHit);
        Assert.Equal("⇧⌘S", mac.Index.Search("save as", mac.Stack, mac.Host.Commands, mac.Keymap).Single(h => h.Kind == SearchHitKind.Keybinding).Shortcut);
    }

    [Fact]
    public void Conflicting_shortcuts_are_marked_with_the_source_of_both_bindings()
    {
        var (host, stack, index, _) = Setup();
        var user = new KeymapLayer(KeymapLayerKind.User, "user keybindings.json",
        [
            new() { Key = "mod+s", Command = "nks.file.saveAll" },
            new() { Key = "mod+q", Command = "nks.file.close", When = "hasDocument" },
            new() { Key = "mod+q", Command = "nks.file.saveAs" },
        ]);
        var keymap = host.BuildKeymap(Platform.Windows, user);

        var save = index.Search("save all", stack, host.Commands, keymap).Single(h => h.Kind == SearchHitKind.Keybinding && h.Id == "nks.file.saveAll" && h.Shortcut == "Ctrl+S");
        Assert.False(save.HasConflict);
        Assert.Single(save.Conflicts);
        Assert.Equal(KeyConflictKind.Overridden, save.Conflicts[0].Kind);

        var quit = index.Search("close", stack, host.Commands, keymap).Single(h => h.Id == "nks.file.close" && h.Shortcut == "Ctrl+Q");
        Assert.True(quit.HasConflict);
        var conflict = Assert.Single(quit.Conflicts, c => c.IsAmbiguous);
        Assert.Equal(("user keybindings.json", "user keybindings.json"), (conflict.Winner.Source, conflict.Loser.Source));

        var shadowed = index.Search("exit", stack, host.Commands, keymap).Single(h => h.Id == "nks.app.quit");
        Assert.False(shadowed.HasConflict);
        Assert.Equal(2, shadowed.Conflicts.Count(c => c.Kind == KeyConflictKind.Overridden));
    }

    [Fact]
    public void Tab_search_lists_the_tab_bindings_with_their_overrides()
    {
        var (host, stack, index, _) = Setup();
        var user = new KeymapLayer(KeymapLayerKind.User, "user", [new() { Key = "tab", Command = "nks.edit.indent", When = "editorFocus" }]);
        var keymap = host.BuildKeymap(Platform.Windows, user);
        var rows = index.Search("tab", stack, host.Commands, keymap).Where(h => h.Kind == SearchHitKind.Keybinding).ToList();
        Assert.Contains(rows, h => h.Id == "nks.edit.tab" && h.Shortcut == "Tab" && h.Conflicts.Count == 1 && !h.HasConflict);
        Assert.Contains(rows, h => h.Id == "nks.edit.indent" && h.Shortcut == "Tab" && h.Conflicts.Count == 1);
    }
}

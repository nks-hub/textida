using System.Security.Cryptography;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench.Tests;

public class SettingsMigrationTests
{
    private const string Original = """
        {
          "schemaVersion": 1,
          "settings": { "editor.tabWidth": 3, "files.delay": 20, "plugin.option": { "nested": [1, 2] } },
          "languages": { "csharp": { "editor.tabWidth": 8, "files.delay": 50, "plugin.language": true } },
          "futureSection": { "name": "kept" }
        }
        """;

    private static SettingsMigrator ThreeVersions() => new(3,
    [
        SettingsMigration.RenameKey(1, "editor.tabWidth", "editor.tabSize"),
        SettingsMigration.RenameKey(2, "files.delay", "files.saveDelay"),
    ]);

    [Fact]
    public void Every_released_schema_round_trips_without_touching_the_journal()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var journal = dir.File("document.journal");
        File.WriteAllText(path, Original);
        File.WriteAllBytes(journal, [0, 1, 2, 128, 255]);
        var journalHash = SHA256.HashData(File.ReadAllBytes(journal));
        var expected = JsonNode.Parse(Original);
        var migrator = SettingsMigrator.Default;

        Assert.Equal(2, migrator.CurrentVersion);
        for (int version = 1; version <= migrator.CurrentVersion; version++)
        {
            var settings = SettingsFile.Load(path).MigrateTo(version);
            Assert.Equal(SettingsSaveResult.Saved, settings.Save(path));
            Assert.Equal(version, SettingsFile.Load(path).MigratedFrom ?? version);
            expected!["schemaVersion"] = version;
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(File.ReadAllText(path))));
            Assert.Equal(journalHash, SHA256.HashData(File.ReadAllBytes(journal)));
        }
        // only the upgrade from version 1 keeps a copy of the original
        Assert.Equal(["settings.json.v1.bak"], Directory.GetFiles(dir.Path, "*.bak").Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_reverse_chain_can_return_to_every_known_version_and_upgrade_again(int version)
    {
        var latest = SettingsFile.Parse(Original, ThreeVersions());
        var latestJson = latest.ToJson();

        var converted = latest.MigrateTo(version);

        Assert.Equal(version, converted.Version);
        Assert.Equal(version, converted.Root["schemaVersion"]!.GetValue<int>());
        Assert.Equal(3, converted.Get(version < 2 ? "editor.tabWidth" : "editor.tabSize")!.GetValue<int>());
        Assert.Equal(8, converted.Get(version < 2 ? "editor.tabWidth" : "editor.tabSize", "csharp")!.GetValue<int>());
        Assert.Equal(20, converted.Get(version < 3 ? "files.delay" : "files.saveDelay")!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(latest.Root, converted.MigrateTo(3).Root));
        Assert.Equal(latestJson, latest.ToJson());
        if (version == 1) Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Original), converted.Root));
    }

    [Fact]
    public void Saving_a_returned_document_preserves_the_previous_schema_and_the_journal()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var journal = dir.File("document.journal");
        var latest = SettingsFile.Parse(Original, ThreeVersions());
        File.WriteAllText(path, latest.ToJson());
        File.WriteAllBytes(journal, [9, 0, 255, 13, 10]);
        var previousHash = SHA256.HashData(File.ReadAllBytes(path));
        var journalHash = SHA256.HashData(File.ReadAllBytes(journal));

        var returned = SettingsFile.Load(path, ThreeVersions()).MigrateTo(1);
        Assert.Equal(SettingsSaveResult.Saved, returned.Save(path));

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Original), JsonNode.Parse(File.ReadAllText(path))));
        Assert.Equal(previousHash, SHA256.HashData(File.ReadAllBytes(path + ".v3.bak")));
        Assert.Equal(journalHash, SHA256.HashData(File.ReadAllBytes(journal)));
        Assert.True(JsonNode.DeepEquals(latest.Root, SettingsFile.Load(path, ThreeVersions()).Root));
    }

    [Fact]
    public void An_unsaved_upgrade_returned_to_its_original_schema_needs_no_new_backup()
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        File.WriteAllText(path, Original);

        var returned = SettingsFile.Load(path, ThreeVersions()).MigrateTo(1);

        Assert.Null(returned.MigratedFrom);
        Assert.Equal(SettingsSaveResult.Saved, returned.Save(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.bak"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Original), JsonNode.Parse(File.ReadAllText(path))));
    }

    [Fact]
    public void A_missing_reverse_step_refuses_the_return_without_changing_the_file()
    {
        var migrator = new SettingsMigrator(2, [new SettingsMigration(1, _ => { })]);
        AssertReturnRefused(migrator, "No reverse migration");
    }

    [Fact]
    public void A_missing_earlier_reverse_step_does_not_leave_a_partly_converted_document()
    {
        var rename = SettingsMigration.RenameKey(1, "editor.tabWidth", "editor.tabSize");
        var migrator = new SettingsMigrator(3,
        [
            new SettingsMigration(1, rename.Apply),
            SettingsMigration.RenameKey(2, "files.delay", "files.saveDelay"),
        ]);
        AssertReturnRefused(migrator, "No reverse migration from version 2");
    }

    [Fact]
    public void A_reverse_step_that_drops_an_unknown_setting_refuses_the_return()
    {
        var rename = SettingsMigration.RenameKey(1, "editor.tabWidth", "editor.tabSize");
        var migrator = new SettingsMigrator(2,
        [
            new SettingsMigration(1, rename.Apply, root =>
            {
                rename.Revert!(root);
                ((JsonObject)root["settings"]!).Remove("plugin.option");
            }),
        ]);
        AssertReturnRefused(migrator, "would lose settings");
    }

    private static void AssertReturnRefused(SettingsMigrator migrator, string message)
    {
        using var dir = new TempDir();
        var path = dir.File("settings.json");
        var latest = SettingsFile.Parse(Original, migrator);
        File.WriteAllText(path, latest.ToJson());
        var originalHash = SHA256.HashData(File.ReadAllBytes(path));
        var originalJson = latest.ToJson();

        var error = Assert.Throws<InvalidOperationException>(() => latest.MigrateTo(1));

        Assert.Contains(message, error.Message);
        Assert.Equal(originalJson, latest.ToJson());
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.bak"));
    }

    [Fact]
    public void A_rename_collision_keeps_both_values_through_the_return()
    {
        var file = SettingsFile.Parse("""
            { "schemaVersion": 2, "settings": { "old.key": 1, "new.key": 2 } }
            """, new SettingsMigrator(2, [SettingsMigration.RenameKey(1, "old.key", "new.key")]));

        var returned = file.MigrateTo(1);

        Assert.Equal(1, returned.Get("old.key")!.GetValue<int>());
        Assert.Equal(2, returned.Get("new.key")!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(file.Root, returned.MigrateTo(2).Root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void The_current_build_refuses_an_unknown_target_version(int version)
    {
        var file = SettingsFile.Parse(Original);
        Assert.Throws<ArgumentOutOfRangeException>(() => file.MigrateTo(version));
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{ \"schemaVersion\": 3, \"settings\": { \"future.key\": 3 } }")]
    public void Broken_or_newer_files_cannot_be_forced_into_the_current_schema(string json)
    {
        var file = SettingsFile.Parse(json);
        Assert.Throws<InvalidOperationException>(() => file.MigrateTo(1));
    }

    [Fact]
    public void The_unread_keymap_profile_moves_into_the_behaviour_profile()
    {
        var file = SettingsFile.Parse("""{ "schemaVersion": 1, "settings": { "workbench.keymapProfile": "pspad", "editor.tabSize": 2 } }""");

        Assert.Equal((2, (int?)1), (file.Version, file.MigratedFrom));
        Assert.Equal("pspad", file.Get("editor.profile")!.GetValue<string>());
        Assert.Null(file.Get("workbench.keymapProfile"));
        Assert.Equal(2, file.Get("editor.tabSize")!.GetValue<int>());
    }

    [Fact]
    public void A_behaviour_profile_already_set_wins_over_the_unread_keymap_profile()
    {
        var file = SettingsFile.Parse("""{ "schemaVersion": 1, "settings": { "workbench.keymapProfile": "pspad", "editor.profile": "default" } }""");

        Assert.Equal("default", file.Get("editor.profile")!.GetValue<string>());
        Assert.Null(file.Get("workbench.keymapProfile"));
    }

    [Fact]
    public void Returning_to_version_1_keeps_the_behaviour_profile()
    {
        var file = SettingsFile.Parse("""{ "schemaVersion": 2, "settings": { "editor.profile": "pspad" } }""");

        var returned = file.MigrateTo(1);

        Assert.Equal("pspad", returned.Get("editor.profile")!.GetValue<string>());
        Assert.Null(returned.Get("workbench.keymapProfile"));
    }
}

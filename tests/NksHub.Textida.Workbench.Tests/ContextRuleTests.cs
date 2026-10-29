using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P3.6.16 in the manifest: what is only for one mode or one kind of file is hidden elsewhere (<c>when</c>), what is general but cannot run now is grey (<c>enablement</c>) and the palette says why.</summary>
public class ContextRuleTests
{
    private static WhenContext Text(string language = "plaintext", string kind = "text") =>
        new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.HasDocument, true).Set(ContextKeys.TextView, true).Set(ContextKeys.ReadOnly, false)
            .Set(ContextKeys.Language, language).Set(ContextKeys.FileKind, kind).Set(ContextKeys.LanguageGroup, "").Set(ContextKeys.HexMode, false).Set(ContextKeys.TableMode, false)
            .Set(ContextKeys.CompareMode, false).Set(ContextKeys.InGitRepo, false).Set(ContextKeys.LogMode, false).Set(ContextKeys.RemoteFile, false).Set(ContextKeys.PackageManifest, false);

    private static bool Available(WorkbenchHost host, string id, IWhenContext context) => host.Commands.TryGet(id, out var c) && c.IsAvailable(context);

    [Fact]
    public void The_new_context_keys_are_known_and_the_menu_keys_are_a_subset_of_them()
    {
        string[] added = ["fileKind", "languageGroup", "logMode", "inGitRepo", "remoteFile", "markdownPreview", "textView", "hasPath", "packageManifest"];
        Assert.All(added, k => Assert.Contains(k, ContextKeys.All));
        Assert.All(ContextKeys.MenuKeys, k => Assert.Contains(k, ContextKeys.All));
        Assert.Equal(ContextKeys.All.Count, ContextKeys.All.Distinct().Count());
    }

    [Fact]
    public void The_commands_of_hex_tables_and_comparisons_are_hidden_outside_their_mode_and_have_no_other_condition_that_greys_them()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var hex = host.Commands.Commands.Where(c => c.Id.StartsWith("nks.hex.", StringComparison.Ordinal)).ToList();
        Assert.True(hex.Count >= 30);
        var inHex = Text().Set(ContextKeys.HexMode, true).Set(ContextKeys.TextView, false);
        Assert.All(hex, c =>
        {
            Assert.False(c.IsAvailable(Text()), c.Id);
            Assert.True(c.IsAvailable(inHex), c.Id);
            Assert.Null(c.Enablement);
        });
        var table = host.Commands.Commands.Where(c => c.Id.StartsWith("nks.table.", StringComparison.Ordinal) && c.Id != "nks.table.editRegion").ToList();
        Assert.Equal(8, table.Count);
        Assert.All(table, c => Assert.False(c.IsAvailable(Text()), c.Id));
        Assert.All(table, c => Assert.True(c.IsAvailable(Text().Set(ContextKeys.TableMode, true)), c.Id));
        Assert.True(Available(host, "nks.table.editRegion", Text()));
        foreach (var id in new[] { "nks.compare.next", "nks.compare.previous", "nks.compare.copyToRight", "nks.compare.copyToLeft", "nks.compare.action1", "nks.compare.action4" })
        {
            Assert.False(Available(host, id, Text()), id);
            Assert.True(Available(host, id, Text().Set(ContextKeys.CompareMode, true)), id);
        }
    }

    [Theory]
    [InlineData("nks.tools.markdownUpdateToc", "markdown", true)]
    [InlineData("nks.tools.markdownUpdateToc", "html", false)]
    [InlineData("nks.tools.markdownPreview", "html", true)]
    [InlineData("nks.tools.markdownPreview", "css", false)]
    [InlineData("nks.tools.copyKeyPath", "yaml", true)]
    [InlineData("nks.tools.copyKeyPath", "json", true)]
    [InlineData("nks.tools.copyKeyPath", "csharp", false)]
    [InlineData("nks.tools.showOutline", "php", true)]
    [InlineData("nks.tools.showOutline", "json", false)]
    [InlineData("nks.tools.emmetPreview", "css", true)]
    [InlineData("nks.tools.emmetWrap", "css", false)]
    [InlineData("nks.tools.sqlDialect", "sql", true)]
    [InlineData("nks.tools.sqlDialect", "plaintext", false)]
    [InlineData("nks.edit.htmlTidy", "html", true)]
    [InlineData("nks.edit.htmlTidy", "xml", false)]
    [InlineData("nks.edit.htmlCloseTag", "xml", true)]
    [InlineData("nks.edit.htmlCloseTag", "python", false)]
    [InlineData("nks.view.modeRead", "markdown", true)]
    [InlineData("nks.view.modeRead", "plaintext", false)]
    public void A_command_of_one_language_is_shown_for_that_language_and_hidden_for_the_others(string id, string language, bool shown)
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.Equal(shown, Available(host, id, Text(language)));
    }

    [Fact]
    public void The_formatters_are_shown_in_every_text_view_and_available_for_their_language_or_a_selection()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.True(host.Commands.TryGet("nks.edit.formatJson", out var json));
        Assert.True(host.Commands.TryGet("nks.edit.formatSql", out var sql));
        Assert.True(json.IsAvailable(Text()));
        Assert.False(json.IsEnabled(Text()));
        Assert.True(json.IsEnabled(Text().Set(ContextKeys.HasSelection, true)));
        Assert.True(json.IsEnabled(Text("json")));
        Assert.False(json.IsAvailable(Text().Set(ContextKeys.TextView, false)));
        Assert.False(sql.IsEnabled(Text("json")));
        Assert.True(sql.IsEnabled(Text("sql")));
        Assert.True(sql.IsEnabled(Text("csharp", "code").Set(ContextKeys.HasSelection, true)));
        Assert.False(json.IsEnabled(Text("json").Set(ContextKeys.ReadOnly, true)));
    }

    [Fact]
    public void Git_package_remote_and_log_commands_follow_the_repository_the_manifest_the_server_and_the_log()
    {
        var host = WorkbenchHost.CreateBuiltin();
        foreach (var id in new[] { "nks.git.status", "nks.git.diffIndex", "nks.git.diffHead", "nks.git.stageHunk", "nks.git.conflictOurs" })
        {
            Assert.False(Available(host, id, Text()), id);
            Assert.True(Available(host, id, Text().Set(ContextKeys.InGitRepo, true)), id);
        }
        Assert.False(Available(host, "nks.packages.lookup", Text("json")));
        Assert.True(Available(host, "nks.packages.lookup", Text("json").Set(ContextKeys.PackageManifest, true)));
        Assert.False(Available(host, "nks.remote.upload", Text()));
        Assert.True(Available(host, "nks.remote.upload", Text().Set(ContextKeys.RemoteFile, true)));
        Assert.False(Available(host, "nks.log.unfold", Text()));
        Assert.True(Available(host, "nks.log.unfold", Text("log").Set(ContextKeys.LogMode, true)));
        Assert.False(Available(host, "nks.tools.lint", Text()));
        Assert.True(Available(host, "nks.tools.lint", Text("csharp").Set(ContextKeys.LanguageGroup, "csharp")));
        Assert.False(Available(host, "nks.lsp.start", Text("plaintext", "text")));
        Assert.True(Available(host, "nks.lsp.start", Text("yaml", "data")));
        Assert.False(Available(host, "nks.lsp.start", Text("csharp", "code").Set(ContextKeys.TextView, false)));
    }

    [Fact]
    public void No_command_is_made_grey_by_the_kind_of_file_alone()
    {
        var host = WorkbenchHost.CreateBuiltin();
        // the language may sit in an enablement only beside a selection (the formatters work on a piece of text in any file); the kind and the group never do
        foreach (var c in host.Commands.Commands)
        {
            if (c.Enablement is not { } enablement) continue;
            Assert.DoesNotContain(ContextKeys.FileKind, enablement.Keys);
            Assert.DoesNotContain(ContextKeys.LanguageGroup, enablement.Keys);
            if (enablement.Keys.Contains(ContextKeys.Language)) Assert.True(enablement.Keys.Contains(ContextKeys.HasSelection), $"{c.Id} is grey for a language: that belongs in `when`");
            Assert.False(enablement.ToString() is "hexMode" or "tableMode" or "compareMode", $"{c.Id} is grey outside a mode: that belongs in `when`");
        }
    }

    [Fact]
    public void The_palette_says_why_a_command_cannot_run_in_every_language()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var keymap = host.BuildKeymap(Platform.Windows);
        foreach (var language in StringTable.UiLanguages)
        {
            var strings = StringTable.LoadBuiltin(language);
            var hits = CommandPalette.Query(host.Commands, keymap, strings, Text(), "", 2000);
            Assert.Equal(strings.Get("reason.hasSelection.false"), hits.Single(h => h.CommandId == "nks.edit.formatJson").DisabledReason);
            Assert.Equal(strings.Get("reason.hasPath.false"), hits.Single(h => h.CommandId == "nks.file.reload").DisabledReason);
            Assert.Equal(strings.Get("reason.multipleCarets.false"), hits.Single(h => h.CommandId == "nks.edit.clearCarets").DisabledReason);
            var logs = CommandPalette.Query(host.Commands, keymap, strings, Text().Set(ContextKeys.TextView, false), "", 2000);
            Assert.Equal(strings.Get("reason.textView.false"), logs.Single(h => h.CommandId == "nks.log.nextError").DisabledReason);
            Assert.DoesNotContain(logs, h => h.CommandId == "nks.edit.formatJson");
            Assert.DoesNotContain(hits, h => h.CommandId.StartsWith("nks.hex.", StringComparison.Ordinal));
            foreach (var key in new[] { "reason.fileType", "reason.textView.false", "reason.hasPath.false", "reason.multipleCarets.false" }) Assert.True(strings.HasOwn(key), $"{language}: {key}");
        }
    }

    [Fact]
    public void A_command_of_a_plugin_for_one_kind_of_file_gets_the_reason_that_it_is_for_another_kind()
    {
        var host = WorkbenchHost.CreateBuiltin();
        const string plugin = """
            { "schemaVersion": 1, "id": "acme.kinds",
              "commands": [ { "id": "acme.kinds.crop", "title": "acme.kinds.crop", "enablement": "fileKind == 'image'" },
                            { "id": "acme.kinds.open", "title": "acme.kinds.open", "when": "remoteFile || fileKind == 'image'" } ] }
            """;
        Assert.Empty(host.AddManifest(plugin, ManifestTrust.Plugin));
        var strings = StringTable.LoadBuiltin("en");
        var hits = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, Text(), "", 2000);
        Assert.Equal("The command is for another kind of file", hits.Single(h => h.CommandId == "acme.kinds.crop").DisabledReason);
        Assert.DoesNotContain(hits, h => h.CommandId == "acme.kinds.open");
        var image = CommandPalette.Query(host.Commands, host.BuildKeymap(Platform.Windows), strings, Text("plaintext", "image"), "", 2000);
        Assert.True(image.Single(h => h.CommandId == "acme.kinds.crop").Enabled);
        Assert.Contains(image, h => h.CommandId == "acme.kinds.open");
    }

    [Fact]
    public void The_editor_context_menu_entries_have_their_own_conditions_and_the_groups_come_in_order()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var json = host.Commands.GetMenu("editor.context", Text("json"));
        var plain = host.Commands.GetMenu("editor.context", Text());
        Assert.Contains(json, i => i.CommandId == "nks.edit.formatJson");
        Assert.DoesNotContain(plain, i => i.CommandId == "nks.edit.formatJson");
        Assert.Equal(["1_clipboard", "2_edit", "4_compare"], plain.Select(i => i.Group).Distinct());
        Assert.Contains("3_type", json.Select(i => i.Group));
        var order = json.Select(i => i.Group).ToList();
        Assert.Equal(order.Order(StringComparer.Ordinal), order);
    }
}

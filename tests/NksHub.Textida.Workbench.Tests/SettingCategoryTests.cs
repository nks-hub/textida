using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P3.6.15: every setting is in exactly one category of the tree of the settings window.</summary>
public class SettingCategoryTests
{
    [Fact]
    public void Every_builtin_setting_is_in_one_category_that_the_tree_has()
    {
        var host = WorkbenchHost.CreateBuiltin();
        Assert.True(host.Settings.Definitions.Count >= 90);
        Assert.Empty(SettingCategories.Problems(host.Settings.Definitions));
        // one key is one setting, so it is in one category only
        Assert.Equal(host.Settings.Definitions.Count, host.Settings.Definitions.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void Every_builtin_setting_gets_a_real_control_and_a_text_box_only_when_it_is_marked_as_text()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var fallback = host.Settings.Definitions.Where(d => SettingFormats.ControlOf(d) == SettingControl.FallbackText).Select(d => d.Key).ToList();
        Assert.True(fallback.Count == 0, "these settings would get a plain text box; give them a format or an enum: " + string.Join(", ", fallback));
        Assert.All(host.Settings.Definitions, d => Assert.Null(SettingFormats.Problem(d)));
        var byControl = host.Settings.Definitions.ToLookup(SettingFormats.ControlOf, d => d.Key);
        Assert.Equal(SettingControl.Font, SettingFormats.ControlOf(Def(host, "editor.fontFamily")));
        Assert.Equal(SettingControl.FontList, SettingFormats.ControlOf(Def(host, "editor.fallbackFontFamily")));
        Assert.Equal(SettingControl.Folder, SettingFormats.ControlOf(Def(host, "files.journalDirectory")));
        Assert.Equal(SettingControl.File, SettingFormats.ControlOf(Def(host, "html.tidyPath")));
        Assert.Equal(SettingControl.List, SettingFormats.ControlOf(Def(host, "files.secretFilePatterns")));
        Assert.Equal(SettingControl.Encoding, SettingFormats.ControlOf(Def(host, "tools.outputEncoding")));
        Assert.Equal(SettingControl.IconSet, SettingFormats.ControlOf(Def(host, "appearance.iconSkin")));
        Assert.Equal(SettingControl.Select, SettingFormats.ControlOf(Def(host, "workbench.keymapProfile")));
        // free text is left only where the content is text: patterns, templates, addresses
        Assert.Equal(["editor.dateTimeFormat", "plugins.catalogUrl", "tools.diffArguments", "web.searchUrl"], byControl[SettingControl.Text].Order());
        Assert.All(host.Settings.Definitions.Where(d => d.Type == "boolean"), d => Assert.Equal(SettingControl.Toggle, SettingFormats.ControlOf(d)));
        Assert.All(host.Settings.Definitions.Where(d => d.Type is "integer" or "number"), d => Assert.Equal(SettingControl.Number, SettingFormats.ControlOf(d)));
    }

    private static SettingDefinition Def(WorkbenchHost host, string key) => host.Settings.TryGet(key, out var d) ? d : throw new KeyNotFoundException(key);

    [Fact]
    public void A_plugin_setting_with_an_unknown_or_misplaced_format_is_refused_and_a_multiselect_checks_each_value()
    {
        Assert.NotNull(SettingFormats.Problem(new SettingDefinition { Key = "a.b", Format = "fancy" }));
        Assert.NotNull(SettingFormats.Problem(new SettingDefinition { Key = "a.b", Type = "integer", Format = "file" }));
        Assert.NotNull(SettingFormats.Problem(new SettingDefinition { Key = "a.b", Format = "multi" }));
        Assert.Null(SettingFormats.Problem(new SettingDefinition { Key = "a.b", Format = "color" }));
        Assert.Equal(SettingControl.FallbackText, SettingFormats.ControlOf(new SettingDefinition { Key = "a.b" }));

        var multi = new SettingDefinition { Key = "a.langs", Format = "multi", Values = ["cs", "en", "de"] };
        Assert.Equal(SettingControl.MultiSelect, SettingFormats.ControlOf(multi));
        Assert.Null(SettingRules.Validate(multi, System.Text.Json.Nodes.JsonValue.Create("cs;de")));
        Assert.Null(SettingRules.Validate(multi, System.Text.Json.Nodes.JsonValue.Create("")));
        Assert.NotNull(SettingRules.Validate(multi, System.Text.Json.Nodes.JsonValue.Create("cs;fr")));
        Assert.Equal(["cs", "de"], SettingFormats.Split(multi, " cs ;; de "));
        var fonts = new SettingDefinition { Key = "a.fonts", Format = "font-list", Separator = "," };
        Assert.Equal("A, B", SettingFormats.Join(fonts, ["A", " B ", ""]));
    }

    [Fact]
    public void A_setting_without_a_category_or_in_a_category_the_tree_lacks_is_reported()
    {
        var good = new SettingDefinition { Key = "a.good", Category = SettingCategories.Files };
        var none = new SettingDefinition { Key = "a.none" };
        var empty = new SettingDefinition { Key = "a.empty", Category = "" };
        var lost = new SettingDefinition { Key = "a.lost", Category = "cat.nowhere" };
        var problems = SettingCategories.Problems([good, none, empty, lost]);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("a.none", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("a.lost", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("a.good", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tree_is_well_formed_and_every_node_has_a_title_in_every_language()
    {
        var ids = SettingCategories.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(SettingCategories.All, c => Assert.True(c.Parent is null || ids.IndexOf(c.Parent) is >= 0 and var parent && parent < ids.IndexOf(c.Id), $"{c.Id}: its parent must be listed before it"));
        Assert.All(SettingCategories.All, c => Assert.True(SettingCategories.PathOf(c.Id).Count <= 3));
        foreach (var language in StringTable.UiLanguages)
        {
            var table = StringTable.LoadBuiltin(language);
            Assert.All(ids, id => Assert.True(table.HasOwn(id), $"{language}: {id}"));
        }
        Assert.Equal([SettingCategories.Editor, SettingCategories.EditorDisplay], SettingCategories.PathOf(SettingCategories.EditorDisplay));
        Assert.Equal(["cat.editor.display", "cat.editor.typing", "cat.editor.format", "cat.editor.encoding"], SettingCategories.All.Where(c => c.Parent == SettingCategories.Editor).Select(c => c.Id));
    }

    [Fact]
    public void The_new_tree_holds_the_appearance_the_editor_and_the_rest_and_the_old_flat_keys_hold_nothing()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var byCategory = host.Settings.Definitions.GroupBy(d => d.Category!).ToDictionary(g => g.Key, g => g.Select(d => d.Key).ToList());
        Assert.Contains("workbench.uiLanguage", byCategory[SettingCategories.Appearance]);
        Assert.Contains("workbench.theme", byCategory[SettingCategories.Appearance]);
        Assert.Contains("editor.tabSize", byCategory[SettingCategories.EditorFormat]);
        Assert.Contains("files.encoding", byCategory[SettingCategories.EditorEncoding]);
        Assert.Contains("history.enabled", byCategory[SettingCategories.History]);
        Assert.Contains("git.gutter", byCategory[SettingCategories.Git]);
        Assert.Contains("hex.structureTemplates", byCategory[SettingCategories.Hex]);
        Assert.All(SettingCategories.Legacy, legacy => Assert.False(byCategory.ContainsKey(legacy), legacy + " is for settings declared the old way"));
        // each part of the editor is a page worth a node
        Assert.All(new[] { SettingCategories.EditorDisplay, SettingCategories.EditorTyping, SettingCategories.EditorFormat, SettingCategories.EditorEncoding, SettingCategories.Files, SettingCategories.Tools, SettingCategories.Advanced },
            id => Assert.True(byCategory[id].Count >= 2, id));
    }

    [Fact]
    public void The_category_of_a_plugin_setting_is_its_name_and_it_lands_under_the_plugins()
    {
        Assert.True(SettingCategories.IsPluginCategory("Acme Formatter"));
        Assert.False(SettingCategories.IsPluginCategory(SettingCategories.Files));
        Assert.False(SettingCategories.IsPluginCategory(null));
        Assert.False(SettingCategories.IsPluginCategory(""));
        Assert.Null(SettingCategories.ParentOf(SettingCategories.Plugins));
    }

    [Fact]
    public void Search_hits_carry_the_category_of_the_setting_and_a_shortcut_belongs_to_the_shortcuts()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var index = new SettingsSearchIndex(host.Settings, StringTable.LoadBuiltin("en"));
        var hits = index.Search("tab", new SettingsStack(host.Settings), host.Commands, host.BuildKeymap(Platform.Windows));
        Assert.Equal(SettingCategories.EditorFormat, hits.Single(h => h.Id == "editor.tabSize").CategoryId);
        Assert.All(hits.Where(h => h.Kind == SearchHitKind.Keybinding), h => Assert.Equal(SettingCategories.Keys, h.CategoryId));
        var all = index.Search("", new SettingsStack(host.Settings), host.Commands, host.BuildKeymap(Platform.Windows), allKeys: true);
        Assert.True(all.Count(h => h.Kind == SearchHitKind.Keybinding) > 50);
        Assert.Empty(index.Search("", new SettingsStack(host.Settings), host.Commands, host.BuildKeymap(Platform.Windows)).Where(h => h.Kind == SearchHitKind.Keybinding));
    }
}

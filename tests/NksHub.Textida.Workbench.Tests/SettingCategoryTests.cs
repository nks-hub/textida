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

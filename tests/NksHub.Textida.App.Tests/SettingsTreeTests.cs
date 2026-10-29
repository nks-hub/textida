using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.15: the settings window as a tree of categories with a page for each, the search that narrows the tree, the language under Appearance, the way back to the defaults and the link to one setting.</summary>
[Collection("LocState")]
public class SettingsTreeTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private SettingsWindow? _view;

    public SettingsTreeTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        _view?.Close();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private SettingsWindow Open(string? query = null, string? key = null)
    {
        _app.OpenSettings(query, key);
        Dispatcher.UIThread.RunJobs();
        return _view = _app.SettingsView!;
    }

    private static IEnumerable<string> Texts(Control row) => row.GetVisualDescendants().OfType<TextBlock>().Select(SearchHighlight.TextOf);

    // ---- the tree ----

    [AvaloniaFact]
    public void Without_a_search_the_tree_lists_the_categories_that_hold_something_and_the_editor_has_its_four_pages_under_it()
    {
        var view = Open();
        var ids = view.TreeIds;
        Assert.Equal(SettingCategories.Appearance, ids[0]);
        Assert.Equal([SettingCategories.Editor, SettingCategories.EditorDisplay, SettingCategories.EditorTyping, SettingCategories.EditorFormat, SettingCategories.EditorEncoding], ids.Skip(1).Take(5));
        foreach (var id in new[] { SettingCategories.Files, SettingCategories.Search, SettingCategories.Hex, SettingCategories.Git, SettingCategories.History, SettingCategories.Tools, SettingCategories.Keys, SettingCategories.System, SettingCategories.Advanced })
            Assert.Contains(id, ids);
        // nothing is listed for a category that holds nothing yet
        Assert.DoesNotContain(SettingCategories.Log, ids);
        Assert.DoesNotContain(SettingCategories.Ai, ids);
        Assert.DoesNotContain(SettingCategories.Plugins, ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(view.TreeItemName(id)), id));
        Assert.Equal(Loc.Get(K.SettingsTitle), view.Title);
        Assert.Equal(SettingCategories.Appearance, view.SelectedCategory);
        Assert.Contains("workbench.theme", view.ShownIds);
    }

    [AvaloniaFact]
    public void Every_setting_is_on_exactly_one_page_of_the_top_level_categories()
    {
        var view = Open();
        var wb = _app.Workbench;
        var keys = wb.Host.Settings.Definitions.Select(d => d.Key).ToHashSet();
        var seen = new List<string>();
        foreach (var top in view.TreeIds.Where(id => SettingCategories.ParentOf(id) is null))
        {
            view.SelectCategory(top);
            seen.AddRange(view.ShownIds.Where(keys.Contains));
        }
        Assert.Equal(keys.Count, seen.Count);
        Assert.Equal(keys.Order(), seen.Order());
    }

    [AvaloniaFact]
    public void A_page_has_its_title_the_path_of_the_category_and_a_name_for_the_screen_reader()
    {
        var view = Open();
        view.SelectCategory(SettingCategories.EditorDisplay);
        Assert.Equal("Editor › Zobrazení", view.PageTitleText);
        Assert.Equal(SettingCategories.EditorDisplay, view.SelectedCategory);
        Assert.Contains("editor.lineNumbers", view.ShownIds);
        Assert.DoesNotContain("editor.tabSize", view.ShownIds);
        Assert.Contains("Editor › Zobrazení", view.GetVisualDescendants().OfType<ScrollViewer>().Select(AutomationProperties.GetName).First(n => n.Contains("Zobrazení", StringComparison.Ordinal)));

        // a category that has others under it lists all of them, each with its heading
        view.SelectCategory(SettingCategories.Editor);
        Assert.Contains("editor.tabSize", view.ShownIds);
        Assert.Contains("editor.lineNumbers", view.ShownIds);
        var headings = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("settinggroup")).Select(t => t.Text).ToList();
        Assert.Equal(["Editor › Zobrazení", "Editor › Psaní", "Editor › Formátování", "Editor › Kódování a konce řádků"], headings);
    }

    [AvaloniaFact]
    public void A_long_category_is_divided_into_sections()
    {
        var view = Open();
        view.SelectCategory(SettingCategories.EditorDisplay);
        var sections = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("settingsection")).Select(t => t.Text).ToList();
        Assert.Equal([Loc.Get(K.SettingsSectionGeneral), Loc.Get(K.SettingsSectionOccurrences)], sections);
        var ids = view.ShownIds.ToList();
        Assert.True(ids.IndexOf("editor.lineNumbers") < ids.IndexOf("editor.occurrences.enabled"));
        view.SelectCategory(SettingCategories.EditorTyping);
        Assert.Contains(Loc.Get(K.SettingsSectionSmartKeys), view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("settingsection")).Select(t => t.Text));
        // a category with nothing to divide has no headings
        view.SelectCategory(SettingCategories.Search);
        Assert.Empty(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("settingsection")));
    }

    // ---- the language ----

    [AvaloniaFact]
    public void The_language_of_the_interface_is_chosen_under_Appearance_and_takes_effect_at_once()
    {
        var view = Open();
        var row = view.RowOf("workbench.uiLanguage")!;
        var combo = row.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(["auto", "cs", "en", "sk", "de"], combo.ItemsSource!.Cast<string>());
        Assert.Equal(AutomationProperties.GetName(combo), "Jazyk rozhraní");
        Assert.Equal("Čeština", SettingsWindow.ValueLabel("workbench.uiLanguage", "cs"));
        Assert.Equal("Podle systému", SettingsWindow.ValueLabel("workbench.uiLanguage", "auto"));

        combo.SelectedItem = "en";
        Assert.Equal("en", _app.Settings.Language);
        Assert.Equal("Settings", view.Title);
        Assert.Equal("Appearance", view.TreeItemName(SettingCategories.Appearance));
        Assert.Equal("en", _app.Workbench.Settings.GetString("workbench.uiLanguage"));
        Assert.Contains("\"workbench.uiLanguage\": \"en\"", File.ReadAllText(_app.Workbench.UserSettingsPath));

        foreach (var language in Loc.Languages)
        {
            view.RowOf("workbench.uiLanguage")!.GetVisualDescendants().OfType<ComboBox>().Single().SelectedItem = language;
            Assert.Equal(language, _app.Settings.Language);
            Assert.Equal(Loc.Get(K.SettingsTitle), view.Title);
        }
    }

    // ---- the search ----

    [AvaloniaFact]
    public void The_search_narrows_the_tree_to_the_categories_with_a_match_counts_them_and_marks_the_words()
    {
        var view = Open("tab");
        var ids = view.TreeIds;
        Assert.Contains(SettingCategories.Editor, ids);
        Assert.Contains(SettingCategories.EditorFormat, ids);
        Assert.Contains(SettingCategories.Keys, ids);
        Assert.DoesNotContain(SettingCategories.Hex, ids);
        Assert.DoesNotContain(SettingCategories.History, ids);
        Assert.Equal(view.ShownIds.Count, view.TreeIds.Where(id => SettingCategories.ParentOf(id) is null).Sum(id => CountOf(view, id)));

        // the count is shown after the name and the screen reader gets it in words
        int inFormat = CountOf(view, SettingCategories.EditorFormat);
        Assert.True(inFormat >= 4);
        Assert.Equal($"Formátování ({inFormat})", view.TreeItemText(SettingCategories.EditorFormat));
        Assert.Equal(Loc.Plural(K.SettingsTreeCount, inFormat, ("category", "Formátování")), view.TreeItemName(SettingCategories.EditorFormat));

        // the matched part is bold in the title, the key and the description
        var row = view.RowOf("editor.tabSize")!;
        var marked = row.GetVisualDescendants().OfType<TextBlock>().SelectMany(SearchHighlight.MarkedOf).ToList();
        Assert.Contains("tab", marked, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(marked, m => m.Equals("Tab", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(Loc.Plural(K.SettingsResultCount, view.ShownIds.Count), view.CountText);

        // the groups of the page are the categories with a match, and choosing a node leaves the search alone
        var headings = view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("settinggroup")).Select(t => t.Text!).ToList();
        Assert.Contains("Editor › Formátování", headings);
        Assert.Contains("Klávesové zkratky", headings);
        Assert.Equal("tab", view.Query);
    }

    private static int CountOf(SettingsWindow view, string id)
    {
        string text = view.TreeItemText(id);
        int open = text.LastIndexOf('('), close = text.LastIndexOf(')');
        return open >= 0 && close > open ? int.Parse(text[(open + 1)..close]) : 0;
    }

    [AvaloniaFact]
    public void A_search_without_a_match_says_so_and_clearing_it_brings_the_pages_back()
    {
        var view = Open("xyzzy-nothing");
        Assert.Empty(view.TreeIds);
        Assert.Empty(view.ShownIds);
        Assert.Contains(Loc.Get(K.SettingsNoResults), view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.Equal(Loc.Plural(K.SettingsResultCount, 0), view.CountText);
        view.SetQuery("");
        Assert.NotEmpty(view.TreeIds);
        Assert.Contains("workbench.theme", view.ShownIds);
    }

    [AvaloniaFact]
    public void The_search_finds_a_setting_by_title_description_key_and_value()
    {
        var view = Open("kodovani");
        Assert.Contains("files.encoding", view.ShownIds);
        view.SetQuery("editor.tabSize");
        Assert.Equal("editor.tabSize", view.ShownIds.First());
        view.SetQuery("dark"); // the value of the theme only when it is changed: the default is "system", so a word of the description finds it
        Assert.Contains("workbench.theme", view.ShownIds);
        _app.Workbench.Set("workbench.theme", "dark");
        view.SetQuery("dark");
        Assert.Contains("workbench.theme", view.ShownIds);
    }

    [AvaloniaFact]
    public void The_marks_cover_the_letters_the_search_ignores_in_every_language()
    {
        Assert.Equal([(0, 8)], SearchHighlight.Ranges("Kódování", ["kodovani"]));
        Assert.Equal([(0, 9)], SearchHighlight.Ranges("Schließen", ["schliessen"]));
        Assert.Equal([(5, 3)], SearchHighlight.Ranges("Auto tab size", ["tab"]));
        Assert.Equal([(0, 3), (4, 4)], SearchHighlight.Ranges("Tab size", ["tab", "size"]));
        Assert.Empty(SearchHighlight.Ranges("Nothing", ["tab"]));
        Assert.Empty(SearchHighlight.Ranges("", ["tab"]));
        // words that touch or overlap are one mark
        Assert.Equal([(0, 7)], SearchHighlight.Ranges("tabsize", ["tab", "size"]));
        Assert.Equal([(0, 5)], SearchHighlight.Ranges("table", ["tab", "able"]));
        Assert.Equal(["tab", "size"], SearchHighlight.Words("Tab  size tab"));
    }

    // ---- the way back ----

    [AvaloniaFact]
    public async Task A_changed_setting_is_marked_and_its_reset_button_names_it()
    {
        var view = Open();
        view.SelectCategory(SettingCategories.EditorFormat);
        var row = view.RowOf("editor.tabSize")!;
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("settingchanged") && t.IsVisible);
        Assert.False(view.ResetCategoryButton.IsVisible);

        Assert.Null(_app.Workbench.Set("editor.tabSize", JsonValue.Create(2)));
        row = view.RowOf("editor.tabSize")!;
        var mark = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("settingchanged"));
        Assert.True(mark.IsVisible);
        Assert.Equal(Loc.Get(K.SettingsChanged), mark.Text);
        Assert.Equal(Loc.Get(K.SettingsRowChanged, ("title", "Velikost tabulátoru"), ("value", "2")), AutomationProperties.GetName(row));
        var reset = row.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == Loc.Get(K.SettingsReset));
        Assert.Equal(Loc.Get(K.SettingsResetRowA11y, ("title", "Velikost tabulátoru")), AutomationProperties.GetName(reset));
        Assert.True(view.ResetCategoryButton.IsVisible);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task A_category_goes_back_to_its_defaults_after_the_question_and_only_what_the_user_changed()
    {
        var view = Open();
        Assert.Null(_app.Workbench.Set("editor.tabSize", JsonValue.Create(2)));
        Assert.Null(_app.Workbench.Set("editor.insertSpaces", JsonValue.Create(false)));
        Assert.Null(_app.Workbench.Set("editor.lineNumbers", JsonValue.Create(false))); // another page of the editor
        view.SelectCategory(SettingCategories.EditorFormat);
        Assert.True(view.ResetCategoryButton.IsVisible);
        Assert.Equal(Loc.Get(K.SettingsResetCategoryA11y, ("category", "Formátování")), AutomationProperties.GetName(view.ResetCategoryButton));

        // cancelled: nothing changes
        _dialogs.Answers.Enqueue(1);
        Assert.Equal(0, await view.ResetCategoryAsync());
        Assert.Equal(2, _app.Workbench.Settings.GetInt("editor.tabSize"));
        var asked = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Plural(K.SettingsResetCategoryAsk, 2, ("category", "Formátování")), asked.Message);
        Assert.Equal(Loc.Get(K.SettingsResetCategory), asked.Title);

        _dialogs.Answers.Enqueue(0);
        Assert.Equal(2, await view.ResetCategoryAsync());
        Assert.Equal(4, _app.Workbench.Settings.GetInt("editor.tabSize"));
        Assert.True(_app.Workbench.Settings.GetBool("editor.insertSpaces"));
        Assert.False(_app.Workbench.Settings.GetBool("editor.lineNumbers"));
        Assert.DoesNotContain("editor.tabSize", File.ReadAllText(_app.Workbench.UserSettingsPath));
        Assert.Contains("editor.lineNumbers", File.ReadAllText(_app.Workbench.UserSettingsPath));
        Assert.False(view.ResetCategoryButton.IsVisible);

        // the button of the page asks the same question for the page it is on
        _dialogs.Answers.Enqueue(0);
        view.SelectCategory(SettingCategories.EditorDisplay);
        Assert.True(view.ResetCategoryButton.IsVisible);
        view.ResetCategoryButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Until(() => _app.Workbench.Settings.GetBool("editor.lineNumbers"));
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    private static async Task Until(Func<bool> condition)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 10_000, "Not reached");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    // ---- a link to one setting ----

    [AvaloniaFact]
    public void Opening_at_a_key_shows_its_page_with_the_row_framed()
    {
        var view = Open(key: "files.encoding");
        Assert.Equal(SettingCategories.EditorEncoding, view.SelectedCategory);
        Assert.Equal("", view.Query);
        Assert.Contains("found", view.RowOf("files.encoding")!.Classes);
        Assert.Contains(AutomationProperties.GetName(view.RowOf("files.encoding")!), view.CountText);

        // the window is open already: the same call moves it
        _app.OpenSettings(key: "history.maxAgeDays");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(view, _app.SettingsView);
        Assert.Equal(SettingCategories.History, view.SelectedCategory);
        Assert.Contains("found", view.RowOf("history.maxAgeDays")!.Classes);
        Assert.DoesNotContain("found", view.RowOf("history.enabled")!.Classes);

        // a shortcut leads to the page of the shortcuts, a search that would hide the setting is cleared
        view.SetQuery("tab");
        _app.OpenSettings(key: "nks.view.zoomIn");
        Assert.Equal(SettingCategories.Keys, view.SelectedCategory);
        Assert.Equal("", view.Query);
        Assert.Contains("found", view.RowsOf("nks.view.zoomIn").First().Classes);
    }

    [AvaloniaFact]
    public void Opening_with_a_search_and_a_key_keeps_the_search()
    {
        var view = Open("tab", "editor.tabSize");
        Assert.Equal("tab", view.Query);
        Assert.Contains("found", view.RowOf("editor.tabSize")!.Classes);
    }

    // ---- shortcuts, system and plugins ----

    [AvaloniaFact]
    public void The_shortcuts_page_lists_every_shortcut_and_the_keymap_profile()
    {
        var view = Open();
        view.SelectCategory(SettingCategories.Keys);
        Assert.Contains("workbench.keymapProfile", view.ShownIds);
        Assert.Contains("nks.file.save", view.ShownIds);
        Assert.True(view.ShownIds.Count > 80);
    }

    [AvaloniaFact]
    public void The_settings_of_a_plugin_are_listed_in_a_node_of_their_own_under_Plugins()
    {
        var manifest = new ContributionManifest { Id = "acme.kit", Settings = [new SettingDefinition { Key = "acme.kit.level", Type = "integer", Default = JsonValue.Create(3), Title = "acme.kit.level", Scope = "application" }] };
        Assert.Empty(_app.Workbench.AddPlugin(manifest, "Acme Kit"));
        var view = Open();
        Assert.Contains(SettingCategories.Plugins, view.TreeIds);
        string node = SettingsWindow.PluginNodePrefix + "Acme Kit";
        Assert.Contains(node, view.TreeIds);
        Assert.Equal("Acme Kit", view.TreeItemName(node));
        view.SelectCategory(node);
        Assert.Equal("Pluginy › Acme Kit", view.PageTitleText);
        Assert.Equal(["acme.kit.level"], view.ShownIds);
        view.SelectCategory(SettingCategories.Plugins);
        Assert.Equal(["acme.kit.level"], view.ShownIds);
        view.SetQuery("level");
        Assert.Contains(node, view.TreeIds);
        Assert.Equal(1, CountOf(view, node));
    }

    // ---- languages and gates ----

    [AvaloniaFact]
    public void The_window_shows_no_raw_key_and_no_empty_name_in_any_language()
    {
        var view = Open();
        foreach (var language in Loc.Languages)
        {
            _app.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
            foreach (var id in view.TreeIds.Where(id => SettingCategories.ParentOf(id) is null).Take(6))
            {
                view.SelectCategory(id);
                Assert.All(view.GetVisualDescendants().OfType<TextBlock>(), t => Assert.DoesNotMatch(@"^(cat|setting|settings|a11y|source)\.[A-Za-z0-9.]+$", SearchHighlight.TextOf(t)));
                Assert.All(view.TreeIds, tree => Assert.False(string.IsNullOrWhiteSpace(view.TreeItemName(tree))));
            }
            Assert.Equal(Loc.Get(K.SettingsTitle), view.Title);
        }
    }
}

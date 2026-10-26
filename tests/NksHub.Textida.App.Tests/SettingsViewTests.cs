using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The searchable settings view over <c>SettingsSearchIndex</c>: values, where they come from, and shortcut conflicts.</summary>
[Collection("LocState")]
public class SettingsViewTests : IDisposable
{
    private readonly TempDir _temp = new();
    private App _app;
    private SettingsWindow? _view;

    public SettingsViewTests() => _app = TestApp.Prepare(_temp);

    public void Dispose()
    {
        _view?.Close();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private SettingsWindow Open(string? query = null)
    {
        _app.OpenSettings(query);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return _view = _app.SettingsView!;
    }

    private static IEnumerable<string> Texts(Control row) => row.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    [AvaloniaFact]
    public void Searching_tab_finds_every_indent_and_tab_setting_with_its_value_source()
    {
        var view = Open("tab");
        foreach (var id in new[] { "editor.tabSize", "editor.indentSize", "editor.insertSpaces", "editor.detectIndentation" })
        {
            Assert.Contains(id, view.ShownIds);
            var row = view.RowOf(id)!;
            Assert.Contains(Texts(row), t => t == Loc.Get(K.SettingsValueSource, ("source", Loc.Get(K.SourceDefault))));
        }
        Assert.Contains(view.ShownIds, id => id == "nks.edit.tab"); // the Tab key row
    }

    [AvaloniaFact]
    public void Search_ignores_diacritics_and_lists_settings_by_title()
    {
        var view = Open("kodovani");
        Assert.Contains("files.encoding", view.ShownIds);
        view.SetQuery("");
        Assert.True(view.ShownIds.Count >= 15);
        Assert.Equal(Loc.Plural(K.SettingsResultCount, view.ShownIds.Count), view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.Contains("výsled") == true).Text);
    }

    [AvaloniaFact]
    public void Changing_a_value_writes_the_user_file_changes_the_source_and_applies_at_once()
    {
        var tab = new MainWindowHolder(_app).Window.NewDocument();
        var view = Open("tabSize");
        var row = view.RowOf("editor.tabSize")!;
        var box = row.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text = "2";
        box.Focus();
        TestApp.Press(view, Key.Enter);

        Assert.Equal(2, _app.Workbench.Settings.GetInt("editor.tabSize"));
        Assert.Contains("\"editor.tabSize\": 2", File.ReadAllText(_app.Workbench.UserSettingsPath));
        row = view.RowOf("editor.tabSize")!;
        Assert.Contains(Texts(row), t => t == Loc.Get(K.SettingsValueSource, ("source", Loc.Get(K.SourceUser))));
        Assert.Equal(2, tab.Editor!.Options.IndentationSize);

        var reset = row.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == Loc.Get(K.SettingsReset));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4, _app.Workbench.Settings.GetInt("editor.tabSize"));
        Assert.Contains(Texts(view.RowOf("editor.tabSize")!), t => t.Contains(Loc.Get(K.SourceDefault)));
    }

    [AvaloniaFact]
    public void A_value_outside_the_limits_is_refused_and_the_file_stays_as_it_was()
    {
        var view = Open("tabSize");
        var box = view.RowOf("editor.tabSize")!.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text = "99";
        box.Focus();
        TestApp.Press(view, Key.Enter);
        Assert.Equal(4, _app.Workbench.Settings.GetInt("editor.tabSize"));
        Assert.False(File.Exists(_app.Workbench.UserSettingsPath));
        var shown = view.RowOf("editor.tabSize")!.GetVisualDescendants().OfType<TextBlock>().Select(t => (t.IsVisible ? "+" : "-") + t.Text).ToList();
        Assert.True(shown.Any(t => t.StartsWith("+Nepovedlo se uložit", StringComparison.Ordinal)), string.Join(" | ", shown));
    }

    [AvaloniaFact]
    public void Boolean_and_choice_settings_have_their_own_editors()
    {
        var view = Open("");
        var check = view.RowOf("editor.insertSpaces")!.GetVisualDescendants().OfType<CheckBox>().Single();
        check.IsChecked = false;
        Assert.False(_app.Workbench.Settings.GetBool("editor.insertSpaces"));

        view = _app.SettingsView!;
        var combo = view.RowOf("workbench.theme")!.GetVisualDescendants().OfType<ComboBox>().Single();
        combo.SelectedItem = "dark";
        Assert.Equal("dark", _app.Settings.Theme);
        _app.SetTheme("system");
    }

    [AvaloniaFact]
    public void Shortcut_conflicts_are_listed_and_the_rows_are_marked()
    {
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile),
            """{ "schemaVersion": 1, "bindings": [ { "key": "mod+alt+7", "command": "nks.view.zoomIn" }, { "key": "mod+alt+7", "command": "nks.view.zoomOut" } ] }""");
        _view?.Close();
        _app = TestApp.Restart(_temp, _app);
        var view = Open("");
        Assert.Contains(view.BannerTexts, t => t.StartsWith("Konflikt zkratek", StringComparison.Ordinal) && t.Contains("Přiblížit") && t.Contains("Oddálit") && t.Contains("stejná vrstva"));

        view.SetQuery("přiblížit");
        Assert.Contains(view.RowsOf("nks.view.zoomIn"), r => Texts(r).Any(t => t.StartsWith("Konflikt s příkazem", StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public void An_overridden_default_binding_is_shown_as_information_not_as_a_conflict_of_the_row()
    {
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile),
            """{ "schemaVersion": 1, "bindings": [ { "key": "mod+s", "command": "nks.file.close" } ] }""");
        _view?.Close();
        _app = TestApp.Restart(_temp, _app);
        var view = Open("");
        Assert.Contains(view.BannerTexts, t => t.Contains("přepsáno vyšší vrstvou"));
    }

    [AvaloniaFact]
    public void A_damaged_keymap_file_is_reported_and_the_defaults_stay()
    {
        File.WriteAllText(Path.Combine(_app.ProfileDirectory, AppWorkbench.UserKeymapFile), "{ not json");
        _view?.Close();
        _app = TestApp.Restart(_temp, _app);
        var view = Open("");
        Assert.Contains(view.BannerTexts, t => t.StartsWith("Soubor zkratek se nepoužil", StringComparison.Ordinal));
        Assert.NotEmpty(_app.Workbench.Keymap.Bindings);
    }

    [AvaloniaFact]
    public void A_settings_file_from_a_newer_version_is_never_overwritten()
    {
        var path = Path.Combine(_app.ProfileDirectory, AppWorkbench.UserSettingsFile);
        Directory.CreateDirectory(_app.ProfileDirectory);
        File.WriteAllText(path, """{ "schemaVersion": 99, "settings": { "future.key": 1 } }""");
        _view?.Close();
        _app = TestApp.Restart(_temp, _app);
        var before = File.ReadAllText(path);
        Assert.NotNull(_app.Workbench.Set("editor.tabSize", JsonValue.Create(3)));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(4, _app.Workbench.Settings.GetInt("editor.tabSize"));
    }

    [AvaloniaFact]
    public void The_view_follows_the_language()
    {
        var view = Open("");
        _app.SetLanguage("en");
        Assert.Equal("Settings", view.Title);
        _app.SetLanguage("cs");
        Assert.Equal("Nastavení", view.Title);
    }

    private sealed class MainWindowHolder(App app)
    {
        public MainWindow Window { get; } = app.Windows.FirstOrDefault() ?? app.CreateWindow(null);
    }
}

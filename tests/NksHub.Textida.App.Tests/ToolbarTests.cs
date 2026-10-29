using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Icons;
using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.13: the user toolbar. Off by default, switched on in the View menu, buttons from toolbar.json with an editor, and a button whose command is missing stays hidden.</summary>
[Collection("LocState")]
public class ToolbarTests : IDisposable
{
    private const string Sep = ToolbarLayout.Separator;

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ToolbarTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private bool Registered(string id) => _app.Workbench.Commands.TryGet(id, out _) && _app.Workbench.Commands.HasHandler(id);

    private IReadOnlyList<string> ExpectedDefault() => ToolbarLayout.Shown(ToolbarLayout.Default, Registered);

    private Button ButtonOf(string id) => _w.ToolbarButtons.Single(b => (string)b.Tag! == id);

    private string TitleOf(string id)
    {
        Assert.True(_app.Workbench.Commands.TryGet(id, out var command), id);
        return _app.Workbench.Strings.Get(command.TitleKey);
    }

    private static IEnumerable<MenuCommand> Commands(IEnumerable<MenuNode> nodes) => nodes.SelectMany(n => n switch
    {
        MenuCommand c => [c],
        MenuGroup g => Commands(g.Items),
        _ => [],
    });

    [AvaloniaFact]
    public void The_toolbar_is_off_until_the_view_menu_switches_it_on_and_off()
    {
        Assert.False(_w.ToolbarShown);
        Assert.False(_app.Workbench.Settings.GetBool("workbench.toolbar"));
        Assert.False(_w.ToolbarHost.IsVisible);
        Assert.Empty(_w.ToolbarButtons);

        var toggle = Commands(_w.MenuModel.SelectMany(g => g.Items)).Single(c => c.CommandId == "nks.view.toggleToolbar");
        Assert.Equal(false, toggle.Checked);
        Assert.Contains(Commands(_w.MenuModel.SelectMany(g => g.Items)), c => c.CommandId == "nks.view.editToolbar");

        Assert.Equal(CommandOutcome.Handled, Run("nks.view.toggleToolbar"));
        Assert.True(_w.ToolbarShown);
        Assert.True(_w.ToolbarHost.IsVisible);
        Assert.NotEmpty(_w.ToolbarButtons);
        Assert.Equal(true, Commands(_w.MenuModel.SelectMany(g => g.Items)).Single(c => c.CommandId == "nks.view.toggleToolbar").Checked);
        Assert.Equal(Loc.Get(K.ToolbarA11y), AutomationProperties.GetName(_w.ToolbarHost));

        Assert.Equal(CommandOutcome.Handled, Run("nks.view.toggleToolbar"));
        Assert.False(_w.ToolbarShown);
        Assert.False(_w.ToolbarHost.IsVisible);
    }

    [AvaloniaFact]
    public void The_setting_is_the_switch_and_a_second_window_follows_it()
    {
        var second = _app.CreateWindow(null, look: TestApp.WindowsLook);
        _app.Workbench.Set("workbench.toolbar", JsonValue.Create(true));
        Assert.True(_w.ToolbarHost.IsVisible);
        Assert.True(second.ToolbarHost.IsVisible);
        Assert.Equal(_w.ToolbarDrawn, second.ToolbarDrawn);
        _app.Workbench.Set("workbench.toolbar", JsonValue.Create(false));
        Assert.False(_w.ToolbarHost.IsVisible);
        Assert.False(second.ToolbarHost.IsVisible);
    }

    [AvaloniaFact]
    public void The_first_buttons_are_drawn_in_order_with_their_icons_names_and_shortcuts()
    {
        Run("nks.view.toggleToolbar");
        Assert.Equal(ExpectedDefault(), _w.ToolbarDrawn);
        Assert.Equal(_w.ToolbarDrawn.Where(b => b != Sep), _w.ToolbarButtons.Select(b => (string)b.Tag!));
        // the Git commands come with the Git work: while one is not registered its button is not there
        foreach (var id in ToolbarLayout.Default.Where(id => id != Sep && !Registered(id))) Assert.DoesNotContain(id, _w.ToolbarDrawn);
        foreach (var id in new[] { "nks.file.new", "nks.file.open", "nks.file.save", "nks.file.saveAll", "nks.edit.undo", "nks.edit.redo", "nks.search.find", "nks.search.replace" })
        {
            var button = ButtonOf(id);
            Assert.Equal(TitleOf(id), AutomationProperties.GetName(button));
            Assert.Same(_app.Workbench.Commands.IconOf(id), Assert.IsType<IconView>(button.Content).Shape);
            string tip = Assert.IsType<string>(ToolTip.GetTip(button));
            Assert.StartsWith(TitleOf(id), tip, StringComparison.Ordinal);
            if (_app.Workbench.Keymap.ShortcutsFor(id).FirstOrDefault() is { } shortcut) Assert.Contains(shortcut, tip, StringComparison.Ordinal);
            Assert.True(button.Focusable);
        }
        Assert.True(_w.ToolbarDrawn.Count(b => b == Sep) >= 2); // the groups are told apart by separators
    }

    [AvaloniaFact]
    public void A_button_is_greyed_while_its_command_cannot_run_for_the_open_document()
    {
        Run("nks.view.toggleToolbar");
        Assert.True(ButtonOf("nks.file.new").IsEnabled);
        Assert.True(ButtonOf("nks.file.open").IsEnabled);
        Assert.False(ButtonOf("nks.file.save").IsEnabled); // needs a document
        Assert.False(ButtonOf("nks.search.find").IsEnabled);
        _w.OpenFile(_temp.File("a.txt", "alpha\n"), null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ButtonOf("nks.file.save").IsEnabled);
        Assert.True(ButtonOf("nks.search.find").IsEnabled);
        foreach (var tab in _w.Tabs.ToList()) _w.CloseTab(tab);
        Dispatcher.UIThread.RunJobs();
        Assert.False(ButtonOf("nks.file.save").IsEnabled);
    }

    [AvaloniaFact]
    public void A_click_runs_the_command_like_the_menu_does()
    {
        Run("nks.view.toggleToolbar");
        int before = _w.Tabs.Count();
        Click(ButtonOf("nks.file.new"));
        Assert.Equal(before + 1, _w.Tabs.Count());
        Assert.True(ButtonOf("nks.file.save").IsEnabled); // the state follows the command that ran
    }

    [AvaloniaFact]
    public void A_button_for_a_command_that_is_not_there_stays_in_the_file_and_stays_hidden()
    {
        Run("nks.view.toggleToolbar");
        Assert.True(_app.Toolbar.Save(["nks.file.new", "acme.gone.run", Sep, "nks.file.open", Sep, "acme.gone.other"], out _));
        Assert.Equal(["nks.file.new", Sep, "nks.file.open"], _w.ToolbarDrawn);
        Assert.Contains("acme.gone.run", File.ReadAllText(_app.Toolbar.FilePath), StringComparison.Ordinal);

        // the plugin comes: its button appears, with the icon the plugin brought
        var commands = _app.Workbench.Commands;
        Assert.Empty(commands.AddManifest(new ContributionManifest { Id = "acme.gone", Commands = [new CommandContribution { Id = "acme.gone.run", Title = "t", Icon = "play" }] }));
        commands.SetHandler("acme.gone.run", (_, _) => ValueTask.FromResult(CommandOutcome.Handled));
        _w.RefreshToolbar();
        Assert.Equal(["nks.file.new", "acme.gone.run", Sep, "nks.file.open"], _w.ToolbarDrawn);
        Assert.Same(BuiltinIcons.All["play"], Assert.IsType<IconView>(ButtonOf("acme.gone.run").Content).Shape);

        // and goes again
        commands.RemoveSource("acme.gone");
        _w.RefreshToolbar();
        Assert.Equal(["nks.file.new", Sep, "nks.file.open"], _w.ToolbarDrawn);
    }

    [AvaloniaFact]
    public void A_command_without_an_icon_shows_its_name_on_the_button()
    {
        Run("nks.view.toggleToolbar");
        Assert.True(_app.Toolbar.Save(["nks.edit.tab", "nks.file.new"], out _));
        var button = ButtonOf("nks.edit.tab");
        Assert.Equal(TitleOf("nks.edit.tab"), Assert.IsType<TextBlock>(button.Content).Text);
        Assert.Equal(TitleOf("nks.edit.tab"), AutomationProperties.GetName(button));
    }

    [AvaloniaFact]
    public void The_buttons_are_named_in_every_language_and_follow_a_switch()
    {
        Run("nks.view.toggleToolbar");
        var seen = new HashSet<string>();
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get(K.ToolbarA11y), AutomationProperties.GetName(_w.ToolbarHost));
            foreach (var button in _w.ToolbarButtons)
            {
                var name = AutomationProperties.GetName(button);
                Assert.Equal(TitleOf((string)button.Tag!), name);
                Assert.False(name.Contains("cmd.", StringComparison.Ordinal), name);
            }
            seen.Add(AutomationProperties.GetName(ButtonOf("nks.file.save")));
        }
        Assert.Equal(Loc.Languages.Count(), seen.Count);
    }

    [AvaloniaFact]
    public void A_file_that_cannot_be_read_is_ignored_and_the_reason_is_shown()
    {
        File.WriteAllText(_app.Toolbar.FilePath, "{ not json");
        _w.OpenFile(_temp.File("a.txt", "alpha\n"), null);
        Run("nks.view.toggleToolbar");
        Assert.Equal(ExpectedDefault(), _w.ToolbarDrawn);
        Assert.NotNull(_app.Toolbar.Problem);
        Assert.Contains(_app.Toolbar.Problem!, _w.StatusLeftText, StringComparison.Ordinal);
        var editor = _w.ShowToolbarEditor();
        Assert.Contains(_app.Toolbar.Problem!, editor.NoteText, StringComparison.Ordinal);
        editor.Close();
    }

    [AvaloniaFact]
    public void The_toolbar_is_in_the_configuration_backup_and_the_store_writes_it_atomically()
    {
        Assert.True(_app.Toolbar.Save(["nks.file.new", Sep, "nks.file.open"], out var detail), detail);
        Assert.Equal(["nks.file.new", Sep, "nks.file.open"], _app.Toolbar.Load());
        Assert.Null(_app.Toolbar.Problem);
        Assert.False(File.Exists(_app.Toolbar.FilePath + ".tmp"));
        string zip = Path.Combine(_temp.Path, "backup.zip");
        ConfigBackup.Export(_app.ProfileDirectory, zip, []);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == ToolbarLayout.FileName);
    }

    // ---- the editor ----

    private ToolbarEditorWindow Editor()
    {
        var editor = _w.ShowToolbarEditor();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    [AvaloniaFact]
    public void The_editor_lists_the_commands_that_can_be_buttons_and_the_buttons_of_the_profile()
    {
        var editor = Editor();
        Assert.Equal(Loc.Get(K.ToolbarEditTitle), editor.Title);
        Assert.Equal(ToolbarLayout.Default, editor.Buttons);
        var wb = _app.Workbench;
        var offered = wb.Commands.Commands.Where(c => !c.Hidden && wb.Commands.HasHandler(c.Id)).Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(offered, editor.AvailableIds.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(editor.AvailableIds, id => wb.Commands.TryGet(id, out var c) && c.Hidden);
        // a button of a command that is not there is listed and named as not available
        var missing = ToolbarLayout.Default.Where(id => id != Sep && !Registered(id)).ToList();
        foreach (var id in missing) Assert.False(editor.Lines[ToolbarLayout.Default.ToList().IndexOf(id)].Available);
        Assert.True(editor.Lines.Where(l => l.Id != Sep && !missing.Contains(l.Id)).All(l => l.Available && l.Icon is not null));
        Assert.Equal(Loc.Plural(K.ToolbarEditCount, ToolbarLayout.Default.Count(b => b != Sep)), editor.CountText);
        editor.Close();
    }

    [AvaloniaFact]
    public void Add_puts_a_command_after_the_chosen_button_and_a_second_add_of_it_is_refused()
    {
        var editor = Editor();
        editor.SelectButton(0);
        Assert.True(editor.AddCommand("nks.edit.copy"));
        Assert.Equal(["nks.file.new", "nks.edit.copy", "nks.file.open"], editor.Buttons.Take(3));
        Assert.Equal(1, editor.CurrentList.SelectedIndex);
        Assert.Contains(TitleOf("nks.edit.copy"), editor.NoteText, StringComparison.Ordinal);
        Assert.False(editor.AddCommand("nks.edit.copy"));
        Assert.Equal(Loc.Get(K.ToolbarEditHasAlready, ("title", TitleOf("nks.edit.copy"))), editor.NoteText);
        Assert.Single(editor.Buttons, b => b == "nks.edit.copy");
        editor.Close();
    }

    [AvaloniaFact]
    public void The_search_narrows_the_list_and_enter_or_the_button_adds_the_chosen_command()
    {
        var editor = Editor();
        editor.SetSearch("uloz");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("nks.file.save", editor.AvailableIds);
        Assert.DoesNotContain("nks.edit.copy", editor.AvailableIds);
        editor.SetSearch("zzzzqqqq");
        Assert.Empty(editor.AvailableIds);
        Assert.False(editor.AddButton.IsEnabled);
        editor.SetSearch("kopírovat");
        Assert.Contains("nks.edit.copy", editor.AvailableIds);
        editor.SelectAvailable("nks.edit.copy");
        editor.AvailableList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("nks.edit.copy", editor.Buttons[^1]); // nothing was chosen on the right, so it goes to the end
        Assert.Equal(ToolbarLayout.Default.Count + 1, editor.Buttons.Count);
        editor.Close();
    }

    [AvaloniaFact]
    public void Remove_and_the_moves_change_the_list_and_the_keys_do_the_same()
    {
        var editor = Editor();
        editor.SelectButton(1);
        Assert.True(editor.MoveSelected(1));
        Assert.Equal(["nks.file.new", "nks.file.save", "nks.file.open"], editor.Buttons.Take(3));
        Assert.Equal(2, editor.CurrentList.SelectedIndex);
        Assert.True(editor.MoveSelected(-1));
        Assert.Equal(["nks.file.new", "nks.file.open", "nks.file.save"], editor.Buttons.Take(3));
        editor.SelectButton(0);
        Assert.False(editor.MoveSelected(-1));
        Assert.False(editor.UpButton.IsEnabled);
        editor.SelectButton(editor.Buttons.Count - 1);
        Assert.False(editor.DownButton.IsEnabled);

        editor.SelectButton(1);
        editor.CurrentList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt });
        Assert.Equal(["nks.file.new", "nks.file.save", "nks.file.open"], editor.Buttons.Take(3));
        editor.CurrentList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up, KeyModifiers = KeyModifiers.Alt });
        Assert.Equal(["nks.file.new", "nks.file.open", "nks.file.save"], editor.Buttons.Take(3));

        int before = editor.Buttons.Count;
        editor.CurrentList.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
        Assert.Equal(before - 1, editor.Buttons.Count);
        Assert.DoesNotContain("nks.file.open", editor.Buttons);
        Assert.True(editor.RemoveSelected());
        Assert.Equal(before - 2, editor.Buttons.Count);
        editor.Close();
    }

    [AvaloniaFact]
    public void A_separator_can_be_added_and_is_cleaned_when_it_is_saved()
    {
        var editor = Editor();
        editor.SelectButton(0);
        Assert.True(editor.AddSeparator());
        Assert.Equal(Sep, editor.Buttons[1]);
        Assert.Equal(Loc.Get(K.ToolbarEditSeparatorName), editor.Lines[1].Title);
        Assert.True(editor.AddSeparator()); // two in a row...
        Assert.True(editor.Save());
        Assert.False(editor.IsVisible);
        Assert.Equal(1, _app.Toolbar.Load().Take(4).Count(b => b == Sep)); // ...are one on the disk
        Assert.Equal(["nks.file.new", Sep, "nks.file.open"], _app.Toolbar.Load().Take(3));
    }

    [AvaloniaFact]
    public void The_toolbar_holds_at_most_the_placeholder_limit_and_the_editor_says_when_it_is_full()
    {
        var editor = Editor();
        int max = PluginLimitsPlaceholders.ToolbarMaxButtons;
        foreach (var id in editor.AvailableIds)
        {
            if (editor.Buttons.Count >= max) break;
            editor.AddCommand(id);
        }
        Assert.Equal(max, editor.Buttons.Count);
        var another = editor.AvailableIds.First(id => !editor.Buttons.Contains(id));
        Assert.False(editor.AddCommand(another));
        Assert.Equal(Loc.Plural(K.ToolbarEditFull, max), editor.NoteText);
        Assert.False(editor.AddSeparator());
        Assert.Equal(max, editor.Buttons.Count);
        editor.Close();
    }

    [AvaloniaFact]
    public void Restore_defaults_brings_the_first_set_back_and_cancel_leaves_the_file_alone()
    {
        Assert.True(_app.Toolbar.Save(["nks.file.new"], out _));
        string before = File.ReadAllText(_app.Toolbar.FilePath);
        var editor = Editor();
        Assert.Equal(["nks.file.new"], editor.Buttons);
        editor.AddCommand("nks.edit.copy");
        editor.RestoreDefaults();
        Assert.Equal(ToolbarLayout.Default, editor.Buttons);
        Click(editor.CancelButton);
        Assert.False(editor.IsVisible);
        Assert.Equal(before, File.ReadAllText(_app.Toolbar.FilePath));
        Assert.Equal(["nks.file.new"], _app.Toolbar.Load());
    }

    [AvaloniaFact]
    public void OK_saves_and_every_toolbar_changes_at_once()
    {
        Run("nks.view.toggleToolbar");
        var second = _app.CreateWindow(null, look: TestApp.WindowsLook);
        Assert.True(second.ToolbarHost.IsVisible);
        var editor = Editor();
        editor.AddCommand("nks.edit.copy");
        Click(editor.OkButton);
        Assert.False(editor.IsVisible);
        Assert.Contains("nks.edit.copy", File.ReadAllText(_app.Toolbar.FilePath), StringComparison.Ordinal);
        Assert.Contains("nks.edit.copy", _w.ToolbarDrawn);
        Assert.Contains("nks.edit.copy", second.ToolbarDrawn);
        Assert.Equal(TitleOf("nks.edit.copy"), AutomationProperties.GetName(_w.ToolbarButtons.Single(b => (string)b.Tag! == "nks.edit.copy")));
    }

    [AvaloniaFact]
    public void A_second_request_for_the_editor_brings_the_open_one_forward_and_the_menu_opens_it()
    {
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.editToolbar"));
        var first = Assert.Single(_w.OwnedWindows.OfType<ToolbarEditorWindow>());
        Assert.Same(first, _w.ShowToolbarEditor());
        Assert.Single(_w.OwnedWindows.OfType<ToolbarEditorWindow>());
        first.Close();
        Assert.Empty(_w.OwnedWindows.OfType<ToolbarEditorWindow>());
    }

    [AvaloniaFact]
    public void A_file_that_cannot_be_written_is_reported_in_the_editor_and_the_editor_stays_open()
    {
        var editor = Editor();
        Directory.CreateDirectory(_app.Toolbar.FilePath + ".tmp"); // a folder where the temporary file goes
        editor.AddCommand("nks.edit.copy");
        Assert.False(editor.Save());
        Assert.True(editor.IsVisible);
        Assert.StartsWith(Loc.Get(K.ToolbarEditWriteFailed, ("reason", "")).Trim(), editor.NoteText, StringComparison.Ordinal);
        Directory.Delete(_app.Toolbar.FilePath + ".tmp");
        editor.Close();
    }

    [AvaloniaFact]
    public void The_editor_names_every_control_and_follows_the_language()
    {
        var editor = Editor();
        foreach (var lang in Loc.Languages)
        {
            _app.SetLanguage(lang);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get(K.ToolbarEditTitle), editor.Title);
            Assert.Equal(Loc.Get(K.ToolbarEditSearch), AutomationProperties.GetName(editor.SearchBox));
            Assert.Equal(Loc.Get(K.ToolbarEditAvailable), AutomationProperties.GetName(editor.AvailableList));
            Assert.Equal(Loc.Get(K.ToolbarEditCurrent), AutomationProperties.GetName(editor.CurrentList));
            foreach (var (button, key) in new[] { (editor.AddButton, K.ToolbarEditAdd), (editor.AddSeparatorButton, K.ToolbarEditAddSeparator), (editor.RemoveButton, K.ToolbarEditRemove),
                (editor.UpButton, K.ToolbarEditUp), (editor.DownButton, K.ToolbarEditDown), (editor.DefaultsButton, K.ToolbarEditDefaults), (editor.OkButton, K.DialogOk), (editor.CancelButton, K.DialogCancel) })
                Assert.Equal(Loc.Get(key), button.Content);
            Assert.Equal(Loc.Plural(K.ToolbarEditCount, editor.Buttons.Count(b => b != Sep)), editor.CountText);
        }
        editor.Close();
    }
}

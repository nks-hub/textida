using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Colors;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.1.1: the gate that walks the visual trees of the main window, of every window the program opens and of every dialog it can show, in the four languages
/// of the interface. A control the keyboard reaches must have a role and a name that is a text of the language in use. This is the part of the
/// accessibility of the program that a headless run can prove; what NVDA and VoiceOver say needs a person with a screen reader.
/// </summary>
[Collection("LocState")]
public class AccessibilityGateTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AccessibilityGateTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string[] Data => ["alpha.txt", "data.csv", "blob.bin", "big.log", "detail-one", "detail-two", _temp.Path];

    private void Tick()
    {
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Walks one state of the window in all four languages. The state is built once, in the default language, so every other language is a switch while the screen is open.</summary>
    private void Walk(GateRun run, string scenario, Func<Visual> root, Action<string>? prepare = null)
    {
        foreach (var lang in A11yAudit.Languages)
        {
            _app.SetLanguage(lang);
            prepare?.Invoke(lang);
            Tick();
            run.Audit(scenario, lang, root());
        }
        _app.SetLanguage(Loc.DefaultLanguage);
        Tick();
    }

    // ---- the main window ----

    [AvaloniaFact]
    public async Task Every_control_the_keyboard_reaches_in_the_main_window_has_a_role_and_a_translated_name()
    {
        var run = new GateRun(Data);
        var text = _w.OpenFile(_temp.File("alpha.txt", "alpha\nbeta\n"), null);
        var csv = _w.OpenFile(_temp.File("data.csv", "id,name\r\n1,Ann\r\n2,Bob\r\n"), null);
        var bin = _w.OpenFile(_temp.Bytes("blob.bin", [.. Enumerable.Range(0, 64).Select(i => (byte)i)]), null);

        // the text tab with the panels around it, the menu bar and the notice above the document
        _w.SelectTab(text);
        _w.ToggleDocuments(true);
        _w.ToggleOutput(true);
        _w.ToggleAgentPanel(true);
        _w.FindBar.Open(files: false, replace: true);
        Walk(run, "main:text", () => _w, lang => text.ShowBar(new InfoBar(Loc.Get(K.HexBarBinary), [(Loc.Get(K.HexBarOpen), () => { }), (Loc.Get(K.HexBarKeep), () => { })], warning: true)));
        text.ShowBar(null);

        _w.FindBar.Open(files: true, replace: false);
        Walk(run, "main:find-in-files", () => _w);
        _w.FindBar.Close();
        _w.ToggleAgentPanel(false);
        _w.ToggleOutput(false);

        // the files of a folder in the panel on the left
        Assert.True(await _w.ShowFolderAsync(_temp.Path));
        Tick();
        Walk(run, "main:files", () => _w);
        _w.ToggleFiles(false);

        // the table of a CSV file
        _w.SelectTab(csv);
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.table"));
        Walk(run, "main:table", () => _w);
        Run("nks.view.table");

        // the hex view with the panels beside it
        Assert.True(_w.SetHex(bin.Doc, true));
        var hex = bin.Doc.Views.First(v => v.Hex is not null);
        _w.SelectTab(hex);
        _w.ToggleInspector();
        _w.ToggleHexBookmarkPanel();
        _w.ToggleHexTemplate();
        _w.ToggleHexStatistics();
        Walk(run, "main:hex", () => _w);

        // the comparison of two texts
        _w.SelectTab(text);
        await _w.Clipboard!.SetTextAsync("alpha\ngamma\n");
        var compare = await _w.CompareWithClipboardAsync();
        Assert.NotNull(compare);
        await ((Editing.DiffView)compare!.Custom!).Session.RefreshAsync();
        Walk(run, "main:compare", () => _w);

        // the result list of a search in files
        _w.SelectTab(text);
        _w.ShowFindInFiles(replace: false);
        _w.FindBar.FolderBox.Text = _temp.Path;
        _w.FindBar.FindBox.Text = "alpha";
        await _w.RunFilesSearchAsync();
        for (int i = 0; i < 200 && _w.ResultsView is { IsRunning: true }; i++) { await Task.Delay(25); Dispatcher.UIThread.RunJobs(); }
        Assert.NotNull(_w.ResultsView);
        Walk(run, "main:results", () => _w);

        // a large file, shown by the paged view
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var big = _w.OpenFile(_temp.Bytes("big.log", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 150_000).Select(i => $"line {i}\n")))), null); // the threshold is in MiB
        Assert.True(big.Doc.IsLarge);
        for (int i = 0; i < 400 && !big.Paged!.Index.Complete; i++) { await Task.Delay(25); Dispatcher.UIThread.RunJobs(); }
        Walk(run, "main:paged", () => _w);

        // two groups of tabs side by side, with the splitter between them
        _w.SelectTab(text);
        Assert.NotNull(_w.SplitView());
        Assert.Equal(2, _w.Groups.Count);
        Walk(run, "main:split", () => _w);

        run.AssertClean();
    }

    [AvaloniaFact]
    public void The_palette_in_all_its_modes_names_its_input_and_its_rows()
    {
        var run = new GateRun(Data);
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.OpenFile(_temp.File("beta.txt", "beta\n"), null);

        _w.ShowPalette();
        Walk(run, "palette:commands", () => _w.PaletteHost);
        _w.ClosePalette();

        _w.ShowQuickOpen();
        Walk(run, "palette:files", () => _w.PaletteHost);
        _w.ClosePalette();

        _w.ShowEditLocations();
        Walk(run, "palette:edits", () => _w.PaletteHost);
        _w.ClosePalette();

        // the texts of a choice are the caller's, written in the language of the moment they are asked, so each language asks again
        Walk(run, "palette:choice", () => _w.PaletteHost, _ => _w.ShowChoice(Loc.Get(K.PaletteWatermark), Loc.Get(K.A11yPalette),
            [new QuickRow(Loc.Get(K.DialogOk), null, null, null, true, null, Loc.Get(K.DialogOk), () => { }), new QuickRow(Loc.Get(K.DialogCancel), null, null, null, true, null, Loc.Get(K.DialogCancel), () => { })]));
        _w.ClosePalette();

        run.AssertClean();
    }

    [AvaloniaFact]
    public void The_menus_of_the_window_and_the_flyouts_of_the_status_bar_name_every_item()
    {
        var run = new GateRun(Data);
        var tab = _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.SelectTab(tab);
        foreach (var lang in A11yAudit.Languages)
        {
            _app.SetLanguage(lang);
            Tick();
            if (_w.MenuModel.Count == 0) run.Problems.Add($"[menu/{lang}] the window has no menu");
            run.Audit("menu:bar", lang, A11yAudit.CollectMenu(MenuRenderer.ToMenu(_w.MenuModel).Items));
            foreach (var (scenario, open) in new (string, Func<MenuFlyout?>)[] { ("menu:indent", _w.ShowIndentMenu), ("menu:eol", _w.ShowEolMenu), ("menu:language", _w.ShowLanguageMenu) })
            {
                var flyout = open();
                Assert.NotNull(flyout);
                run.Audit(scenario, lang, A11yAudit.CollectMenu(flyout.Items));
                flyout.Hide();
            }
            run.Audit("menu:tab", lang, A11yAudit.CollectMenu(_w.ActiveGroup.Strip.Items.OfType<TabStripItem>().Select(i => i.ContextMenu).OfType<ContextMenu>().SelectMany(m => m.Items.OfType<object>())));
        }
        _app.SetLanguage(Loc.DefaultLanguage);
        run.AssertClean();
    }

    // ---- windows ----

    [AvaloniaFact]
    public void The_settings_window_and_the_help_window_name_every_control()
    {
        var run = new GateRun(Data);
        _app.OpenSettings();
        var settings = _app.SettingsView!;
        _app.OpenHelp();
        var help = _app.HelpView!;
        Walk(run, "window:settings", () => settings);
        Walk(run, "window:help", () => help);
        run.AuditOrder("window:settings", settings);
        run.AuditOrder("window:help", help);
        run.AssertClean();
    }

    [AvaloniaFact]
    public void The_tool_windows_name_every_control_and_are_visited_in_reading_order()
    {
        var run = new GateRun(Data);
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.ShowRegexTester();
        _w.ShowToolEditor();
        _w.ShowLogPatternTester();
        Tick();
        var windows = _w.OwnedWindows.ToList();
        Assert.Contains(windows, w => w is RegexTesterWindow);
        Assert.Contains(windows, w => w is ToolEditorWindow);
        Assert.Contains(windows, w => w is LogPatternWindow);
        foreach (var window in windows)
        {
            string scenario = "window:" + window.GetType().Name;
            Walk(run, scenario, () => window);
            run.AuditOrder(scenario, window);
        }
        run.AssertClean();
    }

    [AvaloniaFact]
    public void The_hex_comparison_window_names_every_control_and_is_visited_in_reading_order()
    {
        var run = new GateRun(Data);
        var bin = _w.OpenFile(_temp.Bytes("blob.bin", [.. Enumerable.Range(0, 64).Select(i => (byte)i)]), null);
        Assert.True(_w.SetHex(bin.Doc, true));
        var hex = bin.Doc.Views.First(v => v.Hex is not null);
        _w.SelectTab(hex);
        _w.CompareHexWithSaved(hex);
        Tick();
        var window = Assert.Single(_w.OwnedWindows.OfType<HexCompareWindow>());
        Walk(run, "window:HexCompareWindow", () => window);
        run.AuditOrder("window:HexCompareWindow", window);
        run.AssertClean();
    }

    // ---- dialogs ----

    private async Task AuditDialog(GateRun run, string scenario, string lang, Func<AvaloniaDialogs, Task> open, bool order)
    {
        var dialogs = new AvaloniaDialogs();
        dialogs.Shown = window => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                window.UpdateLayout();
                if (string.IsNullOrWhiteSpace(window.Title)) run.Problems.Add($"[{scenario}/{lang}] the dialog has no title");
                run.Audit(scenario, lang, window);
                if (order) run.AuditOrder(scenario, window);
                var cancel = window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsCancel);
                if (cancel is null)
                {
                    run.Problems.Add($"[{scenario}/{lang}] the dialog has no button for Esc");
                    window.Close(null);
                }
                else Click(cancel);
            }
            catch (Exception e)
            {
                run.Problems.Add($"[{scenario}/{lang}] the dialog could not be walked: {e.GetType().Name}: {e.Message}");
                window.Close(null);
            }
        }, DispatcherPriority.Background);
        await open(dialogs).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [AvaloniaFact]
    public async Task Every_dialog_the_program_can_show_names_every_control_in_every_language()
    {
        var run = new GateRun(Data);
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        var fields = new List<ClipField>
        {
            new() { Name = "title", Label = "", Type = "text" },
            new() { Name = "when", Label = "", Type = "date" },
            new() { Name = "color", Label = "", Type = "color" },
            new() { Name = "file", Label = "", Type = "file" },
            new() { Name = "kind", Label = "", Type = "choice", Choices = ["detail-one", "detail-two"] },
        };
        (TextCodec, int)[] targets = [(TextCodec.Utf8, 0), (TextCodec.Cp1252, 2)];

        (string Scenario, Func<AvaloniaDialogs, Task> Open)[] dialogs =
        [
            ("dialog:choose", d => d.ChooseAsync(_w, new ChoiceRequest(Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsPath),
                [new DialogButton(Loc.Get(K.DialogSave), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], ["detail-one", "detail-two"]))),
            ("dialog:prompt", d => d.PromptAsync(_w, Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsPath), "x", s => s)),
            ("dialog:prompt-secret", d => d.PromptSecretAsync(_w, Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsPath))),
            ("dialog:save-as", d => d.SaveAsAsync(_w, new SaveAsRequest(Path.Combine(_temp.Path, "x.txt"), TextCodec.Utf8, targets))),
            ("dialog:pick-encoding", d => d.PickEncodingAsync(_w, Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsEncoding), targets, TextCodec.Utf8)),
            ("dialog:about", d => d.ShowAboutAsync(_w)),
            ("dialog:hex-bookmark", d => d.HexBookmarkAsync(_w, new HexBookmarkForm("name", "#0F7B6C", "comment", null, "0x0-0x10"))),
            ("dialog:insert-into-lines", d => d.InsertIntoLinesAsync(_w, new LineInsertOptions(), _ => "preview")),
            ("dialog:sort-lines", d => d.SortLinesAsync(_w, new SortOptions(), _ => "preview")),
            ("dialog:table-region", d => d.TableRegionAsync(_w, CsvDialect.Csv, _ => "preview")),
            ("dialog:clip-form", d => d.ClipFormAsync(_w, Loc.Get(K.DialogSaveAsTitle), WithLabels(fields), new Dictionary<string, string>(),
                _ => new ClipFormState(new Dictionary<string, FieldProblem>(), "preview", null))),
        ];

        foreach (var (scenario, open) in dialogs)
            foreach (var lang in A11yAudit.Languages)
            {
                _app.SetLanguage(lang);
                await AuditDialog(run, scenario, lang, open, order: lang == Loc.DefaultLanguage);
            }
        _app.SetLanguage(Loc.DefaultLanguage);
        run.AssertClean();
    }

    /// <summary>The labels of the fields of a clip are the text of its author; here they are texts of the catalogue, so each language has its own.</summary>
    private static IReadOnlyList<ClipField> WithLabels(List<ClipField> fields)
    {
        string[] labels = [Loc.Get(K.DialogSaveAsPath), Loc.Get(K.DialogSaveAsEncoding), Loc.Get(K.HexBmColor), Loc.Get(K.DialogBrowse), Loc.Get(K.HexBmName)];
        for (int i = 0; i < fields.Count; i++) fields[i].Label = labels[i];
        return fields;
    }

    [AvaloniaFact]
    public void The_colour_picker_names_its_sliders_and_its_value_box()
    {
        var run = new GateRun(Data);
        Assert.True(ColorLiterals.TryParse("#336699", out var match));
        foreach (var lang in A11yAudit.Languages)
        {
            _app.SetLanguage(lang);
            var window = new Window { Content = new Editing.ColorPickerPanel(match), Width = 320, Height = 320 };
            window.Show();
            window.UpdateLayout();
            run.Audit("popup:colour-picker", lang, window);
            window.Close();
        }
        _app.SetLanguage(Loc.DefaultLanguage);
        run.AssertClean();
    }

    [AvaloniaFact]
    public void The_cell_editor_of_the_table_has_a_name_that_says_which_cell_it_edits()
    {
        var csv = _w.OpenFile(_temp.File("data.csv", "id,name\r\n1,Ann\r\n"), null);
        _w.SelectTab(csv);
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.table"));
        Tick();
        var table = _w.ActiveTable!;
        table.BeginEdit();
        Tick();
        var editor = table.GetVisualDescendants().OfType<TextBox>().Single(t => t.IsVisible);
        var (name, role) = A11yAudit.Read(editor);
        Assert.Equal(AutomationControlType.Edit, role);
        Assert.Contains(table.DescribeCell(), name);
        Assert.Empty(A11yAudit.Problems(new A11yAudit.Item("cell editor", name, role), A11yAudit.Keys(), Data));
        table.CancelEdit();
    }

    // ---- the Tab key ----

    [AvaloniaFact]
    public void Tab_visits_the_parts_of_the_main_window_in_the_order_they_are_laid_out()
    {
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.ToggleDocuments(true);
        _w.ToggleOutput(true);
        _w.ToggleAgentPanel(true);
        _w.FindBar.Open(files: false, replace: true);
        Tick();

        var sequence = A11yAudit.TabSequence(_w);
        Assert.NotEmpty(sequence);
        var problems = A11yAudit.ReadingOrderProblems(_w, sequence).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        int At(Control c)
        {
            int i = sequence.ToList().IndexOf(c);
            Assert.True(i >= 0, $"{A11yAudit.Describe(c)} is not visited by Tab");
            return i;
        }
        var tabItem = _w.ActiveGroup.Strip.GetVisualDescendants().OfType<TabStripItem>().First();
        var outputText = _w.OutputPanelControl.GetVisualDescendants().OfType<TextBox>().First();
        int panel = At(_w.DocumentFilter), tabs = At(tabItem), agent = At(_w.AgentInputBox), output = At(outputText), find = At(_w.FindBar.FindBox), status = At(_w.EncodingItem);
        Assert.True(panel < tabs && tabs < agent && agent < output && output < find && find < status,
            $"documents {panel}, tabs {tabs}, agent {agent}, output {output}, find {find}, status {status}: the parts are not visited from top to bottom");
    }

    [AvaloniaFact]
    public void F6_moves_the_keyboard_between_the_parts_of_the_window_and_Shift_F6_goes_back()
    {
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.ToggleOutput(true);
        _w.FindBar.Open(files: false, replace: false);
        Tick();
        _w.FocusActiveView();
        Tick();

        string Where() =>
            _w.OutputPanelControl.IsKeyboardFocusWithin ? "output" :
            _w.FindBar.IsKeyboardFocusWithin ? "find" :
            _w.IndentItem.IsKeyboardFocusWithin ? "status" :
            _w.ActiveGroup.IsKeyboardFocusWithin ? "editor" : "elsewhere";

        Assert.Equal("editor", Where());
        List<string> forward = [];
        for (int i = 0; i < 4; i++)
        {
            TestApp.Press(_w, Key.F6);
            Tick();
            forward.Add(Where());
        }
        Assert.Equal(["output", "find", "status", "editor"], forward);

        List<string> back = [];
        for (int i = 0; i < 4; i++)
        {
            TestApp.Press(_w, Key.F6, RawInputModifiers.Shift);
            Tick();
            back.Add(Where());
        }
        Assert.Equal(["status", "find", "output", "editor"], back);
    }

    // ---- the keyboard reaches every command ----

    private static IEnumerable<string> CommandIds(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
            switch (node)
            {
                case MenuCommand { CommandId: { } id }: yield return id; break;
                case MenuGroup group: foreach (var id in CommandIds(group.Items)) yield return id; break;
            }
    }

    [AvaloniaFact]
    public void Every_command_can_be_reached_from_the_keyboard()
    {
        var wb = _app.Workbench;
        var inMenu = CommandIds(_w.MenuModel).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(inMenu);

        // the palette and the menus open from the keyboard; every command that is not hidden is in the palette
        Assert.NotEmpty(wb.Keymap.ShortcutsFor("nks.view.commandPalette"));
        Assert.NotEmpty(wb.Keymap.ShortcutsFor("nks.view.nextPane"));
        Assert.NotEmpty(wb.Keymap.ShortcutsFor("nks.view.previousPane"));

        // a hidden command is in no list, so only a key or a menu reaches it; the caret and delete keys are the keys of the editor itself, not entries of the keymap
        var editorKeys = Editing.EditorSurface.StepCommands.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var lost = wb.Commands.Commands
            .Where(c => c.Hidden && wb.Commands.HasHandler(c.Id) && !wb.Keymap.ShortcutsFor(c.Id).Any() && !inMenu.Contains(c.Id) && !editorKeys.Contains(c.Id))
            .Select(c => c.Id).ToList();
        Assert.True(lost.Count == 0, "hidden commands that no key and no menu reaches: " + string.Join(", ", lost));

        // a command that does something is not left without a title in a language, or the palette would list it by its id
        foreach (var lang in A11yAudit.Languages)
        {
            var strings = StringTable.LoadBuiltin(lang);
            var untitled = wb.Commands.Commands.Where(c => !c.Hidden && wb.Commands.HasHandler(c.Id) && !strings.HasOwn(c.TitleKey)).Select(c => c.Id).ToList();
            Assert.True(untitled.Count == 0, $"{lang}: commands without a title: " + string.Join(", ", untitled));
        }
    }

    // ---- focus goes back after a dialog ----

    [AvaloniaFact]
    public async Task A_dialog_gives_the_keyboard_back_to_the_control_that_had_it()
    {
        _w.OpenFile(_temp.File("alpha.txt", "alpha\n"), null);
        _w.FindBar.Open(files: false, replace: false);
        Tick();
        var box = _w.FindBar.FindBox;
        Assert.True(box.Focus(NavigationMethod.Tab));
        Tick();
        Assert.Same(box, _w.FocusManager!.GetFocusedElement());

        var dialogs = new AvaloniaDialogs();
        // the window moves the keyboard away while the dialog is open (the system does the same when the window loses its activation)
        dialogs.Shown = window => Dispatcher.UIThread.Post(() =>
        {
            _w.FocusActiveView();
            Click(window.GetVisualDescendants().OfType<Button>().First(b => b.IsCancel));
        }, DispatcherPriority.Background);

        await dialogs.PromptAsync(_w, Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsPath), "x").WaitAsync(TimeSpan.FromSeconds(30));
        Tick();
        Assert.Same(box, _w.FocusManager!.GetFocusedElement());

        await dialogs.ChooseAsync(_w, new ChoiceRequest(Loc.Get(K.DialogSaveAsTitle), Loc.Get(K.DialogSaveAsPath), [new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)])).WaitAsync(TimeSpan.FromSeconds(30));
        Tick();
        Assert.Same(box, _w.FocusManager!.GetFocusedElement());
    }

    // ---- the focus ring (D5) ----

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_keyboard_focus_is_drawn_in_the_accent_colour_and_a_click_draws_no_ring(string theme)
    {
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        _app.RequestedThemeVariant = variant;
        try
        {
            var list = new ListBox { Items = { new ListBoxItem { Content = "item" } } };
            Control[] controls =
            [
                new Button { Content = "button" }, new ToggleButton { Content = "toggle" }, new CheckBox { Content = "check" }, new RadioButton { Content = "radio" },
                new TextBox(), new ComboBox(), new Slider(), list.Items.OfType<ListBoxItem>().First(),
            ];
            var panel = new StackPanel();
            foreach (var c in controls.Where(c => c is not ListBoxItem)) panel.Children.Add(c);
            panel.Children.Add(list);
            var window = new Window { Content = panel, Width = 400, Height = 700 };
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(_app.TryFindResource("Accent", variant, out var found));
            var accent = ((ISolidColorBrush)found!).Color;

            foreach (var control in controls)
            {
                Assert.True(control.Focus(NavigationMethod.Tab), control.GetType().Name);
                Dispatcher.UIThread.RunJobs();
                var rings = AdornerLayer.GetAdornerLayer(control)!.Children.OfType<Border>().ToList();
                var ring = Assert.Single(rings);
                Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(ring.BorderBrush).Color);
                Assert.True(ring.BorderThickness.Left >= 2, $"{control.GetType().Name}: the ring is thinner than 2 px");
            }
            foreach (var control in controls)
            {
                control.Focus(NavigationMethod.Pointer);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(AdornerLayer.GetAdornerLayer(control)!.Children);
            }
            window.Close();
        }
        finally { _app.RequestedThemeVariant = ThemeVariant.Default; }
    }

    // ---- messages are announced ----

    [AvaloniaFact]
    public void A_message_that_matters_is_announced_through_a_live_region_and_the_counters_are_live()
    {
        _w.SetStatusMessage("a message");
        Assert.Equal("a message", _w.AnnouncedText);
        Assert.Equal(AutomationLiveSetting.Polite, _w.AnnouncedUrgency);

        var tab = _w.NewDocument();
        tab.ShowBar(new InfoBar("a warning", [], warning: true));
        Tick();
        Assert.Equal("a warning", _w.AnnouncedText);
        Assert.Equal(AutomationLiveSetting.Assertive, _w.AnnouncedUrgency);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(_w.FindBar.CountText));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(_w.AgentStatusControl));
    }

    [AvaloniaFact]
    public void The_row_the_palette_chooses_and_an_empty_result_are_announced()
    {
        _w.ShowPalette();
        Tick();
        Assert.NotEmpty(_w.QuickResults);
        Assert.Equal(_w.QuickResults[0].AutomationName, _w.AnnouncedText);
        _w.MovePalette(1);
        Assert.Equal(_w.QuickResults[1].AutomationName, _w.AnnouncedText);
        _w.SetPaletteQuery("zzzzqqqq");
        Tick();
        Assert.Empty(_w.QuickResults);
        Assert.Equal(_w.PaletteEmptyText, _w.AnnouncedText);
        Assert.NotEmpty(_w.AnnouncedText);
    }

    // ---- the help ----

    [AvaloniaTheory]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("sk")]
    [InlineData("de")]
    public void The_help_describes_the_keyboard_and_the_high_contrast_in_every_language(string language)
    {
        var topic = HelpCatalog.Find(language, "app.accessibility");
        Assert.NotNull(topic);
        Assert.Contains("nks.view.nextPane", topic.Commands);
        Assert.Contains("nks.view.previousPane", topic.Commands);
        string text = string.Join('\n', topic.Lines);
        Assert.Contains("F6", text);
        Assert.True(text.Contains("Shift+F6") || text.Contains("Umschalt+F6"), "the key that goes back");
        Assert.Contains("Alt", text);
    }
}

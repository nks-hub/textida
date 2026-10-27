using System.Diagnostics;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Tests;

/// <summary>Palette modes, Go to Anything, the document and file panels, recent edit places, the related file and what the panel does to files.</summary>
[Collection("LocState")]
public class NavigationTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;
    private MainWindow _w;

    public NavigationTests()
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

    private static async Task WaitFor(Func<bool> condition, int milliseconds = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for the UI");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    /// <summary>A small project: a controller, a view, docs and a header with two possible sources.</summary>
    private string Project()
    {
        string root = Path.Combine(_temp.Path, "proj");
        foreach (var (name, text) in new[]
        {
            ("src/Mvc/Controllers/HomeController.cs", "class HomeController {}\nline 2\n"),
            ("src/Mvc/Views/Index.cshtml", "<h1>index</h1>\n"),
            ("src/Data/Repository.cs", "class Repository {}\n"),
            ("docs/readme.md", "# readme\n"),
            ("native/foo.h", "#pragma once\n"), ("native/foo.c", "int foo;\n"), ("native/foo.cpp", "int foo2;\n"),
            ("native/bar.c", "int bar;\n"), ("native/bar.h", "int bar;\n"),
            ("shaders/a.vert", "v\n"), ("shaders/a.frag", "f\n"),
        })
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return root;
    }

    // ---- P2.1.1 command palette ----

    [AvaloniaFact]
    public void Palette_calculator_shows_the_result_and_refuses_bad_input_without_running_anything()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("=1+2*3");
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("= 7", row.Title);
        Assert.True(row.Enabled);
        Assert.Empty(_w.PaletteResults);

        _w.SetPaletteQuery("=1/0");
        Assert.Equal("Dělení nulou", _w.QuickResults[0].Title);
        Assert.False(_w.QuickResults[0].Enabled);
        _w.SetPaletteQuery("=1+");
        Assert.Equal("Neplatný výraz", _w.QuickResults[0].Title);
        _w.SetPaletteQuery("=System.Environment.Exit(3)");
        Assert.False(_w.QuickResults[0].Enabled);

        TestApp.Press(_w, Key.Enter);
        Assert.True(_w.IsPaletteOpen); // an error row does not close the palette
        _w.ClosePalette();
    }

    [AvaloniaFact]
    public void Palette_slash_lists_settings_and_enter_opens_the_settings_filtered_by_the_text()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("/tab");
        Assert.Contains(_w.QuickResults, r => r.AutomationName.Contains("Velikost tabulátoru", StringComparison.OrdinalIgnoreCase) || r.Title.Contains("tabul", StringComparison.OrdinalIgnoreCase));
        Assert.All(_w.QuickResults, r => Assert.NotNull(r.Category));
        TestApp.Press(_w, Key.Enter);
        Assert.False(_w.IsPaletteOpen);
        Assert.NotNull(_app.SettingsView);
        Assert.Equal("tab", _app.SettingsView!.Query);
        _app.SettingsView.Close();
    }

    [AvaloniaFact]
    public void Palette_marks_a_command_that_cannot_run_with_the_reason_and_find_all_save_commands_with_keys()
    {
        _w.ShowPalette();
        _w.SetPaletteQuery("ulož");
        var save = _w.PaletteResults.Where(r => r.CommandId.StartsWith("nks.file.save", StringComparison.Ordinal)).ToList();
        Assert.True(save.Count >= 3);
        Assert.All(save, r => Assert.Equal("Není otevřený žádný dokument", r.DisabledReason));
        Assert.All(save.Where(r => r.CommandId is not ("nks.file.saveAll" or "nks.file.saveActions")), r => Assert.NotNull(r.Shortcut));
        Assert.Contains(_w.QuickResults, r => r.Reason == "Není otevřený žádný dokument" && !r.Enabled && r.AutomationName.Contains("Není otevřený žádný dokument"));

        _w.NewDocument();
        _w.SetPaletteQuery("ulož");
        Assert.All(_w.PaletteResults.Where(r => r.CommandId.StartsWith("nks.file.save", StringComparison.Ordinal)), r => Assert.Null(r.DisabledReason));
        _w.ClosePalette();
    }

    // ---- P2.1.2 Go to Anything, recent files ----

    [AvaloniaFact]
    public async Task Go_to_anything_finds_a_file_by_parts_of_its_path_and_opens_it_and_the_preview_leaves_the_recent_list_alone()
    {
        string root = Project();
        Assert.True(await _w.ShowFolderAsync(root));
        var other = _w.OpenFile(Path.Combine(root, "docs", "readme.md"), null);
        var recentBefore = _app.Recent.Files.ToList();
        Assert.Single(recentBefore);

        TestApp.Press(_w, Key.P, TestApp.Mod);
        Assert.True(_w.IsPaletteOpen);
        Assert.Equal(PaletteMode.Files, _w.PaletteModeNow);
        _w.SetPaletteQuery("mvc ctrl");
        await WaitFor(() => _w.QuickResults.Any(r => r.Title == "HomeController.cs"));
        Assert.Equal("HomeController.cs", _w.QuickResults[0].Title);
        Assert.Equal("src/Mvc/Controllers", _w.QuickResults[0].Detail);
        Assert.Contains("class HomeController", _w.PalettePreviewText);

        // moving through the list only previews
        TestApp.Press(_w, Key.Down);
        TestApp.Press(_w, Key.Up);
        Assert.Equal(recentBefore, _app.Recent.Files);
        Assert.Single(_w.Tabs);

        TestApp.Press(_w, Key.Enter);
        Assert.False(_w.IsPaletteOpen);
        Assert.Equal(Path.Combine(root, "src", "Mvc", "Controllers", "HomeController.cs"), _w.Active!.Path);
        Assert.Equal(Path.Combine(root, "src", "Mvc", "Controllers", "HomeController.cs"), _app.Recent.Files[0]);
        Assert.Equal(2, _app.Recent.Files.Count);
        _ = other;
    }

    [AvaloniaFact]
    public void Go_to_anything_lists_open_documents_and_recent_files_first_and_marks_a_path_that_is_gone()
    {
        string gone = _temp.File("gone.txt", "x");
        string kept = _temp.File("kept.txt", "y");
        _w.OpenFile(gone, null);
        _w.OpenFile(kept, null);
        foreach (var t in _w.Tabs.ToList()) _w.CloseTab(t);
        File.Delete(gone);
        var open = _w.OpenFile(_temp.File("open.txt", "z"), null);

        _w.ShowQuickOpen();
        var rows = _w.QuickResults;
        Assert.Equal("open.txt", rows[0].Title);
        Assert.Equal("Otevřený", rows[0].Category);
        var missing = rows.Single(r => r.Title == "gone.txt");
        Assert.False(missing.Enabled);
        Assert.Equal("chybí", missing.Reason);
        Assert.Contains("chybí", missing.AutomationName);
        Assert.True(rows.Single(r => r.Title == "kept.txt").Enabled);
        Assert.Equal("Nedávný", rows.Single(r => r.Title == "kept.txt").Category);

        _w.SetPaletteQuery("gone");
        TestApp.Press(_w, Key.Enter);
        Assert.True(_w.IsPaletteOpen); // nothing opens
        _w.ClosePalette();
        Assert.Same(open, _w.Active);
    }

    [AvaloniaFact]
    public void Go_to_anything_goes_to_a_line_in_the_current_file_or_in_the_file_it_names()
    {
        string path = _temp.File("lines.txt", string.Join("\n", Enumerable.Range(1, 200).Select(i => "line " + i)));
        string other = _temp.File("other.txt", string.Join("\n", Enumerable.Range(1, 50).Select(i => "other " + i)));
        _w.OpenFile(other, null);
        var tab = _w.OpenFile(path, null);

        _w.ShowQuickOpen(":120");
        Assert.Equal("Přejít na řádek 120", _w.QuickResults[0].Title);
        TestApp.Press(_w, Key.Enter);
        Assert.Equal(120, _w.Active!.CaretPosition().Line);

        _w.ShowQuickOpen("lines:12");
        Assert.Equal("lines.txt:12", _w.QuickResults[0].Title);
        TestApp.Press(_w, Key.Enter);
        Assert.Same(tab, _w.Active);
        Assert.Equal(12, _w.Active!.CaretPosition().Line);
    }

    // ---- P2.1.3 documents panel ----

    [AvaloniaFact]
    public void Documents_panel_opens_with_a_key_filters_while_typing_and_enter_switches_the_tab()
    {
        var a = _w.OpenFile(_temp.File("alpha.txt", "a"), null);
        var b = _w.OpenFile(_temp.File("beta.txt", "b"), null);
        var c = _w.OpenFile(_temp.File("gamma.md", "c"), null);
        Assert.False(_w.DocumentsPanelOpen);
        Assert.False(_w.DocumentFilter.IsEffectivelyVisible); // a closed panel draws nothing over the editor
        TestApp.Press(_w, Key.B, TestApp.Mod);
        Assert.True(_w.DocumentsPanelOpen);
        Assert.True(_w.DocumentFilter.IsEffectivelyVisible);
        Assert.False(_w.FilePanelOpen);
        Assert.Equal(3, _w.DocumentListTabs.Count);

        _w.DocumentFilter.Focus();
        void Filter(string text)
        {
            _w.DocumentFilter.Text = text;
            Dispatcher.UIThread.RunJobs();
        }
        Filter("a");
        Assert.Equal([a, b, c], _w.DocumentListTabs); // every title has an "a"
        Filter("beta");
        Assert.Equal([b], _w.DocumentListTabs);
        Filter(".md");
        Assert.Equal([c], _w.DocumentListTabs);
        Filter("zzz");
        Assert.Empty(_w.DocumentListTabs);

        Filter("alp");
        TestApp.Press(_w, Key.Enter);
        Assert.Same(a, _w.Active);
        Assert.Equal("", _w.DocumentFilter.Text);
        TestApp.Press(_w, Key.B, TestApp.Mod);
        Assert.False(_w.DocumentsPanelOpen);
        Assert.False(_w.DocumentFilter.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Documents_panel_and_file_panel_share_the_column_and_every_new_control_has_an_accessible_name_in_the_ui_language()
    {
        _w.NewDocument();
        _w.ToggleDocuments(true);
        Assert.Equal("Filtr otevřených dokumentů", AutomationProperties.GetName(_w.DocumentFilter));
        _w.ToggleFiles(true);
        Assert.True(_w.FilePanelOpen);
        Assert.False(_w.DocumentsPanelOpen);
        Assert.Equal("Strom souborů", AutomationProperties.GetName(_w.FileList));
        _w.ShowQuickOpen();
        Assert.Equal("Přejít na cokoli", AutomationProperties.GetName(_w.PaletteInput));
        _w.ClosePalette();

        Loc.SetLanguage("de");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Dateibaum", AutomationProperties.GetName(_w.FileList));
        Assert.Equal("Offene Dokumente filtern", AutomationProperties.GetName(_w.DocumentFilter));
        _w.ShowEditLocations();
        Assert.Equal("Letzte Bearbeitungsorte", AutomationProperties.GetName(_w.PaletteInput));
        _w.ClosePalette();
        Loc.SetLanguage("cs");
    }

    // ---- P2.1.4 recent edit places, jump history ----

    [AvaloniaFact]
    public void Edits_in_three_tabs_give_three_places_with_snippets_and_a_jump_lands_on_the_same_text_after_more_edits()
    {
        var a = _w.OpenFile(_temp.File("a.txt", "a1\na2\na3\n"), null);
        var b = _w.OpenFile(_temp.File("b.txt", "b1\nb2\nb3\n"), null);
        var c = _w.OpenFile(_temp.File("c.txt", "c1\nc2\nc3\n"), null);
        a.Editor!.Document.Insert(a.Editor.Document.GetLineByNumber(2).Offset, "ALPHA ");
        b.Editor!.Document.Insert(b.Editor.Document.GetLineByNumber(3).Offset, "BETA ");
        c.Editor!.Document.Insert(c.Editor.Document.GetLineByNumber(1).Offset, "GAMMA ");
        _w.ShowEditLocations();
        Assert.Equal(["GAMMA c1", "BETA b3", "ALPHA a2"], _w.QuickResults.Select(r => r.Title));
        Assert.Equal(["c.txt:1", "b.txt:3", "a.txt:2"], _w.QuickResults.Select(r => r.Category));
        _w.ClosePalette();

        // text pushed in above the place: the jump still lands on the same line of text
        a.Editor.Document.Insert(0, "new\nlines\nabove\n");
        _w.SelectTab(c);
        _w.ShowEditLocations();
        var row = _w.QuickResults.Single(r => r.Title == "ALPHA a2");
        Assert.Equal("a.txt:5", row.Category);
        _w.SetPaletteQuery("alpha");
        TestApp.Press(_w, Key.Enter);
        Assert.Same(a, _w.Active);
        Assert.Equal(5, a.CaretPosition().Line);
        Assert.Equal("ALPHA a2", a.Editor.Document.GetText(a.Editor.Document.GetLineByNumber(5).Offset, 8));
    }

    [AvaloniaFact]
    public void A_place_survives_closing_and_reopening_the_tab_and_the_modified_filter_narrows_the_list()
    {
        string pa = _temp.File("a.txt", "a1\na2\na3\n");
        string pb = _temp.File("b.txt", "b1\nb2\n");
        var a = _w.OpenFile(pa, null);
        var b = _w.OpenFile(pb, null);
        a.Editor!.Document.Insert(a.Editor.Document.GetLineByNumber(3).Offset, "KEEP ");
        b.Editor!.Document.Insert(b.Editor.Document.GetLineByNumber(2).Offset, "OTHER ");
        _w.SaveAsync(a).GetAwaiter().GetResult();
        Assert.True(a.Doc.Doc.IsModified == false);

        _w.ShowEditLocations();
        Assert.Equal(2, _w.QuickResults.Count);
        _w.PaletteOnlyModified.IsChecked = true;
        Assert.Equal(["OTHER b2"], _w.QuickResults.Select(r => r.Title));
        _w.PaletteOnlyModified.IsChecked = false;
        _w.ClosePalette();

        _w.CloseTab(a);
        Assert.DoesNotContain(_w.Tabs, t => t.Path == pa);
        _w.ShowEditLocations();
        var row = _w.QuickResults.Single(r => r.Title == "KEEP a3");
        Assert.Equal("a.txt:3", row.Category);
        _w.SetPaletteQuery("keep");
        TestApp.Press(_w, Key.Enter);
        Assert.Equal(pa, _w.Active!.Path);
        Assert.Equal(3, _w.Active.CaretPosition().Line);
    }

    [AvaloniaFact]
    public void Alt_left_and_alt_right_walk_back_and_forward_through_the_jumps()
    {
        string path = _temp.File("j.txt", string.Join("\n", Enumerable.Range(1, 100).Select(i => "l" + i)));
        var tab = _w.OpenFile(path, null);
        _dialogs.Prompts.Enqueue("40");
        _ = _w.GoToLineAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, tab.CaretPosition().Line);
        _dialogs.Prompts.Enqueue("80");
        _ = _w.GoToLineAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(80, tab.CaretPosition().Line);

        TestApp.Press(_w, Key.Left, RawInputModifiers.Alt);
        Assert.Equal(40, tab.CaretPosition().Line);
        TestApp.Press(_w, Key.Left, RawInputModifiers.Alt);
        Assert.Equal(1, tab.CaretPosition().Line);
        TestApp.Press(_w, Key.Left, RawInputModifiers.Alt);
        Assert.Equal("Není kam skočit zpět", ReadStatus());
        TestApp.Press(_w, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(40, tab.CaretPosition().Line);
        TestApp.Press(_w, Key.Right, RawInputModifiers.Alt);
        Assert.Equal(80, tab.CaretPosition().Line);
    }

    private string ReadStatus() => _w.StatusMessageText;

    // ---- P2.1.5 related file ----

    [AvaloniaFact]
    public void Related_file_switches_header_and_source_for_the_builtin_rules_and_a_custom_one()
    {
        string root = Project();
        _w.OpenFile(Path.Combine(root, "native", "bar.c"), null);
        _w.GoToRelatedFile();
        Assert.Equal(Path.Combine(root, "native", "bar.h"), _w.Active!.Path);
        _w.GoToRelatedFile();
        Assert.Equal(Path.Combine(root, "native", "bar.c"), _w.Active!.Path);

        _w.OpenFile(Path.Combine(root, "shaders", "a.vert"), null);
        _w.GoToRelatedFile();
        Assert.Equal(Path.Combine(root, "shaders", "a.vert"), _w.Active!.Path); // no rule yet
        Assert.Contains("a.vert", _w.StatusMessageText);
        Assert.Null(_app.Workbench.Set("files.relatedFileRules", ".vert|.frag"));
        _w.GoToRelatedFile();
        Assert.Equal(Path.Combine(root, "shaders", "a.frag"), _w.Active!.Path);
    }

    [AvaloniaFact]
    public void Two_related_candidates_open_a_choice_that_the_keyboard_can_finish()
    {
        string root = Project();
        _w.OpenFile(Path.Combine(root, "native", "foo.h"), null);
        _w.GoToRelatedFile();
        Assert.True(_w.IsPaletteOpen);
        Assert.Equal(PaletteMode.Choice, _w.PaletteModeNow);
        Assert.Equal(["foo.c", "foo.cpp"], _w.QuickResults.Select(r => r.Title).Order());
        Assert.Equal("Související soubory", AutomationProperties.GetName(_w.PaletteInput));
        _w.SetPaletteQuery("cpp");
        TestApp.Press(_w, Key.Enter);
        Assert.Equal(Path.Combine(root, "native", "foo.cpp"), _w.Active!.Path);
    }

    [AvaloniaFact]
    public void Related_file_of_an_unsaved_document_says_why_nothing_happens()
    {
        _w.NewDocument();
        _w.GoToRelatedFile();
        Assert.Equal("Dokument nejdřív uložte, pak jde najít související soubor", _w.StatusMessageText);
        Assert.False(_w.IsPaletteOpen);
    }

    // ---- P2.3.1 file panel over the disk ----

    [AvaloniaFact]
    public async Task File_panel_opens_with_ctrl_shift_e_reads_one_level_and_shows_a_file_another_program_made()
    {
        string root = Project();
        _dialogs.Folders.Enqueue(root);
        await _w.OpenFolderWithDialogAsync();
        Assert.True(_w.FilePanelOpen);
        Assert.Equal(["docs", "native", "shaders", "src"], _w.Files!.Rows.Select(r => r.Entry.Name));
        Assert.False(_w.Files.IsLoaded(Path.Combine(root, "src")));
        Assert.Equal(_app.Recent.Folder, root);

        _w.SelectFilePath(Path.Combine(root, "src"));
        await _w.ActivateFileRowAsync(); // what Enter and a double click do
        Assert.True(_w.Files.IsExpanded(Path.Combine(root, "src")));
        Assert.Contains(_w.Files.Rows, r => r.Entry.Name == "Mvc" && r.Depth == 1);

        File.WriteAllText(Path.Combine(root, "from-outside.txt"), "x");
        await WaitFor(() => _w.Files.Rows.Any(r => r.Entry.Name == "from-outside.txt"));

        TestApp.Press(_w, Key.E, TestApp.Mod | RawInputModifiers.Shift);
        Assert.False(_w.FilePanelOpen);
        TestApp.Press(_w, Key.E, TestApp.Mod | RawInputModifiers.Shift);
        Assert.True(_w.FilePanelOpen);
    }

    [AvaloniaFact]
    public async Task The_keyboard_goes_to_the_file_tree_when_the_panel_opens_even_while_the_last_folder_is_still_being_read()
    {
        string root = Project();
        _app.Recent.SetFolder(root);
        _w.NewDocument();
        _w.Show();
        TestApp.Press(_w, Key.E, TestApp.Mod | RawInputModifiers.Shift);
        Assert.True(_w.FilePanelOpen);
        await WaitFor(() => _w.Files is not null);
        await WaitFor(() => _w.FileList.IsKeyboardFocusWithin);
        TestApp.Press(_w, Key.Down);
        Assert.Equal(0, _w.FileList.SelectedIndex);
        TestApp.Press(_w, Key.Right);
        await WaitFor(() => _w.Files!.Rows.Count > 4);
        Assert.True(_w.Files!.IsExpanded(_w.SelectedFileRow!.Value.Entry.Path));
        // the rows were replaced by the expansion; the keyboard must still be in the tree and the window keys must still work
        await WaitFor(() => _w.FileList.IsKeyboardFocusWithin);
        TestApp.Press(_w, Key.Down);
        Assert.Equal(1, _w.FileList.SelectedIndex);
        TestApp.Press(_w, Key.B, TestApp.Mod);
        Assert.True(_w.DocumentsPanelOpen);
    }

    [AvaloniaFact]
    public async Task File_panel_rows_have_accessible_names_and_the_context_menu_comes_from_the_registry()
    {
        string root = Project();
        await _w.ShowFolderAsync(root);
        Assert.Equal("Panel souborů", AutomationProperties.GetName(_w.FilePanelControl));
        var menu = _w.FileList.ContextMenu!;
        var titles = menu.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToList();
        Assert.Equal(["Nový soubor ve složce...", "Nová složka...", "Duplikovat", "Přejmenovat...", "Přesunout...", "Smazat do koše", "Obnovit soubory", "Přidat k oblíbeným", "Oblíbené...", "Zvláštní otevření"], titles);
    }

    [AvaloniaFact]
    public async Task A_path_in_the_text_opens_the_file_at_the_line_a_folder_in_the_panel_and_a_missing_one_says_so()
    {
        string root = Project();
        var tab = _w.NewDocument();
        string target = Path.Combine(root, "docs", "readme.md");
        tab.Editor!.Document.Text = $"see {target}:2 here\nand {Path.Combine(root, "src")} and nothing.zzz\n";
        tab.Editor.TextArea.Caret.Offset = 8;
        _w.OpenPathAtCaret();
        Assert.Equal(target, _w.Active!.Path);
        Assert.Equal(2, _w.Active.CaretPosition().Line);

        _w.SelectTab(tab);
        tab.Editor.TextArea.Caret.Offset = tab.Editor.Document.GetLineByNumber(2).Offset + 6;
        _w.OpenPathAtCaret();
        await WaitFor(() => _w.Files is not null && _w.FilePanelOpen);
        Assert.Equal(Path.Combine(root, "src"), _w.Files!.Root);

        tab.Editor.TextArea.Caret.Offset = tab.Editor.Document.GetLineByNumber(2).Offset + tab.Editor.Document.GetLineByNumber(2).Length - 3;
        _w.OpenPathAtCaret();
        Assert.Contains("nothing.zzz", _w.StatusMessageText);
    }

    // ---- P9.5.3 special open ----

    [AvaloniaFact]
    public async Task Special_open_from_the_panel_menu_lists_the_variants()
    {
        await PanelWithFolderAsync(("a.txt", "x"));
        var special = _w.FileList.ContextMenu!.Items.OfType<MenuItem>().Last();
        Assert.Equal(SpecialOpen.Variants.Length, special.Items.Count);
    }

    [AvaloniaFact]
    public async Task Special_open_as_hex_opens_the_file_in_the_hex_view()
    {
        string root = await PanelWithFolderAsync(("a.bin", "abc"));
        _w.OpenFile(Path.Combine(root, "a.bin"), null);
        await _w.OpenTargetAsHexAsync();
        Assert.True(_w.Active!.Doc.IsHex);
    }

    [AvaloniaFact]
    public async Task Special_open_read_only_refuses_a_save()
    {
        string root = await PanelWithFolderAsync(("a.txt", "abc"));
        _w.OpenFile(Path.Combine(root, "a.txt"), null);
        await _w.OpenTargetReadOnlyAsync();
        Assert.True(_w.Active!.Doc.ReadOnly);
        Assert.False(await _w.SaveAsync(_w.Active));
    }

    [AvaloniaFact]
    public async Task Special_open_as_new_copy_gives_an_unsaved_document_with_the_text()
    {
        string root = await PanelWithFolderAsync(("a.txt", "abc"));
        _w.OpenFile(Path.Combine(root, "a.txt"), null);
        await _w.OpenTargetAsNewCopyAsync();
        Assert.Null(_w.Active!.Path);
        Assert.Equal("abc", _w.Active.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task Special_open_with_encoding_opens_the_file()
    {
        string root = await PanelWithFolderAsync(("a.txt", "abc"));
        _dialogs.Picked.Add(Path.Combine(root, "a.txt"));
        await _w.OpenTargetWithEncodingAsync();
        Assert.Equal(Path.Combine(root, "a.txt"), _w.Active!.Path);
    }

    [AvaloniaFact]
    public async Task Special_open_reopens_the_last_closed_tab()
    {
        string root = await PanelWithFolderAsync(("a.txt", "abc"));
        string path = Path.Combine(root, "a.txt");
        await _w.CloseTabAsync(_w.OpenFile(path, null));
        _w.ReopenLastClosed();
        Assert.Equal(path, _w.Active!.Path);
    }

    // ---- P2.3.1 - P2.3.3 operations ----

    private async Task<string> PanelWithFolderAsync(params (string Name, string Text)[] files)
    {
        string root = Path.Combine(_temp.Path, "work");
        Directory.CreateDirectory(root);
        foreach (var (name, text) in files)
        {
            string p = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }
        Assert.True(await _w.ShowFolderAsync(root));
        return root;
    }

    [AvaloniaFact]
    public async Task Create_and_duplicate_in_the_panel_make_real_files_and_select_them()
    {
        string root = await PanelWithFolderAsync(("a.txt", "hello"));
        _dialogs.Prompts.Enqueue("new.txt");
        await _w.NewFileAsync();
        Assert.True(File.Exists(Path.Combine(root, "new.txt")));
        Assert.Equal(Path.Combine(root, "new.txt"), _w.SelectedFileRow!.Value.Entry.Path);
        Assert.Equal(Path.Combine(root, "new.txt"), _w.Active!.Path);

        _dialogs.Prompts.Enqueue("sub");
        await _w.NewFolderAsync();
        Assert.True(Directory.Exists(Path.Combine(root, "sub")));
        Assert.Equal(Path.Combine(root, "sub"), _w.SelectedFileRow!.Value.Entry.Path);

        _w.SelectFilePath(Path.Combine(root, "a.txt"));
        await _w.DuplicateAsync();
        Assert.Equal("hello", File.ReadAllText(Path.Combine(root, "a copy.txt")));
        Assert.Equal(Path.Combine(root, "a copy.txt"), _w.SelectedFileRow!.Value.Entry.Path);

        _dialogs.Prompts.Enqueue("a.txt"); // exists: a reason, nothing created, nothing overwritten
        _w.SelectFilePath(Path.Combine(root, "a.txt"));
        await _w.NewFileAsync();
        Assert.Contains(_w.Notices, n => n.Message.Contains("už existuje"));
    }

    [AvaloniaFact]
    public async Task Renaming_an_open_file_renames_the_tab_and_keeps_unsaved_changes_undo_and_the_journal()
    {
        string root = await PanelWithFolderAsync(("old.txt", "one\ntwo\n"));
        string oldPath = Path.Combine(root, "old.txt");
        var tab = _w.OpenFile(oldPath, null);
        tab.Editor!.Document.Insert(0, "EDIT ");
        Assert.True(tab.Doc.Doc.IsModified);
        int undo = tab.Doc.Doc.UndoDepth;
        var doc = tab.Doc.Doc;

        _w.SelectFilePath(oldPath);
        _dialogs.Prompts.Enqueue("renamed.md");
        await _w.RenameAsync();

        string newPath = Path.Combine(root, "renamed.md");
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(newPath));
        Assert.Equal(newPath, tab.Path);
        Assert.Equal("renamed.md", tab.Title);
        Assert.Same(doc, tab.Doc.Doc);
        Assert.True(doc.IsModified);
        Assert.Equal(undo, doc.UndoDepth);
        Assert.Equal("EDIT one\ntwo\n", tab.Editor.Document.Text);
        Assert.Equal("one\ntwo\n", File.ReadAllText(newPath)); // the unsaved text did not touch the file
        Assert.Empty(_w.Notices);
        Assert.Contains("renamed.md", _app.Recent.Files.Select(Path.GetFileName));
        Assert.DoesNotContain(_app.Recent.Files, p => p == oldPath);

        _w.Undo();
        Assert.Equal("one\ntwo\n", tab.Editor.Document.Text);
        _w.Redo();
        tab.Editor.Document.Insert(tab.Editor.Document.TextLength, "tail");

        // a crash after the rename: the work comes back under the new path
        _app.SaveSession();
        _app.Journal.Flush();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        _w = _app.CreateWindow(null);
        _w.RestoreFrom(_app.Journal);
        var restored = _w.Tabs.Single();
        Assert.Equal(newPath, restored.Path);
        Assert.Equal("EDIT one\ntwo\ntail", restored.Editor!.Document.Text);
        Assert.True(restored.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public async Task Moving_a_file_and_a_whole_folder_moves_the_open_documents_with_them()
    {
        string root = await PanelWithFolderAsync(("a/one.txt", "1"), ("a/two.txt", "2"), ("dest/keep.txt", "k"));
        var one = _w.OpenFile(Path.Combine(root, "a", "one.txt"), null);
        var two = _w.OpenFile(Path.Combine(root, "a", "two.txt"), null);
        two.Editor!.Document.Insert(1, "!");

        await _w.SelectFileAsync(Path.Combine(root, "a", "one.txt"));
        _dialogs.Prompts.Enqueue(Path.Combine(root, "dest"));
        await _w.MoveAsync();
        Assert.Equal(Path.Combine(root, "dest", "one.txt"), one.Path);
        Assert.True(File.Exists(Path.Combine(root, "dest", "one.txt")));

        _w.SelectFilePath(Path.Combine(root, "a"));
        _dialogs.Prompts.Enqueue(Path.Combine(root, "dest"));
        await _w.MoveAsync();
        Assert.Equal(Path.Combine(root, "dest", "a", "two.txt"), two.Path);
        Assert.Equal("2!", two.Editor!.Document.Text);
        Assert.True(two.Doc.Doc.IsModified);
        Assert.Null(two.Doc.Doc.CheckExternalChange());
        Assert.DoesNotContain(_w.Notices, n => n.Message.Contains("zmizel") || n.Message.Contains("chybí"));
    }

    [AvaloniaFact]
    public async Task A_locked_file_cannot_be_moved_and_the_failure_says_why_and_changes_nothing()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = await PanelWithFolderAsync(("locked.txt", "precious"), ("dest/x.txt", "x"));
        string path = Path.Combine(root, "locked.txt");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _w.SelectFilePath(path);
            _dialogs.Prompts.Enqueue(Path.Combine(root, "dest"));
            await _w.MoveAsync();
            Assert.Contains(_w.Notices, n => n.Message.Contains("používá jiný program") && n.Message.Contains("Nic se nezměnilo"));
        }
        Assert.Equal("precious", File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(root, "dest", "locked.txt")));
        Assert.Contains(_w.Files!.Rows, r => r.Entry.Name == "locked.txt");
    }

    [AvaloniaFact]
    public async Task Delete_asks_first_and_cancel_keeps_the_file()
    {
        string root = await PanelWithFolderAsync(("keep.txt", "k"));
        _w.SelectFilePath(Path.Combine(root, "keep.txt"));
        _dialogs.Answers.Enqueue(1);
        await _w.DeleteAsync();
        Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
        Assert.Contains("do koše", _dialogs.Asked[0].Message);
    }

    [AvaloniaFact]
    public async Task Delete_moves_the_file_to_the_real_recycle_bin_where_it_can_be_restored()
    {
        if (!OperatingSystem.IsWindows()) return;
        string name = "textida-bin-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        string root = await PanelWithFolderAsync((name, "content to restore"));
        string path = Path.Combine(root, name);
        var tab = _w.OpenFile(path, null);
        _w.SelectFilePath(path);
        _dialogs.Answers.Enqueue(0);
        await _w.DeleteAsync();
        Assert.False(File.Exists(path));
        Assert.DoesNotContain(_w.Files!.Rows, r => r.Entry.Name == name);
        Assert.DoesNotContain(path, _app.Recent.Files);

        string? meta = FindInRecycleBin(path);
        Assert.True(meta is not null, "the recycle bin of the drive has no record of " + path);
        try
        {
            // the shell's own restore needs the Explorer shell objects; a stripped-down Windows has none, then the record in the bin is the proof
            string answer = RestoreFromRecycleBin(name);
            if (answer == "1")
            {
                Assert.True(File.Exists(path), "the shell restored the file but it is not back");
                Assert.Equal("content to restore", File.ReadAllText(path));
            }
        }
        finally
        {
            if (FindInRecycleBin(path) is { } left) DiscardFromRecycleBin(left);
        }
        _ = tab;
    }

    [AvaloniaFact]
    public void Recycle_bin_refuses_a_missing_path_and_never_deletes_what_it_cannot_recycle()
    {
        if (!RecycleBin.IsSupported) return;
        string missing = Path.Combine(_temp.Path, "nope.txt");
        Assert.Equal(FileFailure.NotFound, Assert.Throws<FileOperationException>(() => RecycleBin.Move(missing)).Kind);
    }

    // ---- recycle bin helpers (Windows) ----

    private static string RecycleFolder(string path) =>
        Path.Combine(Path.GetPathRoot(path)!, "$Recycle.Bin", System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);

    private static string? FindInRecycleBin(string original)
    {
        string folder = RecycleFolder(original);
        if (!Directory.Exists(folder)) return null;
        foreach (var meta in Directory.EnumerateFiles(folder, "$I*"))
        {
            try
            {
                var bytes = File.ReadAllBytes(meta);
                if (bytes.Length < 28 || BitConverter.ToInt64(bytes, 0) != 2) continue;
                int chars = BitConverter.ToInt32(bytes, 24);
                if (Encoding.Unicode.GetString(bytes, 28, Math.Max(0, chars - 1) * 2).Equals(original, StringComparison.OrdinalIgnoreCase)) return meta;
            }
            catch (IOException) { }
        }
        return null;
    }

    private static void DiscardFromRecycleBin(string meta)
    {
        string data = Path.Combine(Path.GetDirectoryName(meta)!, "$R" + Path.GetFileName(meta)[2..]);
        try
        {
            File.Delete(meta);
            if (File.Exists(data)) File.Delete(data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Puts a file back the way the user does in the recycle bin window: the shell's own "undelete" verb.</summary>
    private static string RestoreFromRecycleBin(string name)
    {
        string script = "try{$n=0;$s=New-Object -ComObject Shell.Application;$b=$s.Namespace(10);foreach($i in $b.Items()){if($i.Name -eq '" + name + "'){$i.InvokeVerb('undelete');$n++}};$n}catch{'0'}";
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script]) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        string output = p.StandardOutput.ReadToEnd().Trim();
        return p.WaitForExit(30_000) ? output : "0";
    }
}

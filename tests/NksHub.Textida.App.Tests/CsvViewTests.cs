using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>The table view (P5.1.4 to P5.1.6) through the window: switching, moving, what a reader hears, editing, adding and deleting, and the filter.</summary>
[Collection("LocState")]
public class CsvViewTests : IDisposable
{
    private const string Sample = "id,name,note\r\n1,Ann,\"first\r\nsecond\"\r\n2,Bob,plain\r\n3,Cyd,\"a,b\"\r\n";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public CsvViewTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        Loc.SetLanguage("en");
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    private DocTab OpenCsv(string text, string name = "data.csv")
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    private CsvView ShowTable(DocTab tab)
    {
        Assert.Equal(CommandOutcome.Handled, Run("nks.view.table"));
        var view = Assert.IsType<CsvView>(_w.ActiveTable);
        _w.UpdateLayout();
        view.Focus();
        while (!view.RowsComplete) view.Tick();
        return view;
    }

    private static byte[] Bytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));
    private static string Text(DocTab tab) => Encoding.UTF8.GetString(Bytes(tab));
    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    [AvaloniaFact]
    public void The_chord_switches_to_the_table_and_back_without_touching_the_bytes()
    {
        var tab = OpenCsv(Sample);
        byte[] before = Bytes(tab);
        Press(Key.K, TestApp.Mod);
        Press(Key.T, TestApp.Mod);
        Assert.True(tab.TableShown);
        var view = _w.ActiveTable!;
        Assert.False(tab.Surface!.IsVisible);
        Assert.Equal(["id", "name", "note"], view.Table.Header());
        Assert.Equal("first\r\nsecond", view.CellText(0, 2));
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(3, view.DataRowCount);

        Press(Key.K, TestApp.Mod);
        Press(Key.T, TestApp.Mod);
        Assert.False(tab.TableShown);
        Assert.True(tab.Surface.IsVisible);
        Assert.Equal(before, Bytes(tab));
        Assert.Equal(0, tab.Doc.Doc.Revision);
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void A_tsv_file_is_split_on_tabs_and_the_dialect_menu_changes_it_for_the_document()
    {
        var tab = OpenCsv("a\tb;c\n1\t2;3\n", "data.tsv");
        var view = ShowTable(tab);
        Assert.Equal(["a", "b;c"], view.Table.Header());
        _w.SetTableDialect(tab.Doc, tab.Doc.TableDialect with { Delimiter = ';' });
        Assert.Equal(["a\tb", "c"], view.Table.Header());
        _w.SetTableDialect(tab.Doc, tab.Doc.TableDialect with { HasHeader = false });
        Assert.Null(view.Table.Header());
        Assert.Equal(2, view.DataRowCount);
        Assert.NotNull(_w.ShowTableDialectMenu());
    }

    [AvaloniaFact]
    public void Keys_move_the_current_cell_and_the_status_line_says_where_it_is()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        Assert.Equal((0, 0), (view.Row, view.Column));
        Assert.Equal("Row 1 of 3, column 1 of 3 (id)", _w.StatusLeftText.Split(" · ")[0]);

        Press(Key.Down);
        Press(Key.Right);
        Press(Key.Tab);
        Assert.Equal((1, 2), (view.Row, view.Column));
        Assert.Equal("Row 2 of 3, column 3 of 3 (note)", _w.StatusLeftText.Split(" · ")[0]);
        Press(Key.Right);
        Assert.Equal(2, view.Column);
        Press(Key.Home);
        Assert.Equal(0, view.Column);
        Press(Key.End, TestApp.Mod);
        Assert.Equal(2, view.Row);
        Press(Key.Up);
        Press(Key.Up);
        Press(Key.Up);
        Assert.Equal(-1, view.Row);
        Assert.StartsWith("Header, column 1 of 3 (id)", _w.StatusLeftText);
        Press(Key.Down, RawInputModifiers.Shift);
        Press(Key.Right, RawInputModifiers.Shift);
        Assert.Equal(new CsvCellRange(-1, 0, 0, 1), view.Selection);
    }

    [AvaloniaFact]
    public void A_reader_gets_the_cell_the_row_and_the_column()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        Press(Key.Down);
        Press(Key.Right);
        Press(Key.Right);
        var peer = Assert.IsType<CsvAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(view));
        Assert.Equal("Row 2, column 3 (note): plain", peer.Value);
        Assert.Equal("Row 2 of 3: 2, Bob, plain. Column 3 of 3: note", peer.GetHelpText());
        Assert.Equal("Row 2 of 3: 2, Bob, plain", view.DescribeRow());
        Assert.Equal("Column 3 of 3: note", view.DescribeColumn());
        Press(Key.Up);
        Press(Key.Up);
        Assert.Equal("Header, column 3: note", peer.Value);
        Press(Key.Left);
        Press(Key.Left);
        Press(Key.Down);
        Press(Key.Down);
        Assert.Equal("Row 2, column 1 (id): 2", peer.Value);
    }

    [AvaloniaFact]
    public void A_header_less_column_is_named_by_its_letter()
    {
        var tab = OpenCsv("1,2\n3,4\n");
        _w.SetTableDialect(tab.Doc, CsvDialect.Csv with { HasHeader = false });
        var view = ShowTable(tab);
        Assert.Equal("A", view.ColumnLabel(0));
        Assert.Equal("B", view.ColumnLabel(1));
        Press(Key.Right);
        Assert.Equal("Row 1 of 2, column 2 of 2 (B)", _w.StatusLeftText.Split(" · ")[0]);
    }

    [AvaloniaFact]
    public void Editing_a_cell_changes_only_its_bytes_and_is_one_undo_step()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        byte[] before = Bytes(tab);
        var cell = view.Table.CellRange(1, 2);
        Press(Key.Down);
        Press(Key.Right);
        Press(Key.Right);
        Assert.Equal((1, 2), (view.Row, view.Column));
        Press(Key.Enter);
        Assert.True(view.IsEditing);
        Assert.Equal("plain", view.EditText);
        view.EditText = "two, \"words\"";
        Assert.True(view.CommitEdit());

        string expected = Sample.Replace("plain", "\"two, \"\"words\"\"\"");
        Assert.Equal(expected, Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(before, Bytes(tab));
        Assert.Equal("plain", view.CellText(1, 2));
        Press(Key.Y, TestApp.Mod);
        Assert.Equal(expected, Text(tab));
    }

    [AvaloniaFact]
    public void Typing_starts_an_edit_and_a_multiline_value_in_a_quoted_cell_keeps_the_quotes()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        Press(Key.Right);
        Press(Key.Right);
        Press(Key.Down, RawInputModifiers.None);
        Press(Key.Up);
        Assert.Equal((0, 2), (view.Row, view.Column));
        TestApp.Type(_w, "x");
        Assert.True(view.IsEditing);
        Assert.Equal("x", view.EditText);
        view.EditText = "one\ntwo";
        view.CommitEdit();
        Assert.Equal("id,name,note\r\n1,Ann,\"one\ntwo\"\r\n2,Bob,plain\r\n3,Cyd,\"a,b\"\r\n", Text(tab));
        Press(Key.Escape);
        Assert.False(view.IsEditing);
    }

    [AvaloniaFact]
    public void A_refused_value_leaves_the_cell_and_says_why()
    {
        var tab = OpenCsv("a;b\n1;2\n");
        _w.SetTableDialect(tab.Doc, new CsvDialect(';', '\0', true));
        var view = ShowTable(tab);
        string? said = null;
        view.Refused += m => said = m;
        Assert.False(view.SetCell(0, 0, "x;y"));
        Assert.Contains("no quote character", said);
        Assert.Equal("a;b\n1;2\n", Text(tab));
    }

    [AvaloniaFact]
    public void Rows_and_columns_are_added_and_deleted_with_expected_bytes_and_undo()
    {
        var tab = OpenCsv("id,name\r\n1,Ann\r\n2,Bob\r\n");
        var view = ShowTable(tab);
        byte[] original = Bytes(tab);

        Assert.Equal(CommandOutcome.Handled, Run("nks.table.insertRow"));
        Assert.Equal("id,name\r\n1,Ann\r\n,\r\n2,Bob\r\n", Text(tab));
        Assert.Equal(1, view.Row);
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Run("nks.edit.undo");
        Assert.Equal(original, Bytes(tab));

        Press(Key.Down);
        Assert.Equal(CommandOutcome.Handled, Run("nks.table.deleteRow"));
        Assert.Equal("id,name\r\n1,Ann\r\n", Text(tab));
        Run("nks.edit.undo");
        Assert.Equal(original, Bytes(tab));

        Press(Key.Right);
        _dialogs.Prompts.Enqueue("email");
        Assert.Equal(CommandOutcome.Handled, Run("nks.table.insertColumn"));
        Assert.Equal("id,name,email\r\n1,Ann,\r\n2,Bob,\r\n", Text(tab));
        Assert.Equal(2, view.Column);
        Run("nks.edit.undo");
        Assert.Equal(original, Bytes(tab));

        Assert.Equal(CommandOutcome.Handled, Run("nks.table.deleteColumn"));
        Assert.Equal("id\r\n1\r\n2\r\n", Text(tab));
        Run("nks.edit.undo");
        Assert.Equal(original, Bytes(tab));
        Assert.False(tab.Doc.Doc.CanUndo);
    }

    [AvaloniaFact]
    public void A_row_can_be_appended_after_the_last_one_and_the_missing_final_newline_stays_missing()
    {
        var tab = OpenCsv("a,b\n1,2");
        var view = ShowTable(tab);
        Press(Key.Down);
        Run("nks.table.insertRow");
        Assert.Equal("a,b\n1,2\n,", Text(tab));
        Assert.Equal(1, view.Row);
        view.SelectRow(1);
        Run("nks.table.deleteRow");
        Assert.Equal("a,b\n1,2", Text(tab));
    }

    [AvaloniaFact]
    public void The_filter_shows_matching_rows_and_never_changes_the_file()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        byte[] before = Bytes(tab);
        Press(Key.Right);
        _dialogs.Prompts.Enqueue("b");
        Assert.Equal(CommandOutcome.Handled, Run("nks.table.filter"));
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(1, view.DataRowCount);
        Assert.Equal("Bob", view.CellText(0, 1));
        Assert.Equal(2, view.RowNumber(0));
        Assert.Contains("Filter “b” in column 2: rows 1", _w.StatusLeftText);
        Assert.Equal(before, Bytes(tab));
        Assert.Equal(0, tab.Doc.Doc.Revision);

        Run("nks.table.insertRow");
        Assert.Equal(before, Bytes(tab));
        Assert.Contains("Clear the filter", _w.StatusLeftText);

        Run("nks.table.clearFilter");
        Assert.Equal(3, view.DataRowCount);
        Assert.Null(view.Filter);
    }

    [AvaloniaFact]
    public void The_filter_follows_an_edit_made_in_the_table()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        view.ApplyFilter("o", 1);
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(1, view.DataRowCount);
        view.SetCell(0, 1, "Zed");
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(0, view.DataRowCount);
        Run("nks.edit.undo");
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(1, view.DataRowCount);
    }

    [AvaloniaFact]
    public async Task Copy_gives_tab_separated_text_and_paste_writes_it_back_as_one_step()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        view.Select(0, 1, 2, 2);
        Run("nks.edit.copy");
        string copied = (await _w.Clipboard!.TryGetTextAsync())!;
        Assert.Equal("Ann\t\"first\r\nsecond\"\nBob\tplain\nCyd\ta,b", copied);

        view.MoveTo(2, 0);
        await _w.PasteAsync();
        string expected = "id,name,note\r\n1,Ann,\"first\r\nsecond\"\r\n2,Bob,plain\r\n3,Cyd,\"a,b\"\r\n";
        Assert.NotEqual(expected, Text(tab));
        Assert.Equal("Ann", view.CellText(2, 0));
        Assert.Equal("first\r\nsecond", view.CellText(2, 1));
        Assert.Equal("Bob", view.CellText(3, 0));
        Assert.True(view.RowsComplete);
        Assert.Equal(5, view.DataRowCount);
        Run("nks.edit.undo");
        Assert.Equal(expected, Text(tab));
    }

    [AvaloniaFact]
    public void A_column_is_selected_by_its_header_and_a_row_by_its_number()
    {
        var tab = OpenCsv(Sample);
        var view = ShowTable(tab);
        view.SelectColumn(1);
        Assert.Equal(new CsvCellRange(-1, 2, 1, 1), view.Selection);
        view.SelectRow(1);
        Assert.Equal(new CsvCellRange(1, 1, 0, 2), view.Selection);
        Run("nks.edit.selectAll");
        Assert.Equal(new CsvCellRange(-1, 2, 0, 2), view.Selection);
    }

    [AvaloniaFact]
    public void Delete_clears_the_selection_as_one_undo_step()
    {
        var tab = OpenCsv("a,b\n1,2\n3,4\n");
        var view = ShowTable(tab);
        view.Select(0, 0, 1, 1);
        Press(Key.Delete);
        Assert.Equal("a,b\n,\n,\n", Text(tab));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public void A_file_above_the_paged_threshold_scrolls_without_reading_it_all_and_edits_one_cell()
    {
        var tab = OpenLargeCsv(8 << 20, out int rows);
        Assert.True(tab.Doc.IsLarge);
        Run("nks.view.table");
        var view = Assert.IsType<CsvView>(_w.ActiveTable);
        Assert.False(view.RowsComplete);
        Assert.True(view.DataRowCount < rows / 2);

        view.MoveTo(rows / 2, 1);
        Assert.Equal($"name {rows / 2}", view.CellText(rows / 2, 1));
        long length = tab.Doc.Doc.ByteLength;
        Assert.True(view.SetCell(rows / 2, 1, "renamed"));
        Assert.Equal(length - $"name {rows / 2}".Length + "renamed".Length, tab.Doc.Doc.ByteLength);
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(rows, view.DataRowCount);
        Press(Key.End, TestApp.Mod);
        Assert.Equal(rows - 1, view.Row);
    }

    [AvaloniaFact]
    [Trait("Category", "Large")]
    public void A_file_of_hundreds_of_megabytes_opens_at_once_in_the_table()
    {
        var tab = OpenLargeCsv(300 << 20, out int rows);
        Run("nks.view.table");
        var view = Assert.IsType<CsvView>(_w.ActiveTable);
        Assert.False(view.RowsComplete);
        Assert.True(view.DataRowCount < 10_000);
        Press(Key.End, TestApp.Mod);
        for (int i = 0; i < 100_000 && view.Row != rows - 1; i++) view.Tick();
        Assert.Equal(rows - 1, view.Row);
        Assert.Equal($"name {rows - 1}", view.CellText(rows - 1, 1));
    }

    private DocTab OpenLargeCsv(long bytes, out int rows)
    {
        string path = Path.Combine(_temp.Path, "large.csv");
        int count = 0;
        using (var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16))
        {
            w.Write("id,name,note\r\n");
            while (w.BaseStream.Length + 4096 < bytes)
            {
                for (int i = 0; i < 1000; i++, count++)
                    w.Write(count % 9 == 0 ? $"{count},name {count},\"line one\r\nline two, {count}\"\r\n" : $"{count},name {count},a plain note\r\n");
                w.Flush();
            }
        }
        rows = count;
        var tab = _w.OpenFile(path, null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    // ---- a selected part as a table ----

    private const string Head = "# notes, not a table\nbefore\n";
    private const string Body = "id,name\n1,\"A\nB\"\n2,Bob\n";
    private const string Tail = "after, the table; end\n";

    private DocTab OpenWithSelection(string text, int start, int length)
    {
        var tab = OpenCsv(text, "notes.txt");
        tab.Surface!.SetSelections([new NksHub.Textida.AvaloniaEditAdapter.Sel(start, start + length)]);
        return tab;
    }

    [AvaloniaFact]
    public async Task A_selection_becomes_a_table_and_a_cell_edit_changes_only_that_cell()
    {
        var tab = OpenWithSelection(Head + Body + Tail, Head.Length, Body.Length);
        byte[] before = Bytes(tab);
        _dialogs.TableRegions.Enqueue(CsvDialect.Csv);
        await _w.EditSelectionAsTableAsync();

        var view = Assert.IsType<CsvView>(_w.ActiveTable);
        Assert.True(view.IsRegion);
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(["id", "name"], view.Table.Header());
        Assert.Equal(2, view.DataRowCount);
        Assert.Equal("A\nB", view.CellText(0, 1));
        Assert.StartsWith("id | name\n1 | A B\n2 | Bob", _dialogs.TableRegionPreviews[0]);
        Assert.Contains("only the selected text", _w.StatusLeftText);

        Assert.True(view.SetCell(1, 1, "Robert"));
        Assert.Equal(Head + "id,name\n1,\"A\nB\"\n2,Robert\n" + Tail, Text(tab));
        view.MoveTo(1, 0);
        view.InsertRow();
        Assert.Equal(Head + "id,name\n1,\"A\nB\"\n2,Robert\n,\n" + Tail, Text(tab));
        view.MoveTo(0, 0);
        Run("nks.table.deleteColumn");
        Assert.Equal(Head + "name\n\"A\nB\"\nRobert\n\n" + Tail, Text(tab));
        while (tab.Doc.Doc.CanUndo) tab.Doc.Doc.Undo();
        Assert.Equal(before, Bytes(tab));

        Run("nks.view.table");
        Assert.False(tab.TableShown);
        Run("nks.view.table");
        Assert.False(_w.ActiveTable!.IsRegion);
    }

    [AvaloniaFact]
    public async Task A_selection_with_another_delimiter_and_no_quote_is_read_as_chosen()
    {
        string body = "a;\"b\n1;2\n";
        var tab = OpenWithSelection("x\n" + body + "y\n", 2, body.Length);
        _dialogs.TableRegions.Enqueue(new CsvDialect(';', '\0', HasHeader: false));
        await _w.EditSelectionAsTableAsync();
        var view = _w.ActiveTable!;
        while (!view.RowsComplete) view.Tick();
        Assert.Equal(2, view.DataRowCount);
        Assert.Equal("\"b", view.CellText(0, 1));
        Assert.StartsWith("Row 1 of 2, column 1 of 2 (A) · only the selected text", _w.StatusLeftText);
        Assert.True(view.SetCell(1, 1, "9"));
        Assert.Equal("x\na;\"b\n1;9\ny\n", Text(tab));
    }

    [AvaloniaFact]
    public async Task Without_a_selection_nothing_opens_and_the_status_line_says_why()
    {
        var tab = OpenCsv(Sample, "plain.txt");
        await _w.EditSelectionAsTableAsync();
        Assert.False(tab.TableShown);
        Assert.Equal("Select the text to edit as a table first.", _w.StatusLeftText.Split(" · ")[^1]);
    }

    [AvaloniaFact]
    public async Task The_dialect_menu_of_a_selection_table_changes_only_that_table()
    {
        var tab = OpenWithSelection("a;b\n1;2\n", 0, 8);
        _dialogs.TableRegions.Enqueue(CsvDialect.Csv);
        await _w.EditSelectionAsTableAsync();
        var view = _w.ActiveTable!;
        Assert.Equal(["a;b"], view.Table.Header());
        Assert.NotNull(_w.ShowTableDialectMenu());
        view.SetDialect(new CsvDialect(';'));
        Assert.Equal(["a", "b"], view.Table.Header());
        Assert.Equal(CsvDialect.ForPath(null), tab.Doc.TableDialect);
    }

    [AvaloniaFact]
    public async Task The_table_dialog_follows_its_choices_and_returns_them_on_OK()
    {
        var dialogs = new NksHub.Textida.App.Services.AvaloniaDialogs();
        var previews = new List<string>();
        dialogs.Shown = w => Dispatcher.UIThread.Post(() =>
        {
            T Find<T>(Func<T, bool> match) where T : Avalonia.Controls.Control => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<T>().First(match);
            Find<Avalonia.Controls.RadioButton>(r => r.Content as string == Loc.Get(K.TableDelimiterSemicolon)).IsChecked = true;
            Find<Avalonia.Controls.CheckBox>(c => c.Content as string == Loc.Get(K.TableQuote)).IsChecked = false;
            Find<Avalonia.Controls.CheckBox>(c => c.Content as string == Loc.Get(K.TableHeader)).IsChecked = false;
            Find<Avalonia.Controls.Button>(b => b.Content as string == Loc.Get(K.DialogOk)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }, DispatcherPriority.Background);

        var result = await dialogs.TableRegionAsync(_w, CsvDialect.Csv, d => { previews.Add($"{d.Delimiter}/{d.Quote == '\0'}/{d.HasHeader}"); return "preview"; });

        Assert.Equal(new CsvDialect(';', '\0', false), result);
        Assert.Contains(";/True/False", previews);
    }
}

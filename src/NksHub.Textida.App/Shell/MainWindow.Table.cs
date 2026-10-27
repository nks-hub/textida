using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The table view of the window (P5.1): a document read as CSV or TSV lies over its text view in the tab, with cells, a filter and the
/// commands that add and delete rows and columns. The text view below stays as it was; the table reads and edits the same document.
/// </summary>
internal sealed partial class MainWindow
{
    internal CsvView? ActiveTable => Active is { TableShown: true } tab ? tab.Table : null;

    /// <summary>The accessible names of the table views follow the UI language.</summary>
    private void ApplyTableTexts()
    {
        foreach (var tab in Tabs)
            if (tab.Table is { } table) AutomationProperties.SetName(table, Loc.Get(K.A11yTable));
    }

    /// <summary>The command <c>nks.view.table</c>: the active document is shown as a table, or as text again.</summary>
    internal CommandOutcome ToggleTable()
    {
        if (Active is not { Custom: null } tab || tab.Doc.IsResults || tab.Doc.IsHex) return CommandOutcome.NotHandled;
        SetTable(tab, !tab.TableShown);
        return CommandOutcome.Handled;
    }

    internal void SetTable(DocTab tab, bool on)
    {
        if (on == tab.TableShown) return;
        var before = tab.Table;
        tab.ShowTable(on, OptionsFor(tab));
        if (on && tab.Table != before) WireTable(tab, tab.Table!);
        FocusView(tab);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Active == tab) FocusView(tab); }, Avalonia.Threading.DispatcherPriority.Background);
        UpdateStatus();
    }

    private void WireTable(DocTab tab, CsvView table)
    {
        table.ViewChanged += () => { if (tab == Active) UpdateStatus(); };
        table.Refused += SetStatusMessage;
        AutomationProperties.SetName(table, Loc.Get(K.A11yTable));
    }

    internal CommandOutcome TableCommand(Action<CsvView> run)
    {
        if (ActiveTable is not { } table) return CommandOutcome.NotHandled;
        run(table);
        return CommandOutcome.Handled;
    }

    // ---- dialect ----

    /// <summary>Sets how the document is split into cells; every table view of it follows.</summary>
    internal void SetTableDialect(OpenDocument od, CsvDialect dialect)
    {
        dialect.Validate();
        od.TableDialect = dialect;
        foreach (var view in od.Views) view.Table?.ApplyDialect();
    }

    internal MenuFlyout? ShowTableDialectMenu()
    {
        if (ActiveTable is not { } table || Active is not { } tab) return null;
        var od = tab.Doc;
        var current = table.Dialect;
        var flyout = new MenuFlyout();
        void Choose(CsvDialect dialect)
        {
            if (table.IsRegion) table.SetDialect(dialect);
            else SetTableDialect(od, dialect);
        }
        MenuItem Item(string header, bool on, Action run, bool radio, string group)
        {
            var item = new MenuItem { Header = header, ToggleType = radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox, IsChecked = on, GroupName = group };
            item.Click += (_, _) => run();
            return item;
        }
        foreach (var (key, delimiter) in new[] { (K.TableDelimiterComma, ','), (K.TableDelimiterSemicolon, ';'), (K.TableDelimiterTab, '\t'), (K.TableDelimiterPipe, '|') })
        {
            char d = delimiter;
            flyout.Items.Add(Item(Loc.Get(key), current.Delimiter == d, () => Choose(current with { Delimiter = d }), radio: true, group: "delimiter"));
        }
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Item(Loc.Get(K.TableQuote), current.HasQuote, () => Choose(current with { Quote = current.HasQuote ? '\0' : '"' }), radio: false, group: "quote"));
        flyout.Items.Add(Item(Loc.Get(K.TableHeader), current.HasHeader, () => Choose(current with { HasHeader = !current.HasHeader }), radio: false, group: "header"));
        flyout.ShowAt(table);
        return flyout;
    }

    // ---- a selected part as a table ----

    /// <summary>The bytes of the text selected in the active tab's text view; null when nothing is selected.</summary>
    private ByteRange? SelectedBytes(DocTab tab)
    {
        if (tab.Surface is { } surface)
            return ToBytes(surface, surface.GetSelections()).Cast<ByteRange?>().FirstOrDefault(r => !r!.Value.IsEmpty);
        return tab.Paged is { HasSelection: true } paged ? paged.SelectionRange : null;
    }

    /// <summary>The command <c>nks.table.editRegion</c>: the selected text becomes a table; the rest of the document stays text and is never read or changed by it.</summary>
    internal async Task EditSelectionAsTableAsync()
    {
        if (Active is not { Custom: null } tab || tab.Doc.IsResults || tab.Doc.IsHex) return;
        if (tab.TableShown || SelectedBytes(tab) is not { } range)
        {
            SetStatusMessage(Loc.Get(K.TableRegionEmpty));
            return;
        }
        var doc = tab.Doc.Doc;
        string Preview(CsvDialect dialect)
        {
            using var sample = new CsvTable(doc, dialect, range);
            sample.EnsureRows(PreviewRows);
            var lines = Enumerable.Range(0, Math.Min(sample.RowCount, PreviewRows)).Select(r => string.Join(" | ", sample.Row(r).Select(v => v.ReplaceLineEndings(" ")).Select(v => v.Length > 24 ? v[..24] + "…" : v)));
            return sample.RowCount == 0 ? Loc.Get(K.TableStatusEmpty) : string.Join('\n', lines);
        }
        if (await _app.Dialogs.TableRegionAsync(this, tab.Doc.TableDialect, Preview) is not { } chosen || !_docs.Contains(tab.Doc)) return;
        var before = tab.Table;
        tab.ShowTable(true, OptionsFor(tab), range, chosen);
        if (tab.Table != before) WireTable(tab, tab.Table!);
        FocusView(tab);
        UpdateStatus();
    }

    private const int PreviewRows = 6;

    // ---- commands ----

    internal async Task InsertTableColumnAsync()
    {
        if (ActiveTable is not { } table) return;
        string name = "";
        if (table.HasHeader)
        {
            if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.TableColumnTitle), Loc.Get(K.TableColumnLabel), "") is not { } entered) return;
            name = entered;
        }
        if (ActiveTable == table) table.InsertColumn(name);
    }

    internal async Task FilterTableAsync()
    {
        if (ActiveTable is not { } table) return;
        string label = Loc.Get(K.TableFilterLabel, ("column", table.Column + 1), ("name", table.ColumnName(table.Column)));
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.TableFilterTitle), label, table.Filter?.Text ?? "") is not { } text) return;
        if (ActiveTable == table) table.ApplyFilter(text.Trim(), table.Column);
    }

    // ---- clipboard ----

    /// <summary>The byte ranges of the values that are hidden in the tab (a secrets file whose values are not shown); null when nothing is hidden.</summary>
    private static IReadOnlyList<ByteRange>? HiddenValues(DocTab tab)
    {
        if (tab.Surface is not { SecretsHidden: true }) return null;
        var doc = tab.Doc.Doc;
        return doc.ByteLength > PluginLimitsPlaceholders.SecretScanMaxBytes ? [new ByteRange(doc.BomLength, doc.ByteLength)] : SecretValues.ByteRanges(doc.GetSnapshot(), doc.BomLength);
    }

    /// <summary>Copy and cut of cells: a hidden value goes to the clipboard as the placeholder, and a cut that touches one deletes nothing.</summary>
    private CommandOutcome TableCopy(CsvView table, bool cut)
    {
        string text = table.SelectedText(Active is { } tab ? HiddenValues(tab) : null, out bool masked);
        RememberCopy(text);
        if (Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
        if (cut && !masked) table.ClearSelection();
        return CommandOutcome.Handled;
    }

    /// <summary>Puts text, and HTML when given, on the clipboard in one go, and into the clipboard history.</summary>
    internal void WriteClipboard(string text, string? html)
    {
        RememberCopy(text);
        if (Clipboard is not { } clipboard) return;
        var item = DataTransferItem.CreateText(text);
        if (html is not null) item.Set(ClipboardHtml.Format, ClipboardHtml.Encode(html));
        var data = new DataTransfer();
        data.Add(item);
        _ = clipboard.SetDataAsync(data);
    }

    // ---- copy as ----

    /// <summary>The forms a selection of cells can be copied as: the command of the text tools plugin and the name in the menu.</summary>
    internal static readonly (string Command, string Name)[] TableCopyForms =
    [
        ("table.copyAs.markdown", K.TableCopyAsMarkdown), ("table.copyAs.sql", K.TableCopyAsSql), ("table.copyAs.json", K.TableCopyAsJson), ("table.copyAs.html", K.TableCopyAsHtml),
    ];

    /// <summary>The command <c>nks.table.copyAs</c>: a menu of the forms the selected cells are copied as (Markdown table, SQL INSERT, JSON, HTML).</summary>
    internal MenuFlyout? ShowTableCopyAsMenu()
    {
        if (ActiveTable is not { } table) return null;
        var flyout = new MenuFlyout();
        foreach (var (command, name) in TableCopyForms)
        {
            var item = new MenuItem { Header = Loc.Get(name) };
            var run = command;
            item.Click += (_, _) => _ = CopyTableAsAsync(run);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(table);
        return flyout;
    }

    /// <summary>Copies the selected cells through the text tools plugin, which reads them with the table API and writes the clipboard. Hidden values of a secrets file reach it only as <c>*</c>.</summary>
    internal async Task<bool> CopyTableAsAsync(string command)
    {
        if (ActiveTable is null || Active is not { } tab) return false;
        return await _app.Plugins.RunTableCopyAsync(this, tab, command);
    }

    private async Task<CommandOutcome> TablePasteAsync(CsvView table)
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (!string.IsNullOrEmpty(text) && ActiveTable == table) table.PasteText(text);
        return CommandOutcome.Handled;
    }
}

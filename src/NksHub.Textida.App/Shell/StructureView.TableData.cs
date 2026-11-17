using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView
{
    private const int TableFieldPageSize = MaximumItems / PageSize;
    private Request? tableRequest;
    private Session? tableOwner;
    private Row? tableSelection;
    private StructureNode? tableContainer;
    private bool tableSelecting, tableIsResultPage;
    private int tablePage, tableFieldPage;
    private Results? tableResults;
    private StructureTablePages tablePages = new();

    private void LoadTable()
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner)) return;
        if (showingResults && tableResults is { } results)
        {
            if (!ReferenceEquals(owner, tableOwner) || !tableIsResultPage || tablePage != resultPage) LoadTableResults(results, resultPage);
            return;
        }
        var selected = tree.SelectedNode is { } item && rows.TryGetValue(item, out var found) ? found : null;
        var target = selected?.Data.Node;
        if (target is { ChildCount: 0 }) target = selected?.Parent?.Data.Node;
        long id = target?.Id ?? 0;
        if (ReferenceEquals(owner, tableOwner) && !tableIsResultPage && tableContainer is { } container && container.Id == id) return;
        LoadTableRoot(id);
    }

    private void LoadTableRoot(long id)
    {
        tablePage = tableFieldPage = 0; tableSelection = null;
        tableContainer = null; tableOwner = session; tableIsResultPage = false;
        tablePages = new();
        ReadTablePage(id, 0, 0);
    }

    private void LoadTableResults(Results results, long page)
    {
        tableOwner = session; tableContainer = null; tableSelection = null;
        tableIsResultPage = true; tablePage = checked((int)page); tableFieldPage = 0;
        ReadTablePage(0, tablePage, 0, results);
    }

    private void LoadTablePage(int page, int fields)
    {
        if (showingResults && tableResults is { } results)
        {
            if (page != tablePage) StartResults(Math.Max(0, page));
            else { tableFieldPage = Math.Max(0, fields); ReadTablePage(0, tablePage, tableFieldPage, results); }
            return;
        }
        if (tableContainer is not { } container) return;
        tableSelection = null;
        tableFieldPage = page == tablePage ? Math.Max(0, fields) : 0;
        tablePage = Math.Max(0, page);
        ReadTablePage(container.Id, tablePage, tableFieldPage);
    }

    private void ReadTablePage(long id, int page, int fieldPage, Results? matches = null)
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner)) return;
        tableRequest?.Cancel();
        var request = tableRequest = new Request(owner.Token);
        var knownPages = tablePages;
        var task = Run(owner, ct =>
        {
            var cursors = knownPages.Clone();
            var index = owner.Analysis!.Index;
            StructureNode? container = null;
            IReadOnlyList<RowData> children;
            var reader = new StructureText.Reader(index, ct);
            bool moreRows;
            if (matches is not null) { children = matches.Rows; moreRows = !matches.Complete; }
            else
            {
                var parent = index.ReadNode(id);
                if (parent.Kind == StructureKind.Document && parent.ChildCount == 1) parent = index.ReadNode(parent.Id + 1);
                container = parent;
                var nodes = cursors.Read(index, parent, page, PageSize, ct);
                children = nodes.Select(node => ReadRow(index, node, ct, reader)).ToArray();
                moreRows = nodes.Count > 0 && nodes[^1].Ordinal + 1 < parent.ChildCount;
            }
            var columns = new List<StructureTableColumn>();
            var columnsSeen = new HashSet<StructureTableColumn>();
            var data = new List<StructureTableRow>(children.Count);
            bool records = children.Any(child => child.Node.ChildCount > 0), moreFields = false;
            foreach (var row in children)
            {
                ct.ThrowIfCancellationRequested();
                var child = row.Node;
                var cells = new Dictionary<StructureTableColumn, StructureTableCell>();
                if (records && child.ChildCount > 0)
                {
                    var fields = cursors.Read(index, child, fieldPage, TableFieldPageSize, ct);
                    moreFields |= fields.Count > 0 && fields[^1].Ordinal + 1 < child.ChildCount;
                    var names = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var field in fields)
                    {
                        var value = ReadRow(index, field, ct, reader);
                        string name = DisplayKey(value);
                        int occurrence = names.GetValueOrDefault(name) + 1; names[name] = occurrence;
                        var key = new StructureTableColumn(name, occurrence);
                        cells.Add(key, new(field, value.Name, DisplayValue(value)));
                        if (columnsSeen.Add(key)) columns.Add(key);
                    }
                }
                else
                {
                    var key = new StructureTableColumn(Loc.Get(K.StructureTableValue));
                    cells.Add(key, new(child, row.Name, DisplayValue(row), row.ResultRange));
                    if (columnsSeen.Add(key)) columns.Add(key);
                }
                data.Add(new(DisplayKey(row), child, cells));
            }
            return (Container: container, Page: new StructureTablePage(columns, data, page, fieldPage, moreRows, moreFields), Cursors: cursors);
        }, result =>
        {
            tableOwner = owner; tableContainer = result.Container; tablePages = result.Cursors;
            tableSelection = null; table.SetPage(result.Page); UpdateButtons();
        }, request.Token);
        var cleanup = task.ContinueWith(_ => request.Dispose(), TaskScheduler.Default);
        ObserveCancellationCleanup(owner, cleanup);
    }

    private void SelectTableCell(StructureTableCell? cell)
    {
        tableSelecting = true;
        try
        {
            tableSelection = cell is null ? null : new(new(cell.Node, cell.Name, cell.Value, cell.ResultRange), new StructureTreeNode(), null);
            UpdateButtons();
            if (cell is not null) Jump();
        }
        finally { tableSelecting = false; }
    }

    private void EditTableCell(StructureTableCell cell)
    {
        SelectTableCell(cell);
        if (cell.Node.ChildCount > 0) EnterTableContainer(cell.Node.Id);
        else _ = EditAsync();
    }

    private void EnterTableContainer(long id)
    {
        if (showingResults)
        {
            changing = true; filter.Text = ""; changing = false;
            showingResults = false; tableResults = null; LoadRoot(id);
        }
        else LoadTableRoot(id);
    }
}

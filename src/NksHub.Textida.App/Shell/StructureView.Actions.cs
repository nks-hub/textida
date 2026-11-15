using Avalonia.Controls;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureView
{
    private (Session Owner, Row Row, long RequestId, long NodeId, long Offset)? sourceJump;
    private sealed record Results(IReadOnlyList<RowData> Rows, long Cursor, long Next, bool Complete);
    private string resultSignature = "";
    private Row? Selected => tree.SelectedNode is { } item && rows.TryGetValue(item, out var row) ? row : null;

    private void UpdateButtons()
    {
        bool ready = session is { Analysis.IsValid: true } owner && Current(owner);
        bool selected = ready && Selected != null;
        refresh.IsEnabled = session != null;
        cancel.IsEnabled = session != null && !session.Token.IsCancellationRequested;
        up.IsEnabled = ready && !showingResults && rootParentId >= 0;
        jump.IsEnabled = copyPath.IsEnabled = copySource.IsEnabled = selected;
        edit.IsEnabled = selected && session?.Source.ReadOnly == false && Editable(Selected!.Data.Node.Kind);
        find.IsEnabled = clear.IsEnabled = ready;
        previous.IsVisible = next.IsVisible = showingResults;
        if (!showingResults) previous.IsEnabled = next.IsEnabled = false;
    }

    private static bool Editable(StructureKind kind) => kind is StructureKind.JsonString or StructureKind.JsonNumber
        or StructureKind.JsonBoolean or StructureKind.JsonNull or StructureKind.XmlAttribute or StructureKind.XmlText
        or StructureKind.XmlCData or StructureKind.XmlElement;

    private void Jump()
    {
        if (session is { } owner && Current(owner) && Selected is { } row && source() is { } latest && SameSource(latest, owner.Source))
        {
            row.Touched = ++clock;
            var range = row.Data.Range;
            var previous = sourceJump;
            sourceJump = (owner, row, row.RequestId, row.Data.Node.Id, Math.Max(latest.BomLength, range.Start));
            try { latest.Jump(range); }
            finally { sourceJump = previous; }
        }
    }

    private void StartResults(long page, bool debounce = false)
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner)) return;
        string expression = filter.Text ?? "";
        if (expression.Length == 0) { ShowDocument(); return; }
        int mode = Math.Max(0, scope.SelectedIndex);
        string mappings = namespaces.Text ?? "";
        string signature = mode + "\0" + expression + "\0" + mappings;
        if (signature != resultSignature) { page = 0; resultCursors.Clear(); resultSignature = signature; }
        filterRequest?.Cancel();
        var request = filterRequest = new Request(owner.Token);
        var known = resultCursors.ToDictionary(pair => pair.Key, pair => pair.Value);
        SetStatus(K.StructureLoading);
        Task task = Run(owner, ct =>
        {
            var index = owner.Analysis!.Index;
            StructureQuery? query = mode == 3 ? StructureQuery.Parse(expression, index.Format == StructureFormat.Xml ? StructureQueryLanguage.XPath : StructureQueryLanguage.JsonPath) : null;
            var ns = mode == 3 && index.Format == StructureFormat.Xml ? NamespaceMap(mappings) : null;
            long cursor = known.GetValueOrDefault(page, 0);
            long currentPage = known.ContainsKey(page) ? page : 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var found = query != null ? query.ReadPage(index, cursor, PageSize, ct, ns)
                    : StructureSearch.ReadPage(index, expression, (StructureSearchScope)mode, cursor, PageSize, ct);
                if (currentPage == page || found.Complete)
                {
                    var result = new List<RowData>(found.NodeIds.Count);
                    var reader = new StructureText.Reader(index, ct);
                    foreach (long id in found.NodeIds)
                    {
                        var node = index.ReadNode(id);
                        var data = ReadRow(index, node, ct, reader);
                        if (query != null && index.Format == StructureFormat.Xml)
                        {
                            var preview = reader.ReadResultValue(node, 256);
                            data = data with { Value = preview.Text + (preview.Truncated ? "…" : ""), ResultRange = StructureQuery.ResultRange(index, id, ct) };
                        }
                        result.Add(data);
                    }
                    return new Results(result, cursor, found.NextNodeId, found.Complete);
                }
                cursor = found.NextNodeId; currentPage++;
            }
        }, result =>
        {
            ResetRows(); showingResults = true; rootParentId = -1;
            ReserveRows(result.Rows.Count, result.Rows.Count(data => data.Node.ChildCount > 0));
            Programmatic(() => { foreach (var data in result.Rows) tree.Items.Add(NewRow(data, null).Item); });
            resultPage = page;
            resultCursors[page] = result.Cursor;
            if (!result.Complete) resultCursors[page + 1] = result.Next;
            while (resultCursors.Count > 64) resultCursors.Remove(resultCursors.Keys.First());
            previous.IsEnabled = page > 0; next.IsEnabled = !result.Complete;
            SetStatus(result.Rows.Count == 0 ? K.StructureNoResults : K.StructureResults, ("page", page + 1));
            UpdateButtons(); MarkCaret(owner, reveal: false);
        }, request.Token, debounce ? 300 : 0);
        var cleanup = task.ContinueWith(_ => request.Dispose(), TaskScheduler.Default);
        ObserveCancellationCleanup(owner, cleanup);
    }

    private static Dictionary<string, string> NamespaceMap(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string entry in text.Split('\n'))
        {
            string line = entry.Trim();
            if (line.Length == 0) continue;
            int equal = line.IndexOf('=');
            if (equal < 1 || equal == line.Length - 1) throw new FormatException(Loc.Get(K.StructureNamespaceError));
            string prefix = line[..equal].Trim(), uri = line[(equal + 1)..].Trim();
            try { System.Xml.XmlConvert.VerifyNCName(prefix); }
            catch (System.Xml.XmlException) { throw new FormatException(Loc.Get(K.StructureNamespaceError)); }
            if (uri.Length == 0 || !result.TryAdd(prefix, uri)) throw new FormatException(Loc.Get(K.StructureNamespaceError));
        }
        return result;
    }

    private Task CopyAsync(bool path)
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner) || Selected is not { } row) return Task.CompletedTask;
        var data = row.Data;
        return Queue(async () =>
        {
            try
            {
                owner.Token.ThrowIfCancellationRequested();
                var index = owner.Analysis!.Index;
                var text = path ? new StructurePreview(StructurePaths.Create(index, data.Node, ct: owner.Token), false)
                    : StructureText.ReadSource(index, data.Node with { Start = data.Range.Start, End = data.Range.End }, StructureText.MaxChars, owner.Token);
                owner.Token.ThrowIfCancellationRequested();
                Task copyTask = await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    if (!Current(owner) || source() is not { } latest || !SameSource(latest, owner.Source)) return Task.CompletedTask;
                    if (text.Truncated) { SetStatus(K.StructureCopyLimit, ("limit", StructureText.MaxChars)); return Task.CompletedTask; }
                    return copy(text.Text);
                });
                await copyTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { await Dispatcher.UIThread.InvokeAsync(() => { if (Current(owner)) SetStatus(K.StructureFailed, ("message", error.Message)); }); }
        });
    }

    private Task EditAsync()
    {
        if (session is not { Analysis.IsValid: true } owner || !Current(owner) || owner.Source.ReadOnly || Selected is not { } row) return Task.CompletedTask;
        var data = row.Data;
        if (data.Grouped) { SetStatus(K.StructureGroupEdit); return Task.CompletedTask; }
        return Queue(async () =>
        {
            try
            {
                owner.Token.ThrowIfCancellationRequested();
                var index = owner.Analysis!.Index;
                var target = EditableLeaf(index, data.Node, owner.Token);
                var previousValue = StructureText.ReadValue(index, target, StructureEdits.MaxInputChars, owner.Token);
                Task<string?> dialog = await Dispatcher.UIThread.InvokeAsync<Task<string?>>(() =>
                {
                    if (!Current(owner) || source() is not { ReadOnly: false } latest || !SameSource(latest, owner.Source)) return Task.FromResult<string?>(null);
                    return prompt(Loc.Get(K.StructureEditTitle), Loc.Get(previousValue.Truncated ? K.StructureEditLarge : K.StructureEditLabel, ("limit", StructureEdits.MaxInputChars)), previousValue.Truncated ? "" : previousValue.Text);
                });
                string? value = await dialog.ConfigureAwait(false);
                owner.Token.ThrowIfCancellationRequested();
                if (value == null) return;
                var change = StructureEdits.CreateEdit(index, target, value, owner.Token);
                Task<bool> apply = await Dispatcher.UIThread.InvokeAsync<Task<bool>>(() =>
                {
                    if (!Current(owner) || source() is not { ReadOnly: false } latest || !SameSource(latest, owner.Source)) return Task.FromResult(false);
                    return latest.Apply(change, index.Revision, owner.Token);
                });
                bool applied = await apply.ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!Current(owner)) return;
                    if (applied) Sync();
                    else SetStatus(K.StructureStale);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { await Dispatcher.UIThread.InvokeAsync(() => { if (Current(owner)) SetStatus(K.StructureEditFailed, ("message", error.Message)); }); }
        });
    }

    private static StructureNode EditableLeaf(IStructureIndex index, StructureNode node, CancellationToken ct)
    {
        if (node.Kind != StructureKind.XmlElement) return node;
        StructureNode? selected = null;
        for (long id = node.Id + 1; id <= node.LastDescendantId;)
        {
            ct.ThrowIfCancellationRequested();
            var child = index.ReadNode(id);
            if (child.Kind == StructureKind.XmlElement || (child.Kind is StructureKind.XmlText or StructureKind.XmlCData && selected.HasValue))
                throw new InvalidOperationException(Loc.Get(K.StructureSingleValue));
            if (child.Kind is StructureKind.XmlText or StructureKind.XmlCData) selected = child;
            id = child.LastDescendantId + 1;
        }
        return selected ?? throw new InvalidOperationException(Loc.Get(K.StructureSingleValue));
    }

    private void ShowIssues()
    {
        var index = session?.Analysis?.Index;
        int count = index?.Issues.Count ?? 0;
        issue.IsVisible = nextIssue.IsVisible = count > 0;
        nextIssue.IsEnabled = count > 1;
        if (count == 0) return;
        issuePosition %= count;
        var problem = index!.Issues[issuePosition];
        issue.Content = Loc.Get(K.StructureIssue, ("position", issuePosition + 1), ("total", count), ("message", problem.Message));
        ToolTip.SetTip(issue, Loc.Get(K.StructureIssues, ("shown", count), ("total", index.IssueCount)));
    }

    private void JumpIssue()
    {
        if (session is not { Analysis: { } analysis } owner || !Current(owner) || analysis.Index.Issues.Count == 0) return;
        var problem = analysis.Index.Issues[issuePosition % analysis.Index.Issues.Count];
        if (source() is { } latest && SameSource(latest, owner.Source)) latest.Jump(new(problem.Start, problem.End));
    }
}

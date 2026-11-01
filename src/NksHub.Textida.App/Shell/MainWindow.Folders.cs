using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Compares two folders (PZ.7.3), each a local folder or a folder on a saved server, by size, modification time or content. The result opens as a report in a new tab; a copy from one side to the other is offered, shown as a list of
/// the files it would write, and carried out only after the user has confirmed that list. Nothing is ever deleted: files that only the target has stay as they are.
/// </summary>
internal sealed partial class MainWindow
{
    private const int FolderPreviewLines = 40;

    internal CommandOutcome CompareFolders()
    {
        _ = CompareFoldersAsync();
        return CommandOutcome.Handled;
    }

    private async Task CompareFoldersAsync()
    {
        var clients = new List<IRemoteClient>();
        try
        {
            var left = await PickFolderSideAsync(1, clients);
            if (left is null) return;
            var right = await PickFolderSideAsync(2, clients);
            if (right is null) return;
            await CompareFolderSidesAsync(left, right);
        }
        finally
        {
            foreach (var client in clients) await client.DisposeAsync();
        }
    }

    private async Task<IFolderSource?> PickFolderSideAsync(int number, List<IRemoteClient> clients)
    {
        string title = Loc.Get(K.FoldersTitle);
        int kind = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.FoldersSide, ("number", number)),
            [new DialogButton(Loc.Get(K.FoldersLocal), IsDefault: true), new DialogButton(Loc.Get(K.FoldersServer)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (kind == 0) return await _app.Dialogs.PickFolderAsync(this) is { Length: > 0 } folder ? new LocalFolderSource(folder) : null;
        if (kind != 1) return null;

        var items = _app.Remote.Connections.Load().Select(c => (c.Name, (string?)DescribeConnection(c), (RemoteConnection?)c)).ToList();
        var pick = await PickAsync(Loc.Get(K.FoldersConnectionWatermark), Loc.Get(K.FoldersConnectionA11y), items, Loc.Get(K.FoldersNoConnections));
        if (!pick.Chosen || pick.Value is not { } connection) return null;
        string? path = (await _app.Dialogs.PromptAsync(this, title, Loc.Get(K.FoldersServerPath, ("name", connection.Name)), "/"))?.Trim();
        if (string.IsNullOrEmpty(path)) return null;
        if (await ConnectAsync(connection) is not { } client) return null;
        clients.Add(client);
        return new RemoteFolderSource(client, path, connection.Name + ":" + path);
    }

    private string FolderByName(FolderCompareBy by) => Loc.Get(by switch { FolderCompareBy.Size => K.FoldersReportSize, FolderCompareBy.Time => K.FoldersReportTime, _ => K.FoldersReportContent });

    /// <summary>Compares the two sides, opens the report and offers the copy. Public to the tests, which give it local sides.</summary>
    internal async Task CompareFolderSidesAsync(IFolderSource left, IFolderSource right)
    {
        string title = Loc.Get(K.FoldersTitle);
        int choice = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.FoldersBy),
            [new DialogButton(Loc.Get(K.FoldersBySize)), new DialogButton(Loc.Get(K.FoldersByTime), IsDefault: true), new DialogButton(Loc.Get(K.FoldersByContent)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (choice is < 0 or > 2) return;
        var by = (FolderCompareBy)choice;

        SetStatusMessage(Loc.Get(K.FoldersComparing));
        IReadOnlyList<FolderDifference> differences;
        try { differences = await FolderCompare.CompareAsync(left, right, by, CancellationToken.None); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or RemoteException)
        {
            SetStatusMessage(Loc.Get(K.FoldersFailed, ("reason", e.Message)));
            return;
        }

        int same = differences.Count(d => d.State == FolderState.Same);
        int different = differences.Count(d => d.State == FolderState.Different);
        int onlyLeft = differences.Count(d => d.State == FolderState.OnlyLeft);
        int onlyRight = differences.Count(d => d.State == FolderState.OnlyRight);
        string summary = Loc.Get(K.FoldersSummary, ("same", same), ("different", different), ("left", onlyLeft), ("right", onlyRight));
        SetStatusMessage(summary);
        if (same == differences.Count) return;

        OpenGeneratedText(summary + Environment.NewLine + FolderCompare.Report(left, right, by, differences, Environment.NewLine, FolderByName), "plaintext");

        var toRight = FolderCompare.PlanCopy(differences, CopyDirection.LeftToRight);
        var toLeft = FolderCompare.PlanCopy(differences, CopyDirection.RightToLeft);
        if (toRight.Count == 0 && toLeft.Count == 0) return;
        int direction = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.FoldersCopyAsk, ("summary", summary)),
            [new DialogButton(Loc.Get(K.FoldersCopyRight, ("count", toRight.Count))), new DialogButton(Loc.Get(K.FoldersCopyLeft, ("count", toLeft.Count))), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]));
        if (direction is not (0 or 1)) return;

        var (from, to, steps) = direction == 0 ? (left, right, toRight) : (right, left, toLeft);
        if (steps.Count == 0) return;
        var preview = steps.Take(FolderPreviewLines).Select(s => Loc.Get(s.Overwrites ? K.FoldersPreviewReplace : K.FoldersPreviewNew, ("path", s.Path))).ToList();
        if (steps.Count > FolderPreviewLines) preview.Add(Loc.Get(K.FoldersPreviewMore, ("count", steps.Count - FolderPreviewLines)));
        int confirmed = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title,
            Loc.Plural(K.FoldersPreview, steps.Count, ("replaced", steps.Count(s => s.Overwrites)), ("from", from.Label), ("to", to.Label)),
            [new DialogButton(Loc.Get(K.FoldersCopyDo)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)], preview));
        if (confirmed != 0) return;

        var times = differences.ToDictionary(d => d.Path, d => (direction == 0 ? d.Left : d.Right)?.ModifiedUtc, StringComparer.Ordinal);
        FolderCopyResult result;
        try { result = await FolderCompare.CopyAsync(from, to, steps, path => times.GetValueOrDefault(path), step => Avalonia.Threading.Dispatcher.UIThread.Post(() => SetStatusMessage(Loc.Get(K.FoldersCopying, ("path", step.Path)))), CancellationToken.None); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or RemoteException)
        {
            SetStatusMessage(Loc.Get(K.FoldersFailed, ("reason", e.Message)));
            return;
        }
        foreach (var (path, reason) in result.Failed) _app.Output.Append(OutputChannel.Tasks, "folder-compare", path + ": " + reason);
        if (result.Failed.Count > 0) ShowOutput(OutputChannel.Tasks);
        SetStatusMessage(Loc.Get(K.FoldersCopied, ("copied", result.Copied), ("failed", result.Failed.Count)));
    }
}

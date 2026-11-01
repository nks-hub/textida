using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A set of remote changes (PZ.7.1): the copies of one connection that were saved and not sent, listed with the state of their server file. The user ticks the files to send, may open the differences of any of them
/// (local copy against the server file), and sends the ticked ones in one go. A file whose server version changed, or is gone, cannot be ticked before its differences were shown (or, for a deleted file, the question answered);
/// the upload then expects exactly the version that was shown, so a newer one is still a conflict and nothing is overwritten. A tab with unsaved edits is not sent: its file on disk is not what the user sees.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<string, RemoteRevision> _remoteDiffSeen = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The running Send Changes, for tests.</summary>
    internal Task SendRemoteChangesTask { get; private set; } = Task.CompletedTask;

    internal CommandOutcome SendRemoteChanges()
    {
        SendRemoteChangesTask = SendRemoteChangesAsync();
        return CommandOutcome.Handled;
    }

    private sealed class ChangeItem(CopyStatus status, bool unsavedTab)
    {
        public CopyStatus Status { get; } = status;
        public bool UnsavedTab { get; } = unsavedTab;
        public bool Selected { get; set; }
        public bool NeedsDiff => Status.Server is ServerState.Changed or ServerState.Missing;
    }

    private bool HasUnsavedTab(WorkingCopy copy) =>
        _app.Windows.SelectMany(w => w.Documents).Any(d => d.Doc.IsModified && d.RemoteCopy is { } c && string.Equals(c.LocalPath, copy.LocalPath, StringComparison.OrdinalIgnoreCase));

    internal async Task SendRemoteChangesAsync(RemoteConnection? only = null)
    {
        var unsent = _app.Remote.Copies.Unsent().Where(c => _app.Remote.Copies.LocalStateOf(c) == LocalState.Unsent).ToList();
        var connections = _app.Remote.Connections.Load().Where(c => unsent.Any(u => u.ConnectionId == c.Id)).ToList();
        if (connections.Count == 0) { SetStatusMessage(Loc.Get(K.RemoteUnsentEmpty)); return; }
        var connection = only ?? (connections.Count == 1 ? connections[0] : null);
        if (connection is null)
        {
            var pick = await PickAsync(Loc.Get(K.RemoteConnectionsWatermark), Loc.Get(K.RemoteConnectionsA11y), [.. connections.Select(c => (c.Name, (string?)DescribeConnection(c), c))]);
            if (!pick.Chosen) return;
            connection = pick.Value!;
        }

        await using var client = await ConnectAsync(connection);
        if (client is null) return;
        List<ChangeItem> items;
        try
        {
            var paired = await _app.Remote.Copies.PairAsync(client, CancellationToken.None);
            items = [.. paired.Where(s => s.Local == LocalState.Unsent).OrderBy(s => s.Copy.RemotePath, StringComparer.Ordinal).Select(s => new ChangeItem(s, HasUnsavedTab(s.Copy)))];
        }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); return; }
        if (items.Count == 0) { SetStatusMessage(Loc.Get(K.RemoteUnsentEmpty)); return; }

        const int Send = -1, Diff = -2, Note = -3;
        string? note = null;
        while (true)
        {
            int selected = items.Count(i => i.Selected);
            var rows = new List<(string Title, string? Detail, int Value)>
            {
                (Loc.Get(K.RemoteChangesSend, ("selected", selected)), null, Send),
                (Loc.Get(K.RemoteChangesDiff), null, Diff),
            };
            if (note is not null) rows.Insert(0, (note, null, Note));
            note = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                string state = item.UnsavedTab ? Loc.Get(K.RemoteChangesUnsavedTab) : StateOf(item.Status);
                if (item.NeedsDiff && !item.UnsavedTab && _remoteDiffSeen.ContainsKey(item.Status.Copy.LocalPath)) state += " · " + Loc.Get(K.RemoteChangesDiffSeen);
                rows.Add(((item.Selected ? "☑ " : "☐ ") + Path.GetFileName(item.Status.Copy.RemotePath), state + " · " + item.Status.Copy.RemotePath, i));
            }
            var chosen = await PickAsync(Loc.Get(K.RemoteChangesWatermark, ("name", connection.Name)), Loc.Get(K.RemoteChangesA11y), rows);
            if (!chosen.Chosen) return;
            switch (chosen.Value)
            {
                case Send:
                    if (selected == 0) { note = Loc.Get(K.RemoteChangesNothingSelected); continue; }
                    await SendChangeItemsAsync(connection, client, [.. items.Where(i => i.Selected)]);
                    return;
                case Diff:
                    await ShowChangeDifferencesAsync(client, items);
                    break;
                case Note:
                    break;
                default:
                    note = await ToggleChangeAsync(items[chosen.Value]);
                    break;
            }
        }
    }

    /// <summary>Ticks or unticks the file; the reason why it could not be ticked, else null (the list shows it on top, because a message of the status bar does not outlive the list).</summary>
    private async Task<string?> ToggleChangeAsync(ChangeItem item)
    {
        string name = Path.GetFileName(item.Status.Copy.RemotePath);
        if (item.Selected) { item.Selected = false; return null; }
        if (item.UnsavedTab) return Loc.Get(K.RemoteChangesUnsavedHint, ("name", name));
        if (item.NeedsDiff && !_remoteDiffSeen.ContainsKey(item.Status.Copy.LocalPath))
        {
            if (item.Status.Server == ServerState.Changed) return Loc.Get(K.RemoteChangesNeedDiff, ("name", name));
            var ask = new ChoiceRequest(Loc.Get(K.RemoteConflictTitle), Loc.Get(K.RemoteConflictDeleted, ("title", name)),
                [new DialogButton(Loc.Get(K.RemoteChangesRecreate)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return null;
            _remoteDiffSeen[item.Status.Copy.LocalPath] = RemoteRevision.None;
        }
        item.Selected = true;
        return null;
    }

    private async Task ShowChangeDifferencesAsync(IRemoteClient client, List<ChangeItem> items)
    {
        var pick = await PickAsync(Loc.Get(K.RemoteChangesDiffWatermark), Loc.Get(K.RemoteChangesA11y), [.. items.Select((item, i) => (Path.GetFileName(item.Status.Copy.RemotePath), (string?)item.Status.Copy.RemotePath, i))]);
        if (!pick.Chosen) return;
        var copy = items[pick.Value].Status.Copy;
        string name = Path.GetFileName(copy.RemotePath);
        try
        {
            var (revision, bytes) = await RemoteSync.ReadServerAsync(client, copy.RemotePath, CancellationToken.None);
            if (bytes is null) { SetStatusMessage(Loc.Get(K.RemoteBarServerMissing)); return; }
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).Contains((byte)0)) { SetStatusMessage(Loc.Get(K.RemoteChangesDiffBinary, ("name", name))); return; }
            using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            OpenCompare(new CompareSide { Title = Loc.Get(K.RemoteCompareServer, ("title", name)), Text = reader.ReadToEnd() }, new CompareSide { Title = name, FilePath = copy.LocalPath });
            _remoteDiffSeen[copy.LocalPath] = revision;
            SetStatusMessage(Loc.Get(K.RemoteChangesDiffShown, ("name", name)));
        }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); }
    }

    private async Task SendChangeItemsAsync(RemoteConnection connection, IRemoteClient client, List<ChangeItem> chosen)
    {
        using var cancel = new CancellationTokenSource();
        _remoteTransfer = cancel;
        var mode = _app.Workbench.Settings.GetString("remote.versionCheck") == "content" ? VersionCheck.Content : VersionCheck.Stat;
        int uploaded = 0, conflicts = 0, failed = 0;
        try
        {
            foreach (var item in chosen)
            {
                var copy = item.Status.Copy;
                string name = Path.GetFileName(copy.RemotePath);
                _remoteDiffSeen.TryGetValue(copy.LocalPath, out var expected);
                try
                {
                    var outcome = await RemoteSync.UploadAsync(client, _app.Remote.Copies, copy, mode, expected, TransferOptionsFor(connection, name, cancel), cancel.Token);
                    if (outcome.Status == UploadStatus.Conflict)
                    {
                        conflicts++;
                        RemoteLog(connection, Loc.Get(K.RemoteLogConflict, ("path", copy.RemotePath)));
                        _remoteDiffSeen.Remove(copy.LocalPath);
                        continue;
                    }
                    uploaded++;
                    _remoteDiffSeen.Remove(copy.LocalPath);
                    foreach (var open in _app.Windows.SelectMany(w => w.Documents).Where(d => d.RemoteCopy is { } c && string.Equals(c.LocalPath, copy.LocalPath, StringComparison.OrdinalIgnoreCase))) open.RemoteCopy = outcome.Copy;
                }
                catch (RemoteException e)
                {
                    failed++;
                    RemoteLog(connection, Loc.Get(K.RemoteUploadFailed, ("title", name), ("reason", e.Message)));
                }
            }
        }
        catch (OperationCanceledException) { RemoteLog(connection, Loc.Get(K.RemoteCancelled, ("title", connection.Name))); }
        finally { EndTransfer(cancel); }
        SetStatusMessage(Loc.Get(K.RemoteChangesDone, ("uploaded", uploaded), ("conflicts", conflicts), ("failed", failed)));
    }
}

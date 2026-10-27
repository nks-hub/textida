using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Remote files (P5.4): saved connections, opening a file as a local copy, the bar that says whether the copy matches the server, uploading with a version
/// check, and the list of changes that were saved locally and not sent. Passwords live in the credential store and are asked for only when a connection is created.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<OpenDocument, RemoteRevision> _acceptedServerVersion = [];

    /// <summary>Shows the identity of a server and asks whether to trust it; the library calls this from a background thread while it connects.</summary>
    private sealed class DialogTrust(MainWindow window) : IServerTrustPrompt
    {
        public async ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation) =>
            await Dispatcher.UIThread.InvokeAsync(() => window.ConfirmServerAsync(connection, presented, reason));
    }

    private async Task<bool> ConfirmServerAsync(RemoteConnection connection, ServerIdentity identity, TrustReason reason)
    {
        string text = reason switch
        {
            TrustReason.Changed => Loc.Get(K.RemoteTrustChanged, ("host", connection.Host)),
            TrustReason.Invalid => Loc.Get(K.RemoteTrustInvalid, ("host", connection.Host)),
            _ => Loc.Get(K.RemoteTrustFirst, ("host", connection.Host)),
        };
        string kind = identity.Kind == IdentityKind.SshHostKey ? Loc.Get(K.RemoteTrustKindSsh) : Loc.Get(K.RemoteTrustKindTls);
        var request = new ChoiceRequest(Loc.Get(K.RemoteTrustTitle), text,
            [new DialogButton(Loc.Get(K.RemoteTrustAccept)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)],
            [kind + ": " + identity.Detail, Loc.Get(K.RemoteTrustFingerprint, ("fingerprint", identity.Fingerprint))]);
        return await _app.Dialogs.ChooseAsync(this, request) == 0;
    }

    private void RemoteLog(RemoteConnection connection, string line) => _app.Output.Append(OutputChannel.Remote, connection.Name.Length > 0 ? connection.Name : connection.Host, line);

    private static string DescribeStep(TransferEvent e) => e.Step switch
    {
        TransferStep.RemovingLeftover => Loc.Get(K.RemoteLogLeftover, ("path", e.Path)),
        TransferStep.Uploading => Loc.Get(K.RemoteLogUploading, ("bytes", e.Bytes), ("path", e.Path)),
        TransferStep.Resuming => Loc.Get(K.RemoteLogResuming, ("bytes", e.Bytes), ("path", e.Path)),
        _ => Loc.Get(K.RemoteLogReplacing, ("path", e.Path)),
    };

    private static string ReasonOf(Exception e) => e is RemoteException r ? r.Message : e.Message;

    // ---- a small list helper: the palette list as an awaitable question ----

    private readonly record struct Pick<T>(bool Chosen, T? Value);

    private Task<Pick<T>> PickAsync<T>(string watermark, string name, IReadOnlyList<(string Title, string? Detail, T Value)> items, string emptyText = "")
    {
        var source = new TaskCompletionSource<Pick<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = items.Select(i => new QuickRow(i.Title, null, i.Detail, null, true, null, i.Detail is null ? i.Title : i.Title + ", " + i.Detail, () => source.TrySetResult(new Pick<T>(true, i.Value)))).ToList();
        ShowChoice(watermark, name, rows, () => source.TrySetResult(new Pick<T>(false, default)), emptyText);
        return source.Task;
    }

    // ---- connecting ----

    /// <summary>Connects and, when the user confirmed a new server identity, remembers it for the next time. Null when the connection could not be made (the status bar says why).</summary>
    private async Task<IRemoteClient?> ConnectAsync(RemoteConnection connection)
    {
        IRemoteClient? client = null;
        try
        {
            RemoteLog(connection, Loc.Get(K.RemoteLogConnecting, ("host", connection.Host), ("port", connection.EffectivePort), ("protocol", connection.Protocol)));
            client = _app.Remote.CreateClient(connection, new DialogTrust(this));
            await client.ConnectAsync(CancellationToken.None);
            if (client.ConfirmedIdentity is { } identity && identity != connection.TrustedIdentity)
                _app.Remote.Connections.Add(connection with { TrustedIdentity = identity }, null, _app.Remote.Secrets);
            RemoteLog(connection, Loc.Get(K.RemoteLogConnected));
            return client;
        }
        catch (Exception e) when (e is RemoteException or CredentialStoreUnavailableException or IOException)
        {
            if (client is not null) await client.DisposeAsync();
            RemoteLog(connection, Loc.Get(K.RemoteConnectFailed, ("name", connection.Name), ("reason", ReasonOf(e))));
            SetStatusMessage(Loc.Get(K.RemoteConnectFailed, ("name", connection.Name), ("reason", ReasonOf(e))));
            return null;
        }
    }

    // ---- connections ----

    internal CommandOutcome ShowConnections()
    {
        _ = ConnectionsAsync();
        return CommandOutcome.Handled;
    }

    private static string DescribeConnection(RemoteConnection c) => $"{c.Protocol.ToString().ToUpperInvariant()} · {c.UserName}@{c.Host}:{c.EffectivePort}";

    private async Task ConnectionsAsync()
    {
        var items = _app.Remote.Connections.Load().Select(c => (c.Name, (string?)DescribeConnection(c), (RemoteConnection?)c)).ToList();
        items.Add((Loc.Get(K.RemoteNewConnection), null, null));
        var pick = await PickAsync(Loc.Get(K.RemoteConnectionsWatermark), Loc.Get(K.RemoteConnectionsA11y), items);
        if (!pick.Chosen) return;
        if (pick.Value is null) await NewConnectionAsync();
        else await ConnectionActionsAsync(pick.Value);
    }

    private async Task NewConnectionAsync()
    {
        var dialogs = _app.Dialogs;
        string title = Loc.Get(K.RemoteNewTitle);
        string? name = (await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteNewName), ""))?.Trim();
        if (string.IsNullOrEmpty(name)) return;

        int protocol = await dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.RemoteNewProtocol),
            [new DialogButton("SFTP", IsDefault: true), new DialogButton(Loc.Get(K.RemoteProtocolFtpsExplicit)), new DialogButton(Loc.Get(K.RemoteProtocolFtpsImplicit)),
             new DialogButton(Loc.Get(K.RemoteProtocolFtpPlain)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (protocol is < 0 or > 3) return;
        var connection = new RemoteConnection
        {
            Name = name,
            Protocol = protocol switch { 0 => RemoteProtocol.Sftp, 3 => RemoteProtocol.Ftp, _ => RemoteProtocol.Ftps },
            FtpsMode = protocol == 2 ? FtpsMode.Implicit : FtpsMode.Explicit,
        };
        if (connection.Protocol == RemoteProtocol.Ftp)
        {
            // plain FTP sends the password and the files in the clear; it is only used after this explicit answer
            int allow = await dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RemotePlainTitle), Loc.Get(K.RemotePlainWarning),
                [new DialogButton(Loc.Get(K.RemotePlainAllow)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]));
            if (allow != 0) return;
            connection = connection with { AllowPlainFtp = true };
        }

        string? host = (await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteNewHost), ""))?.Trim();
        if (string.IsNullOrEmpty(host)) return;
        connection = connection with { Host = host };
        string? portText = await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteNewPort, ("port", connection.EffectivePort)), "");
        if (portText is null) return;
        if (portText.Trim().Length > 0)
        {
            if (!int.TryParse(portText.Trim(), out int port) || port is < 1 or > 65535) { SetStatusMessage(Loc.Get(K.RemoteBadPort)); return; }
            connection = connection with { Port = port };
        }
        string? user = (await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteNewUser), ""))?.Trim();
        if (user is null) return;
        connection = connection with { UserName = user };

        if (connection.Protocol == RemoteProtocol.Sftp)
        {
            string? keyPath = await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteNewKeyFile), "");
            if (keyPath is null) return;
            if (keyPath.Trim().Length > 0) connection = connection with { Auth = SftpAuth.PrivateKey, PrivateKeyPath = keyPath.Trim() };
        }
        string? secret = await dialogs.PromptSecretAsync(this, title, Loc.Get(connection.Auth == SftpAuth.PrivateKey ? K.RemoteNewPassphrase : K.RemoteNewPassword));
        if (secret is null) return;
        if (secret.Length == 0 && connection.Auth == SftpAuth.PrivateKey) secret = null;

        try
        {
            connection.Validate();
            _app.Remote.Connections.Add(connection, secret, _app.Remote.Secrets);
        }
        catch (Exception e) when (e is RemoteException or CredentialStoreUnavailableException or IOException)
        {
            SetStatusMessage(Loc.Get(K.RemoteSaveFailed, ("reason", ReasonOf(e))));
            return;
        }
        SetStatusMessage(Loc.Get(K.RemoteSaved, ("name", connection.Name)));
    }

    private async Task ConnectionActionsAsync(RemoteConnection connection)
    {
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(connection.Name, DescribeConnection(connection),
            [new DialogButton(Loc.Get(K.RemoteActionOpen), IsDefault: true), new DialogButton(Loc.Get(K.RemoteActionPassword)), new DialogButton(Loc.Get(K.RemoteActionDelete)),
             new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (answer == 0) await OpenRemoteFileAsync(connection);
        else if (answer == 1)
        {
            string? secret = await _app.Dialogs.PromptSecretAsync(this, connection.Name, Loc.Get(connection.Auth == SftpAuth.PrivateKey ? K.RemoteNewPassphrase : K.RemoteNewPassword));
            if (secret is null) return;
            try { _app.Remote.Secrets.Set(connection.SecretKey, secret); }
            catch (Exception e) when (e is CredentialStoreUnavailableException or IOException) { SetStatusMessage(Loc.Get(K.RemoteSaveFailed, ("reason", e.Message))); return; }
            SetStatusMessage(Loc.Get(K.RemotePasswordSaved, ("name", connection.Name)));
        }
        else if (answer == 2)
        {
            int sure = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RemoteDeleteTitle), Loc.Get(K.RemoteDeleteMessage, ("name", connection.Name)),
                [new DialogButton(Loc.Get(K.RemoteActionDelete)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]));
            if (sure != 0) return;
            try { _app.Remote.Connections.Remove(connection, _app.Remote.Secrets); }
            catch (Exception e) when (e is CredentialStoreUnavailableException or IOException) { SetStatusMessage(Loc.Get(K.RemoteSaveFailed, ("reason", e.Message))); return; }
            SetStatusMessage(Loc.Get(K.RemoteDeleted, ("name", connection.Name)));
        }
    }

    // ---- backup of the connections ----

    internal CommandOutcome ExportConnections()
    {
        _ = ExportConnectionsAsync();
        return CommandOutcome.Handled;
    }

    private async Task ExportConnectionsAsync()
    {
        var list = _app.Remote.Connections.Load();
        if (list.Count == 0) { SetStatusMessage(Loc.Get(K.RemoteNoConnections)); return; }
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.RemoteExportTitle), "textida-connections.json");
        if (path is null) return;
        try { File.WriteAllText(path, ConnectionStore.Export(list)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.RemoteSaveFailed, ("reason", e.Message))); return; }
        SetStatusMessage(Loc.Get(K.RemoteExported, ("path", path)));
    }

    internal CommandOutcome ImportConnections()
    {
        _ = ImportConnectionsAsync();
        return CommandOutcome.Handled;
    }

    private async Task ImportConnectionsAsync()
    {
        var picked = await _app.Dialogs.PickFilesAsync(this);
        if (picked.Count == 0) return;
        try
        {
            foreach (var connection in ConnectionStore.Import(File.ReadAllText(picked[0])))
            {
                connection.Validate();
                _app.Remote.Connections.Add(connection, null, _app.Remote.Secrets); // a backup holds no password: the connection asks for it when it is used
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or RemoteException or IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.RemoteImportFailed, ("reason", ReasonOf(e))));
            return;
        }
        SetStatusMessage(Loc.Get(K.RemoteImported, ("path", picked[0])));
    }

    // ---- backup of the whole configuration ----

    /// <summary>The passwords the credential store keeps for the connections and the proxy; the backup leaves out any value that holds one.</summary>
    private List<string> KnownSecrets()
    {
        var secrets = new List<string>();
        var store = _app.Remote.Secrets;
        try
        {
            foreach (var c in _app.Remote.Connections.Load()) secrets.AddRange(new[] { store.Get(c.SecretKey), store.Get(c.ProxySecretKey) }.OfType<string>());
            if (store.Get(NetworkPolicy.SecretKey) is { } proxy) secrets.Add(proxy);
        }
        catch (CredentialStoreUnavailableException) { /* no store: no password was saved anywhere */ }
        return secrets;
    }

    internal CommandOutcome BackupConfiguration()
    {
        _ = BackupConfigurationAsync();
        return CommandOutcome.Handled;
    }

    private async Task BackupConfigurationAsync()
    {
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.ConfigBackupTitle), ConfigBackup.SuggestedName(_app.Clock()));
        if (path is null) return;
        try
        {
            var result = ConfigBackup.Export(_app.ProfileDirectory, path, KnownSecrets());
            SetStatusMessage(Loc.Get(result.LeftOut > 0 ? K.ConfigBackupDoneLeftOut : K.ConfigBackupDone, ("files", result.Files), ("leftOut", result.LeftOut), ("path", path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.ConfigBackupFailed, ("reason", e.Message))); }
    }

    internal CommandOutcome RestoreConfiguration()
    {
        _ = RestoreConfigurationAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Checks the backup and stages it; the files come back at the next start, so nothing running writes its old state over them.</summary>
    private async Task RestoreConfigurationAsync()
    {
        var picked = await _app.Dialogs.PickFilesAsync(this);
        if (picked.Count == 0) return;
        int files;
        try { files = ConfigBackup.Inspect(picked[0]); }
        catch (Exception e) when (e is ConfigBackupException or IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.ConfigRestoreFailed, ("reason", e.Message))); return; }

        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.ConfigRestoreTitle), Loc.Get(K.ConfigRestoreMessage, ("files", files)),
            [new DialogButton(Loc.Get(K.ConfigRestoreQuit), IsDefault: true), new DialogButton(Loc.Get(K.ConfigRestoreLater)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (answer is not (0 or 1)) return;
        try { ConfigBackup.Stage(_app.ProfileDirectory, picked[0]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { SetStatusMessage(Loc.Get(K.ConfigRestoreFailed, ("reason", e.Message))); return; }
        SetStatusMessage(Loc.Get(K.ConfigRestoreStaged));
        if (answer == 0) _app.Exit();
    }

    /// <summary>After a start that put a staged backup back: how many files came back and where the configuration from before is kept, or why it failed.</summary>
    internal void ShowConfigRestore()
    {
        if (_app.ConfigRestore is not { } r) return;
        string text = (r.Problem, r.SavedBefore) switch
        {
            (null, _) => Loc.Get(K.ConfigRestoreApplied, ("files", r.Files), ("path", r.SavedBefore ?? "")),
            (_, null) => Loc.Get(K.ConfigRestoreApplyFailed, ("reason", r.Problem)),
            _ => Loc.Get(K.ConfigRestoreApplyPartial, ("reason", r.Problem), ("path", r.SavedBefore ?? "")),
        };
        ShowNotice("configRestore", text, warning: r.Problem is not null);
    }

    // ---- opening ----

    internal CommandOutcome OpenRemote()
    {
        _ = PickConnectionAndOpenAsync();
        return CommandOutcome.Handled;
    }

    private async Task PickConnectionAndOpenAsync()
    {
        var list = _app.Remote.Connections.Load();
        if (list.Count == 0) { SetStatusMessage(Loc.Get(K.RemoteNoConnections)); return; }
        var pick = await PickAsync(Loc.Get(K.RemoteConnectionsWatermark), Loc.Get(K.RemoteConnectionsA11y), [.. list.Select(c => (c.Name, (string?)DescribeConnection(c), c))]);
        if (pick.Chosen) await OpenRemoteFileAsync(pick.Value!);
    }

    private async Task OpenRemoteFileAsync(RemoteConnection connection)
    {
        // the connection record may carry a newer trusted identity than the list the user picked from
        connection = _app.Remote.Connections.Load().FirstOrDefault(c => c.Id == connection.Id) ?? connection;
        string? path = await _app.Dialogs.PromptAsync(this, Loc.Get(K.RemoteOpenTitle), Loc.Get(K.RemoteOpenPrompt), "/");
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim();
        await using var client = await ConnectAsync(connection);
        if (client is null) return;
        try
        {
            var stat = await client.StatAsync(path, CancellationToken.None);
            if (stat is null) { SetStatusMessage(Loc.Get(K.RemoteNotFound, ("path", path))); return; }
            string? file = stat.Value.IsDirectory ? await BrowseAsync(client, path) : path;
            if (file is null) return;
            RemoteLog(connection, Loc.Get(K.RemoteLogOpening, ("path", file)));
            var copy = await _app.Remote.Copies.OpenAsync(client, file, CancellationToken.None);
            var tab = OpenFile(copy.LocalPath, null);
            tab.Doc.RemoteCopy = copy;
            RemoteLog(connection, Loc.Get(K.RemoteLogOpened, ("path", file), ("local", copy.LocalPath)));
            SetStatusMessage(Loc.Get(K.RemoteOpened, ("path", file), ("name", connection.Name)));
            await ShowRemoteBarAsync(tab, client);
        }
        catch (Exception e) when (e is RemoteException or IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.RemoteOpenFailed, ("path", path), ("reason", ReasonOf(e))));
        }
    }

    private static string ParentOf(string dir) => dir.TrimEnd('/') is { Length: > 0 } t && t.LastIndexOf('/') is var i and > 0 ? t[..i] : "/";

    private static string Join(string dir, string name) => dir.TrimEnd('/') + "/" + name;

    /// <summary>Lists a folder as a choice list; a folder row goes into it, a file row ends the browsing with its path. Null when the list was dismissed.</summary>
    private async Task<string?> BrowseAsync(IRemoteClient client, string directory)
    {
        while (true)
        {
            var entries = await client.ListAsync(directory, CancellationToken.None);
            var items = new List<(string Title, string? Detail, RemoteEntry? Value)>();
            if (directory != "/") items.Add((Loc.Get(K.RemoteBrowseUp), ParentOf(directory), new RemoteEntry("..", ParentOf(directory), new RemoteStat(0, null, true))));
            items.AddRange(entries.OrderByDescending(e => e.Stat.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Select(e => (e.Stat.IsDirectory ? e.Name + "/" : e.Name, e.Stat.IsDirectory ? null : (string?)OpenDocument.FormatSize(e.Stat.Size), (RemoteEntry?)e)));
            var pick = await PickAsync(Loc.Get(K.RemoteBrowseWatermark, ("path", directory)), Loc.Get(K.RemoteBrowseA11y), items, Loc.Get(K.RemoteBrowseEmpty));
            if (!pick.Chosen) return null;
            var entry = pick.Value!;
            if (entry.Stat.IsDirectory) directory = entry.Name == ".." ? entry.FullPath : Join(directory, entry.Name);
            else return Join(directory, entry.Name);
        }
    }

    // ---- the state of a remote tab ----

    private RemoteConnection? ConnectionOf(WorkingCopy copy) => _app.Remote.Connections.Load().FirstOrDefault(c => c.Id == copy.ConnectionId);

    private void ClearRemoteBar(DocTab tab) => tab.ShowBar(null);

    /// <summary>Shows what the local state alone tells: the copy has unsent changes, or it matches the revision it was made from. No connection is made.</summary>
    private void ShowRemoteBarOffline(DocTab tab)
    {
        if (tab.Doc.RemoteCopy is not { } copy) return;
        var local = _app.Remote.Copies.LocalStateOf(copy);
        var actions = new List<(string, Action)> { (Loc.Get(K.RemoteBarCheck), () => _ = CheckRemoteAsync(tab)) };
        if (local == LocalState.Unsent) actions.Insert(0, (Loc.Get(K.RemoteBarUpload), () => _ = UploadRemoteAsync(tab)));
        tab.ShowBar(new InfoBar(Loc.Get(local == LocalState.Unsent ? K.RemoteBarUnsent : K.RemoteBarOffline), actions, warning: local == LocalState.Unsent, onClose: () => ClearRemoteBar(tab)));
    }

    private async Task ShowRemoteBarAsync(DocTab tab, IRemoteClient client)
    {
        if (tab.Doc.RemoteCopy is not { } copy) return;
        var status = await _app.Remote.Copies.CheckAsync(client, copy, CancellationToken.None);
        void Diff() => _ = ShowServerDifferencesAsync(tab);
        (string Message, List<(string, Action)> Actions, bool Warning) bar = (status.Local, status.Server) switch
        {
            (LocalState.InSync, ServerState.InSync) => (Loc.Get(K.RemoteBarInSync), [(Loc.Get(K.RemoteBarCheck), () => _ = CheckRemoteAsync(tab))], false),
            (LocalState.Unsent, ServerState.InSync) => (Loc.Get(K.RemoteBarUnsent), [(Loc.Get(K.RemoteBarUpload), () => _ = UploadRemoteAsync(tab))], true),
            (LocalState.InSync, ServerState.Changed) => (Loc.Get(K.RemoteBarServerChanged), [(Loc.Get(K.RemoteBarDifferences), Diff), (Loc.Get(K.RemoteBarReload), () => _ = ReloadFromServerAsync(tab))], true),
            (_, ServerState.Missing) => (Loc.Get(K.RemoteBarServerMissing), [(Loc.Get(K.RemoteBarUpload), () => _ = UploadRemoteAsync(tab))], true),
            _ => (Loc.Get(K.RemoteBarBoth), [(Loc.Get(K.RemoteBarDifferences), Diff)], true),
        };
        tab.ShowBar(new InfoBar(bar.Message, bar.Actions, bar.Warning, () => ClearRemoteBar(tab)));
    }

    // ---- commands on the active tab ----

    private DocTab? RemoteTarget()
    {
        if (Active is { } tab && tab.Doc.RemoteCopy is not null) return tab;
        SetStatusMessage(Loc.Get(K.RemoteNotRemote));
        return null;
    }

    internal CommandOutcome CheckRemote()
    {
        if (RemoteTarget() is { } tab) _ = CheckRemoteAsync(tab);
        return CommandOutcome.Handled;
    }

    private async Task CheckRemoteAsync(DocTab tab)
    {
        if (tab.Doc.RemoteCopy is not { } copy) return;
        if (ConnectionOf(copy) is not { } connection) { SetStatusMessage(Loc.Get(K.RemoteConnectionGone)); return; }
        await using var client = await ConnectAsync(connection);
        if (client is null) { ShowRemoteBarOffline(tab); return; }
        try { await ShowRemoteBarAsync(tab, client); }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); }
    }

    private CancellationTokenSource? _remoteTransfer;

    internal CommandOutcome CancelRemoteTransfer()
    {
        if (_remoteTransfer is { } running) running.Cancel(); else SetStatusMessage(Loc.Get(K.RemoteNoTransfer));
        return CommandOutcome.Handled;
    }

    internal CommandOutcome UploadRemote()
    {
        if (RemoteTarget() is { } tab) _ = UploadRemoteAsync(tab);
        return CommandOutcome.Handled;
    }

    /// <param name="saveFirst">False for the upload that follows a save of the save actions: the file on disk is then exactly the saved revision, and a later change of the document must not be saved and sent with it.</param>
    /// <param name="interactive">False after an autosave: a version conflict is not asked about in a dialog, the bar of the tab says that the server changed.</param>
    /// <returns>True when the server holds the saved file now (uploaded, or it was already the same).</returns>
    private async Task<bool> UploadRemoteAsync(DocTab tab, bool saveFirst = true, bool interactive = true)
    {
        var od = tab.Doc;
        if (od.RemoteCopy is not { } copy) return false;
        if (saveFirst && od.Doc.IsModified && !await SaveAsync(tab)) return false; // the copy on disk is what gets sent
        if (ConnectionOf(copy) is not { } connection) { SetStatusMessage(Loc.Get(K.RemoteConnectionGone)); return false; }
        var client = await ConnectAsync(connection);
        if (client is null) { ShowRemoteBarOffline(tab); return false; }
        using var cancel = new CancellationTokenSource();
        _remoteTransfer = cancel;

        var mode = _app.Workbench.Settings.GetString("remote.versionCheck") == "content" ? VersionCheck.Content : VersionCheck.Stat;
        _acceptedServerVersion.TryGetValue(od, out var accepted);
        try
        {
            UploadOutcome outcome;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    outcome = await RemoteSync.UploadAsync(client, _app.Remote.Copies, copy, mode, accepted, new TransferOptions { Log = e => RemoteLog(connection, DescribeStep(e)) }, cancel.Token);
                    break;
                }
                catch (RemoteException e) when (e.Error == RemoteError.ConnectionLost && attempt <= PluginLimitsPlaceholders.RemoteRetryCount)
                {
                    // A new call, so the server version is checked again before anything is written; a partial temporary file is continued.
                    int delay = PluginLimitsPlaceholders.RemoteRetryBaseDelayMilliseconds << (attempt - 1);
                    RemoteLog(connection, Loc.Get(K.RemoteLogRetrying, ("attempt", attempt), ("max", PluginLimitsPlaceholders.RemoteRetryCount), ("seconds", delay / 1000.0), ("reason", e.Message)));
                    await client.DisposeAsync();
                    await Task.Delay(delay, cancel.Token);
                    client = await ConnectAsync(connection);
                    if (client is null) { ShowRemoteBarOffline(tab); return false; }
                }
            }
            switch (outcome.Status)
            {
                case UploadStatus.Uploaded:
                    _acceptedServerVersion.Remove(od);
                    od.RemoteCopy = outcome.Copy;
                    SetStatusMessage(Loc.Get(K.RemoteUploaded, ("title", od.Title), ("name", connection.Name)));
                    await ShowRemoteBarAsync(tab, client);
                    return true;
                case UploadStatus.NothingToSend:
                    SetStatusMessage(Loc.Get(K.RemoteNothingToSend, ("title", od.Title)));
                    await ShowRemoteBarAsync(tab, client);
                    return true;
                default:
                    if (interactive) await ResolveUploadConflictAsync(tab, connection, client, outcome);
                    else
                    {
                        RemoteLog(connection, Loc.Get(K.RemoteLogConflict, ("path", outcome.Copy.RemotePath)));
                        SetStatusMessage(Loc.Get(K.SaveActionsUploadConflict, ("title", od.Title)));
                        await ShowRemoteBarAsync(tab, client);
                    }
                    return false;
            }
        }
        catch (OperationCanceledException)
        {
            RemoteLog(connection, Loc.Get(K.RemoteCancelled, ("title", od.Title)));
            SetStatusMessage(Loc.Get(K.RemoteCancelled, ("title", od.Title)));
            return false;
        }
        catch (RemoteException e)
        {
            RemoteLog(connection, Loc.Get(K.RemoteUploadFailed, ("title", od.Title), ("reason", e.Message)));
            SetStatusMessage(Loc.Get(K.RemoteUploadFailed, ("title", od.Title), ("reason", e.Message)));
            ShowRemoteBarOffline(tab);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_remoteTransfer, cancel)) _remoteTransfer = null;
            if (client is not null) await client.DisposeAsync();
        }
    }

    /// <summary>Nothing was written. The user looks at the differences, uploads anyway against exactly the version that was found, or leaves it.</summary>
    private async Task ResolveUploadConflictAsync(DocTab tab, RemoteConnection connection, IRemoteClient client, UploadOutcome outcome)
    {
        var od = tab.Doc;
        bool deleted = outcome.ServerContent is null;
        RemoteLog(connection, Loc.Get(K.RemoteLogConflict, ("path", outcome.Copy.RemotePath)));
        // answers in the order of the buttons: 0 differences (only when there is a server version to show), 1 upload anyway, 2 leave it
        var kinds = new List<int>();
        var buttons = new List<DialogButton>();
        if (!deleted) { buttons.Add(new DialogButton(Loc.Get(K.RemoteConflictDifferences), IsDefault: true)); kinds.Add(0); }
        buttons.Add(new DialogButton(Loc.Get(K.RemoteConflictUploadAnyway))); kinds.Add(1);
        buttons.Add(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true, IsDefault: deleted)); kinds.Add(2);
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RemoteConflictTitle),
            Loc.Get(deleted ? K.RemoteConflictDeleted : K.RemoteConflictMessage, ("title", od.Title)), buttons));
        switch (answer < 0 || answer >= kinds.Count ? 2 : kinds[answer])
        {
            case 0:
                _acceptedServerVersion[od] = outcome.Server!;
                OpenServerCompare(tab, outcome.Copy, outcome.ServerContent!);
                SetStatusMessage(Loc.Get(K.RemoteConflictShown));
                break;
            case 1:
                _acceptedServerVersion[od] = outcome.Server ?? RemoteRevision.None;
                await UploadRemoteAsync(tab);
                break;
            default:
                ShowRemoteBarOffline(tab);
                break;
        }
    }

    private void OpenServerCompare(DocTab tab, WorkingCopy copy, byte[] serverBytes)
    {
        string text = tab.Doc.Doc.Codec.GetString(serverBytes).TrimStart('﻿');
        OpenCompare(new CompareSide { Title = Loc.Get(K.RemoteCompareServer, ("title", Path.GetFileName(copy.RemotePath))), Text = text }, DocumentSide(tab));
    }

    private async Task ShowServerDifferencesAsync(DocTab tab)
    {
        if (tab.Doc.RemoteCopy is not { } copy) return;
        if (ConnectionOf(copy) is not { } connection) { SetStatusMessage(Loc.Get(K.RemoteConnectionGone)); return; }
        await using var client = await ConnectAsync(connection);
        if (client is null) return;
        try
        {
            var (revision, bytes) = await RemoteSync.ReadServerAsync(client, copy.RemotePath, CancellationToken.None);
            if (bytes is null) { SetStatusMessage(Loc.Get(K.RemoteBarServerMissing)); return; }
            _acceptedServerVersion[tab.Doc] = revision;
            OpenServerCompare(tab, copy, bytes);
        }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); }
    }

    private async Task ReloadFromServerAsync(DocTab tab)
    {
        if (tab.Doc.RemoteCopy is not { } copy || _app.Remote.Copies.LocalStateOf(copy) != LocalState.InSync || tab.Doc.Doc.IsModified) return; // never discards local work
        if (ConnectionOf(copy) is not { } connection) { SetStatusMessage(Loc.Get(K.RemoteConnectionGone)); return; }
        await using var client = await ConnectAsync(connection);
        if (client is null) return;
        try
        {
            tab.Doc.RemoteCopy = await _app.Remote.Copies.DownloadAsync(client, copy.RemotePath, CancellationToken.None);
            await ReloadAsync(tab);
            await ShowRemoteBarAsync(tab, client);
        }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); }
    }

    // ---- unsent changes ----

    internal CommandOutcome ShowUnsent()
    {
        var unsent = _app.Remote.Copies.Unsent();
        var connections = _app.Remote.Connections.Load();
        var rows = unsent.Select(c => new QuickRow(Path.GetFileName(c.RemotePath), null,
            (connections.FirstOrDefault(x => x.Id == c.ConnectionId)?.Name ?? Loc.Get(K.RemoteConnectionGone)) + " · " + c.RemotePath, null, true, null, c.RemotePath,
            () => OpenCopy(c))).ToList();
        ShowChoice(Loc.Get(K.RemoteUnsentWatermark), Loc.Get(K.RemoteUnsentA11y), rows, emptyText: Loc.Get(K.RemoteUnsentEmpty));
        return CommandOutcome.Handled;
    }

    private DocTab? OpenCopy(WorkingCopy copy)
    {
        if (!File.Exists(copy.LocalPath)) { SetStatusMessage(Loc.Get(K.RemoteCopyMissing, ("path", copy.RemotePath))); return null; }
        var tab = OpenFile(copy.LocalPath, null);
        tab.Doc.RemoteCopy ??= copy;
        ShowRemoteBarOffline(tab);
        return tab;
    }

    // ---- the cache paired with the server ----

    internal CommandOutcome PairCache()
    {
        _ = PairCacheAsync();
        return CommandOutcome.Handled;
    }

    private static string StateOf(CopyStatus s) => (s.Local, s.Server) switch
    {
        (LocalState.Missing, _) => Loc.Get(K.RemoteCopyMissing, ("path", s.Copy.RemotePath)),
        (_, ServerState.Missing) => Loc.Get(K.RemoteBarServerMissing),
        (LocalState.Unsent, ServerState.Changed) => Loc.Get(K.RemoteBarBoth),
        (_, ServerState.Changed) => Loc.Get(K.RemoteBarServerChanged),
        (LocalState.Unsent, _) => Loc.Get(K.RemoteBarUnsent),
        _ => Loc.Get(K.RemoteBarInSync),
    };

    /// <summary>
    /// After a restart: pairs every local copy of one connection with the server by the revision stored with it and lists each with its state. Nothing is
    /// downloaded or overwritten; a row opens the copy and checks it once more.
    /// </summary>
    private async Task PairCacheAsync()
    {
        var cached = _app.Remote.Copies.All().Select(c => c.ConnectionId).ToHashSet();
        var list = _app.Remote.Connections.Load().Where(c => cached.Contains(c.Id)).ToList();
        if (list.Count == 0) { SetStatusMessage(Loc.Get(K.RemoteCacheEmpty)); return; }
        var pick = await PickAsync(Loc.Get(K.RemoteConnectionsWatermark), Loc.Get(K.RemoteConnectionsA11y), [.. list.Select(c => (c.Name, (string?)DescribeConnection(c), c))]);
        if (!pick.Chosen) return;
        var connection = pick.Value!;
        IReadOnlyList<CopyStatus> paired;
        await using (var client = await ConnectAsync(connection))
        {
            if (client is null) return;
            try { paired = await _app.Remote.Copies.PairAsync(client, CancellationToken.None); }
            catch (RemoteException e) { SetStatusMessage(Loc.Get(K.RemoteCheckFailed, ("reason", e.Message))); return; }
        }
        var rows = paired.OrderBy(s => s.Copy.RemotePath, StringComparer.Ordinal).Select(s =>
        {
            string name = Path.GetFileName(s.Copy.RemotePath), state = StateOf(s);
            return new QuickRow(name, null, state + " · " + s.Copy.RemotePath, null, true, null, name + ", " + state, () => { if (OpenCopy(s.Copy) is { } tab) _ = CheckRemoteAsync(tab); });
        }).ToList();
        ShowChoice(Loc.Get(K.RemoteCacheWatermark, ("name", connection.Name)), Loc.Get(K.RemoteCacheA11y), rows, emptyText: Loc.Get(K.RemoteCacheEmpty));
    }

    // ---- proxy ----

    internal CommandOutcome SetNetworkProxy()
    {
        _ = NetworkProxyAsync();
        return CommandOutcome.Handled;
    }

    private async Task NetworkProxyAsync()
    {
        var dialogs = _app.Dialogs;
        string title = Loc.Get(K.RemoteProxyTitle);
        int type = await dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.RemoteProxyType),
            [new DialogButton(Loc.Get(K.RemoteProxyNone)), new DialogButton("HTTP"), new DialogButton("SOCKS4"), new DialogButton("SOCKS5", IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        if (type is < 0 or > 3) return;
        var kind = type switch { 1 => ProxyType.Http, 2 => ProxyType.Socks4, 3 => ProxyType.Socks5, _ => ProxyType.None };
        var proxy = new ProxySettings();
        string? password = null;
        if (kind != ProxyType.None)
        {
            string? host = (await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteProxyHost), ""))?.Trim();
            if (string.IsNullOrEmpty(host)) return;
            string? portText = await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteProxyPort), "");
            if (!int.TryParse(portText?.Trim(), out int port) || port is < 1 or > 65535) { SetStatusMessage(Loc.Get(K.RemoteBadPort)); return; }
            string? user = (await dialogs.PromptAsync(this, title, Loc.Get(K.RemoteProxyUser), ""))?.Trim();
            if (user is null) return;
            if (user.Length > 0) password = await dialogs.PromptSecretAsync(this, title, Loc.Get(K.RemoteProxyPassword));
            proxy = new ProxySettings(kind, host, port, user.Length > 0 ? user : null);
        }
        try { NetworkPolicy.Save(_app.Remote.NetworkPolicyPath, proxy, password, _app.Remote.Secrets); }
        catch (Exception e) when (e is RemoteException or CredentialStoreUnavailableException or IOException)
        {
            SetStatusMessage(Loc.Get(K.RemoteSaveFailed, ("reason", ReasonOf(e))));
            return;
        }
        SetStatusMessage(Loc.Get(kind == ProxyType.None ? K.RemoteProxyOff : K.RemoteProxySaved));
    }
}

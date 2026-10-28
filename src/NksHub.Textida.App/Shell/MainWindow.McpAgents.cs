using System.Globalization;
using System.Text;
using System.Text.Json;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.McpClient;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Remote;
using NksHub.Textida.Rpc;
using NksHub.Textida.Workbench.Commands;
using Client = NksHub.Textida.McpClient.McpClient;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, MCP Servers of Agents: one list of the MCP servers that Claude Code, Cursor, Codex and OpenCode are set up with, read from their files (never written). A server can be
/// tried (its tools are counted), connected to (its tools and resources are listed, a resource is read and a tool is called after the user has seen the call and agreed) or converted
/// into a snippet for another agent, shown in a new tab. A server started by a command is started only after the user has seen the exact command with its variables hidden; one reached
/// by an address is contacted only after the user has seen the address. A key a configuration refers to as <c>${NAME}</c> comes from the environment or the credential store; one that is
/// in neither is asked for once and goes to the credential store, never to a file.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Prefix of the Credential Manager entries that hold the value of a variable a configuration refers to as <c>${NAME}</c>.</summary>
    internal const string McpEnvCredentialPrefix = "textida:mcp:env:";

    internal CommandOutcome ShowAgentMcpServers()
    {
        McpTask = ChooseAgentMcpServersAsync();
        return CommandOutcome.Handled;
    }

    internal static string AgentName(McpAgent agent) => agent switch
    {
        McpAgent.ClaudeCode => "Claude Code",
        McpAgent.Cursor => "Cursor",
        McpAgent.Codex => "Codex",
        _ => "OpenCode",
    };

    private async Task ChooseAgentMcpServersAsync()
    {
        var folders = Active?.Path is { } path && Path.GetDirectoryName(path) is { } directory ? McpConfig.WithParents(directory).ToList() : [];
        var home = _app.AgentConfigHome;
        var scan = await Task.Run(() => McpConfig.Scan(home, folders));
        if (scan.Problems.Count > 0)
            SetStatusMessage(Loc.Get(K.McpAgentsProblems, ("files", string.Join(", ", scan.Problems.Select(p => Path.GetFileName(p.File)).Distinct()))));
        else if (scan.Servers.Count == 0)
            SetStatusMessage(Loc.Get(K.McpAgentsNone));
        if (scan.Servers.Count == 0) return;

        var items = scan.Servers.Select((s, i) => new PickItem(i.ToString(), s.Name, $"{AgentName(s.Agent)}, {Path.GetFileName(s.SourceFile)}: {s.Summary}")).ToList();
        while (true)
        {
            var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.McpAgentsTitle), Loc.Get(K.McpAgentsWatermark), items), CancellationToken.None);
            if (chosen is null || !int.TryParse(chosen, out int index)) return;
            if (await ChooseAgentActionAsync(scan.Servers[index])) return;
        }
    }

    /// <summary>Asks what to do with one entry; true when the list should not come back (a snippet was opened in a tab).</summary>
    private async Task<bool> ChooseAgentActionAsync(McpServerEntry entry)
    {
        var items = new List<PickItem> { new("try", Loc.Get(K.McpAgentsTry), null), new("connect", Loc.Get(K.McpAgentsConnect), null) };
        foreach (var agent in Enum.GetValues<McpAgent>().Where(a => a != entry.Agent))
            items.Add(new PickItem(agent.ToString(), Loc.Get(K.McpAgentsConvert, ("agent", AgentName(agent))), McpConfig.FileHint(agent)));
        var chosen = await PluginPickAsync(new QuickPickRequest(entry.Name, Loc.Get(K.McpAgentsActionWatermark, ("name", entry.Name)), items), CancellationToken.None);
        if (chosen is null) return false;
        if (chosen == "try")
        {
            await TryAgentMcpServerAsync(entry);
            return false;
        }
        if (chosen == "connect")
        {
            await ConnectAgentMcpServerAsync(entry);
            return false;
        }
        return ConvertAgentMcpServer(entry, Enum.Parse<McpAgent>(chosen));
    }

    // ---- values of variables ----

    private string? McpVariable(string name)
    {
        if (Environment.GetEnvironmentVariable(name) is { } value) return value;
        try { return _app.Credentials.Get(McpEnvCredentialPrefix + name); }
        catch (Exception e) when (e is IOException or CredentialStoreUnavailableException) { return null; }
    }

    /// <summary>
    /// The values of the variables the entry refers to, from the environment and the credential store. For each one that neither knows the user is asked (the typing is hidden) and the answer
    /// goes into the credential store only; when the store is unavailable it is used for this connection and nowhere else. Null when the user gave no value.
    /// </summary>
    private async Task<Func<string, string?>?> ResolveMcpVariablesAsync(McpServerEntry entry)
    {
        var typed = new Dictionary<string, string>();
        string? Lookup(string name) => typed.TryGetValue(name, out var value) ? value : McpVariable(name);
        while (McpConfig.FirstMissingVariable(entry, Lookup) is { } name)
        {
            var value = await _app.Dialogs.PromptSecretAsync(this, Loc.Get(K.McpClientAskTitle, ("name", name)), Loc.Get(K.McpClientAskLabel, ("server", entry.Name), ("name", name)));
            if (string.IsNullOrEmpty(value))
            {
                SetStatusMessage(Loc.Get(K.McpClientAskNone, ("name", name)));
                return null;
            }
            typed[name] = value;
            try { _app.Credentials.Set(McpEnvCredentialPrefix + name, value); }
            catch (Exception e) when (e is IOException or CredentialStoreUnavailableException) { SetStatusMessage(Loc.Get(K.McpClientNotSaved, ("name", name), ("reason", e.Message))); }
        }
        return Lookup;
    }

    /// <summary>
    /// Forgets the values the credential store holds for the variables in the address and the headers of the entry, after the server refused the key. A value that the environment
    /// provides is not ours to drop. Returns the names of the variables dropped.
    /// </summary>
    private List<string> DropStoredMcpKeys(McpServerEntry entry)
    {
        var names = new List<string>();
        foreach (var text in entry.Headers.Values.Append(entry.Url ?? ""))
            McpConfig.Expand(text, name => { names.Add(name); return ""; });
        var dropped = new List<string>();
        foreach (var name in names.Distinct())
        {
            if (Environment.GetEnvironmentVariable(name) is not null) continue;
            try
            {
                if (_app.Credentials.Delete(McpEnvCredentialPrefix + name)) dropped.Add(name);
            }
            catch (Exception e) when (e is IOException or CredentialStoreUnavailableException) { /* nothing stored that we could drop */ }
        }
        return dropped;
    }

    // ---- connecting ----

    /// <summary>What a connection will do, built from the entry: shown to the user before anything starts, used to start it, and the way to hide its secrets in a message.</summary>
    private sealed record McpTarget(McpLaunch? Launch, McpRemote? Remote, IReadOnlyList<string> Details, string CommandLine, Func<string, string> Mask);

    /// <summary>The result of opening a connection: the client, or why there is none (null: the user said no, or a message is already in the status bar).</summary>
    private sealed record McpOpened(Client? Client, McpTarget? Target, string? Failure, IReadOnlyList<string> Details);

    private McpTarget BuildMcpTarget(McpServerEntry entry, Func<string, string?> variables)
    {
        if (entry.Transport == McpTransport.Remote)
        {
            var remote = McpConfig.BuildRemote(entry, variables);
            return new McpTarget(null, remote, [Loc.Get(K.McpClientDetailUrl, ("url", remote.MaskedUrl)), .. remote.MaskedHeaders], remote.MaskedUrl, remote.Mask);
        }
        var defaultDirectory = Active?.Path is { } path && Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
        var launch = McpConfig.BuildLaunch(entry, variables, defaultDirectory);
        return new McpTarget(launch, null, [Loc.Get(K.McpAgentsDetailCommand, ("command", launch.CommandLine)), Loc.Get(K.McpAgentsDetailFolder, ("folder", launch.WorkingDirectory)), .. launch.MaskedEnvironment],
            launch.CommandLine, launch.Mask);
    }

    private async Task<bool> ConfirmMcpStartAsync(McpServerEntry entry, McpTarget target, bool browse)
    {
        bool remote = target.Remote is not null;
        var buttons = new[] { new DialogButton(Loc.Get(remote ? K.McpClientConnectButton : K.McpAgentsTryStart), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        var title = remote ? Loc.Get(K.McpClientConnectTitle, ("name", entry.Name)) : Loc.Get(K.McpAgentsTryTitle, ("name", entry.Name));
        var message = Loc.Get(remote ? K.McpClientConnectMessage : browse ? K.McpClientStartMessage : K.McpAgentsTryMessage);
        return await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, message, buttons, target.Details)) == 0;
    }

    private async Task<Client> StartMcpAsync(McpTarget target, McpClientOptions options, string source, CancellationToken ct)
    {
        if (target.Launch is { } launch) return await Client.StartAsync(launch, options, line => _app.Output.Append(OutputChannel.Tasks, source, line), ct);
        // the key in the headers belongs to the address the user saw, so a redirect may lead to another page of that host and nowhere else
        var handler = _app.Remote.Policy().CreateHttpHandler();
        handler.AllowAutoRedirect = false;
        var http = new HttpClient(new SameHostRedirectHandler(handler)) { Timeout = Timeout.InfiniteTimeSpan };
        return await Client.StartRemoteAsync(target.Remote!, options, http, ct);
    }

    /// <summary>
    /// Why a call failed, in words for the user with the secrets hidden, and the lines the server wrote to its error output when it died; null for an exception that is not about the server.
    /// </summary>
    private (string Reason, IReadOnlyList<string> Details)? ExplainMcp(Exception e, McpTarget target, McpClientOptions options) => e switch
    {
        McpServerExitedException x => (x.Message, [.. x.Exit.StderrTail.Split('\n', StringSplitOptions.RemoveEmptyEntries)]),
        McpTimeoutException x => (Loc.Get(K.McpAgentsTimeout, ("seconds", x.Timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture))), []),
        OperationCanceledException => (Loc.Get(K.McpAgentsTimeout, ("seconds", options.HandshakeTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture))), []),
        McpClientException or RpcException or IOException or RemoteException => (target.Mask(e.Message), []),
        _ => null,
    };

    /// <summary>
    /// Asks for the missing variables, shows what will happen and, if the user agrees, connects. A key the server refuses is dropped from the credential store and asked for once more.
    /// </summary>
    private async Task<McpOpened> OpenMcpAsync(McpServerEntry entry, bool browse)
    {
        var nothing = new McpOpened(null, null, null, []);
        var options = _app.McpClientOptions;
        bool confirmed = false;
        for (int attempt = 0; ; attempt++)
        {
            if (await ResolveMcpVariablesAsync(entry) is not { } variables) return nothing;
            McpTarget target;
            try { target = BuildMcpTarget(entry, variables); }
            catch (McpClientException e)
            {
                SetStatusMessage(Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", e.Message)));
                return nothing;
            }
            if (!confirmed && !await ConfirmMcpStartAsync(entry, target, browse)) return nothing;
            confirmed = true;
            var source = Loc.Get(K.McpAgentsOutputSource, ("name", entry.Name));
            _app.Output.Append(OutputChannel.Tasks, source, target.CommandLine);
            try
            {
                using var timeout = new CancellationTokenSource(options.HandshakeTimeout);
                return new McpOpened(await StartMcpAsync(target, options, source, timeout.Token), target, null, []);
            }
            catch (McpAuthException e)
            {
                var dropped = DropStoredMcpKeys(entry);
                if (attempt == 0 && dropped.Count > 0)
                {
                    SetStatusMessage(Loc.Get(K.McpClientAuthDropped, ("name", entry.Name), ("variables", string.Join(", ", dropped))));
                    continue;
                }
                var reason = dropped.Count > 0 ? target.Mask(e.Message) : Loc.Get(K.McpClientAuthKept, ("name", entry.Name));
                return new McpOpened(null, target, Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", reason)), []);
            }
            catch (Exception e) when (ExplainMcp(e, target, options) is { } why)
            {
                return new McpOpened(null, target, Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", why.Reason)), why.Details);
            }
        }
    }

    private async Task ShowMcpResultAsync(McpServerEntry entry, string message, IReadOnlyList<string> details)
    {
        _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.McpAgentsOutputSource, ("name", entry.Name)), message);
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(entry.Name, message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)], details));
    }

    // ---- trying ----

    private async Task TryAgentMcpServerAsync(McpServerEntry entry)
    {
        var open = await OpenMcpAsync(entry, browse: false);
        string message;
        IReadOnlyList<string> result = open.Details;
        if (open.Client is { } client)
        {
            await using (client)
            {
                try
                {
                    var tools = await client.ListToolsAsync();
                    result = [.. tools.Select(t => t.Name)];
                    message = Loc.Get(K.McpAgentsResultOk, ("name", entry.Name), ("tools", tools.Count.ToString()), ("version", client.ProtocolVersion));
                }
                catch (Exception e) when (ExplainMcp(e, open.Target!, _app.McpClientOptions) is { } why)
                {
                    message = Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", why.Reason));
                    result = why.Details;
                }
            }
        }
        else if (open.Failure is { } failure) message = failure;
        else return;
        await ShowMcpResultAsync(entry, message, result);
    }

    // ---- browsing: tools and resources ----

    private async Task ConnectAgentMcpServerAsync(McpServerEntry entry)
    {
        var open = await OpenMcpAsync(entry, browse: true);
        if (open.Client is not { } client)
        {
            if (open.Failure is { } failure) await ShowMcpResultAsync(entry, failure, open.Details);
            return;
        }
        await using (client) await BrowseMcpAsync(entry, client, open.Target!);
    }

    /// <summary>Lists the tools and resources of the connected server and acts on the one the user chooses, until the list is dismissed or the server is gone.</summary>
    private async Task BrowseMcpAsync(McpServerEntry entry, Client client, McpTarget target)
    {
        var options = _app.McpClientOptions;
        IReadOnlyList<McpTool> tools;
        IReadOnlyList<McpResource> resources;
        try
        {
            tools = await client.ListToolsAsync();
            resources = await client.ListResourcesAsync();
        }
        catch (Exception e) when (ExplainMcp(e, target, options) is { } why)
        {
            await ShowMcpResultAsync(entry, Loc.Get(K.McpClientFailed, ("name", entry.Name), ("reason", why.Reason)), why.Details);
            return;
        }
        if (tools.Count + resources.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.McpClientNothing, ("name", entry.Name)));
            return;
        }
        var items = new List<PickItem>();
        for (int i = 0; i < tools.Count; i++)
            items.Add(new PickItem("t" + i, tools[i].Name, Loc.Get(K.McpClientToolRow, ("text", OneLine(tools[i].Description ?? tools[i].Title)))));
        for (int i = 0; i < resources.Count; i++)
            items.Add(new PickItem("r" + i, resources[i].Title ?? resources[i].Name, Loc.Get(K.McpClientResourceRow, ("text", OneLine(resources[i].Description ?? resources[i].Uri)))));
        while (true)
        {
            var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.McpClientListTitle, ("name", entry.Name)), Loc.Get(K.McpClientListWatermark), items), CancellationToken.None);
            if (chosen is null) return;
            int index = int.Parse(chosen[1..]);
            bool gone = chosen[0] == 't' ? await CallMcpToolAsync(entry, client, target, tools[index]) : await ReadMcpResourceAsync(entry, client, target, resources[index]);
            if (gone) return;
        }
    }

    private static string OneLine(string? text) => (text ?? "").ReplaceLineEndings(" ").Trim();

    private static bool IsJsonObject(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Asks for the arguments and then, in a question of its own, whether this call may be made: the user sees the server, the tool, the arguments and whether the server says the tool only reads.
    /// Nothing is sent before the answer is yes. Returns true when the server is gone.
    /// </summary>
    private async Task<bool> CallMcpToolAsync(McpServerEntry entry, Client client, McpTarget target, McpTool tool)
    {
        var arguments = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.McpClientArgsTitle, ("tool", tool.Name)), Loc.Get(K.McpClientArgsLabel), "{}",
            text => IsJsonObject(text) ? Loc.Get(K.McpClientArgsSchema, ("schema", OneLine(tool.InputSchemaJson))) : Loc.Get(K.McpClientArgsInvalid)))?.Trim();
        if (arguments is null) return false;
        if (!IsJsonObject(arguments))
        {
            SetStatusMessage(Loc.Get(K.McpClientArgsInvalid));
            return false;
        }
        var buttons = new[] { new DialogButton(Loc.Get(K.McpClientCallAllow)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true) };
        var note = Loc.Get(tool.ReadOnly ? K.McpClientCallReadOnly : K.McpClientCallMayChange);
        var question = new ChoiceRequest(Loc.Get(K.McpClientCallTitle, ("tool", tool.Name), ("server", entry.Name)),
            Loc.Get(K.McpClientCallMessage, ("server", entry.Name), ("tool", tool.Name), ("note", note)), buttons, [Loc.Get(K.McpClientCallDetailArgs, ("arguments", arguments))]);
        if (await _app.Dialogs.ChooseAsync(this, question) != 0) return false;

        var source = Loc.Get(K.McpAgentsOutputSource, ("name", entry.Name));
        SetStatusMessage(Loc.Get(K.McpClientCalling, ("tool", tool.Name)));
        _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.McpClientLogCall, ("tool", tool.Name), ("chars", arguments.Length)));
        McpToolResult result;
        try { result = await client.CallToolAsync(tool.Name, arguments); }
        catch (Exception e) when (ExplainMcp(e, target, _app.McpClientOptions) is { } why)
        {
            var failed = Loc.Get(K.McpClientFailed, ("name", tool.Name), ("reason", why.Reason));
            _app.Output.Append(OutputChannel.Tasks, source, failed);
            SetStatusMessage(failed);
            return client.Exited.IsCompleted;
        }
        ShowMcpText(Loc.Get(K.McpClientResultTab, ("server", entry.Name), ("tool", tool.Name)), result.Text);
        var message = Loc.Get(result.IsError ? K.McpClientResultError : K.McpClientResultOk, ("tool", tool.Name), ("length", result.FullLength));
        if (result.Truncated) message += " " + Loc.Get(K.McpClientCut, ("limit", result.Text.Length), ("length", result.FullLength));
        _app.Output.Append(OutputChannel.Tasks, source, message);
        SetStatusMessage(message);
        return false;
    }

    /// <summary>Reads the resource the user chose and shows it in a new read-only tab. Returns true when the server is gone.</summary>
    private async Task<bool> ReadMcpResourceAsync(McpServerEntry entry, Client client, McpTarget target, McpResource resource)
    {
        var source = Loc.Get(K.McpAgentsOutputSource, ("name", entry.Name));
        McpResourceContent content;
        try { content = await client.ReadResourceAsync(resource.Uri); }
        catch (Exception e) when (ExplainMcp(e, target, _app.McpClientOptions) is { } why)
        {
            var failed = Loc.Get(K.McpClientFailed, ("name", resource.Name), ("reason", why.Reason));
            _app.Output.Append(OutputChannel.Tasks, source, failed);
            SetStatusMessage(failed);
            return client.Exited.IsCompleted;
        }
        var name = resource.Title ?? resource.Name;
        ShowMcpText(Loc.Get(K.McpClientResourceTab, ("server", entry.Name), ("resource", name)), content.Text);
        var message = Loc.Get(K.McpClientResourceOk, ("resource", name), ("length", content.FullLength));
        if (content.Truncated) message += " " + Loc.Get(K.McpClientCut, ("limit", content.Text.Length), ("length", content.FullLength));
        _app.Output.Append(OutputChannel.Tasks, source, message);
        SetStatusMessage(message);
        return false;
    }

    /// <summary>A read-only tab with text a server sent; it is a copy and belongs to no file.</summary>
    private void ShowMcpText(string title, string text)
    {
        var document = Document.CreateVirtual("mcp-answer.txt", Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, TextCodec.Utf8);
        FocusView(OpenVirtualDocument(document, title));
    }

    // ---- converting ----

    private bool ConvertAgentMcpServer(McpServerEntry entry, McpAgent target)
    {
        var snippet = McpConfig.Render([entry], target, maskValues: true);
        if (McpConfig.Check(target, snippet.Text) is { Count: > 0 } problems)
        {
            SetStatusMessage(Loc.Get(K.McpAgentsConvertInvalid, ("agent", AgentName(target)), ("reason", string.Join("; ", problems))));
            return false;
        }
        var tab = NewDocument();
        if (tab.Surface is not { } surface) return false;
        surface.Document.Text = snippet.Text;
        FocusView(tab);
        SetStatusMessage(string.Join(' ', new[] { Loc.Get(K.McpAgentsConverted, ("agent", AgentName(target)), ("file", McpConfig.FileHint(target))) }.Concat(snippet.Notes.Select(NoteText))));
        return true;
    }

    private static string NoteText(McpNote note) => note.Kind switch
    {
        McpNoteKind.WorkingDirectoryDropped => Loc.Get(K.McpAgentsNoteFolder, ("name", note.Server)),
        McpNoteKind.DisabledDropped => Loc.Get(K.McpAgentsNoteDisabled, ("name", note.Server)),
        _ => Loc.Get(K.McpAgentsNoteRenamed, ("name", note.Server), ("to", note.Detail ?? "")),
    };
}

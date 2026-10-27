using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.McpClient;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;
using NksHub.Textida.Workbench.Commands;
using Client = NksHub.Textida.McpClient.McpClient;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, MCP Servers of Agents: one list of the MCP servers that Claude Code, Cursor, Codex and OpenCode are set up with, read from their files (never written). A server started by
/// a command can be tried after the user has seen the exact command with its variables hidden; any entry can be converted into a snippet for another agent, shown in a new tab.
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
        var items = new List<PickItem> { new("try", Loc.Get(K.McpAgentsTry), null) };
        foreach (var agent in Enum.GetValues<McpAgent>().Where(a => a != entry.Agent))
            items.Add(new PickItem(agent.ToString(), Loc.Get(K.McpAgentsConvert, ("agent", AgentName(agent))), McpConfig.FileHint(agent)));
        var chosen = await PluginPickAsync(new QuickPickRequest(entry.Name, Loc.Get(K.McpAgentsActionWatermark, ("name", entry.Name)), items), CancellationToken.None);
        if (chosen is null) return false;
        if (chosen == "try")
        {
            await TryAgentMcpServerAsync(entry);
            return false;
        }
        return ConvertAgentMcpServer(entry, Enum.Parse<McpAgent>(chosen));
    }

    private string? McpVariable(string name)
    {
        if (Environment.GetEnvironmentVariable(name) is { } value) return value;
        try { return _app.Credentials.Get(McpEnvCredentialPrefix + name); }
        catch (Exception e) when (e is IOException or CredentialStoreUnavailableException) { return null; }
    }

    private async Task TryAgentMcpServerAsync(McpServerEntry entry)
    {
        if (entry.Transport != McpTransport.Stdio)
        {
            SetStatusMessage(Loc.Get(K.McpAgentsRemote, ("name", entry.Name)));
            return;
        }
        var defaultDirectory = Active?.Path is { } path && Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
        McpLaunch launch;
        try { launch = McpConfig.BuildLaunch(entry, McpVariable, defaultDirectory); }
        catch (McpClientException e)
        {
            SetStatusMessage(Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", e.Message)));
            return;
        }

        var details = new List<string> { Loc.Get(K.McpAgentsDetailCommand, ("command", launch.CommandLine)), Loc.Get(K.McpAgentsDetailFolder, ("folder", launch.WorkingDirectory)) };
        details.AddRange(launch.MaskedEnvironment);
        var buttons = new[] { new DialogButton(Loc.Get(K.McpAgentsTryStart), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        if (await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.McpAgentsTryTitle, ("name", entry.Name)), Loc.Get(K.McpAgentsTryMessage), buttons, details)) != 0) return;

        var source = Loc.Get(K.McpAgentsOutputSource, ("name", entry.Name));
        _app.Output.Append(OutputChannel.Tasks, source, launch.CommandLine);
        var options = PluginLimitsPlaceholders.McpClient();
        string message;
        IReadOnlyList<string> result = [];
        try
        {
            using var timeout = new CancellationTokenSource(options.HandshakeTimeout);
            await using var client = await Client.StartAsync(launch, options, line => _app.Output.Append(OutputChannel.Tasks, source, line), timeout.Token);
            var tools = await client.ListToolsAsync(timeout.Token);
            result = [.. tools.Select(t => t.Name)];
            message = Loc.Get(K.McpAgentsResultOk, ("name", entry.Name), ("tools", tools.Count.ToString()), ("version", client.ProtocolVersion));
        }
        catch (McpServerExitedException e)
        {
            message = Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", e.Message));
            result = [.. e.Exit.StderrTail.Split('\n', StringSplitOptions.RemoveEmptyEntries)];
        }
        catch (OperationCanceledException)
        {
            message = Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", Loc.Get(K.McpAgentsTimeout, ("seconds", options.HandshakeTimeout.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)))));
        }
        catch (Exception e) when (e is McpClientException or RpcException or IOException)
        {
            message = Loc.Get(K.McpAgentsResultFailed, ("name", entry.Name), ("reason", launch.Mask(e.Message)));
        }
        _app.Output.Append(OutputChannel.Tasks, source, message);
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(entry.Name, message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)], result));
    }

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

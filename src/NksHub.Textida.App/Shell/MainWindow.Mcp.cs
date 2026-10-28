using Avalonia.Automation;
using Avalonia.Controls;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, MCP Server / Share This Tab with MCP / Share Selection with MCP / MCP Tools: the editor as an MCP server for the assistant of the user. The server is off until the user has read
/// what the assistant gets and switched it on; it sees only the tabs shared one by one, or the text of a selection as a read-only excerpt, and the values of a secrets file only when
/// the user allows them for that tab (<see cref="McpService"/>).
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The last server switch, share or tool choice the user started; tests wait for it.</summary>
    internal Task McpTask { get; private set; } = Task.CompletedTask;

    internal CommandOutcome ToggleMcpServer()
    {
        McpTask = ToggleMcpServerAsync();
        return CommandOutcome.Handled;
    }

    private async Task ToggleMcpServerAsync()
    {
        var mcp = _app.Plugins.Mcp;
        if (mcp.Enabled)
        {
            await mcp.DisableAsync();
            SetStatusMessage(Loc.Get(K.McpOffStatus));
            return;
        }
        var tools = McpServer.AllTools.Where(t => mcp.Server.IsEnabled(t.Name)).Select(t => "• " + McpToolLabel(t.Name)).ToList();
        var buttons = new[] { new DialogButton(Loc.Get(K.McpOnConfirm), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.McpOnTitle), Loc.Get(K.McpOnMessage, ("program", McpService.BridgeProgram)), buttons, tools));
        if (answer != 0) return;
        try { mcp.Enable(); }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.McpFailed, ("reason", e.Message)));
            return;
        }
        SetStatusMessage(Loc.Get(K.McpOnStatus, ("program", McpService.BridgeProgram)));
    }

    internal CommandOutcome ToggleIdeBridge()
    {
        McpTask = ToggleIdeBridgeAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the port of the /ide bridge of this window only after the user has been told what the agent gets; the same command closes it.</summary>
    private async Task ToggleIdeBridgeAsync()
    {
        var mcp = _app.Plugins.Mcp;
        if (mcp.IdeEnabled(this))
        {
            await mcp.DisableIdeAsync(this);
            SetStatusMessage(Loc.Get(K.IdeOffStatus));
            return;
        }
        if (!mcp.Enabled)
        {
            SetStatusMessage(Loc.Get(K.McpShareNeedServer));
            return;
        }
        var buttons = new[] { new DialogButton(Loc.Get(K.IdeOnConfirm), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.IdeOnTitle), Loc.Get(K.IdeOnMessage, ("folder", mcp.IdeLockDirectory)), buttons));
        if (answer != 0 || !mcp.Enabled || mcp.IdeEnabled(this)) return;
        try { mcp.EnableIde(this); }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.IdeFailed, ("reason", e.Message)));
            return;
        }
        RefreshIdeItem();
        SetStatusMessage(Loc.Get(K.IdeOnStatus, ("port", mcp.IdeOf(this)!.Port)));
    }

    /// <summary>Cuts the agents off the bridge of this window. The bridge stays open, so the agent can connect again with <c>/ide</c>.</summary>
    internal CommandOutcome DisconnectIdeAgent()
    {
        if (_app.Plugins.Mcp.IdeOf(this) is not { } bridge) SetStatusMessage(Loc.Get(K.IdeNotOn));
        else
        {
            var names = string.Join(", ", bridge.Agents.Select(IdeAgentName));
            SetStatusMessage(bridge.DisconnectAgents() == 0 ? Loc.Get(K.IdeNoAgent) : Loc.Get(K.IdeDisconnected, ("agent", names)));
        }
        return CommandOutcome.Handled;
    }

    private static string IdeAgentName(IdeAgent agent) =>
        agent.Name is null ? Loc.Get(K.IdeAgentUnnamed) : agent.Version is null ? agent.Name : agent.Name + " " + agent.Version;

    /// <summary>The folders the lock file of this window lists: the folders of the file panel, else the folders of the files shared with the agent.</summary>
    internal IReadOnlyList<string> IdeWorkspaceFolders()
    {
        if (_fileTree is { Roots.Count: > 0 } tree) return [.. tree.Roots];
        return [.. Tabs.Where(t => t.Path is not null && _app.Plugins.Mcp.IsShared(t)).Select(t => System.IO.Path.GetDirectoryName(t.Path)!).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private readonly Button _ideItem = StatusItem();

    internal Button IdeItem => _ideItem;

    /// <summary>The status bar item of the /ide bridge: it shows the connected agent, and a click disconnects it. Hidden while the bridge of this window is off.</summary>
    private void StartIdeItem()
    {
        _ideItem.IsVisible = false;
        _ideItem.Click += (_, _) => DisconnectIdeAgent();
        _app.Plugins.Mcp.IdeChanged += OnIdeChanged;
        _app.Plugins.Mcp.IdeIncompatible += OnIdeIncompatible;
    }

    private void ReleaseIde()
    {
        _app.Plugins.Mcp.IdeChanged -= OnIdeChanged;
        _app.Plugins.Mcp.IdeIncompatible -= OnIdeIncompatible;
        _ = _app.Plugins.Mcp.DisableIdeAsync(this);
    }

    private void OnIdeChanged(MainWindow window)
    {
        if (ReferenceEquals(window, this)) RefreshIdeItem();
    }

    private void OnIdeIncompatible(MainWindow window, string reason)
    {
        if (!ReferenceEquals(window, this)) return;
        RefreshIdeItem();
        ShowNotice("ide", Loc.Get(K.IdeIncompatible, ("reason", reason)), warning: true);
    }

    internal void RefreshIdeItem()
    {
        var bridge = _app.Plugins.Mcp.IdeOf(this);
        _ideItem.IsVisible = bridge is not null;
        if (bridge is null) return;
        var agents = bridge.Agents;
        string text = agents.Count == 0 ? Loc.Get(K.IdeStatusWaiting) : Loc.Get(K.IdeStatusConnected, ("agent", string.Join(", ", agents.Select(IdeAgentName))));
        string tip = Loc.Get(K.IdeStatusTip, ("port", bridge.Port));
        _ideItem.Content = text;
        ToolTip.SetTip(_ideItem, tip);
        AutomationProperties.SetName(_ideItem, text);
        AutomationProperties.SetHelpText(_ideItem, tip);
    }

    internal CommandOutcome ShareTabWithMcp()
    {
        if (Active is not { } tab) return CommandOutcome.NotHandled;
        McpTask = ShareTabWithMcpAsync(tab);
        return CommandOutcome.Handled;
    }

    private async Task ShareTabWithMcpAsync(DocTab tab)
    {
        var mcp = _app.Plugins.Mcp;
        if (!mcp.Enabled)
        {
            SetStatusMessage(Loc.Get(K.McpShareNeedServer));
            return;
        }
        if (tab.Custom is not null || tab.Doc.IsResults)
        {
            SetStatusMessage(Loc.Get(K.McpShareUnavailable));
            return;
        }
        if (mcp.IsShared(tab))
        {
            mcp.Unshare(tab.Doc.Doc.Id);
            SetStatusMessage(Loc.Get(K.McpShareOff, ("name", tab.Title)));
            return;
        }
        if (await AskRevealSecretsAsync(tab) is not { } reveal) return;
        if (!mcp.Enabled || mcp.IsShared(tab)) return; // the server was switched off, or the tab shared, while the question was open
        mcp.Share(this, tab, reveal);
        SetStatusMessage(Loc.Get(K.McpShareOn, ("name", tab.Title)));
    }

    /// <summary>Asks whether the assistant may see the real values of a secrets file; null when the user cancelled. Any other file needs no question.</summary>
    private async Task<bool?> AskRevealSecretsAsync(DocTab tab)
    {
        if (!SecretValues.IsSecretFile(tab.Doc.Doc.SecretsPath)) return false;
        var buttons = new[] { new DialogButton(Loc.Get(K.McpSecretsKeep), IsDefault: true), new DialogButton(Loc.Get(K.McpSecretsShow)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.McpSecretsTitle), Loc.Get(K.McpSecretsMessage, ("file", tab.Title)), buttons));
        return answer is 0 or 1 ? answer == 1 : null;
    }

    internal CommandOutcome ShareSelectionWithMcp()
    {
        if (Active is not { } tab) return CommandOutcome.NotHandled;
        McpTask = ShareSelectionWithMcpAsync(tab);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Gives the assistant the selected text (the primary selection) as a read-only excerpt in a tab of its own, so the rest of the tab stays hidden from it. The excerpt is a copy:
    /// it does not follow the tab, and the assistant cannot propose changes to it. The excerpt carries the file name of its source, so the values of a secrets file stay hidden in it.
    /// </summary>
    private async Task ShareSelectionWithMcpAsync(DocTab tab)
    {
        var mcp = _app.Plugins.Mcp;
        if (!mcp.Enabled)
        {
            SetStatusMessage(Loc.Get(K.McpShareNeedServer));
            return;
        }
        if (tab.Custom is not null || tab.Doc.IsResults || PrimarySelectionOf(tab) is not { } first)
        {
            SetStatusMessage(Loc.Get(K.McpShareUnavailable));
            return;
        }
        if (first.IsEmpty)
        {
            SetStatusMessage(Loc.Get(K.McpShareSelectionEmpty));
            return;
        }
        if (await AskRevealSecretsAsync(tab) is not { } reveal) return;
        var selection = PrimarySelectionOf(tab) ?? first; // read again: the question may have been open for a while
        if (!mcp.Enabled || selection.IsEmpty) return;
        if (selection.Length > PluginLimitsPlaceholders.McpMaxSelectionShareBytes)
        {
            SetStatusMessage(Loc.Get(K.McpShareSelectionTooLarge, ("limit", PluginLimitsPlaceholders.McpMaxSelectionShareBytes)));
            return;
        }
        var source = tab.Doc.Doc;
        var name = source.SecretsPath is { } path ? System.IO.Path.GetFileName(path) : tab.Title;
        var excerpt = Document.CreateVirtual(name, source.ReadBytes(selection.ToRange()), AvaloniaEditBackendFactory.Instance, source.Codec.WithBom(false));
        var opened = OpenVirtualDocument(excerpt, Loc.Get(K.McpSelectionTab, ("name", tab.Title)));
        mcp.Share(this, opened, reveal);
        SetStatusMessage(Loc.Get(K.McpShareSelectionOn, ("name", tab.Title), ("bytes", selection.Length)));
    }

    private static TextSelection? PrimarySelectionOf(DocTab tab) => tab.Surface?.ByteSelections()?.PrimarySelection;

    internal CommandOutcome ShowMcpTools()
    {
        McpTask = ChooseMcpToolsAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Lists the tools with their state; choosing one switches it, and the list stays until it is dismissed.</summary>
    private async Task ChooseMcpToolsAsync()
    {
        var server = _app.Plugins.Mcp.Server;
        while (true)
        {
            var items = McpServer.AllTools.Select(t => new PickItem(t.Name, McpToolLabel(t.Name), Loc.Get(server.IsEnabled(t.Name) ? K.McpToolsOn : K.McpToolsOff))).ToList();
            var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.McpToolsTitle), Loc.Get(K.McpToolsWatermark), items), CancellationToken.None);
            if (chosen is null) return;
            server.SetEnabled(chosen, !server.IsEnabled(chosen));
        }
    }

    internal static string McpToolLabel(string tool) => Loc.Get(tool switch
    {
        "editor_list_documents" => K.McpToolListDocuments,
        "editor_read_range" => K.McpToolReadRange,
        "editor_search" => K.McpToolSearch,
        "editor_get_selection" => K.McpToolGetSelection,
        "editor_prepare_changes" => K.McpToolPrepareChanges,
        "editor_get_change_status" => K.McpToolChangeStatus,
        "editor_outline" => K.McpToolOutline,
        "editor_find_in_files" => K.McpToolFindInFiles,
        "editor_file_facts" => K.McpToolFileFacts,
        "editor_log_query" => K.McpToolLogQuery,
        "editor_diagnostics" => K.McpToolDiagnostics,
        "editor_read_bytes" => K.McpToolReadBytes,
        "editor_search_bytes" => K.McpToolSearchBytes,
        "editor_prepare_byte_changes" => K.McpToolPrepareByteChanges,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "No label for this MCP tool."),
    });
}

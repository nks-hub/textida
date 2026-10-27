using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, MCP Server / Share This Tab with MCP / MCP Tools: the editor as an MCP server for the assistant of the user. The server is off until the user has read what the assistant gets
/// and switched it on; it sees only the tabs shared one by one, and the values of a secrets file only when the user allows them for that tab (<see cref="McpService"/>).
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
        bool reveal = false;
        if (SecretValues.IsSecretFile(tab.Path))
        {
            var buttons = new[] { new DialogButton(Loc.Get(K.McpSecretsKeep), IsDefault: true), new DialogButton(Loc.Get(K.McpSecretsShow)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
            int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.McpSecretsTitle), Loc.Get(K.McpSecretsMessage, ("file", tab.Title)), buttons));
            if (answer is not (0 or 1)) return;
            reveal = answer == 1;
        }
        if (!mcp.Enabled || mcp.IsShared(tab)) return; // the server was switched off, or the tab shared, while the question was open
        mcp.Share(this, tab, reveal);
        SetStatusMessage(Loc.Get(K.McpShareOn, ("name", tab.Title)));
    }

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
        "editor_prepare_changes" => K.McpToolPrepareChanges,
        "editor_get_change_status" => K.McpToolChangeStatus,
        "editor_outline" => K.McpToolOutline,
        "editor_find_in_files" => K.McpToolFindInFiles,
        "editor_file_facts" => K.McpToolFileFacts,
        "editor_log_query" => K.McpToolLogQuery,
        "editor_read_bytes" => K.McpToolReadBytes,
        "editor_search_bytes" => K.McpToolSearchBytes,
        "editor_prepare_byte_changes" => K.McpToolPrepareByteChanges,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "No label for this MCP tool."),
    });
}

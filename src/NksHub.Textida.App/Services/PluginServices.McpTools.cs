using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Commands of plugins offered to the assistants as MCP tools (PZ.10.1). A tool is on offer only while the plugin holds the <c>mcp.tool</c> permission, which the user grants in the plugin manager,
/// and only to a client the user allowed in Tools, MCP, Clients; both are checked at every list and every call. A call may name a document: it must be one the user shared with the assistant.
/// </summary>
internal sealed partial class PluginServices
{
    /// <summary>Prefix of the name of every tool a plugin brings, so that none can take the name of a built-in tool or of another plugin's id space.</summary>
    internal const string McpToolPrefix = "plugin_";

    /// <summary>The tools on offer now, asked by the MCP server.</summary>
    internal IReadOnlyList<McpPluginTool> McpPluginTools()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var tools = new List<McpPluginTool>();
        foreach (var declared in _app.Clips.McpTools)
        {
            string name = McpToolPrefix + declared.Tool.Id;
            if (!Permissions.IsGranted(declared.PluginId, DocumentPermissions.McpTool, GrantScope.All) || !names.Add(name)) continue;
            tools.Add(new McpPluginTool(name, declared.Tool.Title, declared.Tool.Description, SchemaWithDocument(declared.Tool.InputSchema), declared.Tool.ReadOnly,
                (arguments, cancellation) => RunMcpToolAsync(declared, arguments, cancellation)));
        }
        return tools;
    }

    /// <summary>The schema of the plugin with the optional <c>document</c> every plugin tool takes added to its properties.</summary>
    internal static string SchemaWithDocument(JsonElement? schema)
    {
        var node = schema is { } given ? JsonNode.Parse(given.GetRawText())!.AsObject() : new JsonObject { ["type"] = "object" };
        var properties = node["properties"] as JsonObject ?? new JsonObject();
        properties["document"] = new JsonObject { ["type"] = "string", ["description"] = "Optional id of a document from editor_list_documents; the command then works on that document." };
        node["properties"] = properties;
        return node.ToJsonString();
    }

    private async Task<string> RunMcpToolAsync(DeclaredMcpTool declared, JsonElement arguments, CancellationToken cancellation)
    {
        string plugin = declared.PluginId, command = declared.Tool.Command;
        if (!Permissions.IsGranted(plugin, DocumentPermissions.McpTool, GrantScope.All)) throw new McpToolRefusedException("The user withdrew the permission of this plugin to offer tools.");
        Guid? document = null;
        var forwarded = new JsonObject();
        if (arguments.ValueKind == JsonValueKind.Object)
            foreach (var property in arguments.EnumerateObject())
            {
                if (property.Name == "document")
                {
                    document = property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var id) ? id : throw new McpToolRefusedException("'document' must be a document id from editor_list_documents.");
                    continue;
                }
                forwarded[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
        string argumentsJson = forwarded.ToJsonString();
        try
        {
            if (document is { } shared)
            {
                if (!Mcp.IsSharedDocument(shared) || TabOf(shared) is not var (window, tab)) throw new McpToolRefusedException("This document is not shared with the assistant.");
                var outcome = await Dispatcher.UIThread.InvokeAsync(() => ExecuteAsync(window, tab, plugin, "onCommand:" + command, declared.PluginName, command, ",\"arguments\":" + argumentsJson)).ConfigureAwait(false);
                return outcome.Started ? outcome.Result.GetRawText() : throw new McpToolRefusedException("The plugin could not run the command on this document now (it is busy there, or it did not start).");
            }
            await _host.ActivateAsync("onCommand:" + command).ConfigureAwait(false);
            var request = $"{{\"command\":\"{JsonEncodedText.Encode(command)}\",\"language\":\"en\",\"arguments\":{argumentsJson}}}";
            var result = await _host.CallAsync(plugin, "command/execute", Encoding.UTF8.GetBytes(request), ct: cancellation).ConfigureAwait(false);
            return result.GetRawText();
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or RpcException)
        {
            throw new McpToolRefusedException($"The plugin '{declared.PluginName}' could not run the command: {e.Message}");
        }
    }
}

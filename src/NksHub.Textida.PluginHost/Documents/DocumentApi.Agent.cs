using System.Text.Json;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// Agent panel for plugins (P6.2.5): a plugin hosts its own conversation in the editor's agent panel, the one the ACP agents use. The plugin streams the agent's messages and tools in
/// with <see cref="DocumentMethods.AgentUpdate"/>; the messages of the user, the cancelling of a turn and the end of the conversation come back as events. Nothing here writes a file:
/// a change to a document is an <see cref="DocumentMethods.EditPropose"/> and goes to the change review, and a question to the user is <see cref="DocumentMethods.AgentPermission"/>, the permission dialog.
/// </summary>
public sealed partial class DocumentApi
{
    private AgentPanelOptions NeedAgent() => _options.Agent ?? throw new RpcException(RpcErrorCodes.MethodNotFound, "The editor takes no agent conversations from plugins.");

    private string AgentText(AgentPanelOptions options, JsonElement p, string name, bool allowEmpty = false)
    {
        var text = NeedString(p, name);
        if (text.Length == 0 && !allowEmpty) throw Invalid($"Parameter '{name}' must not be empty.");
        return text.Length <= options.MaxUpdateChars ? text : throw new RpcException(RpcErrorCodes.MessageTooLarge, $"'{name}' holds at most {options.MaxUpdateChars} characters; got {text.Length}.");
    }

    private static string? OptionalString(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static RpcException NoSession() => Fail(DocumentErrorCodes.NoAgentSession, "The plugin has no conversation in the agent panel; open one first.");

    private async Task<byte[]> AgentOpenAsync(string pluginId, JsonElement p)
    {
        var options = NeedAgent();
        var name = AgentText(options, p, "name");
        return await _owner.InvokeAsync(() => options.Panel.Open(pluginId, name)
            ? Obj(w => w.WriteBoolean("opened", true))
            : throw Fail(DocumentErrorCodes.AgentPanelBusy, "The agent panel holds another conversation; the user has to end it first."));
    }

    private async Task<byte[]> AgentUpdateAsync(string pluginId, JsonElement p)
    {
        var options = NeedAgent();
        var kind = NeedString(p, "kind");
        var update = kind switch
        {
            "message" => new AgentUpdate(AgentUpdateKind.Message, AgentText(options, p, "text")),
            "tool" => new AgentUpdate(AgentUpdateKind.Tool, AgentText(options, p, "title"), AgentText(options, p, "id"), OptionalString(p, "status")),
            "toolUpdate" => new AgentUpdate(AgentUpdateKind.ToolUpdate, OptionalString(p, "title"), AgentText(options, p, "id"), OptionalString(p, "status")),
            "note" => new AgentUpdate(AgentUpdateKind.Note, AgentText(options, p, "text")),
            "problem" => new AgentUpdate(AgentUpdateKind.Problem, AgentText(options, p, "text")),
            "turnEnd" => new AgentUpdate(AgentUpdateKind.TurnEnd, p.TryGetProperty("reason", out _) ? AgentText(options, p, "reason", allowEmpty: true) : ""),
            _ => throw Invalid($"Unknown update kind '{kind}'."),
        };
        return await _owner.InvokeAsync(() =>
        {
            if (!options.Panel.HasSession(pluginId)) throw NoSession();
            options.Panel.Update(pluginId, update);
            return Obj(w => w.WriteBoolean("accepted", true));
        });
    }

    /// <summary>Asks the user. Needs an open conversation; the answer is <c>{"optionId"}</c> or <c>{"cancelled":true}</c> (the dialog was cancelled, or the turn was).</summary>
    private async Task<byte[]> AgentPermissionAsync(string pluginId, JsonElement p, CancellationToken cancellation)
    {
        var options = NeedAgent();
        var tool = AgentText(options, p, "tool");
        var array = Need(p, "options");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw Invalid("Parameter 'options' must be a non-empty array.");
        List<AgentPermissionOption> choices = [];
        foreach (var o in array.EnumerateArray())
        {
            var id = AgentText(options, o, "id");
            if (choices.Any(c => c.Id == id)) throw Invalid($"The option id '{id}' is used twice.");
            choices.Add(new AgentPermissionOption(id, AgentText(options, o, "name"), o.TryGetProperty("reject", out var r) && r.ValueKind == JsonValueKind.True));
        }
        if (!options.Panel.HasSession(pluginId)) throw NoSession();
        var chosen = await options.Panel.AskAsync(pluginId, tool, choices, cancellation);
        return Obj(w =>
        {
            if (chosen is null) w.WriteBoolean("cancelled", true);
            else w.WriteString("optionId", chosen);
        });
    }

    private byte[] AgentClose(string pluginId)
    {
        NeedAgent().Panel.Close(pluginId);
        return Obj(w => w.WriteBoolean("closed", true));
    }

    /// <summary>The user sent a message in the panel of this plugin's conversation. Delivered in order with the other events of the plugin.</summary>
    public void AgentUserMessage(string pluginId, string text) => EnqueueAgent(pluginId, DocumentEvents.AgentMessage, Obj(w => w.WriteString("text", text)));

    /// <summary>The user cancelled the running turn.</summary>
    public void AgentCancelled(string pluginId) => EnqueueAgent(pluginId, DocumentEvents.AgentCancel, Obj(_ => { }));

    /// <summary>The editor ended the conversation (<paramref name="reason"/>: <c>user</c> or <c>window</c>).</summary>
    public void AgentClosed(string pluginId, string reason) => EnqueueAgent(pluginId, DocumentEvents.AgentClosed, Obj(w => w.WriteString("reason", reason)));
}

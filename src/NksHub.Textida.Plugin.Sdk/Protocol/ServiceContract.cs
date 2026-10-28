using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.PluginHost.Permissions;

/// <summary>Method names of the services of the editor that are not part of the document or UI API: the network, the secret store of the plugin and the commands of other plugins.</summary>
public static class PluginServiceMethods
{
    /// <summary>Sends one HTTP request for the plugin through the network policy and proxy of the editor. Needs <see cref="PluginPermissionNames.Network"/>.</summary>
    public const string NetRequest = "net/request";
    /// <summary>Reads a secret of the plugin by name; the answer holds <c>value</c>, or null. A plugin reaches only its own secrets.</summary>
    public const string SecretGet = "secret/get";
    /// <summary>Stores a secret of the plugin (the editor keeps it in the credential store of the system).</summary>
    public const string SecretSet = "secret/set";
    /// <summary>Deletes a secret of the plugin.</summary>
    public const string SecretDelete = "secret/delete";
    /// <summary>
    /// Runs a command, as the editor does with <c>command/execute</c> to a plugin; sent by a plugin to the editor it runs a command of another plugin.
    /// Needs <see cref="PluginPermissionNames.CommandExecute"/> scoped to the target plugin.
    /// </summary>
    public const string CommandExecute = "command/execute";
}

/// <summary>Permissions outside the document API.</summary>
public static class PluginPermissionNames
{
    /// <summary>Run a command of another plugin. The scope is the target plugin.</summary>
    public const string CommandExecute = "command.execute";

    /// <summary>Send HTTP requests through the editor (<c>net/request</c>), which applies its network policy and proxy. The scope is all.</summary>
    public const string Network = "network";

    /// <summary>
    /// What a manifest may ask for; the user grants each one to the plugin in the plugin manager, for all documents. <see cref="DocumentPermissions.SecretsReveal"/> (one document)
    /// and <see cref="CommandExecute"/> (one target plugin) have a narrower scope and are granted when needed, not declared.
    /// </summary>
    public static IReadOnlyList<string> Declarable { get; } =
    [
        DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect,
        DocumentPermissions.ClipboardWrite, DocumentPermissions.Virtual, DocumentPermissions.WorkspaceFiles, DocumentPermissions.Source,
        DocumentPermissions.Preview, DocumentPermissions.AgentPanel,
        DocumentPermissions.LanguageServer, Network,
    ];
}

/// <summary>Which permission a request of a plugin needs. The editor checks it before it runs the request; a tool can use the same table to compare what a plugin declares with what it calls.</summary>
public static class PluginMethodPermissions
{
    /// <summary>
    /// The permission a method of the document API needs; null for the calls that only give something back (unsubscribe, release a snapshot).
    /// Every other method of that API, known or not, needs <see cref="DocumentPermissions.Read"/>.
    /// </summary>
    public static string? ForDocumentApi(string method) => method switch
    {
        DocumentMethods.Unsubscribe or DocumentMethods.SnapshotRelease => null,
        DocumentMethods.EditApply or DocumentMethods.SnippetInsert => DocumentPermissions.EditDirect,
        DocumentMethods.EditPropose or DocumentMethods.WorkspaceEdit => DocumentPermissions.EditPropose,
        DocumentMethods.ClipboardWrite => DocumentPermissions.ClipboardWrite,
        DocumentMethods.VirtualOpen or DocumentMethods.VirtualUpdate or DocumentMethods.VirtualClose => DocumentPermissions.Virtual,
        DocumentMethods.SourceRegister => DocumentPermissions.Source,
        DocumentMethods.PreviewShow or DocumentMethods.PreviewClose => DocumentPermissions.Preview,
        DocumentMethods.AgentOpen or DocumentMethods.AgentUpdate or DocumentMethods.AgentPermission or DocumentMethods.AgentClose => DocumentPermissions.AgentPanel,
        _ => DocumentPermissions.Read,
    };

    /// <summary>
    /// The permission any request of a plugin to the editor needs: the document API, the network and the commands of other plugins. The UI API and the secret store of the plugin need none (null).
    /// </summary>
    public static string? For(string method) => method switch
    {
        PluginServiceMethods.NetRequest => PluginPermissionNames.Network,
        PluginServiceMethods.CommandExecute => PluginPermissionNames.CommandExecute,
        PluginServiceMethods.SecretGet or PluginServiceMethods.SecretSet or PluginServiceMethods.SecretDelete => null,
        _ when method.StartsWith(UiPrefix, StringComparison.Ordinal) => null,
        _ => ForDocumentApi(method),
    };

    private const string UiPrefix = "ui/";
}

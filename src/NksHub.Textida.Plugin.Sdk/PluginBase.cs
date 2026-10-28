using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>Handles one request of the editor; returns the result as raw JSON, or null for a null result.</summary>
public delegate ValueTask<byte[]?> RequestHandler(JsonElement parameters, CancellationToken cancellation);

/// <summary>
/// Base of a plugin written in C#. Register methods with <see cref="On(string, RequestHandler)"/> or the typed overload and start it with <see cref="PluginRunner.RunAsync"/>.
/// The handshake, state resync, ping, shutdown and exit are handled here. The typed overload takes source-generated
/// <see cref="JsonTypeInfo{T}"/>, so a plugin built on it carries no reflection and can be published with NativeAOT.
/// </summary>
/// <param name="name">Name reported in the handshake.</param>
/// <param name="apiVersion">Plugin API version the plugin implements; the editor refuses a plugin whose version differs from its own.</param>
public abstract class PluginBase(string name, int apiVersion)
{
    private readonly Dictionary<string, RequestHandler> _methods = [];

    /// <summary>Name reported in the handshake.</summary>
    public string Name { get; } = name;
    /// <summary>The plugin API version the plugin implements (<see cref="PluginApi.Version"/> for the API of this SDK).</summary>
    public int ApiVersion { get; } = apiVersion;

    /// <summary>The connection to the editor, for requests and notifications to it. Set before the first request is handled.</summary>
    public JsonRpcDispatcher Rpc { get; internal set; } = null!;

    /// <summary>
    /// True for a plugin that holds nothing in the editor which cannot be set up again (no virtual tab, panel, source or provider it registered at its start and cannot register twice):
    /// the editor ends its process after a while without work and starts it again with the next activation event.
    /// </summary>
    protected virtual bool IdleStop => false;

    /// <summary>Registers the handler of a request the editor sends. A method nobody registered is answered with <see cref="RpcErrorCodes.MethodNotFound"/>.</summary>
    protected void On(string method, RequestHandler handler) => _methods.Add(method, handler);

    /// <summary>Registers the handler of a request with typed parameters and result. Parameters that do not fit <paramref name="request"/> are answered with <see cref="RpcErrorCodes.InvalidParams"/>.</summary>
    protected void On<TRequest, TResult>(string method, JsonTypeInfo<TRequest> request, JsonTypeInfo<TResult> result, Func<TRequest, CancellationToken, ValueTask<TResult>> handler) =>
        On(method, async (p, ct) =>
        {
            TRequest value;
            try { value = p.Deserialize(request) ?? throw new JsonException("The parameters are null."); }
            catch (JsonException e) { throw new RpcException(RpcErrorCodes.InvalidParams, e.Message); }
            return JsonSerializer.SerializeToUtf8Bytes(await handler(value, ct).ConfigureAwait(false), result);
        });

    /// <summary>The request the editor sends to run a command of the plugin.</summary>
    public const string CommandMethod = "command/execute";

    private readonly Dictionary<string, CommandHandler> _commands = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tasks = new();

    /// <summary>
    /// Registers a command. The plugin answers <see cref="CommandMethod"/> by itself: it reads the request, runs the handler of the command it names, and sends the
    /// <see cref="CommandResult"/> back; a command it does not know is an <see cref="RpcErrorCodes.InvalidParams"/> error.
    /// </summary>
    protected void OnCommand(string command, CommandHandler handler)
    {
        if (_commands.Count == 0) On(CommandMethod, RunCommandAsync);
        _commands.Add(command, handler);
    }

    private async ValueTask<byte[]?> RunCommandAsync(JsonElement parameters, CancellationToken cancellation)
    {
        var context = CommandContext.From(parameters);
        if (!_commands.TryGetValue(context.Command, out var handler)) throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
        return (await handler(context, cancellation).ConfigureAwait(false)).ToJson();
    }

    /// <summary>The client of the document and revision a command was started on.</summary>
    protected DocumentClient DocumentOf(CommandContext command) => DocumentClient.For(Rpc, command);

    /// <summary>Shows a notice in the window of the editor.</summary>
    protected Task NotifyAsync(NoticeLevel level, string message, CancellationToken cancellation = default) => DocumentClient.NotifyAsync(Rpc, level, message, cancellation);

    /// <summary>
    /// Starts a task the user sees, with progress and a cancel button. Its <see cref="PluginTask.Cancellation"/> is cancelled when the user cancels it or when <paramref name="cancellation"/> is.
    /// Reports closer together than <paramref name="reportEvery"/> are dropped; null sends every one.
    /// </summary>
    protected async Task<PluginTask> StartTaskAsync(string title, CancellationToken cancellation, TimeSpan? reportEvery = null)
    {
        var started = await Rpc.RequestAsync(UiMethods.TaskStart, new TaskStartParams(title, Cancellable: true), SdkJson.Default.TaskStartParams, SdkJson.Default.TaskStartResult, cancellation).ConfigureAwait(false);
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        _tasks[started.Task] = source;
        return new PluginTask(Rpc, started.Task, source, reportEvery, id => _tasks.TryRemove(id, out _));
    }

    /// <summary>The state after a start or restart, as the editor sends it (open documents with revisions, lost events).</summary>
    protected virtual ValueTask OnResyncAsync(JsonElement state) => ValueTask.CompletedTask;

    /// <summary>An editor event (<c>$/event</c> notification) with its name and payload.</summary>
    protected virtual ValueTask OnEventAsync(JsonElement eventPayload) => ValueTask.CompletedTask;

    internal async ValueTask<byte[]?> HandleAsync(string method, JsonElement p, CancellationToken ct)
    {
        switch (method)
        {
            case PluginMethods.Initialize:
                return JsonSerializer.SerializeToUtf8Bytes(new InitializeResult(ApiVersion, Name, EventAcks: true, IdleStop: IdleStop), PluginJson.Default.InitializeResult);
            case PluginMethods.Resync:
                await OnResyncAsync(p).ConfigureAwait(false);
                return null;
            case PluginMethods.Ping:
            case PluginMethods.Shutdown:
                return "true"u8.ToArray();
            default:
                if (!_methods.TryGetValue(method, out var handler)) throw new RpcException(RpcErrorCodes.MethodNotFound, $"The plugin has no method '{method}'.");
                return await handler(p, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Events are handled one after another and each is acknowledged, so the editor holds the next ones back while <see cref="OnEventAsync"/> is slow.</summary>
    internal async ValueTask HandleNotificationAsync(string method, JsonElement p)
    {
        if (method != PluginMethods.Event) return;
        CancelTaskOf(p);
        try { await OnEventAsync(p).ConfigureAwait(false); }
        // an event that failed is still handled; without the acknowledgement the editor would wait for it for good
        finally { await Rpc.NotifyAsync(PluginMethods.EventsHandled, HandledOne).ConfigureAwait(false); }
    }

    /// <summary>The user cancelled a task of this plugin (the event <c>ui/taskCancelled</c>): its token is cancelled.</summary>
    private void CancelTaskOf(JsonElement eventPayload)
    {
        if (eventPayload.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == UiEvents.TaskCancelled
            && eventPayload.TryGetProperty("payload", out var payload) && payload.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.String
            && _tasks.TryGetValue(task.GetString()!, out var source))
        {
            try { source.Cancel(); }
            catch (ObjectDisposedException) { } // the task ended in the same moment
        }
    }

    private static readonly byte[] HandledOne = "{\"count\":1}"u8.ToArray();
}

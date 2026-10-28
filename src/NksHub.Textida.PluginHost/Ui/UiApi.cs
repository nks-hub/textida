using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Ui;

/// <summary>
/// UI contributions of the plugin protocol: panels with a list or tree, one status item per plugin, quick pick, input, confirm,
/// notifications and tasks with progress and cancel. The plugin only sends data; <see cref="IPluginUi"/> draws it. Combine with the
/// document API through <see cref="PluginApiRouter"/>, and call <see cref="PluginEnded"/> when a plugin stops.
/// </summary>
public sealed class UiApi
{
    private readonly UiApiOptions _options;
    private readonly IPluginUi _ui;
    private readonly PluginEventSender _send;
    private readonly object _gate = new();
    private readonly Dictionary<string, Contributions> _plugins = [];

    public UiApi(UiApiOptions options, IPluginUi ui, PluginEventSender send)
    {
        if (options.MaxPanelsPerPlugin < 1 || options.MaxItemsPerPanel < 1 || options.MaxTreeDepth < 1 || options.MaxTasksPerPlugin < 1
            || options.MaxTextLength < 1 || options.MaxPickItems < 1 || options.TaskCancelGrace <= TimeSpan.Zero)
            throw new ArgumentException("Every UI API limit must be positive.", nameof(options));
        _options = options;
        _ui = ui;
        _send = send;
    }

    private sealed class Contributions
    {
        public readonly Dictionary<string, HashSet<string>> Panels = [];
        public readonly Dictionary<string, TaskEntry> Tasks = [];
    }

    private sealed class TaskEntry(bool cancellable) { public bool Cancellable { get; } = cancellable; public bool CancelSent; }

    private Contributions Of(string pluginId)
    {
        if (!_plugins.TryGetValue(pluginId, out var c)) _plugins[pluginId] = c = new Contributions();
        return c;
    }

    public async ValueTask<byte[]?> HandleAsync(string pluginId, string method, JsonElement p, CancellationToken cancellation)
    {
        switch (method)
        {
            case UiMethods.PanelSet: return SetPanel(pluginId, p);
            case UiMethods.PanelRemove: return RemovePanel(pluginId, p);
            case UiMethods.StatusSet:
                _ui.SetStatus(pluginId, new StatusModel(Text(p, "text"), OptionalText(p, "tooltip")));
                return DocumentApi.Obj(w => w.WriteBoolean("ok", true));
            case UiMethods.StatusClear:
                _ui.SetStatus(pluginId, null);
                return DocumentApi.Obj(w => w.WriteBoolean("ok", true));
            case UiMethods.QuickPick: return await PickAsync(pluginId, p, cancellation);
            case UiMethods.Input:
            {
                var answer = await _ui.InputAsync(pluginId, new InputRequest(Text(p, "title"), Text(p, "label"), OptionalText(p, "value") ?? "",
                    p.TryGetProperty("secret", out var secret) && secret.ValueKind == JsonValueKind.True), cancellation);
                return DocumentApi.Obj(w => { if (answer is null) w.WriteNull("value"); else w.WriteString("value", answer); });
            }
            case UiMethods.Confirm:
            {
                var yes = await _ui.ConfirmAsync(pluginId, new ConfirmRequest(Text(p, "title"), Text(p, "message")), cancellation);
                return DocumentApi.Obj(w => w.WriteBoolean("confirmed", yes));
            }
            case UiMethods.Notify:
                _ui.Notify(pluginId, Level(p), Text(p, "message"));
                return DocumentApi.Obj(w => w.WriteBoolean("ok", true));
            case UiMethods.TaskStart: return StartTask(pluginId, p);
            case UiMethods.TaskProgress: return TaskProgress(pluginId, p);
            case UiMethods.TaskEnd: return EndTask(pluginId, p);
            default: throw new RpcException(RpcErrorCodes.MethodNotFound, $"The editor has no method '{method}'.");
        }
    }

    // ---- validation ----

    private string Text(JsonElement p, string name) => Check(DocumentApi.NeedString(p, name), name);

    private string? OptionalText(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? Check(v.ValueKind == JsonValueKind.String ? v.GetString()! : throw DocumentApi.Invalid($"Parameter '{name}' must be a string."), name)
            : null;

    private string Check(string value, string name) =>
        value.Length <= _options.MaxTextLength ? value : throw DocumentApi.Invalid($"Parameter '{name}' is longer than {_options.MaxTextLength} characters.");

    private string Id(JsonElement p, string name)
    {
        var id = Text(p, name);
        return id.Length > 0 ? id : throw DocumentApi.Invalid($"Parameter '{name}' must not be empty.");
    }

    private static NoticeLevel Level(JsonElement p) => p.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.String
        ? l.GetString() switch
        {
            "info" => NoticeLevel.Info,
            "warning" => NoticeLevel.Warning,
            "error" => NoticeLevel.Error,
            _ => throw DocumentApi.Invalid("Parameter 'level' must be info, warning or error."),
        }
        : NoticeLevel.Info;

    private static RpcException Quota(string what, int limit) =>
        DocumentApi.Fail(UiErrorCodes.QuotaExceeded, $"A plugin may have {limit} {what}.", w => w.WriteNumber("limit", limit));

    // ---- panels ----

    private byte[] SetPanel(string pluginId, JsonElement p)
    {
        var id = Id(p, "panel");
        var title = Text(p, "title");
        var kind = DocumentApi.NeedString(p, "kind") switch
        {
            "list" => PanelKind.List,
            "tree" => PanelKind.Tree,
            _ => throw DocumentApi.Invalid("Parameter 'kind' must be list or tree."),
        };
        var itemsJson = DocumentApi.Need(p, "items");
        if (itemsJson.ValueKind != JsonValueKind.Array) throw DocumentApi.Invalid("Parameter 'items' must be an array.");
        HashSet<string> ids = [];
        var items = ParseItems(itemsJson, kind, 1, ids);
        var reveal = p.TryGetProperty("reveal", out var r) && r.ValueKind == JsonValueKind.True;
        lock (_gate)
        {
            var c = Of(pluginId);
            if (!c.Panels.ContainsKey(id) && c.Panels.Count >= _options.MaxPanelsPerPlugin) throw Quota("panels", _options.MaxPanelsPerPlugin);
            c.Panels[id] = ids;
        }
        _ui.SetPanel(pluginId, new PanelModel(id, title, kind, items, reveal));
        return DocumentApi.Obj(w => w.WriteNumber("items", ids.Count));
    }

    private List<PanelItem> ParseItems(JsonElement array, PanelKind kind, int depth, HashSet<string> ids)
    {
        if (depth > _options.MaxTreeDepth) throw DocumentApi.Invalid($"A tree may be {_options.MaxTreeDepth} levels deep.");
        List<PanelItem> items = [];
        foreach (var item in array.EnumerateArray())
        {
            var id = Id(item, "id");
            if (!ids.Add(id)) throw DocumentApi.Invalid($"The item id '{id}' is used twice in the panel.");
            if (ids.Count > _options.MaxItemsPerPanel) throw Quota("items in a panel", _options.MaxItemsPerPanel);
            IReadOnlyList<PanelItem> children = [];
            if (item.TryGetProperty("children", out var kids) && kids.ValueKind == JsonValueKind.Array && kids.GetArrayLength() > 0)
                children = kind == PanelKind.Tree ? ParseItems(kids, kind, depth + 1, ids) : throw DocumentApi.Invalid("A list has no child items.");
            items.Add(new PanelItem(id, Text(item, "label"), OptionalText(item, "detail"), children));
        }
        return items;
    }

    private byte[] RemovePanel(string pluginId, JsonElement p)
    {
        var id = Id(p, "panel");
        bool removed;
        lock (_gate) removed = _plugins.TryGetValue(pluginId, out var c) && c.Panels.Remove(id);
        if (removed) _ui.RemovePanel(pluginId, id);
        return DocumentApi.Obj(w => w.WriteBoolean("removed", removed));
    }

    /// <summary>The user opened an item of a panel; the plugin hears about it. False when the panel or item is not (or no longer) there.</summary>
    public async Task<bool> ActivateItemAsync(string pluginId, string panelId, string itemId)
    {
        lock (_gate)
        {
            if (!_plugins.TryGetValue(pluginId, out var c) || !c.Panels.TryGetValue(panelId, out var ids) || !ids.Contains(itemId)) return false;
        }
        return await _send(pluginId, UiEvents.PanelActivated, DocumentApi.Obj(w => { w.WriteString("panel", panelId); w.WriteString("item", itemId); }));
    }

    // ---- prompts ----

    private async Task<byte[]> PickAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var itemsJson = DocumentApi.Need(p, "items");
        if (itemsJson.ValueKind != JsonValueKind.Array) throw DocumentApi.Invalid("Parameter 'items' must be an array.");
        if (itemsJson.GetArrayLength() > _options.MaxPickItems) throw Quota("items in a quick pick", _options.MaxPickItems);
        HashSet<string> ids = [];
        List<PickItem> items = [];
        foreach (var item in itemsJson.EnumerateArray())
        {
            var id = Id(item, "id");
            if (!ids.Add(id)) throw DocumentApi.Invalid($"The item id '{id}' is used twice.");
            items.Add(new PickItem(id, Text(item, "label"), OptionalText(item, "detail")));
        }
        var chosen = await _ui.PickAsync(pluginId, new QuickPickRequest(Text(p, "title"), OptionalText(p, "placeholder"), items), ct);
        return DocumentApi.Obj(w => { if (chosen is null) w.WriteNull("id"); else w.WriteString("id", chosen); });
    }

    // ---- tasks ----

    private byte[] StartTask(string pluginId, JsonElement p)
    {
        var title = Text(p, "title");
        var cancellable = p.TryGetProperty("cancellable", out var c) && c.ValueKind == JsonValueKind.True;
        var id = Guid.NewGuid().ToString("N");
        lock (_gate)
        {
            var tasks = Of(pluginId).Tasks;
            if (tasks.Count >= _options.MaxTasksPerPlugin) throw Quota("running tasks", _options.MaxTasksPerPlugin);
            tasks[id] = new TaskEntry(cancellable);
        }
        _ui.StartTask(pluginId, new TaskModel(id, title, cancellable));
        return DocumentApi.Obj(w => w.WriteString("task", id));
    }

    private void RequireTask(string pluginId, string id)
    {
        lock (_gate)
        {
            if (!_plugins.TryGetValue(pluginId, out var c) || !c.Tasks.ContainsKey(id))
                throw DocumentApi.Fail(UiErrorCodes.UnknownTask, "The task is unknown or has ended.");
        }
    }

    private byte[] TaskProgress(string pluginId, JsonElement p)
    {
        var id = DocumentApi.NeedString(p, "task");
        RequireTask(pluginId, id);
        double? fraction = null;
        if (p.TryGetProperty("fraction", out var f) && f.ValueKind != JsonValueKind.Null)
            fraction = f.ValueKind == JsonValueKind.Number && f.TryGetDouble(out var d) && d is >= 0 and <= 1 ? d : throw DocumentApi.Invalid("Parameter 'fraction' must be a number from 0 to 1.");
        _ui.UpdateTask(pluginId, id, new TaskProgressModel(fraction, OptionalText(p, "message")));
        return DocumentApi.Obj(w => w.WriteBoolean("ok", true));
    }

    private byte[] EndTask(string pluginId, JsonElement p)
    {
        var id = DocumentApi.NeedString(p, "task");
        bool ended;
        lock (_gate) ended = _plugins.TryGetValue(pluginId, out var c) && c.Tasks.Remove(id);
        if (ended) _ui.EndTask(pluginId, id);
        return DocumentApi.Obj(w => w.WriteBoolean("ended", ended));
    }

    /// <summary>
    /// The user pressed cancel; the plugin hears about it once and ends the task. A plugin that does not end it within <see cref="UiApiOptions.TaskCancelGrace"/>,
    /// or could not be told, loses it: the row goes, the quota slot is free and later calls about the task get <see cref="UiErrorCodes.UnknownTask"/>.
    /// False when there is nothing to cancel or the plugin could not be told.
    /// </summary>
    public async Task<bool> CancelTaskAsync(string pluginId, string taskId)
    {
        TaskEntry entry;
        lock (_gate)
        {
            if (!_plugins.TryGetValue(pluginId, out var c) || !c.Tasks.TryGetValue(taskId, out var t) || !t.Cancellable || t.CancelSent) return false;
            t.CancelSent = true;
            entry = t;
        }
        bool sent = await _send(pluginId, UiEvents.TaskCancelled, DocumentApi.Obj(w => w.WriteString("task", taskId)));
        _ = DropAsync(pluginId, taskId, entry, sent ? _options.TaskCancelGrace : TimeSpan.Zero);
        return sent;
    }

    private async Task DropAsync(string pluginId, string taskId, TaskEntry entry, TimeSpan after)
    {
        if (after > TimeSpan.Zero) await Task.Delay(after);
        bool dropped;
        lock (_gate) dropped = _plugins.TryGetValue(pluginId, out var c) && c.Tasks.TryGetValue(taskId, out var t) && ReferenceEquals(t, entry) && c.Tasks.Remove(taskId);
        if (dropped) _ui.EndTask(pluginId, taskId);
    }

    /// <summary>Whether the plugin shows a panel or runs a task now; both would vanish with its process.</summary>
    public bool HoldsState(string pluginId)
    {
        lock (_gate) return _plugins.TryGetValue(pluginId, out var c) && (c.Panels.Count > 0 || c.Tasks.Count > 0);
    }

    /// <summary>The plugin stopped (exit, crash, restart): its panels, status item, tasks and notices go away.</summary>
    public void PluginEnded(string pluginId)
    {
        lock (_gate) _plugins.Remove(pluginId);
        _ui.RemovePlugin(pluginId);
    }
}

namespace NksHub.Textida.PluginHost.Ui;

/// <summary>Method names of the UI contributions (requests from the plugin to the editor). The plugin sends data, the editor draws it.</summary>
public static class UiMethods
{
    public const string PanelSet = "ui/panel/set";
    public const string PanelRemove = "ui/panel/remove";
    public const string StatusSet = "ui/status/set";
    public const string StatusClear = "ui/status/clear";
    public const string QuickPick = "ui/quickPick";
    public const string Input = "ui/input";
    public const string Confirm = "ui/confirm";
    public const string Notify = "ui/notify";
    public const string TaskStart = "ui/task/start";
    public const string TaskProgress = "ui/task/progress";
    public const string TaskEnd = "ui/task/end";
}

/// <summary>Events (<c>$/event</c> payloads) the editor sends to a plugin about its UI.</summary>
public static class UiEvents
{
    /// <summary>The user opened an item of a panel (payload: <c>panel</c>, <c>item</c>).</summary>
    public const string PanelActivated = "ui/panelActivated";
    /// <summary>The user pressed cancel on a task (payload: <c>task</c>).</summary>
    public const string TaskCancelled = "ui/taskCancelled";
}

/// <summary>JSON-RPC error codes of the UI API (implementation-defined range -32000..-32099).</summary>
public static class UiErrorCodes
{
    public const int QuotaExceeded = -32030;
    public const int UnknownTask = -32031;
}

public enum PanelKind { List, Tree }

public enum NoticeLevel { Info, Warning, Error }

/// <param name="Children">Empty for a list; a list panel never has children.</param>
public sealed record PanelItem(string Id, string Label, string? Detail, IReadOnlyList<PanelItem> Children);

/// <param name="Reveal">The editor shows the panel at once, not only when the user opens it.</param>
public sealed record PanelModel(string Id, string Title, PanelKind Kind, IReadOnlyList<PanelItem> Items, bool Reveal);

public sealed record StatusModel(string Text, string? Tooltip);

public sealed record PickItem(string Id, string Label, string? Detail);

public sealed record QuickPickRequest(string Title, string? Placeholder, IReadOnlyList<PickItem> Items);

/// <param name="Secret">The editor hides what the user types (a token or a password); <paramref name="Value"/> is then ignored.</param>
public sealed record InputRequest(string Title, string Label, string Value, bool Secret = false);

public sealed record ConfirmRequest(string Title, string Message);

public sealed record TaskModel(string Id, string Title, bool Cancellable);

/// <param name="Fraction">0 to 1, or null when the plugin cannot tell how far it is.</param>
public sealed record TaskProgressModel(double? Fraction, string? Message);

/// <summary>
/// What the editor draws for plugins. Calls come from the plugin protocol threads, so an implementation moves them to the UI thread.
/// A prompt ends with null/false when the user dismisses it, and throws <see cref="OperationCanceledException"/> when <c>cancellation</c> fires (the plugin died).
/// </summary>
public interface IPluginUi
{
    void SetPanel(string pluginId, PanelModel panel);
    void RemovePanel(string pluginId, string panelId);
    /// <summary>At most one status item per plugin; null removes it.</summary>
    void SetStatus(string pluginId, StatusModel? status);
    /// <summary>The id of the chosen item, or null.</summary>
    Task<string?> PickAsync(string pluginId, QuickPickRequest request, CancellationToken cancellation);
    /// <summary>The entered text, or null.</summary>
    Task<string?> InputAsync(string pluginId, InputRequest request, CancellationToken cancellation);
    Task<bool> ConfirmAsync(string pluginId, ConfirmRequest request, CancellationToken cancellation);
    void Notify(string pluginId, NoticeLevel level, string message);
    void StartTask(string pluginId, TaskModel task);
    void UpdateTask(string pluginId, string taskId, TaskProgressModel progress);
    void EndTask(string pluginId, string taskId);
    /// <summary>The plugin ended: everything it contributed goes away.</summary>
    void RemovePlugin(string pluginId);
}

/// <summary>Quotas of the UI API. Everything is required: the numbers are an owner decision, not code defaults.</summary>
public sealed record UiApiOptions
{
    public required int MaxPanelsPerPlugin { get; init; }
    /// <summary>Items of one panel, children included.</summary>
    public required int MaxItemsPerPanel { get; init; }
    public required int MaxTreeDepth { get; init; }
    public required int MaxTasksPerPlugin { get; init; }
    /// <summary>Longest text of any label, title or message, in UTF-16 code units.</summary>
    public required int MaxTextLength { get; init; }
    public required int MaxPickItems { get; init; }
}

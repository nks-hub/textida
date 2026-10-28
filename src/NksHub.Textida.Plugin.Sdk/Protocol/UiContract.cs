namespace NksHub.Textida.PluginHost.Ui;

/// <summary>Method names of the UI contributions (requests from the plugin to the editor). The plugin sends data, the editor draws it. None of them needs a permission; the editor limits how much a plugin may contribute.</summary>
public static class UiMethods
{
    /// <summary>Sets (or replaces) a panel of the plugin: a list or a tree of items.</summary>
    public const string PanelSet = "ui/panel/set";
    /// <summary>Removes a panel of the plugin.</summary>
    public const string PanelRemove = "ui/panel/remove";
    /// <summary>Moves the mark of the current item of a panel that is shown, without sending its items again (params: <c>panel</c>, <c>current</c>, an item id or null for none).</summary>
    public const string PanelCurrent = "ui/panel/current";
    /// <summary>Sets the status bar item of the plugin (at most one).</summary>
    public const string StatusSet = "ui/status/set";
    /// <summary>Removes the status bar item of the plugin.</summary>
    public const string StatusClear = "ui/status/clear";
    /// <summary>Asks the user to pick one of a list of items; the answer holds the id of the chosen item, or null.</summary>
    public const string QuickPick = "ui/quickPick";
    /// <summary>Asks the user for a line of text; the answer holds the text, or null when the user dismissed the prompt.</summary>
    public const string Input = "ui/input";
    /// <summary>Asks the user a yes or no question.</summary>
    public const string Confirm = "ui/confirm";
    /// <summary>Shows a notice with a level in the window of the editor.</summary>
    public const string Notify = "ui/notify";
    /// <summary>Starts a task the user sees, with progress and optionally a cancel button; the answer holds the id of the task.</summary>
    public const string TaskStart = "ui/task/start";
    /// <summary>Reports how far a task is.</summary>
    public const string TaskProgress = "ui/task/progress";
    /// <summary>Ends a task.</summary>
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

/// <summary>JSON-RPC error codes of the UI API (implementation-defined range -32050..-32059; the document API owns -32020..-32049, so no number is shared).</summary>
public static class UiErrorCodes
{
    /// <summary>The plugin has as many panels, items, tasks or choices as the editor allows.</summary>
    public const int QuotaExceeded = -32050;
    /// <summary>The task does not exist (never started, already ended, or ended by the editor after the user cancelled it).</summary>
    public const int UnknownTask = -32051;
}

/// <summary>Level of a notice the plugin shows (<see cref="UiMethods.Notify"/>).</summary>
public enum NoticeLevel
{
    /// <summary>Information.</summary>
    Info,
    /// <summary>Something the user should know about.</summary>
    Warning,
    /// <summary>Something failed.</summary>
    Error,
}

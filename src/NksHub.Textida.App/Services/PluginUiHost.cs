using Avalonia.Threading;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.App.Services;

/// <summary>A panel as the windows draw it; <paramref name="PluginId"/> and the panel id are the key.</summary>
internal sealed record PluginPanel(string PluginId, PanelModel Model);

/// <summary>A running task of a plugin with its latest progress.</summary>
internal sealed record PluginTask(string PluginId, TaskModel Model, TaskProgressModel Progress);

/// <summary>
/// The editor's side of the plugin UI contributions: keeps what plugins asked to show (panels, one status item each, tasks) and puts prompts and
/// notices on the active window. Plugins only hand over data; the windows draw it. State is read and changed on the UI thread only.
/// </summary>
internal sealed class PluginUiHost(App app) : IPluginUi
{
    private readonly List<PluginPanel> _panels = [];
    private readonly Dictionary<string, StatusModel> _status = [];
    private readonly List<PluginTask> _tasks = [];

    /// <summary>Set by the composition root once the UI API exists; the windows report item activations and cancels through it.</summary>
    public UiApi? Api { get; set; }

    public IReadOnlyList<PluginPanel> Panels => _panels;
    public IReadOnlyDictionary<string, StatusModel> Status => _status;
    public IReadOnlyList<PluginTask> Tasks => _tasks;

    /// <summary>Anything shown by a plugin changed; windows draw it again.</summary>
    public event Action? Changed;

    /// <summary>A plugin asked for a panel to be shown at once.</summary>
    public event Action<string, string>? Revealed;

    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public void SetPanel(string pluginId, PanelModel panel) => OnUi(() =>
    {
        int at = _panels.FindIndex(p => p.PluginId == pluginId && p.Model.Id == panel.Id);
        var entry = new PluginPanel(pluginId, panel);
        if (at >= 0) _panels[at] = entry; else _panels.Add(entry);
        Changed?.Invoke();
        if (panel.Reveal) Revealed?.Invoke(pluginId, panel.Id);
    });

    public void RemovePanel(string pluginId, string panelId) => OnUi(() =>
    {
        if (_panels.RemoveAll(p => p.PluginId == pluginId && p.Model.Id == panelId) > 0) Changed?.Invoke();
    });

    public void SetStatus(string pluginId, StatusModel? status) => OnUi(() =>
    {
        if (status is null) _status.Remove(pluginId); else _status[pluginId] = status;
        Changed?.Invoke();
    });

    public void StartTask(string pluginId, TaskModel task) => OnUi(() =>
    {
        _tasks.Add(new PluginTask(pluginId, task, new TaskProgressModel(null, null)));
        Changed?.Invoke();
    });

    public void UpdateTask(string pluginId, string taskId, TaskProgressModel progress) => OnUi(() =>
    {
        int at = _tasks.FindIndex(t => t.PluginId == pluginId && t.Model.Id == taskId);
        if (at < 0) return;
        _tasks[at] = _tasks[at] with { Progress = progress };
        Changed?.Invoke();
    });

    public void EndTask(string pluginId, string taskId) => OnUi(() =>
    {
        if (_tasks.RemoveAll(t => t.PluginId == pluginId && t.Model.Id == taskId) > 0) Changed?.Invoke();
    });

    public void RemovePlugin(string pluginId) => OnUi(() =>
    {
        int removed = _panels.RemoveAll(p => p.PluginId == pluginId) + _tasks.RemoveAll(t => t.PluginId == pluginId) + (_status.Remove(pluginId) ? 1 : 0);
        app.ActiveWindow?.DismissNotice(NoticeKey(pluginId));
        if (removed > 0) Changed?.Invoke();
    });

    internal static string NoticeKey(string pluginId) => "plugin:" + pluginId;

    public void Notify(string pluginId, NoticeLevel level, string message) => OnUi(() =>
        app.ActiveWindow?.ShowNotice(NoticeKey(pluginId), message, [], warning: level != NoticeLevel.Info));

    public Task<string?> PickAsync(string pluginId, QuickPickRequest request, CancellationToken cancellation) =>
        OnWindowAsync<string?>(w => w.PluginPickAsync(request, cancellation), null, cancellation);

    public Task<string?> InputAsync(string pluginId, InputRequest request, CancellationToken cancellation) =>
        OnWindowAsync<string?>(w => app.Dialogs.PromptAsync(w, request.Title, request.Label, request.Value), null, cancellation);

    public Task<bool> ConfirmAsync(string pluginId, ConfirmRequest request, CancellationToken cancellation) =>
        OnWindowAsync(w => w.PluginConfirmAsync(request), false, cancellation);

    private async Task<T> OnWindowAsync<T>(Func<Shell.MainWindow, Task<T>> ask, T none, CancellationToken cancellation)
    {
        var task = Dispatcher.UIThread.InvokeAsync(() => app.ActiveWindow is { } w ? ask(w) : Task.FromResult(none));
        return await task.WaitAsync(cancellation);
    }
}

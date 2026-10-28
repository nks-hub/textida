using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using NksHub.Textida.Acp;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, Start Agent / Agent Panel / Share This Tab with Agent: a coding agent over ACP (<see cref="AcpService"/>). The panel under the editor, in the look of the output window, shows the
/// conversation and has the box for the next message; the permission questions of the agent are dialogs and what it writes goes to the change review. The panel
/// follows the service only while it is shown.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Border _agentPanel = new() { IsVisible = false, Height = 280 };
    private readonly TextBlock _agentStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _agentBoundary = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 4) };
    private readonly TextBlock _agentBoundaryNow = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 4), IsVisible = false };
    private readonly TextBox _agentText = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBox _agentInput = new() { MinHeight = 26 };
    private readonly Button _agentSend = new() { Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _agentCancel = new();
    private readonly Button _agentStop = new();
    private readonly Button _agentClose = new();
    private bool _agentFollowing, _agentDirty;

    /// <summary>The last start, send or stop the user began; tests wait for it.</summary>
    internal Task AgentTask { get; private set; } = Task.CompletedTask;

    /// <summary>The agents Start Agent offers; tests replace it to start the stand-in agent through the command.</summary>
    internal IReadOnlyList<AcpAdapter> AgentAdapters { get; set; } = AcpAdapters.All;

    internal string AgentBoundaryNowText => _agentBoundaryNow.Text ?? "";

    public bool AgentPanelOpen => _agentPanel.IsVisible;
    internal string AgentPanelText => _agentText.Text ?? "";
    internal string AgentPanelStatus => _agentStatus.Text ?? "";
    internal Control AgentPanelControl => _agentPanel;
    internal TextBlock AgentStatusControl => _agentStatus;
    internal TextBox AgentTranscriptBox => _agentText;
    internal IReadOnlyList<Button> AgentButtons => [_agentSend, _agentCancel, _agentStop, _agentClose];
    internal TextBox AgentInputBox => _agentInput;
    internal Button AgentSendButton => _agentSend;
    internal Button AgentCancelButton => _agentCancel;

    private Control BuildAgentPanel()
    {
        _agentStatus.Classes.Add("findcount");
        _agentBoundary.Classes.Add("findcount");
        _agentBoundaryNow.Classes.Add("findcount");
        _agentText.Classes.Add("outputtext");
        _agentInput.Classes.Add("findinput");
        foreach (var button in new[] { _agentSend, _agentCancel, _agentStop, _agentClose }) button.Classes.Add("outputaction");

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 8, 0), Children = { _agentCancel, _agentStop, _agentClose } };
        var header = new DockPanel { Height = 30, LastChildFill = false };
        header.Children.Add(Dock(actions, Avalonia.Controls.Dock.Right));
        header.Children.Add(Dock(_agentStatus, Avalonia.Controls.Dock.Left));
        var inputRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 6) };
        Grid.SetColumn(_agentSend, 1);
        inputRow.Children.Add(_agentInput);
        inputRow.Children.Add(_agentSend);

        _agentSend.Click += (_, _) => SendAgentMessage();
        _agentCancel.Click += (_, _) => CancelAgentTurn();
        _agentStop.Click += (_, _) => StopAgent();
        _agentClose.Click += (_, _) => ToggleAgentPanel(false);
        _agentPanel.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source == _agentInput)
            {
                SendAgentMessage();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ToggleAgentPanel(false);
                e.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _agentPanel.Classes.Add("outputpanel");
        HelpContext.Set(_agentPanel, "tools.agent");
        _agentPanel.Child = new DockPanel { Children = { Dock(header, Avalonia.Controls.Dock.Top), Dock(_agentBoundary, Avalonia.Controls.Dock.Top), Dock(_agentBoundaryNow, Avalonia.Controls.Dock.Top), Dock(inputRow, Avalonia.Controls.Dock.Bottom), _agentText } };
        return _agentPanel;
    }

    private void ApplyAgentTexts()
    {
        _agentBoundary.Text = Loc.Get(K.AgentBoundary);
        _agentSend.Content = Loc.Get(K.AgentSend);
        _agentCancel.Content = Loc.Get(K.AgentCancel);
        _agentStop.Content = Loc.Get(K.AgentStop);
        _agentClose.Content = Loc.Get(K.AgentClose);
        _agentInput.PlaceholderText = Loc.Get(K.AgentInputWatermark);
        _agentText.PlaceholderText = Loc.Get(K.AgentEmpty);
        AutomationProperties.SetName(_agentPanel, Loc.Get(K.A11yAgentPanel));
        AutomationProperties.SetName(_agentText, Loc.Get(K.A11yAgentTranscript));
        AutomationProperties.SetName(_agentStatus, Loc.Get(K.A11yAgentStatus));
        AutomationProperties.SetLiveSetting(_agentStatus, AutomationLiveSetting.Polite); // a screen reader announces "working" and "ready" as the turn goes on
        AutomationProperties.SetName(_agentInput, Loc.Get(K.A11yAgentInput));
        AutomationProperties.SetName(_agentSend, Loc.Get(K.AgentSend));
        AutomationProperties.SetName(_agentCancel, Loc.Get(K.AgentCancel));
        AutomationProperties.SetName(_agentStop, Loc.Get(K.AgentStop));
        AutomationProperties.SetName(_agentClose, Loc.Get(K.AgentClose));
        if (_agentPanel.IsVisible) RefreshAgent();
    }

    /// <summary>The window is closing: the panel stops following the service, and an agent that was started here, with this window as its place for questions, is stopped.</summary>
    private void ReleaseAgent()
    {
        FollowAgent(false);
        var acp = _app.Plugins.Acp;
        if (ReferenceEquals(acp.HostWindow, this) && acp.State != AgentState.None) _ = acp.StopAsync();
    }

    private void FollowAgent(bool follow)
    {
        if (follow == _agentFollowing) return;
        _agentFollowing = follow;
        if (follow) _app.Plugins.Acp.Changed += OnAgentChanged;
        else _app.Plugins.Acp.Changed -= OnAgentChanged;
    }

    private void OnAgentChanged()
    {
        if (_agentDirty) return;
        _agentDirty = true;
        Dispatcher.UIThread.Post(RefreshAgent, DispatcherPriority.Background);
    }

    public void ToggleAgentPanel(bool? open = null)
    {
        bool show = open ?? !AgentPanelOpen;
        if (show == AgentPanelOpen) return;
        _agentPanel.IsVisible = show;
        FollowAgent(show);
        if (show)
        {
            RefreshAgent();
            Dispatcher.UIThread.Post(() => _agentInput.Focus(), DispatcherPriority.Input);
        }
        else if (Active is { } tab) FocusView(tab);
    }

    private void RefreshAgent()
    {
        _agentDirty = false;
        var acp = _app.Plugins.Acp;
        var sb = new StringBuilder();
        foreach (var line in acp.Lines())
        {
            sb.Append(line.Role switch
            {
                AgentRole.You => Loc.Get(K.AgentLineYou, ("text", line.Text)),
                AgentRole.Agent => Loc.Get(K.AgentLineAgent, ("agent", acp.AgentName), ("text", line.Text)),
                AgentRole.Tool => Loc.Get(line.Outside ? K.AgentLineToolOutside : K.AgentLineTool, ("title", line.Text), ("status", ToolStatusText(line.Status))),
                AgentRole.Note => Loc.Get(K.AgentLineNote, ("text", line.Text)),
                _ => Loc.Get(K.AgentLineProblem, ("text", line.Text)),
            }).Append('\n');
        }
        _agentText.Text = sb.ToString();
        _agentText.CaretIndex = _agentText.Text.Length;
        _agentStatus.Text = acp.State switch
        {
            AgentState.Starting => Loc.Get(K.AgentStatusStarting, ("agent", acp.AgentName)),
            AgentState.Ready => Loc.Get(K.AgentStatusReady, ("agent", acp.AgentName)),
            AgentState.Working => Loc.Get(K.AgentStatusWorking, ("agent", acp.AgentName)),
            AgentState.Ended => Loc.Get(K.AgentStatusEnded, ("agent", acp.AgentName)),
            _ => Loc.Get(K.AgentStatusNone),
        };
        _agentBoundaryNow.Text = BoundaryText(acp);
        _agentBoundaryNow.IsVisible = _agentBoundaryNow.Text.Length > 0;
        _agentSend.IsEnabled = acp.State == AgentState.Ready;
        _agentCancel.IsEnabled = acp.TurnRunning;
        _agentStop.IsEnabled = acp.State != AgentState.None;
    }

    /// <summary>What the editor vouches for in this session, counted by the editor, and what the agent reported doing outside it. Empty when no agent of the table runs.</summary>
    private static string BoundaryText(AcpService acp)
    {
        if (acp.State == AgentState.None || acp.HostsPlugin) return "";
        var lines = new List<string>();
        if (acp.Adapter is { } adapter) lines.Add(Loc.Get(K.AgentBoundaryMode, ("agent", adapter.Name), ("reach", ReachText(adapter.OwnToolsReach))));
        var b = acp.Boundary();
        lines.Add(Loc.Get(K.AgentBoundaryCounts, ("reads", b.Reads), ("proposals", b.Proposals), ("applied", b.Applied), ("commands", b.Commands), ("other", b.OtherOutside)));
        return string.Join('\n', lines);
    }

    private static string ReachText(AcpReach reach)
    {
        var parts = new List<string>();
        if (reach.HasFlag(AcpReach.Files)) parts.Add(Loc.Get(K.AgentReachFiles));
        if (reach.HasFlag(AcpReach.Programs)) parts.Add(Loc.Get(K.AgentReachPrograms));
        if (reach.HasFlag(AcpReach.Network)) parts.Add(Loc.Get(K.AgentReachNetwork));
        return string.Join(", ", parts);
    }

    private static string ToolStatusText(string? status) => status switch
    {
        "pending" => Loc.Get(K.AgentToolPending),
        "in_progress" => Loc.Get(K.AgentToolInProgress),
        "completed" => Loc.Get(K.AgentToolCompleted),
        "failed" => Loc.Get(K.AgentToolFailed),
        "cancelled" => Loc.Get(K.AgentToolCancelled),
        _ => status ?? "",
    };

    // ---- commands ----

    internal CommandOutcome ToggleAgentPanelCommand()
    {
        ToggleAgentPanel();
        return CommandOutcome.Handled;
    }

    internal CommandOutcome StartAgent()
    {
        AgentTask = StartAgentAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>The folder the agent works in: the folder opened in the file panel, else the folder of the active file.</summary>
    private string? AgentFolder() => FileRootFolder ?? (Active?.Path is { } file ? Path.GetDirectoryName(file) : null);

    private async Task StartAgentAsync()
    {
        var acp = _app.Plugins.Acp;
        if (acp.IsRunning)
        {
            SetStatusMessage(Loc.Get(K.AgentStartRunning));
            ToggleAgentPanel(true);
            return;
        }
        var folder = AgentFolder();
        if (folder is null)
        {
            SetStatusMessage(Loc.Get(K.AgentStartNoFolder));
            return;
        }
        var adapters = AgentAdapters;
        var items = adapters.Select(a => new PickItem(a.Id, a.Name, a.CommandLine + " · " + AgentInstallState(a))).ToList();
        var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.AgentTitle), Loc.Get(K.AgentStartWatermark), items), CancellationToken.None);
        if (chosen is null || adapters.FirstOrDefault(a => a.Id == chosen) is not { } adapter) return;

        var (program, problem) = AcpLaunch.Find(adapter.Command);
        if (program is null)
        {
            var ok = new[] { new DialogButton(Loc.Get(K.DialogOk), IsDefault: true, IsCancel: true) };
            await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.AgentStartTitle, ("agent", adapter.Name)),
                Loc.Get(K.AgentStartMissing, ("agent", adapter.Name), ("command", adapter.Command), ("reason", problem ?? ""), ("repository", adapter.Repository)), ok));
            return;
        }
        var launch = new AcpLaunch(program, adapter.Args, folder);
        var buttons = new[] { new DialogButton(Loc.Get(K.AgentStartConfirm)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true) };
        var details = new[] { Loc.Get(K.AgentStartReads), Loc.Get(K.AgentStartWrites), Loc.Get(K.AgentStartOwn, ("agent", adapter.Name), ("reach", ReachText(adapter.OwnToolsReach))), Loc.Get(K.AgentStartMode, ("agent", adapter.Name)) };
        var command = new ToolInvocation(adapter.Id, program, adapter.Args, folder, null, "").CommandLine;
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.AgentStartTitle, ("agent", adapter.Name)), Loc.Get(K.AgentStartMessage, ("command", command), ("folder", folder)), buttons, details));
        if (answer != 0 || acp.IsRunning) return;

        ToggleAgentPanel(true);
        await acp.StartAsync(this, launch, adapter.Name, adapter);
    }

    /// <summary>The picker detail: whether the program is in PATH and, if it is, that no run of the test scenarios has passed for it.</summary>
    private static string AgentInstallState(AcpAdapter adapter) =>
        AcpLaunch.Find(adapter.Command).Path is null ? Loc.Get(K.AgentStartMissingInPath)
        : Loc.Get(K.AgentStartFound) + (adapter.IsVerified ? "" : " · " + Loc.Get(K.AgentStartUnverified));

    internal CommandOutcome ShareTabWithAgent()
    {
        if (Active is not { } tab) return CommandOutcome.NotHandled;
        var acp = _app.Plugins.Acp;
        if (!acp.IsRunning)
        {
            SetStatusMessage(Loc.Get(K.AgentShareNeedAgent));
            return CommandOutcome.Handled;
        }
        // an agent names a file by its path, so a tab without a file cannot be shared
        if (tab.Custom is not null || tab.Doc.IsResults || tab.Doc.Doc.FilePath is null)
        {
            SetStatusMessage(Loc.Get(K.AgentShareUnavailable));
            return CommandOutcome.Handled;
        }
        if (acp.IsShared(tab))
        {
            acp.Unshare(tab.Doc.Doc.Id);
            SetStatusMessage(Loc.Get(K.AgentShareOff, ("name", tab.Title)));
        }
        else
        {
            acp.Share(this, tab);
            SetStatusMessage(Loc.Get(K.AgentShareOn, ("name", tab.Title)));
        }
        return CommandOutcome.Handled;
    }

    internal CommandOutcome CancelAgentTurn()
    {
        _app.Plugins.Acp.CancelTurn();
        return CommandOutcome.Handled;
    }

    internal CommandOutcome StopAgent()
    {
        AgentTask = _app.Plugins.Acp.StopAsync();
        return CommandOutcome.Handled;
    }

    internal void SendAgentMessage()
    {
        var text = (_agentInput.Text ?? "").Trim();
        if (text.Length == 0) return;
        var acp = _app.Plugins.Acp;
        if (acp.State != AgentState.Ready)
        {
            SetStatusMessage(Loc.Get(acp.State == AgentState.Working ? K.AgentBusy : K.AgentShareNeedAgent));
            return;
        }
        _agentInput.Text = "";
        AgentTask = acp.SendAsync(text);
    }
}

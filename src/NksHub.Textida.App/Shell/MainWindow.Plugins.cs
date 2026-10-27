using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What plugins contribute, drawn from their data: a panel with a list or tree on the left, one status item per plugin and running tasks in the
/// status bar, quick pick, input, confirm and notices. A plugin never creates a control; it sends <see cref="PanelModel"/> and the like.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly DockPanel _pluginPanel = new() { IsVisible = false };
    private readonly TextBlock _pluginPanelTitle = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(12, 10, 12, 6), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _pluginPanelClose = new() { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TreeView _pluginTree = new() { Background = Brushes.Transparent, Focusable = true };
    private readonly StackPanel _pluginStatus = new() { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
    private (string Plugin, string Panel)? _pluginPanelKey;

    public bool PluginPanelOpen => _body.ColumnDefinitions[0].Width.Value > 0 && _pluginPanel.IsVisible;
    internal TreeView PluginTree => _pluginTree;
    internal Control PluginStatusHost => _pluginStatus;
    internal string? PluginPanelTitle => _pluginPanelKey is null ? null : _pluginPanelTitle.Text;

    private Control BuildPluginPanel()
    {
        _pluginPanelClose.Classes.Add("tabclose");
        _pluginPanelClose.Content = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,0 L8,8 M8,0 L0,8"), StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None };
        _pluginPanelClose.Click += (_, _) => ToggleLeft(LeftPanel.Plugin, false);
        var header = new DockPanel { Children = { Dock(_pluginPanelClose, Avalonia.Controls.Dock.Right), _pluginPanelTitle } };
        _pluginPanel.Children.Add(Dock(header, Avalonia.Controls.Dock.Top));
        _pluginPanel.Children.Add(_pluginTree);
        _pluginTree.DoubleTapped += (_, _) => ActivatePluginItem();
        _pluginTree.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            ActivatePluginItem();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _app.Plugins.Ui.Changed += RefreshPluginUi;
        _app.Plugins.Ui.Revealed += OnPluginPanelRevealed;
        return _pluginPanel;
    }

    private void ReleasePlugins()
    {
        _app.Plugins.Ui.Changed -= RefreshPluginUi;
        _app.Plugins.Ui.Revealed -= OnPluginPanelRevealed;
    }

    internal void ActivatePluginItem()
    {
        if (_pluginPanelKey is not { } key || _pluginTree.SelectedItem is not TreeViewItem { Tag: string item }) return;
        if (key.Panel.StartsWith(PluginServices.SymbolsPanelPrefix, StringComparison.Ordinal)) _app.Plugins.RevealSymbol(key.Plugin, key.Panel, item);
        else if (_app.Plugins.Ui.Api is { } api) _ = api.ActivateItemAsync(key.Plugin, key.Panel, item);
    }

    /// <summary>Selects the bytes of <paramref name="range"/> in the tab and scrolls to them; the outline and the plugin API both end here.</summary>
    internal bool RevealBytes(DocTab tab, ByteRange range)
    {
        if (tab.Surface is not { } surface || tab.Doc.TextBackend is not { } backend) return false;
        if (!backend.TryToCharOffset(range.Start, out int start) || !backend.TryToCharOffset(range.End, out int end)) return false;
        SelectTab(tab);
        surface.SetSelections([new Sel(start, end)]);
        return true;
    }

    /// <summary>Puts the carets a plugin computed (bytes of the current revision) into the tab; the last one that can be placed stays the primary.</summary>
    internal void PlaceSelections(DocTab tab, SelectionSet set)
    {
        if (tab.Surface is not { } surface || tab.Doc.TextBackend is not { } backend) return;
        var sels = new List<Sel>(set.Count);
        int primary = 0;
        for (int i = 0; i < set.Count; i++)
        {
            var s = set.Items[i];
            if (!backend.TryToCharOffset(s.Anchor, out int anchor) || !backend.TryToCharOffset(s.Caret, out int caret)) continue;
            if (i == set.Primary) primary = sels.Count;
            sels.Add(new Sel(anchor, caret));
        }
        if (sels.Count > 0) surface.SetCarets(Carets.Create(sels, primary));
    }

    private void OnPluginPanelRevealed(string plugin, string panel)
    {
        if (!ReferenceEquals(_app.ActiveWindow, this)) return;
        _pluginPanelKey = (plugin, panel);
        RefreshPluginUi();
        if (!PluginPanelOpen) ToggleLeft(LeftPanel.Plugin, true);
    }

    /// <summary>Draws the panel, the status items and the tasks again from the state the plugins have sent.</summary>
    private void RefreshPluginUi()
    {
        var ui = _app.Plugins.Ui;
        RefreshPluginPanel(ui);
        _pluginPanelClose.SetValue(ToolTip.TipProperty, Loc.Get(K.PluginPanelClose));
        AutomationProperties.SetName(_pluginPanelClose, Loc.Get(K.PluginPanelClose));

        _pluginStatus.Children.Clear();
        foreach (var (plugin, status) in ui.Status)
        {
            var item = new TextBlock { Text = status.Text, VerticalAlignment = VerticalAlignment.Center };
            if (status.Tooltip is not null) ToolTip.SetTip(item, status.Tooltip);
            AutomationProperties.SetName(item, status.Text);
            _pluginStatus.Children.Add(item);
        }
        foreach (var task in ui.Tasks) _pluginStatus.Children.Add(BuildTaskItem(task));
    }

    private void RefreshPluginPanel(PluginUiHost ui)
    {
        var shown = _pluginPanelKey is { } key ? ui.Panels.FirstOrDefault(p => p.PluginId == key.Plugin && p.Model.Id == key.Panel) : null;
        if (shown is null)
        {
            if (_pluginPanelKey is not null)
            {
                _pluginPanelKey = null;
                _pluginTree.Items.Clear();
                if (PluginPanelOpen) ToggleLeft(LeftPanel.Plugin, false);
            }
            return;
        }
        _pluginPanelTitle.Text = shown.Model.Title;
        string name = Loc.Get(K.A11yPluginPanel, ("title", shown.Model.Title));
        AutomationProperties.SetName(_pluginPanel, name);
        AutomationProperties.SetName(_pluginTree, name);
        _pluginTree.Items.Clear();
        foreach (var item in shown.Model.Items) _pluginTree.Items.Add(BuildPluginNode(item));
    }

    private static TreeViewItem BuildPluginNode(PanelItem item)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center });
        if (item.Detail is not null) header.Children.Add(new TextBlock { Text = item.Detail, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center });
        var node = new TreeViewItem { Header = header, Tag = item.Id, IsExpanded = true };
        AutomationProperties.SetName(node, item.Detail is null ? item.Label : item.Label + ", " + item.Detail);
        foreach (var child in item.Children) node.Items.Add(BuildPluginNode(child));
        return node;
    }

    private Control BuildTaskItem(PluginTask task)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var text = task.Progress.Message is { Length: > 0 } message ? task.Model.Title + ": " + message : task.Model.Title;
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var bar = new ProgressBar { Width = 80, Height = 4, Minimum = 0, Maximum = 1, IsIndeterminate = task.Progress.Fraction is null, Value = task.Progress.Fraction ?? 0, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(bar);
        AutomationProperties.SetName(row, task.Progress.Fraction is { } f
            ? Loc.Get(K.A11yPluginTask, ("title", task.Model.Title), ("percent", (int)Math.Round(f * 100)))
            : Loc.Get(K.A11yPluginTaskBusy, ("title", task.Model.Title)));
        if (task.Model.Cancellable)
        {
            var cancel = new Button { Content = Loc.Get(K.PluginTaskCancel), VerticalAlignment = VerticalAlignment.Center };
            cancel.Classes.Add("statusitem");
            AutomationProperties.SetName(cancel, Loc.Get(K.A11yPluginTaskCancel, ("title", task.Model.Title)));
            cancel.Click += (_, _) => { if (_app.Plugins.Ui.Api is { } api) _ = api.CancelTaskAsync(task.PluginId, task.Model.Id); };
            row.Children.Add(cancel);
        }
        return row;
    }

    // ---- prompts ----

    /// <summary>Quick pick in the palette; null when it is dismissed or <paramref name="cancellation"/> fires.</summary>
    internal Task<string?> PluginPickAsync(QuickPickRequest request, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rows = request.Items.Select(i => new QuickRow(i.Label, null, i.Detail, null, true, null, i.Detail is null ? i.Label : i.Label + ", " + i.Detail, () => answer.TrySetResult(i.Id))).ToList();
        ShowChoice(request.Placeholder ?? Loc.Get(K.PluginPickWatermark), request.Title.Length > 0 ? request.Title : Loc.Get(K.A11yPluginPick), rows, () => answer.TrySetResult(null));
        cancellation.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!answer.Task.IsCompleted && PaletteModeNow == PaletteMode.Choice) ClosePalette();
        }));
        return answer.Task;
    }

    /// <summary>Asks which of the other open YAML or JSON files to compare the keys of <paramref name="tab"/> with, and writes the keys each lacks to the output window.</summary>
    internal async Task CompareKeysAsync(DocTab tab)
    {
        var candidates = Tabs.Where(t => t.Surface is not null && !ReferenceEquals(t.Doc, tab.Doc) && PluginServices.KeySyntax(t.Doc.Language.Id) is not null)
            .GroupBy(t => t.Doc).Select(g => g.First()).ToList();
        if (candidates.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.KeysCompareNoOther));
            return;
        }
        var items = candidates.Select((t, i) => new PickItem(i.ToString(), t.Doc.Title, t.Path)).ToList();
        var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.KeysComparePickTitle), Loc.Get(K.KeysComparePickWatermark), items), CancellationToken.None);
        if (chosen is null || !int.TryParse(chosen, out int index)) return;
        await _app.Plugins.CompareKeysAsync(this, tab, candidates[index]);
    }

    internal async Task<bool> PluginConfirmAsync(ConfirmRequest request)
    {
        var buttons = new[] { new DialogButton(Loc.Get(K.DialogOk), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        return await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(request.Title, request.Message, buttons)) == 0;
    }

    /// <summary>Shows what a plugin proposes in the preview of the other whole-document actions and applies it when the user agrees.</summary>
    internal async Task ReviewPluginProposalAsync(EditProposal proposal, DocTab tab)
    {
        var preview = new ChangePreview(proposal.Revision, "plugin:" + proposal.PluginId, proposal.Edits, LossReport.None, false);
        if (await ConfirmPreviewAsync(tab, preview, Loc.Get(K.PluginProposal, ("title", proposal.Title ?? proposal.PluginId), ("changes", proposal.Edits.Count)))) Apply(tab, preview);
    }
}

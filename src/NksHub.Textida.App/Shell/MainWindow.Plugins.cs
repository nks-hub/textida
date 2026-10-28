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
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Mcp;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

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
    private readonly Dictionary<string, TextBlock> _pluginStatusItems = [];
    private readonly Dictionary<(string Plugin, string Task), StackPanel> _pluginTaskRows = [];
    private (string Plugin, string Panel)? _pluginPanelKey;
    private PluginPanel? _pluginPanelDrawn;

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
        else if (key.Panel == PluginServices.LspLocationsPanel) { if (PluginServices.ParsePlace(item) is { } place) GoToPlace(place.Path, place.Line, place.Character, place.EndLine, place.EndCharacter); }
        else if (key.Panel == PluginServices.GitStatusPanel)
        {
            // a path that is not there (a conflict both sides deleted, a file deleted in the tree) is the plugin's to answer: it can resolve the first
            if (File.Exists(item) || Directory.Exists(item) || _app.Plugins.Ui.Api is not { } git) GoToPlace(item, 0, 0, 0, 0);
            else _ = git.ActivateItemAsync(key.Plugin, key.Panel, item);
        }
        else if (_app.Plugins.Ui.Api is { } api) _ = api.ActivateItemAsync(key.Plugin, key.Panel, item);
    }

    /// <summary>The program of a language server the editor found, and the name of its entry in the catalog; <see cref="Declined"/> is set when the user said no or the path they set is wrong, and nothing may be started.</summary>
    internal sealed record LspProgram(string Name, string Path)
    {
        public bool Declined { get; init; }
    }

    /// <summary>
    /// Finds the program of the catalog's language server for <paramref name="language"/> the way every tool of the catalog is found: the path the user set as <c>lsp:&lt;language&gt;</c> in <c>toolPaths</c> of
    /// the saveActions.json of the profile, then the folders of the project (<c>node_modules/.bin</c>, <c>vendor/bin</c>, <c>.venv</c>) and the PATH. Nothing is downloaded. A program found in a folder of the project is
    /// a program the project brought, so the user is asked once before it runs, like for a linter (the answer is kept as long as the file stays the same). Null when no program of the language is found; the plugin then
    /// looks in the PATH itself and reports what it did not find.
    /// </summary>
    internal async Task<LspProgram?> LocateLspProgramAsync(DocTab tab, string language)
    {
        var file = tab.Doc.Doc.FilePath;
        var start = file is null ? null : Path.GetDirectoryName(Path.GetFullPath(file));
        var userPath = LoadSaveActions(file).ToolPaths?.GetValueOrDefault("lsp:" + language);
        if (!string.IsNullOrWhiteSpace(userPath) && !File.Exists(userPath) && !Directory.Exists(userPath))
        {
            SetStatusMessage(Loc.Get(K.LspToolPathMissing, ("language", language), ("path", userPath)));
            return new LspProgram("", userPath) { Declined = true };
        }
        foreach (var entry in LanguageCatalog.Lsp.Where(e => e.Language == language))
        {
            if (ToolLocator.Find(entry.Program, start, userPath) is not { } found) continue;
            bool brought = !string.Equals(found, ToolLocator.Find(entry.Program, null, userPath), PathComparison); // only a folder of the project has it
            if (brought && !await ConfirmLspProgramAsync(entry, found, start)) return new LspProgram(entry.Program, found) { Declined = true };
            return new LspProgram(entry.Program, found);
        }
        return null;
    }

    private async Task<bool> ConfirmLspProgramAsync(CatalogLsp entry, string program, string? folder)
    {
        var info = new FileInfo(program);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"lsp\0{entry.Language}\0{Path.GetFullPath(program)}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}\0{string.Join('\u0001', entry.Arguments)}")));
        if (_app.ToolApprovals.Has(fingerprint)) return true;
        var command = string.Join(' ', new[] { program }.Concat(entry.Arguments).Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        var request = new ChoiceRequest(Loc.Get(K.ToolConfirmTitle), Loc.Get(K.ToolConfirmMessage, ("title", Loc.Get(K.LspServerName, ("program", entry.Program)))),
            [new DialogButton(Loc.Get(K.ToolConfirmRun), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
            [Loc.Get(K.ToolConfirmCommand, ("command", command)), Loc.Get(K.ToolConfirmDirectory, ("directory", folder ?? ""))]);
        if (await _app.Dialogs.ChooseAsync(this, request) != 0) return false;
        _app.ToolApprovals.Approve(fingerprint);
        return true;
    }

    /// <summary>Selects the bytes of <paramref name="range"/> in the tab and scrolls to them; the outline and the plugin API both end here.</summary>
    internal bool RevealBytes(DocTab tab, ByteRange range)
    {
        if (tab.Paged is { } paged)
        {
            SelectTab(tab);
            paged.GoToOffset(range.Start);
            return true;
        }
        if (tab.Surface is not { } surface || tab.Doc.TextBackend is not { } backend) return false;
        if (!backend.TryToCharOffset(range.Start, out int start) || !backend.TryToCharOffset(range.End, out int end)) return false;
        SelectTab(tab);
        surface.SetSelections([new Sel(start, end)]);
        return true;
    }

    /// <summary>
    /// Opens the file of a place a language server named and selects it. Lines are 0-based and columns count UTF-16 units, which is how the text view counts characters,
    /// so the place is the one the server meant whatever the encoding of the file.
    /// </summary>
    internal void GoToPlace(string path, int line, int character, int endLine, int endCharacter)
    {
        try
        {
            var tab = OpenFile(path, null);
            if (tab.Surface is { } surface)
            {
                int At(int l, int c)
                {
                    var info = surface.Document.GetLineByNumber(Math.Clamp(l + 1, 1, surface.Document.LineCount));
                    return info.Offset + Math.Clamp(c, 0, info.Length);
                }
                surface.SetSelections([new Sel(At(line, character), At(endLine, endCharacter))]);
            }
            FocusView(tab);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(e.Message);
        }
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
        ShowPluginPanel(plugin, panel, focus: !PluginPanelOpen);
    }

    /// <summary>Shows a panel a plugin has sent in the left column; with <paramref name="focus"/> the keyboard goes to its selected (or first) item.</summary>
    internal void ShowPluginPanel(string plugin, string panel, bool focus = true)
    {
        _pluginPanelKey = (plugin, panel);
        RefreshPluginPanel(_app.Plugins.Ui);
        if (focus && _pluginPanelKey is not null) ToggleLeft(LeftPanel.Plugin, true);
    }

    /// <summary>The panels the plugins show now, in the quick list; the chosen one opens on the left.</summary>
    internal CommandOutcome ShowPluginPanels()
    {
        var plugins = _app.Plugins;
        var names = PluginServices.BuiltIns.ToDictionary(b => b.Id, b => b.Name, StringComparer.Ordinal);
        if (plugins.Ui.Panels.Any(p => !names.ContainsKey(p.PluginId)))
            foreach (var installed in plugins.Manager.Installed()) names.TryAdd(installed.Id, installed.Name);
        var rows = plugins.Ui.Panels.Select(p =>
        {
            string plugin = names.GetValueOrDefault(p.PluginId, p.PluginId);
            return new QuickRow(p.Model.Title, plugin, null, null, true, null, p.Model.Title + ", " + plugin, () => ShowPluginPanel(p.PluginId, p.Model.Id));
        }).ToList();
        // a plugin that lists onPanel:id is not running, so it has sent no panel yet: opening the row starts it and the panel opens as soon as the plugin shows it
        rows.AddRange(plugins.UnshownPanels().Select(d =>
            new QuickRow(d.PanelId, d.Name, Loc.Get(K.PluginPanelsStarts), null, true, null, d.PanelId + ", " + d.Name, () => _ = OpenDeclaredPluginPanelAsync(d.PluginId, d.PanelId, d.Name))));
        ShowChoice(Loc.Get(K.PluginPanelsWatermark), Loc.Get(K.A11yPluginPanels), rows, emptyText: Loc.Get(K.PluginPanelsEmpty));
        return CommandOutcome.Handled;
    }

    private (string Plugin, string Panel)? _pendingPluginPanel;

    private async Task OpenDeclaredPluginPanelAsync(string plugin, string panel, string name)
    {
        _pendingPluginPanel = (plugin, panel);
        if (await _app.Plugins.ActivatePanelAsync(panel) is { } why)
        {
            _pendingPluginPanel = null;
            await Dispatcher.UIThread.InvokeAsync(() => SetStatusMessage(Loc.Get(K.PluginUnavailable, ("name", name), ("reason", why))));
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() => ShowPendingPluginPanel(_app.Plugins.Ui));
    }

    /// <summary>Opens the panel the user asked for through <c>onPanel</c> once the plugin has sent it.</summary>
    private void ShowPendingPluginPanel(PluginUiHost ui)
    {
        if (_pendingPluginPanel is not { } pending || !ui.Panels.Any(p => p.PluginId == pending.Plugin && p.Model.Id == pending.Panel)) return;
        _pendingPluginPanel = null;
        ShowPluginPanel(pending.Plugin, pending.Panel);
    }

    /// <summary>Gives the keyboard to the panel: the selected item, else the first one, so the arrows and Enter work at once and a screen reader names an item.</summary>
    private void FocusPluginTree()
    {
        if (_pluginTree.SelectedItem is null && _pluginTree.Items.Count > 0) _pluginTree.SelectedItem = _pluginTree.Items[0];
        if (_pluginTree.SelectedItem is not TreeViewItem item || !item.Focus()) _pluginTree.Focus();
    }

    /// <summary>Draws the panel, the status items and the tasks again from the state the plugins have sent.</summary>
    private void RefreshPluginUi()
    {
        var ui = _app.Plugins.Ui;
        ShowPendingPluginPanel(ui);
        RefreshPluginPanel(ui);
        OnGitStatusChanged(ui);
        _pluginPanelClose.SetValue(ToolTip.TipProperty, Loc.Get(K.PluginPanelClose));
        AutomationProperties.SetName(_pluginPanelClose, Loc.Get(K.PluginPanelClose));

        // items and rows are updated in place: rebuilding them on every progress report would lose a click on Cancel that lands between two reports
        var shown = new List<Control>(ui.Status.Count + ui.Tasks.Count);
        foreach (var (plugin, status) in ui.Status)
        {
            if (!_pluginStatusItems.TryGetValue(plugin, out var item)) _pluginStatusItems[plugin] = item = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            item.Text = status.Text;
            ToolTip.SetTip(item, status.Tooltip);
            AutomationProperties.SetName(item, status.Text);
            shown.Add(item);
        }
        foreach (var gone in _pluginStatusItems.Keys.Where(k => !ui.Status.ContainsKey(k)).ToList()) _pluginStatusItems.Remove(gone);
        foreach (var task in ui.Tasks) shown.Add(PluginTaskRow(task));
        foreach (var gone in _pluginTaskRows.Keys.Where(k => !ui.Tasks.Any(t => t.PluginId == k.Plugin && t.Model.Id == k.Task)).ToList()) _pluginTaskRows.Remove(gone);
        if (_pluginStatus.Children.SequenceEqual(shown)) return;
        _pluginStatus.Children.Clear();
        _pluginStatus.Children.AddRange(shown);
    }

    private void RefreshPluginPanel(PluginUiHost ui)
    {
        var shown = _pluginPanelKey is { } key ? ui.Panels.FirstOrDefault(p => p.PluginId == key.Plugin && p.Model.Id == key.Panel) : null;
        if (shown is null)
        {
            if (_pluginPanelKey is not null)
            {
                _pluginPanelKey = null;
                _pluginPanelDrawn = null;
                _pluginCurrentNode = null;
                _pluginTree.Items.Clear();
                if (PluginPanelOpen) ToggleLeft(LeftPanel.Plugin, false);
            }
            return;
        }
        _pluginPanelTitle.Text = shown.Model.Title;
        string name = Loc.Get(K.A11yPluginPanel, ("title", shown.Model.Title));
        AutomationProperties.SetName(_pluginPanel, name);
        AutomationProperties.SetName(_pluginTree, name);
        if (ReferenceEquals(_pluginPanelDrawn?.Model, shown.Model)) return; // a status or task change: the tree keeps its selection and focus

        // new items of the same panel keep the selected item (by id) and the keyboard where they were
        bool samePanel = _pluginPanelDrawn is { } drawn && drawn.PluginId == shown.PluginId && drawn.Model.Id == shown.Model.Id;
        bool onlyMark = samePanel && ReferenceEquals(_pluginPanelDrawn!.Model.Items, shown.Model.Items); // the plugin moved the current item and sent no items
        string? selected = samePanel ? (_pluginTree.SelectedItem as TreeViewItem)?.Tag as string : null;
        bool focused = samePanel && _pluginTree.IsKeyboardFocusWithin;
        _pluginPanelDrawn = shown;
        if (!onlyMark)
        {
            _pluginCurrentNode = null;
            _pluginTree.Items.Clear();
            foreach (var item in shown.Model.Items) _pluginTree.Items.Add(BuildPluginNode(item));
            if (selected is not null && FindPluginNode(_pluginTree.Items, selected) is { } again) _pluginTree.SelectedItem = again;
            if (focused) FocusPluginTree();
        }
        MarkCurrentPluginItem(shown.Model.Current);
    }

    private TreeViewItem? _pluginCurrentNode;

    /// <summary>
    /// Marks the current item of the panel: its label in bold, and its accessible name says so. It is no selection and takes no focus, so a user who walks through the panel with the keyboard is not moved.
    /// The item is scrolled into view; an id the panel does not have takes the mark away.
    /// </summary>
    private void MarkCurrentPluginItem(string? id)
    {
        if (_pluginCurrentNode is { } old) SetPluginNodeMark(old, false);
        _pluginCurrentNode = id is null ? null : FindPluginNode(_pluginTree.Items, id);
        if (_pluginCurrentNode is not { } node) return;
        SetPluginNodeMark(node, true);
        node.BringIntoView();
    }

    private static void SetPluginNodeMark(TreeViewItem node, bool current)
    {
        if (node.Header is not StackPanel { Children: [TextBlock label, ..] } header) return;
        label.FontWeight = current ? FontWeight.Bold : FontWeight.Normal;
        string name = header.Children.Count > 1 && header.Children[1] is TextBlock { Text: { } detail } ? label.Text + ", " + detail : label.Text ?? "";
        AutomationProperties.SetName(node, current ? Loc.Get(K.A11yPluginCurrentItem, ("name", name)) : name);
    }

    private static TreeViewItem? FindPluginNode(ItemCollection items, string id)
    {
        foreach (var node in items.OfType<TreeViewItem>())
        {
            if ((string?)node.Tag == id) return node;
            if (FindPluginNode(node.Items, id) is { } found) return found;
        }
        return null;
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

    /// <summary>The row of a task: its text, a progress bar and, while it can still be cancelled, the Cancel button. A cancelled task says so until it ends.</summary>
    private StackPanel PluginTaskRow(PluginTask task)
    {
        var key = (task.PluginId, task.Model.Id);
        if (!_pluginTaskRows.TryGetValue(key, out var row))
        {
            row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new ProgressBar { Width = 80, Height = 4, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center });
            if (task.Model.Cancellable)
            {
                var cancel = new Button { VerticalAlignment = VerticalAlignment.Center };
                cancel.Classes.Add("statusitem");
                cancel.Click += (_, _) => _ = _app.Plugins.Ui.CancelAsync(key.PluginId, key.Id);
                row.Children.Add(cancel);
            }
            _pluginTaskRows[key] = row;
        }
        string title = task.Model.Title;
        var progress = task.Progress;
        string text = task.Cancelling ? Loc.Get(K.PluginTaskCancelling, ("title", title)) : progress.Message is { Length: > 0 } message ? title + ": " + message : title;
        ((TextBlock)row.Children[0]).Text = text;
        var bar = (ProgressBar)row.Children[1];
        bar.IsIndeterminate = progress.Fraction is null;
        bar.Value = progress.Fraction ?? 0;
        AutomationProperties.SetName(row, task.Cancelling ? text : progress.Fraction is { } f
            ? Loc.Get(K.A11yPluginTask, ("title", title), ("percent", (int)Math.Round(f * 100)))
            : Loc.Get(K.A11yPluginTaskBusy, ("title", title)));
        if (row.Children.Count > 2 && row.Children[2] is Button button)
        {
            button.Content = Loc.Get(K.PluginTaskCancel);
            AutomationProperties.SetName(button, Loc.Get(K.A11yPluginTaskCancel, ("title", title)));
            button.IsVisible = !task.Cancelling;
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

    // ---- help for the language ----

    /// <summary>The word under the caret, or the selection when it is one short run of word characters.</summary>
    private static string? WordUnderCaret(AvaloniaEdit.TextEditor editor)
    {
        if (editor.SelectedText is { Length: > 0 and <= 128 } selected && LanguageHelp.WordAt(selected, 0) == selected) return selected;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset);
        return LanguageHelp.WordAt(editor.Document.GetText(line.Offset, line.Length), editor.CaretOffset - line.Offset);
    }

    /// <summary>
    /// Alt+F1: looks up the word under the caret in the help sources the data plugins set for the language of the tab. A file of a plugin opens in the editor;
    /// a web address is shown first and opens in the browser only after the user agrees. With more than one source the user picks.
    /// </summary>
    internal async Task ShowLanguageHelpAsync()
    {
        if (Active is not { Editor: { } editor } tab) return;
        var mode = tab.Doc.Language;
        var sources = LanguageHelp.For(_app.Clips.Helps.Select(h => h.Source), mode.Id);
        if (sources.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.LangHelpNone, ("language", mode.Name.Length > 0 ? mode.Name : mode.Id)));
            return;
        }
        if (WordUnderCaret(editor) is not { } word)
        {
            SetStatusMessage(Loc.Get(K.LangHelpNoWord));
            return;
        }
        var source = sources[0];
        if (sources.Count > 1)
        {
            var items = sources.Select((s, i) => new PickItem(i.ToString(), s.Title, null)).ToList();
            var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.LangHelpPickTitle, ("word", word)), Loc.Get(K.LangHelpPickWatermark), items), CancellationToken.None);
            if (chosen is null || !int.TryParse(chosen, out int index)) return;
            source = sources[index];
        }
        if (source.Kind == HelpKind.File)
        {
            if (File.Exists(source.Target)) OpenFile(source.Target, null);
            return;
        }
        if (LanguageHelp.UrlFor(source, word) is not { } uri) return;
        if (!await PluginConfirmAsync(new ConfirmRequest(Loc.Get(K.LangHelpOpenTitle), Loc.Get(K.LangHelpOpenMessage, ("source", source.Title), ("url", uri.AbsoluteUri))))) return;
        bool opened = _app.UriOpener is { } open ? await open(uri) : await Launcher.LaunchUriAsync(uri);
        if (!opened) SetStatusMessage(Loc.Get(K.LangHelpOpenFailed, ("url", uri.AbsoluteUri)));
    }

    // ---- SQL dialect of a file ----

    /// <summary>Asks for the dialect of the SQL file in the active tab; the first row hands it back to the folder and the global setting.</summary>
    internal async Task ChooseSqlDialectAsync()
    {
        if (Active is not { } tab || tab.Doc.Language.Id != "sql") return;
        var items = new List<PickItem> { new("", Loc.Get(K.SqlDialectInherit), null) };
        items.AddRange(SqlDialects.All.Select(d => new PickItem(d.Id, d.Name, null)));
        var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.SqlDialectPickTitle), Loc.Get(K.SqlDialectPickWatermark), items), CancellationToken.None);
        if (chosen is null) return;
        tab.Doc.ChooseSqlDialect(chosen.Length == 0 ? null : chosen);
        var name = SqlDialects.All.First(d => d.Id == tab.Doc.SqlDialect).Name;
        SetStatusMessage(Loc.Get(chosen.Length == 0 ? K.SqlDialectInherited : K.SqlDialectSet, ("dialect", name)));
    }

    internal async Task<bool> PluginConfirmAsync(ConfirmRequest request)
    {
        var buttons = new[] { new DialogButton(Loc.Get(K.DialogOk), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true) };
        return await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(request.Title, request.Message, buttons)) == 0;
    }

    /// <summary>
    /// Shows what a plugin proposes in the change review (hunks with ticks in the Search Results tab) and answers when the user has decided. A proposal made for another
    /// revision than the document has now is stale and nothing is written. Edits that are not text of the document (raw bytes) or a hidden secrets file go to the
    /// byte inspector instead, which can only accept or reject the batch as a whole. When <paramref name="cancellation"/> fires either review closes and nothing is written.
    /// </summary>
    internal async Task<ProposalDecision> ReviewPluginProposalAsync(EditProposal proposal, DocTab tab, string id, CancellationToken cancellation)
    {
        var doc = tab.Doc.Doc;
        int total = proposal.Edits.Count;
        string title = proposal.Title ?? proposal.PluginId;
        if (cancellation.IsCancellationRequested) return new ProposalDecision(id, ProposalOutcome.Rejected, 0, total); // withdrawn before it was shown
        if (doc.Revision != proposal.Revision)
        {
            SetStatusMessage(Loc.Get(K.StatusChangeConflict));
            return new ProposalDecision(id, ProposalOutcome.Stale, 0, total);
        }
        if (total > 0 && !proposal.RawBytes && !tab.SecretsHidden && TextReplacements(doc, proposal.Edits) is { } replacements)
        {
            FileChange? change = null;
            try { change = ChangeBuilder.ForDocument(doc, replacements); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException) { }
            if (change is not null) return await ShowProposalAsync(id, title, "plugin:" + proposal.PluginId, tab, change, cancellation);
        }

        var report = doc.InspectEdits(proposal.Edits, "plugin:" + proposal.PluginId, proposal.Revision, proposal.RawBytes);
        if (report is null)
        {
            SetStatusMessage(Loc.Get(K.StatusChangeConflict));
            return new ProposalDecision(id, ProposalOutcome.Stale, 0, total);
        }
        var headline = proposal.PluginId == McpServer.ClientId
            ? Loc.Get(K.McpProposal, ("title", proposal.Title ?? Loc.Get(K.McpToolsTitle)), ("changes", total))
            : Loc.Get(K.PluginProposal, ("title", title), ("changes", total));
        // a plugin that crashed, restarted or lost its permission while the user decided has its batch dropped, as the review by hunks does
        var (answer, _) = await ConfirmInspectionAsync(tab.Doc, report, headline, Loc.Get(K.PreviewApply), cancellation);
        if (answer != LossAnswer.Replace) return new ProposalDecision(id, ProposalOutcome.Rejected, 0, total);
        report.Confirm();
        if (doc.Apply(report).Applied) return new ProposalDecision(id, ProposalOutcome.Applied, total, total);
        SetStatusMessage(Loc.Get(K.StatusChangeConflict));
        return new ProposalDecision(id, ProposalOutcome.Stale, 0, total);
    }

    /// <summary>
    /// Shows a workspace edit (text edits in several documents and files, file operations) in the same change review, one tick per change, and answers when the user has decided.
    /// A document that moved on since the plan was made makes the plan stale at once; nothing is written then.
    /// </summary>
    internal Task<ProposalDecision> ReviewWorkspaceEditAsync(ChangeProposal plan, string id, CancellationToken cancellation)
    {
        if (!IsStale(plan)) return ShowPlanAsync(id, plan, null, cancellation);
        SetStatusMessage(Loc.Get(K.StatusChangeConflict));
        return Task.FromResult(new ProposalDecision(id, ProposalOutcome.Stale, 0, plan.ChangeCount));
    }

    /// <summary>The edits as text of the document, or null when one of them is bytes that do not decode to text the codec writes back unchanged.</summary>
    private static List<TextReplacement>? TextReplacements(Document doc, IReadOnlyList<ByteEdit> edits)
    {
        var list = new List<TextReplacement>(edits.Count);
        foreach (var edit in edits)
        {
            var bytes = edit.Insert.ToArray();
            try
            {
                string text = doc.Codec.Decode(bytes).Text;
                if (!doc.Codec.Encode(text).AsSpan().SequenceEqual(bytes)) return null;
                list.Add(new TextReplacement(edit.Range, text));
            }
            catch (Exception e) when (e is ArgumentException or UnrepresentableCharactersException) { return null; }
        }
        return list;
    }
}

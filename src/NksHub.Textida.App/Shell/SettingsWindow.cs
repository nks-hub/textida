using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;
using KeyBinding = NksHub.Textida.Workbench.Keys.KeyBinding;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The settings: a tree of categories on the left and the page of the selected category on the right, with its settings in sections, each with its effective value, where the value comes from, a mark when
/// the user changed it and a way back to the default (for one setting and for the whole category). The search above the tree narrows the tree to the categories with a match, counts the matches in each, marks
/// them in the texts and lists the matches of every category on the page. A change is written to the user settings file and takes effect at once.
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    /// <summary>Prefix of the id of the node of a plugin; the name of the plugin follows.</summary>
    internal const string PluginNodePrefix = "plugin:";

    private readonly App _app;
    private readonly TextBox _search = new();
    private readonly TreeView _tree = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly StackPanel _page = new() { Spacing = 8, Margin = new Thickness(0, 0, 14, 0) };
    private readonly ScrollViewer _scroller = new();
    private readonly StackPanel _banners = new() { Spacing = 6 };
    private readonly TextBlock _count = new() { Opacity = 0.7, Margin = new Thickness(2, 0, 0, 0) };
    private readonly TextBlock _pageTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _resetCategory = new() { Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Control> _rows = [];
    private readonly List<string> _treeOrder = [];
    private readonly Dictionary<string, TreeViewItem> _treeItems = [];
    private readonly Dictionary<string, Control> _groups = [];
    private List<string> _pendingReset = [];
    private string _selected = SettingCategories.Appearance;
    private string? _focusKey;
    private string? _framed, _framedQuery;
    private bool _building;
    private bool _editing;

    public SettingsWindow(App app, string? query = null, string? key = null)
    {
        _app = app;
        Classes.Add("shell");
        Width = 1000;
        Height = 700;
        MinWidth = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var top = new StackPanel { Spacing = 8, Margin = new Thickness(18, 16, 18, 8), Children = { _search, _banners, _count } };
        var header = new DockPanel { Margin = new Thickness(2, 0, 14, 8) };
        DockPanel.SetDock(_resetCategory, Dock.Right);
        header.Children.Add(_resetCategory);
        header.Children.Add(_pageTitle);
        _scroller.Content = _page;
        _scroller.Margin = new Thickness(0, 0, 4, 12);
        var pageHost = new DockPanel { Margin = new Thickness(16, 0, 0, 0), Children = { Docked(header, Dock.Top), _scroller } };
        var treeHost = new Border { Child = _tree, Width = 270, Padding = new Thickness(6), BorderThickness = new Thickness(0, 0, 1, 0), Margin = new Thickness(18, 0, 0, 12) };
        treeHost.Classes.Add("settingtree");
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(pageHost, 1);
        body.Children.Add(treeHost);
        body.Children.Add(pageHost);
        Content = new DockPanel { Children = { Docked(top, Dock.Top), body } };

        _search.TextChanged += (_, _) => Refresh();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Down || _tree.SelectedItem is not TreeViewItem item) return;
            item.Focus();
            e.Handled = true;
        };
        _tree.SelectionChanged += (_, _) =>
        {
            if (_building || _tree.SelectedItem is not TreeViewItem { Tag: string id }) return;
            ChooseNode(id);
        };
        _resetCategory.Click += (_, _) => _ = ResetCategoryAsync();
        HelpContext.OnF1(this, "app.settings");
        app.Workbench.SettingChanged += OnSettingChanged;
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            app.Workbench.SettingChanged -= OnSettingChanged;
            Loc.Changed -= OnLanguageChanged;
        };
        ApplyTexts();
        _focusKey = key;
        if (query is not null) _search.Text = query;
        Refresh();
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private void OnSettingChanged(string key)
    {
        if (!_editing) RefreshKeepingPlace();
    }

    private void OnLanguageChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
        Refresh();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.SettingsTitle);
        _search.PlaceholderText = Loc.Get(K.SettingsSearchWatermark);
        AutomationProperties.SetName(_search, Loc.Get(K.SettingsSearchA11y));
        AutomationProperties.SetName(_tree, Loc.Get(K.SettingsTreeA11y));
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_pageTitle, AutomationLiveSetting.Polite);
        _resetCategory.Content = Loc.Get(K.SettingsResetCategory);
    }

    // ---- what the window offers to tests and to links ----

    internal void SetQuery(string text)
    {
        _search.Text = text;
        Refresh();
    }

    internal string Query => _search.Text ?? "";

    /// <summary>The id of the category the page shows (or, while searching, the one the tree points at).</summary>
    internal string SelectedCategory => _selected;

    /// <summary>The ids of the categories the tree shows, top to bottom.</summary>
    internal IReadOnlyList<string> TreeIds => [.. _treeOrder];

    internal string? TreeItemName(string id) => _treeItems.TryGetValue(id, out var item) ? AutomationProperties.GetName(item) : null;

    internal string TreeItemText(string id) => _treeItems.TryGetValue(id, out var item) && item.Header is TextBlock block ? block.Text ?? "" : "";

    internal string CountText => _count.Text ?? "";

    internal string PageTitleText => _pageTitle.Text ?? "";

    internal Button ResetCategoryButton => _resetCategory;

    /// <summary>Rows shown, for tests: setting keys and command ids.</summary>
    internal IReadOnlyList<string> ShownIds => [.. _rows.Select(c => c.Tag as string).OfType<string>()];

    internal Control? RowOf(string id) => _rows.FirstOrDefault(c => c.Tag as string == id);

    internal IEnumerable<Control> RowsOf(string id) => _rows.Where(c => c.Tag as string == id);

    internal IReadOnlyList<string> BannerTexts => [.. _banners.Children.OfType<TextBlock>().Select(t => t.Text ?? "")];

    /// <summary>Shows the page of a category.</summary>
    internal void SelectCategory(string id)
    {
        _framed = null;
        _selected = id;
        _search.Text = "";
        Refresh();
    }

    /// <summary>Leads to one setting (or shortcut): its category opens, the row is framed and brought into view. A search that would hide the row is cleared.</summary>
    internal void ShowKey(string key, bool keepSearch = false)
    {
        _focusKey = key;
        if (!keepSearch && (_search.Text ?? "").Length > 0) _search.Text = "";
        Refresh(); // the box reports its change later; the page is right now
    }

    // ---- the nodes of the tree ----

    private sealed record Node(string Id, string? Parent, string Title);

    private List<Node> Nodes()
    {
        var wb = _app.Workbench;
        var nodes = SettingCategories.All.Select(c => new Node(c.Id, c.Parent, wb.Strings.Get(c.Id))).ToList();
        foreach (var name in wb.Host.Settings.Definitions.Select(d => d.Category).Where(SettingCategories.IsPluginCategory).OfType<string>().Distinct())
            nodes.Add(new Node(PluginNodePrefix + name, SettingCategories.Plugins, wb.Strings.Get(name)));
        return nodes;
    }

    /// <summary>The node a setting is listed in: its category, or a node of its plugin.</summary>
    internal static string NodeOf(string? category) =>
        SettingCategories.IsKnown(category) ? category! : string.IsNullOrEmpty(category) ? SettingCategories.Advanced : PluginNodePrefix + category;

    private static bool IsUnder(IReadOnlyList<Node> nodes, string id, string ancestor)
    {
        for (var at = id; at is not null; at = nodes.FirstOrDefault(n => n.Id == at)?.Parent)
            if (at == ancestor) return true;
        return false;
    }

    private static string PathText(IReadOnlyList<Node> nodes, string id)
    {
        var parts = new List<string>();
        for (var at = id; at is not null; at = nodes.FirstOrDefault(n => n.Id == at)?.Parent) parts.Insert(0, nodes.FirstOrDefault(n => n.Id == at)?.Title ?? at);
        return string.Join(" › ", parts);
    }

    // ---- building ----

    private void RefreshKeepingPlace()
    {
        var offset = _scroller.Offset;
        Refresh();
        Dispatcher.UIThread.Post(() => _scroller.Offset = offset, DispatcherPriority.Background);
    }

    /// <summary>Builds the tree and the page again from the settings, the shortcuts and the search; <paramref name="tree"/> false leaves the tree alone (the user just chose a node of it).</summary>
    internal void Refresh(bool tree = true)
    {
        var wb = _app.Workbench;
        string query = _search.Text ?? "";
        var words = SearchHighlight.Words(query);
        bool searching = words.Count > 0;
        var nodes = Nodes();
        // the setting a link leads to needs its page
        if (_focusKey is { } wanted && !searching) _selected = NodeFor(wanted);

        var hits = wb.SearchIndex.Search(query, wb.Settings, wb.Commands, wb.Keymap, allKeys: !searching);
        var byNode = new Dictionary<string, List<SearchHit>>(StringComparer.Ordinal);
        var order = wb.Host.Settings.Definitions.Select((d, i) => (d.Key, i)).ToDictionary(p => p.Key, p => p.i, StringComparer.Ordinal);
        foreach (var hit in hits.OrderBy(h => h.Kind == SearchHitKind.Setting && order.TryGetValue(h.Id, out var i) ? i : int.MaxValue))
        {
            string node = hit.Kind == SearchHitKind.Keybinding ? SettingCategories.Keys : NodeOf(hit.CategoryId);
            if (!byNode.TryGetValue(node, out var list)) byNode[node] = list = [];
            list.Add(hit);
        }
        bool systemHit = (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && MatchesSystemSection(query);

        // the tree: while searching the categories with a match (and the ones above them), otherwise the ones that hold something
        var visible = new HashSet<string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in byNode.Keys.Append(SettingCategories.System).Distinct())
        {
            int count = (byNode.TryGetValue(node, out var list) ? list.Count : 0) + (searching && systemHit && node == SettingCategories.System ? 1 : 0);
            if (count == 0) continue;
            for (var at = node; at is not null; at = nodes.FirstOrDefault(n => n.Id == at)?.Parent)
            {
                visible.Add(at);
                counts[at] = counts.GetValueOrDefault(at) + count;
            }
        }
        if (!searching)
        {
            visible.Add(SettingCategories.Keys);
            visible.Add(SettingCategories.System);
        }
        if (visible.Count > 0 && !visible.Contains(_selected)) _selected = nodes.First(n => visible.Contains(n.Id)).Id;

        if (tree) BuildTree(nodes, visible, searching ? counts : null);
        BuildPage(nodes, byNode, words, searching, systemHit);
        BuildBanners();
        if (_focusKey is { } key)
        {
            _focusKey = null;
            (_framed, _framedQuery) = (key, query);
            FrameRow(key, focus: true);
        }
        else if (_framed is { } framed)
        {
            // the frame stays while the page is the same search; the box that reports a change later and a changed setting build the page again
            if (query == _framedQuery) FrameRow(framed, focus: false);
            else _framed = null;
        }
    }

    private string NodeFor(string key)
    {
        var wb = _app.Workbench;
        if (wb.Host.Settings.TryGet(key, out var definition)) return NodeOf(definition.Category);
        return wb.Commands.TryGet(key, out _) ? SettingCategories.Keys : _selected;
    }

    private void BuildTree(IReadOnlyList<Node> nodes, HashSet<string> visible, Dictionary<string, int>? counts)
    {
        _building = true;
        _tree.Items.Clear();
        _treeItems.Clear();
        _treeOrder.Clear();
        foreach (var node in nodes.Where(n => n.Parent is null && visible.Contains(n.Id))) _tree.Items.Add(TreeItemFor(nodes, visible, counts, node));
        if (_treeItems.TryGetValue(_selected, out var selected)) _tree.SelectedItem = selected;
        _building = false;
    }

    private TreeViewItem TreeItemFor(IReadOnlyList<Node> nodes, HashSet<string> visible, Dictionary<string, int>? counts, Node node)
    {
        string text = counts is null ? node.Title : $"{node.Title} ({counts.GetValueOrDefault(node.Id)})";
        var item = new TreeViewItem { Header = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Tag = node.Id, IsExpanded = true };
        AutomationProperties.SetName(item, counts is null ? node.Title : Loc.Plural(K.SettingsTreeCount, counts.GetValueOrDefault(node.Id), ("category", node.Title)));
        _treeItems[node.Id] = item;
        _treeOrder.Add(node.Id);
        foreach (var child in nodes.Where(n => n.Parent == node.Id && visible.Contains(n.Id))) item.Items.Add(TreeItemFor(nodes, visible, counts, child));
        return item;
    }

    /// <summary>The user chose a node of the tree: its page opens, or, among search results, the list scrolls to its group.</summary>
    private void ChooseNode(string id)
    {
        _selected = id;
        if ((_search.Text ?? "").Trim().Length > 0)
        {
            if (_groups.TryGetValue(id, out var group)) group.BringIntoView();
            return;
        }
        Refresh(tree: false);
    }

    private void BuildPage(IReadOnlyList<Node> nodes, Dictionary<string, List<SearchHit>> byNode, IReadOnlyList<string> words, bool searching, bool systemHit)
    {
        _page.Children.Clear();
        _rows.Clear();
        _groups.Clear();
        // the nodes whose rows are listed, in the order of the tree: the selected one and what is under it, or every node with a match
        bool IsShown(Node n) => (byNode.ContainsKey(n.Id) && (searching || IsUnder(nodes, n.Id, _selected)))
            || (n.Id == SettingCategories.System && (searching ? systemHit : IsUnder(nodes, n.Id, _selected)));
        var shown = nodes.Where(IsShown).ToList();
        string selectedTitle = nodes.FirstOrDefault(n => n.Id == _selected)?.Title ?? "";
        string path = PathText(nodes, _selected);
        _pageTitle.Text = searching ? "" : path;
        _pageTitle.IsVisible = !searching;
        AutomationProperties.SetName(_scroller, Loc.Get(K.SettingsPageA11y, ("category", searching ? selectedTitle : path)));

        var changed = new List<string>();
        foreach (var node in shown)
        {
            if (shown.Count > 1) _page.Children.Add(GroupHeader(node, PathText(nodes, node.Id)));
            var (list, sections) = Sectioned(byNode.GetValueOrDefault(node.Id) ?? []);
            string? section = null;
            foreach (var hit in list)
            {
                string name = sections.GetValueOrDefault(hit.Id, "");
                if (sections.Count > 0 && name != section)
                {
                    section = name;
                    _page.Children.Add(SectionHeader(name));
                }
                var row = hit.Kind == SearchHitKind.Setting ? SettingRow(hit, words, changed) : KeyRow(hit, words);
                _page.Children.Add(row);
                _rows.Add(row);
            }
            if (node.Id == SettingCategories.System && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && systemHit)
            {
                var system = SystemSection();
                _page.Children.Add(system);
                _rows.Add(system);
            }
        }
        if (_rows.Count == 0)
        {
            string empty = searching ? Loc.Get(K.SettingsNoResults) : _selected == SettingCategories.Plugins ? Loc.Get(K.SettingsPluginsEmpty) : "";
            if (empty.Length > 0) _page.Children.Add(new TextBlock { Text = empty, Opacity = 0.8, Margin = new Thickness(2, 8) });
        }
        _count.Text = Loc.Plural(K.SettingsResultCount, _rows.Count);
        _resetCategory.IsVisible = !searching && changed.Count > 0;
        AutomationProperties.SetName(_resetCategory, Loc.Get(K.SettingsResetCategoryA11y, ("category", selectedTitle)));
        _pendingReset = changed;
    }

    private TextBlock GroupHeader(Node node, string text)
    {
        var header = new TextBlock { Text = text, Margin = new Thickness(2, 10, 0, 2), TextWrapping = TextWrapping.Wrap };
        header.Classes.Add("settinggroup");
        AutomationProperties.SetName(header, text);
        _groups[node.Id] = header;
        return header;
    }

    private static TextBlock SectionHeader(string name)
    {
        var header = new TextBlock { Text = name, Margin = new Thickness(2, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        header.Classes.Add("settingsection");
        return header;
    }

    /// <summary>The prefixes of keys that name a section of their own, with the text of the section.</summary>
    private static readonly (string Prefix, string TextKey)[] SectionTexts =
    [
        ("editor.occurrences.", K.SettingsSectionOccurrences),
        ("editor.smartKeys.", K.SettingsSectionSmartKeys),
        ("tools.", K.SettingsSectionTools),
    ];

    /// <summary>
    /// The settings of a category in sections: a prefix of <see cref="SectionTexts"/> makes a section when at least three settings of the category share it, the others make the general section, which
    /// comes first; the others follow in the order of the table. Nothing is divided (the second value is empty) when no prefix qualifies or no setting is left for the general section.
    /// </summary>
    private static (List<SearchHit> Hits, Dictionary<string, string> Sections) Sectioned(List<SearchHit> hits)
    {
        bool InSection((string Prefix, string TextKey) s, SearchHit h) => h.Kind == SearchHitKind.Setting && h.Id.StartsWith(s.Prefix, StringComparison.Ordinal);
        var named = SectionTexts.Where(s => hits.Count(h => InSection(s, h)) >= 3).ToList();
        if (named.Count == 0 || hits.All(h => named.Any(s => InSection(s, h)))) return (hits, []);
        int Rank(SearchHit h) => named.FindIndex(s => InSection(s, h)) + 1;
        var ordered = hits.OrderBy(Rank).ToList();
        var sections = ordered.ToDictionary(h => h.Id, h => Rank(h) == 0 ? Loc.Get(K.SettingsSectionGeneral) : Loc.Get(named[Rank(h) - 1].TextKey), StringComparer.Ordinal);
        return (ordered, sections);
    }

    private void BuildBanners()
    {
        var wb = _app.Workbench;
        _banners.Children.Clear();
        if (wb.SettingsProblem is { } problem) _banners.Children.Add(Banner(Loc.Get(K.SettingsFileProblem, ("reason", problem))));
        if (wb.KeymapProblem is { } keymapProblem) _banners.Children.Add(Banner(Loc.Get(K.SettingsKeymapProblem, ("reason", keymapProblem))));
        foreach (var c in wb.Keymap.Conflicts)
        {
            string Title(KeyBinding b) => wb.Commands.TryGet(b.CommandId, out var cmd) ? wb.Strings.Get(cmd.TitleKey) : b.CommandId;
            var kind = c.Kind switch { KeyConflictKind.SameLayer => K.ConflictSameLayer, KeyConflictKind.Prefix => K.ConflictPrefix, _ => K.ConflictOverridden };
            _banners.Children.Add(Banner(Loc.Get(K.SettingsConflict, ("shortcut", c.Winner.Sequence.Display(wb.Platform)), ("winner", Title(c.Winner)), ("loser", Title(c.Loser)), ("kind", Loc.Get(kind))), c.IsAmbiguous));
        }
    }

    private static TextBlock Banner(string text, bool strong = true) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = strong ? ThemeColors.Danger : null, Opacity = strong ? 1 : 0.7 };

    /// <summary>Frames the row of a setting a link led to, brings it into view and moves the keyboard to its editor.</summary>
    private void FrameRow(string key, bool focus)
    {
        if (RowOf(key) is not { } row) return;
        row.Classes.Add("found");
        string name = AutomationProperties.GetName(row);
        if (focus)
            Dispatcher.UIThread.Post(() =>
            {
                row.BringIntoView();
                FindEditor(row)?.Focus();
            }, DispatcherPriority.Loaded);
        _count.Text = Loc.Get(K.SettingsFound, ("title", name.Length > 0 ? name : key), ("category", PathText(Nodes(), _selected)));
    }

    private static InputElement? FindEditor(Control row)
    {
        foreach (var visual in row.GetVisualDescendants())
            if (visual is TextBox or CheckBox or ComboBox && visual is InputElement { Focusable: true, IsEnabled: true } input) return input;
        return null;
    }

    // ---- the way back to the defaults ----

    /// <summary>
    /// Takes the values the user changed in a category (and in the categories under it) back to their defaults, after asking. The values set per language or by a profile or an EditorConfig file stay.
    /// Returns how many settings went back.
    /// </summary>
    internal async Task<int> ResetCategoryAsync(string? id = null)
    {
        var nodes = Nodes();
        string target = id ?? _selected;
        var keys = id is null ? _pendingReset : ChangedKeys(nodes, target);
        if (keys.Count == 0) return 0;
        string title = nodes.FirstOrDefault(n => n.Id == target)?.Title ?? target;
        var buttons = new List<DialogButton> { new(Loc.Get(K.SettingsResetCategory), IsDefault: true), new(Loc.Get(K.DialogCancel), IsCancel: true) };
        int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.SettingsResetCategory), Loc.Plural(K.SettingsResetCategoryAsk, keys.Count, ("category", title)), buttons));
        if (answer != 0) return 0;
        int done = 0;
        _editing = true;
        try
        {
            foreach (var key in keys)
                if (_app.Workbench.Set(key, null) is null) done++;
        }
        finally { _editing = false; }
        RefreshKeepingPlace();
        return done;
    }

    private List<string> ChangedKeys(IReadOnlyList<Node> nodes, string target)
    {
        var wb = _app.Workbench;
        return [.. wb.Host.Settings.Definitions.Where(d => IsUnder(nodes, NodeOf(d.Category), target) && wb.Settings.Resolve(d.Key) is { Source: SettingSource.User or SettingSource.UserLanguage }).Select(d => d.Key)];
    }
}

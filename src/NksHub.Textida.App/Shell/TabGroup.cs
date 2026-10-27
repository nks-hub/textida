using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>One group of tabs: a strip of headers and the content of the selected tab. A window holds one or more groups side by side.</summary>
internal sealed class TabGroup : DockPanel
{
    private static readonly Geometry CloseGlyph = Geometry.Parse("M0,0 L8,8 M8,0 L0,8");
    internal static readonly DataFormat<DocTab> TabFormat = DataFormat.CreateInProcessFormat<DocTab>("textida.tab");
    private const double DragThreshold = 8;

    private readonly ContentControl _content = new();
    private bool _syncing;
    private PointerPressedEventArgs? _pressed;
    private DocTab? _pressedTab;
    private readonly List<DocTab> _tabs = [];

    public TabGroup()
    {
        Strip = new TabStrip { Background = Brushes.Transparent, ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal }), ClipToBounds = true };
        StripHost = new Border { Padding = new Thickness(8, 5, 8, 0), Child = Strip };
        StripHost.Classes.Add("tabrow");
        SetDock(StripHost, Dock.Top);
        Children.Add(StripHost);
        Children.Add(_content);
        Strip.SelectionChanged += (_, _) =>
        {
            int i = Strip.SelectedIndex;
            if (_syncing || i < 0 || i >= _tabs.Count) return;
            Active = _tabs[i];
            Show(Active);
            Activated?.Invoke(this, Active);
        };
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(PointerPressedEvent, (_, _) => Focused?.Invoke(this), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Classes.Add("tabgroup");
    }

    public TabStrip Strip { get; }
    public Border StripHost { get; }
    public IReadOnlyList<DocTab> Tabs => _tabs;
    public DocTab? Active { get; private set; }
    public Control? Empty { get => _emptyContent; set { _emptyContent = value; if (Active is null) _content.Content = value; } }
    private Control? _emptyContent;

    /// <summary>Menu actions of a tab, supplied by the window; only the ones that apply are listed.</summary>
    public Func<DocTab, IReadOnlyList<(string Label, Action Run)>>? ContextActions { get; set; }

    public event Action<TabGroup, DocTab>? Activated;
    public event Action<DocTab>? CloseRequested;
    public event Action<TabGroup, DocTab, int>? TabDropped;
    public event Action<TabGroup, IReadOnlyList<string>>? FilesDropped;
    public event Action<TabGroup>? Focused;

    // ---- model ----

    public void Add(DocTab tab, int? index = null)
    {
        tab.Group = this;
        _tabs.Insert(Math.Clamp(index ?? _tabs.Count, 0, _tabs.Count), tab);
        Select(tab);
    }

    public void Remove(DocTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;
        _tabs.RemoveAt(index);
        tab.Group = null;
        if (Active == tab)
        {
            Active = _tabs.Count == 0 ? null : _tabs[Math.Min(index, _tabs.Count - 1)];
            Show(Active);
        }
        Refresh();
    }

    public void Select(DocTab tab)
    {
        Active = tab;
        Show(tab);
        Refresh();
    }

    private void Show(DocTab? tab)
    {
        if (tab is null) { _content.Content = _emptyContent; return; }
        if (!ReferenceEquals(_content.Content, tab.Root)) _content.Content = tab.Root;
    }

    // ---- strip ----

    public void Refresh()
    {
        _syncing = true;
        Strip.Items.Clear();
        foreach (var tab in _tabs) Strip.Items.Add(MakeItem(tab));
        Strip.SelectedIndex = Active is null ? -1 : _tabs.IndexOf(Active);
        _syncing = false;
    }

    public static string HeaderStateOf(DocTab tab) => $"{tab.Title}|{tab.Doc.HasUnsavedWork}|{tab.Doc.SizeBadge}|{tab.Doc.ReviewBadge}";

    private TabStripItem MakeItem(DocTab tab)
    {
        bool modified = tab.Doc.HasUnsavedWork;
        tab.HeaderState = HeaderStateOf(tab);
        var title = new TextBlock { Text = tab.Title, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = new Avalonia.Controls.Shapes.Path { Data = CloseGlyph, StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None } };
        close.Classes.Add("tabclose");
        AutomationProperties.SetName(close, Loc.Get(K.TabCloseA11y, ("title", tab.Title)));
        close.Click += (_, e) => { CloseRequested?.Invoke(tab); e.Handled = true; };
        var slot = new Panel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dot.Classes.Add("modifieddot");
        AutomationProperties.SetName(dot, Loc.Get(K.TabModifiedA11y));
        slot.Children.Add(close);
        slot.Children.Add(dot);
        slot.Classes.Add("tabslot");
        if (modified) slot.Classes.Add("modified");

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 6, 0), Children = { title } };
        if (tab.Doc.SizeBadge is { } badge)
        {
            var badgeText = new TextBlock { Text = badge, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var border = new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0), Child = badgeText };
            border.Classes.Add("sizebadge");
            header.Children.Add(border);
        }
        if (tab.Doc.ReviewBadge is { } review)
        {
            var reviewText = new TextBlock { Text = review, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            var border = new Border { Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0), Child = reviewText };
            border.Classes.Add("sizebadge");
            AutomationProperties.SetName(border, Loc.Get(K.ReviewBar));
            header.Children.Add(border);
        }
        header.Children.Add(slot);

        var item = new TabStripItem { Content = header };
        AutomationProperties.SetName(item, modified ? Loc.Get(K.TabModifiedName, ("title", tab.Title)) : tab.Title);
        item.PointerPressed += (_, e) => OnItemPressed(tab, e);
        item.PointerMoved += (_, e) => OnItemMoved(tab, e);
        item.PointerReleased += (_, _) => { _pressed = null; _pressedTab = null; };
        item.AddHandler(PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            if (e.GetCurrentPoint(item).Properties.IsMiddleButtonPressed) { CloseRequested?.Invoke(tab); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        if (ContextActions?.Invoke(tab) is { Count: > 0 } actions)
        {
            var menu = new ContextMenu();
            foreach (var (label, run) in actions)
            {
                var mi = new MenuItem { Header = label };
                mi.Click += (_, _) => run();
                menu.Items.Add(mi);
            }
            item.ContextMenu = menu;
        }
        return item;
    }

    // ---- drag and drop of tabs ----

    private void OnItemPressed(DocTab tab, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        _pressed = e;
        _pressedTab = tab;
    }

    private async void OnItemMoved(DocTab tab, PointerEventArgs e)
    {
        if (_pressed is not { } pressed || _pressedTab != tab || !e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        var from = pressed.GetPosition(null);
        var now = e.GetPosition(null);
        if (Math.Abs(now.X - from.X) + Math.Abs(now.Y - from.Y) < DragThreshold) return;
        _pressed = null;
        _pressedTab = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(TabFormat, tab));
        await DragDrop.DoDragDropAsync(pressed, transfer, DragDropEffects.Move);
    }

    private static DocTab? DraggedTab(DragEventArgs e) => e.DataTransfer.TryGetValue(TabFormat);

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DraggedTab(e) is not null ? DragDropEffects.Move : e.DataTransfer.TryGetFiles() is { Length: > 0 } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DraggedTab(e) is { } tab)
        {
            TabDropped?.Invoke(this, tab, DropIndex(e.GetPosition(Strip)));
            e.Handled = true;
        }
        else if (e.DataTransfer.TryGetFiles() is { Length: > 0 } files)
        {
            FilesDropped?.Invoke(this, [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()]);
            e.Handled = true;
        }
    }

    private int DropIndex(Point inStrip)
    {
        if (inStrip.Y < 0 || inStrip.Y > Strip.Bounds.Height) return _tabs.Count;
        for (int i = 0; i < Strip.Items.Count; i++)
            if (Strip.Items[i] is Control item && inStrip.X < item.Bounds.X + item.Bounds.Width / 2) return i;
        return _tabs.Count;
    }
}

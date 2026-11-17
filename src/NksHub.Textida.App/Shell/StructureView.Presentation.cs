using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal enum StructureViewPresentation { Tree, Table, Graph }

internal sealed partial class StructureView
{
    private StructureGraph graph = null!;
    private StructureTable table = null!;
    private readonly Grid surfaces = new();
    private readonly Expander namespacePanel = new();
    private readonly Button more = new(), help = new(), statistics = new(), expand = new(), collapse = new();
    private readonly Button zoomIn = new(), zoomOut = new(), fit = new();
    private readonly StackPanel graphTools = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private StructureViewPresentation presentation;
    private static readonly Geometry ClosedChevron = Geometry.Parse("M 4,2 L 10,8 L 4,14 Z");
    private static readonly Geometry OpenChevron = Geometry.Parse("M 2,4 L 14,4 L 8,10 Z");

    public StructureViewPresentation Presentation
    {
        get => presentation;
        set
        {
            if (presentation == value) return;
            presentation = value;
            tree.IsVisible = value == StructureViewPresentation.Tree;
            graph.IsVisible = value == StructureViewPresentation.Graph;
            table.IsVisible = value == StructureViewPresentation.Table;
            graphTools.IsVisible = value == StructureViewPresentation.Graph;
            if (value == StructureViewPresentation.Graph) graph.Refresh();
            if (value == StructureViewPresentation.Table) LoadTable();
            UpdateButtons();
        }
    }

    internal long SourceOffset => source()?.Offset ?? 0;
    internal ByteRange? SelectedRange => Selected?.Data.Range;
    internal StructureGraph Graph => graph;
    internal StructureTable Table => table;
    internal static Geometry ChevronGeometry(bool expanded) => expanded ? OpenChevron : ClosedChevron;
    internal static PathIcon Chevron(bool expanded) => new() { Data = ChevronGeometry(expanded), Width = 10, Height = 10 };

    private void BuildPresentation()
    {
        graph = new StructureGraph(tree) { IsVisible = false };
        table = new StructureTable() { IsVisible = false };
        graph.NodeActivated += (_, _) => Jump();
        graph.EditRequested += (_, _) => _ = EditAsync();
        table.CellSelected += SelectTableCell;
        table.EditRequested += EditTableCell;
        table.PageRequested += LoadTablePage;
        tree.SelectionChanged += (_, _) => { if (Presentation == StructureViewPresentation.Table && !tableSelecting) LoadTable(); };
        expand.Click += (_, _) => SetExpanded(true);
        collapse.Click += (_, _) => SetExpanded(false);
        zoomIn.Click += (_, _) => graph.Zoom(1.2);
        zoomOut.Click += (_, _) => graph.Zoom(1 / 1.2);
        fit.Click += (_, _) => graph.Fit();
        ConfigureIcon(refresh, "refresh"); ConfigureIcon(cancel, "cancel"); ConfigureIcon(up, "up");
        ConfigureIcon(jump, "jump"); ConfigureIcon(copyPath, "copy"); ConfigureIcon(copySource, "copy");
        ConfigureIcon(edit, "edit"); ConfigureIcon(find, "find"); ConfigureIcon(clear, "clear");
        ConfigureIcon(previous, "previous"); ConfigureIcon(next, "next"); ConfigureIcon(nextIssue, "next");
        ConfigureIcon(expand, "expand"); ConfigureIcon(collapse, "collapse");
        ConfigureIcon(more, "more"); ConfigureIcon(help, "help"); ConfigureIcon(statistics, "statistics");
        ConfigureIcon(zoomIn, "expand"); ConfigureIcon(zoomOut, "collapse"); ConfigureIcon(fit, "fit");

        var commands = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { refresh, cancel, up, expand, collapse, jump, copyPath, edit, more }) commands.Children.Add(button);
        foreach (var button in new[] { zoomOut, zoomIn, fit }) graphTools.Children.Add(button);
        var toolbar = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(6, 4) };
        toolbar.Children.Add(commands); Grid.SetColumn(graphTools, 1); toolbar.Children.Add(graphTools);
        graphTools.IsVisible = false;
        DockPanel.SetDock(toolbar, Dock.Top); Children.Add(toolbar);

        var search = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), ColumnSpacing = 4, Margin = new Thickness(6, 0, 6, 5) };
        scope.MinWidth = 84; scope.MaxWidth = 140;
        filter.MinWidth = 48;
        search.Children.Add(scope); Grid.SetColumn(filter, 1); search.Children.Add(filter);
        Grid.SetColumn(find, 2); search.Children.Add(find); Grid.SetColumn(clear, 3); search.Children.Add(clear);
        DockPanel.SetDock(search, Dock.Top); Children.Add(search);

        var counts = new StackPanel { Margin = new Thickness(8) };
        foreach (var kind in Enum.GetValues<NksHub.Textida.Core.Data.Structure.StructureKind>())
        {
            var label = new TextBlock(); countLabels.Add(kind, label); counts.Children.Add(label);
        }
        typeCounts.Content = counts; typeCounts.IsExpanded = true;
        statistics.Flyout = new Flyout { Content = new ScrollViewer { Content = typeCounts, MaxHeight = 360 } };
        help.Flyout = new Flyout { Content = new Border { Child = syntax, Padding = new Thickness(12), MaxWidth = 480 } };
        namespacePanel.Content = namespaces;
        var options = new StackPanel { Spacing = 4, Margin = new Thickness(6, 0) };
        options.Children.Add(namespacePanel);
        DockPanel.SetDock(options, Dock.Top); Children.Add(options);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(6, 4) };
        status.VerticalAlignment = VerticalAlignment.Center;
        status.MaxHeight = 48; issue.MaxWidth = 220; footer.Children.Add(status);
        Control[] footerControls = [previous, next, issue, statistics, help];
        for (int i = 0; i < footerControls.Length; i++) { Grid.SetColumn(footerControls[i], i + 1); footer.Children.Add(footerControls[i]); }
        DockPanel.SetDock(footer, Dock.Bottom); Children.Add(footer);
        surfaces.Children.Add(tree); surfaces.Children.Add(graph); surfaces.Children.Add(table);
        Children.Add(surfaces);

        tree.ContextMenu = BuildContextMenu(); graph.ContextMenu = BuildContextMenu(); table.ContextMenu = BuildContextMenu();
        tree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(tree).Properties.IsRightButtonPressed || e.Source is not Visual visual) return;
            var row = visual.GetSelfAndVisualAncestors().OfType<StructureTreeRow>().FirstOrDefault();
            if (row is { IsBindingCurrent: true, BoundNode: { } node }) tree.SelectedNode = node;
        }, RoutingStrategies.Tunnel);
    }

    private void SetExpanded(bool expanded)
    {
        if (Presentation == StructureViewPresentation.Table)
        {
            if (expanded && tableSelection?.Data.Node is { ChildCount: > 0 } selected) EnterTableContainer(selected.Id);
            else if (!expanded) ShowParent();
            return;
        }
        var node = tree.SelectedNode ?? tree.VisibleNodes.FirstOrDefault(item => item.HasChildren);
        if (node is { HasChildren: true }) node.IsExpanded = expanded;
    }

    private ContextMenu BuildContextMenu() => new() { ItemsSource = ActionMenuItems() };

    private MenuItem[] ActionMenuItems() => new[]
    {
        ActionItem(K.StructureExpand, expand), ActionItem(K.StructureCollapse, collapse),
        ActionItem(K.StructureJump, jump), ActionItem(K.StructureEdit, edit),
        ActionItem(K.StructureCopyPath, copyPath), ActionItem(K.StructureCopySource, copySource),
        ActionItem(K.StructureUp, up), ActionItem(K.StructureRefresh, refresh),
        ActionItem(K.StructureNextIssue, nextIssue),
    };

    private static MenuItem ActionItem(string key, Button button)
    {
        var item = new MenuItem { Header = Loc.Get(key) };
        item.Bind(IsEnabledProperty, button.GetObservable(IsEnabledProperty));
        item.Click += (_, _) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return item;
    }

    private void ApplyPresentationTexts()
    {
        foreach (var (button, key) in new[]
        {
            (more, K.StructureMore), (help, K.StructureHelp), (statistics, K.StructureTypeCounts),
            (expand, K.StructureExpand), (collapse, K.StructureCollapse),
            (zoomIn, K.StructureZoomIn), (zoomOut, K.StructureZoomOut), (fit, K.StructureFit),
        })
        {
            ToolTip.SetTip(button, Loc.Get(key)); AutomationProperties.SetName(button, Loc.Get(key));
        }
        namespacePanel.Header = Loc.Get(K.StructureNamespaces);
        more.Flyout = new MenuFlyout { ItemsSource = ActionMenuItems() };
        AutomationProperties.SetName(graph, Loc.Get(K.StructureModeGraph));
        AutomationProperties.SetName(table, Loc.Get(K.StructureModeTable));
        tree.ContextMenu = BuildContextMenu(); graph.ContextMenu = BuildContextMenu(); table.ContextMenu = BuildContextMenu();
    }

    private static void ConfigureIcon(Button button, string name)
    {
        button.Tag = name;
        button.Content = new StructureIcon(name);
        button.Width = button.MinHeight = 30;
        button.Padding = new Thickness(6);
        button.Margin = new Thickness(1);
    }
}

internal sealed class StructureIcon(string name) : Control
{
    private static readonly IReadOnlyDictionary<string, Geometry> Paths = new Dictionary<string, Geometry>
    {
        ["refresh"] = Geometry.Parse("M13 6 A5 5 0 1 0 13 11 M13 2 L13 6 L9 6"),
        ["cancel"] = Geometry.Parse("M4 4 L12 12 M12 4 L4 12"),
        ["up"] = Geometry.Parse("M3 9 L8 4 L13 9 M8 4 L8 14"),
        ["jump"] = Geometry.Parse("M8 3 L13 3 L13 8 M13 3 L6 10 M11 10 L11 13 L3 13 L3 5 L6 5"),
        ["copy"] = Geometry.Parse("M5 5 L13 5 L13 14 L5 14 Z M10 5 L10 2 L2 2 L2 11 L5 11"),
        ["edit"] = Geometry.Parse("M3 10 L10 3 L13 6 L6 13 L2 14 Z M8 5 L11 8"),
        ["find"] = Geometry.Parse("M11 7 A4 4 0 1 1 3 7 A4 4 0 1 1 11 7 M10 10 L14 14"),
        ["clear"] = Geometry.Parse("M5 4 L1 8 L5 12 M1 8 L12 8"),
        ["previous"] = Geometry.Parse("M10 3 L5 8 L10 13"),
        ["next"] = Geometry.Parse("M5 3 L10 8 L5 13"),
        ["expand"] = Geometry.Parse("M3 8 L13 8 M8 3 L8 13"),
        ["collapse"] = Geometry.Parse("M3 8 L13 8"),
        ["more"] = Geometry.Parse("M3 7 L3 9 M8 7 L8 9 M13 7 L13 9"),
        ["help"] = Geometry.Parse("M5 5 A3 3 0 1 1 9 8 L8 9 L8 11 M8 13 L8 14"),
        ["statistics"] = Geometry.Parse("M3 13 L3 8 M8 13 L8 3 M13 13 L13 6"),
        ["fit"] = Geometry.Parse("M2 6 L2 2 L6 2 M10 2 L14 2 L14 6 M14 10 L14 14 L10 14 M6 14 L2 14 L2 10"),
    };

    protected override Size MeasureOverride(Size availableSize) => new(16, 16);
    public override void Render(DrawingContext context)
    {
        using (context.PushTransform(Matrix.CreateScale(Bounds.Width / 16, Bounds.Height / 16)))
            context.DrawGeometry(null, new Pen(TextElement.GetForeground(this), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Paths[name]);
    }
}

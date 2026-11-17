using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureTreeRow : ListBoxItem
{
    private const double Indent = 16;
    private readonly Grid body = new() { ColumnDefinitions = new("20,*") };
    private readonly StructureExpander expander = new()
    {
        Width = 20, MinHeight = 20, Padding = new Thickness(0), BorderThickness = new Thickness(0),
        Focusable = false, VerticalAlignment = VerticalAlignment.Top,
    };
    private readonly StructureRowHeader header = new();
    private readonly PathIcon chevron = StructureView.Chevron(false);
    private bool listening, attached;

    internal StructureTreeRow()
    {
        expander.RowOwner = this;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Padding = new Thickness(0);
        Grid.SetColumn(header, 1);
        body.Children.Add(expander); body.Children.Add(header);
        expander.Click += (_, e) =>
        {
            if (BoundNode is { IsEnabled: true, HasChildren: true } node && IsBindingCurrent)
            {
                Owner!.SelectAndReveal(node);
                node.IsExpanded = !node.IsExpanded;
                e.Handled = true;
            }
        };
        DoubleTapped += (_, e) =>
        {
            if (BoundNode is { IsEnabled: true, HasChildren: true, Kind: StructureTreeNodeKind.Normal } node
                && IsBindingCurrent && !FromButton(e.Source)) node.IsExpanded = !node.IsExpanded;
        };
    }

    protected override Type StyleKeyOverride => typeof(ListBoxItem);
    internal StructureTree? Owner { get; private set; }
    internal StructureTree? LastOwner { get; private set; }
    internal StructureTreeNode? BoundNode { get; private set; }
    internal long BindingVersion { get; private set; }
    internal Control HeaderVisual => header;
    internal Control ExpanderVisual => expander;
    internal bool IsBindingCurrent => BoundNode is { } node && Owner?.IsCurrent(node, BindingVersion) == true;

    internal void Bind(StructureTree owner, StructureTreeNode node)
    {
        Unbind();
        Owner = LastOwner = owner; BoundNode = node; BindingVersion = node.BindingVersion;
        DataContext = node;
        owner.RowBound(this);
        Listen();
        Content = body;
        Refresh();
    }

    internal void Unbind()
    {
        StopListening();
        Owner?.RowUnbound(this);
        header.SetCaret(false); header.SetText("", "");
        body.Margin = default; expander.Content = null; expander.IsVisible = false;
        Tag = null; AutomationProperties.SetName(this, "");
        AutomationProperties.SetItemStatus(this, "");
        Content = null;
        DataContext = null;
        Owner = null; BoundNode = null; BindingVersion = 0;
        IsEnabled = true;
    }

    private void NodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StructureTreeNode.BindingVersion))
        {
            if (Owner is { } owner && BoundNode is { } node) Bind(owner, node);
            return;
        }
        Refresh();
    }

    private void Listen()
    {
        if (listening || !attached || BoundNode is not { } node) return;
        node.PropertyChanged += NodeChanged; listening = true;
    }

    private void StopListening()
    {
        if (listening && BoundNode is { } node) node.PropertyChanged -= NodeChanged;
        listening = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        if (BoundNode is { } node) BindingVersion = node.BindingVersion;
        Listen(); Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        StopListening();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        if (BoundNode is not { } node) return;
        int depth = 0;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) if (!parent.HeaderHidden) depth++;
        body.Margin = new Thickness(depth * Indent, 0, 0, 0);
        Tag = node.Tag; IsEnabled = node.IsEnabled;
        header.SetText(node.DisplayKey, node.DisplayValue); header.SetCaret(node.IsCaret);
        chevron.Data = StructureView.ChevronGeometry(node.IsExpanded);
        if (!ReferenceEquals(expander.Content, chevron)) expander.Content = chevron;
        expander.IsVisible = node.HasChildren && node.Kind == StructureTreeNodeKind.Normal;
        AutomationProperties.SetName(this, node.DisplayName);
        AutomationProperties.SetItemStatus(this, node.IsCaret ? Loc.Get(K.StructureCaret) : "");
        if (node.Kind == StructureTreeNodeKind.Pager && node.PagerContent is { } pager)
            Content = pager;
        else if (!ReferenceEquals(Content, body)) Content = body;
    }

    private bool FromButton(object? source)
        => source is Visual visual && visual.GetSelfAndVisualAncestors().TakeWhile(v => !ReferenceEquals(v, this)).Any(v => v is Button);

    private sealed partial class StructureExpander : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);
        internal StructureTreeRow? RowOwner { get; set; }
    }
}

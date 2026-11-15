using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;

namespace NksHub.Textida.App.Shell;

internal static class StructureRowThemes
{
    public static ControlTheme? TryCreateLeaf(IResourceHost owner)
    {
        if (FindResource(owner, typeof(TreeViewItem)) is not ControlTheme stock
            || FindResource(owner, "TreeViewItemExpandCollapseChevronMargin") is not Thickness margin) return null;
        return new ControlTheme(typeof(TreeViewItem))
        {
            BasedOn = stock,
            Setters = { new Setter(TemplatedControl.TemplateProperty,
                new FuncControlTemplate<TreeViewItem>((_, scope) => BuildLeaf(scope, margin))) },
        };
    }

    private static object? FindResource(IResourceHost owner, object key)
        => owner.TryFindResource(key, out var value) ? value
            : Application.Current is { } application && application.TryFindResource(key, out value) ? value : null;

    private static Control BuildLeaf(INameScope scope, Thickness margin)
    {
        var root = new Border { Name = "PART_LayoutRoot", Focusable = true };
        root.Classes.Add("TreeViewItemLayoutRoot");
        TemplatedControl.SetIsTemplateFocusTarget(root, true);
        Bind(root, Border.BackgroundProperty, TemplatedControl.BackgroundProperty);
        Bind(root, Border.BorderBrushProperty, TemplatedControl.BorderBrushProperty);
        Bind(root, Border.BorderThicknessProperty, TemplatedControl.BorderThicknessProperty);
        Bind(root, Border.CornerRadiusProperty, TemplatedControl.CornerRadiusProperty);
        Bind(root, Layoutable.MinHeightProperty, Layoutable.MinHeightProperty);
        var grid = new Grid { Name = "PART_Header", ColumnDefinitions = new("Auto,*") };
        grid.Bind(Layoutable.MarginProperty, new MultiBinding
        {
            Converter = TreeViewItemIndentConverter.Instance,
            Bindings = { new TemplateBinding(TreeViewItem.LevelProperty), new DynamicResourceExtension("TreeViewItemIndent") },
        });
        var gap = new Panel { Name = "PART_ExpandCollapseChevronContainer", Margin = margin };
        var presenter = new ContentPresenter { Name = "PART_HeaderPresenter", Focusable = false, Background = Brushes.Transparent };
        Bind(presenter, ContentPresenter.ContentProperty, HeaderedItemsControl.HeaderProperty);
        Bind(presenter, ContentPresenter.ContentTemplateProperty, HeaderedItemsControl.HeaderTemplateProperty);
        Bind(presenter, Layoutable.HorizontalAlignmentProperty, Layoutable.HorizontalAlignmentProperty);
        Bind(presenter, Layoutable.VerticalAlignmentProperty, Layoutable.VerticalAlignmentProperty);
        Bind(presenter, Layoutable.MarginProperty, TemplatedControl.PaddingProperty);
        Grid.SetColumn(presenter, 1);
        grid.Children.Add(gap); grid.Children.Add(presenter); root.Child = grid;
        scope.Register(root.Name, root); scope.Register(grid.Name, grid);
        scope.Register(gap.Name, gap); scope.Register(presenter.Name, presenter);
        return root;
    }

    private static void Bind(Control target, AvaloniaProperty property, AvaloniaProperty source)
        => target.Bind(property, new TemplateBinding(source));
}

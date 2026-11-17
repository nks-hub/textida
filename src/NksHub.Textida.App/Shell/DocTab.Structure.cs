using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Shell;

internal sealed partial class DocTab
{
    private Grid? structureSplit;
    private DockPanel? structureSource;
    public StructureView? Structure { get; private set; }
    public bool StructureShown { get; private set; }
    internal Core.Documents.Document? StructureDocument { get; set; }

    internal void ShowStructure(StructureView view, bool show, bool split, EditorOptions options)
    {
        if (Structure is not null && !ReferenceEquals(Structure, view))
            throw new InvalidOperationException("A tab keeps one structure analysis.");
        Structure = view;
        if (TableShown) ShowTable(false, options);
        if (JsonShown) ShowJson(false, options);
        RestoreStructureSource();
        Root.Children.Remove(view);
        StructureShown = show;
        if (!show)
        {
            View.IsVisible = true;
            if (Minimap is { } hiddenMap) hiddenMap.IsVisible = options.Minimap;
            _automation?.SetCurrentValue(Control.IsVisibleProperty, true);
            if (!view.Parsing.IsCompleted) view.Suspend();
            return;
        }
        if (split)
        {
            Root.Children.Remove(View);
            structureSource = new DockPanel();
            if (Minimap is { } map)
            {
                Root.Children.Remove(map);
                map.IsVisible = options.Minimap;
                structureSource.Children.Add(map);
            }
            View.IsVisible = true;
            structureSource.Children.Add(View);
            structureSplit = new Grid { ColumnDefinitions = new("*,5,*") };
            structureSplit.Children.Add(structureSource);
            var divider = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Columns,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(divider, 1); structureSplit.Children.Add(divider);
            Grid.SetColumn(view, 2); structureSplit.Children.Add(view);
            Root.Children.Add(structureSplit);
        }
        else
        {
            View.IsVisible = false;
            if (Minimap is { } map) map.IsVisible = false;
            Grid.SetColumn(view, 0);
            Root.Children.Add(view);
        }
        _automation?.SetCurrentValue(Control.IsVisibleProperty, split);
        view.Sync();
    }

    private void RestoreStructureSource()
    {
        if (structureSplit is null) return;
        structureSplit.Children.Clear();
        structureSource!.Children.Clear();
        Root.Children.Remove(structureSplit);
        if (Minimap is { } map) Root.Children.Add(map);
        Root.Children.Add(View);
        structureSource = null;
        structureSplit = null;
    }
}

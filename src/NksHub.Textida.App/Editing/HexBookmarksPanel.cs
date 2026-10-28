using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

internal enum HexBookmarkAction { GoTo, Add, Edit, Delete, Export, Import }

/// <summary>
/// The list of the hex bookmarks of a document beside the grid: a filter that matches name, comment and type, the regions with their colour and offsets,
/// and the actions on them. Enter and a double click select the region in the grid; Delete removes it.
/// </summary>
internal sealed class HexBookmarksPanel : Border
{
    private readonly HexView _owner;
    private readonly TextBox _filter = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single, Focusable = true, Background = Brushes.Transparent };
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(2, 4) };
    private readonly Button _add = new(), _edit = new(), _delete = new(), _export = new(), _import = new();
    private IReadOnlyList<HexBookmark> _shown = [];
    private bool _refreshing;

    public HexBookmarksPanel(HexView owner)
    {
        HelpContext.Set(this, "hex.bookmarks");
        _owner = owner;
        Classes.Add("sidepanel");
        Width = 290;
        Padding = new Thickness(10, 8);
        _filter.TextChanged += (_, _) => Refresh();
        _list.DoubleTapped += (_, _) => Raise(HexBookmarkAction.GoTo);
        _list.KeyDown += OnListKey;
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list.ItemCount > 0) { _list.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _owner.Focus(); e.Handled = true; }
        };
        _add.Click += (_, _) => Raise(HexBookmarkAction.Add);
        _edit.Click += (_, _) => Raise(HexBookmarkAction.Edit);
        _delete.Click += (_, _) => Raise(HexBookmarkAction.Delete);
        _export.Click += (_, _) => Raise(HexBookmarkAction.Export);
        _import.Click += (_, _) => Raise(HexBookmarkAction.Import);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _add, _edit, _delete } };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 6, 0, 0), Children = { _export, _import } };
        var stack = new Shell.ReadingDockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_filter, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_empty, Dock.Bottom);
        stack.Children.Add(_title);
        stack.Children.Add(_filter);
        stack.Children.Add(top);
        stack.Children.Add(bottom);
        stack.Children.Add(_empty);
        stack.Children.Add(_list);
        Child = stack;
        ApplyTexts();
    }

    /// <summary>The panel asks the window to do something with a bookmark (null for Add, Export and Import).</summary>
    public event Action<HexBookmarkAction, HexBookmark?>? ActionRequested;

    internal ListBox Rows => _list;
    internal TextBox FilterBox => _filter;
    /// <summary>The row texts as a screen reader gets them; for tests.</summary>
    internal IReadOnlyList<string> RowTexts => [.. _list.Items.OfType<ListBoxItem>().Select(AutomationProperties.GetName)];
    public HexBookmark? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _shown.Count ? _shown[_list.SelectedIndex] : null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
    }

    private void ApplyTexts()
    {
        _title.Text = Loc.Get(K.HexBmTitle);
        _filter.PlaceholderText = Loc.Get(K.HexBmFilter);
        _empty.Text = Loc.Get(K.HexBmEmpty);
        _add.Content = Loc.Get(K.HexBmAdd);
        _edit.Content = Loc.Get(K.HexBmEdit);
        _delete.Content = Loc.Get(K.HexBmDelete);
        _export.Content = Loc.Get(K.HexBmExport);
        _import.Content = Loc.Get(K.HexBmImport);
        AutomationProperties.SetName(_filter, Loc.Get(K.HexBmFilter));
        AutomationProperties.SetName(_list, Loc.Get(K.HexBmA11yList));
        Refresh();
    }

    private void Raise(HexBookmarkAction action)
    {
        var item = Selected;
        if (action is HexBookmarkAction.GoTo or HexBookmarkAction.Edit or HexBookmarkAction.Delete && item is null) return;
        ActionRequested?.Invoke(action, item);
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Raise(HexBookmarkAction.GoTo);
        else if (e.Key == Key.Delete) Raise(HexBookmarkAction.Delete);
        else if (e.Key == Key.Escape) _owner.Focus();
        else return;
        e.Handled = true;
    }

    public bool FocusList()
    {
        IsVisible = true;
        Refresh();
        return _filter.Focus();
    }

    /// <summary>The text of a row: the name (or "unnamed"), the region and the type.</summary>
    internal static string RowText(HexBookmark b, Func<long, string> offset)
    {
        string range = Loc.Get(K.HexBmRange, ("start", offset(b.Range.Start)), ("end", offset(b.Range.End)), ("length", b.Range.Length));
        string row = Loc.Get(K.HexBmRow, ("name", b.Name.Length > 0 ? b.Name : Loc.Get(K.HexBmUnnamed)), ("range", range));
        return b.Type is { } type ? Loc.Get(K.HexBmRowType, ("row", row), ("type", type)) : row;
    }

    /// <summary>Reads the bookmarks again; call after they or the filter changed.</summary>
    public void Refresh()
    {
        if (!IsVisible || _refreshing) return;
        _refreshing = true;
        try
        {
            var keep = Selected?.Id;
            _shown = _owner.Marks.Filter(_filter.Text);
            _list.Items.Clear();
            foreach (var b in _shown)
            {
                string text = RowText(b, _owner.OffsetText);
                var swatch = new Border
                {
                    Width = 12, Height = 12, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                    Background = new SolidColorBrush(Color.Parse(b.Color ?? "#8A9099")), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(b.Color is null ? 1 : 0),
                };
                var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center } } };
                var item = new ListBoxItem { Content = line, Padding = new Thickness(6, 3) };
                AutomationProperties.SetName(item, b.Comment is { } c ? $"{text}. {c}" : text);
                if (b.Comment is { } comment) ToolTip.SetTip(item, comment);
                _list.Items.Add(item);
            }
            _list.SelectedIndex = keep is { } id ? Math.Max(0, _shown.ToList().FindIndex(b => b.Id == id)) : _shown.Count > 0 ? 0 : -1;
            _empty.IsVisible = _shown.Count == 0;
            _edit.IsEnabled = _delete.IsEnabled = _shown.Count > 0;
            _export.IsEnabled = _owner.Marks.Count > 0;
        }
        finally { _refreshing = false; }
    }
}

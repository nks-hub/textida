using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>The Markdown document the headings panel shows: its identity, the revision of its text, the text, the (fractional, zero-based) line now at the top or under the caret, and where a heading is shown.</summary>
internal sealed record HeadingSource(object Key, long Revision, Func<string> Text, Func<double> CurrentLine, Action<int> Jump);

/// <summary>
/// The headings panel of a Markdown file (P9.3.4): a tree of the headings H1 to H6 that stands beside the document in reading, editing and source alike. The heading of the current place is shown in bold
/// and named "current" for a screen reader; a click or Enter shows the heading in the document; the tree follows typing after a short pause (the text is parsed off the UI thread). The buttons 1 to 6 show
/// the headings down to that level and fold the deeper ones, and the filter keeps the headings whose title contains its text, with the headings above them.
/// </summary>
internal sealed class HeadingsView : DockPanel
{
    private readonly Func<HeadingSource?> _source;
    private readonly TextBox _filter = new();
    private readonly StackPanel _levels = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0, 8, 4) };
    private readonly TreeView _tree = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 8), IsVisible = false };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(PluginLimits.PreviewLiveDebounceMs) };
    private IReadOnlyList<MarkdownHeading> _headings = [];
    private readonly List<TreeViewItem> _items = [];
    private object? _key;
    private long _revision = -1, _pending = -1;
    private int _depth = 6;
    private TreeViewItem? _current;
    private bool _supported;

    public HeadingsView(Func<HeadingSource?> source)
    {
        _source = source;
        _debounce.Tick += (_, _) => { _debounce.Stop(); Rebuild(); };
        _filter.Margin = new Thickness(8, 4, 8, 4);
        _filter.TextChanged += (_, _) => ApplyFilter();
        for (int level = 1; level <= 6; level++)
        {
            var button = new Button { Content = level.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(8, 2), Tag = level };
            button.Classes.Add("headingslevel");
            int depth = level;
            button.Click += (_, _) => ShowDepth(depth);
            _levels.Children.Add(button);
        }
        _tree.AddHandler(KeyDownEvent, OnTreeKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SetDock(_filter, Dock.Top);
        SetDock(_levels, Dock.Top);
        SetDock(_message, Dock.Top);
        Children.Add(_filter);
        Children.Add(_levels);
        Children.Add(_message);
        Children.Add(_tree);
        ApplyTexts();
    }

    protected override Type StyleKeyOverride => typeof(DockPanel);

    internal TextBox Filter => _filter;

    internal TreeView Tree => _tree;

    internal string Message => _message.IsVisible ? _message.Text ?? "" : "";

    internal IReadOnlyList<MarkdownHeading> Headings => _headings;

    /// <summary>The heading of the current place, or null.</summary>
    internal MarkdownHeading? Current => _current?.Tag as MarkdownHeading;

    /// <summary>The headings the tree shows now (not folded away and not filtered out), in order.</summary>
    internal IReadOnlyList<MarkdownHeading> Visible => [.. _items.Where(IsShown).Select(i => (MarkdownHeading)i.Tag!)];

    /// <summary>The task of the parsing that runs now, for tests.</summary>
    internal Task Parsing { get; private set; } = Task.CompletedTask;

    public void ApplyTexts()
    {
        _filter.PlaceholderText = Loc.Get(K.HeadingsFilter);
        AutomationProperties.SetName(_filter, Loc.Get(K.HeadingsFilter));
        AutomationProperties.SetName(_tree, Loc.Get(K.HeadingsTree));
        foreach (var button in _levels.Children.OfType<Button>())
        {
            string name = Loc.Get(K.HeadingsLevel, ("level", button.Tag!));
            AutomationProperties.SetName(button, name);
            ToolTip.SetTip(button, name);
        }
        if (_current is not null) AutomationProperties.SetItemStatus(_current, Loc.Get(K.HeadingsCurrent));
        ShowMessage();
    }

    public bool FocusTree() => _items.Count > 0 && _tree.Focus() || _filter.Focus();

    /// <summary>
    /// Follows the active document: another document is read at once, a new revision of the same one after the pause of typing, and the current heading follows the caret or the scrolled page.
    /// Called by the window whenever the caret, the page, the text or the active tab changes.
    /// </summary>
    public void Sync()
    {
        var source = _source();
        if (source is null)
        {
            _debounce.Stop();
            _key = null;
            _revision = -1;
            _supported = false;
            Fill([]);
            return;
        }
        _supported = true;
        if (!ReferenceEquals(source.Key, _key))
        {
            _key = source.Key;
            _revision = -1;
            _debounce.Stop();
            Rebuild();
            return;
        }
        if (source.Revision != _revision && source.Revision != _pending)
        {
            _pending = source.Revision;
            _debounce.Stop();
            _debounce.Start();
        }
        MarkCurrent(source.CurrentLine());
    }

    private void Rebuild()
    {
        if (_source() is not { } source) return;
        long revision = source.Revision;
        object key = source.Key;
        string text = source.Text();
        _pending = revision;
        Parsing = ParseAsync(text, key, revision);
    }

    private async Task ParseAsync(string text, object key, long revision)
    {
        IReadOnlyList<MarkdownHeading> headings;
        try { headings = await Task.Run(() => MarkdownSyntax.Headings(MarkdownSyntax.Parse(text, PluginLimits.PreviewMarkdownMaxDepth))); }
        catch (MarkdownSyntaxException) { headings = []; }
        if (!ReferenceEquals(key, _key) || _source() is not { } source || !ReferenceEquals(source.Key, key)) return;
        _revision = revision;
        _pending = -1;
        if (!headings.SequenceEqual(_headings)) Fill(headings);
        MarkCurrent(source.CurrentLine());
    }

    /// <summary>Builds the tree: each heading under the nearest heading above it of a higher level.</summary>
    private void Fill(IReadOnlyList<MarkdownHeading> headings)
    {
        _headings = headings;
        _items.Clear();
        _current = null;
        var roots = new List<TreeViewItem>();
        var open = new List<(int Level, TreeViewItem Item)>();
        foreach (var heading in headings)
        {
            var item = new TreeViewItem { Header = new TextBlock { Text = heading.Title, TextTrimming = TextTrimming.CharacterEllipsis }, Tag = heading, IsExpanded = heading.Level < _depth };
            AutomationProperties.SetName(item, heading.Title);
            item.Tapped += (_, e) => { e.Handled = true; Jump(heading); };
            while (open.Count > 0 && open[^1].Level >= heading.Level) open.RemoveAt(open.Count - 1);
            if (open.Count == 0) roots.Add(item);
            else open[^1].Item.Items.Add(item);
            open.Add((heading.Level, item));
            _items.Add(item);
        }
        _tree.ItemsSource = roots;
        ApplyFilter();
        ShowMessage();
    }

    private void ShowMessage()
    {
        string? text = !_supported ? Loc.Get(K.PreviewOutlineUnsupported)
            : _headings.Count == 0 ? Loc.Get(K.PreviewOutlineEmpty)
            : _items.Count > 0 && !_items.Any(IsShown) ? Loc.Get(K.HeadingsNoMatch) : null;
        _message.Text = text;
        _message.IsVisible = text is not null;
    }

    private static TreeViewItem? ParentOf(TreeViewItem item) => item.Parent as TreeViewItem;

    /// <summary>True when the item is in the tree now: it passes the filter and every heading above it is unfolded.</summary>
    private static bool IsShown(TreeViewItem item)
    {
        if (!item.IsVisible) return false;
        for (var parent = ParentOf(item); parent is not null; parent = ParentOf(parent))
            if (!parent.IsExpanded || !parent.IsVisible) return false;
        return true;
    }

    /// <summary>Shows the headings down to <paramref name="depth"/> and folds those below.</summary>
    internal void ShowDepth(int depth)
    {
        _depth = Math.Clamp(depth, 1, 6);
        foreach (var item in _items) item.IsExpanded = ((MarkdownHeading)item.Tag!).Level < _depth;
        ApplyFilter();
    }

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    private void ApplyFilter()
    {
        string filter = (_filter.Text ?? "").Trim();
        if (filter.Length == 0)
            foreach (var item in _items) item.IsVisible = true;
        else
        {
            foreach (var item in _items) item.IsVisible = false;
            foreach (var item in _items)
            {
                if (Compare.IndexOf(((MarkdownHeading)item.Tag!).Title, filter, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) < 0) continue;
                item.IsVisible = true;
                for (var parent = ParentOf(item); parent is not null; parent = ParentOf(parent))
                {
                    parent.IsVisible = true;
                    parent.IsExpanded = true; // a match is shown even below a folded level
                }
            }
        }
        ShowMessage();
    }

    /// <summary>Marks the last heading at or above the line in bold.</summary>
    private void MarkCurrent(double line)
    {
        TreeViewItem? found = null;
        foreach (var item in _items)
        {
            if (((MarkdownHeading)item.Tag!).Line > line + 0.001) break;
            found = item;
        }
        if (ReferenceEquals(found, _current)) return;
        if (_current is not null)
        {
            _current.Classes.Remove("current");
            ((TextBlock)_current.Header!).FontWeight = FontWeight.Normal;
            _current.ClearValue(AutomationProperties.ItemStatusProperty);
        }
        _current = found;
        if (found is null) return;
        found.Classes.Add("current");
        ((TextBlock)found.Header!).FontWeight = FontWeight.Bold;
        AutomationProperties.SetItemStatus(found, Loc.Get(K.HeadingsCurrent));
        if (IsShown(found)) found.BringIntoView();
    }

    private void Jump(MarkdownHeading heading)
    {
        if (_source() is not { } source) return;
        source.Jump(heading.Line);
        MarkCurrent(heading.Line);
    }

    /// <summary>Enter shows the selected heading in the document; the arrows only move in the tree.</summary>
    private void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _tree.SelectedItem is not TreeViewItem { Tag: MarkdownHeading heading }) return;
        Jump(heading);
        e.Handled = true;
    }

    /// <summary>Shows a heading in the document as a click on it does.</summary>
    internal void Activate(MarkdownHeading heading) => Jump(heading);
}

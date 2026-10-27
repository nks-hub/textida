using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The structure of the file beside the hex grid (P8.10.1): the fields a <c>.ksy</c> template finds, as a tree that is read as it is opened. Choosing a field selects its bytes in the
/// grid. The template is the shipped one whose magic number or extension fits the file; the choice made in the list is kept for the extension until the program ends.
/// </summary>
internal sealed class HexTemplatePanel : Border
{
    private static readonly Dictionary<string, string> Chosen = new(StringComparer.OrdinalIgnoreCase);
    private readonly HexView _owner;
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly ComboBox _picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _note = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TreeView _tree = new() { Background = Brushes.Transparent, Focusable = true };
    private bool _refreshing;

    public HexTemplatePanel(HexView owner)
    {
        _owner = owner;
        Classes.Add("sidepanel");
        Width = 320;
        Padding = new Thickness(10, 8);
        _picker.ItemsSource = KsyCatalog.All;
        _picker.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<KsyPinned>((p, _) => new TextBlock { Text = p.Template.Title ?? p.Id });
        _picker.SelectionChanged += (_, _) => PickerChanged();
        _tree.SelectionChanged += (_, _) => SelectField();
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Escape) return;
            _owner.Focus();
            e.Handled = true;
        };
        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_picker, Dock.Top);
        DockPanel.SetDock(_note, Dock.Top);
        dock.Children.Add(_title);
        dock.Children.Add(_picker);
        dock.Children.Add(_note);
        dock.Children.Add(_tree);
        Child = dock;
        ApplyTexts();
    }

    /// <summary>The tree of fields; for tests.</summary>
    internal TreeView Tree => _tree;

    /// <summary>The template in use, or null when none fits the file.</summary>
    internal KsyPinned? Current => _picker.SelectedItem as KsyPinned;

    internal string? NoteShown => _note.IsVisible ? _note.Text : null;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
        _owner.DocumentChanged += Refresh;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
        _owner.DocumentChanged -= Refresh;
    }

    private void ApplyTexts()
    {
        _title.Text = Loc.Get(K.KsyTitle);
        AutomationProperties.SetName(_picker, Loc.Get(K.KsyPickerA11y));
        AutomationProperties.SetName(_tree, Loc.Get(K.KsyTitle));
        Rebuild();
    }

    public bool FocusTree()
    {
        IsVisible = true;
        Refresh();
        return _tree.Focus();
    }

    /// <summary>Chooses the template for the file again and reads its tree from the start; call after the panel opened or the document changed.</summary>
    public void Refresh()
    {
        if (!IsVisible || _refreshing) return;
        _refreshing = true;
        try
        {
            var doc = _owner.Document;
            string ext = Path.GetExtension(doc.FilePath ?? "");
            var header = new byte[16];
            int got = 0;
            try { got = doc.GetSnapshot().Read(0, header); }
            catch (IOException) { _owner.FileChangedBehind(); return; }
            catch (ObjectDisposedException) { return; }
            var pinned = Chosen.TryGetValue(ext, out string? id) ? KsyCatalog.All.FirstOrDefault(p => p.Id == id) : null;
            pinned ??= KsyCatalog.Find(doc.FilePath, header.AsSpan(0, got));
            _picker.SelectedItem = pinned;
        }
        finally { _refreshing = false; }
        Rebuild();
    }

    private void PickerChanged()
    {
        if (_refreshing) return;
        if (_picker.SelectedItem is KsyPinned p) Chosen[Path.GetExtension(_owner.Document.FilePath ?? "")] = p.Id;
        Rebuild();
    }

    private void Rebuild()
    {
        if (!IsVisible) return;
        _tree.Items.Clear();
        _note.IsVisible = Current is null;
        _note.Text = Loc.Get(K.KsyNone);
        if (Current is not { } pinned) return;
        DocumentSnapshot snapshot;
        try { snapshot = _owner.Document.GetSnapshot(); }
        catch (ObjectDisposedException) { return; }
        var root = Item(new KsyReader(snapshot, pinned.Template).Root);
        _tree.Items.Add(root);
        root.IsExpanded = true;
    }

    private TreeViewItem Item(KsyNode node)
    {
        string text = Describe(node);
        string place = $"0x{node.Start:X} +{node.Length}";
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var label = new TextBlock { Text = text };
        if (node.Error is not null && this.TryFindResource("Danger", ActualThemeVariant, out var danger) && danger is IBrush brush) label.Foreground = brush;
        header.Children.Add(label);
        header.Children.Add(new TextBlock { Text = place, Opacity = 0.65 });
        var item = new TreeViewItem { Header = header, Tag = node };
        AutomationProperties.SetName(item, $"{text}, {place}");
        if (node.HasChildren)
        {
            item.Items.Add(new TreeViewItem());
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == TreeViewItem.IsExpandedProperty && item.IsExpanded) Populate(item);
            };
        }
        return item;
    }

    private static string Describe(KsyNode node)
    {
        if (node.Error is { } error) return $"{node.Name}: {KsyText.Describe(error)}";
        if (node.Value != "") return $"{node.Name}: {node.Value}";
        return node.TypeName is { } type ? $"{node.Name} ({type})" : node.Name;
    }

    private void Populate(TreeViewItem item)
    {
        if (item.Tag is not KsyNode node || item.Items is not [TreeViewItem { Tag: null }]) return;
        IReadOnlyList<KsyNode> children;
        try { children = node.Children; }
        catch (IOException) { _owner.FileChangedBehind(); return; }
        item.Items.Clear();
        foreach (var child in children) item.Items.Add(Item(child));
    }

    private void SelectField()
    {
        if (_tree.SelectedItem is not TreeViewItem { Tag: KsyNode node }) return;
        _owner.Select(node.Start, node.End);
    }
}

/// <summary>The sentence for an error of a template or of a file read through one.</summary>
internal static class KsyText
{
    public static string Describe(KsyError error)
    {
        string key = error.Kind switch
        {
            KsyErrorKind.Syntax => K.KsyErrSyntax,
            KsyErrorKind.Unsupported => K.KsyErrUnsupported,
            KsyErrorKind.Invalid => K.KsyErrInvalid,
            KsyErrorKind.UnknownType => K.KsyErrUnknownType,
            KsyErrorKind.UnknownField => K.KsyErrUnknownField,
            KsyErrorKind.UnknownEnum => K.KsyErrUnknownEnum,
            KsyErrorKind.Duplicate => K.KsyErrDuplicate,
            KsyErrorKind.OutOfFile => K.KsyErrOutOfFile,
            KsyErrorKind.Contents => K.KsyErrContents,
            KsyErrorKind.LimitDepth => K.KsyErrLimitDepth,
            KsyErrorKind.LimitRepeat => K.KsyErrLimitRepeat,
            KsyErrorKind.LimitSize => K.KsyErrLimitSize,
            KsyErrorKind.Expression => K.KsyErrExpression,
            _ => K.KsyErrLimitWork,
        };
        string text = Loc.Get(key, ("subject", error.Subject));
        return error.Line > 0 ? text + " " + Loc.Get(K.KsyErrLine, ("line", error.Line)) : text;
    }
}

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
/// What the structure panels of all hex views of one app share (P8.10.5): the templates the user opened from files, the template picked for each file type in this run, and where the choices that outlive the run are
/// kept (the setting <c>hex.structureTemplates</c>, written <c>extension=template</c>). A template that comes from a file is never kept past the run.
/// </summary>
internal sealed class HexTemplateHost
{
    public List<KsyPinned> Loaded { get; } = [];
    public Dictionary<string, string> Chosen { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string> ReadChoices { get; set; } = () => "";
    public Action<string> WriteChoices { get; set; } = _ => { };
}

/// <summary>
/// The structure of the file beside the hex grid (P8.10.1): the fields a <c>.ksy</c> template finds, as a tree that is read as it is opened. Choosing a field selects its bytes in the
/// grid, and moving the caret in the grid selects the field that holds it. The template is the one the user picked for the file type (kept in the setting), else the shipped one whose magic number or extension fits
/// the file; the list offers the shipped templates and those the user opened from files.
/// </summary>
internal sealed class HexTemplatePanel : Border
{
    private readonly HexView _owner;
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly ComboBox _picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 6, 0, 6) };
    private readonly Button _open = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _info = new() { IsVisible = false, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _note = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TreeView _tree = new() { Background = Brushes.Transparent, Focusable = true };
    private KsyNode? _rootNode;
    private TreeViewItem? _rootItem;
    private bool _refreshing, _following;
    private long _followed = -1;

    public HexTemplatePanel(HexView owner)
    {
        _owner = owner;
        Classes.Add("sidepanel");
        Width = 320;
        Padding = new Thickness(10, 8);
        _picker.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<KsyPinned>((p, _) => new TextBlock { Text = p is null ? "" : Label(p) }); // the box of a combo without a choice builds its content from null
        _picker.SelectionChanged += (_, _) => PickerChanged();
        _open.Click += (_, _) => _ = OpenFileAsync();
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite); // a refused template is announced without moving the focus
        _tree.SelectionChanged += (_, _) => SelectField();
        _tree.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                SelectField();
                _owner.Focus();
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                _owner.Focus();
                e.Handled = true;
            }
        };
        var dock = new DockPanel { LastChildFill = true };
        foreach (Control top in new Control[] { _title, _picker, _open, _info, _note })
        {
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
        }
        dock.Children.Add(_tree);
        Child = dock;
        ApplyTexts();
    }

    /// <summary>The tree of fields; for tests.</summary>
    internal TreeView Tree => _tree;

    /// <summary>The template in use, or null when none fits the file.</summary>
    internal KsyPinned? Current => _picker.SelectedItem as KsyPinned;

    internal ComboBox Picker => _picker;

    internal string? NoteShown => _note.IsVisible ? _note.Text : null;

    internal string? InfoShown => _info.IsVisible ? _info.Text : null;

    private IReadOnlyList<KsyPinned> Entries => [.. KsyCatalog.All, .. _owner.TemplateHost.Loaded];

    private static string Key(KsyPinned p) => p.Custom ? "file:" + p.File : p.Id;

    private static string Label(KsyPinned p) => p.Custom ? $"{p.Template.Title ?? p.Id} ({p.File})" : p.Template.Title ?? p.Id;

    private KsyPinned? FindByKey(string key) => Entries.FirstOrDefault(p => Key(p) == key);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
        _owner.DocumentChanged += Refresh;
        _owner.ViewChanged += Follow;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
        _owner.DocumentChanged -= Refresh;
        _owner.ViewChanged -= Follow;
    }

    private void ApplyTexts()
    {
        _title.Text = Loc.Get(K.KsyTitle);
        _open.Content = Loc.Get(K.KsyOpen);
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

    private string Extension() => Path.GetExtension(_owner.Document.FilePath ?? "").TrimStart('.').ToLowerInvariant();

    /// <summary>Chooses the template for the file again and reads its tree from the start; call after the panel opened or the document changed.</summary>
    public void Refresh()
    {
        if (!IsVisible || _refreshing) return;
        _refreshing = true;
        try
        {
            var doc = _owner.Document;
            var header = new byte[16];
            int got = 0;
            try { got = doc.GetSnapshot().Read(0, header); }
            catch (IOException) { _owner.FileChangedBehind(); return; }
            catch (ObjectDisposedException) { return; }
            _picker.ItemsSource = Entries;
            var host = _owner.TemplateHost;
            string ext = Extension();
            KsyPinned? pinned = null;
            if (ext.Length > 0)
            {
                if (host.Chosen.TryGetValue(ext, out string? key)) pinned = FindByKey(key);
                if (pinned is null && KsyCatalog.ParseChoices(host.ReadChoices()).TryGetValue(ext, out string? id)) pinned = KsyCatalog.ById(id);
            }
            pinned ??= KsyCatalog.Find(doc.FilePath, header.AsSpan(0, got));
            _picker.SelectedItem = pinned;
        }
        finally { _refreshing = false; }
        Rebuild();
    }

    private void PickerChanged()
    {
        if (_refreshing) return;
        if (_picker.SelectedItem is KsyPinned p && Extension() is { Length: > 0 } ext)
        {
            var host = _owner.TemplateHost;
            host.Chosen[ext] = Key(p);
            if (!p.Custom)
            {
                var saved = KsyCatalog.ParseChoices(host.ReadChoices());
                saved[ext] = p.Id;
                host.WriteChoices(KsyCatalog.FormatChoices(saved));
            }
        }
        Rebuild();
    }

    private void ShowNote(string text)
    {
        _note.Text = text;
        _note.IsVisible = true;
    }

    private void Rebuild()
    {
        if (!IsVisible) return;
        _tree.Items.Clear();
        _rootNode = null;
        _rootItem = null;
        _followed = -1;
        _note.IsVisible = Current is null;
        _note.Text = Loc.Get(K.KsyNone);
        _info.IsVisible = Current is not null;
        if (Current is not { } pinned) return;
        _info.Text = pinned.Custom ? Loc.Get(K.KsyInfoCustom, ("file", pinned.File)) : Loc.Get(K.KsyInfo, ("file", pinned.File), ("version", pinned.Version), ("license", pinned.License));
        DocumentSnapshot snapshot;
        try { snapshot = _owner.Document.GetSnapshot(); }
        catch (ObjectDisposedException) { return; }
        _rootNode = new KsyReader(_owner.ShownBytes(snapshot), pinned.Template).Root; // the fields of a hidden value read as stars
        _rootItem = Item(_rootNode);
        _tree.Items.Add(_rootItem);
        _rootItem.IsExpanded = true;
    }

    // ---- templates from files ----

    /// <summary>Asks for a <c>.ksy</c> file and offers its template in the list; a file that cannot be read or uses what the subset does not have is named in the note and changes nothing else.</summary>
    public async Task OpenFileAsync()
    {
        IsVisible = true;
        Refresh();
        if (_owner.PickTemplateFile is not { } pick) return;
        if (await pick() is { } path) LoadFile(path);
    }

    internal void LoadFile(string path)
    {
        IsVisible = true;
        Refresh();
        string name = Path.GetFileName(path);
        string text;
        try
        {
            if (new FileInfo(path).Length > KsyParser.MaxTemplateChars * 4L)
            {
                ShowNote(Loc.Get(K.KsyRefused, ("file", name), ("reason", KsyText.Describe(new KsyError(KsyErrorKind.LimitSize, 0, "template")))));
                return;
            }
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNote(Loc.Get(K.KsyOpenFailed, ("file", name), ("reason", e.Message)));
            return;
        }
        if (!KsyCatalog.TryParseCustom(text, name, out var custom, out var error))
        {
            ShowNote(Loc.Get(K.KsyRefused, ("file", name), ("reason", KsyText.Describe(error!))));
            return;
        }
        var host = _owner.TemplateHost;
        host.Loaded.RemoveAll(p => p.File == name);
        host.Loaded.Add(custom!);
        _refreshing = true;
        try { _picker.ItemsSource = Entries; }
        finally { _refreshing = false; }
        _picker.SelectedItem = custom;
    }

    // ---- the tree ----

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
        if (_following || _tree.SelectedItem is not TreeViewItem { Tag: KsyNode node }) return;
        _following = true; // the grid tells the panel about its own selection; the panel must not answer that
        try { _owner.Select(node.Start, node.End); }
        finally { _following = false; }
        _followed = _owner.Caret;
    }

    // ---- from the grid to the tree ----

    private void Follow()
    {
        if (!IsVisible || _following || _rootNode is null || _tree.IsKeyboardFocusWithin) return;
        long caret = _owner.Caret;
        if (caret == _followed) return;
        _followed = caret;
        SelectOffset(caret);
    }

    /// <summary>Opens the tree down to the field that holds the byte at <paramref name="offset"/> and selects it; the bytes selected in the grid stay as they are. False when no field holds it.</summary>
    internal bool SelectOffset(long offset)
    {
        if (_rootNode is null || _rootItem is null) return false;
        IReadOnlyList<KsyNode> path;
        try { path = _rootNode.PathTo(offset); }
        catch (IOException)
        {
            _owner.FileChangedBehind();
            return false;
        }
        if (path.Count == 0) return false;
        var item = _rootItem;
        for (int i = 1; i < path.Count; i++)
        {
            item.IsExpanded = true;
            Populate(item);
            var next = item.Items.OfType<TreeViewItem>().FirstOrDefault(c => ReferenceEquals(c.Tag, path[i]));
            if (next is null) break;
            item = next;
        }
        _following = true;
        try { _tree.SelectedItem = item; }
        finally { _following = false; }
        item.BringIntoView();
        return true;
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
            KsyErrorKind.LimitTime => K.KsyErrLimitTime,
            _ => K.KsyErrLimitWork,
        };
        string text = Loc.Get(key, ("subject", error.Subject));
        return error.Line > 0 ? text + " " + Loc.Get(K.KsyErrLine, ("line", error.Line)) : text;
    }
}

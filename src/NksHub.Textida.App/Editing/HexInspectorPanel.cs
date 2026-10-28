using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The data inspector beside the hex grid: the bytes at the caret read as every type of <see cref="DataInspector"/>, in little or big endian order.
/// Enter on a row asks for a new value; the value is checked while it is typed and written as one undo step.
/// </summary>
internal sealed class HexInspectorPanel : Border
{
    private const int ReadWindow = 16;
    private readonly HexView _owner;
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Single, Focusable = true, Background = Avalonia.Media.Brushes.Transparent };
    private readonly ToggleButton _little = new() { MinWidth = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly ToggleButton _big = new() { MinWidth = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _title = new() { FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBox _edit = new() { IsVisible = false };
    private readonly TextBlock _error = new() { IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _hint = new() { IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly InspectorKind[] _kinds = [.. DataInspector.All];
    private InspectorValue?[] _values = [];
    private bool _bigEndian;
    private InspectorKind? _editing;
    private long _editStart;
    private bool _refreshing;

    public HexInspectorPanel(HexView owner)
    {
        _owner = owner;
        Classes.Add("sidepanel");
        Width = 260;
        Padding = new Thickness(10, 8);
        _little.Click += (_, _) => SetEndian(false);
        _big.Click += (_, _) => SetEndian(true);
        _little.IsChecked = true;
        _edit.TextChanged += (_, _) => Validate();
        _edit.KeyDown += OnEditKey;
        _list.KeyDown += OnListKey;
        _list.DoubleTapped += (_, _) => BeginEdit();
        var order = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 6, 0, 6), Children = { _little, _big } };
        var stack = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(order, Dock.Top);
        var editor = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0), Children = { _hint, _edit, _error } };
        DockPanel.SetDock(editor, Dock.Bottom);
        stack.Children.Add(_title);
        stack.Children.Add(order);
        stack.Children.Add(editor);
        stack.Children.Add(_list);
        Child = stack;
        ApplyTexts();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Loc.Changed += ApplyTexts;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Loc.Changed -= ApplyTexts;
    }

    public bool IsEditing => _editing is not null;
    public bool BigEndian => _bigEndian;
    internal TextBox EditBox => _edit;
    internal ListBox Rows => _list;
    /// <summary>The error line under the edit box, or null; for tests.</summary>
    internal string? ErrorShown => _error.IsVisible ? _error.Text : null;

    /// <summary>Row texts, for tests.</summary>
    internal IReadOnlyList<string> RowTexts => [.. _list.Items.OfType<ListBoxItem>().Select(i => AutomationProperties.GetName(i))];

    private void ApplyTexts()
    {
        _title.Text = Loc.Get(K.HexInspectorTitle);
        _little.Content = Loc.Get(K.HexInspectorLittle);
        _big.Content = Loc.Get(K.HexInspectorBig);
        AutomationProperties.SetName(_little, Loc.Get(K.HexInspectorLittleA11y));
        AutomationProperties.SetName(_big, Loc.Get(K.HexInspectorBigA11y));
        AutomationProperties.SetName(_list, Loc.Get(K.HexInspectorTitle));
        AutomationProperties.SetName(_edit, Loc.Get(K.HexInspectorEditA11y));
        Refresh();
    }

    private void SetEndian(bool big)
    {
        _bigEndian = big;
        _little.IsChecked = !big;
        _big.IsChecked = big;
        Refresh();
        if (IsEditing) Validate();
    }

    private static string KindName(InspectorKind kind) => kind switch
    {
        InspectorKind.Int8 => "int8",
        InspectorKind.UInt8 => "uint8",
        InspectorKind.Int16 => "int16",
        InspectorKind.UInt16 => "uint16",
        InspectorKind.Int32 => "int32",
        InspectorKind.UInt32 => "uint32",
        InspectorKind.Int64 => "int64",
        InspectorKind.UInt64 => "uint64",
        InspectorKind.Uleb128 => "ULEB128",
        InspectorKind.Sleb128 => "SLEB128",
        InspectorKind.Float16 => "float16",
        InspectorKind.Float32 => "float32",
        InspectorKind.Float64 => "float64",
        InspectorKind.UnixTime32 => "Unix time 32",
        InspectorKind.UnixTime64 => "Unix time 64",
        InspectorKind.FileTime => "FILETIME",
        InspectorKind.DotNetTicks => ".NET ticks",
        InspectorKind.Latin1 => "Latin-1",
        InspectorKind.Utf8 => "UTF-8",
        InspectorKind.Utf16 => "UTF-16",
        InspectorKind.Utf32 => "UTF-32",
        _ => "GUID",
    };

    /// <summary>
    /// The type and the length of the value the selected row shows at the caret, for a bookmark made from it (<c>uint32 LE</c>, 4); null while the panel is closed,
    /// when the row is not a complete value, or when there is a selection (the selection is then the region).
    /// </summary>
    internal (string Type, int Length)? TypedValue()
    {
        int index = _list.SelectedIndex;
        if (!IsVisible || _owner.Editor.HasSelection || index < 0 || index >= _values.Length || _values[index] is not { Status: InspectorStatus.Ok } v || v.Length <= 0) return null;
        var kind = _kinds[index];
        bool ordered = kind is not (InspectorKind.Int8 or InspectorKind.UInt8 or InspectorKind.Uleb128 or InspectorKind.Sleb128 or InspectorKind.Latin1 or InspectorKind.Utf8 or InspectorKind.Guid);
        return (KindName(kind) + (ordered ? (_bigEndian ? " BE" : " LE") : ""), v.Length);
    }

    private long ValueStart => _owner.Editor.HasSelection ? _owner.Editor.Selection.Start : _owner.Editor.Caret;

    private string Show(InspectorValue v) => v.Status switch
    {
        InspectorStatus.Ok => v.Text,
        InspectorStatus.Incomplete => Loc.Get(K.HexInspectorIncomplete, ("need", v.Length)),
        _ => Loc.Get(K.HexInspectorInvalid),
    };

    /// <summary>Reads the bytes at the caret again; call after the caret or the document moved.</summary>
    public void Refresh()
    {
        if (!IsVisible || _refreshing) return;
        _refreshing = true;
        try
        {
            var doc = _owner.Editor.Document;
            long start = ValueStart;
            var data = new byte[(int)Math.Max(0, Math.Min(ReadWindow, doc.ByteLength - start))];
            try { doc.Read(start, data); }
            catch (IOException) { _owner.FileChangedBehind(); return; }
            bool hidden = _owner.TouchesHidden(start, start + data.Length); // a value of a secrets file is not shown as any type
            _owner.MaskHidden(start, data);
            _values = [.. _kinds.Select(k => DataInspector.Decode(k, data, _bigEndian))];
            int selected = Math.Max(0, _list.SelectedIndex);
            _list.Items.Clear();
            for (int i = 0; i < _kinds.Length; i++)
            {
                var v = _values[i]!;
                string name = KindName(_kinds[i]), shown = hidden ? Loc.Get(K.SecretHiddenValue) : Show(v);
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*") };
                row.Children.Add(new TextBlock { Text = name, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center });
                var value = new TextBlock { Text = shown, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontFamily = new Avalonia.Media.FontFamily(_owner.Options.FontFamily) };
                if (v.Status != InspectorStatus.Ok) value.Opacity = 0.65;
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                var item = new ListBoxItem { Content = row, Padding = new Thickness(6, 2) };
                AutomationProperties.SetName(item, $"{name}: {shown}");
                _list.Items.Add(item);
            }
            _list.SelectedIndex = Math.Min(selected, _kinds.Length - 1);
        }
        finally { _refreshing = false; }
    }

    public bool FocusList()
    {
        IsVisible = true;
        Refresh();
        return _list.Focus();
    }

    // ---- editing a value ----

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) { BeginEdit(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _owner.Focus(); e.Handled = true; }
        else if (e.Key is Key.L or Key.B && e.KeyModifiers == KeyModifiers.None) { SetEndian(e.Key == Key.B); e.Handled = true; }
    }

    private void BeginEdit()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _kinds.Length) return;
        var value = _values[index]!;
        if (value.Status == InspectorStatus.Incomplete)
        {
            ShowError(Loc.Get(K.HexInspectorIncompleteEdit));
            return;
        }
        _editing = _kinds[index];
        _editStart = ValueStart;
        _edit.IsVisible = true;
        _hint.IsVisible = true;
        _hint.Text = Loc.Get(K.HexInspectorEditHint, ("type", KindName(_kinds[index])));
        _edit.Text = value.Status == InspectorStatus.Ok ? EditText(_kinds[index], value) : "";
        _edit.Focus();
        _edit.SelectAll();
        Validate();
    }

    /// <summary>The text to start a new value from: the shown value without the code point that follows a character.</summary>
    private static string EditText(InspectorKind kind, InspectorValue v) =>
        kind is InspectorKind.Latin1 or InspectorKind.Utf8 or InspectorKind.Utf16 or InspectorKind.Utf32 ? v.Text[(v.Text.LastIndexOf("U+", StringComparison.Ordinal))..] : v.Text;

    private void ShowError(string? text)
    {
        _error.IsVisible = !string.IsNullOrEmpty(text);
        _error.Text = text ?? "";
        if (_error.IsVisible) _error.Foreground = (Avalonia.Media.IBrush?)(this.TryFindResource("Danger", ActualThemeVariant, out var b) ? b : null) ?? Avalonia.Media.Brushes.Red;
    }

    /// <summary>Checks the text against the type while it is typed, so a value out of range is told at once.</summary>
    private bool Validate(out byte[] bytes)
    {
        bytes = [];
        if (_editing is not { } kind) return false;
        if (!DataInspector.TryEncode(kind, _edit.Text ?? "", _bigEndian, out bytes, out string error))
        {
            ShowError(ErrorText(error));
            return false;
        }
        ShowError(null);
        return true;
    }

    private void Validate() => Validate(out _);

    private static string ErrorText(string token)
    {
        if (token.StartsWith("range ", StringComparison.Ordinal)) return Loc.Get(K.HexInspectorRange, ("range", token[6..]));
        return token switch
        {
            "empty" => Loc.Get(K.HexInspectorEmpty),
            "date" => Loc.Get(K.HexInspectorDate),
            "guid" => Loc.Get(K.HexInspectorGuid),
            "character" => Loc.Get(K.HexInspectorCharacter),
            "range" => Loc.Get(K.HexInspectorRangeFloat),
            _ => Loc.Get(K.HexInspectorFormat),
        };
    }

    private void OnEditKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { EndEdit(); e.Handled = true; }
        else if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
    }

    private void EndEdit()
    {
        _editing = null;
        _edit.IsVisible = _hint.IsVisible = false;
        ShowError(null);
        _list.Focus();
    }

    /// <summary>Writes the typed value over the bytes it was read from, as one undo step; a value of another length needs insert mode.</summary>
    private void Commit()
    {
        if (!Validate(out var bytes) || _editing is not { } kind) return;
        int index = Array.IndexOf(_kinds, kind);
        var current = _values[index]!;
        int length = current.Status == InspectorStatus.Ok ? current.Length : DataInspector.SizeOf(kind);
        if (length == 0) length = bytes.Length;
        if (bytes.Length != length && !_owner.Insert)
        {
            ShowError(Loc.Get(K.HexInspectorLength, ("old", length), ("new", bytes.Length)));
            return;
        }
        var doc = _owner.Editor.Document;
        if (_editStart + length > doc.ByteLength)
        {
            ShowError(Loc.Get(K.HexInspectorIncompleteEdit));
            return;
        }
        var result = _owner.Editor.Write(new ByteRange(_editStart, _editStart + length), bytes);
        _owner.Do(result);
        if (result.Applied) EndEdit();
    }
}

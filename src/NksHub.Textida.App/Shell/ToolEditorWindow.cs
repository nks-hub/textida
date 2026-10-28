using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The one editor for the tool definitions of <c>tools.json</c>. New, Save and Delete write the file at once. The line under the form shows the command the
/// definition starts for the open file; it comes from the same resolution the launcher uses, so the tool starts with these arguments from the menu too.
/// </summary>
internal sealed class ToolEditorWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly ToolCatalog _catalog;
    private readonly Func<ExternalToolContribution, string> _preview;
    private List<ExternalToolContribution> _definitions = [];
    private int _index = -1;
    private bool _loading;

    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBox _id = new() { FontFamily = Mono }, _title = new(), _program = new() { FontFamily = Mono }, _cwd = new() { FontFamily = Mono },
        _timeout = new() { FontFamily = Mono }, _pattern = new() { FontFamily = Mono }, _encoding = new() { FontFamily = Mono };
    private readonly TextBox _args = new() { FontFamily = Mono, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 90 };
    private readonly TextBox _variables = new() { FontFamily = Mono, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 70 };
    private readonly TextBlock _listLabel = new(), _idLabel = new(), _titleLabel = new(), _programLabel = new(), _argsLabel = new(), _cwdLabel = new(),
        _timeoutLabel = new(), _patternLabel = new(), _encodingLabel = new(), _variablesLabel = new();
    private readonly TextBlock _previewLine = new() { TextWrapping = TextWrapping.Wrap, FontFamily = Mono, FontSize = 12 };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _new = new() { MinWidth = 84 }, _delete = new() { MinWidth = 84 }, _save = new() { IsDefault = true, MinWidth = 100 }, _close = new() { IsCancel = true, MinWidth = 84 };

    public ToolEditorWindow(ToolCatalog catalog, Func<ExternalToolContribution, string> preview)
    {
        HelpContext.OnF1(this, "tools.edit");
        _catalog = catalog;
        _preview = preview;
        Classes.Add("shell");
        Width = 900;
        Height = 660;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var label in new[] { _listLabel, _idLabel, _titleLabel, _programLabel, _argsLabel, _cwdLabel, _timeoutLabel, _patternLabel, _encodingLabel, _variablesLabel }) label.FontWeight = FontWeight.SemiBold;

        var form = new StackPanel { Spacing = 4 };
        foreach (var (label, box) in new (TextBlock, Control)[]
        {
            (_idLabel, _id), (_titleLabel, _title), (_programLabel, _program), (_argsLabel, _args), (_cwdLabel, _cwd), (_timeoutLabel, _timeout), (_patternLabel, _pattern), (_encodingLabel, _encoding), (_variablesLabel, _variables),
        })
        {
            form.Children.Add(label);
            form.Children.Add(box);
        }
        var listButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _new, _delete } };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
        void Place(Control c, int row) { Grid.SetRow(c, row); left.Children.Add(c); }
        Place(_listLabel, 0);
        Place(new Border { Child = _list, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 1);
        Place(listButtons, 2);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 16, Margin = new Thickness(18, 14) };
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(left);
        body.Children.Add(scroll);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _save, _close } };
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 6, Children = { _previewLine, _note, buttons } };
        var root = new ReadingDockPanel { Children = { footer, body } };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        foreach (var box in new[] { _id, _title, _program, _cwd, _timeout, _pattern, _encoding, _args, _variables }) box.TextChanged += (_, _) => RefreshPreview();
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedIndex >= 0) Select(_list.SelectedIndex); };
        _new.Click += (_, _) => NewNow();
        _delete.Click += (_, _) => DeleteNow();
        _save.Click += (_, _) => SaveNow();
        _close.Click += (_, _) => Close();
        // subscribed only while open, so a window that is never shown or is closed does not stay reachable from Loc
        Opened += (_, _) => { Loc.Changed += OnLanguageChanged; ApplyTexts(); _id.Focus(); };
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        Reload();
        ApplyTexts();
    }

    internal string NoteText => _note.Text ?? "";
    internal string PreviewText => _previewLine.Text ?? "";
    internal IReadOnlyList<string> Titles => [.. _definitions.Select(TitleOf)];
    internal ToolDraft Draft
    {
        get => new()
        {
            Id = _id.Text ?? "", Title = _title.Text ?? "", Executable = _program.Text ?? "", Args = _args.Text ?? "", Cwd = _cwd.Text ?? "",
            Timeout = _timeout.Text ?? "", LogPattern = _pattern.Text ?? "", OutputEncoding = _encoding.Text ?? "", Variables = _variables.Text ?? "",
        };
        set
        {
            _id.Text = value.Id; _title.Text = value.Title; _program.Text = value.Executable; _args.Text = value.Args; _cwd.Text = value.Cwd;
            _timeout.Text = value.Timeout; _pattern.Text = value.LogPattern; _encoding.Text = value.OutputEncoding; _variables.Text = value.Variables;
        }
    }

    private string TitleOf(ExternalToolContribution t) => t.Title.Length > 0 ? t.Title : t.Id.Length > 0 ? t.Id : Loc.Get(K.ToolEditUntitled);

    private void Reload()
    {
        try { _definitions = _catalog.LoadDefinitions(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            _definitions = [];
            SetNote(Loc.Get(K.ToolEditUnreadable, ("reason", e.Message)), error: true);
        }
        RefreshList(_definitions.Count > 0 ? 0 : -1);
    }

    private void RefreshList(int select)
    {
        _loading = true;
        _list.ItemsSource = _definitions.Select(TitleOf).ToList();
        _list.SelectedIndex = select;
        _loading = false;
        Select(select);
    }

    /// <summary>Shows the definition with this number in the form; -1 gives an empty form for a new tool.</summary>
    internal void Select(int index)
    {
        _index = index >= 0 && index < _definitions.Count ? index : -1;
        Draft = _index < 0 ? new ToolDraft() : ToolDraft.From(_definitions[_index]);
        _delete.IsEnabled = _index >= 0;
        RefreshPreview();
    }

    internal void NewNow()
    {
        _loading = true;
        _list.SelectedIndex = -1;
        _loading = false;
        Select(-1);
        SetNote("", error: false);
        _id.Focus();
    }

    /// <summary>Checks the form, puts the definition in the list and writes the file; the note says what is wrong when it does not go through.</summary>
    internal bool SaveNow()
    {
        var definition = Draft.ToDefinition(out string? error);
        if (definition is null)
        {
            SetNote(error!, error: true);
            return false;
        }
        for (int i = 0; i < _definitions.Count; i++)
            if (i != _index && _definitions[i].Id == definition.Id)
            {
                SetNote(Loc.Get(K.ToolEditDuplicate, ("id", definition.Id)), error: true);
                return false;
            }
        var next = new List<ExternalToolContribution>(_definitions);
        int at = _index;
        if (at < 0) { next.Add(definition); at = next.Count - 1; }
        else next[at] = definition;
        if (!Write(next)) return false;
        _definitions = next;
        RefreshList(at);
        SetNote(Loc.Get(K.ToolEditSaved, ("title", definition.Title)), error: false);
        return true;
    }

    internal bool DeleteNow()
    {
        if (_index < 0) return false;
        var next = new List<ExternalToolContribution>(_definitions);
        string title = TitleOf(next[_index]);
        next.RemoveAt(_index);
        if (!Write(next)) return false;
        int at = Math.Min(_index, next.Count - 1);
        _definitions = next;
        RefreshList(at);
        SetNote(Loc.Get(K.ToolEditDeleted, ("title", title)), error: false);
        return true;
    }

    private bool Write(List<ExternalToolContribution> tools)
    {
        try
        {
            _catalog.Save(tools);
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            SetNote(Loc.Get(K.ToolEditWriteFailed, ("reason", e.Message)), error: true);
            return false;
        }
    }

    private void RefreshPreview()
    {
        var definition = Draft.ToDefinition(out _);
        _previewLine.Text = definition is null ? "" : _preview(definition);
    }

    private void SetNote(string text, bool error)
    {
        _note.Text = text;
        _note.Foreground = error ? ThemeColors.Danger : null;
    }

    private void OnLanguageChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.ToolEditTitle);
        _listLabel.Text = Loc.Get(K.ToolEditList);
        _idLabel.Text = Loc.Get(K.ToolEditId);
        _titleLabel.Text = Loc.Get(K.ToolEditName);
        _programLabel.Text = Loc.Get(K.ToolEditProgram);
        _argsLabel.Text = Loc.Get(K.ToolEditArgs);
        _cwdLabel.Text = Loc.Get(K.ToolEditCwd);
        _timeoutLabel.Text = Loc.Get(K.ToolEditTimeout);
        _patternLabel.Text = Loc.Get(K.ToolEditPattern);
        _encodingLabel.Text = Loc.Get(K.ToolEditEncoding);
        _variablesLabel.Text = Loc.Get(K.ToolEditVariables);
        _new.Content = Loc.Get(K.ToolEditNew);
        _delete.Content = Loc.Get(K.ToolEditDelete);
        _save.Content = Loc.Get(K.ToolEditSave);
        _close.Content = Loc.Get(K.PanelOutputClose);
        AutomationProperties.SetName(_list, Loc.Get(K.ToolEditList));
        AutomationProperties.SetName(_id, Loc.Get(K.ToolEditId));
        AutomationProperties.SetName(_title, Loc.Get(K.ToolEditName));
        AutomationProperties.SetName(_program, Loc.Get(K.ToolEditProgram));
        AutomationProperties.SetName(_args, Loc.Get(K.ToolEditArgs));
        AutomationProperties.SetName(_cwd, Loc.Get(K.ToolEditCwd));
        AutomationProperties.SetName(_timeout, Loc.Get(K.ToolEditTimeout));
        AutomationProperties.SetName(_pattern, Loc.Get(K.ToolEditPattern));
        AutomationProperties.SetName(_encoding, Loc.Get(K.ToolEditEncoding));
        AutomationProperties.SetName(_variables, Loc.Get(K.ToolEditVariables));
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        RefreshPreview();
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Icons;
using NksHub.Textida.Workbench.Matching;
using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.App.Shell;

/// <summary>One line of the lists of the toolbar editor: a command (or the separator), how it is named and drawn, and whether its command is there to run.</summary>
internal sealed record ToolbarChoice(string Id, string Title, string? Category, IconShape? Icon, bool Available);

/// <summary>
/// The editor of the toolbar buttons. Commands that can be a button are on the left (with a search box); the buttons of the toolbar, in order, are on the right. Add puts a command after the
/// chosen button, Remove, Move Up and Move Down change the list, Add Separator puts a thin line in it, and Restore Defaults goes back to the set a profile starts with. OK writes
/// <c>toolbar.json</c> and the toolbars of all windows follow at once; Cancel leaves it as it was. Everything works with the keyboard: Enter adds, Delete removes, Alt+Up and Alt+Down move.
/// </summary>
internal sealed class ToolbarEditorWindow : Window
{
    private readonly AppWorkbench _wb;
    private readonly ToolbarStore _store;
    private List<string> _buttons;
    private List<ToolbarChoice> _choices = [];
    private List<ToolbarChoice> _listed = [];

    private readonly TextBox _search = new();
    private readonly ListBox _available = new() { Background = Brushes.Transparent };
    private readonly ListBox _current = new() { Background = Brushes.Transparent };
    private readonly TextBlock _availableLabel = new(), _currentLabel = new(), _countLabel = new(), _empty = new() { Margin = new Thickness(8, 6), IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _keys = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _add = new() { MinWidth = 120 }, _addSeparator = new() { MinWidth = 120 }, _remove = new() { MinWidth = 120 }, _up = new() { MinWidth = 120 }, _down = new() { MinWidth = 120 },
        _defaults = new() { MinWidth = 120 }, _ok = new() { IsDefault = true, MinWidth = 90 }, _cancel = new() { IsCancel = true, MinWidth = 90 };
    private bool _loading;

    public ToolbarEditorWindow(AppWorkbench wb, ToolbarStore store)
    {
        HelpContext.OnF1(this, "view.toolbar");
        _wb = wb;
        _store = store;
        _buttons = [.. store.Load()];
        Classes.Add("shell");
        Width = 900;
        Height = 620;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        foreach (var label in new[] { _availableLabel, _currentLabel }) label.FontWeight = FontWeight.SemiBold;

        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 4 };
        void Place(Grid grid, Control c, int row) { Grid.SetRow(c, row); grid.Children.Add(c); }
        Place(left, _availableLabel, 0);
        Place(left, _search, 1);
        Place(left, new Border { Child = new Panel { Children = { _available, _empty } }, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 2);

        var middle = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _add, _addSeparator } };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 4 };
        Place(right, _currentLabel, 0);
        Place(right, new Border { Child = _current, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 1);
        Place(right, new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { _up, _down, _remove } }, 2);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 16, Margin = new Thickness(18, 14) };
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(right, 2);
        body.Children.Add(left);
        body.Children.Add(middle);
        body.Children.Add(right);

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(_countLabel, 1);
        Grid.SetColumn(_ok, 2);
        Grid.SetColumn(_cancel, 3);
        buttons.Children.Add(_defaults);
        buttons.Children.Add(_countLabel);
        buttons.Children.Add(_ok);
        buttons.Children.Add(_cancel);
        _countLabel.VerticalAlignment = VerticalAlignment.Center;
        _countLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 6, Children = { _keys, _note, buttons } };
        var root = new ReadingDockPanel { Children = { footer, body } };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        _search.TextChanged += (_, _) => RefreshAvailable();
        _available.SelectionChanged += (_, _) => RefreshButtons();
        _current.SelectionChanged += (_, _) => { if (!_loading) RefreshButtons(); };
        _available.DoubleTapped += (_, _) => AddSelected();
        _available.KeyDown += (_, e) => { if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None) { AddSelected(); e.Handled = true; } };
        // in the tunnel, so the list does not take Alt+Up and Alt+Down for its own movement of the selection first
        _current.AddHandler(KeyDownEvent, OnCurrentKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _add.Click += (_, _) => AddSelected();
        _addSeparator.Click += (_, _) => AddSeparator();
        _remove.Click += (_, _) => RemoveSelected();
        _up.Click += (_, _) => MoveSelected(-1);
        _down.Click += (_, _) => MoveSelected(1);
        _defaults.Click += (_, _) => RestoreDefaults();
        _ok.Click += (_, _) => Save();
        _cancel.Click += (_, _) => Close();
        // subscribed only while open, so a window that is never shown or is closed does not stay reachable from Loc
        Opened += (_, _) => { Loc.Changed += OnLanguageChanged; ApplyTexts(); _search.Focus(); };
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        ApplyTexts();
    }

    /// <summary>The buttons as they stand in the editor, not yet saved.</summary>
    internal IReadOnlyList<string> Buttons => _buttons;

    /// <summary>The ids of the commands listed on the left, in the order shown.</summary>
    internal IReadOnlyList<string> AvailableIds => [.. _listed.Select(c => c.Id)];

    /// <summary>The lines of the list of buttons, as they are shown.</summary>
    internal IReadOnlyList<ToolbarChoice> Lines => [.. _buttons.Select(ChoiceOf)];

    internal string NoteText => _note.Text ?? "";
    internal string CountText => _countLabel.Text ?? "";
    internal TextBox SearchBox => _search;
    internal ListBox AvailableList => _available;
    internal ListBox CurrentList => _current;
    internal Button AddButton => _add;
    internal Button AddSeparatorButton => _addSeparator;
    internal Button RemoveButton => _remove;
    internal Button UpButton => _up;
    internal Button DownButton => _down;
    internal Button DefaultsButton => _defaults;
    internal Button OkButton => _ok;
    internal Button CancelButton => _cancel;

    internal void SetSearch(string text)
    {
        _search.Text = text;
        RefreshAvailable(); // the box reports its change later, on the dispatcher
    }

    internal void SelectAvailable(string id) => _available.SelectedIndex = _listed.FindIndex(c => c.Id == id);

    internal void SelectButton(int index) => _current.SelectedIndex = index;

    // ---- the lists ----

    private ToolbarChoice ChoiceOf(string id)
    {
        if (id == ToolbarLayout.Separator) return new ToolbarChoice(id, Loc.Get(K.ToolbarEditSeparatorName), null, null, true);
        return _choices.FirstOrDefault(c => c.Id == id) ?? new ToolbarChoice(id, Loc.Get(K.ToolbarEditUnavailable, ("title", id)), null, null, false);
    }

    private void RebuildChoices()
    {
        var commands = _wb.Commands;
        _choices =
        [
            .. commands.Commands.Where(c => !c.Hidden && commands.HasHandler(c.Id))
                .Select(c => new ToolbarChoice(c.Id, _wb.Strings.Get(c.TitleKey), c.CategoryKey is null ? null : _wb.Strings.Get(c.CategoryKey), commands.IconOf(c.Id), true))
                .OrderBy(c => c.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Id, StringComparer.Ordinal),
        ];
    }

    private static string NameOf(ToolbarChoice c) => c.Category is null ? c.Title : $"{c.Title}, {c.Category}";

    private Control RowOf(ToolbarChoice choice, bool withCategory)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*") };
        if (choice.Icon is { } icon)
        {
            var picture = CommandIcons.Create(icon);
            picture.VerticalAlignment = VerticalAlignment.Center;
            picture.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(picture);
        }
        // a long name wraps instead of being cut off, and the category stands under it
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = choice.Title, TextWrapping = TextWrapping.Wrap, Opacity = choice.Available ? 1 : 0.6 });
        if (withCategory && choice.Category is not null) text.Children.Add(new TextBlock { Text = choice.Category, TextWrapping = TextWrapping.Wrap, Opacity = 0.6, FontSize = 12 });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        AutomationProperties.SetName(grid, withCategory ? NameOf(choice) : choice.Title);
        return grid;
    }

    private void RefreshAvailable()
    {
        var words = TextMatching.Fold(_search.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        _listed = [.. _choices.Where(c => words.All(w => TextMatching.Fold(c.Title + " " + c.Category).Contains(w, StringComparison.Ordinal)))];
        _available.Items.Clear();
        foreach (var choice in _listed) _available.Items.Add(new ListBoxItem { Content = RowOf(choice, withCategory: true) });
        _empty.IsVisible = _listed.Count == 0;
        _available.IsVisible = _listed.Count > 0;
        if (_listed.Count > 0) _available.SelectedIndex = 0;
        RefreshButtons();
    }

    private void RefreshCurrent(int select)
    {
        _loading = true;
        _current.Items.Clear();
        foreach (var id in _buttons) _current.Items.Add(new ListBoxItem { Content = RowOf(ChoiceOf(id), withCategory: false) });
        _current.SelectedIndex = select < 0 || _buttons.Count == 0 ? -1 : Math.Min(select, _buttons.Count - 1);
        _loading = false;
        _countLabel.Text = Loc.Plural(K.ToolbarEditCount, _buttons.Count(b => b != ToolbarLayout.Separator));
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        int at = _current.SelectedIndex;
        _add.IsEnabled = _available.SelectedIndex >= 0;
        _remove.IsEnabled = at >= 0;
        _up.IsEnabled = at > 0;
        _down.IsEnabled = at >= 0 && at < _buttons.Count - 1;
    }

    // ---- changes ----

    /// <summary>Puts the command chosen on the left into the toolbar, after the button chosen on the right (at the end without one).</summary>
    internal bool AddSelected()
    {
        int i = _available.SelectedIndex;
        return i >= 0 && i < _listed.Count && AddCommand(_listed[i].Id);
    }

    /// <summary>Puts a command into the toolbar. False, with the reason in the note, when it is there already or the toolbar is full.</summary>
    internal bool AddCommand(string id)
    {
        var choice = ChoiceOf(id);
        if (_buttons.Contains(id))
        {
            SetNote(Loc.Get(K.ToolbarEditHasAlready, ("title", choice.Title)), error: true);
            return false;
        }
        if (!HasRoom()) return false;
        int at = Insert(id);
        RefreshCurrent(at);
        SetNote(Loc.Get(K.ToolbarEditAdded, ("title", choice.Title)), error: false);
        return true;
    }

    internal bool AddSeparator()
    {
        if (!HasRoom()) return false;
        RefreshCurrent(Insert(ToolbarLayout.Separator));
        SetNote("", error: false);
        return true;
    }

    private bool HasRoom()
    {
        int max = PluginLimitsPlaceholders.ToolbarMaxButtons;
        if (_buttons.Count < max) return true;
        SetNote(Loc.Plural(K.ToolbarEditFull, max), error: true);
        return false;
    }

    private int Insert(string id)
    {
        int at = _current.SelectedIndex >= 0 ? _current.SelectedIndex + 1 : _buttons.Count;
        _buttons.Insert(at, id);
        return at;
    }

    internal bool RemoveSelected()
    {
        int at = _current.SelectedIndex;
        if (at < 0 || at >= _buttons.Count) return false;
        var choice = ChoiceOf(_buttons[at]);
        _buttons.RemoveAt(at);
        RefreshCurrent(at);
        SetNote(choice.Id == ToolbarLayout.Separator ? "" : Loc.Get(K.ToolbarEditRemoved, ("title", choice.Title)), error: false);
        return true;
    }

    internal bool MoveSelected(int delta)
    {
        int at = _current.SelectedIndex, to = at + delta;
        if (at < 0 || to < 0 || to >= _buttons.Count) return false;
        (_buttons[at], _buttons[to]) = (_buttons[to], _buttons[at]);
        RefreshCurrent(to);
        _current.Focus();
        return true;
    }

    internal void RestoreDefaults()
    {
        _buttons = [.. ToolbarLayout.Default];
        RefreshCurrent(-1);
        SetNote("", error: false);
    }

    /// <summary>Writes <c>toolbar.json</c> and closes; the note says why when the file cannot be written.</summary>
    internal bool Save()
    {
        if (!_store.Save(_buttons, out string detail))
        {
            SetNote(Loc.Get(K.ToolbarEditWriteFailed, ("reason", detail)), error: true);
            return false;
        }
        Close();
        return true;
    }

    private void OnCurrentKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None) e.Handled = RemoveSelected();
        else if (e.Key is Key.Up or Key.Down && e.KeyModifiers == KeyModifiers.Alt)
        {
            MoveSelected(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
    }

    private void SetNote(string text, bool error)
    {
        _note.Text = text;
        _note.Foreground = error ? ThemeColors.Danger : null;
    }

    // ---- text ----

    private void OnLanguageChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.ToolbarEditTitle);
        _availableLabel.Text = Loc.Get(K.ToolbarEditAvailable);
        _currentLabel.Text = Loc.Get(K.ToolbarEditCurrent);
        _search.PlaceholderText = Loc.Get(K.ToolbarEditSearch);
        _empty.Text = Loc.Get(K.ToolbarEditNoMatch);
        _keys.Text = Loc.Get(K.ToolbarEditKeys);
        _add.Content = Loc.Get(K.ToolbarEditAdd);
        _addSeparator.Content = Loc.Get(K.ToolbarEditAddSeparator);
        _remove.Content = Loc.Get(K.ToolbarEditRemove);
        _up.Content = Loc.Get(K.ToolbarEditUp);
        _down.Content = Loc.Get(K.ToolbarEditDown);
        _defaults.Content = Loc.Get(K.ToolbarEditDefaults);
        _ok.Content = Loc.Get(K.DialogOk);
        _cancel.Content = Loc.Get(K.DialogCancel);
        AutomationProperties.SetName(_search, Loc.Get(K.ToolbarEditSearch));
        AutomationProperties.SetName(_available, Loc.Get(K.ToolbarEditAvailable));
        AutomationProperties.SetName(_current, Loc.Get(K.ToolbarEditCurrent));
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        string? problem = _store.Problem;
        if (problem is not null && _note.Text is null or "") SetNote(Loc.Get(K.ToolbarUnreadable, ("reason", problem)), error: true);
        RebuildChoices();
        int keep = _current.SelectedIndex;
        RefreshAvailable();
        RefreshCurrent(keep);
    }
}

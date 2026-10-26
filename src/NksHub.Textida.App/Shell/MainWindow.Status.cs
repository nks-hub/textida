using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private readonly TextBlock _statusLeft = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _indentItem = StatusItem();
    private readonly Button _encodingItem = StatusItem();
    private readonly Button _eolItem = StatusItem();
    private readonly Button _languageItem = StatusItem();
    private readonly Button _configItem = StatusItem();
    private readonly StackPanel _statusRight = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private static Button StatusItem()
    {
        var b = new Button { VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("statusitem");
        return b;
    }

    /// <summary>Status bar texts, for tests and accessibility checks.</summary>
    internal string StatusLeftText => _statusLeft.Text ?? "";
    internal string StatusRightText => string.Join(" · ", new[] { _indentItem, _encodingItem, _eolItem, _languageItem, _configItem }.Where(b => b.IsVisible).Select(b => (string)b.Content!));
    internal Button IndentItem => _indentItem;
    internal Button EncodingItem => _encodingItem;
    internal Button EolItem => _eolItem;
    internal Button LanguageItem => _languageItem;

    private Control BuildStatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_statusRight, 1);
        grid.Children.Add(_statusLeft);
        grid.Children.Add(_statusRight);
        _indentItem.Click += (_, _) => ShowIndentMenu();
        _encodingItem.Click += (_, _) => ShowEncodingMenu(null);
        _eolItem.Click += (_, _) => ShowEolMenu();
        _languageItem.Click += (_, _) => ShowLanguageMenu();
        _configItem.Click += (_, _) => OpenEditorConfigFile();
        _configItem.IsVisible = false;
        return grid;
    }

    private void UpdateStatus()
    {
        CheckJournalHealth();
        _statusRight.Children.Clear();
        if (Active is not { } tab)
        {
            _statusLeft.Text = Loc.Get(K.StatusReady);
            return;
        }
        if (tab.Custom is not null)
        {
            ShowResultsStatus();
            return;
        }

        var od = tab.Doc;
        var opts = Options;
        string position;
        if (tab.Surface is { } surface)
        {
            var (line, column) = surface.CaretLineColumn();
            position = Loc.Get(K.StatusPosition, ("line", line), ("column", column));
            int selected = surface.TextArea.Selection.Length;
            if (selected > 0) position += " " + Loc.Get(K.StatusSelected, ("count", selected));
        }
        else
        {
            var (_, complete) = tab.Paged!.Index;
            position = Loc.Get(K.StatusLine, ("line", (complete ? "" : "≈") + (tab.Paged.TopLine + 1)));
        }
        string message = _message.Length > 0 ? _message : od.IsLarge ? Loc.Get(K.StatusLargeReadOnly, ("size", od.SizeBadge ?? "")) : Loc.Get(K.StatusReady);
        _statusLeft.Text = position + " · " + message;

        _indentItem.Content = Loc.Get(opts.InsertSpaces ? K.StatusIndentSpaces : K.StatusIndentTabs, ("size", opts.InsertSpaces ? opts.IndentSize : opts.TabSize));
        var codec = od.Doc.Codec;
        _encodingItem.Content = codec.Name.Replace(" with BOM", " BOM", StringComparison.Ordinal) + (od.EncodingUncertain ? "?" : "");
        _eolItem.Content = EolLabel(od);
        _languageItem.Content = od.Language.Id == LanguageModes.PlainId ? Loc.Get(K.LanguagePlain) : od.Language.Name;
        _configItem.Content = Loc.Get(K.StatusEditorConfig);
        _configItem.IsVisible = od.EditorConfig.Properties.Count > 0;

        ToolTip.SetTip(_encodingItem, od.EncodingUncertain ? Loc.Get(K.StatusEncodingGuess, ("percent", (int)Math.Round(od.Guess!.Confidence * 100))) : Loc.Get(K.StatusEncodingTip));
        ToolTip.SetTip(_indentItem, IndentSourceFile() is { } indentFile ? Loc.Get(K.StatusIndentFromConfig, ("file", indentFile)) : Loc.Get(K.StatusIndentTip));
        if (od.EditorConfig.Files.Count > 0) ToolTip.SetTip(_configItem, Loc.Get(K.StatusEditorConfigTip, ("file", od.EditorConfig.Files[0]), ("properties", string.Join(", ", od.EditorConfig.Properties.Keys))));
        ToolTip.SetTip(_eolItem, Loc.Get(K.StatusEolTip));
        ToolTip.SetTip(_languageItem, Loc.Get(K.StatusLanguageTip));
        AutomationProperties.SetName(_encodingItem, Loc.Get(K.StatusEncodingA11y, ("value", (string)_encodingItem.Content)));
        AutomationProperties.SetName(_eolItem, Loc.Get(K.StatusEolA11y, ("value", (string)_eolItem.Content)));
        AutomationProperties.SetName(_indentItem, Loc.Get(K.StatusIndentA11y, ("value", (string)_indentItem.Content)));
        AutomationProperties.SetName(_languageItem, Loc.Get(K.StatusLanguageA11y, ("value", (string)_languageItem.Content)));

        var items = new List<Button> { _indentItem, _encodingItem, _eolItem, _languageItem };
        if (_configItem.IsVisible) items.Add(_configItem);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) _statusRight.Children.Add(StatusDot());
            _statusRight.Children.Add(items[i]);
        }
    }

    private static Border StatusDot()
    {
        var dot = new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.Classes.Add("statusdot");
        return dot;
    }

    private string EolLabel(OpenDocument od) => od.Eol.Style switch
    {
        EolStyle.Mixed => Loc.Get(K.StatusEolMixed),
        EolStyle.None => od.NewLine(Options.ConfiguredEol) switch { "\r\n" => "CRLF", "\r" => "CR", _ => "LF" },
        _ => od.Eol.Label,
    };

    // ---- flyouts of the status bar ----

    internal MenuFlyout ShowIndentMenu()
    {
        var flyout = new MenuFlyout();
        var opts = Options;
        MenuItem Item(string header, bool on, Action run, bool radio = true)
        {
            var mi = new MenuItem { Header = header, ToggleType = radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox, IsChecked = on, GroupName = "indent" };
            mi.Click += (_, _) => run();
            return mi;
        }
        flyout.Items.Add(Item(Loc.Get(K.IndentUseSpaces), opts.InsertSpaces, () => _app.Workbench.Set("editor.insertSpaces", JsonValue.Create(true))));
        flyout.Items.Add(Item(Loc.Get(K.IndentUseTabs), !opts.InsertSpaces, () => _app.Workbench.Set("editor.insertSpaces", JsonValue.Create(false))));
        flyout.Items.Add(new Separator());
        foreach (int size in new[] { 2, 3, 4, 8 })
        {
            int s = size;
            flyout.Items.Add(Item(Loc.Get(K.IndentSize, ("size", size)), opts.IndentSize == size && opts.TabSize == size, () =>
            {
                _app.Workbench.Set("editor.tabSize", JsonValue.Create(s));
                _app.Workbench.Set("editor.indentSize", JsonValue.Create(s));
            }));
        }
        flyout.ShowAt(_indentItem);
        return flyout;
    }

    internal MenuFlyout? ShowEolMenu()
    {
        if (Active is not { } tab) return null;
        var flyout = new MenuFlyout();
        foreach (var (label, style) in new[] { (Loc.Get(K.EolToCrLf), EolStyle.CrLf), (Loc.Get(K.EolToLf), EolStyle.Lf), (Loc.Get(K.EolToCr), EolStyle.Cr) })
        {
            var target = style;
            var mi = new MenuItem { Header = label, IsEnabled = !tab.Doc.ReadOnly };
            mi.Click += (_, _) => _ = UnifyEolAsync(tab, target);
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(_eolItem);
        return flyout;
    }

    internal MenuFlyout? ShowLanguageMenu()
    {
        if (Active is not { } tab) return null;
        var flyout = new MenuFlyout();
        foreach (var mode in LanguageModes.All)
        {
            var m = mode;
            var mi = new MenuItem { Header = mode.Id == LanguageModes.PlainId ? Loc.Get(K.LanguagePlain) : mode.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = tab.Doc.Language == mode, GroupName = "language" };
            mi.Click += (_, _) => SetLanguage(tab, m);
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(_languageItem);
        return flyout;
    }

    public void SetLanguage(DocTab tab, LanguageMode mode)
    {
        tab.Doc.Language = mode;
        ApplyLanguageOptions();
        tab.Doc.NotifyChanged();
        UpdateStatus();
    }
}

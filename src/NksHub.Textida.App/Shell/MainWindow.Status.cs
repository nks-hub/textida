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
    private readonly Button _zoomItem = StatusItem();
    private readonly Button _configItem = StatusItem();
    private readonly Button _updateItem = StatusItem();
    private readonly StackPanel _statusRight = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    private static Button StatusItem()
    {
        var b = new Button { VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("statusitem");
        return b;
    }

    /// <summary>Status bar texts, for tests and accessibility checks.</summary>
    internal string StatusLeftText => _statusLeft.Text ?? "";
    internal string StatusRightText => string.Join(" · ", new[] { _indentItem, _encodingItem, _eolItem, _languageItem, _zoomItem, _configItem }.Where(b => b.IsVisible).Select(b => (string)b.Content!));
    internal Button IndentItem => _indentItem;
    internal Button EncodingItem => _encodingItem;
    internal Button EolItem => _eolItem;
    internal Button LanguageItem => _languageItem;
    internal Button ZoomItem => _zoomItem;

    private Control BuildStatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_statusRight, 3);
        Grid.SetColumn(_pluginStatus, 2);
        Grid.SetColumn(_ideItem, 1);
        grid.Children.Add(_statusLeft);
        grid.Children.Add(_announcer);
        grid.Children.Add(_ideItem);
        grid.Children.Add(_pluginStatus);
        grid.Children.Add(_statusRight);
        Grid.SetColumn(_updateItem, 4);
        grid.Children.Add(_updateItem);
        _updateItem.IsVisible = false;
        _updateItem.Click += (_, _) => InstallOffered();
        StartIdeItem();
        _indentItem.Click += (_, _) => ShowIndentMenu();
        _encodingItem.Click += (_, _) => ShowEncodingMenu(null);
        _eolItem.Click += (_, _) => ShowEolMenu();
        _languageItem.Click += (_, _) => ShowLanguagePicker();
        _zoomItem.Click += (_, _) => ShowZoomMenu();
        _configItem.Click += (_, _) => ShowConfigFiles();
        _tokensItem.Click += (_, _) => ShowTokenMenu();
        _tokensItem.IsVisible = false;
        _configItem.IsVisible = false;
        return grid;
    }

    private bool _inputEvent, _statusStale;

    /// <summary>A key or typed text changes the caret, the selection and the text one after the other; the status bar is written once, when the event has been through all handlers.</summary>
    private void BracketInputEvent<T>(Avalonia.Interactivity.RoutedEvent<T> input) where T : Avalonia.Interactivity.RoutedEventArgs
    {
        AddHandler(input, (_, _) => BeginInputEvent(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(input, (_, _) => EndInputEvent(), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void BeginInputEvent()
    {
        EndInputEvent();
        _inputEvent = true;
    }

    private void EndInputEvent()
    {
        _inputEvent = false;
        if (!_statusStale) return;
        _statusStale = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_inputEvent)
        {
            _statusStale = true;
            return;
        }
        long t = Core.TypingTrace.Begin();
        UpdateStatusCore();
        Core.TypingTrace.End("win.updateStatus", t);
    }

    private void UpdateStatusCore()
    {
        RefreshToolbarState();
        SyncHeadings();
        long part = Core.TypingTrace.Begin();
        CheckJournalHealth();
        Core.TypingTrace.End("win.status.journalHealth", part);
        part = Core.TypingTrace.Begin();
        _app.Plugins.TrackKeyPath(this, Active);
        _app.Plugins.TrackSymbol(Active);
        _app.Plugins.TrackSchema(this, Active);
        _app.Plugins.TrackActivation(this, Active);
        Core.TypingTrace.End("win.status.pluginTrackers", part);
        part = Core.TypingTrace.Begin();
        if (Active is not { Custom: null, Hex: null }) _statusRight.Children.Clear(); // a text tab keeps its items and swaps them only when they differ
        if (Active is not { } tab)
        {
            _statusLeft.Text = Loc.Get(K.StatusReady);
            return;
        }
        if (tab.Custom is ImageView or MarkdownPreview)
        {
            ShowViewerStatus(tab);
            return;
        }
        if (tab.Custom is not null)
        {
            ShowResultsStatus();
            return;
        }

        if (tab.Hex is not null)
        {
            ShowHexStatus(tab);
            return;
        }

        Core.TypingTrace.End("win.status.head", part);
        part = Core.TypingTrace.Begin();
        var od = tab.Doc;
        var opts = Options;
        string position;
        if (tab.Surface is { } surface)
        {
            var (line, column) = surface.CaretLineColumn();
            position = Loc.Get(K.StatusPosition, ("line", line), ("column", column));
            int selected = surface.SelectedLength;
            if (surface.IsMulti) position += " " + Loc.Plural(K.StatusCarets, surface.CaretCount);
            if (selected > 0) position += " " + Loc.Get(K.StatusSelected, ("count", selected));
            if (surface.Occurrences.Result.Describe() is { } others) position += " " + others;
            if (GitBlameStatusText(tab, (int)line - 1) is { } blame) position += " · " + blame;
        }
        else
        {
            var (line, column) = tab.CaretPosition();
            position = tab.Paged!.LinesExact && tab.Paged.CaretLineColumn() is not null
                ? Loc.Get(K.StatusPosition, ("line", line), ("column", column))
                : Loc.Get(K.StatusLine, ("line", "≈" + line));
            if (tab.Paged.IsMulti) position += " " + Loc.Plural(K.StatusCarets, tab.Paged.CaretCount);
            if (tab.Paged.Occurrences.Result.Describe() is { } others) position += " " + others;
        }
        if (tab.TableShown) position = tab.Table!.StatusText();
        if (tab.JsonShown) position = tab.Json!.StatusText();
        string message = _message.Length > 0 ? _message : tab.Syntax is { LineTooLong: true } ? Loc.Get(K.StatusSyntaxLongLine) : tab.Syntax is { Pending: true } ? Loc.Get(K.StatusSyntaxPending) : od.IsLarge ? Loc.Get(K.StatusLargePaged, ("size", od.SizeBadge ?? ""), ("revision", od.Doc.Revision)) : Loc.Get(K.StatusReady);
        _statusLeft.Text = position + " · " + message;
        Core.TypingTrace.End("win.status.position", part);
        part = Core.TypingTrace.Begin();

        // the accessible name of an item is formatted again only when its text or the language changes: the status update runs on every keystroke
        bool relabel = !ReferenceEquals(_statusLanguage, Loc.Language);
        _statusLanguage = Loc.Language;
        SetStatusItem(_indentItem, Loc.Get(opts.InsertSpaces ? K.StatusIndentSpaces : K.StatusIndentTabs, ("size", opts.InsertSpaces ? opts.IndentSize : opts.TabSize)), K.StatusIndentA11y, relabel);
        var codec = od.Doc.Codec;
        SetStatusItem(_encodingItem, codec.Name.Replace(" with BOM", " BOM", StringComparison.Ordinal) + (od.EncodingUncertain ? "?" : ""), K.StatusEncodingA11y, relabel);
        SetStatusItem(_eolItem, EolLabel(od, opts), K.StatusEolA11y, relabel);
        SetStatusItem(_languageItem, od.Language.Id == LanguageModes.PlainId ? Loc.Get(K.LanguagePlain) : od.Language.Name, K.StatusLanguageA11y, relabel);
        SetStatusItem(_zoomItem, Loc.Get(K.StatusZoom, ("percent", ZoomPercent)), K.StatusZoomA11y, relabel);
        var configs = ConfigHitsOf(od).Where(h => h.Path is not null).ToList();
        _configItem.Content = ConfigLabel(configs);
        _configItem.IsVisible = configs.Count > 0;
        UpdateTokensItem(tab);
        Core.TypingTrace.End("win.status.items", part);
        part = Core.TypingTrace.Begin();

        ToolTip.SetTip(_encodingItem, od.EncodingUncertain ? Loc.Get(K.StatusEncodingGuess, ("percent", (int)Math.Round(od.Guess!.Confidence * 100))) : Loc.Get(K.StatusEncodingTip));
        ToolTip.SetTip(_indentItem, IndentSourceFile() is { } indentFile ? Loc.Get(K.StatusIndentFromConfig, ("file", indentFile)) : Loc.Get(K.StatusIndentTip));
        if (configs.Count > 0) ToolTip.SetTip(_configItem, Loc.Get(K.StatusConfigFilesTip) + "\n" + string.Join("\n", configs.Select(h => h.Path)));
        ToolTip.SetTip(_eolItem, Loc.Get(K.StatusEolTip));
        ToolTip.SetTip(_languageItem, Loc.Get(K.StatusLanguageTip));
        ToolTip.SetTip(_zoomItem, Loc.Get(K.StatusZoomTip));

        Core.TypingTrace.End("win.status.tips", part);
        var items = new List<Button> { _indentItem, _encodingItem, _eolItem, _languageItem, _zoomItem };
        if (_configItem.IsVisible) items.Add(_configItem);
        if (_tokensItem.IsVisible) items.Add(_tokensItem);
        var shown = new List<Control>(items.Count * 2);
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                while (_statusDots.Count < i) _statusDots.Add(StatusDot());
                shown.Add(_statusDots[i - 1]);
            }
            shown.Add(items[i]);
        }
        if (_statusRight.Children.SequenceEqual(shown)) return;
        _statusRight.Children.Clear();
        _statusRight.Children.AddRange(shown);
    }

    private readonly List<Border> _statusDots = [];
    private string? _statusLanguage;

    private static void SetStatusItem(Button item, string text, string a11yKey, bool relabel)
    {
        if (!relabel && item.Content is string old && old == text) return;
        item.Content = text;
        AutomationProperties.SetName(item, Loc.Get(a11yKey, ("value", text)));
    }

    private static Border StatusDot()
    {
        var dot = new Border { Width = 2, Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(2, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.Classes.Add("statusdot");
        return dot;
    }

    private static string EolLabel(OpenDocument od, EditorOptions opts) => od.Eol.Style switch
    {
        EolStyle.Mixed => Loc.Get(K.StatusEolMixed),
        EolStyle.None => od.NewLine(opts.ConfiguredEol) switch { "\r\n" => "CRLF", "\r" => "CR", _ => "LF" },
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
            var mi = new MenuItem { Header = label, IsEnabled = !tab.Doc.IsLarge };
            mi.Click += (_, _) => _ = UnifyEolAsync(tab, target);
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(_eolItem);
        return flyout;
    }

    /// <summary>
    /// The choice of the file language in the palette: Automatic by file extension (it drops a choice made before), the languages picked last, the one the extension gives,
    /// and then every language by name. Each row shows the extensions of its language, and typing narrows the list by name or extension.
    /// </summary>
    internal bool ShowLanguagePicker()
    {
        if (Active is not { } tab) return false;
        var od = tab.Doc;
        var detected = LanguageModes.ForPath(od.Path);
        string current = Loc.Get(K.LanguagePickCurrent);
        static string NameOf(LanguageMode mode) => mode.Id == LanguageModes.PlainId ? Loc.Get(K.LanguagePlain) : mode.Name;
        QuickRow Row(LanguageMode mode, string? category)
        {
            string name = NameOf(mode), extensions = string.Join(" ", mode.Extensions.Concat(mode.FileNames));
            string? reason = od.LanguageChosen && od.Language == mode ? current : null;
            string spoken = string.Join(", ", new[] { category, name, extensions.Length == 0 ? null : Loc.Get(K.LanguagePickExtensions, ("list", extensions)), reason }.Where(t => !string.IsNullOrEmpty(t)));
            return new QuickRow(name, category, extensions.Length == 0 ? null : extensions, null, true, reason, spoken, () =>
            {
                _app.RecentLanguages.Used(mode.Id);
                SetLanguage(tab, mode);
            });
        }
        string auto = Loc.Get(K.LanguagePickAuto), detectedName = NameOf(detected);
        var rows = new List<QuickRow>
        {
            new(auto, null, detectedName, null, true, od.LanguageChosen ? null : current, string.Join(", ", new[] { auto, detectedName, od.LanguageChosen ? null : current }.Where(t => t is not null)), () => ResetLanguage(tab)),
        };
        var listed = new HashSet<LanguageMode>();
        foreach (var mode in _app.RecentLanguages.Names.Select(id => LanguageModes.All.FirstOrDefault(m => m.Id == id)).OfType<LanguageMode>())
            if (listed.Add(mode)) rows.Add(Row(mode, Loc.Get(K.LanguagePickRecent)));
        if (listed.Add(detected)) rows.Add(Row(detected, Loc.Get(K.LanguagePickDetected)));
        var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Language);
        rows.AddRange(LanguageModes.All.Where(m => !listed.Contains(m)).OrderBy(NameOf, StringComparer.Create(culture, ignoreCase: true)).Select(m => Row(m, null)));
        ShowChoice(Loc.Get(K.LanguagePickWatermark), Loc.Get(K.LanguagePickA11y), rows);
        return true;
    }

    /// <summary>Reset Zoom and the zoom levels of <see cref="ZoomPresets"/>; a level reads as the zoom its whole font size gives, and levels that give the same size are one row.</summary>
    internal MenuFlyout ShowZoomMenu()
    {
        var flyout = new MenuFlyout();
        var reset = new MenuItem { Header = Loc.Get(K.ZoomResetItem), InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D0, Shortcuts.Primary) };
        reset.Click += (_, _) => ZoomTo(100);
        flyout.Items.Add(reset);
        flyout.Items.Add(new Separator());
        int current = ZoomPercent;
        var seen = new HashSet<int>();
        foreach (int preset in ZoomPresets)
        {
            int size = FontSizeFor(preset), percent = (int)Math.Round(size / DefaultFontSize() * 100);
            if (!seen.Add(size)) continue;
            int target = preset;
            var mi = new MenuItem { Header = Loc.Get(K.StatusZoom, ("percent", percent)), ToggleType = MenuItemToggleType.Radio, IsChecked = percent == current, GroupName = "zoom" };
            mi.Click += (_, _) => ZoomTo(target);
            flyout.Items.Add(mi);
        }
        flyout.ShowAt(_zoomItem);
        return flyout;
    }

    public void SetLanguage(DocTab tab, LanguageMode mode)
    {
        tab.Doc.ChooseLanguage(mode);
        LanguageApplied(tab);
    }

    /// <summary>Automatic by file extension: the language the file name gives, now and after a save under another name.</summary>
    public void ResetLanguage(DocTab tab)
    {
        tab.Doc.ResetLanguage();
        LanguageApplied(tab);
    }

    private void LanguageApplied(DocTab tab)
    {
        ApplyLanguageOptions();
        tab.Doc.NotifyChanged();
        UpdateStatus();
    }
}

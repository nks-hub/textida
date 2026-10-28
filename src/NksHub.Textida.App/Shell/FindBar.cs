using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The find and replace strip above the status bar (D5): non-modal, one row of the query, the match counter, the switches (case, regular expression,
/// whole word, in selection) and the replacement; a second row names the folder when the search runs over files. Holds no search logic; the window
/// reads the fields and answers the events.
/// </summary>
internal sealed class FindBar : Border
{
    private static readonly Geometry SearchGlyph = Geometry.Parse("M6.5,2.5 A4,4 0 1 0 6.5,10.5 A4,4 0 1 0 6.5,2.5 M9.5,9.5 L13,13");
    private static readonly Geometry CloseGlyph = Geometry.Parse("M0,0 L8,8 M8,0 L0,8");

    private readonly WrapPanel _row = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4) };
    private readonly Grid _folderRow = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 0, 12, 4), IsVisible = false };
    private readonly TextBlock _folderLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    public FindBar()
    {
        Classes.Add("findbar");
        IsVisible = false;
        FindBox = Input(220);
        ReplaceBox = Input(200);
        FolderBox = Input(0);
        FolderBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        CaseToggle = Toggle();
        RegexToggle = Toggle();
        WordToggle = Toggle();
        SelectionToggle = Toggle();
        ReplaceOneButton = TextButton();
        ReplaceAllButton = TextButton();
        GoButton = TextButton();
        ListButton = TextButton();
        StopButton = TextButton();
        TryButton = TextButton();
        BrowseButton = TextButton();
        CloseButton = new Button { Content = new Avalonia.Controls.Shapes.Path { Data = CloseGlyph, StrokeThickness = 1.2, Width = 8, Height = 8, Stretch = Stretch.None }, VerticalAlignment = VerticalAlignment.Center };
        CloseButton.Classes.Add("tabclose");
        CountText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), MinWidth = 52 };
        CountText.Classes.Add("findcount");

        FindBox.InnerLeftContent = new Avalonia.Controls.Shapes.Path { Data = SearchGlyph, Stroke = Brushes.Gray, StrokeThickness = 1.3, Width = 14, Height = 14, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0) };
        foreach (var c in new Control[] { FindBox, CountText, StopButton, CaseToggle, RegexToggle, WordToggle, SelectionToggle, TryButton, GoButton, ListButton, ReplaceBox, ReplaceOneButton, ReplaceAllButton, CloseButton })
        {
            c.Margin = c is TextBlock ? c.Margin : new Thickness(0, 2, 4, 2);
            _row.Children.Add(c);
        }
        CloseButton.Margin = new Thickness(8, 2, 0, 2);

        Grid.SetColumn(FolderBox, 1);
        Grid.SetColumn(BrowseButton, 2);
        BrowseButton.Margin = new Thickness(4, 0, 0, 0);
        _folderRow.Children.Add(_folderLabel);
        _folderRow.Children.Add(FolderBox);
        _folderRow.Children.Add(BrowseButton);
        Child = new StackPanel { Children = { _row, _folderRow } };

        CloseButton.Click += (_, _) => CloseRequested?.Invoke();
        GoButton.Click += (_, _) => GoRequested?.Invoke();
        ListButton.Click += (_, _) => ListRequested?.Invoke();
        StopButton.Click += (_, _) => StopRequested?.Invoke();
        ReplaceOneButton.Click += (_, _) => ReplaceOneRequested?.Invoke();
        ReplaceAllButton.Click += (_, _) => ReplaceAllRequested?.Invoke();
        TryButton.Click += (_, _) => TryRequested?.Invoke();
        BrowseButton.Click += (_, _) => BrowseRequested?.Invoke();
        FindBox.TextChanged += (_, _) => QueryChanged?.Invoke();
        foreach (var t in new[] { CaseToggle, RegexToggle, WordToggle, SelectionToggle })
            t.IsCheckedChanged += (_, _) => { UpdateVisibility(); QueryChanged?.Invoke(); };
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ApplyTexts();
        UpdateVisibility();
    }

    public TextBox FindBox { get; }
    public TextBox ReplaceBox { get; }
    public TextBox FolderBox { get; }
    public ToggleButton CaseToggle { get; }
    public ToggleButton RegexToggle { get; }
    public ToggleButton WordToggle { get; }
    public ToggleButton SelectionToggle { get; }
    public Button ReplaceOneButton { get; }
    public Button ReplaceAllButton { get; }
    public Button GoButton { get; }
    public Button ListButton { get; }
    /// <summary>Cancels the running search of the document; shown only while one runs.</summary>
    public Button StopButton { get; }
    public Button TryButton { get; }
    public Button BrowseButton { get; }
    public Button CloseButton { get; }
    public TextBlock CountText { get; }

    /// <summary>True when the replacement field is shown.</summary>
    public bool ReplaceShown { get; private set; }
    /// <summary>True when the search runs over a folder and the open documents instead of the current document.</summary>
    public bool FilesMode { get; private set; }

    public event Action? CloseRequested;
    public event Action? QueryChanged;
    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? ReplaceOneRequested;
    public event Action? ReplaceAllRequested;
    public event Action? GoRequested;
    /// <summary>The matches of the current document are to be listed in the Search Results tab.</summary>
    public event Action? ListRequested;
    public event Action? StopRequested;
    public event Action? TryRequested;
    public event Action? BrowseRequested;

    /// <summary>Shows the bar for the current document or for files; <paramref name="replace"/> adds the replacement field.</summary>
    public void Open(bool files, bool replace)
    {
        FilesMode = files;
        ReplaceShown = replace;
        IsVisible = true;
        UpdateVisibility();
        ApplyTexts();
    }

    public void Close() => IsVisible = false;

    private bool _running;

    /// <summary>Tells the bar whether a search of the document runs, which shows its Cancel button.</summary>
    public void SetRunning(bool running)
    {
        _running = running;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        ReplaceBox.IsVisible = ReplaceShown;
        ReplaceOneButton.IsVisible = ReplaceShown && !FilesMode;
        ReplaceAllButton.IsVisible = ReplaceShown && !FilesMode;
        SelectionToggle.IsVisible = !FilesMode;
        GoButton.IsVisible = FilesMode;
        ListButton.IsVisible = !FilesMode;
        StopButton.IsVisible = _running && !FilesMode;
        TryButton.IsVisible = RegexToggle.IsChecked == true;
        _folderRow.IsVisible = FilesMode;
        CountText.IsVisible = !FilesMode;
    }

    public void ApplyTexts()
    {
        FindBox.PlaceholderText = Loc.Get(K.FindWatermark);
        ReplaceBox.PlaceholderText = Loc.Get(K.FindReplaceWatermark);
        FolderBox.PlaceholderText = Loc.Get(K.FindFolderWatermark);
        CaseToggle.Content = Loc.Get(K.FindCase);
        RegexToggle.Content = Loc.Get(K.FindRegex);
        WordToggle.Content = Loc.Get(K.FindWord);
        SelectionToggle.Content = Loc.Get(K.FindSelection);
        ReplaceOneButton.Content = Loc.Get(K.FindReplaceOne);
        ReplaceAllButton.Content = Loc.Get(K.FindReplaceAll);
        GoButton.Content = Loc.Get(ReplaceShown ? K.FindPreview : K.FindAll);
        ListButton.Content = Loc.Get(K.FindList);
        StopButton.Content = Loc.Get(K.ResultsCancel);
        TryButton.Content = Loc.Get(K.FindTry);
        BrowseButton.Content = Loc.Get(K.FindBrowse);
        _folderLabel.Text = Loc.Get(K.FindFolder);
        ToolTip.SetTip(CaseToggle, Loc.Get(K.FindCaseTip));
        ToolTip.SetTip(RegexToggle, Loc.Get(K.FindRegexTip));
        ToolTip.SetTip(WordToggle, Loc.Get(K.FindWordTip));
        ToolTip.SetTip(SelectionToggle, Loc.Get(K.FindSelectionTip));
        ToolTip.SetTip(TryButton, Loc.Get(K.FindTryTip));
        AutomationProperties.SetName(this, Loc.Get(K.A11yFindBar));
        AutomationProperties.SetName(FindBox, Loc.Get(K.A11yFindInput));
        AutomationProperties.SetName(ReplaceBox, Loc.Get(K.A11yReplaceInput));
        AutomationProperties.SetName(FolderBox, Loc.Get(K.A11yFindFolder));
        AutomationProperties.SetName(CaseToggle, Loc.Get(K.FindCaseTip));
        AutomationProperties.SetName(RegexToggle, Loc.Get(K.FindRegexTip));
        AutomationProperties.SetName(WordToggle, Loc.Get(K.FindWordTip));
        AutomationProperties.SetName(SelectionToggle, Loc.Get(K.FindSelectionTip));
        AutomationProperties.SetName(CloseButton, Loc.Get(K.A11yFindClose));
        // a text block is named by its text; the counter is read out whenever it changes, and the label tells what it counts
        AutomationProperties.SetHelpText(CountText, Loc.Get(K.A11yFindCount));
        AutomationProperties.SetLiveSetting(CountText, AutomationLiveSetting.Polite);
    }

    /// <summary>Shows the match counter, or the reason the pattern cannot be used (<paramref name="error"/>).</summary>
    public void ShowCount(string text, bool error = false)
    {
        CountText.Text = text;
        if (error) CountText.Classes.Add("error");
        else CountText.Classes.Remove("error");
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        bool alt = e.KeyModifiers == KeyModifiers.Alt, none = e.KeyModifiers == KeyModifiers.None, shift = e.KeyModifiers == KeyModifiers.Shift;
        switch (e.Key)
        {
            case Key.Escape when none:
                CloseRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter when e.Source == FindBox && (none || shift):
                if (FilesMode) GoRequested?.Invoke();
                else if (shift) PreviousRequested?.Invoke();
                else NextRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter when e.Source == FolderBox && none:
                GoRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.Enter when e.Source == ReplaceBox && (none || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt)):
                if (FilesMode) GoRequested?.Invoke();
                else if (none) ReplaceOneRequested?.Invoke();
                else ReplaceAllRequested?.Invoke();
                e.Handled = true;
                break;
            case Key.C when alt: CaseToggle.IsChecked = CaseToggle.IsChecked != true; e.Handled = true; break;
            case Key.R when alt: RegexToggle.IsChecked = RegexToggle.IsChecked != true; e.Handled = true; break;
            case Key.W when alt: WordToggle.IsChecked = WordToggle.IsChecked != true; e.Handled = true; break;
        }
    }

    private static TextBox Input(double width)
    {
        var box = new TextBox { VerticalAlignment = VerticalAlignment.Center };
        if (width > 0) box.Width = width;
        box.Classes.Add("findinput");
        return box;
    }

    private static ToggleButton Toggle()
    {
        var t = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        t.Classes.Add("findtoggle");
        return t;
    }

    private static Button TextButton()
    {
        var b = new Button { VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add("findbutton");
        return b;
    }
}

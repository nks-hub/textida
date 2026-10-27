using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// "Try" for a LOG pattern: the pattern is run over a sample of tool output and every line shows the file, line and column it found, or that it found
/// nothing. The pattern that works is handed to the output window with "Use for the output".
/// </summary>
internal sealed class LogPatternWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextBox _pattern = new() { FontFamily = Mono };
    private readonly ComboBox _syntax = new() { MinWidth = 190 };
    private readonly TextBox _sample = new() { FontFamily = Mono, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly ListBox _results = new() { Background = Brushes.Transparent };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _patternLabel = new(), _sampleLabel = new(), _resultsLabel = new();
    private readonly Button _use = new() { IsDefault = true, MinWidth = 150 };
    private readonly Button _close = new() { IsCancel = true, MinWidth = 84 };
    private LogPattern? _compiled;

    public LogPatternWindow(string pattern, LogPatternSyntax syntax, string sample)
    {
        Classes.Add("shell");
        Width = 920;
        Height = 640;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _pattern.Text = pattern;
        _sample.Text = sample;
        foreach (var label in new[] { _patternLabel, _sampleLabel, _resultsLabel }) label.FontWeight = FontWeight.SemiBold;

        var patternRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { _pattern, _syntax } };
        Grid.SetColumn(_syntax, 1);
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 4 };
        void Place(Control c, int row) { Grid.SetRow(c, row); left.Children.Add(c); }
        Place(_patternLabel, 0);
        Place(patternRow, 1);
        Place(_sampleLabel, 2);
        Place(_sample, 3);
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 4 };
        void PlaceRight(Control c, int row) { Grid.SetRow(c, row); right.Children.Add(c); }
        PlaceRight(_resultsLabel, 0);
        PlaceRight(new Border { Child = _results, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 1);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16, Margin = new Thickness(18, 14) };
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { _use, _close } };
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 6, Children = { _note, _help, buttons } };
        var root = new DockPanel { Children = { footer, body } };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        _pattern.TextChanged += (_, _) => Refresh();
        _sample.TextChanged += (_, _) => Refresh();
        _syntax.SelectionChanged += (_, _) => Refresh();
        _use.Click += (_, _) => UseNow();
        _close.Click += (_, _) => Close();
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        Opened += (_, _) => { _pattern.Focus(); _pattern.SelectAll(); };
        ApplyTexts();
        Syntax = syntax;
        Refresh();
    }

    /// <summary>Raised with the pattern the user chose to use; the window stays open.</summary>
    public event Action<LogPattern>? Used;

    internal string PatternText { get => _pattern.Text ?? ""; set => _pattern.Text = value; }
    internal string SampleText { get => _sample.Text ?? ""; set => _sample.Text = value; }
    internal LogPatternSyntax Syntax { get => _syntax.SelectedIndex == 0 ? LogPatternSyntax.Mask : LogPatternSyntax.Regex; set => _syntax.SelectedIndex = value == LogPatternSyntax.Mask ? 0 : 1; }
    internal string NoteText => _note.Text ?? "";
    internal bool CanUse => _compiled is not null;
    internal IReadOnlyList<string> ResultLines => [.. _results.Items.OfType<TextBlock>().Select(t => t.Text ?? "")];

    /// <summary>What the Use button does: hands the compiled pattern over when the pattern is valid.</summary>
    internal void UseNow()
    {
        if (_compiled is { } pattern) Used?.Invoke(pattern);
    }

    private void OnLanguageChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
        Refresh();
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.LogPatternTitle);
        _patternLabel.Text = Loc.Get(K.LogPatternPattern);
        _sampleLabel.Text = Loc.Get(K.LogPatternSample);
        _resultsLabel.Text = Loc.Get(K.LogPatternResults);
        _help.Text = Loc.Get(K.LogPatternHelp);
        _use.Content = Loc.Get(K.LogPatternUse);
        _close.Content = Loc.Get(K.PanelOutputClose);
        int selected = _syntax.SelectedIndex;
        _syntax.ItemsSource = new[] { Loc.Get(K.LogPatternMask), Loc.Get(K.LogPatternRegex) };
        _syntax.SelectedIndex = Math.Max(0, selected);
        AutomationProperties.SetName(_pattern, Loc.Get(K.LogPatternPattern));
        AutomationProperties.SetName(_syntax, Loc.Get(K.LogPatternSyntax));
        AutomationProperties.SetName(_sample, Loc.Get(K.LogPatternSample));
        AutomationProperties.SetName(_results, Loc.Get(K.LogPatternResults));
    }

    private void Refresh()
    {
        _results.Items.Clear();
        _note.Text = "";
        _note.Foreground = null;
        _compiled = null;
        _use.IsEnabled = false;
        if (_syntax.SelectedIndex < 0 || (_pattern.Text ?? "").Length == 0) return;
        try { _compiled = LogPattern.Compile(_pattern.Text!, Syntax); }
        catch (LogPatternException e)
        {
            _note.Text = Loc.Get(K.LogPatternInvalid, ("reason", e.Message));
            _note.Foreground = new SolidColorBrush(Color.Parse("#C0392B"));
            return;
        }
        _use.IsEnabled = true;
        int found = 0;
        foreach (var row in _compiled.Preview(_sample.Text ?? ""))
        {
            if (row.Text.Length == 0 && row.Location is null) continue;
            if (row.Location is { } at) found++;
            _results.Items.Add(new TextBlock { Text = Describe(row), FontFamily = Mono, FontSize = 12, Padding = new Thickness(6, 1), TextWrapping = TextWrapping.Wrap, Opacity = row.Location is null ? 0.6 : 1 });
        }
        _note.Text = Loc.Plural(K.LogPatternFound, found);
    }

    /// <summary>The sentence for one line of the sample: where the pattern points, or that it found nothing.</summary>
    internal static string Describe(LogPreviewLine row)
    {
        string number = row.Number.ToString(CultureInfo.CurrentCulture);
        if (row.Location is not { } at) return Loc.Get(K.LogPatternNoPlace, ("number", number), ("text", Short(row.Text)));
        string file = at.File ?? Loc.Get(K.LogPatternCurrentFile);
        return at.Column is { } column
            ? Loc.Get(K.LogPatternPlaceColumn, ("number", number), ("file", file), ("line", at.Line), ("column", column))
            : Loc.Get(K.LogPatternPlace, ("number", number), ("file", file), ("line", at.Line));
    }

    private static string Short(string text) => text.Length <= 70 ? text : text[..70] + "…";
}

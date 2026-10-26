using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// "Try" for a regular expression: the pattern is run over a sample text with the engine of the search (<see cref="RegexTools.Test"/>), the matches and
/// their groups are listed, the replacement is shown, and "Explain" describes every part of the pattern, naming the syntax of other dialects as not supported.
/// </summary>
internal sealed class RegexTesterWindow : Window
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextBox _pattern = new() { FontFamily = Mono };
    private readonly TextBox _replacement = new() { FontFamily = Mono };
    private readonly TextBox _sample = new() { FontFamily = Mono, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly TextBox _replaced = new() { FontFamily = Mono, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 60 };
    private readonly ToggleButton _case = new();
    private readonly ListBox _matches = new() { Background = Brushes.Transparent };
    private readonly ListBox _explain = new() { Background = Brushes.Transparent };
    private readonly TextBlock _note = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _dialect = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _limits = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _patternLabel = new(), _replacementLabel = new(), _sampleLabel = new(), _matchesLabel = new(), _explainLabel = new(), _replacedLabel = new();

    public RegexTesterWindow(SearchPattern start)
    {
        Classes.Add("shell");
        Width = 980;
        Height = 700;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _pattern.Text = start.IsRegex ? start.Text : Regex.Escape(start.Text);
        _case.IsChecked = start.CaseSensitive;
        _case.Classes.Add("findtoggle");
        foreach (var label in new[] { _patternLabel, _replacementLabel, _sampleLabel, _matchesLabel, _explainLabel, _replacedLabel }) label.FontWeight = FontWeight.SemiBold;

        var patternRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, Children = { _pattern, _case } };
        Grid.SetColumn(_case, 1);
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*"), RowSpacing = 4 };
        void Place(Control c, int row) { Grid.SetRow(c, row); left.Children.Add(c); }
        Place(_patternLabel, 0);
        Place(patternRow, 1);
        Place(_replacementLabel, 2);
        Place(_replacement, 3);
        Place(_sampleLabel, 4);
        Place(_sample, 5);
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,*"), RowSpacing = 4 };
        void PlaceRight(Control c, int row) { Grid.SetRow(c, row); right.Children.Add(c); }
        PlaceRight(_matchesLabel, 0);
        PlaceRight(new Border { Child = _matches, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 1);
        PlaceRight(_replacedLabel, 2);
        PlaceRight(_replaced, 3);
        PlaceRight(_explainLabel, 4);
        PlaceRight(new Border { Child = _explain, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4) }, 5);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16, Margin = new Thickness(18, 14) };
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);
        var footer = new StackPanel { Margin = new Thickness(18, 0, 18, 12), Spacing = 4, Children = { _note, _dialect, _limits } };
        var root = new DockPanel { Children = { footer, body } };
        DockPanel.SetDock(footer, Dock.Bottom);
        Content = root;

        _pattern.TextChanged += (_, _) => Refresh();
        _replacement.TextChanged += (_, _) => Refresh();
        _sample.TextChanged += (_, _) => Refresh();
        _case.IsCheckedChanged += (_, _) => Refresh();
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        Opened += (_, _) => { _pattern.Focus(); _pattern.SelectAll(); };
        ApplyTexts();
        Refresh();
    }

    internal string PatternText { get => _pattern.Text ?? ""; set => _pattern.Text = value; }
    internal string SampleText { get => _sample.Text ?? ""; set => _sample.Text = value; }
    internal string ReplacementText { get => _replacement.Text ?? ""; set => _replacement.Text = value; }
    internal string ReplacedText => _replaced.Text ?? "";
    internal string NoteText => _note.Text ?? "";
    internal IReadOnlyList<string> MatchLines => [.. _matches.Items.OfType<TextBlock>().Select(t => t.Text ?? "")];
    internal IReadOnlyList<string> ExplainLines => [.. _explain.Items.OfType<TextBlock>().Select(t => t.Text ?? "")];

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
        Title = Loc.Get(K.RegexTitle);
        _patternLabel.Text = Loc.Get(K.RegexPattern);
        _replacementLabel.Text = Loc.Get(K.RegexReplacement);
        _sampleLabel.Text = Loc.Get(K.RegexSample);
        _matchesLabel.Text = Loc.Get(K.RegexMatches);
        _explainLabel.Text = Loc.Get(K.RegexExplain);
        _replacedLabel.Text = Loc.Get(K.RegexReplaced);
        _dialect.Text = Loc.Get(K.RegexDialect);
        _limits.Text = Loc.Get(K.RegexLimits);
        _case.Content = Loc.Get(K.FindCase);
        ToolTip.SetTip(_case, Loc.Get(K.FindCaseTip));
        AutomationProperties.SetName(_pattern, Loc.Get(K.RegexPattern));
        AutomationProperties.SetName(_replacement, Loc.Get(K.RegexReplacement));
        AutomationProperties.SetName(_sample, Loc.Get(K.RegexSample));
        AutomationProperties.SetName(_matches, Loc.Get(K.RegexMatches));
        AutomationProperties.SetName(_explain, Loc.Get(K.RegexExplain));
        AutomationProperties.SetName(_replaced, Loc.Get(K.RegexReplaced));
        AutomationProperties.SetName(_case, Loc.Get(K.FindCaseTip));
    }

    private void Refresh()
    {
        var pattern = new SearchPattern(_pattern.Text ?? "", IsRegex: true, CaseSensitive: _case.IsChecked == true);
        string sample = _sample.Text ?? "";
        string? replacement = string.IsNullOrEmpty(_replacement.Text) ? null : _replacement.Text;
        _matches.Items.Clear();
        _explain.Items.Clear();
        _replaced.Text = "";
        _note.Text = "";
        _note.Foreground = null;
        if (pattern.Text.Length == 0)
        {
            _matches.Items.Add(Line(Loc.Get(K.RegexNoMatch)));
            return;
        }

        var result = RegexTools.Test(pattern, sample, replacement);
        if (result.Error is { } error)
        {
            _note.Text = Loc.Get(K.FindInvalid, ("reason", error));
            _note.Foreground = new SolidColorBrush(Color.Parse("#C0392B"));
        }
        else if (result.TimedOut) _note.Text = Loc.Get(K.RegexTimedOut);
        else if (result.Truncated) _note.Text = Loc.Get(K.RegexTruncated, ("max", RegexTools.MaxTestMatches));

        if (result.Error is null && result.Matches.Count == 0) _matches.Items.Add(Line(Loc.Get(K.RegexNoMatch)));
        int n = 0;
        foreach (var m in result.Matches)
        {
            _matches.Items.Add(Line(Loc.Get(K.RegexMatchLine, ("number", ++n), ("value", Visible(m.Value)), ("index", m.Index.ToString(CultureInfo.CurrentCulture)))));
            foreach (var g in m.Groups.Where(g => g.Name != "0"))
                _matches.Items.Add(Line("    " + Loc.Get(K.RegexGroupLine, ("name", g.Name), ("value", Visible(g.Value)))));
        }
        _replaced.Text = result.Replaced ?? "";

        foreach (var part in RegexTools.Explain(pattern.Text))
            _explain.Items.Add(Line(new string(' ', part.Depth * 2) + part.Token + "   " + Describe(part)));
    }

    private static string Visible(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    private static TextBlock Line(string text) => new() { Text = text, FontFamily = Mono, FontSize = 12, Padding = new Thickness(6, 1), TextWrapping = TextWrapping.Wrap };

    /// <summary>The sentence for one part of the pattern, in the language of the interface.</summary>
    internal static string Describe(RegexPart part)
    {
        string text = part.Kind switch
        {
            "literal" => Loc.Get(K.RegexExplainLiteral, ("arg", part.Arg)),
            "any" => Loc.Get(K.RegexExplainAny),
            "digit" => Loc.Get(K.RegexExplainDigit),
            "notDigit" => Loc.Get(K.RegexExplainNotDigit),
            "word" => Loc.Get(K.RegexExplainWord),
            "notWord" => Loc.Get(K.RegexExplainNotWord),
            "space" => Loc.Get(K.RegexExplainSpace),
            "notSpace" => Loc.Get(K.RegexExplainNotSpace),
            "wordBoundary" => Loc.Get(K.RegexExplainWordBoundary),
            "notWordBoundary" => Loc.Get(K.RegexExplainNotWordBoundary),
            "startLine" => Loc.Get(K.RegexExplainStartLine),
            "endLine" => Loc.Get(K.RegexExplainEndLine),
            "startText" => Loc.Get(K.RegexExplainStartText),
            "endText" => Loc.Get(K.RegexExplainEndText),
            "endTextNewline" => Loc.Get(K.RegexExplainEndTextNewline),
            "charClass" => Loc.Get(K.RegexExplainClass, ("arg", part.Arg)),
            "negatedClass" => Loc.Get(K.RegexExplainNegatedClass, ("arg", part.Arg)),
            "controlChar" => Loc.Get(K.RegexExplainControl, ("token", part.Token)),
            "charCode" => Loc.Get(K.RegexExplainCharCode, ("arg", part.Arg)),
            "property" => Loc.Get(K.RegexExplainProperty, ("arg", part.Arg)),
            "notProperty" => Loc.Get(K.RegexExplainNotProperty, ("arg", part.Arg)),
            "backreference" => Loc.Get(K.RegexExplainBackreference, ("arg", part.Arg)),
            "group" => Loc.Get(K.RegexExplainGroup, ("arg", part.Arg)),
            "namedGroup" => Loc.Get(K.RegexExplainNamedGroup, ("arg", part.Arg)),
            "nonCapturing" => Loc.Get(K.RegexExplainNonCapturing),
            "lookahead" => Loc.Get(K.RegexExplainLookahead),
            "negativeLookahead" => Loc.Get(K.RegexExplainNegativeLookahead),
            "lookbehind" => Loc.Get(K.RegexExplainLookbehind),
            "negativeLookbehind" => Loc.Get(K.RegexExplainNegativeLookbehind),
            "atomic" => Loc.Get(K.RegexExplainAtomic),
            "optionGroup" => Loc.Get(K.RegexExplainOptionGroup, ("arg", part.Arg)),
            "inlineOptions" => Loc.Get(K.RegexExplainInlineOptions, ("arg", part.Arg)),
            "comment" => Loc.Get(K.RegexExplainComment, ("arg", part.Arg)),
            "groupEnd" => Loc.Get(K.RegexExplainGroupEnd),
            "alternation" => Loc.Get(K.RegexExplainAlternation),
            "quantOpt" => Loc.Get(K.RegexExplainOptional),
            "quantStar" => Loc.Get(K.RegexExplainStar),
            "quantPlus" => Loc.Get(K.RegexExplainPlus),
            "quantRange" => Range(part.Arg),
            _ => Loc.Get(K.RegexExplainUnsupported, ("token", part.Token)),
        };
        return part.Lazy ? text + " " + Loc.Get(K.RegexExplainLazy) : text;
    }

    private static string Range(string counts)
    {
        var parts = counts.Split(',');
        if (parts.Length == 1) return Loc.Get(K.RegexExplainExactly, ("n", parts[0]));
        return parts[1].Length == 0 ? Loc.Get(K.RegexExplainAtLeast, ("n", parts[0])) : Loc.Get(K.RegexExplainBetween, ("min", parts[0]), ("max", parts[1]));
    }
}

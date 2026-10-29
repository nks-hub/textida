using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.App.Editing;

/// <summary>What the buttons of the peek at a change do; the window supplies them.</summary>
internal sealed record GitPeekActions(Action Revert, Action Stage, Action Previous, Action Next, Action Close);

/// <summary>One line of the peek: its text, the words of it that changed, and whether it is a line of the old text.</summary>
internal sealed record GitPeekRow(string Text, IReadOnlyList<WordSpan> Changed, bool Old);

/// <summary>
/// The peek at a change in the editor: the old lines of the change in red above the new lines in green, with the words that changed inside a line marked, and the buttons to revert the change, to
/// stage it or take it back from the index, to go to the previous or the next change and to close the peek. It lies over the editor right under the change; Esc closes it.
/// </summary>
internal sealed class GitPeekView : Border
{
    private const int TabColumns = 4;
    private const int MaxLineChars = 2000;
    private readonly GitPeek _data;
    private readonly IReadOnlyList<string> _newLines;
    private readonly FontFamily _font;
    private readonly double _fontSize;
    private readonly int _index, _total;
    private readonly GitPeekActions _actions;
    private readonly bool _canRevert;
    private readonly Button _revert = new(), _stage = new(), _previous = new(), _next = new(), _close = new();
    private readonly TextBlock _header = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _rows = new();
    private readonly List<Grid> _rowControls = [];

    public GitPeekView(GitPeek data, IReadOnlyList<string> newLines, FontFamily font, double fontSize, int index, int total, GitPeekActions actions, bool canRevert)
    {
        _data = data;
        _newLines = newLines;
        _font = font;
        _fontSize = fontSize;
        _index = index;
        _total = total;
        _actions = actions;
        _canRevert = canRevert;
        HelpContext.Set(this, "tools.git");
        Focusable = true;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        Rows = BuildRows(data, newLines);
        _revert.Click += (_, _) => _actions.Revert();
        _stage.Click += (_, _) => _actions.Stage();
        _previous.Click += (_, _) => _actions.Previous();
        _next.Click += (_, _) => _actions.Next();
        _close.Click += (_, _) => _actions.Close();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            _actions.Close();
        };
        Child = Build();
        UpdateTexts();
    }

    /// <summary>The rows of the peek in order, for tests and for the screen reader's description.</summary>
    public IReadOnlyList<GitPeekRow> Rows { get; }

    public GitPeek Data => _data;

    /// <summary>The controls of the rows, one per entry of <see cref="Rows"/>, for tests.</summary>
    public IReadOnlyList<Grid> RowControls => _rowControls;
    public Button RevertButton => _revert;
    public Button StageButton => _stage;
    public Button PreviousButton => _previous;
    public Button NextButton => _next;
    public Button CloseButton => _close;
    public string HeaderText => _header.Text ?? "";

    /// <summary>Puts the keyboard focus on the first button that can be pressed.</summary>
    public void FocusFirst()
    {
        foreach (var button in new[] { _revert, _stage, _next, _close })
            if (button.IsEnabled && button.Focus()) return;
        Focus();
    }

    /// <summary>The old rows, then the new ones; the words that differ between the lines that stand in the same place of the two blocks are marked.</summary>
    internal static IReadOnlyList<GitPeekRow> BuildRows(GitPeek data, IReadOnlyList<string> newLines)
    {
        var rows = new List<GitPeekRow>(data.Old.Count + newLines.Count);
        bool pair = data.Mark.Kind == GitMarkKind.Modified;
        var oldMarks = new List<IReadOnlyList<WordSpan>>();
        var newMarks = new List<IReadOnlyList<WordSpan>>();
        for (int i = 0; i < Math.Max(data.Old.Count, newLines.Count); i++)
        {
            if (pair && i < data.Old.Count && i < newLines.Count)
            {
                var diff = WordDiff.Compute(data.Old[i], newLines[i]);
                oldMarks.Add(diff.Old);
                newMarks.Add(diff.New);
            }
            else
            {
                oldMarks.Add([]);
                newMarks.Add([]);
            }
        }
        for (int i = 0; i < data.Old.Count; i++) rows.Add(new GitPeekRow(data.Old[i], oldMarks[i], true));
        for (int i = 0; i < newLines.Count; i++) rows.Add(new GitPeekRow(newLines[i], newMarks[i], false));
        return rows;
    }

    private IBrush Brush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    private Control Build()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8, 6) };
        foreach (var button in new[] { _revert, _stage, _previous, _next })
        {
            button.Padding = new Thickness(10, 3);
            buttons.Children.Add(button);
        }
        var closeHost = new DockPanel { LastChildFill = false };
        _close.Padding = new Thickness(10, 3);
        _close.Margin = new Thickness(6, 6, 8, 6);
        DockPanel.SetDock(_close, Dock.Right);
        closeHost.Children.Add(_close);
        closeHost.Children.Add(buttons);
        var headerHost = new Border { Padding = new Thickness(10, 6), Child = _header };
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MaxHeight = PluginLimitsPlaceholders.GitPeekVisibleRows * Math.Ceiling(_fontSize * 1.5),
            Content = _rows,
        };
        var stack = new StackPanel { Children = { headerHost, scroller } };
        if (_data.Truncated)
        {
            var note = new TextBlock { Margin = new Thickness(10, 4), TextWrapping = TextWrapping.Wrap, Text = Loc.Plural(K.GitPeekTruncated, _data.Old.Count) };
            stack.Children.Add(note);
        }
        stack.Children.Add(closeHost);
        return stack;
    }

    /// <summary>Sets every text and name from the catalogue; again when the language changes.</summary>
    public void UpdateTexts()
    {
        _header.Text = (_total > 1 ? Loc.Get(K.GitPeekPosition, ("index", _index + 1), ("total", _total)) + " · " : "") + GitMargin.DescribeOf(_data.Mark);
        AutomationProperties.SetName(this, Loc.Get(K.A11yGitPeek, ("description", GitMargin.DescribeOf(_data.Mark))));
        void Set(Button button, string key, string tip, bool enabled = true)
        {
            string text = Loc.Get(key);
            button.Content = text;
            button.IsEnabled = enabled;
            AutomationProperties.SetName(button, text);
            ToolTip.SetTip(button, tip);
        }
        bool staged = _data.Mark.Staged;
        Set(_revert, K.GitPeekRevert, Loc.Get(K.GitPeekRevertTip), _canRevert && !_data.Truncated && !staged);
        Set(_stage, staged ? K.GitPeekUnstage : K.GitPeekStage, Loc.Get(staged ? K.GitPeekUnstageTip : K.GitPeekStageTip));
        Set(_previous, K.GitPeekPrevious, Loc.Get(K.GitPeekPreviousTip), _total > 1);
        Set(_next, K.GitPeekNext, Loc.Get(K.GitPeekNextTip), _total > 1);
        Set(_close, K.GitPeekClose, Loc.Get(K.GitPeekCloseTip));
        FillRows();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnTheme;
        Apply();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnTheme;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTheme(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        Background = Brush("Bg1", Brushes.White);
        BorderBrush = Brush("Border", Brushes.Gray);
        FillRows();
    }

    private static string Expand(string text)
    {
        if (text.Length > MaxLineChars) text = text[..MaxLineChars] + "…";
        if (text.IndexOf('\t') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (c == '\t') sb.Append(' ', TabColumns - sb.Length % TabColumns);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The rows are drawn with the brushes of the theme in use, so they are made again when it changes.</summary>
    private void FillRows()
    {
        _rows.Children.Clear();
        _rowControls.Clear();
        var removed = Brush("DiffRemoved", Brushes.MistyRose);
        var added = Brush("DiffAdded", Brushes.Honeydew);
        var removedWord = Brush("DiffRemovedWord", Brushes.LightCoral);
        var addedWord = Brush("DiffAddedWord", Brushes.LightGreen);
        var foreground = Brush("EditorFg", Brushes.Black);
        foreach (var row in Rows)
        {
            var text = new TextBlock { FontFamily = _font, FontSize = _fontSize, Foreground = foreground, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(4, 0, 12, 0) };
            string shown = row.Text;
            int at = 0;
            foreach (var span in row.Changed.OrderBy(s => s.Start))
            {
                if (span.Start < at || span.Start >= shown.Length) continue;
                int length = Math.Min(span.Length, shown.Length - span.Start);
                if (span.Start > at) text.Inlines!.Add(new Run(Expand(shown[at..span.Start])));
                text.Inlines!.Add(new Run(Expand(shown.Substring(span.Start, length))) { Background = row.Old ? removedWord : addedWord, FontWeight = FontWeight.SemiBold });
                at = span.Start + length;
            }
            if (at < shown.Length) text.Inlines!.Add(new Run(Expand(shown[at..])));
            if (shown.Length == 0) text.Inlines!.Add(new Run(" "));
            var sign = new TextBlock { Text = row.Old ? "−" : "+", FontFamily = _font, FontSize = _fontSize, Foreground = foreground, Width = 18, TextAlignment = TextAlignment.Center };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = row.Old ? removed : added, Children = { sign, text } };
            Grid.SetColumn(text, 1);
            AutomationProperties.SetName(line, (row.Old ? "−" : "+") + " " + row.Text);
            _rows.Children.Add(line);
            _rowControls.Add(line);
        }
    }
}

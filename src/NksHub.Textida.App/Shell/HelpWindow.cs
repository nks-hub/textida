using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>Offline help: a searchable list of topics on the left, the chosen topic on the right. All text comes from <see cref="HelpCatalog"/>; nothing is fetched.</summary>
internal sealed partial class HelpWindow : Window
{
    private readonly TextBox _search = new();
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly TextBlock _empty = new() { Margin = new Thickness(12, 6), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _build = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly ScrollViewer _scroller = new();
    private IReadOnlyList<HelpTopic> _shown = [];
    private string _currentId = HelpCatalog.StartTopic;

    public HelpWindow()
    {
        Classes.Add("shell");
        Width = 940;
        Height = 680;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var left = new DockPanel { Margin = new Thickness(12, 12, 6, 12), LastChildFill = true };
        DockPanel.SetDock(_search, Dock.Top);
        _search.Margin = new Thickness(0, 0, 0, 8);
        left.Children.Add(_search);
        left.Children.Add(new Panel { Children = { _list, _empty } });
        var leftHost = new Border { Width = 280, Child = left };

        _body.Margin = new Thickness(18, 16, 22, 20);
        _scroller.Content = new StackPanel { Children = { _body } };
        DockPanel.SetDock(leftHost, Dock.Left);
        Content = new DockPanel { Children = { leftHost, _scroller } };

        _search.TextChanged += (_, _) => Refresh(select: null);
        _search.AddHandler(KeyDownEvent, OnSearchKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.SelectionChanged += (_, _) => { if (_list.SelectedIndex is var i and >= 0 && i < _shown.Count) Render(_shown[i]); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        ApplyTexts();
        Refresh(select: HelpCatalog.StartTopic);
        Opened += (_, _) => _search.Focus();
    }

    /// <summary>The topic on screen, for tests.</summary>
    internal string CurrentTopic => _currentId;

    /// <summary>Titles in the topic list, for tests.</summary>
    internal IReadOnlyList<string> ShownTitles => [.. _shown.Select(t => t.Title)];

    internal string Query => _search.Text ?? "";

    internal string BodyText => string.Join('\n', _body.Children.Select(TextOf));

    private static string TextOf(Control c) => c switch
    {
        TextBlock t => t.Text ?? "",
        Grid g => string.Join(' ', g.Children.OfType<TextBlock>().Select(t => t.Text ?? "")),
        _ => "",
    };

    internal void Search(string text)
    {
        _search.Text = text;
        Refresh(select: null);
    }

    /// <summary>Shows a topic by id; the search is cleared so the topic is in the list. An unknown id shows the start page.</summary>
    internal void ShowTopic(string id)
    {
        _search.Text = "";
        Refresh(select: HelpCatalog.Find(Loc.Language, id) is null ? HelpCatalog.StartTopic : id);
    }

    private void OnLanguageChanged()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) ApplyTexts();
        else Avalonia.Threading.Dispatcher.UIThread.Post(ApplyTexts);
    }

    private void ApplyTexts()
    {
        Title = Loc.Get(K.HelpTitle);
        _search.PlaceholderText = Loc.Get(K.HelpSearchWatermark);
        _empty.Text = Loc.Get(K.HelpNoResults);
        _build.Text = Loc.Get(K.HelpBuildNote, ("version", typeof(HelpWindow).Assembly.GetName().Version?.ToString(3) ?? ""));
        AutomationProperties.SetName(_search, Loc.Get(K.HelpSearchA11y));
        AutomationProperties.SetName(_list, Loc.Get(K.HelpTopicsA11y));
        AutomationProperties.SetName(_scroller, Loc.Get(K.HelpContentA11y));
        Refresh(select: _currentId);
    }

    private void Refresh(string? select)
    {
        select ??= _currentId;
        _shown = HelpCatalog.Search(Loc.Language, Query);
        _list.Items.Clear();
        foreach (var topic in _shown) _list.Items.Add(new ListBoxItem { Content = topic.Title });
        _empty.IsVisible = _shown.Count == 0;
        if (_shown.Count == 0) return;
        int index = _shown.ToList().FindIndex(t => t.Id == select);
        _list.SelectedIndex = index >= 0 ? index : 0;
    }

    private void OnSearchKey(object? sender, KeyEventArgs e)
    {
        int step = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
        if (step == 0 || _shown.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + step, 0, _shown.Count - 1);
        _list.ScrollIntoView(_list.SelectedIndex);
        e.Handled = true;
    }

    private void Render(HelpTopic topic)
    {
        _currentId = topic.Id;
        _body.Children.Clear();
        _title.Text = topic.Title;
        _body.Children.Add(_title);
        _body.Children.Add(_build);
        foreach (var line in topic.Lines) _body.Children.Add(Block(line));
        _scroller.Offset = default;
    }

    private static Control Block(string line)
    {
        if (line.StartsWith("### ", StringComparison.Ordinal))
            return new SelectableTextBlock { Text = line[4..], FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        string marker = "";
        string text = line;
        if (line.StartsWith("- ", StringComparison.Ordinal)) (marker, text) = ("•", line[2..]);
        else if (Step().Match(line) is { Success: true } m) (marker, text) = (m.Groups[1].Value + ".", m.Groups[2].Value);
        var block = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (marker.Length == 0) return block;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*"), Margin = new Thickness(6, 0, 0, 0) };
        row.Children.Add(new TextBlock { Text = marker, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Left });
        Grid.SetColumn(block, 1);
        row.Children.Add(block);
        return row;
    }

    [GeneratedRegex(@"^(\d+)\. (.*)$")]
    private static partial Regex Step();
}

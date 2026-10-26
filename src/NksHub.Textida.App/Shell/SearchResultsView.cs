using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The content of the "Search Results" tab: a heading with the query and the counts, a note when the result limit was reached, and the matches grouped by file.
/// A search shows the matches; a replace shows each change as the removed text in red and the new text in green, with a checkbox per file and per change,
/// and after applying the outcome of every file. The window feeds it; it holds no search logic.
/// </summary>
internal sealed class SearchResultsView : DockPanel
{
    public abstract class Row;

    public sealed class FileRow(string key, string display, int count, FileChange? change = null) : Row
    {
        public string Key { get; } = key;
        public string Display { get; } = display;
        public int Count { get; } = count;
        public FileChange? Change { get; } = change;
        public bool Selected { get; set; } = true;
        public string? Status { get; set; }
        public bool Failed { get; set; }
    }

    public sealed class HitRow(SearchHit hit) : Row
    {
        public SearchHit Hit { get; } = hit;
    }

    public sealed class HunkRow(FileRow file, Hunk hunk) : Row
    {
        public FileRow File { get; } = file;
        public Hunk Hunk { get; } = hunk;
        public bool Selected { get; set; } = true;
    }

    public sealed class NoteRow(string text, bool warning) : Row
    {
        public string Text { get; } = text;
        public bool Warning { get; } = warning;
    }

    private const int ContextBefore = 60, ContextAfter = 90;
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.Medium, Margin = new Thickness(28, 20, 28, 2), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summary = new() { FontSize = 12, Margin = new Thickness(28, 0, 28, 6), FontFamily = Mono, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _limit = new() { FontSize = 12, Margin = new Thickness(28, 0, 28, 6), TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 2, MinHeight = 2, IsVisible = false, Margin = new Thickness(28, 0, 28, 4) };
    private readonly Button _cancel = new() { Padding = new Thickness(10, 2), MinHeight = 24 };
    private readonly Button _apply = new() { Padding = new Thickness(10, 2), MinHeight = 24, IsVisible = false };
    private readonly Button _repreview = new() { Padding = new Thickness(10, 2), MinHeight = 24, IsVisible = false };
    private readonly ListBox _list = new() { Background = Brushes.Transparent, Margin = new Thickness(16, 0, 16, 8) };
    private readonly AvaloniaList<Row> _rows = [];
    private bool _running;

    public SearchResultsView()
    {
        Classes.Add("results");
        Focusable = true;
        GotFocus += (_, e) => { if (ReferenceEquals(e.Source, this)) _list.Focus(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(28, 0, 28, 8), Children = { _cancel, _apply, _repreview } };
        foreach (var b in new[] { _cancel, _apply, _repreview }) b.Classes.Add("barbutton");
        SetDock(_title, Dock.Top);
        SetDock(_summary, Dock.Top);
        SetDock(_limit, Dock.Top);
        SetDock(_progress, Dock.Top);
        SetDock(buttons, Dock.Top);
        Children.Add(_title);
        Children.Add(_summary);
        Children.Add(_limit);
        Children.Add(_progress);
        Children.Add(buttons);

        _list.Classes.Add("resultlist");
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<Row>((row, _) => row is null ? null : Build(row));
        _list.DoubleTapped += (_, e) => OpenSelected(e);
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.Handled) { OpenSelected(e); e.Handled = true; }
        };
        Children.Add(_list);

        _cancel.Click += (_, _) => CancelRequested?.Invoke();
        _apply.Click += (_, _) => ApplyRequested?.Invoke();
        _repreview.Click += (_, _) => RepreviewRequested?.Invoke();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && _running) { CancelRequested?.Invoke(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ApplyTexts();
    }

    public IReadOnlyList<Row> Rows => _rows;
    public ListBox List => _list;
    /// <summary>Highlights the match in each line of the results; the search pattern as a regex, or null for no highlight.</summary>
    public Regex? Highlight { get; set; }
    public string TitleText => _title.Text ?? "";
    public string SummaryText => _summary.Text ?? "";
    public string LimitText => _limit.IsVisible ? _limit.Text ?? "" : "";
    public bool IsRunning => _running;
    public Button CancelButton => _cancel;
    public Button ApplyButton => _apply;
    public Button RepreviewButton => _repreview;

    public event Action? CancelRequested;
    public event Action<Row>? OpenRequested;
    public event Action? ApplyRequested;
    public event Action? RepreviewRequested;

    public void ApplyTexts()
    {
        _cancel.Content = Loc.Get(K.ResultsCancel);
        _apply.Content = Loc.Get(K.ResultsApply);
        _repreview.Content = Loc.Get(K.ResultsRepreview);
        AutomationProperties.SetName(_list, Loc.Get(K.A11yResultsList));
    }

    public void Clear()
    {
        _rows.Clear();
        _limit.IsVisible = false;
        _apply.IsVisible = false;
        _repreview.IsVisible = false;
    }

    public void SetHeader(string title, string summary)
    {
        _title.Text = title;
        _summary.Text = summary;
    }

    public void SetRunning(bool running)
    {
        _running = running;
        _progress.IsVisible = running;
        _cancel.IsVisible = running;
    }

    public void SetLimit(string? text)
    {
        _limit.Text = text;
        _limit.IsVisible = !string.IsNullOrEmpty(text);
    }

    public void SetApplyVisible(bool apply, bool repreview)
    {
        _apply.IsVisible = apply;
        _repreview.IsVisible = repreview;
    }

    /// <summary>Appends matches; the hits of one file arrive together and in order, so a file row heads each run.</summary>
    public void AddHits(IReadOnlyList<SearchHit> hits, Func<string, string> display)
    {
        var batch = new List<Row>();
        for (int i = 0; i < hits.Count;)
        {
            var source = hits[i].Source;
            int end = i;
            while (end < hits.Count && ReferenceEquals(hits[end].Source, source)) end++;
            batch.Add(new FileRow(source.Path ?? source.DocumentId?.ToString() ?? source.Title, display(source.Path ?? source.Title), end - i));
            for (int j = i; j < end; j++) batch.Add(new HitRow(hits[j]));
            i = end;
        }
        _rows.AddRange(batch);
    }

    public void AddPlan(ChangeProposal plan, Func<string, string> display)
    {
        var batch = new List<Row>();
        foreach (var file in plan.Files)
        {
            var head = new FileRow(file.Key, display(file.DisplayName), file.Hunks.Count, file);
            batch.Add(head);
            batch.AddRange(file.Hunks.Select(h => new HunkRow(head, h)));
        }
        _rows.AddRange(batch);
    }

    public void AddNote(string text, bool warning) => _rows.Add(new NoteRow(text, warning));

    public void ReplaceRows(IEnumerable<Row> rows)
    {
        _rows.Clear();
        _rows.AddRange(rows);
    }

    private void OpenSelected(Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_list.SelectedItem is Row row and (HitRow or HunkRow or FileRow)) OpenRequested?.Invoke(row);
    }

    // ---- rows ----

    private Control Build(Row row) => row switch
    {
        FileRow f => BuildFile(f),
        HitRow h => BuildHit(h),
        HunkRow h => BuildHunk(h),
        NoteRow n => BuildNote(n),
        _ => new Border(),
    };

    private Control BuildFile(FileRow f)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(f.Change is null ? "*,Auto" : "Auto,*,Auto,Auto"), Margin = new Thickness(12, 12, 12, 2) };
        int offset = 0;
        if (f.Change is not null)
        {
            var box = new CheckBox { IsChecked = f.Selected, Margin = new Thickness(0, 0, 4, 0), MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            box.IsCheckedChanged += (_, _) => f.Selected = box.IsChecked == true;
            AutomationProperties.SetName(box, Loc.Get(K.ResultsIncludeFile, ("file", f.Display)));
            grid.Children.Add(box);
            offset = 1;
        }
        var name = new TextBlock { Text = f.Display, FontFamily = Mono, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.Classes.Add("resultfile");
        Grid.SetColumn(name, offset);
        grid.Children.Add(name);
        if (f.Status is { } status)
        {
            var st = new TextBlock { Text = status, Margin = new Thickness(12, 0, 0, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            st.Classes.Add(f.Failed ? "resultbad" : "resultmuted");
            Grid.SetColumn(st, offset + 1);
            grid.Children.Add(st);
        }
        var count = new TextBlock { Text = f.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), FontFamily = Mono, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        count.Classes.Add("resultmuted");
        Grid.SetColumn(count, grid.ColumnDefinitions.Count - 1);
        grid.Children.Add(count);
        AutomationProperties.SetName(grid, Loc.Get(K.ResultsFileA11y, ("file", f.Display), ("matches", f.Count)));
        return grid;
    }

    private Control BuildHit(HitRow h)
    {
        var line = new TextBlock { Text = h.Hit.Line.ToString(System.Globalization.CultureInfo.CurrentCulture), FontFamily = Mono, FontSize = 12, TextAlignment = TextAlignment.Right, Width = 44, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        line.Classes.Add("resultmuted");
        var text = new TextBlock { FontFamily = Mono, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        string preview = h.Hit.Preview;
        int col = h.Hit.Column, length = MatchLength(preview, col);
        if (length > 0)
        {
            text.Inlines!.Add(new Run(preview[..col]));
            text.Inlines.Add(Marked(text, preview.Substring(col, length), "Highlight"));
            text.Inlines.Add(new Run(preview[(col + length)..]));
        }
        else text.Inlines!.Add(new Run(preview));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 1, 12, 1), Children = { line, text } };
        AutomationProperties.SetName(row, Loc.Get(K.ResultsHitA11y, ("line", h.Hit.Line), ("text", preview)));
        return row;
    }

    /// <summary>Length of the match that starts at the column of the hit in its preview; 0 when it cannot be told.</summary>
    public int MeasureMatch(HitRow h) => MatchLength(h.Hit.Preview, h.Hit.Column);

    private int MatchLength(string preview, int column)
    {
        if (Highlight is not { } regex || column < 0 || column >= preview.Length) return 0;
        try
        {
            var m = regex.Match(preview, column);
            return m.Success && m.Index == column ? m.Length : Math.Min(1, preview.Length - column);
        }
        catch (RegexMatchTimeoutException)
        {
            return 0;
        }
    }

    private Control BuildHunk(HunkRow h)
    {
        var box = new CheckBox { IsChecked = h.Selected, MinWidth = 0, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Top };
        box.IsCheckedChanged += (_, _) => h.Selected = box.IsChecked == true;
        AutomationProperties.SetName(box, Loc.Get(K.ResultsIncludeChange, ("line", h.Hunk.Line)));
        var line = new TextBlock { Text = h.Hunk.Line.ToString(System.Globalization.CultureInfo.CurrentCulture), FontFamily = Mono, FontSize = 12, TextAlignment = TextAlignment.Right, Width = 44, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        line.Classes.Add("resultmuted");
        var old = Diff(h.Hunk, removed: true);
        var added = Diff(h.Hunk, removed: false);
        var sign1 = new TextBlock { Text = "−", FontFamily = Mono, Width = 16, VerticalAlignment = VerticalAlignment.Top };
        var sign2 = new TextBlock { Text = "+", FontFamily = Mono, Width = 16, VerticalAlignment = VerticalAlignment.Top };
        sign1.Classes.Add("resultbad");
        sign2.Classes.Add("resultgood");
        var lines = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        Grid.SetRow(sign2, 1);
        Grid.SetRow(added, 1);
        Grid.SetColumn(old, 1);
        Grid.SetColumn(added, 1);
        lines.Children.Add(sign1);
        lines.Children.Add(old);
        lines.Children.Add(sign2);
        lines.Children.Add(added);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 1, 12, 5), Children = { box, line, lines } };
        if (h.Hunk.Problem is { } problem) ToolTip.SetTip(row, problem);
        AutomationProperties.SetName(row, Loc.Get(K.ResultsChangeA11y, ("line", h.Hunk.Line), ("old", h.Hunk.OldText), ("new", h.Hunk.NewText)));
        return row;
    }

    /// <summary>One line of a change: the text before, the removed or the added text marked, the text after.</summary>
    private static TextBlock Diff(Hunk h, bool removed)
    {
        var block = new TextBlock { FontFamily = Mono, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
        string before = h.LineBefore.Length > ContextBefore ? h.LineBefore[^ContextBefore..] : h.LineBefore;
        string after = h.LineAfter.Length > ContextAfter ? h.LineAfter[..ContextAfter] : h.LineAfter;
        string middle = Flat(removed ? h.OldText : h.NewText);
        block.Inlines!.Add(new Run(before));
        if (middle.Length > 0) block.Inlines.Add(Marked(block, middle, removed ? "DiffRemoved" : "DiffAdded"));
        block.Inlines.Add(new Run(after));
        return block;
    }

    private static string Flat(string text) => text.Replace("\r\n", "↵").Replace("\n", "↵").Replace("\r", "↵");

    private static Run Marked(Control host, string text, string brushKey)
    {
        var run = new Run(text) { FontWeight = FontWeight.SemiBold };
        run.Bind(Inline.BackgroundProperty, host.GetResourceObservable(brushKey));
        return run;
    }

    private static Control BuildNote(NoteRow n)
    {
        var text = new TextBlock { Text = n.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 6, 12, 6), FontSize = 12 };
        text.Classes.Add(n.Warning ? "resultbad" : "resultmuted");
        AutomationProperties.SetName(text, n.Text);
        return text;
    }
}

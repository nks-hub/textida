using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private static readonly Geometry SearchGlyph = Geometry.Parse("M9.5,3.5 A5.5,5.5 0 1 0 9.5,14.5 A5.5,5.5 0 1 0 9.5,3.5 M13.5,13.5 L18,18");

    private readonly Panel _paletteHost = new() { IsVisible = false };
    private readonly Border _palette = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), Width = 640 };
    private readonly TextBox _paletteInput = new();
    private readonly ListBox _paletteList = new() { MaxHeight = 380, Background = Brushes.Transparent };
    private readonly TextBlock _paletteEmpty = new() { Margin = new Thickness(14, 10), IsVisible = false };
    private List<PaletteItem> _paletteItems = [];
    private DocTab? _paletteReturnTo;

    internal bool IsPaletteOpen => _paletteHost.IsVisible;
    internal TextBox PaletteInput => _paletteInput;
    internal IReadOnlyList<PaletteItem> PaletteResults => _paletteItems;

    private Control BuildPalette()
    {
        var scrim = new Border();
        scrim.Classes.Add("scrim");
        scrim.PointerPressed += (_, _) => ClosePalette();
        _palette.Classes.Add("palette");
        _paletteList.Classes.Add("paletteList");
        var glyph = new Avalonia.Controls.Shapes.Path { Data = SearchGlyph, Stroke = Brushes.Gray, StrokeThickness = 1.4, Width = 18, Height = 18, Stretch = Stretch.Uniform, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var esc = new Border { Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = new NksHub.Textida.Workbench.Keys.KeyChord(NksHub.Textida.Workbench.Keys.KeyModifiers.None, "escape").Display(_app.Workbench.Platform), FontSize = 11, Opacity = 0.7 } };
        esc.Classes.Add("escchip");
        _paletteInput.InnerLeftContent = glyph;
        _paletteInput.InnerRightContent = esc;
        _palette.Child = new StackPanel { Children = { _paletteInput, _paletteList, _paletteEmpty } };
        _paletteInput.Classes.Add("paletteinput");
        _paletteInput.TextChanged += (_, _) => RefreshPalette();
        _paletteInput.AddHandler(KeyDownEvent, OnPaletteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _paletteList.DoubleTapped += (_, _) => RunPaletteSelection();
        _paletteHost.Children.Add(scrim);
        _paletteHost.Children.Add(_palette);
        return _paletteHost;
    }

    private void ApplyPaletteTexts()
    {
        _paletteInput.PlaceholderText = Loc.Get(K.PaletteWatermark);
        _paletteEmpty.Text = Loc.Get(K.PaletteNoResults);
        AutomationProperties.SetName(_paletteInput, Loc.Get(K.A11yPalette));
        AutomationProperties.SetName(_paletteList, Loc.Get(K.A11yPaletteResults));
    }

    public void ShowPalette()
    {
        _paletteReturnTo = Active;
        _paletteHost.IsVisible = true;
        _paletteInput.Text = "";
        RefreshPalette();
        Dispatcher.UIThread.Post(() => _paletteInput.Focus(), DispatcherPriority.Input);
    }

    public void ClosePalette()
    {
        if (!_paletteHost.IsVisible) return;
        _paletteHost.IsVisible = false;
        if (_paletteReturnTo is { } tab && tab.Group is not null) FocusView(tab);
    }

    internal void SetPaletteQuery(string text)
    {
        _paletteInput.Text = text;
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        var wb = _app.Workbench;
        var context = new ShellContext(this, forMenu: true);
        string query = _paletteInput.Text ?? "";
        _paletteItems = [.. CommandPalette.Query(wb.Commands, wb.Keymap, wb.Strings, context, query, 400).Where(i => wb.Commands.HasHandler(i.CommandId)).Take(60)];
        _paletteList.Items.Clear();
        foreach (var item in _paletteItems)
        {
            var category = new TextBlock { Text = item.Category ?? "", Opacity = 0.6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            foreach (var run in Highlighted(item.Title, query)) title.Inlines!.Add(run);
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var key in KeyCaps(item.Shortcut)) keys.Children.Add(KeyCap(key));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), Opacity = item.Enabled ? 1 : 0.5 };
            Grid.SetColumn(title, 1);
            Grid.SetColumn(keys, 2);
            row.Children.Add(category);
            row.Children.Add(title);
            row.Children.Add(keys);
            _paletteList.Items.Add(new ListBoxItem { Content = row, Tag = item.CommandId });
            AutomationProperties.SetName(row, (item.Category is null ? "" : item.Category + ": ") + item.Title + (item.Shortcut is null ? "" : ", " + item.Shortcut));
        }
        if (_paletteItems.Count > 0) _paletteList.SelectedIndex = 0;
        _paletteList.IsVisible = _paletteItems.Count > 0;
        _paletteEmpty.IsVisible = _paletteItems.Count == 0;
    }

    private static Border KeyCap(string key)
    {
        var cap = new Border { Child = new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center } };
        cap.Classes.Add("keycap");
        return cap;
    }

    /// <summary>"Ctrl+Shift+P" becomes three caps; on macOS every glyph of "⇧⌘P" is a cap.</summary>
    private static IEnumerable<string> KeyCaps(string? shortcut)
    {
        if (string.IsNullOrEmpty(shortcut)) return [];
        return shortcut.Contains('+') ? SplitPlus(shortcut) : shortcut.Select(c => c.ToString());
    }

    private static IEnumerable<string> SplitPlus(string shortcut)
    {
        // a trailing plus is the key itself ("Ctrl++")
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in shortcut)
        {
            if (c == '+' && current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    // ---- highlighting of what the query matched ----

    private static char Base(char c)
    {
        var d = c.ToString().Normalize(System.Text.NormalizationForm.FormD);
        return char.ToLowerInvariant(d.Length > 0 ? d[0] : c);
    }

    internal static IEnumerable<Run> Highlighted(string text, string query)
    {
        var marks = MatchedChars(text, query);
        var runs = new List<Run>();
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && marks[i] == marks[start]) continue;
            var run = new Run(text[start..i]);
            if (marks[start]) run.Background = new SolidColorBrush(Color.Parse("#99FFD866"));
            runs.Add(run);
            start = i;
        }
        return runs;
    }

    private static bool[] MatchedChars(string text, string query)
    {
        var marks = new bool[text.Length];
        query = query.Trim();
        if (query.Length == 0 || text.Length == 0) return marks;
        var folded = new string([.. text.Select(Base)]);
        var q = new string([.. query.Select(Base)]);
        int at = folded.IndexOf(q, StringComparison.Ordinal);
        if (at >= 0)
        {
            for (int i = at; i < at + q.Length; i++) marks[i] = true;
            return marks;
        }
        int qi = 0;
        for (int i = 0; i < folded.Length && qi < q.Length; i++)
            if (folded[i] == q[qi]) { marks[i] = true; qi++; }
        if (qi < q.Length) Array.Clear(marks);
        return marks;
    }

    private void OnPaletteKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: MovePalette(1); e.Handled = true; break;
            case Key.Up: MovePalette(-1); e.Handled = true; break;
            case Key.Enter: RunPaletteSelection(); e.Handled = true; break;
            case Key.Escape: ClosePalette(); e.Handled = true; break;
        }
    }

    private void MovePalette(int step)
    {
        if (_paletteItems.Count == 0) return;
        _paletteList.SelectedIndex = Math.Clamp(_paletteList.SelectedIndex + step, 0, _paletteItems.Count - 1);
        _paletteList.ScrollIntoView(_paletteList.SelectedIndex);
    }

    internal void RunPaletteSelection()
    {
        int index = _paletteList.SelectedIndex;
        if (index < 0 || index >= _paletteItems.Count) return;
        var item = _paletteItems[index];
        ClosePalette();
        _ = _app.Workbench.Commands.ExecuteAsync(item.CommandId, new ShellContext(this, forMenu: true));
    }
}

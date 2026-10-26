using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Shell;

/// <summary>What the floating list at the top of the window shows.</summary>
internal enum PaletteMode { Commands, Files, Edits, Choice }

/// <summary>One row of any palette mode; <paramref name="Run"/> is what Enter does. A row that is not enabled is shown dimmed with its <paramref name="Reason"/>.</summary>
internal sealed record QuickRow(string Title, string? Category, string? Detail, string? Shortcut, bool Enabled, string? Reason, string AutomationName, Action Run, Func<string>? Preview = null);

internal sealed partial class MainWindow
{
    private static readonly Geometry SearchGlyph = Geometry.Parse("M9.5,3.5 A5.5,5.5 0 1 0 9.5,14.5 A5.5,5.5 0 1 0 9.5,3.5 M13.5,13.5 L18,18");

    private readonly Panel _paletteHost = new() { IsVisible = false };
    private readonly Border _palette = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 24, 0, 0), Width = 640 };
    private readonly TextBox _paletteInput = new();
    private readonly ListBox _paletteList = new() { MaxHeight = 380, Background = Brushes.Transparent };
    private readonly TextBlock _paletteEmpty = new() { Margin = new Thickness(14, 10), IsVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _paletteOnlyModified = new() { IsVisible = false, Margin = new Thickness(14, 6, 14, 2) };
    private readonly TextBlock _palettePreview = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12, Margin = new Thickness(14, 8), MaxHeight = 190, TextWrapping = TextWrapping.NoWrap };
    private readonly Border _palettePreviewHost = new() { IsVisible = false, BorderThickness = new Thickness(0, 1, 0, 0), ClipToBounds = true };
    private List<PaletteItem> _paletteItems = [];
    private List<QuickRow> _quickRows = [];
    private IReadOnlyList<QuickRow> _choiceRows = [];
    private PaletteMode _paletteMode;
    private string _choiceWatermark = "", _choiceName = "";
    private DocTab? _paletteReturnTo;

    internal bool IsPaletteOpen => _paletteHost.IsVisible;
    internal PaletteMode PaletteModeNow => _paletteMode;
    internal TextBox PaletteInput => _paletteInput;
    /// <summary>The command rows of the palette; empty in the other modes.</summary>
    internal IReadOnlyList<PaletteItem> PaletteResults => _paletteMode == PaletteMode.Commands ? _paletteItems : [];
    /// <summary>The rows of any mode, in the order they are shown.</summary>
    internal IReadOnlyList<QuickRow> QuickResults => _quickRows;
    internal string PalettePreviewText => _palettePreview.Text ?? "";
    internal CheckBox PaletteOnlyModified => _paletteOnlyModified;

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
        _palettePreviewHost.Child = _palettePreview;
        _palettePreviewHost.Classes.Add("palettepreview");
        _palette.Child = new StackPanel { Children = { _paletteInput, _paletteOnlyModified, _paletteList, _paletteEmpty, _palettePreviewHost } };
        _paletteInput.Classes.Add("paletteinput");
        _paletteInput.TextChanged += (_, _) => RefreshPalette();
        _paletteInput.AddHandler(KeyDownEvent, OnPaletteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _paletteList.DoubleTapped += (_, _) => RunPaletteSelection();
        _paletteList.SelectionChanged += (_, _) => UpdatePalettePreview();
        _paletteOnlyModified.Classes.Add("panelaction");
        _paletteOnlyModified.IsCheckedChanged += (_, _) => RefreshPalette();
        _paletteHost.Children.Add(scrim);
        _paletteHost.Children.Add(_palette);
        return _paletteHost;
    }

    private void ApplyPaletteTexts()
    {
        (_paletteInput.PlaceholderText, string name, string results, _paletteEmpty.Text) = _paletteMode switch
        {
            PaletteMode.Files => (Loc.Get(K.PaletteFilesWatermark), Loc.Get(K.A11yQuickOpen), Loc.Get(K.A11yQuickOpenResults), Loc.Get(K.PaletteFilesNoResults)),
            PaletteMode.Edits => (Loc.Get(K.PaletteEditsWatermark), Loc.Get(K.A11yEdits), Loc.Get(K.A11yEditsResults), Loc.Get(K.PaletteEditsNoResults)),
            PaletteMode.Choice => (_choiceWatermark, _choiceName, _choiceName, Loc.Get(K.PaletteFilesNoResults)),
            _ => (Loc.Get(K.PaletteWatermark), Loc.Get(K.A11yPalette), Loc.Get(K.A11yPaletteResults), Loc.Get(K.PaletteNoResults)),
        };
        _paletteOnlyModified.Content = Loc.Get(K.PaletteOnlyModified);
        AutomationProperties.SetName(_paletteInput, name);
        AutomationProperties.SetName(_paletteList, results);
        AutomationProperties.SetName(_paletteOnlyModified, Loc.Get(K.PaletteOnlyModified));
        AutomationProperties.SetName(_palettePreview, Loc.Get(K.A11yPreview));
    }

    // ---- opening in a mode ----

    public void ShowPalette() => OpenPalette(PaletteMode.Commands, "");

    /// <summary>Go to Anything: files by part of the path, open documents, recent files, and <c>:120</c> for a line.</summary>
    public void ShowQuickOpen(string initial = "") => OpenPalette(PaletteMode.Files, initial);

    public void ShowEditLocations() => OpenPalette(PaletteMode.Edits, "");

    /// <summary>A short list to choose from, such as the candidates of "related file".</summary>
    internal void ShowChoice(string watermark, string name, IReadOnlyList<QuickRow> rows)
    {
        _choiceWatermark = watermark;
        _choiceName = name;
        _choiceRows = rows;
        OpenPalette(PaletteMode.Choice, "");
    }

    private void OpenPalette(PaletteMode mode, string initial)
    {
        _paletteReturnTo = Active;
        _paletteMode = mode;
        _paletteOnlyModified.IsChecked = false;
        _paletteOnlyModified.IsVisible = mode == PaletteMode.Edits;
        _palettePreviewHost.IsVisible = false;
        ApplyPaletteTexts();
        if (mode == PaletteMode.Files)
        {
            RestoreFolder();
            EnsureWorkspaceIndex();
        }
        _paletteHost.IsVisible = true;
        _paletteInput.Text = initial;
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

    // ---- rows per mode ----

    private void RefreshPalette()
    {
        string query = _paletteInput.Text ?? "";
        string highlight = query;
        string? emptyText = null;
        _paletteItems = [];
        switch (_paletteMode)
        {
            case PaletteMode.Commands:
                _quickRows = CommandRows(query, out highlight, out emptyText);
                break;
            case PaletteMode.Files:
                _quickRows = FileRows(query, out highlight, out emptyText);
                break;
            case PaletteMode.Edits:
                _quickRows = EditRows(query);
                break;
            default:
                _quickRows = [.. _choiceRows.Where(r => Matches(query, r.Title + " " + r.Detail))];
                break;
        }
        RenderRows(highlight);
        _paletteEmpty.Text = emptyText ?? _paletteEmpty.Text;
        UpdatePalettePreview();
    }

    private static bool Matches(string query, string text) =>
        TextMatching.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => TextMatching.Fold(text).Contains(w, StringComparison.Ordinal));

    private List<QuickRow> CommandRows(string query, out string highlight, out string? emptyText)
    {
        var wb = _app.Workbench;
        highlight = query;
        emptyText = Loc.Get(K.PaletteNoResults);
        if (query.StartsWith('/'))
        {
            string text = query[1..].Trim();
            highlight = text;
            emptyText = Loc.Get(K.PaletteSettingsNoResults);
            return [.. wb.SearchIndex.Search(text, wb.Settings, wb.Commands, wb.Keymap, null)
                .Where(h => h.Kind == NksHub.Textida.Workbench.Settings.SearchHitKind.Setting).Take(60)
                .Select(h => new QuickRow(h.Title, h.Category, h.ValueText?.Trim('"'), null, true, null,
                    $"{h.Category}: {h.Title}, {h.ValueText?.Trim('"')}", () => _app.OpenSettings(text.Length == 0 ? h.Id : text)))];
        }
        if (query.StartsWith('='))
        {
            highlight = "";
            return [CalculatorRow(query[1..])];
        }

        var context = new ShellContext(this, forMenu: true);
        _paletteItems = [.. CommandPalette.Query(wb.Commands, wb.Keymap, wb.Strings, context, query, 400).Where(i => wb.Commands.HasHandler(i.CommandId)).Take(60)];
        return [.. _paletteItems.Select(item => new QuickRow(item.Title, item.Category, null, item.Shortcut, item.Enabled, item.DisabledReason,
            (item.Category is null ? "" : item.Category + ": ") + item.Title + (item.Shortcut is null ? "" : ", " + item.Shortcut) + (item.DisabledReason is null ? "" : ", " + item.DisabledReason),
            () => _ = wb.Commands.ExecuteAsync(item.CommandId, new ShellContext(this, forMenu: true))))];
    }

    /// <summary>The result of <c>=1+2*3</c>. The text goes through <see cref="Calculator"/>, a parser of arithmetic and nothing else; no code is ever run.</summary>
    private QuickRow CalculatorRow(string expression)
    {
        var result = Calculator.Evaluate(expression);
        string category = Loc.Get(K.PaletteCategoryCalc);
        if (!result.Ok)
        {
            string error = result.Error switch
            {
                CalculatorError.Empty => Loc.Get(K.PaletteCalcEmpty),
                CalculatorError.DivisionByZero => Loc.Get(K.PaletteCalcDivision),
                CalculatorError.Overflow => Loc.Get(K.PaletteCalcOverflow),
                CalculatorError.TooComplex => Loc.Get(K.PaletteCalcComplex),
                _ => Loc.Get(K.PaletteCalcSyntax),
            };
            return new QuickRow(error, category, null, null, false, null, category + ": " + error, () => { });
        }
        string value = result.Text;
        return new QuickRow("= " + value, category, Loc.Get(K.PaletteCalcCopy), null, true, null, category + ": " + value,
            () => { _ = Clipboard?.SetTextAsync(value); SetStatusMessage(Loc.Get(K.PaletteCalcCopied, ("value", value))); });
    }

    private void RenderRows(string highlight)
    {
        _paletteList.Items.Clear();
        foreach (var row in _quickRows)
        {
            var category = new TextBlock { Text = row.Category ?? "", Opacity = 0.6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            foreach (var run in Highlighted(row.Title, highlight)) title.Inlines!.Add(run);
            string? note = row.Detail is null ? row.Reason : row.Reason is null ? row.Detail : row.Detail + " · " + row.Reason;
            if (note is not null) title.Inlines!.Add(new Run("  " + note) { FontSize = 12, Foreground = Brushes.Gray });
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var key in KeyCaps(row.Shortcut)) keys.Children.Add(key.Length == 0 ? new Border { Width = 6 } : KeyCap(key));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), Opacity = row.Enabled ? 1 : 0.5 };
            Grid.SetColumn(title, 1);
            Grid.SetColumn(keys, 2);
            grid.Children.Add(category);
            grid.Children.Add(title);
            grid.Children.Add(keys);
            _paletteList.Items.Add(new ListBoxItem { Content = grid });
            AutomationProperties.SetName(grid, row.AutomationName);
        }
        if (_quickRows.Count > 0) _paletteList.SelectedIndex = 0;
        _paletteList.IsVisible = _quickRows.Count > 0;
        _paletteEmpty.IsVisible = _quickRows.Count == 0;
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
        // a sequence ("Ctrl+K Ctrl+O", "⌘K ⌘O") is a group of caps per chord with a gap between them
        var caps = new List<string>();
        foreach (var chord in shortcut.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (caps.Count > 0) caps.Add("");
            caps.AddRange(chord.Contains('+') ? SplitPlus(chord) : chord.Select(c => c.ToString()));
        }
        return caps;
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

    // ---- keys ----

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
        if (_quickRows.Count == 0) return;
        _paletteList.SelectedIndex = Math.Clamp(_paletteList.SelectedIndex + step, 0, _quickRows.Count - 1);
        _paletteList.ScrollIntoView(_paletteList.SelectedIndex);
    }

    internal void RunPaletteSelection()
    {
        int index = _paletteList.SelectedIndex;
        if (index < 0 || index >= _quickRows.Count) return;
        var row = _quickRows[index];
        // a calculator error or a missing file stays on screen; a disabled command closes the palette and the registry refuses it
        bool isCommand = _paletteMode == PaletteMode.Commands && _paletteItems.Count == _quickRows.Count;
        if (!row.Enabled && !isCommand) return;
        ClosePalette();
        row.Run();
    }
}

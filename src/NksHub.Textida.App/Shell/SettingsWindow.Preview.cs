using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;


namespace NksHub.Textida.App.Shell;

/// <summary>
/// The preview at the top of the Appearance and Editor › Display pages: a few lines of code drawn with the font, size and fallback fonts of the editor, the line numbers, the current line, a selection and
/// the whitespace marks as the settings have them now, highlighted in the colour theme of the editor with its custom colours. It follows every change made on the page.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The pages that show the preview.</summary>
    private static readonly HashSet<string> PreviewNodes = [Workbench.Settings.SettingCategories.Appearance, Workbench.Settings.SettingCategories.EditorDisplay];

    private EditorPreview? _preview;

    internal EditorPreview? Preview => _preview;

    private Border PreviewCard()
    {
        _preview = new EditorPreview(_app);
        var (card, body) = Card(Loc.Get(K.SettingsPreviewTitle), "settingpreviewtitle");
        _preview.Margin = new Thickness(14, 0, 14, 12);
        body.Children.Add(_preview);
        return card;
    }

    internal sealed class EditorPreview : Border
    {
        private readonly App _app;
        private readonly Grid _lines = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

        public EditorPreview(App app)
        {
            _app = app;
            Classes.Add("settingpreview");
            CornerRadius = new CornerRadius(6);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(0, 6);
            Child = _lines;
            AutomationProperties.SetName(this, Loc.Get(K.SettingsPreviewA11y));
            Refresh();
        }

        /// <summary>The font family the preview uses: the main font list of the editor followed by its fallback fonts.</summary>
        internal string FontText { get; private set; } = "";

        internal double FontSizeValue { get; private set; }

        internal bool ShowsLineNumbers { get; private set; }

        /// <summary>The colour theme the preview drew in last.</summary>
        internal EditorColorTheme? Look { get; private set; }

        /// <summary>The text colour of the first piece of the preview that holds <paramref name="fragment"/>, for tests.</summary>
        internal Color? ForegroundOf(string fragment) => RunOf(fragment)?.Foreground is ISolidColorBrush b ? b.Color : null;

        /// <summary>The first piece of the preview that holds <paramref name="fragment"/>, for tests.</summary>
        internal Run? RunOf(string fragment) =>
            _lines.Children.OfType<TextBlock>().SelectMany(t => t.Inlines ?? []).OfType<Run>().FirstOrDefault(r => (r.Text ?? "").Contains(fragment, StringComparison.Ordinal));

        public void Refresh()
        {
            var s = _app.Workbench.Settings;
            FontText = string.Join(", ", new[] { s.GetString("editor.fontFamily"), s.GetString("editor.fallbackFontFamily") }.Where(t => t.Trim().Length > 0));
            FontSizeValue = s.GetInt("editor.fontSize");
            ShowsLineNumbers = s.GetBool("editor.lineNumbers");
            bool currentLine = s.GetBool("editor.highlightCurrentLine");
            bool whitespace = s.GetString("editor.renderWhitespace") == "all";
            var family = new FontFamily(FontText.Length > 0 ? FontText : "monospace");
            string[] code =
            [
                "// " + Loc.Get(K.SettingsPreviewComment),
                "function greet(name) {",
                "\tconst text = `Hello, ${name}!`;",
                "\treturn text.repeat(2);   ",
                "}",
            ];
            var look = Look = _app.EditorThemes.Active(_app.ActualThemeVariant);
            static IBrush Solid(Color c) => new ImmutableSolidColorBrush(c);
            Background = Solid(look[EditorColorTheme.Background]);
            // one colour per character from the grammar, as the editor and the export colour it
            string all = string.Join("\n", code);
            var colors = new (Color? Color, TokenStyle Style)[all.Length];
            foreach (var segment in HighlightExport.Segments(all, LanguageModes.Tokenizer("javascript"), null, look.Palette))
                for (int k = segment.Start; k < segment.Start + segment.Length; k++) colors[k] = (segment.Color, segment.Style);
            int lineStart = 0;
            _lines.Children.Clear();
            _lines.RowDefinitions.Clear();
            for (int i = 0; i < code.Length; i++)
            {
                _lines.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (currentLine && i == 2)
                {
                    var band = new Border { Background = Solid(look[EditorColorTheme.CurrentLine]) };
                    band.Classes.Add("settingpreviewcurrent");
                    Grid.SetRow(band, i);
                    Grid.SetColumnSpan(band, 2);
                    _lines.Children.Add(band);
                }
                var number = new TextBlock
                {
                    Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontFamily = family, FontSize = FontSizeValue, Margin = new Thickness(12, 0, 14, 0),
                    TextAlignment = TextAlignment.Right, MinWidth = 18, IsVisible = ShowsLineNumbers, Foreground = Solid(look[EditorColorTheme.LineNumbers]),
                };
                number.Classes.Add("settingpreviewnumber");
                Grid.SetRow(number, i);
                _lines.Children.Add(number);
                var text = new TextBlock { FontFamily = family, FontSize = FontSizeValue, Margin = new Thickness(ShowsLineNumbers ? 0 : 12, 0, 12, 0), Foreground = Solid(look[EditorColorTheme.Foreground]) };
                text.Classes.Add("settingpreviewtext");
                // the word "text" of the fourth line stands for a selection
                int selected = i == 3 ? code[i].IndexOf("text", StringComparison.Ordinal) : -1;
                var run = new System.Text.StringBuilder();
                (Color? Color, TokenStyle Style, bool Selected) runKey = default;
                void Flush()
                {
                    if (run.Length == 0) return;
                    var piece = new Run(run.ToString()) { Foreground = Solid(runKey.Color ?? look[EditorColorTheme.Foreground]) };
                    if (runKey.Style.HasFlag(TokenStyle.Bold)) piece.FontWeight = FontWeight.Bold;
                    if (runKey.Style.HasFlag(TokenStyle.Italic)) piece.FontStyle = FontStyle.Italic;
                    if (runKey.Selected) piece.Background = Solid(look[EditorColorTheme.Selection]);
                    text.Inlines!.Add(piece);
                    run.Clear();
                }
                for (int c = 0; c < code[i].Length; c++)
                {
                    char ch = code[i][c];
                    var key = (colors[lineStart + c].Color, colors[lineStart + c].Style, selected >= 0 && c >= selected && c < selected + 4);
                    if (key != runKey) Flush();
                    runKey = key;
                    run.Append(ch switch { '\t' => whitespace ? "→   " : "    ", ' ' when whitespace => "·", _ => ch.ToString() });
                }
                Flush();
                lineStart += code[i].Length + 1;
                Grid.SetRow(text, i);
                Grid.SetColumn(text, 1);
                _lines.Children.Add(text);
            }
        }
    }
}

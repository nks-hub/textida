using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The preview at the top of the Appearance and Editor › Display pages: a few lines of code drawn with the font, size and fallback fonts of the editor, the line numbers, the current line and the
/// whitespace marks as the settings have them now, in the colours of the theme in use. It follows every change made on the page.
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
                "\treturn text;   ",
                "}",
            ];
            _lines.Children.Clear();
            _lines.RowDefinitions.Clear();
            for (int i = 0; i < code.Length; i++)
            {
                _lines.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                if (currentLine && i == 2)
                {
                    var band = new Border();
                    band.Classes.Add("settingpreviewcurrent");
                    Grid.SetRow(band, i);
                    Grid.SetColumnSpan(band, 2);
                    _lines.Children.Add(band);
                }
                var number = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontFamily = family, FontSize = FontSizeValue, Margin = new Thickness(12, 0, 14, 0), TextAlignment = TextAlignment.Right, MinWidth = 18, IsVisible = ShowsLineNumbers };
                number.Classes.Add("settingpreviewnumber");
                Grid.SetRow(number, i);
                _lines.Children.Add(number);
                string line = whitespace ? code[i].Replace("\t", "→   ", StringComparison.Ordinal).Replace(' ', '·') : code[i].Replace("\t", "    ", StringComparison.Ordinal);
                var text = new TextBlock { Text = line, FontFamily = family, FontSize = FontSizeValue, Margin = new Thickness(ShowsLineNumbers ? 0 : 12, 0, 12, 0) };
                text.Classes.Add("settingpreviewtext");
                Grid.SetRow(text, i);
                Grid.SetColumn(text, 1);
                _lines.Children.Add(text);
            }
        }
    }
}

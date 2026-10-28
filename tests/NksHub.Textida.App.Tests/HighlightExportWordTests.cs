using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Media;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Tests.RealAgents;
using NksHub.Textida.AvaloniaEditAdapter;
using static NksHub.Textida.App.Tests.HighlightExportTests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.3.2 against the real Word: the HTML and RTF payloads of "copy as highlighted" are given to Word and the colour of every character is read back from the document through COM, then
/// compared with what the editor shows. The clipboard itself is not used (the desktop belongs to the user); the bytes that go on it are written to files and Word takes them from there
/// (<c>Range.InsertFile</c>). That the command puts them on the clipboard is covered by the headless tests of <see cref="HighlightExportWindowTests"/>.
/// </summary>
public class HighlightExportWordTests
{
    private const int WordAutomaticColor = -16777216;

    private static (int R, int G, int B) FromWord(int bgr) => (bgr & 0xFF, (bgr >> 8) & 0xFF, (bgr >> 16) & 0xFF);

    private static (int R, int G, int B) Rgb(Color c) => (c.R, c.G, c.B);

    /// <summary>The part of a CF_HTML payload from <c>StartHTML</c> to <c>EndHTML</c>: the document Word reads when the user pastes.</summary>
    private static byte[] CfHtmlDocument(byte[] payload)
    {
        string header = Encoding.ASCII.GetString(payload, 0, Math.Min(payload.Length, 200));
        int Field(string name) => int.Parse(Regex.Match(header, name + @":(\d{10})").Groups[1].Value, CultureInfo.InvariantCulture);
        return payload[Field("StartHTML")..Field("EndHTML")];
    }

    [Fact]
    [Trait("Category", "RealWord")]
    public void Word_gives_the_text_of_the_html_and_rtf_export_the_colors_of_the_editor()
    {
        Assert.SkipUnless(WordSession.WhyNot() is null, WordSession.WhyNot() ?? "");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "textida-realword-" + Guid.NewGuid().ToString("N"))).FullName;
        var problems = new List<string>();
        try
        {
            using var word = WordSession.Start();
            foreach (bool dark in new[] { false, true })
                foreach (var (text, grammar) in new[] { (CSharp, "csharp"), (Markdown, "markdown") })
                {
                    var theme = Theme(dark);
                    var tokenizer = LanguageModes.Tokenizer(grammar);
                    var expected = EditorColors(text, grammar, theme.Palette);
                    var payloads = new Dictionary<string, byte[]>
                    {
                        ["html"] = CfHtmlDocument(ClipboardHtml.Encode(HighlightExport.Html(text, tokenizer, null, theme))),
                        ["rtf"] = ClipboardRtf.Encode(HighlightExport.Rtf(text, tokenizer, null, theme)),
                    };
                    foreach (var (kind, bytes) in payloads)
                    {
                        string label = $"{kind}/{grammar}/{(dark ? "dark" : "light")}";
                        string file = Path.Combine(folder, label.Replace('/', '-') + (kind == "html" ? ".html" : ".rtf"));
                        // an HTML document in UTF-8 carries a byte order mark so that Word does not read it in the code page of the machine; the clipboard says it with the CF_HTML header
                        File.WriteAllBytes(file, kind == "html" ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes);
                        dynamic document = word.NewDocument();
                        document.Content.InsertFile(file);
                        problems.AddRange(Compare(document, text, expected, theme, label));
                        // the check can fail: against the colours of the other theme the same document must differ
                        var other = Theme(!dark);
                        Assert.NotEmpty(Compare(document, text, EditorColors(text, grammar, other.Palette), other, label + " (control)"));
                        document.Close(0);
                    }
                }
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static List<string> Compare(dynamic document, string text, Color?[] expected, ExportTheme theme, string label)
    {
        var problems = new List<string>();
        // Word and the export disagree about line ends (a paragraph mark, a line break), so only the characters that are not white space are compared, in order
        var want = new List<(char Char, (int R, int G, int B) Color)>();
        for (int i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i])) want.Add((text[i], Rgb(expected[i] ?? theme.Foreground)));

        int count = (int)document.Characters.Count;
        var got = new List<(char Char, int Color, string Font, double Size)>();
        for (int i = 1; i <= count; i++)
        {
            dynamic range = document.Range(i - 1, i);
            string s = range.Text;
            if (s.Length != 1 || char.IsWhiteSpace(s[0])) continue;
            got.Add((s[0], (int)range.Font.Color, (string)range.Font.Name, (double)range.Font.Size));
        }

        if (got.Count != want.Count)
        {
            problems.Add($"{label}: Word has {got.Count} visible characters, the export has {want.Count}; Word starts with \"{new string([.. got.Take(40).Select(g => g.Char)])}\"");
            return problems;
        }
        int wrongColor = 0, wrongChar = 0;
        string? firstWrong = null;
        for (int i = 0; i < want.Count; i++)
        {
            if (got[i].Char != want[i].Char) { wrongChar++; continue; }
            var actual = got[i].Color == WordAutomaticColor ? (R: -1, G: -1, B: -1) : FromWord(got[i].Color);
            if (actual != want[i].Color)
            {
                wrongColor++;
                firstWrong ??= $"'{want[i].Char}' at {i}: Word {got[i].Color} ({actual}), editor {want[i].Color}";
            }
        }
        if (wrongChar > 0) problems.Add($"{label}: {wrongChar} characters differ from the exported text");
        if (wrongColor > 0) problems.Add($"{label}: {wrongColor} of {want.Count} characters have another colour than in the editor; the first is {firstWrong}");
        if (!got.All(g => g.Font.StartsWith("Consolas", StringComparison.OrdinalIgnoreCase))) problems.Add($"{label}: the font is {got.Select(g => g.Font).Distinct().First()}, not Consolas");
        if (!got.All(g => Math.Abs(g.Size - theme.FontSize) < 0.01)) problems.Add($"{label}: the size is {got.Select(g => g.Size).Distinct().First()}, not {theme.FontSize}");
        if (want.Select(w => w.Color).Distinct().Count() < 3) problems.Add($"{label}: the sample uses fewer than three colours, so it proves little");

        // the page colour: an RTF export shades the characters, an HTML export shades the paragraph (the pre element)
        dynamic first = document.Range(0, 1);
        int fontShade = (int)first.Font.Shading.BackgroundPatternColor, paragraphShade = (int)first.ParagraphFormat.Shading.BackgroundPatternColor;
        var page = Rgb(theme.Background);
        if ((fontShade == WordAutomaticColor ? (-1, -1, -1) : FromWord(fontShade)) != page && (paragraphShade == WordAutomaticColor ? (-1, -1, -1) : FromWord(paragraphShade)) != page)
            problems.Add($"{label}: the page colour is {page} in the export, Word shades the characters {fontShade} and the paragraph {paragraphShade}");
        return problems;
    }
}

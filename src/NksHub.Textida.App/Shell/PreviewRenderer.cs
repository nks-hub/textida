using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Renders the text of a plugin preview (P6.2.4) into plain text controls. There is nothing in it that could load or run anything: the result is text blocks and borders, so no script,
/// no network and no local file. Raw HTML stays literal text, an image is shown as a note with its alt text and a link as its text followed by its target, neither is followed or fetched.
/// Markdown covers headings, paragraphs, lists, quotes, fenced code, rules, emphasis, strong text and inline code.
/// </summary>
internal static partial class PreviewRenderer
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*([-*+]|\d{1,9}[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    public static Control Render(string format, string text, FontFamily mono)
    {
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 6) };
        if (format == PreviewFormats.Markdown) Blocks(text.ReplaceLineEndings("\n").Split('\n'), body.Children, mono);
        else body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return body;
    }

    private static bool StartsBlock(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith('>') || Heading().IsMatch(line) || ListItem().IsMatch(line) || Rule().IsMatch(line);

    private static void Blocks(string[] lines, Avalonia.Controls.Controls into, FontFamily mono)
    {
        for (int i = 0; i < lines.Length;)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) { i++; continue; }
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal); i++) code.Append(lines[i]).Append('\n');
                i++;
                var block = new Border { Child = new SelectableTextBlock { Text = code.ToString().TrimEnd('\n'), FontFamily = mono, TextWrapping = TextWrapping.Wrap } };
                block.Classes.Add("previewcode");
                into.Add(block);
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                text.Classes.Add("previewhead" + heading.Groups[1].Length);
                Spans(heading.Groups[2].Value, text.Inlines!, FontWeight.Normal, FontStyle.Normal, mono);
                into.Add(text);
                i++;
            }
            else if (Rule().IsMatch(line))
            {
                var rule = new Border();
                rule.Classes.Add("previewrule");
                into.Add(rule);
                i++;
            }
            else if (line.StartsWith('>'))
            {
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].StartsWith('>'); i++) quote.Add(lines[i][1..].TrimStart());
                var inner = new StackPanel { Spacing = 8 };
                Blocks([.. quote], inner.Children, mono);
                var border = new Border { Child = inner };
                border.Classes.Add("previewquote");
                into.Add(border);
            }
            else if (ListItem().IsMatch(line))
            {
                var list = new StackPanel { Spacing = 2 };
                for (; i < lines.Length && ListItem().Match(lines[i]) is { Success: true } item; i++)
                {
                    var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(Math.Min(item.Groups[0].Value.Length - item.Value.TrimStart().Length, 12) * 8, 0, 0, 0) };
                    var marker = item.Groups[1].Value;
                    text.Inlines!.Add(new Run((char.IsDigit(marker[0]) ? marker : "•") + " "));
                    Spans(item.Groups[2].Value, text.Inlines, FontWeight.Normal, FontStyle.Normal, mono);
                    list.Children.Add(text);
                }
                into.Add(list);
            }
            else
            {
                var paragraph = new List<string>();
                for (; i < lines.Length && lines[i].Trim().Length > 0 && (paragraph.Count == 0 || !StartsBlock(lines[i])); i++) paragraph.Add(lines[i].Trim());
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                Spans(string.Join(' ', paragraph), text.Inlines!, FontWeight.Normal, FontStyle.Normal, mono);
                into.Add(text);
            }
        }
    }

    private static bool Word(char c) => char.IsLetterOrDigit(c);

    /// <summary>An underscore only opens emphasis at the start of a word and closes it at the end of one, so snake_case_names stay as they are.</summary>
    private static bool Emphasis(string s, int open, int close, int width) =>
        s[open] == '*' || ((open == 0 || !Word(s[open - 1])) && (close + width >= s.Length || !Word(s[close + width])));

    private static void Spans(string s, InlineCollection into, FontWeight weight, FontStyle style, FontFamily mono)
    {
        var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length > 0) into.Add(new Run(plain.ToString()) { FontWeight = weight, FontStyle = style });
            plain.Clear();
        }
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length && "\\`*_[]()!#<>".Contains(s[i + 1])) { plain.Append(s[i + 1]); i += 2; }
            else if (c == '`' && s.IndexOf('`', i + 1) is var codeEnd and > 0)
            {
                Flush();
                into.Add(new Run(s[(i + 1)..codeEnd]) { FontFamily = mono, FontWeight = weight, FontStyle = style });
                i = codeEnd + 1;
            }
            else if (c is '*' or '_' && i + 1 < s.Length && s[i + 1] == c && s.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) is var strongEnd and > 0 && Emphasis(s, i, strongEnd, 2))
            {
                Flush();
                Spans(s[(i + 2)..strongEnd], into, FontWeight.Bold, style, mono);
                i = strongEnd + 2;
            }
            else if (c is '*' or '_' && s.IndexOf(c, i + 1) is var emEnd and > 0 && emEnd > i + 1 && Emphasis(s, i, emEnd, 1))
            {
                Flush();
                Spans(s[(i + 1)..emEnd], into, weight, FontStyle.Italic, mono);
                i = emEnd + 1;
            }
            else if (Link(s, i) is var (image, label, target, length) && length > 0)
            {
                Flush();
                into.Add(new Run(image ? Loc.Get(K.PreviewPanelImage, ("alt", label)) : Loc.Get(K.PreviewPanelLink,("text", label), ("url", target))) { FontWeight = weight, FontStyle = style });
                i += length;
            }
            else
            {
                plain.Append(c);
                i++;
            }
        }
        Flush();
    }

    /// <summary>A <c>[label](target)</c> or <c>![label](target)</c> at <paramref name="at"/>; Length 0 when there is none.</summary>
    private static (bool Image, string Label, string Target, int Length) Link(string s, int at)
    {
        int open = s[at] == '!' ? at + 1 : at;
        if (open >= s.Length || s[open] != '[') return default;
        int close = s.IndexOf(']', open + 1);
        if (close < 0 || close + 1 >= s.Length || s[close + 1] != '(') return default;
        int end = s.IndexOf(')', close + 2);
        return end < 0 ? default : (open != at, s[(open + 1)..close], s[(close + 2)..end], end + 1 - at);
    }
}

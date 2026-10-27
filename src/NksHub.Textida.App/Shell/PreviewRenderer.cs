using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Renders the text of a plugin preview (P6.2.4) into Avalonia controls under <see cref="PreviewPolicy"/> (P6.2.7): no web view, so there is no script and no network. Raw HTML stays literal text.
/// An image is drawn only from a <c>data:</c> URI within the size limit, any other becomes a note with its alt text. A link to the web or a mail address is a button that asks the user before it
/// opens; any other link is text followed by its target and is never followed.
/// Markdown covers headings, paragraphs, lists, pipe tables, quotes, fenced code, rules, emphasis, strong text and inline code.
/// </summary>
internal static partial class PreviewRenderer
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*([-*+]|\d{1,9}[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    /// <summary>What a render needs: the font of code and, for allowed links, what a click does (null: links stay text).</summary>
    internal sealed record Ctx(FontFamily Mono, Action<Uri>? Open = null);

    public static Control Render(string format, string text, Ctx ctx)
    {
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 6) };
        if (format == PreviewFormats.Markdown) Blocks(text.ReplaceLineEndings("\n").Split('\n'), body.Children, ctx);
        else body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return body;
    }

    private static bool StartsBlock(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith('>') || Heading().IsMatch(line) || ListItem().IsMatch(line) || Rule().IsMatch(line);

    /// <summary>The headings as a list of buttons, indented by level, so the keyboard and a screen reader reach each one; a click hands the heading to <paramref name="jump"/>.</summary>
    public static Control Outline(IReadOnlyList<MarkdownHeading> headings, Action<MarkdownHeading> jump)
    {
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(12, 6) };
        if (headings.Count == 0) list.Children.Add(new TextBlock { Text = Loc.Get(K.PreviewOutlineEmpty), TextWrapping = TextWrapping.Wrap });
        int top = headings.Count == 0 ? 1 : headings.Min(h => h.Level);
        foreach (var heading in headings)
        {
            var button = new Button { Content = new TextBlock { Text = heading.Title, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness((heading.Level - top) * 16, 0, 0, 0) };
            button.Classes.Add("previewoutline");
            AutomationProperties.SetName(button, heading.Title);
            button.Click += (_, _) => jump(heading);
            list.Children.Add(button);
        }
        return list;
    }

    private static bool TableStart(string[] lines, int i) =>
        i + 1 < lines.Length && lines[i].Contains('|') && MarkdownTools.IsSeparator(lines[i + 1]) && !MarkdownTools.IsSeparator(lines[i]);

    /// <summary>A pipe table as a grid: the first row is the header, a missing cell is empty, the marks of the separator row align the columns. Cells hold inline text only.</summary>
    private static Grid Table(string[] rows, Ctx ctx)
    {
        var cells = rows.Select(MarkdownTools.Cells).ToList();
        int columns = cells.Max(r => r.Count);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < cells.Count; r++)
        {
            if (r == 1) continue;
            int row = r == 0 ? 0 : r - 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < columns; c++)
            {
                string mark = c < cells[1].Count ? cells[1][c] : "";
                var text = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = mark.StartsWith(':') && mark.EndsWith(':') ? TextAlignment.Center : mark.EndsWith(':') ? TextAlignment.Right : TextAlignment.Left,
                    MaxWidth = 400,
                };
                Spans(c < cells[r].Count ? cells[r][c] : "", text.Inlines!, r == 0 ? FontWeight.SemiBold : FontWeight.Normal, FontStyle.Normal, ctx);
                var cell = new Border { Child = text };
                cell.Classes.Add("previewcell");
                if (r == 0) cell.Classes.Add("previewcellhead");
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return grid;
    }

    private static void Blocks(string[] lines, Avalonia.Controls.Controls into, Ctx ctx)
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
                var block = new Border { Child = new SelectableTextBlock { Text = code.ToString().TrimEnd('\n'), FontFamily = ctx.Mono, TextWrapping = TextWrapping.Wrap } };
                block.Classes.Add("previewcode");
                into.Add(block);
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                text.Classes.Add("previewhead" + heading.Groups[1].Length);
                Spans(heading.Groups[2].Value, text.Inlines!, FontWeight.Normal, FontStyle.Normal, ctx);
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
                Blocks([.. quote], inner.Children, ctx);
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
                    Spans(item.Groups[2].Value, text.Inlines, FontWeight.Normal, FontStyle.Normal, ctx);
                    list.Children.Add(text);
                }
                into.Add(list);
            }
            else if (TableStart(lines, i))
            {
                int end = i + 2;
                while (end < lines.Length && lines[end].Contains('|') && lines[end].Trim().Length > 0) end++;
                into.Add(Table(lines[i..end], ctx));
                i = end;
            }
            else
            {
                var paragraph = new List<string>();
                for (; i < lines.Length && lines[i].Trim().Length > 0 && (paragraph.Count == 0 || !(StartsBlock(lines[i]) || TableStart(lines, i))); i++) paragraph.Add(lines[i].Trim());
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                Spans(string.Join(' ', paragraph), text.Inlines!, FontWeight.Normal, FontStyle.Normal, ctx);
                into.Add(text);
            }
        }
    }

    private static bool Word(char c) => char.IsLetterOrDigit(c);

    /// <summary>An underscore only opens emphasis at the start of a word and closes it at the end of one, so snake_case_names stay as they are.</summary>
    private static bool Emphasis(string s, int open, int close, int width) =>
        s[open] == '*' || ((open == 0 || !Word(s[open - 1])) && (close + width >= s.Length || !Word(s[close + width])));

    private static void Spans(string s, InlineCollection into, FontWeight weight, FontStyle style, Ctx ctx)
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
                into.Add(new Run(s[(i + 1)..codeEnd]) { FontFamily = ctx.Mono, FontWeight = weight, FontStyle = style });
                i = codeEnd + 1;
            }
            else if (c is '*' or '_' && i + 1 < s.Length && s[i + 1] == c && s.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal) is var strongEnd and > 0 && Emphasis(s, i, strongEnd, 2))
            {
                Flush();
                Spans(s[(i + 2)..strongEnd], into, FontWeight.Bold, style, ctx);
                i = strongEnd + 2;
            }
            else if (c is '*' or '_' && s.IndexOf(c, i + 1) is var emEnd and > 0 && emEnd > i + 1 && Emphasis(s, i, emEnd, 1))
            {
                Flush();
                Spans(s[(i + 1)..emEnd], into, weight, FontStyle.Italic, ctx);
                i = emEnd + 1;
            }
            else if (Link(s, i) is var (image, label, target, length) && length > 0)
            {
                Flush();
                var text = image ? Loc.Get(K.PreviewPanelImage, ("alt", label)) : Loc.Get(K.PreviewPanelLink, ("text", label), ("url", target));
                if (image && PreviewPolicy.ImageBytes(target) is { } bytes && Picture(bytes, label) is { } picture) into.Add(new InlineUIContainer(picture));
                else if (!image && ctx.Open is { } open && PreviewPolicy.LinkTarget(target) is { } uri) into.Add(new InlineUIContainer(LinkButton(text, uri, open)));
                else into.Add(new Run(text) { FontWeight = weight, FontStyle = style });
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

    /// <summary>A link the policy allows: a button, so the keyboard and a screen reader reach it. It only hands the target to <paramref name="open"/>, which asks the user first.</summary>
    private static Button LinkButton(string text, Uri uri, Action<Uri> open)
    {
        var button = new Button { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } };
        button.Classes.Add("previewlink");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => open(uri);
        return button;
    }

    /// <summary>The image of a <c>data:</c> URI the policy accepted, or null when it does not decode. Decoded at a bounded width so a small file cannot expand to a huge bitmap.</summary>
    private static Image? Picture(byte[] bytes, string alt)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new Image { Source = Bitmap.DecodeToWidth(stream, PreviewPolicy.MaxImageWidth), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, MaxWidth = PreviewPolicy.MaxImageWidth };
            AutomationProperties.SetName(image, alt);
            return image;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
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

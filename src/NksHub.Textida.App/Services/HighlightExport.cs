using System.Globalization;
using System.Text;
using Avalonia.Media;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Services;

/// <summary>The colors of the editor an export is written in: the page and the text, and the palette that gives each token kind its color.</summary>
internal sealed record ExportTheme(Color Background, Color Foreground, SyntaxPalette Palette, string FontFamily = "Consolas, monospace", double FontSize = 10);

/// <summary>
/// HTML and RTF with the colors the editor shows (P9.3.2). The tokens come from the grammar of the document and the colors from the palette of the editor, so an
/// export and the editor cannot disagree; log lines take the color of their level the way the editor does.
/// </summary>
internal static class HighlightExport
{
    /// <summary>A stretch of the text that has one color; null is the plain text color.</summary>
    internal readonly record struct Segment(int Start, int Length, Color? Color);

    /// <summary>
    /// Splits the text into colored stretches, line by line, carrying the grammar state from one line to the next. Equal neighbours are joined.
    /// The whole text is tokenized, so a part taken out of it (<paramref name="range"/>) is colored as the editor colors it.
    /// </summary>
    internal static List<Segment> Segments(string text, Tokenizer? tokenizer, LogLevelRules? levels, SyntaxPalette palette, (int Start, int Length)? range = null, CancellationToken cancel = default)
    {
        var all = Tokenized(text, tokenizer, levels, palette, cancel);
        if (range is not { } r) return all;
        var part = new List<Segment>();
        foreach (var s in all)
        {
            int from = Math.Max(s.Start, r.Start), to = Math.Min(s.Start + s.Length, r.Start + r.Length);
            if (from < to) part.Add(new Segment(from, to - from, s.Color));
        }
        return part;
    }

    private static List<Segment> Tokenized(string text, Tokenizer? tokenizer, LogLevelRules? levels, SyntaxPalette palette, CancellationToken cancel)
    {
        var result = new List<Segment>();
        void Add(int start, int end, Color? color)
        {
            if (end <= start) return;
            if (result.Count > 0 && result[^1] is { } last && last.Color == color && last.Start + last.Length == start) result[^1] = last with { Length = last.Length + end - start };
            else result.Add(new Segment(start, end - start, color));
        }
        Color? Of(string kind) => palette.GetBrush(kind) is ISolidColorBrush solid ? solid.Color : null;

        var state = tokenizer?.InitialState;
        var tokens = new List<Token>();
        for (int pos = 0; pos < text.Length;)
        {
            cancel.ThrowIfCancellationRequested();
            int eol = text.AsSpan(pos).IndexOfAny('\r', '\n');
            int end = eol < 0 ? text.Length : pos + eol;
            int next = end == text.Length ? end : end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            if (levels is not null && levels.Classify(text.Substring(pos, Math.Min(end - pos, LogLevelColorizer.MaxClassifiedChars))) is { } level)
                Add(pos, end, Of(LogLevelKinds.Of(level)));
            else if (tokenizer is not null)
            {
                tokens.Clear();
                state = tokenizer.TokenizeLine(text.Substring(pos, end - pos), state!, tokens);
                int at = 0;
                foreach (var token in tokens)
                {
                    int from = Math.Max(token.Start, at), to = Math.Min(token.End, end - pos);
                    if (from >= to) continue;
                    Add(pos + at, pos + from, null);
                    Add(pos + from, pos + to, Of(token.Kind));
                    at = to;
                }
                Add(pos + at, end, null);
            }
            else Add(pos, end, null);
            Add(end, next, null);
            pos = next;
        }
        return result;
    }

    private static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    private static void AppendHtml(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
    }

    /// <summary>The colored text as one <c>pre</c> element: the fragment for the clipboard, or with <paramref name="title"/> a whole page.</summary>
    public static string Html(string text, Tokenizer? tokenizer, LogLevelRules? levels, ExportTheme theme, string? title = null, (int Start, int Length)? range = null, CancellationToken cancel = default)
    {
        var sb = new StringBuilder(text.Length * 2 + 256);
        if (title is not null)
        {
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>");
            AppendHtml(sb, title);
            sb.Append("</title>\n</head>\n<body>\n");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<pre style=\"background-color:{Hex(theme.Background)};color:{Hex(theme.Foreground)};font-family:{theme.FontFamily};font-size:{theme.FontSize}pt;\">");
        foreach (var segment in Segments(text, tokenizer, levels, theme.Palette, range, cancel))
        {
            var slice = text.AsSpan(segment.Start, segment.Length);
            if (segment.Color is { } color) sb.Append("<span style=\"color:").Append(Hex(color)).Append("\">");
            AppendHtml(sb, slice);
            if (segment.Color is not null) sb.Append("</span>");
        }
        sb.Append("</pre>");
        if (title is not null) sb.Append("\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>The colored text as RTF: a color table (page, text, then the token colors), a fixed-width font, and every character outside ASCII as a <c>\u</c> escape.</summary>
    public static string Rtf(string text, Tokenizer? tokenizer, LogLevelRules? levels, ExportTheme theme, (int Start, int Length)? range = null, CancellationToken cancel = default)
    {
        var segments = Segments(text, tokenizer, levels, theme.Palette, range, cancel);
        var colors = new List<Color> { theme.Background, theme.Foreground };
        foreach (var segment in segments)
            if (segment.Color is { } c && !colors.Contains(c)) colors.Add(c);

        var sb = new StringBuilder(text.Length * 2 + 256);
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern ").Append(theme.FontFamily.Split(',')[0].Trim().Replace(";", "")).Append(@";}}{\colortbl;");
        foreach (var c in colors) sb.Append(CultureInfo.InvariantCulture, $"\\red{c.R}\\green{c.G}\\blue{c.B};");
        sb.Append(CultureInfo.InvariantCulture, $"}}\\f0\\fs{(int)Math.Round(theme.FontSize * 2)}\\chcbpat1\\cf2 ");
        foreach (var segment in segments)
        {
            if (segment.Color is { } color) sb.Append(CultureInfo.InvariantCulture, $"{{\\cf{colors.IndexOf(color) + 1} ");
            AppendRtf(sb, text.AsSpan(segment.Start, segment.Length));
            if (segment.Color is not null) sb.Append('}');
        }
        return sb.Append('}').ToString();
    }

    private static void AppendRtf(StringBuilder sb, ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (c)
            {
                case '\\' or '{' or '}': sb.Append('\\').Append(c); break;
                case '\r': if (i + 1 < text.Length && text[i + 1] == '\n') i++; sb.Append("\\par\r\n"); break;
                case '\n': sb.Append("\\par\r\n"); break;
                case '\t': sb.Append("\\tab "); break;
                case < ' ': break;
                case > '~': sb.Append(CultureInfo.InvariantCulture, $"\\u{(short)c}?"); break;
                default: sb.Append(c); break;
            }
        }
    }
}

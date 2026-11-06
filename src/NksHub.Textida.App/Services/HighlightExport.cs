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
/// HTML, RTF and LaTeX with the colors the editor shows (P9.3.2). The tokens come from the grammar of the document and the colors from the palette of the editor, so an
/// export and the editor cannot disagree; log lines take the color of their level the way the editor does.
/// </summary>
internal static class HighlightExport
{
    /// <summary>A stretch of the text that has one color and one style; a null color is the plain text color.</summary>
    internal readonly record struct Segment(int Start, int Length, Color? Color, TokenStyle Style = TokenStyle.None);

    /// <summary>
    /// Splits the text into colored stretches, line by line, carrying the grammar state from one line to the next. Equal neighbours are joined.
    /// The whole text is tokenized, so a part taken out of it (<paramref name="range"/>) is colored as the editor colors it.
    /// <paramref name="startState"/> is the grammar state at the start of <paramref name="text"/> when that is not the start of the document (null: the initial state).
    /// </summary>
    internal static List<Segment> Segments(string text, Tokenizer? tokenizer, LogLevelRules? levels, SyntaxPalette palette, (int Start, int Length)? range = null, CancellationToken cancel = default, LineState? startState = null)
    {
        var all = Tokenized(text, tokenizer, levels, palette, cancel, startState);
        if (range is not { } r) return all;
        var part = new List<Segment>();
        foreach (var s in all)
        {
            int from = Math.Max(s.Start, r.Start), to = Math.Min(s.Start + s.Length, r.Start + r.Length);
            if (from < to) part.Add(s with { Start = from, Length = to - from });
        }
        return part;
    }

    private static List<Segment> Tokenized(string text, Tokenizer? tokenizer, LogLevelRules? levels, SyntaxPalette palette, CancellationToken cancel, LineState? startState)
    {
        var result = new List<Segment>();
        void Add(int start, int end, Color? color, TokenStyle style = TokenStyle.None)
        {
            if (end <= start) return;
            if (result.Count > 0 && result[^1] is { } last && last.Color == color && last.Style == style && last.Start + last.Length == start) result[^1] = last with { Length = last.Length + end - start };
            else result.Add(new Segment(start, end - start, color, style));
        }
        Color? Of(string kind) => palette.GetBrush(kind) is ISolidColorBrush solid ? solid.Color : null;

        var state = startState ?? tokenizer?.InitialState;
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
                    Add(pos + from, pos + to, Of(token.Kind), palette.GetStyle(token.Kind));
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
    public static string Html(string text, Tokenizer? tokenizer, LogLevelRules? levels, ExportTheme theme, string? title = null, (int Start, int Length)? range = null, CancellationToken cancel = default, LineState? startState = null)
    {
        var sb = new StringBuilder(text.Length * 2 + 256);
        if (title is not null)
        {
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title>");
            AppendHtml(sb, title);
            sb.Append("</title>\n</head>\n<body>\n");
        }
        sb.Append(CultureInfo.InvariantCulture, $"<pre style=\"background-color:{Hex(theme.Background)};color:{Hex(theme.Foreground)};font-family:{theme.FontFamily};font-size:{theme.FontSize}pt;\">");
        foreach (var segment in Segments(text, tokenizer, levels, theme.Palette, range, cancel, startState))
        {
            var slice = text.AsSpan(segment.Start, segment.Length);
            bool span = segment.Color is not null || segment.Style != TokenStyle.None;
            if (span)
            {
                sb.Append("<span style=\"");
                if (segment.Color is { } color) sb.Append("color:").Append(Hex(color)).Append(';');
                if (segment.Style.HasFlag(TokenStyle.Bold)) sb.Append("font-weight:bold;");
                if (segment.Style.HasFlag(TokenStyle.Italic)) sb.Append("font-style:italic;");
                sb.Length--;
                sb.Append("\">");
            }
            AppendHtml(sb, slice);
            if (span) sb.Append("</span>");
        }
        sb.Append("</pre>");
        if (title is not null) sb.Append("\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>The colored text as RTF: a color table (page, text, then the token colors), a fixed-width font, and every character outside ASCII as a <c>\u</c> escape.</summary>
    public static string Rtf(string text, Tokenizer? tokenizer, LogLevelRules? levels, ExportTheme theme, (int Start, int Length)? range = null, CancellationToken cancel = default, LineState? startState = null)
    {
        var segments = Segments(text, tokenizer, levels, theme.Palette, range, cancel, startState);
        var colors = new List<Color> { theme.Background, theme.Foreground };
        foreach (var segment in segments)
            if (segment.Color is { } c && !colors.Contains(c)) colors.Add(c);

        var sb = new StringBuilder(text.Length * 2 + 256);
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fmodern ").Append(theme.FontFamily.Split(',')[0].Trim().Replace(";", "")).Append(@";}}{\colortbl;");
        foreach (var c in colors) sb.Append(CultureInfo.InvariantCulture, $"\\red{c.R}\\green{c.G}\\blue{c.B};");
        sb.Append(CultureInfo.InvariantCulture, $"}}\\f0\\fs{(int)Math.Round(theme.FontSize * 2)}\\chcbpat1\\cf2 ");
        foreach (var segment in segments)
        {
            bool group = segment.Color is not null || segment.Style != TokenStyle.None;
            if (group)
            {
                sb.Append('{');
                if (segment.Color is { } color) sb.Append(CultureInfo.InvariantCulture, $"\\cf{colors.IndexOf(color) + 1}");
                if (segment.Style.HasFlag(TokenStyle.Bold)) sb.Append("\\b");
                if (segment.Style.HasFlag(TokenStyle.Italic)) sb.Append("\\i");
                sb.Append(' ');
            }
            AppendRtf(sb, text.AsSpan(segment.Start, segment.Length));
            if (group) sb.Append('}');
        }
        return sb.Append('}').ToString();
    }

    /// <summary>
    /// The colored text as a LaTeX document that compiles with pdfLaTeX, XeLaTeX or LuaLaTeX: the page and text colors of the theme, and the text in a <c>Verbatim</c> block
    /// (fancyvrb) in which each colored stretch is a <c>\textcolor</c>. A stretch never spans a line, since the block reads line by line. Backslash and braces are written as macros,
    /// every other character as it is (the file is UTF-8). A tab becomes spaces up to the next multiple of four columns; control characters, DEL and the byte order mark are left out.
    /// The preamble picks the engine with iftex: fontspec under XeTeX and LuaTeX (all characters), inputenc utf8 + fontenc T1 under pdfTeX. pdfTeX stops at a character that inputenc
    /// or the T1 fonts do not know, so every character outside <see cref="IsPdfTexSafe"/> is written as <c>\TxU{c}</c>: the character itself under XeTeX and LuaTeX, a bold ? under pdfTeX.
    /// The argument is only passed over, never expanded, so pdfTeX reads it without an error; no \symbol or \char is used, since a glyph the font lacks would be a warning at best.
    /// </summary>
    public static string Tex(string text, Tokenizer? tokenizer, LogLevelRules? levels, ExportTheme theme, (int Start, int Length)? range = null, CancellationToken cancel = default, LineState? startState = null)
    {
        var sb = new StringBuilder(text.Length * 3 + 512);
        sb.Append("\\documentclass{article}\n\\usepackage{iftex}\n\\ifPDFTeX\n\\usepackage[T1]{fontenc}\n\\usepackage[utf8]{inputenc}\n\\else\n\\usepackage{fontspec}\n\\fi\n\\usepackage[margin=1.5cm]{geometry}\n\\usepackage{xcolor}\n\\usepackage{fancyvrb}\n");
        sb.Append(@"\newcommand{\TxU}[1]{\ifPDFTeX\textbf{?}\else #1\fi}").Append('\n');
        sb.Append(@"\newcommand{\TxBs}{\char`\\}").Append('\n').Append(@"\newcommand{\TxOb}{\char`\{}").Append('\n').Append(@"\newcommand{\TxCb}{\char`\}}").Append('\n');
        sb.Append(@"\begin{document}").Append('\n');
        sb.Append(@"\pagecolor[HTML]{").Append(TexHex(theme.Background)).Append(@"}\color[HTML]{").Append(TexHex(theme.Foreground)).Append('}').Append('\n');
        sb.Append(@"\begin{Verbatim}[commandchars=\\\{\},fontsize=\small]").Append('\n');

        var line = new StringBuilder();
        void Flush(Segment segment)
        {
            if (line.Length == 0) return;
            int close = 0;
            if (segment.Color is { } c)
            {
                sb.Append(@"\textcolor[HTML]{").Append(TexHex(c)).Append("}{");
                close++;
            }
            if (segment.Style.HasFlag(TokenStyle.Bold))
            {
                sb.Append(@"\textbf{");
                close++;
            }
            if (segment.Style.HasFlag(TokenStyle.Italic))
            {
                sb.Append(@"\textit{");
                close++;
            }
            sb.Append(line).Append('}', close);
            line.Clear();
        }
        int column = 0; // a tab is written as spaces: obeytabs cannot measure the column inside the argument of \textcolor
        foreach (var segment in Segments(text, tokenizer, levels, theme.Palette, range, cancel, startState))
        {
            var slice = text.AsSpan(segment.Start, segment.Length);
            for (int i = 0; i < slice.Length; i++)
            {
                char c = slice[i];
                switch (c)
                {
                    case '\r' or '\n':
                        if (c == '\r' && i + 1 < slice.Length && slice[i + 1] == '\n') i++;
                        Flush(segment);
                        sb.Append('\n');
                        column = 0;
                        break;
                    case '\t':
                        int width = TabSize - column % TabSize;
                        line.Append(' ', width);
                        column += width;
                        break;
                    case '\\': line.Append(@"\TxBs{}"); column++; break;
                    case '{': line.Append(@"\TxOb{}"); column++; break;
                    case '}': line.Append(@"\TxCb{}"); column++; break;
                    case < ' ' or (>= '\u007F' and <= '\u009F') or '﻿': break; // LaTeX stops at these
                    default:
                        int length = char.IsHighSurrogate(c) && i + 1 < slice.Length && char.IsLowSurrogate(slice[i + 1]) ? 2 : 1;
                        var written = slice.Slice(i, length);
                        if (IsPdfTexSafe(length == 2 ? char.ConvertToUtf32(c, slice[i + 1]) : c)) line.Append(written);
                        else line.Append(@"\TxU{").Append(written).Append('}');
                        i += length - 1;
                        column++;
                        break;
                }
            }
            Flush(segment);
        }
        if (sb[^1] != '\n') sb.Append('\n');

        return sb.Append(@"\end{Verbatim}").Append('\n').Append(@"\end{document}").Append('\n').ToString();
    }

    private const int TabSize = 4;

    /// <summary>True for the characters pdfLaTeX takes with inputenc utf8 and fontenc T1: ASCII, Latin-1, Latin Extended-A, common general punctuation and the euro sign. Conservative; the rest goes through <c>\TxU</c>.</summary>
    internal static bool IsPdfTexSafe(int cp) => cp is (>= ' ' and <= '~') or (>= 0xA0 and <= 0x17F)
        or 0x2013 or 0x2014 or 0x2018 or 0x2019 or 0x201A or 0x201C or 0x201D or 0x201E or 0x2020 or 0x2021 or 0x2022 or 0x2026 or 0x2030 or 0x2039 or 0x203A or 0x20AC;

    private static string TexHex(Color c) => Hex(c)[1..];

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

using System.Globalization;
using System.Text;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Writes pages as a PDF file (PZ.2.2): the way to print where the platform has no print dialog the editor can call. Text is set in Courier, one of the 14 fonts every PDF reader has, so no font is embedded;
/// the colours and the page header are drawn, the characters outside Windows-1252 are looked up in a table of Central European letters and the rest print as a question mark.
/// </summary>
internal static class PdfExport
{
    public const double PageWidth = 595.28, PageHeight = 841.89, Margin = 40, FontSize = 8.5, LineHeight = 10.5, HeaderSize = 9;
    private const double CharWidth = 0.6 * FontSize;

    /// <summary>The columns and rows of a page of this file; the header takes two rows.</summary>
    public static PrintOptions Options(bool lineNumbers) =>
        new((int)((PageWidth - 2 * Margin) / CharWidth), (int)((PageHeight - 2 * Margin) / LineHeight) - 2, lineNumbers);

    private static readonly Dictionary<char, string> GlyphNames = new()
    {
        ['Ă'] = "Abreve", ['ă'] = "abreve", ['Ą'] = "Aogonek", ['ą'] = "aogonek", ['Ć'] = "Cacute", ['ć'] = "cacute", ['Č'] = "Ccaron", ['č'] = "ccaron",
        ['Ď'] = "Dcaron", ['ď'] = "dcaron", ['Đ'] = "Dcroat", ['đ'] = "dcroat", ['Ę'] = "Eogonek", ['ę'] = "eogonek", ['Ě'] = "Ecaron", ['ě'] = "ecaron",
        ['Ğ'] = "Gbreve", ['ğ'] = "gbreve", ['İ'] = "Idotaccent", ['ı'] = "dotlessi", ['Ĺ'] = "Lacute", ['ĺ'] = "lacute", ['Ľ'] = "Lcaron", ['ľ'] = "lcaron",
        ['Ł'] = "Lslash", ['ł'] = "lslash", ['Ń'] = "Nacute", ['ń'] = "nacute", ['Ň'] = "Ncaron", ['ň'] = "ncaron", ['Ő'] = "Ohungarumlaut", ['ő'] = "ohungarumlaut",
        ['Ŕ'] = "Racute", ['ŕ'] = "racute", ['Ř'] = "Rcaron", ['ř'] = "rcaron", ['Ś'] = "Sacute", ['ś'] = "sacute", ['Ş'] = "Scedilla", ['ş'] = "scedilla",
        ['Ţ'] = "Tcommaaccent", ['ţ'] = "tcommaaccent", ['Ť'] = "Tcaron", ['ť'] = "tcaron", ['Ů'] = "Uring", ['ů'] = "uring", ['Ű'] = "Uhungarumlaut", ['ű'] = "uhungarumlaut",
        ['Ź'] = "Zacute", ['ź'] = "zacute", ['Ż'] = "Zdotaccent", ['ż'] = "zdotaccent",
    };

    // the characters Windows-1252 has in 0x80..0x9F
    private static readonly char[] Cp1252High =
        ['€', '\0', '‚', 'ƒ', '„', '…', '†', '‡', 'ˆ', '‰', 'Š', '‹', 'Œ', '\0', 'Ž', '\0',
         '\0', '‘', '’', '“', '”', '•', '–', '—', '˜', '™', 'š', '›', 'œ', '\0', 'ž', 'Ÿ'];

    /// <summary>Which byte a character is written as, with the glyph names the file has to declare for the bytes it borrowed.</summary>
    private sealed class Encoder
    {
        private readonly Dictionary<char, byte> _map = [];
        private readonly Dictionary<byte, string> _differences = [];
        private readonly Stack<byte> _free = new();

        public Encoder(IEnumerable<char> used)
        {
            var distinct = used.Distinct().ToList();
            var taken = new HashSet<byte>();
            foreach (char c in distinct)
            {
                if (c < 0x80 || c is >= ' ' and <= 'ÿ') _map[c] = (byte)c;
                else if (Array.IndexOf(Cp1252High, c) is var at and >= 0) { _map[c] = (byte)(0x80 + at); taken.Add((byte)(0x80 + at)); }
            }
            // codes of 0x80..0x9F that this text does not use are lent to the other letters
            for (int code = 0x9F; code >= 0x80; code--)
                if (!taken.Contains((byte)code)) _free.Push((byte)code);
            foreach (char c in distinct)
                if (!_map.ContainsKey(c) && GlyphNames.TryGetValue(c, out string? name) && _free.Count > 0)
                {
                    byte code = _free.Pop();
                    _map[c] = code;
                    _differences[code] = name;
                }
        }

        public byte Of(char c) => _map.TryGetValue(c, out byte b) ? b : (byte)'?';
        public IReadOnlyDictionary<byte, string> Differences => _differences;
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void Escaped(StringBuilder sb, Encoder encoder, string text)
    {
        sb.Append('(');
        foreach (char c in text)
        {
            byte b = encoder.Of(c);
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b < 0x20) sb.Append(' ');
            else sb.Append((char)b); // the file is written as Latin-1, so a byte and a char are the same
        }
        sb.Append(')');
    }

    private static string Colour(int rgb) => $"{Number(((rgb >> 16) & 255) / 255.0)} {Number(((rgb >> 8) & 255) / 255.0)} {Number((rgb & 255) / 255.0)} rg";

    /// <summary>The PDF of the pages, with the title and the page number in the header of each.</summary>
    public static byte[] Write(IReadOnlyList<PrintPage> pages, string title)
    {
        var encoder = new Encoder("0123456789 /".Concat(title).Concat(pages.SelectMany(p => p.Lines).SelectMany(l => l.Runs).SelectMany(r => r.Text)));
        var objects = new List<string>();
        int fontObject = 3, firstPage = 4;
        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add($"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + 2 * i} 0 R"))}] /Count {pages.Count} >>");
        var font = new StringBuilder("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding << /Type /Encoding /BaseEncoding /WinAnsiEncoding");
        if (encoder.Differences.Count > 0)
        {
            font.Append(" /Differences [");
            foreach (var (code, name) in encoder.Differences.OrderBy(d => d.Key)) font.Append(code).Append(" /").Append(name).Append(' ');
            font.Append(']');
        }
        font.Append(" >> >>");
        objects.Add(font.ToString());
        var bodies = new List<string>();
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var s = new StringBuilder();
            s.Append("BT\n/F1 ").Append(Number(HeaderSize)).Append(" Tf\n0 0 0 rg\n1 0 0 1 ").Append(Number(Margin)).Append(' ').Append(Number(PageHeight - Margin - HeaderSize)).Append(" Tm\n");
            Escaped(s, encoder, title);
            s.Append(" Tj\n");
            string right = $"{page.Number} / {pages.Count}";
            s.Append("1 0 0 1 ").Append(Number(PageWidth - Margin - right.Length * 0.6 * HeaderSize)).Append(' ').Append(Number(PageHeight - Margin - HeaderSize)).Append(" Tm\n");
            Escaped(s, encoder, right);
            s.Append(" Tj\nET\n0.6 G 0.5 w ").Append(Number(Margin)).Append(' ').Append(Number(PageHeight - Margin - HeaderSize - 4)).Append(" m ")
                .Append(Number(PageWidth - Margin)).Append(' ').Append(Number(PageHeight - Margin - HeaderSize - 4)).Append(" l S\nBT\n/F1 ").Append(Number(FontSize)).Append(" Tf\n");
            double y = PageHeight - Margin - 2 * LineHeight - FontSize;
            foreach (var line in page.Lines)
            {
                s.Append("1 0 0 1 ").Append(Number(Margin)).Append(' ').Append(Number(y)).Append(" Tm\n");
                foreach (var run in line.Runs)
                {
                    s.Append(Colour(run.Rgb)).Append(' ');
                    Escaped(s, encoder, run.Text);
                    s.Append(" Tj\n");
                }
                y -= LineHeight;
            }
            s.Append("ET\n");
            bodies.Add(s.ToString());
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(PageWidth)} {Number(PageHeight)}] /Resources << /Font << /F1 {fontObject} 0 R >> >> /Contents {firstPage + 2 * i + 1} 0 R >>");
            objects.Add($"<< /Length {s.Length} >>\nstream\n{s}endstream");
        }

        var latin1 = Encoding.Latin1;
        using var output = new MemoryStream();
        void Write(string text) => output.Write(latin1.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        long xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets) Write($"{offset:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}

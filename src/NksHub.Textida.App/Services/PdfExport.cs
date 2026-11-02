using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;
using SkiaSharp;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Writes pages as a PDF file (PZ.2.2): the way to print where the platform has no print dialog the editor can call. Skia's PDF backend draws the text in a monospaced system font
/// with a ToUnicode map, so any character prints and can be copied back out; a character the main font lacks comes from the font the system offers for it.
/// Each run starts at its column of the fixed grid, the same way the Windows printer places them.
/// </summary>
internal static partial class PdfExport
{
    public const float PageWidth = 595.28f, PageHeight = 841.89f, Margin = 40, FontSize = 8.5f, LineHeight = 10.5f, HeaderSize = 9;

    private static readonly string[] MonospaceFamilies = ["Consolas", "Cascadia Mono", "Menlo", "SF Mono", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono"];

    private static readonly Lazy<SKTypeface> MainTypeface = new(() =>
    {
        var manager = SKFontManager.Default;
        foreach (string family in MonospaceFamilies)
            if (manager.MatchFamily(family) is { IsFixedPitch: true } typeface) return typeface;
        return manager.MatchFamily("monospace") ?? SKTypeface.Default;
    });

    private static float CharWidth
    {
        get
        {
            using var font = new SKFont(MainTypeface.Value, FontSize);
            return font.MeasureText("M");
        }
    }

    /// <summary>The columns and rows of a page of this file; the header takes two rows.</summary>
    public static PrintOptions Options(bool lineNumbers) =>
        new((int)((PageWidth - 2 * Margin) / CharWidth), (int)((PageHeight - 2 * Margin) / LineHeight) - 2, lineNumbers);

    /// <summary>The PDF of the pages, with the title and the page number in the header of each.</summary>
    public static byte[] Write(IReadOnlyList<PrintPage> pages, string title)
    {
        var numbers = pages.Select(p => $"{p.Number} / {pages.Count}").ToList();
        using var fonts = new Fonts(MainTypeface.Value, numbers.Append(title).Concat(pages.SelectMany(p => p.Lines).SelectMany(l => l.Runs).Select(r => r.Text)));
        using var stream = new SKDynamicMemoryWStream();
        using (var document = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata { Title = title, Creator = "Textida", Producer = "Textida" })
            ?? throw new InvalidOperationException("SkiaSharp has no PDF backend on this platform."))
        using (var paint = new SKPaint { IsAntialias = true })
        using (var rule = new SKPaint { Color = new SKColor(153, 153, 153), StrokeWidth = 0.5f, Style = SKPaintStyle.Stroke })
        {
            float cell = CharWidth;
            for (int i = 0; i < pages.Count; i++)
            {
                var canvas = document.BeginPage(PageWidth, PageHeight);
                paint.Color = SKColors.Black;
                float headerBaseline = Margin + HeaderSize;
                fonts.Draw(canvas, title, Margin, headerBaseline, HeaderSize, paint);
                fonts.Draw(canvas, numbers[i], PageWidth - Margin - fonts.Measure(numbers[i], HeaderSize), headerBaseline, HeaderSize, paint);
                canvas.DrawLine(Margin, headerBaseline + 4, PageWidth - Margin, headerBaseline + 4, rule);

                float y = Margin + 2 * LineHeight + FontSize;
                foreach (var line in pages[i].Lines)
                {
                    int column = 0;
                    foreach (var run in line.Runs)
                    {
                        paint.Color = new SKColor(0xFF000000u | (uint)(run.Rgb & 0xFFFFFF));
                        fonts.Draw(canvas, run.Text, Margin + column * cell, y, FontSize, paint);
                        column += run.Text.Length;
                    }
                    y += LineHeight;
                }
                document.EndPage();
            }
            document.Close();
        }
        using var data = stream.DetachAsData();
        return data.ToArray();
    }

    /// <summary>
    /// The fonts of one file: for each character the main font or the system's fallback for it, each cut down to the characters the file uses.
    /// The SkiaSharp build embeds whole fonts (it has no subsetter), which is megabytes for a CJK font, so HarfBuzz subsets them first.
    /// </summary>
    private sealed class Fonts : IDisposable
    {
        private readonly SKTypeface _main;
        private readonly Dictionary<int, SKTypeface> _fallback = [];
        private readonly Dictionary<SKTypeface, SKTypeface> _subsets = [];

        public Fonts(SKTypeface main, IEnumerable<string> texts)
        {
            _main = main;
            var used = new Dictionary<SKTypeface, HashSet<int>>();
            foreach (string text in texts)
                foreach (var rune in text.EnumerateRunes())
                {
                    var typeface = TypefaceFor(rune.Value);
                    if (!used.TryGetValue(typeface, out var set)) used[typeface] = set = [];
                    set.Add(rune.Value);
                }
            foreach (var (typeface, codePoints) in used)
                if (Subset(typeface, codePoints) is { } subset) _subsets[typeface] = subset;
        }

        /// <summary>The font that draws the character: the main one when it has the glyph, otherwise the one the system matches for it (the main one again when none does).</summary>
        private SKTypeface TypefaceFor(int codePoint)
        {
            if (codePoint < 0x80 || _main.ContainsGlyph(codePoint)) return _main;
            if (!_fallback.TryGetValue(codePoint, out var typeface))
                _fallback[codePoint] = typeface = SKFontManager.Default.MatchCharacter(_main.FamilyName, SKFontStyle.Normal, null, codePoint) ?? _main;
            return typeface;
        }

        /// <summary>The pieces of the text that one font draws, in order.</summary>
        private IEnumerable<(string Text, SKTypeface Typeface)> Pieces(string text)
        {
            int start = 0;
            SKTypeface? current = null;
            for (int i = 0; i < text.Length;)
            {
                Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int length); // a lone surrogate comes back as U+FFFD
                var typeface = TypefaceFor(rune.Value);
                if (current is not null && typeface != current)
                {
                    yield return (text[start..i], _subsets.GetValueOrDefault(current, current));
                    start = i;
                }
                current = typeface;
                i += length;
            }
            if (current is not null) yield return (text[start..], _subsets.GetValueOrDefault(current, current));
        }

        public float Measure(string text, float size)
        {
            float width = 0;
            foreach (var (piece, typeface) in Pieces(text))
            {
                using var font = new SKFont(typeface, size);
                width += font.MeasureText(piece);
            }
            return width;
        }

        public void Draw(SKCanvas canvas, string text, float x, float baseline, float size, SKPaint paint)
        {
            foreach (var (piece, typeface) in Pieces(text))
            {
                using var font = new SKFont(typeface, size);
                canvas.DrawText(piece, x, baseline, font, paint);
                x += font.MeasureText(piece);
            }
        }

        public void Dispose()
        {
            foreach (var typeface in _subsets.Values) typeface.Dispose();
            foreach (var typeface in _fallback.Values.Distinct())
                if (typeface != _main) typeface.Dispose();
        }
    }

    /// <summary>
    /// The font cut down to the characters, or null to embed it whole: when HarfBuzz cannot read or cut it, or the font forbids subsetting (OS/2 fsType bit 8).
    /// A font that forbids embedding at all is left to Skia, which then draws its glyphs as outlines.
    /// </summary>
    private static SKTypeface? Subset(SKTypeface typeface, HashSet<int> codePoints)
    {
        if (typeface.TryGetTableData(0x4F532F32 /* OS/2 */, out byte[] os2) && os2.Length >= 10 && (BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(8)) & 0x0100) != 0) return null;
        using var asset = typeface.OpenStream(out int index);
        if (asset is null) return null;
        using var data = SKData.Create(asset);
        if (data is null) return null;
        try
        {
            using var blob = new Blob(data.Data, (int)data.Size, MemoryMode.ReadOnly);
            using var face = new Face(blob, index);
            nint input = HarfBuzzSubset.hb_subset_input_create_or_fail();
            if (input == 0) return null;
            nint cut;
            try
            {
                nint set = HarfBuzzSubset.hb_subset_input_unicode_set(input);
                foreach (int codePoint in codePoints) HarfBuzzSubset.hb_set_add(set, (uint)codePoint);
                cut = HarfBuzzSubset.hb_subset_or_fail(face.Handle, input);
            }
            finally { HarfBuzzSubset.hb_subset_input_destroy(input); }
            if (cut == 0) return null;
            try
            {
                nint result = HarfBuzzSubset.hb_face_reference_blob(cut);
                try
                {
                    nint bytes = HarfBuzzSubset.hb_blob_get_data(result, out uint length);
                    if (bytes == 0 || length == 0) return null;
                    using var copy = SKData.CreateCopy(bytes, length);
                    return SKTypeface.FromData(copy);
                }
                finally { HarfBuzzSubset.hb_blob_destroy(result); }
            }
            finally { HarfBuzzSubset.hb_face_destroy(cut); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The subsetter of the HarfBuzz library Avalonia ships; HarfBuzzSharp has no managed API for it.</summary>
    private static partial class HarfBuzzSubset
    {
        private const string Library = "libHarfBuzzSharp";

        [LibraryImport(Library)] internal static partial nint hb_subset_input_create_or_fail();
        [LibraryImport(Library)] internal static partial void hb_subset_input_destroy(nint input);
        [LibraryImport(Library)] internal static partial nint hb_subset_input_unicode_set(nint input);
        [LibraryImport(Library)] internal static partial void hb_set_add(nint set, uint codePoint);
        [LibraryImport(Library)] internal static partial nint hb_subset_or_fail(nint face, nint input);
        [LibraryImport(Library)] internal static partial nint hb_face_reference_blob(nint face);
        [LibraryImport(Library)] internal static partial void hb_face_destroy(nint face);
        [LibraryImport(Library)] internal static partial nint hb_blob_get_data(nint blob, out uint length);
        [LibraryImport(Library)] internal static partial void hb_blob_destroy(nint blob);
    }
}

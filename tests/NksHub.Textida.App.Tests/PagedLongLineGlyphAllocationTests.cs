using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using NksHub.Textida.App.Services;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

public sealed partial class PagedLongLineFormattingTests
{
    private const int GlyphPrefixLength = 16 * 1024;

    [AvaloniaFact]
    public void Sixteen_partitioned_recolors_and_rasterizations_allocate_at_most_250_kb_beyond_output_pixels()
    {
        const int repaints = 16, pixelBytes = 1152 * 128 * 4;
        const long allocationLimit = pixelBytes + 250_000;
        bool tracing = TypingTrace.Enabled;
        try
        {
            TypingTrace.Enabled = false;
            using var host = new Host(new string('x', 2 << 20));
            NormalGlyphColors(host);
            host.Paged.SelectBytes(3, 9);
            host.Tokens(GlyphTokens(64));
            host.Draw();
            Assert.Equal(GlyphPrefixLength, Assert.IsType<string>(host.Paged.DisplayOf(0)).Length);
            var palette = EditorLook.Of(host.Paged).Palette;
            Assert.Equal(TokenStyle.None, palette.GetStyle("keyword"));
            Assert.Equal(TokenStyle.None, palette.GetStyle("number"));
            object geometry = Assert.IsAssignableFrom<object>(host.Geometry());
            object entry = Assert.IsAssignableFrom<object>(host.Entry());
            byte[] previous = TextPixels(EntryProperty<DrawingGroup>(entry, "Drawing"));
            int pinkPixels = Count(previous, Color.Parse(SyntaxPink)), bluePixels = Count(previous, Color.Parse(SyntaxBlue));
            string initial = $"initial pinkPixels={pinkPixels} bluePixels={bluePixels} expected={SyntaxPink}/{SyntaxBlue} palette={(palette.GetBrush("keyword") as ISolidColorBrush)?.Color}/{(palette.GetBrush("number") as ISolidColorBrush)?.Color}";
            output.WriteLine(initial);
            WriteGlyphDrawings(EntryProperty<DrawingGroup>(entry, "Drawing"));
            Assert.True(pinkPixels > 10, initial);
            Assert.True(bluePixels > 10, initial);
            var selection = host.Paged.SelectionRange;
            long caret = host.Paged.CaretOffset, revision = host.Tab.Doc.Doc.Revision;
            long largestAllocation = 0;

            for (int repaint = 0; repaint < repaints; repaint++)
            {
                Token[] tokens = GlyphTokens(64, repaint % 2 == 0);
                long before = GC.GetAllocatedBytesForCurrentThread();
                host.Tokens(tokens);
                host.Draw();
                object next = host.Entry()!;
                byte[] pixels = TextPixels(EntryProperty<DrawingGroup>(next, "Drawing"));
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                largestAllocation = Math.Max(largestAllocation, allocated);
                output.WriteLine($"repaint={repaint + 1}/{repaints} allocated={allocated} B pixels={pixels.Length} B overhead={allocated - pixelBytes} B limit={allocationLimit} B");

                Assert.Equal(pixelBytes, pixels.Length);
                Assert.True(allocated >= pixelBytes);
                Assert.NotSame(entry, next);
                Assert.Same(geometry, host.Geometry());
                Assert.True(Count(pixels, Color.Parse(SyntaxPink)) > 10);
                Assert.True(Count(pixels, Color.Parse(SyntaxBlue)) > 10);
                Assert.False(previous.AsSpan().SequenceEqual(pixels));
                Assert.Equal(selection, host.Paged.SelectionRange);
                Assert.Equal("xxxxxx", host.Paged.SelectedText());
                Assert.Equal(caret, host.Paged.CaretOffset);
                Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
                previous = pixels;
                entry = next;
            }

            Assert.True(largestAllocation <= allocationLimit,
                $"A rasterized recolor allocated {largestAllocation} B; the fixed limit is {allocationLimit} B including {pixelBytes} output bytes.");
        }
        finally { TypingTrace.Enabled = tracing; }
    }

    [AvaloniaFact]
    public void Changing_unicode_partitions_and_font_style_returns_to_the_original_raster_and_selection()
    {
        const string prefix = "office ffi אבגדה 👩‍💻 e\u0301\t";
        using var host = new Host(prefix + new string('x', 2 << 20), width: 1200);
        host.App.Workbench.Set("editor.fontFamily", JsonValue.Create("Arial"));
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(24));
        NormalGlyphColors(host);
        host.Paged.SelectBytes(0, Encoding.UTF8.GetByteCount(prefix));
        host.Tokens(GlyphTokens(2));
        host.Draw();
        object geometry = Assert.IsAssignableFrom<object>(host.Geometry());
        byte[] original = TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"));
        var caret = SyntaxCaret(host).CursorRectangle;
        long revision = host.Tab.Doc.Doc.Revision;

        (int Boundary, string Name)[] boundaries = [(9, "Latin9"), (prefix.IndexOf("אבגדה", StringComparison.Ordinal) + 1, "RTL"),
            (prefix.IndexOf("👩‍💻", StringComparison.Ordinal) + 2, "ZWJ")];
        foreach (var (boundary, name) in boundaries)
        {
            host.Tokens(GlyphTokens(boundary));
            host.Draw();
            Assert.Same(geometry, host.Geometry());
            Assert.False(original.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
            output.WriteLine($"unicode-oracle name={name} boundary={boundary} style=normal");
            // A foreground split inside ZWJ can change FormattedText fallback; check whole glyphs and colors from a fresh layout separately.
            if (name == "ZWJ") AssertUnsplitGlyphAndFreshColorRasters(host);
            else AssertLegacyTextPixels(host);

            host.Tokens(GlyphTokens(2));
            host.Draw();
            Assert.Same(geometry, host.Geometry());
            Assert.True(original.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
        }
        host.App.Workbench.Set(EditorThemes.ColorsSetting,
            $"keyword={SyntaxPink} bold italic;number={SyntaxBlue} normal");
        host.Draw();
        Assert.NotSame(geometry, host.Geometry());
        Assert.False(original.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
        output.WriteLine("unicode-oracle name=original2 boundary=2 style=bold-italic");
        AssertLegacyTextPixels(host);

        object styled = Assert.IsAssignableFrom<object>(host.Geometry());
        NormalGlyphColors(host);
        host.Draw();
        Assert.NotSame(styled, host.Geometry());
        Assert.True(original.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
        Assert.Equal(prefix, host.Paged.SelectedText());
        Assert.Equal(Encoding.UTF8.GetByteCount(prefix), host.Paged.CaretOffset);
        Assert.Equal(caret, SyntaxCaret(host).CursorRectangle);
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
        output.WriteLine("unicode-oracle name=original2 boundary=2 style=normal-restored");
        AssertLegacyTextPixels(host);
    }

    [AvaloniaFact]
    public void A_failed_recolor_keeps_the_previous_drawing_rasterizable_and_allows_recovery()
    {
        using var host = new Host(new string('x', 2 << 20));
        NormalGlyphColors(host);
        host.Paged.SelectBytes(3, 9);
        host.Tokens(GlyphTokens(32));
        host.Draw();
        object geometry = Assert.IsAssignableFrom<object>(host.Geometry());
        object entry = Assert.IsAssignableFrom<object>(host.Entry());
        DrawingGroup previousDrawing = EntryProperty<DrawingGroup>(entry, "Drawing");
        byte[] before = TextPixels(previousDrawing);
        var runs = RasterizedGlyphRuns(previousDrawing).ToArray();
        Assert.True(runs.Length >= 2);
        var backendField = typeof(GlyphRun).GetField("_platformImpl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] backends = runs.Select(run => Assert.IsAssignableFrom<object>(backendField.GetValue(run))).ToArray();
        var key = EntryProperty<object>(entry, "Key");
        object view = EntryProperty<object>(key, "View");
        var foreground = new ImmutableSolidColorBrush(EntryProperty<Color>(key, "Foreground"), EntryProperty<double>(key, "Opacity"));
        var palette = EditorLook.Of(SyntaxCanvas(host)).Palette;
        var source = Assert.IsType<DrawingGroup>(geometry.GetType()
            .GetField("drawing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(geometry));
        var unsupported = new GeometryDrawing { Brush = Brushes.Black, Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1)) };
        Token[] changed = [new(0, 32, "keyword"), new(32, 32, "number"), new(64, GlyphPrefixLength - 64, "keyword")];
        long revision = host.Tab.Doc.Doc.Revision;
        var selection = host.Paged.SelectionRange;
        long caret = host.Paged.CaretOffset;

        // A trailing unsupported drawing fails after both reused and newly partitioned glyphs were visited.
        source.Children.Add(unsupported);
        try
        {
            var failure = Assert.Throws<TargetInvocationException>(() => geometry.GetType()
                .GetMethod("Paint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(geometry, [changed, view, palette, foreground]));
            Assert.Equal("A long-line text layout produced an unexpected drawing.",
                Assert.IsType<InvalidOperationException>(failure.InnerException).Message);
        }
        finally { source.Children.Remove(unsupported); }

        Assert.Same(entry, host.Entry());
        // Dispose clears this backend but permits a later Draw to recreate it with identical pixels.
        for (int index = 0; index < runs.Length; index++) Assert.Same(backends[index], backendField.GetValue(runs[index]));
        Assert.True(before.AsSpan().SequenceEqual(TextPixels(previousDrawing)));
        host.Tokens(changed);
        host.Draw();
        Assert.Same(geometry, host.Geometry());
        Assert.False(before.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
        Assert.Equal(selection, host.Paged.SelectionRange);
        Assert.Equal(caret, host.Paged.CaretOffset);
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
        AssertLegacyTextPixels(host);
    }

    private static void NormalGlyphColors(Host host) => host.App.Workbench.Set(EditorThemes.ColorsSetting,
        $"keyword={SyntaxPink} normal;number={SyntaxBlue} normal");

    private static Token[] GlyphTokens(int boundary, bool swapped = false) =>
        [new(0, boundary, swapped ? "number" : "keyword"),
            new(boundary, GlyphPrefixLength - boundary, swapped ? "keyword" : "number")];

    private static IEnumerable<GlyphRun> RasterizedGlyphRuns(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing { GlyphRun: { } run }) yield return run;
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var glyph in RasterizedGlyphRuns(child)) yield return glyph;
    }

    private void WriteGlyphDrawings(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing { GlyphRun: { } run } glyph)
            output.WriteLine($"glyph brush={(glyph.Foreground as ISolidColorBrush)?.Color} chars={run.Characters.Length} baseline={run.BaselineOrigin} bounds={run.Bounds}");
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children) WriteGlyphDrawings(child);
    }

    private static void AssertUnsplitGlyphAndFreshColorRasters(Host host)
    {
        object entry = Assert.IsAssignableFrom<object>(host.Entry());
        object key = EntryProperty<object>(entry, "Key");
        object view = EntryProperty<object>(key, "View");
        string shown = EntryProperty<string>(view, "Display");
        var drawing = EntryProperty<DrawingGroup>(entry, "Drawing");
        var palette = EntryProperty<SyntaxPalette>(key, "Palette");
        byte[] colored = TextPixels(drawing);
        Assert.True(Count(colored, Color.Parse(SyntaxPink)) > 10);
        Assert.True(Count(colored, Color.Parse(SyntaxBlue)) > 10);
        Assert.Equal(Color.Parse(SyntaxPink), (palette.GetBrush("keyword") as ISolidColorBrush)?.Color);
        Assert.Equal(Color.Parse(SyntaxBlue), (palette.GetBrush("number") as ISolidColorBrush)?.Color);

        object geometry = Assert.IsAssignableFrom<object>(host.Geometry());
        object geometryKey = EntryProperty<object>(geometry, "Key");
        var spans = (Array)EntryProperty<object>(geometry, "Spans");
        Assert.Empty(spans);
        Assert.Equal(shown, EntryProperty<string>(geometryKey, "Text"));
        var text = new FormattedText(shown, EntryProperty<CultureInfo>(key, "Culture"), FlowDirection.LeftToRight,
            new Typeface(EntryProperty<string>(key, "Family")), EntryProperty<double>(key, "FontSize"), Brushes.Black);
        byte[] whole = TextPixels(context => context.DrawText(text, default));
        byte[] uniform = TextPixels(context => DrawUniformGlyphs(drawing, context));
        AssertGlyphRaster(whole, uniform, "The whole-text Unicode glyph geometry");

        using var fresh = (IDisposable)geometry.GetType()
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single()
            .Invoke([geometryKey, shown, spans]);
        var foreground = new ImmutableSolidColorBrush(EntryProperty<Color>(key, "Foreground"), EntryProperty<double>(key, "Opacity"));
        var first = Assert.IsType<DrawingGroup>(fresh.GetType()
            .GetMethod("Paint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(fresh, [EntryProperty<Token[]>(key, "Tokens"), view, palette, foreground]));
        AssertGlyphRaster(TextPixels(first), colored, "The first fresh Unicode color drawing");
    }

    private static void DrawUniformGlyphs(Drawing drawing, DrawingContext context)
    {
        if (drawing is DrawingGroup group)
        {
            using (context.PushTransform(group.Transform?.Value ?? Matrix.Identity))
            using (context.PushOpacity(group.Opacity))
            using (group.ClipGeometry is { } clip ? context.PushGeometryClip(clip) : default)
                foreach (var child in group.Children) DrawUniformGlyphs(child, context);
        }
        else if (drawing is GlyphRunDrawing { GlyphRun: { } run }) context.DrawGlyphRun(Brushes.Black, run);
        else throw new InvalidOperationException("The Unicode oracle encountered a non-glyph drawing.");
    }

    private static void AssertGlyphRaster(byte[] expected, byte[] actual, string description)
    {
        Assert.Equal(expected.Length, actual.Length);
        int differences = 0;
        for (int at = 0; at < actual.Length; at += 4)
            if (!actual.AsSpan(at, 4).SequenceEqual(expected.AsSpan(at, 4))) differences++;
        Assert.True(differences == 0, $"{description} differs at {differences} pixels.");
    }
}

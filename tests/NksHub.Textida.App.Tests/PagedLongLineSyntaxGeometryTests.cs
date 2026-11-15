using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

public sealed partial class PagedLongLineFormattingTests
{
    private const string SyntaxPink = "#D412A7", SyntaxBlue = "#1279D4";

    [AvaloniaTheory]
    [InlineData(900)]
    [InlineData(1800)]
    public async Task Fresh_syntax_viewports_repaint_without_reshaping_the_same_long_prefix(int width)
    {
        const string unit = "\"officeffi😀אבגě\",123,true,\t";
        string text = "[" + string.Concat(Enumerable.Repeat(unit, 512)) + "\"" + new string('x', 2 << 20) + "\"]";
        using var host = new Host(text, "long.json", width);
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(22));
        host.App.Workbench.Set(EditorThemes.ColorsSetting, $"string={SyntaxPink} normal;number={SyntaxBlue} normal;keyword=normal");
        Assert.True(host.Paged.Syntax.Active);
        long revision = host.Tab.Doc.Doc.Revision;
        int beforeRuns = host.Paged.Syntax.ViewportRuns;
        host.Paged.Syntax.SetLimits(32, 512);
        await FreshSyntax(host, beforeRuns, request => request.Columns > 0);
        var beforeRequest = host.Paged.Syntax.Requested;
        Token[] beforeTokens = host.Paged.TokensOf(0)!.ToArray();
        object? geometry = host.Geometry(), entry = host.Entry();
        Assert.NotNull(geometry);
        Assert.NotNull(entry);
        Assert.NotEmpty(beforeTokens);

        beforeRuns = host.Paged.Syntax.ViewportRuns;
        host.Paged.ScrollToColumn(640);
        await FreshSyntax(host, beforeRuns, request => request.Columns > beforeRequest.Columns);

        Assert.NotEqual(beforeRequest, host.Paged.Syntax.Requested);
        Assert.False(beforeTokens.SequenceEqual(host.Paged.TokensOf(0)!));
        Assert.Same(geometry, host.Geometry());
        Assert.NotSame(entry, host.Entry());
        byte[] pixels = host.Frame();
        Assert.True(Count(pixels, Color.Parse(SyntaxPink)) > 10);
        Assert.True(Count(pixels, Color.Parse(SyntaxBlue)) > 10);
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
    }

    [AvaloniaTheory]
    [InlineData("office ffi affinity", 2, 1)]
    [InlineData("אבגדה مر\u200Cحبا", 1, 4)]
    [InlineData("👩‍💻😀e\u0301\tffi", 2, 1)]
    [InlineData("ab\0cdffi", 1, 4)]
    public void Recoloring_unicode_clusters_keeps_geometry_selection_and_legacy_pixels(string sample, int start, int length)
    {
        string prefix = new string('\u2588', 24) + sample;
        using var host = new Host(prefix + new string('x', 2 << 20), width: 1200);
        host.App.Workbench.Set("editor.fontFamily", JsonValue.Create("Arial"));
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(24));
        host.App.Workbench.Set(EditorThemes.ColorsSetting, $"keyword={SyntaxPink} normal;number={SyntaxBlue} normal");
        Token[] original = [new(0, 8, "keyword"), new(24 + start, length, "number")];
        host.Tokens(original);
        host.Paged.SelectBytes(0, Encoding.UTF8.GetByteCount(prefix));
        host.Draw();
        if (sample.Contains('\0')) Assert.Contains('\0', Assert.IsType<string>(host.Paged.DisplayOf(0)));
        object? geometry = host.Geometry(), entry = host.Entry();
        Assert.NotNull(geometry);
        var caret = SyntaxCaret(host).CursorRectangle;
        long revision = host.Tab.Doc.Doc.Revision;
        byte[] before = TextPixels(EntryProperty<DrawingGroup>(entry!, "Drawing"));

        host.Tokens(original.ToArray());
        host.Draw();
        Assert.Same(entry, host.Entry());
        Assert.Same(geometry, host.Geometry());
        Assert.True(original.SequenceEqual(host.Paged.TokensOf(0)!));

        Token[] moved = [new(8, 8, "keyword"), new(24 + start, length, "number")];
        host.Tokens(moved);
        host.Draw();
        Assert.Same(geometry, host.Geometry());
        Assert.NotSame(entry, host.Entry());
        Assert.True(moved.SequenceEqual(host.Paged.TokensOf(0)!));
        Assert.False(before.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));

        entry = host.Entry();
        host.App.Workbench.Set(EditorThemes.ColorsSetting, $"keyword={SyntaxBlue} normal;number={SyntaxPink} normal");
        host.Draw();
        Assert.Same(geometry, host.Geometry());
        Assert.NotSame(entry, host.Entry());
        Assert.Equal(prefix, host.Paged.SelectedText());
        Assert.Equal(Encoding.UTF8.GetByteCount(prefix), host.Paged.CaretOffset);
        Assert.Equal(caret, SyntaxCaret(host).CursorRectangle);
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
        AssertLegacyTextPixels(host);
    }

    [AvaloniaFact]
    public void Overlapping_syntax_colors_use_the_last_token_instead_of_brush_group_order()
    {
        using var host = new Host("███ office ffi אבג 😀\t" + new string('x', 2 << 20));
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(28));
        host.App.Workbench.Set(EditorThemes.ColorsSetting, $"keyword={SyntaxPink} normal;number={SyntaxBlue} normal");
        host.Tokens([new(0, 2, "keyword"), new(1, 2, "number"), new(2, 1, "keyword")]);
        host.Draw();
        byte[] pixels = TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"));
        Assert.True(Count(pixels, Color.Parse(SyntaxPink)) > Count(pixels, Color.Parse(SyntaxBlue)));
        Assert.True(Count(pixels, Color.Parse(SyntaxBlue)) > 10);
        AssertLegacyTextPixels(host);
    }

    [AvaloniaFact]
    public void Font_styles_and_font_metrics_rebuild_geometry_while_color_only_themes_reuse_it()
    {
        const string prefix = "office ffi affinity אבג 😀\t";
        using var host = new Host(prefix + new string('x', 2 << 20));
        host.App.Workbench.Set("editor.fontFamily", JsonValue.Create("Arial"));
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(24));
        host.App.Workbench.Set(EditorThemes.ColorsSetting, $"keyword={SyntaxPink} normal");
        host.Tokens([new(0, prefix.Length, "keyword")]);
        host.Draw();
        object? geometry = host.Geometry(), entry = host.Entry();
        Assert.NotNull(geometry);

        host.App.Workbench.Set(EditorThemes.ThemeSetting, JsonValue.Create("github-dark"));
        host.Draw();
        Assert.Same(geometry, host.Geometry());
        Assert.NotSame(entry, host.Entry());

        foreach (string style in new[] { "bold", "italic", "bold italic" })
        {
            var oldGeometry = host.Geometry();
            byte[] previous = TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"));
            host.App.Workbench.Set(EditorThemes.ColorsSetting, $"keyword={SyntaxPink} {style}");
            host.Draw();
            Assert.NotSame(oldGeometry, host.Geometry());
            var palette = EditorLook.Of(SyntaxCanvas(host)).Palette;
            TokenStyle expected = (style.Contains("bold", StringComparison.Ordinal) ? TokenStyle.Bold : TokenStyle.None)
                | (style.Contains("italic", StringComparison.Ordinal) ? TokenStyle.Italic : TokenStyle.None);
            Assert.Equal(expected, palette.GetStyle("keyword"));
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("Arial"),
                expected.HasFlag(TokenStyle.Italic) ? FontStyle.Italic : FontStyle.Normal,
                expected.HasFlag(TokenStyle.Bold) ? FontWeight.Bold : FontWeight.Normal), out _));
            Assert.False(previous.AsSpan().SequenceEqual(TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"))));
        }

        geometry = host.Geometry();
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(30));
        host.Draw();
        Assert.NotSame(geometry, host.Geometry());
        geometry = host.Geometry();
        host.App.Workbench.Set("editor.fontFamily", JsonValue.Create("Courier New"));
        host.Draw();
        Assert.NotSame(geometry, host.Geometry());
        AssertLegacyTextPixels(host);
    }

    private static async Task FreshSyntax(Host host, int previousRuns,
        Func<(long First, int Count, int Column, int Columns), bool> requested)
    {
        for (int index = 0; index < 1000; index++)
        {
            host.Draw();
            var syntax = host.Paged.Syntax;
            if (syntax.ViewportRuns > previousRuns && requested(syntax.Requested)
                && !host.Paged.WindowsPending && !host.Paged.CaretMovePending
                && syntax.CaptureReadiness(host.Tab.Doc.Doc).Ready(host.Tab.Doc.Doc.Revision)) return;
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Fail("A new requested syntax viewport did not finish; the previous ready viewport is not sufficient.");
    }

    private static Control SyntaxCanvas(Host host) => (Control)typeof(PagedTextView)
        .GetField("_canvas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.Paged)!;

    private static T EntryProperty<T>(object entry, string name) => (T)entry.GetType()
        .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(entry)!;

    private static TextInputMethodClient SyntaxCaret(Host host)
    {
        var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        host.Paged.RaiseEvent(request);
        return Assert.IsAssignableFrom<TextInputMethodClient>(request.Client);
    }

    private static byte[] TextPixels(DrawingGroup drawing) => TextPixels(context => drawing.Draw(context));

    private static byte[] TextPixels(Action<DrawingContext> draw)
    {
        var size = new PixelSize(1152, 128);
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var context = target.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.White, new Rect(new Size(size.Width, size.Height)));
            using (context.PushTransform(Matrix.CreateTranslation(16, 8))) draw(context);
        }
        using var copy = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = copy.Lock();
        target.CopyPixels(buffer);
        var pixels = new byte[size.Width * size.Height * 4];
        for (int row = 0; row < size.Height; row++)
            Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * size.Width * 4, size.Width * 4);
        return pixels;
    }

    private static void AssertLegacyTextPixels(Host host)
    {
        object entry = host.Entry()!;
        var canvas = SyntaxCanvas(host);
        var key = EntryProperty<object>(entry, "Key");
        var view = EntryProperty<object>(key, "View");
        string shown = EntryProperty<string>(view, "Display");
        var foreground = new ImmutableSolidColorBrush(EntryProperty<Color>(key, "Foreground"), EntryProperty<double>(key, "Opacity"));
        byte[] actual = TextPixels(EntryProperty<DrawingGroup>(entry, "Drawing"));
        Token[] tokens = host.Paged.TokensOf(0)!.ToArray();
        long revision = host.Tab.Doc.Doc.Revision;
        byte[] expected;
        // A never-matching log regex disables the cache without changing any glyph, token or document byte.
        host.Tab.Doc.SetLogHighlight("(?!)", TimeSpan.FromMilliseconds(50));
        try
        {
            var scratch = new DrawingGroup();
            FormattedText reference;
            using (var context = scratch.Open())
                reference = (FormattedText)canvas.GetType().GetMethod("FormatLine", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(canvas, [context, view, 0L, 0d, shown, EditorLook.Of(canvas), foreground,
                        new ImmutableSolidColorBrush(Colors.Gray, 0.4), 0])!;
            Assert.NotSame(EntryProperty<FormattedText>(entry, "Text"), reference);
            Assert.True(tokens.SequenceEqual(host.Paged.TokensOf(0)!));
            expected = TextPixels(context => context.DrawText(reference, default));
        }
        finally { host.Tab.Doc.SetLogHighlight(null, TimeSpan.FromMilliseconds(50)); }
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
        int differences = 0;
        for (int at = 0; at < actual.Length; at += 4)
            if (!actual.AsSpan(at, 4).SequenceEqual(expected.AsSpan(at, 4))) differences++;
        Assert.True(differences == 0, $"The cached prefix differs from the original FormattedText path at {differences} pixels.");
    }
}

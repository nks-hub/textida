using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.3.4: the reader draws strikethrough, autolinks, footnotes and alerts, and lays the page out with the sizes of github-markdown-css 5.8.1 relative to the base font size:
/// text 1em with a line height of 1.5, headings 2/1.5/1.25/1/0.875/0.85em with 1.25, 16 px under blocks and 24 px above headings, code at 85 % with 16 px of padding.
/// </summary>
[Collection("LocState")]
public class MarkdownGfmRenderTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<ReaderHost> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        Loc.SetLanguage(Loc.DefaultLanguage);
    }

    private ReaderHost Host(string source, double width = 1000, double height = 1000, ThemeVariant? theme = null)
    {
        Loc.SetLanguage("en");
        var host = new ReaderHost(source, width, height, theme);
        _hosts.Add(host);
        return host;
    }

    private static MarkdownReaderDocument Build(string source)
    {
        Loc.SetLanguage("en");
        return MarkdownReaderModel.Build(source);
    }

    private static ReaderBlock Block(MarkdownReaderDocument doc, string text) => Assert.Single(doc.Blocks, b => b.Text == text);

    private static double FontSizeOf(TextLayout layout) => layout.TextLines[0].TextRuns[0].Properties!.FontRenderingEmSize;

    [AvaloniaFact]
    public void Strikethrough_is_a_struck_run_without_its_tildes()
    {
        var b = Block(Build("a ~~gone~~ b"), "a gone b");
        var span = Assert.Single(b.Spans, s => s.Style.HasFlag(ReaderStyle.Strike));
        Assert.Equal("gone", b.Text.Substring(span.Start, span.Length));
    }

    [AvaloniaFact]
    public void A_bare_url_is_a_link_that_only_hands_the_address_to_the_confirmation()
    {
        var host = Host("see https://example.com/page and www.example.org now");
        Assert.Equal(2, host.Doc.Links.Count);
        Assert.Empty(host.Opened);
        host.Reader.ActivateLink(0);
        host.Reader.ActivateLink(1);
        Assert.Equal(["https://example.com/page", "https://www.example.org/"], host.Opened.Select(u => u.ToString()));
        Assert.Equal("see https://example.com/page and www.example.org now", host.Doc.PlainText);
    }

    [AvaloniaFact]
    public void A_link_without_a_confirmation_handler_does_nothing_and_its_text_is_not_repeated()
    {
        var host = new ReaderHost("see https://example.com/page now", links: false);
        _hosts.Add(host);
        host.Reader.ActivateLink(0);
        Assert.Empty(host.Opened);
        Assert.Equal("see https://example.com/page now", host.Doc.PlainText);
    }

    [AvaloniaFact]
    public void A_footnote_has_a_mark_a_numbered_note_and_a_back_link_that_jump()
    {
        var host = Host("First line.\n\nText[^a] here.\n\n[^a]: The note.\n");
        Assert.Equal(["[1]", "↩"], host.Doc.Blocks.SelectMany(b => b.Spans.Where(s => s.Link >= 0).Select(s => b.Text.Substring(s.Start, s.Length))));
        var note = Assert.Single(host.Doc.Blocks, b => b.Text.StartsWith("The note.", StringComparison.Ordinal));
        Assert.Equal("1.", note.Path[^1].Marker);
        Assert.True(note.Small);
        Assert.Contains(host.Doc.Blocks, b => b.Kind == ReaderBlockKind.Rule && b.Level == 1);
        host.Reader.ActivateLink(0);
        host.Reader.ActivateLink(1);
        Assert.Equal([4, 2], host.Jumps); // the mark goes to the definition (line 4), the back link to the line of the mark (line 2)
    }

    [AvaloniaTheory]
    [InlineData("NOTE", "Note"), InlineData("TIP", "Tip"), InlineData("IMPORTANT", "Important"), InlineData("WARNING", "Warning"), InlineData("CAUTION", "Caution")]
    public void An_alert_has_a_title_in_its_colour_and_a_bar_of_its_kind(string kind, string title)
    {
        var host = Host($"> [!{kind}]\n> Mind the **gap**.\n");
        var head = host.Doc.Blocks[0];
        Assert.Equal((ReaderBlockKind.AlertTitle, title), (head.Kind, head.Text));
        Assert.Equal(kind, head.Path[^1].AlertKind, ignoreCase: true);
        Assert.Equal("Mind the gap.", host.Doc.Blocks[1].Text);
        Assert.Same(head.Path[^1], host.Doc.Blocks[1].Path[^1]);
        Assert.DoesNotContain("[!", host.Doc.PlainText);
        Assert.True(host.Application().TryFindResource("Md" + title, ThemeVariant.Light, out var brush) && brush is ISolidColorBrush);
        // the text stands 0.25em of bar plus 1em of padding to the right of the edge, and 8 px of padding above the title
        Assert.Equal(20, host.Reader.BlockBounds(1).Left - LeftEdge(host), 3);
        Assert.Equal(MarkdownReader.PagePaddingY + 8, host.Reader.BlockBounds(0).Top, 3);
    }

    private static double LeftEdge(ReaderHost host) => (host.Reader.Bounds.Width - Math.Min(MarkdownReader.ColumnMax, host.Reader.Bounds.Width - 2 * MarkdownReader.PagePaddingX)) / 2;

    [AvaloniaFact]
    public void An_unknown_alert_kind_keeps_its_word_as_the_title()
    {
        var doc = Build("> [!MAYBE]\n> text\n");
        Assert.Equal("MAYBE", doc.Blocks[0].Text);
        Assert.Equal("text", doc.Blocks[1].Text);
    }

    [AvaloniaFact]
    public void An_emoji_shortcode_is_drawn_as_the_character_and_a_smiley_stays_text()
    {
        Assert.Equal("hi \U0001F604 :)", Build("hi :smile: :)").PlainText);
    }

    [AvaloniaFact]
    public void A_link_to_a_heading_jumps_to_its_line_even_when_the_heading_has_new_inlines()
    {
        var host = Host("[go](#second-old-part)\n\n# Second ~~old~~ part\n");
        host.Reader.ActivateLink(0);
        Assert.Equal([2], host.Jumps);
        Assert.Equal(1, Block(host.Doc, "Second old part").Level);
    }

    // ---- the look of GitHub ----

    private const string Reference = "# Heading one\n\nA paragraph with **bold**, *italic*, ~~struck~~, `inline code` and [a link](https://example.invalid).\n\n"
        + "Second paragraph.\n\n## Heading two\n\n### Heading three\n\n#### Heading four\n\n##### Heading five\n\n###### Heading six\n\n"
        + "- first item\n- second item\n  - nested item\n- third item\n\n1. one\n2. two\n   1. roman\n      1. alpha\n\n- [x] done task\n- [ ] open task\n\n"
        + "> A quote that spans\n> two lines.\n\n> [!NOTE]\n> Useful information.\n\n> [!WARNING]\n> Critical content.\n\n"
        + "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n\n| Left | Centre | Right |\n|:---|:---:|---:|\n| a | b | c |\n| longer cell | middle | 42 |\n| stripe | row | 3 |\n\n"
        + "---\n\nA sentence with a footnote[^1].\n\n[^1]: The footnote text.\n";

    [AvaloniaFact]
    public void Text_and_headings_have_the_sizes_and_line_heights_of_github_relative_to_the_base_font()
    {
        var host = Host(Reference);
        int paragraph = host.Find(b => b.Text.StartsWith("A paragraph", StringComparison.Ordinal));
        var body = host.Reader.LayoutOf(paragraph)!;
        Assert.Equal(16, FontSizeOf(body), 3);
        Assert.All(body.TextLines, line => Assert.Equal(24, line.Height, 3));
        double[] sizes = [32, 24, 20, 16, 14, 13.6];
        for (int level = 1; level <= 6; level++)
        {
            int index = host.Find(b => b.Kind == ReaderBlockKind.Heading && b.Level == level);
            var layout = host.Reader.LayoutOf(index)!;
            Assert.Equal(sizes[level - 1], FontSizeOf(layout), 3);
            Assert.Equal(1.25 * sizes[level - 1], layout.TextLines[0].Height, 3);
            Assert.Equal(FontWeight.SemiBold, layout.TextLines[0].TextRuns[0].Properties!.Typeface.Weight);
            // H1 and H2 have 0.3em of padding and a 1 px rule under them
            double extra = level <= 2 ? 0.3 * sizes[level - 1] + 1 : 0;
            Assert.Equal(layout.Height + extra, host.Reader.BlockBounds(index).Height, 3);
        }
        int code = host.Find(b => b.Text.Contains("inline code", StringComparison.Ordinal));
        var runs = host.Reader.LayoutOf(code)!.TextLines.SelectMany(l => l.TextRuns).Where(r => r.Text.ToString().Contains("inline", StringComparison.Ordinal)).ToList();
        Assert.Contains(runs, r => Math.Abs(r.Properties!.FontRenderingEmSize - 13.6) < 0.01);
        int bold = host.Find(b => b.Text.StartsWith("A paragraph", StringComparison.Ordinal));
        Assert.Contains(host.Doc.Blocks[bold].Spans, s => s.Style == ReaderStyle.Bold && host.Doc.Blocks[bold].Text.Substring(s.Start, s.Length) == "bold");
    }

    private static double GapBetween(ReaderHost host, int above, int below) => host.Reader.BlockBounds(below).Top - host.Reader.BlockBounds(above).Bottom;

    [AvaloniaFact]
    public void Blocks_are_spaced_as_github_spaces_them()
    {
        var host = Host(Reference);
        int Index(string start) => host.Find(b => b.Text.StartsWith(start, StringComparison.Ordinal));
        Assert.Equal(MarkdownReader.PagePaddingY, host.Reader.BlockBounds(0).Top, 3); // the first block has no margin of its own
        Assert.Equal(16, GapBetween(host, Index("Heading one"), Index("A paragraph")), 3);
        Assert.Equal(16, GapBetween(host, Index("A paragraph"), Index("Second paragraph")), 3);
        Assert.Equal(24, GapBetween(host, Index("Second paragraph"), Index("Heading two")), 3);
        Assert.Equal(24, GapBetween(host, Index("Heading two"), Index("Heading three")), 3);
        Assert.Equal(16, GapBetween(host, Index("Heading six"), Index("first item")), 3);
        Assert.Equal(4, GapBetween(host, Index("first item"), Index("second item")), 3); // li + li: 0.25em
        Assert.Equal(0, GapBetween(host, Index("second item"), Index("nested item")), 3); // a nested list has no margin
        Assert.Equal(4, GapBetween(host, Index("nested item"), Index("third item")), 3);
        Assert.Equal(16, GapBetween(host, Index("third item"), Index("one")), 3);
        Assert.Equal(16, GapBetween(host, Index("A quote"), Index("Note")) - 8, 3); // after a quote 16, plus 8 of padding inside the alert
        int rule = host.Find(b => b.Kind == ReaderBlockKind.Rule && b.Level == 0);
        Assert.Equal(4, host.Reader.BlockBounds(rule).Height, 3);
        Assert.Equal(24, GapBetween(host, rule - 1, rule), 3);
        Assert.Equal(24, GapBetween(host, rule, rule + 1), 3);
    }

    [AvaloniaFact]
    public void Lists_quotes_code_and_tables_are_indented_padded_and_ordered_like_github()
    {
        var host = Host(Reference);
        int Index(string start) => host.Find(b => b.Text.StartsWith(start, StringComparison.Ordinal));
        double edge = LeftEdge(host);
        Assert.Equal(32, host.Reader.BlockBounds(Index("first item")).Left - edge, 3); // 2em
        Assert.Equal(64, host.Reader.BlockBounds(Index("nested item")).Left - edge, 3);
        Assert.Equal(20, host.Reader.BlockBounds(Index("A quote")).Left - edge, 3); // 0.25em bar + 1em padding
        Assert.Equal(["•", "◦"], new[] { host.Doc.Blocks[Index("first item")].Path[^1].Marker, host.Doc.Blocks[Index("nested item")].Path[^1].Marker });
        Assert.Equal(["1.", "i.", "a."], new[] { Index("one"), Index("roman"), Index("alpha") }.Select(i => host.Doc.Blocks[i].Path[^1].Marker));
        Assert.Equal([true, false], new[] { Index("done task"), Index("open task") }.Select(i => host.Doc.Blocks[i].Path[^1].Task!.Value));

        int code = host.Find(b => b.Kind == ReaderBlockKind.Code);
        var codeLayout = host.Reader.LayoutOf(code)!;
        Assert.Equal(13.6, FontSizeOf(codeLayout), 3);
        Assert.Equal(1.45 * 13.6, codeLayout.TextLines[0].Height, 3);
        var bounds = host.Reader.BlockBounds(code);
        var origin = host.Reader.BoxOrigin(code)!.Value;
        Assert.Equal((16.0, 16.0), (Math.Round(origin.X - bounds.Left, 3), Math.Round(origin.Y - bounds.Top, 3)));
        Assert.Equal(2 * 1.45 * 13.6 + 32, bounds.Height, 3);

        var rows = Enumerable.Range(0, host.Doc.Blocks.Count).Where(i => host.Doc.Blocks[i].Kind == ReaderBlockKind.TableRow).ToList();
        Assert.Equal(4, rows.Count);
        var cell = host.Reader.BoxOrigin(rows[1], 0)!.Value;
        Assert.Equal((14.0, 7.0), (Math.Round(cell.X - host.Reader.BlockBounds(rows[1]).Left, 3), Math.Round(cell.Y - host.Reader.BlockBounds(rows[1]).Top, 3))); // 1 px border, 13 by 6 px padding
        Assert.Equal(24 + 12 + 1, host.Reader.BlockBounds(rows[1]).Height, 3);
        Assert.All(rows.Skip(1), r => Assert.Equal(0, GapBetween(host, r - 1, r), 3));
        Assert.True(host.Doc.Blocks[rows[0]].Header);
        Assert.Equal([0, 1, 2], rows.Skip(1).Select(r => host.Doc.Blocks[r].Row));
        Assert.True(host.Reader.LayoutOf(rows[1], 2)!.TextLines[0].Start > 0, "the right column is aligned right");
    }

    [AvaloniaFact]
    public void Everything_scales_with_the_base_font_size()
    {
        var host = Host(Reference);
        host.Reader.BaseFontSize = 20;
        host.Settle();
        int Index(string start) => host.Find(b => b.Text.StartsWith(start, StringComparison.Ordinal));
        Assert.Equal(30, host.Reader.LayoutOf(Index("A paragraph"))!.TextLines[0].Height, 3);
        Assert.Equal(40, FontSizeOf(host.Reader.LayoutOf(Index("Heading one"))!), 3);
        Assert.Equal(20, GapBetween(host, Index("A paragraph"), Index("Second paragraph")), 3);
        Assert.Equal(30, GapBetween(host, Index("Second paragraph"), Index("Heading two")), 3);
    }

    [AvaloniaTheory, InlineData("Light"), InlineData("Dark")]
    public void The_reference_page_renders_to_a_picture_in_both_themes(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var host = Host(Reference, 1000, 2100, variant);
        var frame = host.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string folder = Environment.GetEnvironmentVariable("TEXTIDA_MD_RENDER_OUT") is { Length: > 0 } wanted ? wanted : Path.Combine(Path.GetTempPath(), "textida-md-render");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, $"reference-{name.ToLowerInvariant()}.png");
        frame!.Save(file);
        output.WriteLine(file);
        Assert.True(new FileInfo(file).Length > 10_000);
        // the page is drawn in the colour of GitHub's page of the theme
        Assert.True(host.Application().TryFindResource("MdPage", variant, out var page) && page is ISolidColorBrush);
        using var pixels = frame.Lock();
        var colour = ((ISolidColorBrush)page!).Color;
        uint pixel = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(pixels.Address + 10 * pixels.RowBytes + 10 * 4);
        bool rgba = pixels.Format == Avalonia.Platform.PixelFormats.Rgba8888;
        var drawn = rgba ? ((byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16)) : ((byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
        Assert.Equal((colour.R, colour.G, colour.B), drawn);
    }

    [AvaloniaTheory, InlineData("Light"), InlineData("Dark")]
    public void The_text_bars_and_links_of_the_page_stand_out_in_both_themes(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Color Of(string key)
        {
            Assert.True(Application.Current!.TryFindResource(key, variant, out var value), key);
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }
        foreach (var key in new[] { "MdFg", "MdMuted", "MdLink" })
            Assert.True(ContrastColors.Ratio(Of(key), Of("MdPage")) >= 4.5, $"{name}: {key} on the page");
        Assert.True(ContrastColors.Ratio(Of("MdFg"), Of("MdCodeBg")) >= 4.5, $"{name}: code text on its background");
        foreach (var key in new[] { "MdNote", "MdTip", "MdImportant", "MdWarning", "MdCaution" })
            Assert.True(ContrastColors.Ratio(Of(key), Of("MdPage")) >= 3, $"{name}: {key} on the page");
    }
}

internal static class ReaderHostExtensions
{
    public static Application Application(this ReaderHost host) => Avalonia.Application.Current!;
}

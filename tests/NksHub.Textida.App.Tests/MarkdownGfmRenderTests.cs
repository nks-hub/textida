using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.3.4: the Markdown preview draws strikethrough, autolinks, footnotes and alerts, and the styles space it like GitHub's page.</summary>
[Collection("LocState")]
public class MarkdownGfmRenderTests : IDisposable
{
    private readonly List<Window> _windows = [];

    public MarkdownGfmRenderTests() => Loc.SetLanguage("en");

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();
        Loc.SetLanguage(Loc.DefaultLanguage);
    }

    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines) => inlines.SelectMany(inline => inline switch
    {
        Run run => new[] { run },
        Span span => Runs(span.Inlines),
        _ => [],
    });

    private static string Text(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? string.Concat(Runs(inlines).Select(run => run.Text)) : block.Text ?? "";

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Renders into a window that is shown, so the styles of the application are applied to the controls.</summary>
    private Control Render(string source, Action<Uri>? open = null, Action<int>? jump = null)
    {
        var body = PreviewRenderer.Render(PreviewFormats.Markdown, source, new PreviewRenderer.Ctx(new FontFamily("Consolas"), Open: open, JumpToLine: jump));
        var window = new Window { Content = new ScrollViewer { Content = body }, Width = 1000, Height = 800 };
        _windows.Add(window);
        window.Show();
        window.UpdateLayout();
        return body;
    }

    private static Button Link(Control body, string name) => Assert.Single(Tree(body).OfType<Button>(), b => b.Classes.Contains("previewlink") && Avalonia.Automation.AutomationProperties.GetName(b).Contains(name));

    [AvaloniaFact]
    public void Strikethrough_is_a_struck_through_span_without_its_tildes()
    {
        var body = Render("a ~~gone~~ b");
        var paragraph = Assert.Single(Tree(body).OfType<SelectableTextBlock>());
        var span = Assert.Single(paragraph.Inlines!.OfType<Span>());
        Assert.Contains(span.Classes, c => c == "previewstrike");
        Assert.Equal(TextDecorationLocation.Strikethrough, Assert.Single(span.TextDecorations!).Location);
        Assert.Equal("gone", Text(new TextBlock { Inlines = { span } }));
        Assert.Equal("a gone b", Text(paragraph));
    }

    [AvaloniaFact]
    public void A_bare_url_is_a_link_button_that_only_hands_the_address_to_the_confirmation()
    {
        var opened = new List<Uri>();
        var body = Render("see https://example.com/page and www.example.org now", opened.Add);
        var links = Tree(body).OfType<Button>().Where(b => b.Classes.Contains("previewlink")).ToList();
        Assert.Equal(2, links.Count);
        Assert.Empty(opened);
        Click(links[0]);
        Click(links[1]);
        Assert.Equal(["https://example.com/page", "https://www.example.org/"], opened.Select(u => u.ToString()));
    }

    [AvaloniaFact]
    public void A_bare_url_stays_text_without_a_confirmation_handler_and_is_not_repeated()
    {
        var body = Render("see https://example.com/page now");
        Assert.Empty(Tree(body).OfType<Button>());
        Assert.Equal("see https://example.com/page now", Text(Assert.Single(Tree(body).OfType<SelectableTextBlock>())));
    }

    [AvaloniaFact]
    public void A_footnote_has_a_mark_a_numbered_note_and_a_back_link_that_jump()
    {
        var jumps = new List<int>();
        var body = Render("First line.\n\nText[^a] here.\n\n[^a]: The note.\n", jump: jumps.Add);
        var marks = Tree(body).OfType<Button>().Where(b => b.Classes.Contains("previewfootnote")).ToList();
        Assert.Equal(2, marks.Count);
        Assert.Equal(["[1]", "↩"], marks.Select(m => ((TextBlock)m.Content!).Text));
        var note = Assert.Single(Tree(body).OfType<Grid>(), g => Avalonia.Automation.AutomationProperties.GetName(g) == "Footnote 1");
        Assert.Contains(Tree(note).OfType<TextBlock>(), t => t.Text == "1." && t.Classes.Contains("previewmarker"));
        Assert.Contains(Tree(note).OfType<SelectableTextBlock>(), t => Text(t).StartsWith("The note."));
        Click(marks[0]);
        Click(marks[1]);
        Assert.Equal([4, 2], jumps); // the mark goes to the definition (line 4), the back link to the line of the mark (line 2)
    }

    [AvaloniaTheory]
    [InlineData("NOTE", "Note", "previewalertnote"), InlineData("TIP", "Tip", "previewalerttip"), InlineData("IMPORTANT", "Important", "previewalertimportant")]
    [InlineData("WARNING", "Warning", "previewalertwarning"), InlineData("CAUTION", "Caution", "previewalertcaution")]
    public void An_alert_has_an_icon_a_title_and_a_bar_of_its_kind(string kind, string title, string style)
    {
        var body = Render($"> [!{kind}]\n> Mind the **gap**.\n");
        var alert = Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewalert"));
        Assert.Contains(style, alert.Classes);
        Assert.Contains("previewquote", alert.Classes);
        Assert.Equal(title, Assert.Single(Tree(alert).OfType<TextBlock>(), t => t.Classes.Contains("previewalerttitle")).Text);
        Assert.NotNull(Assert.Single(Tree(alert).OfType<Avalonia.Controls.Shapes.Path>()).Data);
        Assert.Contains(Tree(alert).OfType<SelectableTextBlock>(), t => Text(t) == "Mind the gap.");
        Assert.DoesNotContain(Tree(body).OfType<TextBlock>(), t => Text(t).Contains("[!"));
        Assert.Equal(new Thickness(4, 0, 0, 0), alert.BorderThickness);
        Assert.NotNull(alert.BorderBrush);
    }

    [AvaloniaFact]
    public void An_unknown_alert_kind_keeps_its_word_as_the_title_and_the_text()
    {
        var body = Render("> [!MAYBE]\n> text\n");
        Assert.Equal("MAYBE", Assert.Single(Tree(body).OfType<TextBlock>(), t => t.Classes.Contains("previewalerttitle")).Text);
        Assert.Contains(Tree(body).OfType<SelectableTextBlock>(), t => Text(t) == "text");
    }

    [AvaloniaFact]
    public void An_emoji_shortcode_is_drawn_as_the_character_and_a_smiley_stays_text()
    {
        var body = Render("hi :smile: :)");
        Assert.Equal("hi \U0001F604 :)", Text(Assert.Single(Tree(body).OfType<SelectableTextBlock>())));
    }

    [AvaloniaFact]
    public void A_link_to_a_heading_jumps_to_its_line_even_when_the_heading_has_new_inlines()
    {
        var jumps = new List<int>();
        var body = Render("[go](#second-old-part)\n\n# Second ~~old~~ part\n", jump: jumps.Add);
        Click(Link(body, "go"));
        Assert.Equal([2], jumps);
        Assert.Contains(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewhead1"));
    }

    [AvaloniaFact]
    public void The_page_is_a_centred_column_of_at_most_860_with_16_between_blocks()
    {
        var body = Assert.IsType<StackPanel>(Render("first\n\nsecond\n"));
        Assert.Equal(860, body.MaxWidth);
        Assert.Equal(HorizontalAlignment.Center, body.HorizontalAlignment);
        Assert.Equal(16, body.Spacing);
        Assert.Equal(new Thickness(32, 24), body.Margin);
        var paragraphs = body.Children.OfType<SelectableTextBlock>().ToList();
        Assert.Equal(2, paragraphs.Count);
        Assert.All(paragraphs, p => Assert.Equal(22, p.LineHeight));
        Assert.Equal(16, paragraphs[1].Bounds.Top - paragraphs[0].Bounds.Bottom);
    }

    [AvaloniaFact]
    public void Headings_have_the_sizes_and_margins_of_a_page_and_h1_and_h2_a_rule()
    {
        var body = Render("# One\n\n## Two\n\n### Three\n\n###### Six\n");
        double Size(int level) => Assert.Single(Tree(body).OfType<SelectableTextBlock>(), t => t.Classes.Contains("previewhead" + level)).FontSize;
        Assert.Equal([28, 22, 18, 13], [Size(1), Size(2), Size(3), Size(6)]);
        foreach (int level in new[] { 1, 2 })
        {
            var box = Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewhead" + level));
            Assert.Equal(new Thickness(0, 0, 0, 1), box.BorderThickness);
            Assert.Equal(new Thickness(0, 8, 0, 0), box.Margin);
        }
        Assert.Equal(new Thickness(0, 8, 0, 0), Assert.Single(Tree(body).OfType<SelectableTextBlock>(), t => t.Classes.Contains("previewhead3")).Margin);
    }

    [AvaloniaFact]
    public void Lists_quotes_code_tables_and_rules_are_indented_shaded_and_bordered()
    {
        var body = Render("- a\n  - nested\n- b\n\n> quoted\n\n```\ncode\n```\n\n`x` y\n\n| h1 | h2 |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n\n---\n");
        var lists = Tree(body).OfType<StackPanel>().Where(p => p.Classes.Contains("previewlist")).ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, l => Assert.Equal(new Thickness(16, 0, 0, 0), l.Margin));
        Assert.Equal(4, lists[0].Spacing);
        Assert.Equal(new Thickness(4, 0, 0, 0), Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewquote")).BorderThickness);
        var code = Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewcode"));
        Assert.NotNull(code.Background);
        Assert.Equal((new Thickness(16), new CornerRadius(6)), (code.Padding, code.CornerRadius));
        var inline = Assert.Single(Tree(body).OfType<SelectableTextBlock>().SelectMany(t => t.Inlines!.OfType<Run>()), r => r.Classes.Contains("previewinlinecode"));
        Assert.NotNull(inline.Background);
        var cells = Tree(body).OfType<Border>().Where(b => b.Classes.Contains("previewcell")).ToList();
        Assert.Equal(6, cells.Count);
        Assert.All(cells, c => Assert.Equal((new Thickness(13, 6), new Thickness(1)), (c.Padding, c.BorderThickness)));
        Assert.Equal([false, false, false, false, true, true], cells.Select(c => c.Background is not null && !c.Classes.Contains("previewcellhead")));
        Assert.NotNull(cells[0].Background); // the header is shaded as well
        Assert.NotEqual(cells[0].Background, cells[4].Background);
        Assert.Equal(3, Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewrule")).Height);
    }

    [AvaloniaTheory, InlineData("Light"), InlineData("Dark")]
    public void The_bars_and_icons_of_the_alerts_stand_out_from_the_page_in_both_themes(string name)
    {
        var variant = name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        Color Of(string key)
        {
            Assert.True(Application.Current!.TryFindResource(key, variant, out var value), key);
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }
        foreach (var key in new[] { "Accent", "Good", "GitConflict", "GitModified", "Danger", "FgMuted" })
        {
            double ratio = ContrastColors.Ratio(Of(key), Of("BgEditor"));
            Assert.True(ratio >= 3, $"{name}: {key} on the page is {ratio:0.00} : 1");
        }
        foreach (var fill in new[] { "Bg1", "BgHover" })
            Assert.True(ContrastColors.Ratio(Of("Fg"), Of(fill)) >= 4.5, $"{name}: text on {fill}");
    }
}

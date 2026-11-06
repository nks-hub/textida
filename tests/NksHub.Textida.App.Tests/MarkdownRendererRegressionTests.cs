using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

public class MarkdownRendererRegressionTests
{
    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines) => inlines.SelectMany(inline => inline switch
    {
        Run run => new[] { run },
        Span span => Runs(span.Inlines),
        _ => [],
    });

    private static string Text(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? string.Concat(Runs(inlines).Select(run => run.Text)) : block.Text ?? "";

    private static MarkdownReaderDocument Build(string source) => MarkdownReaderModel.Build(source);

    [AvaloniaFact]
    public void An_html_table_applies_colspan_and_rowspan()
    {
        var body = PreviewRenderer.Render(PreviewFormats.Html,
            "<table><tr><td colspan=\"2\">wide</td><td rowspan=\"2\">tall</td></tr><tr><td>a</td><td>b</td></tr></table>",
            new PreviewRenderer.Ctx(new FontFamily("Consolas")));
        var grid = Assert.Single(Tree(body).OfType<Grid>(), g => g.ColumnDefinitions.Count > 0);
        Assert.Equal(3, grid.ColumnDefinitions.Count);
        var cells = grid.Children.OfType<Border>().ToDictionary(b => Text((TextBlock)b.Child!));
        Assert.Equal((0, 0, 2, 1), (Grid.GetRow(cells["wide"]), Grid.GetColumn(cells["wide"]), Grid.GetColumnSpan(cells["wide"]), Grid.GetRowSpan(cells["wide"])));
        Assert.Equal((0, 2, 1, 2), (Grid.GetRow(cells["tall"]), Grid.GetColumn(cells["tall"]), Grid.GetColumnSpan(cells["tall"]), Grid.GetRowSpan(cells["tall"])));
        Assert.Equal((1, 0), (Grid.GetRow(cells["a"]), Grid.GetColumn(cells["a"])));
        Assert.Equal((1, 1), (Grid.GetRow(cells["b"]), Grid.GetColumn(cells["b"])));
    }

    [AvaloniaFact]
    public void A_heading_keeps_its_hash_and_its_text_runs_are_semibold()
    {
        var heading = Assert.Single(Build("# C#\n").Blocks);
        Assert.Equal((ReaderBlockKind.Heading, 1, "C#"), (heading.Kind, heading.Level, heading.Text));
        Assert.NotEmpty(heading.Spans);
        Assert.All(heading.Spans, span => Assert.True(span.Style.HasFlag(ReaderStyle.Bold)));
    }

    [AvaloniaTheory]
    [InlineData("~~~\n# hidden\n~~~\n# visible", 3)]
    [InlineData("````\n# hidden\n```\n# still hidden\n````\n# visible", 5)]
    [InlineData("```\n# hidden\n~~~\n# still hidden\n```\n# visible", 5)]
    [InlineData("  ```\n# hidden\n  ```\n# visible", 3)]
    public void The_page_and_outline_agree_on_fenced_code_and_source_lines(string source, int line)
    {
        var doc = Build(source);
        var heading = Assert.Single(doc.Blocks, b => b.Kind == ReaderBlockKind.Heading);
        Assert.Equal(("visible", line), (heading.Text, heading.Line));
        var outline = Assert.Single(MarkdownTools.Headings(source.Split('\n')));
        Assert.Equal(("visible", line), (outline.Title, outline.Line));
        Assert.Equal(outline, Assert.Single(doc.Headings));
    }

    [AvaloniaFact]
    public void Repeated_ordered_list_markers_are_numbered_from_the_first_marker()
    {
        Assert.Equal(["1.", "2.", "3."], Build("1. first\n1. second\n1. third\n").Blocks.Select(b => b.Path[^1].Marker));
        Assert.Equal(["7.", "8."], Build("7. first\n1. second\n").Blocks.Select(b => b.Path[^1].Marker));
    }

    [AvaloniaFact]
    public void A_nested_list_keeps_its_continued_paragraph_and_code_in_the_item()
    {
        var blocks = Build("- outer\n  - inner\n\n    continuation\n\n        code\n\n- last\n").Blocks;
        var inner = Assert.Single(blocks, b => b.Text == "inner");
        var item = inner.Path[^1];
        Assert.Contains(item, Assert.Single(blocks, b => b.Text == "continuation").Path);
        Assert.Contains(item, Assert.Single(blocks, b => b.Kind == ReaderBlockKind.Code && b.Text.Contains("code", StringComparison.Ordinal)).Path);
        Assert.DoesNotContain(item, Assert.Single(blocks, b => b.Text == "last").Path);
        Assert.Equal(2, inner.Path.Length);
    }

    [AvaloniaTheory]
    [InlineData("first  \nsecond")]
    [InlineData("first\\\nsecond")]
    public void A_hard_break_starts_a_new_visual_line(string source)
    {
        using var host = new ReaderHost(source, 600, 300);
        Assert.Equal("first\nsecond", host.Doc.PlainText);
        Assert.Equal(2, host.Reader.LayoutOf(0)!.TextLines.Count);
    }

    [AvaloniaTheory]
    [InlineData("\\*literal\\* &amp; \\|", "*literal* & |")]
    [InlineData("``a ` b``", "a ` b")]
    [InlineData("`first\nsecond`", "first second")]
    public void Escapes_entities_and_code_spans_have_their_visible_text(string source, string expected)
    {
        Assert.Equal(expected, Assert.Single(Build(source).Blocks).Text);
    }

    [AvaloniaFact]
    public void Strong_emphasis_is_both_bold_and_italic_without_literal_markers()
    {
        var paragraph = Assert.Single(Build("***both***").Blocks);
        Assert.Equal("both", paragraph.Text);
        Assert.All(paragraph.Spans, span => Assert.Equal(ReaderStyle.Bold | ReaderStyle.Italic, span.Style));
    }

    [AvaloniaFact]
    public void A_formatted_link_label_keeps_the_balanced_parentheses_of_its_target()
    {
        using var host = new ReaderHost("See [**manual**](https://example.invalid/a_(b)) after.", 600, 300);
        var paragraph = Assert.Single(host.Doc.Blocks);
        Assert.Equal("See manual after.", paragraph.Text);
        var label = Assert.Single(paragraph.Spans, s => s.Link >= 0);
        Assert.Equal(("manual", ReaderStyle.Bold | ReaderStyle.Link), (paragraph.Text.Substring(label.Start, label.Length), label.Style));
        host.Reader.ActivateLink(label.Link);
        Assert.Equal("https://example.invalid/a_(b)", Assert.Single(host.Opened).OriginalString);
    }

    [AvaloniaFact]
    public void Link_reference_definitions_are_not_shown_and_a_refused_link_keeps_its_target_as_text()
    {
        var doc = Build("[ref]: https://example.invalid/x\n\nA [local](file:///etc/passwd) link.\n");
        Assert.Equal("A local (file:///etc/passwd) link.", Assert.Single(doc.Blocks).Text);
        Assert.Empty(doc.Links);
    }
}

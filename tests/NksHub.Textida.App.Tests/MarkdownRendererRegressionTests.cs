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

    private static Control Render(string source, List<PreviewRenderer.Anchor>? anchors = null, Action<Uri>? open = null) =>
        PreviewRenderer.Render(PreviewFormats.Markdown, source, new PreviewRenderer.Ctx(new FontFamily("Consolas"), Open: open, Anchors: anchors));

    [AvaloniaFact]
    public void A_heading_keeps_its_hash_and_its_text_runs_are_semibold()
    {
        var body = Render("# C#\n");
        var heading = Assert.Single(Tree(body).OfType<TextBlock>(), t => t.Classes.Contains("previewhead1"));
        Assert.Equal("C#", Text(heading));
        Assert.NotEmpty(Runs(heading.Inlines!));
        Assert.All(Runs(heading.Inlines!), run => Assert.Equal(FontWeight.SemiBold, run.FontWeight));
    }

    [AvaloniaTheory]
    [InlineData("~~~\n# hidden\n~~~\n# visible", 3)]
    [InlineData("````\n# hidden\n```\n# still hidden\n````\n# visible", 5)]
    [InlineData("```\n# hidden\n~~~\n# still hidden\n```\n# visible", 5)]
    [InlineData("  ```\n# hidden\n  ```\n# visible", 3)]
    public void The_page_and_outline_agree_on_fenced_code_and_source_lines(string source, int line)
    {
        var anchors = new List<PreviewRenderer.Anchor>();
        var body = Render(source, anchors);
        var headings = Tree(body).OfType<TextBlock>().Where(t => t.Classes.Contains("previewhead1")).ToList();
        Assert.Equal("visible", Text(Assert.Single(headings)));
        var heading = Assert.Single(MarkdownTools.Headings(source.Split('\n')));
        Assert.Equal(("visible", line), (heading.Title, heading.Line));
        Assert.Equal(line, Assert.Single(anchors, a => a.Control.Classes.Contains("previewhead1")).Line);
    }

    [AvaloniaFact]
    public void Repeated_ordered_list_markers_are_numbered_from_the_first_marker()
    {
        var body = Render("1. first\n1. second\n1. third\n");
        var markers = Tree(body).OfType<TextBlock>().Select(Text)
            .Select(text => Regex.Match(text, @"^([0-9]+)\."))
            .Where(match => match.Success).Select(match => match.Groups[1].Value);
        Assert.Equal(["1", "2", "3"], markers);
    }

    [AvaloniaFact]
    public void A_nested_list_keeps_its_continued_paragraph_and_code_in_the_item()
    {
        var body = Render("- outer\n  - inner\n\n    continuation\n\n        code\n\n- last\n");
        var blocks = Tree(body).OfType<TextBlock>().ToList();
        var inner = Assert.Single(blocks, t => Text(t).EndsWith("inner", StringComparison.Ordinal));
        var continued = Assert.Single(blocks, t => Text(t) == "continuation");
        var code = Assert.Single(Tree(body).OfType<Border>(), b => b.Classes.Contains("previewcode"));
        Assert.Contains("code", string.Join("\n", Tree(code).OfType<TextBlock>().Select(Text)));
        var item = inner.GetLogicalAncestors().OfType<StackPanel>().FirstOrDefault(panel => !ReferenceEquals(panel, body)
            && Tree(panel).Contains(continued) && Tree(panel).Contains(code));
        Assert.NotNull(item);
        Assert.DoesNotContain(Tree(item), c => c is TextBlock text && Text(text).EndsWith("last", StringComparison.Ordinal));
    }

    [AvaloniaTheory]
    [InlineData("first  \nsecond")]
    [InlineData("first\\\nsecond")]
    public void A_hard_break_starts_a_new_visual_line(string source)
    {
        var body = Render(source);
        var window = new Window { Width = 600, Height = 300, Content = body };
        try
        {
            window.Show();
            window.UpdateLayout();
            var paragraph = Assert.Single(Tree(body).OfType<TextBlock>());
            Assert.Equal(2, paragraph.TextLayout.TextLines.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("\\*literal\\* &amp; \\|", "*literal* & |")]
    [InlineData("``a ` b``", "a ` b")]
    [InlineData("`first\nsecond`", "first second")]
    public void Escapes_entities_and_code_spans_have_their_visible_text(string source, string expected)
    {
        Assert.Equal(expected, Text(Assert.Single(Tree(Render(source)).OfType<TextBlock>())));
    }

    [AvaloniaFact]
    public void Strong_emphasis_is_both_bold_and_italic_without_literal_markers()
    {
        var paragraph = Assert.Single(Tree(Render("***both***")).OfType<TextBlock>());
        Assert.Equal("both", Text(paragraph));
        Assert.All(Runs(paragraph.Inlines!), run =>
        {
            Assert.True(run.FontWeight >= FontWeight.SemiBold);
            Assert.Equal(FontStyle.Italic, run.FontStyle);
        });
    }

    [AvaloniaFact]
    public void A_formatted_link_label_keeps_the_balanced_parentheses_of_its_target()
    {
        var opened = new List<Uri>();
        var body = Render("See [**manual**](https://example.invalid/a_(b)) after.", open: opened.Add);
        var window = new Window { Width = 600, Height = 300, Content = body };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = Assert.Single(Tree(body).OfType<Button>());
            var label = Assert.Single(Tree(button).OfType<TextBlock>());
            Assert.Equal("manual", Text(label));
            Assert.All(Runs(label.Inlines!), run => Assert.True(run.FontWeight >= FontWeight.SemiBold));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://example.invalid/a_(b)", Assert.Single(opened).OriginalString);
        }
        finally { window.Close(); }
    }
}

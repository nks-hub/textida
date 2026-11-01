using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Markdig.Extensions.Tables;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class MarkdownBudgetTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Documents()
    {
        yield return [new string('>', 100) + " start\n\nend", "depth"];
        yield return ["start " + string.Concat(Enumerable.Repeat("`word` ", 11_000)) + " end", "elements"];
        yield return ["start\n\n" + string.Concat(Enumerable.Repeat("[link](#target)\n\n", 2_000)) + "end", "controls"];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Documents))]
    public void Exceeding_a_markdown_budget_shows_the_entire_source_and_a_visible_reason(string source, string budget)
    {
        Assert.True(source.Length < PluginLimits.PreviewLiveMaxChars);
        var plan = PreviewRenderer.PrepareMarkdown(source);
        Assert.Null(plan.Document);
        Assert.Contains(budget, plan.Problem);
        Assert.Equal(source, plan.Source);

        var body = PreviewRenderer.RenderMarkdown(plan, new PreviewRenderer.Ctx(new FontFamily("Consolas")));
        var controls = new[] { body }.Concat(body.GetLogicalDescendants().OfType<Control>()).ToList();
        Assert.Equal(source, Assert.Single(controls.OfType<SelectableTextBlock>()).Text);
        Assert.Contains(controls.OfType<TextBlock>(), block => block.Text == Loc.Get(K.PreviewMarkdownLimit, ("reason", plan.Problem!)));
        Assert.InRange(controls.Count, 2, 5);
    }

    [AvaloniaFact]
    public void A_wide_ragged_table_is_refused_without_building_its_rectangular_control_tree()
    {
        var small = MarkdownSyntax.Parse("|a|b|c|\n|---|---|---|\n|one|\n|two|\n");
        var table = Assert.Single(MarkdownSyntax.Walk(small).Select(entry => entry.Node).OfType<Table>());
        var widths = table.OfType<TableRow>().Select(row => row.Count).ToArray();
        output.WriteLine("Small ragged AST row widths: " + string.Join(",", widths));
        Assert.Equal(3, widths[0]);
        Assert.Equal(3, table.Count);

        const int columns = 500, rows = 100;
        string source = "|" + string.Concat(Enumerable.Repeat("header|", columns)) + "\n|"
            + string.Concat(Enumerable.Repeat("---|", columns)) + "\n"
            + string.Concat(Enumerable.Repeat("|one|\n", rows));
        var plan = PreviewRenderer.PrepareMarkdown(source);
        Assert.Null(plan.Document);
        Assert.NotEmpty(plan.Problem!);
        Assert.Equal(source, plan.Source);
    }
}

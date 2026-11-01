using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class MarkdownBudgetTests
{
    public static IEnumerable<object[]> Documents()
    {
        yield return [new string('>', 100) + " start\n\nend", "depth"];
        yield return ["start " + string.Concat(Enumerable.Repeat("*word* ", 6_000)) + " end", "elements"];
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
}

using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

internal static partial class PreviewRenderer
{
    internal sealed record MarkdownPlan(string Source, MarkdownDocument? Document, string? Problem,
        IReadOnlyDictionary<CodeBlock, Flowchart?> Diagrams, IReadOnlyDictionary<string, int> Targets, int[] Lines);

    // This pass creates no controls. Walk is iterative, including inline containers.
    internal static MarkdownPlan PrepareMarkdown(string source)
    {
        var diagrams = new Dictionary<CodeBlock, Flowchart?>();
        var lines = MarkdownSyntax.LineStarts(source);
        MarkdownPlan Refused(string reason) => new(source, null, reason, new Dictionary<CodeBlock, Flowchart?>(), new Dictionary<string, int>(), lines);
        MarkdownDocument document;
        try { document = MarkdownSyntax.Parse(source, PluginLimits.PreviewMarkdownMaxDepth); }
        catch (MarkdownSyntaxException) { return Refused($"Markdown could not be parsed within nesting depth {PluginLimits.PreviewMarkdownMaxDepth}"); }
        int nodes = 0, controls = 1;
        foreach (var (node, depth) in MarkdownSyntax.Walk(document))
        {
            if (++nodes > PluginLimits.PreviewMarkdownMaxNodes) return Refused($"more than {PluginLimits.PreviewMarkdownMaxNodes} Markdown elements");
            if (depth > PluginLimits.PreviewMarkdownMaxDepth) return Refused($"nesting depth exceeds {PluginLimits.PreviewMarkdownMaxDepth}");
            controls += node switch
            {
                ListItemBlock => 4,
                QuoteBlock or TableCell => 2,
                HeadingBlock => 2,
                ListBlock or Table or ParagraphBlock or Markdig.Syntax.HtmlBlock or ThematicBreakBlock => 1,
                Markdig.Syntax.CodeBlock => 2,
                LinkInline or AutolinkInline => 8,
                Markdig.Extensions.TaskLists.TaskList => 1,
                _ => 0,
            };
            if (node is FencedCodeBlock { Info: { } info } code && info.Equals("mermaid", StringComparison.OrdinalIgnoreCase))
            {
                var diagram = MermaidFlowchart.Parse(code.Lines.ToString(), PluginLimits.PreviewMermaidMaxNodes, PluginLimits.PreviewMermaidMaxEdges);
                diagrams[code] = diagram;
                controls += diagram is null ? 3 : 2 + diagram.Nodes.Count * 3 + diagram.Edges.Count * 3;
            }
            if (controls > PluginLimits.PreviewMarkdownMaxControls) return Refused($"more than {PluginLimits.PreviewMarkdownMaxControls} preview controls");
        }
        return new(source, document, null, diagrams, MarkdownSyntax.HeadingTargets(document), lines);
    }

    internal static Control RenderMarkdown(MarkdownPlan plan, Ctx ctx)
    {
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 6) };
        if (plan.Document is null)
        {
            ctx.Anchors?.Clear();
            body.Children.Add(new TextBlock { Text = Loc.Get(K.PreviewMarkdownLimit, ("reason", plan.Problem ?? "")), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new SelectableTextBlock { Text = plan.Source, TextWrapping = TextWrapping.Wrap, FontFamily = ctx.Mono });
        }
        else
            foreach (var block in plan.Document) body.Children.Add(MarkdownBlock(block, plan, ctx));
        return body;
    }

    private static Control MarkdownBlock(Block block, MarkdownPlan plan, Ctx ctx, FontWeight? inherited = null)
    {
        Control control;
        switch (block)
        {
            case ListBlock list:
                var items = new StackPanel { Spacing = list.IsLoose ? 8 : 2 };
                long number = long.TryParse(list.OrderedStart, out var first) ? first : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    var marker = new TextBlock { Text = list.IsOrdered ? number++ + "." : "•", Margin = new Thickness(0, 0, 8, 0) };
                    var contents = new StackPanel { Spacing = list.IsLoose ? 8 : 2 };
                    foreach (var child in item) contents.Children.Add(MarkdownBlock(child, plan, ctx));
                    Grid.SetColumn(contents, 1);
                    row.Children.Add(marker);
                    row.Children.Add(contents);
                    items.Children.Add(row);
                }
                control = items;
                break;
            case QuoteBlock quote:
                var quoted = new StackPanel { Spacing = 8 };
                foreach (var child in quote) quoted.Children.Add(MarkdownBlock(child, plan, ctx));
                var border = new Border { Child = quoted };
                border.Classes.Add("previewquote");
                control = border;
                break;
            case Table table:
                control = MarkdownTable(table, plan, ctx);
                break;
            case CodeBlock code:
                if (plan.Diagrams.TryGetValue(code, out var diagram))
                {
                    if (diagram is not null) control = DrawFlowchart(diagram);
                    else
                        control = new StackPanel { Spacing = 4, Children = {
                            new SelectableTextBlock { Text = Loc.Get(K.PreviewMermaidUnsupported), TextWrapping = TextWrapping.Wrap },
                            CodeBlock(code.Lines.ToString(), ctx) } };
                }
                else control = CodeBlock(code.Lines.ToString(), ctx);
                break;
            case ThematicBreakBlock:
                control = HorizontalRule();
                break;
            case HtmlBlock html:
                control = new SelectableTextBlock { Text = html.Lines.ToString(), TextWrapping = TextWrapping.Wrap };
                break;
            case LeafBlock leaf:
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                bool heading = leaf is HeadingBlock;
                if (leaf is HeadingBlock h) text.Classes.Add("previewhead" + h.Level);
                MarkdownInlines(leaf.Inline, text.Inlines!, heading ? FontWeight.SemiBold : inherited ?? FontWeight.Normal, FontStyle.Normal, plan, ctx);
                control = text;
                break;
            default:
                control = new SelectableTextBlock { Text = block.Span.Start >= 0 ? plan.Source.Substring(block.Span.Start, block.Span.Length) : "", TextWrapping = TextWrapping.Wrap };
                break;
        }
        if (block.Span.Start >= 0)
        {
            control.Tag = block.Span;
            ctx.Anchors?.Add(new Anchor(block.Line, control));
            ctx.Anchors?.Add(new Anchor(MarkdownSyntax.LineAt(plan.Lines, block.Span.End) + 1, control, End: true));
        }
        return control;
    }

    private static Grid MarkdownTable(Table table, MarkdownPlan plan, Ctx ctx)
    {
        int columns = table.Max(row => ((TableRow)row).Count);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(grid, Loc.Get(K.PreviewTableName, ("rows", table.Count - 1), ("columns", columns)));
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < table.Count; r++)
        {
            var row = (TableRow)table[r];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < columns; c++)
            {
                var contents = new StackPanel { MaxWidth = 400 };
                if (c < row.Count)
                    foreach (var child in (TableCell)row[c]) contents.Children.Add(MarkdownBlock(child, plan, ctx, row.IsHeader ? FontWeight.SemiBold : FontWeight.Normal));
                var align = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                foreach (var text in contents.Children.OfType<SelectableTextBlock>())
                    text.TextAlignment = align switch { TableColumnAlign.Center => TextAlignment.Center, TableColumnAlign.Right => TextAlignment.Right, _ => TextAlignment.Left };
                var cell = new Border { Child = contents };
                cell.Classes.Add("previewcell");
                if (row.IsHeader) cell.Classes.Add("previewcellhead");
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return grid;
    }
}

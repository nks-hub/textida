using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

/// <summary>The HTML half of <see cref="PreviewRenderer"/>: the nodes of <see cref="HtmlSubset"/> become the same controls as Markdown. Attributes other than <c>href</c>, <c>src</c>, <c>alt</c> and <c>class</c> are ignored.</summary>
internal static partial class PreviewRenderer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Where a run of text-level nodes stands: whether the last thing written was a space or a line break, and the last text written, so spaces collapse across tags and the ends are trimmed.</summary>
    private sealed class Flow
    {
        public bool AfterSpace = true;
        public Run? LastText;
    }

    private static void HtmlBlocks(IReadOnlyList<HtmlNode> nodes, Avalonia.Controls.Controls into, Ctx ctx, bool top)
    {
        var run = new List<HtmlNode>();
        void Flush()
        {
            if (run.Any(n => n.Tag is not null || n.Literal || n.Text.Trim().Length > 0))
            {
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                HtmlText(run, text, ctx);
                Put(into, text, run[0].Line, ctx, top);
            }
            run.Clear();
        }
        foreach (var node in nodes)
        {
            if (node.Tag is { } tag && HtmlSubset.IsBlock(tag))
            {
                Flush();
                Put(into, HtmlBlock(node, ctx), node.Line, ctx, top);
            }
            else run.Add(node);
        }
        Flush();
    }

    /// <summary>Writes text-level nodes into a text block, collapsing white space the way a browser does and trimming both ends.</summary>
    private static void HtmlText(IReadOnlyList<HtmlNode> nodes, SelectableTextBlock into, Ctx ctx, FontWeight weight = FontWeight.Normal)
    {
        var flow = new Flow();
        HtmlInlines(nodes, into.Inlines!, weight, FontStyle.Normal, mono: false, ctx, flow);
        if (flow.LastText is { } last) last.Text = last.Text!.TrimEnd(' ');
    }

    private static void HtmlInlines(IReadOnlyList<HtmlNode> nodes, InlineCollection into, FontWeight weight, FontStyle style, bool mono, Ctx ctx, Flow flow)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is null)
            {
                string text = node.Literal ? node.Text : Whitespace().Replace(node.Text, " ");
                if (!node.Literal && flow.AfterSpace) text = text.TrimStart(' ');
                if (text.Length == 0) continue;
                var run = new Run(text) { FontWeight = weight, FontStyle = style };
                if (mono) run.FontFamily = ctx.Mono;
                into.Add(run);
                flow.LastText = node.Literal ? null : run;
                flow.AfterSpace = !node.Literal && text.EndsWith(' ');
                continue;
            }
            flow.LastText = null;
            switch (node.Tag)
            {
                case "br":
                    into.Add(new LineBreak());
                    flow.AfterSpace = true;
                    break;
                case "img":
                    AddImage(into, node.Attribute("src"), node.Attribute("alt"), ctx, weight, style);
                    flow.AfterSpace = false;
                    break;
                case "a" when node.Attribute("href").Length > 0:
                    string label = Whitespace().Replace(node.TextContent(), " ").Trim();
                    AddLink(into, label.Length > 0 ? label : node.Attribute("href"), node.Attribute("href"), ctx, weight, style);
                    flow.AfterSpace = false;
                    break;
                case "em" or "i":
                    HtmlInlines(node.Children, into, weight, FontStyle.Italic, mono, ctx, flow);
                    break;
                case "strong" or "b":
                    HtmlInlines(node.Children, into, FontWeight.Bold, style, mono, ctx, flow);
                    break;
                case "code":
                    HtmlInlines(node.Children, into, weight, style, mono: true, ctx, flow);
                    break;
                default: // an anchor without a target, or a block tag in a place the layout has no room for it: its content is written in line
                    HtmlInlines(node.Children, into, weight, style, mono, ctx, flow);
                    break;
            }
        }
    }

    private static Control HtmlBlock(HtmlNode node, Ctx ctx)
    {
        switch (node.Tag)
        {
            case "ul" or "ol":
                return HtmlList(node, ctx);
            case "table":
                return HtmlTable(node, ctx);
            case "pre":
                return HtmlPre(node, ctx);
            case "blockquote":
                var inner = new StackPanel { Spacing = 8 };
                HtmlBlocks(node.Children, inner.Children, ctx, top: false);
                var quote = new Border { Child = inner };
                quote.Classes.Add("previewquote");
                return quote;
            case "hr":
                return HorizontalRule();
            default: // headings and paragraphs
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                if (node.Tag is ['h', var level]) text.Classes.Add("previewhead" + level);
                HtmlText(node.Children, text, ctx);
                return text;
        }
    }

    private static StackPanel HtmlList(HtmlNode list, Ctx ctx)
    {
        var panel = new StackPanel { Spacing = 2 };
        int number = 0;
        foreach (var item in list.Children)
        {
            if (item.Tag != "li")
            {
                HtmlBlocks([item], panel.Children, ctx, top: false);
                continue;
            }
            number++;
            int split = item.Children.FindIndex(c => c.Tag is { } tag && HtmlSubset.IsBlock(tag));
            if (split < 0) split = item.Children.Count;
            var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
            text.Inlines!.Add(new Run(list.Tag == "ol" ? number + ". " : "• "));
            var flow = new Flow { AfterSpace = true };
            HtmlInlines(item.Children.Take(split).ToList(), text.Inlines, FontWeight.Normal, FontStyle.Normal, mono: false, ctx, flow);
            if (flow.LastText is { } last) last.Text = last.Text!.TrimEnd(' ');
            panel.Children.Add(text);
            if (split == item.Children.Count) continue;
            var nested = new StackPanel { Spacing = 2, Margin = new Thickness(20, 0, 0, 0) };
            HtmlBlocks(item.Children.Skip(split).ToList(), nested.Children, ctx, top: false);
            panel.Children.Add(nested);
        }
        return panel;
    }

    /// <summary>A table as a grid. A cell holds text-level content only and spans one column (shortcut: <c>colspan</c> and <c>rowspan</c> are not applied; add them when a page needs them).</summary>
    private static Control HtmlTable(HtmlNode table, Ctx ctx)
    {
        var rows = new List<List<HtmlNode>>();
        void Collect(HtmlNode parent)
        {
            foreach (var child in parent.Children)
            {
                if (child.Tag == "tr") rows.Add([.. child.Children.Where(c => c.Tag is "td" or "th")]);
                else if (child.Tag is "thead" or "tbody" or "tfoot") Collect(child);
            }
        }
        Collect(table);
        rows.RemoveAll(r => r.Count == 0);
        if (rows.Count == 0)
        {
            var empty = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
            HtmlText(table.Children, empty, ctx);
            return empty;
        }
        int columns = rows.Max(r => r.Count);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(grid, Loc.Get(K.PreviewTableName, ("rows", rows.Count), ("columns", columns)));
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            bool head = rows[r].All(c => c.Tag == "th");
            for (int c = 0; c < columns; c++)
            {
                var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400 };
                if (c < rows[r].Count) HtmlText(rows[r][c].Children, text, ctx, head ? FontWeight.SemiBold : FontWeight.Normal);
                grid.Children.Add(Cell(text, head, r, c));
            }
        }
        return grid;
    }

    private static Control HtmlPre(HtmlNode pre, Ctx ctx)
    {
        string code = pre.TextContent();
        if (code.StartsWith('\n')) code = code[1..];
        code = code.TrimEnd('\n');
        var classes = pre.Attribute("class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Concat(pre.Children.Where(c => c.Tag == "code").SelectMany(c => c.Attribute("class").Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        return classes.Any(c => c is "mermaid" or "language-mermaid") ? Diagram(code, ctx) : CodeBlock(code, ctx);
    }
}

using Markdig.Extensions.Alerts;
using Markdig.Extensions.Footnotes;
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
        catch (MarkdownSyntaxException e) { return Refused(MarkdownMessages.Reason(e)); }
        int nodes = 0, controls = 1;
        foreach (var (node, depth) in MarkdownSyntax.Walk(document))
        {
            if (++nodes > PluginLimits.PreviewMarkdownMaxNodes) return Refused(Loc.Get(K.PreviewMarkdownNodeLimit, ("limit", PluginLimits.PreviewMarkdownMaxNodes)));
            if (depth > PluginLimits.PreviewMarkdownMaxDepth) return Refused(Loc.Get(K.MarkdownNestingLimit, ("limit", PluginLimits.PreviewMarkdownMaxDepth)));
            controls += node switch
            {
                ListItemBlock or Footnote => 4,
                AlertBlock => 5,
                QuoteBlock or TableCell or FootnoteGroup => 2,
                HeadingBlock => 3,
                FootnoteLink => 8,
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
            if (controls > PluginLimits.PreviewMarkdownMaxControls) return Refused(Loc.Get(K.PreviewMarkdownControlLimit, ("limit", PluginLimits.PreviewMarkdownMaxControls)));
        }
        return new(source, document, null, diagrams, MarkdownSyntax.HeadingTargets(document), lines);
    }

    internal static Control RenderMarkdown(MarkdownPlan plan, Ctx ctx)
    {
        var body = new StackPanel();
        body.Classes.Add("previewpage");
        if (plan.Document is null)
        {
            ctx.Anchors?.Clear();
            body.Children.Add(new TextBlock { Text = Loc.Get(K.PreviewMarkdownLimit, ("reason", plan.Problem ?? "")), TextWrapping = TextWrapping.Wrap });
            var source = new SelectableTextBlock { Text = plan.Source, TextWrapping = TextWrapping.Wrap, FontFamily = ctx.Mono };
            body.Children.Add(source);
            ctx.Anchors?.Add(new Anchor(0, source));
            ctx.Anchors?.Add(new Anchor(plan.Lines.Length, source, End: true));
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
                var items = new StackPanel();
                items.Classes.Add("previewlist");
                items.Classes.Add(list.IsLoose ? "previewloose" : "previewtight");
                long number = long.TryParse(list.OrderedStart, out var first) ? first : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    var marker = new TextBlock { Text = list.IsOrdered ? number++ + "." : "•" };
                    marker.Classes.Add("previewmarker");
                    var contents = new StackPanel();
                    contents.Classes.Add(list.IsLoose ? "previewloose" : "previewtight");
                    foreach (var child in item) contents.Children.Add(MarkdownBlock(child, plan, ctx));
                    Grid.SetColumn(contents, 1);
                    row.Children.Add(marker);
                    row.Children.Add(contents);
                    items.Children.Add(row);
                }
                control = items;
                break;
            case AlertBlock alert:
                control = MarkdownAlert(alert, plan, ctx);
                break;
            case QuoteBlock quote:
                var quoted = new StackPanel();
                quoted.Classes.Add("previewblocks");
                foreach (var child in quote) quoted.Children.Add(MarkdownBlock(child, plan, ctx));
                var border = new Border { Child = quoted };
                border.Classes.Add("previewquote");
                control = border;
                break;
            case FootnoteGroup group:
                var notes = new StackPanel();
                notes.Classes.Add("previewfootnotes");
                notes.Children.Add(HorizontalRule());
                foreach (var child in group) notes.Children.Add(MarkdownBlock(child, plan, ctx));
                control = notes;
                break;
            case Footnote note:
                var entry = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                var noteNumber = new TextBlock { Text = note.Order + "." };
                noteNumber.Classes.Add("previewmarker");
                var noteBody = new StackPanel();
                noteBody.Classes.Add("previewblocks");
                foreach (var child in note) noteBody.Children.Add(MarkdownBlock(child, plan, ctx));
                Grid.SetColumn(noteBody, 1);
                entry.Children.Add(noteNumber);
                entry.Children.Add(noteBody);
                AutomationProperties.SetName(entry, Loc.Get(K.PreviewFootnoteName, ("number", note.Order)));
                control = entry;
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
                control = leaf is HeadingBlock { Level: <= 2 } ? HeadingWithRule(text, ((HeadingBlock)leaf).Level) : text;
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

    /// <summary>A heading of level 1 or 2 with the rule below it; the box carries the heading's class so the page and the outline find it by it.</summary>
    private static Border HeadingWithRule(SelectableTextBlock text, int level)
    {
        var box = new Border { Child = text };
        box.Classes.Add("previewheadbox");
        box.Classes.Add("previewhead" + level);
        return box;
    }

    private static readonly Dictionary<string, (string Key, string Icon)> AlertKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = (K.PreviewAlertNote, "M8,1.5 A6.5,6.5 0 1 0 8.01,1.5 Z M8,7 L8,11.5 M8,4.6 L8,4.9"),
        ["TIP"] = (K.PreviewAlertTip, "M6,12.5 L10,12.5 M6.5,14.5 L9.5,14.5 M5,9 A3.5,3.5 0 1 1 11,9 C10.3,9.8 10,10.5 10,11.5 L6,11.5 C6,10.5 5.7,9.8 5,9 Z"),
        ["IMPORTANT"] = (K.PreviewAlertImportant, "M2,2.5 L14,2.5 L14,11 L8.5,11 L5,14 L5,11 L2,11 Z M8,5 L8,8 M8,9.3 L8,9.6"),
        ["WARNING"] = (K.PreviewAlertWarning, "M8,2 L14.5,13.5 L1.5,13.5 Z M8,6.5 L8,10 M8,11.7 L8,12"),
        ["CAUTION"] = (K.PreviewAlertCaution, "M5.2,2 L10.8,2 L14,5.2 L14,10.8 L10.8,14 L5.2,14 L2,10.8 L2,5.2 Z M8,5 L8,8.5 M8,10.5 L8,10.9"),
    };

    /// <summary>A GitHub alert: a bar in the colour of its kind, an icon and a title (a kind that is not one of the five keeps its own word as the title), then its blocks.</summary>
    private static Border MarkdownAlert(AlertBlock alert, MarkdownPlan plan, Ctx ctx)
    {
        string kind = alert.Kind.ToString().ToUpperInvariant();
        bool known = AlertKinds.TryGetValue(kind, out var look);
        string title = known ? Loc.Get(look.Key) : alert.Kind.ToString();
        var icon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(known ? look.Icon : AlertKinds["NOTE"].Icon), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.Classes.Add("previewalerticon");
        var heading = new TextBlock { Text = title };
        heading.Classes.Add("previewalerttitle");
        var head = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, heading } };
        head.Classes.Add("previewalerthead");
        AutomationProperties.SetName(head, title);
        var content = new StackPanel();
        content.Classes.Add("previewblocks");
        content.Children.Add(head);
        foreach (var child in alert) content.Children.Add(MarkdownBlock(child, plan, ctx));
        var box = new Border { Child = content };
        box.Classes.Add("previewquote");
        box.Classes.Add("previewalert");
        box.Classes.Add("previewalert" + (known ? kind.ToLowerInvariant() : "note"));
        return box;
    }

    private static Grid MarkdownTable(Table table, MarkdownPlan plan, Ctx ctx)
    {
        int columns = table.Max(row => ((TableRow)row).Count);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.Classes.Add("previewtable");
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
                else if (r % 2 == 0) cell.Classes.Add("previewcellalt");
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return grid;
    }
}

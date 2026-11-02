using System.Text;
using Markdig;
using Markdig.Extensions.AutoLinks;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace NksHub.Textida.Core.Markdown;

public enum MarkdownSyntaxFailure { Parse, Nesting, GeneratedTableCells }

public sealed class MarkdownSyntaxException(string message, Exception? inner = null) : FormatException(message, inner)
{
    public MarkdownSyntaxFailure Failure { get; } = MarkdownSyntaxFailure.Parse;
    public int? Limit { get; }

    public MarkdownSyntaxException(MarkdownSyntaxFailure failure, int? limit = null, Exception? inner = null)
        : this(failure switch
        {
            MarkdownSyntaxFailure.Nesting => $"Markdown nesting exceeds the parser limit of {limit}.",
            MarkdownSyntaxFailure.GeneratedTableCells => $"Markdown tables would add more than {limit} empty cells.",
            _ => "Markdown could not be parsed.",
        }, inner)
    {
        Failure = failure;
        Limit = limit;
    }
}

public static class MarkdownSyntax
{
    public static MarkdownDocument Parse(string source, int? maximumDepth = null)
    {
        // GFM: tables, task lists, ~~strikethrough~~, bare-URL autolinks, footnotes, [!NOTE] alerts and :emoji: shortcodes (no ":)" smileys)
        var builder = new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseAutoLinks(new AutoLinkOptions { UseHttpsForWWWLinks = true }).UseFootnotes().UseAlertBlocks().UseEmojiAndSmiley(false).UsePreciseSourceLocation();
        builder.Extensions.Add(new TableExpansionLimit());
        if (maximumDepth is { } depth) builder.MaximumNestingDepth = depth;
        try { return Markdig.Markdown.Parse(source, builder.Build()); }
        catch (ArgumentException e) { throw new MarkdownSyntaxException(MarkdownSyntaxFailure.Nesting, builder.MaximumNestingDepth, e); }
        catch (InvalidOperationException e) { throw new MarkdownSyntaxException(MarkdownSyntaxFailure.Parse, inner: e); }
    }

    public static MarkdownDocument Parse(IReadOnlyList<string> lines) => Parse(string.Join('\n', lines));

    // Both block and inline containers take part in the budget and source mapping.
    public static IEnumerable<(MarkdownObject Node, int Depth)> Walk(MarkdownObject root)
    {
        var pending = new Stack<(MarkdownObject Node, int Depth)>();
        pending.Push((root, 0));
        while (pending.TryPop(out var entry))
        {
            yield return entry;
            switch (entry.Node)
            {
                case ContainerBlock blocks:
                    for (int i = blocks.Count - 1; i >= 0; i--) pending.Push((blocks[i], entry.Depth + 1));
                    break;
                case LeafBlock { Inline: { } inline }:
                    pending.Push((inline, entry.Depth + 1));
                    break;
                case ContainerInline inlines:
                    for (var child = inlines.LastChild; child is not null; child = child.PreviousSibling) pending.Push((child, entry.Depth + 1));
                    break;
            }
        }
    }

    public static string Text(ContainerInline? inline)
    {
        if (inline is null) return "";
        var text = new StringBuilder();
        foreach (var (node, _) in Walk(inline))
            switch (node)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case HtmlEntityInline entity: text.Append(entity.Transcoded); break;
                case HtmlInline html: text.Append(html.Tag); break;
                case LineBreakInline: text.Append(' '); break;
                case AutolinkInline link: text.Append(link.Url); break;
                case TaskList task: text.Append(task.Checked ? "[x] " : "[ ] "); break;
            }
        return text.ToString();
    }

    public static IReadOnlyList<MarkdownHeading> Headings(MarkdownDocument document) =>
        [.. Walk(document).Select(x => x.Node).OfType<HeadingBlock>().Select(h => new MarkdownHeading(h.Level, Text(h.Inline), h.Line))];

    public static IReadOnlyDictionary<string, int> HeadingTargets(MarkdownDocument document)
    {
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var heading in Headings(document)) targets.Add(MarkdownTools.Slug(heading.Title, seen), heading.Line);
        return targets;
    }

    public static IReadOnlyList<(int First, int Last)> CodeRanges(MarkdownDocument document, IReadOnlyList<string> lines)
    {
        var starts = LineStarts(lines);
        return [.. Walk(document).Select(x => x.Node).OfType<CodeBlock>().Select(b => (b.Line, LineAt(starts, b.Span.End)))];
    }

    public static int[] LineStarts(IReadOnlyList<string> lines)
    {
        var starts = new int[lines.Count];
        for (int i = 1; i < starts.Length; i++) starts[i] = checked(starts[i - 1] + lines[i - 1].Length + 1);
        return starts;
    }

    public static int[] LineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
            else if (source[i] == '\n') starts.Add(i + 1);
        }
        return [.. starts];
    }

    public static int LineAt(int[] starts, int offset)
    {
        int index = Array.BinarySearch(starts, offset);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    public static (int First, int Last)? TableAt(MarkdownDocument document, IReadOnlyList<string> lines, int line)
    {
        var starts = LineStarts(lines);
        foreach (var table in Walk(document).Select(x => x.Node).OfType<Table>())
        {
            int last = LineAt(starts, table.Span.End);
            if (line >= table.Line && line <= last) return (table.Line, last);
        }
        return null;
    }
}

using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace NksHub.Textida.Core.Markdown;

internal sealed class TableExpansionLimit : IMarkdownExtension
{
    private const int MaxAddedCells = 20_000;

    internal static void CheckAddedCells(long count)
    {
        if (count > MaxAddedCells)
            throw new MarkdownSyntaxException(MarkdownSyntaxFailure.GeneratedTableCells, MaxAddedCells);
    }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        var parser = pipeline.InlineParsers.FindExact<PipeTableParser>()!;
        var lineBreak = pipeline.InlineParsers.FindExact<LineBreakInlineParser>()!;
        pipeline.InlineParsers[pipeline.InlineParsers.IndexOf(parser)] = new GuardedPipeTableParser(lineBreak, parser.Options);
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer) { }

    private sealed class GuardedPipeTableParser(LineBreakInlineParser lineBreak, PipeTableOptions options)
        : PipeTableParser(lineBreak, options), IPostInlineProcessor
    {
        private readonly HashSet<Table> checkedTables = [];
        private long addedCells;

        bool IPostInlineProcessor.PostProcess(InlineProcessor state, Inline? root, Inline? lastChild, int index, bool isFinalProcessing)
        {
            // Markdig 1.4.0 reprocesses each cell after building the table, before Normalize allocates padding cells.
            if (root is ContainerInline { ParentBlock.Parent: TableCell { Parent: TableRow { Parent: Table table } } }
                && checkedTables.Add(table))
            {
                int columns = Options.UseHeaderForColumnCount ? ((TableRow)table[0]).Count : 0;
                if (!Options.UseHeaderForColumnCount)
                    foreach (TableRow row in table) columns = Math.Max(columns, row.Count);

                foreach (TableRow row in table) addedCells += Math.Max(0, columns - row.Count);
                CheckAddedCells(addedCells);
            }
            return base.PostProcess(state, root, lastChild, index, isFinalProcessing);
        }
    }
}

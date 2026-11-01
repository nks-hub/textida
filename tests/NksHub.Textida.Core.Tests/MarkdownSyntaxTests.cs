using Markdig.Extensions.Tables;
using Markdig.Syntax;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.Core.Tests;

public class MarkdownSyntaxTests
{
    private static string Sparse(int columns, int rows) => "|" + string.Concat(Enumerable.Repeat("header|", columns)) + "\n|"
        + string.Concat(Enumerable.Repeat("---|", columns)) + "\n" + string.Concat(Enumerable.Repeat("|one|\n", rows));

    private static IEnumerable<Table> Tables(MarkdownDocument document) => MarkdownSyntax.Walk(document).Select(entry => entry.Node).OfType<Table>();

    [Theory]
    [InlineData(201, true)]
    [InlineData(202, false)]
    public void Table_normalization_allows_twenty_thousand_generated_cells_and_refuses_more(int columns, bool allowed)
    {
        string source = Sparse(columns, 100);
        if (!allowed)
        {
            Assert.Throws<MarkdownSyntaxException>(() => MarkdownSyntax.Parse(source));
            return;
        }
        var table = Assert.Single(Tables(MarkdownSyntax.Parse(source)));
        Assert.Equal(101, table.Count);
        Assert.All(table.OfType<TableRow>(), row => Assert.Equal(columns, row.Count));
    }

    [Fact]
    public void A_sparse_table_inside_a_fence_stays_literal_without_spending_the_cell_budget()
    {
        string sparse = Sparse(500, 100);
        var document = MarkdownSyntax.Parse("~~~\n" + sparse + "~~~\n");
        Assert.Empty(Tables(document));
        var code = Assert.Single(MarkdownSyntax.Walk(document).Select(entry => entry.Node).OfType<CodeBlock>());
        Assert.Contains(sparse.TrimEnd('\n'), code.Lines.ToString());
    }

    [Fact]
    public async Task Generated_cells_are_counted_across_tables_but_not_across_separate_or_concurrent_parses()
    {
        string table = Sparse(101, 100);
        string pair = table + "\n" + table;
        Assert.Equal(2, Tables(MarkdownSyntax.Parse(pair)).Count());
        Assert.Throws<MarkdownSyntaxException>(() => MarkdownSyntax.Parse(pair + "\n" + table));
        Assert.Equal(2, Tables(MarkdownSyntax.Parse(pair)).Count());
        var documents = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => MarkdownSyntax.Parse(pair))));
        Assert.All(documents, document => Assert.Equal(2, Tables(document).Count()));
    }

    [Fact]
    public void A_dense_table_keeps_all_of_its_explicit_cells_in_core_operations()
    {
        const int columns = 201, rows = 100;
        string source = "|" + string.Concat(Enumerable.Repeat("header|", columns)) + "\n|"
            + string.Concat(Enumerable.Repeat("---|", columns)) + "\n"
            + string.Concat(Enumerable.Repeat("|" + string.Concat(Enumerable.Repeat("value|", columns)) + "\n", rows));
        var table = Assert.Single(Tables(MarkdownSyntax.Parse(source)));
        Assert.Equal(rows + 1, table.Count);
        Assert.All(table.OfType<TableRow>(), row => Assert.Equal(columns, row.Count));
        Assert.Equal(rows * columns, MarkdownSyntax.Walk(table).Select(entry => entry.Node).OfType<TableCell>()
            .Count(cell => cell.OfType<ParagraphBlock>().Any(block => MarkdownSyntax.Text(block.Inline) == "value")));
    }

    [Fact]
    public void Direct_formatting_and_repair_without_a_separator_refuse_expansion_before_creating_output()
    {
        string[] rows = Sparse(500, 100).TrimEnd('\n').Split('\n');
        Assert.Throws<MarkdownSyntaxException>(() => MarkdownTools.FormatTable(rows));
        string[] withoutSeparator = [rows[0], .. rows.Skip(2)];
        Assert.Throws<MarkdownSyntaxException>(() => MarkdownTools.Repair(withoutSeparator, 1));
    }
}

using Avalonia;
using Avalonia.Headless.XUnit;
using Markdig.Extensions.Tables;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Tests;

/// <summary>P9.3.4: the reader has no limit on the size of a document; only the parser keeps its guards (nesting, generated table cells), and then the source is shown below the reason.</summary>
[Collection("LocState")]
public class MarkdownBudgetTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Large()
    {
        yield return ["start " + string.Concat(Enumerable.Repeat("`word` ", 11_000)) + " end", 1];
        yield return ["start\n\n" + string.Concat(Enumerable.Repeat("[link](#target)\n\n", 2_000)) + "end", 2_002];
        yield return [string.Concat(Enumerable.Range(1, 12_000).Select(i => $"- item {i}\n")) + "\nend", 12_001];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Large))]
    public void A_document_of_many_thousand_elements_is_rendered_and_not_replaced_by_its_source(string source, int blocks)
    {
        Assert.True(source.Length < PluginLimits.PreviewLiveMaxChars);
        var doc = MarkdownReaderModel.Build(source);
        Assert.Null(doc.Problem);
        Assert.Equal(blocks, doc.Blocks.Count);
        Assert.DoesNotContain(doc.Blocks, b => b.Kind == ReaderBlockKind.Code);
        Assert.EndsWith("end", doc.PlainText);
        using var host = new ReaderHost(source);
        Assert.InRange(host.Reader.LaidCount, 1, 200);
    }

    [AvaloniaFact]
    public void A_table_of_five_thousand_rows_is_laid_out_row_by_row()
    {
        string source = "| n | name |\n|---:|---|\n" + string.Concat(Enumerable.Range(1, 5_000).Select(i => $"| {i} | row {i} |\n"));
        using var host = new ReaderHost(source);
        var rows = host.Doc.Blocks.Where(b => b.Kind == ReaderBlockKind.TableRow).ToList();
        Assert.Equal(5_001, rows.Count);
        Assert.InRange(host.Reader.LaidCount, 1, 200);
        host.Scroller.Offset = new Vector(0, host.Scroller.Extent.Height);
        host.Settle();
        int last = host.Doc.Blocks.Count - 1;
        Assert.Equal(last, host.Reader.Realized.Last);
        Assert.Equal("5000\trow 5000", host.Doc.Blocks[last].Text);
        Assert.Equal(2, host.Doc.Blocks[last].Cells.Length);
        Assert.True(host.Reader.LayoutOf(last, 0)!.TextLines[0].Start > 0, "the number column is aligned right");
    }

    [AvaloniaFact]
    public void Nesting_beyond_the_parser_limit_shows_the_reason_above_the_whole_source()
    {
        string source = new string('>', 100) + " start\n\nend";
        var doc = MarkdownReaderModel.Build(source);
        Assert.NotNull(doc.Problem);
        Assert.Contains(PluginLimits.PreviewMarkdownMaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture), doc.Problem);
        Assert.Equal(Loc.Get(K.PreviewMarkdownLimit, ("reason", doc.Problem!)), doc.Blocks[0].Text);
        Assert.Equal(source, string.Join("\n", doc.Blocks.Where(b => b.Kind == ReaderBlockKind.Code).Select(b => b.Text)));
    }

    [AvaloniaFact]
    public void A_wide_ragged_table_is_refused_by_the_parser_guard_and_shown_as_source()
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
        var doc = MarkdownReaderModel.Build(source);
        Assert.NotEmpty(doc.Problem!);
        Assert.Equal(source.TrimEnd('\n'), string.Join("\n", doc.Blocks.Where(b => b.Kind == ReaderBlockKind.Code).Select(b => b.Text)).TrimEnd('\n'));
    }
}

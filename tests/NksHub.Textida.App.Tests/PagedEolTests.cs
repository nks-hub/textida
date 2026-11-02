using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>The line ending style of a paged document comes from the whole file, counted while its line index is built, and follows edits.</summary>
public class PagedEolTests : IDisposable
{
    private const int Rows = 300_000;
    private const int OddRow = 250_000; // far behind the first 64 KiB

    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedEolTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    private static byte[] Text(string? oddEnd) =>
        Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, Rows).Select(i => $"line {i}{(i == OddRow && oddEnd is not null ? oddEnd : "\n")}")));

    private DocTab OpenPaged(byte[] content, string name = "big.log")
    {
        var tab = _w.OpenFile(_temp.Bytes(name, content), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        return tab;
    }

    [AvaloniaFact]
    public void One_crlf_line_after_the_first_block_makes_the_document_mixed_once_indexed()
    {
        var tab = OpenPaged(Text("\r\n"));
        for (int i = 0; i < 100 && tab.Doc.Eol.Style != EolStyle.Mixed; i++)
        {
            Thread.Sleep(20);
            Dispatcher.UIThread.RunJobs(); // the completion of the index refreshes the status by itself
        }
        Assert.Equal(new EolStats(Rows - 1, 1, 0), tab.Doc.Eol);
    }

    [AvaloniaFact]
    public void A_uniform_file_stays_uniform()
    {
        var lf = OpenPaged(Text(null));
        lf.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows, 0, 0), lf.Doc.Eol);
        var crlf = OpenPaged(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, Rows).Select(i => $"line {i}\r\n"))), "crlf.log");
        crlf.Doc.FlushEol();
        Assert.Equal(new EolStats(0, Rows, 0), crlf.Doc.Eol);
    }

    [AvaloniaFact]
    public void Edits_that_add_or_remove_the_odd_ending_change_the_result()
    {
        var tab = OpenPaged(Text("\r\n"));
        var doc = tab.Doc.Doc;
        tab.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows - 1, 1, 0), tab.Doc.Eol);

        // a CR in front of the final LF turns that line into a second CRLF
        doc.Insert(doc.ByteLength - 1, "\r"u8.ToArray());
        tab.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows - 2, 2, 0), tab.Doc.Eol);

        // a lone CR at the very start, followed by text
        doc.Insert(0, "\r"u8.ToArray());
        tab.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows - 2, 2, 1), tab.Doc.Eol);

        // removing the LF of the odd CRLF line leaves a lone CR instead
        long oddEnd = Encoding.UTF8.GetByteCount(string.Concat(Enumerable.Range(0, OddRow + 1).Select(i => $"line {i}\n"))) + 1; // +1: the inserted CR before it
        Assert.Equal((byte)'\n', doc.ReadBytes(new ByteRange(oddEnd, oddEnd + 1))[0]);
        doc.Delete(new ByteRange(oddEnd, oddEnd + 1));
        tab.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows - 2, 1, 2), tab.Doc.Eol);
    }

    [AvaloniaFact]
    public void Removing_the_only_odd_line_ending_makes_the_document_uniform_again()
    {
        var tab = OpenPaged(Text("\r\n"));
        var doc = tab.Doc.Doc;
        long cr = Encoding.UTF8.GetByteCount(string.Concat(Enumerable.Range(0, OddRow).Select(i => $"line {i}\n"))) + Encoding.UTF8.GetByteCount($"line {OddRow}");
        Assert.Equal((byte)'\r', doc.ReadBytes(new ByteRange(cr, cr + 1))[0]);
        doc.Delete(new ByteRange(cr, cr + 1));
        tab.Doc.FlushEol();
        Assert.Equal(new EolStats(Rows, 0, 0), tab.Doc.Eol);
    }
}

using System.Runtime.InteropServices;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>The block cache of a mapped file (P3.1.9): pages read past the resident budget go back to the system and read the same again.</summary>
public unsafe partial class MappedResidencyTests
{
    private const int Mib = 1 << 20;

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)(i * 7 + i / 4096);
        return bytes;
    }

    private static void WaitUntil(Func<bool> done, string what)
    {
        for (int i = 0; i < 400 && !done(); i++) Thread.Sleep(25);
        Assert.True(done(), what);
    }

    [Fact]
    public void Reads_past_the_budget_are_given_back_and_read_the_same_again()
    {
        var content = Pattern(24 * Mib);
        using var f = new TempFile(content);
        using var file = MappedFile.Open(f.Path);
        file.ResidentBudget = 4 * Mib;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int at = 0; at < content.Length; at += Mib)
                Assert.True(file.GetSpan(at, Mib).SequenceEqual(content.AsSpan(at, Mib)), $"chunk at {at}, pass {pass}");
            WaitUntil(() => file.ResidentEstimate <= file.ResidentBudget, "the estimate is back under the budget");
        }
    }

    [Fact]
    public void Without_a_budget_every_chunk_read_is_counted_and_kept()
    {
        using var f = new TempFile(Pattern(6 * Mib + 10));
        using var file = MappedFile.Open(f.Path);
        _ = file.GetSpan(0, 10);
        _ = file.GetSpan(Mib - 1, 2); // across the edge of two chunks
        _ = file.GetSpanClamped(6 * Mib, long.MaxValue);
        Assert.Equal(3L * Mib, file.ResidentEstimate);
        Thread.Sleep(100);
        Assert.Equal(3L * Mib, file.ResidentEstimate);
    }

    [Fact]
    public void A_file_in_memory_is_never_trimmed()
    {
        using var file = MappedFile.FromMemory(Pattern(8 * Mib));
        file.ResidentBudget = Mib;
        for (int at = 0; at < 8 * Mib; at += Mib) _ = file.GetSpan(at, Mib);
        Assert.Equal(0, file.ResidentEstimate);
    }

    [Fact]
    public void Indexing_a_file_larger_than_the_budget_ends_under_it_with_the_right_line_count()
    {
        var line = Encoding.ASCII.GetBytes("a line of the block cache test\n");
        int lines = 20 * Mib / line.Length;
        using var f = new TempFile([.. Enumerable.Range(0, lines).SelectMany(_ => line)]);
        using var src = MappedFileSource.Open(f.Path, new MappedFileSourceOptions { IndexBlockSize = Mib, ResidentBudgetBytes = 2 * Mib });
        src.IndexTask.Wait();
        Assert.Equal((lines + 1L, true), src.LineIndexState);
        WaitUntil(() => src.File.ResidentEstimate <= 2 * Mib, "the estimate is back under the budget after the index");
        Assert.True(src.TryGetLineStart(lines - 1, out long last));
        Assert.Equal((long)(lines - 1) * line.Length, last);
    }

    [Fact]
    public void A_trim_takes_the_read_pages_out_of_the_working_set()
    {
        if (!OperatingSystem.IsWindows()) return; // the working set of single pages is read through a Windows call
        var content = Pattern(4 * Mib);
        using var f = new TempFile(content);
        using var file = MappedFile.Open(f.Path);
        Assert.Equal(content[Mib + 5], file.GetSpan(Mib, 4096)[5]);
        Assert.True(IsResident(file.GetSpan(Mib, 1)));
        file.Trim();
        Assert.Equal(0, file.ResidentEstimate);
        Assert.False(IsResident(file.GetSpan(Mib, 1)));
        Assert.Equal(content[Mib + 5], file.GetSpan(Mib, 4096)[5]); // faulted in again, the same bytes
        Assert.True(IsResident(file.GetSpan(Mib, 1)));
    }

    [Fact]
    public void A_sequential_scan_keeps_the_working_set_near_the_budget_where_a_scan_without_a_budget_fills_it()
    {
        if (!OperatingSystem.IsWindows()) return; // the working set of single pages is read through a Windows call
        const int size = 48 * Mib;
        var content = Pattern(size);
        using var f = new TempFile(content);

        long Scan(long budget, out long peak)
        {
            using var file = MappedFile.Open(f.Path);
            file.ResidentBudget = budget;
            byte* start;
            fixed (byte* p = file.GetSpan(0, 1)) start = p;
            peak = 0;
            for (int at = 0; at < size; at += Mib)
            {
                Assert.True(file.GetSpan(at, Mib).SequenceEqual(content.AsSpan(at, Mib)), $"chunk at {at}");
                Thread.Sleep(2); // the pages are given back on the thread pool; give it the time a real scroll leaves it
                peak = Math.Max(peak, ResidentBytes(start, size));
            }
            if (budget > 0) WaitUntil(() => ResidentBytes(start, size) <= budget + 2 * Mib, "the working set is back near the budget");
            return ResidentBytes(start, size);
        }

        Scan(0, out long withoutBudget);
        Assert.True(withoutBudget >= size * 3 / 4, $"without a budget the scan left {withoutBudget >> 20} MiB of {size >> 20} resident, so the measurement shows nothing");
        long end = Scan(4 * Mib, out long peak);
        Assert.True(peak <= 4 * Mib + 12 * Mib, $"the working set peaked at {peak >> 20} MiB with a budget of 4 MiB");
        Assert.True(end <= 6 * Mib, $"{end >> 20} MiB resident after the scan");
    }

    [Fact]
    public void A_search_over_a_paged_document_leaves_no_more_of_the_mapping_in_the_working_set_than_the_budget()
    {
        if (!OperatingSystem.IsWindows()) return; // the working set of single pages is read through a Windows call
        var line = Encoding.ASCII.GetBytes("2026-10-26 12:00:00 INFO request handled path=/api/items/42\n");
        var bytes = new byte[40 * Mib / line.Length * line.Length];
        for (int at = 0; at < bytes.Length; at += line.Length) line.CopyTo(bytes, at);
        using var f = new TempFile(bytes);

        long ResidentAfterSearch(long budget)
        {
            using var doc = Doc.Open(f.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TestFormat.Utf8.Format });
            var file = ((PagedBackend)doc.Backend).Paged.Source.File;
            byte* start;
            fixed (byte* p = file.GetSpan(0, 1)) start = p;
            file.Trim(); // opening read the file under the default budget; count from here
            file.ResidentBudget = budget;
            var settings = new SearchSettings { MaxResults = 10, StopAtLimit = false, ChunkBytes = 1 << 20, Parallelism = 4 };
            var (_, summary) = SearchWorker.CollectAsync(new SearchPattern("never-in-this-file"), new SearchScope([], [LiveBuffer.Of(doc)]), settings).GetAwaiter().GetResult();
            Assert.Equal((SearchEnd.Completed, 1L, 0L), (summary.End, summary.FilesScanned, summary.TotalMatches));
            if (budget > 0) WaitUntil(() => ResidentBytes(start, file.Length) <= budget + 2 * Mib, "the mapped pages are back under the budget after the search");
            return ResidentBytes(start, file.Length);
        }

        long without = ResidentAfterSearch(0);
        long with = ResidentAfterSearch(4 * Mib);
        Assert.True(with <= 6 * Mib, $"{with >> 20} MiB of the mapping resident after a search with a 4 MiB budget ({without >> 20} MiB without one)");
    }

    private static long ResidentBytes(byte* start, long length)
    {
        const int page = 4096;
        int pages = (int)((length + page - 1) / page);
        var infos = new WorkingSetExInformation[pages];
        for (int i = 0; i < pages; i++) infos[i].VirtualAddress = (nint)(start + (long)i * page);
        Assert.True(QueryWorkingSetEx(GetCurrentProcess(), ref infos[0], pages * sizeof(WorkingSetExInformation)));
        return infos.Count(i => (i.VirtualAttributes & 1) != 0) * (long)page;
    }

    private static bool IsResident(ReadOnlySpan<byte> at)
    {
        fixed (byte* p = at)
        {
            var info = new WorkingSetExInformation { VirtualAddress = (nint)p };
            Assert.True(QueryWorkingSetEx(GetCurrentProcess(), ref info, sizeof(WorkingSetExInformation)));
            return (info.VirtualAttributes & 1) != 0; // the Valid bit: the page is in the working set
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WorkingSetExInformation
    {
        public nint VirtualAddress;
        public nuint VirtualAttributes;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "K32QueryWorkingSetEx")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryWorkingSetEx(nint process, ref WorkingSetExInformation info, int size);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();
}

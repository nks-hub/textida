using System.Runtime.InteropServices;
using System.Text;

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

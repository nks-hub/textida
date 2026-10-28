using System.Diagnostics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Tests;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// The speeds the 50 GiB tests are planned with, measured on the machine they run on: the generator of <see cref="HugeText"/>, SHA-256, creating a file of 50 GiB with preallocation, writing 4 GiB
/// and then 24 GiB (the second is more than the memory of the machine, so it shows the speed of the disk and not of the file cache), reading, and opening a document, which reads the whole
/// file for its fingerprint. Each test of the machine gets 20 minutes, so a plan of several passes over 50 GiB has to know what a pass costs. Runs only where <c>TEXTIDA_HUGE=1</c> is set.
/// </summary>
[Trait("Category", "Perf")]
public sealed class HugeCalibrationTests(ITestOutputHelper output)
{
    private void Say(string line)
    {
        output.WriteLine(line);
        HugeText.Log(line);
    }

    private static string Rate(long bytes, Stopwatch clock) => $"{bytes / 1048576.0 / Math.Max(0.001, clock.Elapsed.TotalSeconds):F0} MiB/s in {clock.Elapsed.TotalSeconds:F1} s";

    private static void Write(FileStream stream, byte[] buffer, long from, long to)
    {
        for (long at = from; at < to; at += buffer.Length)
        {
            HugeText.Fill(0, at, buffer);
            stream.Write(buffer, 0, buffer.Length);
        }
    }

    [Fact]
    public void The_machine_generates_hashes_writes_reads_and_opens_at_the_speeds_the_50_GiB_tests_are_planned_with()
    {
        if (!HugeText.Enabled)
        {
            output.WriteLine("skipped: TEXTIDA_HUGE is not 1");
            return;
        }
        string dir = Directory.CreateTempSubdirectory("textida-calibration-").FullName;
        var clock = Stopwatch.StartNew();
        try
        {
            Say($"cores {Environment.ProcessorCount}, memory {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes >> 20} MiB, free on the drive {new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace >> 30} GiB");
            var buffer = new byte[HugeText.BlockBytes];
            clock.Restart();
            for (int i = 0; i < 2048; i++) HugeText.Fill(0, (long)i * buffer.Length, buffer);
            Say("generator: " + Rate(2048L << 20, clock));
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                clock.Restart();
                for (int i = 0; i < 2048; i++) hash.AppendData(buffer);
                Say("SHA-256 in memory: " + Rate(2048L << 20, clock));
            }

            string big = Path.Combine(dir, "prealloc.bin");
            clock.Restart();
            using (new FileStream(big, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, PreallocationSize = HugeText.Size(50L << 30) })) { }
            Say($"creating a file with a preallocation of {HugeText.Size(50L << 30) >> 30} GiB: {clock.Elapsed.TotalSeconds:F1} s");
            clock.Restart();
            File.Delete(big);
            Say($"deleting it: {clock.Elapsed.TotalSeconds:F1} s");

            long size = HugeText.Size(4L << 30);
            string small = Path.Combine(dir, "small.bin");
            clock.Restart();
            using (var stream = new FileStream(small, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0 }))
            {
                Write(stream, buffer, 0, size);
                Say($"write {size >> 30} GiB: " + Rate(size, clock));
                clock.Restart();
            }
            Say($"closing it: {clock.Elapsed.TotalSeconds:F1} s");
            clock.Restart();
            using (var stream = new FileStream(small, FileMode.Open, FileAccess.Read, FileShare.Read, HugeText.BlockBytes, FileOptions.SequentialScan))
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            Say("read it back (it may still be in the cache): " + Rate(size, clock));
            clock.Restart();
            using (var doc = Doc.Open(small, new PagedBackendFactory(startIndexing: false)))
                Say("Document.Open that hashes the whole file before it returns: " + Rate(size, clock));
            clock.Restart();
            using (var doc = Doc.Open(small, PagedBackendFactory.Instance, new DocumentOptions { HashInBackgroundAboveBytes = 64L << 20 }))
            {
                Say("Document.Open with the hash in the background, line index started, returned after " + $"{clock.Elapsed.TotalMilliseconds:F0} ms");
                while (!doc.LineIndexState.Complete) Thread.Sleep(10);
                Say("the line index complete after: " + Rate(size, clock));
                doc.HashPass!.Wait();
                Say("the pass with the hash and the block digests ended after: " + Rate(size, clock));
            }
            File.Delete(small);

            // more than the memory of the machine: the first GiBs go into the file cache, the later ones show the disk
            long steady = HugeText.Size(24L << 30), step = HugeText.Size(2L << 30);
            string large = Path.Combine(dir, "large.bin");
            var total = Stopwatch.StartNew();
            using (var stream = new FileStream(large, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0 }))
            {
                for (long at = 0; at < steady; at += step)
                {
                    clock.Restart();
                    Write(stream, buffer, at, Math.Min(steady, at + step));
                    Say($"write {steady / 1073741824.0:F0} GiB file, from GiB {at >> 30}: " + Rate(Math.Min(step, steady - at), clock));
                }
                clock.Restart();
            }
            Say($"closing the {steady >> 30} GiB file: {clock.Elapsed.TotalSeconds:F1} s; all written in {total.Elapsed.TotalSeconds:F0} s");
            clock.Restart();
            using (var stream = new FileStream(large, FileMode.Open, FileAccess.Read, FileShare.Read, HugeText.BlockBytes, FileOptions.SequentialScan))
            {
                stream.Position = steady - size;
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            }
            Say($"read of its last {size >> 30} GiB: " + Rate(size, clock));
            clock.Restart();
            using (var stream = new FileStream(large, FileMode.Open, FileAccess.Read, FileShare.Read, HugeText.BlockBytes, FileOptions.SequentialScan))
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            Say($"read of all of it: " + Rate(steady, clock));
        }
        finally
        {
            clock.Restart();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Say($"clean-up {clock.Elapsed.TotalSeconds:F1} s");
        }
    }
}

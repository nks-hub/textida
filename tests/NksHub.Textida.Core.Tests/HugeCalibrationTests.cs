using System.Diagnostics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Tests;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// The speeds the 50 GiB tests are planned with, measured on the machine they run on: the generator of <see cref="HugeText"/>, SHA-256, creating a file of 50 GiB with and without
/// preallocation, writing, reading, opening a document (which reads the whole file for its fingerprint) and deleting. Each test of the machine gets 20 minutes, so a plan of five passes
/// over 50 GiB has to know what a pass costs. Runs only where <c>TEXTIDA_HUGE=1</c> is set.
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

    [Fact]
    public void The_machine_generates_hashes_writes_reads_opens_and_deletes_at_the_speeds_the_50_GiB_tests_are_planned_with()
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
            foreach (bool preallocate in new[] { true, false })
            {
                string path = Path.Combine(dir, preallocate ? "with.bin" : "without.bin");
                clock.Restart();
                using (var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, PreallocationSize = preallocate ? size : 0 }))
                {
                    var created = clock.Elapsed;
                    clock.Restart();
                    for (long at = 0; at < size; at += buffer.Length)
                    {
                        HugeText.Fill(0, at, buffer);
                        stream.Write(buffer, 0, buffer.Length);
                    }
                    Say($"write {size >> 30} GiB {(preallocate ? "with" : "without")} preallocation: created in {created.TotalSeconds:F1} s, written " + Rate(size, clock));
                    clock.Restart();
                }
                Say($"closing it: {clock.Elapsed.TotalSeconds:F1} s");
            }

            string read = Path.Combine(dir, "without.bin");
            clock.Restart();
            using (var stream = new FileStream(read, FileMode.Open, FileAccess.Read, FileShare.Read, HugeText.BlockBytes, FileOptions.SequentialScan))
                while (stream.Read(buffer, 0, buffer.Length) > 0) { }
            Say("read (the file may still be in the cache): " + Rate(size, clock));

            clock.Restart();
            using (var doc = Doc.Open(read, new PagedBackendFactory(startIndexing: false)))
                Say("Document.Open, which reads and hashes the whole file: " + Rate(size, clock));
            clock.Restart();
            using (var doc = Doc.Open(read, PagedBackendFactory.Instance))
            {
                Say("Document.Open with the line index started: " + Rate(size, clock));
                while (!doc.LineIndexState.Complete) Thread.Sleep(10);
                Say("and the line index complete after: " + Rate(size, clock));
            }
        }
        finally
        {
            clock.Restart();
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
            Say($"clean-up {clock.Elapsed.TotalSeconds:F1} s");
        }
    }
}

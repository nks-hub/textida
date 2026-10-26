using System.Diagnostics;
using System.Runtime.CompilerServices;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class PagedSnapshotLifetimeTests
{
    private static PagedBackend OpenBackend(string path) => PagedBackend.FromFile(path, TextCodec.Cp1252, startIndexing: false);

    private static byte[] Read(IByteSnapshot snapshot, long offset, int length)
    {
        var bytes = new byte[length];
        Assert.Equal(length, snapshot.Read(offset, bytes));
        return bytes;
    }

    [Fact]
    public void Direct_snapshot_keeps_original_and_added_bytes_after_document_disposal()
    {
        using var file = new TempFile("abc"u8.ToArray());
        var document = PagedDocument.Open(file.Path, new() { StartIndexing = false, Codec = TextCodec.Cp1252 });
        document.Insert(1, "XY"u8);
        var snapshot = document.CreateSnapshot();
        document.Dispose();
        Assert.Equal("aXYbc"u8.ToArray(), Read(snapshot, 0, 5));
        File.Delete(file.Path);
        Assert.Equal("aXYbc"u8.ToArray(), Read(snapshot, 0, 5));
    }

    [Fact]
    public void Memory_snapshot_keeps_old_append_buffers_after_growth_and_disposal()
    {
        var document = PagedDocument.OpenMemory("abc"u8, new() { StartIndexing = false, Codec = TextCodec.Cp1252 });
        document.Insert(1, "XY"u8);
        var snapshot = document.CreateSnapshot();
        document.Insert(0, new byte[16_384]);
        document.Dispose();
        Assert.Equal("aXYbc"u8.ToArray(), Read(snapshot, 0, 5));
    }

    [Fact]
    public void Saving_replaces_the_path_but_snapshots_keep_the_original_file_object()
    {
        using var file = new TempFile("abc"u8.ToArray());
        using var document = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        var before = document.GetSnapshot();
        document.Insert(1, "XY"u8.ToArray());
        var edited = document.GetSnapshot();
        Assert.True(document.Save().Saved);
        Assert.Equal("aXYbc"u8.ToArray(), File.ReadAllBytes(file.Path));
        Assert.Equal("abc"u8.ToArray(), before.ReadBytes(new(0, 3)));
        Assert.Equal("aXYbc"u8.ToArray(), edited.ReadBytes(new(0, 5)));
        document.Dispose();
        Assert.Equal("abc"u8.ToArray(), before.ReadBytes(new(0, 3)));
        Assert.Equal("aXYbc"u8.ToArray(), edited.ReadBytes(new(0, 5)));
    }

    [Fact]
    public async Task A_worker_keeps_reading_while_the_owner_closes()
    {
        using var file = new TempFile(Enumerable.Repeat((byte)0x37, 1 << 20).ToArray());
        var backend = OpenBackend(file.Path);
        var snapshot = backend.CreateSnapshot();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = Task.Run(async () =>
        {
            byte[] buffer = new byte[4096];
            started.SetResult();
            for (int i = 0; i < 500; i++)
            {
                Assert.Equal(buffer.Length, snapshot.Read(i * 31, buffer));
                Assert.All(buffer, b => Assert.Equal(0x37, b));
            }
            await closed.Task;
            Assert.Equal(buffer.Length, snapshot.Read(123, buffer));
            Assert.All(buffer, b => Assert.Equal(0x37, b));
        });
        await started.Task;
        backend.Dispose();
        closed.SetResult();
        await reading;
    }

    [Fact]
    public void Explicit_materialization_releases_the_last_file_handle()
    {
        using var file = new TempFile("abc"u8.ToArray());
        var backend = OpenBackend(file.Path);
        var snapshot = Assert.IsType<PagedSnapshot>(backend.CreateSnapshot());
        backend.Dispose();
        if (OperatingSystem.IsWindows())
            Assert.Throws<IOException>(() => { using var exclusive = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        snapshot.Materialize();
        using (var exclusive = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            exclusive.Write("NEW"u8);
        Assert.Equal("abc"u8.ToArray(), Read(snapshot, 0, 3));
    }

    [Fact]
    public void Collecting_the_last_snapshot_releases_its_file_handle()
    {
        using var file = new TempFile("abc"u8.ToArray());
        using var backend = OpenBackend(file.Path);
        var snapshot = AbandonSnapshot(backend);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(snapshot.TryGetTarget(out _));
        using var exclusive = new FileStream(file.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(3, exclusive.Length);
        GC.KeepAlive(backend);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<IByteSnapshot> AbandonSnapshot(PagedBackend backend)
    {
        var snapshot = backend.CreateSnapshot();
        backend.Dispose();
        return new WeakReference<IByteSnapshot>(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Source_changes_after_close_invalidate_reads(bool truncate)
    {
        using var file = new TempFile("old"u8.ToArray());
        DateTime stamp = File.GetLastWriteTimeUtc(file.Path);
        var backend = OpenBackend(file.Path);
        var snapshot = backend.CreateSnapshot();
        backend.Dispose();
        File.WriteAllBytes(file.Path, truncate ? "x"u8.ToArray() : "NEW"u8.ToArray());
        File.SetLastWriteTimeUtc(file.Path, stamp.AddMinutes(1));
        Assert.Throws<SnapshotInvalidatedException>(() => snapshot.Read(0, new byte[3]));
        File.WriteAllBytes(file.Path, "old"u8.ToArray());
        File.SetLastWriteTimeUtc(file.Path, stamp);
        Assert.Throws<SnapshotInvalidatedException>(() => snapshot.Read(0, new byte[3]));
    }

    [Fact]
    public void Metadata_guard_cannot_detect_a_same_size_write_with_restored_time()
    {
        using var file = new TempFile("old"u8.ToArray());
        DateTime stamp = File.GetLastWriteTimeUtc(file.Path);
        var backend = OpenBackend(file.Path);
        var snapshot = backend.CreateSnapshot();
        backend.Dispose();
        File.WriteAllBytes(file.Path, "NEW"u8.ToArray());
        File.SetLastWriteTimeUtc(file.Path, stamp);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file.Path));
        Assert.Equal("NEW"u8.ToArray(), Read(snapshot, 0, 3));
    }

    [Fact]
    public void Guard_uses_observable_metadata_while_an_external_writer_remains_open()
    {
        using var file = new TempFile("old"u8.ToArray());
        DateTime stamp = File.GetLastWriteTimeUtc(file.Path);
        var backend = OpenBackend(file.Path);
        var snapshot = backend.CreateSnapshot();
        backend.Dispose();
        using var writer = new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer.Write("NEW"u8);
        writer.Flush();
        if (File.GetLastWriteTimeUtc(file.Path) != stamp)
            Assert.Throws<SnapshotInvalidatedException>(() => snapshot.Read(0, new byte[3]));
        else
            Assert.Equal("NEW"u8.ToArray(), Read(snapshot, 0, 3));
    }

    [Fact]
    public void Closing_a_snapshot_over_four_gibibytes_keeps_long_offsets_without_materializing()
    {
        const long length = (4L << 30) + 19;
        using var file = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", file.Path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
            stream.Position = length - 4;
            stream.Write("TAIL"u8);
        }
        var backend = OpenBackend(file.Path);
        var before = backend.CreateSnapshot();
        backend.Replace(length - 2, 0, "NEW"u8);
        var after = backend.CreateSnapshot();
        backend.Dispose();
        Assert.Equal(length, before.ByteLength);
        Assert.Equal(length + 3, after.ByteLength);
        Assert.Equal("TAIL"u8.ToArray(), Read(before, length - 4, 4));
        Assert.Equal("TANEWIL"u8.ToArray(), Read(after, length - 4, 7));
        Assert.Equal(new byte[4], Read(before, (2L << 30) + 1, 4));
    }
}

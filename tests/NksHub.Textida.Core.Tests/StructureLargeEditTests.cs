using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureLargeEditTests
{
    [HugeJsonFact]
    [Trait("Category", "Perf")]
    [Trait("Category", "Large")]
    public async Task Gigabyte_replacement_has_bounded_preparation_and_byte_exact_single_undo_redo()
    {
        string directory = Directory.CreateTempSubdirectory("textida-structure-edit-").FullName;
        try
        {
            string path = Path.Combine(directory, "large.dat");
            const long length = 1L << 30;
            CreateSource(path, length);
            using var document = Open(path, directory);
            var snapshot = document.GetSnapshot();
            string original = Digest(document);
            var edit = new ByteEdit(new(5, length - 5), "new value"u8.ToArray());
            long allocated = 0;
            using var prepared = await Task.Run(() =>
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var result = Doc.PrepareReplacement(snapshot, document.Id, snapshot.Revision, edit, directory);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                return result;
            });
            Assert.True(allocated < 64L << 20, $"Preparation allocated {allocated} bytes for a 1 GiB source.");
            long applyBefore = GC.GetAllocatedBytesForCurrentThread();
            Assert.True(document.ApplyPreparedReplacement(prepared, new() { Source = "structure-value", BaseRevision = snapshot.Revision }).Applied);
            Assert.Equal(1, document.UndoDepth);
            Assert.Equal(1, document.Revision);
            Assert.Equal("HEAD\nnew value\nTAIL", document.ReadText(new(0, document.ByteLength)));
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - applyBefore < 64L << 20);

            long undoBefore = GC.GetAllocatedBytesForCurrentThread();
            Assert.NotNull(document.Undo());
            Assert.Equal(length, document.ByteLength);
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - undoBefore < 64L << 20);
            Assert.Equal(original, Digest(document));
            Assert.False(document.CanUndo);
            Assert.NotNull(document.Redo());
            Assert.Equal("HEAD\nnew value\nTAIL", document.ReadText(new(0, document.ByteLength)));
            Assert.Equal(3, document.Revision);
            using var source = File.OpenRead(path);
            Assert.Equal(length, source.Length);
            Assert.Equal(original, Convert.ToHexString(SHA256.HashData(source)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Undo_survives_a_streaming_save_after_the_original_file_was_replaced()
    {
        string directory = Directory.CreateTempSubdirectory("textida-structure-edit-save-").FullName;
        try
        {
            string path = Path.Combine(directory, "source.dat");
            CreateSource(path, 16L << 20);
            using var document = Open(path, directory);
            string original = Digest(document);
            Assert.True(document.Replace(new(5, document.ByteLength - 5), "x"u8.ToArray()).Applied);
            Assert.True((await document.SaveAsync()).Saved);
            Assert.NotNull(document.Undo());
            Assert.Equal(original, Digest(document));
            Assert.NotNull(document.Redo());
            Assert.Equal("HEAD\nx\nTAIL", document.ReadText(new(0, document.ByteLength)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Failed_or_cancelled_preparation_leaves_bytes_revision_history_and_disk_unchanged()
    {
        string directory = Directory.CreateTempSubdirectory("textida-structure-edit-fail-").FullName;
        try
        {
            string path = Path.Combine(directory, "source.dat");
            CreateSource(path, 8L << 20);
            using var document = Open(path, directory);
            string original = Digest(document);
            var snapshot = document.GetSnapshot();
            var edit = new ByteEdit(new(5, document.ByteLength - 5), "x"u8.ToArray());
            Assert.Throws<IOException>(() => Doc.PrepareReplacement(new FailingSource(snapshot), document.Id, 0, edit, directory));
            using var cancellation = new CancellationTokenSource();
            Assert.Throws<OperationCanceledException>(() => Doc.PrepareReplacement(new CancellingSource(snapshot, cancellation), document.Id, 0,
                edit, directory, cancellation.Token));
            string unavailable = Path.Combine(directory, "file-instead-of-directory");
            File.WriteAllText(unavailable, "occupied");
            Assert.ThrowsAny<IOException>(() => Doc.PrepareReplacement(snapshot, document.Id, 0, edit, unavailable));
            Assert.Equal(0, document.Revision);
            Assert.False(document.CanUndo);
            Assert.False(document.CanRedo);
            Assert.Equal(original, Digest(document));
            Assert.Empty(Directory.GetFiles(directory, "*.spill"));
            using var disk = File.OpenRead(path);
            Assert.Equal(original, Convert.ToHexString(SHA256.HashData(disk)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Prepared_edit_maps_a_disjoint_new_revision_and_rejects_an_overlapping_one()
    {
        string directory = Directory.CreateTempSubdirectory("textida-structure-edit-revision-").FullName;
        try
        {
            string path = Path.Combine(directory, "source.dat");
            CreateSource(path, 4L << 20);
            using var document = Open(path, directory);
            var snapshot = document.GetSnapshot();
            using var prepared = Doc.PrepareReplacement(snapshot, document.Id, snapshot.Revision,
                new(new(5, document.ByteLength - 5), "x"u8.ToArray()), directory);
            Assert.True(document.Insert(0, ">"u8.ToArray()).Applied);
            Assert.True(document.ApplyPreparedReplacement(prepared).Applied);
            Assert.Equal(">HEAD\nx\nTAIL", document.ReadText(new(0, document.ByteLength)));
            Assert.NotNull(document.Undo());
            var later = document.GetSnapshot();
            using var stale = Doc.PrepareReplacement(later, document.Id, later.Revision,
                new(new(6, document.ByteLength - 5), "y"u8.ToArray()), directory);
            Assert.True(document.Replace(new(6, 7), "!"u8.ToArray()).Applied);
            string afterInterveningEdit = Digest(document);
            long revision = document.Revision;
            var result = document.ApplyPreparedReplacement(stale);
            Assert.False(result.Applied);
            Assert.Equal(ConflictReason.Overlap, result.Conflict);
            Assert.Equal(revision, document.Revision);
            Assert.Equal(afterInterveningEdit, Digest(document));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void Large_batch_is_one_undo_step_and_invalid_utf8_boundaries_leave_every_edit_unapplied()
    {
        byte[] bytes = new byte[6 << 20];
        bytes.AsSpan().Fill((byte)'a');
        "😀"u8.CopyTo(bytes);
        using var document = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });
        var original = Digest(document);
        var invalid = document.Apply([new(new(1, 2L << 20), "x"u8.ToArray()), new(new(4L << 20, 6L << 20), "y"u8.ToArray())]);
        Assert.False(invalid.Applied);
        Assert.Equal(ConflictReason.InvalidRange, invalid.Conflict);
        Assert.Equal(0, document.Revision);
        Assert.False(document.CanUndo);
        Assert.Equal(original, Digest(document));
        Assert.True(document.Apply([new(new(4, 2L << 20), "first"u8.ToArray()), new(new(4L << 20, 6L << 20), "second"u8.ToArray())]).Applied);
        string replaced = Digest(document);
        Assert.Equal(1, document.UndoDepth);
        Assert.NotNull(document.Undo());
        Assert.Equal(original, Digest(document));
        Assert.NotNull(document.Redo());
        Assert.Equal(replaced, Digest(document));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Failure_after_one_streamed_history_span_restores_bytes_revision_and_history(bool redo, bool duringSave)
    {
        string directory = Directory.CreateTempSubdirectory("textida-structure-history-fail-").FullName;
        try
        {
            string path = Path.Combine(directory, "source.dat");
            CreateSource(path, 10L << 20);
            using var document = Open(path, directory);
            string original = Digest(document);
            var first = new byte[3 << 20];
            var second = new byte[4 << 20];
            first.AsSpan().Fill((byte)'a');
            second.AsSpan().Fill((byte)'b');
            Assert.True(document.Apply([new(new(1L << 20, 3L << 20), first), new(new(6L << 20, 8L << 20), second)]).Applied);
            string replacement = Digest(document);
            Assert.True((await document.SaveAsync()).Saved);
            if (redo) Assert.NotNull(document.Undo());
            var snapshot = document.GetSnapshot();
            string before = Digest(document);
            long length = document.ByteLength;
            long revision = document.Revision;
            int undoDepth = document.UndoDepth, redoDepth = document.RedoDepth;
            bool modified = document.IsModified;
            int changes = 0, sources = 0;
            document.Changed += (_, _) => changes++;
            document.StreamRestoreSource = source =>
            {
                if (++sources == 1) return source;
                // The higher span must already have changed the length before this source fails on its third block.
                Assert.Equal(length + (redo ? 2L << 20 : -(2L << 20)), document.ByteLength);
                return new FailingSource(source);
            };
            if (duringSave) document.Backend.BeginStreamingSave();
            try
            {
                Assert.Throws<IOException>(() => { if (redo) document.Redo(); else document.Undo(); });
                Assert.Equal(2, sources);
                Assert.Equal(before, Digest(document));
                Assert.Equal(length, document.ByteLength);
                Assert.Equal(revision, document.Revision);
                Assert.Equal(undoDepth, document.UndoDepth);
                Assert.Equal(redoDepth, document.RedoDepth);
                Assert.Equal(modified, document.IsModified);
                Assert.Same(snapshot, document.GetSnapshot());
                Assert.Equal(0, changes);
                document.StreamRestoreSource = null;
                Assert.NotNull(redo ? document.Redo() : document.Undo());
                Assert.Equal(redo ? replacement : original, Digest(document));
            }
            finally { if (duringSave) document.Backend.EndStreamingSave(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Doc Open(string path, string spillDirectory) => Doc.Open(path,
        new PagedBackendFactory(startIndexing: false, spillDirectory: spillDirectory), new() { Format = TextCodec.Utf8 });

    private static void CreateSource(string path, long length)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.SetLength(length);
        file.Write("HEAD\n"u8);
        file.Position = length / 2;
        file.Write("middle\r\n"u8);
        file.Position = length - 5;
        file.Write("\nTAIL"u8);
    }

    private static string Digest(Doc document)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        for (long at = 0; at < document.ByteLength;)
        {
            int read = document.Read(at, buffer);
            Assert.True(read > 0);
            hash.AppendData(buffer.AsSpan(0, read));
            at += read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class FailingSource(IByteSnapshot source) : IByteSnapshot
    {
        public long ByteLength => source.ByteLength;
        public int Read(long offset, Span<byte> destination) => offset >= 128 * 1024
            ? throw new IOException("Injected source read failure.") : source.Read(offset, destination);
    }

    private sealed class CancellingSource(IByteSnapshot source, CancellationTokenSource cancellation) : IByteSnapshot
    {
        public long ByteLength => source.ByteLength;
        public int Read(long offset, Span<byte> destination)
        {
            int read = source.Read(offset, destination);
            if (offset >= 64 * 1024) cancellation.Cancel();
            return read;
        }
    }
}

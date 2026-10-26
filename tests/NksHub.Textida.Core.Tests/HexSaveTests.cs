using System.Diagnostics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class HexSaveTests
{
    private static Doc Open(string path) => Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });

    private static byte[] Random(int length, int seed = 7) => Gen.RandomBytes(new Random(seed), length);

    private static EditOptions Raw => new() { RawBytes = true };

    // ---- raw byte edits ----

    [Fact]
    public void A_raw_edit_may_split_a_character_and_a_normal_edit_may_not()
    {
        using var d = Doc.FromBytes("a€b"u8.ToArray(), new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8 });
        Assert.Equal(ConflictReason.InvalidRange, d.Replace(new(2, 3), new byte[] { 0x41 }).Conflict);
        Assert.True(d.Replace(new(2, 3), new byte[] { 0x41 }, Raw).Applied);
        Assert.Equal([0x61, 0xE2, 0x41, 0xAC, 0x62], d.ReadBytes(new(0, d.ByteLength)));
        d.Undo();
        Assert.Equal("a€b"u8.ToArray(), d.ReadBytes(new(0, d.ByteLength)));
    }

    [Fact]
    public void A_raw_edit_may_overwrite_the_byte_order_mark_and_the_bom_length_follows()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x61];
        using var d = Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8.WithBom(true) });
        Assert.Equal(3, d.BomLength);
        Assert.False(d.Replace(new(0, 1), new byte[] { 0x41 }).Applied);
        Assert.True(d.Replace(new(0, 1), new byte[] { 0x41 }, Raw).Applied);
        Assert.Equal(0, d.BomLength);
        d.Undo();
        Assert.Equal(3, d.BomLength);
    }

    [Fact]
    public void Typing_a_run_with_one_coalesce_key_is_one_undo_step()
    {
        using var d = Doc.FromBytes(new byte[8], new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
        for (int i = 0; i < 4; i++) d.Replace(new(i, i + 1), new byte[] { (byte)(i + 1) }, new() { RawBytes = true, CoalesceKey = "hex#1" });
        Assert.Equal(1, d.UndoDepth);
        d.Undo();
        Assert.Equal(new byte[8], d.ReadBytes(new(0, 8)));
    }

    // ---- in-place save ----

    [Fact]
    public void Same_length_changes_save_in_place_and_undo_still_works_after_the_save()
    {
        byte[] original = Random(5 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        long inode = new FileInfo(file.Path).Length;
        Assert.True(d.Replace(new(original.Length - 512, original.Length - 508), new byte[] { 1, 2, 3, 4 }, Raw).Applied);
        Assert.True(d.Replace(new(10, 11), new byte[] { 0xEE }, Raw).Applied);
        var plan = d.PlanSave();
        Assert.Equal(SaveMode.InPlace, plan.Mode);
        Assert.Equal(5, plan.ChangedBytes);

        Assert.True(d.Save().Saved);
        var expected = (byte[])original.Clone();
        expected[10] = 0xEE;
        new byte[] { 1, 2, 3, 4 }.CopyTo(expected, original.Length - 512);
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
        Assert.Equal(inode, new FileInfo(file.Path).Length);
        Assert.False(File.Exists(InPlacePatch.JournalPathOf(file.Path)));
        Assert.False(d.IsModified);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), d.Fingerprint!.Sha256);

        Assert.NotNull(d.Undo());
        Assert.NotNull(d.Undo());
        Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
        Assert.True(d.IsModified);
        Assert.True(d.Save().Saved);
        Assert.Equal(original, File.ReadAllBytes(file.Path));
    }

    [Fact]
    public void A_length_change_streams_a_new_file()
    {
        byte[] original = Random(3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Insert(100, new byte[] { 9 });
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);
        Assert.True(d.Save().Saved);
        var expected = original.Take(100).Append((byte)9).Concat(original.Skip(100)).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
        d.Undo();
        Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
    }

    [Fact]
    public void A_shifting_change_hidden_in_an_equal_length_total_is_not_saved_in_place()
    {
        byte[] original = Random(1 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Insert(10, new byte[] { 1 });
        d.Delete(new(500, 501));
        Assert.Equal(original.Length, d.ByteLength);
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);
    }

    [Theory]
    [InlineData("journal", false)]
    [InlineData("data-partial", false)]
    [InlineData("data", false)]
    [InlineData("flush", false)]
    [InlineData("commit-partial", false)]
    [InlineData("commit", true)]
    public void A_crash_after_any_step_is_completed_or_undone_at_the_next_open(string step, bool newBytes)
    {
        byte[] original = Random(6 << 20);
        using var file = new TempFile(original);
        var expected = (byte[])original.Clone();
        using (var d = Open(file.Path))
        {
            // two ranges more than a chunk apart, so a crash can fall between their chunks
            foreach (long at in new long[] { 3, 4 << 20 })
            {
                d.Replace(new(at, at + 4), new byte[] { 0xA, 0xB, 0xC, 0xD }, Raw);
                new byte[] { 0xA, 0xB, 0xC, 0xD }.CopyTo(expected, at);
            }
            d.InPlaceHooks = new PatchHooks { AfterStep = s => { if (s == step) throw new PatchCrash(); } };
            Assert.Throws<PatchCrash>(() => d.Save());
            Assert.True(File.Exists(InPlacePatch.JournalPathOf(file.Path)));
        }
        using (var reopened = Open(file.Path))
            Assert.Equal(newBytes ? expected : original, reopened.ReadBytes(new(0, reopened.ByteLength)));
        Assert.Equal(newBytes ? expected : original, File.ReadAllBytes(file.Path));
        Assert.False(File.Exists(InPlacePatch.JournalPathOf(file.Path)));
    }

    [Fact]
    public void A_journal_that_was_cut_short_means_the_data_was_never_touched()
    {
        byte[] original = Random(2 << 20);
        using var file = new TempFile(original);
        using (var d = Open(file.Path))
        {
            d.Replace(new(5, 9), new byte[] { 1, 2, 3, 4 }, Raw);
            d.InPlaceHooks = new PatchHooks { AfterStep = s => { if (s == "journal") throw new PatchCrash(); } };
            Assert.Throws<PatchCrash>(() => d.Save());
        }
        string journal = InPlacePatch.JournalPathOf(file.Path);
        using (var fs = new FileStream(journal, FileMode.Open)) fs.SetLength(fs.Length / 2);
        Assert.Equal(PatchRecovery.Discarded, InPlacePatch.Recover(file.Path));
        Assert.Equal(original, File.ReadAllBytes(file.Path));
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public void A_failure_while_writing_puts_the_original_bytes_back()
    {
        byte[] original = Random(6 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(3, 7), new byte[] { 1, 2, 3, 4 }, Raw);
        d.Replace(new(4 << 20, (4 << 20) + 4), new byte[] { 1, 2, 3, 4 }, Raw);
        d.InPlaceHooks = new PatchHooks { AfterStep = s => { if (s == "data-partial") throw new IOException("disk full"); } };
        Assert.Throws<IOException>(() => d.Save());
        Assert.Equal(original, File.ReadAllBytes(file.Path));
        Assert.False(File.Exists(InPlacePatch.JournalPathOf(file.Path)));
        Assert.True(d.IsModified);
    }

    [Fact]
    public void A_file_changed_by_someone_else_is_a_conflict_and_nothing_is_written()
    {
        byte[] original = Random(1 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(5, 6), new byte[] { 1 }, Raw);
        var foreign = (byte[])original.Clone();
        foreign[900] ^= 0xFF;
        using (var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.Position = 900;
            fs.WriteByte(foreign[900]);
        }
        var result = d.SaveAsync().GetAwaiter().GetResult();
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        Assert.Equal(foreign, File.ReadAllBytes(file.Path));
    }

    // ---- snapshots across an in-place save ----

    [Fact]
    public void An_older_snapshot_keeps_the_old_bytes_after_an_in_place_save_and_a_newer_one_sees_the_new_ones()
    {
        using var file = new TempFile("abc"u8.ToArray());
        using var d = Open(file.Path);
        var s0 = d.GetSnapshot();
        Assert.True(d.Replace(new(1, 2), "X"u8.ToArray(), Raw).Applied);
        var s1 = d.GetSnapshot();
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
        Assert.True(d.Save().Saved);
        Assert.Equal("aXc", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(file.Path)));

        Assert.Equal("abc", System.Text.Encoding.ASCII.GetString(s0.ReadBytes(new(0, 3))));
        Assert.Equal("aXc", System.Text.Encoding.ASCII.GetString(s1.ReadBytes(new(0, 3))));
        var s2 = d.GetSnapshot();
        Assert.Equal("aXc", System.Text.Encoding.ASCII.GetString(s2.ReadBytes(new(0, 3))));
        Assert.Null(d.CheckExternalChange());
        Assert.False(d.IsModified);
        Assert.Equal(Convert.ToHexString(SHA256.HashData("aXc"u8)), d.Fingerprint!.Sha256);
        Assert.Equal(File.GetLastWriteTimeUtc(file.Path), d.Fingerprint.LastWriteUtc);
    }

    [Fact]
    public void Every_snapshot_of_a_chain_of_in_place_saves_reads_its_own_revision()
    {
        byte[] original = Random(3 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        var states = new List<(DocumentSnapshot Snapshot, byte[] Bytes)> { (d.GetSnapshot(), original) };
        var current = (byte[])original.Clone();
        for (int round = 1; round <= 4; round++)
        {
            long at = (long)round * 700_000 + 11;
            var patch = new byte[] { (byte)round, (byte)(round * 3), 0x55 };
            Assert.True(d.Replace(new(at, at + 3), patch, Raw).Applied);
            patch.CopyTo(current, at);
            Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
            Assert.True(d.Save().Saved);
            Assert.Null(d.CheckExternalChange());
            states.Add((d.GetSnapshot(), (byte[])current.Clone()));
        }
        foreach (var (snapshot, bytes) in states)
            Assert.Equal(bytes, snapshot.ReadBytes(new(0, snapshot.ByteLength)));
        Assert.Equal(current, File.ReadAllBytes(file.Path));
        Assert.Equal(current, ByteReadAll(d));
    }

    private static byte[] ByteReadAll(Doc d) => d.ReadBytes(new(0, d.ByteLength));

    [Fact]
    public async Task A_snapshot_read_while_the_in_place_write_runs_sees_one_consistent_revision()
    {
        byte[] original = Random(4 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        var old = d.GetSnapshot();
        foreach (long at in new long[] { 100, 2 << 20 }) d.Replace(new(at, at + 8), new byte[8] { 9, 9, 9, 9, 9, 9, 9, 9 }, Raw);
        using var midWrite = new ManualResetEventSlim();
        using var go = new ManualResetEventSlim();
        d.InPlaceHooks = new PatchHooks { AfterStep = s => { if (s == "data-partial") { midWrite.Set(); go.Wait(5000); } } };
        var save = d.SaveAsync();
        Assert.True(midWrite.Wait(5000));
        Assert.Equal(original, old.ReadBytes(new(0, old.ByteLength))); // one chunk is already written, the other is not
        go.Set();
        Assert.True((await save).Saved);
        Assert.Equal(original, old.ReadBytes(new(0, old.ByteLength)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_foreign_change_that_keeps_size_and_write_time_is_not_recorded_as_our_save(bool async)
    {
        byte[] original = Random(2 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(10, 14), new byte[] { 1, 2, 3, 4 }, Raw);
        // between the writing and the check someone changes a byte nobody wrote to and puts the write time back
        d.InPlaceHooks = new PatchHooks
        {
            AfterStep = step =>
            {
                if (step != "commit") return;
                var time = File.GetLastWriteTimeUtc(file.Path);
                using (var fs = new FileStream(file.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Position = 1_500_000;
                    fs.WriteByte((byte)(original[1_500_000] ^ 0xFF));
                }
                File.SetLastWriteTimeUtc(file.Path, time);
            },
        };
        var result = async ? await d.SaveAsync() : d.Save();
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        Assert.Equal(ExternalChangeKind.Modified, result.Conflict!.Kind);
        Assert.True(d.IsModified);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), d.Fingerprint!.Sha256);
        Assert.NotNull(d.CheckExternalChange());
    }

    [Fact]
    public async Task After_our_own_save_the_baseline_is_the_hash_of_the_file_on_disk_and_nothing_is_flagged()
    {
        byte[] original = Random(2 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(10, 14), new byte[] { 1, 2, 3, 4 }, Raw);
        int events = 0;
        d.ExternalChangeDetected += (_, _) => events++;
        Assert.True((await d.SaveAsync()).Saved);
        var disk = FileFingerprint.TryRead(file.Path)!;
        Assert.Equal(disk.Sha256, d.Fingerprint!.Sha256);
        Assert.Equal(disk.Size, d.Fingerprint.Size);
        Assert.Equal(disk.LastWriteUtc, d.Fingerprint.LastWriteUtc);
        Assert.Null(d.CheckExternalChange());
        Assert.Equal(0, events);
    }

    [Fact]
    public void Snapshot_reads_running_in_parallel_with_repeated_own_saves_never_fail_and_return_their_revision()
    {
        byte[] original = Random(4 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        var states = new List<(DocumentSnapshot Snapshot, byte[] Bytes)> { (d.GetSnapshot(), original) };
        var current = (byte[])original.Clone();
        using var stop = new CancellationTokenSource();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var readers = Enumerable.Range(0, 4).Select(k => Task.Run(() =>
        {
            var r = new Random(k);
            var buf = new byte[4096];
            while (!stop.IsCancellationRequested)
            {
                DocumentSnapshot snapshot;
                byte[] bytes;
                lock (states) (snapshot, bytes) = states[r.Next(states.Count)];
                long at = r.NextInt64(0, snapshot.ByteLength - buf.Length);
                try
                {
                    int n = snapshot.Read(at, buf);
                    if (!buf.AsSpan(0, n).SequenceEqual(bytes.AsSpan((int)at, n))) failures.Enqueue($"wrong bytes at {at}");
                }
                catch (Exception e)
                {
                    failures.Enqueue(e.GetType().Name + ": " + e.Message);
                }
            }
        })).ToArray();
        for (int round = 0; round < 40; round++)
        {
            long at = (round * 97_531L) % (current.Length - 16);
            var patch = new byte[] { (byte)round, 2, 3, 4, 5, 6, 7, 8 };
            Assert.True(d.Replace(new(at, at + 8), patch, Raw).Applied);
            patch.CopyTo(current, at);
            Assert.True(d.Save().Saved);
            var snapshot = d.GetSnapshot();
            lock (states) states.Add((snapshot, (byte[])current.Clone()));
            Thread.Sleep(2);
        }
        stop.Cancel();
        Task.WaitAll(readers);
        Assert.Empty(failures);
        Assert.Null(d.CheckExternalChange());
    }

    // ---- async save ----

    [Fact]
    public async Task A_slow_flush_does_not_block_the_caller_and_the_save_completes_only_after_it()
    {
        byte[] original = Random(1 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(5, 6), new byte[] { 1 }, Raw);
        var order = new List<string>();
        var watch = Stopwatch.StartNew();
        d.InPlaceHooks = new PatchHooks { BeforeFlush = ct => { Thread.Sleep(1600); lock (order) order.Add("flushed"); } };
        d.Saved += (_, _) => { lock (order) order.Add("saved"); };

        var task = d.SaveAsync();
        Assert.True(watch.ElapsedMilliseconds < 1000, "SaveAsync must hand control back before the flush ends");
        Assert.True(d.IsSaving);
        Assert.Equal(ConflictReason.Saving, d.Replace(new(0, 1), new byte[] { 2 }, Raw).Conflict);
        Assert.Null(d.Undo());
        Assert.True((await task).Saved);
        Assert.True(watch.ElapsedMilliseconds >= 1500);
        Assert.Equal(["flushed", "saved"], order);
        Assert.False(d.IsSaving);
        Assert.True(d.Replace(new(0, 1), new byte[] { 2 }, Raw).Applied);
        Assert.True(d.IsModified);
    }

    [Fact]
    public async Task Cancelling_during_the_flush_leaves_the_original_and_the_unsaved_state()
    {
        byte[] original = Random(1 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Replace(new(5, 6), new byte[] { 1 }, Raw);
        using var inFlush = new ManualResetEventSlim();
        d.InPlaceHooks = new PatchHooks { BeforeFlush = ct => { inFlush.Set(); ct.WaitHandle.WaitOne(10_000); } };
        var task = d.SaveAsync();
        Assert.True(inFlush.Wait(5000));
        d.CancelSave();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(original, File.ReadAllBytes(file.Path));
        Assert.False(File.Exists(InPlacePatch.JournalPathOf(file.Path)));
        Assert.True(d.IsModified);
        Assert.False(d.IsSaving);
        Assert.True((await d.SaveAsync()).Saved);
        Assert.Equal(1, File.ReadAllBytes(file.Path)[5]);
    }

    [Fact]
    public async Task An_async_stream_save_writes_the_snapshot_and_keeps_undo()
    {
        byte[] original = Random(2 << 20);
        using var file = new TempFile(original);
        using var d = Open(file.Path);
        d.Insert(1000, new byte[] { 7, 7 });
        Assert.True((await d.SaveAsync()).Saved);
        var expected = original.Take(1000).Concat(new byte[] { 7, 7 }).Concat(original.Skip(1000)).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(file.Path));
        Assert.False(d.IsModified);
        d.Undo();
        Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
        Assert.True((await d.SaveAsync()).Saved);
        Assert.Equal(original, File.ReadAllBytes(file.Path));
    }

    // ---- the 10 GiB gate ----

    [Fact]
    [Trait("Category", "Perf")]
    public async Task Ten_gibibyte_file_overwrite_insert_undo_redo_save_and_search_agree_with_an_independent_pass()
    {
        const long length = 10L << 30;
        using var file = new TempFile();
        if (OperatingSystem.IsWindows())
        {
            var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
            foreach (string arg in new[] { "sparse", "setflag", file.Path }) command.ArgumentList.Add(arg);
            using var process = Process.Start(command)!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        long[] zips = [0, (1 << 20) - 2, (4L << 30) + 1, length - 4];
        using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength(length);
            foreach (long offset in zips) { stream.Position = offset; stream.Write([0x50, 0x4B, 0x33, 0x04]); }
        }
        var pattern = BytePattern.ParseHex("50 4B ?? 04");
        var d = Open(file.Path);
        try
        {
            await TenGibiByteScenario(d, file.Path, length, zips, pattern);
        }
        finally
        {
            // the streamed save made a fully allocated 10 GiB file: it must not outlive the test
            d.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            for (int attempt = 0; attempt < 20 && File.Exists(file.Path); attempt++)
            {
                try { File.Delete(file.Path); }
                catch (IOException) { Thread.Sleep(200); }
            }
        }
    }

    private static async Task TenGibiByteScenario(Doc d, string path, long length, long[] zips, BytePattern pattern)
    {
        Assert.Equal(length, d.ByteLength);

        // jump to end-512, overwrite 4 bytes, insert 1 byte lower in the file, undo and redo
        long at = OffsetExpression.Evaluate("end-512", new OffsetContext(d.ByteLength, 0, 0, null));
        Assert.Equal(length - 512, at);
        long insertAt = (6L << 30) - 1;
        byte[] patch = [0x50, 0x4B, 0x07, 0x04];
        byte[] inserted = [0x55];
        Assert.True(d.Replace(new(at, at + 4), patch, Raw).Applied);
        Assert.True(d.Insert(insertAt, inserted, new() { RawBytes = true }).Applied);
        Assert.NotNull(d.Undo());
        Assert.NotNull(d.Undo());
        Assert.Equal(length, d.ByteLength);
        Assert.NotNull(d.Redo());
        Assert.NotNull(d.Redo());
        Assert.Equal(length + 1, d.ByteLength);
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);

        // the length changed: the whole file is streamed; undo is still there afterwards
        Assert.True((await d.SaveAsync()).Saved);
        Assert.Equal(length + 1, new FileInfo(path).Length);
        Assert.True(d.UndoDepth >= 2);

        // then a 4 byte overwrite in the saved file goes in place
        long at2 = d.ByteLength - 512;
        byte[] patch2 = [0x50, 0x4B, 0x09, 0x04];
        Assert.True(d.Replace(new(at2, at2 + 4), patch2, Raw).Applied);
        var plan = d.PlanSave();
        Assert.Equal(SaveMode.InPlace, plan.Mode);
        Assert.Equal(4, plan.ChangedBytes);
        Assert.True((await d.SaveAsync()).Saved);

        // one independent pass over the file: it is the original plus exactly the edits, and its hash is the baseline of the document
        long patched = length + 1 - 512;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var differing = new List<(long Start, long End)>();
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan))
        {
            var buf = new byte[1 << 20];
            var model = new byte[1 << 20];
            for (long o = 0; o < fs.Length;)
            {
                int n = fs.Read(buf, 0, buf.Length);
                Assert.True(n > 0);
                hash.AppendData(buf, 0, n);
                Array.Clear(model, 0, n);
                void Put(long position, byte[] bytes)
                {
                    for (int i = 0; i < bytes.Length; i++)
                        if (position + i >= o && position + i < o + n) model[position + i - o] = bytes[i];
                }
                foreach (long z in zips) Put(z < insertAt ? z : z + 1, [0x50, 0x4B, 0x33, 0x04]);
                Put(insertAt, inserted);
                Put(patched, patch2);
                for (int i = 0; i < n; i++)
                {
                    if (buf[i] == model[i]) continue;
                    long pos = o + i;
                    if (differing.Count > 0 && differing[^1].End == pos) differing[^1] = (differing[^1].Start, pos + 1); else differing.Add((pos, pos + 1));
                }
                o += n;
            }
        }
        Assert.Empty(differing);
        Assert.Equal(Convert.ToHexString(hash.GetHashAndReset()), d.Fingerprint!.Sha256);
        Assert.Null(d.CheckExternalChange());

        // searching the current revision finds what an independent pass finds
        var snapshot = d.GetSnapshot();
        Assert.Equal(NaiveZip(snapshot), ByteSearch.Find(snapshot, pattern).Matches.Select(m => m.Start).ToList());

        // undo after both saves gives the first revision back, and its bytes at the edited places are the original ones
        while (d.UndoDepth > 0) d.Undo();
        Assert.Equal(length, d.ByteLength);
        Span<byte> check = stackalloc byte[4];
        d.Read(length - 4, check);
        Assert.Equal([0x50, 0x4B, 0x33, 0x04], check.ToArray());
        d.Read(length - 512, check);
        Assert.Equal(new byte[4], check.ToArray());
    }

    private static List<long> NaiveZip(DocumentSnapshot s)
    {
        var offsets = new List<long>();
        var buf = new byte[1 << 20];
        uint window = 0;
        for (long o = 0; o < s.ByteLength;)
        {
            int n = s.Read(o, buf);
            for (int i = 0; i < n; i++)
            {
                window = (window << 8) | buf[i];
                if (o + i >= 3 && (window & 0xFFFF00FF) == 0x504B0004) offsets.Add(o + i - 3);
            }
            o += n;
        }
        return offsets;
    }
}

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CrashHarness;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P7.1.4: a change set with very many edits (Replace All) goes to the journal as a list in a payload file instead of one huge record, the records a recovery
/// has to hold stay bounded, and changes that are too large to replay at start wait for the choice to restore or discard them.
/// </summary>
[Collection("Crash harness")]
public sealed class LargeRecoveryTests : IDisposable
{
    private const long Size = 3 << 20;
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-large-recovery-").FullName;

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Data => Path.Combine(_dir, "data", "doc.txt");
    private string Journal => Path.Combine(_dir, "journal");
    private string Payloads => Path.Combine(Journal, "payload");

    private PagedBackendFactory Paged(long budget = 64 << 10) => new(startIndexing: false, spillBudgetBytes: budget, spillDirectory: Path.Combine(_dir, "spill"));

    private static Func<string, IBackendFactory?> Backends(IBackendFactory paged) => name => name switch
    {
        "paged" => paged,
        "reference" => ReferenceBackendFactory.Instance,
        _ => null,
    };

    private static JournalOptions Manual => new() { Fsync = FsyncPolicy.Manual };

    private static byte[] All(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));

    private static List<EditRecord> EditRecords(string journalPath)
    {
        using var reader = new JournalReader(journalPath);
        var list = new List<EditRecord>();
        while (reader.TryRead(out var rec))
            if (rec.Type == RecordType.DocEdit) list.Add(JournalRecords.DecodeEdit(rec.Payload));
        return list;
    }

    private static byte[] Rows(int count) => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, count).Select(i => $"row {i} value\n")));

    /// <summary>One edit per "value" in the text, in ascending order.</summary>
    private static List<ByteEdit> ReplaceValues(byte[] text, Func<int, byte[]> replacement)
    {
        var edits = new List<ByteEdit>();
        ReadOnlySpan<byte> needle = "value"u8;
        for (int from = 0, k = 0; ;)
        {
            int i = text.AsSpan(from).IndexOf(needle);
            if (i < 0) return edits;
            int at = from + i;
            edits.Add(new ByteEdit(new ByteRange(at, at + needle.Length), replacement(k++)));
            from = at + needle.Length;
        }
    }

    private static byte[] ApplyNaively(byte[] text, List<ByteEdit> edits)
    {
        var output = new MemoryStream();
        long pos = 0;
        foreach (var e in edits)
        {
            output.Write(text.AsSpan((int)pos, (int)(e.Range.Start - pos)));
            output.Write(e.Insert.Span);
            pos = e.Range.End;
        }
        output.Write(text.AsSpan((int)pos));
        return output.ToArray();
    }

    private static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static string HashOf(Doc doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            hash.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ---- edit lists ----

    /// <summary>A named reference document with a replace-all of 6000 edits applied and the journal flushed, as the crash leaves it.</summary>
    private (byte[] Text, byte[] Expected, List<ByteEdit> Edits, string Journal, EditListRef List, string File) ReplaceAllAndCrash(string file)
    {
        byte[] text = Rows(6000);
        File.WriteAllBytes(file, text);
        var longer = "a considerably longer replacement"u8.ToArray();
        var shorter = "v"u8.ToArray();
        var edits = ReplaceValues(text, k => k % 7 == 0 ? longer : shorter);
        byte[] expected = ApplyNaively(text, edits);
        using var session = JournalSession.Start(Journal, Backends(Paged()), Manual);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        Assert.True(doc.Apply(edits).Applied);
        session.Flush();
        var record = Assert.Single(EditRecords(session.JournalPath!));
        return (text, expected, edits, session.JournalPath!, record.List!, file);
    }

    [Fact]
    public void A_replace_all_with_thousands_of_edits_is_written_as_a_list_and_comes_back_exactly()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (text, expected, edits, journalPath, list, _) = ReplaceAllAndCrash(file);

        var record = Assert.Single(EditRecords(journalPath));
        Assert.Empty(record.Edits);
        Assert.NotNull(record.List);
        Assert.Equal(edits.Count, list.Count);
        Assert.True(new FileInfo(journalPath).Length < 4096, "the record grew with the edits");
        Assert.True(new FileInfo(Path.Combine(Payloads, list.File)).Length >= list.Offset + list.Length);

        var back = Assert.Single(JournalRecovery.Recover(journalPath, Backends(Paged())).Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.True(expected.AsSpan().SequenceEqual(All(restored)), "the recovered text differs");
        Assert.Equal(1, restored.Revision);
        Assert.NotNull(restored.Undo());
        Assert.True(text.AsSpan().SequenceEqual(All(restored)), "undo did not give the text back");
        Assert.True(text.AsSpan().SequenceEqual(File.ReadAllBytes(file)), "recovery wrote the file");
    }

    [Fact]
    public void A_list_is_a_part_of_the_session_too_after_adoption_and_a_second_crash()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (_, expected, _, _, _, _) = ReplaceAllAndCrash(file);

        using (var second = JournalSession.Start(Journal, Backends(Paged()), Manual))
        {
            var doc = Assert.Single(second.Recovery.Documents).Document!;
            Assert.True(expected.AsSpan().SequenceEqual(All(doc)));
            Assert.True(doc.Insert(0, "tail "u8.ToArray()).Applied);
            second.Flush();
            doc.Dispose();
        }

        using var third = JournalSession.Start(Journal, Backends(Paged()));
        var again = Assert.Single(third.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, again.Status);
        Assert.True(Encoding.UTF8.GetBytes("tail ").Concat(expected).SequenceEqual(All(again.Document!)));
        again.Document!.Dispose();
    }

    [Fact]
    public void A_bit_flip_in_the_middle_of_a_list_is_found_and_nothing_is_applied()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (text, _, _, journalPath, list, _) = ReplaceAllAndCrash(file);
        string blob = Path.Combine(Payloads, list.File);
        using (var fs = new FileStream(blob, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            fs.Position = list.Offset + list.Length / 2;
            int b = fs.ReadByte();
            fs.Position = list.Offset + list.Length / 2;
            fs.WriteByte((byte)(b ^ 0xFF));
        }

        var back = Assert.Single(JournalRecovery.Recover(journalPath, Backends(Paged())).Documents);
        Assert.Equal(RecoveryStatus.Failed, back.Status);
        Assert.Null(back.Document);
        Assert.Contains("differs from what the journal recorded", back.Error);
        Assert.True(text.AsSpan().SequenceEqual(File.ReadAllBytes(file)));
    }

    [Fact]
    public void A_missing_list_file_fails_the_document_and_keeps_its_records()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (_, _, _, journalPath, list, _) = ReplaceAllAndCrash(file);
        File.Delete(Path.Combine(Payloads, list.File));

        using var session = JournalSession.Start(Journal, Backends(Paged()));
        var back = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Failed, back.Status);
        Assert.Contains("missing", back.Error);
        Assert.True(back.PendingRecords > 0);
        Assert.Contains(EditRecords(session.JournalPath!), e => e.List is not null);
        Assert.False(File.Exists(journalPath)); // adopted: the new journal holds the records
    }

    [Fact]
    public void Equal_short_replacements_in_a_row_share_one_array_when_a_list_is_read()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (_, _, edits, _, list, _) = ReplaceAllAndCrash(file);

        var loaded = JournalPayloads.LoadList(Payloads, list);
        Assert.Equal(edits.Count, loaded.Count);
        for (int k = 0; k < loaded.Count; k++)
        {
            Assert.Equal(edits[k].Range, loaded[k].Range);
            Assert.True(edits[k].Insert.Span.SequenceEqual(loaded[k].Insert.Span));
        }
        Assert.True(MemoryMarshal.TryGetArray(loaded[1].Insert, out var one) & MemoryMarshal.TryGetArray(loaded[2].Insert, out var two));
        Assert.Same(one.Array, two.Array);
    }

    [Fact]
    public void The_weight_of_a_replace_all_is_what_it_inserts_and_removes()
    {
        string file = Path.Combine(_dir, "rows.txt");
        var (_, _, edits, journalPath, _, _) = ReplaceAllAndCrash(file);
        long weight = edits.Sum(e => (long)e.Insert.Length + e.Range.Length);

        var back = Assert.Single(JournalRecovery.Recover(journalPath, Backends(Paged()), new RecoveryOptions { DeferAboveBytes = 1 }).Documents);
        Assert.Equal(RecoveryStatus.Pending, back.Status);
        Assert.Equal(weight, back.PendingBytes);
        Assert.Null(back.Document);

        var all = Assert.Single(JournalRecovery.Recover(journalPath, Backends(Paged()), new RecoveryOptions { DeferAboveBytes = weight }).Documents);
        Assert.Equal(RecoveryStatus.Restored, all.Status); // exactly at the threshold is not above it
        all.Document!.Dispose();
    }

    [Fact]
    public void Edits_past_the_inline_budget_of_a_document_go_to_a_payload_file_and_still_recover()
    {
        string file = Path.Combine(_dir, "grow.txt");
        File.WriteAllBytes(file, "start\n"u8.ToArray());
        var chunk = new byte[512 << 10];
        Random.Shared.NextBytes(chunk);
        for (int i = 0; i < chunk.Length; i++) chunk[i] = (byte)('a' + chunk[i] % 26); // text, so the document stays valid UTF-8
        int count = (int)(JournalPlaceholders.MaxInlineDocumentBytes / chunk.Length) + 6;

        byte[] expected;
        using (var session = JournalSession.Start(Journal, Backends(Paged()), Manual))
        using (var doc = Doc.Open(file, ReferenceBackendFactory.Instance))
        {
            session.Track(doc);
            for (int i = 0; i < count; i++) Assert.True(doc.Insert(doc.ByteLength, chunk).Applied);
            session.Flush();
            expected = All(doc);
        }

        var path = Directory.GetFiles(Journal, "session-*.njl").Single();
        var edits = EditRecords(path).SelectMany(e => e.Edits).ToList();
        long inline = edits.Sum(e => (long)e.Inserted.Length);
        Assert.InRange(edits.Count(e => e.External is not null), 1, 8);
        Assert.True(inline <= JournalPlaceholders.MaxInlineDocumentBytes, $"{inline} bytes stayed inline");

        var back = Assert.Single(JournalRecovery.Recover(path, Backends(Paged())).Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        using var restored = back.Document!;
        Assert.True(expected.AsSpan().SequenceEqual(All(restored)), "the recovered text differs");
    }

    // ---- changes that wait ----

    private (LargeModel Model, string OriginalHash) EditAndCrash()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Data)!);
        LargeText.WriteFile(Data, Size);
        string original = HashFile(Data);
        var model = new LargeModel(Size);
        var factory = Paged();
        using var session = JournalSession.Start(Journal, Backends(factory), Manual);
        using var doc = Doc.Open(Data, factory);
        session.Track(doc);
        Edit(doc, model, 100, 5, 51, 40);
        Edit(doc, model, Size / 2, 0, 52, 2 << 20);
        Edit(doc, model, 9, 0, 53, 3);
        session.Flush();
        return (model, original);
    }

    private static void Edit(Doc doc, LargeModel model, long offset, long delete, long seed, long length)
    {
        var bytes = new byte[length];
        LargeText.Fill(seed, 0, bytes);
        Assert.True(doc.Replace(new ByteRange(offset, offset + delete), bytes).Applied);
        model.Replace(offset, delete, seed, length);
    }

    private JournalSession StartDeferring(long above = 1 << 20) =>
        JournalSession.Start(Journal, Backends(Paged()), recovery: new RecoveryOptions { DeferAboveBytes = above });

    [Fact]
    public void Large_changes_wait_for_a_decision_then_restore_exactly_and_are_tracked_again()
    {
        var (model, original) = EditAndCrash();

        var session = StartDeferring();
        var pending = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Pending, pending.Status);
        Assert.Null(pending.Document);
        Assert.InRange(pending.PendingBytes, 2 << 20, 3 << 20);
        Assert.Equal(3, pending.PendingRecords);
        Assert.Equal(original, HashFile(Data));

        var back = session.RecoverPending(pending.DocId);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        Assert.Same(back, Assert.Single(session.Recovery.Documents));
        var doc = back.Document!;
        Assert.Equal(model.Sha256(), HashOf(doc));
        Assert.Equal(3, doc.UndoDepth);
        Assert.NotNull(session.IdOf(doc));
        Assert.Equal(original, HashFile(Data));

        // the rebuilt document is journaled again: one more edit, a crash, and the next start gets all of it
        Edit(doc, model, 5, 0, 99, 20);
        session.Flush();
        doc.Dispose();
        session.Dispose();

        using var next = JournalSession.Start(Journal, Backends(Paged()));
        var again = Assert.Single(next.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, again.Status);
        Assert.Equal(model.Sha256(), HashOf(again.Document!));
        again.Document!.Dispose();
    }

    [Fact]
    public void Changes_that_nobody_decided_on_stay_pending_over_starts_and_replay_when_the_threshold_is_off()
    {
        var (model, _) = EditAndCrash();

        long weight;
        using (var first = StartDeferring())
            weight = Assert.Single(first.Recovery.Documents).PendingBytes;
        using (var second = StartDeferring())
        {
            var still = Assert.Single(second.Recovery.Documents);
            Assert.Equal(RecoveryStatus.Pending, still.Status);
            Assert.Equal(weight, still.PendingBytes);
        }

        using var third = JournalSession.Start(Journal, Backends(Paged()));
        var back = Assert.Single(third.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        Assert.Equal(model.Sha256(), HashOf(back.Document!));
        back.Document!.Dispose();
    }

    [Fact]
    public void Small_changes_are_replayed_at_start_even_when_a_threshold_is_set()
    {
        var (model, _) = EditAndCrash();
        using var session = StartDeferring(above: 64 << 20);
        var back = Assert.Single(session.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        Assert.Equal(model.Sha256(), HashOf(back.Document!));
        back.Document!.Dispose();
    }

    [Fact]
    public void Discarding_pending_changes_closes_the_document_and_deletes_its_payload_files()
    {
        var (_, original) = EditAndCrash();

        using (var session = StartDeferring())
        {
            var pending = Assert.Single(session.Recovery.Documents);
            Assert.NotEmpty(Directory.GetFiles(Payloads));
            Assert.True(session.DiscardPending(pending.DocId));
            Assert.Empty(session.Recovery.Documents);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.DoesNotContain(Directory.GetFiles(Payloads), file => File.Exists(file));
            Assert.False(session.DiscardPending(pending.DocId));
            Assert.Throws<InvalidOperationException>(() => session.RecoverPending(pending.DocId));
        }

        using var next = JournalSession.Start(Journal, Backends(Paged()));
        Assert.Empty(next.Recovery.Documents);
        Assert.Equal(original, HashFile(Data));
    }

    [Fact]
    public void A_failed_replay_of_a_pending_document_keeps_it_for_another_try()
    {
        EditAndCrash();
        using var session = StartDeferring();
        var pending = Assert.Single(session.Recovery.Documents);
        var changed = Encoding.ASCII.GetBytes("someone else wrote this file");
        File.WriteAllBytes(Data, changed);

        var back = session.RecoverPending(pending.DocId);
        Assert.Equal(RecoveryStatus.Conflict, back.Status);
        Assert.Null(back.Document);
        Assert.Same(back, Assert.Single(session.Recovery.Documents));
        Assert.Equal(changed, File.ReadAllBytes(Data));
        Assert.True(session.DiscardPending(back.DocId)); // a conflict can be thrown away as well
    }

    // ---- many edits over a large file ----

    [Fact]
    [Trait("Category", "Large")]
    public void Replace_all_with_a_million_edits_over_a_64_MiB_file_keeps_the_records_small_and_recovers_exactly()
    {
        const long size = 64L << 20;
        Directory.CreateDirectory(Path.GetDirectoryName(Data)!);
        LargeText.WriteFile(Data, size);
        string expected = ReplacedLineHash(Data);
        long lines = size / LargeText.LineBytes;
        var xy = "XY"u8.ToArray();
        var edits = new List<ByteEdit>((int)lines);
        for (long i = 0; i < lines; i++) edits.Add(new ByteEdit(new ByteRange(i * LargeText.LineBytes + 12, i * LargeText.LineBytes + 13), xy));

        using (var session = JournalSession.Start(Journal, Backends(Paged(PagedSpillPlaceholders.MemoryBudgetBytes)), Manual))
        using (var doc = Doc.Open(Data, Paged(PagedSpillPlaceholders.MemoryBudgetBytes)))
        {
            session.Track(doc);
            Assert.True(doc.Apply(edits).Applied);
            Assert.True(session.IsActive, "the journal gave up: " + session.Fault);
            session.Flush();
            Assert.Equal(expected, HashOf(doc));
        }
        edits = null!;

        var path = Directory.GetFiles(Journal, "session-*.njl").Single();
        using (var reader = new JournalReader(path))
            while (reader.TryRead(out var rec)) Assert.True(rec.Payload.Length < 1 << 20, $"a {rec.Type} record of {rec.Payload.Length} bytes");
        var list = Assert.Single(EditRecords(path)).List!;
        Assert.Equal(lines, list.Count);

        GC.Collect();
        using var next = JournalSession.Start(Journal, Backends(Paged(PagedSpillPlaceholders.MemoryBudgetBytes)));
        var back = Assert.Single(next.Recovery.Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        Assert.Equal(expected, HashOf(back.Document!));
        back.Document!.Dispose();
    }

    private static string ReplacedLineHash(string path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = File.OpenRead(path);
        var input = new byte[1 << 20]; // a multiple of the 64 byte line
        var output = new byte[input.Length / LargeText.LineBytes * (LargeText.LineBytes + 1)];
        for (int n; (n = fs.ReadAtLeast(input, input.Length, throwOnEndOfStream: false)) > 0;)
        {
            int o = 0;
            for (int at = 0; at < n; at += LargeText.LineBytes)
            {
                input.AsSpan(at, 12).CopyTo(output.AsSpan(o));
                output[o + 12] = (byte)'X';
                output[o + 13] = (byte)'Y';
                input.AsSpan(at + 13, LargeText.LineBytes - 13).CopyTo(output.AsSpan(o + 14));
                o += LargeText.LineBytes + 1;
            }
            hash.AppendData(output, 0, o);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public class TextCodecGetStringTests
{
    [Fact]
    public void GetString_gives_the_text_of_Decode_for_valid_and_damaged_UTF8()
    {
        var rng = new Random(11);
        const string alphabet = "abc ěščřžýáíé 中文 😀\r\n\t";
        for (int round = 0; round < 600; round++)
        {
            var text = new StringBuilder();
            for (int i = rng.Next(0, 400); i > 0; i--) text.Append(alphabet[rng.Next(alphabet.Length)]);
            byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
            if (round % 2 == 1 && bytes.Length > 0)
                for (int k = 1 + rng.Next(3); k > 0; k--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
            Assert.Equal(TextCodec.Utf8.Decode(bytes).Text, TextCodec.Utf8.GetString(bytes));
        }
    }

    [Fact]
    public void A_cut_off_character_at_the_end_is_one_replacement_character()
    {
        byte[] bytes = [.. "ab"u8, 0xE4, 0xB8]; // two bytes of a three byte character
        Assert.Equal(TextCodec.Utf8.Decode(bytes).Text, TextCodec.Utf8.GetString(bytes));
    }
}

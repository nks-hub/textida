using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>A big file opens with size and write time only; hash and block digests come from a pass on the thread pool and are taken over only for the bytes the file was opened on.</summary>
public class OpenHashTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static EditOptions Raw => new() { RawBytes = true };

    private static Doc Open(string path, TaskCompletionSource? gate = null) =>
        Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252, HashInBackgroundAboveBytes = 0, HashPassGate = gate?.Task });

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void Finish(Doc d)
    {
        Assert.True(d.HashPass!.Wait(Patience), "The pass did not end.");
    }

    [Fact]
    public void A_file_over_the_limit_opens_without_a_hash_and_gets_hash_and_digests_from_the_pass()
    {
        byte[] bytes = Gen.RandomBytes(new Random(1), 3 << 20);
        using var file = new TempFile(bytes);
        var gate = new TaskCompletionSource();
        using var d = Open(file.Path, gate);
        int raised = 0;
        d.FingerprintHashed += (_, _) => Interlocked.Increment(ref raised);
        Assert.False(d.Fingerprint!.HasHash);
        Assert.Equal(bytes.Length, d.Fingerprint.Size);
        Assert.Null(d.CheckExternalChange());
        Assert.Equal(0, raised);

        gate.SetResult();
        Finish(d);
        Assert.Equal(Sha(bytes), d.Fingerprint.Sha256);
        Assert.Equal(1, raised);
        Assert.Null(d.CheckExternalChange());
        Assert.True(d.Replace(new(10, 11), new byte[] { 1 }, Raw).Applied);
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode); // the digests are the baseline an in-place save needs
    }

    [Fact]
    public void A_file_up_to_the_limit_is_hashed_before_the_open_returns()
    {
        byte[] bytes = Gen.RandomBytes(new Random(2), 1 << 20);
        using var file = new TempFile(bytes);
        using var d = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252, HashInBackgroundAboveBytes = bytes.Length });
        Assert.Equal(Sha(bytes), d.Fingerprint!.Sha256);
        Assert.Null(d.HashPass);
    }

    [Fact]
    public void Until_the_pass_ends_a_save_writes_the_whole_file_and_the_pass_gives_way()
    {
        byte[] bytes = Gen.RandomBytes(new Random(3), 3 << 20);
        using var file = new TempFile(bytes);
        var gate = new TaskCompletionSource();
        using var d = Open(file.Path, gate);
        int raised = 0;
        d.FingerprintHashed += (_, _) => Interlocked.Increment(ref raised);
        Assert.True(d.Replace(new(10, 11), new[] { (byte)(bytes[10] ^ 0xFF) }, Raw).Applied);
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode); // nothing vouches for the unchanged blocks yet
        Assert.True(d.Save().Saved);
        var saved = d.Fingerprint!;
        Assert.True(saved.HasHash);

        gate.SetResult();
        Finish(d);
        Assert.Same(saved, d.Fingerprint);
        Assert.Equal(Sha(File.ReadAllBytes(file.Path)), d.Fingerprint!.Sha256);
        Assert.Equal(0, raised);
        Assert.Null(d.CheckExternalChange());
    }

    [Fact]
    public void A_file_that_changed_while_the_pass_waited_gets_no_hash_and_stays_a_change()
    {
        using var file = new TempFile(Gen.RandomBytes(new Random(4), 2 << 20));
        var gate = new TaskCompletionSource();
        using var d = Open(file.Path, gate);
        int raised = 0;
        d.FingerprintHashed += (_, _) => Interlocked.Increment(ref raised);
        File.SetLastWriteTimeUtc(file.Path, File.GetLastWriteTimeUtc(file.Path).AddMinutes(5));

        gate.SetResult();
        Finish(d);
        Assert.False(d.Fingerprint!.HasHash);
        Assert.Equal(0, raised);
        Assert.Equal(ExternalChangeKind.Modified, d.CheckExternalChange()!.Kind);
    }

    [Fact]
    public void Disposing_the_document_stops_a_waiting_pass_without_a_hash()
    {
        using var file = new TempFile(Gen.RandomBytes(new Random(5), 2 << 20));
        var gate = new TaskCompletionSource();
        var d = Open(file.Path, gate);
        d.Dispose();
        gate.SetResult();
        Finish(d);
        Assert.False(d.Fingerprint!.HasHash);
    }

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static JournalSession NewSession(JournalTempDir dir) =>
        JournalSession.Start(dir.Combine("journal"), JournalFormatTests.ReferenceResolver, new JournalOptions { Fsync = FsyncPolicy.Manual });

    private static Doc OpenReference(string path, TaskCompletionSource gate) =>
        Doc.Open(path, ReferenceBackendFactory.Instance, new() { HashInBackgroundAboveBytes = 0, HashPassGate = gate.Task });

    [Fact]
    public void The_hash_of_a_lazily_opened_file_is_journaled_so_a_change_of_the_same_size_is_a_conflict()
    {
        using var dir = new JournalTempDir();
        string file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("original text"));
        using var session = NewSession(dir);
        var gate = new TaskCompletionSource();
        using var doc = OpenReference(file, gate);
        session.Track(doc);
        gate.SetResult();
        Finish(doc);
        Assert.True(doc.Fingerprint!.HasHash);
        doc.Insert(0, U("my edit "));
        session.Flush();

        File.WriteAllBytes(file, U("ORIGINAL TEXT"));
        var back = Assert.Single(JournalRecovery.Recover(session.JournalPath!, JournalFormatTests.ReferenceResolver).Documents);
        Assert.Equal(RecoveryStatus.Conflict, back.Status);
    }

    [Fact]
    public void Before_the_hash_is_journaled_only_the_size_vouches_for_the_file()
    {
        using var dir = new JournalTempDir();
        string file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U("original text"));
        using var session = NewSession(dir);
        var gate = new TaskCompletionSource();
        using var doc = OpenReference(file, gate);
        session.Track(doc);
        doc.Insert(0, U("my edit "));
        session.Flush();

        File.WriteAllBytes(file, U("ORIGINAL TEXT"));
        var back = Assert.Single(JournalRecovery.Recover(session.JournalPath!, JournalFormatTests.ReferenceResolver).Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        back.Document!.Dispose();
        File.WriteAllBytes(file, U("shorter"));
        Assert.Equal(RecoveryStatus.Conflict, Assert.Single(JournalRecovery.Recover(session.JournalPath!, JournalFormatTests.ReferenceResolver).Documents).Status);
        gate.SetResult();
    }

    private static JournalRecord OpenRecordOf(Guid id, FileFingerprint print) =>
        new(0, 0, RecordType.DocOpen, JournalRecords.Encode(new OpenRecord(id, "f.bin", print, "utf-8", "paged", 0, 0)));

    private static FileFingerprint? PrintOf(JournalCompactor.DocLog log) => JournalRecords.DecodeOpen(log.Open.Payload).Fingerprint;

    [Fact]
    public void A_second_open_record_adds_the_hash_only_before_the_first_save_and_only_for_the_same_size()
    {
        var id = Guid.NewGuid();
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bare = new FileFingerprint(10, time, "");
        var hashed = new FileFingerprint(10, time, "AA");

        Assert.Equal("AA", PrintOf(Assert.Single(JournalCompactor.Plan([OpenRecordOf(id, bare), OpenRecordOf(id, hashed)]).Docs))!.Sha256);
        Assert.Equal("", PrintOf(Assert.Single(JournalCompactor.Plan([OpenRecordOf(id, bare), OpenRecordOf(id, hashed with { Size = 11 })]).Docs))!.Sha256);

        var save = new JournalRecord(0, 0, RecordType.DocSave, JournalRecords.Encode(new SaveRecord(id, 1, "f.bin", new FileFingerprint(12, time, "SAVED"))));
        var after = Assert.Single(JournalCompactor.Plan([OpenRecordOf(id, bare), save, OpenRecordOf(id, hashed)]).Docs);
        Assert.Equal("SAVED", PrintOf(after)!.Sha256);
    }
}

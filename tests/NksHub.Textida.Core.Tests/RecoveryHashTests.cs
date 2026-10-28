using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// A crash in front of a file of tens of GiB must not make the recovery read all of it. With <see cref="RecoveryOptions.HashInBackgroundAboveBytes"/> the document is rebuilt at once and the pass over the
/// file compares its hash with the one the journal recorded: a file of another size is a conflict at once, one of the same size and write time is taken for the same file and the pass finds a difference
/// later, and a save that is not an explicit overwrite waits for the pass. The passes are held at a gate, so what is asked is what recovery does while the hash is still to come.
/// </summary>
public class RecoveryHashTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60), Quick = TimeSpan.FromSeconds(10);
    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>A named document with the hash in its journaled fingerprint and one edit, as a crash leaves it.</summary>
    private static (JournalSession Session, string File, DateTime Time) Crashed(JournalTempDir dir, string text)
    {
        string file = dir.Combine("note.txt");
        File.WriteAllBytes(file, U(text));
        var session = JournalSession.Start(dir.Combine("journal"), JournalFormatTests.ReferenceResolver, new JournalOptions { Fsync = FsyncPolicy.Manual });
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(0, U("my edit "));
        session.Flush();
        return (session, file, File.GetLastWriteTimeUtc(file));
    }

    private static RecoveredDocument Recover(JournalSession session, TaskCompletionSource? gate) =>
        Assert.Single(JournalRecovery.Recover(session.JournalPath!, JournalFormatTests.ReferenceResolver, new RecoveryOptions { HashInBackgroundAboveBytes = 0, HashPassGate = gate?.Task }).Documents);

    /// <summary>Recovers while the pass is held at <paramref name="gate"/>; a recovery that waits for the pass fails the test after <see cref="Quick"/> instead of hanging it.</summary>
    private static RecoveredDocument RecoverWithoutWaiting(JournalSession session, TaskCompletionSource gate)
    {
        var recovering = Task.Run(() => Recover(session, gate));
        if (recovering.Wait(Quick)) return recovering.Result;
        gate.SetResult();
        recovering.Wait(Patience);
        Assert.Fail("recovery waits for the pass over the file");
        return null!;
    }

    [Fact]
    public void Recovery_of_a_big_file_does_not_wait_for_the_hash_and_the_document_is_whole()
    {
        using var dir = new JournalTempDir();
        var (session, _, _) = Crashed(dir, "original text");
        using var closing = session;
        var gate = new TaskCompletionSource();
        try
        {
            var back = RecoverWithoutWaiting(session, gate);
            Assert.Equal(RecoveryStatus.Restored, back.Status);
            using var doc = back.Document!;
            Assert.Equal("my edit original text", Encoding.UTF8.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
            Assert.False(doc.Fingerprint!.HasHash);
            Assert.False(doc.HashPass!.IsCompleted);
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public void A_file_of_another_size_is_a_conflict_at_once_without_the_pass()
    {
        using var dir = new JournalTempDir();
        var (session, file, _) = Crashed(dir, "original text");
        using var closing = session;
        File.WriteAllBytes(file, U("shorter"));
        var gate = new TaskCompletionSource();
        try { Assert.Equal(RecoveryStatus.Conflict, RecoverWithoutWaiting(session, gate).Status); }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public void Other_bytes_under_the_same_size_and_write_time_are_found_by_the_pass_and_a_save_answers_with_the_change_until_it_is_an_overwrite()
    {
        using var dir = new JournalTempDir();
        var (session, file, time) = Crashed(dir, "original text");
        using var closing = session;
        File.WriteAllBytes(file, U("ORIGINAL TEXT"));
        File.SetLastWriteTimeUtc(file, time);
        var gate = new TaskCompletionSource();
        try
        {
            var back = RecoverWithoutWaiting(session, gate);
            Assert.Equal(RecoveryStatus.Restored, back.Status); // size and time are all that vouches until the pass ends
            using var doc = back.Document!;
            var raised = new List<ExternalChange>();
            doc.ExternalChangeDetected += (_, change) => { lock (raised) raised.Add(change); };

            gate.SetResult();
            Assert.True(doc.HashPass!.Wait(Patience));
            var change = Assert.Single(raised);
            Assert.Equal(ExternalChangeKind.Modified, change.Kind);
            Assert.False(doc.Fingerprint!.HasHash); // the hash of a file that is not the recorded one is not taken over
            var refused = doc.Save();
            Assert.False(refused.Saved);
            Assert.Equal(change, refused.Conflict);
            Assert.Equal("ORIGINAL TEXT", File.ReadAllText(file));

            Assert.True(doc.Save(overwriteExternalChanges: true).Saved);
            Assert.Equal("my edit ORIGINAL TEXT", File.ReadAllText(file)); // the document was rebuilt from the file as it was then
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public void The_same_file_is_confirmed_by_the_pass_and_gets_its_hash()
    {
        using var dir = new JournalTempDir();
        var (session, file, _) = Crashed(dir, "original text");
        using var closing = session;
        var gate = new TaskCompletionSource();
        try
        {
            using var doc = RecoverWithoutWaiting(session, gate).Document!;
            int raised = 0, hashed = 0;
            doc.ExternalChangeDetected += (_, _) => Interlocked.Increment(ref raised);
            doc.FingerprintHashed += (_, _) => Interlocked.Increment(ref hashed);
            gate.SetResult();
            Assert.True(doc.HashPass!.Wait(Patience));
            Assert.Equal((0, 1), (raised, hashed));
            Assert.True(doc.Fingerprint!.HasHash);
            Assert.True(doc.Save().Saved);
            Assert.Equal("my edit original text", File.ReadAllText(file));
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public void A_save_waits_for_the_pass_that_has_not_confirmed_the_base_yet()
    {
        using var dir = new JournalTempDir();
        var (session, file, _) = Crashed(dir, "original text");
        using var closing = session;
        var gate = new TaskCompletionSource();
        try
        {
            using var doc = RecoverWithoutWaiting(session, gate).Document!;
            var saving = Task.Run(() => doc.Save());
            Assert.False(saving.Wait(TimeSpan.FromMilliseconds(500)), "the save did not wait for the pass");
            Assert.Equal("original text", File.ReadAllText(file));
            gate.SetResult();
            Assert.True(saving.Wait(Patience));
            Assert.True(saving.Result.Saved);
            Assert.Equal("my edit original text", File.ReadAllText(file));
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public void The_same_size_with_another_write_time_is_settled_by_the_pass_the_touched_file_stays_and_the_changed_one_is_a_conflict()
    {
        foreach (bool changed in new[] { false, true })
        {
            using var dir = new JournalTempDir();
            var (session, file, time) = Crashed(dir, "original text");
            using var closing = session;
            if (changed) File.WriteAllBytes(file, U("ORIGINAL TEXT"));
            File.SetLastWriteTimeUtc(file, time.AddMinutes(7));

            var gate = new TaskCompletionSource();
            var recovering = Task.Run(() => Recover(session, gate));
            Assert.False(recovering.Wait(TimeSpan.FromMilliseconds(300)), "only the content can tell a touched file from a changed one, so recovery waits for the pass here");
            gate.SetResult();
            Assert.True(recovering.Wait(Patience));
            Assert.Equal(changed ? RecoveryStatus.Conflict : RecoveryStatus.Restored, recovering.Result.Status);
            recovering.Result.Document?.Dispose();
        }
    }
}

using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.History;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class FileHistoryTests
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now = start;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class SimulatedCrash : Exception;

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static (FileHistory History, Clock Clock) Make(JournalTempDir dir, HistoryPolicy? policy = null)
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (new FileHistory(dir.Combine("history"), policy, clock), clock);
    }

    [Fact]
    public void EveryRevisionCanBeReadBackAfterRestartOfTheStore()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        for (int i = 0; i < 5; i++, clock.Advance(TimeSpan.FromSeconds(1)))
            Assert.Equal(HistoryStatus.Recorded, h.Record(file, U("v" + i), HistorySource.Save).Status);

        var reopened = new FileHistory(dir.Combine("history"));
        var list = reopened.List(file);
        Assert.Equal(5, list.Count);
        Assert.Equal(["v4", "v3", "v2", "v1", "v0"], list.Select(r => Encoding.UTF8.GetString(reopened.Read(file, r.Id))));
    }

    [Fact]
    public void SameBytesAsTheNewestRevisionAddNothing()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        h.Record(file, U("x"), HistorySource.Save);
        Assert.Equal(HistoryStatus.Unchanged, h.Record(file, U("x"), HistorySource.Save).Status);
        Assert.Single(h.List(file));
    }

    [Fact]
    public void SameInstantStillGivesDistinctOrderedIds()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        for (int i = 0; i < 3; i++) h.Record(file, U("v" + i), HistorySource.Save);
        var ids = h.List(file).Select(r => r.Id).ToList();
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(x => x, StringComparer.Ordinal), ids);
    }

    [Fact]
    public void FormatFromANewerEditorIsRefusedAndLeftAlone()
    {
        using var dir = new JournalTempDir();
        Directory.CreateDirectory(dir.Combine("history"));
        File.WriteAllText(dir.Combine("history/format"), (FileHistory.FormatVersion + 1).ToString());
        var h = new FileHistory(dir.Combine("history"));
        Assert.Throws<HistoryFormatException>(() => h.Record(dir.Combine("a.txt"), U("x"), HistorySource.Save));
        Assert.Equal((FileHistory.FormatVersion + 1).ToString(), File.ReadAllText(dir.Combine("history/format")));
    }

    [Fact]
    public void HistoryOfTheFirstFormatIsReadAndTheMarkerMovesToTheCurrentVersion()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        var first = h.Record(file, U("written by the first version"), HistorySource.Save).Revision!;
        File.WriteAllText(dir.Combine("history/format"), "1");

        var later = new FileHistory(dir.Combine("history"), null, clock);
        Assert.Equal("written by the first version", Encoding.UTF8.GetString(later.Read(file, first.Id)));
        Assert.Equal(HistoryStatus.Recorded, later.Record(file, U("and one more"), HistorySource.Save).Status);
        Assert.Equal(FileHistory.FormatVersion.ToString(), File.ReadAllText(dir.Combine("history/format")).Trim());
        Assert.Equal(2, later.List(file).Count);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("meta")]
    public void CrashWhileWritingARevisionLeavesTheHistoryIntact(string stage)
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        File.WriteAllText(file, "on disk");
        h.Record(file, U("old"), HistorySource.Save);
        clock.Advance(TimeSpan.FromSeconds(1));

        h.Stage = s => { if (s == stage) throw new SimulatedCrash(); };
        Assert.Throws<SimulatedCrash>(() => h.Record(file, U("new"), HistorySource.Save));

        foreach (var f in Directory.GetFiles(dir.Combine("history"), "*", SearchOption.AllDirectories)) if (!f.EndsWith(".json") && !f.EndsWith("path.txt") && !f.EndsWith("format")) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-1));
        var after = new FileHistory(dir.Combine("history"), null, clock);
        var list = after.List(file);
        Assert.Equal("old", Encoding.UTF8.GetString(after.Read(file, Assert.Single(list).Id)));
        Assert.Equal("on disk", File.ReadAllText(file));

        Assert.Equal(HistoryStatus.Recorded, after.Record(file, U("new"), HistorySource.Save).Status);
        Assert.Equal(2, after.List(file).Count);
        var folder = Directory.GetDirectories(dir.Combine("history")).Single();
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "blobs")).Length);
    }

    [Fact]
    public void FailedWriteIsReportedAndDamagesNothing()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        h.Record(file, U("old"), HistorySource.Save);
        h.Stage = s => { if (s == "meta") throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)); };
        var result = h.Record(file, U("new"), HistorySource.Save);
        Assert.Equal(HistoryStatus.Failed, result.Status);
        Assert.True(result.NoSpace);
        h.Stage = null;
        Assert.Equal("old", Encoding.UTF8.GetString(h.Read(file, Assert.Single(h.List(file)).Id)));
        var folder = Directory.GetDirectories(dir.Combine("history")).Single();
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "blobs")));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void CorruptedBlobIsDetectedOnRead()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        var rev = h.Record(file, U("abc"), HistorySource.Save).Revision!;
        var blob = Directory.GetFiles(dir.Combine("history"), rev.Id + ".rev", SearchOption.AllDirectories).Single();
        File.WriteAllText(blob, "abd");
        Assert.Throws<InvalidDataException>(() => h.Read(file, rev.Id));
    }

    [Fact]
    public void FileAboveTheSizeLimitReportsTooLargeAndKeepsEarlierRevisions()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir, new HistoryPolicy { MaxRevisionBytes = 4 });
        var file = dir.Combine("a.txt");
        Assert.Equal(HistoryStatus.Recorded, h.Record(file, U("abc"), HistorySource.Save).Status);
        Assert.Equal(HistoryStatus.TooLarge, h.Record(file, U("abcdef"), HistorySource.Save).Status);
        Assert.Single(h.List(file));
    }

    [Fact]
    public void SecretsFilesAreNotStoredUnlessAllowed()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        Assert.Equal(HistoryStatus.Skipped, h.Record(dir.Combine(".env"), U("KEY=value"), HistorySource.Save).Status);
        Assert.Empty(h.List(dir.Combine(".env")));
        var (allowed, _) = Make(dir, new HistoryPolicy { StoreSecretFiles = true });
        Assert.Equal(HistoryStatus.Recorded, allowed.Record(dir.Combine(".env"), U("KEY=value"), HistorySource.Save).Status);
    }

    [Fact]
    public async Task SaveAndExternalChangeBecomeRevisions()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        File.WriteAllText(file, "one\n");
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var rec = new HistoryRecorder(h, doc);
        await rec.Pending;
        Assert.Equal(HistorySource.Initial, Assert.Single(h.List(file)).Source);

        doc.Insert(0, U("zero\n"));
        Assert.True(doc.Save().Saved);
        await rec.Pending;
        Assert.Equal("zero\none\n", Encoding.UTF8.GetString(h.Read(file, h.List(file)[0].Id)));

        File.WriteAllText(file, "outside\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));
        Assert.NotNull(doc.CheckExternalChange());
        await rec.Pending;
        var newest = h.List(file)[0];
        Assert.Equal(HistorySource.External, newest.Source);
        Assert.Equal("outside\n", Encoding.UTF8.GetString(h.Read(file, newest.Id)));
        Assert.Equal(3, h.List(file).Count);
    }

    [Fact]
    public void RestoringASectionChangesOnlyThatSection()
    {
        var old = "a\nb\nc\nd\ne\nf\ng\nh\n";
        var current = "a\nB\nc\nd\ne\nf\nG\nh\n";
        Assert.Equal("a\nB\nc\nd\ne\nf\ng\nh\n", HistoryRestore.RestoreSection(current, old, 6, 7));
        Assert.Equal("a\nb\nc\nd\ne\nf\nG\nh\n", HistoryRestore.RestoreSection(current, old, 1, 2));
        Assert.Equal(old, HistoryRestore.RestoreSection(current, old, 0, 8));
        Assert.Equal(current, HistoryRestore.RestoreSection(current, old, 2, 5));
    }

    [Fact]
    public void RestoringASectionKeepsLineEndingsOutsideIt()
    {
        var old = "a\r\nb\r\nc\r\nd\r\n";
        var current = "a\r\nX\r\nc\r\nd\n";
        Assert.Equal("a\r\nb\r\nc\r\nd\n",HistoryRestore.RestoreSection(current, old, 1, 2));
    }

    // ---- retention ----

    [Fact]
    public void LimitOnCountDeletesTheOldestUnpinnedAndNeverThePinned()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir, new HistoryPolicy { MaxRevisionsPerFile = 2 });
        var file = dir.Combine("a.txt");
        var first = h.Record(file, U("v0"), HistorySource.Save).Revision!;
        h.Pin(file, first.Id, "pending-review");
        for (int i = 1; i <= 4; i++) { clock.Advance(TimeSpan.FromSeconds(1)); h.Record(file, U("v" + i), HistorySource.Save); }
        var texts = h.List(file).Select(r => Encoding.UTF8.GetString(h.Read(file, r.Id))).ToList();
        Assert.Equal(["v4", "v0"], texts);
        h.Unpin(file, first.Id, "pending-review");
        h.Prune(file); clock.Advance(TimeSpan.FromSeconds(1)); h.Record(file, U("v5"), HistorySource.Save);
        Assert.Equal(["v5", "v4"], h.List(file).Select(r => Encoding.UTF8.GetString(h.Read(file, r.Id))));
    }

    [Fact]
    public void AgeLimitDropsOldRevisionsButKeepsTheNewest()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir, new HistoryPolicy { MaxAge = TimeSpan.FromDays(1) });
        var file = dir.Combine("a.txt");
        h.Record(file, U("v0"), HistorySource.Save);
        clock.Advance(TimeSpan.FromDays(3));
        h.Prune(file);
        Assert.Equal("v0",Encoding.UTF8.GetString(h.Read(file, Assert.Single(h.List(file)).Id)));
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(file, U("v1"), HistorySource.Save);
        Assert.Equal(["v1"], h.List(file).Select(r => Encoding.UTF8.GetString(h.Read(file, r.Id))));
    }

    [Fact]
    public void TotalQuotaKeepsPinnedBaseEvenWhenItCannotBeMet()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir, new HistoryPolicy { MaxTotalBytes = 10 });
        var a = dir.Combine("a.txt");
        var b = dir.Combine("b.txt");
        var basis = h.Record(a, U("0123456789"), HistorySource.Save).Revision!;
        h.Pin(a, basis.Id, "review");
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(a, U("0123456789AB"), HistorySource.Save);
        h.Record(b, U("bbbbbbbb"), HistorySource.Save);
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(b, U("cccccccc"), HistorySource.Save);

        h.PruneAll();

        Assert.Equal("0123456789", Encoding.UTF8.GetString(h.Read(a, basis.Id)));
        Assert.Equal(2, h.List(a).Count);
        Assert.Single(h.List(b));
    }

    // ---- labels and search ----

    [Fact]
    public void LabelledRevisionIsFoundByLabelAndLabelSurvivesRestart()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        var r1 = h.Record(file, U("v1"), HistorySource.Save).Revision!;
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(file, U("v2"), HistorySource.Save);
        h.SetLabel(file, r1.Id, "Release 1.0");

        var again = new FileHistory(dir.Combine("history"));
        Assert.Equal(r1.Id, Assert.Single(again.List(file, "release")).Id);
        Assert.Empty(again.List(file, "nothing"));
    }

    [Fact]
    public async Task SearchFindsTextThatIsOnlyInAnOlderRevision()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        h.Record(file, U("alpha\nneedle here\n"), HistorySource.Save);
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(file, U("alpha\ngone\n"), HistorySource.Save);

        var hits = await h.SearchAsync(file, new SearchPattern("needle"));
        var hit = Assert.Single(hits);
        Assert.Equal(h.List(file)[1].Id, hit.Revision.Id);
        Assert.Contains("needle", hit.Preview);
        Assert.Empty(await h.SearchAsync(file, new SearchPattern("absent")));
    }

    // ---- large files kept as patches (P5.3.1) ----

    private static byte[] Splice(byte[] bytes, int at, int remove, byte[] insert) => [.. bytes[..at], .. insert, .. bytes[(at + remove)..]];

    private static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Makes a history whose files above 100 000 bytes are kept as patches, and records a first, whole copy of <paramref name="first"/>.</summary>
    private static (FileHistory History, Clock Clock, string File, HistoryRevision Anchor) PatchedHistory(JournalTempDir dir, byte[] first, HistoryPolicy? policy = null)
    {
        var (h, clock) = Make(dir, policy ?? new HistoryPolicy { PatchAboveBytes = 100_000 });
        string file = dir.Combine("big.bin");
        File.WriteAllBytes(file, first);
        var anchor = h.RecordFile(file, HistorySource.Initial).Revision!;
        return (h, clock, file, anchor);
    }

    [Fact]
    public void ARevisionOfALargeFileIsAPatchAgainstTheFirstWholeCopyAndReadsBackExactly()
    {
        using var dir = new JournalTempDir();
        byte[] v0 = RandomBytes(1_000_000, 5);
        var (h, clock, file, anchor) = PatchedHistory(dir, v0);
        Assert.False(anchor.IsPatch);

        var versions = new List<byte[]> { v0 };
        for (int i = 1; i <= 4; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            var next = Splice(versions[^1], i * 100_000, i % 2, Encoding.UTF8.GetBytes("inserted text number " + i)); // an insertion, and every other time a removed byte
            File.WriteAllBytes(file, next);
            var revision = h.RecordFile(file, HistorySource.Save).Revision!;
            Assert.True(revision.IsPatch);
            Assert.Equal(anchor.Id, revision.BaseId);
            Assert.True(revision.PatchBytes < 40_000, $"{revision.PatchBytes} bytes for a few edits of a megabyte");
            Assert.Equal(next.Length, revision.Size);
            versions.Add(next);
        }

        var reopened = new FileHistory(dir.Combine("history"));
        var list = reopened.List(file);
        Assert.Equal(5, list.Count);
        for (int i = 0; i < 5; i++)
        {
            var stored = list[4 - i];
            Assert.True(versions[i].AsSpan().SequenceEqual(reopened.Read(file, stored.Id)), $"revision {i}");
        }
        Assert.True(reopened.TotalBytes() < 2 * v0.Length, "the patches take far less than whole copies");
    }

    [Fact]
    public void AnExportAndAPreviewOfAPatchRevisionAreTheBytesOfThatRevision()
    {
        using var dir = new JournalTempDir();
        byte[] v0 = RandomBytes(300_000, 6);
        var (h, clock, file, _) = PatchedHistory(dir, v0);
        clock.Advance(TimeSpan.FromSeconds(1));
        byte[] v1 = Splice(v0, 150_000, 0, "middle"u8.ToArray());
        File.WriteAllBytes(file, v1);
        var patch = h.RecordFile(file, HistorySource.Save).Revision!;
        Assert.True(patch.IsPatch);

        string copy = dir.Combine("copy.bin");
        h.Export(file, patch.Id, copy);
        Assert.True(File.ReadAllBytes(copy).AsSpan().SequenceEqual(v1));
        Assert.True(h.ReadPrefix(file, patch.Id, 1000).AsSpan().SequenceEqual(v1.AsSpan(0, 1000)));
        Assert.True(h.ReadPrefix(file, patch.Id, v1.Length + 10).AsSpan().SequenceEqual(v1));
    }

    [Fact]
    public void ARevisionThatDiffersTooMuchIsANewWholeCopyAndTheNextPatchesAreAgainstIt()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, anchor) = PatchedHistory(dir, RandomBytes(400_000, 7));
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, RandomBytes(400_000, 8));
        var whole = h.RecordFile(file, HistorySource.Save).Revision!;
        Assert.False(whole.IsPatch);

        clock.Advance(TimeSpan.FromSeconds(1));
        byte[] next = Splice(File.ReadAllBytes(file), 1000, 0, "x"u8.ToArray());
        File.WriteAllBytes(file, next);
        var patch = h.RecordFile(file, HistorySource.Save).Revision!;
        Assert.Equal(whole.Id, patch.BaseId);
        Assert.NotEqual(anchor.Id, patch.BaseId);
    }

    [Fact]
    public void ARevisionIsNotAPatchWhenTheFileIsNotLargeEnough()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, _) = PatchedHistory(dir, RandomBytes(50_000, 9)); // below the 100 000 bytes of the policy
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 100, 0, "x"u8.ToArray()));
        Assert.False(h.RecordFile(file, HistorySource.Save).Revision!.IsPatch);
    }

    [Fact]
    public void ARevisionGivenAsBytesInMemoryIsAWholeCopy()
    {
        using var dir = new JournalTempDir();
        byte[] v0 = RandomBytes(300_000, 2);
        var (h, clock, file, _) = PatchedHistory(dir, v0);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(h.Record(file, Splice(v0, 100, 0, "x"u8.ToArray()), HistorySource.Save).Revision!.IsPatch); // a patch is made from a file on disk
    }

    [Fact]
    public void TheSameBytesAsTheNewestPatchRevisionAddNothing()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, _) = PatchedHistory(dir, RandomBytes(300_000, 10));
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 5000, 0, "z"u8.ToArray()));
        var patch = h.RecordFile(file, HistorySource.Save).Revision!;
        Assert.Equal(HistoryStatus.Unchanged, h.RecordFile(file, HistorySource.Save).Status);
        Assert.Equal(2, h.List(file).Count);
        Assert.Equal(patch.Id, h.List(file)[0].Id);
    }

    [Fact]
    public void RetentionKeepsTheWholeCopyThatAKeptPatchNeedsAndDeletesPatchesLikeAnyRevision()
    {
        using var dir = new JournalTempDir();
        byte[] v0 = RandomBytes(300_000, 11);
        var (h, clock, file, anchor) = PatchedHistory(dir, v0, new HistoryPolicy { PatchAboveBytes = 100_000, MaxRevisionsPerFile = 2 });
        byte[] current = v0;
        for (int i = 1; i <= 4; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            current = Splice(current, 1000 * i, 0, [(byte)'a', (byte)i]);
            File.WriteAllBytes(file, current);
            Assert.True(h.RecordFile(file, HistorySource.Save).Revision!.IsPatch);
        }

        var kept = h.List(file);
        Assert.Equal(2, kept.Count); // the newest patch and the whole copy it stands on
        Assert.Contains(kept, r => r.Id == anchor.Id);
        Assert.True(current.AsSpan().SequenceEqual(h.Read(file, kept[0].Id)));

        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, RandomBytes(300_000, 12));
        var whole = h.RecordFile(file, HistorySource.Save).Revision!; // a new whole copy: the patch is the oldest revision nobody needs
        Assert.False(whole.IsPatch);
        Assert.Equal([whole.Id, anchor.Id], h.List(file).Select(r => r.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(Directory.GetDirectories(dir.Combine("history")).Single(), "blobs"), "*.dlt"));

        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, RandomBytes(300_000, 13));
        var third = h.RecordFile(file, HistorySource.Save).Revision!; // now the first whole copy is needed by nothing and goes
        Assert.Equal([third.Id, whole.Id], h.List(file).Select(r => r.Id));
    }

    [Fact]
    public void APinnedPatchKeepsItsWholeCopy()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, anchor) = PatchedHistory(dir, RandomBytes(300_000, 13), new HistoryPolicy { PatchAboveBytes = 100_000, MaxRevisionsPerFile = 1 });
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 10, 0, "p"u8.ToArray()));
        var pinned = h.RecordFile(file, HistorySource.Save).Revision!;
        h.Pin(file, pinned.Id, "review");
        for (int i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 20 + i, 0, "q"u8.ToArray()));
            h.RecordFile(file, HistorySource.Save);
        }
        h.PruneAll();

        var ids = h.List(file).Select(r => r.Id).ToList();
        Assert.Contains(pinned.Id, ids);
        Assert.Contains(anchor.Id, ids);
        Assert.NotEmpty(h.Read(file, pinned.Id));
    }

    [Fact]
    public void AMissingWholeCopyIsReportedWhenItsPatchIsRead()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, anchor) = PatchedHistory(dir, RandomBytes(300_000, 14));
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 10, 0, "p"u8.ToArray()));
        var patch = h.RecordFile(file, HistorySource.Save).Revision!;

        File.Delete(Directory.GetFiles(dir.Combine("history"), anchor.Id + ".rev", SearchOption.AllDirectories).Single());

        Assert.Throws<InvalidDataException>(() => h.Read(file, patch.Id));
    }

    [Fact]
    public void ADamagedPatchIsDetectedOnRead()
    {
        using var dir = new JournalTempDir();
        var (h, clock, file, _) = PatchedHistory(dir, RandomBytes(300_000, 15));
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(File.ReadAllBytes(file), 10, 0, "patched text"u8.ToArray()));
        var patch = h.RecordFile(file, HistorySource.Save).Revision!;

        string blob = Directory.GetFiles(dir.Combine("history"), patch.Id + ".dlt", SearchOption.AllDirectories).Single();
        byte[] bytes = File.ReadAllBytes(blob);
        bytes[^3] ^= 0x55;
        File.WriteAllBytes(blob, bytes);

        Assert.Throws<InvalidDataException>(() => h.Read(file, patch.Id));
    }

    [Fact]
    public async Task RevisionsKeptAsPatchesAreNotSearched()
    {
        using var dir = new JournalTempDir();
        byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 20_000).Select(i => $"row {i} of the file\n")));
        var (h, clock, file, anchor) = PatchedHistory(dir, text);
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(text, 100, 0, "a needle in a patch\n"u8.ToArray()));
        Assert.True(h.RecordFile(file, HistorySource.Save).Revision!.IsPatch);

        Assert.Empty(await h.SearchAsync(file, new SearchPattern("needle")));
        Assert.Equal(anchor.Id, Assert.Single(await h.SearchAsync(file, new SearchPattern("row 5 of"))).Revision.Id);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("meta")]
    public void CrashWhileWritingAPatchLeavesTheHistoryIntact(string stage)
    {
        using var dir = new JournalTempDir();
        byte[] v0 = RandomBytes(300_000, 16);
        var (h, clock, file, anchor) = PatchedHistory(dir, v0);
        clock.Advance(TimeSpan.FromSeconds(1));
        File.WriteAllBytes(file, Splice(v0, 500, 0, "new"u8.ToArray()));

        h.Stage = s => { if (s == stage) throw new SimulatedCrash(); };
        Assert.Throws<SimulatedCrash>(() => h.RecordFile(file, HistorySource.Save));

        foreach (var f in Directory.GetFiles(dir.Combine("history"), "*", SearchOption.AllDirectories)) if (!f.EndsWith(".json") && !f.EndsWith("path.txt") && !f.EndsWith("format")) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddHours(-1));
        var after = new FileHistory(dir.Combine("history"), new HistoryPolicy { PatchAboveBytes = 100_000 }, clock);
        Assert.Equal(anchor.Id, Assert.Single(after.List(file)).Id);
        Assert.Equal(HistoryStatus.Recorded, after.RecordFile(file, HistorySource.Save).Status);
        var folder = Directory.GetDirectories(dir.Combine("history")).Single();
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "blobs"), "*.dlt"));
    }

    [Fact]
    public async Task ALargeFileIsCopiedAtTheFirstEditAndSavedAsAPatch()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir, new HistoryPolicy { PatchAboveBytes = 10_000 });
        var file = dir.Combine("a.txt");
        string original = string.Concat(Enumerable.Range(0, 5000).Select(i => $"row number {i}\n"));
        File.WriteAllText(file, original);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var rec = new HistoryRecorder(h, doc);
        await rec.Pending;
        Assert.Empty(h.List(file)); // a file that is only read is not copied

        doc.Insert(0, U("edited\n"));
        await rec.Pending;
        var initial = Assert.Single(h.List(file));
        Assert.Equal(HistorySource.Initial, initial.Source);
        Assert.Equal(original, Encoding.UTF8.GetString(h.Read(file, initial.Id)));

        Assert.True(doc.Save().Saved);
        await rec.Pending;
        var saved = h.List(file)[0];
        Assert.True(saved.IsPatch);
        Assert.Equal(initial.Id, saved.BaseId);
        Assert.Equal("edited\n" + original, Encoding.UTF8.GetString(h.Read(file, saved.Id)));
    }

    [Fact]
    public async Task SavesThatArriveWhileARecordIsQueuedAreOneRecord()
    {
        using var dir = new JournalTempDir();
        var (h, _) = Make(dir);
        var file = dir.Combine("a.txt");
        File.WriteAllText(file, "orig\n");
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        int blobs = 0;
        h.Stage = s =>
        {
            if (s == "blob" && Interlocked.Increment(ref blobs) == 2)
            {
                started.Set();
                gate.Wait(TimeSpan.FromSeconds(30));
            }
        };
        using var rec = new HistoryRecorder(h, doc);
        await rec.Pending; // the first record of the file

        doc.Insert(0, U("1"));
        Assert.True(doc.Save().Saved);
        Assert.True(started.Wait(TimeSpan.FromSeconds(10))); // the record of this save runs and holds the history
        doc.Insert(0, U("2"));
        Assert.True(doc.Save().Saved); // queued behind it
        doc.Insert(0, U("3"));
        Assert.True(doc.Save().Saved); // the queued record reads the file when it starts, so it takes this one too
        gate.Set();
        await rec.Pending;

        var list = h.List(file);
        Assert.Equal(3, list.Count);
        Assert.Equal("321orig\n", Encoding.UTF8.GetString(h.Read(file, list[0].Id)));
    }
}

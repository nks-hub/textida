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
        File.WriteAllText(dir.Combine("history/format"), "2");
        var h = new FileHistory(dir.Combine("history"));
        Assert.Throws<HistoryFormatException>(() => h.Record(dir.Combine("a.txt"), U("x"), HistorySource.Save));
        Assert.Equal("2", File.ReadAllText(dir.Combine("history/format")));
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
}

using System.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.History;
using NksHub.Textida.Document.Contract;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.3.4: the counts of lines of the revisions of a file, the summary of its history and the notice that the revisions changed.</summary>
public class HistoryStatsTests
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now = start;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    private static (FileHistory History, Clock Clock) Make(JournalTempDir dir, HistoryPolicy? policy = null)
    {
        var clock = new Clock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (new FileHistory(dir.Combine("history"), policy, clock), clock);
    }

    private static Dictionary<string, RevisionChange?> Count(FileHistory h, string file, long max = long.MaxValue, Func<HistoryRevision, HistoryRevision?, bool>? wanted = null)
    {
        var counted = new Dictionary<string, RevisionChange?>();
        h.CountChanges(file, TextCodec.Utf8, max, wanted ?? ((_, _) => true), (r, change) => counted[r.Id] = change);
        return counted;
    }

    [Fact]
    public void Between_counts_the_lines_the_line_diff_adds_and_removes()
    {
        var change = HistoryStats.Between(U("a\nb\nc\n"), U("a\nX\nc\nd\n"), TextCodec.Utf8);
        Assert.Equal((2L, 1L, true), (change.Added, change.Removed, change.Exact));
        var none = HistoryStats.Between(U("same\n"), U("same\n"), TextCodec.Utf8);
        Assert.Equal((0L, 0L), (none.Added, none.Removed));
        var all = HistoryStats.Between(U(""), U("1\n2\n3\n"), TextCodec.Utf8);
        Assert.Equal(3, all.Added);
    }

    [Fact]
    public void Every_revision_has_the_change_against_the_one_before_it_and_the_oldest_has_none()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        string[] versions = ["a\nb\nc\n", "a\nB\nc\n", "a\nB\nc\nd\ne\n", "e\n"];
        foreach (var version in versions)
        {
            h.Record(file, U(version), HistorySource.Save);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var list = h.List(file); // newest first
        var counted = Count(h, file);

        Assert.Equal(4, counted.Count);
        Assert.Null(counted[list[3].Id]); // the oldest
        Assert.Equal((1L, 1L), (counted[list[2].Id]!.Value.Added, counted[list[2].Id]!.Value.Removed));
        Assert.Equal((2L, 0L), (counted[list[1].Id]!.Value.Added, counted[list[1].Id]!.Value.Removed));
        Assert.Equal((0L, 4L), (counted[list[0].Id]!.Value.Added, counted[list[0].Id]!.Value.Removed)); // a b c d e became e: the lines a, B, c and d went
        // the numbers are those of the comparison of the two revisions, which is what the panel shows
        for (int i = 0; i < 3; i++)
        {
            var expected = HistoryStats.Between(h.Read(file, list[i + 1].Id), h.Read(file, list[i].Id), TextCodec.Utf8);
            Assert.Equal(expected, counted[list[i].Id]);
        }
    }

    [Fact]
    public void A_revision_kept_as_a_patch_is_counted_like_a_whole_copy_and_a_large_one_is_left_out()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir, new HistoryPolicy { PatchAboveBytes = 20_000 });
        var file = dir.Combine("big.txt");
        string Text(Func<int, string> line) => string.Concat(Enumerable.Range(0, 2000).Select(i => line(i) + "\n"));
        string[] versions = [Text(i => $"line {i} of the file"), Text(i => i == 700 ? "changed" : $"line {i} of the file"), Text(i => i is 700 or 1500 ? "changed again" : $"line {i} of the file")];
        foreach (var version in versions)
        {
            File.WriteAllText(file, version);
            Assert.Equal(HistoryStatus.Recorded, h.RecordFile(file, HistorySource.Save).Status);
            clock.Advance(TimeSpan.FromSeconds(1));
        }
        var list = h.List(file);
        Assert.Contains(list, r => r.IsPatch);

        var counted = Count(h, file);
        Assert.Equal((1L, 1L), (counted[list[1].Id]!.Value.Added, counted[list[1].Id]!.Value.Removed));
        Assert.Equal((2L, 2L), (counted[list[0].Id]!.Value.Added, counted[list[0].Id]!.Value.Removed));

        var limited = Count(h, file, max: list.Min(r => r.Size) - 1); // every revision is above the limit
        Assert.Equal(3, limited.Count);
        Assert.All(limited.Values, change => Assert.Null(change));
    }

    [Fact]
    public void Only_the_revisions_asked_for_are_counted_and_each_is_asked_with_the_one_before_it()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        for (int i = 0; i < 4; i++, clock.Advance(TimeSpan.FromSeconds(1))) h.Record(file, U($"v{i}\n"), HistorySource.Save);
        var list = h.List(file);
        var asked = new List<(string Id, string? Before)>();

        var counted = Count(h, file, wanted: (r, before) =>
        {
            asked.Add((r.Id, before?.Id));
            return r.Id == list[1].Id;
        });

        Assert.Equal([list[1].Id], counted.Keys);
        Assert.Equal(list.Select((r, i) => (r.Id, i + 1 < list.Count ? list[i + 1].Id : null)), asked);
        Assert.Empty(Count(h, file, wanted: (_, _) => false));
    }

    [Fact]
    public void Counting_stops_when_asked_to()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        for (int i = 0; i < 3; i++, clock.Advance(TimeSpan.FromSeconds(1))) h.Record(file, U($"v{i}\n"), HistorySource.Save);
        using var cts = new CancellationTokenSource();
        int reported = 0;
        Assert.Throws<OperationCanceledException>(() => h.CountChanges(file, TextCodec.Utf8, long.MaxValue, (_, _) => true, (_, _) =>
        {
            reported++;
            cts.Cancel();
        }, cts.Token));
        Assert.Equal(1, reported);
    }

    [Fact]
    public void The_summary_says_how_many_revisions_how_much_disk_they_take_and_when_the_first_and_the_last_were_made()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        Assert.Equal(new HistorySummary(0, 0, null, null), HistorySummary.Of(h.List(file)));
        h.Record(file, U("one\n"), HistorySource.Save);
        clock.Advance(TimeSpan.FromHours(5));
        h.Record(file, U("one\ntwo\n"), HistorySource.Save);

        var summary = HistorySummary.Of(h.List(file));
        Assert.Equal((2, 4L + 8L), (summary.Revisions, summary.StoredBytes));
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), summary.OldestUtc);
        Assert.Equal(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc), summary.NewestUtc);
    }

    [Fact]
    public void The_store_says_when_the_revisions_of_a_file_changed_and_not_when_nothing_did()
    {
        using var dir = new JournalTempDir();
        var (h, clock) = Make(dir);
        var file = dir.Combine("a.txt");
        var other = dir.Combine("b.txt");
        var changed = new List<string>();
        h.FileChanged += changed.Add;

        h.Record(file, U("one\n"), HistorySource.Save);
        Assert.Equal([file], changed);
        h.Record(file, U("one\n"), HistorySource.Save); // the same bytes: no new revision
        Assert.Single(changed);
        clock.Advance(TimeSpan.FromSeconds(1));
        h.Record(file, U("two\n"), HistorySource.Save);
        var id = h.List(file)[0].Id;
        h.SetLabel(file, id, "label");
        h.Pin(file, id, "user");
        h.Unpin(file, id, "user");
        h.Prune(file);
        Assert.Equal(6, changed.Count); // two revisions, a label, a pin, an unpin and a pruning
        Assert.All(changed, c => Assert.Equal(file, c));

        h.Record(other, U("x\n"), HistorySource.Save);
        Assert.Equal(other, changed[^1]);
        var off = new FileHistory(dir.Combine("off"), new HistoryPolicy { Enabled = false });
        int raised = 0;
        off.FileChanged += _ => raised++;
        off.Record(file, U("x\n"), HistorySource.Save);
        Assert.Equal(0, raised);
    }
}

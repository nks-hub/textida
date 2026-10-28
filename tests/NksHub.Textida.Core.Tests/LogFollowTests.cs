using System.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.4.3, P9.4.4: a growing log is read only from where it was read, and a truncated or replaced log is noticed.</summary>
public class LogFollowTests
{
    /// <summary>A log in memory that the test grows, shrinks or replaces; it counts the bytes the follower reads.</summary>
    private sealed class MemoryLog : ILogSource
    {
        private byte[] _bytes = [];
        public long BytesRead;
        public long Identity = 1;
        public long Stamp;
        public bool Exists = true;

        public void Append(string text) => _bytes = [.. _bytes, .. Encoding.UTF8.GetBytes(text)];
        public void Set(string text) => _bytes = Encoding.UTF8.GetBytes(text);
        public byte[] Bytes => _bytes;
        public LogSourceState? Probe() => Exists ? new LogSourceState(_bytes.Length, Identity, Stamp) : null;

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Clamp(_bytes.Length - offset, 0, destination.Length);
            _bytes.AsSpan((int)offset, count).CopyTo(destination);
            BytesRead += count;
            return count;
        }
    }

    private static readonly LogFilter Errors = new([LogLevel.Error]);

    private static LogFollower Follow(ILogSource source, LogFilter? filter = null, int maxMatches = int.MaxValue) => new(source, TextCodec.Utf8, LogLevelRules.Default, filter ?? Errors, maxMatches);

    /// <summary>Starts of the lines the filter keeps, found by walking the whole text.</summary>
    private static long[] Naive(byte[] bytes, Func<string, bool> keep)
    {
        var starts = new List<long>();
        long pos = 0;
        foreach (string line in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            if (keep(line)) starts.Add(pos);
            pos += Encoding.UTF8.GetByteCount(line) + 1;
        }
        return [.. starts];
    }

    private static bool IsError(string line) => LogLevelRules.Default.Classify(line) == LogLevel.Error;

    [Fact]
    public void Appended_lines_are_examined_alone_and_the_matches_agree_with_a_walk_over_the_whole_file()
    {
        var log = new MemoryLog();
        var follower = Follow(log);
        var random = new Random(7);
        string[] samples = ["INFO started", "ERROR disk full ěšč", "WARN slow", "DEBUG x=1", "plain", "FATAL out of memory"];
        var first = string.Concat(Enumerable.Range(0, 3000).Select(i => samples[random.Next(samples.Length)] + " #" + i + "\n"));
        log.Append(first);
        var update = follower.Poll();
        Assert.Equal(log.Bytes.Length, update.BytesRead);
        Assert.Equal(Naive(log.Bytes, IsError), follower.Matches());

        for (int round = 0; round < 20; round++)
        {
            string added = string.Concat(Enumerable.Range(0, random.Next(1, 40)).Select(i => samples[random.Next(samples.Length)] + " #r" + round + "." + i + "\n"));
            log.Append(added);
            log.BytesRead = 0;
            update = follower.Poll();
            Assert.Equal(LogFollowChange.None, update.Change);
            // the added bytes and the head and tail samples checked and the new tail sample kept; nothing of the 100 KiB before
            Assert.True(log.BytesRead <= Encoding.UTF8.GetByteCount(added) + 3 * LogFollower.HeadBytes, log.BytesRead.ToString());
            Assert.Equal(Naive(log.Bytes, IsError), follower.Matches());
        }

        log.BytesRead = 0;
        Assert.Equal(0, follower.Poll().BytesRead);
        Assert.Equal(0, log.BytesRead);
    }

    [Fact]
    public void A_line_without_its_terminator_waits_and_is_read_whole_once_it_ends()
    {
        var log = new MemoryLog();
        var follower = Follow(log);
        log.Append("INFO a\nERR");
        follower.Poll();
        Assert.Empty(follower.Matches());
        Assert.Equal(7, follower.Consumed);
        log.Append("OR b\nINFO c\n");
        follower.Poll();
        Assert.Equal([7L], follower.Matches());
    }

    [Fact]
    public void Text_and_regex_filters_keep_only_the_lines_that_hold_them_with_a_level_when_one_is_asked()
    {
        var log = new MemoryLog();
        log.Append("ERROR db timeout\nINFO db ok\nERROR cache miss\nWARN db slow\n");
        Assert.Equal([0L], Follow(log, new LogFilter([LogLevel.Error], "db")).Tap(f => f.Poll()).Matches());
        var plain = Follow(log, new LogFilter([], "db")).Tap(f => f.Poll());
        Assert.Equal(Naive(log.Bytes, l => l.Contains("db")), plain.Matches());
        var regex = Follow(log, new LogFilter([LogLevel.Error, LogLevel.Warning], @"^\w+ (db|cache)", TextIsRegex: true)).Tap(f => f.Poll());
        Assert.Equal(Naive(log.Bytes, l => l.StartsWith("ERROR") || l.StartsWith("WARN")), regex.Matches());
        Assert.Throws<LogPatternException>(() => Follow(log, new LogFilter([], "(", TextIsRegex: true)));
        Assert.Empty(Follow(log, LogFilter.None).Tap(f => f.Poll()).Matches());
    }

    [Fact]
    public void Next_and_previous_match_are_found_from_any_offset()
    {
        var log = new MemoryLog();
        log.Append("ERROR a\nINFO\nERROR b\nINFO\nERROR c\n");
        var follower = Follow(log).Tap(f => f.Poll());
        Assert.Equal([0L, 13L, 26L], follower.Matches());
        Assert.Equal(13, follower.Next(0));
        Assert.Equal(13, follower.Next(5));
        Assert.Null(follower.Next(26));
        Assert.Equal(13, follower.Previous(26));
        Assert.Equal(0, follower.Previous(13));
        Assert.Null(follower.Previous(0));
    }

    [Fact]
    public void A_shorter_file_is_reported_as_truncated_and_reading_starts_again_with_the_old_matches_dropped()
    {
        var log = new MemoryLog();
        log.Append("ERROR one\nINFO two\nERROR three\n");
        var follower = Follow(log).Tap(f => f.Poll());
        Assert.Equal(2, follower.MatchCount);
        int epoch = follower.Epoch;

        log.Set("ERROR new\n");
        var update = follower.Poll();
        Assert.Equal(LogFollowChange.Truncated, update.Change);
        Assert.Equal(2, update.Invalidated);
        Assert.Equal(1, update.NewMatches);
        Assert.Equal([0L], follower.Matches());
        Assert.Equal(epoch + 1, follower.Epoch);

        log.Set("");
        Assert.Equal(LogFollowChange.Truncated, follower.Poll().Change);
        Assert.Empty(follower.Matches());
        log.Append("ERROR again\n");
        Assert.Equal(LogFollowChange.None, follower.Poll().Change);
        Assert.Equal([0L], follower.Matches());
    }

    [Fact]
    public void A_replaced_file_is_reported_as_rotated_even_when_it_is_longer_than_the_old_one()
    {
        var log = new MemoryLog();
        log.Append("2026-01-01 ERROR old one\nINFO old two\n");
        var follower = Follow(log).Tap(f => f.Poll());
        Assert.Equal([0L], follower.Matches());

        log.Set("2026-02-02 INFO new first line\nERROR new second line\nINFO third\nINFO fourth\n");
        var update = follower.Poll();
        Assert.Equal(LogFollowChange.Rotated, update.Change);
        Assert.Equal(1, update.Invalidated);
        Assert.Equal([31L], follower.Matches());
        Assert.Equal(log.Bytes.Length, follower.Consumed);
    }

    [Fact]
    public void A_file_with_another_identity_a_missing_file_and_its_return_are_rotation()
    {
        var log = new MemoryLog();
        log.Append("ERROR a\n");
        var follower = Follow(log).Tap(f => f.Poll());

        log.Identity = 2;
        log.Append("ERROR b\n");
        Assert.Equal(LogFollowChange.Rotated, follower.Poll().Change);
        Assert.Equal([0L, 8L], follower.Matches());

        log.Exists = false;
        Assert.Equal(LogFollowChange.Missing, follower.Poll().Change);
        Assert.Equal(LogFollowChange.None, follower.Poll().Change);
        Assert.Equal(2, follower.MatchCount);

        log.Exists = true;
        log.Set("INFO fresh\nERROR fresh\n");
        var back = follower.Poll();
        Assert.Equal(LogFollowChange.Rotated, back.Change);
        Assert.Equal(2, back.Invalidated);
        Assert.Equal([11L], follower.Matches());
    }

    private static string Banner => string.Concat(Enumerable.Range(0, 300).Select(i => $"# banner line {i} of the log header\n"));

    [Fact]
    public void A_longer_file_with_the_same_start_and_no_identity_is_rotation_when_the_bytes_before_the_offset_differ()
    {
        var log = new MemoryLog { Identity = 0 }; // the platform gives no file identity, as outside Windows
        log.Append(Banner + string.Concat(Enumerable.Range(0, 400).Select(i => $"2026-01-01 {(i % 50 == 0 ? "ERROR" : "INFO")} old #{i}\n")));
        Assert.True(Banner.Length > 2 * LogFollower.HeadBytes);
        var follower = Follow(log).Tap(f => f.Poll());
        Assert.Equal(8, follower.MatchCount);

        log.Set(Banner + string.Concat(Enumerable.Range(0, 900).Select(i => $"2026-02-02 {(i % 300 == 0 ? "ERROR" : "INFO")} new #{i}\n")));
        Assert.True(log.Bytes.Length > follower.Consumed);
        var update = follower.Poll();

        Assert.Equal(LogFollowChange.Rotated, update.Change);
        Assert.Equal(8, update.Invalidated);
        Assert.Equal(3, follower.MatchCount);
        Assert.Equal(Naive(log.Bytes, IsError), follower.Matches());
        Assert.Equal(log.Bytes.Length, follower.Consumed);
    }

    [Fact]
    public void A_file_of_the_same_length_and_start_is_rotation_only_when_its_write_time_moved_and_the_bytes_differ()
    {
        var log = new MemoryLog { Identity = 0, Stamp = 10 };
        log.Append(Banner + "2026-01-01 ERROR old\n");
        var follower = Follow(log).Tap(f => f.Poll());
        log.BytesRead = 0;

        Assert.Equal(LogFollowChange.None, follower.Poll().Change);
        Assert.Equal(0, log.BytesRead); // nothing moved: the file is not read at all

        log.Stamp = 11; // touched, bytes unchanged
        Assert.Equal(LogFollowChange.None, follower.Poll().Change);
        Assert.Equal([Banner.Length + 0L], follower.Matches());

        log.Set(Banner + "2026-02-02 ERROR new\n");
        log.Stamp = 12;
        var update = follower.Poll();
        Assert.Equal(LogFollowChange.Rotated, update.Change);
        Assert.Equal(1, update.Invalidated);
        Assert.Equal(Naive(log.Bytes, IsError), follower.Matches());
    }

    [Fact]
    public void Only_the_newest_matches_are_kept_up_to_the_bound_and_the_dropped_ones_are_counted()
    {
        var log = new MemoryLog();
        const int bound = 100;
        log.Append(string.Concat(Enumerable.Range(0, 1000).Select(i => $"ERROR failure #{i}\n")));
        var follower = Follow(log, maxMatches: bound);

        var update = follower.Poll();
        long kept = follower.MatchCount;
        Assert.InRange(kept, bound * 3 / 4, bound);
        Assert.Equal(1000, update.NewMatches); // found, whether still kept or not
        Assert.Equal(1000 - kept, update.Dropped);
        Assert.Equal(update.Dropped, follower.Dropped);
        Assert.Equal(Naive(log.Bytes, IsError).TakeLast((int)kept), follower.Matches());
        Assert.Equal(Enumerable.Range(1000 - (int)kept, (int)kept).Select(i => (long)i), follower.MatchLines().Select(m => m.Line));
        Assert.Equal(follower.Matches()[0], follower.Next(0)); // a search from the start finds the oldest match that is still there

        log.Append(string.Concat(Enumerable.Range(1000, 500).Select(i => $"ERROR failure #{i}\n")));
        update = follower.Poll();
        Assert.InRange(follower.MatchCount, bound * 3 / 4, bound);
        Assert.Equal(1500 - follower.MatchCount, follower.Dropped);
        Assert.Equal(follower.Dropped - (1000 - kept), update.Dropped);
        Assert.Equal(Naive(log.Bytes, IsError).TakeLast((int)follower.MatchCount), follower.Matches());

        log.Set("ERROR new file\n"); // truncated: the old matches and their count are gone
        Assert.Equal(LogFollowChange.Truncated, follower.Poll().Change);
        Assert.Equal(0, follower.Dropped);
        Assert.Equal([0L], follower.Matches());
    }

    [Fact]
    public void A_bound_of_one_keeps_the_last_match_and_a_bound_below_one_is_refused()
    {
        var log = new MemoryLog();
        log.Append("ERROR a\nERROR b\nERROR c\n");
        var follower = Follow(log, maxMatches: 1).Tap(f => f.Poll());
        Assert.Equal([16L], follower.Matches());
        Assert.Equal(2, follower.Dropped);
        Assert.Throws<ArgumentOutOfRangeException>(() => Follow(log, maxMatches: 0));
    }

    [Fact]
    public void A_cancelled_poll_goes_on_where_it_stopped()
    {
        var log = new MemoryLog();
        log.Append(string.Concat(Enumerable.Repeat("ERROR line of the log, long enough to fill blocks ........\n", 60_000)));
        Assert.True(log.Bytes.Length > 3 * LogScan.BlockBytes);
        var follower = Follow(log);
        using var cts = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => follower.Poll(cts.Token, _ => cts.Cancel()));
        Assert.InRange(follower.Consumed, 1, log.Bytes.Length - 1);
        follower.Poll();
        Assert.Equal(Naive(log.Bytes, IsError), follower.Matches());
    }

    [Fact]
    public void A_real_file_is_followed_and_notices_truncation_and_a_rename_with_a_new_file()
    {
        using var temp = new TempFile("INFO a\nERROR b\n"u8.ToArray());
        var follower = Follow(new FileLogSource(temp.Path));
        Assert.Equal(LogFollowChange.None, follower.Poll().Change);
        Assert.Equal([7L], follower.Matches());

        File.AppendAllText(temp.Path, "ERROR c\n");
        follower.Poll();
        Assert.Equal([7L, 15L], follower.Matches());

        File.WriteAllText(temp.Path, "x\n");
        Assert.Equal(LogFollowChange.Truncated, follower.Poll().Change);

        string rotated = temp.Path + ".1";
        File.Move(temp.Path, rotated);
        Assert.Equal(LogFollowChange.Missing, follower.Poll().Change);
        File.WriteAllText(temp.Path, "ERROR in the new file\nINFO\n");
        var update = follower.Poll();
        Assert.Equal(LogFollowChange.Rotated, update.Change);
        Assert.Equal([0L], follower.Matches());
        File.Delete(rotated);
    }

    [Fact]
    public void A_real_file_reports_its_write_time_as_the_stamp_and_a_rewrite_with_equal_length_and_start_is_rotation()
    {
        string banner = Banner;
        using var temp = new TempFile(Encoding.UTF8.GetBytes(banner + "2026-01-01 ERROR old\n"));
        var source = new FileLogSource(temp.Path);
        var follower = Follow(source).Tap(f => f.Poll());
        long stamp = source.Probe()!.Value.Stamp;
        Assert.Equal(File.GetLastWriteTimeUtc(temp.Path).Ticks, stamp);

        File.WriteAllText(temp.Path, banner + "2026-02-02 ERROR new\n");
        File.SetLastWriteTimeUtc(temp.Path, new DateTime(stamp, DateTimeKind.Utc).AddMinutes(1));
        var update = follower.Poll();
        // on Windows the identity is the creation time, which a rewrite in place does not move; the bytes before the offset tell the file apart
        Assert.Equal(LogFollowChange.Rotated, update.Change);
        Assert.Equal([banner.Length + 0L], follower.Matches());
    }

    [Fact]
    public void Utf16_with_a_byte_order_mark_is_read_from_the_first_line()
    {
        var codec = TextCodec.Utf16LEBom;
        var bytes = new List<byte>(codec.Bom);
        bytes.AddRange(codec.Encode("INFO a\r\nERROR b\r\n"));
        var source = new FixedLog([.. bytes]);
        var follower = new LogFollower(source, codec, LogLevelRules.Default, Errors);
        follower.Poll();
        Assert.Equal([codec.Bom.Length + codec.Encode("INFO a\r\n").Length], follower.Matches());
        Assert.Equal([new LogMatchLine(1, LogLevel.Error)], follower.MatchLines());
    }

    [Fact]
    public void Matches_carry_their_line_and_level_and_a_crlf_split_between_two_polls_is_one_line_break()
    {
        var log = new MemoryLog();
        var follower = Follow(log, new LogFilter([], "db"));
        log.Append("INFO db up\r\nplain\r\nERROR db down\r");
        follower.Poll();
        Assert.Equal([new LogMatchLine(0, LogLevel.Info)], follower.MatchLines());
        Assert.Equal(19, follower.Consumed); // the CR at the end waits for its LF

        log.Append("\nWARN db slow\n\nno level db\n");
        follower.Poll();
        Assert.Equal([new LogMatchLine(0, LogLevel.Info), new(2, LogLevel.Error), new(3, LogLevel.Warning), new(5, null)], follower.MatchLines());
        Assert.Equal([0L, 19L, 34L, 48L], follower.Matches());

        log.Set("ERROR db gone\n");
        follower.Poll();
        Assert.Equal([new LogMatchLine(0, LogLevel.Error)], follower.MatchLines()); // a truncated log is counted from its first line again
    }

    [Fact]
    public void A_line_longer_than_a_block_with_its_crlf_across_the_block_edge_counts_once()
    {
        var log = new MemoryLog();
        // the CR is the last byte of the first block, the LF the first byte of the second
        log.Append("ERROR " + new string('a', LogScan.BlockBytes - 7) + "\r\nERROR next\n");
        var follower = Follow(log).Tap(f => f.Poll());
        Assert.Equal([new LogMatchLine(0, LogLevel.Error), new(1, LogLevel.Error)], follower.MatchLines());
        Assert.Equal([0L, LogScan.BlockBytes + 1L], follower.Matches());
    }

    [Fact]
    public void The_matches_found_so_far_can_be_read_while_a_large_log_is_still_being_examined()
    {
        var log = new MemoryLog();
        string filler = string.Concat(Enumerable.Repeat("INFO filler line\n", 70_000)); // a little more than a block
        for (int section = 0; section < 4; section++) log.Append($"ERROR in section {section}\n" + filler);
        var follower = Follow(log);

        var seen = new List<(long Reached, int Matches)>();
        var update = follower.Poll(default, reached => seen.Add((reached, follower.MatchLines().Length)));

        Assert.True(seen.Count >= 4, $"{seen.Count} progress reports");
        Assert.Equal(seen.Order(), seen); // the offset and the matches only grow
        Assert.Equal(log.Bytes.Length, seen[^1].Reached);
        Assert.InRange(seen[0].Matches, 1, 3); // the first report comes before the last section was examined
        Assert.Equal(4, seen[^1].Matches);
        Assert.Equal(4, update.NewMatches);
        Assert.Equal([0L, 70_001L, 140_002L, 210_003L], follower.MatchLines().Select(m => m.Line));
    }

    private sealed class FixedLog(byte[] bytes) : ILogSource
    {
        public LogSourceState? Probe() => new LogSourceState(bytes.Length, 1);
        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Clamp(bytes.Length - offset, 0, destination.Length);
            bytes.AsSpan((int)offset, count).CopyTo(destination);
            return count;
        }
    }

    /// <summary>A log of fixed 64 byte lines made up on demand that grows by raising <see cref="Length"/>; line n is an ERROR line when it is in <see cref="ErrorLines"/>.</summary>
    private sealed class GeneratedLog : ILogSource
    {
        public const int LineBytes = 64;
        public HashSet<long> ErrorLines { get; } = [];
        public long Length;
        public long BytesRead;

        public LogSourceState? Probe() => new LogSourceState(Length, 1);

        public int Read(long offset, Span<byte> destination)
        {
            int count = (int)Math.Min(destination.Length, Length - offset);
            BytesRead += count;
            Span<byte> line = stackalloc byte[LineBytes];
            for (int done = 0; done < count;)
            {
                long n = (offset + done) / LineBytes;
                int within = (int)((offset + done) % LineBytes);
                string text = $"{n,12} {(ErrorLines.Contains(n) ? "ERROR" : "INFO ")} ";
                Encoding.ASCII.GetBytes(text, line);
                line[text.Length..].Fill((byte)'.');
                line[LineBytes - 1] = (byte)'\n';
                int take = Math.Min(LineBytes - within, count - done);
                line.Slice(within, take).CopyTo(destination[done..]);
                done += take;
            }
            return count;
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public void A_log_that_grows_to_one_gibibyte_adds_matches_without_reading_the_old_part_again()
    {
        const long half = 512L << 20, full = 1L << 30;
        var log = new GeneratedLog { Length = half };
        long lines = full / GeneratedLog.LineBytes;
        log.ErrorLines.UnionWith([5, 4_000_000, 8_388_607, 8_388_608, 12_000_000, lines - 1]);
        var follower = Follow(log);

        follower.Poll();
        Assert.Equal([5L * 64, 4_000_000L * 64, 8_388_607L * 64], follower.Matches());
        Assert.Equal(half, follower.Consumed);

        log.Length = full;
        log.BytesRead = 0;
        var update = follower.Poll();
        Assert.Equal(LogFollowChange.None, update.Change);
        Assert.Equal(full - half, update.BytesRead);
        Assert.InRange(log.BytesRead, full - half, full - half + 3 * LogFollower.HeadBytes);
        Assert.Equal(new long[] { 5, 4_000_000, 8_388_607, 8_388_608, 12_000_000, lines - 1 }.Select(n => n * 64), follower.Matches());
        Assert.Equal(full, follower.Consumed);
    }
}

internal static class FollowerTestExtensions
{
    public static T Tap<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

/// <summary>P9.4.1: saved views of a log survive a restart.</summary>
public class LogViewStoreTests
{
    [Fact]
    public void A_saved_view_comes_back_with_filter_levels_highlight_and_position()
    {
        using var temp = new TempFile();
        string file = Path.Combine(Path.GetDirectoryName(temp.Path)!, "profile", "logviews.json");
        var view = new LogView("Errors of the service", new LogFilter([LogLevel.Error, LogLevel.Warning], @"svc-\d+", TextIsRegex: true), "My log", @"timeout|refused", @"C:\logs\app.log", 123_456_789_012);
        new LogViewStore(file).Save(view);

        var loaded = new LogViewStore(file).Load(out var problem);
        Assert.Null(problem);
        var back = Assert.Single(loaded);
        Assert.Equal(view with { Filter = back.Filter }, back);
        Assert.Equal(view.Filter.Levels, back.Filter.Levels);
        Assert.Equal(view.Filter.Text, back.Filter.Text);
        Assert.True(back.Filter.TextIsRegex);
        Assert.Equal([file], Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    [Fact]
    public void Saving_under_a_known_name_replaces_that_view_and_the_others_stay()
    {
        using var temp = new TempFile();
        var store = new LogViewStore(Path.Combine(Path.GetDirectoryName(temp.Path)!, "views.json"));
        store.Save(new LogView("b", LogFilter.None, null, null, null, 1));
        store.Save(new LogView("A", new LogFilter([LogLevel.Info]), null, null, null, 2));
        store.Save(new LogView("B", new LogFilter([LogLevel.Debug]), null, null, null, 3));
        var views = store.Load(out _);
        Assert.Equal(["A", "B"], views.Select(v => v.Name));
        Assert.Equal(3, views[1].Position);
    }

    [Fact]
    public void A_damaged_file_or_view_is_reported_and_does_not_hide_the_good_views()
    {
        using var temp = new TempFile("{ not json"u8.ToArray());
        var store = new LogViewStore(temp.Path);
        Assert.Empty(store.Load(out var problem));
        Assert.NotNull(problem);

        File.WriteAllText(temp.Path, """{"Version":1,"Views":[{"Name":"ok","Levels":["error"]},{"Name":"bad","Levels":["loud"]}]}""");
        var views = store.Load(out problem);
        Assert.Equal(["ok"], views.Select(v => v.Name));
        Assert.Contains("bad", problem);
        Assert.Empty(new LogViewStore(Path.Combine(Path.GetDirectoryName(temp.Path)!, "none.json")).Load(out problem));
        Assert.Null(problem);
    }
}

using System.Buffers;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tools;

/// <summary>Where a followed log is read from. <see cref="Probe"/> and <see cref="Read"/> are separate so a test can count the bytes that are read.</summary>
public interface ILogSource
{
    /// <summary>Size and identity of the file now; null when it does not exist.</summary>
    LogSourceState? Probe();

    /// <summary>Reads up to <paramref name="destination"/>.Length bytes at <paramref name="offset"/>; fewer only at the end of the file.</summary>
    int Read(long offset, Span<byte> destination);
}

/// <summary><paramref name="Identity"/> tells one file from another under the same name; 0 when the platform gives no reliable one.</summary>
public readonly record struct LogSourceState(long Length, long Identity);

/// <summary>
/// The file of a path, opened for each read with sharing that lets the writer of the log carry on and rotate it. The identity is the creation time on Windows;
/// elsewhere it is 0, because a creation time there can move with every write.
/// </summary>
public sealed class FileLogSource(string path) : ILogSource
{
    public LogSourceState? Probe()
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new LogSourceState(info.Length, OperatingSystem.IsWindows() ? info.CreationTimeUtc.Ticks : 0) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public int Read(long offset, Span<byte> destination)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int total = 0;
            while (total < destination.Length && RandomAccess.Read(handle, destination[total..], offset + total) is var n and > 0) total += n;
            return total;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return 0; }
    }
}

/// <summary>
/// Which lines of a log a view keeps: those of one of <paramref name="Levels"/> (all levels when empty) that also hold <paramref name="Text"/>, a plain
/// substring or a regular expression. A filter with neither is empty and keeps nothing: the view then only follows the file.
/// </summary>
public sealed record LogFilter(IReadOnlyList<LogLevel> Levels, string? Text = null, bool TextIsRegex = false)
{
    public static LogFilter None { get; } = new([]);

    public bool IsEmpty => Levels.Count == 0 && string.IsNullOrEmpty(Text);
}

public enum LogFollowChange
{
    /// <summary>Nothing happened, or only bytes were appended; see <see cref="LogFollowUpdate.BytesRead"/>.</summary>
    None,
    /// <summary>The file is shorter than what was read: another program truncated it. Reading restarts at its beginning.</summary>
    Truncated,
    /// <summary>The file under the name is another one, or the same name came back after it was gone. Reading restarts at the beginning of the new file.</summary>
    Rotated,
    /// <summary>The file does not exist; reported once, the follower waits for it.</summary>
    Missing,
}

/// <param name="Invalidated">Matches that belonged to the old file or content and were dropped (<see cref="LogFollower.Epoch"/> moved).</param>
/// <param name="BytesRead">Bytes of the log that were examined by this poll (those already read are not examined again, a line still without its terminator excepted).</param>
public sealed record LogFollowUpdate(LogFollowChange Change, long BytesRead, long NewMatches, long Invalidated, long Consumed);

/// <summary>
/// Follows a growing log: each <see cref="Poll"/> examines only the bytes added since the one before, whole lines at a time, and records the start of the
/// lines the <see cref="LogFilter"/> keeps. A line without its terminator yet is left for the next poll. The old bytes are not read again; the first
/// bytes of the file are compared with the ones seen earlier to notice that the file was replaced, and a shorter file means truncation. Both start the
/// reading over on the new content and drop the matches of the old (<see cref="Epoch"/>). Not thread safe for polling; the match queries may run beside it.
/// </summary>
public sealed class LogFollower
{
    /// <summary>Placeholder, OPEN: bytes from the start of the file kept to recognise a replaced file; not a decided value.</summary>
    public const int HeadBytes = 4096;

    private readonly ILogSource _source;
    private readonly TextCodec _codec;
    private readonly LogLevelRules _rules;
    private readonly LogFilter _filter;
    private readonly Regex? _regex;
    private readonly HashSet<LogLevel> _levels;
    private readonly List<long> _matches = [];
    private readonly Lock _gate = new();
    private byte[] _head = [];
    private long _consumed, _length, _identity;
    private bool _known, _missing, _started, _pending;

    /// <exception cref="LogPatternException">The filter text is not a valid regular expression.</exception>
    public LogFollower(ILogSource source, TextCodec codec, LogLevelRules rules, LogFilter filter)
    {
        _source = source;
        _codec = codec;
        _rules = rules;
        _filter = filter;
        _levels = [.. filter.Levels];
        if (filter.TextIsRegex && !string.IsNullOrEmpty(filter.Text))
        {
            try { _regex = new Regex(filter.Text, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException e) { throw new LogPatternException($"Filter: invalid regular expression: {e.Message}"); }
        }
    }

    public LogFilter Filter => _filter;

    /// <summary>Counts up each time the matches were dropped; offsets taken in an earlier epoch mean nothing in the new one.</summary>
    public int Epoch { get; private set; }

    /// <summary>Offset up to which the file has been examined: the start of the first line not examined yet.</summary>
    public long Consumed => Volatile.Read(ref _consumed);

    public long MatchCount { get { lock (_gate) return _matches.Count; } }

    public long[] Matches() { lock (_gate) return [.. _matches]; }

    /// <summary>Start of the first match that begins after <paramref name="offset"/>; null when there is none.</summary>
    public long? Next(long offset)
    {
        lock (_gate)
        {
            int i = _matches.BinarySearch(offset);
            i = i >= 0 ? i + 1 : ~i;
            return i < _matches.Count ? _matches[i] : null;
        }
    }

    /// <summary>Start of the last match that begins before <paramref name="offset"/>; null when there is none.</summary>
    public long? Previous(long offset)
    {
        lock (_gate)
        {
            int i = _matches.BinarySearch(offset);
            i = (i >= 0 ? i : ~i) - 1;
            return i >= 0 ? _matches[i] : null;
        }
    }

    /// <summary>
    /// Examines what was added to the file since the last poll. <paramref name="progress"/> receives the offset reached after each block (the first poll of a
    /// large file takes many blocks); cancelling leaves the follower consistent and the next poll goes on from there.
    /// </summary>
    public LogFollowUpdate Poll(CancellationToken ct = default, Action<long>? progress = null)
    {
        var probe = _source.Probe();
        if (probe is not { } state)
        {
            bool first = !_missing;
            _missing = true;
            return new(first ? LogFollowChange.Missing : LogFollowChange.None, 0, 0, 0, _consumed);
        }

        var change = LogFollowChange.None;
        if (_missing) change = LogFollowChange.Rotated;
        else if (_known)
        {
            if (state.Identity != _identity) change = LogFollowChange.Rotated;
            else if (state.Length < Math.Max(_length, _consumed)) change = LogFollowChange.Truncated;
            else if (state.Length == _length && !_pending) return new(LogFollowChange.None, 0, 0, 0, _consumed);
            else if (HeadChanged()) change = LogFollowChange.Rotated;
        }
        _missing = false;

        long invalidated = 0;
        if (change != LogFollowChange.None || !_started)
        {
            lock (_gate)
            {
                invalidated = _matches.Count;
                _matches.Clear();
                if (_started) Epoch++;
            }
            _head = [];
            Volatile.Write(ref _consumed, FirstLineStart());
        }
        _started = true;
        _identity = state.Identity;
        _known = true;

        _pending = true;
        long found = 0, read = 0;
        // a filter that keeps nothing only needs to know where the file ends, so a large log is not read to follow it
        if (_filter.IsEmpty) Volatile.Write(ref _consumed, state.Length);
        else read = Examine(state.Length, ct, progress, out found);
        _pending = false;
        _length = state.Length;
        RememberHead();
        return new(change, read, found, invalidated, _consumed);
    }

    private long FirstLineStart()
    {
        int bom = _codec.Bom.Length;
        if (bom == 0) return 0;
        Span<byte> head = stackalloc byte[bom];
        return _source.Read(0, head) == bom && head.SequenceEqual(_codec.Bom) ? bom : 0;
    }

    private bool HeadChanged()
    {
        if (_head.Length == 0) return false;
        var now = new byte[_head.Length];
        return _source.Read(0, now) != now.Length || !now.AsSpan().SequenceEqual(_head);
    }

    private void RememberHead()
    {
        int want = (int)Math.Min(_consumed, HeadBytes);
        if (want <= _head.Length) return;
        var head = new byte[want];
        if (_source.Read(0, head) == want) _head = head;
    }

    /// <summary>Examines whole lines from <see cref="Consumed"/> to <paramref name="end"/>; returns the bytes read.</summary>
    private long Examine(long end, CancellationToken ct, Action<long>? progress, out long found)
    {
        int w = _codec.Alignment;
        long read = 0;
        found = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(LogScan.BlockBytes);
        try
        {
            while (_consumed < end)
            {
                ct.ThrowIfCancellationRequested();
                int got = _source.Read(_consumed, buffer.AsSpan(0, (int)Math.Min(LogScan.BlockBytes, end - _consumed)));
                got -= got % w;
                if (got <= 0) break;
                read += got;
                var span = buffer.AsSpan(0, got);
                bool more = _consumed + got < end;
                int start = 0;
                for (int i; (i = LineScanner.FindTerminator(span, start, _codec)) >= 0; start = i + w)
                    if (i > start && Consider(span[start..i], _consumed + start)) found++;
                int cut = start;
                if (cut == 0 && more)
                {
                    // a line longer than a block is cut where the block ends, like the search for an error does
                    cut = _codec.CompleteLength(span);
                    if (cut > 0 && Consider(span[..cut], _consumed)) found++;
                }
                if (cut <= 0) break;
                Volatile.Write(ref _consumed, _consumed + cut);
                progress?.Invoke(_consumed);
                if (!more) break;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return read;
    }

    private bool Consider(ReadOnlySpan<byte> line, long offset)
    {
        if (_filter.IsEmpty) return false;
        string text = _codec.GetString(line).TrimEnd('\r');
        if (text.Length == 0) return false;
        try
        {
            if (_levels.Count > 0 && !(_rules.Classify(text) is { } level && _levels.Contains(level))) return false;
            if (_regex is not null ? !_regex.IsMatch(text) : !string.IsNullOrEmpty(_filter.Text) && !text.Contains(_filter.Text, StringComparison.Ordinal)) return false;
        }
        catch (RegexMatchTimeoutException) { return false; }
        lock (_gate) _matches.Add(offset);
        return true;
    }
}

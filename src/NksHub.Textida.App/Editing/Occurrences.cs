using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Completion;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Editing;

/// <summary>The settings of the highlight of the other occurrences of the selected text (P3.6.12).</summary>
internal sealed record OccurrenceOptions(bool Enabled = true, bool WholeWord = false, bool MatchCase = true, bool AtCaret = false, int MinLength = PluginLimitsPlaceholders.OccurrenceMinLength)
{
    public static OccurrenceOptions Default { get; } = new();

    public static OccurrenceOptions From(SettingsStack s) => new(
        s.GetBool("editor.occurrences.enabled"), s.GetBool("editor.occurrences.wholeWord"), s.GetBool("editor.occurrences.matchCase"),
        s.GetBool("editor.occurrences.atCaret"), (int)s.GetInt("editor.occurrences.minLength"));
}

/// <summary>What the highlight looks for: the text and the two switches the find bar has for plain text.</summary>
internal readonly record struct OccurrenceQuery(string Needle, bool MatchCase, bool WholeWord)
{
    /// <summary>The pattern as the find bar and the search worker build it, so both find the same places.</summary>
    public SearchPattern Pattern => new(Needle, IsRegex: false, CaseSensitive: MatchCase, WholeWord: WholeWord);
}

/// <param name="Own">Where the selected text (or the word at the caret) itself starts, in the offsets of the surface; that place is not an "other" occurrence.</param>
internal readonly record struct OccurrenceTarget(OccurrenceQuery Query, long Own);

/// <param name="Lines">Zero-based lines that hold another occurrence, ascending, each once.</param>
/// <param name="Count">The other occurrences found.</param>
/// <param name="Limited">The scan stopped at <see cref="PluginLimitsPlaceholders.OccurrenceMaxMarks"/>, so there are more.</param>
/// <param name="InView">Only the lines on screen were looked at (a large file in the paged view).</param>
internal sealed record OccurrenceScan(IReadOnlyList<long> Lines, long Count, bool Limited = false, bool InView = false)
{
    public static OccurrenceScan None { get; } = new([], 0);

    /// <summary>The sentence for the status bar and the screen reader, or null when there is no other occurrence.</summary>
    public string? Describe() => Count == 0 ? null
        : Limited ? Loc.Get(K.StatusOccurrencesLimit, ("count", Count))
        : Loc.Plural(InView ? K.StatusOccurrencesView : K.StatusOccurrences, Count);
}

/// <summary>Finding the other occurrences in a line, on top of the same regular expression the find bar builds (<see cref="RegexTools.Create"/>).</summary>
internal static class OccurrenceMatcher
{
    /// <summary>
    /// What to look for when <paramref name="text"/> is selected, or null when it is not worth highlighting: too short, too long, over a line break or only white space.
    /// <paramref name="wholeWord"/> forces whole words, as for the word at the caret.
    /// </summary>
    public static OccurrenceTarget? TargetOf(string text, long own, bool wholeWord, OccurrenceOptions options)
    {
        if (text.Length < Math.Max(1, options.MinLength) || text.Length > PluginLimitsPlaceholders.OccurrenceMaxChars) return null;
        var span = text.AsSpan();
        if (span.IndexOfAny('\r', '\n') >= 0 || span.IsWhiteSpace()) return null;
        return new OccurrenceTarget(new OccurrenceQuery(text, options.MatchCase, options.WholeWord || wholeWord), own);
    }

    /// <summary>The word (letters, digits, underscore) that the column is in or touches.</summary>
    public static bool WordAt(ReadOnlySpan<char> line, int column, out int start, out int end)
    {
        column = Math.Clamp(column, 0, line.Length);
        start = end = column;
        while (start > 0 && WordCompletion.IsWordChar(line[start - 1])) start--;
        while (end < line.Length && WordCompletion.IsWordChar(line[end])) end++;
        return end > start;
    }

    public static Regex? RegexOf(OccurrenceQuery query)
    {
        try { return RegexTools.Create(query.Pattern); }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Adds the matches in <paramref name="line"/> (start and length) to <paramref name="into"/> until it holds <paramref name="max"/>.</summary>
    public static void Spans(Regex regex, ReadOnlySpan<char> line, List<(int Start, int Length)> into, int max)
    {
        try
        {
            foreach (var match in regex.EnumerateMatches(line))
            {
                if (match.Length == 0) continue;
                into.Add((match.Index, match.Length));
                if (into.Count >= max) return;
            }
        }
        catch (RegexMatchTimeoutException) { } // the pattern is a literal, so this is only a pattern the engine could not make linear
    }
}

/// <summary>The marks of the lines beside the scrollbar: one short bar per row of pixels that holds a line, drawn at the right edge of the text.</summary>
internal static class OccurrenceStrip
{
    public const double Width = 5;
    private const double Thickness = 3;

    /// <summary>The distinct pixel rows, of a strip <paramref name="height"/> pixels high, that the ascending <paramref name="lines"/> fall in; the same rows the minimap marks.</summary>
    public static IReadOnlyList<int> Rows(IReadOnlyList<long> lines, long lineCount, int height)
    {
        var rows = new List<int>();
        if (lineCount <= 0 || height <= 0) return rows;
        int last = -1;
        foreach (long line in lines)
        {
            if (line < 0 || line >= lineCount) continue;
            int row = (int)Math.Min(height - 1, line * height / lineCount);
            if (row == last) continue;
            rows.Add(row);
            last = row;
        }
        return rows;
    }

    public static void Draw(DrawingContext context, IReadOnlyList<long> lines, long lineCount, Rect area, IBrush brush)
    {
        foreach (int row in Rows(lines, lineCount, (int)area.Height))
            context.FillRectangle(brush, new Rect(area.Right - Width, area.Top + Math.Max(0, row - 1), Width, Thickness));
    }
}

/// <summary>
/// The state of the highlight of the other occurrences of the selection on one surface. A change of the selection, the caret or the text drops what is shown at once
/// and looks again once nothing has changed for <see cref="Debounce"/>; a scroll keeps the text looked for and only counts again (<see cref="Refresh"/>).
/// The surface draws the occurrences of the lines on screen itself from <see cref="Target"/> and <see cref="Matcher"/>; the count and the marked lines of the
/// whole document come from the scan the surface supplies, which runs off the UI thread. While nothing is selected a change costs a check of the settings and one of the selection.
/// </summary>
internal sealed class OccurrenceTracker : IDisposable
{
    private readonly Func<OccurrenceOptions> _options;
    private readonly Func<OccurrenceOptions, OccurrenceTarget?> _read;
    private readonly Func<OccurrenceTarget, CancellationToken, Task<OccurrenceScan>> _scan;
    private readonly Func<bool> _hasSelection;
    private readonly Action _redraw;
    private readonly DispatcherTimer _timer = new();
    private OccurrenceTarget? _target;
    private Regex? _matcher;
    private CancellationTokenSource? _cts;
    private long _touched, _announced;
    private int _generation;
    private bool _fresh, _disposed;
    private Task _scanning = Task.CompletedTask;

    /// <param name="options">The settings now.</param>
    /// <param name="read">What the surface has selected (or the word at the caret), or null when there is nothing to look for.</param>
    /// <param name="scan">Counts the other occurrences and lists their lines; called on the UI thread, may finish on another.</param>
    /// <param name="hasSelection">Whether something is selected; a cheap check that keeps typing without a selection free.</param>
    /// <param name="redraw">Draws the occurrences on screen again.</param>
    public OccurrenceTracker(Func<OccurrenceOptions> options, Func<OccurrenceOptions, OccurrenceTarget?> read, Func<OccurrenceTarget, CancellationToken, Task<OccurrenceScan>> scan, Func<bool> hasSelection, Action redraw)
    {
        (_options, _read, _scan, _hasSelection, _redraw) = (options, read, scan, hasSelection, redraw);
        _timer.Tick += OnTick;
    }

    /// <summary>How long nothing may change before the occurrences are looked for; a test shortens it.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.OccurrenceDebounceMs);

    /// <summary>The least time between two announcements to a screen reader; a test shortens it.</summary>
    public TimeSpan AnnounceInterval { get; set; } = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.OccurrenceAnnounceMs);

    /// <summary>What is highlighted now; null while nothing is, or until the change of the selection has settled.</summary>
    public OccurrenceTarget? Target => _target;

    /// <summary>The expression that finds <see cref="Target"/> in a line; null with it.</summary>
    public Regex? Matcher => _matcher;

    /// <summary>The count and the lines of the last scan that finished for <see cref="Target"/>.</summary>
    public OccurrenceScan Result { get; private set; } = OccurrenceScan.None;

    /// <summary>The last scan, which is finished when nothing is looked for; a test awaits it.</summary>
    public Task Scanning => _scanning;

    /// <summary>A change waits for the delay to end before the occurrences are looked for.</summary>
    public bool Pending => _timer.IsEnabled;

    /// <summary>Raised when <see cref="Result"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Raised with the result of a scan that followed a change of the selection, at most once per <see cref="AnnounceInterval"/>.</summary>
    public event Action<OccurrenceScan>? Announce;

    /// <summary>The selection, the caret or the text changed (or the settings did).</summary>
    public void Touch()
    {
        if (_disposed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Touch);
            return;
        }
        var options = _options();
        if (!options.Enabled)
        {
            Drop();
            return;
        }
        if (_target is null && !options.AtCaret && !_hasSelection()) return; // typing: nothing is shown and nothing is selected
        Drop();
        _fresh = true;
        Arm();
    }

    /// <summary>The part of the document that is looked at moved (a scroll of the paged view): the text looked for stays and the count is made again.</summary>
    public void Refresh()
    {
        if (_disposed || _target is null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }
        Arm();
    }

    /// <summary>Looks now instead of when the delay is over; the task ends with the scan.</summary>
    public Task SettleAsync()
    {
        _timer.Stop();
        bool fresh = _fresh;
        _fresh = false;
        return Settle(again: !fresh);
    }

    /// <summary>Drops what is shown and stops looking, for a surface that is taken off the screen.</summary>
    public void Suspend()
    {
        _timer.Stop();
        _fresh = false;
        Drop();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _cts?.Cancel();
        _cts = null;
        _target = null;
        _matcher = null;
    }

    private void Arm()
    {
        _touched = Stopwatch.GetTimestamp();
        if (_timer.IsEnabled) return;
        _timer.Interval = Debounce;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(_touched) < Debounce) return; // changed again meanwhile: wait for the next tick
        _ = SettleAsync();
    }

    private void Drop()
    {
        _generation++;
        _cts?.Cancel();
        _cts = null;
        if (_target is not null)
        {
            _target = null;
            _matcher = null;
            _redraw();
        }
        if (Result.Count > 0 || Result.Lines.Count > 0)
        {
            Result = OccurrenceScan.None;
            Changed?.Invoke();
        }
    }

    private Task Settle(bool again)
    {
        if (_disposed) return Task.CompletedTask;
        var options = _options();
        var target = again && _target is not null ? _target : options.Enabled ? _read(options) : null;
        var matcher = target is { } t ? again && _matcher is not null ? _matcher : OccurrenceMatcher.RegexOf(t.Query) : null;
        if (target is not { } found || matcher is null)
        {
            Drop();
            return _scanning = Task.CompletedTask;
        }
        if (!again || _target is null)
        {
            _target = found;
            _matcher = matcher;
            _redraw();
        }
        return _scanning = Scan(found, announce: !again);
    }

    private async Task Scan(OccurrenceTarget target, bool announce)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        int generation = ++_generation;
        OccurrenceScan result;
        try { result = await _scan(target, cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { result = OccurrenceScan.None; } // the file changed under the snapshot
        if (generation != _generation || _disposed) return;
        Result = result;
        Changed?.Invoke();
        if (!announce || result.Count == 0 || Stopwatch.GetElapsedTime(_announced) < AnnounceInterval) return;
        _announced = Stopwatch.GetTimestamp();
        Announce?.Invoke(result);
    }
}

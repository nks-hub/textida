using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>What stays visible when a log is folded: lines of at least the level <paramref name="AtLeast"/> (error is the most severe) and lines that match <paramref name="Filter"/>. A null part keeps everything.</summary>
public sealed record LogFoldCriteria(LogLevel? AtLeast, Regex? Filter)
{
    public bool IsEmpty => AtLeast is null && Filter is null;
}

/// <summary>A run of lines to fold: zero-based, both ends included.</summary>
public readonly record struct LogFoldRange(int First, int Last)
{
    public int Count => Last - First + 1;
}

/// <summary>
/// Finds the runs of lines that hide behind one placeholder row. A line with a level word is judged by its level and by the filter. A line without one
/// (a continuation such as a stack trace frame) belongs to the entry above it and shares its fate when a level is asked for; with only a filter it is
/// judged by the filter on its own text.
/// </summary>
public static class LogFold
{
    /// <summary>Folds are only made for runs of at least this many lines; a single line behind a placeholder would save nothing.</summary>
    public const int MinRun = 2;

    public static IReadOnlyList<LogFoldRange> Ranges(int lineCount, Func<int, string> lineAt, LogLevelRules rules, LogFoldCriteria criteria, int minRun = MinRun)
    {
        var ranges = new List<LogFoldRange>();
        if (criteria.IsEmpty) return ranges;
        bool headKept = false;
        int runStart = -1;
        for (int i = 0; i < lineCount; i++)
        {
            string text = lineAt(i);
            bool filterOk = criteria.Filter is null || Matches(criteria.Filter, text);
            bool keep;
            if (rules.Classify(text) is { } level)
            {
                keep = (criteria.AtLeast is not { } least || level <= least) && filterOk;
                headKept = keep;
            }
            else keep = criteria.AtLeast is not null ? headKept : filterOk;
            if (keep)
            {
                Close(ranges, runStart, i - 1, minRun);
                runStart = -1;
            }
            else if (runStart < 0) runStart = i;
        }
        Close(ranges, runStart, lineCount - 1, minRun);
        return ranges;
    }

    /// <summary>Whether a single line is kept; unlike <see cref="Ranges"/> it cannot see the entry above, so a line without a level counts as kept when a level is asked for.</summary>
    public static bool KeepsAlone(string text, LogLevelRules rules, LogFoldCriteria criteria)
    {
        if (criteria.IsEmpty) return true;
        bool filterOk = criteria.Filter is null || Matches(criteria.Filter, text);
        if (rules.Classify(text) is not { } level) return criteria.AtLeast is not null || filterOk;
        return (criteria.AtLeast is not { } least || level <= least) && filterOk;
    }

    private static bool Matches(Regex filter, string text)
    {
        try { return filter.IsMatch(text); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static void Close(List<LogFoldRange> ranges, int from, int to, int minRun)
    {
        if (from >= 0 && to - from + 1 >= minRun) ranges.Add(new LogFoldRange(from, to));
    }
}

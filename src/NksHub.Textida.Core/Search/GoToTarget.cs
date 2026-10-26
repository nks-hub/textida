using System.Globalization;

namespace NksHub.Textida.Core.Search;

public enum GoToKind { Line, Percent, Byte }

/// <summary>Where "Go to" should land: a line with an optional column (both 1-based), a percentage of the file, or a byte offset.</summary>
public readonly record struct GoToTarget(GoToKind Kind, long Value, long Column = 1)
{
    /// <summary>
    /// Reads <c>12</c>, <c>12:5</c> or <c>12,5</c> (line, column), <c>40%</c> (percent of the file, 0 to 100) and <c>@4096</c> (byte offset). False for anything else.
    /// </summary>
    public static bool TryParse(string? text, out GoToTarget target)
    {
        target = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        const NumberStyles digits = NumberStyles.None;
        var inv = CultureInfo.InvariantCulture;
        if (text[^1] == '%')
        {
            if (!long.TryParse(text.AsSpan(0, text.Length - 1).Trim(), digits, inv, out long percent) || percent > 100) return false;
            target = new GoToTarget(GoToKind.Percent, percent);
            return true;
        }
        if (text[0] == '@')
        {
            if (!long.TryParse(text.AsSpan(1).Trim(), digits, inv, out long offset)) return false;
            target = new GoToTarget(GoToKind.Byte, offset);
            return true;
        }
        var parts = text.Split([':', ','], 2, StringSplitOptions.TrimEntries);
        if (!long.TryParse(parts[0], digits, inv, out long line) || line < 1) return false;
        long column = 1;
        if (parts.Length == 2 && (!long.TryParse(parts[1], digits, inv, out column) || column < 1)) return false;
        target = new GoToTarget(GoToKind.Line, line, column);
        return true;
    }
}

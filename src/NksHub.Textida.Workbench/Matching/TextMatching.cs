using System.Globalization;
using System.Text;

namespace NksHub.Textida.Workbench.Matching;

/// <summary>Diacritics-insensitive, case-insensitive text folding and fuzzy scoring.</summary>
public static class TextMatching
{
    /// <summary>Lower-cases and removes combining marks, so "Kódování" becomes "kodovani"; the German sharp s becomes "ss", so "Schließen" is found by typing "schliessen".</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        string decomposed;
        try
        {
            decomposed = text.Normalize(NormalizationForm.FormD);
        }
        catch (ArgumentException)
        {
            decomposed = text;
        }

        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is 'ß' or 'ẞ')
            {
                sb.Append("ss");
                continue;
            }

            sb.Append(ch switch
            {
                'ł' or 'Ł' => 'l',
                'đ' or 'Đ' => 'd',
                'ø' or 'Ø' => 'o',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// Subsequence match of <paramref name="query"/> in <paramref name="text"/> (whitespace in the query is ignored).
    /// Returns null when it does not match; a higher score is a better match. Consecutive characters and
    /// word starts score higher.
    /// </summary>
    public static int? FuzzyScore(string query, string text)
    {
        var q = Fold(query).Replace(" ", "", StringComparison.Ordinal);
        var t = Fold(text);
        if (q.Length == 0)
        {
            return 0;
        }

        if (q.Length > t.Length)
        {
            return null;
        }

        const int None = int.MinValue;
        var prev = new int[t.Length];
        var cur = new int[t.Length];
        for (var qi = 0; qi < q.Length; qi++)
        {
            Array.Fill(cur, None);
            var gapBest = None;
            for (var ti = 0; ti < t.Length; ti++)
            {
                if (qi > 0 && ti >= 2 && prev[ti - 2] != None)
                {
                    gapBest = Math.Max(gapBest, prev[ti - 2] - 1);
                }

                if (t[ti] != q[qi])
                {
                    continue;
                }

                var gain = 1 + (ti == 0 || !char.IsLetterOrDigit(t[ti - 1]) ? 8 : 0);
                if (qi == 0)
                {
                    cur[ti] = gain - Math.Min(ti, 4);
                    continue;
                }

                var best = ti > 0 && prev[ti - 1] != None ? prev[ti - 1] + 5 : None;
                best = Math.Max(best, gapBest);
                if (best != None)
                {
                    cur[ti] = best + gain;
                }
            }

            (prev, cur) = (cur, prev);
        }

        var result = prev.Max();
        if (result == None)
        {
            return null;
        }

        return result + (t.StartsWith(q, StringComparison.Ordinal) ? 10 : 0);
    }
}

namespace NksHub.Textida.Core.Data;

/// <summary>Glob masks over paths with '/' or '\' separators: <c>*</c> and <c>?</c> inside one segment, <c>**</c> over any number of segments.</summary>
public static class Glob
{
    public static bool IsMatch(string pattern, string path)
    {
        var p = path.Replace('\\', '/');
        var g = pattern.Replace('\\', '/');
        if (!g.Contains('/')) return Segment(g, p[(p.LastIndexOf('/') + 1)..]);
        if (!g.StartsWith("**/", StringComparison.Ordinal) && !g.StartsWith('/')) g = "**/" + g; // a relative mask may start anywhere in the path
        return Segments(g.Split('/', StringSplitOptions.RemoveEmptyEntries), 0, p.Split('/', StringSplitOptions.RemoveEmptyEntries), 0);
    }

    private static bool Segments(string[] g, int gi, string[] p, int pi)
    {
        if (gi == g.Length) return pi == p.Length;
        if (g[gi] == "**")
        {
            for (int k = pi; k <= p.Length; k++) if (Segments(g, gi + 1, p, k)) return true;
            return false;
        }
        return pi < p.Length && Segment(g[gi], p[pi]) && Segments(g, gi + 1, p, pi + 1);
    }

    private static bool Segment(string g, string s)
    {
        int gi = 0, si = 0, star = -1, mark = 0;
        while (si < s.Length)
        {
            if (gi < g.Length && (g[gi] == '?' || g[gi] == s[si])) { gi++; si++; }
            else if (gi < g.Length && g[gi] == '*') { star = gi++; mark = si; }
            else if (star >= 0) { gi = star + 1; si = ++mark; }
            else return false;
        }
        while (gi < g.Length && g[gi] == '*') gi++;
        return gi == g.Length;
    }
}

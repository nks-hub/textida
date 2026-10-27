using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.Json.Packages;

/// <summary>
/// Orders version texts of the seven ecosystems with one rule: dotted numbers first, then the text after them as a pre-release (a release is newer than any pre-release of it),
/// build metadata after <c>+</c> ignored. Epochs of Python and the exact NuGet rules for four-part versions are not modelled.
/// </summary>
public static partial class Versions
{
    [GeneratedRegex(@"^[vV]?(?<nums>\d+(?:\.\d+)*)(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();

    [GeneratedRegex(@"^[vV]?\d+(\.\d+)*(?![.]?[xX]$)([-.]?[0-9A-Za-z][0-9A-Za-z.+-]*)?(\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Plain();

    /// <summary>Whether the text is one version (not a range, a wildcard, a variable or a path).</summary>
    public static bool IsPlain(string text) => Plain().IsMatch(text);

    private static (int[] Numbers, string Pre) Parse(string version)
    {
        var m = Shape().Match(version.Trim());
        if (!m.Success) return ([], version);
        var numbers = m.Groups["nums"].Value.Split('.').Select(n => int.TryParse(n, out var v) ? v : int.MaxValue).ToArray();
        var rest = m.Groups["rest"].Value;
        int build = rest.IndexOf('+', StringComparison.Ordinal);
        if (build >= 0) rest = rest[..build];
        return (numbers, rest.TrimStart('-', '.', '_'));
    }

    public static bool IsPrerelease(string version) => Parse(version).Pre.Length > 0;

    public static int Compare(string a, string b)
    {
        var (na, pa) = Parse(a);
        var (nb, pb) = Parse(b);
        for (int i = 0; i < Math.Max(na.Length, nb.Length); i++)
        {
            int c = (i < na.Length ? na[i] : 0).CompareTo(i < nb.Length ? nb[i] : 0);
            if (c != 0) return c;
        }
        if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
        var ia = pa.Split('.');
        var ib = pb.Split('.');
        for (int i = 0; i < Math.Min(ia.Length, ib.Length); i++)
        {
            int c = int.TryParse(ia[i], out var x) && int.TryParse(ib[i], out var y) ? x.CompareTo(y) : string.Compare(ia[i], ib[i], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return ia.Length.CompareTo(ib.Length);
    }

    /// <summary>The newest release of the list; when there is none (every version is a pre-release) the newest of all. Null for an empty list.</summary>
    public static string? Latest(IEnumerable<string> versions)
    {
        var all = versions.ToList();
        var pool = all.Where(v => !IsPrerelease(v)).ToList();
        if (pool.Count == 0) pool = all;
        string? best = null;
        foreach (var v in pool) if (best is null || Compare(v, best) > 0) best = v;
        return best;
    }
}

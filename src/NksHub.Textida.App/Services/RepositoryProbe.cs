using System.Collections.Concurrent;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Tells whether a file lies in a Git working tree: a folder with <c>.git</c> (a folder, or a file in a linked work tree or a submodule) at or above it. It only looks for the entry; no <c>git</c> program runs.
/// The answer for a folder is kept for a few seconds, because a menu or the palette asks for it once per command.
/// </summary>
internal static class RepositoryProbe
{
    private const long KeepMilliseconds = 3000;
    private static readonly ConcurrentDictionary<string, (long Until, bool Inside)> Known = new(StringComparer.Ordinal);

    public static bool IsInside(string path)
    {
        string? folder;
        try { folder = Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
        if (folder is null) return false;
        long now = Environment.TickCount64;
        if (Known.TryGetValue(folder, out var seen) && seen.Until > now) return seen.Inside;
        bool inside = false;
        for (var at = folder; at is not null && !inside; at = Path.GetDirectoryName(at))
        {
            try { inside = Directory.Exists(Path.Combine(at, ".git")) || File.Exists(Path.Combine(at, ".git")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { break; }
        }
        Known[folder] = (now + KeepMilliseconds, inside);
        return inside;
    }

    /// <summary>Forgets what was learned; a test that makes a repository after it asked once needs it.</summary>
    internal static void Forget() => Known.Clear();
}

using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Files;

/// <summary>
/// A folder of a fixed name that only the current user can enter, for sockets, locks and scratch files. On Linux another user may have made <c>/tmp/name</c> first, as a folder of their own
/// or as a link: the folder is used only when it is a real folder of this user with mode 0700, otherwise this process gets a new private folder of its own. Windows, and macOS (whose
/// temporary folder belongs to the user), use the temporary folder as before.
/// </summary>
public static partial class PrivateDirectory
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly ConcurrentDictionary<string, string> Fallbacks = new();

    /// <summary>The folder <paramref name="name"/>, created when missing; with <paramref name="runtime"/> it is in $XDG_RUNTIME_DIR when there is one (memory-backed and small: sockets and locks only).</summary>
    public static string Get(string name, bool runtime)
    {
        if (OperatingSystem.IsWindows())
        {
            var dir = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(dir);
            return dir;
        }
        if (!OperatingSystem.IsLinux())
        {
            var dir = Path.Combine(Path.GetTempPath(), name);
            Directory.CreateDirectory(dir, OwnerOnly);
            return dir;
        }
        string root = runtime && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } xdg && Path.IsPathRooted(xdg) && IsPrivate(xdg) ? xdg : Path.GetTempPath();
        string path = Path.Combine(root, name);
        try { Directory.CreateDirectory(path, OwnerOnly); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a file or a foreign link in the way: decided below */ }
        return IsPrivate(path) ? path : Fallbacks.GetOrAdd(name, n => Directory.CreateTempSubdirectory(n + "-").FullName);
    }

    /// <summary>
    /// Whether <paramref name="path"/> itself (a link is not followed) is a folder owned by this user with mode 0700. Once true it stays true: in the sticky /tmp nobody else can rename or
    /// replace a folder this user owns.
    /// </summary>
    internal static unsafe bool IsPrivate(string path)
    {
        // struct statx has the same layout on every Linux architecture, unlike struct stat; 256 bytes
        byte* buffer = stackalloc byte[256];
        try
        {
            if (statx(AtFdCwd, path, AtSymlinkNoFollow, StatxBasicStats, buffer) != 0) return false;
            uint uid = *(uint*)(buffer + 20); // stx_uid
            uint mode = *(ushort*)(buffer + 28); // stx_mode
            return (mode & 0xF000) == 0x4000 && (mode & 0xFFF) == 0x1C0 && uid == geteuid(); // S_IFDIR, 0700
        }
        catch (EntryPointNotFoundException) { return false; } // a C library without statx: treated as not private
    }

    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x7FF;

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static unsafe partial int statx(int dirfd, string path, int flags, uint mask, byte* buffer);

    [LibraryImport("libc")]
    private static partial uint geteuid();
}

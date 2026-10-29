using System.IO.Compression;

namespace NksHub.Textida.PluginHost.Manager;

/// <summary>Reasons the plugin manager refuses a package or an operation; each has its own error so the user is told which.</summary>
public enum PluginInstallError
{
    NotAZip, UnsafePath, SymbolicLink, TooLarge, TooManyEntries, DuplicateEntry, NoManifest, InvalidManifest,
    IdCollision, NotNewer, ConsentRequired, NothingToRollBack, UnknownPlugin, Failed,
}

public sealed class PluginInstallException(PluginInstallError error, string message, IReadOnlyList<string>? details = null) : Exception(message)
{
    public PluginInstallError Error { get; } = error;

    /// <summary>The single problems behind <see cref="Error"/> (manifest errors, permissions that need consent).</summary>
    public IReadOnlyList<string> Details { get; } = details ?? [];
}

/// <summary>Quotas for unpacking a plugin ZIP. No defaults: both numbers are an owner decision.</summary>
public sealed record PluginInstallLimits(long MaxUnpackedBytes, int MaxEntries, long MaxIconBytes = 0);

/// <summary>Unpacks a plugin ZIP into an empty folder, refusing anything that could write outside it.</summary>
internal static class PluginZip
{
    public static void Extract(string zipPath, string target, PluginInstallLimits limits)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallException(PluginInstallError.NotAZip, $"'{Path.GetFileName(zipPath)}' cannot be read as a ZIP file: {e.Message}");
        }
        using (zip)
        {
            if (zip.Entries.Count > limits.MaxEntries)
                throw new PluginInstallException(PluginInstallError.TooManyEntries, $"The ZIP has {zip.Entries.Count} entries; the limit is {limits.MaxEntries}.");
            long declared = 0;
            foreach (var entry in zip.Entries) declared += entry.Length;
            if (declared > limits.MaxUnpackedBytes)
                throw new PluginInstallException(PluginInstallError.TooLarge, $"The ZIP unpacks to {declared} bytes; the limit is {limits.MaxUnpackedBytes}.");

            var root = Path.GetFullPath(target);
            var rootWithSeparator = root + Path.DirectorySeparatorChar;
            var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            long written = 0;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (IsSymbolicLink(entry))
                    throw new PluginInstallException(PluginInstallError.SymbolicLink, $"'{name}' is a symbolic link; plugins may not contain links.");
                var destination = name.Length == 0 || name.Contains('\0') ? "" : Path.GetFullPath(Path.Combine(root, name));
                if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    throw new PluginInstallException(PluginInstallError.UnsafePath, $"'{name}' would be written outside the plugin folder.");

                if (name.EndsWith('/') || name.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                if (!seen.Add(destination))
                    throw new PluginInstallException(PluginInstallError.DuplicateEntry, $"'{name}' appears twice in the ZIP.");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    throw new PluginInstallException(PluginInstallError.Failed, $"'{name}' cannot be unpacked: {e.Message}");
                }
                written += new FileInfo(destination).Length;
                if (written > limits.MaxUnpackedBytes)
                    throw new PluginInstallException(PluginInstallError.TooLarge, $"The ZIP unpacks to more than {limits.MaxUnpackedBytes} bytes.");
            }
        }
    }

    /// <summary>A Unix link (mode bits in the high half of the attributes) or a Windows reparse point.</summary>
    private static bool IsSymbolicLink(ZipArchiveEntry entry) => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0;

    /// <summary>
    /// Whether the file carries the Mark-of-the-Web of Windows (zone Internet or Restricted, the <c>Zone.Identifier</c> stream written by browsers). False elsewhere:
    /// the macOS quarantine flag is not read yet (shortcut: needs <c>getxattr</c>, add it with the macOS distribution check P3b.5.2).
    /// </summary>
    public static bool IsFromInternet(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var reader = new StreamReader(path + ":Zone.Identifier");
            string? line;
            while ((line = reader.ReadLine()) is not null)
                if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.AsSpan(7), out var zone)) return zone >= 3;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException) { /* no stream: the file was not downloaded */ }
        return false;
    }
}

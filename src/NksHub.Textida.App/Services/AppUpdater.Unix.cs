using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace NksHub.Textida.App.Services;

internal enum UpdatePlatform { Windows, MacOS, Linux }

internal sealed partial class AppUpdater
{
    internal static UpdatePlatform CurrentPlatform => OperatingSystem.IsWindows() ? UpdatePlatform.Windows
        : OperatingSystem.IsMacOS() ? UpdatePlatform.MacOS : UpdatePlatform.Linux;

    internal Action<string>? MacSignatureCheck { get; init; }

    internal static string Executable(string dir, UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.Windows => Path.Combine(dir, "Textida.exe"),
        UpdatePlatform.MacOS => Path.Combine(dir, "Contents", "MacOS", "Textida"),
        _ => Path.Combine(dir, "Textida")
    };

    internal static string VersionFile(string dir) => Directory.Exists(Path.Combine(dir, "Contents"))
        ? Path.Combine(dir, "Contents", "Resources", "version.txt") : Path.Combine(dir, "version.txt");

    internal static string InstallationFolder(string programFolder)
    {
        var folder = new DirectoryInfo(programFolder);
        return folder.Name == "MacOS" && folder.Parent?.Name == "Contents" ? folder.Parent.Parent!.FullName : folder.FullName;
    }

    internal static void RecoverAtStartup(string installDir)
    {
        if (!OperatingSystem.IsWindows()) { new UpdateSwap(installDir).Recover(); return; }
        var aside = installDir + ".aside";
        if (!File.Exists(Path.Combine(aside, "Textida.exe"))) return;
        if (!Directory.Exists(installDir)) Directory.Move(aside, installDir);
        else if (File.Exists(Path.Combine(installDir, "Textida.exe")))
        {
            var previous = installDir + ".previous";
            if (Directory.Exists(previous)) Directory.Delete(previous, recursive: true);
            Directory.Move(aside, previous);
        }
    }

    private string StageUnix(string package, UpdateRelease release)
    {
        var unpack = StagingDir + ".unpack";
        ClearStaging();
        if (Directory.Exists(unpack)) Directory.Delete(unpack, recursive: true);
        Directory.CreateDirectory(unpack);
        try
        {
            if (Platform == UpdatePlatform.MacOS)
            {
                using var archive = ZipFile.OpenRead(package);
                foreach (var entry in archive.Entries)
                {
                    RequireArchivePath(unpack, entry.FullName);
                    if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                        throw new InvalidDataException("An update archive cannot contain symbolic links.");
                }
                archive.Dispose();
                if (OperatingSystem.IsMacOS()) RunChecked("/usr/bin/ditto", "-x", "-k", package, unpack);
                else ZipFile.ExtractToDirectory(package, unpack);
            }
            else
            {
                using var gzip = new GZipStream(File.OpenRead(package), CompressionMode.Decompress);
                using var reader = new TarReader(gzip);
                while (reader.GetNextEntry() is { } entry)
                {
                    var path = RequireArchivePath(unpack, entry.Name);
                    if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(path);
                    else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        entry.ExtractToFile(path, overwrite: false);
                    }
                    else throw new InvalidDataException("An update archive can contain only files and directories.");
                }
            }
            var source = Path.Combine(unpack, Platform == UpdatePlatform.MacOS ? "Textida.app" : $"Textida-{release.Version}-{release.Rid}");
            if (!File.Exists(Executable(source, Platform)) || !File.Exists(VersionFile(source)) || InstalledVersion(source) != release.Version)
                throw new UpdateException(UpdateError.BadPackage, "The package is incomplete or has a different version.");
            if (Platform == UpdatePlatform.MacOS) CheckMacSignature(source);
            Directory.Move(source, StagingDir);
            return StagingDir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or Win32Exception)
        {
            ClearStaging();
            throw new UpdateException(UpdateError.BadPackage, "The package cannot be unpacked or verified: " + e.Message, e);
        }
        finally { if (Directory.Exists(unpack)) Directory.Delete(unpack, recursive: true); }
    }

    private static string RequireArchivePath(string folder, string name)
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(folder, name));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("An archive entry is outside the staging folder.");
        return path;
    }

    private void CheckMacSignature(string bundle)
    {
        if (MacSignatureCheck is { } check) { check(bundle); return; }
        RunChecked("/usr/bin/codesign", "--verify", "--deep", "--strict", bundle);
        RunChecked("/usr/sbin/spctl", "--assess", "--type", "execute", bundle);
        RunChecked("/usr/bin/xcrun", "stapler", "validate", bundle);
    }

    private static void RunChecked(string executable, params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo(executable, args) { UseShellExecute = false, CreateNoWindow = true })!;
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new UpdateException(UpdateError.BadSignature, "Package verification timed out.");
        }
        if (process.ExitCode != 0) throw new UpdateException(UpdateError.BadSignature, "Package verification was refused.");
    }

    private Process StartUnixHelper(string? staged, bool restart)
    {
        if (UsesSystemPackageManager) throw new UpdateException(UpdateError.WrongSystem, "Update this installation through apt.");
        if (staged is not null && Path.GetFullPath(staged) != Path.GetFullPath(StagingDir))
            throw new UpdateException(UpdateError.BadPackage, "The staged update is not next to this installation.");
        var start = new ProcessStartInfo(Executable(InstallDir, Platform)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
        foreach (var arg in new[] { "--update-helper", InstallDir, staged is null ? "rollback" : "apply", WaitPid.ToString(), restart ? "restart" : "stop" })
            start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }

    internal static int RunUnixHelper(string[] args)
    {
        if (args.Length != 5 || args[0] != "--update-helper" || args[2] is not ("apply" or "rollback") ||
            !int.TryParse(args[3], out var pid) || pid < 0 || args[4] is not ("restart" or "stop") || OperatingSystem.IsWindows()) return 2;
        var updater = new AppUpdater(new HttpClient(), "", "", Path.GetFullPath(args[1]));
        try
        {
            if (pid > 0)
            {
                try
                {
                    using var parent = Process.GetProcessById(pid);
                    if (!parent.WaitForExit(120_000)) return 1;
                }
                catch (ArgumentException) { /* The parent ended before the helper opened its handle. */ }
            }
            var incoming = args[2] == "rollback" ? updater.PreviousDir : updater.StagingDir;
            if (!File.Exists(Executable(incoming, updater.Platform))) throw new UpdateException(UpdateError.BadPackage, "The incoming version is incomplete.");
            new UpdateSwap(updater.InstallDir) { VerifyIncoming = updater.Platform == UpdatePlatform.MacOS ? updater.CheckMacSignature : null }.Apply(args[2] == "rollback");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UpdateException or Win32Exception)
        {
            Console.Error.WriteLine("textida update: " + e.Message);
            return 1;
        }
        finally
        {
            if (args[4] == "restart" && File.Exists(Executable(updater.InstallDir, updater.Platform)))
                Process.Start(new ProcessStartInfo(Executable(updater.InstallDir, updater.Platform)) { UseShellExecute = false });
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace NksHub.Textida.Install;

/// <summary>What stopped the setup. The text is the business of the caller, which shows it in the language of the user.</summary>
public enum SetupError { Running, BadPackage, UnknownDir, NotInstalled, NoPrevious, BadStaging, InvalidDir, Failed }

/// <summary>A refusal or a failure of the install, update, rollback or uninstall. Nothing was changed, or the change was undone, unless <see cref="Error"/> is <see cref="SetupError.Failed"/> with a note.</summary>
public sealed class SetupException(SetupError error, string detail, Exception? inner = null) : Exception(detail, inner)
{
    public SetupError Error { get; } = error;

    /// <summary>The folder or file the error is about, or the message of the system for <see cref="SetupError.Failed"/>.</summary>
    public string Detail { get; } = detail;
}

public enum SetupStage { Preparing, Files, Switching, Integration, Finishing }

/// <param name="Dir">The install folder.</param>
/// <param name="StartMenu">A shortcut in the Start menu.</param>
/// <param name="Desktop">A shortcut on the desktop.</param>
/// <param name="ContextMenu">The entry of the Explorer context menu.</param>
/// <param name="Extensions">The extensions Textida is offered for under Open with; the others are removed.</param>
/// <param name="MenuLabel">The text of the context menu entry, in the language of the user.</param>
/// <param name="SetupExe">The running setup; a copy without its package stays in the install folder as the uninstaller. Null leaves no uninstaller.</param>
public sealed record InstallOptions(string Dir, bool StartMenu, bool Desktop, bool ContextMenu, IReadOnlyList<string> Extensions, string MenuLabel, string? SetupExe);

/// <param name="PreviousDir">The folder that keeps the version that was replaced; null for a first install.</param>
/// <param name="Warnings">Things that did not work but do not undo the install, like a shortcut that could not be written.</param>
public sealed record InstallResult(string Version, bool Updated, string Dir, string ExePath, string? PreviousDir, IReadOnlyList<string> Warnings);

public sealed record InstalledProduct(string Dir, string Version);

/// <summary>
/// Install, update, rollback and uninstall of Textida for one user, without administrator rights. The package is the ZIP of the Windows package.
/// A first install extracts straight into the folder and removes it again when anything fails. An install over an existing one extracts next to it (<c>.new</c>) and switches the folders:
/// the old one stays as <c>.previous</c>, so <see cref="Rollback"/> can swap back. The portable profile (<c>profile</c>, <c>textida.portable</c>) and the uninstaller survive every switch.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Installer(SystemLocations locations)
{
    public const string ProgramFile = "Textida.exe";
    public const string SetupFile = "TextidaSetup.exe";
    public const string Publisher = "NKS Hub";
    public const string HomePage = "https://github.com/nks-hub/textida";

    /// <summary>The portable profile lives next to the program and must survive an update and an uninstall.</summary>
    private static readonly string[] Keep = ["profile", "textida.portable"];

    /// <summary>Reports the stage and how far it is, 0 to 1; called on the thread that does the work.</summary>
    public Action<SetupStage, double>? Progress { get; init; }

    /// <summary>True when the program at the path is running; tests replace it because their host is not Textida.exe.</summary>
    public Func<string, bool> IsRunning { get; init; } = ProgramIsRunning;

    /// <summary>The installed program: the folder named by the Apps list, else the default one. <paramref name="hint"/> is looked at first.</summary>
    public InstalledProduct? FindInstalled(string? hint = null)
    {
        string? registered;
        using (var key = Registry.CurrentUser.OpenSubKey(locations.UninstallKeyPath)) registered = key?.GetValue("InstallLocation") as string;
        foreach (var dir in new[] { hint, registered, locations.DefaultInstallDir }.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(Path.Combine(dir, ProgramFile))) return new InstalledProduct(dir, InstalledVersion(dir));
        return null;
    }

    public static string InstalledVersion(string dir)
    {
        try { return File.ReadAllText(Path.Combine(dir, "version.txt")).Trim() is { Length: > 0 } v ? v : "?"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "?"; }
    }

    /// <summary>Where the settings of the installed program live: next to it when it is portable, else in the profile of the user.</summary>
    public string UserDataLocation(string dir) => File.Exists(Path.Combine(dir, "textida.portable")) ? Path.Combine(dir, "profile") : locations.UserDataDir;

    /// <summary>The full path of an install folder as typed by the user, or <see cref="SetupError.InvalidDir"/>: it must be a full path and not a drive root.</summary>
    public static string NormalizeDir(string dir)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables((dir ?? "").Trim().Trim('"'));
            if (expanded.Length == 0 || !Path.IsPathFullyQualified(expanded)) throw new SetupException(SetupError.InvalidDir, dir ?? "");
            var full = Path.GetFullPath(expanded).TrimEnd('\\', '/');
            if (full.Length <= 2 || Path.GetPathRoot(full)!.TrimEnd('\\', '/').Length == full.Length) throw new SetupException(SetupError.InvalidDir, dir!);
            return full;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { throw new SetupException(SetupError.InvalidDir, dir ?? "", e); }
    }

    // ---- install ----

    public InstallResult Install(Stream package, InstallOptions options)
    {
        var dir = NormalizeDir(options.Dir);
        try { return InstallCore(package, options, dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException) { throw Fail(e); }
    }

    private InstallResult InstallCore(Stream package, InstallOptions options, string dir)
    {
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var entries = zip.Entries.Where(e => e.Name.Length > 0 && !e.FullName.Equals("Install.cmd", StringComparison.OrdinalIgnoreCase)).ToList();
        if (!entries.Any(e => e.FullName.Equals(ProgramFile, StringComparison.OrdinalIgnoreCase))) throw new SetupException(SetupError.BadPackage, ProgramFile);
        var version = ReadVersion(zip);
        var exe = Path.Combine(dir, ProgramFile);
        var updating = File.Exists(exe);
        if (!updating && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new SetupException(SetupError.UnknownDir, dir);
        if (IsRunning(exe)) throw new SetupException(SetupError.Running, exe);

        Report(SetupStage.Preparing, 0);
        var existed = Directory.Exists(dir);
        var target = updating ? dir + ".new" : dir;
        var previous = dir + ".previous";
        var total = entries.Sum(e => e.Length);
        try
        {
            if (updating) DeleteTree(target);
            Extract(entries, target, total);
            if (options.SetupExe is not null) PayloadFile.CopyWithoutPayload(options.SetupExe, Path.Combine(target, SetupFile));
            if (updating)
            {
                Report(SetupStage.Switching, 0);
                Switch(dir, target, rollback: false);
            }
        }
        catch
        {
            if (updating) DeleteTree(target);
            else if (existed) ClearDir(dir);
            else DeleteTree(dir);
            throw;
        }

        Report(SetupStage.Integration, 0);
        var warnings = new List<string>();
        try
        {
            WriteUninstallKey(dir, version, exe, total);
            var integration = new SystemIntegration(locations, exe);
            integration.SetMenu(options.ContextMenu, options.MenuLabel);
            integration.SetAssociations(options.Extensions);
            Shortcut(warnings, () => integration.SetStartMenuShortcut(options.StartMenu));
            Shortcut(warnings, () => integration.SetDesktopShortcut(options.Desktop));
            integration.Notify();
        }
        catch
        {
            Undo(updating, existed, dir, exe);
            throw;
        }
        Report(SetupStage.Finishing, 1);
        return new InstallResult(version, updating, dir, exe, updating ? previous : null, warnings);
    }

    private static void Shortcut(List<string> warnings, Action write)
    {
        try { write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
    }

    /// <summary>Takes back an install that failed after the files were in place. Best effort: the error that caused it is the one that is reported.</summary>
    private void Undo(bool updating, bool existed, string dir, string exe)
    {
        if (updating)
        {
            try { Switch(dir, dir + ".previous", rollback: true); } catch (Exception) { /* the folders stay as they are; the next run finds them */ }
            return;
        }
        try { new SystemIntegration(locations, exe).RemoveAll(); } catch (Exception) { /* nothing more to take back */ }
        try { Registry.CurrentUser.DeleteSubKeyTree(locations.UninstallKeyPath, throwOnMissingSubKey: false); } catch (Exception) { /* nothing more to take back */ }
        if (existed) ClearDir(dir);
        else DeleteTree(dir);
    }

    private void Extract(List<ZipArchiveEntry> entries, string root, long total)
    {
        Directory.CreateDirectory(root);
        var rootPrefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var buffer = new byte[81920];
        long done = 0;
        int reported = -1;
        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("An entry of the package points outside the install folder: " + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var from = entry.Open();
            using var to = new FileStream(path, FileMode.Create, FileAccess.Write);
            int read;
            while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
            {
                to.Write(buffer, 0, read);
                done += read;
                var percent = total > 0 ? (int)(done * 100 / total) : 100;
                if (percent != reported) Report(SetupStage.Files, (reported = percent) / 100.0);
            }
        }
    }

    /// <summary>The version a package carries (<c>version.txt</c>), without installing it; "0" when it names none or is not a package.</summary>
    public static string PackageVersion(Stream package)
    {
        try
        {
            using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
            return ReadVersion(zip);
        }
        catch (InvalidDataException) { return "0"; }
    }

    private static string ReadVersion(ZipArchive zip)
    {
        if (zip.GetEntry("version.txt") is not { } entry) return "0";
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd().Trim() is { Length: > 0 } version ? version : "0";
    }

    private void WriteUninstallKey(string dir, string version, string exe, long bytes)
    {
        var setup = Path.Combine(dir, SetupFile);
        using var key = Registry.CurrentUser.CreateSubKey(locations.UninstallKeyPath);
        key.SetValue("DisplayName", ShellIntegration.ProductName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", dir);
        key.SetValue("DisplayIcon", $"\"{exe}\",0"); // the icon of the program, the same one the shortcuts show
        key.SetValue("URLInfoAbout", HomePage);
        key.SetValue("UninstallString", $"\"{setup}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{setup}\" --uninstall --silent");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    // ---- update and rollback ----

    /// <summary>Switches the install folder to the verified, unpacked <paramref name="staged"/> folder; the old folder is kept as <c>.previous</c>. Returns the new version.</summary>
    public string ApplyUpdate(string dir, string staged)
    {
        try { return Switch(NormalizeDir(dir), Path.GetFullPath(staged).TrimEnd('\\', '/'), rollback: false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { throw Fail(e); }
    }

    /// <summary>Swaps the install folder and <c>.previous</c>. Returns the version that is installed afterwards.</summary>
    public string Rollback(string dir)
    {
        try { var full = NormalizeDir(dir); return Switch(full, full + ".previous", rollback: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { throw Fail(e); }
    }

    /// <summary>Fails before anything moved, or ends with the new folder in place; a crash between two moves is repaired by the next call (<c>.aside</c>).</summary>
    private string Switch(string dir, string incoming, bool rollback)
    {
        var previous = dir + ".previous";
        var aside = dir + ".aside";
        if (!Directory.Exists(dir) && Directory.Exists(aside)) Directory.Move(aside, dir);
        var exe = Path.Combine(dir, ProgramFile);
        if (IsRunning(exe)) throw new SetupException(SetupError.Running, exe);
        if (rollback && !File.Exists(Path.Combine(previous, ProgramFile))) throw new SetupException(SetupError.NoPrevious, dir);
        if (!rollback && !File.Exists(Path.Combine(incoming, ProgramFile))) throw new SetupException(SetupError.BadStaging, incoming);

        var here = Environment.CurrentDirectory;
        if (here.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) Directory.SetCurrentDirectory(Path.GetTempPath()); // a folder cannot be moved from under the process that stands in it
        foreach (var name in Keep)
        {
            var from = Path.Combine(dir, name);
            var to = Path.Combine(incoming, name);
            if (Directory.Exists(from)) { DeleteTree(to); CopyTree(from, to); }
            else if (File.Exists(from)) File.Copy(from, to, overwrite: true);
        }
        var uninstaller = Path.Combine(dir, SetupFile);
        if (File.Exists(uninstaller) && !File.Exists(Path.Combine(incoming, SetupFile))) File.Copy(uninstaller, Path.Combine(incoming, SetupFile));

        DeleteTree(aside);
        Directory.Move(dir, aside);
        try { Directory.Move(incoming, dir); }
        catch { Directory.Move(aside, dir); throw; }
        if (!rollback) DeleteTree(previous);
        Directory.Move(aside, previous);

        var version = InstalledVersion(dir);
        using (var key = Registry.CurrentUser.OpenSubKey(locations.UninstallKeyPath, writable: true))
            if (key is not null && version != "?") key.SetValue("DisplayVersion", version);
        return version;
    }

    // ---- uninstall ----

    /// <summary>
    /// Removes the menu entry, the associations, the shortcuts that point at this program, the entry of the Apps list and the files. The settings stay unless <paramref name="deleteUserData"/> is set.
    /// <paramref name="runningProgram"/> is the setup that runs from the install folder: it cannot delete itself, so it is left for the caller to remove after it ended.
    /// </summary>
    public void Uninstall(string dir, bool deleteUserData, string? runningProgram = null)
    {
        try
        {
            dir = NormalizeDir(dir);
            var exe = Path.Combine(dir, ProgramFile);
            if (!File.Exists(exe)) throw new SetupException(SetupError.NotInstalled, dir);
            if (IsRunning(exe)) throw new SetupException(SetupError.Running, exe);

            Report(SetupStage.Integration, 0);
            var integration = new SystemIntegration(locations, exe);
            integration.RemoveAll();
            integration.Notify();
            Registry.CurrentUser.DeleteSubKeyTree(locations.UninstallKeyPath, throwOnMissingSubKey: false);

            Report(SetupStage.Files, 0);
            var self = runningProgram is null ? null : Path.GetFullPath(runningProgram);
            if (Environment.CurrentDirectory.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) Directory.SetCurrentDirectory(Path.GetTempPath());
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir).ToList())
            {
                if (!deleteUserData && Keep.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)) continue;
                if (self is not null && string.Equals(Path.GetFullPath(entry), self, StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            foreach (var side in new[] { ".previous", ".new", ".aside" }) DeleteTree(dir + side);
            if (deleteUserData) DeleteTree(locations.UserDataDir);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            Report(SetupStage.Finishing, 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { throw Fail(e); }
    }

    // ---- helpers ----

    private void Report(SetupStage stage, double fraction) => Progress?.Invoke(stage, fraction);

    private static SetupException Fail(Exception e) => e switch
    {
        SetupException s => s,
        InvalidDataException => new SetupException(SetupError.BadPackage, e.Message, e),
        _ => new SetupException(SetupError.Failed, e.Message, e),
    };

    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static void ClearDir(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path).ToList())
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else File.Delete(entry);
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var sub in Directory.GetDirectories(from)) CopyTree(sub, Path.Combine(to, Path.GetFileName(sub)));
    }

    private static bool ProgramIsRunning(string exe)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ProgramFile)))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException) { /* a process of another user, or one that just ended */ }
            }
        }
        return false;
    }
}

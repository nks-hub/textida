using System.Diagnostics;
using System.Text;

namespace NksHub.Textida.Install;

/// <summary>Where the macOS integration of one user writes. The defaults are the real folders of the user; tests point every one of them at a temporary folder.</summary>
public sealed record MacLocations
{
    public string Home { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The folder with the quick actions and services of the user.</summary>
    public string ServicesDir => Path.Combine(Home, "Library", "Services");

    /// <summary>The first choice for the <c>textida</c> command; used when the folder exists and the user may write to it.</summary>
    public string SystemBinDir { get; init; } = "/usr/local/bin";

    /// <summary>The second choice; it is made when needed.</summary>
    public string UserBinDir => Path.Combine(Home, "bin");

    /// <summary>What the integration remembers between runs: the application each extension had before Textida took it, so that removing the association gives it back.</summary>
    public string StateFile => Path.Combine(Home, "Library", "Application Support", "Textida", "integration.txt");

    /// <summary>Asks the system to read the services again after a change (<c>pbs -update</c>); off in tests.</summary>
    public bool RefreshServices { get; init; } = true;
}

/// <summary>
/// What Textida adds to macOS for one user: the <c>textida</c> command in the terminal, the quick action "Open in Textida" for Finder and the associations of file extensions in Launch Services.
/// The state is always read from the disk and from Launch Services, never kept here, so the settings page, the Tools commands and the tests show and change the same thing; the one thing kept is the
/// application each extension had before (<see cref="MacLocations.StateFile"/>). Each change is written at once, and <see cref="RemoveAll"/> takes back all of them. The folder of the user's other
/// programs and the files of other applications are never touched: a link is removed only when it leads to this program, an action only when it carries <see cref="MacBundle.ActionMarker"/>.
/// </summary>
public sealed class MacIntegration(MacLocations locations, string executablePath, ILaunchServices launchServices)
{
    public const string CommandName = "textida";

    /// <summary>The program this integration points at: <c>Textida.app/Contents/MacOS/Textida</c>.</summary>
    public string ExecutablePath => executablePath;

    /// <summary>The application bundle of the program, or null when it does not run from one (a build folder): the Finder action and the associations need a bundle.</summary>
    public string? BundlePath => MacBundle.BundleOf(executablePath);

    // ---- the textida command ----

    /// <summary>The folder of the user the command goes to when the folder of the system is not writable; a terminal may not have it on its PATH.</summary>
    public string UserBinDir => locations.UserBinDir;

    private IEnumerable<string> CommandLinks() => new[] { locations.SystemBinDir, locations.UserBinDir }.Select(d => Path.Combine(d, CommandName));

    private bool LeadsHere(string link) => new FileInfo(link).LinkTarget is { } target && string.Equals(Path.GetFullPath(target, Path.GetDirectoryName(link)!), executablePath, StringComparison.Ordinal);

    /// <summary>The link that leads to this program, or null when there is none.</summary>
    public string? CommandLink() => CommandLinks().FirstOrDefault(LeadsHere);

    public bool HasCommand() => CommandLink() is not null;

    /// <summary>
    /// Makes the command: a symbolic link to the program, in <see cref="MacLocations.SystemBinDir"/> when the user may write there, otherwise in <see cref="MacLocations.UserBinDir"/>. A link that is
    /// there already and leads elsewhere is not replaced; <see cref="IOException"/> says so. Returns the link.
    /// </summary>
    public string AddCommand()
    {
        if (CommandLink() is { } existing) return existing;
        foreach (var dir in new[] { locations.SystemBinDir, locations.UserBinDir })
        {
            bool system = dir == locations.SystemBinDir;
            if (system && !Directory.Exists(dir)) continue;
            var link = Path.Combine(dir, CommandName);
            if (File.Exists(link) || new FileInfo(link).LinkTarget is not null)
            {
                if (system) continue; // another program's command of that name: the folder of the user is next
                throw new IOException($"{link} exists and does not lead to this program.");
            }
            try
            {
                Directory.CreateDirectory(dir);
                File.CreateSymbolicLink(link, executablePath);
                return link;
            }
            catch (Exception e) when (system && e is UnauthorizedAccessException or IOException) { /* the folder of the system is not the user's: the next one is */ }
        }
        throw new IOException($"The command could not be made in {locations.SystemBinDir} or {locations.UserBinDir}.");
    }

    public void RemoveCommand()
    {
        foreach (var link in CommandLinks().Where(LeadsHere)) File.Delete(link);
    }

    // ---- the quick action in Finder ----

    /// <summary>The folder name of the quick action, the same in every language; the name Finder shows is the label.</summary>
    public const string ActionFolderName = "Open in Textida";

    private string ActionFolder => Path.Combine(locations.ServicesDir, ActionFolderName + ".workflow");

    /// <summary>Whether the quick action is there and was written by Textida.</summary>
    public bool HasFinderAction() => File.Exists(Path.Combine(ActionFolder, "Contents", "document.wflow")) && File.ReadAllText(Path.Combine(ActionFolder, "Contents", "document.wflow")).Contains(MacBundle.ActionMarker, StringComparison.Ordinal);

    /// <summary>Writes (or writes again, pointing at this program) the quick action, which Finder shows as <paramref name="label"/>. Needs the application bundle; <see cref="InvalidOperationException"/> when the program runs from elsewhere.</summary>
    public void AddFinderAction(string label)
    {
        var bundle = BundlePath ?? throw new InvalidOperationException("The program does not run from an application bundle.");
        var folder = ActionFolder;
        if (Directory.Exists(folder) && !HasFinderAction()) throw new IOException($"{folder} exists and was not made by Textida.");
        foreach (var (relative, text) in MacBundle.FinderActionFiles(label, bundle))
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        RefreshServices();
    }

    public void RemoveFinderAction()
    {
        if (!HasFinderAction()) return;
        Directory.Delete(ActionFolder, recursive: true);
        RefreshServices();
    }

    /// <summary>Tells the system to read the services again, so the action shows without signing in again.</summary>
    private void RefreshServices()
    {
        if (!locations.RefreshServices) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/System/Library/CoreServices/pbs", "-update") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
            process?.WaitForExit(InstallLimitsPlaceholders.RefreshServicesMilliseconds);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { /* the action shows after the next sign-in */ }
    }

    // ---- associations in Launch Services ----

    private static string Key(string extension) => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();

    private Dictionary<string, string> LoadPrevious()
    {
        var previous = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(locations.StateFile)) return previous;
        foreach (var line in File.ReadAllLines(locations.StateFile))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0) previous[line[..tab]] = line[(tab + 1)..];
        }
        return previous;
    }

    private void SavePrevious(Dictionary<string, string> previous)
    {
        if (previous.Count == 0)
        {
            if (File.Exists(locations.StateFile)) File.Delete(locations.StateFile);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(locations.StateFile)!);
        File.WriteAllLines(locations.StateFile, previous.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "\t" + p.Value));
    }

    public bool IsAssociated(string extension) => string.Equals(launchServices.DefaultHandlerOf(Key(extension)), MacBundle.BundleId, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AssociatedExtensions() => [.. IntegrationExtensions.All.Where(IsAssociated)];

    /// <summary>Makes Textida the application for the extension; the one that was it before is remembered, so <see cref="SetAssociated"/> with false gives it back.</summary>
    public void SetAssociated(string extension, bool associated)
    {
        string key = Key(extension);
        var previous = LoadPrevious();
        if (associated)
        {
            if (IsAssociated(key)) return;
            string before = launchServices.DefaultHandlerOf(key) ?? "";
            launchServices.SetDefaultHandler(key, MacBundle.BundleId);
            if (before.Length > 0) previous[key] = before;
            SavePrevious(previous);
            return;
        }
        if (previous.Remove(key, out var handler) && handler.Length > 0 && !string.Equals(handler, MacBundle.BundleId, StringComparison.OrdinalIgnoreCase) && IsAssociated(key))
            launchServices.SetDefaultHandler(key, handler);
        SavePrevious(previous);
    }

    // ---- everything ----

    /// <summary>Takes back everything this class added: the command, the quick action and every association (each extension goes back to the application it had).</summary>
    public void RemoveAll()
    {
        RemoveCommand();
        RemoveFinderAction();
        foreach (var extension in IntegrationExtensions.All) SetAssociated(extension, false);
        foreach (var extension in LoadPrevious().Keys.ToList()) SetAssociated(extension, false);
    }
}

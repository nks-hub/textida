using System.Diagnostics;

namespace NksHub.Textida.Install;

/// <summary>
/// Where the desktop entries of one user and of the system are (XDG Base Directory). The defaults read <c>XDG_DATA_HOME</c> and <c>XDG_DATA_DIRS</c>; tests point both at a temporary folder.
/// </summary>
public sealed record LinuxLocations
{
    /// <summary>The data folder of the user, <c>~/.local/share</c> by default; install.sh writes the entries under it.</summary>
    public string DataHome { get; init; } = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } home ? home
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    /// <summary>The data folders of the system; the .deb puts its entries under <c>/usr/share</c>.</summary>
    public IReadOnlyList<string> DataDirs { get; init; } =
        (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Runs <c>update-desktop-database</c> after a change, as install.sh does; off in tests.</summary>
    public bool RefreshDatabase { get; init; } = true;

    public string UserApplicationsDir => Path.Combine(DataHome, "applications");
}

/// <summary>
/// The desktop entry that offers "Open in Textida as project" on folders in Linux file managers. It is a second entry of its own, so the main one never claims folders: it handles only
/// <c>inode/directory</c>, is hidden from the application menus (<c>NoDisplay</c>) and shows under "Open With" of a folder, where it runs the program with <c>--project</c>.
/// </summary>
public static class LinuxDesktop
{
    public const string ProjectEntryFile = "textida-project.desktop";

    /// <summary>The names of the entry in the languages of the program; English is the default.</summary>
    public static readonly IReadOnlyList<(string Language, string Name)> ProjectNames =
        [("cs", "Otevřít v Textidě jako projekt"), ("sk", "Otvoriť v Textide ako projekt"), ("de", "In Textida als Projekt öffnen")];

    public const string ProjectName = "Open in Textida as project";

    /// <summary>The text of the entry for the program at <paramref name="exec"/>.</summary>
    // shortcut: exec is written unquoted like in the main entry, fine for /opt/textida and ~/.local/opt/textida; quote it when a package installs elsewhere
    public static string ProjectEntry(string exec)
    {
        var names = string.Concat(ProjectNames.Select(n => $"Name[{n.Language}]={n.Name}\n"));
        return $"[Desktop Entry]\nType=Application\nName={ProjectName}\n{names}Exec={exec} --project %f\nIcon=textida\nTerminal=false\nNoDisplay=true\nMimeType=inode/directory;\nStartupWMClass=Textida\n";
    }

    /// <summary>Written over an entry of the system (from the .deb) that the user turned off: an entry of the user with <c>Hidden=true</c> counts as deleted.</summary>
    public static readonly string HiddenProjectEntry = $"[Desktop Entry]\nType=Application\nName={ProjectName}\nHidden=true\n";

    private static bool InSystem(LinuxLocations locations) => locations.DataDirs.Any(d => File.Exists(Path.Combine(d, "applications", ProjectEntryFile)));

    /// <summary>Whether folders offer the entry: the user's entry when there is one and it is not hidden, otherwise one of the system.</summary>
    public static bool HasProjectEntry(LinuxLocations locations)
    {
        var user = Path.Combine(locations.UserApplicationsDir, ProjectEntryFile);
        return File.Exists(user) ? !File.ReadLines(user).Any(l => l.Trim() == "Hidden=true") : InSystem(locations);
    }

    /// <summary>
    /// Writes the user's entry for the program at <paramref name="exec"/>, or takes the entry away: the user's file is deleted, or hidden when the system has one too, because the user cannot
    /// delete that one.
    /// </summary>
    public static void SetProjectEntry(LinuxLocations locations, bool enabled, string exec)
    {
        var user = Path.Combine(locations.UserApplicationsDir, ProjectEntryFile);
        if (enabled || InSystem(locations))
        {
            Directory.CreateDirectory(locations.UserApplicationsDir);
            File.WriteAllText(user, enabled ? ProjectEntry(exec) : HiddenProjectEntry);
        }
        else File.Delete(user);
        if (!locations.RefreshDatabase) return;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("update-desktop-database", [locations.UserApplicationsDir]) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
            process?.WaitForExit(InstallLimits.RefreshServicesMilliseconds);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { /* file managers that watch the folder still see the change */ }
    }
}

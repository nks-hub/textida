namespace NksHub.Textida.Install;

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
}

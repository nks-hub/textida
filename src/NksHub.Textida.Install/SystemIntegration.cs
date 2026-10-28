using System.Runtime.Versioning;

namespace NksHub.Textida.Install;

/// <summary>
/// What Textida adds to Windows for one user: the Explorer menu entry, the file associations and the shortcuts of the Start menu and of the desktop.
/// The state is always read from the registry and the disk, never kept here, so the settings page, the Tools commands and the installer show and change the same thing.
/// Each change is written at once; <see cref="Notify"/> tells Explorer afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemIntegration(SystemLocations locations, string exePath)
{
    public const string ShortcutFileName = "Textida.lnk";

    public string ExePath => exePath;

    public string StartMenuShortcut => Path.Combine(locations.StartMenuDir, ShortcutFileName);

    public string DesktopShortcut => Path.Combine(locations.DesktopDir, ShortcutFileName);

    private T WithShell<T>(Func<ShellIntegration, T> use)
    {
        using var classes = locations.OpenClasses();
        return use(new ShellIntegration(classes, exePath));
    }

    public bool HasMenu() => WithShell(s => s.HasMenu());

    /// <summary>Adds the entry with <paramref name="label"/>, or removes it. Adding again points an existing entry at this program.</summary>
    public void SetMenu(bool enabled, string label) => WithShell(s =>
    {
        if (enabled) s.AddMenu(label);
        else s.RemoveMenu();
        return 0;
    });

    public bool IsAssociated(string extension) => WithShell(s => s.IsAssociated(extension));

    public IReadOnlyList<string> AssociatedExtensions() => WithShell<IReadOnlyList<string>>(s => [.. ShellIntegration.Extensions.Where(s.IsAssociated)]);

    public void SetAssociated(string extension, bool associated) => WithShell(s =>
    {
        if (associated) s.Associate(extension);
        else s.Dissociate(extension);
        return 0;
    });

    /// <summary>Associates exactly <paramref name="extensions"/> of <see cref="ShellIntegration.Extensions"/> and removes the others.</summary>
    public void SetAssociations(IEnumerable<string> extensions)
    {
        var wanted = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in ShellIntegration.Extensions) SetAssociated(extension, wanted.Contains(extension));
    }

    public bool HasStartMenuShortcut() => File.Exists(StartMenuShortcut);

    public bool HasDesktopShortcut() => File.Exists(DesktopShortcut);

    public void SetStartMenuShortcut(bool enabled) => SetShortcut(StartMenuShortcut, enabled);

    public void SetDesktopShortcut(bool enabled) => SetShortcut(DesktopShortcut, enabled);

    private void SetShortcut(string path, bool enabled)
    {
        if (enabled) ShortcutFile.Create(path, exePath, Path.GetDirectoryName(exePath)!, ShellIntegration.ProductName);
        else DeleteFile(path);
    }

    /// <summary>File.Delete throws when the folder is missing, and a Start menu folder or a desktop that was never made has nothing to delete.</summary>
    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Removes the Explorer menu entry, every association and the program id; the shortcuts stay.</summary>
    public void RemoveExplorerIntegration() => WithShell(s =>
    {
        s.RemoveAll();
        return 0;
    });

    /// <summary>Removes everything this class can add. A shortcut is deleted only when it points at this program, because another copy of Textida may own a shortcut of the same name.</summary>
    public void RemoveAll()
    {
        RemoveExplorerIntegration();
        foreach (var path in new[] { StartMenuShortcut, DesktopShortcut })
            if (ShortcutFile.ReadTarget(path) is not { } target || string.Equals(target, exePath, StringComparison.OrdinalIgnoreCase)) DeleteFile(path);
    }

    public void Notify()
    {
        if (locations.NotifyExplorer) ShellIntegration.NotifyExplorer();
    }
}

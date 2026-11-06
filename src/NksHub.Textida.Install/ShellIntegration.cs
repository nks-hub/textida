using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace NksHub.Textida.Install;

/// <summary>
/// The Explorer context menu entry and the optional file associations of one user, written under <c>HKCU\Software\Classes</c> (<paramref name="classes"/>; tests pass a key of their own).
/// Nothing needs administrator rights and nothing outside the keys named here is read or written, so the keys of other programs, PSPad included, stay as they are.
/// Associations are added to <c>OpenWithProgids</c> of the extension: Textida shows in "Open with" and the user makes it the default in Windows, the way Windows 10 and 11 allow.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ShellIntegration(RegistryKey classes, string exePath)
{
    public const string ProductName = "Textida";

    public const string ProgId = ProductName + ".File";

    /// <summary>Extensions the user can pick from; none is associated until chosen.</summary>
    public static readonly IReadOnlyList<string> Extensions = IntegrationExtensions.All;

    private const string MenuPath = @"*\shell\" + ProductName;

    private const string FolderVerb = ProductName + ".Project";

    private const string FolderMenuPath = @"Directory\shell\" + FolderVerb;

    private const string BackgroundMenuPath = @"Directory\Background\shell\" + FolderVerb;

    /// <summary>The command Explorer runs: the program and the clicked file, quoted so a path with spaces stays one argument.</summary>
    public string Command => $"\"{exePath}\" \"%1\"";

    /// <summary>The command of the folder entry: <c>%1</c> is the clicked folder, <c>%V</c> the open folder whose background was clicked.</summary>
    public string FolderCommand(bool background) => $"\"{exePath}\" --project \"{(background ? "%V" : "%1")}\"";

    public static RegistryKey OpenUserClasses() => Registry.CurrentUser.CreateSubKey(@"Software\Classes");

    public void AddMenu(string label) => WriteVerb(MenuPath, label, Command);

    public bool HasMenu() { using var verb = classes.OpenSubKey(MenuPath); return verb is not null; }

    public void RemoveMenu() => RemoveVerb(MenuPath, @"*\shell", "*");

    /// <summary>The entry "Open in Textida as project" on a folder and on the background of an open folder: the folder becomes the root of the window (<c>--project</c>).</summary>
    public void AddFolderMenu(string label)
    {
        WriteVerb(FolderMenuPath, label, FolderCommand(background: false));
        WriteVerb(BackgroundMenuPath, label, FolderCommand(background: true));
    }

    public bool HasFolderMenu() { using var verb = classes.OpenSubKey(FolderMenuPath); return verb is not null; }

    public void RemoveFolderMenu()
    {
        RemoveVerb(BackgroundMenuPath, @"Directory\Background\shell", @"Directory\Background", @"Directory\shell", "Directory");
        RemoveVerb(FolderMenuPath, @"Directory\shell", "Directory");
    }

    private void WriteVerb(string path, string label, string commandLine)
    {
        using var verb = classes.CreateSubKey(path);
        verb.SetValue("", label);
        verb.SetValue("Icon", $"\"{exePath}\",0");
        using var command = verb.CreateSubKey("command");
        command.SetValue("", commandLine);
    }

    /// <summary>Deletes the entry, then each of <paramref name="parents"/> (innermost first) that nothing else lives in any more.</summary>
    private void RemoveVerb(string path, params string[] parents)
    {
        classes.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        foreach (var parent in parents)
        {
            using var key = classes.OpenSubKey(parent);
            if (key is null) continue;
            if (key is not { ValueCount: 0, SubKeyCount: 0 }) return;
            key.Dispose();
            classes.DeleteSubKey(parent, throwOnMissingSubKey: false);
        }
    }

    /// <summary>The icon of a program id: the .ico of the group next to the program, or the icon of the program when that file is not there (a copy that was not installed by the setup).</summary>
    private string IconOf(FileTypeGroup? group)
    {
        var ico = group is null ? null : Path.Combine(Path.GetDirectoryName(exePath) ?? "", FileTypeIcons.WindowsFolder, group.IcoFile);
        return ico is not null && File.Exists(ico) ? $"\"{ico}\"" : $"\"{exePath}\",0";
    }

    private void WriteProgId(string progId, FileTypeGroup? group)
    {
        using var key = classes.CreateSubKey(progId);
        key.SetValue("", group is null ? ProductName : $"{ProductName} {group.Name}");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", IconOf(group));
        using var open = key.CreateSubKey(@"shell\open");
        open.SetValue("FriendlyAppName", ProductName);
        using var command = open.CreateSubKey("command");
        command.SetValue("", Command);
    }

    /// <summary>Registers the program id of the group of <paramref name="extension"/> (with its own icon) and lists it under the extension; <see cref="ProgId"/> stays the program id of an extension that has no group.</summary>
    public void Associate(string extension)
    {
        var group = FileTypeIcons.GroupOf(extension);
        var progId = group is null ? ProgId : FileTypeIcons.ProgIdOf(group, ProgId);
        WriteProgId(progId, group);
        using var progIds = classes.CreateSubKey(extension + @"\OpenWithProgids");
        progIds.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        if (group is not null && progIds.GetValue(ProgId) is not null) progIds.DeleteValue(ProgId); // the entry of an older version, which had one icon for all
    }

    private static string ProgIdFor(string extension) => FileTypeIcons.GroupOf(extension) is { } g ? FileTypeIcons.ProgIdOf(g, ProgId) : ProgId;

    public bool IsAssociated(string extension)
    {
        using var progIds = classes.OpenSubKey(extension + @"\OpenWithProgids");
        return progIds?.GetValue(ProgIdFor(extension)) is not null || progIds?.GetValue(ProgId) is not null;
    }

    /// <summary>Removes the values written by <see cref="Associate"/>, then the keys that this left empty; the last association of a group also takes its program id, the last of all <see cref="ProgId"/>.</summary>
    public void Dissociate(string extension)
    {
        using (var progIds = classes.OpenSubKey(extension + @"\OpenWithProgids", writable: true))
        {
            if (progIds is null || !IsAssociated(extension)) return;
            foreach (var id in new[] { ProgIdFor(extension), ProgId }) if (progIds.GetValue(id) is not null) progIds.DeleteValue(id);
            if (progIds.ValueCount == 0 && progIds.SubKeyCount == 0)
            {
                progIds.Dispose();
                classes.DeleteSubKey(extension + @"\OpenWithProgids", throwOnMissingSubKey: false);
                using var ext = classes.OpenSubKey(extension);
                if (ext is { ValueCount: 0, SubKeyCount: 0 })
                {
                    ext.Dispose();
                    classes.DeleteSubKey(extension, throwOnMissingSubKey: false);
                }
            }
        }
        foreach (var group in FileTypeIcons.Groups)
            if (!group.Extensions.Any(IsAssociated)) classes.DeleteSubKeyTree(FileTypeIcons.ProgIdOf(group, ProgId), throwOnMissingSubKey: false);
        if (!Extensions.Any(IsAssociated)) classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
    }

    /// <summary>Removes the menu entries, every association and the program ids; used by the Tools command and by the uninstaller.</summary>
    public void RemoveAll()
    {
        RemoveMenu();
        RemoveFolderMenu();
        foreach (var extension in Extensions) Dissociate(extension);
        classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        foreach (var group in FileTypeIcons.Groups) classes.DeleteSubKeyTree(FileTypeIcons.ProgIdOf(group, ProgId), throwOnMissingSubKey: false);
    }

    /// <summary>Tells Explorer that associations changed so it does not wait for the next sign-in.</summary>
    public static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

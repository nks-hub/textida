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
    /// <summary>Working name (P3.7.6); the final name replaces this one constant and so every key below.</summary>
    public const string ProductName = "Textida";

    public const string ProgId = ProductName + ".File";

    /// <summary>Extensions the user can pick from; none is associated until chosen.</summary>
    public static readonly IReadOnlyList<string> Extensions = IntegrationExtensions.All;

    private const string MenuPath = @"*\shell\" + ProductName;

    /// <summary>The command Explorer runs: the program and the clicked file, quoted so a path with spaces stays one argument.</summary>
    public string Command => $"\"{exePath}\" \"%1\"";

    public static RegistryKey OpenUserClasses() => Registry.CurrentUser.CreateSubKey(@"Software\Classes");

    public void AddMenu(string label)
    {
        using var verb = classes.CreateSubKey(MenuPath);
        verb.SetValue("", label);
        verb.SetValue("Icon", $"\"{exePath}\",0");
        using var command = verb.CreateSubKey("command");
        command.SetValue("", Command);
    }

    public bool HasMenu() { using var verb = classes.OpenSubKey(MenuPath); return verb is not null; }

    public void RemoveMenu()
    {
        classes.DeleteSubKeyTree(MenuPath, throwOnMissingSubKey: false);
        foreach (var parent in new[] { @"*\shell", "*" }) // the parents that were created for the entry go too, when nothing else lives in them
        {
            using var key = classes.OpenSubKey(parent);
            if (key is not { ValueCount: 0, SubKeyCount: 0 }) return;
            key.Dispose();
            classes.DeleteSubKey(parent, throwOnMissingSubKey: false);
        }
    }

    public void Associate(string extension)
    {
        using (var progId = classes.CreateSubKey(ProgId))
        {
            progId.SetValue("", ProductName);
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exePath}\",0");
            using var open = progId.CreateSubKey(@"shell\open");
            open.SetValue("FriendlyAppName", ProductName);
            using var command = open.CreateSubKey("command");
            command.SetValue("", Command);
        }
        using var progIds = classes.CreateSubKey(extension + @"\OpenWithProgids");
        progIds.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
    }

    public bool IsAssociated(string extension)
    {
        using var progIds = classes.OpenSubKey(extension + @"\OpenWithProgids");
        return progIds?.GetValue(ProgId) is not null;
    }

    /// <summary>Removes the value written by <see cref="Associate"/>, then the keys that this left empty; the last association also takes the program id.</summary>
    public void Dissociate(string extension)
    {
        using (var progIds = classes.OpenSubKey(extension + @"\OpenWithProgids", writable: true))
        {
            if (progIds?.GetValue(ProgId) is null) return;
            progIds.DeleteValue(ProgId);
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
        if (!Extensions.Any(IsAssociated)) classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
    }

    /// <summary>Removes the menu entry, every association and the program id; used by the Tools command and by the uninstaller.</summary>
    public void RemoveAll()
    {
        RemoveMenu();
        foreach (var extension in Extensions) Dissociate(extension);
        classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
    }

    /// <summary>Tells Explorer that associations changed so it does not wait for the next sign-in.</summary>
    public static void NotifyExplorer() => SHChangeNotify(0x08000000, 0, 0, 0);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

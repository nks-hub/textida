namespace NksHub.Textida.Install;

/// <summary>
/// The limits and timeouts of the install, update and uninstall. EVERY NUMBER HERE IS A PLACEHOLDER waiting for the owner's decision; none of them is a decided value.
/// They live here and not in PluginLimitsPlaceholders of the editor because the setup program, which uses them, does not contain the editor.
/// </summary>
public static class InstallLimitsPlaceholders
{
    /// <summary>Placeholder, OPEN: milliseconds the setup waits for the editor to end before it switches the folders; after that it goes on and reports that Textida is still running. The owner has not decided it.</summary>
    public const int WaitForEditorMilliseconds = 60_000;

    /// <summary>Placeholder, OPEN: pings, about a second each and the first at once, that the command removing the uninstaller waits for the uninstaller to end before it deletes the file. The owner has not decided it.</summary>
    public const int SelfRemovalPings = 4;
}

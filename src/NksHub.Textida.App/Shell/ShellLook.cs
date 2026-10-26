using Avalonia.Input;

namespace NksHub.Textida.App.Shell;

/// <summary>How the D5 window frame is built on the current OS.</summary>
/// <param name="Mica">Translucent Mica backdrop (Windows 11 only); otherwise the background is a solid colour of the theme.</param>
/// <param name="NativeMenu">Menu lives in the system menu bar (macOS); otherwise it is drawn in the title strip.</param>
/// <param name="TabsInTitleBar">Tabs share the title bar with the window buttons (macOS); otherwise they are a row under the menu.</param>
public readonly record struct ShellLook(bool Mica, bool NativeMenu, bool TabsInTitleBar)
{
    /// <summary>First Windows 11 build; earlier builds (Windows 10 22H2 is 19045) have no Mica.</summary>
    public const int FirstWindows11Build = 22000;

    public static ShellLook Choose(bool isWindows, bool isMac, int windowsBuild) =>
        isMac ? new ShellLook(false, true, true)
        : new ShellLook(isWindows && windowsBuild >= FirstWindows11Build, false, false);

    public static ShellLook ForThisMachine() =>
        Choose(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), OperatingSystem.IsWindows() ? Environment.OSVersion.Version.Build : 0);
}

/// <summary>Primary shortcut modifier: Command on macOS, Control elsewhere.</summary>
public static class Shortcuts
{
    public static KeyModifiers Primary => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture Gesture(Key key, bool shift = false) => new(key, Primary | (shift ? KeyModifiers.Shift : KeyModifiers.None));

    /// <summary>Text for the shortcut as the user reads it, like "Ctrl+N" or the Command symbol plus N.</summary>
    public static string Display(Key key, bool shift = false)
    {
        var letter = key.ToString();
        return OperatingSystem.IsMacOS() ? (shift ? "⇧" : "") + "⌘" + letter : "Ctrl+" + (shift ? "Shift+" : "") + letter;
    }
}

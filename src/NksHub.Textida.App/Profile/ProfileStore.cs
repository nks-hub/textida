using System.Text.Json;
using System.Text.Json.Serialization;

namespace NksHub.Textida.App.Profile;

/// <summary>Where a profile lives: next to the program (portable), in the folder of the user, or in a folder the start named (the TEXTIDA_PROFILE variable).</summary>
public enum ProfileKind { Portable, User, Custom }

/// <summary>Where the profile lives: next to the executable when it carries a marker file, else in the user's profile.</summary>
public static class ProfileLocation
{
    public const string PortableMarker = "textida.portable";
    public const string PortableFolder = "profile";

    /// <summary>Pure rule so it can be tested without touching the disk.</summary>
    public static string Resolve(string exeDirectory, bool markerExists, bool isMac, string homeDirectory, string appDataDirectory)
    {
        if (markerExists) return Path.Combine(exeDirectory, PortableFolder);
        return isMac
            ? Path.Combine(homeDirectory, "Library", "Application Support", "Textida")
            : Path.Combine(appDataDirectory, "Textida");
    }

    /// <summary>Which of the places <see cref="Resolve"/> picks <paramref name="profileDirectory"/> is; a folder that is neither is a custom one.</summary>
    public static ProfileKind KindOf(string profileDirectory, string exeDirectory, bool markerExists, bool isMac, string homeDirectory, string appDataDirectory)
    {
        if (markerExists && SamePath(profileDirectory, Resolve(exeDirectory, true, isMac, homeDirectory, appDataDirectory))) return ProfileKind.Portable;
        return SamePath(profileDirectory, Resolve(exeDirectory, false, isMac, homeDirectory, appDataDirectory)) ? ProfileKind.User : ProfileKind.Custom;
    }

    public static ProfileKind KindForThisMachine(string profileDirectory, string exeDirectory) =>
        KindOf(profileDirectory, exeDirectory, File.Exists(Path.Combine(exeDirectory, PortableMarker)), OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public static string ResolveForThisMachine()
    {
        var exeDir = AppContext.BaseDirectory;
        return Resolve(exeDir, File.Exists(Path.Combine(exeDir, PortableMarker)), OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }
}

/// <summary>Last window geometry. Size is in device-independent units, position in physical pixels.</summary>
public sealed class SavedWindow
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    /// <summary>Where the side panels are docked and how large each side is; null in a profile from before the panels could be moved.</summary>
    public SavedPanels? Panels { get; set; }
}

/// <summary>The sides ("left", "right", "bottom") of the three side panels, and the width of the left and right side and the height of the bottom one, in device-independent units.</summary>
public sealed class SavedPanels
{
    public string Documents { get; set; } = "left";
    public string Files { get; set; } = "left";
    public string Plugin { get; set; } = "left";
    public double LeftWidth { get; set; } = 220;
    public double RightWidth { get; set; } = 220;
    public double BottomHeight { get; set; } = 200;
}

/// <summary>User settings. Values are language-neutral tags, never translated text.</summary>
public sealed class Settings
{
    public string Language { get; set; } = "cs";
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
}

[JsonSerializable(typeof(SavedWindow))]
[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ProfileJson : JsonSerializerContext;

/// <summary>Reads and writes the profile files. A missing or damaged file yields defaults instead of blocking startup.</summary>
public sealed class ProfileStore(string directory)
{
    public string Directory { get; } = directory;

    public SavedWindow? LoadWindow() => Load("window.json", ProfileJson.Default.SavedWindow);
    public void SaveWindow(SavedWindow state) => Save("window.json", state, ProfileJson.Default.SavedWindow);
    public Settings LoadSettings() => Load("settings.json", ProfileJson.Default.Settings) ?? new Settings();
    public void SaveSettings(Settings settings) => Save("settings.json", settings, ProfileJson.Default.Settings);

    private T? Load<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(System.IO.Path.Combine(Directory, file)), info); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void Save<T>(string file, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = System.IO.Path.Combine(Directory, file);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, info));
        File.Move(temp, path, overwrite: true);
    }
}

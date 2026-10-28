using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Services;

internal enum PluginOrigin { Bundled, Installed, Disabled, Unreadable }

internal sealed record AboutPlugin(string Name, string Version, PluginOrigin Origin, string? Problem = null);

/// <summary>
/// What the About window shows and "Copy information" copies, read once when the window opens. Nothing here touches the network: the commit and the date are
/// compiled in (<see cref="BuildStamp"/>), the rest is asked of the running system and of the files next to the program.
/// </summary>
internal sealed record AboutInfo(string Version, string Commit, string BuildDate, string Runtime, string Compilation, string Os, string Architecture,
    string Language, ProfileKind Profile, string ProfileFolder, string LogFolder, IReadOnlyList<AboutPlugin> Plugins, string Copyright)
{
    public const string ProjectUrl = "https://github.com/nks-hub/textida";
    public const string IssuesUrl = ProjectUrl + "/issues";
    public const string NoticesFile = "THIRD-PARTY-NOTICES.md";
    public const string LicenseFile = "LICENSE";

    public static AboutInfo Collect(App app, string programDirectory)
    {
        string version = VersionIn(programDirectory);
        return new AboutInfo(version, BuildStamp.Commit, BuildStamp.Date, RuntimeInformation.FrameworkDescription, RuntimeFeature.IsDynamicCodeCompiled ? "JIT" : "NativeAOT",
            HostFacts.OsName(), HostFacts.ArchitectureName(), Loc.Language, ProfileLocation.KindForThisMachine(app.ProfileDirectory, programDirectory),
            app.ProfileDirectory, app.Output.Folder, PluginsOf(app, version), CopyrightIn(programDirectory));
    }

    /// <summary>The version of the installed package (<c>version.txt</c> next to the program), else the version the program was built as.</summary>
    internal static string VersionIn(string programDirectory)
    {
        try { if (File.ReadAllText(Path.Combine(programDirectory, "version.txt")).Trim() is { Length: > 0 } v) return v; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a copy without the file */ }
        return BuildStamp.Version;
    }

    /// <summary>The copyright line of the <c>LICENSE</c> file next to the program; empty when the file is missing or has none.</summary>
    internal static string CopyrightIn(string programDirectory)
    {
        try { return File.ReadLines(Path.Combine(programDirectory, LicenseFile)).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Copyright", StringComparison.Ordinal)) ?? ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>The text of <c>THIRD-PARTY-NOTICES.md</c> next to the program, or null when it cannot be read.</summary>
    internal static string? NoticesIn(string programDirectory)
    {
        try { return File.ReadAllText(Path.Combine(programDirectory, NoticesFile)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The bundled plugins (they are built and shipped with the program, so they carry its version) and the installed ones.</summary>
    private static List<AboutPlugin> PluginsOf(App app, string programVersion)
    {
        var list = PluginServices.BuiltIns.Select(b => new AboutPlugin(b.Name, programVersion, PluginOrigin.Bundled)).ToList();
        foreach (var p in app.Plugins.Manager.Installed())
            list.Add(new AboutPlugin(p.Name, p.Version, p.Problem is not null ? PluginOrigin.Unreadable : p.Enabled ? PluginOrigin.Installed : PluginOrigin.Disabled, p.Problem));
        return list;
    }

    /// <summary>
    /// The plain-text block for a bug report. Its labels are English whatever the language of the program, so a report reads the same for whoever receives it.
    /// The folders of the profile and of the log carry the name of the user, so they come only with <paramref name="includePaths"/>.
    /// </summary>
    public string ToReport(bool includePaths)
    {
        var lines = new List<string>
        {
            "Textida " + Version,
            "Commit: " + OrUnknown(Commit),
            "Build date: " + OrUnknown(BuildDate),
            $"Runtime: {Runtime} ({Compilation})",
            "OS: " + Os,
            "Architecture: " + Architecture,
            "UI language: " + Language,
            "Profile: " + Profile.ToString().ToLowerInvariant(),
            "Plugins: " + string.Join(", ", Plugins.Select(p => $"{p.Name} {p.Version} ({OriginName(p.Origin)})")),
        };
        if (includePaths)
        {
            lines.Add("Profile folder: " + ProfileFolder);
            lines.Add("Log folder: " + LogFolder);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string OrUnknown(string value) => value.Length > 0 ? value : "unknown";

    private static string OriginName(PluginOrigin origin) => origin switch
    {
        PluginOrigin.Bundled => "bundled",
        PluginOrigin.Installed => "installed",
        PluginOrigin.Disabled => "installed, disabled",
        _ => "unreadable",
    };
}

/// <summary>What the machine says about itself, in words a bug report can use.</summary>
internal static partial class HostFacts
{
    private const string MacVersionFile = "/System/Library/CoreServices/SystemVersion.plist";

    private static readonly Regex ProductVersion = new(@"<key>ProductVersion</key>\s*<string>([^<]+)</string>", RegexOptions.CultureInvariant);

    public static string OsName()
    {
        if (OperatingSystem.IsWindows())
        {
            var v = Environment.OSVersion.Version;
            return $"Windows {(v.Build >= 22000 ? 11 : 10)} ({v.ToString(3)})";
        }
        if (OperatingSystem.IsMacOS())
        {
            try { if (ProductVersionOf(File.ReadAllText(MacVersionFile)) is { } version) return "macOS " + version; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the description below names the kernel */ }
        }
        return RuntimeInformation.OSDescription;
    }

    /// <summary>The <c>ProductVersion</c> of a macOS <c>SystemVersion.plist</c>, or null.</summary>
    internal static string? ProductVersionOf(string plist) => ProductVersion.Match(plist) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    public static string ArchitectureName() => ArchitectureName(RuntimeInformation.ProcessArchitecture, IsRunningUnderRosetta());

    internal static string ArchitectureName(Architecture architecture, bool rosetta) =>
        architecture.ToString().ToLowerInvariant() + (rosetta ? " (Rosetta on Apple silicon)" : "");

    /// <summary>True when this is an x64 process on a Mac with Apple silicon: the system translates it and says so in <c>sysctl.proc_translated</c>.</summary>
    public static bool IsRunningUnderRosetta()
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return false;
        int translated = 0;
        nuint size = sizeof(int);
        return SysCtlByName("sysctl.proc_translated", ref translated, ref size, 0, 0) == 0 && translated == 1;
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SysCtlByName(string name, ref int value, ref nuint size, nint newValue, nuint newSize);
}

using System.Security;
using System.Text;

namespace NksHub.Textida.Install;

/// <summary>
/// What makes <c>Textida.app</c> a macOS application, as text: the <c>Info.plist</c> of the bundle and the files of the Finder quick action. The package builder writes the first, the integration of a
/// running program writes the second; both are plain property lists, so they are checked on any system (the builder also runs <c>plutil -lint</c> on the Mac).
/// </summary>
public static class MacBundle
{
    /// <summary>Working identifier of the bundle (P3.7.6 has not decided the final name); the final one replaces this constant, and every place that needs it reads it from here.</summary>
    public const string BundleId = "cz.nks-hub.textida";

    public const string AppName = "Textida";

    /// <summary>The program inside the bundle, <c>Contents/MacOS/Textida</c>; it is also the command-line program.</summary>
    public const string ExecutableName = "Textida";

    /// <summary>The icon file in <c>Contents/Resources</c> without the extension, as <c>CFBundleIconFile</c> names it.</summary>
    public const string IconName = "Textida";

    /// <summary>Oldest macOS the bundle starts on (PR.1.2); every Mach-O file of the package has a minimum version of at most this major number.</summary>
    public const int MinimumMajor = 14;

    /// <summary>The languages of the program, for the system dialogs that follow the bundle.</summary>
    public static readonly IReadOnlyList<string> Localizations = ["en", "cs", "sk", "de"];

    /// <summary>
    /// The version in the form the bundle keys take: up to three numbers separated by dots. A version such as <c>1.4.0-beta.2+abc123</c> or <c>0.0.0-3f2a9c1</c> keeps its numbers
    /// (<c>1.4.0</c>, <c>0.0.0</c>); a text with no leading number is <c>0.0.0</c>.
    /// </summary>
    public static string BundleVersion(string version)
    {
        var numbers = new List<string>();
        foreach (var part in version.Split('-', '+')[0].Split('.'))
        {
            if (numbers.Count == 3 || part.Length == 0 || !part.All(char.IsAsciiDigit)) break;
            numbers.Add(part.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0");
        }
        return numbers.Count == 0 ? "0.0.0" : string.Join('.', numbers);
    }

    /// <summary>The path of the application bundle that holds <paramref name="executable"/> (<c>/Applications/Textida.app</c> for <c>/Applications/Textida.app/Contents/MacOS/Textida</c>), or null when it is not in one.</summary>
    public static string? BundleOf(string executable)
    {
        var folder = Path.GetDirectoryName(executable);
        if (folder is null || !string.Equals(Path.GetFileName(folder), "MacOS", StringComparison.Ordinal)) return null;
        var contents = Path.GetDirectoryName(folder);
        if (contents is null || !string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)) return null;
        var bundle = Path.GetDirectoryName(contents);
        return bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle : null;
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    private static void Entry(StringBuilder xml, string key, string value) => xml.Append("\t<key>").Append(Escape(key)).Append("</key>\n\t<string>").Append(Escape(value)).Append("</string>\n");

    private static void Flag(StringBuilder xml, string key, bool value) => xml.Append("\t<key>").Append(Escape(key)).Append("</key>\n\t<").Append(value ? "true" : "false").Append("/>\n");

    private static void Strings(StringBuilder xml, string indent, IEnumerable<string> values)
    {
        xml.Append(indent).Append("<array>\n");
        foreach (var value in values) xml.Append(indent).Append("\t<string>").Append(Escape(value)).Append("</string>\n");
        xml.Append(indent).Append("</array>\n");
    }

    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n";

    /// <summary>
    /// The <c>Info.plist</c> of the application bundle: name, identifier, the executable and the icon, the oldest system, the document types (the extensions of <see cref="IntegrationExtensions"/> and plain text,
    /// as an alternative handler, so installing the application takes no file type away from another one) and a retina-capable window.
    /// </summary>
    public static string InfoPlist(string version)
    {
        string bundleVersion = BundleVersion(version);
        var xml = new StringBuilder(Header);
        Entry(xml, "CFBundleDevelopmentRegion", "en");
        Entry(xml, "CFBundleDisplayName", AppName);
        Entry(xml, "CFBundleExecutable", ExecutableName);
        Entry(xml, "CFBundleIconFile", IconName);
        Entry(xml, "CFBundleIdentifier", BundleId);
        Entry(xml, "CFBundleInfoDictionaryVersion", "6.0");
        xml.Append("\t<key>CFBundleLocalizations</key>\n");
        Strings(xml, "\t", Localizations);
        Entry(xml, "CFBundleName", AppName);
        Entry(xml, "CFBundlePackageType", "APPL");
        Entry(xml, "CFBundleShortVersionString", bundleVersion);
        xml.Append("\t<key>CFBundleSupportedPlatforms</key>\n");
        Strings(xml, "\t", ["MacOSX"]);
        Entry(xml, "CFBundleVersion", bundleVersion);
        xml.Append("\t<key>CFBundleDocumentTypes</key>\n\t<array>\n");
        DocumentType(xml, "Text", extensions: IntegrationExtensions.All.Select(e => e.TrimStart('.')), contentTypes: null);
        DocumentType(xml, "Plain text", extensions: null, contentTypes: ["public.plain-text", "public.source-code"]);
        xml.Append("\t</array>\n");
        Entry(xml, "LSApplicationCategoryType", "public.app-category.developer-tools");
        Entry(xml, "LSMinimumSystemVersion", MinimumMajor + ".0");
        Flag(xml, "NSHighResolutionCapable", true);
        Entry(xml, "NSPrincipalClass", "NSApplication");
        xml.Append("</dict>\n</plist>\n");
        return xml.ToString();
    }

    private static void DocumentType(StringBuilder xml, string name, IEnumerable<string>? extensions, IEnumerable<string>? contentTypes)
    {
        xml.Append("\t\t<dict>\n");
        xml.Append("\t\t\t<key>CFBundleTypeName</key>\n\t\t\t<string>").Append(Escape(name)).Append("</string>\n");
        xml.Append("\t\t\t<key>CFBundleTypeRole</key>\n\t\t\t<string>Editor</string>\n");
        xml.Append("\t\t\t<key>LSHandlerRank</key>\n\t\t\t<string>Alternate</string>\n");
        if (extensions is not null)
        {
            xml.Append("\t\t\t<key>CFBundleTypeExtensions</key>\n");
            Strings(xml, "\t\t\t", extensions);
        }
        if (contentTypes is not null)
        {
            xml.Append("\t\t\t<key>LSItemContentTypes</key>\n");
            Strings(xml, "\t\t\t", contentTypes);
        }
        xml.Append("\t\t</dict>\n");
    }

    /// <summary>Marks the shell script of the quick action as written by Textida, so removing the integration never deletes an action of the same name that someone else made.</summary>
    public const string ActionMarker = "# textida-integration";

    /// <summary>
    /// The files of the quick action "Open in Textida" for Finder (a <c>.workflow</c> bundle, the form of a quick action made in Automator): relative path to text. It runs one shell script that opens
    /// the chosen files and folders with the application at <paramref name="bundlePath"/>; Finder offers it in the context menu under Quick Actions and in the Services menu.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FinderActionFiles(string label, string bundlePath)
    {
        string script = $"{ActionMarker}\nfor f in \"$@\"; do\n  open -a {ShellQuote(bundlePath)} \"$f\"\ndone\n";
        var info = new StringBuilder(Header);
        info.Append("\t<key>NSServices</key>\n\t<array>\n\t\t<dict>\n");
        info.Append("\t\t\t<key>NSBackgroundColorName</key>\n\t\t\t<string>background</string>\n");
        info.Append("\t\t\t<key>NSIconName</key>\n\t\t\t<string>NSActionTemplate</string>\n");
        info.Append("\t\t\t<key>NSMenuItem</key>\n\t\t\t<dict>\n\t\t\t\t<key>default</key>\n\t\t\t\t<string>").Append(Escape(label)).Append("</string>\n\t\t\t</dict>\n");
        info.Append("\t\t\t<key>NSMessage</key>\n\t\t\t<string>runWorkflowAsService</string>\n");
        info.Append("\t\t\t<key>NSRequiredContext</key>\n\t\t\t<dict>\n\t\t\t\t<key>NSApplicationIdentifier</key>\n\t\t\t\t<string>com.apple.finder</string>\n\t\t\t</dict>\n");
        info.Append("\t\t\t<key>NSSendFileTypes</key>\n");
        Strings(info, "\t\t\t", ["public.item"]);
        info.Append("\t\t</dict>\n\t</array>\n</dict>\n</plist>\n");
        return new Dictionary<string, string>
        {
            ["Contents/Info.plist"] = info.ToString(),
            ["Contents/document.wflow"] = Workflow(script),
        };
    }

    /// <summary>The single quotes keep a path with spaces and quotes in it one word of the shell.</summary>
    internal static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";

    private static string Workflow(string script)
    {
        // the description of one parameter of the action, as Automator writes it
        static string Argument(int number, string name, string defaultValueXml) =>
            $"\t\t\t\t\t<key>{number}</key>\n\t\t\t\t\t<dict>\n\t\t\t\t\t\t<key>default value</key>\n\t\t\t\t\t\t{defaultValueXml}\n"
            + $"\t\t\t\t\t\t<key>name</key>\n\t\t\t\t\t\t<string>{name}</string>\n\t\t\t\t\t\t<key>required</key>\n\t\t\t\t\t\t<string>0</string>\n\t\t\t\t\t\t<key>type</key>\n\t\t\t\t\t\t<string>0</string>\n"
            + $"\t\t\t\t\t\t<key>uuid</key>\n\t\t\t\t\t\t<string>{number}</string>\n\t\t\t\t\t</dict>\n";
        var xml = new StringBuilder(Header);
        xml.Append("\t<key>AMApplicationBuild</key>\n\t<string>523</string>\n\t<key>AMApplicationVersion</key>\n\t<string>2.10</string>\n\t<key>AMDocumentVersion</key>\n\t<string>2</string>\n");
        xml.Append("\t<key>actions</key>\n\t<array>\n\t\t<dict>\n\t\t\t<key>action</key>\n\t\t\t<dict>\n");
        xml.Append("\t\t\t\t<key>AMAccepts</key>\n\t\t\t\t<dict>\n\t\t\t\t\t<key>Container</key>\n\t\t\t\t\t<string>List</string>\n\t\t\t\t\t<key>Optional</key>\n\t\t\t\t\t<true/>\n\t\t\t\t\t<key>Types</key>\n");
        Strings(xml, "\t\t\t\t\t", ["com.apple.cocoa.string"]);
        xml.Append("\t\t\t\t</dict>\n\t\t\t\t<key>AMActionVersion</key>\n\t\t\t\t<string>2.0.3</string>\n\t\t\t\t<key>AMApplication</key>\n");
        Strings(xml, "\t\t\t\t", ["Automator"]);
        xml.Append("\t\t\t\t<key>AMParameterProperties</key>\n\t\t\t\t<dict>\n");
        foreach (var name in new[] { "COMMAND_STRING", "CheckedForUserDefaultShell", "inputMethod", "shell", "source" }) xml.Append("\t\t\t\t\t<key>").Append(name).Append("</key>\n\t\t\t\t\t<dict/>\n");
        xml.Append("\t\t\t\t</dict>\n\t\t\t\t<key>AMProvides</key>\n\t\t\t\t<dict>\n\t\t\t\t\t<key>Container</key>\n\t\t\t\t\t<string>List</string>\n\t\t\t\t\t<key>Types</key>\n");
        Strings(xml, "\t\t\t\t\t", ["com.apple.cocoa.string"]);
        xml.Append("\t\t\t\t</dict>\n");
        xml.Append("\t\t\t\t<key>ActionBundlePath</key>\n\t\t\t\t<string>/System/Library/Automator/Run Shell Script.action</string>\n\t\t\t\t<key>ActionName</key>\n\t\t\t\t<string>Run Shell Script</string>\n");
        xml.Append("\t\t\t\t<key>ActionParameters</key>\n\t\t\t\t<dict>\n");
        xml.Append("\t\t\t\t\t<key>COMMAND_STRING</key>\n\t\t\t\t\t<string>").Append(Escape(script)).Append("</string>\n");
        xml.Append("\t\t\t\t\t<key>CheckedForUserDefaultShell</key>\n\t\t\t\t\t<true/>\n\t\t\t\t\t<key>inputMethod</key>\n\t\t\t\t\t<integer>1</integer>\n");
        xml.Append("\t\t\t\t\t<key>shell</key>\n\t\t\t\t\t<string>/bin/zsh</string>\n\t\t\t\t\t<key>source</key>\n\t\t\t\t\t<string></string>\n\t\t\t\t</dict>\n");
        xml.Append("\t\t\t\t<key>BundleIdentifier</key>\n\t\t\t\t<string>com.apple.RunShellScript</string>\n\t\t\t\t<key>CFBundleVersion</key>\n\t\t\t\t<string>2.0.3</string>\n");
        xml.Append("\t\t\t\t<key>CanShowSelectedItemsWhenRun</key>\n\t\t\t\t<false/>\n\t\t\t\t<key>CanShowWhenRun</key>\n\t\t\t\t<true/>\n\t\t\t\t<key>Category</key>\n");
        Strings(xml, "\t\t\t\t", ["AMCategoryUtilities"]);
        xml.Append("\t\t\t\t<key>Class Name</key>\n\t\t\t\t<string>RunShellScriptAction</string>\n");
        xml.Append("\t\t\t\t<key>InputUUID</key>\n\t\t\t\t<string>5A5B5B94-6C0B-4F6B-9B5A-0F7A1D8E2C11</string>\n");
        xml.Append("\t\t\t\t<key>Keywords</key>\n");
        Strings(xml, "\t\t\t\t", ["Shell", "Script", "Command", "Run", "Unix"]);
        xml.Append("\t\t\t\t<key>OutputUUID</key>\n\t\t\t\t<string>8C1E3A47-2D4F-4B7A-A3E1-6B9D0F5C7A22</string>\n");
        xml.Append("\t\t\t\t<key>UUID</key>\n\t\t\t\t<string>3F7D9B21-5E6A-4C88-8B1D-2A4E6C8F0B33</string>\n");
        xml.Append("\t\t\t\t<key>UnlocalizedApplications</key>\n");
        Strings(xml, "\t\t\t\t", ["Automator"]);
        xml.Append("\t\t\t\t<key>arguments</key>\n\t\t\t\t<dict>\n");
        xml.Append(Argument(0, "inputMethod", "<integer>0</integer>")).Append(Argument(1, "source", "<string></string>")).Append(Argument(2, "CheckedForUserDefaultShell", "<true/>"))
            .Append(Argument(3, "COMMAND_STRING", "<string></string>")).Append(Argument(4, "shell", "<string>/bin/sh</string>"));
        xml.Append("\t\t\t\t</dict>\n\t\t\t\t<key>isViewVisible</key>\n\t\t\t\t<integer>1</integer>\n\t\t\t\t<key>location</key>\n\t\t\t\t<string>309.000000:253.000000</string>\n");
        xml.Append("\t\t\t\t<key>nibPath</key>\n\t\t\t\t<string>/System/Library/Automator/Run Shell Script.action/Contents/Resources/Base.lproj/main.nib</string>\n");
        xml.Append("\t\t\t</dict>\n\t\t\t<key>isViewVisible</key>\n\t\t\t<integer>1</integer>\n\t\t</dict>\n\t</array>\n\t<key>connectors</key>\n\t<dict/>\n");
        xml.Append("\t<key>workflowMetaData</key>\n\t<dict>\n\t\t<key>applicationBundleIDsByPath</key>\n\t\t<dict/>\n\t\t<key>applicationPaths</key>\n\t\t<array/>\n");
        xml.Append("\t\t<key>inputTypeIdentifier</key>\n\t\t<string>com.apple.Automator.fileSystemObject</string>\n\t\t<key>outputTypeIdentifier</key>\n\t\t<string>com.apple.Automator.nothing</string>\n");
        xml.Append("\t\t<key>presentationMode</key>\n\t\t<integer>15</integer>\n\t\t<key>processesInput</key>\n\t\t<integer>0</integer>\n");
        xml.Append("\t\t<key>serviceInputTypeIdentifier</key>\n\t\t<string>com.apple.Automator.fileSystemObject</string>\n\t\t<key>serviceOutputTypeIdentifier</key>\n\t\t<string>com.apple.Automator.nothing</string>\n");
        xml.Append("\t\t<key>serviceProcessesInput</key>\n\t\t<integer>0</integer>\n\t\t<key>systemImageName</key>\n\t\t<string>NSActionTemplate</string>\n");
        xml.Append("\t\t<key>useAutomaticInputType</key>\n\t\t<integer>0</integer>\n\t\t<key>workflowTypeIdentifier</key>\n\t\t<string>com.apple.Automator.servicesMenu</string>\n\t</dict>\n</dict>\n</plist>\n");
        return xml.ToString();
    }
}

using NksHub.Textida.Install;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Where the files that travel with the program are: the built-in plugins, the program an MCP client starts, and the license notices. They sit next to the executable, except in a macOS application
/// bundle: there <c>Contents/MacOS</c> may hold only the executable and its libraries (the signature of the bundle does not accept anything else in it), so these files are in <c>Contents/Resources</c>.
/// </summary>
internal static class ProgramFolders
{
    /// <summary>The folder with the plugins, the MCP program and the notices of the running program.</summary>
    public static string Files => FilesOf(AppContext.BaseDirectory);

    /// <summary>The same for a program in <paramref name="programDirectory"/>.</summary>
    public static string FilesOf(string programDirectory)
    {
        var folder = programDirectory.TrimEnd('/', '\\');
        if (MacBundle.BundleOf(folder + "/" + MacBundle.ExecutableName) is null) return programDirectory;
        return Path.Combine(Path.GetDirectoryName(folder)!, "Resources");
    }
}

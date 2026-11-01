using System.Xml.Linq;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>The hosts of the package sources a project's nuget.config files name, so the user's own feeds can be allowed for a C# server next to nuget.org.</summary>
public static class NuGetSources
{
    /// <summary>The http and https hosts of <c>packageSources</c> in the nuget.config files from <paramref name="root"/> upwards and in the config of the user.</summary>
    public static IReadOnlyList<string> Hosts(string root)
    {
        var files = new List<string>();
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
            foreach (var name in new[] { "nuget.config", "NuGet.Config", "NuGet.config" })
                if (File.Exists(Path.Combine(dir.FullName, name))) { files.Add(Path.Combine(dir.FullName, name)); break; }
        string home = OperatingSystem.IsWindows() ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "NuGet") : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".nuget", "NuGet");
        files.Add(Path.Combine(home, "NuGet.Config"));
        var hosts = new List<string>();
        foreach (var file in files)
        {
            try
            {
                if (!File.Exists(file)) continue;
                foreach (var add in XDocument.Load(file).Descendants("packageSources").Elements("add"))
                    if (Uri.TryCreate((string?)add.Attribute("value"), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !hosts.Contains(uri.Host)) hosts.Add(uri.Host);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { /* a broken config adds nothing */ }
        }
        return hosts;
    }
}

namespace NksHub.Textida.Install;

/// <summary>The extensions the user can make Textida the application of, on every system: the list of the Windows associations, the document types of the macOS bundle and the associations of the macOS settings.</summary>
public static class IntegrationExtensions
{
    /// <summary>With the leading dot; none is associated until chosen.</summary>
    public static readonly IReadOnlyList<string> All =
        [".txt", ".log", ".md", ".json", ".xml", ".html", ".css", ".js", ".ts", ".yml", ".yaml", ".ini", ".cfg", ".csv", ".sql", ".php", ".py", ".cs", ".sh", ".bat"];
}

namespace NksHub.Textida.Workbench.Tests;

public class SourceTreeTests
{
    private static readonly string[] Extensions = [".cs", ".json", ".md", ".csproj", ".axaml", ".sln", ".props", ".targets", ".xml", ".txt"];

    [Fact]
    public void No_file_of_the_sources_or_tests_holds_a_merge_conflict_marker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "NksHub.Textida.sln"))) root = root.Parent;
        Assert.NotNull(root);

        var found = new List<string>();
        foreach (var top in new[] { "src", "tests", "samples" })
        {
            var dir = Path.Combine(root.FullName, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                int line = 0;
                foreach (var text in File.ReadLines(file))
                {
                    line++;
                    if (text.StartsWith("<<<<<<< ", StringComparison.Ordinal) || text.StartsWith(">>>>>>> ", StringComparison.Ordinal))
                        found.Add($"{Path.GetRelativePath(root.FullName, file)}:{line}");
                }
            }
        }
        Assert.True(found.Count == 0, "Merge conflict markers: " + string.Join(", ", found));
    }
}

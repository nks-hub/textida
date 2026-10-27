using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.5.1: the project root a language server is started for: markers of the language, then <c>.git</c> or <c>.textida</c>, within a bounded climb.</summary>
public sealed class ProjectRootsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-roots-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Folder(string relative) => Directory.CreateDirectory(Path.Combine(_dir, relative)).FullName;

    private void Touch(string relativeFolder, string name) => File.WriteAllText(Path.Combine(Folder(relativeFolder), name), "");

    [Fact]
    public void A_solution_above_a_project_file_wins_over_it_and_a_project_file_alone_is_the_root()
    {
        Touch("sol", "All.sln");
        Touch("sol/src/App", "App.csproj");
        Touch("lonely/src", "Lib.csproj");

        Assert.Equal(Path.Combine(_dir, "sol"), ProjectRoots.Find(Folder("sol/src/App/Models"), "csharp", 8));
        Assert.Equal(Path.Combine(_dir, "lonely", "src"), ProjectRoots.Find(Folder("lonely/src/Models"), "csharp", 3)); // a bound that stays inside the test folder: no solution above it can change the answer
    }

    [Fact]
    public void The_marker_of_the_language_decides_and_the_nearest_folder_with_it_wins()
    {
        Touch("rs", "Cargo.toml");
        Touch("rs/member", "Cargo.toml");
        Touch("php", "composer.json");
        Touch("dart", "pubspec.yaml");
        Touch("ts", "package.json");
        Touch("c", "CMakeLists.txt");
        Touch("c/build", "compile_commands.json");

        Assert.Equal(Path.Combine(_dir, "rs", "member"), ProjectRoots.Find(Folder("rs/member/src"), "rust", 8));
        Assert.Equal(Path.Combine(_dir, "php"), ProjectRoots.Find(Folder("php/src/Model"), "php", 8));
        Assert.Equal(Path.Combine(_dir, "dart"), ProjectRoots.Find(Folder("dart/lib"), "dart", 8));
        Assert.Equal(Path.Combine(_dir, "ts"), ProjectRoots.Find(Folder("ts/src"), "typescript", 8));
        Assert.Equal(Path.Combine(_dir, "c", "build"), ProjectRoots.Find(Folder("c/build/obj"), "cpp", 8));
        Assert.Equal(Path.Combine(_dir, "c"), ProjectRoots.Find(Folder("c/src"), "c", 2));
    }

    [Fact]
    public void Without_a_marker_of_the_language_the_repository_folder_is_the_root_and_it_ends_the_search()
    {
        Touch("outer", "package.json");
        Folder("outer/repo/.git");
        Folder("marked/.textida");

        Assert.Equal(Path.Combine(_dir, "outer", "repo"), ProjectRoots.Find(Folder("outer/repo/src"), "javascript", 8)); // the package.json above the repository is not its own
        Assert.Equal(Path.Combine(_dir, "outer", "repo"), ProjectRoots.Find(Folder("outer/repo/src"), "python", 8));
        Assert.Equal(Path.Combine(_dir, "marked"), ProjectRoots.Find(Folder("marked/a/b"), "unknown-language", 8));
    }

    [Fact]
    public void The_climb_stops_after_the_given_number_of_folders()
    {
        Touch("proj", "composer.json");
        var deep = Folder("proj/a/b/c");

        Assert.Equal(Path.Combine(_dir, "proj"), ProjectRoots.Find(deep, "php", 4));
        Assert.Null(ProjectRoots.Find(deep, "php", 3));
        Assert.Null(ProjectRoots.Find(deep, "php", 0));
    }

    [Fact]
    public void Nothing_is_found_without_a_marker_or_a_folder()
    {
        Assert.Null(ProjectRoots.Find(Folder("bare"), "rust", 1));
        Assert.Null(ProjectRoots.Find(null, "rust", 8));
    }
}

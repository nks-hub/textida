using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

public class PackageManifestsTests
{
    [Theory]
    [InlineData("package.json", "npm")]
    [InlineData("composer.json", "packagist")]
    [InlineData("requirements.txt", "pypi")]
    [InlineData("requirements-dev.txt", "pypi")]
    [InlineData("App.csproj", "nuget")]
    [InlineData("go.mod", "go")]
    [InlineData("Cargo.toml", "crates")]
    [InlineData("Gemfile", "rubygems")]
    [InlineData("pubspec.yaml", "pub")]
    [InlineData("pom.xml", "maven")]
    [InlineData("build.gradle", "maven")]
    [InlineData("build.gradle.kts", "maven")]
    public void A_manifest_has_the_registry_of_its_file_name(string file, string registry) =>
        Assert.Equal(registry, PackageManifests.For(Path.Combine("some", "folder", file))?.Id);

    [Theory]
    [InlineData("package-lock.json")]
    [InlineData("tsconfig.json")]
    [InlineData("notes.txt")]
    [InlineData("go.sum")]
    public void Any_other_file_has_no_registry(string file) => Assert.Null(PackageManifests.For(file));

    [Fact]
    public void Scripts_are_read_with_their_text_and_the_runner_that_starts_them()
    {
        var npm = PackageManifests.Scripts("package.json", "{ /* c */ \"scripts\": { \"build\": \"tsc\", \"-x\": \"no\", \"test:unit\": [\"a\", \"b\"], }, }");
        Assert.Equal(["build", "test:unit"], npm.Select(s => s.Name));
        Assert.Equal("npm", npm[1].Executable);
        Assert.Equal(["run", "test:unit"], npm[1].Args);
        Assert.Equal("\"tsc\"", npm[0].Body);

        var composer = PackageManifests.Scripts("composer.json", "{ \"scripts\": { \"post-update-cmd\": \"x\" } }");
        Assert.Equal("composer", composer[0].Executable);
        Assert.Equal(["run-script", "post-update-cmd"], composer[0].Args);

        Assert.Empty(PackageManifests.Scripts("package.json", "{ \"scripts\": "));
        Assert.Empty(PackageManifests.Scripts("package.json", "{ \"name\": \"x\" }"));
        Assert.Empty(PackageManifests.Scripts("go.mod", "{ \"scripts\": { \"a\": \"b\" } }"));
    }

    [Fact]
    public void On_Windows_the_program_behind_a_batch_file_is_found_next_to_it()
    {
        string root = Path.Combine(Path.GetTempPath(), "textida-pm-" + Guid.NewGuid().ToString("N"));
        string node = Directory.CreateDirectory(Path.Combine(root, "node")).FullName, cli = Directory.CreateDirectory(Path.Combine(node, "node_modules", "npm", "bin")).FullName;
        string php = Directory.CreateDirectory(Path.Combine(root, "php")).FullName;
        try
        {
            foreach (var file in new[] { Path.Combine(node, "npm.cmd"), Path.Combine(node, "node.exe"), Path.Combine(cli, "npm-cli.js"), Path.Combine(php, "composer.bat"), Path.Combine(php, "composer.phar"), Path.Combine(php, "php.exe") })
                File.WriteAllText(file, "");
            string path = string.Join(Path.PathSeparator, node, php);

            var npm = PackageManifests.WindowsTarget("npm", path)!.Value;
            Assert.Equal(Path.Combine(node, "node.exe"), npm.Executable);
            Assert.Equal([Path.Combine(cli, "npm-cli.js")], npm.Prefix);
            var composer = PackageManifests.WindowsTarget("composer", path)!.Value;
            Assert.Equal(Path.Combine(php, "php.exe"), composer.Executable);
            Assert.Equal([Path.Combine(php, "composer.phar")], composer.Prefix);
            Assert.Null(PackageManifests.WindowsTarget("npm", php)); // no npm.cmd on this path
            Assert.Null(PackageManifests.WindowsTarget("make", path));
            File.Delete(Path.Combine(cli, "npm-cli.js"));
            Assert.Null(PackageManifests.WindowsTarget("npm", path)); // not the standard layout: the runner reports the batch file
        }
        finally { Directory.Delete(root, true); }
    }
}

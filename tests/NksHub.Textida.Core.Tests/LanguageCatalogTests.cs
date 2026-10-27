using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8.5.3 and P8.7.4: the table of language tools, the discovery of a program on disk and the configuration files that apply to a file.
/// ToolEcho stands in for every tool. No real formatter, linter or language server is started, so their command lines are not verified here (only gofmt has a test with the real program).
/// </summary>
public sealed class LanguageCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-catalog-").FullName;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly string[] GroupIds =
        ["js-ts", "web", "sql", "python", "shell", "java", "csharp", "c-cpp", "php", "go", "rust", "kotlin", "lua", "swift", "ruby", "dart", "r"];

    private static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>Copies ToolEcho into <paramref name="folder"/> under another name: the program with the files it loads, so the renamed copy still runs.</summary>
    private static string InstallFakeTool(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(folder, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string target = Path.Combine(folder, ExeName(name));
        File.Copy(Path.Combine(AppContext.BaseDirectory, ExeName("NksHub.Textida.ToolEcho")), target, overwrite: true);
        return target;
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    // ---- the table ----

    [Fact]
    public void Every_one_of_the_17_groups_has_a_record_with_modes_and_each_mode_has_a_grammar()
    {
        Assert.Equal(GroupIds, LanguageCatalog.Groups.Select(g => g.Id));
        Assert.All(LanguageCatalog.Groups, g =>
        {
            Assert.NotEmpty(g.Modes);
            Assert.All(g.Modes, m =>
            {
                Assert.True(m.Extensions.Count + m.FileNames.Count > 0, m.Id);
                Assert.True(m.Grammar is not null && BuiltinGrammars.Registry.Find(m.Grammar) is not null, $"{m.Id}: no grammar '{m.Grammar}'");
            });
        });
        var ids = LanguageCatalog.Groups.SelectMany(g => g.Modes).Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var extensions = LanguageCatalog.Groups.SelectMany(g => g.Modes).SelectMany(m => m.Extensions).ToList();
        Assert.Equal(extensions.Count, extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(1, LanguageCatalog.Version);
    }

    [Fact]
    public void The_tools_of_the_plan_are_data_in_the_catalog_with_a_severity_table_for_every_linter()
    {
        var formatters = LanguageCatalog.Formatters.Select(f => f.Id).ToHashSet();
        Assert.Superset(new HashSet<string> { "gofmt", "rustfmt", "clang-format", "rubocop", "prettier", "biome", "ruff-format", "shfmt", "sqlfluff-fix", "stylua", "dart-format" }, formatters);
        var linters = LanguageCatalog.Linters.Select(l => l.Id).ToHashSet();
        Assert.Superset(new HashSet<string> { "phpstan", "psalm", "golangci-lint", "clippy", "clang-tidy", "rubocop-lint", "ruff", "shellcheck", "sqlfluff" }, linters);
        Assert.All(LanguageCatalog.Linters, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.OutputFormat), l.Id);
            Assert.NotEmpty(l.Severities);
            Assert.NotEmpty(l.Extensions); // Clippy checks the whole crate, so a linter need not take the file as an argument
        });
        Assert.All(LanguageCatalog.Formatters, f => Assert.Contains("${File}", f.Args));
        // no version is pinned and only gofmt was run for real: the table says so instead of claiming more
        Assert.All(LanguageCatalog.Lsp, l => Assert.Null(l.Version));
        Assert.Equal(["gofmt"], LanguageCatalog.Formatters.Where(f => f.Verified).Select(f => f.Id));
    }

    [Fact]
    public void The_consumers_read_the_catalog_and_behave_as_before()
    {
        Assert.Equal(["clang-format", "gofmt", "rubocop", "rustfmt"], ConfigFormatters.All.Select(f => f.Id).Order());
        var gofmt = ConfigFormatters.Find("gofmt")!;
        Assert.Equal("gofmt", gofmt.Executable);
        Assert.Equal([".go"], gofmt.Extensions);
        Assert.Equal(["go.mod"], gofmt.ConfigFiles);
        Assert.Equal(["-w", "${File}"], gofmt.Args);
        Assert.Contains(".cs", ConfigFormatters.Find("clang-format")!.Extensions);
        Assert.Null(ConfigFormatters.Find("prettier")); // data only: Save Actions offers just the four that were tried
        // the language servers the plugin starts: Roslyn first, csharp-ls as the fallback
        Assert.Equal(["Microsoft.CodeAnalysis.LanguageServer", "csharp-ls"], LanguageCatalog.Lsp.Where(l => l.Shipped).Select(l => l.Program));
        Assert.Equal(["--stdio", "--telemetryLevel", "off"], LanguageCatalog.Lsp.First(l => l.Shipped).Arguments);
    }

    // ---- discovery ----

    [Fact]
    public void A_tool_in_the_PATH_is_found_and_a_missing_one_is_not()
    {
        string tool = Unique("fake-path-");
        string folder = Path.Combine(_dir, "bin");
        string installed = InstallFakeTool(folder, tool);
        Environment.SetEnvironmentVariable("PATH", _path + Path.PathSeparator + folder);
        Assert.Equal(installed, ToolLocator.Find(tool), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(ToolLocator.Find(Unique("missing-")));
    }

    [Theory]
    [InlineData("node_modules/.bin")]
    [InlineData("vendor/bin")]
    [InlineData(".venv/Scripts")]
    [InlineData(".venv/bin")]
    public void A_tool_in_a_project_folder_is_found_from_a_subfolder_of_the_project(string projectFolder)
    {
        string tool = Unique("fake-local-");
        string project = Path.Combine(_dir, "project");
        string installed = InstallFakeTool(Path.Combine(project, projectFolder), tool);
        string start = Path.Combine(project, "src", "deep");
        Directory.CreateDirectory(start);
        Assert.Equal(installed, ToolLocator.Find(tool, start), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(ToolLocator.Find(tool)); // without a folder to start from only the PATH counts
        Assert.Null(ToolLocator.Find(tool, Path.Combine(_dir, "elsewhere")));
    }

    [Fact]
    public void A_path_from_the_user_wins_over_the_project_and_the_PATH_and_a_folder_from_the_user_is_searched()
    {
        string tool = Unique("fake-user-");
        string project = Path.Combine(_dir, "project");
        InstallFakeTool(Path.Combine(project, "node_modules", ".bin"), tool);
        string own = InstallFakeTool(Path.Combine(_dir, "mine"), tool);
        Assert.Equal(own, ToolLocator.Find(tool, project, userPath: own), ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(own, ToolLocator.Find(tool, project, userPath: Path.GetDirectoryName(own)), ignoreCase: OperatingSystem.IsWindows());
        Assert.Null(ToolLocator.Find(tool, null, userPath: Path.Combine(_dir, "nothing-here"))); // a wrong path from the user is not silently replaced by a guess in the PATH
    }

    [Fact]
    public void Nothing_in_the_catalog_or_the_discovery_can_reach_the_network()
    {
        var referenced = typeof(ToolLocator).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(referenced, n => n.StartsWith("System.Net.Http", StringComparison.Ordinal) || n.StartsWith("System.Net.Sockets", StringComparison.Ordinal));
    }

    // ---- the generic scenarios, through ToolEcho (success, syntax error, missing tool, timeout) ----

    [Fact]
    public async Task A_discovered_tool_runs_ends_with_its_exit_code_is_stopped_at_the_time_limit_and_a_missing_one_is_reported()
    {
        string tool = Unique("fake-run-");
        string project = Path.Combine(_dir, "project");
        InstallFakeTool(Path.Combine(project, "node_modules", ".bin"), tool);
        string found = ToolLocator.Find(tool, project)!;
        var lines = new List<string>();

        var success = await ToolRunner.RunAsync(ToolRunner.Resolve(new ToolSpec("ok", found, ["--both"], null, TimeSpan.FromSeconds(30)), _ => null, _dir), lines.Add);
        Assert.Equal(new ToolResult(ToolEnd.Exited, 0), success);

        var syntaxError = await ToolRunner.RunAsync(ToolRunner.Resolve(new ToolSpec("bad", found, ["--format-fail", "x.txt"], null, TimeSpan.FromSeconds(30)), _ => null, _dir), lines.Add);
        Assert.Equal(new ToolResult(ToolEnd.Exited, 2), syntaxError);
        Assert.Contains("syntax error", lines);

        var slow = await ToolRunner.RunAsync(ToolRunner.Resolve(new ToolSpec("slow", found, ["--sleep"], null, TimeSpan.FromSeconds(1)), _ => null, _dir), lines.Add);
        Assert.Equal(ToolEnd.TimedOut, slow.End);

        Assert.Throws<ToolException>(() => ToolRunner.Resolve(new ToolSpec("none", Unique("missing-"), [], null, TimeSpan.FromSeconds(1)), _ => null, _dir));
    }

    // ---- configuration files of a file ----

    private string Write(string relative, string content = "")
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_file_in_a_subfolder_gets_the_nearest_parent_editorconfig_and_clang_format()
    {
        string root = Write(".editorconfig", "root = true\n");
        string rootFormat = Write(".clang-format", "BasedOnStyle: LLVM\n");
        string nearFormat = Write(Path.Combine("src", "lib", ".clang-format"), "BasedOnStyle: Google\n");
        string source = Write(Path.Combine("src", "lib", "deep", "a.cpp"), "int x;\n");
        string other = Write(Path.Combine("src", "main.c"), "int y;\n");

        var hits = ConfigFiles.For(source).ToDictionary(h => h.Config.Names[0], h => h.Path);
        Assert.Equal(root, hits[".editorconfig"]);
        Assert.Equal(nearFormat, hits[".clang-format"]);
        Assert.Null(hits[".clang-tidy"]); // reported as missing, never searched by running clang-tidy
        Assert.Equal(rootFormat, ConfigFiles.For(other).First(h => h.Config.Names[0] == ".clang-format").Path);
    }

    [Fact]
    public void The_configuration_of_a_file_follows_its_language_and_the_tools_that_handle_it()
    {
        Assert.Equal([".editorconfig"], LanguageCatalog.ConfigsFor("notes.txt").Select(c => c.Names[0]));
        Assert.Contains(".rubocop.yml", LanguageCatalog.ConfigsFor("a.rb").Select(c => c.Names[0]));
        Assert.Contains("rustfmt.toml", LanguageCatalog.ConfigsFor("a.rs").Select(c => c.Names[0]));
        Assert.Contains(".golangci.yml", LanguageCatalog.ConfigsFor("a.go").Select(c => c.Names[0]));
        Assert.Contains("ruff.toml", LanguageCatalog.ConfigsFor("a.py").Select(c => c.Names[0]));
        var csharp = LanguageCatalog.ConfigsFor("a.cs").Select(c => c.Names[0]).ToList();
        Assert.Contains(".clang-format", csharp); // clang-format formats C# too
        Assert.DoesNotContain(".clang-tidy", csharp);
        Assert.Contains(".clang-tidy", LanguageCatalog.ConfigsFor("CMakeLists.txt").Select(c => c.Names[0])); // by file name
    }

    [Fact]
    public void A_missing_configuration_is_created_from_its_template_in_the_project_folder_and_never_replaced()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        string source = Write(Path.Combine("src", "a.rb"), "x = 1\n");
        var rubocop = ConfigFiles.For(source).First(h => h.Config.Names[0] == ".rubocop.yml").Config;
        string created = ConfigFiles.Create(rubocop, source);
        Assert.Equal(Path.Combine(_dir, ".rubocop.yml"), created);
        Assert.Equal("AllCops:\n  NewCops: disable\n", File.ReadAllText(created));
        Assert.Equal(created, ConfigFiles.For(source).First(h => h.Config.Names[0] == ".rubocop.yml").Path);
        Assert.Throws<IOException>(() => ConfigFiles.Create(rubocop, source));
        var noTemplate = LanguageCatalog.ConfigsFor("a.py").First(c => c.Names[0] == "pyproject.toml");
        Assert.Throws<InvalidOperationException>(() => ConfigFiles.Create(noTemplate, Write("a.py")));
    }
}

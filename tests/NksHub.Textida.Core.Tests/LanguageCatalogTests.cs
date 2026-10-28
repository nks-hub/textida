using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8.5.3 and P8.7.4: the table of language tools, the discovery of a program on disk and the configuration files that apply to a file.
/// ToolEcho stands in for every tool. No real formatter, linter or language server is started, so their command lines are not verified here (gofmt, clang-format and dart format have tests with the real program in <see cref="SaveActionsTests"/>).
/// </summary>
public sealed class LanguageCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-catalog-").FullName;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        TestDir.Delete(_dir);
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
        Assert.Equal(4, LanguageCatalog.Version);
    }

    [Fact]
    public void The_tools_of_the_plan_are_data_in_the_catalog_with_a_severity_table_for_every_linter()
    {
        var formatters = LanguageCatalog.Formatters.Select(f => f.Id).ToHashSet();
        Assert.Superset(new HashSet<string> { "gofmt", "rustfmt", "clang-format", "rubocop", "prettier", "biome", "ruff-format", "shfmt", "sqlfluff-fix", "stylua", "dart-format" }, formatters);
        var linters = LanguageCatalog.Linters.Select(l => l.Id).ToHashSet();
        Assert.Superset(new HashSet<string> { "phpstan", "psalm", "golangci-lint", "clippy", "clang-tidy", "rubocop-lint", "ruff", "shellcheck", "sqlfluff", "eslint", "dart-analyze" }, linters);
        Assert.All(LanguageCatalog.Linters, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.OutputFormat), l.Id);
            Assert.NotEmpty(l.Severities);
            Assert.NotEmpty(l.Extensions); // Clippy checks the whole crate, so a linter need not take the file as an argument
        });
        Assert.All(LanguageCatalog.Formatters, f => Assert.Contains("${File}", f.Args));
        // no version is pinned; every formatter and linter was run against the real program (RealToolsTests, category RealTools), which is what Verified says
        Assert.All(LanguageCatalog.Lsp, l => Assert.Null(l.Version));
        Assert.All(LanguageCatalog.Formatters, f => Assert.True(f.Verified, f.Id));
        Assert.All(LanguageCatalog.Linters, l => Assert.True(l.Verified, l.Id));
        Assert.All(LanguageCatalog.Linters.Where(l => l.Id != "clippy"), l => Assert.Contains("${File}", l.Args)); // Clippy checks the crate
    }

    [Fact]
    public void The_consumers_read_the_catalog_and_behave_as_before()
    {
        Assert.Equal(LanguageCatalog.Formatters.Select(f => f.Id), ConfigFormatters.All.Select(f => f.Id)); // Save Actions offers every formatter of the table
        var gofmt = ConfigFormatters.Find("gofmt")!;
        Assert.Equal("gofmt", gofmt.Executable);
        Assert.Equal([".go"], gofmt.Extensions);
        Assert.Equal(["go.mod"], gofmt.ConfigFiles);
        Assert.Equal(["-w", "${File}"], gofmt.Args);
        Assert.Contains(".cs", ConfigFormatters.Find("clang-format")!.Extensions);
        Assert.Equal([".editorconfig"], ConfigFormatters.Find("shfmt")!.ConfigFiles); // shfmt reads its options from EditorConfig
        Assert.Equal(["analysis_options.yaml"], ConfigFormatters.Find("dart-format")!.ConfigFiles);
        // C#: Roslyn first (the executable of the C# extension, then the command of the .NET tool), csharp-ls as the fallback
        Assert.Equal(["Microsoft.CodeAnalysis.LanguageServer", "roslyn-language-server", "csharp-ls"], LanguageCatalog.Lsp.Where(l => l.Language == "csharp").Select(l => l.Program));
        Assert.Equal(["--stdio", "--telemetryLevel", "off"], LanguageCatalog.Lsp.First(l => l.Language == "csharp").Arguments);
    }

    [Fact]
    public void Every_language_server_of_the_table_is_one_the_plugin_starts_and_every_mode_with_a_server_has_a_mode_in_the_table()
    {
        Assert.Empty(LanguageCatalog.Lsp.Where(l => l.Language.Length == 0 || l.Program.Length == 0));
        Assert.Equal(LanguageCatalog.Lsp.Count(), LanguageCatalog.Lsp.Select(l => (l.Language, l.Program)).Distinct().Count());
        // each language of a server is the id of a mode of the table, so the plugin is told a language it has a server for
        var modes = LanguageCatalog.Groups.SelectMany(g => g.Modes).Select(m => m.Id).ToHashSet();
        Assert.All(LanguageCatalog.Lsp, l => Assert.Contains(l.Language, modes));
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
        Assert.Equal(Path.GetFullPath(installed), ToolLocator.Find(tool, start), ignoreCase: OperatingSystem.IsWindows());
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
    public void On_Windows_the_batch_file_of_a_project_wins_over_the_script_without_extension_next_to_it()
    {
        if (!OperatingSystem.IsWindows()) return; // vendor/bin and node_modules/.bin hold both files only for Windows
        string tool = Unique("fake-bat-");
        string project = Path.Combine(_dir, "project");
        foreach (var (folder, extension) in new[] { ("vendor/bin", ".bat"), ("node_modules/.bin", ".cmd") })
        {
            string bin = Directory.CreateDirectory(Path.Combine(project, folder)).FullName;
            File.WriteAllText(Path.Combine(bin, tool), "#!/usr/bin/env php\n");
            File.WriteAllText(Path.Combine(bin, tool + extension), "@echo off\r\n");
            Assert.Equal(Path.Combine(bin, tool + extension), ToolLocator.Find(tool, project), ignoreCase: true);
            File.Delete(Path.Combine(bin, tool + extension));
            Assert.Null(ToolLocator.Find(tool, project)); // a script without an extension cannot be started by Windows
            File.Delete(Path.Combine(bin, tool));
        }
    }

    [Fact]
    public void Nothing_in_the_catalog_or_the_discovery_can_reach_the_network()
    {
        var referenced = typeof(ToolLocator).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(referenced, n => n.StartsWith("System.Net.Http", StringComparison.Ordinal) || n.StartsWith("System.Net.Sockets", StringComparison.Ordinal));
    }

    // ---- the matrix: group, kind, tool and version, scenarios, systems ----

    [Fact]
    public void The_matrix_has_rows_for_all_17_groups_and_derives_every_tool_of_the_catalog()
    {
        Assert.Equal(GroupIds, LanguageCatalog.Matrix.Select(r => r.Group).Distinct());
        Assert.Equal(LanguageCatalog.Formatters.Select(f => f.Id).Order(), LanguageCatalog.Matrix.Where(r => r.Kind == ToolKind.Format).Select(r => r.Tool).Order());
        Assert.Equal(LanguageCatalog.Linters.Select(l => l.Id).Order(), LanguageCatalog.Matrix.Where(r => r.Kind == ToolKind.Lint).Select(r => r.Tool).Order());
        Assert.Equal(LanguageCatalog.Lsp.Count(), LanguageCatalog.Matrix.Count(r => r.Kind == ToolKind.Lsp));
        Assert.Equal(LanguageCatalog.Groups.Sum(g => g.Modes.Count), LanguageCatalog.Matrix.Count(r => r.Kind == ToolKind.Grammar));
        Assert.All(LanguageCatalog.Matrix, r => Assert.Equal(ToolSystem.Windows | ToolSystem.MacOS, r.Systems));
    }

    [Fact]
    public void Every_formatter_and_linter_promises_the_five_scenarios_and_each_has_a_test_for_it()
    {
        var runs = LanguageCatalog.Matrix.Where(r => r.Kind is ToolKind.Format or ToolKind.Lint).ToList();
        const ToolScenario all = ToolScenario.Success | ToolScenario.SyntaxError | ToolScenario.MissingTool | ToolScenario.Timeout | ToolScenario.NoNetwork;
        Assert.All(runs, r => Assert.Equal(all, r.Scenarios));
        // success, syntax error, missing program and timeout: a theory over the ids of these data sets, so a tool added to the catalog without a case fails here
        Assert.Equal(runs.Where(r => r.Kind == ToolKind.Format).Select(r => r.Tool).Order(), SaveActionsTests.FormatterCaseIds.Order());
        Assert.Equal(runs.Where(r => r.Kind == ToolKind.Lint).Select(r => r.Tool).Order(), LinterTests.CaseIds.Order());
        // and the same cases again with the real program (category RealTools; skipped where the program is not installed)
        Assert.Equal(runs.Where(r => r.Kind == ToolKind.Format).Select(r => r.Tool).Order(), RealToolsTests.FormatCaseIds.Order());
        Assert.Equal(runs.Where(r => r.Kind == ToolKind.Lint).Select(r => r.Tool).Order(), RealToolsTests.LintCaseIds.Order());
    }

    [Fact]
    public void No_tool_is_pinned_to_a_version_yet_and_the_rows_say_which_ones_were_run_for_real()
    {
        Assert.All(LanguageCatalog.Matrix, r => Assert.Null(r.Version)); // OPEN: the versions to pin are the owner's to choose
        Assert.All(LanguageCatalog.Matrix.Where(r => r.Kind is ToolKind.Format or ToolKind.Lint), r => Assert.True(r.Verified, r.Tool));
        Assert.All(LanguageCatalog.Matrix.Where(r => r.Kind == ToolKind.Grammar), r => Assert.Null(r.Executable));
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

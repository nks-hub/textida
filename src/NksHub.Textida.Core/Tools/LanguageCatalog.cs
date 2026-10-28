namespace NksHub.Textida.Core.Tools;

// This file has no dependency on the rest of the assembly: the language server plugin (a separate process) compiles the same file, so the editor and the plugin read one table.

/// <summary>How a linter's own severity word maps to the editor's.</summary>
public enum ToolSeverity { Error, Warning, Info, Hint }

/// <summary>One severity word of a linter's output; <c>*</c> stands for every word not listed.</summary>
public sealed record CatalogSeverity(string Tool, ToolSeverity Editor);

/// <summary>A language mode of a group: the file extensions and exact file names that select it and the grammar that colours it (null: none).</summary>
public sealed record CatalogMode(string Id, IReadOnlyList<string> Extensions, IReadOnlyList<string> FileNames, string? Grammar);

/// <summary>
/// A language server for one mode. <paramref name="Version"/> null means the version to pin is still open. <paramref name="Shipped"/> is whether the language server plugin starts it today;
/// the other entries are data that no code starts yet. <paramref name="Verified"/> is true only after the launch line was run against the real program.
/// </summary>
public sealed record CatalogLsp(string Language, string Program, IReadOnlyList<string> Arguments, string? Version = null, bool Shipped = false, bool Verified = false);

/// <summary>A formatter that rewrites <c>${File}</c> in place. Save Actions offers it for a file only where one of <paramref name="ConfigFiles"/> exists.</summary>
public sealed record CatalogFormatter(string Id, string Title, string Executable, IReadOnlyList<string> Args, IReadOnlyList<string> ConfigFiles, IReadOnlyList<string> Extensions, string? Version = null, bool Verified = false);

/// <summary>
/// A linter: <paramref name="OutputFormat"/> names the parser of its output (the parsers come with the linter display) and <paramref name="Severities"/> maps its severity words.
/// <paramref name="Environment"/> are variables set for the run; the catalog uses them to keep a linter that would fetch packages (Cargo crates, Go modules) off the network.
/// </summary>
public sealed record CatalogLinter(string Id, string Title, string Executable, IReadOnlyList<string> Args, string OutputFormat, IReadOnlyList<CatalogSeverity> Severities, IReadOnlyList<string> ConfigFiles, IReadOnlyList<string> Extensions,
    string? Version = null, bool Verified = false, IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>A configuration file of a group: <paramref name="Names"/> are the accepted names (the first one is created), <paramref name="Template"/> the minimal content of a new one, or null where no neutral content exists.</summary>
public sealed record CatalogConfig(IReadOnlyList<string> Names, string? Template);

/// <summary>The package manifest of a group and the public registry behind it.</summary>
public sealed record CatalogManifest(string File, string Registry);

public enum ToolKind { Grammar, Lsp, Format, Lint }

/// <summary>What a test of the matrix runs for a tool: a clean run, a syntax error in the file, a program that is not there, a run over the time limit and a tool the editor never lets download anything.</summary>
[Flags]
public enum ToolScenario { None = 0, Success = 1, SyntaxError = 2, MissingTool = 4, Timeout = 8, NoNetwork = 16 }

/// <summary>The operating systems of the distribution (the three runtime identifiers are two systems).</summary>
[Flags]
public enum ToolSystem { Windows = 1, MacOS = 2 }

/// <summary>One row of <see cref="LanguageCatalog.Matrix"/>. <paramref name="Verified"/> says the launch line was run against the real program; a grammar is always <c>true</c>, it has no program.</summary>
public sealed record CatalogMatrixRow(string Group, ToolKind Kind, string Tool, string? Executable, string? Version, bool Verified, ToolScenario Scenarios, ToolSystem Systems);

/// <summary>One group of languages with everything the editor needs to know about its tools.</summary>
public sealed record LanguageGroup(
    string Id,
    string Name,
    IReadOnlyList<CatalogMode> Modes,
    IReadOnlyList<CatalogLsp> Lsp,
    IReadOnlyList<CatalogFormatter> Formatters,
    IReadOnlyList<CatalogLinter> Linters,
    IReadOnlyList<CatalogConfig> Configs,
    IReadOnlyList<CatalogManifest> Manifests);

/// <summary>
/// The versioned table of language tools: one record per language group (the 17 groups of the plan). It is the single source of the file extensions and names of the language modes,
/// the grammars, the language server launch lines, the formatters of Save Actions, the linters, the configuration files shown in the status bar and the package manifests.
/// Nothing here runs a program or reaches the network; <see cref="ToolLocator"/> only looks for a program on disk.
/// OPEN: no tool version is pinned (<c>Version</c> is null everywhere). Marked verified are the formatters whose command line was run against the real program: <c>gofmt</c>, <c>clang-format</c> 19.1.5
/// (it formats code that does not parse and fails only on a broken configuration), <c>dart format</c> of Dart 3.12.2 (exit code 65 for code that does not parse) and <c>dart analyze --format=machine</c> of Dart 3.12.2
/// (pipe separated lines, exit code 3 for errors and warnings, nothing printed when clean). The other command lines and output formats come from the documentation of the tools and have not been run.
/// <see cref="Matrix"/> lists what the table promises per group, tool and system.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>Version of the table layout and content; raised when an entry changes in a way a consumer can see.</summary>
    public const int Version = 3;

    /// <summary>The configuration that applies to every file.</summary>
    public static readonly CatalogConfig EditorConfig = new([".editorconfig"], "root = true\n\n[*]\n");

    private static readonly CatalogSeverity AnyWarning = new("*", ToolSeverity.Warning);

    private static readonly string[] EslintConfigs =
        ["eslint.config.mjs", "eslint.config.js", "eslint.config.cjs", "eslint.config.ts", "eslint.config.mts", "eslint.config.cts", ".eslintrc.json", ".eslintrc.js", ".eslintrc.cjs", ".eslintrc.yml", ".eslintrc.yaml", ".eslintrc"];

    private static CatalogMode M(string id, string[] extensions, string? grammar, string[]? fileNames = null) => new(id, extensions, fileNames ?? [], grammar);
    private static CatalogConfig C(string[] names, string? template = null) => new(names, template);

    public static IReadOnlyList<LanguageGroup> Groups { get; } =
    [
        new("js-ts", "JavaScript, TypeScript",
            [M("javascript", [".js", ".mjs", ".cjs", ".jsx"], "javascript"), M("typescript", [".ts", ".tsx"], "javascript"), M("json", [".json", ".jsonc"], "json")],
            [new("javascript", "typescript-language-server", ["--stdio"]), new("typescript", "typescript-language-server", ["--stdio"])],
            [
                new("prettier", "Prettier", "prettier", ["--write", "${File}"], [".prettierrc", ".prettierrc.json", ".prettierrc.yml", ".prettierrc.yaml", ".prettierrc.toml", "prettier.config.js"], [".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".json", ".jsonc", ".css", ".html"]),
                new("biome", "Biome", "biome", ["format", "--write", "${File}"], ["biome.json", "biome.jsonc"], [".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".json", ".jsonc"]),
            ],
            [new("eslint", "ESLint", "eslint", ["--format", "json", "${File}"], "eslint-json", [new("fatal", ToolSeverity.Error), new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning)], EslintConfigs, [".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx"])],
            [C(["tsconfig.json"], "{\n  \"compilerOptions\": {}\n}\n"), C([".prettierrc"], "{}\n"), C(["biome.json"], "{}\n"),
                C(EslintConfigs, "// ESLint reports nothing but syntax errors while this list is empty.\nexport default [];\n")],
            [new("package.json", "npm")]),
        new("web", "HTML, CSS",
            [M("html", [".html", ".htm", ".xhtml"], "html"), M("css", [".css"], "css")],
            [new("html", "vscode-html-language-server", ["--stdio"]), new("css", "vscode-css-language-server", ["--stdio"])],
            [], [], [], []),
        new("sql", "SQL",
            [M("sql", [".sql"], "sql")],
            [new("sql", "sqls", [])],
            [new("sqlfluff-fix", "SQLFluff", "sqlfluff", ["fix", "--force", "${File}"], [".sqlfluff"], [".sql"])],
            [new("sqlfluff", "SQLFluff", "sqlfluff", ["lint", "--format", "json", "${File}"], "sqlfluff-json", [new("error", ToolSeverity.Error), AnyWarning], [".sqlfluff"], [".sql"])],
            [C([".sqlfluff"], "[sqlfluff]\n")],
            []),
        new("python", "Python",
            [M("python", [".py"], "python")],
            [new("python", "pyright-langserver", ["--stdio"])],
            [new("ruff-format", "Ruff", "ruff", ["format", "${File}"], ["ruff.toml", ".ruff.toml", "pyproject.toml"], [".py"])],
            [new("ruff", "Ruff", "ruff", ["check", "--output-format=json", "${File}"], "ruff-json", [new("error", ToolSeverity.Error), AnyWarning], ["ruff.toml", ".ruff.toml", "pyproject.toml"], [".py"])],
            [C(["ruff.toml", ".ruff.toml"], "# Ruff uses its defaults while this file is empty.\n"), C(["pyproject.toml"])],
            [new("requirements.txt", "PyPI")]),
        new("shell", "Bash, PowerShell",
            [M("shell", [".sh", ".bash", ".zsh"], "shell"), M("powershell", [".ps1", ".psm1", ".psd1"], "powershell"), M("batch", [".bat", ".cmd"], "batch")],
            [new("shell", "bash-language-server", ["start"])],
            [new("shfmt", "shfmt", "shfmt", ["-w", "${File}"], [".editorconfig"], [".sh", ".bash"])],
            [new("shellcheck", "ShellCheck", "shellcheck", ["--format=json", "${File}"], "shellcheck-json",
                [new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("info", ToolSeverity.Info), new("style", ToolSeverity.Hint)], [".shellcheckrc"], [".sh", ".bash"])],
            [C([".shellcheckrc"], "# ShellCheck options, one per line.\n")],
            []),
        new("java", "Java",
            [M("java", [".java"], "java")],
            [new("java", "jdtls", [])],
            [], [], [], [new("pom.xml", "Maven Central"), new("build.gradle", "Maven Central")]),
        new("csharp", "C#",
            [M("csharp", [".cs"], "csharp"), M("vb", [".vb", ".vbs", ".bas"], "vb")],
            [new("csharp", "Microsoft.CodeAnalysis.LanguageServer", ["--stdio", "--telemetryLevel", "off"], Shipped: true), new("csharp", "csharp-ls", [], Shipped: true)],
            [], [], [],
            [new("*.csproj", "NuGet")]),
        new("c-cpp", "C, C++",
            [M("c", [".c"], "c"), M("cpp", [".h", ".cpp", ".hpp", ".cc", ".cxx", ".hh", ".hxx"], "cpp"), M("cmake", [".cmake"], "cmake", ["CMakeLists.txt"]),
                M("makefile", [".mk", ".mak", ".make"], "makefile", ["Makefile", "makefile", "GNUmakefile"]), M("meson", [], "meson", ["meson.build", "meson_options.txt", "meson.options"])],
            [new("c", "clangd", []), new("cpp", "clangd", [])],
            [new("clang-format", "clang-format", "clang-format", ["-i", "${File}"], [".clang-format", "_clang-format"], [".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hh", ".hxx", ".m", ".mm", ".cs", ".java"], Verified: true)],
            [new("clang-tidy", "clang-tidy", "clang-tidy", ["${File}"], "gcc-text",
                [new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("note", ToolSeverity.Info)], [".clang-tidy"], [".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hh", ".hxx"])],
            [C([".clang-format", "_clang-format"], "BasedOnStyle: LLVM\n"), C([".clang-tidy"], "Checks: 'clang-diagnostic-*'\n")],
            []),
        new("php", "PHP",
            [M("php", [".php"], "php"), M("latte", [".latte"], "latte"), M("neon", [".neon"], "neon"), M("blade", [".blade.php"], "blade"), M("twig", [".twig"], "twig")],
            [new("php", "phpactor", ["language-server"])],
            [],
            [
                new("phpstan", "PHPStan", "phpstan", ["analyse", "--error-format=json", "--no-progress", "--no-interaction", "${File}"], "phpstan-json", [new("*", ToolSeverity.Error)], ["phpstan.neon", "phpstan.neon.dist", "phpstan.dist.neon"], [".php"]),
                new("psalm", "Psalm", "psalm", ["--output-format=json", "--no-progress", "${File}"], "psalm-json", [new("error", ToolSeverity.Error), new("info", ToolSeverity.Info)], ["psalm.xml", "psalm.xml.dist"], [".php"]),
            ],
            [C(["phpstan.neon", "phpstan.neon.dist", "phpstan.dist.neon"]), C(["psalm.xml", "psalm.xml.dist"])],
            [new("composer.json", "Packagist")]),
        new("go", "Go",
            [M("go", [".go"], "go"), M("gomod", [], "gomod", ["go.mod", "go.work"]), M("gotmpl", [".gohtml", ".gotmpl"], "gotmpl")],
            [new("go", "gopls", [])],
            [new("gofmt", "gofmt", "gofmt", ["-w", "${File}"], ["go.mod"], [".go"], Verified: true)],
            [new("golangci-lint", "golangci-lint", "golangci-lint", ["run", "--output.json.path=stdout", "${File}"], "golangci-json",
                [new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("info", ToolSeverity.Info), AnyWarning], [".golangci.yml", ".golangci.yaml", ".golangci.toml", ".golangci.json"], [".go"],
                Environment: new Dictionary<string, string> { ["GOPROXY"] = "off" })],
            [C([".golangci.yml", ".golangci.yaml", ".golangci.toml", ".golangci.json"], "version: \"2\"\n")],
            [new("go.mod", "Go module proxy")]),
        new("rust", "Rust",
            [M("rust", [".rs"], "rust"), M("toml", [".toml"], "toml", ["Cargo.lock", "Pipfile", "poetry.lock", "uv.lock"])],
            [new("rust", "rust-analyzer", [])],
            [new("rustfmt", "rustfmt", "rustfmt", ["${File}"], ["rustfmt.toml", ".rustfmt.toml"], [".rs"])],
            [new("clippy", "Clippy", "cargo", ["clippy", "--message-format=json", "--quiet"], "cargo-json",
                [new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("note", ToolSeverity.Info), new("help", ToolSeverity.Hint), AnyWarning], ["clippy.toml", ".clippy.toml"], [".rs"],
                Environment: new Dictionary<string, string> { ["CARGO_NET_OFFLINE"] = "true" })],
            [C(["rustfmt.toml", ".rustfmt.toml"], "# rustfmt uses its defaults while this file is empty.\n"), C(["clippy.toml", ".clippy.toml"], "# Clippy uses its defaults while this file is empty.\n")],
            [new("Cargo.toml", "crates.io")]),
        new("kotlin", "Kotlin",
            [M("kotlin", [".kt", ".kts"], "kotlin")],
            [new("kotlin", "kotlin-lsp", ["--stdio"])],
            [], [], [], [new("build.gradle.kts", "Maven Central")]),
        new("lua", "Lua",
            [M("lua", [".lua", ".rockspec"], "lua")],
            [new("lua", "lua-language-server", [])],
            [new("stylua", "StyLua", "stylua", ["${File}"], ["stylua.toml", ".stylua.toml"], [".lua"])],
            [],
            [C(["stylua.toml", ".stylua.toml"], "# StyLua uses its defaults while this file is empty.\n"), C([".luarc.json"], "{}\n")],
            []),
        new("swift", "Swift",
            [M("swift", [".swift"], "swift")],
            [new("swift", "sourcekit-lsp", [])],
            [], [], [], []),
        new("ruby", "Ruby",
            [M("ruby", [".rb", ".rake", ".gemspec", ".ru", ".rbw", ".thor", ".jbuilder", ".podspec"], "ruby",
                    ["Gemfile", "Rakefile", "Guardfile", "Capfile", "Vagrantfile", "Podfile", "Brewfile", "Berksfile", "Fastfile", "Appfile"]),
                M("erb", [".erb", ".rhtml"], "erb"), M("haml", [".haml"], "haml"), M("slim", [".slim"], "slim"), M("rbs", [".rbs"], "rbs")],
            [new("ruby", "ruby-lsp", [])],
            [new("rubocop", "RuboCop", "rubocop", ["-a", "--format", "quiet", "${File}"], [".rubocop.yml"], [".rb", ".rake"])],
            [new("rubocop-lint", "RuboCop", "rubocop", ["--format", "json", "${File}"], "rubocop-json",
                [new("fatal", ToolSeverity.Error), new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("convention", ToolSeverity.Info), new("refactor", ToolSeverity.Info), new("info", ToolSeverity.Info)], [".rubocop.yml"], [".rb", ".rake"])],
            [C([".rubocop.yml"], "AllCops:\n  NewCops: disable\n")],
            [new("Gemfile", "RubyGems")]),
        new("dart", "Dart",
            [M("dart", [".dart"], "dart")],
            [new("dart", "dart", ["language-server"])],
            [new("dart-format", "dart format", "dart", ["format", "${File}"], ["analysis_options.yaml"], [".dart"], Verified: true)],
            [new("dart-analyze", "dart analyze", "dart", ["analyze", "--format=machine", "${File}"], "dart-machine",
                [new("error", ToolSeverity.Error), new("warning", ToolSeverity.Warning), new("info", ToolSeverity.Info)], ["analysis_options.yaml"], [".dart"], Verified: true)],
            [C(["analysis_options.yaml"])],
            [new("pubspec.yaml", "pub.dev")]),
        new("r", "R",
            [M("r", [".r"], "r", [".Rprofile"])],
            [], [], [], [], []),
    ];

    /// <summary>
    /// The versioned table group → kind → tool and version → scenarios → systems, one row per tool of <see cref="Groups"/> (and one per mode for the grammar). Rows are derived from the groups, so the table cannot drift from the data.
    /// A formatter or linter promises a run with success, a syntax error, a missing program, a time-out and no network; a language server promises only that it is found and never downloaded (its runs are tested with a fake server where <see cref="CatalogLsp.Shipped"/>).
    /// OPEN: <c>Version</c> is null while no version is pinned.
    /// </summary>
    public static IReadOnlyList<CatalogMatrixRow> Matrix { get; } =
    [
        .. Groups.SelectMany(g => g.Modes.Select(m => new CatalogMatrixRow(g.Id, ToolKind.Grammar, m.Id, null, null, true, ToolScenario.None, AllSystems))
            .Concat(g.Lsp.Select(l => new CatalogMatrixRow(g.Id, ToolKind.Lsp, l.Language, l.Program, l.Version, l.Verified, ToolScenario.NoNetwork, AllSystems)))
            .Concat(g.Formatters.Select(f => new CatalogMatrixRow(g.Id, ToolKind.Format, f.Id, f.Executable, f.Version, f.Verified, RunScenarios, AllSystems)))
            .Concat(g.Linters.Select(l => new CatalogMatrixRow(g.Id, ToolKind.Lint, l.Id, l.Executable, l.Version, l.Verified, RunScenarios, AllSystems)))),
    ];

    private const ToolSystem AllSystems = ToolSystem.Windows | ToolSystem.MacOS;
    private const ToolScenario RunScenarios = ToolScenario.Success | ToolScenario.SyntaxError | ToolScenario.MissingTool | ToolScenario.Timeout | ToolScenario.NoNetwork;

    public static IEnumerable<CatalogLsp> Lsp => Groups.SelectMany(g => g.Lsp);
    public static IEnumerable<CatalogFormatter> Formatters => Groups.SelectMany(g => g.Formatters);
    public static IEnumerable<CatalogLinter> Linters => Groups.SelectMany(g => g.Linters);

    /// <summary>The mode with this id, or null when no group owns it (plain text, XML, Markdown and other modes outside the 17 groups).</summary>
    public static CatalogMode? Mode(string id) => Groups.SelectMany(g => g.Modes).FirstOrDefault(m => m.Id == id);

    /// <summary>The group of a file: its exact name first, then its extension.</summary>
    public static LanguageGroup? GroupOfPath(string path)
    {
        string name = Path.GetFileName(path), ext = Path.GetExtension(path);
        return Groups.FirstOrDefault(g => g.Modes.Any(m => m.FileNames.Contains(name, StringComparer.OrdinalIgnoreCase)))
            ?? (ext.Length == 0 ? null : Groups.FirstOrDefault(g => g.Modes.Any(m => m.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))));
    }

    /// <summary>
    /// The configuration files that can apply to <paramref name="path"/>: <c>.editorconfig</c>, those of its group and those of the formatters and linters of another group that handle its extension
    /// (clang-format formats C# and Java too). Only names; whether a file exists is up to the caller.
    /// </summary>
    public static IReadOnlyList<CatalogConfig> ConfigsFor(string path)
    {
        string ext = Path.GetExtension(path);
        var own = GroupOfPath(path);
        var result = new List<CatalogConfig> { EditorConfig };
        if (own is not null) result.AddRange(own.Configs);
        foreach (var group in Groups.Where(g => g != own))
        {
            var names = group.Formatters.Where(f => f.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)).SelectMany(f => f.ConfigFiles)
                .Concat(group.Linters.Where(l => l.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)).SelectMany(l => l.ConfigFiles)).ToHashSet();
            result.AddRange(group.Configs.Where(c => c.Names.Any(names.Contains)));
        }
        return [.. result.DistinctBy(c => c.Names[0])];
    }
}

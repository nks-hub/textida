using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Document.Contract;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

[CollectionDefinition("RealTools", DisableParallelization = true)]
public sealed class RealToolsCollection;

/// <summary>A test that needs real programs; it is skipped, with the names, where one of them is not found in PATH.</summary>
public sealed class RealToolFactAttribute : FactAttribute
{
    public RealToolFactAttribute(params string[] programs)
    {
        var missing = programs.Where(p => ToolLocator.Find(p) is null).ToList();
        if (missing.Count > 0) Skip = string.Join(", ", missing) + " is not installed here (not found in PATH).";
    }
}

/// <summary>Skipped when none of the programs is found in PATH.</summary>
public sealed class RealToolAnyFactAttribute : FactAttribute
{
    public RealToolAnyFactAttribute(params string[] programs)
    {
        if (programs.All(p => ToolLocator.Find(p) is null)) Skip = "None of " + string.Join(", ", programs) + " is installed here (not found in PATH).";
    }
}

/// <summary>
/// P8.5.3, P8.3.1 and P8.8.1 against the real programs: every formatter and linter of <see cref="LanguageCatalog"/> goes through success, unchanged or clean, a syntax error, a missing program, a time-out and a run
/// with a proxy that records every connection (no connection means the tool and the editor downloaded nothing). The programs come from PATH; a test is skipped where its program is not installed.
/// Run with <c>--filter Category=RealTools</c>; the quick suite leaves them out. The tests of one tool report each scenario in the test output and fail at the end with all that failed.
/// </summary>
[Collection("RealTools")]
[Trait("Category", "RealTools")]
public sealed class RealToolsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-real-").FullName;
    private readonly List<string> _failed = [];

    public void Dispose() => TestDir.Delete(_dir);

    // ---- helpers ----

    private static string Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string Text(Doc d) => Encoding.UTF8.GetString(d.ReadBytes(new ByteRange(0, d.ByteLength)));

    private async Task Scenario(string tool, string name, Func<Task> body)
    {
        try
        {
            await body();
            output.WriteLine($"PASS {tool} / {name}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            output.WriteLine($"FAIL {tool} / {name}: {e.Message}");
            _failed.Add($"{tool} / {name}: {e.Message}");
        }
    }

    private void Verdict() => Assert.True(_failed.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, _failed));

    /// <summary>Runs <paramref name="body"/> with a PATH that holds no program, the way the editor sees a tool that is not installed.</summary>
    private static async Task<T> WithoutTools<T>(Func<Task<T>> body)
    {
        string? old = Environment.GetEnvironmentVariable("PATH");
        string empty = Directory.CreateTempSubdirectory("textida-nopath-").FullName;
        Environment.SetEnvironmentVariable("PATH", empty);
        try { return await body(); }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", old);
            TestDir.Delete(empty);
        }
    }

    /// <summary>
    /// A listener that every proxy variable points to: a program that tries to fetch something (a package, a module, a crate) connects to it, and the connection is counted.
    /// The variables are process wide, so the tests of this class do not run in parallel with any other.
    /// </summary>
    private sealed class NetCanary : IDisposable
    {
        private static readonly string[] Names = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy"];
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, string?> _saved = [];
        private int _connections;

        public int Connections => Volatile.Read(ref _connections);

        public NetCanary()
        {
            _listener.Start();
            string proxy = "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        Interlocked.Increment(ref _connections);
                    }
                }
                catch (Exception e) when (e is ObjectDisposedException or SocketException or InvalidOperationException) { /* stopped */ }
            });
            foreach (var name in Names)
            {
                _saved[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, name.StartsWith("NO_", StringComparison.OrdinalIgnoreCase) ? "" : proxy);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _saved) Environment.SetEnvironmentVariable(name, value);
            _listener.Stop();
        }
    }

    /// <summary>The first line the program prints for its version, for the test output and the table of tool versions.</summary>
    private async Task<string> VersionOf(string[] command)
    {
        try
        {
            var lines = new List<string>();
            var tool = ToolRunner.Resolve(new ToolSpec("version", command[0], command[1..], null, TimeSpan.FromSeconds(60)), _ => null, _dir);
            await ToolRunner.RunAsync(tool, lines.Add);
            return string.Join(" | ", lines.Where(l => l.Trim().Length > 0).Take(2).Select(l => l.Trim()));
        }
        catch (ToolException e) { return "unknown: " + e.Message; }
    }

    // ---- formatters ----

    /// <param name="Version">The command line that prints the version.</param>
    /// <param name="Syntax">The file that is overwritten to make the run fail and what it gets: the file itself with code the tool cannot read, or the configuration where the tool formats any code (clang-format).</param>
    private sealed record FormatCase(string Id, string[] Version, (string Path, string Text)[] Project, string File, string Text, string Formatted, (string Path, string Text) Syntax, int Code, string Message);

    private static readonly FormatCase[] FormatCases =
    [
        new("gofmt", ["go", "version"], [("go.mod", "module x\n")], "main.go", "package main\n\nfunc  main( ){}\n", "package main\n\nfunc main() {}\n",
            ("main.go", "package main\n\nfunc  main( ){}\nx\n"), 2, "expected declaration"),
        new("rustfmt", ["rustfmt", "--version"], [("rustfmt.toml", "")], "src/lib.rs", "pub fn f( )->i32{1}\n", "pub fn f() -> i32 {\n    1\n}\n",
            ("src/lib.rs", "pub fn f( )->i32{ 1 \nx\n"), 1, "unclosed delimiter"),
        new("clang-format", ["clang-format", "--version"], [(".clang-format", "BasedOnStyle: LLVM\n")], "src/a.cpp", "int  main( ){return 0;}\n", "int main() { return 0; }\n",
            (".clang-format", "BasedOnStyle: NoSuchStyle\n"), 1, "BasedOnStyle"),
        new("rubocop", ["rubocop", "--version"], [(".rubocop.yml", "AllCops:\n  NewCops: disable\nLayout/EndOfLine:\n  Enabled: false\n")], "lib/a.rb", "puts  1\n", "puts 1\n",
            ("lib/a.rb", "puts (\n"), 1, "Lint/Syntax"),
        new("prettier", ["prettier", "--version"], [(".prettierrc", "{}\n")], "src/a.js", "let  x=1\n", "let x = 1;\n", ("src/a.js", "let x = ;\n"), 2, ""),
        new("biome", ["biome", "--version"], [("biome.json", "{}\n")], "src/a.ts", "let  x=1\n", "let x = 1;\n", ("src/a.ts", "let x = ;\n"), 1, ""),
        new("sqlfluff-fix", ["sqlfluff", "--version"], [(".sqlfluff", "[sqlfluff]\ndialect = ansi\n")], "q.sql", "select  1\n", "select 1\n", ("q.sql", "selec t t t ( 1\n"), 1, ""),
        new("ruff-format", ["ruff", "--version"], [("ruff.toml", "")], "pkg/a.py", "x =  1\n", "x = 1\n", ("pkg/a.py", "x = (\n"), 2, "Failed to parse"),
        new("shfmt", ["shfmt", "--version"], [(".editorconfig", "root = true\n")], "run.sh", "echo  hi\n", "echo hi\n", ("run.sh", "echo \"hi\n"), 1, "reached EOF"),
        new("stylua", ["stylua", "--version"], [("stylua.toml", "")], "a.lua", "local  x=1\n", "local x = 1\n", ("a.lua", "local x = = 1\n"), 2, ""),
        new("dart-format", ["dart", "--version"], [("analysis_options.yaml", "formatter:\n  page_width: 80\n")], "lib/a.dart", "void main( ){print(1);}\n", "void main() {\n  print(1);\n}\n",
            ("lib/a.dart", "void main( { print(1);\n"), 65, ""),
    ];

    public static IReadOnlyList<string> FormatCaseIds => [.. FormatCases.Select(c => c.Id)];

    private async Task Formatter(string id)
    {
        var c = FormatCases.First(f => f.Id == id);
        var def = ConfigFormatters.Find(id)!;
        string project = Path.Combine(_dir, id);
        foreach (var (path, text) in c.Project) Write(project, path, text);
        string file = Write(project, c.File, c.Text);
        string copyFilter = "*.textida-fmt-*";
        int CopiesLeft() => Directory.GetFiles(project, copyFilter, SearchOption.AllDirectories).Length;
        using var canary = new NetCanary();
        output.WriteLine($"{id}: {await VersionOf(c.Version)}");

        await Scenario(id, "success", async () =>
        {
            Assert.Contains(id, ConfigFormatters.Offered(file).Select(f => f.Id));
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(120));
            Assert.True(outcome.Result == FormatResult.Formatted, $"{outcome.Result}: {outcome.Reason}");
            // RuboCop writes the corrected file in text mode, so on Windows every line of it ends with CR LF afterwards, whatever the file had
            Assert.Equal(c.Formatted, id == "rubocop" && OperatingSystem.IsWindows() ? Text(doc).ReplaceLineEndings("\n") : Text(doc));
            Assert.NotNull(doc.Undo()); // one step
            Assert.Equal(c.Text, Text(doc));
            Assert.Equal(0, CopiesLeft());
        });

        await Scenario(id, "already formatted", async () =>
        {
            string formatted = Write(project, "again/" + Path.GetFileName(c.File), c.Formatted);
            foreach (var (path, text) in c.Project) Write(Path.Combine(project, "again"), path, text);
            using var doc = Doc.Open(formatted, ReferenceBackendFactory.Instance);
            var outcome = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(120));
            Assert.True(outcome.Result == FormatResult.Unchanged, $"{outcome.Result}: {outcome.Reason}");
            Assert.Equal(c.Formatted, Text(doc));
        });

        await Scenario(id, "syntax error", async () =>
        {
            string broken = Path.Combine(project, "broken");
            foreach (var (path, text) in c.Project) Write(broken, path, text);
            string target = Write(broken, c.File, c.Text);
            Write(broken, c.Syntax.Path, c.Syntax.Text);
            using var doc = Doc.Open(target, ReferenceBackendFactory.Instance);
            string before = Text(doc);
            var outcome = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(120));
            Assert.True(outcome.Result == FormatResult.Failed, $"{outcome.Result}: {outcome.Reason}");
            Assert.Contains($"code {c.Code}", outcome.Reason);
            Assert.Contains(c.Message, outcome.Reason);
            Assert.Equal(before, Text(doc));
            Assert.Equal(0, Directory.GetFiles(broken, copyFilter, SearchOption.AllDirectories).Length);
        });

        await Scenario(id, "missing tool", async () =>
        {
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await WithoutTools(() => ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(60)));
            Assert.True(outcome.Result == FormatResult.Missing, $"{outcome.Result}: {outcome.Reason}");
            Assert.Contains("was not found", outcome.Reason);
            Assert.Equal(c.Text, Text(doc));
        });

        await Scenario(id, "timeout", async () =>
        {
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var watch = Stopwatch.StartNew();
            // a limit of zero ends the run the moment the program has started: a program that is done in a millisecond (gofmt, stylua) cannot beat it, and the program was started for real
            var outcome = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.Zero);
            Assert.True(outcome.Result == FormatResult.TimedOut, $"{outcome.Result}: {outcome.Reason}");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
            Assert.Equal(c.Text, Text(doc));
            Assert.Equal(0, CopiesLeft());
        });

        await Scenario(id, "no network", () =>
        {
            Assert.Equal(0, canary.Connections);
            return Task.CompletedTask;
        });
        Verdict();
    }

    [RealToolFact("go", "gofmt")] public Task Formatter_gofmt() => Formatter("gofmt");
    [RealToolFact("rustfmt")] public Task Formatter_rustfmt() => Formatter("rustfmt");
    [RealToolFact("clang-format")] public Task Formatter_clang_format() => Formatter("clang-format");
    [RealToolFact("rubocop")] public Task Formatter_rubocop() => Formatter("rubocop");
    [RealToolFact("prettier")] public Task Formatter_prettier() => Formatter("prettier");
    [RealToolFact("biome")] public Task Formatter_biome() => Formatter("biome");
    [RealToolFact("sqlfluff")] public Task Formatter_sqlfluff_fix() => Formatter("sqlfluff-fix");
    [RealToolFact("ruff")] public Task Formatter_ruff_format() => Formatter("ruff-format");
    [RealToolFact("shfmt")] public Task Formatter_shfmt() => Formatter("shfmt");
    [RealToolFact("stylua")] public Task Formatter_stylua() => Formatter("stylua");
    [RealToolFact("dart")] public Task Formatter_dart_format() => Formatter("dart-format");

    // ---- linters ----

    /// <param name="Pick">The code of the finding to look at when the tool reports more than one for the file; null: there is exactly one.</param>
    /// <param name="Underlined">The text the finding covers; null: not checked (the tool gives a position and no length).</param>
    /// <param name="SyntaxLine">The line the error of the broken file is on; null: any line.</param>
    private sealed record LintCase(string Id, string[] Version, (string Path, string Text)[] Project, string File, string Bad, string? Underlined, int Line, int Column, ToolSeverity Severity, string Code, string? Pick,
        string Good, string Syntax, int? SyntaxLine);

    private const string RuboCopConfig = "AllCops:\n  NewCops: disable\nLayout/EndOfLine:\n  Enabled: false\n";

    private static readonly LintCase[] LintCases =
    [
        new("phpstan", ["phpstan", "--version"], [("composer.json", "{\"name\":\"x/y\"}\n"), ("phpstan.neon", "parameters:\n  level: 5\n")], "src/a.php", "<?php\n\nfoo($x);\n", "foo($x);", 3, 1, ToolSeverity.Error,
            "function.notFound", "function.notFound", "<?php\n\n$a = strlen(\"a\");\n", "<?php\n\nfunction (\n", 4),
        new("psalm", ["psalm", "--version"], [("composer.json", "{\"name\":\"x/y\"}\n"), ("vendor/autoload.php", "<?php\n"),
                ("psalm.xml", "<?xml version=\"1.0\"?>\n<psalm errorLevel=\"1\" resolveFromConfigFile=\"true\" xmlns=\"https://getpsalm.org/schema/config\"><projectFiles><directory name=\"src\"/></projectFiles></psalm>\n")],
            "src/a.php", "<?php\nfunction f(): int {\n    return $b;\n}\n", "$b", 3, 12, ToolSeverity.Error, "UndefinedVariable", "UndefinedVariable", "<?php\nfunction f(): int {\n    return 1;\n}\n", "<?php\nfunction (\n", null),
        new("golangci-lint", ["golangci-lint", "--version"], [("go.mod", "module x\n\ngo 1.21\n"), (".golangci.yml", "version: \"2\"\n")], "main.go",
            "package main\n\nimport \"os\"\n\nfunc main() {\n\tos.Remove(\"x\")\n}\n", null, 6, 11, ToolSeverity.Warning, "errcheck", null,
            "package main\n\nimport \"os\"\n\nfunc main() {\n\t_ = os.Remove(\"x\")\n}\n", "package main\n\nfunc main() {\n\tx\n}\n", 4),
        new("clippy", ["cargo", "clippy", "--version"], [("Cargo.toml", "[package]\nname = \"demo\"\nversion = \"0.1.0\"\nedition = \"2021\"\n")], "src/lib.rs",
            "pub fn f() -> i32 {\n    return 1;\n}\n", "return 1", 2, 5, ToolSeverity.Warning, "clippy::needless_return", null, "pub fn f() -> i32 {\n    1\n}\n", "pub fn f() -> i32 {\n    return 1\n    x\n}\n", 2),
        new("clang-tidy", ["clang-tidy", "--version"], [(".clang-tidy", "Checks: \"readability-braces-around-statements,clang-diagnostic-*\"\n")], "src/a.cpp",
            "void f(int a) {\n  if (a) return;\n}\n", null, 2, 9, ToolSeverity.Warning, "readability-braces-around-statements", null, "void f(int a) {\n  if (a) {\n    return;\n  }\n}\n", "int main( { return 0 }\n", 1),
        new("rubocop-lint", ["rubocop", "--version"], [(".rubocop.yml", RuboCopConfig)], "lib/l.rb", "# frozen_string_literal: true\n\nputs 1 \n", " ", 3, 7, ToolSeverity.Info, "Layout/TrailingWhitespace", null,
            "# frozen_string_literal: true\n\nputs 1\n", "x = (\n", 1),
        new("eslint", ["eslint", "--version"], [("package.json", "{\"name\":\"x\"}\n"), ("eslint.config.mjs", "export default [{ rules: { \"no-undef\": \"error\" } }];\n")], "src/a.js",
            "foo(1);\n", "foo", 1, 1, ToolSeverity.Error, "no-undef", null, "function foo() {}\nfoo(1);\n", "foo(;\n", 1),
        new("ruff", ["ruff", "--version"], [("ruff.toml", "[lint]\nselect = [\"F\"]\n")], "a.py", "import os\nprint(1)\n", "os", 1, 8, ToolSeverity.Warning, "F401", null, "print(1)\n", "x = (\n", 2),
        new("shellcheck", ["shellcheck", "--version"], [(".shellcheckrc", "")], "a.sh", "#!/bin/sh\nx=$1\necho $x\n", "$x", 3, 6, ToolSeverity.Info, "SC2086", null, "#!/bin/sh\nx=$1\necho \"$x\"\n",
            "#!/bin/sh\nif true; then\n", null),
        new("sqlfluff", ["sqlfluff", "--version"], [(".sqlfluff", "[sqlfluff]\ndialect = ansi\n")], "q.sql", "SELECT a  FROM t\n", "  ", 1, 9, ToolSeverity.Warning, "LT01", null, "SELECT a FROM t\n", "selec t t t ( 1\n", 1),
        new("dart-analyze", ["dart", "--version"], [("pubspec.yaml", "name: demo\nenvironment:\n  sdk: \">=3.0.0 <4.0.0\"\n")], "lib/a.dart", "int f() {\n  return y;\n}\n", "y", 2, 10, ToolSeverity.Error,
            "undefined_identifier", null, "int f() => 1;\n", "int f() {\n  return ;\n  var = ;\n", null),
    ];

    public static IReadOnlyList<string> LintCaseIds => [.. LintCases.Select(c => c.Id)];

    private static string Underlined(Doc d, LintDiagnostic f) => Encoding.UTF8.GetString(d.ReadBytes(f.Range));

    private static string Describe(LintOutcome o) => string.Join("; ", o.Findings.Select(f => $"{f.Line}:{f.Column} {f.Severity} {f.Code} '{f.Message}'")) + (o.Dropped > 0 ? $" ({o.Dropped} dropped)" : "");

    private static Task<LintOutcome> Lint(Doc doc, CatalogLinter linter, string file, TimeSpan timeout) => Linters.RunAsync(doc, linter, Linters.Prepare(linter, file, timeout));

    private async Task Linter(string id)
    {
        var c = LintCases.First(l => l.Id == id);
        var linter = Linters.Find(id)!;
        string project = Path.Combine(_dir, id);
        foreach (var (path, text) in c.Project) Write(project, path, text);
        string file = Write(project, c.File, c.Bad);
        using var canary = new NetCanary();
        output.WriteLine($"{id}: {await VersionOf(c.Version)}");

        await Scenario(id, "finding on the right line and column", async () =>
        {
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(180));
            Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
            IReadOnlyList<LintDiagnostic> matching = c.Pick is null ? outcome.Findings : [.. outcome.Findings.Where(f => f.Code == c.Pick)];
            Assert.True(matching.Count == 1, $"expected one finding{(c.Pick is null ? "" : " " + c.Pick)}, got {outcome.Findings.Count}: {Describe(outcome)}");
            var finding = matching[0];
            if (c.Underlined is not null) Assert.Equal(c.Underlined, Underlined(doc, finding));
            Assert.Equal((c.Line, c.Column, c.Severity, c.Code), (finding.Line, finding.Column, finding.Severity, finding.Code));
            Assert.Equal(doc.Revision, outcome.Revision);
            Assert.Equal(0, outcome.Dropped);
        });

        await Scenario(id, "gone after the fix", async () =>
        {
            Write(project, c.File, c.Good);
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(180));
            Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
            Assert.True(outcome.Findings.Count == 0, string.Join("; ", outcome.Findings.Select(f => $"{f.Line}:{f.Column} {f.Code} {f.Message}")));
        });

        await Scenario(id, "syntax error", async () =>
        {
            Write(project, c.File, c.Syntax);
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(180));
            Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
            var errors = outcome.Findings.Where(f => f.Severity == ToolSeverity.Error && (c.SyntaxLine is null || f.Line == c.SyntaxLine)).ToList();
            Assert.True(errors.Count > 0, "no error finding" + (c.SyntaxLine is null ? "" : $" on line {c.SyntaxLine}") + ": " + string.Join("; ", outcome.Findings.Select(f => $"{f.Line}:{f.Column} {f.Severity} {f.Code} {f.Message}")));
        });

        await Scenario(id, "missing tool", async () =>
        {
            Write(project, c.File, c.Bad);
            var e = await WithoutTools(() => Task.FromResult(Assert.Throws<ToolException>(() => Linters.Prepare(linter, file, TimeSpan.FromSeconds(60)))));
            Assert.True(e.Missing);
            Assert.Contains("was not found", e.Message);
        });

        await Scenario(id, "timeout", async () =>
        {
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var watch = Stopwatch.StartNew();
            var outcome = await Lint(doc, linter, file, TimeSpan.Zero); // see the formatter
            Assert.True(outcome.State == LintState.TimedOut, $"{outcome.State}: {outcome.Reason}");
            Assert.Contains("did not finish", outcome.Reason);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed}");
        });

        await Scenario(id, "version inside the window of the catalog", async () =>
        {
            var check = await LinterVersions.CheckAsync(linter, Linters.Prepare(linter, file, TimeSpan.FromSeconds(60)), TimeSpan.FromSeconds(60));
            output.WriteLine($"{id}: version {check.Found} state {check.State} window {linter.Supported?.Min}..{linter.Supported?.Below}");
            Assert.True(check.State is LinterVersionState.Supported or LinterVersionState.NotChecked, $"{check.State} {check.Found} {check.Reason}");
        });

        await Scenario(id, "no network", () =>
        {
            Assert.Equal(0, canary.Connections);
            return Task.CompletedTask;
        });
        Verdict();
    }

    [RealToolFact("phpstan", "php")] public Task Linter_phpstan() => Linter("phpstan");
    [RealToolFact("psalm", "php")] public Task Linter_psalm() => Linter("psalm");
    [RealToolFact("golangci-lint", "go")] public Task Linter_golangci_lint() => Linter("golangci-lint");
    [RealToolFact("cargo")] public Task Linter_clippy() => Linter("clippy");
    [RealToolFact("clang-tidy")] public Task Linter_clang_tidy() => Linter("clang-tidy");
    [RealToolFact("rubocop")] public Task Linter_rubocop() => Linter("rubocop-lint");
    [RealToolFact("eslint", "node")] public Task Linter_eslint() => Linter("eslint");
    [RealToolFact("ruff")] public Task Linter_ruff() => Linter("ruff");
    [RealToolFact("shellcheck")] public Task Linter_shellcheck() => Linter("shellcheck");
    [RealToolFact("sqlfluff")] public Task Linter_sqlfluff() => Linter("sqlfluff");
    [RealToolFact("dart")] public Task Linter_dart_analyze() => Linter("dart-analyze");

    // ---- the files the editor creates, and a tool that never comes back ----

    [RealToolFact("sqlfluff")]
    public async Task The_sqlfluff_configuration_the_editor_creates_is_one_sqlfluff_can_use()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        string file = Write(_dir, "q.sql", "SELECT a  FROM t\n");
        var config = ConfigFiles.For(file).First(h => h.Config.Names[0] == ".sqlfluff").Config;
        ConfigFiles.Create(config, file);

        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        var linter = Linters.Find("sqlfluff")!;
        var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(120));
        Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
        Assert.Contains(outcome.Findings, f => f.Code == "LT01");

        var format = await ConfigFormatters.FormatAsync(doc, ConfigFormatters.Find("sqlfluff-fix")!, TimeSpan.FromSeconds(120));
        Assert.True(format.Result == FormatResult.Formatted, $"{format.Result}: {format.Reason}");
        Assert.Equal("SELECT a FROM t\n", Text(doc));
    }

    [RealToolFact("clang-tidy")]
    public async Task The_clang_tidy_configuration_the_editor_creates_is_one_clang_tidy_accepts()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".git"));
        string file = Write(_dir, "src/a.cpp", "int main() { return 0; }\n");
        ConfigFiles.Create(ConfigFiles.For(file).First(h => h.Config.Names[0] == ".clang-tidy").Config, file);
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        var outcome = await Lint(doc, Linters.Find("clang-tidy")!, file, TimeSpan.FromSeconds(120));
        Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
        Assert.Empty(outcome.Findings);
    }

    /// <summary>
    /// A tool that really runs until it is stopped (its own configuration loops forever and writes a heartbeat): the time limit ends it and everything it started, behind a batch file or a launcher script too.
    /// </summary>
    private async Task Hang(string id, (string Path, string Text)[] project, string file, string content, string heartbeat)
    {
        string root = Path.Combine(_dir, "hang-" + id);
        foreach (var (path, text) in project) Write(root, path, text.Replace("{HEARTBEAT}", heartbeat.Replace("\\", "/", StringComparison.Ordinal)));
        string target = Write(root, file, content);
        var linter = Linters.Find(id)!;
        using var doc = Doc.Open(target, ReferenceBackendFactory.Instance);
        var watch = Stopwatch.StartNew();
        var outcome = await Lint(doc, linter, target, TimeSpan.FromSeconds(6));

        Assert.True(outcome.State == LintState.TimedOut, $"{outcome.State}: {outcome.Reason}");
        await AssertStopped(heartbeat, watch);
    }

    private static async Task AssertStopped(string heartbeat, Stopwatch watch)
    {
        Assert.True(File.Exists(heartbeat) && new FileInfo(heartbeat).Length > 0, "the tool never started its loop");
        long size = new FileInfo(heartbeat).Length;
        await Task.Delay(2000);
        Assert.Equal(size, new FileInfo(heartbeat).Length); // nothing is writing any more: the whole tree is gone
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(40), $"took {watch.Elapsed}");
    }

    [RealToolFact("prettier", "node")]
    public async Task A_hanging_Prettier_is_stopped_with_the_node_process_behind_its_launcher()
    {
        string heartbeat = Path.Combine(Directory.CreateTempSubdirectory("textida-hb-").FullName, "prettier.hb");
        string root = Path.Combine(_dir, "hang-prettier");
        Write(root, "prettier.config.js", "const fs = require('fs');\nlet last = 0;\nwhile (true) { if (Date.now() - last > 100) { fs.appendFileSync('" + heartbeat.Replace("\\", "/", StringComparison.Ordinal) + "', 'x'); last = Date.now(); } }\n");
        string target = Write(root, "a.js", "let  x=1\n");
        using var doc = Doc.Open(target, ReferenceBackendFactory.Instance);
        var watch = Stopwatch.StartNew();
        var outcome = await ConfigFormatters.FormatAsync(doc, ConfigFormatters.Find("prettier")!, TimeSpan.FromSeconds(6));

        Assert.True(outcome.Result == FormatResult.TimedOut, $"{outcome.Result}: {outcome.Reason}");
        Assert.Equal("let  x=1\n", Text(doc));
        await AssertStopped(heartbeat, watch);
        Assert.Empty(Directory.GetFiles(root, "*.textida-fmt-*"));
    }

    [RealToolFact("eslint", "node")]
    public Task A_hanging_ESLint_is_stopped_with_the_node_process_behind_its_launcher() =>
        Hang("eslint", [("package.json", "{\"name\":\"x\"}\n"), ("eslint.config.mjs", "import { appendFileSync } from 'node:fs';\nlet last = 0;\nwhile (true) { if (Date.now() - last > 100) { appendFileSync('{HEARTBEAT}', 'x'); last = Date.now(); } }\n")],
            "a.js", "foo(1);\n", Path.Combine(Directory.CreateTempSubdirectory("textida-hb-").FullName, "eslint.hb"));

    [RealToolFact("phpstan", "php")]
    public Task A_hanging_PHPStan_is_stopped_with_the_php_process_behind_its_launcher() =>
        Hang("phpstan", [("composer.json", "{\"name\":\"x/y\"}\n"), ("phpstan.neon", "parameters:\n  level: 5\n  bootstrapFiles:\n    - hang.php\n"),
                ("hang.php", "<?php\n$last = 0;\nwhile (true) { if (microtime(true) - $last > 0.1) { file_put_contents('{HEARTBEAT}', 'x', FILE_APPEND); $last = microtime(true); } }\n")],
            "a.php", "<?php\n\n$a = 1;\n", Path.Combine(Directory.CreateTempSubdirectory("textida-hb-").FullName, "phpstan.hb"));

    [RealToolFact("rubocop")]
    public Task A_hanging_RuboCop_is_stopped_with_the_ruby_process_behind_its_launcher() =>
        Hang("rubocop-lint", [(".rubocop.yml", RuboCopConfig + "require:\n  - ./hang.rb\n"),
                ("hang.rb", "last = 0\nloop do\n  if Time.now.to_f - last > 0.1\n    File.open('{HEARTBEAT}', 'a') { |f| f.write('x') }\n    last = Time.now.to_f\n  end\nend\n")],
            "a.rb", "puts 1\n", Path.Combine(Directory.CreateTempSubdirectory("textida-hb-").FullName, "rubocop.hb"));

    // ---- a tool of the project folder ----

    /// <summary>The PATH without the folders that hold <paramref name="program"/>: the tool is then found only where the project keeps it, and what it needs to run (node, php) is still there.</summary>
    private static string PathWithout(string program)
    {
        string[] extensions = ["", ".exe", ".cmd", ".bat", ".ps1"];
        return string.Join(Path.PathSeparator, (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(dir => !extensions.Any(e => File.Exists(Path.Combine(dir.Trim('"'), program + e)))));
    }

    /// <summary>A folder <paramref name="link"/> that shows the folder <paramref name="target"/> (a junction on Windows, which needs no privilege, a symbolic link elsewhere).</summary>
    private static void Link(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        using var junction = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        junction.WaitForExit();
        Assert.True(junction.ExitCode == 0, "mklink: " + junction.StandardError.ReadToEnd());
    }

    /// <summary>Runs the linter with its program found in the folder of the project and nowhere else; the finding is the one of the run in PATH.</summary>
    private async Task ProjectTool(string id, string program, string linkInProject)
    {
        var c = LintCases.First(l => l.Id == id);
        var linter = Linters.Find(id)!;
        string found = ToolLocator.Find(program) ?? throw new FileNotFoundException(program);
        string tools = Path.GetDirectoryName(found)!; // .../node_modules/.bin or .../vendor/bin
        string project = Path.Combine(_dir, "local-" + id);
        foreach (var (path, text) in c.Project) Write(project, path, text);
        string file = Write(project, c.File, c.Bad);
        Link(Path.Combine(project, linkInProject), Path.GetDirectoryName(tools)!);

        string? old = Environment.GetEnvironmentVariable("PATH");
        using var canary = new NetCanary();
        Environment.SetEnvironmentVariable("PATH", PathWithout(program));
        try
        {
            Assert.Null(ToolLocator.Find(program)); // not in PATH any more
            var tool = Linters.Prepare(linter, file, TimeSpan.FromSeconds(180));
            Assert.StartsWith(Path.GetFullPath(project), tool.Executable, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
            var outcome = await Linters.RunAsync(doc, linter, tool);
            Assert.True(outcome.State == LintState.Done, $"{outcome.State}: {outcome.Reason}");
            IReadOnlyList<LintDiagnostic> matching = c.Pick is null ? outcome.Findings : [.. outcome.Findings.Where(f => f.Code == c.Pick)];
            var finding = Assert.Single(matching);
            Assert.Equal((c.Line, c.Column, c.Code), (finding.Line, finding.Column, finding.Code));
        }
        finally { Environment.SetEnvironmentVariable("PATH", old); }
        Assert.Equal(0, canary.Connections);
    }

    [RealToolFact("eslint", "node")]
    public Task ESLint_is_found_in_the_node_modules_of_the_project_when_it_is_not_in_PATH_and_nothing_is_downloaded() => ProjectTool("eslint", "eslint", "node_modules");

    [RealToolFact("phpstan", "php")]
    public Task PHPStan_is_found_in_the_vendor_folder_of_the_project_when_it_is_not_in_PATH_and_nothing_is_downloaded() => ProjectTool("phpstan", "phpstan", "vendor");

    // ---- no network: a tool that could fetch packages is kept offline by the catalog ----

    [RealToolFact("cargo")]
    public async Task Clippy_with_a_dependency_it_does_not_have_fails_offline_and_connects_nowhere_while_plain_cargo_does_try()
    {
        string root = Path.Combine(_dir, "offline-crate");
        Write(root, "Cargo.toml", "[package]\nname = \"demo\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[dependencies]\ntextida-no-such-crate-9f3a = \"1\"\n");
        string file = Write(root, "src/lib.rs", "pub fn f() {}\n");
        var linter = Linters.Find("clippy")!;
        using var canary = new NetCanary();

        using (var doc = Doc.Open(file, ReferenceBackendFactory.Instance))
        {
            var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(120));
            Assert.True(outcome.State is LintState.Failed or LintState.BadOutput || outcome.Findings.Count == 0, $"{outcome.State} {outcome.Reason}");
            Assert.True(outcome.State != LintState.Done || outcome.Findings.Count == 0);
        }
        Assert.Equal(0, canary.Connections);

        // the control: the same cargo without the variable of the catalog asks the network (the proxy), so the counter works
        var plain = linter with { Environment = null };
        using var again = Doc.Open(file, ReferenceBackendFactory.Instance);
        await Lint(again, plain, file, TimeSpan.FromSeconds(120));
        Assert.True(canary.Connections > 0, "cargo did not try the network without CARGO_NET_OFFLINE; the check proves nothing");
    }

    [RealToolFact("golangci-lint", "go")]
    public async Task GolangciLint_with_a_module_it_does_not_have_fails_offline_and_connects_nowhere_while_plain_go_does_try()
    {
        string root = Path.Combine(_dir, "offline-module");
        Write(root, "go.mod", "module x\n\ngo 1.21\n\nrequire example.com/textida-no-such-module v1.0.0\n");
        Write(root, ".golangci.yml", "version: \"2\"\n");
        string file = Write(root, "main.go", "package main\n\nimport _ \"example.com/textida-no-such-module\"\n\nfunc main() {}\n");
        var linter = Linters.Find("golangci-lint")!;
        using var canary = new NetCanary();
        string? flags = Environment.GetEnvironmentVariable("GOFLAGS");
        Environment.SetEnvironmentVariable("GOFLAGS", "-mod=mod"); // without it go stops at the missing go.sum entry and never looks for the module
        try
        {
            using (var doc = Doc.Open(file, ReferenceBackendFactory.Instance))
            {
                var outcome = await Lint(doc, linter, file, TimeSpan.FromSeconds(180));
                output.WriteLine($"offline run: {outcome.State} {outcome.Reason} findings={Describe(outcome)}");
            }
            Assert.Equal(0, canary.Connections);

            var plain = linter with { Environment = null };
            using var again = Doc.Open(file, ReferenceBackendFactory.Instance);
            await Lint(again, plain, file, TimeSpan.FromSeconds(180));
            Assert.True(canary.Connections > 0, "go did not try the network without GOPROXY=off; the check proves nothing");
        }
        finally { Environment.SetEnvironmentVariable("GOFLAGS", flags); }
    }

    // ---- the versions ----

    [RealToolAnyFactAttribute("phpstan", "psalm", "golangci-lint", "cargo", "rubocop", "eslint", "ruff", "shellcheck", "sqlfluff", "dart")]
    public async Task The_installed_tools_are_inside_the_version_windows_of_the_catalog_and_the_versions_are_listed()
    {
        foreach (var c in LintCases)
        {
            var linter = Linters.Find(c.Id)!;
            if (ToolLocator.Find(linter.Executable) is null)
            {
                output.WriteLine($"{c.Id}: not installed");
                continue;
            }
            string project = Path.Combine(_dir, "v-" + c.Id);
            foreach (var (path, text) in c.Project) Write(project, path, text);
            string file = Write(project, c.File, c.Bad);
            var check = linter.Supported is null ? null : await LinterVersions.CheckAsync(linter, Linters.Prepare(linter, file, TimeSpan.FromSeconds(60)), TimeSpan.FromSeconds(60));
            string version = await VersionOf(c.Version);
            output.WriteLine($"{c.Id}: {version} | window {linter.Supported?.Min}..{linter.Supported?.Below} | {check?.State.ToString() ?? "no window"}");
            if (check is { Mismatch: true } or { State: LinterVersionState.Unreadable }) _failed.Add($"{c.Id}: {check.State} {check.Found}");
        }
        foreach (var c in FormatCases)
        {
            var def = ConfigFormatters.Find(c.Id)!;
            output.WriteLine($"{c.Id}: {(ToolLocator.Find(def.Executable) is null ? "not installed" : await VersionOf(c.Version))}");
        }
        Verdict();
    }
}

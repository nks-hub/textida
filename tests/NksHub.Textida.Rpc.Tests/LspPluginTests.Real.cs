using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Plugins.Lsp;
using Xunit.Abstractions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.5.1 and P8.5.2 against real language servers (category RealTools, skipped where the server is not installed): the Roslyn server renames through a solution of several files, gopls answers a completion that
/// brings an import, the status bar follows a real server, a file over the limit starts none and a server that is killed is started again by the plugin.
/// </summary>
public partial class LspPluginTests
{
    private static string? LastStatus(Rig rig)
    {
        lock (rig.Ui.Statuses) return rig.Ui.Statuses.LastOrDefault()?.Text;
    }

    /// <summary>Starts the server for the document and fails with the answer of the plugin and its notices (the reason a server did not start is in them).</summary>
    private static async Task StartedAsync(Rig rig, Document doc, (string Name, string Path)? program = null)
    {
        var r = await rig.RunAsync(doc, LspPlugin.StartCommand, program: program);
        Assert.True(r.GetProperty("status").GetString() == "ok", "the answer of the plugin: " + r.GetRawText() + "; notices: " + string.Join(" | ", rig.Ui.Notices.Select(n => n.Message)));
    }

    private static async Task RestoreAsync(string folder)
    {
        var info = new ProcessStartInfo(T.DotnetHost, ["restore", "-nologo", "-v", "q"]) { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy" }) info.Environment.Remove(name); // the restore is not what is counted
        using var restore = Process.Start(info)!;
        var output = restore.StandardOutput.ReadToEndAsync();
        await restore.WaitForExitAsync();
        Assert.True(restore.ExitCode == 0, "dotnet restore: " + await output);
    }

    [RealToolFact("roslyn-language-server")]
    [Trait("Category", "RealTools")]
    public async Task A_rename_through_the_real_Roslyn_server_reaches_every_file_of_the_project_in_one_review_and_is_one_step()
    {
        await using var rig = await Rig.CreateAsync(language: "csharp", builtInCatalog: true, startTimeoutMs: 120_000, requestTimeoutMs: 120_000);
        File.WriteAllText(Path.Combine(rig.Folder, "demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <OutputType>Library</OutputType>\n  </PropertyGroup>\n</Project>\n");
        await RestoreAsync(rig.Folder);
        const string head = "namespace Demo;\npublic class Greeter\n{\n    public string He";
        var first = rig.Add("A.cs", head + "llo(string name) => \"Hello \" + name;\n}\n");
        var second = rig.Add("B.cs", "namespace Demo;\npublic class Caller\n{\n    public string Run() => new Greeter().Hello(\"x\");\n}\n");
        string onDisk = Path.Combine(rig.Folder, "C.cs"); // not open in the editor: the review edits it by the hash of its bytes
        File.WriteAllText(onDisk, "namespace Demo;\npublic class Other\n{\n    public string Run() => new Greeter().Hello(\"y\");\n}\n");
        var roslyn = ("roslyn-language-server", ServerCatalog.Find("roslyn-language-server")!);

        await StartedAsync(rig, first, roslyn);
        await T.Until(() => LastStatus(rig) == "LSP: running");
        rig.Ui.Input = request => request.Value == "Hello" ? "Greet" : null;

        // the server is still loading the project here: the plugin waits for it, a rename answered now would reach only the files the server has seen
        var r = await rig.RunAsync(first, LspPlugin.RenameCommand, Bytes(head), program: roslyn);

        Assert.True(r.GetProperty("status").GetString() == "ok", "the answer of the plugin: " + r.GetRawText() + "; notices: " + string.Join(" | ", rig.Ui.Notices.Select(n => n.Message)));
        Assert.Equal("applied", r.GetProperty("decision").GetString());
        var shown = Assert.Single(rig.Review.Shown);
        Assert.True(shown.Proposal.Atomic);
        Assert.Equal("Rename Hello to Greet", shown.Proposal.Title);
        Assert.Equal(3, shown.Proposal.Files.Count);
        Assert.Contains("public string Greet(string name)", Rig.TextOf(first));
        Assert.Contains("new Greeter().Greet(\"x\")", Rig.TextOf(second));
        Assert.Contains("new Greeter().Greet(\"y\")", File.ReadAllText(onDisk));
        Assert.DoesNotContain("Hello(", Rig.TextOf(second));
        Assert.Equal(1, first.UndoDepth);
        Assert.Equal(1, second.UndoDepth);
    }

    /// <summary>
    /// P8.5.1: a running language server records no telemetry. The catalog starts Roslyn with <c>--telemetryLevel off</c>, and 5.13 still opens connections to the telemetry service of Visual Studio, as many with the
    /// flag as without it (checked on Windows 11 and macOS 15: <c>CONNECT telemetry.visualstudio.microsoft.com:443</c> through the proxy of the environment, also from a server that only answered initialize).
    /// The proxy refuses them, so what the connections would have carried is not known. No option or environment variable of the server that stops them is known either; this test stays red until one is found.
    /// </summary>
    [RealToolFact("roslyn-language-server")]
    [Trait("Category", "RealTools")]
    public async Task Roslyn_started_with_telemetry_off_opens_no_connection_to_the_telemetry_service()
    {
        using var canary = new NetCanary(); // set before the plugin starts, so that the server inherits it
        await using var rig = await Rig.CreateAsync(language: "csharp", builtInCatalog: true, startTimeoutMs: 120_000, requestTimeoutMs: 120_000);
        File.WriteAllText(Path.Combine(rig.Folder, "demo.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <OutputType>Library</OutputType>\n  </PropertyGroup>\n</Project>\n");
        var doc = rig.Add("A.cs", "namespace Demo;\npublic class Greeter { }\n");
        await StartedAsync(rig, doc, ("roslyn-language-server", ServerCatalog.Find("roslyn-language-server")!));
        await Task.Delay(5000); // the project is loaded and the server has been up for a while

        Assert.True(canary.Connections == 0, "the server tried to reach: " + string.Join(", ", canary.Targets.Distinct()) + $" ({canary.Connections} connections)");
    }

    [RealToolFact("gopls", "go")]
    [Trait("Category", "RealTools")]
    public async Task A_completion_of_the_real_gopls_with_an_additional_edit_is_written_as_one_undo_step()
    {
        await using var rig = await Rig.CreateAsync(language: "go", builtInCatalog: true, startTimeoutMs: 60_000, requestTimeoutMs: 60_000);
        File.WriteAllText(Path.Combine(rig.Folder, "go.mod"), "module x\n\ngo 1.21\n");
        const string text = "package main\n\nfunc main() {\n\tfmt.Prin\n}\n";
        var doc = rig.Add("main.go", text);
        await StartedAsync(rig, doc);
        var depth = doc.UndoDepth;

        long caret = Bytes(text[..(text.IndexOf("Prin", StringComparison.Ordinal) + 4)]);
        var clock = Stopwatch.StartNew();
        CompletionAnswer answer;
        while (!(answer = await CompleteAsync(rig, doc, caret)).Items.Any(i => i.Label == "Println")) // gopls answers with nothing while it loads the module
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "gopls never offered Println");
            await Task.Delay(500);
        }

        var item = answer.Items.First(i => i.Label == "Println");
        var extra = Assert.Single(item.AdditionalEdits);
        Assert.Contains("import \"fmt\"", extra.Text);
        await rig.Owner.InvokeAsync(() => rig.Docs.ApplyCompletion(item));

        string applied = Rig.TextOf(doc);
        Assert.Contains("import \"fmt\"", applied);
        Assert.Contains("\tfmt.Println\n", applied);
        Assert.Equal(depth + 1, doc.UndoDepth);
        await rig.Owner.InvokeAsync(() => doc.Undo());
        Assert.Equal(text, Rig.TextOf(doc));
    }

    [RealToolFact("gopls", "go")]
    [Trait("Category", "RealTools")]
    public async Task The_status_bar_follows_a_real_server_and_a_file_over_the_limit_starts_none()
    {
        string copy = RealTools.UniqueCopy("gopls", Path.Combine(Path.GetTempPath(), "textida-gopls-" + Guid.NewGuid().ToString("N")[..8]));
        try
        {
            await using (var paged = await Rig.CreateAsync(declared: true, program: copy, language: "go", realArguments: [], maxBytes: 100))
            {
                var big = paged.Add("big.go", "package main\n// " + new string('x', 200) + "\n");
                Assert.Equal("tooLarge", (await paged.RunAsync(big, LspPlugin.StartCommand)).GetProperty("status").GetString());
                await Task.Delay(500);
                Assert.Empty(RealTools.Running(copy));
                Assert.Null(LastStatus(paged));
            }

            await using var rig = await Rig.CreateAsync(declared: true, program: copy, language: "go", realArguments: [], startTimeoutMs: 60_000);
            File.WriteAllText(Path.Combine(rig.Folder, "go.mod"), "module x\n\ngo 1.21\n");
            var doc = rig.Add("main.go", "package main\n\nfunc main() {}\n");
            await StartedAsync(rig, doc);
            await T.Until(() => LastStatus(rig) == "LSP: running");
            Assert.NotEmpty(RealTools.Running(copy)); // gopls may start a helper process of its own: the server counts as running when there is one

            Assert.Equal("stopped", (await rig.RunAsync(doc, LspPlugin.StopCommand)).GetProperty("status").GetString());
            await T.Until(() => LastStatus(rig) == "LSP: off");
            await T.Until(() => RealTools.Running(copy).Length == 0);
        }
        finally { TestDirDelete(Path.GetDirectoryName(copy)!); }
    }

    [Fact]
    [Trait("Category", "RealTools")]
    public async Task The_status_bar_says_not_found_for_a_language_of_the_catalog_whose_server_is_not_installed()
    {
        string? language = new[] { "swift", "kotlin", "ruby", "java", "lua", "php", "sql" }.FirstOrDefault(l => { ServerCatalog.BuiltIn.Resolve(l, out bool found); return !found; });
        if (language is null) return; // every server of these languages is installed here: nothing to be missing
        await using var rig = await Rig.CreateAsync(language: language, builtInCatalog: true);
        var doc = rig.Add("a.txt", "x\n");

        Assert.Equal("noServer", (await rig.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());
        await T.Until(() => LastStatus(rig) == "LSP: not found");
    }

    [RealToolFact("gopls", "go")]
    [Trait("Category", "RealTools")]
    public async Task A_declared_real_server_that_is_killed_is_started_again_by_itself_and_serves_the_open_document()
    {
        string copy = RealTools.UniqueCopy("gopls", Path.Combine(Path.GetTempPath(), "textida-gopls-" + Guid.NewGuid().ToString("N")[..8]));
        try
        {
            await using var rig = await Rig.CreateAsync(declared: true, program: copy, language: "go", realArguments: [], restart: (3, 300, 2000, 600_000), startTimeoutMs: 60_000, requestTimeoutMs: 60_000);
            File.WriteAllText(Path.Combine(rig.Folder, "go.mod"), "module x\n\ngo 1.21\n");
            const string text = "package main\n\nfunc helper() {}\n\nfunc main() {\n\thelper()\n}\n";
            var doc = rig.Add("main.go", text);
            long call = Bytes(text[..(text.LastIndexOf("helper", StringComparison.Ordinal) + 2)]);
            await StartedAsync(rig, doc);
            await T.Until(() => RealTools.Running(copy).Length >= 1);
            var killed = RealTools.Running(copy).Select(p => p.Id).ToHashSet();

            foreach (var process in RealTools.Running(copy)) process.Kill(entireProcessTree: true); // the server dies under the plugin
            await T.Until(() => rig.Ui.Notices.Any(n => n.Message.Contains("stopped unexpectedly")));
            await T.Until(() => RealTools.Running(copy).Any(p => !killed.Contains(p.Id)), 30_000);

            var answer = await rig.RunAsync(doc, LspPlugin.DefinitionCommand, call);
            Assert.Equal("ok", answer.GetProperty("status").GetString());
            var place = Assert.Single(answer.GetProperty("locations").EnumerateArray());
            Assert.Equal(2, place.GetProperty("line").GetInt32()); // `func helper() {}` is the third line
            await T.Until(() => LastStatus(rig) == "LSP: running");
        }
        finally { TestDirDelete(Path.GetDirectoryName(copy)!); }
    }
}

/// <summary>
/// The launch lines of the catalog, run for real: every language server of the table that is installed here is started the way the plugin starts it and has to answer <c>initialize</c>.
/// The test output lists what each one can do and which are not installed.
/// </summary>
[Trait("Category", "RealTools")]
public sealed class RealLanguageServerTests(ITestOutputHelper output)
{
    private static readonly (string Language, string Program)[] Servers =
    [
        ("javascript", "typescript-language-server"), ("html", "vscode-html-language-server"), ("css", "vscode-css-language-server"), ("sql", "sqls"), ("python", "pyright-langserver"), ("shell", "bash-language-server"),
        ("java", "jdtls"), ("csharp", "roslyn-language-server"), ("csharp", "csharp-ls"), ("c", "clangd"), ("php", "phpactor"), ("go", "gopls"), ("rust", "rust-analyzer"), ("kotlin", "kotlin-lsp"),
        ("lua", "lua-language-server"), ("swift", "sourcekit-lsp"), ("ruby", "ruby-lsp"), ("dart", "dart"),
    ];

    /// <summary>The project a tool of <c>node_modules/.bin</c> belongs to: typescript-language-server wants the typescript package of the project it works in.</summary>
    private static string? ProjectOf(string program)
    {
        var bin = Path.GetDirectoryName(program);
        return bin is not null && Path.GetFileName(bin) == ".bin" && Path.GetDirectoryName(bin) is { } modules && Path.GetFileName(modules) == "node_modules" ? Path.GetDirectoryName(modules) : null;
    }

    [RealToolAnyFact("typescript-language-server", "vscode-html-language-server", "vscode-css-language-server", "sqls", "pyright-langserver", "bash-language-server", "jdtls", "roslyn-language-server", "csharp-ls", "clangd",
        "phpactor", "gopls", "rust-analyzer", "kotlin-lsp", "lua-language-server", "sourcekit-lsp", "ruby-lsp", "dart")]
    public async Task Every_installed_language_server_of_the_catalog_answers_initialize_on_the_launch_line_of_the_catalog()
    {
        string root = Directory.CreateTempSubdirectory("textida-lsp-root-").FullName;
        var failed = new List<string>();
        var settings = new LspSettings(1 << 20, 120_000, 30_000, 5_000, 50, 5, 200, 50);
        try
        {
            foreach (var (language, program) in Servers)
            {
                var entry = ServerCatalog.BuiltIn.Entry(language, program);
                Assert.NotNull(entry); // the table of the editor names it
                string? path = ServerCatalog.Find(program);
                if (path is null)
                {
                    output.WriteLine($"{language} / {program}: not installed");
                    continue;
                }
                if (OperatingSystem.IsWindows() && program == "phpactor")
                {
                    output.WriteLine($"{language} / {program}: not run, phpactor needs the posix extension of PHP, which PHP for Windows does not have");
                    continue;
                }
                var watch = Stopwatch.StartNew();
                using var canary = new NetCanary(); // its own for each server: a server inherits the address it was started with, so a late connection of an earlier one is not counted for this one
                try
                {
                    var server = await LspServer.StartAsync(entry! with { Program = path }, ProjectOf(path) ?? root, settings, (_, _) => ValueTask.CompletedTask, CancellationToken.None);
                    output.WriteLine($"{language} / {program}: ok in {watch.Elapsed.TotalSeconds:0.0} s, definition={server.Definition} references={server.References} completion={server.Completion} rename={server.Rename} incremental={server.Incremental}");
                    await Task.Delay(1500); // what a server does at once after its start (a version check, telemetry) happens in this time
                    await server.StopAsync();
                    if (canary.Connections != 0) // information only: ruby-lsp installs its gems from the network, Roslyn is the subject of its own test
                        output.WriteLine($"{language} / {program}: tried to reach the network ({canary.Connections} connections): {string.Join(", ", canary.Targets.Distinct())}");
                }
                catch (ServerStartException e)
                {
                    output.WriteLine($"{language} / {program}: FAILED {e.Message}");
                    failed.Add($"{language} / {program}: {e.Message}");
                }
            }
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        Assert.True(failed.Count == 0, Environment.NewLine + string.Join(Environment.NewLine, failed));
    }
}

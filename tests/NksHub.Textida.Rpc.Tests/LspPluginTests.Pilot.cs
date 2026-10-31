using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.5.7: the pilot servers of the catalog run for real on a small project of their own language: definition, references, completion in a tab that is not saved, diagnostics, rename, and a cold and a warm start with
/// the memory of the processes the server started. Skipped where the server is not installed. Every run writes its findings to <c>textida-lsp-pilot/&lt;server&gt;.txt</c> in the temporary folder, because the
/// numbers are the point and a passing test hides them.
/// </summary>
public partial class LspPluginTests
{
    private sealed record Pilot(string Language, string Server, Dictionary<string, string> Files, string UseFile, string UseWord, string DefFile, string DefWord, string CompleteFile, string CompleteLine,
        string CompleteLabel, string BrokenLine, string Restore = "", string RenameTo = "Renamed", string[]? DeclaredArgs = null, bool Rename = true);

    private const string CSharpProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <OutputType>Library</OutputType>\n  </PropertyGroup>\n</Project>\n";

    private static Pilot CSharp(string server) => new("csharp", server, new()
    {
        ["demo.csproj"] = CSharpProject,
        ["Greeter.cs"] = "namespace Demo;\npublic class Greeter\n{\n    public string Hello(string name) => \"Hello \" + name;\n}\n",
        ["Caller.cs"] = "namespace Demo;\npublic class Caller\n{\n    public string Run() => new Greeter().Hello(\"x\");\n    public string Other()\n    {\n        var g = new Greeter();\n        return g.Hel;\n    }\n}\n",
    }, "Caller.cs", "Hello", "Greeter.cs", "Hello", "Caller.cs", "        return g.Hel", "Hello", "        int broken = \"text\";\n", Restore: "dotnet");

    private static readonly Pilot Shell = new("shell", "bash-language-server", new()
    {
        ["lib.sh"] = "#!/bin/bash\ngreet_user() {\n  echo \"hello $1\"\n}\n",
        ["main.sh"] = "#!/bin/bash\nsource ./lib.sh\ngreet_user world\ngreet_us\n",
    }, "main.sh", "greet_user", "lib.sh", "greet_user", "main.sh", "greet_us", "greet_user", "if then fi (\n");

    private static readonly Pilot Php = new("php", "intelephense", new()
    {
        ["Greeter.php"] = "<?php\nclass Greeter\n{\n    public function hello(string $name): string\n    {\n        return \"Hello \" . $name;\n    }\n}\n",
        ["main.php"] = "<?php\nrequire_once __DIR__ . '/Greeter.php';\n$g = new Greeter();\necho $g->hello('x');\necho $g->hel\n",
    }, "main.php", "hello", "Greeter.php", "hello", "main.php", "echo $g->hel", "hello", "$x = ;\n", DeclaredArgs: ["--stdio"], Rename: false); // not in the catalog: started the way a plugin manifest declares a server; rename is a paid feature of this server (the plugin answers notSupported)

    private static readonly Pilot Rust = new("rust", "rust-analyzer", new()
    {
        ["Cargo.toml"] = "[package]\nname = \"demo\"\nversion = \"0.1.0\"\nedition = \"2021\"\n",
        ["src/lib.rs"] = "pub fn greet(name: &str) -> String {\n    format!(\"hello {name}\")\n}\n\npub fn run() -> String {\n    greet(\"x\")\n}\n\npub fn other() -> String {\n    gree\n}\n",
    }, "src/lib.rs", "greet", "src/lib.rs", "greet", "src/lib.rs", "    gree", "greet", "let broken: i32 = \"text\";\n");

    private static readonly Pilot Clang = new("c", "clangd", new()
    {
        ["greet.h"] = "int greet_user(int x);\n",
        ["greet.c"] = "#include \"greet.h\"\nint greet_user(int x) { return x + 1; }\n",
        ["main.c"] = "#include \"greet.h\"\nint run(void) { return greet_user(1); }\nint other(void) { return greet_us\n}\n",
        ["compile_commands.json"] = "[]",
    }, "main.c", "greet_user", "greet.c", "greet_user", "main.c", "int other(void) { return greet_us", "greet_user", "int broken = ;\n");

    private static string ServerVersion(string path)
    {
        try
        {
            var info = new ProcessStartInfo(path, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(info)!;
            var text = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(20_000)) { process.Kill(true); return "unknown"; }
            return text.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "unknown";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return "unknown"; }
    }

    private static (int Count, long PrivateMiB) Since(DateTime from, HashSet<int> before)
    {
        int count = 0;
        long bytes = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (before.Contains(process.Id) || process.StartTime < from || process.ProcessName is "conhost" or "WerFault") continue;
                    count++;
                    bytes += process.PrivateMemorySize64;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { /* gone or not ours */ }
            }
        }
        return (count, bytes >> 20);
    }

    private static long At(string text, string word, int inside = 1) => Bytes(text[..(text.IndexOf(word, StringComparison.Ordinal) + inside)]);

    private async Task RunPilotAsync(Pilot pilot)
    {
        var found = ServerCatalog.Find(pilot.Server) ?? throw new InvalidOperationException(pilot.Server + " vanished from PATH");
        var report = new StringBuilder($"{pilot.Server} {ServerVersion(found)} ({pilot.Language}) on {found}\n");
        void Note(string step, bool ok, string detail) => report.AppendLine($"{(ok ? "OK  " : "FAIL")} {step}: {detail}");
        try
        {
            await using var rig = pilot.DeclaredArgs is { } declaredArgs
                ? await Rig.CreateAsync(language: pilot.Language, declared: true, program: found, realArguments: declaredArgs, startTimeoutMs: 180_000, requestTimeoutMs: 120_000)
                : await Rig.CreateAsync(language: pilot.Language, builtInCatalog: true, startTimeoutMs: 180_000, requestTimeoutMs: 120_000);
            foreach (var (name, content) in pilot.Files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(rig.Folder, name))!);
                File.WriteAllText(Path.Combine(rig.Folder, name), content);
            }
            if (pilot.Files.ContainsKey("compile_commands.json")) // clangd learns the flags of each file from the compile database
                File.WriteAllText(Path.Combine(rig.Folder, "compile_commands.json"), JsonSerializer.Serialize(pilot.Files.Keys.Where(k => k.EndsWith(".c")).Select(k =>
                    new { directory = rig.Folder, file = Path.Combine(rig.Folder, k), arguments = new[] { "clang", "-c", Path.Combine(rig.Folder, k) } })));
            if (pilot.Restore == "dotnet") await RestoreAsync(rig.Folder);
            var docs = pilot.Files.Keys.Where(k => k.EndsWith(".cs") || k.EndsWith(".sh") || k.EndsWith(".php") || k.EndsWith(".rs") || k.EndsWith(".c") || k.EndsWith(".h")).ToDictionary(k => k, k => rig.Add(k, pilot.Files[k]));
            (string, string)? program = pilot.DeclaredArgs is null ? (pilot.Server, found) : null;
            var use = docs[pilot.UseFile];
            var def = docs[pilot.DefFile];
            var target = docs[pilot.CompleteFile];

            // cold start
            var before = Process.GetProcesses().Select(p => { using (p) return p.Id; }).ToHashSet();
            var t0 = DateTime.Now;
            var clock = Stopwatch.StartNew();
            var start = await rig.RunAsync(use, LspPlugin.StartCommand, program: program);
            bool started = start.GetProperty("status").GetString() == "ok";
            Note("start", started, started ? $"cold {clock.Elapsed.TotalSeconds:0.0} s" : start.GetRawText() + " | " + string.Join(" | ", rig.Ui.Notices.Select(n => n.Message)));
            if (!started) return;

            // definition (the server may still be loading the project: ask again for a while)
            JsonElement locations = default;
            clock.Restart();
            string useText = Rig.TextOf(use);
            for (; clock.Elapsed < TimeSpan.FromSeconds(120); await Task.Delay(1000))
            {
                var r = await rig.RunAsync(use, LspPlugin.DefinitionCommand, At(useText, pilot.UseWord), program: program);
                if (r.TryGetProperty("locations", out locations) && locations.GetArrayLength() > 0) break;
            }
            bool defOk = locations.ValueKind == JsonValueKind.Array && locations.GetArrayLength() > 0 && Path.GetFileName(locations[0].GetProperty("path").GetString()!) == Path.GetFileName(pilot.DefFile);
            Note("definition", defOk, defOk ? $"{Path.GetFileName(pilot.DefFile)} line {locations[0].GetProperty("line").GetInt32() + 1}, ready {clock.Elapsed.TotalSeconds:0.0} s after start" : "no location in " + pilot.DefFile);

            // references
            var refs = await rig.RunAsync(def, LspPlugin.ReferencesCommand, At(Rig.TextOf(def), pilot.DefWord), program: program);
            int count = refs.TryGetProperty("count", out var c) ? c.GetInt32() : -1;
            Note("references", count >= 2, $"{count} found");

            // completion in a tab that is not saved: the text of the editor holds the word the file on disk does not
            string line = pilot.CompleteLine;
            string targetText = Rig.TextOf(target);
            if (!targetText.Contains(line)) { await rig.Edit(target, Bytes(targetText), Bytes(targetText), "\n" + line); targetText = Rig.TextOf(target); }
            var answer = await CompleteAsync(rig, target, At(targetText, line, line.Length));
            bool offered = answer.Items.Any(i => i.Label.TrimStart().StartsWith(pilot.CompleteLabel, StringComparison.OrdinalIgnoreCase)); // clangd puts a space before the label
            Note("completion in an unsaved tab", offered, offered ? $"{answer.Items.Count} items, first \"{answer.Items.First().Label}\"" : $"{answer.Items.Count} items, none starting with {pilot.CompleteLabel}: " + string.Join(", ", answer.Items.Take(8).Select(i => i.Label)));

            // diagnostics: the broken line is only in the editor
            long endBytes = Bytes(Rig.TextOf(use));
            await rig.Edit(use, endBytes, endBytes, pilot.BrokenLine);
            clock.Restart();
            bool diagnosed = false;
            for (; clock.Elapsed < TimeSpan.FromSeconds(90) && !(diagnosed = rig.DiagnosticsOf(use).Count > 0); await Task.Delay(500)) { }
            Note("diagnostics", diagnosed, diagnosed ? $"{rig.DiagnosticsOf(use).Count} after {clock.Elapsed.TotalSeconds:0.0} s: {rig.DiagnosticsOf(use)[0].Message}" : "none in 90 s");
            await rig.Owner.InvokeAsync(() => use.Undo());

            // rename
            if (pilot.Rename)
            {
                rig.Ui.Input = request => request.Value == pilot.UseWord ? pilot.RenameTo : null;
                var rename = await rig.RunAsync(def, LspPlugin.RenameCommand, At(Rig.TextOf(def), pilot.DefWord), program: program);
                bool renamed = rename.GetProperty("status").GetString() == "ok" && rig.Review.Shown.Count > 0;
                Note("rename", renamed, renamed ? $"{rig.Review.Shown[^1].Proposal.Files.Count} file(s) in the review: {rename.GetProperty("decision").GetString()}" : rename.GetRawText());
            }
            else Note("rename", true, "not run: the server does not offer it without a licence");

            // memory of what the server started, then a warm start
            await Task.Delay(2000);
            var memory = Since(t0, before);
            Note("memory", true, $"{memory.Count} process(es), {memory.PrivateMiB} MiB private");
            await rig.RunAsync(use, LspPlugin.StopCommand, program: program);
            await Task.Delay(1000);
            clock.Restart();
            var again = await rig.RunAsync(use, LspPlugin.StartCommand, program: program);
            Note("warm start", again.GetProperty("status").GetString() == "ok", $"{clock.Elapsed.TotalSeconds:0.0} s (a second start in the same plugin process; the cold start of a new process is the first line)");
            await rig.RunAsync(use, LspPlugin.StopCommand, program: program);
        }
        finally
        {
            string folder = Path.Combine(Path.GetTempPath(), "textida-lsp-pilot");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, pilot.Server + ".txt"), report.ToString());
        }
        Assert.DoesNotContain("FAIL", report.ToString());
    }

    [RealToolFact("roslyn-language-server")]
    [Trait("Category", "RealTools")]
    public Task Pilot_roslyn_language_server() => RunPilotAsync(CSharp("roslyn-language-server"));

    [RealToolFact("csharp-ls")]
    [Trait("Category", "RealTools")]
    public Task Pilot_csharp_ls() => RunPilotAsync(CSharp("csharp-ls"));

    [RealToolFact("bash-language-server")]
    [Trait("Category", "RealTools")]
    public Task Pilot_bash_language_server() => RunPilotAsync(Shell);

    [RealToolFact("intelephense")]
    [Trait("Category", "RealTools")]
    public Task Pilot_intelephense() => RunPilotAsync(Php);

    [RealToolFact("rust-analyzer")]
    [Trait("Category", "RealTools")]
    public Task Pilot_rust_analyzer() => RunPilotAsync(Rust);

    [RealToolFact("clangd")]
    [Trait("Category", "RealTools")]
    public Task Pilot_clangd() => RunPilotAsync(Clang);
}

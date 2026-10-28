using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// PR.6.4, first mode: the editor in its default state, with a network proxy set that nothing may reach. Every scenario must end with no outbound attempt of any process: no connect, no name sent
/// to the resolver, no request, no datagram. The local channels that were used are listed in the report of each run (output of the test).
/// </summary>
[Collection("LocState")]
[Trait("Category", "Network")]
public class NetworkNoConsentTests
{
    private const string OnePixelPng = PreviewSafetyTests.OnePixelPng;

    internal static void AssertSilent(NetReport report, NetworkRig rig)
    {
        Assert.True(report.Forbidden.Count == 0, "Outbound attempts without consent:\n" + report);
        Assert.True(report.Requests.Count == 0, "Requests without consent:\n" + report);
        Assert.Empty(rig.Net.Sinkhole.Targets);
    }

    /// <summary>A silent run proves nothing about a process the watch did not get into.</summary>
    internal static void AssertWatched(NetReport report, params string[] processes)
    {
        foreach (var process in processes)
            Assert.True(report.Hooked.Any(h => h.Process == process), $"The watch never got into {process}:\n{report}");
    }

    [AvaloniaFact]
    public async Task Starting_the_editor_makes_no_connection()
    {
        using var rig = new NetworkRig();
        rig.StartLikeTheProgram();
        await rig.Settle(1500);

        AssertSilent(await rig.Net.FinishAsync(), rig);
    }

    /// <summary>One file of every kind the editor treats differently, with references to remote hosts inside the ones that could fetch them.</summary>
    private static IEnumerable<(string Name, string Content)> Corpus()
    {
        const string remote = "https://assets.example.test/";
        yield return ("notes.txt", "plain text " + remote + "a.png\n");
        yield return ("readme.md", $"# Title\n\n![logo]({remote}logo.png)\n\n[docs]({remote}docs)\n\n<img src=\"{remote}b.png\">\n");
        yield return ("page.html", $"<!doctype html><html><head><link rel=\"stylesheet\" href=\"{remote}s.css\"><script src=\"{remote}x.js\"></script></head><body><img src=\"{remote}p.png\"><a href=\"{remote}l\">link</a></body></html>\n");
        yield return ("style.css", $"@import url({remote}i.css);\nbody {{ background: url({remote}bg.png); }}\n");
        yield return ("app.js", $"fetch('{remote}api');\n");
        yield return ("main.ts", $"const u: string = '{remote}t';\n");
        yield return ("data.json", $"{{ \"$schema\": \"{remote}data.schema.json\", \"a\": 1 }}\n");
        yield return ("settings.jsonc", "// comment\n{ \"a\": 1 }\n");
        yield return ("config.yaml", $"# yaml-language-server: $schema={remote}config.schema.json\nname: demo\nitems:\n  - 1\n  - 2\n");
        yield return (".github/workflows/ci.yml", "name: ci\non: [push]\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo hi\n");
        yield return ("Cargo.toml", "[package]\nname = \"demo\"\nversion = \"0.1.0\"\n");
        yield return ("pyproject.toml", "[project]\nname = \"demo\"\nversion = \"0.1.0\"\n");
        yield return ("tool.toml", $"#:schema {remote}tool.schema.json\nkey = 1\n");
        yield return ("doc.xml", $"<?xml version=\"1.0\"?>\n<!DOCTYPE doc SYSTEM \"{remote}doc.dtd\">\n<doc xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"{remote}doc.xsd\"/>\n");
        yield return ("icon.svg", $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><image xlink:href=\"{remote}i.png\"/></svg>\n");
        yield return ("data.csv", "a,b\n1,2\n");
        yield return ("index.php", $"<?php\n$x = file_get_contents('{remote}php');\n");
        yield return ("Program.cs", $"class C {{ string u = \"{remote}cs\"; }}\n");
        yield return ("script.py", $"import urllib.request\nurllib.request.urlopen('{remote}py')\n");
        yield return ("query.sql", "select 1;\n");
        yield return ("app.log", "2026-10-28 10:00:00 INFO started\n2026-10-28 10:00:01 ERROR failed\n");
        yield return ("settings.ini", "[a]\nb=1\n");
        yield return ("run.ps1", "Write-Host 'hi'\n");
        yield return (".env", "TOKEN=abc\n");
        yield return ("SKILL.md", "---\nname: skill\ndescription: demo\n---\nBody\n");
    }

    [AvaloniaFact]
    public async Task Opening_files_of_every_kind_makes_no_connection_and_the_checks_of_the_data_files_finish()
    {
        using var rig = new NetworkRig();
        rig.StartLikeTheProgram();
        var checkedTabs = new List<DocTab>();
        foreach (var (name, content) in Corpus())
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            if (PluginServices.IsSchemaFile(tab)) checkedTabs.Add(tab);
            await rig.Settle(50);
        }
        // binary and wide-character files, the hex view, and a file above the large-file threshold shown in pages
        var binary = Path.Combine(rig.Temp.Path, "proj", "blob.bin");
        File.WriteAllBytes(binary, RandomNumberGenerator.GetBytes(4096));
        rig.Window.OpenHex(rig.Window.OpenFile(binary, null), null);
        File.WriteAllBytes(Path.Combine(rig.Temp.Path, "proj", "wide.txt"), [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("wide https://assets.example.test/w\n")]);
        rig.Window.OpenFile(Path.Combine(rig.Temp.Path, "proj", "wide.txt"), null);
        File.WriteAllBytes(Path.Combine(rig.Temp.Path, "proj", "image.png"), Convert.FromBase64String(OnePixelPng));
        rig.Window.OpenFile(Path.Combine(rig.Temp.Path, "proj", "image.png"), null);
        rig.App.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var big = Path.Combine(rig.Temp.Path, "proj", "big.txt");
        File.WriteAllText(big, string.Concat(Enumerable.Repeat("line of the large file https://assets.example.test/big\n", 40_000)));
        Assert.True(rig.Window.OpenFile(big, null).Doc.IsLarge);

        foreach (var tab in checkedTabs)
        {
            rig.Window.SelectTab(tab);
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab) is not null, "the schema check of " + tab.Path);
        }
        await rig.Settle(1000);

        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        AssertWatched(report, "NksHub.Textida.Plugins.Json");
        // the schemas named inside the files are held back, not just never looked at
        foreach (var name in new[] { "data.json", "config.yaml" })
            Assert.Equal("noConsent", rig.App.Plugins.SchemaResultOf(checkedTabs.Single(t => Path.GetFileName(t.Path) == name))!.State);
    }

    [AvaloniaFact]
    public async Task JSON_YAML_and_TOML_files_that_need_a_schema_from_the_web_download_nothing_and_asking_to_allow_it_and_saying_no_leaves_it_so()
    {
        using var rig = new NetworkRig();
        var wanted = new (string Name, string Content, string Host)[]
        {
            ("tsconfig.json", "{ \"compilerOptions\": {} }\n", "json.schemastore.org"),
            ("data.json", "{ \"$schema\": \"https://schemas.example.test/data.json\", \"a\": 1 }\n", "schemas.example.test"),
            ("config.yaml", "# yaml-language-server: $schema=https://schemas.example.test/config.json\nname: demo\n", "schemas.example.test"),
            ("pyproject.toml", "[project]\nname = \"demo\"\n", "json.schemastore.org"),
        };
        foreach (var (name, content, host) in wanted)
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab) is not null, "the schema check of " + name);
            Assert.Equal(("noConsent", host), (rig.App.Plugins.SchemaResultOf(tab)!.State, rig.App.Plugins.SchemaResultOf(tab)!.Host));
            rig.Dialogs.Answers.Enqueue(1);
            await rig.Window.SchemaAllowDownloadAsync(tab);
            Assert.Equal("noConsent", rig.App.Plugins.SchemaResultOf(tab)!.State);
            Assert.False(rig.App.Plugins.Registries.IsAllowed(PackageRegistryConsent.SchemaId(host)));
        }
        await rig.Settle(500);

        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        AssertWatched(report, "NksHub.Textida.Plugins.Json");
    }

    [AvaloniaFact]
    public async Task Completion_and_edits_in_files_with_a_remote_schema_mapping_or_dependency_download_and_look_up_nothing()
    {
        using var rig = new NetworkRig();
        Assert.Null(rig.App.Workbench.Set("schema.mappings", JsonValue.Create("*.cfgx=https://maps.example.test/cfg.json")));
        var files = new (string Name, string Content, string Caret)[]
        {
            ("data.json", "{ \"$schema\": \"https://schemas.example.test/data.json\", \"na\": 1 }\n", "\"na"),
            ("a.cfgx", "{ \"na\": 1 }\n", "\"na"),
            ("package.json", "{ \"dependencies\": { \"lef\": \"1.0.0\" } }\n", "\"lef"),
            ("config.yaml", "# yaml-language-server: $schema=https://schemas.example.test/config.json\nna: 1\n", "na"),
        };
        foreach (var (name, content, caret) in files)
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab) is not null, "the check of " + name);
            int at = content.IndexOf(caret, StringComparison.Ordinal) + caret.Length;
            tab.Editor!.Select(at, 0);
            await rig.App.Plugins.RequestCompletionAsync(tab);
            tab.Editor.Document.Insert(at, "m"); // an edit makes the check run again
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab)!.Revision == tab.Doc.Doc.Revision, "the check after the edit of " + name);
            await rig.App.Plugins.RequestCompletionAsync(tab);
        }
        Assert.Equal("noConsent", rig.App.Plugins.SchemaResultOf(rig.Window.Tabs.Single(t => Path.GetFileName(t.Path) == "a.cfgx"))!.State);
        await rig.Settle(500);

        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        AssertWatched(report, "NksHub.Textida.Plugins.Json");
    }

    [AvaloniaFact]
    public async Task The_token_estimate_of_a_selection_is_counted_here_under_every_rule_and_sends_nothing()
    {
        using var rig = new NetworkRig();
        var tab = rig.Window.NewDocument();
        tab.Editor!.Document.Text = "some text to count ".PadRight(4000, 'x');
        tab.Editor.Select(0, 3000);
        foreach (var rule in TokenEstimator.Presets)
        {
            rig.Window.TokenRule = rule;
            Assert.True(rig.Window.TokensItem.IsVisible);
            rig.Dialogs.Answers.Enqueue(0);
            rig.Window.ShowTokenEstimate();
            await rig.Window.TokenDialogTask;
        }
        await rig.Settle(500);

        AssertSilent(await rig.Net.FinishAsync(), rig);
    }

    [AvaloniaFact]
    public async Task Package_manifests_ask_no_registry_and_a_refused_question_keeps_it_so()
    {
        using var rig = new NetworkRig();
        var manifests = new (string Name, string Content)[]
        {
            ("package.json", "{ \"name\": \"demo\", \"dependencies\": { \"left-pad\": \"1.0.0\" } }\n"),
            ("composer.json", "{ \"require\": { \"acme/lib\": \"1.0.0\" } }\n"),
            ("requirements.txt", "requests==2.0.0\n"),
            ("demo.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Acme.Lib\" Version=\"1.0.0\" /></ItemGroup></Project>\n"),
            ("go.mod", "module demo\n\ngo 1.22\n\nrequire example.test/lib v1.0.0\n"),
            ("Cargo.toml", "[package]\nname = \"demo\"\nversion = \"0.1.0\"\n\n[dependencies]\nacme = \"1.0.0\"\n"),
            ("Gemfile", "source 'https://rubygems.org'\ngem 'acme', '1.0.0'\n"),
            ("pubspec.yaml", "name: demo\ndependencies:\n  acme: 1.0.0\n"),
            ("pom.xml", "<project><dependencies><dependency><groupId>acme</groupId><artifactId>lib</artifactId><version>1.0.0</version></dependency></dependencies></project>\n"),
        };
        foreach (var (name, content) in manifests)
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            Assert.True(PluginServices.IsManifestFile(tab), name);
            await rig.Until(() => rig.App.Plugins.SchemaResultOf(tab) is not null, "the check of " + name);
            rig.Dialogs.Answers.Enqueue(1);
            await rig.Window.PackageLookupAsync(tab, refresh: false);
        }
        Assert.Equal(manifests.Length, rig.Dialogs.Asked.Count);
        Assert.False(File.Exists(rig.App.Plugins.Registries.FilePath));

        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
    }

    [AvaloniaFact]
    public async Task A_preview_of_Markdown_and_HTML_with_remote_pictures_connects_nowhere_and_offers_to_load_instead()
    {
        using var rig = new NetworkRig();
        foreach (var (name, content) in Corpus().Where(c => c.Name is "readme.md" or "page.html"))
        {
            rig.Window.OpenFile(rig.ProjectFile(name, content), null);
            rig.Window.ShowLivePreview(outline: false);
            await rig.Settle(700);
            rig.Window.UpdateLayout();
            var body = rig.Window.PreviewBody!;
            Assert.Empty(new[] { body }.Concat(body.GetLogicalDescendants().OfType<Control>()).OfType<Image>());
            if (name == "readme.md") Assert.NotEmpty(body.GetLogicalDescendants().OfType<Button>());
        }
        Assert.Empty(rig.Dialogs.Asked);

        AssertSilent(await rig.Net.FinishAsync(), rig);
    }

    [AvaloniaFact]
    public async Task Every_built_in_plugin_starts_and_idles_without_a_connection()
    {
        using var rig = new NetworkRig();
        var host = rig.App.Plugins.HostForTests;
        foreach (var activation in new[] { "onCommand:json", "onCommand:texttools", "onCommand:webdata", "onCommand:codeexplorer", "onCommand:emmet", "onCommand:formatters", "onCommand:git", "onCommand:lsp" })
            await host.ActivateAsync(activation);
        await rig.App.Plugins.ActivateSourcesAsync();
        await rig.Settle(2500);

        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        AssertWatched(report, [.. new[] { "Json", "TextTools", "WebData", "CodeExplorer", "Emmet", "Formatters", "Git", "Lsp", "Wiki" }.Select(n => "NksHub.Textida.Plugins." + n)]);
    }

    /// <summary>The fake server under the name of the C# server of the catalog, in a folder the user's tool path names; the editor starts it like the real one.</summary>
    private static string InstallFakeServer(NetworkRig rig)
    {
        var folder = Directory.CreateDirectory(Path.Combine(rig.Temp.Path, "lsp-tools")).FullName;
        const string name = "NksHub.Textida.FakeLsp";
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" }) File.Copy(Path.Combine(AppContext.BaseDirectory, name + extension), Path.Combine(folder, name + extension));
        var host = OperatingSystem.IsWindows() ? ".exe" : "";
        File.Copy(Path.Combine(AppContext.BaseDirectory, name + host), Path.Combine(folder, "Microsoft.CodeAnalysis.LanguageServer" + host));
        SaveActionConfig.Write(Path.Combine(rig.App.ProfileDirectory, SaveActionConfig.FileName), new SaveActionsFile { ToolPaths = new Dictionary<string, string> { ["lsp:csharp"] = folder } });
        return folder;
    }

    [AvaloniaFact]
    public async Task A_running_language_server_is_started_without_telemetry_and_neither_it_nor_its_client_makes_a_connection()
    {
        using var rig = new NetworkRig();
        InstallFakeServer(rig);
        var argv = Path.Combine(rig.Temp.Path, "argv.txt");
        Environment.SetEnvironmentVariable("TEXTIDA_FAKELSP_ARGV", argv);
        try
        {
            var tab = rig.Window.OpenFile(rig.ProjectFile("src/Program.cs", "class C { void M() { } }\n"), null);
            Assert.True(await rig.App.Plugins.RunLspAsync(rig.Window, tab, "lsp.start"));
            await rig.Until(() => File.Exists(argv), "the language server to start");
            tab.Editor!.Document.Insert(0, "// edited\n");
            await rig.Settle(2500);
        }
        finally { Environment.SetEnvironmentVariable("TEXTIDA_FAKELSP_ARGV", null); }

        Assert.Contains("--telemetryLevel\noff", File.ReadAllText(argv));
        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        AssertWatched(report, "NksHub.Textida.Plugins.Lsp", "Microsoft.CodeAnalysis.LanguageServer");
    }

    [AvaloniaFact]
    public async Task The_MCP_server_the_IDE_bridge_the_agent_panel_and_the_list_of_agent_servers_idle_without_a_connection()
    {
        using var rig = new NetworkRig();
        var home = Directory.CreateDirectory(Path.Combine(rig.Temp.Path, "home")).FullName;
        File.WriteAllText(Path.Combine(home, ".claude.json"), """{ "mcpServers": { "remote": { "type": "http", "url": "https://mcp.example.test/mcp" } } }""");
        rig.App.AgentConfigHome = home;
        var mcp = rig.App.Plugins.Mcp;
        mcp.IdeLockDirectory = Path.Combine(rig.Temp.Path, "ide");
        mcp.Enable();
        mcp.EnableIde(rig.Window);
        var bridge = mcp.IdeOf(rig.Window)!;
        rig.Net.AllowLoopback(bridge.Port, "IDE bridge of the editor, the door of a CLI agent");
        using (var agent = new System.Net.Sockets.TcpClient()) await agent.ConnectAsync(System.Net.IPAddress.Loopback, bridge.Port);

        await rig.App.Workbench.Commands.ExecuteAsync("nks.tools.mcpServers", new ShellContext(rig.Window, forMenu: false)).AsTask();
        await rig.Until(() => rig.Window.IsPaletteOpen && rig.Window.PaletteModeNow == PaletteMode.Choice && rig.Window.QuickResults.Count == 1, "the list of servers");
        rig.Window.ClosePalette();
        await rig.Until(() => rig.Window.McpTask.IsCompleted, "the list to end");
        await rig.Settle(2000);

        Assert.False(rig.App.Plugins.Acp.IsRunning);
        Assert.Empty(rig.Dialogs.Asked);
        var report = await rig.Net.FinishAsync();
        AssertSilent(report, rig);
        Assert.Contains(report.Ipc, e => e.Target == $"127.0.0.1:{bridge.Port}");
        await mcp.DisableAsync();
    }

    private static string FeedFor(string version, out string publicKey)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        return AppUpdater.CreateFeed(key, new UpdateRelease(version, AppUpdater.CurrentRid, "p.zip", new string('0', 64), 10));
    }

    /// <summary>An install folder the updater accepts and a feed on a loopback address that nothing listens on: the policy proxy is the only way to it.</summary>
    internal static AppUpdater UpdaterOf(NetworkRig rig, string feedAuthority, out string feed)
    {
        var dir = Path.Combine(rig.Temp.Path, "Textida");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Textida.exe"), "");
        File.WriteAllText(Path.Combine(dir, "Setup.ps1"), "");
        File.WriteAllText(Path.Combine(dir, "version.txt"), "1.0.0");
        feed = FeedFor("1.1.0", out var publicKey);
        return new AppUpdater(rig.Window.CreateUpdateHttpClient(), $"http://{feedAuthority}/feed.json", publicKey, dir);
    }

    [AvaloniaFact]
    public async Task With_the_update_check_off_the_feed_is_not_asked_at_start_or_later()
    {
        using var rig = new NetworkRig();
        rig.Window.UpdaterOverride = UpdaterOf(rig, "127.0.0.1:9", out _);
        Assert.False(rig.App.Workbench.Settings.GetBool("updates.checkOnStart"));

        rig.StartLikeTheProgram();
        rig.Window.CheckUpdatesOnStart();
        await rig.Window.UpdateTask;
        await rig.Settle(1000);

        AssertSilent(await rig.Net.FinishAsync(), rig);
    }
}

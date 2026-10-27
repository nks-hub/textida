using System.Text.Json;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.Rpc.Tests;

public class PluginManifestTests
{
    const long MaxGrammarBytes = 1 << 20;

    const string DemoGrammar = "{\"formatVersion\":1,\"id\":\"demo-lang\",\"name\":\"Demo\",\"extensions\":[\".demo\"],\"rules\":[{\"match\":\"#.*\",\"token\":\"comment\"}]}";

    static string Plugin(string manifest, params (string Path, string Text)[] files)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest);
        foreach (var (path, text) in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, path))!);
            File.WriteAllText(Path.Combine(dir, path), text);
        }
        return dir;
    }

    static PluginLoadResult Load(string dir) => PluginManifestLoader.Load(dir, 1, MaxGrammarBytes);

    const string Header = "\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"Demo\",\"version\":\"1.2.3\",\"api\":{\"min\":1,\"max\":1}";

    static string DataManifest(string extra = "") =>
        "{" + Header + ",\"contributes\":{\"commands\":[{\"id\":\"acme.demo.hello\",\"title\":\"Hello\"}]," +
        "\"clips\":[{\"id\":\"hello\",\"title\":\"Hello\",\"text\":\"hello\",\"command\":\"acme.demo.hello\"}]," +
        "\"grammars\":[\"demo.grammar.json\"]" + extra + "}}";

    [Fact]
    public void ADataPluginAddsAGrammarAndACommandAndStartsNoProcess()
    {
        var dir = Plugin(DataManifest(), ("demo.grammar.json", DemoGrammar));
        var result = Load(dir);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var plugin = result.Plugin!;
        Assert.Null(plugin.Spec);

        var commands = new CommandRegistry();
        var grammars = new GrammarRegistry();
        Assert.Empty(plugin.Install(commands, grammars));
        Assert.True(commands.TryGet("acme.demo.hello", out var command));
        Assert.Equal("acme.demo", command.Source);
        Assert.Equal("demo-lang", grammars.ForFile("notes.demo")?.Id);
        Assert.Equal(new DataAction("clip", "hello"), plugin.CommandData["acme.demo.hello"]);
        Assert.Null(BuiltinGrammars.Registry.Find("demo-lang"));

        var again = plugin.Install(commands, new GrammarRegistry());
        Assert.NotEmpty(again);
    }

    [Fact]
    public void ALogPatternInMaskSyntaxWithLevelsAndAToolThatUsesItAreAccepted()
    {
        var manifest = "{" + Header + ",\"contributes\":{\"logPatterns\":[{\"id\":\"gcc\",\"title\":\"gcc\",\"syntax\":\"mask\",\"pattern\":\"%F:%L:%C:*\"," +
            "\"levels\":{\"error\":\"\\\\|E\\\\|\"}}]," +
            "\"externalTools\":[{\"id\":\"build\",\"title\":\"Build\",\"executable\":\"make\",\"args\":[\"-j\"],\"timeoutSeconds\":60,\"logPattern\":\"gcc\"}]}}";
        var result = Load(Plugin(manifest));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var contributes = result.Plugin!.Manifest.Contributes;
        Assert.Equal("mask", contributes.LogPatterns.Single().Syntax);
        Assert.Equal((60, "gcc"), (contributes.ExternalTools.Single().TimeoutSeconds, contributes.ExternalTools.Single().LogPattern));
    }

    [Theory]
    [InlineData("{ not json", "Invalid")]
    [InlineData("{\"schemaVersion\":1,\"surprise\":true}", "Invalid")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"nks.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}}", "nks.")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"one\",\"api\":{\"min\":1,\"max\":1}}", "version")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":2,\"max\":3}}", "provides 1")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"commands\":[{\"id\":\"other.cmd\",\"title\":\"x\"}]}}", "start with 'acme.demo.'")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"commands\":[{\"id\":\"acme.demo.a\",\"title\":\"x\"}]}}", "runs nothing")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"grammars\":[\"missing.json\"]}}", "does not exist")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"grammars\":[\"..\\\\..\\\\outside.json\"]}}", "leaves the plugin folder")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"logPatterns\":[{\"id\":\"p\",\"title\":\"P\",\"pattern\":\"(\"}]}}", "regular expression")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"logPatterns\":[{\"id\":\"p\",\"title\":\"P\",\"syntax\":\"mask\",\"pattern\":\"%F\"}]}}", "needs %L")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"logPatterns\":[{\"id\":\"p\",\"title\":\"P\",\"syntax\":\"glob\",\"pattern\":\"x\"}]}}", "syntax must be")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"logPatterns\":[{\"id\":\"p\",\"title\":\"P\",\"pattern\":\"(?<line>[0-9]+)\",\"levels\":{\"fatal\":\"x\"}}]}}", "Unknown level")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"externalTools\":[{\"id\":\"t\",\"title\":\"T\",\"executable\":\"x\",\"logPattern\":\"nope\"}]}}", "unknown log pattern")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"contributes\":{\"externalTools\":[{\"id\":\"t\",\"title\":\"T\",\"executable\":\"x\",\"timeoutSeconds\":0}]}}", "time limit")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"main\":{\"executable\":\"python\",\"args\":[\"x.py\"]},\"contributes\":{\"commands\":[{\"id\":\"acme.demo.a\",\"title\":\"x\"}]}}", "bare name")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"main\":{\"executable\":\"./nothing.exe\"},\"contributes\":{\"commands\":[{\"id\":\"acme.demo.a\",\"title\":\"x\"}]}}", "does not exist")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"acme.demo\",\"name\":\"D\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"main\":{\"executable\":\"./run.cmd\",\"activationEvents\":[\"sometime\"]}}", "Activation event")]
    public void AnInvalidManifestIsRefusedWithAReason(string manifest, string expected)
    {
        var dir = Plugin(manifest, ("run.cmd", ""));
        var result = Load(dir);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AClipWithAFormAndATemplateLoadAndTheirCommandsRunData()
    {
        var manifest = "{" + Header + ",\"contributes\":{\"commands\":[{\"id\":\"acme.demo.box\",\"title\":\"Box\"},{\"id\":\"acme.demo.page\",\"title\":\"Page\"}]," +
            "\"clips\":[{\"id\":\"box\",\"title\":\"Box\",\"text\":\"<b>${c}$|</b>\",\"command\":\"acme.demo.box\",\"fields\":[{\"name\":\"c\",\"type\":\"color\",\"default\":\"#fff\"}]}]," +
            "\"templates\":[{\"id\":\"page\",\"title\":\"Page\",\"text\":\"<p>$|</p>\",\"command\":\"acme.demo.page\"}]}}";
        var result = Load(Plugin(manifest));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var plugin = result.Plugin!;
        Assert.Null(plugin.Spec);
        Assert.Equal("color", plugin.Manifest.Contributes.Clips[0].Fields[0].Type);
        Assert.Equal(new DataAction("clip", "box"), plugin.CommandData["acme.demo.box"]);
        Assert.Equal(new DataAction("template", "page"), plugin.CommandData["acme.demo.page"]);

        var twice = manifest.Replace("\"templates\":[", "\"templates\":[{\"id\":\"page\",\"title\":\"Other\",\"text\":\"x\"},");
        Assert.Contains(Load(Plugin(twice)).Errors, e => e.Contains("Duplicate template id"));
        var badText = manifest.Replace("<p>$|</p>", "<p>${</p>");
        Assert.Contains(Load(Plugin(badText)).Errors, e => e.Contains("template 'page'") && e.Contains("unclosed"));
    }

    [Fact]
    public void ABrokenGrammarOrOneThatClaimsABuiltInIdIsRefused()
    {
        var broken = Load(Plugin(DataManifest(), ("demo.grammar.json", "{\"formatVersion\":1,\"id\":\"demo-lang\",\"typo\":1}")));
        Assert.Contains(broken.Errors, e => e.Contains("demo.grammar.json"));
        var builtin = Load(Plugin(DataManifest(), ("demo.grammar.json", DemoGrammar.Replace("demo-lang", "json"))));
        Assert.Contains(builtin.Errors, e => e.Contains("built-in"));
        var tooBig = PluginManifestLoader.Load(Plugin(DataManifest(), ("demo.grammar.json", DemoGrammar)), 1, 10);
        Assert.Contains(tooBig.Errors, e => e.Contains("larger than 10"));
    }

    [Fact]
    public async Task AProcessPluginStartsOnTheFirstRunOfItsCommand()
    {
        var manifest = "{" + Header + ",\"main\":{\"executable\":" + JsonSerializer.Serialize(T.DotnetFullPath) + ",\"args\":[" + JsonSerializer.Serialize(T.PluginDll) + "]}," +
            "\"contributes\":{\"commands\":[{\"id\":\"acme.demo.run\",\"title\":\"Run\"}]}}";
        var result = Load(Plugin(manifest));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(["onCommand:acme.demo.run"], result.Plugin!.Spec!.ActivationEvents);

        var commands = new CommandRegistry();
        Assert.Empty(result.Plugin.Install(commands, new GrammarRegistry()));
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        result.Plugin.Attach(commands, host);

        Assert.Equal(PluginState.Inactive, host.GetState("acme.demo"));
        var outcome = await commands.ExecuteAsync("acme.demo.run", new WhenContext());
        Assert.Equal(CommandOutcome.Handled, outcome);
        Assert.Equal(PluginState.Running, host.GetState("acme.demo"));
    }

    [Fact]
    public async Task APythonPluginRunsThroughTheHost()
    {
        var python = Environment.GetEnvironmentVariable("TEXTIDA_PYTHON") ?? T.FindOnPath("python.exe") ?? T.FindOnPath("python3") ?? T.FindOnPath("python");
        Assert.True(python is not null && File.Exists(python), "No Python interpreter: set TEXTIDA_PYTHON to its full path.");

        var dir = T.TempDir();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples", "plugins", "python-hello")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        var manifestPath = Path.Combine(dir, PluginManifest.FileName);
        var text = File.ReadAllText(manifestPath);
        Assert.Contains("/path/to/python", text);
        File.WriteAllText(manifestPath, text.Replace("\"/path/to/python\"", JsonSerializer.Serialize(python)));

        var result = Load(dir);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var commands = new CommandRegistry();
        Assert.Empty(result.Plugin!.Install(commands, new GrammarRegistry()));
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        result.Plugin!.Attach(commands, host);

        Assert.Equal(CommandOutcome.Handled, await commands.ExecuteAsync("example.python-hello.greet", new WhenContext()));
        await T.Until(() => host.GetStderrTail("example.python-hello").Contains("executed example.python-hello.greet"));
    }

    const string GrammarOnly = "{" + Header + ",\"contributes\":{\"grammars\":[\"g.json\"]}}";

    [Theory]
    [InlineData("{\"formatVersion\":1,\"id\":\"bad-re\",\"rules\":[{\"match\":\"(\",\"token\":\"comment\"}]}", "bad-re")]
    [InlineData("{\"formatVersion\":1,\"id\":\"typo\",\"rules\":[{\"mach\":\"a\"}]}", "unknown property 'mach'")]
    [InlineData("{\"formatVersion\":1,\"id\":\"no-end\",\"rules\":[{\"begin\":\"a\"}]}", "end pattern")]
    [InlineData("{\"formatVersion\":1,\"id\":\"ghost\",\"rules\":[{\"begin\":\"<<\",\"end\":\">>\",\"embed\":\"no-such-lang\"}]}", "no-such-lang")]
    [InlineData("{\"formatVersion\":1,\"id\":\"ghost2\",\"rules\":[{\"include\":\"no-such-lang#x\"}]}", "no-such-lang")]
    [InlineData("{\"formatVersion\":1,\"id\":\"html\",\"rules\":[]}", "built-in")]
    public void AnInvalidHighlighterRuleIsRefusedWhenThePluginIsRead(string grammar, string expected)
    {
        var result = Load(Plugin(GrammarOnly, ("g.json", grammar)));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("Grammar 'g.json'") && e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AHighlighterMayEmbedABuiltInLanguageAndColoursWithoutAProcess()
    {
        const string embedding = "{\"formatVersion\":1,\"id\":\"tpl\",\"extensions\":[\".tpl\"],\"rules\":[{\"begin\":\"\\\\{js\\\\}\",\"end\":\"\\\\{/js\\\\}\",\"multiline\":true,\"embed\":\"javascript\"}]}";
        var result = Load(Plugin(GrammarOnly, ("g.json", embedding)));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Null(result.Plugin!.Spec);

        var registry = new GrammarRegistry(BuiltinGrammars.Registry);
        foreach (var json in result.Plugin.GrammarJson) registry.Add(json);
        var tokenizer = new Tokenizer(registry.Find("tpl")!);
        var tokens = new List<Token>();
        tokenizer.TokenizeLine("{js} var a = 1; {/js}", tokenizer.InitialState, tokens);
        Assert.Contains(tokens, t => t.Kind == "keyword");
        Assert.Null(BuiltinGrammars.Registry.Find("tpl"));
    }
}

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
}

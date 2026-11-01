using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>PZ.8.4: the hex contributions of a plugin manifest are read and checked, start the plugin on use, and a real Python process answers the four requests.</summary>
public class HexPluginManifestTests
{
    static readonly string MainPart = ",\"main\":{\"executable\":" + JsonSerializer.Serialize(T.DotnetFullPath) + ",\"args\":[]}";
    static readonly string Header = "\"schemaVersion\":1,\"id\":\"acme.hex\",\"name\":\"Hex\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[\"hex.read\"]" + MainPart;

    static PluginLoadResult Load(string manifest)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest);
        return PluginManifestLoader.Load(dir, 1, 1 << 20);
    }

    static string Manifest(string contributes, string? header = null) => "{" + (header ?? Header) + ",\"contributes\":{" + contributes + "}}";

    [Fact]
    public void Every_kind_is_read_and_starts_the_plugin_by_its_own_event()
    {
        var result = Load(Manifest("\"hexDecoders\":[{\"id\":\"bcd\",\"title\":\"BCD\",\"length\":2}],\"hexHashes\":[{\"id\":\"h\",\"title\":\"H\"}],\"hexExporters\":[{\"id\":\"c\",\"title\":\"C\",\"extension\":\".c\"}],\"hexTemplates\":[{\"id\":\"t\",\"title\":\"T\"}]"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var c = result.Plugin!.Manifest.Contributes;
        Assert.Equal(("bcd", "BCD", 2), (c.HexDecoders[0].Id, c.HexDecoders[0].Title, c.HexDecoders[0].Length));
        Assert.Equal(".c", c.HexExporters[0].Extension);
        Assert.Equal(["onHex:decode", "onHex:hash", "onHex:export", "onHex:templates"], result.Plugin.Spec!.ActivationEvents);
    }

    [Theory]
    [InlineData("\"hexDecoders\":[{\"id\":\"BCD\",\"title\":\"x\"}]", "id must be")]
    [InlineData("\"hexDecoders\":[{\"id\":\"a\",\"title\":\"\"}]", "title must be")]
    [InlineData("\"hexDecoders\":[{\"id\":\"a\",\"title\":\"x\",\"length\":0}]", "length must be")]
    [InlineData("\"hexDecoders\":[{\"id\":\"a\",\"title\":\"x\",\"length\":65}]", "length must be")]
    [InlineData("\"hexDecoders\":[{\"id\":\"a\",\"title\":\"x\"},{\"id\":\"a\",\"title\":\"y\"}]", "Duplicate")]
    [InlineData("\"hexExporters\":[{\"id\":\"a\",\"title\":\"x\",\"extension\":\"c\"}]", "extension must be")]
    [InlineData("\"hexExporters\":[{\"id\":\"a\",\"title\":\"x\",\"extension\":\".c/../x\"}]", "extension must be")]
    public void A_bad_entry_refuses_the_manifest_with_a_reason(string contributes, string expected)
    {
        var result = Load(Manifest(contributes));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_the_permission_or_without_a_process_the_contributions_are_refused()
    {
        const string one = "\"hexHashes\":[{\"id\":\"h\",\"title\":\"H\"}]";
        var noPermission = Load(Manifest(one, Header.Replace(",\"permissions\":[\"hex.read\"]", "")));
        Assert.False(noPermission.Succeeded);
        Assert.Contains(noPermission.Errors, e => e.Contains("hex.read"));

        var noProcess = Load(Manifest(one, Header.Replace(MainPart, "")));
        Assert.False(noProcess.Succeeded);
        Assert.Contains(noProcess.Errors, e => e.Contains("main"));
    }

    [Fact]
    public async Task A_real_Python_plugin_answers_decode_hash_export_and_templates()
    {
        var python = Environment.GetEnvironmentVariable("TEXTIDA_PYTHON") ?? T.FindOnPath("python.exe") ?? T.FindOnPath("python3") ?? T.FindOnPath("python");
        Assert.True(python is not null && File.Exists(python), "No Python interpreter: set TEXTIDA_PYTHON to its full path.");
        var dir = T.TempDir();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples", "plugins", "python-hex")))
            File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
        var manifestPath = Path.Combine(dir, PluginManifest.FileName);
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath).Replace("\"/path/to/python\"", JsonSerializer.Serialize(python)));

        var result = PluginManifestLoader.Load(dir, 1, 1 << 20);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var commands = new CommandRegistry();
        Assert.Empty(result.Plugin!.Install(commands, new GrammarRegistry()));
        await using var host = new PluginHost.PluginHost(PluginHostTests.Options());
        result.Plugin.Attach(commands, host);
        Assert.Equal(PluginState.Inactive, host.GetState("example.python-hex"));
        Assert.Equal(["example.python-hex"], await host.ActivateAsync("onHex:decode"));

        static byte[] Request(string name, string value, byte[] data) => Encoding.UTF8.GetBytes($"{{\"{name}\":\"{value}\",\"data\":\"{Convert.ToBase64String(data)}\"}}");
        var ok = await host.CallAsync("example.python-hex", HexMethods.Decode, Request("decoder", "bcd", [0x42]));
        Assert.Equal(("ok", "42"), (ok.GetProperty("status").GetString(), ok.GetProperty("text").GetString()));
        var invalid = await host.CallAsync("example.python-hex", HexMethods.Decode, Request("decoder", "bcd", [0xAB]));
        Assert.Equal("invalid", invalid.GetProperty("status").GetString());
        var none = await host.CallAsync("example.python-hex", HexMethods.Decode, Request("decoder", "bcd", []));
        Assert.Equal(("incomplete", 1), (none.GetProperty("status").GetString(), none.GetProperty("length").GetInt32()));

        var hash = await host.CallAsync("example.python-hex", HexMethods.Hash, Request("hash", "blake2b-128", Encoding.ASCII.GetBytes("abc")));
        Assert.Equal("cf4ab791c62b8d2b2109c90275287816", hash.GetProperty("value").GetString());

        var export = await host.CallAsync("example.python-hex", HexMethods.Export, Request("exporter", "c-array", [1, 255]));
        Assert.Equal("bytes.c", export.GetProperty("name").GetString());
        Assert.Contains("0xFF", export.GetProperty("text").GetString());

        var templates = await host.CallAsync("example.python-hex", HexMethods.Templates, Request("source", "samples", []));
        Assert.Equal("pair.ksy", templates.GetProperty("templates")[0].GetProperty("name").GetString());
    }
}

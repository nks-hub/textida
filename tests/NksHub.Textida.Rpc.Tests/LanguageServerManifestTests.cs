using System.Text.Json;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.5.2: language servers declared in a plugin manifest are read, checked and matched to documents.</summary>
public class LanguageServerManifestTests
{
    const string Header = "\"schemaVersion\":1,\"id\":\"acme.lang\",\"name\":\"Lang\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1},\"permissions\":[\"language.server\"]";

    static string Manifest(string servers, string header = Header) => "{" + header + ",\"contributes\":{\"languageServers\":[" + servers + "]}}";

    static PluginLoadResult Load(string manifest)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest);
        return PluginManifestLoader.Load(dir, 1, 1 << 20);
    }

    const string Good = "{\"id\":\"foo\",\"languages\":[\"foo\",\"foo-x\"],\"globs\":[\"*.foo\",\"src/**/*.fx\"],\"command\":\"foo-ls\",\"args\":[\"--stdio\"],\"initializationOptions\":{\"mode\":\"strict\"}}";

    [Fact]
    public void A_server_with_languages_globs_arguments_and_options_is_read()
    {
        var result = Load(Manifest(Good));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var s = result.Plugin!.Manifest.Contributes.LanguageServers.Single();
        Assert.Equal(["foo", "foo-x"], s.Languages);
        Assert.Equal(["*.foo", "src/**/*.fx"], s.Globs);
        Assert.Equal(("foo-ls", "path"), (s.Command, s.Locate));
        Assert.Equal(["--stdio"], s.Args);
        Assert.Equal("strict", s.InitializationOptions!.Value.GetProperty("mode").GetString());
    }

    [Theory]
    [InlineData("{\"id\":\"Foo\",\"languages\":[\"foo\"],\"command\":\"x\"}", "id must be")]
    [InlineData("{\"id\":\"foo\",\"command\":\"x\"}", "at least one language")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"Foo!\"],\"command\":\"x\"}", "language mode id")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"globs\":[\"\"],\"command\":\"x\"}", "glob")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"]}", "command is empty")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"command\":\"bin/x\"}", "bare program name")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"command\":\"x\",\"locate\":\"somewhere\"}", "locate must be")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"command\":\"..\\\\..\\\\x.exe\",\"locate\":\"plugin\"}", "leaves the plugin folder")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"command\":\"x\",\"initializationOptions\":[1]}", "must be an object")]
    [InlineData("{\"id\":\"foo\",\"languages\":[\"foo\"],\"command\":\"x\"},{\"id\":\"foo\",\"languages\":[\"bar\"],\"command\":\"y\"}", "Duplicate language server")]
    public void A_bad_entry_refuses_the_whole_manifest_with_a_reason(string servers, string expected)
    {
        var result = Load(Manifest(servers));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_server_without_the_permission_is_refused()
    {
        var result = Load(Manifest(Good, Header.Replace(",\"permissions\":[\"language.server\"]", "")));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("language.server"));
    }

    [Fact]
    public void A_server_inside_the_plugin_folder_is_started_by_its_full_path_and_one_on_the_path_by_its_name()
    {
        var result = Load(Manifest("{\"id\":\"a\",\"languages\":[\"a\"],\"command\":\"bin/a-ls.exe\",\"locate\":\"plugin\"},{\"id\":\"b\",\"languages\":[\"b\"],\"command\":\"b-ls\"}"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var plugin = result.Plugin!;
        var a = new DeclaredServer("acme.lang", plugin.Directory, plugin.Manifest.Contributes.LanguageServers[0]);
        var b = new DeclaredServer("acme.lang", plugin.Directory, plugin.Manifest.Contributes.LanguageServers[1]);
        Assert.Equal(Path.Combine(plugin.Directory, "bin", "a-ls.exe"), a.Program);
        Assert.Equal("b-ls", b.Program);

        using var launch = JsonDocument.Parse(b.ToLaunchJson("b"));
        Assert.Equal(("b", "b-ls"), (launch.RootElement.GetProperty("language").GetString(), launch.RootElement.GetProperty("program").GetString()));
        Assert.False(launch.RootElement.TryGetProperty("initializationOptions", out _));
    }

    static DeclaredServer Server(string id, string[] languages, string[] globs) =>
        new("acme.lang", Path.GetTempPath(), new LanguageServerContribution { Id = id, Languages = [.. languages], Globs = [.. globs], Command = id + "-ls" });

    [Theory]
    [InlineData("*.foo", "C:\\work\\a\\x.foo", true)]
    [InlineData("*.foo", "/home/u/x.FOO", true)]
    [InlineData("*.foo", "x.foobar", false)]
    [InlineData("*.foo", "dir.foo/x.txt", false)]
    [InlineData("Makefile.?", "src/Makefile.x", true)]
    [InlineData("Makefile.?", "src/Makefile.xy", false)]
    [InlineData("src/**/*.fx", "C:\\p\\src\\a\\b\\c.fx", true)]
    [InlineData("src/**/*.fx", "C:\\p\\other\\c.fx", false)]
    [InlineData("src/**/*.fx", "p/src/c.fx", true)]
    [InlineData("src/**/*.fx", "p/mysrc/c.fx", false)]
    [InlineData("src/*.fx", "p/src/a/c.fx", false)]
    [InlineData("a+b(1).txt", "a+b(1).txt", true)]
    public void A_glob_matches_the_file_name_or_the_full_path(string glob, string path, bool expected) =>
        Assert.Equal(expected, LanguageServerMatcher.MatchesGlob(glob, path));

    [Fact]
    public void A_document_gets_the_first_server_that_lists_its_language_or_matches_a_glob()
    {
        var foo = Server("foo", ["foo", "foo-x"], ["*.fx"]);
        var bar = Server("bar", ["bar"], ["*.bar", "*.fx"]);
        DeclaredServer[] all = [foo, bar];

        Assert.Same(foo, LanguageServerMatcher.Find(all, "foo-x", null));
        Assert.Same(bar, LanguageServerMatcher.Find(all, "bar", "C:\\a\\x.txt"));
        Assert.Same(bar, LanguageServerMatcher.Find(all, "plaintext", "C:\\a\\x.bar"));
        Assert.Same(foo, LanguageServerMatcher.Find(all, "plaintext", "C:\\a\\x.fx"));
        Assert.Null(LanguageServerMatcher.Find(all, "plaintext", "C:\\a\\x.txt"));
        Assert.Null(LanguageServerMatcher.Find(all, "plaintext", null));

        // a document that matches only by glob is served for the first language of the server, so all of them share one process
        Assert.Equal("foo", foo.LanguageFor("plaintext"));
        Assert.Equal("foo-x", foo.LanguageFor("foo-x"));
    }
}

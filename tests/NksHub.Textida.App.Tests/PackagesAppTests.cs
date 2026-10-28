using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.7.3 in the app: the consent for a registry, and a manifest script through the tool runner. The registry requests themselves are tested with the plugin.</summary>
[Collection("LocState")]
public class PackagesAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PackagesAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        _w.StopTool();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string Manifest(string name, string content)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "proj")).FullName;
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void Finish(Task task) => ToolTestKit.WaitFor(() => task.IsCompleted, "the task did not finish");

    [AvaloniaFact]
    public void A_registry_is_asked_about_only_after_the_user_agreed_and_a_refusal_leaves_nothing_behind()
    {
        string path = Manifest("package.json", "{ \"dependencies\": { \"lodash\": \"4.17.0\" } }");
        _w.OpenFile(path, null);

        _dialogs.Answers.Enqueue(1);
        Finish(_w.PackageLookupAsync(_w.Active!, refresh: false));

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.Contains("registry.npmjs.org", StringComparison.Ordinal));
        Assert.False(_app.Plugins.Registries.IsAllowed("npm"));
        Assert.False(File.Exists(_app.Plugins.Registries.FilePath));
    }

    [AvaloniaFact]
    public void A_file_that_is_no_manifest_is_refused_with_a_message()
    {
        _w.OpenFile(Manifest("notes.txt", "x"), null);

        _w.PackageLookup(refresh: false);
        Assert.Equal(Loc.Get(K.PackagesNotManifest), _w.StatusMessageText);
        _w.ShowPackageScripts();
        Assert.Equal(Loc.Get(K.PackagesScriptNotManifest), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void The_consent_is_a_file_the_plugin_reads_and_a_revoke_empties_it()
    {
        var consent = _app.Plugins.Registries;

        consent.Allow("npm");
        consent.Allow("pypi");
        consent.Allow("npm");
        Assert.Equal(["npm", "pypi"], JsonNode.Parse(File.ReadAllText(consent.FilePath))!["allowed"]!.AsArray().Select(n => (string)n!));
        Assert.True(consent.IsAllowed("pypi"));
        Assert.False(consent.IsAllowed("nuget"));

        consent.RevokeAll();
        Assert.False(consent.IsAllowed("npm"));
        Assert.Empty(JsonNode.Parse(File.ReadAllText(consent.FilePath))!["allowed"]!.AsArray());
    }

    [AvaloniaFact]
    public void The_proxy_of_the_network_policy_is_passed_on_and_one_with_a_login_stops_the_lookups()
    {
        var consent = _app.Plugins.Registries;

        consent.Sync(new NetworkPolicy(new ProxySettings(ProxyType.Socks5, "proxy.example", 1080), null));
        var file = JsonNode.Parse(File.ReadAllText(consent.FilePath))!;
        Assert.Equal("socks5://proxy.example:1080", (string?)file["proxy"]);
        Assert.False((bool)file["blocked"]!);

        consent.Sync(new NetworkPolicy(new ProxySettings(ProxyType.Http, "proxy.example", 3128, "user"), "secret"));
        file = JsonNode.Parse(File.ReadAllText(consent.FilePath))!;
        Assert.True((bool)file["blocked"]!);
        Assert.DoesNotContain("secret", File.ReadAllText(consent.FilePath), StringComparison.Ordinal);

        consent.Sync(null); // unreadable policy: do not go direct
        Assert.True((bool)JsonNode.Parse(File.ReadAllText(consent.FilePath))!["blocked"]!);
        consent.Sync(NetworkPolicy.Direct);
        file = JsonNode.Parse(File.ReadAllText(consent.FilePath))!;
        Assert.False((bool)file["blocked"]!);
        Assert.Null(file["proxy"]);
    }

    [AvaloniaFact]
    public void A_script_runs_through_the_runner_after_a_confirmation_that_is_tied_to_its_text()
    {
        string path = Manifest("package.json", "{ \"scripts\": { \"build\": \"tsc -p .\" } }");
        var script = PackageManifests.Scripts(path, File.ReadAllText(path)).Single();
        Assert.Equal("npm", script.Executable);
        Assert.Equal(["run", "build"], script.Args);
        _dialogs.Answers.Enqueue(0);

        Finish(_w.RunPackageScriptAsync(path, script, ToolTestKit.Dotnet, [ToolTestKit.EchoDll]));

        var asked = Assert.Single(_dialogs.Asked);
        Assert.Contains(asked.Details!, d => d.Contains("run", StringComparison.Ordinal) && d.Contains("build", StringComparison.Ordinal));
        Assert.Contains(asked.Details!, d => d.Contains(path, StringComparison.Ordinal)); // the file the script comes from
        var lines = _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Contains("[run]", lines);
        Assert.Contains("[build]", lines);

        Finish(_w.RunPackageScriptAsync(path, script, ToolTestKit.Dotnet, [ToolTestKit.EchoDll]));
        Assert.Single(_dialogs.Asked); // the same text is not asked again

        File.WriteAllText(path, "{ \"scripts\": { \"build\": \"tsc --noEmit\" } }");
        var changed = PackageManifests.Scripts(path, File.ReadAllText(path)).Single();
        _dialogs.Answers.Enqueue(1);
        Finish(_w.RunPackageScriptAsync(path, changed, ToolTestKit.Dotnet, [ToolTestKit.EchoDll]));
        Assert.Equal(2, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public void A_schema_host_is_allowed_in_the_same_file_by_its_own_id_and_the_one_revoke_takes_it_back_with_the_registries()
    {
        var consent = _app.Plugins.Registries;
        Assert.Equal("schemas:json.schemastore.org", PackageRegistryConsent.SchemaId("JSON.SchemaStore.org"));
        Assert.False(consent.AllowsAnySchema());

        consent.Allow("npm");
        Assert.False(consent.AllowsAnySchema()); // a registry is no schema host
        consent.Allow(PackageRegistryConsent.SchemaId("json.schemastore.org"));

        Assert.True(consent.AllowsAnySchema());
        Assert.True(consent.IsAllowed("schemas:json.schemastore.org"));
        Assert.False(consent.IsAllowed("schemas:example.test")); // another host is asked about separately
        _w.PackageRevoke();
        Assert.False(consent.AllowsAnySchema());
        Assert.False(consent.IsAllowed("npm"));
        Assert.Equal(Loc.Get(K.PackagesRevoked), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void The_consent_command_for_a_schema_refuses_a_file_that_waits_for_nothing_and_asks_nothing()
    {
        _w.OpenFile(Manifest("notes.txt", "x"), null);
        _w.SchemaAllowDownload();
        Assert.Equal(Loc.Get(K.SchemaNotFile), _w.StatusMessageText);

        var tab = _w.OpenFile(Manifest("plain.json", "{ \"a\": 1 }"), null);
        Finish(_w.SchemaAllowDownloadAsync(tab)); // it has not been checked, and a schema that is not wanted waits for no consent
        Assert.Equal(Loc.Get(K.SchemaNothingToAllow), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void The_status_text_of_a_schema_says_what_became_of_it_in_every_language()
    {
        var ready = new PluginServices.SchemaResult(1, "thing.json", "ready", null, false, false);
        foreach (var language in new[] { "cs", "en", "sk", "de" })
        {
            Loc.SetLanguage(language);
            var texts = new[]
            {
                PluginServices.SchemaStatusText(ready),
                PluginServices.SchemaStatusText(ready with { Cached = true }),
                PluginServices.SchemaStatusText(ready with { Cached = true, Offline = true }),
                PluginServices.SchemaStatusText(ready with { State = "noConsent", Host = "example.test" }),
                PluginServices.SchemaStatusText(ready with { State = "unavailable" }),
                PluginServices.SchemaStatusText(ready with { State = "unsupported" }),
                PluginServices.SchemaStatusText(ready with { Name = null }),
            };
            Assert.All(texts, t => Assert.DoesNotContain("{", t, StringComparison.Ordinal));
            Assert.Equal(texts.Length - 1, texts.Take(texts.Length - 1).Distinct().Count()); // each state reads differently
            Assert.All(texts.Take(texts.Length - 1), t => Assert.Contains("thing.json", t, StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public void The_commands_are_in_the_tools_menu()
    {
        var ids = _w.MenuModel.Single(g => g.Label == "Nástroje").Items.AllCommands().Select(c => c.CommandId).ToList();
        Assert.Contains("nks.packages.lookup", ids);
        Assert.Contains("nks.packages.refresh", ids);
        Assert.Contains("nks.packages.revoke", ids);
        Assert.Contains("nks.packages.script", ids);
        Assert.Contains("nks.schemas.allow", ids);
    }
}

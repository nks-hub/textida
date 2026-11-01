using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.10.1: the plugin processes the host starts run under the operating system limit when the user switched it on, inside the job that ends all children with the editor.</summary>
[Collection("LocState")]
public class PluginSandboxAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly string? _variable = Environment.GetEnvironmentVariable(PluginServices.SandboxVariable);

    public PluginSandboxAppTests()
    {
        Environment.SetEnvironmentVariable(PluginServices.SandboxVariable, null);
        _app = TestApp.Prepare(_temp);
        _app.CreateWindow(null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(PluginServices.SandboxVariable, _variable);
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "A call did not finish.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        return await task;
    }

    /// <summary>Starts the test plugin as <paramref name="id"/> and asks it to start a command shell; returns what it answered.</summary>
    private async Task<string> SpawnFromAsync(string id)
    {
        var host = _app.Plugins.HostForTests;
        host.Register(new PluginSpec(id, id, Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll")], ["onCommand:" + id]));
        await Pump(host.ActivateAsync("onCommand:" + id));
        var args = JsonSerializer.Serialize(new Dictionary<string, string> { ["program"] = Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["args"] = "/c exit 0" });
        return (await Pump(host.CallAsync(id, "spawn", Encoding.UTF8.GetBytes(args)))).GetString()!;
    }

    [AvaloniaFact]
    public async Task A_plugin_starts_programs_as_before_and_under_the_switch_it_cannot_unless_it_is_one_that_must()
    {
        if (!OperatingSystem.IsWindows()) return; // the job object limit is Windows only; the macOS wrapper is checked as text in the Core tests

        Assert.Equal("started", await SpawnFromAsync("free"));

        _app.Workbench.Set(PluginServices.SandboxSetting, JsonValue.Create(true));
        Assert.StartsWith("refused", await SpawnFromAsync("locked"));
        Assert.Equal(ProcessSandboxLevel.Strict, _app.Plugins.SandboxOf(new PluginSpec("acme.any", "x", "x", [], [])));
        Assert.Equal(ProcessSandboxLevel.Contained, _app.Plugins.SandboxOf(new PluginSpec(PluginServices.GitPluginId, "Git", "x", [], [])));
    }

    [AvaloniaFact]
    public async Task The_environment_variable_sets_the_level_for_every_plugin_and_wins_over_the_setting()
    {
        if (!OperatingSystem.IsWindows()) return;

        Environment.SetEnvironmentVariable(PluginServices.SandboxVariable, "contained");
        Assert.Equal("started", await SpawnFromAsync("contained")); // contained plugins may start programs
        Assert.Equal(ProcessSandboxLevel.Contained, _app.Plugins.SandboxOf(new PluginSpec("acme.any", "x", "x", [], [])));

        Environment.SetEnvironmentVariable(PluginServices.SandboxVariable, "strict");
        Assert.StartsWith("refused", await SpawnFromAsync("strict"));

        Environment.SetEnvironmentVariable(PluginServices.SandboxVariable, "off");
        _app.Workbench.Set(PluginServices.SandboxSetting, JsonValue.Create(true));
        Assert.Equal(ProcessSandboxLevel.Off, _app.Plugins.SandboxOf(new PluginSpec("acme.any", "x", "x", [], [])));
    }
}

using System.Diagnostics;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Remote.Tests;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// An editor under network watch: a throw-away profile, scripted dialogs, a credential store in memory and the sinkhole proxy as the network proxy of the policy.
/// With the proxy set, anything that obeys the policy shows up in <see cref="NetworkHarness.Sinkhole"/>, and anything that does not shows up as a connect somewhere else.
/// </summary>
internal sealed class NetworkRig : IDisposable
{
    public TempDir Temp { get; } = new();
    public ScriptedDialogs Dialogs { get; } = new();
    public NetworkHarness Net { get; }
    public App App { get; }
    public MainWindow Window { get; }
    public MemoryCredentials Secrets { get; } = new();

    /// <summary>The harness is built first: the environment of the plugin processes is fixed when the first one starts.</summary>
    /// <param name="proxy">False leaves the policy without a proxy, as it is until the user sets one.</param>
    public NetworkRig(bool proxy = true)
    {
        Net = new NetworkHarness();
        App = TestApp.Prepare(Temp, Dialogs);
        App.Credentials = Secrets;
        App.Remote = new RemoteServices(App.ProfileDirectory, Secrets);
        if (proxy) NetworkPolicy.Save(App.Remote.NetworkPolicyPath, Net.Sinkhole.Settings, null, Secrets);
        Window = App.CreateWindow(null);
    }

    public string ProjectFile(string relativePath, string content)
    {
        var path = Path.Combine(Temp.Path, "proj", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>What <c>OnFrameworkInitializationCompleted</c> does when the first window is shown, in the same order.</summary>
    public void StartLikeTheProgram()
    {
        Window.RestoreFrom(App.Journal);
        Window.ShowJournalWarnings();
        Window.ShowConfigRestore();
        Window.StartWorkspace();
        App.Plugins.ActivateStartup();
        Window.CheckUpdatesOnStart();
        if (Window.Active is null) Window.NewDocument();
    }

    public async Task Until(Func<bool> condition, string what, int seconds = 60)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(seconds), "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    /// <summary>Awaits a call into a plugin while the UI thread keeps serving the calls the plugin makes back.</summary>
    public async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call to a plugin");
        return await task;
    }

    public async Task Pump(Task task)
    {
        await Until(() => task.IsCompleted, "a call to a plugin");
        await task;
    }

    /// <summary>Starts the test plugin as plugin <c>p</c> (a separate process speaking the plugin protocol) and returns a caller for its requests.</summary>
    public async Task<Func<string, string, Task<System.Text.Json.JsonElement>>> StartTestPluginAsync()
    {
        var host = App.Plugins.HostForTests;
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.PluginHost.TestPlugin.dll");
        host.Register(new NksHub.Textida.PluginHost.PluginSpec("p", "Test plugin", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", [dll], ["onCommand:p"]));
        await Pump(host.ActivateAsync("onCommand:p"));
        return async (method, json) => (await Pump(host.CallAsync("p", method, System.Text.Encoding.UTF8.GetBytes(json)))).Clone();
    }

    /// <summary>Lets timers and background work run, so that what would have happened has happened.</summary>
    public async Task Settle(int milliseconds = 1000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    public void Dispose()
    {
        TestApp.Cleanup(App);
        Loc.SetLanguage(Loc.DefaultLanguage);
        Net.Dispose();
        Temp.Dispose();
    }
}

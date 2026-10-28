using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.NetWatch;

namespace NksHub.Textida.App.Tests;

/// <summary>What a run of the editor did on the network, sorted by what the test allowed.</summary>
/// <param name="Hooked">The processes that started with the hook and said so; a process missing here was not watched.</param>
/// <param name="Ipc">Local channels: Unix sockets, names sent to the resolver that are local, and loopback ports the test declared.</param>
/// <param name="Requests">Every HttpClient request that began, anywhere in the editor or its plugins.</param>
/// <param name="Forbidden">Everything else: a connect to an address nobody allowed, a name sent to the resolver, a datagram.</param>
internal sealed record NetReport(IReadOnlyList<NetEvent> Hooked, IReadOnlyList<NetEvent> Ipc, IReadOnlyList<NetEvent> Requests, IReadOnlyList<NetEvent> Forbidden)
{
    public IEnumerable<string> Hosts => Requests.Select(r => NetworkHarness.HostPort(r.Target)).Distinct().Order(StringComparer.Ordinal);

    public override string ToString() =>
        $"hooked: {string.Join(", ", Hooked.Select(h => $"{h.Process}[{h.Pid}]"))}\nipc: {string.Join("; ", Ipc.Select(i => i.ToString()))}\n"
        + $"requests: {string.Join("; ", Requests.Select(r => r.Process + " " + r.Target))}\nforbidden: {string.Join("; ", Forbidden.Select(f => f.ToString()))}";
}

/// <summary>
/// Watches the network use of the editor under test: its own process (an event listener on the sockets, the resolver and HttpClient) and every .NET process it starts (the same listener loaded as a startup
/// hook through <c>DOTNET_STARTUP_HOOKS</c>, which the children inherit). Attempts are judged by where they go, not by which class made them, so code that opens a <c>Socket</c> itself is seen as well as code that
/// uses the network policy. What is allowed: Unix sockets, resolving <c>localhost</c>, and the loopback ports the test declares with <see cref="AllowLoopback"/>.
/// The local channels the editor has, and so the only things that may show up there: standard input and output pipes to plugins, language servers and agents (not sockets at all); a named pipe on Windows and a
/// Unix socket elsewhere for the second start of the editor and for <c>textida-mcp</c>; the loopback port of the IDE bridge while the user has it on; the proxy of the network policy, which the test declares.
/// Not seen: native code (a program such as git) and QUIC. A program built for NativeAOT is not seen either, so the run is over the plugins as the build of the tests makes them (see <see cref="SwitchOnWatching"/>).
/// </summary>
internal sealed class NetworkHarness : IDisposable
{
    private static readonly ConcurrentQueue<NetEvent> s_none = new();
    private static NetRecorder? s_recorder;
    private static volatile NetworkHarness? s_current;

    private readonly ConcurrentQueue<NetEvent> _inProcess = new();
    private readonly List<(int Port, string Why)> _loopback = [];
    private readonly string? _oldHooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
    private readonly string? _oldFolder = Environment.GetEnvironmentVariable(NetLog.FolderVariable);

    public NetworkHarness()
    {
        LogFolder = Path.Combine(Path.GetTempPath(), "textida-netwatch-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(LogFolder);
        var hook = typeof(NetRecorder).Assembly.Location;
        Assert.True(File.Exists(hook), "the hook assembly is missing: " + hook);
        SwitchOnWatching();
        if (s_recorder is null)
        {
            s_recorder = new NetRecorder(e => (s_current?._inProcess ?? s_none).Enqueue(e), freshProcess: false);
            // the first report of the datagram counter sets the level later reports are compared with
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!s_recorder.HasBaseline && wait.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(50);
        }
        s_current = this;
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", hook);
        Environment.SetEnvironmentVariable(NetLog.FolderVariable, LogFolder);
        Sinkhole = new SinkholeProxy();
    }

    /// <summary>
    /// The plugins are built for NativeAOT, and the SDK writes that into their runtimeconfig.json even for a plain build: no startup hooks and no EventSource, so nothing could watch them.
    /// The copies in the test folder get both switched back on; the network code of a program does not depend on either.
    /// </summary>
    private static void SwitchOnWatching()
    {
        var folders = new List<string> { AppContext.BaseDirectory };
        foreach (var parent in new[] { "plugins", "mcp" }.Select(d => Path.Combine(AppContext.BaseDirectory, d)).Where(Directory.Exists))
        {
            folders.Add(parent);
            folders.AddRange(Directory.GetDirectories(parent));
        }
        foreach (var file in folders.SelectMany(f => Directory.GetFiles(f, "*.runtimeconfig.json")))
        {
            var root = JsonNode.Parse(File.ReadAllText(file));
            if (root?["runtimeOptions"]?["configProperties"] is not JsonObject properties) continue;
            bool changed = false;
            foreach (var knob in new[] { "System.StartupHookProvider.IsSupported", "System.Diagnostics.Tracing.EventSource.IsSupported" })
                if (properties[knob] is JsonValue { } value && value.TryGetValue<bool>(out var on) && !on)
                {
                    properties[knob] = true;
                    changed = true;
                }
            if (changed) File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public string LogFolder { get; }

    /// <summary>A proxy that records and answers, to be set as the network proxy of the editor.</summary>
    public SinkholeProxy Sinkhole { get; }

    /// <summary>A loopback port the editor itself serves and the test lets connections go to (the IDE bridge); named in the report.</summary>
    public void AllowLoopback(int port, string why) => _loopback.Add((port, why));

    /// <summary><c>host:port</c> of an event target such as <c>https://registry.example:443</c>.</summary>
    public static string HostPort(string target) => NetTarget.HostOf(target) + ":" + NetTarget.PortOf(target);

    /// <summary><c>host:port</c> of an address, with the default port of its scheme.</summary>
    public static string HostPort(Uri address) => address.Host + ":" + address.Port;

    private NetEvent? Local(NetEvent e) => e.Kind switch
    {
        NetKind.Connect when NetTarget.IsUnix(e.Target) => e,
        NetKind.Connect when NetTarget.IsLoopbackHost(NetTarget.HostOf(e.Target)) && _loopback.Any(l => l.Port == NetTarget.PortOf(e.Target)) => e with { Detail = _loopback.First(l => l.Port == NetTarget.PortOf(e.Target)).Why },
        NetKind.Dns when NetTarget.IsLoopbackHost(e.Target) => e,
        _ => null,
    };

    /// <summary>The moment a later <see cref="FinishAsync"/> can start counting from, for a test whose first part is allowed to use the network.</summary>
    public static long Mark() => DateTime.UtcNow.Ticks;

    /// <summary>Waits for the datagram counters to report, then reads what this process and the hooked children recorded since <paramref name="since"/>.</summary>
    public async Task<NetReport> FinishAsync(long since = 0)
    {
        // the counters report once a second; one more second makes sure a datagram sent just before is in a report
        await Task.Delay(1200);
        var all = _inProcess.Concat(NetLog.ReadAll(LogFolder)).Where(e => e.Ticks >= since || e.Kind == NetKind.Hello).OrderBy(e => e.Ticks).ToList();
        var report = new NetReport(
            [.. all.Where(e => e.Kind == NetKind.Hello)],
            [.. all.Select(Local).OfType<NetEvent>()],
            [.. all.Where(e => e.Kind == NetKind.Http)],
            [.. all.Where(e => e.Kind != NetKind.Hello && e.Kind != NetKind.Http && Local(e) is null)]);
        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        return report;
    }

    public void Dispose()
    {
        if (ReferenceEquals(s_current, this)) s_current = null;
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", _oldHooks);
        Environment.SetEnvironmentVariable(NetLog.FolderVariable, _oldFolder);
        Sinkhole.Dispose();
        try { Directory.Delete(LogFolder, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

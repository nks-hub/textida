using System.Diagnostics.Tracing;
using System.Net;
using System.Text;

namespace NksHub.Textida.NetWatch;

public static class NetKind
{
    /// <summary>A process with the hook started; not network traffic.</summary>
    public const string Hello = "hello";
    /// <summary>A managed <c>Socket</c> started to connect. The target is <c>ip:port</c>, <c>[ipv6]:port</c> or <c>unix:path</c>.</summary>
    public const string Connect = "connect";
    /// <summary>A name was sent to the resolver.</summary>
    public const string Dns = "dns";
    /// <summary>An <c>HttpClient</c> request began; the target is <c>scheme://host:port</c> of the request, whatever proxy carries it.</summary>
    public const string Http = "http";
    /// <summary>The datagram counter of the sockets grew. The runtime reports no address for a datagram, so the target is empty.</summary>
    public const string Datagram = "datagram";
}

/// <summary>One thing a process did on the network, as the .NET runtime reported it.</summary>
public sealed record NetEvent(long Ticks, int Pid, string Process, string Kind, string Target, string Detail = "")
{
    public string ToLine() => string.Join('\t', Ticks, Pid, Clean(Process), Kind, Clean(Target), Clean(Detail));

    public static NetEvent? Parse(string line)
    {
        var parts = line.Split('\t');
        return parts.Length == 6 && long.TryParse(parts[0], out var ticks) && int.TryParse(parts[1], out var pid) ? new NetEvent(ticks, pid, parts[2], parts[3], parts[4], parts[5]) : null;
    }

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public override string ToString() => $"{Process}[{Pid}] {Kind} {Target}{(Detail.Length > 0 ? " (" + Detail + ")" : "")}".Trim();
}

/// <summary>Reading and judging the targets of <see cref="NetEvent"/>.</summary>
public static class NetTarget
{
    /// <summary>Turns the address string of the sockets event (<c>Family:size:{port, address bytes...}</c>, without the two family bytes) into <c>ip:port</c> or <c>unix:path</c>.</summary>
    public static string FromSocketAddress(string raw)
    {
        int open = raw.IndexOf('{'), colon = raw.IndexOf(':');
        if (open < 0 || colon < 0 || !raw.EndsWith('}')) return "?" + raw;
        var family = raw[..colon];
        var bytes = new List<byte>();
        foreach (var part in raw[(open + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (byte.TryParse(part, out var b)) bytes.Add(b);
            else return "?" + raw;
        switch (family)
        {
            case "InterNetwork" when bytes.Count >= 6:
                return Format(new IPAddress(bytes.GetRange(2, 4).ToArray()), bytes[0] << 8 | bytes[1]);
            case "InterNetworkV6" when bytes.Count >= 22:
                var ip = new IPAddress(bytes.GetRange(6, 16).ToArray());
                return Format(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip, bytes[0] << 8 | bytes[1]);
            case "Unix":
                bool abstractName = bytes.Count > 0 && bytes[0] == 0; // Linux: a name that is not a file starts with a zero byte
                int start = abstractName ? 1 : 0;
                int stop = bytes.IndexOf(0, start);
                return "unix:" + (abstractName ? "@" : "") + Encoding.UTF8.GetString(bytes.GetRange(start, (stop < 0 ? bytes.Count : stop) - start).ToArray());
            default:
                return "?" + raw;
        }
    }

    private static string Format(IPAddress ip, int port) => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{ip}]:{port}" : $"{ip}:{port}";

    public static bool IsUnix(string target) => target.StartsWith("unix:", StringComparison.Ordinal);

    /// <summary>The host part of <c>host:port</c>, <c>[v6]:port</c> or <c>scheme://host:port</c>.</summary>
    public static string HostOf(string target)
    {
        int scheme = target.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) target = target[(scheme + 3)..];
        if (target.StartsWith('[')) return target[1..target.IndexOf(']')];
        int colon = target.LastIndexOf(':');
        return colon < 0 ? target : target[..colon];
    }

    public static int PortOf(string target)
    {
        int colon = target.LastIndexOf(':');
        return colon >= 0 && int.TryParse(target[(colon + 1)..], out var port) ? port : -1;
    }

    /// <summary>True for <c>localhost</c> and for an address in 127.0.0.0/8 or ::1; a name that is none of those is not local whatever it resolves to.</summary>
    public static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
}

/// <summary>
/// Listens to the networking event sources of the runtime in this process: every managed socket connect, every name sent to the resolver, every <c>HttpClient</c> request and the growth of the datagram counter.
/// Code that opens its own <c>Socket</c> is seen as well as code that goes through the editor's network policy. Traffic of native code is not seen.
/// </summary>
public sealed class NetRecorder : EventListener
{
    private readonly Action<NetEvent>? _sink;
    private readonly int _pid = Environment.ProcessId;
    private readonly string _process = Label();
    private long _datagrams = -1;

    /// <param name="freshProcess">The recorder starts with the process, so no datagram was sent before it. Otherwise the first report of the counter only sets the level to compare with.</param>
    public NetRecorder(Action<NetEvent> sink, bool freshProcess)
    {
        _sink = sink;
        if (freshProcess) Interlocked.CompareExchange(ref _datagrams, 0, -1);
    }

    public string Process => _process;

    /// <summary>The datagram counter has reported once, so a later report can be compared with it.</summary>
    public bool HasBaseline => Interlocked.Read(ref _datagrams) >= 0;

    public void Emit(string kind, string target, string detail = "") =>
        _sink?.Invoke(new NetEvent(DateTime.UtcNow.Ticks, _pid, _process, kind, target, detail));

    /// <summary>The name of the process, and for the shared host (<c>dotnet app.dll</c>) the name of the dll.</summary>
    private static string Label()
    {
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "?";
        if (name != "dotnet") return name;
        var dll = Environment.GetCommandLineArgs().FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)); // the first element is the dll when the host starts it
        return dll is null ? name : name + ":" + Path.GetFileName(dll);
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        switch (source.Name)
        {
            case "System.Net.Sockets":
                // 1 s is the shortest interval the counters take; a fraction is parsed with the culture of the machine
                EnableEvents(source, EventLevel.Informational, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
                break;
            case "System.Net.NameResolution":
            case "System.Net.Http":
                EnableEvents(source, EventLevel.Informational, EventKeywords.All);
                break;
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (_sink is null || e.Payload is null) return;
        switch (e.EventSource.Name, e.EventName)
        {
            case ("System.Net.Sockets", "ConnectStart"):
                Emit(NetKind.Connect, NetTarget.FromSocketAddress(e.Payload[0]?.ToString() ?? ""));
                break;
            case ("System.Net.NameResolution", "ResolutionStart"):
                Emit(NetKind.Dns, e.Payload[0]?.ToString() ?? "");
                break;
            case ("System.Net.Http", "RequestStart"):
                Emit(NetKind.Http, $"{Field(e, "scheme")}://{Field(e, "host")}:{Field(e, "port")}", Field(e, "pathAndQuery"));
                break;
            case ("System.Net.Sockets", "EventCounters") when e.Payload[0] is IDictionary<string, object> counter && counter.TryGetValue("Name", out var name) && "datagrams-sent".Equals(name):
                long total = Convert.ToInt64(counter["Mean"], System.Globalization.CultureInfo.InvariantCulture);
                long before = Interlocked.Exchange(ref _datagrams, total);
                if (before >= 0 && total > before) Emit(NetKind.Datagram, "", $"+{total - before}");
                break;
        }
    }

    private static string Field(EventWrittenEventArgs e, string name)
    {
        int i = e.PayloadNames?.IndexOf(name) ?? -1;
        return i >= 0 ? e.Payload![i]?.ToString() ?? "" : "";
    }
}

/// <summary>The files a hooked process writes: one <c>PID.tsv</c> per process in a folder the test names.</summary>
public static class NetLog
{
    public const string FolderVariable = "TEXTIDA_NETWATCH_DIR";

    public static List<NetEvent> ReadAll(string folder)
    {
        var all = new List<NetEvent>();
        if (!Directory.Exists(folder)) return all;
        foreach (var file in Directory.EnumerateFiles(folder, "*.tsv"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            for (string? line; (line = reader.ReadLine()) is not null;)
                if (NetEvent.Parse(line) is { } parsed) all.Add(parsed);
        }
        return [.. all.OrderBy(x => x.Ticks)];
    }
}

/// <summary>Writes each event of a hooked process to its file at once, so a process that is killed still leaves what it did.</summary>
internal static class NetWatchHook
{
    private static NetRecorder? s_recorder;

    public static void Start()
    {
        var folder = Environment.GetEnvironmentVariable(NetLog.FolderVariable);
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        var writer = new StreamWriter(new FileStream(Path.Combine(folder, Environment.ProcessId + ".tsv"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        s_recorder = new NetRecorder(e => { lock (gate) writer.WriteLine(e.ToLine()); }, freshProcess: true);
        s_recorder.Emit(NetKind.Hello, Environment.ProcessPath ?? "", string.Join(' ', Environment.GetCommandLineArgs()));
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NksHub.Textida.Remote;

/// <summary>
/// A proxy on a random loopback port for the programs the editor starts (language servers, formatters, linters). Each program is a tool: its proxy address carries the tool name as the proxy login, so the proxy knows who asks.
/// A request passes only when the host is on the list the tool was registered with (<see cref="Register"/>), is not a telemetry host (<see cref="IsTelemetry"/>, never, whatever the list or the consent say) and the user
/// has allowed that host for that tool (<see cref="ToolNetworkConsent"/>). Anything else is answered with 403 and logged in <see cref="Refused"/>. A request that passes goes straight to the host, or through the HTTP
/// proxy of the network policy when the consent file names one. A plain HTTP request is routed by its own target and carries <c>Connection: close</c>, so each request of a connection is served by its own host.
/// </summary>
public sealed class ToolProxy : IDisposable
{
    private const int MaxHead = 16 * 1024;
    private static readonly string[] Names = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"];
    private static readonly string[] TelemetryHosts =
    [
        "telemetry.visualstudio.microsoft.com", "dc.services.visualstudio.com", "rt.services.visualstudio.com", "vortex.data.microsoft.com", "dc.applicationinsights.microsoft.com", "az667904.vo.msecnd.net",
    ];
    private static readonly string[] TelemetrySuffixes = [".events.data.microsoft.com", ".vortex.data.microsoft.com", ".applicationinsights.azure.com"];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _refused = new();
    private readonly ConcurrentDictionary<string, string[]> _tools = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();

    /// <summary>The proxy every program of the editor uses, started on first use.</summary>
    public static ToolProxy Shared { get; } = new();

    public ToolProxy()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>The consent of the user and the proxy of the network policy; without it nothing passes.</summary>
    public ToolNetworkConsent? Consent { get; set; }

    /// <summary>The hosts a tool may reach when the user agrees; <c>*</c> stands for any host but the telemetry ones. Calling it again replaces the list.</summary>
    public void Register(string tool, IEnumerable<string> hosts) => _tools[tool] = [.. hosts.Select(h => h.ToLowerInvariant())];

    /// <summary>The hosts that were refused, in order, one entry per request (<c>host:port</c>).</summary>
    public IReadOnlyCollection<string> Refused => _refused.ToArray();

    /// <summary>Hosts that take usage data of Microsoft tools; they are refused for every tool.</summary>
    public static bool IsTelemetry(string host)
    {
        host = host.ToLowerInvariant();
        return TelemetryHosts.Contains(host) || TelemetrySuffixes.Any(host.EndsWith);
    }

    /// <summary>Whether <paramref name="tool"/> may reach <paramref name="host"/> now: on its list, not telemetry, consented, and the network policy can be honoured.</summary>
    public bool Permits(string tool, string host)
    {
        host = host.ToLowerInvariant();
        if (IsTelemetry(host) || !_tools.TryGetValue(tool, out var listed) || !(listed.Contains("*") || listed.Contains(host))) return false;
        return Consent is { } consent && !consent.Read().Blocked && consent.IsAllowed(tool, host);
    }

    /// <summary>The address a tool gets in <c>HTTP_PROXY</c> and its relatives; the tool name is the login.</summary>
    public string UrlFor(string tool) => $"http://{Uri.EscapeDataString(tool)}:x@127.0.0.1:{Port}";

    /// <summary>The variables that send a program through this proxy; only loopback is exempt.</summary>
    public IReadOnlyDictionary<string, string> Environment(string tool)
    {
        var env = Names.ToDictionary(n => n, _ => UrlFor(tool));
        env["NO_PROXY"] = env["no_proxy"] = "localhost,127.0.0.1,::1";
        return env;
    }

    /// <summary>Sets <see cref="Environment"/> on a start info, over what the editor's own environment says.</summary>
    public void Apply(ProcessStartInfo info, string tool)
    {
        foreach (var (name, value) in Environment(tool)) info.Environment[name] = value;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { /* stopped */ }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            var stream = client.GetStream();
            var head = new List<byte>();
            var one = new byte[1];
            while (head.Count < MaxHead && !EndsWithBlankLine(head))
            {
                if (await stream.ReadAsync(one, _stop.Token).ConfigureAwait(false) == 0) return;
                head.Add(one[0]);
            }
            var lines = Encoding.ASCII.GetString([.. head]).Split("\r\n");
            var parts = lines[0].Split(' ');
            if (parts.Length < 3) return;
            bool connect = parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
            string tool = ToolOf(lines), hostHeader = HeaderValue(lines, "Host");
            if (!TryTarget(parts[1], connect, hostHeader, out string host, out int port)) { Refuse(parts[1]); await RefuseAsync(stream).ConfigureAwait(false); return; }
            if (!Permits(tool, host))
            {
                Refuse($"{host}:{port}");
                await RefuseAsync(stream).ConfigureAwait(false);
                return;
            }
            var proxy = Consent?.Read().Proxy is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
            using var upstream = new TcpClient();
            await upstream.ConnectAsync(proxy?.Host ?? host, proxy?.Port ?? port, _stop.Token).ConfigureAwait(false);
            var remote = upstream.GetStream();
            if (connect)
            {
                if (proxy is not null && !await TunnelThroughAsync(remote, host, port).ConfigureAwait(false)) { await RefuseAsync(stream).ConfigureAwait(false); return; }
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), _stop.Token).ConfigureAwait(false);
            }
            else await remote.WriteAsync(Encoding.ASCII.GetBytes(ForwardedHead(lines)), _stop.Token).ConfigureAwait(false);
            await Task.WhenAny(stream.CopyToAsync(remote, _stop.Token), remote.CopyToAsync(stream, _stop.Token)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { /* the peer went away */ }
    }

    /// <summary>Asks the HTTP proxy of the network policy for a tunnel to the host; false when it does not give one.</summary>
    private async Task<bool> TunnelThroughAsync(NetworkStream remote, string host, int port)
    {
        await remote.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {host}:{port} HTTP/1.1\r\nHost: {host}:{port}\r\n\r\n"), _stop.Token).ConfigureAwait(false);
        var answer = new List<byte>();
        var one = new byte[1];
        while (answer.Count < MaxHead && !EndsWithBlankLine(answer))
        {
            if (await remote.ReadAsync(one, _stop.Token).ConfigureAwait(false) == 0) return false;
            answer.Add(one[0]);
        }
        return Encoding.ASCII.GetString([.. answer]).StartsWith("HTTP/1.", StringComparison.Ordinal) && Encoding.ASCII.GetString([.. answer]).Split(' ').ElementAtOrDefault(1) == "200";
    }

    /// <summary>The request head without the proxy headers of the tool and with <c>Connection: close</c>, so the connection serves this one request.</summary>
    private static string ForwardedHead(string[] lines)
    {
        var sb = new StringBuilder(lines[0]).Append("\r\n");
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) continue;
            sb.Append(line).Append("\r\n");
        }
        return sb.Append("Connection: close\r\n\r\n").ToString();
    }

    private static string HeaderValue(string[] lines, string name)
    {
        foreach (var line in lines.Skip(1))
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim();
        return "";
    }

    /// <summary>The tool named in the proxy login of the request; empty when the request has none.</summary>
    private static string ToolOf(string[] lines)
    {
        var value = HeaderValue(lines, "Proxy-Authorization");
        if (!value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return "";
        try
        {
            var login = Encoding.UTF8.GetString(Convert.FromBase64String(value[6..].Trim()));
            int colon = login.IndexOf(':');
            return Uri.UnescapeDataString(colon < 0 ? login : login[..colon]);
        }
        catch (FormatException) { return ""; }
    }

    /// <summary>Where the refused hosts are also appended, one per line, when <c>TEXTIDA_PROXY_LOG</c> names a file; for a plugin process whose memory a test cannot read.</summary>
    private static readonly string? LogFile = System.Environment.GetEnvironmentVariable("TEXTIDA_PROXY_LOG") is { Length: > 0 } path ? path : null;

    private void Refuse(string target)
    {
        _refused.Enqueue(target);
        if (LogFile is null) return;
        try { lock (_refused) File.AppendAllText(LogFile, target + "\n"); }
        catch (IOException) { /* the log is a help, not a duty */ }
    }

    private static bool EndsWithBlankLine(List<byte> head) =>
        head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n';

    /// <summary>The host and port a request is for: the authority of CONNECT, else the absolute URI of the request, else its Host header.</summary>
    private static bool TryTarget(string target, bool connect, string hostHeader, out string host, out int port)
    {
        host = "";
        port = connect ? 443 : 80;
        if (connect)
        {
            int colon = target.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(target[(colon + 1)..], out port)) return false;
            host = target[..colon].Trim('[', ']');
            return host.Length > 0;
        }
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp) { host = uri.Host; port = uri.Port; return true; }
        if (target.StartsWith('/') && hostHeader.Length > 0 && Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out var byHeader)) { host = byHeader.Host; port = byHeader.Port; return true; }
        return false;
    }

    private async Task RefuseAsync(NetworkStream stream)
    {
        var body = "Textida: this program may not use the network.";
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 403 Forbidden\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), _stop.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

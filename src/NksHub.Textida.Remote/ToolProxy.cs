using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NksHub.Textida.Remote;

/// <summary>
/// A proxy on a random loopback port for the programs the editor starts (language servers, formatters, linters). Such a program reaches the network only through it: a request for a host outside
/// <see cref="Allowed"/>, or any request while <see cref="Permit"/> says no (the network policy and the consent of the user), is answered with 403 and the host is logged in <see cref="Refused"/>.
/// A request that passes is forwarded straight to the host (a tunnel for CONNECT, the raw request for plain HTTP).
/// shortcut: a plain HTTP connection is tunnelled to the host of its first request, so a client that reuses it for another host is served by the first; the programs here use CONNECT for https, upgrade if one needs more.
/// </summary>
public sealed class ToolProxy : IDisposable
{
    private const int MaxHead = 16 * 1024;
    private static readonly string[] Names = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _refused = new();
    private readonly ConcurrentDictionary<string, byte> _allowed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();

    /// <summary>The proxy every program of the editor uses, started on first use.</summary>
    public static ToolProxy Shared { get; } = new();

    public ToolProxy(IEnumerable<string>? allowed = null)
    {
        foreach (var host in allowed ?? []) _allowed[host] = 0;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>Whether a host outside the list may be reached at all; the editor sets it from the network policy and the consent. Default: no.</summary>
    public Func<string, bool> Permit { get; set; } = static _ => false;

    /// <summary>The hosts a request may reach (when <see cref="Permit"/> agrees); empty means none.</summary>
    public ICollection<string> Allowed => _allowed.Keys;

    public void Allow(string host) => _allowed[host] = 0;

    /// <summary>The hosts that were refused, in order, one entry per request (<c>host:port</c>).</summary>
    public IReadOnlyCollection<string> Refused => _refused.ToArray();

    /// <summary>The address programs get in <c>HTTP_PROXY</c> and its relatives.</summary>
    public string Url => $"http://127.0.0.1:{Port}";

    /// <summary>The variables that send a program through this proxy; only loopback is exempt.</summary>
    public IReadOnlyDictionary<string, string> Environment()
    {
        var env = Names.ToDictionary(n => n, _ => Url);
        env["NO_PROXY"] = env["no_proxy"] = "localhost,127.0.0.1,::1";
        return env;
    }

    /// <summary>Sets <see cref="Environment"/> on a start info, over what the editor's own environment says.</summary>
    public void Apply(ProcessStartInfo info)
    {
        foreach (var (name, value) in Environment()) info.Environment[name] = value;
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
            if (parts.Length < 2) return;
            bool connect = parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
            if (!TryTarget(parts[1], connect, out string host, out int port)) { Refuse(parts[1]); await RefuseAsync(stream).ConfigureAwait(false); return; }
            if (!_allowed.ContainsKey(host) || !Permit(host))
            {
                Refuse($"{host}:{port}");
                await RefuseAsync(stream).ConfigureAwait(false);
                return;
            }
            using var upstream = new TcpClient();
            await upstream.ConnectAsync(host, port, _stop.Token).ConfigureAwait(false);
            var remote = upstream.GetStream();
            if (connect) await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"), _stop.Token).ConfigureAwait(false);
            else await remote.WriteAsync(head.ToArray(), _stop.Token).ConfigureAwait(false);
            await Task.WhenAny(stream.CopyToAsync(remote, _stop.Token), remote.CopyToAsync(stream, _stop.Token)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { /* the peer went away */ }
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

    private static bool TryTarget(string target, bool connect, out string host, out int port)
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
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp) return false;
        host = uri.Host;
        port = uri.Port;
        return true;
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

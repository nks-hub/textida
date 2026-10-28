using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// An HTTP proxy on the loopback address that connects to nothing. It writes down what the client asked it to reach (<c>host:port</c> of a CONNECT, the authority of an absolute address)
/// and answers a plain request from <see cref="Respond"/>, a CONNECT with 502. Set as the network proxy of the editor, it shows which hosts the editor would have talked to, and nothing leaves the machine.
/// </summary>
internal sealed class SinkholeProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<string> _targets = new();

    /// <summary>The body for a plain request, or null for 404.</summary>
    public Func<Uri, (string Type, byte[] Body)?> Respond { get; set; } = _ => null;

    public SinkholeProxy()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public int Port { get; }

    public NksHub.Textida.Remote.ProxySettings Settings => new(NksHub.Textida.Remote.ProxyType.Http, "127.0.0.1", Port);

    /// <summary>When set, a request without this Basic login is answered 407 and not counted in <see cref="Targets"/>.</summary>
    public (string User, string Password)? Login { get; set; }

    private int _challenged;

    /// <summary>How many requests were turned away for want of the login.</summary>
    public int Challenged => Volatile.Read(ref _challenged);

    /// <summary>Each request in arrival order as <c>host:port</c>.</summary>
    public IReadOnlyList<string> Targets => [.. _targets];

    private async Task AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(); }
            catch (Exception e) when (e is ObjectDisposedException or SocketException or InvalidOperationException) { return; }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buffer = new byte[4096];
                while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    head.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                var lines = head.ToString().Split("\r\n");
                var line = lines[0].Split(' ');
                if (line.Length < 2) return;
                if (Login is { } login && !lines.Contains("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{login.User}:{login.Password}")), StringComparer.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _challenged);
                    await stream.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"sinkhole\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
                    return;
                }
                if (line[0] == "CONNECT")
                {
                    _targets.Enqueue(line[1]);
                    await stream.WriteAsync("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
                    return;
                }
                if (!Uri.TryCreate(line[1], UriKind.Absolute, out var uri))
                {
                    await stream.WriteAsync(Http.Status(400));
                    return;
                }
                _targets.Enqueue($"{uri.Host}:{uri.Port}");
                await stream.WriteAsync(Respond(uri) is var (type, body) ? Http.Ok(body, type) : Http.Status(404));
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
    }

    public void Dispose() => _listener.Stop();
}

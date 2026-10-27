using System.Net;
using System.Net.Sockets;
using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>
/// A working proxy of one kind (HTTP CONNECT, SOCKS4, SOCKS5) that relays real TCP connections. It counts the tunnels it opened, so a test can prove that the
/// traffic went through it and not around it.
/// </summary>
public sealed class TestProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _tunnels;
    private int _refused;

    public ProxyType Kind { get; }
    public string? User { get; }
    public string? Password { get; }
    public int Port { get; }
    public int Tunnels => Volatile.Read(ref _tunnels);
    public int Refused => Volatile.Read(ref _refused);

    public TestProxy(ProxyType kind, string? user = null, string? password = null)
    {
        Kind = kind; User = user; Password = password;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public ProxySettings Settings => new(Kind, "127.0.0.1", Port, User);

    private async Task AcceptLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Serve(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var s = client.GetStream();
                var target = Kind switch { ProxyType.Http => await Http(s), ProxyType.Socks4 => await Socks4(s), _ => await Socks5(s) };
                if (target is null) { Interlocked.Increment(ref _refused); return; }
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(target.Value.Host, target.Value.Port, _stop.Token);
                Interlocked.Increment(ref _tunnels);
                var u = upstream.GetStream();
                var a = s.CopyToAsync(u, _stop.Token);
                var b = u.CopyToAsync(s, _stop.Token);
                await Task.WhenAny(a, b);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static async Task<byte[]> Exactly(Stream s, int n)
    {
        var buf = new byte[n];
        await s.ReadExactlyAsync(buf);
        return buf;
    }

    private async Task<(string Host, int Port)?> Http(NetworkStream s)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n"))
        {
            if (await s.ReadAsync(one) == 0) return null;
            sb.Append((char)one[0]);
        }
        var lines = sb.ToString().Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts[0] != "CONNECT") { await s.WriteAsync("HTTP/1.1 405 Method Not Allowed\r\n\r\n"u8.ToArray()); return null; }
        if (User is not null)
        {
            string expected = "Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Password}"));
            if (!lines.Any(l => l.Equals(expected, StringComparison.OrdinalIgnoreCase)))
            {
                await s.WriteAsync("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"t\"\r\n\r\n"u8.ToArray());
                return null;
            }
        }
        int colon = parts[1].LastIndexOf(':');
        await s.WriteAsync("HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray());
        return (parts[1][..colon], int.Parse(parts[1][(colon + 1)..]));
    }

    private async Task<(string Host, int Port)?> Socks4(NetworkStream s)
    {
        var head = await Exactly(s, 8); // VN CD DSTPORT DSTIP
        var id = new List<byte>();
        var one = new byte[1];
        while (true) { await s.ReadExactlyAsync(one); if (one[0] == 0) break; id.Add(one[0]); }
        string host = new IPAddress(head[4..8]).ToString();
        if (head[4] == 0 && head[5] == 0 && head[6] == 0 && head[7] != 0) // SOCKS4a: the host name follows
        {
            var name = new List<byte>();
            while (true) { await s.ReadExactlyAsync(one); if (one[0] == 0) break; name.Add(one[0]); }
            host = Encoding.ASCII.GetString([.. name]);
        }
        if (head[0] != 4 || head[1] != 1 || (User is not null && Encoding.ASCII.GetString([.. id]) != User))
        {
            await s.WriteAsync(new byte[] { 0, 0x5B, 0, 0, 0, 0, 0, 0 });
            return null;
        }
        await s.WriteAsync(new byte[] { 0, 0x5A, 0, 0, 0, 0, 0, 0 });
        return (host, (head[2] << 8) | head[3]);
    }

    private async Task<(string Host, int Port)?> Socks5(NetworkStream s)
    {
        var greeting = await Exactly(s, 2);
        var methods = await Exactly(s, greeting[1]);
        byte method = User is null ? (byte)0 : (byte)2;
        if (!methods.Contains(method)) { await s.WriteAsync(new byte[] { 5, 0xFF }); return null; }
        await s.WriteAsync(new byte[] { 5, method });
        if (User is not null)
        {
            var v = await Exactly(s, 2);
            string user = Encoding.UTF8.GetString(await Exactly(s, v[1]));
            string pass = Encoding.UTF8.GetString(await Exactly(s, (await Exactly(s, 1))[0]));
            bool ok = user == User && pass == Password;
            await s.WriteAsync(new byte[] { 1, (byte)(ok ? 0 : 1) });
            if (!ok) return null;
        }
        var req = await Exactly(s, 4);
        string host = req[3] switch
        {
            1 => new IPAddress(await Exactly(s, 4)).ToString(),
            3 => Encoding.ASCII.GetString(await Exactly(s, (await Exactly(s, 1))[0])),
            4 => new IPAddress(await Exactly(s, 16)).ToString(),
            _ => throw new IOException("address type"),
        };
        var p = await Exactly(s, 2);
        await s.WriteAsync(new byte[] { 5, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        return (host, (p[0] << 8) | p[1]);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

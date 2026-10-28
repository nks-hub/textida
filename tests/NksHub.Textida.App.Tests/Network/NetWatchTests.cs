using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NksHub.Textida.NetWatch;

namespace NksHub.Textida.App.Tests;

/// <summary>PR.6.4: the watch itself. A harness that cannot see a deliberate connect would let every scenario pass.</summary>
[Collection("LocState")]
[Trait("Category", "Network")]
public class NetWatchTests
{
    private static int PortOf(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    [Fact]
    public async Task A_connect_a_name_a_request_and_a_datagram_are_seen_and_only_the_declared_local_channels_are_allowed()
    {
        using var net = new NetworkHarness();
        using var declared = new TcpListener(IPAddress.Loopback, 0);
        using var other = new TcpListener(IPAddress.Loopback, 0);
        declared.Start();
        other.Start();
        net.AllowLoopback(PortOf(declared), "listener the test declared");

        using (var client = new TcpClient()) await client.ConnectAsync(IPAddress.Loopback, PortOf(declared));
        using (var client = new TcpClient()) await client.ConnectAsync(IPAddress.Loopback, PortOf(other));
        var socketFile = Path.Combine(Path.GetTempPath(), "nw-" + Guid.NewGuid().ToString("N")[..8] + ".sock");
        using (var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            server.Bind(new UnixDomainSocketEndPoint(socketFile));
            server.Listen(1);
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(socketFile));
        }
        File.Delete(socketFile);

        using (var cancel = new CancellationTokenSource(300))
        {
            using var client = new TcpClient();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ConnectAsync(IPAddress.Parse("192.0.2.1"), 80, cancel.Token));
        }
        await Assert.ThrowsAnyAsync<SocketException>(() => Dns.GetHostAddressesAsync("telemetry.invalid"));
        using (var http = new HttpClient())
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.GetAsync("http://203.0.113.9/x", new CancellationTokenSource(300).Token));
        using (var udp = new UdpClient()) await udp.SendAsync(new byte[3], 3, new IPEndPoint(IPAddress.Parse("198.51.100.7"), 9));

        var report = await net.FinishAsync();

        Assert.Contains(report.Ipc, e => e.Target == $"127.0.0.1:{PortOf(declared)}" && e.Detail == "listener the test declared");
        Assert.Contains(report.Ipc, e => e.Target.StartsWith("unix:", StringComparison.Ordinal) && e.Target.EndsWith(".sock", StringComparison.Ordinal));
        Assert.Contains(report.Forbidden, e => e is { Kind: NetKind.Connect } && e.Target == $"127.0.0.1:{PortOf(other)}");
        Assert.Contains(report.Forbidden, e => e is { Kind: NetKind.Connect, Target: "192.0.2.1:80" });
        Assert.Contains(report.Forbidden, e => e is { Kind: NetKind.Connect, Target: "203.0.113.9:80" });
        Assert.Contains(report.Forbidden, e => e is { Kind: NetKind.Dns, Target: "telemetry.invalid" });
        Assert.Contains(report.Forbidden, e => e.Kind == NetKind.Datagram);
        Assert.Equal(["203.0.113.9:80"], report.Hosts);
    }

    [Fact]
    public async Task A_dotnet_process_the_editor_starts_reports_itself_to_the_watch()
    {
        using var net = new NetworkHarness();
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var info = new ProcessStartInfo(host, [Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.FakeLsp.dll")]) { RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        using var child = Process.Start(info)!;
        var sw = Stopwatch.StartNew();
        while (!NetLog.ReadAll(net.LogFolder).Any(e => e.Kind == NetKind.Hello) && sw.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(50);
        child.StandardInput.Close();
        Assert.True(child.WaitForExit(30_000));

        var report = await net.FinishAsync();

        var hello = Assert.Single(report.Hooked);
        Assert.Equal("dotnet:NksHub.Textida.FakeLsp.dll", hello.Process);
        Assert.Equal(child.Id, hello.Pid);
        Assert.Empty(report.Forbidden);
    }

    [Theory]
    [InlineData("InterNetwork:16:{1,187,192,0,2,7,0,0,0,0,0,0,0,0}", "192.0.2.7:443")]
    [InlineData("InterNetworkV6:28:{0,80,0,0,0,0,0,0,0,0,0,0,0,0,0,0,255,255,198,51,100,9,0,0,0,0}", "198.51.100.9:80")]
    [InlineData("InterNetworkV6:28:{31,144,0,0,0,0,32,1,13,184,0,0,0,0,0,0,0,0,0,0,0,1,0,0,0,0}", "[2001:db8::1]:8080")]
    [InlineData("Unix:110:{47,116,109,112,47,97,46,115,111,99,107,0,0,0}", "unix:/tmp/a.sock")]
    [InlineData("Unix:110:{0,97,98,99,0,0}", "unix:@abc")]
    [InlineData("nonsense", "?nonsense")]
    public void The_address_of_a_connect_is_read_from_the_event_text(string raw, string expected) => Assert.Equal(expected, NetTarget.FromSocketAddress(raw));
}

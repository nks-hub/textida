using System.Diagnostics;
using System.Text;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>A test that needs real programs; it is skipped, with their names, where one of them is not found in PATH. The tests that use it have the category RealTools, which the quick suite leaves out.</summary>
public sealed class RealToolFactAttribute : FactAttribute
{
    public RealToolFactAttribute(params string[] programs)
    {
        var missing = programs.Where(p => ServerCatalog.Find(p) is null).ToList();
        if (missing.Count > 0) Skip = string.Join(", ", missing) + " is not installed here (not found in PATH).";
    }
}

/// <summary>Skipped when none of the programs is found in PATH.</summary>
public sealed class RealToolAnyFactAttribute : FactAttribute
{
    public RealToolAnyFactAttribute(params string[] programs)
    {
        if (programs.All(p => ServerCatalog.Find(p) is null)) Skip = "None of " + string.Join(", ", programs) + " is installed here (not found in PATH).";
    }
}

/// <summary>
/// A listener that every proxy variable points to, so that a program which tries to reach the network (a package, telemetry) connects to it and is counted. The variables are process wide:
/// the tests that use it run alone (the category RealTools is run by itself).
/// </summary>
internal sealed class NetCanary : IDisposable
{
    private static readonly string[] Names = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy", "NO_PROXY", "no_proxy"];
    private readonly System.Net.Sockets.TcpListener _listener = new(System.Net.IPAddress.Loopback, 0);
    private readonly Dictionary<string, string?> _saved = [];
    private int _connections;

    public int Connections => Volatile.Read(ref _connections);

    /// <summary>The first line each connection sent (a proxy request names the host: <c>CONNECT host:443 HTTP/1.1</c>).</summary>
    public IReadOnlyCollection<string> Targets => _targets.ToArray();

    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _targets = new();

    public NetCanary()
    {
        _listener.Start();
        string proxy = "http://127.0.0.1:" + ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _connections);
                    try
                    {
                        client.ReceiveTimeout = 500;
                        var buffer = new byte[300];
                        int read = client.GetStream().Read(buffer, 0, buffer.Length);
                        _targets.Enqueue(Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0].Trim());
                    }
                    catch (Exception e) when (e is IOException or ObjectDisposedException) { _targets.Enqueue("(nothing sent)"); }
                }
            }
            catch (Exception e) when (e is ObjectDisposedException or System.Net.Sockets.SocketException or InvalidOperationException) { /* stopped */ }
        });
        foreach (var name in Names)
        {
            _saved[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, name.StartsWith("NO_", StringComparison.OrdinalIgnoreCase) ? "" : proxy);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved) Environment.SetEnvironmentVariable(name, value);
        _listener.Stop();
    }
}

internal static class RealTools
{
    /// <summary>
    /// A copy of a program under a name nobody else uses, in a folder of its own. A test that kills or counts the processes of a server starts this copy, so it never touches the process of another run;
    /// the copy of a Go server is one file, a program that needs its folder (a Java or Node server) is not copied this way.
    /// </summary>
    public static string UniqueCopy(string program, string folder)
    {
        string source = ServerCatalog.Find(program) ?? throw new FileNotFoundException(program);
        string name = "textida-" + Path.GetFileNameWithoutExtension(program) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(source);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, name);
        File.Copy(source, target);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return target;
    }

    /// <summary>The processes started from <paramref name="program"/> (a path of <see cref="UniqueCopy"/>) that are running now.</summary>
    public static Process[] Running(string program) => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(program));
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.FileSystem.DotNet;
using Microsoft.Extensions.DependencyInjection;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

/// <summary>A temp folder that is removed with the test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("textida-remote-").FullName;
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>An SFTP server (paramiko, a real SSH implementation) started as a child process with a host key generated for the run.</summary>
public sealed class SftpServer : IServerFixture
{
    private static readonly Lazy<bool> _available = new(CheckAvailable);
    private readonly Process _process;

    public static bool Available => _available.Value;
    public RemoteProtocol Protocol => RemoteProtocol.Sftp;
    public string Root { get; }
    public int Port { get; }
    public string User => "u";
    public string Password => "p";
    public string Identity { get; }
    public string? ClientKeyPath { get; }

    private SftpServer(Process process, string root, int port, string identity, string? clientKey)
    {
        _process = process; Root = root; Port = port; Identity = identity; ClientKeyPath = clientKey;
    }

    public RemoteConnection Connection(string? trusted = null, ProxySettings? proxy = null) => new()
    {
        Name = "test", Protocol = RemoteProtocol.Sftp, Host = "127.0.0.1", Port = Port, UserName = User,
        TrustedIdentity = trusted, Proxy = proxy ?? new(),
    };

    private static bool CheckAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("python", "-c \"import paramiko\"") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return false;
            p.WaitForExit(30000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <param name="hostKeyFile">Where the host key lives; an existing file is reused, so two servers with the same file present the same key.</param>
    /// <param name="dropAfterBytes">When set, the server cuts the connection after it received that many bytes of file data.</param>
    /// <param name="dropReadAfterBytes">When set, the server cuts the connection after it sent that many bytes of file data (a download cut in the middle).</param>
    /// <param name="clientKeyType">"ed25519" or "rsa": the kind of login key <paramref name="clientKey"/> generates.</param>
    /// <param name="legacyRsa">"userauth", "hostkey" or "both": the server knows only the SHA-1 signature ssh-rsa for the login key, for its host key, or for both, like a server from before 2016.</param>
    public static SftpServer Start(string root, string hostKeyFile, int port = 0, int? dropAfterBytes = null, bool clientKey = false, string clientKeyType = "ed25519", string? legacyRsa = null, int? dropReadAfterBytes = null)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "Servers", "sftp_server.py");
        string args = $"\"{script}\" --root \"{root}\" --hostkey \"{hostKeyFile}\" --port {port}";
        string? keyPath = null;
        if (dropAfterBytes is { } d) args += $" --drop-after {d}";
        if (dropReadAfterBytes is { } r) args += $" --drop-read-after {r}";
        if (legacyRsa is not null) args += $" --legacy-rsa {legacyRsa}";
        if (clientKey) { keyPath = hostKeyFile + ".client"; args += $" --gen-client-key \"{keyPath}\" --client-key-type {clientKeyType}"; }
        var psi = new ProcessStartInfo("python", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var process = Process.Start(psi) ?? throw new InvalidOperationException("python did not start");
        var errors = new System.Text.StringBuilder();
        process.ErrorDataReceived += (_, e) => { lock (errors) errors.AppendLine(e.Data); };
        process.BeginErrorReadLine();
        var line = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
        if (line is null)
        {
            process.WaitForExit(5000);
            lock (errors) throw new InvalidOperationException("the SFTP test server printed nothing: " + errors);
        }
        using var json = JsonDocument.Parse(line);
        return new SftpServer(process, root, json.RootElement.GetProperty("port").GetInt32(),
            "SHA256:" + json.RootElement.GetProperty("sha256").GetString(), keyPath);
    }

    public ValueTask DisposeAsync()
    {
        try { _process.Kill(entireProcessTree: true); _process.WaitForExit(10000); } catch (InvalidOperationException) { }
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Skipped on a machine where Python with paramiko is not installed; the SFTP tests need it for a real SSH server.</summary>
public sealed class SftpFactAttribute : FactAttribute
{
    public SftpFactAttribute() { if (!SftpServer.Available) Skip = "SFTP test server needs python with paramiko (pip install paramiko)."; }
}

/// <summary>Starts a server of each protocol for a test body.</summary>
public static class Servers
{
    public static async Task<IServerFixture> StartAsync(RemoteProtocol protocol, string root, string dir) => protocol switch
    {
        RemoteProtocol.Ftps => await FtpsServer.StartAsync(root),
        _ => SftpServer.Start(root, Path.Combine(dir, "host_key")),
    };

    /// <summary>FTPS always; SFTP when the Python SSH server can run here (a separate skipped test says when it cannot).</summary>
    public static IEnumerable<object[]> Both => SftpServer.Available ? [[RemoteProtocol.Sftp], [RemoteProtocol.Ftps]] : [[RemoteProtocol.Ftps]];
}

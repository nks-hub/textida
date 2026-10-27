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

/// <summary>Both protocols behind one shape, so a test body runs against each of them.</summary>
public interface IServerFixture : IAsyncDisposable
{
    RemoteProtocol Protocol { get; }
    /// <summary>The folder the server serves; a test changes files in it directly to play "someone else wrote on the server".</summary>
    string Root { get; }
    int Port { get; }
    string User { get; }
    string Password { get; }
    /// <summary>The identity the server presents in the form stored in <see cref="RemoteConnection.TrustedIdentity"/>.</summary>
    string Identity { get; }
    RemoteConnection Connection(string? trusted = null, ProxySettings? proxy = null);
}

/// <summary>Explicit or implicit FTPS served by FubarDev.FtpServer inside the test process, with a certificate generated for the run.</summary>
public sealed class FtpsServer : IServerFixture
{
    private readonly ServiceProvider _provider;
    private readonly IFtpServerHost _host;

    public RemoteProtocol Protocol => RemoteProtocol.Ftps;
    public string Root { get; }
    public int Port { get; }
    public string User => "u";
    public string Password => "p";
    public string Identity { get; }
    public bool Implicit { get; }

    private FtpsServer(ServiceProvider provider, IFtpServerHost host, string root, int port, string identity, bool isImplicit)
    {
        _provider = provider; _host = host; Root = root; Port = port; Identity = identity; Implicit = isImplicit;
    }

    public RemoteConnection Connection(string? trusted = null, ProxySettings? proxy = null) => new()
    {
        Name = "test", Protocol = RemoteProtocol.Ftps, Host = "127.0.0.1", Port = Port, UserName = User,
        FtpsMode = Implicit ? FtpsMode.Implicit : FtpsMode.Explicit, TrustedIdentity = trusted, Proxy = proxy ?? new(),
    };

    public static X509Certificate2 NewCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
    }

    public static async Task<FtpsServer> StartAsync(string root, bool isImplicit = false, X509Certificate2? certificate = null, int? port = null, bool plain = false)
    {
        certificate ??= NewCertificate();
        int p = port ?? FreePort();
        var services = new ServiceCollection();
        if (!plain) services.Configure<AuthTlsOptions>(o => { o.ServerCertificate = certificate; o.ImplicitFtps = isImplicit; });
        services.Configure<DotNetFileSystemOptions>(o => o.RootPath = root);
        services.Configure<FtpServerOptions>(o => { o.ServerAddress = "127.0.0.1"; o.Port = p; });
        services.AddSingleton<FixedMembership>();
        services.AddSingleton<IMembershipProvider>(sp => sp.GetRequiredService<FixedMembership>());
        services.AddSingleton<IMembershipProviderAsync>(sp => sp.GetRequiredService<FixedMembership>());
        services.AddFtpServer(b => b.UseDotNetFileSystem().UseSingleRoot());
        var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IFtpServerHost>();
        await host.StartAsync(CancellationToken.None);
        return new FtpsServer(provider, host, root, p, FtpRemoteClient.FormatFingerprint(certificate), isImplicit);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    private sealed class FixedMembership : IMembershipProviderAsync, IMembershipProvider
    {
        public Task<MemberValidationResult> ValidateUserAsync(string username, string password, CancellationToken cancellationToken)
        {
            if (username != "u" || password != "p") return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], "test");
            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, new ClaimsPrincipal(identity)));
        }

        public Task<MemberValidationResult> ValidateUserAsync(string username, string password) => ValidateUserAsync(username, password, CancellationToken.None);

        public Task LogOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) => Task.CompletedTask;
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
    public static SftpServer Start(string root, string hostKeyFile, int port = 0, int? dropAfterBytes = null, bool clientKey = false)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "Servers", "sftp_server.py");
        string args = $"\"{script}\" --root \"{root}\" --hostkey \"{hostKeyFile}\" --port {port}";
        string? keyPath = null;
        if (dropAfterBytes is { } d) args += $" --drop-after {d}";
        if (clientKey) { keyPath = hostKeyFile + ".client"; args += $" --gen-client-key \"{keyPath}\""; }
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

public sealed class AcceptAll : IServerTrustPrompt
{
    public List<(ServerIdentity Identity, TrustReason Reason)> Asked { get; } = [];
    public ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation)
    {
        Asked.Add((presented, reason));
        return ValueTask.FromResult(true);
    }
}

public sealed class MemoryCredentials : NksHub.Textida.Core.Secrets.ICredentialStore
{
    public Dictionary<string, string> Values { get; } = [];
    public string? Get(string key) => Values.GetValueOrDefault(key);
    public void Set(string key, string value) => Values[key] = value;
    public bool Delete(string key) => Values.Remove(key);
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

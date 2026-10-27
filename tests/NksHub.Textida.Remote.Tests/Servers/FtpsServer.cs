using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.FileSystem.DotNet;
using Microsoft.Extensions.DependencyInjection;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

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
    public string Password { get; }
    public string Identity { get; }
    public bool Implicit { get; }

    private FtpsServer(ServiceProvider provider, IFtpServerHost host, string root, int port, string identity, bool isImplicit, string password)
    {
        _provider = provider; _host = host; Root = root; Port = port; Identity = identity; Implicit = isImplicit; Password = password;
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

    public static async Task<FtpsServer> StartAsync(string root, bool isImplicit = false, X509Certificate2? certificate = null, int? port = null, bool plain = false, string password = "p")
    {
        certificate ??= NewCertificate();
        int p = port ?? FreePort();
        var services = new ServiceCollection();
        if (!plain) services.Configure<AuthTlsOptions>(o => { o.ServerCertificate = certificate; o.ImplicitFtps = isImplicit; });
        services.Configure<DotNetFileSystemOptions>(o => o.RootPath = root);
        services.Configure<FtpServerOptions>(o => { o.ServerAddress = "127.0.0.1"; o.Port = p; });
        services.AddSingleton(new FixedMembership(password));
        services.AddSingleton<IMembershipProvider>(sp => sp.GetRequiredService<FixedMembership>());
        services.AddSingleton<IMembershipProviderAsync>(sp => sp.GetRequiredService<FixedMembership>());
        services.AddFtpServer(b => b.UseDotNetFileSystem().UseSingleRoot());
        var provider = services.BuildServiceProvider();
        var host = provider.GetRequiredService<IFtpServerHost>();
        await host.StartAsync(CancellationToken.None);
        return new FtpsServer(provider, host, root, p, FtpRemoteClient.FormatFingerprint(certificate), isImplicit, password);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        await _provider.DisposeAsync();
    }

    private sealed class FixedMembership(string password) : IMembershipProviderAsync, IMembershipProvider
    {
        public Task<MemberValidationResult> ValidateUserAsync(string username, string pass, CancellationToken cancellationToken)
        {
            if (username != "u" || pass != password) return Task.FromResult(new MemberValidationResult(MemberValidationStatus.InvalidLogin));
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], "test");
            return Task.FromResult(new MemberValidationResult(MemberValidationStatus.AuthenticatedUser, new ClaimsPrincipal(identity)));
        }

        public Task<MemberValidationResult> ValidateUserAsync(string username, string pass) => ValidateUserAsync(username, pass, CancellationToken.None);

        public Task LogOutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) => Task.CompletedTask;
    }
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


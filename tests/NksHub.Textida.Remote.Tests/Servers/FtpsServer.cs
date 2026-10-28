using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FubarDev.FtpServer;
using FubarDev.FtpServer.AccountManagement;
using FubarDev.FtpServer.Commands;
using FubarDev.FtpServer.Features;
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
    private static int _held;

    /// <summary>
    /// FubarDev.FtpServer 3.1.2 starts a connection service with a local <c>SemaphoreSlim</c> that a <c>Progress&lt;T&gt;</c> callback releases on the thread pool; when the connection is cancelled while it starts, the method
    /// has returned and disposed the semaphore before the callback runs, and the <see cref="ObjectDisposedException"/> has no catcher. That ended the whole test host in the middle of a run (a log of a run on a slow machine
    /// stopped after 54 of 82 tests and said "Test host process crashed"). The runtime ends the process only after the handlers of <c>UnhandledException</c> have returned, so a handler that knows this one holds its thread for good
    /// (one pool thread per occurrence) and leaves every other unhandled exception alone.
    /// </summary>
    internal static Func<Exception, bool> IsKnownServerRace { get; set; } = e =>
        e is ObjectDisposedException { ObjectName: "System.Threading.SemaphoreSlim" } && e.StackTrace?.Contains("FubarDev.FtpServer.Networking.PausableFtpService", StringComparison.Ordinal) == true;

    /// <summary>How many times <see cref="IsKnownServerRace"/> held an exception in this process.</summary>
    internal static int HeldServerRaces => Volatile.Read(ref _held);

    static FtpsServer() =>
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception exception || !IsKnownServerRace(exception)) return;
            Interlocked.Increment(ref _held);
            Console.Error.WriteLine("The FTP test server hit its known shutdown race (" + exception.GetType().Name + " in " + exception.TargetSite?.DeclaringType?.Name + "); the thread is held so that the test host stays up.");
            Thread.Sleep(Timeout.Infinite);
        };

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

    public static async Task<FtpsServer> StartAsync(string root, bool isImplicit = false, X509Certificate2? certificate = null, int? port = null, bool plain = false, string password = "p", TimeSpan? commandLag = null)
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
        if (commandLag is { } lag) services.AddSingleton<IFtpCommandMiddleware>(new LagAfterTheResponse(lag));
        services.AddSingleton<IFtpMiddleware, WaitForTheRunningCommand>();
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

    /// <summary>
    /// FubarDev runs RNFR, RNTO and the other commands it declares abortable as a background task and answers the next such command with
    /// "503 Parallel commands aren't allowed" until its dispatcher noticed that the task ended. The task ends a moment after the reply is
    /// queued, so a client that sends RNTO the instant it has read the 350 of RNFR (as every client must) is refused on a loaded machine.
    /// This waits for that task before the next command is dispatched; ABOR is the one command that is meant to arrive meanwhile.
    /// </summary>
    private sealed class WaitForTheRunningCommand : IFtpMiddleware
    {
        public async Task InvokeAsync(FtpContext context, FtpRequestDelegate next)
        {
            var features = context.Connection.Features;
            if (features.Get<IBackgroundTaskLifetimeFeature?>() is { } running && !context.Command.Name.Equals("ABOR", StringComparison.OrdinalIgnoreCase))
            {
                await Task.WhenAny(running.Task, Task.Delay(TimeSpan.FromSeconds(10)));
                if (running.Task.IsCompleted) features.Set<IBackgroundTaskLifetimeFeature?>(null);
            }
            await next(context);
        }
    }

    /// <summary>Keeps every command running for a while after its reply was queued, which widens the window of the race <see cref="WaitForTheRunningCommand"/> closes.</summary>
    private sealed class LagAfterTheResponse(TimeSpan lag) : IFtpCommandMiddleware
    {
        public async Task InvokeAsync(FtpExecutionContext context, FtpCommandExecutionDelegate next)
        {
            await next(context);
            await Task.Delay(lag);
        }
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


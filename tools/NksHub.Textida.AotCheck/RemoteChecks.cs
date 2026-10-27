using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Remote;

/// <summary>
/// The remote file code under NativeAOT: JSON models, the credential store (native calls), the proxy policy and the two clients. With
/// <c>--live sftp PORT IDENTITY PASSWORD</c> or <c>--live ftps PORT IDENTITY PASSWORD</c> it also talks to a server on localhost that serves a file
/// <c>/aot.txt</c> with the text "server": it opens a working copy, uploads an edit, and expects a conflict after the file was changed through a second connection.
/// </summary>
internal static class RemoteChecks
{
    private sealed class Deny : IServerTrustPrompt
    {
        public ValueTask<bool> ConfirmAsync(RemoteConnection connection, ServerIdentity presented, TrustReason reason, CancellationToken cancellation) => ValueTask.FromResult(false);
    }

    public static async Task RunAsync(string[] args, Action<bool, string> check)
    {
        var connection = new RemoteConnection { Name = "A", Host = "192.0.2.1", UserName = "u", Proxy = new(ProxyType.Socks5, "198.51.100.1", 1080, "pu") };
        string json = ConnectionStore.Export([connection]);
        check(ConnectionStore.Import(json).Single() == connection, "connection json round trip");
        check(!json.Contains("secret", StringComparison.OrdinalIgnoreCase), "no secret field in the connection json");

        var credentials = CredentialStores.ForCurrentPlatform();
        if (OperatingSystem.IsWindows())
        {
            string key = "textida:aotcheck:" + Guid.NewGuid().ToString("N");
            credentials.Set(key, "s3cret-ünï");
            check(credentials.Get(key) == "s3cret-ünï", "credential store round trip");
            check(credentials.Delete(key) && credentials.Get(key) is null, "credential store delete");
        }

        string dir = Directory.CreateTempSubdirectory("textida-aotcheck-").FullName;
        try
        {
            string policyFile = Path.Combine(dir, "network.json");
            var memory = new Memory();
            NetworkPolicy.Save(policyFile, new ProxySettings(ProxyType.Http, "198.51.100.2", 3128, "pu"), "pp", memory);
            var policy = NetworkPolicy.Load(policyFile, memory);
            check(policy.Proxy.Type == ProxyType.Http && policy.ProxyPassword == "pp", "network policy round trip");
            using (policy.CreateHttpHandler()) { }

            check(RemoteClients.Create(connection with { Protocol = RemoteProtocol.Sftp }, memory, new Deny()) is SftpRemoteClient, "sftp client is created");
            check(RemoteClients.Create(connection with { Protocol = RemoteProtocol.Ftps }, memory, new Deny()) is FtpRemoteClient, "ftps client is created");
            bool refused = false;
            try { RemoteClients.Create(connection with { Protocol = RemoteProtocol.Ftp }, memory, new Deny()); }
            catch (RemoteException e) when (e.Error == RemoteError.UnsupportedCombination) { refused = true; }
            check(refused, "plain ftp is refused without the explicit choice");

            int live = Array.IndexOf(args, "--live");
            if (live >= 0) await LiveAsync(args[live + 1], int.Parse(args[live + 2]), args[live + 3], args[live + 4], dir, check);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static async Task LiveAsync(string kind, int port, string identity, string password, string dir, Action<bool, string> check)
    {
        var connection = new RemoteConnection
        {
            Name = "live", Protocol = kind == "sftp" ? RemoteProtocol.Sftp : RemoteProtocol.Ftps, Host = "127.0.0.1", Port = port, UserName = "u", TrustedIdentity = identity,
        };
        var secrets = new Memory();
        secrets.Set(connection.SecretKey, password);
        await using var client = RemoteClients.Create(connection, secrets, new Deny());
        await client.ConnectAsync(default);
        var store = new WorkingCopyStore(Path.Combine(dir, "cache"));
        var copy = await store.OpenAsync(client, "/aot.txt", default);
        check(File.ReadAllText(copy.LocalPath) == "server", $"{kind}: working copy has the server content");

        File.WriteAllText(copy.LocalPath, "from aot check");
        var sent = await RemoteSync.UploadAsync(client, store, copy, VersionCheck.Stat, null, null, default);
        check(sent.Status == UploadStatus.Uploaded, $"{kind}: upload");
        var ms = new MemoryStream();
        await client.DownloadAsync("/aot.txt", ms, default);
        check(System.Text.Encoding.UTF8.GetString(ms.ToArray()) == "from aot check", $"{kind}: server holds the uploaded content");

        await using var other = RemoteClients.Create(connection, secrets, new Deny());
        await other.ConnectAsync(default);
        await other.UploadAsync("/aot.txt", new MemoryStream("someone else"u8.ToArray()), default);
        File.WriteAllText(sent.Copy.LocalPath, "second edit");
        var blocked = await RemoteSync.UploadAsync(client, store, sent.Copy, VersionCheck.Content, null, null, default);
        check(blocked.Status == UploadStatus.Conflict && System.Text.Encoding.UTF8.GetString(blocked.ServerContent!) == "someone else", $"{kind}: conflict is detected");
        Console.WriteLine($"live {kind}: done");
    }

    private sealed class Memory : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string value) => _values[key] = value;
        public bool Delete(string key) => _values.Remove(key);
    }
}

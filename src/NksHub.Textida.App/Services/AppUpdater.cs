using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace NksHub.Textida.App.Services;

/// <summary>The one build the signed feed offers: <paramref name="Url"/> may be relative to the feed; <paramref name="Sha256"/> is hex, <paramref name="Size"/> in bytes.</summary>
internal sealed record UpdateRelease(string Version, string Rid, string Url, string Sha256, long Size);

internal enum UpdateError { NotConfigured, Network, BadFeed, BadSignature, WrongSystem, BadPackage, NoPrevious }

internal sealed class UpdateException(UpdateError error, string message, Exception? inner = null) : Exception(message, inner)
{
    public UpdateError Error { get; } = error;
}

/// <summary>
/// P3.7.2: the updater of the per-user Windows install. The feed is one static JSON file that is signed with ECDSA P-256 (SHA-256); the package is a ZIP made by
/// tools/BuildWindowsPackage.cs whose SHA-256 and size are inside the signed part. Checking is a single GET of the feed and sends nothing else. The new version is unpacked
/// next to the old one (<c>Textida.new</c>) and verified there; the switch is done by Setup.ps1 after Textida has ended, because a running program cannot replace its own folder.
/// The old folder stays as <c>Textida.previous</c> and <see cref="Rollback"/> swaps back.
/// </summary>
internal sealed class AppUpdater(HttpClient http, string feedUrl, string publicKeyBase64, string installDir)
{
    /// <summary>OPEN DECISION: the address of the feed is not decided; empty means the build cannot check for updates.</summary>
    public const string DefaultFeedUrl = "";

    /// <summary>OPEN DECISION: the update signing key does not exist yet. Base64 of the SubjectPublicKeyInfo of the ECDSA P-256 public key; empty means the build cannot verify, so it does not check.</summary>
    public const string DefaultPublicKey = "";

    public const int FeedFormat = 1;
    private const int MaxFeedBytes = 64 * 1024;

    public string InstallDir { get; } = installDir.TrimEnd('\\', '/');
    public string StagingDir => InstallDir + ".new";
    public string PreviousDir => InstallDir + ".previous";

    /// <summary>The registry key of Apps &amp; features that Setup.ps1 keeps in step; null is the real one. Tests point it elsewhere.</summary>
    public string? UninstallKey { get; init; }

    /// <summary>The process the helper waits for before it touches the folders; tests set 0 because their host keeps running.</summary>
    public int WaitPid { get; init; } = Environment.ProcessId;

    public bool IsConfigured => feedUrl.Length > 0 && publicKeyBase64.Length > 0;

    /// <summary>Update and return need the installer script next to the program.</summary>
    public bool IsInstalled => File.Exists(Path.Combine(InstallDir, "Textida.exe")) && File.Exists(Path.Combine(InstallDir, "Setup.ps1"));

    public bool HasPrevious => File.Exists(Path.Combine(PreviousDir, "Textida.exe"));

    /// <summary>The identifier of this system in the feed and in the package name, like <c>win-x64</c>.</summary>
    public static string CurrentRid => (OperatingSystem.IsWindows() ? "win-" : "other-") + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>The version of the installed package: <c>version.txt</c> next to the program, else the assembly.</summary>
    public static string InstalledVersion(string dir)
    {
        try { if (File.ReadAllText(Path.Combine(dir, "version.txt")).Trim() is { Length: > 0 } v) return v; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a portable copy without the file */ }
        return typeof(AppUpdater).Assembly.GetName().Version?.ToString(3) ?? "0";
    }

    /// <summary>The signed feed text for <paramref name="release"/>; the release tooling and the tests build their feeds with it.</summary>
    public static string CreateFeed(ECDsa key, UpdateRelease release)
    {
        var payload = new JsonObject { ["version"] = release.Version, ["rid"] = release.Rid, ["url"] = release.Url, ["sha256"] = release.Sha256, ["size"] = release.Size }.ToJsonString();
        var signature = key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256);
        return new JsonObject { ["format"] = FeedFormat, ["payload"] = payload, ["signature"] = Convert.ToBase64String(signature) }.ToJsonString();
    }

    /// <summary>Fetches and verifies the feed. Null when it offers nothing newer than <paramref name="currentVersion"/> (an older or equal version is ignored).</summary>
    public async Task<UpdateRelease?> CheckAsync(string currentVersion, string rid, CancellationToken cancel)
    {
        if (!IsConfigured) throw new UpdateException(UpdateError.NotConfigured, "No update address or signing key is set in this build.");
        var feedUri = RequireSecure(new Uri(feedUrl));
        var text = Encoding.UTF8.GetString(await GetBytesAsync(feedUri, MaxFeedBytes, cancel));
        var release = Verify(text);
        if (release.Rid != rid) throw new UpdateException(UpdateError.WrongSystem, $"The feed offers {release.Rid}, this system is {rid}.");
        return Compare(release.Version, currentVersion) > 0 ? release with { Url = new Uri(feedUri, release.Url).AbsoluteUri } : null;
    }

    private UpdateRelease Verify(string text)
    {
        JsonObject root;
        try { root = JsonNode.Parse(text) as JsonObject ?? throw new FormatException(); }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException) { throw new UpdateException(UpdateError.BadFeed, "The feed is not valid JSON.", e); }
        if (root["format"]?.GetValue<int>() != FeedFormat) throw new UpdateException(UpdateError.BadFeed, "The feed has a format this Textida does not know.");
        string? payload = root["payload"]?.GetValue<string>(), signature = root["signature"]?.GetValue<string>();
        if (payload is null || signature is null) throw new UpdateException(UpdateError.BadFeed, "The feed has no payload or signature.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            if (!key.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256))
                throw new UpdateException(UpdateError.BadSignature, "The signature of the feed does not match.");
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { throw new UpdateException(UpdateError.BadSignature, "The signature of the feed cannot be verified.", e); }
        try
        {
            var p = (JsonNode.Parse(payload) as JsonObject)!;
            var release = new UpdateRelease(p["version"]!.GetValue<string>(), p["rid"]!.GetValue<string>(), p["url"]!.GetValue<string>(), p["sha256"]!.GetValue<string>(), p["size"]!.GetValue<long>());
            if (release.Size <= 0 || release.Sha256.Length != 64 || release.Version.Length == 0) throw new FormatException();
            _ = Compare(release.Version, "0");
            return release;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or NullReferenceException or InvalidOperationException)
        {
            throw new UpdateException(UpdateError.BadFeed, "The signed part of the feed is incomplete.", e);
        }
    }

    /// <summary>Downloads the package into <paramref name="folder"/> and checks it against the signed size and SHA-256. A mismatch deletes it.</summary>
    public async Task<string> DownloadAsync(UpdateRelease release, string folder, CancellationToken cancel)
    {
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, "Textida-" + release.Version + ".zip");
        try
        {
            using var file = new FileStream(zip, FileMode.Create, FileAccess.ReadWrite);
            await FetchAsync(RequireSecure(new Uri(release.Url)), release.Size, file, cancel);
            file.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancel));
            if (file.Length == release.Size && hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) return zip;
        }
        catch { File.Delete(zip); throw; }
        File.Delete(zip);
        throw new UpdateException(UpdateError.BadPackage, "The downloaded package does not match the signed checksum.");
    }

    /// <summary>Unpacks the verified ZIP next to the installed folder and checks that it is a whole package of <paramref name="release"/>. The running version is not touched.</summary>
    public string Stage(string zip, UpdateRelease release)
    {
        ClearStaging();
        try
        {
            ZipFile.ExtractToDirectory(zip, StagingDir);
            File.Delete(Path.Combine(StagingDir, "Install.cmd"));
            foreach (var required in new[] { "Textida.exe", "Setup.ps1", "version.txt" })
                if (!File.Exists(Path.Combine(StagingDir, required))) throw new UpdateException(UpdateError.BadPackage, $"The package has no {required}.");
            if (File.ReadAllText(Path.Combine(StagingDir, "version.txt")).Trim() != release.Version)
                throw new UpdateException(UpdateError.BadPackage, "The version inside the package is not the version the feed announced.");
            return StagingDir;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ClearStaging();
            throw new UpdateException(UpdateError.BadPackage, "The package cannot be unpacked: " + e.Message, e);
        }
        catch (UpdateException) { ClearStaging(); throw; }
    }

    private void ClearStaging()
    {
        if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, recursive: true);
    }

    /// <summary>
    /// Starts the switch of the staged folder (<paramref name="staged"/> is the result of <see cref="Stage"/>) and returns the helper; the caller ends Textida at once. The helper is a copy of
    /// the installer script run from a temporary folder, waits for this process to end, and starts Textida again when <paramref name="restart"/> is set.
    /// </summary>
    public Process Switch(string staged, bool restart) => StartScript(["-ApplyUpdate", staged], restart);

    /// <summary>Same as <see cref="Switch"/> for the return to <c>Textida.previous</c>; the version that is running now becomes the previous one.</summary>
    public Process Rollback(bool restart)
    {
        if (!HasPrevious) throw new UpdateException(UpdateError.NoPrevious, "There is no previous version.");
        return StartScript(["-Rollback"], restart);
    }

    private Process StartScript(string[] mode, bool restart)
    {
        var script = Path.Combine(Path.GetTempPath(), "textida-update-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(script);
        File.Copy(Path.Combine(InstallDir, "Setup.ps1"), Path.Combine(script, "Setup.ps1"));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = script };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(script, "Setup.ps1"), "-Dir", InstallDir }.Concat(mode)) start.ArgumentList.Add(a);
        if (WaitPid > 0) { start.ArgumentList.Add("-WaitPid"); start.ArgumentList.Add(WaitPid.ToString()); }
        if (UninstallKey is not null) { start.ArgumentList.Add("-UninstallKey"); start.ArgumentList.Add(UninstallKey); }
        if (restart) start.ArgumentList.Add("-Restart");
        return Process.Start(start)!;
    }

    /// <summary>The comparison of versions like 1.2.3 or 1.2.3-rc1: the numbers first, and a version without suffix is newer than the same numbers with one.</summary>
    public static int Compare(string a, string b)
    {
        static (int[] Numbers, string Suffix) Split(string v)
        {
            int cut = v.IndexOfAny(['-', '+']);
            return ([.. (cut < 0 ? v : v[..cut]).Split('.').Select(int.Parse)], cut < 0 ? "" : v[cut..]);
        }
        var (an, asuffix) = Split(a);
        var (bn, bsuffix) = Split(b);
        for (int i = 0; i < Math.Max(an.Length, bn.Length); i++)
        {
            int x = i < an.Length ? an[i] : 0, y = i < bn.Length ? bn[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        if (asuffix == bsuffix) return 0;
        if (asuffix.Length == 0) return 1;
        return bsuffix.Length == 0 ? -1 : string.CompareOrdinal(asuffix, bsuffix);
    }

    private static Uri RequireSecure(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ? uri
            : throw new UpdateException(UpdateError.BadFeed, "Updates are fetched over https only.");

    private async Task<byte[]> GetBytesAsync(Uri uri, long limit, CancellationToken cancel)
    {
        using var buffer = new MemoryStream();
        await FetchAsync(uri, limit, buffer, cancel);
        return buffer.ToArray();
    }

    private async Task FetchAsync(Uri uri, long limit, Stream sink, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new UpdateException(UpdateError.BadPackage, "The server sent more than the signed size.");
            await using var body = await response.Content.ReadAsStreamAsync(cancel);
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(chunk, cancel)) > 0)
            {
                total += read;
                if (total > limit) throw new UpdateException(UpdateError.BadPackage, "The server sent more than the signed size.");
                await sink.WriteAsync(chunk.AsMemory(0, read), cancel);
            }
        }
        catch (Exception e) when (e is HttpRequestException or IOException) { throw new UpdateException(UpdateError.Network, e.Message, e); }
    }
}

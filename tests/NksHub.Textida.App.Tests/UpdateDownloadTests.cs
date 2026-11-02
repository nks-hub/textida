using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>The package download of the updater: a stalled or dropped connection resumes with a Range request, a server without ranges starts over, and a cancel leaves no partial file.</summary>
public sealed class UpdateDownloadTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly byte[] _package = RandomNumberGenerator.GetBytes(300_000);

    public void Dispose() => _temp.Dispose();

    private UpdateRelease Release => new("1.1.0", "win-x64", "https://example.test/p.zip", Convert.ToHexString(SHA256.HashData(_package)), _package.Length);

    private AppUpdater Updater(HttpMessageHandler handler, TimeSpan? stall = null) =>
        new(new HttpClient(handler), "https://example.test/feed.json", "key", Path.Combine(_temp.Path, "Textida")) { StallTimeout = stall ?? TimeSpan.FromMilliseconds(300) };

    private string Folder => Path.Combine(_temp.Path, "download");

    [Fact]
    public async Task A_download_that_stalls_resumes_from_the_bytes_it_has_and_is_verified()
    {
        var handler = new ScriptedHandler(request => request.Number == 1
            ? Response(HttpStatusCode.OK, _package, stallAfter: 100_000)
            : Response(HttpStatusCode.PartialContent, _package[(int)request.From!.Value..], range: (request.From.Value, _package.Length)));
        long last = 0;

        var zip = await Updater(handler).DownloadAsync(Release, Folder, CancellationToken.None, done => last = done);

        Assert.Equal(_package, await File.ReadAllBytesAsync(zip));
        Assert.Equal([null, 100_000L], handler.Ranges);
        Assert.Equal(_package.Length, last);
    }

    [Fact]
    public async Task A_server_that_ignores_the_range_request_starts_the_file_over()
    {
        var handler = new ScriptedHandler(request => request.Number == 1
            ? Response(HttpStatusCode.OK, _package, stallAfter: 100_000)
            : Response(HttpStatusCode.OK, _package));

        var zip = await Updater(handler).DownloadAsync(Release, Folder, CancellationToken.None);

        Assert.Equal(_package, await File.ReadAllBytesAsync(zip));
        Assert.Equal([null, 100_000L], handler.Ranges);
    }

    [Fact]
    public async Task A_download_that_never_delivers_fails_with_the_silence_in_the_message_and_leaves_no_file()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK, _package, stallAfter: 1000));

        var error = await Assert.ThrowsAsync<UpdateException>(() => Updater(handler, TimeSpan.FromMilliseconds(100)).DownloadAsync(Release, Folder, CancellationToken.None));

        Assert.Equal(UpdateError.Network, error.Error);
        Assert.Contains("No data from the server", error.Message);
        Assert.Equal(AppUpdater.MaxDownloadAttempts, handler.Ranges.Count);
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public async Task A_cancel_stops_the_download_and_deletes_the_partial_file()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK, _package, stallAfter: 100_000));
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Updater(handler, TimeSpan.FromSeconds(60))
            .DownloadAsync(Release, Folder, cancel.Token, done => { if (done > 0) cancel.Cancel(); }));

        Assert.Empty(Directory.GetFiles(Folder));
        Assert.Single(handler.Ranges);
    }

    [Theory]
    [InlineData("https://github.com/nks-hub/textida/releases/download/v0.1.4/Textida-0.1.4-win-x64.zip", "https://github.com/nks-hub/textida/releases/tag/v0.1.4")]
    [InlineData("https://example.test/p.zip", "https://github.com/nks-hub/textida/releases")]
    public void The_release_page_comes_from_the_package_address(string url, string page) =>
        Assert.Equal(page, AppUpdater.ReleasePage(Release with { Url = url }));

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body, int? stallAfter = null, (long From, long Total)? range = null)
    {
        var content = new StreamContent(new StallingStream(body, stallAfter));
        content.Headers.ContentLength = body.Length;
        if (range is { } r) content.Headers.ContentRange = new ContentRangeHeaderValue(r.From, r.Total - 1, r.Total);
        return new HttpResponseMessage(status) { Content = content };
    }

    internal sealed record Seen(int Number, long? From);

    /// <summary>Answers each request from a script and records the start of its Range header.</summary>
    internal sealed class ScriptedHandler(Func<Seen, HttpResponseMessage> script) : HttpMessageHandler
    {
        public List<long?> Ranges { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            long? from = request.Headers.Range?.Ranges.First().From;
            Ranges.Add(from);
            return Task.FromResult(script(new Seen(Ranges.Count, from)));
        }
    }

    /// <summary>Delivers <c>stallAfter</c> bytes (all when null), then waits until the read is cancelled, like a connection that went silent.</summary>
    private sealed class StallingStream(byte[] data, int? stallAfter) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int end = stallAfter ?? data.Length;
            if (_position >= end)
            {
                if (stallAfter is null) return 0;
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            int count = Math.Min(Math.Min(buffer.Length, 40_000), end - _position);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

[Collection("LocState")]
public class UpdateInstallFailureTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly FeedServer _server = new();

    public void Dispose()
    {
        if (Avalonia.Application.Current is App app && app.Journal is not null) TestApp.Cleanup(app);
        _server.Dispose();
        _temp.Dispose();
    }

    private sealed class ThrowingOnPackage : DelegatingHandler
    {
        public ThrowingOnPackage() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("p.zip", StringComparison.Ordinal) ? throw new CryptographicException("unexpected kind of failure") : base.SendAsync(request, cancellationToken);
    }

    [AvaloniaFact]
    public async Task Any_exception_during_the_install_shows_a_bar_with_the_reason_and_the_release_page_button()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the per-user install is Windows only");
        var dir = Path.Combine(_temp.Path, "Textida");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Textida.exe"), "");
        File.WriteAllText(Path.Combine(dir, "Setup.ps1"), "");
        File.WriteAllText(Path.Combine(dir, "version.txt"), "1.0.0");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Routes["/feed.json"] = Encoding.UTF8.GetBytes(AppUpdater.CreateFeed(key, new UpdateRelease("1.1.0", AppUpdater.CurrentRid, "p.zip", new string('0', 64), 10)));
        var dialogs = new ScriptedDialogs();
        dialogs.Answers.Enqueue(0);
        var app = TestApp.Prepare(_temp, dialogs);
        var w = app.CreateWindow(null);
        w.UpdaterOverride = new AppUpdater(new HttpClient(new ThrowingOnPackage()), _server.Base + "feed.json", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), dir);

        w.CheckForUpdates();
        await w.UpdateTask;

        var bar = Assert.Single(w.Notices, n => n.Message.Contains("unexpected kind"));
        Assert.True(w.Notices.Count == 1, string.Join(" | ", w.Notices.Select(n => n.Message)));
        Assert.Contains("unexpected kind of failure", bar.Message);
        Assert.StartsWith(Loc.Get(K.UpdateFailed, ("reason", "")).TrimEnd(), bar.Message);
        Assert.Equal([Loc.Get(K.UpdateReleasePage)], bar.Buttons.Select(b => (string)b.Content!));
        w.Close();
    }
}

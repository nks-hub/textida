using System.Security.Cryptography;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manager;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The plugin catalog (PZ.8.1): the list of plugins in a Git repository the user names in <c>plugins.catalogUrl</c>. A plugin is installed only when the SHA-256 of its ZIP and the signature of its publisher match the
/// index, the key of the publisher was confirmed by the user, and what its manifest asks for is within what the index states; the install itself is the one of the plugin manager, so an update that asks for
/// more permissions shows the new ones and needs a new consent.
/// </summary>
internal sealed partial class MainWindow
{
    internal const string CatalogSetting = "plugins.catalogUrl";
    private const string CatalogNetworkTool = "formatter-or-linter"; // the tool id the proxy of the editor gives to every tool of the catalog
    private const long CatalogMaxPackageBytes = 64L << 20;

    /// <summary>Hook of the tests: where the ZIP of an https package comes from instead of the network.</summary>
    internal Func<Uri, Task<byte[]?>>? CatalogDownloadOverride { get; set; }

    internal CommandOutcome OpenPluginCatalog(bool refresh)
    {
        _ = ShowPluginCatalogAsync(refresh);
        return CommandOutcome.Handled;
    }

    private CatalogTrust Trust => new(Path.Combine(_app.ProfileDirectory, "plugin-publishers.json"));

    private async Task ShowCatalogMessageAsync(string text, params string[] details) =>
        await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.CatalogTitle), text, [new DialogButton(Loc.Get(K.DialogOk), IsDefault: true)], details.Length == 0 ? null : details));

    /// <summary>Lists the plugins of the catalog; with <paramref name="refresh"/>, or when there is no copy yet, the repository is cloned again first.</summary>
    internal async Task ShowPluginCatalogAsync(bool refresh)
    {
        string source = _app.Workbench.Settings.GetString(CatalogSetting).Trim();
        if (source.Length == 0)
        {
            SetStatusMessage(Loc.Get(K.CatalogNoSource));
            return;
        }
        if (!PluginCatalogSource.IsSource(source))
        {
            SetStatusMessage(Loc.Get(K.CatalogBadSource, ("source", source)));
            return;
        }
        string folder = PluginCatalogSource.CacheFolder(_app.ProfileDirectory, source);
        if (refresh || !File.Exists(Path.Combine(folder, PluginCatalog.IndexFile)))
        {
            if (ToolLocator.Find("git") is not { } git)
            {
                SetStatusMessage(Loc.Get(K.CatalogNoGit));
                return;
            }
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !await AllowCatalogHostAsync(uri.Host)) return;
            SetStatusMessage(Loc.Get(K.CatalogCloning, ("source", source)));
            string? problem = await PluginCatalogSource.RefreshAsync(git, _app.ProfileDirectory, source, tool => ApproveToolAsync(tool, "Git"));
            if (problem == "cancelled") return;
            if (problem is not null)
            {
                SetStatusMessage(Loc.Get(K.CatalogRefreshFailed, ("reason", problem)));
                return;
            }
        }
        if (PluginCatalogSource.ReadIndex(folder) is not { } index)
        {
            SetStatusMessage(Loc.Get(K.CatalogNoIndex));
            return;
        }
        ShowCatalogRows(source, folder, index);
    }

    private async Task<bool> AllowCatalogHostAsync(string host)
    {
        var consent = _app.Plugins.ToolNetwork;
        if (consent.IsAllowed(CatalogNetworkTool, host)) return true;
        var ask = new ChoiceRequest(Loc.Get(K.CatalogTitle), Loc.Get(K.CatalogHostConsent, ("host", host)), [new DialogButton(Loc.Get(K.HttpConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        consent.Allow(CatalogNetworkTool, [host]);
        return true;
    }

    private void ShowCatalogRows(string source, string folder, PluginCatalogIndex index)
    {
        var installed = _app.Plugins.Manager.Installed().ToDictionary(p => p.Id, p => p.Version, StringComparer.Ordinal);
        var rows = new List<QuickRow>();
        if (index.Problems.Count > 0)
            rows.Add(new QuickRow(Loc.Get(K.CatalogProblems, ("problems", index.Problems.Count)), null, string.Join(" ", index.Problems.Take(3)), null, true, null, Loc.Get(K.CatalogProblems, ("problems", index.Problems.Count)), () => _ = ShowCatalogMessageAsync(Loc.Get(K.CatalogProblems, ("problems", index.Problems.Count)), [.. index.Problems])));
        foreach (var entry in index.Entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            var publisher = index.Publishers.FirstOrDefault(p => p.Id == entry.Publisher);
            string state = !installed.TryGetValue(entry.Id, out string? have) ? Loc.Get(K.CatalogStateNew)
                : PluginCatalog.IsNewer(entry.Version, have) ? Loc.Get(K.CatalogStateUpdate, ("installed", have)) : Loc.Get(K.CatalogStateInstalled, ("version", have));
            string detail = $"{(publisher?.Name ?? entry.Publisher)}  ·  {state}" + (entry.Description.Length > 0 ? "  ·  " + entry.Description : "");
            string title = $"{entry.Name} {entry.Version}";
            bool usable = publisher is not null;
            var captured = entry;
            rows.Add(new QuickRow(title, null, detail, null, usable, usable ? null : Loc.Get(K.CatalogUnknownPublisher, ("publisher", entry.Publisher)), title, () => _ = InstallFromCatalogAsync(folder, index, captured)));
        }
        if (rows.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.CatalogEmpty));
            return;
        }
        ShowChoice(Loc.Get(K.CatalogWatermark), Loc.Get(K.CatalogA11y), rows);
    }

    private string CatalogCheckText(CatalogCheck check, CatalogEntry entry) => Loc.Get(check switch
    {
        CatalogCheck.UnknownPublisher => K.CatalogUnknownPublisher,
        CatalogCheck.BadKey => K.CatalogBadKey,
        CatalogCheck.BadSignature => K.CatalogBadSignature,
        _ => K.CatalogBadHash,
    }, ("publisher", entry.Publisher), ("name", entry.Name));

    internal async Task<bool> InstallFromCatalogAsync(string folder, PluginCatalogIndex index, CatalogEntry entry)
    {
        var publisher = index.Publishers.FirstOrDefault(p => p.Id == entry.Publisher);
        var signature = PluginCatalog.CheckSignature(entry, publisher);
        if (signature != CatalogCheck.Ok)
        {
            await ShowCatalogMessageAsync(CatalogCheckText(signature, entry));
            return false;
        }
        var trust = Trust;
        switch (trust.Of(publisher!))
        {
            case PublisherTrust.Changed:
                await ShowCatalogMessageAsync(Loc.Get(K.CatalogKeyChanged, ("publisher", publisher!.Name)), publisher.Fingerprint);
                return false;
            case PublisherTrust.New:
                var ask = new ChoiceRequest(Loc.Get(K.CatalogTrustTitle), Loc.Get(K.CatalogTrustMessage, ("publisher", publisher!.Name), ("id", publisher.Id)),
                    [new DialogButton(Loc.Get(K.CatalogTrustAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], [Loc.Get(K.CatalogFingerprint, ("fingerprint", publisher.Fingerprint))]);
                if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
                trust.Trust(publisher);
                break;
        }

        string zip = Path.Combine(Path.GetTempPath(), "textida-catalog-" + Guid.NewGuid().ToString("N")[..12] + ".zip");
        try
        {
            if (!await FetchCatalogPackageAsync(folder, entry, zip)) return false;
            if (PluginCatalog.CheckHash(entry, zip) != CatalogCheck.Ok)
            {
                await ShowCatalogMessageAsync(CatalogCheckText(CatalogCheck.BadHash, entry));
                return false;
            }
            var installed = _app.Plugins.Manager.Installed().FirstOrDefault(p => p.Id == entry.Id);
            return await InstallPluginAsync(zip, installed is null ? null : entry.Id, entry);
        }
        finally
        {
            try { File.Delete(zip); } catch (IOException) { /* the temporary copy stays for the system to clean */ }
        }
    }

    /// <summary>Puts the ZIP of the entry at <paramref name="target"/>: a copy of the file in the repository, or the download of an https package. False (with the reason shown) when it could not.</summary>
    private async Task<bool> FetchCatalogPackageAsync(string folder, CatalogEntry entry, string target)
    {
        try
        {
            if (!Uri.TryCreate(entry.Package, UriKind.Absolute, out var uri))
            {
                if (PluginCatalogSource.LocalPackage(folder, entry.Package) is not { } local || new FileInfo(local).Length > CatalogMaxPackageBytes)
                {
                    await ShowCatalogMessageAsync(Loc.Get(K.CatalogNoPackage, ("package", entry.Package)));
                    return false;
                }
                File.Copy(local, target, true);
                return true;
            }
            byte[]? bytes;
            if (CatalogDownloadOverride is { } over) bytes = await over(uri);
            else
            {
                if (!await AllowCatalogHostAsync(uri.Host)) return false;
                SetStatusMessage(Loc.Get(K.CatalogDownloading, ("name", entry.Name)));
                using var client = new HttpClient(_app.Remote.Policy().CreateHttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(3) };
                var download = await HttpSender.DownloadAsync(client, uri, (int)CatalogMaxPackageBytes, CancellationToken.None);
                if (!download.Success || download.Truncated)
                {
                    await ShowCatalogMessageAsync(Loc.Get(K.CatalogDownloadFailed, ("reason", download.Truncated ? Loc.Get(K.CatalogTooLarge, ("limit", CatalogMaxPackageBytes >> 20)) : $"HTTP {download.Status}")));
                    return false;
                }
                bytes = download.Bytes;
            }
            if (bytes is null || bytes.Length > CatalogMaxPackageBytes)
            {
                await ShowCatalogMessageAsync(Loc.Get(K.CatalogDownloadFailed, ("reason", "-")));
                return false;
            }
            await File.WriteAllBytesAsync(target, bytes);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or RemoteException or InvalidOperationException)
        {
            await ShowCatalogMessageAsync(Loc.Get(K.CatalogDownloadFailed, ("reason", e.InnerException?.Message ?? e.Message)));
            return false;
        }
    }
}

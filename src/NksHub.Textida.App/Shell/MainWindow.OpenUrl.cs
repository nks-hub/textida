using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Open from the internet (PZ.2.1): the text of an http or https address in a new tab. The host needs the user's consent (the same one as for requests of a <c>.http</c> file), the request goes through the network policy of the editor,
/// and what comes back is shown as text: it is not rendered, run, saved or opened with another program. A page that is not text, is larger than <see cref="OpenUrlMaxBytes"/> or answers with an error is not opened.
/// </summary>
internal sealed partial class MainWindow
{
    internal const int OpenUrlMaxBytes = 16 << 20;

    internal CommandOutcome OpenFromInternet()
    {
        _ = OpenFromInternetAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Asks whether the editor may talk to <paramref name="host"/>, once per host; false when the user said no.</summary>
    private async Task<bool> AllowHostAsync(string host, string title, string message)
    {
        var consent = _app.Plugins.ToolNetwork;
        if (consent.IsAllowed(HttpConsentTool, host)) return true;
        var ask = new ChoiceRequest(title, message, [new DialogButton(Loc.Get(K.HttpConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        consent.Allow(HttpConsentTool, [host]);
        return true;
    }

    private async Task OpenFromInternetAsync()
    {
        string initial = SelectedOrWholeText() is { Length: > 0 and < 2048 } selected && Uri.TryCreate(selected.Trim(), UriKind.Absolute, out var guess) && guess.Scheme is "http" or "https" ? selected.Trim() : "";
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.OpenUrlTitle), Loc.Get(K.OpenUrlLabel), initial) is not { } answer) return;
        await OpenUrlAsync(answer.Trim());
    }

    /// <summary>Fetches <paramref name="address"/> and opens its text in a new tab; says in the status bar why when it does not.</summary>
    internal async Task OpenUrlAsync(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            SetStatusMessage(Loc.Get(K.OpenUrlBad, ("address", address)));
            return;
        }
        if (!await AllowHostAsync(uri.Host, Loc.Get(K.OpenUrlTitle), Loc.Get(K.OpenUrlConsent, ("host", uri.Host)))) return;
        NetworkPolicy policy;
        try { policy = _app.Remote.Policy(); }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.OpenUrlFailed, ("reason", e.Message))); return; }
        SetStatusMessage(Loc.Get(K.OpenUrlFetching, ("address", address)));
        Download download;
        try
        {
            using var client = new HttpClient(policy.CreateHttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
            download = await HttpSender.DownloadAsync(client, uri, OpenUrlMaxBytes, CancellationToken.None);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            SetStatusMessage(Loc.Get(K.OpenUrlFailed, ("reason", e.InnerException?.Message ?? e.Message)));
            return;
        }
        if (!download.Success) { SetStatusMessage(Loc.Get(K.OpenUrlStatus, ("status", download.Status), ("address", address))); return; }
        if (download.Truncated) { SetStatusMessage(Loc.Get(K.OpenUrlTooLarge, ("limit", OpenUrlMaxBytes >> 20))); return; }
        if (download.LooksBinary) { SetStatusMessage(Loc.Get(K.OpenUrlBinary, ("type", download.ContentType ?? "?"))); return; }
        string name = Path.GetFileName(download.FinalUri.AbsolutePath);
        var mode = name.Length > 0 && LanguageModes.ForPath(name) is { } byName && byName.Id != LanguageModes.PlainId ? byName.Id
            : download.ContentType switch
            {
                { } t when t.Contains("json", StringComparison.OrdinalIgnoreCase) => "json",
                { } t when t.Contains("xml", StringComparison.OrdinalIgnoreCase) => "xml",
                { } t when t.Contains("html", StringComparison.OrdinalIgnoreCase) => "html",
                { } t when t.Contains("css", StringComparison.OrdinalIgnoreCase) => "css",
                { } t when t.Contains("javascript", StringComparison.OrdinalIgnoreCase) => "javascript",
                { } t when t.Contains("markdown", StringComparison.OrdinalIgnoreCase) => "markdown",
                _ => "plaintext",
            };
        OpenGeneratedText(download.Text, mode);
        SetStatusMessage(Loc.Get(K.OpenUrlDone, ("address", download.FinalUri.ToString()), ("bytes", download.Bytes.Length)));
    }
}

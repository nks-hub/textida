using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Http;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Requests from the text of a <c>.http</c> or <c>.rest</c> file (PZ.6.3): the request at the caret is sent with the network policy of the editor, after the user has allowed the host, and the answer opens in a new tab.
/// The environment (<c>http-client.env.json</c> beside the file, with the private <c>http-client.private.env.json</c>) is chosen with a command and remembered per folder. Nothing in a response is run.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The name the consent of the hosts of requests is kept under, next to those of language servers (one file, one revoke).</summary>
    internal const string HttpConsentTool = "http-client";

    private const int HttpMaxBodyBytes = 8 << 20;

    private string HttpEnvironmentFile => System.IO.Path.Combine(_app.ProfileDirectory, "http-environments.json");

    private string? ChosenHttpEnvironment(string folder)
    {
        try { return JsonNode.Parse(System.IO.File.ReadAllText(HttpEnvironmentFile))?[folder]?.GetValue<string>(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private void RememberHttpEnvironment(string folder, string? name)
    {
        JsonObject root;
        try { root = JsonNode.Parse(System.IO.File.ReadAllText(HttpEnvironmentFile)) as JsonObject ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { root = []; }
        if (name is null) root.Remove(folder); else root[folder] = name;
        Directory.CreateDirectory(_app.ProfileDirectory);
        System.IO.File.WriteAllText(HttpEnvironmentFile, root.ToJsonString());
    }

    /// <summary>The request at the caret with its variables replaced, or null with the reason said in the status bar. <paramref name="secrets"/> gets the values that came from the private file.</summary>
    private HttpRequest? ResolveHttpRequest(out List<string> secrets)
    {
        secrets = [];
        if (ActiveEditor is not { } surface || Active is not { } tab) return null;
        var file = HttpRequestFile.Parse(surface.Document.Text);
        int line = surface.Document.GetLineByOffset(Math.Min(surface.CaretOffset, surface.Document.TextLength)).LineNumber - 1;
        if (file.At(line) is not { } request)
        {
            SetStatusMessage(Loc.Get(K.HttpNoRequest));
            return null;
        }
        string folder = tab.Path is { } path ? System.IO.Path.GetDirectoryName(path)! : "";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (folder.Length > 0)
        {
            var environments = HttpEnvironments.Load(folder);
            var (env, secret) = environments.Values(ChosenHttpEnvironment(folder));
            foreach (var (k, v) in env) values[k] = v;
            secrets = [.. secret.Select(k => env[k])];
        }
        foreach (var (k, v) in file.Variables) values[k] = v; // a variable of the file wins over the environment
        var missing = new List<string>();
        var resolved = HttpVariables.Resolve(request, n => values.GetValueOrDefault(n), missing);
        if (missing.Count > 0)
        {
            SetStatusMessage(Loc.Get(K.HttpMissingVariables, ("names", string.Join(", ", missing))));
            return null;
        }
        return resolved;
    }

    internal CommandOutcome SendHttpRequest()
    {
        if (ActiveEditor is null) return CommandOutcome.NotHandled;
        _ = SendHttpRequestAsync();
        return CommandOutcome.Handled;
    }

    private async Task SendHttpRequestAsync()
    {
        if (ResolveHttpRequest(out var secrets) is not { } request) return;
        string masked(string text) => HttpEnvironments.Mask(text, secrets);
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            SetStatusMessage(Loc.Get(K.HttpBadAddress, ("address", masked(request.Url))));
            return;
        }
        var consent = _app.Plugins.ToolNetwork;
        if (!consent.IsAllowed(HttpConsentTool, uri.Host))
        {
            var ask = new ChoiceRequest(Loc.Get(K.HttpConsentTitle), Loc.Get(K.HttpConsentMessage, ("host", uri.Host), ("method", request.Method)),
                [new DialogButton(Loc.Get(K.HttpConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
            consent.Allow(HttpConsentTool, [uri.Host]);
        }
        NetworkPolicy policy;
        try { policy = _app.Remote.Policy(); }
        catch (RemoteException e) { SetStatusMessage(Loc.Get(K.HttpFailed, ("reason", e.Message))); return; }
        SetStatusMessage(Loc.Get(K.HttpSending, ("method", request.Method), ("address", masked(request.Url))));
        try
        {
            using var client = new HttpClient(policy.CreateHttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(60) };
            var answer = await HttpSender.SendAsync(client, request, HttpMaxBodyBytes, CancellationToken.None);
            OpenGeneratedText(answer.Format(Environment.NewLine) + (answer.Truncated ? Environment.NewLine + Loc.Get(K.HttpTruncated, ("limit", HttpMaxBodyBytes >> 20)) : ""), answer.LanguageId);
            SetStatusMessage(Loc.Get(K.HttpDone, ("method", request.Method), ("address", masked(request.Url)), ("status", answer.Status), ("ms", (int)answer.Elapsed.TotalMilliseconds)));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            SetStatusMessage(Loc.Get(K.HttpFailed, ("reason", masked(e.InnerException?.Message ?? e.Message))));
        }
    }

    internal CommandOutcome ChooseHttpEnvironment()
    {
        if (ActiveEditor is null || Active?.Path is not { } path) return CommandOutcome.NotHandled;
        _ = ChooseHttpEnvironmentAsync(System.IO.Path.GetDirectoryName(path)!);
        return CommandOutcome.Handled;
    }

    private async Task ChooseHttpEnvironmentAsync(string folder)
    {
        var names = HttpEnvironments.Load(folder).Names;
        if (names.Count == 0) { SetStatusMessage(Loc.Get(K.HttpNoEnvironments, ("file", HttpEnvironments.SharedFileName))); return; }
        string? current = ChosenHttpEnvironment(folder);
        var items = new List<PickItem> { new("", Loc.Get(K.HttpEnvironmentNone), current is null ? Loc.Get(K.HttpEnvironmentCurrent) : null) };
        items.AddRange(names.Select(n => new PickItem(n, n, n == current ? Loc.Get(K.HttpEnvironmentCurrent) : null)));
        var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.HttpEnvironmentTitle), Loc.Get(K.HttpEnvironmentWatermark), items), CancellationToken.None);
        if (chosen is null) return;
        RememberHttpEnvironment(folder, chosen.Length == 0 ? null : chosen);
        SetStatusMessage(Loc.Get(K.HttpEnvironmentSet, ("name", chosen.Length == 0 ? Loc.Get(K.HttpEnvironmentNone) : chosen)));
    }

    /// <summary>Puts the request at the caret on the clipboard as a <c>curl</c> command, with the variables replaced (the private values are in it, it is what would be sent).</summary>
    internal CommandOutcome CopyHttpAsCurl()
    {
        if (ActiveEditor is null) return CommandOutcome.NotHandled;
        if (ResolveHttpRequest(out _) is not { } request) return CommandOutcome.Handled;
        _ = Clipboard?.SetTextAsync(CurlCommand.From(request));
        SetStatusMessage(Loc.Get(K.HttpCurlCopied));
        return CommandOutcome.Handled;
    }

    /// <summary>Turns the selected <c>curl</c> command (or the whole document) into a request of a <c>.http</c> file in a new tab.</summary>
    internal CommandOutcome CurlToHttp()
    {
        if (SelectedOrWholeText() is not { } text) return CommandOutcome.NotHandled;
        if (CurlCommand.Parse(text.Trim()) is not { } request)
        {
            SetStatusMessage(Loc.Get(K.HttpNotCurl));
            return CommandOutcome.Handled;
        }
        var sb = new System.Text.StringBuilder().Append(request.Method).Append(' ').Append(request.Url).Append('\n');
        foreach (var (name, value) in request.Headers) sb.Append(name).Append(": ").Append(value).Append('\n');
        if (request.Body.Length > 0) sb.Append('\n').Append(request.Body).Append('\n');
        OpenGeneratedText(sb.ToString().ReplaceLineEndings(Environment.NewLine), "plaintext");
        return CommandOutcome.Handled;
    }
}

using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Http;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Two commands that hand an address to the browser of the system (PZ.2.3, PZ.2.4): the selection searched on the web through the address template in the setting <c>web.searchUrl</c>, and the active file opened at the address that the rules in
/// <c>web.pathMap</c> give for its path. Both are the user's own rules; the editor starts no server and has no search engine of its own. A file that holds secrets is never searched.
/// </summary>
internal sealed partial class MainWindow
{
    internal const string WebSearchSetting = "web.searchUrl";
    internal const string WebPathMapSetting = "web.pathMap";

    private async Task<bool> OpenInBrowserAsync(Uri uri) => _app.UriOpener is { } open ? await open(uri) : await Launcher.LaunchUriAsync(uri);

    /// <summary>The selection, or the word at the caret when nothing is selected.</summary>
    private string? SelectionOrWord()
    {
        if (ActiveEditor is not { } surface) return null;
        var selected = surface.GetSelections().Where(s => !s.IsEmpty).Select(s => surface.Document.GetText(s.Start, s.Length)).ToList();
        if (selected.Count > 0) return string.Join(' ', selected);
        string text = surface.Document.Text;
        int at = Math.Min(surface.CaretOffset, text.Length), start = at, end = at;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return text[start..end];
    }

    internal CommandOutcome SearchSelectionOnWeb()
    {
        if (ActiveEditor is null) return CommandOutcome.NotHandled;
        _ = SearchSelectionOnWebAsync();
        return CommandOutcome.Handled;
    }

    internal async Task SearchSelectionOnWebAsync()
    {
        string template = _app.Workbench.Settings.GetString(WebSearchSetting);
        if (string.IsNullOrWhiteSpace(template)) { SetStatusMessage(Loc.Get(K.WebSearchNoTemplate, ("setting", WebSearchSetting))); return; }
        if (SecretValues.IsSecretFile(Active?.Path)) { SetStatusMessage(Loc.Get(K.WebSearchSecrets)); return; }
        string text = SelectionOrWord() ?? "";
        if (WebLinks.SearchUri(template, text) is not { } uri)
        {
            SetStatusMessage(Loc.Get(text.Trim().Length == 0 ? K.WebSearchNothing : K.WebSearchBad, ("setting", WebSearchSetting), ("max", WebLinks.MaxQueryChars)));
            return;
        }
        SetStatusMessage(await OpenInBrowserAsync(uri) ? Loc.Get(K.WebSearchOpened, ("host", uri.Host)) : Loc.Get(K.WebOpenFailed, ("address", uri.AbsoluteUri)));
    }

    internal CommandOutcome OpenFileAtMappedUrl()
    {
        if (Active is null) return CommandOutcome.NotHandled;
        _ = OpenFileAtMappedUrlAsync();
        return CommandOutcome.Handled;
    }

    internal async Task OpenFileAtMappedUrlAsync()
    {
        if (Active?.Path is not { } path) { SetStatusMessage(Loc.Get(K.WebMapUnsaved)); return; }
        var rules = WebLinks.ParseRules(_app.Workbench.Settings.GetString(WebPathMapSetting));
        if (rules.Count == 0) { SetStatusMessage(Loc.Get(K.WebMapNoRules, ("setting", WebPathMapSetting))); return; }
        if (WebLinks.MapPath(rules, path) is not { } uri) { SetStatusMessage(Loc.Get(K.WebMapNoMatch, ("path", path), ("setting", WebPathMapSetting))); return; }
        SetStatusMessage(await OpenInBrowserAsync(uri) ? Loc.Get(K.WebMapOpened, ("address", uri.AbsoluteUri)) : Loc.Get(K.WebOpenFailed, ("address", uri.AbsoluteUri)));
    }
}

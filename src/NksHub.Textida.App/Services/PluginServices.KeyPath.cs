using System.Text.Json;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.App.Services;

internal sealed partial class PluginServices
{
    /// <summary>The key under the caret as the plugin read it: the revision and the caret it belongs to, the dotted path and the bytes of the key.</summary>
    internal sealed record KeyPathAnswer(long Revision, long Caret, string Path, ByteRange Key);

    private static long? KeyPathCaret(DocTab tab) =>
        tab.JsonShown ? tab.Json?.SourceOffset : tab.Surface is { } surface ? CaretOf(surface) : null;

    private DispatcherTimer? _keyPathTimer;
    private (MainWindow Window, DocTab Tab)? _keyPathWanted;
    private bool _keyPathShown;

    private static long? CaretOf(EditorSurface surface) =>
        surface.ByteSelections() is { Count: > 0 } selections ? selections.Items[selections.Primary].Caret : null;

    /// <summary>The status bar is drawn for this tab: once the caret has rested, asks for the key path under it, when the tab is a YAML or JSON text. Any other tab clears it.</summary>
    public void TrackKeyPath(MainWindow window, DocTab? tab)
    {
        if (tab is not { Surface: not null } || KeySyntax(tab.Doc.Language.Id) is null)
        {
            _keyPathWanted = null;
            ShowKeyPath(null);
            return;
        }
        _keyPathWanted = (window, tab);
        if (_keyPathTimer is null)
        {
            _keyPathTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimits.KeyPathDebounceMilliseconds) };
            _keyPathTimer.Tick += (_, _) =>
            {
                _keyPathTimer.Stop();
                if (_keyPathWanted is { } wanted) _ = QueryKeyPathAsync(wanted.Window, wanted.Tab, background: true);
            };
        }
        _keyPathTimer.Stop();
        _keyPathTimer.Start();
    }

    private void ShowKeyPath(KeyPathAnswer? answer)
    {
        if (answer is null)
        {
            if (!_keyPathShown) return;
            _keyPathShown = false;
            Ui.SetStatus(WebDataPluginId, null);
            return;
        }
        _keyPathShown = true;
        Ui.SetStatus(WebDataPluginId, new StatusModel(answer.Path, Loc.Get(K.KeyPathTip)));
    }

    /// <summary>
    /// Asks the web data plugin for the key under the caret of a YAML or JSON tab and shows it in the status bar. The answer belongs to the revision and the caret it was asked for:
    /// when the document or the caret has moved by the time it arrives, it is dropped and the status bar keeps what is true. Null then, and when there is no key.
    /// </summary>
    internal async Task<KeyPathAnswer?> QueryKeyPathAsync(MainWindow window, DocTab tab, bool background = false)
    {
        if (tab.Surface is null || KeySyntax(tab.Doc.Language.Id) is not { } syntax || KeyPathCaret(tab) is not { } caret) return null;
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        var outcome = await ExecuteAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "keypath.at", $",\"offset\":{caret},\"syntax\":\"{syntax}\"", notify: false, background: background);
        if (!outcome.Started)
        {
            if (_keyPathWanted is not null) _keyPathTimer?.Start(); // a command was using the document: ask again when it is free
            return null;
        }
        var r = outcome.Result;
        if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("revision", out var answered) || answered.GetInt64() != revision) return null;
        if (!ReferenceEquals(tab.Doc.Doc, document) || document.Revision != revision || KeyPathCaret(tab) != caret) return null;
        KeyPathAnswer? answer = null;
        if (r.GetProperty("status").GetString() == "ok")
            answer = new KeyPathAnswer(revision, caret, r.GetProperty("path").GetString()!, new ByteRange(r.GetProperty("start").GetInt64(), r.GetProperty("end").GetInt64()));
        ShowKeyPath(ReferenceEquals(window.Active, tab) ? answer : null);
        return answer;
    }

    /// <summary>Puts the key path under the caret on the clipboard.</summary>
    public async Task<bool> CopyKeyPathAsync(MainWindow window, DocTab tab)
    {
        var answer = await QueryKeyPathAsync(window, tab);
        if (answer is null)
        {
            window.SetStatusMessage(Loc.Get(K.KeyPathNone));
            return false;
        }
        if (window.Clipboard is { } clipboard) await clipboard.SetTextAsync(answer.Path);
        window.SetStatusMessage(Loc.Get(K.KeyPathCopied, ("path", answer.Path)));
        return true;
    }

    /// <summary>Reports the keys each JSON or YAML document lacks only while both read revisions are current.</summary>
    public async Task<bool> CompareKeysAsync(MainWindow window, DocTab tab, DocTab other)
    {
        if (tab.Surface is null || other.Surface is null || ReferenceEquals(tab.Doc, other.Doc)
            || KeySyntax(tab.Doc.Language.Id) is not { } syntax || KeySyntax(other.Doc.Language.Id) is not { } otherSyntax) return false;
        var document = tab.Doc.Doc;
        var otherDocument = other.Doc.Doc;
        long revision = document.Revision, otherRevision = otherDocument.Revision;
        var outcome = await ExecuteAsync(window, tab, WebDataPluginId, WebDataActivation, "Web data", "keypath.compare",
            $",\"other\":\"{otherDocument.Id}\",\"otherRevision\":{otherRevision},\"syntax\":\"{syntax}\",\"otherSyntax\":\"{otherSyntax}\"", other);
        if (!outcome.Started) return false;
        var r = outcome.Result;
        if (r.ValueKind != JsonValueKind.Object || r.GetProperty("status").GetString() != "compared") return true;
        if (!ReferenceEquals(tab.Doc.Doc, document) || !ReferenceEquals(other.Doc.Doc, otherDocument)
            || document.Revision != revision || otherDocument.Revision != otherRevision)
        {
            window.SetStatusMessage(Loc.Get(K.KeysCompareChanged));
            return true;
        }

        string nameA = tab.Doc.Title, nameB = other.Doc.Title;
        var lines = new List<string> { Loc.Get(K.KeysCompareHeader, ("a", nameA), ("b", nameB)) };
        if (r.GetProperty("errorLine").GetInt32() is > 0 and var errorA) lines.Add(Loc.Get(K.KeysCompareSyntax, ("file", nameA), ("line", errorA)));
        if (r.GetProperty("otherErrorLine").GetInt32() is > 0 and var errorB) lines.Add(Loc.Get(K.KeysCompareSyntax, ("file", nameB), ("line", errorB)));
        void Section(string property, string file)
        {
            var keys = r.GetProperty(property).EnumerateArray().Select(k => k.GetString()!).ToList();
            lines.Add(keys.Count == 0 ? Loc.Get(K.KeysCompareNoneMissing, ("file", file)) : Loc.Plural(K.KeysCompareMissing, keys.Count, ("file", file)));
            lines.AddRange(keys.Select(k => "    " + k));
        }
        Section("missingInOther", nameB);
        Section("missingInDocument", nameA);
        _app.Output.Append(OutputChannel.Tasks, Loc.Get(K.KeysCompareSource), string.Join('\n', lines));
        window.ShowOutput(OutputChannel.Tasks);
        return true;
    }
}

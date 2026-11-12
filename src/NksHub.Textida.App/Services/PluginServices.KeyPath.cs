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

}

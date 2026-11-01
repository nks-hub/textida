using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Spelling (PZ.3.1): comments and strings of source files, Markdown and plain text are checked against a Hunspell dictionary the user chose or downloaded from LibreOffice after saying yes. The mistakes are underlined,
/// the suggestions are a list, and the completion of diacritics is a change the user reviews before it is applied. Nothing is checked until the user switches it on and a dictionary is set.
/// </summary>
internal sealed partial class MainWindow
{
    internal const string SpellEnabledSetting = "spell.enabled", SpellDictionarySetting = "spell.dictionary", SpellAutocorrectSetting = "spell.autocorrect";
    internal const int SpellMaxChars = 1_000_000;

    private readonly Dictionary<OpenDocument, CancellationTokenSource> _spellJobs = [];

    private bool SpellOn => _app.Workbench.Settings.GetBool(SpellEnabledSetting);

    /// <summary>The checker of the dictionary named by the setting; loaded again when the setting names another file. Null, with the reason in <see cref="App.SpellProblem"/>, when there is none.</summary>
    private SpellChecker? Speller()
    {
        string path = _app.Workbench.Settings.GetString(SpellDictionarySetting).Trim();
        if (path.Length == 0)
        {
            _app.Speller = null;
            _app.SpellProblem = null;
            return null;
        }
        if (_app.Speller is { } current && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)) return current;
        _app.Speller = SpellChecker.Load(path, _app.SpellFiles.PersonalWords(), out string? problem);
        _app.SpellProblem = problem;
        return _app.Speller;
    }

    private (Core.Syntax.Tokenizer? Tokenizer, bool Markdown, bool Checkable) SpellKindOf(DocTab tab)
    {
        var od = tab.Doc;
        string? grammar = LanguageModes.GrammarId(od);
        bool markdown = od.Language.Id == "markdown";
        bool plain = od.Language.Id == LanguageModes.PlainId || grammar is null;
        return (plain ? null : LanguageModes.Tokenizer(grammar!), markdown, tab.Surface is not null && !od.IsLarge && od.Language.Id != "log");
    }

    /// <summary>Underlines the mistakes of the tab, or takes the underlines away when the check is off. Runs the check on a worker; an answer for text that has changed since is dropped.</summary>
    internal async Task RefreshSpellingAsync(DocTab tab)
    {
        if (tab.Surface is not { } surface) return;
        var speller = SpellOn ? Speller() : null;
        var (tokenizer, markdown, checkable) = SpellKindOf(tab);
        if (speller is null || !checkable || surface.Document.TextLength > SpellMaxChars)
        {
            surface.SetSpellMarks([]);
            return;
        }
        string text = surface.Text;
        var found = await Task.Run(() => SpellText.Misspelled(text, tokenizer, markdown, speller.Check));
        if (surface.Document.TextLength != text.Length || surface.Text != text) return; // the text moved on; the next change schedules a new check
        surface.SetSpellMarks(found.Select(m => (m.Start, m.Start + m.Length)));
    }

    /// <summary>Checks the document again shortly after its last change.</summary>
    private void ScheduleSpelling(OpenDocument od)
    {
        if (!SpellOn) return;
        if (_spellJobs.Remove(od, out var old)) old.Cancel();
        var cts = _spellJobs[od] = new CancellationTokenSource();
        _ = Task.Delay(SpellDelay, cts.Token).ContinueWith(_ => Dispatcher.UIThread.Post(async () =>
        {
            if (_spellJobs.GetValueOrDefault(od) != cts) return;
            _spellJobs.Remove(od);
            foreach (var tab in od.Views) await RefreshSpellingAsync(tab);
        }), cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>How long after the last change the text is checked again; tests shorten it.</summary>
    internal TimeSpan SpellDelay { get; set; } = TimeSpan.FromMilliseconds(700);

    private async Task RefreshAllSpellingAsync()
    {
        foreach (var tab in Tabs.ToList()) await RefreshSpellingAsync(tab);
    }

    private void SetSpellSetting(string key, JsonNode value) => _app.Workbench.Set(key, value);

    // ---- commands ----

    internal CommandOutcome ToggleSpelling()
    {
        _ = ToggleSpellingAsync();
        return CommandOutcome.Handled;
    }

    internal async Task ToggleSpellingAsync()
    {
        bool on = !SpellOn;
        SetSpellSetting(SpellEnabledSetting, JsonValue.Create(on));
        if (!on)
        {
            SetStatusMessage(Loc.Get(K.SpellOff));
            await RefreshAllSpellingAsync();
            return;
        }
        if (Speller() is null)
        {
            SetStatusMessage(Loc.Get(K.SpellNoDictionary));
            return;
        }
        SetStatusMessage(Loc.Get(K.SpellOn, ("dictionary", Path.GetFileNameWithoutExtension(_app.Speller!.Path))));
        await RefreshAllSpellingAsync();
    }

    internal CommandOutcome ChooseSpellDictionary()
    {
        _ = ChooseSpellDictionaryAsync();
        return CommandOutcome.Handled;
    }

    internal async Task ChooseSpellDictionaryAsync()
    {
        string? chosen = (await _app.Dialogs.PickFilesAsync(this)).FirstOrDefault();
        if (chosen is null) return;
        await UseSpellDictionaryAsync(chosen);
    }

    private async Task UseSpellDictionaryAsync(string dic)
    {
        var checker = SpellChecker.Load(dic, _app.SpellFiles.PersonalWords(), out string? problem);
        if (checker is null)
        {
            SetStatusMessage(Loc.Get(K.SpellBadDictionary, ("file", Path.GetFileName(dic)), ("reason", problem ?? "")));
            return;
        }
        SetSpellSetting(SpellDictionarySetting, JsonValue.Create(Path.GetFullPath(dic)));
        SetSpellSetting(SpellEnabledSetting, JsonValue.Create(true));
        _app.Speller = checker;
        SetStatusMessage(Loc.Get(K.SpellOn, ("dictionary", Path.GetFileNameWithoutExtension(dic))));
        await RefreshAllSpellingAsync();
    }

    internal CommandOutcome DownloadSpellDictionary()
    {
        var rows = SpellFiles.Downloadable.Select(d => new QuickRow(d.Name, null, d.Uri("dic").ToString(), null, true, null, d.Name, () => _ = DownloadSpellDictionaryAsync(d))).ToList();
        ShowChoice(Loc.Get(K.SpellDownloadWatermark), Loc.Get(K.SpellDownloadA11y), rows);
        return CommandOutcome.Handled;
    }

    /// <summary>Downloads the two files of a LibreOffice dictionary after the user has seen where from and under which licences, and uses it.</summary>
    internal async Task DownloadSpellDictionaryAsync(DownloadableDictionary dictionary)
    {
        var ask = new ChoiceRequest(Loc.Get(K.SpellDownloadTitle), Loc.Get(K.SpellDownloadConsent, ("name", dictionary.Name), ("host", DownloadableDictionary.Host)),
            [new DialogButton(Loc.Get(K.SpellDownloadAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
            [dictionary.Uri("dic").ToString(), dictionary.Uri("aff").ToString(), Loc.Get(K.SpellDownloadLicence)], "spelling");
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
        try
        {
            SetStatusMessage(Loc.Get(K.SpellDownloading, ("name", dictionary.Name)));
            byte[] aff, dic;
            if (SpellDownloadOverride is { } over) (aff, dic) = (await over(dictionary.Uri("aff")), await over(dictionary.Uri("dic")));
            else
            {
                if (!await AllowHostAsync(DownloadableDictionary.Host, Loc.Get(K.SpellDownloadTitle), Loc.Get(K.SpellDownloadConsent, ("name", dictionary.Name), ("host", DownloadableDictionary.Host)))) return;
                using var client = new HttpClient(_app.Remote.Policy().CreateHttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(5) };
                aff = await FetchDictionaryFileAsync(client, dictionary.Uri("aff"));
                dic = await FetchDictionaryFileAsync(client, dictionary.Uri("dic"));
            }
            string path = _app.SpellFiles.Save(dictionary, dic, aff);
            await UseSpellDictionaryAsync(path);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or RemoteException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.SpellDownloadFailed, ("reason", e.InnerException?.Message ?? e.Message)));
        }
    }

    /// <summary>Hook of the tests: where the bytes of a dictionary file come from instead of the network.</summary>
    internal Func<Uri, Task<byte[]>>? SpellDownloadOverride { get; set; }

    private static async Task<byte[]> FetchDictionaryFileAsync(HttpClient client, Uri uri)
    {
        var download = await HttpSender.DownloadAsync(client, uri, (int)SpellFiles.MaxDictionaryBytes, CancellationToken.None);
        if (!download.Success || download.Truncated) throw new IOException(download.Truncated ? $"{uri.Segments[^1]} is larger than {SpellFiles.MaxDictionaryBytes >> 20} MiB" : $"{uri.Segments[^1]}: HTTP {download.Status}");
        return download.Bytes;
    }

    private static readonly Regex SpellWordAtCaret = new(@"\p{L}[\p{L}\p{M}'’]*", RegexOptions.Compiled);

    internal CommandOutcome SpellSuggestions()
    {
        if (Active is not { Surface: { } surface } tab) return CommandOutcome.NotHandled;
        if (Speller() is not { } speller)
        {
            SetStatusMessage(Loc.Get(K.SpellNoDictionary));
            return CommandOutcome.Handled;
        }
        string text = surface.Text;
        int caret = surface.CaretOffset;
        var word = SpellWordAtCaret.Matches(text).FirstOrDefault(m => caret >= m.Index && caret <= m.Index + m.Length);
        if (word is null)
        {
            SetStatusMessage(Loc.Get(K.SpellNoWord));
            return CommandOutcome.Handled;
        }
        string trimmed = word.Value.Trim('\'', '’');
        if (speller.Check(trimmed))
        {
            SetStatusMessage(Loc.Get(K.SpellWordOk, ("word", trimmed)));
            return CommandOutcome.Handled;
        }
        var rows = speller.Suggest(trimmed).Take(12).Select(s => new QuickRow(s, null, null, null, true, null, s, () => ReplaceSpellWord(tab, word.Index, word.Length, s))).ToList();
        rows.Add(new QuickRow(Loc.Get(K.SpellAddWord, ("word", trimmed)), null, null, null, true, null, Loc.Get(K.SpellAddWord, ("word", trimmed)), () => AddSpellWord(tab, trimmed)));
        ShowChoice(Loc.Get(K.SpellSuggestWatermark, ("word", trimmed)), Loc.Get(K.SpellSuggestA11y), rows);
        return CommandOutcome.Handled;
    }

    private void ReplaceSpellWord(DocTab tab, int start, int length, string replacement)
    {
        if (tab.Surface is not { } surface || start + length > surface.Document.TextLength) return;
        surface.Document.Replace(start, length, replacement);
        _ = RefreshSpellingAsync(tab);
    }

    private void AddSpellWord(DocTab tab, string word)
    {
        try { _app.SpellFiles.AddPersonal(word); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.SpellAddFailed, ("reason", e.Message)));
            return;
        }
        _app.Speller?.AddPersonal(word);
        SetStatusMessage(Loc.Get(K.SpellWordAdded, ("word", word)));
        _ = RefreshAllSpellingAsync();
    }

    internal CommandOutcome NextSpellingMistake()
    {
        if (Active is not { Surface: { } surface }) return CommandOutcome.NotHandled;
        var marks = surface.SpellMarks;
        if (marks.Count == 0)
        {
            SetStatusMessage(Loc.Get(SpellOn ? K.SpellNoMistakes : K.SpellOffHint));
            return CommandOutcome.Handled;
        }
        int caret = surface.CaretOffset;
        var next = marks.FirstOrDefault(m => m.Start > caret);
        if (next == default) next = marks[0];
        surface.CaretOffset = next.Start;
        surface.Select(next.Start, next.End - next.Start);
        surface.ScrollToLine(surface.Document.GetLineByOffset(next.Start).LineNumber);
        return CommandOutcome.Handled;
    }

    // ---- autocorrect ----

    /// <summary>
    /// With <c>spell.autocorrect</c> on, a word the dictionary does not know and knows with diacritics (exactly one way) is completed when the user ends it with a character that is not a letter. It is a change of its own in the
    /// history, so Undo gives the word as it was typed. Off by default; a source file is only corrected inside comments and strings.
    /// </summary>
    internal void AutoCorrectTyped(DocTab tab, string typed)
    {
        if (typed.Length != 1 || char.IsLetter(typed[0]) || !_app.Workbench.Settings.GetBool(SpellAutocorrectSetting) || !SpellOn || tab.Surface is not { } surface) return;
        if (Speller() is not { } speller) return;
        var (tokenizer, markdown, checkable) = SpellKindOf(tab);
        if (!checkable || surface.Document.TextLength > 200_000) return;
        int end = surface.CaretOffset - 1;
        if (end <= 0) return;
        string text = surface.Text;
        int start = end;
        while (start > 0 && (char.IsLetter(text[start - 1]) || char.GetUnicodeCategory(text[start - 1]) == System.Globalization.UnicodeCategory.NonSpacingMark || text[start - 1] is '\'' or '’')) start--;
        if (end - start < 3) return;
        var words = SpellText.Words(text, SpellText.Regions(text, tokenizer, markdown).Where(r => r.Start <= start && r.End >= end).Select(r => (start, end)).Take(1).ToList()).ToList();
        if (words is not [{ } word] || word.Start != start || word.Length != end - start) return;
        string plain = word.Word.Trim('\'', '’');
        if (speller.Check(plain) || SpellText.DiacriticsFor(plain, speller.Suggest(plain)) is not { } completion) return;
        surface.Document.Replace(start, end - start, completion);
        SetStatusMessage(Loc.Get(K.SpellAutocorrected, ("from", plain), ("to", completion)));
    }

    // ---- diacritics ----

    /// <summary>The words of the text (the selection, else all of it) that a dictionary knows only with diacritics, and the words they become; each word has exactly one such completion.</summary>
    internal static List<(Misspelling Word, string Completion)> DiacriticsCompletions(string text, int from, int to, Core.Syntax.Tokenizer? tokenizer, bool markdown, SpellChecker speller)
    {
        var regions = SpellText.Regions(text, tokenizer, markdown).Select(r => (Start: Math.Max(r.Start, from), End: Math.Min(r.End, to))).Where(r => r.End > r.Start).ToList();
        var result = new List<(Misspelling, string)>();
        foreach (var word in SpellText.Words(text, regions))
        {
            string plain = word.Word.Trim('\'', '’');
            if (speller.Check(plain)) continue;
            if (SpellText.DiacriticsFor(plain, speller.Suggest(plain)) is { } completion) result.Add((word, completion));
        }
        return result;
    }

    internal CommandOutcome CompleteDiacritics()
    {
        _ = CompleteDiacriticsAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Completes the diacritics of the misspelled words of the selection (all of the document without one) and shows the changes in the review; the text changes only for the ones the user applies.</summary>
    internal async Task CompleteDiacriticsAsync()
    {
        if (Active is not { Surface: { } surface } tab || tab.Doc.IsLarge) return;
        if (Speller() is not { } speller)
        {
            SetStatusMessage(Loc.Get(K.SpellNoDictionary));
            return;
        }
        var (tokenizer, markdown, checkable) = SpellKindOf(tab);
        if (!checkable || surface.Document.TextLength > SpellMaxChars) return;
        string text = surface.Text;
        int from = surface.SelectionLength > 0 ? surface.SelectionStart : 0, to = surface.SelectionLength > 0 ? surface.SelectionStart + surface.SelectionLength : text.Length;
        SetStatusMessage(Loc.Get(K.SpellDiacriticsRunning));
        var found = await Task.Run(() => DiacriticsCompletions(text, from, to, tokenizer, markdown, speller));
        if (found.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.SpellDiacriticsNone));
            return;
        }
        var sb = new StringBuilder(text);
        foreach (var (word, completion) in found.OrderByDescending(f => f.Word.Start))
        {
            string original = word.Word;
            string keep = original.Length > 0 && (original[0] == '\'' || original[0] == '’') ? original[..1] : "";
            sb.Remove(word.Start, word.Length).Insert(word.Start, keep + completion);
        }
        var doc = tab.Doc.Doc;
        byte[] bytes;
        try { bytes = doc.Codec.Encode(sb.ToString()); }
        catch (UnrepresentableCharactersException)
        {
            SetStatusMessage(Loc.Get(K.SpellDiacriticsEncoding, ("encoding", doc.Codec.Name)));
            return;
        }
        if (doc.Codec.Bom.Length > 0 && !bytes.AsSpan().StartsWith(doc.Codec.Bom)) bytes = [.. doc.Codec.Bom, .. bytes];
        if (ExternalReview.BuildRevert(doc, bytes) is not { } change)
        {
            SetStatusMessage(Loc.Get(K.SpellDiacriticsNone));
            return;
        }
        var decision = await ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.SpellDiacriticsTitle, ("number", found.Count)), "spelling:diacritics", tab, change, CancellationToken.None);
        await RefreshSpellingAsync(tab);
        _ = decision;
    }
}

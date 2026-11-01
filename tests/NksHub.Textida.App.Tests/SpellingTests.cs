using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>The dictionary of these tests: written here, a few Czech and English words and the MAP lines that tell Hunspell which letters are the same letter with a diacritic. It is a real Hunspell dictionary read by the real engine; no dictionary of anybody is in the repository.</summary>
internal static class TinyDictionary
{
    private static readonly string[] Maps = ["aá", "cč", "dď", "eéě", "iíí", "nň", "oó", "rř", "sš", "tť", "uúů", "yý", "zž"];

    private static readonly string[] Words = ["příliš", "žluťoučký", "kůň", "úpěl", "ďábelské", "ódy", "hello", "world", "comment", "string", "text", "the", "quick", "brown", "fox", "color", "šest", "šel", "šla", "sel", "už", "úz"];

    public static string Write(string folder, string name = "tiny")
    {
        Directory.CreateDirectory(folder);
        string aff = Path.Combine(folder, name + ".aff"), dic = Path.Combine(folder, name + ".dic");
        File.WriteAllText(aff, "SET UTF-8\nTRY esianrtolcdugmphbyfvkwzESIANRTOLCDUGMPHBYFVKWZ'\nMAP " + Maps.Length + "\n" + string.Join("\n", Maps.Select(m => "MAP " + m)) + "\n", new UTF8Encoding(false));
        File.WriteAllText(dic, Words.Length + "\n" + string.Join("\n", Words) + "\n", new UTF8Encoding(false));
        return dic;
    }
}

/// <summary>PZ.3.1: what is checked, which words are words, the diacritics, and the engine (Hunspell through WeCantSpell) over a real dictionary file.</summary>
public class SpellTextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spell-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static Tokenizer Tokenizer(string grammar) => LanguageModes.Tokenizer(grammar);

    private static List<string> Checked(string text, Tokenizer? tokenizer, bool markdown = false) =>
        [.. SpellText.Words(text, SpellText.Regions(text, tokenizer, markdown)).Select(w => w.Word)];

    [Fact]
    public void Plain_text_is_all_checked_and_a_source_file_only_in_its_comments_and_strings()
    {
        Assert.Equal(["Hello", "worldd"], Checked("Hello worldd", null));

        const string code = "let hellp = \"strng text\"; // commment here\nfunction doThing() { return 1; }\n";
        var words = Checked(code, Tokenizer("javascript"));
        Assert.Equal(["strng", "text", "commment", "here"], words); // the names (hellp, function, doThing) are not language
    }

    [Fact]
    public void Markdown_is_checked_except_its_code_and_links()
    {
        const string text = "# Titel here\n\nSome texxt with `codee` and [a lnk](http://example.test/pathh) and\n\n```js\nlet notaword = 1;\n```\n";
        var words = Checked(text, Tokenizer("markdown"), markdown: true);
        Assert.Contains("texxt", words);
        Assert.Contains("Titel", words);
        Assert.DoesNotContain("codee", words);
        Assert.DoesNotContain("notaword", words);
        Assert.DoesNotContain("pathh", words);
    }

    [Theory]
    [InlineData("ab is a UI", new string[0])]
    [InlineData("camelCase iPhone HTTPServer URL", new string[0])]
    [InlineData("file_name path/to/thing user@example.test value123 12abc", new string[0])]
    [InlineData("see https://example.test/page now", new[] { "see", "now" })]
    [InlineData("příliš žluťoučký kůň", new[] { "příliš", "žluťoučký", "kůň" })]
    [InlineData("don't stop", new[] { "don't", "stop" })]
    public void Only_ordinary_words_are_given_to_the_dictionary(string text, string[] expected) => Assert.Equal(expected, Checked(text, null));

    [Theory]
    [InlineData("příliš", "prilis")]
    [InlineData("Łódź", "Lodz")]
    [InlineData("Ångström", "Angstrom")]
    [InlineData("plain", "plain")]
    public void Diacritics_are_stripped(string word, string plain) => Assert.Equal(plain, SpellText.Strip(word));

    [Fact]
    public void A_completion_must_be_the_one_form_that_differs_in_diacritics_only_and_keeps_the_capitals()
    {
        Assert.Equal("příliš", SpellText.DiacriticsFor("prilis", ["příliš", "prilis x", "plis"]));
        Assert.Equal("Příliš", SpellText.DiacriticsFor("Prilis", ["příliš"]));
        Assert.Equal("PŘÍLIŠ", SpellText.DiacriticsFor("PRILIS", ["příliš"]));
        Assert.Null(SpellText.DiacriticsFor("sel", ["šel", "šla"]) is { } x && x == "šla" ? "wrong" : null); // šla differs in more than diacritics, so only šel remains
        Assert.Equal("šel", SpellText.DiacriticsFor("sel", ["šel", "šla"]));
        Assert.Null(SpellText.DiacriticsFor("sest", ["šest", "sešt"] ));
        Assert.Null(SpellText.DiacriticsFor("prilis", ["prilis"])); // the same word is no completion
        Assert.Null(SpellText.DiacriticsFor("prilis", []));
    }

    [Fact]
    public void A_real_dictionary_checks_suggests_and_knows_the_words_of_the_user()
    {
        string dic = TinyDictionary.Write(_dir);
        var speller = SpellChecker.Load(dic, ["Kubík"], out string? problem)!;
        Assert.Null(problem);
        Assert.True(speller.Check("hello"));
        Assert.True(speller.Check("Hello"));
        Assert.True(speller.Check("příliš"));
        Assert.False(speller.Check("helo"));
        Assert.Contains("hello", speller.Suggest("helo"));
        Assert.True(speller.Check("kubík")); // a word of the user, whatever the case
        speller.AddPersonal("Zdeněk");
        Assert.True(speller.Check("zdeněk"));

        Assert.Contains("příliš", speller.Suggest("prilis"));
        Assert.Contains("kůň", speller.Suggest("kun"));
    }

    [Fact]
    public void A_dictionary_without_its_affix_file_or_a_missing_one_is_refused_with_a_reason()
    {
        string dic = TinyDictionary.Write(_dir);
        Assert.Null(SpellChecker.Load(Path.Combine(_dir, "none.dic"), [], out string? missing));
        Assert.Contains("does not exist", missing);
        File.Delete(Path.ChangeExtension(dic, ".aff"));
        Assert.Null(SpellChecker.Load(dic, [], out string? noAffix));
        Assert.Contains(".aff", noAffix);
    }

    [Fact]
    public void The_diacritics_of_a_text_are_completed_only_inside_the_regions_and_only_when_one_form_is_certain()
    {
        var speller = SpellChecker.Load(TinyDictionary.Write(_dir), [], out _)!;
        const string text = "var prilis = 1; // prilis zlutoucky kun\nfunction kun() {}";
        var found = MainWindow.DiacriticsCompletions(text, 0, text.Length, Tokenizer("javascript"), markdown: false, speller);
        Assert.Equal(["příliš", "žluťoučký", "kůň"], found.Select(f => f.Completion));
        Assert.All(found, f => Assert.True(f.Word.Start > text.IndexOf("//", StringComparison.Ordinal)));
        var selection = MainWindow.DiacriticsCompletions("prilis kun", 0, 6, null, false, speller);
        Assert.Equal("příliš", Assert.Single(selection).Completion);
    }

    [Fact]
    public void The_downloadable_dictionaries_name_the_two_files_in_the_repository_of_libreoffice()
    {
        var czech = SpellFiles.Downloadable.Single(d => d.Code == "cs_CZ");
        Assert.Equal("https://raw.githubusercontent.com/LibreOffice/dictionaries/master/cs_CZ/cs_CZ.dic", czech.Uri("dic").ToString());
        Assert.Equal("https://raw.githubusercontent.com/LibreOffice/dictionaries/master/de/de_DE_frami.aff", SpellFiles.Downloadable.Single(d => d.Code == "de_DE").Uri("aff").ToString());
        Assert.All(SpellFiles.Downloadable, d => Assert.Equal(DownloadableDictionary.Host, d.Uri("dic").Host));
    }

    [Fact]
    public async Task With_the_network_tests_on_every_downloadable_file_exists_and_a_real_czech_dictionary_completes_diacritics()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_NETWORK_TESTS") != "1") return;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        foreach (var d in SpellFiles.Downloadable)
            foreach (string extension in new[] { "aff", "dic" })
            {
                using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, d.Uri(extension)));
                Assert.True(response.IsSuccessStatusCode, d.Uri(extension).ToString());
            }
        var czech = SpellFiles.Downloadable.Single(d => d.Code == "cs_CZ");
        var files = new SpellFiles(_dir);
        string path = files.Save(czech, await client.GetByteArrayAsync(czech.Uri("dic")), await client.GetByteArrayAsync(czech.Uri("aff")));
        var speller = SpellChecker.Load(path, [], out string? problem)!;
        Assert.Null(problem);
        Assert.True(speller.Check("příliš"));
        Assert.Equal("příliš", SpellText.DiacriticsFor("prilis", speller.Suggest("prilis")));
        Assert.Equal("žluťoučký", SpellText.DiacriticsFor("zlutoucky", speller.Suggest("zlutoucky")));
    }
}

[Collection("LocState")]
public class SpellingAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public SpellingAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.SpellDelay = TimeSpan.FromMilliseconds(30);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string Dictionary() => TinyDictionary.Write(Path.Combine(_temp.Path, "dictionaries-of-the-user"));

    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(15);
        }
        Assert.True(condition(), what);
    }

    private async Task<DocTab> SpellOnAsync(string name, string text)
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _dialogs.Picked.Clear(); _dialogs.Picked.Add(Dictionary());
        await _w.ChooseSpellDictionaryAsync();
        await Until(() => tab.Surface!.SpellMarks.Count > 0 || text.Length == 0, "the underlines");
        return tab;
    }

    [AvaloniaFact]
    public async Task Nothing_is_checked_until_a_dictionary_is_chosen_and_the_check_is_on()
    {
        var tab = _w.OpenFile(_temp.File("a.txt", "helo world\n"), null);
        await _w.ToggleSpellingAsync();
        Assert.Equal(Loc.Get(K.SpellNoDictionary), _w.StatusMessageText);
        Assert.Empty(tab.Surface!.SpellMarks);

        _dialogs.Picked.Clear(); _dialogs.Picked.Add(Path.Combine(_temp.Path, "none.dic"));
        await _w.ChooseSpellDictionaryAsync();
        Assert.Contains("none.dic", _w.StatusMessageText);
        Assert.Empty(tab.Surface.SpellMarks);
    }

    [AvaloniaFact]
    public async Task A_chosen_dictionary_underlines_the_mistakes_of_plain_text_and_follows_the_edits()
    {
        var tab = await SpellOnAsync("a.txt", "hello helo world wrld\n");
        var text = tab.Surface!.Text;
        Assert.Equal(["helo", "wrld"], tab.Surface.SpellMarks.Select(m => text[m.Start..m.End]));
        Assert.Equal(Loc.Get(K.SpellOn, ("dictionary", "tiny")), _w.StatusMessageText);

        tab.Editor!.Document.Replace(text.IndexOf("helo", StringComparison.Ordinal), 4, "hello");
        await Until(() => tab.Surface.SpellMarks.Count == 1, "the underline of the fixed word to go");
        Assert.Equal("wrld", tab.Surface.Text[tab.Surface.SpellMarks[0].Start..tab.Surface.SpellMarks[0].End]);

        await _w.ToggleSpellingAsync();
        Assert.Empty(tab.Surface.SpellMarks);
        Assert.Equal(Loc.Get(K.SpellOff), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task In_a_source_file_only_comments_and_strings_are_underlined()
    {
        var tab = await SpellOnAsync("a.js", "let helo = \"wrld hello\"; // quikc fox\n");
        var text = tab.Surface!.Text;
        Assert.Equal(["wrld", "quikc"], tab.Surface.SpellMarks.Select(m => text[m.Start..m.End]));
    }

    [AvaloniaFact]
    public async Task The_suggestions_replace_the_word_and_a_new_word_is_kept_in_the_words_of_the_user()
    {
        var tab = await SpellOnAsync("a.txt", "hello helo world\n");
        tab.Editor!.CaretOffset = 8;
        _w.SpellSuggestions();
        Assert.True(_w.IsPaletteOpen);
        var rows = _w.QuickResults;
        Assert.Contains(rows, r => r.Title == "hello");
        rows.First(r => r.Title == "hello").Run();
        Assert.Equal("hello hello world\n", tab.Editor.Document.Text);

        tab.Editor.Document.Replace(0, 5, "kubik");
        tab.Editor.CaretOffset = 2;
        await Until(() => tab.Surface!.SpellMarks.Count == 1, "the underline");
        _w.SpellSuggestions();
        _w.QuickResults.First(r => r.Title == Loc.Get(K.SpellAddWord, ("word", "kubik"))).Run();
        await Until(() => tab.Surface!.SpellMarks.Count == 0, "the underline to go");
        Assert.Contains("kubik", File.ReadAllLines(_app.SpellFiles.PersonalPath));
    }

    [AvaloniaFact]
    public async Task The_next_mistake_selects_the_next_underlined_word_and_wraps_round()
    {
        var tab = await SpellOnAsync("a.txt", "helo hello wrld\n");
        tab.Editor!.CaretOffset = 0;
        _w.NextSpellingMistake();
        Assert.Equal("wrld", tab.Editor.SelectedText);
        _w.NextSpellingMistake();
        Assert.Equal("helo", tab.Editor.SelectedText);
    }

    [AvaloniaFact]
    public async Task Completing_diacritics_shows_the_changes_and_writes_only_what_the_user_applies()
    {
        var tab = await SpellOnAsync("a.txt", "prilis zlutoucky kun a uz\n");
        var run = _w.CompleteDiacriticsAsync();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        Assert.Equal("prilis zlutoucky kun a uz\n", tab.Editor!.Document.Text); // nothing is written while the user looks
        _w.ApplyPlan();
        await run;
        Assert.Equal("příliš žluťoučký kůň a uz\n", tab.Editor.Document.Text); // "uz" has two forms (už, úz): left alone

        _w.SelectTab(tab);
        _w.Undo();
        Assert.Equal("prilis zlutoucky kun a uz\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task A_rejected_completion_changes_nothing_and_a_text_without_candidates_says_so()
    {
        var tab = await SpellOnAsync("a.txt", "prilis\n");
        var run = _w.CompleteDiacriticsAsync();
        await Until(() => _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true, "the review");
        foreach (var row in _w.ResultsView!.Rows.OfType<SearchResultsView.HunkRow>()) row.Selected = false;
        _w.ApplyPlan();
        await run;
        Assert.Equal("prilis\n", tab.Editor!.Document.Text);

        tab.Editor.Document.Text = "hello world\n";
        await _w.CompleteDiacriticsAsync();
        Assert.False(_w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() == true); // nothing to review
        Assert.Equal("hello world\n", tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Autocorrect_is_off_by_default_and_when_on_completes_the_word_that_was_just_ended_as_one_undo_step()
    {
        var tab = await SpellOnAsync("a.txt", "");
        tab.Editor!.TextArea.PerformTextInput("prilis ");
        Assert.Equal("prilis ", tab.Editor.Document.Text);

        _app.Workbench.Set("spell.autocorrect", true);
        tab.Editor.Document.Text = "";
        tab.Editor.TextArea.PerformTextInput("prilis");
        tab.Editor.TextArea.PerformTextInput(" ");
        Assert.Equal("příliš ", tab.Editor.Document.Text);

        tab.Editor.TextArea.PerformTextInput("kunx");
        tab.Editor.TextArea.PerformTextInput(" ");
        Assert.Equal("příliš kunx ", tab.Editor.Document.Text); // no certain form: left as typed
    }

    [AvaloniaFact]
    public async Task A_dictionary_is_downloaded_only_after_the_user_agrees_and_is_then_in_use()
    {
        var czech = SpellFiles.Downloadable.Single(d => d.Code == "cs_CZ");
        var asked = new List<Uri>();
        _w.SpellDownloadOverride = uri =>
        {
            asked.Add(uri);
            return Task.FromResult(File.ReadAllBytes(Path.ChangeExtension(Dictionary(), uri.AbsolutePath.EndsWith(".aff") ? "aff" : "dic")));
        };
        _dialogs.Answers.Enqueue(1); // no
        await _w.DownloadSpellDictionaryAsync(czech);
        Assert.Empty(asked);
        Assert.False(Directory.Exists(_app.SpellFiles.DictionaryFolder));

        _dialogs.Answers.Enqueue(0);
        await _w.DownloadSpellDictionaryAsync(czech);
        Assert.Equal(["cs_CZ.aff", "cs_CZ.dic"], asked.Select(u => u.Segments[^1]).Order());
        Assert.True(File.Exists(Path.Combine(_app.SpellFiles.DictionaryFolder, "cs_CZ.dic")));
        Assert.Equal(Path.Combine(_app.SpellFiles.DictionaryFolder, "cs_CZ.dic"), _app.Speller!.Path);
        Assert.Contains("raw.githubusercontent.com", _dialogs.Asked[^1].Message);
    }
}

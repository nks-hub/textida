using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// Gates of P9.2.2 over every text the UI shows in cs, en, sk and de: translated and not copied from English, no text of another language, the same placeholders.
/// The catalogues are the app texts, the texts of commands, menus and settings (Workbench) and the texts of the bundled plugins.
/// A native speaker has not read the Slovak and German texts; these gates find what a program can find.
/// </summary>
public class TranslationGateTests
{
    // Keys that may read like the English text, and in which languages: product and format names, notation, terms that are the same word in the language.
    // A text with no word of its own (only placeholders, symbols and one-letter units) needs no entry.
    private static readonly Dictionary<string, string> AppSameAsEnglish = new()
    {
        ["app.name"] = "cs sk de",
        ["encoding.group.Unicode"] = "cs sk de", // the name of a standard
        ["encoding.group.Dos"] = "cs sk de", // the name of an operating system
        ["a11y.minimap"] = "de",
        ["dialog.about.version"] = "de",
        ["dialog.about.build"] = "de",
        ["dialog.ok"] = "cs sk de",
        ["restore.details"] = "de",
        ["source.editorConfig"] = "cs sk de",
        ["source.plugin"] = "cs sk",
        ["find.case"] = "cs sk de",
        ["find.word"] = "cs sk de",
        ["find.hexMode"] = "cs sk de",
        ["hex.inspector.little"] = "cs sk de",
        ["hex.inspector.big"] = "cs sk de",
        ["hex.goto.label"] = "cs sk de",
        ["hex.bm.name"] = "de",
        ["hex.format.python"] = "cs sk",
        ["hex.format.javascript"] = "cs sk de",
        ["hex.format.base64"] = "cs sk de",
        ["hex.format.intelHex"] = "cs sk de",
        ["hex.format.srec"] = "cs sk",
        ["hex.format.hexdump"] = "cs sk de",
        ["hex.pasteAs.base64"] = "cs sk de",
        ["hex.op.xor"] = "cs sk de",
        ["hex.op.and"] = "cs sk de",
        ["hex.op.or"] = "cs sk de",
        ["hex.op.not"] = "cs sk de",
        ["hex.sum.crcMenu"] = "cs sk de",
        ["plugin.log.source"] = "de",
        ["lines.insert.mode.text"] = "cs sk de",
        ["lines.insert.text"] = "cs sk de",
        ["a11y.clips"] = "de",
        ["clips.form.title"] = "de",
        ["palette.category.symbol"] = "cs sk de",
        ["tool.out.directory"] = "de",
        ["tidy.output.source"] = "cs sk de",
        ["table.copyAs.sql"] = "cs sk de",
        ["table.copyAs.json"] = "cs sk de",
        ["panel.output.remote"] = "de",
        ["remote.sshRsa.title"] = "cs sk de", // the name of an SSH signature algorithm
        ["compare.side.revisionBare"] = "de",
        ["source.compare.remote"] = "de",
        ["ide.status.connected"] = "cs sk de",
        ["ide.agent.unnamed"] = "cs sk",
        ["ide.proposal"] = "cs sk de",
        ["mcp.agents.outputSource"] = "cs sk de",
        ["mcp.client.toolRow"] = "de",
        ["mcp.client.argsSchema"] = "de",
        ["agent.title"] = "cs sk de",
        ["tool.edit.id"] = "cs sk de",
        ["tool.edit.program"] = "cs sk",
        ["make.title"] = "cs sk de",
        ["a11y.plugins.manager"] = "de",
        ["update.title"] = "de",
        ["viewmode.text"] = "cs sk de",
        ["viewmode.hex"] = "cs sk de",
        ["commit.title"] = "cs sk de", // Commit is the word of Git in each of them
        ["commit.commit"] = "cs sk de",
        ["commit.branch"] = "de",
        ["commit.side.head"] = "cs sk de",
        ["commit.side.index"] = "cs sk de",
        ["settings.value.language.cs"] = "cs sk de", // each language is named in itself
        ["settings.value.language.en"] = "cs sk de",
        ["settings.value.language.sk"] = "cs sk de",
        ["settings.value.language.de"] = "cs sk de",
    };

    private static readonly Dictionary<string, string> WorkbenchSameAsEnglish = new()
    {
        ["cat.editor"] = "cs sk de",
        ["setting.editor.minimap.title"] = "de",
        ["cat.format"] = "de",
        ["menu.format"] = "de",
        ["menu.html"] = "cs sk de",
        ["cat.log"] = "cs sk de",
        ["submenu.markdown"] = "cs sk de",
        ["submenu.log"] = "cs sk de",
        ["submenu.ai.mcp"] = "cs sk de",
        ["submenu.ai.agent"] = "cs sk de",
        ["submenu.git"] = "cs sk de",
        ["submenu.hex"] = "cs sk de",
        ["cmd.settings.language.cs"] = "cs sk de", // each language is named in itself
        ["cmd.settings.language.en"] = "cs sk de",
        ["cmd.settings.language.sk"] = "cs sk de",
        ["cmd.settings.language.de"] = "cs sk de",
        ["cat.hex"] = "cs sk de",
        ["cmd.git.commit"] = "de", // the names of the Git actions that German keeps
        ["cmd.git.fetch"] = "de",
        ["cmd.git.pull"] = "de",
        ["cmd.git.push"] = "de",
        ["cat.git"] = "cs sk de",
        ["cat.plugins"] = "de",
        ["cat.system"] = "de",
    };

    private static readonly Dictionary<string, string> PluginSameAsEnglish = new()
    {
        ["Git:indexTitle"] = "cs sk",
        ["Git:headTitle"] = "cs sk de",
        ["Git:pullModeRebase"] = "de", // a name of Git that German keeps
        ["TextTools:hash.task"] = "cs sk de",
        ["TextTools:hash.pickTitle"] = "cs sk de",
    };

    /// <summary>The Mark of the Web is the name of a Windows feature; every language keeps it.</summary>
    private static readonly HashSet<string> EnglishWordsAllowed = ["plugins.consent.downloaded"];

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}");
    private static readonly Regex Word = new(@"\p{L}{2,}");
    private static readonly Regex EnglishWord = new(@"\b(the|and|with|cannot|could|this|that|your|from|which|are|have)\b");
    private static readonly Regex NotSlovak = new("[ěřů]");
    private static readonly Regex NotCzech = new("[ľĺŕôä]");
    private static readonly Regex Slavic = new("[ěřůľĺŕôščťžýčňď]", RegexOptions.IgnoreCase);

    private static readonly string[] Others = ["cs", "sk", "de"];

    public static IEnumerable<object[]> OtherLanguages() => Others.Select(l => new object[] { l });

    private static bool HasNoWords(string text) => !Word.IsMatch(Placeholder.Replace(text, ""));

    private static bool Allowed(Dictionary<string, string> list, string key, string lang) => list.TryGetValue(key, out var langs) && langs.Split(' ').Contains(lang);

    internal static Dictionary<string, string> WorkbenchTexts(string lang)
    {
        using var stream = typeof(StringTable).Assembly.GetManifestResourceStream($"strings.{lang}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static IReadOnlyDictionary<string, string> AppTexts(string lang) => Loc.Catalog(lang);

    private static string Unescape(string literal) => literal.Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static readonly Regex PluginRow = new(@"^\s*\[""((?:[^""\\]|\\.)*)""\]\s*=\s*\[(.*)\],?\s*$", RegexOptions.Multiline);
    private static readonly Regex PluginLiteral = new(@"""((?:[^""\\]|\\.)*)""");

    /// <summary>Every row of the Texts.cs tables of the bundled plugins: columns cs, en, sk, de. A row the reader cannot parse fails the test, so none is skipped.</summary>
    internal static List<(string Plugin, string Key, string[] Columns)> PluginRows()
    {
        var rows = new List<(string, string, string[])>();
        foreach (var dir in Directory.EnumerateDirectories(Path.Combine(Repo.Root, "src"), "NksHub.Textida.Plugins.*"))
        {
            var file = Path.Combine(dir, "Texts.cs");
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            var plugin = Path.GetFileName(dir)["NksHub.Textida.Plugins.".Length..];
            int declared = Regex.Matches(text, @"^\s*\[""", RegexOptions.Multiline).Count;
            var parsed = PluginRow.Matches(text).Select(m => (plugin, m.Groups[1].Value, PluginLiteral.Matches(m.Groups[2].Value).Select(l => Unescape(l.Groups[1].Value)).ToArray())).ToList();
            Assert.True(parsed.Count == declared, $"{plugin}: read {parsed.Count} of {declared} text rows");
            rows.AddRange(parsed);
        }
        return rows;
    }

    /// <summary>All texts of all catalogues as (catalogue, language, key, text); the plugin tables only with their placeholder-free rows' keys as <c>Plugin:key</c>.</summary>
    private static IEnumerable<(string Catalogue, string Lang, string Key, string Text)> AllTexts()
    {
        foreach (var lang in Loc.Languages)
        {
            foreach (var (key, text) in AppTexts(lang)) yield return ("app", lang, key, text);
            foreach (var (key, text) in WorkbenchTexts(lang)) yield return ("workbench", lang, key, text);
        }
        foreach (var (plugin, key, columns) in PluginRows())
        {
            if (columns.Length != 4) continue;
            for (int i = 0; i < 4; i++) yield return ("plugin", new[] { "cs", "en", "sk", "de" }[i], $"{plugin}:{key}", columns[i]);
        }
    }

    [Theory, MemberData(nameof(OtherLanguages))]
    public void Every_text_is_translated_or_listed_as_neutral(string lang)
    {
        var problems = new List<string>();

        void Check(string catalogue, IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> own, Dictionary<string, string> neutral)
        {
            foreach (var (key, en) in english)
                if (own.TryGetValue(key, out var text) && text == en && !HasNoWords(en) && !Allowed(neutral, key, lang))
                    problems.Add($"{catalogue}:{key} = {en}");
        }

        Check("app", AppTexts("en"), AppTexts(lang), AppSameAsEnglish);
        Check("workbench", WorkbenchTexts("en"), WorkbenchTexts(lang), WorkbenchSameAsEnglish);
        Assert.True(problems.Count == 0, $"{lang} is left in English (translate it, or list the key as neutral):\n" + string.Join("\n", problems));
    }

    [Fact]
    public void The_neutral_lists_name_real_keys_and_every_entry_is_still_needed()
    {
        var stale = new List<string>();

        void Check(string catalogue, IReadOnlyDictionary<string, string> english, Func<string, IReadOnlyDictionary<string, string>> texts, Dictionary<string, string> neutral)
        {
            foreach (var (key, langs) in neutral)
            {
                if (!english.TryGetValue(key, out var en)) { stale.Add($"{catalogue}:{key} is not a key"); continue; }
                foreach (var lang in langs.Split(' '))
                    if (!texts(lang).TryGetValue(key, out var text) || text != en) stale.Add($"{catalogue}:{key} is translated in {lang}; remove {lang} from the list");
            }
        }

        Check("app", AppTexts("en"), AppTexts, AppSameAsEnglish);
        Check("workbench", WorkbenchTexts("en"), WorkbenchTexts, WorkbenchSameAsEnglish);
        Assert.True(stale.Count == 0, string.Join("\n", stale));
    }

    [Fact]
    public void Plugin_texts_have_four_columns_the_same_placeholders_and_are_translated()
    {
        var rows = PluginRows();
        Assert.True(rows.Count >= 150, $"only {rows.Count} plugin texts were read");
        var problems = new List<string>();
        foreach (var (plugin, key, c) in rows)
        {
            if (c.Length != 4 || c.Any(string.IsNullOrWhiteSpace)) { problems.Add($"{plugin}:{key} needs four non-empty columns (cs, en, sk, de)"); continue; }
            var en = c[1];
            foreach (var (lang, text) in new[] { ("cs", c[0]), ("sk", c[2]), ("de", c[3]) })
            {
                if (!Loc.Placeholders(en).SetEquals(Loc.Placeholders(text))) problems.Add($"{plugin}:{key} {lang}: placeholders differ");
                if (text == en && !HasNoWords(en) && !Allowed(PluginSameAsEnglish, $"{plugin}:{key}", lang)) problems.Add($"{plugin}:{key} {lang} is left in English: {en}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        var rowKeys = rows.ToDictionary(r => $"{r.Plugin}:{r.Key}", r => r.Columns);
        var stale = PluginSameAsEnglish.Where(e => !rowKeys.TryGetValue(e.Key, out var cols) || e.Value.Split(' ').Any(l => cols[Array.IndexOf(new[] { "cs", "en", "sk", "de" }, l)] != cols[1])).Select(e => e.Key).ToList();
        Assert.True(stale.Count == 0, "neutral plugin texts that are translated or missing: " + string.Join(", ", stale));
    }

    [Fact]
    public void No_text_contains_letters_of_another_language()
    {
        var problems = new List<string>();
        foreach (var (catalogue, lang, key, text) in AllTexts())
        {
            var plain = Placeholder.Replace(text, "");
            if (lang == "sk" && NotSlovak.IsMatch(plain)) problems.Add($"{catalogue}:{key} (sk) has Czech letters: {text}");
            if (lang == "cs" && NotCzech.IsMatch(plain)) problems.Add($"{catalogue}:{key} (cs) has Slovak letters: {text}");
            if (lang is "de" or "en" && Slavic.IsMatch(plain) && !key.StartsWith("cmd.settings.language.", StringComparison.Ordinal) && !key.StartsWith("settings.value.language.", StringComparison.Ordinal))
                problems.Add($"{catalogue}:{key} ({lang}) has Czech or Slovak letters: {text}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void No_translation_contains_english_words()
    {
        var problems = AllTexts()
            .Where(t => t.Lang != "en" && !EnglishWordsAllowed.Contains(t.Key) && EnglishWord.IsMatch(Placeholder.Replace(t.Text, "")))
            .Select(t => $"{t.Catalogue}:{t.Key} ({t.Lang}): {t.Text}").ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Slovak_is_a_translation_not_a_copy_of_Czech_and_German_is_not_Czech()
    {
        CheckCopies("app", AppTexts);
        CheckCopies("workbench", l => WorkbenchTexts(l));
    }

    private static void CheckCopies(string name, Func<string, IReadOnlyDictionary<string, string>> texts)
    {
        IReadOnlyDictionary<string, string> en = texts("en"), cs = texts("cs"), sk = texts("sk"), de = texts("de");
        var translated = en.Keys.Where(k => cs.TryGetValue(k, out var czech) && czech != en[k] && !HasNoWords(en[k])).ToList();
        Assert.True(translated.Count > 100, $"{name}: only {translated.Count} translated texts");
        double slovakCopies = translated.Count(k => sk.TryGetValue(k, out var s) && s == cs[k]) / (double)translated.Count;
        double germanCopies = translated.Count(k => de.TryGetValue(k, out var d) && d == cs[k]) / (double)translated.Count;
        // Czech and Slovak share many short words; a copy of the whole catalogue would be near 100 %
        Assert.True(slovakCopies < 0.15, $"{name}: {slovakCopies:P0} of the Slovak texts are the Czech text");
        Assert.True(germanCopies < 0.02, $"{name}: {germanCopies:P0} of the German texts are the Czech text");
    }
}

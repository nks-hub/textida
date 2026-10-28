using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.2.2: the inventory of the UI texts per language with the review of each area, and the gates that need no window. <c>TranslationReview.json</c> sorts every
/// key of the three sources of texts (the application, the Workbench with its menus, commands and settings, and the bundled plugins) into an area of the
/// interface and records for each language and area who read the texts. A reviewer of OPEN, or none, means that nobody did: the Slovak and German texts
/// were written and checked by program only, and the other two languages have had no second reader either. These tests keep that record complete; they
/// cannot tell whether a name in it is true, so a name is entered by the person who did the review and by nobody else.
/// </summary>
public class TranslationReviewTests
{
    private sealed record Rule(string Catalogue, string[] Prefixes);

    private sealed record Area(string Id, string Title, Rule[] Rules);

    private sealed record Review(string Language, string Area, string Reviewer, string ReviewedOn)
    {
        public bool IsOpen => Reviewer.Trim().Length == 0 || Reviewer == "OPEN";
    }

    private sealed record Register(int SchemaVersion, Area[] Areas, Review[] Reviews);

    internal sealed record Row(string Language, string Catalogue, string Key, string Area, string Reviewer, string ReviewedOn, string Text);

    private static readonly string[] Catalogues = ["app", "workbench", "plugin"];

    private static readonly string[] PluginColumns = ["cs", "en", "sk", "de"];

    private static Register LoadRegister() => JsonSerializer.Deserialize<Register>(
        File.ReadAllText(Path.Combine(Repo.Root, "tests", "NksHub.Textida.App.Tests", "TranslationReview.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>Every text of a language from the three sources, by catalogue and key; a plugin key is written <c>Plugin:key</c>.</summary>
    internal static IEnumerable<(string Catalogue, string Key, string Text)> Texts(string lang)
    {
        foreach (var (key, text) in Loc.Catalog(lang)) yield return ("app", key, text);
        foreach (var (key, text) in TranslationGateTests.WorkbenchTexts(lang)) yield return ("workbench", key, text);
        int column = Array.IndexOf(PluginColumns, lang);
        foreach (var (plugin, key, columns) in TranslationGateTests.PluginRows())
            if (columns.Length == 4) yield return ("plugin", $"{plugin}:{key}", columns[column]);
    }

    private static bool Matches(Rule rule, string catalogue, string key) =>
        rule.Catalogue == catalogue && rule.Prefixes.Any(p => p == "*" || key.StartsWith(p, StringComparison.Ordinal));

    /// <summary>The first area, in the order of the register, whose rules take the key.</summary>
    private static string? AreaOf(Register register, string catalogue, string key) =>
        register.Areas.FirstOrDefault(a => a.Rules.Any(r => Matches(r, catalogue, key)))?.Id;

    /// <summary>Every text of a language with its area and the review of that area.</summary>
    internal static IEnumerable<Row> Inventory(string lang) => Inventory(LoadRegister(), lang);

    private static IEnumerable<Row> Inventory(Register register, string lang)
    {
        foreach (var (catalogue, key, text) in Texts(lang))
        {
            string area = AreaOf(register, catalogue, key) ?? "";
            var review = register.Reviews.FirstOrDefault(r => r.Language == lang && r.Area == area);
            yield return new Row(lang, catalogue, key, area, review?.Reviewer ?? "", review?.ReviewedOn ?? "", text);
        }
    }

    private static string Cell(string text) => text.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");

    /// <summary>Writes one sheet per language, <c>{language}.tsv</c>, for the person who reviews the texts: the key, the area, the review and the text to read.</summary>
    internal static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        var register = LoadRegister();
        foreach (var lang in StringTable.UiLanguages)
        {
            var lines = new List<string> { "language\tcatalogue\tkey\tarea\treviewer\treviewedOn\ttext" };
            lines.AddRange(Inventory(register, lang).OrderBy(r => r.Area, StringComparer.Ordinal).ThenBy(r => r.Catalogue, StringComparer.Ordinal).ThenBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => string.Join('\t', r.Language, r.Catalogue, r.Key, r.Area, r.Reviewer, r.ReviewedOn, Cell(r.Text))));
            File.WriteAllLines(Path.Combine(directory, lang + ".tsv"), lines, new UTF8Encoding(false));
        }
    }

    // ---- the register ----

    [Fact]
    public void The_register_has_one_review_for_every_language_and_area_and_never_a_name_without_a_date()
    {
        var register = LoadRegister();
        Assert.Equal(1, register.SchemaVersion);
        Assert.Equal(register.Areas.Length, register.Areas.Select(a => a.Id).Distinct().Count());
        Assert.All(register.Areas, a =>
        {
            Assert.NotEmpty(a.Rules);
            Assert.All(a.Rules, r =>
            {
                Assert.Contains(r.Catalogue, Catalogues);
                Assert.NotEmpty(r.Prefixes);
            });
        });

        var expected = StringTable.UiLanguages.SelectMany(l => register.Areas.Select(a => (l, a.Id))).Order().ToList();
        Assert.Equal(expected, register.Reviews.Select(r => (r.Language, r.Area)).Order().ToList());

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var review in register.Reviews)
        {
            if (review.IsOpen) Assert.True(review.ReviewedOn.Length == 0, $"{review.Language}/{review.Area}: an open review has no date");
            else
            {
                Assert.True(DateOnly.TryParseExact(review.ReviewedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= today,
                    $"{review.Language}/{review.Area}: reviewer {review.Reviewer} needs the date of the review (yyyy-MM-dd, not in the future)");
            }
        }
    }

    [Fact]
    public void Every_text_belongs_to_an_area_and_every_area_and_rule_is_in_use()
    {
        var register = LoadRegister();
        var texts = Texts("cs").ToList();
        Assert.Contains(texts, t => t.Catalogue == "app");
        Assert.Contains(texts, t => t.Catalogue == "workbench");
        Assert.Contains(texts, t => t.Catalogue == "plugin");

        var unassigned = texts.Where(t => AreaOf(register, t.Catalogue, t.Key) is null).Select(t => $"{t.Catalogue}:{t.Key}").ToList();
        Assert.True(unassigned.Count == 0, "texts that no area takes (add a prefix to an area of TranslationReview.json): " + string.Join(", ", unassigned.Take(40)));

        foreach (var area in register.Areas)
        {
            Assert.True(texts.Any(t => AreaOf(register, t.Catalogue, t.Key) == area.Id), $"area {area.Id} takes no text");
            foreach (var rule in area.Rules)
                foreach (var prefix in rule.Prefixes)
                    Assert.True(texts.Any(t => Matches(new Rule(rule.Catalogue, [prefix]), t.Catalogue, t.Key)), $"area {area.Id}: the prefix {rule.Catalogue}:{prefix} matches no text");
        }
    }

    [Fact]
    public void The_inventory_of_every_language_has_the_same_keys_and_gives_each_a_review()
    {
        var register = LoadRegister();
        // cs and sk have a .few plural form that en and de do not need, so the keys are compared without their plural category
        static List<(string, string)> Keys(IEnumerable<Row> rows) =>
            rows.Select(r => (r.Catalogue, PluralForm.Replace(r.Key, ""))).Distinct().Order().ToList();
        var reference = Keys(Inventory(register, "cs"));
        Assert.True(reference.Count > 2000, $"only {reference.Count} texts in the inventory");
        foreach (var lang in StringTable.UiLanguages)
        {
            var rows = Inventory(register, lang).ToList();
            Assert.True(Keys(rows).SequenceEqual(reference), $"{lang}: the keys differ from the keys of the default language");
            Assert.All(rows, r =>
            {
                Assert.True(r.Area.Length > 0, $"{lang}:{r.Catalogue}:{r.Key} has no area");
                Assert.False(string.IsNullOrWhiteSpace(r.Text), $"{lang}:{r.Catalogue}:{r.Key} is empty");
            });
            // every text has a review record, so a language is reviewed only when every record names a reader
            Assert.Equal(register.Areas.Length, rows.Select(r => r.Area).Distinct().Count());
        }
    }

    [Fact]
    public void The_inventory_is_exported_as_one_sheet_per_language_for_the_reviewer()
    {
        using var dir = new TempDir();
        Export(dir.Path);
        // UI_REVIEW_EXPORT=<folder> dotnet test --filter TranslationReviewTests writes the sheets to hand to the reviewers
        if (Environment.GetEnvironmentVariable("UI_REVIEW_EXPORT") is { Length: > 0 } target) Export(target);

        foreach (var lang in StringTable.UiLanguages)
        {
            var lines = File.ReadAllLines(Path.Combine(dir.Path, lang + ".tsv"));
            Assert.Equal("language\tcatalogue\tkey\tarea\treviewer\treviewedOn\ttext", lines[0]);
            Assert.Equal(Texts(lang).Count() + 1, lines.Length);
            Assert.All(lines.Skip(1), l => Assert.Equal(7, l.Split('\t').Length));
            var file = Assert.Single(lines, l => l.StartsWith($"{lang}\tworkbench\tmenu.file\t", StringComparison.Ordinal));
            Assert.Equal(["menus", "OPEN", ""], file.Split('\t')[3..6]);
        }
    }

    // ---- gates over the texts themselves ----

    private static Dictionary<(string Catalogue, string Key), string> Table(string lang) =>
        Texts(lang).Where(t => t.Catalogue != "plugin").ToDictionary(t => (t.Catalogue, t.Key), t => t.Text);

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}");
    private static readonly Regex PluralForm = new(@"\.(one|few|many|other)$");

    /// <summary>How a text ends where it matters to the reader: an ellipsis says that a dialog follows, a colon that something follows in the same row.</summary>
    private static string Ending(string text)
    {
        string plain = Placeholder.Replace(text, "x").TrimEnd();
        return plain.EndsWith("...", StringComparison.Ordinal) || plain.EndsWith('…') ? "…" : plain.EndsWith(':') ? ":" : "";
    }

    [Fact]
    public void A_text_ends_with_an_ellipsis_or_a_colon_in_every_language_or_in_none()
    {
        var tables = StringTable.UiLanguages.ToDictionary(l => l, Table);
        var problems = new List<string>();
        foreach (var key in tables["en"].Keys.Where(k => StringTable.UiLanguages.All(l => tables[l].ContainsKey(k))))
        {
            var endings = StringTable.UiLanguages.ToDictionary(l => l, l => Ending(tables[l][key]));
            if (endings.Values.Distinct().Count() > 1) problems.Add($"{key.Catalogue}:{key.Key}: " + string.Join(", ", endings.Select(e => $"{e.Key} '{e.Value}'")));
        }
        Assert.True(problems.Count == 0, "the texts end differently in the languages:\n" + string.Join("\n", problems));
    }

    private static readonly Regex KeyName = new(@"\b(?:(?:Ctrl|Strg|Shift|Umschalt|Alt|Cmd)\+)+(?:[A-Z0-9]|F\d{1,2}|Enter|Esc|Tab|Space|Del|Home|End)\b");

    [Fact]
    public void A_shortcut_written_in_a_text_is_the_same_shortcut_in_every_language()
    {
        var tables = StringTable.UiLanguages.ToDictionary(l => l, Table);
        var problems = new List<string>();
        int withKeys = 0;
        foreach (var key in tables["en"].Keys.Where(k => StringTable.UiLanguages.All(l => tables[l].ContainsKey(k))))
        {
            // the German names of the modifier keys are the only difference
            var shortcuts = StringTable.UiLanguages.ToDictionary(l => l, l => string.Join(' ', KeyName.Matches(tables[l][key]).Select(m => m.Value.Replace("Strg", "Ctrl").Replace("Umschalt", "Shift")).Distinct().Order()));
            if (shortcuts.Values.Any(s => s.Length > 0)) withKeys++;
            if (shortcuts.Values.Distinct().Count() > 1) problems.Add($"{key.Catalogue}:{key.Key}: " + string.Join(", ", shortcuts.Select(e => $"{e.Key} '{e.Value}'")));
        }
        Assert.True(withKeys >= 3, $"only {withKeys} texts with a shortcut were found");
        Assert.True(problems.Count == 0, "the shortcuts differ between the languages:\n" + string.Join("\n", problems));
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("en")]
    [InlineData("sk")]
    [InlineData("de")]
    public void The_find_bar_tips_name_the_accelerators_the_bar_handles_and_no_two_are_the_same(string lang)
    {
        var catalogue = Loc.Catalog(lang);
        (string Key, string Accelerator)[] tips = [(K.FindCaseTip, "Alt+C"), (K.FindRegexTip, "Alt+R"), (K.FindWordTip, "Alt+W")];
        foreach (var (key, accelerator) in tips)
            Assert.True(catalogue[key].Contains($"({accelerator})", StringComparison.Ordinal), $"{lang}:{key} does not name {accelerator}: {catalogue[key]}");
        Assert.Equal(tips.Length, tips.Select(t => t.Accelerator).Distinct().Count());
    }
}

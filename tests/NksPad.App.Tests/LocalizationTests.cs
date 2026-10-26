using System.Reflection;
using System.Text;
using NksPad.App.Text;

namespace NksPad.App.Tests;

/// <summary>Tests that read or change the process-wide active language must not run in parallel.</summary>
[CollectionDefinition("LocState", DisableParallelization = true)]
public class LocStateCollection;

[Collection("LocState")]
public class LocalizationTests : IDisposable
{
    private static readonly string[] PluralSuffixes = ["one", "few", "many", "other"];

    public void Dispose() => Loc.SetLanguage(Loc.DefaultLanguage);

    private static IEnumerable<string> KeyConstants() =>
        typeof(K).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    private static bool IsPluralForm(IReadOnlyDictionary<string, string> catalog, string key)
    {
        int dot = key.LastIndexOf('.');
        return dot > 0 && PluralSuffixes.Contains(key[(dot + 1)..]) && !catalog.ContainsKey(key[..dot]) && catalog.ContainsKey(key[..dot] + ".other");
    }

    private static HashSet<string> BaseKeys(string lang)
    {
        var c = Loc.Catalog(lang);
        return c.Keys.Select(k => IsPluralForm(c, k) ? k[..k.LastIndexOf('.')] : k).ToHashSet();
    }

    private static IEnumerable<string> PluralBases(string lang)
    {
        var c = Loc.Catalog(lang);
        return c.Keys.Where(k => IsPluralForm(c, k)).Select(k => k[..k.LastIndexOf('.')]).Distinct();
    }

    public static IEnumerable<object[]> Languages() => Loc.Languages.Select(l => new object[] { l });

    [Fact]
    public void Default_language_and_english_ship()
    {
        Assert.Contains("cs", Loc.Languages);
        Assert.Contains("en", Loc.Languages);
        Assert.Equal("cs", Loc.DefaultLanguage);
    }

    [Theory, MemberData(nameof(Languages))]
    public void Every_language_has_every_key_of_the_default_language(string lang)
    {
        var missing = BaseKeys("cs").Except(BaseKeys(lang)).ToList();
        var extra = BaseKeys(lang).Except(BaseKeys("cs")).ToList();
        Assert.True(missing.Count == 0, $"{lang} lacks: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"{lang} has keys the default language does not: {string.Join(", ", extra)}");
    }

    [Theory, MemberData(nameof(Languages))]
    public void Every_key_used_in_code_exists_in_every_language(string lang)
    {
        var c = Loc.Catalog(lang);
        var plurals = PluralBases(lang).ToHashSet();
        var missing = KeyConstants().Where(k => !plurals.Contains(k) && !c.ContainsKey(k) && !(c.ContainsKey(k + ".other"))).ToList();
        Assert.True(missing.Count == 0, $"{lang} lacks keys used by code: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_key_of_the_catalogue_is_used_by_code()
    {
        var constants = KeyConstants().ToHashSet();
        var unused = BaseKeys("cs").Except(constants).ToList();
        Assert.True(unused.Count == 0, "keys without a constant in K: " + string.Join(", ", unused));
        Assert.Equal(constants.Count, KeyConstants().Count());
    }

    [Fact]
    public void Every_constant_in_K_is_referenced_from_code()
    {
        var sources = Directory.EnumerateFiles(Repo.AppSources, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && Path.GetFileName(f) != "K.cs")
            .Select(File.ReadAllText).ToList();
        var unused = typeof(K).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral)
            .Select(f => f.Name).Where(n => !sources.Any(s => s.Contains("K." + n, StringComparison.Ordinal))).ToList();
        Assert.True(unused.Count == 0, "K constants nobody uses: " + string.Join(", ", unused));
    }

    [Theory, MemberData(nameof(Languages))]
    public void No_text_is_empty_or_only_whitespace(string lang) =>
        Assert.All(Loc.Catalog(lang), kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{lang}:{kv.Key}"));

    [Theory, MemberData(nameof(Languages))]
    public void Placeholders_match_the_default_language(string lang)
    {
        var def = Loc.Catalog("cs");
        var c = Loc.Catalog(lang);
        foreach (var key in BaseKeys("cs"))
        {
            if (def.TryGetValue(key, out var plain))
            {
                Assert.True(Loc.Placeholders(plain).SetEquals(Loc.Placeholders(c[key])), $"{lang}:{key} placeholders differ: {plain} / {c[key]}");
                continue;
            }
            // plural forms may drop {count} ("one tab") but must not invent names
            var allowed = def.Where(kv => kv.Key.StartsWith(key + ".")).SelectMany(kv => Loc.Placeholders(kv.Value)).Append("count").ToHashSet();
            foreach (var kv in c.Where(kv => kv.Key.StartsWith(key + ".")))
                Assert.True(Loc.Placeholders(kv.Value).IsSubsetOf(allowed), $"{lang}:{kv.Key} uses an unknown placeholder: {kv.Value}");
        }
    }

    [Theory, MemberData(nameof(Languages))]
    public void Plural_keys_have_exactly_the_forms_of_the_language(string lang)
    {
        var c = Loc.Catalog(lang);
        var required = PluralRules.Categories(lang);
        foreach (var key in PluralBases("cs"))
        {
            var present = PluralSuffixes.Where(s => c.ContainsKey(key + "." + s)).ToList();
            Assert.True(required.All(present.Contains), $"{lang}:{key} needs forms {string.Join("/", required)}, has {string.Join("/", present)}");
            Assert.True(present.All(required.Contains), $"{lang}:{key} has forms the language does not use: {string.Join("/", present)}");
        }
    }

    [Theory, MemberData(nameof(Languages))]
    public void Plural_text_and_placeholder_work_for_a_range_of_counts(string lang)
    {
        Loc.SetLanguage(lang);
        foreach (long n in new long[] { 0, 1, 2, 3, 4, 5, 11, 21, 22, 100, 101 })
        {
            var text = Loc.Plural(K.StatusTabs, n);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain('{', text);
            Assert.DoesNotContain('}', text);
            if (lang != "cs" || n != 1) Assert.Contains(n.ToString(), text);
        }
        Assert.Equal(lang == "cs" ? "1 otevřená karta" : "1 open tab", Loc.Plural(K.StatusTabs, 1));
    }

    [Theory]
    [InlineData("cs", 0, "other"), InlineData("cs", 1, "one"), InlineData("cs", 2, "few"), InlineData("cs", 4, "few"), InlineData("cs", 5, "other"), InlineData("cs", 21, "other")]
    [InlineData("en", 0, "other"), InlineData("en", 1, "one"), InlineData("en", 2, "other"), InlineData("en", 21, "other")]
    [InlineData("sk", 3, "few"), InlineData("de", 3, "other"), InlineData("xx", 1, "one")]
    public void Plural_categories(string lang, long n, string expected) => Assert.Equal(expected, PluralRules.Select(lang, n));

    [Fact]
    public void Placeholders_are_filled_by_name_and_braces_can_be_escaped()
    {
        Assert.Equal("a 5 b x", Loc.Format("a {n} b {name}", [("n", 5), ("name", "x")]));
        Assert.Equal("{literal} 7", Loc.Format("{{literal}} {n}", [("n", 7)]));
        Assert.Equal("keeps {missing}", Loc.Format("keeps {missing}", []));
        Assert.Equal("1.5", Loc.Format("{v}", [("v", 1.5)])); // invariant culture, so text is the same on every machine
    }

    [Fact]
    public void Missing_key_falls_back_to_the_default_language_then_to_the_key()
    {
        var def = new Dictionary<string, string> { ["a"] = "default a", ["b"] = "default b" };
        var cur = new Dictionary<string, string> { ["a"] = "current a" };
        Assert.Equal("current a", Loc.Resolve(cur, def, "a"));
        Assert.Equal("default b", Loc.Resolve(cur, def, "b"));
        Assert.Equal("c", Loc.Resolve(cur, def, "c"));
        Assert.Equal("default b", Loc.Resolve(cur, def, "x.few", fallbackKey: "b"));
    }

    [Fact]
    public void Unknown_language_tag_uses_the_default_and_switching_back_restores_the_texts()
    {
        var cs = Loc.Get(K.MenuFile);
        Assert.Equal("en", Loc.SetLanguage("en"));
        var en = Loc.Get(K.MenuFile);
        Assert.NotEqual(cs, en);
        Assert.Equal("cs", Loc.SetLanguage("xx"));
        Assert.Equal(cs, Loc.Get(K.MenuFile));
    }

    [Fact]
    public void Language_change_is_announced_once_and_only_on_a_real_change()
    {
        int raised = 0;
        void Handler() => raised++;
        Loc.Changed += Handler;
        try
        {
            Loc.SetLanguage("en");
            Loc.SetLanguage("en");
            Loc.SetLanguage("cs");
        }
        finally { Loc.Changed -= Handler; }
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Unknown_key_is_visible_instead_of_empty() => Assert.Equal("no.such.key", Loc.Get("no.such.key"));

    [Fact]
    public void Inventory_covers_the_text_categories_of_the_shell()
    {
        var prefixes = BaseKeys("cs").Select(k => k[..k.IndexOf('.')]).ToHashSet();
        foreach (var required in new[] { "menu", "status", "panel", "dialog", "error", "a11y" }) Assert.Contains(required, prefixes);
    }

    [Fact]
    public void Inventory_document_is_up_to_date()
    {
        var path = Path.Combine(Repo.Root, "docs", "p1", "ui-text-inventory.md");
        var expected = BuildInventory();
        if (Environment.GetEnvironmentVariable("UPDATE_INVENTORY") == "1") File.WriteAllText(path, expected, new UTF8Encoding(false));
        Assert.True(File.Exists(path), "docs/p1/ui-text-inventory.md is missing; run the tests with UPDATE_INVENTORY=1");
        Assert.Equal(expected.ReplaceLineEndings("\n"), File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    private static string BuildInventory()
    {
        var cs = Loc.Catalog("cs");
        var plurals = PluralBases("cs").ToHashSet();
        var sb = new StringBuilder();
        sb.AppendLine("# Inventář textů rozhraní");
        sb.AppendLine();
        sb.AppendLine("Vzniká z `src/NksPad.App/Strings/cs.json` testem `Inventory_document_is_up_to_date`; ruční úpravy test odmítne. Přegenerování: `UPDATE_INVENTORY=1 dotnet test tests/NksPad.App.Tests`.");
        sb.AppendLine();
        sb.AppendLine($"Jazyky v prostředcích: {string.Join(", ", Loc.Languages.Order())}. Výchozí: {Loc.DefaultLanguage}. Počet klíčů: {BaseKeys("cs").Count}.");
        sb.AppendLine("Které jazyky vyjdou v MVP, rozhodne P1.1.7; infrastruktura nezná pevný počet jazyků.");
        sb.AppendLine("Paleta příkazů v P1.1 neexistuje, její klíče přibudou s ní.");
        foreach (var group in BaseKeys("cs").Order(StringComparer.Ordinal).GroupBy(k => k[..k.IndexOf('.')]))
        {
            sb.AppendLine().AppendLine($"## {group.Key}").AppendLine();
            sb.AppendLine("| Klíč | Zástupné parametry | Množné číslo | Výchozí text (cs) |").AppendLine("|---|---|---|---|");
            foreach (var key in group)
            {
                var text = plurals.Contains(key) ? cs[key + ".other"] : cs[key];
                var ph = string.Join(", ", Loc.Placeholders(text).Order().Select(p => "`" + p + "`"));
                sb.AppendLine($"| `{key}` | {(ph.Length == 0 ? "-" : ph)} | {(plurals.Contains(key) ? "ano" : "-")} | {text.Replace("|", "\\|")} |");
            }
        }
        return sb.ToString();
    }
}

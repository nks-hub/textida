namespace NksHub.Textida.Plugins.Json;

/// <summary>Texts the plugin shows through notifications, in the languages of the editor (cs, en, sk, de); the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
internal static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["title.format"] = ["Formátovat JSON", "Format JSON", "Formátovať JSON", "JSON formatieren"],
        ["title.minify"] = ["Sbalit JSON", "Minify JSON", "Zbaliť JSON", "JSON verdichten"],
        ["title.sortKeys"] = ["Seřadit klíče JSON", "Sort JSON Keys", "Zoradiť kľúče JSON", "JSON-Schlüssel sortieren"],
        ["title.fixCommas"] = ["Odstranit čárky na konci JSON", "Remove JSON Trailing Commas", "Odstrániť čiarky na konci JSON", "Abschließende Kommas in JSON entfernen"],

        ["unchanged.format"] = ["JSON už je naformátovaný.", "The JSON is already formatted.", "JSON je už naformátovaný.", "Das JSON ist bereits formatiert."],
        ["unchanged.minify"] = ["JSON už je sbalený.", "The JSON is already minified.", "JSON je už zbalený.", "Das JSON ist bereits verdichtet."],
        ["unchanged.sortKeys"] = ["Klíče už jsou seřazené.", "The keys are already sorted.", "Kľúče sú už zoradené.", "Die Schlüssel sind bereits sortiert."],
        ["unchanged.fixCommas"] = ["Nenašla se žádná čárka před uzavírací závorkou.", "No trailing comma was found.", "Nenašla sa žiadna čiarka pred uzatváracou zátvorkou.", "Es wurde kein abschließendes Komma gefunden."],

        ["doneOne.format"] = ["JSON byl naformátován.", "JSON formatted.", "JSON bol naformátovaný.", "JSON wurde formatiert."],
        ["doneMany.format"] = ["Naformátováno částí: {count}.", "{count} ranges formatted.", "Naformátovaných častí: {count}.", "{count} Bereiche formatiert."],
        ["doneOne.minify"] = ["JSON byl sbalen.", "JSON minified.", "JSON bol zbalený.", "JSON wurde verdichtet."],
        ["doneMany.minify"] = ["Sbaleno částí: {count}.", "{count} ranges minified.", "Zbalených častí: {count}.", "{count} Bereiche verdichtet."],
        ["doneOne.sortKeys"] = ["Klíče byly seřazeny.", "Keys sorted.", "Kľúče boli zoradené.", "Schlüssel wurden sortiert."],
        ["doneMany.sortKeys"] = ["Seřazeno částí: {count}.", "{count} ranges sorted.", "Zoradených častí: {count}.", "{count} Bereiche sortiert."],
        ["doneOne.fixCommas"] = ["Čárky na konci byly odstraněny.", "Trailing commas removed.", "Čiarky na konci boli odstránené.", "Abschließende Kommas wurden entfernt."],
        ["doneMany.fixCommas"] = ["Čárky na konci odstraněny v částech: {count}.", "Trailing commas removed in {count} ranges.", "Čiarky na konci odstránené v častiach: {count}.", "Abschließende Kommas in {count} Bereichen entfernt."],

        ["syntax"] = ["Chyba syntaxe JSON na řádku {line}, sloupci {column}: {detail}", "JSON syntax error at line {line}, column {column}: {detail}", "Chyba syntaxe JSON na riadku {line}, stĺpci {column}: {detail}", "JSON-Syntaxfehler in Zeile {line}, Spalte {column}: {detail}"],
        ["syntaxInRange"] = ["Chyba syntaxe JSON na řádku {line}, sloupci {column} označeného textu: {detail}", "JSON syntax error at line {line}, column {column} of the selected text: {detail}", "Chyba syntaxe JSON na riadku {line}, stĺpci {column} označeného textu: {detail}", "JSON-Syntaxfehler in Zeile {line}, Spalte {column} des markierten Textes: {detail}"],
        ["detail.comment"] = ["V JSON nejsou komentáře povolené. Použijte soubor .jsonc, nebo je odstraňte.", "Comments are not allowed in JSON. Use a .jsonc file or remove them.", "V JSON nie sú komentáre povolené. Použite súbor .jsonc, alebo ich odstráňte.", "Kommentare sind in JSON nicht erlaubt. Verwenden Sie eine .jsonc-Datei oder entfernen Sie sie."],
        ["detail.trailingComma"] = ["V JSON není čárka před zavírací závorkou povolená. Spusťte nejdřív „Odstranit čárky na konci JSON“.", "A trailing comma is not allowed in JSON. Run \"Remove JSON Trailing Commas\" first.", "V JSON nie je čiarka pred zatváracou zátvorkou povolená. Najprv spustite „Odstrániť čiarky na konci JSON“.", "Ein abschließendes Komma ist in JSON nicht erlaubt. Führen Sie zuerst „Abschließende Kommas in JSON entfernen“ aus."],
        ["detail.depth"] = ["Vnoření je hlubší než {max} úrovní.", "The nesting is deeper than {max} levels.", "Vnorenie je hlbšie ako {max} úrovní.", "Die Verschachtelung ist tiefer als {max} Ebenen."],
        ["selfCheck"] = ["Výsledek neprošel kontrolou, proto se nic nezměnilo.", "The result failed its check, so nothing was changed.", "Výsledok neprešiel kontrolou, preto sa nič nezmenilo.", "Das Ergebnis hat die Prüfung nicht bestanden, deshalb wurde nichts geändert."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }

    /// <summary>The wording of one operation (<c>format</c>, <c>minify</c>, <c>sortKeys</c>, <c>fixCommas</c>) for the messages of the range flow that differ from the standard ones.</summary>
    public static IReadOnlyDictionary<string, string[]> ForOperation(string operation) =>
        new[] { "unchanged", "doneOne", "doneMany" }.ToDictionary(key => key, key => Table[key + "." + operation]);

    /// <summary>The message for a text that failed to read, or whose result failed its check. <paramref name="inSelection"/>: the line and column count from the start of the text that was read, not of the document.</summary>
    public static string Syntax(string language, JsonFormatter.SyntaxError error, bool inSelection, int maxDepth)
    {
        if (error.Kind == JsonErrorKind.SelfCheck) return Get(language, "selfCheck");
        var detail = error.Kind switch
        {
            JsonErrorKind.Comment => Get(language, "detail.comment"),
            JsonErrorKind.TrailingComma => Get(language, "detail.trailingComma"),
            JsonErrorKind.Depth => Get(language, "detail.depth", ("max", Math.Clamp(maxDepth, 1, JsonFormatter.StackSafeDepth).ToString())),
            _ => error.Detail,
        };
        return Get(language, inSelection ? "syntaxInRange" : "syntax", ("line", error.Line.ToString()), ("column", error.Column.ToString()), ("detail", detail));
    }
}

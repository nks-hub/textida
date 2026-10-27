namespace NksHub.Textida.Plugins.WebData;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["stale"] = ["Dokument se mezitím změnil. Spusťte příkaz znovu.", "The document changed in the meantime. Run the command again.", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus."],
        ["tooLarge"] = ["Dokument má {size} B, limit je {max} B.", "The document is {size} bytes; the limit is {max} bytes.", "Dokument má {size} B, limit je {max} B.", "Das Dokument hat {size} Bytes, das Limit ist {max} Bytes."],
        ["color.none"] = ["V označení ani u kurzoru není žádná barva.", "There is no colour in the selection or at the caret.", "V označení ani pri kurzore nie je žiadna farba.", "In der Markierung oder am Cursor steht keine Farbe."],
        ["color.unchanged"] = ["Barva už je v tomto zápisu.", "The colour is already written this way.", "Farba je už v tomto zápise.", "Die Farbe ist bereits so geschrieben."],
        ["color.doneOne"] = ["Barva byla převedena.", "The colour was converted.", "Farba bola prevedená.", "Die Farbe wurde umgewandelt."],
        ["color.doneMany"] = ["Převedeno barev: {count}.", "{count} colours converted.", "Prevedených farieb: {count}.", "{count} Farben umgewandelt."],
        ["keys.tooMany"] = ["Soubor má víc klíčů, než se vejde do přehledu ({max}). Zobrazí se prvních {max}.", "The file has more keys than the outline holds ({max}). The first {max} are shown.", "Súbor má viac kľúčov, než sa zmestí do prehľadu ({max}). Zobrazí sa prvých {max}.", "Die Datei hat mehr Schlüssel, als die Gliederung fasst ({max}). Die ersten {max} werden gezeigt."],
        ["keys.syntax"] = ["Chyba syntaxe na řádku {line}: klíče za ní se nezobrazí.", "Syntax error at line {line}: the keys after it are not shown.", "Chyba syntaxe na riadku {line}: kľúče za ňou sa nezobrazia.", "Syntaxfehler in Zeile {line}: Die Schlüssel danach werden nicht gezeigt."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}

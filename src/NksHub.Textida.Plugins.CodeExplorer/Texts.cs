namespace NksHub.Textida.Plugins.CodeExplorer;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["tooLarge"] = ["Dokument má {size} B, limit je {max} B.", "The document is {size} bytes; the limit is {max} bytes.", "Dokument má {size} B, limit je {max} B.", "Das Dokument hat {size} Bytes, das Limit ist {max} Bytes."],
        ["tooMany"] = ["Soubor má víc symbolů, než se vejde do osnovy ({max}). Zobrazí se prvních {max}.", "The file has more symbols than the outline holds ({max}). The first {max} are shown.", "Súbor má viac symbolov, než sa zmestí do osnovy ({max}). Zobrazí sa prvých {max}.", "Die Datei hat mehr Symbole, als die Gliederung fasst ({max}). Die ersten {max} werden gezeigt."],
        ["partial"] = ["Soubor je větší než {max} B. Osnova vznikla jen z jeho začátku.", "The file is larger than {max} bytes. The outline is made from its beginning only.", "Súbor je väčší než {max} B. Osnova vznikla len z jeho začiatku.", "Die Datei ist größer als {max} Bytes. Die Gliederung entsteht nur aus ihrem Anfang."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}

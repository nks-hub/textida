namespace NksHub.Textida.Plugins.Emmet;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["none"] = ["Před kurzorem není žádná zkratka Emmet.", "There is no Emmet abbreviation in front of the caret.", "Pred kurzorom nie je žiadna skratka Emmet.", "Vor dem Cursor steht keine Emmet-Abkürzung."],
        ["stale"] = ["Dokument se mezitím změnil, zkratka se nerozbalila.", "The document changed meanwhile; the abbreviation was not expanded.", "Dokument sa medzitým zmenil, skratka sa nerozbalila.", "Das Dokument hat sich inzwischen geändert; die Abkürzung wurde nicht erweitert."],
        ["tooLarge"] = ["Rozbalení by bylo delší než {max} znaků.", "The expansion would be longer than {max} characters.", "Rozbalenie by bolo dlhšie než {max} znakov.", "Die Erweiterung wäre länger als {max} Zeichen."],
        ["invalid"] = ["Zkratka není platná.", "The abbreviation is not valid.", "Skratka nie je platná.", "Die Abkürzung ist ungültig."],
        ["failed"] = ["Zkratku se nepodařilo rozbalit.", "The abbreviation could not be expanded.", "Skratku sa nepodarilo rozbaliť.", "Die Abkürzung konnte nicht erweitert werden."],
        ["preview.title"] = ["Náhled zkratky Emmet", "Emmet abbreviation preview", "Náhľad skratky Emmet", "Vorschau der Emmet-Abkürzung"],
        ["preview.message"] = ["{abbreviation}\n\n{text}\n\nRozbalit?", "{abbreviation}\n\n{text}\n\nExpand it?", "{abbreviation}\n\n{text}\n\nRozbaliť?", "{abbreviation}\n\n{text}\n\nErweitern?"],
        ["wrap.title"] = ["Obalit zkratkou Emmet", "Wrap with an Emmet abbreviation", "Obaliť skratkou Emmet", "Mit Emmet-Abkürzung umschließen"],
        ["wrap.label"] = ["Zkratka (například div.box nebo ul>li*):", "Abbreviation (for example div.box or ul>li*):", "Skratka (napríklad div.box alebo ul>li*):", "Abkürzung (zum Beispiel div.box oder ul>li*):"],
        ["wrap.empty"] = ["Nejdřív označte text, který se má obalit.", "Select the text to wrap first.", "Najprv označte text, ktorý sa má obaliť.", "Markieren Sie zuerst den Text, der umschlossen werden soll."],
        ["wrap.html"] = ["Obalení zkratkou funguje jen v HTML.", "Wrapping with an abbreviation works in HTML only.", "Obalenie skratkou funguje len v HTML.", "Das Umschließen mit einer Abkürzung funktioniert nur in HTML."],
        ["wrap.tooBig"] = ["Označený text je větší než {max} bajtů.", "The selected text is larger than {max} bytes.", "Označený text je väčší než {max} bajtov.", "Der markierte Text ist größer als {max} Bytes."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}

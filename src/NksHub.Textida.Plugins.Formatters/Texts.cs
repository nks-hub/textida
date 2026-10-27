namespace NksHub.Textida.Plugins.Formatters;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["sql.title"] = ["Formátovat SQL", "Format SQL", "Formátovať SQL", "SQL formatieren"],
        ["tidy.title"] = ["Formátovat pomocí HTML Tidy", "Format with HTML Tidy", "Formátovať pomocou HTML Tidy", "Mit HTML Tidy formatieren"],
        ["tidy.missing"] = ["HTML Tidy nebyl nalezen. Hledáno v: {places}; a v každé složce z PATH. Nastavte cestu v nastavení „html.tidyPath“.", "HTML Tidy was not found. Looked in: {places}; and in every folder of PATH. Set the path in the setting \"html.tidyPath\".", "HTML Tidy sa nenašiel. Hľadalo sa v: {places}; a v každom priečinku z PATH. Nastavte cestu v nastavení „html.tidyPath“.", "HTML Tidy wurde nicht gefunden. Gesucht in: {places}; und in jedem Ordner von PATH. Legen Sie den Pfad in der Einstellung „html.tidyPath“ fest."],
        ["tidy.timeout"] = ["HTML Tidy neskončil do {seconds} s a byl ukončen.", "HTML Tidy did not finish within {seconds} s and was stopped.", "HTML Tidy neskončil do {seconds} s a bol ukončený.", "HTML Tidy wurde nach {seconds} s nicht fertig und wurde beendet."],
        ["tidy.start"] = ["HTML Tidy se nepodařilo spustit: {reason}", "HTML Tidy could not be started: {reason}", "HTML Tidy sa nepodarilo spustiť: {reason}", "HTML Tidy konnte nicht gestartet werden: {reason}"],
        ["tidy.noOutput"] = ["HTML Tidy nevrátil žádný výsledek (kód {code}). Zprávy jsou v okně výstupu.", "HTML Tidy gave no result (exit code {code}). The messages are in the output window.", "HTML Tidy nevrátil žiadny výsledok (kód {code}). Správy sú v okne výstupu.", "HTML Tidy lieferte kein Ergebnis (Exitcode {code}). Die Meldungen stehen im Ausgabefenster."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}

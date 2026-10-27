namespace NksHub.Textida.Plugins.Lsp;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["noServer"] = ["Jazykový server pro {language} nebyl nalezen: {program}", "No language server for {language} was found: {program}", "Jazykový server pre {language} sa nenašiel: {program}", "Für {language} wurde kein Sprachserver gefunden: {program}"],
        ["noLanguage"] = ["Pro tento typ dokumentu není nastavený žádný jazykový server.", "No language server is set up for this kind of document.", "Pre tento typ dokumentu nie je nastavený žiadny jazykový server.", "Für diese Art von Dokument ist kein Sprachserver eingerichtet."],
        ["startFailed"] = ["Jazykový server pro {language} se nepodařilo spustit: {reason}", "The language server for {language} could not be started: {reason}", "Jazykový server pre {language} sa nepodarilo spustiť: {reason}", "Der Sprachserver für {language} konnte nicht gestartet werden: {reason}"],
        ["crashed"] = ["Jazykový server pro {language} neočekávaně skončil ({detail}). Spusťte příkaz znovu, server se spustí a otevřené dokumenty se mu předají znovu.", "The language server for {language} stopped unexpectedly ({detail}). Run the command again: the server starts and gets the open documents again.", "Jazykový server pre {language} neočakávane skončil ({detail}). Spustite príkaz znova, server sa spustí a otvorené dokumenty sa mu odovzdajú znova.", "Der Sprachserver für {language} wurde unerwartet beendet ({detail}). Führen Sie den Befehl erneut aus: Der Server startet und erhält die offenen Dokumente erneut."],
        ["tooLarge"] = ["Soubor je větší než {max} B, jazykový server ho nedostane.", "The file is larger than {max} bytes, so the language server does not get it.", "Súbor je väčší než {max} B, jazykový server ho nedostane.", "Die Datei ist größer als {max} Bytes, der Sprachserver erhält sie nicht."],
        ["invalidBytes"] = ["Soubor obsahuje bajty, které nejsou v jeho kódování platné. Jazykový server ho nedostane.", "The file contains bytes that are not valid in its encoding. The language server does not get it.", "Súbor obsahuje bajty, ktoré nie sú v jeho kódovaní platné. Jazykový server ho nedostane.", "Die Datei enthält Bytes, die in ihrer Kodierung nicht gültig sind. Der Sprachserver erhält sie nicht."],
        ["stale"] = ["Dokument se mezitím změnil. Spusťte příkaz znovu.", "The document changed meanwhile. Run the command again.", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus."],
        ["notSupported"] = ["Jazykový server tuto možnost nenabízí.", "The language server does not offer this.", "Jazykový server túto možnosť neponúka.", "Der Sprachserver bietet dies nicht an."],
        ["noDefinition"] = ["Definice nebyla nalezena.", "No definition was found.", "Definícia sa nenašla.", "Es wurde keine Definition gefunden."],
        ["noReferences"] = ["Žádné odkazy nebyly nalezeny.", "No references were found.", "Žiadne odkazy sa nenašli.", "Es wurden keine Verweise gefunden."],
        ["timeout"] = ["Jazykový server neodpověděl včas.", "The language server did not answer in time.", "Jazykový server neodpovedal včas.", "Der Sprachserver hat nicht rechtzeitig geantwortet."],
        ["failed"] = ["Jazykový server skončil chybou: {reason}", "The language server reported an error: {reason}", "Jazykový server skončil chybou: {reason}", "Der Sprachserver meldete einen Fehler: {reason}"],
        ["stopped"] = ["Jazykový server pro {language} je zastavený.", "The language server for {language} is stopped.", "Jazykový server pre {language} je zastavený.", "Der Sprachserver für {language} ist beendet."],
        ["restarted"] = ["Jazykový server pro {language} běží a má {count} otevřených dokumentů.", "The language server for {language} is running with {count} open documents.", "Jazykový server pre {language} beží a má {count} otvorených dokumentov.", "Der Sprachserver für {language} läuft mit {count} offenen Dokumenten."],
        ["definitionsTitle"] = ["Definice: {count}", "Definitions: {count}", "Definície: {count}", "Definitionen: {count}"],
        ["referencesTitle"] = ["Odkazy: {count}", "References: {count}", "Odkazy: {count}", "Verweise: {count}"],
        ["truncated"] = ["Míst je víc, než se vejde do seznamu. Zobrazí se prvních {max}.", "There are more places than the list holds. The first {max} are shown.", "Miest je viac, než sa zmestí do zoznamu. Zobrazí sa prvých {max}.", "Es gibt mehr Stellen, als die Liste fasst. Die ersten {max} werden gezeigt."],
        ["noMessage"] = ["(bez popisu)", "(no message)", "(bez popisu)", "(ohne Beschreibung)"],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }

    /// <summary>Every key has all four languages; a test walks this.</summary>
    public static bool Complete => Table.Values.All(v => v.Length == 4 && v.All(t => t.Length > 0));
}

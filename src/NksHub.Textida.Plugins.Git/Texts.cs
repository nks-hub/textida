namespace NksHub.Textida.Plugins.Git;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["noFile"] = ["Dokument není uložený jako soubor, Git ho nezná.", "The document is not saved as a file, so Git does not know it.", "Dokument nie je uložený ako súbor, Git ho nepozná.", "Das Dokument ist nicht als Datei gespeichert, Git kennt es nicht."],
        ["noRepository"] = ["Soubor není v repozitáři Git.", "The file is not in a Git repository.", "Súbor nie je v repozitári Git.", "Die Datei liegt in keinem Git-Repository."],
        ["noGit"] = ["Program git se nepodařilo spustit: {reason}", "Git could not be started: {reason}", "Program git sa nepodarilo spustiť: {reason}", "Git konnte nicht gestartet werden: {reason}"],
        ["failed"] = ["Git skončil chybou: {reason}", "Git failed: {reason}", "Git skončil chybou: {reason}", "Git ist fehlgeschlagen: {reason}"],
        ["stale"] = ["Dokument se mezitím změnilo. Spusťte příkaz znovu.", "The document changed meanwhile. Run the command again.", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus."],
        ["tooLarge"] = ["Soubor v indexu nebo v HEAD má víc než {max} B.", "The file in the index or in HEAD is larger than {max} bytes.", "Súbor v indexe alebo v HEAD má viac než {max} B.", "Die Datei im Index oder in HEAD ist größer als {max} Bytes."],
        ["clean"] = ["Pracovní strom je čistý.", "The working tree is clean.", "Pracovný strom je čistý.", "Der Arbeitsbaum ist sauber."],
        ["truncated"] = ["Změněných souborů je víc, než se vejde do panelu. Zobrazí se prvních {max}.", "More files changed than the panel holds. The first {max} are shown.", "Zmenených súborov je viac, než sa zmestí do panela. Zobrazí sa prvých {max}.", "Es haben sich mehr Dateien geändert, als das Panel fasst. Die ersten {max} werden gezeigt."],
        ["unmerged"] = ["Soubor má nevyřešený konflikt slučování. Vyřešte ho a teprve pak připravujte úseky.", "The file has an unresolved merge conflict. Resolve it before staging hunks.", "Súbor má nevyriešený konflikt zlučovania. Vyriešte ho a až potom pripravujte úseky.", "Die Datei hat einen ungelösten Merge-Konflikt. Lösen Sie ihn, bevor Sie Abschnitte vormerken."],
        ["noHunk"] = ["Kurzor nestojí v žádné změně oproti indexu.", "The caret is not in a change against the index.", "Kurzor nestojí v žiadnej zmene voči indexu.", "Der Cursor steht in keiner Änderung gegenüber dem Index."],
        ["staged"] = ["Úsek {hunk} souboru {file} je v indexu.", "Hunk {hunk} of {file} is staged.", "Úsek {hunk} súboru {file} je v indexe.", "Abschnitt {hunk} von {file} ist vorgemerkt."],
        ["conflict"] = ["Index souboru {file} se změnil od posledního načtení (jiným programem). Načetl jsem ho znovu a nic jsem nezapsal. Zkontrolujte změny a připravte úsek znovu.", "The index of {file} changed since it was last read (by another program). It was read again and nothing was written. Check the changes and stage the hunk again.", "Index súboru {file} sa zmenil od posledného načítania (iným programom). Načítal som ho znova a nič som nezapísal. Skontrolujte zmeny a pripravte úsek znova.", "Der Index von {file} hat sich seit dem letzten Lesen geändert (durch ein anderes Programm). Er wurde neu gelesen, es wurde nichts geschrieben. Prüfen Sie die Änderungen und merken Sie den Abschnitt erneut vor."],
        ["indexTitle"] = ["{file} (index)", "{file} (index)", "{file} (index)", "{file} (Index)"],
        ["headTitle"] = ["{file} (HEAD)", "{file} (HEAD)", "{file} (HEAD)", "{file} (HEAD)"],
        ["treeTitle"] = ["{file} (pracovní strom)", "{file} (working tree)", "{file} (pracovný strom)", "{file} (Arbeitsbaum)"],
        ["quota"] = ["Nejde otevřít další dokument pluginu. Zavřete některé starší porovnání Git.", "Another plugin document cannot be opened. Close an older Git comparison.", "Nejde otvoriť ďalší dokument pluginu. Zatvorte niektoré staršie porovnanie Git.", "Ein weiteres Plugin-Dokument kann nicht geöffnet werden. Schließen Sie einen älteren Git-Vergleich."],
        // status words
        ["staged:"] = ["V indexu", "Staged", "V indexe", "Vorgemerkt"],
        ["changed:"] = ["Ve stromu", "In tree", "V strome", "Im Baum"],
        ["M"] = ["změněno", "modified", "zmenené", "geändert"],
        ["A"] = ["přidáno", "added", "pridané", "hinzugefügt"],
        ["D"] = ["smazáno", "deleted", "zmazané", "gelöscht"],
        ["R"] = ["přejmenováno", "renamed", "premenované", "umbenannt"],
        ["C"] = ["zkopírováno", "copied", "skopírované", "kopiert"],
        ["T"] = ["změněn typ", "type changed", "zmenený typ", "Typ geändert"],
        ["U"] = ["konflikt", "conflict", "konflikt", "Konflikt"],
        ["?"] = ["nesledováno", "untracked", "nesledované", "nicht verfolgt"],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }

    /// <summary>The description of a status entry: what is staged and what is changed in the tree, in words.</summary>
    public static string Describe(string language, StatusEntry e)
    {
        string Word(char c) => Table.ContainsKey(c.ToString()) ? Get(language, c.ToString()) : c.ToString();
        if (e.Index == '?') return Word('?');
        if (e.Index == 'U' || e.Tree == 'U' || (e.Index, e.Tree) is ('A', 'A') or ('D', 'D')) return Word('U');
        var parts = new List<string>();
        if (e.Index != ' ') parts.Add(Get(language, "staged:") + ": " + Word(e.Index));
        if (e.Tree != ' ') parts.Add(Get(language, "changed:") + ": " + Word(e.Tree));
        return string.Join("; ", parts);
    }
}

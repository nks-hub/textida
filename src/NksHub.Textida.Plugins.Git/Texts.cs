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
        ["unstaged"] = ["Úsek {hunk} souboru {file} je vrácen z indexu.", "Hunk {hunk} of {file} is removed from the index.", "Úsek {hunk} súboru {file} je vrátený z indexu.", "Abschnitt {hunk} von {file} wurde aus dem Index genommen."],
        ["noStagedHunk"] = ["Kurzor nestojí v žádné změně, která je v indexu.", "The caret is not in a change that is in the index.", "Kurzor nestojí v žiadnej zmene, ktorá je v indexe.", "Der Cursor steht in keiner Änderung, die im Index liegt."],
        ["noConflictMarkers"] = ["Kurzor nestojí v konfliktním bloku (mezi řádky <<<<<<< a >>>>>>>).", "The caret is not in a conflict block (between the <<<<<<< and >>>>>>> lines).", "Kurzor nestojí v konfliktnom bloku (medzi riadkami <<<<<<< a >>>>>>>).", "Der Cursor steht in keinem Konfliktblock (zwischen den Zeilen <<<<<<< und >>>>>>>)."],
        ["resolved"] = ["Konflikt vyřešen: {side}.", "Conflict resolved: {side}.", "Konflikt vyriešený: {side}.", "Konflikt gelöst: {side}."],
        ["ours"] = ["naše změny", "ours", "naše zmeny", "unsere Änderungen"],
        ["theirs"] = ["jejich změny", "theirs", "ich zmeny", "ihre Änderungen"],
        ["both"] = ["obě strany", "both sides", "obe strany", "beide Seiten"],
        ["notInConflict"] = ["Soubor {file} nemá v indexu nevyřešený konflikt.", "{file} has no unresolved conflict in the index.", "Súbor {file} nemá v indexe nevyriešený konflikt.", "{file} hat im Index keinen ungelösten Konflikt."],
        ["markersLeft"] = ["V souboru zůstaly značky konfliktu (<<<<<<<, =======, >>>>>>>). Vyřešte všechny bloky a pak soubor označte jako vyřešený.", "Conflict markers are left in the file (<<<<<<<, =======, >>>>>>>). Resolve every block, then mark the file resolved.", "V súbore zostali značky konfliktu (<<<<<<<, =======, >>>>>>>). Vyriešte všetky bloky a potom súbor označte ako vyriešený.", "In der Datei sind noch Konfliktmarkierungen (<<<<<<<, =======, >>>>>>>). Lösen Sie alle Blöcke und markieren Sie die Datei dann als gelöst."],
        ["unsaved"] = ["Soubor {file} má neuložené změny. Uložte ho a označte jako vyřešený znovu.", "{file} has unsaved changes. Save it and mark it resolved again.", "Súbor {file} má neuložené zmeny. Uložte ho a označte ako vyriešený znova.", "{file} hat ungespeicherte Änderungen. Speichern Sie die Datei und markieren Sie sie erneut als gelöst."],
        ["markedResolved"] = ["Soubor {file} je označen jako vyřešený a přidán do indexu.", "{file} is marked resolved and added to the index.", "Súbor {file} je označený ako vyriešený a pridaný do indexu.", "{file} ist als gelöst markiert und im Index vorgemerkt."],
        ["statusTip"] = ["Git, větev {branch}: v indexu {staged}, změněno {changed}, nesledováno {untracked}, konflikty {conflicts}", "Git, branch {branch}: staged {staged}, changed {changed}, untracked {untracked}, conflicts {conflicts}", "Git, vetva {branch}: v indexe {staged}, zmenené {changed}, nesledované {untracked}, konflikty {conflicts}", "Git, Branch {branch}: vorgemerkt {staged}, geändert {changed}, nicht verfolgt {untracked}, Konflikte {conflicts}"],
        ["noMarkers"] = ["Soubor je v konfliktu bez značek ({kind}), nemá co brát ze stran. Ponechte ho příkazem „Git: označit soubor jako vyřešený“, nebo ho smažte příkazem „Git: konflikt, smazat soubor“.", "The file is in a conflict without markers ({kind}), so there is no side to take. Keep it with \"Git: Mark File Resolved\" or delete it with \"Git: Conflict, Delete File\".", "Súbor je v konflikte bez značiek ({kind}), nie je z čoho vyberať. Ponechajte ho príkazom „Git: označiť súbor ako vyriešený“, alebo ho zmažte príkazom „Git: konflikt, zmazať súbor“.", "Die Datei hat einen Konflikt ohne Markierungen ({kind}), es gibt keine Seite zum Übernehmen. Behalten Sie sie mit „Git: Datei als gelöst markieren“ oder löschen Sie sie mit „Git: Konflikt, Datei löschen“."],
        ["conflictDeleted"] = ["Soubor {file} je smazán a konflikt je vyřešen.", "{file} is deleted and the conflict is resolved.", "Súbor {file} je zmazaný a konflikt je vyriešený.", "{file} wurde gelöscht und der Konflikt ist gelöst."],
        ["actionStage"] = ["Připravit úsek", "Stage hunk", "Pripraviť úsek", "Abschnitt vormerken"],
        ["actionUnstage"] = ["Vrátit úsek z indexu", "Unstage hunk", "Vrátiť úsek z indexu", "Abschnitt aus dem Index nehmen"],
        // kinds of conflict, by the two letters of git status
        ["UU"] = ["konflikt: změněno oběma", "conflict: both modified", "konflikt: zmenené oboma", "Konflikt: beidseitig geändert"],
        ["AA"] = ["konflikt: přidáno oběma", "conflict: added by both", "konflikt: pridané oboma", "Konflikt: beidseitig hinzugefügt"],
        ["DD"] = ["konflikt: smazáno oběma", "conflict: deleted by both", "konflikt: zmazané oboma", "Konflikt: beidseitig gelöscht"],
        ["AU"] = ["konflikt: přidáno námi", "conflict: added by us", "konflikt: pridané nami", "Konflikt: von uns hinzugefügt"],
        ["UA"] = ["konflikt: přidáno jimi", "conflict: added by them", "konflikt: pridané nimi", "Konflikt: von ihnen hinzugefügt"],
        ["DU"] = ["konflikt: smazáno námi", "conflict: deleted by us", "konflikt: zmazané nami", "Konflikt: von uns gelöscht"],
        ["UD"] = ["konflikt: smazáno jimi", "conflict: deleted by them", "konflikt: zmazané nimi", "Konflikt: von ihnen gelöscht"],
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
        if (StatusCounts.IsConflict(e)) return Table.ContainsKey(new string([e.Index, e.Tree])) ? Get(language, new string([e.Index, e.Tree])) : Word('U');
        var parts = new List<string>();
        if (e.Index != ' ') parts.Add(Get(language, "staged:") + ": " + Word(e.Index));
        if (e.Tree != ' ') parts.Add(Get(language, "changed:") + ": " + Word(e.Tree));
        return string.Join("; ", parts);
    }
}

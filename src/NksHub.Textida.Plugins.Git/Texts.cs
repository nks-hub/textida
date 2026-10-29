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
        ["bothDeletedTitle"] = ["Smazáno oběma stranami", "Deleted by both sides", "Zmazané oboma stranami", "Von beiden Seiten gelöscht"],
        ["bothDeletedMessage"] = ["Soubor {file} smazaly obě strany, takže tu není co ponechat. Odebrat ho z indexu a konflikt tím vyřešit? (git rm)", "Both sides deleted {file}, so there is nothing left to keep. Remove it from the index and resolve the conflict? (git rm)", "Súbor {file} zmazali obe strany, takže tu nie je čo ponechať. Odobrať ho z indexu a konflikt tým vyriešiť? (git rm)", "Beide Seiten haben {file} gelöscht, es bleibt also nichts zu behalten. Aus dem Index entfernen und den Konflikt so lösen? (git rm)"],
        ["missingFile"] = ["Soubor {file} není v pracovním stromu, nemá se co otevřít.", "{file} is not in the working tree, so there is nothing to open.", "Súbor {file} nie je v pracovnom strome, nie je čo otvoriť.", "{file} liegt nicht im Arbeitsbaum, es gibt nichts zu öffnen."],
        ["actionStage"] = ["Připravit úsek", "Stage hunk", "Pripraviť úsek", "Abschnitt vormerken"],
        ["actionUnstage"] = ["Vrátit úsek z indexu", "Unstage hunk", "Vrátiť úsek z indexu", "Abschnitt aus dem Index nehmen"],
        ["branchSwitched"] = ["Přepnuto na větev {branch}.", "Switched to branch {branch}.", "Prepnuté na vetvu {branch}.", "Zu Branch {branch} gewechselt."],
        ["branchCreated"] = ["Větev {branch} je vytvořena a přepnuto na ni.", "Branch {branch} is created and checked out.", "Vetva {branch} je vytvorená a prepnuté na ňu.", "Branch {branch} wurde angelegt und ausgecheckt."],
        ["branchDeleted"] = ["Větev {branch} je smazána.", "Branch {branch} is deleted.", "Vetva {branch} je zmazaná.", "Branch {branch} wurde gelöscht."],
        ["branchPickTitle"] = ["Přepnout větev", "Switch Branch", "Prepnúť vetvu", "Branch wechseln"],
        ["branchPickPlaceholder"] = ["Vyberte větev", "Choose a branch", "Vyberte vetvu", "Branch auswählen"],
        ["branchCurrent"] = ["aktuální", "current", "aktuálna", "aktuell"],
        ["branchRemote"] = ["větev vzdáleného repozitáře, vytvoří se místní kopie", "remote branch, a local branch is made", "vetva vzdialeného repozitára, vytvorí sa miestna kópia", "Remote-Branch, ein lokaler Branch wird angelegt"],
        ["branchNone"] = ["Repozitář nemá žádnou jinou větev.", "The repository has no other branch.", "Repozitár nemá žiadnu inú vetvu.", "Das Repository hat keinen anderen Branch."],
        ["noBranch"] = ["Repozitář je v odpojeném stavu (detached HEAD), větev tu není.", "The repository is on a detached HEAD, so there is no branch.", "Repozitár je v odpojenom stave (detached HEAD), vetva tu nie je.", "Das Repository steht auf einem losgelösten HEAD, es gibt keinen Branch."],
        ["dirtyTitle"] = ["Neuložené změny", "Uncommitted changes", "Nezapísané zmeny", "Nicht committete Änderungen"],
        ["dirtyMessage"] = ["Počet souborů se změnami v pracovním stromu: {count}. Přepnutí je ponese s sebou, nebo selže, kde se s cílovou větví překrývají. Přepnout?", "Files with changes in the working tree: {count}. Switching carries them over, or fails where they overlap the target branch. Switch?", "Počet súborov so zmenami v pracovnom strome: {count}. Prepnutie ich ponesie so sebou, alebo zlyhá, kde sa s cieľovou vetvou prekrývajú. Prepnúť?", "Dateien mit Änderungen im Arbeitsbaum: {count}. Beim Wechsel werden sie mitgenommen, oder er schlägt fehl, wo sie sich mit dem Ziel-Branch überschneiden. Wechseln?"],
        ["newBranchTitle"] = ["Nová větev", "New Branch", "Nová vetva", "Neuer Branch"],
        ["newBranchLabel"] = ["Název nové větve (vychází z aktuálního commitu):", "Name of the new branch (made from the current commit):", "Názov novej vetvy (vychádza z aktuálneho commitu):", "Name des neuen Branches (vom aktuellen Commit aus):"],
        ["deletePickTitle"] = ["Smazat větev", "Delete Branch", "Zmazať vetvu", "Branch löschen"],
        ["deleteUnmergedTitle"] = ["Větev není sloučená", "Branch is not merged", "Vetva nie je zlúčená", "Branch nicht zusammengeführt"],
        ["deleteUnmergedMessage"] = ["Větev {branch} není celá sloučená. Přesto ji smazat? Commity, které nejsou jinde, by se ztratily.", "Branch {branch} is not fully merged. Delete it anyway? Commits that are nowhere else would be lost.", "Vetva {branch} nie je celá zlúčená. Napriek tomu ju zmazať? Commity, ktoré nie sú inde, by sa stratili.", "Branch {branch} ist nicht vollständig zusammengeführt. Trotzdem löschen? Commits, die sonst nirgends sind, gingen verloren."],
        ["stashTitle"] = ["Odložit změny", "Stash Changes", "Odložiť zmeny", "Änderungen zurückstellen"],
        ["stashLabel"] = ["Popis odložených změn (nepovinný):", "Description of the stash (optional):", "Popis odložených zmien (nepovinný):", "Beschreibung des Stashs (optional):"],
        ["stashSaved"] = ["Změny jsou odložené (stash).", "The changes are stashed.", "Zmeny sú odložené (stash).", "Die Änderungen sind zurückgestellt (Stash)."],
        ["stashNothing"] = ["V pracovním stromu nejsou žádné změny k odložení.", "There are no changes in the working tree to stash.", "V pracovnom strome nie sú žiadne zmeny na odloženie.", "Im Arbeitsbaum gibt es keine Änderungen zum Zurückstellen."],
        ["stashNone"] = ["Žádné odložené změny nejsou.", "There are no stashes.", "Žiadne odložené zmeny nie sú.", "Es gibt keine Stashes."],
        ["stashApplyTitle"] = ["Použít odložené změny", "Apply a Stash", "Použiť odložené zmeny", "Stash anwenden"],
        ["stashDropTitle"] = ["Zahodit odložené změny", "Drop a Stash", "Zahodiť odložené zmeny", "Stash verwerfen"],
        ["stashPickPlaceholder"] = ["Vyberte odložené změny", "Choose a stash", "Vyberte odložené zmeny", "Stash auswählen"],
        ["stashApplied"] = ["Odložené změny {name} jsou použité. Zůstávají v seznamu, dokud je nezahodíte.", "Stash {name} is applied. It stays in the list until you drop it.", "Odložené zmeny {name} sú použité. Zostávajú v zozname, kým ich nezahodíte.", "Stash {name} wurde angewendet. Er bleibt in der Liste, bis Sie ihn verwerfen."],
        ["stashConflict"] = ["Použití odložených změn {name} skončilo konflikty. Vyřešte je v panelu stavu.", "Applying stash {name} ended in conflicts. Resolve them in the status panel.", "Použitie odložených zmien {name} skončilo konfliktmi. Vyriešte ich v paneli stavu.", "Das Anwenden von Stash {name} endete mit Konflikten. Lösen Sie sie im Statuspanel."],
        ["stashDropConfirmMessage"] = ["Zahodit {name} ({description})? Nejde to vrátit.", "Drop {name} ({description})? This cannot be undone.", "Zahodiť {name} ({description})? Nedá sa to vrátiť.", "{name} ({description}) verwerfen? Das lässt sich nicht rückgängig machen."],
        ["stashDropped"] = ["Odložené změny {name} jsou zahozené.", "Stash {name} is dropped.", "Odložené zmeny {name} sú zahodené.", "Stash {name} wurde verworfen."],
        ["logPanelFile"] = ["Git: historie souboru {name}", "Git: history of {name}", "Git: história súboru {name}", "Git: Verlauf von {name}"],
        ["logPanelRepo"] = ["Git: historie repozitáře {name}", "Git: history of the repository {name}", "Git: história repozitára {name}", "Git: Verlauf des Repositorys {name}"],
        ["logNone"] = ["Nenalezen žádný commit.", "No commit found.", "Nenájdený žiadny commit.", "Kein Commit gefunden."],
        ["logFilesTitle"] = ["Soubory změněné v {commit}", "Files changed in {commit}", "Súbory zmenené v {commit}", "In {commit} geänderte Dateien"],
        ["logFilesPlaceholder"] = ["Vyberte soubor k porovnání", "Choose a file to compare", "Vyberte súbor na porovnanie", "Datei zum Vergleichen auswählen"],
        ["logNoFile"] = ["Commit {commit} nezměnil žádný soubor.", "Commit {commit} changed no file.", "Commit {commit} nezmenil žiadny súbor.", "Commit {commit} hat keine Datei geändert."],
        ["logBefore"] = ["{file} (před {commit})", "{file} (before {commit})", "{file} (pred {commit})", "{file} (vor {commit})"],
        ["logAfter"] = ["{file} ({commit})", "{file} ({commit})", "{file} ({commit})", "{file} ({commit})"],
        ["taskFetch"] = ["Git: stahuji změny z {remote}", "Git: fetching from {remote}", "Git: sťahujem zmeny z {remote}", "Git: Abruf von {remote}"],
        ["taskPull"] = ["Git: aktualizuji větev {branch}", "Git: updating branch {branch}", "Git: aktualizujem vetvu {branch}", "Git: Branch {branch} wird aktualisiert"],
        ["taskPush"] = ["Git: odesílám větev {branch}", "Git: pushing branch {branch}", "Git: odosielam vetvu {branch}", "Git: Branch {branch} wird gesendet"],
        ["fetchDone"] = ["Změny z {remote} jsou stažené.", "Fetched from {remote}.", "Zmeny z {remote} sú stiahnuté.", "Von {remote} abgerufen."],
        ["pullDone"] = ["Větev {branch} je aktualizovaná.", "Branch {branch} is updated.", "Vetva {branch} je aktualizovaná.", "Branch {branch} ist aktualisiert."],
        ["pullUpToDate"] = ["Větev {branch} je aktuální, není co stáhnout.", "Branch {branch} is up to date.", "Vetva {branch} je aktuálna, nie je čo stiahnuť.", "Branch {branch} ist auf dem neuesten Stand."],
        ["pullConflicts"] = ["Aktualizace se zastavila na konfliktech (soubory: {count}). Vyřešte je v panelu stavu, označte soubory jako vyřešené a udělejte commit.", "The update stopped at conflicts (files: {count}). Resolve them in the status panel, mark the files resolved and commit.", "Aktualizácia sa zastavila na konfliktoch (súbory: {count}). Vyriešte ich v paneli stavu, označte súbory ako vyriešené a urobte commit.", "Die Aktualisierung wurde bei Konflikten angehalten (Dateien: {count}). Lösen Sie sie im Statuspanel, markieren Sie die Dateien als gelöst und committen Sie."],
        ["pullRebaseStopped"] = ["Rebase se zastavil na konfliktu. Vyřešte konflikty a rebase dokončete (git rebase --continue) nebo zrušte (git rebase --abort).", "The rebase stopped at a conflict. Resolve the conflicts, then finish the rebase (git rebase --continue) or cancel it (git rebase --abort).", "Rebase sa zastavil na konflikte. Vyriešte konflikty a rebase dokončite (git rebase --continue) alebo zrušte (git rebase --abort).", "Der Rebase wurde bei einem Konflikt angehalten. Lösen Sie die Konflikte und beenden Sie den Rebase (git rebase --continue) oder brechen Sie ihn ab (git rebase --abort)."],
        ["pullModeTitle"] = ["Aktualizovat větev", "Update Branch", "Aktualizovať vetvu", "Branch aktualisieren"],
        ["pullModePlaceholder"] = ["Jak spojit stažené změny s vaší větví?", "How to combine the new commits with your branch?", "Ako spojiť stiahnuté zmeny s vašou vetvou?", "Wie sollen die neuen Commits mit Ihrem Branch zusammengeführt werden?"],
        ["pullModeMerge"] = ["Sloučit (merge)", "Merge", "Zlúčiť (merge)", "Zusammenführen (Merge)"],
        ["pullModeMergeDetail"] = ["Vznikne slučovací commit, pokud se větve rozešly.", "Makes a merge commit when the branches have diverged.", "Vznikne zlučovací commit, ak sa vetvy rozišli.", "Erzeugt einen Merge-Commit, wenn die Branches auseinandergelaufen sind."],
        ["pullModeRebase"] = ["Přeskládat (rebase)", "Rebase", "Preskladať (rebase)", "Rebase"],
        ["pullModeRebaseDetail"] = ["Vaše commity se přehrají nad staženými; změny v pracovním stromu se na chvíli odloží.", "Your commits are replayed on top of the new ones; changes in the working tree are set aside for a moment.", "Vaše commity sa prehrajú nad stiahnutými; zmeny v pracovnom strome sa na chvíľu odložia.", "Ihre Commits werden über die neuen gespielt; Änderungen im Arbeitsbaum werden kurz beiseitegelegt."],
        ["pullNoUpstream"] = ["Větev {branch} nesleduje žádnou větev vzdáleného repozitáře. Nejdřív ji odešlete (Git: odeslat).", "Branch {branch} tracks no remote branch. Push it first (Git: Push).", "Vetva {branch} nesleduje žiadnu vetvu vzdialeného repozitára. Najprv ju odošlite (Git: odoslať).", "Branch {branch} verfolgt keinen Remote-Branch. Senden Sie ihn zuerst (Git: Push)."],
        ["noRemote"] = ["Repozitář nemá žádný vzdálený repozitář (remote).", "The repository has no remote.", "Repozitár nemá žiadny vzdialený repozitár (remote).", "Das Repository hat keinen Remote."],
        ["pushNothing"] = ["Nic k odeslání: větev {branch} je na úrovni {upstream}.", "Nothing to push: {branch} is level with {upstream}.", "Nič na odoslanie: vetva {branch} je na úrovni {upstream}.", "Nichts zu senden: {branch} ist auf dem Stand von {upstream}."],
        ["pushConfirmTitle"] = ["Odeslat větev {branch}", "Push Branch {branch}", "Odoslať vetvu {branch}", "Branch {branch} senden"],
        ["pushTarget"] = ["Do {target}:", "To {target}:", "Do {target}:", "Nach {target}:"],
        ["pushNewBranch"] = ["nová větev v {remote}", "a new branch on {remote}", "nová vetva v {remote}", "neuer Branch auf {remote}"],
        ["pushAlsoAhead"] = ["Další větve s neodeslanými commity: {names}. Odesílá se jen větev {branch}.", "Other branches with unpushed commits: {names}. Only {branch} is pushed.", "Ďalšie vetvy s neodoslanými commitmi: {names}. Odosiela sa len vetva {branch}.", "Weitere Branches mit ungesendeten Commits: {names}. Nur {branch} wird gesendet."],
        ["pushDone"] = ["Větev {branch} je odeslána do {target}.", "Branch {branch} is pushed to {target}.", "Vetva {branch} je odoslaná do {target}.", "Branch {branch} wurde nach {target} gesendet."],
        ["pushRejected"] = ["Vzdálený repozitář odmítl odeslání: má commity, které nemáte. Nejdřív větev aktualizujte (Git: aktualizovat) a odešlete znovu.", "The remote rejected the push: it has commits that you do not have. Update the branch first (Git: Pull), then push again.", "Vzdialený repozitár odmietol odoslanie: má commity, ktoré nemáte. Najprv vetvu aktualizujte (Git: aktualizovať) a odošlite znova.", "Der Remote hat das Senden abgelehnt: Er hat Commits, die Sie nicht haben. Aktualisieren Sie den Branch zuerst (Git: Pull) und senden Sie erneut."],
        ["authFailed"] = ["Git se nemohl přihlásit ke vzdálenému repozitáři. Hesla spravuje pomocník pro přihlašovací údaje Gitu, ne editor: nastavte ho (git config credential.helper) a zkuste to znovu.", "Git could not sign in to the remote. Passwords are kept by the credential helper of Git, not by the editor: set one up (git config credential.helper) and try again.", "Git sa nemohol prihlásiť k vzdialenému repozitáru. Heslá spravuje pomocník pre prihlasovacie údaje Gitu, nie editor: nastavte ho (git config credential.helper) a skúste to znova.", "Git konnte sich nicht am Remote anmelden. Passwörter verwaltet der Credential-Helper von Git, nicht der Editor: Richten Sie einen ein (git config credential.helper) und versuchen Sie es erneut."],
        ["networkFailed"] = ["Spojení se vzdáleným repozitářem selhalo: {reason}", "The connection to the remote failed: {reason}", "Spojenie so vzdialeným repozitárom zlyhalo: {reason}", "Die Verbindung zum Remote ist fehlgeschlagen: {reason}"],
        ["timeout"] = ["Vzdálený repozitář neodpověděl včas.", "The remote did not answer in time.", "Vzdialený repozitár neodpovedal včas.", "Der Remote hat nicht rechtzeitig geantwortet."],
        ["cancelled"] = ["Zrušeno.", "Cancelled.", "Zrušené.", "Abgebrochen."],
        ["committed"] = ["Commit {sha}: {subject}", "Committed {sha}: {subject}", "Commit {sha}: {subject}", "Commit {sha}: {subject}"],
        ["amended"] = ["Commit {sha} upraven: {subject}", "Amended {sha}: {subject}", "Commit {sha} upravený: {subject}", "Commit {sha} geändert: {subject}"],
        ["commitNothing"] = ["V indexu není nic k odevzdání. Nejdřív označte soubory nebo změny.", "Nothing is staged to commit. Tick files or changes first.", "V indexe nie je nič na odovzdanie. Najprv označte súbory alebo zmeny.", "Nichts ist zum Committen vorgemerkt. Haken Sie zuerst Dateien oder Änderungen an."],
        ["commitNoMessage"] = ["Napište zprávu commitu.", "Write a commit message.", "Napíšte správu commitu.", "Schreiben Sie eine Commit-Nachricht."],
        ["commitIdentity"] = ["Git neví, kdo jste. Nastavte user.name a user.email (git config) a commit zopakujte.", "Git does not know who you are. Set user.name and user.email (git config) and commit again.", "Git nevie, kto ste. Nastavte user.name a user.email (git config) a commit zopakujte.", "Git weiß nicht, wer Sie sind. Setzen Sie user.name und user.email (git config) und committen Sie erneut."],
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

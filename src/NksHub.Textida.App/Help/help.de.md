## [start] Erste Schritte
commands: nks.help.contents
Diese Hilfe ist Teil des Programms und braucht keine Internetverbindung. Sie öffnen sie mit F1 oder über das Menü Hilfe. Drücken Sie F1 bei einem markierten Befehl in der Befehlspalette oder in einem geöffneten Bereich (Suche, Dateien, Dokumente, Einstellungen, Regex-Tester), öffnet sich gleich dessen Thema.
### In der Hilfe suchen
- Links steht die Themenliste. Tippen Sie ein Wort in das Feld darüber; es bleiben nur Themen, die alle eingegebenen Wörter enthalten. Groß- und Kleinschreibung und Akzente spielen keine Rolle.
- Die Pfeiltasten nach oben und unten im Feld wechseln die Themen, Esc schließt die Hilfe.
### Tastenkürzel
- In allen Anleitungen steht Strg; auf macOS gilt stattdessen ⌘ (Cmd). Die genannten Kürzel sind die Standardkürzel. Eigene Kürzel stehen in den Einstellungen, und das Verhaltensprofil PSPad ändert einige davon (Thema Verhaltensprofil).
- Jeden Befehl finden Sie in der Befehlspalette: Strg+Umschalt+P und einen Teil des Namens tippen.
### Was diese Hilfe abdeckt
- Sie beschreibt nur Funktionen, die diese Version wirklich hat. Was diese Version nicht kann, steht im Thema Was diese Version nicht kann.
- Besondere Themen: Wiederherstellung nach Absturz, Große Dateien (Seitenmodus), Fehlermeldungen.

## [view.palette] Befehlspalette
commands: nks.view.commandPalette
Die Palette ist eine durchsuchbare Liste aller Befehle des Programms.
1. Drücken Sie Strg+Umschalt+P.
2. Tippen Sie einen Teil des Befehlsnamens, zum Beispiel speich.
3. Wählen Sie mit den Pfeiltasten eine Zeile und drücken Sie die Eingabetaste. Esc schließt die Palette.
- Bei einem Befehl steht sein Kürzel. Ein Befehl, der gerade nicht benutzt werden kann (zum Beispiel Speichern ohne geöffnetes Dokument), ist abgeblendet und nennt den Grund.
- Beginnen Sie mit /, durchsucht die Palette die Einstellungen, und die Eingabetaste öffnet die Einstellungen an der gefundenen Stelle.
- Beginnen Sie mit =, rechnet die Palette: =1+2*3 zeigt 7, und die Eingabetaste kopiert das Ergebnis. Einen ungültigen Ausdruck, eine Division durch null oder eine zu große Zahl meldet die Palette neben dem Ausdruck.
- F1 beim markierten Befehl öffnet sein Hilfethema.

## [help.contents] Hilfe
commands: nks.help.contents
Öffnet dieses Hilfefenster.
1. Drücken Sie F1 oder wählen Sie Hilfe und dann Hilfe.
2. Das Fenster öffnet sich bei dem Thema, das zu Ihrer Situation passt: bei einem markierten Befehl in der Palette bei dessen Thema, in der Suchleiste, im Dateipanel oder in der Dokumentliste beim Thema dieses Bereichs, sonst auf der Startseite.
3. F1 in den Einstellungen öffnet das Thema über die Einstellungen, F1 im Regex-Tester das Thema über den Tester.
- Die Hilfe ist ein einziges Fenster. Ein weiteres F1 schaltet es nur auf ein anderes Thema um.

## [help.about] Info
commands: nks.help.about
Zeigt den Programmnamen und die Versionsnummer.
1. Wählen Sie Hilfe und dann Info.
2. Schließen Sie das Fenster mit der Schaltfläche Schließen oder mit Esc.
- Dieselbe Versionsnummer steht oben in jedem Hilfethema.

## [file.new] Neues Dokument
commands: nks.file.new
1. Drücken Sie Strg+N oder wählen Sie Datei und dann Neu.
2. Ein Tab Unbenannt 1 öffnet sich (die nächsten bekommen 2, 3 und so weiter), und der Cursor steht darin.
- Die Codierung eines neuen Dokuments bestimmt die Einstellung Standardcodierung, die Zeilenenden die Einstellung Zeilenende.
- Der Text wird sofort ins Wiederherstellungsjournal geschrieben und geht deshalb auch bei einem Absturz nicht verloren (Thema Wiederherstellung nach Absturz). Namen und Ort bekommt das Dokument beim ersten Speichern.

## [file.open] Eine Datei öffnen
commands: nks.file.open
1. Drücken Sie Strg+O oder wählen Sie Datei und dann Öffnen.
2. Wählen Sie im Systemfenster eine oder mehrere Dateien und bestätigen Sie.
3. Jede Datei öffnet sich in einem eigenen Tab. Eine Datei, die schon offen ist, wird nur angezeigt.
- Dateien lassen sich auch ins Programmfenster ziehen.
- Die Codierung wird automatisch erkannt. Ist es nur eine Schätzung, zeigt die Statusleiste den Prozentwert der Sicherheit; ein Klick auf die Codierung ändert sie (Thema Codierung).
- Eine Datei über der Grenze für große Dateien (standardmäßig 6 MiB) öffnet sich im Seitenmodus mit eingeschränkter Bearbeitung (Thema Große Dateien).
- Eine Datei, die sich nicht lesen lässt, wird nicht geöffnet, und die Statusleiste nennt den Grund (Thema Fehlermeldungen).

## [file.openFolder] Einen Ordner öffnen
commands: nks.file.openFolder
1. Drücken Sie Strg+K und dann Strg+O oder wählen Sie Datei und dann Ordner öffnen.
2. Wählen Sie im Systemfenster den Ordner.
3. Links erscheint das Panel Dateien mit dem Ordnerbaum. Die Dateisuche nach Namen (Strg+P) und In Dateien suchen arbeiten dann über diesem Ordner.
- Lässt sich der Ordner nicht öffnen, nennt die Statusleiste den Grund.

## [file.save] Speichern
commands: nks.file.save, nks.file.saveAll
1. Strg+S speichert den aktiven Tab, Strg+Alt+S speichert alle Tabs mit ungespeicherten Änderungen.
2. Ein Dokument ohne Datei fragt beim Speichern nach Name und Codierung, genau wie Speichern unter.
3. Nach dem Speichern zeigt die Statusleiste Gespeichert.
- Bevor eine Datei überschrieben wird, legt das Programm eine Sicherung der alten Fassung an. Misslingt die Sicherung, bleibt die Datei unverändert, und die Statusleiste sagt es. Misslingt nur die Sicherung nach dem Schreiben, steht dort Gespeichert, aber die Sicherung ist fehlgeschlagen.
- Hat sich die Datei inzwischen geändert oder ist verschwunden, fragt das Programm: Überschreiben, Vergleichen oder Abbrechen. Ohne Ihre Zustimmung wird nichts überschrieben (Thema Datei von außen geändert).
- Enthält der Text Zeichen, die die Codierung der Datei nicht darstellen kann, listet das Programm sie auf und bietet Ersetzen und speichern (die Zeichen werden zu einem Fragezeichen) oder Abbrechen an.
- Die Einstellungen Leerraum am Zeilenende entfernen, Zeilenende am Dateiende einfügen und Automatisch speichern betreffen das Speichern.

## [file.saveAs] Speichern unter
commands: nks.file.saveAs
1. Drücken Sie Strg+Umschalt+S oder wählen Sie Datei und dann Speichern unter.
2. Tippen Sie im Feld Datei den Pfad oder wählen Sie ihn mit Durchsuchen.
3. Wählen Sie im Feld Codierung eine Codierung. Eine Codierung, die einige Zeichen verlieren würde, ist als verlustbehaftet mit der Zeichenzahl gekennzeichnet.
4. Bestätigen Sie mit Speichern.
- Existiert die Zieldatei, fragt das Programm nach dem Überschreiben.
- Der Tab gehört danach zur neuen Datei.
- Eine große Datei im Seitenmodus lässt sich nur in ihrer eigenen Codierung speichern.

## [file.close] Einen Tab schließen
commands: nks.file.close
1. Drücken Sie Strg+W oder wählen Sie Datei und dann Schließen.
2. Hat das Dokument ungespeicherte Änderungen, fragt das Programm: Speichern, Nicht speichern, Im Journal behalten oder Abbrechen. Im Journal behalten wird nur angeboten, wenn die Wiederherstellung nach Absturz funktioniert; die Änderungen warten dann im Journal und kommen beim nächsten Start zurück.
- Sind zwei Ansichten desselben Dokuments offen (geteilte Ansicht), fragt das Schließen einer Ansicht nichts.
- Im Verhaltensprofil PSPad schaltet Strg+W den Zeilenumbruch um; den Tab schließen Sie dann über das Menü oder mit dem Kreuz am Tab.

## [file.reload] Vom Datenträger neu laden
commands: nks.file.reload
1. Wählen Sie Datei und dann Vom Datenträger neu laden oder suchen Sie den Befehl in der Palette.
2. Hat der Tab ungespeicherte Änderungen, fragt das Programm; die Schaltfläche Änderungen verwerfen und neu laden verwirft sie.
3. Die Statusleiste zeigt Vom Datenträger neu geladen.
- Der Befehl funktioniert nur bei einem Tab, der eine Datei auf dem Datenträger hat.
- Dasselbe bietet die Leiste über dem Dokument an, wenn sich eine Datei von außen ändert (Thema Datei von außen geändert).

## [file.compare] Mit Datenträger vergleichen
commands: nks.file.compareWithDisk
Der Befehl startet Ihr eigenes Vergleichsprogramm: links die Datei auf dem Datenträger, rechts der Inhalt des Tabs.
1. Tragen Sie einmal in den Einstellungen das Externe Vergleichsprogramm (den Pfad zu einem Programm) und bei Bedarf die Argumente des Vergleichsprogramms ein. In der Vorlage steht {left} für die Datei auf dem Datenträger und {right} für den Inhalt des Tabs.
2. Wählen Sie Datei und dann Mit Datenträger vergleichen.
- Ist kein Programm eingestellt, erscheint eine Leiste: Kein externes Vergleichsprogramm eingestellt, mit der Schaltfläche Einstellungen öffnen.
- Ein Tab ohne Datei auf dem Datenträger meldet: Der Tab hat keine Datei auf dem Datenträger, mit der verglichen werden kann.
- Startet das Programm nicht, meldet die Statusleiste Das Vergleichsprogramm ist nicht gestartet mit dem Grund.
- Ein eigenes Vergleichsfenster hat das Programm nicht; es verwendet das von Ihnen eingestellte Programm.

## [file.openPath] Pfad am Cursor öffnen
commands: nks.file.openPathAtCaret
1. Setzen Sie den Cursor auf einen Dateipfad im Text, zum Beispiel src/a.cs:12.
2. Wählen Sie Datei und dann Pfad am Cursor öffnen oder suchen Sie den Befehl in der Palette. Ein Kürzel hat er nicht.
3. Die Datei öffnet sich; eine Zeile (und Spalte) nach dem Doppelpunkt wird verwendet.
- Ein relativer Pfad wird im Ordner der aktuellen Datei und im geöffneten Ordner gesucht. Ein Pfad, der mit ~/ beginnt, zeigt in Ihren Benutzerordner.
- Zeigt der Pfad auf einen Ordner, wird er im Panel Dateien angezeigt.
- Kein Pfad am Cursor: Am Cursor steht kein Dateipfad. Datei existiert nicht: Der Pfad wird mit dem Hinweis genannt, dass er nicht existiert.

## [app.settings] Einstellungen
commands: nks.app.openSettings
1. Drücken Sie Strg+Komma oder wählen Sie Einstellungen und dann Einstellungen.
2. Tippen Sie oben, was Sie suchen, zum Beispiel tab oder Kürzel. Durchsucht werden Einstellungen und Tastenkürzel.
3. Jeder Eintrag zeigt den gültigen Wert und woher er stammt (Standard, Benutzer, Arbeitsbereich, EditorConfig, Dokument).
4. Ändern Sie den Wert direkt in der Zeile; er wirkt sofort und wird in die Einstellungsdatei geschrieben. Auf Standard zurücksetzen stellt den ursprünglichen Wert wieder her.
- Ein unsinniger Wert wird abgelehnt, und die Zeile sagt, was daran nicht stimmt. Lässt sich die Einstellungsdatei nicht schreiben, steht der Grund über der Liste.
- Kürzelkonflikte werden bei den betroffenen Zeilen angezeigt.
- F1 in diesem Fenster öffnet dieses Thema.

## [app.quit] Beenden
commands: nks.app.quit
1. Drücken Sie Strg+Q oder wählen Sie Datei und dann Beenden.
2. Funktioniert die Wiederherstellung nach Absturz, fragt das Programm nichts: Ungespeicherter Text bleibt im Journal und kommt beim nächsten Start zurück.
3. Funktioniert sie nicht (das Journal lässt sich nicht schreiben), fragt das Programm bei jedem Dokument mit ungespeicherten Änderungen: Speichern, Nicht speichern, Abbrechen.
- Die Anordnung der Tabs wird beim Beenden gespeichert und beim nächsten Start wiederhergestellt.

## [edit.undo] Rückgängig und Wiederholen
commands: nks.edit.undo, nks.edit.redo
1. Strg+Z macht den letzten Schritt rückgängig, Strg+Y oder Strg+Umschalt+Z stellt ihn wieder her. Das Menü Bearbeiten hat dasselbe.
2. Ein Schritt ist eine zusammenhängende Eingabe oder ein Befehl (zum Beispiel Zeile löschen oder eine Codierungsumwandlung).
3. Nach einem Schritt springt der Cursor an die Stelle, an der die Änderung stattfand.
- Zwei Ansichten desselben Dokuments teilen sich den Verlauf.
- Rückgängig und Wiederholen funktionieren auch im Seitenmodus.
- Das Profil PSPad hat zusätzlich Alt+Rücktaste (Rückgängig) und Umschalt+Alt+Rücktaste (Wiederholen).

## [edit.clipboard] Ausschneiden, Kopieren, Einfügen
commands: nks.edit.cut, nks.edit.copy, nks.edit.paste
1. Markieren Sie Text; Strg+X schneidet ihn aus, Strg+C kopiert ihn, Strg+V fügt ihn an der Cursorposition oder über der Auswahl ein.
- Eingefügter Text übernimmt die Zeilenenden des Dokuments.
- Bei mehreren Cursorn werden die markierten Texte zeilenweise kopiert, und beim Einfügen bekommt jeder Cursor sein Stück.
- In einer Blockauswahl geht eine Zeile der Zwischenablage in jede Zeile des Blocks.
- Beim Einfügen in eine Zeichenkette einer Programmiersprache werden Anführungszeichen und Backslashes maskiert (Einstellung Beim Einfügen in Zeichenketten maskieren).
- Ausschneiden und Einfügen gehen nicht in einem schreibgeschützten Dokument und nicht im Seitenmodus (Thema Große Dateien).

## [edit.select] Auswählen
commands: nks.edit.selectAll, nks.edit.selectLine, nks.edit.expandSelection
1. Strg+A wählt den ganzen Text aus.
2. Strg+L wählt die Zeile samt Zeilenende aus. Ein weiteres Strg+L fügt die nächste Zeile hinzu.
3. Umschalt+Alt+Pfeil rechts erweitert die Auswahl: erst das Wort, dann den Inhalt der Klammern darum, dann die Klammern selbst, das nächste Klammerpaar, die ganzen Zeilen und zuletzt den ganzen Text.
- Bei mehreren Cursorn nimmt jeder Cursor seine eigene Zeile.

## [edit.lines] Arbeiten mit Zeilen
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines, nks.edit.toggleComment
Die Befehle betreffen die Zeile mit dem Cursor oder alle Zeilen der Auswahl. Jeder ist ein Schritt für Rückgängig.
- Zeile löschen: Strg+Umschalt+K.
- Zeile duplizieren: Strg+Umschalt+D fügt eine Kopie unter der Zeile ein.
- Zeile nach oben oder unten verschieben: Alt+Pfeil nach oben, Alt+Pfeil nach unten.
- Zeilen verbinden: Strg+Umschalt+J verbindet die Zeile mit der nächsten.
- Kommentar umschalten: Strg+/ kommentiert die Zeilen aus oder wieder ein. Es nimmt die Kommentarzeichen der Sprache an der Zeile, ein Skript in HTML bekommt also // und das HTML darum einen HTML-Kommentar.
- Alle stehen auch im Menü Bearbeiten.
- Sie gehen nicht in einem schreibgeschützten Dokument und nicht im Seitenmodus.
- Im Profil PSPad liegt Zeile löschen auf Strg+Y, Zeile duplizieren auf Umschalt+Alt+Pfeil nach unten, und die Alt+Pfeile verschieben keine Zeilen.

## [edit.indent] Einzug
commands: nks.edit.indent, nks.edit.outdent, nks.edit.insertIndent, nks.edit.tab
1. Strg+] vergrößert den Einzug der markierten Zeilen (oder der Zeile mit dem Cursor), Strg+[ verkleinert ihn.
2. Die Tabulatortaste rückt bei einer Auswahl über mehrere Zeilen alle ein, ohne eine solche Auswahl fügt sie einen Einzug an der Cursorposition ein. Umschalt+Tab verkleinert den Einzug.
- Der Einzug besteht aus so vielen Leerzeichen oder Tabulatoren, wie die Einstellungen Einzugsgröße und Mit Leerzeichen einrücken sagen. Bei einer geöffneten Datei schätzt das Programm den Einzug (Einstellung Einrückung erkennen), und eine Datei, für die .editorconfig gilt, folgt diesem. Den Einzug ändern Sie per Klick darauf in der Statusleiste.
- Der Befehl Einzug einfügen hat kein Kürzel; er ist das, was Tab ohne Auswahl über mehrere Zeilen tut.
- Im Profil PSPad liegen Einzug vergrößern und verkleinern auf Strg+Umschalt+I und Strg+Umschalt+U.

## [edit.newline] Neue Zeile
commands: nks.edit.newline
1. Die Eingabetaste beginnt eine neue Zeile mit dem Einzug der vorherigen, im Zeilenendformat des Dokuments.
2. Steht der Cursor in einem Kommentar, setzt die neue Zeile den Kommentar fort (Einstellung Kommentare fortsetzen).

## [edit.case] Groß- und Kleinschreibung
commands: nks.edit.upperCase, nks.edit.lowerCase
1. Markieren Sie Text.
2. Strg+K und dann Strg+U macht die Buchstaben groß, Strg+K und dann Strg+L klein.
- Ohne Auswahl sind die Befehle abgeblendet. Sie stehen auch im Menü Bearbeiten.

## [edit.clipboardHistory] Zwischenablage-Verlauf
commands: nks.edit.clipboardHistory, nks.edit.pasteKeepCaret
1. Drücken Sie Strg+Umschalt+V (Menü Bearbeiten, Zwischenablage-Verlauf). Die Liste zeigt die zuletzt kopierten oder ausgeschnittenen Texte, den neuesten zuerst, mit einer Vorschau des markierten Eintrags.
2. Durch Tippen grenzen Sie die Liste ein, wählen mit den Pfeiltasten einen Text und drücken die Eingabetaste.
- Der Text wird wie bei Strg+V eingefügt: bei mehreren Cursorn in jeden Cursor (eine Zeile pro Cursor, wenn die Zeilenzahl der Cursorzahl entspricht), als ein Rückgängig-Schritt. Es funktioniert auch im seitenweisen Modus.
- Der Verlauf hält so viele Texte, wie die Einstellung Größe des Zwischenablage-Verlaufs sagt. Er liegt nur im Speicher und wird beim Beenden vergessen. Ein Text mit mehr als 1.048.576 Zeichen wird nicht gespeichert.
- Einfügen ohne den Cursor zu bewegen (Menü Bearbeiten) fügt den Text aus der Zwischenablage ein und lässt den Cursor, wo er war, vor dem neuen Text. In einer Blockauswahl kehrt der Cursor in die linke obere Ecke zurück.
- Einfügen funktioniert nicht in einem schreibgeschützten Dokument; Einfügen ohne den Cursor zu bewegen auch nicht im seitenweisen Modus (Thema Große Dateien).

## [edit.insertLines] In Zeilen einfügen
commands: nks.edit.insertIntoLines
Der Befehl schreibt Text, Nummern oder Aufzählungszeichen in die Zeilen der Auswahl, oder in alle Zeilen, wenn nur ein Cursor da ist.
1. Wählen Sie Bearbeiten, In Zeilen einfügen. Die Vorschau zeigt die ersten Zeilen so, wie sie aussehen werden.
2. Wählen Sie Text, Nummern oder Aufzählungszeichen und wohin geschrieben wird: Anfang jeder Zeile, Ende jeder Zeile, vor eine Spalte oder in jeden Cursor.
3. Drücken Sie OK. Das Ergebnis ist ein Rückgängig-Schritt.
- Nummern: Erste Nummer, Stellen (die Nummer wird links mit Nullen auf diese Breite aufgefüllt) und Trenner, der zwischen Nummer und Zeilentext steht (am Zeilenanfang nach der Nummer, am Zeilenende vor der Nummer). In jeden Cursor wird nur die Nummer geschrieben, drei Cursor bekommen also 1, 2 und 3.
- Anfang jeder Zeile schreibt an den Zeilenanfang; Einrückung überspringen schreibt vor das erste sichtbare Zeichen. Vor Spalte zählt Zeichen ab 1, und eine kürzere Zeile bekommt den Text an ihrem Ende. Leere Zeilen überspringen überspringt auch Zeilen nur mit Leerzeichen, sie verbrauchen keine Nummer.
- Die Zeilenenden des Dokuments ändern sich nicht. Es funktioniert nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus (Thema Große Dateien).
- Letzten Befehl wiederholen führt ihn mit denselben Einstellungen erneut aus. Im Profil PSPad ist es Alt+I.

## [edit.sort] Zeilen sortieren
commands: nks.edit.sortLines
1. Markieren Sie die Zeilen (oder setzen Sie den Cursor irgendwohin, um alle Zeilen zu sortieren) und wählen Sie Bearbeiten, Zeilen sortieren.
2. Wählen Sie auf- oder absteigend, ob die Groß-/Kleinschreibung zählt, ob doppelte Zeilen entfernt werden und wonach verglichen wird. Die Vorschau zeigt die ersten Zeilen des Ergebnisses.
3. Drücken Sie OK. Das Ergebnis ist ein Rückgängig-Schritt, und die sortierten Zeilen bleiben markiert.
- Zeichencode ordnet nach dem Code der Zeichen. Gebietsschema nutzt die Regeln einer Sprache, angegeben als Kürzel wie cs-CZ oder als Hexadezimalzahl wie 0405 (leer heißt sprachneutral). Zahl am Anfang liest die Zahl, mit der eine Zeile beginnt; Zeilen ohne Zahl kommen zuerst.
- Ab Spalte und Bis Spalte vergleichen nur diesen Teil jeder Zeile (0 bedeutet die ganze Zeile).
- Das Entfernen doppelter Zeilen vergleicht ganze Zeilen und behält die erste.
- Mehrere Auswahlen werden jede für sich sortiert. Die Zeilenenden ändern sich nicht. Es funktioniert nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus.

## [search.selectLines] Zeilen nach Bereich auswählen
commands: nks.search.selectLinesByRange
1. Wählen Sie Suchen, Zeilen nach Bereich auswählen.
2. Geben Sie Zeilennummern und Bereiche ein, zum Beispiel 10-20, 40-50, getrennt durch Kommas, Semikolons oder Leerzeichen. Die Zeile unter dem Feld sagt, wie viele Auswahlen entstehen.
3. Drücken Sie OK. Jeder Bereich wird zu einer eigenen Auswahl mit eigenem Cursor, und Kopieren kopiert dann die Zeilen aller Bereiche.
- Bereiche, die sich überlappen oder aneinanderstoßen, werden verbunden. Zeilennummern beginnen bei 1; Zeilen hinter dem Dokumentende werden ignoriert.
- Letzten Befehl wiederholen wählt dieselben Zeilen erneut aus.
- Es funktioniert nicht im seitenweisen Modus.

## [format.block] Zeilen ausrichten und umbrechen
commands: nks.edit.reformatParagraph, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
Diese Befehle stehen im Menü Format. Sie wirken auf die markierten Zeilen, oder auf alle Zeilen, wenn nur ein Cursor da ist (Absatz neu umbrechen: auf den Absatz mit dem Cursor). Jeder ist ein Rückgängig-Schritt.
- Absatz neu umbrechen bricht den Text an der Spalte der Einstellung Spalte des rechten Rands um, verbindet die Zeilen eines Absatzes und entfernt überzählige Leerzeichen. Absätze trennt eine Leerzeile, und jede Zeile eines Absatzes bekommt die Einrückung der ersten. Ist kein rechter Rand eingestellt, sagt es die Statusleiste.
- Zeilen linksbündig entfernt die Leerzeichen am Anfang und Ende der Zeilen.
- Zeilen rechtsbündig und Zeilen zentrieren richten sich nach der Breite der längsten Zeile.
- Zeilen im Blocksatz vergrößert die Lücken zwischen den Wörtern, bis jede Zeile so breit ist wie die längste; eine Zeile mit einem Wort bleibt.
- Im Profil PSPad liegen Zeilen linksbündig und Zeilen rechtsbündig auf Strg+Alt+Pfeil links und Strg+Alt+Pfeil rechts.
- Sie funktionieren nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus.

## [edit.insertSmall] Datum, GUID und Wiederholen
commands: nks.edit.insertDateTime, nks.edit.insertGuid, nks.edit.repeatLast
- Datum und Uhrzeit einfügen schreibt Datum und Uhrzeit in jeden Cursor, im Format der Einstellung Datums- und Zeitformat (ein benutzerdefiniertes .NET-Format wie yyyy-MM-dd HH:mm; Monats- und Tagesnamen folgen der Sprache der Oberfläche). Eine Auswahl wird ersetzt. Im Profil PSPad ist es Alt+D.
- GUID einfügen schreibt in jeden Cursor eine neue GUID, jeder seine eigene: klein geschrieben, mit Bindestrichen, ohne geschweifte Klammern.
- Letzten Befehl wiederholen führt den zuletzt ausgeführten Befehl, der Text ändert, erneut aus: Zeile löschen, Zeile duplizieren, Zeile verschieben, Zeilen verbinden, Einzug vergrößern, Einzug verkleinern, Groß-/Kleinschreibung, Kommentar umschalten, In Zeilen einfügen, Zeilen sortieren, Absatz neu umbrechen und die Ausrichtungsbefehle, Datum und Uhrzeit einfügen, GUID einfügen. Ein Befehl mit Dialog wird mit den Einstellungen des letzten Laufs ohne Nachfrage wiederholt. Im Profil PSPad ist es Alt+Q.
- Sie funktionieren nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus.

## [edit.json] JSON formatieren
commands: nks.edit.formatJson, nks.edit.previewFormatJson
1. Markieren Sie JSON-Text, oder markieren Sie nichts, um das ganze Dokument zu formatieren.
2. Menü Bearbeiten, JSON formatieren, formatiert die markierten Bereiche (jede Auswahl) oder das ganze Dokument. Alle Bereiche sind ein Rückgängig-Schritt; Text außerhalb bleibt Byte für Byte unverändert, auch die Zeilenenden.
3. Vorschau der JSON-Formatierung zeigt die Änderung zuerst als Vorschlag. Wenden Sie ihn in der Leiste über dem Editor an oder verwerfen Sie ihn.
- Ein Syntaxfehler wird mit Zeile und Spalte gemeldet, und nichts wird geändert. Text über dem Limit wird mit einer Meldung abgelehnt.
- Hat sich das Dokument nach dem Start des Befehls geändert, bricht der Befehl ab und bittet Sie, ihn erneut auszuführen.

## [tools.stats] Dokumentstatistik und Hashes
commands: nks.tools.statistics, nks.tools.hash
1. Menü Extras, Dokumentstatistik, zählt Bytes, Zeichen, Wörter, Zeilen und die längste Zeile des ganzen Dokuments.
2. Menü Extras, Hash berechnen, fragt nach dem Algorithmus (MD5, SHA-1, SHA-256, SHA-384 oder SHA-512) und berechnet den Hash der rohen Bytes der ganzen Datei oder des markierten Teils.
- Beide Befehle lesen das Dokument blockweise, während Sie weiterarbeiten, zeigen den Fortschritt und lassen sich abbrechen. Keiner hält die ganze Datei im Speicher, daher funktionieren sie bei Dateien jeder Größe, auch in der seitenweisen und der Hex-Ansicht.
- Eine abgebrochene Statistik wird als unvollständig angezeigt, mit der Zahl der gezählten Bytes. Ein abgebrochener Hash hat kein Ergebnis.
- Statistik und Hash gelten für das Dokument, wie es beim Start des Befehls war; Änderungen währenddessen ändern sie nicht.
- Ein Wort ist eine zusammenhängende Folge von Buchstaben und Ziffern. Für mehrere markierte Bereiche gleichzeitig gibt es keinen Hash.

## [tools.calculate] Blockberechnung, Ausdrücke und Zahlensysteme
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.convertBase
1. Markierten Block berechnen addiert die Zahlen in der Markierung (Blockmarkierung, eine Spalte von Zahlen) und zeigt Anzahl, Summe, kleinste, größte und Durchschnitt.
2. Ausdruck auswerten fragt nach einem Ausdruck, beginnt mit dem markierten Text und zeigt das Ergebnis. Verstanden werden + - * / ^ ( ), MOD und die Funktionen ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR und TRUNC. Zahlen werden mit Punkt geschrieben. Alles andere, etwa der Aufruf eines Systembefehls, wird abgelehnt und nichts wird ausgeführt.
3. Zahlensystem umrechnen fragt nach einer ganzen Zahl, dezimal oder mit dem Präfix 0x, 0b oder 0o, und zeigt sie dezimal, hexadezimal, oktal und binär.
- Das Programm erkennt, wie die Zahlen in der Markierung geschrieben sind (Tschechisch und Slowakisch: 1 234,50, Deutsch: 1.234,50, Englisch: 1,234.50). Passen mehrere Schreibweisen, etwa bei 1,234, nimmt es die der Programmsprache. Die Ergebnisse erscheinen im Format der Programmsprache. Ein Sprachwechsel ändert das Dokument nicht.
- Text in der Markierung, der keine Zahl ist, wird übersprungen und gezählt. Die Zahlenumrechnung funktioniert für Werte von -9223372036854775808 bis 18446744073709551615.
- Potenzen nehmen nur eine positive Basis. Die Berechnung verwendet Zahlen mit doppelter Genauigkeit.

## [tools.colors] Farben
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. In CSS-, HTML-, XML-, JavaScript-, TypeScript-, PHP-, Razor- und Latte-Dateien steht vor jedem Farbwert (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) ein kleines Farbfeld in seiner Farbe. Das Farbfeld belegt kein Zeichen des Dokuments.
2. Klicken Sie auf das Farbfeld, oder setzen Sie den Cursor auf den Wert und wählen Sie Extras, Farbe am Cursor wählen. Es öffnet sich die Auswahl: Regler für Farbton, Sättigung, Helligkeit und Deckkraft und ein Wertfeld, das auch eine Farbe in anderer Schreibweise annimmt. Ein Klick außerhalb der Auswahl schreibt die Farbe über den Wert, in der Schreibweise, die er hatte, als einen Rückgängig-Schritt; Esc schließt die Auswahl ohne Änderung.
3. Extras, Farbe in HEX, RGB, HSL oder oklch umwandeln schreibt den Farbwert am Cursor, oder alle in der Markierung, in dieser Schreibweise neu. Die Farbe bleibt dieselbe Acht-Bit-Farbe; oklch wird mit fünf Nachkommastellen bei Helligkeit und Chroma und zwei beim Farbton geschrieben.
- Eine Farbe außerhalb des sRGB-Bereichs wird darauf beschnitten. Die Deckkraft bleibt erhalten, als `/ Alpha` oder als die letzten zwei Hexziffern.
- Text, der nur wie eine Farbe aussieht, etwa `rgb(var(--c))`, die HTML-Entität `&#123;` oder `#12345`, bleibt unberührt.
- Eine Datei, die größer als das Limit des Plugins ist, wird nicht umgewandelt; die Meldung sagt es.

## [tools.keys] Schlüsselpfade in YAML und JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. In einer YAML- oder JSON-Datei zeigt die Statusleiste den Pfad des Schlüssels am Cursor, zum Beispiel `cs.app.title`. Listeneinträge werden `items[0]` geschrieben, ein Schlüssel mit Punkt oder Klammer wird `["a.b"]` geschrieben. Der Pfad wird für die Revision und den Cursor gelesen, für die gefragt wurde: Tippen oder bewegen Sie sich inzwischen, wird die Antwort verworfen.
2. Extras, Schlüsselpfad kopieren legt den Pfad in die Zwischenablage.
3. Extras, Gehe zu Schlüssel (oder `@` in Gehe zu Beliebigem) listet die Schlüssel der Datei nach Pfad; Enter markiert den Schlüssel, wie er geschrieben steht. Schlüsselgliederung anzeigen listet dieselben Schlüssel im Plugin-Bereich, und ein Klick dort markiert dieselben Bytes.
4. Extras, Schlüssel mit anderer Datei vergleichen fragt nach einer weiteren geöffneten YAML- oder JSON-Datei und schreibt ins Ausgabefenster die Schlüssel mit Wert, die der jeweils anderen Datei fehlen.
- YAML im Blockstil wird verstanden: verschachtelte Schlüssel, Listen, Schlüssel in Anführungszeichen und Blockskalare. Ein Wert in geschweiften oder eckigen Klammern zählt als ein Wert. JSON darf Kommentare und ein Komma am Ende haben.
- Ein Syntaxfehler beendet das Lesen an dieser Stelle; die Schlüssel davor bleiben, und die Meldung nennt die Zeile.

## [tools.outline] Codegliederung und Symbole
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. In HTML-, PHP-, CSS-, C#- und Markdown-Dateien zeigt Extras, Codegliederung anzeigen die Funktionen, Klassen, Überschriften und Regeln der Datei als Baum im Plugin-Bereich. Ein Klick markiert den Namen des Symbols.
2. Extras, Gehe zu Symbol (oder `@` in Gehe zu Beliebigem) listet dieselben Symbole nach Namen; Enter markiert dieselben Bytes wie ein Klick in der Gliederung.
3. Die Gliederung gehört zu einer Revision des Dokuments. Nach einer Änderung wird sie von selbst erneuert; bis die neue da ist, bleibt die alte als veraltet markiert, und ein Sprung wird über die seitdem gemachten Änderungen getragen oder abgelehnt, wenn der Text, auf dem das Symbol stand, geändert wurde. Eine Antwort für eine ältere Revision wird nie als aktuell gezeigt.
4. Hält der Plugin-Prozess an, wird die Gliederung nach seiner Rückkehr neu gelesen.
- Gelesen wird mit einfachen Regeln der Sprache, ohne Sprachserver und ohne Projektindex: HTML (Überschriften, Elemente mit `id`, `main`, `nav`, `section` und ähnliche), PHP (Namensräume, Klassen, Schnittstellen, Traits, Enums, Funktionen, Methoden), CSS (Regeln und At-Regeln), C# (Namensräume, Typen, Methoden, Konstruktoren, Eigenschaften), Markdown (`#`-Überschriften).
- Kommentare und Zeichenketten werden nicht gelesen. Was die Regeln nicht erkennen, wird übersprungen.
- Eine Datei über dem Limit des Plugins oder mit mehr Symbolen, als die Gliederung fasst, wird gekürzt gezeigt, und die Meldung sagt es.

## [edit.carets] Mehrere Cursor
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Markieren Sie ein Wort; Strg+D fügt sein nächstes Vorkommen zur Auswahl hinzu. Wiederholen Sie das für weitere Vorkommen.
2. Strg+K und dann Strg+D überspringt das letzte Vorkommen und nimmt das nächste.
3. Strg+Umschalt+L wählt alle Vorkommen auf einmal aus.
4. Strg+Alt+Pfeil nach oben und Strg+Alt+Pfeil nach unten fügen einen Cursor in der Zeile darüber und darunter hinzu.
5. Beim Tippen ändern sich alle Stellen gleichzeitig. Esc lässt nur einen Cursor übrig.
- Die Statusleiste zeigt die Anzahl der Cursor.
- Alt+Ziehen mit der Maus erzeugt je nach Einstellung Alt+Ziehen einen Cursor in jeder Zeile oder eine Blockauswahl.

## [edit.mark] Marke und bis Marke auswählen
commands: nks.edit.selectToMark
1. Suchen Sie den Befehl Marke setzen / bis Marke auswählen im Menü Bearbeiten.
2. Die erste Benutzung speichert eine Marke an der Cursorposition.
3. Bewegen Sie den Cursor woandershin und benutzen Sie den Befehl erneut: Der Text von der Marke bis zum Cursor wird ausgewählt, und die Marke verschwindet.
- Die Taste F8 hat dieser Befehl nur im Verhaltensprofil PSPad; im Standardprofil ist F8 die Blockauswahl.

## [edit.block] Blockauswahl
commands: nks.edit.toggleBlockSelect
1. Drücken Sie F8 (im Profil PSPad Strg+Umschalt+C).
2. Erweitern Sie das Rechteck über Zeilen und Spalten mit den Pfeiltasten, Pos1 oder Umschalt mit den Pfeilen.
3. Beim Tippen oder Einfügen ändert sich der Text in jeder Zeile des Blocks. Beim Einfügen werden die Zeilen der Zwischenablage einzeln verwendet.
4. Ein weiteres F8 hebt den Block auf.
- Ein Block lässt sich auch mit gedrückter Alt-Taste und der Maus ziehen, wenn die Einstellung Alt+Ziehen es zulässt.
- Im Seitenmodus gibt es keine Blockauswahl.

## [search.find] Im Dokument suchen
commands: nks.search.find, nks.search.findNext, nks.search.findPrevious, nks.search.findWord
1. Strg+F öffnet unten die Suchleiste. Tippen Sie den Text; die Trefferzahl erscheint neben dem Feld, und die Treffer werden markiert.
2. Die Eingabetaste findet das nächste Vorkommen, Umschalt+Eingabe das vorherige. Genauso arbeiten F3 und Umschalt+F3.
3. Strg+F3 sucht das Wort unter dem Cursor.
4. Esc schließt die Leiste.
- Schalter: Aa beachtet die Groß- und Kleinschreibung (Alt+C), .* ist ein regulärer Ausdruck (Alt+R), ab ganze Wörter (Alt+W), Auswahl sucht nur im markierten Text.
- Erreicht die Suche das Ende, geht sie am Anfang weiter, und die Statusleiste meldet es.
- Ein ungültiger Ausdruck wird bei der Trefferzahl angezeigt. Ein zu langsamer Ausdruck wird von der Suche abgebrochen.
- Die Suche funktioniert auch im Seitenmodus; das Ersetzen dort nicht.
- Schalten Sie den regulären Ausdruck ein, erscheint die Schaltfläche Testen (Thema Regex-Tester).

## [search.replace] Ersetzen
commands: nks.search.replace
1. Drücken Sie Strg+H oder wählen Sie Suchen und dann Ersetzen. Die Leiste hat zusätzlich das Feld Ersetzen durch.
2. Tippen Sie den Suchtext und den Ersatztext.
3. Die Eingabetaste im Ersatzfeld ersetzt einen Treffer, Strg+Alt+Eingabe alle. Dasselbe tun die Schaltflächen Ersetzen und Alle.
4. Die Statusleiste meldet, wie viele Treffer ersetzt wurden, oder Es wurde nichts ersetzt.
- Beim Ersetzen mit regulären Ausdrücken gelten $1, $& und die Umwandlung der Schreibweise \U \L \E \u \l.
- Es geht nicht in einem schreibgeschützten Dokument und nicht im Seitenmodus.

## [search.inFiles] In Dateien suchen und ersetzen
commands: nks.search.findInFiles, nks.search.replaceInFiles
1. Strg+Umschalt+F öffnet die Leiste mit dem Feld Ordner; Strg+Umschalt+H fügt das Ersatzfeld hinzu.
2. Tragen Sie Suchtext und Ordner ein (die Schaltfläche Durchsuchen wählt ihn) und drücken Sie die Eingabetaste oder Suchen.
3. Die Ergebnisse öffnen sich im Tab Suchergebnisse: Dateien, Zeilen und Anzahlen. Die Eingabetaste oder ein Doppelklick öffnet einen Treffer.
4. Beim Ersetzen zeigt die Schaltfläche Vorschau die vorgeschlagenen Änderungen. Durch Abwählen nehmen Sie eine Datei oder eine einzelne Änderung heraus, Ausgewählte Änderungen anwenden schreibt sie.
- Eine Suche lässt sich mit der Schaltfläche Abbrechen oder Esc abbrechen. Bei zu vielen Treffern hält sie an und nennt, wie viele sie gezeigt hat.
- Beim Ersetzen bleiben Dateien mit Konflikt unverändert: Das Dokument wurde nach der Vorschau geändert, ein anderes Programm hat die Datei geändert, die Datei ist verschwunden, der neue Text lässt sich in der Codierung der Datei nicht schreiben oder die Änderung passt nicht mehr. Neue Vorschau erstellt die Vorschau erneut.
- Vor dem Schreiben in Dateien trägt das Programm den Plan ins Journal ein; nach einem Absturz mitten im Stapel bietet es Abschließen oder Zurücksetzen an.
- F1 im Ergebnis-Tab öffnet dieses Thema.

## [search.regex] Regex-Tester
commands: nks.search.regexTester
1. Wählen Sie Suchen und dann Regex-Tester oder drücken Sie in der Suchleiste bei eingeschaltetem regulärem Ausdruck auf Testen.
2. Tippen Sie den Ausdruck, optional einen Ersatz und einen Beispieltext.
3. Das Fenster zeigt die Treffer mit ihren Gruppen, den Text nach dem Ersetzen und eine Erklärung des Ausdrucks Teil für Teil.
- Es nutzt dieselbe Engine wie die Suche (.NET-Regex). Die Erklärung kennzeichnet Syntax anderer Dialekte als nicht unterstützt.
- Ein Ausdruck mit Rückverweis, Lookaround oder atomarer Gruppe läuft in einer langsameren klassischen Engine und wird nach dem Limit der Einstellung Zeitlimit für reguläre Ausdrücke (standardmäßig 2000 ms) abgebrochen; andere Ausdrücke laufen in linearer Zeit ohne Limit.
- Läuft ein Ausdruck zu lange, wird er abgebrochen, und das Fenster sagt es. Es wird nur eine begrenzte Zahl von Treffern aufgelistet.
- F1 in diesem Fenster öffnet dieses Thema.

## [search.gotoLine] Gehe zu Zeile
commands: nks.search.gotoLine
1. Drücken Sie Strg+G oder wählen Sie Suchen und dann Gehe zu Zeile.
2. Tippen Sie eine Zeilennummer (120), Zeile und Spalte (120:5), einen Prozentwert (40 %) oder ein Byte (@4096).
3. Bestätigen Sie mit der Eingabetaste. Der Cursor springt dorthin.
- Eine Zahl über der Zeilenzahl führt zur letzten Zeile.
- Eine ungültige Eingabe meldet „...“ ist keine Zeilennummer.
- In einer großen Datei, deren Zeilen noch nummeriert werden, meldet die Statusleiste: Der Zeilenindex reicht noch nicht bis zu dieser Position; versuchen Sie es gleich noch einmal. Ein Byte-Offset (@4096) oder eine Prozentangabe wirkt sofort; bis die Nummerierung aufgeholt hat, ist die Zeilennummer nach einem solchen Sprung mit ≈ markiert.

## [search.gotoBracket] Zur passenden Klammer springen
commands: nks.search.gotoBracket
1. Setzen Sie den Cursor neben eine Klammer.
2. Drücken Sie Strg+Umschalt+Backslash oder wählen Sie Suchen und dann Zur passenden Klammer springen.
3. Der Cursor springt zur Gegenklammer.
- Hat die Klammer kein Gegenstück, meldet die Statusleiste Die Klammer hat kein Gegenstück.
- In einer großen Datei im Seitenmodus gibt es den Befehl nicht.

## [search.gotoAnything] Gehe zu Beliebigem
commands: nks.search.gotoAnything
1. Drücken Sie Strg+P.
2. Tippen Sie einen Teil eines Dateinamens oder Pfads. Die Liste zeigt Dateien des geöffneten Ordners, geöffnete Dokumente und zuletzt verwendete Dateien.
3. Die Eingabetaste öffnet die Datei. Tippen Sie :120, springt der Cursor zu Zeile 120 des aktiven Dokuments.
- Ohne geöffneten Ordner werden nur offene und zuletzt verwendete Dateien aufgelistet.
- Eine Datei, die nicht mehr existiert, ist als fehlt gekennzeichnet.
- Unter der Liste steht eine Vorschau der markierten Datei; eine Binärdatei wird nicht angezeigt. Die Werte einer `.env`-Datei erscheinen in der Vorschau als `••••`.
- In einer YAML- oder JSON-Datei listet `@` mit einem Teil eines Schlüsselpfads die Schlüssel der Datei (Thema Schlüsselpfade in YAML und JSON).

## [search.jump] Bearbeitungsorte und Sprünge
commands: nks.search.recentEdits, nks.search.jumpBack, nks.search.jumpForward
1. Strg+K und dann Strg+E öffnet eine Liste der Stellen, an denen Sie zuletzt Text bearbeitet haben, die neuesten zuerst. Ein Kontrollkästchen beschränkt sie auf geänderte Dokumente. Die Eingabetaste springt zur Stelle.
2. Alt+Pfeil nach links kehrt zur Stelle zurück, von der aus Sie gesprungen sind (nach Gehe zu Zeile, dem Öffnen eines Pfads, einer zugehörigen Datei und Ähnlichem). Alt+Pfeil nach rechts geht wieder vor.
- Gibt es keinen Ort zum Springen, meldet die Statusleiste Kein Ort zum Zurückspringen oder Vorwärtsspringen. Eine Liste ohne Bearbeitungen meldet Noch keine Bearbeitungsorte.
- Die Stellen wandern mit dem Text mit und überstehen das Schließen eines Dokuments.

## [search.related] Zugehörige Datei
commands: nks.search.relatedFile
1. Drücken Sie in einem gespeicherten Dokument Alt+O oder wählen Sie Suchen und dann Zur zugehörigen Datei.
2. Wird genau eine gefunden, öffnet sie sich. Sind es mehrere, wählen Sie aus der Liste.
- Eingebaute Paare: .c mit .h oder .hpp, .cpp mit .hpp oder .h, .m mit .h, eine Datei und ihr _test (Go, Python), Test.php, Tests.cs, .spec.ts und .test.ts, .xaml mit .xaml.cs (ebenso axaml, razor).
- Eigene Paare tragen Sie in der Einstellung Regeln für zugehörige Dateien als Endung|Endung ein, durch Semikolons getrennt.
- Ein Dokument ohne Datei meldet: Speichern Sie das Dokument zuerst. Kein Treffer: Keine zugehörige Datei für den Namen.

## [search.bookmarks] Lesezeichen
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
1. Strg+F2 setzt oder entfernt ein Lesezeichen in der Zeile des Cursors; links neben den Zeilennummern erscheint ein Punkt.
2. F2 springt zum nächsten Lesezeichen, Umschalt+F2 zum vorherigen; nach dem letzten geht es beim ersten weiter.
- Suchen, Lesezeichen löschen entfernt alle Lesezeichen des Dokuments.
- Ein Lesezeichen wandert mit dem Text, wenn darüber Zeilen hinzukommen oder wegfallen, und wird in der Sitzung gespeichert: Nach einem Neustart ist es wieder da.

## [view.wordwrap] Zeilenumbruch
commands: nks.view.toggleWordWrap
1. Drücken Sie Alt+Z oder wählen Sie Ansicht und dann Zeilenumbruch umschalten.
2. Lange Zeilen werden am Fensterrand umbrochen. Mit erneutem Drücken kehren Sie zurück.
- Die Wahl wird in der Einstellung Zeilenumbruch gespeichert.
- Im Seitenmodus gibt es den Befehl nicht.
- Im Profil PSPad lautet das Kürzel Strg+W.

## [view.zoom] Schriftgröße
commands: nks.view.zoomIn, nks.view.zoomOut, nks.view.zoomReset
1. Strg+= vergrößert die Schrift um einen Punkt, Strg+- verkleinert sie.
2. Strg+0 stellt die Standardgröße wieder her.
- Die Größe liegt zwischen 6 und 72 Punkt und wird in der Einstellung Schriftgröße gespeichert.

## [view.display] Verborgene Zeichen, Zeilennummern und Minimap
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Wählen Sie Ansicht und Verborgene Zeichen anzeigen (Strg+K, Strg+W): Leerzeichen, Tabulatoren und die Zeilenenden CR, LF und CRLF werden gezeichnet. Das Dokument wird nicht verändert.
2. Zeilennummern anzeigen (Strg+K, Strg+N), Aktuelle Zeile hervorheben, Rechten Rand anzeigen (senkrechte Linie bei Spalte 80, einstellbar mit der Einstellung Spalte des rechten Rands) und Anklickbare Links schalten den Rest ein und aus.
3. Minimap anzeigen (Strg+K, Strg+M) zeigt am rechten Rand ein verkleinertes Bild des Textes in den Farben der Hervorhebung. Klicken oder Ziehen scrollt. Bei einer großen Datei zeigt sie nur die Zeilen um den Bildschirm.
- Treffer der Suchleiste sind im schmalen Streifen am rechten Rand der Minimap markiert.
- Links öffnen sich mit Strg+Klick und sind im seitenweisen Modus nicht verfügbar.
- Eine Zeile mit mehr als 50.000 Zeichen wird nie umgebrochen: Zeilenumbruch umschalten sagt das in der Statusleiste. Der seitenweise Modus bricht nie um.
- Zeichen, die die Hauptschrift nicht hat, werden mit den Schriften der Einstellung Ersatzschriften gezeichnet.
- Strg und das Mausrad ändern die Schriftgröße wie die Zoom-Befehle.

## [view.secrets] Verborgene Werte in .env-Dateien
commands: nks.view.toggleSecrets
1. Dateien mit dem Namen `.env`, `.env.*` oder `*.env` zeigen jeden Wert einer Variablen als `••••`. Ansicht, Verborgene Werte anzeigen zeigt sie im aktuellen Tab, und ein erneuter Aufruf verbirgt sie wieder.
- Ruht der Mauszeiger auf einem Wert, wird nur dieser eine gezeigt.
- Der Text der Datei bleibt unverändert: Speichern schreibt dieselben Bytes.
- Verborgene Werte sind auch in der Minimap, in den Vorschauen von Gehe zu Beliebigem und Bearbeitungsorten und in den Suchergebnissen verborgen. Kopieren legt `••••` statt eines verborgenen Werts in die Zwischenablage, und Ausschneiden löscht keinen.
- Ein Plugin liest statt jedes verborgenen Werts das Zeichen `*`. Bevor ein eingebauter Befehl auf einer solchen Datei läuft, fragt das Programm, ob er die Werte lesen darf.
- Der seitenweise Modus und die Hex-Ansicht zeigen die Bytes, wie sie sind.

## [view.tabs] Tabs wechseln
commands: nks.view.nextTab, nks.view.previousTab
1. Strg+Tab wechselt zum nächsten Tab der Gruppe, Strg+Umschalt+Tab zum vorherigen.
2. Nach dem letzten Tab geht es mit dem ersten weiter.
- Tabs lassen sich auch mit der Maus ziehen und anordnen.

## [view.documents] Liste der geöffneten Dokumente
commands: nks.view.toggleDocumentList
1. Strg+B öffnet oder schließt links das Panel Geöffnete Dokumente.
2. Wählen Sie mit den Pfeilen ein Dokument und drücken Sie die Eingabetaste, um es anzuzeigen. Ungespeicherte Dokumente tragen die Marke ●.
3. Das Feld oben filtert die Liste.
- Esc schließt das Panel.

## [view.files] Panel Dateien
commands: nks.view.toggleFilePanel
1. Strg+Umschalt+E öffnet oder schließt das Panel Dateien. Ohne geöffneten Ordner bietet es Ordner öffnen an.
2. Die Pfeile gehen durch den Baum, Pfeil nach rechts und links klappen einen Ordner auf und zu, die Eingabetaste oder ein Doppelklick öffnet eine Datei.
3. Die rechte Maustaste bietet Dateivorgänge an (Thema Dateivorgänge). F2 benennt um, Entf verschiebt in den Papierkorb, F5 aktualisiert.
- Der Baum folgt Änderungen auf dem Datenträger selbstständig.

## [files.create] Neue Datei und neuer Ordner
commands: nks.files.newFile, nks.files.newFolder
1. Öffnen Sie einen Ordner und wählen Sie im Panel Dateien den Ordner, in dem erstellt werden soll.
2. Wählen Sie mit der rechten Maustaste Neue Datei im Ordner oder Neuer Ordner oder suchen Sie den Befehl in der Palette.
3. Tippen Sie den Namen und bestätigen Sie.
- Die Statusleiste zeigt Erstellt und den Namen.
- Ein ungültiger oder schon vorhandener Name wird abgelehnt (Thema Fehlermeldungen), und nichts ändert sich.

## [files.manage] Duplizieren, umbenennen, verschieben, löschen
commands: nks.files.duplicate, nks.files.rename, nks.files.move, nks.files.delete
Wählen Sie im Panel Dateien eine Datei oder einen Ordner. Die Vorgänge stehen im Kontextmenü und in der Palette.
- Duplizieren legt eine Kopie neben dem Original an.
- Umbenennen (F2): Tippen Sie den neuen Namen. Offene Tabs und die Listen der Stellen folgen mit.
- Verschieben: Tippen Sie den Zielordner.
- In den Papierkorb verschieben (Entf): Das Programm fragt nach, und die Datei kommt in den Papierkorb, aus dem Sie sie wiederherstellen können. Ist sie mit ungespeicherten Änderungen geöffnet, bleibt der Tab offen und behält sie.
- Misslingt ein Vorgang, nennt eine Leiste über dem Dokument den Grund und sagt Es wurde nichts geändert.

## [files.refresh] Aktualisieren und Datei anzeigen
commands: nks.files.refresh, nks.files.reveal
- Dateien aktualisieren (F5 im Panel) liest den Baum erneut vom Datenträger.
- Aktive Datei im Panel anzeigen öffnet das Panel und wählt darin die Datei des aktiven Tabs aus. Es funktioniert bei geöffnetem Ordner und geöffnetem Dokument.

## [format.encoding] Codierung
commands: nks.format.reread, nks.format.convert
Die Statusleiste zeigt die Codierung; ein Klick darauf öffnet dieselbe Auswahl. Ist die Codierung geschätzt, steht der Prozentwert der Sicherheit dabei.
- Mit Codierung neu öffnen: Die Datei wird in einer anderen Codierung erneut vom Datenträger gelesen. Nutzen Sie das, wenn nach dem Öffnen falsche Buchstaben erscheinen.
- In Codierung konvertieren: Das Dokument wird in eine andere Codierung umgewandelt. Welche Zeichen verloren gingen, zeigen die Liste (verlustbehaftet und eine Anzahl) und ein Fenster mit Zeile, Spalte und Byte jedes Zeichens. Ersetzen und konvertieren ersetzt sie durch ein Fragezeichen.
- Standardliste der Codierungen in den Einstellungen: UTF-8 (mit und ohne BOM), UTF-16 LE und BE, Windows-1250, Windows-1252, ISO-8859-2.
- Bei einer großen Datei im Seitenmodus sind beide Auswahlen nicht verfügbar.

## [format.eol] Zeilenenden
commands: nks.format.eolCrLf, nks.format.eolLf, nks.format.eolCr
1. Wählen Sie Format und dann Zeilenenden auf CRLF, LF oder CR oder klicken Sie in der Statusleiste auf die Zeilenenden (gemischte heißen gemischt).
2. Eine Vorschau der Änderung erscheint: wie viele Zeilenenden sich ändern. Anwenden führt sie aus.
- Gibt es nichts zu ändern, meldet die Statusleiste Nichts zu ändern.
- Mit Rückgängig lässt es sich zurücknehmen.
- In einem schreibgeschützten Dokument und im Seitenmodus gibt es den Befehl nicht.

## [format.normalize] Unicode-Normalisierung
commands: nks.format.normalizeNfc, nks.format.normalizeNfd
Ein Zeichen mit Akzent kann als ein Zeichen (NFC) oder als Buchstabe mit einem getrennten Akzent (NFD) geschrieben sein. Sie sehen gleich aus, aber Suche und Vergleich unterscheiden sie.
1. Wählen Sie Format und dann Unicode nach NFC normalisieren (oder NFD).
2. Eine Vorschau mit der Zahl der Stellen erscheint. Anwenden führt sie aus.
- Ließe sich nach dem Normalisieren ein Zeichen nicht in der Codierung der Datei schreiben, lehnt das Programm die Änderung ab und sagt es.
- Gab es nichts zu ändern, meldet die Statusleiste Nichts zu ändern.

## [format.language] Sprachmodus
commands: nks.format.language
1. Wählen Sie Format und dann Sprachmodus oder klicken Sie in der Statusleiste auf die Sprache.
2. Wählen Sie eine Sprache, zum Beispiel Nur-Text.
- Die Sprache bestimmt, welche Einstellungen nur für diese Sprache gelten (zum Beispiel Zeilenumbruch oder Einzug), und die Regeln der Hervorhebung.
- Beim Öffnen wird die Sprache anhand der Dateiendung gewählt.

## [window.new] Neues Fenster
commands: nks.window.new
1. Drücken Sie Strg+Umschalt+N oder wählen Sie Fenster und dann Neues Fenster.
2. Ein zweites Programmfenster öffnet sich. Jedes Fenster hat eigene Dokumente und Tabs.
- Beim Schließen eines Fensters bleibt ungespeicherte Arbeit im Journal und kommt beim nächsten Start zurück.

## [window.split] Geteilte Ansicht
commands: nks.window.splitView, nks.window.moveToOtherGroup
1. Strg+\ zeigt eine zweite Ansicht desselben Dokuments in der Gruppe rechts. Beide Ansichten teilen sich Text und Rückgängig-Verlauf.
2. Tab in andere Gruppe verschieben bringt den Tab in die andere Gruppe (der Befehl steht im Menü Fenster).
3. Die Grenze zwischen den Gruppen lässt sich mit der Maus ziehen.
- Tabs lassen sich auch zwischen Gruppen ziehen.
- Eine Gruppe verschwindet, wenn kein Tab mehr darin ist.

## [settings.theme] Design
commands: nks.settings.theme.system, nks.settings.theme.light, nks.settings.theme.dark
1. Wählen Sie Einstellungen und eines der Designs: Design: wie System, Design: Hell, Design: Dunkel.
2. Die Änderung wirkt sofort und wird gemerkt.
- Wie System bedeutet, dass das Programm der Systemeinstellung folgt.

## [settings.language] Sprache der Oberfläche
commands: nks.settings.language.cs, nks.settings.language.en, nks.settings.language.sk, nks.settings.language.de
1. Wählen Sie Einstellungen und dann Čeština, English, Slovenčina oder Deutsch.
2. Menüs, Meldungen und diese Hilfe schalten sofort um.
- Standard ist die Systemsprache (Einstellung Sprache der Oberfläche).

## [settings.profile] Verhaltensprofil
commands: nks.settings.profile.default, nks.settings.profile.pspad
1. Wählen Sie Einstellungen und dann Verhaltensprofil: Standard oder Verhaltensprofil: PSPad.
- Das Profil PSPad ändert: Pos1 und Ende springen zwischen dem ersten Zeichen und dem Zeilenrand, die Rücktaste im Einzug löscht bis zum Tabstopp, Alt+Ziehen macht eine Blockauswahl, Zeichenpaare werden wie in PSPad ergänzt, und einige Kürzel (Zeile löschen Strg+Y, Rückgängig Alt+Rücktaste, Zeilenumbruch Strg+W, F8 Marke, Strg+Umschalt+C Blockauswahl, Einzug Strg+Umschalt+I und U).
- Eigene Kürzel in den Einstellungen haben Vorrang vor dem Profil.

## [view.output] Ausgabefenster
commands: nks.view.toggleOutput
Das Ausgabefenster ist ein Streifen unter dem Editor, in den Aufgaben und Suchen schreiben. Standardmäßig ist es ausgeblendet.
1. Drücken Sie Strg+Umschalt+Y oder wählen Sie Ansicht und dann Ausgabe.
2. Die Tabs Aufgaben und Suche wechseln den Kanal. Die Zusammenfassung einer Suche in Dateien wird auch in den Kanal Suche geschrieben.
3. Die Schaltfläche Leeren leert den Kanal, Schließen (oder Esc) blendet das Fenster aus.
- Der Text lässt sich markieren und kopieren. Ein leerer Kanal meldet In diese Ausgabe wurde noch nichts geschrieben.
- Die Ausgabe wird aus Dateien im Profil gelesen und bleibt deshalb nach einem Absturz des schreibenden Programms sichtbar.

## [view.fullscreen] Vollbild
commands: nks.view.fullScreen
1. Drücken Sie F11 oder wählen Sie Ansicht und dann Vollbild.
2. Der Fensterrahmen verschwindet; Menü, Tabs, Editor und Statusleiste bleiben. Getippt wird weiter im Editor.
3. Ein weiteres F11 stellt den vorigen Zustand wieder her, auch ein maximiertes Fenster.
- Auf macOS verwendet das System F11; nutzen Sie das Menü Ansicht.

## [hex.open] Hex: Öffnen und Bewegen
commands: nks.view.hex, nks.file.openAsHex
Hex zeigt eine Datei Byte für Byte: Offset, Bytes hexadezimal und Zeichen. Es liest nur die Zeilen auf dem Bildschirm, deshalb spielt die Dateigröße keine Rolle.
1. Strg+K und dann Strg+H schaltet das aktive Dokument zwischen Text und Hex um (Menü Ansicht, Hex und Text umschalten). Es ist dasselbe Dokument mit demselben Rückgängig-Verlauf; die Auswahl wird über Bytes umgerechnet.
2. Datei und dann In Hex öffnen öffnet die gewählte Datei gleich in Hex. Auf der Kommandozeile tut das der Schalter --hex, bei Bedarf mit --offset.
3. Eine Datei, die binär aussieht (ein NUL-Byte in den ersten 8.000 Bytes), bekommt eine Leiste über dem Dokument: In Hex öffnen oder Als Text behalten.
- Pfeile, Bild auf und Bild ab, Pos1 und Ende (in der Zeile), Strg+Pos1 und Strg+Ende (ganze Datei). Umschalt erweitert die Auswahl. Tab wechselt zwischen Byte- und Zeichenspalte.
- Ausschneiden, Kopieren und Einfügen arbeiten mit Bytes; zwischen zwei Hex-Ansichten werden sie exakt übertragen.

## [hex.find] Hex: Suchen und Gehe zu Offset
- Strg+F öffnet das Fenster Bytes suchen. Geben Sie Bytes hexadezimal ein (50 4B ?? 04, ?? ist ein beliebiges Byte), Text in Anführungszeichen oder eine Zahl mit Typ (0x1F40 i32).
- F3 und Umschalt+F3 finden den nächsten und vorherigen Treffer. Die Statusleiste meldet Treffer 1 von 4 oder Mindestens N Treffer, wenn die Suche an ihre Grenze kam. Ein Ersetzen von Bytes gibt es nicht.
- Strg+G öffnet Gehe zu Offset. Schreibweise: 0x1F40, +0x100 (ab dem Cursor), end-512, 50%, 4K, sel (Beginn der Auswahl), val (der Vier-Byte-Wert am Cursor).
- Nicht gefunden: Es gibt keinen Treffer. Ungültiges Muster oder Ungültiger Ausdruck: Korrigieren Sie die Eingabe. Der Offset liegt außerhalb der Datei: Geben Sie einen kleineren Wert ein. Die Datei hat sich inzwischen auf dem Datenträger geändert, das Suchergebnis wurde verworfen: Wiederholen Sie die Suche.

## [hex.edit] Hex: Bearbeiten
commands: nks.hex.toggleInsert
1. Die Taste Einfg (auf macOS ⌥⌘I) schaltet zwischen ÜBERSCHREIBEN und EINFÜGEN um; der Zustand steht in der Statusleiste.
2. Beim Überschreiben werden Bytes ersetzt, und die Dateilänge ändert sich nicht. Am Dateiende sagt das Programm, dass Überschreiben die Länge nicht ändert, und dass Sie den Einfügemodus einschalten sollen.
3. Entf und Rücktaste beim Überschreiben fragen: Nullen (die Länge bleibt) oder Löschen (die Datei wird kürzer).
4. Fügen Sie mehr Bytes ein, als bis zum Dateiende übrig sind, fragt das Programm: Datei verlängern oder Nur einfügen, was passt.
- Ein Zeichen in der Zeichenspalte, das die Codierung nicht schreiben kann, wird abgelehnt: Das Zeichen lässt sich in der Codierung nicht schreiben.
- Rückgängig und Wiederholen funktionieren. Geänderte Bytes sind schattiert und unterstrichen.
- Während die Datei gespeichert wird, warten Änderungen bis zum Ende des Speicherns.

## [hex.inspector] Hex: Dateninspektor
commands: nks.hex.inspector
1. Strg+K und dann Strg+I öffnet oder schließt rechts das Panel Dateninspektor (nur in Hex).
2. Es zeigt den Wert ab dem Cursor als verschiedene Typen: Zahlen, Datum, GUID, Zeichen. Der Schalter LE und BE wählt die Bytereihenfolge.
3. Einen Wert überschreiben Sie in seiner Zeile: Eingabetaste schreibt ihn, Esc bricht ab.
- Ein Wert am Dateiende, dem Bytes fehlen, ist unvollständig und lässt sich nicht überschreiben. Eine ungültige Eingabe und ein Wert außerhalb des Typbereichs werden gemeldet (zum Beispiel Außerhalb des Typbereichs, Ungültige GUID, Datum und Uhrzeit als JJJJ-MM-TT HH:MM:SS).
- Ein Wert, der eine andere Zahl von Bytes belegt, als er ersetzt, braucht den Einfügemodus.

## [hex.layout] Hex: Aussehen des Rasters
commands: nks.hex.charset, nks.hex.layout, nks.hex.bytesPerRow8, nks.hex.bytesPerRow16, nks.hex.bytesPerRow24, nks.hex.bytesPerRow32, nks.hex.bytesPerRow48, nks.hex.bytesPerRow64, nks.hex.offsetFormat
Die Befehle stehen in Hex, im Menü Ansicht, in der Statusleiste und in der Palette zur Verfügung.
- Codierung der Zeichenspalte: die Codierung, in der Bytes als Zeichen angezeigt werden.
- Layout: Bytes pro Zeile (8, 16, 24, 32, 48, 64), Gruppierung der Bytes und Offsetformat. Jede Größe hat auch einen eigenen Befehl.
- Offsets zwischen dezimal und hexadezimal umschalten.
- Die nächste Hex-Ansicht merkt sich die Auswahl.

## [hex.save] Hex: Speichern und Änderungen vor dem Speichern
commands: nks.hex.inspectChanges
1. Strg+S speichert das Hex. Das Speichern läuft im Hintergrund, und das Fenster bleibt benutzbar; Esc bricht das Speichern ab, bevor Daten geschrieben werden (Speichern abgebrochen, die Datei blieb unverändert).
2. Datei und dann Änderungen vor dem Speichern zeigt, was das Speichern schreibt: die geänderten Bereiche mit alten und neuen Bytes und die Art des Schreibens.
- Ändern sich nur Bytewerte, wird an Ort und Stelle gespeichert (An Ort und Stelle gespeichert, N B geändert), und die ursprünglichen Bytes werden zuerst ins Journal geschrieben, damit ein durch einen Absturz unterbrochenes Speichern abgeschlossen oder zurückgesetzt werden kann.
- Ändert sich die Länge, wird eine neue Datei geschrieben und atomar ausgetauscht.
- Bevor eine kleinere Datei überschrieben wird, entsteht eine Sicherung; misslingt sie, bleibt die Datei unverändert.

## [hex.copyAs] Hex: Kopieren, Einfügen und Exportieren als
commands: nks.hex.copyAs, nks.hex.pasteAs, nks.hex.exportSelection
1. Bearbeiten und dann Hex: Auswahl kopieren als legt die ausgewählten Bytes als Text in die Zwischenablage: Hex-Zeichenfolge, Array für C, C#, Java, Python, Rust, Go, JavaScript oder Pascal, Base64, Intel HEX, Motorola S-Record oder Hexdump.
2. Hex: Einfügen als liest die Zwischenablage als Hex-Text, Base64 oder Array aus Quellcode und fügt die Bytes wie Strg+V ein.
3. Hex: Auswahl exportieren schreibt die Auswahl in eine Datei, binär oder in einer der Textformen. Es läuft im Hintergrund, und Esc bricht es ab.
- Intel HEX, S-Record und Hexdump tragen den Offset jedes Bytes in der Datei als Adresse; Intel HEX und S-Record kennen nur 32-Bit-Adressen, eine Auswahl über 4 GiB wird deshalb abgelehnt.
- Eine Auswahl, deren Text nicht in die Zwischenablage passt, wird mit dem Hinweis auf den Export abgelehnt.
- Ein aus Quellcode eingefügtes Array darf Dezimalzahlen, Hex als 0x und $ sowie Binärzahlen als 0b enthalten; eine Zahl außerhalb von -128 bis 255 stoppt das Einfügen.

## [hex.fill] Hex: Füllen und Operationen auf der Auswahl
commands: nks.hex.fill, nks.hex.operate
1. Hex: Auswahl füllen füllt die Auswahl mit einem Muster aus Hex-Bytes oder Text, wiederholt die Auswahl hinter sich, schreibt Zufallsbytes oder Zahlen mit Schrittweite. Ohne Auswahl füllt ein Muster mit * und einer Anzahl so viele Bytes am Cursor (im Einfügemodus werden sie eingefügt).
2. Hex: Operationen auf der Auswahl führt XOR, AND, OR, NOT, Addieren, eine Verschiebung oder Rotation um 1 bis 7 Bit (je Byte) oder einen Tausch der Bytereihenfolge in Gruppen zu 2, 4 oder 8 aus. Operanden wiederholen sich über die Auswahl.
3. Der Dialog zeigt vor der Bestätigung, wie viele Bytes sich ändern. Jedes Füllen und jede Operation ist ein Schritt für Rückgängig, und die gefüllten Bytes bleiben ausgewählt.
- Ein Füllen von mehr als 256 MiB wird als Muster und Länge gehalten, nicht als Bytes, kostet also selbst bei 5 GiB weder Speicher noch Zeit; das Speichern schreibt es als neue Datei. Rückgängig für ein solches Füllen und die Schritte davor gilt bis die Datei überschrieben gespeichert wird.
- Zufallsbytes, Zahlen mit Schrittweite und die Operationen halten die Auswahl im Speicher und lehnen mehr als 2 GiB ab.

## [hex.checksum] Hex: Prüfsummen und Hashes
commands: nks.hex.checksum
1. Hex: Prüfsummen und Hashes rechnet über die Auswahl oder, wenn nichts ausgewählt ist, über die ganze Datei: CRC-16, CRC-32, CRC-64 in mehreren Varianten, CRC mit eigenem Polynom, Adler-32, Sum-8/16/32, xxHash32, xxHash64, MD5, SHA-1, SHA-256, SHA-384 und SHA-512.
2. Die Berechnung läuft im Hintergrund, der Fortschritt steht in der Statuszeile; Esc bricht sie ab.
3. Das Ergebnis lässt sich kopieren oder mit einem eingefügten Wert vergleichen; Leerzeichen, Doppelpunkte, 0x und Groß- und Kleinschreibung spielen keine Rolle.
- Jede CRC-Variante hat ein festes Polynom und feste Parameter (Breite, Startwert, gespiegelte Ein- und Ausgabe, End-XOR) nach dem Katalog der CRC-Algorithmen.
- Ein CRC mit eigenem Polynom nimmt die Breite in Bit und hex das Polynom (ungerade), den Startwert, die zwei Spiegelungsflags (0 oder 1) und das End-XOR.

## [tools.run] Werkzeuge: externes Programm starten
commands: nks.tools.run, nks.tools.stop
1. Werkzeuge, Werkzeug ausführen zeigt die Werkzeuge aus der Datei tools.json im Profilordner. Die letzte Zeile öffnet diese Datei; beim ersten Mal schreibt sie eine leere Datei, die Sie ausfüllen.
2. Ein Werkzeug hat id, title, executable und args (eine Liste: ein Eintrag ist ein Argument). Optional sind cwd (das Verzeichnis, in dem es läuft; ohne Angabe der Ordner der aktuellen Datei), timeoutSeconds (ohne Angabe gibt es kein Zeitlimit) und logPattern (siehe LOG-Muster). Beispiel: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. In executable, args und cwd stehen die Variablen ${File} (vollständiger Pfad), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} und ${Column} für das aktuelle Dokument; $$ ist ein Dollarzeichen. Ein Argument bleibt ein Argument, was auch im Wert steht, Leerzeichen und Anführungszeichen eingeschlossen: Es wird keine Shell verwendet. Eine unbekannte Variable oder ${File} bei einem Dokument ohne Datei stoppt den Start, bevor etwas beginnt.
4. Ein Werkzeug, dessen Befehl und Verzeichnis noch nicht gelaufen sind, wird mit beidem angezeigt und startet erst nach Ihrer Bestätigung. Die Bestätigung gilt, bis sich die Definition oder das Programm dahinter ändert.
5. Die Ausgabe geht Zeile für Zeile in den Reiter Aufgaben des Ausgabefensters, am Ende mit Exitcode und Zeit. Beenden in diesem Fenster oder Laufendes Werkzeug beenden stoppt das Programm und alles, was es gestartet hat; ebenso das Zeitlimit.
- Batchdateien (.bat, .cmd) werden unter Windows abgelehnt, weil der Befehlsinterpreter die Argumente erneut zerlegen und auflösen würde. Geben Sie das Programm an, das die Batchdatei startet.
- Ein Programmname ohne Pfad wird nur in PATH gesucht, nie im Arbeitsverzeichnis.
- Die Ausgabe wird als UTF-8 gelesen. Es läuft immer nur ein Werkzeug.

## [tools.logPattern] LOG-Muster: auf einen Fehler in der Ausgabe klicken
commands: nks.tools.logPattern
1. LOG-Muster für die Ausgabe öffnet einen Test des Musters mit der letzten Ausgabe der Werkzeuge als Beispiel. Jede Zeile des Beispiels zeigt Datei, Zeile und Spalte, die das Muster darin gefunden hat, oder dass es nichts gefunden hat.
2. Die Maske wird von links und ohne Beachtung der Groß- und Kleinschreibung verglichen. * überspringt beliebigen Text bis zum nächsten festen Teil der Maske. %F ist der Dateiname, bis zum nächsten festen Teil. %D ist ein Laufwerk (C: gefolgt von einem Schrägstrich) davor. %L ist die Zeile und %C die Spalte (Ziffern). %S überspringt Leerzeichen und Tabulatoren. %E beendet die Maske. Eine Maske braucht %L; ohne %F zeigt sie auf die Datei, die geöffnet war. Beispiele: %F:%L:%C:* für main.c:12:5: error, *] %F(%L) für [Error] a.pas(346): text.
3. Die andere Sprache ist ein regulärer Ausdruck mit den Gruppen line (erforderlich), file und column.
4. Für die Ausgabe verwenden sorgt dafür, dass das Muster für die Zeilen im Ausgabefenster gilt: Ein Klick auf eine Zeile oder Enter öffnet die Datei an dieser Zeile. Ein relativer Dateiname wird im Verzeichnis gesucht, in dem das Werkzeug lief, dann im Ordner der beim Start geöffneten Datei, dann unter den offenen Dokumenten nach Namen.
- Ein Werkzeug kann ein eigenes Muster mitbringen: in tools.json "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] und im Werkzeug "logPattern": "gcc". Ohne "syntax" ist das Muster ein regulärer Ausdruck.

## [log.mode] Log-Modus: Stufen und nächster Fehler
commands: nks.log.nextError, nks.log.previousError, nks.log.levels
1. Ein Dokument in der Sprache Log (Dateien auf .log, oder Format, Sprache) färbt jede Zeile nach ihrer Stufe: Fehler, Warnung, Information oder Debug. Die schwerste in einer Zeile gefundene Stufe entscheidet.
2. Log: nächster Fehler und Log: vorheriger Fehler springen vom Cursor zur nächsten oder vorherigen Zeile der Stufe Fehler. Die Datei wird blockweise gelesen und die Suche endet beim ersten Treffer, sie funktioniert also bei jeder Dateigröße; die Statuszeile zeigt den Fortschritt, Esc bricht ab.
3. Log: Stufen wählt die Regeln für das Dokument. Die Standardregeln suchen die in Großbuchstaben geschriebenen Wörter ERROR, ERR, FATAL, CRITICAL, SEVERE, WARN, WARNING, INFO, DEBUG und TRACE als ganze Wörter. Eigene Regeln kommen aus tools.json: "logPatterns": [{"id": "mine", "title": "Mein Log", "levels": {"error": "\\|E\\|", "warning": "\\|W\\|"}}]. Die Schlüssel sind error, warning, info und debug; jeder Wert ist ein regulärer Ausdruck. Der Befehl schaltet für das Dokument auch den Log-Modus ein.
- Die Regeln gelten, solange das Dokument geöffnet ist, und werden nicht gespeichert.
- Eine Zeile mit mehr als 8192 Zeichen wird im Editor nach ihren ersten 8192 Zeichen beurteilt.

## [view.options] Anzeige im Editor
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
Die Befehle stehen im Menü Ansicht und in der Palette. Jeder schaltet eine Einstellung um; die Einstellungen lassen sich auch in den Einstellungen ändern.
- Verborgene Zeichen anzeigen (Strg+K und dann Strg+W): zeichnet Leerzeichen und Tabulatoren. Entspricht der Einstellung Leerraum anzeigen.
- Zeilennummern anzeigen (Strg+K und dann Strg+N): links eine Spalte mit Zeilennummern (Einstellung Zeilennummern).
- Aktuelle Zeile hervorheben: schattiert die Zeile mit dem Cursor (Einstellung Aktuelle Zeile hervorheben).
- Rechten Rand anzeigen: eine senkrechte Linie hinter der Spalte aus der Einstellung Spalte des rechten Rands; der Wert 0 schaltet die Linie aus.
- Klickbare Links: Eine Webadresse öffnet sich per Klick bei gedrückter Strg-Taste (Einstellung Klickbare Links). In einer großen Datei im Seitenmodus gibt es den Befehl nicht.
- Minimap anzeigen (Strg+K und dann Strg+M): ein verkleinertes Bild des Textes rechts, durch Ziehen wird gescrollt (Einstellung Minimap).
- Ohne geöffnetes Dokument sind die Befehle abgeblendet.

## [bookmarks] Lesezeichen
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
Ein Lesezeichen markiert eine Zeile, zu der Sie zurückkehren wollen.
1. Strg+F2 setzt ein Lesezeichen in der Zeile mit dem Cursor und entfernt es in einer Zeile, die schon eins hat. Die Statusleiste meldet Lesezeichen in Zeile N gesetzt oder Lesezeichen in Zeile N entfernt.
2. F2 springt zum nächsten Lesezeichen, Umschalt+F2 zum vorherigen. Nach dem letzten geht es beim ersten weiter.
3. Lesezeichen löschen (in der Palette) entfernt alle Lesezeichen des Dokuments; die Statusleiste meldet Lesezeichen entfernt.
- Hat das Dokument keine Lesezeichen, meldet die Statusleiste Das Dokument hat keine Lesezeichen.
- In Hex und in einem Tab mit Suchergebnissen gibt es keine Lesezeichen. In einer großen Datei, deren Zeilen noch nummeriert werden, meldet der Befehl, dass der Zeilenindex diese Stelle noch nicht erreicht.

## [recovery] Wiederherstellung nach Absturz
Das Programm schreibt ungespeicherten Text laufend in ein Journal, spätestens zwei Sekunden nach einer Änderung. Ein ungespeichertes Dokument geht so auch nicht verloren, wenn das Programm abstürzt oder der Rechner ausgeschaltet wird.
1. Starten Sie das Programm erneut.
2. Die Tabs kommen samt ungespeichertem Text zurück. Über dem Dokument steht eine Leiste Nach einem unerwarteten Beenden wurden Tabs wiederhergestellt, und in Klammern die Zahl der Tabs mit ungespeicherten Änderungen.
3. Die Schaltfläche Details listet jedes Dokument auf: samt ungespeicherten Änderungen wiederhergestellt, aus der Datei neu geöffnet, die Datei hat sich inzwischen geändert, die Datei existiert nicht mehr oder konnte nicht wiederhergestellt werden mit dem Grund.
- Hat sich eine Datei inzwischen auf dem Datenträger geändert, bleiben die Änderungen im Journal, und nichts wird überschrieben.
- Das Journal liegt im Profilordner. Einen anderen Ordner bestimmt die Einstellung Journalordner, einen Ersatz die Einstellung Ersatzordner für das Journal.
- Lässt sich das Journal in keinen Ordner schreiben, sagt die Leiste: Die Wiederherstellung nach Absturz ist aus. Ungespeicherter Text bleibt dann nur in den Tabs; speichern Sie von Hand. Hört es im Betrieb auf zu funktionieren: Das Journal funktioniert nicht mehr, die Wiederherstellung nach Absturz gilt nicht mehr, speichern Sie von Hand.
- Wird der Ersatzordner verwendet, nennt die Leiste, welcher.
- Ein unterbrochener Stapel von Ersetzungen in Dateien wird nach dem Start angeboten: Abschließen oder Zurücksetzen.

## [paged] Große Dateien (Seitenmodus)
Eine Datei über der Grenze für große Dateien (Einstellung Grenze für große Dateien, standardmäßig 6 MiB) öffnet sich im Seitenmodus. Um eine Datei ohne diese Einschränkungen byteweise zu durchsuchen und zu bearbeiten, öffnen Sie sie in Hex (Thema Hex: Öffnen und Bewegen). Das Programm liest nur die Zeilen auf dem Bildschirm, deshalb öffnet sich die Datei schnell und füllt den Speicher nicht.
Die Statusleiste zeigt dann Große Datei, ihre Größe und die Revision.
### Was geht
- Bewegen mit den Pfeilen, Bild auf, Bild ab, Pos1, Ende sowie Strg+Pos1 und Strg+Ende. Gehe zu Zeile, Prozent oder Byte (Strg+G).
- Tippen, Löschen (Rücktaste und Entf), Eingabetaste und Tab an der Cursorposition. Rückgängig und Wiederholen. Speichern.
- Suchen (Strg+F). In Dateien suchen.
### Was nicht geht
- Auswahl, Zwischenablage (Ausschneiden, Kopieren, Einfügen), Blockauswahl, mehrere Cursor.
- Befehle für Zeilen, Einzug, Schreibweise, Ersetzen.
- Zur passenden Klammer springen, Zeilenumbruch.
- Konvertieren und erneutes Lesen der Codierung, Vereinheitlichen der Zeilenenden, Normalisierung.
- Speichern unter nur in derselben Codierung.
- Eine Zeile über 16 KiB wird nur teilweise angezeigt und lässt sich nicht bearbeiten.
### Meldungen
- Die Zeilennummerierung läuft noch, Bearbeiten ist gleich möglich: Warten Sie, bis die Datei durchgelesen ist.
- Die Zeile ist länger als 16 KiB und kann im Seitenmodus nicht bearbeitet werden: Bearbeiten Sie die Zeile mit einem anderen Programm.
- Das Zeichen kann in der Codierung der Datei nicht geschrieben werden: Tippen Sie ein anderes Zeichen.
- Eine große Datei in einer Codierung lässt sich noch nicht öffnen: Die Datei wird nicht geöffnet; andere Codierungen liest das Programm in einer großen Datei noch nicht.
- Das Speichern einer großen Datei hält das Fenster auf.

## [conflict] Datei von außen geändert
Ändert ein anderes Programm die Datei, erscheint über dem Dokument eine Leiste: Die Datei wurde auf dem Datenträger geändert.
- Neu laden verwirft Ihre Änderungen und lädt die Datei vom Datenträger.
- Meine behalten behält Ihre Änderungen; das nächste Speichern fragt nach, bevor es die Datei überschreibt.
- Vergleichen öffnet das Vergleichsprogramm (Thema Mit Datenträger vergleichen).
Wurde die Datei gelöscht oder umbenannt, sagt die Leiste, dass sie nicht mehr da ist. Behalten lässt den Tab, Tab schließen schließt ihn.
Beim Speichern bekommen Sie dieselbe Frage im Fenster Datei geändert: Überschreiben, Vergleichen, Abbrechen. Ohne Zustimmung wird nichts überschrieben.

## [statusbar] Statusleiste
Unten sehen Sie Zeile und Spalte des Cursors (bei einer Auswahl die Zeichenzahl, bei mehreren Cursorn deren Anzahl), den Einzug, die Codierung, die Zeilenenden und die Sprache. Meldungen über das Ergebnis eines Befehls erscheinen an derselben Stelle.
- Ein Klick auf den Einzug bietet Leerzeichen oder Tabulatoren und die Größe an.
- Ein Klick auf die Codierung öffnet die Auswahl der Codierung (Thema Codierung).
- Ein Klick auf die Zeilenenden vereinheitlicht sie (Thema Zeilenenden).
- Ein Klick auf die Sprache öffnet die Auswahl der Sprache (Thema Sprachmodus).
- Die Hervorhebung wird noch berechnet: Bei einer langen Datei holt die Hervorhebung auf, der Text ist benutzbar. Die Zeile ist zu lang für die Hervorhebung: Eine solche Zeile bleibt ohne Farben.
- Bei einer Datei, für die .editorconfig gilt, wird der Eintrag .editorconfig angezeigt; ein Klick öffnet die Datei.

## [errors.file] Fehlermeldungen: Dateien
- Die Datei konnte nicht geöffnet werden: Pfad (Grund). Prüfen Sie, ob die Datei existiert, ob Sie die Berechtigung haben und ob kein anderes Programm sie festhält.
- Speichern fehlgeschlagen: Grund. Der Inhalt bleibt im Tab. Speichern Sie woanders (Speichern unter) oder beheben Sie die Ursache (Speicherplatz, Berechtigungen) und speichern Sie erneut.
- Die Sicherung ist fehlgeschlagen, die Datei blieb unverändert: Beheben Sie die Ursache (meist Speicherplatz) und speichern Sie erneut.
- Gespeichert, aber die Sicherung ist fehlgeschlagen: Die Datei ist in Ordnung, nur die Sicherung der früheren Fassung fehlt.
- Datei geändert oder Die Datei ist nicht mehr auf dem Datenträger: siehe Thema Datei von außen geändert.
- Zeichen, die die Codierung nicht darstellen kann: Wählen Sie eine andere Codierung (zum Beispiel UTF-8) oder Ersetzen und speichern, wodurch die Zeichen durch ein Fragezeichen ersetzt werden.
- Nach der Normalisierung ließen sich Zeichen nicht in der Codierung schreiben: Ändern Sie die Codierung der Datei und wiederholen Sie dann die Normalisierung.
- Die Änderung ist fehlgeschlagen, der Text hat sich inzwischen geändert: Wiederholen Sie den Befehl.
- Leerzeichen am Zeilenende wurden beim Speichern nicht entfernt: Die Datei wird ohne Entfernen gespeichert; schalten Sie das Entfernen in den Einstellungen aus oder verkleinern Sie die Datei.
- Eine große Datei in einer Codierung lässt sich noch nicht öffnen: siehe Thema Große Dateien.

## [errors.files] Fehlermeldungen: Panel Dateien
Misslingt ein Vorgang mit einer Datei, steht über dem Dokument eine Leiste Der Vorgang ist fehlgeschlagen mit dem Grund und immer Es wurde nichts geändert.
- Pfad existiert nicht: Aktualisieren Sie den Baum (F5), die Datei ist inzwischen verschwunden.
- Pfad existiert bereits: Wählen Sie einen anderen Namen.
- Keine Berechtigung für Pfad: Ändern Sie die Berechtigungen im Dateimanager des Systems.
- Pfad wird von einem anderen Programm verwendet: Schließen Sie dieses Programm und versuchen Sie es erneut.
- Nicht genug Speicherplatz: Schaffen Sie Platz.
- Name ist kein gültiger Name: Entfernen Sie Zeichen, die das System in Namen verbietet.
- Pfad lässt sich auf diesem System so nicht behandeln: Nutzen Sie den Dateimanager des Systems.
- Der Ordner lässt sich nicht öffnen: Wählen Sie einen anderen Ordner oder prüfen Sie die Berechtigungen.

## [errors.search] Fehlermeldungen: Suche
- Ungültiger Ausdruck: Grund. Korrigieren Sie den regulären Ausdruck oder schalten Sie .* aus; die Schaltfläche Testen zeigt, wie der Ausdruck gelesen wird.
- Der Ausdruck ist zu langsam; die Suche wurde abgebrochen: Vereinfachen Sie den Ausdruck, besonders verschachtelte Wiederholungen, oder erhöhen Sie die Einstellung Zeitlimit für reguläre Ausdrücke.
- Markieren Sie zuerst den Text, in dem gesucht werden soll: Der Schalter Auswahl ist ohne Auswahl eingeschaltet; markieren Sie Text oder schalten Sie ihn aus.
- Ordner nicht gefunden: Korrigieren Sie den Pfad im Feld Ordner.
- Keine Treffer: Es gibt keinen Treffer; prüfen Sie die Schalter Aa, .* und ab.
- Die ersten N Treffer werden angezeigt; die Suche wurde angehalten: Grenzen Sie die Suche ein.
- Suche abgebrochen: Starten Sie sie erneut.
- Dateien mit Konflikt blieben unverändert: siehe Thema In Dateien suchen und ersetzen.
- Das Wiederherstellungsjournal ist nicht verfügbar: Das Ersetzen in Dateien läuft ohne die Sicherheit, dass es sich nach einem Absturz abschließen oder zurücksetzen lässt; starten Sie es besser, wenn das Journal funktioniert.
- „...“ ist keine Zeilennummer: Tippen Sie im Fenster Gehe zu Zeile eine Zahl, Zeile:Spalte, einen Prozentwert oder @Byte (Thema Gehe zu Zeile).

## [limits] Was diese Version nicht kann
Diese Hilfe beschreibt nur, was diese Version kann. Diese Dinge sind noch nicht darin:
- Programmaktualisierung: Das Programm aktualisiert sich nicht selbst und sucht nicht nach neuen Versionen. Eine neue Version installieren Sie selbst.
- Der Hex-Editor hat keine Minimap, sammelt nicht alle Treffer in einem Tab und ersetzt keine Bytes; Gehe zu Offset zeigt keine Vorschau des Ziels; eine Einstellung, die Dateien immer in Hex öffnet, fehlt.
- Ein eigenes Vergleichsfenster: Es wird ein externes Vergleichsprogramm verwendet (Thema Mit Datenträger vergleichen).
- Bearbeiten sehr großer Dateien: nur eingeschränkt, siehe Thema Große Dateien.
- Erweiterungen (Plugins), entfernte Dateien und weitere Funktionen aus dem Plan für Version 1.0: nicht in dieser Version.

## [edit.clips] Clips
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables
Ein Clip ist ein Stück Text mit Stellen für Variablen und Eingabemarken. Clips liefern Datenplugins im Ordner plugins des Profils; ein Unterordner pro Plugin, mit der Datei nkspad-plugin.json und ohne Programm.
1. Bearbeiten, Clip einfügen wählen. Die Liste zeigt die Clips mit einer Vorschau des Textes, der eingefügt würde.
2. Einen Clip wählen. Hat er ein Formular, ausfüllen: Die Vorschau folgt der Eingabe, ein falscher Wert wird markiert und OK bleibt gedimmt, bis jeder Wert zu seinem Typ passt.
3. Der Text geht in jede Eingabemarke und ersetzt jede Auswahl, als ein Rückgängig-Schritt. Die Eingabemarken stehen danach dort, wo der Clip die Marke $| hat.
- Auswahl als Clip speichern fragt nach einem Namen und fügt den markierten Text Ihrer eigenen Bibliothek hinzu (Plugin user.clips, Ordner user-clips). Der Text wird wörtlich gespeichert, ein Dollarzeichen bleibt also ein Dollarzeichen. Der Clip steht sofort in der Liste.
- Vorlagensprache: ${name} ist eine Variable, $| setzt eine Eingabemarke (beliebig viele), $$ ist ein Dollarzeichen. Zeilenumbrüche werden zum Zeilenumbruch des Dokuments.
- Woher eine Variable ihren Wert hat, stärkste zuerst: 1. der im Formular des Clips eingegebene Wert, 2. Ihre eigenen Variablen (Bearbeiten, Benutzervariablen bearbeiten öffnet die Datei variables.json des Profils: ein JSON-Objekt aus Name und Text), 3. die eingebauten. Derselbe Name bedeutet überall dasselbe.
- Eingebaute Variablen: file.path, file.dir, file.name, file.base, file.ext (ein ungespeichertes Dokument hat kein file.*), date (jjjj-MM-tt), time (HH:mm:ss), datetime (die Einstellung Datums- und Zeitformat), guid (für jede Eingabemarke neu), selection (der an dieser Eingabemarke markierte Text), user.name. Ein Name ohne Wert stoppt das Einfügen und die Statusleiste nennt ihn.
- Formularfelder: name, label, type, default, choices, required. Typen: text, integer (ganze Zahl), number (Dezimalpunkt), color (#RGB, #RGBA, #RRGGBB, #RRGGBBAA), date (jjjj-MM-tt), file (eine vorhandene Datei), choice (einer der Werte aus choices). Ein Vorgabewert darf Variablen nutzen, etwa ${date}.
- In nkspad-plugin.json steht ein Clip als {"id": "box", "title": "Box", "text": "<div>$|</div>", "fields": [...]} unter contributes.clips; ein Clip mit "command" bekommt zusätzlich einen Befehl dieses Namens.
- Probleme mit einem Plugin (beschädigte Datei, ein Programm in einem Datenplugin) erscheinen in der Statusleiste und das Plugin wird übersprungen.
- Clips funktionieren nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus (Thema Große Dateien).

## [file.templates] Neue Datei aus Vorlage
commands: nks.file.newFromTemplate
1. Datei, Neu aus Vorlage wählen und eine Vorlage aussuchen. Eine Vorlage mit Formular fragt die Werte wie ein Clip ab.
2. Ein neuer Tab öffnet sich mit dem Text der Vorlage und den Eingabemarken an ihren Marken $| (am Anfang, wenn sie keine hat).
- Vorlagen sind ebenfalls Daten: contributes.templates in nkspad-plugin.json, mit id, title, text, fields und optional command. Sie nutzen die Vorlagensprache und die Variablen aus dem Thema Clips; eine neue Datei hat keine file.*-Variablen.

## [edit.convert] Text umwandeln
commands: nks.edit.convert
Eine Umwandlung ändert den Text aller Auswahlen auf einmal, oder der ganzen Datei, wenn nichts markiert ist. Der neue Text bleibt markiert.
1. Bearbeiten, Umwandeln wählen. Die Liste zeigt die Umwandlungen mit einer Vorschau am markierten Text (am ersten Beispiel des Konverters, wenn nichts markiert ist).
2. Eine wählen. Alle Auswahlen ändern sich in einem Schritt für Rückgängig. Lässt sich eine nicht umwandeln, wird nichts geändert und die Statuszeile nennt den Grund.
- Letzten Befehl wiederholen führt die zuletzt gelaufene Umwandlung ohne die Liste aus.
- Konverter sind Daten: contributes.converters in nkspad-plugin.json, mit id, title, rules und optional samples und command. Eine Regel ist {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Die Regeln laufen der Reihe nach, jede auf dem Ergebnis der vorigen. Mit regex gilt der Dialekt der Suche: replace darf $1, ${name}, $& und die Groß-/Kleinschreibungs-Codes \U \L \E \u \l nutzen.
- Ein Beispiel ist {"input": "...", "expected": "..."}. Beim Lesen des Plugins wird jedes Beispiel umgewandelt. Ein Konverter, dessen Beispiel einen anderen Text ergibt, der keine Regeln hat, ein leeres find oder einen falschen regulären Ausdruck, wird abgelehnt und das Plugin mit dem Grund in der Statuszeile übersprungen.
- Ein Konverter mit "command" bekommt auch einen Befehl dieses Namens, der sofort umwandelt, ohne Liste.
- Die speziellen Umwandlungen des Editors selbst stehen in derselben Liste. Diese Version hat noch keine.

## [edit.recipes] Bearbeitungsrezepte
commands: nks.edit.runRecipe
Ein Rezept wählt Teile der geöffneten Datei, ändert sie und zeigt das Ergebnis, bevor etwas geschrieben wird.
1. Bearbeiten, Bearbeitungsrezept ausführen wählen. Die Liste zeigt jedes Rezept mit den Änderungen, die es in dieser Datei machen würde.
2. Eines wählen. Das Vorschaufenster listet jede Änderung: Zeile, Spalte, alter und neuer Text. Mit Anwenden werden sie geschrieben.
3. Alle Änderungen sind ein Schritt für Rückgängig; Rückgängig bringt die Datei genau in den alten Zustand zurück. Die geänderten Teile bleiben markiert. Es wird nichts geschrieben, wenn sich die Datei inzwischen geändert hat oder das Rezept nicht laufen kann (Variable ohne Wert, zu langsames Muster).
- Ein Rezept arbeitet mit den Auswahlen, oder mit der ganzen Datei, wenn nichts markiert ist. Letzten Befehl wiederholen wendet das letzte Rezept ohne Vorschau an.
- Rezepte sind Daten: contributes.recipes in nkspad-plugin.json, mit id, title, steps und optional command. Wählende Schritte kommen zuerst. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} behält die Treffer innerhalb der bisherigen Teile. {"kind": "columns", "from": 3, "to": 5} behält die Spalten 3 bis 5 jeder Zeile (in Zeichen ab 1 gezählt; to 0 oder weggelassen heißt bis zum Zeilenende; eine kürzere Zeile ergibt nichts).
- Ändernde Schritte: {"kind": "convert", "converter": "id"} (ein Konverter desselben Plugins), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} und {"kind": "set", "text": "..."}, der den Teil durch einen Text in der Vorlagensprache der Clips ersetzt. Darin ist ${selection} der Teil selbst, und die Variablen haben dieselben Werte wie in einem Clip (Thema Clips); Einfügemarken $| werden ignoriert.
- Ein Rezept ohne Schritte, mit unbekannter Art, falschem Muster, unbekanntem Konverter, einem wählenden Schritt nach einem ändernden oder ohne ändernden Schritt wird beim Lesen des Plugins abgelehnt.

## [edit.sql] SQL: Dialekt und Formatierung
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Bearbeiten, SQL formatieren ordnet die Auswahlen an, oder die ganze Datei, wenn nichts markiert ist: eine Klausel pro Zeile, ein Select-Element pro Zeile, Unterabfragen eingerückt, Schlüsselwörter in einheitlicher Schreibung. Es ist ein Rückgängig-Schritt, und außerhalb der Auswahl ändert sich nichts.
2. Bearbeiten, Vorschau der SQL-Formatierung zeigt dasselbe Ergebnis zuerst in der Änderungsvorschau.
3. Der Dialekt bestimmt die Farben und welche Wörter Schlüsselwörter sind. Es gilt das erste gesetzte von diesen: Extras, SQL-Dialekt (nur diese Datei); `sql_dialect = mysql` in einer `.editorconfig` (jede SQL-Datei in diesem Ordner und darunter); die Einstellung Standard-SQL-Dialekt. Ohne sie ist der Dialekt Generic, und nichts wird als Fehler gemeldet.
- Dialekte: generic, mysql, postgres, sqlite, tsql. Der Editor verbindet sich nie mit einer Datenbank.
- Zeichenfolgen, in Anführungszeichen gesetzte Namen und Kommentare werden unverändert übernommen. Erneutes Formatieren des Ergebnisses ändert nichts.
- Eine Auswahl über dem Limit des Plugins wird mit einer Meldung abgelehnt.
- Einrückung (4 Leerzeichen) und Schreibung der Schlüsselwörter (Großbuchstaben) sind vorerst fest; es sind noch keine Einstellungen.

## [edit.html] HTML: Entitäten und HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlDecode, nks.edit.htmlTidy
1. Bearbeiten, In HTML-Entitäten umwandeln ersetzt in den Auswahlen (in der ganzen Datei, wenn nichts markiert ist) die Zeichen & < > " ' durch Entitäten. Aus HTML-Entitäten umwandeln liest benannte, dezimale und hexadezimale Entitäten zurück. Ein kodierter Text, zurückgewandelt, ergibt den ursprünglichen Text. Jedes ist ein Rückgängig-Schritt.
2. Bearbeiten, Mit HTML Tidy formatieren schickt das HTML an das Programm HTML Tidy und zeigt das Ergebnis in der Änderungsvorschau. Die Meldungen von Tidy gehen ins Ausgabefenster.
3. Tidy ist ein eigenes Programm. Der Editor sucht es am Pfad der Einstellung HTML-Tidy-Programm, im Ordner `tidy` neben dem Formatierer-Plugin, in `tools/tidy` des Programmordners und in den Ordnern von PATH. Wird es nicht gefunden, nennt die Meldung alle Orte.
- Tidy gehört nicht zu diesem Build: Installieren Sie es und setzen Sie den Pfad, oder legen Sie es an einen dieser Orte.
- Zeilennummern in den Meldungen zählen ab dem Anfang des Textes, der an Tidy geschickt wurde.
- Buchstaben außerhalb von ASCII werden nicht als Entitäten geschrieben; die Kodierung der Datei trägt sie.

## [help.language] Hilfe zu einer Sprache
commands: nks.help.languageHelp
1. Setzen Sie den Cursor auf ein Wort, zum Beispiel einen Funktionsnamen, und drücken Sie Alt+F1.
2. Der Editor schlägt das Wort in den Hilfequellen nach, die für die Sprache der Datei festgelegt sind. Eine Datei des Plugins öffnet sich im Editor. Eine Webadresse wird zuerst angezeigt und im Browser erst nach Ihrer Bestätigung geöffnet.
3. Gibt es für die Sprache mehrere Quellen, wählen Sie eine.
- Quellen sind Daten. Ein Plugin ohne Programm listet sie unter `languageHelp` in `nkspad-plugin.json` auf, jede mit `id`, `title`, `language` (Sprachkennung, zum Beispiel `php`), `kind` (`url` oder `file`) und `target`. In einer Webadresse steht `${word}` für das Wort und `${language}` für die Sprachkennung. Das Ziel einer Datei ist ein Pfad im Plugin-Ordner.
- Nur `http`- und `https`-Adressen werden angenommen; ein Plugin mit einer anderen Adresse wird beim Lesen abgelehnt.
- Es sind noch keine Quellen enthalten. Gibt es für die Sprache keine, sagt es die Statusleiste.

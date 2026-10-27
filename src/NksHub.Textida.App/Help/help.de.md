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
Der Tab und die Datei auf dem Datenträger werden nebeneinander in einem eigenen Tab gezeigt. Links steht der Tab mit den nicht gespeicherten Änderungen, rechts die Datei auf dem Datenträger.
1. Wählen Sie Datei und dann Mit Datenträger vergleichen. Der Tab mit dem Vergleich öffnet sich (Thema Texte vergleichen).
2. Ist in den Einstellungen ein Externes Vergleichsprogramm eingetragen, startet stattdessen Ihr Programm: links die Datei auf dem Datenträger, rechts der Inhalt des Tabs. In der Argumentvorlage ist {left} die Datei auf dem Datenträger und {right} der Inhalt des Tabs. Die dem Programm übergebene Kopie des Tabs wird gelöscht, wenn es endet.
- Ein Tab ohne Datei auf dem Datenträger meldet: Der Tab hat keine Datei auf dem Datenträger, mit der verglichen werden kann.
- Startet das Programm nicht, meldet die Statusleiste Das Vergleichsprogramm ist nicht gestartet mit dem Grund.
- Ein eigenes Vergleichsfenster hat das Programm nicht; es verwendet das von Ihnen eingestellte Programm.

## [remote.connections] Remote-Verbindungen
commands: nks.remote.connections
Eine Verbindung merkt sich Server, Benutzer und Protokoll. Das Passwort liegt im Anmeldeinformationsspeicher des Betriebssystems und wird nie in eine Einstellungsdatei oder ein Backup geschrieben.
1. Wählen Sie Datei und dann Remote-Verbindungen. Die Liste zeigt die gespeicherten Verbindungen.
2. Wählen Sie Neue Verbindung und beantworten Sie die Fragen: Name, Protokoll, Server, Port, Benutzer und Passwort. Bei SFTP können Sie statt eines Passworts eine Datei mit dem privaten Schlüssel angeben.
3. Wählen Sie eine Verbindung, um eine Datei daraus zu öffnen oder sie mit dem gespeicherten Passwort zu löschen.
- Üblich sind FTPS und SFTP. Einfaches FTP sendet Passwort und Dateien unverschlüsselt und wird erst benutzt, wenn Sie es für diese Verbindung ausdrücklich erlauben.
- Bei der ersten Verbindung zeigt das Programm die Identität des Servers (einen SSH-Hostschlüssel oder ein TLS-Zertifikat) und fragt, ob Sie ihr vertrauen. Vergleichen Sie sie mit der, die Ihnen der Serveradministrator gegeben hat.
- Ändert sich die Identität eines Servers, dem Sie vertraut haben, verbindet sich das Programm erst, wenn Sie die neue bestätigen. Lesen Sie vorher die Warnung.
- Das Speichern von Passwörtern funktioniert unter Windows (Anmeldeinformationsverwaltung). Unter macOS und Linux wird der Anmeldeinformationsspeicher noch nicht unterstützt; das Programm sagt das, statt das Passwort anderswo abzulegen.

## [remote.open] Remote-Datei öffnen
commands: nks.remote.open
1. Wählen Sie Datei und dann Remote-Datei öffnen und wählen Sie eine Verbindung.
2. Geben Sie den Pfad auf dem Server ein. Ein Ordnerpfad zeigt seinen Inhalt: Wählen Sie einen Ordner, um hineinzugehen, oder eine Datei, um sie zu öffnen.
3. Die Datei wird auf diesen Computer kopiert und in einem Tab geöffnet. Bearbeiten und speichern Sie wie gewohnt: Speichern ändert nur die Kopie auf diesem Computer.
- Die Leiste über dem Text sagt, ob die Kopie dem Stand auf dem Server entspricht. Der Editor selbst arbeitet ohne Verbindung.
- Eine Remote-Datei, die schon eine Kopie hat, wird aus der Kopie geöffnet, auch mit Änderungen, die Sie noch nicht gesendet haben. Die Serverversion wird nie über Ihre Arbeit geladen.
- Das Protokoll der Übertragungen steht im Tab Remote des Ausgabefensters.

## [remote.upload] Auf Server hochladen
commands: nks.remote.upload
1. Wählen Sie Datei und dann Auf Server hochladen. Ein geänderter Tab wird zuerst gespeichert.
2. Bevor das Programm etwas sendet, sieht es sich die Datei auf dem Server an. Ist es noch die Version, die Sie geöffnet haben, wird der neue Inhalt unter einem vorläufigen Namen gesendet und ersetzt erst dann die alte Datei. Ein misslungener oder unterbrochener Upload lässt die alte Version auf dem Server.
3. Hat sich die Datei auf dem Server inzwischen geändert, wird nichts gesendet. Wählen Sie Unterschiede zeigen, sehen Sie die Serverversion neben Ihrer, übernehmen Sie in Ihre Seite, was Sie brauchen, speichern Sie und laden Sie erneut hoch. Trotzdem hochladen ersetzt genau die Serverversion, die Sie gesehen haben.
- Die Einstellung Prüfung der Serverversion legt fest, wie eine Änderung erkannt wird: stat vergleicht Größe und Änderungszeit, content holt die Serverdatei immer und vergleicht sie; es erkennt auch eine Änderung in derselben Sekunde und mit gleicher Größe.
- Grenze: Prüfung und Austausch sind zwei Schritte, weil FTP und SFTP kein bedingtes Schreiben kennen. Eine Änderung, die jemand in diesem letzten Moment macht, wird nicht bemerkt. Die Prüfung läuft erst nach dem vollständigen Upload, der Moment ist also kurz.
- Nichts wird automatisch wiederholt. Wählen Sie nach einem Fehler erneut Auf Server hochladen; es prüft zuerst wieder den Server.

## [remote.check] Serverversion prüfen
commands: nks.remote.check
Wählen Sie Datei und dann Serverversion prüfen. Die Leiste sagt: gleich wie auf dem Server, hier geändert und nicht gesendet, auf dem Server geändert, an beiden Stellen geändert oder die Datei gibt es auf dem Server nicht mehr. Es wird nichts geladen oder geändert.

## [remote.unsent] Nicht gesendete Remote-Änderungen
commands: nks.remote.unsent
Änderungen an einer Remote-Datei werden zuerst auf diesem Computer gespeichert. Die Liste zeigt die Remote-Dateien, deren Kopie geändert und nicht gesendet wurde, auch nach dem Schließen oder einem Absturz des Programms. Wählen Sie eine aus, dann öffnet sich die Kopie; die Leiste bietet Hochladen an.
- Das Bearbeiten braucht keine Verbindung. Nicht gesendete Änderungen bleiben, bis Sie sie hochladen.

## [remote.backup] Verbindungen sichern
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Wählen Sie Datei und dann Verbindungen exportieren und benennen Sie die Datei. Sie listet die Verbindungen: Name, Protokoll, Server, Port, Benutzer und die vertrauenswürdige Serveridentität.
2. Wählen Sie Datei und dann Verbindungen importieren, um eine solche Datei auf einem anderen Computer oder nach einer Neuinstallation zu lesen.
- Die Datei enthält keine Passwörter, Passphrasen oder Token und kann daher weitergegeben werden. Wählen Sie nach dem Import eine Verbindung und dann Passwort setzen.
- Eine importierte Verbindung ersetzt eine Verbindung mit derselben internen Kennung.

## [remote.proxy] Netzwerk-Proxy
commands: nks.remote.proxy
1. Wählen Sie Datei und dann Netzwerk-Proxy und wählen Sie HTTP, SOCKS4, SOCKS5 oder keinen Proxy.
2. Geben Sie Server und Port ein, bei Bedarf auch Benutzername und Passwort des Proxys. Das Passwort kommt in den Anmeldeinformationsspeicher.
- Der Proxy wird für FTPS und SFTP verwendet. Eine Verbindung mit eigenem Proxy behält ihn.
- SOCKS4 funktioniert nur für SFTP. Für FTPS nehmen Sie SOCKS5 oder HTTP.
- Jeder andere Proxytyp wird mit einer Meldung abgelehnt.

## [file.locks] Gesperrte Dateien
Eine Datei wird schreibgeschützt geöffnet, wenn ein anderes Programm sie geöffnet hat und anderen das Schreiben verbietet oder wenn ein anderes Textida-Fenster sie bearbeitet. Die Leiste über dem Text nennt den Grund.
- Erneut versuchen prüft die Sperre noch einmal, zum Beispiel nachdem das andere Programm die Datei geschlossen hat.
- Trotzdem bearbeiten schaltet das parallele Schreiben für die Datei ein. Jedes Speichern vergleicht dann die Datei auf dem Datenträger mit dem, was Sie geöffnet haben, und fragt nach, wenn sie sich geändert hat; nichts wird stillschweigend überschrieben.
- Die Sperre eines Fensters endet mit dem Fenster, auch nach einem Absturz. Textida legt keine Sperrdateien neben Ihre Dokumente und ignoriert .lck-Dateien anderer Programme.

## [file.history] Lokaler Verlauf
commands: nks.file.history
Das Programm behält bei jedem Speichern und bei jeder Änderung durch ein anderes Programm eine Revision der Datei. Es braucht weder ein Projekt noch Git.
1. Wählen Sie Datei und dann Lokaler Verlauf. Die Liste zeigt die Revisionen der Datei im Tab, die neueste zuerst, mit Anlass, Größe und Bezeichnung.
2. Wählen Sie eine Revision. Vergleichen öffnet sie in Ihrem Vergleichsprogramm (dem von Mit Datenträger vergleichen) links und den Tab rechts. Datei wiederherstellen setzt die ganze Revision in den Tab. Auswahl wiederherstellen ersetzt nur die ausgewählten Zeilen durch die passenden Zeilen der Revision. Bezeichnung gibt der Revision einen Namen.
- Das Wiederherstellen ist eine Änderung im Tab; Rückgängig nimmt sie zurück, und nichts wird geschrieben, bis Sie speichern. Der Text, der im Tab stand, bleibt als Revision erhalten.
- Die Revisionen liegen im Profilordner, ein Update des Programms löscht sie also nicht.
- Dateien mit geheimen Werten, etwa .env, werden nicht gespeichert, solange Sie in den Einstellungen nicht Verlauf von Dateien mit geheimen Werten führen einschalten.
- Grenzen (Revisionen pro Datei, Tage, MiB pro Datei, MiB insgesamt) stellen Sie in den Einstellungen ein; 0 heißt keine Grenze. Die neueste Revision einer Datei wird nie gelöscht.
- Ist der Datenträger voll, meldet das die Statusleiste, und keine Revision wird beschädigt.

## [file.historyMark] Revision markieren
commands: nks.file.historyMark
1. Wählen Sie Datei und dann Revision markieren und geben Sie eine Bezeichnung ein, zum Beispiel Vor dem Refactoring.
2. Der aktuelle Text des Tabs wird als Revision mit dieser Bezeichnung gespeichert. Enthält die neueste Revision schon denselben Text, erhält diese die Bezeichnung.
- Die Revision finden Sie später im Lokalen Verlauf über die Bezeichnung.

## [file.historySearch] Im Verlauf suchen
commands: nks.file.historySearch
1. Wählen Sie Datei und dann Im Verlauf suchen und geben Sie den Text ein.
2. Die Liste zeigt die Revisionen der Datei, die den Text enthalten, mit der Zahl der Treffer und der ersten Zeile. Wählen Sie eine aus, dann öffnen sich die Aktionen, zum Beispiel Vergleichen.
- Revisionen von Dateien mit geheimen Werten werden nicht durchsucht.

- Startet Ihr Programm nicht, schreibt die Statusleiste Das Vergleichsprogramm ist nicht gestartet und nennt den Grund.
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

## [edit.macro] Makros
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run
1. Extras, Makro aufzeichnen, startet die Aufzeichnung. Arbeiten Sie mit Befehlen und Tippen. Extras, Makroaufzeichnung beenden, beendet sie.
2. Extras, Letztes Makro abspielen, spielt es erneut über den aktuellen Auswahlen und Einfügemarken ab.
3. Extras, Letztes Makro speichern unter, legt es unter einem Namen im Ordner macros des Profils ab. Extras, Gespeichertes Makro ausführen, listet die gespeicherten auf und spielt das gewählte ab.
- Ein Makro merkt sich Befehle, keine Tasten: Zeile duplizieren, Groß-/Kleinschreibung, Nächstes Vorkommen hinzufügen und Ähnliches, sowie getippten Text. Mit mehreren Einfügemarken tut es dasselbe wie die Schritte von Hand.
- Das Abspielen ist ein einziger Rückgängig-Schritt. Läuft ein Schritt nicht, hält das Makro dort an und die Statuszeile nennt den Schritt; was vorher lief, bleibt ein Rückgängig-Schritt.
- Beim Aufzeichnen wird ein Befehl, den ein Makro nicht abspielen kann, abgelehnt und die Statuszeile sagt es: alles mit Dialog, Dateibefehle, Ausschneiden, Kopieren, Einfügen, Bewegen der Einfügemarke mit den Pfeiltasten, Suchen. Die Aufzeichnung läuft weiter.
- Zum Abspielen ist ein bearbeitbares Textdokument nötig. Der Import von PSPad-Makros ist nicht verfügbar.

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

## [format.inspector] Byte-Inspektor
commands: nks.format.applyEditorConfig
Jede Aktion, die Bytes umschreibt, die Sie nicht bearbeitet haben, zeigt zuerst, was sie ändert, und schreibt nichts, bis Sie bestätigen: In Codierung umwandeln, Zeilenenden, Unicode normalisieren, Speichern unter mit anderer Codierung und EditorConfig anwenden.
- Das Fenster nennt die Zahl der geänderten Bereiche und Bytes, die Größe vorher und nachher, Codierung und Zeilenenden vorher und nachher und eine Zeile je Bereich: wo er liegt, alte und neue Länge und die ersten Bytes (höchstens 500 Zeilen, der Rest wird mitgezählt).
- Kann die Zielcodierung einige Zeichen nicht aufnehmen, listet dasselbe Fenster jedes mit Zeile, Spalte und Byte auf und bietet drei Antworten: abbrechen (nichts wird geschrieben), die Codierung ändern oder jedes Zeichen durch ein Fragezeichen ersetzen.
- Der Befehl Codierung und Zeilenenden aus EditorConfig anwenden im Menü Format wandelt die Datei in den Zeichensatz und das Zeilenende um, die ihre .editorconfig festlegt; jeder Schritt öffnet den Inspektor. Eine Eigenschaft, die die Datei nicht setzt, bleibt unberührt.
- Die Bereiche gelten für die Revision, die Sie gesehen haben; ändert sich das Dokument inzwischen, wird die Aktion abgelehnt.
- Datei und dann Änderungen vor dem Speichern zeigt dieselbe Liste für das nächste Speichern.

## [format.batchEncoding] Codierung vieler Dateien umwandeln
commands: nks.file.convertEncodingBatch
1. Format und dann Codierung von Dateien in einem Ordner umwandeln. Wählen Sie den Ordner, Dateinamenmuster (zum Beispiel *.txt;*.php, leer bedeutet alle Dateien) und die Zielcodierung.
2. Das Programm liest die Dateien und zeigt, bevor es etwas schreibt, wie viele ohne Verlust umgewandelt werden und welche Dateien Zeichen verlieren würden, mit diesen Zeichen und ihrer Anzahl. Dateien, die binär sind, diese Bytes schon haben oder eine Codierung haben, die das Programm nur raten kann, werden übersprungen und mit dem Grund aufgelistet.
3. Wählen Sie N Dateien umwandeln (Dateien, die Zeichen verlieren würden, bleiben unverändert) oder Alles umwandeln und verlorene Zeichen durch ein Fragezeichen ersetzen.
4. Eine Zusammenfassung listet auf, was umgewandelt wurde, was blieb und was sich nach der Vorschau geändert hat.
- Der Ordnerdurchlauf beachtet die Ignorierdateien und lässt versteckte Dateien und Dateien über 64 MiB aus, wie die Suche in Dateien. Offene Dokumente mit ungespeicherten Änderungen bleiben außen vor.
- Jede Datei wird ganz ersetzt oder gar nicht, und nur wenn sie noch den Inhalt hat, aus dem die Vorschau entstand. Der Lauf wird protokolliert: Bleibt das Programm oder der Rechner mittendrin stehen, bietet der nächste Start an, den Stapel zu beenden oder die alten Dateien zurückzuholen.
- Eine Datei in einer nur geratenen Codierung (niedrige Sicherheit) wird nie umgewandelt; öffnen Sie sie, wählen Sie Mit Codierung neu öffnen und wandeln Sie sie dort um.

## [format.language] Sprachmodus
commands: nks.format.language
1. Wählen Sie Format und dann Sprachmodus oder klicken Sie in der Statusleiste auf die Sprache.
2. Wählen Sie eine Sprache, zum Beispiel Nur-Text.
- Die Sprache bestimmt, welche Einstellungen nur für diese Sprache gelten (zum Beispiel Zeilenumbruch oder Einzug), und die Regeln der Hervorhebung.
- Beim Öffnen wird die Sprache anhand der Dateiendung gewählt.
- Eigene Hervorhebungen sind Daten: contributes.grammars in nkspad-plugin.json nennt Grammatikdateien (JSON, formatVersion 1, beschrieben in grammar.schema.json): id, name, extensions, fileNames, comments, keywords und rules. Eine Regel ist match (ein regulärer Ausdruck), words, operators, include oder ein Bereich mit begin und end, der eine andere Sprache einbetten kann.
- Eine Hervorhebung kann die eingebauten Sprachen einbinden und einbetten, zum Beispiel "embed": "javascript". Eine Datei, die auf eine ihrer Endungen endet oder einen ihrer fileNames hat, öffnet sich in dieser Sprache; die id der Hervorhebung ist die Sprache in den Spracheinstellungen.
- Eine Hervorhebung mit falschem regulärem Ausdruck, unbekannter Eigenschaft, unbekanntem include oder embed oder mit der id einer eingebauten Sprache wird beim Lesen des Plugins abgelehnt. Das Plugin wird übersprungen und die Statusleiste nennt Datei und Regel.
- In einer sehr langen Zeile lässt die Hervorhebung eines Plugins die Zeile ohne Farben.

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
- F3 und Umschalt+F3 finden den nächsten und vorherigen Treffer. Die Statusleiste meldet Treffer 1 von 4 oder Mindestens N Treffer, wenn die Suche an ihre Grenze kam.
- Strg+G öffnet Gehe zu Offset. Schreibweise: 0x1F40, +0x100 (ab dem Cursor), end-512, 50%, 4K, sel (Beginn der Auswahl), val (der Vier-Byte-Wert am Cursor).
- Nicht gefunden: Es gibt keinen Treffer. Ungültiges Muster oder Ungültiger Ausdruck: Korrigieren Sie die Eingabe. Der Offset liegt außerhalb der Datei: Geben Sie einen kleineren Wert ein. Die Datei hat sich inzwischen auf dem Datenträger geändert, das Suchergebnis wurde verworfen: Wiederholen Sie die Suche.
- Der Platzhalter *: 50 4B * 04 steht für 0 bis N Bytes zwischen 4B und 04. N ist die Einstellung Längster Platzhalter * in der Hex-Suche; solange sie 0 ist (nicht gewählt), fragt die Suche danach. *{2,8} schreibt einen eigenen Bereich und braucht keine Einstellung.
- Ein Muster zwischen Schrägstrichen ist ein regulärer Ausdruck über Bytes, zum Beispiel /\x50\x4B[\x01-\x08]/. Ein Byte ist ein Zeichen 0 bis 255, . ist ein beliebiges Byte (auch 0A und 0D), \xHH ist ein Byte und [...] eine Menge von Bytes. Gruppen, |, ? * + {n,m} und \n \r \t \0 \d \w \s funktionieren; ^, $, \b, Rückverweise und Lookaround nicht, weil Bytes keine Zeilen oder Wörter haben. Eine offene Wiederholung (.*, +, {n,}) reicht höchstens N Bytes weit. Ein Treffer braucht mindestens ein Byte, und Treffer dürfen sich überlappen.
- Strg+H ersetzt Bytes: zuerst das Muster (dieselben Formen wie bei Suchen), dann der Ersatz als Hex, Text in Anführungszeichen oder Zahl; ein leerer Ersatz löscht die Treffer. Bei einer Auswahl wird nur in ihr gesucht.
- Eine Vorschau listet jeden Treffer mit den alten und neuen Bytes auf und warnt, wenn sich die Länge der Datei ändert. Alle ersetzen schreibt alles als einen Rückgängig-Schritt. Hat sich die Datei nach der Vorschau geändert, wird nichts ersetzt. Treffer, die einen früheren überlappen, bleiben außen vor.
- Im Überschreibmodus muss der Ersatz so lang sein wie der Treffer; schalten Sie den Einfügemodus ein (Taste Einfg), um die Länge zu ändern.

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

## [hex.bookmarks] Hex: Lesezeichen und farbige Bereiche
commands: nks.hex.bookmarks.panel, nks.hex.bookmarks.edit, nks.hex.bookmarks.export, nks.hex.bookmarks.import
Ein Hex-Lesezeichen markiert einen Bytebereich mit Name, Farbe, Kommentar und dem Typ, als der er im Dateninspektor gelesen wurde.
1. Strg+F2 markiert die Auswahl (ohne Auswahl den Wert, den der Dateninspektor am Cursor zeigt, oder ein Byte) und entfernt das engste Lesezeichen unter dem Cursor, falls es eins gibt. F2 und Umschalt+F2 springen zum nächsten und vorherigen Lesezeichen.
2. Hex: Lesezeichen-Eigenschaften öffnet Name, Farbe, Kommentar und Typ des Lesezeichens unter dem Cursor (ohne eins entsteht ein neues).
3. Hex: Lesezeichen-Bereich (Strg+K, dann Strg+B) listet die Lesezeichen mit einem Filter, der in Name, Kommentar und Typ sucht. Eingabe oder Doppelklick wählt den Bereich aus, Entf entfernt ihn.
4. Hex: Lesezeichen exportieren schreibt eine CSV (Name, Farbe, Kommentar, Typ, Start- und Endoffset) oder, wenn der Dateiname auf .json endet, JSON. Hex: Lesezeichen importieren liest beide Formen und fügt die Bereiche hinzu, die in die Datei passen; ein leerer Bereich, einer außerhalb der Datei oder ein vorhandener wird übersprungen, und die Statusleiste zählt sie.
- Ein Bereich wird in seiner Farbe getönt mit einem Balken oben gezeichnet, die Farbe ist also nicht das einzige Zeichen; die Statuszeile nennt das Lesezeichen unter dem Cursor.
- Bereiche folgen den Bytes: Bytes vor einem Bereich verschieben ihn, Bytes darin verlängern ihn, eine Änderung über seinen Rand wird aufgenommen, Rückgängig und Wiederholen bringen ihn zurück. Ein Bereich, dessen Bytes alle gelöscht wurden, entfällt.
- Lesezeichen werden in der Sitzung gespeichert (nach einem Neustart sind sie wieder da), nie in der Datei. Lesezeichen löschen entfernt alle.

## [hex.map] Hex: Dateikarte und Byte-Statistik
commands: nks.hex.minimap, nks.hex.minimap.mode, nks.hex.statistics
Die Karte ist ein Streifen neben der Bildlaufleiste, der die ganze Datei auf einmal zeigt, in Zellen von etwa vier Pixeln Höhe. Ein Rechteck markiert den Teil auf dem Bildschirm; ein Klick oder Ziehen scrollt das Raster dorthin.
1. Hex: Dateikarte (Strg+K, dann Strg+G) zeigt oder verbirgt die Karte. Hex: Karte nach Entropie oder Art der Bytes schaltet um, was die Zellen zeigen: die Entropie der Bytes (hell für geordnete Daten, kräftig für komprimierte oder verschlüsselte) oder die Art Byte, die den größten Teil der Zelle ausmacht (Nullbytes, Text, Steuerbytes, hohe Bytes, FF).
2. Ein schmaler Streifen am rechten Rand markiert die Treffer der letzten Suche, die Lesezeichen in ihren Farben und die seit dem letzten Speichern geänderten Stellen.
3. Hex: Byte-Statistik (Strg+K, dann Strg+S) öffnet einen Bereich mit dem Histogramm der Auswahl oder, wenn nichts ausgewählt ist, der ganzen Datei: wie viele der 256 Bytewerte vorkommen, die Entropie in Bit pro Byte, die häufigsten Werte und die Anteile der Byteklassen.
- Eine Datei bis 1 GiB wird im Hintergrund ganz gelesen. Bei einer größeren zeigt die Karte zuerst eine Stichprobe, einige Blöcke je Zelle, und wird genauer, je mehr das Programm liest, insgesamt höchstens 1 GiB; eine noch nicht gelesene Zelle ist blass.
- Die Statistik einer ganzen Datei über 1 GiB wird nicht auf einmal gezählt: Wählen Sie einen Bereich.
- Karte und Zählen laufen neben dem Fenster und werden bei einer Änderung der Datei neu gemacht.

## [hex.compare] Hex: Binärvergleich
commands: nks.hex.compare.file, nks.hex.compare.saved
Zwei Hex-Raster nebeneinander, die zusammen scrollen, mit eingefärbten Unterschieden und einer Liste darunter.
1. Hex: Mit Datei vergleichen fragt nach einer Datei und vergleicht sie mit dem Dokument des aktiven Tabs (mit seinen ungespeicherten Änderungen). Hex: Mit gespeicherter Datei vergleichen vergleicht das Dokument mit seiner eigenen Datei auf dem Datenträger, sodass Sie genau sehen, was das nächste Speichern ändern würde.
2. Ausgerichtet (Standard) sucht nach Bytes, die eine Datei eingefügt oder gelöscht hat, sodass ein eingefügter Block als Einfügung erscheint und die Bytes dahinter nicht als geändert gemeldet werden. Byte für Byte vergleicht jeden Offset mit demselben Offset der anderen Datei, was für Dateien mit festem Aufbau passt. Es gilt für jede Datei über 256 MiB, die der ausgerichtete Modus nicht liest.
3. Die Liste benennt jeden Unterschied: geändert, eingefügt oder gelöscht, mit seinem Bereich in der linken und der rechten Datei. Eingabe auf einer Zeile, F8 und Umschalt+F8 (oder die Schaltflächen) springen zum nächsten und vorherigen Unterschied in beiden Rastern.
- Das Scrollen eines Rasters scrollt das andere an dieselbe Stelle der anderen Datei; bei eingefügten Bytes ist das die entsprechende Stelle, nicht derselbe Offset.
- Die Raster sind schreibgeschützt. Die Karte neben jedem Raster markiert die Unterschiede.
- Die Liste endet bei 1.000.000 Unterschieden (das Fenster sagt es); zwei unabhängige Dateien würden sonst den Speicher füllen.
- Zwei Revisionen einer Datei aus dem lokalen Verlauf werden noch nicht angeboten, weil es den lokalen Verlauf noch nicht gibt.

## [tools.run] Werkzeuge: externes Programm starten
commands: nks.tools.run, nks.tools.stop
1. Werkzeuge, Werkzeug ausführen zeigt die Werkzeuge aus der Datei tools.json im Profilordner. Die letzte Zeile öffnet diese Datei; beim ersten Mal schreibt sie eine leere Datei, die Sie ausfüllen.
2. Ein Werkzeug hat id, title, executable und args (eine Liste: ein Eintrag ist ein Argument). Optional sind cwd (das Verzeichnis, in dem es läuft; ohne Angabe der Ordner der aktuellen Datei), timeoutSeconds (ohne Angabe gibt es kein Zeitlimit) und logPattern (siehe LOG-Muster). Beispiel: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. In executable, args und cwd stehen die Variablen ${File} (vollständiger Pfad), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} und ${Column} für das aktuelle Dokument; $$ ist ein Dollarzeichen. Ein Argument bleibt ein Argument, was auch im Wert steht, Leerzeichen und Anführungszeichen eingeschlossen: Es wird keine Shell verwendet. Eine unbekannte Variable oder ${File} bei einem Dokument ohne Datei stoppt den Start, bevor etwas beginnt.
4. Ein Werkzeug, dessen Befehl und Verzeichnis noch nicht gelaufen sind, wird mit beidem angezeigt und startet erst nach Ihrer Bestätigung. Die Bestätigung gilt, bis sich die Definition oder das Programm dahinter ändert.
5. Die Ausgabe geht Zeile für Zeile in den Reiter Aufgaben des Ausgabefensters, am Ende mit Exitcode und Zeit. Beenden in diesem Fenster oder Laufendes Werkzeug beenden stoppt das Programm und alles, was es gestartet hat; ebenso das Zeitlimit.
- Batchdateien (.bat, .cmd) werden über cmd.exe gestartet, und jedes Argument wird dafür in Anführungszeichen gesetzt, sodass Leerzeichen, Anführungszeichen und Zeichen wie & | < > ^ % unverändert ankommen. Ein Argument mit Zeilenumbruch oder einem anderen Steuerzeichen wird vor dem Start abgelehnt, ebenso eines mit !, wenn die Batchdatei die verzögerte Erweiterung einschaltet.
- Ein Programmname ohne Pfad wird nur in PATH gesucht, nie im Arbeitsverzeichnis.
- Die Ausgabe wird als UTF-8 gelesen. Es läuft immer nur ein Werkzeug.

## [tools.logPattern] LOG-Muster: auf einen Fehler in der Ausgabe klicken
commands: nks.tools.logPattern
1. LOG-Muster für die Ausgabe öffnet einen Test des Musters mit der letzten Ausgabe der Werkzeuge als Beispiel. Jede Zeile des Beispiels zeigt Datei, Zeile und Spalte, die das Muster darin gefunden hat, oder dass es nichts gefunden hat.
2. Die Maske wird von links und ohne Beachtung der Groß- und Kleinschreibung verglichen. * überspringt beliebigen Text bis zum nächsten festen Teil der Maske. %F ist der Dateiname, bis zum nächsten festen Teil. %D ist ein Laufwerk (C: gefolgt von einem Schrägstrich) davor. %L ist die Zeile und %C die Spalte (Ziffern). %S überspringt Leerzeichen und Tabulatoren. %E beendet die Maske. Eine Maske braucht %L; ohne %F zeigt sie auf die Datei, die geöffnet war. Beispiele: %F:%L:%C:* für main.c:12:5: error, *] %F(%L) für [Error] a.pas(346): text.
3. Die andere Sprache ist ein regulärer Ausdruck mit den Gruppen line (erforderlich), file und column.
4. Für die Ausgabe verwenden sorgt dafür, dass das Muster für die Zeilen im Ausgabefenster gilt: Ein Klick auf eine Zeile oder Enter öffnet die Datei an dieser Zeile. Ein relativer Dateiname wird im Verzeichnis gesucht, in dem das Werkzeug lief, dann im Ordner der beim Start geöffneten Datei, dann unter den offenen Dokumenten nach Namen.
- Ein Werkzeug kann ein eigenes Muster mitbringen: in tools.json "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] und im Werkzeug "logPattern": "gcc". Ohne "syntax" ist das Muster ein regulärer Ausdruck.

## [tools.edit] Werkzeuge: Definitionen bearbeiten, typisierte Variablen, Werkzeuge aus einem Ordner
commands: nks.tools.edit
1. Werkzeuge, Werkzeugdefinitionen bearbeiten öffnet einen Editor für die Werkzeuge der Datei tools.json. Neu, Speichern und Löschen schreiben die Datei sofort. Die Zeile unter dem Formular zeigt den Befehl genau so, wie er für die geöffnete Datei starten würde, oder warum er jetzt nicht laufen kann. Editor und Werkzeug ausführen nutzen dieselbe Definition, das Werkzeug startet also von beiden mit denselben Argumenten.
2. Eine Definition kann Variablen deklarieren, eine pro Zeile: Name:text oder Name:path, optional mit =Wert (der Wert darf andere Variablen nutzen, etwa Out:path=${FileDir}/out). Ohne Wert gilt die eigene oder eingebaute Variable gleichen Namens. Die Reihenfolge: deklarierter Wert, Ihre eigenen Variablen (Eigene Variablen bearbeiten), eingebaute. In executable, args und cwd steht sie als ${Name}.
3. Ein Text ist eine Zeile. Ein Pfad ist eine nicht leere Zeile ohne die Zeichen * ? " < > | . Ein Wert des falschen Typs stoppt den Lauf, bevor etwas startet, und die Ausgabe nennt die Variable.
4. Der Ordner der geöffneten Datei oder ein Ordner darüber kann .textida/tools.json mit Werkzeugen gleicher Form enthalten. Sie erscheinen in Werkzeug ausführen, laufen aber erst nach Bestätigung von Befehl und Verzeichnis, auch wenn derselbe Text in Ihrer eigenen Datei steht. Ändert sich die Definition, auch die Variablen, wird erneut gefragt. Diese Werkzeuge lassen sich im Editor nicht bearbeiten.
- Ein leeres Argument lässt sich im Editor nicht schreiben. Beim Speichern gehen Kommentare in tools.json verloren.

## [tools.make] Makefile: Ziel ausführen
commands: nks.tools.makeTargets
1. Bei geöffnetem Makefile (Makefile, GNUmakefile, *.mk, *.mak) listet Werkzeuge, Makefile-Ziel ausführen dessen Ziele aus dem Text des Tabs. Das gewählte Ziel läuft als make -f mit dieser Datei und dem Ziel im Ordner der Datei.
2. Der Lauf geht über den Werkzeug-Starter: Der Befehl wird gezeigt und beim ersten Mal bestätigt, die Ausgabe geht in den Tab Aufgaben, Stopp beendet make und alles, was es gestartet hat.
3. Eine Ausgabezeile mit Datei und Zeile (main.c:12:5: error, Makefile:7:) öffnet diese Stelle per Klick oder Enter.
- Make muss installiert und im PATH sein. Spezielle Ziele (.PHONY), Musterregeln und Namen mit Variable oder Platzhalter werden nicht aufgelistet. Die Bestätigung gilt dem Befehl, nicht dem Inhalt des Makefile, denn ein Makefile auszuführen heißt auszuführen, was darin steht.

## [packages.registry] Paketmanifeste: Namen, Versionen und Marke für neuere Versionen
commands: nks.packages.lookup, nks.packages.refresh, nks.packages.revoke
1. In package.json, composer.json, requirements.txt, einer .csproj-Datei, go.mod, Cargo.toml und Gemfile kann der Editor das Paketregister (npm, Packagist, PyPI, NuGet, Go-Proxy, crates.io, RubyGems) nach Namen und Versionen fragen. Ohne Ihre Zustimmung wird nichts gesendet: Werkzeuge, Paketversionen prüfen zeigt die Server und fragt einmal je Register.
2. Nach der Zustimmung bietet Strg+J im Namen eines Eintrags Namen aus dem Register an und in einer Version die Versionen dieses Pakets, die neueste zuerst. PyPI und der Go-Proxy haben keine Suche: ein Name wird nur angeboten, wenn der getippte Text der ganze Name ist.
3. Ein Eintrag mit genau festgelegter Version (1.2.3, ==1.2.3, v1.2.3), zu dem es ein neueres Release gibt, bekommt an der Version eine Informationsmarke. Bereiche (^, ~, >=) werden nicht markiert. Eine Vorabversion gilt nicht als neuere Version, solange es ein Release gibt. Die Prüfung läuft, wenn Sie aufhören zu tippen.
4. Antworten bleiben bis zum Schließen des Editors im Cache. Ein Angebot oder eine Marke aus dem Cache sagt das („cache“). Paketversionen aktualisieren verwirft den Cache dieses Registers und fragt erneut.
5. Paketregister-Abfragen verbieten nimmt jede Zustimmung zurück und verwirft den Cache. Es gilt der Proxy aus dem Befehl Netzwerk-Proxy; ein Proxy mit Benutzernamen lässt sich nicht verwenden, die Abfragen stoppen dann.
- Es wird nichts heruntergeladen oder installiert, nach Schwachstellen wird nicht gesucht und go mod download läuft nie. Zum Register gelangen nur Paketnamen, der getippte Text und Ihre Netzwerkadresse.
- Ein Register, das ausfällt, bietet nichts an; Paketversionen prüfen nennt den Fehler in der Statusleiste. Zeitlimit, Größenlimit und Zahl der Pakete je Prüfung sind vorläufig.

## [packages.scripts] Paketskripte: npm und Composer
commands: nks.packages.script
1. Bei geöffneter package.json oder composer.json listet Werkzeuge, Paketskript ausführen deren Skripte auf. Das gewählte läuft als npm run Name oder composer run-script Name im Ordner der Datei.
2. Es läuft über den Werkzeug-Starter wie ein Werkzeug aus einem Projektordner: Befehl, Ordner und die Datei, aus der das Skript stammt, werden gezeigt und müssen bestätigt werden. Die Bestätigung gilt für den Text des Skripts, ein geändertes Skript fragt also erneut. Die Ausgabe geht in den Tab Aufgaben; Stopp beendet es.
- npm oder Composer muss installiert sein. Unter Windows sind beide Batchdateien; der Editor startet Node mit npm-cli.js oder PHP mit composer.phar aus dem Ordner der Batchdatei, daher funktioniert auch ein Skriptname mit Zeilenumbruch oder !. Ohne dieses Standardlayout wird die Batchdatei über cmd.exe gestartet.

## [plugins.manager] Plugin-Manager: Plugins installieren, aktualisieren, zurücksetzen und entfernen
commands: nks.plugins.manage
1. Extras, Plugin-Manager listet die mitgelieferten und die selbst installierten Plugins mit den Berechtigungen auf, die jedes hält. Mit Plugin aus ZIP installieren kommt ein weiteres hinzu.
2. Die ZIP-Datei wird in einen Vorbereitungsordner entpackt und geprüft, bevor irgendetwas davon läuft: Jeder Pfad muss im Plugin-Ordner bleiben, Links werden abgelehnt, Größe und Dateianzahl sind begrenzt, das Manifest nkspad-plugin.json muss im Stamm der ZIP liegen und gültig sein, und die ID darf keinem eingebauten Plugin gehören. Jedes Problem hat eine eigene Meldung, und es wird nichts installiert.
3. Danach zeigt der Editor die Berechtigungen, die das Plugin verlangt, und wartet auf Ihre Antwort. Ohne Ihre Erlaubnis läuft nichts vom Plugin. Die Berechtigung, Dateien unter den Arbeitsordnern zu ändern und zu erstellen (workspace.files), erteilt hier der Benutzer, wie jede andere auch. Wurde die ZIP heruntergeladen (unter Windows Mark of the Web), sagt der Dialog das.
4. Nach der Wahl eines installierten Plugins können Sie es aktivieren oder deaktivieren, aus einer neueren ZIP aktualisieren, auf die vorherige Version zurücksetzen, seine Berechtigungen ändern oder es deinstallieren. Eine neue Version wird neben der alten entpackt und erst aktiv, wenn sie lädt; die alte bleibt für das Zurücksetzen. Ein Update fragt nur nach Berechtigungen, nach denen noch nie gefragt wurde; frühere Antworten gelten weiter.
5. Eine zurückgenommene Berechtigung stoppt neue Anfragen des Plugins sofort und bricht wartende ab. Ein Plugin ohne Berechtigung erhält einen Fehler, kein leeres Ergebnis. Deaktivieren beendet den Prozess; Deinstallieren löscht nur den Ordner dieses Plugins.
- Berechtigungen halten fest, was Sie erlaubt haben, und sind keine Sandbox: Ein Plugin ist ein Programm, das mit Ihren Benutzerrechten läuft. Installieren Sie nur Plugins, denen Sie vertrauen. Signaturen werden noch nicht geprüft. Die Grenzen für Größe und Dateianzahl einer ZIP sind vorläufig. Neue Befehle eines aktualisierten Plugins erscheinen nach einem Neustart.

## [tools.lint] Linter: Funde von PHPStan, Psalm, golangci-lint, Clippy, clang-tidy und RuboCop unterstreichen
commands: nks.tools.lint
1. Werkzeuge, Datei mit Linter prüfen startet den Linter, der zur geöffneten Datei passt: PHPStan oder Psalm für PHP, golangci-lint für Go, Clippy für Rust (braucht eine Cargo.toml im Ordner der Datei oder darüber), clang-tidy für C und C++, RuboCop für Ruby. Verarbeiten zwei Linter die Datei, gilt der erste, dessen Konfigurationsdatei (phpstan.neon, psalm.xml) gefunden wird, sonst der erste installierte.
2. Der Linter prüft die Datei auf dem Datenträger, also zuerst speichern: Ein Dokument mit ungespeicherten Änderungen wird nicht geprüft. Die Funde werden im Text unterstrichen (die Meldung erscheint, wenn der Zeiger darauf ruht) und auf dem Tab Aufgaben aufgelistet, einer pro Zeile; Klick oder Eingabe öffnet die Stelle. Der nächste Lauf desselben Linters ersetzt sie, ein behobenes Problem verschwindet danach.
3. Um nach jedem Speichern zu prüfen, schalten Sie unter Datei, Aktionen beim Speichern „Mit PHPStan prüfen“ (oder den anderen Linter) ein oder schreiben {"kind": "lint", "id": "phpstan"} in saveActions.json. Ein Linter läuft nie bei einer Änderung. Die Kennungen sind phpstan, psalm, golangci-lint, clippy, clang-tidy und rubocop-lint.
- Das Programm wird in vendor/bin, node_modules/.bin, .venv und PATH gesucht; es wird nichts heruntergeladen. Beim ersten Lauf müssen Sie den Befehl bestätigen.
- Es ist kein Zeitlimit voreingestellt: Fehlt lintTimeoutSeconds in saveActions.json, läuft kein Linter und die Statuszeile sagt es. Ein Linter, der das Limit überschreitet, wird gestoppt und gemeldet; der Editor bleibt dabei bedienbar.
- PHPStan meldet eine Zeile und keine Spalte, daher wird der Text der ganzen Zeile unterstrichen. Ein Programm, das eine Batchdatei ist (phpstan.bat), wird wie bei jedem Werkzeug abgelehnt.

## [file.saveActions] Aktionen beim Speichern: formatieren, Werkzeug ausführen, hochladen
commands: nks.file.saveActions
1. Datei, Aktionen beim Speichern listet auf, was rund um das Speichern läuft: Formatieren, ein Werkzeug, Hochladen auf den Server. Eine Zeile schaltet die Aktion ein oder aus; die Zeile darunter schaltet „nur beim manuellen Speichern“ um. Ein Formatierer (gofmt, rustfmt, clang-format, RuboCop) wird nur angeboten, wenn seine Konfigurationsdatei (go.mod, rustfmt.toml, .clang-format, .rubocop.yml) im Ordner der Datei oder darüber liegt, und er bleibt aus, bis Sie ihn einschalten.
2. Die Reihenfolge ist fest: zuerst die Formatierer, dann das Speichern, dann Werkzeuge und Hochladen. Ein Formatierer arbeitet an einer Kopie des Textes und wird nach Ablauf des Zeitlimits gestoppt. Läuft er ab, scheitert er (zum Beispiel an einem Syntaxfehler) oder ist er nicht installiert, wird die Datei unverändert gespeichert, und Statuszeile und Registerkarte Aufgaben nennen den Grund. Text, den Sie währenddessen getippt haben, wird nie überschrieben. Formatiert wird nur ein Dokument mit ungespeicherten Änderungen.
3. Das Hochladen sendet genau die Bytes, die dieses Speichern geschrieben hat, nie ältere oder neuere. Hat sich die Datei auf dem Datenträger nach dem Speichern geändert, wird nichts gesendet. Es gilt für eine von einem Server geöffnete Datei und geht an denselben Server.
4. Ein automatisches Speichern führt die Aktionen aus, die nicht mit „nur beim manuellen Speichern“ markiert sind. Ein Speichern durch ein Plugin führt keine Aktion aus, so löst eine Aktion, die speichert, keine Schleife aus. Speichern unter führt keine Aktion aus.
5. Die Aktionen stehen in saveActions.json im Profil (Datei der Speicheraktionen öffnen) und für ein Projekt in .textida/saveActions.json im Ordner der Datei oder darüber. Ein Eintrag des Projekts ersetzt den Eintrag des Profils mit gleicher Art und Kennung. Beispiel: {"formatTimeoutSeconds": 10, "actions": [{"kind": "format", "id": "gofmt"}, {"kind": "tool", "id": "lint", "onlyManual": true}, {"kind": "upload"}]}. Ein Werkzeug ist eines aus Werkzeug ausführen, nach seiner Kennung.
- Ein Zeitlimit ist nicht voreingestellt: Solange formatTimeoutSeconds fehlt, läuft kein Formatierer, und die Statuszeile sagt es. Der Wert im Beispiel ist nur ein Beispiel.
- Nach einem automatischen Speichern kann niemand antworten: Ein nie bestätigtes Werkzeug startet nicht, und eine neuere Version auf dem Server erscheint in der Leiste der Registerkarte statt in einem Dialog.
- Das Umschalten einer Aktion in der Liste schreibt saveActions.json im Profil neu und verwirft darin Kommentare. Aktionen aus einem Projekt werden aufgelistet, aber in der Projektdatei geändert.
- Aktionen beim Speichern bietet auch „Mit ... prüfen“ für die Linter an, die die Datei verarbeiten; sie ist aus, bis Sie sie einschalten. Die Prüfung läuft nach dem Speichern und hält es nicht auf; ihr Zeitlimit ist lintTimeoutSeconds.
## [file.configFiles] Konfigurationsdateien der Datei
commands: nks.file.configFiles
1. Die Statusleiste nennt die Konfigurationsdateien, die für die geöffnete Datei gelten: .editorconfig, .clang-format, .clang-tidy, .rubocop.yml, rustfmt.toml, .golangci.yml und die Konfigurationen der Sprache (zum Beispiel ruff.toml, stylua.toml, .shellcheckrc). Es ist jeweils die nächstgelegene Datei im Ordner der Datei oder darüber. Klicken Sie auf den Eintrag oder wählen Sie Datei, Konfigurationsdateien dieser Datei.
2. Aus der Liste öffnen Sie die gewählte Datei. Eine fehlende Konfiguration lässt sich aus einer minimalen Vorlage anlegen; sie wird in den nächsten übergeordneten Ordner mit .git geschrieben, sonst in den Ordner der Datei, und eine vorhandene Datei wird nie ersetzt.
- Es werden nur Ordner angesehen: kein Werkzeug wird gestartet, die Liste zeigt also, was auf dem Datenträger liegt, nicht was ein Werkzeug verwenden würde. Die Liste wird beim Öffnen, Speichern und Umbenennen der Datei neu gelesen; eine außerhalb des Editors angelegte Datei erscheint daher nach dem nächsten Speichern.
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
- Der Ergebnis-Tab der Suche hat keine Lesezeichen. Im Hex markieren dieselben Befehle Bytebereiche (siehe Hex: Lesezeichen). In einer großen Datei, deren Zeilen noch nummeriert werden, meldet der Befehl, dass der Zeilenindex diese Stelle noch nicht erreicht.

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

## [edit.recipeFiles] Rezepte über Dateien
commands: nks.edit.runRecipeFiles
Ein gespeichertes Rezept läuft erneut über eine andere Dateiauswahl. Sie sehen jede Änderung vorher; nichts wird geschrieben, bevor Sie sie anwenden.
1. Wählen Sie Bearbeiten, Rezept auf Dateien anwenden und ein Rezept: aus einem Daten-Plugin oder eine Datei im Ordner recipes des Profils.
2. Wählen Sie die Dateien: einen Ordner mit Dateimaske (zum Beispiel *.cs;*.txt; die Ignore-Dateien der Suche gelten), die offenen Tabs oder die Dateien der Suchergebnisse.
3. Der Tab Suchergebnisse listet die Änderungen Datei für Datei mit denselben Häkchen wie Replace in Files. Anwenden schreibt die angehakten. Eine Datei, die nach der Vorschau von jemand anderem geändert wurde, bleibt unberührt: sie wird als Konflikt gemeldet und die übrigen Dateien werden geschrieben. Offene Dokumente erhalten einen Rückgängig-Schritt.
- Eine Rezeptdatei ist JSON mit id, title und steps, dieselben Felder wie ein Rezept in nkspad-plugin.json, dazu optional version und eine Liste converters (die Konverter, die ihre convert-Schritte nennen). Eine Datei ohne version ist Version 1, das Format der ersten Rezepte, und lädt wie bisher. Eine Datei neuerer Version wird mit einer Meldung abgelehnt.
- Schritte: matches, columns, convert, replace und set arbeiten wie bei den Bearbeitungsrezepten (jede Datei ist ihr ganzer Text). Ein Schritt {"kind": "format", "formatter": "id"} lässt einen Formatierer der Speicheraktionen über das Ergebnis der vorherigen Schritte laufen; er muss der letzte sein, braucht das Zeitlimit formatTimeoutSeconds in saveActions.json und arbeitet mit Dateien mit Namen und einer Endung, die dieser Formatierer kennt. Eine Datei, in der ein Schritt nicht läuft, wird ausgelassen und mit Grund gemeldet.
- Jede Datei behält ihre Kodierung und ihre Zeilenenden; ein neues Zeichen, das die Kodierung nicht aufnehmen kann, erscheint als Änderung, die sich nicht anwenden lässt. Format-Schritte laufen bei Rezept ausführen für eine einzelne Datei nicht.

## [edit.aiContext] Als KI-Kontext kopieren
commands: nks.edit.copyAiContext
Legt die Auswahl, die Datei, die geöffneten Tabs oder den geöffneten Ordner als einen Block in Markdown oder XML in die Zwischenablage, zum Einfügen in einen KI-Assistenten. Es wird nichts gesendet.
1. Wählen Sie Bearbeiten, Als KI-Kontext kopieren und dann, was kopiert wird: die Auswahl (alle Auswahlen), Diese Datei, die Geöffneten Tabs oder den Geöffneten Ordner.
2. Die Vorschau zeigt, wie viele Dateien hineinkommen, die Größe als Markdown und als XML und was mit welchem Grund weggelassen wurde. Wählen Sie Als Markdown kopieren oder Als XML kopieren.
- Jede Datei hat Pfad, Zeilen, Sprache, Kodierung und Revision. Eine Datei mit nicht gespeicherten Änderungen ist markiert, und der Text ist der Text im Editor.
- Weggelassen und benannt: .env-Dateien (ihre Werte werden nie kopiert, auch nicht, wenn sie im Editor sichtbar sind), von Ignorierdateien ausgeschlossene Dateien und Ordner, versteckte und Binärdateien, Dateien über dem Größenlimit beim Durchsuchen des Ordners und nicht lesbare Dateien.
- Eine große Datei im seitenweisen Modus wird nie ganz kopiert: nur ihre Auswahl. Ohne Auswahl wird sie als weggelassen aufgeführt.

## [edit.fragment] Zeichenkette als JSON, SQL oder HTML bearbeiten
commands: nks.edit.fragmentJson, nks.edit.fragmentSql, nks.edit.fragmentHtml, nks.edit.fragmentWriteBack
Eine Zeichenkette in einer PHP- oder JavaScript-Datei öffnet sich in einem eigenen Tab in der gewählten Sprache und geht mit dem Escaping ihrer Art zurück in die Zeichenkette.
1. Setzen Sie den Cursor in die Zeichenkette und wählen Sie Bearbeiten, Zeichenkette als JSON bearbeiten, Zeichenkette als SQL bearbeiten oder Zeichenkette als HTML bearbeiten. Der neue Tab zeigt den Text, für den die Zeichenkette steht, ohne Anführungszeichen und Escapes.
2. Bearbeiten Sie den Text. Wählen Sie Bearbeiten, Fragment in die Zeichenkette zurückschreiben: Der Text wird für die Zeichenkette escaped und ersetzt ihren Inhalt. Der Tab bleibt offen, Sie können erneut schreiben.
- Arten: PHP-Zeichenketten in einfachen und doppelten Anführungszeichen, Heredoc und Nowdoc; JavaScript-Zeichenketten in einfachen und doppelten Anführungszeichen und Templates in Backticks. In einer Zeichenkette in doppelten Anführungszeichen und in einem Heredoc bekommt jedes Dollarzeichen einen Backslash, damit nie versehentlich eine Variable entsteht.
- Mit Meldung abgelehnt: eine Zeichenkette mit Variablen oder Ausdrücken (`$name`, `{$x}`), ein JavaScript-Template mit `${...}`, ein Escape, der kein Text ist (Byte über 127, Oktal) und beim Schreiben eine Zeile, die ein Heredoc oder Nowdoc beenden würde.
- Der Tab folgt der Zeichenkette bei Änderungen der Datei an anderer Stelle. Wurde die Datei nach dem Öffnen des Tabs innerhalb der Zeichenkette geändert, wird nichts ohne Rückfrage überschrieben: Der Dialog bietet Überschreiben oder Abbrechen.
- Wird die Datei geschlossen, bleibt der Fragment-Tab als getrennter Text offen. Der Titel sagt es, und Fragment in die Zeichenkette zurückschreiben schreibt nichts.

## [tools.emmet] Emmet-Abkürzungen
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. Geben Sie in einer HTML- oder CSS-Datei eine Abkürzung ein und drücken Sie Tab. Aus `ul>li*3` wird eine Liste mit drei Einträgen und einem Cursor in jedem.
2. Tab rückt wie bisher ein, wenn vor dem Cursor keine Abkürzung steht, wenn Text markiert ist, wenn der Cursor in einem offenen Tag steht oder wenn die Abkürzung ungültig ist.
3. Extras, Emmet-Abkürzung als Vorschau zeigt, was aus der Abkürzung vor dem Cursor würde (`|` markiert die Cursor), und erweitert sie nach Bestätigung.
4. Extras, Mit Emmet-Abkürzung umschließen (nur HTML) fragt nach einer Abkürzung und umschließt damit jede Markierung. Der Text kommt in das innerste letzte Element; ein nacktes `*` an diesem Element (`ul>li*`) erzeugt ein Element pro Zeile.
5. Jede Erweiterung geht in alle Cursor gleichzeitig und ist ein Schritt für Rückgängig.
- HTML: `name#id.class[attr=value]{text}*3`; `>` Kind, `+` Geschwister, `^` eine Ebene höher, `( )` Gruppe; in Namen, Klassen und Text ist `$` die Nummer der Wiederholung (`$$` füllt mit Nullen, `$@-` zählt abwärts, `$@3` beginnt bei 3). Ein einzelnes Wort wird nur erweitert, wenn es ein HTML-Tag-Name oder ein Kurzname des Plugins ist (`bq`, `btn`, `a:link`, `input:checkbox`, `!` für ein Seitengerüst). In einem Element wird der fehlende Name je nach Eltern zu `li`, `tr`, `td`, `option`, `span` oder `div`.
- CSS: `m10-20` ist `margin: 10px 20px;`, `w100p` ist `width: 100%;`, `c#f00`, `pos:a`, `dn`, ein `!` am Ende fügt `!important` an, `+` verbindet mehrere Deklarationen. Eine ganze Zahl bekommt `px`, ein Bruch `em`; die Buchstaben `p`, `e`, `x`, `r` nach einer Zahl bedeuten `%`, `em`, `ex`, `rem`. Es funktioniert nur innerhalb eines Blocks `{ }`.
- Die Kurznamen sind die eigene Tabelle des Plugins mit gängigen Eigenschaften und Tags, nicht der volle Emmet-Umfang.
- Die Cursor stehen in allen leeren Elementen gleichzeitig (gibt es keines, im ersten leeren Attributwert); ein Sprung von einem Platzhalter zum nächsten fehlt noch.
- Der Emmet-Prozess startet mit dem ersten Tab in einer HTML- oder CSS-Datei.

## [tools.git] Git: Status, Vergleich und Abschnitt vormerken
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk
1. Öffnen Sie eine Datei aus einem Git-Repository und wählen Sie Werkzeuge, Git: Dateistatus. Das Panel zeigt, was `git status` für das ganze Repository meldet: was vorgemerkt und was im Arbeitsbaum geändert ist. Führen Sie den Befehl erneut aus, wenn sich das Repository von außen ändert.
2. Git: Mit Index vergleichen und Git: Mit HEAD vergleichen öffnen einen Vergleichs-Tab des offenen Textes mit seiner Version im Index oder in HEAD. Die Abschnitte sind dieselben wie in `git diff`.
3. Git: Abschnitt am Cursor vormerken schreibt nur die Änderung in den Index, in der der Cursor steht. Die übrigen Änderungen der Datei bleiben nur im Arbeitsbaum. Die Datei auf der Festplatte wird nicht verändert.
4. Hat ein anderes Programm den Index inzwischen geändert, wird nichts geschrieben. Das Plugin liest den Index neu und meldet einen Konflikt; prüfen Sie die Änderungen und wiederholen Sie den Befehl.
- Das Plugin schreibt nur bei Git: Abschnitt am Cursor vormerken in das Repository. Status und Vergleich lesen nur und erneuern nicht einmal die Indexdatei.
- Vorgemerkt wird der im Editor offene Text, gespeichert oder nicht. Eine Datei mit ungelöstem Merge-Konflikt wird nicht vorgemerkt.
- Noch nicht vorhanden: Abschnitt zurücknehmen, Commit und Branches. Eine Datei mit Zeilenenden-Umwandlung (`autocrlf`) kann sich in jeder Zeile vom Index unterscheiden.
- Der Git-Prozess startet mit dem ersten Befehl. Das Programm `git` muss im PATH liegen.

## [tools.lsp] Sprachserver: Definition, Verweise, Umbenennen, Vervollständigung und Diagnose
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.rename, nks.lsp.restart, nks.lsp.stop
1. Öffnen Sie ein Textdokument und wählen Sie Extras, Sprachserver: für dieses Dokument starten. Der Server für die Sprache des Dokuments startet und erhält den Text. Von da an bekommt er jede Änderung, und was er als Fehler meldet, wird unterstrichen.
2. Sprachserver: Zur Definition springen springt zur Definition des Worts am Cursor, auch in eine andere Datei. Sprachserver: Verweise suchen listet alle Stellen im Panel links auf; Enter oder ein Doppelklick auf einen Eintrag öffnet ihn.
3. Sprachserver: Symbol umbenennen fragt nach dem neuen Namen des Worts am Cursor. Die Änderungen des Servers in allen Dateien, samt dem Anlegen, Umbenennen und Löschen von Dateien, das er verlangt, erscheinen gemeinsam in der Änderungsvorschau; was Sie annehmen, wird als ein Schritt angewendet. Eine Datei, die in einem anderen Tab offen ist, muss ebenfalls mit dem ersten Befehl gestartet worden sein, sonst wird das Umbenennen als Ganzes abgelehnt.
4. Strg+J (Bearbeiten, Wort oder Clip vervollständigen) listet auch auf, was der laufende Server am Cursor anbietet. Die Wahl eines solchen Eintrags führt im selben Rückgängig-Schritt auch die weiteren Änderungen aus, die der Server verlangt, etwa das Hinzufügen eines Imports. Eine Antwort, die nach einer Textänderung eintrifft, wird nicht angezeigt.
5. Beendet sich der Server von selbst, meldet das ein Hinweis, und die Unterstreichungen, die er gezeichnet hat, verschwinden. Sprachserver: Neu starten (oder ein anderer Befehl dieser Gruppe) startet ihn erneut und übergibt ihm alle offenen Dokumente so, wie sie jetzt sind. Sprachserver: Beenden fährt ihn herunter.
- Der Editor startet von sich aus keinen Server: Solange Sie keinen Befehl dieser Gruppe wählen, läuft nichts, und der Server weiß nichts von der Datei.
- Große Dateien, die seitenweise angezeigt werden, und Dateien über dem Größenlimit gehen nie an einen Server.
- Der Server wird über seinen Namen im PATH gesucht. Wird er nicht gefunden, nennt ein Hinweis das gesuchte Programm.
- Die Statusleiste zeigt LSP: läuft, LSP: nicht gefunden oder LSP: aus für die Sprache des letzten Befehls.
- Noch nicht vorhanden: die Wahl des Projektordners (verwendet wird der Ordner des ersten Dokuments) und das Umbenennen in Dateien, die nicht UTF-8 sind.

## [edit.complete] Vervollständigung von Wörtern und Clips
commands: nks.edit.complete
1. Drücken Sie Strg+J (Bearbeiten, Wort oder Clip vervollständigen). Die Liste bietet die Wörter an, die das Wort vor dem Cursor fortsetzen, danach die Clips, die zur Sprache des Dokuments passen.
2. Wählen Sie einen Eintrag. Ein Wort ersetzt das Wort vor jedem Cursor; ein Clip wird wie im Thema Clips eingefügt und ersetzt dieses Wort. Es ist ein Schritt für Rückgängig.
- Die Wörter stammen aus allen offenen Text-Tabs. Das Wort, das gerade getippt wird, wird nicht angeboten, und ein Wort wird nur angeboten, wenn es länger ist als das Getippte. Zuerst kommen Wörter des bearbeiteten Dokuments, dann die häufigeren.
- Clips stammen aus der Bibliothek des Themas Clips. Ein Clip ohne Sprache passt zu jedem Dokument; ein Clip mit Sprache nur zu Dokumenten dieser Sprache.
- Die Werte einer Geheimnisdatei (`.env`) werden nie angeboten, solange sie verborgen sind. Sie werden nur in der Datei selbst und nur angezeigt angeboten.
- Die Liste hält eine begrenzte Zahl von Wörtern; Sie engen sie durch Tippen im Feld ein. Einträge eines laufenden Sprachservers kommen dazu, siehe das Thema Sprachserver.

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

## [table.view] Tabellenansicht für CSV und TSV
commands: nks.view.table, nks.table.dialect
Eine CSV- oder TSV-Datei kann als Tabelle angezeigt werden: eine Zeile der Datei je Tabellenzeile, ein Feld je Zelle. Es ist dasselbe Dokument wie in der Textansicht, mit demselben Rückgängig-Verlauf; nichts wird umgewandelt, und das Bearbeiten einer Zelle ändert nur die Bytes dieser Zelle.
1. Strg+K und dann Strg+T (Menü Ansicht, Tabellenansicht umschalten) wechselt zwischen Tabelle und Text. Eine .tsv- oder .tab-Datei wird an Tabulatoren geteilt, jede andere an Kommas.
2. Ansicht, Tabellentrennzeichen und Kopfzeile wählt Komma, Semikolon, Tabulator oder senkrechten Strich als Trennzeichen, ob Anführungszeichen Werte umschließen und ob die erste Zeile die Kopfzeile ist.
- Eine große Datei öffnet sich sofort: Die Zeilen werden im Hintergrund gezählt, und der Bildlaufbereich wächst, während die Datei gelesen wird. Bis das Ende erreicht ist, zeigt die Statusleiste die Zeilenzahl mit einem Plus, etwa 12+.
- Pfeiltasten, Tab, Bild auf und Bild ab, Pos1 und Ende (innerhalb der Zeile), Strg+Pos1 und Strg+Ende bewegen die aktuelle Zelle; Umschalt erweitert die Auswahl. Ein Klick auf eine Spaltenüberschrift wählt die Spalte, ein Klick auf eine Zeilennummer die Zeile.
- Eine Zelle in Anführungszeichen kann Zeilenumbrüche enthalten; die Tabelle zeigt ihre erste Zeile und das Zeichen ↵.
- Kopieren legt die ausgewählten Zellen als durch Tabulatoren getrennten Text in die Zwischenablage; Einfügen schreibt durch Tabulatoren getrennten Text ab der aktuellen Zelle.

## [table.edit] Tabellenansicht: Bearbeiten und Filtern
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Eingabe oder F2 (oder einfach Tippen) bearbeitet die aktuelle Zelle. Eingabe speichert und geht nach unten, Umschalt+Eingabe fügt einen Zeilenumbruch ein, Esc bricht ab. Eine Zelle, die in Anführungszeichen stand, bleibt darin; ein anderer Wert wird nur dann in Anführungszeichen gesetzt, wenn er das Trennzeichen, ein Anführungszeichen oder einen Zeilenumbruch enthält.
2. Ansicht, Zeile einfügen fügt hinter der aktuellen eine leere Zeile ein. Spalte einfügen fragt nach dem Namen der Spalte und fügt sie hinter der aktuellen ein. Zeile löschen und Spalte löschen entfernen die ausgewählten Zeilen oder Spalten. Jede Aktion ist ein Rückgängig-Schritt.
3. Ansicht, Zeilen filtern zeigt nur die Zeilen, deren aktuelle Spalte den Text enthält (Groß- und Kleinschreibung spielt keine Rolle). Filter entfernen zeigt wieder alle Zeilen. Ein Filter ändert die Datei nie, und die Zeilennummern bleiben die der Datei.
- Entf leert die ausgewählten Zellen. Eine kurze Zeile erhält die fehlenden Trennzeichen, wenn eine Zelle hinter ihrem Ende gefüllt wird.
- Ist das Anführungszeichen in Tabellentrennzeichen und Kopfzeile ausgeschaltet, wird ein Wert mit Trennzeichen oder Zeilenumbruch abgelehnt.
- Solange ein Filter aktiv ist, können keine Zeilen hinzugefügt werden.

## [table.copyAs] Zellen als Markdown, SQL, JSON oder HTML kopieren
commands: nks.table.copyAs
Markieren Sie Zellen in der Tabellenansicht und wählen Sie Ansicht, Tabelle kopieren als und dann eine Form. Die Zwischenablage enthält die markierten Spalten und Zeilen in dieser Form.
- Markdown-Tabelle: eine Tabelle mit senkrechten Strichen, mit Kopfzeile (ohne Kopfzeile mit den Spaltenbuchstaben A, B, ...). Sonderzeichen wie der senkrechte Strich, der Backslash, Sternchen, Unterstriche und spitze Klammern erhalten einen Backslash, und ein Zeilenumbruch in einer Zelle wird zu `<br>`.
- SQL INSERT: eine Anweisung pro Zeile, die Werte als Text in einfachen Anführungszeichen (ein Anführungszeichen wird verdoppelt). Die Spaltennamen stammen aus der Kopfzeile. Das Programm fragt nach dem Tabellennamen und schlägt den Dateinamen vor.
- JSON: ein Array von Objekten, benannt nach der Kopfzeile, oder ein Array von Arrays, wenn die Kopfzeile fehlt, einen leeren Namen hat oder einen Namen wiederholt. Alle Werte sind Text.
- HTML-Tabelle: ein table-Element mit Entitäten für `& < > " '`; die Zwischenablage enthält das HTML als Quelltext und als HTML zum Einfügen in ein Dokument.
- Ein verborgener Wert einer .env-Datei wird als Sterne kopiert, nicht als sein Text. Kopieren und Ausschneiden mit Strg+C und Strg+X setzen ebenfalls den Platzhalter ein, und ein Ausschneiden, das einen berührt, löscht nichts.
- Ändert sich die Datei während des Befehls, wird nichts kopiert, und ein Hinweis fordert zum erneuten Ausführen auf.

## [table.region] Ausgewählten Text als Tabelle bearbeiten
commands: nks.table.editRegion
Wählen Sie einen Teil eines Textes aus, etwa einen Block kommagetrennter Zeilen in einer Notiz oder einem Log, und wählen Sie Ansicht, Auswahl als Tabelle bearbeiten.
1. Der Dialog fragt nach dem Trennzeichen (Komma, Semikolon, Tabulator oder senkrechter Strich), ob Anführungszeichen Werte umschließen und ob die erste ausgewählte Zeile die Kopfzeile ist. Die Vorschau zeigt die ersten Zeilen als Zellen.
2. Der ausgewählte Text wird zur Tabelle. Der Rest der Datei bleibt Text: Das Bearbeiten einer Zelle ändert nur die Bytes dieser Zelle, und außerhalb der Auswahl wird nichts gelesen oder geändert.
- Alles aus der Tabellenansicht funktioniert darin: Bewegen, Bearbeiten, Zeilen und Spalten hinzufügen und löschen, Filtern, Kopieren und Einfügen. Die Statusleiste sagt, dass nur der ausgewählte Text die Tabelle ist.
- Am Ende hinzugefügte Zeilen gehören zur Auswahl. Tabellenansicht umschalten kehrt zum Text zurück; das nächste Umschalten zeigt die ganze Datei als Tabelle.

## [compare.text] Texte vergleichen
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts
Zwei Texte werden zeilenweise in einem eigenen Tab verglichen. Gleiche Zeilen stehen auf gleicher Höhe, ein geänderter Block ist auf beiden Seiten getönt und im mittleren Streifen markiert, und die Leiste oben nennt die Zahl der Unterschiede.
1. Mit Zwischenablage vergleichen (Menü Extras oder Kontextmenü des Editors) vergleicht den Tab mit dem Text der Zwischenablage. Ist im Editor Text ausgewählt, wird nur die Auswahl verglichen.
2. Mit Datei vergleichen fragt nach einer Datei und vergleicht den Tab damit. Mit Datenträger vergleichen steht im Menü Datei und vergleicht den Tab mit seiner eigenen Datei.
3. Zwei Texte vergleichen öffnet zwei leere Seiten. Die Schaltflächen Linken Text einfügen und Rechten Text einfügen setzen die Zwischenablage in eine Seite ein.
- Von der Befehlszeile öffnet textida --diff links rechts den Vergleich zweier Dateien: Die linke Datei öffnet sich in einem Tab und kann Blöcke der rechten aufnehmen, die schreibgeschützt ist.
- Die Tab-Seite ist das Dokument selbst: Ändert es sich, wird nach einer kurzen Pause neu verglichen. Zwischenablage, Auswahl und Datei auf dem Datenträger sind schreibgeschützte Seiten.
- Ein anderes Zeilenende (CR, LF, CRLF) macht eine Zeile nicht anders. Leerzeichen und Groß-/Kleinschreibung schon.
- Eine große Datei im seitenweisen Modus wird verglichen, ohne geladen zu werden: Es werden nur die Zeilen auf dem Bildschirm gelesen.
- Jede Seite darf zwischen gemeinsamem Anfang und gemeinsamem Ende bis zu 16 Millionen Zeilen haben; darüber ist die Strecke ein geänderter Block. Bis zu 10 000 geänderte Zeilen werden genau gefunden; bei mehr schreibt die Leiste „nicht minimal“ und geänderte Zeilen werden zu größeren Blöcken zusammengefasst. Das Ergebnis bleibt richtig.

## [compare.navigate] Zwischen Unterschieden wechseln und sie kopieren
commands: nks.compare.next, nks.compare.previous, nks.compare.copyToRight, nks.compare.copyToLeft
1. F7 springt zum nächsten Unterschied, Umschalt+F7 zum vorherigen (auch im Menü Extras). Der aktuelle Unterschied hat einen Rahmen.
2. Im mittleren Streifen hat die erste Zeile jedes Unterschieds zwei Pfeile. Der linke kopiert den Block der rechten Seite nach links, der rechte den Block der linken Seite nach rechts. Dasselbe tun für den aktuellen Unterschied die Schaltflächen über den Seiten und die Befehle Unterschied nach rechts kopieren und nach links kopieren.
3. Ein Kopieren ist eine einzige Änderung des Dokuments, Rückgängig nimmt es in einem Schritt zurück.
- Nur eine Seite, die ein geöffnetes Dokument ist, kann einen Block aufnehmen. Eine Datei auf dem Datenträger, die Zwischenablage und eine Auswahl haben keinen Pfeil.
- Hat sich das Dokument nach dem Vergleich geändert, wird nichts kopiert; es wird neu verglichen und Sie kopieren noch einmal.
- Kann die Kodierung des Ziels die Zeichen des Blocks nicht aufnehmen, wird nichts kopiert und die Statusleiste sagt es.
- Das Mausrad bewegt beide Seiten gemeinsam, Umschalt und Mausrad scrollen zur Seite.

## [view.syncScroll] Synchrones Scrollen
commands: nks.view.syncScroll
Zwei Tabs scrollen zusammen, sodass gleicher Text zweier Dateien nebeneinander bleibt.
1. Zeigen Sie die beiden Dateien nebeneinander: Wählen Sie im Menü eines Tabs In andere Gruppe verschieben, damit jede Datei ihre eigene Gruppe hat.
2. Wählen Sie Ansicht und dann Scrollen synchronisieren. Der aktive Tab und der aktive Tab der nächsten Gruppe folgen einander. Wählen Sie den Befehl noch einmal, um es zu beenden.
- Die Zeilen werden mit dem Vergleich aus dem Thema Texte vergleichen zugeordnet: Die andere Datei scrollt zur entsprechenden Zeile, auch über eingefügte oder entfernte Blöcke hinweg. Solange der Vergleich berechnet wird, gilt dieselbe Zeilennummer.
- Es endet, wenn einer der beiden Tabs geschlossen wird. Hex- und Tabellenansicht nehmen nicht teil.

## [tools.mcp] Der Editor als MCP-Server
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpTools
Ein KI-Assistent, den Sie auf diesem Computer starten (zum Beispiel ein Coding-Agent), kann Textida über das Model Context Protocol nutzen. Er sieht nur die Tabs, die Sie ihm freigeben, und schreibt nie selbst: Jeden Vorschlag sehen Sie als Änderungsvorschau und Sie entscheiden.
1. Wählen Sie Extras, MCP-Server. Der Dialog listet auf, was der Assistent bekommt. Mit Einschalten startet der Server; erneutes Wählen des Befehls schaltet ihn aus und entzieht alle Tabs.
2. Richten Sie den Assistenten so ein, dass er das Programm textida-mcp.exe aus dem Ordner mcp neben dem Editor startet (die Statusleiste zeigt den vollständigen Pfad). Es spricht über eine lokale Pipe mit dem Editor, ein Netzwerkport wird nicht geöffnet.
3. Öffnen Sie einen Tab und wählen Sie Extras, Tab mit MCP teilen. Derselbe Befehl entzieht den Tab wieder. Ein Tab, der nicht geteilt ist, wird nicht einmal aufgelistet.
4. Mit Extras, MCP-Werkzeuge schalten Sie einzelne Werkzeuge ein oder aus. Die Wahl eines Werkzeugs in der Liste ändert seinen Zustand, Escape schließt die Liste.
- Der Assistent liest den Text so, wie er im Editor steht, auch ungespeichert, sucht darin, fragt die Gliederung, die Kodierung und die Zeilenenden ab, findet Zeilen einer Stufe in einem Log beliebiger Größe und liest oder durchsucht Bytes (höchstens 64 KiB pro Lesen).
- Eine Änderung ist ein Vorschlag für eine Revision des Textes. Hat sich der Text inzwischen geändert, wird der Vorschlag abgelehnt und der Assistent muss neu lesen. Den Vorschlag sehen Sie wie andere Änderungen des ganzen Dokuments und wählen, ob er angewendet wird; der Assistent kann nachfragen, wie er ausging.
- Werte in .env-Dateien sieht der Assistent als *. Beim Teilen eines solchen Tabs werden Sie gefragt, ob er die echten Werte sehen darf; die Antwort gilt nur für diesen Tab.
- Das Entziehen eines Tabs oder das Ausschalten des Servers stoppt die laufenden Anfragen. Auch das erneute Laden einer Datei entzieht sie.
- Das Programm für den Assistenten spricht die aktuelle MCP-Version (2026-07-28) und für Assistenten, die noch mit initialize beginnen, die Version 2025-11-25.

## [tools.agent] Agent über ACP
commands: nks.agent.start, nks.agent.panel, nks.agent.shareTab, nks.agent.cancel, nks.agent.stop
1. Werkzeuge, Agent starten... startet einen Coding-Agenten, der das Agent Client Protocol (ACP) spricht: Gemini CLI, Codex, Claude, Junie, GitHub Copilot oder OpenCode. Der Dialog zeigt den genauen Befehl und den Ordner, in dem er läuft; ohne Bestätigung startet nichts. Der Agent muss installiert und im PATH zu finden sein. Ein Programm, das nur als Stapeldatei (.cmd, .bat) vorliegt, wird nicht gestartet.
2. Der Agent-Bereich unter dem Editor zeigt die Unterhaltung. Nachricht tippen und Eingabe drücken. Zug des Agenten abbrechen (oder die Schaltfläche Zug abbrechen) schickt dem Agenten den Abbruch und beantwortet seine offenen Berechtigungsfragen mit „abgebrochen“; Agent beenden beendet den Prozess.
3. Der Agent liest nur die Tabs, die Sie teilen: Tab mit Agent teilen. Er bekommt den Text, wie er im Editor steht, samt ungespeicherten Änderungen; Werte von .env-Dateien bleiben verborgen. Beim Beenden des Agenten werden alle geteilten Tabs zurückgenommen.
4. Schreibt der Agent in eine geteilte Datei, erscheint die Änderung abschnittsweise in der Änderungsprüfung (Tab Suchergebnisse) und wird erst geschrieben, wenn Sie sie anwenden. Lehnen Sie sie ab, bekommt der Agent einen Fehler. Eine Datei, die nicht geteilt oder nicht geöffnet ist, wird nicht über den Editor geschrieben.
5. Bittet der Agent um Erlaubnis, zeigt ein Dialog die Möglichkeiten, die der Agent anbietet. Abbrechen oder Schließen des Dialogs antwortet „abgebrochen“.
- Wo die Garantie endet: Der Editor steuert nur, was über ihn läuft. Die eigenen Werkzeuge des Agenten können Dateien ändern, Programme starten und das Netzwerk nutzen, ohne den Editor, und der Editor stellt bei keinem Agenten den Berechtigungsmodus ein (jeder behält seinen eigenen Standard). Was der Agent darf, stellen Sie in den Einstellungen des Agenten selbst ein.
- Jeder Agent steht als „nicht überprüft“ in der Liste: Noch kein Lauf mit dem echten Programm hat die Testszenarien bestanden. Ein Zug, in dem der Agent zu lange weder etwas sagt noch fragt, endet als Zeitüberschreitung. Die Zeitlimits und die Größe einer an den Agenten übergebenen Datei sind vorläufig.
## [tools.mcpAgents] MCP-Server der Agenten
commands: nks.tools.mcpServers
Eine Liste der MCP-Server, mit denen Ihre Coding-Agenten eingerichtet sind. Textida liest Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) und OpenCode (opencode.json, opencode.jsonc). Projektdateien werden im Ordner der aktiven Datei und in den übergeordneten Ordnern gesucht. Textida liest diese Dateien nur und schreibt nie hinein.
1. Wählen Sie Extras, MCP-Server der Agenten. Jede Zeile zeigt den Server, den Agenten und die Datei, aus der er stammt, sowie Befehl oder Adresse mit verborgenen Geheimnissen.
2. Wählen Sie einen Server und dann Ausprobieren. Ein Dialog zeigt den genauen Befehl, den Ordner und die Variablen mit verborgenen Werten (***). Nichts startet, bevor Sie Starten drücken.
3. Nach Starten führt Textida das Programm aus, fragt es nach seinen Tools (MCP-Versionen 2026-07-28 und 2025-11-25) und beendet es. Das Ergebnis nennt die Zahl der Tools oder den Fehler und die letzten Zeilen, die das Programm auf die Fehlerausgabe geschrieben hat. Dieselben Zeilen stehen in der Ausgabe Aufgaben.
4. Mit Eintrag für einen anderen Agenten umwandeln erscheint der Eintrag im Format dieses Agenten in einem neuen Tab. Der Ausschnitt wird zuerst gegen dieses Format geprüft. Werte von Variablen und Headern sind durch *** ersetzt; ergänzen Sie sie, bevor Sie den Ausschnitt in die Datei dieses Agenten einfügen.
- ${NAME} in einem Befehl, Argument oder einer Variable wird aus der Umgebung von Textida ersetzt, ${NAME:-text} liefert einen Standardwert. Ein dort nicht gesetzter Name wird in der Windows-Anmeldeinformationsverwaltung unter textida:mcp:env:NAME gesucht. Ein Name ohne Wert ist ein Fehler, und nichts startet.
- Ausprobieren lässt sich nur ein Server, der per Befehl gestartet wird. Ein Server mit Adresse wird aufgelistet und lässt sich umwandeln, wird aber nicht gestartet. npx und npm werden als node mit dem Skript des Node.js-Shims gestartet, weil Textida keine Batch-Dateien startet.
- Geheimnisse sind in der Liste, im Dialog, in der Ausgabe und in den Ausschnitten verborgen, auch wenn eines in einem Argument oder in der Fehlerausgabe des Programms vorkommt. Werte unter 4 Zeichen werden im Text nicht gesucht.

## [tools.shell] Explorer-Menü und Dateizuordnungen
commands: nks.tools.shellMenuAdd, nks.tools.shellAssociate, nks.tools.shellRemove
Nur Windows. Textida kann sich in das Kontextmenü des Explorers Ihres Benutzers und in die Liste Öffnen mit eintragen. Dafür sind keine Administratorrechte nötig, und es werden nur Schlüssel von Textida unter HKEY_CURRENT_USER\Software\Classes geschrieben. Schlüssel anderer Programme, auch von PSPad, werden weder gelesen noch geändert. Beide Befehle funktionieren in der installierten Textida.exe.
1. Wählen Sie Extras, Textida zum Explorer-Menü hinzufügen. Dateien erhalten den Eintrag Mit Textida öffnen; er startet Textida mit der Datei oder gibt die Datei an die bereits laufende Textida weiter. Unter Windows 11 steht der Eintrag unter Weitere Optionen anzeigen.
2. Wählen Sie Extras, Dateizuordnungen und dann Endungen. Die Wahl einer Endung in der Liste fügt Textida in deren Liste Öffnen mit hinzu oder entfernt es; Escape schließt die Liste. Solange Sie keine Endung wählen, wird nichts zugeordnet.
3. Damit Textida das Standardprogramm einer Endung wird, nutzen Sie im Explorer Öffnen mit, Andere App auswählen. Diese Wahl verwaltet Windows selbst.
4. Wählen Sie Extras, Textida aus dem Explorer entfernen: Der Menüeintrag und alle Zuordnungen, die Textida angelegt hat, werden gelöscht. Das Deinstallationsprogramm tut dasselbe.
- Der Text des Menüeintrags steht in der Sprache von Textida zum Zeitpunkt des Hinzufügens. Fügen Sie ihn nach einem Sprachwechsel erneut hinzu.
- Wird Textida.exe in einen anderen Ordner verschoben, bleibt der alte Pfad in den Einträgen. Fügen Sie den Eintrag erneut hinzu oder entfernen Sie ihn vorher.

## [help.updates] Updates und Rückkehr zur vorherigen Version
commands: nks.help.updateCheck, nks.help.updateReturn
Nur Windows, in der installierten Textida (der mit Setup.ps1 angelegte Ordner). Textida prüft nie von selbst, außer Sie schalten die Einstellung Beim Start nach Updates suchen ein; sie ist standardmäßig aus. Eine Prüfung ist eine einzige Anfrage nach einer kleinen signierten Datei; sonst wird nichts gesendet, keine Kennung von Ihnen oder Ihrem Computer.
1. Wählen Sie Hilfe, Nach Updates suchen. Wird eine neuere Version angeboten, fragt Textida, bevor es etwas tut.
2. Installieren und neu starten lädt das Paket herunter, prüft seine Prüfsumme anhand der signierten Datei und entpackt es neben der aktuellen Version. Erst dann schließt Textida, die Ordner werden getauscht und Textida startet mit Ihren Tabs neu. Ungespeicherter Text bleibt im Journal; bei ausgeschaltetem Journal speichern oder schließen Sie die Dokumente zuerst.
3. Stimmt etwas nicht (Signatur, Prüfsumme, ein Paket, das nicht passt), wird nichts installiert und die aktuelle Version läuft weiter.
4. Hilfe, Zur vorherigen Version zurückkehren bringt Sie zu der Version, die vor dem letzten Update installiert war. Die neuere Version ist dann die, zu der Sie zurückkehren können.
- Die Einstellungen von Textida und das Journal bleiben, ein portabler Profilordner wird übernommen.
- Dieser Build sucht nur, wenn er eine Update-Adresse und einen Signaturschlüssel hat; ohne beide sagt er das.

## [review.external] Änderungen von außen prüfen
commands: nks.review.open, nks.review.next, nks.review.previous
Ändert ein anderes Programm (Skript, Assistent, Build-Werkzeug) die Datei eines Tabs ohne ungespeicherte Änderungen, bietet die Leiste Änderungen prüfen an.
1. Wählen Sie Änderungen prüfen. Der Tab übernimmt den neuen Inhalt, der frühere Inhalt bleibt im lokalen Verlauf und wird während der Prüfung nicht gelöscht, und der Tab Suchergebnisse listet die Änderungen zeilenweise auf.
2. Haken Sie die Änderungen an, die bleiben sollen. Nicht angehakte, oder die ganze Datei bei nicht angehaktem Feld, gehen mit Anwenden auf den früheren Inhalt zurück.
3. Anwenden schreibt den zurückgesetzten Text als einen Rückgängig-Schritt in den Tab und speichert die Datei; Bytes außerhalb der zurückgesetzten Änderungen bleiben unverändert.
- Der Tab zeigt eine Marke mit der Zahl der wartenden Änderungen. Über Datei und Änderungen von außen prüfen öffnen Sie die Liste erneut; sie kehrt auch nach einem Neustart zurück.
- Alt+F7 und Alt+Umschalt+F7 springen zur nächsten und vorherigen Änderung von außen.
- Tippen Sie während der Prüfung im Tab, werden die Änderungen aus dem neuen Text erneut angezeigt.
- Dateien mit geheimen Werten (.env) und Dateien mit ungespeicherten Änderungen behalten die ältere Leiste: Neu laden, Meine behalten, Vergleichen.
- Mit Änderungen von außen ohne Prüfung behalten in den Einstellungen übernimmt der Tab den neuen Inhalt sofort. Standardmäßig ist es aus.

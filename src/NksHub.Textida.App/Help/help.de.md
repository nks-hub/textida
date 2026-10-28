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
1. Wählen Sie Datei und dann Remotedateien und dann Remote-Verbindungen. Die Liste zeigt die gespeicherten Verbindungen.
2. Wählen Sie Neue Verbindung und beantworten Sie die Fragen: Name, Protokoll, Server, Port, Benutzer und Passwort. Bei SFTP können Sie statt eines Passworts eine Datei mit dem privaten Schlüssel angeben.
3. Wählen Sie eine Verbindung, um eine Datei daraus zu öffnen oder sie mit dem gespeicherten Passwort zu löschen.
- Üblich sind FTPS und SFTP. Einfaches FTP sendet Passwort und Dateien unverschlüsselt und wird erst benutzt, wenn Sie es für diese Verbindung ausdrücklich erlauben.
- Bei der ersten Verbindung zeigt das Programm die Identität des Servers (einen SSH-Hostschlüssel oder ein TLS-Zertifikat) und fragt, ob Sie ihr vertrauen. Vergleichen Sie sie mit der, die Ihnen der Serveradministrator gegeben hat.
- Ändert sich die Identität eines Servers, dem Sie vertraut haben, verbindet sich das Programm erst, wenn Sie die neue bestätigen. Lesen Sie vorher die Warnung.
- Das Speichern von Passwörtern funktioniert unter Windows (Anmeldeinformationsverwaltung). Unter macOS und Linux wird der Anmeldeinformationsspeicher noch nicht unterstützt; das Programm sagt das, statt das Passwort anderswo abzulegen.

## [remote.open] Remote-Datei öffnen
commands: nks.remote.open
1. Wählen Sie Datei und dann Remotedateien und dann Remote-Datei öffnen und wählen Sie eine Verbindung.
2. Geben Sie den Pfad auf dem Server ein. Ein Ordnerpfad zeigt seinen Inhalt: Wählen Sie einen Ordner, um hineinzugehen, oder eine Datei, um sie zu öffnen.
3. Die Datei wird auf diesen Computer kopiert und in einem Tab geöffnet. Bearbeiten und speichern Sie wie gewohnt: Speichern ändert nur die Kopie auf diesem Computer.
- Die Leiste über dem Text sagt, ob die Kopie dem Stand auf dem Server entspricht. Der Editor selbst arbeitet ohne Verbindung.
- Eine Remote-Datei, die schon eine Kopie hat, wird aus der Kopie geöffnet, auch mit Änderungen, die Sie noch nicht gesendet haben. Die Serverversion wird nie über Ihre Arbeit geladen.
- Das Protokoll der Übertragungen steht im Tab Remote des Ausgabefensters.

## [remote.upload] Auf Server hochladen
commands: nks.remote.upload
1. Wählen Sie Datei und dann Remotedateien und dann Auf Server hochladen. Ein geänderter Tab wird zuerst gespeichert.
2. Bevor das Programm etwas sendet, sieht es sich die Datei auf dem Server an. Ist es noch die Version, die Sie geöffnet haben, wird der neue Inhalt unter einem vorläufigen Namen gesendet und ersetzt erst dann die alte Datei. Ein misslungener oder unterbrochener Upload lässt die alte Version auf dem Server.
3. Hat sich die Datei auf dem Server inzwischen geändert, wird nichts gesendet. Wählen Sie Unterschiede zeigen, sehen Sie die Serverversion neben Ihrer, übernehmen Sie in Ihre Seite, was Sie brauchen, speichern Sie und laden Sie erneut hoch. Trotzdem hochladen ersetzt genau die Serverversion, die Sie gesehen haben.
- Die Einstellung Prüfung der Serverversion legt fest, wie eine Änderung erkannt wird: stat vergleicht Größe und Änderungszeit, content holt die Serverdatei immer und vergleicht sie; es erkennt auch eine Änderung in derselben Sekunde und mit gleicher Größe.
- Grenze: Prüfung und Austausch sind zwei Schritte, weil FTP und SFTP kein bedingtes Schreiben kennen. Eine Änderung, die jemand in diesem letzten Moment macht, wird nicht bemerkt. Die Prüfung läuft erst nach dem vollständigen Upload, der Moment ist also kurz.
- Bricht die Verbindung beim Hochladen ab, versucht das Programm es einige Male mit wachsenden Pausen erneut. Jeder Versuch prüft zuerst wieder die Serverversion, und eine teilweise gesendete temporäre Datei wird erst fortgesetzt, wenn ihr Inhalt mit Ihrer Datei übereinstimmt. Verweigert der Server das Umbenennen, das die Datei an ihren Platz setzt, wird die alte Datei beiseitegelegt, die neue verschoben und bei einem Fehler die alte zurückgeholt.

## [remote.cancel] Übertragung abbrechen
commands: nks.remote.cancel
Wählen Sie Datei, dann Remote-Dateien, dann Übertragung abbrechen. Das laufende Hochladen stoppt, der Server behält seine vorherige Version, und eine teilweise gesendete temporäre Datei setzt der nächste Upload fort.

## [remote.check] Serverversion prüfen
commands: nks.remote.check
Wählen Sie Datei und dann Remotedateien und dann Serverversion prüfen. Die Leiste sagt: gleich wie auf dem Server, hier geändert und nicht gesendet, auf dem Server geändert, an beiden Stellen geändert oder die Datei gibt es auf dem Server nicht mehr. Es wird nichts geladen oder geändert.

## [remote.unsent] Nicht gesendete Remote-Änderungen
commands: nks.remote.unsent
Änderungen an einer Remote-Datei werden zuerst auf diesem Computer gespeichert. Die Liste zeigt die Remote-Dateien, deren Kopie geändert und nicht gesendet wurde, auch nach dem Schließen oder einem Absturz des Programms. Wählen Sie eine aus, dann öffnet sich die Kopie; die Leiste bietet Hochladen an.
- Das Bearbeiten braucht keine Verbindung. Nicht gesendete Änderungen bleiben, bis Sie sie hochladen.

## [remote.cache] Cache mit dem Server abgleichen
commands: nks.remote.pairCache
Die Kopien von Remote-Dateien bleiben auf diesem Computer mit der Serverversion, aus der sie entstanden sind, auch nach dem Schließen des Programms.
1. Wählen Sie Datei und dann Remotedateien und dann Cache mit dem Server abgleichen, und wählen Sie eine Verbindung.
2. Das Programm verbindet sich und vergleicht jede Kopie dieser Verbindung anhand der gespeicherten Version mit dem Server. Die Liste sagt zu jeder Datei: gleich wie auf dem Server, hier geändert und nicht gesendet, auf dem Server geändert, an beiden Stellen geändert, oder nicht mehr auf dem Server.
3. Wählen Sie eine Datei, dann öffnet sich ihre Kopie. Die Leiste prüft sie noch einmal und bietet Hochladen, Unterschiede zeigen oder Vom Server neu laden an.
- Nichts wird heruntergeladen oder überschrieben. Die Liste bietet nur Verbindungen an, die Kopien haben.

## [source.open] Aus einer Plugin-Quelle öffnen
commands: nks.source.open
1. Wählen Sie Datei und dann Aus Quelle öffnen. Die Liste zeigt die Quellen, die Plugins anbieten, und was jede kann: ob sie schreibt, ob eine geänderte Datei geschützt ist und ob sie Ordner auflistet.
2. Wählen Sie eine Quelle und geben Sie die Adresse ein, die mit dem Schema der Quelle beginnt.
3. Der Text öffnet sich in einem Tab. Die Leiste darüber zeigt die Quelle, die Adresse mit durch *** ersetzten Geheimnissen und die Revision, in der der Text gelesen wurde.
- Speichern schreibt den Text nur zurück, wenn die Datei an der Quelle noch in dieser Revision ist. Wurde sie geändert, wird nichts geschrieben und Ihr Text bleibt im Tab. Unterschiede zeigen vergleicht, Überschreiben ersetzt genau die gefundene Version, Abbrechen lässt alles, wie es ist.
- Eine Quelle, die die Revision nicht in einem Schritt prüfen kann, sagt das, und das Programm warnt vor jedem Speichern: Es prüft zuerst die Revision und schreibt dann, eine Änderung dazwischen wird ohne Hinweis überschrieben.
- Ist die Quelle nicht erreichbar, öffnet sich der Tab aus der Kopie, die das Programm im Speicher hält, und zeigt Offline. Aus Quelle neu laden versucht es erneut und ersetzt nie ungespeicherte Änderungen.
- Adressen mit Passwort, Token oder Schlüssel werden in der Leiste, in den zuletzt geöffneten Dateien und in den Protokollen mit *** gezeigt. Die zuletzt geöffneten Dateien behalten nur die maskierte Adresse; eine Quelle, die das Geheimnis in der Adresse braucht, fragt erneut danach.
- Grenze: Ein Geheimnis im Pfad einer Adresse wird nicht erkannt und bleibt sichtbar. Speichern unter schreibt eine Kopie auf die Festplatte und beendet die Verbindung zur Quelle.
- Eine Quelle, die suchen kann (die Liste sagt es), nimmt neben einer Adresse auch Suchbegriffe an. Suchbegriffe ergeben eine Liste der gefundenen Seiten, und die Auswahl öffnet eine Seite; eine Adresse, die mit dem Schema der Quelle beginnt, öffnet sich sofort.
- Die Ausgabe Remote nennt zu jeder geöffneten und gespeicherten Datei, welchen Adapter und welchen Dienst die Quelle angegeben hat, zum Beispiel: Adapter 1.0.0, Dienst NKS IS API v1.
- NKS-IS-Wiki (nksis://wiki/Seite): Beim ersten Mal fragt das Programm nach der Adresse des Wikis und nach einem API-Schlüssel. Beides wird im Anmeldeinformationsspeicher des Systems abgelegt, nie in einer Datei. Ein Schlüssel, den das Wiki ablehnt, wird verworfen und Sie werden sofort nach einem neuen gefragt; wird auch dieser abgelehnt, endet der Aufruf mit einer Meldung. Das Wiki kann beim Schreiben die Revision nicht prüfen, deshalb warnt das Programm vor jedem Speichern, wie oben beschrieben. Eine Seite, die es noch nicht gibt, lässt sich von hier aus nicht anlegen.

## [plugins.virtual] Tabs, die ein Plugin liefert
Ein Plugin kann einen eigenen Tab mit Daten öffnen, die es selbst hält: ein Gesprächsprotokoll, eine erzeugte Datei, eine Auflistung oder einen Vergleich. Ein solcher Tab hat keine Datei. Er trägt ein Kennzeichen („virtuell · schreibgeschützt“ oder „virtuell · bearbeitbar“) und eine Leiste, die das Plugin und die Adresse nennt, die es ihm gegeben hat (bei einer Adresse mit Passwort oder Token stehen an deren Stelle ***).
1. In einem schreibgeschützten Tab kann man suchen, markieren und kopieren, und er hat eine Gliederung wie jede Datei seiner Sprache, aber man kann weder hineinschreiben noch ihn speichern. Speichern und Speichern unter sagen das in der Statusleiste.
2. In einen bearbeitbaren Tab kann man schreiben, mit Rückgängig und Wiederholen. Das Plugin erfährt jede Änderung von selbst. Speichern (auch wenn Sie den Tab schließen und Speichern wählen) schickt den Text an das Plugin, nie in eine Datei; lehnt das Plugin ab oder antwortet nicht rechtzeitig, bleibt der Tab ungespeichert und die Statusleiste nennt den Grund. Speichern unter ist nicht verfügbar.
3. Das Plugin kann den Text seines Tabs ersetzen. Dabei geht auch verloren, was Sie geschrieben und nicht gespeichert haben, und der Rückgängig-Verlauf des Tabs endet; ein Plugin, das das vermeiden will, nennt die Revision, die es zuletzt gesehen hat, und eine Änderung von Ihnen dazwischen stoppt dann das Ersetzen.
4. Schließt das Plugin seinen Tab, endet es oder wird es abgeschaltet, schließt sich der Tab ohne Rückfrage und die Statusleiste sagt es. Nicht gespeicherter Text geht verloren.
- Ein Tab eines Plugins kommt nicht in die Sitzung, ins Journal oder in die zuletzt geöffneten Dateien.
- Werte in einem .env-Tab sind wie in einer Datei verborgen. Das Plugin sieht an ihrer Stelle Sterne, außer Sie haben es erlaubt.
- Die Zahl solcher Tabs pro Plugin, ihre Größe und die Wartezeit beim Speichern sind vorläufig.

## [remote.backup] Verbindungen sichern
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Wählen Sie Datei und dann Remotedateien und dann Verbindungen exportieren und benennen Sie die Datei. Sie listet die Verbindungen: Name, Protokoll, Server, Port, Benutzer und die vertrauenswürdige Serveridentität.
2. Wählen Sie Datei und dann Remotedateien und dann Verbindungen importieren, um eine solche Datei auf einem anderen Computer oder nach einer Neuinstallation zu lesen.
- Die Datei enthält keine Passwörter, Passphrasen oder Token und kann daher weitergegeben werden. Wählen Sie nach dem Import eine Verbindung und dann Passwort setzen.
- Eine importierte Verbindung ersetzt eine Verbindung mit derselben internen Kennung.
- Die ganze Konfiguration samt Verbindungen übertragen Sie mit Einstellungen und dann Konfiguration sichern (Thema Konfiguration sichern).

## [remote.proxy] Netzwerk-Proxy
commands: nks.remote.proxy
1. Wählen Sie Datei und dann Remotedateien und dann Netzwerk-Proxy und wählen Sie HTTP, SOCKS4, SOCKS5 oder keinen Proxy.
2. Geben Sie Server und Port ein, bei Bedarf auch Benutzername und Passwort des Proxys. Das Passwort kommt in den Anmeldeinformationsspeicher.
- Der Proxy wird für FTPS, SFTP, die Update-Prüfung und die Anfragen verwendet, die Plugins über den Editor senden (Berechtigung network). Eine Verbindung mit eigenem Proxy behält ihn.
- Ein Plugin, das selbst eine Verbindung öffnet, umgeht den Proxy. Abfragen bei Paketregistern verwenden den Proxy, aber keinen mit Benutzernamen.
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
1. Wählen Sie Datei und dann Verlauf und dann Lokaler Verlauf. Die Liste zeigt die Revisionen der Datei im Tab, die neueste zuerst, mit Anlass, Größe und Bezeichnung.
2. Wählen Sie eine Revision. Vergleichen öffnet sie in Ihrem Vergleichsprogramm (dem von Mit Datenträger vergleichen) links und den Tab rechts. Datei wiederherstellen setzt die ganze Revision in den Tab. Auswahl wiederherstellen ersetzt nur die ausgewählten Zeilen durch die passenden Zeilen der Revision. Bezeichnung gibt der Revision einen Namen.
- Das Wiederherstellen ist eine Änderung im Tab; Rückgängig nimmt sie zurück, und nichts wird geschrieben, bis Sie speichern. Der Text, der im Tab stand, bleibt als Revision erhalten.
- Die Revisionen liegen im Profilordner, ein Update des Programms löscht sie also nicht.
- Dateien mit geheimen Werten, etwa .env, werden nicht gespeichert, solange Sie in den Einstellungen nicht Verlauf von Dateien mit geheimen Werten führen einschalten.
- Grenzen (Revisionen pro Datei, Tage, MiB pro Datei, MiB insgesamt) stellen Sie in den Einstellungen ein; 0 heißt keine Grenze. Die neueste Revision einer Datei wird nie gelöscht.
- Ist der Datenträger voll, meldet das die Statusleiste, und keine Revision wird beschädigt.
- Eine angeheftete Revision löschen die Grenzen nie; Anheften im Revisionsmenü hält sie, Lösen gibt sie frei. Eine Datei über der Größengrenze bekommt keine Revisionen, und die Statusleiste meldet das. Alte Revisionen werden im Hintergrund aufgeräumt.

## [file.historyMark] Revision markieren
commands: nks.file.historyMark
1. Wählen Sie Datei und dann Verlauf und dann Revision markieren und geben Sie eine Bezeichnung ein, zum Beispiel Vor dem Refactoring.
2. Der aktuelle Text des Tabs wird als Revision mit dieser Bezeichnung gespeichert. Enthält die neueste Revision schon denselben Text, erhält diese die Bezeichnung.
- Die Revision finden Sie später im Lokalen Verlauf über die Bezeichnung.

## [file.historySearch] Im Verlauf suchen
commands: nks.file.historySearch
1. Wählen Sie Datei und dann Verlauf und dann Im Verlauf suchen und geben Sie den Text ein.
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

## [file.openSpecial] Spezielles Öffnen
commands: nks.open.special, nks.open.hex, nks.open.withEncoding, nks.format.reread, nks.open.readOnly, nks.open.newCopy, nks.file.openPathAtCaret, nks.search.relatedFile, nks.open.containingFolder, nks.open.defaultApp, nks.open.lastClosed
1. Wählen Sie Datei und dann Spezielles Öffnen oder starten Sie Spezielles Öffnen in der Palette. Im Dateipanel klicken Sie mit der rechten Maustaste auf eine Datei und wählen Spezielles Öffnen.
2. Wählen Sie, wie die Datei geöffnet wird. Aus dem Panel wird die gewählte Datei verwendet, sonst die Datei des aktuellen Tabs; gibt es keine, wählen Sie eine aus.
- Datei in Hex öffnen, Mit Kodierung öffnen (die Kodierungsliste erscheint nach dem Öffnen), Schreibgeschützt öffnen (der Tab speichert nicht), Als neue Kopie öffnen (ein ungespeichertes Dokument mit dem Text der Datei).
- Mit Kodierung neu öffnen, Pfad am Cursor öffnen und Zugehörige Datei arbeiten mit dem aktuellen Tab.
- Ordner der Datei öffnen zeigt den Ordner im Dateimanager des Systems; Mit Standard-App öffnen übergibt die Datei dem Programm, das das System dafür festgelegt hat.
- Zuletzt geschlossenen Tab erneut öffnen öffnet die Datei des zuletzt geschlossenen Tabs; geschlossene Dateien, die nicht mehr existieren, werden übersprungen.
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
- Ausschneiden und Einfügen gehen nicht in einem schreibgeschützten Dokument. Im Seitenmodus gehen alle drei (Thema Große Dateien); eine Auswahl über dem dortigen Kopierlimit (256 MiB, vorläufiger Wert) wird nicht kopiert, und die Statusleiste sagt es.

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

## [edit.specialPaste] Als HTML und aus einer Datei einfügen
commands: nks.edit.pasteAsHtml, nks.edit.insertFromFile
- Als HTML einfügen (Bearbeiten, Einfügen) fügt den HTML-Quelltext ein, den ein Browser oder eine Textverarbeitung beim Kopieren von formatiertem Text in die Zwischenablage gelegt hat. Er wird als Text eingefügt, wie bei Strg+V, als ein Rückgängig-Schritt. Enthält die Zwischenablage kein HTML, sagt es die Statusleiste.
- Aus Datei einfügen (Bearbeiten, Einfügen) setzt den Text der gewählten Datei in jeden Cursor, als ein Rückgängig-Schritt. Die Kodierung der Datei wird wie beim Öffnen erkannt, und ihre Zeilenenden werden zu denen des Dokuments. Eine Datei über 16 MiB wird nicht eingefügt.
- Beide funktionieren nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus (Thema Große Dateien).

## [edit.insertLines] In Zeilen einfügen
commands: nks.edit.insertIntoLines
Der Befehl schreibt Text, Nummern oder Aufzählungszeichen in die Zeilen der Auswahl, oder in alle Zeilen, wenn nur ein Cursor da ist.
1. Wählen Sie Bearbeiten, Einfügen, In Zeilen einfügen. Die Vorschau zeigt die ersten Zeilen so, wie sie aussehen werden.
2. Wählen Sie Text, Nummern oder Aufzählungszeichen und wohin geschrieben wird: Anfang jeder Zeile, Ende jeder Zeile, vor eine Spalte oder in jeden Cursor.
3. Drücken Sie OK. Das Ergebnis ist ein Rückgängig-Schritt.
- Nummern: Erste Nummer, Stellen (die Nummer wird links mit Nullen auf diese Breite aufgefüllt) und Trenner, der zwischen Nummer und Zeilentext steht (am Zeilenanfang nach der Nummer, am Zeilenende vor der Nummer). In jeden Cursor wird nur die Nummer geschrieben, drei Cursor bekommen also 1, 2 und 3.
- Anfang jeder Zeile schreibt an den Zeilenanfang; Einrückung überspringen schreibt vor das erste sichtbare Zeichen. Vor Spalte zählt Zeichen ab 1, und eine kürzere Zeile bekommt den Text an ihrem Ende. Leere Zeilen überspringen überspringt auch Zeilen nur mit Leerzeichen, sie verbrauchen keine Nummer.
- Die Zeilenenden des Dokuments ändern sich nicht. Es funktioniert nicht in einem schreibgeschützten Dokument und nicht im seitenweisen Modus (Thema Große Dateien).
- Letzten Befehl wiederholen führt ihn mit denselben Einstellungen erneut aus. Im Profil PSPad ist es Alt+I.

## [edit.sort] Zeilen sortieren
commands: nks.edit.sortLines
1. Markieren Sie die Zeilen (oder setzen Sie den Cursor irgendwohin, um alle Zeilen zu sortieren) und wählen Sie Bearbeiten, Umwandlungen und Rezepte, Zeilen sortieren.
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
commands: nks.edit.reformatParagraph, nks.edit.reformatShortLines, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
Diese Befehle stehen im Menü Format. Sie wirken auf die markierten Zeilen, oder auf alle Zeilen, wenn nur ein Cursor da ist (Absatz neu umbrechen: auf den Absatz mit dem Cursor). Jeder ist ein Rückgängig-Schritt.
- Absatz neu umbrechen bricht den Text an der Spalte der Einstellung Spalte des rechten Rands um, verbindet die Zeilen eines Absatzes und entfernt überzählige Leerzeichen. Absätze trennt eine Leerzeile, und jede Zeile eines Absatzes bekommt die Einrückung der ersten. Ist kein rechter Rand eingestellt, sagt es die Statusleiste.
- Absatz neu umbrechen (endet an kurzer Zeile) macht dasselbe, aber ein Absatz endet auch nach einer Zeile, die kürzer als die Hälfte des rechten Rands ist, sodass sie nicht mit der nächsten Zeile verbunden wird. Das passt zu Text ohne Leerzeilen zwischen den Absätzen.
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
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run, nks.edit.cursorLeft, nks.edit.cursorLeftSelect, nks.edit.cursorRight, nks.edit.cursorRightSelect, nks.edit.cursorWordLeft, nks.edit.cursorWordLeftSelect, nks.edit.cursorWordRight, nks.edit.cursorWordRightSelect, nks.edit.cursorUp, nks.edit.cursorUpSelect, nks.edit.cursorDown, nks.edit.cursorDownSelect, nks.edit.cursorLineStart, nks.edit.cursorLineStartSelect, nks.edit.cursorLineEnd, nks.edit.cursorLineEndSelect, nks.edit.cursorDocumentStart, nks.edit.cursorDocumentStartSelect, nks.edit.cursorDocumentEnd, nks.edit.cursorDocumentEndSelect, nks.edit.deleteLeft, nks.edit.deleteRight, nks.edit.deleteWordLeft, nks.edit.deleteWordRight, nks.macro.rename, nks.macro.delete
1. Extras, Makros, Makro aufzeichnen, startet die Aufzeichnung. Arbeiten Sie mit Befehlen und Tippen. Extras, Makros, Makroaufzeichnung beenden, beendet sie.
2. Extras, Makros, Letztes Makro abspielen, spielt es erneut über den aktuellen Auswahlen und Einfügemarken ab.
3. Extras, Makros, Letztes Makro speichern unter, legt es unter einem Namen im Ordner macros des Profils ab. Extras, Makros, Gespeichertes Makro ausführen, listet die gespeicherten auf und spielt das gewählte ab.
4. Extras, Makros, Gespeichertes Makro umbenennen, listet die gespeicherten auf und benennt das gewählte um; ein Name, den ein anderes Makro schon hat, wird abgelehnt. Extras, Makros, Gespeichertes Makro löschen, fragt nach und löscht das gewählte endgültig.
- Ein Makro merkt sich Befehle, keine Tasten: Zeile duplizieren, Groß-/Kleinschreibung, Nächstes Vorkommen hinzufügen und Ähnliches, Ausschneiden, Kopieren, Einfügen sowie getippten Text. Mit mehreren Einfügemarken tut es dasselbe wie die Schritte von Hand.
- Beim Aufzeichnen werden auch die Pfeiltasten, Pos1, Ende, Strg+Pos1, Strg+Ende, Strg mit den Pfeiltasten (mit oder ohne Umschalt), Rücktaste und Entf als Befehle aufgezeichnet. Sie bewegen und löschen an jeder Einfügemarke.
- Weitersuchen und Rückwärts suchen (F3, Umschalt+F3, Eingabe in der Suchleiste) speichern mit dem Schritt den Suchtext der Suchleiste, sodass das Makro beim späteren Abspielen denselben Text findet. Öffnen Sie die Suchleiste und tippen Sie den Suchtext vor dem Start der Aufzeichnung; ohne Suchtext wird der Schritt nicht aufgezeichnet.
- Ersetzen und Alle in der Ersetzen-Leiste (die Befehle Nächstes ersetzen und Alle ersetzen) speichern mit dem Schritt die ganze Leiste: Suchtext, Schalter und Ersatztext. Vor dem Schritt stellt das Makro die Leiste auf diese Werte, sodass das Ergebnis nicht davon abhängt, was die Leiste vorher zeigte. Bei eingeschalteter Auswahl arbeitet der Schritt mit der Auswahl, die das Makro beim Ausführen hat. Ohne Suchtext wird der Schritt nicht aufgezeichnet.
- Das Abspielen ist ein einziger Rückgängig-Schritt. Läuft ein Schritt nicht, hält das Makro dort an und die Statuszeile nennt den Schritt; was vorher lief, bleibt ein Rückgängig-Schritt.
- Beim Aufzeichnen wird ein Befehl, den ein Makro nicht abspielen kann, abgelehnt und die Statuszeile sagt es: alles mit Dialog, Dateibefehle, das Öffnen der Suchleiste. Die Aufzeichnung läuft weiter. Mausklicks, Bild auf und Bild ab werden nicht aufgezeichnet.
- Zum Abspielen ist ein bearbeitbares Textdokument nötig. Der Import von PSPad-Makros ist nicht verfügbar.
- Standardkürzel, Platzhalter (OPEN, noch nicht entschieden): Strg+K Strg+R zeichnet auf, Strg+K Strg+Umschalt+R beendet, Strg+K Strg+P spielt das letzte Makro ab; auf macOS ⌘ statt Strg. Eigene Kürzel in den Einstellungen haben Vorrang.
- Die Datei eines gespeicherten Makros über 1 MB (Platzhalter-Grenze, OPEN) wird nicht gelesen; die Liste meldet sie als beschädigt.

## [edit.json] JSON formatieren
commands: nks.edit.formatJson, nks.edit.previewFormatJson
1. Markieren Sie JSON-Text, oder markieren Sie nichts, um das ganze Dokument zu formatieren.
2. Menü Bearbeiten, Formatierung, JSON formatieren, formatiert die markierten Bereiche (jede Auswahl) oder das ganze Dokument. Alle Bereiche sind ein Rückgängig-Schritt; Text außerhalb bleibt Byte für Byte unverändert, auch die Zeilenenden.
3. Vorschau der JSON-Formatierung zeigt die Änderung zuerst als Vorschlag. Wenden Sie ihn in der Leiste über dem Editor an oder verwerfen Sie ihn.
- Ein Syntaxfehler wird mit Zeile und Spalte gemeldet, und nichts wird geändert. Text über dem Limit wird mit einer Meldung abgelehnt.
- Hat sich das Dokument nach dem Start des Befehls geändert, bricht der Befehl ab und bittet Sie, ihn erneut auszuführen.

## [tools.stats] Dokumentstatistik und Hashes
commands: nks.tools.statistics, nks.tools.hash, nks.tools.hashCopy, nks.tools.hashCompare
1. Menü Extras, Text und Berechnungen, Dokumentstatistik, zählt Bytes, Zeichen, Wörter, Zeilen und die längste Zeile des ganzen Dokuments oder der markierten Teile zusammen und listet die häufigsten Zeichen und Wörter auf.
2. Menü Extras, Text und Berechnungen, Hash berechnen, fragt nach dem Algorithmus (CRC-32, MD5, SHA-1, SHA-256, SHA-384 oder SHA-512) und berechnet den Hash der rohen Bytes der ganzen Datei oder jedes markierten Teils. Hash berechnen und kopieren legt den Hash auch in die Zwischenablage. Hash mit eingefügtem vergleichen fragt nach einem Hash, erkennt den Algorithmus an der Länge und sagt, ob er übereinstimmt.
- Beide Befehle lesen das Dokument blockweise, während Sie weiterarbeiten, zeigen den Fortschritt und lassen sich abbrechen. Keiner hält die ganze Datei im Speicher, daher funktionieren sie bei Dateien jeder Größe, auch in der seitenweisen und der Hex-Ansicht.
- Eine abgebrochene Statistik wird als unvollständig angezeigt, mit der Zahl der gezählten Bytes. Ein abgebrochener Hash hat kein Ergebnis.
- Statistik und Hash gelten für das Dokument, wie es beim Start des Befehls war; Änderungen währenddessen ändern sie nicht.
- Ein Wort ist eine zusammenhängende Folge von Buchstaben und Ziffern; Wörter werden in Kleinbuchstaben gezählt. Die Wortliste hält nur eine begrenzte Zahl verschiedener Wörter und sagt, wenn sie voll ist. Mehrere markierte Teile werden als ein Text gezählt.

## [tools.calculate] Blockberechnung, Ausdrücke und Zahlensysteme
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.insertExpression, nks.tools.convertBase
1. Markierten Block berechnen addiert die Zahlen in der Markierung (Blockmarkierung, eine Spalte von Zahlen) und zeigt Anzahl, Summe, kleinste, größte und Durchschnitt.
2. Ausdruck auswerten fragt nach einem Ausdruck, beginnt mit dem markierten Text oder der Zeile des Cursors und zeigt das Ergebnis. Ausdrucksergebnis einfügen schreibt " = Ergebnis" hinter die Markierung oder die Zeile, als einen Rückgängig-Schritt, mit Punkt als Dezimalzeichen in jeder Sprache. Verstanden werden + - * / ^ ( ), MOD und die Funktionen ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR und TRUNC. Zahlen werden mit Punkt geschrieben. Alles andere, etwa der Aufruf eines Systembefehls, wird abgelehnt und nichts wird ausgeführt.
3. Zahlensystem umrechnen fragt nach einer ganzen Zahl, dezimal, mit dem Präfix 0x, 0b oder 0o, oder römisch, und zeigt sie dezimal, hexadezimal, oktal, binär, als römische Zahl (1 bis 3999) und als das Datum, das sie als Unix-Millisekunden ergibt.
- Das Programm erkennt, wie die Zahlen in der Markierung geschrieben sind (Tschechisch und Slowakisch: 1 234,50, Deutsch: 1.234,50, Englisch: 1,234.50). Passen mehrere Schreibweisen, etwa bei 1,234, nimmt es die der Programmsprache. Die Ergebnisse erscheinen im Format der Programmsprache. Ein Sprachwechsel ändert das Dokument nicht.
- Text in der Markierung, der keine Zahl ist, wird übersprungen und gezählt. Die Zahlenumrechnung funktioniert für Werte von -9223372036854775808 bis 18446744073709551615.
- Potenzen nehmen nur eine positive Basis. Die Berechnung verwendet Zahlen mit doppelter Genauigkeit.

## [tools.colors] Farben
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. In CSS-, HTML-, XML-, JavaScript-, TypeScript-, PHP-, Razor- und Latte-Dateien steht vor jedem Farbwert (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) ein kleines Farbfeld in seiner Farbe. Das Farbfeld belegt kein Zeichen des Dokuments.
2. Klicken Sie auf das Farbfeld, oder setzen Sie den Cursor auf den Wert und wählen Sie Extras, Farben, Farbe am Cursor wählen. Es öffnet sich die Auswahl: Regler für Farbton, Sättigung, Helligkeit und Deckkraft und ein Wertfeld, das auch eine Farbe in anderer Schreibweise annimmt. Ein Klick außerhalb der Auswahl schreibt die Farbe über den Wert, in der Schreibweise, die er hatte, als einen Rückgängig-Schritt; Esc schließt die Auswahl ohne Änderung.
3. Extras, Farbe in HEX, RGB, HSL oder oklch umwandeln schreibt den Farbwert am Cursor, oder alle in der Markierung, in dieser Schreibweise neu. Die Farbe bleibt dieselbe Acht-Bit-Farbe; oklch wird mit fünf Nachkommastellen bei Helligkeit und Chroma und zwei beim Farbton geschrieben.
- Eine Farbe außerhalb des sRGB-Bereichs wird darauf beschnitten. Die Deckkraft bleibt erhalten, als `/ Alpha` oder als die letzten zwei Hexziffern.
- Text, der nur wie eine Farbe aussieht, etwa `rgb(var(--c))`, die HTML-Entität `&#123;` oder `#12345`, bleibt unberührt.
- Eine Datei, die größer als das Limit des Plugins ist, wird nicht umgewandelt; die Meldung sagt es.

## [tools.markdown] Markdown-Vorschau, Tabellen und Inhaltsverzeichnis
commands: nks.tools.markdownPreview, nks.tools.markdownOutline, nks.tools.markdownFormatTable, nks.tools.markdownUpdateToc, nks.tools.markdownRepairTable, nks.markdown.nextCell, nks.markdown.previousCell, nks.markdown.newRow
1. Extras, Markdown, Vorschau für Markdown oder HTML öffnet unter dem Editor den Vorschaubereich mit der aktuellen Datei. Der Bereich folgt dem Tippen: Er wird kurz nach der letzten Taste neu gezeichnet und behält die Scrollposition.
2. Markdown erscheint als Seite: Überschriften, Listen, Zitate, Code, Hervorhebungen und Tabellen mit ihren Zellen (die Doppelpunkte in der Strichzeile bestimmen die Ausrichtung einer Spalte).
3. Eine HTML-Datei erscheint als Text, samt Tags. Nichts darin wird ausgeführt und nichts aus dem Netz geladen.
4. Extras, Markdown, Markdown-Gliederung anzeigen listet im selben Bereich die Überschriften der Datei, nach Ebene eingerückt. Ein Klick oder Enter auf eine Überschrift setzt den Cursor im Editor in ihre Zeile.
5. Extras, Markdown, Markdown-Tabelle ausrichten schreibt die Tabelle unter dem Cursor so um, dass jede Spalte so breit ist wie ihre breiteste Zelle. Die Zeichen `:--`, `:-:` und `--:` bleiben, eine kürzere Zeile wird mit leeren Zellen aufgefüllt. Ein Rückgängig-Schritt macht es rückgängig.
6. Extras, Markdown, Markdown-Inhaltsverzeichnis aktualisieren schreibt Links zu den Überschriften zwischen die Zeilen `<!-- TOC -->` und `<!-- /TOC -->`. Nur der Text dazwischen ändert sich. Fehlen die Zeilen, werden sie am Cursor eingefügt. Die Anker sind klein geschrieben, Leerzeichen werden zu Bindestrichen, ein wiederholter Titel bekommt `-1`, `-2`.
7. In einer Tabelle wählt Tab die nächste Zelle und Umschalt+Tab die vorherige; dabei werden die Spalten ausgerichtet. Tab in der letzten Zelle fügt eine Zeile an. Außerhalb einer Tabelle wirken beide Tasten wie bisher. Jeder Sprung ist ein Rückgängig-Schritt.
8. In einer Tabelle fügt Umschalt+Eingabe unter der Cursorzeile eine leere Zeile mit der richtigen Zellenzahl ein. Extras, Markdown, Markdown-Tabelle reparieren behebt eine kaputte Tabelle: Sie ergänzt eine fehlende Strichzeile, füllt kurze Zeilen auf, behält die Doppelpunkte und richtet die Spalten aus.
- Ein Bild wird nur aus einer `data:`-Adresse mit PNG, JPEG, GIF, BMP oder WebP gezeichnet. Ein Bild aus dem Netz oder aus einer lokalen Datei erscheint als Hinweis mit seinem Alternativtext. Ein Link ins Web oder zu einer E-Mail-Adresse fragt zuerst nach Ihrer Bestätigung; jeder andere Link ist nur Text.
- Eine Datei über dem Limit der Vorschau wird nicht gezeichnet; die Statusleiste sagt es. Esc oder Schließen beendet die Vorschau.
- Aufgelistet werden nur mit `#` geschriebene Überschriften. Die Breite einer Tabellenzelle ist ihre Anzeigebreite: breite ostasiatische Zeichen zählen als zwei Spalten. Ein senkrechter Strich in einer Zelle wird als `\|` geschrieben oder steht in einem Code-Ausschnitt.

## [tools.keys] Schlüsselpfade in YAML und JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. In einer YAML- oder JSON-Datei zeigt die Statusleiste den Pfad des Schlüssels am Cursor, zum Beispiel `cs.app.title`. Listeneinträge werden `items[0]` geschrieben, ein Schlüssel mit Punkt oder Klammer wird `["a.b"]` geschrieben. Der Pfad wird für die Revision und den Cursor gelesen, für die gefragt wurde: Tippen oder bewegen Sie sich inzwischen, wird die Antwort verworfen.
2. Extras, Struktur und Sprache, Schlüsselpfad kopieren legt den Pfad in die Zwischenablage.
3. Extras, Struktur und Sprache, Gehe zu Schlüssel (oder `@` in Gehe zu Beliebigem) listet die Schlüssel der Datei nach Pfad; Enter markiert den Schlüssel, wie er geschrieben steht. Schlüsselgliederung anzeigen listet dieselben Schlüssel im Plugin-Bereich, und ein Klick dort markiert dieselben Bytes.
4. Extras, Struktur und Sprache, Schlüssel mit anderer Datei vergleichen fragt nach einer weiteren geöffneten YAML- oder JSON-Datei und schreibt ins Ausgabefenster die Schlüssel mit Wert, die der jeweils anderen Datei fehlen.
- YAML im Blockstil wird verstanden: verschachtelte Schlüssel, Listen, Schlüssel in Anführungszeichen und Blockskalare. Ein Wert in geschweiften oder eckigen Klammern zählt als ein Wert. JSON darf Kommentare und ein Komma am Ende haben.
- Ein Syntaxfehler beendet das Lesen an dieser Stelle; die Schlüssel davor bleiben, und die Meldung nennt die Zeile.

## [tools.outline] Codegliederung und Symbole
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. In HTML-, PHP-, CSS-, C#- und Markdown-Dateien zeigt Extras, Struktur und Sprache, Codegliederung anzeigen die Funktionen, Klassen, Überschriften und Regeln der Datei als Baum im Plugin-Bereich. Ein Klick markiert den Namen des Symbols.
2. Extras, Struktur und Sprache, Gehe zu Symbol (oder `@` in Gehe zu Beliebigem) listet dieselben Symbole nach Namen; Enter markiert dieselben Bytes wie ein Klick in der Gliederung.
3. Die Gliederung gehört zu einer Revision des Dokuments. Nach einer Änderung wird sie von selbst erneuert; bis die neue da ist, bleibt die alte als veraltet markiert, und ein Sprung wird über die seitdem gemachten Änderungen getragen oder abgelehnt, wenn der Text, auf dem das Symbol stand, geändert wurde. Eine Antwort für eine ältere Revision wird nie als aktuell gezeigt.
4. Hält der Plugin-Prozess an, wird die Gliederung nach seiner Rückkehr neu gelesen.
5. Solange eine Datei eine Gliederung hat, nennt die Statusleiste das Symbol, in dem der Cursor steht, samt Eltern (zum Beispiel `Klasse › Methode`); sie folgt dem Cursor und ist bei veralteter Gliederung ausgeblendet.
- Gelesen wird mit einfachen Regeln der Sprache, ohne Sprachserver und ohne Projektindex. Außer den Sprachen unten: JavaScript und TypeScript (Klassen, Schnittstellen, Enums, Funktionen, Pfeilfunktionen, Methoden), Python (Klassen, Funktionen, Methoden nach Einrückung), Rust (Module, Structs, Enums, Traits, impl-Blöcke, Funktionen, Makros), Dart (Klassen, Mixins, Enums, Erweiterungen, Funktionen, Methoden), C und C++ (Namensräume, Structs, Klassen, Enums, Funktionen, Makros), Bash (Funktionen), CMake (Funktionen, Makros, Ziele), Makefile (Ziele, `define`), Meson (Ziele, Tests, Unterordner). Schlüssel von YAML und JSON liefert die Schlüsselgliederung. HTML (Überschriften, Elemente mit `id`, `main`, `nav`, `section` und ähnliche), PHP (Namensräume, Klassen, Schnittstellen, Traits, Enums, Funktionen, Methoden), CSS (Regeln und At-Regeln), C# (Namensräume, Typen, Methoden, Konstruktoren, Eigenschaften), Markdown (`#`-Überschriften).
- Kommentare und Zeichenketten werden nicht gelesen. Was die Regeln nicht erkennen, wird übersprungen.
- Eine Datei über dem Limit des Plugins oder mit mehr Symbolen, als die Gliederung fasst, wird gekürzt gezeigt, und die Meldung sagt es. Eine große Datei in der seitenweisen Ansicht wird im Hintergrund vom Anfang bis zum Limit gelesen; ein Sprung zu einem Symbol scrollt die seitenweise Ansicht dorthin.

## [edit.carets] Mehrere Cursor
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Markieren Sie ein Wort; Strg+D fügt sein nächstes Vorkommen zur Auswahl hinzu. Wiederholen Sie das für weitere Vorkommen.
2. Strg+K und dann Strg+D überspringt das letzte Vorkommen und nimmt das nächste.
3. Strg+Umschalt+L wählt alle Vorkommen auf einmal aus.
4. Strg+Alt+Pfeil nach oben und Strg+Alt+Pfeil nach unten fügen einen Cursor in der Zeile darüber und darunter hinzu.
5. Beim Tippen ändern sich alle Stellen gleichzeitig. Esc lässt nur einen Cursor übrig.
- Die Statusleiste zeigt die Anzahl der Cursor.
- Alt+Ziehen mit der Maus erzeugt je nach Einstellung Alt+Ziehen einen Cursor in jeder Zeile oder eine Blockauswahl.
- Mit einer Eingabemethode (Japanisch, Chinesisch, Koreanisch) erscheint der Text, der gerade zusammengesetzt wird, unterstrichen an jedem Cursor. Nach dem Bestätigen wird er an allen als ein Rückgängig-Schritt eingefügt; Esc bricht ihn ab und lässt die Cursor stehen.

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
- In einer großen Datei zeigt die Leiste den Fortschritt in Prozent, und Abbrechen hält die Suche an. Auflisten zeigt die Treffer im Tab Suchergebnisse; gibt es mehr Treffer, als aufgelistet werden, sagt der Tab das.
- Schalten Sie den regulären Ausdruck ein, erscheint die Schaltfläche Testen (Thema Regex-Tester).

## [search.replace] Ersetzen
commands: nks.search.replace, nks.search.replaceNext, nks.search.replaceAll
1. Drücken Sie Strg+H oder wählen Sie Suchen und dann Ersetzen. Die Leiste hat zusätzlich das Feld Ersetzen durch.
2. Tippen Sie den Suchtext und den Ersatztext.
3. Die Eingabetaste im Ersatzfeld ersetzt einen Treffer, Strg+Alt+Eingabe alle. Dasselbe tun die Schaltflächen Ersetzen und Alle.
4. Die Statusleiste meldet, wie viele Treffer ersetzt wurden, oder Es wurde nichts ersetzt.
- Beim Ersetzen mit regulären Ausdrücken gelten $1, $& und die Umwandlung der Schreibweise \U \L \E \u \l.
- Nächstes ersetzen und Alle ersetzen sind auch Befehle (Befehlspalette). Ein Makro kann sie aufzeichnen (Thema Makros).
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
- Steht der Cursor neben einer Klammer und ist nichts markiert, werden die Klammer und ihr Gegenstück eingefärbt. Klammern in Zeichenketten und Kommentaren zählen nicht. Passende Klammern hervorheben in den Einstellungen schaltet das ab.
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
- Die Größe hat keine feste Obergrenze: Sie wächst, solange mindestens eine Zeile in die Ansicht passt, das Minimum ist 6 Punkt. Sie wird in der Einstellung Schriftgröße gespeichert.
- Im Text ändert auch Strg+Mausrad die Größe, in Hex Strg+Umschalt+Mausrad.

## [view.display] Verborgene Zeichen, Zeilennummern und Minimap
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Wählen Sie Ansicht und Verborgene Zeichen anzeigen (Strg+K, Strg+W): Leerzeichen, Tabulatoren und die Zeilenenden CR, LF und CRLF werden gezeichnet. Das Dokument wird nicht verändert.
2. Zeilennummern anzeigen (Strg+K, Strg+N), Aktuelle Zeile hervorheben, Rechten Rand anzeigen (senkrechte Linie bei Spalte 80, einstellbar mit der Einstellung Spalte des rechten Rands) und Anklickbare Links schalten den Rest ein und aus.
3. Minimap anzeigen (Strg+K, Strg+M) zeigt am rechten Rand ein verkleinertes Bild des Textes in den Farben der Hervorhebung, ein Log in den Farben seiner Stufen. Klicken oder Ziehen scrollt. Eine lange Datei im Editor zeigt nur die Zeilen um den Bildschirm.
- Bei einer großen Datei (seitenweiser Modus) ist das Bild eine Stichprobe: kurze Zeilenfolgen vom Anfang gleich großer Teile der ganzen Datei, die Datei wird also nie ganz gelesen. Statt eines Rechtecks zeigt eine dünne Linie die Position. Solange die Zeilen indiziert werden, deckt das Bild die bisher gezählten Zeilen ab, wie die Bildlaufleiste. Die Zahl der Zeilen einer Folge ist ein Platzhalter, kein entschiedener Wert.
- Treffer der Suchleiste sind im schmalen Streifen am rechten Rand der Minimap markiert. Die Zeilen, die der Filter einer Live-Ansicht eines Logs behält, sind dort ebenfalls markiert: Fehlerzeilen rot, Warnungen gelb, die übrigen blau.
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

## [workspace.projects] Projekte
commands: nks.project.save, nks.project.open, nks.project.addFolder, nks.project.delete, nks.project.settings
Ein Projekt ist ein benannter Satz von Ordnern mit den Dateien, die darin geöffnet waren. Es liegt im Profil und lässt sich wie ein Ordner wechseln.
- Ordner zum Panel hinzufügen zeigt einen weiteren Ordner neben den vorhandenen. Jeder Ordner ist eine oberste Zeile des Baums.
- Projekt speichern (Datei, Projekte und Sitzungen) legt die Ordner des Panels und die darin liegenden offenen Dateien unter einem Namen ab. Ein Projekt gleichen Namens wird ersetzt.
- Projekt öffnen zeigt die Ordner im Panel und öffnet die Dateien, die das Projekt offen hatte. Ein fehlender Ordner oder eine fehlende Datei wird in der Statusleiste genannt; die übrigen öffnen sich.
- Beim Wechsel zu einem anderen Projekt bleiben die offenen Tabs. Das verlassene Projekt merkt sich seine eigenen Dateien.
- Projekt löschen entfernt nur den Eintrag. Ordner und Dateien bleiben.
- Projekteinstellungen bearbeiten öffnet die Einstellungsdatei des aktiven Projekts (gleiches Format wie die Benutzereinstellungen). Die Werte gelten, solange das Projekt aktiv ist, nach erneutem Öffnen des Projekts. Gehe zu Allem durchsucht nur den ersten Ordner.

## [workspace.favorites] Favoriten
commands: nks.favorite.add, nks.favorite.open, nks.favorite.remove
- Zu Favoriten hinzufügen speichert die gewählte Zeile des Dateipanels, sonst die aktive Datei, sonst den Ordner des Panels. Der Befehl steht auch im Kontextmenü des Panels.
- Favoriten listet sie auf (auch aus dem leeren Panel). Enter öffnet einen Ordner im Panel und eine Datei in einem Tab. Ein fehlender Pfad erscheint abgeblendet mit Nicht gefunden.
- Aus Favoriten entfernen listet sie auf und entfernt den gewählten Eintrag.

## [workspace.sessions] Benannte Sitzungen
commands: nks.session.save, nks.session.open, nks.session.delete
- Sitzung speichern legt die offenen Tabs unter einem Namen ab: Gruppen, Reihenfolge, aktiven Tab und die Auswahl jedes Tabs. Ein Tab mit ungesicherten Änderungen oder ohne Datei behält seinen Text. Eine Sitzung gleichen Namens wird ersetzt.
- Sitzung öffnen öffnet die Tabs neben den bereits offenen. Dateien werden von der Platte gelesen, dann ersetzt der ungesicherte Text der Sitzung ihren Text, sodass er sich rückgängig machen oder verwerfen lässt. Eine fehlende Datei wird in der Statusleiste genannt; ihr Text, falls die Sitzung ihn hatte, öffnet sich in einem unbenannten Dokument.
- Sitzung löschen fragt zuerst nach, weil die Sitzung ungesicherten Text enthalten kann.
- Eine Sitzungsdatei, die sich nicht lesen lässt, wird übersprungen und in einer Leiste genannt; die anderen Sitzungen funktionieren.
- Große Dateien im seitenweisen Modus werden nur mit dem Pfad gespeichert.

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

## [settings.backup] Konfiguration sichern
commands: nks.settings.backup, nks.settings.restore
1. Wählen Sie Einstellungen und dann Konfiguration sichern und benennen Sie die Datei. Die ZIP-Datei hat das Datum im Namen. Sie enthält die Einstellungen, Tastenkürzel, Benutzervariablen, externen Werkzeuge, Aktionen beim Speichern, Logansichten, Makros, Rezepte, Clips und Datenplugins, Arbeitsbereiche, Remote-Verbindungen und den Proxy.
2. Auf einem anderen Computer oder nach einer Neuinstallation wählen Sie Einstellungen und dann Konfiguration wiederherstellen und wählen die Datei. Die Dateien kommen beim nächsten Start zurück: Wählen Sie Beenden und wiederherstellen oder Beim nächsten Start.
- Eine Sicherung enthält keine Passwörter oder Token. Passwörter bleiben im Anmeldeinformationsspeicher des Betriebssystems. Ein Wert in den Dateien, der ein gespeichertes Passwort enthält oder dessen Name sagt, dass er ein Passwort, ein Token oder ein Schlüssel ist, wird ausgelassen, und die Statusleiste sagt, wie viele. Geben Sie die Passwörter nach einer Wiederherstellung neu ein (Remote-Verbindungen und dann Passwort setzen).
- Eine Wiederherstellung ersetzt nur die Dateien, die in der Sicherung sind. Die Konfiguration von vorher wird zuerst im Ordner config-backups des Profils gespeichert.
- Nicht in einer Sicherung: das Journal, der lokale Verlauf, die Sicherungen von Dateien, die Kopien von Remote-Dateien, Sitzungen, Logs und installierte Plugins.
- Eine Sicherung aus einer neueren Version des Programms wird abgelehnt.

## [view.output] Ausgabefenster
commands: nks.view.toggleOutput
Das Ausgabefenster ist ein Streifen unter dem Editor, in den Aufgaben und Suchen schreiben. Standardmäßig ist es ausgeblendet.
1. Drücken Sie Strg+Umschalt+Y oder wählen Sie Ansicht und dann Ausgabe.
2. Die Tabs Aufgaben und Suche wechseln den Kanal. Die Zusammenfassung einer Suche in Dateien wird auch in den Kanal Suche geschrieben.
3. Die Schaltfläche Leeren leert den Kanal, Schließen (oder Esc) blendet das Fenster aus.
- Der Text lässt sich markieren und kopieren. Ein leerer Kanal meldet In diese Ausgabe wurde noch nichts geschrieben.
- Die Ausgabe wird aus Dateien im Profil gelesen und bleibt deshalb nach einem Absturz des schreibenden Programms sichtbar.

## [view.pluginPanels] Plugin-Bereiche
commands: nks.view.pluginPanels
Ein Plugin-Bereich ist eine Liste oder ein Baum, den ein Plugin an den Editor schickt. Der Editor zeigt ihn in der linken Spalte.
1. Wählen Sie Ansicht und dann Plugin-Bereiche oder suchen Sie den Befehl in der Palette. Die Liste zeigt die Bereiche, die Plugins gerade haben, jeweils mit dem Namen des Plugins.
2. Wählen Sie einen Bereich und drücken Sie Enter. Er öffnet sich mit dem ersten Eintrag ausgewählt.
3. Die Pfeiltasten nach oben und unten gehen durch die Einträge, nach rechts und links klappen sie einen Zweig auf und zu. Enter oder ein Doppelklick öffnet einen Eintrag.
4. Esc im Bereich schließt ihn und bringt Sie zurück in den Text. Während Sie im Text schreiben, bleibt der Bereich offen.
- Wenn das Plugin die Einträge erneuert, bleibt der ausgewählte Eintrag ausgewählt.
- Abbrechen in der Statusleiste bricht eine Aufgabe eines Plugins ab. Die Zeile meldet dann, dass die Aufgabe abgebrochen wird. Beendet das Plugin sie nicht bald selbst, beendet der Editor sie und gibt ihren Platz frei.
- Wenn der Plugin-Prozess endet, verschwinden seine Bereiche, sein Eintrag in der Statusleiste und seine Aufgaben.

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
- Der Platzhalter *: 50 4B * 04 steht für 0 bis N Bytes zwischen 4B und 04. N ist die Einstellung Längster Platzhalter * in der Hex-Suche; solange sie 0 ist (nicht gewählt), gilt der eingebaute Standardwert von 256 Bytes. *{2,8} schreibt einen eigenen Bereich und braucht keine Einstellung.
- Ein Muster zwischen Schrägstrichen ist ein regulärer Ausdruck über Bytes, zum Beispiel /\x50\x4B[\x01-\x08]/. Ein Byte ist ein Zeichen 0 bis 255, . ist ein beliebiges Byte (auch 0A und 0D), \xHH ist ein Byte und [...] eine Menge von Bytes. Gruppen, |, ? * + {n,m} und \n \r \t \0 \d \w \s funktionieren; ^ und $ sind Anfang und Ende der Datei (beim Ersetzen der Auswahl); \b, Rückverweise und Lookaround nicht, weil Bytes keine Zeilen oder Wörter haben. Eine offene Wiederholung (.*, +, {n,}) reicht höchstens N Bytes weit. Ein Treffer braucht mindestens ein Byte, und Treffer dürfen sich überlappen.
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
- Strg+Mausrad über Hex fügt einen Block Bytes pro Zeile hinzu oder nimmt ihn weg (eine Gruppe, mindestens 8); die Zeile mit dem Cursor bleibt an ihrer Stelle. Es gibt keine feste Grenze, nur die Fensterbreite: Passt der nächste Block nicht, tut das Mausrad nichts.
- An Fensterbreite anpassen: Die Bytes pro Zeile folgen der Fensterbreite, abgerundet auf ein Vielfaches der Gruppe. Eine Größe von Hand oder Strg+Mausrad schaltet es aus.
- Strg+Umschalt+Mausrad ändert die Schriftgröße auch in Hex, wie Strg+=, Strg+- und Strg+0.
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
1. Bearbeiten und dann Hex und dann Hex: Auswahl kopieren als legt die ausgewählten Bytes als Text in die Zwischenablage: Hex-Zeichenfolge, Array für C, C#, Java, Python, Rust, Go, JavaScript oder Pascal, Base64, Intel HEX, Motorola S-Record oder Hexdump.
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

## [hex.template] Hex: Strukturvorlage
commands: nks.hex.template, nks.hex.template.open
Das Strukturpanel liest die Datei mit einer Vorlage und zeigt ihre Felder als Baum. Ein gewähltes Feld markiert seine Bytes im Raster, und wer den Cursor im Raster bewegt, wählt das Feld, das ihn enthält.
1. Hex: Strukturvorlage (Strg+J) öffnet das Panel oder schließt es, wenn es schon den Fokus hat. Die Vorlage wird nach der Magic Number gewählt, danach nach der Dateiendung; die Liste über dem Baum erlaubt eine andere Wahl, die für diese Endung gemerkt wird (Einstellung hex.structureTemplates, geschrieben als Endung=Vorlage).
2. Ein Feld mit Teilen (Struktur oder Liste) wird erst beim Aufklappen gelesen, daher öffnet sich auch eine große Datei sofort. Eine lange Liste erscheint als Bereiche (Einträge[0..255]), die sich zu ihren Elementen aufklappen, und eine Liste, deren Elemente alle gleich groß sind, etwa die Dreiecke eines 3D-Modells, kann Millionen Elemente haben und öffnet sich trotzdem sofort. Jede Zeile zeigt den Namen, den Wert und grau den Offset und die Länge in Bytes. Pfeil nach oben und unten gehen durch die Zeilen, Pfeil nach rechts und links öffnen und schließen ein Feld, Enter gibt den Fokus an das Raster zurück, mit den Bytes des Feldes markiert, Esc gibt ihn ohne Markierung zurück.
3. Ein Feld, das sich nicht lesen lässt, steht rot mit dem Grund da, zum Beispiel weil es über das Ende der Daten hinausreicht oder von den erwarteten Bytes abweicht. Die Felder dahinter erscheinen nicht, weil ihre Lage unbekannt ist.
4. Hex: Strukturvorlagendatei öffnen fragt nach einer .ksy-Datei (die Schaltfläche unter der Liste tut dasselbe) und bietet ihre Vorlage in der Liste an. Eine Vorlage, die sich nicht verwenden lässt, wird in der Notiz unter der Liste mit ihrer Zeile genannt, zum Beispiel „nicht unterstützt: process“. So geöffnete Vorlagen bleiben bis zum Programmende in der Liste.
- Mitgelieferte Vorlagen: BMP-Header (mit der Zeilengröße), PNG-Chunks (IHDR, gAMA, tEXt), ZIP-Archivabschnitte, Windows-PE-Header, ELF-Header (32 und 64 Bit, beide Byte-Reihenfolgen, Abschnitt dynamic) und binäres STL. Die Datei pinned.yaml neben den Vorlagen nennt zu jeder die Version, die Lizenz und die verwendeten Konstrukte. Wir haben sie für Textida geschrieben, sie stehen wie das Programm unter der MIT-Lizenz; Vorlagen anderer Projekte werden nicht kopiert. Das Panel zeigt Datei, Version und Lizenz der verwendeten Vorlage.
- Vorlagen sind eine Teilmenge des Formats Kaitai Struct .ksy: Ganzzahlen (u1 bis u8, s1 bis s8), Gleitkommazahlen (f4, f8), bitweise Ganzzahlen (b1 bis b64; hintereinander, nicht wiederholt), Zeichenketten (str, strz), rohe Bytes, feste Bytes (contents), Aufzählungen (auch innerhalb eines Typs), verschachtelte Typen, size, size-eos, repeat (eos, expr oder until), if, switch-on mit cases (Schlüssel _ ist der Standardzweig), instances mit pos (und io: _root._io) oder value, meta endian (auch mit switch-on und cases) und bit-endian sowie Ausdrücke. Ausdrücke sind in size, repeat-expr, repeat-until, if, pos, io, value und switch-on erlaubt. Sie rechnen mit 64-Bit-Ganzzahlen, Gleitkommazahlen, true/false und Zeichenketten: + - * / %, << >> & | ^ ~, < <= > >= == !=, and or not, a ? b : c, Klammern, Zahlen (dezimal, 0x, 0b und Gleitkommazahlen wie 1.5 oder 2.5e-3), Zeichenketten in Anführungszeichen, Namen früherer Felder und Instanzen, a.b, Liste[i], enum::Mitglied und Typ::enum::Mitglied, _parent, _root, _index, _ (das letzte Element in repeat-until), _io.pos, _io.size, _io.eof, .as<Typ> (ohne Umwandlung), .to_i bei Zeichenketten, Gleitkommazahlen und Wahrheitswerten, .length bei Zeichenketten, .size, .first und .last bei Listen. Eine Gleitkommazahl ist nie eine Größe oder Anzahl (verwenden Sie .to_i), und % und die Bit-Operatoren nehmen nur Ganzzahlen. pos zählt ab dem Anfang des Datenstroms der Struktur (eine Struktur mit size ist ein eigener Strom). Array-Schreibweise, andere Methoden, process (ein xor- oder zlib-Schritt über die Bytes) und alles Übrige, was nicht aufgeführt ist, wird mit der Vorlagenzeile abgelehnt, zum Beispiel „nicht unterstützt: process“. Eine Division durch null, ein fehlendes Feld oder eine falsche Art von Wert ergibt einen Fehler an diesem Feld; eine riesige Größe oder Anzahl endet mit einem Limitfehler. Instanzen stehen hinter den Feldern.
- Das Lesen endet mit einer Meldung, wenn eine Vorlage zu tief verschachtelt, ein Feld zu oft wiederholt, eine Zeichenkette oder Arbeitsmenge über einem festen Limit liest oder ein Knoten zu lange dauert.

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
3. In executable, args und cwd stehen die Variablen ${File} (vollständiger Pfad), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} und ${Column} für das aktuelle Dokument; ${Selection} ist der markierte Text (er darf mehrere Zeilen haben), ${Word} das Wort unter dem Cursor und ${ProjectRoot} der nächste Ordner über der Datei (oder ihr eigener), der .textida oder .git enthält; $$ ist ein Dollarzeichen. Ein Argument bleibt ein Argument, was auch im Wert steht, Leerzeichen und Anführungszeichen eingeschlossen: Es wird keine Shell verwendet. Eine unbekannte oder nicht passende Variable (${File} bei einem Dokument ohne Datei, ${Selection} ohne Markierung, ${Word} außerhalb eines Wortes) stoppt den Start, bevor etwas beginnt. Die Variablen von Clips und Rezepten gelten hier auch (file.path, date, datetime, selection, user.name und Ihre eigenen Variablen), mit demselben Wert wie dort. Eine eigene Variable gleichen Namens geht auch ${File} und den anderen vor. Lässt sich die Datei der eigenen Variablen nicht lesen, startet das Werkzeug nicht und die Ausgabe nennt den Grund.
4. Ein Werkzeug, dessen Befehl und Verzeichnis noch nicht gelaufen sind, wird mit beidem angezeigt und startet erst nach Ihrer Bestätigung. Die Bestätigung gilt, bis sich die Definition oder das Programm dahinter ändert.
5. Die Ausgabe geht Zeile für Zeile in den Reiter Aufgaben des Ausgabefensters, am Ende mit Exitcode und Zeit. Beenden in diesem Fenster oder Laufendes Werkzeug beenden stoppt das Programm und alles, was es gestartet hat; ebenso das Zeitlimit.
- Batchdateien (.bat, .cmd) werden über cmd.exe gestartet, und jedes Argument wird dafür in Anführungszeichen gesetzt, sodass Leerzeichen, Anführungszeichen und Zeichen wie & | < > ^ % unverändert ankommen. Ein Argument mit Zeilenumbruch oder einem anderen Steuerzeichen wird vor dem Start abgelehnt, ebenso eines mit !, wenn die Batchdatei die verzögerte Erweiterung einschaltet.
- Ein Programmname ohne Pfad wird nur in PATH gesucht, nie im Arbeitsverzeichnis.
- Die Ausgabe wird als UTF-8 gelesen, es sei denn, das Werkzeug nennt eine andere Kodierung ("outputEncoding": "windows-1250") oder die Einstellung Standard-Ausgabekodierung für Werkzeuge ist gesetzt. Ein Werkzeug ohne timeoutSeconds bekommt die Einstellung Standard-Zeitlimit für Werkzeuge (0: kein Limit). Es läuft immer nur ein Werkzeug.

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
- Das Feld Kodierung der Ausgabe nennt die Kodierung, in der die Ausgabe gelesen wird (utf-8, windows-1250, ibm852); ein Name, den das System nicht kennt, wird abgelehnt. Leer: der Standard aus den Einstellungen.

## [tools.make] Makefile: Ziel ausführen
commands: nks.tools.makeTargets, nks.tools.makeRepeat
1. Werkzeuge, Makefile-Ziel ausführen listet die Ziele des Makefile zur geöffneten Datei: die Datei selbst, wenn sie ein Makefile ist (Makefile, GNUmakefile, *.mk, *.mak), sonst das nächste GNUmakefile, makefile oder Makefile in ihrem Ordner oder in den Ordnern darüber. Die Suche endet an der Wurzel des Projekts (ein Ordner mit .textida oder .git). Ist das Makefile geöffnet, werden die Ziele aus dem Text des Tabs gelesen. Das gewählte Ziel läuft als make -f mit dieser Datei und dem Ziel im Ordner des Makefile.
2. Letztes Makefile-Ziel wiederholen führt das zuletzt gestartete Ziel noch einmal aus.
3. Der Lauf geht über den Werkzeug-Starter: Der Befehl wird gezeigt und beim ersten Mal bestätigt, die Ausgabe geht in den Tab Aufgaben, Stopp beendet make und alles, was es gestartet hat.
4. Eine Ausgabezeile mit Datei und Zeile (main.c:12:5: error, Makefile:7:) öffnet diese Stelle per Klick oder Enter. Ein rekursives make meldet „Entering directory“ und „Leaving directory“; ein relativer Dateiname nach einer solchen Zeile wird in diesem Ordner gesucht.
- Make muss installiert und im PATH sein oder in der Einstellung Make-Programm stehen (zum Beispiel mingw32-make). Spezielle Ziele (.PHONY), Musterregeln und Namen mit Variable oder Platzhalter werden nicht aufgelistet. Die Bestätigung gilt dem Befehl, nicht dem Inhalt des Makefile, denn ein Makefile auszuführen heißt auszuführen, was darin steht.

## [packages.registry] Paketmanifeste: Namen, Versionen und Marke für neuere Versionen
commands: nks.packages.lookup, nks.packages.refresh, nks.packages.revoke
1. In package.json, composer.json, requirements.txt, einer .csproj-Datei, go.mod, Cargo.toml, Gemfile und pubspec.yaml kann der Editor das Paketregister (npm, Packagist, PyPI, NuGet, Go-Proxy, crates.io, RubyGems, pub.dev) nach Namen und Versionen fragen. Ohne Ihre Zustimmung wird nichts gesendet: Werkzeuge, Paketversionen prüfen zeigt die Server und fragt einmal je Register.
2. Nach der Zustimmung bietet Strg+J im Namen eines Eintrags Namen aus dem Register an und in einer Version die Versionen dieses Pakets, die neueste zuerst. PyPI und der Go-Proxy haben keine Suche: ein Name wird nur angeboten, wenn der getippte Text der ganze Name ist.
3. Ein Eintrag mit genau festgelegter Version (1.2.3, ==1.2.3, v1.2.3), zu dem es ein neueres Release gibt, bekommt an der Version eine Informationsmarke. Bereiche (^, ~, >=) werden nicht markiert. Eine Vorabversion gilt nicht als neuere Version, solange es ein Release gibt. Die Prüfung läuft, wenn Sie aufhören zu tippen.
4. Antworten bleiben im Cache im Speicher, höchstens bis zu einer Größengrenze (die ältesten fallen zuerst heraus), nur begrenzte Zeit und längstens bis zum Schließen des Editors. Ein Angebot oder eine Marke aus dem Cache sagt das („cache“). Paketversionen aktualisieren verwirft den Cache dieses Registers und fragt erneut.
5. Registerabfragen und Schema-Downloads verbieten nimmt jede Zustimmung zurück und verwirft den Cache. Es gilt der Proxy aus dem Befehl Netzwerk-Proxy; ein Proxy mit Benutzernamen lässt sich nicht verwenden, die Abfragen stoppen dann.
- Es wird nichts heruntergeladen oder installiert, nach Schwachstellen wird nicht gesucht und go mod download läuft nie. Zum Register gelangen nur Paketnamen, der getippte Text und Ihre Netzwerkadresse.
- Ein Register, das ausfällt, bietet nichts an; Paketversionen prüfen nennt den Fehler in der Statusleiste. Zeitlimit, Größenlimit, Größe und Lebensdauer des Caches und Zahl der Pakete je Prüfung sind vorläufig.

## [tools.schema] Schemas für JSON, YAML und TOML: Prüfung, Vervollständigung und Beschreibungen
commands: nks.schemas.allow
1. Eine JSON-, JSONC-, TOML- oder YAML-Datei wird gegen das Schema geprüft, das zu ihr passt. Die Datei kann ihr Schema selbst nennen („$schema“ am Anfang einer JSON-Datei, der Kommentar „# yaml-language-server: $schema=ADRESSE“ in YAML, „#:schema ADRESSE“ in TOML); sonst entscheidet der Dateiname (package.json, Cargo.toml, GitHub-Workflows, die Dateien von Claude Code, MCP und Codex und eine kurze Liste üblicher Konfigurationsdateien). Die Statusleiste zeigt den Namen des Schemas oder „kein Schema“. Ein Befund wird unterstrichen, und sein Hinweis nennt die Stelle, bei einem Syntaxfehler auch Zeile und Spalte.
2. Strg+J bietet die Schlüssel an, die das Schema im Objekt um den Cursor erlaubt, und dort, wo ein Wert geschrieben wird, auch die erlaubten Werte (enum-Werte, true und false). Ruht der Mauszeiger auf einem Schlüssel oder Wert, zeigt sich, was das Schema dazu sagt: Beschreibung, Typ, erlaubte Werte und Standardwert.
3. Die Schemas von package.json, Cargo.toml, GitHub-Workflows und den Agentendateien gehören zum Editor. Das Schema einer anderen bekannten Datei und ein Schema, das eine Datei mit einer Adresse nennt, wird erst nach Ihrer Zustimmung geladen. Bis dahin sagt die Statusleiste „Download nicht erlaubt“, und Extras, Pakete, Schema-Download erlauben zeigt den Server und fragt einmal je Server. Registerabfragen und Schema-Downloads verbieten nimmt jede Zustimmung zurück. Ein Schema, das eine Datei mit einem Pfad nennt, wird von der Festplatte gelesen und braucht keine Zustimmung.
4. Ein geladenes Schema wird auf diesem Computer gespeichert, höchstens bis zu einer Größengrenze (die ältesten werden zuerst gelöscht), und solange es aktuell ist ohne Anfrage wiederverwendet. Antwortet der Server nicht, wird die gespeicherte Kopie benutzt, und die Statusleiste sagt das. Es gilt der Proxy aus dem Befehl Netzwerk-Proxy; ein Proxy mit Benutzernamen lässt sich nicht verwenden.
5. Ein Syntaxfehler in einer JSON-, JSONC-, TOML- oder YAML-Datei wird auch dann unterstrichen, wenn kein Schema gilt. YAML mit Ankern, Tags oder komplexen Schlüsseln lässt sich nicht lesen: Eine solche Datei wird nicht geprüft, und die Statusleiste sagt das.
- Eine Teilmenge von JSON Schema wird verstanden: Typen, enum, required, properties, items, oneOf, anyOf, allOf, if/then, lokales $ref und pattern. Verweise auf andere Dateien oder Adressen innerhalb eines Schemas werden übergangen. Größengrenzen, Lebensdauer der gespeicherten Kopien und Wartezeiten sind vorläufig.

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
6. Ein Befehl eines installierten Plugins, das Dokumente lesen oder ändern darf, arbeitet mit dem aktiven Tab, und das Plugin sieht dieses Dokument nur, solange der Befehl läuft. Eine Änderung, die es vorschlägt, erscheint in der Änderungsprüfung und wird erst geschrieben, wenn Sie sie anwenden. Eine direkte Änderung (document.edit.direct) ist ein Schritt für Rückgängig. Endet das Plugin, bevor Sie entscheiden, verfällt sein Vorschlag, und nichts wird geschrieben.
- Berechtigungen halten fest, was Sie erlaubt haben, und sind keine Sandbox: Ein Plugin ist ein Programm, das mit Ihren Benutzerrechten läuft. Installieren Sie nur Plugins, denen Sie vertrauen. Signaturen werden noch nicht geprüft. Die Grenzen für Größe und Dateianzahl einer ZIP sind vorläufig. Neue Befehle eines aktualisierten Plugins erscheinen nach einem Neustart.
- Ein Plugin kann Tasten und Einstellungen mitbringen. Seine Tasten (contributes.keybindings, optional mit "when") funktionieren wie die Tasten des Editors, aber eine Taste des Editors, des Profils oder Ihrer keymap.json gewinnt immer gegen eine Plugin-Taste; der Konflikt steht beim Plugin im Plugin-Manager. Seine Einstellungen (contributes.settings, Schlüssel beginnen mit der Plugin-ID) erscheinen unter dem Namen des Plugins in den Einstellungen, mit dem Standardwert aus dem Manifest, und ein laufendes Plugin bekommt die Werte beim Start und danach als Ereignis settings.changed. Ein Befehl, der im Manifest ein Werkzeug nennt (contributes.externalTools), startet es ohne Plugin-Code, nachdem Sie Befehl und Verzeichnis bestätigt haben.
- Jedes Plugin läuft als eigener Prozess. Endet es oder antwortet es nicht mehr, beendet der Editor es, eine Leiste nennt das Plugin und sagt, wann es neu startet; nach wiederholten Abstürzen bleibt es ausgeschaltet. Ausgabe anzeigen in der Leiste öffnet das Ausgabefenster mit den letzten Fehlerzeilen des Plugins. Ein Plugin, das auf Ihre Antwort wartet (eine Änderungsprüfung, eine Frage), wird nicht beendet, weil es nicht antwortet. Ein laufender Befehl wird nach dem Neustart nicht wiederholt, es wird also nichts doppelt geschrieben oder hochgeladen. Die Zahl der Abstürze, die Wartezeiten und die Zeitlimits sind vorläufig.

## [tools.lint] Linter: Funde von PHPStan, Psalm, golangci-lint, Clippy, clang-tidy, RuboCop, ESLint, Ruff, ShellCheck, SQLFluff und dart analyze unterstreichen
commands: nks.tools.lint
1. Werkzeuge, Datei mit Linter prüfen startet den Linter, der zur geöffneten Datei passt: PHPStan oder Psalm für PHP, golangci-lint für Go, Clippy für Rust (braucht eine Cargo.toml im Ordner der Datei oder darüber), clang-tidy für C und C++, RuboCop für Ruby, ESLint für JavaScript und TypeScript, Ruff für Python, ShellCheck für Shell-Skripte, SQLFluff für SQL und dart analyze für Dart. Verarbeiten zwei Linter die Datei, gilt der erste, dessen Konfigurationsdatei (phpstan.neon, psalm.xml) gefunden wird, sonst der erste installierte.
2. Der Linter prüft die Datei auf dem Datenträger, also zuerst speichern: Ein Dokument mit ungespeicherten Änderungen wird nicht geprüft. Die Funde werden im Text unterstrichen (die Meldung erscheint, wenn der Zeiger darauf ruht) und auf dem Tab Aufgaben aufgelistet, einer pro Zeile; Klick oder Eingabe öffnet die Stelle. Der nächste Lauf desselben Linters ersetzt sie, ein behobenes Problem verschwindet danach. Bearbeiten Sie die Datei während der Prüfung, wandern die Funde mit dem Text mit, und Funde, deren Text Sie geändert haben, verschwinden.
3. Um nach jedem Speichern zu prüfen, schalten Sie unter Datei, Aktionen beim Speichern „Mit PHPStan prüfen“ (oder den anderen Linter) ein oder schreiben {"kind": "lint", "id": "phpstan"} in saveActions.json. Ein Linter läuft nie bei einer Änderung. Die Kennungen sind phpstan, psalm, golangci-lint, clippy, clang-tidy, rubocop-lint, eslint, ruff, shellcheck, sqlfluff und dart-analyze. Eine Prüfung, die ein Speichern startet, wartet eine halbe Sekunde auf ein weiteres Speichern derselben Datei; das neuere Speichern ersetzt sie.
- Das Programm wird im angegebenen Pfad gesucht, dann in vendor/bin, node_modules/.bin und .venv (vom Ordner der Datei aufwärts) und im PATH; es wird nichts heruntergeladen. Den Pfad geben Sie in der saveActions.json des Profils an: "toolPaths": {"phpstan": "C:\\werkzeuge\\phpstan.phar"} (eine Datei oder der Ordner, der das Programm enthält). Das gilt auch für Formatierer, und eine Projektdatei kann es nicht setzen. Ein Pfad, den es nicht gibt, wird gemeldet und nicht durch eine Suche ersetzt. Beim ersten Lauf müssen Sie den Befehl bestätigen.
- Ohne lintTimeoutSeconds in saveActions.json hat ein Linter ein vorläufiges Limit von 60 Sekunden; der endgültige Wert ist noch nicht entschieden. Ein Linter, der das Limit überschreitet, wird gestoppt und gemeldet; der Editor bleibt dabei bedienbar, und die Funde des früheren Laufs bleiben.
- PHPStan meldet eine Zeile und keine Spalte, daher wird der Text der ganzen Zeile unterstrichen. Ein Programm, das eine Batchdatei ist (phpstan.bat, eslint.cmd), wird wie bei jedem Werkzeug über cmd.exe gestartet.
- Nur dart analyze wurde mit dem echten Programm geprüft (Dart 3.12.2); die anderen Linter werden nach dem dokumentierten Format ihrer Ausgabe gelesen.

## [file.saveActions] Aktionen beim Speichern: formatieren, Werkzeug ausführen, hochladen
commands: nks.file.saveActions
1. Datei, Aktionen beim Speichern listet auf, was rund um das Speichern läuft: Formatieren, ein Werkzeug, Hochladen auf den Server. Eine Zeile schaltet die Aktion ein oder aus; die Zeile darunter schaltet „nur beim manuellen Speichern“ um. Ein Formatierer wird nur angeboten, wenn seine Konfigurationsdatei im Ordner der Datei oder darüber liegt, und er bleibt aus, bis Sie ihn einschalten: gofmt (go.mod), rustfmt (rustfmt.toml), clang-format (.clang-format), RuboCop (.rubocop.yml), Prettier (.prettierrc), Biome (biome.json), Ruff (ruff.toml, pyproject.toml), shfmt (.editorconfig), SQLFluff (.sqlfluff), StyLua (stylua.toml), dart format (analysis_options.yaml).
2. Die Reihenfolge ist fest: zuerst die Formatierer, dann das Speichern, dann Werkzeuge und Hochladen. Ein Formatierer arbeitet an einer Kopie des Textes und wird nach Ablauf des Zeitlimits gestoppt. Läuft er ab, scheitert er (zum Beispiel an einem Syntaxfehler) oder ist er nicht installiert, wird die Datei unverändert gespeichert, und Statuszeile und Registerkarte Aufgaben nennen den Grund. Text, den Sie währenddessen getippt haben, wird nie überschrieben. Formatiert wird nur ein Dokument mit ungespeicherten Änderungen.
3. Das Hochladen sendet genau die Bytes, die dieses Speichern geschrieben hat, nie ältere oder neuere. Hat sich die Datei auf dem Datenträger nach dem Speichern geändert, wird nichts gesendet. Eine von einem Server geöffnete Datei geht an denselben Server zurück, eine Datei vom Datenträger an den Standardserver ihres Projekts (Schritt 6).
4. Ein automatisches Speichern führt die Aktionen aus, die nicht mit „nur beim manuellen Speichern“ markiert sind. Ein Speichern durch ein Plugin führt keine Aktion aus, so löst eine Aktion, die speichert, keine Schleife aus. Speichern unter ist ein manuelles Speichern: Es führt die Aktionen des Ordners aus, in den Sie speichern.
5. Die Aktionen stehen in saveActions.json im Profil (Datei der Speicheraktionen öffnen) und für ein Projekt in .textida/saveActions.json im Ordner der Datei oder darüber. Ein Eintrag des Projekts ersetzt den Eintrag des Profils mit gleicher Art und Kennung. Beispiel: {"formatTimeoutSeconds": 10, "actions": [{"kind": "format", "id": "gofmt"}, {"kind": "tool", "id": "lint", "onlyManual": true}, {"kind": "upload"}]}. Ein Werkzeug ist eines aus Werkzeug ausführen, nach seiner Kennung.
6. Der Standardserver eines Projekts steht in der .textida/saveActions.json des Projekts: {"kind": "upload", "server": "Web", "remoteFolder": "/var/www/web"}. server ist der Name einer gespeicherten Verbindung, remoteFolder der Ordner auf dem Server, der dem Projektordner entspricht (dem Ordner mit .textida). Die Datei src/a.php geht dann nach /var/www/web/src/a.php. Dateien im Ordner .textida werden nicht hochgeladen, und im Profil lässt sich kein Server eintragen.
- Die Projektdatei kommt mit dem Projekt, deshalb fragt das erste Hochladen, ob die Dateien des Projekts auf diesen Server dürfen. Die Zustimmung gilt für diesen Ordner, diese Verbindung und diesen Ordner auf dem Server. Nach einem automatischen Speichern wird niemand gefragt und nichts gesendet.
- Der Editor merkt sich die Version, die das Projekt zuletzt auf den Server geschickt hat. Hat sich die Datei auf dem Server seitdem geändert, oder hat das Projekt sie nie geschickt und sie weicht ab, wird nichts überschrieben: Ein manuelles Speichern bietet die Unterschiede oder trotzdem Hochladen an, nach einem automatischen Speichern meldet es die Statuszeile. Eine gleiche Datei auf dem Server wird nur vermerkt.
- Ohne formatTimeoutSeconds hat ein Formatierer ein vorläufiges Limit von 10 Sekunden; der endgültige Wert ist noch nicht entschieden. Der Wert im Beispiel ist nur ein Beispiel. Das Programm eines Formatierers wird im PATH gesucht oder aus toolPaths in der saveActions.json des Profils genommen, und nichts wird heruntergeladen.
- clang-format formatiert auch Code, der sich nicht übersetzen lässt, und meldet einen Fehler nur bei einer kaputten Konfiguration. dart format endet bei Code, den es nicht lesen kann, mit Code 65, und die Datei wird unformatiert gespeichert.
- Nach einem automatischen Speichern kann niemand antworten: Ein nie bestätigtes Werkzeug startet nicht, und eine neuere Version auf dem Server erscheint in der Leiste der Registerkarte statt in einem Dialog.
- Das Umschalten einer Aktion in der Liste ändert in saveActions.json im Profil nur diesen einen Wert oder fügt die Aktion am Ende der Liste an. Kommentare und der Rest der Datei bleiben. Aktionen aus einem Projekt werden aufgelistet, aber in der Projektdatei geändert.
- Aktionen beim Speichern bietet auch „Mit ... prüfen“ für die Linter an, die die Datei verarbeiten; sie ist aus, bis Sie sie einschalten. Die Prüfung läuft nach dem Speichern und hält es nicht auf; ihr Zeitlimit ist lintTimeoutSeconds (ohne die Angabe vorläufig 60 Sekunden).
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

## [log.live] Live-Log: Verfolgen, gespeicherte Anzeigen, Rotation
commands: nks.log.follow, nks.log.saveView, nks.log.views, nks.log.nextMatch, nks.log.previousMatch
1. Log: verfolgen liest die Datei erneut, sobald Zeilen hinzukommen, und setzt den Cursor ans Ende. Das geht bei jeder Dateigröße, auch im seitenweisen Modus. Es braucht ein als Datei gespeichertes Dokument ohne ungespeicherte Änderungen; bei ungespeicherten Änderungen hält es an und die Statuszeile sagt es. Der Befehl noch einmal beendet das Verfolgen.
2. Log: Anzeige speichern fragt nach einem Namen, einem regulären Ausdruck und den Stufen, die bleiben (error, warning, info, debug; leer heißt alle). Die Anzeige wird in logviews.json im Profil gespeichert, zusammen mit den Stufenregeln des Dokuments und der Cursorposition, und wird die Live-Anzeige des Dokuments.
3. Log: Anzeigen listet die gespeicherten Anzeigen. Die gewählte setzt Stufenregeln, Filter und Cursorposition und liest die Datei einmal, um die passenden Zeilen zu finden. Wurde die Anzeige für eine andere Datei gespeichert, wird die Position nicht verwendet.
4. Log: nächster Treffer der Anzeige und Log: vorheriger Treffer der Anzeige springen zwischen den Zeilen, die der Filter behält. Wächst die Datei, werden nur die neuen Zeilen untersucht; der früher gelesene Teil wird nicht noch einmal gelesen. Eine letzte Zeile ohne Zeilenende wird erst übernommen, wenn sie endet.
5. Kürzt ein anderes Programm das Log oder ersetzt es (Rotation: die Datei wird umbenannt und unter dem alten Namen entsteht eine neue), sagt es die Statuszeile, die früheren Treffer werden verworfen und das Lesen beginnt am Anfang des neuen Inhalts. Eine fehlende Datei wird einmal gemeldet, dann wird gewartet.
- Eine Rotation erkennt man an den ersten 4096 Bytes der Datei und unter Windows an der Erstellungszeit. Eine neue Datei, die mit denselben Bytes beginnt und nicht kürzer ist als die alte, ist davon nicht zu unterscheiden; eine ersetzte Datei gleicher Länge mit gleicher Zeit und gleichen Bytes fällt nicht auf.
- Die Datei wird einmal pro Sekunde angesehen. Dieser Wert und die 4096 Bytes sind keine entschiedenen Werte.
- Die Treffer liegen im Speicher ohne Obergrenze.
- Log: Anzeige speichern fragt auch nach einem Text zum Hervorheben (ein regulärer Ausdruck; leer markiert nichts). Wird die Anzeige angewendet, erhalten die Treffer des Ausdrucks in jeder Zeile einen Hintergrund, im Editor und im seitenweisen Modus. Gesucht wird nur in den ersten 8192 Zeichen einer Zeile, und eine Zeile, deren Suche zu lange dauert (Platzhalterwert, nicht entschieden), bleibt unmarkiert.

## [export.highlight] Export und Kopie mit Hervorhebung
commands: nks.file.exportHtml, nks.file.exportRtf, nks.edit.copyFormatted, nks.edit.copyHtmlSource
1. Bearbeiten, Kopieren als, Als HTML exportieren und Als RTF exportieren schreiben die Auswahl, oder das ganze Dokument, wenn nichts markiert ist, in eine Datei Ihrer Wahl. Die Farben sind die des Editors im aktuellen hellen oder dunklen Design: dieselben Tokens der Grammatik des Dokuments, in einem Log die Farben der Stufen. Die Seiten- und Textfarbe des Editors ist enthalten.
2. Bearbeiten, Kopieren als, Mit Formatierung kopieren legt Text, HTML und RTF zusammen in die Zwischenablage. Das Einfügen in eine Textverarbeitung oder ein Mail behält die Farben, das Einfügen in einen Editor ergibt einfachen Text. Als HTML-Quelltext kopieren legt das HTML selbst als Text in die Zwischenablage.
- Das ganze Dokument wird gefärbt und die Auswahl daraus geschnitten, eine Auswahl, die in einem Kommentar beginnt, ist also wie im Editor gefärbt. Bei einer großen Datei (seitenweiser Modus) und bei mehreren Cursorn oder einer Blockauswahl wird nur der markierte Text genommen und ab seinem eigenen Anfang gefärbt.
- Ein Text über 8388608 Zeichen wird mit einer Meldung abgelehnt; markieren Sie einen Teil. Die Grenze ist ein Platzhalter, kein entschiedener Wert. Die Arbeit läuft im Hintergrund, das Fenster bleibt bedienbar.
- HTML ist UTF-8. RTF ist ASCII: Rückwärtsschrägstrich, geschweifte Klammern und jedes Zeichen außerhalb von ASCII (zum Beispiel tschechische Buchstaben) werden als Escape geschrieben.

## [log.full] Voller Log-Modus: Einklappen, Stacktraces, Heatmap, ANSI-Farben
commands: nks.log.fold, nks.log.unfold, nks.log.openFrame, nks.log.showRaw
1. Log: Uninteressante Zeilen einklappen fragt nach einer Stufe (Zeilen mindestens dieser Stufe bleiben: error, warning, info, debug) und einem regulären Ausdruck (passende Zeilen bleiben). Jeder Abschnitt der übrigen Zeilen wird zu einer Platzhalterzeile, die ein Klick öffnet. Eine Zeile ohne Stufenwort, etwa ein Stacktrace-Frame, gehört zum Eintrag darüber. Log: Alle Zeilen anzeigen öffnet wieder alles. In einer seitenweise geladenen (sehr großen) Datei lassen sich Zeilen nicht einklappen; die Zeilen, die eingeklappt würden, werden stattdessen abgeblendet gezeichnet.
2. Strg+Klick auf einen Stacktrace-Frame von .NET, Java, Python, PHP oder Node.js öffnet die Datei an dieser Zeile; Log: Datei der Stacktrace-Zeile öffnen tut dasselbe für die Zeile mit dem Cursor. Ein relativer Pfad wird im Ordner des Logs gesucht, dann in den Arbeitsbereich-Ordnern; ein bloßer Dateiname (Java) wird darunter gesucht.
3. Der Streifen am rechten Rand der Minimap zeigt, wo Fehlerzeilen (rot) und Warnungen (gelb) dicht liegen. Er wird im Hintergrund gezählt. Eine Datei bis 256 MiB wird ganz gelesen, eine größere nur stichprobenartig; das Bild ist dann eine Schätzung. Diese Grenzen sind keine entschiedenen Werte.
4. ANSI-Escape-Sequenzen in einem Log werden ausgeblendet und als Farben (16, 256 und True Color), Fett und Unterstreichung gezeichnet. Die Datei wird nie verändert, Speichern behält die Sequenzen also. Log: ANSI-Escapes unverändert anzeigen wechselt zwischen Farben und den reinen Sequenzen. Ein Stil, den eine Zeile offen lässt, geht nicht in die nächste Zeile über.

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
- Ein unterbrochener Stapel von Dateiänderungen (Ersetzungen in Dateien sowie die Dateien, die eine Arbeitsbereichsänderung eines Plugins anlegt, umbenennt oder löscht) wird nach dem Start angeboten: Abschließen oder Zurücksetzen. Eine Datei, die inzwischen jemand anderes geändert hat, bleibt unverändert.
### Große Änderungen
- Sind die ungespeicherten Änderungen eines Dokuments größer als das Wiederherstellungslimit (64 MiB, vorläufiger Wert), zum Beispiel ein Alles ersetzen oder ein großes Einfügen in einer großen Datei, kommt sein Tab nicht von selbst zurück. Eine Leiste nennt die Datei, die Größe der Änderungen und wann sie entstanden, und bietet Änderungen wiederherstellen und Änderungen verwerfen an. Bis Sie wählen, wird nichts aus dem Journal gelesen, und die Datei bleibt unberührt.
- Änderungen wiederherstellen liest das Journal im Hintergrund, das Programm arbeitet also weiter, und öffnet dann den Tab mit den ungespeicherten Änderungen wie nach jeder anderen Wiederherstellung. Gelingt das nicht (die Datei hat sich inzwischen geändert, ein Teil des Journals fehlt oder ist beschädigt, der Speicher reicht nicht), sagt die Leiste warum, und die Änderungen bleiben im Journal.
- Änderungen verwerfen fragt noch einmal nach, weil nichts die Änderungen zurückbringt; dann werden sie aus dem Journal gelöscht. Die Datei auf dem Datenträger ändert keine der beiden Wahlen.
- Das Schließen der Leiste entscheidet nichts: Die Leiste kommt beim nächsten Start wieder.
- Ein Alles ersetzen mit sehr vielen Treffern wird als Liste in einer eigenen Datei ins Journal geschrieben, nie als ein riesiger Eintrag, das Journal funktioniert also bei jeder Zahl von Treffern. Eine beschädigte oder fehlende Liste hält nur dieses Dokument vom Zurückkehren ab, mit dem Grund.

## [paged] Große Dateien (Seitenmodus)
Eine Datei über der Grenze für große Dateien (Einstellung Grenze für große Dateien, standardmäßig 6 MiB) öffnet sich im Seitenmodus. Um eine Datei ohne diese Einschränkungen byteweise zu durchsuchen und zu bearbeiten, öffnen Sie sie in Hex (Thema Hex: Öffnen und Bewegen). Das Programm liest nur die Zeilen auf dem Bildschirm, deshalb öffnet sich die Datei schnell und füllt den Speicher nicht. Bereits gelesene Teile der Datei, etwa beim Nummerieren der Zeilen, gibt es an das System zurück, deshalb wächst der belegte Speicher nicht mit der Größe der Datei.
Die Statusleiste zeigt dann Große Datei, ihre Größe und die Revision.
### Was geht
- Bewegen mit den Pfeilen, Bild auf, Bild ab, Pos1, Ende sowie Strg+Pos1 und Strg+Ende. Gehe zu Zeile, Prozent oder Byte (Strg+G).
- Tippen, Löschen (Rücktaste und Entf), Eingabetaste und Tab an der Cursorposition. Rückgängig und Wiederholen. Speichern.
- Suchen (Strg+F). In Dateien suchen.
- Auswählen (Umschalt mit den Pfeilen, Maus, Strg+A), Kopieren, Ausschneiden und Einfügen. Eine Auswahl über dem Kopierlimit (256 MiB, vorläufiger Wert) wird nicht kopiert, und die Statusleiste sagt es. Eine lange Auswahl wird im Hintergrund gelesen, und Ausschneiden löscht nur, wenn sich der Text inzwischen nicht geändert hat.
- Eine Zeile über 16 KiB verschieben Sie mit der Leiste unter dem Text zur Seite. Ein weit entfernter Teil der Zeile zeigt kurz …, bis er gelesen ist. Seine Farben erscheinen, sobald das Programm die Zeile bis dorthin gelesen hat.
- Eine Zeile von Dutzenden oder Hunderten MiB wird an Ort und Stelle bearbeitet: Tippen, Rücktaste, Entf, Eingabetaste, Tab, Einfügen, Auswählen und Rückgängig lesen nur den Text um den Cursor, nie die ganze Zeile, deshalb wächst der Speicher nicht mit der Länge der Zeile. Das erste Bewegen des Cursors in einen entfernten Teil einer solchen Zeile muss die Zeichen bis dorthin zählen, das dauert einen Moment; das Programm merkt sich die Zahl für die nächsten Bewegungen.
### Was nicht geht
- Blockauswahl, mehrere Cursor.
- Befehle für Zeilen, Einzug, Schreibweise, Ersetzen.
- Zur passenden Klammer springen, Zeilenumbruch.
- Konvertieren und erneutes Lesen der Codierung, Vereinheitlichen der Zeilenenden, Normalisierung.
- Speichern unter nur in derselben Codierung.
### Meldungen
- Die Zeilennummerierung läuft noch, Bearbeiten ist gleich möglich: Warten Sie, bis die Datei durchgelesen ist.
- Das Zeichen kann in der Codierung der Datei nicht geschrieben werden: Tippen Sie ein anderes Zeichen.
- Die Auswahl ist größer als das Kopierlimit: Wählen Sie weniger aus; die Zwischenablage blieb, wie sie war.
- Die Änderung ist zu groß, um rückgängig gemacht zu werden: Entfernen oder fügen Sie weniger Text auf einmal ein; das Dokument wurde nicht verändert.
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
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables, nks.snippet.next, nks.snippet.previous, nks.snippet.end
Ein Clip ist ein Stück Text mit Stellen für Variablen und Eingabemarken. Clips liefern Datenplugins im Ordner plugins des Profils; ein Unterordner pro Plugin, mit der Datei nkspad-plugin.json und ohne Programm.
1. Bearbeiten, Einfügen, Clip einfügen wählen. Die Liste zeigt die Clips mit einer Vorschau des Textes, der eingefügt würde.
2. Einen Clip wählen. Hat er ein Formular, ausfüllen: Die Vorschau folgt der Eingabe, ein falscher Wert wird markiert und OK bleibt gedimmt, bis jeder Wert zu seinem Typ passt.
3. Der Text geht in jede Eingabemarke und ersetzt jede Auswahl, als ein Rückgängig-Schritt. Die Eingabemarken stehen danach dort, wo der Clip die Marke $| hat.
- Auswahl als Clip speichern fragt nach einem Namen und fügt den markierten Text Ihrer eigenen Bibliothek hinzu (Plugin user.clips, Ordner user-clips). Der Text wird wörtlich gespeichert, ein Dollarzeichen bleibt also ein Dollarzeichen. Der Clip steht sofort in der Liste.
- Vorlagensprache: ${name} ist eine Variable, $| setzt eine Eingabemarke (beliebig viele), $$ ist ein Dollarzeichen. Zeilenumbrüche werden zum Zeilenumbruch des Dokuments.
- Snippets: Ein Clip (und der Plugin-Aufruf snippet/insert mit "format": "template") darf nummerierte Platzhalter verwenden: $1, ${1:Vorgabetext}, ${1|eins,zwei,drei|}, $0 (die Endposition). Dieselbe Nummer an mehreren Stellen wird gemeinsam bearbeitet. Nach dem Einfügen ist der erste Platzhalter markiert; Tab springt zum nächsten, Umschalt+Tab zum vorherigen, nach dem letzten geht die Eingabemarke zu $0. Esc oder ein Klick außerhalb beendet das Snippet. Ein Platzhalter mit Werteliste öffnet die Auswahlliste. Bei mehreren Eingabemarken hat jede ihr eigenes Snippet, die Platzhalter werden gemeinsam durchlaufen.
- Die Zeilen eines Snippets nach der ersten übernehmen die Einrückung der Zeile, in der es beginnt; Tabs in der eigenen Einrückung werden zur Einrückung des Dokuments (Tabs oder Leerzeichen). Ein Clip ohne nummerierte Platzhalter wird unverändert eingefügt.
- In einem Snippet ist $| keine Eingabemarke, und ein Dollarzeichen vor einer Ziffer ist ein Platzhalter: Schreiben Sie $$ oder \$ für ein Dollarzeichen. Neben ${name} gelten die Variablen TM_SELECTED_TEXT, TM_FILENAME, TM_FILENAME_BASE, TM_DIRECTORY, TM_FILEPATH, UUID und CURRENT_YEAR, CURRENT_YEAR_SHORT, CURRENT_MONTH, CURRENT_DATE, CURRENT_HOUR, CURRENT_MINUTE, CURRENT_SECOND (auch als $NAME). Verschachtelte Platzhalter und Transformationen werden nicht unterstützt; Text, der nicht zur Syntax passt, bleibt unverändert.
- Woher eine Variable ihren Wert hat, stärkste zuerst: 1. der im Formular des Clips eingegebene Wert, 2. Ihre eigenen Variablen (Bearbeiten, Einfügen, Benutzervariablen bearbeiten öffnet die Datei variables.json des Profils: ein JSON-Objekt aus Name und Text), 3. die eingebauten. Derselbe Name gibt in einem Clip, einem Rezept und einem externen Werkzeug denselben Wert.
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
1. Bearbeiten, Umwandlungen und Rezepte, Umwandeln wählen. Die Liste zeigt die Umwandlungen mit einer Vorschau am markierten Text (am ersten Beispiel des Konverters, wenn nichts markiert ist).
2. Eine wählen. Alle Auswahlen ändern sich in einem Schritt für Rückgängig. Lässt sich eine nicht umwandeln, wird nichts geändert und die Statuszeile nennt den Grund.
- Letzten Befehl wiederholen führt die zuletzt gelaufene Umwandlung ohne die Liste aus.
- Konverter sind Daten: contributes.converters in nkspad-plugin.json, mit id, title, rules und optional samples und command. Eine Regel ist {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Die Regeln laufen der Reihe nach, jede auf dem Ergebnis der vorigen. Mit regex gilt der Dialekt der Suche: replace darf $1, ${name}, $& und die Groß-/Kleinschreibungs-Codes \U \L \E \u \l nutzen.
- Ein Beispiel ist {"input": "...", "expected": "..."}. Beim Lesen des Plugins wird jedes Beispiel umgewandelt. Ein Konverter, dessen Beispiel einen anderen Text ergibt, der keine Regeln hat, ein leeres find oder einen falschen regulären Ausdruck, wird abgelehnt und das Plugin mit dem Grund in der Statuszeile übersprungen.
- Ein Konverter mit "command" bekommt auch einen Befehl dieses Namens, der sofort umwandelt, ohne Liste.
- Die speziellen Umwandlungen des Editors selbst stehen in derselben Liste (Thema Spezielle Umwandlungen).

## [edit.specialConversion] Spezielle Umwandlungen
commands: nks.edit.convert.decToHex, nks.edit.convert.hexToDec, nks.edit.convert.urlEncode, nks.edit.convert.urlDecode, nks.edit.convert.quotedPrintableEncode, nks.edit.convert.quotedPrintableDecode, nks.edit.convert.base64Encode, nks.edit.convert.base64Decode, nks.edit.convert.removeAccents, nks.edit.convert.tabsToSpaces
Umwandlungen, die der Editor selbst hat. Jede ist ein Befehl in der Befehlspalette und im Menü Bearbeiten, Umwandlungen und Rezepte, und steht auch in der Liste Umwandeln. Sie ändert den Text aller Auswahlen auf einmal als ein Rückgängig-Schritt, oder die ganze Datei, wenn nichts markiert ist. Der neue Text bleibt markiert.
- Dezimal nach Hexadezimal und zurück: Die Auswahl muss eine ganze Zahl von 0 bis 18446744073709551615 (FFFFFFFFFFFFFFFF) sein, eine hexadezimale darf mit 0x beginnen. Leerzeichen und Zeilenenden um die Zahl bleiben. 255 ergibt FF, ACE ergibt 2766.
- Text in URL-Kodierung schreibt alles außer den Buchstaben A bis Z, Ziffern und - _ . ~ als %XX der UTF-8-Bytes, ein Leerzeichen als %20 und ein Zeilenende als %0D%0A, oder %0A in einer Datei mit LF-Zeilenenden. URL-Kodierung in Text liest %XX zurück; ein % ohne zwei Hexadezimalziffern bleibt, wie es ist.
- Text in Quoted-Printable behält druckbares ASCII und Leerzeichen und schreibt jedes andere UTF-8-Byte, Zeilenenden und = eingeschlossen, als =XX. Quoted-Printable in Text liest =XX und lässt = am Zeilenende weg; jedes andere = lehnt den Text ab.
- Text in Base64 kodiert die UTF-8-Bytes. Base64 in Text überspringt Leerzeichen und Zeilenenden darin und lehnt Text ab, der kein Base64 ist oder dessen Bytes kein UTF-8 sind.
- Akzente entfernen nimmt Akut, Hatschek, Kringel und Umlautpunkte von den Buchstaben (č zu c, ü zu u). Buchstaben ohne Zerlegung (ł, ø, ß) und die Zeichen anderer Schriften bleiben.
- Tabulatoren in Leerzeichen ersetzt jeden Tabulator durch Leerzeichen bis zum nächsten Tabstopp nach der Tabulatorgröße des Dokuments, gezählt ab der Spalte, in der die Auswahl beginnt.
- Wenn die Umwandlung eine Auswahl nicht lesen kann, ändert sich nichts und die Statusleiste sagt es. Letzten Befehl wiederholen führt dieselbe Umwandlung erneut aus.

## [edit.recipes] Bearbeitungsrezepte
commands: nks.edit.runRecipe
Ein Rezept wählt Teile der geöffneten Datei, ändert sie und zeigt das Ergebnis, bevor etwas geschrieben wird.
1. Bearbeiten, Umwandlungen und Rezepte, Bearbeitungsrezept ausführen wählen. Die Liste zeigt jedes Rezept mit den Änderungen, die es in dieser Datei machen würde.
2. Eines wählen. Das Vorschaufenster listet jede Änderung: Zeile, Spalte, alter und neuer Text. Mit Anwenden werden sie geschrieben.
3. Alle Änderungen sind ein Schritt für Rückgängig; Rückgängig bringt die Datei genau in den alten Zustand zurück. Die geänderten Teile bleiben markiert. Es wird nichts geschrieben, wenn sich die Datei inzwischen geändert hat oder das Rezept nicht laufen kann (Variable ohne Wert, zu langsames Muster).
- Ein Rezept arbeitet mit den Auswahlen, oder mit der ganzen Datei, wenn nichts markiert ist. Letzten Befehl wiederholen wendet das letzte Rezept ohne Vorschau an.
- Rezepte sind Daten: contributes.recipes in nkspad-plugin.json, mit id, title, steps und optional command. Wählende Schritte kommen zuerst. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} behält die Treffer innerhalb der bisherigen Teile. {"kind": "columns", "from": 3, "to": 5} behält die Spalten 3 bis 5 jeder Zeile (in Zeichen ab 1 gezählt; to 0 oder weggelassen heißt bis zum Zeilenende; eine kürzere Zeile ergibt nichts).
- Ändernde Schritte: {"kind": "convert", "converter": "id"} (ein Konverter desselben Plugins), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} und {"kind": "set", "text": "..."}, der den Teil durch einen Text in der Vorlagensprache der Clips ersetzt. Darin ist ${selection} der Teil selbst, und die Variablen haben dieselben Werte wie in einem Clip (Thema Clips); Einfügemarken $| werden ignoriert.
- Ein Rezept ohne Schritte, mit unbekannter Art, falschem Muster, unbekanntem Konverter, einem wählenden Schritt nach einem ändernden oder ohne ändernden Schritt wird beim Lesen des Plugins abgelehnt.

## [edit.recipeFiles] Rezepte über Dateien
commands: nks.edit.runRecipeFiles
Ein gespeichertes Rezept läuft erneut über eine andere Dateiauswahl. Sie sehen jede Änderung vorher; nichts wird geschrieben, bevor Sie sie anwenden.
1. Wählen Sie Bearbeiten, Umwandlungen und Rezepte, Rezept auf Dateien anwenden und ein Rezept: aus einem Daten-Plugin oder eine Datei im Ordner recipes des Profils.
2. Wählen Sie die Dateien: einen Ordner mit Dateimaske (zum Beispiel *.cs;*.txt; die Ignore-Dateien der Suche gelten), die offenen Tabs oder die Dateien der Suchergebnisse.
3. Der Tab Suchergebnisse listet die Änderungen Datei für Datei mit denselben Häkchen wie Replace in Files. Anwenden schreibt die angehakten. Eine Datei, die nach der Vorschau von jemand anderem geändert wurde, bleibt unberührt: sie wird als Konflikt gemeldet und die übrigen Dateien werden geschrieben. Offene Dokumente erhalten einen Rückgängig-Schritt.
- Eine Rezeptdatei ist JSON mit id, title und steps, dieselben Felder wie ein Rezept in nkspad-plugin.json, dazu optional version und eine Liste converters (die Konverter, die ihre convert-Schritte nennen). Eine Datei ohne version ist Version 1, das Format der ersten Rezepte, und lädt wie bisher. Eine Datei neuerer Version wird mit einer Meldung abgelehnt.
- Schritte: matches, columns, convert, replace und set arbeiten wie bei den Bearbeitungsrezepten (jede Datei ist ihr ganzer Text). Ein Schritt {"kind": "format", "formatter": "id"} lässt einen Formatierer der Speicheraktionen über das Ergebnis der vorherigen Schritte laufen; er muss der letzte sein, sein Zeitlimit ist formatTimeoutSeconds in saveActions.json (ohne es vorläufig 10 Sekunden) und arbeitet mit Dateien mit Namen und einer Endung, die dieser Formatierer kennt. Eine Datei, in der ein Schritt nicht läuft, wird ausgelassen und mit Grund gemeldet.
- Jede Datei behält ihre Kodierung und ihre Zeilenenden; ein neues Zeichen, das die Kodierung nicht aufnehmen kann, erscheint als Änderung, die sich nicht anwenden lässt. Format-Schritte laufen bei Rezept ausführen für eine einzelne Datei nicht.

## [edit.aiContext] Als KI-Kontext kopieren
commands: nks.edit.copyAiContext
Legt die Auswahl, die Datei, die geöffneten Tabs oder den geöffneten Ordner als einen Block in Markdown oder XML in die Zwischenablage, zum Einfügen in einen KI-Assistenten. Es wird nichts gesendet.
1. Wählen Sie Bearbeiten, Als KI-Kontext kopieren und dann, was kopiert wird: die Auswahl (alle Auswahlen), Diese Datei, die Geöffneten Tabs oder den Geöffneten Ordner.
2. Die Vorschau zeigt, wie viele Dateien hineinkommen, die Größe als Markdown und als XML und was mit welchem Grund weggelassen wurde. Wählen Sie Als Markdown kopieren oder Als XML kopieren.
- Jede Datei hat Pfad, Zeilen, Sprache, Kodierung und Revision. Eine Datei mit nicht gespeicherten Änderungen ist markiert, und der Text ist der Text im Editor.
- Weggelassen und benannt: .env-Dateien (ihre Werte werden nie kopiert, auch nicht, wenn sie im Editor sichtbar sind), von Ignorierdateien ausgeschlossene Dateien und Ordner, versteckte und Binärdateien, Dateien über dem Größenlimit beim Durchsuchen des Ordners und nicht lesbare Dateien.
- Eine große Datei im seitenweisen Modus wird nie ganz kopiert: nur ihre Auswahl. Ohne Auswahl wird sie als weggelassen aufgeführt.
- Eine Datei oder Auswahl, die nicht mehr in die Größengrenze des ganzen Kontexts passt, wird weggelassen und mit der Grenze aufgeführt; eine kleinere Datei danach kann noch hineinpassen.

## [edit.fragment] Zeichenkette als JSON, SQL oder HTML bearbeiten
commands: nks.edit.fragmentJson, nks.edit.fragmentSql, nks.edit.fragmentHtml, nks.edit.fragmentWriteBack
Eine Zeichenkette in einer PHP- oder JavaScript-Datei öffnet sich in einem eigenen Tab in der gewählten Sprache und geht mit dem Escaping ihrer Art zurück in die Zeichenkette.
1. Setzen Sie den Cursor in die Zeichenkette und wählen Sie Bearbeiten, Zeichenfolge bearbeiten als, Zeichenkette als JSON bearbeiten, Zeichenkette als SQL bearbeiten oder Zeichenkette als HTML bearbeiten. Der neue Tab zeigt den Text, für den die Zeichenkette steht, ohne Anführungszeichen und Escapes.
2. Bearbeiten Sie den Text. Wählen Sie Bearbeiten, Zeichenfolge bearbeiten als, Fragment in die Zeichenkette zurückschreiben: Der Text wird für die Zeichenkette escaped und ersetzt ihren Inhalt. Der Tab bleibt offen, Sie können erneut schreiben.
- Arten: PHP-Zeichenketten in einfachen und doppelten Anführungszeichen, Heredoc und Nowdoc; JavaScript-Zeichenketten in einfachen und doppelten Anführungszeichen und Templates in Backticks. In einer Zeichenkette in doppelten Anführungszeichen und in einem Heredoc bekommt jedes Dollarzeichen einen Backslash, damit nie versehentlich eine Variable entsteht.
- Mit Meldung abgelehnt: eine Zeichenkette mit Variablen oder Ausdrücken (`$name`, `{$x}`), ein JavaScript-Template mit `${...}`, ein Escape, der kein Text ist (Byte über 127, Oktal) und beim Schreiben eine Zeile, die ein Heredoc oder Nowdoc beenden würde.
- Der Tab folgt der Zeichenkette bei Änderungen der Datei an anderer Stelle. Wurde die Datei nach dem Öffnen des Tabs innerhalb der Zeichenkette geändert, wird nichts ohne Rückfrage überschrieben: Der Dialog bietet Überschreiben oder Abbrechen.
- Wird die Datei geschlossen, bleibt der Fragment-Tab als getrennter Text offen. Der Titel sagt es, und Fragment in die Zeichenkette zurückschreiben schreibt nichts.

## [tools.emmet] Emmet-Abkürzungen
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. Geben Sie in einer HTML- oder CSS-Datei eine Abkürzung ein und drücken Sie Tab. Aus `ul>li*3` wird eine Liste mit drei Einträgen und dem Cursor im ersten; jeder weitere Tab springt zum nächsten Eintrag.
2. Tab rückt wie bisher ein, wenn vor dem Cursor keine Abkürzung steht, wenn Text markiert ist, wenn der Cursor in einem offenen Tag steht oder wenn die Abkürzung ungültig ist.
3. Extras, Struktur und Sprache, Emmet-Abkürzung als Vorschau zeigt, was aus der Abkürzung vor dem Cursor würde (`|` markiert die Stellen, die Tab besucht), und erweitert sie nach Bestätigung.
4. Extras, Struktur und Sprache, Mit Emmet-Abkürzung umschließen (nur HTML) fragt nach einer Abkürzung und umschließt damit jede Markierung. Der Text kommt in das innerste letzte Element; ein nacktes `*` an diesem Element (`ul>li*`) erzeugt ein Element pro Zeile.
5. Jede Erweiterung geht in alle Cursor gleichzeitig und ist ein Schritt für Rückgängig.
- HTML: `name#id.class[attr=value]{text}*3`; `>` Kind, `+` Geschwister, `^` eine Ebene höher, `( )` Gruppe; in Namen, Klassen und Text ist `$` die Nummer der Wiederholung (`$$` füllt mit Nullen, `$@-` zählt abwärts, `$@3` beginnt bei 3). Ein einzelnes Wort wird nur erweitert, wenn es ein HTML-Tag-Name oder ein Kurzname des Plugins ist (`bq`, `btn`, `a:link`, `input:checkbox`, `!` für ein Seitengerüst). In einem Element wird der fehlende Name je nach Eltern zu `li`, `tr`, `td`, `option`, `span` oder `div`.
- CSS: `m10-20` ist `margin: 10px 20px;`, `w100p` ist `width: 100%;`, `c#f00`, `pos:a`, `dn`, ein `!` am Ende fügt `!important` an, `+` verbindet mehrere Deklarationen. Eine ganze Zahl bekommt `px`, ein Bruch `em`; die Buchstaben `p`, `e`, `x`, `r` nach einer Zahl bedeuten `%`, `em`, `ex`, `rem`. Es funktioniert nur innerhalb eines Blocks `{ }`.
- Die Kurznamen sind die eigene Tabelle des Plugins mit gängigen Eigenschaften und Tags, nicht der volle Emmet-Umfang.
- Die Erweiterung ist ein Snippet: Tab besucht ihre leeren Stellen nacheinander, zuerst die leeren Elemente, dann die leeren Attributwerte, etwa `href` bei `a` (im Seitengerüst `!` den Titel nach dem Body). Bei mehreren Cursorn hat jede Erweiterung ihre eigenen Stellen und die Cursor rücken gemeinsam vor. Nach der letzten Stelle springt der Cursor ans Ende des erweiterten Textes. Solange Tab durch die Stellen geht, erweitert es nie: ein in eine Stelle getippter Tag-Name bleibt, wie er ist. Umschalt+Tab geht zurück, Esc beendet den Durchgang.
- Die Einstellung Emmet mit Tab schaltet das Erweitern mit Tab für alle Sprachen oder für eine aus; die beiden Befehle funktionieren weiter.
- Der Emmet-Prozess startet mit dem ersten Tab in einer HTML- oder CSS-Datei.

## [tools.git] Git: Status, Vergleich und Abschnitt vormerken
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk, nks.git.unstageHunk, nks.git.conflictOurs, nks.git.conflictTheirs, nks.git.conflictBoth, nks.git.markResolved, nks.git.conflictDelete
1. Öffnen Sie eine Datei aus einem Git-Repository und wählen Sie Werkzeuge, Git: Dateistatus. Das Panel zeigt, was `git status` für das ganze Repository meldet: was vorgemerkt und was im Arbeitsbaum geändert ist. Das Panel und die Statusleiste aktualisieren sich nach einer Änderung von außen von selbst, kurz nach der letzten Änderung.
2. Git: Mit Index vergleichen und Git: Mit HEAD vergleichen öffnen einen Vergleichs-Tab des offenen Textes mit seiner Version im Index oder in HEAD. Die Abschnitte sind dieselben wie in `git diff`.
3. Git: Abschnitt am Cursor vormerken schreibt nur die Änderung in den Index, in der der Cursor steht. Die übrigen Änderungen der Datei bleiben nur im Arbeitsbaum. Die Datei auf der Festplatte wird nicht verändert.
4. Hat ein anderes Programm den Index inzwischen geändert, wird nichts geschrieben. Das Plugin liest den Index neu und meldet einen Konflikt; prüfen Sie die Änderungen und wiederholen Sie den Befehl.
- Das Plugin schreibt nur in das Repository, wenn Sie einen Abschnitt vormerken oder zurücknehmen oder einen Konflikt lösen. Status und Vergleich lesen nur und erneuern nicht einmal die Indexdatei.
- Vorgemerkt wird der im Editor offene Text, gespeichert oder nicht. Eine Datei mit ungelöstem Merge-Konflikt wird nicht vorgemerkt.
5. Git: Abschnitt am Cursor aus dem Index nehmen nimmt nur die vorgemerkte Änderung, in der der Cursor steht (oder auf die er in der Datei zeigt), nach HEAD zurück. Die übrigen vorgemerkten Änderungen bleiben.
6. Setzen Sie bei einem Merge-Konflikt den Cursor in den Block zwischen `<<<<<<<` und `>>>>>>>` und wählen Sie Git: Konflikt, unsere Änderungen / ihre Änderungen / beide Seiten nehmen. Der Block wird durch die gewählte Seite ersetzt. Ist kein Block mehr übrig und die Datei gespeichert, wählen Sie Git: Datei als gelöst markieren; die Datei wird im Index vorgemerkt.
7. Doppelklick oder Enter auf eine Datei im Statuspanel öffnet sie. Die Statusleiste zeigt den Branch und Zähler: `+` vorgemerkt, `~` geändert, `?` nicht verfolgt, `!` Konflikte; sie wird nach jedem Git-Befehl aktualisiert.
8. In einer Datei aus einem Git-Repository markiert der linke Rand die Zeilen, die sich vom Index unterscheiden, mit einem gefüllten Balken (blau geändert, grün hinzugefügt, ein kurzer roter Balken dort, wo Zeilen entfernt wurden) und die schon vorgemerkten Zeilen mit einem Umriss. Ein Klick auf eine Markierung merkt die Änderung vor; ein Klick auf eine Umriss-Markierung nimmt sie aus dem Index. Die Einstellung Git-Änderungen am linken Rand anzeigen schaltet die Markierungen aus. Dateien über dem Größenlimit und Dateien mit verborgenen Werten (.env) haben keine.
9. Die Tabs Git: Mit Index vergleichen und Git: Mit HEAD vergleichen haben die Schaltflächen Abschnitt vormerken und Abschnitt aus dem Index nehmen für den Unterschied, auf dem der Tab steht (per Tastatur tun das die Befehle Vergleich: erste und zweite Plugin-Aktion im Menü Extras). Nach einer Schaltfläche wird der Vergleich an der Stelle des Tabs neu geöffnet.
10. Ein Konflikt, in dem eine Seite die Datei gelöscht hat (das Panel schreibt „Konflikt: von uns gelöscht“ oder „von ihnen gelöscht“) oder beide Seiten sie hinzugefügt haben und sie sich nicht zusammenführen lässt, hat keine Markierungen. Git: Konflikt, unsere Änderungen / ihre Änderungen / beide Seiten nehmen sagt das dann. Die Datei behalten Sie mit Git: Datei als gelöst markieren, löschen können Sie sie mit Git: Konflikt, Datei löschen. Eine von beiden Seiten gelöschte Datei zeigt das Panel, aber es gibt nichts zu öffnen; lösen Sie sie mit `git rm`.
11. Konfliktblöcke werden auch in Dateien gefunden, die als UTF-16 und UTF-32 gespeichert sind.
12. Mit `core.autocrlf` (oder einem `eol`-Attribut) wird ein Abschnitt einer Datei mit CRLF-Zeilenenden mit LF gespeichert, wie `git add` ihn speichert.
- Noch nicht vorhanden: Commit und Branches.
- Der Git-Prozess startet mit dem ersten Befehl oder wenn eine Datei geöffnet wird und die Markierungen am linken Rand eingeschaltet sind. Das Programm `git` muss im PATH liegen.

## [tools.lsp] Sprachserver: Definition, Verweise, Umbenennen, Vervollständigung und Diagnose
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.rename, nks.lsp.restart, nks.lsp.stop
1. Öffnen Sie ein Textdokument und wählen Sie Extras, Sprachserver, Sprachserver: für dieses Dokument starten. Der Server für die Sprache des Dokuments startet und erhält den Text. Von da an bekommt er jede Änderung, und was er als Fehler meldet, wird unterstrichen.
2. Sprachserver: Zur Definition springen springt zur Definition des Worts am Cursor, auch in eine andere Datei. Sprachserver: Verweise suchen listet alle Stellen im Panel links auf; Enter oder ein Doppelklick auf einen Eintrag öffnet ihn.
3. Sprachserver: Symbol umbenennen fragt nach dem neuen Namen des Worts am Cursor. Die Änderungen des Servers in allen Dateien, samt dem Anlegen, Umbenennen und Löschen von Dateien, das er verlangt, erscheinen gemeinsam in der Änderungsvorschau; was Sie annehmen, wird als ein Schritt angewendet. Eine neue Datei erscheint mit ihren ersten Zeilen als hinzugefügter Text. Der Tab einer offenen Datei folgt ihr beim Umbenennen; der Tab einer gelöschten Datei wird geschlossen, und hat er ungespeicherte Änderungen, fragt der Editor, ob er ihn schließen oder offen lassen soll. Eine Datei, die in einem anderen Tab offen ist, muss ebenfalls mit dem ersten Befehl gestartet worden sein, sonst wird das Umbenennen als Ganzes abgelehnt.
4. Strg+J (Bearbeiten, Einfügen, Wort oder Clip vervollständigen) listet auch auf, was der laufende Server am Cursor anbietet. Die Wahl eines solchen Eintrags führt im selben Rückgängig-Schritt auch die weiteren Änderungen aus, die der Server verlangt, etwa das Hinzufügen eines Imports. Eine Antwort, die nach einer Textänderung eintrifft, wird nicht angezeigt.
5. Beendet sich der Server von selbst, meldet das ein Hinweis, und die Unterstreichungen, die er gezeichnet hat, verschwinden. Sprachserver: Neu starten (oder ein anderer Befehl dieser Gruppe) startet ihn erneut und übergibt ihm alle offenen Dokumente so, wie sie jetzt sind. Sprachserver: Beenden fährt ihn herunter. Neu starten und Beenden gelten für den Server des Projekts, zu dem das aktuelle Dokument gehört; die Server anderer Projekte laufen weiter.
- Der Editor startet von sich aus keinen Server: Solange Sie keinen Befehl dieser Gruppe wählen, läuft nichts, und der Server weiß nichts von der Datei.
- Große Dateien, die seitenweise angezeigt werden, und Dateien über dem Größenlimit gehen nie an einen Server.
- Der Server wird über seinen Namen im PATH gesucht. Wird er nicht gefunden, nennt ein Hinweis das gesuchte Programm.
- Die Statusleiste zeigt LSP: läuft, LSP: nicht gefunden oder LSP: aus für den Server des letzten Befehls; der Tooltipp nennt seine Sprache und den Projektordner.
- Ein Server läuft für einen Projektordner. Das ist der nächste Ordner über der Datei, der eine Markierungsdatei der Sprache enthält (C#: zuerst eine Projektmappe `.sln` oder `.slnx`, dann `.csproj`; Rust: `Cargo.toml`; PHP: `composer.json`; Dart: `pubspec.yaml`; JavaScript und TypeScript: `tsconfig.json` oder `package.json`; C und C++: `compile_commands.json` oder `CMakeLists.txt`), sonst der nächste Ordner mit `.git` oder `.textida`, sonst der Ordner der Datei. Eine Markierung oberhalb eines Repositorys zählt nicht. Nach oben wird nur eine begrenzte Zahl von Ordnern durchsucht. Dokumente zweier Projekte erhalten zwei Serverprozesse, Dokumente eines Projekts teilen sich einen.
- Noch nicht vorhanden: die Wahl des Projektordners von Hand und das Umbenennen in Dateien, die nicht UTF-8 sind.

## [tools.lspPlugin] Sprachserver aus einem Plugin
1. Ein Plugin nennt seine Server unter `languageServers` in `nkspad-plugin.json` und verlangt die Berechtigung `language.server`. Jeder Server hat eine `id`, `languages` (IDs der Sprachmodi, mindestens eine), optional `globs` (etwa `*.foo` oder `src/**/*.fx`), `command`, `args`, optional `initializationOptions` (ein Objekt, das der Server in `initialize` erhält) und `locate`.
2. `locate` ist `path` (Standard): `command` ist ein bloßer Programmname, der im PATH gesucht wird. Bei `plugin` ist `command` ein Pfad im Plugin-Ordner.
3. Bei der Installation des Plugins zeigt der Editor die Berechtigung und wartet auf Ihre Antwort. Ohne sie wird der Server nie gestartet.
4. Die Befehle der Gruppe Sprachserver nutzen den Server für jedes Dokument, dessen Sprache genannt ist oder dessen Dateiname zu einem Muster passt. Ein Server aus einem Plugin hat Vorrang vor dem mit dem Editor gelieferten.
- Pro Sprache, Projektordner und Servereinstellung (Programm, Argumente, Umgebung und `initializationOptions`) läuft ein Serverprozess, den alle offenen Dokumente dieses Projekts teilen. Ein Dokument, das nur über ein Muster passt, wird für die erste Sprache des Servers bedient.
- Fehlt das Programm oder startet es nicht, nennt eine Meldung es einmal und die Bearbeitung geht weiter. Beendet es sich selbst, starten Sie einen beliebigen Befehl der Gruppe, dann startet es neu.
- Ein Server ohne offenes Dokument seines Projekts wird nach einer Zeit beendet und vom nächsten Befehl neu gestartet. Die Server anderer Projekte laufen weiter.
- Fragt der Server nach Einstellungen (`workspace/configuration`), erhält er den Teil von `initializationOptions`, den der verlangte Abschnitt nennt: `a.b` ist das Objekt `b` in `a`, eine Anfrage ohne Abschnitt erhält das ganze Objekt. Alles andere wird mit `null` beantwortet.
- Ein Manifest mit fehlerhaftem Eintrag wird als Ganzes abgelehnt, mit Begründung.

## [edit.complete] Vervollständigung von Wörtern und Clips
commands: nks.edit.complete
1. Drücken Sie Strg+J (Bearbeiten, Einfügen, Wort oder Clip vervollständigen). Die Liste bietet die Wörter an, die das Wort vor dem Cursor fortsetzen, danach die Clips, die zur Sprache des Dokuments passen.
2. Wählen Sie einen Eintrag. Ein Wort ersetzt das Wort vor jedem Cursor; ein Clip wird wie im Thema Clips eingefügt und ersetzt dieses Wort. Es ist ein Schritt für Rückgängig.
- Die Wörter stammen aus allen offenen Text-Tabs. Das Wort, das gerade getippt wird, wird nicht angeboten, und ein Wort wird nur angeboten, wenn es länger ist als das Getippte. Zuerst kommen Wörter des bearbeiteten Dokuments, dann die häufigeren.
- Clips stammen aus der Bibliothek des Themas Clips. Ein Clip ohne Sprache passt zu jedem Dokument; ein Clip mit Sprache nur zu Dokumenten dieser Sprache.
- Die Werte einer Geheimnisdatei (`.env`) werden nie angeboten, solange sie verborgen sind. Sie werden nur in der Datei selbst und nur angezeigt angeboten.
- Die Liste hält eine begrenzte Zahl von Wörtern; Sie engen sie durch Tippen im Feld ein. Einträge eines laufenden Sprachservers kommen dazu, siehe das Thema Sprachserver.

## [edit.sql] SQL: Dialekt und Formatierung
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Bearbeiten, Formatierung, SQL formatieren ordnet die Auswahlen an, oder die ganze Datei, wenn nichts markiert ist: eine Klausel pro Zeile, ein Select-Element pro Zeile, Unterabfragen eingerückt, Schlüsselwörter in einheitlicher Schreibung. Es ist ein Rückgängig-Schritt, und außerhalb der Auswahl ändert sich nichts.
2. Bearbeiten, Formatierung, Vorschau der SQL-Formatierung zeigt dasselbe Ergebnis zuerst in der Änderungsvorschau.
3. Der Dialekt bestimmt die Farben und welche Wörter Schlüsselwörter sind. Es gilt das erste gesetzte von diesen: Extras, Struktur und Sprache, SQL-Dialekt (nur diese Datei); `sql_dialect = mysql` in einer `.editorconfig` (jede SQL-Datei in diesem Ordner und darunter); die Einstellung Standard-SQL-Dialekt. Ohne sie ist der Dialekt Generic, und nichts wird als Fehler gemeldet.
- Dialekte: generic, mysql, postgres, sqlite, tsql. Der Editor verbindet sich nie mit einer Datenbank.
- Zeichenfolgen, in Anführungszeichen gesetzte Namen und Kommentare werden unverändert übernommen. Erneutes Formatieren des Ergebnisses ändert nichts.
- Eine Auswahl über dem Limit des Plugins wird mit einer Meldung abgelehnt.
- Einrückung (4 Leerzeichen) und Schreibung der Schlüsselwörter (Großbuchstaben) sind vorerst fest; es sind noch keine Einstellungen.

## [edit.html] HTML: Entitäten und HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlEncodeAll, nks.edit.htmlDecode, nks.edit.htmlTidy, nks.edit.htmlCloseTag, nks.edit.htmlMatchTag, nks.edit.htmlSelectElement, nks.edit.htmlRemoveTag, nks.edit.htmlWrapTag, nks.edit.htmlStripTags
1. Bearbeiten, Formatierung, In HTML-Entitäten umwandeln ersetzt in den Auswahlen (in der ganzen Datei, wenn nichts markiert ist) die Zeichen & < > " ' durch Entitäten. Aus HTML-Entitäten umwandeln liest benannte, dezimale und hexadezimale Entitäten zurück. Ein kodierter Text, zurückgewandelt, ergibt den ursprünglichen Text. Jedes ist ein Rückgängig-Schritt.
2. Bearbeiten, Formatierung, Mit HTML Tidy formatieren schickt das HTML an das Programm HTML Tidy und zeigt das Ergebnis in der Änderungsvorschau. Die Meldungen von Tidy gehen ins Ausgabefenster.
3. Tidy ist ein eigenes Programm. Der Editor sucht es am Pfad der Einstellung HTML-Tidy-Programm, im Ordner `tidy` neben dem Formatierer-Plugin, in `tools/tidy` des Programmordners und in den Ordnern von PATH. Wird es nicht gefunden, nennt die Meldung alle Orte.
- Tidy gehört nicht zu diesem Build: Installieren Sie es und setzen Sie den Pfad, oder legen Sie es an einen dieser Orte.
- Zeilennummern in den Meldungen zählen ab dem Anfang des Textes, der an Tidy geschickt wurde.
- In HTML-Entitäten umwandeln, auch Nicht-ASCII schreibt zusätzlich jedes Zeichen außerhalb von ASCII als numerische Entität (`&#233;`); die Rückwandlung ergibt wieder den Text.
4. Tag-Werkzeuge (HTML-, XML- und PHP-Dateien): Bearbeiten, Formatierung, Offenes Tag schließen schreibt das End-Tag des nächsten Tags, das am Cursor noch offen ist. Zum Gegen-Tag springen setzt den Cursor auf den Partner des Tags am Cursor. Element auswählen wählt das Element um die Auswahl; wiederholt wählt es das nächste äußere. Tag entfernen löscht Start- und End-Tag des Elements am Cursor und behält den Inhalt. In Tag einschließen fragt nach einem Namen und umschließt jede Auswahl damit. Alle Tags entfernen löscht aus den Auswahlen alle Tags und Kommentare (aus der ganzen Datei, wenn nichts markiert ist).
- Tags werden nach Verschachtelung gepaart. Kommentare und der Inhalt von `script` und `style` enthalten keine Tags. Die optionalen End-Tags von HTML (`li`, `p`) werden nicht ergänzt; ein solches Tag gilt also als offen.
- Das Einfügen eines Bildes oder Links mit Größe gibt es noch nicht.

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
1. Strg+K und dann Strg+T (Menü Ansicht, Tabelle, Tabellenansicht umschalten) wechselt zwischen Tabelle und Text. Eine .tsv- oder .tab-Datei wird an Tabulatoren geteilt, jede andere an Kommas.
2. Ansicht, Tabelle, Tabellentrennzeichen und Kopfzeile wählt Komma, Semikolon, Tabulator oder senkrechten Strich als Trennzeichen, ob Anführungszeichen Werte umschließen und ob die erste Zeile die Kopfzeile ist. Solange Sie nichts wählen, wird das Trennzeichen aus den ersten Zeilen der Datei erkannt (bei unklarem Ergebnis entscheidet die Endung).
- Eine große Datei öffnet sich sofort: Die Zeilen werden im Hintergrund gezählt, und der Bildlaufbereich wächst, während die Datei gelesen wird. Bis das Ende erreicht ist, zeigt die Statusleiste die Zeilenzahl mit einem Plus, etwa 12+.
- Pfeiltasten, Tab, Bild auf und Bild ab, Pos1 und Ende (innerhalb der Zeile), Strg+Pos1 und Strg+Ende bewegen die aktuelle Zelle; Umschalt erweitert die Auswahl. Ein Klick auf eine Spaltenüberschrift wählt die Spalte, ein Klick auf eine Zeilennummer die Zeile.
- Eine Zelle in Anführungszeichen kann Zeilenumbrüche enthalten; die Tabelle zeigt ihre erste Zeile und das Zeichen ↵.
- Kopieren legt die ausgewählten Zellen als durch Tabulatoren getrennten Text in die Zwischenablage; Einfügen schreibt durch Tabulatoren getrennten Text ab der aktuellen Zelle.

## [table.edit] Tabellenansicht: Bearbeiten und Filtern
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Eingabe oder F2 (oder einfach Tippen) bearbeitet die aktuelle Zelle. Eingabe speichert und geht nach unten, Umschalt+Eingabe fügt einen Zeilenumbruch ein, Esc bricht ab. Eine Zelle, die in Anführungszeichen stand, bleibt darin; ein anderer Wert wird nur dann in Anführungszeichen gesetzt, wenn er das Trennzeichen, ein Anführungszeichen oder einen Zeilenumbruch enthält.
2. Ansicht, Tabelle, Zeile einfügen fügt hinter der aktuellen eine leere Zeile ein. Spalte einfügen fragt nach dem Namen der Spalte und fügt sie hinter der aktuellen ein. Zeile löschen und Spalte löschen entfernen die ausgewählten Zeilen oder Spalten. Jede Aktion ist ein Rückgängig-Schritt.
3. Ansicht, Tabelle, Zeilen filtern zeigt nur die Zeilen, deren aktuelle Spalte den Text enthält (Groß- und Kleinschreibung spielt keine Rolle). Filter entfernen zeigt wieder alle Zeilen. Ein Filter ändert die Datei nie, und die Zeilennummern bleiben die der Datei.
- Entf leert die ausgewählten Zellen. Eine kurze Zeile erhält die fehlenden Trennzeichen, wenn eine Zelle hinter ihrem Ende gefüllt wird.
- Ist das Anführungszeichen in Tabellentrennzeichen und Kopfzeile ausgeschaltet, wird ein Wert mit Trennzeichen oder Zeilenumbruch abgelehnt.
- Solange ein Filter aktiv ist, können keine Zeilen hinzugefügt werden.

## [table.copyAs] Zellen als Markdown, SQL, JSON oder HTML kopieren
commands: nks.table.copyAs
Markieren Sie Zellen in der Tabellenansicht und wählen Sie Ansicht, Tabelle, Tabelle kopieren als und dann eine Form. Die Zwischenablage enthält die markierten Spalten und Zeilen in dieser Form.
- Markdown-Tabelle: eine Tabelle mit senkrechten Strichen, mit Kopfzeile (ohne Kopfzeile mit den Spaltenbuchstaben A, B, ...). Sonderzeichen wie der senkrechte Strich, der Backslash, Sternchen, Unterstriche und spitze Klammern erhalten einen Backslash, und ein Zeilenumbruch in einer Zelle wird zu `<br>`.
- SQL INSERT: eine Anweisung pro Zeile, die Werte als Text in einfachen Anführungszeichen (ein Anführungszeichen wird verdoppelt). Die Spaltennamen stammen aus der Kopfzeile. Das Programm fragt nach dem Tabellennamen und schlägt den Dateinamen vor.
- Die Quotierung richtet sich nach dem SQL-Dialekt: `sql_dialect` in einer `.editorconfig` des Ordners, sonst die Einstellung Standard-SQL-Dialekt (Thema SQL: Dialekt und Formatierung). MySQL setzt Namen in Backticks und verdoppelt einen Backslash im Wert; T-SQL setzt Namen in eckige Klammern und schreibt Werte als N'...'; die übrigen Dialekte setzen Namen in doppelte Anführungszeichen und lassen einen Backslash, wie er ist.
- JSON: ein Array von Objekten, benannt nach der Kopfzeile, oder ein Array von Arrays, wenn die Kopfzeile fehlt, einen leeren Namen hat oder einen Namen wiederholt. Alle Werte sind Text.
- HTML-Tabelle: ein table-Element mit Entitäten für `& < > " '`; die Zwischenablage enthält das HTML als Quelltext und als HTML zum Einfügen in ein Dokument.
- Ein verborgener Wert einer .env-Datei wird als Sterne kopiert, nicht als sein Text. Kopieren und Ausschneiden mit Strg+C und Strg+X setzen ebenfalls den Platzhalter ein, und ein Ausschneiden, das einen berührt, löscht nichts.
- Ändert sich die Datei während des Befehls, wird nichts kopiert, und ein Hinweis fordert zum erneuten Ausführen auf.
- Eine eigene Form ist ein Konverter eines Datenplugins mit "table": true (Thema Text umwandeln). Sie erscheint unter den eingebauten Formen. Sie erhält die markierten Zellen so, wie Kopieren sie schreibt: durch Tabulatoren getrennt, eine Tabellenzeile pro Textzeile, die Kopfzeile, wenn sie markiert ist, und ein Wert mit Tabulator, Anführungszeichen oder Zeilenumbruch in doppelten Anführungszeichen. Das Ergebnis kommt in die Zwischenablage; schlägt die Umwandlung fehl, bleibt die Zwischenablage unverändert.

## [table.region] Ausgewählten Text als Tabelle bearbeiten
commands: nks.table.editRegion
Wählen Sie einen Teil eines Textes aus, etwa einen Block kommagetrennter Zeilen in einer Notiz oder einem Log, und wählen Sie Ansicht, Tabelle, Auswahl als Tabelle bearbeiten.
1. Der Dialog fragt nach dem Trennzeichen (Komma, Semikolon, Tabulator oder senkrechter Strich), ob Anführungszeichen Werte umschließen und ob die erste ausgewählte Zeile die Kopfzeile ist. Die Vorschau zeigt die ersten Zeilen als Zellen.
2. Der ausgewählte Text wird zur Tabelle. Der Rest der Datei bleibt Text: Das Bearbeiten einer Zelle ändert nur die Bytes dieser Zelle, und außerhalb der Auswahl wird nichts gelesen oder geändert.
- Alles aus der Tabellenansicht funktioniert darin: Bewegen, Bearbeiten, Zeilen und Spalten hinzufügen und löschen, Filtern, Kopieren und Einfügen. Die Statusleiste sagt, dass nur der ausgewählte Text die Tabelle ist.
- Am Ende hinzugefügte Zeilen gehören zur Auswahl. Tabellenansicht umschalten kehrt zum Text zurück; das nächste Umschalten zeigt die ganze Datei als Tabelle.

## [compare.text] Texte vergleichen
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts, nks.compare.ignoreWhitespace, nks.compare.ignoreCase, nks.compare.action1, nks.compare.action2, nks.compare.action3, nks.compare.action4
Zwei Texte werden zeilenweise in einem eigenen Tab verglichen. Gleiche Zeilen stehen auf gleicher Höhe, ein geänderter Block ist auf beiden Seiten getönt und im mittleren Streifen markiert, und die Leiste oben nennt die Zahl der Unterschiede.
1. Mit Zwischenablage vergleichen (Menü Extras oder Kontextmenü des Editors) vergleicht den Tab mit dem Text der Zwischenablage. Ist im Editor Text ausgewählt, wird nur die Auswahl verglichen.
2. Mit Datei vergleichen fragt nach einer Datei und vergleicht den Tab damit. Mit Datenträger vergleichen steht im Menü Datei und vergleicht den Tab mit seiner eigenen Datei.
3. Zwei Texte vergleichen öffnet zwei leere Seiten. Die Schaltflächen Linken Text einfügen und Rechten Text einfügen setzen die Zwischenablage in eine Seite ein.
- Von der Befehlszeile öffnet textida --diff links rechts den Vergleich zweier Dateien: Die linke Datei öffnet sich in einem Tab und kann Blöcke der rechten aufnehmen, die schreibgeschützt ist.
- Ein Plugin kann einen Vergleich beliebiger zwei Quellen öffnen (eine Revision eines Dokuments, eine Datei, ein virtuelles Dokument oder Text, den es selbst liefert) und ihm Schaltflächen hinzufügen (Git fügt Abschnitt vormerken und Abschnitt aus dem Index nehmen hinzu). Eine Schaltfläche wirkt auf den Unterschied, auf dem der Tab steht. Per Tastatur drücken Sie die Ziffer 1 bis 9, solange der Vergleich den Fokus hat (der Tooltipp einer Schaltfläche nennt ihre Taste), oder nutzen die Befehle Vergleich: erste bis vierte Plugin-Aktion im Menü Extras. Die Schaltflächen wirken, solange sich die Dokumente und Dateien des Vergleichs seit seiner Erstellung nicht geändert haben; sonst öffnen Sie den Vergleich neu. Erneuert das Plugin den Vergleich, übernimmt der neue Tab den Platz des alten und bleibt beim selben Unterschied. Eine Seite, die größer ist als der Editor Plugins erlaubt, wird abgelehnt.
- Die Tab-Seite ist das Dokument selbst: Ändert es sich, wird nach einer kurzen Pause neu verglichen. Zwischenablage, Auswahl und Datei auf dem Datenträger sind schreibgeschützte Seiten.
- Ein anderes Zeilenende (CR, LF, CRLF) macht eine Zeile nicht anders. Leerzeichen und Groß-/Kleinschreibung schon, bis Sie unter Extras, Vergleichen die Optionen Leerraum ignorieren oder Groß-/Kleinschreibung ignorieren einschalten. Dann sind Zeilen gleich, die sich nur in Leerzeichen und Tabulatoren (irgendwo in der Zeile) oder nur in der Groß-/Kleinschreibung unterscheiden. Die Wahl gilt für jeden Vergleich, offene werden sofort neu verglichen, und die Leiste oben nennt sie. Solche gleichen Zeilen werden nicht getönt, auch wenn ihre Leerzeichen abweichen, und das Kopieren nach links oder rechts übernimmt die Zeilen, wie sie sind.
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
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpShareSelection, nks.tools.mcpTools
Ein KI-Assistent, den Sie auf diesem Computer starten (zum Beispiel ein Coding-Agent), kann Textida über das Model Context Protocol nutzen. Er sieht nur die Tabs, die Sie ihm freigeben, und schreibt nie selbst: Jeden Vorschlag sehen Sie als Änderungsvorschau und Sie entscheiden.
1. Wählen Sie Extras, MCP-Server. Der Dialog listet auf, was der Assistent bekommt. Mit Einschalten startet der Server; erneutes Wählen des Befehls schaltet ihn aus und entzieht alle Tabs.
2. Richten Sie den Assistenten so ein, dass er das Programm textida-mcp.exe aus dem Ordner mcp neben dem Editor startet (die Statusleiste zeigt den vollständigen Pfad). Es spricht über eine lokale Pipe mit dem Editor, ein Netzwerkport wird nicht geöffnet.
3. Öffnen Sie einen Tab und wählen Sie Extras, MCP, Tab mit MCP teilen. Derselbe Befehl entzieht den Tab wieder. Ein Tab, der nicht geteilt ist, wird nicht einmal aufgelistet.
4. Wollen Sie dem Assistenten nur einen Teil eines Tabs geben, markieren Sie den Text und wählen Sie Extras, MCP, Auswahl mit MCP teilen. Der markierte Text öffnet sich als schreibgeschützter Auszug in einem eigenen Tab: eine Kopie, die dem Original nicht folgt, und der Assistent kann dazu keine Änderungen vorschlagen. Das Schließen dieses Tabs entzieht ihn.
5. Mit Extras, MCP, MCP-Werkzeuge schalten Sie einzelne Werkzeuge ein oder aus. Die Wahl eines Werkzeugs in der Liste ändert seinen Zustand, Escape schließt die Liste.
- Der Assistent liest den Text so, wie er im Editor steht, auch ungespeichert, sucht darin, fragt ab, wo Ihr Cursor und Ihre Auswahl stehen, liest die Fehler und Warnungen von Plugins und Linter, fragt die Gliederung, die Kodierung und die Zeilenenden ab, findet Zeilen einer Stufe in einem Log beliebiger Größe und liest oder durchsucht Bytes (höchstens 64 KiB pro Lesen).
- Eine Änderung ist ein Vorschlag für eine Revision des Textes. Hat sich der Text inzwischen geändert, wird der Vorschlag abgelehnt und der Assistent muss neu lesen. Den Vorschlag sehen Sie wie andere Änderungen des ganzen Dokuments und wählen, ob er angewendet wird; der Assistent kann nachfragen, wie er ausging.
- Werte in .env-Dateien sieht der Assistent als *. Beim Teilen eines solchen Tabs werden Sie gefragt, ob er die echten Werte sehen darf; die Antwort gilt nur für diesen Tab.
- Das Entziehen eines Tabs oder das Ausschalten des Servers stoppt die laufenden Anfragen, und ein Vorschlag, der noch auf Ihre Entscheidung wartet, wird zurückgezogen und gilt als abgelehnt. Auch das erneute Laden einer Datei entzieht sie.
- Nur eine begrenzte Zahl von Vorschlägen kann gleichzeitig auf Ihre Entscheidung warten (das Limit ist vorläufig); ein weiterer wird abgelehnt, bis Sie entschieden haben. Ein Ergebnis über dem Größenlimit wird abgelehnt, und der Assistent erfährt, dass er weniger anfordern soll.
- Das Programm für den Assistenten spricht die aktuelle MCP-Version (2026-07-28) und für Assistenten, die noch mit initialize beginnen, die Version 2025-11-25.

## [tools.ide] Die /ide-Brücke für Claude Code
commands: nks.tools.ideBridge, nks.tools.ideDisconnect
Claude Code, das in einem Terminal auf diesem Computer läuft, verbindet sich mit dem Befehl /ide mit einem Fenster von Textida. Der Agent weiß dann, wo Ihre Auswahl liegt und welche Datei offen ist, und Änderungen, die er an einem freigegebenen Tab vornehmen will, erscheinen in der Änderungsprüfung. Er sieht nur Tabs, die Sie freigeben, und Textida übernimmt nur, was Sie bestätigen.
1. Schalten Sie zuerst den MCP-Server ein (Werkzeuge, MCP-Server) und geben Sie einen Tab frei (Werkzeuge, Diesen Tab mit MCP teilen). Die Brücke nutzt dieselben freigegebenen Tabs.
2. Wählen Sie Werkzeuge, /ide-Brücke. Der Dialog nennt, was der Agent bekommt. Erst nach Ihrer Bestätigung öffnet Textida einen Port, nur auf diesem Computer (127.0.0.1), und schreibt eine Sperrdatei mit einem Token und den Ordnern des Fensters nach ~/.claude/ide (oder in den Ordner ide von CLAUDE_CONFIG_DIR). Wählen Sie den Befehl erneut, wird der Port geschlossen und die Datei gelöscht.
3. Führen Sie im Terminal, in einem Ordner dieses Fensters, in Claude Code den Befehl /ide aus. Ein Programm ohne das Token aus der Sperrdatei wird abgewiesen.
4. Die Statusleiste zeigt den verbundenen Agenten. Ein Klick darauf oder der Befehl Werkzeuge, /ide-Brücke: Agenten trennen trennt ihn. Der Port bleibt offen, der Agent kann sich also mit /ide erneut verbinden; um das zu verhindern, schalten Sie die Brücke aus.
- Jedes Fenster hat eine eigene Brücke mit eigenem Port und Token, ebenso jede Instanz von Textida. Ein Agent, der mit einem Fenster verbunden ist, sieht nur die in diesem Fenster freigegebenen Tabs. Eine Sperrdatei, die eine abgestürzte Instanz hinterlassen hat, wird beim Start der nächsten Brücke gelöscht.
- Der Agent erfährt Auswahl und Pfad eines freigegebenen Tabs bei jeder Änderung der Auswahl und beim Wechsel zu dem Tab. Ein Tab, der nicht freigegeben ist oder noch keine Datei hat, wird nicht gemeldet. Werte aus .env bleiben wie bei jedem anderen Lesen verborgen. Das Zurücknehmen des Tabs stoppt das sofort.
- Eine Änderung, die Claude Code für einen freigegebenen Tab vorschlägt, öffnet sich in der Änderungsprüfung (Tab Suchergebnisse). Wenden Sie alles an, lautet die Antwort FILE_SAVED, lehnen Sie ab, DIFF_REJECTED; wenden Sie nur einige Abschnitte an, antwortet die Brücke mit einem Fehler, damit der Agent die Datei neu liest. Textida übernimmt im Tab, was Sie bestätigen, und speichert die Datei nicht. Eine Datei, die im Fenster nicht offen oder nicht freigegeben ist, und eine .env-Datei werden abgelehnt.
- getDiagnostics liefert die Fehler und Warnungen der freigegebenen Tabs.
- Nicht verifiziert: Claude Code veröffentlicht keine Spezifikation dieses Protokolls. Die Brücke folgt der offiziellen Seite zum eingebauten IDE-Server und einer Beschreibung der Community (coder/claudecode.nvim); bisher ist keine Claude-Code-Version als funktionierend bestätigt. Nicht umgesetzt: Dateien vom Agenten öffnen, die Liste der offenen Tabs, Speichern über die Brücke, Notebook-Zellen ausführen und @-Erwähnungen.

## [tools.agent] Agent über ACP
commands: nks.agent.start, nks.agent.panel, nks.agent.shareTab, nks.agent.cancel, nks.agent.stop
1. Werkzeuge, Agent starten... startet einen Coding-Agenten, der das Agent Client Protocol (ACP) spricht: Gemini CLI, Codex, Claude, Junie, GitHub Copilot oder OpenCode. Der Dialog zeigt den genauen Befehl und den Ordner, in dem er läuft; ohne Bestätigung startet nichts. Der Agent muss installiert und im PATH zu finden sein. Eine Stapeldatei (.cmd, .bat), die übliche Form eines über npm installierten Agenten unter Windows, wird über cmd.exe gestartet, und jedes Argument wird dafür in Anführungszeichen gesetzt. Die Liste sagt bei jedem Agenten, ob sein Programm im PATH gefunden wird; bei einem fehlenden nennt eine Meldung, woher er sich installieren lässt, und nichts startet. Der Dialog zeigt den vollständigen Pfad des Programms, das läuft. Nach der Bestätigung startet Textida das Programm mit --version und vermerkt die Version im Bereich neben der Version der Tabelle; ein Programm, das keine ausgibt, wird nur vermerkt.
2. Der Agent-Bereich unter dem Editor zeigt die Unterhaltung. Nachricht tippen und Eingabe drücken. Zug des Agenten abbrechen (oder die Schaltfläche Zug abbrechen) schickt dem Agenten den Abbruch und beantwortet seine offenen Berechtigungsfragen mit „abgebrochen“; Agent beenden beendet den Prozess.
3. Der Agent liest nur die Tabs, die Sie teilen: Tab mit Agent teilen. Er bekommt den Text, wie er im Editor steht, samt ungespeicherten Änderungen; Werte von .env-Dateien bleiben verborgen. Beim Beenden des Agenten werden alle geteilten Tabs zurückgenommen.
4. Schreibt der Agent in eine geteilte Datei, erscheint die Änderung abschnittsweise in der Änderungsprüfung (Tab Suchergebnisse) und wird erst geschrieben, wenn Sie sie anwenden. Lehnen Sie sie ab, bekommt der Agent einen Fehler. Eine Datei, die nicht geteilt oder nicht geöffnet ist, wird nicht über den Editor geschrieben.
5. Bittet der Agent um Erlaubnis, zeigt ein Dialog die Möglichkeiten, die der Agent anbietet. Abbrechen oder Schließen des Dialogs antwortet „abgebrochen“. Bei einem Werkzeug, das der Agent selbst ausführt (Befehl, Löschen, Verschieben, Herunterladen), sagt der Dialog, dass der Editor es weder prüfen noch rückgängig machen kann, und er weist darauf hin, dass sich der Agent eine mit „immer“ gekennzeichnete Wahl merkt, nicht der Editor.
- Wo die Garantie endet: Der Editor steuert nur, was über ihn läuft. Die eigenen Werkzeuge des Agenten können Dateien ändern, Programme starten und das Netzwerk nutzen, ohne den Editor, und der Editor stellt bei keinem Agenten den Berechtigungsmodus ein (jeder behält seinen eigenen Standard). Was der Agent darf, stellen Sie in den Einstellungen des Agenten selbst ein. Der Bereich zeigt das unter der Überschrift: den Berechtigungsmodus, den der Editor einstellt (keinen), was die eigenen Werkzeuge des Agenten können, und laufende Zähler. „Über den Editor“ zählt, was der Editor selbst bedient hat: Lesezugriffe, Vorschläge, über die Sie entschieden haben, und die, die Sie angewendet haben. „Außerhalb des Editors“ ist nur, was der Agent über seine eigenen Werkzeuge gemeldet hat (Befehle, Löschen, Verschieben, Herunterladen); die wirkliche Zahl kann also höher sein. Solche Werkzeugzeilen sind in der Unterhaltung gekennzeichnet.
- Textida speichert für Agenten keine Schlüssel oder Passwörter, und die Tabelle der Agenten enthält nur Programmnamen und Optionen. Melden Sie sich mit dem Programm des Agenten selbst an; der Agent bekommt die Umgebung, in der Textida läuft. Die Version in der Tabelle ist die, die das öffentliche ACP-Register nennt, keine getestete.
- Jeder Agent steht als „nicht überprüft“ in der Liste: Noch kein Lauf mit dem echten Programm hat die Testszenarien bestanden. Ein Zug, in dem der Agent zu lange weder etwas sagt noch fragt, endet als Zeitüberschreitung. Die Zeitlimits und die Größe einer an den Agenten übergebenen Datei sind vorläufig.
- Ein Plugin kann im selben Bereich eine eigene Unterhaltung führen (Berechtigung agent.panel). Der Bereich lässt sich mit der Tastatur bedienen: Er öffnet sich mit dem Cursor im Nachrichtenfeld, Enter sendet, Tab geht durch Senden, Zug abbrechen, Agent stoppen und Schließen, Leertaste oder Enter drückt die Schaltfläche, und Esc schließt den Bereich und kehrt zum Editor zurück. Agent-Zug abbrechen und Agent stoppen sind auch Befehle im Menü und in der Palette. Ein Screenreader bekommt einen Namen für den Bereich, die Unterhaltung, das Nachrichtenfeld, den Status und jede Schaltfläche, und eine Änderung des Status (startet, bereit, arbeitet, beendet) wird angesagt. Geprüft am Zugänglichkeitsbaum des Programms, noch nicht mit einem echten Screenreader.

## [tools.mcpAgents] MCP-Server der Agenten
commands: nks.tools.mcpServers
Eine Liste der MCP-Server, mit denen Ihre Coding-Agenten eingerichtet sind. Textida liest Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) und OpenCode (opencode.json, opencode.jsonc). Projektdateien werden im Ordner der aktiven Datei und in den übergeordneten Ordnern gesucht. Textida liest diese Dateien nur und schreibt nie hinein.
1. Wählen Sie Extras, MCP, MCP-Server der Agenten. Jede Zeile zeigt den Server, den Agenten und die Datei, aus der er stammt, sowie Befehl oder Adresse mit verborgenen Geheimnissen.
2. Wählen Sie einen Server und dann Ausprobieren. Ein Dialog zeigt den genauen Befehl, den Ordner und die Variablen mit verborgenen Werten (***). Bei einem Server mit Adresse zeigt er stattdessen die Adresse und die Namen der Header. Nichts startet und nichts wird gesendet, bevor Sie Starten drücken (bei einer Adresse Verbinden).
3. Danach startet Textida das Programm oder verbindet sich mit der Adresse, fragt nach den Tools (MCP-Versionen 2026-07-28 und 2025-11-25 bei einem Programm, 2025-11-25 bei einer Adresse) und beendet die Verbindung. Das Ergebnis nennt die Zahl der Tools oder den Fehler und die letzten Zeilen, die das Programm auf die Fehlerausgabe geschrieben hat. Dieselben Zeilen stehen in der Ausgabe Aufgaben.
4. Mit Verbinden benutzen Sie den Server. Eine Liste zeigt seine Tools und Ressourcen. Eine Ressource öffnet sich in einem neuen, schreibgeschützten Tab. Bei einem Tool werden die Argumente als JSON-Objekt abgefragt (beim Tippen wird das Schema des Tools gezeigt), dann zeigt eine eigene Frage den Server, das Tool, die Argumente und ob der Server angibt, dass das Tool nur liest. Nichts wird gesendet, bevor Sie Aufrufen drücken; die Standardschaltfläche ist Abbrechen. Die Antwort öffnet sich in einem neuen, schreibgeschützten Tab, und die Ausgabe Aufgaben vermerkt, dass das Tool aufgerufen wurde und wie viele Zeichen die Argumente hatten, nicht die Argumente selbst.
5. Mit Eintrag für einen anderen Agenten umwandeln erscheint der Eintrag im Format dieses Agenten in einem neuen Tab. Der Ausschnitt wird zuerst gegen dieses Format geprüft. Werte von Variablen und Headern sind durch *** ersetzt; ergänzen Sie sie, bevor Sie den Ausschnitt in die Datei dieses Agenten einfügen.
- ${NAME} in einem Befehl, Argument, einer Variable, Adresse oder einem Header wird aus der Umgebung von Textida ersetzt, ${NAME:-text} liefert einen Standardwert. Ein dort nicht gesetzter Name wird in der Windows-Anmeldeinformationsverwaltung unter textida:mcp:env:NAME gesucht. Ein Name, den es auch dort nicht gibt, wird abgefragt (die Eingabe ist verborgen) und nur in der Anmeldeinformationsverwaltung abgelegt, nirgends sonst; ist der Anmeldeinformationsspeicher nicht verfügbar, gilt der Wert nur für diese Verbindung. Wer die Frage abbricht, startet nichts.
- Ein Server mit Adresse bekommt Schlüssel in seinen Headern, deshalb muss die Adresse mit https:// beginnen, sofern sie nicht auf diesem Computer liegt. Textida spricht Streamable HTTP (MCP 2025-11-25); der ältere Transport mit getrenntem Ereigniskanal (Typ sse aus MCP 2024-11-05) wird nicht unterstützt. Der Netzwerk-Proxy des Programms gilt. Ein Schlüssel, den der Server ablehnt (HTTP 401 oder 403), wird aus der Anmeldeinformationsverwaltung verworfen und einmal neu abgefragt; ein Schlüssel aus der Umgebung oder direkt in der Konfigurationsdatei wird von Textida nicht geändert, und die Meldung sagt das. npx und npm werden als node mit dem Skript des Node.js-Shims gestartet, sodass kein cmd.exe zwischen Editor und Server steht. Jede andere Batchdatei wird über cmd.exe gestartet.
- Grenzen (vorläufig): Eine Antwort oder Ressource über 262144 Zeichen wird abgeschnitten, und die Statuszeile nennt die ursprüngliche Länge. Eine Liste, ein Aufruf oder ein Lesevorgang, der länger als 120 Sekunden dauert, endet mit einer Meldung, und der Server wird aufgefordert, die Anfrage zu stoppen. Ein Programm, das endet oder gestoppt wird, wird mit Exitcode und den letzten Zeilen der Fehlerausgabe gemeldet, und die Liste schließt sich.
- Geheimnisse sind in der Liste, im Dialog, in der Ausgabe und in den Ausschnitten verborgen, auch wenn eines in einem Argument oder in der Fehlerausgabe des Programms vorkommt. Werte unter 4 Zeichen werden im Text nicht gesucht.

## [tools.shell] Explorer-Menü und Dateizuordnungen
commands: nks.tools.shellMenuAdd, nks.tools.shellAssociate, nks.tools.shellRemove
Nur Windows. Textida kann sich in das Kontextmenü des Explorers Ihres Benutzers und in die Liste Öffnen mit eintragen. Dafür sind keine Administratorrechte nötig, und es werden nur Schlüssel von Textida unter HKEY_CURRENT_USER\Software\Classes geschrieben. Schlüssel anderer Programme, auch von PSPad, werden weder gelesen noch geändert. Beide Befehle funktionieren in der installierten Textida.exe.
1. Wählen Sie Extras, Systemintegration, Textida zum Explorer-Menü hinzufügen. Dateien erhalten den Eintrag Mit Textida öffnen; er startet Textida mit der Datei oder gibt die Datei an die bereits laufende Textida weiter. Unter Windows 11 steht der Eintrag unter Weitere Optionen anzeigen.
2. Wählen Sie Extras, Systemintegration, Dateizuordnungen und dann Endungen. Die Wahl einer Endung in der Liste fügt Textida in deren Liste Öffnen mit hinzu oder entfernt es; Escape schließt die Liste. Solange Sie keine Endung wählen, wird nichts zugeordnet.
3. Damit Textida das Standardprogramm einer Endung wird, nutzen Sie im Explorer Öffnen mit, Andere App auswählen. Diese Wahl verwaltet Windows selbst.
4. Wählen Sie Extras, Systemintegration, Textida aus dem Explorer entfernen: Der Menüeintrag und alle Zuordnungen, die Textida angelegt hat, werden gelöscht. Das Deinstallationsprogramm tut dasselbe.
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

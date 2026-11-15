## [view.structure] JSON/XML-Struktur
commands: nks.view.structure
Das Panel zeigt die Knoten und Werte des aktiven JSON-, JSONC- oder XML-Dokuments. Beim Durchsehen wird die Datei nicht umgeschrieben. Das Panel meldet auch Knotenzahlen und Fehler.
1. Öffnen Sie das Dokument und wählen Sie Ansicht, Bereiche, JSON/XML-Struktur anzeigen, oder starten Sie denselben Befehl über die Befehlspalette.
2. Klappen Sie Knoten mit der Maus oder den Pfeiltasten auf und zu. Die Auswahl eines Knotens markiert seinen Bereich im ursprünglichen Text; Zum Quelltext zeigt diese Auswahl erneut.
3. Pfad kopieren kopiert den Pfad zum Knoten, Quelltext kopieren dessen ursprünglichen Text. Eine zu große Auswahl wird nicht kopiert; das Panel nennt die Grenze.
4. Wählen Sie für die Suche Name, Wert, Pfad oder JSONPath/XPath und geben Sie einen Ausdruck ein. Suchen oder Eingabe startet die Suche auch manuell. Ergebnisse haben Vorherige Seite und Nächste Seite; Baum anzeigen kehrt zum normalen Baum zurück.
5. Wert bearbeiten ändert einen einfachen JSON-Wert oder ein XML-Attribut oder einen XML-Text. Ein XML-Element lässt sich nur bearbeiten, wenn es einen einzigen Textwert ohne verschachtelte Elemente enthält. Bei schreibgeschützten Dateien ist die Bearbeitung gesperrt.
6. Abbrechen beendet die Arbeit des Panels. Aktualisieren liest den Baum des aktuellen Dokuments erneut.
- Unterstützte JSONPath-Teilmenge: Mitglieder, Indizes, Platzhalter, rekursive Suche und Gleichheitsfilter für einfache Werte.
- Unterstützte XPath-Teilmenge: Kind- und Nachkommenpfade, Attribute, Namensräume, node()/text()/comment(), Positionen und Gleichheitsbedingungen. Geben Sie Namensräume als prefix=URI ein, einen pro Zeile. Gelesen wird XML 1.0; DTD ist deaktiviert.

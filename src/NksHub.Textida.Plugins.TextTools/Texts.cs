namespace NksHub.Textida.Plugins.TextTools;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language with each command. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["stale"] = ["Dokument se mezitím změnil. Spusťte příkaz znovu.", "The document changed in the meantime. Run the command again.", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus."],
        ["stats.task"] = ["Statistika dokumentu", "Document statistics", "Štatistika dokumentu", "Dokumentstatistik"],
        ["stats.result"] = ["Bajty: {bytes}, znaky: {characters}, slova: {words}, řádky: {lines}, nejdelší řádek: {longest} znaků.", "Bytes: {bytes}, characters: {characters}, words: {words}, lines: {lines}, longest line: {longest} characters.", "Bajty: {bytes}, znaky: {characters}, slová: {words}, riadky: {lines}, najdlhší riadok: {longest} znakov.", "Bytes: {bytes}, Zeichen: {characters}, Wörter: {words}, Zeilen: {lines}, längste Zeile: {longest} Zeichen."],
        ["stats.partial"] = ["Neúplná statistika, zpracováno {done} z {total} B. ", "Incomplete statistics, {done} of {total} bytes processed. ", "Neúplná štatistika, spracované {done} z {total} B. ", "Unvollständige Statistik, {done} von {total} Bytes verarbeitet. "],
        ["hash.task"] = ["Hash {algorithm}", "Hash {algorithm}", "Hash {algorithm}", "Hash {algorithm}"],
        ["hash.pickTitle"] = ["Hash", "Hash", "Hash", "Hash"],
        ["hash.pickPlaceholder"] = ["Vyberte algoritmus", "Choose an algorithm", "Vyberte algoritmus", "Algorithmus wählen"],
        ["hash.result"] = ["{algorithm} ({bytes} B): {hash}", "{algorithm} ({bytes} bytes): {hash}", "{algorithm} ({bytes} B): {hash}", "{algorithm} ({bytes} Bytes): {hash}"],
        ["hash.cancelled"] = ["Výpočet hashe byl zrušen po {done} z {total} B, výsledek není.", "The hash was cancelled after {done} of {total} bytes; there is no result.", "Výpočet hashu bol zrušený po {done} z {total} B, výsledok nie je.", "Die Hash-Berechnung wurde nach {done} von {total} Bytes abgebrochen; es gibt kein Ergebnis."],
        ["hash.severalRanges"] = ["Označte jen jednu část, nebo nic pro celý dokument.", "Select one part only, or nothing for the whole document.", "Označte len jednu časť, alebo nič pre celý dokument.", "Markieren Sie nur einen Bereich oder nichts für das ganze Dokument."],
        ["hash.unknown"] = ["Neznámý algoritmus '{algorithm}'.", "Unknown algorithm '{algorithm}'.", "Neznámy algoritmus '{algorithm}'.", "Unbekannter Algorithmus '{algorithm}'."],
        ["calc.result"] = ["Počet: {count}, součet: {sum}, nejmenší: {min}, největší: {max}, průměr: {average}.", "Count: {count}, sum: {sum}, smallest: {min}, largest: {max}, average: {average}.", "Počet: {count}, súčet: {sum}, najmenšie: {min}, najväčšie: {max}, priemer: {average}.", "Anzahl: {count}, Summe: {sum}, kleinster: {min}, größter: {max}, Durchschnitt: {average}."],
        ["calc.ignored"] = [" Přeskočeno položek, které nejsou čísla: {ignored}.", " Skipped items that are not numbers: {ignored}.", " Preskočené položky, ktoré nie sú čísla: {ignored}.", " Übersprungene Einträge, die keine Zahlen sind: {ignored}."],
        ["calc.none"] = ["V označeném textu nejsou žádná čísla.", "There are no numbers in the selected text.", "V označenom texte nie sú žiadne čísla.", "Im markierten Text stehen keine Zahlen."],
        ["calc.noSelection"] = ["Označte čísla, která se mají sečíst.", "Select the numbers to add up.", "Označte čísla, ktoré sa majú sčítať.", "Markieren Sie die Zahlen, die addiert werden sollen."],
        ["calc.overflow"] = ["Součet je mimo rozsah čísel.", "The sum is outside the range of numbers.", "Súčet je mimo rozsahu čísel.", "Die Summe liegt außerhalb des Zahlenbereichs."],
        ["calc.tooLarge"] = ["Označený text má {size} B, limit je {max} B. Označte menší část.", "The selected text is {size} bytes; the limit is {max} bytes. Select a smaller part.", "Označený text má {size} B, limit je {max} B. Označte menšiu časť.", "Der markierte Text hat {size} Bytes, das Limit ist {max} Bytes. Markieren Sie einen kleineren Teil."],
        ["expr.title"] = ["Vyhodnocení výrazu", "Evaluate Expression", "Vyhodnotenie výrazu", "Ausdruck auswerten"],
        ["expr.label"] = ["Výraz (čísla s tečkou)", "Expression (numbers with a dot)", "Výraz (čísla s bodkou)", "Ausdruck (Zahlen mit Punkt)"],
        ["expr.result"] = ["{expression} = {result}", "{expression} = {result}", "{expression} = {result}", "{expression} = {result}"],
        ["expr.syntax"] = ["Chyba ve výrazu na pozici {position}: {detail}", "Syntax error in the expression at position {position}: {detail}", "Chyba vo výraze na pozícii {position}: {detail}", "Syntaxfehler im Ausdruck an Position {position}: {detail}"],
        ["expr.unknownName"] = ["Neznámý název '{detail}'. Povoleny jsou jen ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR a TRUNC.", "Unknown name '{detail}'. Only ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR and TRUNC are allowed.", "Neznámy názov '{detail}'. Povolené sú len ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR a TRUNC.", "Unbekannter Name '{detail}'. Erlaubt sind nur ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR und TRUNC."],
        ["expr.domain"] = ["Neplatný argument u {detail} (pozice {position}).", "Invalid argument for {detail} (position {position}).", "Neplatný argument pri {detail} (pozícia {position}).", "Ungültiges Argument bei {detail} (Position {position})."],
        ["expr.range"] = ["Číslo nebo výsledek je mimo rozsah {detail} (pozice {position}).", "A number or result is out of range {detail} (position {position}).", "Číslo alebo výsledok je mimo rozsahu {detail} (pozícia {position}).", "Eine Zahl oder das Ergebnis liegt außerhalb des Bereichs {detail} (Position {position})."],
        ["expr.divisionByZero"] = ["Dělení nulou (pozice {position}).", "Division by zero (position {position}).", "Delenie nulou (pozícia {position}).", "Division durch null (Position {position})."],
        ["base.title"] = ["Převod soustav", "Convert Number Base", "Prevod sústav", "Zahlensystem umrechnen"],
        ["base.label"] = ["Číslo (desítkově, nebo s 0x, 0b, 0o)", "Number (decimal, or with 0x, 0b, 0o)", "Číslo (desiatkovo, alebo s 0x, 0b, 0o)", "Zahl (dezimal oder mit 0x, 0b, 0o)"],
        ["base.result"] = ["Desítkově: {dec}, šestnáctkově: {hex}, osmičkově: {oct}, dvojkově: {bin}", "Decimal: {dec}, hexadecimal: {hex}, octal: {oct}, binary: {bin}", "Desiatkovo: {dec}, šestnástkovo: {hex}, osmičkovo: {oct}, dvojkovo: {bin}", "Dezimal: {dec}, hexadezimal: {hex}, oktal: {oct}, binär: {bin}"],
        ["base.invalid"] = ["'{text}' není celé číslo (desítkové, nebo s předponou 0x, 0b, 0o).", "'{text}' is not a whole number (decimal, or with the prefix 0x, 0b, 0o).", "'{text}' nie je celé číslo (desiatkové, alebo s predponou 0x, 0b, 0o).", "'{text}' ist keine ganze Zahl (dezimal oder mit dem Präfix 0x, 0b, 0o)."],
        ["table.none"] = ["Dokument není zobrazen jako tabulka.", "The document is not shown as a table.", "Dokument nie je zobrazený ako tabuľka.", "Das Dokument wird nicht als Tabelle angezeigt."],
        ["table.copied"] = ["Zkopírováno jako {format}: řádků {rows}, sloupců {columns}.", "Copied as {format}: {rows} rows, {columns} columns.", "Skopírované ako {format}: riadkov {rows}, stĺpcov {columns}.", "Kopiert als {format}: {rows} Zeilen, {columns} Spalten."],
        ["table.sqlTitle"] = ["Kopírovat jako SQL INSERT", "Copy as SQL INSERT", "Kopírovať ako SQL INSERT", "Als SQL INSERT kopieren"],
        ["table.sqlLabel"] = ["Název tabulky", "Table name", "Názov tabuľky", "Tabellenname"],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}

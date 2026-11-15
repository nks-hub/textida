## [view.structure] Strom JSON/XML
commands: nks.view.structure
Panel ukazuje uzly aktivního dokumentu JSON, JSONC nebo XML a jejich hodnoty. Samotné procházení soubor nepřepisuje. Panel uvádí také počty uzlů a nalezené chyby.
1. Otevřete dokument a zvolte Zobrazit, Panely, Zobrazit strom JSON/XML, nebo stejný příkaz spusťte z palety příkazů.
2. Rozbalujte a sbalujte uzly myší nebo šipkami. Výběr uzlu označí jeho rozsah v původním textu; tlačítko Přejít do zdroje výběr znovu ukáže.
3. Kopírovat cestu zkopíruje cestu k uzlu, Kopírovat zdroj jeho původní text. Příliš velký výběr se nekopíruje a panel oznámí limit.
4. Pro hledání zvolte Název, Hodnota, Cesta nebo JSONPath/XPath a napište výraz. Hledat nebo Enter hledání spustí také ručně. Výsledky mají Předchozí stránku a Další stránku; Zobrazit strom vrátí běžný strom.
5. Upravit hodnotu mění jednoduchou hodnotu JSON nebo atribut a text XML. Element XML lze upravit jen tehdy, má-li jedinou textovou hodnotu bez vnořených elementů. V souboru jen pro čtení je úprava zakázaná.
6. Zrušit zastaví práci panelu. Obnovit znovu načte strom aktuálního dokumentu.
- Podporovaná podmnožina JSONPath: členy, indexy, zástupný znak, hledání v potomcích a filtry rovnosti jednoduchých hodnot.
- Podporovaná podmnožina XPath: cesty k dětem a potomkům, atributy, jmenné prostory, node()/text()/comment(), pozice a podmínky rovnosti. Jmenné prostory zadejte jako prefix=URI, jeden na řádek. Čte se XML 1.0; DTD je zakázáno.

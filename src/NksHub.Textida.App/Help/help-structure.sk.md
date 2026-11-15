## [view.structure] Strom JSON/XML
commands: nks.view.structure
Panel ukazuje uzly aktívneho dokumentu JSON, JSONC alebo XML a ich hodnoty. Samotné prezeranie súbor neprepisuje. Panel uvádza aj počty uzlov a nájdené chyby.
1. Otvorte dokument a zvoľte Zobraziť, Panely, Zobraziť strom JSON/XML, alebo rovnaký príkaz spustite z palety príkazov.
2. Rozbaľujte a zbaľujte uzly myšou alebo šípkami. Výber uzla označí jeho rozsah v pôvodnom texte; tlačidlo Prejsť do zdroja výber znovu ukáže.
3. Kopírovať cestu skopíruje cestu k uzlu, Kopírovať zdroj jeho pôvodný text. Príliš veľký výber sa nekopíruje a panel oznámi limit.
4. Na hľadanie zvoľte Názov, Hodnota, Cesta alebo JSONPath/XPath a napíšte výraz. Hľadať alebo Enter hľadanie spustí aj ručne. Výsledky majú Predchádzajúcu stranu a Ďalšiu stranu; Zobraziť strom vráti bežný strom.
5. Upraviť hodnotu mení jednoduchú hodnotu JSON alebo atribút a text XML. Element XML možno upraviť len vtedy, ak má jedinú textovú hodnotu bez vnorených elementov. V súbore iba na čítanie je úprava zakázaná.
6. Zrušiť zastaví prácu panela. Obnoviť znovu načíta strom aktuálneho dokumentu.
- Podporovaná podmnožina JSONPath: členy, indexy, zástupný znak, hľadanie v potomkoch a filtre rovnosti jednoduchých hodnôt.
- Podporovaná podmnožina XPath: cesty k deťom a potomkom, atribúty, menné priestory, node()/text()/comment(), pozície a podmienky rovnosti. Menné priestory zadajte ako prefix=URI, jeden na riadok. Číta sa XML 1.0; DTD je zakázané.

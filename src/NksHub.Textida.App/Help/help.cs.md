## [start] Začínáme
commands: nks.help.contents
Tato nápověda leží přímo v programu a nepotřebuje internet. Otevřete ji klávesou F1 nebo z nabídky Nápověda. Když stisknete F1 u vybraného příkazu v paletě příkazů nebo v otevřeném panelu (hledání, soubory, dokumenty, nastavení, zkouška regulárního výrazu), otevře se rovnou jeho téma.
### Jak v nápovědě hledat
- Vlevo je seznam témat. Do pole nad ním napište slovo; zůstanou jen témata, která obsahují všechna napsaná slova. Háčky a velikost písmen nehrají roli.
- Šipky nahoru a dolů v poli přepínají témata, Esc nápovědu zavře.
### Zkratky
- Ve všech postupech je Ctrl; na macOS se místo něj používá ⌘ (Cmd). Uvedené zkratky jsou výchozí. Vlastní zkratky najdete v nastavení a v profilu chování PSPad se několik liší (téma Profil chování).
- Každý příkaz jde najít v paletě příkazů: Ctrl+Shift+P a napsat část názvu.
### Co nápověda pokrývá
- Popisuje jen funkce, které tato verze opravdu má. Čeho tato verze nemá, se týká téma Co tato verze neumí.
- Zvláštní témata: Obnova po pádu, Velké soubory (stránkovaný režim), Chybová hlášení.

## [view.palette] Paleta příkazů
commands: nks.view.commandPalette
Paleta je seznam všech příkazů programu s hledáním.
1. Stiskněte Ctrl+Shift+P.
2. Napište část názvu příkazu, například uloz. Háčky se nemusí psát.
3. Šipkami vyberte řádek a stiskněte Enter. Esc paletu zavře.
- U příkazu se zobrazí zkratka. Příkaz, který teď nejde použít (například Uložit bez otevřeného dokumentu), je ztlumený a má u sebe důvod.
- Napíšete-li na začátek /, paleta hledá v nastavení a Enter otevře nastavení na nalezené položce.
- Napíšete-li na začátek =, paleta počítá: =1+2*3 ukáže 7 a Enter výsledek zkopíruje. Neplatný výraz, dělení nulou nebo příliš velké číslo paleta ohlásí vedle výrazu.
- F1 na vybraném příkazu otevře jeho téma nápovědy.

## [help.contents] Nápověda
commands: nks.help.contents
Otevře toto okno nápovědy.
1. Stiskněte F1, nebo zvolte Nápověda a Nápověda.
2. Okno se otevře na tématu podle toho, kde se právě nacházíte: u vybraného příkazu v paletě na jeho tématu, v hledání, v panelu souborů nebo v seznamu dokumentů na tématu toho panelu, jinak na úvodu.
3. F1 v nastavení otevře téma o nastavení, F1 ve zkoušce regulárního výrazu téma o zkoušce.
- Nápověda je jedno okno. Další F1 ho jen přepne na jiné téma.

## [help.about] O aplikaci
commands: nks.help.about
Ukáže název programu a číslo verze.
1. Zvolte Nápověda a O aplikaci.
2. Okno zavřete tlačítkem Zavřít nebo klávesou Esc.
- Stejné číslo verze je uvedeno nahoře v každém tématu nápovědy.

## [file.new] Nový dokument
commands: nks.file.new
1. Stiskněte Ctrl+N, nebo zvolte Soubor a Nový.
2. Otevře se karta Bez názvu 1 (další dostanou čísla 2, 3 a tak dále) a kurzor je v ní.
- Kódování nového dokumentu určuje nastavení Výchozí kódování, konce řádků nastavení Konce řádků.
- Text se hned zapisuje do žurnálu obnovy, takže se neztratí ani při pádu (téma Obnova po pádu). Název a místo dostane dokument při prvním uložení.

## [file.open] Otevření souboru
commands: nks.file.open
1. Stiskněte Ctrl+O, nebo zvolte Soubor a Otevřít.
2. V systémovém okně vyberte jeden nebo více souborů a potvrďte.
3. Každý soubor se otevře na vlastní kartě. Soubor, který už je otevřený, se jen zobrazí.
- Soubory jde také přetáhnout do okna programu.
- Kódování se pozná samo. Je-li to jen odhad, ukáže to stavový řádek s procentem jistoty; kliknutím na kódování ho změníte (téma Kódování).
- Soubor větší než hranice velkého souboru (výchozí 6 MiB) se otevře ve stránkovaném režimu s omezenou editací (téma Velké soubory).
- Soubor, který nejde přečíst, se neotevře a ve stavovém řádku je důvod (téma Chybová hlášení).

## [file.openFolder] Otevření složky
commands: nks.file.openFolder
1. Stiskněte Ctrl+K a potom Ctrl+O, nebo zvolte Soubor a Otevřít složku.
2. V systémovém okně vyberte složku.
3. Vlevo se ukáže panel Soubory se stromem složky. Hledání souborů podle názvu (Ctrl+P) a Hledat v souborech pak pracují nad touto složkou.
- Pokud se složka nedá otevřít, stavový řádek napíše důvod.

## [file.save] Uložení
commands: nks.file.save, nks.file.saveAll
1. Ctrl+S uloží aktivní kartu, Ctrl+Alt+S uloží všechny karty s neuloženými změnami.
2. Dokument bez souboru se při uložení zeptá na název a kódování stejně jako Uložit jako.
3. Po uložení se ve stavovém řádku objeví Uloženo.
- Před přepsáním souboru program pořídí zálohu původní verze. Když se záloha nepovede, soubor zůstane beze změny a stavový řádek to napíše. Když se nepovede jen záloha po zápisu, napíše se Uloženo, ale záloha se nepovedla.
- Pokud se soubor na disku mezitím změnil nebo zmizel, program se zeptá: Přepsat soubor, Porovnat, nebo Zrušit. Nic se nepřepíše bez vašeho souhlasu (téma Změna souboru zvenku).
- Obsahuje-li text znaky, které kódování souboru neumí, program je vypíše a nabídne Nahradit a uložit (znaky se nahradí otazníkem) nebo Zrušit.
- Nastavení Odstranit mezery na koncích řádků, Vložit konec řádku na konec souboru a Automatické ukládání se týkají ukládání.

## [file.saveAs] Uložit jako
commands: nks.file.saveAs
1. Stiskněte Ctrl+Shift+S, nebo zvolte Soubor a Uložit jako.
2. Do pole Soubor napište cestu nebo ji vyberte tlačítkem Procházet.
3. V poli Kódování vyberte kódování. U kódování, které by některé znaky ztratilo, je napsáno ztrátové a počet znaků.
4. Potvrďte tlačítkem Uložit.
- Existuje-li cílový soubor, program se zeptá na přepsání.
- Karta pak patří novému souboru.
- Velký soubor ve stránkovaném režimu jde uložit jen v jeho vlastním kódování.

## [file.close] Zavření karty
commands: nks.file.close
1. Stiskněte Ctrl+W, nebo zvolte Soubor a Zavřít.
2. Má-li dokument neuložené změny, program se zeptá: Uložit, Neukládat, Nechat v žurnálu nebo Zrušit. Nechat v žurnálu se nabízí, jen když obnova po pádu funguje; změny pak čekají v žurnálu a při dalším spuštění se vrátí.
- Jsou-li otevřené dva pohledy téhož dokumentu (rozdělený pohled), zavření jednoho se na nic neptá.
- V profilu chování PSPad je Ctrl+W přepínání zalamování řádků; kartu pak zavřete z nabídky nebo křížkem na kartě.

## [file.reload] Znovu načíst z disku
commands: nks.file.reload
1. Zvolte Soubor a Znovu načíst z disku, nebo příkaz najděte v paletě.
2. Má-li karta neuložené změny, program se zeptá; tlačítko Zahodit změny a načíst je zahodí.
3. Ve stavovém řádku se objeví Znovu načteno z disku.
- Příkaz funguje jen u karty, která má soubor na disku.
- Totéž nabízí lišta nad dokumentem, když se soubor změní zvenku (téma Změna souboru zvenku).

## [file.compare] Porovnat s diskem
commands: nks.file.compareWithDisk
Karta a soubor na disku se ukážou vedle sebe v samostatné kartě. Vlevo je karta i s neuloženými změnami, vpravo soubor na disku.
1. Zvolte Soubor a potom Porovnat s diskem. Otevře se karta s porovnáním (téma Porovnání textů).
2. Je-li v nastavení vyplněný Externí porovnávač, spustí se místo toho váš program: vlevo soubor na disku, vpravo obsah karty. V šabloně argumentů je {left} soubor na disku a {right} obsah karty. Kopie karty předaná programu se po jeho skončení smaže.
- Karta bez souboru na disku řekne: Karta nemá soubor na disku, není s čím porovnávat.
- Když se váš program nespustí, stavový řádek napíše Porovnávač se nespustil a uvede důvod.
## [file.openPath] Otevřít cestu pod kurzorem
commands: nks.file.openPathAtCaret
1. Dejte kurzor na cestu k souboru v textu, například src/a.cs:12.
2. Zvolte Soubor a Otevřít cestu pod kurzorem, nebo příkaz najděte v paletě. Zkratku nemá.
3. Soubor se otevře; za dvojtečkou uvedený řádek (a případně sloupec) se použije.
- Relativní cesta se hledá ve složce aktuálního souboru a v otevřené složce. Cesta začínající ~/ míří do vaší domovské složky.
- Míří-li cesta na složku, ukáže se v panelu Soubory.
- Pod kurzorem není cesta: Pod kurzorem není cesta k souboru. Soubor neexistuje: napíše se cesta a že neexistuje.

## [app.settings] Nastavení
commands: nks.app.openSettings
1. Stiskněte Ctrl+čárka, nebo zvolte Nastavení a Nastavení.
2. Do pole nahoře napište, co hledáte, například tab nebo zkratka. Hledá se v nastaveních i v klávesových zkratkách.
3. U každé položky je vidět platná hodnota a odkud pochází (výchozí, uživatel, pracovní prostor, EditorConfig, dokument).
4. Hodnotu změňte přímo v řádku; projeví se hned a uloží se do souboru nastavení. Tlačítko Obnovit výchozí vrátí původní hodnotu.
- Nesmyslná hodnota se odmítne a řádek napíše, co s ní je. Nejde-li soubor nastavení zapsat, nad seznamem je důvod.
- Konflikty zkratek jsou vidět u příslušných řádků.
- F1 v tomto okně otevře toto téma.

## [app.quit] Konec programu
commands: nks.app.quit
1. Stiskněte Ctrl+Q, nebo zvolte Soubor a Konec.
2. Funguje-li obnova po pádu, program se na nic neptá: neuložený text zůstane v žurnálu a po dalším spuštění se vrátí.
3. Nefunguje-li (žurnál nejde zapisovat), program se u každého dokumentu s neuloženými změnami zeptá: Uložit, Neukládat, Zrušit.
- Rozložení karet se při konci uloží a při dalším spuštění vrátí.

## [edit.undo] Zpět a Znovu
commands: nks.edit.undo, nks.edit.redo
1. Ctrl+Z vrátí poslední krok, Ctrl+Y nebo Ctrl+Shift+Z ho vrátí zpět. Nabídka Úpravy má totéž.
2. Krok je celý souvislý kus psaní nebo jeden příkaz (například Smazat řádek nebo převod kódování).
3. Po kroku se kurzor vrátí na místo, kde změna proběhla.
- Dva pohledy téhož dokumentu mají společnou historii.
- Ve stránkovaném režimu Zpět a Znovu fungují také.
- V profilu PSPad jsou i Alt+Backspace (Zpět) a Shift+Alt+Backspace (Znovu).

## [edit.clipboard] Vyjmout, Kopírovat, Vložit
commands: nks.edit.cut, nks.edit.copy, nks.edit.paste
1. Vyberte text, Ctrl+X ho vyjme, Ctrl+C zkopíruje, Ctrl+V vloží na místo kurzoru nebo přes výběr.
- Vložený text dostane konce řádků podle dokumentu.
- S více kurzory se vybrané texty zkopírují po jednom na řádek a při vložení dostane každý kurzor svůj kus.
- V blokovém výběru se vkládá po jednom řádku schránky na každý řádek bloku.
- Vložení do řetězce v programovacím jazyce escapuje uvozovky a zpětná lomítka (nastavení Escapovat při vložení do řetězce).
- Vyjmout a Vložit nejdou u dokumentu jen pro čtení a ve stránkovaném režimu (téma Velké soubory).

## [edit.select] Výběr
commands: nks.edit.selectAll, nks.edit.selectLine, nks.edit.expandSelection
1. Ctrl+A vybere celý text.
2. Ctrl+L vybere řádek včetně konce řádku. Opakované Ctrl+L přidá další řádek.
3. Shift+Alt+šipka vpravo rozšíří výběr: nejdřív slovo, pak obsah závorek kolem něj, pak i závorky, další závorky, celé řádky a nakonec celý text.
- S více kurzory vybere každý kurzor svůj řádek.

## [edit.lines] Práce s řádky
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines, nks.edit.toggleComment
Příkazy se týkají řádku s kurzorem, případně všech řádků výběru. Každý je jeden krok Zpět.
- Smazat řádek: Ctrl+Shift+K.
- Duplikovat řádek: Ctrl+Shift+D vloží kopii pod řádek.
- Přesunout řádek nahoru nebo dolů: Alt+šipka nahoru, Alt+šipka dolů.
- Spojit řádky: Ctrl+Shift+J spojí řádek s následujícím.
- Zakomentovat / odkomentovat: Ctrl+/ řádky zakomentuje, nebo komentář zase odebere. Použije značky jazyka na daném řádku, takže skript uvnitř HTML dostane // a HTML kolem něj komentář HTML.
- Všechny jsou také v nabídce Úpravy.
- Nejdou u dokumentu jen pro čtení ani ve stránkovaném režimu.
- V profilu PSPad je Smazat řádek na Ctrl+Y, Duplikovat řádek na Shift+Alt+šipka dolů, a Alt+šipky řádky nepřesouvají.

## [edit.indent] Odsazení
commands: nks.edit.indent, nks.edit.outdent, nks.edit.insertIndent, nks.edit.tab
1. Ctrl+] zvětší odsazení vybraných řádků (nebo řádku s kurzorem), Ctrl+[ ho zmenší.
2. Klávesa Tab při výběru přes více řádků odsadí všechny, bez takového výběru vloží odsazení na místo kurzoru. Shift+Tab odsazení zmenší.
- Odsazení je tolik mezer nebo tabulátor, kolik říkají nastavení Velikost odsazení a Odsazovat mezerami. U otevřeného souboru program odsazení odhadne (nastavení Zjistit odsazení) a soubor s .editorconfig se řídí jím. Odsazení lze změnit klepnutím na něj ve stavovém řádku.
- Příkaz Vložit odsazení zkratku nemá; je to to, co dělá Tab bez výběru přes více řádků.
- V profilu PSPad jsou Zvětšit a Zmenšit odsazení na Ctrl+Shift+I a Ctrl+Shift+U.

## [edit.newline] Nový řádek
commands: nks.edit.newline
1. Enter založí nový řádek s odsazením předchozího, a ve formátu konce řádku dokumentu.
2. Stojí-li kurzor v komentáři, nový řádek komentář nasadí znovu (nastavení Pokračovat v komentáři).

## [edit.case] Velikost písmen
commands: nks.edit.upperCase, nks.edit.lowerCase
1. Vyberte text.
2. Ctrl+K a potom Ctrl+U změní písmena na velká, Ctrl+K a potom Ctrl+L na malá.
- Bez výběru jsou příkazy ztlumené. Jsou také v nabídce Úpravy.

## [edit.clipboardHistory] Historie schránky
commands: nks.edit.clipboardHistory, nks.edit.pasteKeepCaret
1. Stiskněte Ctrl+Shift+V (menu Úpravy, Historie schránky). Seznam ukáže naposledy zkopírované nebo vyjmuté texty, nejnovější první, s náhledem označené položky.
2. Psaním seznam zúžíte, šipkami vyberete text a stisknete Enter.
- Text se vloží jako při Ctrl+V: při více kurzorech do každého kurzoru (jeden řádek na kurzor, když se počet řádků rovná počtu kurzorů), jako jeden krok zpět. Funguje i ve stránkovaném režimu.
- Historie drží tolik textů, kolik říká nastavení Velikost historie schránky. Je jen v paměti a po zavření programu se zapomene. Text delší než 1 048 576 znaků se neukládá.
- Vložit bez posunu kurzoru (menu Úpravy) vloží text ze schránky a kurzor nechá tam, kde byl, před novým textem. V blokovém výběru se kurzor vrátí do levého horního rohu.
- Vložení nefunguje v dokumentu jen pro čtení; Vložit bez posunu kurzoru nefunguje ani ve stránkovaném režimu (téma Velké soubory).

## [edit.insertLines] Vložení do řádků
commands: nks.edit.insertIntoLines
Příkaz zapíše text, čísla nebo odrážky do řádků výběru, nebo do všech řádků, když je jen kurzor.
1. Zvolte Úpravy, Vložit do řádků. Náhled ukáže první řádky, jak budou vypadat.
2. Zvolte Text, Čísla nebo Odrážky a kam psát: začátek každého řádku, konec každého řádku, před sloupec, nebo do každého kurzoru.
3. Stiskněte OK. Výsledek je jeden krok zpět.
- Čísla: První číslo, Počet číslic (číslo se zleva doplní nulami na tuto šířku) a Oddělovač, který leží mezi číslem a textem řádku (na začátku řádku za číslem, na konci před číslem). Do každého kurzoru se zapíše jen číslo, takže tři kurzory dostanou 1, 2 a 3.
- Začátek každého řádku píše na začátek řádku; Přeskočit odsazení píše před první viditelný znak. Před sloupec počítá znaky od 1 a kratší řádek dostane text na konec. Přeskočit prázdné řádky přeskočí i řádky jen z mezer a ty nespotřebují číslo.
- Konce řádků dokumentu se nemění. Nefunguje v dokumentu jen pro čtení ani ve stránkovaném režimu (téma Velké soubory).
- Opakovat poslední příkaz ho spustí znovu se stejnou volbou. V profilu PSPad je to Alt+I.

## [edit.sort] Řazení řádků
commands: nks.edit.sortLines
1. Vyberte řádky (nebo dejte kurzor kamkoli, aby se seřadily všechny) a zvolte Úpravy, Seřadit řádky.
2. Zvolte vzestupně nebo sestupně, zda záleží na velikosti písmen, zda odstranit duplicitní řádky a podle čeho porovnávat. Náhled ukáže první řádky výsledku.
3. Stiskněte OK. Výsledek je jeden krok zpět a seřazené řádky zůstanou vybrané.
- Kód znaků řadí podle kódu znaků. Jazykové pravidla použijí pravidla jazyka zadaného značkou, třeba cs-CZ, nebo šestnáctkovým číslem, třeba 0405 (prázdné znamená bez jazyka). Číslo na začátku čte číslo, kterým řádek začíná; řádky bez čísla jsou první.
- Od sloupce a Do sloupce porovnají jen tu část každého řádku (0 znamená celý řádek).
- Odstranění duplicit porovnává celé řádky a nechá první z nich.
- Více výběrů se řadí každý zvlášť. Konce řádků se nemění. Nefunguje v dokumentu jen pro čtení ani ve stránkovaném režimu.

## [search.selectLines] Výběr řádků podle rozsahu
commands: nks.search.selectLinesByRange
1. Zvolte Hledat, Vybrat řádky podle rozsahu.
2. Napište čísla řádků a rozsahy, třeba 10-20, 40-50, oddělené čárkami, středníky nebo mezerami. Řádek pod polem říká, kolik výběrů vznikne.
3. Stiskněte OK. Každý rozsah se stane vlastním výběrem s vlastním kurzorem a Kopírovat pak zkopíruje řádky všech rozsahů.
- Rozsahy, které se překrývají nebo na sebe navazují, se spojí. Čísla řádků začínají od 1; řádky za koncem dokumentu se ignorují.
- Opakovat poslední příkaz vybere stejné řádky znovu.
- Nefunguje ve stránkovaném režimu.

## [format.block] Zarovnání a zalomení řádků
commands: nks.edit.reformatParagraph, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
Tyto příkazy jsou v menu Formát. Působí na vybrané řádky, nebo na všechny řádky, když je jen kurzor (Přeformátovat odstavec: na odstavec s kurzorem). Každý je jeden krok zpět.
- Přeformátovat odstavec zalomí text na sloupci z nastavení Sloupec pravého okraje, spojí řádky odstavce a odstraní nadbytečné mezery. Odstavce odděluje prázdný řádek a každý řádek odstavce dostane odsazení prvního řádku. Když není pravý okraj nastaven, řekne to stavový řádek.
- Zarovnat řádky vlevo odstraní mezery na začátku a na konci řádků.
- Zarovnat řádky vpravo a Vycentrovat řádky zarovnají na šířku nejdelšího řádku.
- Zarovnat řádky do bloku rozšíří mezery mezi slovy, až je každý řádek tak široký jako nejdelší; řádek s jedním slovem zůstane.
- V profilu PSPad je Zarovnat řádky vlevo na Ctrl+Alt+šipka vlevo a Zarovnat řádky vpravo na Ctrl+Alt+šipka vpravo.
- Nefungují v dokumentu jen pro čtení ani ve stránkovaném režimu.

## [edit.insertSmall] Datum, GUID a opakování
commands: nks.edit.insertDateTime, nks.edit.insertGuid, nks.edit.repeatLast
- Vložit datum a čas zapíše aktuální datum a čas do každého kurzoru ve formátu z nastavení Formát data a času (vlastní formát .NET, třeba yyyy-MM-dd HH:mm; názvy měsíců a dnů podle jazyka rozhraní). Výběr se nahradí. V profilu PSPad je to Alt+D.
- Vložit GUID zapíše nový GUID do každého kurzoru, každý jiný: malými písmeny, s pomlčkami, bez složených závorek.
- Opakovat poslední příkaz spustí znovu poslední příkaz, který měnil text: Smazat řádek, Duplikovat řádek, Přesunout řádek, Spojit řádky, Zvětšit odsazení, Zmenšit odsazení, změna velikosti písmen, Zakomentovat / odkomentovat, Vložit do řádků, Seřadit řádky, Přeformátovat odstavec a příkazy zarovnání, Vložit datum a čas, Vložit GUID. Příkaz s dialogem se zopakuje s volbou z posledního spuštění, bez ptaní. V profilu PSPad je to Alt+Q.
- Nefungují v dokumentu jen pro čtení ani ve stránkovaném režimu.

## [edit.json] Formátování JSON
commands: nks.edit.formatJson, nks.edit.previewFormatJson
1. Označte text JSON, nebo neoznačujte nic a naformátuje se celý dokument.
2. Nabídka Úpravy, Formátovat JSON, naformátuje označené části (každý výběr) nebo celý dokument. Všechny části jsou jeden krok Zpět; text mimo ně zůstane bajt po bajtu stejný, včetně konců řádků.
3. Náhled formátování JSON ukáže změnu nejdřív jako návrh. Použijte ho z lišty nad editorem, nebo ho zahoďte.
- Chyba syntaxe se ohlásí s řádkem a sloupcem a nic se nezmění. Text větší než limit se odmítne se zprávou.
- Pokud se dokument po spuštění příkazu změnil, příkaz se zastaví a vyzve vás ke spuštění znovu.

## [tools.stats] Statistika dokumentu a hashe
commands: nks.tools.statistics, nks.tools.hash
1. Nabídka Nástroje, Statistika dokumentu, spočítá bajty, znaky, slova, řádky a nejdelší řádek celého dokumentu.
2. Nabídka Nástroje, Spočítat hash, se zeptá na algoritmus (MD5, SHA-1, SHA-256, SHA-384 nebo SHA-512) a spočítá hash surových bajtů celého souboru nebo označené části.
- Oba příkazy čtou dokument po blocích a vy můžete dál pracovat, ukazují průběh a jdou zrušit. Žádný nedrží celý soubor v paměti, takže fungují na souborech libovolné velikosti, i ve stránkovaném a hex zobrazení.
- Zrušená statistika se ukáže jako neúplná s počtem spočítaných bajtů. Zrušený hash výsledek nemá.
- Statistika i hash platí pro dokument, jaký byl při spuštění příkazu; úpravy během výpočtu je nemění.
- Slovo je souvislá řada písmen a číslic. Hash nemá výsledek pro více označených částí najednou.

## [tools.calculate] Výpočet bloku, výrazy a číselné soustavy
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.convertBase
1. Spočítat označený blok sečte čísla v označení (blokové označení, sloupec čísel) a ukáže počet, součet, nejmenší, největší a průměr.
2. Vyhodnotit výraz se zeptá na výraz, předvyplní označený text a ukáže výsledek. Rozumí + - * / ^ ( ), MOD a funkcím ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR a TRUNC. Čísla se píší s tečkou. Cokoli jiného, třeba volání příkazu systému, se odmítne a nic se nespustí.
3. Převést číselnou soustavu se zeptá na celé číslo, desítkově nebo s předponou 0x, 0b či 0o, a ukáže ho desítkově, šestnáctkově, osmičkově a dvojkově.
- Program pozná, jak jsou čísla v označení zapsána (čeština a slovenština: 1 234,50, němčina: 1.234,50, angličtina: 1,234.50). Když vyhovuje víc zápisů, třeba 1,234, vezme ten z jazyka programu. Výsledky se ukážou ve formátu jazyka programu. Změna jazyka dokument nezmění.
- Text v označení, který není číslo, se přeskočí a spočítá. Převod čísel funguje pro hodnoty od -9223372036854775808 do 18446744073709551615.
- Mocnina bere jen kladný základ. Výpočet používá čísla s dvojitou přesností.

## [tools.colors] Barvy
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. V souborech CSS, HTML, XML, JavaScript, TypeScript, PHP, Razor a Latte má každá hodnota barvy (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) před sebou malý vzorek své barvy. Vzorek nezabírá žádný znak dokumentu.
2. Klikněte na vzorek, nebo dejte kurzor na hodnotu a zvolte Nástroje, Vybrat barvu u kurzoru. Otevře se výběr: posuvníky pro odstín, sytost, světlost a krytí a pole s hodnotou, které přijme i barvu v jiném zápisu. Kliknutí mimo výběr zapíše barvu přes hodnotu v zápisu, jaký měla, jako jeden krok zpět; Esc výběr zavře beze změny.
3. Nástroje, Převést barvu na HEX, RGB, HSL nebo oklch přepíše hodnotu barvy u kurzoru, nebo všechny v označení, do zvoleného zápisu. Barva zůstane stejná osmibitová barva; oklch se píše na pět desetinných míst u světlosti a sytosti a na dvě u odstínu.
- Barva mimo rozsah sRGB se do něj ořízne. Krytí se zachová, jako `/ alfa` nebo jako poslední dvě hexadecimální číslice.
- Text, který jen vypadá jako barva, třeba `rgb(var(--c))`, entita HTML `&#123;` nebo `#12345`, se nechá být.
- Soubor větší než limit pluginu se nepřevádí; zpráva to řekne.

## [tools.keys] Cesty klíčů v YAML a JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. V souboru YAML nebo JSON ukazuje stavový řádek cestu klíče pod kurzorem, například `cs.app.title`. Položky seznamu se píšou `items[0]` a klíč s tečkou nebo závorkou se píše `["a.b"]`. Cesta se čte pro revizi a kurzor, pro které se zeptalo: když mezitím píšete nebo kurzor přesunete, odpověď se zahodí.
2. Nástroje, Kopírovat cestu klíče dá cestu do schránky.
3. Nástroje, Přejít na klíč (nebo `@` v Přejít na cokoli) vypíše klíče souboru podle cesty; Enter označí klíč tak, jak je napsán. Zobrazit osnovu klíčů vypíše tytéž klíče v panelu pluginu a kliknutí tam označí stejné bajty.
4. Nástroje, Porovnat klíče s jiným souborem se zeptá na další otevřený soubor YAML nebo JSON a do okna výstupu vypíše klíče s hodnotou, které každému souboru chybí.
- YAML v blokovém zápisu se čte: vnořené klíče, seznamy, klíče v uvozovkách a blokové skaláry. Hodnota ve složených nebo hranatých závorkách se počítá za jednu hodnotu. JSON smí mít komentáře a čárku na konci.
- Chyba syntaxe zastaví čtení na tom místě; klíče před ní zůstanou a zpráva uvede řádek.

## [tools.outline] Osnova kódu a symboly
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. V souborech HTML, PHP, CSS, C# a Markdown ukáže Nástroje, Zobrazit osnovu kódu funkce, třídy, nadpisy a pravidla souboru jako strom v panelu pluginu. Kliknutí označí jméno symbolu.
2. Nástroje, Přejít na symbol (nebo `@` v Přejít na cokoli) vypíše tytéž symboly podle jména; Enter označí stejné bajty jako kliknutí v osnově.
3. Osnova patří ke konkrétní revizi dokumentu. Po úpravě se sama obnoví; než přijde nová, zůstane stará označená jako zastaralá a skok se přesune přes provedené úpravy, nebo se odmítne, když se změnil text, na kterém symbol stál. Odpověď pro starší revizi se nikdy nezobrazí jako aktuální.
4. Když se proces pluginu zastaví, osnova se po jeho obnovení načte znovu.
- Čte se podle jednoduchých pravidel jazyka, bez jazykového serveru a bez indexu projektu: HTML (nadpisy, prvky s `id`, `main`, `nav`, `section` a podobné), PHP (jmenné prostory, třídy, rozhraní, traity, výčty, funkce, metody), CSS (pravidla a @-pravidla), C# (jmenné prostory, typy, metody, konstruktory, vlastnosti), Markdown (nadpisy `#`).
- Komentáře a řetězce se nečtou. Co pravidla nepoznají, se přeskočí.
- Soubor větší než limit pluginu nebo s víc symboly, než osnova unese, se zobrazí zkrácený a zpráva to řekne.

## [edit.carets] Více kurzorů
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Vyberte slovo a Ctrl+D přidá další jeho výskyt do výběru. Opakujte pro další výskyty.
2. Ctrl+K a potom Ctrl+D přeskočí poslední výskyt a vezme další.
3. Ctrl+Shift+L vybere všechny výskyty najednou.
4. Ctrl+Alt+šipka nahoru a Ctrl+Alt+šipka dolů přidají kurzor na řádek nad a pod.
5. Psaním se mění všechna místa naráz. Esc nechá jediný kurzor.
- Stavový řádek ukazuje počet kurzorů.
- Alt+tažení myší vytvoří kurzor na každém řádku nebo blokový výběr podle nastavení Alt+tažení.

## [edit.mark] Značka a výběr po značku
commands: nks.edit.selectToMark
1. Najděte příkaz Nastavit značku nebo vybrat po značku v nabídce Úpravy.
2. První použití uloží značku na místo kurzoru.
3. Přesuňte kurzor jinam a příkaz použijte znovu: vybere se text od značky ke kurzoru a značka zmizí.
- Klávesu F8 má tento příkaz jen v profilu chování PSPad; ve výchozím profilu je F8 blokový výběr.

## [edit.block] Blokový výběr
commands: nks.edit.toggleBlockSelect
1. Stiskněte F8 (v profilu PSPad Ctrl+Shift+C).
2. Šipkami, Home nebo Shift s šipkami rozšiřte obdélník přes řádky a sloupce.
3. Psaním nebo vložením se text změní na každém řádku bloku. Vložení využije řádky schránky po jednom.
4. Dalším stiskem F8 blok zrušíte.
- Blok jde vytvořit také tažením myši s Alt, když to nastavení Alt+tažení dovolí.
- Ve stránkovaném režimu blokový výběr není.

## [search.find] Hledání v dokumentu
commands: nks.search.find, nks.search.findNext, nks.search.findPrevious, nks.search.findWord
1. Ctrl+F otevře lištu hledání dole. Napište text; počet shod se ukáže vedle pole a shody se označí.
2. Enter najde další výskyt, Shift+Enter předchozí. Stejně fungují F3 a Shift+F3.
3. Ctrl+F3 hledá slovo pod kurzorem.
4. Esc lištu zavře.
- Přepínače: Aa rozlišuje velikost písmen (Alt+C), .* je regulární výraz (Alt+R), ab celá slova (Alt+W), Výběr hledá jen ve vybraném textu.
- Když hledání dojde na konec, pokračuje od začátku a stavový řádek to ohlásí.
- Neplatný výraz je vidět u počtu shod. Příliš pomalý výraz hledání zastaví.
- Hledání funguje i ve stránkovaném režimu; nahrazování tam ne.
- Zapnete-li regulární výraz, objeví se tlačítko Zkusit (téma Zkouška regulárního výrazu).

## [search.replace] Nahrazení
commands: nks.search.replace
1. Stiskněte Ctrl+H, nebo zvolte Hledat a Nahradit. Lišta má navíc pole Nahradit čím.
2. Napište hledaný a náhradní text.
3. Enter v poli náhrady nahradí jednu shodu, Ctrl+Alt+Enter všechny. Totéž dělají tlačítka Nahradit a Vše.
4. Stavový řádek napíše, kolik shod se nahradilo, nebo Nic se nenahradilo.
- V regulární náhradě platí $1, $& a změna velikosti písmen \U \L \E \u \l.
- Nejde u dokumentu jen pro čtení ani ve stránkovaném režimu.

## [search.inFiles] Hledání a nahrazení v souborech
commands: nks.search.findInFiles, nks.search.replaceInFiles
1. Ctrl+Shift+F otevře lištu s polem Složka; Ctrl+Shift+H přidá pole náhrady.
2. Vyplňte hledaný text a složku (tlačítko Procházet ji vybere) a stiskněte Enter nebo Hledat.
3. Výsledky se otevřou v kartě Výsledky hledání: soubory, řádky a počty. Enter nebo dvojklik otevře shodu.
4. U nahrazení tlačítko Náhled ukáže navržené změny. Odškrtnutím vyřadíte soubor nebo jednu změnu, Použít vybrané změny je zapíše.
- Hledání jde zrušit tlačítkem Zrušit nebo Esc. Při příliš mnoha shodách se zastaví a napíše, kolik jich ukázalo.
- Při nahrazení se konfliktní soubory nezmění: dokument se po náhledu změnil, soubor změnil jiný program, soubor zmizel, nový text nejde zapsat v kódování souboru nebo změna už nesedí. Nový náhled náhled vytvoří znovu.
- Před zápisem do souborů program zapíše plán do žurnálu; po pádu uprostřed dávky nabídne Dokončit nebo Vrátit zpět.
- F1 v kartě výsledků otevře toto téma.

## [search.regex] Zkouška regulárního výrazu
commands: nks.search.regexTester
1. Zvolte Hledat a Zkouška regulárního výrazu, nebo v liště hledání s regulárním výrazem stiskněte Zkusit.
2. Napište výraz, případně náhradu a vzorový text.
3. Okno ukáže shody se skupinami, text po náhradě a po částech vysvětlení výrazu.
- Používá se stejný stroj jako při hledání (regulární výrazy .NET). Syntaxi jiných dialektů vysvětlení označí jako nepodporovanou.
- Výraz se zpětným odkazem, lookaroundem nebo atomickou skupinou běží v pomalejším klasickém enginu a po limitu z nastavení Časový limit regulárního výrazu (výchozí 2000 ms) se zastaví; ostatní výrazy běží v lineárním čase bez limitu.
- Běží-li výraz příliš dlouho, zastaví se a okno to napíše. Vypíše se nejvýš omezený počet shod.
- F1 v tomto okně otevře toto téma.

## [search.gotoLine] Přejít na řádek
commands: nks.search.gotoLine
1. Stiskněte Ctrl+G, nebo zvolte Hledat a Přejít na řádek.
2. Napište číslo řádku (120), řádek a sloupec (120:5), procento (40 %) nebo bajt (@4096).
3. Potvrďte Enter. Kurzor skočí na místo.
- Číslo větší než počet řádků skočí na poslední řádek.
- Neplatný zápis napíše „...“ není číslo řádku.
- Ve velkém souboru, jehož řádky se ještě číslují, ukáže stavový řádek: Na tuto pozici ještě nevede index řádků, zkuste to za chvíli. Bajtový offset (@4096) nebo procenta fungují hned; dokud číslování nedoběhne, je číslo řádku po takovém skoku označeno ≈.

## [search.gotoBracket] Přejít na párovou závorku
commands: nks.search.gotoBracket
1. Dejte kurzor k závorce.
2. Stiskněte Ctrl+Shift+zpětné lomítko, nebo zvolte Hledat a Přejít na párovou závorku.
3. Kurzor skočí na protější závorku.
- Nemá-li závorka protějšek, stavový řádek napíše Závorka nemá protějšek.
- Ve velkém souboru ve stránkovaném režimu příkaz není.

## [search.gotoAnything] Přejít na cokoli
commands: nks.search.gotoAnything
1. Stiskněte Ctrl+P.
2. Napište část názvu nebo cesty souboru. Seznam ukazuje soubory otevřené složky, otevřené dokumenty a nedávné soubory.
3. Enter soubor otevře. Napíšete-li :120, kurzor skočí na řádek 120 aktivního dokumentu. 
- Bez otevřené složky se vypíšou jen otevřené a nedávné soubory.
- Soubor, který už neexistuje, je označen jako chybí.
- Pod seznamem je náhled zvýrazněného souboru; binární soubor se nezobrazí. Hodnoty souboru `.env` se v náhledu ukazují jako `••••`.
- V souboru YAML nebo JSON vypíše `@` s částí cesty klíče všechny klíče souboru (téma Cesty klíčů v YAML a JSON).

## [search.jump] Místa úprav a skoky
commands: nks.search.recentEdits, nks.search.jumpBack, nks.search.jumpForward
1. Ctrl+K a potom Ctrl+E otevře seznam míst, kde jste naposled upravovali text, nejnovější první. Zaškrtnutím lze omezit na upravené dokumenty. Enter na místo skočí.
2. Alt+šipka doleva se vrátí na místo, odkud jste skočili (po Přejít na řádek, otevření cesty, souvisejícího souboru a podobně). Alt+šipka doprava jde zpět vpřed.
- Není-li kam skočit, stavový řádek napíše Není kam skočit zpět nebo vpřed. Seznam bez úprav napíše Zatím žádná místa úprav.
- Místa se posouvají spolu s textem a přežijí zavření dokumentu.

## [search.related] Související soubor
commands: nks.search.relatedFile
1. V uloženém dokumentu stiskněte Alt+O, nebo zvolte Hledat a Přejít na související soubor.
2. Najde-li se jediný, otevře se. Je-li jich více, vyberte ze seznamu.
- Vestavěné dvojice: .c a .h či .hpp, .cpp a .hpp či .h, .m a .h, soubor a jeho _test (Go, Python), Test.php, Tests.cs, .spec.ts a .test.ts, .xaml a .xaml.cs (stejně axaml, razor).
- Vlastní dvojice zapíšete v nastavení Pravidla souvisejících souborů ve tvaru koncovka|koncovka, oddělené středníky.
- Dokument bez souboru napíše: Dokument nejdřív uložte. Bez shody: Pro název není žádný související soubor.

## [search.bookmarks] Záložky
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
1. Ctrl+F2 nastaví nebo zruší záložku na řádku s kurzorem; vlevo od čísel řádků se objeví tečka.
2. F2 skočí na další záložku, Shift+F2 na předchozí; za poslední se pokračuje od první.
- Hledat, Smazat záložky odstraní všechny záložky dokumentu.
- Záložka se posouvá s textem, když nad ní přibudou nebo ubudou řádky, a ukládá se do relace: po restartu je zpět.

## [view.wordwrap] Zalamování řádků
commands: nks.view.toggleWordWrap
1. Stiskněte Alt+Z, nebo zvolte Zobrazit a Přepnout zalamování řádků.
2. Dlouhé řádky se zalomí na okraji okna. Dalším stiskem se vrátí.
- Volba se uloží do nastavení Zalamování řádků.
- Ve stránkovaném režimu příkaz není.
- V profilu PSPad je zkratka Ctrl+W.

## [view.zoom] Velikost písma
commands: nks.view.zoomIn, nks.view.zoomOut, nks.view.zoomReset
1. Ctrl+= zvětší písmo o jeden bod, Ctrl+- zmenší.
2. Ctrl+0 vrátí výchozí velikost.
- Velikost je v rozsahu 6 až 72 bodů a ukládá se do nastavení Velikost písma.

## [view.display] Skryté znaky, čísla řádků a minimapa
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Zvolte Zobrazit a Zobrazit skryté znaky (Ctrl+K, Ctrl+W): vykreslí se mezery, tabulátory a konce řádků CR, LF a CRLF. Dokument se nemění.
2. Zobrazit čísla řádků (Ctrl+K, Ctrl+N), Zvýraznit aktuální řádek, Zobrazit pravý okraj (svislá čára ve sloupci 80, nastavuje se v nastavení Sloupec pravého okraje) a Klikací odkazy zapínají a vypínají zbytek.
3. Zobrazit minimapu (Ctrl+K, Ctrl+M) ukáže vpravo zmenšený obraz textu v barvách zvýraznění. Kliknutím nebo tažením se posouvá. U velkého souboru ukazuje jen řádky kolem obrazovky.
- Shody z lišty hledání jsou označené v úzkém pruhu na pravém okraji minimapy.
- Odkazy se otevřou kliknutím s Ctrl a ve stránkovaném režimu nejsou k dispozici.
- Řádek delší než 50 000 znaků se nikdy nezalomí: Přepnout zalamování to řekne ve stavovém řádku. Stránkovaný režim nezalamuje nikdy.
- Znaky, které hlavní písmo nemá, se vykreslí písmy z nastavení Náhradní písma.
- Ctrl a kolečko myši mění velikost písma jako příkazy zvětšení.

## [view.secrets] Skryté hodnoty v souborech .env
commands: nks.view.toggleSecrets
1. Soubory s názvem `.env`, `.env.*` nebo `*.env` ukazují každou hodnotu proměnné jako `••••`. Zobrazit a Zobrazit skryté hodnoty je ukáže v aktuální kartě, další použití je zase skryje.
- Když na hodnotu najedete ukazatelem myši, ukáže se jen ta jedna.
- Text souboru se nemění: uložení zapíše stejné bajty.
- Skryté jsou i v minimapě, v náhledech Přejít na cokoli a Místa úprav a ve výsledcích hledání. Kopírování dá do schránky `••••` místo skryté hodnoty a Vyjmout skrytou hodnotu nesmaže.
- Plugin čte místo každé skryté hodnoty hvězdičky `*`. Než se na takovém souboru spustí vestavěný příkaz, program se zeptá, jestli smí hodnoty přečíst.
- Stránkovaný režim a hexadecimální zobrazení ukazují bajty tak, jak jsou.

## [view.tabs] Přepínání karet
commands: nks.view.nextTab, nks.view.previousTab
1. Ctrl+Tab přepne na další kartu skupiny, Ctrl+Shift+Tab na předchozí.
2. Po poslední kartě se pokračuje první.
- Karty jde také přetahovat myší a seřadit.

## [view.documents] Seznam otevřených dokumentů
commands: nks.view.toggleDocumentList
1. Ctrl+B otevře nebo zavře panel Otevřené dokumenty vlevo.
2. Šipkami vyberte dokument, Enter ho zobrazí. Neuložené dokumenty mají značku ●.
3. Pole nahoře seznam filtruje.
- Esc panel zavře.

## [view.files] Panel Soubory
commands: nks.view.toggleFilePanel
1. Ctrl+Shift+E otevře nebo zavře panel Soubory. Bez otevřené složky nabízí Otevřít složku.
2. Šipky procházejí strom, Šipka doprava a doleva rozbalí a sbalí složku, Enter nebo dvojklik soubor otevře.
3. Pravé tlačítko nabízí operace se soubory (téma Operace se soubory). F2 přejmenuje, Delete smaže do koše, F5 obnoví.
- Strom sleduje změny na disku sám.

## [files.create] Nový soubor a nová složka
commands: nks.files.newFile, nks.files.newFolder
1. Otevřete složku a vyberte v panelu Soubory složku, do které se má vytvořit.
2. Pravým tlačítkem zvolte Nový soubor ve složce nebo Nová složka, nebo příkaz najděte v paletě.
3. Napište název a potvrďte.
- Ve stavovém řádku se objeví Vytvořeno a název.
- Neplatný název nebo existující název se odmítne (téma Chybová hlášení), nic se nezmění.

## [files.manage] Duplikovat, přejmenovat, přesunout, smazat
commands: nks.files.duplicate, nks.files.rename, nks.files.move, nks.files.delete
Vyberte soubor nebo složku v panelu Soubory. Operace jsou v místní nabídce a v paletě.
- Duplikovat vytvoří kopii vedle původního.
- Přejmenovat (F2): napište nový název. Otevřené karty a seznamy míst se přesunou s ním.
- Přesunout: napište cílovou složku.
- Smazat do koše (Delete): program se zeptá, a soubor jde do koše, odkud ho vrátíte. Je-li otevřený s neuloženými změnami, karta zůstane otevřená a změny si ponechá.
- Selže-li operace, nad dokumentem je lišta s důvodem a Nic se nezměnilo.

## [files.refresh] Obnovit a ukázat soubor
commands: nks.files.refresh, nks.files.reveal
- Obnovit soubory (F5 v panelu) znovu načte strom podle disku.
- Ukázat aktivní soubor v panelu otevře panel a vybere v něm soubor aktivní karty. Funguje při otevřené složce a otevřeném dokumentu.

## [format.encoding] Kódování
commands: nks.format.reread, nks.format.convert
Stavový řádek ukazuje kódování; klepnutím na něj se otevře stejný výběr. Je-li kódování odhad, je u něj procento jistoty.
- Znovu přečíst jako: soubor se znovu přečte z disku v jiném kódování. Použijte, když se po otevření ukážou špatná písmena.
- Převést na: dokument se převede do jiného kódování. Které znaky by se ztratily, ukáže seznam (ztrátové a počet) a okno s řádkem, sloupcem a bajtem každého znaku. Nahradit a převést je nahradí otazníkem.
- Výchozí seznam kódování v nastavení: UTF-8 (s BOM i bez), UTF-16 LE a BE, Windows-1250, Windows-1252, ISO-8859-2.
- U velkého souboru ve stránkovaném režimu jsou obě volby nedostupné.

## [format.eol] Konce řádků
commands: nks.format.eolCrLf, nks.format.eolLf, nks.format.eolCr
1. Zvolte Formát a Konce řádků na CRLF, LF nebo CR, nebo klepněte na konce řádků ve stavovém řádku (u smíšených je napsáno smíšené).
2. Ukáže se náhled změny: kolik konců řádků se změní. Použít změnu provede.
- Není-li co měnit, stavový řádek napíše Není co měnit.
- Jde to vrátit klávesou Zpět.
- U dokumentu jen pro čtení a ve stránkovaném režimu příkaz není.

## [format.normalize] Normalizace Unicode
commands: nks.format.normalizeNfc, nks.format.normalizeNfd
Znaky s diakritikou mohou být zapsány jedním znakem (NFC) nebo písmenem a samostatnou diakritikou (NFD). Vzhled je stejný, ale hledání a porovnání je rozlišuje.
1. Zvolte Formát a Normalizovat Unicode na NFC (nebo NFD).
2. Ukáže se náhled s počtem míst. Použít změnu provede.
- Kdyby se po normalizaci nějaký znak nedal zapsat v kódování souboru, program změnu odmítne a napíše to.
- Nebylo-li co měnit, stavový řádek napíše Není co měnit.

## [format.language] Režim jazyka
commands: nks.format.language
1. Zvolte Formát a Režim jazyka, nebo klepněte na jazyk ve stavovém řádku.
2. Vyberte jazyk, například Prostý text.
- Jazyk určuje nastavení platná jen pro tento jazyk (například zalamování řádků nebo odsazení) a pravidla zvýraznění.
- Při otevření se jazyk určí podle přípony souboru.
- Vlastní zvýrazňovače jsou data: contributes.grammars v nkspad-plugin.json uvádí soubory gramatik (JSON, formatVersion 1, popsané v grammar.schema.json): id, name, extensions, fileNames, comments, keywords a rules. Pravidlo je match (regulární výraz), words, operators, include, nebo oblast s begin a end, která může vnořit jiný jazyk.
- Zvýrazňovač může zahrnout a vnořit vestavěné jazyky, například "embed": "javascript". Soubor, který končí jednou z jeho přípon nebo má jeden z jeho fileNames, se otevře v tomto jazyce; id zvýrazňovače je jazyk v nastavení jazyka.
- Zvýrazňovač se špatným regulárním výrazem, neznámou vlastností, neznámým include nebo embed, nebo s id vestavěného jazyka se při načtení pluginu odmítne. Plugin se přeskočí a stavový řádek řekne, který soubor a které pravidlo.
- V příliš dlouhém řádku nechá zvýrazňovač z pluginu řádek bez barev.

## [window.new] Nové okno
commands: nks.window.new
1. Stiskněte Ctrl+Shift+N, nebo zvolte Okno a Nové okno.
2. Otevře se druhé okno programu. Dokumenty a karty má každé okno vlastní.
- Při zavření okna zůstane neuložená práce v žurnálu a vrátí se při dalším spuštění.

## [window.split] Rozdělený pohled
commands: nks.window.splitView, nks.window.moveToOtherGroup
1. Ctrl+\ ukáže druhý pohled téhož dokumentu ve skupině vpravo. Oba pohledy mají společný text a historii Zpět.
2. Přesunout kartu do jiné skupiny ji přesune do druhé skupiny (příkaz je v nabídce Okno).
3. Hranici skupin jde táhnout myší.
- Karty jde přetahovat i mezi skupinami.
- Skupina se zruší, když v ní nezůstane karta.

## [settings.theme] Motiv
commands: nks.settings.theme.system, nks.settings.theme.light, nks.settings.theme.dark
1. Zvolte Nastavení a jeden z motivů: Motiv podle systému, Světlý motiv, Tmavý motiv.
2. Změna se projeví hned a zapamatuje se.
- Podle systému znamená, že se program řídí nastavením systému.

## [settings.language] Jazyk rozhraní
commands: nks.settings.language.cs, nks.settings.language.en, nks.settings.language.sk, nks.settings.language.de
1. Zvolte Nastavení a Čeština, English, Slovenčina nebo Deutsch.
2. Nabídky, hlášení i tato nápověda se přepnou hned.
- Výchozí volba je podle systému (nastavení Jazyk rozhraní).

## [settings.profile] Profil chování
commands: nks.settings.profile.default, nks.settings.profile.pspad
1. Zvolte Nastavení a Profil chování: výchozí, nebo Profil chování: PSPad.
- Profil PSPad mění: Home a End přeskakují mezi prvním znakem a krajem řádku, Backspace v odsazení maže do tabulační zarážky, Alt+tažení dělá blokový výběr, párové znaky se doplňují podle PSPadu, a několik zkratek (Smazat řádek Ctrl+Y, Zpět Alt+Backspace, Zalamování Ctrl+W, F8 značka, Ctrl+Shift+C blokový výběr, Odsazení Ctrl+Shift+I a U).
- Vlastní zkratky v nastavení mají přednost před profilem.

## [view.output] Okno výstupu
commands: nks.view.toggleOutput
Okno výstupu je pruh pod editorem, do kterého zapisují úlohy a hledání. Ve výchozím stavu je skryté.
1. Stiskněte Ctrl+Shift+Y, nebo zvolte Zobrazit a Výstup.
2. Záložky Úlohy a Hledání přepínají kanál. Souhrn hledání v souborech se zapíše i do kanálu Hledání.
3. Tlačítko Vymazat kanál vyprázdní, Zavřít (nebo Esc) okno schová.
- Text jde vybrat a zkopírovat. Prázdný kanál napíše Do tohoto výstupu se zatím nic nezapsalo.
- Výstup se čte ze souborů v profilu, takže po pádu programu, který ho psal, zůstane vidět.

## [view.fullscreen] Celá obrazovka
commands: nks.view.fullScreen
1. Stiskněte F11, nebo zvolte Zobrazit a Celá obrazovka.
2. Zmizí rám okna; zůstane nabídka, karty, editor a stavový řádek. Psaní jde dál do editoru.
3. Dalším F11 se vrátí předchozí stav, i maximalizované okno.
- Na macOS používá F11 systém; použijte nabídku Zobrazit.

## [hex.open] Hex: otevření a pohyb
commands: nks.view.hex, nks.file.openAsHex
Hex zobrazuje soubor po bajtech: offset, bajty šestnáctkově a znaky. Čte jen řádky na obrazovce, takže velikost souboru nevadí.
1. Ctrl+K a potom Ctrl+H přepne aktivní dokument mezi textem a hexem (nabídka Zobrazit, Přepnout hex a text). Je to stejný dokument se stejnou historií Zpět; výběr se převede přes bajty.
2. Soubor a Otevřít v hex otevře vybraný soubor rovnou v hexu. Z příkazového řádku to dělá přepínač --hex, případně s --offset.
3. Soubor, který vypadá binárně (nulový bajt v prvních 8 000 bajtech), nabídne lišta nad dokumentem: Otevřít v hex, nebo Nechat jako text.
- Šipky, PageUp a PageDown, Home a End (na řádku), Ctrl+Home a Ctrl+End (celý soubor). Shift rozšiřuje výběr. Tab přepíná mezi sloupcem bajtů a znaků.
- Vyjmout, Kopírovat a Vložit pracují s bajty; mezi dvěma hexy se přenesou přesně.

## [hex.find] Hex: hledání a skok na offset
- Ctrl+F otevře okno Hledat bajty. Zadejte bajty šestnáctkově (50 4B ?? 04, ?? je libovolný bajt), text v uvozovkách, nebo číslo s typem (0x1F40 i32).
- F3 a Shift+F3 najdou další a předchozí shodu. Stavový řádek napíše Shoda 1 z 4, nebo Aspoň N shod, když hledání došlo na limit. Nahrazování bajtů není.
- Ctrl+G otevře Přejít na offset. Zápis: 0x1F40, +0x100 (od kurzoru), end-512, 50%, 4K, sel (začátek výběru), val (čtyřbajtová hodnota pod kurzorem).
- Nenalezeno: shoda není. Neplatný vzor nebo Neplatný výraz: opravte zápis. Offset leží mimo soubor: zadejte menší hodnotu. Soubor se mezitím změnil na disku, výsledek hledání se zahodil: hledání zopakujte.

## [hex.edit] Hex: úpravy
commands: nks.hex.toggleInsert
1. Klávesa Insert (na macOS ⌥⌘I) přepíná mezi PŘEPIS a VKLÁDÁNÍ; stav je vidět ve stavovém řádku.
2. V přepisu se bajty přepisují a délka souboru se nemění. Na konci souboru program napíše: Přepis nemění délku souboru, a ať zapnete vkládání.
3. Delete a Backspace v přepisu se zeptají: Vynulovat (délka zůstane), nebo Smazat (soubor se zkrátí).
4. Vložíte-li více bajtů, než zbývá do konce souboru, program se zeptá: Prodloužit soubor, nebo Vložit jen to, co se vejde.
- Znak ve sloupci znaků, který kódování nezapíše, program odmítne: Znak nejde zapsat v kódování.
- Zpět a Znova fungují. Změněné bajty jsou podbarvené a podtržené.
- Když se soubor právě ukládá, úpravy počkají na konec uložení.

## [hex.inspector] Hex: inspektor dat
commands: nks.hex.inspector
1. Ctrl+K a potom Ctrl+I otevře nebo zavře panel Inspektor dat vpravo (jen v hexu).
2. Ukáže hodnotu od kurzoru jako různé typy: čísla, datum, GUID, znak. Přepínač LE a BE volí pořadí bajtů.
3. Hodnotu přepíšete v řádku: Enter ji zapíše, Esc zruší.
- Hodnota na konci souboru, které chybí bajty, je neúplná a nejde přepsat. Neplatný zápis a hodnota mimo rozsah typu se ohlásí (například Mimo rozsah typu, Neplatný GUID, Datum a čas ve tvaru RRRR-MM-DD HH:MM:SS).
- Hodnota, která zabere jiný počet bajtů, než kolik přepisuje, vyžaduje režim vkládání.

## [hex.layout] Hex: vzhled mřížky
commands: nks.hex.charset, nks.hex.layout, nks.hex.bytesPerRow8, nks.hex.bytesPerRow16, nks.hex.bytesPerRow24, nks.hex.bytesPerRow32, nks.hex.bytesPerRow48, nks.hex.bytesPerRow64, nks.hex.offsetFormat
Příkazy jsou dostupné v hexu, v nabídce Zobrazit, ve stavovém řádku a v paletě.
- Kódování znakového sloupce: výběr kódování, kterým se bajty ukazují jako znaky.
- Rozložení: bajtů na řádek (8, 16, 24, 32, 48, 64), seskupení po bajtech a formát offsetů. Každá velikost má i vlastní příkaz.
- Přepnout offsety dekadicky a hexadecimálně.
- Volby si další hex zobrazení pamatuje.

## [hex.save] Hex: uložení a změny před uložením
commands: nks.hex.inspectChanges
1. Ctrl+S uloží hex. Ukládá se na pozadí a okno zůstává použitelné; Esc uložení zruší dřív, než se zapíšou data (Ukládání zrušeno, soubor zůstal beze změny).
2. Soubor a Změny před uložením ukáže, co uložení zapíše: změněné úseky se starými a novými bajty a způsob zápisu.
- Mění-li se jen hodnoty bajtů, uloží se na místě (Uloženo na místě, změněno N B) a původní bajty se nejdřív zapíšou do žurnálu, aby šlo uložení po pádu dokončit nebo vrátit.
- Mění-li se délka, uloží se nový soubor, který se atomicky vymění.
- Před přepsáním menšího souboru vznikne záloha; nepovede-li se, soubor zůstane beze změny.

## [hex.copyAs] Hex: kopírování, vkládání a export jako
commands: nks.hex.copyAs, nks.hex.pasteAs, nks.hex.exportSelection
1. Úpravy a Hex: Kopírovat výběr jako dá vybrané bajty do schránky jako text: hex řetězec, pole pro C, C#, Javu, Python, Rust, Go, JavaScript nebo Pascal, Base64, Intel HEX, Motorola S-record nebo hexdump.
2. Hex: Vložit jako přečte schránku jako hex text, Base64 nebo pole ze zdrojového kódu a vloží bajty stejně jako Ctrl+V.
3. Hex: Exportovat výběr zapíše výběr do souboru, binárně nebo v jedné z textových podob. Běží na pozadí a Esc ho zruší.
- Intel HEX, S-record a hexdump nesou jako adresu offset každého bajtu v souboru; Intel HEX a S-record mají 32bitové adresy, takže výběr za 4 GiB se odmítne.
- Výběr, jehož text by se do schránky nevešel, se odmítne s radou ho exportovat.
- Pole vložené ze zdrojového kódu může obsahovat čísla desítkově, hex ve tvaru 0x i $ a binárně ve tvaru 0b; číslo mimo -128 až 255 vložení zastaví.

## [hex.fill] Hex: vyplnění a operace nad výběrem
commands: nks.hex.fill, nks.hex.operate
1. Hex: Vyplnit výběr vyplní výběr vzorem z hex bajtů nebo textu, zopakuje výběr za sebou, zapíše náhodné bajty nebo čísla s krokem. Bez výběru vyplní vzor s * a počtem tolik bajtů u kurzoru (v režimu vkládání je vloží).
2. Hex: Operace nad výběrem provede XOR, AND, OR, NOT, přičtení, posun nebo rotaci o 1 až 7 bitů (u každého bajtu) nebo prohození pořadí bajtů po 2, 4 nebo 8. Operandy se opakují po celém výběru.
3. Dialog před potvrzením ukáže, kolik bajtů se změní. Každé vyplnění i operace je jeden krok zpět a vyplněné bajty zůstanou vybrané.
- Vyplnění větší než 256 MiB se drží jako vzor a délka, ne jako bajty, takže i 5 GiB nestojí paměť ani čas; uložení ho zapíše jako nový soubor. Vrácení takového vyplnění i kroků před ním trvá do uložení přes soubor.
- Náhodné bajty, čísla s krokem a operace drží výběr v paměti a odmítnou víc než 2 GiB.

## [hex.checksum] Hex: kontrolní součty a hashe
commands: nks.hex.checksum
1. Hex: Kontrolní součty a hashe počítá nad výběrem, nebo nad celým souborem, když nic není vybráno: CRC-16, CRC-32, CRC-64 v několika variantách, CRC s vlastním polynomem, Adler-32, Sum-8/16/32, xxHash32, xxHash64, MD5, SHA-1, SHA-256, SHA-384 a SHA-512.
2. Výpočet běží na pozadí a postup ukazuje stavový řádek; Esc ho zruší.
3. Výsledek jde zkopírovat nebo porovnat s vloženou hodnotou; na mezerách, dvojtečkách, 0x a velikosti písmen nezáleží.
- Každá varianta CRC má pevný polynom a parametry (šířka, počáteční hodnota, zrcadlení vstupu a výsledku, koncové xor) podle katalogu algoritmů CRC.
- CRC s vlastním polynomem vezme šířku v bitech a hex polynom (lichý), počáteční hodnotu, dva příznaky zrcadlení (0 nebo 1) a koncové xor.

## [tools.run] Nástroje: spuštění externího programu
commands: nks.tools.run, nks.tools.stop
1. Nástroje, Spustit nástroj nabídne nástroje zapsané v souboru tools.json ve složce profilu. Poslední řádek ten soubor otevře; poprvé do něj zapíše prázdný, který doplníte.
2. Nástroj má id, title, executable a args (seznam: jedna položka je jeden argument). Volitelné jsou cwd (adresář, kde poběží; bez něj složka aktuálního souboru), timeoutSeconds (bez něj není žádný časový limit) a logPattern (viz Vzor LOGu). Příklad: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. V executable, args a cwd zastupují proměnné ${File} (úplná cesta), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} a ${Column} aktuální dokument; $$ je znak dolaru. Argument zůstane jedním argumentem, ať hodnota obsahuje cokoli, i mezery a uvozovky: žádný shell se nepoužívá. Neznámá proměnná nebo ${File} u dokumentu bez souboru zastaví spuštění dřív, než cokoli začne.
4. Nástroj, jehož příkaz a adresář ještě neběžely, se ukáže s oběma a spustí se až po vašem potvrzení. Potvrzení platí, dokud se nezmění definice nebo program, na který vede.
5. Výstup jde řádek po řádku na kartu Úlohy v okně výstupu, nakonec s kódem ukončení a časem. Tlačítko Zastavit v tom okně nebo příkaz Zastavit běžící nástroj ukončí program i vše, co spustil; totéž udělá časový limit.
- Dávkové soubory (.bat, .cmd) se ve Windows odmítnou, protože by příkazový interpret argumenty znovu rozdělil a rozvinul. Uveďte program, který dávka spouští.
- Název programu bez cesty se hledá jen v PATH, nikdy v pracovním adresáři.
- Výstup se čte jako UTF-8. Běží vždy jen jeden nástroj.

## [tools.logPattern] Vzor LOGu: kliknutí na chybu ve výstupu
commands: nks.tools.logPattern
1. Vzor LOGu pro výstup otevře zkoušku vzoru s posledním výstupem nástrojů jako ukázkou. Každý řádek ukázky ukáže soubor, řádek a sloupec, které v něm vzor našel, nebo že nenašel nic.
2. Maska se porovnává zleva a bez ohledu na velikost písmen. * přeskočí libovolný text až k další pevné části masky. %F je název souboru, až po další pevnou část. %D je disk (C: následované lomítkem) před ním. %L je řádek a %C sloupec (číslice). %S přeskočí mezery a tabulátory. %E ukončí masku. Maska potřebuje %L; bez %F míří na soubor, který byl otevřený. Příklady: %F:%L:%C:* pro main.c:12:5: error, *] %F(%L) pro [Error] a.pas(346): text.
3. Druhý jazyk je regulární výraz se skupinami line (povinná), file a column.
4. Použít pro výstup zařídí, že vzor platí pro řádky v okně výstupu: kliknutí na řádek nebo Enter otevře soubor na tom řádku. Relativní název souboru se hledá v adresáři, kde nástroj běžel, pak ve složce souboru, který byl otevřený při spuštění, pak mezi otevřenými dokumenty podle názvu.
- Nástroj může mít vzor vlastní: v tools.json "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] a v nástroji "logPattern": "gcc". Bez "syntax" je vzor regulární výraz.

## [log.mode] Režim logu: úrovně a další chyba
commands: nks.log.nextError, nks.log.previousError, nks.log.levels
1. Dokument v jazyce Log (soubory .log, nebo Formát, Jazyk) obarví každý řádek podle jeho úrovně: chyba, varování, informace nebo ladění. Rozhoduje nejzávažnější úroveň nalezená v řádku.
2. Log: další chyba a Log: předchozí chyba skočí od kurzoru na další nebo předchozí řádek úrovně chyba. Soubor se čte po blocích a hledání skončí u prvního nálezu, takže funguje v souboru jakékoli velikosti; stavový řádek ukazuje průběh a Esc hledání zruší.
3. Log: úrovně vybere pravidla pro dokument. Výchozí pravidla hledají velkými písmeny napsaná slova ERROR, ERR, FATAL, CRITICAL, SEVERE, WARN, WARNING, INFO, DEBUG a TRACE jako celá slova. Vlastní pravidla přijdou z tools.json: "logPatterns": [{"id": "mine", "title": "Můj log", "levels": {"error": "\\|E\\|", "warning": "\\|W\\|"}}]. Klíče jsou error, warning, info a debug; každá hodnota je regulární výraz. Příkaz také zapne režim logu pro dokument.
- Pravidla platí, dokud je dokument otevřený, a neukládají se.
- Řádek delší než 8192 znaků se v editoru posuzuje podle prvních 8192 znaků.

## [view.options] Zobrazení editoru
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
Příkazy jsou v nabídce Zobrazit a v paletě. Každý přepíná jedno nastavení; jde je změnit i v nastavení.
- Zobrazit skryté znaky (Ctrl+K a potom Ctrl+W): vykreslí mezery a tabulátory. Odpovídá nastavení Zobrazit bílé znaky.
- Zobrazit čísla řádků (Ctrl+K a potom Ctrl+N): sloupec s čísly řádků vlevo (nastavení Čísla řádků).
- Zvýraznit aktuální řádek: podbarví řádek s kurzorem (nastavení Zvýraznit aktuální řádek).
- Zobrazit pravý okraj: svislá čára za sloupcem z nastavení Sloupec pravého okraje; hodnota 0 čáru vypíná.
- Klikací odkazy: webová adresa se otevře kliknutím při stisknutém Ctrl (nastavení Klikací odkazy). Ve velkém souboru ve stránkovaném režimu příkaz není.
- Zobrazit minimapu (Ctrl+K a potom Ctrl+M): zmenšený obraz textu vpravo, tažením se posouvá (nastavení Minimapa).
- Příkazy jsou ztlumené bez otevřeného dokumentu.

## [bookmarks] Záložky
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
Záložka označí řádek, na který se chcete vrátit.
1. Ctrl+F2 nastaví záložku na řádku s kurzorem, na řádku, který ji už má, ji odstraní. Stavový řádek napíše Záložka nastavena na řádku N, nebo Záložka na řádku N odstraněna.
2. F2 skočí na další záložku, Shift+F2 na předchozí. Po poslední se pokračuje od první.
3. Smazat záložky (v paletě) odstraní všechny záložky dokumentu; stavový řádek napíše Záložky odstraněny.
- Nemá-li dokument žádné záložky, stavový řádek napíše Dokument nemá žádné záložky.
- Záložky nejsou v hexu ani v kartě výsledků hledání. Ve velkém souboru, jehož řádky se ještě číslují, příkaz napíše, že index řádků ještě nesahá na to místo.

## [recovery] Obnova po pádu
Program průběžně zapisuje neuložený text do žurnálu, nejdéle do dvou sekund od změny. Neuložený dokument se tak neztratí, ani když program spadne nebo vypnete počítač.
1. Spusťte program znovu.
2. Karty se vrátí včetně neuloženého textu. Nad dokumentem je lišta Po neočekávaném ukončení se vrátily karty a v závorce počet karet s neuloženými změnami.
3. Tlačítko Podrobnosti vypíše každý dokument: obnoveno včetně neuložených změn, znovu otevřeno ze souboru, soubor se mezitím změnil, soubor už neexistuje, nebo nepodařilo se obnovit s důvodem.
- Když se soubor mezitím změnil na disku, změny zůstanou v žurnálu a nic se nepřepíše.
- Žurnál leží ve složce profilu. Jinou složku určuje nastavení Složka žurnálu, náhradní Náhradní složka žurnálu.
- Nejde-li žurnál zapisovat do žádné složky, lišta řekne: Obnova po pádu je vypnutá. Neuložený text pak zůstává jen v kartách; ukládejte ručně. Když přestane fungovat za provozu: Žurnál přestal fungovat, obnova už neplatí, ukládejte ručně.
- Použije-li se náhradní složka, lišta napíše, která.
- Přerušená dávka nahrazení v souborech se po startu nabídne: Dokončit, nebo Vrátit zpět.

## [paged] Velké soubory (stránkovaný režim)
Soubor větší než hranice velkého souboru (nastavení Hranice velkého souboru, výchozí 6 MiB) se otevře ve stránkovaném režimu. Chcete-li soubor procházet a upravovat po bajtech bez těchto omezení, otevřete ho v hexu (téma Hex: otevření a pohyb). Program čte jen řádky na obrazovce, takže soubor se otevře rychle a nezabere paměť.
Stavový řádek pak ukazuje Velký soubor, jeho velikost a revizi.
### Co jde
- Procházet šipkami, PageUp, PageDown, Home, End a Ctrl+Home a Ctrl+End. Přejít na řádek, na procento nebo na bajt (Ctrl+G).
- Psát, mazat (Backspace a Delete), Enter a Tab na místě kurzoru. Zpět a Znovu. Uložit.
- Hledat (Ctrl+F). Hledání v souborech.
### Co nejde
- Výběr, schránka (Vyjmout, Kopírovat, Vložit), blokový výběr, více kurzorů.
- Příkazy nad řádky, odsazením, velikostí písmen, nahrazení.
- Přejít na párovou závorku, zalamování řádků.
- Převod a opětovné čtení kódování, sjednocení konců řádků, normalizace.
- Uložit jako jen ve stejném kódování.
- Řádek delší než 16 KiB se ukáže jen z části a upravit nejde.
### Hlášení
- Číslování řádků ještě běží, úpravy půjdou za chvíli: počkejte, až se soubor dočte.
- Řádek je delší než 16 KiB, ve stránkovaném režimu se nedá upravit: řádek upravte jiným programem.
- Znak se nedá zapsat v kódování souboru: napište jiný znak.
- Velký soubor v kódování ... se zatím nedá otevřít: soubor se neotevře; jiné kódování ve velkém souboru program zatím nepřečte.
- Ukládání velkého souboru zdrží okno.

## [conflict] Změna souboru zvenku
Změní-li soubor jiný program, nad dokumentem se ukáže lišta Soubor se na disku změnil.
- Znovu načíst zahodí vaše změny a načte soubor z disku.
- Ponechat moje nechá vaše změny; další uložení se zeptá, než soubor přepíše.
- Porovnat otevře porovnávač (téma Porovnat s diskem).
Je-li soubor smazaný nebo přejmenovaný, lišta napíše, že už není. Ponechat nechá kartu, Zavřít kartu ji zavře.
Při uložení dostanete tutéž otázku v okně Soubor se změnil: Přepsat soubor, Porovnat, Zrušit. Nic se nepřepíše bez souhlasu.

## [statusbar] Stavový řádek
Dole je vidět řádek a sloupec kurzoru (u výběru počet znaků, u více kurzorů jejich počet), odsazení, kódování, konce řádků a jazyk. Zprávy o výsledku příkazu se ukážou na stejném místě.
- Klepnutí na odsazení nabídne mezery nebo tabulátory a velikost.
- Klepnutí na kódování otevře výběr kódování (téma Kódování).
- Klepnutí na konce řádků je sjednotí (téma Konce řádků).
- Klepnutí na jazyk otevře výběr jazyka (téma Režim jazyka).
- Zvýraznění se dopočítává: u dlouhého souboru zvýraznění dobíhá, text je použitelný. Řádek je příliš dlouhý na zvýraznění: takový řádek zůstane bez barev.
- U souboru, na který se vztahuje .editorconfig, je vidět položka .editorconfig; klepnutím se soubor otevře.

## [errors.file] Chybová hlášení: soubory
- Soubor se nepodařilo otevřít: cesta (důvod). Zkontrolujte, že soubor existuje, že na něj máte oprávnění a že ho nedrží jiný program.
- Uložení se nepovedlo: důvod. Obsah zůstává v kartě. Uložte jinam (Uložit jako) nebo opravte příčinu (místo na disku, oprávnění) a uložte znovu.
- Záloha se nepovedla, soubor zůstal beze změny: příčinu opravte (obvykle místo na disku) a uložte znovu.
- Uloženo, ale záloha se nepovedla: soubor je v pořádku, jen chybí záloha předchozí verze.
- Soubor se změnil nebo Soubor už na disku není: viz téma Změna souboru zvenku.
- Znaky, které kódování nepodporuje: vyberte jiné kódování (například UTF-8), nebo zvolte Nahradit a uložit, čímž se znaky nahradí otazníkem.
- Po normalizaci by znaky nešly zapsat v kódování: změňte kódování souboru, normalizaci pak zopakujte.
- Změna se nepovedla, text se mezitím změnil: příkaz zopakujte.
- Ořez mezer se při ukládání nepodařil: soubor se uloží bez ořezu; vypněte ořez mezer v nastavení, nebo soubor zmenšete.
- Velký soubor v kódování ... se zatím nedá otevřít: viz téma Velké soubory.

## [errors.files] Chybová hlášení: panel Soubory
Při nepovedené operaci se souborem je nad dokumentem lišta Operace selhala a důvod, a vždy Nic se nezměnilo.
- cesta neexistuje: obnovte strom (F5), soubor mezitím zmizel.
- cesta už existuje: zvolte jiný název.
- K cestě chybí oprávnění: změňte oprávnění ve správci souborů systému.
- cestu používá jiný program: zavřete ten program a zkuste to znovu.
- Na disku není dost místa: uvolněte místo.
- název není platný název: odstraňte znaky, které systém v názvech zakazuje.
- S cestou to na tomto systému takto nejde: použijte správce souborů systému.
- Složku nejde otevřít: vyberte jinou složku nebo zkontrolujte oprávnění.

## [errors.search] Chybová hlášení: hledání
- Neplatný výraz: důvod. Opravte regulární výraz nebo vypněte .* ; tlačítko Zkusit ukáže, jak se výraz čte.
- Výraz je příliš pomalý a hledání ho zastavilo: zjednodušte výraz, zejména vnořená opakování, nebo zvyšte nastavení Časový limit regulárního výrazu.
- Nejdřív vyberte text, ve kterém se má hledat: přepínač Výběr je zapnutý bez výběru; vyberte text, nebo přepínač vypněte.
- Složka neexistuje: opravte cestu v poli Složka.
- Nic nenalezeno: shoda není; zkontrolujte přepínače Aa, .* a ab.
- Zobrazeno prvních N shod, hledání se zastavilo: zúžte hledání.
- Hledání bylo zrušeno: spusťte ho znovu.
- Soubory s konfliktem zůstaly beze změny: viz téma Hledání a nahrazení v souborech.
- Žurnál obnovy není k dispozici: nahrazení v souborech se provede bez jistoty, že se po pádu dá dokončit nebo vrátit; raději ho spusťte, až žurnál funguje.
- „...“ není číslo řádku: v okně Přejít na řádek napište číslo, řádek:sloupec, procento nebo @bajt (téma Přejít na řádek).

## [limits] Co tato verze neumí
Nápověda popisuje jen to, co tato verze umí. Tato věc v ní zatím není:
- Aktualizace programu: program se sám neaktualizuje a nekontroluje nové verze. Novou verzi nainstalujete sami.
- Hex editor nemá minimapu, nehledá všechny shody do karty a bajty nenahrazuje; skok na offset neukazuje náhled cíle; nastavení, které by soubory vždy otevíralo v hexu, chybí.
- Porovnání souborů vlastním oknem: používá se externí porovnávač (téma Porovnat s diskem).
- Úpravy velmi velkých souborů: jen omezené, viz téma Velké soubory.
- Rozšíření (pluginy), vzdálené soubory a další funkce z plánu verze 1.0: v této verzi nejsou.

## [edit.clips] Klipy
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables
Klip je kus textu s místy pro proměnné a kurzory. Klipy dodávají datové pluginy ve složce plugins v profilu; jeden podadresář na plugin, se souborem nkspad-plugin.json a bez programu.
1. Zvolte Úpravy, Vložit klip. Seznam ukazuje klipy s náhledem textu, který by se vložil.
2. Vyberte klip. Má-li formulář, vyplňte ho: náhled sleduje, co píšete, špatná hodnota je označená a OK zůstane zašedlé, dokud každá hodnota nesedí k svému typu.
3. Text se vloží do každého kurzoru a nahradí každý výběr, jako jeden krok Zpět. Kurzory skončí tam, kde má klip značku $|.
- Uložit výběr jako klip se zeptá na název a přidá označený text do vaší vlastní knihovny (plugin user.clips, složka user-clips). Text se uloží doslova, takže dolar v něm zůstane dolarem. Klip je v seznamu hned.
- Jazyk šablon: ${name} je proměnná, $| značí kurzor (kolik chcete), $$ je znak dolaru. Konce řádků se změní na konec řádku dokumentu.
- Odkud proměnná bere hodnotu, od nejsilnějšího: 1. hodnota napsaná ve formuláři klipu, 2. vaše vlastní proměnné (Úpravy, Upravit uživatelské proměnné otevře soubor variables.json v profilu: jeden objekt JSON se jménem a textem), 3. vestavěné. Stejné jméno znamená všude totéž.
- Vestavěné proměnné: file.path, file.dir, file.name, file.base, file.ext (neuložený dokument file.* nemá), date (rrrr-MM-dd), time (HH:mm:ss), datetime (nastavení Formát data a času), guid (pro každý kurzor nový), selection (text vybraný u tohoto kurzoru), user.name. Jméno bez hodnoty vložení zastaví a stavový řádek ho vypíše.
- Pole formuláře: name, label, type, default, choices, required. Typy: text, integer (celé číslo), number (desetinná tečka), color (#RGB, #RGBA, #RRGGBB, #RRGGBBAA), date (rrrr-MM-dd), file (existující soubor), choice (jedna z hodnot choices). Výchozí hodnota smí používat proměnné, třeba ${date}.
- V nkspad-plugin.json je klip {"id": "box", "title": "Box", "text": "<div>$|</div>", "fields": [...]} v contributes.clips; klip s "command" dostane i příkaz toho jména.
- Potíže s pluginem (poškozený soubor, program v datovém pluginu) se ukážou ve stavovém řádku a plugin se přeskočí.
- Klipy nefungují v dokumentu jen pro čtení ani ve stránkovaném režimu (téma Velké soubory).

## [file.templates] Nový soubor ze šablony
commands: nks.file.newFromTemplate
1. Zvolte Soubor, Nový ze šablony a vyberte šablonu. Šablona s formulářem se ptá na hodnoty stejně jako klip.
2. Otevře se nová karta s textem šablony a kurzory na jejích značkách $| (na začátku, když žádnou nemá).
- Šablony jsou taky data: contributes.templates v nkspad-plugin.json, s id, title, text, fields a volitelně command. Používají jazyk šablon a proměnné z tématu Klipy; nový soubor nemá proměnné file.*.

## [edit.convert] Převod textu
commands: nks.edit.convert
Převod změní text všech výběrů najednou, nebo celého souboru, když nic není označeno. Nový text zůstane označený.
1. Zvolte Úpravy, Převést. Seznam ukáže převody s náhledem na označeném textu (když nic není označeno, na prvním vzorku převodníku).
2. Vyberte jeden. Všechny výběry se změní jedním krokem Zpět. Když se některý nedá převést, nic se nezmění a stavový řádek řekne proč.
- Zopakovat poslední příkaz spustí poslední převod bez seznamu.
- Převodníky jsou data: contributes.converters v nkspad-plugin.json, s id, title, rules a volitelně samples a command. Pravidlo je {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Pravidla běží popořadě, každé nad výsledkem předchozího. Se zapnutým regex platí dialekt hledání: replace smí používat $1, ${name}, $& a kódy velikosti písmen \U \L \E \u \l.
- Vzorek je {"input": "...", "expected": "..."}. Při čtení pluginu se každý vzorek převede. Převodník, jehož vzorek dá jiný text, který nemá pravidla, má prázdné find nebo chybný regulární výraz, se odmítne a plugin se přeskočí s důvodem ve stavovém řádku.
- Převodník s "command" dostane i příkaz toho jména, který převádí hned, bez seznamu.
- Speciální převody samotného editoru jsou ve stejném seznamu. Tato verze zatím žádné nemá.

## [edit.recipes] Recepty úprav
commands: nks.edit.runRecipe
Recept vybere části otevřeného souboru, změní je a před zápisem ukáže výsledek.
1. Zvolte Úpravy, Spustit recept úprav. Seznam ukáže každý recept se změnami, které by v tomto souboru udělal.
2. Vyberte jeden. Okno náhledu vypíše každou změnu: řádek, sloupec, starý a nový text. Tlačítkem Použít je zapíšete.
3. Všechny změny jsou jeden krok Zpět; Zpět vrátí soubor přesně do původního stavu. Změněné části zůstanou označené. Nic se nezapíše, když se soubor mezitím změnil nebo recept nejde spustit (proměnná bez hodnoty, příliš pomalý vzor).
- Recept pracuje s výběry, nebo s celým souborem, když nic není označeno. Zopakovat poslední příkaz použije poslední recept bez náhledu.
- Recepty jsou data: contributes.recipes v nkspad-plugin.json, s id, title, steps a volitelně command. Kroky, které vybírají, jdou první. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} ponechá shody uvnitř dosavadních částí. {"kind": "columns", "from": 3, "to": 5} ponechá sloupce 3 až 5 každého řádku (počítá se ve znacích od 1; to 0 nebo vynechané znamená do konce řádku; kratší řádek nedá nic).
- Kroky, které mění: {"kind": "convert", "converter": "id"} (převodník stejného pluginu), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} a {"kind": "set", "text": "..."}, který nahradí část textem v jazyce šablon klipů. V něm je ${selection} samotná část a proměnné mají stejné hodnoty jako v klipu (téma Klipy); kurzory $| se ignorují.
- Recept bez kroků, s neznámým druhem, chybným vzorem, neznámým převodníkem, vybírajícím krokem po měnícím nebo bez měnícího kroku se při čtení pluginu odmítne.

## [edit.aiContext] Kopírovat jako kontext pro AI
commands: nks.edit.copyAiContext
Vloží do schránky výběr, soubor, otevřené karty nebo otevřenou složku jako jeden blok v Markdownu nebo XML, který vložíte do AI asistenta. Nic se nikam neposílá.
1. Zvolte Úpravy, Kopírovat jako kontext pro AI a vyberte, co kopírovat: Výběr (všechny výběry), Tento soubor, Otevřené karty nebo Otevřenou složku.
2. Náhled ukáže, kolik souborů se vloží, velikost jako Markdown a jako XML a co bylo vynecháno s důvodem. Zvolte Kopírovat jako Markdown nebo Kopírovat jako XML.
- U každého souboru je cesta, řádky, jazyk, kódování a revize. Soubor s neuloženými změnami je označen a text je ten, který je teď v editoru.
- Vynecháno a pojmenováno: soubory .env (jejich hodnoty se nikdy nekopírují, ani když jsou v editoru zobrazené), soubory a složky vyřazené soubory ignorování, skryté, binární soubory, soubory nad limitem velikosti při procházení složky a soubory, které nejdou přečíst.
- Velký soubor ve stránkovaném režimu se nikdy nekopíruje celý: jen jeho výběr. Bez výběru je uveden jako vynechaný.

## [tools.emmet] Zkratky Emmet
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. V souboru HTML nebo CSS napište zkratku a stiskněte Tab. Z `ul>li*3` vznikne seznam o třech položkách s kurzorem v každé z nich.
2. Tab odsazuje jako dřív, když před kurzorem žádná zkratka není, když je něco označeno, když je kurzor uvnitř otevřené značky nebo když zkratka není platná.
3. Nástroje, Náhled zkratky Emmet ukáže, čím by zkratka před kurzorem byla (`|` označuje kurzory), a po potvrzení ji rozbalí.
4. Nástroje, Obalit zkratkou Emmet (jen HTML) se zeptá na zkratku a obalí jí každý výběr. Text přijde do nejvnitřnějšího posledního prvku; holá `*` na tom prvku (`ul>li*`) udělá jeden prvek na řádek.
5. Každé rozbalení jde do všech kurzorů najednou a je jeden krok Zpět.
- HTML: `name#id.class[attr=value]{text}*3`; `>` potomek, `+` sourozenec, `^` o úroveň výš, `( )` skupina; v názvech, třídách a textu je `$` číslo opakování (`$$` doplní nuly, `$@-` počítá dolů, `$@3` začíná od 3). Samotné slovo se rozbalí, jen když je to název značky HTML nebo krátký název z pluginu (`bq`, `btn`, `a:link`, `input:checkbox`, `!` pro kostru stránky). Uvnitř prvku se chybějící název doplní na `li`, `tr`, `td`, `option`, `span` nebo `div`, podle rodiče.
- CSS: `m10-20` je `margin: 10px 20px;`, `w100p` je `width: 100%;`, `c#f00`, `pos:a`, `dn`, koncový `!` přidá `!important`, `+` spojí víc deklarací. Celé číslo dostane `px`, zlomek `em`; písmena `p`, `e`, `x`, `r` za číslem znamenají `%`, `em`, `ex`, `rem`. Funguje jen uvnitř bloku `{ }`.
- Krátké názvy jsou vlastní tabulka pluginu s běžnými vlastnostmi a značkami, ne úplná sada Emmet.
- Kurzory stojí ve všech prázdných prvcích najednou (když žádný není, v první prázdné hodnotě atributu); skok z jednoho zástupného místa na další zatím není.
- Proces Emmet se spustí prvním Tabem v souboru HTML nebo CSS.

## [edit.complete] Doplňování slov a klipů
commands: nks.edit.complete
1. Stiskněte Ctrl+J (Úpravy, Doplnit slovo nebo klip). Seznam nabídne slova, která pokračují slovo před kurzorem, a pak klipy, které se hodí pro jazyk dokumentu.
2. Vyberte položku. Slovo nahradí slovo před každým kurzorem; klip se vloží jako v tématu Klipy a nahradí toto slovo. Je to jeden krok Zpět.
- Slova se berou ze všech otevřených textových karet. Slovo, které právě píšete, se nenabízí, a slovo se nabízí, jen když je delší než napsaný začátek. Nejdřív jdou slova z upravovaného dokumentu, pak ta častější.
- Klipy jsou z knihovny z tématu Klipy. Klip bez jazyka se hodí do každého dokumentu; klip s jazykem jen do dokumentů toho jazyka.
- Hodnoty tajného souboru (`.env`) se nenabízejí, dokud jsou skryté. Nabízejí se jen v tom samém souboru a jen když jsou zobrazené.
- Seznam drží omezený počet slov; zúžíte ho psaním do pole. Doplňování přes LSP v této verzi není.

## [edit.sql] SQL: dialekt a formátování
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Úpravy, Formátovat SQL uspořádá výběry, nebo celý soubor, když nic není vybráno: klauzule na samostatném řádku, každá položka výběru na řádku, poddotazy odsazené, klíčová slova jedním typem písma. Je to jeden krok zpět a nic mimo výběr se nezmění.
2. Úpravy, Náhled formátování SQL ukáže stejný výsledek nejdřív v náhledu změn.
3. Dialekt určuje barvy a to, která slova jsou klíčová. Platí první nastavený z těchto: Nástroje, Dialekt SQL (jen tento soubor); `sql_dialect = mysql` v souboru `.editorconfig` (každý soubor SQL v té složce a pod ní); nastavení Výchozí dialekt SQL. Bez nich je dialekt Generic a nic se nehlásí jako chyba.
- Dialekty: generic, mysql, postgres, sqlite, tsql. Editor se k žádné databázi nepřipojuje.
- Řetězce, uvozené názvy a komentáře se opíšou beze změny. Další formátování výsledku nic nezmění.
- Výběr větší než limit pluginu se odmítne se zprávou.
- Odsazení (4 mezery) a velikost klíčových slov (velká) jsou zatím pevné; nastavení to ještě není.

## [edit.html] HTML: entity a HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlDecode, nks.edit.htmlTidy
1. Úpravy, Převést na entity HTML nahradí v označených částech (v celém souboru, když nic není vybráno) znaky & < > " ' entitami. Převést z entit HTML přečte pojmenované, desítkové i šestnáctkové entity zpět. Zakódovaný text převedený zpět dá původní text. Každé je jeden krok zpět.
2. Úpravy, Formátovat pomocí HTML Tidy pošle HTML programu HTML Tidy a výsledek ukáže v náhledu změn. Zprávy Tidy jdou do okna výstupu.
3. Tidy je samostatný program. Editor ho hledá na cestě z nastavení Program HTML Tidy, ve složce `tidy` vedle pluginu formátovačů, ve složce `tools/tidy` aplikace a ve složkách z PATH. Když ho nenajde, zpráva vypíše všechna místa.
- Tidy není součástí tohoto sestavení: nainstalujte ho a nastavte cestu, nebo ho dejte na některé z těch míst.
- Čísla řádků ve zprávách se počítají od začátku textu, který se Tidy poslal.
- Písmena mimo ASCII se jako entity nepíšou; drží je kódování souboru.

## [help.language] Nápověda k jazyku
commands: nks.help.languageHelp
1. Dejte kurzor na slovo, například název funkce, a stiskněte Alt+F1.
2. Editor slovo vyhledá ve zdrojích nápovědy nastavených pro jazyk souboru. Soubor pluginu se otevře v editoru. Webová adresa se nejdřív ukáže a v prohlížeči se otevře až po potvrzení.
3. Když je pro jazyk víc zdrojů, vyberete jeden.
- Zdroje jsou data. Plugin bez programu je vypíše v `languageHelp` v `nkspad-plugin.json`, každý s `id`, `title`, `language` (id jazyka, například `php`), `kind` (`url` nebo `file`) a `target`. Ve webové adrese `${word}` znamená slovo a `${language}` id jazyka. Cíl souboru je cesta uvnitř složky pluginu.
- Přijímají se jen adresy `http` a `https`; plugin s jinou adresou se při načtení odmítne.
- Žádné zdroje zatím nejsou přibalené. Když pro jazyk žádný není, stavový řádek to řekne.

## [table.view] Tabulka pro CSV a TSV
commands: nks.view.table, nks.table.dialect
Soubor CSV nebo TSV lze zobrazit jako tabulku: jeden řádek souboru je jeden řádek tabulky, jedno pole je jedna buňka. Je to týž dokument jako v textovém zobrazení, se stejnou historií Zpět; nic se nepřevádí a úprava buňky změní jen bajty té buňky.
1. Ctrl+K a pak Ctrl+T (nabídka Zobrazení, Přepnout tabulku a text) přepíná mezi tabulkou a textem. Soubor .tsv nebo .tab se dělí podle tabulátorů, ostatní soubory podle čárek.
2. Zobrazení, Oddělovač a záhlaví tabulky vybere čárku, středník, tabulátor nebo svislítko jako oddělovač, zda uvozovky ohraničují hodnoty a zda je první řádek záhlaví.
- Velký soubor se otevře hned: řádky se počítají na pozadí a rozsah posuvníku roste, jak se soubor čte. Než se dojde na konec, stavový řádek ukazuje počet řádků s plusem, například 12+.
- Šipky, Tab, PageUp a PageDown, Home a End (v rámci řádku), Ctrl+Home a Ctrl+End přesouvají aktuální buňku; Shift rozšiřuje výběr. Klepnutí na záhlaví sloupce vybere sloupec, klepnutí na číslo řádku vybere řádek.
- Buňka v uvozovkách může obsahovat zalomení řádku; tabulka ukáže její první řádek a značku ↵.
- Kopírovat dá vybrané buňky do schránky jako text oddělený tabulátory; Vložit zapíše text oddělený tabulátory od aktuální buňky.

## [table.edit] Tabulka: úpravy a filtr
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Enter nebo F2 (nebo prostě psaní) upraví aktuální buňku. Enter uloží a posune se dolů, Shift+Enter vloží zalomení řádku, Esc zruší. Buňka, která byla v uvozovkách, v nich zůstane; jiná hodnota se uvede do uvozovek jen tehdy, když obsahuje oddělovač, uvozovku nebo zalomení řádku.
2. Zobrazení, Vložit řádek přidá prázdný řádek za aktuální. Vložit sloupec se zeptá na název sloupce a přidá ho za aktuální. Smazat řádek a Smazat sloupec odstraní vybrané řádky nebo sloupce. Každá akce je jeden krok Zpět.
3. Zobrazení, Filtrovat řádky ukáže jen řádky, jejichž aktuální sloupec obsahuje zadaný text (na velikosti písmen nezáleží). Zrušit filtr ukáže všechny řádky. Filtr soubor nikdy nemění a čísla řádků zůstávají čísly řádků v souboru.
- Delete vymaže vybrané buňky. Krátký řádek dostane chybějící oddělovače, když se vyplní buňka za jeho koncem.
- Když je znak uvozovek vypnutý v Oddělovač a záhlaví tabulky, hodnota s oddělovačem nebo zalomením řádku se odmítne.
- Dokud je zapnutý filtr, nelze přidat řádek.

## [table.region] Úprava vybraného textu jako tabulky
commands: nks.table.editRegion
Vyberte část textu, například blok řádků oddělených čárkami v poznámce nebo v logu, a zvolte Zobrazení, Upravit výběr jako tabulku.
1. Dialog se zeptá na oddělovač (čárka, středník, tabulátor nebo svislítko), zda uvozovky ohraničují hodnoty a zda je první vybraný řádek záhlaví. Náhled ukáže první řádky jako buňky.
2. Vybraný text se stane tabulkou. Zbytek souboru zůstane textem: úprava buňky změní jen bajty té buňky a nic mimo výběr se nečte ani nemění.
- Funguje v něm vše z tabulky: pohyb, úpravy, přidávání a mazání řádků a sloupců, filtr, kopírování a vkládání. Stavový řádek říká, že tabulkou je jen vybraný text.
- Řádky přidané na konec patří do výběru. Přepnout tabulku a text vrátí text; další Přepnout tabulku a text ukáže jako tabulku celý soubor.

## [compare.text] Porovnání textů
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts
Dva texty se porovnají po řádcích v samostatné kartě. Stejné řádky stojí proti sobě, změněný blok je na obou stranách podbarvený a označený ve střední liště a lišta nahoře říká, kolik je rozdílů.
1. Porovnat se schránkou (menu Nástroje nebo místní nabídka editoru) porovná kartu s textem ve schránce. Je-li v editoru vybraný text, porovná se jen výběr.
2. Porovnat se souborem se zeptá na soubor a porovná s ním kartu. Porovnat s diskem je v menu Soubor a porovná kartu s jejím souborem.
3. Porovnat dva texty otevře dvě prázdné strany. Tlačítka Vložit levý text a Vložit pravý text vloží do strany obsah schránky.
- Strana s kartou je samotný dokument: když se změní, porovná se znovu po krátké pauze. Schránka, výběr a soubor na disku jsou strany jen pro čtení.
- Jiný konec řádku (CR, LF, CRLF) řádek nezmění. Mezery a velikost písmen ano.
- Velký soubor ve stránkovaném režimu se porovnává bez načtení: čtou se jen řádky na obrazovce.
- Každá strana může mít mezi společným začátkem a společným koncem až 16 milionů řádků; víc je jeden změněný blok. Až 10 000 změněných řádků se najde přesně; při větším počtu lišta napíše, že výsledek není minimální, a změněné řádky se sloučí do větších bloků. Výsledek je pořád správný.

## [compare.navigate] Přechod mezi rozdíly a jejich kopírování
commands: nks.compare.next, nks.compare.previous, nks.compare.copyToRight, nks.compare.copyToLeft
1. F7 skočí na další rozdíl a Shift+F7 na předchozí (také v menu Nástroje). Aktuální rozdíl má rámeček.
2. Ve střední liště má první řádek každého rozdílu dvě šipky. Levá zkopíruje blok pravé strany doleva, pravá zkopíruje blok levé strany doprava. Totéž pro aktuální rozdíl dělají tlačítka nad stranami a příkazy Zkopírovat rozdíl doprava a doleva.
3. Kopie je jedna úprava dokumentu, takže Zpět ji vrátí jedním krokem.
- Blok může přijmout jen strana, která je otevřený dokument. Soubor na disku, schránka a výběr šipku nemají.
- Když se dokument po porovnání změnil, nic se nezkopíruje; porovná se znovu a zkopírujete to ještě jednou.
- Když kódování cíle nepojme znaky bloku, nic se nezkopíruje a stavový řádek to napíše.
- Kolečko myši posouvá obě strany společně, Shift a kolečko posouvá do stran.

## [view.syncScroll] Synchronizované posouvání
commands: nks.view.syncScroll
Dvě karty se posouvají spolu, takže stejný text dvou souborů zůstává proti sobě.
1. Zobrazte oba soubory vedle sebe: v nabídce karty zvolte Přesunout do jiné skupiny, aby měl každý soubor svou skupinu.
2. Zvolte Zobrazit a potom Synchronizovat posouvání. Aktivní karta a aktivní karta další skupiny se sledují. Dalším zvolením příkazu to vypnete.
- Řádky se přiřazují porovnáním z tématu Porovnání textů: druhý soubor se posune na odpovídající řádek, i přes vložené nebo odebrané bloky. Než se porovnání dopočítá, platí stejné číslo řádku.
- Skončí, když se zavře jedna ze dvou karet. Hexadecimální a tabulkový pohled se neúčastní.

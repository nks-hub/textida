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
Příkaz spustí váš vlastní program na porovnání: vlevo soubor na disku, vpravo obsah karty.
1. Jednou v nastavení vyplňte Externí porovnávač (cestu k programu) a případně Argumenty porovnávače. V šabloně znamená {left} soubor na disku a {right} obsah karty.
2. Zvolte Soubor a Porovnat s diskem.
- Bez nastaveného programu se ukáže lišta Není nastaven externí porovnávač s tlačítkem Otevřít nastavení.
- Karta bez souboru na disku napíše Karta nemá soubor na disku, není s čím porovnávat.
- Nespustí-li se program, stavový řádek napíše Porovnávač se nespustil a důvod.
- Vlastní porovnávač v programu není; používá se ten, který nastavíte.

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
commands: nks.edit.selectAll, nks.edit.selectLine
1. Ctrl+A vybere celý text.
2. Ctrl+L vybere řádek včetně konce řádku. Opakované Ctrl+L přidá další řádek.
- S více kurzory vybere každý kurzor svůj řádek.

## [edit.lines] Práce s řádky
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines
Příkazy se týkají řádku s kurzorem, případně všech řádků výběru. Každý je jeden krok Zpět.
- Smazat řádek: Ctrl+Shift+K.
- Duplikovat řádek: Ctrl+Shift+D vloží kopii pod řádek.
- Přesunout řádek nahoru nebo dolů: Alt+šipka nahoru, Alt+šipka dolů.
- Spojit řádky: Ctrl+Shift+J spojí řádek s následujícím.
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
- Běží-li výraz příliš dlouho, zastaví se a okno to napíše. Vypíše se nejvýš omezený počet shod.
- F1 v tomto okně otevře toto téma.

## [search.gotoLine] Přejít na řádek
commands: nks.search.gotoLine
1. Stiskněte Ctrl+G, nebo zvolte Hledat a Přejít na řádek.
2. Napište číslo řádku (120), řádek a sloupec (120:5), procento (40 %) nebo bajt (@4096).
3. Potvrďte Enter. Kurzor skočí na místo.
- Číslo větší než počet řádků skočí na poslední řádek.
- Neplatný zápis napíše „...“ není číslo řádku.
- Ve velkém souboru, jehož řádky se ještě číslují, ukáže stavový řádek: Na tuto pozici ještě nevede index řádků, zkuste to za chvíli.

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
- Pod seznamem je náhled zvýrazněného souboru; binární soubor se nezobrazí.

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
Soubor větší než hranice velkého souboru (nastavení Hranice velkého souboru, výchozí 6 MiB) se otevře ve stránkovaném režimu. Program čte jen řádky na obrazovce, takže soubor se otevře rychle a nezabere paměť.
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
- Výraz je příliš pomalý a hledání ho zastavilo: zjednodušte výraz, zejména vnořená opakování.
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
- Hexadecimální zobrazení a úpravy: v této verzi není. Soubor, který není text, program otevře jako text.
- Porovnání souborů vlastním oknem: používá se externí porovnávač (téma Porovnat s diskem).
- Úpravy velmi velkých souborů: jen omezené, viz téma Velké soubory.
- Rozšíření (pluginy), vzdálené soubory a další funkce z plánu verze 1.0: v této verzi nejsou.

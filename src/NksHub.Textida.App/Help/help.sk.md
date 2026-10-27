## [start] Začíname
commands: nks.help.contents
Tento pomocník je priamo v programe a nepotrebuje internet. Otvoríte ho klávesom F1 alebo z ponuky Pomocník. Keď stlačíte F1 pri vybranom príkaze v palete príkazov alebo v otvorenom paneli (hľadanie, súbory, dokumenty, nastavenia, skúška regulárneho výrazu), otvorí sa rovno jeho téma.
### Ako v pomocníkovi hľadať
- Vľavo je zoznam tém. Do poľa nad ním napíšte slovo; zostanú len témy, ktoré obsahujú všetky napísané slová. Diakritika a veľkosť písmen nehrajú rolu.
- Šípky nahor a nadol v poli prepínajú témy, Esc pomocníka zavrie.
### Skratky
- Vo všetkých postupoch je Ctrl; na macOS sa namiesto neho používa ⌘ (Cmd). Uvedené skratky sú predvolené. Vlastné skratky nájdete v nastaveniach a v profile správania PSPad sa niekoľko líši (téma Profil správania).
- Každý príkaz sa dá nájsť v palete príkazov: Ctrl+Shift+P a napísať časť názvu.
### Čo pomocník pokrýva
- Opisuje len funkcie, ktoré táto verzia naozaj má. Čo táto verzia nemá, sa týka témy Čo táto verzia nevie.
- Osobitné témy: Obnova po páde, Veľké súbory (stránkovaný režim), Chybové hlásenia.

## [view.palette] Paleta príkazov
commands: nks.view.commandPalette
Paleta je zoznam všetkých príkazov programu s hľadaním.
1. Stlačte Ctrl+Shift+P.
2. Napíšte časť názvu príkazu, napríklad uloz. Diakritika sa nemusí písať.
3. Šípkami vyberte riadok a stlačte Enter. Esc paletu zavrie.
- Pri príkaze sa zobrazí skratka. Príkaz, ktorý sa teraz nedá použiť (napríklad Uložiť bez otvoreného dokumentu), je stlmený a má pri sebe dôvod.
- Ak napíšete na začiatok /, paleta hľadá v nastaveniach a Enter otvorí nastavenia na nájdenej položke.
- Ak napíšete na začiatok =, paleta počíta: =1+2*3 ukáže 7 a Enter výsledok skopíruje. Neplatný výraz, delenie nulou alebo príliš veľké číslo paleta ohlási vedľa výrazu.
- F1 na vybranom príkaze otvorí jeho tému pomocníka.

## [help.contents] Pomocník
commands: nks.help.contents
Otvorí toto okno pomocníka.
1. Stlačte F1, alebo zvoľte Pomocník a Pomocník.
2. Okno sa otvorí na téme podľa toho, kde sa práve nachádzate: pri vybranom príkaze v palete na jeho téme, v hľadaní, v paneli súborov alebo v zozname dokumentov na téme toho panelu, inak na úvode.
3. F1 v nastaveniach otvorí tému o nastaveniach, F1 v skúške regulárneho výrazu tému o skúške.
- Pomocník je jedno okno. Ďalšie F1 ho len prepne na inú tému.

## [help.about] O aplikácii
commands: nks.help.about
Ukáže názov programu a číslo verzie.
1. Zvoľte Pomocník a O aplikácii.
2. Okno zavriete tlačidlom Zavrieť alebo klávesom Esc.
- Rovnaké číslo verzie je uvedené hore v každej téme pomocníka.

## [file.new] Nový dokument
commands: nks.file.new
1. Stlačte Ctrl+N, alebo zvoľte Súbor a Nový.
2. Otvorí sa karta Bez názvu 1 (ďalšie dostanú čísla 2, 3 a tak ďalej) a kurzor je v nej.
- Kódovanie nového dokumentu určuje nastavenie Predvolené kódovanie, konce riadkov nastavenie Konce riadkov.
- Text sa hneď zapisuje do žurnálu obnovy, takže sa nestratí ani pri páde (téma Obnova po páde). Názov a miesto dostane dokument pri prvom uložení.

## [file.open] Otvorenie súboru
commands: nks.file.open
1. Stlačte Ctrl+O, alebo zvoľte Súbor a Otvoriť.
2. V systémovom okne vyberte jeden alebo viac súborov a potvrďte.
3. Každý súbor sa otvorí na vlastnej karte. Súbor, ktorý je už otvorený, sa len zobrazí.
- Súbory sa dajú aj pretiahnuť do okna programu.
- Kódovanie sa pozná samo. Ak je to len odhad, ukáže to stavový riadok s percentom istoty; kliknutím na kódovanie ho zmeníte (téma Kódovanie).
- Súbor väčší ako hranica veľkého súboru (predvolene 6 MiB) sa otvorí v stránkovanom režime s obmedzenou editáciou (téma Veľké súbory).
- Súbor, ktorý sa nedá prečítať, sa neotvorí a v stavovom riadku je dôvod (téma Chybové hlásenia).

## [file.openFolder] Otvorenie priečinka
commands: nks.file.openFolder
1. Stlačte Ctrl+K a potom Ctrl+O, alebo zvoľte Súbor a Otvoriť priečinok.
2. V systémovom okne vyberte priečinok.
3. Vľavo sa ukáže panel Súbory so stromom priečinka. Hľadanie súborov podľa názvu (Ctrl+P) a Hľadať v súboroch potom pracujú nad týmto priečinkom.
- Ak sa priečinok nedá otvoriť, stavový riadok napíše dôvod.

## [file.save] Uloženie
commands: nks.file.save, nks.file.saveAll
1. Ctrl+S uloží aktívnu kartu, Ctrl+Alt+S uloží všetky karty s neuloženými zmenami.
2. Dokument bez súboru sa pri uložení opýta na názov a kódovanie rovnako ako Uložiť ako.
3. Po uložení sa v stavovom riadku objaví Uložené.
- Pred prepísaním súboru program vyhotoví zálohu pôvodnej verzie. Ak sa záloha nepodarí, súbor zostane bez zmeny a stavový riadok to napíše. Ak sa nepodarí len záloha po zápise, napíše sa Uložené, ale záloha sa nepodarila.
- Ak sa súbor medzitým na disku zmenil alebo zmizol, program sa opýta: Prepísať súbor, Porovnať, alebo Zrušiť. Nič sa neprepíše bez vášho súhlasu (téma Zmena súboru zvonku).
- Ak text obsahuje znaky, ktoré kódovanie súboru nevie, program ich vypíše a ponúkne Nahradiť a uložiť (znaky sa nahradia otáznikom) alebo Zrušiť.
- Nastavenia Odstrániť medzery na koncoch riadkov, Vložiť koniec riadka na koniec súboru a Automatické ukladanie sa týkajú ukladania.

## [file.saveAs] Uložiť ako
commands: nks.file.saveAs
1. Stlačte Ctrl+Shift+S, alebo zvoľte Súbor a Uložiť ako.
2. Do poľa Súbor napíšte cestu alebo ju vyberte tlačidlom Prehľadávať.
3. V poli Kódovanie vyberte kódovanie. Pri kódovaní, ktoré by niektoré znaky stratilo, je napísané stratové a počet znakov.
4. Potvrďte tlačidlom Uložiť.
- Ak cieľový súbor existuje, program sa opýta na prepísanie.
- Karta potom patrí novému súboru.
- Veľký súbor v stránkovanom režime sa dá uložiť len vo vlastnom kódovaní.

## [file.close] Zavretie karty
commands: nks.file.close
1. Stlačte Ctrl+W, alebo zvoľte Súbor a Zavrieť.
2. Ak má dokument neuložené zmeny, program sa opýta: Uložiť, Neukladať, Nechať v žurnále alebo Zrušiť. Nechať v žurnále sa ponúka, len keď obnova po páde funguje; zmeny potom čakajú v žurnále a pri ďalšom spustení sa vrátia.
- Ak sú otvorené dva pohľady toho istého dokumentu (rozdelené zobrazenie), zavretie jedného sa na nič nepýta.
- V profile správania PSPad je Ctrl+W prepínanie zalamovania riadkov; kartu potom zavriete z ponuky alebo krížikom na karte.

## [file.reload] Znova načítať z disku
commands: nks.file.reload
1. Zvoľte Súbor a Znova načítať z disku, alebo príkaz nájdite v palete.
2. Ak má karta neuložené zmeny, program sa opýta; tlačidlo Zahodiť zmeny a načítať ich zahodí.
3. V stavovom riadku sa objaví Znova načítané z disku.
- Príkaz funguje len pri karte, ktorá má súbor na disku.
- To isté ponúka lišta nad dokumentom, keď sa súbor zmení zvonku (téma Zmena súboru zvonku).

## [file.compare] Porovnať s diskom
commands: nks.file.compareWithDisk
Príkaz spustí váš vlastný program na porovnanie: vľavo súbor na disku, vpravo obsah karty.
1. Raz v nastaveniach vyplňte Externý porovnávač (cestu k programu) a prípadne Argumenty porovnávača. V šablóne znamená {left} súbor na disku a {right} obsah karty.
2. Zvoľte Súbor a Porovnať s diskom.
- Bez nastaveného programu sa ukáže lišta Nie je nastavený externý porovnávač s tlačidlom Otvoriť nastavenia.
- Karta bez súboru na disku napíše Karta nemá súbor na disku, nie je s čím porovnávať.
- Ak sa program nespustí, stavový riadok napíše Porovnávač sa nespustil a dôvod.
- Vlastný porovnávač v programe nie je; používa sa ten, ktorý nastavíte.

## [file.openPath] Otvoriť cestu pod kurzorom
commands: nks.file.openPathAtCaret
1. Dajte kurzor na cestu k súboru v texte, napríklad src/a.cs:12.
2. Zvoľte Súbor a Otvoriť cestu pod kurzorom, alebo príkaz nájdite v palete. Skratku nemá.
3. Súbor sa otvorí; riadok (a prípadne stĺpec) uvedený za dvojbodkou sa použije.
- Relatívna cesta sa hľadá v priečinku aktuálneho súboru a v otvorenom priečinku. Cesta začínajúca ~/ mieri do vášho domovského priečinka.
- Ak cesta mieri na priečinok, ukáže sa v paneli Súbory.
- Pod kurzorom nie je cesta: Pod kurzorom nie je cesta k súboru. Súbor neexistuje: napíše sa cesta a že neexistuje.

## [app.settings] Nastavenia
commands: nks.app.openSettings
1. Stlačte Ctrl+čiarka, alebo zvoľte Nastavenia a Nastavenia.
2. Do poľa hore napíšte, čo hľadáte, napríklad tab alebo skratka. Hľadá sa v nastaveniach aj v klávesových skratkách.
3. Pri každej položke je vidieť platná hodnota a odkiaľ pochádza (predvolená, používateľ, pracovný priestor, EditorConfig, dokument).
4. Hodnotu zmeňte priamo v riadku; prejaví sa hneď a uloží sa do súboru nastavení. Tlačidlo Obnoviť predvolené vráti pôvodnú hodnotu.
- Nezmyselná hodnota sa odmietne a riadok napíše, čo s ňou je. Ak sa súbor nastavení nedá zapísať, nad zoznamom je dôvod.
- Konflikty skratiek sú vidieť pri príslušných riadkoch.
- F1 v tomto okne otvorí túto tému.

## [app.quit] Koniec programu
commands: nks.app.quit
1. Stlačte Ctrl+Q, alebo zvoľte Súbor a Koniec.
2. Ak obnova po páde funguje, program sa na nič nepýta: neuložený text zostane v žurnále a po ďalšom spustení sa vráti.
3. Ak nefunguje (žurnál sa nedá zapisovať), program sa pri každom dokumente s neuloženými zmenami opýta: Uložiť, Neukladať, Zrušiť.
- Rozloženie kariet sa pri konci uloží a pri ďalšom spustení vráti.

## [edit.undo] Späť a Znova
commands: nks.edit.undo, nks.edit.redo
1. Ctrl+Z vráti posledný krok, Ctrl+Y alebo Ctrl+Shift+Z ho vráti späť. Ponuka Úpravy má to isté.
2. Krok je celý súvislý kus písania alebo jeden príkaz (napríklad Odstrániť riadok alebo prevod kódovania).
3. Po kroku sa kurzor vráti na miesto, kde zmena prebehla.
- Dva pohľady toho istého dokumentu majú spoločnú históriu.
- V stránkovanom režime Späť a Znova fungujú tiež.
- V profile PSPad sú aj Alt+Backspace (Späť) a Shift+Alt+Backspace (Znova).

## [edit.clipboard] Vystrihnúť, Kopírovať, Vložiť
commands: nks.edit.cut, nks.edit.copy, nks.edit.paste
1. Vyberte text; Ctrl+X ho vystrihne, Ctrl+C skopíruje, Ctrl+V vloží na miesto kurzora alebo cez výber.
- Vložený text dostane konce riadkov podľa dokumentu.
- Pri viacerých kurzoroch sa vybrané texty skopírujú po jednom na riadok a pri vložení dostane každý kurzor svoj kus.
- V blokovom výbere sa vkladá po jednom riadku schránky na každý riadok bloku.
- Vloženie do reťazca v programovacom jazyku escapuje úvodzovky a spätné lomky (nastavenie Escapovať pri vložení do reťazca).
- Vystrihnúť a Vložiť nejdú pri dokumente len na čítanie a v stránkovanom režime (téma Veľké súbory).

## [edit.select] Výber
commands: nks.edit.selectAll, nks.edit.selectLine, nks.edit.expandSelection
1. Ctrl+A vyberie celý text.
2. Ctrl+L vyberie riadok vrátane konca riadka. Opakované Ctrl+L pridá ďalší riadok.
3. Shift+Alt+šípka doprava rozšíri výber: najprv slovo, potom obsah zátvoriek okolo neho, potom aj zátvorky, ďalšie zátvorky, celé riadky a nakoniec celý text.
- Pri viacerých kurzoroch vyberie každý kurzor svoj riadok.

## [edit.lines] Práca s riadkami
commands: nks.edit.deleteLine, nks.edit.duplicateLine, nks.edit.moveLineUp, nks.edit.moveLineDown, nks.edit.joinLines, nks.edit.toggleComment
Príkazy sa týkajú riadka s kurzorom, prípadne všetkých riadkov výberu. Každý je jeden krok Späť.
- Odstrániť riadok: Ctrl+Shift+K.
- Duplikovať riadok: Ctrl+Shift+D vloží kópiu pod riadok.
- Presunúť riadok nahor alebo nadol: Alt+šípka nahor, Alt+šípka nadol.
- Spojiť riadky: Ctrl+Shift+J spojí riadok s nasledujúcim.
- Zakomentovať / odkomentovať: Ctrl+/ riadky zakomentuje, alebo komentár zase odstráni. Použije značky jazyka na danom riadku, takže skript vnútri HTML dostane // a HTML okolo neho komentár HTML.
- Všetky sú aj v ponuke Úpravy.
- Nejdú pri dokumente len na čítanie ani v stránkovanom režime.
- V profile PSPad je Odstrániť riadok na Ctrl+Y, Duplikovať riadok na Shift+Alt+šípka nadol a Alt+šípky riadky nepresúvajú.

## [edit.indent] Odsadenie
commands: nks.edit.indent, nks.edit.outdent, nks.edit.insertIndent, nks.edit.tab
1. Ctrl+] zväčší odsadenie vybraných riadkov (alebo riadka s kurzorom), Ctrl+[ ho zmenší.
2. Kláves Tab pri výbere cez viac riadkov odsadí všetky, bez takého výberu vloží odsadenie na miesto kurzora. Shift+Tab odsadenie zmenší.
- Odsadenie je toľko medzier alebo tabulátorov, koľko hovoria nastavenia Veľkosť odsadenia a Odsadzovať medzerami. Pri otvorenom súbore program odsadenie odhadne (nastavenie Zistiť odsadenie) a súbor s .editorconfig sa riadi ním. Odsadenie zmeníte kliknutím naň v stavovom riadku.
- Príkaz Vložiť odsadenie skratku nemá; je to to, čo robí Tab bez výberu cez viac riadkov.
- V profile PSPad sú Zväčšiť a Zmenšiť odsadenie na Ctrl+Shift+I a Ctrl+Shift+U.

## [edit.newline] Nový riadok
commands: nks.edit.newline
1. Enter založí nový riadok s odsadením predchádzajúceho, vo formáte konca riadka dokumentu.
2. Ak je kurzor v komentári, nový riadok komentár založí znova (nastavenie Pokračovať v komentári).

## [edit.case] Veľkosť písmen
commands: nks.edit.upperCase, nks.edit.lowerCase
1. Vyberte text.
2. Ctrl+K a potom Ctrl+U zmení písmená na veľké, Ctrl+K a potom Ctrl+L na malé.
- Bez výberu sú príkazy stlmené. Sú aj v ponuke Úpravy.

## [edit.clipboardHistory] História schránky
commands: nks.edit.clipboardHistory, nks.edit.pasteKeepCaret
1. Stlačte Ctrl+Shift+V (ponuka Úpravy, História schránky). Zoznam ukáže naposledy skopírované alebo vystrihnuté texty, najnovší prvý, s náhľadom označenej položky.
2. Písaním zoznam zúžite, šípkami vyberiete text a stlačíte Enter.
- Text sa vloží ako pri Ctrl+V: pri viacerých kurzoroch do každého kurzora (jeden riadok na kurzor, keď sa počet riadkov rovná počtu kurzorov), ako jeden krok späť. Funguje aj v stránkovanom režime.
- História drží toľko textov, koľko hovorí nastavenie Veľkosť histórie schránky. Je len v pamäti a po zatvorení programu sa zabudne. Text dlhší ako 1 048 576 znakov sa neukladá.
- Vložiť bez posunu kurzora (ponuka Úpravy) vloží text zo schránky a kurzor nechá tam, kde bol, pred novým textom. V blokovom výbere sa kurzor vráti do ľavého horného rohu.
- Vloženie nefunguje v dokumente len na čítanie; Vložiť bez posunu kurzora nefunguje ani v stránkovanom režime (téma Veľké súbory).

## [edit.insertLines] Vloženie do riadkov
commands: nks.edit.insertIntoLines
Príkaz zapíše text, čísla alebo odrážky do riadkov výberu, alebo do všetkých riadkov, keď je len kurzor.
1. Zvoľte Úpravy, Vložiť do riadkov. Náhľad ukáže prvé riadky, ako budú vyzerať.
2. Zvoľte Text, Čísla alebo Odrážky a kam písať: začiatok každého riadka, koniec každého riadka, pred stĺpec, alebo do každého kurzora.
3. Stlačte OK. Výsledok je jeden krok späť.
- Čísla: Prvé číslo, Počet číslic (číslo sa zľava doplní nulami na túto šírku) a Oddeľovač, ktorý leží medzi číslom a textom riadka (na začiatku riadka za číslom, na konci pred číslom). Do každého kurzora sa zapíše len číslo, takže tri kurzory dostanú 1, 2 a 3.
- Začiatok každého riadka píše na začiatok riadka; Preskočiť odsadenie píše pred prvý viditeľný znak. Pred stĺpec počíta znaky od 1 a kratší riadok dostane text na koniec. Preskočiť prázdne riadky preskočí aj riadky len z medzier a tie nespotrebujú číslo.
- Konce riadkov dokumentu sa nemenia. Nefunguje v dokumente len na čítanie ani v stránkovanom režime (téma Veľké súbory).
- Opakovať posledný príkaz ho spustí znova s rovnakou voľbou. V profile PSPad je to Alt+I.

## [edit.sort] Zoradenie riadkov
commands: nks.edit.sortLines
1. Vyberte riadky (alebo dajte kurzor kamkoľvek, aby sa zoradili všetky) a zvoľte Úpravy, Zoradiť riadky.
2. Zvoľte vzostupne alebo zostupne, či záleží na veľkosti písmen, či odstrániť duplicitné riadky a podľa čoho porovnávať. Náhľad ukáže prvé riadky výsledku.
3. Stlačte OK. Výsledok je jeden krok späť a zoradené riadky zostanú vybrané.
- Kód znakov radí podľa kódu znakov. Jazykové pravidlá použijú pravidlá jazyka zadaného značkou, napríklad cs-CZ, alebo šestnástkovým číslom, napríklad 0405 (prázdne znamená bez jazyka). Číslo na začiatku číta číslo, ktorým riadok začína; riadky bez čísla sú prvé.
- Od stĺpca a Po stĺpec porovnajú len tú časť každého riadka (0 znamená celý riadok).
- Odstránenie duplicít porovnáva celé riadky a nechá prvý z nich.
- Viac výberov sa radí každý zvlášť. Konce riadkov sa nemenia. Nefunguje v dokumente len na čítanie ani v stránkovanom režime.

## [search.selectLines] Výber riadkov podľa rozsahu
commands: nks.search.selectLinesByRange
1. Zvoľte Hľadať, Vybrať riadky podľa rozsahu.
2. Napíšte čísla riadkov a rozsahy, napríklad 10-20, 40-50, oddelené čiarkami, bodkočiarkami alebo medzerami. Riadok pod poľom hovorí, koľko výberov vznikne.
3. Stlačte OK. Každý rozsah sa stane vlastným výberom s vlastným kurzorom a Kopírovať potom skopíruje riadky všetkých rozsahov.
- Rozsahy, ktoré sa prekrývajú alebo na seba nadväzujú, sa spoja. Čísla riadkov začínajú od 1; riadky za koncom dokumentu sa ignorujú.
- Opakovať posledný príkaz vyberie rovnaké riadky znova.
- Nefunguje v stránkovanom režime.

## [format.block] Zarovnanie a zalomenie riadkov
commands: nks.edit.reformatParagraph, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
Tieto príkazy sú v ponuke Formát. Pôsobia na vybrané riadky, alebo na všetky riadky, keď je len kurzor (Preformátovať odsek: na odsek s kurzorom). Každý je jeden krok späť.
- Preformátovať odsek zalomí text na stĺpci z nastavenia Stĺpec pravého okraja, spojí riadky odseku a odstráni nadbytočné medzery. Odseky oddeľuje prázdny riadok a každý riadok odseku dostane odsadenie prvého riadka. Keď nie je pravý okraj nastavený, povie to stavový riadok.
- Zarovnať riadky vľavo odstráni medzery na začiatku a na konci riadkov.
- Zarovnať riadky vpravo a Vycentrovať riadky zarovnajú na šírku najdlhšieho riadka.
- Zarovnať riadky do bloku rozšíri medzery medzi slovami, kým nie je každý riadok taký široký ako najdlhší; riadok s jedným slovom zostane.
- V profile PSPad je Zarovnať riadky vľavo na Ctrl+Alt+šípka vľavo a Zarovnať riadky vpravo na Ctrl+Alt+šípka vpravo.
- Nefungujú v dokumente len na čítanie ani v stránkovanom režime.

## [edit.insertSmall] Dátum, GUID a opakovanie
commands: nks.edit.insertDateTime, nks.edit.insertGuid, nks.edit.repeatLast
- Vložiť dátum a čas zapíše aktuálny dátum a čas do každého kurzora vo formáte z nastavenia Formát dátumu a času (vlastný formát .NET, napríklad yyyy-MM-dd HH:mm; názvy mesiacov a dní podľa jazyka rozhrania). Výber sa nahradí. V profile PSPad je to Alt+D.
- Vložiť GUID zapíše nový GUID do každého kurzora, každý iný: malými písmenami, s pomlčkami, bez zložených zátvoriek.
- Opakovať posledný príkaz spustí znova posledný príkaz, ktorý menil text: Odstrániť riadok, Duplikovať riadok, Presunúť riadok, Spojiť riadky, Zväčšiť odsadenie, Zmenšiť odsadenie, zmena veľkosti písmen, Zakomentovať / odkomentovať, Vložiť do riadkov, Zoradiť riadky, Preformátovať odsek a príkazy zarovnania, Vložiť dátum a čas, Vložiť GUID. Príkaz s dialógom sa zopakuje s voľbou z posledného spustenia, bez pýtania. V profile PSPad je to Alt+Q.
- Nefungujú v dokumente len na čítanie ani v stránkovanom režime.

## [edit.json] Formátovanie JSON
commands: nks.edit.formatJson, nks.edit.previewFormatJson
1. Označte text JSON, alebo neoznačujte nič a naformátuje sa celý dokument.
2. Ponuka Úpravy, Formátovať JSON, naformátuje označené časti (každý výber) alebo celý dokument. Všetky časti sú jeden krok Späť; text mimo nich zostane bajt po bajte rovnaký, vrátane koncov riadkov.
3. Náhľad formátovania JSON ukáže zmenu najprv ako návrh. Použite ho z lišty nad editorom, alebo ho zahoďte.
- Chyba syntaxe sa ohlási s riadkom a stĺpcom a nič sa nezmení. Text väčší ako limit sa odmietne so správou.
- Ak sa dokument po spustení príkazu zmenil, príkaz sa zastaví a vyzve vás na opätovné spustenie.

## [tools.stats] Štatistika dokumentu a hashe
commands: nks.tools.statistics, nks.tools.hash
1. Ponuka Nástroje, Štatistika dokumentu, spočíta bajty, znaky, slová, riadky a najdlhší riadok celého dokumentu.
2. Ponuka Nástroje, Vypočítať hash, sa opýta na algoritmus (MD5, SHA-1, SHA-256, SHA-384 alebo SHA-512) a vypočíta hash surových bajtov celého súboru alebo označenej časti.
- Oba príkazy čítajú dokument po blokoch a vy môžete ďalej pracovať, ukazujú priebeh a dajú sa zrušiť. Žiadny nedrží celý súbor v pamäti, takže fungujú na súboroch ľubovoľnej veľkosti, aj v stránkovanom a hex zobrazení.
- Zrušená štatistika sa ukáže ako neúplná s počtom spočítaných bajtov. Zrušený hash výsledok nemá.
- Štatistika aj hash platia pre dokument, aký bol pri spustení príkazu; úpravy počas výpočtu ich nemenia.
- Slovo je súvislý rad písmen a číslic. Hash nemá výsledok pre viac označených častí naraz.

## [tools.calculate] Výpočet bloku, výrazy a číselné sústavy
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.convertBase
1. Vypočítať označený blok sčíta čísla v označení (blokové označenie, stĺpec čísel) a ukáže počet, súčet, najmenšie, najväčšie a priemer.
2. Vyhodnotiť výraz sa opýta na výraz, predvyplní označený text a ukáže výsledok. Rozumie + - * / ^ ( ), MOD a funkciám ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR a TRUNC. Čísla sa píšu s bodkou. Čokoľvek iné, napríklad volanie príkazu systému, sa odmietne a nič sa nespustí.
3. Previesť číselnú sústavu sa opýta na celé číslo, desiatkovo alebo s predponou 0x, 0b či 0o, a ukáže ho desiatkovo, šestnástkovo, osmičkovo a dvojkovo.
- Program pozná, ako sú čísla v označení zapísané (čeština a slovenčina: 1 234,50, nemčina: 1.234,50, angličtina: 1,234.50). Keď vyhovuje viac zápisov, napríklad 1,234, vezme ten z jazyka programu. Výsledky sa ukážu vo formáte jazyka programu. Zmena jazyka dokument nezmení.
- Text v označení, ktorý nie je číslo, sa preskočí a spočíta. Prevod čísel funguje pre hodnoty od -9223372036854775808 do 18446744073709551615.
- Mocnina berie len kladný základ. Výpočet používa čísla s dvojitou presnosťou.

## [tools.colors] Farby
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. V súboroch CSS, HTML, XML, JavaScript, TypeScript, PHP, Razor a Latte má každá hodnota farby (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) pred sebou malú vzorku svojej farby. Vzorka nezaberá žiadny znak dokumentu.
2. Kliknite na vzorku, alebo dajte kurzor na hodnotu a zvoľte Nástroje, Vybrať farbu pri kurzore. Otvorí sa výber: posuvníky pre odtieň, sýtosť, svetlosť a krytie a pole s hodnotou, ktoré prijme aj farbu v inom zápise. Kliknutie mimo výberu zapíše farbu cez hodnotu v zápise, aký mala, ako jeden krok späť; Esc výber zavrie bez zmeny.
3. Nástroje, Previesť farbu na HEX, RGB, HSL alebo oklch prepíše hodnotu farby pri kurzore, alebo všetky v označení, do zvoleného zápisu. Farba zostane rovnaká osembitová farba; oklch sa píše na päť desatinných miest pri svetlosti a sýtosti a na dve pri odtieni.
- Farba mimo rozsahu sRGB sa do neho orezá. Krytie sa zachová, ako `/ alfa` alebo ako posledné dve hexadecimálne číslice.
- Text, ktorý len vyzerá ako farba, napríklad `rgb(var(--c))`, entita HTML `&#123;` alebo `#12345`, sa nechá byť.
- Súbor väčší než limit pluginu sa neprevádza; správa to povie.

## [tools.keys] Cesty kľúčov v YAML a JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. V súbore YAML alebo JSON ukazuje stavový riadok cestu kľúča pod kurzorom, napríklad `cs.app.title`. Položky zoznamu sa píšu `items[0]` a kľúč s bodkou alebo zátvorkou sa píše `["a.b"]`. Cesta sa číta pre revíziu a kurzor, pre ktoré sa pýtalo: keď medzitým píšete alebo kurzor presuniete, odpoveď sa zahodí.
2. Nástroje, Kopírovať cestu kľúča dá cestu do schránky.
3. Nástroje, Prejsť na kľúč (alebo `@` v Prejsť na čokoľvek) vypíše kľúče súboru podľa cesty; Enter označí kľúč tak, ako je napísaný. Zobraziť osnovu kľúčov vypíše tie isté kľúče v paneli pluginu a kliknutie tam označí rovnaké bajty.
4. Nástroje, Porovnať kľúče s iným súborom sa opýta na ďalší otvorený súbor YAML alebo JSON a do okna výstupu vypíše kľúče s hodnotou, ktoré každému súboru chýbajú.
- YAML v blokovom zápise sa číta: vnorené kľúče, zoznamy, kľúče v úvodzovkách a blokové skaláry. Hodnota v zložených alebo hranatých zátvorkách sa počíta za jednu hodnotu. JSON smie mať komentáre a čiarku na konci.
- Chyba syntaxe zastaví čítanie na tom mieste; kľúče pred ňou zostanú a správa uvedie riadok.

## [tools.outline] Osnova kódu a symboly
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. V súboroch HTML, PHP, CSS, C# a Markdown ukáže Nástroje, Zobraziť osnovu kódu funkcie, triedy, nadpisy a pravidlá súboru ako strom v paneli pluginu. Kliknutie označí meno symbolu.
2. Nástroje, Prejsť na symbol (alebo `@` v Prejsť na čokoľvek) vypíše tie isté symboly podľa mena; Enter označí rovnaké bajty ako kliknutie v osnove.
3. Osnova patrí ku konkrétnej revízii dokumentu. Po úprave sa sama obnoví; kým príde nová, zostane stará označená ako zastaralá a skok sa presunie cez vykonané úpravy, alebo sa odmietne, keď sa zmenil text, na ktorom symbol stál. Odpoveď pre staršiu revíziu sa nikdy nezobrazí ako aktuálna.
4. Keď sa proces pluginu zastaví, osnova sa po jeho obnovení načíta znova.
- Číta sa podľa jednoduchých pravidiel jazyka, bez jazykového servera a bez indexu projektu: HTML (nadpisy, prvky s `id`, `main`, `nav`, `section` a podobné), PHP (menné priestory, triedy, rozhrania, traity, výčty, funkcie, metódy), CSS (pravidlá a @-pravidlá), C# (menné priestory, typy, metódy, konštruktory, vlastnosti), Markdown (nadpisy `#`).
- Komentáre a reťazce sa nečítajú. Čo pravidlá nepoznajú, sa preskočí.
- Súbor väčší než limit pluginu alebo s viac symbolmi, než osnova unesie, sa zobrazí skrátený a správa to povie.

## [edit.carets] Viac kurzorov
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Vyberte slovo; Ctrl+D pridá ďalší jeho výskyt do výberu. Opakujte pre ďalšie výskyty.
2. Ctrl+K a potom Ctrl+D preskočí posledný výskyt a vezme ďalší.
3. Ctrl+Shift+L vyberie všetky výskyty naraz.
4. Ctrl+Alt+šípka nahor a Ctrl+Alt+šípka nadol pridajú kurzor na riadok nad a pod.
5. Písaním sa menia všetky miesta naraz. Esc nechá jediný kurzor.
- Stavový riadok ukazuje počet kurzorov.
- Alt+ťahanie myšou vytvorí kurzor na každom riadku alebo blokový výber podľa nastavenia Alt+ťahanie.

## [edit.mark] Značka a výber po značku
commands: nks.edit.selectToMark
1. Nájdite príkaz Nastaviť značku / vybrať po značku v ponuke Úpravy.
2. Prvé použitie uloží značku na miesto kurzora.
3. Presuňte kurzor inam a príkaz použite znova: vyberie sa text od značky ku kurzoru a značka zmizne.
- Kláves F8 má tento príkaz len v profile správania PSPad; v predvolenom profile je F8 blokový výber.

## [edit.block] Blokový výber
commands: nks.edit.toggleBlockSelect
1. Stlačte F8 (v profile PSPad Ctrl+Shift+C).
2. Šípkami, Home alebo Shift so šípkami rozšírte obdĺžnik cez riadky a stĺpce.
3. Písaním alebo vložením sa text zmení na každom riadku bloku. Vloženie využije riadky schránky po jednom.
4. Ďalším stlačením F8 blok zrušíte.
- Blok sa dá vytvoriť aj ťahaním myši s Alt, keď to nastavenie Alt+ťahanie dovolí.
- V stránkovanom režime blokový výber nie je.

## [search.find] Hľadanie v dokumente
commands: nks.search.find, nks.search.findNext, nks.search.findPrevious, nks.search.findWord
1. Ctrl+F otvorí lištu hľadania dole. Napíšte text; počet zhôd sa ukáže vedľa poľa a zhody sa označia.
2. Enter nájde ďalší výskyt, Shift+Enter predchádzajúci. Rovnako fungujú F3 a Shift+F3.
3. Ctrl+F3 hľadá slovo pod kurzorom.
4. Esc lištu zavrie.
- Prepínače: Aa rozlišuje veľkosť písmen (Alt+C), .* je regulárny výraz (Alt+R), ab celé slová (Alt+W), Výber hľadá len vo vybranom texte.
- Keď hľadanie dôjde na koniec, pokračuje od začiatku a stavový riadok to ohlási.
- Neplatný výraz je vidieť pri počte zhôd. Príliš pomalý výraz hľadanie zastaví.
- Hľadanie funguje aj v stránkovanom režime; nahrádzanie tam nie.
- Keď zapnete regulárny výraz, objaví sa tlačidlo Skúsiť (téma Skúška regulárneho výrazu).

## [search.replace] Nahradenie
commands: nks.search.replace
1. Stlačte Ctrl+H, alebo zvoľte Hľadať a Nahradiť. Lišta má navyše pole Nahradiť čím.
2. Napíšte hľadaný a náhradný text.
3. Enter v poli náhrady nahradí jednu zhodu, Ctrl+Alt+Enter všetky. To isté robia tlačidlá Nahradiť a Všetko.
4. Stavový riadok napíše, koľko zhôd sa nahradilo, alebo Nič sa nenahradilo.
- V regulárnej náhrade platí $1, $& a zmena veľkosti písmen \U \L \E \u \l.
- Nejde pri dokumente len na čítanie ani v stránkovanom režime.

## [search.inFiles] Hľadanie a nahrádzanie v súboroch
commands: nks.search.findInFiles, nks.search.replaceInFiles
1. Ctrl+Shift+F otvorí lištu s poľom Priečinok; Ctrl+Shift+H pridá pole náhrady.
2. Vyplňte hľadaný text a priečinok (tlačidlo Prehľadávať ho vyberie) a stlačte Enter alebo Hľadať.
3. Výsledky sa otvoria v karte Výsledky hľadania: súbory, riadky a počty. Enter alebo dvojklik otvorí zhodu.
4. Pri nahrádzaní tlačidlo Náhľad ukáže navrhnuté zmeny. Odškrtnutím vyradíte súbor alebo jednu zmenu, Použiť vybrané zmeny ich zapíše.
- Hľadanie sa dá zrušiť tlačidlom Zrušiť alebo Esc. Pri príliš mnohých zhodách sa zastaví a napíše, koľko ich ukázalo.
- Pri nahrádzaní zostanú súbory s konfliktom bez zmeny: dokument sa po náhľade zmenil, súbor zmenil iný program, súbor zmizol, nový text sa nedá zapísať v kódovaní súboru alebo zmena už nesedí. Nový náhľad vytvorí náhľad znova.
- Pred zápisom do súborov program zapíše plán do žurnálu; po páde uprostred dávky ponúkne Dokončiť alebo Vrátiť späť.
- F1 v karte výsledkov otvorí túto tému.

## [search.regex] Skúška regulárneho výrazu
commands: nks.search.regexTester
1. Zvoľte Hľadať a Skúška regulárneho výrazu, alebo v lište hľadania s regulárnym výrazom stlačte Skúsiť.
2. Napíšte výraz, prípadne náhradu a vzorový text.
3. Okno ukáže zhody so skupinami, text po náhrade a po častiach vysvetlenie výrazu.
- Používa sa rovnaký stroj ako pri hľadaní (regulárne výrazy .NET). Syntax iných dialektov vysvetlenie označí ako nepodporovanú.
- Výraz so spätným odkazom, lookaroundom alebo atómovou skupinou beží v pomalšom klasickom engine a po limite z nastavenia Časový limit regulárneho výrazu (predvolene 2000 ms) sa zastaví; ostatné výrazy bežia v lineárnom čase bez limitu.
- Ak výraz beží príliš dlho, zastaví sa a okno to napíše. Vypíše sa najviac obmedzený počet zhôd.
- F1 v tomto okne otvorí túto tému.

## [search.gotoLine] Prejsť na riadok
commands: nks.search.gotoLine
1. Stlačte Ctrl+G, alebo zvoľte Hľadať a Prejsť na riadok.
2. Napíšte číslo riadka (120), riadok a stĺpec (120:5), percento (40 %) alebo bajt (@4096).
3. Potvrďte Enter. Kurzor skočí na miesto.
- Číslo väčšie ako počet riadkov skočí na posledný riadok.
- Neplatný zápis napíše „...“ nie je číslo riadka.
- Vo veľkom súbore, ktorého riadky sa ešte číslujú, ukáže stavový riadok: Index riadkov na túto pozíciu ešte nesiaha, skúste to o chvíľu. Bajtový offset (@4096) alebo percentá fungujú hneď; kým číslovanie nedobehne, je číslo riadka po takom skoku označené ≈.

## [search.gotoBracket] Prejsť na párovú zátvorku
commands: nks.search.gotoBracket
1. Dajte kurzor k zátvorke.
2. Stlačte Ctrl+Shift+spätná lomka, alebo zvoľte Hľadať a Prejsť na párovú zátvorku.
3. Kurzor skočí na protiľahlú zátvorku.
- Ak zátvorka nemá protějšok, stavový riadok napíše Zátvorka nemá protějšok.
- Vo veľkom súbore v stránkovanom režime príkaz nie je.

## [search.gotoAnything] Prejsť na čokoľvek
commands: nks.search.gotoAnything
1. Stlačte Ctrl+P.
2. Napíšte časť názvu alebo cesty súboru. Zoznam ukazuje súbory otvoreného priečinka, otvorené dokumenty a nedávne súbory.
3. Enter súbor otvorí. Ak napíšete :120, kurzor skočí na riadok 120 aktívneho dokumentu.
- Bez otvoreného priečinka sa vypíšu len otvorené a nedávne súbory.
- Súbor, ktorý už neexistuje, je označený ako chýba.
- Pod zoznamom je náhľad zvýrazneného súboru; binárny súbor sa nezobrazí. Hodnoty súboru `.env` sa v náhľade ukazujú ako `••••`.
- V súbore YAML alebo JSON vypíše `@` s časťou cesty kľúča všetky kľúče súboru (téma Cesty kľúčov v YAML a JSON).

## [search.jump] Miesta úprav a skoky
commands: nks.search.recentEdits, nks.search.jumpBack, nks.search.jumpForward
1. Ctrl+K a potom Ctrl+E otvorí zoznam miest, kde ste naposledy upravovali text, najnovšie prvé. Zaškrtnutím sa dá obmedziť na upravené dokumenty. Enter na miesto skočí.
2. Alt+šípka doľava sa vráti na miesto, odkiaľ ste skočili (po Prejsť na riadok, otvorení cesty, súvisiaceho súboru a podobne). Alt+šípka doprava ide znova dopredu.
- Ak niet kam skočiť, stavový riadok napíše Niet kam skočiť späť alebo dopredu. Zoznam bez úprav napíše Zatiaľ žiadne miesta úprav.
- Miesta sa posúvajú spolu s textom a prežijú zavretie dokumentu.

## [search.related] Súvisiaci súbor
commands: nks.search.relatedFile
1. V uloženom dokumente stlačte Alt+O, alebo zvoľte Hľadať a Prejsť na súvisiaci súbor.
2. Ak sa nájde jediný, otvorí sa. Ak ich je viac, vyberte zo zoznamu.
- Vstavané dvojice: .c a .h či .hpp, .cpp a .hpp či .h, .m a .h, súbor a jeho _test (Go, Python), Test.php, Tests.cs, .spec.ts a .test.ts, .xaml a .xaml.cs (rovnako axaml, razor).
- Vlastné dvojice zapíšete v nastavení Pravidlá súvisiacich súborov v tvare koncovka|koncovka, oddelené bodkočiarkami.
- Dokument bez súboru napíše: Dokument najprv uložte. Bez zhody: Pre názov nie je žiadny súvisiaci súbor.

## [search.bookmarks] Záložky
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
1. Ctrl+F2 nastaví alebo zruší záložku na riadku s kurzorom; vľavo od čísel riadkov sa zobrazí bodka.
2. F2 skočí na ďalšiu záložku, Shift+F2 na predchádzajúcu; za poslednou sa pokračuje od prvej.
- Hľadať, Zmazať záložky odstráni všetky záložky dokumentu.
- Záložka sa posúva s textom, keď nad ňou pribudnú alebo ubudnú riadky, a ukladá sa do relácie: po reštarte je späť.

## [view.wordwrap] Zalamovanie riadkov
commands: nks.view.toggleWordWrap
1. Stlačte Alt+Z, alebo zvoľte Zobraziť a Prepnúť zalamovanie riadkov.
2. Dlhé riadky sa zalomia na okraji okna. Ďalším stlačením sa vrátia.
- Voľba sa uloží do nastavenia Zalamovanie riadkov.
- V stránkovanom režime príkaz nie je.
- V profile PSPad je skratka Ctrl+W.

## [view.zoom] Veľkosť písma
commands: nks.view.zoomIn, nks.view.zoomOut, nks.view.zoomReset
1. Ctrl+= zväčší písmo o jeden bod, Ctrl+- zmenší.
2. Ctrl+0 vráti predvolenú veľkosť.
- Veľkosť je v rozsahu 6 až 72 bodov a ukladá sa do nastavenia Veľkosť písma.

## [view.display] Skryté znaky, čísla riadkov a minimapa
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Zvoľte Zobraziť a Zobraziť skryté znaky (Ctrl+K, Ctrl+W): vykreslia sa medzery, tabulátory a konce riadkov CR, LF a CRLF. Dokument sa nemení.
2. Zobraziť čísla riadkov (Ctrl+K, Ctrl+N), Zvýrazniť aktuálny riadok, Zobraziť pravý okraj (zvislá čiara v stĺpci 80, nastavuje sa v nastavení Stĺpec pravého okraja) a Klikacie odkazy zapínajú a vypínajú zvyšok.
3. Zobraziť minimapu (Ctrl+K, Ctrl+M) ukáže vpravo zmenšený obraz textu vo farbách zvýraznenia. Kliknutím alebo ťahaním sa posúva. Pri veľkom súbore ukazuje len riadky okolo obrazovky.
- Zhody z lišty hľadania sú označené v úzkom pruhu na pravom okraji minimapy.
- Odkazy sa otvoria kliknutím s Ctrl a v stránkovanom režime nie sú k dispozícii.
- Riadok dlhší ako 50 000 znakov sa nikdy nezalomí: Prepnúť zalamovanie to povie v stavovom riadku. Stránkovaný režim nezalamuje nikdy.
- Znaky, ktoré hlavné písmo nemá, sa vykreslia písmami z nastavenia Náhradné písma.
- Ctrl a koliesko myši menia veľkosť písma ako príkazy zväčšenia.

## [view.secrets] Skryté hodnoty v súboroch .env
commands: nks.view.toggleSecrets
1. Súbory s názvom `.env`, `.env.*` alebo `*.env` ukazujú každú hodnotu premennej ako `••••`. Zobraziť a Zobraziť skryté hodnoty ich ukáže v aktuálnej karte, ďalšie použitie ich zase skryje.
- Keď na hodnotu prejdete ukazovateľom myši, ukáže sa len tá jedna.
- Text súboru sa nemení: uloženie zapíše rovnaké bajty.
- Skryté sú aj v minimape, v náhľadoch Prejsť na čokoľvek a Miesta úprav a vo výsledkoch hľadania. Kopírovanie dá do schránky `••••` namiesto skrytej hodnoty a Vystrihnúť skrytú hodnotu nezmaže.
- Plugin číta namiesto každej skrytej hodnoty hviezdičky `*`. Skôr než sa na takom súbore spustí vstavaný príkaz, program sa opýta, či smie hodnoty prečítať.
- Stránkovaný režim a hexadecimálne zobrazenie ukazujú bajty tak, ako sú.

## [view.tabs] Prepínanie kariet
commands: nks.view.nextTab, nks.view.previousTab
1. Ctrl+Tab prepne na ďalšiu kartu skupiny, Ctrl+Shift+Tab na predchádzajúcu.
2. Po poslednej karte sa pokračuje prvou.
- Karty sa dajú aj pretiahnuť myšou a zoradiť.

## [view.documents] Zoznam otvorených dokumentov
commands: nks.view.toggleDocumentList
1. Ctrl+B otvorí alebo zavrie panel Otvorené dokumenty vľavo.
2. Šípkami vyberte dokument, Enter ho zobrazí. Neuložené dokumenty majú značku ●.
3. Pole hore zoznam filtruje.
- Esc panel zavrie.

## [view.files] Panel Súbory
commands: nks.view.toggleFilePanel
1. Ctrl+Shift+E otvorí alebo zavrie panel Súbory. Bez otvoreného priečinka ponúka Otvoriť priečinok.
2. Šípky prechádzajú strom, šípka doprava a doľava rozbalí a zbalí priečinok, Enter alebo dvojklik súbor otvorí.
3. Pravé tlačidlo ponúka operácie so súbormi (téma Operácie so súbormi). F2 premenuje, Delete zmaže do koša, F5 obnoví.
- Strom sleduje zmeny na disku sám.

## [files.create] Nový súbor a nový priečinok
commands: nks.files.newFile, nks.files.newFolder
1. Otvorte priečinok a vyberte v paneli Súbory priečinok, do ktorého sa má vytvoriť.
2. Pravým tlačidlom zvoľte Nový súbor v priečinku alebo Nový priečinok, alebo príkaz nájdite v palete.
3. Napíšte názov a potvrďte.
- V stavovom riadku sa objaví Vytvorené a názov.
- Neplatný alebo existujúci názov sa odmietne (téma Chybové hlásenia) a nič sa nezmení.

## [files.manage] Duplikovať, premenovať, presunúť, zmazať
commands: nks.files.duplicate, nks.files.rename, nks.files.move, nks.files.delete
Vyberte súbor alebo priečinok v paneli Súbory. Operácie sú v kontextovej ponuke a v palete.
- Duplikovať vytvorí kópiu vedľa pôvodného.
- Premenovať (F2): napíšte nový názov. Otvorené karty a zoznamy miest sa presunú s ním.
- Presunúť: napíšte cieľový priečinok.
- Zmazať do koša (Delete): program sa opýta a súbor ide do koša, odkiaľ ho vrátite. Ak je otvorený s neuloženými zmenami, karta zostane otvorená a zmeny si ponechá.
- Ak operácia zlyhá, nad dokumentom je lišta s dôvodom a Nič sa nezmenilo.

## [files.refresh] Obnoviť a ukázať súbor
commands: nks.files.refresh, nks.files.reveal
- Obnoviť súbory (F5 v paneli) znova načíta strom podľa disku.
- Ukázať aktívny súbor v paneli otvorí panel a vyberie v ňom súbor aktívnej karty. Funguje pri otvorenom priečinku a otvorenom dokumente.

## [format.encoding] Kódovanie
commands: nks.format.reread, nks.format.convert
Stavový riadok ukazuje kódovanie; kliknutím naň sa otvorí rovnaký výber. Ak je kódovanie odhad, je pri ňom percento istoty.
- Znova prečítať ako: súbor sa znova prečíta z disku v inom kódovaní. Použite, keď sa po otvorení ukážu zlé písmená.
- Previesť na: dokument sa prevedie do iného kódovania. Ktoré znaky by sa stratili, ukáže zoznam (stratové a počet) a okno s riadkom, stĺpcom a bajtom každého znaku. Nahradiť a previesť ich nahradí otáznikom.
- Predvolený zoznam kódovaní v nastaveniach: UTF-8 (s BOM aj bez), UTF-16 LE a BE, Windows-1250, Windows-1252, ISO-8859-2.
- Pri veľkom súbore v stránkovanom režime sú obe voľby nedostupné.

## [format.eol] Konce riadkov
commands: nks.format.eolCrLf, nks.format.eolLf, nks.format.eolCr
1. Zvoľte Formát a Konce riadkov na CRLF, LF alebo CR, alebo kliknite na konce riadkov v stavovom riadku (pri zmiešaných je napísané zmiešané).
2. Ukáže sa náhľad zmeny: koľko koncov riadkov sa zmení. Použiť zmenu vykoná.
- Ak nie je čo meniť, stavový riadok napíše Nie je čo meniť.
- Dá sa to vrátiť klávesom Späť.
- Pri dokumente len na čítanie a v stránkovanom režime príkaz nie je.

## [format.normalize] Normalizácia Unicode
commands: nks.format.normalizeNfc, nks.format.normalizeNfd
Znaky s diakritikou môžu byť zapísané jedným znakom (NFC) alebo písmenom a samostatnou diakritikou (NFD). Vzhľad je rovnaký, ale hľadanie a porovnanie ich rozlišuje.
1. Zvoľte Formát a Normalizovať Unicode na NFC (alebo NFD).
2. Ukáže sa náhľad s počtom miest. Použiť zmenu vykoná.
- Ak by sa po normalizácii niektorý znak nedal zapísať v kódovaní súboru, program zmenu odmietne a napíše to.
- Ak nebolo čo meniť, stavový riadok napíše Nie je čo meniť.

## [format.language] Režim jazyka
commands: nks.format.language
1. Zvoľte Formát a Režim jazyka, alebo kliknite na jazyk v stavovom riadku.
2. Vyberte jazyk, napríklad Obyčajný text.
- Jazyk určuje nastavenia platné len pre tento jazyk (napríklad zalamovanie riadkov alebo odsadenie) a pravidlá zvýrazňovania.
- Pri otvorení sa jazyk určí podľa prípony súboru.
- Vlastné zvýrazňovače sú dáta: contributes.grammars v nkspad-plugin.json uvádza súbory gramatík (JSON, formatVersion 1, popísané v grammar.schema.json): id, name, extensions, fileNames, comments, keywords a rules. Pravidlo je match (regulárny výraz), words, operators, include, alebo oblasť s begin a end, ktorá môže vnoriť iný jazyk.
- Zvýrazňovač môže zahrnúť a vnoriť vstavané jazyky, napríklad "embed": "javascript". Súbor, ktorý končí jednou z jeho prípon alebo má jeden z jeho fileNames, sa otvorí v tomto jazyku; id zvýrazňovača je jazyk v nastaveniach jazyka.
- Zvýrazňovač so zlým regulárnym výrazom, neznámou vlastnosťou, neznámym include alebo embed, alebo s id vstavaného jazyka sa pri načítaní pluginu odmietne. Plugin sa preskočí a stavový riadok povie, ktorý súbor a ktoré pravidlo.
- V príliš dlhom riadku nechá zvýrazňovač z pluginu riadok bez farieb.

## [window.new] Nové okno
commands: nks.window.new
1. Stlačte Ctrl+Shift+N, alebo zvoľte Okno a Nové okno.
2. Otvorí sa druhé okno programu. Dokumenty a karty má každé okno vlastné.
- Pri zavretí okna zostane neuložená práca v žurnále a vráti sa pri ďalšom spustení.

## [window.split] Rozdelené zobrazenie
commands: nks.window.splitView, nks.window.moveToOtherGroup
1. Ctrl+\ ukáže druhý pohľad toho istého dokumentu v skupine vpravo. Oba pohľady majú spoločný text a históriu Späť.
2. Presunúť kartu do inej skupiny ju presunie do druhej skupiny (príkaz je v ponuke Okno).
3. Hranicu skupín sa dá ťahať myšou.
- Karty sa dajú preťahovať aj medzi skupinami.
- Skupina sa zruší, keď v nej nezostane karta.

## [settings.theme] Motív
commands: nks.settings.theme.system, nks.settings.theme.light, nks.settings.theme.dark
1. Zvoľte Nastavenia a jeden z motívov: Motív podľa systému, Svetlý motív, Tmavý motív.
2. Zmena sa prejaví hneď a zapamätá sa.
- Podľa systému znamená, že sa program riadi nastavením systému.

## [settings.language] Jazyk rozhrania
commands: nks.settings.language.cs, nks.settings.language.en, nks.settings.language.sk, nks.settings.language.de
1. Zvoľte Nastavenia a Čeština, English, Slovenčina alebo Deutsch.
2. Ponuky, hlásenia aj tento pomocník sa prepnú hneď.
- Predvolená voľba je podľa systému (nastavenie Jazyk rozhrania).

## [settings.profile] Profil správania
commands: nks.settings.profile.default, nks.settings.profile.pspad
1. Zvoľte Nastavenia a Profil správania: predvolený, alebo Profil správania: PSPad.
- Profil PSPad mení: Home a End preskakujú medzi prvým znakom a krajom riadka, Backspace v odsadení maže po tabulačnú zarážku, Alt+ťahanie robí blokový výber, párové znaky sa dopĺňajú podľa PSPadu a niekoľko skratiek (Odstrániť riadok Ctrl+Y, Späť Alt+Backspace, Zalamovanie Ctrl+W, F8 značka, Ctrl+Shift+C blokový výber, Odsadenie Ctrl+Shift+I a U).
- Vlastné skratky v nastaveniach majú prednosť pred profilom.

## [view.output] Okno výstupu
commands: nks.view.toggleOutput
Okno výstupu je pruh pod editorom, do ktorého zapisujú úlohy a hľadania. Predvolene je skryté.
1. Stlačte Ctrl+Shift+Y, alebo zvoľte Zobraziť a Výstup.
2. Záložky Úlohy a Hľadanie prepínajú kanál. Súhrn hľadania v súboroch sa zapíše aj do kanála Hľadanie.
3. Tlačidlo Vymazať kanál vyprázdni, Zavrieť (alebo Esc) okno schová.
- Text sa dá vybrať a skopírovať. Prázdny kanál napíše Do tohto výstupu sa zatiaľ nič nezapísalo.
- Výstup sa číta zo súborov v profile, takže po páde programu, ktorý ho písal, zostane viditeľný.

## [view.fullscreen] Celá obrazovka
commands: nks.view.fullScreen
1. Stlačte F11, alebo zvoľte Zobraziť a Celá obrazovka.
2. Zmizne rám okna; zostane ponuka, karty, editor a stavový riadok. Písanie ide ďalej do editora.
3. Ďalšie F11 vráti predchádzajúci stav, aj maximalizované okno.
- Na macOS používa F11 systém; použite ponuku Zobraziť.

## [hex.open] Hex: otvorenie a pohyb
commands: nks.view.hex, nks.file.openAsHex
Hex zobrazuje súbor po bajtoch: offset, bajty šestnástkovo a znaky. Číta len riadky na obrazovke, takže veľkosť súboru nevadí.
1. Ctrl+K a potom Ctrl+H prepne aktívny dokument medzi textom a hexom (ponuka Zobraziť, Prepnúť hex a text). Je to ten istý dokument s rovnakou históriou Späť; výber sa prevedie cez bajty.
2. Súbor a Otvoriť v hex otvorí vybraný súbor rovno v hexe. Z príkazového riadka to robí prepínač --hex, prípadne s --offset.
3. Súbor, ktorý vyzerá binárne (nulový bajt v prvých 8 000 bajtoch), ponúkne lišta nad dokumentom: Otvoriť v hex, alebo Nechať ako text.
- Šípky, PageUp a PageDown, Home a End (na riadku), Ctrl+Home a Ctrl+End (celý súbor). Shift rozširuje výber. Tab prepína medzi stĺpcom bajtov a znakov.
- Vystrihnúť, Kopírovať a Vložiť pracujú s bajtmi; medzi dvoma hexmi sa prenesú presne.

## [hex.find] Hex: hľadanie a skok na offset
- Ctrl+F otvorí okno Hľadať bajty. Zadajte bajty šestnástkovo (50 4B ?? 04, ?? je ľubovoľný bajt), text v úvodzovkách, alebo číslo s typom (0x1F40 i32).
- F3 a Shift+F3 nájdu ďalšiu a predchádzajúcu zhodu. Stavový riadok napíše Zhoda 1 z 4, alebo Aspoň N zhôd, keď hľadanie došlo na limit. Nahrádzanie bajtov nie je.
- Ctrl+G otvorí Prejsť na offset. Zápis: 0x1F40, +0x100 (od kurzora), end-512, 50%, 4K, sel (začiatok výberu), val (štvorbajtová hodnota pod kurzorom).
- Nenájdené: zhoda nie je. Neplatný vzor alebo Neplatný výraz: opravte zápis. Offset leží mimo súboru: zadajte menšiu hodnotu. Súbor sa medzitým zmenil na disku, výsledok hľadania sa zahodil: hľadanie zopakujte.

## [hex.edit] Hex: úpravy
commands: nks.hex.toggleInsert
1. Kláves Insert (na macOS ⌥⌘I) prepína medzi PREPIS a VKLADANIE; stav je vidieť v stavovom riadku.
2. V prepise sa bajty prepisujú a dĺžka súboru sa nemení. Na konci súboru program napíše, že prepis nemení dĺžku súboru, a nech zapnete vkladanie.
3. Delete a Backspace v prepise sa opýtajú: Vynulovať (dĺžka zostane), alebo Zmazať (súbor sa skráti).
4. Ak vložíte viac bajtov, než zostáva do konca súboru, program sa opýta: Predĺžiť súbor, alebo Vložiť len to, čo sa zmestí.
- Znak v stĺpci znakov, ktorý kódovanie nezapíše, program odmietne: Znak nejde zapísať v kódovaní.
- Späť a Znova fungujú. Zmenené bajty sú podfarbené a podčiarknuté.
- Keď sa súbor práve ukladá, úpravy počkajú na koniec ukladania.

## [hex.inspector] Hex: inšpektor dát
commands: nks.hex.inspector
1. Ctrl+K a potom Ctrl+I otvorí alebo zavrie panel Inšpektor dát vpravo (len v hexe).
2. Ukáže hodnotu od kurzora ako rôzne typy: čísla, dátum, GUID, znak. Prepínač LE a BE volí poradie bajtov.
3. Hodnotu prepíšete v riadku: Enter ju zapíše, Esc zruší.
- Hodnota na konci súboru, ktorej chýbajú bajty, je neúplná a nedá sa prepísať. Neplatný zápis a hodnota mimo rozsahu typu sa ohlásia (napríklad Mimo rozsahu typu, Neplatný GUID, Dátum a čas v tvare RRRR-MM-DD HH:MM:SS).
- Hodnota, ktorá zaberie iný počet bajtov, než koľko prepisuje, vyžaduje režim vkladania.

## [hex.layout] Hex: vzhľad mriežky
commands: nks.hex.charset, nks.hex.layout, nks.hex.bytesPerRow8, nks.hex.bytesPerRow16, nks.hex.bytesPerRow24, nks.hex.bytesPerRow32, nks.hex.bytesPerRow48, nks.hex.bytesPerRow64, nks.hex.offsetFormat
Príkazy sú dostupné v hexe, v ponuke Zobraziť, v stavovom riadku a v palete.
- Kódovanie stĺpca znakov: výber kódovania, ktorým sa bajty ukazujú ako znaky.
- Rozloženie: bajtov na riadok (8, 16, 24, 32, 48, 64), zoskupenie po bajtoch a formát offsetov. Každá veľkosť má aj vlastný príkaz.
- Prepnúť offsety desiatkovo a hexadecimálne.
- Voľby si ďalšie hex zobrazenie pamätá.

## [hex.save] Hex: uloženie a zmeny pred uložením
commands: nks.hex.inspectChanges
1. Ctrl+S uloží hex. Ukladá sa na pozadí a okno ostáva použiteľné; Esc uloženie zruší skôr, než sa zapíšu dáta (Ukladanie zrušené, súbor zostal bez zmeny).
2. Súbor a Zmeny pred uložením ukáže, čo uloženie zapíše: zmenené úseky so starými a novými bajtmi a spôsob zápisu.
- Ak sa menia len hodnoty bajtov, uloží sa na mieste (Uložené na mieste, zmenených N B) a pôvodné bajty sa najprv zapíšu do žurnálu, aby sa uloženie po páde dalo dokončiť alebo vrátiť.
- Ak sa mení dĺžka, uloží sa nový súbor, ktorý sa atomicky vymení.
- Pred prepísaním menšieho súboru vznikne záloha; ak sa nepodarí, súbor zostane bez zmeny.

## [hex.copyAs] Hex: kopírovanie, vkladanie a export ako
commands: nks.hex.copyAs, nks.hex.pasteAs, nks.hex.exportSelection
1. Úpravy a Hex: Kopírovať výber ako dá vybrané bajty do schránky ako text: hex reťazec, pole pre C, C#, Javu, Python, Rust, Go, JavaScript alebo Pascal, Base64, Intel HEX, Motorola S-record alebo hexdump.
2. Hex: Vložiť ako prečíta schránku ako hex text, Base64 alebo pole zo zdrojového kódu a vloží bajty rovnako ako Ctrl+V.
3. Hex: Exportovať výber zapíše výber do súboru, binárne alebo v jednej z textových podôb. Beží na pozadí a Esc ho zruší.
- Intel HEX, S-record a hexdump nesú ako adresu offset každého bajtu v súbore; Intel HEX a S-record majú 32-bitové adresy, takže výber za 4 GiB sa odmietne.
- Výber, ktorého text by sa do schránky nezmestil, sa odmietne s radou ho exportovať.
- Pole vložené zo zdrojového kódu môže obsahovať čísla desiatkovo, hex v tvare 0x aj $ a binárne v tvare 0b; číslo mimo -128 až 255 vloženie zastaví.

## [hex.fill] Hex: vyplnenie a operácie nad výberom
commands: nks.hex.fill, nks.hex.operate
1. Hex: Vyplniť výber vyplní výber vzorom z hex bajtov alebo textu, zopakuje výber za sebou, zapíše náhodné bajty alebo čísla s krokom. Bez výberu vyplní vzor s * a počtom toľko bajtov pri kurzore (v režime vkladania ich vloží).
2. Hex: Operácie nad výberom vykonajú XOR, AND, OR, NOT, pripočítanie, posun alebo rotáciu o 1 až 7 bitov (pri každom bajte) alebo prehodenie poradia bajtov po 2, 4 alebo 8. Operandy sa opakujú po celom výbere.
3. Dialóg pred potvrdením ukáže, koľko bajtov sa zmení. Každé vyplnenie aj operácia je jeden krok späť a vyplnené bajty zostanú vybrané.
- Vyplnenie väčšie než 256 MiB sa drží ako vzor a dĺžka, nie ako bajty, takže ani 5 GiB nestojí pamäť ani čas; uloženie ho zapíše ako nový súbor. Vrátenie takého vyplnenia aj krokov pred ním trvá do uloženia cez súbor.
- Náhodné bajty, čísla s krokom a operácie držia výber v pamäti a odmietnu viac než 2 GiB.

## [hex.checksum] Hex: kontrolné súčty a hashe
commands: nks.hex.checksum
1. Hex: Kontrolné súčty a hashe počíta nad výberom, alebo nad celým súborom, keď nie je nič vybrané: CRC-16, CRC-32, CRC-64 vo viacerých variantoch, CRC s vlastným polynómom, Adler-32, Sum-8/16/32, xxHash32, xxHash64, MD5, SHA-1, SHA-256, SHA-384 a SHA-512.
2. Výpočet beží na pozadí a postup ukazuje stavový riadok; Esc ho zruší.
3. Výsledok sa dá skopírovať alebo porovnať s vloženou hodnotou; na medzerách, dvojbodkách, 0x a veľkosti písmen nezáleží.
- Každý variant CRC má pevný polynóm a parametre (šírka, začiatočná hodnota, zrkadlenie vstupu a výsledku, koncové xor) podľa katalógu algoritmov CRC.
- CRC s vlastným polynómom vezme šírku v bitoch a hex polynóm (nepárny), začiatočnú hodnotu, dva príznaky zrkadlenia (0 alebo 1) a koncové xor.

## [tools.run] Nástroje: spustenie externého programu
commands: nks.tools.run, nks.tools.stop
1. Nástroje, Spustiť nástroj ponúkne nástroje zapísané v súbore tools.json v priečinku profilu. Posledný riadok ten súbor otvorí; prvýkrát do neho zapíše prázdny, ktorý doplníte.
2. Nástroj má id, title, executable a args (zoznam: jedna položka je jeden argument). Voliteľné sú cwd (adresár, kde pobeží; bez neho priečinok aktuálneho súboru), timeoutSeconds (bez neho nie je žiadny časový limit) a logPattern (pozri Vzor LOGu). Príklad: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. V executable, args a cwd zastupujú premenné ${File} (úplná cesta), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} a ${Column} aktuálny dokument; $$ je znak dolára. Argument zostane jedným argumentom, nech hodnota obsahuje čokoľvek, aj medzery a úvodzovky: žiadny shell sa nepoužíva. Neznáma premenná alebo ${File} pri dokumente bez súboru zastaví spustenie skôr, než sa čokoľvek začne.
4. Nástroj, ktorého príkaz a adresár ešte nebežali, sa ukáže s oboma a spustí sa až po vašom potvrdení. Potvrdenie platí, kým sa nezmení definícia alebo program, na ktorý vedie.
5. Výstup ide riadok po riadku na kartu Úlohy v okne výstupu, nakoniec s kódom ukončenia a časom. Tlačidlo Zastaviť v tom okne alebo príkaz Zastaviť bežiaci nástroj ukončí program aj všetko, čo spustil; to isté urobí časový limit.
- Dávkové súbory (.bat, .cmd) sa vo Windows odmietnu, pretože by príkazový interpret argumenty znova rozdelil a rozvinul. Uveďte program, ktorý dávka spúšťa.
- Názov programu bez cesty sa hľadá len v PATH, nikdy v pracovnom adresári.
- Výstup sa číta ako UTF-8. Beží vždy len jeden nástroj.

## [tools.logPattern] Vzor LOGu: kliknutie na chybu vo výstupe
commands: nks.tools.logPattern
1. Vzor LOGu pre výstup otvorí skúšku vzoru s posledným výstupom nástrojov ako ukážkou. Každý riadok ukážky ukáže súbor, riadok a stĺpec, ktoré v ňom vzor našiel, alebo že nenašiel nič.
2. Maska sa porovnáva zľava a bez ohľadu na veľkosť písmen. * preskočí ľubovoľný text až po ďalšiu pevnú časť masky. %F je názov súboru, až po ďalšiu pevnú časť. %D je disk (C: nasledované lomkou) pred ním. %L je riadok a %C stĺpec (číslice). %S preskočí medzery a tabulátory. %E ukončí masku. Maska potrebuje %L; bez %F mieri na súbor, ktorý bol otvorený. Príklady: %F:%L:%C:* pre main.c:12:5: error, *] %F(%L) pre [Error] a.pas(346): text.
3. Druhý jazyk je regulárny výraz so skupinami line (povinná), file a column.
4. Použiť pre výstup zariadi, že vzor platí pre riadky v okne výstupu: kliknutie na riadok alebo Enter otvorí súbor na tom riadku. Relatívny názov súboru sa hľadá v adresári, kde nástroj bežal, potom v priečinku súboru, ktorý bol otvorený pri spustení, potom medzi otvorenými dokumentmi podľa názvu.
- Nástroj môže mať vzor vlastný: v tools.json "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] a v nástroji "logPattern": "gcc". Bez "syntax" je vzor regulárny výraz.

## [log.mode] Režim logu: úrovne a ďalšia chyba
commands: nks.log.nextError, nks.log.previousError, nks.log.levels
1. Dokument v jazyku Log (súbory .log, alebo Formát, Jazyk) zafarbí každý riadok podľa jeho úrovne: chyba, varovanie, informácia alebo ladenie. Rozhoduje najzávažnejšia úroveň nájdená v riadku.
2. Log: ďalšia chyba a Log: predchádzajúca chyba skočia od kurzora na ďalší alebo predchádzajúci riadok úrovne chyba. Súbor sa číta po blokoch a hľadanie skončí pri prvom náleze, takže funguje v súbore akejkoľvek veľkosti; stavový riadok ukazuje priebeh a Esc hľadanie zruší.
3. Log: úrovne vyberie pravidlá pre dokument. Predvolené pravidlá hľadajú veľkými písmenami napísané slová ERROR, ERR, FATAL, CRITICAL, SEVERE, WARN, WARNING, INFO, DEBUG a TRACE ako celé slová. Vlastné pravidlá prídu z tools.json: "logPatterns": [{"id": "mine", "title": "Môj log", "levels": {"error": "\\|E\\|", "warning": "\\|W\\|"}}]. Kľúče sú error, warning, info a debug; každá hodnota je regulárny výraz. Príkaz tiež zapne režim logu pre dokument.
- Pravidlá platia, kým je dokument otvorený, a neukladajú sa.
- Riadok dlhší ako 8192 znakov sa v editore posudzuje podľa prvých 8192 znakov.

## [view.options] Zobrazenie editora
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
Príkazy sú v ponuke Zobraziť a v palete. Každý prepína jedno nastavenie; dajú sa zmeniť aj v nastaveniach.
- Zobraziť skryté znaky (Ctrl+K a potom Ctrl+W): vykreslí medzery a tabulátory. Zodpovedá nastaveniu Zobraziť biele znaky.
- Zobraziť čísla riadkov (Ctrl+K a potom Ctrl+N): stĺpec s číslami riadkov vľavo (nastavenie Čísla riadkov).
- Zvýrazniť aktuálny riadok: podfarbí riadok s kurzorom (nastavenie Zvýrazniť aktuálny riadok).
- Zobraziť pravý okraj: zvislá čiara za stĺpcom z nastavenia Stĺpec pravého okraja; hodnota 0 čiaru vypína.
- Klikateľné odkazy: webová adresa sa otvorí kliknutím so stlačeným Ctrl (nastavenie Klikateľné odkazy). Vo veľkom súbore v stránkovanom režime príkaz nie je.
- Zobraziť minimapu (Ctrl+K a potom Ctrl+M): zmenšený obraz textu vpravo, ťahaním sa posúva (nastavenie Minimapa).
- Príkazy sú stlmené bez otvoreného dokumentu.

## [bookmarks] Záložky
commands: nks.bookmarks.toggle, nks.bookmarks.next, nks.bookmarks.previous, nks.bookmarks.clear
Záložka označí riadok, na ktorý sa chcete vrátiť.
1. Ctrl+F2 nastaví záložku na riadku s kurzorom, na riadku, ktorý ju už má, ju odstráni. Stavový riadok napíše Záložka nastavená na riadku N, alebo Záložka na riadku N odstránená.
2. F2 skočí na ďalšiu záložku, Shift+F2 na predchádzajúcu. Po poslednej sa pokračuje od prvej.
3. Zmazať záložky (v palete) odstráni všetky záložky dokumentu; stavový riadok napíše Záložky odstránené.
- Ak dokument nemá žiadne záložky, stavový riadok napíše Dokument nemá žiadne záložky.
- Záložky nie sú v hexe ani v karte výsledkov hľadania. Vo veľkom súbore, ktorého riadky sa ešte číslujú, príkaz napíše, že index riadkov ešte na to miesto nesiaha.

## [recovery] Obnova po páde
Program priebežne zapisuje neuložený text do žurnálu, najneskôr do dvoch sekúnd od zmeny. Neuložený dokument sa tak nestratí, ani keď program spadne alebo vypnete počítač.
1. Spustite program znova.
2. Karty sa vrátia vrátane neuloženého textu. Nad dokumentom je lišta Po neočakávanom ukončení sa vrátili karty a v zátvorke počet kariet s neuloženými zmenami.
3. Tlačidlo Podrobnosti vypíše každý dokument: obnovené vrátane neuložených zmien, znova otvorené zo súboru, súbor sa medzitým zmenil, súbor už neexistuje, alebo nepodarilo sa obnoviť s dôvodom.
- Ak sa súbor medzitým zmenil na disku, zmeny zostanú v žurnále a nič sa neprepíše.
- Žurnál leží v priečinku profilu. Iný priečinok určuje nastavenie Priečinok žurnálu, náhradný Náhradný priečinok žurnálu.
- Ak sa žurnál nedá zapisovať do žiadneho priečinka, lišta povie: Obnova po páde je vypnutá. Neuložený text potom zostáva len v kartách; ukladajte ručne. Keď prestane fungovať za prevádzky: Žurnál prestal fungovať, obnova už neplatí, ukladajte ručne.
- Ak sa použije náhradný priečinok, lišta napíše, ktorý.
- Prerušená dávka nahradení v súboroch sa po štarte ponúkne: Dokončiť, alebo Vrátiť späť.

## [paged] Veľké súbory (stránkovaný režim)
Súbor väčší ako hranica veľkého súboru (nastavenie Hranica veľkého súboru, predvolene 6 MiB) sa otvorí v stránkovanom režime. Ak chcete súbor prechádzať a upravovať po bajtoch bez týchto obmedzení, otvorte ho v hexe (téma Hex: otvorenie a pohyb). Program číta len riadky na obrazovke, takže súbor sa otvorí rýchlo a nezaberie pamäť.
Stavový riadok potom ukazuje Veľký súbor, jeho veľkosť a revíziu.
### Čo ide
- Prechádzať šípkami, PageUp, PageDown, Home, End a Ctrl+Home a Ctrl+End. Prejsť na riadok, na percento alebo na bajt (Ctrl+G).
- Písať, mazať (Backspace a Delete), Enter a Tab na mieste kurzora. Späť a Znova. Uložiť.
- Hľadať (Ctrl+F). Hľadanie v súboroch.
### Čo nejde
- Výber, schránka (Vystrihnúť, Kopírovať, Vložiť), blokový výber, viac kurzorov.
- Príkazy nad riadkami, odsadením, veľkosťou písmen, nahradenie.
- Prejsť na párovú zátvorku, zalamovanie riadkov.
- Prevod a opätovné čítanie kódovania, zjednotenie koncov riadkov, normalizácia.
- Uložiť ako len v rovnakom kódovaní.
- Riadok dlhší ako 16 KiB sa ukáže len sčasti a upraviť sa nedá.
### Hlásenia
- Číslovanie riadkov ešte beží, úpravy pôjdu o chvíľu: počkajte, kým sa súbor dočíta.
- Riadok je dlhší ako 16 KiB, v stránkovanom režime sa nedá upraviť: riadok upravte iným programom.
- Znak sa nedá zapísať v kódovaní súboru: napíšte iný znak.
- Veľký súbor v kódovaní ... sa zatiaľ nedá otvoriť: súbor sa neotvorí; iné kódovanie vo veľkom súbore program zatiaľ neprečíta.
- Ukladanie veľkého súboru zdrží okno.

## [conflict] Zmena súboru zvonku
Ak súbor zmení iný program, nad dokumentom sa ukáže lišta Súbor sa na disku zmenil.
- Znova načítať zahodí vaše zmeny a načíta súbor z disku.
- Ponechať moje nechá vaše zmeny; ďalšie uloženie sa opýta, než súbor prepíše.
- Porovnať otvorí porovnávač (téma Porovnať s diskom).
Ak je súbor zmazaný alebo premenovaný, lišta napíše, že už nie je. Ponechať nechá kartu, Zavrieť kartu ju zavrie.
Pri uložení dostanete tú istú otázku v okne Súbor sa zmenil: Prepísať súbor, Porovnať, Zrušiť. Nič sa neprepíše bez súhlasu.

## [statusbar] Stavový riadok
Dole je vidieť riadok a stĺpec kurzora (pri výbere počet znakov, pri viacerých kurzoroch ich počet), odsadenie, kódovanie, konce riadkov a jazyk. Správy o výsledku príkazu sa ukážu na rovnakom mieste.
- Kliknutie na odsadenie ponúkne medzery alebo tabulátory a veľkosť.
- Kliknutie na kódovanie otvorí výber kódovania (téma Kódovanie).
- Kliknutie na konce riadkov ich zjednotí (téma Konce riadkov).
- Kliknutie na jazyk otvorí výber jazyka (téma Režim jazyka).
- Zvýraznenie sa dopočítava: pri dlhom súbore zvýraznenie dobieha, text je použiteľný. Riadok je príliš dlhý na zvýraznenie: takýto riadok zostane bez farieb.
- Pri súbore, na ktorý sa vzťahuje .editorconfig, je vidieť položka .editorconfig; kliknutím sa súbor otvorí.

## [errors.file] Chybové hlásenia: súbory
- Súbor sa nepodarilo otvoriť: cesta (dôvod). Skontrolujte, že súbor existuje, že naň máte oprávnenie a že ho nedrží iný program.
- Uloženie sa nepodarilo: dôvod. Obsah zostáva v karte. Uložte inam (Uložiť ako) alebo opravte príčinu (miesto na disku, oprávnenia) a uložte znova.
- Záloha sa nepodarila, súbor zostal bez zmeny: príčinu opravte (zvyčajne miesto na disku) a uložte znova.
- Uložené, ale záloha sa nepodarila: súbor je v poriadku, chýba len záloha predchádzajúcej verzie.
- Súbor sa zmenil alebo Súbor už na disku nie je: pozri tému Zmena súboru zvonku.
- Znaky, ktoré kódovanie nepodporuje: vyberte iné kódovanie (napríklad UTF-8), alebo zvoľte Nahradiť a uložiť, čím sa znaky nahradia otáznikom.
- Po normalizácii by znaky nešlo zapísať v kódovaní: zmeňte kódovanie súboru, normalizáciu potom zopakujte.
- Zmena sa nepodarila, text sa medzitým zmenil: príkaz zopakujte.
- Orezanie medzier pri ukladaní sa nepodarilo: súbor sa uloží bez orezania; vypnite orezanie medzier v nastaveniach, alebo súbor zmenšite.
- Veľký súbor v kódovaní ... sa zatiaľ nedá otvoriť: pozri tému Veľké súbory.

## [errors.files] Chybové hlásenia: panel Súbory
Pri nepodarenej operácii so súborom je nad dokumentom lišta Operácia zlyhala a dôvod, a vždy Nič sa nezmenilo.
- cesta neexistuje: obnovte strom (F5), súbor medzitým zmizol.
- cesta už existuje: zvoľte iný názov.
- Pre cestu chýba oprávnenie: zmeňte oprávnenia v správcovi súborov systému.
- cestu používa iný program: zavrite ten program a skúste to znova.
- Na disku nie je dosť miesta: uvoľnite miesto.
- názov nie je platný názov: odstráňte znaky, ktoré systém v názvoch zakazuje.
- S cestou to na tomto systéme takto nejde: použite správcu súborov systému.
- Priečinok sa nedá otvoriť: vyberte iný priečinok alebo skontrolujte oprávnenia.

## [errors.search] Chybové hlásenia: hľadanie
- Neplatný výraz: dôvod. Opravte regulárny výraz alebo vypnite .*; tlačidlo Skúsiť ukáže, ako sa výraz číta.
- Výraz je príliš pomalý a hľadanie ho zastavilo: zjednodušte výraz, najmä vnorené opakovania, alebo zvýšte nastavenie Časový limit regulárneho výrazu.
- Najprv vyberte text, v ktorom sa má hľadať: prepínač Výber je zapnutý bez výberu; vyberte text, alebo prepínač vypnite.
- Priečinok neexistuje: opravte cestu v poli Priečinok.
- Nič sa nenašlo: zhoda nie je; skontrolujte prepínače Aa, .* a ab.
- Zobrazených je prvých N zhôd, hľadanie sa zastavilo: zúžte hľadanie.
- Hľadanie bolo zrušené: spustite ho znova.
- Súbory s konfliktom ostali bez zmeny: pozri tému Hľadanie a nahrádzanie v súboroch.
- Žurnál obnovy nie je k dispozícii: nahrádzanie v súboroch sa vykoná bez istoty, že sa po páde dá dokončiť alebo vrátiť; radšej ho spustite, keď žurnál funguje.
- „...“ nie je číslo riadka: v okne Prejsť na riadok napíšte číslo, riadok:stĺpec, percento alebo @bajt (téma Prejsť na riadok).

## [limits] Čo táto verzia nevie
Pomocník opisuje len to, čo táto verzia vie. Tieto veci v nej zatiaľ nie sú:
- Aktualizácia programu: program sa sám neaktualizuje a nekontroluje nové verzie. Novú verziu nainštalujete sami.
- Hex editor nemá minimapu, nehľadá všetky zhody do karty a bajty nenahrádza; skok na offset neukazuje náhľad cieľa; nastavenie, ktoré by súbory vždy otváralo v hexe, chýba.
- Porovnanie súborov vlastným oknom: používa sa externý porovnávač (téma Porovnať s diskom).
- Úpravy veľmi veľkých súborov: len obmedzené, pozri tému Veľké súbory.
- Rozšírenia (pluginy), vzdialené súbory a ďalšie funkcie z plánu verzie 1.0: v tejto verzii nie sú.

## [edit.clips] Klipy
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables
Klip je kus textu s miestami pre premenné a kurzory. Klipy dodávajú dátové pluginy v priečinku plugins v profile; jeden podpriečinok na plugin, so súborom nkspad-plugin.json a bez programu.
1. Zvoľte Úpravy, Vložiť klip. Zoznam ukazuje klipy s náhľadom textu, ktorý by sa vložil.
2. Vyberte klip. Ak má formulár, vyplňte ho: náhľad sleduje, čo píšete, zlá hodnota je označená a OK zostane zošedlé, kým každá hodnota nesedí so svojím typom.
3. Text sa vloží do každého kurzora a nahradí každý výber, ako jeden krok Späť. Kurzory skončia tam, kde má klip značku $|.
- Uložiť výber ako klip sa opýta na názov a pridá označený text do vašej vlastnej knižnice (plugin user.clips, priečinok user-clips). Text sa uloží doslovne, takže dolár v ňom zostane dolárom. Klip je v zozname hneď.
- Jazyk šablón: ${name} je premenná, $| značí kurzor (koľko chcete), $$ je znak dolára. Konce riadkov sa zmenia na koniec riadka dokumentu.
- Odkiaľ premenná berie hodnotu, od najsilnejšieho: 1. hodnota napísaná vo formulári klipu, 2. vaše vlastné premenné (Úpravy, Upraviť používateľské premenné otvorí súbor variables.json v profile: jeden objekt JSON s menom a textom), 3. vstavané. Rovnaké meno znamená všade to isté.
- Vstavané premenné: file.path, file.dir, file.name, file.base, file.ext (neuložený dokument file.* nemá), date (rrrr-MM-dd), time (HH:mm:ss), datetime (nastavenie Formát dátumu a času), guid (pre každý kurzor nový), selection (text vybraný pri tomto kurzore), user.name. Meno bez hodnoty vloženie zastaví a stavový riadok ho vypíše.
- Polia formulára: name, label, type, default, choices, required. Typy: text, integer (celé číslo), number (desatinná bodka), color (#RGB, #RGBA, #RRGGBB, #RRGGBBAA), date (rrrr-MM-dd), file (existujúci súbor), choice (jedna z hodnôt choices). Predvolená hodnota smie používať premenné, napríklad ${date}.
- V nkspad-plugin.json je klip {"id": "box", "title": "Box", "text": "<div>$|</div>", "fields": [...]} v contributes.clips; klip s "command" dostane aj príkaz toho mena.
- Problémy s pluginom (poškodený súbor, program v dátovom plugine) sa ukážu v stavovom riadku a plugin sa preskočí.
- Klipy nefungujú v dokumente len na čítanie ani v stránkovanom režime (téma Veľké súbory).

## [file.templates] Nový súbor zo šablóny
commands: nks.file.newFromTemplate
1. Zvoľte Súbor, Nový zo šablóny a vyberte šablónu. Šablóna s formulárom sa pýta na hodnoty rovnako ako klip.
2. Otvorí sa nová karta s textom šablóny a kurzormi na jej značkách $| (na začiatku, keď žiadnu nemá).
- Šablóny sú tiež dáta: contributes.templates v nkspad-plugin.json, s id, title, text, fields a voliteľne command. Používajú jazyk šablón a premenné z témy Klipy; nový súbor nemá premenné file.*.

## [edit.convert] Prevod textu
commands: nks.edit.convert
Prevod zmení text všetkých výberov naraz, alebo celého súboru, keď nič nie je označené. Nový text zostane označený.
1. Zvoľte Úpravy, Previesť. Zoznam ukáže prevody s náhľadom na označenom texte (keď nič nie je označené, na prvej vzorke prevodníka).
2. Vyberte jeden. Všetky výbery sa zmenia jedným krokom Späť. Keď sa niektorý nedá previesť, nič sa nezmení a stavový riadok povie prečo.
- Zopakovať posledný príkaz spustí posledný prevod bez zoznamu.
- Prevodníky sú dáta: contributes.converters v nkspad-plugin.json, s id, title, rules a voliteľne samples a command. Pravidlo je {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Pravidlá bežia v poradí, každé nad výsledkom predchádzajúceho. So zapnutým regex platí dialekt hľadania: replace smie používať $1, ${name}, $& a kódy veľkosti písmen \U \L \E \u \l.
- Vzorka je {"input": "...", "expected": "..."}. Pri čítaní pluginu sa každá vzorka prevedie. Prevodník, ktorého vzorka dá iný text, ktorý nemá pravidlá, má prázdne find alebo chybný regulárny výraz, sa odmietne a plugin sa preskočí s dôvodom v stavovom riadku.
- Prevodník s "command" dostane aj príkaz toho mena, ktorý prevádza hneď, bez zoznamu.
- Špeciálne prevody samotného editora sú v tom istom zozname. Táto verzia zatiaľ žiadne nemá.

## [edit.recipes] Recepty úprav
commands: nks.edit.runRecipe
Recept vyberie časti otvoreného súboru, zmení ich a pred zápisom ukáže výsledok.
1. Zvoľte Úpravy, Spustiť recept úprav. Zoznam ukáže každý recept so zmenami, ktoré by v tomto súbore urobil.
2. Vyberte jeden. Okno náhľadu vypíše každú zmenu: riadok, stĺpec, starý a nový text. Tlačidlom Použiť ich zapíšete.
3. Všetky zmeny sú jeden krok Späť; Späť vráti súbor presne do pôvodného stavu. Zmenené časti zostanú označené. Nič sa nezapíše, keď sa súbor medzitým zmenil alebo recept nejde spustiť (premenná bez hodnoty, príliš pomalý vzor).
- Recept pracuje s výbermi, alebo s celým súborom, keď nič nie je označené. Zopakovať posledný príkaz použije posledný recept bez náhľadu.
- Recepty sú dáta: contributes.recipes v nkspad-plugin.json, s id, title, steps a voliteľne command. Kroky, ktoré vyberajú, idú prvé. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} ponechá zhody vo vnútri doterajších častí. {"kind": "columns", "from": 3, "to": 5} ponechá stĺpce 3 až 5 každého riadku (počíta sa v znakoch od 1; to 0 alebo vynechané znamená do konca riadku; kratší riadok nedá nič).
- Kroky, ktoré menia: {"kind": "convert", "converter": "id"} (prevodník rovnakého pluginu), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} a {"kind": "set", "text": "..."}, ktorý nahradí časť textom v jazyku šablón klipov. V ňom je ${selection} samotná časť a premenné majú rovnaké hodnoty ako v klipe (téma Klipy); kurzory $| sa ignorujú.
- Recept bez krokov, s neznámym druhom, chybným vzorom, neznámym prevodníkom, vyberajúcim krokom po meniacom alebo bez meniaceho kroku sa pri čítaní pluginu odmietne.

## [edit.aiContext] Kopírovať ako kontext pre AI
commands: nks.edit.copyAiContext
Vloží do schránky výber, súbor, otvorené karty alebo otvorený priečinok ako jeden blok v Markdowne alebo XML, ktorý vložíte do AI asistenta. Nič sa nikam neposiela.
1. Zvoľte Úpravy, Kopírovať ako kontext pre AI a vyberte, čo kopírovať: Výber (všetky výbery), Tento súbor, Otvorené karty alebo Otvorený priečinok.
2. Náhľad ukáže, koľko súborov sa vloží, veľkosť ako Markdown a ako XML a čo bolo vynechané s dôvodom. Zvoľte Kopírovať ako Markdown alebo Kopírovať ako XML.
- Pri každom súbore je cesta, riadky, jazyk, kódovanie a revízia. Súbor s neuloženými zmenami je označený a text je ten, ktorý je teraz v editore.
- Vynechané a pomenované: súbory .env (ich hodnoty sa nikdy nekopírujú, ani keď sú v editore zobrazené), súbory a priečinky vylúčené súbormi ignorovania, skryté, binárne súbory, súbory nad limitom veľkosti pri prechádzaní priečinka a súbory, ktoré sa nedajú prečítať.
- Veľký súbor v stránkovanom režime sa nikdy nekopíruje celý: len jeho výber. Bez výberu je uvedený ako vynechaný.

## [table.view] Tabuľka pre CSV a TSV
commands: nks.view.table, nks.table.dialect
Súbor CSV alebo TSV možno zobraziť ako tabuľku: jeden riadok súboru je jeden riadok tabuľky, jedno pole je jedna bunka. Je to ten istý dokument ako v textovom zobrazení, s rovnakou históriou Späť; nič sa neprevádza a úprava bunky zmení len bajty tej bunky.
1. Ctrl+K a potom Ctrl+T (ponuka Zobrazenie, Prepnúť tabuľku a text) prepína medzi tabuľkou a textom. Súbor .tsv alebo .tab sa delí podľa tabulátorov, ostatné súbory podľa čiarok.
2. Zobrazenie, Oddeľovač a záhlavie tabuľky vyberie čiarku, bodkočiarku, tabulátor alebo zvislú čiaru ako oddeľovač, či úvodzovky ohraničujú hodnoty a či je prvý riadok záhlavie.
- Veľký súbor sa otvorí hneď: riadky sa počítajú na pozadí a rozsah posuvníka rastie, ako sa súbor číta. Kým sa nedôjde na koniec, stavový riadok ukazuje počet riadkov s plusom, napríklad 12+.
- Šípky, Tab, PageUp a PageDown, Home a End (v rámci riadku), Ctrl+Home a Ctrl+End presúvajú aktuálnu bunku; Shift rozširuje výber. Kliknutie na záhlavie stĺpca vyberie stĺpec, kliknutie na číslo riadku vyberie riadok.
- Bunka v úvodzovkách môže obsahovať zalomenie riadku; tabuľka ukáže jej prvý riadok a značku ↵.
- Kopírovať dá vybrané bunky do schránky ako text oddelený tabulátormi; Vložiť zapíše text oddelený tabulátormi od aktuálnej bunky.

## [table.edit] Tabuľka: úpravy a filter
commands: nks.table.insertRow, nks.table.deleteRow, nks.table.insertColumn, nks.table.deleteColumn, nks.table.filter, nks.table.clearFilter
1. Enter alebo F2 (alebo jednoducho písanie) upraví aktuálnu bunku. Enter uloží a posunie sa nadol, Shift+Enter vloží zalomenie riadku, Esc zruší. Bunka, ktorá bola v úvodzovkách, v nich zostane; iná hodnota sa uvedie do úvodzoviek len vtedy, keď obsahuje oddeľovač, úvodzovku alebo zalomenie riadku.
2. Zobrazenie, Vložiť riadok pridá prázdny riadok za aktuálny. Vložiť stĺpec sa spýta na názov stĺpca a pridá ho za aktuálny. Zmazať riadok a Zmazať stĺpec odstránia vybrané riadky alebo stĺpce. Každá akcia je jeden krok Späť.
3. Zobrazenie, Filtrovať riadky ukáže len riadky, ktorých aktuálny stĺpec obsahuje zadaný text (na veľkosti písmen nezáleží). Zrušiť filter ukáže všetky riadky. Filter súbor nikdy nemení a čísla riadkov zostávajú číslami riadkov v súbore.
- Delete vymaže vybrané bunky. Krátky riadok dostane chýbajúce oddeľovače, keď sa vyplní bunka za jeho koncom.
- Keď je znak úvodzoviek vypnutý v Oddeľovač a záhlavie tabuľky, hodnota s oddeľovačom alebo zalomením riadku sa odmietne.
- Kým je zapnutý filter, nemožno pridať riadok.

## [table.region] Úprava vybraného textu ako tabuľky
commands: nks.table.editRegion
Vyberte časť textu, napríklad blok riadkov oddelených čiarkami v poznámke alebo v logu, a zvoľte Zobrazenie, Upraviť výber ako tabuľku.
1. Dialóg sa spýta na oddeľovač (čiarka, bodkočiarka, tabulátor alebo zvislá čiara), či úvodzovky ohraničujú hodnoty a či je prvý vybraný riadok záhlavie. Náhľad ukáže prvé riadky ako bunky.
2. Vybraný text sa stane tabuľkou. Zvyšok súboru zostane textom: úprava bunky zmení len bajty tej bunky a nič mimo výberu sa nečíta ani nemení.
- Funguje v ňom všetko z tabuľky: pohyb, úpravy, pridávanie a mazanie riadkov a stĺpcov, filter, kopírovanie a vkladanie. Stavový riadok hovorí, že tabuľkou je len vybraný text.
- Riadky pridané na koniec patria do výberu. Prepnúť tabuľku a text vráti text; ďalšie Prepnúť tabuľku a text ukáže ako tabuľku celý súbor.

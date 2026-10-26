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
- Vo veľkom súbore, ktorého riadky sa ešte číslujú, ukáže stavový riadok: Index riadkov na túto pozíciu ešte nesiaha, skúste to o chvíľu.

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
- Pod zoznamom je náhľad zvýrazneného súboru; binárny súbor sa nezobrazí.

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

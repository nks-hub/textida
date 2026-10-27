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

## [remote.connections] Vzdialené pripojenia
commands: nks.remote.connections
Pripojenie si pamätá server, používateľa a protokol. Heslo je v systémovej kľúčenke operačného systému a nikdy sa nezapíše do súboru nastavení ani do zálohy.
1. Zvoľte Súbor a potom Vzdialené pripojenia. Zoznam ukáže uložené pripojenia.
2. Zvoľte Nové pripojenie a odpovedzte na otázky: názov, protokol, server, port, používateľ a heslo. Pri SFTP možno namiesto hesla zadať súbor so súkromným kľúčom.
3. Vyberte pripojenie, otvorte z neho súbor, alebo ho zmažte aj s uloženým heslom.
- Bežná voľba je FTPS a SFTP. Obyčajné FTP posiela heslo aj súbory nešifrovane a použije sa, až keď ho pre dané pripojenie výslovne povolíte.
- Pri prvom pripojení program ukáže identitu servera (kľúč SSH alebo certifikát TLS) a opýta sa, či jej veríte. Porovnajte ju s tou, ktorú vám dal správca servera.
- Ak sa zmení identita servera, ktorému ste uverili, program sa nepripojí, kým novú nepotvrdíte. Skôr než to urobíte, prečítajte si varovanie.
- Ukladanie hesla funguje vo Windows (Správca poverení). V macOS a Linuxe systémová kľúčenka zatiaľ nie je podporovaná a program to povie, namiesto aby heslo uložil inam.

## [remote.open] Otvoriť vzdialený súbor
commands: nks.remote.open
1. Zvoľte Súbor a potom Otvoriť vzdialený súbor a vyberte pripojenie.
2. Napíšte cestu na serveri. Cesta k priečinku ukáže jeho obsah: priečinok otvorte, aby sa vošlo dovnútra, súbor, aby sa otvoril.
3. Súbor sa skopíruje do tohto počítača a otvorí v karte. Upravujte a ukladajte ako zvyčajne: Uložiť mení len kópiu v tomto počítači.
- Lišta nad textom hovorí, či je kópia zhodná so serverom. Editor sám funguje bez pripojenia.
- Vzdialený súbor, ktorý už kópiu má, sa otvorí z kópie, vrátane zmien, ktoré ste ešte neposlali. Verzia zo servera sa nikdy nestiahne cez vašu prácu.
- Záznam prenosov je na karte Vzdialené v okne výstupu.

## [remote.upload] Nahrať na server
commands: nks.remote.upload
1. Zvoľte Súbor a potom Nahrať na server. Zmenená karta sa najprv uloží.
2. Skôr než program čokoľvek pošle, pozrie sa na súbor na serveri. Ak je to stále verzia, ktorú ste otvorili, nový obsah sa pošle pod dočasným menom a až potom nahradí starý súbor. Nepodarené alebo prerušené nahranie nechá na serveri starú verziu.
3. Ak sa súbor na serveri medzitým zmenil, nepošle sa nič. Zvoľte Zobraziť rozdiely, uvidíte verziu zo servera vedľa svojej, prevezmite do svojej strany, čo potrebujete, uložte a nahrajte znova. Aj tak nahrať nahradí práve tú verziu servera, ktorú ste videli.
- Nastavenie Kontrola verzie na serveri určuje, ako sa zmena pozná: stat porovnáva veľkosť a čas zmeny, content vždy stiahne súbor zo servera a porovná ho; pozná aj zmenu v rovnakej sekunde a s rovnakou veľkosťou.
- Medza: kontrola a výmena sú dva kroky, pretože FTP ani SFTP nemajú podmienený zápis. Zmena, ktorú niekto urobí v tom poslednom okamihu, sa nespozná. Kontrola beží až po dokončenom nahraní, takže ten okamih je krátky.
- Nič sa neopakuje samo. Po chybe zvoľte Nahrať na server znova; najprv opäť skontroluje server.

## [remote.check] Skontrolovať verziu na serveri
commands: nks.remote.check
Zvoľte Súbor a potom Skontrolovať verziu na serveri. Lišta povie: zhodné so serverom, zmenené tu a neodoslané, zmenené na serveri, zmenené na oboch miestach, alebo súbor už na serveri neexistuje. Nič sa nesťahuje ani nemení.

## [remote.unsent] Neodoslané vzdialené zmeny
commands: nks.remote.unsent
Zmeny vzdialeného súboru sa najprv uložia v tomto počítači. Zoznam ukáže vzdialené súbory, ktorých kópia je zmenená a neodoslaná, aj po zatvorení programu alebo po páde. Vyberte jeden a otvorí sa kópia; lišta ponúkne Nahrať.
- Úpravy nepotrebujú pripojenie. Neodoslané zmeny zostanú, kým ich nenahráte.

## [remote.backup] Záloha pripojení
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Zvoľte Súbor a potom Exportovať pripojenia a pomenujte súbor. Obsahuje zoznam pripojení: názov, protokol, server, port, používateľa a dôveryhodnú identitu servera.
2. Zvoľte Súbor a potom Importovať pripojenia a načítate taký súbor na inom počítači alebo po preinštalovaní.
- Súbor neobsahuje heslá, prístupové frázy ani tokeny, takže ho možno zdieľať. Po importe vyberte pripojenie a zvoľte Nastaviť heslo.
- Importované pripojenie nahradí pripojenie s rovnakým vnútorným identifikátorom.

## [remote.proxy] Sieťový proxy
commands: nks.remote.proxy
1. Zvoľte Súbor a potom Sieťový proxy a vyberte HTTP, SOCKS4, SOCKS5 alebo žiadny proxy.
2. Zadajte server a port, a podľa potreby používateľa a heslo proxy. Heslo sa uloží do systémovej kľúčenky.
- Proxy sa používa pre FTPS a SFTP. Pripojenie, ktoré má vlastný proxy, si ho ponechá.
- SOCKS4 funguje len pre SFTP. Pre FTPS použite SOCKS5 alebo HTTP.
- Iný typ proxy program odmietne so správou.

## [file.locks] Zamknuté súbory
Súbor sa otvorí len na čítanie, keď ho má otvorený iný program a nedovoľuje ostatným zapisovať, alebo keď ho upravuje iné okno Textidy. Lišta nad textom uvedie dôvod.
- Skúsiť znova skontroluje zámok znova, napríklad po zatvorení súboru v inom programe.
- Aj tak upraviť zapne pre súbor súbežný zápis. Každé uloženie potom porovná súbor na disku s tým, čo ste otvorili, a opýta sa, keď sa zmenil; nič sa neprepíše potichu.
- Zámok okna zaniká s oknom, aj po páde. Textida nezapisuje žiadne súbory zámkov k vašim dokumentom a súbory .lck iných programov ignoruje.

## [file.history] Miestna história
commands: nks.file.history
Program uchováva revíziu súboru pri každom uložení a pri každej zmene, ktorú súbor urobí iný program. Nepotrebuje projekt ani Git.
1. Zvoľte Súbor a potom Miestna história. Zoznam ukazuje revízie súboru z karty, najnovšie prvé, s dôvodom, veľkosťou a štítkom.
2. Vyberte revíziu. Porovnať ju otvorí vo vašom porovnávači (rovnakom ako Porovnať s diskom) vľavo a kartu vpravo. Obnoviť súbor vloží do karty celú revíziu. Obnoviť výber nahradí len vybrané riadky zodpovedajúcimi riadkami revízie. Štítok revíziu pomenuje.
- Obnova je úprava v karte; Späť ju vráti a nič sa nezapíše, kým neuložíte. Text, ktorý v karte bol, zostane ako revízia.
- Revízie ležia v priečinku profilu, takže ich aktualizácia programu nezmaže.
- Súbory s tajnými hodnotami, napríklad .env, sa neukladajú, kým v nastaveniach nezapnete Ukladať históriu súborov s tajnými hodnotami.
- Limity (revízií na súbor, dni, MiB na súbor, MiB celkom) sa nastavujú v nastaveniach; 0 znamená bez obmedzenia. Najnovšia revízia súboru sa nikdy nezmaže.
- Keď je disk plný, stavový riadok to oznámi a žiadna revízia sa nepoškodí.

## [file.historyMark] Označiť revíziu
commands: nks.file.historyMark
1. Zvoľte Súbor a potom Označiť revíziu a napíšte štítok, napríklad Pred refaktorom.
2. Súčasný text karty sa uloží ako revízia s týmto štítkom. Ak posledná revízia už obsahuje rovnaký text, štítok dostane ona.
- Revíziu neskôr nájdete v Miestnej histórii podľa štítka.

## [file.historySearch] Hľadať v histórii
commands: nks.file.historySearch
1. Zvoľte Súbor a potom Hľadať v histórii a napíšte text.
2. Zoznam ukáže revízie súboru, ktoré text obsahujú, s počtom zhôd a prvým riadkom. Vyberte jednu a otvoria sa akcie, napríklad Porovnať.
- Revízie súborov s tajnými hodnotami sa neprehľadávajú.

Karta a súbor na disku sa zobrazia vedľa seba v samostatnej karte. Vľavo je karta aj s neuloženými zmenami, vpravo súbor na disku.
1. Zvoľte Súbor a potom Porovnať s diskom. Otvorí sa karta s porovnaním (téma Porovnanie textov).
2. Ak je v nastaveniach vyplnený Externý porovnávač, spustí sa namiesto toho váš program: vľavo súbor na disku, vpravo obsah karty. V šablóne argumentov je {left} súbor na disku a {right} obsah karty. Kópia karty odovzdaná programu sa po jeho skončení zmaže.
- Karta bez súboru na disku povie: Karta nemá súbor na disku, nie je s čím porovnávať.
- Keď sa váš program nespustí, stavový riadok napíše Porovnávač sa nespustil a uvedie dôvod.
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

## [edit.macro] Makrá
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run
1. Nástroje, Nahrať makro, spustí nahrávanie. Pracujte príkazmi a písaním. Nástroje, Zastaviť nahrávanie makra, ho ukončí.
2. Nástroje, Prehrať posledné makro, ho znova prehrá nad aktuálnymi výbermi a kurzormi.
3. Nástroje, Uložiť posledné makro ako, ho uloží pod názvom do priečinka macros v profile. Nástroje, Spustiť uložené makro, ukáže uložené makrá a prehrá vybrané.
- Makro si pamätá príkazy, nie klávesy: Duplikovať riadok, veľkosť písmen, Pridať ďalší výskyt a podobné, a napísaný text. Pri viacerých kurzoroch urobí to isté ako ručný postup.
- Prehratie je jeden krok Späť. Keď krok neprebehne, makro sa tam zastaví a stavový riadok krok pomenuje; čo prebehlo predtým, zostáva jedným krokom Späť.
- Pri nahrávaní sa príkaz, ktorý makro nemôže prehrať, odmietne a stavový riadok to povie: všetko s dialógom, príkazy so súbormi, Vystrihnúť, Kopírovať, Vložiť, pohyb kurzora šípkami, Hľadať. Nahrávanie pokračuje.
- Prehratie potrebuje upraviteľný textový dokument. Import makier PSPadu nie je k dispozícii.

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

## [format.inspector] Inšpektor bajtov
commands: nks.format.applyEditorConfig
Každá akcia, ktorá prepisuje bajty, ktoré ste neupravili, najprv ukáže, čo zmení, a nič nezapíše, kým ju nepotvrdíte: Previesť do kódovania, Konce riadkov, Normalizovať Unicode, Uložiť ako s iným kódovaním a Použiť EditorConfig.
- Okno uvádza počet zmenených úsekov a bajtov, veľkosť pred a po, kódovanie a konce riadkov pred a po a jeden riadok na úsek: kde leží, pôvodná a nová dĺžka a prvé bajty (najviac 500 riadkov, zvyšok je započítaný).
- Keď cieľové kódovanie niektoré znaky nepojme, to isté okno vypíše každý z nich s riadkom, stĺpcom a bajtom a ponúkne tri odpovede: zrušiť (nič sa nezapíše), zmeniť kódovanie, alebo každý znak nahradiť otáznikom.
- Príkaz Použiť kódovanie a konce riadkov z EditorConfig v ponuke Formát prevedie súbor na znakovú sadu a koniec riadka, ktoré nastavuje jeho .editorconfig; každý krok otvorí inšpektor. Vlastnosť, ktorú súbor nenastavuje, sa nechá tak.
- Úseky platia pre revíziu, ktorú ste videli; ak sa dokument medzitým zmení, akcia sa odmietne.
- Súbor a potom Zmeny pred uložením ukáže rovnaký výpis pre budúce uloženie.

## [format.batchEncoding] Prevod kódovania viacerých súborov
commands: nks.file.convertEncodingBatch
1. Formát a potom Previesť kódovanie súborov v priečinku. Zvoľte priečinok, masky názvov súborov (napríklad *.txt;*.php, prázdne pole znamená všetky súbory) a cieľové kódovanie.
2. Program súbory prečíta a skôr než niečo zapíše, ukáže, koľko sa ich prevedie bez straty a ktoré súbory by prišli o znaky, s týmito znakmi a ich počtom. Súbory, ktoré sú binárne, už majú tieto bajty alebo majú kódovanie, ktoré program len odhaduje, sa preskočia a vypíšu sa s dôvodom.
3. Zvoľte Previesť N súborov (súbory, ktoré by stratili znaky, zostanú, ako sú), alebo Previesť všetko a stratené znaky nahradiť otáznikom.
4. Súhrn vypíše, čo sa previedlo, čo zostalo a čo sa zmenilo po náhľade.
- Prechádzanie priečinka dodržiava súbory ignorovania a vynecháva skryté súbory a súbory nad 64 MiB, rovnako ako hľadanie v súboroch. Otvorené dokumenty s neuloženými zmenami zostanú bokom.
- Každý súbor sa nahradí celý, alebo vôbec, a len keď má stále obsah, z ktorého vznikol náhľad. Beh sa zapisuje do žurnálu: keď sa program alebo počítač uprostred zastaví, ďalší štart ponúkne dokončenie dávky alebo vrátenie starých súborov.
- Súbor v kódovaní, ktoré je len odhad (nízke percento istoty), sa nikdy neprevádza; otvorte ho, zvoľte Znova otvoriť v kódovaní a preveďte ho odtiaľ.

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
- F3 a Shift+F3 nájdu ďalšiu a predchádzajúcu zhodu. Stavový riadok napíše Zhoda 1 z 4, alebo Aspoň N zhôd, keď hľadanie došlo na limit.
- Ctrl+G otvorí Prejsť na offset. Zápis: 0x1F40, +0x100 (od kurzora), end-512, 50%, 4K, sel (začiatok výberu), val (štvorbajtová hodnota pod kurzorom).
- Nenájdené: zhoda nie je. Neplatný vzor alebo Neplatný výraz: opravte zápis. Offset leží mimo súboru: zadajte menšiu hodnotu. Súbor sa medzitým zmenil na disku, výsledok hľadania sa zahodil: hľadanie zopakujte.
- Zástupný znak *: 50 4B * 04 znamená 0 až N bajtov medzi 4B a 04. N je nastavenie Najdlhší zástupný znak * v hex hľadaní; kým je 0 (nezvolené), hľadanie oň požiada. *{2,8} zapíše vlastný rozsah a nastavenie nepotrebuje.
- Vzor medzi lomkami je regulárny výraz nad bajtmi, napríklad /\x50\x4B[\x01-\x08]/. Bajt je znak 0 až 255, . je jeden ľubovoľný bajt (aj 0A a 0D), \xHH je bajt a [...] množina bajtov. Skupiny, |, ? * + {n,m} a \n \r \t \0 \d \w \s fungujú; ^, $, \b, spätné odkazy a lookaround nie, pretože bajty nemajú riadky ani slová. Otvorené opakovanie (.*, +, {n,}) siaha najviac na N bajtov. Zhoda potrebuje aspoň jeden bajt a zhody sa smú prekrývať.
- Ctrl+H nahrádza bajty: najprv vzor (rovnaké tvary ako Hľadať), potom náhradu hexom, textom v úvodzovkách alebo číslom; prázdna náhrada zhody zmaže. Ak je niečo vybrané, hľadá sa len vo výbere.
- Náhľad vypíše každú zhodu s pôvodnými a novými bajtmi a upozorní, keď sa zmení dĺžka súboru. Nahradiť všetko zapíše všetko jedným krokom späť. Ak sa súbor po náhľade zmenil, nenahradí sa nič. Zhody, ktoré sa prekrývajú s predchádzajúcou, sa vynechajú.
- V režime prepisovania musí mať náhrada dĺžku zhody; dĺžku zmeníte po zapnutí režimu vkladania (kláves Insert).

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

## [hex.bookmarks] Hex: záložky a farebné oblasti
commands: nks.hex.bookmarks.panel, nks.hex.bookmarks.edit, nks.hex.bookmarks.export, nks.hex.bookmarks.import
Záložka v hexe označí oblasť bajtov menom, farbou, poznámkou a typom, ako bola prečítaná v inšpektore dát.
1. Ctrl+F2 označí výber (bez výberu hodnotu, ktorú inšpektor dát ukazuje pri kurzore, alebo jeden bajt) a odstráni najužšiu záložku pod kurzorom, ak nejaká je. F2 a Shift+F2 skočia na ďalšiu a predchádzajúcu záložku.
2. Hex: vlastnosti záložky otvorí meno, farbu, poznámku a typ záložky pod kurzorom (bez nej vznikne nová).
3. Hex: panel záložiek (Ctrl+K a potom Ctrl+B) vypíše záložky s filtrom, ktorý hľadá v mene, poznámke a type. Enter alebo dvojklik vyberie oblasť, Delete ju odstráni.
4. Hex: exportovať záložky zapíše CSV (meno, farba, poznámka, typ, počiatočný a koncový offset) alebo, keď názov súboru končí na .json, JSON. Hex: importovať záložky prečíta obe formy a pridá oblasti, ktoré sa do súboru zmestia; prázdna oblasť, oblasť mimo súboru alebo už existujúca sa preskočí a stavový riadok ich spočíta.
- Oblasť je vykreslená vo farbe záložky s prúžkom hore, takže farba nie je jediné znamenie; stavový riadok pomenuje záložku pod kurzorom.
- Oblasti sledujú bajty: bajty vložené pred oblasť ju posunú, bajty vložené dovnútra ju predĺžia, úprava cez jej okraj sa do nej zahrnie, späť a znova ju vrátia. Oblasť, ktorej bajty boli všetky zmazané, zaniká.
- Záložky sa ukladajú do relácie (po reštarte sú späť), nikdy do súboru. Zmazať záložky odstráni všetky.

## [hex.map] Hex: mapa súboru a štatistika bajtov
commands: nks.hex.minimap, nks.hex.minimap.mode, nks.hex.statistics
Mapa je prúžok vedľa posuvníka, ktorý ukazuje celý súbor naraz v bunkách s výškou asi štyri pixely. Obdĺžnik označuje časť na obrazovke; klik alebo ťahanie tam posunie mriežku.
1. Hex: mapa súboru (Ctrl+K a potom Ctrl+G) mapu ukáže alebo skryje. Hex: mapa podľa entropie alebo druhu bajtov prepína, čo bunky ukazujú: entropiu bajtov (svetlá pre usporiadané dáta, výrazná pre komprimované alebo šifrované), alebo druh bajtu, ktorý tvorí väčšinu bunky (nulové bajty, text, riadiace bajty, vysoké bajty, FF).
2. Úzky pruh pri pravom okraji označuje zhody posledného hľadania, záložky v ich farbách a miesta zmenené od posledného uloženia.
3. Hex: štatistika bajtov (Ctrl+K a potom Ctrl+S) otvorí panel s histogramom výberu, alebo celého súboru, keď nie je nič vybrané: koľko zo 256 hodnôt bajtu sa vyskytuje, entropia v bitoch na bajt, najčastejšie hodnoty a podiely tried bajtov.
- Súbor do 1 GiB sa číta celý na pozadí. Pri väčšom súbore mapa najprv ukáže vzorku, pár blokov z každej bunky, a spresňuje sa, ako program číta ďalej, celkom najviac 1 GiB; bunka, ktorá sa ešte nečítala, je svetlá.
- Štatistika celého súboru nad 1 GiB sa nepočíta naraz: vyberte úsek.
- Mapa aj počítanie bežia vedľa okna a pri zmene súboru sa robia znova.

## [hex.compare] Hex: binárne porovnanie
commands: nks.hex.compare.file, nks.hex.compare.saved
Dve hex mriežky vedľa seba, ktoré sa posúvajú spoločne, s rozdielmi podfarbenými a vypísanými dole.
1. Hex: porovnať so súborom sa spýta na súbor a porovná ho s dokumentom aktívnej karty (aj s jeho neuloženými zmenami). Hex: porovnať s uloženým súborom porovná dokument s jeho vlastným súborom na disku, takže vidíte presne, čo by budúce uloženie zmenilo.
2. So zarovnaním (predvolené) hľadá bajty, ktoré jeden súbor vložil alebo zmazal, takže vložený blok sa ukáže ako vloženie a bajty za ním sa nehlásia ako zmenené. Bajt po bajte porovnáva každý offset s rovnakým offsetom v druhom súbore, čo sa hodí na súbory s pevným rozložením. Použije sa pri každom súbore nad 256 MiB, ktorý režim so zarovnaním nečíta.
3. Zoznam pomenuje každý rozdiel: zmenené, vložené alebo zmazané, s jeho rozsahom v ľavom a v pravom súbore. Enter na riadku, F8 a Shift+F8 (alebo tlačidlá) skočia na ďalší a predchádzajúci rozdiel v oboch mriežkach.
- Posun jednej mriežky posunie druhú na rovnaké miesto v druhom súbore; pri vložených bajtoch je to zodpovedajúce miesto, nie rovnaký offset.
- Mriežky sú len na čítanie. Mapa vedľa každej mriežky označuje rozdiely.
- Zoznam končí na 1 000 000 rozdieloch (okno to povie); dva nesúvisiace súbory by inak zaplnili pamäť.
- Dve revízie súboru z miestnej histórie sa zatiaľ neponúkajú, pretože miestna história neexistuje.

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

## [tools.edit] Nástroje: úprava definícií, typované premenné, nástroje z priečinka
commands: nks.tools.edit
1. Nástroje, Upraviť definície nástrojov otvorí jeden editor nástrojov zo súboru tools.json. Nový, Uložiť a Zmazať zapisujú súbor hneď. Riadok pod formulárom ukáže príkaz presne tak, ako by sa spustil pre otvorený súbor, alebo prečo sa teraz nedá spustiť. Editor aj Spustiť nástroj berú rovnakú definíciu, takže nástroj sa z oboch spustí s rovnakými argumentmi.
2. Definícia môže deklarovať premenné, jednu na riadok: Názov:text alebo Názov:path, prípadne s =hodnota (hodnota smie používať ďalšie premenné, napríklad Out:path=${FileDir}/out). Bez hodnoty sa vezme používateľská alebo vstavaná premenná rovnakého mena. Poradie je: deklarovaná hodnota, vaše vlastné premenné (Upraviť používateľské premenné), vstavané. V executable, args a cwd ju použijete ako ${Názov}.
3. Text je jeden riadok. Cesta je jeden neprázdny riadok bez znakov * ? " < > | . Hodnota zlého typu zastaví spustenie skôr, než čokoľvek začne, a výstup menuje premennú.
4. Priečinok otvoreného súboru alebo priečinok nad ním môže obsahovať .textida/tools.json s nástrojmi rovnakého tvaru. Ukážu sa v Spustiť nástroj, ale spustia sa až po potvrdení príkazu a adresára, aj keď rovnaký text máte vo vlastnom súbore. Ak sa definícia zmení vrátane premenných, opýtame sa znova. Tieto nástroje sa v editore upraviť nedajú.
- Prázdny argument sa v editore napísať nedá. Uloženie zahodí komentáre v tools.json.

## [tools.make] Makefile: spustenie cieľa
commands: nks.tools.makeTargets
1. S otvoreným Makefile (Makefile, GNUmakefile, *.mk, *.mak) vypíše Nástroje, Spustiť cieľ Makefile jeho ciele z textu karty. Vybraný cieľ spustí ako make -f s týmto súborom a cieľom v priečinku súboru.
2. Beh ide cez spúšťač nástrojov: príkaz sa ukáže a prvýkrát sa potvrdzuje, výstup ide do karty Úlohy, Zastaviť ukončí make aj všetko, čo spustil.
3. Riadok výstupu, ktorý menuje súbor a riadok (main.c:12:5: error, Makefile:7:), otvorí to miesto klikom alebo Enterom.
- Make musí byť nainštalovaný a v PATH. Zvláštne ciele (.PHONY), vzorové pravidlá a názvy s premennou alebo zástupným znakom sa nevypisujú. Potvrdenie sa viaže na príkaz, nie na obsah Makefile, pretože spustiť Makefile znamená spustiť, čo hovorí.

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
- Karta výsledkov hľadania záložky nemá. V hexe tie isté príkazy označujú oblasti bajtov (pozri Hex: záložky). Vo veľkom súbore, ktorého riadky sa ešte číslujú, príkaz napíše, že index riadkov ešte na to miesto nesiaha.

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

## [tools.emmet] Skratky Emmet
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. V súbore HTML alebo CSS napíšte skratku a stlačte Tab. Z `ul>li*3` vznikne zoznam s tromi položkami a kurzorom v každej z nich.
2. Tab odsadzuje ako predtým, keď pred kurzorom nie je žiadna skratka, keď je niečo označené, keď je kurzor vo vnútri otvorenej značky alebo keď skratka nie je platná.
3. Nástroje, Náhľad skratky Emmet ukáže, čím by skratka pred kurzorom bola (`|` označuje kurzory), a po potvrdení ju rozbalí.
4. Nástroje, Obaliť skratkou Emmet (len HTML) sa opýta na skratku a obalí ňou každý výber. Text príde do najvnútornejšieho posledného prvku; holá `*` na tom prvku (`ul>li*`) urobí jeden prvok na riadok.
5. Každé rozbalenie ide do všetkých kurzorov naraz a je jeden krok Späť.
- HTML: `name#id.class[attr=value]{text}*3`; `>` potomok, `+` súrodenec, `^` o úroveň vyššie, `( )` skupina; v názvoch, triedach a texte je `$` číslo opakovania (`$$` doplní nuly, `$@-` počíta nadol, `$@3` začína od 3). Samotné slovo sa rozbalí, len keď je to názov značky HTML alebo krátky názov z pluginu (`bq`, `btn`, `a:link`, `input:checkbox`, `!` pre kostru stránky). Vo vnútri prvku sa chýbajúci názov doplní na `li`, `tr`, `td`, `option`, `span` alebo `div`, podľa rodiča.
- CSS: `m10-20` je `margin: 10px 20px;`, `w100p` je `width: 100%;`, `c#f00`, `pos:a`, `dn`, koncový `!` pridá `!important`, `+` spojí viac deklarácií. Celé číslo dostane `px`, zlomok `em`; písmená `p`, `e`, `x`, `r` za číslom znamenajú `%`, `em`, `ex`, `rem`. Funguje len vo vnútri bloku `{ }`.
- Krátke názvy sú vlastná tabuľka pluginu so bežnými vlastnosťami a značkami, nie úplná sada Emmet.
- Kurzory stoja vo všetkých prázdnych prvkoch naraz (keď žiadny nie je, v prvej prázdnej hodnote atribútu); skok z jedného zástupného miesta na ďalšie zatiaľ nie je.
- Proces Emmet sa spustí prvým Tabom v súbore HTML alebo CSS.

## [tools.git] Git: stav, porovnanie a príprava úseku
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk
1. Otvorte súbor z repozitára Git a v ponuke Nástroje zvoľte Git: stav súborov. Panel ukáže, čo `git status` hlási pre celý repozitár: čo je v indexe a čo je zmenené v strome. Príkaz spustite znova, keď sa repozitár zmení zvonka.
2. Git: porovnať s indexom a Git: porovnať s HEAD otvoria kartu porovnania otvoreného textu s verziou v indexe alebo v HEAD. Úseky sú rovnaké ako v `git diff`.
3. Git: pripraviť úsek pod kurzorom pridá do indexu iba zmenu, v ktorej stojí kurzor. Ostatné zmeny súboru ostanú len v strome. Súbor na disku sa nemení.
4. Ak sa index medzitým zmenil iným programom, nič sa nezapíše. Plugin index načíta znova a oznámi konflikt; skontrolujte zmeny a príkaz zopakujte.
- Plugin zapisuje do repozitára iba pri Git: pripraviť úsek. Stav a porovnanie iba čítajú a nemenia ani súbor index.
- Pripravuje sa text otvorený v editore, aj keď nie je uložený. Súbor s nevyriešeným konfliktom zlučovania sa nepripravuje.
- Zatiaľ chýba: vrátenie úseku z indexu, commit a vetvy. Súbor s prevodom koncov riadkov (`autocrlf`) sa môže s indexom líšiť v každom riadku.
- Proces Git sa spustí prvým príkazom. Program `git` musí byť v PATH.

## [tools.lsp] Jazykový server: definícia, odkazy a diagnostika
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.restart, nks.lsp.stop
1. Otvorte textový dokument a zvoľte Nástroje, Jazykový server: spustiť pre tento dokument. Spustí sa server pre jazyk dokumentu a dostane jeho text. Odvtedy mu ide každá úprava a to, čo server označí za chybu, sa podčiarkne.
2. Jazykový server: prejsť na definíciu skočí na definíciu slova pod kurzorom, aj do iného súboru. Jazykový server: nájsť odkazy vypíše všetky miesta v paneli vľavo; Enter alebo dvojklik na položku ju otvorí.
3. Keď server skončí sám, upozornenie to oznámi a podčiarknutia, ktoré nakreslil, zmiznú. Jazykový server: reštartovať (alebo iný príkaz tejto skupiny) ho spustí znova a odovzdá mu všetky otvorené dokumenty tak, ako sú teraz. Jazykový server: zastaviť ho vypne.
- Editor žiadny server sám nespúšťa: kým nezvolíte prvý príkaz tejto skupiny, nič nebeží a server o súbore nič nevie.
- Veľké súbory zobrazené po stránkach a súbory nad limitom veľkosti sa serveru nikdy neodovzdajú.
- Server sa hľadá podľa názvu v PATH. Keď sa nenájde, upozornenie pomenuje program, ktorý sa hľadal.
- Zatiaľ chýba: dopĺňanie a premenovanie cez server, položka v stavovom riadku a voľba priečinka projektu (použije sa priečinok prvého dokumentu).

## [edit.complete] Dopĺňanie slov a klipov
commands: nks.edit.complete
1. Stlačte Ctrl+J (Úpravy, Doplniť slovo alebo klip). Zoznam ponúkne slová, ktoré pokračujú slovo pred kurzorom, a potom klipy, ktoré sa hodia pre jazyk dokumentu.
2. Vyberte položku. Slovo nahradí slovo pred každým kurzorom; klip sa vloží ako v téme Klipy a nahradí toto slovo. Je to jeden krok Späť.
- Slová sa berú zo všetkých otvorených textových kariet. Slovo, ktoré práve píšete, sa neponúka, a slovo sa ponúka, len keď je dlhšie než napísaný začiatok. Najprv idú slová z upravovaného dokumentu, potom tie častejšie.
- Klipy sú z knižnice z témy Klipy. Klip bez jazyka sa hodí do každého dokumentu; klip s jazykom len do dokumentov toho jazyka.
- Hodnoty tajného súboru (`.env`) sa neponúkajú, kým sú skryté. Ponúkajú sa len v tom istom súbore a len keď sú zobrazené.
- Zoznam drží obmedzený počet slov; zúžite ho písaním do poľa. Dopĺňanie cez LSP v tejto verzii nie je.

## [edit.sql] SQL: dialekt a formátovanie
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Úpravy, Formátovať SQL usporiada výbery, alebo celý súbor, keď nie je nič vybrané: klauzula na samostatnom riadku, každá položka výberu na riadku, poddotazy odsadené, kľúčové slová jedným typom písma. Je to jeden krok späť a nič mimo výberu sa nezmení.
2. Úpravy, Náhľad formátovania SQL ukáže rovnaký výsledok najprv v náhľade zmien.
3. Dialekt určuje farby a to, ktoré slová sú kľúčové. Platí prvý nastavený z týchto: Nástroje, Dialekt SQL (len tento súbor); `sql_dialect = mysql` v súbore `.editorconfig` (každý súbor SQL v tom priečinku a pod ním); nastavenie Predvolený dialekt SQL. Bez nich je dialekt Generic a nič sa nehlási ako chyba.
- Dialekty: generic, mysql, postgres, sqlite, tsql. Editor sa k žiadnej databáze nepripája.
- Reťazce, ohraničené názvy a komentáre sa prepíšu bez zmeny. Ďalšie formátovanie výsledku nič nezmení.
- Výber väčší ako limit pluginu sa odmietne so správou.
- Odsadenie (4 medzery) a veľkosť kľúčových slov (veľké) sú zatiaľ pevné; nastavenie to ešte nie je.

## [edit.html] HTML: entity a HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlDecode, nks.edit.htmlTidy
1. Úpravy, Previesť na entity HTML nahradí v označených častiach (v celom súbore, keď nie je nič vybrané) znaky & < > " ' entitami. Previesť z entít HTML prečíta pomenované, desiatkové aj šestnástkové entity späť. Zakódovaný text prevedený späť dá pôvodný text. Každé je jeden krok späť.
2. Úpravy, Formátovať pomocou HTML Tidy pošle HTML programu HTML Tidy a výsledok ukáže v náhľade zmien. Správy Tidy idú do okna výstupu.
3. Tidy je samostatný program. Editor ho hľadá na ceste z nastavenia Program HTML Tidy, v priečinku `tidy` vedľa pluginu formátovačov, v priečinku `tools/tidy` aplikácie a v priečinkoch z PATH. Keď ho nenájde, správa vypíše všetky miesta.
- Tidy nie je súčasťou tohto zostavenia: nainštalujte ho a nastavte cestu, alebo ho dajte na niektoré z tých miest.
- Čísla riadkov v správach sa počítajú od začiatku textu, ktorý sa Tidy poslal.
- Písmená mimo ASCII sa ako entity nepíšu; drží ich kódovanie súboru.

## [help.language] Pomocník k jazyku
commands: nks.help.languageHelp
1. Dajte kurzor na slovo, napríklad názov funkcie, a stlačte Alt+F1.
2. Editor slovo vyhľadá v zdrojoch pomocníka nastavených pre jazyk súboru. Súbor pluginu sa otvorí v editore. Webová adresa sa najprv ukáže a v prehliadači sa otvorí až po potvrdení.
3. Keď je pre jazyk viac zdrojov, vyberiete jeden.
- Zdroje sú dáta. Plugin bez programu ich vypíše v `languageHelp` v `nkspad-plugin.json`, každý s `id`, `title`, `language` (id jazyka, napríklad `php`), `kind` (`url` alebo `file`) a `target`. Vo webovej adrese `${word}` znamená slovo a `${language}` id jazyka. Cieľ súboru je cesta vnútri priečinka pluginu.
- Prijímajú sa len adresy `http` a `https`; plugin s inou adresou sa pri načítaní odmietne.
- Žiadne zdroje zatiaľ nie sú pribalené. Keď pre jazyk žiadny nie je, stavový riadok to povie.

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

## [table.copyAs] Kopírovanie buniek ako Markdown, SQL, JSON alebo HTML
commands: nks.table.copyAs
Označte bunky v tabuľkovom zobrazení a zvoľte Zobrazenie, Kopírovať tabuľku ako a potom podobu. Do schránky sa uložia označené stĺpce a riadky v tej podobe.
- Tabuľka Markdown: tabuľka s oddeľovačmi zvislíc, s hlavičkou (alebo s písmenami stĺpcov A, B, ... bez hlavičky). Zvláštne znaky, napríklad zvislica, spätná lomka, hviezdičky, podčiarkovníky a špicaté zátvorky, dostanú spätnú lomku a koniec riadka v bunke sa zmení na `<br>`.
- SQL INSERT: jeden príkaz na riadok, hodnoty ako text v jednoduchých úvodzovkách (úvodzovka sa zdvojí). Názvy stĺpcov sú z hlavičky. Program sa spýta na názov tabuľky, ponúkne názov súboru.
- JSON: pole objektov pomenovaných podľa hlavičky, alebo pole polí, keď hlavička chýba, má prázdny názov alebo sa názov opakuje. Všetky hodnoty sú text.
- Tabuľka HTML: prvok table s entitami pre `& < > " '`; schránka drží HTML ako zdrojový text aj ako HTML na vloženie do dokumentu.
- Skrytá hodnota súboru .env sa skopíruje ako hviezdičky, nie ako jej text. Kopírovanie a vystrihnutie klávesmi Ctrl+C a Ctrl+X dá na jej miesto tiež zástupný znak a vystrihnutie, ktoré sa jej dotkne, nič nezmaže.
- Ak sa súbor počas príkazu zmení, nič sa neskopíruje a oznámenie vyzve na nové spustenie.

## [table.region] Úprava vybraného textu ako tabuľky
commands: nks.table.editRegion
Vyberte časť textu, napríklad blok riadkov oddelených čiarkami v poznámke alebo v logu, a zvoľte Zobrazenie, Upraviť výber ako tabuľku.
1. Dialóg sa spýta na oddeľovač (čiarka, bodkočiarka, tabulátor alebo zvislá čiara), či úvodzovky ohraničujú hodnoty a či je prvý vybraný riadok záhlavie. Náhľad ukáže prvé riadky ako bunky.
2. Vybraný text sa stane tabuľkou. Zvyšok súboru zostane textom: úprava bunky zmení len bajty tej bunky a nič mimo výberu sa nečíta ani nemení.
- Funguje v ňom všetko z tabuľky: pohyb, úpravy, pridávanie a mazanie riadkov a stĺpcov, filter, kopírovanie a vkladanie. Stavový riadok hovorí, že tabuľkou je len vybraný text.
- Riadky pridané na koniec patria do výberu. Prepnúť tabuľku a text vráti text; ďalšie Prepnúť tabuľku a text ukáže ako tabuľku celý súbor.

## [compare.text] Porovnanie textov
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts
Dva texty sa porovnajú po riadkoch v samostatnej karte. Rovnaké riadky stoja oproti sebe, zmenený blok je na oboch stranách podfarbený a označený v strednom pruhu a lišta hore hovorí, koľko je rozdielov.
1. Porovnať so schránkou (ponuka Nástroje alebo kontextová ponuka editora) porovná kartu s textom v schránke. Ak je v editore vybraný text, porovná sa len výber.
2. Porovnať so súborom sa opýta na súbor a porovná s ním kartu. Porovnať s diskom je v ponuke Súbor a porovná kartu s jej súborom.
3. Porovnať dva texty otvorí dve prázdne strany. Tlačidlá Vložiť ľavý text a Vložiť pravý text vložia do strany obsah schránky.
- Z príkazového riadka otvorí textida --diff ľavý pravý porovnanie dvoch súborov: ľavý súbor sa otvorí v karte a môže prijímať bloky z pravého, ktorý je len na čítanie.
- Strana s kartou je samotný dokument: keď sa zmení, porovná sa znova po krátkej pauze. Schránka, výber a súbor na disku sú strany len na čítanie.
- Iný koniec riadku (CR, LF, CRLF) riadok nezmení. Medzery a veľkosť písmen áno.
- Veľký súbor v stránkovanom režime sa porovnáva bez načítania: čítajú sa len riadky na obrazovke.
- Každá strana môže mať medzi spoločným začiatkom a spoločným koncom až 16 miliónov riadkov; viac je jeden zmenený blok. Až 10 000 zmenených riadkov sa nájde presne; pri väčšom počte lišta napíše, že výsledok nie je minimálny, a zmenené riadky sa zlúčia do väčších blokov. Výsledok je stále správny.

## [compare.navigate] Prechod medzi rozdielmi a ich kopírovanie
commands: nks.compare.next, nks.compare.previous, nks.compare.copyToRight, nks.compare.copyToLeft
1. F7 skočí na ďalší rozdiel a Shift+F7 na predchádzajúci (aj v ponuke Nástroje). Aktuálny rozdiel má rámik.
2. V strednom pruhu má prvý riadok každého rozdielu dve šípky. Ľavá skopíruje blok pravej strany doľava, pravá skopíruje blok ľavej strany doprava. To isté pre aktuálny rozdiel robia tlačidlá nad stranami a príkazy Skopírovať rozdiel doprava a doľava.
3. Kópia je jedna úprava dokumentu, takže Späť ju vráti jedným krokom.
- Blok môže prijať len strana, ktorá je otvorený dokument. Súbor na disku, schránka a výber šípku nemajú.
- Keď sa dokument po porovnaní zmenil, nič sa neskopíruje; porovná sa znova a skopírujete to ešte raz.
- Keď kódovanie cieľa nepojme znaky bloku, nič sa neskopíruje a stavový riadok to napíše.
- Koliesko myši posúva obe strany spolu, Shift a koliesko posúva do strán.

## [view.syncScroll] Synchronizované posúvanie
commands: nks.view.syncScroll
Dve karty sa posúvajú spolu, takže rovnaký text dvoch súborov zostáva oproti sebe.
1. Zobrazte oba súbory vedľa seba: v ponuke karty zvoľte Presunúť do inej skupiny, aby mal každý súbor svoju skupinu.
2. Zvoľte Zobraziť a potom Synchronizovať posúvanie. Aktívna karta a aktívna karta ďalšej skupiny sa sledujú. Ďalším zvolením príkazu to vypnete.
- Riadky sa priraďujú porovnaním z témy Porovnanie textov: druhý súbor sa posunie na zodpovedajúci riadok, aj cez vložené alebo odobraté bloky. Kým sa porovnanie dopočíta, platí rovnaké číslo riadka.
- Skončí, keď sa zavrie jedna z dvoch kariet. Hexadecimálny a tabuľkový pohľad sa nezúčastňujú.

## [tools.mcp] Editor ako server MCP
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpTools
Asistent s umelou inteligenciou, ktorého na tomto počítači spustíte (napríklad programovací agent), môže Textidu používať cez Model Context Protocol. Vidí len karty, ktoré mu zdieľate, a nikdy nezapisuje: každý návrh uvidíte ako náhľad zmien a rozhodnete vy.
1. Zvoľte Nástroje, Server MCP. Dialóg vypíše, čo asistent dostane. Zapnutím sa server spustí; ďalším zvolením príkazu sa vypne a všetky karty sa odvolajú.
2. Asistenta nastavte tak, aby spúšťal program textida-mcp.exe z priečinka mcp vedľa editora (celú cestu ukáže stavový riadok). S editorom komunikuje cez miestnu rúru, žiadny sieťový port sa neotvára.
3. Otvorte kartu a zvoľte Nástroje, Zdieľať kartu s MCP. Rovnakým príkazom kartu odvoláte. Karta, ktorá nie je zdieľaná, nie je ani vypísaná.
4. Voľbou Nástroje, Nástroje MCP zapnete alebo vypnete jednotlivé nástroje. Výber nástroja v zozname zmení jeho stav, Escape zoznam zavrie.
- Asistent číta text tak, ako je v editore, aj neuložený, hľadá v ňom, žiada osnovu, kódovanie a konce riadkov, hľadá riadky danej úrovne v logu ľubovoľnej veľkosti a číta alebo hľadá bajty (najviac 64 KiB naraz).
- Zmena je návrh pre jednu revíziu textu. Ak sa text medzitým zmenil, návrh sa odmietne a asistent si musí text prečítať znova. Návrh uvidíte ako iné zmeny celého dokumentu a zvolíte, či ho použiť; asistent sa môže opýtať, ako dopadol.
- Hodnoty v súboroch .env vidí asistent ako *. Pri zdieľaní takej karty sa opýtame, či smie vidieť skutočné hodnoty; odpoveď platí len pre tú kartu.
- Odvolanie karty alebo vypnutie servera zastaví požiadavky, ktoré práve bežia. Odvolá ju aj opätovné načítanie súboru.
- Program pre asistenta hovorí aktuálnou verziou MCP (2026-07-28) a pre asistentov, ktorí ešte začínajú príkazom initialize, verziou 2025-11-25.

## [tools.mcpAgents] Servery MCP agentov
commands: nks.tools.mcpServers
Jeden zoznam serverov MCP, s ktorými sú nastavení vaši programovací agenti. Textida číta Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) a OpenCode (opencode.json, opencode.jsonc). Súbory projektu hľadá v priečinku aktívneho súboru a v nadradených priečinkoch. Súbory iba číta, nikdy do nich nezapisuje.
1. Zvoľte Nástroje, Servery MCP agentov. Každý riadok ukazuje server, agenta a súbor, odkiaľ pochádza, a jeho príkaz alebo adresu so skrytými tajnými hodnotami.
2. Vyberte server a potom Vyskúšať. Okno ukáže presný príkaz, priečinok a premenné s hodnotami skrytými (***). Nič sa nespustí, kým nestlačíte Spustiť.
3. Po Spustiť Textida program spustí, opýta sa ho na nástroje (MCP verzie 2026-07-28 a 2025-11-25) a ukončí ho. Výsledok povie, koľko nástrojov server ponúka, alebo ukáže chybu a posledné riadky, ktoré program napísal na chybový výstup. Rovnaké riadky sú vo výstupe Úlohy.
4. Voľba Previesť záznam pre iného agenta otvorí záznam vo formáte toho agenta v novej karte. Úryvok sa najprv skontroluje podľa toho formátu. Hodnoty premenných a hlavičiek sú nahradené ***, doplňte ich, kým úryvok vložíte do súboru toho agenta.
- ${NAME} v príkaze, argumente alebo premennej sa nahradí z prostredia Textidy, ${NAME:-text} dá predvolenú hodnotu. Meno, ktoré tam nie je, sa hľadá v Správcovi poverení Windows pod textida:mcp:env:NAME. Meno bez hodnoty je chyba a nič sa nespustí.
- Vyskúšať ide iba server spúšťaný príkazom. Server s adresou sa v zozname ukáže a ide previesť, ale nespúšťa sa. npx a npm sa spúšťajú ako node so skriptom zástupcu Node.js, pretože Textida nespúšťa dávkové súbory.
- Tajné hodnoty sú skryté v zozname, v okne, vo výstupe aj v úryvkoch, tiež keď sa objavia vnútri argumentu alebo v chybovom výstupe programu. Hodnoty kratšie ako 4 znaky sa v texte nehľadajú.

## [review.external] Kontrola zmien zvonka
commands: nks.review.open, nks.review.next, nks.review.previous
Keď iný program (skript, asistent, nástroj zostavenia) zmení súbor karty bez neuložených úprav, panel ponúkne Skontrolovať zmeny.
1. Zvoľte Skontrolovať zmeny. Karta prevezme nový obsah, skorší obsah zostane v miestnej histórii a po dobu kontroly sa nezmaže a karta Výsledky hľadania vypíše zmeny po riadkoch.
2. Zaškrtnite zmeny, ktoré chcete ponechať. Nezaškrtnuté, alebo celý súbor s nezaškrtnutým poľom, sa po voľbe Použiť vrátia na skorší obsah.
3. Použiť zapíše vrátený text do karty ako jeden krok späť a súbor uloží; bajty mimo vrátených zmien zostanú bez zmeny.
- Karta ukazuje štítok s počtom čakajúcich zmien. Voľbou Súbor a Skontrolovať zmeny zvonka zoznam otvoríte znova; vráti sa aj po reštarte.
- Alt+F7 a Alt+Shift+F7 skočia na ďalšiu a predchádzajúcu zmenu zvonka.
- Ak počas kontroly v karte píšete, zmeny sa ukážu znova z nového textu.
- Súbory s tajnými hodnotami (.env) a súbory s neuloženými úpravami zostávajú pri staršom paneli: Načítať znova, Ponechať moje, Porovnať.
- S voľbou Ponechať zmeny zvonka bez kontroly v nastaveniach karta prevezme nový obsah hneď. V predvolenom stave je vypnutá.

## [start] Začínáme
commands: nks.help.contents
Tato nápověda leží přímo v programu a nepotřebuje internet. Otevřete ji klávesou F1 nebo z nabídky Nápověda. F1 funguje v každém okně, dialogu a panelu: stiskněte ho u vybraného příkazu v paletě příkazů, uvnitř panelu (hledání, soubory, dokumenty, výstup, náhled, agent, hex panely), v liště nad dokumentem, v dialogu nebo v jiném okně programu a otevře se rovnou téma, které sedí.
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
2. Okno se otevře na tématu podle toho, kde se právě nacházíte: u vybraného příkazu v paletě na jeho tématu, v hledání, v panelu, v pohledu (hex, tabulka, porovnání, velký soubor) nebo v liště nad dokumentem na tématu té části, v dialogu na tématu příkazu, který ho otevřel, jinak na úvodu.
3. F1 v nastavení, ve zkoušce regulárního výrazu, v editoru nástrojů, ve zkoušce vzoru LOGu, v binárním porovnání a v tomto okně nápovědy otevře téma o tom okně.
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

## [remote.connections] Vzdálená připojení
commands: nks.remote.connections
Připojení si pamatuje server, uživatele a protokol. Heslo je v systémové klíčence operačního systému a nikdy se nezapíše do souboru nastavení ani do zálohy.
1. Zvolte Soubor a pak Vzdálené soubory a pak Vzdálená připojení. Seznam ukáže uložená připojení.
2. Zvolte Nové připojení a odpovězte na otázky: název, protokol, server, port, uživatel a heslo. U SFTP lze místo hesla zadat soubor se soukromým klíčem.
3. Vyberte připojení, otevřete z něj soubor, nebo ho smažte i s uloženým heslem.
- Běžná volba je FTPS a SFTP. Prosté FTP posílá heslo i soubory nešifrovaně a použije se, až když ho pro dané připojení výslovně povolíte.
- Při prvním připojení program ukáže identitu serveru (klíč SSH nebo certifikát TLS) a zeptá se, zda jí věříte. Porovnejte ji s tou, kterou vám dal správce serveru.
- Změní-li se identita serveru, kterému jste uvěřili, program se nepřipojí, dokud novou nepotvrdíte. Než to uděláte, přečtěte si varování.
- Ukládání hesla funguje ve Windows (Správce přihlašovacích údajů). V macOS a Linuxu systémová klíčenka zatím podporovaná není a program to řekne, místo aby heslo uložil jinam.

## [remote.open] Otevřít vzdálený soubor
commands: nks.remote.open
1. Zvolte Soubor a pak Vzdálené soubory a pak Otevřít vzdálený soubor a vyberte připojení.
2. Napište cestu na serveru. Cesta ke složce ukáže její obsah: složku otevřete, aby se vstoupilo dovnitř, soubor, aby se otevřel.
3. Soubor se zkopíruje do tohoto počítače a otevře v kartě. Upravujte a ukládejte jako obvykle: Uložit mění jen kopii v tomto počítači.
- Lišta nad textem říká, zda je kopie shodná se serverem. Editor sám funguje bez připojení.
- Vzdálený soubor, který už kopii má, se otevře z kopie, včetně změn, které jste ještě neposlali. Verze ze serveru se nikdy nestáhne přes vaši práci.
- Záznam přenosů je na kartě Vzdálené v okně výstupu.

## [remote.upload] Nahrát na server
commands: nks.remote.upload
1. Zvolte Soubor a pak Vzdálené soubory a pak Nahrát na server. Změněná karta se nejdřív uloží.
2. Než program cokoli pošle, podívá se na soubor na serveru. Je-li to pořád verze, kterou jste otevřeli, nový obsah se pošle pod dočasným jménem a teprve pak nahradí starý soubor. Nepovedené nebo přerušené nahrání nechá na serveru starou verzi.
3. Změnil-li se soubor na serveru mezitím, nepošle se nic. Zvolte Zobrazit rozdíly, uvidíte verzi ze serveru vedle své, převezměte do své strany, co potřebujete, uložte a nahrajte znovu. Přesto nahrát nahradí právě tu verzi serveru, kterou jste viděli.
- Nastavení Kontrola verze na serveru určuje, jak se změna pozná: stat porovnává velikost a čas změny, content vždy stáhne soubor ze serveru a porovná ho; pozná i změnu ve stejné vteřině a se stejnou velikostí.
- Mez: kontrola a výměna jsou dva kroky, protože FTP ani SFTP nemají podmíněný zápis. Změna, kterou někdo udělá v tom posledním okamžiku, se nepozná. Kontrola běží až po dokončeném nahrání, takže ten okamžik je krátký.
- Přeruší-li se spojení během nahrávání, program to několikrát zopakuje s rostoucími pauzami. Každý pokus nejdřív znovu zkontroluje verzi na serveru a částečně poslaný dočasný soubor se doplní od místa přerušení, až když se jeho obsah shoduje s vaším souborem. Odmítne-li server přejmenování, kterým se soubor dává na místo, starý soubor se odloží, nový se přesune a při selhání se starý vrátí.

## [remote.cancel] Zrušit přenos
commands: nks.remote.cancel
Zvolte Soubor a pak Vzdálené soubory a pak Zrušit přenos. Běžící nahrávání se zastaví, server si ponechá předchozí verzi a částečně poslaný dočasný soubor doplní další nahrávání.

## [remote.check] Zkontrolovat verzi na serveru
commands: nks.remote.check
Zvolte Soubor a pak Vzdálené soubory a pak Zkontrolovat verzi na serveru. Lišta řekne: shodné se serverem, změněno tady a neodesláno, změněno na serveru, změněno na obou místech, nebo soubor už na serveru neexistuje. Nic se nestahuje ani nemění.

## [remote.unsent] Neodeslané vzdálené změny
commands: nks.remote.unsent
Změny vzdáleného souboru se nejdřív uloží v tomto počítači. Seznam ukáže vzdálené soubory, jejichž kopie je změněná a neodeslaná, i po zavření programu nebo po pádu. Vyberte jeden a otevře se kopie; lišta nabídne Nahrát.
- Úpravy nepotřebují připojení. Neodeslané změny zůstanou, dokud je nenahrajete.

## [remote.cache] Porovnat mezipaměť se serverem
commands: nks.remote.pairCache
Kopie vzdálených souborů zůstávají v tomto počítači s verzí ze serveru, ze které vznikly, i po zavření programu.
1. Zvolte Soubor a pak Vzdálené soubory a pak Porovnat mezipaměť se serverem a vyberte připojení.
2. Program se připojí a porovná každou kopii toho připojení se serverem podle uložené verze. Seznam u každého souboru řekne: shodné se serverem, změněno tady a neodesláno, změněno na serveru, změněno na obou místech, nebo soubor už na serveru není.
3. Vyberte soubor a otevře se jeho kopie. Lišta ji ještě jednou zkontroluje a nabídne Nahrát, Zobrazit rozdíly nebo Znovu načíst ze serveru.
- Nic se nestahuje ani nepřepisuje. Seznam nabízí jen připojení, která mají kopie.

## [source.open] Otevřít ze zdroje pluginu
commands: nks.source.open
1. Zvolte Soubor a potom Otevřít ze zdroje. Seznam ukazuje zdroje, které pluginy nabízejí, a co každý umí: zda zapisuje, zda chrání změněný soubor a zda vypisuje složky.
2. Vyberte zdroj a napište adresu, která začíná schématem zdroje.
3. Text se otevře v kartě. Lišta nad ním ukazuje zdroj, adresu s tajnými částmi nahrazenými ***, a revizi, ve které byl text načten.
- Uložit zapíše text zpět jen tehdy, když je soubor ve zdroji stále v té revizi. Pokud se změnil, nic se nezapíše a váš text zůstane v kartě. Zobrazit rozdíly je porovná, Přepsat nahradí přesně nalezenou verzi, Zrušit nechá vše jak je.
- Zdroj, který neumí zkontrolovat revizi v jednom kroku, to říká, a program před každým uložením varuje: nejdřív zkontroluje revizi a potom zapíše, takže změna provedená mezitím se přepíše bez upozornění.
- Když zdroj není dostupný, karta se otevře z kopie, kterou program drží v paměti, a uvádí Offline. Načíst znovu ze zdroje to zkusí znovu a nikdy nenahradí neuložené změny.
- Adresy s heslem, tokenem nebo klíčem se na liště, v nedávných souborech a v logu ukazují s ***. Nedávné soubory drží jen zamaskovanou adresu, takže zdroj, který tajnou část v adrese potřebuje, si ji vyžádá znovu.
- Omezení: tajná část uvnitř cesty adresy se nepozná a zůstane vidět. Uložit jako zapíše kopii na disk a ukončí vazbu na zdroj.
- Zdroj, který hledá (seznam to říká), přijme kromě adresy i slova. Slova dají seznam nalezených stránek a výběr stránku otevře; adresa, která začíná schématem zdroje, se otevře hned.
- Výstup Vzdálené říká u každého otevřeného a uloženého souboru, jaký adaptér a jakou službu zdroj uvedl, například: adaptér 1.0.0, služba NKS IS API v1.
- Wiki NKS IS (nksis://wiki/stránka): poprvé se program zeptá na adresu wiki a na klíč API. Obojí se uloží do úložiště přihlašovacích údajů systému, nikdy do souboru. Klíč, který wiki odmítne, se zapomene a hned se zeptá na nový; když je odmítnut i ten, volání skončí zprávou. Wiki neumí při zápisu zkontrolovat revizi, takže program před každým uložením varuje, jak je popsáno výše. Stránku, která ještě neexistuje, odtud vytvořit nejde.

## [plugins.virtual] Karty, které dodá plugin
Plugin může otevřít vlastní kartu s daty, která si drží: přepis konverzace, vygenerovaný soubor, výpis nebo rozdíl. Taková karta nemá soubor. Nese štítek („virtuální · jen čtení“ nebo „virtuální · upravitelná“) a lištu, která jmenuje plugin a adresu, kterou mu dal (adresa s heslem nebo tokenem ukazuje na jejich místě ***).
1. V kartě jen pro čtení jde hledat, vybírat a kopírovat a má osnovu jako každý soubor svého jazyka, ale nejde do ní psát ani ji uložit. Uložit i Uložit jako to řeknou ve stavovém řádku.
2. Do upravitelné karty jde psát, včetně Zpět a Znovu. Plugin se o každé úpravě dozví sám. Uložit (i když kartu zavřete a zvolíte Uložit) pošle text pluginu, nikdy do souboru; když plugin odmítne nebo neodpoví včas, karta zůstane neuložená a stavový řádek uvede důvod. Uložit jako není k dispozici.
3. Plugin může text své karty nahradit. Nahradí tím i to, co jste napsali a neuložili, a skončí tím historie zpět karty; plugin, který tomu chce předejít, uvede revizi, kterou viděl naposledy, a změna, kterou jste mezitím udělali, pak nahrazení zastaví.
4. Když plugin svou kartu zavře, skončí nebo je vypnut, karta se zavře bez ptaní a stavový řádek to řekne. Neuložený text je ztracen.
- Karta pluginu se neukládá do relace, do žurnálu ani do nedávných souborů.
- Hodnoty v kartě .env jsou skryté jako v souboru. Plugin na jejich místě vidí hvězdičky, pokud jste mu to nepovolili.
- Počet takových karet na plugin, jejich velikost a čekání na uložení jsou zatím provizorní.

## [remote.backup] Záloha připojení
commands: nks.remote.exportConnections, nks.remote.importConnections
1. Zvolte Soubor a pak Vzdálené soubory a pak Exportovat připojení a pojmenujte soubor. Obsahuje seznam připojení: název, protokol, server, port, uživatele a důvěryhodnou identitu serveru.
2. Zvolte Soubor a pak Vzdálené soubory a pak Importovat připojení a načtete takový soubor na jiném počítači nebo po přeinstalaci.
- Soubor neobsahuje hesla, přístupové fráze ani tokeny, takže ho lze sdílet. Po importu vyberte připojení a zvolte Nastavit heslo.
- Importované připojení nahradí připojení se stejným vnitřním identifikátorem.
- Celou konfiguraci včetně připojení přenesete přes Nastavení a pak Zálohovat konfiguraci (téma Záloha konfigurace).

## [remote.proxy] Síťová proxy
commands: nks.remote.proxy
1. Zvolte Soubor a pak Vzdálené soubory a pak Síťová proxy a vyberte HTTP, SOCKS4, SOCKS5 nebo žádnou proxy.
2. Zadejte server a port, a podle potřeby uživatele a heslo proxy. Heslo se uloží do systémové klíčenky.
- Proxy se používá pro FTPS, SFTP, kontrolu aktualizací a požadavky, které pluginy posílají přes editor (oprávnění network). Připojení, které má vlastní proxy, si ji ponechá.
- Plugin, který si spojení otevře sám, proxy obejde. Dotazy do registrů balíčků proxy použijí, ale ne proxy s uživatelským jménem.
- SOCKS4 funguje jen pro SFTP. Pro FTPS použijte SOCKS5 nebo HTTP.
- Jiný typ proxy program odmítne se zprávou.

## [file.locks] Zamčené soubory
Soubor se otevře jen pro čtení, když ho má otevřený jiný program a nedovoluje ostatním zapisovat, nebo když ho upravuje jiné okno Textidy. Lišta nad textem uvede důvod.
- Zkusit znovu zámek zkontroluje znovu, například po zavření souboru v jiném programu.
- Přesto upravit zapne pro soubor souběžný zápis. Každé uložení pak porovná soubor na disku s tím, co jste otevřeli, a zeptá se, když se změnil; nic se nepřepíše potichu.
- Zámek okna zaniká s oknem, i po pádu. Textida nezapisuje žádné soubory zámků k vašim dokumentům a soubory .lck jiných programů ignoruje.

## [file.history] Místní historie
commands: nks.file.history
Program uchovává revizi souboru při každém uložení a při každé změně, kterou soubor provede jiný program. Nepotřebuje projekt ani Git.
1. Zvolte Soubor a pak Historie a pak Místní historie. Seznam ukazuje revize souboru z karty, nejnovější první, s důvodem, velikostí a štítkem.
2. Vyberte revizi. Porovnat ji ukáže vlevo a kartu vpravo v kartě s porovnáním (téma Porovnat s diskem); je-li nastavený Externí porovnávač, spustí se místo toho on. Obnovit soubor vloží do karty celou revizi. Obnovit výběr nahradí jen vybrané řádky odpovídajícími řádky revize. Štítek revizi pojmenuje.
- Obnova je úprava v kartě; Zpět ji vrátí a nic se nezapíše, dokud neuložíte. Text, který v kartě byl, zůstane jako revize.
- Revize leží ve složce profilu, takže je aktualizace programu nesmaže.
- Soubory s tajnými hodnotami, například .env, se neukládají, dokud v nastavení nezapnete Ukládat historii souborů s tajnými hodnotami.
- Limity (revizí na soubor, dny, MiB na soubor, MiB celkem) se nastavují v nastavení; 0 znamená bez omezení. Nejnovější revize souboru se nikdy nesmaže.
- Když je disk plný, stavový řádek to oznámí a žádná revize se nepoškodí.
- Připnutou revizi limity nikdy nesmažou; Připnout v nabídce revize ji drží, Odepnout ji uvolní. Soubor nad limitem velikosti revize nedostane a stavový řádek to oznámí. Staré revize se uklízejí na pozadí.

## [file.historyMark] Označit revizi
commands: nks.file.historyMark
1. Zvolte Soubor a pak Historie a pak Označit revizi a napište štítek, například Před refaktorem.
2. Současný text karty se uloží jako revize s tímto štítkem. Pokud poslední revize už obsahuje stejný text, štítek dostane ona.
- Revizi později najdete v Místní historii podle štítku.

## [file.historySearch] Hledat v historii
commands: nks.file.historySearch
1. Zvolte Soubor a pak Historie a pak Hledat v historii a napište text.
2. Seznam ukáže revize souboru, které text obsahují, s počtem shod a prvním řádkem. Vyberte jednu a otevřou se akce, například Porovnat.
- Revize souborů s tajnými hodnotami se neprohledávají.

## [file.openPath] Otevřít cestu pod kurzorem
commands: nks.file.openPathAtCaret
1. Dejte kurzor na cestu k souboru v textu, například src/a.cs:12.
2. Zvolte Soubor a Otevřít cestu pod kurzorem, nebo příkaz najděte v paletě. Zkratku nemá.
3. Soubor se otevře; za dvojtečkou uvedený řádek (a případně sloupec) se použije.
- Relativní cesta se hledá ve složce aktuálního souboru a v otevřené složce. Cesta začínající ~/ míří do vaší domovské složky.
- Míří-li cesta na složku, ukáže se v panelu Soubory.
- Pod kurzorem není cesta: Pod kurzorem není cesta k souboru. Soubor neexistuje: napíše se cesta a že neexistuje.

## [file.openSpecial] Zvláštní otevření
commands: nks.open.special, nks.open.hex, nks.open.withEncoding, nks.format.reread, nks.open.readOnly, nks.open.newCopy, nks.file.openPathAtCaret, nks.search.relatedFile, nks.open.containingFolder, nks.open.defaultApp, nks.open.lastClosed
1. Zvolte Soubor a pak Zvláštní otevření, nebo spusťte Zvláštní otevření v paletě. V panelu souborů klikněte na soubor pravým tlačítkem a zvolte Zvláštní otevření.
2. Vyberte, jak soubor otevřít. Z panelu se použije vybraný soubor, jinak soubor aktuální karty; když žádný není, vyberete ho.
- Otevřít soubor v hex, Otevřít s kódováním (seznam kódování se otevře po souboru), Otevřít jen pro čtení (karta neukládá), Otevřít jako novou kopii (neuložený dokument s textem souboru).
- Znovu otevřít s kódováním, Otevřít cestu pod kurzorem a Související soubor pracují s aktuální kartou.
- Otevřít složku souboru ukáže složku ve správci souborů systému; Otevřít výchozí aplikací předá soubor programu, který mu systém přiřadil.
- Znovu otevřít naposledy zavřenou kartu otevře soubor karty zavřené jako poslední; zavřené soubory, které už neexistují, se přeskočí.
## [app.settings] Nastavení
commands: nks.app.openSettings
1. Stiskněte Ctrl+čárka, nebo zvolte Nastavení a Nastavení.
2. Do pole nahoře napište, co hledáte, například tab nebo zkratka. Hledá se v nastaveních i v klávesových zkratkách.
3. U každé položky je vidět platná hodnota a odkud pochází (výchozí, uživatel, pracovní prostor, EditorConfig, dokument).
4. Hodnotu změňte přímo v řádku; projeví se hned a uloží se do souboru nastavení. Tlačítko Obnovit výchozí vrátí původní hodnotu.
- Nesmyslná hodnota se odmítne a řádek napíše, co s ní je. Nejde-li soubor nastavení zapsat, nad seznamem je důvod.
- Konflikty zkratek jsou vidět u příslušných řádků.
- F1 v tomto okně otevře toto téma.

## [app.accessibility] Přístupnost
commands: nks.view.nextPane, nks.view.previousPane
Program jde ovládat jen klávesnicí a se čtečkou obrazovky. Ověřuje se přes přístupnostní strom programu; s NVDA ani VoiceOverem samotným zatím vyzkoušený není, takže napište, kde čtečka čte něco špatně.
### Klávesnice
- Každý příkaz je v paletě příkazů (Ctrl+Shift+P) a, pokud ho má, i v nabídce. K liště nabídek se dostanete klávesou Alt ve Windows; na macOS je to lišta nabídek systému (Ctrl+F2).
- Editor si nechává klávesu Tab pro sebe. F6 přesune klávesnici na další část okna: seznam vlevo, dokument, panely náhledu, agenta a výstupu, lištu hledání, stavový řádek. Shift+F6 jde zpět. Zavřená část se přeskočí.
- V ostatních částech se Tab a Shift+Tab pohybují mezi ovládacími prvky shora dolů a zleva doprava; v seznamu se mezi řádky pohybujete šipkami. Esc zavře dialog, paletu a panely a vrátí klávesnici do dokumentu.
- Po zavření dialogu se klávesnice vrátí na prvek, který ji měl předtím.
### Co dostane čtečka obrazovky
- Každý ovládací prvek, okno a dialog má jméno a roli ve všech čtyřech jazycích rozhraní (čeština, angličtina, slovenština, němčina). Jména se při změně jazyka změní hned.
- Zpráva ve stavovém řádku, upozornění nad dokumentem, počet shod při hledání, vybraný řádek v paletě a stav agenta se přečtou, jakmile se objeví, aniž by se hnula klávesnice.
### Vysoký kontrast
- Když systém žádá vysoký kontrast (vysoce kontrastní motiv ve Windows), program pro okna, karty, seznamy, rámečky a rámeček fokusu použije barvy systému, i v editoru. Text vybraného řádku má barvu, kterou pro něj systém určuje. Na macOS to platí, když systém tento údaj programu sdělí (volba Zvýšit kontrast).
- Barvy zvýraznění syntaxe se nemění. Fokus klávesnice je rámeček kolem ovládacího prvku v barvě akcentu (ve vysokém kontrastu v barvě systému pro odkazy).

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
- Vyjmout a Vložit nejdou u dokumentu jen pro čtení. Ve stránkovaném režimu fungují všechny tři (téma Velké soubory); výběr větší než tamní limit kopírování (256 MiB, zatím provizorní hodnota) se nezkopíruje a stavový řádek to řekne.

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

## [edit.specialPaste] Vložit jako HTML a ze souboru
commands: nks.edit.pasteAsHtml, nks.edit.insertFromFile
- Vložit jako HTML (Úpravy, Vložit) vloží zdrojový kód HTML, který do schránky uložil prohlížeč nebo textový editor při kopírování formátovaného textu. Vloží se jako text, stejně jako při Ctrl+V, jako jeden krok zpět. Když ve schránce HTML není, řekne to stavový řádek.
- Vložit ze souboru (Úpravy, Vložit) vloží text vybraného souboru do každého kurzoru jako jeden krok zpět. Kódování souboru se pozná jako při otevření a konce řádků se převedou na konce řádků dokumentu. Soubor větší než 16 MiB se nevloží.
- Oba příkazy nefungují v dokumentu jen pro čtení ani ve stránkovaném režimu (téma Velké soubory).

## [edit.insertLines] Vložení do řádků
commands: nks.edit.insertIntoLines
Příkaz zapíše text, čísla nebo odrážky do řádků výběru, nebo do všech řádků, když je jen kurzor.
1. Zvolte Úpravy, Vložit, Vložit do řádků. Náhled ukáže první řádky, jak budou vypadat.
2. Zvolte Text, Čísla nebo Odrážky a kam psát: začátek každého řádku, konec každého řádku, před sloupec, nebo do každého kurzoru.
3. Stiskněte OK. Výsledek je jeden krok zpět.
- Čísla: První číslo, Počet číslic (číslo se zleva doplní nulami na tuto šířku) a Oddělovač, který leží mezi číslem a textem řádku (na začátku řádku za číslem, na konci před číslem). Do každého kurzoru se zapíše jen číslo, takže tři kurzory dostanou 1, 2 a 3.
- Začátek každého řádku píše na začátek řádku; Přeskočit odsazení píše před první viditelný znak. Před sloupec počítá znaky od 1 a kratší řádek dostane text na konec. Přeskočit prázdné řádky přeskočí i řádky jen z mezer a ty nespotřebují číslo.
- Konce řádků dokumentu se nemění. Nefunguje v dokumentu jen pro čtení ani ve stránkovaném režimu (téma Velké soubory).
- Opakovat poslední příkaz ho spustí znovu se stejnou volbou. V profilu PSPad je to Alt+I.

## [edit.sort] Řazení řádků
commands: nks.edit.sortLines
1. Vyberte řádky (nebo dejte kurzor kamkoli, aby se seřadily všechny) a zvolte Úpravy, Převody a recepty, Seřadit řádky.
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
commands: nks.edit.reformatParagraph, nks.edit.reformatShortLines, nks.edit.justifyLeft, nks.edit.justifyRight, nks.edit.justifyCenter, nks.edit.justifyBlock
Tyto příkazy jsou v menu Formát. Působí na vybrané řádky, nebo na všechny řádky, když je jen kurzor (Přeformátovat odstavec: na odstavec s kurzorem). Každý je jeden krok zpět.
- Přeformátovat odstavec zalomí text na sloupci z nastavení Sloupec pravého okraje, spojí řádky odstavce a odstraní nadbytečné mezery. Odstavce odděluje prázdný řádek a každý řádek odstavce dostane odsazení prvního řádku. Když není pravý okraj nastaven, řekne to stavový řádek.
- Přeformátovat odstavec (konec u krátkého řádku) dělá totéž, ale odstavec končí i za řádkem kratším než polovina pravého okraje, takže se nespojí s dalším řádkem. Hodí se pro text bez prázdných řádků mezi odstavci.
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

## [edit.macro] Makra
commands: nks.macro.record, nks.macro.stop, nks.macro.playLast, nks.macro.save, nks.macro.run, nks.edit.cursorLeft, nks.edit.cursorLeftSelect, nks.edit.cursorRight, nks.edit.cursorRightSelect, nks.edit.cursorWordLeft, nks.edit.cursorWordLeftSelect, nks.edit.cursorWordRight, nks.edit.cursorWordRightSelect, nks.edit.cursorUp, nks.edit.cursorUpSelect, nks.edit.cursorDown, nks.edit.cursorDownSelect, nks.edit.cursorLineStart, nks.edit.cursorLineStartSelect, nks.edit.cursorLineEnd, nks.edit.cursorLineEndSelect, nks.edit.cursorDocumentStart, nks.edit.cursorDocumentStartSelect, nks.edit.cursorDocumentEnd, nks.edit.cursorDocumentEndSelect, nks.edit.deleteLeft, nks.edit.deleteRight, nks.edit.deleteWordLeft, nks.edit.deleteWordRight, nks.macro.rename, nks.macro.delete
1. Nástroje, Makra, Nahrát makro, spustí nahrávání. Pracujte příkazy a psaním. Nástroje, Makra, Zastavit nahrávání makra, ho ukončí.
2. Nástroje, Makra, Přehrát poslední makro, ho znovu přehraje nad aktuálními výběry a kurzory.
3. Nástroje, Makra, Uložit poslední makro jako, ho uloží pod názvem do složky macros v profilu. Nástroje, Makra, Spustit uložené makro, ukáže uložená makra a přehraje vybrané.
4. Nástroje, Makra, Přejmenovat uložené makro, ukáže uložená makra a vybrané přejmenuje; název, který už má jiné makro, se odmítne. Nástroje, Makra, Smazat uložené makro, se zeptá a vybrané smaže natřvalo.
- Makro si pamatuje příkazy, ne klávesy: Duplikovat řádek, velikost písmen, Přidat další výskyt a podobné, Vyjmout, Kopírovat, Vložit, a napsaný text. Při více kurzorech udělá totéž jako ruční postup.
- Při nahrávání se jako příkazy zaznamenají i šipky, Home, End, Ctrl+Home, Ctrl+End, Ctrl se šipkami (se Shiftem i bez), Backspace a Delete. Posouvají a mažou u všech kurzorů.
- Najít další a Najít předchozí (F3, Shift+F3, Enter v liště hledání) si s krokem uloží hledaný text z lišty hledání, takže makro najde tentýž text i při pozdějším přehrání. Lištu hledání otevřete a hledaný text napište před začátkem nahrávání; bez hledaného textu se krok nenahraje.
- Nahradit a Vše v liště nahrazování (příkazy Nahradit další a Nahradit vše) si s krokem uloží celou lištu: hledaný text, přepínače a náhradu. Makro před krokem lištu nastaví na tyto hodnoty, takže výsledek nezávisí na tom, co lišta ukazovala předtím. Se zapnutým Výběrem krok pracuje s výběrem, který makro má v okamžiku kroku. Bez hledaného textu se krok nenahraje.
- Přehrání je jeden krok Zpět. Když krok neproběhne, makro se tam zastaví a stavový řádek krok pojmenuje; co proběhlo předtím, zůstává jedním krokem Zpět.
- Při nahrávání se příkaz, který makro nemůže přehrát, odmítne a stavový řádek to řekne: vše s dialogem, příkazy se soubory, otevření lišty hledání. Nahrávání pokračuje. Kliknutí myší, Page Up a Page Down se nenahrávají.
- Přehrání potřebuje upravitelný textový dokument. Import maker PSPadu není k dispozici.
- Výchozí zkratky jsou zástupné (OPEN, zatím nerozhodnuto): Ctrl+K Ctrl+R nahrává, Ctrl+K Ctrl+Shift+R zastaví, Ctrl+K Ctrl+P přehraje poslední makro; na macOS ⌘ místo Ctrl. Vaše vlastní zkratky v nastavení mají přednost.
- Soubor uloženého makra větší než 1 MB (zástupný limit, OPEN) se nečte; seznam ho ohlásí jako poškozený.

## [edit.json] Formátování JSON
commands: nks.edit.formatJson, nks.edit.previewFormatJson, nks.edit.minifyJson, nks.edit.sortJsonKeys, nks.edit.fixJsonCommas
1. Označte text JSON, nebo neoznačujte nic a příkaz se provede nad celým dokumentem.
2. Nabídka Úpravy, Formátování, Formátovat JSON, naformátuje označené části (každý výběr) nebo celý dokument. Všechny části jsou jeden krok Zpět; text mimo ně zůstane bajt po bajtu stejný, včetně konců řádků a kódování.
3. Náhled formátování JSON ukáže změnu nejdřív jako návrh. Použijte ho z lišty nad editorem, nebo ho zahoďte.
4. Sbalit JSON zapíše hodnotu na jeden řádek bez mezer a odstraní komentáře. Seřadit klíče JSON seřadí členy každého objektu podle názvu (komentář se přesune se svým členem) a text rozvrhne jako Formátovat JSON. Odstranit čárky na konci JSON odstraní jen čárky před zavírací závorkou a nic jiného nezmění.
- Komentáře a čárky na konci patří do souborů JSONC (.jsonc, tsconfig.json a podobné). Formátovat JSON i Seřadit klíče JSON je tam zachovají. V běžném souboru JSON se ohlásí jako chyby; čárky opraví Odstranit čárky na konci JSON.
- Chyba syntaxe se ohlásí s řádkem a sloupcem v dokumentu a nic se nezmění. Text větší než limit nebo vnořený hlouběji než limit se odmítne se zprávou.
- Výběr bez zalomení řádku převezme konec řádku, který má většina řádků souboru.
- Pokud se dokument po spuštění příkazu změnil, příkaz se zastaví a vyzve vás ke spuštění znovu.

## [tools.stats] Statistika dokumentu a hashe
commands: nks.tools.statistics, nks.tools.hash, nks.tools.hashCopy, nks.tools.hashCompare
1. Nabídka Nástroje, Text a výpočty, Statistika dokumentu, spočítá bajty, znaky, slova, řádky a nejdelší řádek celého dokumentu, nebo označených částí dohromady, a vypíše nejčastější znaky a slova.
2. Nabídka Nástroje, Text a výpočty, Spočítat hash, se zeptá na algoritmus a spočítá hash surových bajtů celého souboru nebo každé označené části. Seznam je stejný jako v tématu Hex: kontrolní součty a hashe (každé CRC, Adler-32, součty, xxHash32, xxHash64, XXH3, MD5, SHA-1, SHA-2). Spočítat hash a zkopírovat dá hash i do schránky. Porovnat hash s vloženým se zeptá na hash a řekne, jestli se shoduje; mezery, dvojtečky, 0x a velikost písmen nevadí. CRC-32, MD5, SHA-1, SHA-256, SHA-384 a SHA-512 se poznají podle délky vloženého hashe, u jiné délky algoritmus vyberete.
- Oba příkazy čtou dokument po blocích a vy můžete dál pracovat, ukazují průběh a jdou zrušit. Žádný nedrží celý soubor v paměti, takže fungují na souborech libovolné velikosti, i ve stránkovaném a hex zobrazení.
- Zrušená statistika se ukáže jako neúplná s počtem spočítaných bajtů. Zrušený hash výsledek nemá.
- Statistika i hash platí pro dokument, jaký byl při spuštění příkazu; úpravy během výpočtu je nemění.
- Slovo je souvislá řada písmen a číslic; slova se počítají malými písmeny. Seznam slov drží omezený počet různých slov a řekne, když je plný. Více označených částí se počítá jako jeden text.

## [tools.calculate] Výpočet bloku, výrazy a číselné soustavy
commands: nks.tools.calculateBlock, nks.tools.evaluateExpression, nks.tools.insertExpression, nks.tools.convertBase
1. Spočítat označený blok sečte čísla v označení (blokové označení, sloupec čísel) a ukáže počet, součet, nejmenší, největší a průměr.
2. Vyhodnotit výraz se zeptá na výraz, předvyplní označený text nebo řádek s kurzorem a ukáže výsledek. Vložit výsledek výrazu zapíše " = výsledek" za označení nebo řádek jako jeden krok zpět, s desetinnou tečkou bez ohledu na jazyk. Rozumí + - * / ^ ( ), MOD a funkcím ABS, ATAN, COS, EXP, LN, ROUND, SIN, SQRT, SQR a TRUNC. Čísla se píší s tečkou. Cokoli jiného, třeba volání příkazu systému, se odmítne a nic se nespustí.
3. Převést číselnou soustavu se zeptá na celé číslo, desítkově, s předponou 0x, 0b či 0o, nebo římsky, a ukáže ho desítkově, šestnáctkově, osmičkově, dvojkově, jako římské číslo (1 až 3999) a jako datum, kterým je při čtení jako milisekundy od 1. 1. 1970.
- Program pozná, jak jsou čísla v označení zapsána (čeština a slovenština: 1 234,50, němčina: 1.234,50, angličtina: 1,234.50). Když vyhovuje víc zápisů, třeba 1,234, vezme ten z jazyka programu. Výsledky se ukážou ve formátu jazyka programu. Změna jazyka dokument nezmění.
- Text v označení, který není číslo, se přeskočí a spočítá. Převod čísel funguje pro hodnoty od -9223372036854775808 do 18446744073709551615.
- Mocnina bere jen kladný základ. Výpočet používá čísla s dvojitou přesností.

## [tools.colors] Barvy
commands: nks.tools.pickColor, nks.tools.colorToHex, nks.tools.colorToRgb, nks.tools.colorToHsl, nks.tools.colorToOklch
1. V souborech CSS, HTML, XML, JavaScript, TypeScript, PHP, Razor a Latte má každá hodnota barvy (`#ff8800`, `rgb()`, `hsl()`, `oklch()`) před sebou malý vzorek své barvy. Vzorek nezabírá žádný znak dokumentu.
2. Klikněte na vzorek, nebo dejte kurzor na hodnotu a zvolte Nástroje, Barvy, Vybrat barvu u kurzoru. Otevře se výběr: posuvníky pro odstín, sytost, světlost a krytí a pole s hodnotou, které přijme i barvu v jiném zápisu. Kliknutí mimo výběr zapíše barvu přes hodnotu v zápisu, jaký měla, jako jeden krok zpět; Esc výběr zavře beze změny.
3. Nástroje, Barvy, Převést barvu na HEX, RGB, HSL nebo oklch přepíše hodnotu barvy u kurzoru, nebo všechny v označení, do zvoleného zápisu. Barva zůstane stejná osmibitová barva; oklch se píše na pět desetinných míst u světlosti a sytosti a na dvě u odstínu.
- Barva mimo rozsah sRGB se do něj ořízne. Krytí se zachová, jako `/ alfa` nebo jako poslední dvě hexadecimální číslice.
- Text, který jen vypadá jako barva, třeba `rgb(var(--c))`, entita HTML `&#123;` nebo `#12345`, se nechá být.
- Soubor větší než limit pluginu se nepřevádí; zpráva to řekne.

## [tools.markdown] Náhled Markdownu, tabulky a obsah
commands: nks.tools.markdownPreview, nks.tools.markdownOutline, nks.tools.markdownFormatTable, nks.tools.markdownUpdateToc, nks.tools.markdownRepairTable, nks.markdown.nextCell, nks.markdown.previousCell, nks.markdown.newRow
1. Nástroje, Markdown, Náhled Markdownu nebo HTML otevře pod editorem panel náhledu s aktuálním souborem. Panel sleduje psaní: překreslí se chvíli po poslední klávese a drží polohu posuvníku.
2. Markdown se ukáže jako stránka: nadpisy, seznamy, citace, kód, zvýraznění a tabulky s buňkami (dvojtečky v řádku pomlček určují zarovnání sloupce).
3. Soubor HTML se ukáže jako text, i se značkami. Nic z něj se nespouští a nic se nenačítá ze sítě.
4. Nástroje, Markdown, Zobrazit osnovu Markdownu vypíše v témže panelu nadpisy souboru, odsazené podle úrovně. Klik, nebo Enter na nadpisu, přesune kurzor v editoru na jeho řádek.
5. Nástroje, Markdown, Zarovnat tabulku v Markdowne přepíše tabulku pod kurzorem tak, aby měl každý sloupec šířku nejširší buňky. Značky `:--`, `:-:` a `--:` zůstanou a kratší řádek se doplní prázdnými buňkami. Zpět to vrátí jedním krokem.
6. Nástroje, Markdown, Aktualizovat obsah Markdownu zapíše odkazy na nadpisy mezi řádky `<!-- TOC -->` a `<!-- /TOC -->`. Mění se jen text mezi nimi. Když tam nejsou, přidají se na místo kurzoru. Kotvy jsou malými písmeny, mezery se mění na pomlčky a opakovaný nadpis dostane `-1`, `-2`.
7. V tabulce Tab vybere další buňku a Shift+Tab předchozí; sloupce se při tom zarovnají. Tab v poslední buňce přidá řádek. Mimo tabulku obě klávesy fungují jako dřív. Každý přesun vrátí Zpět jedním krokem.
8. V tabulce Shift+Enter přidá pod řádek s kurzorem prázdný řádek se správným počtem buněk. Nástroje, Markdown, Opravit tabulku v Markdownu spraví rozbitou tabulku: doplní chybějící řádek pomlček, doplní krátké řádky, ponechá dvojtečky a zarovná sloupce.
- Obrázek se kreslí jen z adresy `data:` s obrázkem PNG, JPEG, GIF, BMP nebo WebP. Vzdálený obrázek a obrázek z místního souboru se ukážou jako poznámka s popisem. Odkaz na web nebo e-mail se nejdřív potvrzuje; jiný odkaz je jen text.
- Soubor delší než limit náhledu se nekreslí; stavový řádek to oznámí. Esc nebo Zavřít náhled ukončí.
- V osnově jsou jen nadpisy psané přes `#`. Šířka buňky tabulky je šířka na obrazovce: široké východoasijské znaky se počítají za dva sloupce. Svislítko v buňce se píše `\|` nebo musí být v útržku kódu.

## [tools.keys] Cesty klíčů v YAML a JSON
commands: nks.tools.copyKeyPath, nks.tools.showKeys, nks.tools.goToKey, nks.tools.compareKeys
1. V souboru YAML nebo JSON ukazuje stavový řádek cestu klíče pod kurzorem, například `cs.app.title`. Položky seznamu se píšou `items[0]` a klíč s tečkou nebo závorkou se píše `["a.b"]`. Cesta se čte pro revizi a kurzor, pro které se zeptalo: když mezitím píšete nebo kurzor přesunete, odpověď se zahodí.
2. Nástroje, Struktura a jazyk, Kopírovat cestu klíče dá cestu do schránky.
3. Nástroje, Struktura a jazyk, Přejít na klíč (nebo `@` v Přejít na cokoli) vypíše klíče souboru podle cesty; Enter označí klíč tak, jak je napsán. Zobrazit osnovu klíčů vypíše tytéž klíče v panelu pluginu a kliknutí tam označí stejné bajty.
4. Nástroje, Struktura a jazyk, Porovnat klíče s jiným souborem se zeptá na další otevřený soubor YAML nebo JSON a do okna výstupu vypíše klíče s hodnotou, které každému souboru chybí.
- YAML v blokovém zápisu se čte: vnořené klíče, seznamy, klíče v uvozovkách a blokové skaláry. Hodnota ve složených nebo hranatých závorkách se počítá za jednu hodnotu. JSON smí mít komentáře a čárku na konci.
- Chyba syntaxe zastaví čtení na tom místě; klíče před ní zůstanou a zpráva uvede řádek.

## [tools.outline] Osnova kódu a symboly
commands: nks.tools.showOutline, nks.tools.goToSymbol
1. V souborech HTML, PHP, CSS, C# a Markdown ukáže Nástroje, Struktura a jazyk, Zobrazit osnovu kódu funkce, třídy, nadpisy a pravidla souboru jako strom v panelu pluginu. Kliknutí označí jméno symbolu.
2. Nástroje, Struktura a jazyk, Přejít na symbol (nebo `@` v Přejít na cokoli) vypíše tytéž symboly podle jména; Enter označí stejné bajty jako kliknutí v osnově.
3. Osnova patří ke konkrétní revizi dokumentu. Po úpravě se sama obnoví; než přijde nová, zůstane stará označená jako zastaralá a skok se přesune přes provedené úpravy, nebo se odmítne, když se změnil text, na kterém symbol stál. Odpověď pro starší revizi se nikdy nezobrazí jako aktuální.
4. Když se proces pluginu zastaví, osnova se po jeho obnovení načte znovu.
5. Dokud má soubor osnovu, ukazuje stavový řádek symbol, ve kterém je kurzor, i s nadřazenými (například `Třída › metoda`); jde za kurzorem a při zastaralé osnově se skryje.
- Čte se podle jednoduchých pravidel jazyka, bez jazykového serveru a bez indexu projektu. Kromě jazyků níže: JavaScript a TypeScript (třídy, rozhraní, výčty, funkce, šipkové funkce, metody), Python (třídy, funkce, metody podle odsazení), Rust (moduly, struktury, výčty, traity, bloky impl, funkce, makra), Dart (třídy, mixiny, výčty, rozšíření, funkce, metody), C a C++ (jmenné prostory, struktury, třídy, výčty, funkce, makra), Bash (funkce), CMake (funkce, makra, cíle), Makefile (cíle, `define`), Meson (cíle, testy, podadresáře). Klíče YAML a JSON dává osnova klíčů. HTML (nadpisy, prvky s `id`, `main`, `nav`, `section` a podobné), PHP (jmenné prostory, třídy, rozhraní, traity, výčty, funkce, metody), CSS (pravidla a @-pravidla), C# (jmenné prostory, typy, metody, konstruktory, vlastnosti), Markdown (nadpisy `#`).
- Komentáře a řetězce se nečtou. Co pravidla nepoznají, se přeskočí.
- Soubor větší než limit pluginu nebo s víc symboly, než osnova unese, se zobrazí zkrácený a zpráva to řekne. Velký soubor ve stránkovaném zobrazení se čte na pozadí od začátku až po limit; skok na symbol posune stránkované zobrazení na jeho místo.

## [edit.carets] Více kurzorů
commands: nks.edit.addNextOccurrence, nks.edit.skipOccurrence, nks.edit.selectAllOccurrences, nks.edit.addCaretAbove, nks.edit.addCaretBelow, nks.edit.clearCarets
1. Vyberte slovo a Ctrl+D přidá další jeho výskyt do výběru. Opakujte pro další výskyty.
2. Ctrl+K a potom Ctrl+D přeskočí poslední výskyt a vezme další.
3. Ctrl+Shift+L vybere všechny výskyty najednou.
4. Ctrl+Alt+šipka nahoru a Ctrl+Alt+šipka dolů přidají kurzor na řádek nad a pod.
5. Psaním se mění všechna místa naráz. Esc nechá jediný kurzor.
- Stavový řádek ukazuje počet kurzorů.
- Alt+tažení myší vytvoří kurzor na každém řádku nebo blokový výběr podle nastavení Alt+tažení.
- Se vstupní metodou (japonština, čínština, korejština) je rozepsaný text vidět podtržený u každého kurzoru. Po potvrzení se zapíše do všech naráz jako jeden krok Zpět; Esc ho zruší a kurzory nechá.

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
- Ve velkém souboru ukazuje lišta průběh v procentech a tlačítko Zrušit hledání zastaví. Vypsat dá shody do karty Výsledky hledání; je-li shod víc, než se vypíše, karta to napíše.
- Zapnete-li regulární výraz, objeví se tlačítko Zkusit (téma Zkouška regulárního výrazu).

## [search.replace] Nahrazení
commands: nks.search.replace, nks.search.replaceNext, nks.search.replaceAll
1. Stiskněte Ctrl+H, nebo zvolte Hledat a Nahradit. Lišta má navíc pole Nahradit čím.
2. Napište hledaný a náhradní text.
3. Enter v poli náhrady nahradí jednu shodu, Ctrl+Alt+Enter všechny. Totéž dělají tlačítka Nahradit a Vše.
4. Stavový řádek napíše, kolik shod se nahradilo, nebo Nic se nenahradilo.
- V regulární náhradě platí $1, $& a změna velikosti písmen \U \L \E \u \l.
- Nahradit další a Nahradit vše jsou i příkazy (Paleta příkazů). Makro je umí nahrát (téma Makra).
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
- Když kurzor stojí u závorky a nic není vybrané, podbarví se závorka i její protějšek. Závorky v řetězcích a komentářích se nepočítají. Vypne to volba Zvýraznit párové závorky v nastavení.
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
- Velikost nemá pevný strop: roste, dokud se do zobrazení vejde alespoň jeden řádek, nejméně je 6 bodů. Ukládá se do nastavení Velikost písma.
- V textu mění velikost i Ctrl+kolečko myši, v hexu Ctrl+Shift+kolečko.

## [view.display] Skryté znaky, čísla řádků a minimapa
commands: nks.view.toggleHiddenCharacters, nks.view.toggleLineNumbers, nks.view.toggleCurrentLine, nks.view.toggleRightMargin, nks.view.toggleLinks, nks.view.toggleMinimap
1. Zvolte Zobrazit a Zobrazit skryté znaky (Ctrl+K, Ctrl+W): vykreslí se mezery, tabulátory a konce řádků CR, LF a CRLF. Dokument se nemění.
2. Zobrazit čísla řádků (Ctrl+K, Ctrl+N), Zvýraznit aktuální řádek, Zobrazit pravý okraj (svislá čára ve sloupci 80, nastavuje se v nastavení Sloupec pravého okraje) a Klikací odkazy zapínají a vypínají zbytek.
3. Zobrazit minimapu (Ctrl+K, Ctrl+M) ukáže vpravo zmenšený obraz textu v barvách zvýraznění, log v barvách úrovní. Kliknutím nebo tažením se posouvá. Dlouhý soubor v editoru ukazuje jen řádky kolem obrazovky.
- U velkého souboru (stránkovaný režim) je obraz vzorkovaný: krátké úseky řádků ze začátků stejně velkých částí celého souboru, takže se nikdy nečte celý. Místo obdélníku ukazuje pozici tenká čára. Při indexování řádků pokrývá obraz zatím spočítané řádky, stejně jako posuvník. Počet řádků v úseku je placeholder, není rozhodnutá hodnota.
- Shody z lišty hledání jsou označené v úzkém pruhu na pravém okraji minimapy. Řádky, které ponechá filtr živého pohledu na log, jsou v pruhu také: chybové červeně, varování žlutě, ostatní modře.
- Odkazy se otevřou kliknutím s Ctrl a ve stránkovaném režimu nejsou k dispozici.
- Řádek delší než 50 000 znaků se nikdy nezalomí: Přepnout zalamování to řekne ve stavovém řádku. Stránkovaný režim nezalamuje nikdy.
- Znaky, které hlavní písmo nemá, se vykreslí písmy z nastavení Náhradní písma.
- Ctrl a kolečko myši mění velikost písma jako příkazy zvětšení.

## [view.secrets] Skryté hodnoty v souborech .env
commands: nks.view.toggleSecrets
1. Soubory s názvem `.env`, `.env.*` nebo `*.env` ukazují každou hodnotu proměnné jako `••••`, v textovém editoru, ve stránkovaném režimu i v hexadecimálním zobrazení. Zobrazit a Zobrazit skryté hodnoty je ukáže v aktuální kartě, další použití je zase skryje.
- V textovém editoru se po najetí ukazatelem myši na hodnotu ukáže jen ta jedna.
- Text souboru se nemění: uložení zapíše stejné bajty.
- Skryté jsou i v minimapě, v náhledech Přejít na cokoli a Místa úprav, ve výsledcích hledání a v exportech s formátováním (HTML, RTF). Kopírování dá do schránky `••••` místo skryté hodnoty a Vyjmout skrytou hodnotu nesmaže.
- V hexadecimálním zobrazení je skrytá hodnota tečka za každý bajt v obou sloupcích. Kopírovat, Kopírovat jako, Exportovat, inspektor dat, histogram a panel struktury vidí na místě těch bajtů `*` a stavový řádek říká, že hodnota je skrytá.
- Ve stránkovaném režimu čtečka obrazovky čte za každý znak skryté hodnoty odrážku. Řádek delší než 16 KiB, který hodnotu obsahuje, skryje vše za svou první částí.
- Plugin čte místo každé skryté hodnoty hvězdičky `*`. Než se na takovém souboru spustí vestavěný příkaz, program se zeptá, jestli smí hodnoty přečíst.
- Nastavení Tajné soubory obsahuje masky názvů souborů, jejichž hodnoty se skrývají, oddělené středníkem (`*` zastupuje libovolný text). Výchozí seznam je zatím předběžný.

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

## [workspace.projects] Projekty
commands: nks.project.save, nks.project.open, nks.project.addFolder, nks.project.delete, nks.project.settings
Projekt je pojmenovaná sada složek se soubory, které v nich byly otevřené. Uložený je v profilu a přepíná se jako složka.
- Přidat složku do panelu ukáže v panelu další složku vedle těch stávajících. Každá složka je horní řádek stromu.
- Uložit projekt (Soubor, Projekty a relace) uloží pod názvem složky panelu a otevřené soubory, které v nich leží. Projekt stejného názvu se přepíše.
- Otevřít projekt ukáže složky v panelu a otevře soubory, které projekt měl otevřené. Složka nebo soubor, který zmizel, se uvede ve stavovém řádku; ostatní se otevřou.
- Přepnutí na jiný projekt ponechá otevřené karty. Projekt, který opouštíte, si pamatuje své soubory.
- Smazat projekt odstraní jen záznam. Složky a soubory zůstanou.
- Upravit nastavení projektu otevře soubor nastavení aktivního projektu (stejný formát jako uživatelské nastavení). Hodnoty platí, dokud je projekt aktivní, po novém otevření projektu. Přejít kamkoli prohledává jen první složku.

## [workspace.favorites] Oblíbené
commands: nks.favorite.add, nks.favorite.open, nks.favorite.remove
- Přidat k oblíbeným uloží vybraný řádek panelu souborů, jinak aktivní soubor, jinak složku panelu. Je i v kontextovém menu panelu.
- Oblíbené je vypíše (i z prázdného panelu). Enter otevře složku v panelu a soubor v kartě. Cesta, která zmizela, je zobrazena ztlumeně s textem Nenalezeno.
- Odebrat z oblíbených je vypíše a odebere vybranou položku.

## [workspace.sessions] Pojmenované relace
commands: nks.session.save, nks.session.open, nks.session.delete
- Uložit relaci uloží otevřené karty pod názvem: skupiny, pořadí, aktivní kartu a výběr každé karty. Karta s neuloženými změnami nebo bez souboru si ponechá text. Relace stejného názvu se přepíše.
- Otevřít relaci otevře karty vedle těch, které už jsou otevřené. Soubory se načtou z disku a neuložený text relace jejich text nahradí, takže jde vrátit zpět nebo zahodit. Soubor, který zmizel, se uvede ve stavovém řádku; jeho text, pokud ho relace měla, se otevře v nepojmenovaném dokumentu.
- Smazat relaci se nejdřív zeptá, protože relace může obsahovat neuložený text.
- Soubor relace, který nejde přečíst, se přeskočí a uvede v liště; ostatní relace fungují.
- Velké soubory otevřené ve stránkovaném režimu se ukládají jen cestou.

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

## [format.inspector] Inspektor bajtů
commands: nks.format.applyEditorConfig
Každá akce, která přepisuje bajty, které jste neupravili, nejdřív ukáže, co změní, a nic nezapíše, dokud ji nepotvrdíte: Převést do kódování, Konce řádků, Normalizovat Unicode, Uložit jako s jiným kódováním a Použít EditorConfig.
- Okno uvádí počet změněných úseků a bajtů, velikost před a po, kódování a konce řádků před a po a jeden řádek na úsek: kde leží, původní a nová délka a první bajty (nejvýš 500 řádků, zbytek je započítán).
- Když cílové kódování některé znaky nepojme, stejné okno vypíše každý z nich s řádkem, sloupcem a bajtem a nabídne tři odpovědi: zrušit (nic se nezapíše), změnit kódování, nebo každý znak nahradit otazníkem.
- Příkaz Použít kódování a konce řádků z EditorConfig v nabídce Formát převede soubor na znakovou sadu a konec řádku, které nastavuje jeho .editorconfig; každý krok otevře inspektor. Vlastnost, kterou soubor nenastavuje, se nechá být.
- Úseky platí pro revizi, kterou jste viděli; změní-li se dokument mezitím, akce se odmítne.
- Soubor a potom Změny před uložením ukáže stejný výpis pro příští uložení.

## [format.batchEncoding] Převod kódování více souborů
commands: nks.file.convertEncodingBatch
1. Formát a potom Převést kódování souborů ve složce. Zvolte složku, masky názvů souborů (například *.txt;*.php, prázdné pole znamená všechny soubory) a cílové kódování.
2. Program soubory přečte a dřív než cokoli zapíše, ukáže, kolik jich se převede bez ztráty a které soubory by přišly o znaky, s těmito znaky a jejich počtem. Soubory, které jsou binární, už mají tyto bajty nebo mají kódování, které program jen odhaduje, se přeskočí a vypíšou se s důvodem.
3. Zvolte Převést N souborů (soubory, které by ztratily znaky, zůstanou, jak jsou), nebo Převést vše a ztracené znaky nahradit otazníkem.
4. Souhrn vypíše, co se převedlo, co zůstalo a co se změnilo po náhledu.
- Procházení složky dodržuje soubory ignorování a vynechává skryté soubory a soubory nad 64 MiB, stejně jako hledání v souborech. Otevřené dokumenty s neuloženými změnami zůstanou stranou.
- Každý soubor se nahradí celý, nebo vůbec, a jen když má pořád obsah, ze kterého vznikl náhled. Běh se zapisuje do žurnálu: když se program nebo počítač uprostřed zastaví, nabídne příští start dokončení dávky nebo vrácení starých souborů.
- Soubor v kódování, které je jen odhad (nízké procento jistoty), se nikdy nepřevádí; otevřete ho, zvolte Znovu otevřít v kódování a převeďte ho odtud.

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

## [settings.backup] Záloha konfigurace
commands: nks.settings.backup, nks.settings.restore
1. Zvolte Nastavení a pak Zálohovat konfiguraci a pojmenujte soubor. Soubor ZIP má v názvu datum. Obsahuje nastavení, klávesové zkratky, uživatelské proměnné, externí nástroje, akce při uložení, pohledy na logy, makra, recepty, klipy a datové pluginy, pracovní prostory, vzdálená připojení a proxy.
2. Na jiném počítači nebo po přeinstalaci zvolte Nastavení a pak Obnovit konfiguraci a vyberte soubor. Soubory se vrátí při příštím spuštění: zvolte Ukončit a obnovit, nebo Při příštím spuštění.
- Záloha neobsahuje hesla ani tokeny. Hesla zůstávají v systémové klíčence. Hodnota v souborech, která obsahuje uložené heslo nebo jejíž název říká, že jde o heslo, token nebo klíč, se vynechá a stavový řádek řekne kolik. Po obnovení zadejte hesla znovu (Vzdálená připojení a pak Nastavit heslo).
- Obnovení nahradí jen soubory, které záloha obsahuje. Konfigurace z doby předtím se nejdřív uloží do složky config-backups v profilu.
- V záloze není: žurnál, místní historie, zálohy souborů, kopie vzdálených souborů, relace, logy a nainstalované pluginy.
- Zálohu z novější verze programu program odmítne.

## [view.output] Okno výstupu
commands: nks.view.toggleOutput
Okno výstupu je pruh pod editorem, do kterého zapisují úlohy a hledání. Ve výchozím stavu je skryté.
1. Stiskněte Ctrl+Shift+Y, nebo zvolte Zobrazit a Výstup.
2. Záložky Úlohy a Hledání přepínají kanál. Souhrn hledání v souborech se zapíše i do kanálu Hledání.
3. Tlačítko Vymazat kanál vyprázdní, Zavřít (nebo Esc) okno schová.
- Text jde vybrat a zkopírovat. Prázdný kanál napíše Do tohoto výstupu se zatím nic nezapsalo.
- Výstup se čte ze souborů v profilu, takže po pádu programu, který ho psal, zůstane vidět.

## [view.pluginPanels] Panely pluginů
commands: nks.view.pluginPanels
Panel pluginu je seznam nebo strom, který plugin pošle editoru. Editor ho ukáže v levém sloupci.
1. Zvolte Zobrazit a Panely pluginů, nebo příkaz najděte v paletě. Seznam ukáže panely, které pluginy právě mají, a u každého název pluginu.
2. Vyberte panel a stiskněte Enter. Otevře se s vybranou první položkou.
3. Šipky nahoru a dolů procházejí položky, šipka vpravo a vlevo rozbalí a sbalí větev. Enter nebo dvojklik položku otevře.
4. Esc v panelu ho zavře a vrátí vás do textu. Když píšete v textu, panel zůstane otevřený.
- Když plugin položky obnoví, vybraná položka zůstane vybraná.
- Úlohu pluginu zrušíte tlačítkem Zrušit ve stavovém řádku. Řádek pak u úlohy píše, že se ruší. Když ji plugin brzy sám neukončí, ukončí ji editor a uvolní její místo.
- Když proces pluginu skončí, zmizí jeho panely, položka stavového řádku i úlohy.

## [view.fullscreen] Celá obrazovka
commands: nks.view.fullScreen
1. Stiskněte F11, nebo zvolte Zobrazit a Celá obrazovka.
2. Zmizí rám okna; zůstane nabídka, karty, editor a stavový řádek. Psaní jde dál do editoru.
3. Dalším F11 se vrátí předchozí stav, i maximalizované okno.
- Na macOS používá F11 systém; použijte nabídku Zobrazit.

## [hex.open] Hex: otevření a pohyb
commands: nks.view.hex, nks.file.openAsHex
Hex zobrazuje soubor po bajtech: offset, bajty šestnáctkově a znaky. Čte jen řádky na obrazovce, takže velikost souboru nevadí.
1. Ctrl+K a potom Ctrl+H přepne aktivní dokument mezi textem a hexem (nabídka Zobrazit, Hex, Přepnout hex a text). Je to stejný dokument se stejnou historií Zpět; výběr se převede přes bajty.
2. Soubor a Otevřít v hex otevře vybraný soubor rovnou v hexu. Z příkazového řádku to dělá přepínač --hex, případně s --offset.
3. Soubor, který vypadá binárně (nulový bajt v prvních 8 000 bajtech), nabídne lišta nad dokumentem: Otevřít v hex, nebo Nechat jako text.
- Šipky, PageUp a PageDown, Home a End (na řádku), Ctrl+Home a Ctrl+End (celý soubor). Shift rozšiřuje výběr. Tab přepíná mezi sloupcem bajtů a znaků.
- Vyjmout, Kopírovat a Vložit pracují s bajty; mezi dvěma hexy se přenesou přesně.

## [hex.find] Hex: hledání a skok na offset
- Ctrl+F otevře okno Hledat bajty. Zadejte bajty šestnáctkově (50 4B ?? 04, ?? je libovolný bajt), text v uvozovkách, nebo číslo s typem (0x1F40 i32).
- F3 a Shift+F3 najdou další a předchozí shodu. Stavový řádek napíše Shoda 1 z 4, nebo Aspoň N shod, když hledání došlo na limit.
- Ctrl+G otevře Přejít na offset. Zápis: 0x1F40, +0x100 (od kurzoru), end-512, 50%, 4K, sel (začátek výběru), val (čtyřbajtová hodnota pod kurzorem).
- Nenalezeno: shoda není. Neplatný vzor nebo Neplatný výraz: opravte zápis. Offset leží mimo soubor: zadejte menší hodnotu. Soubor se mezitím změnil na disku, výsledek hledání se zahodil: hledání zopakujte.
- Zástupný znak *: 50 4B * 04 znamená 0 až N bajtů mezi 4B a 04. N je nastavení Nejdelší zástupný znak * v hex hledání; dokud je 0 (nezvoleno), platí vestavěná výchozí hodnota 256 bajtů. *{2,8} zapíše vlastní rozsah a nastavení nepotřebuje.
- Vzor mezi lomítky je regulární výraz nad bajty, například /\x50\x4B[\x01-\x08]/. Bajt je znak 0 až 255, . je jeden libovolný bajt (i 0A a 0D), \xHH je bajt a [...] množina bajtů. Skupiny, |, ? * + {n,m} a \n \r \t \0 \d \w \s fungují; ^ a $ jsou začátek a konec souboru (při nahrazení výběru); \b, zpětné odkazy a lookaround ne, protože bajty nemají řádky ani slova. Otevřené opakování (.*, +, {n,}) sahá nejvýš na N bajtů. Shoda potřebuje aspoň jeden bajt a shody se smějí překrývat.
- Ctrl+H nahrazuje bajty: nejdřív vzor (stejné tvary jako Hledat), potom náhradu hexem, textem v uvozovkách nebo číslem; prázdná náhrada shody smaže. Je-li něco vybráno, hledá se jen ve výběru.
- Náhled vypíše každou shodu s původními a novými bajty a upozorní, když se změní délka souboru. Nahradit vše zapíše všechno jedním krokem zpět. Změnil-li se soubor po náhledu, nenahradí se nic. Shody, které se překrývají s předchozí, se vynechají.
- V režimu přepisu musí mít náhrada délku shody; délku změníte po zapnutí režimu vkládání (klávesa Insert).

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
- Ctrl+kolečko myši nad hexem přidá nebo ubere jeden blok bajtů na řádku (jedno seskupení, nejméně 8) a řádek s kurzorem zůstane na místě. Strop není pevný, jen šířka okna: když se další blok nevejde, kolečko nic neudělá.
- Podle šířky okna: počet bajtů na řádku sleduje šířku okna a zaokrouhluje se dolů na násobek seskupení. Ruční volba velikosti nebo Ctrl+kolečko to vypne.
- Ctrl+Shift+kolečko mění velikost písma i v hexu, stejně jako Ctrl+= , Ctrl+- a Ctrl+0.
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
1. Úpravy, Hex, Hex: Kopírovat výběr jako dá vybrané bajty do schránky jako text: hex řetězec, pole pro C, C#, Javu, Python, Rust, Go, JavaScript nebo Pascal, Base64, Intel HEX, Motorola S-record nebo hexdump.
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
1. Hex: Kontrolní součty a hashe počítá nad výběrem, nebo nad celým souborem, když nic není vybráno: CRC-16, CRC-32, CRC-64 v několika variantách, CRC s vlastním polynomem, Adler-32, Sum-8/16/32, xxHash32, xxHash64, XXH3-64, XXH3-128, MD5, SHA-1, SHA-256, SHA-384 a SHA-512. Nástroje, Text a výpočty, Spočítat hash nabízí stejný seznam pro textový pohled.
2. Výpočet běží na pozadí a postup ukazuje stavový řádek; Esc ho zruší.
3. Výsledek jde zkopírovat nebo porovnat s vloženou hodnotou; na mezerách, dvojtečkách, 0x a velikosti písmen nezáleží.
- Každá varianta CRC má pevný polynom a parametry (šířka, počáteční hodnota, zrcadlení vstupu a výsledku, koncové xor) podle katalogu algoritmů CRC.
- CRC s vlastním polynomem vezme šířku v bitech a hex polynom (lichý), počáteční hodnotu, dva příznaky zrcadlení (0 nebo 1) a koncové xor.

## [hex.bookmarks] Hex: záložky a barevné oblasti
commands: nks.hex.bookmarks.panel, nks.hex.bookmarks.edit, nks.hex.bookmarks.export, nks.hex.bookmarks.import
Záložka v hexu označí oblast bajtů jménem, barvou, poznámkou a typem, jak byla přečtena v inspektoru dat.
1. Ctrl+F2 označí výběr (bez výběru hodnotu, kterou inspektor dat ukazuje u kurzoru, nebo jeden bajt) a odstraní nejužší záložku pod kurzorem, pokud nějaká je. F2 a Shift+F2 skočí na další a předchozí záložku.
2. Hex: vlastnosti záložky otevře jméno, barvu, poznámku a typ záložky pod kurzorem (bez ní vznikne nová).
3. Hex: panel záložek (Ctrl+K a potom Ctrl+B) vypíše záložky s filtrem, který hledá ve jménu, poznámce a typu. Enter nebo dvojklik vybere oblast, Delete ji odstraní.
4. Hex: exportovat záložky zapíše CSV (jméno, barva, poznámka, typ, počáteční a koncový offset) nebo, když název souboru končí na .json, JSON. Hex: importovat záložky přečte obě formy a přidá oblasti, které se do souboru vejdou; prázdná oblast, oblast mimo soubor nebo už existující se přeskočí a stavový řádek je spočítá.
- Oblast je vykreslená v barvě záložky s proužkem nahoře, takže barva není jediné znamení; stavový řádek jmenuje záložku pod kurzorem.
- Oblasti sledují bajty: bajty vložené před oblast ji posunou, bajty vložené dovnitř ji prodlouží, úprava přes její okraj se do ní zahrne, zpět a znovu ji vrátí. Oblast, jejíž bajty byly všechny smazány, zaniká.
- Záložky se ukládají do relace (po restartu jsou zpět), nikdy do souboru. Smazat záložky odstraní všechny.

## [hex.map] Hex: mapa souboru a statistika bajtů
commands: nks.hex.minimap, nks.hex.minimap.mode, nks.hex.statistics
Mapa je proužek vedle posuvníku, který ukazuje celý soubor najednou v buňkách o výšce asi čtyři pixely. Obdélník označuje část na obrazovce; klik nebo tažení tam posune mřížku.
1. Hex: mapa souboru (Ctrl+K a potom Ctrl+G) mapu ukáže nebo skryje. Hex: mapa podle entropie nebo druhu bajtů přepíná, co buňky ukazují: entropii bajtů (světlá pro uspořádaná data, výrazná pro komprimovaná nebo šifrovaná), nebo druh bajtu, který tvoří většinu buňky (nulové bajty, text, řídicí bajty, vysoké bajty, FF).
2. Úzký pruh u pravého okraje označuje shody posledního hledání, záložky v jejich barvách a místa změněná od posledního uložení.
3. Hex: statistika bajtů (Ctrl+K a potom Ctrl+S) otevře panel s histogramem výběru, nebo celého souboru, když není nic vybráno: kolik ze 256 hodnot bajtu se vyskytuje, entropie v bitech na bajt, nejčastější hodnoty a podíly tříd bajtů.
- Soubor do 1 GiB se čte celý na pozadí. U většího souboru mapa nejdřív ukáže vzorek, pár bloků z každé buňky, a zpřesňuje se, jak program čte dál, celkem nejvýš 1 GiB; buňka, která se ještě nečetla, je světlá.
- Statistika celého souboru nad 1 GiB se nepočítá najednou: vyberte úsek.
- Mapa i počítání běží vedle okna a při změně souboru se dělají znovu.

## [hex.template] Hex: šablona struktury
commands: nks.hex.template, nks.hex.template.open
Panel struktury čte soubor podle šablony a ukáže jeho pole jako strom. Výběr pole označí jeho bajty v mřížce a posun kurzoru v mřížce vybere pole, které ho obsahuje.
1. Hex: Šablona struktury (Ctrl+J) panel otevře, nebo zavře, když už má fokus. Šablona se vybere podle magického čísla, potom podle přípony souboru; seznam nad stromem dovolí zvolit jinou a volba se pamatuje pro danou příponu (nastavení hex.structureTemplates, zapsané přípona=šablona).
2. Pole s částmi (struktura nebo seznam) se čte až při rozbalení, takže velký soubor se otevře hned. Dlouhý seznam se ukáže jako rozsahy (položky[0..255]), které se rozbalí na prvky, a seznam, jehož prvky mají všechny stejnou velikost, třeba trojúhelníky 3D modelu, může mít miliony prvků a otevře se přesto hned. Řádek ukazuje jméno, hodnotu a šedě offset a délku v bajtech. Šipky nahoru a dolů procházejí řádky, doprava a doleva pole otevírají a zavírají, Enter vrátí fokus do mřížky s označenými bajty pole, Esc ho vrátí bez označení.
3. Pole, které nejde přečíst, je červené s důvodem, třeba že přesahuje konec dat nebo se liší od očekávaných bajtů. Pole za ním se nezobrazí, protože jejich místo není známé.
4. Hex: Otevřít soubor šablony struktury vyzve k výběru souboru .ksy (totéž dělá tlačítko pod seznamem) a nabídne jeho šablonu v seznamu. Šablona, kterou nelze použít, se pojmenuje v poznámce pod seznamem i s řádkem, například „nepodporováno: process“. Takto otevřené šablony zůstanou v seznamu do konce běhu programu.
- Přibalené šablony: hlavička BMP (s velikostí řádku), bloky PNG (IHDR, gAMA, tEXt), sekce archivu ZIP, hlavičky Windows PE, hlavičky ELF (32 i 64 bit, oba řády bajtů, sekce dynamic) a binární STL. Soubor pinned.yaml vedle šablon u každé uvádí verzi, licenci a použité konstrukce. Napsali jsme je pro Textida a mají licenci MIT jako program; šablona jiného projektu se nekopíruje. Panel ukazuje soubor, verzi a licenci použité šablony.
- Šablony jsou podmnožina formátu Kaitai Struct .ksy: celá čísla (u1 až u8, s1 až s8), desetinná čísla (f4, f8), celá čísla po bitech (b1 až b64; za sebou, neopakují se), řetězce (str, strz), surové bajty, pevné bajty (contents), výčty (i uvnitř typu), vnořené typy, size, size-eos, repeat (eos, expr nebo until), if, switch-on s cases (klíč _ je výchozí větev), instances s pos (a io: _root._io) nebo value, meta endian (i se switch-on a cases) a bit-endian a výrazy. Výrazy jsou povolené v size, repeat-expr, repeat-until, if, pos, io, value a switch-on. Počítají 64bitová celá čísla, desetinná čísla, true/false a řetězce: + - * / %, << >> & | ^ ~, < <= > >= == !=, and or not, a ? b : c, závorky, čísla (desítkově, 0x, 0b a desetinná jako 1.5 nebo 2.5e-3), řetězce v uvozovkách, jména dřívějších polí a instancí, a.b, seznam[i], enum::člen a typ::enum::člen, _parent, _root, _index, _ (poslední prvek v repeat-until), _io.pos, _io.size, _io.eof, .as<typ> (bez převodu), .to_i u řetězce, desetinného čísla a pravdivostní hodnoty, .length u řetězce, .size, .first a .last u seznamu. Desetinné číslo nikdy není size ani počet (použijte .to_i) a % a bitové operátory berou jen celá čísla. pos se počítá od začátku proudu struktury (struktura se size je vlastní proud). Pole v zápisu, jiné metody, process (krok xor nebo zlib nad bajty) a vše ostatní, co není uvedeno, se odmítne s řádkem šablony, například „nepodporováno: process“. Dělení nulou, chybějící pole nebo špatný druh hodnoty dá chybu na daném poli; obří size nebo počet skončí chybou limitu. Instance jsou vypsané za poli.
- Čtení skončí hláškou, když šablona vnořuje příliš hluboko, opakuje jedno pole příliš často, čte řetězec či množství práce nad pevným limitem, nebo jeden uzel trvá příliš dlouho.

## [hex.compare] Hex: binární porovnání
commands: nks.hex.compare.file, nks.hex.compare.saved
Dvě hex mřížky vedle sebe, které se posouvají společně, s rozdíly podbarvenými a vypsanými dole.
1. Hex: porovnat se souborem se zeptá na soubor a porovná ho s dokumentem aktivní karty (i s jeho neuloženými změnami). Hex: porovnat s uloženým souborem porovná dokument s jeho vlastním souborem na disku, takže vidíte přesně, co by příští uložení změnilo.
2. Se zarovnáním (výchozí) hledá bajty, které jeden soubor vložil nebo smazal, takže vložený blok se ukáže jako vložení a bajty za ním se nehlásí jako změněné. Bajt po bajtu porovnává každý offset se stejným offsetem v druhém souboru, což se hodí na soubory s pevným rozložením. Použije se u každého souboru nad 256 MiB, který režim se zarovnáním nečte.
3. Seznam pojmenuje každý rozdíl: změněno, vloženo nebo smazáno, s jeho rozsahem v levém a v pravém souboru. Enter na řádku, F8 a Shift+F8 (nebo tlačítka) skočí na další a předchozí rozdíl v obou mřížkách.
- Posun jedné mřížky posune druhou na stejné místo v druhém souboru; u vložených bajtů je to odpovídající místo, ne stejný offset.
- Mřížky jsou jen pro čtení. Mapa vedle každé mřížky označuje rozdíly.
- Seznam končí na 1 000 000 rozdílech (okno to řekne); dva nesouvisející soubory by jinak zaplnily paměť.
- Dvě revize souboru z místní historie se v hexu porovnat nedají. Porovnat v místní historii porovnává text (téma Místní historie).

## [tools.run] Nástroje: spuštění externího programu
commands: nks.tools.run, nks.tools.stop
1. Nástroje, Spouštění nástrojů, Spustit nástroj nabídne nástroje zapsané v souboru tools.json ve složce profilu. Poslední řádek ten soubor otevře; poprvé do něj zapíše prázdný, který doplníte.
2. Nástroj má id, title, executable a args (seznam: jedna položka je jeden argument). Volitelné jsou cwd (adresář, kde poběží; bez něj složka aktuálního souboru), timeoutSeconds (bez něj není žádný časový limit) a logPattern (viz Vzor LOGu). Příklad: {"externalTools": [{"id": "build", "title": "Build", "executable": "dotnet", "args": ["build", "${File}"], "cwd": "${FileDir}", "timeoutSeconds": 120}]}
3. V executable, args a cwd zastupují proměnné ${File} (úplná cesta), ${FileDir}, ${FileName}, ${FileBase}, ${FileExt}, ${Line} a ${Column} aktuální dokument; ${Selection} je vybraný text (může mít víc řádků), ${Word} slovo pod kurzorem a ${ProjectRoot} nejbližší složka nad souborem (nebo jeho vlastní), která má .textida nebo .git; $$ je znak dolaru. Argument zůstane jedním argumentem, ať hodnota obsahuje cokoli, i mezery a uvozovky: žádný shell se nepoužívá. Neznámá proměnná nebo taková, která se nehodí (${File} u dokumentu bez souboru, ${Selection} bez výběru, ${Word} mimo slovo), zastaví spuštění dřív, než cokoli začne. Platí tu i proměnné klipů a receptů (file.path, date, datetime, selection, user.name a vaše vlastní proměnné) se stejnou hodnotou jako tam. Vlastní proměnná stejného jména má přednost i před ${File} a dalšími. Když soubor vlastních proměnných nejde přečíst, nástroj se nespustí a výstup řekne proč.
4. Nástroj, jehož příkaz a adresář ještě neběžely, se ukáže s oběma a spustí se až po vašem potvrzení. Potvrzení platí, dokud se nezmění definice nebo program, na který vede.
5. Výstup jde řádek po řádku na kartu Úlohy v okně výstupu, nakonec s kódem ukončení a časem. Tlačítko Zastavit v tom okně nebo příkaz Zastavit běžící nástroj ukončí program i vše, co spustil; totéž udělá časový limit.
- Dávkové soubory (.bat, .cmd) se spouštějí přes cmd.exe a každý argument se pro něj uzavře do uvozovek, takže mezery, uvozovky a znaky jako & | < > ^ % projdou beze změny. Argument s koncem řádku nebo jiným řídicím znakem se odmítne před spuštěním; stejně tak argument s !, když dávka zapíná zpožděné rozvinutí.
- Název programu bez cesty se hledá jen v PATH, nikdy v pracovním adresáři.
- Výstup se čte jako UTF-8, pokud nástroj neuvádí jiné kódování ("outputEncoding": "windows-1250") nebo není zadané nastavení Výchozí kódování výstupu nástrojů. Nástroj bez timeoutSeconds dostane nastavení Výchozí časový limit nástrojů (0: bez limitu). Běží vždy jen jeden nástroj.

## [tools.logPattern] Vzor LOGu: kliknutí na chybu ve výstupu
commands: nks.tools.logPattern
1. Vzor LOGu pro výstup otevře zkoušku vzoru s posledním výstupem nástrojů jako ukázkou. Každý řádek ukázky ukáže soubor, řádek a sloupec, které v něm vzor našel, nebo že nenašel nic.
2. Maska se porovnává zleva a bez ohledu na velikost písmen. * přeskočí libovolný text až k další pevné části masky. %F je název souboru, až po další pevnou část. %D je disk (C: následované lomítkem) před ním. %L je řádek a %C sloupec (číslice). %S přeskočí mezery a tabulátory. %E ukončí masku. Maska potřebuje %L; bez %F míří na soubor, který byl otevřený. Příklady: %F:%L:%C:* pro main.c:12:5: error, *] %F(%L) pro [Error] a.pas(346): text.
3. Druhý jazyk je regulární výraz se skupinami line (povinná), file a column.
4. Použít pro výstup zařídí, že vzor platí pro řádky v okně výstupu: kliknutí na řádek nebo Enter otevře soubor na tom řádku. Relativní název souboru se hledá v adresáři, kde nástroj běžel, pak ve složce souboru, který byl otevřený při spuštění, pak mezi otevřenými dokumenty podle názvu.
- Nástroj může mít vzor vlastní: v tools.json "logPatterns": [{"id": "gcc", "title": "gcc", "syntax": "mask", "pattern": "%F:%L:%C:*"}] a v nástroji "logPattern": "gcc". Bez "syntax" je vzor regulární výraz.

## [tools.edit] Nástroje: úprava definic, typované proměnné, nástroje ze složky
commands: nks.tools.edit
1. Nástroje, Spouštění nástrojů, Upravit definice nástrojů otevře jeden editor nástrojů ze souboru tools.json. Nový, Uložit a Smazat zapisují soubor hned. Řádek pod formulářem ukáže příkaz přesně tak, jak by se spustil pro otevřený soubor, nebo proč teď nejde spustit. Editor i Spustit nástroj berou stejnou definici, takže nástroj se z obou spustí se stejnými argumenty.
2. Definice může deklarovat proměnné, jednu na řádek: Název:text nebo Název:path, případně s =hodnota (hodnota smí používat další proměnné, třeba Out:path=${FileDir}/out). Bez hodnoty se vezme uživatelská nebo vestavěná proměnná stejného jména. Pořadí je: deklarovaná hodnota, vaše vlastní proměnné (Upravit uživatelské proměnné), vestavěné. V executable, args a cwd ji použijete jako ${Název}.
3. Text je jeden řádek. Cesta je jeden neprázdný řádek bez znaků * ? " < > | . Hodnota špatného typu zastaví spuštění dřív, než cokoli začne, a výstup jmenuje proměnnou.
4. Složka otevřeného souboru nebo složka nad ní může obsahovat .textida/tools.json s nástroji stejného tvaru. Ukážou se ve Spustit nástroj, ale spustí se až po potvrzení příkazu a adresáře, i když stejný text máte ve vlastním souboru. Změní-li se definice včetně proměnných, zeptáme se znovu. Tyto nástroje se v editoru upravit nedají.
- Prázdný argument se v editoru napsat nedá. Uložení zahodí komentáře v tools.json.
- Pole Kódování výstupu jmenuje kódování, ve kterém se výstup čte (utf-8, windows-1250, ibm852); jméno, které systém nezná, se odmítne. Prázdné: výchozí z nastavení.

## [tools.make] Makefile: spuštění cíle
commands: nks.tools.makeTargets, nks.tools.makeRepeat
1. Nástroje, Spouštění nástrojů, Spustit cíl Makefile vypíše cíle Makefile k otevřenému souboru: samotný soubor, když je Makefile (Makefile, GNUmakefile, *.mk, *.mak), jinak nejbližší GNUmakefile, makefile nebo Makefile v jeho složce nebo ve složkách nad ní. Hledání končí v kořeni projektu (složka s .textida nebo .git). Je-li Makefile otevřený, čtou se cíle z textu karty. Vybraný cíl spustí jako make -f s tím souborem a cílem ve složce Makefile.
2. Opakovat poslední cíl Makefile spustí naposledy spuštěný cíl znovu.
3. Běh jde přes spouštěč nástrojů: příkaz se ukáže a poprvé se potvrzuje, výstup jde do karty Úlohy, Zastavit ukončí make i vše, co spustil.
4. Řádek výstupu, který jmenuje soubor a řádek (main.c:12:5: error, Makefile:7:), otevře to místo klikem nebo Enterem. Rekurzivní make hlásí „Entering directory“ a „Leaving directory“; relativní název souboru za takovým řádkem se hledá v té složce.
- Make musí být nainstalovaný a v PATH, nebo uvedený v nastavení Program make (třeba mingw32-make). Zvláštní cíle (.PHONY), vzorová pravidla a názvy s proměnnou či zástupným znakem se nevypisují. Potvrzení se váže na příkaz, ne na obsah Makefile, protože spustit Makefile znamená spustit, co říká.

## [packages.registry] Manifesty balíčků: názvy, verze a značka novější verze
commands: nks.packages.lookup, nks.packages.refresh, nks.packages.revoke
1. V souborech package.json, composer.json, requirements.txt, .csproj, go.mod, Cargo.toml, Gemfile a pubspec.yaml se editor umí zeptat registru balíčků (npm, Packagist, PyPI, NuGet, proxy Go, crates.io, RubyGems, pub.dev) na názvy a verze. Bez vašeho souhlasu se nic neposílá: Nástroje, Balíčky, Zkontrolovat verze balíčků ukáže servery a zeptá se jednou pro každý registr.
2. Po souhlasu nabídne Ctrl+J v názvu položky názvy z registru a ve verzi verze toho balíčku, od nejnovější. PyPI a proxy Go nemají vyhledávání: název se nabídne jen tehdy, když napsaný text je celý název.
3. Položka s přesně zadanou verzí (1.2.3, ==1.2.3, v1.2.3), pro kterou existuje novější vydání, dostane informační značku na verzi. Rozsahy (^, ~, >=) se neznačí. Předběžná verze se za novější nebere, dokud existuje vydání. Kontrola běží, když přestanete psát.
4. Odpovědi se drží v cache v paměti, nejvýše do limitu velikosti (nejstarší odpadnou první), jen omezenou dobu a nejdéle do zavření editoru. Nabídka nebo značka z cache to říká („cache“). Obnovit verze balíčků zahodí cache toho registru a zeptá se znovu.
5. Zakázat dotazy na registry a stahování schémat vezme zpět všechny souhlasy a zahodí cache. Použije se síťová proxy z příkazu Síťová proxy; proxy s uživatelským jménem použít nejde a dotazy se pak zastaví.
- Nic se nestahuje ani neinstaluje, nehledají se zranitelnosti a go mod download se nikdy nespouští. K registru se dostanou jen názvy balíčků, napsaný text a vaše síťová adresa.
- Registr, který selže, nenabídne nic; Zkontrolovat verze balíčků napíše chybu do stavového řádku. Časový limit, limit velikosti, velikost a doba života cache a počet balíčků na jednu kontrolu jsou prozatímní.

## [tools.schema] Schémata pro JSON, YAML a TOML: kontrola, doplňování a popisy
commands: nks.schemas.allow
1. Soubor JSON, JSONC, TOML nebo YAML se kontroluje podle schématu, které mu patří. Soubor může schéma pojmenovat sám („$schema“ na začátku souboru JSON, komentář „# yaml-language-server: $schema=ADRESA“ v YAML, „#:schema ADRESA“ v TOML); jinak rozhoduje název souboru (package.json, Cargo.toml, workflow GitHubu, soubory Claude Code, MCP a Codexu a krátký seznam běžných konfiguračních souborů). Stavový řádek ukáže název schématu, nebo „bez schématu“. Nález je podtržený a jeho bublina říká místo, u syntaktické chyby i řádek a sloupec.
2. Ctrl+J nabídne klíče, které schéma v objektu kolem kurzoru dovoluje, a tam, kde se píše hodnota, i povolené hodnoty (hodnoty enum, true a false). Když myš chvíli stojí na klíči nebo hodnotě, ukáže se, co o ní schéma říká: popis, typ, povolené hodnoty a výchozí hodnota.
3. Schémata package.json, Cargo.toml, workflow GitHubu a souborů agentů jsou součástí editoru. Schéma jiného známého souboru a schéma, které soubor pojmenuje adresou, se stáhne až po vašem souhlasu. Do té doby stavový řádek říká „stažení není povoleno“ a Nástroje, Balíčky, Povolit stažení schématu ukáže server a zeptá se jednou pro každý server. Zakázat dotazy na registry a stahování schémat vezme zpět všechny souhlasy. Schéma, které soubor pojmenuje cestou, se čte z disku a souhlas nepotřebuje.
4. Stažené schéma se ukládá do tohoto počítače, nejvýše do limitu velikosti (nejstarší se mažou první), a dokud je čerstvé, použije se bez dotazu. Když server neodpoví, použije se uložená kopie a stavový řádek to říká. Použije se síťová proxy z příkazu Síťová proxy; proxy s uživatelským jménem použít nejde.
5. Syntaktická chyba v souboru JSON, JSONC, TOML nebo YAML se podtrhne i tehdy, když žádné schéma neplatí. YAML s kotvami, tagy nebo složitými klíči se číst nedá: takový soubor se nekontroluje a stavový řádek to říká.
- Rozumí se podmnožině JSON Schema: typy, enum, required, properties, items, oneOf, anyOf, allOf, if/then, místní $ref a pattern. Odkazy na jiné soubory nebo adresy uvnitř schématu se přeskakují. Limity velikosti, doba života uložených kopií a čekací doby jsou prozatímní.

## [packages.scripts] Skripty balíčku: npm a Composer
commands: nks.packages.script
1. S otevřeným souborem package.json nebo composer.json vypíše Nástroje, Balíčky, Spustit skript balíčku jeho skripty. Vybraný se spustí jako npm run název nebo composer run-script název ve složce souboru.
2. Jde to přes spouštěč nástrojů jako nástroj z projektové složky: příkaz, složka a soubor, ze kterého skript pochází, se ukážou a je třeba je potvrdit. Potvrzení je vázané na text skriptu, takže změněný skript se zeptá znovu. Výstup jde do karty Úlohy; Zastaviť ho ukončí.
- npm nebo Composer musí být nainstalovaný. Na Windows jsou oba dávkové soubory; editor spustí Node s npm-cli.js, nebo PHP s composer.phar, ze složky dávkového souboru, takže funguje i název skriptu s koncem řádku nebo s !. Bez tohoto standardního uspořádání se dávka spustí přes cmd.exe.

## [plugins.manager] Správce pluginů: instalace, aktualizace, návrat a odebrání pluginů
commands: nks.plugins.manage
1. Nástroje, Správce pluginů vypíše pluginy dodávané s editorem i ty, které jste nainstalovali, a oprávnění každého z nich. Příkazem Nainstalovat plugin ze ZIP se přidá další.
2. ZIP se rozbalí do přípravné složky a zkontroluje dřív, než cokoli z něj běží: každá cesta musí zůstat ve složce pluginu, odkazy se odmítají, velikost i počet souborů jsou omezené, manifest nkspad-plugin.json musí ležet v kořeni ZIP a být platný a ID nesmí patřit vestavěnému pluginu. Každý problém má vlastní hlášení a nic se neinstaluje.
3. Pak editor ukáže oprávnění, o která plugin žádá, a čeká na vaši odpověď. Dokud nepovolíte, nic z pluginu neběží. Oprávnění upravovat a vytvářet soubory v pracovních složkách (workspace.files) uděluje tady uživatel, stejně jako každé jiné. Pokud byl ZIP stažen (na Windows značka Mark of the Web), dialog to řekne.
4. Po výběru nainstalovaného pluginu ho můžete zapnout nebo vypnout, aktualizovat z novějšího ZIP, vrátit na předchozí verzi, změnit jeho oprávnění nebo odinstalovat. Nová verze se rozbalí vedle staré a aktivní je, až když se načte; stará zůstane pro návrat. Aktualizace se ptá jen na oprávnění, na která jste se dosud nebyli dotázáni; dřívější odpovědi platí dál.
5. Odebrané oprávnění hned zastaví nové požadavky pluginu a zruší ty, které čekají. Plugin bez oprávnění dostane chybu, ne prázdný výsledek. Vypnutí ukončí proces; odinstalace smaže jen složku daného pluginu.
6. Příkaz nainstalovaného pluginu, který smí číst nebo upravovat dokumenty, pracuje s aktivní kartou. Plugin ten dokument vidí jen po dobu příkazu. Změnu, kterou navrhne, ukáže kontrola změn a zapíše se, až ji použijete. Přímá úprava (document.edit.direct) je jeden krok zpět. Když plugin skončí dřív, než se rozhodnete, jeho návrh zmizí a nic se nezapíše.
- Oprávnění jsou záznam toho, co jste povolili, ne izolace: plugin je program, který běží s právy vašeho uživatele. Instalujte jen pluginy, kterým věříte. Podpisy se zatím nekontrolují. Limity velikosti a počtu souborů ve ZIP jsou provizorní. Nové příkazy aktualizovaného pluginu se objeví po restartu.
- Plugin může přinést klávesy a nastavení. Jeho klávesy (contributes.keybindings, volitelně s podmínkou "when") fungují jako klávesy editoru, ale klávesa editoru, profilu nebo vašeho keymap.json vždy vyhraje nad klávesou pluginu; kolize se vypíše u pluginu ve Správci pluginů. Jeho nastavení (contributes.settings, klíče začínají id pluginu) jsou v Nastavení pod názvem pluginu, s výchozí hodnotou z manifestu, a běžící plugin dostane hodnoty při startu a pak znovu jako událost settings.changed. Příkaz, který v manifestu pojmenuje nástroj (contributes.externalTools), ho spustí bez kódu pluginu, až po vašem potvrzení příkazu a adresáře.
- Každý plugin běží jako samostatný proces. Když skončí nebo přestane odpovídat, editor ho ukončí, lišta řekne, o který plugin jde a kdy se znovu spustí; po opakovaných pádech zůstane vypnutý. Tlačítko Zobrazit výstup na liště otevře okno výstupu s posledními chybovými řádky pluginu. Plugin, který čeká na vaši odpověď (kontrolu změn, otázku), se kvůli neodpovídání neukončí. Příkaz, který zrovna běžel, se po restartu nezopakuje, takže se nic nezapíše ani nenahraje dvakrát. Počet pádů, prodlevy a časové limity jsou provizorní.

## [plugins.lifecycle] Procesy pluginů: kdy se spouštějí, jak je vidět a jak končí
1. Plugin s vlastním programem se spustí, až když o to něco požádá. Žádají události aktivace v nkspad-plugin.json (main.activationEvents): onCommand:id (spustí se příkaz), onLanguage:id (zobrazí se dokument v daném jazykovém režimu), onFileType:.přípona (zobrazí se soubor s touto příponou), workspaceContains:vzor (otevřená složka obsahuje soubor odpovídající vzoru), onStartup (editor se spustil) a onPanel:id. Bez activationEvents se plugin spustí při prvním příkazu.
2. Nástroje, Správce pluginů ukazuje stav procesu každého pluginu: běží, spouští se, neběží (spustí se, až bude potřeba), přestal běžet a čeká na restart, nebo je vypnutý. Po výběru pluginu nabídne akce, které se hodí: Spustit, Zastavit, Restartovat. Spustit u vypnutého pluginu smaže jeho pády a zkusí to znovu; Zastavit u pluginu, který čeká na restart, restart zruší.
3. Zastavit i Restartovat požádají plugin, aby skončil, zavřou mu vstup a proces ukončí, pokud neskončí v časové rezervě. Zastavený plugin není pád: neobjeví se upozornění a spustí se znovu při další události aktivace. Volání, které při zastavení běželo, skončí chybou a nezopakuje se.
4. Plugin se může v úvodní výměně zpráv přihlásit k ukončení, když nemá práci. Editor pak jeho proces po klidové době ukončí, ledaže plugin ukazuje panel, úlohu, virtuální kartu nebo zdroj, anebo běží volání, a při další události aktivace ho spustí znovu.
5. Při zavření editoru dostanou všechny pluginy najednou žádost, aby skončily. Co po krátké době stále žije, se ukončí násilně podle čísla procesu, takže žádný program pluginu nezůstane.
- Vzor workspaceContains bez lomítka odpovídá názvu souboru v libovolné hloubce, vzor s lomítkem konci cesty pod složkou; hledání je omezené počtem položek a hloubkou a přeskakuje .git a node_modules. Časy (restart, klidová doba, zavření) i limity hledání jsou provizorní.

## [tools.lint] Lintery: podtržení nálezů PHPStanu, Psalmu, golangci-lint, Clippy, clang-tidy, RuboCopu, ESLintu, Ruffu, ShellChecku, SQLFluffu a dart analyze
commands: nks.tools.lint
1. Nástroje, Spouštění nástrojů, Zkontrolovat soubor linterem spustí linter, který se pro otevřený soubor hodí: PHPStan nebo Psalm pro PHP, golangci-lint pro Go, Clippy pro Rust (potřebuje Cargo.toml ve složce souboru nebo nad ní), clang-tidy pro C a C++, RuboCop pro Ruby, ESLint pro JavaScript a TypeScript, Ruff pro Python, ShellCheck pro skripty shellu, SQLFluff pro SQL a dart analyze pro Dart. Když soubor zpracovávají dva lintery, použije se první, jehož konfigurační soubor (phpstan.neon, psalm.xml) se najde, jinak první nainstalovaný.
2. Linter kontroluje soubor na disku, takže nejdřív ukládejte: dokument s neuloženými změnami se nekontroluje. Nálezy jsou v textu podtržené (hláška se ukáže po najetí) a vypsané na kartě Úlohy, jeden na řádek; klik nebo Enter otevře místo. Další běh stejného linteru je nahradí, takže opravený problém po něm zmizí. Když soubor během kontroly upravíte, nálezy se posunou s textem a ty, jejichž text jste změnili, zmizí.
3. Chcete-li kontrolovat po každém uložení, zapněte v Soubor, Akce při uložení položku „Zkontrolovat nástrojem PHPStan“ (nebo jiného linteru), nebo napište do saveActions.json {"kind": "lint", "id": "phpstan"}. Linter se nikdy nespouští při změně. Identifikátory jsou phpstan, psalm, golangci-lint, clippy, clang-tidy, rubocop-lint, eslint, ruff, shellcheck, sqlfluff a dart-analyze. Kontrola spuštěná uložením čeká půl vteřiny na další uložení téhož souboru; novější uložení ji nahradí.
- Program se hledá v cestě, kterou zadáte, pak ve vendor/bin, node_modules/.bin a .venv (od složky souboru směrem nahoru) a v PATH; nic se nestahuje. Cestu zadáte v saveActions.json profilu: "toolPaths": {"phpstan": "C:\\nastroje\\phpstan.phar"} (soubor, nebo složka, kde program je). Platí i pro formátovače a soubor projektu ji nastavit nemůže. Cesta, která neexistuje, se ohlásí a nenahradí se hledáním. První spuštění vás požádá o potvrzení příkazu.
- Bez lintTimeoutSeconds v saveActions.json má linter provizorní limit 60 sekund; konečná hodnota není rozhodnutá. Linter, který limit překročí, se zastaví a ohlásí; editor mezitím zůstává použitelný a nálezy dřívějšího běhu zůstanou.
- PHPStan hlásí řádek a žádný sloupec, proto se podtrhne text celého řádku. Program, který je dávkový soubor (phpstan.bat, eslint.cmd), se spustí přes cmd.exe, stejně jako u každého nástroje.
- Se skutečným programem byl ověřen jen dart analyze (Dart 3.12.2); ostatní lintery se čtou podle zdokumentovaného formátu výstupu.

## [file.saveActions] Akce při uložení: formátovat, spustit nástroj, nahrát
commands: nks.file.saveActions
1. Soubor, Akce při uložení vypíše, co běží kolem uložení: formátování, nástroj, nahrání na server. Řádek akci zapne nebo vypne; řádek pod ním přepíná „jen při ručním uložení“. Formátovač se nabídne jen tehdy, když jeho konfigurace leží ve složce souboru nebo výš, a je vypnutý, dokud ho nezapnete: gofmt (go.mod), rustfmt (rustfmt.toml), clang-format (.clang-format), RuboCop (.rubocop.yml), Prettier (.prettierrc), Biome (biome.json), Ruff (ruff.toml, pyproject.toml), shfmt (.editorconfig), SQLFluff (.sqlfluff), StyLua (stylua.toml), dart format (analysis_options.yaml).
2. Pořadí je dané: nejdřív formátovače, pak uložení, pak nástroje a nahrání. Formátovač pracuje na kopii textu a po uplynutí časového limitu se zastaví. Když vyprší, skončí chybou (třeba syntaktickou) nebo není nainstalovaný, soubor se uloží tak, jak je, a stavový řádek i karta Úlohy řeknou proč. Text, který jste mezitím napsali, se nikdy nepřepíše. Formátuje se jen dokument s neuloženými změnami.
3. Nahrání pošle přesně bajty, které toto uložení zapsalo, nikdy starší ani novější. Pokud se soubor na disku po uložení změnil, nic se neodešle. Soubor otevřený ze serveru jde na tentýž server, soubor z disku na výchozí server svého projektu (bod 6).
4. Autosave spouští akce, které nejsou označené „jen při ručním uložení“. Uložení z pluginu nespustí žádnou akci, takže akce, která ukládá, nezpůsobí smyčku. Uložit jako je ruční uložení: spustí akce složky, do které ukládáte.
5. Akce jsou v saveActions.json v profilu (Otevřít soubor akcí při uložení) a pro projekt v .textida/saveActions.json ve složce souboru nebo výš. Položka projektu nahradí položku profilu se stejným druhem a identifikátorem. Příklad: {"formatTimeoutSeconds": 10, "actions": [{"kind": "format", "id": "gofmt"}, {"kind": "tool", "id": "lint", "onlyManual": true}, {"kind": "upload"}]}. Nástroj je jeden z nabídky Spustit nástroj, podle identifikátoru.
6. Výchozí server projektu se píše do .textida/saveActions.json projektu: {"kind": "upload", "server": "Web", "remoteFolder": "/var/www/web"}. Server je název uloženého připojení, remoteFolder je složka na serveru, která odpovídá složce projektu (té, ve které leží .textida). Soubor src/a.php tak jde do /var/www/web/src/a.php. Soubory ze složky .textida se nenahrávají a v profilu server zapsat nejde.
- Soubor projektu přichází s projektem, proto se první nahrání zeptá, jestli soubory projektu na ten server smějí. Souhlas platí pro tuto složku, připojení a složku na serveru. Po autosave se nikdo nezeptá a nic se neodešle.
- Editor si pamatuje verzi, kterou projekt na server naposledy poslal. Když se soubor na serveru od té doby změnil, nebo ho tam projekt nikdy neposlal a je jiný, nic se nepřepíše: ruční uložení nabídne rozdíly nebo nahrání i tak, po autosave to ohlásí stavový řádek. Stejný soubor na serveru se jen zapamatuje.
- Bez formatTimeoutSeconds má formátovač prozatímní limit 10 sekund; definitivní hodnota zatím není rozhodnutá. Hodnota v příkladu je jen příklad. Program formátovače se hledá v PATH nebo se vezme z toolPaths v saveActions.json profilu a nic se nestahuje.
- clang-format naformátuje i kód, který nejde přeložit, a chybu hlásí jen u poškozené konfigurace. dart format u kódu, který nejde přečíst, skončí kódem 65 a soubor se uloží neformátovaný.
- Po autosave nemá kdo odpovědět: nástroj, který nebyl nikdy potvrzen, se nespustí, a novější verze na serveru se ukáže v liště karty místo dialogu.
- Přepnutí akce v seznamu změní v saveActions.json v profilu jen tu jednu hodnotu, nebo přidá akci na konec seznamu. Komentáře a zbytek souboru zůstanou. Akce z projektu se vypíšou, ale mění se v souboru projektu.
- Akce při uložení nabízí také „Zkontrolovat nástrojem ...“ pro lintery, které soubor zpracovávají; je vypnutá, dokud ji nezapnete. Kontrola běží po uložení a neblokuje je; její časový limit je lintTimeoutSeconds (bez něj provizorních 60 sekund).
## [file.configFiles] Konfigurační soubory souboru
commands: nks.file.configFiles
1. Stavový řádek ukazuje konfigurační soubory, které platí pro otevřený soubor: .editorconfig, .clang-format, .clang-tidy, .rubocop.yml, rustfmt.toml, .golangci.yml a konfigurace jazyka (například ruff.toml, stylua.toml, .shellcheckrc). U každé je to nejbližší soubor ve složce souboru nebo nad ní. Klepněte na položku, nebo použijte Soubor, Konfigurační soubory tohoto souboru.
2. Ze seznamu otevřete vybraný soubor. Chybějící konfiguraci lze vytvořit z minimální šablony; zapíše se do nejbližší nadřazené složky s .git, jinak do složky souboru, a existující soubor se nikdy nepřepíše.
- Prochází se jen složky: žádný nástroj se nespouští, seznam tedy ukazuje, co je na disku, ne co by nástroj použil. Seznam se načte znovu při otevření, uložení a přejmenování souboru, takže soubor vytvořený mimo editor se ukáže po dalším uložení.
## [log.mode] Režim logu: úrovně a další chyba
commands: nks.log.nextError, nks.log.previousError, nks.log.levels
1. Dokument v jazyce Log (soubory .log, nebo Formát, Jazyk) obarví každý řádek podle jeho úrovně: chyba, varování, informace nebo ladění. Rozhoduje nejzávažnější úroveň nalezená v řádku.
2. Log: další chyba a Log: předchozí chyba skočí od kurzoru na další nebo předchozí řádek úrovně chyba. Soubor se čte po blocích a hledání skončí u prvního nálezu, takže funguje v souboru jakékoli velikosti; stavový řádek ukazuje průběh a Esc hledání zruší.
3. Log: úrovně vybere pravidla pro dokument. Výchozí pravidla hledají velkými písmeny napsaná slova ERROR, ERR, FATAL, CRITICAL, SEVERE, WARN, WARNING, INFO, DEBUG a TRACE jako celá slova. Vlastní pravidla přijdou z tools.json: "logPatterns": [{"id": "mine", "title": "Můj log", "levels": {"error": "\\|E\\|", "warning": "\\|W\\|"}}]. Klíče jsou error, warning, info a debug; každá hodnota je regulární výraz. Příkaz také zapne režim logu pro dokument.
- Pravidla platí, dokud je dokument otevřený, a neukládají se.
- Řádek delší než 8192 znaků se v editoru posuzuje podle prvních 8192 znaků.

## [log.live] Živý log: sledování, uložené pohledy, rotace
commands: nks.log.follow, nks.log.saveView, nks.log.views, nks.log.nextMatch, nks.log.previousMatch
1. Log: sledovat čte soubor znovu, kdykoli přibudou řádky, a dá kurzor na jeho konec. Funguje u souboru jakékoli velikosti, i ve stránkovaném režimu. Potřebuje dokument uložený jako soubor bez neuložených změn; při neuložených změnách se zastaví a stavový řádek to řekne. Dalším spuštěním příkazu sledování skončí.
2. Log: zapamatovat pohled se zeptá na název, regulární výraz a úrovně k ponechání (error, warning, info, debug; prázdné znamená všechny). Pohled se uloží do logviews.json v profilu spolu s pravidly úrovní dokumentu a pozicí kurzoru a stane se živým pohledem dokumentu.
3. Log: pohledy vypíše uložené pohledy. Vybraný nastaví pravidla úrovní, filtr a pozici kurzoru a jednou přečte soubor, aby našel vyhovující řádky. Byl-li pohled uložen pro jiný soubor, pozice se nepoužije.
4. Log: další shoda pohledu a Log: předchozí shoda pohledu skáčou mezi řádky, které filtr ponechal. Když soubor roste, zkoumají se jen přibylé řádky; dřív přečtená část se znovu nečte. Poslední řádek bez konce řádku se vezme, až když skončí.
5. Když jiný program log zkrátí nebo ho vymění (rotace: soubor se přejmenuje a pod starým jménem vznikne nový), řekne to stavový řádek, dřívější shody se zahodí a čtení začne znovu od začátku nového obsahu. Chybějící soubor se ohlásí jednou a čeká se na něj.
- Rotace se pozná podle prvních 4096 bajtů souboru a ve Windows i podle času vzniku. Nový soubor, který začíná stejnými bajty a není kratší než starý, se od něj nerozliší; vyměněný soubor stejné délky se stejným časem a bajty se nepozná.
- Soubor se kontroluje jednou za sekundu. Tato hodnota ani 4096 bajtů nejsou rozhodnuté hodnoty.
- Shody se drží v paměti bez horní meze.
- Log: zapamatovat pohled se ptá i na text ke zvýraznění (regulární výraz; prázdné nezvýrazní nic). Po použití pohledu mají shody výrazu v každém řádku pozadí, v editoru i ve stránkovaném režimu. Hledá se jen v prvních 8192 znacích řádku a řádek, jehož hledání trvá příliš dlouho (hodnota je placeholder, není rozhodnutá), zůstane bez značky.

## [export.highlight] Export a kopie se zvýrazněním
commands: nks.file.exportHtml, nks.file.exportRtf, nks.edit.copyFormatted, nks.edit.copyHtmlSource
1. Úpravy, Kopírovat jako, Exportovat jako HTML a Exportovat jako RTF zapíšou výběr, nebo celý dokument, když nic není označené, do souboru, který zvolíte. Barvy jsou barvy editoru ve stávajícím světlém nebo tmavém motivu: stejné tokeny gramatiky dokumentu a v logu barvy úrovní. Barva stránky a textu editoru je v exportu.
2. Úpravy, Kopírovat jako, Kopírovat se zvýrazněním dá do schránky text, HTML a RTF najednou. Vložení do textového procesoru nebo pošty zachová barvy, vložení do editoru dá prostý text. Kopírovat jako zdroj HTML dá do schránky samotné HTML jako text.
- Obarví se celý dokument a výběr se z něj vyřízne, takže výběr začínající uvnitř komentáře je obarvený jako v editoru. U velkého souboru (stránkovaný režim) a u více kurzorů nebo bloku se bere jen označený text a barví se od svého začátku.
- Text nad 8388608 znaků se odmítne zprávou, označte jeho část. Mez je placeholder, není rozhodnutá hodnota. Práce běží na pozadí, okno zůstává použitelné.
- HTML je UTF-8. RTF je ASCII: zpětné lomítko, složené závorky a každý znak mimo ASCII (například česká písmena s diakritikou) se zapíšou jako escape.

## [log.full] Plný režim logu: sbalení, stack trace, heatmapa, barvy ANSI
commands: nks.log.fold, nks.log.unfold, nks.log.openFrame, nks.log.showRaw
1. Log: Sbalit nezajímavé řádky se zeptá na úroveň (zůstanou řádky aspoň této úrovně: error, warning, info, debug) a na regulární výraz (zůstanou řádky, které mu vyhovují). Každý úsek ostatních řádků se změní v jeden zástupný řádek, který se klepnutím otevře. Řádek bez slova úrovně, například řádek stack trace, patří k záznamu nad ním. Log: Zobrazit všechny řádky vše znovu otevře. Ve stránkovaném (velmi velkém) souboru se řádky sbalit nedají; řádky, které by se sbalily, se místo toho kreslí tlumeně.
2. Ctrl+klepnutí na řádek stack trace z .NET, Javy, Pythonu, PHP nebo Node.js otevře soubor na daném řádku; Log: Otevřít soubor řádku stack trace to udělá pro řádek s kurzorem. Relativní cesta se hledá ve složce logu, potom ve složkách pracovního prostoru; holé jméno souboru (Java) se hledá pod nimi.
3. Pruh u pravého okraje minimapy ukazuje, kde jsou hustě chybové řádky (červeně) a varování (žlutě). Počítá se na pozadí. Soubor do 256 MiB se čte celý, větší se vzorkuje, takže obraz je odhad. Tyto meze nejsou rozhodnuté hodnoty.
4. Escape sekvence ANSI v logu se skryjí a kreslí se jako barvy (16, 256 i true color), tučné písmo a podtržení. Soubor se nikdy nemění, takže uložení escape sekvence zachová. Log: Zobrazit escape sekvence ANSI nezpracované přepíná mezi barvami a holými sekvencemi. Styl, který řádek nechá otevřený, se do dalšího řádku nepřenáší.

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
- Karta výsledků hledání záložky nemá. V hexu tytéž příkazy označují oblasti bajtů (viz Hex: záložky). Ve velkém souboru, jehož řádky se ještě číslují, příkaz napíše, že index řádků ještě nesahá na to místo.

## [recovery] Obnova po pádu
Program průběžně zapisuje neuložený text do žurnálu, nejdéle do dvou sekund od změny. Neuložený dokument se tak neztratí, ani když program spadne nebo vypnete počítač.
1. Spusťte program znovu.
2. Karty se vrátí včetně neuloženého textu. Nad dokumentem je lišta Po neočekávaném ukončení se vrátily karty a v závorce počet karet s neuloženými změnami.
3. Tlačítko Podrobnosti vypíše každý dokument: obnoveno včetně neuložených změn, znovu otevřeno ze souboru, soubor se mezitím změnil, soubor už neexistuje, nebo nepodařilo se obnovit s důvodem.
- Když se soubor mezitím změnil na disku, změny zůstanou v žurnálu a nic se nepřepíše.
- Žurnál leží ve složce profilu. Jinou složku určuje nastavení Složka žurnálu, náhradní Náhradní složka žurnálu.
- Nejde-li žurnál zapisovat do žádné složky, lišta řekne: Obnova po pádu je vypnutá. Neuložený text pak zůstává jen v kartách; ukládejte ručně. Když přestane fungovat za provozu: Žurnál přestal fungovat, obnova už neplatí, ukládejte ručně.
- Použije-li se náhradní složka, lišta napíše, která.
- Přerušená dávka změn v souborech (nahrazení v souborech i soubory, které úprava pluginu napříč pracovním prostorem vytváří, přejmenovává nebo maže) se po startu nabídne: Dokončit, nebo Vrátit zpět. Soubor, který mezitím změnil někdo jiný, zůstane, jak je.
### Velké změny
- Když jsou neuložené změny jednoho dokumentu větší než limit obnovy (64 MiB, zatím provizorní hodnota), třeba Nahradit vše nebo velké vložení ve velkém souboru, jeho karta se sama nevrátí. Lišta napíše soubor, velikost změn a kdy vznikly a nabídne Obnovit změny a Zahodit změny. Dokud nevyberete, nečte se ze žurnálu nic a soubor se nedotkne.
- Obnovit změny čte žurnál na pozadí, takže program dál pracuje, a pak otevře kartu s neuloženými změnami jako po každé jiné obnově. Když se to nepodaří (soubor se mezitím změnil, část žurnálu chybí nebo je poškozená, nestačí paměť), lišta řekne proč a změny zůstanou v žurnálu.
- Zahodit změny se ještě jednou zeptá, protože změny už nic nevrátí; pak se smažou ze žurnálu. Soubor na disku se žádnou z voleb nemění.
- Zavření lišty nic nerozhoduje: lišta se vrátí při příštím spuštění.
- Nahradit vše s velmi mnoha shodami se do žurnálu zapíše jako seznam v samostatném souboru, nikdy jako jeden obří záznam, takže žurnál funguje, kolik shod je. Poškozený nebo chybějící seznam zastaví jen návrat toho dokumentu, s důvodem.

## [paged] Velké soubory (stránkovaný režim)
Soubor větší než hranice velkého souboru (nastavení Hranice velkého souboru, výchozí 6 MiB) se otevře ve stránkovaném režimu. Chcete-li soubor procházet a upravovat po bajtech bez těchto omezení, otevřete ho v hexu (téma Hex: otevření a pohyb). Program čte jen řádky na obrazovce, takže soubor se otevře rychle a nezabere paměť. Části souboru, které už přečetl (třeba při číslování řádků), vrací systému, takže obsazená paměť neroste s velikostí souboru.
Stavový řádek pak ukazuje Velký soubor, jeho velikost a revizi.
### Co jde
- Procházet šipkami, PageUp, PageDown, Home, End a Ctrl+Home a Ctrl+End. Přejít na řádek, na procento nebo na bajt (Ctrl+G).
- Psát, mazat (Backspace a Delete), Enter a Tab na místě kurzoru. Zpět a Znovu. Uložit.
- Uložení (Ctrl+S) běží na pozadí: okno zůstává použitelné, stavový řádek ukazuje, kolik procent nového souboru je zapsáno, a Esc uložení zruší dřív, než se data potvrdí (soubor zůstane beze změny). Můžete mezitím psát dál: do souboru se zapíše text, jaký byl ve chvíli stisku Uložit, pozdější úpravy zůstanou neuložené a karta je ukazuje jako neuložené změny. Zpět a Znovu fungují během uložení i po něm. Uložení, které jen přepisuje bajty stejné délky, zapíše jen tyto bajty a po svou krátkou dobu úpravy nepřijímá. Soubor větší než 1 GiB (zatím provizorní limit) se uloží bez kopie staré verze ve složce záloh; starý soubor zůstane nedotčený, dokud není nový celý.

- Zpět a Znovu fungují i po uložení: kroky provedené před uložením jdou dál vrátit a další uložení zapíše stav po vrácení. Napsané slovo bez přesunu kurzoru je jeden krok.
- Hledat (Ctrl+F). Hledání v souborech.
- Výběr (Shift se šipkami, myš, Ctrl+A), Kopírovat, Vyjmout a Vložit. Výběr větší než limit kopírování (256 MiB, zatím provizorní hodnota) se nezkopíruje a stavový řádek to řekne. Dlouhý výběr se čte na pozadí a vyjmutí smaže jen tehdy, když se text mezitím nezměnil.
- Vložit přijme nejvýš 32 Mi znaků (zatím provizorní hodnota); delší text se nevloží a stavový řádek to řekne. Text nad 1 Mi znaků se připraví na pozadí a vloží se tam, kde je kurzor, až bude hotovo.
- Řádek delší než 16 KiB posunete do stran posuvníkem pod textem. Vzdálená část řádku se na chvíli ukáže jako …, dokud se nenačte. Barvy v ní přibudou, jakmile program přečte řádek až k ní.
- Řádek o desítkách či stovkách MiB se upravuje na místě: psaní, Backspace, Delete, Enter, Tab, vložení, výběr i Zpět čtou jen text kolem kurzoru, nikdy celý řádek, takže paměť neroste s délkou řádku. První přesun kurzoru do vzdálené části takového řádku musí napočítat znaky až k ní, což chvíli trvá; počet si program pamatuje pro další přesuny. Řádek delší než 1 GiB (zatím provizorní hodnota) se neupravuje; stavový řádek to řekne a dokument se nezmění.
### Co nejde
- Blokový výběr, více kurzorů.
- Příkazy nad řádky, odsazením, velikostí písmen, nahrazení.
- Přejít na párovou závorku, zalamování řádků.
- Převod a opětovné čtení kódování, sjednocení konců řádků, normalizace.
- Uložit jako jen ve stejném kódování.
### Hlášení
- Číslování řádků ještě běží, úpravy půjdou za chvíli: počkejte, až se soubor dočte.
- Znak se nedá zapsat v kódování souboru: napište jiný znak.
- Výběr je větší než limit kopírování: vyberte méně; schránka zůstala, jak byla.
- Úprava je příliš velká, než aby šla vrátit zpět: odeberte nebo vložte najednou méně textu; dokument se nezměnil.
- Řádek je delší než limit, který se ve stránkovaném režimu upravuje: upravte ho v hexu; dokument se nezměnil.
- Text ve schránce je větší než limit vložení: vložte ho po částech; dokument se nezměnil.
- Kódování dokumentu se změnilo, zatímco se text připravoval: vložte ho znovu.
- Velký soubor v kódování ... se zatím nedá otevřít: soubor se neotevře; jiné kódování ve velkém souboru program zatím nepřečte.
- Soubor se právě ukládá, zkuste to po skončení uložení: zkusili jste něco během uložení, které přepisuje bajty na místě, nebo jste soubor načetli znovu během jakéhokoli uložení.
- Zavření karty nebo okna během uložení počká na konec uložení; Esc uložení zruší.

## [conflict] Změna souboru zvenku
Změní-li soubor jiný program, nad dokumentem se ukáže lišta Soubor se na disku změnil.
- Znovu načíst zahodí vaše změny a načte soubor z disku.
- Ponechat moje nechá vaše změny; další uložení se zeptá, než soubor přepíše.
- Porovnat ukáže rozdíly mezi vaší kartou a souborem na disku (téma Porovnat s diskem).
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

## [errors.remote] Chybová hlášení: vzdálené soubory a zdroje
Hlášení o vzdáleném souboru nebo zdroji se objeví ve stavovém řádku, v liště nad textem nebo v dialogu. Každý řádek cituje hlášení a říká, co dělat.
- Nelze se připojit k název: důvod. Zkontrolujte jméno serveru, port, uživatele, heslo nebo klíč i svou síť. Používáte-li proxy, zkontrolujte i ji (téma Síťová proxy).
- POZOR: identita serveru server není ta, které jste dříve uvěřili. Program se nepřipojí, dokud novou identitu nepotvrdíte. Zeptejte se správce serveru, zda server nebyl přeinstalován, a připojte se jen tehdy, když víte, proč se změnila (téma Vzdálená připojení).
- Port musí být číslo od 1 do 65535. Napište celé číslo od 1 do 65535, nebo nechte pole prázdné a použije se obvyklý port protokolu.
- Připojení se neuložilo: důvod. Důvod říká, co selhalo: složka profilu (oprávnění, místo na disku) nebo úložiště přihlašovacích údajů (funguje jen ve Windows, téma Co tato verze neumí). Opravte to a uložte znovu.
- cesta na serveru neexistuje. Zkontrolujte cestu. Cesta ke složce ukáže její obsah, takže k souboru můžete dojít od /.
- Soubor cesta nelze otevřít: důvod. Nic se neotevřelo. Odstraňte důvod (oprávnění na serveru, přerušené připojení) a otevřete soubor znovu.
- Nahrání souboru název selhalo: důvod. Server si ponechává předchozí verzi, vaše změny zůstávají uložené tady. Odstraňte důvod a zvolte znovu Nahrát na server; částečně odeslaný soubor se doplní (téma Nahrát na server).
- Kontrola se serverem selhala: důvod. Nic se nestáhlo ani nezměnilo. Až bude připojení fungovat, zvolte v liště Zkontrolovat znovu, nebo Zkontrolovat verzi na serveru.
- Místní kopie souboru cesta byla smazána. Otevřete soubor ze serveru znovu (Otevřít vzdálený soubor).
- Soubor není použitelný seznam připojení: důvod. Zvolte soubor vytvořený příkazem Exportovat připojení (téma Záloha připojení).
- Adresu adresa neobsluhuje žádný plugin. Porovnejte schéma na začátku adresy se seznamem v Otevřít ze zdroje pluginu. Vypnutý plugin nic neposkytuje (téma Správce pluginů: instalace, aktualizace, návrat a odebrání pluginů).
- Nelze otevřít adresa: důvod. Nic se neotevřelo. Odstraňte důvod a zkuste to znovu.
- Nelze uložit název: důvod. Váš text zůstává v kartě. Odstraňte důvod a uložte znovu, nebo použijte Uložit jako a zapište kopii na disk; tím se spojení se zdrojem přeruší.
- Zdroj název zápis nepřijímá. Tento zdroj jen čte. Pomocí Uložit jako si ponechte kopii na disku; tím se spojení se zdrojem přeruší.
- název nepřijal název: důvod. Změny zůstaly v kartě. Odstraňte důvod a uložte znovu. Plugin musí běžet (téma Karty, které dodá plugin).
- Tato karta je jen pro čtení, protože je soubor zamčený. Použijte na liště Přesto upravit, nebo Uložit jako. Zvolte v liště Přesto upravit: každé uložení pak nejdřív zkontroluje soubor na disku. Nebo použijte Uložit jako (téma Zamčené soubory).

## [errors.plugins] Chybová hlášení: pluginy
Plugin, který nejde nainstalovat nebo neběží, se ohlásí v dialogu, v liště nad textem nebo ve stavovém řádku. Každý řádek cituje hlášení a říká, co dělat.
- Soubor není čitelný ZIP. Nic se nenainstalovalo. Stáhněte ZIP pluginu znovu nebo požádejte autora o nový.
- ZIP obsahuje cestu, která by se zapsala mimo složku pluginu. Nic se nenainstalovalo. Autor musí plugin zabalit znovu.
- ZIP se rozbalí na víc, než je povoleno (zatím provizorní limit). Nic se nenainstalovalo. Požádejte autora o menší balíček; limit je provizorní.
- ZIP nemá v kořeni soubor nkspad-plugin.json. Soubor musí ležet přímo v kořeni ZIPu, ne v podsložce.
- Manifest je odmítnut: Řádky za ním říkají, která položka je špatně; autor musí opravit nkspad-plugin.json.
- ID pluginu patří vestavěnému pluginu. Id vestavěného pluginu nejde použít; autor musí zvolit jiné id.
- Stejná nebo novější verze tohoto pluginu už je nainstalovaná. Nic se nenainstalovalo. Správce se umí vrátit jen na předchozí verzi, kterou si ponechal.
- Tento ZIP obsahuje jiný plugin (id), ne aktualizaci vybraného pluginu. Zvolte plugin, kterému ZIP patří, nebo ho nainstalujte jako nový plugin příkazem Nainstalovat plugin ze ZIP.
- Plugin se nepodařilo nainstalovat: Důvod následuje za hlášením. Nic se nenainstalovalo.
- Zásuvný modul název se nepodařilo spustit: důvod. Přečtěte si důvod. Zmizel-li program pluginu, nainstalujte plugin znovu z jeho ZIPu.
- Zásuvný modul název přestal běžet (důvod). Znovu se spustí za N s. Počkejte: plugin se spustí sám znovu. Zobrazit výstup v liště otevře poslední chybové řádky pluginu.
- Zásuvný modul název přestal běžet (důvod). Po opakovaných pádech je vypnutý. Zůstane vypnutý. Zobrazit výstup v liště řekne proč; plugin aktualizujte nebo nainstalujte znovu a zapněte ho ve Správci pluginů.
- Karta název se zavřela: její zásuvný modul ji zavřel nebo skončil. Neuložené změny jsou ztraceny. Kartu uložte dřív, než se plugin zastaví: Uložit pošle text pluginu (téma Karty, které dodá plugin).

## [errors.agents] Chybová hlášení: MCP, /ide a agenti
Hlášení serveru MCP, mostu /ide a agentů se objevují ve stavovém řádku, v dialozích a v panelu agenta. Každý řádek cituje hlášení a říká, co dělat. Nic se na vaše karty nepoužije bez vašeho potvrzení, takže nezdařený krok je nechá, jak byly.
- Server MCP se nepodařilo spustit: důvod. Odstraňte důvod a zvolte Server MCP znovu. Zatím se nic nesdílí.
- Nejdřív zapněte server MCP (Nástroje, Server MCP). Server zapněte a kartu pak sdílejte znovu (téma Editor jako server MCP).
- Tuto kartu nelze sdílet. Sdílet jde jen textové karty. Porovnání ani seznam výsledků hledání ne.
- Výběr je větší než N B. Vyberte méně, nebo sdílejte celou kartu. Vyberte méně, nebo sdílejte celou kartu.
- Most /ide se nepodařilo spustit: důvod. Odstraňte důvod a zvolte Most /ide znovu.
- Most /ide byl vypnut: důvod. Agent mluví verzí protokolu, kterou Textida nezná. V kartách se nic nezměnilo. Použijte verzi Claude Code, která mluví protokolem, který Textida zná, a most pak zapněte znovu (téma Most /ide pro Claude Code).
- K mostu /ide není připojený žádný agent. Spusťte /ide v Claude Code v terminálu ve složce tohoto okna.
- název se nepodařilo vyzkoušet: důvod. Důvod uvádí chybu a poslední řádky, které program napsal na standardní chybový výstup. Opravte nastavení serveru v souboru jeho agenta (Textida ten soubor nikdy nemění) a zkuste to znovu.
- Bez odpovědi do N s. Serveru bylo řečeno, ať ten požadavek ukončí. Ověřte, že server mimo Textidu startuje a odpovídá, a zkuste to znovu.
- Úryvek pro agent neprošel kontrolou formátu: důvod. Nic se nezapsalo. Položka používá něco, co formát druhého agenta neumí; opravte ji v jejím zdrojovém souboru a převeďte znovu.
- název selhal: důvod. Seznam se zavře. Poslední řádky programu jsou ve výstupu Úlohy.
- Pro název nebyla zadána hodnota, nic se nespustilo. Připojte se znovu a hodnotu napište; uloží se do úložiště přihlašovacích údajů a nikam jinam.
- název odmítl klíč. Uložená hodnota proměnné byla zapomenuta, zeptám se znovu. Až se vás program zeptá, napište nový klíč.
- název odmítl klíč. Nepochází z úložiště přihlašovacích údajů Textidy (je v prostředí nebo přímo v konfiguraci), změňte ho tam. Změňte klíč tam, kde je, v prostředí nebo v konfiguračním souboru; Textida ho tam nemění.
- Není to objekt JSON. Argumenty napište jako objekt JSON, například {"name": "value"}; {} znamená žádné.
- Zobrazeno je prvních N z N znaků. Zbytek je useknutý. Požádejte nástroj o méně (téma Servery MCP agentů).
- agent se nepodařilo spustit: důvod. Ověřte, že je program nainstalovaný a spustí se v terminálu; Textida žádné agenty neinstaluje.
- Nejdřív otevřete soubor nebo složku; agent pracuje v její složce. Nejdřív otevřete soubor nebo složku a pak agenta spusťte.
- Agent už běží. Nejdřív ho zastavte příkazem Zastavit agenta.
- Nejdřív spusťte agenta. Spusťte agenta příkazem Spustit agenta a pak kartu sdílejte (téma Agent přes ACP).
- Tuto kartu nelze s agentem sdílet. Agent pojmenovává soubor cestou: kartu nejdřív uložte. Porovnání ani seznam výsledků hledání sdílet nejde.
- Tah ještě běží. Nejdřív ho zrušte. Zvolte Zrušit tah agenta, nebo počkejte, až tah skončí.
- Agent neodpověděl včas: důvod. Tah skončil. Pošlete zprávu znovu, nebo agenta zastavte a spusťte znovu.
- Agent selhal: důvod. Přečtěte si důvod v panelu; pokud agent už neběží, spusťte ho znovu.

## [errors.tools] Chybová hlášení: nástroje, lintery, jazykové servery a Git
Tato hlášení pocházejí z programů, které editor spouští za vás. Objevují se ve stavovém řádku, ve výstupu Úlohy nebo v liště nad textem. Každý řádek cituje hlášení a říká, co dělat.
- název ještě běží. Nejdřív ho zastavte. Běží vždy jen jeden nástroj. Počkejte na něj, nebo zvolte Zastavit běžící nástroj.
- název nelze spustit: důvod. Důvod uvádí proměnnou nebo program. Neznámá proměnná, nebo taková, která pro tento dokument neplatí, zastaví běh dřív, než cokoli začne (téma Nástroje: spuštění externího programu).
- Zastaveno: uplynul časový limit N s. Zvyšte timeoutSeconds v definici nástroje, nebo nastavení Výchozí časový limit nástrojů.
- název neběžel: program se nenašel ve vendor/bin, node_modules/.bin, .venv ani v PATH. Nainstalujte ho tam nebo uveďte jeho cestu v toolPaths v saveActions.json. Nainstalujte program, nebo uveďte jeho cestu jako toolPaths v saveActions.json (téma Lintery).
- název nedoběhl za N s a byl zastaven. Nálezy z dřívějšího běhu zůstávají. Zvyšte lintTimeoutSeconds v saveActions.json.
- Uloženo bez formátování: důvod. Soubor se uložil, jak byl. Odstraňte důvod a uložte znovu, nebo formátování vypněte (téma Akce při uložení: formátovat, spustit nástroj, nahrát).
- Nástroj ještě nebyl potvrzen; spusťte ho jednou ručně přes Spustit nástroj. Spusťte nástroj jednou příkazem Spustit nástroj a potvrďte jeho příkaz a složku; pak může běžet při uložení.
- Nahrávání souborů z folder na název není povolené; uložte soubor projektu ručně a povolte ho. Uložte soubor projektu ručně: program se zeptá a nahrávání pro ten projekt můžete povolit.
- Dotazy na registr nejsou možné: nejsou povoleny, nebo proxy vyžaduje přihlášení. Zvolte Nástroje, Balíčky, Zkontrolovat verze balíčků a pro registr souhlaste. Proxy s uživatelským jménem se pro dotazy použít nedá (téma Manifesty balíčků: názvy, verze a značka novější verze).
- schéma název: stažení není povoleno. Zvolte Nástroje, Balíčky, Povolit stažení schématu a pro server souhlaste (téma Schémata pro JSON, YAML a TOML: kontrola, doplňování a popisy).
- Vedle otevřeného souboru ani v nadřazených složkách není Makefile. Otevřete soubor ve složce, která má Makefile, nebo přímo Makefile (téma Makefile: spuštění cíle).
- Jazykový server pracuje jen s textovými dokumenty, ne s velkými soubory zobrazenými po stránkách. Soubor ve stránkovaném režimu se jazykovému serveru nikdy nepředá (téma Velké soubory (stránkovaný režim)).
- Jazykový server pro jazyk nebyl nalezen: program. Nainstalujte server tak, aby se jeho program našel v PATH (téma Jazykový server: definice, odkazy, přejmenování, doplňování a diagnostika).
- Jazykový server pro jazyk se nepodařilo spustit: důvod. Přečtěte si důvod, odstraňte ho a spusťte příkaz té skupiny znovu.
- Jazykový server pro jazyk neočekávaně skončil (podrobnost). Spusťte libovolný příkaz té skupiny znovu: server se spustí a dostane otevřené dokumenty znovu.
- Program git se nepodařilo spustit: důvod. Program git musí být nainstalovaný a nalezený v PATH (téma Git: stav, porovnání a příprava úseku).
- Soubor není v repozitáři Git. Otevřete soubor z repozitáře Git.
- Index souboru soubor se změnil od posledního načtení (jiným programem). Nic se nezapsalo. Zkontrolujte změny a připravte úsek znovu.
- Soubor má nevyřešený konflikt slučování. Vyřešte ho a teprve pak připravujte úseky. Vyřešte konflikt příkazem Git: konflikt, vzít naše změny / vzít jejich změny / vzít obě strany, soubor uložte a pak zvolte Git: označit soubor jako vyřešený.

## [limits] Co tato verze neumí
Nápověda popisuje jen to, co tato verze umí. Tyto věci v ní nejsou a tady je, co dělat místo nich:
- Aktualizace: program se sám aktualizuje jen ve Windows, v nainstalované Textidě (téma Aktualizace a návrat na předchozí verzi). V macOS a Linuxu novou verzi nainstalujete sami. Sestavení bez adresy aktualizací a podpisového klíče nové verze nehledá a řekne to.
- Uložená hesla: úložiště přihlašovacích údajů funguje jen ve Windows (Správce přihlašovacích údajů). V macOS a Linuxu program heslo nikam neukládá a ptá se na ně při každém připojení (téma Vzdálená připojení).
- Pluginy: plugin je program, který běží s právy vašeho uživatele. Jeho oprávnění jsou záznam toho, co jste povolili, ne izolace, a podpisy pluginů se nekontrolují (téma Správce pluginů: instalace, aktualizace, návrat a odebrání pluginů). Instalujte jen pluginy, kterým věříte.
- Asistenti: most /ide a agent přes ACP nejsou ověřené se skutečnými programy (témata Most /ide pro Claude Code a Agent přes ACP). Připojení, které nejde, program ohlásí s důvodem ve stavovém řádku nebo ve výstupu Úlohy.
- Velké soubory: úpravy ve stránkovaném režimu jsou omezené (téma Velké soubory (stránkovaný režim)). Jazykový server, formátování a většina příkazů, které mění text, tam nefungují. Bajty souboru jakékoli velikosti změníte v hexu.
- Hex: dvě revize z místní historie nejde porovnat bajt po bajtu a šablony struktury jsou podmnožina formátu .ksy (téma Hex: šablona struktury).
- Programy, které editor jen spouští (git, lintery, formátovače, jazykové servery, make, npm, Composer, HTML Tidy): musí být nainstalované a nalezené v PATH. Editor žádný z nich nestahuje.
- Nabídka Průzkumníka a přiřazení souborů: jen Windows.
- Jazyky rozhraní: čeština, angličtina, slovenština a němčina. Staré překlady PSPadu se převzít nedají.

## [edit.clips] Klipy
commands: nks.edit.insertClip, nks.edit.clipFromSelection, nks.clips.editVariables, nks.snippet.next, nks.snippet.previous, nks.snippet.end
Klip je kus textu s místy pro proměnné a kurzory. Klipy dodávají datové pluginy ve složce plugins v profilu; jeden podadresář na plugin, se souborem nkspad-plugin.json a bez programu.
1. Zvolte Úpravy, Vložit, Vložit klip. Seznam ukazuje klipy s náhledem textu, který by se vložil.
2. Vyberte klip. Má-li formulář, vyplňte ho: náhled sleduje, co píšete, špatná hodnota je označená a OK zůstane zašedlé, dokud každá hodnota nesedí k svému typu.
3. Text se vloží do každého kurzoru a nahradí každý výběr, jako jeden krok Zpět. Kurzory skončí tam, kde má klip značku $|.
- Uložit výběr jako klip se zeptá na název a přidá označený text do vaší vlastní knihovny (plugin user.clips, složka user-clips). Text se uloží doslova, takže dolar v něm zůstane dolarem. Klip je v seznamu hned.
- Jazyk šablon: ${name} je proměnná, $| značí kurzor (kolik chcete), $$ je znak dolaru. Konce řádků se změní na konec řádku dokumentu.
- Snippety: klip (a volání pluginu snippet/insert s "format": "template") smí používat číslovaná zástupná místa: $1, ${1:výchozí text}, ${1|jedna,dva,tři|}, $0 (konečný kurzor). Stejné číslo na více místech se upravuje společně. Po vložení je vybráno první místo; Tab skočí na další, Shift+Tab na předchozí a po posledním jde kurzor na $0. Esc nebo klik mimo snippet ho ukončí. Místo se seznamem hodnot otevře seznam k výběru. Při více kurzorech má každý svůj snippet a místa se procházejí společně.
- Řádky snippetu za prvním dostanou odsazení řádku, na kterém snippet začíná; tabulátory v jeho vlastním odsazení se změní na odsazení dokumentu (tabulátory nebo mezery). Klip bez číslovaných míst se vkládá tak, jak je.
- Ve snippetu $| není kurzor a dolar před číslicí je zástupné místo: dolar napište $$ nebo \$. Kromě ${name} fungují proměnné TM_SELECTED_TEXT, TM_FILENAME, TM_FILENAME_BASE, TM_DIRECTORY, TM_FILEPATH, UUID a CURRENT_YEAR, CURRENT_YEAR_SHORT, CURRENT_MONTH, CURRENT_DATE, CURRENT_HOUR, CURRENT_MINUTE, CURRENT_SECOND (i jako $NAME). Vnořená místa a transformace nejsou podporovány; text, který do syntaxe nezapadá, zůstane, jak je.
- Odkud proměnná bere hodnotu, od nejsilnějšího: 1. hodnota napsaná ve formuláři klipu, 2. vaše vlastní proměnné (Úpravy, Vložit, Upravit uživatelské proměnné otevře soubor variables.json v profilu: jeden objekt JSON se jménem a textem), 3. vestavěné. Stejné jméno dá stejnou hodnotu v klipu, receptu i externím nástroji.
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
1. Zvolte Úpravy, Převody a recepty, Převést. Seznam ukáže převody s náhledem na označeném textu (když nic není označeno, na prvním vzorku převodníku).
2. Vyberte jeden. Všechny výběry se změní jedním krokem Zpět. Když se některý nedá převést, nic se nezmění a stavový řádek řekne proč.
- Zopakovat poslední příkaz spustí poslední převod bez seznamu.
- Převodníky jsou data: contributes.converters v nkspad-plugin.json, s id, title, rules a volitelně samples a command. Pravidlo je {"find": "&", "replace": "&amp;", "regex": false, "ignoreCase": false}. Pravidla běží popořadě, každé nad výsledkem předchozího. Se zapnutým regex platí dialekt hledání: replace smí používat $1, ${name}, $& a kódy velikosti písmen \U \L \E \u \l.
- Vzorek je {"input": "...", "expected": "..."}. Při čtení pluginu se každý vzorek převede. Převodník, jehož vzorek dá jiný text, který nemá pravidla, má prázdné find nebo chybný regulární výraz, se odmítne a plugin se přeskočí s důvodem ve stavovém řádku.
- Převodník s "command" dostane i příkaz toho jména, který převádí hned, bez seznamu.
- Speciální převody samotného editoru jsou ve stejném seznamu (téma Speciální převody).

## [edit.specialConversion] Speciální převody
commands: nks.edit.convert.decToHex, nks.edit.convert.hexToDec, nks.edit.convert.urlEncode, nks.edit.convert.urlDecode, nks.edit.convert.quotedPrintableEncode, nks.edit.convert.quotedPrintableDecode, nks.edit.convert.base64Encode, nks.edit.convert.base64Decode, nks.edit.convert.removeAccents, nks.edit.convert.tabsToSpaces
Převody, které má editor sám. Každý je příkaz v paletě i v nabídce Úpravy, Převody a recepty a je také v seznamu Převést. Změní text všech výběrů najednou jedním krokem Zpět, nebo celého souboru, když nic není označeno. Nový text zůstane označený.
- Desítkové číslo na šestnáctkové a zpět: výběr musí být jedno celé číslo od 0 do 18446744073709551615 (FFFFFFFFFFFFFFFF), šestnáctkové smí začínat 0x. Mezery a konce řádků kolem čísla zůstanou. 255 dá FF, ACE dá 2766.
- Text na URL kódování zapíše všechno kromě písmen A až Z, číslic a znaků - _ . ~ jako %XX bajtů UTF-8, mezeru jako %20 a konec řádku jako %0D%0A, nebo %0A v souboru s konci řádků LF. URL kódování na text čte %XX zpět; % bez dvou šestnáctkových číslic zůstane, jak je.
- Text na quoted-printable nechá tisknutelné znaky ASCII a mezery, ostatní bajty UTF-8 včetně konců řádků a znaku = zapíše jako =XX. Quoted-printable na text čte =XX a vynechá = na konci řádku; jiné = převod odmítne.
- Text na Base64 kóduje bajty UTF-8. Base64 na text přeskočí mezery a konce řádků uvnitř a odmítne text, který není Base64 nebo jehož bajty nejsou UTF-8.
- Odstranit diakritiku sundá z písmen čárky, háčky, kroužky a přehlásky (č na c, ů na u). Písmena bez rozkladu (ł, ø, ß) a značky jiných písem zůstanou.
- Tabulátory na mezery nahradí každý tabulátor mezerami do další zarážky podle velikosti tabulátoru dokumentu. Počítá se od sloupce, kde výběr začíná.
- Když převod některý výběr nepřečte, nezmění se nic a stavový řádek to řekne. Zopakovat poslední příkaz spustí stejný převod znovu.

## [edit.recipes] Recepty úprav
commands: nks.edit.runRecipe
Recept vybere části otevřeného souboru, změní je a před zápisem ukáže výsledek.
1. Zvolte Úpravy, Převody a recepty, Spustit recept úprav. Seznam ukáže každý recept se změnami, které by v tomto souboru udělal.
2. Vyberte jeden. Okno náhledu vypíše každou změnu: řádek, sloupec, starý a nový text. Tlačítkem Použít je zapíšete.
3. Všechny změny jsou jeden krok Zpět; Zpět vrátí soubor přesně do původního stavu. Změněné části zůstanou označené. Nic se nezapíše, když se soubor mezitím změnil nebo recept nejde spustit (proměnná bez hodnoty, příliš pomalý vzor).
- Recept pracuje s výběry, nebo s celým souborem, když nic není označeno. Zopakovat poslední příkaz použije poslední recept bez náhledu.
- Recepty jsou data: contributes.recipes v nkspad-plugin.json, s id, title, steps a volitelně command. Kroky, které vybírají, jdou první. {"kind": "matches", "find": "...", "regex": false, "ignoreCase": false, "wholeWord": false} ponechá shody uvnitř dosavadních částí. {"kind": "columns", "from": 3, "to": 5} ponechá sloupce 3 až 5 každého řádku (počítá se ve znacích od 1; to 0 nebo vynechané znamená do konce řádku; kratší řádek nedá nic).
- Kroky, které mění: {"kind": "convert", "converter": "id"} (převodník stejného pluginu), {"kind": "replace", "find": "...", "replacement": "...", "regex": false} a {"kind": "set", "text": "..."}, který nahradí část textem v jazyce šablon klipů. V něm je ${selection} samotná část a proměnné mají stejné hodnoty jako v klipu (téma Klipy); kurzory $| se ignorují.
- Recept bez kroků, s neznámým druhem, chybným vzorem, neznámým převodníkem, vybírajícím krokem po měnícím nebo bez měnícího kroku se při čtení pluginu odmítne.

## [edit.recipeFiles] Recepty nad soubory
commands: nks.edit.runRecipeFiles
Uložený recept se pustí znovu nad jinou sadou souborů. Každou změnu uvidíte předem; nic se nezapíše, dokud ji nepoužijete.
1. Zvolte Úpravy, Převody a recepty, Spustit recept nad soubory a vyberte recept: z datového pluginu, nebo soubor ve složce recipes v profilu.
2. Vyberte soubory: složku s maskou souborů (například *.cs;*.txt; platí ignorovací soubory hledání), otevřené karty, nebo soubory z výsledků hledání.
3. Karta Výsledky hledání ukáže změny po souborech se stejnými zaškrtávátky jako Replace in Files. Použít zapíše zaškrtnuté. Soubor, který po náhledu změnil někdo jiný, se nepřepíše: ohlásí se jako konflikt a ostatní soubory se zapíšou. Otevřené dokumenty dostanou jeden krok Zpět.
- Soubor s receptem je JSON s id, title a steps, stejná pole jako recept v nkspad-plugin.json, a volitelně version a seznam converters (převodníky, které jmenují kroky convert). Soubor bez version je verze 1, formát prvních receptů, a načte se jako dřív. Soubor novější verze se odmítne se zprávou.
- Kroky: matches, columns, convert, replace a set fungují jako v Receptech úprav (každý soubor je celý svým textem). Krok {"kind": "format", "formatter": "id"} pustí formátovač z Akcí při uložení nad výsledkem předchozích kroků; musí být poslední, jeho časový limit je formatTimeoutSeconds v saveActions.json (bez něj prozatímních 10 sekund) a pracuje se soubory s názvem a příponou, kterou ten formátovač zná. Soubor, kde krok nejde provést, se vynechá a ohlásí s důvodem.
- Každý soubor si drží své kódování a konce řádků; nový znak, který kódování neunese, se ukáže jako změna, kterou nejde použít. Krok format se nad jedním souborem v Spustit recept úprav nespustí.

## [edit.aiContext] Kopírovat jako kontext pro AI
commands: nks.edit.copyAiContext
Vloží do schránky výběr, soubor, otevřené karty nebo otevřenou složku jako jeden blok v Markdownu nebo XML, který vložíte do AI asistenta. Nic se nikam neposílá.
1. Zvolte Úpravy, Kopírovat jako kontext pro AI a vyberte, co kopírovat: Výběr (všechny výběry), Tento soubor, Otevřené karty nebo Otevřenou složku.
2. Náhled ukáže, kolik souborů se vloží, velikost jako Markdown a jako XML a co bylo vynecháno s důvodem. Zvolte Kopírovat jako Markdown nebo Kopírovat jako XML.
- U každého souboru je cesta, řádky, jazyk, kódování a revize. Soubor s neuloženými změnami je označen a text je ten, který je teď v editoru.
- Vynecháno a pojmenováno: soubory .env (jejich hodnoty se nikdy nekopírují, ani když jsou v editoru zobrazené), soubory a složky vyřazené soubory ignorování, skryté, binární soubory, soubory nad limitem velikosti při procházení složky a soubory, které nejdou přečíst.
- Velký soubor ve stránkovaném režimu se nikdy nekopíruje celý: jen jeho výběr. Bez výběru je uveden jako vynechaný.
- Soubor nebo výběr, který se nevejde do zbytku limitu velikosti celého kontextu, se vynechá a je uveden i s limitem; menší soubor za ním se ještě může vejít.

## [edit.fragment] Úprava řetězce jako JSON, SQL nebo HTML
commands: nks.edit.fragmentJson, nks.edit.fragmentSql, nks.edit.fragmentHtml, nks.edit.fragmentWriteBack
Řetězec v souboru PHP nebo JavaScript se otevře v samostatné kartě ve zvoleném jazyce a vrací se zpět do řetězce s escapováním podle jeho druhu.
1. Dejte kurzor do řetězce a zvolte Úpravy, Upravit řetězec jako, Upravit řetězec jako JSON, Upravit řetězec jako SQL nebo Upravit řetězec jako HTML. Nová karta ukáže text, který řetězec představuje, bez uvozovek a escape znaků.
2. Text upravte. Zvolte Úpravy, Upravit řetězec jako, Zapsat fragment zpět do řetězce: text se escapuje pro řetězec a nahradí jeho obsah. Karta zůstane otevřená, takže můžete zapsat znovu.
- Druhy: řetězce PHP v jednoduchých a dvojitých uvozovkách, heredoc a nowdoc; řetězce JavaScript v jednoduchých a dvojitých uvozovkách a šablony v zpětných apostrofech. V řetězci ve dvojitých uvozovkách a v heredoc dostane každý znak dolaru zpětné lomítko, takže se omylem nevytvoří proměnná.
- S hlášením se odmítne: řetězec s proměnnými nebo výrazy (`$name`, `{$x}`), šablona JavaScript s `${...}`, escape, který není text (bajt nad 127, osmičkový zápis) a při zápisu řádek, který by ukončil heredoc nebo nowdoc.
- Karta sleduje řetězec při úpravách souboru jinde. Pokud se soubor uvnitř řetězce změnil po otevření karty, nic se nepřepíše bez ptaní: dialog nabídne Přepsat nebo Zrušit.
- Když se soubor zavře, karta fragmentu zůstane otevřená jako odpojený text. Název to říká a Zapsat fragment zpět do řetězce nic nezapíše.

## [tools.emmet] Zkratky Emmet
commands: nks.tools.emmetPreview, nks.tools.emmetWrap
1. V souboru HTML nebo CSS napište zkratku a stiskněte Tab. Z `ul>li*3` vznikne seznam o třech položkách s kurzorem v první z nich; každý další Tab skočí na další položku.
2. Tab odsazuje jako dřív, když před kurzorem žádná zkratka není, když je něco označeno, když je kurzor uvnitř otevřené značky nebo když zkratka není platná.
3. Nástroje, Struktura a jazyk, Náhled zkratky Emmet ukáže, čím by zkratka před kurzorem byla (`|` označuje místa, kam Tab skáče), a po potvrzení ji rozbalí.
4. Nástroje, Struktura a jazyk, Obalit zkratkou Emmet (jen HTML) se zeptá na zkratku a obalí jí každý výběr. Text přijde do nejvnitřnějšího posledního prvku; holá `*` na tom prvku (`ul>li*`) udělá jeden prvek na řádek.
5. Každé rozbalení jde do všech kurzorů najednou a je jeden krok Zpět.
- HTML: `name#id.class[attr=value]{text}*3`; `>` potomek, `+` sourozenec, `^` o úroveň výš, `( )` skupina; v názvech, třídách a textu je `$` číslo opakování (`$$` doplní nuly, `$@-` počítá dolů, `$@3` začíná od 3). Samotné slovo se rozbalí, jen když je to název značky HTML nebo krátký název z pluginu (`bq`, `btn`, `a:link`, `input:checkbox`, `!` pro kostru stránky). Uvnitř prvku se chybějící název doplní na `li`, `tr`, `td`, `option`, `span` nebo `div`, podle rodiče.
- CSS: `m10-20` je `margin: 10px 20px;`, `w100p` je `width: 100%;`, `c#f00`, `pos:a`, `dn`, koncový `!` přidá `!important`, `+` spojí víc deklarací. Celé číslo dostane `px`, zlomek `em`; písmena `p`, `e`, `x`, `r` za číslem znamenají `%`, `em`, `ex`, `rem`. Funguje jen uvnitř bloku `{ }`.
- Krátké názvy jsou vlastní tabulka pluginu s běžnými vlastnostmi a značkami, ne úplná sada Emmet.
- Rozbalení je snippet: Tab prochází jeho prázdná místa postupně, nejdřív prázdné prvky, potom prázdné hodnoty atributů, třeba `href` u `a` (v kostře stránky `!` titulek po těle). Při více kurzorech má každé rozbalení svá místa a kurzory se posouvají společně. Po posledním místě kurzor skočí na konec rozbaleného textu. Dokud Tab prochází místa, nikdy nerozbaluje: název značky napsaný do místa zůstane tak, jak je. Shift+Tab jde zpět, Esc chození ukončí.
- Nastavení Emmet klávesou Tab vypne rozbalování Tabem pro všechny jazyky nebo jen pro jeden; oba příkazy fungují dál.
- Proces Emmet se spustí prvním Tabem v souboru HTML nebo CSS.

## [tools.git] Git: stav, porovnání a příprava úseku
commands: nks.git.status, nks.git.diffIndex, nks.git.diffHead, nks.git.stageHunk, nks.git.unstageHunk, nks.git.conflictOurs, nks.git.conflictTheirs, nks.git.conflictBoth, nks.git.markResolved, nks.git.conflictDelete
1. Otevřete soubor z repozitáře Git a v nabídce Nástroje zvolte Git: stav souborů. Panel ukáže, co `git status` hlásí pro celý repozitář: co je v indexu a co je změněné ve stromu. Panel i stavový řádek se po změně zvenku obnoví samy krátce po poslední změně.
2. Git: porovnat s indexem a Git: porovnat s HEAD otevřou kartu porovnání otevřeného textu s verzí v indexu nebo v HEAD. Úseky jsou stejné jako v `git diff`.
3. Git: připravit úsek pod kurzorem přidá do indexu jen změnu, ve které stojí kurzor. Ostatní změny souboru zůstanou jen ve stromu. Soubor na disku se nemění.
4. Když se index mezitím změnil jiným programem, nic se nezapíše. Plugin index načte znovu a oznámí konflikt; zkontrolujte změny a příkaz zopakujte.
- Plugin zapisuje do repozitáře jen když připravíte nebo vrátíte úsek, nebo vyřešíte konflikt. Stav a porovnání jen čtou a nemění ani soubor index.
- Připravuje se text otevřený v editoru, i když není uložený. Soubor s nevyřešeným konfliktem slučování se nepřipravuje.
5. Git: vrátit úsek pod kurzorem z indexu vezme zpět do HEAD jen přípravenou změnu, ve které kurzor stojí (nebo na ni v souboru ukazuje). Ostatní připravené změny zůstanou.
6. Při konfliktu slučování umístěte kurzor do bloku mezi `<<<<<<<` a `>>>>>>>` a zvolte Git: konflikt, vzít naše změny / jejich změny / obě strany. Blok se nahradí zvolenou stranou. Až žádný blok nezbyde a soubor je uložený, zvolte Git: označit soubor jako vyřešený; soubor se přidá do indexu.
7. Panel stavu otevře soubor dvojklikem nebo Enterem. Ve stavovém řádku je větev a počty: `+` v indexu, `~` změněno, `?` nesledováno, `!` konflikty; aktualizuje se po každém příkazu Git.
8. V souboru z repozitáře Git značí levý okraj řádky, které se liší od indexu, plným proužkem (modrý změněné, zelený přidané, krátký červený proužek tam, kde řádky ubyly) a řádky už připravené obrysem. Kliknutí na značku změnu připraví, kliknutí na obrysovou značku ji vrátí z indexu. Nastavení Značky změn Gitu v levém okraji značky vypne. Soubor nad limitem velikosti a soubor se skrytými hodnotami (.env) značky nemá.
9. Karty Git: porovnat s indexem a Git: porovnat s HEAD mají tlačítka Připravit úsek a Vrátit úsek z indexu pro rozdíl, na kterém karta stojí (z klávesnice stejně fungují příkazy Porovnání: první a druhá akce zásuvného modulu v nabídce Nástroje). Po tlačítku se porovnání otevře znovu na místě karty.
10. Konflikt, ve kterém jedna strana soubor smazala (panel píše „konflikt: smazáno námi“ nebo „smazáno jimi“) nebo ho obě přidaly a nejde sloučit, nemá žádné značky. Git: konflikt, vzít naše změny / jejich změny / obě strany to pak řekne. Soubor ponecháte příkazem Git: označit soubor jako vyřešený, smažete ho příkazem Git: konflikt, smazat soubor. Soubor smazaný oběma stranami panel ukáže, ale nemá se v čem otevřít; vyřešte ho příkazem `git rm`.
11. Bloky konfliktu se najdou i v souborech uložených jako UTF-16 a UTF-32.
12. S `core.autocrlf` (nebo atributem `eol`) se úsek souboru s konci řádků CRLF uloží s LF, jak ho ukládá `git add`.
- Chybí zatím: commit a větve.
- Proces Git se spustí prvním příkazem, nebo když se otevře soubor a značky v levém okraji jsou zapnuté. Program `git` musí být v PATH.

## [tools.lsp] Jazykový server: definice, odkazy, přejmenování, doplňování a diagnostika
commands: nks.lsp.start, nks.lsp.goToDefinition, nks.lsp.findReferences, nks.lsp.rename, nks.lsp.restart, nks.lsp.stop
1. Otevřete textový dokument a zvolte Nástroje, Jazykový server, Jazykový server: spustit pro tento dokument. Spustí se server pro jazyk dokumentu a dostane jeho text. Od té chvíle mu jde každá úprava a to, co server označí za chybu, se podtrhne.
2. Jazykový server: přejít na definici skočí na definici slova pod kurzorem, i do jiného souboru. Jazykový server: najít odkazy vypíše všechna místa v panelu vlevo; Enter nebo dvojklik na položku ji otevře.
3. Jazykový server: přejmenovat symbol se zeptá na nový název slova pod kurzorem. Změny serveru ve všech souborech, včetně vytvoření, přejmenování a smazání souborů, které žádá, se ukážou společně v náhledu změn; co přijmete, se použije jako jeden krok. Nový soubor se ukáže prvními řádky jako přidaný text. Karta otevřeného souboru se při přejmenování přesune s ním; karta smazaného souboru se zavře, a má-li neuložené změny, editor se zeptá, zda ji zavřít, nebo nechat otevřenou. Soubor otevřený v jiné kartě musí být také spuštěný prvním příkazem, jinak se přejmenování odmítne celé.
4. Ctrl+J (Úpravy, Vložit, Doplnit slovo nebo klip) nabídne i to, co běžící server nabízí u kurzoru. Výběr takové položky provede ve stejném kroku Zpět i další změny, které server žádá, třeba přidání importu. Odpověď, která přijde po změně textu, se nezobrazí.
5. Když server skončí sám, upozornění to oznámí a podtržení, která nakreslil, zmizí. Jazykový server: restartovat (nebo jiný příkaz této skupiny) ho spustí znovu a předá mu všechny otevřené dokumenty tak, jak jsou teď. Jazykový server: zastavit ho vypne. Restart i zastavení platí pro server projektu, do kterého patří aktuální dokument; servery jiných projektů běží dál.
- Editor žádný server sám nespouští: dokud nezvolíte první příkaz této skupiny, nic neběží a server o souboru nic neví.
- Velké soubory zobrazené po stránkách a soubory nad limitem velikosti se serveru nikdy nepředají.
- Server se hledá podle názvu v PATH. Když se nenajde, upozornění pojmenuje program, který se hledal.
- Stavový řádek ukazuje LSP: běží, LSP: nenalezen nebo LSP: vypnut pro server posledního příkazu; nápověda u položky jmenuje jeho jazyk a složku projektu.
- Server běží pro jednu složku projektu. Je to nejbližší složka nad souborem, ve které je soubor se značkou jazyka (C#: nejdřív řešení `.sln` nebo `.slnx`, potom `.csproj`; Rust: `Cargo.toml`; PHP: `composer.json`; Dart: `pubspec.yaml`; JavaScript a TypeScript: `tsconfig.json` nebo `package.json`; C a C++: `compile_commands.json` nebo `CMakeLists.txt`), jinak nejbližší složka s `.git` nebo `.textida`, jinak složka souboru. Značka nad repozitářem se nepočítá. Nahoru se prohledává jen omezený počet složek. Dokumenty dvou projektů mají dva procesy serveru, dokumenty jednoho projektu sdílejí jeden.
- Zatím chybí: ruční volba složky projektu a přejmenování v souborech, které nejsou v UTF-8.

## [tools.lspPlugin] Jazykové servery z pluginu
1. Plugin uvede své servery v `languageServers` v `nkspad-plugin.json` a požádá o oprávnění `language.server`. Každý server má `id`, `languages` (id jazykových režimů, alespoň jedno), volitelné `globs` (třeba `*.foo` nebo `src/**/*.fx`), `command`, `args`, volitelné `initializationOptions` (objekt, který server dostane v `initialize`) a `locate`.
2. `locate` je `path` (výchozí): `command` je holé jméno programu hledané v PATH. U `plugin` je `command` cesta uvnitř složky pluginu.
3. Při instalaci pluginu editor oprávnění ukáže a čeká na vaši odpověď. Bez něj se server nikdy nespustí.
4. Server používají příkazy skupiny Jazykový server pro každý dokument, jehož jazyk je uvedený nebo jehož jméno souboru odpovídá masce. Server z pluginu má přednost před serverem dodaným s editorem.
- Pro jeden jazyk, složku projektu a nastavení serveru (program, argumenty, prostředí a `initializationOptions`) běží jeden proces serveru, společný všem otevřeným dokumentům projektu. Dokument, který odpovídá jen maskou, obsluhuje server jako první jazyk ze seznamu.
- Když program chybí nebo se nespustí, jedno oznámení ho jmenuje a úpravy pokračují. Když skončí sám, spusťte libovolný příkaz skupiny a spustí se znovu.
- Server bez otevřeného dokumentu svého projektu se po čase zastaví a další příkaz ho spustí znovu. Servery jiných projektů běží dál.
- Když se server zeptá na nastavení (`workspace/configuration`), dostane tu část `initializationOptions`, kterou pojmenuje požadovaná sekce: `a.b` je objekt `b` uvnitř `a`, dotaz bez sekce dostane celý objekt. Na vše ostatní se odpoví `null`.
- Manifest s chybným záznamem se odmítne celý, i s důvodem.

## [edit.complete] Doplňování slov a klipů
commands: nks.edit.complete
1. Stiskněte Ctrl+J (Úpravy, Vložit, Doplnit slovo nebo klip). Seznam nabídne slova, která pokračují slovo před kurzorem, a pak klipy, které se hodí pro jazyk dokumentu.
2. Vyberte položku. Slovo nahradí slovo před každým kurzorem; klip se vloží jako v tématu Klipy a nahradí toto slovo. Je to jeden krok Zpět.
- Slova se berou ze všech otevřených textových karet. Slovo, které právě píšete, se nenabízí, a slovo se nabízí, jen když je delší než napsaný začátek. Nejdřív jdou slova z upravovaného dokumentu, z nich ta nejblíž kurzoru (v omezeném počtu znaků na každou stranu), pak ta častější.
- Klipy jsou z knihovny z tématu Klipy. Klip bez jazyka se hodí do každého dokumentu; klip s jazykem jen do dokumentů toho jazyka.
- Hodnoty tajného souboru (`.env`) se nenabízejí, dokud jsou skryté. Nabízejí se jen v tom samém souboru a jen když jsou zobrazené.
- Seznam drží omezený počet slov; zúžíte ho psaním do pole. Položky běžícího jazykového serveru přibudou, viz téma Jazykový server.

## [edit.sql] SQL: dialekt a formátování
commands: nks.edit.formatSql, nks.edit.previewFormatSql, nks.tools.sqlDialect
1. Úpravy, Formátování, Formátovat SQL uspořádá výběry, nebo celý soubor, když nic není vybráno: klauzule na samostatném řádku, každá položka výběru na řádku, poddotazy odsazené, klíčová slova jedním typem písma. Je to jeden krok zpět a nic mimo výběr se nezmění.
2. Úpravy, Formátování, Náhled formátování SQL ukáže stejný výsledek nejdřív v náhledu změn.
3. Dialekt určuje barvy a to, která slova jsou klíčová. Platí první nastavený z těchto: Nástroje, Struktura a jazyk, Dialekt SQL (jen tento soubor); `sql_dialect = mysql` v souboru `.editorconfig` (každý soubor SQL v té složce a pod ní); nastavení Výchozí dialekt SQL. Bez nich je dialekt Generic a nic se nehlásí jako chyba.
- Dialekty: generic, mysql, postgres, sqlite, tsql. Editor se k žádné databázi nepřipojuje.
- Řetězce, uvozené názvy a komentáře se opíšou beze změny. Další formátování výsledku nic nezmění.
- Výběr větší než limit pluginu se odmítne se zprávou.
- Odsazení (4 mezery) a velikost klíčových slov (velká) jsou zatím pevné; nastavení to ještě není.

## [edit.html] HTML: entity a HTML Tidy
commands: nks.edit.htmlEncode, nks.edit.htmlEncodeAll, nks.edit.htmlDecode, nks.edit.htmlTidy, nks.edit.htmlCloseTag, nks.edit.htmlMatchTag, nks.edit.htmlSelectElement, nks.edit.htmlRemoveTag, nks.edit.htmlWrapTag, nks.edit.htmlStripTags, nks.edit.htmlInsertImage, nks.edit.htmlInsertLink
1. Úpravy, Formátování, Převést na entity HTML nahradí v označených částech (v celém souboru, když nic není vybráno) znaky & < > " ' entitami. Převést z entit HTML přečte pojmenované, desítkové i šestnáctkové entity zpět. Zakódovaný text převedený zpět dá původní text. Každé je jeden krok zpět.
2. Úpravy, Formátování, Formátovat pomocí HTML Tidy pošle HTML programu HTML Tidy a výsledek ukáže v náhledu změn. Zprávy Tidy jdou do okna výstupu.
3. Tidy je samostatný program. Editor ho hledá na cestě z nastavení Program HTML Tidy, ve složce `tidy` vedle pluginu formátovačů, ve složce `tools/tidy` aplikace a ve složkách z PATH. Když ho nenajde, zpráva vypíše všechna místa.
- Tidy není součástí tohoto sestavení: nainstalujte ho a nastavte cestu, nebo ho dejte na některé z těch míst.
- Čísla řádků ve zprávách se počítají od začátku textu, který se Tidy poslal.
- Převést na entity HTML včetně znaků mimo ASCII zapíše i každý znak mimo ASCII jako číselnou entitu (`&#233;`); převod zpět dá text zase.
4. Nástroje pro tagy (soubory HTML, XML a PHP): Úpravy, Formátování, Zavřít otevřený tag zapíše koncový tag nejbližšího tagu, který je u kurzoru ještě otevřený. Přejít na protější tag přesune kurzor k protějšku tagu u kurzoru. Vybrat element vybere element kolem výběru; opakováním se vybere další nadřazený. Odstranit tag smaže počáteční a koncový tag elementu u kurzoru a obsah nechá. Obalit tagem se zeptá na název a obalí jím každý výběr. Odstranit všechny tagy smaže z označených částí všechny tagy a komentáře (z celého souboru, když nic není vybráno).
- Tagy se párují podle vnoření. Komentáře a obsah `script` a `style` tagy neobsahují. Nepovinné koncové tagy HTML (`li`, `p`) se nedomýšlejí, takový tag se tedy počítá jako otevřený.
5. Úpravy, Formátování, Vložit obrázek se zeptá na soubor s obrázkem a na místo každého výběru zapíše `<img src="…" width="…" height="…" alt="">`. Adresa je vzhledem k dokumentu (soubor na jiném disku nebo neuložený dokument dostane adresu `file:///`). Šířka a výška se čtou z hlavičky souborů PNG, GIF, JPEG, BMP a WebP; u jiného souboru, třeba SVG, tag rozměr nemá. Vybraný text na jednom řádku se stane alternativním textem; když nic není vybráno, kurzor stojí v prázdném alt.
6. Vložit odkaz se zeptá na adresu a kolem každého výběru zapíše `<a href="…">`. Když nic není vybráno, odkaz ukáže adresu, nebo u souboru s obrázkem obrázek s šířkou a výškou. Adresa, která je cestou k existujícímu souboru, se zapíše vzhledem k dokumentu.

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
2. Zobrazení, Oddělovač a záhlaví tabulky vybere čárku, středník, tabulátor nebo svislítko jako oddělovač, zda uvozovky ohraničují hodnoty a zda je první řádek záhlaví. Dokud nevyberete, oddělovač se pozná z prvních řádků souboru (když jsou nejasné, rozhodne přípona).
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

## [table.copyAs] Kopírování buněk jako Markdown, SQL, JSON nebo HTML
commands: nks.table.copyAs
Označte buňky v tabulkovém pohledu a zvolte Zobrazení, Kopírovat tabulku jako a pak podobu. Do schránky se uloží označené sloupce a řádky v té podobě.
- Tabulka Markdown: tabulka s oddělovači svislítek, se záhlavím (nebo s písmeny sloupců A, B, ... bez záhlaví). Zvláštní znaky, například svislítko, zpětné lomítko, hvězdičky, podtržítka a špičaté závorky, dostanou zpětné lomítko a konec řádku v buňce se změní na `<br>`.
- SQL INSERT: jeden příkaz na řádek, hodnoty jako text v jednoduchých uvozovkách (uvozovka se zdvojí). Názvy sloupců jsou ze záhlaví. Program se zeptá na název tabulky, nabídne název souboru.
- Uvozování určuje dialekt SQL: `sql_dialect` v `.editorconfig` složky, jinak nastavení Výchozí dialekt SQL (téma SQL: dialekt a formátování). MySQL dá názvy do zpětných apostrofů a zpětné lomítko v hodnotě zdvojí; T-SQL dá názvy do hranatých závorek a hodnoty píše jako N'...'; ostatní dialekty dají názvy do dvojitých uvozovek a zpětné lomítko nechají, jak je.
- JSON: pole objektů pojmenovaných podle záhlaví, nebo pole polí, když záhlaví chybí, má prázdný název nebo se název opakuje. Všechny hodnoty jsou text.
- Tabulka HTML: prvek table s entitami pro `& < > " '`; schránka drží HTML jako zdrojový text i jako HTML pro vložení do dokumentu.
- Skrytá hodnota souboru .env se zkopíruje jako hvězdičky, ne jako její text. Kopírování a vyjmutí klávesami Ctrl+C a Ctrl+X dá na její místo také zástupný znak a vyjmutí, které se jí dotkne, nic nesmaže.
- Změní-li se soubor během příkazu, nic se nezkopíruje a oznámení vyzve k novému spuštění.
- Vlastní podoba je převodník datového pluginu s "table": true (téma Převod textu). Objeví se pod vestavěnými podobami. Dostane označené buňky tak, jak je zapíše Kopírovat: oddělené tabulátorem, řádek tabulky na řádek textu, řádek záhlaví, když je označený, a hodnota s tabulátorem, uvozovkou nebo koncem řádku ve dvojitých uvozovkách. Výsledek jde do schránky; když převod selže, schránka se nezmění.

## [table.region] Úprava vybraného textu jako tabulky
commands: nks.table.editRegion
Vyberte část textu, například blok řádků oddělených čárkami v poznámce nebo v logu, a zvolte Zobrazení, Upravit výběr jako tabulku.
1. Dialog se zeptá na oddělovač (čárka, středník, tabulátor nebo svislítko), zda uvozovky ohraničují hodnoty a zda je první vybraný řádek záhlaví. Náhled ukáže první řádky jako buňky.
2. Vybraný text se stane tabulkou. Zbytek souboru zůstane textem: úprava buňky změní jen bajty té buňky a nic mimo výběr se nečte ani nemění.
- Funguje v něm vše z tabulky: pohyb, úpravy, přidávání a mazání řádků a sloupců, filtr, kopírování a vkládání. Stavový řádek říká, že tabulkou je jen vybraný text.
- Řádky přidané na konec patří do výběru. Přepnout tabulku a text vrátí text; další Přepnout tabulku a text ukáže jako tabulku celý soubor.

## [compare.text] Porovnání textů
commands: nks.compare.clipboard, nks.compare.file, nks.compare.texts, nks.compare.ignoreWhitespace, nks.compare.ignoreCase, nks.compare.action1, nks.compare.action2, nks.compare.action3, nks.compare.action4
Dva texty se porovnají po řádcích v samostatné kartě. Stejné řádky stojí proti sobě, změněný blok je na obou stranách podbarvený a označený ve střední liště a lišta nahoře říká, kolik je rozdílů.
1. Porovnat se schránkou (menu Nástroje nebo místní nabídka editoru) porovná kartu s textem ve schránce. Je-li v editoru vybraný text, porovná se jen výběr.
2. Porovnat se souborem se zeptá na soubor a porovná s ním kartu. Porovnat s diskem je v menu Soubor a porovná kartu s jejím souborem.
3. Porovnat dva texty otevře dvě prázdné strany. Tlačítka Vložit levý text a Vložit pravý text vloží do strany obsah schránky.
- Z příkazového řádku otevře textida --diff levý pravý porovnání dvou souborů: levý soubor se otevře v kartě a může přijímat bloky z pravého, který je jen pro čtení.
- Zásuvný modul může otevřít porovnání libovolných dvou zdrojů (revize dokumentu, soubor, virtuální dokument nebo text, který sám dodá) a přidat do něj tlačítka (Git přidává Připravit úsek a Vrátit úsek z indexu). Tlačítko působí na rozdíl, na kterém karta stojí. Z klávesnice stiskněte číslici 1 až 9, když má porovnání fokus (tip tlačítka říká, jakou klávesu má), nebo použijte příkazy Porovnání: první až čtvrtá akce zásuvného modulu v nabídce Nástroje. Tlačítka fungují, dokud se dokumenty a soubory porovnání od jeho vzniku nezměnily; jinak porovnání otevřete znovu. Když zásuvný modul porovnání obnoví, nová karta převezme místo staré a zůstane na stejném rozdílu. Strana větší, než editor zásuvným modulům dovolí, se odmítne.
- Strana s kartou je samotný dokument: když se změní, porovná se znovu po krátké pauze. Schránka, výběr a soubor na disku jsou strany jen pro čtení.
- Jiný konec řádku (CR, LF, CRLF) řádek nezmění. Mezery a velikost písmen ano, dokud v menu Nástroje a Porovnání nezapnete Nerozlišovat mezery nebo Nerozlišovat velikost písmen. Pak jsou stejné řádky, které se liší jen mezerami a tabulátory (kdekoli v řádku), nebo jen velikostí písmen. Volba platí pro všechna porovnání, otevřená se hned porovnají znovu a lišta nahoře ji připomene. Stejné řádky se pak nepodbarví, i když se v mezerách liší, a Kopírovat doleva i doprava přenáší řádky tak, jak jsou.
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

## [tools.mcp] Editor jako server MCP
commands: nks.tools.mcpServer, nks.tools.mcpShareTab, nks.tools.mcpShareSelection, nks.tools.mcpTools
Asistent s umělou inteligencí, kterého na tomto počítači spustíte (například programovací agent), může Textidu používat přes Model Context Protocol. Vidí jen karty, které mu sdílíte, a nikdy nezapisuje: každý návrh uvidíte jako náhled změn a rozhodnete vy.
1. Zvolte Nástroje, Server MCP. Dialog vypíše, co asistent dostane. Zapnutím se server spustí; dalším zvolením příkazu se vypne a všechny karty se odvolají.
2. Asistenta nastavte tak, aby spouštěl program textida-mcp.exe ze složky mcp vedle editoru (celou cestu ukáže stavový řádek). S editorem mluví přes místní rouru, žádný síťový port se neotevírá.
3. Otevřete kartu a zvolte Nástroje, MCP, Sdílet kartu s MCP. Stejným příkazem kartu odvoláte. Karta, která není sdílená, není ani vypsaná.
4. Chcete-li asistentovi dát jen část karty, vyberte text a zvolte Nástroje, MCP, Sdílet výběr s MCP. Vybraný text se otevře jako kopie jen pro čtení ve vlastní kartě: nesleduje originál a asistent k ní nemůže navrhovat změny. Zavřením té karty ji odvoláte.
5. Volbou Nástroje, MCP, Nástroje MCP zapnete nebo vypnete jednotlivé nástroje. Výběr nástroje v seznamu změní jeho stav, Escape seznam zavře.
- Asistent čte text tak, jak je v editoru, i neuložený, hledá v něm, zjišťuje, kde máte kurzor a výběr, čte chyby a varování pluginů a kontroly kódu, žádá osnovu, kódování a konce řádků, hledá řádky dané úrovně v logu libovolné velikosti a čte nebo hledá bajty (nejvýš 64 KiB najednou).
- Změna je návrh pro jednu revizi textu. Když se text mezitím změnil, návrh se odmítne a asistent si musí text přečíst znovu. Návrh uvidíte jako jiné změny celého dokumentu a zvolíte, jestli ho použít; asistent se může zeptat, jak dopadl.
- Hodnoty v souborech .env vidí asistent jako *. Při sdílení takové karty se zeptáme, jestli smí vidět skutečné hodnoty; odpověď platí jen pro tu kartu.
- Odvolání karty nebo vypnutí serveru zastaví požadavky, které právě běží, a návrh, který ještě čeká na vaše rozhodnutí, se stáhne a počítá se jako zamítnutý. Odvolá ji i znovunačtení souboru.
- Na vaše rozhodnutí může čekat jen omezený počet návrhů najednou (limit je zatím provizorní); další se odmítne, dokud nerozhodnete. Výsledek větší než limit velikosti se odmítne a asistent se dozví, že má chtít méně.
- Program pro asistenta mluví aktuální verzí MCP (2026-07-28) a pro asistenty, kteří ještě začínají příkazem initialize, verzí 2025-11-25.

## [tools.ide] Most /ide pro Claude Code
commands: nks.tools.ideBridge, nks.tools.ideDisconnect
Claude Code běžící v terminálu na tomto počítači se příkazem /ide připojí k oknu Textidy. Agent pak ví, kde máte výběr a který soubor je otevřený, a změny, které chce ve zpřístupněné kartě udělat, se objeví v kontrole změn. Vidí jen karty, které zpřístupníte, a Textida použije jen to, co potvrdíte.
1. Nejdřív zapněte server MCP (Nástroje, Server MCP) a zpřístupněte kartu (Nástroje, Sdílet tuto kartu s MCP). Most používá stejné zpřístupněné karty.
2. Zvolte Nástroje, Most /ide. Dialog řekne, co agent dostane. Teprve po potvrzení Textida otevře port, jen na tomto počítači (127.0.0.1), a zapíše soubor zámku s tokenem a složkami okna do ~/.claude/ide (nebo do složky ide v CLAUDE_CONFIG_DIR). Opětovná volba příkazu port zavře a soubor smaže.
3. V terminálu ve složce tohoto okna spusťte v Claude Code příkaz /ide. Program bez tokenu ze souboru zámku se odmítne.
4. Stavový řádek ukazuje připojeného agenta. Kliknutím, nebo příkazem Nástroje, Most /ide: odpojit agenta ho odpojíte. Port zůstane otevřený, takže se agent může příkazem /ide připojit znovu; chcete-li tomu zabránit, most vypněte.
- Každé okno má svůj most s vlastním portem a tokenem, stejně jako každá instance Textidy. Agent připojený k oknu vidí jen karty zpřístupněné v tomto okně. Soubor zámku, který po sobě nechala spadlá instance, se smaže při spuštění dalšího mostu.
- Agent se dozví výběr a cestu zpřístupněné karty při každé změně výběru i při přepnutí na kartu. Karta, která není zpřístupněná nebo ještě nemá soubor, se nehlásí. Hodnoty z .env jsou skryté jako u každého jiného čtení. Odvolání karty to okamžitě zastaví.
- Změna, kterou Claude Code navrhne pro zpřístupněnou kartu, se otevře v kontrole změn (karta Výsledky hledání). Použití celé změny odpoví FILE_SAVED, odmítnutí DIFF_REJECTED; použití jen některých úseků odpoví chybou, aby si agent soubor přečetl znovu. Textida použije v kartě to, co potvrdíte, a soubor neuloží. Soubor, který není v okně otevřený nebo zpřístupněný, a soubor .env se odmítnou.
- getDiagnostics vrací chyby a varování zpřístupněných karet.
- Neověřeno: Claude Code tento protokol nespecifikuje. Most se řídí oficiální stránkou o vestavěném serveru IDE a popisem komunity (coder/claudecode.nvim) a žádná verze Claude Code zatím nebyla potvrzena jako funkční. Neprovedeno: otevírání souborů z agenta, seznam otevřených karet, ukládání přes most, spouštění buněk notebooků a @-zmínky.

## [tools.agent] Agent přes ACP
commands: nks.agent.start, nks.agent.panel, nks.agent.shareTab, nks.agent.cancel, nks.agent.stop
1. Nástroje, Agent, Spustit agenta... spustí programovacího agenta, který mluví Agent Client Protocolem (ACP): Gemini CLI, Codex, Claude, Junie, GitHub Copilot nebo OpenCode. Dialog ukáže přesný příkaz a složku, ve které poběží; bez potvrzení se nespustí nic. Agent musí být nainstalován a nalezen v PATH. Dávkový soubor (.cmd, .bat), obvyklá podoba agenta nainstalovaného přes npm na Windows, se spouští přes cmd.exe a každý argument se pro něj uzavře do uvozovek. Seznam u každého agenta říká, zda je jeho program nalezen v PATH; u chybějícího zpráva řekne, odkud jej nainstalovat, a nic se nespustí. Dialog ukáže úplnou cestu programu, který poběží. Po potvrzení Textida program spustí s --version a verzi zapíše do panelu vedle verze z tabulky; program, který žádnou nevypíše, se jen poznamená.
2. Panel agenta pod editorem ukazuje konverzaci. Napište zprávu a stiskněte Enter. Zrušit tah agenta (nebo tlačítko Zrušit tah) pošle agentovi zrušení a jeho otevřené dotazy na oprávnění zodpoví „zrušeno“; Zastavit agenta ukončí proces.
3. Agent čte jen karty, které sdílíte: Sdílet kartu s agentem. Dostane text tak, jak je v editoru, včetně neuložených změn; hodnoty souborů .env zůstanou skryté. Zastavením agenta se všechny sdílené karty vezmou zpět.
4. Když agent zapíše do sdíleného souboru, změna se ukáže po úsecích v kontrole změn (karta Výsledky hledání) a zapíše se, až ji použijete. Když ji odmítnete, agent dostane chybu. Soubor, který není sdílený nebo otevřený, se přes editor nezapisuje.
5. Když agent žádá o oprávnění, dialog ukáže volby, které agent nabízí. Zrušení nebo zavření dialogu odpoví „zrušeno“. U nástroje, který agent spouští sám (příkaz, smazání, přesun, stažení), dialog řekne, že to editor nemůže ani zkontrolovat, ani vrátit, a upozorní, že volbu označenou „vždy“ si pamatuje agent, ne editor.
- Kde záruka končí: editor řídí jen to, co jde přes něj. Vlastní nástroje agenta mohou měnit soubory, spouštět programy a používat síť bez editoru a editor žádnému agentovi nenastavuje režim oprávnění (každý si ponechá vlastní výchozí). Co smí agent dělat, nastavte v nastavení samotného agenta. Panel to ukazuje pod nadpisem: režim oprávnění, který editor nastavuje (žádný), co smějí vlastní nástroje agenta, a průběžné počty. „Přes editor“ je to, co editor sám obsloužil: čtení, návrhy, o kterých jste rozhodli, a ty, které jste použili. „Mimo editor“ je jen to, co agent o svých nástrojích ohlásil (příkazy, smazání, přesuny, stahování), skutečný počet tedy může být vyšší; takové řádky nástrojů jsou v konverzaci označené.
- Textida pro agenty neukládá žádné klíče ani hesla a tabulka agentů obsahuje jen názvy programů a přepínače. Přihlaste se programem samotného agenta; agent dostane prostředí, ve kterém Textida běží. Verze v tabulce je ta, kterou uvádí veřejný registr ACP, ne vyzkoušená.
- Každý agent je v seznamu jako „neověřeno“: žádný běh se skutečným programem zatím neprošel zkušebními scénáři. Tah, ve kterém agent příliš dlouho nic neřekne ani nezeptá, skončí jako vypršelý. Časové limity a velikost souboru předaného agentovi jsou zatím provizorní.
- Plugin může ve stejném panelu vést vlastní konverzaci (oprávnění agent.panel). Panel jde ovládat klávesnicí: otevře se s kurzorem v poli zprávy, Enter odešle, Tab prochází tlačítka Odeslat, Zrušit tah, Zastavit agenta a Zavřít, mezerník nebo Enter tlačítko stiskne a Esc panel zavře a vrátí do editoru. Zrušit tah agenta a Zastavit agenta jsou i příkazy v nabídce a v paletě. Čtečka obrazovky dostane jméno panelu, konverzace, pole zprávy, stavu a každého tlačítka a změna stavu (startuje, připraven, pracuje, skončil) se ohlásí. Ověřeno stromem přístupnosti programu, zatím ne skutečnou čtečkou obrazovky.

## [tools.mcpAgents] Servery MCP agentů
commands: nks.tools.mcpServers
Jeden seznam serverů MCP, se kterými jsou nastavení vaši programovací agenti. Textida čte Claude Code (.mcp.json, ~/.claude.json), Cursor (.cursor/mcp.json), Codex (~/.codex/config.toml) a OpenCode (opencode.json, opencode.jsonc). Soubory projektu hledá ve složce aktivního souboru a v nadřazených složkách. Soubory jen čte, nikdy do nich nezapisuje.
1. Zvolte Nástroje, MCP, Servery MCP agentů. Každý řádek ukazuje server, agenta a soubor, odkud pochází, a jeho příkaz nebo adresu se skrytými tajnými hodnotami.
2. Vyberte server a pak Vyzkoušet. Okno ukáže přesný příkaz, složku a proměnné s hodnotami skrytými (***). U serveru s adresou ukáže místo toho adresu a jména hlaviček. Nic se nespustí a nic se neodešle, dokud nestisknete Spustit (u adresy Připojit).
3. Potom Textida program spustí nebo se připojí na adresu, zeptá se na nástroje (MCP verze 2026-07-28 a 2025-11-25 u programu, 2025-11-25 u adresy) a spojení ukončí. Výsledek řekne, kolik nástrojů server nabízí, nebo ukáže chybu a poslední řádky, které program napsal na chybový výstup. Stejné řádky jsou ve výstupu Úlohy.
4. Volba Připojit slouží k použití serveru. Seznam ukáže jeho nástroje a zdroje dat. Zdroj dat se otevře v nové kartě jen pro čtení. U nástroje se zeptá na argumenty jako objekt JSON (při psaní se ukazuje schéma nástroje) a pak samostatná otázka ukáže server, nástroj, argumenty a to, zda server uvádí, že nástroj jen čte. Nic se neodešle, dokud nestisknete Zavolat; výchozí tlačítko je Zrušit. Odpověď se otevře v nové kartě jen pro čtení a výstup Úlohy zaznamená, že nástroj byl zavolán a kolik znaků měly argumenty, ne argumenty samotné.
5. Volba Převést záznam pro jiného agenta otevře záznam ve formátu toho agenta v nové kartě. Úryvek se nejdřív zkontroluje podle toho formátu. Hodnoty proměnných a hlaviček jsou nahrazené ***, doplňte je, než úryvek vložíte do souboru toho agenta.
- ${NAME} v příkazu, argumentu, proměnné, adrese nebo hlavičce se nahradí z prostředí Textidy, ${NAME:-text} dá výchozí hodnotu. Jméno, které tam není, se hledá ve Správci pověření Windows pod textida:mcp:env:NAME. Jméno, které není ani tam, se vyžádá (psaní je skryté) a uloží se do Správce pověření a nikam jinam; když úložiště pověření není dostupné, hodnota se použije jen pro toto spojení. Zrušení otázky nic nespustí.
- Server s adresou dostává klíče v hlavičkách, proto musí mít adresu https://, pokud není na tomto počítači. Textida mluví Streamable HTTP (MCP 2025-11-25); starší přenos s odděleným kanálem událostí (typ sse z MCP 2024-11-05) se nepodporuje. Platí síťový proxy programu. Klíč, který server odmítne (HTTP 401 nebo 403), se zapomene ve Správci pověření a jednou se zeptá znovu; klíč z prostředí nebo zapsaný přímo v konfiguračním souboru Textida nemění a zpráva to řekne. npx a npm se spouštějí jako node se skriptem zástupce Node.js, takže mezi editorem a serverem nestojí cmd.exe. Jiný dávkový soubor se spouští přes cmd.exe.
- Meze (zatím provizorní): odpověď nebo zdroj dat delší než 262144 znaků se ořízne a stavový řádek řekne, jak dlouhý byl. Seznam, volání nebo čtení, které trvá déle než 120 sekund, skončí zprávou a server se vyzve, aby požadavek zastavil. Program, který skončí nebo je zastaven, se ohlásí s kódem ukončení a posledními řádky chybového výstupu a seznam se zavře.
- Tajné hodnoty jsou skryté v seznamu, v okně, ve výstupu i v úryvcích, také když se objeví uvnitř argumentu nebo v chybovém výstupu programu. Hodnoty kratší než 4 znaky se v textu nehledají.

## [tools.shell] Nabídka Průzkumníka a asociace souborů
commands: nks.tools.shellMenuAdd, nks.tools.shellAssociate, nks.tools.shellRemove
Jen Windows. Textida se umí přidat do místní nabídky Průzkumníka vašeho uživatele a do seznamu Otevřít v programu. Nic nevyžaduje práva správce a zapisují se jen klíče Textidy pod HKEY_CURRENT_USER\Software\Classes. Klíče jiných programů, včetně PSPadu, se nečtou ani nemění. Oba příkazy fungují v nainstalované Textida.exe.
1. Zvolte Nástroje, Integrace do systému, Přidat Textidu do nabídky Průzkumníka. Soubory dostanou položku Otevřít v Textidě; spustí Textidu se souborem, nebo soubor předá už běžící Textidě. Ve Windows 11 je položka pod Zobrazit další možnosti.
2. Zvolte Nástroje, Integrace do systému, Asociace souborů a vyberte přípony. Volba přípony v seznamu přidá nebo odebere Textidu v jejím seznamu Otevřít v programu; Escape seznam zavře. Dokud příponu nevyberete, žádná asociace se nevytvoří.
3. Aby byla Textida výchozím programem přípony, použijte v Průzkumníku Otevřít v programu, Vybrat jinou aplikaci. Tuto volbu si Windows drží sám.
4. Zvolte Nástroje, Integrace do systému, Odebrat Textidu z Průzkumníka: smaže položku nabídky a všechny asociace, které Textida přidala. Totéž udělá odinstalátor.
- Text položky nabídky je v jazyce Textidy v okamžiku přidání. Po změně jazyka ji přidejte znovu.
- Přesunutí Textida.exe do jiné složky nechá v položkách starou cestu. Přidejte položku znovu, nebo ji nejdřív odeberte.

## [help.updates] Aktualizace a návrat na předchozí verzi
commands: nks.help.updateCheck, nks.help.updateReturn
Jen Windows, v nainstalované Textidě (složka vytvořená přes Setup.ps1). Textida sama nezjišťuje nic, dokud nezapnete nastavení Zjišťovat aktualizace při startu; ve výchozím stavu je vypnuté. Zjištění je jeden dotaz na malý podepsaný soubor; neposílá se nic dalšího, žádný identifikátor vás ani počítače.
1. Zvolte Nápověda, Zjistit aktualizace. Je-li nabídnuta novější verze, Textida se před čímkoli zeptá.
2. Nainstalovat a restartovat stáhne balíček, ověří jeho kontrolní součet podle podepsaného souboru a rozbalí ho vedle stávající verze. Teprve potom se Textida zavře, složky se přepnou a Textida se spustí znovu s vašimi kartami. Neuložený text drží žurnál; při vypnutém žurnálu dokumenty nejdřív uložte nebo zavřete.
3. Když cokoli nesedí (podpis, kontrolní součet, balíček, který neodpovídá), nic se nenainstaluje a stávající verze běží dál.
4. Nápověda, Vrátit se na předchozí verzi vás vrátí na verzi, která byla nainstalovaná před poslední aktualizací. Novější verze se pak stane tou, na kterou se lze vrátit.
- Nastavení Textidy a žurnál zůstávají, složka přenosného profilu se přenese.
- Tento build zjišťuje aktualizace jen tehdy, má-li adresu aktualizací a podpisový klíč; build bez nich to řekne.
### Hlášení
- Aktualizace fungují jen v nainstalované Textidě (Setup.ps1 vedle Textida.exe ve Windows). Aktualizace funguje jen ve Windows, v nainstalované Textidě. Jinde novou verzi nainstalujete sami (téma Co tato verze neumí).
- Tento build nemá nastavenou adresu aktualizací ani podpisový klíč, aktualizace proto nejde zjistit. Použijte sestavení, které má adresu aktualizací a podpisový klíč, nebo novou verzi nainstalujte sami.
- Aktualizace selhala: důvod. Nic se nenainstalovalo a běží dál současná verze. Důvod uvádí příčinu (nesedící podpis nebo kontrolní součet, balíček, který nesouhlasí, chyba sítě); zkuste to později.
- Nejdřív uložte nebo zavřete dokumenty s neuloženými změnami: žurnál je vypnutý, aktualizace by je mohla ztratit. Dokumenty uložte, nebo zapněte obnovu po pádu (téma Obnova po pádu), a pak aktualizaci zvolte znovu.
- Není žádná předchozí verze, na kterou by se šlo vrátit. Předchozí verze existuje jen po aktualizaci.

## [review.external] Kontrola změn zvenku
commands: nks.review.open, nks.review.next, nks.review.previous
Když jiný program (skript, asistent, nástroj sestavení) změní soubor karty bez neuložených úprav, panel nabídne Zkontrolovat změny.
1. Zvolte Zkontrolovat změny. Karta převezme nový obsah, dřívější obsah zůstane v místní historii a po dobu kontroly se nesmaže a karta Výsledky hledání vypíše změny po řádcích.
2. Zaškrtněte změny, které chcete ponechat. Nezaškrtnuté, nebo celý soubor s nezaškrtnutým polem, se po volbě Použít vrátí na dřívější obsah.
3. Použít zapíše vrácený text do karty jako jeden krok zpět a soubor uloží; bajty mimo vrácené změny zůstanou beze změny.
- Karta ukazuje štítek s počtem čekajících změn. Volbou Soubor a Zkontrolovat změny zvenku seznam otevřete znovu; vrátí se i po restartu.
- Alt+F7 a Alt+Shift+F7 skočí na další a předchozí změnu zvenku.
- Pokud během kontroly v kartě píšete, změny se ukážou znovu z nového textu.
- Soubory s tajnými hodnotami (.env) a soubory s neuloženými úpravami zůstávají u staršího panelu: Znovu načíst, Ponechat moje, Porovnat.
- S volbou Ponechat změny zvenku bez kontroly v nastavení karta převezme nový obsah hned. Ve výchozím stavu je vypnuta.

# Textida – úkoly

Stav 26. 10. 2026. Úkoly vycházejí z interního plánu, zadání a měření, které v repozitáři nejsou; tady je jen jejich výsledek: etapy, celky, úkoly, kritéria dokončení a odhady.

Jak číst položku:
- ID má tvar `etapa.celek.úkol` a nemění se. `PR` jsou průběžné úkoly, `PZ` úkoly po verzi 1.0. Nové úkoly dostávají další volné číslo v celku, proto pořadí v textu nemusí odpovídat číslům.
- **Funkce** jsou F-ID z matice. „Částečně“ znamená, že zbytek funkce řeší jiná položka. Funkce rozložená do více etap má „částečně“ všude kromě etap, které jí dává matice nebo plán
- **Odhad** je převzatý z matice (sloupec Odhad), z analýzy konkurence, revize, vždy s uvedeným zdrojem. Cena jedné funkce se uvádí jen u jedné položky; ostatní píšou „v rámci F…“. Číslo před „v rámci F…“ je podíl už započtené ceny a nesčítá se znovu. Funkce F001–F146 mají v podkladech jen součet za etapu, proto „bez odhadu“. Kde odhad chybí, položka to říká. Že se taková práce vejde do rozpočtu etapy, doložené není; cenu nebo podíl určí vstupní brána etapy (PR.8.1, PR.8.2).
- **Umístění** (od P3b) je jádro, vestavěný plugin nebo data podle plánu Všechny pluginy editoru jsou přibalené a zapnuté.
- Pokrytí, závislosti, cykly, odkazy na ID, dvojí započtení odhadů a shodu rozpočtů mezi dokumenty kontroluje `python -I tools/check_todo.py`. Nula nálezů znamená, že ID, odkazy a čísla sedí. Neznamená, že je funkce věcně úplná, že kritéria dávají správné pořadí dokončení, ani že jsou ceny úplné.

Společná pravidla pro „Hotovo, když“:
- Každý test má předem určený scénář a očekávaný výsledek. Záznam uvádí build nebo commit, OS a architekturu, backend dokumentu a výsledek.
- Distribuční brány platí pro tři RID: `win-x64`, `osx-arm64` a `osx-x64`.
- Headless test dokládá jen headless chování. Čtečka, IME, prezentace obrazu a Gatekeeper potřebují skutečný běh.
- „Změřeno“, „existuje záznam“ ani „majitel rozhodl“ samy nejsou splnění. Výsledek se porovná s očekáváním. Neprovedená kontrola má stav **neověřeno**, ne hotovo.
- Limity označené „návrh revize“ nejsou rozhodnutí. Schvalují se při zahájení položky; do té doby platí cíle z plánu
- **Vstupní rozhodnutí.** Kde kritérium mluví o „nastaveném“ limitu, stropu nebo rozpočtu, nebo o „uzavřeném“ seznamu, zapíše se hodnota nebo seznam do položky **před jejím zahájením** a schválí ho majitel. Bez zápisu se položka nezahajuje. Hodnota se nesmí určit až podle dosaženého výsledku.
- **Průběžné položky po vydáních.** Položka PR se neodškrtává jednou za celý projekt. Pro každé vydání (MVP, 1.0) má vlastní záznam s SHA buildu, seznamem OS × RID a platným rozsahem. Brána vydání čte jen záznam svého vydání; 1.0 nepřebírá výsledek MVP a MVP nečeká na práci pro 1.0.
- **Kompatibilita R10 a výkon R8 jsou dvě brány.** Výkonové cíle se měří na referenční Windows VM a referenční Mac (R8). Kompatibilita se dokládá na každé verzi a edici OS z R10 × RID × scénář × hash balíku (PR.1.3). Jedno nenahrazuje druhé.

## Průběžně

Úkoly bez vlastní etapy. Začínají v P1 a běží až do P10. Nemají vlastní odhad. Etapa uvedená u „Odhad“ je místo, kde se práce ocení ve vstupní bráně (PR.8.1, PR.8.2); že ji stávající rozpočet etapy už nese, doložené není.

### PR.1 CI a sestavení

Dnes v repozitáři žádné CI není. Bez něj nejde doložit sloupce Win a mac v matici ani start pod NativeAOT.

- [ ] **PR.1.1** CI pro Windows a macOS
  - **Co a proč:** Každý commit se sestaví a otestuje na Windows (win-x64) i macOS (osx-arm64). macOS je povinná platforma, testy jádra už na Macu prošly ručně. Testy knihoven pod JIT jsou oddělené od testů publikovaného AOT programu (PR.1.2).
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - build celého `NksHub.Textida.sln` a `dotnet test` na obou OS
    - testy UI headless; testy, které potřebují data PSPadu, z produktových sad vyřadit (kap. 7)
    - kontrola, že produktová sada a instalátor projdou bez dat reverzní analýzy, ne jen podle názvů cest
    - rychlé kontroly při každém commitu na novějších systémech; úplná matice R10 ze seznamu P1.1.6 před každým vydáním (PR.1.3)
    - u změny, která se dotýká platformy (UI, vstup, přístupnost, nativní závislosti, runtime, instalátor), cílený běh na nejstarších systémech z R10 (Windows 10 22H2, Windows 11 22H2, macOS 14) ještě před přijetím změny
  - **Hotovo, když:** push spustí build a testy na Windows i macOS a výsledek je vidět u commitu; platformní změna má před přijetím záznam cíleného běhu na nejstarších systémech R10; běh bez dat reverzní analýzy projde stejně. Úplnou matici vydání dokládá PR.1.3, ne tato položka.
  - **Závisí na:** P1.1.1, P1.1.6
  - **Odhad:** bez odhadu; účtuje se v P1 (kostra aplikace)
- [ ] **PR.1.2** Publikace NativeAOT a kouřový test v CI
  - **Co a proč:** Cílem distribuce je NativeAOT (start 0,29 s v P0 na vývojovém PC), záloha ReadyToRun. CI musí AOT build vyrobit pro win-x64, osx-arm64 a osx-x64 a každý spustit, včetně nativního Intel Macu. Postup a kontroly převezme z prototypu P1.1.8.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - zapsat skutečně použité SDK, runtime pack, ILCompiler, Avalonia, AvaloniaEdit a nativní závislosti (Skia, případný WebView wrapper)
    - u macOS zkontrolovat architekturu a `LC_BUILD_VERSION`/`minos` (případně `LC_VERSION_MIN_MACOSX`) všech Mach-O souborů balíku: aplikace, přibalených procesů a dylib; minimum nesmí být vyšší než macOS 14
    - u Windows zkontrolovat skutečné importy a dostupnost použitých API na buildu 19045 (Windows 10 22H2) a 22621 (Windows 11 22H2)
  - **Hotovo, když:** publikovaná aplikace na všech třech RID otevře soubor, změní ho, uloží, znovu otevře a bajty se shodují s očekávanými, a to na stroji **bez nainstalovaného .NET**; záznam uvádí verze z kroků, výsledek kontroly `minos` a importů a hash artefaktu. Nedostupný běh na kterémkoli RID blokuje distribuční bránu P3.7.5 a P10.1.4, nepočítá se jako splněný.
  - **Závisí na:** PR.1.1, P1.1.2, P1.1.8
  - **Odhad:** bez odhadu; účtuje se v P1 (P1.1.2) a v P3 (P3.7)
- [ ] **PR.1.3** Kompatibilitní matice R10 po buildech vydání
  - **Co a proč:** R10 slibuje plnou podporu a testy na Windows 10 22H2, Windows 11 22H2 a novějších a macOS 14 a novějších. Je to závazek projektu, ne záruka Microsoftu ani Avalonie. Přibalený runtime nemění systémové knihovny OS, proto se dokládá každý systém zvlášť. Tři RID nejsou tři testovací systémy.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - matice: verze a edice OS ze seznamu P1.1.6 × RID (`win-x64`, `osx-arm64`, `osx-x64` na nativním Intelu) × scénář × hash podepsaného balíku
    - scénáře na čistém systému bez nainstalovaného .NET: instalace, první spuštění, otevření, změna a uložení souboru, textové prostředky, Unicode, čtečka (PR.4.1), IME (PR.4.2), spuštění přibaleného pluginu (od 1.0), aktualizace a návrat verze
    - na každém systému ověřit podpis a časové razítko: na macOS od MVP včetně přiloženého notarizačního ticketu offline (PR.3.1), na Windows Authenticode od jeho zavedení v P9 (PR.3.2)
    - po každé aktualizaci přibaleného runtime, ILCompileru nebo nativních knihoven (Avalonia, Skia, WebView) celou matici zopakovat i pro přibalené pluginy; self-contained balík se sám neaktualizuje
    - automatická kontrola každého vydávacího balíku: `minos` všech Mach-O souborů ≤ 14.0, minimum všech PE ≤ 10.0 a statické importy proti DLL buildů 19045 a 22621; kontrolní skript napsat do `tools/` podle postupu v měření (pokus ho v repozitáři nenechal)
    - testy UI ve virtuálu spouštět v přihlášené relaci (naplánovaná úloha), protože v session 0 chybí GPU kompozice (`EGL_CONTEXT_LOST`); vykreslování v softwaru ověřit zvlášť
  - **Výjimka R10:** buňky macOS 14 a nativní Intel mají výsledek „jen staticky“ (P1.1.6).
  - **Hotovo, když:** pro každé vydání (MVP, 1.0) existuje záznam, ve kterém má každá buňka matice výsledek „prošlo“ s hashem balíku a datem; buňka bez běhu má stav **neověřeno** a vydání neprojde. Bez strojů z P1.1.6 (Windows 10 22H2 a macOS 14 dodá majitel) brána vydání neprojde. Výkonové brány R8 tato položka nenahrazuje.
  - **Závisí na:** P1.1.6, P1.1.8, PR.1.2, PR.3.1
  - **Odhad:** bez odhadu; zavedení prostředí v P1, opakované přijetí v P3 (P3.7.5), P9 a P10 (P10.1.4); ocení PR.8.1 a PR.8.2

### PR.2 Testovací korpus a výkonové brány

Cíle kap. 3 platí pro referenční stroje R8 a protokol z revize. Původní měření P0 z 25. 10. 2026 je historické: Windows jen warm na silnějším vývojovém PC (Ryzen 9 7900X3D), Mac na referenční Mac starším kódem prototypu (commit 15e3ebb) a s neznámým stavem cache. Dne 26. 10. 2026 proběhlo měření jádra a prototypu na referenční Windows VM včetně cold. Jeho cold znamená cold ve virtuálu se stavem cache hostitele neznámým a start běžel jen v session 0 bez prezentace. Ani jedno měření není měření produktu; produkt se měří znovu.

- [ ] **PR.2.1** Korpus a malé fixture soubory v testech
  - **Co a proč:** Korpus z plánu musí běžet automaticky: UTF-8 (ASCII i čeština), UTF-16 LE/BE, UTF-32 LE/BE s BOM i bez něj, CP1250/CP1252, Kamenický, poškozené sekvence a vícebajtový znak na hranici bloku, lichý nebo neúplný konec UTF-16 a UTF-32, dlouhé řádky, minifikovaný JSON, logy, CSV s víceřádkovou buňkou, CRLF rozdělené mezi bloky. Generátor velkých souborů je hotový v `bench/NksHub.Textida.Bench`. Dnešní `text-100MiB.log` má 104 857 600 B, ale 103 247 302 znaků, protože obsahuje i české zprávy; je to UTF-8 log, ne čistý ASCII vstup paměťové brány kap. 3.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - nový fixture: čistý ASCII text 100 MiB (jen bajty 0x20–0x7E a LF), s pevným SHA-256 zapsaným v korpusu
    - dosavadní UTF-8 log 100 MiB zůstává jako samostatný případ
  - **Hotovo, když:** malé fixture soubory běží v každém CI běhu; velké soubory generuje harness na vyžádání a hash korpusu je stejný na Windows i macOS; čistý ASCII fixture 100 MiB má ověřený obsah (jen ASCII), hash a vlastní název odlišný od UTF-8 logu.
  - **Závisí na:** PR.1.1
  - **Odhad:** bez odhadu; účtuje se v P1
- [ ] **PR.2.2** Výkonové brány kap. 3 na referenčním profilu
  - **Co a proč:** Každý cíl z kap. 3 potřebuje opakovatelné měření na referenčních strojích (R8): Windows **referenční Windows VM** (10 vCPU, 16 GiB, virtuální disk, profil 4 jader afinitou) a macOS **referenční Mac** (M1 Pro, 32 GB). Cold cache na Windows vyžaduje práva správce, na macOS `sudo purge` se souhlasem majitele.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - harness A1–A7 rozšířit o scénáře produkční aplikace
    - ≥ 30 opakování interaktivních scénářů, všechny vzorky do CSV
    - u každého výsledku build (SHA), stroj, typ úložiště, stav cache, režim a metoda měření prezentace
    - start a paměť prázdné aplikace jen v **přihlášené interaktivní relaci** se skutečnou plochou; vzorek, u kterého selže prezentace (například ANGLE hlásí `Could not create additional swap chains`), se zahodí a běh se opakuje; běh v session 0 je jen orientační
    - cold na referenční Windows VM se zapisuje jako „cold ve virtuálu, cache hostitele neznámá“; když se pro R8 použije tento protokol, platí stejný význam cold v P3.7.3 i P10.1.1
    - u velkých souborů zapisovat private commit i working set
  - **Hotovo, když:** každý cíl kap. 3 příslušného vydání **prošel** na obou strojích R8; záznam uvádí percentil, počet vzorků, cache, režim, SHA buildu, relaci a metodu prezentace. Výsledek „nesplněno“, „neměřeno“ nebo běh ze session 0 bránu nesplňuje; nesplněný cíl jde k rozhodnutí podle plán
  - **Závisí na:** PR.2.1
  - **Odhad:** bez odhadu; účtuje se v P3 (P3.7.3) a P10 (P10.1.1)
- [ ] **PR.2.3** Latence psaní a posunu na skutečném displeji
  - **Co a proč:** Měření v P0 bylo headless a neobsahuje prezentaci na displeji. Cíl p95 ≤ 33 ms, p99 ≤ 80 ms platí od vstupu po prezentaci. Počet přečtených bajtů není čas vykreslení.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** vstup → prezentace má na obou strojích R8 v přihlášené relaci p95 ≤ 33 ms a p99 ≤ 80 ms pro soubor 1 MiB se zvýrazněním, pro stránkovaný režim, pro dlouhý řádek, při změně DPI a během indexace na pozadí, včetně několikaminutové úplné indexace 50GiB souboru; záznam má náležitosti z PR.2.2.
  - **Závisí na:** P1.4.1, P3.2.1
  - **Odhad:** bez odhadu; účtuje se v P3
- [ ] **PR.2.4** Testy odolnosti
  - **Co a proč:** plán: změna, přejmenování, zkrácení a smazání souboru jiným procesem, plný disk při uložení, přerušený přenos, pád během dávkové změny. Obecný pád neověří všechny fáze zápisu.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** každý případ z kap. 8 má automatický test; pád se injektuje před a po flush, před a po výměně souboru a během zápisu žurnálu, včetně plného disku; pomalý disk se simuluje injektovaným zdržením flush/fsync aspoň 1,5 s a UI během něj splní limity kap. 3, uložení jde zrušit a potvrzení nepřijde dřív než flush; po obnově se originál i poslední potvrzená revize porovnají nezávislým výpočtem a žádný případ nevede ke ztrátě neuložené práce ani k poškození souboru.
  - **Závisí na:** P1.5.1, P1.2.3
  - **Odhad:** bez odhadu; účtuje se v P1 a v P3 (hex uložení)

### PR.3 Podpis a notarizace

- [ ] **PR.3.1** Podpis a notarizace pro macOS
  - **Co a proč:** Aplikace pro macOS musí být podepsaná a notarizovaná pro Apple Silicon i Intel. Totéž platí pro spustitelné soubory přibalených pluginů.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** balík skutečně stažený přes prohlížeč projde Gatekeeperem na čistém Macu s Apple Silicon i Intel bez ruční výjimky, a to na macOS 14 i na novějších verzích ze seznamu P1.1.6; každý distribuovaný spustitelný soubor včetně přibalených procesů je podepsaný Developer ID s Hardened Runtime a časovým razítkem; přiložený notarizační ticket projde kontrolou bez sítě; záznam uvádí Developer ID, výsledek notarizace, hash balíku a verzi OS pro oba RID; přibalené pluginy se spustí.
  - **Závisí na:** PR.1.2
  - **Odhad:** bez odhadu; účtuje se v P3 (P3.7.1) a P9
- [ ] **PR.3.2** Podpis buildů pro Windows (Authenticode)
  - **Co a proč:** plán P9 určuje podpis buildů pro Windows přes Authenticode. Zbývá provedení: typ certifikátu a přístup k němu.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** instalátor i spustitelné soubory z CI jsou podepsané Authenticode s časovým razítkem; ověření podpisu výsledného artefaktu projde na čistém Windows 10 22H2 i na Windows 11 ze seznamu P1.1.6, u výsledného balíku i u přibalených pluginů; záznam uvádí hash balíku a verzi OS. Podpis dokládá původ, ne kompatibilitu (tu dokládá PR.1.3).
  - **Závisí na:** PR.1.2
  - **Odhad:** bez odhadu; účtuje se v P9

### PR.4 Přístupnost a IME

**Matice R10 pro čtečku a IME** (dál jen „matice R10“): každá verze OS ze seznamu P1.1.6; na Windows NVDA, na macOS VoiceOver; na macOS oba RID (`osx-arm64` a `osx-x64` na nativním Intelu); vždy publikovaný AOT build ve skutečné přihlášené relaci. VM pro headless testy není důkaz čtečky, IME ani prezentace. Čtyři jazyky UI nejsou čtyři klávesnice: cs, en, sk a de nenahrazují japonské a čínské IME. Nová textová plocha (stránkovaná plocha, hex, příspěvky pluginů) dostane vlastní důkazy, nepřebírá je z jiné plochy.

- [ ] **PR.4.1** Kontrola čtečkou obrazovky v každé etapě
  - **Co a proč:** Přístupnost začíná v P0 a P3, plná kontrola je až v P9. Každá etapa proto projde stejný ruční seznam s NVDA a VoiceOverem.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** na konci každé etapy má každý scénář nových obrazovek a dialogů v matici R10 předem zapsaný očekávaný text, slovo, výběr, pozici kurzoru, fokus a změnu revize a vedle něj skutečný výsledek; žádná blokující odchylka nezůstala, každá závada má reprodukci a navázaný bod opravy. Záznam se vede zvlášť pro každé vydání se SHA buildu: MVP pokrývá obrazovky MVP, 1.0 se celý zopakuje na buildu 1.0 včetně příspěvků přibalených pluginů.
  - **Závisí na:** P1.8.1
  - **Odhad:** bez odhadu; účtuje se v etapě, která obrazovku přidala
- [ ] **PR.4.2** Kontrola IME při každém vydání
  - **Co a proč:** Skutečný IME nejde otestovat headless. Ručně česká, japonská a čínská klávesnice, i s více kurzory.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** před každým vydáním je v matici R10 u tří klávesnic zapsané očekávání a výsledek pro zahájení, změnu, potvrzení a zrušení kompozice, přepnutí karty během kompozice, změnu DPI, více kurzorů a undo; nic se nezdvojí ani neztratí; záznam uvádí SHA buildu vydání.
  - **Závisí na:** P1.8.2
  - **Odhad:** bez odhadu; účtuje se v P3 (vydání MVP) a P9
- [ ] **PR.4.3** Kontrast a vysoký kontrast
  - **Co a proč:** Prototyp P0 přepočítal kontrast světlého i tmavého tématu podle WCAG na ≥ 4,5 : 1. Produkční vzhled D5 se kontroluje znovu. V hexu se změny neoznačují jen barvou.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** automatický test spočítá kontrast všech dvojic barev textu a pozadí z témat a každá dvojice pod 4,5 : 1 test shodí; systémový vysoký kontrast na Windows i macOS se ručně ověří; změny v textu, diffu a hexu jdou rozlišit i bez barvy.
  - **Závisí na:** P1.1.3
  - **Odhad:** bez odhadu; účtuje se v P1 a P9

### PR.5 Dokumentace

- [ ] **PR.5.1** Soubory LICENSE a README
  - **Co a proč:** Repozitář nemá `LICENSE` ani `README.md`. Licence je MIT (R3, rozhodnuto 26. 10. 2026). Jazyk README a uživatelských návodů určí matice textů P1.1.7.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** v kořeni je `LICENSE` s textem MIT a `README.md` s popisem, požadavky (podporované systémy podle R10) a postupem sestavení, který projde na čistém stroji podle textu README; jazyk README odpovídá P1.1.7.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu; účtuje se v P1
- [ ] **PR.5.2** Záznam odchylek od PSPadu
  - **Co a proč:** rozbor PSPadu žádá zapsat odchylky do `docs/deviations` (přesnější sonda UTF-8, UTF-32, hlášení ztráty a smíšených konců). Stejně se zapíšou odchylky z `08`.
  - **Funkce:** žádná (dokumentace)
  - **Hotovo, když:** každá vědomá odchylka od chování PSPadu má řádek s odkazem na kapitolu rozboru a na test.
  - **Závisí na:** P1.3.1
  - **Odhad:** bez odhadu; účtuje se v P1–P3

### PR.6 Bezpečnost a licence

- [ ] **PR.6.1** Bezpečnostní revize před MVP
  - **Co a proč:** Brána MVP nesmí čekat na distribuci 1.0. Projde pravidla, která platí pro MVP: síť jen po svolení, žádná telemetrie (PR.6.4), aktualizace s ověřením původu (P3.7.2), bezpečné uložení a obnova, žádná data PSPadu v balíku.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** každé pravidlo z zadání, které se týká funkcí MVP, má test nebo záznam ruční kontroly s buildem a výsledkem; žádné nesplněné pravidlo nezůstalo bez opravy.
  - **Závisí na:** P3.7.1, P3.7.2, PR.6.4
  - **Odhad:** bez odhadu; účtuje se v P3
- [ ] **PR.6.3** Bezpečnostní revize před 1.0
  - **Co a proč:** Před 1.0 projít všechna pravidla z kap. 5.2–5.4: lokální transport MCP, port mostu F179 jen po potvrzení, výslovné zpřístupnění karet, síť jen po svolení, oprávnění pluginů a hranice záruky API u agentů (P6.6.3), instalace ZIP, záznam volání bez obsahu a tokenů, jeden filtr maskování tajemství (P4.6.2).
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** každé pravidlo z zadání má test nebo záznam ruční kontroly s buildem na všech třech RID a výsledkem; žádné nesplněné pravidlo nezůstalo bez opravy.
  - **Závisí na:** PR.6.1, P9.6.1, P6.6.3
  - **Odhad:** bez odhadu; účtuje se v P10
- [ ] **PR.6.4** Síťový test: žádná telemetrie
  - **Co a proč:** Zadání zakazuje telemetrii editoru i vlastních pluginů, nejen telemetrii obsahu. Síť pro funkce, které uživatel sám vyžádá (FTPS, registr balíčků, count_tokens), se posuzuje zvlášť.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** běh editoru bez aktivace síťových funkcí nezaznamená žádné odchozí spojení kromě dotazu na verzi z P3.7.2, a ten jen se zapnutou kontrolou aktualizací; test běží v CI na Windows i macOS. Záznam se vede po vydáních: pro MVP nad buildem MVP bez pluginů, pro 1.0 znovu nad buildem 1.0 se všemi přibalenými pluginy.
  - **Závisí na:** PR.1.1
  - **Odhad:** bez odhadu; účtuje se v P3, rozšíření o pluginy v P10
- [ ] **PR.6.2** Kontrola licencí a čistého přepisu
  - **Co a proč:** Nic z reverzní analýzy ani z PSPadu nesmí do repozitáře a balíku. GPL a AGPL jen jako cizí proces. Šablony struktur jen CC0, MIT, BSD; schémata jen s ověřenou licencí.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** CI selže, když je v repozitáři cesta `re/` nebo soubor z distribuce PSPadu; seznam závislostí s licencemi je součástí balíku (about).
  - **Závisí na:** PR.1.1
  - **Odhad:** bez odhadu; účtuje se v P1 a P10

### PR.7 Matice funkcí

- [ ] **PR.7.1** Dohledatelnost matice a kontrola TODO
  - **Co a proč:** Jedna práce se dvěma kontrolami (dřívější PR.7.1 a PR.7.2). Rozsah funkcí (vydání, rozhodnutí, popis) generuje `tools/gen_matrix.py`. Důkazy, tedy sloupce Stav, Build, Win a mac, doplňuje vývoj ručně přímo v matici funkcí a generátor je zachová. Do sloupce mac se píše datum zvlášť pro arm64 a x64. `tools/check_todo.py` porovná TODO s maticí a s kap. 6 plánu, kontroluje závislosti, rozdělené funkce a dvojí započtení odhadů; spouští se po každé změně matice, plánu nebo TODO.
  - **Funkce:** žádná (infrastruktura)
  - **Hotovo, když:** po každé dokončené položce TODO mají její funkce v matici stav, build a data pro Windows, Mac arm64 a Mac x64 a nové vygenerování matice je nesmaže; sloupce Win a mac odkazují na záznam kompatibility PR.1.3 daného vydání, ne na jeden stroj; brána 1.0 zapíše nové údaje nad buildem 1.0 a nepřebírá údaje z MVP; `python -I tools/check_todo.py` hlásí 0 nálezů a `--selftest` projde.
  - **Závisí na:** PR.1.1
  - **Odhad:** bez odhadu; účtuje se v etapě dokončené položky

### PR.8 Plánovací brány: ceny, podíly a vnější závislosti

Revize revize a 6.2: řada položek nemá cenu, rozdělené funkce nemají podíly po etapách a není doložené, že se práce vejde do starých rozpočtů. Čísla se nevymýšlejí. Místo toho má každá neoceněná práce vlastníka a bránu, před kterou se ocení. Návrh ceny dělá vývojář, schvaluje majitel.

- [ ] **PR.8.1** Vstupní brána P1: ocenění a podíly
  - **Co a proč:** P1 má 43 úkolů, z nich 37 bez čísla (`--stats`, 26. 10. 2026); oceněné části dávají 11–17 ČT proti rozpočtu 13–20 ČT. Není doložené, že se do rozdílu vejde kostra, bajtový model, D5, CI, UIA/AX, IME, nastavení a nový rozsah R10.
  - **Funkce:** žádná (plánování)
  - **Kroky:**
    - ocenit nebo dát podíl každé položce P1 bez čísla, včetně pokusu o cestu 100 MiB (P1.2.9), prototypu R10 (P1.1.8), testovacích strojů (P1.1.6), CI (PR.1.1, PR.1.2), UIA/AX (P1.8.3, P1.8.1) a IME (P1.8.2)
    - u R10 oddělit zavedení prostředí v P1 od platformních oprav a opakovaného přijetí vydání v P3, P9 a P10; před odhadem vypsat konkrétní testovací kombinace, jejich automatizaci a ruční práci
    - výsledek zapsat do odhadů položek a do rozpočtu P1 v plánu, zadání, plán a do Souhrnu; `tools/check_todo.py` musí projít
  - **Hotovo, když:** každá položka P1 má číslo nebo číselný podíl se zdrojem, nebo zapsaný důvod, proč ji nejde ocenit, se jménem bodu, který ji ocení; součet je porovnaný s rozpočtem P1 a rozdíl je buď schválený majitelem, nebo promítnutý do rozpočtu. Do té doby je rozpočet P1 neúplný.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu (plánovací práce v P1)
- [ ] **PR.8.2** Vstupní brána každé další etapy: ceny, podíly a limity
  - **Co a proč:** Před zahájením každé etapy P2–P10 se stejným postupem ocení její neoceněné položky. Tahle brána nese i rozdělení cen, které dnes chybí: F155 3–5, F169 2–4 a F248 1–2 ČT (dohromady 6–11 ČT) mezi P3 a P5, P7 nebo P9; přesun částí F154 z P7 (10–16 ČT) do MVP, dřív do P3.5.5 a P3.5.9, po spiku měření a rozhodnutí majitele 26. 10. 2026 i do P1.2.8, P3.1.5 a P3.1.11–P3.1.13, včetně odečtu z P7 (podíl P1.2.8 zapíše PR.8.1); cenu editace 100 MiB (P3.1.10–P3.1.13), kterou spike nedal; dohledání, zda rozpočet P5 24–37 ČT obsahoval dřívějších 0,5 ČT za úsek CSV jako tabulku (F160); podíl API pro časné nástroje (P3b.3.5) ze zbytku P3b 5–7 ČT; podíl P8b.1.1 a dokumentace SDK ze zbytku P8b 2–4 ČT; cenu důsledků R10 v P9 (čtyři překlady, jejich kontrola, opravy UI, platformní testy, odděleně jednorázová práce a opakování vydání) po inventáři P1.1.7. Celých 6–11 ČT nejde automaticky přičíst k 1.0, pozdější rozpočty je mohou obsahovat.
  - **Funkce:** žádná (plánování)
  - **Kroky:**
    - pro etapu vypsat položky bez čísla a ocenit je, nebo jim dát podíl
    - u rozdělené funkce určit podíl každé etapy tak, aby součet podílů nepřekročil cenu z matice; podíl zapsat jako „N ČT v rámci F…“
    - zapsat hodnoty „nastavených“ limitů a uzavřené seznamy položek etapy (vstupní rozhodnutí z úvodu TODO)
    - přepočítat rozpočet etapy a souhrny v plánu, zadání, plán a v Souhrnu; `tools/check_todo.py` musí projít
  - **Hotovo, když:** pro každou etapu existuje před jejím zahájením záznam: položky s cenou nebo podílem, vstupní rozhodnutí limitů a seznamů, přepočtený rozpočet schválený majitelem. Podíly P3 na F155, F169 a F248, přesun F154, cena P3.1.10–P3.1.13 a rozpočet P3 jsou uzavřené před zahájením P3; cena důsledků R10 v P9 je uzavřená nejpozději před zahájením P9 a promítnutá do ceny 1.0 hned po inventáři P1.1.7. Do té doby jsou rozpočty P3 až P10 a součty MVP a 1.0 neúplné.
  - **Závisí na:** PR.8.1, P1.1.7
  - **Odhad:** bez odhadu (plánovací práce na začátku každé etapy)
- [ ] **PR.8.3** Nezávislý autor pluginu pro R9: získání
  - **Co a proč:** R9 podmiňuje API 1.0 pluginem aspoň jednoho nezávislého autora napsaným jen podle veřejné dokumentace (P8b.2.4). Takový autor dnes není určený: **neověřeno**. Nezávislý autor není druhý proces ani další vlastní ukázkový plugin. Aby přejímka nebyla náhodné čekání na konci, autor se získá předem.
  - **Funkce:** žádná (vnější závislost)
  - **Hotovo, když:** majitel zapíše jméno nebo organizaci autora, potvrzení, že nemá přístup do interního kódu, a dohodnutý termín, a to nejpozději před zahájením P8b.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu (práce majitele, mimo rozpočet editoru)
- [ ] **PR.8.4** Dohoda o kontraktu wiki NKS IS
  - **Co a proč:** Bezpečné uložení wiki (P6.4.1) potřebuje atomickou kontrolu revize na straně NKS IS (P6.4.4). Je to práce mimo tento repozitář a mimo rozpočet editoru. Dnes chybí vlastník dodávky i termín.
  - **Funkce:** žádná (vnější závislost)
  - **Hotovo, když:** majitel zapíše odpovědnou roli a osobu na straně NKS IS, dohodnutý tvar parametru očekávané revize, kapacitu mimo rozpočet editoru a termín dodání, a to nejpozději před zahájením P6.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu (mimo rozpočet editoru)

## P0 – ověřit rizika (hotovo)

P0 je uzavřená (5–7 ČT). Výsledky: měření, screenshoty v měření.

### P0.1 Výsledky

- [x] **P0.1.1** Stránkované jádro `src/NksHub.Textida.Core`
  - **Co a proč:** Dokument nad souborem mapovaným do paměti, strom úseků s undo, líný index řádků, uložení streamem, detekce kódování, hledání literálu a regexu se zrušením. 59 testů prošlo na Windows i macOS.
  - **Funkce:** žádná (P0 ověřovalo technologii)
  - **Hotovo, když:** splněno; brána P0 jádro přijala.
  - **Závisí na:** žádná
  - **Odhad:** součást P0
- [x] **P0.1.2** Měřicí harness `bench/NksHub.Textida.Bench`
  - **Co a proč:** Generátor korpusu, měření A1–A7, scénář `hex`, skript `bench/run-mac.sh` pro macOS.
  - **Funkce:** žádná (P0 ověřovalo technologii)
  - **Hotovo, když:** splněno; výsledky v měření.
  - **Závisí na:** žádná
  - **Odhad:** součást P0
- [x] **P0.1.3** Prototyp UI `src/NksHub.Textida.P0.App` a headless testy
  - **Co a proč:** Avalonia 12, karty, paleta, Goto Anything, víc kurzorů jako vrstva nad AvaloniaEdit, Code Explorer, velký soubor, inspektor kódování, varianty vzhledu. Screenshoty a měření UI z `tests/NksHub.Textida.P0.App.Tests`.
  - **Funkce:** žádná (P0 ověřovalo technologii)
  - **Hotovo, když:** splněno; verdikty v měření.
  - **Závisí na:** žádná
  - **Odhad:** součást P0
- [x] **P0.1.4** Brána P0 a výběr vzhledu
  - **Co a proč:** Rozhodnutí: jádro přijato; AvaloniaEdit jen pro menší soubory; TextMate odmítnut; vlastní hledání bez ripgrepu; NativeAOT; macOS povinný. Majitel vybral D5 „Klidný“.
  - **Funkce:** žádná (P0 ověřovalo technologii)
  - **Hotovo, když:** splněno 25. 10. 2026.
  - **Závisí na:** žádná
  - **Odhad:** součást P0

### P0.2 Co se pro produkt přepíše

Prototyp P0 není základ produktu. Z něj se přebírají ověřené postupy, kód se píše znovu:
- `NksHub.Textida.P0.App` má ukázková data. Hledání v souborech, náhrada, hot exit, uložení nastavení a import jsou jen vzhled; panel souborů nevidí disk.
- Karty a řádky palety jsou `Border` bez role a fokusu; nahradí je `TabStrip`, `ListBox`, `Button`.
- `PspadScan.cs` a průvodce importem čtou data PSPadu z lokální kopie PSPadu. To do produktu nesmí (kap. 7, licence bod 3).
- `MultiCaret` drží výběry jako čísla, ne kotvy, a undo obnoví jen primární kurzor.
- TextMate zvýrazňování se nahradí vlastním zvýrazňovačem.
- Přepínače vzhledů `--variant` a `--look` zůstanou jen v prototypu; produkt má jen D5.
- `NksHub.Textida.Core`: po úpravě chybí čísla řádků, UTF-16 stránkovaný zdroj chybí, add buffer má limit 2 GiB, úpravy během uložení se odmítají, uložení přes otevřený soubor smaže undo a hledání běží jen nad souborem na disku.
- Prototyp hex mřížky je jen v benchmarku (`bench/NksHub.Textida.Bench/HexView.cs`).

## P1 – bezpečný základ

Odhad etapy 13–20 ČT, **neúplný**. Položky s vlastním odhadem dávají 11–17 ČT, ostatní jsou bez odhadu. Že se vejdou do rozdílu 2–3 ČT, doložené není; ocení je PR.8.1 před převzetím etapy. Hotovo, když: otevřít → upravit → uložit → obnovit po pádu bez poškození; kódování a EOL se zachovají bajtově; cesta pro editaci 100 MiB v MVP (editovatelný stránkovaný režim, vybraný spikem) je doměřená na prototypu s UI tak, že splní cíle současně (P1.2.9); kompatibilitní prototyp R10 prošel (P1.1.8). Vše v P1–P3 je v jádře; MVP nečeká na veřejné API.

Souběžně mohou běžet kostra, D5 a CI (P1.1) a čisté jádro dokumentu (P1.2, P1.3); stejně schéma nastavení (P1.7) a registr příkazů (P1.6). Společná je integrační brána etapy. Jádro dokumentu jde v pořadí kontrakt a testovací data (P1.2.1) → adaptéry (P1.2.7, P1.2.8) → společné přijetí (P1.2.10); dvě různé smlouvy nevzniknou. Kostrou, smlouvou dokumentu a ověřovacími prototypy jde začít hned; celou etapu převezme PR.8.1.

### P1.1 Kostra produkční aplikace

Nový produkční projekt nahradí prototyp. Od začátku běží pod NativeAOT a ve vzhledu D5 na obou OS.

- [ ] **P1.1.1** Produkční projekt aplikace
  - **Co a proč:** Nový projekt aplikace vedle `NksHub.Textida.Core`, bez kódu prototypu, který čte data PSPadu. Start rovnou do pracovní plochy, bez povinného dialogu šablon. Umístění profilu portable i uživatelské. Poloha okna se obnoví na dostupném monitoru.
  - **Funkce:** F002, F021, F143
  - **Kroky:**
    - projekt aplikace a testovací projekt v `NksHub.Textida.sln`
    - umístění profilu: portable vedle aplikace nebo v profilu uživatele, vlastní formát
    - obnova polohy okna s kontrolou monitoru
  - **Stav 26. 10. 2026:** projekt `NksHub.Textida.App` hotový (commity 6829658, ecf1cf1): start do prázdné plochy, profil portable i uživatelský, umístění okna jako čistá funkce s 12 testy včetně odpojeného monitoru. Neověřeno odpojení skutečného monitoru.
  - **Hotovo, když:** aplikace startuje na Windows i macOS do prázdné pracovní plochy; po odpojení monitoru se okno otevře na dostupném; nic v projektu nečte soubory PSPadu a produktová sada projde bez dat reverzní analýzy.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu
- [ ] **P1.1.2** NativeAOT od P1: trimování a reflexe
  - **Co a proč:** V P0 se pod NativeAOT ověřil jen start a první snímek, ne celá aplikace. NativeAOT vyžaduje trimming a omezuje dynamické načítání. Každá nová závislost se musí ověřit pod AOT hned, i v hotovém programu, ne jen v knihovně.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - AOT publikace aplikace s varováními trimování a AOT jako chybou ve vlastním kódu
    - seznam závislostí s ověřenou AOT cestou (Avalonia, AvaloniaEdit, JSON) a evidence výjimek knihoven
    - testy knihoven pod JIT odděleně od scénářů nad publikovaným programem
    - ReadyToRun vést jako technickou zálohu k ověření, ne jako automaticky přijatelnou náhradu: na referenční Windows VM mělo plné okno v session 0 warm p95 1,19 s proti limitu 0,5 s a cold p95 1,67 s proti 1,5 s; záznam rozliší funkční zálohu a zálohu, která splní výkon
  - **Stav 26. 10. 2026:** AOT publikace win-x64 a osx-arm64 bez varování, analyzátory jako chyby (úmyslné porušení spadne na IL2026/IL3050), seznam závislostí v interním záznamu; `--smoke` na Windows skončí 0 za ~0,8 s, na macOS (minos 14.0) projde smoke i e2e. Měření startu s prázdnou relací (26. 10. 2026, build 4a3b998, 30 warm + 30 cold po 3 zahozených, čas do druhého vykresleného snímku; `p1/startup-measure.md`): referenční Windows VM v přihlášené relaci warm p50/p95 242/272 ms, cold 553/761 ms; referenční Mac warm 197/257 ms při běžné zátěži (382 ms a 1 755 ms ve dvou sadách se souběžným buildem), cold p95 2,48 s jen pod silnou zátěží, bez spolehlivého verdiktu; vývojové PC warm 253/288 ms. Brána warm p95 ≤ 500 ms tedy splněná na Windows, na referenční Mac ve 2 ze 3 sad. ReadyToRun na referenční Windows VM funkční (120 startů bez chyby), výkonem ne: warm p95 1,04 s, cold p95 1,83 s. UIA a AX v AOT viz P1.8.3. Chybí: cold po restartu, referenční Mac bez souběžné zátěže a s fyzickým displejem (se zavřeným víkem padá na RenderTimer -6661), proces pluginu.
  - **Hotovo, když:** scénáře nad publikovaným AOT programem dají na Windows i macOS stejný výsledek jako JIT u I/O, textových prostředků, serializace, UIA/AX a spuštění pluginového procesu; každé varování trimování a AOT má opravu, nebo zdůvodněnou výjimku s testem; start s prázdnou relací má v přihlášené relaci warm p95 ≤ 500 ms (PR.2.2). Přechod z AOT na ReadyToRun je možný jen po zopakování všech výkonových a kompatibilitních bran nad buildem ReadyToRun.
  - **Závisí na:** P1.1.1
  - **Odhad:** bez odhadu
- [ ] **P1.1.3** Vzhled D5 na Windows a macOS
  - **Co a proč:** D5 je jediný produkční vzhled. Na Windows menu v titulkovém pruhu a karty pod ním, Mica, systémový akcent. Na macOS skutečný AppKit: karty v titulku vedle semaforu, menu v systémové liště, zkratky ⌘. Témata světlé a tmavé; barvy PSPadu se nenačítají (import až po 1.0). Na Windows 10 není Mica, pozadí je jednobarevné podle tématu (R10).
  - **Funkce:** F105, F008, F068
  - **Stav 26. 10. 2026:** D5 shell na Windows se světlým i tmavým tématem a Mica na Windows 11; review proti D5 (1. kolo) našlo odchylky: menu jen 4 položky, chybí čísla řádků, minimapa, tečka neuložené karty a stavový řádek podle D5. macOS vzhled bez snímku, DPI, Windows 10 22H2 a macOS 14 neověřeno.
  - **Hotovo, když:** hlavní okno na obou OS odpovídá měření kap. D5: nahoře jen menu a karty, žádný pruh tlačítek; na každém OS se navíc funkčně ověří menu, titulková oblast, fokus, přepnutí světlého a tmavého tématu za běhu, změna DPI a dostupnost skrytých panelů z klávesnice; na Windows 10 22H2 má okno jednobarevné pozadí podle tématu.
  - **Závisí na:** P1.1.1
  - **Odhad:** bez odhadu
- [x] **P1.1.4** Jedna instance a příkazová řádka
  - **Co a proč:** Druhé spuštění předá cesty běžící instanci přes vlastní IPC; výslovně jde otevřít nové okno. Příkazová řádka otevře cestu a pozici. `--wait` skončí až po zavření karty, pro Git a `crontab -e`.
  - **Funkce:** F114, F003
  - **Stav 26. 10. 2026:** jedna instance přes pojmenovanou rouru a unix socket, `--line`, `--wait`, neznámý přepínač skončí 2 se zprávou; ověřeno na publikovaném exe na Windows i macOS. Zavření jedné karty při `--wait` jen v unit testech.
  - **Hotovo, když:** spuštění s cestou a řádkem otevře kartu v běžícím okně na tom řádku; s `--wait` vrátí řízení až po zavření karty; neznámý přepínač se ohlásí.
  - **Závisí na:** P1.1.1
  - **Odhad:** F003 `--wait` 0,5 ČT; jinak bez odhadu
- [x] **P1.1.5** Texty rozhraní v prostředcích
  - **Co a proč:** Všechny texty UI jsou v textových prostředcích, ne v kódu, aby šly v P9 přeložit. P1 staví infrastrukturu: klíče, zástupné parametry, množná čísla a fallback. Rozsah jazyků MVP a dalších textů určí P1.1.7; úplnost překladů cs, en, sk a de pro 1.0 ověří P9.2.2. Prostředky v kódu samy nedokazují překlad, změnu jazyka ani přístupné názvy.
  - **Funkce:** F104
  - **Stav 26. 10. 2026:** klíče, zástupné parametry, množná čísla a fallback; hotovo: cs, en, sk a de v obou sadách textů (aplikace i registr příkazů), jeden seznam jazyků `StringTable.UiLanguages`; testy najdou chybějící klíč a rozdílné zástupné parametry v každém jazyce i text UI v kódu (Roslyn); každý klíč s parametrem nebo množným číslem se zkouší ve všech jazycích pro počty 0–101; přepnutí přes všechny jazyky a zpět nemění bajty dokumentu ani souboru nastavení; volba jazyka v menu Nastavení. Ověřeno i během: smoke přepne en, sk, de a zpět, menu a přístupné názvy přečtené přes UI Automation v každém jazyce. Překlady sk a de nekontroloval rodilý mluvčí (P9.2.2). Nalezeno: prvek menu nemá přístupný název a tlačítka okna od Avalonie jsou vždy anglicky (P1.8.1).
  - **Hotovo, když:** existuje inventář klíčů textů (menu, paleta, stavový řádek, skryté panely, dialogy, chybová hlášení, přístupné názvy); test najde chybějící klíč v kterémkoli jazyce, který P1.1.7 určí pro MVP, a nenajde text UI zapsaný přímo v kódu; klíč se zástupným parametrem a s množným číslem má test pro každý takový jazyk; přepnutí jazyka a návrat na výchozí jazyk nenechá prázdný popisek a nezmění bajty dokumentu ani strojové formáty nastavení.
  - **Závisí na:** P1.1.1, P1.1.7
  - **Odhad:** bez odhadu
- [ ] **P1.1.6** Matice podpory R10 a testovací stroje
  - **Co a proč:** Systémy rozhodl majitel 26. 10. 2026 (R10): plně podporované a testované jsou **Windows 10 22H2, Windows 11 22H2 a novější a macOS 14 a novější**, s přibaleným runtime. Je to závazek projektu. Oficiální podklady ho samy nekryjí:
    - seznam .NET 10 neuvádí běžné Windows 10 22H2 ani Windows 11 22H2; u Windows 11 23H2 rozlišuje edice a uvádí 23H2 Enterprise a Education ([S1], [S2])
    - release seznam .NET 10 řadí macOS 14 mimo podporu, instalační stránka Microsoft Learn ho uvádí; zdroje se rozcházejí ([S1], [S5])
    - Avalonia 12 řadí Windows 10 22H2, Windows 11 od 22H2 pod 24H2 (u 23H2 podle výkladu minim) a macOS 14 a 15 do Tier 2, tedy bez pravidelného testování dodavatelem; Tier 1 je Windows 11 od 24H2 ([S7])
    - přibalený runtime ani NativeAOT nemění operační systém ani jeho knihovny; podporu dokládá jen ověřený balík na konkrétním systému ([S3], [S4])
    - zdroje [S1]–[S7] jsou v revizi
  - **Funkce:** žádná (rozhodnutí a záznam)
  - **Kroky:**
    - seznam verzí a edic s datem: nejméně Windows 10 22H2, Windows 11 22H2, 23H2, 24H2 a každé další vydané Windows 11, macOS 14, 15 a každé další vydané macOS; seznam se obnoví před každým vydáním podle oficiálních seznamů vydání
    - ke každé položce seznamu stroj nebo VM, architektura, odpovědná osoba, způsob interaktivního běhu (přihlášená relace) a stav dostupnosti
    - dnešní pokrytí: referenční Windows VM je Windows 11 22H2 x64, ale dosavadní běhy byly v session 0, ne v přihlášené relaci; referenční Mac je macOS 15.7.4 arm64 a nepokrývá macOS 14 ani nativní Intel; dostupnost ostatních strojů je **neověřeno**
    - Windows 10 22H2: testovací PC s Windows 10; majitel ho zapne, povolí probouzení po síti a nainstaluje klienta vzdáleného přístupu
    - **Výjimka (rozhodnutí majitele 26. 10. 2026):** macOS 14 a nativní Intel Mac nemáme. Ověřují se jen bez spuštění (`minos` a importy všech binárek), verze pro Intel navíc během přes Rosettu na macOS 15 (referenční Mac). Kde kritérium uvádí macOS 14 nebo nativní Intel, platí tato výjimka a v záznamu stojí „jen staticky“. Windows 10 22H2 pokrývá testovací PC s Windows 10: majitel ho zapne a nainstaluje na něj klienta vzdáleného přístupu, build 19045 se ověří před prvním během.
    - seznam zapsat do CI (PR.1.1, PR.1.3) a do dokumentace (README, P1.1.7)
    - výkon na referenční Windows VM a referenční Mac zůstává zvláštní branou R8; testy instalace a aktualizace celého produktu nejsou v P1, patří P3.7, P9.6 a branám vydání (PR.1.3)
  - **Hotovo, když:** seznam a tabulka strojů jsou zapsané a schválené majitelem; testovací PC s Windows 10 s Windows 10 22H2, referenční Windows VM s Windows 11 22H2 a referenční Mac s macOS 15 jsou skutečně dostupné pro přihlášený běh. Bez testovací PC s Windows 10 tento bod ani žádná brána vydání neprojde. macOS 14 a nativní Intel mají podle výjimky R10 záznam „jen staticky“.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu; ocení PR.8.1
- [x] **P1.1.7** Matice dodávaných textů a jazyků
  - **Co a proč:** R10 určuje jazyky UI pro verzi 1.0: cs, en, sk a de. Neurčuje jazyk ostatních dodávaných textů ani jazykový rozsah MVP. Čtyři jazyky UI samy nevyžadují čtyři jazyky programátorského API ani vývojářské dokumentace. Tyto rozsahy se tu zapíšou jako rozhodnutí majitele, bez automatického rozšíření R10.
  - **Funkce:** žádná (rozhodnutí a záznam)
  - **Kroky:**
    - matice oblast × jazyk × vydání: UI editoru včetně menu, palety, stavového řádku, skrytých panelů, obrazovek obnovy a updateru; příspěvky vlastních pluginů; vlastní obrazovky instalátoru a aktualizátoru (systémové dialogy se vlastním textem nepřekládají); offline nápověda (F142); README a uživatelské návody; veřejné SDK a jeho dokumentace
    - u každé buňky: dodává se / nedodává se / rozhodne majitel, a vydání (MVP, 1.0)
  - **Stav 26. 10. 2026:** hotovo: majitel rozhodl, že UI, přístupné názvy, obrazovky instalátoru a aktualizátoru, offline nápověda, README a návody mají cs, en, sk a de už v MVP; SDK a jeho dokumentace jen anglicky.
  - **Hotovo, když:** matice je zapsaná v zadání a majitel u ní výslovně rozhodl jazykový rozsah MVP a rozsah překladu nápovědy, návodů a SDK; dokud rozhodnutí chybí, je tento rozsah **otevřený** a nepočítá se za schválený.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu; ocení PR.8.1
- [ ] **P1.1.8** Kompatibilitní prototyp R10: AOT hello-world s Avalonií 12
  - **Co a proč:** Běžný JIT build ani stávající výsledky referenční Windows VM nedokazují, že publikovaný balík běží na nejstarších systémech R10. Minimální prototyp to ověří dřív, než na tom stojí celé P1.
  - **Funkce:** žádná (ověření)
  - **Kroky:**
    - NativeAOT hello-world s Avalonií 12 v připnuté verzi: okno, textové pole, uložení souboru, Unicode text, přístupný název prvku; stejný program jako ReadyToRun pro zálohu
    - zapsat skutečné SDK, runtime pack, ILCompiler, Avalonia a nativní závislosti
    - pro macOS AOT výslovně nastavit a zapsat `AppleMinOSVersion` (výchozí hodnota v .NET v10.0.13 je 12.0, [S6a], [S6b]); u všech Mach-O souborů zkontrolovat architekturu a `minos` (`LC_BUILD_VERSION`, případně `LC_VERSION_MIN_MACOSX`); samotné přejmenování RID ani popisek instalátoru nestačí
    - pro Windows zkontrolovat skutečné importy proti buildům 19045 a 22621 ([S6c])
    - spustit na čistém systému bez nainstalovaného .NET: Windows 10 22H2 x64, Windows 11 22H2 x64 (referenční Windows VM v přihlášené relaci), macOS 15 arm64 a x64 přes Rosettu (referenční Mac); macOS 14 a nativní Intel jen staticky (výjimka R10 v P1.1.6)
    - opravy z pokusu měření:
      - `AppleMinOSVersion=14.0` se projeví jen při buildu z čisté složky `obj`, jinak zůstane 12.0; vydávací build proto vždy z čistého stavu (PR.1.2)
      - styly přidané v kódu přes `StyleInclude` pod NativeAOT padají, styly jen v XAML a AOT test na to
      - Avalonia.Native padá s „RenderTimer -6661“, když na Macu spí displej: nahlásit Avalonii, regresní test se zavřeným víkem nebo spícím displejem
  - **Stav 26. 10. 2026**:
    - během ověřeno na Windows 11 22H2 (referenční Windows VM, AOT i ReadyToRun, v přihlášené relaci přes naplánovanou úlohu), Windows 11 26H2 a macOS 15.7.4 (arm64 i x64 přes Rosettu): otevření, psaní přes události Avalonie, uložení, kód 0, D5 s Mica a kartami v titulku
    - jen bez spuštění: Windows 10 22H2 (minimum všech PE 10.0, 5 893 statických importů je v DLL buildu 19045.6396, z 925 P/Invoke chybí jen 4 nevolané funkce ML-KEM) a macOS 14 (hlavní Mach-O `minos` 12.0, dylib 11.0 a 10.13)
    - neověřeno: psaní přes klávesnici systému a IME, čtečky, Windows 11 23H2 až 25H2, macOS 26, nativní Intel Mac
  - **Hotovo, když:** na každém ze čtyř systémů se prototyp spustí v přihlášené relaci, okno se vykreslí, text jde napsat a uložit a soubor má očekávané bajty; NVDA nebo VoiceOver přečte přístupný název; záznam uvádí hash artefaktu, verzi OS, verze z kroků a výsledek kontroly `minos` a importů. Na systému, který majitel nedodal, je výsledek **neověřeno** a P1 bránou neprojde. Tvrdit, že balík na systému běží, jde jen s tímto záznamem.
  - **Závisí na:** P1.1.1, P1.1.6
  - **Odhad:** bez odhadu; ocení PR.8.1

### P1.2 Dokument, revize a změnová služba

Společný model, na kterém stojí hledání, náhrady, recepty, MCP i pluginy. Plán ho vyžaduje už v P1 (kap. 5.1).

- [x] **P1.2.1** Bajtový kontrakt dokumentu nad dvěma backendy
  - **Co a proč:** Jedno rozhraní pro AvaloniaEdit (menší soubory) a stránkovaný `PagedDocument` (velké soubory). Autoritou jsou původní bajty souboru a strom úseků nad nimi; text je pohled přes mapu znaků s hranicemi znaků, ne druhá úplná kopie. Operace pracují s identitou dokumentu, revizí a bajtovými rozsahy `[od, do)`. Převod na řádek a sloupec UTF-16 dělá index dokumentu. Přechod mezi backendy proběhne v paměti nad původními bajty, ne příkazem Uložit. Nikdo nesmí volat `.Text` stránkovaného dokumentu.
  - **Funkce:** F042
  - **Kroky:**
    - rozhraní: čtení rozsahu bajtů i textu, vložení, smazání, převod pozic, stav indexu
    - testovací data a sada testů kontraktu (vložit, smazat, undo, převod pozic) s očekávanými bajty, nezávislá na backendu
    - analyzátor nebo test, který zakáže `.Text` u stránkovaného backendu
  - **Stav 26. 10. 2026:** hotovo (commity 25426e9–bc2694e): kontrakt `IByteBackend` a společná sada s očekávanými bajty; referenční backend projde; `.Text` u `PagedDocument` a `PagedBackend` je `[Obsolete(error: true)]`, takže volání neprojde překladem (místo analyzátoru), test to ověří reflexí.
  - **Hotovo, když:** rozhraní a sada testů kontraktu jsou v repozitáři; referenční implementace v testech sadou projde; analyzátor `.Text` u stránkovaného backendu odmítne. Přijetí nad oběma backendy je P1.2.10.
  - **Závisí na:** P0.1.1
  - **Odhad:** bez odhadu
- [x] **P1.2.10** Společné přijetí kontraktu nad oběma backendy
  - **Co a proč:** Integrační přijetí po adaptérech. Kontrakt, adaptéry a přijetí jdou v tomto pořadí, aby nevznikly dvě různé smlouvy.
  - **Funkce:** F042 (částečně: integrační přijetí)
  - **Stav 26. 10. 2026:** hotovo: stejná sada běží nad referenčním, stránkovaným a AvaloniaEdit backendem; šest semínek po 3000 krocích porovná všechny tři s naivním bajtovým modelem; nepojmenovaný dokument s BOM, smíšenými EOL a vadným UTF-8 přejde mezi backendy v paměti se zachovanou identitou, revizí, stavem neuloženo, undo i bajty.
  - **Hotovo, když:** stejná sada testů operací z P1.2.1 projde nad oběma backendy a nad stejnými daty dá stejné bajty; nepojmenovaný neuložený dokument s BOM, smíšenými EOL a vadným UTF-8 přejde mezi backendy tam a zpět, nic se nezapíše na disk a zachová se identita, revize, stav „neuloženo“, undo i neplatné bajty.
  - **Závisí na:** P1.2.7, P1.2.8
  - **Odhad:** bez odhadu
- [x] **P1.2.7** Adaptér AvaloniaEdit nad bajtovým kontraktem
  - **Co a proč:** AvaloniaEdit drží text s offsety `int`. Adaptér vede mapu mezi jeho offsety a bajty původního souboru, aby nezměněné úseky zůstaly bajtově stejné (kap. 5.1).
  - **Funkce:** F042 (částečně: adaptér běžného editoru)
  - **Stav 26. 10. 2026:** hotovo: psaní překóduje jen napsaný text; nezměněný round trip i změna jednoho znaku jsou bajtově přesné. Známá vada: smazání, které spojí dva neplatné úseky, dál ukazuje dva náhradní znaky; bajty zůstanou správně.
  - **Hotovo, když:** po vložení a smazání znaků s vícebajtovým kódováním, CRLF a neplatnou sekvencí převede adaptér každý offset na stejný bajt jako referenční výpočet; čtení nezměněného rozsahu vrátí původní bajty.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu
- [x] **P1.2.8** Adaptér stránkovaného zdroje
  - **Co a proč:** Adaptér `PagedDocument` s pozicemi `long` nad stejným kontraktem. Na něm stojí stránkovaná plocha (P3.1), hex (P3.5) a stránkovaný UTF-16 (P3.1.2). Patří sem i počty řádků po úpravě: každý uzel stromu úseků nese počet konců řádků a CRLF rozdělené mezi dva úseky se odečte. Jádro to umí od commitu `f7defd7`. Dřív to byla P7.1.1; v P7 zůstává ověření nad 10 GiB.
  - **Funkce:** F042 (částečně: adaptér stránkovaného dokumentu), F154 (částečně: čísla řádků po úpravě)
  - **Stav 26. 10. 2026:** hotovo: celá sada kontraktu (65 testů) běží nad řídkým souborem 4,5 GiB a projde za 37 s; náhodné úpravy s CR, LF a CRLF na hranicích 2^31 a 2^32 souhlasí s naivním průchodem i po undo; dotaz před dokončením indexu vrátí „neznámé“ (test `LineQueriesAreUnknownUntilTheOriginalIsIndexed`). Stránkovaný UTF-16 zůstává P3.1.2.
  - **Hotovo, když:** sada testů kontraktu z P1.2.1 projde nad souborem větším než 4 GiB a pozice za hranicí `int` se převádějí bez přetečení; po náhodných úpravách s CR, LF a CRLF, včetně CRLF rozděleného mezi úseky, souhlasí počet a začátky řádků s naivním průchodem, i nad souborem větším než 4 GiB; dokud index originálu nedoběhl, vrátí dotaz na začátek řádku po úpravě „neznámé“, ne odhad.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu
- [x] **P1.2.2** Hranice přepnutí backendu a schopnosti režimů
  - **Co a proč:** AvaloniaEdit stojí u 100 MiB +552 MiB paměti na vývojovém PC a +501 MiB na referenční Windows VM a nad ~2,1 mld. znaků nefunguje. Hranici přepnutí nastavit měřením, výchozí odhad 16–32 MiB. Každý režim má popsané schopnosti a limity místo pevných malých limitů PSPadu. Hranice nesmí z textu 100 MiB udělat soubor jen pro čtení: MVP slibuje jeho editaci. P1.2.9 zvolila editovatelný stránkovaný režim nad `PagedDocument`; P1 ho doměří na prototypu s UI a produktovou editaci 100 MiB přijímá až P3.1.10.
  - **Funkce:** F141
  - **Stav 26. 10. 2026:** hotovo: hranice 6 MiB změřená na vývojovém PC, referenční Windows VM a referenční Mac (`p1/paged-edit-measure.md`): AvaloniaEdit drží špičku paměti při psaní pod 350 MiB jen do 6 MiB na všech třech strojích (259 / 275 / 191 MiB), u 8 MiB ji referenční Windows VM překročí (358 MiB); dřívější odhad 16–32 MiB vycházel z ustálené paměti, ne ze špičky. Nastavení `files.largeFileThreshold` se zdrojem hodnoty; soubor nad hranicí se otevře v editovatelné stránkované ploše, ne jen pro čtení.
  - **Hotovo, když:** hranice je určená měřením paměti a latence na obou OS a je v nastavení se zdrojem hodnoty; soubor nad hranicí se otevře v editovatelném stránkovaném režimu, ne jen pro čtení, a na prototypu jde text 100 MiB editovat podle záznamu P1.2.9.
  - **Závisí na:** P1.2.10, P1.2.9
  - **Odhad:** bez odhadu
- [ ] **P1.2.9** Technická cesta pro editaci 100 MiB v MVP
  - **Co a proč:** MVP slibuje u textu 100 MiB první editaci ≤ 2 s warm / 4 s cold a přírůstek paměti ≤ 350 MiB. Spike z 26. 10. 2026 cestu vybral: text nad hranicí přepnutí (P1.2.2) se otevře v editovatelném stránkovaném režimu nad `PagedDocument`. Úpravy, undo a add buffer jsou v paměti, originál zůstává namapovaný a ukládá se streamem. Na referenční Windows VM se 4 jádry splnil dokument bez vykreslení oba cíle současně: první editace warm p95 35,8 ms (ASCII) a 39,2 ms (UTF-8), cold p95 457 a 449 ms včetně JIT, nejvýš +1,2 MiB private po 5 000 úpravách, výpočet za úpravou p95 0,80 ms. Uložený soubor má stejný SHA-256 jako nezávislý výstup AvaloniaEdit; počty i začátky řádků po 5 000 úpravách a 500 undo a redo souhlasí s úplným průchodem. AvaloniaEdit `TextDocument` paměť nesplnil ani při načtení po částech (+450 až +528 MiB). Spike měřil jen jádro, v session 0 jako SYSTEM bez plochy, a cold uvnitř VM s neznámou cache hostitele. Vykreslení v Avalonii, vstup z klávesnice, IME, přístupnost, obnovu po pádu ani referenční Mac neměřil. Vyřešená je dokumentová vrstva, textová plocha s editací **neověřená**.
  - **Funkce:** F141 (částečně: rozhodnutí o režimu pro 100 MiB)
  - **Kroky:**
    - hotovo ve spiku: popis variant, měření jádra a AvaloniaEdit nad čistým ASCII i UTF-8 souborem 100 MiB, volba cesty, rozsah a závislosti P3.1.10 a přesun částí P7 do MVP (P1.2.8, P3.1.5, P3.1.11–P3.1.13, P3.5.9)
    - hotovo 26. 10. 2026: `PagedTextView` nad `PagedBackend` s mapou znaků, kurzorem, psaním, mazáním, Enter, Tab, šipkami, myší, undo a redo, revize ve stavovém řádku, uložení; měřicí režim `TEXTIDA_MEASURE` v aplikaci. 100 MiB ASCII i UTF-8, 10 běhů, AOT v přihlášené relaci: první editace vykreslená warm 70–82 ms (vývojové PC), 612–662 ms (referenční Windows VM), 168–235 ms (referenční Mac); cold 1021–1135 ms (referenční Windows VM), 207–258 ms (referenční Mac); špička paměti nad prázdnou aplikací +118 až +123 / +50 až +51 / +37 až +39 MiB; uložený soubor má očekávanou délku a SHA-256. AvaloniaEdit na 100 MiB: otevření 1,9–4,7 s, špička 1,9–6,0 GiB. Opraveno při tom: `Document.SaveAs` ve stejném kódování už nedekóduje celý stránkovaný dokument.
    - změřit ho v přihlášené relaci na referenční Windows VM i referenční Mac; paměť proti stabilizované prázdné aplikaci, se špičkou, okamžikem měření, backendem a stavem indexace
    - zbývá: živé IME a čtečka nad upraveným řádkem (poskytovatel přístupnosti zatím na stránkovanou plochu nenapojený), obnova po skutečném zabití AOT procesu, vstup přes klávesnici systému na referenční Mac (chybí oprávnění pro `CGEventPost`), latence do prezentace na referenční Windows VM (odpojená relace tiká po ~38 ms); uložení 100 MiB na referenční Windows VM trvá 2 s a blokuje UI
    - cenu P3.1.10–P3.1.13 spike nedal; určí ji PR.8.2 před zahájením P3
  - **Hotovo, když:** na prototypu zvolené cesty s UI v přihlášené relaci na referenční Windows VM i referenční Mac se čistý ASCII fixture 100 MiB i UTF-8 log 100 MiB otevře a první úprava z klávesnice je vykreslená do 2 s warm / 4 s cold; přírůstek private commit proti stabilizované prázdné aplikaci je u ASCII ≤ 350 MiB; skládaný znak ze scénáře IME v P1.8.2 se dopíše bez zdvojení i ztráty a NVDA na referenční Windows VM i VoiceOver na referenční Mac přečtou upravený řádek; po zabití procesu se úprava vrátí podle P1.5.1 a soubor jde uložit. Když cesta některý z těchto cílů nesplní, bod zůstává **otevřený** a P1 neprojde bránou. Předložení rozporu majiteli je eskalace, ne splnění; příslib 100 MiB platí, dokud ho majitel výslovně nezmění v zadání.
  - **Závisí na:** P1.2.7, P1.2.8, PR.2.1, P1.5.1, P1.8.2
  - **Odhad:** bez odhadu; cenu pokusu určí PR.8.1
- [x] **P1.2.3** Identita, revize a otisk podkladu
  - **Co a proč:** Každý dokument má revizi (číslo, které roste s každou změnou) a otisk podkladu na disku (velikost, čas, otisk). Změna souboru jiným programem se pozná a nikdy se tiše nepřepíše. Tady vzniká služba detekce; nabídku v UI (porovnat, znovu načíst, ponechat) přijímá P1.4.8, aby kritéria nebyla kruhová.
  - **Funkce:** F140, F006, F032
  - **Stav 26. 10. 2026:** hotovo: testy pokrývají událost konfliktu s revizí a otiskem, stejnou velikost a čas (pozná hash), přejmenování, zkrácení, smazání i změnu mezi kontrolou a záměnou; dotyk se stejným obsahem konflikt není. Otisk počítá SHA-256 celého souboru při otevření.
  - **Hotovo, když:** změna souboru jiným procesem při otevřené upravené kartě vyvolá událost konfliktu s revizí a otiskem; řízená změna podkladu před kontrolou i mezi kontrolou a zápisem, se stejnou velikostí a časem, přejmenováním i zkrácením, skončí konfliktem, nikdy tichým přepsáním.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu
- [x] **P1.2.4** Změnová služba, záznam změn a snapshoty
  - **Co a proč:** Jedna služba aplikuje úpravy v transakci a vede záznam změn mezi revizemi. Výsledek hledání, návrh náhrady, recept i požadavek z MCP nebo pluginu nese svou revizi; nad novější revizí se rozsahy přepočítají. Neměnný snapshot revize slouží čtení na pozadí. Záznam změn má určenou dobu platnosti; po jeho zkrácení se starý rozsah nepřepočítá odhadem, ale vrátí konflikt. Na této službě stojí P2.5.2, F178 i F199.
  - **Funkce:** žádná (infrastruktura, kap. 5.1)
  - **Stav 26. 10. 2026:** hotovo: přepočet rozsahů přes záznam změn; překryv, zkrácený záznam, budoucí revize a dva zdroje nad stejnou revizí vrátí konflikt; snapshot čte stejné bajty i po úpravách a po uložení na místo.
  - **Hotovo, když:** rozsah z revize N se přes záznam změn přepočte na revizi N+k; při překryvu se změnou uživatele, po zkrácení záznamu a při souběžné změně ze dvou zdrojů služba vrátí konflikt; snapshot revize N vrací stejné bajty i po dalších úpravách; vše pokrývají testy.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu
- [x] **P1.2.5** Kotvy pozic
  - **Co a proč:** Kotva je pozice, která se posouvá se změnami dokumentu. Potřebují ji výběry víc kurzorů (P0 je měl jako holá čísla), záložky, nedávná místa úprav a výsledky hledání. měření je řadí do P1.
  - **Funkce:** žádná (infrastruktura)
  - **Stav 26. 10. 2026:** hotovo: tabulkový test a náhodný test proti referenci projdou; smazaná kotva zůstane neplatná i po undo, nic se neodhaduje.
  - **Hotovo, když:** tabulkové testy pokrývají vložení přesně na kotvu, smazání kotvy, překryv, undo a redo, ztracenou historii a nahrazení celého souboru; u nejednoznačného případu se kotva zneplatní nebo vrátí konflikt, pozice se neodhaduje; náhodný test proti referenčnímu výpočtu prochází.
  - **Závisí na:** P1.2.4
  - **Odhad:** bez odhadu
- [x] **P1.2.6** Undo a redo jako transakce
  - **Co a proč:** Undo sjednocuje všechny zdroje úprav. Skupiny undo (psaní v jednom úseku, operace nad výběrem) a správný stav „uloženo“. Undo obnoví všechny kurzory, ne jen primární.
  - **Funkce:** F043
  - **Stav 26. 10. 2026:** hotovo: undo po uložení vrátí přesně uložený stav a značka neuloženo zmizí; seskupení, slučování a psaní do více kurzorů jedním krokem undo včetně obnovy výběrů mají testy.
  - **Hotovo, když:** po uložení a dalších úpravách undo vrátí přesně uložený stav a značka „neuloženo“ zmizí; jeden krok undo vrátí psaní do všech kurzorů včetně jejich pozic.
  - **Závisí na:** P1.2.4, P1.2.5
  - **Odhad:** bez odhadu

### P1.3 Kódování a konce řádků

Bajtová věrnost je hlavní slib produktu (kap. 5.1) a mezera na trhu. Chování PSPadu je reference v rozboru PSPadu; jeho chyby se neopakují.

- [x] **P1.3.1** Detekce kódování
  - **Co a proč:** Pořadí BOM → sonda UTF-8 nad celým vzorkem → statistický odhad 8bitové stránky → výchozí kódování (`10` kap. 1, 2, 10). Statistika má vlastní tabulky, tabulky z binárky PSPadu se nepřebírají. Nejistota se přizná a nabídne volbu. Rozšíří se `EncodingDetector` z P0 o Kamenický a UTF-32 (`10` kap. 10 bod 6). Detekce sama není podpora: čtení a zápis UTF-32 řeší P1.3.2 a P3.1.2.
  - **Funkce:** F014
  - **Stav 26. 10. 2026:** služba hotová v `NksHub.Textida.Core.Encodings` (`EncodingGuesser.Guess`, commity 3c81d47–166a380): fixture s BOM a jednoznačné fixture (UTF-8/16/32 s BOM i bez, CP1250, CP1252, ISO-8859-2, Kamenický podle veřejné tabulky) mají jistotu ≥ 0,8, `café` v CP1252 vrací nejistotu s alternativami, prázdný soubor UTF-8. Zbývá zobrazení nejistoty a ruční volba ve stavovém řádku (P1.4.6). Neumí: UTF-16 bez BOM s CJK textem.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** soubory s BOM a vlastní jednoznačné fixture mají přesně určený výsledek a dostanou ho; nejednoznačný vzorek má v testu očekávanou nejistotu, stavový řádek ji ukáže a nabídne ruční volbu; prázdný soubor dostane určené výchozí kódování.
  - **Závisí na:** P1.2.1, PR.2.1
  - **Odhad:** bez odhadu
- [x] **P1.3.2** Bajtově věrné čtení a zápis
  - **Co a proč:** Nezměněné úseky se uloží tak, jak byly, včetně BOM, neplatných sekvencí a znaků mimo cílové kódování. Překódují se jen upravené rozsahy. Neplatná sekvence se ukáže jako náhradní znak, v souboru zůstanou původní bajty. `MappedFileSource` z P0 UTF-16 a UTF-32 odmítá; běžný backend je musí umět už v P1. Stránkovaný zdroj UTF-16 a UTF-32 vzniká v P3.1.2 a stejnou sadou projde tam; oba backendy musí sadou projít před bránou MVP (P3.7.5).
  - **Funkce:** F013, F163
  - **Stav 26. 10. 2026:** služba `ByteFidelity`/`SavePlan` hotová pro paměťový model: všechny fixture se uloží beze změny a změna jednoho znaku dá rozdíl jen v jeho bajtech (UTF-8, CP1250, UTF-16/32 LE/BE s BOM i bez, smíšené EOL, poškozené UTF-8, lichý konec UTF-16/32), dekódování po blocích 4–41 B se shoduje. Zbývá napojení na kontrakt dokumentu (P1.2.10) a stránkovaný backend.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** projde test brány z kap. 5.1: otevřít → změnit jeden znak → uložit → binární rozdíl obsahuje jen ten rozsah; pro UTF-8, CP1250, UTF-16 LE/BE, UTF-32 LE/BE s BOM i bez, smíšené EOL, poškozené UTF-8 a lichý nebo neúplný konec UTF-16 a UTF-32 v běžném backendu; ve stránkovaném backendu pro UTF-8, CP1250, smíšené EOL a poškozené UTF-8.
  - **Závisí na:** P1.2.10
  - **Odhad:** F163 3–5 ČT (matice), společně s P1.3.4; F013 bez odhadu
- [x] **P1.3.3** „Znovu přečíst jako…“ a „Převést na…“
  - **Co a proč:** Menu kódování ve stavovém řádku má dvě oddělené akce: znovu přečíst (soubor se nemění) a převést (soubor se přepíše). Kódování, ve kterém by se ztratily znaky, je v seznamu označené předem. Malý náhled ztrát a změněných bajtů vzniká už tady; P2.5.2 a P3.4.1 ho rozšíří, nevytvoří druhý.
  - **Funkce:** F015
  - **Stav 26. 10. 2026:** logika hotová a otestovaná: znovu přečíst nemění bajty, převod `řč😀` do CP1250 bez náhrady odmítne a hlásí jen 😀, `řč` → `f8 e8`, ztrátová kódování jsou označená předem (`ByteFidelity.ListTargets`). Zbývá menu ve stavovém řádku (P1.4.6) a revize podkladu z P1.2.3.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** „Znovu přečíst jako CP1250“ nezmění bajty na disku ani revizi podkladu; „Převést na CP1250“ u textu s `ř`, `č` a `😀` označí v seznamu ztrátu předem, v náhledu ukáže jen `😀` jako nezapsatelný znak a bez potvrzené náhrady převod odmítne; `ř` a `č` projdou bez ztráty (`řč` v CP1250 je `f8 e8`).
  - **Závisí na:** P1.3.2, P1.4.6
  - **Odhad:** 0,5 ČT v rámci F015
- [x] **P1.3.4** Varování před ztrátou znaků
  - **Co a proč:** PSPad ukládá do 8bitové stránky ztrátově a potichu (`10` kap. 4.5). Tady přesný převod bez best-fit a dialog se seznamem znaků, řádků a bajtů. Dialog používá stejný malý náhled jako P1.3.3. Plný inspektor F169 přijde v P3 a P5.
  - **Funkce:** F163
  - **Stav 26. 10. 2026:** `LossReport` s řádkem, sloupcem a bajtovou pozicí každé ztráty hotový; zablokované uložení nic nezapíše, výslovná náhrada zapíše přesné bajty, best-fit se nepoužívá. Zbývá samotný dialog v aplikaci.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** uložení textu s „č“ do CP1252, která ho nemá, otevře dialog se znaky, řádky a bajtovými pozicemi; zrušení nic nezapíše; výslovná náhrada zapíše přesně určené bajty; tichý best-fit nenastane nikdy.
  - **Závisí na:** P1.3.2
  - **Odhad:** viz P1.3.2
- [x] **P1.3.5** Konce řádků a normalizace jen výslovně
  - **Co a proč:** Detekce CR, LF, CRLF; smíšené konce řádků zůstanou. Sjednocení konců řádků a normalizace Unicode (NFC, NFD) jsou jen výslovné akce s náhledem.
  - **Funkce:** F075
  - **Stav 26. 10. 2026:** `EolScanner`, sjednocení na CR/LF/CRLF s přesnými počty a NFC/NFD (`UnicodeNormalizer`) hotové s vlastními testy, smíšené EOL zůstanou bajtově. Zbývá undo přes dokument z P1.2.6 a stav „smíšené“ ve stavovém řádku.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** soubor se smíšenými konci po úpravě jednoho řádku a uložení zachová všechny ostatní konce řádků bajtově a stavový řádek ukáže „smíšené“; výslovné sjednocení na CR, LF i CRLF a převod `e` + U+0301 ↔ `é` mají každý vlastní test, náhled přesně vyčíslí změněné rozsahy a undo vrátí původní bajty.
  - **Závisí na:** P1.3.2
  - **Odhad:** bez odhadu

### P1.4 Editace, soubory a karty

Běžná práce s textem nad AvaloniaEdit. Chování editoru vychází z rozboru PSPadu–11 jako reference.

- [x] **P1.4.1** Textová plocha pro běžné soubory
  - **Co a proč:** AvaloniaEdit za úzkým rozhraním z P1.2.1. Psaní, myš, výběry, posun, schránka a mazání bez nechtěné změny formátu, drop souborů a přesun výběru tažením.
  - **Funkce:** F042, F020, F044, F139
  - **Nález review D5 (1. kolo):** plocha je zatím `TextBox`: chybí čísla řádků a v tmavém tématu text působí tučně. Nahradit AvaloniaEdit nad kontraktem dokumentu a znovu porovnat s `D5-klidny/01-hlavni-okno.png` a `07-tmave.png`.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** psaní v souboru 1 MiB bez zvýraznění má p95 ≤ 33 ms (headless i na displeji, PR.2.3); přetažený soubor se otevře v nové kartě; kopie a vložení zachová konce řádků podle cíle.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu
- [x] **P1.4.2** Typy výběru a blokový výběr
  - **Co a proč:** Normální, řádkový a sloupcový výběr; blokový výběr klávesou F8. Vkládání do sloupcového výběru patří sem jako základ speciálního vložení F045.
  - **Funkce:** F053, F054, F045 (částečně: vložení do sloupce)
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** Alt+tažení a F8 vytvoří obdélníkový výběr; psaní a vložení víceřádkové schránky do sloupce vloží řádek do každého řádku výběru.
  - **Závisí na:** P1.4.1
  - **Odhad:** bez odhadu
- [x] **P1.4.3** Řádkové operace, velikost písmen a párové znaky
  - **Co a proč:** Přesun, duplikace, odstranění a spojení řádků; změna velikosti písmen nad výběry; doplnění párových znaků a pokračování komentáře. Reference `08` kap. 2 a 5. Chytré klávesy pro web jsou v P2.2.2.
  - **Funkce:** F046, F070, F107
  - **Před zahájením:** seznam ID příkazů a voleb profilu se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** pro každý příkaz ze seznamu ID v testu (přesun, duplikace, odstranění a spojení řádků, změna velikosti písmen, párové znaky, pokračování komentáře) existuje vstup, očekávaný výstup a undo; každý příkaz se v jádře ověří nad seznamem více rozsahů jako jeden krok undo; tabulkové testy z `08` kap. 2 a 5 procházejí v rozsahu, který profil editoru přebírá; nový příkaz bez testovacího případu bránou neprojde. Interaktivní test více kurzorů je v P2.2.1.
  - **Závisí na:** P1.4.1, P1.2.6
  - **Odhad:** bez odhadu
- [x] **P1.4.4** Nový, otevřít, uložit, zavřít, znovu načíst
  - **Co a proč:** Prázdný dokument okamžitě; otevření vybere backend podle velikosti; obnovitelné uložení s kontrolou podkladu; zavření s obnovou a srozumitelným zahozením; režim jen pro čtení. Potvrzování podle dopadu, ne při běžném psaní.
  - **Funkce:** F029, F030, F034, F035, F037, F108
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Oprava 26. 10. 2026:** náhodné zabití v testu dávky ukázalo, že Windows `ReplaceFile` napřed přejmenuje cíl a pak přesune nový soubor; pád mezi tím nechal cíl chybět a původní obsah pod jménem `~RF….TMP`. Přepis teď parkuje original pod známým jménem `*.textida-*.old.tmp`; otevření souboru ho vrátí, když cíl chybí, a zbylé kopie smaže (`FileSwap.RecoverAbandoned`, test). Zabití přímo uvnitř `ReplaceFile` u běžného uložení harness zatím nezkouší.
  - **Rozhodnutí majitele 26. 10. 2026 (velké mapované soubory):** soubor se nezamyká; změna zvenku se pozná podle délky a času a před uložením se obsah nezměněných částí originálu ověří proti blokovým otiskům (SHA-256 po 1 MiB, pořízeným při čtení, bez druhého celého průchodu); při rozdílu konflikt místo uložení. Čtení zastaralého snímku vyhodí `SnapshotInvalidatedException` (hotovo, Codex fa4521e). Blokové otisky hotové (`OriginalBlockHashes`, 1 MiB SHA-256 při počátečním čtení, kontrola před uložením na místě; cílené testy 46/46 na referenční Windows VM); varianta nad 4 GiB čeká na celý běh. Zbývající hranice: souběžný zápis jiného programu mezi kontrolou a naším zápisem bez zámku nejde atomicky vyloučit (XML docs).
  - **Hotovo, když:** pád injektovaný před a po flush a před a po výměně souboru nechá na disku buď starou, nebo novou verzi celou, ověřeno nezávislým porovnáním; plný disk při uložení nic nepoškodí a neuložený obsah zůstane v kartě; zavření neuložené karty nabídne uložit, zahodit nebo nechat v žurnálu; v režimu jen pro čtení nejde změnit dokument žádnou cestou.
  - **Závisí na:** P1.3.2, P1.2.3
  - **Odhad:** bez odhadu
- [x] **P1.4.7** Externí nástroj pro porovnání
  - **Co a proč:** Než vznikne vlastní diff (P5.2.1), slouží externí diff jako první cesta. Potřebuje ho nabídka „porovnat“ u změny zvenku (P1.4.8). Neuložený obsah se předá přes dočasný soubor. Úkol nahrazuje dřívější P3.6.4.
  - **Funkce:** F082
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** porovnání neuložené karty s diskem otevře nastavený externí nástroj se dvěma soubory a dočasný soubor se po zavření nástroje smaže; bez nastaveného nástroje příkaz řekne, co chybí.
  - **Závisí na:** P1.4.4
  - **Odhad:** bez odhadu
- [x] **P1.4.8** Nabídka při změně souboru zvenku
  - **Co a proč:** UI nad službou detekce z P1.2.3. Upravená karta, jejíž soubor změnil jiný program, nabídne porovnat externím nástrojem (P1.4.7), znovu načíst, nebo ponechat. Reload chrání rozpracované změny. Oddělení od P1.2.3 ruší kruh P1.2.3 → P1.4.7 → P1.4.4 → P1.2.3 v kritériích.
  - **Funkce:** F140 (částečně: nabídka v UI)
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** změna souboru jiným procesem při otevřené upravené kartě ukáže nabídku porovnat, znovu načíst nebo ponechat; „porovnat“ otevře externí nástroj; „znovu načíst“ se u neuložených změn zeptá a bez potvrzení je nezahodí; „ponechat“ nechá kartu a příští uložení skončí konfliktem podle P1.2.3.
  - **Závisí na:** P1.2.3, P1.4.7
  - **Odhad:** bez odhadu
- [x] **P1.4.5** Karty, skupiny a rozdělení okna
  - **Co a proč:** Karty a skupiny karet místo MDI; kontextové menu karty jen s dostupnými akcemi. Rozdělení okna na pohledy jednoho dokumentu. Skupiny a rozdělení nahrazují Cascade a Tile.
  - **Funkce:** F004, F005, F131, F132
  - **Nález review D5 (1. kolo):** karta neukazuje tečku neuložené změny ani štítek velikosti u velkého souboru.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** dva pohledy jednoho dokumentu ukazují stejnou revizi a sdílí undo; kartu jde přetáhnout do jiné skupiny; karty mají roli a fokus z klávesnice.
  - **Závisí na:** P1.4.1
  - **Odhad:** bez odhadu
- [x] **P1.4.6** Stavový řádek
  - **Co a proč:** Vlevo pozice a výběr, vpravo klikací `Mezery: 4 · UTF-8 · CRLF · jazyk` (D5). Nedokončený index řádků se označí.
  - **Funkce:** F017
  - **Nález review D5 (1. kolo):** stavový řádek má ukazovat vlevo „Ř 21, Sl 16 · Připraveno“ a vpravo „Mezery: 4 · UTF-8 · CRLF · HTML“; dnes je kódování vlevo, chybí konec řádku, odsazení a jazyk a vpravo je „3 otevřené karty“, které D5 nemá.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** klik na kódování, EOL a jazyk otevře volbu; u neúplného indexu je číslo řádku označené „≈“.
  - **Závisí na:** P1.1.3
  - **Odhad:** bez odhadu

### P1.5 Žurnál, hot exit a zálohy

Neuložená práce přežije restart i pád. Žurnál je oddělený od automatického ukládání do zdrojového souboru.

- [x] **P1.5.1** Žurnál relace: pády a limit ztráty
  - **Co a proč:** Společný žurnál relace a snapshoty pro každý backend. Nepojmenovaný i změněný dokument přežije restart a pád. Ukládá se i stav pracovní plochy: karty a jejich skutečný neuložený obsah. Položka spojuje části P1.5.3–P1.5.5 a ověřuje celek.
  - **Funkce:** F150, F027
  - **Stav 26. 10. 2026:** jádro hotové (commity 9832275–1e21479): harness `tests/NksHub.Textida.Journal.CrashHarness` zabíjí skutečný proces v náhodném okamžiku a porovnává s orákulem; 3 běhy (200, 300 a 200 iterací, celkem přes 53 000 operací) bez chyby, největší ztráta 920 ms při limitu 2000 ms, 371 useknutých nebo poškozených konců žurnálu rozpoznáno. Zbývá lišta „co se vrátilo“ v aplikaci.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Nález 26. 10. 2026 (referenční Windows VM):** test `BatchedRecordsAreOnDiskWithinTheFlushIntervalAndWellInsideTwoSeconds` (Perf, samostatný běh projektu) dal 2565 ms a jednou 1702 ms proti limitu 1500 ms; na vývojovém PC prošel. Na referenčním stroji se tedy záznam žurnálu může dostat na disk až za 2,5 s – prověřit (fsync po 500 ms vs. výkon disku VM) a buď opravit, nebo doložit, že limit ztráty z P1.5.1 platí.
  - **Hotovo, když:** zabití procesu během psaní a nový start obnoví všechny karty včetně nepojmenovaných se ztrátou nejvýš 2 s žurnálu (kap. 3), změřeno podle časových značek žurnálu; pád během zápisu žurnálu nepoškodí předchozí potvrzený stav; po obnově lišta ukáže, co se vrátilo.
  - **Závisí na:** P1.5.3, P1.5.4, P1.5.5
  - **Odhad:** F150 4–6 ČT (matice) za P1.5.1 a P1.5.3–P1.5.5; F027 bez odhadu
- [x] **P1.5.3** Formát žurnálu
  - **Co a proč:** Verzovaný formát žurnálu s kontrolním součtem záznamu a body potvrzení. Stejný žurnál použije uložení hexu na místo (P3.5.5), dávka náhrad (P2.5.6) a revize změn F178.
  - **Funkce:** F150 (částečně: formát)
  - **Stav 26. 10. 2026:** hotovo: verzovaný formát s CRC na záznam, zápis po dávkách s fsync každých 500 ms (interval nad 1 s se odmítne), bezpečná komprese; uříznutí na každém bajtu a otočení libovolného bitu vrátí právě předchozí záznamy; rozložení 2 (kodek podle `TextCodec.Name`) čte i rozložení 1 migrací. Popis v `src/NksHub.Textida.Core/Journal/FORMAT.md`.
  - **Hotovo, když:** useknutý nebo poškozený poslední záznam se při čtení pozná a zahodí bez ztráty předchozích; starší verze formátu se přečte testovanou migrací.
  - **Závisí na:** P1.2.4
  - **Odhad:** v rámci F150 (viz P1.5.1)
- [x] **P1.5.4** Zápis a obnova dokumentu
  - **Co a proč:** Změny dokumentu jdou do žurnálu průběžně, velké vložené bloky jako odkaz na snapshot. Obnova postaví dokument nad stejnými původními bajty a revizí podkladu.
  - **Funkce:** F150 (částečně: dokument)
  - **Stav 26. 10. 2026:** hotovo: obnova vrátí bajty, revizi, kroky undo a stav neuloženo pojmenovaných i nepojmenovaných dokumentů včetně převodů kódování; změněný nebo chybějící soubor dá Conflict nebo Missing a nic se nepoužije ani nezapíše; 20MiB stránkovaný soubor má žurnál pod 4 KB. Undo z doby před uložením se neobnovuje.
  - **Hotovo, když:** obnovený dokument má stejné bajty, revizi, undo a stav „neuloženo“ jako před pádem; když se podklad na disku mezitím změnil, obnova ohlásí konflikt a nic nepřepíše.
  - **Závisí na:** P1.5.3, P1.4.4
  - **Odhad:** v rámci F150 (viz P1.5.1)
- [x] **P1.5.5** Relace a nepojmenované karty
  - **Co a proč:** Seznam karet, skupin a pořadí, nepojmenované dokumenty a jejich obsah.
  - **Funkce:** F027
  - **Stav 26. 10. 2026:** model relace hotový: skupiny, pořadí karet, aktivní karta, nepojmenované dokumenty a výběry přes kotvy se uloží a obnoví; čtou se jen soubory `session-*.njl`. Zbývá napojit obnovu na aplikaci.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** po restartu jsou karty ve stejných skupinách a pořadí a nepojmenované dokumenty mají celý obsah.
  - **Závisí na:** P1.5.3, P1.4.5
  - **Odhad:** v rámci F150 (viz P1.5.1)
- [x] **P1.5.2** Zálohy a automatické ukládání odděleně
  - **Co a proč:** Autosave do zdrojového souboru je volba uživatele a je oddělený od žurnálu obnovy. Cesty záloh a žurnálu mají kontrolu dostupnosti. Náhradní místo žurnálu se určí ve schématu nastavení (P1.7.1), aby ho test znal předem.
  - **Funkce:** F110, F109
  - **Stav 26. 10. 2026:** záloha při uložení (centrální složka nebo vedle souboru) a autosave ve výchozím stavu vypnutý; žurnál přejde na náhradní složku a ohlásí, když nejde zapsat nikam; test potvrdí, že žurnál nikdy nezapíše soubor uživatele. Zbývá hlášení v aplikaci.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** s vypnutým autosave se zdrojový soubor nikdy nezmění bez uložení; nedostupná primární cesta se ohlásí při startu a žurnál přejde na náhradní místo; když nejde ani náhradní místo, aplikace to řekne a neslibuje obnovu po pádu, neuložený obsah zůstane v otevřené kartě.
  - **Závisí na:** P1.5.1
  - **Odhad:** bez odhadu

### P1.6 Registr příkazů a klávesy

Registr příkazů je společný základ palety, kláves, maker a pluginů. Hranice rozšíření se nedá doplnit později bez přestavby.

- [x] **P1.6.1** Registr příkazů s vnitřní hranicí rozšíření
  - **Co a proč:** Příkazy mají ID s prefixem `nks.`, kontexty `when` (jazyk, výběr, velký režim, platforma) a registrují se z manifestu stejného tvaru, jaký později použijí pluginy. Bez veřejného API. Stará jména akcí PSPadu jen jako aliasy (až s importem).
  - **Funkce:** F098, F195
  - **Stav 26. 10. 2026:** registr hotový v knihovně Workbench: všech 37 vestavěných příkazů, menu, kláves a nastavení se načte z vloženého manifestu `builtin.contributions.json` a test ověří čistý průchod; `when` má test pro každý kontextový klíč; konflikty ID, aliasů, dvojích obsluh a ID pluginu s prefixem `nks.` se odhalí; schémata kontroluje test. Zbývá napojit paletu, menu a klávesy aplikace jen na registr (P1.1.1).
  - **Nález review D5 (1. kolo):** menu aplikace má jen Soubor, Úpravy, Zobrazit, Nápověda; D5 má Soubor, Projekty, Úpravy, Hledat, Zobrazit, Formát, Nástroje, HTML, Nastavení, Okno, Nápověda. Menu se má sestavit z registru a manifestu.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** všechny vestavěné příkazy se načtou z manifestu; paleta, menu a klávesy čtou jen registr; test ověří vyhodnocení `when` pro každý kontext.
  - **Závisí na:** P1.1.1
  - **Odhad:** F195 1–2 ČT (matice); F098 bez odhadu
- [x] **P1.6.2** Výchozí zkratky a mapování kláves
  - **Co a proč:** Výchozí mapa kláves pro Windows (Ctrl) a macOS (⌘), editor zkratek s kontexty a viditelnými konflikty. Klávesa pluginu nikdy nepřepíše výchozí ani uživatelskou zkratku. Příkaz má v registru vazbu na kontext, výběry, zrušení a transakci undo; `Tab` má určený návrat k běžnému odsazení, když ho příkaz neobslouží. Profil „PSPad“ je MVP v P2.
  - **Funkce:** F019, F119
  - **Stav 26. 10. 2026:** model kláves hotový: konflikty SameLayer, Overridden a Prefix se zdrojem obou vazeb, výchozí mapa nemá nejednoznačný konflikt na Windows, macOS ani Linuxu, příkaz se zkratkou musí být v menu nebo paletě (test), neobsloužený `Tab` spadne na `nks.edit.insertIndent`; model umí i klávesy profilu „PSPad“. Zbývá zobrazení konfliktu v nastavení aplikace.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** konflikt dvou zkratek ve stejném kontextu se ukáže v nastavení; každý příkaz se zkratkou je i v menu nebo paletě; neobsloužený `Tab` odsadí jako bez příkazu.
  - **Závisí na:** P1.6.1
  - **Odhad:** bez odhadu

### P1.7 Nastavení

- [x] **P1.7.1** Verzované schéma nastavení
  - **Co a proč:** Vlastní verzované schéma s migrací (kap. 5, řádek Import PSPadu: stará INI neurčují stavbu UI). Hodnoty mají zdroj: výchozí, uživatel, jazyk, EditorConfig, dokument, později plugin. Pořadí přednosti je pevně dané. Nastavení podle jazyka a dokumentu. Pravidlo pro návrat ke starší verzi editoru je součástí schématu.
  - **Funkce:** F103, F106
  - **Stav 26. 10. 2026:** hotovo. Test tabulky 28 dvojic zdrojů má očekávaného vítěze, `ResolvedSetting` nese zdroj, migrace jsou otestované (schéma je ve verzi 1, produkční migrace zatím žádná), neznámé klíče přežijí, novější nebo nečitelný soubor se nikdy nepřepíše, zápis přes dočasný soubor a záměnu. Pořadí: Default < Plugin < User < UserLanguage < Workspace < WorkspaceLanguage < EditorConfig < Document.
  - **Hotovo, když:** tabulka konfliktů výchozí / uživatel / jazyk / EditorConfig / dokument / plugin má pro každou dvojici očekávaného vítěze a test ji projde; u každé hodnoty jde zjistit zdroj; změna verze schématu převede staré nastavení testovanou migrací a neznámé podporované hodnoty zachová podle zapsaného pravidla; starší verze editoru novější soubor nastavení nepoškodí.
  - **Závisí na:** P1.1.1
  - **Odhad:** bez odhadu
- [x] **P1.7.2** Prohledávatelné nastavení
  - **Co a proč:** Napíšu „tab“ a vidím všechny volby, odkud hodnota je a konflikty zkratek.
  - **Funkce:** F164
  - **Stav 26. 10. 2026:** model hledání hotový: „tab“ najde všechny čtyři volby odsazení a tabulátoru se zdrojem, „kodovani“ najde „Výchozí kódování“, řádky kláves ukazují zkratku, zdroj a konflikty. Zbývá pohled nastavení v aplikaci.
  - **Stav 26. 10. 2026 (editor v aplikaci, sloučeno po fef705d):** hotovo v aplikaci: AvaloniaEdit nad `Document` s čísly řádků a zvýrazněním aktuálního řádku; soubory (nový, otevřít, uložit, uložit jako s volbou kódování a dialogem ztrát, dotaz při zavření, znovu načíst, konflikt při uložení, nemodální lišta při změně zvenku); menu kódování se dvěma akcemi; stavový řádek podle D5; karty s tečkou a štítkem velikosti, skupiny a rozdělení; řádkové a výběrové operace a blokový výběr; menu, paleta a klávesy jen z registru; okno nastavení se zdrojem hodnot a konflikty; žurnál, lišta obnovy a obnova relace v aplikaci. Publikovaný NativeAOT program prošel skutečným vstupem z klávesnice: CP1250 soubor se po jednom znaku a Ctrl+S liší jen v tom bajtu, 20MiB soubor zůstal stránkovaný a bajtově shodný. Review D5 2. kolo v interním záznamu.
  - **Hotovo, když:** hledání „tab“ najde všechny volby odsazení a tabulátoru se zdrojem hodnoty; konfliktní zkratky jsou označené.
  - **Závisí na:** P1.7.1, P1.6.2
  - **Odhad:** F164 2–3 ČT (matice)

### P1.8 Přístupnost a IME

měření obojí řadí do P1 jako samostatné úkoly. P1 ověří nativní vrstvu pod AOT a běžný editor. Stránkovaná plocha a hex mají vlastní body v P3 (P3.1.5, P3.5.8). Textová editace stránkovaného textu do 100 MiB je od spiku měření také v P3 (P3.1.5), nad 10 GiB v P7 (P7.1.7). Test P0 v `AccessibilityProbeTests.cs` zkoumá jen peery a rozhraní IME, není to běh NVDA ani VoiceOveru. Tvrzení P0 o chybějícím TextPattern je historický podklad; aktuální stav závislosti je **neověřeno**. Všechny body P1.8 se ověřují v matici R10 z PR.4.

- [ ] **P1.8.3** Nativní most UIA a AX pod NativeAOT
  - **Co a proč:** Proveditelnost se ověří hned na začátku P1, ne až v P9: UIA s TextPattern na Windows a NSAccessibility na macOS v publikovaném AOT programu. NativeAOT nemá vestavěný klasický COM interop; pro UIA je cesta přes ComWrappers se source generation. Proto se ověřuje skutečný nativní most, ne jen spravovaný automation peer.
  - **Funkce:** žádná (infrastruktura)
  - **Stav 26. 10. 2026 (připojení k aplikaci):** most je připojený k editoru v okně aplikace: skrytý nativní prvek vedle editoru (HWND s `WM_GETOBJECT`, na macOS NSView), peer `TextArea` ukazuje na něj. Publikovaná AOT win-x64 bez varování; klient `UiaClient app` prošel 24 kontrol nad skutečným oknem (editor jako Document s TextPattern, text s diakritikou, kurzor a výběr z klávesnice, `Select`, `GetCaretRange`, `RangeFromPoint`, psaní a Backspace s událostmi, fokus karta↔editor). macOS 15 AOT bez varování, smoke v GUI relaci přečetl vlastní strom NSAccessibility okna (AXTextArea, AXValue, AXSelectedTextRange, AXFocusedUIElement editor→karta→editor); AX klient z jiného procesu blokuje zamčená obrazovka a chybějící oprávnění Accessibility. NVDA a VoiceOver potřebují člověka.
  - **Stav 26. 10. 2026:** Windows hotovo (commity 52fc47f–d033896): most UIA přes zdrojově generované COM, AOT win-x64 bez varování; samostatný klient (System.Windows.Automation a UIA3 TextPattern2) prošel 29 kontrol: text DocumentRange včetně ěščř a emoji, výběr, 5 pohybů kurzoru = 5 událostí, Select, změny textu, pohyb po slovech, řádcích a odstavcích, FindText, RangeFromPoint, obdélníky řádků, GetCaretRange. Avalonia 12.1.4 TextPattern nemá, proto vlastní most; zatím není připojený do stromu UIA okna Avalonie. macOS: most NSAccessibility přes ObjC runtime, AOT osx-arm64 bez varování, vlastní test 31 kontrol; skutečný AX klient ho nepřečetl (zamčená obrazovka, chybí oprávnění Accessibility), není připojený k NSView Avalonie.
  - **Hotovo, když:** v AOT buildu v matici R10 (PR.4) přečte NVDA a VoiceOver text a pozici kurzoru z minimálního textového prvku a změnu fokusu mezi kartou a editorem; záznam uvádí verzi OS, RID a hash buildu.
  - **Závisí na:** P1.1.2
  - **Odhad:** bez odhadu
- [ ] **P1.8.1** Vrstva UIA a AX pro běžný editor
  - **Co a proč:** AvaloniaEdit nemá automation peer a Avalonia nemá UIA TextPattern. Běžný editor potřebuje vlastní vrstvu nad mostem z P1.8.3: UIA s TextPattern na Windows a NSAccessibility na macOS. Vrstva je napsaná nad kontraktem dokumentu, aby ji v P3 převzala stránkovaná plocha.
  - **Funkce:** žádná (infrastruktura)
  - **Stav 26. 10. 2026:** `EditorTextSurface` napojuje most na AvaloniaEdit v aplikaci (viz P1.8.3), test povrchu v App. Chybí běh s NVDA a VoiceOver a matice R10. Prvek menu má od 26. 10. 2026 přístupný název ve všech čtyřech jazycích (ověřeno přes UIA). Omezení Avalonie 12.1.4: tlačítka okna (Minimize, Maximize, Close) jsou vždy anglicky, protože `WindowDrawnDecorations.AttachCaptionButtons` jim název nastaví sám a rámeček leží ve vlastní vrstvě mimo strom okna; nahlásit Avalonii, jinak vlastní šablona rámečku. Dále: `DocumentTextSurface` při dotazu klienta čte celý text po každé změně (O(n) na úhoz; vyřeší povrch P3).
  - **Hotovo, když:** NVDA i VoiceOver přečtou řádek, slovo, výběr, pozici kurzoru, změnu fokusu a změnu po nové revizi podle předem zapsaného očekávání, nad AOT buildem v matici R10 (PR.4).
  - **Závisí na:** P1.4.1, P1.8.3
  - **Odhad:** bez odhadu
- [ ] **P1.8.2** Kontrola preeditu IME
  - **Co a proč:** AvaloniaEdit hlásí `SupportsPreedit = false`; rozpracovaný text IME se kreslí v okně systému. Ručně ověřit českou, japonskou a čínskou klávesnici a podle výsledku doplnit preedit v řádku.
  - **Funkce:** žádná (infrastruktura)
  - **Stav 26. 10. 2026:** AvaloniaEdit hlásí `SupportsPreedit=False`, rozpracovaný text kreslí systém, ne editor; přesné místo pro napojení (obal klienta v `TextArea`) je v interním záznamu. Skutečná IME (česká, japonská, čínská klávesnice) neověřena.
  - **Hotovo, když:** v matici R10 (PR.4) v přihlášené relaci projde u tří klávesnic zahájení, změna, potvrzení a zrušení kompozice, přepnutí karty během kompozice, změna DPI, kompozice s více kurzory (po P2.2.1 se zopakuje) a undo, bez zdvojení i ztráty textu; rozpracovaný text se kreslí v řádku editoru, nebo je zapsané rozhodnutí majitele, proč ne.
  - **Závisí na:** P1.4.1
  - **Odhad:** bez odhadu

## P2 – navigace a hledání

Odhad etapy 22–37 ČT. Je to součet odhadů položek podle revize; dřívější rozpočet 16,5–25 ČT byl nižší než jeho vlastní položky. Celou cenu F148 (2–3 ČT) nese P2, i když symboly dokončí P4.4.1. Hotovo, když: práci nad složkou i neuloženým textem jde dokončit; konflikty revizí jsou ověřené.

Souběžně mohou běžet souborový panel (P2.3), textové hledání (P2.4, P2.5) a víc kurzorů (P2.2) nad hotovými revizemi a kotvami z P1.

### P2.1 Paleta a rychlá navigace

Hlavní vstupy D5: nahoře není pruh tlačítek, takže paleta a Goto Anything nesou většinu příkazů.

- [x] **P2.1.1** Paleta příkazů
  - **Co a proč:** Ctrl+Shift+P / ⇧⌘P, fuzzy hledání nad registrem, kontext, dostupnost a zkratka u příkazu. `/` hledá v nastavení, `=` spočítá výraz.
  - **Funkce:** F147
  - **Stav 26. 10. 2026:** hotovo: nedostupný příkaz zeslabený s důvodem, `/klíč` hledá v nastavení a Enter otevře filtrované nastavení, `=výraz` počítá (chyba nic nespustí), hledání v názvech i zkratkách, rozptýlená písmena až bez přímé shody.
  - **Hotovo, když:** „ulož“ najde všechny příkazy s uložením i se zkratkami; nedostupný příkaz v aktuálním kontextu je označený s důvodem; `/tab` otevře nalezené volby nastavení; `=1+2*3` ukáže 7, neplatný výraz a dělení nulou ukážou chybu a výraz nikdy nespustí kód.
  - **Závisí na:** P1.6.1, P1.7.2
  - **Odhad:** F147 1–2 ČT (matice); `/` a `=` 0,5 ČT
- [x] **P2.1.2** Goto Anything a nedávné soubory
  - **Co a proč:** Ctrl+P / ⌘P: soubor podle části jména, otevřený buffer, `:120` řádek, dočasný náhled. Sjednocuje rychlý seznam souborů a nedávné soubory s rozlišením chybějící cesty. Symboly `@` přijdou s Code Explorerem v P4.4.1.
  - **Funkce:** F148, F145, F041
  - **Stav 26. 10. 2026:** hotovo: Ctrl+P hledá podle částí cesty, `:řádek`, `soubor:řádek`, otevřené dokumenty a nedávné soubory (seznam v profilu), chybějící cesta označená; náhled je panel s prvními řádky a seznam nedávných nemění.
  - **Hotovo, když:** „mvc ctrl“ najde soubor podle částí cesty; náhled nezmění seznam nedávných souborů, dokud se soubor neotevře; chybějící cesta z historie je označená.
  - **Závisí na:** P2.1.1, P2.3.1
  - **Odhad:** F148 2–3 ČT (matice), včetně symbolů v P4.4.1; celá cena se počítá v P2
- [x] **P2.1.3** Panel otevřených souborů
  - **Co a proč:** Přehled otevřených dokumentů s filtrem, panel na požádání (D5).
  - **Funkce:** F146
  - **Stav 26. 10. 2026:** hotovo: Ctrl+B, filtr při psaní, Enter přepne kartu, přístupné názvy.
  - **Hotovo, když:** panel jde otevřít klávesou, filtr zúží seznam při psaní a Enter přepne kartu; panel je ovladatelný čtečkou.
  - **Závisí na:** P1.4.5
  - **Odhad:** bez odhadu
- [x] **P2.1.4** Nedávná místa úprav
  - **Co a proč:** Ctrl+Shift+E seznam míst posledních změn s úryvky napříč kartami, volba „jen upravené“, historie skoků Alt+← / Alt+→ (sdílí ji skok na offset F236). Místa jsou kotvy, takže přežijí další úpravy.
  - **Funkce:** F174
  - **Stav 26. 10. 2026:** hotovo: úryvky, volba „jen upravené“, Alt+← a Alt+→, místa jsou kotvy a přežijí úpravy i znovuotevření karty; jen v rámci relace, ne přes restart. Zkratka Ctrl+K Ctrl+E, protože Ctrl+Shift+E patří souborovému panelu (D5).
  - **Hotovo, když:** po úpravách ve třech kartách seznam ukáže tři místa s úryvky; po vložení textu nad místem skok trefí stále totéž místo; místo přežije zavření a znovuotevření karty v relaci.
  - **Závisí na:** P1.2.5
  - **Odhad:** F174 1–2 ČT (matice)
- [x] **P2.1.5** Související soubor
  - **Co a proč:** Jedna klávesa přepne `x.c` ↔ `x.h`, `x.cpp` ↔ `x.hpp`, `x.go` ↔ `x_test.go`, vlastní pravidla jmen; při více kandidátech výběr. S běžícím clangd později `switchSourceHeader`.
  - **Funkce:** F211
  - **Stav 26. 10. 2026:** hotovo: Alt+O, vestavěná pravidla a vlastní v `files.relatedFileRules`, výběr při více kandidátech.
  - **Hotovo, když:** v ukázkové složce přepnutí najde párový soubor pro každé vestavěné pravidlo a pro jedno vlastní pravidlo; dva kandidáti otevřou výběr.
  - **Závisí na:** P2.1.2
  - **Odhad:** F211 0,5–1 ČT (matice)

### P2.2 Víc kurzorů a chytré klávesy

- [ ] **P2.2.1** Víc kurzorů: integrace, IME a výkon
  - **Co a proč:** Přepis vrstvy `MultiCaret` z P0 nad kotvami P1.2.5, rozdělený na P2.2.3–P2.2.5. Tahle položka spojuje části, ověří IME a výkon. Přidat a přeskočit výskyt, sloupcové kurzory, vložení schránky po kurzorech, undo jako celek.
  - **Funkce:** F149
  - **Stav 27. 10. 2026:** částečně: výběry přežijí změnu z jiného zdroje i reload v jiném kódování, 1 000 kurzorů dá správný výsledek i undo. Po opravách rychlosti psaní (kotvy binárním hledáním, slučování běhů, stavový řádek) na referenční Windows VM 10 a 100 kurzorů v limitu 33 ms prošly; 1 000 kurzorů p95 58–80 ms, p99 70–89 ms – **nesplněno** (zbytek je ~12 µs na kurzor v `TextDocument.Replace` AvaloniaEdit a ~19 ms mimo obsluhu vstupu). Živé IME na CJK klávesnici neověřeno.
  - **Hotovo, když:** všechny případy z tabulky v měření procházejí a k nim nové zkratky; změna dokumentu z jiného zdroje (náhrada, reload) výběry nezahodí; 1 000 kurzorů v souboru 1 MiB: všechna vložení i undo dají očekávaný výsledek a vstup → prezentace má p95 ≤ 33 ms a p99 ≤ 80 ms; když cíl neplatí, rozpor se předloží s naměřenými čísly a schopnost se vymezí, samotný záznam čísla nestačí; IME s více kurzory projde scénářem z P1.8.2.
  - **Závisí na:** P2.2.3, P2.2.4, P2.2.5, P1.8.2
  - **Odhad:** F149 4–7 ČT (matice) za P2.2.1 a P2.2.3–P2.2.5
- [x] **P2.2.3** Kotvy a normalizace výběrů
  - **Co a proč:** Výběry jako kotvy, sloučení překrytých a sousedních výběrů, primární kurzor.
  - **Funkce:** F149 (částečně: model výběrů)
  - **Stav 26. 10. 2026:** hotovo: `SelectionSet` slučuje překryté i sousední výběry podle tabulky případů, `AnchoredSelections` drží výběry jako kotvy.
  - **Hotovo, když:** překryté výběry se sloučí podle tabulky případů; po vložení textu před výběry zůstanou výběry na stejném textu.
  - **Závisí na:** P1.2.5, P1.2.6
  - **Odhad:** v rámci F149 (viz P2.2.1)
- [x] **P2.2.4** Příkazy, vložení a undo nad více kurzory
  - **Co a proč:** Každý příkaz z registru dostane seznam rozsahů. Vložení schránky po kurzorech, jedno undo, které obnoví všechny kurzory.
  - **Funkce:** F149 (částečně: příkazy a undo)
  - **Stav 26. 10. 2026:** hotovo: psaní, mazání, Enter, vložení (3 řádky do 3 kurzorů), kopírování a vyjmutí jako jedna revize bajtů; jedno undo vrátí text i kurzory; řádkové příkazy pracují se všemi kurzory.
  - **Hotovo, když:** vložení schránky se třemi řádky do tří kurzorů dá do každého jeden řádek; jedno undo vrátí text i pozice všech kurzorů.
  - **Závisí na:** P2.2.3, P1.4.3
  - **Odhad:** v rámci F149 (viz P2.2.1)
- [x] **P2.2.5** Myš a klávesy pro více kurzorů
  - **Co a proč:** Co P0 nemělo: Ctrl+←/→, lepivý sloupec, Shift+klik, Alt+tažení, Ctrl+K Ctrl+D, přidání a přeskočení výskytu.
  - **Funkce:** F149 (částečně: ovládání)
  - **Stav 26. 10. 2026:** hotovo: Ctrl+D, Ctrl+K Ctrl+D, Ctrl+Shift+L, Ctrl+Alt+↑/↓, Alt+klik, Alt+tažení, Shift+klik, Ctrl+←/→, lepivý sloupec, Home/End, Esc. macOS jen testy mapy kláves; Ctrl+Alt+↓ v živém běhu Windows kurzor nepřidal (neověřeno).
  - **Hotovo, když:** každá zkratka a gesto má test se vstupem a očekávanými pozicemi kurzorů na Windows i macOS (⌘ místo Ctrl).
  - **Závisí na:** P2.2.3, P1.4.2
  - **Odhad:** v rámci F149 (viz P2.2.1)
- [x] **P2.2.2** Chytré klávesy pro web
  - **Co a proč:** Jen podle tokenu, bez analýzy kódu: přejmenování párové značky, doplnění `</`, escapování při vložení do řetězce, čárky v JSON, `->` v PHP, výběr proměnné bez `$`, `BEGIN` → `END` v SQL. Každá volba jde vypnout. Tokeny dává sdílený tokenový základ P2.7.1, ne druhý tokenizér.
  - **Funkce:** F107, F051 (částečně: výběr proměnné bez `$`)
  - **Stav 26. 10. 2026:** hotovo: přejmenování párové značky, doplnění `</`, escapování při vložení do řetězce, čárka v JSON po Enteru, `->` v PHP, výběr proměnné bez `$`, `END` po `BEGIN`; každá volba s kladným a záporným testem a vypínačem.
  - **Hotovo, když:** každá volba má test na kladném i záporném příkladu a jde vypnout v nastavení; přejmenování `<div>` na `<section>` změní i uzavírací značku; `</` uvnitř řetězce nic nedoplní.
  - **Závisí na:** P1.4.3, P2.7.1
  - **Odhad:** 1–2 ČT
- [x] **P2.2.6** Profil chování „PSPad“
  - **Co a proč:** Volitelný profil kláves a chování editoru podle rozboru PSPadu (Home/End, Backspace v odsazení, párové znaky, F8). plán ho dává do MVP v P2. Je to vlastní implementace; z PSPadu se nic nenačítá a nejde o import. Úkol nahrazuje dřívější PZ.1.2.
  - **Funkce:** F019 (částečně: profil PSPad), F054 (částečně: F8 v profilu PSPad)
  - **Stav 26. 10. 2026:** hotovo: profil je vrstva nastavení a kláves (Home/End, Backspace v odsazení, párové znaky, F8, Alt+tažení jako blok, kolize zkratek z kap. 0.2 včetně Ctrl+Y, Alt+Backspace, Ctrl+W, Ctrl+Shift+I/U), přepínač v nastavení a v menu. Referenční chování proti originálu ve VM neověřeno.
  - **Hotovo, když:** v MVP jdou přepnout oba profily; v profilu „PSPad“ projdou Home/End, Backspace v odsazení, F8 a kolize zkratek podle konkrétních případů z `08` kap. 0.2 a 1–11; referenční chování se ověří ve VM s originálem mimo repozitář.
  - **Závisí na:** P1.6.2, P1.4.2, P2.2.5
  - **Odhad:** bez odhadu

### P2.3 Souborový panel

- [x] **P2.3.1** Souborový panel nad diskem
  - **Co a proč:** Panel vlevo na požádání (D5), skutečný disk místo ukázkového stromu P0. Líný strom, aktualizace po změně na disku, vytvořit, duplikovat, otevřít cestu z textu. Přejmenování a koš jsou v P2.3.2 a P2.3.3.
  - **Funkce:** F136, F156
  - **Stav 26. 10. 2026:** hotovo: Ctrl+Shift+E, líný strom nad diskem, vytvořit, duplikovat, obnova při změně jiným programem, otevřít cestu pod kurzorem. Odchylka D5: v hlavičce panelu chybí ikony obnovit a sbalit (F5 a paleta). Známá vada: po zkratce s Alt přepne Windows fokus do menu.
  - **Hotovo, když:** složka se 100 000 soubory se otevře bez čekání na celý strom; soubor vytvořený jiným programem se v panelu objeví; cesta pod kurzorem v textu se otevře příkazem.
  - **Závisí na:** P1.4.4, P1.2.3
  - **Odhad:** F156 3–5 ČT (matice) za P2.3.1–P2.3.3; F136 bez odhadu
- [x] **P2.3.2** Přejmenování a přesun otevřených dokumentů
  - **Co a proč:** Přejmenování a přesun v panelu aktualizují otevřené dokumenty, jejich identitu a žurnál.
  - **Funkce:** F156 (částečně: přejmenování a přesun)
  - **Stav 26. 10. 2026:** hotovo: přejmenování a přesun otevřeného dokumentu zachová kartu, neuložené změny i undo; nový záznam žurnálu o přesunu, po pádu se dokument najde pod novou cestou.
  - **Hotovo, když:** přejmenování otevřeného souboru v panelu přejmenuje i kartu a zachová neuložené změny a undo; přesun do jiné složky zachová totéž a obnova po pádu najde dokument pod novou cestou.
  - **Závisí na:** P2.3.1, P1.5.1
  - **Odhad:** v rámci F156 (viz P2.3.1)
- [x] **P2.3.3** Koš a chyby souborových operací
  - **Co a proč:** Smazání jde do systémového koše; chyba práv, zamčený soubor nebo plný disk se ohlásí s důvodem a nic nenechá napůl.
  - **Funkce:** F156 (částečně: koš a chyby)
  - **Stav 26. 10. 2026:** hotovo: koš přes `SHFileOperation` (Windows) a `NSFileManager trashItem` (macOS, ověřeno smoke testem AOT, soubor v `~/.Trash`), chyba se ohlásí s důvodem a soubor zůstane celý.
  - **Hotovo, když:** smazání jde do systémového koše na obou OS a jde vrátit; přesun zamčeného souboru skončí hlášením a soubor zůstane celý na původním místě.
  - **Závisí na:** P2.3.1
  - **Odhad:** v rámci F156 (viz P2.3.1)

### P2.4 Hledání v dokumentu

- [x] **P2.4.1** Lišta hledání dole
  - **Co a proč:** Ctrl+F / Ctrl+H, lišta nad stavovým řádkem s přepínači Aa .* ab, jasný rozsah, počítadlo shod. Další a předchozí výskyt, hledání slova pod kurzorem. Inkrementální hledání se ruší bez blokování UI. Ctrl+H nahradí v aktivním dokumentu jeden výskyt, všechny výskyty nebo jen ve výběru.
  - **Funkce:** F055, F056, F057
  - **Stav 26. 10. 2026:** hotovo: lišta dole nad stavovým řádkem (Ctrl+F, Ctrl+H), přepínače velikosti, regexu, celých slov a výběru, počítadlo, F3 a Shift+F3 s přetočením, Ctrl+F3 slovo pod kurzorem, nahradit jedno, vše i jen ve výběru jako jeden krok undo; hledání přes `SearchWorker` nad živou revizí, nahrazení přes `ReplacePlanner` a `ChangeApplier`. Review D5 v `p2/ui-review-d5-search.md`.
  - **Hotovo, když:** v souboru 100 MiB psaní do pole zruší zastaralé hledání, UI reaguje do 100 ms a worker skončí do 1 s; F3 / Shift+F3 projde shody dopředu i dozadu; nahrazení jednoho, všech a jen ve výběru, prázdná i víceřádková shoda a náhrada během další editace dají správný počet, rozsahy a jedno undo.
  - **Závisí na:** P1.4.1
  - **Odhad:** bez odhadu
- [ ] **P2.4.2** Přejít na řádek a párové závorky
  - **Co a proč:** Skok na řádek a sloupec; u neindexovaného souboru na bajt nebo procento. Párové závorky a skok na pár podle tokenů z P2.7.1 (`08` kap. 7), takže závorka v řetězci nebo komentáři se nepočítá.
  - **Funkce:** F058, F059
  - **Stav 26. 10. 2026:** částečně: Ctrl+G na řádek, řádek:sloupec, procento a `@bajt`; Ctrl+Shift+\ na párovou závorku přeskakuje závorky v řetězcích a komentářích podle tokenizéru. Chybí: procento a bajt ve stránkovaném souboru, jehož index ještě nedoběhl (aplikace řekne, že index tak daleko nesahá).
  - **Hotovo, když:** skok na řádek v indexovaném souboru i na procento v neindexovaném trefí přesný offset z testu; skok na pár trefí odpovídající závorku a přeskočí závorky v řetězcích a komentářích.
  - **Závisí na:** P1.2.1, P2.7.1
  - **Odhad:** bez odhadu
- [x] **P2.4.3** Regulární výrazy a zkouška regexu
  - **Co a proč:** Jeden určený dialekt a jeden engine ve všech cestách textového hledání, `\u \l \U \L \E` v náhradě. Matice slibuje PCRE2 rozšířeně (F063). Úkol určí dialekt a omezení po režimech (běžný, stránkovaný) a buď slib PCRE2 splní knihovnou ověřenou pod AOT, nebo předloží konkrétní rozpor majiteli. Předstírat podporu nesmí. Regex z P0 hledá bez shod přes konce řádků a jeho testy nedokládají celý rozsah. U pole hledání „Zkusit“ nad vzorovým textem (shody, skupiny) a „Vysvětlit“. Import vzorů TRegExpr z PSPadu je až po 1.0.
  - **Funkce:** F063, F188
  - **Stav 26. 10. 2026:** hotovo podle R11: jediné místo volby enginu (`CompiledPattern.Create`) pro lištu, Find/Replace in Files i stránkované hledání; `NonBacktracking`, kde to vzor dovolí, jinak klasický engine s limitem `search.regexTimeoutMs` (výchozí 2000 ms, 50–60000); `^` a `$` vkládají na každý řádek, CRLF zůstává celé; nepodporované konstrukce PCRE v okně „Zkusit“ ve 4 jazycích. Rychlá sada na referenční Windows VM 2298/2298. Neověřeno v běžící aplikaci: patička okna „Zkusit“ a položka v Nastavení.
  - **Hotovo, když:** sada vzorů pro zvolený dialekt projde včetně skupin, náhrad, víceřádkových shod a chybného vzoru s jasnou chybou; vzor nad časovým limitem se zastaví; výsledek „Zkusit“ nad vzorovým textem je stejný jako hledání stejným vzorem v dokumentu; `\U$1` převede skupinu na velká písmena; „Vysvětlit“ popíše každou podporovanou konstrukci vzoru a u nepodporované řekne omezení.
  - **Závisí na:** P2.4.1
  - **Odhad:** F188 0,5–1 ČT (matice); převod velikosti v náhradě 0,5 ČT; výběr a ověření dialektu bez odhadu

### P2.5 Hledání a náhrady v souborech

Vlastní hledání v jádře místo ripgrepu (rozhodnutí brány P0). Služba náhledu změn z této etapy je podmínka pro P3b.

- [x] **P2.5.1** Worker hledání v souborech: výsledky, zrušení a výkon
  - **Co a proč:** Hledání ve složce i v neuložených kartách, průběžné výsledky po dávkách, zrušení, omezení počtu výsledků. Výsledky jako karta „Výsledky hledání“, dvojklik skočí do souboru. Staví na `TreeSearch` z P0, výčtu souborů z P2.5.4 a hledání nad revizemi z P2.5.5.
  - **Funkce:** F151, F060
  - **Stav 26. 10. 2026:** hotovo: worker a karta „Výsledky hledání“ s průběžnými shodami, dvojklik skočí na shodu, hlášení limitu 10 000, hledání i v neuložených kartách; test ověřuje zrušení v UI ≤ 100 ms a ve workeru ≤ 1 s. Poměr času proti `rg --json` 0,33–0,55 nad 100 000 soubory; shoda nepřekročí řez čtení u souborů nad 4 MiB (řez je za LF).
  - **Hotovo, když:** hledání ve 100 000 souborech je ≤ 1,25× času `rg --json` na stejných datech; shodují se nejen počty, ale i uspořádané offsety, délky a revize shod, včetně BOM, CRLF, vícebajtových znaků a překryvů bloků; zrušení: UI ≤ 100 ms, worker ≤ 1 s; při dosažení limitu výsledků to karta řekne.
  - **Závisí na:** P2.5.4, P2.5.5
  - **Odhad:** F151 3–5 ČT (matice) za P2.5.1, P2.5.4 a P2.5.5
- [x] **P2.5.4** Výčet souborů a pravidla ignorování
  - **Co a proč:** Procházení složky s pravidly `.gitignore`, `.git`, `.codegraph/` a přepisy agentů, bez sledování smyček odkazů.
  - **Funkce:** F151 (částečně: výčet souborů)
  - **Stav 26. 10. 2026:** hotovo: `FileWalker` s vnořenými `.gitignore` a `.ignore`, negací, složkovými vzory, skrytými soubory, limitem velikosti, ochranou proti smyčkám odkazů a junctionům a přepisy agentů; rozdílový test proti `git ls-files` souhlasí.
  - **Hotovo, když:** výčet nad testovací složkou s vnořenými `.gitignore`, negací a smyčkou odkazů dá stejný seznam jako referenční seznam v testu.
  - **Závisí na:** P1.2.1
  - **Odhad:** v rámci F151 (viz P2.5.1)
- [x] **P2.5.5** Hledání nad revizemi a živými buffery
  - **Co a proč:** Otevřený dokument se prohledává v aktuální revizi, ne na disku; výsledek nese revizi.
  - **Funkce:** F151 (částečně: živé buffery)
  - **Stav 26. 10. 2026:** hotovo: otevřený dokument se prohledává ve snímku své revize včetně neuložených úprav, shoda nese id dokumentu, revizi a bajtový rozsah; test ukazuje, že úprava během hledání nedá neplatný offset.
  - **Hotovo, když:** neuložená karta vrací shody ze živého textu s její revizí; úprava během hledání nezpůsobí shodu s neplatným offsetem.
  - **Závisí na:** P2.5.4, P2.4.3, P1.2.4
  - **Odhad:** v rámci F151 (viz P2.5.1)
- [x] **P2.5.2** Služba náhledu změn
  - **Co a proč:** Jedno místo, kde se ukáže navrhovaná změna jako rozdíl (červeně co zmizí, zeleně co přibude) a kde se použije, přeskočí nebo vrátí. Návrh nese revizi. Použije ji Replace in Files, recepty, pluginy (P3b) a později revize změn F178; druhý diff ani druhý formát návrhu nevznikne.
  - **Funkce:** F152 (částečně: společná služba)
  - **Stav 26. 10. 2026:** hotovo jako služba: `ChangeProposal`, `FileChange`, `Hunk`, `ChangeSelection` a `ChangeApplier`; zastaralá revize nebo změněný soubor vrátí `NeedsRepreview`, otevřený dokument dostane jeden krok undo. Barevný rozdíl v UI chybí (přijde s P2.5.3 a P3b).
  - **Hotovo, když:** návrh nad zastaralou revizí se odmítne s nabídkou nového náhledu; použití návrhu je jeden krok undo v každém dotčeném dokumentu.
  - **Závisí na:** P1.2.4
  - **Odhad:** v rámci F152 (viz P2.5.3)
- [x] **P2.5.3** Replace in Files: použití dávky
  - **Co a proč:** Náhrada ve více souborech přes službu P2.5.2: náhled po souborech, přeskočení souboru, kontrola revize podkladu u každého souboru. Obnovu po pádu řeší P2.5.6.
  - **Funkce:** F152, F060
  - **Stav 26. 10. 2026:** hotovo: náhled po souborech s červeně odebraným a zeleně přidaným textem, zaškrtávátka u souboru i u změny, konflikt se ohlásí a ostatní soubory se použijí; po startu nabídka dokončit nebo vrátit přerušenou dávku. Pád uprostřed dávky ověřen crash harnessem (P2.5.6), v aplikaci jen simulovaným pádem.
  - **Hotovo, když:** soubor změněný jiným procesem mezi náhledem a použitím se nepřepíše a ohlásí se jako konflikt; ostatní soubory dávky se použijí; pád uprostřed dávky projde scénářem z P2.5.6.
  - **Závisí na:** P2.5.1, P2.5.2, P2.5.6
  - **Odhad:** F152 4–7 ČT (matice) za P2.5.2, P2.5.3 a P2.5.6
- [x] **P2.5.6** Žurnál dávky a dokončení nebo vrácení po pádu
  - **Co a proč:** Dávka náhrad se zapisuje do žurnálu z P1.5.3 tak, aby po pádu šla dokončit nebo vrátit po souborech.
  - **Funkce:** F152 (částečně: obnova dávky)
  - **Stav 26. 10. 2026:** hotovo: `BatchRecovery` dávku dokončí nebo vrátí. Náhodná zabití našla pád uvnitř `ReplaceFile` (chybějící soubor, zbylý `~RF….TMP`); obnova teď vrací zaparkovaný originál. Po opravě 20 běhů po 36 pokusech (26 bodů pádu, 10 náhodných zabití) bez chyby.
  - **Hotovo, když:** pád injektovaný po každém souboru dávky jde po startu dokončit nebo vrátit a výsledek se shoduje s nezávisle spočítaným očekáváním.
  - **Závisí na:** P2.5.2, P1.5.1
  - **Odhad:** v rámci F152 (viz P2.5.3)

### P2.6 EditorConfig

- [x] **P2.6.1** EditorConfig
  - **Co a proč:** Čte `indent_style`, `indent_size`, `tab_width`, `trim_trailing_whitespace`, `insert_final_newline`. Ořez mezer jen na upravených řádcích. `end_of_line` a `charset` jen u nového souboru nebo výslovnou akcí přes F169. Zdroj hodnoty v nastavení a ve stavovém řádku.
  - **Funkce:** F189
  - **Stav 26. 10. 2026:** hotovo: EditorConfig napojený na otevřené dokumenty, odsazení z `.editorconfig` má přednost před detekcí, zdroj hodnoty v nastavení i ve stavovém řádku, uložení ořezává jen upravené řádky a konce řádků nemění. `end_of_line` a `charset` pro nový soubor jen přes vrstvu nastavení; `end_of_line=cr` nastavení nenabízí.
  - **Hotovo, když:** uložení existujícího souboru s jiným `end_of_line` v `.editorconfig` nezmění konce řádků; ořez mezer se týká jen upravených řádků; nastavení ukáže `.editorconfig` jako zdroj hodnoty.
  - **Závisí na:** P1.7.1, P1.3.5
  - **Odhad:** F189 1 ČT (matice)

### P2.7 Tokenový základ

Chytré klávesy (P2.2.2) a párové závorky (P2.4.2) potřebují tokeny dřív, než vznikne zvýrazňování v P3. Proto tady vzniká sdílený základ a P3.2 na něm dokončí vykreslení, pravidla a výkon. Druhý, zahazovaný tokenizér nevznikne.

- [x] **P2.7.1** Stavový tokenizér a formát gramatiky
  - **Co a proč:** Tokenizér se stavem na začátku řádku a formát gramatiky jako data (stejný budou používat pluginy, F197). Zatím jen gramatiky, které potřebují P2.2.2 a P2.4.2: HTML, PHP, JSON, SQL.
  - **Funkce:** F121 (částečně: tokenový základ)
  - **Stav 26. 10. 2026:** hotovo (commity 0d0c58b–d7a7adb, sloučeno a0a5e7d): formát gramatik JSON se schématem (`formatVersion` 1), skupiny klíčových slov, regiony begin/end s vnořením a `embed`; stav řádku je internovaný zásobník otevřených regionů. 16 vestavěných gramatik (text, INI, JSON, XML, HTML s CSS/JS/PHP, CSS, JS, PHP, C#, SQL, Markdown, Python, Shell, YAML, Latte, NEON). Ručně psané fixture HTML, PHP, JSON a SQL sedí, víceřádkové řetězce a komentáře mají správný stav; inkrementální = plná tokenizace v náhodném testu nad 15 gramatikami. Úhoz v 1 MiB p95 1,6–2,8 ms (rozpočet zvýrazňovače ~6,6 ms z 33 ms). Chybí: těla blokových skalárů YAML, heredoc v PHP a Shellu, makra Latte uvnitř HTML značek; skok hluboko do souboru potřebuje kontrolní body stavu (P3).
  - **Hotovo, když:** tokeny HTML, PHP, JSON a SQL se pro testovací soubory shodují s ručně zapsaným očekáváním; řetězec a komentář přes více řádků mají správný stav na začátku každého řádku.
  - **Závisí na:** P1.2.1
  - **Odhad:** bez odhadu

## P3 – MVP a velký náhled

Odhad etapy 18,5–30,5 ČT, **neúplný**: F153 6–10, lišta 0,5, F161 2–4 a F230–F236 10–16 ČT. Neobsahuje zvýrazňovač, editaci 100 MiB (P3.1.10–P3.1.13), přístupnost stránkované plochy ani distribuci, které jsou bez odhadu. Neobsahuje ani podíl P3 na funkcích rozdělených mezi etapy: F155 3–5, F169 2–4 a F248 1–2 ČT, dohromady 6–11 ČT pro P3 a P5, P7 nebo P9, ani části F154 přesunuté z P7. Položky P3 proto dávají 24,5–41,5 ČT, víc než rozpočet etapy. Rozdělení a nový rozpočet uzavře PR.8.2 před zahájením P3. Hotovo, když: splněné cíle MVP z kap. 3 včetně editace textu 100 MiB, undo po uložení i ve stránkovaném režimu, viditelná omezení velkého režimu, reálná práce uživatelů; hex test z P3.5 projde celý bez myši.

Společné bajtové uložení, snapshot a žurnál pro hex vznikají tady (P3.5.5), ne až v P7. P7 je jen rozšíří a nesmí změnit ukládací kontrakt bez integračních testů hexu. Hex renderer (P3.5.2) a textový renderer (P3.1) běží souběžně nad stejným bajtovým rozhraním; hex nečeká na hotové řádkové vykreslení.

### P3.1 Velké soubory

Čtení a hledání souborů do 50 GiB. Text do 100 MiB se v MVP i edituje, v editovatelném stránkovaném režimu (P3.1.10–P3.1.13, cesta z P1.2.9). Editace textu obřích souborů je až P7 (R4).

- [ ] **P3.1.1** Stránkovaná textová plocha: integrace a platformní ověření
  - **Co a proč:** Vlastní textová plocha nad Avalonia/Skia, která čte jen viditelnou část `PagedDocument`. Rozdělená na P3.1.6–P3.1.9; tahle položka je spojí a ověří na obou OS. Lišta řekne, co ve stránkovaném režimu nejde. Stav indexu jen jako text ve stavovém řádku (`37 % · indexuji řádky`), bez pravítka v GiB.
  - **Funkce:** F153, F141 (částečně: lišta omezení režimu)
  - **Hotovo, když:** 10GiB soubor na obou strojích R8: první obraz ≤ 1 s warm / 2 s cold; posun vstup → prezentace p95 ≤ 33 ms, p99 ≤ 80 ms; lišta stránkovaného režimu vyjmenuje nedostupné funkce.
  - **Závisí na:** P3.1.6, P3.1.7, P3.1.8, P3.1.9, P1.2.2
  - **Odhad:** F153 6–10 ČT (matice) za P3.1.1 a P3.1.6–P3.1.9; lišta 0,5 ČT
- [ ] **P3.1.6** Viewport a shaping
  - **Co a proč:** Výřez dokumentu podle viditelné oblasti, tvarování textu přes Skia, náhradní písmo.
  - **Funkce:** F153 (částečně: viewport)
  - **Hotovo, když:** výřez na náhodném offsetu 10GiB souboru vykreslí stejné znaky jako referenční dekódování a nečte víc než určený počet bloků.
  - **Závisí na:** P1.2.8
  - **Odhad:** v rámci F153 (viz P3.1.1)
- [x] **P3.1.7** Navigace a výběr ve stránkované ploše
  - **Co a proč:** Kurzor, výběr, klávesy a myš nad bajtovými pozicemi bez materializace řádku.
  - **Funkce:** F153 (částečně: navigace a výběr)
  - **Stav 26. 10. 2026:** hotovo: výběr na stránkované ploše (Shift+šipky, Home/End, PageUp/PageDown, Ctrl+Home/End, myš, Shift+klik, Ctrl+A), přepis a mazání výběru jako jeden krok undo; testy přesných bajtů přes hranici bloků a CRLF a pozic Home/End a stránkování proti naivnímu průchodu. Neověřen vzhled.
  - **Hotovo, když:** výběr přes hranici bloků a přes CRLF dá přesně určené bajty; Home/End a stránkování trefí pozice z testu.
  - **Závisí na:** P3.1.6
  - **Odhad:** v rámci F153 (viz P3.1.1)
- [ ] **P3.1.8** Líný index řádků a skoky
  - **Co a proč:** Index řádků na pozadí, skok na offset a na řádek, označení neúplného indexu „≈“. Úplný index 50GiB souboru trval na referenční Windows VM 3,7 minuty. Plán pro úplnou indexaci časový limit nemá a nový se nevymýšlí; UI ale musí celou dobu fungovat.
  - **Funkce:** F153 (částečně: index a skoky)
  - **Stav 26. 10. 2026:** částečně: index řádků na pozadí s „≈“; už zaindexovaný začátek jde zobrazit a posouvat s přesnými čísly řádků; skok na offset za indexem (`@N`, procenta, Ctrl+End) trefí bajty s odhadem čísla řádku „≈“ (do 2 %), po doběhnutí indexu přesná čísla se zachovanou pozicí; řádek za indexem vrátí hlášení; opravený pád UI po zavření karty během indexace. Large test na 400 MB prošel; 50GiB soubor a odezva UI neměřeny; obejití počítá s jednobajtovými konci řádků (UTF-8).
  - **Hotovo, když:** skok na offset i na řádek trefí stejné místo jako naivní výpočet; během několikaminutové indexace 50GiB souboru: průběh je vidět, posun splní limity PR.2.3, skok za hotový prefix indexu trefí správný offset a číslo řádku nese „≈“, dokud není přesné; zavření karty, reload a zrušení indexaci ukončí a uvolní zdroje; private commit i working set během indexace jsou zapsané (P3.1.9).
  - **Závisí na:** P1.2.8
  - **Odhad:** v rámci F153 (viz P3.1.1)
- [ ] **P3.1.9** Invalidace a limity paměti
  - **Co a proč:** Které části výřezu se po změně podkladu nebo revize překreslí; strop paměti cache bloků. Na referenční Windows VM měl 50GiB soubor po indexu 109,3 MiB private, ale working set při indexaci přibližně 12 297 MiB; sledují se obě veličiny. Schválený limit private commit se tím nemění.
  - **Funkce:** F153 (částečně: invalidace a limity)
  - **Před zahájením:** strop cache bloků v MiB se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026:** částečně: po úpravě zůstanou v mezipaměti řádky před první změnou, ostatní se načtou znovu; index sloupců se po úpravě a změně kódování zahodí. Strop paměti mezipaměti pohledu v MiB zůstává otevřený (dnes jen 400 řádků).
  - **Hotovo, když:** po změně revize se překreslí jen dotčené řádky výřezu; cache bloků nepřekročí zapsaný strop ani při rychlém posunu přes celý 10GiB soubor ani během úplné indexace 50GiB souboru; záznam uvádí private commit i working set.
  - **Závisí na:** P3.1.6
  - **Odhad:** v rámci F153 (viz P3.1.1)
- [x] **P3.1.2** Stránkovaný zdroj UTF-16 a UTF-32
  - **Co a proč:** `MappedFileSource` dnes UTF-16 a UTF-32 odmítne, protože hledá konce řádků po bajtech. Korpus má UTF-16 LE 100 MiB. Je to samostatný adaptér zdroje nad P1.2.8, nečeká na hotové UI.
  - **Funkce:** F153 (částečně: UTF-16 a UTF-32)
  - **Stav 26. 10. 2026:** hotovo: stránkovaný backend drží UTF-16 a UTF-32 (LE/BE, s BOM i bez), index řádků po kódových jednotkách, změna kódování přes jinou šířku jednotky přestaví index, kontraktová sada i nad UTF-32; na referenční Windows VM Core 1079/1079 včetně Large (řídký soubor 4,5 GiB) na commitu před posledním sloučením.
  - **Hotovo, když:** pro UTF-16 i UTF-32, LE i BE, s BOM i bez: počet řádků souhlasí s generátorem korpusu, text výřezu, skok a hledání vrátí očekávané offsety; lichý konec, znak rozdělený přes blok a CRLF přes blok se zpracují podle testu; stejné případy projdou v běžném backendu (P1.3.2).
  - **Závisí na:** P1.2.8, PR.2.1
  - **Odhad:** bez odhadu
- [ ] **P3.1.3** Dlouhé řádky: náhled
  - **Co a proč:** Horizontální výřezy bez materializace řádku, žádné změny obsahu kvůli zobrazení. Čas výřezu roste lineárně se sloupcem: na referenčním referenční Windows VM trvá výřez od sloupce 90 000 000 p95 117,25 ms, od 50 000 000 p95 74,2 ms; vývojové PC dávalo ~47 ms. Samotný výpočet výřezu tedy překračuje limit 33/80 ms. Sloupcový index nebo cache posledního bodu je nutnost.
  - **Funkce:** F155
  - **Stav 26. 10. 2026:** částečně: `ColumnIndex` (bod po ~1M sloupcích, strop 16 384 bodů), řádek nad 16 KiB jde posouvat vodorovně, kreslí se jen viditelné sloupce; daleký skok bez indexu se čte mimo vlákno UI (značka „…“, novější skok starší čtení zruší), rozsah posuvníku podle délky ve znacích; Large test 100 MiB řádku prošel. Perf na referenční Windows VM 27. 10.: posun po vytvoření indexu na sloupcích 0, 50M a 90M a první skok na 50M a 90M v limitu; první skok na sloupec 0 p95 34,8 ms (po zvýšení na 100 vzorků, aby p95 nebylo nejhorším vzorkem, všech 6 testů dlouhého řádku na referenční Windows VM prošlo; ojedinělý výkyv kolem 36 ms zůstává). Otevřené: zda limit 33/80 ms platí pro snímek se značkou, nebo i pro dokreslení okna (~117 ms na VM); barvy syntaxe ve vzdáleném okně nejdou bez stavu od začátku řádku; upravený dokument čte první daleký skok synchronně; proporcionální písmo.
  - **Hotovo, když:** jeden řádek 100 MiB z korpusu se zobrazí; regresní scénáře skoků na sloupec 0, 50 000 000 a 90 000 000 na obou strojích R8, každý jako první skok bez připraveného sloupcového indexu a pak jako posun po vytvoření indexu, mají vstup → prezentace p95 ≤ 33 ms a p99 ≤ 80 ms; přírůstek paměti ≤ 16 MiB.
  - **Závisí na:** P3.1.1
  - **Odhad:** F155 3–5 ČT za náhled i editaci (matice); editace v P7.1.5; podíl P3 určí PR.8.2
- [ ] **P3.1.4** Hledání ve velkém souboru: integrace F153
  - **Co a proč:** Lišta hledání nad stránkovaným dokumentem: průběh v procentech, výsledky jako karta, zrušení. Algoritmus hledání vzniká v P2 (P2.5.5); tady se jen napojí na stránkovanou plochu, druhá skrytá implementace nevznikne. Hledá v aktuální revizi, tedy i po hex úpravě (P3.5.9).
  - **Funkce:** F153
  - **Stav 26. 10. 2026:** částečně: hledací lišta nad stránkovaným dokumentem přes `SearchWorker` s průběhem v procentech, hlášením limitu a tlačítkem „Vypsat“ do karty výsledků; test offsetů proti nezávislému průchodu přes hranice bloků (47/47 lokálně). Neověřeno: 10GiB soubor (průběh, zrušení ≤ 1 s) na referenční Windows VM.
  - **Hotovo, když:** hledání v 10GiB souboru ukazuje průběh; uspořádané offsety a délky shod souhlasí s nezávislým průchodem, i přes hranice bloků a s vícebajtovými znaky; zrušení zastaví worker do 1 s; při dosažení limitu výsledků to karta řekne.
  - **Závisí na:** P3.1.1, P2.4.1, P2.5.5
  - **Odhad:** v rámci F153
- [ ] **P3.1.5** Přístupnost a IME stránkované plochy
  - **Co a proč:** Vrstva UIA a AX z P1.8.1 nad stránkovanou plochou; IME nad stejnou plochou. Bez toho P1 nemůže ověřit druhou plochu, která vzniká až tady. Od spiku měření sem patří i přístupnost a IME při editaci textu do 100 MiB, dřív P7.1.7; nad 10 GiB zůstávají v P7.1.7.
  - **Funkce:** F154 (částečně: přístupnost a IME editace do 100 MiB)
  - **Stav 26. 10. 2026:** částečně: stránkovaná plocha má automation model editoru (`ITextAutomationSurface`, host sdílený s editorem na Windows i macOS) nad oknem celých řádků kolem zobrazené části; text, výběr a kurzor sedí s naivním dekódováním přes hranice 128 KiB a 1 MiB bloků, CRLF a vícebajtové znaky; IME kreslí kompozici inline s podtržením, commit je jeden krok undo, přesun kurzoru kompozici zruší, rekonverze nastaví výběr. Neověřeno: NVDA, VoiceOver a skutečné IME (TSF, macOS) – potřebují člověka; výběr mimo okno čtečky se ořízne.
  - **Hotovo, když:** v matici R10 (PR.4) NVDA i VoiceOver přečtou ve stránkovaném 10GiB souboru řádek, slovo, výběr, pozici kurzoru a změnu fokusu podle očekávání; v textu do 100 MiB editovaném přes P3.1.11 a P3.1.12 přečtou vložený text, výběr a novou pozici kurzoru a scénář IME z P1.8.2 včetně přepnutí karty, DPI, více kurzorů a undo projde bez zdvojení i ztráty textu. Důkazy z běžného editoru (P1.8.1) se nepřebírají.
  - **Závisí na:** P3.1.1, P3.1.11, P3.1.12, P1.8.1, P1.8.2
  - **Odhad:** bez odhadu
- [ ] **P3.1.10** Editace textu 100 MiB v MVP
  - **Co a proč:** Produktové přijetí cesty z P1.2.9. Text nad hranicí přepnutí (P1.2.2) se otevře v editovatelném stránkovaném režimu nad `PagedDocument`, ne v AvaloniaEdit a ne jen pro čtení. Úpravy, undo a add buffer jsou v paměti, originál zůstává namapovaný a ukládá se streamem. Položka spojí v produktu úpravy na ploše (P3.1.11–P3.1.13), přístupnost a IME (P3.1.5), uložení přes P3.5.5, hledání nad aktuální revizí (P3.5.9) a obnovu po pádu podle P1.5.1: žurnál malých úprav (pozice, smazaná délka, vložené bajty) proti otisku originálu. Uložení trvalo v jádře na referenční Windows VM 0,2–0,25 s a jádro během něj úpravu nedovolí; produkt ji musí podržet, nebo zařadit do fronty. Cíl plán: první editace ≤ 2 s warm / 4 s cold, přírůstek paměti ≤ 350 MiB, psaní p95 ≤ 33 ms. Dokument bez vykreslení cíl na referenční Windows VM splnil; produkt s UI je **neověřeno**.
  - **Funkce:** F141 (částečně: editace 100 MiB)
  - **Hotovo, když:** na obou strojích R8 v přihlášené relaci se čistý ASCII soubor 100 MiB i UTF-8 log 100 MiB z korpusu (PR.2.1) otevře v produktu s UI a první úprava proběhne ≤ 2 s warm / 4 s cold; přírůstek private commit celé aplikace proti stabilizované prázdné aplikaci je u ASCII ≤ 350 MiB a záznam uvádí i špičku, okamžik měření, backend a stav indexace; psaní má p95 ≤ 33 ms a p99 ≤ 80 ms; uložení projde testem brány kap. 5.1 a obnova po pádu podle P1.5.1; úprava zadaná během uložení se neztratí a na disku je revize, která se ukládala.
  - **Závisí na:** P1.2.9, P1.5.1, PR.2.1, P3.1.5, P3.1.11, P3.1.12, P3.1.13, P3.5.5, P3.5.9
  - **Odhad:** bez odhadu; měření cenu nedává, cenu P3.1.10–P3.1.13 určí PR.8.2 před zahájením P3
- [ ] **P3.1.11** Psaní a mazání na stránkované ploše
  - **Co a proč:** Hlavní nová práce MVP podle spiku: stránkovaná plocha musí text nad hranicí přepnutí upravovat, ne jen číst. Kurzor podle čísla řádku a sloupce v aktuální revizi, psaní, Backspace, Delete, Enter a Tab, přepsání výběru psaním a seskupení undo (souvislé psaní je jeden krok). Výpočet za úpravou trval v jádře na referenční Windows VM p95 0,80 ms bez vykreslení; vstup → prezentace změřený není. Sloupec se dnes zjišťuje dekódováním celého řádku, proto MVP editaci řádku nad limitem odmítne; lokální editace dlouhých řádků zůstává v P7.1.5.
  - **Funkce:** F154 (částečně: psaní a mazání v textu do 100 MiB)
  - **Stav 27. 10. 2026:** částečně: psaní, Backspace, Delete, Enter, Tab a přepsání výběru (P3.1.7); po opravách rychlosti (záznam míst bez dotazu do indexu řádků, kurzor nastaven před úpravou, stavový řádek) test `Typing_on_the_paged_surface_stays_within_33_ms_at_p95` na referenční Windows VM prošel (dřív p95 56 ms). Vstupní rozhodnutí o limitu délky řádku zůstává otevřené; scénář s SHA-256 po uložení a druhý stroj R8 neověřeny.
  - **Před zahájením:** limit délky řádku, nad kterým se editace odmítne, se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Hotovo, když:** v čistém ASCII souboru 100 MiB i v UTF-8 logu 100 MiB z korpusu (PR.2.1) dá předem zapsaná sekvence kláves (psaní včetně českých znaků, Backspace, Delete, Enter, Tab, přepsání výběru, úpravy u CRLF) po uložení soubor se stejným SHA-256 jako nezávislý výpočet; kurzor stojí po každém kroku na zapsaném řádku a sloupci; souvislé psaní slova je jeden krok undo a 500 kroků undo a redo vrátí obsah okna podle očekávání; úprava řádku delšího než zapsaný limit se odmítne s hlášením v liště stránkovaného režimu a dokument se nezmění; psaní má na obou strojích R8 vstup → prezentace p95 ≤ 33 ms a p99 ≤ 80 ms.
  - **Závisí na:** P3.1.7, P3.1.8, P1.2.6, P1.2.8
  - **Odhad:** v rámci F154 (viz P7.1.1); podíl určí PR.8.2 před zahájením P3
- [ ] **P3.1.12** Schránka na stránkované ploše
  - **Co a proč:** Kopírovat, vyjmout a vložit v textu do 100 MiB bez `.Text` a bez druhé úplné kopie obsahu. Výběr se převádí na bajty aktuální revize. Velké vložení (desítky MiB) spike neměřil; přepočet řádků projde vložený blok jednou.
  - **Funkce:** F154 (částečně: schránka v textu do 100 MiB)
  - **Před zahájením:** největší vložení ze schránky, které se v MVP měří, a jeho časový limit se zapíšou jako vstupní rozhodnutí.
  - **Stav 26. 10. 2026:** částečně: kopírovat, vyjmout a vložit na stránkované ploše (konce řádků podle dokumentu), každé jeden krok undo, test kopie přes CRLF a vložený úsek. Vstupní rozhodnutí (největší měřené vložení a jeho limit) zůstává otevřené, čas a private commit neměřeny.
  - **Hotovo, když:** kopie výběru přes hranici bloků, přes CRLF a přes úsek vložený úpravou dá do schránky přesně očekávaný text; vyjmutí i vložení jsou každé jeden krok undo; vložení zapsané velikosti do souboru 100 MiB skončí v zapsaném čase, počet a začátky řádků pak souhlasí s úplným průchodem a přírůstek private commit celé aplikace zůstane u ASCII ≤ 350 MiB (kap. 3); u většího vložení UI nezamrzne a dokument zůstane celý: vložení buď projde, nebo se s hlášením odmítne.
  - **Závisí na:** P3.1.11
  - **Odhad:** v rámci F154 (viz P7.1.1); podíl určí PR.8.2 před zahájením P3
- [ ] **P3.1.13** Undo po uložení na místo ve stránkovaném režimu
  - **Co a proč:** Rozhodnutí majitele 26. 10. 2026: undo musí přežít uložení na místo i ve stránkovaném režimu MVP, bez viditelného omezení. Dnes `SaveAsync` na stejnou cestu undo smaže, protože úseky historie ukazují do nahrazeného originálu. Ukládá se přes společnou službu P3.5.5, která pro hex původní bajty pro undo už zachová. Tady se totéž zajistí pro textovou editaci do 100 MiB, i když se délka změní a ukládá se streamem. Zbytek P7.1.3, tedy soubory větší než RAM, zůstává v P7.
  - **Funkce:** F154 (částečně: undo po uložení na místo do 100 MiB)
  - **Stav 26. 10. 2026:** částečně: `PagedSaveUndoTests` (100 MiB, ASCII a UTF-8 log, 5000 úprav): uložení streamem, SHA-256 proti modelu, 500 kroků undo/redo, undo všech úprav na originál prošly na referenční Windows VM. Zápis na místo po zvětšení logu změn na 8192 neověřen; pád během streamového uložení textu nepokryt.
  - **Hotovo, když:** v čistém ASCII souboru 100 MiB i v UTF-8 logu 100 MiB proběhne po 5 000 úpravách uložení na stejnou cestu streamem i zápisem na místo bez změny délky; disk pak obsahuje přesně uloženou revizi (SHA-256 proti nezávislému výpočtu); 500 kroků undo a 500 redo po uložení dá v každém kontrolním bodě očekávaný obsah a undo všech úprav vrátí obsah se SHA-256 originálu; další uložení zapíše revizi po undo; přírůstek private commit u ASCII zůstane ≤ 350 MiB (kap. 3); pád injektovaný během uložení nechá na disku celý originál, nebo celou uloženou revizi, a obnova podle P1.5.1 vrátí neuloženou práci; lišta stránkovaného režimu undo po uložení neuvádí jako omezení.
  - **Závisí na:** P3.5.5, P3.1.11, P1.5.1
  - **Odhad:** v rámci F154 (viz P7.1.1); podíl určí PR.8.2 před zahájením P3

### P3.2 Zvýrazňování

TextMate v P0 zvedl p95 psaní nad 33 ms, proto vlastní zvýrazňovač nad viditelnou částí. Stavový tokenizér a formát gramatiky vznikají v P2.7.1; tady se napojí renderer, přibudou gramatiky MVP a checkpointy stavu pro velké soubory.

- [x] **P3.2.1** Vlastní inkrementální zvýrazňovač
  - **Co a proč:** Napojení tokenizéru z P2.7.1 na obě textové plochy, přeskenování od změny jen do ustálení stavu. Princip uživatelských zvýrazňovačů a multi-zvýrazňovače HTML z `09` kap. 1–6 slouží jako reference, ne jako kód. Gramatiky běžných jazyků včetně C#, Razor a souborů `.sln`, `.csproj`, `.props`; další jazyky v P8.8.2. Ruční volba jazyka má přednost před automatikou.
  - **Funkce:** F121, F067
  - **Kroky:**
    - napojení rendereru běžné i stránkované plochy
    - gramatiky MVP a snapshot testy podle případů z `09` kap. 12
    - checkpointy stavu z P3.2.3
  - **Stav 26. 10. 2026:** hotovo: barvy v běžném editoru i stránkované ploše z jednoho `SyntaxFeeder`, palety D5, ruční volba jazyka přežije reload; gramatiky MVP včetně C#, Razor (`.cshtml`, `.razor`), `.sln` a `.csproj`/`.props` (XML) se snapshot testy tokenů; NEON, Latte, Razor a `.sln` se v editoru barví podle přípony (dřív chyběly v seznamu jazyků). Win11lite2 27. 10.: `Typing_in_a_highlighted_one_mebibyte_file_stays_within_33_ms_at_p95` prošel. Razor: výrazy uvnitř hodnot atributů HTML a uvnitř `<script>` se nebarví jako Razor.
  - **Hotovo, když:** psaní v souboru 1 MiB se zvýrazněním má vstup → prezentace p95 ≤ 33 ms (cíl, který TextMate nesplnil); snapshot testy tokenů všech gramatik MVP procházejí; ruční volba jazyka přežije reload.
  - **Závisí na:** P2.7.1, P3.2.3
  - **Odhad:** bez odhadu
- [x] **P3.2.3** Checkpointy stavu zvýraznění a invalidace
  - **Co a proč:** „Jen viditelná část“ nezajistí správné tokeny po skoku doprostřed souboru. Stav tokenizéru se ukládá v checkpointech; po skoku za nezpracovanou část se doplní s omezeným rozpočtem skenování a neúplný výsledek se pozná. Změna komentáře mimo viewport zneplatní checkpointy za ní.
  - **Funkce:** F121 (částečně: checkpointy)
  - **Před zahájením:** rozpočet času a paměti dopočtu (ms, MiB) a velikost testovacího komentáře se zapíšou jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026:** hotovo: checkpointy a dopočet (Codex 344b9ee, 6d67600), rozpočet majitele 250 ms, 16 MiB, komentář 8 MiB; jádro i aplikace na referenční Windows VM 27. 10. prošly (`A_jump_past_an_eight_mebibyte_comment_is_colored_within_250_ms`), shoda tokenů s plným průchodem po skoku i po editaci začátku komentáře mimo viewport v testech jádra.
  - **Hotovo, když:** po skoku za několikamegabajtový komentář nebo řetězec a po editaci jeho začátku mimo viewport se tokeny výřezu po dopočtu shodují s plným referenčním průchodem; dopočet nepřekročí zapsaný rozpočet času a paměti a do jeho konce je výřez označený jako neúplný.
  - **Závisí na:** P2.7.1, P3.1.8
  - **Odhad:** bez odhadu
- [x] **P3.2.2** Komentování a chytré výběry podle jazyka
  - **Co a proč:** Přidat a odebrat komentář podle jazykového profilu. Výběr slova, řádku, obsahu závorky a rozšiřování výběru; sémantické výběry až později.
  - **Funkce:** F073, F051
  - **Stav 26. 10. 2026:** hotovo: gramatiky mají pole `comments`, Ctrl+/ přepne komentář nad více výběry podle jazyka na řádku (HTML, skript a styl uvnitř HTML, PHP, CSS, JSON, NEON), jeden krok undo; Shift+Alt+→ rozšíří výběr: slovo, obsah závorky, závorka, další pár, řádky, celý text; závorky v řetězcích a komentářích se nepočítají. Testy v jádru i v aplikaci. Sémantické výběry později.
  - **Hotovo, když:** přepnutí komentáře nad více výběry v HTML, PHP, CSS a JSON-like jazyce použije správnou značku komentáře; rozšíření výběru v závorkách vybere obsah, pak i závorky.
  - **Závisí na:** P3.2.1
  - **Odhad:** bez odhadu

### P3.3 Minimapa

- [ ] **P3.3.1** Minimapa D5
  - **Co a proč:** Vpravo zmenšený obraz textu v barvách zvýraznění, průhledný obdélník viditelné části, posun tažením. U velkého souboru vzorkovaný obraz a značky shod filtrů v celém souboru (ERROR červeně, timeout žlutě). Značky změn až později. Prototyp je `src/NksHub.Textida.P0.App/Minimap.cs`.
  - **Funkce:** F161
  - **Stav 26. 10. 2026:** částečně: minimapa na editoru i stránkované ploše v barvách zvýraznění, posun klikem i tažením, pruh se značkami shod z lišty hledání, ve velkém souboru čte jen řádky kolem obrazovky. Neověřeno: tažení ≤ 33 ms a 10GiB mapa na referenční Windows VM (testy napsané, Perf/Large), vzhled. Stránkované UTF-16/32 zatím bez mapy.
  - **Hotovo, když:** tažení v minimapě posouvá editor bez zpoždění nad cíl 33 ms; u 10GiB souboru se ukáže vzorkovaná mapa se značkami filtrů a nenačítá se celý soubor.
  - **Závisí na:** P3.1.1, P3.2.1
  - **Odhad:** F161 2–4 ČT (matice)

### P3.4 Inspektor bajtů

- [x] **P3.4.1** Základ inspektoru před uložením
  - **Co a proč:** Před uložením ukáže, co se v souboru skutečně změní: změněné rozsahy, zachované kódování a konce řádků, znaky, které v cílovém kódování nejdou zapsat. Rozšiřuje malý náhled z P1.3.3, nevytváří druhý. Plný inspektor je v P5.5.1.
  - **Funkce:** F169
  - **Stav 26. 10. 2026:** hotovo (2e5ac7c): inspektor ztrát vypíše znaky, řádky, sloupce, bajtové pozice, náhradní bajty a velikost před a po; volby zrušit / změnit kódování / náhrada, test se třemi znaky v CP1250. Neověřen vzhled dialogu.
  - **Hotovo, když:** u uložení do CP1250 se třemi nezapsatelnými znaky inspektor vypíše tyto znaky, jejich řádky a bajtový rozdíl a nabídne tři volby, každou s přesně určeným výsledkem: zrušit (nic se nezapíše), změnit kódování (zápis v nově zvoleném kódování) a výslovná náhrada (zápis s určenými náhradními bajty); tichý best-fit nenastane nikdy.
  - **Závisí na:** P1.3.4
  - **Odhad:** F169 2–4 ČT navíc za základ i plnou verzi (matice); podíl P3 určí PR.8.2

### P3.5 Hex editor

Plný hex editor už v MVP (rozhodnutí majitele 25. 10. 2026, kap. 5.5). Hex a text jsou dva pohledy na jeden `PagedDocument`, jednu revizi a jedno undo. Podrobný návrh je v analýze konkurence

Hotovo celku (kap. 6 P3): 10GiB soubor v hexu: skok na `end-512`, přepis 4 B, vložení 1 B, undo, redo, uložení; binární rozdíl obsahuje jen tyto rozsahy i po přepnutí text ↔ hex; hledání `50 4B ?? 04` najde stejný počet jako nezávislý průchod i po úpravě; uint32 BE zapsaný v inspektoru jde vrátit jedním undo; vše bez myši na Windows i macOS, s NVDA i VoiceOverem.

- [x] **P3.5.1** Služba `ByteSearch` v jádru
  - **Co a proč:** Nová služba hledá nad stromem úseků, i v upraveném dokumentu. Libovolné bajty včetně `0A` a `0D`, zástupné znaky na bajt (`??`) i půlbajt (`4?`), žádné dělení podle řádků, výsledek s revizí. Dnešní `PagedSearch` neumí zástupné znaky ani vzor s LF a hledá jen v původním souboru. Druhy dotazu: hex, text v kódování, číslo s typem; najít vše do karty výsledků. Vzorové hledání v `bench/NksHub.Textida.Bench/HexView.cs` není produktová služba.
  - **Funkce:** F235
  - **Stav 26. 10. 2026:** hotovo (Codex 2bf3183): `ByteSearch.Find` nad snapshotem, masky bajtů i půlbajtů, text a číselné vzory; na referenční Windows VM hledání nad řídkým 10GiB souborem před i po úpravě shodné s nezávislým průchodem.
  - **Hotovo, když:** hledání `50 4B ?? 04` v 10GiB souboru najde stejné uspořádané offsety jako nezávislý naivní průchod, před úpravou i po ní; projdou i vzory s půlbajtem, s LF a s NUL, vzor přes hranici úseků, text v kódování a číslo s typem; počet výsledků jde omezit a karta dosažení limitu ohlásí.
  - **Závisí na:** P0.1.1, P1.2.4
  - **Odhad:** F235 2–3 ČT (matice)
- [x] **P3.5.2** Hex pohled
  - **Co a proč:** Vlastní prvek nad Skia: offset / hex / text, bajtů na řádek a seskupení, formát offsetu, kódování znakové části (jednobajtová, UTF-8, UTF-16), třídy bajtů, stavový řádek hex, soubory do 50 GiB. AvaloniaHex jen jako zdroj nápadů, ne závislost. Nad bajtovým kontraktem P1.2.8, souběžně s textovou plochou P3.1.
  - **Funkce:** F230, F012
  - **Stav 26. 10. 2026:** hotovo: vlastní mřížka s offsetem, hexem a znaky, 8 až 64 bajtů na řádek, seskupení, dec/hex offset, 6 kódování znakové části, neplatný bajt jako zvýrazněné `�`, změněné bajty podbarvené a podtržené. Neměřeno: vstup→prezentace na referenční Windows VM a pohled nad 50 GiB.
  - **Hotovo, když:** výřez 64 × 16 B na náhodném místě 50GiB souboru má posun vstup → prezentace p95 ≤ 33 ms a p99 ≤ 80 ms, měřeno až po prezentaci v Avalonii na obou strojích R8 (v P0 změřené není); znaková část v UTF-8 ukáže neplatný bajt jako zvýrazněný `�`.
  - **Závisí na:** P1.2.8
  - **Odhad:** F230 2–3 ČT (matice)
- [x] **P3.5.3** Přepínání text ↔ hex
  - **Co a proč:** Přepnutí aktivní karty nic nenačítá znovu, kurzor a výběr se převedou přes bajtový offset. *Otevřít v hex*, `--hex` a `--offset`, nabídka u binárního souboru v liště, ne dialog. Karta AvaloniaEdit se převede na bajtový dokument v paměti nad původními bajty podle kontraktu P1.2.1; přepnutí není příkaz Uložit a nic nezapisuje na disk. Nezapsatelný text otevře inspektor F169.
  - **Funkce:** F231
  - **Stav 26. 10. 2026:** hotovo (2e5ac7c): Ctrl+K Ctrl+H s převodem výběru přes bajty, `--hex`/`--offset`, nastavení `files.binaryAsHex` (ask/always/never), nezapsatelný text otevře inspektor F169. Neověřen vzhled lišty.
  - **Hotovo, když:** výběr v textu se po přepnutí do hexu a zpět vrátí na stejné bajty; nepojmenovaný neuložený dokument s BOM, smíšenými EOL a vadným UTF-8 projde text → hex → text, nic se nezapíše na disk a zachová se identita, revize, stav „neuloženo“, undo i neplatné bajty; `--hex --offset 0x1F40` otevře soubor na offsetu; soubor s NUL v prvních 8 000 B nabídne hex lištou.
  - **Závisí na:** P3.5.2, P1.1.4, P3.4.1, P1.2.10
  - **Odhad:** F231 1–2 ČT (matice)
- [x] **P3.5.4** Hex úpravy
  - **Co a proč:** Přepis je výchozí, vkládání jen výslovně a viditelně (`PŘEPIS` / `VKLÁDÁNÍ`). Půlbajty, psaní ve sloupci text, schránka jako hex text i binárně. Skupiny undo. macOS nemá klávesu `Insert`, přepínač je i v menu a stavovém řádku.
  - **Funkce:** F232
  - **Stav 26. 10. 2026:** hotovo: přepis jako výchozí, vkládání výslovně (stav PŘEPIS/VKLÁDÁNÍ), půlbajty, psaní ve znakovém sloupci, schránka hex i binárně, dotaz u Delete v přepisu a u delšího vložení, souvislé psaní jeden krok undo.
  - **Hotovo, když:** `Delete` v přepisu nic nesmaže potichu a nabídne vynulovat nebo smazat; psaní souvislého úseku je jeden krok undo; vložení delší schránky než zbytek souboru se zeptá.
  - **Závisí na:** P3.5.2, P1.2.6
  - **Odhad:** F232 2–3 ČT (matice)
- [x] **P3.5.5** Společné bajtové uložení, snapshot a žurnál
  - **Co a proč:** MVP hex nesmí záviset na ukládání dokončeném až v P7. Tady vzniká společná ukládací služba pro text i hex: neměnný snapshot ukládané revize, zápis na místo, když se délka nemění, jinak stream přes dočasný soubor a atomickou výměnu. Body obnovy zápisu na místo: žurnál původních bajtů → data → potvrzení. Před zápisem na místo se původní bajty zkopírují, aby undo po uložení fungovalo; žurnál určuje, kdy se smí uvolnit. Hex část inspektoru F169 ukáže rozsahy a způsob uložení. Vzorový zápis v `HexView.cs` používá kopii souboru a `Flush`, ne produkční žurnál. P7 tuto službu jen rozšíří o velké nové bloky a souběžnou textovou editaci.
  - **Funkce:** F233
  - **Stav 26. 10. 2026:** hotovo: zápis na místě přes žurnál (staré bajty, data, potvrzení), jinak stream přes dočasný soubor; uložení na pozadí ze snímku, zrušitelné Esc; staré snímky po uložení na místě čtou původní bajty (kopie přepisovaných rozsahů), čtení a vlastní zápis synchronizované zámkem a počítadlem verzí; po zápisu se ověří hash disku. Brána 10 GiB na referenční Windows VM prošla (přepis 4 B na `end-512`, vložení 1 B, undo, redo, uložení na místě i streamem, binární rozdíl jen očekávané rozsahy, `50 4B ?? 04` shodně s naivním průchodem). Přepnutí text ↔ hex v 10GiB testu není (jen UI test na malém souboru). Pád těsně před potvrzením uložení jen simulovaný.
  - **Hotovo, když:** zápis na místo i streamové uložení projdou testem: po přepisu 4 B v 10GiB souboru se uloží na místo, po vložení 1 B streamem, a SHA-256 obou výsledků odpovídá nezávislému výpočtu; pád injektovaný po každém kroku žurnál → data → potvrzení jde po startu dokončit nebo vrátit, včetně pádu těsně před a těsně po potvrzení žurnálu; injektované zdržení flush aspoň 1,5 s neblokuje UI, které dál splní limity kap. 3, uložení jde během něj zrušit a potvrzení uložení nepřijde dřív než flush; zrušení, cizí změna podkladu a plný disk nepoškodí originál; undo po uložení vrátí původní bajty a výsledek po restartu je stejný.
  - **Závisí na:** P3.5.4, P1.5.1, P3.4.1
  - **Odhad:** F233 1–2 ČT (matice); přesunutá část F154 z P7 bez odhadu, podíl a odečet z P7 určí PR.8.2
- [x] **P3.5.6** Inspektor dat
  - **Co a proč:** Panel na požádání: celá čísla 8–64 bit, LEB128, float16–64, datum a čas, znaky, GUID, přepínač LE / BE. Enter na řádku upraví hodnotu jako jeden krok undo.
  - **Funkce:** F234
  - **Stav 26. 10. 2026:** hotovo: čísla 8–64 bit, LEB128, float16–64, čtyři typy data a času, znaky, GUID, LE/BE, zápis hodnoty Enterem jako jeden krok undo, chyba rozsahu hned, neúplná hodnota na konci souboru označená.
  - **Hotovo, když:** každý deklarovaný typ má test s krajními hodnotami v LE i BE; hodnota uint32 BE zapsaná Enterem se objeví v mřížce a jde vrátit jedním undo; chyba rozsahu se ukáže hned; neúplná hodnota na konci souboru se ukáže jako neúplná, ne jako nula.
  - **Závisí na:** P3.5.4
  - **Odhad:** F234 1,5–2 ČT (matice)
- [x] **P3.5.7** Skok na offset s výrazy
  - **Co a proč:** Ctrl+G v hex kartě: `0x1F40`, `1F40h`, `$1F40`, `+0x100`, `end-512`, `50%`, `4K`, závorky a `+ - * /`, `sel`, `val`. Náhled cíle před potvrzením, historie skoků sdílená s F174.
  - **Funkce:** F236
  - **Stav 26. 10. 2026:** hotovo (2e5ac7c): `OffsetExpression`, Ctrl+G s náhledem cíle (offset, desítkově, bajt nebo důvod neplatnosti), Alt+← zpět.
  - **Hotovo, když:** každý zápis z analýzy konkurence má test; `end-512` u 10GiB souboru skočí přesně; Alt+← vrátí předchozí místo.
  - **Závisí na:** P3.5.2, P2.1.4
  - **Odhad:** F236 0,5–1 ČT (matice)
- [ ] **P3.5.8** Přístupnost hex: základ
  - **Co a proč:** Automation peer mřížky: čtečka čte offset, bajt, hodnotu a znak. Posun po bajtu, skupině, řádku, stránce a výběr klávesnicí. Plná verze v P9.1.2.
  - **Funkce:** F248
  - **Stav 26. 10. 2026:** částečně: prvek mřížky hlásí „offset, bajt, hodnota, znak“ a výběr a změnu ve čtyřech jazycích; NVDA a VoiceOver neověřeny (potřebují člověka).
  - **Hotovo, když:** celý hex test celku P3.5 projde bez myši v matici R10 (PR.4); NVDA i VoiceOver přečtou při pohybu kurzoru, výběru a po úpravě stejný offset a hodnotu podle předem zapsaného očekávání; důkazy textové plochy se nepřebírají.
  - **Závisí na:** P3.5.2, P1.8.1
  - **Odhad:** F248 1–2 ČT za základ i plnou verzi (matice); podíl P3 určí PR.8.2
- [ ] **P3.5.9** Text nad upraveným bajtovým stromem
  - **Co a proč:** Po hex úpravě jde přepnout na text. Textový pohled i textové hledání musí číst aktuální revizi, ne původní obsah disku. Tady vzniká čtecí textový adaptér nad stromem úseků a hledání nad aktuální revizí. Od spiku měření platí totéž i po textové editaci do 100 MiB (P3.1.11): hledání najde vložený text a čísla řádků shod bere z indexu po úpravě (P1.2.8). P7.1.6 to ověří nad 10 GiB; P7 nesmí být podmínkou pravdivého textového náhledu v MVP.
  - **Funkce:** F231 (částečně: text po hex úpravě), F154 (částečně: hledání po textové úpravě do 100 MiB)
  - **Stav 26. 10. 2026:** částečně: textový pohled i hledání čtou aktuální revizi; test po přepisech, vloženích a mazáních (bajty, řádky, offsety a čísla řádků shod proti naivnímu průchodu). Large varianta 5 000 úprav 100 MiB napsaná, 10GiB hex úpravy neověřeny.
  - **Hotovo, když:** po přepisu a vložení bajtů v 10GiB souboru ukáže textový pohled nové bajty a textové hledání najde vložený text; offsety shod souhlasí s naivním průchodem nad výsledkem; po 5 000 textových úpravách souboru 100 MiB najde hledání i vložený text, offsety shod souhlasí s naivním průchodem a čísla řádků shod s úplným průchodem výsledku.
  - **Závisí na:** P3.5.4, P3.1.4, P3.1.11
  - **Odhad:** bez odhadu

### P3.6 Zbytek funkcí MVP

Funkce s vydáním MVP, které kap. 6 nejmenuje a P3 je bere jako „zbytek funkcí s vydáním MVP podle matice“.

- [ ] **P3.6.1** Přepínače zobrazení, zalamování a písmo
  - **Co a proč:** Skryté znaky, čísla řádků, odkazy, aktuální řádek. Zalamování a pravý okraj pro běžné soubory, obří řádky bez automatického zalamování. Písmo, zoom a náhradní písmo pro Unicode.
  - **Funkce:** F065, F066, F069
  - **Stav 26. 10. 2026:** částečně: příkazy `nks.view.*` v menu Zobrazit se zaškrtnutím a klávesami (skryté znaky, čísla řádků, aktuální řádek, pravý okraj, odkazy, minimapa), nastavení s původem, zalamování vypnuté nad 50 000 znaků s hlášením ve stavovém řádku, náhradní písma, zoom Ctrl+kolečkem. Neověřeno: 100MiB řádek na referenční Windows VM a vzhled.
  - **Hotovo, když:** zapnutí skrytých znaků ukáže CR, LF, tabulátor a mezery bez změny dokumentu; řádek 100 MiB se nikdy automaticky nezalomí; znak mimo hlavní písmo se vykreslí náhradním písmem.
  - **Závisí na:** P1.4.1, P3.1.1
  - **Odhad:** bez odhadu
- [x] **P3.6.2** Záložky v textu
  - **Co a proč:** Záložky jako kotvy, navigace mezi nimi, trvalé uložení v relaci (`08` kap. 6).
  - **Funkce:** F064
  - **Stav 26. 10. 2026:** hotovo: `Bookmarks` jako kotvy na začátku řádku, Ctrl+F2 / F2 / Shift+F2 v menu Hledat, značka v okraji obou ploch (i bez čísel řádků), uložení v relaci přes žurnál (rozložení záznamu 3, starší se čtou dál); test posunu po vložení řádků i obnovy po restartu. Neověřeno v běžící aplikaci: vykreslení značky a klávesy.
  - **Hotovo, když:** záložka se po vložení řádků nad ní posune s textem a přežije restart přes žurnál.
  - **Závisí na:** P1.2.5, P1.5.1
  - **Odhad:** bez odhadu
- [x] **P3.6.3** Panely a okno výstupu
  - **Co a proč:** Panely na požádání (Soubory vlevo, Code Explorer vpravo vedle minimapy), celá obrazovka. Výstupy úloh a hledání jsou oddělené podle účelu a zůstanou čitelné i po pádu procesu, který je psal.
  - **Funkce:** F134, F135
  - **Stav 26. 10. 2026:** hotovo: výchozí stav jen editor; Dokumenty Ctrl+B, Soubory Ctrl+Shift+E, Výstup Ctrl+Shift+Y (zavře i Esc), F11 celá obrazovka; `OutputChannels` s kanály Úlohy a Hledání jako soubory v profilu (řádek = zápis), test se skutečným zabitým procesem; review D5 `p3/ui-review-d5-panels.md`. Code Explorer vpravo patří do P4. macOS: F11 zabírá systém, nativní zkratka celé obrazovky chybí.
  - **Hotovo, když:** výchozí stav je jen editor; každý panel jde otevřít z menu Zobrazit nebo klávesou a zavřít; výstup spadlého nástroje zůstane v okně výstupu.
  - **Závisí na:** P1.1.3
  - **Odhad:** bez odhadu
- [ ] **P3.6.5** Nápověda programu
  - **Co a proč:** Vlastní nápověda hotových funkcí, doplňovaná s vydáními. Nápověda PSPadu se nepoužije. Existence odstavce sama nevysvětluje scénář.
  - **Funkce:** F142
  - **Stav 26. 10. 2026:** částečně: okno nápovědy s hledáním, F1 podle kontextu, 69 témat v cs/en/sk/de jako vložené zdroje, test, že každý registrovaný příkaz má téma ve všech jazycích. Chybí: postupy ověřené na skutečném buildu a snímky (aplikace se na vývojovém PC nespouští), kontrola sk/de rodilým mluvčím.
  - **Hotovo, když:** každý příkaz z registru, který je v MVP, má v nápovědě postup ověřený na skutečném buildu; nápověda popisuje aktualizaci, obnovu po pádu, omezení stránkovaného a hex režimu a konkrétní chybová hlášení s tím, co s nimi dělat; je dostupná bez sítě; existuje v jazycích, které pro MVP určila P1.1.7. Rozšíření na celou 1.0 je P9.2.3.
  - **Závisí na:** P1.6.1, P1.1.7
  - **Odhad:** bez odhadu

### P3.7 První distribuce

- [ ] **P3.7.1** Instalační balíky a integrace do systému
  - **Co a proč:** První distribuce pro Windows a macOS. Základní integrace: kontextové menu Průzkumníka a asociace přes registr na Windows, Finder Quick Action nebo Services a Launch Services na macOS. Asociace souborů jsou volitelné.
  - **Funkce:** F112, F113
  - **Hotovo, když:** instalace na čistém Windows a čistém Macu (Apple Silicon i Intel) proběhne na každém systému ze seznamu P1.1.6 včetně Windows 10 22H2 a macOS 14, „Otevřít v …“ z Průzkumníka i Finderu otevře soubor v běžící instanci; odinstalace odebere asociace; vlastní obrazovky instalátoru jsou v jazycích podle P1.1.7 a test pokryje volbu jazyka, chybu, aktualizaci a odinstalaci; balík nese konečné jméno a identifikátory z P3.7.6; záznam uvádí hash balíku a verzi OS.
  - **Závisí na:** PR.1.2, PR.3.1, P1.1.4, P1.1.7, P3.7.6
  - **Odhad:** bez odhadu
- [ ] **P3.7.2** Aktualizace a návrat verze
  - **Co a proč:** Kontrola aktualizací a bezpečný návrat na předchozí verzi. Původní telemetrie PSPadu se nepřebírá a editor žádnou telemetrii nemá (PR.6.4). Aktualizace chrání program i neuložená data i při chybě. P9.6.1 tentýž aktualizátor rozšíří o pluginy; nový nevznikne.
  - **Funkce:** F001
  - **Hotovo, když:** aktualizace na novější build a návrat na předchozí projdou na každém systému ze seznamu P1.1.6 a na všech třech RID bez ztráty nastavení a žurnálu; po aktualizaci se na macOS ověří podpis, časové razítko a notarizační ticket nového balíku i přibalených procesů, na Windows ověření původu nového balíku; balík s neplatným podpisem nebo neověřeným původem se odmítne; poškozený balík se pozná před výměnou; přerušená výměna se po startu dokončí nebo vrátí; návrat na starší verzi zachová kompatibilitu schématu nastavení a žurnálu podle P1.7.1; editor neposílá nic kromě dotazu na verzi.
  - **Závisí na:** P3.7.1
  - **Odhad:** bez odhadu
- [ ] **P3.7.3** Ověření cílů MVP
  - **Co a proč:** Všechny cíle sloupce MVP z kap. 3 na referenčních strojích R8, na Windows i macOS, nad distribuovaným AOT buildem.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** každý řádek MVP tabulky kap. 3 **prošel** se záznamem podle PR.2.2 (percentil, počet vzorků, cache, režim, SHA buildu, relace, metoda prezentace), včetně editace textu 100 MiB; start a paměť prázdné aplikace jsou z přihlášené relace s platnou prezentací, vzorek se selháním prezentace je zahozený; cold na referenční Windows VM má zapsaný význam „cold ve virtuálu, cache hostitele neznámá“ a stejný protokol použije P10.1.1; nesplněný nebo neměřený cíl bránu nesplňuje a má rozhodnutí majitele podle kap. 9 (změnit backend nebo příslib).
  - **Závisí na:** PR.2.2, P3.7.1, P3.1.10
  - **Odhad:** bez odhadu
- [ ] **P3.7.4** Uživatelské scénáře na MVP
  - **Co a proč:** Kap. 6 P3 žádá reálnou práci uživatelů. Kap. 9 a R5 žádají ověřit odlišující postupy (F165–F169) uživatelskými scénáři, ne názvy tlačítek. Počet uživatelů plán neurčuje; návrh revize jsou tři nezávislí uživatelé, majitel ho zatím neschválil.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** scénáře a účastníci jsou určení před testem; účastníci dokončí scénáře otevřít, upravit, obnovit po pádu a hex; žádný nález se ztrátou dat ani blokující chyba nezůstal otevřený; ostatní nálezy jsou zařazené do TODO.
  - **Závisí na:** P3.7.1
  - **Odhad:** bez odhadu
- [ ] **P3.7.5** Brána vydání MVP
  - **Co a proč:** Vydání MVP nesmí projít jen přes build. Brána čeká na všechny povinné kontroly: výkon, odolnost, bezpečnost, přístupnost, IME, aktualizace, běh na třech RID a kompatibilitu R10.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** jsou splněné P3.7.3, P3.7.4, PR.1.2, PR.1.3, PR.2.4, PR.3.1, PR.4.1, PR.4.2, PR.6.1, PR.6.4, P3.1.2 (stránkovaný UTF-16 a UTF-32) a hex test celku P3.5, každá se **záznamem pro MVP** na SHA buildu vydání. Platný rozsah: obrazovky a funkce MVP, bez pluginů; kompatibilitní matice R10 je úplná pro build MVP a obsahuje Windows 10 22H2, Windows 11 22H2 a macOS 14 na obou Mac RID. Žádná položka není ve stavu „neověřeno“; bez strojů z P1.1.6 brána neprojde.
  - **Závisí na:** P3.7.3, P3.7.4, P3.7.2, P3.5.8, P3.1.2, PR.1.2, PR.1.3, PR.2.4, PR.3.1, PR.4.1, PR.4.2, PR.6.1, PR.6.4
  - **Odhad:** bez odhadu
- [ ] **P3.7.6** Konečné jméno a identifikátory
  - **Co a proč:** Pracovní název je Textida (majitel 26. 10. 2026), nahradil „NKS PSPad“, který licence PSPadu pro vydání vylučuje (body 1 a 2). Domény textida.com, .cz, .app a .dev byly 26. 10. 2026 volné a vyhledávání nenašlo produkt s tímto jménem; ochranné známky v registrech zatím neověřené. Jádro ani prototypy na konečné potvrzení nečekají, veřejné balíky ano.
  - **Kroky:**
    - [x] rešerše známek „Textida“ (26. 10. 2026): Rešerše známek 26. 10. 2026 v TMview (EUIPO, ÚPV, USPTO, WIPO a další národní úřady), podobnostní hledání „textida“: shodné znění TEXTIDA mají jen dvě **zaniklé** francouzské známky (INPI 950860 z 1976 a 3270089 z 2004, třídy 24 a 25, textil a oděvy). Platná podobná znění ve třídách 9 nebo 42 (software): **Textima** (DPMA 3020140525968, platná, třídy 7, 37, 42) a TEXTINA (CNIPA, Čína, třídy 9 a 42). Ve třídě 9 ani 42 v EU platná shodná známka není. Není to právní posudek; před registrací vlastní známky ji má posoudit odborník.
    - rozhodnout, zda přihlásit vlastní známku (EUIPO nebo ÚPV, třídy 9 a 42), a nechat posoudit podobnost s Textima
    - zaregistrovat domény, které majitel zvolí
    - přejmenovat jmenné prostory `NksHub.Textida.*`, sestavení, bundle ID a ID příkazů na konečné jméno
  - **Funkce:** žádná (rozhodnutí a záznam)
  - **Hotovo, když:** majitel potvrdil Textidu jako konečné jméno (R2) po rešerši známek a rozhodl o vlastní přihlášce a v repozitáři je zapsané: jméno, bundle ID pro macOS, identifikátory instalátoru a aktualizací pro Windows, jméno balíku SDK a prefix ID příkazů, pokud se mění; dokud rozhodnutí chybí, platí pracovní název a žádný veřejný balík nevznikne.
  - **Závisí na:** žádná
  - **Odhad:** bez odhadu (rozhodnutí majitele)

## P3b – základ pluginů

Odhad etapy 18–28 ČT. Hotovo, když: JSON formátovač běží jako vestavěný plugin proti AOT buildu na obou OS; pád pluginu editor přežije a ukáže viníka; návrh ze zastaralé revize se odmítne; plugin bez oprávnění dostane chybu; stažený ZIP projde karanténou macOS.

Podmínky: registr příkazů F195 z P1, model revizí z kap. 5.1 a služba náhledu změn z P2. Nálezy revize R01–R16 jsou v revizi

Odhady položek F196–F201 a F204 dávají 13–21 ČT. Zbytek rozpočtu etapy 5–7 ČT patří distribučnímu pokusu, vnitřnímu SDK, prvnímu pluginu a API pro časné nástroje (P3b.5, P3b.3.5); zapsaný je jednou, u P3b.5.4. Podíly jednotlivých položek ze zbytku určí PR.8.2 před zahájením P3b. Etapa začíná distribučním pokusem P3b.5.4, protože hlavní riziko je podepsaný proces pluginu na macOS.

### P3b.1 JSON-RPC a procesy

- [ ] **P3b.1.1** Sdílená vrstva JSON-RPC a protokolové adaptéry
  - **Co a proč:** Jeden dispatcher JSON-RPC 2.0, proudy a správa procesů pro pluginy, MCP, ACP i LSP. Stejné JSON-RPC neznamená stejnou sémantiku, proto rámování, inicializace, rušení a průběh patří protokolovým adaptérům:
    - LSP: hlavička `Content-Length` v bajtech, `$/cancelRequest`, `$/progress`
    - MCP stdio: zprávy oddělené novým řádkem, `notifications/cancelled`
    - ACP stdio: zprávy oddělené novým řádkem, `session/cancel` pro tah a vlastní rušení požadavků
    - protokol pluginů: řádkové rámování a vlastní kontrakt handshake, verze, chybových stavů a rušení
    - dispatcher nesmí poslat zprávu jednoho protokolu jinému ani čekat stejnou odpověď po zrušení; volbu knihovny podložit AOT testem (StreamJsonRpc je jen částečně AOT safe, R09)
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - testy: vadný rámec, EOF, částečný a slepený rámec, souběžná obousměrná volání, zahlcení `stderr`, časový limit
    - obě strany současně volají se stejným číselným ID, přijde zrušení a pozdní odpověď, pak restart
    - adaptéry LSP, MCP, ACP a pluginů nad stejným dispatcherem
  - **Stav 26. 10. 2026:** částečně: dispatcher a adaptéry LSP, MCP, ACP a pluginů (`NksHub.Textida.Rpc`), 87 testů rámování, rušení a souběhu. Neověřeno: běh pod NativeAOT a macOS; ACP `$/cancel_request` podle návrhu, ne specifikace.
  - **Hotovo, když:** testy z kroků procházejí pod NativeAOT na Windows i macOS pro všechny čtyři adaptéry; žádná odpověď se nepřiřadí cizímu požadavku a žádný zápis se po restartu nezopakuje.
  - **Závisí na:** P1.1.2
  - **Odhad:** v rámci F196
  - **Umístění:** jádro
- [ ] **P3b.1.2** Hostitel pluginů: pády, restart a resynchronizace
  - **Co a proč:** Proces na plugin nebo balíček vestavěných pluginů, hlídání, restart a vypnutí po opakovaném pádu, log. Ping je oddělený od čekání na rozhodnutí uživatele. Automatický restart nezopakuje zápis ani nahrání (R10). Po restartu se stav dokumentů a čekajících návrhů znovu synchronizuje. Spojuje části P3b.1.3 a P3b.1.4.
  - **Funkce:** F196
  - **Stav 26. 10. 2026:** částečně: `NksHub.Textida.PluginHost` s testy reálných podprocesů (pád, restart s prodlevou, vypnutí po pádech, zamrznutí, čekání na uživatele, resynchronizace, žádné opakování zápisu). Hodnoty restartu a časových limitů nejsou určené (povinné parametry), hostitel není zapojený do aplikace.
  - **Hotovo, když:** pád i zamrznutí pluginu editor přežije a ukáže, který plugin to byl; plugin, který čeká na rozhodnutí uživatele, hlídání neukončí; po restartu proběhne nový handshake, ztracené události se pluginu ohlásí a žádný zápis ani nahrání se nezopakuje.
  - **Závisí na:** P3b.1.3, P3b.1.4
  - **Odhad:** F196 4–6 ČT (matice) za P3b.1.1–P3b.1.4
  - **Umístění:** jádro
- [ ] **P3b.1.3** Inicializace a životní cyklus procesů pluginů
  - **Co a proč:** Líná aktivace (`onCommand`, `onLanguage`, `onFileType`, `onPanel`), handshake s verzí API, ukončení a čerpání `stderr`.
  - **Funkce:** F196 (částečně: aktivace a procesy)
  - **Stav 26. 10. 2026:** částečně: líná aktivace, handshake s chybou verze, vyčerpání `stderr`, vypnutí bez osiřelých procesů. Chybí zapojení do aplikace.
  - **Hotovo, když:** plugin se spustí až při prvním spouštěči aktivace; nesouhlasná verze API v handshaku skončí srozumitelnou chybou; zavření editoru ukončí všechny procesy pluginů bez osiřelých procesů.
  - **Závisí na:** P3b.1.1
  - **Odhad:** v rámci F196 (viz P3b.1.2)
  - **Umístění:** jádro
- [ ] **P3b.1.4** Rušení, kvóty a zpětný tlak
  - **Co a proč:** Limity velikosti zpráv, souběhu, počtu výsledků a paměti relace se zpětným tlakem (R08); rušení požadavků podle adaptéru z P3b.1.1.
  - **Funkce:** F196 (částečně: kvóty a rušení)
  - **Před zahájením:** hodnoty všech limitů se zapíšou jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026:** částečně: limity zpráv, souběhu, výsledků a paměti relace se zpětným tlakem a vlastní chybou, testy. Hodnoty limitů nejsou zapsané (vstupní rozhodnutí z „Před zahájením“ zůstává otevřené).
  - **Hotovo, když:** překročení každého limitu má vlastní test a vrátí určenou chybu, editor nezamrzne a ostatní pluginy běží dál; zrušený požadavek uvolní své zdroje.
  - **Závisí na:** P3b.1.3
  - **Odhad:** v rámci F196 (viz P3b.1.2)
  - **Umístění:** jádro

### P3b.2 Manifest a UI příspěvky

- [ ] **P3b.2.1** Manifest a deklarativní příspěvky
  - **Co a proč:** `nkspad-plugin.json` s JSON Schema, validace před spuštěním kódu. Příspěvky: příkazy, menu (Nástroje › Pluginy a kontextová menu), klávesy, nastavení, jazyky a gramatiky, převodníky, klipy, LOG vzory, externí nástroje. Bez `main` se plugin načte bez procesu. `executable`, `args` a pracovní adresář pro skript bez kompilace.
  - **Funkce:** F197
  - **Stav 26. 10. 2026:** částečně: `nkspad-plugin.json` se kontroluje před spuštěním, datový plugin přidá gramatiku a příkaz bez procesu, příklad v Pythonu `samples/plugins/python-hello` ověřený testem. Chybí: napojení kláves a nastavení pluginu do aplikace, spouštění datových příkazů, `maxGrammarBytes` a schválení tvaru převodníků a klipů.
  - **Hotovo, když:** neplatný manifest se odmítne s chybou před spuštěním čehokoli; datový plugin přidá gramatiku a příkaz bez procesu; dokumentace má příklad pluginu v Pythonu s výslovnou cestou k interpretu, ověřený spuštěním.
  - **Závisí na:** P1.6.1, P3.2.1
  - **Odhad:** F197 2–3 ČT (matice)
  - **Umístění:** jádro
- [ ] **P3b.2.2** UI příspěvky ve vzhledu D5
  - **Co a proč:** Editor kreslí z dat pluginu panel se stromem nebo seznamem, nejvýš jednu položku stavového řádku na plugin, výběr ze seznamu, vstup, potvrzení, oznámení a úlohy s průběhem a zrušením. Plugin nekreslí vlastní UI, nemá webview ani příspěvek do horní lišty.
  - **Funkce:** F200
  - **Stav 26. 10. 2026:** částečně: panel se stromem nebo seznamem, jedna položka stavového řádku na plugin, výběr ze seznamu (paleta), vstup a potvrzení (stávající dialogy), oznámení jako lišta, úlohy s průběhem a zrušením; po konci procesu vše zmizí; testy protokolu i headless testy aplikace s reálným procesem pluginu. Limity a časové limity jsou zástupné hodnoty v `PluginLimitsPlaceholders.cs` (rozhodnutí majitele otevřené); UI volání bez kontroly oprávnění; vzhled neověřen.
  - **Hotovo, když:** panel pluginu jde otevřít z palety, je ovladatelný klávesnicí a čtečkou; zrušení úlohy pluginu uvolní její zdroje.
  - **Závisí na:** P3b.1.2, P3.6.3
  - **Odhad:** F200 2–3 ČT (matice)
  - **Umístění:** jádro

### P3b.3 Dokumenty a úpravy

- [ ] **P3b.3.1** API dokumentů v1: čtení a revize
  - **Co a proč:** Seznam dokumentů, revize, čtení rozsahů po bajtech, převod pozic, hledání jádrem, výběr. Čtení se váže na revizi; nedostupná revize vrátí konflikt. Surové bajty odděleně od dekódovaného textu s hranicemi znaků, nikdy tichý průchod přes náhradní znaky (R01). 64bitové pozice, částečný index se nehlásí jako řádek nula. Snapshoty jsou v P3b.3.3, události v P3b.3.4.
  - **Funkce:** F198
  - **Stav 26. 10. 2026:** částečně: `DocumentApi` v PluginHost – seznam dokumentů, čtení bajtů a textu po revizi, převod pozic, hledání, výběr; stará revize = konflikt, neplatné UTF-8 v `invalid`, neúplný index = `PositionUnknown`; 101 testů Rpc. Neověřeno nad 10GiB stránkovaným dokumentem, nezapojeno do aplikace; limity v `DocumentApiOptions` bez hodnot.
  - **Hotovo, když:** plugin přečte rozsah stránkovaného 10GiB dokumentu bez načtení celého souboru; čtení nad starou revizí vrátí konflikt; soubor s neplatným UTF-8 projde čtením a návratem úpravy bez změny okolních bajtů.
  - **Závisí na:** P3b.1.2, P1.2.4
  - **Odhad:** F198 2–3 ČT (matice) za P3b.3.1, P3b.3.3 a P3b.3.4; revize ho považuje za optimistický, rozpad může odhad zvýšit
  - **Umístění:** jádro
- [ ] **P3b.3.3** Snapshoty a mapování pozic v API
  - **Co a proč:** Omezené snapshoty revize s expirací a převod pozic mezi revizemi přes záznam změn z P1.2.4.
  - **Funkce:** F198 (částečně: snapshoty)
  - **Stav 26. 10. 2026:** částečně: snapshoty s expirací a kvótou na plugin, převod pozic z revize N na aktuální, nejednoznačné = konflikt. Hodnoty kvóty a doby života otevřené.
  - **Hotovo, když:** čtení z prošlého snapshotu vrátí určenou chybu; pozice z revize N se převede na N+k, a když to nejde jednoznačně, vrátí konflikt; překročení počtu snapshotů na plugin vrátí chybu kvóty.
  - **Závisí na:** P3b.3.1
  - **Odhad:** v rámci F198 (viz P3b.3.1)
  - **Umístění:** jádro
- [ ] **P3b.3.4** Události dokumentů a jejich obnova
  - **Co a proč:** Události s rozsahy a revizemi, ne s textem. Plugin, který události nestíhá nebo je ztratil po restartu, dostane signál k nové synchronizaci.
  - **Funkce:** F198 (částečně: události)
  - **Stav 26. 10. 2026:** částečně: události s rozsahy a revizemi bez textu, při přetečení fronty jedna `document/eventsLost`, po restartu resync revizí. Kapacita fronty otevřená; zahlcení ověřeno pozdrženým odesíláním.
  - **Hotovo, když:** pomalý plugin dostane po přetečení fronty událost „ztracené události“ s aktuální revizí; po restartu hostitele plugin zjistí aktuální revize všech zpřístupněných dokumentů.
  - **Závisí na:** P3b.3.1, P3b.1.2
  - **Odhad:** v rámci F198 (viz P3b.3.1)
  - **Umístění:** jádro
- [ ] **P3b.3.5** API pro časné nástroje: symboly, reveal, snippety, kurzory a `Tab`
  - **Co a proč:** Code Explorer (P4.4.1), Emmet (P4.5.2) a cesta klíče (P4.6.4) mají být skutečné pluginy od začátku. Chybí jim poskytovatel symbolů a reveal (skok a zvýraznění místa), vložení snippetu s kurzory a předání neobslouženého `Tab` zpět jádru. Každý kontrakt nese revizi.
  - **Funkce:** žádná (infrastruktura API)
  - **Stav 26. 10. 2026:** částečně: `symbols/publish` jen pro aktuální revizi (starší = konflikt), zastaralá osnova označená a klik najde text, kde je teď; `editor/reveal` i klik v osnově přes `MainWindow.RevealBytes` (test shody bajtů s „é“); `snippet/insert` s kurzory do všech výběrů jako jeden krok undo; `editor/tabHandler` – nezpracovaný, chybný nebo pomalý Tab odsadí jako bez pluginu. Limity jsou zástupné hodnoty v `PluginLimitsPlaceholders.cs`. Chybí: přeskakování zástupných míst snippetu, odsazení dalších řádků snippetu; Tab a klik v běžící aplikaci neověřeny. Nález agenta o tichém nepoužití úpravy bez platné hranice znaku se nepotvrdil: jádro vrací `ConflictReason.InvalidRange` a obě cesty úprav z pluginu (`edit/apply`, `snippet/insert`) ho hlásí chybou.
  - **Hotovo, když:** testovací plugin publikuje symboly pro revizi a starší odpověď se nezobrazí jako aktuální; reveal trefí stejné bajty jako klik v osnově; snippet vloží text s kurzory do všech výběrů jako jeden krok undo; neobsloužený `Tab` odsadí jako bez pluginu.
  - **Závisí na:** P3b.3.1, P3b.2.1, P1.6.2
  - **Odhad:** bez odhadu; podíl ze zbytku rozpočtu P3b u P3b.5.4 určí PR.8.2 před zahájením P3b; že se do zbytku vejde vedle distribuce, SDK a prvního pluginu, doložené není
  - **Umístění:** jádro
- [ ] **P3b.3.2** Úpravy z pluginu
  - **Co a proč:** `edit/propose` jde do služby náhledu změn z P2.5.2 (revize F178 přijde v P6). `edit/apply` jen s oprávněním `document.edit.direct`, jako jeden krok undo. Úpravy v dávce atomicky a bez překryvů.
  - **Funkce:** F199
  - **Stav 26. 10. 2026:** částečně: `edit/apply` s oprávněním `document.edit.direct` proti revizi jako jedna transakce a jeden krok undo, překryv odmítne dávku, `batchId` brání opakování po pádu; `edit/propose` končí na `IEditProposals` (náhled změn P2.5.2 v repu není).
  - **Hotovo, když:** návrh ze zastaralé revize se odmítne; dávka s překryvem se odmítne celá; přímá úprava bez oprávnění vrátí chybu JSON-RPC; dávka přerušená restartem hostitele se nepoužije napůl ani dvakrát.
  - **Závisí na:** P3b.3.1, P2.5.2
  - **Odhad:** F199 1–2 ČT (matice)
  - **Umístění:** jádro

### P3b.4 Důvěra a správa pluginů

- [ ] **P3b.4.1** Oprávnění a důvěra
  - **Co a proč:** Dialog při instalaci, odebrání oprávnění, záznam volání bez obsahu dokumentů a tokenů. Oprávnění se váží na identitu dokumentu a rozsah, ne na aktivní kartu; tajemství má každý plugin zvlášť; povolený příkaz nesmí předat zápis dál (R05). UI říká, že oprávnění nejsou sandbox.
  - **Funkce:** F201
  - **Stav 26. 10. 2026:** částečně: granty vázané na identitu dokumentu nebo cílový plugin, bez oprávnění chyba `PermissionDenied`, odebrání za běhu zruší čekající (`Revoked`) a zastaví události, povolený příkaz nepředá zápis (`Delegated`), tajemství zvlášť pro plugin, záznam volání bez parametrů a výsledků; 108 testů Rpc. Chybí uložení grantů, UI a systémové úložiště tajemství.
  - **Hotovo, když:** plugin bez oprávnění dostane chybu, ne prázdný výsledek; odebrání grantu za běhu zastaví nové požadavky a zruší čekající; povolený příkaz nepředá zápis jinému pluginu; záznam volání neobsahuje text dokumentu ani tokeny.
  - **Závisí na:** P3b.1.2
  - **Odhad:** F201 1–2 ČT (matice)
  - **Umístění:** jádro
- [ ] **P3b.4.2** Správce pluginů
  - **Co a proč:** Instalace ze složky a ZIP, zapnutí, vypnutí, odinstalace, log. ZIP: kontrola cest, odkazů, velikosti rozbalení a kolize ID. Nová verze se rozbalí vedle staré, po ověření se přepne, starou lze vrátit (R15). Nic se neaktivuje jen otevřením projektu.
  - **Funkce:** F204
  - **Hotovo, když:** ZIP s cestou `../`, se symbolickým odkazem ven ze složky, s kolizí ID a s rozbalením nad kvótu se každý odmítne s vlastní chybou; nepovedená aktualizace pluginu vrátí předchozí verzi; odinstalace nesmaže uživatelská data jiných pluginů.
  - **Závisí na:** P3b.2.1, P3b.4.1, P3b.5.4
  - **Odhad:** F204 1–2 ČT (matice)
  - **Umístění:** jádro

### P3b.5 SDK, distribuce a první plugin

- [ ] **P3b.5.4** Distribuční pokus s minimálním podepsaným procesem
  - **Co a proč:** První úkol P3b. Hlavní riziko je podepsaný proces pluginu na macOS, proto se ověří dřív, než vznikne správce ZIP a oprávnění. Minimální AOT proces, který odpoví na `initialize`, se přibalí do podepsaného a notarizovaného balíku editoru a zvlášť se stáhne jako ZIP. Skript a evidence pokusu se znovu použijí v P3b.5.2 a P8b.1.1; vývoj harnessu se účtuje jen tady.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** na čistém Macu s Apple Silicon i Intel (macOS 14 a novější ze seznamu P1.1.6) a na čistém Windows se přibalený minimální proces spustí po instalaci; stažený ZIP s podepsaným a notarizovaným procesem projde karanténou macOS a proces se spustí; skript pokusu je zapsaný jako použitelný postup pro P3b.5.2. Srozumitelná chyba je přijatelná jen u předem určeného nepodporovaného případu (například nepodepsaný proces); když se zablokuje **podporovaný** podepsaný proces, pokus zůstává otevřený s popsanou blokací a P3b.4.2 ani P3b.5.2 se nezahájí, dokud nevznikne funkční postup.
  - **Závisí na:** PR.3.1, P1.1.2
  - **Odhad:** zbytek rozpočtu P3b 5–7 ČT za P3b.5.1–P3b.5.4 a P3b.3.5
  - **Umístění:** jádro
- [ ] **P3b.5.1** Vnitřní základ C# SDK
  - **Co a proč:** Interní knihovna pro vlastní pluginy: AOT, System.Text.Json se source generatorem, bez reflexe. Plugin v C# má asi 2,7 MB a startuje za 7 ms (spike). Veřejné SDK je až v P8b.
  - **Funkce:** F202 (částečně: vnitřní základ)
  - **Stav 26. 10. 2026:** částečně: `NksHub.Textida.PluginSdk` (source generator, bez reflexe) a `samples/csharp-hello`; test publikuje NativeAOT win-x64 a ověří odpověď na `initialize`. osx-arm64 a osx-x64 na Windows AOT publikovat nejde, neověřeno.
  - **Hotovo, když:** ukázkový plugin z interní knihovny se publikuje AOT pro win-x64, osx-arm64 a osx-x64 a na každém RID odpoví na `initialize`.
  - **Závisí na:** P3b.1.1
  - **Odhad:** v rámci zbytku rozpočtu P3b (viz P3b.5.4); cena F202 v matici patří veřejnému SDK (P8b.2.1)
  - **Umístění:** jádro (vývojová infrastruktura)
- [ ] **P3b.5.2** Integrační zkouška distribuce pluginů na Windows a macOS
  - **Co a proč:** Na konci P3b: přibalený i cizí plugin přes hotového správce na Apple Silicon i Intel, karanténa staženého ZIP, první spuštění a aktualizace pluginu. Na macOS musí být spustitelný soubor pluginu podepsaný a notarizovaný, jinak ho může zablokovat Gatekeeper. Používá skript z P3b.5.4.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** stažený ZIP s pluginem projde karanténou macOS, nainstaluje se správcem a plugin se spustí; přibalený plugin se spustí po instalaci na obou OS; aktualizace pluginu a návrat na předchozí verzi projdou na všech třech RID.
  - **Závisí na:** P3b.5.4, P3b.4.2, PR.3.1
  - **Odhad:** v rámci zbytku rozpočtu P3b (viz P3b.5.4)
  - **Umístění:** jádro
- [ ] **P3b.5.3** JSON formátovač jako první vestavěný plugin
  - **Co a proč:** První ověření veřejného API: příkaz na vyžádání, syntaktická chyba, náhled, zastaralá revize, jeden krok undo, více výběrů, odmítnutí příliš velkého vstupu. Nejdřív malé UTF-8 soubory, pak kódování a EOL.
  - **Funkce:** F074, F205 (částečně: první plugin ověřovací sady)
  - **Stav 26. 10. 2026:** částečně: `NksHub.Textida.Plugins.Json` – formátování na vyžádání, náhled ve stávajícím dialogu změn, syntaktická chyba s řádkem a sloupcem, zastaralá revize odmítnuta, více výběrů jako jeden krok undo, příliš velký vstup odmítnut s oběma velikostmi; nad CP1250 se smíšenými EOL se změní jen vybrané rozsahy (test end-to-end). Neověřeno: AOT publikace s přibaleným pluginem a běh na macOS.
  - **Hotovo, když:** formátovač běží jako vestavěný plugin proti AOT buildu na obou OS; nad souborem CP1250 se smíšenými EOL změní jen formátovaný rozsah; vstup nad limit odmítne s hlášením.
  - **Závisí na:** P3b.3.2, P3b.5.1, P3b.2.2
  - **Odhad:** v rámci zbytku rozpočtu P3b (viz P3b.5.4)
  - **Umístění:** vestavěný plugin (balíček formátovačů)

## P4 – textové nástroje

Odhad etapy 19,5–31,5 ČT. Hotovo, když: klip s formulářem, převodník a LOG parser fungují na vlastních datech editoru; recept „vyber sloupec → uprav → náhled → použij“ projde i s návratem. Nástroje od P4 vznikají rovnou jako vestavěné pluginy nebo data nad API z P3b. Vlastní data editoru se tvoří znovu; data PSPadu se nenačítají (kap. 7).

### P4.1 Klipy a šablony

- [x] **P4.1.1** Klipy jako data
  - **Co a proč:** Klipy jsou deklarativní knihovny (`contributes.clips`), vkládací engine je v jádře. Náhled výsledku. Nastavení klipů jako běžné nastavení, ne původní layout dialogu.
  - **Funkce:** F023, F130
  - **Stav 26. 10. 2026:** hotovo: klipy jako data v datových pluginech (složka `plugins` v profilu, bez procesu), Úpravy › „Vložit klip…“ se seznamem a náhledem, vložení do všech kurzorů jako jeden krok undo; vlastní formát `${proměnná}`, `$|` kurzor, `$$`. Limity délky a kurzorů jsou zástupné hodnoty.
  - **Hotovo, když:** vlastní knihovna klipů se nainstaluje jako datový plugin bez procesu, klip se vloží s náhledem a do všech kurzorů.
  - **Závisí na:** P3b.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** data (F023), jádro (F130)
- [x] **P4.1.2** Interaktivní klipy a klip z bloku
  - **Co a proč:** Formulář klipu (barva, soubor, datum…) s typovým ověřením vstupů a atomickým vložením. Uložení výběru jako klip do vlastní knihovny.
  - **Funkce:** F024, F025
  - **Stav 26. 10. 2026:** hotovo: pole formuláře typů text, integer, number, color, date, choice, file; špatná hodnota se označí a nic se nevloží; „Uložit výběr jako klip…“ uloží text doslova do vlastní knihovny. Systémový výběr souboru a náhled barvy neověřeny testem.
  - **Hotovo, když:** klip s formulářem odmítne neplatný vstup a vloží výsledek jako jeden krok undo; výběr uložený jako klip jde hned vložit.
  - **Závisí na:** P4.1.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P4.1.3** Uživatelské proměnné
  - **Co a proč:** Proměnné pro klipy a nástroje s jedním významem všude a s pravidly přednosti. `%File%` znamená v klipu PSPadu něco jiného než v nástroji; tady se významy oddělí.
  - **Funkce:** F127
  - **Stav 26. 10. 2026:** částečně: jedno místo pro proměnné s pořadím formulář › uživatelské (`variables.json` v profilu) › vestavěné, příkaz „Upravit uživatelské proměnné“, test pořadí. Shoda hodnoty s receptem ověřena testem (P4.11.1); se spouštěčem nástrojů čeká na P4.3.3.
  - **Hotovo, když:** stejná proměnná dá v klipu, receptu i externím nástroji stejnou hodnotu; test pokrývá pořadí přednosti.
  - **Závisí na:** P4.1.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [x] **P4.1.4** Šablony souborů
  - **Co a proč:** Uživatelské šablony nových souborů jako data načítaná jádrem F029.
  - **Funkce:** F022
  - **Stav 26. 10. 2026:** hotovo: Soubor › „Nový ze šablony…“ otevře kartu s obsahem šablony a kurzory na `$|`; šablony dodává datový plugin.
  - **Hotovo, když:** „Nový ze šablony“ vytvoří kartu s obsahem šablony a kurzorem na značce; šablony dodá datový plugin.
  - **Závisí na:** P4.1.1
  - **Odhad:** bez odhadu
  - **Umístění:** data

### P4.2 Převodníky a konverze

- [x] **P4.2.1** Uživatelské převodníky
  - **Co a proč:** Převodní tabulky (entity, znaky, regexy) jako data se zkušebním textem a náhledem výsledku; formát, limity a náhled určí jádro.
  - **Funkce:** F095
  - **Stav 26. 10. 2026:** hotovo: převodník z datového pluginu nad všemi výběry (bez výběru nad souborem) s náhledem, jeden krok undo, výběry zůstanou na novém textu, příkaz „Převést…“ opakovatelný; zkušební vzorky převodníku se při načtení pluginu ověří a vadný převodník (chybný vzorek, prázdné hledání, chybný regex) se odmítne. Limity (počet pravidel) neurčeny.
  - **Hotovo, když:** vlastní převodník z datového pluginu se použije nad výběrem s náhledem a jedním krokem undo; neplatný převodník se odmítne při instalaci.
  - **Závisí na:** P3b.2.1, P2.5.2
  - **Odhad:** bez odhadu
  - **Umístění:** data
- [ ] **P4.2.2** Speciální konverze
  - **Co a proč:** Užitečné textové konverze z PSPadu jako příkazy v paletě nad více výběry.
  - **Funkce:** F048
  - **Před zahájením:** seznam ID konverzí se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026:** částečně: mechanismus speciálních konverzí v témže seznamu jako převodníky, nad více výběry jako jeden krok undo, brána v testu vyžaduje pro každou konverzi uzavřený seznam a vzorek. Seznam ID konverzí (vstupní rozhodnutí) chybí, proto zatím žádná konverze.
  - **Hotovo, když:** seznam ID konverzí v testu je uzavřený; každá má vstup, očekávaný výstup a undo, funguje nad více výběry jako jeden krok undo a výběry po ní zůstanou na převedeném textu; nová konverze bez testovacího případu bránou neprojde.
  - **Závisí na:** P2.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P4.3 LOG parser a režim logu

- [x] **P4.3.3** Minimální spouštěč nástrojů, argumenty a proměnné
  - **Co a proč:** LOG parser už v P4 spouští nástroj, ale editor definic, akce při uložení a katalog nástrojů jsou až v P8 (P8.2.1). Proto tady vzniká základ jednoho spouštěče: příkaz, argumenty jako pole, pracovní adresář, proměnné z P4.1.3, časový limit a zachycení výstupu. Příkaz se ukáže předem. P8 ho rozšíří, druhý spouštěč nevznikne.
  - **Funkce:** F124 (částečně: základ spouštěče)
  - **Stav 27. 10. 2026:** hotovo: `ToolRunner` v jádru předává argumenty jako pole bez shellu (test s mezerami, uvozovkami, `&`, `%`, `$`), proměnné `${File}`, `${FileDir}`, `${FileName}`, `${FileBase}`, `${FileExt}`, `${Line}`, `${Column}`, časový limit a Zastavit ukončí celý strom procesů, první spuštění ukáže příkaz a adresář a čeká na potvrzení (otisk definice v profilu), výstup jde po řádcích do karty Úlohy. Definice jsou v `tools.json` v profilu ve tvaru `externalTools` z manifestu pluginu. Dávkové soubory ve Windows se odmítají, holý název se hledá jen v PATH. Chybí: uživatelské proměnné z P4.1.3 (spouštěč bere jejich vyhledání, zatím dodává jen proměnné dokumentu); výchozí časový limit a kódování výstupu jsou vstupní rozhodnutí, otevřená: bez `timeoutSeconds` limit není, výstup se čte jako UTF-8.
  - **Hotovo, když:** proměnná s mezerou v cestě se předá jako jeden argument; nástroj se poprvé spustí až po potvrzení příkazu a adresáře; překročení časového limitu proces ukončí a ohlásí; výstup jde zrušit.
  - **Závisí na:** P4.1.3, P3.6.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [x] **P4.3.1** LOG parser kompilátoru
  - **Co a proč:** Spustí nástroj přes spouštěč z P4.3.3, zachytí výstup, klik na chybu skočí do souboru. LOG vzory jsou data s náhledem vzoru; jazyk vzorů `* %F %L %C %S %E %D` je popsaný v rozboru PSPadu. Podtržení v textu přijde v P8.8.1.
  - **Funkce:** F125
  - **Stav 27. 10. 2026:** hotovo: jazyk masek podle rozboru PSPadu (zleva, bez rozlišení velikosti, bez zpětného prohledávání) a regulární výraz se skupinami `file`, `line`, `column`; okno „Vzor LOGu pro výstup“ ukáže na ukázkovém výstupu soubor, řádek a sloupec každého řádku; klik nebo Enter v kartě Úlohy otevře místo (relativní cesta: adresář nástroje, složka otevřeného souboru, otevřené dokumenty podle názvu). Vzory jsou data v `tools.json`; manifest pluginu má u vzoru `syntax` a `levels`, u nástroje `timeoutSeconds` a `logPattern`. Chybí: načítání pluginů z disku do aplikace (P3b.2.1), takže vzory pluginů se zatím jen ověřují; podtržení v textu (P8.8.1); předvolby kompilátorů jako hotová nabídka.
  - **Hotovo, když:** vlastní vzor s náhledem nad ukázkovým výstupem najde soubor, řádek a sloupec; klik v okně výstupu otevře místo.
  - **Závisí na:** P3b.2.1, P3.6.3, P4.3.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro, vzory jako data
- [x] **P4.3.2** Režim logu: základ
  - **Co a proč:** Úrovně podle vlastního vzoru, další a předchozí chyba, i ve stránkovaném režimu. Sbalení řádků, odkazy ze stack trace, heatmapa a ANSI barvy jsou v P9.4.2.
  - **Funkce:** F187
  - **Stav 27. 10. 2026:** hotovo: jazyk Log barví řádky podle úrovně (chyba, varování, informace, ladění) v editoru i ve stránkovaném zobrazení (test vykreslení); Log: další a předchozí chyba čte soubor po blocích po 1 MiB a končí u prvního nálezu, Esc ruší; test Large na vygenerovaných 10 GiB a na skutečném 10GiB řídkém souboru (25 s, paměť pod 256 MiB), v UTF-8, UTF-16 a jednobajtovém kódování s CR, LF i CRLF proti ručnímu průchodu. Úrovně dává výraz na úroveň z `levels` vzoru v `tools.json`. Neověřeno: 10GiB soubor přes celé okno aplikace na stránkovaném povrchu. Vstupní rozhodnutí otevřené: výchozí slova úrovní (ERROR, WARN, INFO, DEBUG a příbuzná velkými písmeny) jsou jen výchozí volba, řádek delší než 8192 znaků se v editoru posuzuje podle začátku, volba pravidel se neukládá do relace.
  - **Hotovo, když:** v 10GiB logu skok na další ERROR funguje bez načtení celého souboru a řádky jsou obarvené podle úrovně.
  - **Závisí na:** P3.1.4, P4.3.1
  - **Odhad:** F187 2–3 ČT za základ i plný režim (matice)
  - **Umístění:** jádro, vzory jako data

### P4.4 Code Explorer

- [ ] **P4.4.1** Code Explorer bez LSP a symboly v Goto Anything
  - **Co a proč:** Strom funkcí, tříd a značek nad otevřeným souborem bez LSP a bez indexu projektu. Osnova CMake, Makefile, Meson a klíčů YAML. Symboly `@` v Goto Anything. Poskytovatele symbolů a reveal dává API z P3b.3.5.
  - **Funkce:** F083, F148 (částečně: symboly)
  - **Stav 26. 10. 2026:** částečně: vestavěný plugin `NksHub.Textida.Plugins.CodeExplorer` (jednoduchá pravidla jazyka, bez LSP, komentáře a řetězce se nečtou) – osnova HTML, PHP, CSS, C# a Markdownu patří k revizi, po úpravě se obnoví, zpožděná odpověď pro starší revizi se neukáže; klik v osnově i `@` v Přejít na cokoli vyberou stejné bajty; osnova přežije restart procesu pluginu (test). Chybí osnova CMake, Makefile a Meson (jazykové režimy v aplikaci nejsou); limity jsou zástupné hodnoty; ověřeno jen headless.
  - **Hotovo, když:** osnova HTML, PHP, CSS, C# a Markdownu patří ke konkrétní revizi a po úpravě se obnoví; zpožděná odpověď pro starší revizi se nezobrazí jako aktuální; klik i `@` v Goto Anything trefí stejné bajty; osnova přežije restart hostitele pluginů.
  - **Závisí na:** P3b.2.2, P3b.3.5, P2.1.2
  - **Odhad:** osnova CMake a Makefile 0,5 ČT; symboly v rámci F148 (P2.1.2); jinak bez odhadu
  - **Umístění:** vestavěný plugin (balíček Markdown a osnova)

### P4.5 Psaní a schránka

- [x] **P4.5.1** Historie schránky
  - **Co a proč:** Ctrl+Shift+V seznam posledních kopií s náhledem, vložení i do všech kurzorů. Monitor systémové schránky F088 je jiná funkce (později).
  - **Funkce:** F173
  - **Stav 26. 10. 2026:** hotovo: Ctrl+Shift+V otevře historii schránky s náhledem a filtrem, vybraný text se vloží do všech kurzorů jako jeden krok undo, funguje i ve stránkovaném režimu, v read-only odmítnuto; historie jen v paměti. Zástupné hodnoty: 20 položek, max. 1 048 576 znaků na položku.
  - **Hotovo, když:** po pěti kopiích seznam ukáže pět položek a vybraná se vloží do všech kurzorů jako jeden krok undo.
  - **Závisí na:** P2.2.1
  - **Odhad:** F173 0,5–1 ČT (matice)
  - **Umístění:** jádro
- [ ] **P4.5.2** Emmet
  - **Co a proč:** Zkratka + Tab v HTML a CSS, náhled před rozbalením, obalení výběru zkratkou.
  - **Funkce:** F182
  - **Stav 26. 10. 2026:** částečně: plugin `NksHub.Textida.Plugins.Emmet` (algoritmus napsaný podle veřejného popisu syntaxe, bez kódu a dat Emmetu): `ul>li*3` + Tab vytvoří seznam s kurzorem v každé položce jako jeden krok undo, Tab mimo zkratku odsadí jako dřív, náhled a obalení výběru; jádro umí `items` ve `snippet/insert`. Otevřené: jazyky (dnes HTML a CSS), vypnutí v nastavení, zástupné limity; Perf test 33 ms na 1 MiB napsaný, neměřen; přeskakování mezi zástupnými místy chybí.
  - **Hotovo, když:** `ul>li*3` + Tab vytvoří seznam se třemi položkami s kurzory na místech snippetu; Tab mimo zkratku plugin předá zpět jádru a odsadí jako dřív; obalení výběru vrátí jeden krok undo.
  - **Závisí na:** P3b.3.2, P3b.3.5, P4.1.1
  - **Odhad:** F182 2–3 ČT (matice)
  - **Umístění:** vestavěný plugin
- [ ] **P4.5.3** Řádkové a blokové příkazy
  - **Co a proč:** Speciální vložení včetně sloupcové sémantiky, vkládání a číslování řádků s více výběry, řazení s náhledem a pravidly, výběr řádků podle rozsahů jako filtr, formátování bloku. Externí řazení obřích dat až později.
  - **Funkce:** F045 (částečně: speciální vložení mimo vložení do sloupce), F047, F052, F061, F071
  - **Před zahájením:** seznam ID příkazů a voleb se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Stav 26. 10. 2026:** částečně: vložit bez posunu kurzoru, vložit do řádků (text, čísla, odrážky), seřadit řádky s náhledem a pravidly, vybrat řádky podle rozsahu (více výběrů), přeformátovat odstavec na pravý okraj, zarovnat vlevo, vpravo, na střed a do bloku; každý s testem vstupu, výstupu a undo. Vstupní rozhodnutí (seznam ID příkazů a voleb) čeká na schválení; chybí Vložit jako HTML, Vložit ze souboru a zalamování podle krátkého řádku (F071).
  - **Hotovo, když:** seznam ID příkazů a voleb v testu je uzavřený a každá položka má vstup, očekávaný výstup a undo; řazení ukáže náhled a provede se jako jeden krok undo; číslování vloží čísla do každého kurzoru; výběr řádků 10–20 a 40–50 vytvoří dva výběry.
  - **Závisí na:** P2.2.1, P2.5.2
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [x] **P4.5.4** Malé vkládací příkazy
  - **Co a proč:** Datum a čas v uživatelském formátu, GUID, opakování posledního vhodného příkazu. Jsou příliš malé na vlastní proces.
  - **Funkce:** F049, F050, F077
  - **Stav 26. 10. 2026:** hotovo: datum a čas podle nastavení a GUID do každého kurzoru zvlášť, Opakovat poslední příkaz pro příkazy označené `repeatable` se stejnými parametry bez dialogu. Zástupné hodnoty: formát `yyyy-MM-dd HH:mm`, GUID malými písmeny s pomlčkami.
  - **Hotovo, když:** každý příkaz vloží do všech kurzorů; „Opakovat poslední příkaz“ zopakuje poslední opakovatelný příkaz z registru se stejnými parametry.
  - **Závisí na:** P1.6.1, P2.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P4.5.5** Doplňování slov a klipů
  - **Co a proč:** Ctrl+J doplní slova z dokumentu a klipy, funguje hned po instalaci. LSP doplňování je v P8.
  - **Funkce:** F076
  - **Stav 26. 10. 2026:** částečně: Ctrl+J nabídne slova z otevřených karet a klipy podle jazyka, výběr nahradí slovo před každým kurzorem jako jeden krok undo; skryté hodnoty `.env` se nenabízejí (odkryté jen v samotném souboru). Index slov dokumentu se po úpravě opraví jen v dotčených řádcích (test shody s nově postaveným po náhodných úpravách); řádek nad 64 tis. znaků index zahodí a další Ctrl+J ho postaví znovu. Zástupná hodnota 200 slov v nabídce; Perf na referenční Windows VM 27. 10. prošel (nabídka v souboru 1 MiB i hned po napsaném znaku do 33 ms).
  - **Hotovo, když:** doplnění nabídne slova z otevřených karet a klipy podle jazyka do 33 ms v souboru 1 MiB.
  - **Závisí na:** P4.1.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P4.6 Web, barvy a data

- [x] **P4.6.1** Barvy u hodnoty
  - **Co a proč:** Náhled barvy u hodnoty v textu, klik otevře výběr barvy, převod HEX / RGB / HSL / oklch.
  - **Funkce:** F085, F086
  - **Stav 26. 10. 2026:** hotovo: vzorek barvy u hodnoty v CSS a webových jazycích, klik otevře výběr, nová barva přepíše hodnotu ve stejném zápisu jako jeden krok undo; převod HEX/RGB/HSL/oklch tam a zpět dá stejnou 8bitovou barvu (ověřeno na stovkách tisíc barev), plugin `NksHub.Textida.Plugins.WebData`. Otevřené: které jazyky vzorky ukazují (dnes CSS, HTML, XML, JS, TS, PHP, Razor, Latte). Skutečný klik myší neověřen.
  - **Hotovo, když:** u `#ff8800` v CSS je vidět barva, výběr nové barvy přepíše hodnotu a převod na `oklch()` zachová barvu.
  - **Závisí na:** P3.2.1, P3b.3.2
  - **Odhad:** 1–2 ČT
  - **Umístění:** jádro (F085, náhled a výběr), vestavěný plugin (F086, převod)
- [ ] **P4.6.2** Skryté hodnoty v `.env`
  - **Co a proč:** Hodnoty jako `••••`, ukázat najetím nebo přepínačem; obsah se nemění. Maskování je jeden sdílený filtr jádra pro vykreslení, minimapu, schránku, hledání, kopírování jako kontext (F254), přepisy agentů (F257) a API pluginů. Pravidlo pro agenty (hodnoty jen po potvrzení) platí od P6.
  - **Funkce:** F184
  - **Stav 26. 10. 2026:** částečně: hodnoty `.env` skryté `••••` ve vykreslení, minimapě, náhledech hledání, Přejít na cokoli, výsledcích hledání, schránce i historii schránky (test); odhalení přepínačem nebo najetím myši; plugin bez potvrzení čte jen `*`, vestavěné příkazy se ptají; soubor se nemění, záznam volání bez hodnot. Čtečka obrazovky dostane místo hodnoty značku „[skrytá hodnota]“ (cs/sk/en/de, test), po odkrytí skutečný text. Chybí: stránkovaný a hex pohled ukazují surové bajty; NVDA a VoiceOver neověřeny. Otevřené: které soubory jsou „tajné“ (dnes `.env`, `.env.*`, `*.env`).
  - **Hotovo, když:** hodnoty `.env` jsou skryté ve vykreslení, v minimapě, ve výsledcích hledání a ve schránce při kopírování skrytého pohledu; plugin bez výslovného potvrzení dostane jen maskovanou hodnotu; uložení nezmění bajty souboru.
  - **Závisí na:** P3.2.1
  - **Odhad:** F184 0,5–1 ČT (matice)
  - **Umístění:** jádro
- [x] **P4.6.3** SQL bez připojení
  - **Co a proč:** Dialekt pro soubor, složku a globálně, výchozí Generic bez hlášení chyb; formátování s velikostí klíčových slov a odsazením podle dialektu. K databázi se nepřipojuje.
  - **Funkce:** F185
  - **Stav 26. 10. 2026:** hotovo: dialekty generic, mysql, postgres, sqlite, tsql; volba pro soubor příkazem „Dialekt SQL…“, pro složku `sql_dialect` v `.editorconfig`, globálně `sql.dialect` (soubor › složka › globální); změna `.editorconfig` přebarví SQL soubory složky (test); formátování jako jeden krok undo bez změny bajtů mimo výběr (test). Otevřené: seznam dialektů, velikost klíčových slov a odsazení (dnes velká písmena a 4 mezery, zástupné hodnoty). Změna `.editorconfig` mimo editor se projeví až při další změně nastavení nebo aktivaci karty.
  - **Hotovo, když:** změna dialektu složky změní zvýraznění všech SQL souborů v ní; formátování vrátí jeden krok undo a nezmění bajty mimo formátovaný rozsah.
  - **Závisí na:** P3b.5.3
  - **Odhad:** F185 1–3 ČT (matice)
  - **Umístění:** vestavěný plugin (balíček formátovačů)
- [x] **P4.6.4** Cesta klíče v YAML a JSON
  - **Co a proč:** Stavový řádek ukáže cestu klíče pod kurzorem (`cs.app.title`), kopírování cesty, Goto Anything podle cesty, porovnání klíčů dvou překladových souborů.
  - **Funkce:** F212
  - **Stav 26. 10. 2026:** hotovo: stavový řádek ukazuje cestu klíče pod kurzorem (stará revize nebo posunutý kurzor se zahodí), porovnání `en.yml` a `cs.yml` vypíše chybějící klíče na obou stranách do okna výstupu, Přejít na cokoli `@cesta` i klik v osnově vyberou stejné bajty; kopírovat cestu a osnova. Limity jsou zástupné hodnoty.
  - **Hotovo, když:** porovnání `en.yml` a `cs.yml` vypíše chybějící klíče na obou stranách; cesta klíče patří ke konkrétní revizi a zpožděná odpověď pro starší revizi se nezobrazí; Goto Anything s cestou i klik trefí stejné bajty klíče.
  - **Závisí na:** P2.1.2, P3.2.1, P3b.3.5
  - **Odhad:** F212 1–2 ČT (matice)
  - **Umístění:** vestavěný plugin
- [ ] **P4.6.5** HTML nástroje a HTML Tidy
  - **Co a proč:** Nejčastější HTML a textové převody; HTML Tidy jako externí formátovač s diagnostikou. Revize navrhla Tidy jako volitelný plugin, majitel rozhodl vše přibalit a zapnout.
  - **Funkce:** F100, F101
  - **Stav 26. 10. 2026:** částečně: převod výběru na entity a zpět bezeztrátově (test na 20 000 náhodných textech); Tidy bez binárky vypíše, kde ji hledal, se skutečným `tidy.exe` 5.8.0 vrátí výsledek jako návrh a hlášení jdou do okna výstupu; plugin prošel testy i jako NativeAOT. Tidy není přibalený (pro macOS vydává projekt jen instalátor, ne samostatnou binárku; licence právně neověřena). Otevřené: argumenty Tidy, převod znaků mimo ASCII na entity.
  - **Hotovo, když:** převod výběru na entity a zpět je bezeztrátový; Tidy bez nalezené binárky ohlásí, kde ji hledal; s binárkou vrátí výsledek jako návrh s diagnostikou v okně výstupu.
  - **Závisí na:** P3b.5.3, P4.3.1
  - **Odhad:** bez odhadu
  - **Umístění:** vestavěný plugin (textové nástroje, formátovače)

### P4.7 Zvýrazňovače jako data

- [x] **P4.7.1** Multi-zvýraznění, uživatelské zvýrazňovače a jejich nastavení
  - **Co a proč:** Smíšené jazyky (HTML, CSS, JS, PHP v jednom dokumentu) s jednoznačnou prioritou, popsané pravidly pro engine F121. Uživatelské zvýrazňovače jako dokumentovaná podmnožina pravidel. Jazykové profily v nastavení. Princip z `09` kap. 6, ne data PSPadu.
  - **Funkce:** F115, F122, F128
  - **Stav 26. 10. 2026:** hotovo: stránka PHP s HTML, CSS a JS se obarví všemi čtyřmi jazyky a po skoku za dlouhý blok JS a editaci jeho začátku se tokeny shodují s plným průchodem; zvýrazňovač z datového pluginu se načte bez procesu (výběr podle přípony nebo jména souboru, smí vnořit vestavěné jazyky); neplatné pravidlo (regex, neznámá vlastnost, `embed`, `include`, id vestavěného jazyka, duplicitní id) se ohlásí při načtení a plugin se přeskočí; nápověda popisuje formát gramatiky. Jazykové profily přes stávající vrstvu nastavení podle jazyka, bez vlastní volby.
  - **Hotovo, když:** PHP stránka s HTML, CSS a JS se zvýrazní všemi čtyřmi jazyky; po skoku za dlouhý blok jednoho jazyka a po editaci jeho začátku mimo viewport se tokeny shodují s plným referenčním průchodem (checkpointy z P3.2.3); vlastní zvýrazňovač z datového pluginu se načte bez procesu; neplatné pravidlo se ohlásí při instalaci.
  - **Závisí na:** P3.2.1, P3b.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** data (F115, F128), jádro (F122)
- [x] **P4.7.2** Nápověda k jazyku
  - **Co a proč:** Alt+F1 otevře kontextovou nápovědu podle jazyka a slova pod kurzorem. Mapování jazyka na dokument nebo URL jsou data, resolver je v jádře.
  - **Funkce:** F123
  - **Stav 26. 10. 2026:** hotovo: Alt+F1 – zdroje nápovědy k jazyku jsou data (`languageHelp` v manifestu pluginu), soubor z pluginu se otevře v editoru, URL až po potvrzení; test na funkci PHP. Otevřené: které zdroje se budou dodávat (dnes žádný).
  - **Hotovo, když:** Alt+F1 nad funkcí PHP otevře nastavený zdroj; URL se otevře až po potvrzení.
  - **Závisí na:** P3b.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** data

### P4.8 Balík textových nástrojů

- [ ] **P4.8.1** Balík textových nástrojů: statistika
  - **Co a proč:** Jeden balík vestavěných pluginů, jeden proces pro statistiku (tady), hashe (P4.8.2) a výrazy se soustavami (P4.8.3). Statistika asynchronně po blocích s viditelnou neúplností.
  - **Funkce:** F036
  - **Stav 26. 10. 2026:** částečně: vestavěný plugin `NksHub.Textida.Plugins.TextTools` počítá statistiku celého dokumentu po 1MiB blocích na pozadí s průběhem a zrušením (zrušený běh ukáže počet spočtených bajtů); Large test 10 GiB drží živou paměť pod 64 MiB. Otevřená rozhodnutí: definice slova (dnes písmena a číslice); chybí tabulky četností; limity jsou zástupné hodnoty.
  - **Hotovo, když:** statistika 10GiB souboru běží na pozadí se zrušením a pracovní paměť úlohy zůstane ≤ 64 MiB; všechny tři části běží v jednom procesu.
  - **Závisí na:** P3b.3.1, P3b.2.2
  - **Odhad:** bez odhadu
  - **Umístění:** vestavěný plugin (textové nástroje)
- [ ] **P4.8.2** Hashe po blocích
  - **Co a proč:** Hashe po blocích nad surovými bajty; společný dialog s hex hashi F242.
  - **Funkce:** F090
  - **Stav 26. 10. 2026:** částečně: MD5, SHA-1, SHA-256, SHA-384, SHA-512 nad bajty souboru nebo výběru proudem ze snapshotu, SHA-256 se shoduje s `certutil`, zrušení nedá výsledek. Otevřené: seznam algoritmů; chybí porovnání s vloženým hashem a tlačítko kopírovat.
  - **Hotovo, když:** SHA-256 souboru se shoduje s externím SHA nástrojem (`certutil` na Windows, `shasum` na macOS); výpočet jde zrušit.
  - **Závisí na:** P4.8.1
  - **Odhad:** bez odhadu
  - **Umístění:** vestavěný plugin (textové nástroje)
- [ ] **P4.8.3** Výrazy, výpočet bloku a soustavy
  - **Co a proč:** Součet výběru s formátem čísel, převod soustav, vyhodnocení výrazu bez spouštění kódu.
  - **Funkce:** F092, F093, F094
  - **Stav 26. 10. 2026:** částečně: součet bloku, výpočet výrazu bez spouštění kódu a převod soustav (-2^63 až 2^64-1), součty sloupců v cs, sk, de a en formátu, dokument se nemění. Otevřené: volba formátu čísel výběru, nečíselné tokeny (dnes přeskočeny a spočteny), rozsah převodu; chybí vložení výsledku, římská čísla a ms na datum.
  - **Hotovo, když:** součet sloupce čísel ve formátu cs, sk, de i en dá očekávaný výsledek a zobrazí se ve formátu jazyka UI; změna jazyka UI nezmění bajty dokumentu; převod soustav projde krajní hodnoty; výraz s voláním funkce systému se odmítne.
  - **Závisí na:** P4.8.1
  - **Odhad:** bez odhadu
  - **Umístění:** vestavěný plugin (textové nástroje)

### P4.9 Kontext pro AI

- [ ] **P4.9.1** Kopírovat jako kontext pro AI
  - **Co a proč:** Výběr, kurzory, karty, soubory nebo složka jako jeden blok v Markdownu nebo XML s cestou, rozsahem řádků, jazykem, kódováním a revizí; neuložené změny označené. Vynechá `.env`, pravidla ignorování a binární soubory a řekne proč. Nic po síti, jen schránka.
  - **Funkce:** F254
  - **Stav 26. 10. 2026:** částečně: „Kopírovat jako kontext pro AI“ (výběr, soubor, otevřené karty, otevřená složka) s náhledem počtu souborů, velikosti v Markdownu i XML a vynechaných částí s důvodem; cesta, řádky, jazyk, kódování a revize u každého souboru, neuložené změny označené; `.env` se nekopíruje nikdy (filtr v builderu); vynechá `.gitignore`, skryté, binární, velké a nečitelné. Otevřené: limit celkové velikosti; ověřeno jen headless.
  - **Hotovo, když:** náhled ukáže velikost a vynechané části s důvodem; hodnoty z `.env` se do schránky nedostanou; ve stránkovaném režimu se zkopíruje jen výběr.
  - **Závisí na:** P2.5.1, P4.6.2
  - **Odhad:** F254 1–1,5 ČT (matice)
  - **Umístění:** jádro

### P4.10 Hex nástroje

Hotovo celku podle analýzy konkurence, upřesněné podle revize: výběr 1 GiB, SHA-256 se shoduje s externím SHA nástrojem a CRC-32 se známým vektorem a druhou implementací; „kopírovat jako C#“ vložené do kódu se zkompiluje a obsahuje stejné bajty; vyplnění 5 GiB nulami trvá do 1 s, přírůstek paměti ≤ 16 MiB, uložení je bajtově správné.

- [x] **P4.10.1** Kopírovat a vložit jako
  - **Co a proč:** Hex řetězec, pole pro C, C#, Java, Python, Rust, Go, JavaScript, Pascal, Base64, Intel HEX, S-record, hexdump. Vložit jako hex text, Base64, pole C. Export výběru do souboru. Formáty jdou rozšířit jako převodník F095.
  - **Funkce:** F240
  - **Stav 26. 10. 2026:** hotovo: „Kopírovat výběr jako“ (14 podob), „Vložit jako“ a „Exportovat výběr“; pole C# zkopírované z výběru test přeloží Roslynem a bajty se shodují, Base64 a pole všech jazyků tam a zpět beze změny bajtů.
  - **Hotovo, když:** „kopírovat jako C#“ vložené do programu se zkompiluje a obsahuje stejné bajty; Base64 tam a zpět vrátí stejné bajty.
  - **Závisí na:** P3.5.4, P4.2.1
  - **Odhad:** F240 1–1,5 ČT (matice)
  - **Umístění:** jádro (hex, kap. 5.5)
- [x] **P4.10.2** Vyplnit a operace nad výběrem
  - **Co a proč:** Vzor, opakování výběru, náhodně, čísla s krokem; XOR, AND, OR, NOT, posun, rotace, prohození endianity. Velké vyplnění přes nový druh úseku „opakovaný vzor“ ve stromu úseků, ne přes add buffer.
  - **Funkce:** F241
  - **Stav 26. 10. 2026:** hotovo: nový druh úseku „opakovaný vzor“ (`Document.Fill`), „Vyplnit výběr“ a „Operace nad výběrem“; vyplnění 5 GiB alokuje ≤ 16 MiB, uložení se shoduje s nezávislým výpočtem a SHA-256; žurnál ukládá vzor (rozložení záznamu 4). Perf test `Filling_5_GiB_with_zeros_takes_under_a_second_and_16_MiB` na referenční Windows VM 27. 10. prošel (do 1 s). Otevřené: od jaké velikosti vyplnění se undo po uložení přes soubor uvolní (dnes nad 256 MiB, `InPlacePatch.MaxPatchBytes`).
  - **Hotovo, když:** vyplnění 5 GiB opakovaným vzorem trvá do 1 s, přírůstek paměti ≤ 16 MiB a následné uložení se porovná nezávislým výpočtem bajtů.
  - **Závisí na:** P3.5.4
  - **Odhad:** F241 1,5–2 ČT (matice)
  - **Umístění:** jádro (hex, kap. 5.5)
- [x] **P4.10.3** Kontrolní součty a hashe
  - **Co a proč:** CRC-32, CRC-64, xxHash, CRC-16 a vlastní polynom, Adler-32, součty, MD5, SHA-1, SHA-256/384/512 nad výběrem i souborem; porovnání s vloženou hodnotou. Společný dialog s F090.
  - **Funkce:** F242
  - **Stav 26. 10. 2026:** hotovo: „Kontrolní součty a hashe“ nad výběrem nebo souborem na pozadí se zrušením Esc; SHA-256 i CRC-32 výběru 1 GiB se shodují s nezávislým čtením souboru, 13 variant CRC s publikovaným vektorem a bitovou definicí jako druhou implementací, vlastní polynom, XXH32 a XXH64. Chybí XXH3 a společný dialog s P4.8.2.
  - **Hotovo, když:** SHA-256 výběru 1 GiB se shoduje s externím SHA nástrojem (`certutil`, `shasum`); každá CRC varianta má určený polynom a parametry a projde známým ověřovacím vektorem i porovnáním s druhou implementací; výpočet jde zrušit.
  - **Závisí na:** P3.5.2, P4.8.2
  - **Odhad:** F242 0,5–1 ČT (matice)
  - **Umístění:** jádro (hex, kap. 5.5)

### P4.11 Malé recepty

- [x] **P4.11.1** Recept úprav nad otevřeným souborem
  - **Co a proč:** Řetězec najdi → vyber → převeď → nahraď → náhled → použij nad otevřeným souborem, s návratem. Plné recepty nad sadou souborů v P8.1.2. Ověřit uživatelským scénářem (kap. 9).
  - **Funkce:** F165
  - **Stav 26. 10. 2026:** hotovo: „Spustit recept úprav…“ (opakovatelný) – kroky výběru `matches` a `columns`, změny `convert`, `replace`, `set`; náhled změn, jeden krok undo vrátí původní soubor; `set` bere proměnné ze stejného místa jako klip (test shody s klipem včetně přebití uživatelskou proměnnou); nic se nezapíše, když se soubor mezitím změnil; neplatný recept se odmítne při načtení. Krok `convert` jen z vlastního pluginu; ověřeno jen headless.
  - **Hotovo, když:** recept „vyber sloupec → uprav → náhled → použij“ projde i s návratem do původního stavu jedním undo.
  - **Závisí na:** P2.5.2, P4.2.1
  - **Odhad:** F165 3–5 ČT navíc za malé i plné recepty (matice)
  - **Umístění:** jádro

## P5 – data a vzdálené soubory

Odhad etapy 24–37 ČT, **neúplný**: položky s číslem dávají 21–32 ČT a zda rozpočet obsahuje dřívějších 0,5 ČT za úsek CSV jako tabulku (F160, dnes v rámci F160), dohledá PR.8.2. Hotovo, když: korektní CSV round-trip, vzdálený konflikt bez ztráty práce; soubor jde vrátit na každé zachované uložení i po aktualizaci editoru a základ nevyřízené revize F178 retence nesmaže; porovnání dvou 1GiB souborů se 100 změnami najde přesně těch 100 rozsahů. Podmínky: I/O z P1 a změnová služba z P2.

### P5.1 CSV

- [x] **P5.1.1** Režim CSV a TSV: model a round-trip
  - **Co a proč:** CSV model, který nemění bajty mimo upravené buňky: citlivé oddělovače, uvozovky, víceřádkové buňky, bezpečný návrat do textu. Bez join a pivot. Tabulkový pohled, editace a úsek jako tabulka jsou v P5.1.4–P5.1.6.
  - **Funkce:** F160
  - **Stav 26. 10. 2026:** hotovo: `CsvTable` a `CsvDialect` v jádru; úprava buňky nahradí jen bajty buňky jedním krokem undo; uvozovky, oddělovač v buňce, víceřádková buňka, CRLF, neúplný řádek a filtr otestované; soubor z korpusu s víceřádkovou buňkou se po úpravě jedné buňky a uložení liší na disku jen v ní.
  - **Hotovo, když:** projdou uvozovky, oddělovač v buňce, víceřádková buňka, CRLF, neúplný řádek a filtr; CSV s víceřádkovou buňkou z korpusu projde otevřením v tabulce, úpravou jedné buňky a návratem a binární rozdíl obsahuje jen tu buňku.
  - **Závisí na:** P1.3.2, P2.5.2
  - **Odhad:** F160 5–8 ČT (matice) za P5.1.1 a P5.1.3–P5.1.6
  - **Umístění:** jádro
- [ ] **P5.1.4** Tabulkový pohled CSV
  - **Co a proč:** Buňky a sloupce ve vzhledu D5, posun a výběr buněk klávesnicí, přístupný čtečce.
  - **Funkce:** F160 (částečně: tabulkový pohled)
  - **Stav 26. 10. 2026:** částečně: `CsvView` nad textovým pohledem v kartě (bez přepínání backendu), řádky se indexují po dávkách na pozadí, buňky se nevytvářejí předem; čtečka přečte buňku, řádek a sloupec; `nks.view.table`, Ctrl+K Ctrl+T; Large test 300 MB CSV prošel. Neověřeno: vzhled D5 a přetahování v okně, výkon a paměť nad 1 GiB (Perf neměřen). Otevřené: automatická detekce oddělovače (dnes přepínač a výchozí podle přípony).
  - **Hotovo, když:** CSV nad hranicí stránkovaného režimu se posouvá bez převodu celého souboru do buněk; čtečka přečte buňku, řádek a sloupec.
  - **Závisí na:** P5.1.1
  - **Odhad:** v rámci F160 (viz P5.1.1)
  - **Umístění:** jádro
- [x] **P5.1.5** Editace buněk a filtr
  - **Co a proč:** Úprava buňky, přidání a smazání řádku a sloupce, filtr podle hodnoty; každá změna je krok undo.
  - **Funkce:** F160 (částečně: editace a filtr)
  - **Stav 26. 10. 2026:** hotovo: přidat a smazat řádek či sloupec, úprava buňky a filtr podle sloupce; přidání, smazání i úprava s testem vstupu, bajtů a undo; filtr bajty nemění. Vložená editace ve skutečném okně neověřena.
  - **Hotovo, když:** úprava, přidání a smazání mají každé vstup, očekávané bajty a undo; filtr nezmění bajty souboru.
  - **Závisí na:** P5.1.4
  - **Odhad:** v rámci F160 (viz P5.1.1)
  - **Umístění:** jádro
- [x] **P5.1.6** Úsek jako tabulka
  - **Co a proč:** Úprava jen vybraného úseku jako tabulky (dialog oddělovače, uvozovek a záhlaví), zbytek souboru zůstane textem. Zdroj analýza konkurence ho vede v rámci F160; dřívější samostatných 0,5 ČT se nepřičítá.
  - **Funkce:** F160 (částečně: úsek jako tabulka)
  - **Stav 26. 10. 2026:** hotovo: `nks.table.editRegion` s dialogem na oddělovač, uvozovky a záhlaví; text mimo výběr se nečte ani nemění (test).
  - **Hotovo, když:** úprava buňky v úseku změní bajty jen v té buňce a text mimo úsek zůstane beze změny.
  - **Závisí na:** P5.1.5
  - **Odhad:** v rámci F160 (viz P5.1.1)
  - **Umístění:** jádro
- [ ] **P5.1.3** API výběru buněk a schránkové formáty
  - **Co a proč:** Export CSV jako plugin potřebuje model vybraných buněk, ne textový rozsah. API dá pluginu strukturovaný výběr (buňky, záhlaví, revize) a zápis do schránky ve více formátech (text, HTML).
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin dostane výběr tří sloupců s hodnotami a revizí a zapíše do schránky text i HTML; výběr nad starší revizí vrátí konflikt.
  - **Závisí na:** P5.1.4, P3b.3.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P5.1.2** Kopírovat tabulku jako…
  - **Co a proč:** Z CSV nebo výběru SQL INSERT, Markdown, JSON, HTML přes API z P5.1.3; vlastní exportér jako převodník F095.
  - **Funkce:** F186
  - **Hotovo, když:** výběr tří sloupců CSV se zkopíruje jako platná Markdown tabulka, SQL INSERT, JSON i HTML; každý formát má test escapování (apostrof, uvozovky, `|`, `<`, nový řádek v buňce).
  - **Závisí na:** P5.1.3, P4.2.1
  - **Odhad:** F186 1 ČT (matice)
  - **Umístění:** vestavěný plugin

### P5.2 Porovnání

- [ ] **P5.2.1** Porovnání textů
  - **Co a proč:** Soubor proti souboru, disku, výběru nebo schránce; prázdný diff pro dva vložené texty; spuštění z příkazové řádky. Diff je společný podklad pro místní historii, vzdálené změny a revizi F178.
  - **Funkce:** F081
  - **Stav 26. 10. 2026:** částečně: řádkový diff v jádru (`Core/Compare`), karta porovnání (`DiffView`) pro schránku, výběr, soubor, disk a dva vložené texty, kontextové menu editoru, `textida --diff levý pravý`; přenos úseku na druhou stranu je jedna editace a jeden krok undo (i ve stránkovaném souboru); porovnání s diskem a s revizí historie otevře vestavěné porovnání, když není nastavený externí nástroj. Velké soubory: čte okno řádků, 8 B hash na řádek, test 3M řádků (~150 MB) s alokacemi pod 120 MB; nad 10 000 změněných řádků výsledek správný, ne minimální (lišta to hlásí). Neověřeno: vzhled, časy (Perf), čtečka obrazovky (DiffView nemá automation peer); ignorování mezer a velikosti písmen chybí.
  - **Hotovo, když:** porovnání se schránkou jde z kontextového menu; přenesení úseku na druhou stranu je jeden krok undo; diff stránkovaného souboru nenačte celý soubor.
  - **Závisí na:** P1.2.4
  - **Odhad:** diff se schránkou 1 ČT; jinak bez odhadu
  - **Umístění:** jádro
- [x] **P5.2.2** Synchronizované posouvání
  - **Co a proč:** Dva pohledy (dva soubory nebo strany diffu) se posouvají spolu.
  - **Funkce:** F133
  - **Stav 26. 10. 2026:** hotovo: strany porovnání se posouvají společně přes vložené a odebrané bloky, příkaz „Synchronizovat posouvání“ pro dvě karty ve dvou skupinách; test mapování řádků a že se strany navzájem neodrážejí.
  - **Hotovo, když:** posun jedné strany posune druhou na odpovídající řádek i přes vložené bloky.
  - **Závisí na:** P5.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P5.3 Místní historie

- [ ] **P5.3.1** Místní historie souborů: ukládání revizí, diff a obnova
  - **Co a proč:** Revize každého uložení i změny zvenku, diff a obnova celého souboru nebo úseku. Bez projektu a Gitu. Trvalé úložiště s migrací formátu, které aktualizace editoru nesmaže. Je to základ revize změn F178 v P6. Retence je v P5.3.2, štítky a hledání v P5.3.3.
  - **Funkce:** F172
  - **Stav 26. 10. 2026:** částečně: revize jako soubor s vlastními metadaty (bez sdíleného indexu, pád při zápisu nechá nejvýš sirotka, kterého další přístup uklidí), značka verze (historie z novějšího editoru se odmítne beze změny), kontrola hashe při čtení; ukládají se uložení, změny zvenku a stav při prvním otevření; obnova celého souboru jako krok undo, obnova výběru mění jen vybrané řádky; porovnání s revizí přes stávající `ExternalDiff.Compare`. Soubory ve stránkovaném režimu se do historie neukládají (bez rozhodnutého stropu velikosti by se kopírovaly celé), příkazy historie jsou v něm zakázané. Pád simulovaný v procesu, ne harness; UI neověřeno.
  - **Hotovo, když:** každou zachovanou revizi jde obnovit, i po restartu a po aktualizaci editoru; změna zvenku se zapíše jako revize; obnova úseku změní jen ten úsek.
  - **Závisí na:** P5.2.1, P1.2.3
  - **Odhad:** F172 3–5 ČT (matice) za P5.3.1–P5.3.3
  - **Umístění:** jádro
- [ ] **P5.3.2** Retence historie a připnuté revize
  - **Co a proč:** Délka a limit velikosti jsou nastavitelné. Kvóta nesmí zničit možnost vrátit nevyřízenou změnu: revize, která je základem nevyřízené revize změn F178, je připnutá a retence ji nesmaže.
  - **Funkce:** F172 (částečně: retence)
  - **Stav 26. 10. 2026:** částečně: limity počtu, stáří, velikosti na soubor a celkové velikosti; nejnovější a připnutá revize se nesmažou ani při nesplnitelné kvótě; plný disk se ohlásí a starší revize zůstanou. Otevřené: výchozí hodnoty limitů (dnes 0 = bez limitu) a strop velikosti souboru; připnutí jen jako API bez UI.
  - **Hotovo, když:** překročení limitu smaže nejstarší nepřipnuté revize podle nastavení; připnutý základ nevyřízené revize F178 zůstane i při dosažení kvóty; nedostatek místa na disku se ohlásí a žádnou revizi nepoškodí.
  - **Závisí na:** P5.3.1
  - **Odhad:** v rámci F172 (viz P5.3.1)
  - **Umístění:** jádro
- [x] **P5.3.3** Štítky a hledání v revizích
  - **Co a proč:** Štítek u revize a hledání textu ve starších revizích souboru.
  - **Funkce:** F172 (částečně: štítky a hledání)
  - **Stav 26. 10. 2026:** hotovo: štítky s filtrem, hledání v revizích stávajícím vyhledávačem najde text jen ve starší revizi a otevře ji v porovnání. Soubory s tajnými hodnotami (`.env`) se do historie neukládají ani neprohledávají (nastavení `history.storeSecretFiles` vypnuté, rozhodnutí otevřené).
  - **Hotovo, když:** revize se štítkem jde najít podle štítku; hledání najde text, který je jen ve starší revizi, a otevře ji v diffu.
  - **Závisí na:** P5.3.1
  - **Odhad:** v rámci F172 (viz P5.3.1)
  - **Umístění:** jádro

### P5.4 Vzdálené soubory

Vzdálená editace přes lokální pracovní kopii; práce i bez spojení, žádné tiché přepsání novější verze.

- [ ] **P5.4.1** Klient FTPS a SFTP a nastavení spojení
  - **Co a proč:** FTPS a SFTP, prosté FTP jen výslovně. Hesla v systémové klíčence, ověření identity serveru. Import spojení z PSPadu až po 1.0.
  - **Funkce:** F009, F010
  - **Hotovo, když:** matice testů pokryje FTPS i SFTP: platná a neplatná identita serveru, přihlášení, přerušení spojení a konflikt; spojení se změněným klíčem serveru se nepřipojí bez potvrzení; heslo není v souboru nastavení; prosté FTP vyžaduje výslovnou volbu; nepodporovaná kombinace skončí jasnou chybou.
  - **Závisí na:** P1.7.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P5.4.2** Bezpečná vzdálená editace: kontrola verze a nahrání
  - **Co a proč:** Před nahráním kontrola novější verze na serveru (čas a velikost, nebo obsah), při rozdílu diff místo přepsání. Lišta vzdálené karty „shodné se serverem“ / „změněno, nahrát?“. Lokální kopie je v P5.4.6, přerušení a obnova přenosu v P5.4.7. Záruka kontroly verze má hranice podle protokolu: mezi kontrolou a zápisem ji server nemusí umět zajistit atomicky, a UI to řekne.
  - **Funkce:** F162
  - **Hotovo, když:** změna souboru na serveru mezi otevřením a nahráním otevře diff a nic nepřepíše; řízená změna na serveru těsně před nahráním se v testu pozná u obou protokolů, nebo je popsaná jako mez protokolu.
  - **Závisí na:** P5.4.6, P5.4.7, P5.2.1
  - **Odhad:** F162 5–8 ČT (matice) za P5.4.2, P5.4.6 a P5.4.7; kontrola verze 0,5 ČT v rámci F162
  - **Umístění:** jádro
- [ ] **P5.4.6** Lokální pracovní kopie a vzdálená revize
  - **Co a proč:** Vzdálený soubor se upravuje v lokální kopii s uloženou vzdálenou revizí (čas, velikost, případně otisk). Práce jde i bez spojení.
  - **Funkce:** F162 (částečně: lokální kopie)
  - **Hotovo, když:** po odpojení jde soubor dál upravovat a uložit lokálně; neodeslané změny přežijí zavření i pád a po startu se ukážou jako neodeslané.
  - **Závisí na:** P5.4.1, P1.5.1
  - **Odhad:** v rámci F162 (viz P5.4.2)
  - **Umístění:** jádro
- [ ] **P5.4.7** Přerušení, opakování a obnova přenosu
  - **Co a proč:** Nahrání přes dočasné jméno na serveru, kde to protokol dovolí; přerušení nenechá na serveru poškozený soubor.
  - **Funkce:** F162 (částečně: přenos)
  - **Hotovo, když:** přerušené nahrání jde zopakovat a na serveru zůstane buď stará, nebo nová verze celá; opakování se nespustí samo bez kontroly verze.
  - **Závisí na:** P5.4.6
  - **Odhad:** v rámci F162 (viz P5.4.2)
  - **Umístění:** jádro
- [ ] **P5.4.3** Vzdálená cache a záloha konfigurace
  - **Co a proč:** Lokální cache vzdálených souborů s životním cyklem a vzdálenou revizí; zálohy nastavení bez tajných údajů.
  - **Funkce:** F111
  - **Hotovo, když:** export zálohy nastavení neobsahuje hesla ani tokeny; cache se po restartu spáruje se serverem podle uložené vzdálené revize.
  - **Závisí na:** P5.4.2
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P5.4.4** Proxy a síťová pravidla
  - **Co a proč:** Proxy a síťová politika společná pro všechny služby editoru; vestavěný e-mail se vypouští.
  - **Funkce:** F117
  - **Hotovo, když:** nastavená proxy se použije pro FTPS, SFTP, kontrolu aktualizací a síťové požadavky pluginů přes API; každý podporovaný typ proxy má test s FTPS i SFTP a nepodporovaný typ skončí jasnou chybou.
  - **Závisí na:** P5.4.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P5.4.5** Zámky a souběžný zápis
  - **Co a proč:** Systémové a vlastní zámky editoru a nový výslovný režim souběžného zápisu. Zámky `.lck` PSPadu se v 1.0 nečtou; editor nenačítá žádné soubory PSPadu.
  - **Funkce:** F026
  - **Hotovo, když:** soubor se systémovým zámkem jiného procesu nebo s vlastním zámkem jiné instance editoru se otevře jen pro čtení s vysvětlením; výslovný režim souběžného zápisu kontroluje podklad před každým uložením; soubor `.lck` PSPadu vedle dokumentu editor ignoruje.
  - **Závisí na:** P1.2.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P5.5 Kódování naplno

- [ ] **P5.5.1** Plný inspektor bajtů a kódování
  - **Co a proč:** Rozšíření základu z P3.4.1 na všechny cesty uložení: převod celého souboru, sjednocení EOL, normalizace, EditorConfig `charset`, hex. Co se skutečně změní, než se to zapíše.
  - **Funkce:** F169
  - **Hotovo, když:** každá akce, která mění bajty mimo upravené rozsahy, projde inspektorem s výpisem změněných rozsahů a nejde ji provést bez potvrzení.
  - **Závisí na:** P3.4.1, P2.6.1
  - **Odhad:** v rámci F169 (matice, viz P3.4.1)
  - **Umístění:** jádro
- [ ] **P5.5.2** Dávková konverze kódování
  - **Co a proč:** Převod sady souborů s náhledem, obnovou a souhrnem ztrát (`10` kap. 5).
  - **Funkce:** F016
  - **Hotovo, když:** dávka 100 souborů ukáže před spuštěním soubory se ztrátou; pád uprostřed dávky jde dokončit nebo vrátit.
  - **Závisí na:** P5.5.1, P2.5.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P5.6 Hex: hledání, záložky, porovnání

Hotovo celku podle analýzy konkurence: porovnání dvou 1GiB souborů se 100 oddělenými změnami na pevných offsetech najde přesně těch 100 rozsahů; u 100MiB souboru s vloženými bloky ukáže vložení, ne posun; nahrazení všeho jde vrátit jedním krokem a nad novější revizí se odmítne; záložky přežijí vložení před ně; minimapa 50GiB souboru se ukáže do 1 s.

- [ ] **P5.6.1** Regex nad bajty a zástupný znak `*`
  - **Co a proč:** Bajt = znak 0–255, `.` je jeden bajt, `*` = 0–N bajtů (N nastavitelné). Nad `ByteSearch`.
  - **Funkce:** F237
  - **Hotovo, když:** `\x50\x4B[\x01-\x08]` najde stejné offsety jako naivní průchod a vzor s `*` respektuje nastavené N.
  - **Závisí na:** P3.5.1
  - **Odhad:** F237 1–1,5 ČT (matice)
  - **Umístění:** jádro
- [ ] **P5.6.2** Nahrazení bajtů s náhledem
  - **Co a proč:** Náhled podle F152 s původními a novými bajty, upozornění na změnu délky, jedno undo, kontrola revize; v přepisu je změna délky zakázaná.
  - **Funkce:** F238
  - **Hotovo, když:** nahrazení všeho jde vrátit jedním krokem a nad novější revizí se odmítne.
  - **Závisí na:** P3.5.1, P2.5.2
  - **Odhad:** F238 1–1,5 ČT (matice)
  - **Umístění:** jádro
- [ ] **P5.6.3** Záložky a barevné oblasti
  - **Co a proč:** Jméno, barva, komentář, typ z inspektoru; panel, filtr, export a import CSV a JSON. Uložené v metadatech vedle relace, ne v souboru, přepočítané přes záznam změn.
  - **Funkce:** F239
  - **Hotovo, když:** záložky přežijí vložení bajtů před ně; export a import vrátí stejné oblasti.
  - **Závisí na:** P3.5.2, P1.2.5
  - **Odhad:** F239 1–1,5 ČT (matice)
  - **Umístění:** jádro
- [ ] **P5.6.4** Hex minimapa a statistika
  - **Co a proč:** Mapa bajtů podle entropie nebo třídy bajtů, značky shod, záložek a změn; do ~1 GiB plný výpočet, nad tím vzorek a zpřesnění na pozadí. Histogram výběru.
  - **Funkce:** F243
  - **Hotovo, když:** minimapa 50GiB souboru se ukáže do 1 s; během zpřesňování na pozadí má posun vstup → prezentace p95 ≤ 33 ms a p99 ≤ 80 ms.
  - **Závisí na:** P3.3.1, P3.5.2
  - **Odhad:** F243 1–1,5 ČT (matice)
  - **Umístění:** jádro
- [ ] **P5.6.5** Binární porovnání
  - **Co a proč:** Dvě hex mřížky vedle sebe se synchronním posunem, bajt po bajtu (libovolná velikost) i se zarovnáním (výchozí do 256 MiB), seznam rozdílů, revize proti souboru a dvě revize z F172.
  - **Funkce:** F244
  - **Hotovo, když:** dva 1GiB soubory se 100 deterministicky vytvořenými změnami na pevných offsetech, které se nepřekrývají ani nesousedí, dají přesně 100 rozsahů se správnými offsety, délkami a obsahem; testy zvlášť pokryjí přepis, vložení a smazání; 100MiB soubor s vloženými bloky ukáže vložení, ne posun.
  - **Závisí na:** P5.2.1, P3.5.2, P5.3.1
  - **Odhad:** F244 2–3 ČT (matice)
  - **Umístění:** jádro

## P6 – MCP a AI agenti

Odhad etapy 21,5–38 ČT, součet odhadů položek podle revize: F178 2–3, API 6–11, F170 2–4 a souhlas 1–2, F256 1,5–2,5, F247 0,5–1, F171 2–4, F252 1,5–2,5, F179 2–3, F180 3–5 ČT. Souhlas MCP serveru vede analýza konkurence jako samostatných 1–2 ČT, ne v rámci F170, proto byl dřívější rozpočet 20,5–36 ČT o tuto částku nižší. Hotovo, když: asistent přečte neuloženou kartu a připraví změnu, uživatel ji vidí jako rozdíl a použije; zastaralá revize se odmítne; wiki stránka se otevře, upraví a uloží zpět s kontrolou revize; změna od Claude Code, Codexu i skriptu se ukáže po úsecích a jde ponechat nebo vrátit. Podmínky: model revizí, náhled změn z P2 a místní historie F172 z P5.

Po F178 a potřebných API mohou běžet souběžně MCP server (P6.3), MCP klient a wiki (P6.4), most `/ide` (P6.5) a ACP (P6.6). Změna wiki na serveru NKS IS je samostatná vnější závislost (P6.4.4); dohoda o ní (PR.8.4) musí být hotová před zahájením P6. Volba rendereru náhledu a jeho politika (P6.2.7) předchází API náhledu.

### P6.1 Revize změn od agenta

Společný základ celé etapy (kap. 5.3). F179, F180 a pluginy do ní jen posílají návrhy.

- [ ] **P6.1.1** Revize změn po úsecích
  - **Co a proč:** Změna souboru jiným procesem se v otevřené kartě neuplatní potichu. Úseky proti základu (poslední stav, který uživatel viděl nebo uložil, drží F172), Ponechat / Vrátit po úseku, souboru i všech. Značka v liště karet, šipky mezi úseky, stav přežije restart. Automatické ponechání jen jako volba, výchozí vypnuto. Platí pro karty i sledovanou složku; ignoruje `.git`, `.gitignore`, `.codegraph/` a přepisy agentů.
  - **Funkce:** F178
  - **Hotovo, když:** změna od Claude Code, Codexu i skriptu se ukáže po úsecích; Vrátit zapíše obsah základu běžným uložením s kontrolou revize; stav revize přežije pád; základ nevyřízené revize je v místní historii připnutý a retence ho nesmaže (P5.3.2).
  - **Závisí na:** P5.3.1, P5.3.2, P2.5.2, P1.5.1
  - **Odhad:** F178 2–3 ČT (matice)
  - **Umístění:** jádro
- [ ] **P6.1.2** Návrhy pluginů do revize změn
  - **Co a proč:** `edit/propose` z P3b se přepojí ze služby náhledu změn na revizi F178. Odpověď přijde po rozhodnutí uživatele. Je to rozšíření kontraktu F199; návrhový mechanismus z P3b se znovu neúčtuje.
  - **Funkce:** F199 (částečně: napojení na F178)
  - **Hotovo, když:** návrh pluginu se ukáže v revizi jako úseky, plugin dostane výsledek až po rozhodnutí a hlídání hostitele ho mezitím neukončí.
  - **Závisí na:** P6.1.1, P3b.3.2
  - **Odhad:** v rámci F199 (matice, viz P3b.3.2)
  - **Umístění:** jádro

### P6.2 Rozšíření API pluginů

Aby F170, F171, F179 a F180 byly skutečné vestavěné pluginy (rozhodnutí majitele 1 a 4 v kap. 5.4).

- [ ] **P6.2.1** Vzdálené zdroje souborů v API: URI a cache
  - **Co a proč:** Poskytovatel souborů: URI, schopnosti, načtení, offline cache. FTPS a SFTP zůstávají v jádře; adaptéry služeb F171 jdou nad tento kontrakt. Zápis s revizí je v P6.2.3.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací poskytovatel v pluginu otevře dokument podle URI, bez spojení ho otevře z cache s označením „offline“ a schopnosti poskytovatele se ukážou v UI.
  - **Závisí na:** P3b.3.1, P5.4.2
  - **Odhad:** 3–5 ČT za P6.2.1 a P6.2.3
  - **Umístění:** jádro
- [ ] **P6.2.3** Revizní zápis vzdálených zdrojů
  - **Co a proč:** Zápis s očekávanou vzdálenou revizí, chyba konfliktu a obnova bez ztráty práce.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací poskytovatel upraví a uloží dokument; zápis s neočekávanou vzdálenou revizí skončí konfliktem s diffem a lokální změny zůstanou.
  - **Závisí na:** P6.2.1
  - **Odhad:** v rámci odhadu P6.2.1
  - **Umístění:** jádro
- [ ] **P6.2.2** Diff a stage v API
  - **Co a proč:** Veřejné akce diffu vázané na revizi. Revize pracovního stromu i indexu Gitu a operace stage jednoho úseku s konfliktem, aby Git (P8.6.1) byl vestavěný plugin. Odhad z revize stage zahrnuje, proto ho musí ověřit i kritérium.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin otevře diff dvou revizí; testovací plugin předvede stage jediného úseku a při změně indexu mezitím dostane konflikt; nedostane nic mimo zpřístupněné dokumenty.
  - **Závisí na:** P3b.2.2, P6.1.1
  - **Odhad:** 3–6 ČT za P6.2.2, P6.2.4 a P6.2.5
  - **Umístění:** jádro
- [ ] **P6.2.7** Renderer náhledu: volba, AOT prototyp a bezpečnostní politika
  - **Co a proč:** Řízený náhled (P6.2.4) i HTML a Markdown náhled (P9.3.1) potřebují renderer, který dnes vybraný není. Náhled může záviset na runtime mimo přibalený .NET: Avalonia.Controls.WebView používá na Windows WebView2 a na macOS WKWebView a Windows 10 může potřebovat instalaci WebView2 Runtime. Bezpečnostní politika náhledu vzniká tady, před API náhledu; P9.3.1 ji jen rozšíří a zopakuje s Mermaid. Tím zmizí kruh P6.2.4 → P9.3.1 → P6.2.4. R10 nedovoluje náhled na starších systémech vypnout.
  - **Funkce:** žádná (infrastruktura)
  - **Kroky:**
    - vybrat renderer a zapsat jeho licenci, chování pod NativeAOT, minimum OS a způsob dodání a aktualizace závislostí
    - politika: skript, síť a místní soubory jsou bez schválení zakázané
    - minimální AOT prototyp s politikou na Windows 10 22H2, Windows 11 a macOS 14 (arm64 i Intel)
  - **Hotovo, když:** prototyp na každém z těchto systémů vykreslí text, obrázek a diagram, nespustí vložený skript, nenaváže síťové spojení a neotevře místní soubor bez schválení; jde ovládat klávesnicí a čtečkou; při WebView2 projde i stroj bez nainstalovaného WebView2 Runtime a offline instalace; jiný renderer musí mít stejné funkční důkazy; záznam uvádí verzi rendereru, OS, RID a hash buildu.
  - **Závisí na:** P1.1.6, P1.1.8
  - **Odhad:** bez odhadu; ocení PR.8.2 před zahájením P6
  - **Umístění:** jádro
- [ ] **P6.2.4** Řízený náhled v API
  - **Co a proč:** Plocha náhledu ve vzhledu D5 bez obecného webview. Obsah dodá plugin jako data, editor ho vykreslí rendererem a podle bezpečnostní politiky náhledu z P6.2.7. Mermaid se vykresluje přes tento řízený vstup.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin ukáže náhled s textem, obrázkem a diagramem; skript, vzdálený obrázek a odkaz na místní soubor se bez schválené politiky nenačtou.
  - **Závisí na:** P3b.2.2, P6.2.7
  - **Odhad:** v rámci odhadu P6.2.2
  - **Umístění:** jádro
- [ ] **P6.2.5** Panel agenta v API
  - **Co a proč:** Panel konverzace agenta ve vzhledu D5 pro ACP (P6.6) a most `/ide`.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin otevře panel s konverzací, ukáže průběh a zrušení tahu a panel je ovladatelný klávesnicí a čtečkou.
  - **Závisí na:** P3b.2.2
  - **Odhad:** v rámci odhadu P6.2.2
  - **Umístění:** jádro
- [ ] **P6.2.6** Virtuální dokumenty v API
  - **Co a proč:** Dokument, který plugin dodá jako pohled na data (přepisy agentů F257, výsledky), jen pro čtení, s identitou a revizí, nad bajtovým API P3b.3.1. Maskování tajemství (P4.6.2) platí i tady.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin otevře virtuální dokument, editor v něm hledá a vykreslí ho; pokus o zápis vrátí chybu; maskovaná hodnota se neukáže ani ve výsledcích hledání.
  - **Závisí na:** P3b.3.1, P4.6.2
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P6.3 Editor jako MCP server

- [ ] **P6.3.1** MCP server
  - **Co a proč:** Uživatel výslovně zpřístupní kartu nebo výběr. Nástroje `editor_list_documents`, `editor_read_range`, `editor_search` (lokálně, modelu jen úryvky), `editor_prepare_changes` (návrh s revizí, bez zápisu), `editor_get_change_status`. Návrhy jdou přes revizi F178 (P6.1.2) a diff API (P6.2.2), čtení přes sdílený filtr maskování (P4.6.2). Lokální transport bez síťového portu. Dialog při zapnutí s výčtem, co klient uvidí; zapnutí a vypnutí po nástrojích. Hodnoty `.env` jen po potvrzení.
  - **Funkce:** F170, F184 (částečně: pravidlo pro agenta)
  - **Hotovo, když:** asistent přečte neuloženou kartu a připraví změnu, uživatel ji vidí jako rozdíl a použije; návrh nad zastaralou revizí se odmítne; nezpřístupněnou kartu asistent nevidí ani v `editor_list_documents`; odvolání přístupu za běhu zastaví další čtení; hodnota `.env` bez potvrzení přijde maskovaná.
  - **Závisí na:** P6.1.2, P6.2.2, P3b.1.1, P3b.4.1, P4.6.2
  - **Odhad:** F170 2–4 ČT (matice); souhlas a nástroje 1–2 ČT
  - **Umístění:** vestavěný plugin
- [ ] **P6.3.2** Nástroje MCP serveru navíc
  - **Co a proč:** `editor_outline` (osnova i neuložené karty), `editor_find_in_files` (worker F151 i nad živými buffery), `editor_file_facts` (kódování, BOM, EOL, neplatné sekvence, velký režim), `editor_log_query` nad režimem logu. Malá sada nástrojů s přesnými popisy, odpovědi jako úryvky se stropem.
  - **Funkce:** F256
  - **Hotovo, když:** `editor_log_query` vrátí úryvky z 10GiB logu bez načtení celého souboru; `editor_file_facts` u CP1250 se smíšenými EOL vrátí obojí.
  - **Závisí na:** P6.3.1, P4.4.1, P4.3.2
  - **Odhad:** F256 1,5–2,5 ČT (matice)
  - **Umístění:** jádro
- [ ] **P6.3.3** Hex pro MCP
  - **Co a proč:** `editor_read_bytes` (nejvýš 64 KiB, hex nebo base64), `editor_search_bytes`, `editor_prepare_byte_changes` jako návrh s revizí. Modelu se nikdy nepošle celý binární soubor.
  - **Funkce:** F247
  - **Hotovo, když:** asistent přečte 256 B od offsetu, najde vzor a připraví změnu 4 bajtů; uživatel ji vidí v F169 a použije; návrh nad starší revizí se odmítne.
  - **Závisí na:** P6.3.1, P3.5.1
  - **Odhad:** F247 0,5–1 ČT (matice)
  - **Umístění:** jádro (hex, kap. 5.5)

### P6.4 Editor jako MCP klient

- [ ] **P6.4.3** MCP klient
  - **Co a proč:** Společný klient MCP nad JSON-RPC vrstvou P3b.1.1 a poskytovatelem souborů P6.2.1: spuštění serveru, `initialize`, výpis a volání nástrojů, zrušení. Nečeká na wiki ani na změnu mimo repozitář.
  - **Funkce:** F171 (částečně: klient MCP)
  - **Hotovo, když:** klient se připojí k testovacímu MCP serveru, vypíše nástroje, zavolá nástroj, zruší rozpracované volání přes `notifications/cancelled` a pád serveru ohlásí bez pádu editoru.
  - **Závisí na:** P6.2.1, P3b.1.1
  - **Odhad:** v rámci F171 (viz P6.4.1)
  - **Umístění:** vestavěný plugin
- [ ] **P6.4.1** Adaptér NKS IS wiki
  - **Co a proč:** „Otevřít z…“ → služba → dokument, jako první NKS IS wiki. Stránka se upravuje jako běžný dokument a uloží se zpět. MCP nemá obecný zápis zdrojů, každá služba potřebuje adaptér. Bez atomické kontroly revize na straně NKS IS (P6.4.4) nejde wiki bezpečně dokončit.
  - **Funkce:** F171
  - **Hotovo, když:** záznam uvádí verzi adaptéru i služby; wiki stránka se otevře, upraví a uloží zpět; dva klienti uloží proti stejné revizi, uspěje právě jeden a druhý dostane konflikt s diffem.
  - **Závisí na:** P6.4.3, P6.2.3, P6.4.4
  - **Odhad:** F171 2–4 ČT (matice) za P6.4.1 a P6.4.3
  - **Umístění:** vestavěný plugin
- [ ] **P6.4.4** Atomická kontrola revize ve wiki NKS IS (vnější úkol)
  - **Co a proč:** Nástroj aktualizace wiki dnes bere `version` jako novou verzi a nekontroluje očekávanou původní. Pro bezpečné uložení je potřeba parametr typu `expected_revision` kontrolovaný v téže transakci. Je to práce mimo tento repozitář a mimo rozpočet editoru. Vlastníka dodávky, kapacitu a termín zapíše PR.8.4 před zahájením P6. Dnešní stav služby: **neověřeno**.
  - **Funkce:** žádná (vnější závislost)
  - **Hotovo, když:** služba NKS IS má zdokumentovaný parametr očekávané revize a kontraktový test: dva souběžné zápisy proti stejné revizi, uspěje právě jeden; test proběhl dřív, než se začne adaptér P6.4.1.
  - **Závisí na:** PR.8.4
  - **Odhad:** bez odhadu (mimo rozpočet tohoto plánu)
  - **Umístění:** mimo editor
- [ ] **P6.4.2** MCP servery napříč agenty
  - **Co a proč:** Jeden seznam MCP serverů z `.mcp.json`, `~/.claude.json`, `.cursor/mcp.json`, `~/.codex/config.toml` a `opencode.json(c)`. „Vyzkoušet“ ukáže přesný příkaz s maskovanými proměnnými a po potvrzení spustí `initialize` a `tools/list`. Převod záznamu mezi agenty.
  - **Funkce:** F252
  - **Hotovo, když:** test MCP serveru ukáže počet nástrojů nebo chybu ze `stderr` a bez potvrzení nic nespustí; převedený záznam projde kontrolou cílového formátu.
  - **Závisí na:** P6.4.3
  - **Odhad:** F252 1,5–2,5 ČT (matice)
  - **Umístění:** jádro, nad klientem MCP z F171

### P6.5 Most pro Claude Code

- [ ] **P6.5.1** Most `/ide`
  - **Co a proč:** Editor vytvoří zámek `~/.claude/ide/<port>.lock` (práva 0600 ve složce 0700, na Windows odpovídající omezení přístupu) a server na `127.0.0.1` s náhodným portem 10000–65535, ověření hlavičkou `X-Claude-Code-Ide-Authorization`. `openDiff` se otevře v revizi F178 (P6.1.2) přes diff API (P6.2.2) a odpověď odejde po rozhodnutí. Samotný fokus karty není souhlas se sdílením: Claude dostane výběr, cestu a diagnostiku jen u karty, kterou uživatel mostu výslovně zpřístupnil; filtr je stejný jako u F170 a maskování z P4.6.2. `getDiagnostics` vrátí chyby z LSP až po P8 (pozdější rozšíření), do té doby prázdný seznam. Port se otevře až po prvním potvrzení uživatele. Protokol není veřejně specifikovaný; většinu operací dokumentace označuje za interní RPC.
  - **Funkce:** F179
  - **Hotovo, když:** záznam uvádí přesnou verzi CLI, proti které most prošel; Claude Code ve vnějším terminálu se příkazem `/ide` připojí, návrh změny se ukáže v revizi a čeká na potvrzení; testy pokryjí odmítnutý token, práva zámku na obou OS, restart editoru, zrušení diffu, odpojení, změnu fokusu, odvolání přístupu a nezpřístupněnou kartu; řízená nekompatibilita most vypne s hlášením; po změně verze CLI se sada opakuje.
  - **Závisí na:** P6.1.2, P6.2.2, P6.3.1, P4.6.2
  - **Odhad:** F179 2–3 ČT (matice)
  - **Umístění:** vestavěný plugin

### P6.6 Agent přes ACP

- [ ] **P6.6.1** Panel agenta přes ACP
  - **Co a proč:** Editor spustí agenta jako podproces a mluví s ním JSON-RPC přes stdio. Panel na požádání, jako ostatní panely D5. Položka spojuje transport (P6.6.2), soubory, návrhy a oprávnění (P6.6.3) a adaptéry agentů (P6.6.4).
  - **Funkce:** F180
  - **Hotovo, když:** u každého adaptéru z tabulky P6.6.4 agent přečte neuloženou kartu, jeho zápis přes `fs/write_text_file` se ukáže jako úseky v revizi a bez potvrzení se přes editor nic nezapíše; text, který uživatel nezpřístupnil, agent nedostane; chybějící běh adaptéru je ve stavu neověřeno.
  - **Závisí na:** P6.6.2, P6.6.3, P6.6.4
  - **Odhad:** F180 3–5 ČT (matice) za P6.6.1–P6.6.4
  - **Umístění:** vestavěný plugin
- [ ] **P6.6.2** ACP relace a transport
  - **Co a proč:** Adaptér ACP z P3b.1.1: řádkové stdio, inicializace, `session/new`, tahy, `session/cancel` a rušení požadavků podle ACP.
  - **Funkce:** F180 (částečně: relace a transport)
  - **Hotovo, když:** relace se založí, tah se zruší přes `session/cancel`, odpojení a timeout agenta se ohlásí v panelu a editor nespadne.
  - **Závisí na:** P3b.1.1, P6.2.5
  - **Odhad:** v rámci F180 (viz P6.6.1)
  - **Umístění:** vestavěný plugin
- [ ] **P6.6.3** Soubory, návrhy, oprávnění a hranice záruky
  - **Co a proč:** `fs/read_text_file` vrací kartu i s neuloženými změnami; `fs/write_text_file` jde jako návrh do F178. `session/request_permission` se ukáže jako dialog s volbami agenta. Brána API není sandbox: editor hlídá jen zápis, který jde přes něj. Agent jako podproces může zapisovat vlastními nástroji mimo editor. Úkol určí a v UI řekne režim oprávnění každého podporovaného adaptéru a ověří zákaz přímého zápisu jen v rozsahu, který editor skutečně řídí.
  - **Funkce:** F180 (částečně: soubory a oprávnění)
  - **Hotovo, když:** zápis přes `fs/write_text_file` se nikdy nezapíše na disk bez potvrzení; zamítnutí a zrušení dialogu oprávnění vrátí agentovi určenou odpověď; tabulka P6.6.4 uvádí u každého adaptéru, jaký režim oprávnění editor nastavuje a co jeho vlastní nástroje smějí; UI tuto mez uživateli ukáže.
  - **Závisí na:** P6.6.2, P6.1.2, P4.6.2
  - **Odhad:** v rámci F180 (viz P6.6.1)
  - **Umístění:** vestavěný plugin
- [ ] **P6.6.4** Adaptéry podporovaných agentů
  - **Co a proč:** Verzovaná tabulka podporovaných agentů a jejich adaptérů: Gemini CLI, Codex přes `codex-acp`, Claude přes `claude-agent-acp`, Junie, GitHub Copilot, OpenCode přes `opencode acp`. U každého připnutá verze a výsledek sady scénářů.
  - **Funkce:** F180 (částečně: adaptéry)
  - **Hotovo, když:** pro každý adaptér a verzi z tabulky projde čtení neuložené karty, návrh zápisu, zamítnutí, zrušení, odpojení a timeout; adaptér bez běhu má v tabulce „neověřeno“, ne „podporováno“.
  - **Závisí na:** P6.6.3
  - **Odhad:** v rámci F180 (viz P6.6.1)
  - **Umístění:** vestavěný plugin

## P7 – velká editace

Odhad etapy 16–24 ČT, **neúplný**: části F154 do 100 MiB převzalo po spiku měření a rozhodnutí majitele 26. 10. 2026 MVP (P1.2.8, P3.1.5, P3.1.11–P3.1.13, P3.5.9). Spike cenu nedal, takže odečet z P7 není spočítaný; uzavře ho PR.8.2. Hotovo, když: úprava souboru většího než RAM, pád a zrušení s ověřeným výstupem. Staví na prototypu jádra z P0 a P3; nepotřebuje P3b–P6, může běžet souběžně. Bajtové uložení, snapshot a žurnál jsou hotové v P3 (P3.5.5); P7 je rozšíří o velké nové bloky, diskové undo a souběžnou textovou editaci. Ukládací kontrakt nesmí změnit bez integračních testů hexu z P3.5.

### P7.1 Stránkovaná editace

Co dnes `PagedDocument` neumí, podle měření, a cíle sloupce 1.0 z kap. 3 (50 GiB: první obraz ≤ 2 s warm / 5 s cold, ≤ 1 GiB private commit, editace a uložení).

- [ ] **P7.1.1** Čísla řádků po úpravě
  - **Co a proč:** Počty řádků v každém úseku, včetně CRLF přes hranici úseků, umí jádro od commitu `f7defd7` a v MVP je přijímá P1.2.8. Tady zůstává ověření nad 10 GiB a velikost bloku indexu: úprava na náhodném místě dočítá část bloku 1 MiB a na referenční Windows VM trvala p95 0,35 ms proti 0,003 ms na jednom místě (spike kap. 5). Menší blok by to podle spiku zkrátil, změřené to není.
  - **Funkce:** F154
  - **Hotovo, když:** po 1 000 náhodných úpravách 10GiB souboru dává index stejné začátky řádků jako naivní výpočet nad výsledkem; velikost bloku indexu je vybraná měřením úprav na náhodném místě aspoň u dvou velikostí a zapsaná se zdrojem.
  - **Závisí na:** P3.1.1, P1.2.8
  - **Odhad:** F154 10–16 ČT (matice) za P7.1.1–P7.1.4, P7.1.6 a P7.1.7; podíl, který převzalo MVP (P3.5.5 a P3.5.9, po spiku 100 MiB i P1.2.8, P3.1.5 a P3.1.11–P3.1.13), a díly položek P7 určí PR.8.1 a PR.8.2 (podíl MVP před zahájením P1 a P3, díly P7 před zahájením P7)
- [ ] **P7.1.2** Diskové undo a velký add buffer
  - **Co a proč:** Add buffer je dnes jedno pole s limitem 2 GiB. Velké vložené bloky a historie undo na disk.
  - **Funkce:** F154
  - **Hotovo, když:** vložení 3 GiB textu a jeho undo projde a private commit zůstane pod 1 GiB.
  - **Závisí na:** P7.1.1
  - **Odhad:** v rámci F154
- [ ] **P7.1.3** Streaming save souboru většího než RAM se souběžnou editací
  - **Co a proč:** Uložení dnes odmítá souběžné úpravy a uložení přes otevřený soubor smaže undo. Undo, které přežije uložení na místo v textu do 100 MiB, je od rozhodnutí majitele 26. 10. 2026 v MVP (P3.1.13). Tady zůstává rozšíření ukládací služby z P3.5.5 o pokračující editaci během uložení a o undo po uložení na místo u souboru většího než RAM.
  - **Funkce:** F154
  - **Hotovo, když:** 50GiB soubor na stroji s menší RAM se upraví a uloží; SHA-256 výsledku sedí s nezávislým výpočtem; během ukládání revize N vznikne úpravou revize N+1, disk po uložení obsahuje přesně N a editor zůstane na N+1 se stavem „neuloženo“; undo po uložení funguje.
  - **Závisí na:** P7.1.2, P3.5.5, P3.1.13
  - **Odhad:** v rámci F154
- [ ] **P7.1.4** Obnova velkých změn
  - **Co a proč:** Velké změny jdou do žurnálu na disku. Cíl kap. 3 pro 1.0: obnova po pádu i u velkých dokumentů, ztráta nejvýš 2 s žurnálu. Navazuje na hot exit z P1.5.1 a žurnál zápisu z P3.5.5.
  - **Funkce:** F154
  - **Hotovo, když:** zabití procesu během úpravy a během uložení 50GiB souboru vede po startu k obnově bez poškození originálu; obnovená práce chybí nejvýš za 2 s podle časových značek žurnálu; zrušení uložení nechá originál beze změny.
  - **Závisí na:** P7.1.3, P1.5.1
  - **Odhad:** v rámci F154
- [ ] **P7.1.5** Editace dlouhých řádků
  - **Co a proč:** Lokální editace v řádku 100 MiB bez materializace celého řádku; v P0 neměřeno. Do té doby MVP editaci řádku nad limitem odmítne (P3.1.11).
  - **Funkce:** F155 (částečně: editace)
  - **Hotovo, když:** psaní uprostřed řádku 100 MiB má vstup → prezentace p95 ≤ 33 ms a p99 ≤ 80 ms a paměť nepřibude o velikost řádku.
  - **Závisí na:** P3.1.3, P7.1.1
  - **Odhad:** v rámci F155 (matice, viz P3.1.3)
- [ ] **P7.1.6** Hledání nad upraveným stránkovaným dokumentem při textové editaci
  - **Co a proč:** Čtecí adaptér a hledání nad aktuální revizí vznikají už v P3.5.9, pro hex úpravy i pro textovou editaci do 100 MiB včetně čísel řádků shod. Tady se ověří nad 10 GiB, kde index řádků po úpravě ladí P7.1.1.
  - **Funkce:** F154
  - **Hotovo, když:** po vložení textu do 10GiB souboru hledání najde i vložený text, uspořádané offsety sedí s naivním průchodem výsledku a čísla řádků shod odpovídají indexu z P7.1.1.
  - **Závisí na:** P7.1.1, P3.5.9
  - **Odhad:** v rámci F154
- [ ] **P7.1.7** Přístupnost a IME při editaci stránkovaného dokumentu
  - **Co a proč:** P3.1.5 ověří čtení stránkované plochy a editaci textu do 100 MiB; tady se ověří skutečná textová editace nad 10 GiB.
  - **Funkce:** F154 (částečně: přístupnost editace)
  - **Hotovo, když:** v matici R10 (PR.4) v 10GiB souboru NVDA i VoiceOver přečtou vložený text, výběr a novou pozici kurzoru; scénář IME z P1.8.2 včetně přepnutí karty, více kurzorů a undo projde bez zdvojení i ztráty textu; důkazy z P3.1.5 se nepřebírají.
  - **Závisí na:** P3.1.5, P7.1.1
  - **Odhad:** v rámci F154

## P8 – automatizace a jazyk

Odhad etapy 38–64,5 ČT. Hotovo, když: opakovaná úprava a návrat výsledku, kontrola workspace edit. Staví na příkazech a receptech z P4. „17 jazyků“ je uzavřený seznam 17 skupin z tabulky analýza konkurence, kap. „Nejpoužívanější jazyky a jejich editory“ (např. „JavaScript, TypeScript“ nebo „C, C++“ je jedna skupina). Žádná skupina nepotřebuje vlastní engine: gramatika, LSP na vyžádání, formátovač, linter a schémata (kap. 4). Po P8.4 mohou souběžně běžet LSP a schémata; Git navíc potřebuje diff a stage z P6.2.2.

### P8.1 Makra a recepty

- [ ] **P8.1.1** Makra
  - **Co a proč:** Záznam a přehrání příkazů z registru s centrálním pořadím a jedním undo. Ne binární emulace. Import PME až po 1.0.
  - **Funkce:** F018
  - **Hotovo, když:** zaznamenané makro nad více výběry dá při přehrání stejný výsledek jako ruční postup a vrátí se jedním undo.
  - **Závisí na:** P1.6.1, P4.5.4
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P8.1.2** Plné recepty nad sadou souborů
  - **Co a proč:** Uložený recept se pustí znovu nad jinou sadou souborů, s náhledem, obnovou a kontrolou revizí. Krokem může být převodník, náhrada, formátovač.
  - **Funkce:** F165
  - **Hotovo, když:** recept uložený minulý měsíc projde nad novou sadou souborů s náhledem; konflikt revize jednoho souboru nezastaví ostatní a ohlásí se.
  - **Závisí na:** P4.11.1, P2.5.3
  - **Odhad:** v rámci F165 (matice, viz P4.11.1)
  - **Umístění:** jádro

### P8.2 Externí nástroje

- [ ] **P8.2.1** Externí aplikace, proměnné a nastavení nástrojů
  - **Co a proč:** Rozšíření spouštěče z P4.3.3: typované proměnné cest a textu, společný editor definic, příkazový řádek s výstupem. Příkaz se ukáže předem. Druhý spouštěč nevznikne. Plný terminál je F159 (později).
  - **Funkce:** F124, F126, F116, F089
  - **Hotovo, když:** definice nástroje se upraví v editoru definic a spustí se se stejnými argumenty jako ze spouštěče P4.3.3; nástroj z cizí konfigurace se poprvé spustí až po potvrzení příkazu a adresáře; typovaná proměnná se špatným typem se odmítne před spuštěním.
  - **Závisí na:** P4.3.3, P4.1.3, P3.6.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P8.2.2** Cíle Makefile
  - **Co a proč:** Seznam cílů z otevřeného Makefile, spuštění přes F124 v okně výstupu, odkazy na chyby přes F125.
  - **Funkce:** F124
  - **Hotovo, když:** cíl vybraný ze seznamu se spustí a chyba ve výstupu skočí do souboru.
  - **Závisí na:** P8.2.1, P4.3.1
  - **Odhad:** 0,5 ČT
  - **Umístění:** jádro

### P8.3 Akce při uložení

- [ ] **P8.3.1** Akce při uložení a formátovače podle konfigurace
  - **Co a proč:** Seznam akcí po uložení: nahrát na výchozí server, spustit nástroj s proměnnými, formátovat; u každé „jen při ručním uložení“. Formátování je ohraničená transakce před uložením, nahrání až po uložení s revizí uložených bajtů (R11). Formátovač (`gofmt`, `rustfmt`, `clang-format`, RuboCop…) se nabídne, jen když existuje jeho konfigurace, výchozí stav vypnuto.
  - **Funkce:** F176
  - **Hotovo, když:** autosave nespustí akci „jen při ručním uložení“; vypršení formátovače neodešle starší obsah na server; uložení z pluginu nezpůsobí smyčku; každý formátovač z tabulky P8.5.3 má test úspěchu, syntaktické chyby, chybějícího nástroje a timeoutu.
  - **Závisí na:** P8.2.1, P5.4.2, P3b.1.2
  - **Odhad:** F176 2–3 ČT (matice); formátovače podle konfigurace 0,5–1 ČT
  - **Umístění:** jádro

### P8.4 Rozšíření API pro jazykové služby

- [ ] **P8.4.1** Diagnostika v API
  - **Co a proč:** API v1 diagnostiku, doplňování ani workspace edit nemá (R02). Tři samostatné kontrakty: diagnostika (tady), doplňování (P8.4.2) a workspace edit (P8.4.3). Kreslí a ověřuje jádro. Díky tomu jsou LSP, Git a schémata vestavěné pluginy. Publikace diagnostiky s dokumentem, revizí, zdrojem, rozsahy a závažností.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin publikuje diagnostiku, která se vykreslí jako podtržení a zmizí po nové revizi; diagnostika pro starší revizi se přepočte, nebo zahodí.
  - **Závisí na:** P3b.3.1, P2.5.2
  - **Odhad:** 3–5 ČT za P8.4.1–P8.4.3
  - **Umístění:** jádro
- [ ] **P8.4.2** Doplňování v API
  - **Co a proč:** Nabídka doplnění od pluginu s revizí, dodatečnými úpravami (import, `use`) a zrušením zastaralé odpovědi.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** testovací plugin nabídne doplnění s dodatečnou úpravou jinde v souboru a obě se použijí jako jeden krok undo; odpověď pro starší revizi se nezobrazí.
  - **Závisí na:** P8.4.1
  - **Odhad:** v rámci odhadu P8.4.1
  - **Umístění:** jádro
- [ ] **P8.4.3** Workspace edit v API
  - **Co a proč:** Dávka úprav ve více souborech včetně vytvoření, přejmenování a smazání souboru, přes službu náhledu P2.5.2.
  - **Funkce:** žádná (infrastruktura API)
  - **Hotovo, když:** workspace edit s úpravou, vytvořením, přejmenováním a smazáním projde náhledem a kontrolou revizí; úprava mimo povolený rozsah se odmítne celá.
  - **Závisí na:** P8.4.1
  - **Odhad:** v rámci odhadu P8.4.1
  - **Umístění:** jádro

### P8.5 LSP a katalog jazyků

- [ ] **P8.5.1** LSP jako vestavěný plugin: doplňování a rename
  - **Co a proč:** Doplňování a rename s náhledem workspace edits; spouští se na vyžádání. Nad obřími soubory nedostupné. Roslyn LSP s připnutou verzí a `--telemetryLevel off`, záloha csharp-ls. Životní cyklus a synchronizace jsou v P8.5.4, navigace a diagnostika v P8.5.5.
  - **Funkce:** F157
  - **Hotovo, když:** rename v C# přes Roslyn LSP ukáže náhled všech změn a použije je jako jeden krok; doplnění s dodatečnou úpravou projde podle P8.4.2; stavový řádek ukáže „LSP: nenalezen / běží / vypnut“; ve stránkovaném režimu se LSP nespustí; síťový test PR.6.4 s běžícím LSP nezaznamená telemetrii.
  - **Závisí na:** P8.5.4, P8.5.5, P8.4.2, P8.4.3
  - **Odhad:** F157 4–7 ČT (matice) za P8.5.1, P8.5.4 a P8.5.5
  - **Umístění:** vestavěný plugin
- [ ] **P8.5.4** LSP: životní cyklus a synchronizace dokumentu
  - **Co a proč:** Spuštění serveru na vyžádání, `initialize`, synchronizace změn s revizemi, restart a ukončení přes adaptér LSP z P3b.1.1.
  - **Funkce:** F157 (částečně: životní cyklus)
  - **Hotovo, když:** po 1 000 úpravách má server stejný text jako editor (ověřeno požadavkem na obsah nebo diagnostikou testovacího serveru); pád serveru se ohlásí a restart znovu synchronizuje otevřené dokumenty.
  - **Závisí na:** P8.4.1, P3b.1.1
  - **Odhad:** v rámci F157 (viz P8.5.1)
  - **Umístění:** vestavěný plugin
- [ ] **P8.5.5** LSP: navigace a diagnostika
  - **Co a proč:** Definice, odkazy a diagnostika z LSP přes API z P8.4.1.
  - **Funkce:** F157 (částečně: navigace a diagnostika)
  - **Hotovo, když:** skok na definici trefí bajty, které vrátil server, i v souboru s vícebajtovými znaky; diagnostika zmizí po opravě a nové revizi.
  - **Závisí na:** P8.5.4
  - **Odhad:** v rámci F157 (viz P8.5.1)
  - **Umístění:** vestavěný plugin
- [ ] **P8.5.2** Jazykové servery z manifestu pluginu
  - **Co a proč:** Plugin nabídne jazykový server v manifestu (`languageServers`); registr ověří manifest a souhlas se spuštěním.
  - **Funkce:** F208
  - **Hotovo, když:** datový plugin s jazykovým serverem se po potvrzení spustí pro svůj jazyk bez dalšího kódu.
  - **Závisí na:** P8.5.1, P3b.2.1
  - **Odhad:** F208 0,5–1 ČT (matice)
  - **Umístění:** jádro
- [ ] **P8.5.3** Katalog jazykových nástrojů
  - **Co a proč:** Jeden deklarativní záznam na skupinu jazyků: přípony a jména souborů, gramatika, příkaz LSP s ověřenou verzí, formátovač, linter s formátem výstupu a tabulkou závažností, konfigurační soubory, manifest. Na začátku 17 skupin z tabulky analýza konkurence. Editor nástroj jen najde (PATH, `node_modules/.bin`, `vendor/bin`, `.venv`) nebo použije cestu od uživatele; nic nestahuje. Navíc Ruff, Prettier/Biome, ShellCheck, shfmt, SQLFluff, StyLua, `dart format`.
  - **Funkce:** F213
  - **Hotovo, když:** existuje verzovaná tabulka skupina → formát → nástroj a verze → scénář → OS pro všech 17 skupin; pro každou slíbenou kombinaci projde úspěch, syntaktická chyba, chybějící nástroj, timeout a zakázaná síť; editor nástroj najde v PATH i v lokální složce projektu a nic nestáhne.
  - **Závisí na:** P8.5.1, P8.3.1
  - **Odhad:** F213 1–2 ČT (matice); další nástroje 1–2 ČT
  - **Umístění:** data, čte je jádro a podle nich spouští LSP plugin

### P8.6 Git

- [ ] **P8.6.1** Git: stage úseku a konflikt
  - **Co a proč:** Stage vybraného úseku přes kontrakt z P6.2.2 a předání konfliktu. Ne vlastní náhrada celého Git klienta. Stav a diff jsou v P8.6.2.
  - **Funkce:** F158
  - **Hotovo, když:** stage jednoho úseku ze souboru se třemi změnami přidá do indexu jen ten úsek (ověřeno `git diff --cached`); změna indexu jiným programem mezi náhledem a stage skončí konfliktem.
  - **Závisí na:** P8.6.2, P6.2.2
  - **Odhad:** F158 3–5 ČT (matice) za P8.6.1 a P8.6.2
  - **Umístění:** vestavěný plugin
- [ ] **P8.6.2** Git: stav a diff
  - **Co a proč:** Stav souborů v panelu a diff proti indexu a `HEAD` přes diff API.
  - **Funkce:** F158 (částečně: stav a diff)
  - **Hotovo, když:** stav souboru v panelu odpovídá `git status` i po změně zvenku; diff proti indexu a proti `HEAD` ukáže stejné úseky jako `git diff`.
  - **Závisí na:** P8.4.1, P6.2.2
  - **Odhad:** v rámci F158 (viz P8.6.1)
  - **Umístění:** vestavěný plugin

### P8.7 Schémata a manifesty

- [ ] **P8.7.1** Schémata pro JSON, YAML a TOML
  - **Co a proč:** Mapování schématu (JSON Schema Store) na soubor, složku nebo masku, kontrola a doplňování. Přibalená schémata běžných konfigurací, stahování jen po svolení a jedním přepínačem vypnutelné. TOML a soubory bez přípony podle jména. Dialekt podle souboru (striktní JSON, JSONC).
  - **Funkce:** F181
  - **Hotovo, když:** chyba v `package.json` proti schématu se podtrhne; doplňování klíčů podle schématu projde přes API doplňování (P8.4.2) a odpověď pro starší revizi se nezobrazí; každý přibalený formát (JSON, JSONC, YAML, TOML) má test platného souboru, syntaktické chyby a chyby proti schématu; bez svolení editor schéma nestáhne (síťový test PR.6.4); stavový řádek ukáže „bez schématu / schéma X“.
  - **Závisí na:** P8.4.1, P8.4.2
  - **Odhad:** F181 2–4 ČT (matice); TOML a další schémata 0,5–1 ČT
  - **Umístění:** vestavěný plugin, schémata jako data
- [ ] **P8.7.2** Soubory agentů a kontrola frontmatteru
  - **Co a proč:** Mapování „cesta → schéma → dialekt“ pro `.claude/settings*.json` (striktní JSON), `.mcp.json`, Codex `config.toml`, `opencode.json(c)` (JSONC); vlastní malé schéma pro `.mcp.json`. Kontrola YAML frontmatteru `SKILL.md`, `agents/*.md`, `rules/*.md`, `.mdc` v profilech Agent Skills a Claude Code.
  - **Funkce:** F250, F251
  - **Hotovo, když:** komentář v `.claude/settings.json` a `name` neshodné se složkou v `SKILL.md` editor podtrhne dřív, než agent spadne; oba profily frontmatteru (Agent Skills, Claude Code) mají testovací soubory s platným i neplatným obsahem.
  - **Závisí na:** P8.7.1
  - **Odhad:** F250 1–2 ČT, F251 1–2 ČT (matice)
  - **Umístění:** data nad F181 (F250); vestavěný plugin spolu s F181 a datovým profilem (F251), podle plánu
- [ ] **P8.7.3** Manifesty balíčků: společný klient, souhlas a cache
  - **Co a proč:** V manifestech doplnění názvu a verze z registru na vyžádání, značka novější verze, spuštění skriptu přes F124. Společný klient registru se souhlasem a cache; jednotlivé registry jsou adaptéry v P8.7.5. Síť jen po svolení, bez kontroly zranitelností a bez automatického `go mod download`.
  - **Funkce:** F183
  - **Hotovo, když:** bez svolení editor nepošle žádný dotaz do registru (ověřeno síťovým testem PR.6.4); po svolení se u starší verze ukáže značka novější verze; odpověď z cache se pozná a jde obnovit.
  - **Závisí na:** P8.7.1, P8.2.1, P5.4.4
  - **Odhad:** F183 3–5 ČT (matice) za P8.7.3 a P8.7.5
  - **Umístění:** vestavěný plugin
- [ ] **P8.7.5** Adaptéry registrů balíčků
  - **Co a proč:** Adaptéry ve skupinách s vlastními testy: `composer.json`, `package.json`, `requirements.txt`, `.csproj`, `go.mod`, `Cargo.toml`, `Gemfile`.
  - **Funkce:** F183 (částečně: adaptéry registrů)
  - **Hotovo, když:** každý adaptér má test doplnění názvu, verze a značky novější verze proti nahrané odpovědi registru a test chyby registru.
  - **Závisí na:** P8.7.3
  - **Odhad:** v rámci F183 (viz P8.7.3); Go, Rust, Ruby 1–2 ČT
  - **Umístění:** vestavěný plugin
- [ ] **P8.7.4** Konfigurace platná pro soubor
  - **Co a proč:** Stavový řádek ukáže konfigurace platné pro kartu (`.editorconfig`, `.clang-format`, `.clang-tidy`, `.rubocop.yml`, `rustfmt.toml`, `.golangci.yml`), klik otevře, chybějící jde založit ze šablony. Jen procházení nadřazených složek, bez spouštění nástrojů.
  - **Funkce:** F210
  - **Hotovo, když:** u souboru v podsložce stavový řádek ukáže nejbližší nadřazený `.editorconfig` a `.clang-format`.
  - **Závisí na:** P2.6.1, P8.5.3
  - **Odhad:** F210 0,5–1 ČT (matice)
  - **Umístění:** jádro

### P8.8 Lintery a gramatiky

- [ ] **P8.8.1** Podtržení z PHPStanu a linterů
  - **Co a proč:** Výstup PHPStanu, Psalmu, golangci-lint, `cargo clippy`, clang-tidy, RuboCopu jako podtržení v textu a seznam. Tabulka převodu závažností pro každý nástroj. Spuštění při uložení nebo ručně, ne při každé změně; časový limit s hlášením.
  - **Funkce:** F125
  - **Hotovo, když:** chyba PHPStanu se podtrhne na správném řádku a sloupci; po opravě a novém běhu zmizí; každý linter z tabulky P8.5.3 má test úspěchu, syntaktické chyby, chybějícího nástroje a timeoutu; nástroj, který překročí limit, se ohlásí a UI dál reaguje.
  - **Závisí na:** P8.4.1, P4.3.1, P8.3.1
  - **Odhad:** 1–2 ČT a lintery 0,5–1 ČT
  - **Umístění:** jádro, vzory jako data
- [ ] **P8.8.2** Gramatiky: Latte, NEON a 17 skupin jazyků
  - **Co a proč:** Latte a NEON vedle Blade a Twig, párování `{if}…{/if}`. Go, `go.mod`, Go šablony, Rust, TOML, C, C++, CMake, Makefile (`Tab` vždy tabulátor), Meson, Ruby, ERB, HAML, Slim a další ze 17 skupin z tabulky analýza konkurence.
  - **Funkce:** F121
  - **Hotovo, když:** každá gramatika ze seznamu v tabulce P8.5.3 má snapshot test; v Makefile vloží `Tab` tabulátor i při odsazení mezerami.
  - **Závisí na:** P3.2.1, P4.7.1
  - **Odhad:** Latte a NEON 0,5–1 ČT, gramatiky 3. kola 1–2 ČT
  - **Umístění:** jádro (engine), gramatiky jako data

### P8.9 Vložené fragmenty

- [ ] **P8.9.1** Úprava vloženého fragmentu
  - **Co a proč:** Řetězec v PHP nebo JS „upravit jako JSON / SQL / HTML“ v samostatné kartě, propojené s rodičem kotvami; při zápisu zpět editor doplní escapování.
  - **Funkce:** F175
  - **Hotovo, když:** JSON s uvozovkami upravený v samostatné kartě se vrátí do PHP řetězce se správným escapováním; změna rodiče mezitím vyvolá konflikt, ne přepsání.
  - **Závisí na:** P1.2.5, P3.2.1
  - **Odhad:** F175 2–4 ČT (matice)
  - **Umístění:** jádro

### P8.10 Šablony binárních formátů

- [ ] **P8.10.1** Šablony struktur `.ksy`: strom, panel a připnuté formáty
  - **Co a proč:** Podmnožina Kaitai `.ksy` s vlastním interpretem v C#. Panel Struktura ↔ hex, `Ctrl+J`, automatická nabídka podle přípony a magic. Kompilátor Kaitai (GPL-3) se nepřibalí; přibalené formáty jen CC0, MIT, BSD. Přibalené šablony jsou připnuté konkrétní soubory s verzí a licencí; slíbit celou galerii podle názvu formátu nejde. Výběr šablon je v P8.10.5, parser a validace v P8.10.2, líné čtení a limity v P8.10.3, výrazy v P8.10.4. Kompatibilita dnešního návrhu se šablonami PNG, ZIP, ELF a PE je **neověřená**.
  - **Funkce:** F245
  - **Hotovo, když:** každá šablona ze seznamu P8.10.5 má validní i poškozený vstup s očekávaným rozsahem polí; klik na pole označí správné bajty; šablona s `process` vypíše „nepodporováno: process“; strom 10GiB ZIPu přidá nejvýš 100 MiB paměti.
  - **Závisí na:** P8.10.5, P8.10.2, P8.10.3, P8.10.4, P3.5.2, P3.5.6
  - **Odhad:** F245 3–5 ČT (matice) za P8.10.1–P8.10.5; revize ho považuje za optimistický
  - **Umístění:** jádro (hex, kap. 5.5)
- [ ] **P8.10.5** Výběr připnutých šablon `.ksy`
  - **Co a proč:** Parser nemůže stát na výběru šablon, který vznikne až při integraci. Nejdřív se vyberou skutečné soubory a z nich se odvodí podporovaná podmnožina a testovací vstupy.
  - **Funkce:** F245 (částečně: výběr šablon)
  - **Hotovo, když:** v repozitáři je seznam připnutých souborů `.ksy`, každý s verzí nebo commitem zdroje, licencí CC0, MIT nebo BSD a výčtem použitých konstrukcí; podporovaná podmnožina P8.10.2 je sjednocením těchto konstrukcí, nebo seznam říká, která šablona se kvůli nepodporované konstrukci nepřibalí; ke každé šabloně je určený platný a poškozený vstup.
  - **Závisí na:** P3.5.2
  - **Odhad:** v rámci F245 (viz P8.10.1)
  - **Umístění:** data (šablony), jádro (seznam konstrukcí)
- [ ] **P8.10.2** Parser a validace podmnožiny `.ksy`
  - **Co a proč:** Podporované konstrukce: `meta`, `seq`, `types`, `enums`, čísla, `str`/`strz`, `size`, `repeat`, `if`, `switch-on`, `instances`, `contents`, upřesněné podle seznamu P8.10.5. Nepodporovaná konstrukce se ohlásí jménem.
  - **Funkce:** F245 (částečně: parser)
  - **Hotovo, když:** každá konstrukce ze seznamu P8.10.5 má test se šablonou a očekávaným stromem; neplatná šablona se odmítne s řádkem a důvodem.
  - **Závisí na:** P8.10.5
  - **Odhad:** v rámci F245 (viz P8.10.1)
  - **Umístění:** jádro (hex, kap. 5.5)
- [ ] **P8.10.3** Líné čtení a limity proti škodlivému vstupu
  - **Co a proč:** Pole se čtou až při rozbalení. Limity hloubky rekurze, počtu opakování, rozsahů, času a paměti chrání před škodlivým datovým vstupem.
  - **Funkce:** F245 (částečně: limity)
  - **Před zahájením:** hodnoty všech limitů se zapíšou jako vstupní rozhodnutí (úvod TODO).
  - **Hotovo, když:** vstup s nekonečnou rekurzí, obrovským `repeat` a rozsahem za koncem souboru skončí každý jasnou chybou v limitu a editor dál reaguje.
  - **Závisí na:** P8.10.2
  - **Odhad:** v rámci F245 (viz P8.10.1)
  - **Umístění:** jádro (hex, kap. 5.5)
- [ ] **P8.10.4** Výrazy a podmínky v šablonách
  - **Co a proč:** Jednoduché výrazy pro `size`, `if`, `switch-on` a `instances`, bez spouštění kódu.
  - **Funkce:** F245 (částečně: výrazy)
  - **Hotovo, když:** každý podporovaný operátor má test; dělení nulou a odkaz na neexistující pole vrátí chybu pole, ne pád.
  - **Závisí na:** P8.10.2
  - **Odhad:** v rámci F245 (viz P8.10.1)
  - **Umístění:** jádro (hex, kap. 5.5)

## P8b – veřejné SDK a API

Odhad etapy 12,5–20 ČT. Položky F202, F203 a F205 dávají 5–7 ČT, AI funkce F253, F255 a F257 5,5–9 ČT, dohromady 10,5–16 ČT. Zbytek 2–4 ČT patří distribuci (P8b.1.1), pluginu nezávislého autora (P8b.2.4) a dopracování dokumentace širokého SDK; zapsaný je jednou, u P8b.2.4. Hotovo, když: vlastní pluginy F205 a plugin nezávislého autora běží na Windows i macOS proti AOT buildu; ověřovač odmítne manifest s neplatným rozsahem `api`; kontraktové testy pokrývají všechna rozhraní API. Podmínky: API z P3b, P6 a P8; nezávislý autor zajištěný v PR.8.3 před zahájením etapy. Podíl P8b.1.1, P8b.2.4 a dokumentace SDK ze zbytku 2–4 ČT určí PR.8.2.

### P8b.1 Distribuce pluginů na macOS

- [ ] **P8b.1.1** Zkouška distribuce na začátku P8b
  - **Co a proč:** R06 žádá zkoušku distribuce na začátku P8b, před rozšířením SDK: oba RID na macOS, přibalený i cizí plugin, chybějící runtime skriptového pluginu. Ověřuje veřejnou distribuci; skript a evidenci sdílí s P3b.5.4 a P3b.5.2, vývoj harnessu se znovu neúčtuje.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** cizí plugin stažený jako ZIP, plugin v Pythonu bez nainstalovaného Pythonu a přibalený plugin mají na Apple Silicon i Intel, na macOS 14 i novějších verzích ze seznamu P1.1.6, předem zapsané očekávání a výsledek; srozumitelná chyba je přijatelná jen u předem určeného nepodporovaného případu (plugin v Pythonu bez Pythonu); zablokovaný podporovaný podepsaný plugin nechá bod otevřený jako u P3b.5.4.
  - **Závisí na:** P3b.5.2
  - **Odhad:** v rámci zbytku rozpočtu P8b (viz P8b.2.4)
  - **Umístění:** jádro

### P8b.2 Veřejné SDK a ověřování

- [ ] **P8b.2.1** Veřejné C# SDK
  - **Co a proč:** NuGet `NksHub.Textida.Plugin.Sdk` (AOT, `net10.0`), šablona `dotnet new nkspad-plugin` pro `win-x64`, `osx-arm64`, `osx-x64`, JSON Schema API, testovací hostitel bez editoru. Dokumentace kontraktů a skutečných chybových stavů pro celé široké API: P3b, vzdálené zdroje, diff a stage, náhled, panel agenta a virtuální dokumenty z P6.2, symboly a snippety z P3b.3.5, diagnostika, doplňování a workspace edit z P8.4. Definice API generují DTO, schéma a základ dokumentace; ručně se píše sémantika, chybové stavy a scénáře. Jazyk SDK a jeho dokumentace určuje matice P1.1.7; čtyři jazyky UI se na něj automaticky nevztahují. SDK se nepřibaluje do instalátoru editoru.
  - **Funkce:** F202
  - **Hotovo, když:** plugin vytvořený šablonou se sestaví pro tři RID a projde testovacím hostitelem s přehranými událostmi; každý příklad v dokumentaci se v CI sestaví a spustí proti aktuálnímu API; každý chybový stav API má v dokumentaci popis a postup návratu.
  - **Závisí na:** P3b.5.1, P8b.1.1, P3b.3.5, P6.2.3, P6.2.2, P6.2.4, P6.2.5, P6.2.6, P8.4.2, P8.4.3
  - **Odhad:** F202 2–3 ČT (matice)
  - **Umístění:** jádro (vývojová infrastruktura)
- [ ] **P8b.2.2** Ověřovač pluginů a kontraktové testy
  - **Co a proč:** Staticky kontroluje manifest, rozsah `api` a deklarace, za běhu handshake a validaci RPC. Bezpečnostní audit binárky neslibuje (R14). Kontraktové testy pokrývají všechna rozhraní z P8b.2.1, nejen API v1.
  - **Funkce:** F203
  - **Hotovo, když:** ověřovač odmítne manifest s neplatným rozsahem `api`; každé rozhraní z P8b.2.1 má kontraktový test s pozitivním scénářem, konfliktem revize a chybou oprávnění; testy procházejí na všech třech RID.
  - **Závisí na:** P8b.2.1
  - **Odhad:** F203 1 ČT (matice); revize ho pro široké API považuje za optimistický
  - **Umístění:** jádro (vývojová infrastruktura)
- [ ] **P8b.2.3** Ověřovací sada vlastních pluginů
  - **Co a proč:** JSON formátovač, statistika, osnova Markdownu a datový balíček (klip, převodník, gramatika) proti distribuovanému AOT buildu. Převodník typu souboru F138 se nedělá, kontrakt není navržený.
  - **Funkce:** F205
  - **Hotovo, když:** všechny čtyři pluginy běží na Windows i macOS proti AOT buildu a datový balíček se odinstaluje bez poškození uživatelských dat.
  - **Závisí na:** P3b.5.3, P4.8.1, P4.4.1, P4.1.1
  - **Odhad:** F205 2–3 ČT (matice)
  - **Umístění:** vestavěný plugin
- [ ] **P8b.2.4** Plugin nezávislého autora
  - **Co a proč:** Plugin nezávislého autora jen z veřejné dokumentace, bez přístupu do interního kódu. Je to podmínka **vydání editoru 1.0** a podle R9 (rozhodnuto 26. 10. 2026) i jedna ze dvou podmínek API 1.0: API zůstává 0.x, dokud kontraktové testy (P8b.2.2) neprojdou na Windows i macOS a aspoň jeden nezávislý autor nenapíše plugin jen podle dokumentace. Počet cizích pluginů podmínkou není. Nezávislý autor není druhý proces ani další vlastní ukázkový plugin. Autora zajistí PR.8.3 před zahájením P8b.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** záznam uvádí autora, verzi dokumentace a SDK, zdroj nebo sestavitelný balík pluginu, scénář, přesný AOT build a výsledky na všech třech RID; plugin na nich běží; připomínky autora k dokumentaci jsou vyřízené a relevantní kontraktové testy P8b.2.2 se po nich zopakovaly a prošly; stav API (0.x, nebo 1.0 po splnění obou podmínek R9) je v záznamu vydání uvedený zvlášť od verze editoru.
  - **Závisí na:** P8b.2.1, P8b.2.2, PR.8.3
  - **Odhad:** zbytek rozpočtu P8b 2–4 ČT za P8b.1.1, P8b.2.4 a dokumentaci SDK
  - **Umístění:** jádro

### P8b.3 Vestavěné pluginy pro práci s AI

- [ ] **P8b.3.1** Editor hooků
  - **Co a proč:** Hooky Claude Code (`settings.json`) a Codexu (`hooks.json`, `[hooks]`) jako strom událost → matcher → handler nad textem souboru. Kontroly a „Vyzkoušet“ s ukázkovým JSON na stdin a výkladem návratového kódu. Spuštění jen přes `process.exec` a s potvrzením příkazu, zápis jako návrh do F178.
  - **Funkce:** F253
  - **Hotovo, když:** pro každou podporovanou verzi a druh hooku obou produktů existuje tabulka očekávaných výsledků podle návratového kódu a test ji projde (u Claude Code `exit 1` neblokující a `exit 2` blokující); bez potvrzení se nic nespustí. Platnost jedné tabulky pro oba produkty je **neověřená**.
  - **Závisí na:** P8.7.2, P6.1.2
  - **Odhad:** F253 1,5–2,5 ČT (matice)
  - **Umístění:** vestavěný plugin
- [ ] **P8b.3.2** Odhad tokenů
  - **Co a proč:** Položka stavového řádku „≈ 1 240 tok.“ pro výběr. Pro OpenAI místní tokenizér o200k (`Microsoft.ML.Tokenizers`, pod AOT neověřeno), pro Claude jen „≈“. Přesný počet přes count_tokens jen po kliknutí, s klíčem z klíčenky.
  - **Funkce:** F255
  - **Hotovo, když:** počet tokenů pro testovací texty se shoduje s připnutou verzí tokenizéru; odhad pro Claude je v UI viditelně označený „≈“; bez kliknutí na „Spočítat přesně“ neodejde žádný síťový požadavek (síťový test PR.6.4); ve stránkovaném režimu se počítá jen výběr.
  - **Závisí na:** P3b.2.2, P3b.3.1
  - **Odhad:** F255 1–1,5 ČT (matice)
  - **Umístění:** vestavěný plugin
- [ ] **P8b.3.3** Prohlížeč přepisů agentů: panel tahů
  - **Co a proč:** `*.jsonl` z `~/.claude/projects/` a `~/.codex/sessions/` jako relace: panel tahů, tokeny, hledání, skok do surového řádku. Editor kreslí panel a virtuální dokument z P6.2.6. Neznámý záznam jako surový JSON. Jen pro čtení. Parsery a index jsou v P8b.3.4, maskování a limity v P8b.3.5.
  - **Funkce:** F257
  - **Hotovo, když:** přepis 1,2 GiB s řádkem 54 MiB se otevře s prvním obrazem ≤ 1 s warm / 2 s cold (cíl kap. 3 pro 10 GiB) a panel tahů se plní, zatímco posun má p95 ≤ 33 ms; soubor se nikdy nezapíše.
  - **Závisí na:** P8b.3.4, P8b.3.5, P6.2.6, P3b.3.1
  - **Odhad:** F257 3–5 ČT (matice) za P8b.3.3–P8b.3.5
  - **Umístění:** vestavěný plugin
- [ ] **P8b.3.4** Přepisy agentů: parsery a index
  - **Co a proč:** Plugin postaví index „tah → offset“ ve vlastním procesu, pro formát Claude Code i Codexu, nad stránkovaným čtením.
  - **Funkce:** F257 (částečně: parsery a index)
  - **Hotovo, když:** index testovacích přepisů obou formátů dá stejné tahy a offsety jako referenční průchod; neznámý typ záznamu se ukáže jako surový JSON.
  - **Závisí na:** P3.1.1, P3.1.3, P3b.3.1
  - **Odhad:** v rámci F257 (viz P8b.3.3)
  - **Umístění:** vestavěný plugin
- [ ] **P8b.3.5** Přepisy agentů: maskování a limity
  - **Co a proč:** Maskování F184 přes sdílený filtr z P4.6.2 platí v panelu, ve virtuálním dokumentu i v hledání. Index má strop paměti.
  - **Funkce:** F257 (částečně: maskování a limity)
  - **Před zahájením:** strop paměti indexu v MiB se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Hotovo, když:** hodnota z `.env` uvnitř přepisu se neukáže v panelu, ve virtuálním dokumentu ani ve výsledcích hledání; index 1,2GiB přepisu nepřekročí zapsaný strop paměti.
  - **Závisí na:** P8b.3.4, P4.6.2
  - **Odhad:** v rámci F257 (viz P8b.3.3)
  - **Umístění:** vestavěný plugin

## P9 – použitelnost a vydání

Odhad etapy 10,5–16 ČT, **neúplný**: pochází z doby před R10 a nemá doložené pokrytí čtyř jazyků UI a rozšířeného platformního ověření. Cenu důsledků R10 určí PR.8.2 po inventáři P1.1.7. Hotovo, když: podporované scénáře projdou klávesnicí i čtečkou v matici R10; aktualizace a rollback.

### P9.1 Přístupnost

- [ ] **P9.1.1** Přístupnost celé aplikace
  - **Co a proč:** Všechny obrazovky, dialogy a panely včetně příspěvků pluginů projdou klávesnicí a čtečkou. Navazuje na P1.8.1 a průběžné kontroly PR.4.1.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** každý podporovaný scénář z zadání projde klávesnicí i čtečkou (NVDA, VoiceOver) v matici R10 (PR.4) s předem zapsaným očekáváním textu, pozice a fokusu, na buildu 1.0 a ve všech čtyřech jazycích UI; žádná blokující odchylka nezůstala.
  - **Závisí na:** PR.4.1, P3.1.5, P7.1.7
  - **Odhad:** bez odhadu
- [ ] **P9.1.2** Plná přístupnost hex
  - **Co a proč:** Všechny akce hexu jsou v paletě a menu, na macOS bez klávesy `Insert`; panely inspektoru, záložek a struktury jako pojmenované seznamy; vysoký kontrast tříd bajtů a změn i podtržením.
  - **Funkce:** F248
  - **Hotovo, když:** NVDA i VoiceOver přečtou offset a hodnotu při pohybu kurzoru; všechny akce hexu jsou v paletě a menu a na macOS jdou bez `Insert`.
  - **Závisí na:** P3.5.8, P5.6.3, P8.10.1
  - **Odhad:** v rámci F248 (matice, viz P3.5.8)
  - **Umístění:** jádro

### P9.2 Nastavení a překlady

- [ ] **P9.2.1** Pokročilé volby
  - **Co a proč:** Pokročilé volby ve verzovaném schématu, bez původního INI.
  - **Funkce:** F120
  - **Před zahájením:** seznam ID pokročilých voleb se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Hotovo, když:** seznam ID pokročilých voleb v testu je uzavřený; každá volba je v prohledávatelném nastavení se zdrojem hodnoty, má test účinku a projde migrací schématu; nová volba bez testu bránou neprojde.
  - **Závisí na:** P1.7.2
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P9.2.2** Překlady rozhraní
  - **Co a proč:** Kap. 6 P9 má překlady. Navazuje na textové prostředky z P1.1.5. Jazyky UI pro 1.0 jsou čeština, angličtina, slovenština a němčina (R10); ostatní dodávané texty určuje matice P1.1.7. Import starých překladů PSPadu až po 1.0. Úplný seznam klíčů není úplný test lokalizace. Úplný kartézský součin jazyků a jádrových testů není nutný; rozsah testů je ale zapsaný.
  - **Funkce:** žádná (rozšíření textových prostředků z P1.1.5)
  - **Kroky:**
    - inventář textů v jednom seznamu: menu, paleta, stavový řádek, skryté panely, dialogy, chybová hlášení, přístupné názvy, příspěvky vlastních pluginů, updater a obrazovky obnovy
    - překlad, kontrola rodilým mluvčím nebo odpovědným překladatelem, opravy UI
  - **Hotovo, když:** pro každý z jazyků cs, en, sk a de je inventář klíčů úplný a každý text má kontrolu se jménem kontrolora; testy pokryjí zástupné parametry a množná čísla, nejdelší německé popisky bez oříznutí, kolize akcelerátorů v menu, přístupné názvy, hledání přeložených příkazů v paletě, přepnutí jazyka s otevřenými panely a to, že změna jazyka nezmění bajty dokumentu ani strojové formáty nastavení, schémat a protokolů; UI kontroly proběhnou na Windows i macOS ve všech textových plochách; chybějící překlad se ohlásí v testu a za běhu se ukáže výchozí jazyk, ne prázdný popisek.
  - **Závisí na:** P1.1.5, P1.1.7
  - **Odhad:** bez odhadu; cenu určí PR.8.2 po inventáři P1.1.7
  - **Umístění:** jádro, texty jako data
- [ ] **P9.2.3** Nápověda pro 1.0
  - **Co a proč:** P3.6.5 přijímá nápovědu jen pro příkazy MVP. Verze 1.0 přidá pluginy, vzdálené soubory, MCP, agenty, velkou editaci a další funkce; nápověda F142 se rozšíří o ně a její přejímka se váže na vydání.
  - **Funkce:** žádná (rozšíření nápovědy z P3.6.5)
  - **Hotovo, když:** každý příkaz z registru v buildu 1.0, včetně příkazů přibalených pluginů, má v nápovědě postup ověřený na tomto buildu; nápověda popisuje chybová hlášení, obnovu, limity a omezení funkcí 1.0 a co s nimi dělat; je dostupná bez sítě a v jazycích, které pro 1.0 určila P1.1.7; záznam uvádí SHA buildu 1.0.
  - **Závisí na:** P3.6.5, P1.1.7
  - **Odhad:** bez odhadu; ocení PR.8.2
  - **Umístění:** jádro, texty jako data

### P9.3 Náhledy a export

- [ ] **P9.3.1** HTML a Markdown náhled
  - **Co a proč:** Bezpečný náhled bez automatického spouštění aktivního obsahu, v jádře nad rendererem a bezpečnostní politikou z P6.2.7: skript, vzdálené zdroje i odkazy na místní soubory jsou bez schválení zakázané. Tady se politika rozšíří na HTML a Markdown a zopakuje s Mermaid (P9.3.3). Panel na požádání. Pluginy kreslí přes řízený náhled z P6.2.4.
  - **Funkce:** F099
  - **Hotovo, když:** stránka s vloženým skriptem se v náhledu ukáže bez spuštění skriptu; vzdálený obrázek se bez schválené politiky nenačte a nevznikne žádné síťové spojení; odkaz na místní soubor se bez schválení neotevře; Markdown náhled se aktualizuje při psaní; vše projde na systémech ze seznamu P1.1.6 včetně Windows 10 22H2 a macOS 14 a náhled jde ovládat klávesnicí a čtečkou.
  - **Závisí na:** P3.6.3, P6.2.7, P6.2.4
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P9.3.2** Export se zvýrazněním
  - **Co a proč:** Základní HTML a RTF export a schránka se stejnými tokeny a barvami jako zobrazení; TeX a PDF později.
  - **Funkce:** F039
  - **Hotovo, když:** export do HTML dá stejné barvy tokenů jako editor; kopie do schránky vloží zvýrazněný text do textového procesoru.
  - **Závisí na:** P3.2.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P9.3.3** Markdown tabulky a obsah
  - **Co a proč:** `Tab` mezi buňkami, `Shift+Enter` nový řádek, zarovnání sloupců, oprava rozbité tabulky, obsah v `<!-- TOC -->`, Mermaid v náhledu F099 přes řízený vstup z P6.2.4. `Tab` mimo tabulku plugin předá zpět jádru (P3b.3.5). Revize upozorňuje, že API v1 na celou funkci nestačí.
  - **Funkce:** F190
  - **Hotovo, když:** `Tab` v tabulce přeskočí do další buňky a sloupce se zarovnají; `Tab` mimo tabulku odsadí jako bez pluginu; aktualizace obsahu přepíše jen blok `<!-- TOC -->`; diagram Mermaid se vykreslí v náhledu a nespustí skript ani síťový požadavek.
  - **Závisí na:** P9.3.1, P6.2.4, P3b.3.5
  - **Odhad:** F190 1–2 ČT (matice)
  - **Umístění:** vestavěný plugin (balíček Markdown a osnova)

### P9.4 Logy

- [ ] **P9.4.1** Živé pohledy na log a hledání: uložené pohledy
  - **Co a proč:** Uložené připnuté filtry nad rostoucím velkým souborem, klik skočí do logu, obnova pohledu po restartu. Ověřit uživatelským scénářem (kap. 9). Inkrementální filtr je v P9.4.3, rotace a zkrácení logu v P9.4.4.
  - **Funkce:** F166
  - **Hotovo, když:** uložený pohled přežije restart a vrátí stejné filtry a pozici; klik na shodu skočí na její bajty v logu.
  - **Závisí na:** P9.4.3, P9.4.4, P1.2.5
  - **Odhad:** F166 3–5 ČT navíc (matice) za P9.4.1, P9.4.3 a P9.4.4
  - **Umístění:** jádro
- [ ] **P9.4.3** Inkrementální filtr rostoucího logu
  - **Co a proč:** Sledování přibývajících řádků; nové shody se hledají jen v přírůstku.
  - **Funkce:** F166 (částečně: inkrementální filtr)
  - **Hotovo, když:** filtr ERROR nad rostoucím 10GiB logem přidává nové shody bez nového prohledání celého souboru; shody souhlasí s naivním průchodem výsledného souboru.
  - **Závisí na:** P3.1.4
  - **Odhad:** v rámci F166 (viz P9.4.1)
  - **Umístění:** jádro
- [ ] **P9.4.4** Rotace a zkrácení logu
  - **Co a proč:** Log, který jiný program zkrátí nebo vymění při rotaci, se pozná podle otisku podkladu (P1.2.3).
  - **Funkce:** F166 (částečně: rotace)
  - **Hotovo, když:** po zkrácení i po rotaci logu pohled ohlásí změnu, staré shody označí jako neplatné a pokračuje na novém souboru.
  - **Závisí na:** P9.4.3, P1.2.3
  - **Odhad:** v rámci F166 (viz P9.4.1)
  - **Umístění:** jádro
- [ ] **P9.4.2** Plný režim logu
  - **Co a proč:** Sbalení nezajímavých řádků, odkazy ze stack trace na soubor, heatmapa chyb v minimapě, barvy ANSI, i ve stránkovaném režimu.
  - **Funkce:** F187
  - **Hotovo, když:** klik na řádek stack trace otevře soubor na řádku; ANSI barvy se vykreslí bez změny bajtů; heatmapa ukáže shluky chyb v 10GiB logu.
  - **Závisí na:** P4.3.2, P3.3.1
  - **Odhad:** v rámci F187 (matice, viz P4.3.2)
  - **Umístění:** jádro

### P9.5 Pracovní prostor

Kap. 6 tyto funkce 1.0 do žádné etapy nezařazuje; jsou tu, protože patří k použitelnosti.

- [ ] **P9.5.1** Projekty a oblíbené
  - **Co a proč:** Jednoduchý workspace (složka bez projektu funguje od MVP) a oblíbené cesty. Import PPR a Links až po 1.0.
  - **Funkce:** F007, F011
  - **Hotovo, když:** workspace si pamatuje složky a otevřené karty; oblíbená cesta se otevře z panelu a chybějící je označená.
  - **Závisí na:** P2.3.1, P1.5.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P9.5.2** Pojmenované relace
  - **Co a proč:** Pojmenované sady souborů nad stejným úložištěm jako hot exit.
  - **Funkce:** F028
  - **Hotovo, když:** uložená relace se otevře i s neuloženým obsahem a pozicemi kurzorů.
  - **Závisí na:** P1.5.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro
- [ ] **P9.5.3** Zvláštní otevření
  - **Co a proč:** Zvláštní otevření PSPadu sjednocené do palety a souborového panelu.
  - **Funkce:** F031
  - **Před zahájením:** seznam variant zvláštního otevření se zapíše jako vstupní rozhodnutí (úvod TODO).
  - **Hotovo, když:** seznam variant zvláštního otevření v testu je uzavřený; každá je příkaz v paletě a v kontextovém menu panelu a má test vstupu a výsledku.
  - **Závisí na:** P2.1.1, P2.3.1
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

### P9.6 Distribuce a aktualizace

- [ ] **P9.6.1** Distribuce 1.0, aktualizace a návrat
  - **Co a proč:** Kap. 6 P9: distribuce a aktualizace s návratem, podpis Authenticode (PR.3.2) a Developer ID s notarizací (PR.3.1). Rozšiřuje aktualizátor z P3.7.2 o přibalené pluginy, jejich verze a migrace; nový aktualizátor nevzniká a v P9 se oceňuje jen rozšíření.
  - **Funkce:** žádná (rozšíření aktualizací z P3.7.2)
  - **Hotovo, když:** aktualizace editoru i s přibalenými pluginy a návrat na předchozí verzi projdou na všech třech RID a na každém systému ze seznamu P1.1.6 bez ztráty nastavení, místní historie a žurnálu; po aktualizaci se ověří podpis a časové razítko (Authenticode, Developer ID) a na macOS notarizační ticket nového balíku i přibalených procesů; upravený nebo neověřený balík editoru i pluginu se odmítne; přerušená výměna se po startu dokončí nebo vrátí; návrat zachová kompatibilní schéma nastavení, žurnálu a dat pluginů; vlastní obrazovky aktualizátoru jsou v jazycích podle P1.1.7; po aktualizaci přibaleného runtime nebo nativních knihoven proběhne znovu matice PR.1.3.
  - **Závisí na:** P3.7.2, P3b.4.2, PR.3.2, PR.1.3
  - **Odhad:** bez odhadu
  - **Umístění:** jádro

## P10 – ověření 1.0

Odhad etapy 8–12 ČT. Hotovo, když: každý slíbený scénář všech 208 funkcí MVP a 1.0 má výsledek na konkrétním buildu pro `win-x64`, `osx-arm64` i `osx-x64`. Do brány musí dorazit i P7 a všechny průběžné kontroly; distribuce, podpisy, přístupnost a měření nejsou až poslední úkol.

### P10.1 Ověření a opravy

- [ ] **P10.1.1** Výkonové brány 1.0
  - **Co a proč:** Všechny cíle sloupce 1.0 z kap. 3 na referenčních strojích R8, na Windows i macOS, nad distribuovaným AOT buildem.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** každý řádek 1.0 tabulky kap. 3 **prošel** se záznamem podle PR.2.2 na buildu 1.0; start a paměť prázdné aplikace jsou z přihlášené relace s platnou prezentací; cold má stejný zapsaný význam a protokol jako v P3.7.3; nesplněný nebo neměřený cíl bránu nesplňuje a má rozhodnutí majitele podle kap. 9.
  - **Závisí na:** PR.2.2, P7.1.4
  - **Odhad:** bez odhadu
- [ ] **P10.1.2** Dlouhé běhy a paměť pluginů
  - **Co a proč:** Dlouhé běhy editoru s přibalenými pluginy. Kontrolní měření naměřilo 36 MiB private bytes pro deset současných procesů pluginů a 110 MiB pro třicet; cíl prázdné aplikace je ≤ 200 MiB. Návrh revize: 8 h opakovaného otevírání a zavírání dokumentů a aktivace balíků pluginů, tolerance přírůstku 20 MiB; délku, cyklus a toleranci schválí majitel před zahájením.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** po schválené délce běhu se zapsaným cyklem a po uklidnění nezůstaly osiřelé procesy ani handly a součet privátní paměti všech procesů (editor i pluginy) vzrostl proti stabilizovanému začátku nejvýš o schválenou toleranci (návrh ≤ 20 MiB); měřeno na obou strojích R8 se všemi přibalenými pluginy aktivními.
  - **Závisí na:** P8b.2.3
  - **Odhad:** bez odhadu
- [ ] **P10.1.3** Migrace vlastního schématu nastavení
  - **Co a proč:** plán P10 určuje migraci vlastního schématu nastavení mezi verzemi. Import PSPadu je až po 1.0, proto se v P10 testuje jen vlastní schéma a jeho návaznost na návrat verze (P3.7.2, P9.6.1).
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** testy migrace schématu nastavení z P1.7.1 projdou pro všechny vydané verze schématu tam i zpět; návrat na předchozí verzi editoru po migraci nepoškodí nastavení ani žurnál.
  - **Závisí na:** P1.7.1, P9.6.1
  - **Odhad:** bez odhadu
- [ ] **P10.1.4** Brána vydání 1.0: výsledky po buildech
  - **Co a proč:** Každý slíbený scénář má výsledek na konkrétním buildu pro všechny tři RID; sloupce matice Build, Win a mac jsou doplněné (PR.7.1). Brána zahrnuje všech 208 F-ID dodávaných do 1.0: 99 funkcí MVP včetně regresí, 109 dalších funkcí 1.0, pokračování rozdělených funkcí (F148, F155, F169, F248) a infrastrukturu bez F-ID, včetně nezávislého autora (P8b.2.4) a kontraktových testů (P8b.2.2), které F-ID nemají. Majitel rozhodl celý rozsah 1.0 bez odkladů; starší menší řezy a odklady z revize se nepoužijí a přesun funkce není průchod branou. Případná pozdější výslovná změna zadání je samostatná událost, ne cesta, jak tuto bránu splnit.
  - **Funkce:** žádná (ověření)
  - **Hotovo, když:** každá z 208 funkcí MVP a 1.0 z matice má stav „hotovo + test“, build a datum ověření pro Windows, Mac arm64 a Mac x64; splněné jsou P10.1.1–P10.1.3, P8b.2.2, P8b.2.4, P9.2.3, PR.1.2, PR.1.3, PR.2.4, PR.4.1, PR.4.2, PR.6.3, PR.6.4, P9.1.1 a P9.6.1, každá se **záznamem pro 1.0** na SHA buildu vydání, ne s výsledkem MVP; kompatibilitní matice R10 je úplná pro build 1.0 včetně aktuálních přibalených pluginů a podpisů; stav API (0.x nebo 1.0 podle R9) je zapsaný zvlášť od verze editoru; žádná položka není ve stavu „neověřeno“.
  - **Závisí na:** PR.7.1, P10.1.1, P10.1.2, P10.1.3, P8b.2.2, P8b.2.4, P9.2.3, PR.1.2, PR.1.3, PR.2.4, PR.4.1, PR.4.2, PR.6.3, PR.6.4, P9.1.1, P9.6.1
  - **Odhad:** bez odhadu

## Později (mimo 1.0)

Funkce s vydáním „později“ nejsou v součtu 1.0. Odhady jsou z matice, kde je uvádí. Hotovo je zde rozsah z matice; podrobné kritérium se doplní při zařazení do etapy.

### PZ.1 Import dat PSPadu

- [ ] **PZ.1.1** Modul `PSPadImport`
  - **Co a proč:** Rozhodnutí majitele 25. 10. 2026: až po 1.0. Jen data, která si uživatel v PSPadu sám vytvořil (FTP spojení, projekty, zkratky, makra, vlastní klipy a zvýrazňovače). Běží nad kopií profilu, originál se nezapisuje, nic se nespouští automaticky. Týká se importních částí F007, F010, F019, F023, F095, F104, F119, F122, F128 a dalších. Formáty jsou v rozboru PSPadu.
  - **Funkce:** žádná
  - **Hotovo, když:** průvodce rozliší převedeno, s odchylkou, nepodporováno, chybí zdroj, neověřeno; opakovaný import nezdvojí klipy; hesla jdou rovnou do klíčenky.
  - **Závisí na:** verze 1.0
  - **Odhad:** bez odhadu

### PZ.2 Síť, výstup a AI

- [ ] **PZ.2.1** Otevření z internetu
  - **Co a proč:** Stažení URL do lokálního dokumentu, bez automatického spuštění obsahu.
  - **Funkce:** F033
  - **Hotovo, když:** URL se stáhne do nové karty a nic z obsahu se nespustí.
  - **Závisí na:** P5.4.4
  - **Odhad:** bez odhadu
- [ ] **PZ.2.2** Tisk
  - **Co a proč:** Tisk se samostatným platformním ověřením.
  - **Funkce:** F038
  - **Hotovo, když:** tisk zvýrazněného textu projde na Windows i macOS.
  - **Závisí na:** P9.3.2
  - **Odhad:** bez odhadu
- [ ] **PZ.2.3** Hledání výběru na webu
  - **Co a proč:** Obecné hledání výběru přes uživatelskou URL, ne starý Google dialog.
  - **Funkce:** F062
  - **Hotovo, když:** výběr se otevře v prohlížeči přes nastavenou šablonu URL.
  - **Závisí na:** P1.6.1
  - **Odhad:** bez odhadu
- [ ] **PZ.2.4** Mapování cesty na URL
  - **Co a proč:** Mapování lokální cesty na URL, nikoli vlastní webový server.
  - **Funkce:** F118
  - **Hotovo, když:** příkaz otevře URL odpovídající souboru podle nastaveného mapování.
  - **Závisí na:** P9.5.1
  - **Odhad:** bez odhadu
- [ ] **PZ.2.5** Dotazy nad textem přes AI
  - **Co a proč:** Volitelný AI nástroj nad vybraným rozsahem s náhledem změny; ne podmínka běžné editace.
  - **Funkce:** F144
  - **Hotovo, když:** dotaz pošle jen vybraný rozsah po potvrzení a odpověď přijde jako návrh do revize F178.
  - **Závisí na:** P6.1.1
  - **Odhad:** bez odhadu

### PZ.3 Jazyk a text

- [ ] **PZ.3.1** Pravopis, diakritika a automatické opravy
  - **Co a proč:** Hunspell včetně češtiny, kontrola jen v komentářích, řetězcích, Markdownu a textu; doplnění diakritiky s náhledem; automatické opravy volitelně; nastavení.
  - **Funkce:** F078, F079, F080, F129
  - **Hotovo, když:** český slovník Hunspell podtrhne chybu v komentáři, ne v identifikátoru; doplnění diakritiky ukáže náhled.
  - **Závisí na:** P3.2.1
  - **Odhad:** bez odhadu
- [ ] **PZ.3.2** Změna velikosti klíčových slov
  - **Co a proč:** Jen jazykově bezpečná změna velikosti klíčových slov, nezávislá na barvě tokenu.
  - **Funkce:** F072
  - **Hotovo, když:** změna nezasáhne řetězce ani komentáře.
  - **Závisí na:** P3.2.1
  - **Odhad:** bez odhadu
- [ ] **PZ.3.3** Lorem ipsum a inspektor znaku
  - **Co a proč:** Generátor ukázkového textu; inspektor znaku místo samostatné ASCII tabulky.
  - **Funkce:** F091, F084
  - **Hotovo, když:** inspektor ukáže kód, jméno a bajty znaku pod kurzorem v kódování souboru.
  - **Závisí na:** P1.3.1
  - **Odhad:** bez odhadu

### PZ.4 Systém a vzhled

- [ ] **PZ.4.1** Kapátko a monitor schránky
  - **Co a proč:** Kapátko po ověření platformního chování; monitor schránky jen výslovně zapnutý.
  - **Funkce:** F087, F088
  - **Hotovo, když:** monitor schránky je ve výchozím stavu vypnutý a zapnutý sbírá jen kopie od zapnutí.
  - **Závisí na:** P4.5.1
  - **Odhad:** bez odhadu
- [ ] **PZ.4.2** Otevření chráněné cesty
  - **Co a proč:** Obecné otevření chráněné cesty bez pevného seznamu systémových souborů.
  - **Funkce:** F096
  - **Hotovo, když:** chráněnou cestu jde otevřít příkazem bez pevného seznamu systémových souborů a při chybějících právech editor řekne proč.
  - **Závisí na:** P1.4.4
  - **Odhad:** bez odhadu
- [ ] **PZ.4.3** Sady ikon
  - **Co a proč:** Vlastní vektorové ikony; import bitmapového skinu jen volitelně.
  - **Funkce:** F137
  - **Hotovo, když:** výměna sady ikon nezmění rozvržení D5.
  - **Závisí na:** P1.1.3
  - **Odhad:** bez odhadu
- [ ] **PZ.4.4** Most pro WSH skripty
  - **Co a proč:** Volitelný oddělený WSH most jen pro Windows; nedeklarovat kompatibilitu JS a VBS přes Jint.
  - **Funkce:** F097
  - **Hotovo, když:** skript WSH běží v odděleném procesu a editor ho nespustí bez potvrzení.
  - **Závisí na:** P3b.1.2
  - **Odhad:** bez odhadu

### PZ.5 Formátovače a typy souborů

- [ ] **PZ.5.1** PHP formátovač
  - **Co a proč:** Volitelný aktuální PHP formátovač, ne emulace PhpCB.
  - **Funkce:** F102
  - **Hotovo, když:** formátovač PHP se napojí přes akce při uložení F176 a katalog F213.
  - **Závisí na:** P8.3.1
  - **Odhad:** bez odhadu
- [ ] **PZ.5.2** Převodníky typů souborů
  - **Co a proč:** Externí převodník s jasným vstupem a výstupem; kontrakt otevření a uložení přes převod zatím není navržený.
  - **Funkce:** F138
  - **Hotovo, když:** kontrakt import/export s velikostními mezemi a transakčním uložením je navržený a ověřený jedním pluginem.
  - **Závisí na:** P8b.2.2
  - **Odhad:** bez odhadu
- [ ] **PZ.5.3** Import gramatik TextMate
  - **Co a proč:** `.tmLanguage` a `.tmbundle` jako zdroj pravidel pro jazyky bez vestavěné gramatiky; tokenizace dál vlastním enginem.
  - **Funkce:** F192
  - **Hotovo, když:** importovaná gramatika se převede na vlastní formát a psaní zůstane pod 33 ms p95.
  - **Závisí na:** P3.2.1
  - **Odhad:** F192 3–5 ČT (matice)
- [ ] **PZ.5.4** Prohlížeč .NET sestavení
  - **Co a proč:** `.dll` jen pro čtení jako dekompilovaný C# přes `ICSharpCode.Decompiler` (MIT).
  - **Funkce:** F193
  - **Hotovo, když:** otevření `.dll` ukáže dekompilovaný C# v kartě jen pro čtení.
  - **Závisí na:** P3b.2.2
  - **Odhad:** F193 1–2 ČT (matice)

### PZ.6 Vývojářské doplňky

- [ ] **PZ.6.1** Integrovaný terminál
  - **Co a proč:** Terminál s životním cyklem a korektním předáním vstupu.
  - **Funkce:** F159
  - **Hotovo, když:** terminál v panelu spustí shell, přežije změnu velikosti a zavření ukončí proces.
  - **Závisí na:** P8.2.1
  - **Odhad:** F159 3–5 ČT (matice)
- [ ] **PZ.6.2** Poslat výběr do interpretu
  - **Co a proč:** Řádek nebo výběr do `python`, `php -a`, `node`, `psql` v okně výstupu; `.venv` ve složce se použije.
  - **Funkce:** F191
  - **Hotovo, když:** výběr se vykoná v interpretu a výstup je v panelu.
  - **Závisí na:** PZ.6.1
  - **Odhad:** F191 2–3 ČT (matice)
- [ ] **PZ.6.3** HTTP požadavky z textu
  - **Co a proč:** Soubory `.http` a `.rest`, proměnné, prostředí a soukromý soubor s hesly, odpověď v nové kartě, převod z a na cURL; bez skriptů odpovědí.
  - **Funkce:** F194
  - **Hotovo, když:** požadavek ze souboru `.http` vrátí odpověď do nové karty a heslo ze soukromého souboru se neuloží do sdíleného.
  - **Závisí na:** P5.4.4
  - **Odhad:** F194 3–5 ČT (matice)
- [ ] **PZ.6.4** Volající a dopad z codegraph
  - **Co a proč:** Panel „Kdo to volá“ přes `codegraph callers --json`, jen čtení přes CLI.
  - **Funkce:** F258
  - **Hotovo, když:** panel ukáže volající symbolu pod kurzorem a u neuložené karty lištu „graf odpovídá disku“.
  - **Závisí na:** P3b.2.2
  - **Odhad:** F258 1–2 ČT (matice)
- [ ] **PZ.6.5** Strukturální hledání přes ast-grep
  - **Co a proč:** Vzor a přepis přes `ast-grep run --json=stream` do náhledu F152, neuložené karty přes `--stdin`.
  - **Funkce:** F259
  - **Hotovo, když:** shody přijdou do náhledu jako návrh s revizí a konflikt revize se odmítne.
  - **Závisí na:** P2.5.2
  - **Odhad:** F259 1,5–2,5 ČT (matice)
- [ ] **PZ.6.6** Co agent v této složce načte
  - **Co a proč:** Panel souborů agenta (`AGENTS.md`, `CLAUDE.md`, pravidla, skills) s velikostí a odhadem tokenů; zdroj „Plány agentů“ v Goto Anything.
  - **Funkce:** F260
  - **Hotovo, když:** pro aktivní soubor panel ukáže řetěz uplatněných souborů agenta.
  - **Závisí na:** P8b.3.2
  - **Odhad:** F260 1–2 ČT (matice)

### PZ.7 Pracovní postupy

- [ ] **PZ.7.1** Sada vzdálených změn
  - **Co a proč:** Změny ve více souborech na FTP/SFTP odeslané najednou s diffem a kontrolou novější verze.
  - **Funkce:** F167
  - **Hotovo, když:** vybrané změny se odešlou s náhledem a soubor změněný na serveru se neodešle bez diffu.
  - **Závisí na:** P5.4.2
  - **Odhad:** F167 4–6 ČT navíc (matice)
- [ ] **PZ.7.2** Obnova celé pracovní úlohy
  - **Co a proč:** Dokumenty, výběry, dotazy, filtry a recepty uložené jako úloha s návratem.
  - **Funkce:** F168
  - **Hotovo, když:** obnovená úloha vrátí karty, výběry, filtry a rozpracovaný návrh úpravy.
  - **Závisí na:** P9.4.1, P1.5.1
  - **Odhad:** F168 3–5 ČT navíc (matice)
- [ ] **PZ.7.3** Porovnání složek
  - **Co a proč:** Dvě složky nebo lokální složka proti FTP podle velikosti, času nebo obsahu, kopie jedním směrem s náhledem.
  - **Funkce:** F177
  - **Hotovo, když:** porovnání ukáže shodné, rozdílné a chybějící soubory a kopie proběhne až po náhledu.
  - **Závisí na:** P5.2.1, P5.4.2
  - **Odhad:** F177 3–5 ČT (matice)

### PZ.8 Pluginy

- [ ] **PZ.8.1** Katalog pluginů
  - **Co a proč:** Index v Git repozitáři s review, SHA-256 a podpisy vydavatele, aktualizace bez rozšíření oprávnění bez nového souhlasu.
  - **Funkce:** F206
  - **Hotovo, když:** editor odmítne balíček se špatným hashem nebo podpisem.
  - **Závisí na:** P8b.2.2
  - **Odhad:** F206 3–5 ČT (matice)
- [ ] **PZ.8.2** SDK pro JS/TS a Python
  - **Co a proč:** Generované typy z JSON Schema API a tenká knihovna; runtime si plugin přinese.
  - **Funkce:** F207
  - **Hotovo, když:** ukázkový plugin v TypeScriptu a v Pythonu projde ověřovačem a kontraktovými testy.
  - **Závisí na:** P8b.2.1
  - **Odhad:** F207 2–4 ČT (matice)
- [ ] **PZ.8.3** Kód pluginu uvnitř editoru
  - **Co a proč:** WASM nebo Jint jen po měření a rozhodnutí o velikosti a entitlementech pro macOS.
  - **Funkce:** F209
  - **Hotovo, když:** existuje měření a rozhodnutí majitele.
  - **Závisí na:** verze 1.0
  - **Odhad:** F209 3–5 ČT (matice)
- [ ] **PZ.8.4** Místa pro pluginy v hexu
  - **Co a proč:** Dekodér inspektoru, zdroj šablon, exportér, hash; jen čtou bajty a zápis vrací jako návrh.
  - **Funkce:** F249
  - **Hotovo, když:** ukázkový plugin přidá typ do inspektoru dat bez přístupu k zápisu.
  - **Závisí na:** P8b.2.1, P3.5.6
  - **Odhad:** F249 0,5–1 ČT (matice)

### PZ.9 Hex

- [ ] **PZ.9.1** Šablony struktur: rozšíření
  - **Co a proč:** `process` (zlib, xor, rotate), `imports`, bitová pole a `bit-endian`, úplný výrazový jazyk, export stromu do JSON, zápis hodnot ze stromu.
  - **Funkce:** F246
  - **Hotovo, když:** šablona s `process: zlib` se otevře a strom jde exportovat do JSON.
  - **Závisí na:** P8.10.1
  - **Odhad:** F246 3–5 ČT (matice)

### PZ.10 Další body bez F-ID

- [ ] **PZ.10.1** Rozšíření, která plán zmiňuje bez vlastního ID
  - **Co a proč:** Linux jako samostatný rozsah; textové výsledky příkazů LSP (rozbalení makra, AST); převodník JSON → Go struct, C# třída, TypeScript interface (F095); značky změn v minimapě (F161); sandbox pluginů na úrovni OS; nabídka příkazu pluginu jako MCP nástroje.
  - **Funkce:** žádná (rozšíření existujících funkcí)
  - **Hotovo, když:** každý bod dostane F-ID nebo rozhodnutí majitele, že se nedělá.
  - **Závisí na:** verze 1.0
  - **Odhad:** LSP text 1 ČT, JSON → typ 1–2 ČT; jinak bez odhadu

## Souhrn

Počty celků a úkolů spočítal `tools/check_todo.py --stats` a kontrolor je porovnává s tabulkou. ČT jsou z plánu, bez rezervy 25 %. P2, P3 a P6 jsou zvednuté na součet odhadů položek podle revize; u P3 jde o dolní mez.

**Rozpočty jsou neúplné**. Součet zadaných rozpočtů etap sedí, cena celé práce doložená není: P1 a P9 mají většinu položek bez čísla, P9 nemá doložené důsledky R10, P3 nese položky za 24,5–41,5 ČT proti rozpočtu 18,5–30,5 ČT a podíly F155, F169, F248 a přesun F154 nejsou rozdělené. Po spiku 100 MiB a rozhodnutí majitele 26. 10. 2026 přešly další části F154 z P7 do MVP (P1.2.8, P3.1.5, P3.1.11–P3.1.13, P3.5.9) bez ceny i bez odečtu z P7, proto je neúplný i rozpočet P7. Nové rozpočty určí PR.8.1 a PR.8.2; do té doby se dál uvádějí součty níže.

| Etapa | Celky | Úkoly | ČT (kap. 6) |
|---|---|---|---|
| Průběžně | 8 | 23 | ocení PR.8.1 a PR.8.2 podle etap u položek |
| P0 | 2 | 4 (hotovo) | 5–7 |
| P1 | 8 | 43 | 13–20 (neúplné) |
| P2 | 7 | 25 | 22–37 |
| P3 | 7 | 37 | 18,5–30,5 (dolní mez, neúplné) |
| P3b | 5 | 17 | 18–28 |
| P4 | 11 | 30 | 19,5–31,5 |
| P5 | 6 | 25 | 24–37 (neúplné) |
| P6 | 6 | 21 | 21,5–38 |
| P7 | 1 | 7 | 16–24 (neúplné) |
| P8 | 10 | 28 | 38–64,5 |
| P8b | 3 | 10 | 12,5–20 |
| P9 | 6 | 16 | 10,5–16 (neúplné, bez R10) |
| P10 | 1 | 4 | 8–12 |
| Později | 10 | 32 | mimo 1.0 |
| **Celkem** | **91** | **322** | MVP (P0–P3) 58,5–94,5, s rezervou 74–119; 1.0 (P0–P10) 226,5–365,5, s rezervou 284–457 |

### Kritická cesta

Bez odhadů jednotlivých uzlů nejde spočítat časově nejdelší cestu. Tohle je logická kritická cesta podle revize, ne harmonogram:

1. Ocenění P1 (PR.8.1), seznam systémů a stroje R10 (P1.1.6) → produkční kostra, AOT a kompatibilitní prototyp R10 (P1.1, P1.1.8, P1.8.3) → bajtový kontrakt, adaptéry a společné přijetí (P1.2.1, P1.2.7, P1.2.8, P1.2.10), revize a snapshoty (P1.2) → bezpečné uložení a žurnál (P1.3, P1.4.4, P1.5). Brána P1 navíc čeká na doměření zvolené cesty pro editaci 100 MiB na prototypu s UI (P1.2.9: vykreslení, IME, přístupnost, obnova po pádu, referenční Windows VM i referenční Mac v přihlášené relaci); bez něj P1 neprojde.
2. Registr příkazů (P1.6) a tokenový základ (P2.7.1) → navigace, víc kurzorů, hledání a služba náhledu změn (P2.1, P2.2, P2.4, P2.5).
3. Vstupní brána P3 (PR.8.2) → stránkovaná plocha (P3.1) s úpravami (P3.1.11–P3.1.13), editace 100 MiB (P3.1.10), `ByteSearch`, hex úpravy, společné bajtové uložení (P3.5) a přístupnost s IME (P3.1.5, P3.5.8) → konečné jméno (P3.7.6), instalační, kompatibilitní a výkonová brána MVP (P3.7.1, PR.1.3, P3.7.5).
4. Distribuční pokus pluginu s funkčním postupem (P3b.5.4) → hostitel, oprávnění, dokumentové API a API časných nástrojů (P3b.1–P3b.4, P3b.3.5) → první skutečný plugin (P3b.5.3) → nástroje P4.
5. Diff a místní historie s připnutím (P5.2, P5.3) → revize změn F178 (P6.1) → renderer a politika náhledu (P6.2.7) a rozšíření API v P6 (P6.2) → MCP, `/ide`, ACP (P6.3–P6.6); dohoda o wiki (PR.8.4) před P6.
6. Jazykové API (P8.4) → LSP, Git, schémata (P8.5–P8.7) → nezávislý autor (PR.8.3), veřejné SDK a kontraktové testy (P8b.2) → P9 → brána 1.0 (P10.1.4).

Do brány 1.0 musí současně dorazit P7 a všechny průběžné kontroly (PR). Distribuce, podpisy, přístupnost a měření nejsou až poslední úkol této cesty. Souběh popisují úvody etap P1, P2, P3, P6 a P8; jeden vývojář jím součet ČT nezkrátí, kalendář se zkrátí jen při více lidech nebo při čekání na stroje, podpisy a cizí službu (P6.4.4).

**Otevřená rizika:**
- Editace textu 100 MiB v MVP má od 26. 10. 2026 technickou cestu jen pro dokumentovou vrstvu: stránkované jádro na referenční Windows VM bez vykreslení splnilo první editaci i paměť současně. Vykreslení v Avalonii, vstup s UI, IME, přístupnost, obnova po pádu a běh na referenční Mac jsou **neověřené**. Doměří je P1.2.9 před bránou P1, plochu s úpravami postaví P3.1.11–P3.1.13 a v produktu ji přijme P3.1.10; eskalace majiteli bod neuzavírá.
- Kompatibilita R10 celého balíku je **neověřená**. Prototyp P1.1.8 a matice PR.1.3 ji musí doložit. Windows 10 22H2 pokryje testovací PC s Windows 10, bez něj žádná brána vydání neprojde. macOS 14 a nativní Intel zůstanou podle rozhodnutí majitele ověřené jen staticky, takže skutečný běh na nich je trvalé riziko.


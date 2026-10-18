# Technische achtergrond

Analyse van **RedCat Spookkasteel v1.0.0** (Davilex, 2000, ISBN 9055826200) op Windows 11.
Disc-image: `RedCat.bin`, één datatrack MODE2/2352, SHA-1 `5fe44a24722141d6a264ce3a613643e0583fa1a4`.

## Disc-controle

`Disc/DiscVerifier.cs` hasht de image in één keer, in blokken van 512 sectoren, met twee SHA-1's:

| Hash | Over | v1.0.0 |
|---|---|---|
| Image | Het databestand zoals het is (`.bin` of `.iso`) | `5fe44a24722141d6a264ce3a613643e0583fa1a4` |
| Data | De 2048 bytes gebruikersdata van elke sector binnen het ISO 9660-volume | `04cd30e4ff3bad58a6201ecee06cad3862a814a1` |

De datahash is hetzelfde voor een `.bin` (MODE2/2352, MODE1/2352, 2336) en een `.iso` (2048) van dezelfde cd. Hij loopt
tot de volumegrootte uit de primary volume descriptor (offset 80: 98.895 sectoren), dus de 302 lege sectoren na het volume
in `RedCat.bin` (postgap, track 99.197 sectoren) tellen niet mee. Een `.iso` met of zonder die sectoren wordt dus ook herkend.

Herkent geen van beide hashes de image, dan vergelijkt de patcher de SHA-256 van de bestanden die hij aanpast en nodig heeft
(`RcHcGame.dat`, `RcHcMenu.dat` en `GlideDrv.dll` uit `GameVersion.CdFiles`, plus `RedCat.exe`) rechtstreeks in de
ISO-boom. Kloppen die, dan is het een andere rip en gaat de patcher door. Ontbreekt er een, wijkt er een af, of is de image
korter dan het volume, dan volgt een waarschuwing met de details; interactief vraagt de patcher of hij toch moet doorgaan.
Een fout in een ander bestand (bijv. een level) bij een onbekende rip valt zo niet op: daarvoor is de bekende hash nodig.

## Opbouw van het spel

Alles staat in de map `Data` op de cd. Een installatie is niet nodig: het spel draait direct vanuit een kopie van die map.

| Bestand | Rol |
|---|---|
| `RedCat.exe` | "Motor": zwart venster, start het menu en daarna het spel en wacht op beide |
| `RcHcMenu.dat` | Menu (Delphi 5-programma, GDI) |
| `RcHcGame.dat` | Spel (Genesis3D-engine van WildTangent, `-game`) |
| `GlideDrv.dll`, `D3DDrv.dll`, `SoftDrv.dll` | Genesis3D-renderdrivers (Glide, Direct3D, software) |

Het spel controleert de cd niet en heeft geen registersleutels nodig. Het menu leest alleen `Software\Borland\Locales`.
De originele installer loopt op Windows 11 vast, omdat hij `Verdanaz.ttf` probeert te overschrijven.

## Fix 1 — crash bij het laden van een level (`RcHcGame.dat`)

Symptoom: "Er is een onverwachte fout opgetreden in RedCat" tijdens het laadscherm, met elke driver en elk lettertype.
De log `AdamDebug*.log` stopt na `AddBitmap( <font>'s Characters )`.

Oorzaak: in `geFont_AddCharacters()` (Genesis3D `Font/font.c`) wordt per teken
`GetGlyphOutline(hdc, ch, GGO_BITMAP, &gm, 32768, buffer, &mat)` aangeroepen, alleen om de afmetingen op te vragen.
Voor een glyph zonder pixels (de spatie, het eerste teken van elke reeks) geven moderne Windows-versies dan
`GDI_ERROR` terug en laten ze `GLYPHMETRICS` ongemoeid. Windows 9x gaf hier 0 terug.
Met de ongeïnitialiseerde afmetingen plaatst de engine de letter op een negatieve positie en schrijft het buiten de
font-bitmap, over het font-object heen. Later volgt een access violation (`mov eax,[esi+33678h]` met `esi = FF000000`).

Fix: de pushes `50` / `68 00 80 00 00` (buffer, 32768) worden `6A 00` / `6A 00` + 2× `NOP`.
De aanroep vraagt nu alleen de metrics op. `PlaceLetter()` haalt de bitmap daarna zelf op, met een correcte eerste aanroep.

## Fix 2 — menu gebruikt 32-bit kleur (`RcHcMenu.dat`)

Het menu probeert met `ChangeDisplaySettings` achtereenvolgens 800×600 in 24, 16, 15 en 32 bit.
24-bit bestaat niet meer, dus het wordt 16-bit. Windows emuleert die modus met de compat-shim
`DWM8And16BitMitigation`, die de Program Compatibility Assistant zelf in het register zet.
Fix: de eerste poging gebruikt 32 bit (`mov cx,18h` → `mov cx,20h`).
Sinds Fix 4 wisselt het menu helemaal niet meer van modus; deze patch blijft staan voor wie Fix 4 terugdraait.

## Fix 3 — dialogen buiten beeld (`RcHcMenu.dat`)

Het naamvenster, de afsluitvraag en andere dialogen erven van `TfrmCoolDialog` met `Position = poDesktopCenter`.
Delphi 5 centreert dan op de breedte en hoogte van de *virtuele* desktop, maar telt de oorsprong ervan niet mee.
Met een tweede monitor (bijvoorbeeld boven of links van het hoofdscherm) komt de dialoog buiten het 800×600-beeld te staan.
Het menu lijkt dan vast te lopen, omdat het modale venster onzichtbaar is.

Fix in de binaire DFM-resource: `poDesktopCenter` wordt `poScreenCenter`. Dat is één byte korter. Om de resource even lang te
houden, krijgt de `Caption` van het basisformulier één spatie extra; die Caption wordt door elke subklasse overschreven.

## Fix 4 — menu wisselt niet meer van resolutie (`RcHcMenu.dat`)

Symptoom: bij het starten zet het menu het hoofdscherm op 800×600. Andere monitoren schuiven dan mee (Windows legt
de desktop opnieuw aan), vensters springen naar een ander scherm en alt-tab naar het bureaublad toont alles op
800×600. Omdat het menu `ChangeDisplaySettings(&dm, 0)` gebruikt (zonder `CDS_FULLSCREEN`), zet Windows de resolutie
niet terug als het menu crasht of wordt afgeschoten; alleen een nette afsluiting roept `ChangeDisplaySettings(NULL, 0)` aan.

Analyse. Het opstarten van het hoofdformulier (`0x56DF88`) doet:

```
call  0x56D494      ; leest settings\menu.ini [GENERAL] Windowed (TIniFile.ReadBool, standaard False)
test  al, al
jne   0x56DF96      ; Windowed=1: modus-wissel overslaan
call  0x4B06C0      ; 800x600 in 24/16/15/32 bit proberen (zie Fix 2)
```

De ontwikkelaars hadden dus al een verborgen venstermodus. Het menu tekent zich daarin precies zo, maar gecentreerd: het
hoofdformulier (`TfrmMain`, `bsNone`) is schermvullend en zet de 800×600-inhoud in het midden; `RedCat.exe` (het
zwarte `MOTOR`-venster) vult de rest. Er is geen tweede plek die de resolutie wijzigt: `ChangeDisplaySettingsA` wordt
alleen aangeroepen vanuit `0x4B0560` (via `0x4B06C0`) en de terugzet-functie `0x4B05F4`. Het menu gebruikt geen
DirectDraw (geen `ddraw`-import, alleen GDI), dus dgVoodoo kan het niet opschalen.

Fix: `jne` → `jmp` (`75 05` → `EB 05`), zodat de wissel altijd wordt overgeslagen, ook zonder `Windowed=1` in
`menu.ini`. De terugzet-aanroep bij afsluiten blijft staan: zonder eerdere wissel laadt die de resolutie uit het
register, die al actief is. De 32-bit-patch van Fix 2 wordt hierdoor niet meer uitgevoerd, maar is onschadelijk.

Gevolgen:

- Het menu blijft 800×600 pixels. Op een 4K-scherm (100% schaal) is dat een klein vlak in het midden; Windows
  schaalt het programma niet, omdat het als DPI-onbewust de systeem-DPI krijgt. Bij 150% of 200% schaal rekt Windows
  het menu wel op (wazig, maar groter).
- De dialogen van Fix 3 (`poScreenCenter`) centreren op het hoofdscherm en vallen dus midden in het menu.
- Het spel (`RcHcGame.dat`) start pas nadat het menu is afgesloten en gaat via dgVoodoo (fake fullscreen) zelf
  schermvullend. Daar verandert niets aan; met de oude modus-wissel stond de resolutie op dat moment ook al terug.

Getest (Windows 11, 3840×2160 hoofdscherm met een 2560×1440-scherm erboven): het bureaublad blijft op 3840×2160 en
het tweede scherm op zijn plek, het menu staat gecentreerd, het naamvenster in het midden ervan, en het eerste level
draait schermvullend in 4:3. Met de originele `jne` wisselt het hoofdscherm naar 800×600 (ter vergelijking).

## Fix 5 — spel komt niet naar voren (`RedCat.exe`)

Symptoom: na Start! in het menu draait het spel wel, maar het staat achter het bureaublad of een ander venster en je
moet er met alt-tab naartoe. Vooral bij de eerste start na het patchen.

Analyse. `RedCat.exe` (MSVC, `D:\work\RCHorror\Motor\main.cpp`) maakt een schermvullend zwart venster (klasse
`MOTOR`, `WS_POPUP`) en start dan met `_spawnl(P_WAIT, ...)` `rchcmenu.dat /FirstTime`. Na afloop leest het
`settings\Menu.ini [GENERAL] EXITCODE`: 40 = `rchcgame.dat -game` starten en daarna het menu opnieuw met `/Default`,
42 = menu opnieuw, iets anders = afsluiten. `P_WAIT` betekent `CreateProcess` + `WaitForSingleObject(INFINITE)` in
`_spawn` (`0x4055DB`): zolang menu of spel draait verwerkt `RedCat.exe` geen berichten. Na 5 s markeert Windows het
MOTOR-venster als "reageert niet" en vervangt dwm het door een spookvenster (klasse `Ghost`).

Het spel zelf (`0x5A78A9`) doet `SetWindowPos`, `ShowWindow(SW_SHOWNORMAL)`, `UpdateWindow`, `SetFocus`, en roept
geen `SetForegroundWindow` aan: het vertrouwt op de regel dat het eerste venster van een nieuw proces de voorgrond
krijgt als dat mag. Gemeten (Windows 11, het echte menu bediend met gesimuleerde klikken, het voorgrondvenster elke
100 ms gelogd):

- Het menu sluit af via zijn onzichtbare `TApplication`-venster; daarna is het bureaublad (`Progman`, explorer) het
  voorgrondvenster, niet MOTOR. `RedCat.exe` heeft dus geen voorgrondrecht als het het spel start.
- Zonder invoer krijgt het spel toch de voorgrond (ook na 10 s wachten, ook met een open venster ernaast).
- Valt er tussen het sluiten van het menu en het verschijnen van het spelvenster een toetsaanslag of een klik, dan
  blijft het spelvenster achter. Een klik op het zwarte scherm (bijv. de tweede klik van een dubbelklik op Start!)
  activeert het hangende MOTOR-venster/spookvenster. Muisbewegingen kunnen geen kwaad.
- Dat tijdvenster is kort als alles in de cache staat (~0,1 s), maar bij de eerste start na het patchen ~0,6 s
  (Defender scant de nieuwe bestanden) en op een tragere schijf langer. Vandaar "vooral de eerste keer".
- `SetForegroundWindow` vanuit het spel, `SwitchToThisWindow` en topmost-trucs helpen dan niet. `AttachThreadInput`
  aan de voorgrondthread en de Alt-toets-truc helpen wel als het bureaublad de voorgrond heeft, maar niet als het
  hangende MOTOR-venster/spookvenster die heeft.
- `--start-level` start het spel rechtstreeks vanuit de patcher (geen MOTOR); in de test kwam het spel naar voren.

Fix: de `call [WaitForSingleObject]` in `_spawn` wordt `call 0x405920` (+ `nop`): een eigen wachtlus in de vrije ruimte
achter de laatste functie van `.text` (bron: `Patching/GameForeground.asm`, bytes in `GameForeground.cs`; de
`VirtualSize` van `.text` gaat mee omhoog). De lus:

1. wacht met `WaitForSingleObject(proces, 50)` en geeft bij afloop hetzelfde resultaat terug als het origineel;
2. verwerkt tussendoor de berichten van MOTOR (`PeekMessage`/`DispatchMessage`), zodat het venster niet meer hangt en
   een klik erop `RedCat.exe` zelf het voorgrondrecht geeft. Een `WM_QUIT` wordt opnieuw gepost voor de hoofdlus;
3. zoekt het spelvenster (`FindWindowA("RedCat", NULL)`) en roept, zolang het zichtbaar is maar niet op de voorgrond
   staat, `SetForegroundWindow` aan, gekoppeld aan de invoer van de voorgrondthread (`AttachThreadInput`). Na 10
   mislukte pogingen (0,5 s) drukt het één keer Alt in en los (dat geeft het proces het recht); na 40 pogingen (2 s)
   of zodra het spel de voorgrond heeft, stopt het, zodat het je niet terugtrekt als je zelf wegklikt.

De user32-functies die `RedCat.exe` niet importeert haalt de lus op met `GetModuleHandleA`/`GetProcAddress`. Ontbreekt
er een, dan verwerkt de lus alleen berichten. `RedCat.exe` is in alle versies hetzelfde bestand (de updates vervangen
het niet), dus er is één patroon.

Getest met een nepspel (klasse `RedCat`, eerst 2 s wachten, dan hetzelfde `ShowWindow`/`SetFocus` als het spel) en het
echte menu, ongepatcht → gepatcht: toets op het bureaublad niet → wel op de voorgrond, klik op het zwarte scherm niet →
wel, muis bewegen en geen invoer wel → wel. Met het echte spel en dgVoodoo (warm en vers gepatcht) komt het spel naar
voren, en de hele ronde spel → menu → Stoppen → Ja sluit `RedCat.exe` netjes af. Niet na te bootsen met het echte
spel: invoer binnen het tijdvenster, omdat het spelvenster hier binnen ~0,6 s verschijnt. De XP-build is niet getest;
heeft die een andere vensterklasse, dan doet stap 3 niets en blijft alleen het hangen weg.
## Fix 6 — icoon in de taakbalk (`RcHcGame.dat`, `RedCat.exe`)

Spel en `RedCat.exe` hebben elk een eigen icoon (groepsicoon 107 in `RcHcGame.dat`, 101 in `RedCat.exe`), maar hun
vensters gebruiken het niet. In de taakbalk staan dan twee lege standaardiconen: het zwarte venster van `RedCat.exe`
(klasse `MOTOR`, `WS_EX_APPWINDOW`) en het spelvenster (klasse `RedCat`). Het menu (Delphi, `MAINICON`) had het al goed.

- `RcHcGame.dat` registreert zijn klasse met `hIcon = LoadIconA(NULL, IDI_APPLICATION)`, het standaardicoon van Windows.
  Fix (`game-icon`): `push 7F00h; push ebx` wordt `push 107; push edi; nop×3`. `edi` is daar `hInstance`, dezelfde
  waarde die in `WNDCLASS.hInstance` gaat. De XP-build heeft dezelfde code op andere adressen (eigen patroon); 1.0.1 is
  gelijk aan 1.0.0.
- `RedCat.exe` vult `hIcon` en `hIconSm` van `WNDCLASSEX` met `LoadIconA(hInstance, 7F00h)`. Dat id bestaat niet in het
  bestand, dus beide blijven `NULL`. Fix (`redcat-icon`): twee keer `push 7F00h` wordt `push 101; nop×3`.

Getest op Windows 11 met 1.0.0: `GetClassLongPtr(GCL_HICON)` geeft voor beide vensters het RedCat-icoon. De launcher
leest het icoon bij het openen uit `RedCat.exe` in de spelmap (`LoadImageW` + `WM_SETICON`), zodat het niet in deze
repository staat.

## Fix 7 — helpknop: DAO 3.6 in plaats van DAO 3.5 (`RcHcMenu.dat`)

Symptoom: de knop Help in het menu geeft "Er is een onverwachte fout opgetreden in RedCat Menu" met
`DAO.CreateEngineFactory failed: <GetDAOEngine De DAO-database-engine kon niet geïnitialiseerd worden.>`
(ook in `rchcmenu.log`).

Oorzaak: Help opent de hulp-agent, die zijn tekstballonnen leest uit `Agent\RedCat.adb`, een Access-database (Jet 3.0,
tabellen `AgentBln` en `AgentBlnItem`). Het menu maakt de engine aan met de Davilex-unit `OpDAO35`
(`0x4BC5D0`): `CoGetClassObject` op de CLSID van DAO 3.5 `DBEngine` `{00000010-…}` of `PrivDBEngine` `{00000011-…}`,
dan `IClassFactory2::CreateInstanceLic` met de runtime-licentiesleutel van DAO 3.5 voor `_DBEngine` `{00000021-…}`,
en houdt `MSJET35.dll` geladen met `LoadLibrary`. DAO 3.5 en Jet 3.5 zitten niet in Windows 10/11 (de installer van het
spel zette ze neer). Zelfs als de DLL's er staan, zijn ze meestal niet geregistreerd.

Windows heeft wel DAO 3.6 (`dao360.dll`, 32-bit, geregistreerd) met Jet 4.0 (`msjet40.dll`). Die gebruiken dezelfde
interfaces met dezelfde IID's; MFC kiest net zo tussen beide met alleen een andere CLSID en licentiesleutel. Jet 4.0
opent Jet 3-databases zonder ze om te zetten.

Fix (drie vervangingen, even lang):

| Wat | DAO 3.5 | DAO 3.6 |
|---|---|---|
| CLSID `DBEngine` / `PrivDBEngine` (DATA, `0x5719CC`) | `{00000010-0000-0010-8000-00AA006D2EA4}` / `…11…` | `{00000100-…}` / `{00000101-…}` |
| Licentiesleutel (Delphi-string) | `mbmabptebkjcdlgtjmskjwtsdhjbmkmwtrak` | `mjgcqcejfchcijecpdhckcdjqigdejfccjri` |
| `LoadLibrary` | `MSJET35.dll` | `MSJET40.dll` |

Gecontroleerd: DAO 3.6 (`DAO.PrivateDBEngine.36`, 32-bit) opent `RedCat.adb` en leest beide tabellen. Getest: Help
toont de ballon van de agent met de vragen voor het huidige scherm.

## Fix 8 — helpteksten van de agent zonder WinHelp (`RcHcMenu.dat`, `Agent\RedCat.ini`)

Symptoom: een klik op een vraag in de ballon van de agent opent Edge op een Microsoft-pagina; er komt geen helptekst.

Oorzaak: elke vraag (`AgentBlnItem`, `BlnItemKind = 1`) heeft een contextnaam als `ActionText` (bijv. `h_Adventure`).
Het menu trimt die (`0x409974`) en roept `0x54BEC0` aan met het helpbestand (`ExtractFilePath(ExeName)` +
`Agent\RedCat.HLP`) en de naam. Die functie zet `Application.HelpFile` tijdelijk op dat bestand en doet
`HelpCommand(HELP_CONTEXTPOPUP, id)`; `WinHelpA` start `winhlp32.exe`, en dat bestaat sinds Windows 10 alleen nog als
stub die naar de website verwijst.

`REDCAT.HLP` is een klein WinHelp 4-bestand (29 kB) zonder compressie: `|SYSTEM`-flags 0, geen `|Phrases`.
`Patching/AgentHelp.cs` leest het bij het patchen uit:

- De interne bestanden staan in een B+-boom (directory, `|CONTEXT`); `|CONTEXT` koppelt de hash van een contextnaam
  aan een TOPICOFFSET. De hash: `h = h * 43 + waarde(teken)`, met A–Z (hoofdletterongevoelig) als 0x11–0x2A en `_` als
  0x0D. De namen komen uit `Agent\REDCAT.ghc` (een RoboHELP-bestand dat op de cd is meegekomen); alle 38 kloppen.
- `|TOPIC` bestaat uit blokken van 4 kB met een kop van 12 bytes; records (TOPICLINK) kunnen over een blokgrens lopen.
  Een onderwerp begint met een record van type 2 (titel), gevolgd door tekstrecords (0x20): de strings in LinkData2,
  met in LinkData1 per string een opmaakcommando (0x80 lettertype, 0x81 regeleinde, 0x82 einde alinea, …).
- TOPICOFFSET = blok × 0x8000 + het aantal teksttekens (som van DataLen2) sinds het eerste record dat in dat blok
  begint.

Het resultaat staat in `Agent\RedCat.ini` (Windows-1252), per contextnaam een sectie met `Title` en `Text`, en `|` als
regeleinde. De herhaalde titel bovenaan de tekst valt weg.

De menupatch vervangt de functie `0x54BEC0` (204 bytes, met dezelfde aanroep: `eax` = pad, `edx` = naam) door:

```
GetPrivateProfileStringA(naam, "Title", naam, titel, 256, pad)
GetPrivateProfileStringA(naam, "Text", "", tekst, 2048, pad)   ; leeg: niets tonen
| → LF in tekst
MessageBoxA(GetActiveWindow(), tekst, titel, MB_ICONINFORMATION)
```

Alle API's staan al in de importtabel. De strings `Title`, `Text` en `""` staan achter de code in dezelfde 204 bytes.
De absolute adressen zijn veilig: het menu heeft geen `DYNAMICBASE` en laadt altijd op `0x400000`. Het pad
`Agent\RedCat.HLP` wordt `Agent\RedCat.ini` (even lang).

Getest (Windows 11): Help → "Wat is Avontuur?" toont een berichtvenster met de titel en de drie alinea's, gecentreerd
op het menu; na OK werkt het menu gewoon verder.

## dgVoodoo2

De Genesis3D-drivers gebruiken Glide 2 of DirectDraw/Direct3D 6. dgVoodoo2 vertaalt beide naar Direct3D 11/12.
Op Linux gaat dat via DXVK. Het spel kiest automatisch de Glide-driver (`Glide2x.dll` uit de spelmap), dus onder Proton
zijn er geen DLL-overrides nodig. Instellingen die de patcher zet in `dgVoodoo.conf`:

- `VRAM = 128`: de originele D3D-driver ziet meer dan 2 GB videogeheugen als negatief getal.
- `ScalingMode = stretched_ar`, `FullscreenAttributes = fake`: randloos schermvullend beeld in 4:3 (aan te passen met `--display` en `--crt`).
- `[Glide] Antialiasing = 4x` (MSAA, `--msaa`); vsync en `FPSLimit` blijven op de standaard van dgVoodoo (zie "Beeldopties").
- Watermerken uit.
- `CenterAppWindow = true`: in venstermodus (`FullScreenMode = false` of alt-enter) zet het spel zijn venster op y=-73,
  zodat de titelbalk en de bovenste 40 pixels op een monitor boven het hoofdscherm vallen (of buiten beeld). dgVoodoo
  centreert het venster nu op het hoofdscherm. Fake fullscreen (de standaard) merkt hier niets van.
- `[Glide] Resolution = max` (aan te passen met `--resolution`, zie hieronder).

De patcher zoekt elke sleutel binnen zijn eigen sectie (`IniFile`): `Resolution` staat zowel in `[Glide]` als in `[DirectX]`.

### Renderresolutie (`--resolution`)

Het spel kiest zelf een Glide-modus van hooguit 640x480 (856x480 of 640x400 met `--widescreen`). Zonder
geforceerde resolutie (`unforced`, de standaard van dgVoodoo) rendert dgVoodoo op die resolutie en rekt het resultaat
daarna op tot schermgrootte; op een groot scherm is dat wazig. Met `[Glide] Resolution` rendert dgVoodoo de 3D-scène
direct op een hogere resolutie; het spel merkt daar niets van (het ziet nog steeds 640x480).

- Standaard `max`: de grootste resolutie met de beeldverhouding van het spel die op het bureaublad past, bijvoorbeeld
  1440x1080 (4:3) of 1920x1076 (16:9) op een 1080p-scherm, 3840x2153 (16:9) op 4K. Het opschalen naar het scherm valt
  dan (bijna) weg. dgVoodoo leest de bureaubladresolutie zelf bij het starten, dus de patcher hoeft die niet te kennen
  en een ander scherm werkt zonder opnieuw patchen.
- Andere waarden worden ongewijzigd doorgegeven: `2x`/`3x` (veelvoud van de spelresolutie), `max_isf` (grootste
  gehele veelvoud dat past), `desktop`, een vaste resolutie zoals `1920x1080`, of `unforced` voor het originele beeld.
- Alleen `[Glide]`: het spel zelf tekent via Glide. `[DirectX] Resolution` blijft `unforced`, want de menu's
  (`RcHcMenu.dat`, GDI) zijn 2D-plaatjes die daar niet scherper van worden.
- Getest (4K-scherm, 16:9): beeld en HUD kloppen op `2x` (1712x960) en `max` (3840x2153), ook het pauzemenu in het spel.
- In een venster (`FullScreenMode = false`) bepaalt de geforceerde resolutie de venstergrootte; met `max` wordt het
  venster dus zo groot als het scherm. Gebruik daar `2x`.

Prestaties en Proton: de scènes zijn klein (een spel uit 2000), dus ook 4K kost een moderne GPU weinig. Onder Proton
gaat dgVoodoo via DXVK (Direct3D 11 → Vulkan); dat verandert niets aan de instelling. Op de Steam Deck (en andere
gamescope-sessies) is het "bureaublad" de gamescope-resolutie, 1280x800 op het eigen scherm: `max` wordt daar
1280x717 (16:9) of 1066x800 (4:3), ongeveer 2x de originele resolutie en licht voor de APU. Hapert het toch (zwakke
iGPU, groot extern scherm), patch dan opnieuw met `--resolution 2x` of `--resolution unforced`, of pas
`Resolution` onder `[Glide]` in `dgVoodoo.conf` (of via `dgVoodooCpl.exe`) aan.

### Beeldopties (`--display`, `--fps-limit`, `--no-vsync`, `--msaa`, `--crt`)

Allemaal instellingen in `dgVoodoo.conf` (`DisplayOptions` in `Patching/DgVoodoo.cs`); het spel zelf verandert niet.

| Optie | dgVoodoo.conf | Standaard |
|---|---|---|
| `--display borderless` | `FullScreenMode = true`, `FullscreenAttributes = fake` | ja |
| `--display fullscreen` | `FullScreenMode = true`, `FullscreenAttributes` leeg (exclusief) | |
| `--display window` | `FullScreenMode = false`; `[Glide] Resolution = 2x` tenzij `--resolution` is opgegeven | |
| `--fps-limit <n>` | `[GeneralExt] FPSLimit` | `0` (geen limiet) |
| `--no-vsync` | `[Glide] ForceVerticalSync = false` | vsync aan |
| `--msaa <off\|2x\|4x\|8x\|16x>` | `[Glide] Antialiasing` | `4x` |
| `--crt` | `ScalingMode = stretched_ar_crt` | uit |

**Venster.** Met `max` zou het venster zo groot worden als het scherm (zie hierboven), dus `--display window` kiest
`2x`: 1280x960 (4:3) of 1712x960 (16:9), past op een 1080p-scherm. `CenterAppWindow` zet het venster midden op het
hoofdscherm. Exclusief volledig scherm (`fullscreen`) wisselt van beeldmodus; dat is trager bij alt-tab en werkt onder
Proton/gamescope niet beter, daarom blijft `borderless` de standaard.

**Framerate en timing.** Getest op een 240 Hz-scherm (4K) met `DebugPlayerPosition=1` in `AdamWin.ini`: die logt
per spelframe de positie van de speler. Een testscript start `RcHcGame.dat -game`, houdt 1 s de pijl omhoog vast,
springt en loopt nog 1 s, met vier instellingen:

| dgVoodoo | Spelframes per seconde | Afstand in 1 s | Sprong (hoogte) |
|---|---|---|---|
| `FPSLimit = 30` | 30 | 219 | 41,6 |
| `FPSLimit = 60` | 60 | 223 | 41,6 |
| vsync uit, geen limiet | 60 | 220 | 41,6 |
| vsync aan (240 Hz) | 61 | 222 | 41,6 |

Twee conclusies. De engine begrenst zichzelf op ongeveer 60 spelframes per seconde, ook zonder vsync of limiet; een
144/240 Hz-scherm of een ontbrekende limiet laat het spel dus niet sneller lopen. En beweging en sprongen rekenen met de
verstreken tijd (`CAdamClock`, `QueryPerformanceCounter`/`timeGetTime`), niet per frame: ook op 30 fps zijn afstand en
sprong gelijk (de kleine verschillen komen van het handmatig timen van de toets). Er is dus geen limiet nodig. De patcher
laat dgVoodoo's standaard staan (vsync aan in `[Glide]`, `FPSLimit = 0`); `--fps-limit` is er voor wie stroom wil
sparen (Steam Deck: `--fps-limit 30`) of vsync uitzet.

**Beeldkwaliteit.** dgVoodoo kent voor Glide alleen `Antialiasing` (MSAA) en `TMUFiltering` (appdriven, pointsampled,
bilinear). Anisotrope filtering bestaat alleen voor DirectX (`[DirectX] Filtering`); het spel tekent via Glide en de
menu's zijn 2D-plaatjes, dus daar is geen optie voor. MSAA staat standaard op `4x`: dat ondersteunt elke Direct3D 11-GPU
(ook de Steam Deck en DXVK), het haalt de kartelranden van de levelgeometrie weg en kost bij deze kleine scènes vrijwel
niets. `8x`/`16x` werken alleen als de GPU ze ondersteunt. Het CRT-effect (`stretched_ar_crt`) is een schaalmodus van
dgVoodoo zelf: scanlines, afgeronde hoeken en een licht gebold beeld, toegepast bij het schalen naar het scherm. Getest:
schermvullend werkt het met `max` (fijne scanlines, één per gerenderde lijn) en met `unforced` (grove scanlines op
480 lijnen, het meest als een echte monitor uit 2000; combineer daarvoor `--crt --resolution unforced`). In een
venster (`--display window`) schaalt dgVoodoo niet, dus daar is het effect niet te zien.

## Optie `--widescreen` (16:9 / 16:10)

Het spel heeft geen breedbeeldinstelling. Vier dingen zitten in de weg; de patcher lost ze alle vier op.

**1. De Glide-driver kent geen breedbeeldmodus.** `GlideDrv.dll` (Genesis3D) biedt vast 512×384, 640×480, 800×600 en
1024×768 aan, en Glide 2 zelf kent alleen vaste resoluties (`GR_RESOLUTION_*`). Daar zitten twee bruikbare tussen:
856×480 (`0x0A`, 16:9) en 640×400 (`0x06`, 16:10). De patcher vervangt 512×384 door een van die twee, op drie plaatsen:
de modusnaam, breedte/hoogte in de moduslijst en de `switch` in `GLIDE_DrvInit` die `grSstWinOpen` aanroept.

**2. Het spel kiest zelf de modus.** Met `settings\Computer.ini [Display] AutoSelectDriver=1` zoekt
`CAdamEngine::SelectDevice()` in de eerste geschikte driver naar `"640x480"` (met `"320x200"` als ondergrens).
`[Engine] Mode=` in `AdamWin.ini` wordt dan niet gebruikt; met `AutoSelectDriver=0` toont de engine een kiesvenster
voor driver en modus. De patcher vervangt daarom de zoekterm `"640x480"` in `RcHcGame.dat` door de breedbeeldmodus.

**3. Het FOV staat vast.** Genesis3D berekent de projectie uit de breedte (`Scale = Width / Fov`), dus een breder
beeld met hetzelfde FOV knipt boven en onder af (Hor−). `[Camera-LevelCamera]` en `[Camera-PlayerCamera] FieldOfView`
in `AdamWin.ini` worden wel gelezen, maar `CAdamCamera` zet een afwijkende waarde direct terug naar 1,36
(log: *"INI setting FieldOfView is ignored, always 1.360000"*) en de camera neemt die bij de volgende
`geCamera_SetAttributes` over. De patcher maakt van die `jne` een `jmp` en zet in beide secties
`FieldOfView = 1,36 × (B/H) / (4/3)`: 1,819 voor 16:9 en 1,632 voor 16:10. De verticale kijkhoek blijft gelijk
aan het origineel (Hor+).

**4. Het HUD wordt uitgerekt.** `RCHud` tekent elk icoon met `geEngine_DrawAlphaBitmap` op een schermpercentage:
`x / 640` en `y / 480`, met de pixelposities uit `settings\HUDIcon.ini`. Op 856 pixels breed worden de iconen dan een
derde breder. De patcher laat de twee `fmul [1/640]` in `RCHud` naar een ongebruikte kopie van de constante 1/480
(`0x64EB38`) wijzen en zet daar `1 / (480 × B/H)` neer, dus 1/856 of 1/768. De iconen houden zo hun vorm. Iconen in de
rechterhelft (`X >= 320`: score, toverdrank) schuiven in `HUDIcon.ini` 216 (16:9) of 128 (16:10) op, zodat ze rechts
uitgelijnd blijven. Tussenfilmpjes en de tekstballonnen (`AdamCommunicator`, `RcCutScene`) gebruiken eigen kopieën van
1/640 en worden nog steeds uitgerekt; die zijn gecentreerd en vallen minder op.

dgVoodoo hoeft niets anders: `ScalingMode = stretched_ar` behoudt al de beeldverhouding van het spel, dus 16:9 vult een
16:9-scherm en 16:10 krijgt op een 16:9-scherm smalle zwarte balken. Het menu (`RcHcMenu.dat`) blijft 800×600 (4:3),
en onder Instellingen → Beeldkwaliteit staat nog "640x480".

## Optie `--wasd`

Het spel leest toetsen met `GetAsyncKeyState` (`UseDirectInput=0`). Welke toets bij welke actie hoort, staat in
`settings\Player<n>.ini [KeyBinds]` (toetsnamen, bijvoorbeeld `MoveForward=W`). Dat bestand komt niet van de disc:
het menu maakt het vóór elk spel opnieuw aan uit `PlayerDef.ini` plus de instellingen van de speler
(`DataPlayer.pas`, `WriteToInis`). Daarbij schrijft het menu zeven toetsen: vooruit (`MoveForward`), achteruit
(`MoveBack`), links/rechts (`TurnLeft`/`TurnRight`), schieten (`Fire`), springen (`Jump`) en ontwijken (`Walk`), plus
`MoveLeft`/`MoveRight` als ontwijken + links/rechts (standaard `Alt+Left`).

De standaardwaarden voor nieuwe spelers en voor de knop Standaard staan, als virtual-key-codes, in
`settings\static.ini [DEFAULTSETTINGS]` (`Up`, `Down`, `Left`, `Right`, `Extra1`–`Extra3`). Het menu leest die ook
opnieuw als de speler geen eigen toetsen heeft ingesteld. De patcher zet daar W (87), S (83), A (65) en D (68) neer, en
zet in `PlayerDef.ini` dezelfde toetsen voor `MoveForward`, `MoveBack`, `TurnLeft`, `TurnRight` en `Alt+A`/`Alt+D`
voor zijwaarts. Schieten (Ctrl), springen (Spatie) en ontwijken (Alt) blijven hetzelfde. Bestaande spelers met eigen toetsen
houden die; het menu bewaart ze per speler.

## Optie `--controller`

### Onderzoek: de engine kan zelf al joysticks lezen

`RcHcGame.dat` importeert `joyGetDevCapsA` en `joyGetPosEx` uit `WINMM.dll` en bevat `DaviJoystick.cpp` met
debugregels als `*-JS Active Joystick found : %d`. `UseDirectInput` in `AdamWin.ini` heeft daar niets mee te maken: dat
schakelt alleen toetsenbord en muis over op DirectInput (`DaviDInput.cpp`). Hoe het werkt (adressen voor 1.0.0):

- **Opstarten** (`0x5A5A00`): `joyGetPosEx` op joystick 0, anders 1, met `JOY_RETURNX | JOY_RETURNY | JOY_RETURNBUTTONS`
  (`0x83`). De stand op dat moment is het midden; de dode zone is 10% daarvan. Een controller die later wordt
  aangesloten, ziet het spel niet.
- **Elk frame** (`0x5A6030` → `0x5A5DE0`): vier booleans voor de richtingen van de linkerstick en tien voor knop 1–10.
  Geen POV (d-pad), geen Z (triggers), geen R/U (rechterstick).
- **Tabel** (`0x5A5F30`): 14 aanroepen `push vk; push index; mov ecx,esi; call` koppelen elke richting/knop aan een
  virtual-key-code: stick = pijltjestoetsen, knop 1–4 = Ctrl, Alt, Spatie, Enter, knop 5–10 = niets.
- **Toetscontrole** (`0x543DC0`, gebruikt door alle acties uit `[KeyBinds]`): vóór `GetAsyncKeyState` kijkt de engine of
  een joystickrichting of -knop met díe virtual-key-code is ingedrukt (`0x5A6080`).

`ControlType` (Toetsenbord/Gamepad/Joystick) en `JoySensitivity` in het menu (`static.ini`, `Player<n>.ini [General]`)
leest het spel niet; de joystick doet altijd mee. Een controller werkt dus zonder patch, maar alleen met de oude
standaardtoetsen, met een onhandige indeling (A schiet, X springt, geen wapenwissel of menu) en niet meer met `--wasd`.

### Gekozen route: de tabel van de engine aanpassen

Van de drie routes is (a), de eigen joystickcode, het robuustst:

- Geen extra DLL of proces. Windows levert een Xbox-controller (XInput) ook via winmm aan, met de DirectInput-nummering
  (1 A, 2 B, 3 X, 4 Y, 5 LB, 6 RB, 7 Back, 8 Start, 9/10 sticks indrukken). Wine/Proton doen hetzelfde, en op de Steam
  Deck biedt Steam Input de knoppen aan als zo'n virtuele Xbox-controller.
- (b) Een XInput→toetsenbordlaag (proxy-DLL of hulpproces) kan meer (d-pad, triggers, rechterstick, het menu), maar is
  een apart stuk native code dat moet meelopen met dgVoodoo2's DLL's en onder Proton apart getest moet worden. Dat past
  beter bij de launcher (`docs/LAUNCHER.md`); zie "Beperkingen en nog open".
- (c) Een Steam Input-indeling werkt alleen via Steam en is per gebruiker en per snelkoppeling opgeslagen; meeleveren kan
  niet zonder in Steams configuratie te schrijven. De standaardindeling van Steam voor niet-Steam-games (Gamepad,
  rechtertrackpad als muis) volstaat bovendien met deze patch.

De patcher herschrijft alleen de vk-bytes van die 14 `push`-instructies (`Patching/Controller.cs`). Hij zoekt de code aan
de hand van de vaste structuur (14 keer `6A vk 6A index 8B CE E8 …`, met de indexen in deze volgorde), omdat de
call-offset per build verschilt; de code is in 1.0.0, 1.0.1 en de XP-build verder identiek. `push imm8` is signed, dus
alleen virtual-key-codes onder `0x80` kunnen (`[`/`]` zijn `0xDB`/`0xDD`). Daarom krijgen wapenwisselen F5/F6, die de
patcher als extra toets naast `[`/`]` in `PlayerDef.ini [KeyBinds]` zet (meerdere toetsen per actie mag, net als
`Jump=Space,RightMouse`).

| Controller | Toets | Actie |
|---|---|---|
| Linkerstick | pijltjes (W/A/S/D met `--wasd`) | vooruit, achteruit, links/rechts draaien |
| A | Spatie | springen |
| B | Alt | ontwijken; met de stick zijwaarts |
| X | Ctrl | schieten |
| Y | Enter | |
| LB / RB | F5 / F6 | vorig / volgend wapen |
| Back | Tab | scores |
| Start | Escape | menu |
| Linkerstick indrukken | Delete | camera centreren |

Met alleen `--wasd` past de patcher de stick ook aan (W/A/S/D) en laat hij de knoppen zoals ze waren; anders bestuurt
de stick niets meer.

### Beperkingen en nog open

- **Menu** (`RcHcMenu.dat`, Delphi): leest geen joystick en bestaat uit plaatjes om op te klikken. De launcher vult
  dat aan (`RedCatLauncher/MenuPad.cs`, instelling "Controller in menus", standaard aan): na Play blijft hij verborgen
  draaien tot `RedCat.exe` stopt, en zolang een venster van het menuproces op de voorgrond staat (procesnaam
  `RcHcMenu.dat`), beweegt de linkerstick of d-pad de muis (`SetCursorPos`, kwadratische curve), klikt A
  (`SendInput`), is B Escape en Start Enter. In het spel doet hij niets. Ook met `--spelen`. Geen DLL of hook in het
  spel; onder Proton werken XInput en `SendInput` binnen het prefix. Op de Steam Deck blijft ook Steams eigen
  trackpad-muis werken.
- Stick is aan/uit (het zijn toetsen), d-pad, triggers en rechterstick doen niets. Steam Input kan de d-pad op de
  linkerstick leggen.
- Sluit de controller aan vóór het starten en raak de stick niet aan tijdens het laden (midden wordt dan bepaald).
- **Joystick-ID (fix, `Patching/JoystickSearch.cs`).** Het origineel probeert alleen ID 0 en 1. Windows nummert
  controllers in winmm niet altijd vanaf 0: met virtuele gamepads (Steam, Oculus) of eerder aangesloten controllers
  stond een Xbox-controller op een testsysteem op ID 2 (`joyGetPosEx` op 0 en 1: fout 165, `JOYERR_UNPLUGGED`), en
  zag het spel hem nooit. De fix vervangt de twee pogingen (1.0.0: `0x5A5A30`–`0x5A5A9D`, 109 bytes) door een lus over
  ID 0–15 met `ebp` als teller; de eerste ID die antwoordt komt in `[esi]`, waar de code per frame (`0x5A6030`) hem
  leest. Gevonden aan de structuur, met het IAT-adres van `joyGetPosEx` als jokertekens, zoals de joysticktabel.
  Een nieuwe fix verhoogt `Basis.Revision`; de launcher bouwt de bestanden dan bij de volgende keer Play opnieuw op.
- De knoppen volgen de standaardtoetsen. Wie in het menu vooruit/schieten/springen/ontwijken op een andere toets zet,
  verliest die functie op de controller.
- Niet getest met een echte controller (geen beschikbaar bij het bouwen); de werking volgt uit de disassembly hierboven.

## Logbestanden (standaard uit, `--debug-logs`)

Bij elke start schrijven spel, menu en Glide-driver logbestanden in de spelmap. Geen INI-instelling zet dat uit: de
sleutels in `AdamWin.ini [Debug]` (`DebugDriver`, `DebugLoading`, ...) voegen alleen extra meldingen toe, en de logger
wordt in `WinMain` altijd geregistreerd. De patcher zet de schrijvers daarom zelf uit (`Patching/DebugLogs.cs`), tenzij
`--debug-logs` is opgegeven. Bij opnieuw patchen verwijdert hij de oude logbestanden uit de doelmap.

| Bestand | Wie | Wanneer | Patch |
|---|---|---|---|
| `AdamDebug%02d.log` | `RcHcGame.dat`, callback van de engine-log (`DaviTrace`) | Elke start; `[Debug] DebugFileNumber` loopt rond van 1 tot 5 | Schrijver begint met `ret` |
| `AdamError%02d.log` | `RcHcGame.dat` | Elke start de regel `Start run ...`, daarna alleen bij fouten | Alleen de `Start run`-aanroep wordt 5× `NOP`; fouten worden nog gelogd |
| `Sound.log` | `RcHcGame.dat`, geluidssysteem | Elke start | Schrijver begint met `ret` |
| `Levels\<level>.log` | `RcHcGame.dat`, scriptcompiler | Per geladen level, tot ~80 kB | Na het sluiten van het vorige log springt de functie naar zijn uitgang; het `ofstream`-pointer blijft 0 en alle schrijvers testen daarop |
| `GCache.Log` | `GlideDrv.dll` | Bij het afsluiten (inhoud van de texture- en lightmapcache) | De aanroep wordt `mov eax,1` ("gelukt") |
| `MenuTrac.log` | `RcHcMenu.dat`, tracer | Elke start | Tracer wordt aangemaakt met `Enabled = False` (`mov byte [eax+0Ch],1` → `0`); alle schrijfmethoden testen die vlag |
| `rchcmenu.log` | `RcHcMenu.dat`, exception-logger (Delphi, `ChangeFileExt(ParamStr(0), '.log')`) | Elke start en elk afsluiten een regel, verder bij exceptions; ingekort tot 10 kB | Alleen de regels "Exception logging started/ended" worden overgeslagen |
| `D3DDrv.Log`, `D3DCache.Log` | `D3DDrv.dll` | Alleen met de Direct3D-driver, dus zonder dgVoodoo2 | Niet gepatcht; wel opgeruimd |

`RedCat.exe` schrijft niets. Spelersgegevens, scores en `AdamWin.ini` blijven buiten het opruimen.

De Win98-update heeft dezelfde code als de cd-rom, maar zijn globale variabelen en strings staan op andere adressen. Daarom
heeft elke versie van `RcHcGame.dat` eigen patronen: 1.0.0, 1.0.1 en de XP-build (waar `esi` bij de sprong nog op de stack
staat, dus `pop esi; jmp`). `GlideDrv.dll` en `RcHcMenu.dat` verschillen niet tussen de versies.

Getest op 1.0.0: een level spelen en afsluiten, en het menu starten, laat geen enkel `.log`-bestand achter. De patronen
voor 1.0.1 en XP komen precies één keer voor in die builds en zijn gecontroleerd door de code te disassembleren.

## Officiële updates

Davilex bracht twee updates uit als Wise-installer
([Internet Archive](https://archive.org/details/redcatspookkasteelupdate)). De patcher herkent ze aan de SHA-256 en past ze
zelf toe, zonder de installer uit te voeren (`Updates/OfficialUpdate.cs`).

| Update | Exe (SHA-256) | Wat er verandert |
|---|---|---|
| Win98-update, "RedCat Spookkasteel v 1.0.1.0, 19 juni 2001" | `Redcat Spookkasteel 98 patch.exe` (`6B6F671A…398A06`) | `RcHcGame.dat` (nieuwe build van 27-11-2000, zelfde code-adressen), 14 ini's in `settings` (snelheid van vleermuizen en spinnen, health van de eindbazen, vijanden per level, kaartnamen in `Game.ini`, `LanguageNL.ini` ingekort, volumes in `Volume.ini`), 28 geluiden in `Sounds`, `leesmij.hlp`/`.cnt` en `Versie.txt` |
| XP-update ("Rc Spook XP Patch") | `Redcat Spookkasteel XP patch.exe` (`A953AAFA…74F8DE`) | Alleen `RcHcGame.dat`: opnieuw gecompileerd op 20-12-2004 (`RedCatHCD.exe`, FileVersion 1.0.0.1), 4,4 MB, alles op andere adressen |

`RcHcMenu.dat` en de drivers veranderen niet. De patcher kent daarom drie versies van `RcHcGame.dat`
(`Patching/GameVersion.cs`) en twee builds: 1.0.0 en 1.0.1 delen de patronen, de XP-build heeft eigen patronen
(`Xp2004` in `GamePatches.cs` en `Widescreen.cs`). Een onbekende hash stopt de patcher met een melding.

### Wise-installer

Na de PE-stub volgen deflate-streams, elk gevolgd door de CRC-32 van de uitgepakte data. Het Wise-script (zelf een stream)
beschrijft elke installatie-opdracht als begin, eind, datum, grootte, 20 bytes, CRC-32 en doelpad (`%MAINDIR%\…`);
begin en eind zijn relatief aan een vaste basis in de exe. Voor de twee bekende exe's staan de positie van het script en die
basis vast in `OfficialUpdate`.

### Delta-formaat

Bestanden die al op de cd staan, levert de Win98-update als delta (decoder in `WISE0232.DLL`, nagebouwd in
`Updates/WiseDelta.cs`). Header (little-endian): `01 91 2A`, 2 bytes, CRC-32 + grootte van het nieuwe bestand,
headerlengte (25; 0 = het hele nieuwe bestand volgt), CRC-32 + grootte van het oude bestand, lengte van de commando's.
Daarna de commando's en de literals. Getallen lopen door met bit 7 als vervolgbit, maximaal twee bytes van 7 bits en een
laatste van 8 bits.

| Commando | Betekenis |
|---|---|
| `0LLLCCCC` | `L` literals, dan `C+3` bytes uit het oude bestand |
| `1ELLLLLL` + `EKCCCCCC` [+ verschuiving] | `L` literals, dan `C+3` bytes kopiëren; `E` = getal loopt door. Zonder `K` volgt eerst een verschuiving van de leeskop (met teken, bit 6) |

Literals schuiven de leeskop in het oude bestand even ver op, zodat vervangingen van gelijke lengte geen verschuiving nodig
hebben.

### Adressen in de XP-build

| Fix | 1.0.0 / 1.0.1 | XP-build |
|---|---|---|
| `GetGlyphOutlineA` in de importtabel | `0x64A018` | `0x8A34B8` |
| Lijst van modi (`"640x480"`) | strings achter elkaar | strings opgevuld tot 12 bytes |
| FieldOfView-check | `test ah,40h` / `jne` | `test ah,44h` / `jnp` (zelfde sprongdoel, `jmp` +0x9D) |
| 1/640 (HUD) | `0x64D398` | `0x76FC0C` |
| Ongebruikte kopie van 1/480 | `0x64EB38` | `0x771A40` (direct na 255.0f; geen verwijzingen) |

## Snelkoppelingen en Steam

Code in `Install/` en `Steam/`; los herbruikbaar (bijvoorbeeld voor een Steam Input-lay-out per game).

Alles wijst naar `RedCatLauncher.exe` als die in de spelmap staat (de patcher zet hem daar), anders naar `RedCat.exe`.
Het pictogram blijft `RedCat.exe`. Een installatie van v0.1.0/v0.2.0 heeft snelkoppelingen naar `RedCat.exe`; die
worden bij opnieuw patchen met `--shortcut`/`--steam` omgezet in plaats van dat er een tweede bij komt.

### Windows-snelkoppeling (`Install/ShellLink.cs`)

De patcher schrijft het `.lnk`-bestand zelf (MS-SHLLINK), want `IShellLink` via COM werkt niet in een getrimde
single-file build. Het bestand bestaat uit de header, de IDList van `RedCat.exe`, een LinkInfo-blok (lokaal pad, ANSI en
Unicode) en de strings voor omschrijving, werkmap en pictogram (`RedCat.exe,0`). De IDList is nodig: zonder IDList
negeert Verkenner het doel, ook als LinkInfo klopt. Die IDList komt van `SHParseDisplayName` (gewone P/Invoke naar
`shell32`), dus precies wat de shell zelf zou opslaan.

Omzetten: `ShellLink.ReadTarget` leest het doel uit het LinkInfo-blok van een bestaande `.lnk` (`LocalBasePathUnicode`
als die er is, anders het ANSI-pad, plus het achtervoegsel). De patcher schrijft `RedCat Spookkasteel.lnk` op het
bureaublad en in `Programs` opnieuw en zoekt daar (met submappen) naar andere `.lnk`'s met als doel `RedCat.exe` in
dezelfde spelmap, bijvoorbeeld een hernoemde kopie. Die krijgen dezelfde bestandsnaam met de launcher als doel.
Snelkoppelingen zonder LinkInfo (alleen een IDList) en naar een andere map blijven ongemoeid.

### Steam: `shortcuts.vdf`

Niet-Steam-games staan per account in `userdata/<account-id>/config/shortcuts.vdf`, een binair KeyValues-bestand
(`Steam/BinaryVdf.cs`): elk veld is een typebyte, een naam met nul-terminator en de waarde. `0x00` = map, `0x01` = string,
`0x02` = int32, `0x03` = float, `0x07` = uint64, `0x08` = einde van de map. Het bestand zelf is een naamloze map met daarin
`shortcuts` → `0`, `1`, … Lezen en schrijven behoudt volgorde en typen, dus andere games blijven byte voor byte gelijk.

Per game: `appid` (int32), `AppName`, `Exe` en `StartDir` (allebei tussen aanhalingstekens), `icon`, `LaunchOptions`,
`IsHidden`, `AllowOverlay`, `LastPlayTime`, `tags`, enz. De **appid** is wat Steam zelf berekent:

```
appid  = CRC32('"' + exe + '"' + AppName) | 0x80000000
gameid = (appid << 32) | 0x02000000      (voor steam://rungameid/)
```

Dezelfde appid gebruikt Steam voor de Proton-keuze, plaatjes (`config/grid/<appid>p.png`) en controllerconfiguraties.
Staat RedCat al in de lijst, dan werkt de patcher die regel bij (`SteamShortcuts.AddOrUpdate`). Herkenning, in deze volgorde:

1. zelfde appid of zelfde `Exe` (`RedCatLauncher.exe` in deze map);
2. anders een regel met `Exe` = `RedCat.exe` in dezelfde map: de regel van v0.1.0/v0.2.0.

Paden worden vergeleken zonder aanhalingstekens, hoofdletterongevoelig en met `/` gelijk aan `\`. Een regel naar
`RedCat.exe` in een andere map is een andere installatie en blijft staan. De bijgewerkte regel houdt zijn **appid**,
ook al hoort die bij de oude exe: Proton-keuze, plaatjes, controllerlay-out en speeltijd hangen eraan, en Steam leest
de appid uit het bestand in plaats van hem opnieuw te berekenen. Ook `LastPlayTime`, `tags`, `IsHidden` en
`LaunchOptions` blijven; alleen `AppName`, `Exe`, `StartDir` en `icon` worden gezet. Accounts komen uit de mappen in `userdata` (behalve `0`); de namen uit `config/loginusers.vdf`
(SteamID64 = account-id + 76561197960265728).

### Steam: Proton (`config/config.vdf`)

Op Linux zet de patcher de compatibiliteitstool in de tekst-VDF `config/config.vdf` van de Steam-installatie:

```
"InstallConfigStore" { "Software" { "Valve" { "Steam" { "CompatToolMapping" {
    "<appid>" { "name" "proton_experimental"  "config" ""  "priority" "250" } } } } } }
```

Staat er voor de appid al een tool (zelf gekozen in Steam, of eerder door de patcher), dan blijft die staan, tenzij
`--proton` is opgegeven.

`Steam/TextVdf.cs` bewaart strings ruw (met escapes), zodat de rest van het bestand ongewijzigd terugkomt. Steam leest beide
bestanden bij het opstarten en schrijft `config.vdf` terug bij het afsluiten; daarom moet Steam dicht zijn. Steam wordt
gezocht via het register (`HKCU\Software\Valve\Steam\SteamPath`) op Windows en in `~/.steam/steam`, `~/.local/share/Steam`
en Flatpak (`~/.var/app/com.valvesoftware.Steam/…`) op Linux.

De Linux-`.desktop` start het spel via `steam steam://rungameid/<gameid>` (Flatpak: `flatpak run com.valvesoftware.Steam …`),
dus met dezelfde Proton-instellingen als in Steam.

## Direct naar een level

Het menu geeft zijn keuze aan het spel door via `settings\Game.ini [Run]` (`PlayerNumber`, `StartLevelNumber`,
`StartSubLevelNumber`, `StartSubSubLevelNumber`, `Replay`) en start dan `RcHcGame.dat -game`. Het spel heeft één level
(`[level1]`) met vijf sublevels, de Griezelplekken van het menu:

| Nr | Griezelplek | Kaart |
|---|---|---|
| 1 | Het Bos | `lvl00a.bsp` |
| 2 | Het Kasteel | `lvl01a.bsp` |
| 3 | Het Kerkhof | `lvl02a.bsp` |
| 4 | De Grotten | `lvl03a.bsp` |
| 5 | De Toren | `lvl04a.bsp` |

`--start-level` (`Patching/StartLevel.cs`) schrijft `[Run]` met `StartSubLevelNumber` = het gekozen nummer en
speler 10, kopieert `PlayerDef.ini` naar `Player10.ini` en start het spel. Het menu schrijft `[Run]` en de
`Player%d.ini` vóór elk spel opnieuw, dus via `RedCat.exe` speel je daarna gewoon verder. De launcher doet hetzelfde
met "Start level" op het tabblad Testing. Voortgang per speler staat in
`Player%d.ini` (`[Level1] CurrentSubLevel`, `[SubLevel1_N] Completed`).

### Vaardigheden

Schieten is een vaardigheid, geen kwestie van toverflesjes. Bij het begin van elk sublevel
(`RcPlayerProperties.cpp`, `0x439D50` in 1.0.0) leest het spel `[Player]` uit `Player%d.ini` en zet daarna de
toverflesjes en de score op 0: de teller in de HUD (`0 / 50`) is wat je in dít level vindt, en `MagicPotion` of
`InitialPotions` aanpassen helpt dus niet. De vaardigheden (`+0x68`) komen uit `[Player] Skill`, een bitmasker:
1 schieten, 2 power shot, 4 superjump, 8 super shot (dezelfde volgorde als F10 met de debugtoetsen). Alleen bij
`[General] GameType=1` (Snelspel) vervangt het spel die door `StandardSkill` van het sublevel uit `Game.ini`
(`0x43A959`–`0x43A9DB`); bij `GameType=0` (Avontuur) blijft de waarde staan die je in de levels hebt verdiend
(scriptcommando `RcEnableSkill`).

| Nr | Griezelplek | `StandardSkill` |
|---|---|---|
| 1 | Het Bos | 0 |
| 2 | Het Kasteel | 1 (schieten) |
| 3 | Het Kerkhof | 3 |
| 4 | De Grotten | 7 |
| 5 | De Toren | 15 |

`PlayerDef.ini` heeft `Skill=0` en `GameType=0`, dus testspeler 10 kon vanaf level 2 niet schieten. `StartLevel` zet
daarom `[Player] Skill` van `Player10.ini` op de `StandardSkill` van de gekozen Griezelplek (uit `Game.ini`, met de
tabel hierboven als reserve), zoals Snelspel die geeft. Getest in De Grotten: met `Skill=0` schiet Ctrl niets, met
`Skill=7` wel. Gewone spelers en `PlayerDef.ini` blijven ongemoeid.

Bekende crash: het opblazen van de muur in level 4 (De Grotten). De XP-update lost die op volgens de
[uploader op het Internet Archive](https://archive.org/details/redcatspookkasteelupdate); niet getest of 1.0.0/1.0.1 onder
moderne Windows nog crasht.

Daarnaast kent `RcHcGame.dat` `-map <naam>` (kaart uit `Levels`, zonder `.bsp`) en `-file <pad>`; die laden een kaart als
"ad-hoc level" buiten de levelstructuur (`Game.ini [ad-hoc sublevel]`).

## Testen zonder menu

`RcHcGame.dat -game` start direct het eerste level als `settings\Player1.ini` bestaat (kopie van `PlayerDef.ini`).
Patch met `--debug-logs`, anders schrijft het spel geen logbestanden. Met `DebugDriver=1` in `AdamWin.ini [Debug]`
logt `AdamDebug*.log` dan ook de gekozen driver en modus. Met
`FullScreenMode = false` in `dgVoodoo.conf` draait het spel in een venster (zet dan ook `[Glide] Resolution` op
`2x` of `unforced`, anders wordt het venster schermgroot).

## Console, cheats en debugtoetsen (`AdamWin.ini [Debug]`)

Onderzocht in `RcHcGame.dat` 1.0.0 (adressen hieronder) en getest in het spel op Windows 11 met dgVoodoo2 in een venster.
Elke `[Debug]`-sleutel heeft een eigen getter die één keer een instellingsobject aanmaakt (`0x5AC5D0`, sectie `Debug`
van `.\ADAMWIN.INI`) en het in een globale variabele bewaart; de waarde staat als int op `+0x64` en wordt bij het eerste
gebruik met `atoi` uit de tekst op `+0x18` gehaald. Zoeken op de sleutelnaam geeft dus de getter, en de aanroepers van
de getter zijn de plekken waar de sleutel iets doet.

| Sleutel | Getter | Globaal | Aanroepers |
|---|---|---|---|
| `DebugSecretCommands` | `0x515570` | `0x6B98BC` | `0x4BC7F5`, `0x4C0B87`, `0x522DEC`, `0x532068` |
| `DebugKeys` | `0x5184B0` | `0x6B9918` | `0x4C07A8` |
| `DebugUseConsole` | `0x5196B0` | `0x6B9938` | `0x497F90`, `0x49855F`, `0x4BE353`, `0x4BE3C7`, `0x4BFBEA`, `0x52D81F`, `0x52DDEE` |

### De console (`CAdamConsole`, `AdamConsole.cpp`)

Een tekstvenster over de bovenste helft van het beeld, getekend met het lettertype van de engine, dus via Glide en
dgVoodoo zoals de HUD. Het toont de engine-log (dezelfde regels als `AdamDebug*.log`, ook als de patcher de
logbestanden heeft uitgezet: de console hangt aan de log, niet aan de schrijver van het bestand) en heeft een
invoerregel `>>`. Openen en sluiten met de actie `ToggleConsole` uit `[KeyBinds]` (standaard `` ` ``); zolang hij open
is, gaan toetsen naar de console (`0x5940C0`) en niet naar het spel.

De toets werkt alleen als vlag `+0x39C` van de applicatie aan staat (`0x4BEFCB`). Die wordt bij het starten gezet
(`0x4BC7F5`–`0x4BC909`) als **`DebugSecretCommands=1`**, of als **`Console=1`** in `[General]` van het
`Player%d.ini`-bestand van de huidige speler staat (`0x43AEB0` geeft `.\Settings\Player%d.ini`). Die tweede route
kent het menu niet; het menu schrijft met `TIniFile`/`WritePrivateProfileString`, dus een extra sleutel blijft staan.
De launcher gebruikt die route voor testspeler 10 (tabblad Testing, "Console and cheats").

Commando's (`HELP` geeft de lijst, hoofdletters maken niet uit):

| Commando | Wat |
|---|---|
| `HELP` | lijst met commando's |
| `SETTINGS [deel van naam]` | alle instellingen (INI-sleutels die de engine kent) met hun huidige waarde, bijv. `settings debug` |
| `<instelling> = <waarde>` | instelling tijdens het spel wijzigen |
| `EXEC DaviName.Command` | commando naar een spelobject sturen (zoals de scripts van de levels doen) |
| `RESTART` | sublevel opnieuw starten |
| `QUIT` | spel afsluiten |
| `CHEATS [on/off]` | (niet in `HELP`) cheats aan/uit: zet vlag `+0xAC` van het spel (`0x529430`), zodat het Esc-menu "Cheats..." krijgt |

### Het cheatmenu in het spel

Het Esc-menu (`CAdamGame::DisplayMenu`, `0x52E600`) krijgt de regel **Cheats...** als vlag `+0xAC` aan staat
(`0x53169C`). Die vlag is aan bij `DebugSecretCommands=1` (`0x522DEC`) of na `cheats on` in de console. Inhoud
(teksten uit het spel; bedienen met pijltjes en Enter):

| Regel | Wat | Getest |
|---|---|---|
| Volgende level | sublevel afronden: overzichtsscherm, daarna het volgende sublevel | ja: Het Bos → Het Kasteel |
| Ga naar level `[SubLevel1_n]` | naar een gekozen sublevel | nee |
| Door muren vliegen `[AAN/UIT]` | geen botsingen | ja: RedCat loopt de kasteelmuur in |
| Godmodus `[AAN/UIT]` | onkwetsbaar; gaat bij een nieuw sublevel weer uit | ja (aangezet met F9, zichtbaar in het menu) |
| Geef alles behalve sleutels / Geef alles | voorwerpen, met of zonder sleutels | nee |
| Effecten `[0–3]` | grafische details | nee |

Let op: "Volgende level" schrijft de voortgang van de speler (`[Level1] CurrentSubLevel=SubLevel1_2` in
`Player%d.ini`). Met speler 10 van `--start-level` is dat geen probleem; met een echte speler wel.

Met `DebugSecretCommands=1` krijgt "Weergave..." ook een keuze Effecten (`0x532068`, teksten `0x71`–`0x74`); niet
in het spel bekeken.

### Debugtoetsen (`AdamApplication.cpp`, alleen met `DebugSecretCommands=1`)

`0x4C0B50` registreert de toetsen alleen als `DebugSecretCommands` aan staat (anders sprong naar `0x4C268D`); `Console=1`
of `cheats on` geven ze niet (getest: F5 doet dan niets). Elke toets is `(vk, modus, tekst, functie, herhalen)` via
`0x4C4F00`; de toetsen worden elk frame gepold (`0x4BFFA0`, `0x5A9790`). Modus 0: altijd, ook met Shift/Ctrl;
1: zonder Shift en Ctrl; 2: met Shift; 3: met Ctrl (sprongtabel `0x4C0864`).

| Toets | Wat (tekst uit het spel) | Opmerking |
|---|---|---|
| F1 / Shift+F1 | lijst met commando's / statistieken | F1 gaf in de test niets zichtbaars |
| F2, Shift+F2 | frame-calling / AddPoly aan/uit (prestatietest) | |
| F3 | naar het volgende deathmatch-startpunt | teleport |
| F4 | screenshot `ScreenDump%d.bmp` ("Glide only") | |
| F5 | framerate en statistieken van Genesis | getest: werkt |
| F6 | botsing van de speler aan/uit | = door muren |
| F9 | godmodus aan/uit | getest: werkt |
| F10 | vaardigheden doorlopen (Skill Shot, Power Shot, Super Jump, Super Shot) | |
| F11 | "fast keys" aan/uit | |
| Ctrl+F2, Ctrl+F8, `9`, `0` | prestatietest, wireframe, gouraud, additive light | functie `0x4C8780` is `mov al,1; ret`: doet niets |
| Shift+`,` / Shift+`.` | camera links/rechts | |
| A | AI-info tekenen | modus 0 |
| B / Shift+B | bounding boxes / tekenmodus | |
| C / Shift+C | camera neerzetten / oppakken | vrije camera |
| E | test-explosie | |
| F, G, R, V, X, Z (Shift = omlaag) | FOV, gamma, renderrechthoek, volume, richtkruis, camera-ZScale | houden herhaalt |
| H | HUD aan/uit | getest: verbergt delen van de HUD |
| I | interlacing | |
| K | speler onzichtbaar | in de test geen zichtbaar effect |
| L | low-end-pc-instellingen | |
| P | paden tekenen | |
| S / Shift+S | spelobjecten sneller / langzamer | houden herhaalt |
| Shift+T | alle tekst aan/uit | |
| M | test-assertion (bewuste crash) | alleen met ook `DebugFakeAssertion=1` (`0x558710`) |

Botsingen met de patcher: met `--wasd` is A ook draaien/zijwaarts en S achteruit, dus lopen zet de AI-info aan en
versnelt het spel. Met `--controller` zijn LB/RB F5/F6, dus wapen wisselen zet de framerate-info en de botsing van de
speler aan en uit. P is ook `Pause`. Met de oude pijltjestoetsen en zonder `--controller` botst alleen P.

### `DebugKeys` en `DebugUseConsole`

- **`DebugKeys`** (`0x4C07A8`): logt elke `WM_CHAR` (`0x102`) als `unknown keypress %c %d (%X)`. Geen functie; ook
  toetsen die wel iets doen (zoals W) verschijnen, omdat de echte toetsen gepold worden. Alleen ruis in de log/console.
- **`DebugUseConsole`**: opent de console automatisch tijdens het opstarten en laden (`0x4BE353`, `0x497F90`,
  `0x52D81F`) en sluit hem weer als het spel `GS_GAME` ingaat (`0x4BE3C7`, `0x49855F`, `0x52DDEE`); vlag `+0x391`
  onthoudt dat de console automatisch open ging. Getest: de laadlog staat in beeld tot het level begint. Geeft geen
  invoer of commando's buiten de laadschermen.

### Overige `[Debug]`-sleutels

Op dezelfde manier gevonden (getter met aantal aanroepers): `DebugScript` (71), `DebugLoading` (57), `DebugSound` (35),
`DebugDriver` (31), `DebugPlanetoid` (20), `DebugWindowsMessage` (15), `DebugGameState` (14), `DebugDamage` (13),
`DebugWayPoint` (12), `DebugCriticalSection` (12), `DebugFont` (11), `DebugSkipFonts` (10), `DebugMouse` (9),
`DebugDInput` (8), `DebugSprite` (6), `DebugSkipHighASCIIValues` (6), `DebugAnyActor`, `DebugDisplayMessage`, `DebugModel`
(5), `DebugCommand`, `DebugParentChild`, `DebugBitmap` (4), `DebugCleanup` (3), `DebugLowEndPC`, `DebugDisableCoronas`,
`DebugFileNumber` (2) en met één aanroeper `DebugPlayerCollision`, `DebugPlayerPosition`, `DebugLight`, `DebugPrintInfo`,
`DebugStartup`, `DebugActorLighting`, `DebugProjectile`, `DebugFontNoAntialias`, `DebugProcessAllActorFiles`,
`DebugFakeAssertion`, `DebugFakeDriverCrash`, `DebugFakeSoundCrash`, `DebugFakeSoundFailure`. Naar hun naam en
aanroepplaats voegen de meeste alleen logregels toe; niet verder onderzocht. `DebugGrobbers`, `DebugHeavy`,
`DebugMemCheckPoint` en `DebugRGB` staan in `AdamWin.ini` maar worden door de code niet gelezen. Omgekeerd leest de code
`DebugMessage` (`0x523010`), die niet in de INI staat.

### Niet getest

- Proton en de Steam Deck. De console en het cheatmenu tekenen met de engine zelf via Glide, net als de HUD, dus er is
  geen reden waarom ze daar anders zouden werken; de `` ` ``-toets heeft op de Deck wel een toetsenbord nodig
  (`ToggleConsole` is een gewone `[KeyBinds]`-actie en kan naar een andere toets).
- 1.0.1 en de XP-build: de strings en de structuur zijn naar verwachting gelijk, de adressen niet.
- Of `<instelling> = <waarde>` in de console ook naar het INI-bestand schrijft.

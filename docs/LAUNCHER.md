# Ontwerp: launcher naast `RedCat.exe`

Ontwerpnotitie; de stappen 1–5 uit "Aanbevolen volgorde" zijn gebouwd (`src/RedCatLauncher`). Afspraak (17-10-2026): de
opties blijven ook in de patcher werken; de launcher hergebruikt de code uit `Patching/`. Deze notitie verdeelt de huidige opties over drie plekken en
beschrijft wat er nodig is om een keuze op een geïnstalleerde map telkens opnieuw toe te passen en terug te draaien.

| Groep | Wat | Waar |
|---|---|---|
| 1. Altijd in de patcher | Eenmalig, heeft de disc nodig, geen keuze | `RedCatPatcher` |
| 2. Kiezen bij het starten | Smaak van de speler, op elk moment te wisselen | Launcher, tabblad Spelen/Instellingen |
| 3. Testen | Voor wie problemen zoekt of een level wil testen | Launcher, tabblad Testen |

## Huidige situatie

De patcher doet alles in één keer, in deze volgorde (`Program.cs`): disc uitlezen en `Data` kopiëren, officiële updates,
versie bepalen, fixes, logbestanden uit, opties, dgVoodoo2, snelkoppelingen/Steam. Drie eigenschappen bepalen wat een
launcher moet oplossen:

- **Binaire patches gaan één kant op.** `BinaryPatch.Apply` zoekt `Find` en zet er `Replace` neer. Staat `Replace` er al
  precies één keer en `Find` niet meer, dan is het resultaat `AlreadyApplied`; opnieuw toepassen is dus veilig. Er is
  geen omgekeerde richting, en de originele bytes worden nergens bewaard.
- **Versieherkenning werkt alleen op ongepatchte bestanden.** `GameVersion.Detect` vergelijkt de SHA-256 van
  `RcHcGame.dat`, `RcHcMenu.dat` en `GlideDrv.dll` met de bekende originelen. Na het patchen klopt geen hash meer, dus
  een launcher kan de versie (en daarmee `ExeBuild`) niet opnieuw bepalen uit de geïnstalleerde map.
- **Opnieuw patchen begint altijd bij de disc.** Daardoor zijn sommige INI-aanpassingen nu niet herhaalbaar zonder dat
  het opvalt; zie `HUDIcon.ini` bij breedbeeld hieronder.

## Groep 1: altijd in de patcher

Eenmalig of afhankelijk van de disc; er valt niets te kiezen dat een speler later wil wisselen.

| Stap | Code | Wat er verandert | Waarom in de patcher |
|---|---|---|---|
| Disc uitlezen | `Disc/`, `Iso9660.ExtractDirectory` | De hele map `Data` naar de doelmap; spelers, scores en `settings\menu.ini` worden bewaard (`BackupUserData`) | Heeft de disc nodig |
| Disc-controle | `Disc/DiscVerifier.cs` | Niets (alleen hashes) | Heeft de disc nodig |
| Officiële updates | `Updates/OfficialUpdate.cs` | `RcHcGame.dat`, 14 ini's in `settings`, 28 geluiden, help (Win98); alleen `RcHcGame.dat` (XP) | Heeft de update-exe's nodig; bepaalt de versie |
| Versieherkenning | `Patching/GameVersion.cs` | Niets | Moet op de bestanden vóór de fixes |
| Crash-, menu- en vensterfixes | `Patching/GamePatches.cs` | `RcHcGame.dat` (`font-glyph-metrics`, `game-icon`), `RcHcMenu.dat` (`menu-32bit`, `dialog-center`, `menu-no-modeswitch`, `menu-dao36`, `menu-agent-help` + `Agent\RedCat.ini`), `RedCat.exe` (`game-foreground`, `redcat-icon`) | Zonder deze fixes werkt het spel niet; niemand wil ze uit |
| dgVoodoo2 installeren | `Patching/DgVoodoo.cs` (`Install`) | `DDraw.dll`, `D3DImm.dll`, `Glide*.dll`, `dgVoodooCpl.exe`, `dgVoodoo.conf` met de vaste sleutels (`VRAM`, watermerken, `CenterAppWindow`) | Download met hashcontrole; `--no-dgvoodoo` blijft een patcheroptie |
| Logbestanden uit (standaard) | `Patching/DebugLogs.cs` | Zie groep 2, `--debug-logs` | De patcher zet de standaard; de launcher mag hem omzetten |
| Snelkoppelingen, Steam | `Install/`, `Steam/` | `.lnk`/`.desktop`, `shortcuts.vdf`, `config.vdf` | Eenmalig, buiten de spelmap; Steam moet dicht zijn |

Nieuw voor de launcher (ook groep 1): de patcher bewaart een **basis** en schrijft een **keuzebestand**, zie
"Herhaalbaar toepassen" en "Waar de launcher zijn keuze bewaart". Snelkoppelingen en Steam wijzen dan naar de launcher
in plaats van naar `RedCat.exe`.

## Groep 2: kiezen bij het starten

Per optie: welke bestanden en bytes, en wat er nodig is om het telkens opnieuw toe te passen en terug te draaien.

### `--widescreen [16:9|16:10]`

| Bestand | Wat | Soort |
|---|---|---|
| `GlideDrv.dll` | `widescreen-glide-mode`: modus 512×384 wordt 856×480 of 640×400 (moduslijst, naam `"512x384"`, `GLIDE_DrvInit` met `GR_RESOLUTION_*`) | Binair, 3 vervangingen |
| `RcHcGame.dat` | `widescreen-select-mode`: zoekterm `"640x480"` wordt de breedbeeldmodus | Binair (XP-build eigen patroon) |
| `RcHcGame.dat` | `widescreen-fov`: `jne` → `jmp`, zodat `FieldOfView` uit de INI telt | Binair (XP eigen patroon) |
| `RcHcGame.dat` | `widescreen-hud`: twee `fmul [1/640]` naar de vrije kopie van 1/480, daar `1/856` of `1/768` | Binair (XP eigen patroon) |
| `AdamWin.ini` | `[Camera-LevelCamera]` en `[Camera-PlayerCamera] FieldOfView`: 1,819 of 1,632 (origineel `1.360000`) | INI |
| `settings\HUDIcon.ini` | `[IconN] X` van iconen met `X >= 320`: +216 of +128 | INI |

Herhaalbaar: de binaire patches zijn per beeldverhouding verschillend. Van 16:9 naar 16:10 kan dus niet direct: eerst
terug naar 4:3, dan de andere. Terugdraaien door `Find` en `Replace` om te wisselen kan alleen als het `Replace`-patroon
in het ongepatchte bestand niet voorkomt; dat is niet gecontroleerd (bijv. `"856x480\0"` of de float `1/856`).
De INI-kant is herhaalbaar en omkeerbaar: `Widescreen.ApplySettings(gameDir, target)` leest de huidige stand af aan
`FieldOfView` (1,36, 1,819 of 1,632) en verschuift de iconen rechts vanaf hun originele `X` naar die van `target`
(`null` = 4:3). Een `FieldOfView` die bij geen van de drie hoort, geeft een fout zonder iets te veranderen. Vóór deze
wijziging schoof een tweede keer toepassen de iconen opnieuw op.

### `--wasd`

| Bestand | Wat |
|---|---|
| `settings\static.ini [DEFAULTSETTINGS]` | `Up/Down/Left/Right` = 87/83/65/68 (origineel 38/40/37/39) |
| `settings\PlayerDef.ini [KeyBinds]` | `MoveForward=W`, `MoveBack=S`, `TurnLeft=A`, `TurnRight=D`, `MoveLeft=Alt+A`, `MoveRight=Alt+D` (origineel `Up`, `Down`, `Left`, `Right`, `Alt+Left`, `Alt+Right`) |
| `RcHcGame.dat` | Joysticktabel: stick naar W/A/S/D (via `Controller.Apply`, ook zonder `--controller`) |

Herhaalbaar: `IniFile.Set` zet een vaste waarde, dus heen en terug is triviaal als de originele waarden bekend zijn.
Let op: het menu bewaart per speler eigen toetsen. Omzetten raakt alleen spelers zonder eigen toetsen en nieuwe
spelers (en de knop Standaard); een bestaande speler met W/A/S/D houdt die na terugzetten. Dat moet de launcher
zeggen.

### `--controller`

| Bestand | Wat |
|---|---|
| `RcHcGame.dat` | De 14 vk-bytes van de `push`-instructies in `CDaviJoystick` (`Controller.PatchTable`) |
| `settings\PlayerDef.ini [KeyBinds]` | `F5` erbij in `WeaponPrev`, `F6` in `WeaponNext` |

Herhaalbaar: dit is al de goede vorm. `PatchTable` zoekt de tabel aan de structuur, niet aan de bytes, en schrijft elke
gewenste toewijzing; terug is `PatchTable(Controller.Original)`. Wel hangen `--wasd` en `--controller` samen (de stick
volgt WASD), dus de launcher rekent de tabel altijd uit beide keuzes (`Controller.Mapping(wasd, buttons)`). De extra
toetsen in `PlayerDef.ini` worden nu alleen toegevoegd; terugdraaien betekent `,F5`/`,F6` weer weghalen.

### dgVoodoo2-beeldopties (`--resolution`, `--display`, `--fps-limit`, `--no-vsync`, `--msaa`, `--crt`)

Alleen `dgVoodoo.conf` (`DisplayOptions.Settings`), het spel zelf verandert niet:

| Optie | Sleutel |
|---|---|
| `--display` | `[General] FullScreenMode`, `[GeneralExt] FullscreenAttributes` |
| `--crt` | `[General] ScalingMode` (`stretched_ar` / `stretched_ar_crt`) |
| `--fps-limit` | `[GeneralExt] FPSLimit` |
| `--no-vsync` | `[Glide] ForceVerticalSync` |
| `--msaa` | `[Glide] Antialiasing` |
| `--resolution` | `[Glide] Resolution` (standaard afhankelijk van `--display`) |

Herhaalbaar: volledig. Elke sleutel krijgt een vaste waarde via `IniFile.Update`; de launcher schrijft alleen deze
zeven sleutels en laat de rest staan, zodat aanpassingen via `dgVoodooCpl.exe` aan andere sleutels blijven. dgVoodoo
leest het bestand bij elke start van het spel. De launcher hoeft de zip niet opnieuw uit te pakken.

### `--debug-logs`

| Bestand | Wat (zonder `--debug-logs`) |
|---|---|
| `RcHcGame.dat` | `no-logs-game`: 4 vervangingen (`ret` in de schrijvers van `AdamDebug` en `Sound.log`, `Start run` → `NOP`, scriptlog overslaan); eigen patronen voor 1.0.0, 1.0.1 en XP |
| `GlideDrv.dll` | `no-logs-glide`: `GCache.Log` niet schrijven |
| `RcHcMenu.dat` | `no-logs-menu`: tracer uit, begin/einde-regels van `rchcmenu.log` overslaan |

De patcher kiest standaard "uit" (groep 1); de launcher maakt er een schakelaar van. Herhaalbaar: dezelfde situatie als
breedbeeld, binaire patches zonder weg terug. Het opruimen (`DebugLogs.Clean`) kan de launcher als losse knop aanbieden.
De meeste `[Debug]`-sleutels (groep 3) zetten alleen iets in deze logs; zet de launcher zo'n sleutel aan, dan ook de
logs. De console (zie "`AdamWin.ini [Debug]`") toont de log ook zonder logbestanden en heeft ze niet nodig.

## Groep 3: testen

### `--start-level <1-5>`

Schrijft `settings\Game.ini [Run]` (`PlayerNumber=10`, `StartLevelNumber=1`, `StartSubLevelNumber=<n>`,
`StartSubSubLevelNumber=1`, `Replay=0`), kopieert `PlayerDef.ini` naar `Player10.ini` en start `RcHcGame.dat -game`
(`Patching/StartLevel.cs`). Herhaalbaar en zonder terugdraaien: het menu schrijft `[Run]` en `Player%d.ini` vóór elk
spel opnieuw. Dit is al de vorm die een Testen-tab nodig heeft. Onder Linux start de patcher het spel nu niet zelf;
de launcher draait onder Proton in hetzelfde prefix en kan dat wel (zie "Proton").

### `AdamWin.ini [Debug]`

De cd-rom heeft 46 sleutels, allemaal `0`: `DebugActorLighting`, `DebugAnyActor`, `DebugBitmap`, `DebugCleanup`,
`DebugCommand`, `DebugCriticalSection`, `DebugDamage`, `DebugDInput`, `DebugDisableCoronas`, `DebugDisplayMessage`,
`DebugDriver`, `DebugFakeAssertion`, `DebugFakeDriverCrash`, `DebugFakeSoundCrash`, `DebugFakeSoundFailure`,
`DebugFileNumber`, `DebugFont`, `DebugFontNoAntialias`, `DebugGameState`, `DebugGrobbers`, `DebugHeavy`, `DebugKeys`,
`DebugLight`, `DebugLoading`, `DebugLowEndPC`, `DebugMemCheckPoint`, `DebugModel`, `DebugMouse`, `DebugParentChild`,
`DebugPlanetoid`, `DebugPlayerCollision`, `DebugPlayerPosition`, `DebugPrintInfo`, `DebugProcessAllActorFiles`,
`DebugProjectile`, `DebugRGB`, `DebugScript`, `DebugSecretCommands`, `DebugSkipFonts`, `DebugSkipHighASCIIValues`,
`DebugSound`, `DebugSprite`, `DebugStartup`, `DebugUseConsole`, `DebugWayPoint`, `DebugWindowsMessage`.

Bekend uit eerder onderzoek (`TECHNISCH.md`): `DebugDriver` (gekozen driver en modus in de log), `DebugLoading`,
`DebugPlayerPosition` (positie per frame), `DebugFileNumber` (nummer van `AdamDebug%02d.log`). `DebugFake*` laten
het spel bewust crashen; die horen niet als gewone schakelaar in de launcher.

Onderzocht in stap 8 (details en adressen in `TECHNISCH.md`, "Console, cheats en debugtoetsen"): de engine heeft al
een console, een cheatmenu in het Esc-menu en debugtoetsen.

| Wat | Aan met | Doet | Voor de Testen-tab |
|---|---|---|---|
| Console (`` ` ``) | `DebugSecretCommands=1`, of `Console=1` in `[General]` van `Player%d.ini` | log in beeld; `HELP`, `SETTINGS`, `<instelling> = <waarde>`, `RESTART`, `QUIT`, `cheats on` | ja, via `Player10.ini` |
| Esc-menu "Cheats..." | `DebugSecretCommands=1`, of `cheats on` in de console | volgende level, ga naar level, door muren, godmodus, geef alles, effecten | ja (zie console) |
| Debugtoetsen F1–F11 en letters | alleen `DebugSecretCommands=1` | o.a. F9 godmodus, F6 door muren, F5 framerate, F3 teleport, C vrije camera, S spelsnelheid | ja, met waarschuwing |
| Laadlog in beeld | `DebugUseConsole=1` | console open tijdens laden, dicht zodra het level begint | ja, klein |
| `DebugKeys` | `DebugKeys=1` | logt elke toetsaanslag als "unknown keypress" | nee, alleen ruis |

De debugtoetsen botsen met de patcher: met `--wasd` zijn A (AI-info) en S (spel sneller) ook lopen, met `--controller`
zijn LB/RB F5/F6 (framerate-info, botsing uit), en P is ook pauze. Getest op Windows met dgVoodoo2; console en
cheatmenu tekenen via de engine (Glide), dus onder Proton naar verwachting hetzelfde, maar niet getest.

Herhaalbaar: INI-waarden, `0` of `1`, dus triviaal. Een herinstallatie zet `AdamWin.ini` terug (het staat niet in
`BackupUserData`); dat is goed zo.

### Tabblad Testing (gedaan)

In de launcher (`src/RedCatLauncher`, Engelstalig zoals de andere tabbladen), met controller te bedienen zoals de rest:

| Regel | Wat |
|---|---|
| Start level | Off of 1 t/m 5. Play doet dan hetzelfde als `--start-level`: `StartLevel.Prepare` en `RcHcGame.dat -game`, zonder menu (dus ook zonder de controller-als-muis). Ook `--spelen` volgt dit. |
| Console and cheats | Standaard aan; telt alleen met een startlevel. `StartLevel.Prepare(…, console: true)` zet `Console=1` in `[General]` van de verse `Player10.ini` (`IniFile.SetOrAdd`, want `PlayerDef.ini` heeft die sleutel niet). Uitleg: `` ` `` opent de console, `cheats on`, dan Esc → "Cheats...". Raakt alleen testspeler 10. |
| Debug keys | Standaard uit. `DebugSecretCommands=1` in `AdamWin.ini [Debug]`, alleen bij een start met startlevel; Play zonder startlevel zet hem terug naar de basis (`0`). Geeft ook direct het cheatmenu. Staat WASD of de moderne controller aan, dan waarschuwt de statusregel (`TestOptions.KeysClashWith`: A, S, F5, F6). |
| Load log on screen | Standaard uit. `DebugUseConsole=1`, met dezelfde regel als Debug keys. |
| Log graphics driver, Log loading, Log player position | `DebugDriver`, `DebugLoading`, `DebugPlayerPosition` in `AdamWin.ini [Debug]`. Zodra er een aan staat, gaan de logs ook aan (`TestOptions.Effective`); de regel Debug logs onder Advanced toont dan "On (Testing tab)". |
| Clean up logs | `DebugLogs.Clean`, met het aantal verwijderde bestanden in de statusregel |
| Open log folder | De spelmap in de bestandsbeheerder: `explorer.exe` op Windows; onder Proton/Wine `winebrowser` met een `file://`-adres, dat het aan `xdg-open` geeft (Linux-bestandsbeheerder); native op Linux `xdg-open` |

Bewaard in `RedCatLauncher.ini [Testen]` (zie "Waar de launcher zijn keuze bewaart"; `Console` ontbreekt in een
bestand van vóór die schakelaar en staat dan aan). Bij Play zet `TestOptions.Apply` alleen de schakelaars uit
`TestOptions.DebugKeys` plus `DebugSecretCommands` en `DebugUseConsole` (die twee alleen met een startlevel): `1` als
ze aan staan, anders de waarde uit de basis; wat er anders in `AdamWin.ini` staat (ook andere `[Debug]`-sleutels),
blijft staan. Een met de hand aangezette `DebugSecretCommands` gaat bij Play zonder startlevel dus ook uit. Dat is
herhaalbaar vanaf elke vorige stand (`TestOptionsTests`, `BasisTests.Apply_WithTestOptions_FromAnyPrevious_EqualsFreshInstall`).
Een nieuwe log-schakelaar komt in `TestOptions.DebugKeys` en in `DebugSwitches` van `MainWindow` (label en uitleg).
Geen schakelaar voor `DebugKeys` (alleen ruis) en `DebugFake*` (crashes). De patcher zelf (`--start-level`) zet de
console niet aan; dat doet alleen de launcher.

## Herhaalbaar toepassen en terugdraaien

Twee manieren:

**A. Elke patch omkeerbaar maken.** `BinaryPatch.Revert` (`Replace` → `Find`), originele INI-waarden als constanten,
`HUDIcon.ini` met een marker of vanaf bekende originelen. Nadelen: voor elk van de ruim tien binaire patches moet
gecontroleerd worden dat het `Replace`-patroon uniek is in alle drie de builds; wisselen tussen 16:9 en 16:10 wordt
een keten van terugdraaien en toepassen; en de versie blijft onbekend, want `GameVersion.Detect` herkent een gepatcht
bestand niet. Een extra tabel met "hashes na patchen" groeit met elke combinatie van opties.

**B. Opbouwen uit een basis (aanbevolen).** De patcher bewaart na de officiële updates, vóór de fixes, een kopie van
de bestanden die de launcher aanpast, bijvoorbeeld in `RedCatPatcher\basis\`:

| Bestand | Waarom |
|---|---|
| `RcHcGame.dat` | Fixes, logs, breedbeeld, joysticktabel |
| `RcHcMenu.dat` | Fixes, logs |
| `GlideDrv.dll` | Logs, breedbeeld |
| `RedCat.exe` | Fixes (spel naar de voorgrond, icoon) |
| `AdamWin.ini`, `settings\static.ini`, `settings\PlayerDef.ini`, `settings\HUDIcon.ini` | Originele waarden voor FOV, toetsen en iconposities |

Bij "Spelen" vergelijkt de launcher de gewenste keuze met de toegepaste keuze (beide in het keuzebestand). Verschillen
ze, dan:

1. `GameVersion.Detect` op de basis: dat zijn precies de bekende bestanden, dus de versie en `ExeBuild` volgen vanzelf
   en een gewijzigde of beschadigde basis valt op.
2. De binaire bestanden in het geheugen opnieuw opbouwen: basis → `GamePatches.All` → `DebugLogs` (tenzij aan) →
   breedbeeld → joysticktabel. Dit is dezelfde code als nu, alleen op een kopie; elke patch draait altijd orig → nieuw.
3. Wegschrijven naar een tijdelijk bestand en dan vervangen, zodat een onderbreking geen half gepatcht spel achterlaat.
4. De INI-sleutels die de launcher beheert, zetten op de waarde uit de basis of de optie. Alleen die sleutels: wat de
   speler of het menu verder in die bestanden heeft veranderd, blijft staan. `HUDIcon.ini X` rekent vanaf de basis.
5. `dgVoodoo.conf`: de zeven beeldsleutels zetten.
6. De nieuwe toegepaste keuze wegschrijven.

Zo is elke stap per definitie idempotent en terug te draaien, zonder omgekeerde patronen. Het kost een paar MB schijf (de XP-build van `RcHcGame.dat` is 4,4 MB)
en een kleine aanpassing in de patcher (basis bewaren). `Patching/` moet daarvoor met byte-arrays kunnen werken in
plaats van alleen met paden; nu leest en schrijft elke patch zelf het bestand (`BinaryPatch.Apply(gameDir, …)`,
`Controller.PatchTable(path, …)`). Daarvoor zijn er nu `BinaryPatch.Apply(byte[] data, ExeBuild)` en
`Controller.PatchTable(byte[] data, mapping)`: ze patchen in het geheugen (de data verandert alleen als alles lukt) en de
padversies roepen ze aan.

Voorwaarden: het spel mag niet draaien tijdens het opbouwen (op Windows zit `RcHcGame.dat` dan op slot; de launcher
controleert dat en meldt het). Een installatie zonder basis (gepatcht met een oudere patcher) kan de launcher niet
omzetten; die zegt dan "opnieuw patchen met de disc".

## Waar de launcher zijn keuze bewaart

In de spelmap, naast `RedCat.exe`: `RedCatLauncher.ini` (INI, want `IniFile` bestaat al en het is met de hand te
lezen). Twee secties:

```ini
[Keuze]          ; wat de speler wil (launcher of patcher schrijft dit)
Widescreen=16:9
Wasd=1
Controller=0
DebugLogs=0
Display=borderless
Resolution=max
FpsLimit=0
VSync=1
Msaa=4x
Crt=0

[Toegepast]      ; wat er nu in de bestanden staat
Widescreen=16:9
...
BasisSha256=...  ; ter controle van RedCatPatcher\basis
```

- **De patcher schrijft het ook**, met de opties van de opdrachtregel. Zo blijven de opties in de patcher werken (de
  afspraak van 17-10) en begint de launcher met wat er gepatcht is.
- **In de spelmap, niet in `%APPDATA%`**: de map is draagbaar (USB-stick, andere pc), Proton en Windows zien hetzelfde
  bestand, en er is één installatie per map. Een tweede installatie heeft zijn eigen keuze.
- **`BackupUserData`** neemt het bestand mee bij opnieuw patchen, net als `menu.ini`: dezelfde keuze komt terug.
- Testinstellingen (`[Debug]`-schakelaars, startlevel) staan niet in `[Keuze]` maar in een eigen sectie `[Testen]`
  (`StartLevel=0..5`, `Console=0/1`, `DebugDriver=0/1`, ...), die alleen de launcher schrijft en alleen als er iets aan staat. De
  patcher schrijft het bestand zonder `[Testen]`, dus opnieuw patchen zet de testhulp terug, net als `AdamWin.ini`.

## Proton en Steam

- De launcher wordt een **Windows-programma** dat op Linux onder Proton draait, in hetzelfde prefix als het spel.
  Dan kan hij `RedCat.exe` en `RcHcGame.dat -game` gewoon starten, ook voor de Testen-tab. Een native Linux-launcher zou
  zelf Proton moeten aanroepen; dat is veel lastiger en Steam doet het al.
- Risico: een .NET 10-programma met een grafische interface onder Proton. Consoleprogramma's werken onder Wine meestal;
  een GUI-framework moet apart getest worden (WinForms kan niet getrimd worden, de patcher is nu `PublishTrimmed`
  single-file). Dit is de eerste proef, vóór er een scherm ontworpen wordt.
- Steam Deck in Game Mode: geen muis en toetsenbord. De launcher moet met een controller te bedienen zijn, of een
  "direct spelen"-stand hebben (zonder venster `RedCat.exe` starten met de toegepaste keuze), zodat hij in Game Mode
  niet in de weg zit.
- Steam-snelkoppeling: `SteamShortcuts` herkent een bestaande regel aan de appid of aan de exe. Wijst de snelkoppeling
  voortaan naar de launcher, dan klopt geen van beide meer en ontstaat er een tweede RedCat in Steam. De patcher
  herkent daarom ook een regel met `Exe` = `RedCat.exe` in dezelfde map en werkt die bij (appid, Proton-keuze en
  speeltijd blijven). Gedaan in stap 6, zie hieronder.

### Keuze: wat Steam start (stap 6)

De Steam-regel start **het launchervenster**, zonder startopties. Afwegingen:

- Het venster is in Game Mode te bedienen met de controller (stap 5): d-pad/stick, A, LB/RB, Start speelt. Een speler
  die op de Deck iets wil wisselen (16:10, controller-indeling), kan dat dan zonder de bureaubladmodus.
- Na Play verbergt de launcher zich en blijft hij draaien tot `RedCat.exe` stopt (voor de controller in de menu's).
  Steam ziet de game dus de hele tijd als "bezig", ook met `--spelen`; daarin verschillen de twee niet.
- `--spelen` als vaste startoptie zou het venster in Game Mode onbereikbaar maken; wie direct wil spelen, zet het zelf
  bij Eigenschappen → Startopties. De patcher laat de startopties van een bestaande regel staan (ook bij het omzetten
  van een oude `RedCat.exe`-regel), dus die keuze, en bijv. `PROTON_LOG=1 %command%`, blijft bij opnieuw patchen.
- Pictogram (Windows): blijft `RedCat.exe`, het spelpictogram, voor de snelkoppelingen en Steam.

## Proef GUI-framework (stap 4)

Code en teststappen (inmiddels verwijderd): `spikes/launcher` in commit [`0d49696`](https://github.com/Laurensd/RCSpookKasteelPatcher/tree/0d49696/spikes/launcher). Twee keer hetzelfde venster (Spelen, Afsluiten,
XInput-controller), single-file win-x64, self-contained .NET 10. Gemeten op Windows 11 (17-10-2026), gemiddelde van
vijf starts tot het venster getoond is:

| | WinForms | Avalonia 12.1 + Fluent |
|---|---|---|
| Trimmen | Nee (`NETSDK1175`) | Ja, zonder trimwaarschuwingen (UI in code, geen XAML) |
| Grootte, gecomprimeerd | 47 MB (107 MB zonder compressie) | 27 MB met ReadyToRun (20 MB zonder) |
| Opstarttijd | 0,19 s (0,13 s zonder compressie) | 0,4 s met ReadyToRun (0,9 s zonder; `--software` 0,7 s) |
| Tekenen | GDI via Wine's `user32`/`gdiplus` | Skia via ANGLE (Direct3D 11, onder Proton DXVK), terugval op software |
| Platformen | Alleen Windows | Windows; native Linux kan later met dezelfde code |
| Controller | Eigen XInput-poll (`xinput1_4.dll`), focus verplaatsen met `Focus()` | Idem, `Focus(NavigationMethod.Tab)` |
| Spel starten | `Process.Start` vanuit `AppContext.BaseDirectory`, werkt | Idem, werkt |

Bevindingen tot nu toe:

- **Beide werken op Windows**: venster, focusrand, Enter op Spelen start `RedCat.exe` en sluit de launcher.
- **Controller**: geen van beide frameworks leest zelf een gamepad. Een eigen XInput-poll van ~50 regels (gedeeld in
  `Shared/Spike.cs` van de proef) is genoeg voor d-pad/stick, A en B; onder Steam Input komt de Deck-controller standaard als
  XInput-pad binnen. Dit is dus geen onderscheid tussen de frameworks.
- **Grootte en trimmen** pleiten voor Avalonia: getrimd 27 MB tegen 47 MB, en dezelfde trimbuild als de patcher. De
  prijs is 0,2 s extra opstarttijd en native Skia/HarfBuzz/ANGLE-DLL's die bij de eerste start uitgepakt worden.
- **Het grootste risico zit onder Proton bij Avalonia**: ANGLE op DXVK. Daarom heeft de proef `--software` om dat uit
  te sluiten. WinForms leunt op Wine's eigen GDI en gdiplus; dat werkt doorgaans, maar ziet er gedateerd uit.

Nog te testen (met de hand, zie de teststappen in de README): Linux-desktop onder Proton, Steam Deck in Game Mode
(leesbaarheid op 1280×800, d-pad/A/B, of Steam de sessie vasthoudt nadat de launcher sluit).

Keuze (17-10-2026): **Avalonia**, getrimd single-file met ReadyToRun, zoals de proef. Blijft het venster op een
systeem leeg of zwart, dan is software-rendering (`Win32RenderingMode.Software`) de terugval; een launcher heeft geen
GPU nodig. De controller via de eigen XInput-poll uit de proef.

## Een echt menu in het spel?

Een menu tijdens het spel (zoals een debugmenu of een overlay) vraagt native code in het spelproces:

- **Inhaken.** Een proxy-DLL of een extra import. `winmm.dll` als proxy (het spel gebruikt `joyGetPosEx`) laadt onder
  Wine/Proton alleen met `WINEDLLOVERRIDES="winmm=n,b"` in de startopties: extra Steam-configuratie per gebruiker.
  `Glide2x.dll` vervangen door een eigen DLL die naar die van dgVoodoo doorstuurt, kan zonder override (Wine heeft
  geen eigen Glide), maar maakt de patcher afhankelijk van de exports en het gedrag van een specifieke dgVoodoo-versie.
  Een nieuwe import in `RcHcGame.dat` (eigen DLL-naam) kan ook zonder override, maar is een PE-aanpassing in drie builds.
- **Tekenen.** Het spel tekent via Glide 2 naar dgVoodoo (en onder Linux via DXVK naar Vulkan). Een overlay moet in
  `grBufferSwap` met Glide-driehoeken of `grLfbWriteRegion` een menu met eigen lettertype tekenen, op de interne
  renderresolutie (`max`, `2x`, …), met of zonder breedbeeld en CRT.
- **Invoer en pauze.** Toetsen en de joystick moeten naar het menu en niet naar het spel (`GetAsyncKeyState` en
  `joyGetPosEx` onderscheppen). Pauzeren door de engine-lus op te houden laat `CAdamClock` daarna een grote tijdsprong
  zien; hoe de engine daarmee omgaat, is niet onderzocht.
- **Weinig winst.** Bijna alles uit groep 2 wordt alleen bij het starten gelezen: de Glide-modus en het FOV, de
  joysticktabel (bij het opstarten), `dgVoodoo.conf`, de toetsen uit `Player%d.ini`. Een menu in het spel kan die dus
  ook niet live wisselen; het zou "herstart nodig" moeten zeggen, net als een launcher. Echte meerwaarde heeft het
  alleen voor dingen die tijdens het spelen gebeuren (level overslaan, teleporteren, onkwetsbaar), en daarvoor kan de
  engine misschien zelf al iets (`DebugSecretCommands`, `DebugKeys`, `DebugUseConsole`).

| | Menu in het spel | Tabblad Testen in de launcher |
|---|---|---|
| Code | C/C++ 32-bit DLL, eigen toolchain in CI, Glide- en Win32-hooks | C#, hergebruikt `StartLevel`, `IniFile`, `DebugLogs.Clean` |
| Testen | Alleen handmatig, in het spel, op Windows, Proton en de Steam Deck apart | Unit-tests in CI zoals nu (`OptionsPatchesTests`), plus één keer handmatig |
| Risico | Crashes in het spelproces, afhankelijk van dgVoodoo-versie, Wine-overrides, virusscanners die DLL-injectie niet vertrouwen | Laag: schrijft dezelfde bestanden als de patcher nu |
| Werk | Weken, met een onzekere uitkomst (pauze, overlay onder DXVK) | Dagen, zodra de launcher bestaat |
| Kan | Live dingen in het spel | Level kiezen, `[Debug]`-sleutels, logs aan/opruimen, logmap openen |

Conclusie: geen eigen in-game menu. De engine heeft er zelf al een (stap 8): het Esc-menu krijgt "Cheats..." met
volgende level, ga naar level, door muren vliegen en godmodus, en er is een console. Die zet de Testen-tab aan. Een native laag hoort pas op tafel als er een
tweede reden voor is (bijvoorbeeld de XInput-laag voor het menu uit "Optie `--controller`", route (b)), en dan als
eigen project met een proef onder Proton vóór er iets gebouwd wordt.

## Aanbevolen volgorde

1. ~~**`HUDIcon.ini` idempotent maken**~~ (gedaan: breedbeeld-INI's zijn herhaalbaar en omkeerbaar).
2. ~~**`Patching/` op byte-arrays**~~ (gedaan): `Basis.Build` bouwt basis + keuze (`Choice`) in het geheugen op tot
   de vier binaire bestanden (`Basis.BinaryFiles`), `Basis.Apply` schrijft ze en zet de INI-sleutels en `dgVoodoo.conf`. Tests over alle combinaties
   (`BasisTests`): elke keuze vanaf elke vorige keuze geeft dezelfde bestanden als een verse installatie.
3. ~~**De patcher bewaart de basis en schrijft `RedCatLauncher.ini`**~~ (gedaan). De patcher gebruikt zelf ook
   `Basis.Apply`; de bestanden zijn byte voor byte gelijk aan die van v0.2.0. Zonder keuze-opties op de opdrachtregel
   neemt opnieuw patchen de `[Keuze]` uit `RedCatLauncher.ini` over.
4. ~~**Proef: GUI-framework onder Proton**~~ en op de Steam Deck (een leeg venster dat `RedCat.exe` start, single-file).
   Gedaan: Avalonia (zie "Proef GUI-framework").
5. ~~**Launcher, tabblad Spelen**~~ (gedaan, `src/RedCatLauncher`): groep 2 via `Basis.Apply`, één regel per optie,
   te bedienen met controller (XInput-poll uit de proef, met herhaling bij ingedrukt houden). `--spelen` past toe en
   start zonder venster; `--software` tekent zonder GPU. De patcher kopieert `RedCatLauncher.exe` naast zich mee naar
   de spelmap. Nog met de hand te testen onder Proton en op de Deck (teststappen in de PR). Daarna Engelstalig gemaakt
   met een tabbalk: Display, Graphics, Controls en Advanced (LB/RB of PgUp/PgDn wisselt); Testen kan daar later bij.
6. ~~**Snelkoppelingen en Steam naar de launcher**~~ (gedaan), met herkenning van de oude regel naar `RedCat.exe`:
   een Steam-regel aan appid, exe of `RedCat.exe` in dezelfde map (appid, speeltijd, startopties en Proton-keuze
   blijven; `--proton` alleen nog als je hem opgeeft), op Windows ook bestaande `.lnk`'s naar `RedCat.exe` op het
   bureaublad en in het Startmenu. Steam start het venster, niet `--spelen` (zie "Keuze: wat Steam start").
7. ~~**Tabblad Testen**~~ (gedaan, tabblad Testing): startlevel (`StartLevel`), `[Debug]`-sleutels (logs automatisch
   aan), logs opruimen, logmap openen. Zie "Tabblad Testing" onder groep 3.
8. ~~**Onderzoek** naar `DebugSecretCommands`, `DebugKeys` en `DebugUseConsole`~~ (gedaan, zie
   "`AdamWin.ini [Debug]`" en `TECHNISCH.md`). Schakelaars voor het tabblad Testing (gebouwd, zie "Tabblad Testing"):
   - **Console en cheats** (standaard aan bij een testlevel): `Console=1` in `[General]` van `Player10.ini`, na de kopie
     uit `PlayerDef.ini` in `StartLevel`. Raakt alleen testspeler 10, dus niet het gewone spelen en geen toetsbotsingen.
     Uitleg in de launcher: `` ` `` opent de console, `cheats on` en dan Esc → "Cheats...".
   - **Debugtoetsen** (standaard uit): `DebugSecretCommands=1`. Geeft ook direct het cheatmenu. Met een waarschuwing
     als `--wasd` of `--controller` aan staat (A, S, F5, F6). `AdamWin.ini` geldt ook voor gewoon spelen, dus de
     launcher zet hem alleen voor een teststart en bij Play weer op `0`.
   - **Laadlog in beeld** (standaard uit): `DebugUseConsole=1`, met dezelfde regel voor gewoon spelen.
   - Geen schakelaar voor `DebugKeys` (alleen ruis) en `DebugFake*` (crashes).
9. Pas daarna, en alleen met een tweede reden, een besluit over native code in het spel.

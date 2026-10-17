# Technische achtergrond

Analyse van **RedCat Spookkasteel v1.0.0** (Davilex, 2000, ISBN 9055826200) op Windows 11.
Disc-image: `RedCat.bin`, één datatrack MODE2/2352, SHA-1 `5fe44a24722141d6a264ce3a613643e0583fa1a4`.

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

## Fix 3 — dialogen buiten beeld (`RcHcMenu.dat`)

Het naamvenster, de afsluitvraag en andere dialogen erven van `TfrmCoolDialog` met `Position = poDesktopCenter`.
Delphi 5 centreert dan op de breedte en hoogte van de *virtuele* desktop, maar telt de oorsprong ervan niet mee.
Met een tweede monitor (bijvoorbeeld boven of links van het hoofdscherm) komt de dialoog buiten het 800×600-beeld te staan.
Het menu lijkt dan vast te lopen, omdat het modale venster onzichtbaar is.

Fix in de binaire DFM-resource: `poDesktopCenter` wordt `poScreenCenter`. Dat is één byte korter. Om de resource even lang te
houden, krijgt de `Caption` van het basisformulier één spatie extra; die Caption wordt door elke subklasse overschreven.

## dgVoodoo2

De Genesis3D-drivers gebruiken Glide 2 of DirectDraw/Direct3D 6. dgVoodoo2 vertaalt beide naar Direct3D 11/12.
Op Linux gaat dat via DXVK. Het spel kiest automatisch de Glide-driver (`Glide2x.dll` uit de spelmap), dus onder Proton
zijn er geen DLL-overrides nodig. Instellingen die de patcher zet in `dgVoodoo.conf`:

- `VRAM = 128`: de originele D3D-driver ziet meer dan 2 GB videogeheugen als negatief getal.
- `ScalingMode = stretched_ar`, `FullscreenAttributes = fake`: randloos schermvullend beeld in 4:3.
- Watermerken uit.

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

## Testen zonder menu

`RcHcGame.dat -game` start direct het eerste level als `settings\Player1.ini` bestaat (kopie van `PlayerDef.ini`).
Met `DebugDriver=1` in `AdamWin.ini [Debug]` logt `AdamDebug*.log` de gekozen driver en modus, en met
`FullScreenMode = false` in `dgVoodoo.conf` draait het spel in een venster.

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

## Ideeën voor later

- Breedbeeld: `Mode=` in `AdamWin.ini` en het FOV in `[Camera-*]` (`FieldOfView`), plus het HUD (`settings\HUDIcon.ini`).
- Besturing: het spel leest toetsen via `GetAsyncKeyState`/DirectInput. De toewijzing staat in het menu onder Instellingen en in `settings\PlayerDef.ini`.

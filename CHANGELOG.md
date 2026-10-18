# Changelog

Alle noemenswaardige wijzigingen aan de RedCat Spookkasteel patcher.
Opmaak volgens [Keep a Changelog](https://keepachangelog.com/nl/1.1.0/), versies volgens [Semantic Versioning](https://semver.org/lang/nl/).

## [Unreleased]

### Toegevoegd
- `RedCatLauncher.exe` naast `RedCat.exe`: kies breedbeeld, besturing, controller-indeling, beeldopties en logbestanden zonder opnieuw te patchen, en start het spel. Werkt met muis, toetsenbord en controller (ook op de Steam Deck in de gamemodus), en met `--spelen` zonder venster. De patcher zet de launcher in de spelmap; de release-zip en -tar.gz bevatten hem.
- De patcher bewaart een kopie van de spelbestanden vóór de fixes (`RedCatPatcher\basis` in de spelmap) en schrijft de gekozen opties naar `RedCatLauncher.ini`. Daarmee kan de launcher later een andere keuze toepassen en terugdraaien zonder de disc.
- Opnieuw patchen zonder opties voor breedbeeld, besturing, beeld of logs gebruikt de keuze uit `RedCatLauncher.ini`.
- Tabblad Testing in de launcher: start direct een gekozen level (zoals `--start-level`, met testspeler 10), zet extra debugmeldingen aan (`DebugDriver`, `DebugLoading`, `DebugPlayerPosition` in `AdamWin.ini`; de logbestanden gaan dan vanzelf aan), ruim de logbestanden op en open de logmap (ook onder Proton, in de bestandsbeheerder van Linux). Opnieuw patchen zet deze testinstellingen terug.

### Gewijzigd
- Snelkoppelingen (`--shortcut`) en de game in Steam (`--steam`) starten nu `RedCatLauncher.exe` in plaats van `RedCat.exe`. Bestaande snelkoppelingen en de Steam-regel van een eerdere versie worden omgezet bij opnieuw patchen met die optie, zonder tweede RedCat in Steam: speeltijd, startopties, plaatjes en de gekozen Proton-versie blijven. In Steam (ook op de Steam Deck in de gamemodus) opent de launcher met zijn venster; zet `--spelen` bij de startopties om direct te spelen.
- `--proton` overschrijft een in Steam gekozen Proton-versie alleen nog als je de optie opgeeft.

### Opgelost
- Menu's met de controller: de launcher laat de linkerstick de muis bewegen in de menu's, A klikt, B is Esc en Start is Enter (uit te zetten onder Controls). Het menu zelf leest geen controller.
- Controller werkt niet als Windows hem een ander joystick-ID dan 0 of 1 geeft (bijv. naast virtuele gamepads van Steam of Oculus): het spel zoekt nu alle 16 ID's af. Bestaande installaties krijgen de fix bij de volgende Play in de launcher.
- Spel, het zwarte achtergrondvenster van `RedCat.exe` en de launcher hebben het RedCat-icoon in de taakbalk in plaats van een leeg standaardicoon.
- Het spel komt na Start! in het menu op de voorgrond; je hoeft er niet meer met alt-tab naartoe. Dit ging vooral mis bij de eerste start na het patchen, als je tijdens het laden klikte (bijv. een dubbelklik op Start!) of een toets indrukte: `RedCat.exe` wachtte op het spel zonder berichten te verwerken, waardoor het zwarte venster "reageert niet" werd en de voorgrond vasthield. `RedCat.exe` wacht nu met een eigen lus die berichten verwerkt en het spelvenster naar voren haalt.
- **Foutmelding bij de knop Help.** De hulp-agent in het menu gebruikte DAO 3.5 (`DAO.CreateEngineFactory failed`), dat niet in Windows 10/11 zit. Het menu gebruikt nu DAO 3.6, dat Windows zelf meelevert.
- **Helpteksten van de agent.** Een vraag in het ballonnetje opende een WinHelp-popup, en WinHelp bestaat niet meer (er opende alleen een webpagina van Microsoft). De patcher haalt de teksten uit `REDCAT.HLP` en het menu toont ze nu in een berichtvenster.

## [0.2.0] - 2026-10-17

### Toegevoegd
- Optie `--start-level <1-5>`: start het gepatchte spel direct in een gekozen Griezelplek met een testspeler, zonder menu. Om bijvoorbeeld de crash bij de muur van kratten in level 4 (De Grotten) te testen.
- Optie `--widescreen [16:9|16:10]`: breedbeeld (856×480 of 640×400) met een breder blikveld en een HUD dat niet wordt uitgerekt. Standaard 16:9.
- Optie `--wasd`: W/A/S/D als standaardbesturing in plaats van de pijltjestoetsen, voor nieuwe spelers en via de knop Standaard bij Instellingen.
- Optie `--controller`: logische gamepad-indeling in het spel (A springen, B ontwijken, X schieten, LB/RB wapen wisselen, Back scores, Start menu), via de joystickcode die de engine zelf al had. Werkt ook met een Xbox-controller en op de Steam Deck; het menu blijft muis/toetsenbord.
- Ondersteuning voor de officiële Win98-update (v1.0.1) en XP-update (2004) van Davilex. Zet de update-exe's naast de patcher of geef ze op met `--update`; de patcher pakt ze zelf uit (zonder de installer te draaien) en past de fixes en opties toe op de bijbehorende build.
- Versieherkenning: de patcher toont welke versie hij patcht en stopt met een melding (grootte, SHA-256) bij een onbekende versie of update, in plaats van halverwege een patroon te missen.
- Disc-controle: de patcher hasht de disc-image (SHA-1, met voortgang) en meldt `Disc herkend: v1.0.0` bij een bekende dump, als `.cue`/`.bin` én als `.iso`. Bij een onbekende rip controleert hij de spelbestanden die hij nodig heeft; ontbreken die, wijken ze af of is de image onvolledig, dan volgt meteen een duidelijke waarschuwing (en interactief de vraag of je toch wilt doorgaan) in plaats van een vage fout halverwege.
- Scherper beeld: dgVoodoo2 rendert het spel standaard op schermresolutie (`[Glide] Resolution = max`) in plaats van 640x480 op te rekken. Aan te passen met `--resolution` (bijv. `2x`, `1920x1080`, of `unforced` voor het origineel).
- Beeldopties voor dgVoodoo2: `--display borderless|fullscreen|window` (randloos schermvullend, exclusief volledig scherm of een venster van 2× de spelresolutie), `--fps-limit <n>` en `--no-vsync`, `--msaa off|2x|4x|8x|16x` (standaard 4x: gladde randen) en `--crt` (scanlines zoals een oude monitor).

- Snelkoppelingen na het patchen (de patcher vraagt het, of gebruik `--shortcut`): op het bureaublad en in het Startmenu (Windows) of in het programmamenu (Linux).
- Optie `--steam`: RedCat toevoegen aan Steam als niet-Steam-game, voor alle Steam-gebruikers op de computer. Op Linux en de Steam Deck stelt de patcher meteen Proton in (`--proton` om een andere versie te kiezen). Van `shortcuts.vdf` en `config.vdf` wordt eerst een backup gemaakt.
- Optie `--debug-logs`: logbestanden van spel en menu aan laten, voor het zoeken naar problemen.

### Gewijzigd
- Spel, menu en Glide-driver schrijven standaard geen logbestanden meer in de spelmap (`AdamDebug*.log`, `Sound.log`, `GCache.Log`, een log per level in `Levels`, `MenuTrac.log`). Alleen bij een echte fout verschijnt nog `AdamError*.log` of `rchcmenu.log`. Opnieuw patchen ruimt oude logbestanden op.

### Opgelost
- Met `--wasd` bestuurde de joystick-stick niets meer (die stuurde de pijltjestoetsen); de stick volgt nu W/A/S/D.
- Het menu zette het bureaublad op 800×600, waardoor andere monitoren opschoven en alt-tab misging. Het menu laat de schermresolutie nu ongemoeid en verschijnt als 800×600-beeld gecentreerd op het hoofdscherm, met zwart eromheen.
- In venstermodus (alt-enter of `FullScreenMode = false`) stond het spelvenster te hoog, met de titelbalk op een monitor erboven of buiten beeld. dgVoodoo2 centreert het venster nu op het hoofdscherm (`CenterAppWindow = true`).

## [0.1.0] - 2026-10-17

Eerste release. Maakt de cd-rom van RedCat Spookkasteel v1.0.0 (Davilex, 2000) speelbaar op Windows 10/11 en, via Proton/Wine, op Linux.

### Toegevoegd
- Patcher (`RedCatPatcher.exe` voor Windows, `RedCatPatcher` voor Linux) die het spel rechtstreeks uit je eigen disc-image haalt. Ondersteunt `.cue` + `.bin` (MODE1/MODE2), losse `.bin` en `.iso`, en leest ISO 9660 met Joliet zodat de originele bestandsnamen behouden blijven.
- Zoekt de disc automatisch naast de patcher. Lukt dat niet, dan kun je het `.cue`-bestand in het venster slepen.
- Installatie van dgVoodoo2 2.87.5, gedownload van de officiële GitHub-release en gecontroleerd met een SHA-256-hash. Met `--dgvoodoo-zip` kan het ook offline.
- Opties `--out`, `--no-dgvoodoo` en `--yes`.
- Bij opnieuw patchen in een bestaande map blijven spelers, scores en savegames behouden.

### Opgelost
- **Crash bij het laden van een level** ("Er is een onverwachte fout opgetreden in RedCat"). De lettertype-code van de Genesis3D-engine liep vast op hoe moderne Windows-versies de spatie verwerken (`GetGlyphOutline`) en schreef daarbij buiten het geheugen.
- **Naamvenster en afsluitvraag buiten beeld bij meerdere monitoren.** Het menu leek daardoor vast te lopen. De dialogen worden nu gecentreerd op het hoofdscherm.
- **Menu in geëmuleerd 16-bit kleur.** Het menu kiest nu een 32-bit beeldmodus, zodat de compatibiliteits-shim van Windows niet meer nodig is.
- **Spel start in een klein venster.** Het draait nu randloos schermvullend in 4:3.
- **Installer loopt vast op `Verdanaz.ttf`.** Een installatie is niet meer nodig: het spel draait direct vanuit de map.
- **Cd vereist.** Niet meer: alles staat lokaal en er zijn geen registersleutels nodig.
- Grafische fouten met de oude DirectX/Glide-drivers op moderne videokaarten, zoals negatief gerapporteerd videogeheugen, opgelost met dgVoodoo2.

[Unreleased]: https://github.com/Laurensd/RCSpookKasteelPatcher/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Laurensd/RCSpookKasteelPatcher/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/Laurensd/RCSpookKasteelPatcher/releases/tag/v0.1.0

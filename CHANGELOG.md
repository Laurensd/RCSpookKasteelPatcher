# Changelog

Alle noemenswaardige wijzigingen aan de RedCat Spookkasteel patcher.
Opmaak volgens [Keep a Changelog](https://keepachangelog.com/nl/1.1.0/), versies volgens [Semantic Versioning](https://semver.org/lang/nl/).

## [Unreleased]

### Toegevoegd
- Optie `--widescreen [16:9|16:10]`: breedbeeld (856×480 of 640×400) met een breder blikveld en een HUD dat niet wordt uitgerekt. Standaard 16:9.
- Optie `--wasd`: W/A/S/D als standaardbesturing in plaats van de pijltjestoetsen, voor nieuwe spelers en via de knop Standaard bij Instellingen.
- Optie `--controller`: logische gamepad-indeling in het spel (A springen, B ontwijken, X schieten, LB/RB wapen wisselen, Back scores, Start menu), via de joystickcode die de engine zelf al had. Werkt ook met een Xbox-controller en op de Steam Deck; het menu blijft muis/toetsenbord.
- Ondersteuning voor de officiële Win98-update (v1.0.1) en XP-update (2004) van Davilex. Zet de update-exe's naast de patcher of geef ze op met `--update`; de patcher pakt ze zelf uit (zonder de installer te draaien) en past de fixes en opties toe op de bijbehorende build.
- Versieherkenning: de patcher toont welke versie hij patcht en stopt met een melding (grootte, SHA-256) bij een onbekende versie of update, in plaats van halverwege een patroon te missen.
- Disc-controle: de patcher hasht de disc-image (SHA-1, met voortgang) en meldt `Disc herkend: v1.0.0` bij een bekende dump, als `.cue`/`.bin` én als `.iso`. Bij een onbekende rip controleert hij de spelbestanden die hij nodig heeft; ontbreken die, wijken ze af of is de image onvolledig, dan volgt meteen een duidelijke waarschuwing (en interactief de vraag of je toch wilt doorgaan) in plaats van een vage fout halverwege.
- Scherper beeld: dgVoodoo2 rendert het spel standaard op schermresolutie (`[Glide] Resolution = max`) in plaats van 640x480 op te rekken. Aan te passen met `--resolution` (bijv. `2x`, `1920x1080`, of `unforced` voor het origineel).

- Snelkoppelingen na het patchen (de patcher vraagt het, of gebruik `--shortcut`): op het bureaublad en in het Startmenu (Windows) of in het programmamenu (Linux).
- Optie `--steam`: RedCat toevoegen aan Steam als niet-Steam-game, voor alle Steam-gebruikers op de computer. Op Linux en de Steam Deck stelt de patcher meteen Proton in (`--proton` om een andere versie te kiezen). Van `shortcuts.vdf` en `config.vdf` wordt eerst een backup gemaakt.

### Opgelost
- Met `--wasd` bestuurde de joystick-stick niets meer (die stuurde de pijltjestoetsen); de stick volgt nu W/A/S/D.
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

[Unreleased]: https://github.com/Laurensd/RCSpookKasteelPatcher/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Laurensd/RCSpookKasteelPatcher/releases/tag/v0.1.0

# RedCat Spookkasteel — patcher

Maakt de Nederlandse cd-rom **RedCat Spookkasteel** (Davilex, 2000) weer speelbaar op Windows 10/11
en op Linux via Proton/Wine (Steam Deck, Legion Go S).

Je levert zelf de disc aan, als `.cue` + `.bin` of als `.iso`. De patcher haalt het spel van de disc, past de fixes toe en
installeert dgVoodoo2. Daarna start je het spel met `RedCat.exe`, zonder installatie en zonder cd.

## Gebruik

1. Zet `RedCatPatcher.exe` in een map met je disc-image, bijvoorbeeld:
   ```
   RedCatPatcher.exe
   RedCatDisc\RedCat.cue
   RedCatDisc\RedCat.bin
   ```
2. Dubbelklik op `RedCatPatcher.exe`. De patcher zoekt de disc zelf; anders kun je het `.cue`-bestand in het venster slepen.
3. Start `RedCat Spookkasteel\RedCat.exe`.

Opties (opdrachtregel):

```
RedCatPatcher [disc.cue|disc.bin|disc.iso] [--out <map>] [--update <exe>] [--dgvoodoo-zip <zip>] [--no-dgvoodoo]
              [--widescreen [16:9|16:10]] [--wasd] [--resolution <waarde>] [--yes]
```

`--dgvoodoo-zip` installeert offline met een zelf gedownloade
[dgVoodoo2 v2.87.5](https://github.com/dege-diosg/dgVoodoo2/releases/tag/v2.87.5).

### Disc-controle

Voor het kopiëren controleert de patcher de disc-image (een paar seconden, met voortgang). Een bekende dump geeft
`Disc herkend: v1.0.0`; een `.iso` van dezelfde cd wordt net zo goed herkend als de `.cue`/`.bin`. Een andere rip
is geen fout: kloppen de bestanden die de patcher aanpast, dan gaat hij gewoon door. Ontbreken die of wijken ze af, of is
de image onvolledig, dan krijg je meteen een waarschuwing en (zonder `--yes`) de vraag of je toch wilt doorgaan.
Maak in dat geval de image opnieuw.

### Officiële updates

Davilex bracht twee updates uit: de **Win98-update** (versie 1.0.1, juni 2001: spelbalans, nieuwe geluiden, bijgewerkte
leesmij) en de **XP-update** (december 2004: een nieuwe build van het spel). Beide staan op het
[Internet Archive](https://archive.org/details/redcatspookkasteelupdate). Wil je ze meenemen, zet dan
`Redcat Spookkasteel 98 patch.exe` en/of `Redcat Spookkasteel XP patch.exe` naast de patcher, of geef ze op met `--update`.
De patcher voert de installers niet uit maar pakt ze zelf uit, in de juiste volgorde.

Ondersteund zijn versie 1.0.0 van de cd-rom, met of zonder een of beide updates. Vindt de patcher een andere versie van een
spelbestand of een onbekende update, dan stopt hij zonder te patchen en meldt hij de bestandsgrootte en SHA-256.
[Meld die versie](https://github.com/Laurensd/RCSpookKasteelPatcher/issues), dan kan ze worden toegevoegd.

### Extra opties

Deze zijn standaard uit (behalve de renderresolutie); zet ze aan met een vlag (of maak een snelkoppeling naar de patcher met de vlag erachter).

| Vlag | Wat het doet |
|---|---|
| `--widescreen` | Breedbeeld 16:9 (856×480) in plaats van 4:3: breder beeld met dezelfde verticale kijkhoek, HUD-iconen niet uitgerekt en rechts uitgelijnd |
| `--widescreen 16:10` | Hetzelfde voor 16:10-schermen (640×400), bijvoorbeeld de Steam Deck |
| `--resolution <waarde>` | Interne renderresolutie (standaard `max`: het spel wordt op schermresolutie gerenderd in plaats van op 640×480 en daarna opgerekt, dus veel scherper). Andere waarden: `2x`, `3x`, `desktop`, `1920x1080`, of `unforced` voor het originele (wazige) beeld. Kies `2x` of `unforced` als het spel op een zwakke GPU hapert |
| `--wasd` | W/A/S/D in plaats van de pijltjestoetsen (vooruit, links draaien, achteruit, rechts draaien). Geldt voor nieuwe spelers en voor de knop Standaard bij Instellingen → Instellen |

Bijvoorbeeld:

```
RedCatPatcher.exe --widescreen --wasd
```

Spelers die al eigen toetsen hadden ingesteld houden die; kies bij Instellingen → Instellen op Standaard om WASD te krijgen.
Tussenfilmpjes en de tekstballonnen blijven 4:3-ontwerp en worden op breedbeeld breder getekend.

### Linux / Steam Deck

- Draai de Linux-versie van de patcher (`RedCatPatcher`), of de Windows-versie via Proton.
- Voeg `RedCat Spookkasteel/RedCat.exe` in Steam toe als niet-Steam-game en kies bij Compatibiliteit een Proton-versie.

## Wat er wordt opgelost

| Probleem | Fix |
|---|---|
| Installer loopt vast (`Verdanaz.ttf`) | Geen installatie meer nodig; het spel draait vanuit de map |
| Crash "onverwachte fout" bij het laden van een level | Patch in de lettertype-code van de engine (`RcHcGame.dat`) |
| Naamvenster/afsluitvraag buiten beeld, menu lijkt vast | Dialogen centreren op het hoofdscherm (`RcHcMenu.dat`) |
| Menu in geëmuleerd 16-bit kleur | Menu kiest 32-bit (`RcHcMenu.dat`) |
| Klein venster, oude DirectX/Glide-problemen | dgVoodoo2, schermvullend 4:3 |
| Wazig beeld op moderne schermen (640×480 opgerekt) | dgVoodoo2 rendert op schermresolutie (`--resolution`) |
| Cd nodig | Niet meer: alles staat lokaal |

Details en analyse: [docs/TECHNISCH.md](docs/TECHNISCH.md).

## Zelf bouwen

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

De executables komen in `dist/win-x64` en `dist/linux-x64`. Tests draaien met `dotnet test RedCatPatcher.slnx`.

### Release maken

Bij elke push en pull request draait GitHub Actions de tests op Windows en Linux.
Een release bouw je door een versietag te pushen. Zet eerst de wijzigingen onder een nieuwe kop `## [x.y.z]` in [CHANGELOG.md](CHANGELOG.md); die sectie worden de releasenotes. Zijn de tests geslaagd, dan publiceert de workflow de Windows-zip en de Linux-tar.gz als GitHub Release:

```
git tag v0.1.0
git push origin v0.1.0
```

## Juridisch

Deze repository bevat geen bestanden van het spel of van dgVoodoo2. De patcher werkt alleen met een disc die je zelf bezit.
dgVoodoo2 is gemaakt door Dege en wordt tijdens het patchen van de officiële GitHub-release gedownload.

De code van de patcher valt onder de [MIT-licentie](LICENSE), ook alle eerdere versies en commits van deze repository. Die licentie geldt niet voor het spel zelf (© Davilex) of voor dgVoodoo2, dat zijn eigen licentie heeft.

---

**English:** a patcher that makes the Dutch 2000 PC game *RedCat Spookkasteel* playable on Windows 10/11 and Linux (Proton).
Supply your own `.cue/.bin` or `.iso`, run the patcher, then start `RedCat.exe`. See [docs/TECHNISCH.md](docs/TECHNISCH.md) for the root-cause analysis.

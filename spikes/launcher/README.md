# Proef: GUI-framework voor de launcher

Wegwerpproef voor stap 4 van [`docs/LAUNCHER.md`](../../docs/LAUNCHER.md). Twee keer hetzelfde venster, met knoppen
**Spelen** (start `RedCat.exe` uit de eigen map en sluit) en **Afsluiten**:

| Bestand | Framework | Build |
|---|---|---|
| `RedCatLauncher-WinForms.exe` | WinForms | single-file, niet getrimd (kan niet), gecomprimeerd |
| `RedCatLauncher-Avalonia.exe` | Avalonia 12 + Fluent | single-file, getrimd, ReadyToRun, gecomprimeerd |

Onder in het venster staat de opstarttijd, of het onder Wine draait en of XInput geladen is. Dezelfde regel komt in
`launcher-spike.log` naast de exe. Bediening: muis, toetsenbord (Tab, Enter, Esc) of controller: pijltjes of linkerstick
= andere knop, **A** = kiezen, **B** = sluiten.

Bouwen (Windows): `powershell -ExecutionPolicy Bypass -File spikes\launcher\build.ps1`. Resultaat:
`spikes\launcher\dist\RedCatLauncher-proef.zip`.

## Testen

Zet beide exe's in de gepatchte spelmap, naast `RedCat.exe`. Noteer per stap per exe: werkt/werkt niet, plus de regel
onder in het venster (of de inhoud van `launcher-spike.log`).

### Windows

1. Dubbelklik elke exe. Verschijnt het venster, met tekst?
2. Klik **Spelen**: start het spel en verdwijnt het venster?

### Linux-desktop met Steam (Proton)

1. Steam → *Games* → *Add a Non-Steam Game* → *Browse*, kies `RedCatLauncher-WinForms.exe`. Herhaal voor de
   Avalonia-exe.
2. Rechtsklik elk item → *Properties* → *Compatibility* → *Force the use of a specific Steam Play compatibility tool*
   → dezelfde Proton-versie als de bestaande RedCat-snelkoppeling.
3. Start het item. Verschijnt het venster? Is de tekst leesbaar (geen lege of zwarte vlakken)?
   - Avalonia leeg of zwart: *Properties* → *Launch Options* `--software`, opnieuw starten. Werkt het dan wel?
4. Staat onder in het venster `wine=…` met een versie, en `xinput=True`?
5. Klik **Spelen**. Start het spel? Blijft Steam het item als "draaiend" tonen tot je het spel afsluit (en niet al
   zodra de launcher sluit)?
6. Sluit het spel. Staat Steam daarna weer op "Play"?

Let op: een nieuwe Non-Steam-snelkoppeling krijgt een eigen, leeg Proton-prefix (`compatdata/<appid>`). Voor deze
proef is dat goed (we testen het venster en het starten); dat het spel in dát prefix draait, hoort er niet bij.

### Steam Deck, Game Mode

Eerst stap 1–2 hierboven in Desktop Mode, dan terug naar Game Mode.

1. Start het item vanuit de bibliotheek. Verschijnt het venster schermvullend of als klein venster, en is het leesbaar
   op 1280×800?
2. Zonder het touchscreen aan te raken: wissel met de **d-pad** tussen Spelen en Afsluiten. Is te zien welke knop
   gekozen is (rand)?
3. Druk **A** op Spelen. Start het spel?
4. Start opnieuw en druk **B**: sluit de launcher en gaat de Deck terug naar de bibliotheek?
5. Werkt de d-pad niet: druk op de **Steam-knop** → *Controller settings* en noteer welke lay-out actief is (bijv.
   "Gamepad" of "Keyboard (WASD) and Mouse"). Probeer de lay-out *Gamepad*.
6. Werkt het touchscreen (tik op Spelen)?

using System.Text;

namespace RedCatPatcher.Patching;

/// <summary>
/// Alle fixes voor RedCat Spookkasteel v1.0.0 (Davilex, 2000).
/// Achtergrond en analyse per fix: zie docs/TECHNISCH.md.
/// </summary>
public static class GamePatches
{
    public static readonly IReadOnlyList<BinaryPatch> All =
    [
        // Genesis3D geFont_AddCharacters() vraagt glyph-metrics op mét een buffer. Sinds Windows Vista
        // geeft GetGlyphOutline daarvoor GDI_ERROR terug bij glyphs zonder pixels (de spatie) en vult
        // het GLYPHMETRICS niet in. De engine plaatst de letter dan met ongeïnitialiseerde afmetingen
        // en schrijft buiten de font-bitmap: crash bij het laden van elk level.
        // Fix: in die aanroep (cbBuffer, lpvBuffer) = (0, NULL), zodat alleen de metrics worden opgevraagd.
        // De XP-build heeft dezelfde code; alleen het adres van GetGlyphOutlineA in de importtabel verschilt.
        new("font-glyph-metrics", "RcHcGame.dat",
            "Crash bij laden level (lettertype-code, GetGlyphOutline)",
            [new(Hex("8b 54 24 50 50 68 00 80 00 00 51 56 52 57 ff 15 18 a0 64 00"),
                 Hex("8b 54 24 50 6a 00 6a 00 90 90 51 56 52 57 ff 15 18 a0 64 00"))],
            Xp2004:
            [new(Hex("8b 54 24 50 50 68 00 80 00 00 51 56 52 57 ff 15 b8 34 8a 00"),
                 Hex("8b 54 24 50 6a 00 6a 00 90 90 51 56 52 57 ff 15 b8 34 8a 00"))]),

        // Het menu probeert 800x600 in 24, 16, 15 en dan 32 bit. 24-bit bestaat niet meer, dus het wordt
        // 16-bit; Windows emuleert dat met de DWM8And16BitMitigation-shim. Fix: probeer eerst 32-bit.
        new("menu-32bit", "RcHcMenu.dat",
            "Menu schakelt naar 32-bit kleur in plaats van geëmuleerd 16-bit",
            [new(Hex("c6 45 ff 01 66 b9 18 00 66 ba 58 02 66 b8 20 03"),
                 Hex("c6 45 ff 01 66 b9 20 00 66 ba 58 02 66 b8 20 03"))]),

        // Alle menudialogen (naam invoeren, afsluiten, ...) erven van TfrmCoolDialog met
        // Position = poDesktopCenter. Delphi centreert dan op de virtuele desktop (alle monitoren)
        // zonder de oorsprong mee te nemen: met meerdere monitoren valt de dialoog buiten beeld.
        // Fix: poScreenCenter (primaire monitor). De DFM-resource moet even lang blijven, dus de
        // Caption (die door elke subklasse wordt overschreven) krijgt één spatie extra.
        new("dialog-center", "RcHcMenu.dat",
            "Menudialogen vallen buiten beeld bij meerdere monitoren",
            [new(Dfm(Ident("Caption"), VaString, Str("frmCoolDialog"), Ident("ClientHeight")),
                 Dfm(Ident("Caption"), VaString, Str("frmCoolDialog "), Ident("ClientHeight"))),
             new(Dfm(Ident("Position"), VaIdent, Str("poDesktopCenter")),
                 Dfm(Ident("Position"), VaIdent, Str("poScreenCenter")))]),

        // Het menu zet het bureaublad met ChangeDisplaySettings op 800x600; andere monitoren schuiven dan op en
        // alt-tab gaat mis. Met de verborgen instelling settings\menu.ini [GENERAL] Windowed=1 slaat het menu die
        // wissel over en tekent het zich gecentreerd op het hoofdscherm. Fix: na die controle (call, test al,al,
        // jne) altijd overslaan (jne -> jmp), ongeacht menu.ini.
        new("menu-no-modeswitch", "RcHcMenu.dat",
            "Menu laat de schermresolutie ongemoeid (geen wissel naar 800x600)",
            [new(Hex("e8 07 f5 ff ff 84 c0 75 05 e8 2a 27 f4 ff"),
                 Hex("e8 07 f5 ff ff 84 c0 eb 05 e8 2a 27 f4 ff"))]),

        // RedCat.exe wacht op menu en spel zonder berichten te verwerken; het spelvenster blijft dan soms achter.
        // Fix: eigen wachtlus die berichten verwerkt en het spelvenster naar voren haalt (zie GameForeground).
        GameForeground.Patch,

        // Het spelvenster registreert zijn klasse met LoadIconA(NULL, IDI_APPLICATION): het standaardicoon van
        // Windows, terwijl RcHcGame.dat een eigen icoon heeft (groep 107). In de taakbalk staat dan een leeg
        // venstertje. Fix: LoadIconA(hInstance, 107); edi is daar hInstance (ook het veld hInstance van WNDCLASS).
        // "push 7F00h; push ebx" (6 bytes) wordt "push 107; push edi; 3x nop".
        new("game-icon", "RcHcGame.dat",
            "Spelvenster heeft het RedCat-icoon in de taakbalk",
            [new(Hex("68 00 7f 00 00 53 89 46 50 ff 15 28 a2 64 00"),
                 Hex("6a 6b 57 90 90 90 89 46 50 ff 15 28 a2 64 00"))],
            Xp2004:
            [new(Hex("68 00 7f 00 00 53 89 46 50 ff 15 3c 37 8a 00"),
                 Hex("6a 6b 57 90 90 90 89 46 50 ff 15 3c 37 8a 00"))]),

        // RedCat.exe zet een zwart schermvullend venster met een eigen taakbalkknop (WS_EX_APPWINDOW) achter menu en
        // spel. Het grote en kleine icoon komen uit LoadIconA(hInstance, 7F00h), een id dat niet in het bestand
        // staat: geen icoon. Fix: id 101, het icoon van RedCat.exe (esi is hInstance).
        new("redcat-icon", "RedCat.exe",
            "Achtergrondvenster van RedCat.exe heeft het RedCat-icoon in de taakbalk",
            [new(Hex("68 00 7f 00 00 56 89 44 24 30 ff d7 68 00 7f 00 00 56 89 44 24 2c ff d7"),
                 Hex("6a 65 90 90 90 56 89 44 24 30 ff d7 6a 65 90 90 90 56 89 44 24 2c ff d7"))]),
    ];

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    // Delphi binaire DFM: property-naam en strings zijn length-prefixed; 0x06 = vaString, 0x07 = vaIdent.
    private const byte VaString = 0x06, VaIdent = 0x07;
    private static byte[] Str(string s) => [(byte)s.Length, .. Encoding.Latin1.GetBytes(s)];
    private static byte[] Ident(string s) => Str(s);
    private static byte[] Dfm(params object[] parts) =>
        parts.SelectMany(p => p switch { byte b => new[] { b }, byte[] a => a, _ => throw new ArgumentException() }).ToArray();
}

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RedCatPatcher.Patching;

/// <summary>
/// Optioneel: breedbeeld (16:9 of 16:10) in plaats van 4:3.
/// Het spel kiest zelf een Glide-modus (maximaal 640x480) en rekent het beeld en het HUD uit voor 4:3.
/// Achtergrond: zie docs/TECHNISCH.md, "Optie --widescreen".
/// </summary>
public sealed record Widescreen(string Name, int Width, int Height, int GlideResolution)
{
    // Glide 2 kent maar een paar vaste resoluties; dit zijn de breedbeeldvarianten (GR_RESOLUTION_*).
    public static readonly Widescreen Wide16x9 = new("16:9", 856, 480, 0x0A);
    public static readonly Widescreen Wide16x10 = new("16:10", 640, 400, 0x06);

    public static Widescreen? Parse(string value) => value switch
    {
        "16:9" or "16x9" => Wide16x9,
        "16:10" or "16x10" => Wide16x10,
        _ => null,
    };

    private const float OriginalFov = 1.36f;      // horizontaal, in radialen, voor 4:3
    private const int HudVirtualWidth = 640;      // HUDIcon.ini rekent in 640x480

    private float Aspect => (float)Width / Height;

    /// <summary>Horizontaal FOV met dezelfde verticale kijkhoek als het origineel (Hor+).</summary>
    public float FieldOfView => OriginalFov * Aspect / (4f / 3f);

    /// <summary>Breedte van het HUD in 640x480-eenheden, zodat iconen niet worden uitgerekt (856 of 768).</summary>
    public int HudWidth => (int)MathF.Round(480 * Aspect);

    public IReadOnlyList<BinaryPatch> Patches =>
    [
        // De Genesis3D Glide-driver biedt 512x384, 640x480, 800x600 en 1024x768 aan. 512x384 wordt de
        // breedbeeldmodus: in de lijst met modi (naam + breedte/hoogte) en in GLIDE_DrvInit (grSstWinOpen).
        new("widescreen-glide-mode", "GlideDrv.dll",
            $"Glide-driver biedt {Width}x{Height} aan",
            [new(Hex("68 80 01 00 00 68 00 02 00 00 68 fc e2 00 10 6a 00"),
                 [0x68, .. Int(Height), 0x68, .. Int(Width), .. Hex("68 fc e2 00 10 6a 00")]),
             new([.. "512x384"u8, 0], ModeName(8)),
             new(Hex("c7 05 68 55 1f 10 00 02 00 00 c7 05 6c 55 1f 10 80 01 00 00 bf 03 00 00 00"),
                 [.. Hex("c7 05 68 55 1f 10"), .. Int(Width), .. Hex("c7 05 6c 55 1f 10"), .. Int(Height), 0xbf, .. Int(GlideResolution)])]),

        // CAdamEngine::SelectDevice() zoekt automatisch naar modus "640x480" (of "320x200").
        // Die zoekterm wordt de breedbeeldmodus; de string staat in een tabel naast "640x400".
        // In de XP-build zijn de strings in die tabel tot 12 bytes opgevuld.
        new("widescreen-select-mode", "RcHcGame.dat",
            $"Spel kiest automatisch {Width}x{Height}",
            [new([0, .. "640x480"u8, 0, .. "640x400"u8], [0, .. ModeName(8), .. "640x400"u8])],
            Xp2004:
            [new([0, .. "640x480"u8, 0, 0, 0, 0, 0, .. "640x400"u8], [0, .. ModeName(12), .. "640x400"u8])]),

        // CAdamCamera::Init() zet FieldOfView na het aanmaken van de camera terug naar 1.36 als de INI iets anders
        // zegt ("INI setting FieldOfView is ignored"). Die jne wordt een jmp, zodat [Camera-*] FieldOfView telt.
        new("widescreen-fov", "RcHcGame.dat",
            "FieldOfView uit AdamWin.ini wordt gebruikt",
            [new(Hex("d8 1d 3c f8 64 00 df e0 f6 c4 40 0f 85 9c 00 00 00"),
                 Hex("d8 1d 3c f8 64 00 df e0 f6 c4 40 e9 9d 00 00 00 90"))],
            // XP-build: dezelfde vergelijking, maar met test ah,44h / jnp
            Xp2004:
            [new(Hex("d8 1d 78 2c 77 00 df e0 f6 c4 44 0f 8b 9c 00 00 00"),
                 Hex("d8 1d 78 2c 77 00 df e0 f6 c4 44 e9 9d 00 00 00 90"))]),

        // RCHud rekent iconposities om naar schermpercentages: x/640 en y/480. Op een breed scherm worden de
        // iconen dan uitgerekt. x gaat nu door de HUD-breedte (bijv. 856), die in een ongebruikte kopie van de
        // constante 1/480 (0x64EB38) komt te staan. Iconen rechts schuiven op in HUDIcon.ini.
        new("widescreen-hud", "RcHcGame.dat",
            "HUD-iconen niet uitgerekt",
            [new(Hex("db 00 d8 0d 98 d3 64 00 d9 5c 24 08 db 40 04 d8 0d 98 d3 64 00"),
                 Hex("db 00 d8 0d 38 eb 64 00 d9 5c 24 08 db 40 04 d8 0d 38 eb 64 00")),
             new(Hex("89 88 08 3b 89 88 08 3b a8 88 65 00"),
                 [.. Float(1f / HudWidth), .. Hex("89 88 08 3b a8 88 65 00")])],
            // XP-build: 1/640 staat op 0x76FC0C; de ongebruikte kopie van 1/480 op 0x771A40 (na 255.0f).
            Xp2004:
            [new(Hex("db 00 d8 0d 0c fc 76 00 d9 5c 24 08 db 40 04 d8 0d 0c fc 76 00"),
                 Hex("db 00 d8 0d 40 1a 77 00 d9 5c 24 08 db 40 04 d8 0d 40 1a 77 00")),
             new(Hex("00 00 7f 43 89 88 08 3b 89 88 08 3b"),
                 [.. Hex("00 00 7f 43"), .. Float(1f / HudWidth), .. Hex("89 88 08 3b")])]),
    ];

    /// <summary>INI-aanpassingen: FOV van de spelcamera's en de positie van de HUD-iconen rechts.</summary>
    public void ApplySettings(string gameDir) => ApplySettings(gameDir, this);

    /// <summary>
    /// Zet de INI-waarden voor <paramref name="target"/> (null = 4:3, het origineel). Herhaalbaar en omkeerbaar: de
    /// huidige stand volgt uit <c>FieldOfView</c> in AdamWin.ini, en de iconen rechts schuiven vanaf hun originele
    /// positie, niet vanaf de huidige. Zo kan 16:9 naar 16:10 of terug naar 4:3 zonder de bestanden van de disc.
    /// </summary>
    public static void ApplySettings(string gameDir, Widescreen? target)
    {
        var adamWin = Path.Combine(gameDir, "AdamWin.ini");
        var current = Current(IniFile.Get(adamWin, "Camera-LevelCamera", "FieldOfView"));

        var fov = (target?.FieldOfView ?? OriginalFov).ToString("0.000000", CultureInfo.InvariantCulture);
        IniFile.Set(adamWin, "Camera-LevelCamera", "FieldOfView", fov);
        IniFile.Set(adamWin, "Camera-PlayerCamera", "FieldOfView", fov);

        // Iconen in de rechterhelft (score, toverdrank) blijven rechts uitgelijnd. Een verschoven icoon staat op
        // origineel + verschuiving, met origineel >= 320; een icoon links is nooit verschoven en blijft onder 320.
        int from = Shift(current), to = Shift(target);
        IniFile.Update(Path.Combine(gameDir, "settings", "HUDIcon.ini"),
            section => Regex.IsMatch(section, @"^Icon\d+$", RegexOptions.IgnoreCase),
            "X",
            x => int.TryParse(x.Trim(), out int v) && v - from >= HudVirtualWidth / 2 ? (v - from + to).ToString(CultureInfo.InvariantCulture) : x);
    }

    private static int Shift(Widescreen? wide) => wide == null ? 0 : wide.HudWidth - HudVirtualWidth;

    /// <summary>Welke beeldverhouding de INI's nu hebben, af te lezen aan het FOV (null = 4:3).</summary>
    private static Widescreen? Current(string fieldOfView)
    {
        if (!float.TryParse(fieldOfView.Trim(), CultureInfo.InvariantCulture, out float fov))
            throw new PatchException($"FieldOfView '{fieldOfView.Trim()}' in AdamWin.ini is geen getal.");
        foreach (var wide in new Widescreen?[] { null, Wide16x9, Wide16x10 })
            if (MathF.Abs(fov - (wide?.FieldOfView ?? OriginalFov)) < 0.0005f) return wide;
        throw new PatchException($"FieldOfView {fieldOfView.Trim()} in AdamWin.ini hoort niet bij 4:3, 16:9 of 16:10; " +
            "zet het terug op 1.360000 of patch opnieuw vanaf de disc.");
    }

    private byte[] ModeName(int length) => Encoding.ASCII.GetBytes($"{Width}x{Height}".PadRight(length, '\0'));

    private static byte[] Int(int value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        return b;
    }

    private static byte[] Float(float value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(b, value);
        return b;
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));
}

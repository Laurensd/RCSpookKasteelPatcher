using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RedCatPatcher.Patching;

/// <summary>
/// Installeert dgVoodoo2 (DirectDraw/Direct3D/Glide-wrapper) in de spelmap.
/// De zip wordt van de officiële GitHub-release gehaald en tegen een vaste hash gecontroleerd.
/// </summary>
public static class DgVoodoo
{
    public const string Version = "2.87.5";
    public const string DownloadUrl = "https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.87.5/dgVoodoo2_87_5.zip";
    public const string Sha256 = "5FFDE6927F7355CA3FDD5D785B581256A8E6539FA13E395A891ADE6BA1040850";

    // zip-pad -> doelnaam in de spelmap (alleen de 32-bit DLL's; het spel is 32-bit)
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MS/x86/DDraw.dll"] = "DDraw.dll",
        ["MS/x86/D3DImm.dll"] = "D3DImm.dll",
        ["3Dfx/x86/Glide.dll"] = "Glide.dll",
        ["3Dfx/x86/Glide2x.dll"] = "Glide2x.dll",
        ["3Dfx/x86/Glide3x.dll"] = "Glide3x.dll",
        ["dgVoodooCpl.exe"] = "dgVoodooCpl.exe",
    };

    /// <summary>
    /// Standaard interne Glide-resolutie: de grootste resolutie met de beeldverhouding van het spel die op het
    /// bureaublad past (1080p-scherm: 640x480 wordt 1440x1080). Het spel tekent dan direct op schermresolutie
    /// in plaats van 640x480 op te schalen, dus scherper.
    /// </summary>
    public const string DefaultResolution = "max";

    // Aanpassingen op de standaard dgVoodoo.conf (sectie, sleutel, waarde). Sectie is nodig omdat sommige
    // sleutels (zoals Resolution) in meerdere secties voorkomen.
    private static readonly (string Section, string Key, string Value)[] Settings =
    [
        ("DirectX", "VRAM", "128"),                   // oude driver rekent >2 GB videogeheugen negatief
        ("DirectX", "dgVoodooWatermark", "false"),
        ("Glide", "3DfxWatermark", "false"),
        ("General", "CenterAppWindow", "true"),       // venstermodus: spel zet zijn venster deels boven het hoofdscherm
    ];

    /// <summary>
    /// Controleert een waarde voor <c>--resolution</c>: een dgVoodoo-sleutelwoord ("unforced", "max", "desktop",
    /// "2x", "max_2x", "max_isf", ...) of een vaste resolutie ("1920x1080", "1920x1080@60").
    /// </summary>
    public static bool IsValidResolution(string value) =>
        Regex.IsMatch(value,
            @"^(unforced|(max(_isf)?|max_fhd(_isf)?|max_qhd(_isf)?|desktop)(_[1-9]\d?x)?|[1-9]\d?x|\d{3,5}x\d{3,5}(@\d{2,3})?)$");

    /// <summary>Waarden voor <c>--msaa</c>, zoals dgVoodoo ze kent (<c>[Glide] Antialiasing</c>).</summary>
    public static readonly string[] AntialiasingValues = ["off", "2x", "4x", "8x", "16x"];

    public static async Task<byte[]> GetZipAsync(string? localZip, Action<string> log)
    {
        byte[] zip;
        if (localZip != null)
        {
            log($"dgVoodoo2 uit {Path.GetFileName(localZip)}");
            zip = await File.ReadAllBytesAsync(localZip);
        }
        else
        {
            log($"dgVoodoo2 {Version} downloaden van GitHub...");
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RedCatPatcher");
            zip = await http.GetByteArrayAsync(DownloadUrl);
        }
        var hash = Convert.ToHexString(SHA256.HashData(zip));
        if (!hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new PatchException($"dgVoodoo2-zip heeft een onverwachte hash ({hash}). Gebruik versie {Version}.");
        return zip;
    }

    public static void Install(byte[] zipBytes, string gameDir, string glideResolution) =>
        Install(zipBytes, gameDir, new DisplayOptions { Resolution = glideResolution });

    public static void Install(byte[] zipBytes, string gameDir, DisplayOptions? display = null)
    {
        display ??= new DisplayOptions();
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var (source, target) in Files)
        {
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(source, StringComparison.OrdinalIgnoreCase))
                ?? throw new PatchException($"{source} ontbreekt in de dgVoodoo2-zip.");
            entry.ExtractToFile(Path.Combine(gameDir, target), overwrite: true);
        }

        var confEntry = zip.GetEntry("dgVoodoo.conf") ?? throw new PatchException("dgVoodoo.conf ontbreekt in de zip.");
        var confPath = Path.Combine(gameDir, "dgVoodoo.conf");
        confEntry.ExtractToFile(confPath, overwrite: true);
        Set(confPath, Settings);
        ApplyDisplay(gameDir, display);
    }

    /// <summary>
    /// Zet alleen de beeldsleutels van <paramref name="display"/> in een bestaande dgVoodoo.conf; de rest (ook wat via
    /// dgVoodooCpl.exe is aangepast) blijft staan. Geeft false zonder dgVoodoo.conf (gepatcht met --no-dgvoodoo).
    /// </summary>
    public static bool ApplyDisplay(string gameDir, DisplayOptions display)
    {
        var confPath = Path.Combine(gameDir, "dgVoodoo.conf");
        if (!File.Exists(confPath)) return false;
        Set(confPath, display.Settings);
        return true;
    }

    private static void Set(string confPath, IEnumerable<(string Section, string Key, string Value)> settings)
    {
        foreach (var (section, key, value) in settings)
            IniFile.Update(confPath, s => s.Equals(section, StringComparison.OrdinalIgnoreCase), key, _ => " " + value, firstSectionOnly: true);
    }
}

/// <summary>Hoe het spel op het scherm komt (<c>--display</c>).</summary>
public enum DisplayMode
{
    /// <summary>Randloos venster op schermgrootte ("fake fullscreen"); standaard, werkt goed met alt-tab en Proton.</summary>
    Borderless,
    /// <summary>Echt (exclusief) volledig scherm.</summary>
    Fullscreen,
    /// <summary>Venster met rand, standaard 2x de spelresolutie.</summary>
    Window,
}

/// <summary>Beeldinstellingen die de patcher in dgVoodoo.conf zet. Achtergrond: docs/TECHNISCH.md, "Beeldopties".</summary>
public sealed record DisplayOptions
{
    /// <summary>Renderresolutie in een venster: 2x (1280x960, of 1712x960 met 16:9) past op een 1080p-scherm.</summary>
    public const string DefaultWindowResolution = "2x";
    public const string DefaultAntialiasing = "4x";

    public DisplayMode Mode { get; init; } = DisplayMode.Borderless;
    /// <summary><c>[Glide] Resolution</c>; null = standaard voor <see cref="Mode"/>.</summary>
    public string? Resolution { get; init; }
    /// <summary>Maximaal aantal beelden per seconde; 0 = geen limiet.</summary>
    public int FpsLimit { get; init; }
    public bool VSync { get; init; } = true;
    /// <summary>MSAA: "off", "2x", "4x", "8x" of "16x".</summary>
    public string Antialiasing { get; init; } = DefaultAntialiasing;
    /// <summary>CRT-effect (scanlines, kromming) van dgVoodoo.</summary>
    public bool Crt { get; init; }

    public string GlideResolution => Resolution ?? (Mode == DisplayMode.Window ? DefaultWindowResolution : DgVoodoo.DefaultResolution);

    public static DisplayMode? ParseMode(string value) => value.ToLowerInvariant() switch
    {
        "borderless" or "randloos" => DisplayMode.Borderless,
        "fullscreen" or "exclusive" => DisplayMode.Fullscreen,
        "window" or "windowed" or "venster" => DisplayMode.Window,
        _ => null,
    };

    internal IEnumerable<(string Section, string Key, string Value)> Settings =>
    [
        ("General", "FullScreenMode", Mode == DisplayMode.Window ? "false" : "true"),
        ("GeneralExt", "FullscreenAttributes", Mode == DisplayMode.Fullscreen ? "" : "fake"), // ook alt-enter vanuit een venster wordt randloos
        ("General", "ScalingMode", Crt ? "stretched_ar_crt" : "stretched_ar"), // beeldvullend met behoud van de beeldverhouding
        ("GeneralExt", "FPSLimit", FpsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("Glide", "ForceVerticalSync", VSync ? "true" : "false"),
        ("Glide", "Antialiasing", Antialiasing),
        // Alleen [Glide] Resolution: het spel tekent via Glide. [DirectX] Resolution (menu's via DirectDraw) blijft unforced.
        ("Glide", "Resolution", GlideResolution),
    ];
}

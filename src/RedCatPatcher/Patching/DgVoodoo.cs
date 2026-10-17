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
        ("General", "ScalingMode", "stretched_ar"),   // beeldvullend met behoud van de beeldverhouding
        ("General", "CenterAppWindow", "true"),       // venstermodus: spel zet zijn venster deels boven het hoofdscherm
        ("GeneralExt", "FullscreenAttributes", "fake"), // randloos venster op schermgrootte (werkt goed met alt-tab en Proton)
    ];

    /// <summary>
    /// Controleert een waarde voor <c>--resolution</c>: een dgVoodoo-sleutelwoord ("unforced", "max", "desktop",
    /// "2x", "max_2x", "max_isf", ...) of een vaste resolutie ("1920x1080", "1920x1080@60").
    /// </summary>
    public static bool IsValidResolution(string value) =>
        Regex.IsMatch(value,
            @"^(unforced|(max(_isf)?|max_fhd(_isf)?|max_qhd(_isf)?|desktop)(_[1-9]\d?x)?|[1-9]\d?x|\d{3,5}x\d{3,5}(@\d{2,3})?)$");

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

    public static void Install(byte[] zipBytes, string gameDir, string glideResolution = DefaultResolution)
    {
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
        // Alleen [Glide] Resolution: het spel tekent via Glide. [DirectX] Resolution (menu's via DirectDraw) blijft unforced.
        foreach (var (section, key, value) in Settings.Append(("Glide", "Resolution", glideResolution)))
            IniFile.Update(confPath, s => s.Equals(section, StringComparison.OrdinalIgnoreCase), key, _ => " " + value, firstSectionOnly: true);
    }
}

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

    // Aanpassingen op de standaard dgVoodoo.conf (sleutel -> waarde)
    private static readonly (string Key, string Value)[] Settings =
    [
        ("VRAM", "128"),                     // oude driver rekent >2 GB videogeheugen negatief
        ("dgVoodooWatermark", "false"),
        ("3DfxWatermark", "false"),
        ("ScalingMode", "stretched_ar"),     // beeldvullend met behoud van 4:3
        ("FullscreenAttributes", "fake"),    // randloos venster op schermgrootte (werkt goed met alt-tab en Proton)
    ];

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

    public static void Install(byte[] zipBytes, string gameDir)
    {
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var (source, target) in Files)
        {
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(source, StringComparison.OrdinalIgnoreCase))
                ?? throw new PatchException($"{source} ontbreekt in de dgVoodoo2-zip.");
            entry.ExtractToFile(Path.Combine(gameDir, target), overwrite: true);
        }

        var confEntry = zip.GetEntry("dgVoodoo.conf") ?? throw new PatchException("dgVoodoo.conf ontbreekt in de zip.");
        string conf;
        using (var reader = new StreamReader(confEntry.Open())) conf = reader.ReadToEnd();
        foreach (var (key, value) in Settings)
        {
            var re = new Regex($"^({Regex.Escape(key)}\\s*=)[^\\r\\n]*", RegexOptions.Multiline);
            if (!re.IsMatch(conf)) throw new PatchException($"Instelling '{key}' niet gevonden in dgVoodoo.conf.");
            conf = re.Replace(conf, $"$1 {value}");
        }
        File.WriteAllText(Path.Combine(gameDir, "dgVoodoo.conf"), conf);
    }
}

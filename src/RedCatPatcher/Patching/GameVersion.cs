using System.Security.Cryptography;

namespace RedCatPatcher.Patching;

/// <summary>Welke build van RcHcGame.dat de binaire patches moeten verwachten.</summary>
public enum ExeBuild
{
    /// <summary>De build van de cd-rom (2000) en van de Win98-update (2001): dezelfde code-adressen.</summary>
    Original,

    /// <summary>De build uit de XP-update (2004): opnieuw gecompileerd, alles staat op andere adressen.</summary>
    Xp2004,
}

/// <summary>
/// Herkent de spelversie aan de SHA-256 van de bestanden die de patcher aanpast. Een onbekende versie wordt niet
/// gepatcht maar gemeld, zodat de patcher nooit half werk aflevert op een bestand dat hij niet kent.
/// </summary>
public sealed record GameVersion(string Name, ExeBuild Build)
{
    public const string IssuesUrl = "https://github.com/Laurensd/RCSpookKasteelPatcher/issues";

    public static readonly GameVersion V100 = new("1.0.0 (cd-rom, oktober 2000)", ExeBuild.Original);
    public static readonly GameVersion V101 = new("1.0.1 (Win98-update, juni 2001)", ExeBuild.Original);
    public static readonly GameVersion Xp = new("XP-update (december 2004)", ExeBuild.Xp2004);

    private static readonly Dictionary<string, GameVersion> GameExe = new()
    {
        ["D2F651C80CCF9ACC17463365E3F6F8FF2B6052CD77B7B93C59A3DF8BE7F4BD74"] = V100,
        ["E30781FCDC665D1F217C1A3353761C96E1EC3566F1AD472BB1A8A499CB29DBA5"] = V101,
        ["5262A6B30E0201BFB7FD581DE95E9954FB6CDF4D4A9B26CB4CE62294FAB0EF11"] = Xp,
    };

    /// <summary>Bestanden die de updates niet aanraken: daarvan bestaat één versie.</summary>
    private static readonly (string File, string Sha256)[] Unchanged =
    [
        ("RcHcMenu.dat", "C859320E2CB5BE2DB8E1C344A15EE388A7BEE9728E10F09B89B7E1111485698F"),
        ("GlideDrv.dll", "86EA51AEFA315E87AD847EFBC58D90A8738FBC6D710B52A1315AC42420193811"),
    ];

    public static GameVersion Detect(string gameDir)
    {
        var (exeData, exeHash) = Read(gameDir, "RcHcGame.dat");
        if (!GameExe.TryGetValue(exeHash, out var version))
            throw Unsupported("RcHcGame.dat", exeData.Length, exeHash);
        foreach (var (file, expected) in Unchanged)
        {
            var (data, hash) = Read(gameDir, file);
            if (hash != expected) throw Unsupported(file, data.Length, hash);
        }
        return version;
    }

    private static (byte[] Data, string Sha256) Read(string gameDir, string file)
    {
        var path = Path.Combine(gameDir, file);
        if (!File.Exists(path)) throw new PatchException($"{file} niet gevonden in de spelmap.");
        var data = File.ReadAllBytes(path);
        return (data, Convert.ToHexString(SHA256.HashData(data)));
    }

    private static PatchException Unsupported(string file, int size, string sha256) => new(
        $"{file} is een versie die de patcher niet kent ({size} bytes, SHA-256 {sha256}).\n" +
        "      Ondersteund: RedCat Spookkasteel 1.0.0 (cd-rom), met of zonder de officiële Win98- en XP-update.\n" +
        $"      Er is niets gepatcht. Meld deze versie op {IssuesUrl} en vermeld de gegevens hierboven.");
}

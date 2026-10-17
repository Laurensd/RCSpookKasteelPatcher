using System.Security.Cryptography;
using System.Text;

namespace RedCatPatcher.Patching;

/// <summary>
/// De basis: een kopie van de bestanden die fixes en opties aanpassen, zoals ze na de officiële updates en vóór de
/// fixes waren. Een keuze wordt altijd vanaf de basis opgebouwd (basis → fixes → logs → breedbeeld → joysticktabel),
/// zodat elke patch orig → nieuw draait en elke keuze herhaalbaar en terug te draaien is.
/// Achtergrond: zie docs/LAUNCHER.md, "Herhaalbaar toepassen en terugdraaien".
/// </summary>
public static class Basis
{
    /// <summary>Map in de spelmap.</summary>
    public static readonly string DirName = Path.Combine("RedCatPatcher", "basis");

    /// <summary>De binaire bestanden die opnieuw opgebouwd worden.</summary>
    public static readonly string[] BinaryFiles = ["RcHcGame.dat", "RcHcMenu.dat", "GlideDrv.dll"];

    /// <summary>INI's waarvan de launcher de originele waarden nodig heeft (FOV, toetsen, iconposities).</summary>
    public static readonly string[] IniFiles =
    [
        "AdamWin.ini",
        Path.Combine("settings", "static.ini"),
        Path.Combine("settings", "PlayerDef.ini"),
        Path.Combine("settings", "HUDIcon.ini"),
    ];

    public static string DirOf(string gameDir) => Path.Combine(gameDir, DirName);

    /// <summary>Kopieert de bestanden naar de basis en geeft de hash van de basis terug. Vóór de fixes aanroepen.</summary>
    public static string Save(string gameDir)
    {
        var basisDir = DirOf(gameDir);
        foreach (var file in BinaryFiles.Concat(IniFiles))
        {
            var target = Path.Combine(basisDir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(gameDir, file), target, overwrite: true);
        }
        return Sha256(basisDir);
    }

    /// <summary>Eén hash over alle bestanden van de basis, ter controle bij het toepassen.</summary>
    public static string Sha256(string basisDir)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in BinaryFiles.Concat(IniFiles))
        {
            var path = Path.Combine(basisDir, file);
            if (!File.Exists(path)) throw new PatchException($"{file} ontbreekt in {DirName}. {RepatchHint}");
            hash.AppendData(Encoding.UTF8.GetBytes(file.Replace('\\', '/') + "\n"));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// Bouwt de drie binaire bestanden in het geheugen op uit de basis (bestandsnaam → inhoud). De basis zelf blijft
    /// ongewijzigd.
    /// </summary>
    public static Dictionary<string, byte[]> Build(IReadOnlyDictionary<string, byte[]> basis, GameVersion version, Choice choice)
    {
        var files = BinaryFiles.ToDictionary(f => f, f => basis.TryGetValue(f, out var data)
            ? (byte[])data.Clone() : throw new PatchException($"{f} ontbreekt in de basis."));

        IEnumerable<BinaryPatch> patches = GamePatches.All;
        if (!choice.DebugLogs) patches = patches.Concat(DebugLogs.PatchesFor(version));
        if (choice.Widescreen is { } wide) patches = patches.Concat(wide.Patches);
        foreach (var patch in patches)
            patch.Apply(files[patch.File], version.Build);

        // Zonder WASD en controller is de tabel die van de basis (Controller.Original).
        if (choice.Wasd || choice.Controller)
            Controller.PatchTable(files[Controller.File], Controller.Mapping(choice.Wasd, choice.Controller));
        return files;
    }

    /// <summary>
    /// Past <paramref name="choice"/> toe op de spelmap: de binaire bestanden opnieuw uit de basis, en alleen de
    /// INI-sleutels die bij de opties horen (wat de speler of het menu verder veranderde, blijft staan). Met
    /// <paramref name="expectedSha256"/> moet de basis nog dezelfde zijn als bij het patchen.
    /// </summary>
    public static GameVersion Apply(string gameDir, Choice choice, string? expectedSha256 = null) =>
        Apply(gameDir, choice, expectedSha256, GameVersion.Detect);

    /// <summary>Met een eigen versieherkenning, voor tests met nagemaakte bestanden.</summary>
    internal static GameVersion Apply(string gameDir, Choice choice, string? expectedSha256, Func<string, GameVersion> detect)
    {
        var basisDir = DirOf(gameDir);
        if (!Directory.Exists(basisDir))
            throw new PatchException($"Geen basis gevonden ({DirName}). {RepatchHint}");
        if (expectedSha256 != null && !Sha256(basisDir).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new PatchException($"De bestanden in {DirName} zijn veranderd sinds het patchen. {RepatchHint}");

        var version = detect(basisDir);
        var files = Build(BinaryFiles.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(basisDir, f))), version, choice);
        EnsureNotRunning(gameDir);

        // Eerst de INI's: breedbeeld controleert de huidige stand en stopt bij een onbekende waarde zonder iets te
        // veranderen. Daarna pas de binaire bestanden, via een tijdelijk bestand.
        Widescreen.ApplySettings(gameDir, choice.Widescreen);
        ModernControls.Apply(gameDir, choice.Wasd, basisDir);
        Controller.SetExtraBinds(gameDir, choice.Controller);

        foreach (var (file, data) in files)
            File.WriteAllBytes(Path.Combine(gameDir, file + ".tmp"), data);
        foreach (var file in files.Keys)
            File.Move(Path.Combine(gameDir, file + ".tmp"), Path.Combine(gameDir, file), overwrite: true);

        DgVoodoo.ApplyDisplay(gameDir, choice.Display);
        return version;
    }

    /// <summary>Op Windows staan de bestanden van een draaiend spel of menu op slot.</summary>
    private static void EnsureNotRunning(string gameDir)
    {
        foreach (var file in BinaryFiles)
        {
            try { using var _ = File.Open(Path.Combine(gameDir, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (FileNotFoundException) { throw new PatchException($"{file} niet gevonden in de spelmap."); }
            catch (IOException) { throw new PatchException($"{file} is in gebruik. Sluit het spel en probeer het opnieuw."); }
        }
    }

    private const string RepatchHint = "Patch het spel opnieuw met de disc (spelers en scores blijven bewaard).";
}

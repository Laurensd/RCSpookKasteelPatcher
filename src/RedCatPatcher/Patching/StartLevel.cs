using System.Diagnostics;

namespace RedCatPatcher.Patching;

/// <summary>
/// Testhulp: start direct een gekozen Griezelplek, zonder menu en zonder de eerdere levels te spelen.
/// Achtergrond: zie docs/TECHNISCH.md, "Direct naar een level".
/// </summary>
public static class StartLevel
{
    /// <summary>Griezelplekken in de volgorde van het menu (Game.ini [sublevel1_N]).</summary>
    public static readonly (string Name, string Map)[] Levels =
    [
        ("Het Bos", "lvl00a"),
        ("Het Kasteel", "lvl01a"),
        ("Het Kerkhof", "lvl02a"),
        ("De Grotten", "lvl03a"),
        ("De Toren", "lvl04a"),
    ];

    /// <summary>
    /// Speler die het spel bij een directe start gebruikt. Het menu kent spelers 1 t/m 10 en schrijft Player%d.ini
    /// vóór elk spel opnieuw; nummer 10 laat de eigen spelers (en hun voortgang) met rust.
    /// </summary>
    public const int TestPlayer = 10;

    /// <summary>
    /// Het menu geeft zijn keuze door via Game.ini [Run] en start dan "RcHcGame.dat -game". Dit doet hetzelfde,
    /// met een verse kopie van PlayerDef.ini (volle gezondheid, 3 levens, standaardtoetsen).
    /// </summary>
    public static void Prepare(string gameDir, int level)
    {
        if (level < 1 || level > Levels.Length)
            throw new PatchException($"Kies een level van 1 t/m {Levels.Length}.");
        if (!File.Exists(Path.Combine(gameDir, "RcHcGame.dat")))
            throw new PatchException($"Geen gepatcht spel gevonden in '{gameDir}'. Patch eerst, of geef de spelmap op met --out.");

        var settings = Path.Combine(gameDir, "settings");
        File.Copy(Path.Combine(settings, "PlayerDef.ini"), Path.Combine(settings, $"Player{TestPlayer}.ini"), overwrite: true);

        var game = Path.Combine(settings, "Game.ini");
        IniFile.Set(game, "Run", "PlayerNumber", TestPlayer.ToString());
        IniFile.Set(game, "Run", "StartLevelNumber", "1");
        IniFile.Set(game, "Run", "StartSubLevelNumber", level.ToString());
        IniFile.Set(game, "Run", "StartSubSubLevelNumber", "1");
        IniFile.Set(game, "Run", "Replay", "0");
    }

    /// <summary>
    /// Start het spel; alleen op Windows (onder Linux start je het zelf via Proton/Wine; de launcher draait daar zelf
    /// onder Proton en kan het wel).
    /// </summary>
    public static Process? Launch(string gameDir) =>
        Process.Start(new ProcessStartInfo(Path.Combine(gameDir, "RcHcGame.dat"), "-game")
        {
            WorkingDirectory = gameDir,
            UseShellExecute = false,
        });
}

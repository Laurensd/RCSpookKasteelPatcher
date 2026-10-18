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
    /// Vaardigheden (bitmasker: 1 schieten, 2 power shot, 4 superjump, 8 super shot) aan het begin van elke Griezelplek,
    /// zoals <c>StandardSkill</c> in Game.ini [sublevel1_N]. Alleen als Game.ini ze niet heeft.
    /// </summary>
    public static readonly int[] StandardSkills = [0, 1, 3, 7, 15];

    /// <summary>
    /// Het menu geeft zijn keuze door via Game.ini [Run] en start dan "RcHcGame.dat -game". Dit doet hetzelfde,
    /// met een verse kopie van PlayerDef.ini (volle gezondheid, 3 levens, standaardtoetsen). Met <paramref name="console"/>
    /// krijgt testspeler 10 ook de console (<c>[General] Console=1</c>, zie docs/TECHNISCH.md, "De console"): de
    /// <c>`</c>-toets opent hem, en <c>cheats on</c> geeft het cheatmenu in het Esc-menu.
    /// In een Avontuur (<c>GameType=0</c>) neemt het spel de vaardigheden uit <c>[Player] Skill</c>, die je in de levels
    /// verdient; PlayerDef.ini heeft 0, en zonder vaardigheid 1 kan RedCat niet schieten. Daarom krijgt testspeler 10
    /// de <c>StandardSkill</c> van de gekozen Griezelplek, zoals Snelspel die geeft (docs/TECHNISCH.md, "Vaardigheden").
    /// </summary>
    public static void Prepare(string gameDir, int level, bool console = false)
    {
        if (level < 1 || level > Levels.Length)
            throw new PatchException($"Kies een level van 1 t/m {Levels.Length}.");
        if (!File.Exists(Path.Combine(gameDir, "RcHcGame.dat")))
            throw new PatchException($"Geen gepatcht spel gevonden in '{gameDir}'. Patch eerst, of geef de spelmap op met --out.");

        var settings = Path.Combine(gameDir, "settings");
        var player = Path.Combine(settings, $"Player{TestPlayer}.ini");
        File.Copy(Path.Combine(settings, "PlayerDef.ini"), player, overwrite: true);
        if (console) IniFile.SetOrAdd(player, "General", "Console", "1");

        var game = Path.Combine(settings, "Game.ini");
        var skill = IniFile.ReadAll(game).GetValueOrDefault($"sublevel1_{level}")?.GetValueOrDefault("StandardSkill");
        if (!int.TryParse(skill, out _)) skill = StandardSkills[level - 1].ToString();
        IniFile.SetOrAdd(player, "Player", "Skill", skill!);

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

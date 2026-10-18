using System.Globalization;

namespace RedCatPatcher.Patching;

/// <summary>
/// Testhulp van de launcher (groep 3 in docs/LAUNCHER.md, tabblad Testing): direct naar een level, de console van
/// testspeler 10 en de <c>[Debug]</c>-schakelaars in AdamWin.ini. Staat in RedCatLauncher.ini <c>[Testen]</c>, los van
/// de keuze: de patcher schrijft het niet, dus opnieuw patchen zet het terug (net als AdamWin.ini zelf).
/// </summary>
public sealed record TestOptions
{
    /// <summary>
    /// De <c>[Debug]</c>-sleutels voor extra logregels. Alleen deze worden gezet, de andere blijven zoals ze zijn; ze
    /// gelden ook voor gewoon spelen en zetten de logbestanden aan (<see cref="Effective"/>).
    /// Achtergrond: zie docs/TECHNISCH.md, "Logbestanden" en "Testen zonder menu".
    /// </summary>
    public static readonly string[] DebugKeys = ["DebugDriver", "DebugLoading", "DebugPlayerPosition"];

    /// <summary>
    /// <c>DebugSecretCommands</c> (debugtoetsen en het cheatmenu) en <c>DebugUseConsole</c> (laadlog in beeld). AdamWin.ini
    /// geldt ook voor gewoon spelen, dus deze staan alleen aan bij een teststart (<see cref="StartLevel"/>); anders zet
    /// <see cref="Apply"/> ze terug. Zie docs/TECHNISCH.md, "Console, cheats en debugtoetsen".
    /// </summary>
    public const string SecretCommandsKey = "DebugSecretCommands", UseConsoleKey = "DebugUseConsole";

    public const string AdamWin = "AdamWin.ini";
    private const string DebugSection = "Debug", StartLevelKey = "StartLevel", ConsoleKey = "Console";

    /// <summary>null = Play start het spel via het menu; anders 1 t/m 5 (<see cref="Patching.StartLevel.Levels"/>).</summary>
    public int? StartLevel { get; init; }

    /// <summary>De sleutels uit <see cref="DebugKeys"/> die aan staan.</summary>
    public IReadOnlySet<string> Debug { get; init; } = new HashSet<string>();

    /// <summary>Console voor testspeler 10 (<c>Player10.ini [General] Console=1</c>); standaard aan, telt alleen met een startlevel.</summary>
    public bool Console { get; init; } = true;

    /// <summary><c>DebugSecretCommands=1</c> bij een teststart: debugtoetsen (F9 godmodus, F6 door muren, ...) en het cheatmenu.</summary>
    public bool SecretCommands { get; init; }

    /// <summary><c>DebugUseConsole=1</c> bij een teststart: de console staat open tijdens het laden.</summary>
    public bool LoadConsole { get; init; }

    public bool IsDefault => StartLevel == null && Debug.Count == 0 && Console && !SecretCommands && !LoadConsole;

    /// <summary>
    /// De debugtoetsen staan echt aan en botsen met de besturing van de keuze: met WASD zijn A (AI-info) en S (spel
    /// sneller) ook lopen, met de moderne controller zijn LB/RB F5/F6 (framerate-info, botsing uit).
    /// </summary>
    public bool KeysClashWith(Choice choice) => SecretCommands && StartLevel != null && (choice.Wasd || choice.Controller);

    /// <summary>Zet één schakelaar aan of uit; een onbekende sleutel wordt genegeerd.</summary>
    public TestOptions WithDebug(string key, bool on)
    {
        if (!DebugKeys.Contains(key)) return this;
        var debug = new HashSet<string>(Debug);
        if (on) debug.Add(key); else debug.Remove(key);
        return this with { Debug = debug };
    }

    /// <summary>
    /// De keuze die echt toegepast wordt: de sleutels uit <see cref="DebugKeys"/> zetten alleen meldingen in de
    /// logbestanden, dus zodra er een aan staat, gaan de logs ook aan. De console heeft de logbestanden niet nodig.
    /// </summary>
    public Choice Effective(Choice choice) => Debug.Count > 0 && !choice.DebugLogs ? choice with { DebugLogs = true } : choice;

    /// <summary>Wat <see cref="Apply"/> in <c>[Debug]</c> zet: aan of uit per sleutel.</summary>
    private IEnumerable<(string Key, bool On)> Switches() =>
        DebugKeys.Select(k => (k, Debug.Contains(k)))
            .Append((SecretCommandsKey, SecretCommands && StartLevel != null))
            .Append((UseConsoleKey, LoadConsole && StartLevel != null));

    /// <summary>
    /// Zet de sleutels uit <see cref="DebugKeys"/>, <see cref="SecretCommandsKey"/> en <see cref="UseConsoleKey"/> in
    /// AdamWin.ini: <c>1</c> als ze aan staan, anders de waarde uit de basis (<c>0</c> op de cd-rom). Alleen een waarde
    /// die anders is, wordt geschreven; een uitgezette sleutel die in het bestand ontbreekt, wordt overgeslagen;
    /// ontbreekt er een die aan moet, dan verandert er niets.
    /// </summary>
    public void Apply(string gameDir)
    {
        var path = Path.Combine(gameDir, AdamWin);
        var current = IniFile.ReadAll(path).GetValueOrDefault(DebugSection);
        var basis = IniFile.ReadAll(Path.Combine(Basis.DirOf(gameDir), AdamWin)).GetValueOrDefault(DebugSection);
        var changes = new List<(string Key, string Value)>();
        foreach (var (key, on) in Switches())
        {
            var value = on ? "1" : basis?.GetValueOrDefault(key) is { Length: > 0 } original ? original : "0";
            if (current == null || !current.TryGetValue(key, out var now))
            {
                if (on) throw new PatchException($"Instelling '{key}' niet gevonden in {AdamWin} [{DebugSection}].");
                continue;
            }
            if (now != value) changes.Add((key, value));
        }
        foreach (var (key, value) in changes) IniFile.Set(path, DebugSection, key, value);
    }

    private static string Flag(bool on) => on ? "1" : "0";

    internal IEnumerable<(string Key, string Value)> ToIni() =>
        new[] { (StartLevelKey, (StartLevel ?? 0).ToString(CultureInfo.InvariantCulture)), (ConsoleKey, Flag(Console)) }
            .Concat(DebugKeys.Select(k => (k, Flag(Debug.Contains(k)))))
            .Append((SecretCommandsKey, Flag(SecretCommands)))
            .Append((UseConsoleKey, Flag(LoadConsole)));

    /// <summary>
    /// Leest <c>[Testen]</c>. Een ontbrekende of onleesbare sleutel staat uit, behalve <c>Console</c>: die staat standaard
    /// aan (ook in een bestand van vóór die schakelaar).
    /// </summary>
    internal static TestOptions FromIni(IReadOnlyDictionary<string, string> ini)
    {
        bool On(string key) => ini.TryGetValue(key, out var v) && v is "1" or "true";
        return new()
        {
            StartLevel = ini.TryGetValue(StartLevelKey, out var level)
                && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out int nr)
                && nr >= 1 && nr <= Patching.StartLevel.Levels.Length ? nr : null,
            Debug = DebugKeys.Where(On).ToHashSet(),
            Console = !ini.TryGetValue(ConsoleKey, out var console) || console is not ("0" or "false"),
            SecretCommands = On(SecretCommandsKey),
            LoadConsole = On(UseConsoleKey),
        };
    }

    public bool Equals(TestOptions? other) => other != null && StartLevel == other.StartLevel && Debug.SetEquals(other.Debug)
        && Console == other.Console && SecretCommands == other.SecretCommands && LoadConsole == other.LoadConsole;

    public override int GetHashCode() => HashCode.Combine(StartLevel, Debug.Count, Console, SecretCommands, LoadConsole);
}

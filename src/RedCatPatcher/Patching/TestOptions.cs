using System.Globalization;

namespace RedCatPatcher.Patching;

/// <summary>
/// Testhulp van de launcher (groep 3 in docs/LAUNCHER.md, tabblad Testing): direct naar een level en de
/// <c>[Debug]</c>-schakelaars in AdamWin.ini. Staat in RedCatLauncher.ini <c>[Testen]</c>, los van de keuze: de patcher
/// schrijft het niet, dus opnieuw patchen zet het terug (net als AdamWin.ini zelf).
/// </summary>
public sealed record TestOptions
{
    /// <summary>
    /// De <c>[Debug]</c>-sleutels die de launcher beheert. Alleen deze worden gezet, de andere blijven zoals ze zijn.
    /// Een nieuwe schakelaar (bijv. uit het onderzoek naar DebugSecretCommands) komt hier en op het tabblad Testing.
    /// Achtergrond: zie docs/TECHNISCH.md, "Logbestanden" en "Testen zonder menu".
    /// </summary>
    public static readonly string[] DebugKeys = ["DebugDriver", "DebugLoading", "DebugPlayerPosition"];

    public const string AdamWin = "AdamWin.ini";
    private const string DebugSection = "Debug", StartLevelKey = "StartLevel";

    /// <summary>null = Play start het spel via het menu; anders 1 t/m 5 (<see cref="Patching.StartLevel.Levels"/>).</summary>
    public int? StartLevel { get; init; }

    /// <summary>De sleutels uit <see cref="DebugKeys"/> die aan staan.</summary>
    public IReadOnlySet<string> Debug { get; init; } = new HashSet<string>();

    public bool IsDefault => StartLevel == null && Debug.Count == 0;

    /// <summary>Zet één schakelaar aan of uit; een onbekende sleutel wordt genegeerd.</summary>
    public TestOptions WithDebug(string key, bool on)
    {
        if (!DebugKeys.Contains(key)) return this;
        var debug = new HashSet<string>(Debug);
        if (on) debug.Add(key); else debug.Remove(key);
        return this with { Debug = debug };
    }

    /// <summary>
    /// De keuze die echt toegepast wordt: de <c>[Debug]</c>-sleutels zetten alleen meldingen in de logbestanden, dus
    /// zodra er een aan staat, gaan de logs ook aan.
    /// </summary>
    public Choice Effective(Choice choice) => Debug.Count > 0 && !choice.DebugLogs ? choice with { DebugLogs = true } : choice;

    /// <summary>
    /// Zet de sleutels uit <see cref="DebugKeys"/> in AdamWin.ini: <c>1</c> als ze aan staan, anders de waarde uit de
    /// basis (<c>0</c> op de cd-rom). Alleen een waarde die anders is, wordt geschreven; een uitgezette sleutel die in
    /// het bestand ontbreekt, wordt overgeslagen; ontbreekt er een die aan moet, dan verandert er niets.
    /// </summary>
    public void Apply(string gameDir)
    {
        var path = Path.Combine(gameDir, AdamWin);
        var current = IniFile.ReadAll(path).GetValueOrDefault(DebugSection);
        var basis = IniFile.ReadAll(Path.Combine(Basis.DirOf(gameDir), AdamWin)).GetValueOrDefault(DebugSection);
        var changes = new List<(string Key, string Value)>();
        foreach (var key in DebugKeys)
        {
            bool on = Debug.Contains(key);
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

    internal IEnumerable<(string Key, string Value)> ToIni() =>
        DebugKeys.Select(k => (k, Debug.Contains(k) ? "1" : "0"))
            .Prepend((StartLevelKey, (StartLevel ?? 0).ToString(CultureInfo.InvariantCulture)));

    /// <summary>Leest <c>[Testen]</c>. Een ontbrekende of onleesbare sleutel staat uit.</summary>
    internal static TestOptions FromIni(IReadOnlyDictionary<string, string> ini) => new()
    {
        StartLevel = ini.TryGetValue(StartLevelKey, out var level)
            && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out int nr)
            && nr >= 1 && nr <= Patching.StartLevel.Levels.Length ? nr : null,
        Debug = DebugKeys.Where(k => ini.TryGetValue(k, out var v) && v is "1" or "true").ToHashSet(),
    };

    public bool Equals(TestOptions? other) => other != null && StartLevel == other.StartLevel && Debug.SetEquals(other.Debug);

    public override int GetHashCode() => HashCode.Combine(StartLevel, Debug.Count);
}

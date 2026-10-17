namespace RedCatPatcher.Patching;

/// <summary>
/// Optioneel: WASD in plaats van de pijltjestoetsen als standaardbesturing.
/// Achtergrond: zie docs/TECHNISCH.md, "Optie --wasd".
/// </summary>
public static class ModernControls
{
    /// <summary>
    /// Het menu schrijft vóór elk spel de toetsen van de speler naar Player%d.ini [KeyBinds]. Spelers zonder eigen
    /// toetsen (en de knop Standaard) krijgen de waarden uit static.ini [DEFAULTSETTINGS], als virtual-key-codes.
    /// </summary>
    private static readonly (string Key, string VirtualKey)[] MenuDefaults =
    [
        ("Up", "87"),       // W  (Vooruit / MoveForward)
        ("Down", "83"),     // S  (Achteruit / MoveBack)
        ("Left", "65"),     // A  (Links / TurnLeft; met Ontwijken/Alt: MoveLeft)
        ("Right", "68"),    // D  (Rechts / TurnRight; met Ontwijken/Alt: MoveRight)
    ];

    /// <summary>Sjabloon voor nieuwe Player%d.ini-bestanden; het spel zelf leest toetsnamen.</summary>
    private static readonly (string Key, string Name)[] PlayerDefBinds =
    [
        ("MoveForward", "W"),
        ("MoveBack", "S"),
        ("TurnLeft", "A"),
        ("TurnRight", "D"),
        ("MoveLeft", "Alt+A"),
        ("MoveRight", "Alt+D"),
    ];

    public const string Description = "WASD als standaardbesturing in plaats van de pijltjestoetsen";

    public static void Apply(string gameDir) => Apply(gameDir, wasd: true, basisDir: null);

    /// <summary>
    /// Zet de standaardtoetsen op WASD, of (<paramref name="wasd"/> false) terug op de waarden uit
    /// <paramref name="basisDir"/>. Alleen deze sleutels; de rest van static.ini en PlayerDef.ini blijft staan.
    /// </summary>
    public static void Apply(string gameDir, bool wasd, string? basisDir)
    {
        if (!wasd && basisDir == null) throw new ArgumentNullException(nameof(basisDir));
        foreach (var (file, section, keys) in new[]
        {
            ("static.ini", "DEFAULTSETTINGS", MenuDefaults),
            ("PlayerDef.ini", "KeyBinds", PlayerDefBinds),
        })
        {
            var path = Path.Combine(gameDir, "settings", file);
            foreach (var (key, value) in keys)
                IniFile.Set(path, section, key, wasd ? value : IniFile.Get(Path.Combine(basisDir!, "settings", file), section, key));
        }
    }
}

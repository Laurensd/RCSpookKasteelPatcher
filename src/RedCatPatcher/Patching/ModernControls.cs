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

    public static void Apply(string gameDir)
    {
        var settings = Path.Combine(gameDir, "settings");
        foreach (var (key, vk) in MenuDefaults)
            IniFile.Set(Path.Combine(settings, "static.ini"), "DEFAULTSETTINGS", key, vk);
        foreach (var (key, name) in PlayerDefBinds)
            IniFile.Set(Path.Combine(settings, "PlayerDef.ini"), "KeyBinds", key, name);
    }
}

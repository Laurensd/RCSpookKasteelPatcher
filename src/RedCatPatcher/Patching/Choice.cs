using System.Globalization;
using System.Text;

namespace RedCatPatcher.Patching;

/// <summary>
/// Wat een speler bij het starten kiest (groep 2 in docs/LAUNCHER.md): breedbeeld, besturing, controller, logs en de
/// beeldopties van dgVoodoo2. De patcher past dit toe met zijn opdrachtregelopties, de launcher met zijn eigen scherm;
/// beide via <see cref="Basis.Apply"/>.
/// </summary>
public sealed record Choice
{
    /// <summary>null = 4:3, het origineel.</summary>
    public Widescreen? Widescreen { get; init; }
    public bool Wasd { get; init; }
    public bool Controller { get; init; }
    public bool DebugLogs { get; init; }
    /// <summary>
    /// Alleen voor de launcher: de controller in de menu's als muis (het menu leest zelf geen joystick). Verandert geen
    /// spelbestanden.
    /// </summary>
    public bool MenuController { get; init; } = true;
    public DisplayOptions Display { get; init; } = new();

    /// <summary>Waarde in RedCatLauncher.ini voor <c>Resolution</c> als die van de weergave afhangt.</summary>
    public const string AutoResolution = "auto";

    internal IEnumerable<(string Key, string Value)> ToIni() =>
    [
        ("Widescreen", Widescreen?.Name ?? "4:3"),
        ("Wasd", Bool(Wasd)),
        ("Controller", Bool(Controller)),
        ("DebugLogs", Bool(DebugLogs)),
        ("Display", Display.Mode.ToString().ToLowerInvariant()),
        ("Resolution", Display.Resolution ?? AutoResolution),
        ("RenderScale", Display.RenderScale.ToString(CultureInfo.InvariantCulture)),
        ("FpsLimit", Display.FpsLimit.ToString(CultureInfo.InvariantCulture)),
        ("VSync", Bool(Display.VSync)),
        ("Msaa", Display.Antialiasing),
        ("Crt", Bool(Display.Crt)),
        ("MenuController", Bool(MenuController)),
    ];

    /// <summary>Leest een sectie uit RedCatLauncher.ini. Een ontbrekende of onleesbare sleutel krijgt de standaardwaarde.</summary>
    internal static Choice FromIni(IReadOnlyDictionary<string, string> ini)
    {
        string? Get(string key) => ini.TryGetValue(key, out var v) && v.Length > 0 ? v : null;
        bool Flag(string key, bool fallback) => Get(key) switch { "1" or "true" => true, "0" or "false" => false, _ => fallback };

        var defaults = new DisplayOptions();
        var resolution = Get("Resolution")?.ToLowerInvariant();
        var msaa = Get("Msaa")?.ToLowerInvariant();
        return new Choice
        {
            Widescreen = Get("Widescreen") is { } w ? Widescreen.Parse(w) : null,
            Wasd = Flag("Wasd", false),
            Controller = Flag("Controller", false),
            DebugLogs = Flag("DebugLogs", false),
            MenuController = Flag("MenuController", true),
            Display = new DisplayOptions
            {
                Mode = Get("Display") is { } m && DisplayOptions.ParseMode(m) is { } mode ? mode : defaults.Mode,
                Resolution = resolution != null && resolution != AutoResolution && DgVoodoo.IsValidResolution(resolution) ? resolution : null,
                RenderScale = int.TryParse(Get("RenderScale"), NumberStyles.None, CultureInfo.InvariantCulture, out int scale)
                    && scale is >= 1 and <= DisplayOptions.MaxRenderScale ? scale : defaults.RenderScale,
                FpsLimit = int.TryParse(Get("FpsLimit"), NumberStyles.None, CultureInfo.InvariantCulture, out int fps) && fps <= 1000 ? fps : defaults.FpsLimit,
                VSync = Flag("VSync", defaults.VSync),
                Antialiasing = msaa != null && DgVoodoo.AntialiasingValues.Contains(msaa) ? msaa : defaults.Antialiasing,
                Crt = Flag("Crt", defaults.Crt),
            },
        };
    }

    private static string Bool(bool value) => value ? "1" : "0";
}

/// <summary>
/// <c>RedCatLauncher.ini</c> in de spelmap: wat de speler wil (<c>[Keuze]</c>) en wat er nu in de bestanden staat
/// (<c>[Toegepast]</c>, met de hash van de basis). De patcher en de launcher schrijven het allebei. Alleen de launcher
/// schrijft <c>[Testen]</c> (<see cref="TestOptions"/>).
/// </summary>
public sealed record LauncherIni(Choice? Wanted, Choice? Applied, string? BasisSha256, int Revision = Basis.Revision)
{
    public const string FileName = "RedCatLauncher.ini";
    private const string WantedSection = "Keuze", AppliedSection = "Toegepast", TestingSection = "Testen",
        BasisKey = "BasisSha256", RevisionKey = "Revision";

    /// <summary>Het tabblad Testing van de launcher; alleen in het bestand als er iets aan staat.</summary>
    public TestOptions Testing { get; init; } = new();

    /// <summary>Het keuzebestand, of null als het er niet is (gepatcht met een oudere patcher).</summary>
    public static LauncherIni? Read(string gameDir)
    {
        var path = Path.Combine(gameDir, FileName);
        if (!File.Exists(path)) return null;
        var ini = IniFile.ReadAll(path);
        return new LauncherIni(
            ini.TryGetValue(WantedSection, out var wanted) ? Choice.FromIni(wanted) : null,
            ini.TryGetValue(AppliedSection, out var applied) ? Choice.FromIni(applied) : null,
            applied != null && applied.TryGetValue(BasisKey, out var sha) && sha.Length > 0 ? sha : null,
            // Zonder Revision: geschreven vóór revisie 2
            applied != null && applied.TryGetValue(RevisionKey, out var revision) && int.TryParse(revision, out int r) ? r : 1)
        {
            Testing = ini.TryGetValue(TestingSection, out var testing) ? TestOptions.FromIni(testing) : new(),
        };
    }

    public void Write(string gameDir)
    {
        var text = new StringBuilder();
        text.Append("; RedCat Spookkasteel: keuze van de launcher. Zie docs/LAUNCHER.md.\r\n");
        void Section(string name, IEnumerable<(string, string)>? values, string comment, params (string, string)[] extra)
        {
            if (values == null) return;
            text.Append($"\r\n; {comment}\r\n[{name}]\r\n");
            foreach (var (key, value) in values.Concat(extra)) text.Append($"{key}={value}\r\n");
        }
        Section(WantedSection, Wanted?.ToIni(), "Wat de speler wil (launcher of patcher)");
        Section(TestingSection, Testing.IsDefault ? null : Testing.ToIni(), "Tabblad Testing van de launcher; opnieuw patchen zet dit terug");
        Section(AppliedSection, Applied?.ToIni(), "Wat er nu in de spelbestanden staat; niet zelf aanpassen",
            [.. BasisSha256 == null ? [] : new[] { (BasisKey, BasisSha256) }, (RevisionKey, Revision.ToString(CultureInfo.InvariantCulture))]);

        var path = Path.Combine(gameDir, FileName);
        File.WriteAllText(path + ".tmp", text.ToString(), Encoding.Latin1);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

using RedCatPatcher;
using RedCatPatcher.Disc;
using RedCatPatcher.Patching;

const string GameFolderName = "RedCat Spookkasteel";

Options options;
try { options = Options.Parse(args); }
catch (PatchException ex) { Console.WriteLine($"FOUT: {ex.Message}"); return 2; }
if (options.ShowHelp)
{
    Console.WriteLine("""
        RedCat Spookkasteel patcher — maakt de cd-rom uit 2000 speelbaar op Windows 10/11 en Linux (Proton/Wine).

        Gebruik: RedCatPatcher [disc.cue|disc.bin|disc.iso] [opties]

          --out <map>                Installatiemap (standaard: "RedCat Spookkasteel" naast de patcher)
          --dgvoodoo-zip <zip>       Gebruik een lokaal gedownloade dgVoodoo2-zip (offline installeren)
          --no-dgvoodoo              dgVoodoo2 niet installeren
          --widescreen [16:9|16:10]  Breedbeeld in plaats van 4:3 (standaard 16:9)
          --wasd                     WASD als standaardbesturing in plaats van de pijltjestoetsen
          --resolution <waarde>      Interne renderresolutie van dgVoodoo2 (standaard "max": zo groot als het
                                     scherm toelaat). Bijv. 2x, 3x, desktop, 1920x1080, of unforced (origineel)
          --yes                      Niets vragen; bestaande installatiemap overschrijven

        Zonder disc-argument zoekt de patcher naar een .cue/.iso in zijn eigen map en submappen.
        """);
    return 0;
}

bool interactive = !options.Yes && !Console.IsInputRedirected;
try
{
    Console.WriteLine($"RedCat Spookkasteel patcher {typeof(Options).Assembly.GetName().Version?.ToString(3)}");
    Console.WriteLine();

    var discPath = options.Disc ?? FindDisc(interactive);
    var outDir = Path.GetFullPath(options.Out ?? Path.Combine(AppContext.BaseDirectory, GameFolderName));

    if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
    {
        if (interactive && !Ask($"De map '{outDir}' bestaat al. Overschrijven? (spelers en scores blijven bewaard) [j/N] "))
            return 1;
    }

    // 1. Disc uitlezen en de Data-map kopiëren
    Console.WriteLine($"Disc:  {discPath}");
    using (var disc = DiscImage.Open(discPath))
    {
        var iso = new Iso9660(disc);
        Console.WriteLine($"       {disc.Description}, volume '{iso.VolumeLabel}'");
        var data = iso.Find("Data") ?? throw new PatchException("Map 'Data' niet gevonden op de disc. Is dit de RedCat Spookkasteel cd-rom?");
        if (data.Children.All(c => !c.Name.Equals("RcHcGame.dat", StringComparison.OrdinalIgnoreCase)))
            throw new PatchException("RcHcGame.dat niet gevonden op de disc. Is dit de RedCat Spookkasteel cd-rom?");

        Console.WriteLine($"Doel:  {outDir}");
        var keep = BackupUserData(outDir);
        int count = 0;
        iso.ExtractDirectory(data, outDir, _ =>
        {
            if (++count % 25 == 0 && !Console.IsOutputRedirected) Console.Write($"\r       {count} bestanden gekopieerd...");
        });
        RestoreUserData(outDir, keep);
        Console.WriteLine($"{(Console.IsOutputRedirected ? "" : "\r")}       {count} bestanden gekopieerd.          ");
    }

    // 2. Spelbestanden patchen
    Console.WriteLine();
    Console.WriteLine("Fixes:");
    foreach (var patch in GamePatches.All)
    {
        var result = patch.Apply(outDir);
        Console.WriteLine($"  [{(result == PatchResult.Applied ? "ok" : "al toegepast")}] {patch.Description}");
    }

    // 3. Opties
    if (options.Widescreen != null || options.Wasd)
    {
        Console.WriteLine();
        Console.WriteLine("Opties:");
    }
    if (options.Widescreen is { } wide)
    {
        foreach (var patch in wide.Patches)
            patch.Apply(outDir);
        wide.ApplySettings(outDir);
        Console.WriteLine($"  [ok] Breedbeeld {wide.Name} ({wide.Width}x{wide.Height}, FOV {wide.FieldOfView:0.00}, HUD aangepast)");
    }
    if (options.Wasd)
    {
        ModernControls.Apply(outDir);
        Console.WriteLine($"  [ok] {ModernControls.Description}");
    }

    // 4. dgVoodoo2
    if (!options.NoDgVoodoo)
    {
        Console.WriteLine();
        var zip = await DgVoodoo.GetZipAsync(options.DgVoodooZip, msg => Console.WriteLine(msg));
        DgVoodoo.Install(zip, outDir, options.Resolution);
        Console.WriteLine($"  [ok] dgVoodoo2 geïnstalleerd (DirectX/Glide-wrapper, schermvullend {options.Widescreen?.Name ?? "4:3"}, renderresolutie {options.Resolution})");
    }

    Console.WriteLine();
    Console.WriteLine("Klaar! Start het spel met:");
    Console.WriteLine($"  {Path.Combine(outDir, "RedCat.exe")}");
    if (!OperatingSystem.IsWindows())
        Console.WriteLine("  (Linux/Steam Deck: voeg RedCat.exe toe als niet-Steam-game en kies Proton als compatibiliteitstool.)");
    Pause(interactive);
    return 0;
}
catch (Exception ex) when (ex is PatchException or IOException or HttpRequestException or UnauthorizedAccessException)
{
    Console.WriteLine();
    Console.WriteLine($"FOUT: {ex.Message}");
    Pause(interactive);
    return 2;
}

static string FindDisc(bool interactive)
{
    var roots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Distinct();
    var found = roots
        .SelectMany(r => SafeEnumerate(r, depth: 2))
        .Where(f => f.EndsWith(".cue", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath).Distinct().ToList();

    if (found.Count == 1) return found[0];
    if (!interactive)
        throw new PatchException(found.Count == 0 ? "Geen .cue of .iso gevonden. Geef het pad op als argument." : "Meerdere discs gevonden. Geef het pad op als argument.");

    if (found.Count > 1)
    {
        for (int i = 0; i < found.Count; i++) Console.WriteLine($"  {i + 1}. {found[i]}");
        Console.Write("Welke disc? ");
        if (int.TryParse(Console.ReadLine(), out int n) && n >= 1 && n <= found.Count) return found[n - 1];
        throw new PatchException("Geen geldige keuze.");
    }

    Console.WriteLine("Geen .cue of .iso gevonden naast de patcher.");
    Console.Write("Sleep het .cue-bestand in dit venster (of typ het pad) en druk op Enter: ");
    var path = Console.ReadLine()?.Trim().Trim('"', '\'') ?? "";
    if (!File.Exists(path)) throw new PatchException($"Bestand niet gevonden: {path}");
    return path;
}

static IEnumerable<string> SafeEnumerate(string root, int depth)
{
    IEnumerable<string> files;
    try { files = Directory.EnumerateFiles(root).ToList(); } catch { yield break; }
    foreach (var f in files) yield return f;
    if (depth == 0) yield break;
    IEnumerable<string> dirs;
    try { dirs = Directory.EnumerateDirectories(root).ToList(); } catch { yield break; }
    foreach (var d in dirs)
        foreach (var f in SafeEnumerate(d, depth - 1)) yield return f;
}

// Spelers, scores en instellingen die het spel zelf schrijft, overleven een herinstallatie.
static Dictionary<string, byte[]> BackupUserData(string dir)
{
    var keep = new Dictionary<string, byte[]>();
    if (!Directory.Exists(dir)) return keep;
    foreach (var f in Directory.EnumerateFiles(dir, "SaveGame*.RCR"))
        keep[Path.GetFileName(f)] = File.ReadAllBytes(f);
    foreach (var rel in new[] { Path.Combine("settings", "menu.ini") })
        if (File.Exists(Path.Combine(dir, rel))) keep[rel] = File.ReadAllBytes(Path.Combine(dir, rel));
    return keep;
}

static void RestoreUserData(string dir, Dictionary<string, byte[]> keep)
{
    foreach (var (rel, bytes) in keep) File.WriteAllBytes(Path.Combine(dir, rel), bytes);
}

static bool Ask(string question)
{
    Console.Write(question);
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
    return answer is "j" or "ja" or "y" or "yes";
}

static void Pause(bool interactive)
{
    if (!interactive) return;
    Console.WriteLine();
    Console.Write("Druk op Enter om af te sluiten...");
    Console.ReadLine();
}

sealed class Options
{
    public string? Disc, Out, DgVoodooZip;
    public string Resolution = DgVoodoo.DefaultResolution;
    public bool NoDgVoodoo, Yes, ShowHelp, Wasd;
    public Widescreen? Widescreen;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new PatchException($"{args[i]} verwacht een waarde.");
            switch (args[i])
            {
                case "--out": o.Out = Next(); break;
                case "--dgvoodoo-zip": o.DgVoodooZip = Next(); break;
                case "--no-dgvoodoo": o.NoDgVoodoo = true; break;
                case "--widescreen":
                    // De beeldverhouding is optioneel: "--widescreen" alleen betekent 16:9.
                    o.Widescreen = Widescreen.Wide16x9;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && !File.Exists(args[i + 1]))
                        o.Widescreen = Widescreen.Parse(args[++i]) ?? throw new PatchException($"--widescreen: onbekende beeldverhouding '{args[i]}' (kies 16:9 of 16:10).");
                    break;
                case "--wasd": o.Wasd = true; break;
                case "--resolution":
                    o.Resolution = Next().ToLowerInvariant();
                    if (!DgVoodoo.IsValidResolution(o.Resolution))
                        throw new PatchException($"--resolution: onbekende waarde '{o.Resolution}' (bijv. max, 2x, desktop, 1920x1080 of unforced).");
                    break;
                case "--yes" or "-y": o.Yes = true; break;
                case "--help" or "-h" or "/?": o.ShowHelp = true; break;
                default: o.Disc = args[i]; break;
            }
        }
        return o;
    }
}

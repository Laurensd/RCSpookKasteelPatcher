using RedCatPatcher;
using RedCatPatcher.Disc;
using RedCatPatcher.Install;
using RedCatPatcher.Patching;
using RedCatPatcher.Updates;

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
          --update <exe>             Officiële Davilex-update meenemen ("Redcat Spookkasteel 98 patch.exe" en/of
                                     "...XP patch.exe"); mag vaker. Updates naast de patcher worden zelf gevonden
          --dgvoodoo-zip <zip>       Gebruik een lokaal gedownloade dgVoodoo2-zip (offline installeren)
          --no-dgvoodoo              dgVoodoo2 niet installeren
          --widescreen [16:9|16:10]  Breedbeeld in plaats van 4:3 (standaard 16:9)
          --wasd                     WASD als standaardbesturing in plaats van de pijltjestoetsen
          --controller               Gamepad-indeling: stick beweegt, A springen, B ontwijken, X schieten,
                                     LB/RB wapen wisselen, Back scores, Start menu (alleen in het spel, niet in het menu)
          --resolution <waarde>      Interne renderresolutie van dgVoodoo2 (standaard "max": zo groot als het
                                     scherm toelaat). Bijv. 2x, 3x, desktop, 1920x1080, of unforced (origineel)
          --shortcut / --no-shortcut Snelkoppeling op het bureaublad en in het Startmenu (Linux: programmamenu)
          --steam / --no-steam       RedCat toevoegen aan Steam als niet-Steam-game (Linux: met Proton)
          --steam-dir <map>          Steam-map, als de patcher die niet zelf vindt
          --proton <naam>            Compatibiliteitstool voor Steam op Linux (standaard proton_experimental)
          --debug-logs               Logbestanden van spel en menu aan laten (AdamDebug*.log e.d.), voor het
                                     zoeken naar problemen
          --yes                      Niets vragen; bestaande installatiemap overschrijven

        Zonder disc-argument zoekt de patcher naar een .cue/.iso in zijn eigen map en submappen.
        Ondersteund: versie 1.0.0 van de cd-rom, met of zonder de officiële Win98- en XP-update.
        """);
    return 0;
}

bool interactive = !options.Yes && !Console.IsInputRedirected;
try
{
    Console.WriteLine($"RedCat Spookkasteel patcher {typeof(Options).Assembly.GetName().Version?.ToString(3)}");
    Console.WriteLine();

    var discPath = options.Disc ?? FindDisc(interactive);
    var updates = options.Updates.Count > 0
        ? options.Updates.Select(u => (Exe: Path.GetFullPath(u), Update: OfficialUpdate.Identify(u))).ToList()
        : FindUpdates();
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
        var check = DiscVerifier.Verify(disc, iso, Console.IsOutputRedirected ? null
            : (done, total) => Console.Write($"\r       Disc controleren... {done * 100 / total}%"));
        if (!Console.IsOutputRedirected) Console.Write($"\r{new string(' ', 40)}\r");
        foreach (var line in check.Report()) Console.WriteLine($"       {line}");
        if (check.Status == DiscStatus.Suspicious && interactive && !Ask("       Toch doorgaan? [j/N] "))
            return 1;
        var data = iso.Find("Data") ?? throw new PatchException("Map 'Data' niet gevonden op de disc. Is dit de RedCat Spookkasteel cd-rom?");
        if (data.Children.All(c => !c.Name.Equals("RcHcGame.dat", StringComparison.OrdinalIgnoreCase)))
            throw new PatchException("RcHcGame.dat niet gevonden op de disc. Is dit de RedCat Spookkasteel cd-rom?");

        Console.WriteLine($"Doel:  {outDir}");
        var keep = BackupUserData(outDir);
        int logs = DebugLogs.Clean(outDir);
        if (logs > 0) Console.WriteLine($"       {logs} oude {(logs == 1 ? "logbestand" : "logbestanden")} verwijderd.");
        int count = 0;
        iso.ExtractDirectory(data, outDir, _ =>
        {
            if (++count % 25 == 0 && !Console.IsOutputRedirected) Console.Write($"\r       {count} bestanden gekopieerd...");
        });
        RestoreUserData(outDir, keep);
        Console.WriteLine($"{(Console.IsOutputRedirected ? "" : "\r")}       {count} bestanden gekopieerd.          ");
    }

    // 2. Officiële updates van Davilex (in de volgorde waarin ze uitkwamen)
    if (updates.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Officiële updates:");
        foreach (var (exe, update) in updates.DistinctBy(u => u.Update).OrderBy(u => u.Update.Order))
        {
            var changed = update.Apply(exe, outDir);
            Console.WriteLine($"  [ok] {update.Name}: {changed.Count} {(changed.Count == 1 ? "bestand" : "bestanden")} ({Path.GetFileName(exe)})");
        }
    }

    // 3. Versie bepalen; een onbekende versie wordt gemeld en niet gepatcht
    var version = GameVersion.Detect(outDir);
    Console.WriteLine();
    Console.WriteLine($"Versie: {version.Name}");

    // 4. Spelbestanden patchen
    Console.WriteLine();
    Console.WriteLine("Fixes:");
    foreach (var patch in GamePatches.All)
    {
        var result = patch.Apply(outDir, version.Build);
        Console.WriteLine($"  [{(result == PatchResult.Applied ? "ok" : "al toegepast")}] {patch.Description}");
    }
    if (!options.DebugLogs)
        foreach (var patch in DebugLogs.PatchesFor(version))
        {
            var result = patch.Apply(outDir, version.Build);
            Console.WriteLine($"  [{(result == PatchResult.Applied ? "ok" : "al toegepast")}] {patch.Description}");
        }

    // 5. Opties
    if (options.Widescreen != null || options.Wasd || options.Controller)
    {
        Console.WriteLine();
        Console.WriteLine("Opties:");
    }
    if (options.Widescreen is { } wide)
    {
        foreach (var patch in wide.Patches)
            patch.Apply(outDir, version.Build);
        wide.ApplySettings(outDir);
        Console.WriteLine($"  [ok] Breedbeeld {wide.Name} ({wide.Width}x{wide.Height}, FOV {wide.FieldOfView:0.00}, HUD aangepast)");
    }
    if (options.Wasd)
    {
        ModernControls.Apply(outDir);
        Console.WriteLine($"  [ok] {ModernControls.Description}");
    }
    if (options.Controller || options.Wasd)
    {
        // Zonder --controller alleen de stick naar WASD, anders bestuurt de stick met --wasd niets meer.
        Controller.Apply(outDir, options.Wasd, buttons: options.Controller);
        Console.WriteLine($"  [ok] {(options.Controller ? Controller.Description : "Joystick-stick volgt WASD")}");
    }

    // 6. dgVoodoo2
    if (!options.NoDgVoodoo)
    {
        Console.WriteLine();
        var zip = await DgVoodoo.GetZipAsync(options.DgVoodooZip, msg => Console.WriteLine(msg));
        DgVoodoo.Install(zip, outDir, options.Resolution);
        Console.WriteLine($"  [ok] dgVoodoo2 geïnstalleerd (DirectX/Glide-wrapper, schermvullend {options.Widescreen?.Name ?? "4:3"}, renderresolutie {options.Resolution})");
    }

    // 7. Snelkoppelingen en Steam (vraagt interactief j/N als er geen optie is opgegeven)
    Shortcuts.Run(outDir, options.Shortcuts, interactive, Ask);

    Console.WriteLine();
    Console.WriteLine("Klaar! Start het spel met:");
    Console.WriteLine($"  {Path.Combine(outDir, "RedCat.exe")}");
    if (!OperatingSystem.IsWindows() && options.Shortcuts.Steam != true)
        Console.WriteLine("  (Linux/Steam Deck: voeg RedCat.exe toe aan Steam met --steam, of zelf als niet-Steam-game met Proton.)");
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

// Officiële updates naast de patcher (of in een submap) worden vanzelf meegenomen.
static List<(string Exe, OfficialUpdate Update)> FindUpdates()
{
    var roots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }.Distinct();
    return roots
        .SelectMany(r => SafeEnumerate(r, depth: 2))
        .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath).Distinct()
        .Select(f => (Exe: f, Update: OfficialUpdate.TryIdentify(f)))
        .Where(u => u.Update != null)
        .Select(u => (u.Exe, u.Update!))
        .ToList();
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
    public bool NoDgVoodoo, Yes, ShowHelp, Wasd, Controller, DebugLogs;
    public readonly List<string> Updates = [];
    public Widescreen? Widescreen;
    public readonly ShortcutOptions Shortcuts = new();

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
                case "--update":
                    var update = Next();
                    if (!File.Exists(update)) throw new PatchException($"--update: bestand niet gevonden: {update}");
                    o.Updates.Add(update);
                    break;
                case "--no-dgvoodoo": o.NoDgVoodoo = true; break;
                case "--widescreen":
                    // De beeldverhouding is optioneel: "--widescreen" alleen betekent 16:9.
                    o.Widescreen = Widescreen.Wide16x9;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-') && !File.Exists(args[i + 1]))
                        o.Widescreen = Widescreen.Parse(args[++i]) ?? throw new PatchException($"--widescreen: onbekende beeldverhouding '{args[i]}' (kies 16:9 of 16:10).");
                    break;
                case "--wasd": o.Wasd = true; break;
                case "--controller": o.Controller = true; break;
                case "--debug-logs": o.DebugLogs = true; break;
                case "--resolution":
                    o.Resolution = Next().ToLowerInvariant();
                    if (!DgVoodoo.IsValidResolution(o.Resolution))
                        throw new PatchException($"--resolution: onbekende waarde '{o.Resolution}' (bijv. max, 2x, desktop, 1920x1080 of unforced).");
                    break;
                case "--shortcut": o.Shortcuts.Shortcut = true; break;
                case "--no-shortcut": o.Shortcuts.Shortcut = false; break;
                case "--steam": o.Shortcuts.Steam = true; break;
                case "--no-steam": o.Shortcuts.Steam = false; break;
                case "--steam-dir": o.Shortcuts.SteamDir = Next(); break;
                case "--proton": o.Shortcuts.Proton = Next(); break;
                case "--yes" or "-y": o.Yes = true; break;
                case "--help" or "-h" or "/?": o.ShowHelp = true; break;
                default: o.Disc = args[i]; break;
            }
        }
        return o;
    }
}

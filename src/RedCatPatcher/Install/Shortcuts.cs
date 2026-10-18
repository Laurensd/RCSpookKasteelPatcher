using RedCatPatcher.Steam;

namespace RedCatPatcher.Install;

/// <summary>Keuzes voor snelkoppelingen; null betekent: vragen (interactief) of overslaan (met --yes).</summary>
public sealed class ShortcutOptions
{
    public bool? Shortcut, Steam;
    public string? SteamDir;
    /// <summary>Opgegeven met --proton; null: een bestaande keuze in Steam houden, anders <see cref="Shortcuts.DefaultProton"/>.</summary>
    public string? Proton;
}

/// <summary>
/// Snelkoppelingen na de installatie: bureaublad en Startmenu (Windows), het programmamenu (Linux),
/// en de game als niet-Steam-game in Steam, op Linux met Proton als compatibiliteitstool.
/// Ze wijzen naar RedCatLauncher.exe als die in de spelmap staat, anders naar RedCat.exe. Bestaande snelkoppelingen
/// en Steam-regels naar RedCat.exe in dezelfde map worden omgezet naar de launcher.
/// </summary>
public static class Shortcuts
{
    public const string Name = "RedCat Spookkasteel";
    public const string DefaultProton = "proton_experimental";
    public const string GameExe = "RedCat.exe";
    public const string LauncherExe = "RedCatLauncher.exe";

    public static void Run(string gameDir, ShortcutOptions options, bool interactive, Func<string, bool> ask)
    {
        gameDir = Path.GetFullPath(gameDir);
        var game = Path.Combine(gameDir, GameExe);
        var launcher = Path.Combine(gameDir, LauncherExe);
        var exe = File.Exists(launcher) ? launcher : game;
        var steam = options.SteamDir != null ? new SteamInstall(options.SteamDir) : SteamInstall.Find();

        bool wantSteam = options.Steam ?? (interactive && steam != null &&
            ask(OperatingSystem.IsWindows() ? "RedCat toevoegen aan Steam als niet-Steam-game? [j/N] " : "RedCat toevoegen aan Steam als niet-Steam-game (met Proton)? [j/N] "));
        bool wantShortcut = options.Shortcut ?? (interactive &&
            ask(OperatingSystem.IsWindows() ? "Snelkoppeling maken op het bureaublad en in het Startmenu? [j/N] " : "Snelkoppeling maken in het programmamenu? [j/N] "));
        if (!wantSteam && !wantShortcut) return;

        Console.WriteLine();
        Console.WriteLine("Snelkoppelingen:");
        ulong? steamGameId = null;
        if (wantSteam)
        {
            if (steam == null)
                Console.WriteLine("  [overgeslagen] Steam niet gevonden. Geef de Steam-map op met --steam-dir.");
            else
                steamGameId = AddToSteam(steam, exe, game, gameDir, options.Proton, interactive, ask);
        }
        if (wantShortcut)
        {
            if (OperatingSystem.IsWindows())
                foreach (var dir in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs }.Select(Environment.GetFolderPath))
                {
                    var lnk = CreateWindowsShortcut(dir, exe, gameDir, icon: game);
                    Console.WriteLine($"  [ok] {lnk}");
                    if (exe != game)
                        foreach (var old in RetargetWindowsShortcuts(dir, game, exe, gameDir, skip: lnk))
                            Console.WriteLine($"  [ok] {old} (wees naar {GameExe}, nu naar {LauncherExe})");
                }
            else
                CreateDesktopEntry(exe, gameDir, steamGameId, steam);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string CreateWindowsShortcut(string folder, string exe, string gameDir, string icon)
    {
        (exe, gameDir, icon) = (Path.GetFullPath(exe), Path.GetFullPath(gameDir), Path.GetFullPath(icon));
        Directory.CreateDirectory(folder);
        var lnk = Path.Combine(folder, Name + ".lnk");
        ShellLink.Write(lnk, exe, gameDir, icon, Description);
        return lnk;
    }

    private const string Description = "RedCat Spookkasteel (Davilex, 2000)";

    /// <summary>
    /// Zet andere snelkoppelingen in de map (en submappen) die naar <paramref name="oldExe"/> wijzen om naar
    /// <paramref name="exe"/>, met dezelfde bestandsnaam: bijvoorbeeld een hernoemde of gekopieerde snelkoppeling.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> RetargetWindowsShortcuts(string folder, string oldExe, string exe, string gameDir, string skip)
    {
        var done = new List<string>();
        List<string> links;
        try { links = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return done; }

        foreach (var lnk in links.Where(l => !l.Equals(skip, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (ShellLink.ReadTarget(File.ReadAllBytes(lnk)) is not { } target || !SteamShortcuts.SamePath(target, oldExe)) continue;
                ShellLink.Write(lnk, exe, gameDir, oldExe, Description);
                done.Add(lnk);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"  [overgeslagen] {lnk}: {e.Message}");
            }
        }
        return done;
    }

    /// <summary>Voegt de game toe voor elke Steam-gebruiker; geeft de gameid voor steam://rungameid/ terug.</summary>
    private static ulong? AddToSteam(SteamInstall steam, string exe, string game, string gameDir, string? proton, bool interactive, Func<string, bool> ask)
    {
        var users = steam.Users();
        if (users.Count == 0)
        {
            Console.WriteLine($"  [overgeslagen] Geen Steam-gebruikers gevonden in {steam.Root}. Start Steam eerst één keer en log in.");
            return null;
        }

        // Steam houdt de lijst in het geheugen en schrijft config.vdf bij het afsluiten terug.
        if (SteamInstall.IsRunning() && interactive)
        {
            Console.Write("  Steam draait nog. Sluit Steam helemaal af (Steam → Afsluiten) en druk op Enter...");
            Console.ReadLine();
            if (SteamInstall.IsRunning() && !ask("  Steam draait nog steeds. Toch toevoegen? (daarna Steam herstarten) [j/N] "))
            {
                Console.WriteLine("  [overgeslagen] Steam");
                return null;
            }
        }
        bool running = SteamInstall.IsRunning();

        // Steam op Linux kan geen pictogram uit een .exe halen; op Windows wel.
        var shortcut = new SteamShortcut(Name, exe, gameDir, Icon: OperatingSystem.IsWindows() ? game : "");
        var formerExe = exe == game ? null : game;
        var appIds = new HashSet<uint>();
        foreach (var user in users)
        {
            var root = File.Exists(user.ShortcutsVdf) ? BinaryVdf.Read(File.ReadAllBytes(user.ShortcutsVdf)) : SteamShortcuts.Empty();
            SteamInstall.Backup(user.ShortcutsVdf);
            var (appId, found) = SteamShortcuts.AddOrUpdate(root, shortcut, formerExe);
            appIds.Add(appId);
            Directory.CreateDirectory(user.ConfigDir);
            File.WriteAllBytes(user.ShortcutsVdf, BinaryVdf.Write(root));
            Console.WriteLine($"  [ok] Steam ({user.Name}): niet-Steam-game '{Name}'" + found switch
            {
                SteamShortcuts.Found.FormerExe => $" (wees naar {GameExe}, nu naar {LauncherExe})",
                SteamShortcuts.Found.Same => " (bijgewerkt)",
                _ => "",
            });
        }

        if (!OperatingSystem.IsWindows())
        {
            var config = File.Exists(steam.ConfigVdf) ? TextVdf.Parse(File.ReadAllText(steam.ConfigVdf)) : new VdfMap();
            SteamInstall.Backup(steam.ConfigVdf);
            var tools = appIds.Select(appId => SteamShortcuts.SetCompatTool(config, appId, proton ?? DefaultProton, keepExisting: proton == null))
                .Distinct().ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(steam.ConfigVdf)!);
            File.WriteAllText(steam.ConfigVdf, TextVdf.Write(config));
            Console.WriteLine($"  [ok] Steam: compatibiliteitstool {string.Join(", ", tools)}");
        }

        Console.WriteLine($"       (backups van shortcuts.vdf{(OperatingSystem.IsWindows() ? "" : " en config.vdf")} staan ernaast als *.bak)");
        if (running)
            Console.WriteLine("  LET OP: Steam draait. Herstart Steam om RedCat in de bibliotheek te zien.");
        return appIds.Count == 1 ? SteamShortcut.ToGameId(appIds.First()) : null;
    }

    /// <summary>
    /// .desktop-bestand in ~/.local/share/applications. Start via Steam als RedCat daar net is toegevoegd,
    /// anders via Wine als dat er is.
    /// </summary>
    private static void CreateDesktopEntry(string exe, string gameDir, ulong? steamGameId, SteamInstall? steam)
    {
        string exec;
        if (steamGameId is { } gameId)
        {
            bool flatpak = steam?.Root.Contains("com.valvesoftware.Steam", StringComparison.Ordinal) == true;
            exec = $"{(flatpak ? "flatpak run com.valvesoftware.Steam" : "steam")} steam://rungameid/{gameId}";
        }
        else if (FindOnPath("wine") is { } wine)
            exec = $"\"{wine}\" \"{exe}\"";
        else
        {
            Console.WriteLine("  [overgeslagen] Programmamenu: voeg RedCat toe aan Steam (--steam) of installeer Wine.");
            return;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var path = Path.Combine(dataHome, "applications", "redcat-spookkasteel.desktop");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, DesktopEntry(exec, gameDir));
        Console.WriteLine($"  [ok] {path}");
    }

    public static string DesktopEntry(string exec, string gameDir) => $"""
        [Desktop Entry]
        Type=Application
        Name={Name}
        Comment=RedCat Spookkasteel (Davilex, 2000)
        Exec={exec}
        Path={gameDir}
        Icon=applications-games
        Terminal=false
        Categories=Game;

        """;

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
}

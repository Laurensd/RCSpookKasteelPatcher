using RedCatPatcher.Steam;

namespace RedCatPatcher.Install;

/// <summary>Keuzes voor snelkoppelingen; null betekent: vragen (interactief) of overslaan (met --yes).</summary>
public sealed class ShortcutOptions
{
    public bool? Shortcut, Steam;
    public string? SteamDir;
    public string Proton = Shortcuts.DefaultProton;
}

/// <summary>
/// Snelkoppelingen na de installatie: bureaublad en Startmenu (Windows), het programmamenu (Linux),
/// en RedCat.exe als niet-Steam-game in Steam, op Linux met Proton als compatibiliteitstool.
/// </summary>
public static class Shortcuts
{
    public const string Name = "RedCat Spookkasteel";
    public const string DefaultProton = "proton_experimental";

    public static void Run(string gameDir, ShortcutOptions options, bool interactive, Func<string, bool> ask)
    {
        var exe = Path.Combine(gameDir, "RedCat.exe");
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
                steamGameId = AddToSteam(steam, exe, gameDir, options.Proton, interactive, ask);
        }
        if (wantShortcut)
        {
            if (OperatingSystem.IsWindows())
                foreach (var dir in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs }.Select(Environment.GetFolderPath))
                {
                    var lnk = CreateWindowsShortcut(dir, exe, gameDir);
                    Console.WriteLine($"  [ok] {lnk}");
                }
            else
                CreateDesktopEntry(exe, gameDir, steamGameId, steam);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string CreateWindowsShortcut(string folder, string exe, string gameDir)
    {
        (exe, gameDir) = (Path.GetFullPath(exe), Path.GetFullPath(gameDir));
        Directory.CreateDirectory(folder);
        var lnk = Path.Combine(folder, Name + ".lnk");
        ShellLink.Write(lnk, exe, gameDir, exe, "RedCat Spookkasteel (Davilex, 2000)");
        return lnk;
    }

    /// <summary>Voegt de game toe voor elke Steam-gebruiker; geeft de gameid voor steam://rungameid/ terug.</summary>
    private static ulong? AddToSteam(SteamInstall steam, string exe, string gameDir, string proton, bool interactive, Func<string, bool> ask)
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
        var shortcut = new SteamShortcut(Name, exe, gameDir, Icon: OperatingSystem.IsWindows() ? exe : "");
        var appIds = new HashSet<uint>();
        foreach (var user in users)
        {
            var root = File.Exists(user.ShortcutsVdf) ? BinaryVdf.Read(File.ReadAllBytes(user.ShortcutsVdf)) : SteamShortcuts.Empty();
            SteamInstall.Backup(user.ShortcutsVdf);
            appIds.Add(SteamShortcuts.AddOrUpdate(root, shortcut));
            Directory.CreateDirectory(user.ConfigDir);
            File.WriteAllBytes(user.ShortcutsVdf, BinaryVdf.Write(root));
            Console.WriteLine($"  [ok] Steam ({user.Name}): niet-Steam-game '{Name}'");
        }

        if (!OperatingSystem.IsWindows())
        {
            var config = File.Exists(steam.ConfigVdf) ? TextVdf.Parse(File.ReadAllText(steam.ConfigVdf)) : new VdfMap();
            SteamInstall.Backup(steam.ConfigVdf);
            foreach (var appId in appIds) SteamShortcuts.SetCompatTool(config, appId, proton);
            Directory.CreateDirectory(Path.GetDirectoryName(steam.ConfigVdf)!);
            File.WriteAllText(steam.ConfigVdf, TextVdf.Write(config));
            Console.WriteLine($"  [ok] Steam: compatibiliteitstool {proton}");
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

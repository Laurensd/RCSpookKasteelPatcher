using System.Diagnostics;

namespace RedCatPatcher.Steam;

/// <summary>Een Steam-gebruiker op deze computer: de map userdata/&lt;AccountId&gt;.</summary>
public sealed record SteamUser(uint AccountId, string Name, string UserDataDir)
{
    /// <summary>SteamID64, zoals die in loginusers.vdf staat.</summary>
    public ulong SteamId64 => AccountId + 76561197960265728UL;

    public string ConfigDir => Path.Combine(UserDataDir, "config");
    public string ShortcutsVdf => Path.Combine(ConfigDir, "shortcuts.vdf");
}

/// <summary>Een Steam-installatie: de map met userdata/ en config/.</summary>
public sealed class SteamInstall(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string ConfigVdf => Path.Combine(Root, "config", "config.vdf");

    /// <summary>Zoekt Steam op de gebruikelijke plekken (Windows-register, ~/.steam, Flatpak).</summary>
    public static SteamInstall? Find()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string path)
                candidates.Add(path);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            candidates.Add(Path.Combine(home, ".steam", "steam"));
            candidates.Add(Path.Combine(home, ".local", "share", "Steam"));
            candidates.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }
        return candidates.Where(c => Directory.Exists(Path.Combine(c, "userdata"))).Select(c => new SteamInstall(c)).FirstOrDefault();
    }

    /// <summary>Alle Steam-accounts die op deze computer hebben ingelogd (userdata/0 is geen account).</summary>
    public IReadOnlyList<SteamUser> Users()
    {
        var names = PersonaNames();
        var userdata = Path.Combine(Root, "userdata");
        if (!Directory.Exists(userdata)) return [];
        return Directory.EnumerateDirectories(userdata)
            .Select(d => (Dir: d, Ok: uint.TryParse(Path.GetFileName(d), out var id), Id: id))
            .Where(u => u.Ok && u.Id != 0)
            .Select(u => new SteamUser(u.Id, names.GetValueOrDefault(u.Id + 76561197960265728UL, u.Id.ToString()), u.Dir))
            .OrderBy(u => u.AccountId)
            .ToList();
    }

    /// <summary>Weergavenamen uit config/loginusers.vdf, op SteamID64.</summary>
    private Dictionary<ulong, string> PersonaNames()
    {
        var result = new Dictionary<ulong, string>();
        try
        {
            var users = TextVdf.Parse(File.ReadAllText(Path.Combine(Root, "config", "loginusers.vdf"))).Map("users");
            foreach (var (id, value) in users ?? [])
                if (ulong.TryParse(id, out var steamId) && value is VdfMap user && user.String("PersonaName") is { } name)
                    result[steamId] = TextVdf.Unescape(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PatchException) { }
        return result;
    }

    /// <summary>Steam schrijft shortcuts.vdf en config.vdf bij het afsluiten terug, dus wijzigingen moeten met Steam dicht.</summary>
    public static bool IsRunning()
    {
        foreach (var p in Process.GetProcessesByName("steam"))
        {
            p.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>Kopie naast het origineel, met datum en tijd in de naam, zodat elke run een eigen backup heeft.</summary>
    public static string? Backup(string file)
    {
        if (!File.Exists(file)) return null;
        var backup = $"{file}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(file, backup, overwrite: true);
        return backup;
    }
}

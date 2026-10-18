using System.Text;
using RedCatPatcher.Updates;

namespace RedCatPatcher.Steam;

/// <summary>Een niet-Steam-game zoals hij in shortcuts.vdf komt te staan.</summary>
public sealed record SteamShortcut(string AppName, string Exe, string StartDir, string Icon = "", string LaunchOptions = "")
{
    /// <summary>Exe en StartDir staan in shortcuts.vdf tussen aanhalingstekens, zoals Steam ze zelf schrijft.</summary>
    public string QuotedExe => $"\"{Exe}\"";
    public string QuotedStartDir => $"\"{StartDir}\"";

    /// <summary>
    /// De appid die Steam zelf aan een niet-Steam-game geeft: CRC-32 van exe (met aanhalingstekens) + naam,
    /// met het hoogste bit gezet. Dezelfde id gebruikt Steam voor CompatToolMapping, grid-plaatjes en controllerlay-outs.
    /// </summary>
    public uint AppId => ComputeAppId(QuotedExe, AppName);

    /// <summary>Id voor steam://rungameid/ (de oude 64-bit "gameid" van een snelkoppeling).</summary>
    public ulong GameId => ToGameId(AppId);

    public static ulong ToGameId(uint appId) => ((ulong)appId << 32) | 0x02000000;

    public static uint ComputeAppId(string quotedExe, string appName) =>
        Crc32.Compute(Encoding.UTF8.GetBytes(quotedExe + appName)) | 0x80000000;
}

/// <summary>Toevoegen van een niet-Steam-game aan shortcuts.vdf en het kiezen van Proton in config.vdf.</summary>
public static class SteamShortcuts
{
    /// <summary>Hoe <see cref="AddOrUpdate"/> de regel vond.</summary>
    public enum Found
    {
        /// <summary>Geen bestaande regel: nieuw toegevoegd.</summary>
        New,
        /// <summary>Zelfde appid of zelfde exe.</summary>
        Same,
        /// <summary>Een regel naar de vorige exe in dezelfde map (RedCat.exe voordat de launcher er was).</summary>
        FormerExe,
    }

    /// <summary>
    /// Voegt de snelkoppeling toe aan de "shortcuts"-map van shortcuts.vdf, of werkt een bestaande bij
    /// (zelfde appid, zelfde exe, of anders <paramref name="formerExe"/>), zodat opnieuw patchen geen dubbele game
    /// oplevert. Een bestaande regel houdt zijn appid, zodat de Proton-keuze, plaatjes, controllerlay-out en speeltijd
    /// (allemaal per appid) blijven. Velden die Steam of de gebruiker zelf heeft gezet (speeltijd, tags, verborgen,
    /// startopties) blijven staan. Geeft de appid terug die in het bestand staat.
    /// </summary>
    public static (uint AppId, Found Found) AddOrUpdate(VdfMap shortcutsVdf, SteamShortcut shortcut, string? formerExe = null)
    {
        var list = shortcutsVdf.GetOrAddMap("shortcuts");
        var entries = list.Select(kv => kv.Value).OfType<VdfMap>().ToList();
        var found = Found.Same;
        var entry = entries.FirstOrDefault(e => e["appid"] is int id && (uint)id == shortcut.AppId || HasExe(e, shortcut.Exe));
        if (entry == null && formerExe != null && (entry = entries.FirstOrDefault(e => HasExe(e, formerExe))) != null)
            found = Found.FormerExe;

        if (entry == null)
        {
            found = Found.New;
            int index = list.Select(kv => int.TryParse(kv.Key, out var n) ? n + 1 : 0).DefaultIfEmpty(0).Max();
            entry = new VdfMap
            {
                { "appid", (int)shortcut.AppId },
                { "AppName", "" },
                { "Exe", "" },
                { "StartDir", "" },
                { "icon", "" },
                { "ShortcutPath", "" },
                { "LaunchOptions", "" },
                { "IsHidden", 0 },
                { "AllowDesktopConfig", 1 },
                { "AllowOverlay", 1 },
                { "OpenVR", 0 },
                { "Devkit", 0 },
                { "DevkitGameID", "" },
                { "DevkitOverrideAppID", 0 },
                { "LastPlayTime", 0 },
                { "FlatpakAppID", "" },
                { "tags", new VdfMap() },
            };
            entry["LaunchOptions"] = shortcut.LaunchOptions;
            list.Add(index.ToString(), entry);
        }
        else if (entry["appid"] is not int)
            entry["appid"] = (int)shortcut.AppId;

        entry["AppName"] = shortcut.AppName;
        entry["Exe"] = shortcut.QuotedExe;
        entry["StartDir"] = shortcut.QuotedStartDir;
        entry["icon"] = shortcut.Icon;
        return ((uint)(int)entry["appid"]!, found);
    }

    private static bool HasExe(VdfMap entry, string exe) => entry.String("Exe") is { } e && SamePath(e, exe);

    /// <summary>Paden vergelijken zoals Windows en Steam dat doen (aanhalingstekens weg): hoofdletterongevoelig, / en \ gelijk.</summary>
    public static bool SamePath(string a, string b) =>
        Normalize(a).Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Trim('"').Replace('/', '\\').TrimEnd('\\');

    /// <summary>Lege shortcuts.vdf zoals Steam hem aanmaakt.</summary>
    public static VdfMap Empty() => new() { { "shortcuts", new VdfMap() } };

    /// <summary>
    /// Zet een compatibiliteitstool (Proton) voor een appid in config.vdf:
    /// InstallConfigStore/Software/Valve/Steam/CompatToolMapping/&lt;appid&gt;.
    /// Met <paramref name="keepExisting"/> blijft een tool die er al staat (bijv. zelf gekozen in Steam) staan.
    /// Geeft de tool terug die er nu staat.
    /// </summary>
    public static string SetCompatTool(VdfMap configVdf, uint appId, string tool, bool keepExisting = false)
    {
        var mapping = configVdf.GetOrAddMap("InstallConfigStore").GetOrAddMap("Software")
            .GetOrAddMap("Valve").GetOrAddMap("Steam").GetOrAddMap("CompatToolMapping");
        var entry = mapping.GetOrAddMap(appId.ToString());
        if (keepExisting && entry.String("name") is { Length: > 0 } existing) return TextVdf.Unescape(existing);
        entry["name"] = TextVdf.Escape(tool);
        entry["config"] = entry.String("config") ?? "";
        entry["priority"] = "250";
        return tool;
    }
}

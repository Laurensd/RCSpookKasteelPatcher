using System.Text;
using System.Text.RegularExpressions;

namespace RedCatPatcher.Patching;

/// <summary>
/// Past losse waarden in de INI-bestanden van het spel aan en laat de rest van het bestand
/// (commentaar, volgorde, regeleinden, Windows-1252-tekens) ongemoeid.
/// </summary>
public static class IniFile
{
    /// <summary>Zet <paramref name="key"/> in de eerste sectie met de naam <paramref name="section"/>.</summary>
    public static void Set(string path, string section, string key, string value) =>
        Update(path, s => s.Equals(section, StringComparison.OrdinalIgnoreCase), key, _ => value, firstSectionOnly: true);

    /// <summary>De waarde van <paramref name="key"/> in de eerste sectie met de naam <paramref name="section"/>.</summary>
    public static string Get(string path, string section, string key)
    {
        if (!File.Exists(path))
            throw new PatchException($"{Path.GetFileName(path)} niet gevonden in de spelmap.");

        bool inSection = false;
        foreach (var line in File.ReadLines(path, Encoding.Latin1))
        {
            var header = Regex.Match(line, @"^\s*\[([^\]]*)\]");
            if (header.Success)
            {
                // Net als Update: bij dubbele secties telt alleen de eerste.
                if (inSection) break;
                inSection = header.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            var entry = Regex.Match(line, $@"^\s*{Regex.Escape(key)}\s*=(.*)$", RegexOptions.IgnoreCase);
            if (inSection && entry.Success) return entry.Groups[1].Value;
        }
        throw new PatchException($"Instelling '{key}' niet gevonden in {Path.GetFileName(path)}.");
    }

    /// <summary>
    /// Alle secties met hun sleutels (hoofdletterongevoelig). Bij dubbele secties of sleutels telt de eerste, net als
    /// bij Windows. Een ontbrekend bestand geeft een lege lijst.
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> ReadAll(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return sections;
        Dictionary<string, string>? current = null;
        foreach (var line in File.ReadLines(path, Encoding.Latin1))
        {
            var header = Regex.Match(line, @"^\s*\[([^\]]*)\]");
            if (header.Success)
            {
                var name = header.Groups[1].Value.Trim();
                current = sections.ContainsKey(name) ? null : sections[name] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            var entry = Regex.Match(line, @"^\s*([^;=\s][^=]*?)\s*=([^;]*)");
            if (current != null && entry.Success) current.TryAdd(entry.Groups[1].Value, entry.Groups[2].Value.Trim());
        }
        return sections;
    }

    /// <summary>
    /// Past <paramref name="key"/> aan in elke sectie waarvan de naam aan <paramref name="sectionFilter"/> voldoet.
    /// <paramref name="change"/> krijgt de huidige waarde en geeft de nieuwe terug.
    /// </summary>
    public static void Update(string path, Func<string, bool> sectionFilter, string key, Func<string, string> change, bool firstSectionOnly = false)
    {
        if (!File.Exists(path))
            throw new PatchException($"{Path.GetFileName(path)} niet gevonden in de spelmap.");

        var lines = Regex.Split(File.ReadAllText(path, Encoding.Latin1), "(?<=\n)");
        bool inSection = false, seenSection = false, foundKey = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var header = Regex.Match(line, @"^\s*\[([^\]]*)\]");
            if (header.Success)
            {
                // Bij dubbele secties leest Windows (GetPrivateProfileString) alleen de eerste.
                inSection = sectionFilter(header.Groups[1].Value) && !(firstSectionOnly && seenSection);
                seenSection |= inSection;
                continue;
            }
            if (!inSection) continue;

            var entry = Regex.Match(line, $@"^(\s*{Regex.Escape(key)}\s*=)([^\r\n]*)(\r?\n?)$", RegexOptions.IgnoreCase);
            if (!entry.Success) continue;
            lines[i] = entry.Groups[1].Value + change(entry.Groups[2].Value) + entry.Groups[3].Value;
            foundKey = true;
        }
        if (!foundKey)
            throw new PatchException($"Instelling '{key}' niet gevonden in {Path.GetFileName(path)}.");

        File.WriteAllText(path, string.Concat(lines), Encoding.Latin1);
    }
}

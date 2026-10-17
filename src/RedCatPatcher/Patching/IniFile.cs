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

using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Updates;

/// <summary>
/// Een officiële update van Davilex (een Wise-installer) die de patcher zelf toepast, zonder de installer uit te voeren.
/// Achtergrond: zie docs/TECHNISCH.md, "Officiële updates".
/// </summary>
/// <param name="ScriptOffset">Begin van het (deflate-gecomprimeerde) Wise-script in de exe.</param>
/// <param name="DataOffset">Basis waartegen het script de posities van de bestanden opgeeft.</param>
public sealed record OfficialUpdate(string Name, string Sha256, long Size, int Order, int ScriptOffset, int DataOffset)
{
    /// <summary>"Redcat Spookkasteel 98 patch.exe": versie 1.0.1 van RcHcGame.dat, spelbalans, geluiden en leesmij.</summary>
    public static readonly OfficialUpdate Win98 = new("Win98-update (v1.0.1, juni 2001)",
        "6B6F671A210DF1B0D9CCCBA55DFD01B9A91ADC489D9B785024981A9944398A06", 749_639, 1, 0x4E66, 0x1E6FD);

    /// <summary>"Redcat Spookkasteel XP patch.exe": een nieuwe build van RcHcGame.dat uit 2004.</summary>
    public static readonly OfficialUpdate Xp = new("XP-update (december 2004)",
        "A953AAFA32E96267A1F6F8AD53CDC6DD32338E15A7C092A6F5C5DA355D74F8DE", 1_543_032, 2, 0x4DB0, 0x1A934);

    public static IReadOnlyList<OfficialUpdate> Known => [Win98, Xp];

    /// <summary>Herkent een update aan de SHA-256 van de exe; een onbekende exe geeft een melding voor de gebruiker.</summary>
    public static OfficialUpdate Identify(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        return Known.FirstOrDefault(u => u.Sha256 == hash) ?? throw new PatchException(
            $"{Path.GetFileName(path)} is geen ondersteunde update (SHA-256 {hash}).\n" +
            $"      Ondersteund: {string.Join(" en ", Known.Select(u => u.Name))}.\n" +
            $"      Is dit wel een update van RedCat Spookkasteel? Meld het dan op {GameVersion.IssuesUrl}");
    }

    /// <summary>Zoals <see cref="Identify"/>, maar null voor bestanden die geen bekende update zijn.</summary>
    public static OfficialUpdate? TryIdentify(string path)
    {
        if (Known.All(u => u.Size != new FileInfo(path).Length)) return null;
        try { return Identify(path); }
        catch (PatchException) { return null; }
    }

    /// <summary>Past de update toe op een spelmap en geeft de gewijzigde bestanden terug.</summary>
    public IReadOnlyList<string> Apply(string exePath, string gameDir)
    {
        var entries = Entries(File.ReadAllBytes(exePath)).ToList();
        if (entries.Count == 0)
            throw new PatchException($"{Name}: geen bestanden gevonden in {Path.GetFileName(exePath)}.");

        var changed = new List<string>();
        foreach (var (relative, data) in entries)
        {
            var path = ResolvePath(gameDir, relative);
            var content = data;
            if (WiseDelta.IsDelta(data))
            {
                var current = File.Exists(path) ? File.ReadAllBytes(path) : [];
                if (WiseDelta.Matches(current, WiseDelta.Target(data))) continue;
                if (!WiseDelta.Matches(current, WiseDelta.Source(data)))
                    throw new PatchException(
                        $"{Name}: {relative} is niet het bestand waarop deze update past.\n" +
                        $"      Deze combinatie wordt niet ondersteund; meld het op {GameVersion.IssuesUrl}");
                content = WiseDelta.Apply(current, data);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            changed.Add(relative);
        }
        return changed;
    }

    /// <summary>
    /// De bestanden die het Wise-script in de spelmap (%MAINDIR%) installeert, in volgorde. Een installatie-opdracht
    /// staat in het script als: begin, eind, datum, grootte, 20 bytes, CRC-32, doelpad. De gecomprimeerde data staat
    /// op <see cref="DataOffset"/> + begin, gevolgd door de CRC-32 van de uitgepakte data.
    /// </summary>
    private IEnumerable<(string Path, byte[] Data)> Entries(byte[] exe)
    {
        var script = Inflate(exe, ScriptOffset, exe.Length - ScriptOffset);
        var marker = "%MAINDIR%\\"u8.ToArray();
        for (int p = IndexOf(script, marker, 0); p >= 0; p = IndexOf(script, marker, p + 1))
        {
            // Hetzelfde pad komt ook voor in andere opdrachten (bijv. als bron van een delta); die vallen af op de CRC.
            if (p < 40) continue;
            long start = DataOffset + (long)U32(script, p - 40), end = DataOffset + (long)U32(script, p - 36);
            uint crc = U32(script, p - 4);
            if (end - start <= 4 || end > exe.Length || U32(exe, (int)end - 4) != crc) continue;

            byte[] data;
            try { data = Inflate(exe, (int)start, (int)(end - 4 - start)); }
            catch (InvalidDataException) { continue; }
            if (Crc32.Compute(data) != crc) continue;

            int length = Array.IndexOf(script, (byte)0, p) - p - marker.Length;
            var relative = Encoding.Latin1.GetString(script, p + marker.Length, length);
            if (!relative.Contains('%')) yield return (relative, data);
        }
    }

    /// <summary>Zoekt het pad hoofdletterongevoelig op (de cd en de updates schrijven namen verschillend).</summary>
    private static string ResolvePath(string root, string relative)
    {
        var path = root;
        foreach (var part in relative.Split('\\'))
        {
            var existing = Directory.Exists(path)
                ? Directory.EnumerateFileSystemEntries(path).FirstOrDefault(e => Path.GetFileName(e).Equals(part, StringComparison.OrdinalIgnoreCase))
                : null;
            path = existing ?? Path.Combine(path, part);
        }
        return path;
    }

    private static byte[] Inflate(byte[] data, int offset, int length)
    {
        using var deflate = new DeflateStream(new MemoryStream(data, offset, length), CompressionMode.Decompress);
        using var result = new MemoryStream();
        deflate.CopyTo(result);
        return result.ToArray();
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        int i = data.AsSpan(start).IndexOf(pattern);
        return i < 0 ? -1 : i + start;
    }

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
}

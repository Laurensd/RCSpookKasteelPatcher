using System.Security.Cryptography;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Disc;

/// <summary>Een bekende dump van de cd-rom.</summary>
/// <param name="ImageSha1">SHA-1 van het databestand zoals het is (de .bin of .iso).</param>
/// <param name="DataSha1">SHA-1 van de 2048 bytes gebruikersdata van elke sector binnen het ISO 9660-volume. Die is
/// hetzelfde voor een .bin (2352), .iso (2048) of een rip met meer of minder lege sectoren na het volume.</param>
public sealed record KnownDisc(string Name, string ImageSha1, string DataSha1);

public enum DiscStatus
{
    /// <summary>De image is een bekende dump.</summary>
    Recognized,

    /// <summary>Onbekende image (andere rip), maar de spelbestanden die de patcher nodig heeft kloppen.</summary>
    FilesOk,

    /// <summary>Spelbestanden ontbreken of wijken af, of de image is onvolledig: waarschijnlijk een slechte rip.</summary>
    Suspicious,
}

public sealed record DiscCheck(DiscStatus Status, KnownDisc? Disc, string ImageSha1, string DataSha1, IReadOnlyList<string> Problems)
{
    /// <summary>De melding voor de gebruiker, regel voor regel.</summary>
    public IEnumerable<string> Report()
    {
        switch (Status)
        {
            case DiscStatus.Recognized:
                yield return $"Disc herkend: {Disc!.Name}";
                break;
            case DiscStatus.FilesOk:
                yield return $"Onbekende rip (SHA-1 {DataSha1}); de bestanden die de patcher aanpast kloppen wel.";
                break;
            default:
                yield return $"WAARSCHUWING: deze disc-image wijkt af van de bekende cd-rom (SHA-1 {DataSha1}):";
                foreach (var problem in Problems) yield return $"  - {problem}";
                yield return "Waarschijnlijk is de rip beschadigd of onvolledig. Maak de image opnieuw, of meld hem op";
                yield return $"{GameVersion.IssuesUrl} als je zeker weet dat hij goed is.";
                break;
        }
    }
}

/// <summary>
/// Controleert een disc-image tegen bekende hashes, zodat een slechte rip meteen een duidelijke melding geeft in plaats
/// van een vage fout halverwege. Een onbekende image is geen fout: andere rips van dezelfde cd bestaan ook.
/// </summary>
public static class DiscVerifier
{
    public static readonly KnownDisc[] KnownDiscs =
    [
        new("v1.0.0 (cd-rom, oktober 2000)",
            ImageSha1: "5FE44A24722141D6A264CE3A613643E0583FA1A4",
            DataSha1: "04CD30E4FF3BAD58A6201ECEE06CAD3862A814A1"),
    ];

    /// <summary>Bestanden in de map Data die de patcher nodig heeft, met hun SHA-256 op de cd-rom.</summary>
    public static IEnumerable<(string File, string Sha256)> RequiredFiles =>
        GameVersion.CdFiles.Append(("RedCat.exe", "4C4DB9E6696D8F04BC876C0808D7C700893A909CB63584284DCFDAC29807AFA9"));

    public static DiscCheck Verify(DiscImage disc, Iso9660 iso, Action<long, long>? progress = null) =>
        Verify(disc, iso, KnownDiscs, RequiredFiles, progress);

    public static DiscCheck Verify(DiscImage disc, Iso9660 iso, IEnumerable<KnownDisc> known,
        IEnumerable<(string File, string Sha256)> requiredFiles, Action<long, long>? progress = null)
    {
        var problems = new List<string>();
        long volume = iso.VolumeSectors > 0 ? iso.VolumeSectors : disc.SectorCount;
        if (disc.SectorCount < volume)
            problems.Add($"De image is onvolledig: {disc.SectorCount} van de {volume} sectoren.");

        var (imageSha1, dataSha1) = HashImage(disc, volume, progress);
        var match = known.FirstOrDefault(k =>
            k.ImageSha1.Equals(imageSha1, StringComparison.OrdinalIgnoreCase) ||
            k.DataSha1.Equals(dataSha1, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            return new DiscCheck(DiscStatus.Recognized, match, imageSha1, dataSha1, problems);

        foreach (var (file, expected) in requiredFiles)
        {
            var entry = iso.Find("Data/" + file);
            if (entry == null || entry.IsDirectory) { problems.Add($"{file} ontbreekt in de map Data."); continue; }
            string actual;
            try { actual = HashFile(iso, entry); }
            catch (EndOfStreamException) { problems.Add($"{file} valt (deels) buiten de image."); continue; }
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{file} wijkt af ({entry.Size} bytes, SHA-256 {actual}).");
        }
        return new DiscCheck(problems.Count == 0 ? DiscStatus.FilesOk : DiscStatus.Suspicious, null, imageSha1, dataSha1, problems);
    }

    /// <summary>Hasht in één keer het hele databestand en de gebruikersdata van de sectoren binnen het volume.</summary>
    private static (string ImageSha1, string DataSha1) HashImage(DiscImage disc, long volumeSectors, Action<long, long>? progress)
    {
        using var image = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var data = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var stream = new FileStream(disc.DataFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);

        int size = disc.SectorSize;
        var buffer = new byte[size * 512];
        long total = stream.Length, done = 0, sector = 0;
        int read;
        while ((read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)) > 0)
        {
            image.AppendData(buffer, 0, read);
            for (int o = 0; o + size <= read && sector < volumeSectors; o += size, sector++)
            {
                var raw = buffer.AsSpan(o, size);
                data.AppendData(raw.Slice(disc.UserDataOffset(raw), DiscImage.SectorDataSize));
            }
            done += read;
            progress?.Invoke(done, total);
        }
        return (Convert.ToHexString(image.GetHashAndReset()), Convert.ToHexString(data.GetHashAndReset()));
    }

    private static string HashFile(Iso9660 iso, IsoEntry file)
    {
        using var buffer = new MemoryStream((int)file.Size);
        iso.CopyFile(file, buffer);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
    }
}

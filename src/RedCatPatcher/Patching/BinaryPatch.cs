namespace RedCatPatcher.Patching;

/// <summary>Eén zoek-en-vervang in een bestand. Het zoekpatroon moet precies één keer voorkomen.</summary>
public sealed record Replacement(byte[] Find, byte[] Replace);

/// <summary>
/// Een benoemde fix op één spelbestand, opgebouwd uit één of meer vervangingen.
/// Vervangingen mogen de lengte veranderen, maar de bestandslengte moet na afloop gelijk zijn
/// (zodat resource- en sectie-offsets in het PE-bestand blijven kloppen).
/// </summary>
public sealed record BinaryPatch(string Id, string File, string Description, IReadOnlyList<Replacement> Replacements)
{
    public PatchResult Apply(string gameDir)
    {
        var path = Path.Combine(gameDir, File);
        if (!System.IO.File.Exists(path))
            throw new PatchException($"{File} niet gevonden in de spelmap.");

        var data = System.IO.File.ReadAllBytes(path);
        int originalLength = data.Length;

        if (Replacements.All(r => Count(data, r.Replace) == 1 && Count(data, r.Find) == 0))
            return PatchResult.AlreadyApplied;

        foreach (var r in Replacements)
        {
            int count = Count(data, r.Find);
            if (count != 1)
                throw new PatchException($"Patch '{Id}': patroon {count}x gevonden in {File} (verwacht 1x). Is dit wel de originele RedCat Spookkasteel v1.0.0?");
            int at = IndexOf(data, r.Find, 0);
            data = [.. data.AsSpan(0, at), .. r.Replace, .. data.AsSpan(at + r.Find.Length)];
        }

        if (data.Length != originalLength)
            throw new PatchException($"Patch '{Id}': bestandslengte van {File} zou veranderen; patch afgebroken.");

        System.IO.File.WriteAllBytes(path, data);
        return PatchResult.Applied;
    }

    private static int Count(byte[] data, byte[] pattern)
    {
        int n = 0;
        for (int i = IndexOf(data, pattern, 0); i >= 0; i = IndexOf(data, pattern, i + 1)) n++;
        return n;
    }

    private static int IndexOf(byte[] data, byte[] pattern, int start)
    {
        int i = data.AsSpan(start).IndexOf(pattern);
        return i < 0 ? -1 : i + start;
    }
}

public enum PatchResult { Applied, AlreadyApplied }

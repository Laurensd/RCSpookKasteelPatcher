namespace RedCatPatcher.Patching;

/// <summary>Eén zoek-en-vervang in een bestand. Het zoekpatroon moet precies één keer voorkomen.</summary>
public sealed record Replacement(byte[] Find, byte[] Replace);

/// <summary>
/// Een benoemde fix op één spelbestand, opgebouwd uit één of meer vervangingen.
/// Vervangingen mogen de lengte veranderen, maar de bestandslengte moet na afloop gelijk zijn
/// (zodat resource- en sectie-offsets in het PE-bestand blijven kloppen).
/// Voor RcHcGame.dat uit de XP-update (een andere build) staan aparte vervangingen in <paramref name="Xp2004"/>;
/// zonder die lijst gelden <paramref name="Replacements"/> voor elke build (het bestand verschilt dan niet).
/// </summary>
public sealed record BinaryPatch(string Id, string File, string Description, IReadOnlyList<Replacement> Replacements,
    IReadOnlyList<Replacement>? Xp2004 = null)
{
    public IReadOnlyList<Replacement> For(ExeBuild build) => build == ExeBuild.Xp2004 && Xp2004 != null ? Xp2004 : Replacements;

    public PatchResult Apply(string gameDir, ExeBuild build = ExeBuild.Original)
    {
        var replacements = For(build);
        var path = Path.Combine(gameDir, File);
        if (!System.IO.File.Exists(path))
            throw new PatchException($"{File} niet gevonden in de spelmap.");

        var data = System.IO.File.ReadAllBytes(path);
        int originalLength = data.Length;

        if (replacements.All(r => Count(data, r.Replace) == 1 && Count(data, r.Find) == 0))
            return PatchResult.AlreadyApplied;

        foreach (var r in replacements)
        {
            int count = Count(data, r.Find);
            if (count != 1)
                throw new PatchException($"Patch '{Id}': patroon {count}x gevonden in {File} (verwacht 1x).");
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

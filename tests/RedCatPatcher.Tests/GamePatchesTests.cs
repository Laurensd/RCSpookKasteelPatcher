using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Controleert de patch-definities zelf; de echte spelbestanden zitten niet in de repo.</summary>
public class GamePatchesTests
{
    public static TheoryData<string> PatchIds => new(GamePatches.All.Select(p => p.Id));

    [Fact]
    public void PatchIds_AreUnique()
    {
        Assert.Equal(GamePatches.All.Count, GamePatches.All.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(PatchIds))]
    public void Patch_IsWellFormed(string id)
    {
        var patch = GamePatches.All.Single(p => p.Id == id);
        Assert.Contains(patch.File, new[] { "RcHcGame.dat", "RcHcMenu.dat" });
        Assert.NotEmpty(patch.Replacements);
        foreach (var r in patch.Replacements)
        {
            Assert.NotEmpty(r.Find);
            Assert.False(r.Find.AsSpan().SequenceEqual(r.Replace), "find en replace zijn gelijk");
        }
        // Netto lengte moet gelijk blijven, anders weigert BinaryPatch de patch.
        Assert.Equal(0, patch.Replacements.Sum(r => r.Replace.Length - r.Find.Length));
    }

    [Theory]
    [MemberData(nameof(PatchIds))]
    public void Patch_AppliesToSyntheticFile_AndIsIdempotent(string id)
    {
        var patch = GamePatches.All.Single(p => p.Id == id);
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            // Bestand met alle zoekpatronen tussen opvulbytes, in volgorde
            var content = new List<byte>(new byte[64]);
            foreach (var r in patch.Replacements) { content.AddRange(r.Find); content.AddRange(new byte[64]); }
            File.WriteAllBytes(Path.Combine(dir, patch.File), content.ToArray());

            Assert.Equal(PatchResult.Applied, patch.Apply(dir));
            Assert.Equal(content.Count, new FileInfo(Path.Combine(dir, patch.File)).Length);
            Assert.Equal(PatchResult.AlreadyApplied, patch.Apply(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DialogCenterPatch_ProducesValidDelphiIdentifiers()
    {
        var patch = GamePatches.All.Single(p => p.Id == "dialog-center");
        var position = patch.Replacements[1].Replace;
        // \x08Position \x07 (vaIdent) \x0E poScreenCenter
        Assert.Equal(8, position[0]);
        Assert.Equal("Position", System.Text.Encoding.ASCII.GetString(position, 1, 8));
        Assert.Equal(0x07, position[9]);
        Assert.Equal(14, position[10]);
        Assert.Equal("poScreenCenter", System.Text.Encoding.ASCII.GetString(position, 11, 14));
    }
}

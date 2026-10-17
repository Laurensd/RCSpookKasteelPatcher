using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Controleert de patch-definities zelf; de echte spelbestanden zitten niet in de repo.</summary>
public class GamePatchesTests
{
    public static TheoryData<string> PatchIds => new(GamePatches.All.Select(p => p.Id));

    public static TheoryData<string, ExeBuild> PatchIdsPerBuild
    {
        get
        {
            var data = new TheoryData<string, ExeBuild>();
            foreach (var p in GamePatches.All)
                foreach (var b in Enum.GetValues<ExeBuild>()) data.Add(p.Id, b);
            return data;
        }
    }

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
        // Alleen RcHcGame.dat verschilt tussen de builds.
        if (patch.Xp2004 != null) Assert.Equal("RcHcGame.dat", patch.File);
        foreach (var build in Enum.GetValues<ExeBuild>())
        {
            var replacements = patch.For(build);
            Assert.NotEmpty(replacements);
            foreach (var r in replacements)
            {
                Assert.NotEmpty(r.Find);
                Assert.False(r.Find.AsSpan().SequenceEqual(r.Replace), "find en replace zijn gelijk");
            }
            // Netto lengte moet gelijk blijven, anders weigert BinaryPatch de patch.
            Assert.Equal(0, replacements.Sum(r => r.Replace.Length - r.Find.Length));
        }
    }

    [Fact]
    public void RcHcGamePatches_HaveXp2004Variant()
    {
        Assert.All(GamePatches.All.Where(p => p.File == "RcHcGame.dat"), p => Assert.NotNull(p.Xp2004));
        Assert.All(Widescreen.Wide16x9.Patches.Where(p => p.File == "RcHcGame.dat"), p => Assert.NotNull(p.Xp2004));
    }

    [Theory]
    [MemberData(nameof(PatchIdsPerBuild))]
    public void Patch_AppliesToSyntheticFile_AndIsIdempotent(string id, ExeBuild build)
    {
        var patch = GamePatches.All.Single(p => p.Id == id);
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            // Bestand met alle zoekpatronen tussen opvulbytes, in volgorde
            var content = new List<byte>(new byte[64]);
            foreach (var r in patch.For(build)) { content.AddRange(r.Find); content.AddRange(new byte[64]); }
            File.WriteAllBytes(Path.Combine(dir, patch.File), content.ToArray());

            Assert.Equal(PatchResult.Applied, patch.Apply(dir, build));
            Assert.Equal(content.Count, new FileInfo(Path.Combine(dir, patch.File)).Length);
            Assert.Equal(PatchResult.AlreadyApplied, patch.Apply(dir, build));
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

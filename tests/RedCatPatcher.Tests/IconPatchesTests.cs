using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Taakbalkiconen: LoadIconA krijgt het eigen icoon van het bestand in plaats van een standaard-id.</summary>
public class IconPatchesTests
{
    [Theory]
    [InlineData(ExeBuild.Original)]
    [InlineData(ExeBuild.Xp2004)]
    public void GameIcon_LoadsIconGroup107FromHInstance(ExeBuild build)
    {
        var r = GamePatches.All.Single(p => p.Id == "game-icon").For(build).Single();
        Assert.Equal(new byte[] { 0x68, 0x00, 0x7f, 0x00, 0x00, 0x53 }, r.Find[..6]);   // push IDI_APPLICATION; push ebx (NULL)
        Assert.Equal(new byte[] { 0x6a, 107, 0x57, 0x90, 0x90, 0x90 }, r.Replace[..6]); // push 107; push edi (hInstance)
        Assert.Equal(r.Find[6..], r.Replace[6..]);
    }

    [Fact]
    public void RedCatIcon_LoadsIcon101ForBigAndSmallIcon()
    {
        var r = GamePatches.All.Single(p => p.Id == "redcat-icon").Replacements.Single();
        var diff = Enumerable.Range(0, r.Find.Length).Where(i => r.Find[i] != r.Replace[i]).ToList();
        Assert.Equal([0, 1, 2, 3, 4, 12, 13, 14, 15, 16], diff);
        Assert.Equal(new byte[] { 0x6a, 101, 0x90, 0x90, 0x90, 0x56 }, r.Replace[..6]);   // push 101; push esi (hInstance)
        Assert.Equal(r.Replace[..6], r.Replace[12..18]);
    }
}

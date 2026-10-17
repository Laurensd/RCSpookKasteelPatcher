using RedCatPatcher;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

public sealed class BinaryPatchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static BinaryPatch Patch(params (byte[] Find, byte[] Replace)[] parts) =>
        new("test", "game.dat", "test", parts.Select(p => new Replacement(p.Find, p.Replace)).ToList());

    [Fact]
    public void Apply_ReplacesPatternOnce_ThenReportsAlreadyApplied()
    {
        var path = WriteFile("game.dat", [0, 1, 2, 3, 4, 5]);
        var patch = Patch(([2, 3], [9, 9]));

        Assert.Equal(PatchResult.Applied, patch.Apply(_dir));
        Assert.Equal(new byte[] { 0, 1, 9, 9, 4, 5 }, File.ReadAllBytes(path));
        Assert.Equal(PatchResult.AlreadyApplied, patch.Apply(_dir));
    }

    [Fact]
    public void Apply_PatternMissing_Throws()
    {
        WriteFile("game.dat", [0, 1, 2, 3]);
        Assert.Throws<PatchException>(() => Patch(([7, 7], [8, 8])).Apply(_dir));
    }

    [Fact]
    public void Apply_PatternAmbiguous_ThrowsAndLeavesFileUntouched()
    {
        var path = WriteFile("game.dat", [1, 2, 0, 1, 2]);
        Assert.Throws<PatchException>(() => Patch(([1, 2], [3, 4])).Apply(_dir));
        Assert.Equal(new byte[] { 1, 2, 0, 1, 2 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_LengthChangesThatCancelOut_AreAllowed()
    {
        var path = WriteFile("game.dat", [0xAA, 1, 2, 0xBB, 3, 4, 5, 0xCC]);
        // eerste vervanging +1 byte, tweede -1 byte: totale lengte gelijk
        var patch = Patch(([1, 2], [1, 2, 0]), ([3, 4, 5], [6, 7]));

        Assert.Equal(PatchResult.Applied, patch.Apply(_dir));
        Assert.Equal(new byte[] { 0xAA, 1, 2, 0, 0xBB, 6, 7, 0xCC }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_NetLengthChange_ThrowsAndLeavesFileUntouched()
    {
        var path = WriteFile("game.dat", [0, 1, 2, 3]);
        Assert.Throws<PatchException>(() => Patch(([1, 2], [1, 2, 0])).Apply(_dir));
        Assert.Equal(new byte[] { 0, 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_InMemory_PatchesInPlace_AndLeavesDataUntouchedOnError()
    {
        byte[] data = [0xAA, 1, 2, 0xBB, 3, 4, 5, 0xCC];
        Assert.Equal(PatchResult.Applied, Patch(([1, 2], [1, 2, 0]), ([3, 4, 5], [6, 7])).Apply(data));
        Assert.Equal(new byte[] { 0xAA, 1, 2, 0, 0xBB, 6, 7, 0xCC }, data);

        byte[] other = [1, 2, 3, 9];
        // tweede vervanging ontbreekt: ook de eerste mag niet in de data terechtkomen
        Assert.Throws<PatchException>(() => Patch(([1, 2], [5, 5]), ([7, 7], [8, 8])).Apply(other));
        Assert.Equal(new byte[] { 1, 2, 3, 9 }, other);
    }

    [Fact]
    public void Apply_UsesXp2004Replacements_OnlyForThatBuild()
    {
        var patch = new BinaryPatch("test", "game.dat", "test", [new([1, 2], [3, 4])], Xp2004: [new([5, 6], [7, 8])]);

        var path = WriteFile("game.dat", [0, 1, 2, 0]);
        Assert.Equal(PatchResult.Applied, patch.Apply(_dir, ExeBuild.Original));
        Assert.Equal(new byte[] { 0, 3, 4, 0 }, File.ReadAllBytes(path));

        WriteFile("game.dat", [0, 5, 6, 0]);
        Assert.Equal(PatchResult.Applied, patch.Apply(_dir, ExeBuild.Xp2004));
        Assert.Equal(new byte[] { 0, 7, 8, 0 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_WithoutXp2004Replacements_UsesDefaultForEveryBuild()
    {
        var path = WriteFile("game.dat", [0, 1, 2, 0]);
        Assert.Equal(PatchResult.Applied, Patch(([1, 2], [3, 4])).Apply(_dir, ExeBuild.Xp2004));
        Assert.Equal(new byte[] { 0, 3, 4, 0 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void Apply_MissingFile_Throws()
    {
        Assert.Throws<PatchException>(() => Patch(([1], [2])).Apply(_dir));
    }
}

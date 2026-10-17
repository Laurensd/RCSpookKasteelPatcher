using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Logbestanden van spel en menu uit (standaard), en opruimen bij herinstallatie.</summary>
public sealed class DebugLogsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static TheoryData<string> Versions => new() { "1.0.0", "1.0.1", "xp" };

    private static GameVersion Version(string name) => name switch
    {
        "1.0.0" => GameVersion.V100,
        "1.0.1" => GameVersion.V101,
        _ => GameVersion.Xp,
    };

    [Theory]
    [MemberData(nameof(Versions))]
    public void Patches_ApplyToSyntheticFiles_AndAreIdempotent(string name)
    {
        var version = Version(name);
        var patches = DebugLogs.PatchesFor(version);
        Assert.Equal(["RcHcGame.dat", "GlideDrv.dll", "RcHcMenu.dat"], patches.Select(p => p.File));
        foreach (var patch in patches)
        {
            var content = new List<byte>(new byte[64]);
            foreach (var r in patch.For(version.Build))
            {
                Assert.Equal(r.Find.Length, r.Replace.Length);
                content.AddRange(r.Find);
                content.AddRange(new byte[64]);
            }
            File.WriteAllBytes(Path.Combine(_dir, patch.File), content.ToArray());
        }

        Assert.All(patches, p => Assert.Equal(PatchResult.Applied, p.Apply(_dir, version.Build)));
        Assert.All(patches, p => Assert.Equal(PatchResult.AlreadyApplied, p.Apply(_dir, version.Build)));
    }

    [Fact]
    public void Patches_DifferPerVersion()
    {
        // 1.0.0 en 1.0.1 delen de build, maar niet de adressen in de patronen.
        var game100 = DebugLogs.PatchesFor(GameVersion.V100)[0].For(ExeBuild.Original);
        var game101 = DebugLogs.PatchesFor(GameVersion.V101)[0].For(ExeBuild.Original);
        var gameXp = DebugLogs.PatchesFor(GameVersion.Xp)[0].For(ExeBuild.Xp2004);
        Assert.All(game100.Zip(game101, gameXp), t =>
        {
            Assert.NotEqual(t.First.Find, t.Second.Find);
            Assert.NotEqual(t.First.Find, t.Third.Find);
        });
    }

    [Fact]
    public void Clean_RemovesLogsOnly()
    {
        var levels = Directory.CreateDirectory(Path.Combine(_dir, "Levels")).FullName;
        string[] logs =
        [
            "AdamDebug01.log", "AdamDebug05.log", "AdamError01.log", "Sound.log", "GCache.Log", "MenuTrac.log",
            "RcHcMenu.log", Path.Combine("Levels", "lvl00a.log"), Path.Combine("Levels", "lvl01a.LOG"),
        ];
        string[] keep = ["AdamWin.ini", "SaveGame1.RCR", "RcHcGame.dat", Path.Combine("Levels", "lvl00a.bsp"), "leesmij.txt"];
        foreach (var f in logs.Concat(keep)) File.WriteAllText(Path.Combine(_dir, f), "x");

        Assert.Equal(logs.Length, DebugLogs.Clean(_dir));
        Assert.All(logs, f => Assert.False(File.Exists(Path.Combine(_dir, f)), f));
        Assert.All(keep, f => Assert.True(File.Exists(Path.Combine(_dir, f)), f));
        Assert.Equal(0, DebugLogs.Clean(_dir));
        Assert.True(Directory.Exists(levels));
    }

    [Fact]
    public void Clean_MissingFolder_DoesNothing() =>
        Assert.Equal(0, DebugLogs.Clean(Path.Combine(_dir, "bestaat-niet")));
}

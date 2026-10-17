using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>--controller: de joysticktabel in RcHcGame.dat en de extra toetsen in PlayerDef.ini.</summary>
public class ControllerTests
{
    /// <summary>De 14 aanroepen zoals ze in alle drie de builds staan, met een build-afhankelijke call-offset.</summary>
    private static byte[] Table(byte[] calloffset)
    {
        int[] order = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 0, 2, 1, 3];
        var bytes = new List<byte>();
        foreach (var index in order)
            bytes.AddRange([0x6a, Controller.Original[index], 0x6a, (byte)index, 0x8b, 0xce, 0xe8, .. calloffset]);
        return [.. bytes];
    }

    private static byte[] GameFile(byte[] calloffset) =>
        [.. new byte[100], 0x6a, 0x00, 0x6a, 0x04, 0x8b, 0xce, 0xe8, .. new byte[50], .. Table(calloffset), .. new byte[100]];

    private static string MakeGame(byte[] game)
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        File.WriteAllBytes(Path.Combine(dir, "RcHcGame.dat"), game);
        var settings = Directory.CreateDirectory(Path.Combine(dir, "settings")).FullName;
        File.WriteAllText(Path.Combine(settings, "PlayerDef.ini"),
            "[KeyBinds]\r\nFire=Control\r\nWeaponNext=]\r\nWeaponPrev=[\r\nMenu=Escape\r\n");
        return dir;
    }

    [Theory]
    [InlineData(new byte[] { 0x67, 0x01, 0x00, 0x00 })]   // 1.0.0 en 1.0.1
    [InlineData(new byte[] { 0x68, 0x1f, 0xd9, 0xff })]   // XP-build
    public void Apply_RewritesTable_AndIsIdempotent(byte[] calloffset)
    {
        var original = GameFile(calloffset);
        var dir = MakeGame(original);
        try
        {
            Assert.Equal(Controller.Original, Controller.ReadTable(original));

            Assert.Equal(PatchResult.Applied, Controller.Apply(dir, wasd: false));
            var patched = File.ReadAllBytes(Path.Combine(dir, "RcHcGame.dat"));
            Assert.Equal(original.Length, patched.Length);
            Assert.Equal(Controller.Mapping(wasd: false, buttons: true), Controller.ReadTable(patched));
            // Alleen de vk-bytes veranderen; call-offsets en de rest blijven gelijk
            Assert.Equal(7, original.Zip(patched).Count(p => p.First != p.Second));

            Assert.Equal(PatchResult.AlreadyApplied, Controller.Apply(dir, wasd: false));
            Assert.Equal("[KeyBinds]\r\nFire=Control\r\nWeaponNext=],F6\r\nWeaponPrev=[,F5\r\nMenu=Escape\r\n",
                File.ReadAllText(Path.Combine(dir, "settings", "PlayerDef.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Mapping_StickFollowsWasd_ButtonsOnlyWithController()
    {
        var wasdOnly = Controller.Mapping(wasd: true, buttons: false);
        Assert.Equal("ADWS"u8.ToArray(), wasdOnly[..4]);
        Assert.Equal(Controller.Original[4..], wasdOnly[4..]);

        var full = Controller.Mapping(wasd: false, buttons: true);
        Assert.Equal(Controller.Original[..4], full[..4]);
        Assert.Equal(new byte[] { 0x20, 0x12, 0x11, 0x0D, 0x74, 0x75, 0x09, 0x1B, 0x2E, 0 }, full[4..]);
        Assert.All(full, vk => Assert.True(vk < 0x80));
    }

    [Fact]
    public void Apply_WasdOnly_LeavesPlayerDefAlone()
    {
        var dir = MakeGame(GameFile([0x67, 0x01, 0x00, 0x00]));
        try
        {
            var before = File.ReadAllText(Path.Combine(dir, "settings", "PlayerDef.ini"));
            Controller.Apply(dir, wasd: true, buttons: false);
            Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "settings", "PlayerDef.ini")));
            Assert.Equal(Controller.Mapping(true, false), Controller.ReadTable(File.ReadAllBytes(Path.Combine(dir, "RcHcGame.dat"))));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadTable_FailsWhenTableMissingOrDuplicated()
    {
        Assert.Throws<PatchException>(() => Controller.ReadTable(new byte[500]));
        var table = Table([0, 0, 0, 0]);
        Assert.Throws<PatchException>(() => Controller.ReadTable([.. table, .. new byte[10], .. table]));
    }
}

using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>
/// Opbouwen uit de basis (docs/LAUNCHER.md, "Herhaalbaar toepassen"): elke keuze, vanaf elke vorige keuze, geeft
/// dezelfde bestanden als een verse installatie met die keuze. Met nagemaakte spelbestanden die alle zoekpatronen bevatten.
/// </summary>
public sealed class BasisTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly GameVersion[] Versions = [GameVersion.V100, GameVersion.V101, GameVersion.Xp];
    private static readonly Widescreen?[] Aspects = [null, Widescreen.Wide16x9, Widescreen.Wide16x10];

    /// <summary>Alle 24 combinaties van breedbeeld, WASD, controller en logs (beeldopties wisselend).</summary>
    private static IEnumerable<Choice> AllChoices()
    {
        int i = 0;
        foreach (var wide in Aspects)
            foreach (var wasd in new[] { false, true })
                foreach (var controller in new[] { false, true })
                    foreach (var logs in new[] { false, true })
                        yield return new Choice
                        {
                            Widescreen = wide, Wasd = wasd, Controller = controller, DebugLogs = logs,
                            Display = (i++ % 3) switch
                            {
                                0 => new DisplayOptions(),
                                1 => new DisplayOptions { Mode = DisplayMode.Window, FpsLimit = 60, Antialiasing = "off" },
                                _ => new DisplayOptions { Mode = DisplayMode.Fullscreen, Resolution = "3x", VSync = false, Crt = true },
                            },
                        };
    }

    public static TheoryData<int, int> VersionAndChoice
    {
        get
        {
            var data = new TheoryData<int, int>();
            for (int v = 0; v < Versions.Length; v++)
                for (int c = 0; c < AllChoices().Count(); c++) data.Add(v, c);
            return data;
        }
    }

    /// <summary>Basisbestanden met alle zoekpatronen voor deze versie, tussen opvulbytes, plus de joysticktabel.</summary>
    private static Dictionary<string, byte[]> SyntheticBasis(GameVersion version)
    {
        var patches = GamePatches.All.Concat(DebugLogs.PatchesFor(version)).Concat(Widescreen.Wide16x9.Patches);
        var files = new Dictionary<string, byte[]>();
        foreach (var file in Basis.BinaryFiles)
        {
            var content = new List<byte>(new byte[64]);
            var finds = patches.Where(p => p.File == file).SelectMany(p => p.For(version.Build)).Select(r => r.Find);
            foreach (var find in finds.DistinctBy(Convert.ToHexString)) { content.AddRange(find); content.AddRange(new byte[64]); }
            if (file == Controller.File) content.AddRange([.. ControllerTable(), .. new byte[64], .. JoystickSearchTests.Probe([0x50, 0xa2, 0x64, 0x00])]);
            content.AddRange(new byte[64]);
            files[file] = [.. content];
        }
        return files;
    }

    private static byte[] ControllerTable()
    {
        int[] order = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 0, 2, 1, 3];
        return [.. order.SelectMany(i => new byte[] { 0x6a, Controller.Original[i], 0x6a, (byte)i, 0x8b, 0xce, 0xe8, 0x67, 0x01, 0, 0 })];
    }

    private static readonly Dictionary<string, string> BasisInis = new()
    {
        ["AdamWin.ini"] = "[Camera-LevelCamera]\r\nFieldOfView=1.360000\r\n[Camera-PlayerCamera]\r\nFieldOfView=1.360000\r\n[Debug]\r\nDebugDriver=0\r\nDebugLoading=0\r\nDebugPlayerPosition=0\r\n",
        [Path.Combine("settings", "static.ini")] = "[DEFAULTSETTINGS]\r\nLeft=37\r\nRight=39\r\nUp=38\r\nDown=40\r\nExtra1=17\r\n",
        [Path.Combine("settings", "PlayerDef.ini")] =
            "[KeyBinds]\r\nFire=Control\r\nMoveForward=Up\r\nMoveBack=Down\r\nTurnLeft=Left\r\nTurnRight=Right\r\n" +
            "MoveLeft=Alt+Left\r\nMoveRight=Alt+Right\r\nWeaponNext=]\r\nWeaponPrev=[\r\n",
        [Path.Combine("settings", "HUDIcon.ini")] = "[Icon0]\r\nX=9\r\n[Icon3]\r\nX=510\r\n[Icon4]\r\nX=320\r\n",
    };

    private const string DgVoodooConf =
        "[General]\r\nFullScreenMode = true\r\nScalingMode = stretched_ar\r\nCenterAppWindow = true\r\n" +
        "[GeneralExt]\r\nFullscreenAttributes = fake\r\nFPSLimit = 0\r\nResampling = bilinear\r\n" +
        "[Glide]\r\nResolution = max\r\nAntialiasing = 4x\r\nForceVerticalSync = true\r\n" +
        "[DirectX]\r\nResolution = unforced\r\n";

    /// <summary>Een spelmap zoals de patcher hem achterlaat vlak vóór de fixes, met de basis al bewaard.</summary>
    private string MakeGame(GameVersion version, string name = "game")
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "settings"));
        foreach (var (file, data) in SyntheticBasis(version)) File.WriteAllBytes(Path.Combine(dir, file), data);
        foreach (var (file, text) in BasisInis) File.WriteAllText(Path.Combine(dir, file), text);
        File.WriteAllText(Path.Combine(dir, "dgVoodoo.conf"), DgVoodooConf);
        Basis.Save(dir);
        return dir;
    }

    private static void Apply(string dir, Choice choice, GameVersion version) => Basis.Apply(dir, choice, null, _ => version);

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Basis.BinaryFiles.Concat(Basis.IniFiles).Append("dgVoodoo.conf")
            .ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(dir, f)));

    [Theory]
    [MemberData(nameof(VersionAndChoice))]
    public void Build_AppliesExactlyTheChosenPatches(int v, int c)
    {
        var version = Versions[v];
        var choice = AllChoices().ElementAt(c);
        var basis = SyntheticBasis(version);
        var copy = basis.ToDictionary(e => e.Key, e => (byte[])e.Value.Clone());

        var built = Basis.Build(basis, version, choice);

        Assert.Equal(copy, basis);   // de basis zelf blijft ongewijzigd
        Assert.Equal(built, Basis.Build(basis, version, choice));
        Assert.All(Basis.BinaryFiles, f => Assert.Equal(basis[f].Length, built[f].Length));

        bool Applied(BinaryPatch p) => p.For(version.Build).All(r => built[p.File].AsSpan().IndexOf(r.Replace) >= 0)
            && p.For(version.Build).All(r => built[p.File].AsSpan().IndexOf(r.Find) < 0);
        Assert.All(GamePatches.All, p => Assert.True(Applied(p), p.Id));
        Assert.All(DebugLogs.PatchesFor(version), p => Assert.Equal(!choice.DebugLogs, Applied(p)));
        // Sommige breedbeeldpatches (widescreen-fov) zijn voor 16:9 en 16:10 gelijk.
        bool Chosen(BinaryPatch p) => choice.Widescreen?.Patches.Single(q => q.Id == p.Id).For(version.Build)
            .SequenceEqual(p.For(version.Build), ReplacementComparer) ?? false;
        foreach (var wide in Aspects.OfType<Widescreen>())
            Assert.All(wide.Patches, p => Assert.Equal(Chosen(p), Applied(p)));
        Assert.Equal(choice.Wasd || choice.Controller ? Controller.Mapping(choice.Wasd, choice.Controller) : Controller.Original,
            Controller.ReadTable(built[Controller.File]));
    }

    private static readonly IEqualityComparer<Replacement> ReplacementComparer = EqualityComparer<Replacement>.Create(
        (x, y) => x!.Find.AsSpan().SequenceEqual(y!.Find) && x.Replace.AsSpan().SequenceEqual(y.Replace),
        r => r.Find.Length);

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Apply_FromAnyPreviousChoice_EqualsFreshInstall(int v)
    {
        var version = Versions[v];
        var choices = AllChoices().ToList();
        var fresh = choices.Select((choice, i) =>
        {
            var dir = MakeGame(version, $"fresh{i}");
            Apply(dir, choice, version);
            return Snapshot(dir);
        }).ToList();

        // Eén map gaat alle keuzes langs, in een volgorde die elke overgang tussen beeldverhoudingen bevat.
        var game = MakeGame(version);
        foreach (var i in Enumerable.Range(0, choices.Count).Concat(Enumerable.Range(0, choices.Count).Reverse()).Concat([5, 17, 2, 23, 0]))
        {
            Apply(game, choices[i], version);
            Apply(game, choices[i], version);   // nog een keer: verandert niets
            Assert.Equal(fresh[i], Snapshot(game));
        }
    }

    /// <summary>
    /// Zoals de launcher bij Play: de keuze met de logs aan voor een [Debug]-schakelaar, dan de schakelaars zelf. Ook met
    /// het tabblad Testing geeft elke overgang hetzelfde als een verse installatie.
    /// </summary>
    [Fact]
    public void Apply_WithTestOptions_FromAnyPrevious_EqualsFreshInstall()
    {
        var version = GameVersion.V100;
        var on = new TestOptions { StartLevel = 4 }.WithDebug("DebugDriver", true).WithDebug("DebugPlayerPosition", true);
        var steps = new (Choice Choice, TestOptions Testing)[]
        {
            (new Choice(), new TestOptions()),
            (new Choice(), on),
            (new Choice { Widescreen = Widescreen.Wide16x9, DebugLogs = true }, on),
            (new Choice { Widescreen = Widescreen.Wide16x9, DebugLogs = true }, new TestOptions().WithDebug("DebugLoading", true)),
            (new Choice { Wasd = true }, new TestOptions { StartLevel = 1 }),
        };
        void Play(string dir, Choice choice, TestOptions testing)
        {
            Apply(dir, testing.Effective(choice), version);
            testing.Apply(dir);
        }
        var fresh = steps.Select((s, i) =>
        {
            var dir = MakeGame(version, $"fresh{i}");
            Play(dir, s.Choice, s.Testing);
            return Snapshot(dir);
        }).ToList();

        var game = MakeGame(version);
        foreach (var i in new[] { 0, 1, 2, 3, 4, 3, 2, 1, 0, 4, 1, 3, 0 })
        {
            Play(game, steps[i].Choice, steps[i].Testing);
            Play(game, steps[i].Choice, steps[i].Testing);
            Assert.Equal(fresh[i], Snapshot(game));
        }

        // Een schakelaar aan geeft dezelfde binaire bestanden als logs aan zonder schakelaar; alleen AdamWin.ini verschilt.
        var logs = MakeGame(version, "logs");
        Apply(logs, new Choice { DebugLogs = true }, version);
        Assert.All(Basis.BinaryFiles, f => Assert.Equal(Snapshot(logs)[f], fresh[1][f]));
        Assert.Contains("DebugDriver=1\r\nDebugLoading=0\r\nDebugPlayerPosition=1\r\n", System.Text.Encoding.Latin1.GetString(fresh[1]["AdamWin.ini"]));
    }

    [Fact]
    public void Apply_DefaultChoice_RestoresBasisInis()
    {
        var game = MakeGame(GameVersion.V100);
        Apply(game, new Choice { Widescreen = Widescreen.Wide16x10, Wasd = true, Controller = true }, GameVersion.V100);
        Assert.Contains("WeaponPrev=[,F5", File.ReadAllText(Path.Combine(game, "settings", "PlayerDef.ini")));

        Apply(game, new Choice(), GameVersion.V100);
        Assert.All(BasisInis, e => Assert.Equal(e.Value, File.ReadAllText(Path.Combine(game, e.Key))));
        Assert.Equal(DgVoodooConf, File.ReadAllText(Path.Combine(game, "dgVoodoo.conf")));
    }

    [Fact]
    public void Apply_KeepsOtherIniChanges()
    {
        var game = MakeGame(GameVersion.V100);
        var playerDef = Path.Combine(game, "settings", "PlayerDef.ini");
        IniFile.Set(playerDef, "KeyBinds", "Fire", "Space");
        IniFile.Set(Path.Combine(game, "AdamWin.ini"), "Debug", "DebugDriver", "1");
        IniFile.Update(Path.Combine(game, "dgVoodoo.conf"), s => s == "DirectX", "Resolution", _ => " 2x");

        Apply(game, new Choice { Wasd = true, Controller = true }, GameVersion.V100);
        Apply(game, new Choice(), GameVersion.V100);

        Assert.Equal("Space", IniFile.Get(playerDef, "KeyBinds", "Fire"));
        Assert.Equal("1", IniFile.Get(Path.Combine(game, "AdamWin.ini"), "Debug", "DebugDriver"));
        Assert.Equal(" 2x", IniFile.Get(Path.Combine(game, "dgVoodoo.conf"), "DirectX", "Resolution"));
    }

    [Fact]
    public void Apply_WithoutBasisOrWithChangedBasis_Throws()
    {
        var game = MakeGame(GameVersion.V100);
        var sha = Basis.Sha256(Basis.DirOf(game));
        var before = Snapshot(game);

        File.AppendAllText(Path.Combine(Basis.DirOf(game), "AdamWin.ini"), "; anders\r\n");
        Assert.Throws<PatchException>(() => Basis.Apply(game, new Choice { Wasd = true }, sha, _ => GameVersion.V100));

        Directory.Delete(Path.Combine(game, "RedCatPatcher"), recursive: true);
        Assert.Throws<PatchException>(() => Apply(game, new Choice { Wasd = true }, GameVersion.V100));
        Assert.Equal(before, Snapshot(game));
    }

    [Fact]
    public void Apply_WhileGameFileIsInUse_ThrowsWithoutChanges()
    {
        if (!OperatingSystem.IsWindows()) return;   // alleen Windows zet een geopend bestand op slot
        var game = MakeGame(GameVersion.V100);
        var before = Snapshot(game);
        using (File.Open(Path.Combine(game, "RcHcMenu.dat"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<PatchException>(() => Apply(game, new Choice { Widescreen = Widescreen.Wide16x9, Wasd = true }, GameVersion.V100));
        Assert.Equal(before, Snapshot(game));
    }

    [Fact]
    public void Save_HashFollowsContent()
    {
        var game = MakeGame(GameVersion.V100);
        var basisDir = Basis.DirOf(game);
        Assert.All(Basis.BinaryFiles.Concat(Basis.IniFiles), f => Assert.True(File.Exists(Path.Combine(basisDir, f)), f));

        var sha = Basis.Sha256(basisDir);
        Assert.Equal(sha, Basis.Save(game));
        IniFile.Set(Path.Combine(game, "settings", "static.ini"), "DEFAULTSETTINGS", "Up", "87");
        Assert.NotEqual(sha, Basis.Save(game));
    }

    [Fact]
    public void LauncherIni_RoundTrips()
    {
        var game = MakeGame(GameVersion.V100);
        Assert.Null(LauncherIni.Read(game));

        var wanted = new Choice
        {
            Widescreen = Widescreen.Wide16x10, Controller = true, DebugLogs = true,
            Display = new DisplayOptions { Mode = DisplayMode.Window, Resolution = "1920x1080@60", RenderScale = 2, FpsLimit = 144, VSync = false, Antialiasing = "off", Crt = true },
        };
        var ini = new LauncherIni(wanted, new Choice(), "ABC123");
        ini.Write(game);

        Assert.Equal(ini, LauncherIni.Read(game));
        var text = File.ReadAllText(Path.Combine(game, LauncherIni.FileName));
        Assert.Contains("[Keuze]\r\nWidescreen=16:10\r\nWasd=0\r\nController=1\r\nDebugLogs=1\r\nDisplay=window\r\n", text);
        Assert.Contains("[Toegepast]\r\nWidescreen=4:3\r\n", text);
        Assert.Contains("Resolution=auto\r\n", text);
        Assert.EndsWith("BasisSha256=ABC123\r\nRevision=2\r\n", text);
    }

    [Fact]
    public void LauncherIni_UnknownValues_FallBackToDefaults()
    {
        var game = MakeGame(GameVersion.V100);
        File.WriteAllText(Path.Combine(game, LauncherIni.FileName),
            "[keuze]\r\nWidescreen=21:9\r\nWasd = 1 \r\nDisplay=Venster\r\nResolution=heel groot\r\nRenderScale=9\r\nFpsLimit=-5\r\nMsaa=32x\r\nVSync=misschien\r\n");

        var ini = LauncherIni.Read(game)!;
        Assert.Null(ini.Applied);
        Assert.Null(ini.BasisSha256);
        Assert.Equal(new Choice { Wasd = true, Display = new DisplayOptions { Mode = DisplayMode.Window } }, ini.Wanted);
    }

    [Fact]
    public void ExtraBinds_CanBeRemovedAgain()
    {
        var game = MakeGame(GameVersion.V100);
        var playerDef = Path.Combine(game, "settings", "PlayerDef.ini");
        IniFile.Set(playerDef, "KeyBinds", "WeaponNext", "],F6,Q");

        Controller.SetExtraBinds(game, true);
        Assert.Equal("],Q,F6", IniFile.Get(playerDef, "KeyBinds", "WeaponNext"));
        Assert.Equal("[,F5", IniFile.Get(playerDef, "KeyBinds", "WeaponPrev"));

        Controller.SetExtraBinds(game, false);
        Assert.Equal("],Q", IniFile.Get(playerDef, "KeyBinds", "WeaponNext"));
        Assert.Equal("[", IniFile.Get(playerDef, "KeyBinds", "WeaponPrev"));
    }

    [Fact]
    public void ApplyDisplay_WithoutDgVoodoo_DoesNothing()
    {
        var game = MakeGame(GameVersion.V100);
        File.Delete(Path.Combine(game, "dgVoodoo.conf"));
        Assert.False(DgVoodoo.ApplyDisplay(game, new DisplayOptions()));
        Apply(game, new Choice { Display = new DisplayOptions { Crt = true } }, GameVersion.V100);
        Assert.False(File.Exists(Path.Combine(game, "dgVoodoo.conf")));
    }
}

using System.Text;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Opt-in patches: --widescreen en --wasd.</summary>
public class OptionsPatchesTests
{
    public static TheoryData<string, ExeBuild> Aspects => new()
    {
        { "16:9", ExeBuild.Original }, { "16:10", ExeBuild.Original },
        { "16:9", ExeBuild.Xp2004 }, { "16:10", ExeBuild.Xp2004 },
    };

    [Theory]
    [InlineData("16:9", 856, 480, 856, 1.819f)]
    [InlineData("16:10", 640, 400, 768, 1.632f)]
    public void Widescreen_Values(string name, int width, int height, int hudWidth, float fov)
    {
        var wide = Widescreen.Parse(name)!;
        Assert.Equal((width, height), (wide.Width, wide.Height));
        Assert.Equal(hudWidth, wide.HudWidth);
        Assert.Equal(fov, wide.FieldOfView, 0.001f);
    }

    [Fact]
    public void Widescreen_RejectsUnknownAspect() => Assert.Null(Widescreen.Parse("4:3"));

    [Theory]
    [MemberData(nameof(Aspects))]
    public void WidescreenPatches_ApplyToSyntheticFiles_AndAreIdempotent(string aspect, ExeBuild build)
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            var patches = Widescreen.Parse(aspect)!.Patches;
            foreach (var file in patches.Select(p => p.File).Distinct())
            {
                var content = new List<byte>(new byte[64]);
                foreach (var r in patches.Where(p => p.File == file).SelectMany(p => p.For(build)))
                {
                    Assert.Equal(r.Find.Length, r.Replace.Length);
                    content.AddRange(r.Find);
                    content.AddRange(new byte[64]);
                }
                File.WriteAllBytes(Path.Combine(dir, file), content.ToArray());
            }

            Assert.All(patches, p => Assert.Equal(PatchResult.Applied, p.Apply(dir, build)));
            Assert.All(patches, p => Assert.Equal(PatchResult.AlreadyApplied, p.Apply(dir, build)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WidescreenSettings_ChangeFovAndShiftRightHudIcons()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "settings"));
            File.WriteAllText(Path.Combine(dir, "AdamWin.ini"),
                "[Camera-LevelCamera]\r\nFieldOfView=1.360000\r\n\r\n[Camera-PlayerCamera]\r\nFieldOfView=1.360000\r\nZScale=0.092000\r\n");
            File.WriteAllText(Path.Combine(dir, "settings", "HUDIcon.ini"),
                "[Icon Define]\r\nDefine0=LifeIcon\r\n[Icon0]\r\nX=9\r\nY=55\r\n[Icon3]\r\nname=ScoreIcon\r\nX=510\r\n[LIFE]\r\nBaseX=84\r\n");

            Widescreen.Wide16x9.ApplySettings(dir);

            Assert.Equal("[Camera-LevelCamera]\r\nFieldOfView=1.819000\r\n\r\n[Camera-PlayerCamera]\r\nFieldOfView=1.819000\r\nZScale=0.092000\r\n",
                File.ReadAllText(Path.Combine(dir, "AdamWin.ini")));
            Assert.Equal("[Icon Define]\r\nDefine0=LifeIcon\r\n[Icon0]\r\nX=9\r\nY=55\r\n[Icon3]\r\nname=ScoreIcon\r\nX=726\r\n[LIFE]\r\nBaseX=84\r\n",
                File.ReadAllText(Path.Combine(dir, "settings", "HUDIcon.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WidescreenSettings_AreIdempotent_AndCanSwitchOrRevert()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "settings"));
            const string adamWin = "[Camera-LevelCamera]\r\nFieldOfView=1.360000\r\n[Camera-PlayerCamera]\r\nFieldOfView=1.360000\r\n";
            const string hud = "[Icon0]\r\nX=9\r\n[Icon3]\r\nX=510\r\n[Icon4]\r\nX=320\r\n";
            File.WriteAllText(Path.Combine(dir, "AdamWin.ini"), adamWin);
            File.WriteAllText(Path.Combine(dir, "settings", "HUDIcon.ini"), hud);
            string Hud() => File.ReadAllText(Path.Combine(dir, "settings", "HUDIcon.ini"));

            Widescreen.Wide16x9.ApplySettings(dir);
            Widescreen.Wide16x9.ApplySettings(dir);
            Assert.Equal("[Icon0]\r\nX=9\r\n[Icon3]\r\nX=726\r\n[Icon4]\r\nX=536\r\n", Hud());

            Widescreen.Wide16x10.ApplySettings(dir);
            Assert.Equal("[Icon0]\r\nX=9\r\n[Icon3]\r\nX=638\r\n[Icon4]\r\nX=448\r\n", Hud());
            Assert.Contains("FieldOfView=1.632000", File.ReadAllText(Path.Combine(dir, "AdamWin.ini")));

            Widescreen.ApplySettings(dir, null);
            Assert.Equal(hud, Hud());
            Assert.Equal(adamWin, File.ReadAllText(Path.Combine(dir, "AdamWin.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WidescreenSettings_UnknownFieldOfView_ThrowsWithoutChanges()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "settings"));
            const string adamWin = "[Camera-LevelCamera]\r\nFieldOfView=2.000000\r\n[Camera-PlayerCamera]\r\nFieldOfView=2.000000\r\n";
            File.WriteAllText(Path.Combine(dir, "AdamWin.ini"), adamWin);
            File.WriteAllText(Path.Combine(dir, "settings", "HUDIcon.ini"), "[Icon3]\r\nX=510\r\n");

            Assert.Throws<PatchException>(() => Widescreen.Wide16x9.ApplySettings(dir));
            Assert.Equal(adamWin, File.ReadAllText(Path.Combine(dir, "AdamWin.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IniFile_Get_ReadsFirstOfDuplicateSections_WithoutWriting()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[A]\nKey= x \n[A]\nKey=y\n[B]\nOther=1\n");
            var written = File.GetLastWriteTimeUtc(path);
            Assert.Equal(" x ", IniFile.Get(path, "a", "key"));
            Assert.Throws<PatchException>(() => IniFile.Get(path, "B", "Key"));
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ModernControls_SetsMenuDefaultsAndKeyBinds_PreservingOtherBytes()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            var settings = Directory.CreateDirectory(Path.Combine(dir, "settings")).FullName;
            // static.ini bevat Windows-1252-tekst (ü = 0xFC) die onaangeroerd moet blijven
            File.WriteAllBytes(Path.Combine(settings, "static.ini"), Encoding.Latin1.GetBytes(
                "[char0]\r\nstory=Grüße\r\n[DEFAULTSETTINGS]\r\nLeft=37\r\nRight=39\r\nUp=38\r\nDown=40\r\nExtra1=17\r\n"));
            File.WriteAllText(Path.Combine(settings, "PlayerDef.ini"),
                "[KeyBinds]\r\nFire=Control\r\nMoveForward=Up\r\nMoveBack=Down\r\nTurnLeft=Left\r\nTurnRight=Right\r\nMoveLeft=Alt+Left\r\nMoveRight=Alt+Right\r\nRotateLeft=Left\r\n");

            ModernControls.Apply(dir);

            Assert.Equal("[char0]\r\nstory=Grüße\r\n[DEFAULTSETTINGS]\r\nLeft=65\r\nRight=68\r\nUp=87\r\nDown=83\r\nExtra1=17\r\n",
                Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(settings, "static.ini"))));
            Assert.Equal("[KeyBinds]\r\nFire=Control\r\nMoveForward=W\r\nMoveBack=S\r\nTurnLeft=A\r\nTurnRight=D\r\nMoveLeft=Alt+A\r\nMoveRight=Alt+D\r\nRotateLeft=Left\r\n",
                File.ReadAllText(Path.Combine(settings, "PlayerDef.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IniFile_Set_UsesFirstOfDuplicateSections()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[InputKeys]\nKey1=a\n[Other]\nKey1=b\n[InputKeys]\nKey1=c\n");
            IniFile.Set(path, "InputKeys", "Key1", "x");
            Assert.Equal("[InputKeys]\nKey1=x\n[Other]\nKey1=b\n[InputKeys]\nKey1=c\n", File.ReadAllText(path));
            Assert.Throws<PatchException>(() => IniFile.Set(path, "Other", "Missing", "x"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IniFile_SetOrAdd_AddsMissingKeyOrSection()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[General]\r\nA=1\r\n[general]\r\nB=2\r\n");
            IniFile.SetOrAdd(path, "General", "A", "x");          // bestaat: gewoon zetten
            IniFile.SetOrAdd(path, "GENERAL", "Console", "1");    // ontbreekt: onder de eerste kop
            IniFile.SetOrAdd(path, "Console", "Console", "1");    // en een ontbrekende sectie aan het eind
            IniFile.SetOrAdd(path, "General", "Console", "1");    // nog een keer: verandert niets
            Assert.Equal("[General]\r\nConsole=1\r\nA=x\r\n[general]\r\nB=2\r\n[Console]\r\nConsole=1\r\n", File.ReadAllText(path));

            File.WriteAllText(path, "[Other]\nB=2");
            IniFile.SetOrAdd(path, "General", "Console", "1");
            IniFile.SetOrAdd(path, "Other", "C", "3");
            Assert.Equal("[Other]\nC=3\nB=2\n[General]\nConsole=1\n", File.ReadAllText(path));

            File.Delete(path);
            Assert.Throws<PatchException>(() => IniFile.SetOrAdd(path, "General", "Console", "1"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StartLevel_WritesRunSectionAndFreshTestPlayer()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            var settings = Directory.CreateDirectory(Path.Combine(dir, "settings")).FullName;
            File.WriteAllText(Path.Combine(dir, "RcHcGame.dat"), "");
            File.WriteAllText(Path.Combine(settings, "PlayerDef.ini"), "[Player]\r\nLife=3\r\n");
            File.WriteAllText(Path.Combine(settings, $"Player{StartLevel.TestPlayer}.ini"), "[Player]\r\nLife=0\r\n");
            File.WriteAllText(Path.Combine(settings, "Game.ini"),
                "[Run]\r\nPlayerNumber=1\r\nStartLevelNumber=1\r\nStartSubLevelNumber=2\r\nStartSubSubLevelNumber=1\r\nReplay=1\r\nIsLowEndPC=0\r\n");

            StartLevel.Prepare(dir, 4);

            Assert.Equal("[Run]\r\nPlayerNumber=10\r\nStartLevelNumber=1\r\nStartSubLevelNumber=4\r\nStartSubSubLevelNumber=1\r\nReplay=0\r\nIsLowEndPC=0\r\n",
                File.ReadAllText(Path.Combine(settings, "Game.ini")));
            // Zonder [sublevel1_4] in Game.ini: StandardSkill uit de tabel (7 = schieten, power shot, superjump)
            Assert.Equal("[Player]\r\nSkill=7\r\nLife=3\r\n", File.ReadAllText(Path.Combine(settings, $"Player{StartLevel.TestPlayer}.ini")));
            Assert.Throws<PatchException>(() => StartLevel.Prepare(dir, 6));

            // Met de console: Console=1 in [General] van de verse kopie (PlayerDef.ini zelf blijft zoals hij is)
            File.WriteAllText(Path.Combine(settings, "PlayerDef.ini"), "[General]\nDifficultyLevel=1\n\n[Player]\nLife=3\n");
            StartLevel.Prepare(dir, 1, console: true);
            Assert.Equal("[General]\nConsole=1\nDifficultyLevel=1\n\n[Player]\nSkill=0\nLife=3\n",
                File.ReadAllText(Path.Combine(settings, $"Player{StartLevel.TestPlayer}.ini")));
            Assert.Equal("[General]\nDifficultyLevel=1\n\n[Player]\nLife=3\n", File.ReadAllText(Path.Combine(settings, "PlayerDef.ini")));
            StartLevel.Prepare(dir, 1);
            Assert.Equal("[General]\nDifficultyLevel=1\n\n[Player]\nSkill=0\nLife=3\n",
                File.ReadAllText(Path.Combine(settings, $"Player{StartLevel.TestPlayer}.ini")));

            // StandardSkill uit Game.ini gaat voor de tabel; een bestaande Skill wordt overschreven
            File.AppendAllText(Path.Combine(settings, "Game.ini"), "\r\n[sublevel1_3]\r\nNrPotions=40\r\nStandardSkill=11\r\n");
            File.WriteAllText(Path.Combine(settings, "PlayerDef.ini"), "[Player]\r\nSkill=0\r\nLife=3\r\n");
            StartLevel.Prepare(dir, 3);
            Assert.Equal("[Player]\r\nSkill=11\r\nLife=3\r\n", File.ReadAllText(Path.Combine(settings, $"Player{StartLevel.TestPlayer}.ini")));
            Assert.Equal("[Player]\r\nSkill=0\r\nLife=3\r\n", File.ReadAllText(Path.Combine(settings, "PlayerDef.ini")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

using System.Text;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Opt-in patches: --widescreen en --wasd.</summary>
public class OptionsPatchesTests
{
    public static TheoryData<string> Aspects => new("16:9", "16:10");

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
    public void WidescreenPatches_ApplyToSyntheticFiles_AndAreIdempotent(string aspect)
    {
        var dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;
        try
        {
            var patches = Widescreen.Parse(aspect)!.Patches;
            foreach (var file in patches.Select(p => p.File).Distinct())
            {
                var content = new List<byte>(new byte[64]);
                foreach (var r in patches.Where(p => p.File == file).SelectMany(p => p.Replacements))
                {
                    Assert.Equal(r.Find.Length, r.Replace.Length);
                    content.AddRange(r.Find);
                    content.AddRange(new byte[64]);
                }
                File.WriteAllBytes(Path.Combine(dir, file), content.ToArray());
            }

            Assert.All(patches, p => Assert.Equal(PatchResult.Applied, p.Apply(dir)));
            Assert.All(patches, p => Assert.Equal(PatchResult.AlreadyApplied, p.Apply(dir)));
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
}

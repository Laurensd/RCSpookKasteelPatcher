using System.IO.Compression;
using System.Text;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

public sealed class DgVoodooTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string DefaultConf = """
        [General]
        FullScreenMode                       = true
        ScalingMode                          = unspecified
        CenterAppWindow                      = false
        [GeneralExt]
        FullscreenAttributes                 =
        FPSLimit                             = 0
        [Glide]
        Resolution                          = unforced
        Antialiasing                        = appdriven
        ForceVerticalSync                   = true
        3DfxWatermark                       = true
        [DirectX]
        VRAM                                = 256
        Resolution                          = unforced
        dgVoodooWatermark                   = true
        """;

    private static byte[] FakeZip(string conf)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "MS/x86/DDraw.dll", "MS/x86/D3DImm.dll", "3Dfx/x86/Glide.dll", "3Dfx/x86/Glide2x.dll", "3Dfx/x86/Glide3x.dll", "dgVoodooCpl.exe", "MS/x64/DDraw.dll" })
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(name);
            using (var w = new StreamWriter(zip.CreateEntry("dgVoodoo.conf").Open())) w.Write(conf);
        }
        return ms.ToArray();
    }

    [Fact]
    public void Install_CopiesX86DllsAndAdjustsConfig()
    {
        DgVoodoo.Install(FakeZip(DefaultConf), _dir);

        // 32-bit versie, niet de x64-DLL met dezelfde naam
        Assert.Equal("MS/x86/DDraw.dll", File.ReadAllText(Path.Combine(_dir, "DDraw.dll")));
        Assert.True(File.Exists(Path.Combine(_dir, "Glide2x.dll")));

        var conf = File.ReadAllText(Path.Combine(_dir, "dgVoodoo.conf"));
        Assert.Matches(@"(?m)^VRAM\s*= 128\s*$", conf);
        Assert.Matches(@"(?m)^dgVoodooWatermark\s*= false\s*$", conf);
        Assert.Matches(@"(?m)^3DfxWatermark\s*= false\s*$", conf);
        Assert.Matches(@"(?m)^ScalingMode\s*= stretched_ar\s*$", conf);
        Assert.Matches(@"(?m)^FullscreenAttributes\s*= fake\s*$", conf);
        Assert.Matches(@"(?m)^CenterAppWindow\s*= true\s*$", conf);
        Assert.Matches(@"(?m)^FullScreenMode\s*= true\s*$", conf);
        Assert.Matches(@"(?m)^FPSLimit\s*= 0\s*$", conf);
        Assert.Matches(@"(?m)^ForceVerticalSync\s*= true\s*$", conf);
        Assert.Matches(@"(?m)^Antialiasing\s*= 4x\s*$", conf);
    }

    private string InstallWith(DisplayOptions display)
    {
        DgVoodoo.Install(FakeZip(DefaultConf), _dir, display);
        return File.ReadAllText(Path.Combine(_dir, "dgVoodoo.conf"));
    }

    private static string GlideSection(string conf) => conf[conf.IndexOf("[Glide]")..conf.IndexOf("[DirectX]")];

    [Fact]
    public void Install_DisplayWindow_UsesWindowAnd2x()
    {
        var conf = InstallWith(new DisplayOptions { Mode = DisplayMode.Window });
        Assert.Matches(@"(?m)^FullScreenMode\s*= false\s*$", conf);
        Assert.Matches(@"(?m)^Resolution\s*= 2x\s*$", GlideSection(conf)); // niet "max": dan wordt het venster schermgroot
        Assert.Matches(@"(?m)^FullscreenAttributes\s*= fake\s*$", conf); // alt-enter: randloos
    }

    [Fact]
    public void Install_DisplayWindow_KeepsExplicitResolution()
    {
        var conf = InstallWith(new DisplayOptions { Mode = DisplayMode.Window, Resolution = "3x" });
        Assert.Matches(@"(?m)^Resolution\s*= 3x\s*$", GlideSection(conf));
    }

    [Fact]
    public void Install_DisplayFullscreen_IsExclusive()
    {
        var conf = InstallWith(new DisplayOptions { Mode = DisplayMode.Fullscreen });
        Assert.Matches(@"(?m)^FullScreenMode\s*= true\s*$", conf);
        Assert.Matches(@"(?m)^FullscreenAttributes\s*=\s*$", conf); // geen "fake"
        Assert.Matches(@"(?m)^Resolution\s*= max\s*$", GlideSection(conf));
    }

    [Fact]
    public void Install_FpsVsyncMsaaCrt()
    {
        var conf = InstallWith(new DisplayOptions { FpsLimit = 60, VSync = false, Antialiasing = "off", Crt = true });
        Assert.Matches(@"(?m)^FPSLimit\s*= 60\s*$", conf);
        Assert.Matches(@"(?m)^ForceVerticalSync\s*= false\s*$", conf);
        Assert.Matches(@"(?m)^Antialiasing\s*= off\s*$", conf);
        Assert.Matches(@"(?m)^ScalingMode\s*= stretched_ar_crt\s*$", conf);
    }

    [Theory]
    [InlineData("borderless", DisplayMode.Borderless)]
    [InlineData("Fullscreen", DisplayMode.Fullscreen)]
    [InlineData("window", DisplayMode.Window)]
    [InlineData("windowed", DisplayMode.Window)]
    [InlineData("groot", null)]
    public void ParseMode(string value, DisplayMode? expected) => Assert.Equal(expected, DisplayOptions.ParseMode(value));

    [Theory]
    [InlineData(null, "max")]
    [InlineData("2x", "2x")]
    [InlineData("unforced", "unforced")]
    public void Install_SetsOnlyGlideResolution(string? resolution, string expected)
    {
        if (resolution == null) DgVoodoo.Install(FakeZip(DefaultConf), _dir);
        else DgVoodoo.Install(FakeZip(DefaultConf), _dir, resolution);

        var conf = File.ReadAllText(Path.Combine(_dir, "dgVoodoo.conf"));
        var glide = conf[conf.IndexOf("[Glide]")..conf.IndexOf("[DirectX]")];
        var directX = conf[conf.IndexOf("[DirectX]")..];
        Assert.Matches($@"(?m)^Resolution\s*= {expected}\s*$", glide);
        Assert.Matches(@"(?m)^Resolution\s*= unforced\s*$", directX); // menu's (DirectDraw) niet geforceerd
    }

    [Theory]
    [InlineData("max", true)]
    [InlineData("max_isf", true)]
    [InlineData("max_fhd_isf_2x", true)]
    [InlineData("desktop", true)]
    [InlineData("2x", true)]
    [InlineData("1920x1080", true)]
    [InlineData("1920x1080@60", true)]
    [InlineData("unforced", true)]
    [InlineData("0x", false)]
    [InlineData("groot", false)]
    [InlineData("2x\nVRAM = 4096", false)]
    public void IsValidResolution(string value, bool valid) => Assert.Equal(valid, DgVoodoo.IsValidResolution(value));

    [Fact]
    public void Install_UnknownConfigLayout_Throws()
    {
        Assert.Throws<PatchException>(() => DgVoodoo.Install(FakeZip("[General]\nIets = anders\n"), _dir));
    }

    [Fact]
    public async Task GetZip_WrongHash_Throws()
    {
        var zip = Path.Combine(_dir, "dgv.zip");
        await File.WriteAllBytesAsync(zip, Encoding.ASCII.GetBytes("niet de echte zip"));
        await Assert.ThrowsAsync<PatchException>(() => DgVoodoo.GetZipAsync(zip, _ => { }));
    }
}

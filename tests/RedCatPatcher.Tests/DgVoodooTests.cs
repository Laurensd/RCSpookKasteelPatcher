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
        [GeneralExt]
        FullscreenAttributes                 =
        [Glide]
        3DfxWatermark                       = true
        [DirectX]
        VRAM                                = 256
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
        Assert.Matches(@"(?m)^FullScreenMode\s*= true\s*$", conf); // ongewijzigd
    }

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

using RedCatPatcher;
using RedCatPatcher.Disc;

namespace RedCatPatcher.Tests;

public sealed class DiscTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Iso_ExtractsDataFolder(bool joliet)
    {
        var iso = Path.Combine(_dir, "disc.iso");
        File.WriteAllBytes(iso, TestDisc.BuildIso(joliet));
        AssertExtracts(iso, expectMixedCase: joliet);
    }

    [Fact]
    public void CueBin_Mode2Raw_ExtractsDataFolder()
    {
        File.WriteAllBytes(Path.Combine(_dir, "RedCat.bin"), TestDisc.ToRawMode2(TestDisc.BuildIso(joliet: true)));
        var cue = Path.Combine(_dir, "RedCat.cue");
        File.WriteAllText(cue, "FILE \"RedCat.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
        AssertExtracts(cue, expectMixedCase: true);
    }

    [Fact]
    public void RawBinWithoutCue_IsDetectedBySyncPattern()
    {
        var bin = Path.Combine(_dir, "disc.bin");
        File.WriteAllBytes(bin, TestDisc.ToRawMode2(TestDisc.BuildIso(joliet: true)));
        AssertExtracts(bin, expectMixedCase: true);
    }

    [Fact]
    public void Cue_ResolvesFileNameCaseInsensitively()
    {
        File.WriteAllBytes(Path.Combine(_dir, "redcat.BIN"), TestDisc.ToRawMode2(TestDisc.BuildIso(joliet: true)));
        var cue = Path.Combine(_dir, "RedCat.cue");
        File.WriteAllText(cue, "FILE \"RedCat.bin\" BINARY\n  TRACK 01 MODE2/2352\n");
        AssertExtracts(cue, expectMixedCase: true);
    }

    [Fact]
    public void Cue_WithMissingBin_GivesFriendlyError()
    {
        var cue = Path.Combine(_dir, "RedCat.cue");
        File.WriteAllText(cue, "FILE \"weg.bin\" BINARY\n  TRACK 01 MODE2/2352\n");
        var ex = Assert.Throws<PatchException>(() => DiscImage.Open(cue));
        Assert.Contains("weg.bin", ex.Message);
    }

    [Fact]
    public void NonIsoData_GivesFriendlyError()
    {
        var iso = Path.Combine(_dir, "rommel.iso");
        File.WriteAllBytes(iso, new byte[2048 * 20]);
        using var disc = DiscImage.Open(iso);
        Assert.Throws<PatchException>(() => new Iso9660(disc));
    }

    private void AssertExtracts(string discPath, bool expectMixedCase)
    {
        using var disc = DiscImage.Open(discPath);
        var iso = new Iso9660(disc);
        Assert.Equal("REDCAT", iso.VolumeLabel);

        var data = iso.Find("data");
        Assert.NotNull(data);
        Assert.True(data!.IsDirectory);

        var target = Path.Combine(_dir, "out");
        iso.ExtractDirectory(data, target);
        foreach (var (name, content) in TestDisc.Files)
        {
            var expectedName = expectMixedCase ? name : name.ToUpperInvariant();
            var file = Directory.GetFiles(target).Single(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expectedName, Path.GetFileName(file));
            Assert.Equal(content, File.ReadAllBytes(file));
        }
    }
}

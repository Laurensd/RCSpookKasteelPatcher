using System.Security.Cryptography;
using RedCatPatcher.Disc;

namespace RedCatPatcher.Tests;

public sealed class DiscVerifierTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly (string File, string Sha256)[] TestFiles =
        TestDisc.Files.Select(f => (f.Name, Convert.ToHexString(SHA256.HashData(f.Content)))).ToArray();

    private static readonly byte[] Iso = TestDisc.BuildIso(joliet: true);
    private static readonly string IsoSha1 = Convert.ToHexString(SHA1.HashData(Iso));

    [Fact]
    public void IsoAndBin_HaveTheSameDataHash()
    {
        var iso = Check(Write("disc.iso", Iso), []);
        var bin = Check(WriteCue(TestDisc.ToRawMode2(Iso)), []);

        Assert.Equal(IsoSha1, iso.DataSha1);
        Assert.Equal(IsoSha1, iso.ImageSha1);
        Assert.Equal(IsoSha1, bin.DataSha1);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(TestDisc.ToRawMode2(Iso))), bin.ImageSha1);
    }

    [Fact]
    public void KnownDataHash_RecognizesIsoAndBin()
    {
        var known = new KnownDisc("v1.0.0", ImageSha1: "00", DataSha1: IsoSha1.ToLowerInvariant());
        foreach (var path in new[] { Write("disc.iso", Iso), WriteCue(TestDisc.ToRawMode2(Iso)) })
        {
            var check = Check(path, [known], files: [("RcHcGame.dat", "verkeerd")]);
            Assert.Equal(DiscStatus.Recognized, check.Status);
            Assert.Same(known, check.Disc);
            Assert.Empty(check.Problems);
        }
    }

    [Fact]
    public void KnownImageHash_IsRecognized()
    {
        var raw = TestDisc.ToRawMode2(Iso);
        var known = new KnownDisc("v1.0.0", ImageSha1: Convert.ToHexString(SHA1.HashData(raw)), DataSha1: "00");
        Assert.Equal(DiscStatus.Recognized, Check(WriteCue(raw), [known]).Status);
    }

    [Fact]
    public void EmptySectorsAfterTheVolume_DoNotChangeTheDataHash()
    {
        // Een .bin bevat vaak lege sectoren na het volume (postgap); een .iso meestal niet.
        var padded = Iso.Concat(new byte[2048 * 150]).ToArray();
        var check = Check(WriteCue(TestDisc.ToRawMode2(padded)), []);
        Assert.Equal(IsoSha1, check.DataSha1);
    }

    [Fact]
    public void UnknownImage_WithCorrectFiles_IsFilesOk()
    {
        var check = Check(Write("disc.iso", Iso), []);
        Assert.Equal(DiscStatus.FilesOk, check.Status);
        Assert.Null(check.Disc);
        Assert.Empty(check.Problems);
    }

    [Fact]
    public void ChangedFile_IsSuspicious()
    {
        var damaged = (byte[])Iso.Clone();
        damaged[24 * 2048 + 100] ^= 0xFF; // in RcHcGame.dat
        var check = Check(WriteCue(TestDisc.ToRawMode2(damaged)), []);
        Assert.Equal(DiscStatus.Suspicious, check.Status);
        var problem = Assert.Single(check.Problems);
        Assert.Contains("RcHcGame.dat wijkt af (5000 bytes", problem);
    }

    [Fact]
    public void MissingFile_IsSuspicious()
    {
        var check = Check(Write("disc.iso", Iso), [], files: [.. TestFiles, ("RedCat.exe", "00")]);
        Assert.Equal(DiscStatus.Suspicious, check.Status);
        Assert.Contains("RedCat.exe ontbreekt", Assert.Single(check.Problems));
    }

    [Fact]
    public void TruncatedImage_IsSuspicious()
    {
        // Afgekapt midden in RcHcGame.dat (sector 24-26), AdamWin.ini (sector 27) valt er helemaal af.
        var check = Check(Write("disc.iso", Iso[..(26 * 2048)]), []);
        Assert.Equal(DiscStatus.Suspicious, check.Status);
        Assert.Contains(check.Problems, p => p.Contains("onvolledig: 26 van de 28 sectoren"));
        Assert.Contains(check.Problems, p => p.Contains("AdamWin.ini valt (deels) buiten de image"));
    }

    [Fact]
    public void Progress_ReachesTheWholeFile()
    {
        var raw = TestDisc.ToRawMode2(Iso.Concat(new byte[2048 * 1000]).ToArray());
        var path = WriteCue(raw);
        var reports = new List<(long Done, long Total)>();
        using var disc = DiscImage.Open(path);
        DiscVerifier.Verify(disc, new Iso9660(disc), [], TestFiles, (d, t) => reports.Add((d, t)));
        Assert.True(reports.Count > 1);
        Assert.Equal((raw.Length, raw.Length), reports[^1]);
    }

    /// <summary>Draait alleen als de echte disc naast de repository staat (RedCatDisc\RedCat.cue, of via REDCAT_DISC).</summary>
    [Fact]
    public void RealDisc_IsRecognizedAndHasTheExpectedFiles()
    {
        var cue = FindRealDisc();
        if (cue == null) return;
        using var disc = DiscImage.Open(cue);
        var iso = new Iso9660(disc);

        var check = DiscVerifier.Verify(disc, iso);
        Assert.Equal(DiscStatus.Recognized, check.Status);
        Assert.Equal(DiscVerifier.KnownDiscs[0].ImageSha1, check.ImageSha1);
        Assert.Equal(DiscVerifier.KnownDiscs[0].DataSha1, check.DataSha1);

        // Zonder bekende hashes moeten de spelbestanden zelf kloppen.
        var files = DiscVerifier.Verify(disc, iso, [], DiscVerifier.RequiredFiles);
        Assert.Empty(files.Problems);
        Assert.Equal(DiscStatus.FilesOk, files.Status);
    }

    private static string? FindRealDisc()
    {
        var env = Environment.GetEnvironmentVariable("REDCAT_DISC");
        if (!string.IsNullOrEmpty(env)) return env;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var cue = Path.Combine(dir.FullName, "RedCatDisc", "RedCat.cue");
            if (File.Exists(cue)) return cue;
        }
        return null;
    }

    private DiscCheck Check(string path, KnownDisc[] known, (string File, string Sha256)[]? files = null)
    {
        using var disc = DiscImage.Open(path);
        return DiscVerifier.Verify(disc, new Iso9660(disc), known, files ?? TestFiles);
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string WriteCue(byte[] raw)
    {
        Write("RedCat.bin", raw);
        var cue = Path.Combine(_dir, "RedCat.cue");
        File.WriteAllText(cue, "FILE \"RedCat.bin\" BINARY\n  TRACK 01 MODE2/2352\n");
        return cue;
    }
}

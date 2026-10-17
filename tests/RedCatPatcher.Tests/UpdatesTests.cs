using System.Buffers.Binary;
using System.Text;
using RedCatPatcher.Patching;
using RedCatPatcher.Updates;

namespace RedCatPatcher.Tests;

/// <summary>Officiële updates en versieherkenning; de echte updates en spelbestanden zitten niet in de repo.</summary>
public sealed class UpdatesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Bouwt een Wise-delta met de gegeven commando's en literals.</summary>
    private static byte[] Delta(byte[] old, byte[] result, byte[] ops, string literals, bool full = false)
    {
        var header = new byte[29];
        header[0] = 0x01; header[1] = 0x91; header[2] = 0x2A;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(5), Crc32.Compute(result));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9), result.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(13), full ? 0 : 25);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(17), Crc32.Compute(old));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(21), old.Length);
        if (full) return [.. header.AsSpan(0, 25), .. result];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(25), ops.Length);
        return [.. header, .. ops, .. Encoding.ASCII.GetBytes(literals)];
    }

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void Crc32_MatchesStandardCheckValue() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute(Ascii("123456789")));

    [Fact]
    public void WiseDelta_ReplacesWithLiteralsAndSkips()
    {
        var old = Ascii("The quick brown fox jumps");
        var result = Ascii("The quick red fox jumps");
        byte[] ops =
        [
            0x80, 0x47,         // 0 literals; 7+3 bytes kopiëren ("The quick ")
            0x83, 0x07, 0x02,   // 3 literals ("red"); leeskop 2 verder ("brown" -> " fox"), 7+3 bytes kopiëren
        ];
        Assert.Equal(result, WiseDelta.Apply(old, Delta(old, result, ops, "red")));
    }

    [Fact]
    public void WiseDelta_ShortCommandsExtendedLengthsAndNegativeSkip()
    {
        var old = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        // 2 literals + 6 bytes, dan 200 bytes (lengte in twee bytes), dan 1 stap terug en 3 bytes
        byte[] ops = [0x23, 0x80, 0xC1, 0x45, 0x80, 0x00, 0x7F];
        byte[] expected = [.. "AB"u8, .. old.AsSpan(2, 6), .. old.AsSpan(8, 200), .. old.AsSpan(207, 3)];

        Assert.Equal(expected, WiseDelta.Apply(old, Delta(old, expected, ops, "AB")));
    }

    [Fact]
    public void WiseDelta_FullFile()
    {
        var old = Ascii("oud");
        var result = Ascii("helemaal nieuw");
        Assert.Equal(result, WiseDelta.Apply(old, Delta(old, result, [], "", full: true)));
    }

    [Fact]
    public void WiseDelta_WrongResult_Throws()
    {
        var old = Ascii("The quick brown fox jumps");
        var delta = Delta(old, Ascii("The quick red fox jumps"), [0x80, 0x47, 0x83, 0x07, 0x02], "RED");
        Assert.Throws<InvalidDataException>(() => WiseDelta.Apply(old, delta));
    }

    [Fact]
    public void WiseDelta_ReportsSourceAndTarget()
    {
        var old = Ascii("abc");
        var result = Ascii("abcd");
        var delta = Delta(old, result, [], "", full: true);
        Assert.True(WiseDelta.IsDelta(delta));
        Assert.True(WiseDelta.Matches(old, WiseDelta.Source(delta)));
        Assert.True(WiseDelta.Matches(result, WiseDelta.Target(delta)));
        Assert.False(WiseDelta.IsDelta(result));
    }

    [Fact]
    public void OfficialUpdate_UnknownExe_IsReportedWithHash()
    {
        var path = Path.Combine(_dir, "iets.exe");
        File.WriteAllBytes(path, Ascii("MZ geen update"));

        var ex = Assert.Throws<PatchException>(() => OfficialUpdate.Identify(path));
        Assert.Contains("geen ondersteunde update", ex.Message);
        Assert.Contains("SHA-256", ex.Message);
        Assert.Contains(GameVersion.IssuesUrl, ex.Message);
        Assert.Null(OfficialUpdate.TryIdentify(path));
    }

    [Fact]
    public void OfficialUpdate_KnownUpdatesAreOrdered()
    {
        Assert.Equal([OfficialUpdate.Win98, OfficialUpdate.Xp], OfficialUpdate.Known.OrderBy(u => u.Order));
        Assert.All(OfficialUpdate.Known, u => Assert.Equal(64, u.Sha256.Length));
    }

    [Fact]
    public void GameVersion_UnknownGameExe_IsReportedAndNotPatched()
    {
        File.WriteAllBytes(Path.Combine(_dir, "RcHcGame.dat"), Ascii("andere versie"));

        var ex = Assert.Throws<PatchException>(() => GameVersion.Detect(_dir));
        Assert.Contains("RcHcGame.dat", ex.Message);
        Assert.Contains("niet kent", ex.Message);
        Assert.Contains("13 bytes", ex.Message);
        Assert.Contains(GameVersion.IssuesUrl, ex.Message);
    }

    [Fact]
    public void GameVersion_MissingGameExe_Throws()
    {
        var ex = Assert.Throws<PatchException>(() => GameVersion.Detect(_dir));
        Assert.Contains("niet gevonden", ex.Message);
    }
}

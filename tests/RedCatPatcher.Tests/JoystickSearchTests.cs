using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>Fix: de controller op elk joystick-ID zoeken in plaats van alleen 0 en 1.</summary>
public class JoystickSearchTests
{
    /// <summary>De opstartcode van 1.0.0 (0x5A5A30-0x5A5A9D), met het IAT-adres van joyGetPosEx als parameter.</summary>
    internal static byte[] Probe(byte[] iat) => Convert.FromHexString(
        "8B2D" + Convert.ToHexString(iat) + "8D7E04576A00C78424C401000000000000C70734000000C7460883000000FFD585C0751E" +
        "8B460C8B4E10C64638018986AC000000898EB0000000C70600000000EB25576A01FFD585C0751C" +
        "8B560C8B4610C64638018996AC0000008986B0000000C70601000000");

    [Fact]
    public void Apply_ReplacesProbeWithLoop_KeepsImportAddressAndLength()
    {
        byte[] iat = [0x50, 0xa2, 0x64, 0x00];
        var probe = Probe(iat);
        byte[] data = [.. new byte[100], .. probe, 0x8b, 0x06, .. new byte[100]];

        Assert.Equal(PatchResult.Applied, JoystickSearch.Apply(data));
        Assert.Equal(202 + probe.Length, data.Length);

        var code = data.AsSpan(100, probe.Length);
        Assert.True(code[31..37].SequenceEqual((byte[])[0xff, 0x15, .. iat]));      // call [joyGetPosEx]
        Assert.True(code[42..45].SequenceEqual(new byte[] { 0x83, 0xfd, 0x10 }));   // cmp ebp, 16
        Assert.Equal(0x90, code[^1]);
        Assert.Equal(0x8b, data[100 + probe.Length]);                              // daarna ongewijzigd

        Assert.Equal(PatchResult.AlreadyApplied, JoystickSearch.Apply(data));
    }

    [Fact]
    public void Apply_OtherImportAddress()
    {
        byte[] data = [.. new byte[10], .. Probe([0x44, 0x33, 0x65, 0x00]), .. new byte[10]];
        JoystickSearch.Apply(data);
        Assert.True(data.AsSpan(10 + 31, 6).SequenceEqual(new byte[] { 0xff, 0x15, 0x44, 0x33, 0x65, 0x00 }));
    }

    [Fact]
    public void Apply_FailsWhenMissingOrDuplicated()
    {
        Assert.Throws<PatchException>(() => JoystickSearch.Apply(new byte[500]));
        var probe = Probe([1, 2, 3, 4]);
        Assert.Throws<PatchException>(() => JoystickSearch.Apply([.. probe, .. probe]));
    }
}

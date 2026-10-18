using RedCatPatcher;
using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

public class AgentHelpTests
{
    [Theory]
    // Waarden uit |CONTEXT van Agent\REDCAT.HLP
    [InlineData("h_Playerlist", -1576648784)]
    [InlineData("h_Adventure", 1414221235)]
    [InlineData("h_screen_novideocard", 2051094170)]
    public void Hash_MatchesWinHelpContext(string name, int expected)
    {
        Assert.Equal(expected, WinHelpFile.Hash(name));
        Assert.Equal(expected, WinHelpFile.Hash(name.ToUpperInvariant()));
    }

    [Fact]
    public void Hash_UnsupportedCharacter_IsNull() => Assert.Null(WinHelpFile.Hash("h_level2"));

    [Fact]
    public void ReadTopics_RejectsOtherFiles() =>
        Assert.Throws<PatchException>(() => WinHelpFile.ReadTopics(new byte[64], ["h_Adventure"]));

    [Fact]
    public void MenuPatch_ReadsTitleAndTextFromIni()
    {
        var patch = GamePatches.All.Single(p => p.Id == "menu-agent-help");
        var code = patch.Replacements[0].Replace;
        const int functionVa = 0x54BEC0;
        // De strings staan direct achter de code; de pushes verwijzen er absoluut naar.
        int title = code.AsSpan().IndexOf("Title\0Text\0\0"u8);
        Assert.True(title > 0);
        Assert.True(code.AsSpan().IndexOf(Push(functionVa + title)) > 0);
        Assert.True(code.AsSpan().IndexOf(Push(functionVa + title + 6)) > 0);
        Assert.True(code.AsSpan().IndexOf(Push(functionVa + title + 11)) > 0);
        Assert.Equal("Agent\\RedCat.ini\0", System.Text.Encoding.ASCII.GetString(patch.Replacements[1].Replace));
        Assert.Equal("RedCat.ini", AgentHelp.IniName);
    }

    private static byte[] Push(int address) => [0x68, .. BitConverter.GetBytes(address)];
}

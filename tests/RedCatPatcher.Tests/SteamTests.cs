using System.Text;
using RedCatPatcher.Install;
using RedCatPatcher.Steam;
using RedCatPatcher.Updates;

namespace RedCatPatcher.Tests;

public sealed class SteamTests
{
    /// <summary>Een shortcuts.vdf zoals Steam hem schrijft, met één niet-Steam-game (alle veldtypen die Steam gebruikt).</summary>
    private static byte[] SampleShortcutsVdf()
    {
        var b = new List<byte>();
        void Str(string s) { b.AddRange(Encoding.UTF8.GetBytes(s)); b.Add(0); }
        void Map(string k) { b.Add(0x00); Str(k); }
        void Text(string k, string v) { b.Add(0x01); Str(k); Str(v); }
        void Int(string k, int v) { b.Add(0x02); Str(k); b.AddRange(BitConverter.GetBytes(v)); }
        void End() => b.Add(0x08);

        Map("shortcuts");
        Map("0");
        Int("appid", unchecked((int)0xA1B2C3D4));
        Text("AppName", "Mijn spel");
        Text("Exe", "\"C:\\Games\\Spel\\spel.exe\"");
        Text("StartDir", "\"C:\\Games\\Spel\\\"");
        Text("icon", "");
        Text("ShortcutPath", "");
        Text("LaunchOptions", "-fullscreen");
        Int("IsHidden", 0);
        Int("AllowDesktopConfig", 1);
        Int("AllowOverlay", 1);
        Int("OpenVR", 0);
        Int("Devkit", 0);
        Text("DevkitGameID", "");
        Int("DevkitOverrideAppID", 0);
        Int("LastPlayTime", 1700000000);
        Text("FlatpakAppID", "");
        Map("tags");
        Text("0", "favorite");
        End();
        End();
        End();
        End();
        return [.. b];
    }

    [Fact]
    public void BinaryVdf_RoundTripIsByteIdentical()
    {
        var data = SampleShortcutsVdf();
        var root = BinaryVdf.Read(data);
        Assert.Equal(data, BinaryVdf.Write(root));

        var game = root.Map("shortcuts")!.Map("0")!;
        Assert.Equal("Mijn spel", game.String("appname"));
        Assert.Equal(1700000000, game["LastPlayTime"]);
        Assert.Equal("favorite", game.Map("tags")!.String("0"));
    }

    [Fact]
    public void BinaryVdf_EmptyFileFromSteam()
    {
        byte[] empty = [0x00, .. "shortcuts"u8, 0x00, 0x08, 0x08];
        var root = BinaryVdf.Read(empty);
        Assert.Empty(root.Map("shortcuts")!);
        Assert.Equal(empty, BinaryVdf.Write(SteamShortcuts.Empty()));
    }

    [Fact]
    public void BinaryVdf_TruncatedFileIsRejected()
    {
        var data = SampleShortcutsVdf();
        Assert.Throws<PatchException>(() => BinaryVdf.Read(data[..^3]));
    }

    [Fact]
    public void AppId_IsCrcOfQuotedExeAndNameWithHighBit()
    {
        var shortcut = new SteamShortcut("RedCat Spookkasteel", "/home/deck/Games/RedCat Spookkasteel/RedCat.exe", "/home/deck/Games/RedCat Spookkasteel");
        uint expected = Crc32.Compute("\"/home/deck/Games/RedCat Spookkasteel/RedCat.exe\"RedCat Spookkasteel"u8) | 0x80000000;
        Assert.Equal(expected, shortcut.AppId);
        Assert.Equal(0x901D8F8Au, shortcut.AppId); // referentie: Python zlib.crc32
        Assert.Equal(((ulong)shortcut.AppId << 32) | 0x02000000, shortcut.GameId);
    }

    [Fact]
    public void AppId_KnownValue()
    {
        // Referentie: Python zlib.crc32(b'"a"b') | 0x80000000.
        Assert.Equal(0xE6645FEAu, SteamShortcut.ComputeAppId("\"a\"", "b"));
    }

    [Fact]
    public void AddOrUpdate_AppendsAndKeepsExistingGames()
    {
        var root = BinaryVdf.Read(SampleShortcutsVdf());
        var shortcut = new SteamShortcut("RedCat Spookkasteel", @"D:\RedCat Spookkasteel\RedCat.exe", @"D:\RedCat Spookkasteel");

        uint appId = SteamShortcuts.AddOrUpdate(root, shortcut);
        Assert.Equal(shortcut.AppId, appId);

        var list = root.Map("shortcuts")!;
        Assert.Equal(2, list.Count);
        Assert.Equal("Mijn spel", list.Map("0")!.String("AppName"));
        var redcat = list.Map("1")!;
        Assert.Equal((int)appId, redcat["appid"]);
        Assert.Equal("\"D:\\RedCat Spookkasteel\\RedCat.exe\"", redcat.String("Exe"));
        Assert.Equal("\"D:\\RedCat Spookkasteel\"", redcat.String("StartDir"));

        // Na schrijven en teruglezen is alles er nog.
        var reread = BinaryVdf.Read(BinaryVdf.Write(root));
        Assert.Equal(BinaryVdf.Write(root), BinaryVdf.Write(reread));
    }

    [Fact]
    public void AddOrUpdate_TwiceDoesNotDuplicateAndKeepsUserFields()
    {
        var root = SteamShortcuts.Empty();
        var shortcut = new SteamShortcut("RedCat Spookkasteel", "/games/RedCat.exe", "/games");
        SteamShortcuts.AddOrUpdate(root, shortcut);
        root.Map("shortcuts")!.Map("0")!["LastPlayTime"] = 12345;

        // Andere naam, zelfde exe: dezelfde game, appid blijft gelijk (anders kwijt: Proton-keuze, plaatjes, speeltijd).
        uint appId = SteamShortcuts.AddOrUpdate(root, shortcut with { AppName = "RedCat" });

        var list = root.Map("shortcuts")!;
        Assert.Single(list);
        Assert.Equal(shortcut.AppId, appId);
        Assert.Equal("RedCat", list.Map("0")!.String("AppName"));
        Assert.Equal(12345, list.Map("0")!["LastPlayTime"]);
    }

    private const string SampleConfigVdf = """
        "InstallConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"BaseInstallFolder_1"		"D:\\SteamLibrary"
        				"CompatToolMapping"
        				{
        					"0"
        					{
        						"name"		"proton_9"
        						"config"		""
        						"priority"		"75"
        					}
        				}
        			}
        		}
        	}
        	"Music"
        	{
        		"LocalLibrary"		"\"quoted\""
        	}
        }

        """;

    [Fact]
    public void TextVdf_RoundTripKeepsEscapes()
    {
        var text = SampleConfigVdf.ReplaceLineEndings("\n");
        var root = TextVdf.Parse(text);
        Assert.Equal(text, TextVdf.Write(root));

        var steam = root.Map("InstallConfigStore")!.Map("Software")!.Map("Valve")!.Map("Steam")!;
        Assert.Equal(@"D:\SteamLibrary", TextVdf.Unescape(steam.String("BaseInstallFolder_1")!));
        Assert.Equal("\"quoted\"", TextVdf.Unescape(root.Map("InstallConfigStore")!.Map("Music")!.String("LocalLibrary")!));
    }

    [Fact]
    public void SetCompatTool_AddsMappingAndKeepsOthers()
    {
        var root = TextVdf.Parse(SampleConfigVdf);
        SteamShortcuts.SetCompatTool(root, 0x9D9C4A4F, "proton_experimental");

        var reread = TextVdf.Parse(TextVdf.Write(root));
        var mapping = reread.Map("InstallConfigStore")!.Map("Software")!.Map("Valve")!.Map("Steam")!.Map("CompatToolMapping")!;
        Assert.Equal("proton_9", mapping.Map("0")!.String("name"));
        var entry = mapping.Map(0x9D9C4A4Fu.ToString())!;
        Assert.Equal("proton_experimental", entry.String("name"));
        Assert.Equal("250", entry.String("priority"));
    }

    [Fact]
    public void SetCompatTool_CreatesStructureInEmptyConfig()
    {
        var root = new VdfMap();
        SteamShortcuts.SetCompatTool(root, 123, "proton_experimental");
        var text = TextVdf.Write(root);
        Assert.Contains("\"CompatToolMapping\"", text);
        Assert.Equal("proton_experimental", TextVdf.Parse(text).Map("InstallConfigStore")!.Map("Software")!.Map("Valve")!
            .Map("Steam")!.Map("CompatToolMapping")!.Map("123")!.String("name"));
    }

    [Fact]
    public void SteamInstall_ListsNumericUsersWithNames()
    {
        var dir = Directory.CreateTempSubdirectory("redcat-steam-").FullName;
        try
        {
            foreach (var u in new[] { "0", "12345", "ac", "678" }) Directory.CreateDirectory(Path.Combine(dir, "userdata", u));
            Directory.CreateDirectory(Path.Combine(dir, "config"));
            File.WriteAllText(Path.Combine(dir, "config", "loginusers.vdf"), $$"""
                "users"
                {
                	"{{12345 + 76561197960265728UL}}"
                	{
                		"AccountName"		"speler"
                		"PersonaName"		"RedCat Fan"
                	}
                }
                """);

            var users = new SteamInstall(dir).Users();
            Assert.Equal([678u, 12345u], users.Select(u => u.AccountId));
            Assert.Equal("678", users[0].Name);
            Assert.Equal("RedCat Fan", users[1].Name);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ShellLink_HasHeaderTargetAndStrings()
    {
        var lnk = ShellLink.Create(@"C:\Games\RedCat Spookkasteel\RedCat.exe", @"C:\Games\RedCat Spookkasteel",
            @"C:\Games\RedCat Spookkasteel\RedCat.exe", "RedCat");
        Assert.Equal(0x4C, BitConverter.ToInt32(lnk, 0));
        Assert.Equal(new Guid("00021401-0000-0000-C000-000000000046"), new Guid(lnk.AsSpan(4, 16)));
        Assert.True(lnk.AsSpan().IndexOf(Encoding.Unicode.GetBytes(@"C:\Games\RedCat Spookkasteel\RedCat.exe")) > 0);
        Assert.Contains(@"C:\Games\RedCat Spookkasteel\RedCat.exe", Encoding.ASCII.GetString(lnk));
        Assert.Equal(0, BitConverter.ToInt32(lnk, lnk.Length - 4));
    }

    [Fact]
    public void DesktopEntry_StartsViaSteam()
    {
        var entry = Shortcuts.DesktopEntry("steam steam://rungameid/123", "/games/RedCat");
        Assert.StartsWith("[Desktop Entry]\n", entry.ReplaceLineEndings("\n"));
        Assert.Contains("Exec=steam steam://rungameid/123", entry);
        Assert.Contains("Path=/games/RedCat", entry);
    }
}

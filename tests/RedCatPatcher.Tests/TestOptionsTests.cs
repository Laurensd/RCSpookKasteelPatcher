using RedCatPatcher.Patching;

namespace RedCatPatcher.Tests;

/// <summary>
/// Tabblad Testing van de launcher (docs/LAUNCHER.md, "Groep 3: testen"): bewaren in RedCatLauncher.ini [Testen] en de
/// [Debug]-schakelaars in AdamWin.ini herhaalbaar zetten.
/// </summary>
public sealed class TestOptionsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("redcat-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string AdamWin =
        "[Camera-PlayerCamera]\r\nFieldOfView=1.360000\r\n" +
        "[Debug]\r\nDebugActorLighting=0\r\nDebugDriver=0\r\nDebugFakeDriverCrash=0\r\nDebugFileNumber=3\r\n" +
        "DebugLoading=0\r\nDebugPlayerPosition=0\r\nDebugSecretCommands=0\r\nDebugUseConsole=0\r\n" +
        "[Sound]\r\nDebugDriver=0\r\n";

    /// <summary>
    /// Alle 384 combinaties: geen startlevel of 1 t/m 5, met elke deelverzameling van de log-schakelaars, console,
    /// debugtoetsen en laadlog.
    /// </summary>
    private static IEnumerable<TestOptions> AllOptions()
    {
        int n = TestOptions.DebugKeys.Length;
        foreach (var level in new int?[] { null, 1, 2, 3, 4, 5 })
            for (int mask = 0; mask < 1 << n + 3; mask++)
            {
                var options = new TestOptions
                {
                    StartLevel = level,
                    Console = (mask & 1 << n) == 0,
                    SecretCommands = (mask & 1 << n + 1) != 0,
                    LoadConsole = (mask & 1 << n + 2) != 0,
                };
                for (int k = 0; k < n; k++)
                    options = options.WithDebug(TestOptions.DebugKeys[k], (mask & 1 << k) != 0);
                yield return options;
            }
    }

    /// <summary>Een spelmap met AdamWin.ini, en dezelfde in de basis.</summary>
    private string MakeGame(string name = "game", string adamWin = AdamWin)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Basis.DirOf(dir));
        File.WriteAllText(Path.Combine(dir, TestOptions.AdamWin), adamWin);
        File.WriteAllText(Path.Combine(Basis.DirOf(dir), TestOptions.AdamWin), adamWin);
        return dir;
    }

    private static string Read(string dir) => File.ReadAllText(Path.Combine(dir, TestOptions.AdamWin));

    [Fact]
    public void LauncherIni_RoundTripsAllOptions()
    {
        var game = MakeGame();
        foreach (var testing in AllOptions())
        {
            var ini = new LauncherIni(new Choice { Wasd = true }, new Choice(), "ABC123") { Testing = testing };
            ini.Write(game);
            Assert.Equal(ini, LauncherIni.Read(game));
        }
    }

    [Fact]
    public void LauncherIni_WritesTestingSectionOnlyWhenSomethingIsOn()
    {
        var game = MakeGame();
        var path = Path.Combine(game, LauncherIni.FileName);

        new LauncherIni(new Choice(), new Choice(), "ABC123").Write(game);
        Assert.DoesNotContain("[Testen]", File.ReadAllText(path));

        new LauncherIni(new Choice(), new Choice(), "ABC123") { Testing = new TestOptions { StartLevel = 3 }.WithDebug("DebugLoading", true) }.Write(game);
        var text = File.ReadAllText(path);
        Assert.Contains("[Testen]\r\nStartLevel=3\r\nConsole=1\r\nDebugDriver=0\r\nDebugLoading=1\r\nDebugPlayerPosition=0\r\n" +
            "DebugSecretCommands=0\r\nDebugUseConsole=0\r\n", text);
        Assert.EndsWith("BasisSha256=ABC123\r\nRevision=2\r\n", text);

        // De patcher schrijft het bestand opnieuw zonder [Testen]: opnieuw patchen zet de testhulp terug.
        new LauncherIni(new Choice(), new Choice(), "ABC123").Write(game);
        Assert.Equal(new TestOptions(), LauncherIni.Read(game)!.Testing);

        // De console staat standaard aan: uit is wel iets om te bewaren
        new LauncherIni(new Choice(), new Choice(), "ABC123") { Testing = new TestOptions { Console = false } }.Write(game);
        Assert.Contains("Console=0\r\n", File.ReadAllText(path));
        Assert.Equal(new TestOptions { Console = false }, LauncherIni.Read(game)!.Testing);
    }

    [Fact]
    public void LauncherIni_UnknownTestingValues_AreOff()
    {
        var game = MakeGame();
        File.WriteAllText(Path.Combine(game, LauncherIni.FileName),
            "[Keuze]\r\nWasd=1\r\n[testen]\r\nStartLevel=9\r\ndebugdriver = 1\r\nDebugLoading=ja\r\nDebugFakeDriverCrash=1\r\n");
        Assert.Equal(new TestOptions().WithDebug("DebugDriver", true), LauncherIni.Read(game)!.Testing);

        // Zonder Console (bestand van vóór die schakelaar): aan
        File.WriteAllText(Path.Combine(game, LauncherIni.FileName), "[Testen]\r\nStartLevel=5\r\n");
        Assert.Equal(new TestOptions { StartLevel = 5 }, LauncherIni.Read(game)!.Testing);

        File.WriteAllText(Path.Combine(game, LauncherIni.FileName),
            "[Testen]\r\nConsole=false\r\nDebugSecretCommands=1\r\nDebugUseConsole=true\r\nDebugKeys=1\r\n");
        Assert.Equal(new TestOptions { Console = false, SecretCommands = true, LoadConsole = true }, LauncherIni.Read(game)!.Testing);
    }

    [Fact]
    public void WithDebug_IgnoresKeysThatAreNotSwitches()
    {
        var options = new TestOptions().WithDebug("DebugFakeDriverCrash", true);
        Assert.True(options.IsDefault);
        Assert.Equal(new TestOptions(), options.WithDebug("DebugDriver", true).WithDebug("DebugDriver", false));
    }

    [Fact]
    public void Effective_TurnsOnLogsForDebugSwitches()
    {
        var choice = new Choice { Wasd = true };
        Assert.Same(choice, new TestOptions { StartLevel = 2 }.Effective(choice));
        Assert.Equal(choice with { DebugLogs = true }, new TestOptions().WithDebug("DebugDriver", true).Effective(choice));
        var logs = choice with { DebugLogs = true };
        Assert.Same(logs, new TestOptions().WithDebug("DebugLoading", true).Effective(logs));

        // De console hangt aan de log van de engine, niet aan de logbestanden
        Assert.Same(choice, new TestOptions { StartLevel = 1, SecretCommands = true, LoadConsole = true }.Effective(choice));
    }

    [Fact]
    public void KeysClashWith_WasdOrModernController_OnlyWhenTheKeysAreReallyOn()
    {
        var keys = new TestOptions { StartLevel = 2, SecretCommands = true };
        Assert.True(keys.KeysClashWith(new Choice { Wasd = true }));
        Assert.True(keys.KeysClashWith(new Choice { Controller = true }));
        Assert.False(keys.KeysClashWith(new Choice()));
        Assert.False((keys with { StartLevel = null }).KeysClashWith(new Choice { Wasd = true }));
        Assert.False((keys with { SecretCommands = false }).KeysClashWith(new Choice { Wasd = true, Controller = true }));
    }

    /// <summary>
    /// AdamWin.ini geldt ook voor gewoon spelen: debugtoetsen en laadlog staan alleen aan bij een teststart, en Play
    /// zonder startlevel zet ze terug, ook als ze met de hand aangezet waren.
    /// </summary>
    [Fact]
    public void Apply_DebugKeysAndLoadLog_OnlyForATestStart()
    {
        var game = MakeGame();
        var path = Path.Combine(game, TestOptions.AdamWin);
        var test = new TestOptions { StartLevel = 3, SecretCommands = true, LoadConsole = true };

        test.Apply(game);
        Assert.Equal("1", IniFile.Get(path, "Debug", TestOptions.SecretCommandsKey));
        Assert.Equal("1", IniFile.Get(path, "Debug", TestOptions.UseConsoleKey));

        // Zelfde schakelaars, maar Play zonder startlevel
        (test with { StartLevel = null }).Apply(game);
        Assert.Equal(AdamWin, Read(game));

        IniFile.Set(path, "Debug", TestOptions.SecretCommandsKey, "1");
        new TestOptions().Apply(game);
        Assert.Equal(AdamWin, Read(game));
    }

    [Fact]
    public void Apply_FromAnyPreviousOptions_EqualsFreshApply()
    {
        var options = AllOptions().ToList();
        var fresh = options.Select((o, i) =>
        {
            var dir = MakeGame($"fresh{i}");
            o.Apply(dir);
            return Read(dir);
        }).ToList();

        var game = MakeGame();
        foreach (var i in Enumerable.Range(0, options.Count).Concat(Enumerable.Range(0, options.Count).Reverse()).Concat([7, 40, 0, 47, 21]))
        {
            options[i].Apply(game);
            options[i].Apply(game);   // nog een keer: verandert niets
            Assert.Equal(fresh[i], Read(game));
        }

        new TestOptions().Apply(game);
        Assert.Equal(AdamWin, Read(game));
    }

    [Fact]
    public void Apply_SetsOnlyTheSwitchesInTheDebugSection()
    {
        var game = MakeGame();
        var path = Path.Combine(game, TestOptions.AdamWin);
        IniFile.Set(path, "Debug", "DebugFakeDriverCrash", "1");   // met de hand gezet: blijft staan

        var on = TestOptions.DebugKeys.Aggregate(new TestOptions { StartLevel = 1, SecretCommands = true, LoadConsole = true }, (o, k) => o.WithDebug(k, true));
        on.Apply(game);
        Assert.All([.. TestOptions.DebugKeys, TestOptions.SecretCommandsKey, TestOptions.UseConsoleKey], k => Assert.Equal("1", IniFile.Get(path, "Debug", k)));
        Assert.Equal("1", IniFile.Get(path, "Debug", "DebugFakeDriverCrash"));
        Assert.Equal("3", IniFile.Get(path, "Debug", "DebugFileNumber"));
        Assert.Equal("0", IniFile.Get(path, "Sound", "DebugDriver"));

        new TestOptions().Apply(game);
        Assert.Equal(AdamWin.Replace("DebugFakeDriverCrash=0", "DebugFakeDriverCrash=1"), Read(game));
    }

    [Fact]
    public void Apply_Off_RestoresTheBasisValue()
    {
        var game = MakeGame();
        File.WriteAllText(Path.Combine(Basis.DirOf(game), TestOptions.AdamWin), AdamWin.Replace("DebugLoading=0", "DebugLoading=1"));
        new TestOptions().Apply(game);
        Assert.Equal("1", IniFile.Get(Path.Combine(game, TestOptions.AdamWin), "Debug", "DebugLoading"));

        // Zonder basis: 0, zoals op de cd-rom
        Directory.Delete(Basis.DirOf(game), recursive: true);
        new TestOptions().Apply(game);
        Assert.Equal(AdamWin, Read(game));
    }

    [Fact]
    public void Apply_MissingKey_OnlyFailsWhenTurnedOn()
    {
        var adamWin = "[Debug]\r\nDebugDriver=0\r\n";
        var game = MakeGame(adamWin: adamWin);

        new TestOptions().Apply(game);
        new TestOptions().WithDebug("DebugDriver", true).Apply(game);
        Assert.Equal("[Debug]\r\nDebugDriver=1\r\n", Read(game));
        Assert.Throws<PatchException>(() => new TestOptions().WithDebug("DebugLoading", true).Apply(game));
        Assert.Throws<PatchException>(() => new TestOptions { StartLevel = 1, SecretCommands = true }.Apply(game));
        Assert.Equal("[Debug]\r\nDebugDriver=1\r\n", Read(game));   // ook DebugDriver niet teruggezet

        File.Delete(Path.Combine(game, TestOptions.AdamWin));
        new TestOptions().Apply(game);   // geen AdamWin.ini en niets aan: niets te doen
    }
}

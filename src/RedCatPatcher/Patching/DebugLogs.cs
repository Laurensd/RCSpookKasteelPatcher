namespace RedCatPatcher.Patching;

/// <summary>
/// Spel en menu schrijven bij elke start logbestanden in de spelmap. Geen INI-instelling schakelt dat uit
/// (de [Debug]-sleutels in AdamWin.ini voegen alleen meldingen toe), dus de patcher zet de schrijvers zelf uit.
/// Een echte foutmelding komt nog wel in AdamError*.log en rchcmenu.log. Met --debug-logs blijft alles aan.
/// Achtergrond: zie docs/TECHNISCH.md, "Logbestanden".
/// </summary>
public static class DebugLogs
{
    // RcHcGame.dat. De Win98-update is dezelfde code, maar de globale variabelen en strings staan op andere
    // adressen, dus elke versie heeft eigen patronen.
    //  - debug:  de schrijver van AdamDebug%02d.log (callback van de engine-log) begint met ret.
    //  - header: de regel "Start run ..." die bij elke start naar AdamError%02d.log gaat, wordt overgeslagen
    //            (call → 5× NOP; de aanroeper ruimt de stack zelf op).
    //  - sound:  de schrijver van Sound.log begint met ret.
    //  - script: de scriptcompiler opent per level Levels\<level>.log; na het sluiten van het vorige log springt
    //            de functie direct naar zijn uitgang, zodat het ofstream-pointer 0 blijft (alle schrijvers testen daarop).
    private static readonly Replacement[] Game100 =
    [
        new(Hex("56 e8 fa 07 ff ff 8a 0d 05 92 6a 00"), Hex("c3 e8 fa 07 ff ff 8a 0d 05 92 6a 00")),
        new(Hex("50 e8 e3 ef 01 00 8d 54 24 10 52 e8"), Hex("50 90 90 90 90 90 8d 54 24 10 52 e8")),
        new(Hex("a1 9c 9a 6a 00 56 85 c0 74 11"), Hex("c3 9c 9a 6a 00 56 85 c0 74 11")),
        new(Hex("89 1d 80 fb 70 00 68 8c 00 00 00 e8"), Hex("89 1d 80 fb 70 00 e9 3d 01 00 00 e8")),
    ];

    private static readonly Replacement[] Game101 =
    [
        new(Hex("56 e8 ca 08 ff ff 8a 0d 5d 91 6a 00"), Hex("c3 e8 ca 08 ff ff 8a 0d 5d 91 6a 00")),
        new(Hex("50 e8 83 ef 01 00 8d 54 24 10 52 e8"), Hex("50 90 90 90 90 90 8d 54 24 10 52 e8")),
        new(Hex("a1 f4 99 6a 00 56 85 c0 74 11"), Hex("c3 f4 99 6a 00 56 85 c0 74 11")),
        new(Hex("89 1d e8 fa 70 00 68 8c 00 00 00 e8"), Hex("89 1d e8 fa 70 00 e9 3d 01 00 00 e8")),
    ];

    // XP-build: hier staat esi nog op de stack, dus "pop esi" vóór de sprong naar de uitgang.
    private static readonly Replacement[] GameXp =
    [
        new(Hex("56 e8 85 08 d8 ff 8a 0d bd cc 7e 00"), Hex("c3 e8 85 08 d8 ff 8a 0d bd cc 7e 00")),
        new(Hex("50 e8 db ff da ff 8d 4c 24 10 51 e8"), Hex("50 90 90 90 90 90 8d 4c 24 10 51 e8")),
        new(Hex("a1 6c d8 7e 00 56 85 c0 74 11"), Hex("c3 6c d8 7e 00 56 85 c0 74 11")),
        new(Hex("e8 9a 01 00 00 68 8c 00 00 00 e8"), Hex("e8 9a 01 00 00 5e e9 0b 01 00 00")),
    ];

    // GlideDrv.dll: de texture- en lightmap-cache worden bij het afsluiten naar GCache.Log geschreven.
    // De aanroep wordt "mov eax,1" (gelukt), zodat de driver gewoon verdergaat.
    private static readonly BinaryPatch Glide = new("no-logs-glide", "GlideDrv.dll",
        "Glide-driver schrijft geen GCache.Log",
        [new(Hex("68 8c e1 00 10 52 e8 6b fd ff ff"), Hex("68 8c e1 00 10 52 b8 01 00 00 00"))]);

    // RcHcMenu.dat (Delphi):
    //  - de tracer (MenuTrac.log) wordt aangemaakt met Enabled = False; alle schrijfmethoden testen die vlag.
    //  - de exception-logger (rchcmenu.log) schrijft geen "Exception logging started/ended" meer bij elke start;
    //    een echte fout wordt nog wel gelogd.
    private static readonly BinaryPatch Menu = new("no-logs-menu", "RcHcMenu.dat",
        "Menu schrijft geen MenuTrac.log en rchcmenu.log (behalve bij een fout)",
        [new(Hex("8b 45 fc c6 40 0c 01 e8"), Hex("8b 45 fc c6 40 0c 00 e8")),
         new(Hex("84 c0 74 15 68 28 cd 56 00"), Hex("84 c0 74 15 eb 2a cd 56 00")),
         new(Hex("84 c0 74 13 68 c4 cd 56 00"), Hex("84 c0 eb 13 68 c4 cd 56 00"))]);

    /// <summary>De patches voor deze versie; ze worden toegepast met <see cref="GameVersion.Build"/>.</summary>
    public static IReadOnlyList<BinaryPatch> PatchesFor(GameVersion version) =>
    [
        new("no-logs-game", "RcHcGame.dat",
            "Spel schrijft geen AdamDebug*.log, Sound.log en scriptlogs (AdamError*.log alleen bij een fout)",
            version == GameVersion.V101 ? Game101 : Game100, Xp2004: GameXp),
        Glide,
        Menu,
    ];

    /// <summary>Logbestanden die spel, menu en drivers in de spelmap (en Levels) achterlaten.</summary>
    private static readonly string[] RootPatterns =
    [
        "AdamDebug*.log", "AdamError*.log", "Sound.log", "GCache.Log", "D3DDrv.Log", "D3DCache.Log", "zrender.log",
        "Gedit.Log", "MenuTrac.log", "rchcmenu.log",
    ];

    /// <summary>Verwijdert oude logbestanden uit een bestaande installatie en geeft het aantal terug.</summary>
    public static int Clean(string gameDir)
    {
        if (!Directory.Exists(gameDir)) return 0;
        // Hoofdletterongevoelig, ook op Linux: onder Proton bepaalt het spel zelf de schrijfwijze.
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var files = RootPatterns.SelectMany(p => Directory.EnumerateFiles(gameDir, p, options)).ToList();
        var levels = Path.Combine(gameDir, "Levels");
        if (Directory.Exists(levels)) files.AddRange(Directory.EnumerateFiles(levels, "*.log", options));

        foreach (var f in files) File.Delete(f);
        return files.Count;
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));
}

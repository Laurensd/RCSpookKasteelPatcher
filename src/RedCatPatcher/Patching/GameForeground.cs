using System.Buffers.Binary;
using System.Text;

namespace RedCatPatcher.Patching;

/// <summary>
/// RedCat.exe ("motor") start het menu en het spel met <c>_spawnl(P_WAIT, ...)</c> en verwerkt intussen geen berichten.
/// Het zwarte MOTOR-venster hangt dan, en het spelvenster krijgt de voorgrond niet als je tussen het sluiten van het
/// menu en het verschijnen van het spel klikt of een toets indrukt (bijv. een dubbelklik op Start!).
/// Fix: de <c>WaitForSingleObject(proces, INFINITE)</c> in <c>_spawn</c> wordt een aanroep naar een eigen wachtlus in de
/// vrije ruimte achter de code. Die wacht in stappen van 50 ms, verwerkt de berichten van het MOTOR-venster en haalt
/// het spelvenster (klasse "RedCat") naar voren zodra het zichtbaar is. Analyse: docs/TECHNISCH.md, "Spel komt niet
/// naar voren". Bron van de wachtlus: GameForeground.asm.
/// RedCat.exe is in alle versies (cd-rom, Win98- en XP-update) hetzelfde bestand.
/// </summary>
public static class GameForeground
{
    /// <summary>Adres van de wachtlus: direct na de laatste functie van .text (die eindigt op 0x405917).</summary>
    public const int CodeAddress = 0x405920;
    /// <summary>Adres van de strings die de wachtlus gebruikt.</summary>
    public const int StringsAddress = 0x405B20;
    /// <summary>Begin van de volgende sectie (.rdata); de wachtlus moet daarvoor ophouden.</summary>
    public const int TextSectionEnd = 0x406000;
    /// <summary>De <c>call [WaitForSingleObject]</c> in <c>_spawn</c>.</summary>
    public const int CallAddress = 0x4055DB;

    /// <summary>Volgorde bepaalt de adressen (S_* in GameForeground.asm), dus niet aanpassen zonder de code opnieuw te bouwen.</summary>
    public static readonly string[] Strings =
    [
        "user32.dll", "kernel32.dll", "RedCat", "FindWindowA", "GetForegroundWindow", "SetForegroundWindow",
        "IsWindowVisible", "GetWindowThreadProcessId", "AttachThreadInput", "GetCurrentThreadId", "keybd_event",
    ];

    /// <summary>De gebouwde wachtlus (.text van GameForeground.asm, ml /coff), te laden op <see cref="CodeAddress"/>.</summary>
    public static readonly byte[] Code = Hex(
        "55 8b ec 83 ec 4c 53 56 57 c7 45 c0 28 00 00 00 c7 45 bc 00 00 00 00 68 20 5b 40 00 ff 15 60 60" +
        "40 00 8b d8 68 3f 5b 40 00 53 ff 15 50 60 40 00 89 45 dc 85 c0 75 07 c7 45 bc 01 00 00 00 68 4b" +
        "5b 40 00 53 ff 15 50 60 40 00 89 45 d8 85 c0 75 07 c7 45 bc 01 00 00 00 68 5f 5b 40 00 53 ff 15" +
        "50 60 40 00 89 45 d4 85 c0 75 07 c7 45 bc 01 00 00 00 68 73 5b 40 00 53 ff 15 50 60 40 00 89 45" +
        "d0 85 c0 75 07 c7 45 bc 01 00 00 00 68 83 5b 40 00 53 ff 15 50 60 40 00 89 45 cc 85 c0 75 07 c7" +
        "45 bc 01 00 00 00 68 9c 5b 40 00 53 ff 15 50 60 40 00 89 45 c8 85 c0 75 07 c7 45 bc 01 00 00 00" +
        "68 c1 5b 40 00 53 ff 15 50 60 40 00 89 45 b8 85 c0 75 07 c7 45 bc 01 00 00 00 68 2b 5b 40 00 ff" +
        "15 60 60 40 00 8b d8 68 ae 5b 40 00 53 ff 15 50 60 40 00 89 45 c4 85 c0 75 07 c7 45 bc 01 00 00" +
        "00 6a 32 ff 75 08 ff 15 14 60 40 00 3d 02 01 00 00 0f 85 cd 00 00 00 6a 01 6a 00 6a 00 6a 00 8d" +
        "45 e0 50 ff 15 10 61 40 00 85 c0 74 27 83 7d e4 12 75 0b ff 75 e8 ff 15 dc 60 40 00 eb 16 8d 45" +
        "e0 50 ff 15 08 61 40 00 8d 45 e0 50 ff 15 d4 60 40 00 eb c3 83 7d bc 00 75 a7 6a 00 68 38 5b 40" +
        "00 ff 55 dc 85 c0 74 99 8b f0 56 ff 55 d0 85 c0 74 8f ff 55 d8 3b c6 74 5f ff 4d c0 78 5a 83 7d" +
        "c0 1e 75 18 50 6a 00 6a 00 6a 00 6a 12 ff 55 b8 6a 00 6a 02 6a 00 6a 12 ff 55 b8 58 6a 00 50 ff" +
        "55 cc 8b f8 ff 55 c4 8b d8 85 ff 74 0b 3b fb 74 07 6a 01 57 53 ff 55 c8 56 ff 55 d4 85 ff 0f 84" +
        "3d ff ff ff 3b fb 0f 84 35 ff ff ff 6a 00 57 53 ff 55 c8 e9 29 ff ff ff c7 45 bc 01 00 00 00 e9" +
        "1d ff ff ff 5f 5e 5b c9 c2 08 00");

    /// <summary>Adres van elke string, in de volgorde van <see cref="Strings"/>.</summary>
    public static IReadOnlyDictionary<string, int> StringAddresses
    {
        get
        {
            var result = new Dictionary<string, int>();
            int at = StringsAddress;
            foreach (var s in Strings) { result[s] = at; at += s.Length + 1; }
            return result;
        }
    }

    /// <summary>Wachtlus + opvulling + strings, van <see cref="CodeAddress"/> tot het einde van de strings.</summary>
    public static byte[] Cave =>
        [.. Code, .. new byte[StringsAddress - CodeAddress - Code.Length], .. Strings.SelectMany(s => Encoding.ASCII.GetBytes(s + "\0"))];

    // Laatste bytes van de laatste functie in .text (0x4058F0: cld; pop edi; leave; ret), dan nullen tot CodeAddress.
    private static readonly byte[] TextEnd = Hex("fc 5f c9 c3");
    private const int TextEndAddress = 0x405917;

    public static BinaryPatch Patch
    {
        get
        {
            var cave = Cave;
            int padding = CodeAddress - TextEndAddress;
            int rel = CodeAddress - (CallAddress + 5);
            return new("game-foreground", "RedCat.exe",
                "Spel komt na het menu op de voorgrond (geen alt-tab nodig)",
                [
                    // .text VirtualSize: 0x4917 -> tot het einde van de strings
                    new([.. ".text\0\0\0"u8, .. Int(TextEndAddress - 0x401000), .. Hex("00 10 00 00")],
                        [.. ".text\0\0\0"u8, .. Int(CodeAddress + cave.Length - 0x401000), .. Hex("00 10 00 00")]),
                    // push -1; push [ebp-1Ch]; call [WaitForSingleObject] -> call wachtlus; nop
                    new(Hex("6a ff ff 75 e4 ff 15 14 60 40 00 8d 45 10"),
                        [.. Hex("6a ff ff 75 e4 e8"), .. Int(rel), .. Hex("90 8d 45 10")]),
                    new([.. TextEnd, .. new byte[padding + cave.Length]],
                        [.. TextEnd, .. new byte[padding], .. cave]),
                ]);
        }
    }

    private static byte[] Int(int value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        return b;
    }

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));
}
